-- Shows the lives of sessions played with lives as Zelda-style hearts (hearts.lua), in place of the
-- game's revolver drums: next to the duelists' names ("Xams - ♥♥♥"), next to the keyboard language
-- (own lives, other modes), and in the scoreboard / match results rows.
local Host = require("host")
local Hearts = require("hearts")

local Hud = {}

local TYPEMODE_DUEL = 2
local VIS_HIDDEN = 2
local DUEL_NAME_WIDTH = 520  -- the game's 250 leaves no room for 6 hearts after a long name
local VALIGN_CENTER = 2
local BULLETS_WIDGET = "/Game/TheEnd/ui/interfaces/gameplay/tabmenu/wbp_gameplay_tabmenu_bullets.wbp_gameplay_tabmenu_bullets_C"

local function Valid(o) return o and o:IsValid() end

local function SetText(textBlock, wanted)
    if not Valid(textBlock) then return end
    if textBlock:GetText():ToString() ~= wanted then textBlock:SetText(FText(wanted)) end
end

local function Hide(widget)
    if Valid(widget) and widget:GetVisibility() ~= VIS_HIDDEN then widget:SetVisibility(VIS_HIDDEN) end
end

local modLabel = nil

-- "Lanppart3 Mod" under the ping, bottom left, on every screen (the persistent HUD outlives the maps).
local function ShowModLabel()
    local hud = FindFirstOf("wbp_hud_persistent_C")
    if not (Valid(hud) and Valid(hud.PingBlock)) then return end
    if Valid(modLabel) and modLabel:GetOuter():GetOuter():GetAddress() == hud:GetAddress() then return end
    modLabel = StaticConstructObject(StaticFindObject("/Script/UMG.TextBlock"), hud.WidgetTree, FName("LanppartModLabel"))
    local version = os.getenv("LANPPART3_LAUNCHER")
    modLabel:SetText(FText("Lanppart3 Mod" .. ((version and version:match("^%d")) and (" v" .. version) or "")))
    modLabel:SetFont(hud.PingValue.Font)
    modLabel:SetColorAndOpacity({ SpecifiedColor = { R = 0.84, G = 0.45, B = 0.18, A = 1.0 }, ColorUseRule = 0 })
    hud.PingBlock:GetParent():AddChildToVerticalBox(modLabel)
end

-- Hearts rows added to the current gameplay HUD.
local hudKey, duelLeft, duelRight, ownRow = nil, nil, nil, nil

-- Puts a row of hearts after a duelist's name: the name's size box (aligned left or right in the duel
-- block) gets [name, hearts], and room for them.
local function DuelHearts(hud, nameBlock)
    local box = nameBlock:GetParent()
    local line = StaticConstructObject(StaticFindObject("/Script/UMG.HorizontalBox"), hud.WidgetTree)
    box:SetContent(line)
    box:SetMaxDesiredWidth(DUEL_NAME_WIDTH)
    line:AddChildToHorizontalBox(nameBlock):SetVerticalAlignment(VALIGN_CENTER)
    local hearts = Hearts.NewRow(hud.WidgetTree, 22)
    local slot = line:AddChildToHorizontalBox(hearts)
    slot:SetVerticalAlignment(VALIGN_CENTER)
    slot:SetPadding({ Left = 10, Top = 0, Right = 0, Bottom = 0 })
    return hearts
end

local function SetUpHud(hud, duel)
    if hudKey == hud:GetAddress() then return end
    hudKey = hud:GetAddress()
    duelLeft, duelRight, ownRow = nil, nil, nil
    if duel then
        duelLeft = DuelHearts(hud, hud.DuelPlayerName_Left)
        duelRight = DuelHearts(hud, hud.DuelPlayerName_Right)
    else
        -- Own lives after the keyboard language (always on screen).
        ownRow = Hearts.NewRow(hud.WidgetTree, 20)
        local slot = hud.KeyboardLanguageText:GetParent():AddChildToHorizontalBox(ownRow)
        slot:SetVerticalAlignment(VALIGN_CENTER)
        slot:SetPadding({ Left = 14, Top = 0, Right = 0, Bottom = 0 })
    end
end

local function DuelName(textBlock, hearts, ps)
    SetText(textBlock, ps.PlayerNamePrivate:ToString() .. " -")
    Hearts.Set(hearts, Hearts.LivesOf(ps))
end

-- Scoreboard and match results: the bullets cell of each row shows hearts instead.
local bulletRows = {}  -- bullets widget address -> hearts row

local function SessionLives()
    local gs = FindFirstOf("PGGameplayGameState")
    return Valid(gs) and gs.GameplayOptions.BulletSlotsNum or nil
end

local function OnBulletsUpdated(widget)
    if not Host.LivesMode then return end
    local total = SessionLives()
    if not total then return end
    local key = widget:GetAddress()
    local row = bulletRows[key]
    if not Valid(row) then
        row = Hearts.NewRow(widget.WidgetTree, 14)
        widget.Bullet0:GetParent():AddChildToHorizontalBox(row):SetVerticalAlignment(VALIGN_CENTER)
        bulletRows[key] = row
    end
    for i = 0, 5 do Hide(widget["Bullet" .. i]) end
    Hearts.Set(row, total - widget.Bullets, total)
end

local bulletsHooked = false

local function HookBullets()
    if bulletsHooked or not Valid(StaticFindObject(BULLETS_WIDGET)) then return end
    bulletsHooked = true
    RegisterHook(BULLETS_WIDGET .. ":UpdateWidget", function(self)
        local ok, err = pcall(OnBulletsUpdated, self:get())
        if not ok then print("[Lanppart] hearts: " .. tostring(err) .. "\n") end
    end)
end

-- Called periodically on the game thread.
function Hud.Tick()
    ShowModLabel()
    HookBullets()
    if not Host.LivesMode then return end
    local gs = FindFirstOf("PGGameplayGameState")
    if not Valid(gs) then return end
    local hud = FindFirstOf("wbp_hud_gameplay_C")
    if not (Valid(hud) and Valid(hud.PlayerState)) then return end
    local duel = gs.GameplayOptions.TypeMode == TYPEMODE_DUEL
    SetUpHud(hud, duel)
    -- The revolver drums would show the lost lives as loaded chambers.
    Hide(hud.CylinderAssembly)
    Hide(hud.CylinderAssemblyRight)
    if duel then
        DuelName(hud.DuelPlayerName_Left, duelLeft, hud.PlayerState)
        if Valid(hud.DuelOpponentPlayerState) then
            DuelName(hud.DuelPlayerName_Right, duelRight, hud.DuelOpponentPlayerState)
        end
    else
        Hearts.Set(ownRow, Hearts.LivesOf(hud.PlayerState))
    end
end

return Hud
