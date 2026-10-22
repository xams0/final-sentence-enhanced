-- Zelda-style hearts for the lives: a row of full hearts (lives left) then empty ones (lives lost), drawn
-- with Image widgets from Assets/heart_full.png and heart_empty.png. (The game's font has no heart glyph.)
local Hearts = {}

local VIS_VISIBLE, VIS_COLLAPSED = 0, 1
local VALIGN_CENTER = 2
local GAP = 3

local function Valid(o) return o and o:IsValid() end

local function AssetsDir()
    local source = debug.getinfo(1, "S").source:gsub("^@", ""):gsub("\\", "/")
    return source:match("^(.*)/Scripts/[^/]+$") .. "/Assets/"
end

-- Textures imported at runtime live in /Engine/Transient: they are looked up again by name, and imported
-- again once garbage collected (when no heart is on screen any more).
local texturePaths = {}

local function Texture(name, outer)
    local path = texturePaths[name]
    local tex = path and StaticFindObject(path)
    if Valid(tex) then return tex end
    tex = StaticFindObject("/Script/Engine.Default__KismetRenderingLibrary"):ImportFileAsTexture2D(outer, AssetsDir() .. name .. ".png")
    if not Valid(tex) then return nil end
    texturePaths[name] = tex:GetFullName():match("^%S+ (.+)$")  -- "Texture2D /Engine/Transient.Texture2D_n"
    return tex
end

local rows = {}  -- row address -> { Images = {...}, Lives, Total, Size }

-- A new, empty row of hearts (HorizontalBox) owned by a widget tree. size: height in pixels.
function Hearts.NewRow(widgetTree, size)
    local row = StaticConstructObject(StaticFindObject("/Script/UMG.HorizontalBox"), widgetTree)
    rows[row:GetAddress()] = { Images = {}, Size = size or 22, Tree = widgetTree }
    return row
end

-- A heart being lost: it swells, then shrinks and fades into an empty heart (~0.6 s).
local LOSS_FRAMES = { { 1.35, 1.0 }, { 1.7, 1.0 }, { 1.55, 0.9 }, { 1.2, 0.75 }, { 0.85, 0.6 }, { 0.6, 0.45 } }

local function AnimateLoss(img, empty)
    local frame = 0
    LoopAsync(70, function()
        frame = frame + 1
        local done = frame > #LOSS_FRAMES
        local f = LOSS_FRAMES[frame]
        ExecuteInGameThread(function()
            if not Valid(img) then return end
            if done then
                img:SetBrushFromTexture(empty, false)
                img:SetRenderScale({ X = 1.0, Y = 1.0 })
                img:SetRenderOpacity(1.0)
            else
                img:SetRenderScale({ X = f[1], Y = f[1] })
                img:SetRenderOpacity(f[2])
            end
        end)
        return done
    end)
end

-- Shows lives / total hearts in the row (only when they changed). A heart just lost is animated.
-- Returns the number of hearts lost since the previous call.
function Hearts.Set(row, lives, total)
    if not Valid(row) then return 0 end
    local state = rows[row:GetAddress()]
    if not state then return 0 end
    total = math.max(0, math.floor(total))
    lives = math.max(0, math.min(total, math.floor(lives)))
    if state.Lives == lives and state.Total == total then return 0 end
    local lost = (state.Total == total and state.Lives and state.Lives > lives) and state.Lives - lives or 0
    local full, empty = Texture("heart_full", row), Texture("heart_empty", row)
    if not (full and empty) then return 0 end
    while #state.Images < total do
        local img = StaticConstructObject(StaticFindObject("/Script/UMG.Image"), state.Tree)
        local slot = row:AddChildToHorizontalBox(img)
        slot:SetPadding({ Left = 0, Top = 0, Right = GAP, Bottom = 0 })
        slot:SetVerticalAlignment(VALIGN_CENTER)
        state.Images[#state.Images + 1] = img
    end
    local width = state.Size * 13 / 12  -- heart_*.png are 13 x 12 pixel art
    for i, img in ipairs(state.Images) do
        if i <= total then
            if lost > 0 and i > lives and i <= state.Lives then
                AnimateLoss(img, empty)  -- (still full: it empties at the end)
            else
                img:SetBrushFromTexture(i <= lives and full or empty, false)
            end
            img:SetDesiredSizeOverride({ X = width, Y = state.Size })
            img:SetVisibility(VIS_VISIBLE)
        else
            img:SetVisibility(VIS_COLLAPSED)
        end
    end
    state.Lives, state.Total = lives, total
    return lost
end

local LOSS_SOUND = "/Game/TheEnd/audio/sounds/pawn/gun/cue_pawn_gun_trigger_missed.cue_pawn_gun_trigger_missed"

function Hearts.PlayLossSound(context)
    local sound = StaticFindObject(LOSS_SOUND)
    if not Valid(sound) then
        local ok, loaded = pcall(LoadAsset, LOSS_SOUND)
        sound = ok and loaded or nil
    end
    if not Valid(sound) then return end
    StaticFindObject("/Script/Engine.Default__GameplayStatics"):PlaySound2D(context, sound, 1.0, 1.0, 0.0, nil, nil, true)
end

function Hearts.Show(row, shown)
    if not Valid(row) then return end
    local wanted = shown and VIS_VISIBLE or VIS_COLLAPSED
    if row:GetVisibility() ~= wanted then row:SetVisibility(wanted) end
end

-- Lives left and total of a player, from the replicated revolver drum: a lost life loads a chamber
-- (see lanppart_native.c, LoseLife).
function Hearts.LivesOf(ps)
    local drum = ps.RevolverDrumPacked
    local chambers, lost = drum.DrumChambers, 0
    for i = 0, chambers - 1 do
        if (drum.BulletsMask >> i) & 1 == 1 then lost = lost + 1 end
    end
    return chambers - lost, chambers
end

return Hearts
