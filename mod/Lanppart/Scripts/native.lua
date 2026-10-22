-- Bridge to lanppart_native.dll (listen server, Iris and Finales patches).
-- UE4SS doesn't export the Lua C API, so each native entry point takes no arguments.
local Native = {}

local function ModDir()
    local source = debug.getinfo(1, "S").source:gsub("^@", ""):gsub("\\", "/")
    return source:match("^(.*)/Scripts/[^/]+$")
end

local dllPath = ModDir() .. "/lanppart_native.dll"
local entryPoints = {}

local function Call(name)
    local fn = entryPoints[name]
    if not fn then
        local err
        fn, err = package.loadlib(dllPath, name)
        if not fn then
            print(string.format("[Lanppart] cannot load %s from %s: %s\n", name, dllPath, tostring(err)))
            return false
        end
        entryPoints[name] = fn
    end
    fn()
    return true
end

function Native.Install() return Call("lanppart_install") end
function Native.EnableIris() return Call("lanppart_enable_iris") end
function Native.DisableIris() return Call("lanppart_disable_iris") end
-- Server: make sure Iris knows which connection owns each remote player's actors (RPC rights).
function Native.RefreshOwnership() return Call("lanppart_refresh_ownership") end
-- Server: game state upkeep (K.O. roulette survivors).
function Native.ServerTick() return Call("lanppart_server_tick") end
-- Dedicated server: sessions wait for ServerStart, which adds the requested bots and starts the intro.
function Native.ServerModeOn() return Call("lanppart_server_mode_on") end
function Native.ServerStart() return Call("lanppart_server_start") end
-- Writes the game's text list (see Texts.DumpCatalog).
function Native.DumpTexts() return Call("lanppart_dump_texts") end
-- Lives instead of the roulette for the sessions hosted from now on.
function Native.SetLives(on) return Call(on and "lanppart_lives_on" or "lanppart_lives_off") end

return Native
