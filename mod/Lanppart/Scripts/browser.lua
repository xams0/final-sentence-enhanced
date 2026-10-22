-- In-game list of the Lanppart3 dedicated servers. With the mod, the main menu's "Rejoindre avec code"
-- entry becomes "Serveurs LAN" and its window only lists the servers heard on the LAN / Radmin VPN, one
-- game-style button each (players always join over LAN or Radmin, never with an Internet lobby code).
-- The list comes from the launcher (it listens to the servers' announcements and writes
-- %APPDATA%\Lanppart3\servers_seen.txt), which stays open while the game runs.
local Host = require("host")

local Browser = {}

local WINDOW = "/Game/TheEnd/ui/interfaces/mainmenu/lobby/wbp_mainmenu_window_lobby_joinbycode.wbp_mainmenu_window_lobby_joinbycode_C"
local BUTTON = "/Game/TheEnd/ui/interfaces/common/buttons/mainmenubutton/wbp_mainmenu_button.wbp_mainmenu_button_C"
local MENU_ENTRY = "Serveurs LAN"
local BUTTON_WIDTH = 900
local LIST_MAX_AGE = 10  -- seconds: an older list means the launcher is closed
local VIS_COLLAPSED = 1

local hooked = false
local list = nil          -- VerticalBox added to the window
local buttons = {}        -- button address -> server { Address, Lives }
local lastKey = nil

local function Valid(o) return o and o:IsValid() end

-- { Address, Name, Mode, Rules, Players, Capacity, Phase, Lives } from the launcher's file.
local function ReadServers()
    local appdata = os.getenv("APPDATA")
    local f = appdata and io.open(appdata .. "\\Lanppart3\\servers_seen.txt", "r")
    if not f then return nil end
    local stamp = tonumber(f:read("*l") or "")
    local servers = {}
    for line in f:lines() do
        local a, n, m, r, p, c, ph, rou = line:match("^([^\t]*)\t([^\t]*)\t([^\t]*)\t([^\t]*)\t([^\t]*)\t([^\t]*)\t([^\t]*)\t([^\t]*)")
        if a then
            servers[#servers + 1] = { Address = a, Name = n, Mode = m, Rules = r, Players = tonumber(p) or 0,
                                      Capacity = tonumber(c) or 0, Phase = ph, Lives = rou == "0" }
        end
    end
    f:close()
    if not stamp or math.abs(os.time() - stamp) > LIST_MAX_AGE then return nil end
    return servers
end

local function PhaseText(phase)
    if phase == "waiting" then return "en attente" end
    if phase == "playing" then return "en jeu" end
    return "démarrage"
end

local function Window()
    for _, w in ipairs(FindAllOf("wbp_mainmenu_window_lobby_joinbycode_C") or {}) do
        if Valid(w) and Valid(w.CodeTextBox) and Valid(w.WidgetTree) then return w end
    end
end

local function MakeButton(window, caption)
    local cls = StaticFindObject(BUTTON)
    local pc = window.PC or FindFirstOf("PlayerController")
    local b = StaticFindObject("/Script/UMG.Default__WidgetBlueprintLibrary"):Create(window, cls, pc)
    b:UpdateCaption(FText(caption))
    b.ButtonWidth = BUTTON_WIDTH  -- (applied by the button when it is built; the game's fit one short word)
    return b
end

local function AddText(window, text)
    local tb = StaticConstructObject(StaticFindObject("/Script/UMG.TextBlock"), list)
    tb:SetText(FText(text))
    local font = window.HintText.Font
    if font then tb:SetFont(font) end
    list:AddChildToVerticalBox(tb):SetHorizontalAlignment(2)  -- centred
end

-- The window without its lobby code: the list takes the code field's place, its button goes, and the
-- hint becomes the list's title.
local function HideCode(window)
    if Valid(window.BUT_Join) and window.BUT_Join:GetVisibility() ~= VIS_COLLAPSED then window.BUT_Join:SetVisibility(VIS_COLLAPSED) end
    local title = "Cliquez sur un serveur pour le rejoindre :"
    if window.HintText:GetText():ToString() ~= title then window.HintText:SetText(FText(title)) end
end

-- (Re)builds the server buttons when the list changed.
local function Refresh()
    local window = Window()
    if not Valid(window) then return end
    HideCode(window)
    local servers = ReadServers()
    local key = "none"
    if servers then
        local parts = {}
        for _, s in ipairs(servers) do parts[#parts + 1] = s.Address .. s.Players .. s.Phase .. s.Name end
        key = table.concat(parts, ";")
    end
    if Valid(list) and key == lastKey then return end
    lastKey = key
    if not Valid(list) then
        -- In the code field's size box, instead of the field.
        local box = window.CodeTextBox:GetParent()
        if not (Valid(box) and box:IsA("/Script/UMG.SizeBox")) then return end
        list = StaticConstructObject(StaticFindObject("/Script/UMG.VerticalBox"), window.WidgetTree)
        box:ClearHeightOverride()
        box:ClearWidthOverride()
        box:ClearMaxDesiredWidth()
        box:SetContent(list)
    end
    list:ClearChildren()
    buttons = {}
    if not servers then
        AddText(window, "Ouvrez le jeu depuis le lanceur Lanppart3 (et gardez-le ouvert) pour voir les serveurs.")
        return
    end
    if #servers == 0 then
        AddText(window, "Aucun serveur Lanppart3 pour l'instant sur le réseau local ou Radmin VPN.")
        return
    end
    for _, s in ipairs(servers) do
        -- The game's buttons hold one short line: the name on the button, the rules under it.
        local b = MakeButton(window, s.Name)
        list:AddChildToVerticalBox(b)
        if b.ButtonSizeBox:IsValid() then b.ButtonSizeBox:SetWidthOverride(BUTTON_WIDTH) end
        AddText(window, string.format("%s, %s  ·  %d/%d joueurs%s", s.Mode, s.Rules, s.Players, s.Capacity,
                                      s.Phase == "waiting" and "" or "  ·  " .. PhaseText(s.Phase)))
        buttons[b:GetAddress()] = s
    end
end

local function Join(server)
    print("[Lanppart] server list: joining " .. server.Address .. "\n")
    Host.Join(server.Address, server.Lives, true)
end

local function RegisterHooks()
    RegisterHook(WINDOW .. ":ActivateWindow", function() lastKey = nil Refresh() end)
    RegisterHook(BUTTON .. ":FireButtonClick", function(self)
        local s = buttons[self:get():GetAddress()]
        if s then Join(s) end
    end)
end

-- The main menu's entry (the menu is rebuilt each time the player comes back to it).
local function RenameMenuEntry()
    for _, hud in ipairs(FindAllOf("wbp_mainmenu_hud_C") or {}) do
        local entry = Valid(hud) and hud.But_Lobby_JoinByCode
        if Valid(entry) and Valid(entry.CaptionText) and entry.CaptionText:GetText():ToString() ~= MENU_ENTRY then
            entry:UpdateCaption(FText(MENU_ENTRY))
        end
    end
end

-- Called periodically on the game thread.
function Browser.Tick()
    RenameMenuEntry()
    local window = Window()
    if not Valid(window) then return end
    if not hooked then
        hooked = true
        RegisterHooks()
        print("[Lanppart] in-game server list ready\n")
    end
    local v = window:GetVisibility()
    if v ~= 1 and v ~= 2 then Refresh() end  -- shown: keep the list current
end

return Browser
