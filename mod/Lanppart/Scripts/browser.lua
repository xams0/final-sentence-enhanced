-- In-game list of the Lanppart3 dedicated servers, in the "Rejoindre avec code" window (main menu):
-- one game-style button per server heard on the LAN / Radmin VPN, and an IP:port typed in the code field
-- joins that server. The list comes from the launcher (it listens to the servers' announcements and
-- writes %APPDATA%\Lanppart3\servers_seen.txt), which stays open while the game runs.
local Host = require("host")

local Browser = {}

local WINDOW = "/Game/TheEnd/ui/interfaces/mainmenu/lobby/wbp_mainmenu_window_lobby_joinbycode.wbp_mainmenu_window_lobby_joinbycode_C"
local BUTTON_CLASS = "/Game/TheEnd/ui/interfaces/common/buttons/mainmenubutton/wbp_mainmenu_button.wbp_mainmenu_button_C"
local BUTTON = BUTTON_CLASS
local LIST_MAX_AGE = 10  -- seconds: an older list means the launcher is closed

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
        if Valid(w) then return w end
    end
end

local function MakeButton(window, caption)
    local cls = StaticFindObject(BUTTON_CLASS)
    local pc = window.PC or FindFirstOf("PlayerController")
    local b = StaticFindObject("/Script/UMG.Default__WidgetBlueprintLibrary"):Create(window, cls, pc)
    b:UpdateCaption(FText(caption))
    return b
end

local function AddText(text)
    local tb = StaticConstructObject(StaticFindObject("/Script/UMG.TextBlock"), list)
    tb:SetText(FText(text))
    local font = Window().HintText.Font
    if font then tb:SetFont(font) end
    list:AddChildToVerticalBox(tb)
end

-- (Re)builds the server buttons under the code field when the list changed.
local function Refresh()
    local window = Window()
    if not (Valid(window) and Valid(window.CodeTextBox)) then return end
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
        -- Under the window's column (the code field sits in a single-child size box).
        local column = window.CodeTextBox:GetParent()
        while Valid(column) and not column:IsA("/Script/UMG.VerticalBox") do column = column:GetParent() end
        if not Valid(column) then return end
        list = StaticConstructObject(StaticFindObject("/Script/UMG.VerticalBox"), window.WidgetTree)
        column:AddChildToVerticalBox(list)
    end
    list:ClearChildren()
    buttons = {}
    if not servers then
        AddText("Serveurs LAN : ouvrez le jeu depuis le lanceur Lanppart3 pour les voir. Une adresse IP:port tapée ci-dessus fonctionne aussi.")
        return
    end
    if #servers == 0 then
        AddText("Aucun serveur Lanppart3 sur le réseau local ou Radmin VPN. Vous pouvez taper une adresse IP:port ci-dessus.")
        return
    end
    AddText("Serveurs LAN :")
    for _, s in ipairs(servers) do
        local caption = string.format("%s  ·  %s, %s  ·  %d/%d  ·  %s", s.Name, s.Mode, s.Rules, s.Players, s.Capacity, PhaseText(s.Phase))
        local b = MakeButton(window, caption)
        list:AddChildToVerticalBox(b)
        buttons[b:GetAddress()] = s
    end
end

local function LooksLikeAddress(text)
    return text:match("^%d+%.%d+%.%d+%.%d+:?%d*$") ~= nil
end

local function Join(address, lives)
    if not address:find(":") then address = address .. ":7777" end
    print("[Lanppart] server list: joining " .. address .. "\n")
    Host.Join(address, lives)
end

local function RegisterHooks()
    RegisterHook(WINDOW .. ":ActivateWindow", function() lastKey = nil Refresh() end)
    -- An IP:port typed in the code field joins that server.
    RegisterHook(WINDOW .. ":BndEvt__wbp_lobby_invitecode_BUT_Join_K2Node_ComponentBoundEvent_2_OnButtonClicked__DelegateSignature", function(self)
        local text = self:get().CodeTextBox:GetText():ToString():gsub("%s", "")
        if LooksLikeAddress(text) then Join(text, false) end
    end)
    RegisterHook(BUTTON .. ":FireButtonClick", function(self)
        local s = buttons[self:get():GetAddress()]
        if s then Join(s.Address, s.Lives) end
    end)
end

-- Called periodically on the game thread.
function Browser.Tick()
    local window = Window()
    if not Valid(window) then return end
    if not hooked then
        hooked = true
        RegisterHooks()
        local hud = FindFirstOf("wbp_mainmenu_hud_C")
        if Valid(hud) and Valid(hud.But_Lobby_JoinByCode) then
            hud.But_Lobby_JoinByCode:UpdateCaption(FText("Code ou serveur LAN"))
        end
        print("[Lanppart] in-game server list ready\n")
    end
    local v = window:GetVisibility()
    if v ~= 1 and v ~= 2 then Refresh() end  -- shown: keep the list current
end

return Browser
