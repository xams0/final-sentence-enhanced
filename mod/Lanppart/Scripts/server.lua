-- Dedicated server (V2): a game instance started by the launcher (LANPPART3_SERVER = its folder) that
-- hosts sessions without playing. Its settings come from <folder>/session.lua (written by the
-- launcher), the organizer's orders from <folder>/command.txt, and it reports in <folder>/status.lua.
local Host = require("host")
local Native = require("native")
local Texts = require("texts")
local Pages = require("pages")

local Server = {}

local GI_STATE_MAINMENU = 2
local SESSION_WAITING_FOR_PLAYERS, SESSION_TERMINATED = 2, 7
local STARTUP_COMPLETED = 10              -- EPGStartupState
local CAPACITY = { [0] = 40, [1] = 8, [2] = 2 }  -- writers per mode
local RESTART_DELAY = 5                         -- seconds between a session's end and the next one

local dir = nil
local settings = nil
local phase = "boot"      -- boot, ready, hosting, waiting, playing, ended
local phaseSince = 0
local sessionNumber = 0
local textQueue = {}
local lastStatus = nil

local function Log(msg) print("[Lanppart] server: " .. msg .. "\n") end

local function SetPhase(p)
    if phase ~= p then Log("phase " .. p) end
    phase = p
    phaseSince = os.time()
end

function Server.Dir() return dir end

local function LoadSettings()
    local chunk, err = loadfile(dir .. "/session.lua")
    if not chunk then
        Log("no settings: " .. tostring(err))
        return false
    end
    local ok, s = pcall(chunk)
    if not ok or type(s) ~= "table" then
        Log("invalid settings: " .. tostring(s))
        return false
    end
    settings = s
    Texts.SetCustom(s.Texts)
    textQueue = {}
    return true
end

-- The configured texts (ids): the game texts and the custom texts the launcher wrote (only the chosen ones).
local function Pool()
    local pool = {}
    for _, id in ipairs(settings.TextIds or {}) do pool[#pool + 1] = id end
    for _, t in ipairs(settings.Texts or {}) do pool[#pool + 1] = t.Id end
    if #pool == 0 then
        local _, saved = Texts.SavedSelection(settings.Mode)
        pool = saved
    end
    return pool
end
-- Long texts for lives (host.lua); a lobby host (no server settings) uses its saved selection.
Host.MoreTextIds = function() return settings and Pool() or nil end

-- Next text id: the configured pool in a random order, without repeating a text before all were played.
local function NextTextId()
    if #textQueue == 0 then
        local pool = Pool()
        for i = #pool, 2, -1 do
            local j = math.random(i)
            pool[i], pool[j] = pool[j], pool[i]
        end
        textQueue = pool
    end
    return table.remove(textQueue)
end

local function SessionOptions()
    local s = settings
    local saved = {}
    FindFirstOf("PGSaveGame"):GetGameplayOptionsForMode(s.Mode, saved)
    local o = Host.CopyOptions(saved)
    o.bIsPrivate = true
    o.TypeMode = s.Mode
    o.Language = s.Language or o.Language
    o.TextIds = { NextTextId() }
    o.BotsDifficulty = s.BotsDifficulty or o.BotsDifficulty
    o.MistakeBehaviour = s.MistakeBehaviour or o.MistakeBehaviour
    if s.CapitalsMatter ~= nil then o.bCapitalsMatter = s.CapitalsMatter end
    o.RoundDuration = s.RoundDuration or o.RoundDuration
    o.PauseDuration = s.PauseDuration or o.PauseDuration
    o.MaxMistakesPerRound = s.MaxMistakesPerRound or o.MaxMistakesPerRound
    o.bPlayRoulette = s.Roulette
    o.BulletSlotsNum = s.Roulette and (s.BulletSlots or 6) or (s.Lives or 3)
    o.InitialBulletsNum = s.InitialBullets or 0
    -- Every place is free for players until the start; the bots get the bot names of a full session.
    o.BotsCount = (CAPACITY[s.Mode] or 2) - 1
    return Host.SessionOptions(o)
end

local function OpenSession()
    sessionNumber = sessionNumber + 1
    local o, lives = SessionOptions()
    Log(string.format("opening session %d (mode %d, text %s)", sessionNumber, o.TypeMode, tostring(o.TextIds[1])))
    if Host.Start(o, lives) then SetPhase("hosting") end
end

local function GameMode()
    local gm = FindFirstOf("PGGameplayGameMode")
    if gm and gm:IsValid() then return gm end
end

local function GameState()
    local gs = FindFirstOf("PGGameplayGameState")
    if gs and gs:IsValid() then return gs end
end

local function Humans(gs)
    local list = {}
    gs.PlayerArray:ForEach(function(_, p)
        local ps = p:get()
        if ps:IsValid() and not ps.bIsABot then list[#list + 1] = ps.PlayerNamePrivate:ToString() end
    end)
    return list
end

local function Start(gm, gs)
    local humans = #Humans(gs)
    local capacity = CAPACITY[settings.Mode] or 2
    local bots = settings.Bots
    if bots == nil or bots == "fill" then bots = capacity - humans end
    bots = math.max(0, math.min(bots, capacity - humans, capacity - 1))  -- (bot names: capacity - 1)
    gm.BotToSpawnCount = bots
    Log(string.format("start: %d players, %d bots", humans, bots))
    Native.ServerStart()
    SetPhase("playing")
end

-- One line per order; the file is removed once read.
local function ReadCommand()
    local path = dir .. "/command.txt"
    local f = io.open(path, "r")
    if not f then return nil end
    local cmd = f:read("*l")
    f:close()
    os.remove(path)
    return cmd and cmd:match("^%s*(%S+)")
end

-- key=value lines for the launcher; rewritten when something changes, and every few seconds as a heartbeat.
local function WriteStatus(gs)
    local humans = gs and Humans(gs) or {}
    local playingId, page, pages = Pages.Playing()
    if phase ~= "playing" and phase ~= "waiting" then playingId = nil end
    local status = table.concat({
        "phase=" .. phase,
        "session=" .. sessionNumber,
        "mode=" .. (settings and settings.Mode or -1),
        "players=" .. table.concat(humans, "\t"),
        "count=" .. #humans,
        "capacity=" .. (settings and CAPACITY[settings.Mode] or 0),
        "state=" .. (gs and gs.SessionState or -1),
        "text=" .. (playingId or ""),
        "page=" .. (playingId and (page .. "/" .. pages) or ""),
    }, "\n")
    if status == lastStatus and os.time() - (Server.statusTime or 0) < 3 then return end
    lastStatus = status
    Server.statusTime = os.time()
    local f = io.open(dir .. "/status.txt", "w")
    if f then
        f:write(status .. "\ntime=" .. os.time() .. "\n")
        f:close()
    end
end

-- Called periodically on the game thread.
function Server.Tick(gi)
    local gs, gm = GameState(), GameMode()
    local cmd = ReadCommand()
    if cmd == "stop" then
        Log("stop")
        StaticFindObject("/Script/Engine.Default__KismetSystemLibrary"):QuitGame(FindFirstOf("World"), nil, 0, false)
        return
    elseif cmd == "reload" then
        LoadSettings()
        Log("settings reloaded")
    end

    if phase == "boot" then
        -- Without rendering (-nullrhi) the game never leaves its startup screen for the main menu; the
        -- server doesn't need the menu: login and texts are done once the startup state is Completed.
        local startup = FindFirstOf("PGStartupGameState")
        local ready = gi.State == GI_STATE_MAINMENU or
            (startup and startup:IsValid() and startup.CurrentState == STARTUP_COMPLETED)
        if not ready then return end
        Texts.DumpCatalog()
        Native.ServerModeOn()
        -- Console variables need the local player, which the server then drops: it never plays.
        Host.Console("net.SerializeNewActorOverrideLevel 0")
        Host.Console("t.MaxFPS 30")
        local pc = FindFirstOf("PlayerController")
        if pc and pc:IsValid() then StaticFindObject("/Script/Engine.Default__GameplayStatics"):RemovePlayer(pc, true) end
        SetPhase("ready")
    elseif phase == "ready" then
        OpenSession()  -- (on the next tick: travelling in the same tick as dropping the player does nothing)
    elseif phase == "hosting" then
        if gs and gs.SessionState == SESSION_WAITING_FOR_PLAYERS and gm then
            gm.BotToSpawnCount = 0  -- no bot before the start
            SetPhase("waiting")
        elseif os.time() - phaseSince > 20 then
            Log("session did not open, retrying")
            OpenSession()
        end
    elseif phase == "waiting" then
        if cmd == "start" and gs and gm then Start(gm, gs) end
        if cmd == "next" or (cmd == "reload" and gs and #Humans(gs) == 0) then OpenSession() end
    elseif phase == "playing" then
        if not gs or gs.SessionState == SESSION_TERMINATED or gi.State == GI_STATE_MAINMENU then SetPhase("ended") end
        if cmd == "next" then OpenSession() end
    elseif phase == "ended" then
        if os.time() - phaseSince >= RESTART_DELAY then OpenSession() end
    end
    if settings and os.time() - (Server.statusCheck or 0) >= 2 then
        Server.statusCheck = os.time()
        WriteStatus(gs)
    end
end

-- The server folder when this instance is a dedicated server, else nil.
function Server.Init()
    dir = os.getenv("LANPPART3_SERVER")
    if not dir or dir == "" then return nil end
    dir = dir:gsub("\\", "/")
    if not LoadSettings() then return nil end
    -- Players reach dedicated servers by IP (LAN, Radmin VPN): listen on IP, not Steam P2P.
    require("config").HostAddress = settings.Address or "lan"
    Log("instance for " .. dir)
    return dir
end

return Server
