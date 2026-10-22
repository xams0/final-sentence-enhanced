-- Lobby options window (Partie personnalisée, Créer nouveau, lobby "Options") with the mod:
--  * "Jouer à la roulette russe" can be changed in every mode. Unchecked, the "Vies" line replaces the
--    revolver settings: each roulette or elimination of the game then costs a life (lanppart_native.c).
--  * Two presets are added to the mode list: Lanppart Poules (Battle Royale without bots) and
--    Lanppart Finales (Duel with lives).
--
-- The blueprint maps the mode list index straight to EPGTypeMode, so the presets are reported as their
-- base mode to it, and this module adjusts the widgets after each blueprint update (UE4SS runs blueprint
-- hooks after the function). The window saves every change in the player's options, which the game
-- also uses without the mod, on the official servers: Duel / K.O. roulette rules stay out of the save.
local Config = require("config")
local Hearts = require("hearts")

local Menu = {}

local WINDOW_CLASS = "wbp_mainmenu_window_lobby_createnew_C"
local WINDOW = "/Game/TheEnd/ui/interfaces/mainmenu/lobby/wbp_mainmenu_window_lobby_createnew." .. WINDOW_CLASS
local TYPEMODE_BATTLEROYAL = 0
local TYPEMODE_KNOCKDOWN = 1
local TYPEMODE_DUEL = 2
local WINDOW_MODE_CREATE = 0     -- e_lobbyoptions_mode: new lobby
local WINDOW_MODE_VIEW = 2       -- e_lobbyoptions_mode: lobby member looking at the host's options
local VIS_COLLAPSED, VIS_HIDDEN = 1, 2
local VALIGN_CENTER = 2
local BOTS_MARKER = -2           -- never a real bots count (Duel and K.O. use -1, Battle Royale 0..39)
local MAX_CHAMBERS = 6           -- revolver chambers, also the most lives
local MAX_MISTAKES = 10
local LIVES_TITLE = "Vies"

-- Appended after the game's own modes (Battle royale, K.O., Duel).
local FIRST_INDEX = 3
local PRESETS = {
    { Key = "poules", Name = "Lanppart Poules", Base = TYPEMODE_BATTLEROYAL },
    { Key = "finales", Name = "Lanppart Finales", Base = TYPEMODE_DUEL },
}

-- Saved options the official game restricts (Duel always plays the roulette, K.O. never does).
local ROULETTE_FIELDS = { "bPlayRoulette", "BulletSlotsNum", "InitialBulletsNum", "MaxMistakesPerRound" }

local typeCombos = {}     -- addresses of the windows' mode lists
local windowState = {}    -- per window address
local savedRules = {}     -- type mode -> ROULETTE_FIELDS values of the player's save
local pendingStamp = nil  -- rules of the options the window just produced (Créer / Appliquer)
local hooksRegistered = false

-- "poules", "finales" or nil: the preset the lobby options were made with.
function Menu.IsLanppart(opts)
    if not opts.bIsPrivate or opts.BotsCount ~= BOTS_MARKER then return nil end
    if opts.TypeMode == TYPEMODE_DUEL then return "finales" end
    if opts.TypeMode == TYPEMODE_BATTLEROYAL then return "poules" end
    return nil
end

Menu.BOTS_MARKER = BOTS_MARKER

local function Valid(o) return o and o:IsValid() end

local function Clamp(v, lo, hi) return math.max(lo, math.min(hi, math.floor(v))) end

local function IsShown(w)
    local v = w:GetVisibility()
    return v ~= VIS_COLLAPSED and v ~= VIS_HIDDEN
end

local function SetEnabled(widget, enabled)
    if widget:GetIsEnabled() ~= enabled then widget:SetIsEnabled(enabled) end
end

local function Combo(w) return w.TypeModeComboBox.ComboBoxBtn end

local function PresetByKey(key)
    for _, p in ipairs(PRESETS) do
        if p.Key == key then return p end
    end
end

-- Preset selected in the window, or nil. (Reads the option text: the index is remapped.)
local function SelectedPreset(w)
    local name = Combo(w):GetSelectedOption():ToString()
    for _, p in ipairs(PRESETS) do
        if p.Name == name then return p end
    end
end

local function BaseMode(w)
    local preset = SelectedPreset(w)
    if preset then return preset.Base end
    local combo = Combo(w)
    return combo:FindOptionIndex(combo:GetSelectedOption():ToString())
end

local function EnsureEntries(w)
    local combo = Combo(w)
    typeCombos[combo:GetAddress()] = true
    for _, p in ipairs(PRESETS) do
        if combo:FindOptionIndex(p.Name) < 0 then combo:AddOption(p.Name) end
    end
end

local function StateOf(w)
    local key = w:GetAddress()
    local state = windowState[key]
    if not state then
        state = {
            SlotsTitle = w.BulletSlotsStepper.TitleText:GetText(),
            InitialBulletsVisibility = w.InitialBulletsStepper:GetVisibility(),
            RevolverDrumVisibility = w.RevolverDrum:GetVisibility(),
            IsPrivateEnabled = w.IsPrivateCheckbox:GetIsEnabled(),
            BotsDifficultyEnabled = w.BotsDifficultyComboBox:GetIsEnabled(),
        }
        windowState[key] = state
    end
    return state
end

local function RememberSavedRules(typeMode)
    if savedRules[typeMode] then return end
    local saved = {}
    FindFirstOf("PGSaveGame"):GetGameplayOptionsForMode(typeMode, saved)
    local values = {}
    for _, f in ipairs(ROULETTE_FIELDS) do values[f] = saved[f] end
    savedRules[typeMode] = values
end

local function SetRange(stepper, lo, hi)
    if stepper.MinStepper ~= lo then stepper:SetMinStepper(lo) end
    if stepper.MaxStepper ~= hi then stepper:SetMaxStepper(hi) end
    if stepper.Stepper < lo or stepper.Stepper > hi then stepper:SetStepper(Clamp(stepper.Stepper, lo, hi)) end
end

local refreshing = false
local Adjust

-- Brings the widgets in line with the selected mode and roulette choice, after a blueprint update.
-- (Changing a stepper runs the blueprint's change handler, which comes back here: ignored meanwhile.)
local function Refresh(w)
    if refreshing or not Valid(w) then return end
    refreshing = true
    local ok, err = pcall(function()
        Adjust(w)
    end)
    refreshing = false
    if not ok then error(err, 0) end
end

Adjust = function(w)
    EnsureEntries(w)
    local state = StateOf(w)
    local preset = SelectedPreset(w)
    local base = BaseMode(w)
    local editable = w.Mode ~= WINDOW_MODE_VIEW
    if base == TYPEMODE_DUEL or base == TYPEMODE_KNOCKDOWN then RememberSavedRules(base) end

    -- Presets: sessions for the lobby's members only, no bots.
    if preset then
        w.IsPrivateCheckbox.CheckBoxBtn:SetIsChecked(true)
        SetEnabled(w.IsPrivateCheckbox, false)
        SetEnabled(w.BotsDifficultyComboBox, false)
        SetEnabled(w.BotsNumStepper, false)
        if preset.Key == "poules" and w.BotsNumStepper.Stepper ~= 0 then w.BotsNumStepper:SetStepper(0) end
    elseif state.Preset then
        SetEnabled(w.IsPrivateCheckbox, state.IsPrivateEnabled and editable)
        SetEnabled(w.BotsDifficultyComboBox, state.BotsDifficultyEnabled and editable)
    end
    state.Preset = preset and preset.Key

    local roulette = w.PlayRouletteCheckbox
    local finales = preset and preset.Key == "finales"
    if finales and roulette.CheckBoxBtn:IsChecked() then roulette.CheckBoxBtn:SetIsChecked(false) end
    SetEnabled(roulette, editable and not finales)
    local withRoulette = roulette.CheckBoxBtn:IsChecked()
    -- Unchecked by the player just now (not on opening: the saved lives stay).
    local changed = state.Roulette ~= nil and state.Roulette ~= withRoulette
    state.Roulette = withRoulette  -- before the steppers below call back into Refresh

    local slots, initial, mistakes = w.BulletSlotsStepper, w.InitialBulletsStepper, w.MaxMistakesPerRoundStepper
    if withRoulette then
        slots.TitleText:SetText(state.SlotsTitle)
        initial:SetVisibility(state.InitialBulletsVisibility)
        w.RevolverDrum:SetVisibility(state.RevolverDrumVisibility)
        Hearts.Show(state.Hearts, false)
        SetRange(slots, 1, MAX_CHAMBERS)
        SetRange(initial, 0, slots.Stepper - 1)
    else
        slots.TitleText:SetText(FText(LIVES_TITLE))
        initial:SetVisibility(VIS_COLLAPSED)
        w.RevolverDrum:SetVisibility(VIS_HIDDEN)
        SetRange(slots, 1, MAX_CHAMBERS)
        if changed and editable then slots:SetStepper(Clamp(Config.FinalesLives, 1, MAX_CHAMBERS)) end
        if initial.Stepper ~= 0 then initial:SetStepper(0) end
        -- The lives as hearts, where the drum was.
        if not state.Hearts then
            -- On the stepper's line: [-] value [+] hearts.
            state.Hearts = Hearts.NewRow(w.WidgetTree, 20)
            local slot = slots.IncrementButton:GetParent():AddChildToHorizontalBox(state.Hearts)
            slot:SetVerticalAlignment(VALIGN_CENTER)
            slot:SetPadding({ Left = 16, Top = 0, Right = 0, Bottom = 0 })
        end
        Hearts.Show(state.Hearts, true)
        Hearts.Set(state.Hearts, slots.Stepper, slots.Stepper)
    end
    SetEnabled(slots, editable)
    SetEnabled(initial, editable and withRoulette)
    -- Battle Royale: reaching the mistakes limit of a round triggers the roulette, or costs a life.
    if base == TYPEMODE_BATTLEROYAL then SetRange(mistakes, 1, MAX_MISTAKES) end
    SetEnabled(mistakes, editable and base == TYPEMODE_BATTLEROYAL)
end

-- The window was opened on the lobby's options (host editing them, or a member looking at them).
local function ShowLobbyOptions(w)
    if not Valid(w) then return end
    EnsureEntries(w)
    local lobby = FindFirstOf("PGSteamLobbySubsystem")
    if w.Mode == WINDOW_MODE_CREATE or not Valid(lobby) then
        Refresh(w)
        return
    end
    local opts = lobby.GameplayOptions
    local preset = PresetByKey(Menu.IsLanppart(opts))
    local wanted = preset and preset.Name or Combo(w):GetOptionAtIndex(opts.TypeMode):ToString()
    if Combo(w):GetSelectedOption():ToString() ~= wanted then Combo(w):SetSelectedOption(wanted) end
    -- A mode change on the way reloaded the player's saved options for the mode: show the lobby's again.
    w.GameplayOptions = opts
    w:FulfillOptions()
    -- The blueprint shows the official roulette rules of Duel / K.O.: show the lobby's.
    w.PlayRouletteCheckbox.CheckBoxBtn:SetIsChecked(opts.bPlayRoulette)
    StateOf(w).Roulette = opts.bPlayRoulette
    Refresh(w)
    w.BulletSlotsStepper:SetStepper(Clamp(opts.BulletSlotsNum, 1, MAX_CHAMBERS))
    if opts.bPlayRoulette then w.InitialBulletsStepper:SetStepper(math.max(0, opts.InitialBulletsNum)) end
    if opts.TypeMode == TYPEMODE_BATTLEROYAL then w.MaxMistakesPerRoundStepper:SetStepper(opts.MaxMistakesPerRound) end
end

-- Writes the window's rules into options it is sending to the lobby, in place.
local function Stamp(opts)
    local p = pendingStamp
    pendingStamp = nil
    if not p or os.clock() - p.Time > 1 then return end
    opts.TypeMode = p.Base
    opts.bPlayRoulette = p.Roulette
    opts.BulletSlotsNum = Clamp(p.Slots, 1, MAX_CHAMBERS)
    opts.InitialBulletsNum = p.Roulette and Clamp(p.Initial, 0, opts.BulletSlotsNum - 1) or 0
    if p.Base == TYPEMODE_BATTLEROYAL then opts.MaxMistakesPerRound = Clamp(p.Mistakes, 1, MAX_MISTAKES) end
    if p.Preset then
        opts.bIsPrivate = true
        opts.BotsCount = BOTS_MARKER
    elseif opts.BotsCount == BOTS_MARKER then
        local saved = {}
        FindFirstOf("PGSaveGame"):GetGameplayOptionsForMode(p.Base, saved)
        opts.BotsCount = saved.BotsCount
    end
    print(string.format("[Lanppart] lobby options: mode %s, %s %d\n", p.Preset and p.Preset.Name or tostring(p.Base),
        p.Roulette and "roulette, chambers" or "lives", opts.BulletSlotsNum))
end

local function Guard(fn)
    return function(...)
        local ok, err = pcall(fn, ...)
        if not ok then print("[Lanppart] menu: " .. tostring(err) .. "\n") end
    end
end

-- Native hooks, registered at load.
RegisterHook("/Script/UMG.ComboBoxString:GetSelectedIndex", function() end, function(self, ret)
    local index = ret:get()
    if index < FIRST_INDEX or not typeCombos[self:get():GetAddress()] then return nil end
    local p = PRESETS[index - FIRST_INDEX + 1]
    return p and p.Base or nil
end)
RegisterHook("/Script/TheEndClient.PGSaveGame:SetGameplayOptionsForMode", Guard(function(_, typeMode, options)
    local rules = savedRules[typeMode:get()]
    if not rules then return end
    local opts = options:get()
    for f, v in pairs(rules) do opts[f] = v end
end))
for _, fn in ipairs({ "CreateLobby", "UpdateGameplayOptions" }) do
    RegisterHook("/Script/TheEndClient.PGSteamLobbySubsystem:" .. fn, Guard(function(_, options)
        Stamp(options:get())
    end))
end

-- Blueprint hooks: the window class only exists once the main menu is loaded.
local function RegisterWindowHooks()
    local function After(fn, cb) RegisterHook(WINDOW .. ":" .. fn, Guard(cb)) end
    After("FulfillTypeModes", function(self) EnsureEntries(self:get()) end)
    After("FulfillOptions", function(self) Refresh(self:get()) end)
    After("OnTypeModeSelectionChanged_Event", function(self) Refresh(self:get()) end)
    After("OnPlayRouletteCheckStateChanged_Event", function(self) Refresh(self:get()) end)
    After("OnBulletSlotsValueChanged_Event", function(self) Refresh(self:get()) end)
    After("SetMode", function(self) ShowLobbyOptions(self:get()) end)
    After("GetGameplayOptions", function(self)
        local w = self:get()
        pendingStamp = {
            Preset = SelectedPreset(w), Base = BaseMode(w), Time = os.clock(),
            Roulette = w.PlayRouletteCheckbox.CheckBoxBtn:IsChecked(),
            Slots = w.BulletSlotsStepper.Stepper, Initial = w.InitialBulletsStepper.Stepper,
            Mistakes = w.MaxMistakesPerRoundStepper.Stepper,
        }
    end)
end

-- Called periodically on the game thread.
function Menu.Tick()
    if hooksRegistered then return end
    local w = FindFirstOf(WINDOW_CLASS)
    if not Valid(w) then return end
    hooksRegistered = true
    RegisterWindowHooks()
    for _, win in ipairs(FindAllOf(WINDOW_CLASS) or {}) do
        EnsureEntries(win)
        if IsShown(win) then Refresh(win) end
    end
    print("[Lanppart] lobby window extended\n")
end

return Menu
