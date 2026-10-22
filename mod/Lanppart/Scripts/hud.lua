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
local TABMENU_ENTRY = "/Game/TheEnd/ui/interfaces/gameplay/tabmenu/wbp_gameplay_tabmenu_entry.wbp_gameplay_tabmenu_entry_C"

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

-- Returns the hearts just lost.
local function DuelName(textBlock, hearts, ps)
    SetText(textBlock, ps.PlayerNamePrivate:ToString() .. " -")
    return Hearts.Set(hearts, Hearts.LivesOf(ps))
end

-- Scoreboard (Tab) and match results: the bullets cell of each row shows hearts instead. (In K.O. the
-- game hides that cell, having no roulette: it is shown again.)
local bulletRows = {}  -- bullets widget address -> hearts row
local VIS_HIT_TEST_INVISIBLE = 4

local function SessionLives()
    local gs = FindFirstOf("PGGameplayGameState")
    return Valid(gs) and gs.GameplayOptions.BulletSlotsNum or nil
end

local function ShowHearts(widget, lives, total)
    local key = widget:GetAddress()
    local row = bulletRows[key]
    if not Valid(row) then
        row = Hearts.NewRow(widget.WidgetTree, 14)
        widget.Bullet0:GetParent():AddChildToHorizontalBox(row):SetVerticalAlignment(VALIGN_CENTER)
        bulletRows[key] = row
    end
    for i = 0, 5 do Hide(widget["Bullet" .. i]) end
    for _, w in ipairs({ widget, widget:GetParent() }) do
        if Valid(w) and w:GetVisibility() ~= VIS_HIT_TEST_INVISIBLE then w:SetVisibility(VIS_HIT_TEST_INVISIBLE) end
    end
    Hearts.Set(row, lives, total)
end

-- Match results: from the row's bullets (lost lives). (The scoreboard's rows are handled below.)
local function OnBulletsUpdated(widget)
    if not Host.LivesMode or widget:GetFullName():find("tabmenu_entry", 1, true) then return end
    local total = SessionLives()
    if total then ShowHearts(widget, total - widget.Bullets, total) end
end

-- Scoreboard: from the row's player; an empty row shows nothing.
local function OnEntryUpdated(entry, ps)
    if not (Host.LivesMode and Valid(entry.wbp_gameplay_tabmenu_bullets)) then return end
    if Valid(ps) then
        ShowHearts(entry.wbp_gameplay_tabmenu_bullets, Hearts.LivesOf(ps))
    else
        Hide(entry.wbp_gameplay_tabmenu_bullets)
    end
end

-- The scoreboard's header gets a "Vies" column title over the hearts, in the room the game keeps for its
-- bullets column (BulletSpacer), so that every column stays above its values.
local livesHeaders = {}  -- scoreboard address -> "Vies" text block

local function ShowLivesHeader(hud)
    local menu = hud.TabMenuWidget
    if not (Valid(menu) and Valid(menu.BulletSpacer) and Valid(menu.MistakesText)) then return end
    local key = menu:GetAddress()
    local title = livesHeaders[key]
    if not Valid(title) then
        local spacer = menu.BulletSpacer
        title = StaticConstructObject(StaticFindObject("/Script/UMG.TextBlock"), menu.WidgetTree)
        title:SetText(FText("Vies"))
        title:SetFont(menu.MistakesText.Font)
        title:SetColorAndOpacity(menu.MistakesText.ColorAndOpacity)
        title:SetJustification(1)  -- centred
        local slot = spacer:GetParent():AddChildToHorizontalBox(title)
        slot:SetSize(spacer.Slot.Size)
        slot:SetPadding(spacer.Slot.Padding)
        slot:SetHorizontalAlignment(spacer.Slot.HorizontalAlignment)
        slot:SetVerticalAlignment(spacer.Slot.VerticalAlignment)
        livesHeaders[key] = title
    end
    if menu.BulletSpacer:GetVisibility() ~= 1 then menu.BulletSpacer:SetVisibility(1) end  -- collapsed: the title takes its room
end

local bulletsHooked, entriesHooked = false, false

local function Guarded(fn)
    return function(...)
        local ok, err = pcall(fn, ...)
        if not ok then print("[Lanppart] hearts: " .. tostring(err) .. "\n") end
    end
end

local function HookBullets()
    if not bulletsHooked and Valid(StaticFindObject(BULLETS_WIDGET)) then
        bulletsHooked = true
        RegisterHook(BULLETS_WIDGET .. ":UpdateWidget", Guarded(function(self) OnBulletsUpdated(self:get()) end))
    end
    if not entriesHooked and Valid(StaticFindObject(TABMENU_ENTRY)) then
        entriesHooked = true
        RegisterHook(TABMENU_ENTRY .. ":UpdateWidget", Guarded(function(self, ps) OnEntryUpdated(self:get(), ps:get()) end))
        RegisterHook(TABMENU_ENTRY .. ":InitiateEmptyEntry", Guarded(function(self) OnEntryUpdated(self:get(), nil) end))
    end
end

-- On a dedicated server, the players wait in the room until the organizer clicks "Lancer la partie".
local SESSION_WAITING_FOR_PLAYERS = 2
local WAITING_TEXT = "En attente du lancement de la partie par l'organisateur..."
local waitLabel = nil

local function ShowWaiting(gs)
    local waiting = Host.OnDedicatedServer and Valid(gs) and gs.SessionState == SESSION_WAITING_FOR_PLAYERS
    local hud = FindFirstOf("wbp_hud_persistent_C")
    if not Valid(hud) then return end
    if not (Valid(waitLabel) and waitLabel:GetOuter():GetOuter():GetAddress() == hud:GetAddress()) then
        if not waiting then return end
        local root = hud.WidgetTree.RootWidget
        if not (Valid(root) and root:IsA("/Script/UMG.CanvasPanel")) then return end
        waitLabel = StaticConstructObject(StaticFindObject("/Script/UMG.TextBlock"), hud.WidgetTree, FName("LanppartWaitLabel"))
        waitLabel:SetText(FText(WAITING_TEXT))
        waitLabel:SetFont(hud.PingValue.Font)
        local font = waitLabel.Font
        font.Size = 30
        waitLabel:SetFont(font)
        waitLabel:SetColorAndOpacity({ SpecifiedColor = { R = 0.95, G = 0.86, B = 0.66, A = 1.0 }, ColorUseRule = 0 })
        waitLabel:SetShadowOffset({ X = 2, Y = 2 })
        waitLabel:SetShadowColorAndOpacity({ R = 0, G = 0, B = 0, A = 0.9 })
        local slot = root:AddChildToCanvas(waitLabel)
        slot:SetAnchors({ Minimum = { X = 0.5, Y = 0.2 }, Maximum = { X = 0.5, Y = 0.2 } })
        slot:SetAlignment({ X = 0.5, Y = 0.5 })
        slot:SetAutoSize(true)
    end
    local wanted = waiting and 4 or 1  -- HitTestInvisible / Collapsed
    if waitLabel:GetVisibility() ~= wanted then waitLabel:SetVisibility(wanted) end
end

-- A writer's camera is their own pawn while they wait or type. A client loading slowly may get its pawn
-- before its camera manager: the camera retarget is then skipped (lanppart_native.c, remote controller
-- camera patch) and the view stayed on the room's default, overhead camera for the whole game. Put it back
-- when it is off the pawn for a while in those states (other states have their own cameras: roulette,
-- execution, death...).
local CAMERA_STATES = { [1] = true, [2] = true, [3] = true, [4] = true }  -- Waiting, Typing, Rewinding, Await
local CAMERA_PATIENCE = 3  -- ticks (0.5 s each) off the pawn before the view is put back
local cameraOff = 0

local function KeepCameraOnWriter()
    local pc
    for _, c in ipairs(FindAllOf("PlayerController") or {}) do
        if c:IsValid() and c:IsLocalController() then pc = c break end
    end
    local pawn = pc and pc.Pawn
    local ps = pc and pc.PlayerState
    if not (Valid(pawn) and Valid(ps) and Valid(pc.PlayerCameraManager) and ps:IsA("/Script/TheEnd.PGGameplayPlayerState")
            and CAMERA_STATES[ps.WriterState]) then
        cameraOff = 0
        return
    end
    local view = pc:GetViewTarget()
    if Valid(view) and view:GetAddress() == pawn:GetAddress() then
        cameraOff = 0
        return
    end
    cameraOff = cameraOff + 1
    if cameraOff < CAMERA_PATIENCE then return end
    cameraOff = 0
    print("[Lanppart] camera was off the writer: back on it\n")
    pc:SetViewTargetWithBlend(pawn, 0.0, 0, 0.0, false)
end

-- Called periodically on the game thread.
function Hud.Tick()
    KeepCameraOnWriter()
    ShowModLabel()
    HookBullets()
    ShowWaiting(FindFirstOf("PGGameplayGameState"))
    if not Host.LivesMode then return end
    local gs = FindFirstOf("PGGameplayGameState")
    if not Valid(gs) then return end
    local hud = FindFirstOf("wbp_hud_gameplay_C")
    if not (Valid(hud) and Valid(hud.PlayerState)) then return end
    local duel = gs.GameplayOptions.TypeMode == TYPEMODE_DUEL
    SetUpHud(hud, duel)
    ShowLivesHeader(hud)
    -- The revolver drums would show the lost lives as loaded chambers.
    Hide(hud.CylinderAssembly)
    Hide(hud.CylinderAssemblyRight)
    local lost
    if duel then
        lost = DuelName(hud.DuelPlayerName_Left, duelLeft, hud.PlayerState)
        if Valid(hud.DuelOpponentPlayerState) then
            DuelName(hud.DuelPlayerName_Right, duelRight, hud.DuelOpponentPlayerState)
        end
    else
        lost = Hearts.Set(ownRow, Hearts.LivesOf(hud.PlayerState))
    end
    if lost > 0 then Hearts.PlayLossSound(hud) end  -- (own lives only)
end

return Hud
