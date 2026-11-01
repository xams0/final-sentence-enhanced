-- Small UMG helpers for the panels the mod adds to the gameplay HUD (kill feed, observer, cup standings):
-- text blocks in the game's font and colours, placed on the HUD's root canvas.
local UI = {}

UI.VIS_VISIBLE, UI.VIS_COLLAPSED, UI.VIS_HIDDEN, UI.VIS_HIT_TEST_INVISIBLE = 0, 1, 2, 4
UI.VALIGN_CENTER = 2

-- The game's palette. (Given as on screen, sRGB; UMG takes linear colours.)
local function Colour(r, g, b)
    return { R = r ^ 2.2, G = g ^ 2.2, B = b ^ 2.2, A = 1.0 }
end
UI.CREAM = Colour(0.95, 0.88, 0.72)
UI.DIM = Colour(0.66, 0.60, 0.50)
UI.ORANGE = Colour(0.93, 0.52, 0.20)
UI.RED = Colour(0.92, 0.25, 0.18)
UI.GREEN = Colour(0.66, 0.88, 0.45)
UI.GOLD = Colour(1.0, 0.80, 0.28)

function UI.Valid(o) return o ~= nil and o:IsValid() end

local function Class(name) return StaticFindObject("/Script/UMG." .. name) end

-- The gameplay HUD of this game, or nil.
function UI.GameplayHud()
    for _, hud in ipairs(FindAllOf("wbp_hud_gameplay_C") or {}) do
        if hud:IsValid() and UI.Valid(hud.WidgetTree) and UI.Valid(hud.WidgetTree.RootWidget) then return hud end
    end
end

-- Whether a widget we made still belongs to this HUD (a new map builds a new HUD).
function UI.Owns(hud, widget)
    return UI.Valid(widget) and UI.Valid(hud) and widget:GetOuter():GetOuter():GetAddress() == hud:GetAddress()
end

-- A text block in the game's font. size: point size; colour: one of the colours above.
function UI.Text(hud, size, colour, text)
    local block = StaticConstructObject(Class("TextBlock"), hud.WidgetTree)
    block:SetFont(hud.TabMenuHintText.Font)
    local font = block.Font  -- (our copy: the HUD's own text keeps its size)
    font.Size = size
    block:SetFont(font)
    block:SetColorAndOpacity({ SpecifiedColor = colour or UI.CREAM, ColorUseRule = 0 })
    block:SetShadowOffset({ X = 1.5, Y = 1.5 })
    block:SetShadowColorAndOpacity({ R = 0, G = 0, B = 0, A = 0.85 })
    if text then block:SetText(FText(text)) end
    return block
end

function UI.SetText(block, text)
    if UI.Valid(block) and block:GetText():ToString() ~= text then block:SetText(FText(text)) end
end

function UI.SetColour(block, colour)
    if UI.Valid(block) then block:SetColorAndOpacity({ SpecifiedColor = colour, ColorUseRule = 0 }) end
end

function UI.VBox(hud) return StaticConstructObject(Class("VerticalBox"), hud.WidgetTree) end
function UI.HBox(hud) return StaticConstructObject(Class("HorizontalBox"), hud.WidgetTree) end

-- A dark, see-through plate around content (readable over the room).
function UI.Plate(hud, content, alpha)
    local border = StaticConstructObject(Class("Border"), hud.WidgetTree)
    border:SetBrushColor({ R = 0.0, G = 0.0, B = 0.0, A = alpha or 0.5 })
    border:SetPadding({ Left = 14, Top = 8, Right = 14, Bottom = 8 })
    border:SetContent(content)
    return border
end

-- Adds a widget to the HUD's root canvas. anchor/align: {X, Y} in 0..1; pos: pixel offset from the anchor.
function UI.Place(hud, widget, anchor, align, pos)
    local slot = hud.WidgetTree.RootWidget:AddChildToCanvas(widget)
    slot:SetAnchors({ Minimum = anchor, Maximum = anchor })
    slot:SetAlignment(align)
    slot:SetPosition(pos or { X = 0, Y = 0 })
    slot:SetAutoSize(true)
    slot:SetZOrder(50)
    return slot
end

-- The widget in a box of a fixed width (table columns).
function UI.Fixed(hud, widget, width)
    local box = StaticConstructObject(Class("SizeBox"), hud.WidgetTree)
    box:SetWidthOverride(width)
    box:SetContent(widget)
    return box
end

function UI.AddRow(box, widget, padding)
    local slot = box:AddChildToVerticalBox(widget)
    if padding then slot:SetPadding(padding) end
    return slot
end

function UI.AddCell(box, widget, padding)
    local slot = box:AddChildToHorizontalBox(widget)
    slot:SetVerticalAlignment(UI.VALIGN_CENTER)
    if padding then slot:SetPadding(padding) end
    return slot
end

function UI.Show(widget, shown)
    if not UI.Valid(widget) then return end
    local wanted = shown and UI.VIS_HIT_TEST_INVISIBLE or UI.VIS_COLLAPSED
    if widget:GetVisibility() ~= wanted then widget:SetVisibility(wanted) end
end

-- The text cut to its first n characters (a long name would spill over the next column).
function UI.Short(text, n)
    if (utf8.len(text) or #text) <= n then return text end
    return text:sub(1, (utf8.offset(text, n) or (n + 1)) - 1) .. "."
end

-- Player name of a player state.
function UI.Name(ps) return ps.PlayerNamePrivate:ToString() end

-- The writers' player states of a game state (bots included).
function UI.Writers(gs)
    local list = {}
    gs.PlayerArray:ForEach(function(_, p)
        local ps = p:get()
        if ps:IsValid() and ps:IsA("/Script/TheEnd.PGGameplayPlayerState") and ps.WriterId >= 0 then list[#list + 1] = ps end
    end)
    return list
end

-- This game's own player state, or nil.
function UI.LocalPlayerState()
    for _, pc in ipairs(FindAllOf("PlayerController") or {}) do
        if pc:IsValid() and pc:IsLocalController() then
            local ps = pc.PlayerState
            if UI.Valid(ps) and ps:IsA("/Script/TheEnd.PGGameplayPlayerState") then return ps, pc end
            return nil, pc
        end
    end
end

return UI
