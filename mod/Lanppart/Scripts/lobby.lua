-- Steam lobby integration: with the mod, every lobby game is hosted by the lobby host (whatever its mode)
-- and the other members join it. (lanppart_native.dll keeps the game from starting AWS matchmaking.)
local Config = require("config")
local Host = require("host")

local Lobby = {}

local GI_STATE_MAINMENU = 2
-- Impossible pause duration, set in the lobby options by the host once its server waits for players.
-- (Lobby options are only read in menus: in game, sessions use the options replicated by the server.)
local READY_MARKER = -777
local PORT = 7777
local MODE_NAMES = { [0] = "Battle Royale", [1] = "K.O.", [2] = "Duel" }

local launched = false     -- this lobby launch was handled
local markerPending = false
local matchmakingShown = false  -- hidden at launch: hide again once in game
local matchmakingHidden = false

local function Subsystem()
    local lobby = FindFirstOf("PGSteamLobbySubsystem")
    if lobby and lobby:IsValid() then return lobby end
end

local function HasMarker(opts)
    return opts.PauseDuration == READY_MARKER
end

-- Edits the lobby's own options struct in place: rebuilding it from Lua would lose its text ids
-- (UE4SS can't convert a Lua table into a TArray inside a struct).
local function SetMarker(lobby, present)
    local opts = lobby.GameplayOptions
    if present then
        opts.PauseDuration = READY_MARKER
    else
        local saved = {}
        FindFirstOf("PGSaveGame"):GetGameplayOptionsForMode(opts.TypeMode, saved)
        opts.PauseDuration = saved.PauseDuration or 0
    end
    lobby:UpdateGameplayOptions(opts)
end

local function HostAddress(lobby)
    if Config.HostAddress then return Config.HostAddress .. ":" .. PORT end
    return "steam." .. lobby:GetLobbyHostId():ToString() .. ":" .. PORT
end

local function IsHost(lobby)
    return lobby:IsMemberHost(lobby:GetLocalMemberId():ToString())
end

-- The game shows its "matchmaking" screen (mode, rules, timer) when the lobby launches and only hides
-- it when the official servers answer: hide it ourselves.
local function HideMatchmaking()
    local hud = FindFirstOf("wbp_hud_persistent_C")
    if not (hud and hud:IsValid()) then return end
    hud:StopMatchmakingTimer()
    hud:HandleHideMatchmakingVisual()
end

-- Members in the lobby and its size, as the lobby window shows them ("2/8").
local function MemberCounts()
    local window = FindFirstOf("wbp_mainmenu_window_lobby_inlobby_C")
    if not (window and window:IsValid()) then return nil end
    local members, capacity = window.LobbyMemberCounter:GetText():ToString():match("(%d+)%s*/%s*(%d+)")
    return tonumber(members), tonumber(capacity)
end

local function Describe(opts)
    local rules = opts.bPlayRoulette and "roulette" or string.format("%d lives", opts.BulletSlotsNum)
    return string.format("%s, %s", MODE_NAMES[opts.TypeMode] or tostring(opts.TypeMode), rules)
end

-- Host side: tell the members the server is up.
Host.OnServerReady = function()
    if not markerPending then return end
    markerPending = false
    local lobby = Subsystem()
    if lobby and lobby:IsLocalPlayerInLobby() then
        SetMarker(lobby, true)
        print("[Lanppart] lobby told the server is ready\n")
    end
end

-- Called periodically on the game thread.
function Lobby.Tick(gi)
    local lobby = Subsystem()
    if not (lobby and lobby:IsLocalPlayerInLobby()) then
        launched = false
        return
    end
    -- Once more in game, in case the screen came back while the map was loading.
    if gi.State ~= GI_STATE_MAINMENU then
        if matchmakingShown then
            matchmakingShown = false
            HideMatchmaking()
        end
        return
    end
    local opts = lobby.GameplayOptions
    if lobby:IsLobbyInMatchmaking() then
        if not matchmakingHidden then
            matchmakingHidden = true
            matchmakingShown = true
            HideMatchmaking()
        end
    else
        matchmakingHidden = false
    end

    if IsHost(lobby) then
        -- Back from the session: the game ended the lobby's matchmaking state.
        if not lobby:IsLobbyInMatchmaking() then
            launched = false
            if HasMarker(opts) then SetMarker(lobby, false) end
            return
        end
        if not launched then
            launched = true
            markerPending = true
            local members, capacity = MemberCounts()
            print(string.format("[Lanppart] lobby launched: hosting %s, %s/%s players\n", Describe(opts),
                tostring(members), tostring(capacity)))
            if not Host.Start(Host.SessionOptions(opts, members, capacity)) then markerPending = false end
        end
        return
    end

    -- Members follow the host's marker only (it stays set until the host is back in the menu).
    if not HasMarker(opts) then
        launched = false
    elseif not launched then
        launched = true
        print("[Lanppart] lobby launched: joining the host for " .. Describe(opts) .. "\n")
        Host.Join(HostAddress(lobby), not opts.bPlayRoulette)
    end
end

return Lobby
