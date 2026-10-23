-- Hosting a Lanppart session on the local game (listen server).
local Config = require("config")
local Native = require("native")
local Texts = require("texts")
local Presets = require("presets")
local Pages = require("pages")

local Host = {}

local TYPEMODE_BATTLEROYAL = 0
local TYPEMODE_KNOCKDOWN = 1
local TYPEMODE_DUEL = 2
local SESSION_WAITING_FOR_PLAYERS = 2
local MAPS = {
    [TYPEMODE_BATTLEROYAL] = "/Game/TheEnd/maps/game/lvlg_game_battleroyale",
    [TYPEMODE_KNOCKDOWN] = "/Game/TheEnd/maps/game/lvlg_game_knockdown",
    [TYPEMODE_DUEL] = "/Game/TheEnd/maps/game/lvlg_game_duel",
}

local injectedFor = nil   -- address of the game mode we already gave a text to
Host.OnServerReady = nil  -- called once a hosted session has its text and waits for players
Host.LivesMode = false    -- the current session (hosted or joined) plays with lives
Host.OnDedicatedServer = false  -- joined a dedicated server (it waits for its organizer to start)
Host.MoreTextIds = nil    -- server.lua: function giving the text pool (for the next pages)

-- This game's local player controller, or nil (a dedicated server has none; bots have controllers too).
local function LocalController()
    for _, pc in ipairs(FindAllOf("PlayerController") or {}) do
        if pc:IsValid() and pc:IsLocalController() then return pc end
    end
end

-- (Without a local player, the game instance gives the command its world.)
local function Console(cmd)
    local pc = LocalController()
    local context = pc or FindFirstOf("PGGameInstance")
    StaticFindObject("/Script/Engine.Default__KismetSystemLibrary"):ExecuteConsoleCommand(context, cmd, pc)
end
Host.Console = Console

-- Travels to "map?options" (console "open" needs a local player, OpenLevel doesn't).
local function Open(url)
    local map, options = url:match("^([^?]*)%?(.*)$")
    if LocalController() then
        Console("open " .. url)
    else
        -- (The game instance's own world: FindFirstOf("World") may find a streaming level's world.)
        local world = FindFirstOf("PGGameInstance"):GetWorld()
        StaticFindObject("/Script/Engine.Default__GameplayStatics"):OpenLevel(world, FName(map or url), true,
            options or "")
    end
end
Host.Open = Open

local function B(v) return v and 1 or 0 end

-- Session options, in the "?Key=Value" form the game mode parses.
local function OptionsUrl(o)
    local kv = {
        { "IsPrivate", 1 }, { "TypeMode", o.TypeMode }, { "Language", o.Language },
        { "BotsCount", o.BotsCount }, { "BotsDifficulty", o.BotsDifficulty },
        { "MistakeBehaviour", o.MistakeBehaviour }, { "CapitalsMatter", B(o.bCapitalsMatter) },
        { "PlayRoulette", B(o.bPlayRoulette) }, { "MaxMistakesPerRound", o.MaxMistakesPerRound },
        { "BulletSlotsNum", o.BulletSlotsNum }, { "InitialBulletsNum", o.InitialBulletsNum },
        { "RoundDuration", o.RoundDuration }, { "PauseDuration", o.PauseDuration },
        { "TextIds", o.TextId },
    }
    local parts = {}
    for _, p in ipairs(kv) do parts[#parts + 1] = p[1] .. "=" .. tostring(p[2]) end
    return "?" .. table.concat(parts, "?")
end

-- Plain Lua copy of an FPGGameplayOptions (UE struct or out-param table), text ids as a Lua array.
function Host.CopyOptions(src)
    local ids = {}
    local textIds = src.TextIds
    if type(textIds) == "table" then
        for i = 1, #textIds do
            local v = textIds[i]
            ids[#ids + 1] = type(v) == "number" and v or v:get()
        end
    elseif textIds then
        textIds:ForEach(function(_, v) ids[#ids + 1] = v:get() end)
    end
    local lang = src.Language
    if type(lang) ~= "string" then lang = lang:ToString() end
    return {
        bIsPrivate = src.bIsPrivate, TypeMode = src.TypeMode, Language = lang, TextIds = ids,
        BotsCount = src.BotsCount, BotsDifficulty = src.BotsDifficulty, MistakeBehaviour = src.MistakeBehaviour,
        bCapitalsMatter = src.bCapitalsMatter, bPlayRoulette = src.bPlayRoulette,
        MaxMistakesPerRound = src.MaxMistakesPerRound, BulletSlotsNum = src.BulletSlotsNum,
        InitialBulletsNum = src.InitialBulletsNum, RoundDuration = src.RoundDuration,
        PauseDuration = src.PauseDuration,
    }
end

-- Session options for lobby options (any mode). With the roulette unchecked, the lobby's revolver
-- chambers are the players' lives: the session then runs with the roulette ON so that every roulette
-- trigger of the game fires, and lanppart_native.dll turns each one into a lost life.
-- Automatic bot counts (-1, Duel / K.O.) fill the lobby's free places: the game would otherwise count
-- only the host, then give the members joining afterwards no room.
-- Returns the options and whether the session plays with lives.
function Host.SessionOptions(lobbyOptions, members, capacity)
    local o = Host.CopyOptions(lobbyOptions)
    local preset, presetBots = Presets.FromMarker(o.BotsCount, o.TypeMode)
    if preset then o.BotsCount = presetBots end  -- (-1: fill the free places, below)
    if o.BotsCount < 0 and members and capacity then o.BotsCount = math.max(0, capacity - members) end
    local lives = not o.bPlayRoulette
    if lives then
        o.bPlayRoulette = true
        o.BulletSlotsNum = math.max(1, math.min(6, math.floor(o.BulletSlotsNum)))
        o.InitialBulletsNum = 0
    end
    return o, lives
end

-- Ctrl+F8 shortcut: a duel with lives, no bots.
function Host.FinalesOptions(base, lives)
    local o = Host.CopyOptions(base)
    o.TypeMode = TYPEMODE_DUEL
    o.BotsCount = 0
    o.bPlayRoulette = false
    o.BulletSlotsNum = lives
    o.MaxMistakesPerRound = -1
    o.PauseDuration = -1
    return Host.SessionOptions(o)
end

-- Hosts a session with options from Host.SessionOptions.
function Host.Start(o, lives)
    local ids = {}
    for _, id in ipairs(o.TextIds) do
        if id > 0 then ids[#ids + 1] = id end
    end
    if #ids == 0 then
        local _, saved = Texts.SavedSelection(o.TypeMode)
        ids = saved
    end
    if #ids == 0 then
        print("[Lanppart] no text selected for this mode\n")
        return false
    end
    o.TextId = ids[math.random(#ids)]
    Host.LivesMode = lives and true or false
    Native.SetLives(Host.LivesMode)
    Native.EnableIris()
    -- Iris' actor bridge reads the driver's world while the host tears the map down; with this off
    -- it never does (all our replicated actors live in the persistent level anyway).
    Console("net.SerializeNewActorOverrideLevel 0")
    -- With an IP address configured, listen on IP; otherwise on Steam P2P only.
    local url = MAPS[o.TypeMode] .. "?listen" .. (Config.HostAddress and "?bIsLanMatch" or "") .. OptionsUrl(o)
    print("[Lanppart] hosting: " .. url .. "\n")
    Open(url)
    return true
end

-- Test shortcut: Finales from the player's saved Duel lobby settings.
function Host.StartFinales()
    local saved = {}
    FindFirstOf("PGSaveGame"):GetGameplayOptionsForMode(TYPEMODE_DUEL, saved)
    return Host.Start(Host.FinalesOptions(saved, Config.FinalesLives))
end

-- dedicated: a Lanppart3 dedicated server (server list, launcher), not a lobby host.
function Host.Join(address, lives, dedicated)
    Host.LivesMode = lives and true or false
    Host.OnDedicatedServer = dedicated and true or false
    Native.EnableIris()
    print("[Lanppart] joining " .. address .. "\n")
    Console("open " .. address)
end

-- The game mode asks the backend for the text, which the client build never does: provide it.
local function ProvideText(gm, gs)
    local key = gm:GetAddress()
    if injectedFor == key or gs.SessionState ~= SESSION_WAITING_FOR_PLAYERS then return end
    -- The writers' seats come with a sub-level streamed in after the map: placing the players before
    -- (which the text triggers) crashes. A dedicated server, without rendering, loads fast enough to get there.
    local start = gm.PlayerStart
    if not (start and start:IsValid()) then return end
    if gs.OriginalText:ToString() ~= "" then return end
    local opts = gs.GameplayOptions
    local lang = opts.Language:ToString()
    local ids = {}
    opts.TextIds:ForEach(function(_, v) ids[#ids + 1] = v:get() end)
    local id = ids[math.random(math.max(#ids, 1))] or Texts.RandomId(opts.TypeMode)
    local text = id and Texts.Get(lang, id)
    if not text then
        print("[Lanppart] could not find text " .. tostring(id) .. " (" .. lang .. ")\n")
        return
    end
    text = Pages.Start(text, lang, opts, Host.LivesMode)
    injectedFor = key
    gm:OnGetRandomText({ bSuccessful = true, ErrCode = 200, ErrMsg = "" },
        { Text = text.Text, text_id = text.Id, text_difficulty = text.Difficulty })
    print(string.format("[Lanppart] text %d provided (%s)\n", text.Id, lang))
    if Host.OnServerReady then Host.OnServerReady() end
end

-- The texts the next pages are taken from: the dedicated server's pool, or the lobby host's selection.
local function TextPool(typeMode)
    if Host.MoreTextIds then return Host.MoreTextIds() end
    return select(2, Texts.SavedSelection(typeMode))
end

-- Called periodically on the game thread.
function Host.Tick()
    local gm = FindFirstOf("PGGameplayGameMode")
    if not (gm and gm:IsValid()) then return end
    local gs = FindFirstOf("PGGameplayGameState")
    if not (gs and gs:IsValid()) then return end
    Native.RefreshOwnership()
    Native.ServerTick()
    ProvideText(gm, gs)
    Pages.ServerTick(gm, gs, TextPool)
end

return Host
