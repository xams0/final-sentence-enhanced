-- Lanppart3: lobby games hosted by the lobby host, with free rules (roulette or lives in every mode),
-- and dedicated servers (V2) started from the launcher.
--
-- The mod only runs in games started by the Lanppart3 launcher: it injects the mod into them (the game's
-- own folder holds nothing of it) and sets LANPPART3_LAUNCHER (players) or LANPPART3_SERVER (dedicated
-- servers). Started any other way (Steam...), the game is the original one.

-- The launcher's orders for this start (LANPPART3_JOIN=26.1.2.3:7777, LANPPART3_LIVES=1).
local launchOrders = {
    join = os.getenv("LANPPART3_JOIN"),
    lives = os.getenv("LANPPART3_LIVES"),
}
if launchOrders.join == "" then launchOrders.join = nil end

local function StartedByLauncher()
    if os.getenv("LANPPART3_DEV") == "1" then return true end
    local server = os.getenv("LANPPART3_SERVER")
    if server and server ~= "" then return true end
    local launcher = os.getenv("LANPPART3_LAUNCHER")
    return launcher ~= nil and launcher ~= ""
end

if not StartedByLauncher() then
    print("[Lanppart] not started by the Lanppart3 launcher: mod inactive\n")
    return
end

local Config = require("config")
local Native = require("native")
local Host = require("host")
local Server = require("server")
local Texts = require("texts")

local GI_STATE_MAINMENU = 2
local isServer = Server.Init() ~= nil
local Hud, Lobby, Menu, Browser, Pages
if not isServer then
    Hud = require("hud")
    Pages = require("pages")
    Lobby = require("lobby")
    Menu = require("menu")
    Browser = require("browser")
end

local installed = false
local catalogDone = false
local joinDone = launchOrders.join == nil
local lastGiState = nil

local function OnGameThreadTick()
    local gi = FindFirstOf("PGGameInstance")
    if not (gi and gi:IsValid()) then return end
    if not installed then
        installed = Native.Install()
        Native.DisableIris()
    end
    -- Official servers use generic replication: leave Iris on only while in a Lanppart session.
    if gi.State == GI_STATE_MAINMENU and lastGiState ~= GI_STATE_MAINMENU then
        Native.DisableIris()
        Native.SetLives(false)
        Host.LivesMode = false
        Host.OnDedicatedServer = false
    end
    lastGiState = gi.State
    -- The launcher's text list, once the texts are loaded (servers: see Server.Tick).
    if not catalogDone and gi.State == GI_STATE_MAINMENU and not isServer then catalogDone = Texts.DumpCatalog() end
    if isServer then
        Server.Tick(gi)
        Host.Tick()
        return
    end
    -- Server picked in the launcher's server list: join it once in the main menu.
    if not joinDone and gi.State == GI_STATE_MAINMENU then
        joinDone = true
        Host.Join(launchOrders.join, launchOrders.lives == "1", true)
    end
    Menu.Tick()
    Browser.Tick()
    Lobby.Tick(gi)
    Host.Tick()
    Hud.Tick()
    Pages.ClientTick()
end

LoopAsync(500, function()
    ExecuteInGameThread(function()
        local ok, err = pcall(OnGameThreadTick)
        if not ok then print("[Lanppart] " .. tostring(err) .. "\n") end
    end)
    return false
end)

if not isServer then
    -- Test shortcuts without a lobby. Key binds are machine-wide and can repeat, so ignore presses
    -- closer than a few seconds apart.
    local lastShortcut = 0
    local function Shortcut(action)
        return function()
            if os.time() - lastShortcut < 3 then return end
            lastShortcut = os.time()
            ExecuteInGameThread(action)
        end
    end
    RegisterKeyBind(Key.F8, { ModifierKey.CONTROL }, Shortcut(Host.StartFinales))
    RegisterKeyBind(Key.F9, { ModifierKey.CONTROL }, Shortcut(function() Host.Join(Config.TestJoinAddress) end))
end

print("[Lanppart] loaded" .. (isServer and " (dedicated server)" or "") .. "\n")
