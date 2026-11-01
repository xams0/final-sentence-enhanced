-- Kill feed, top right of the screen: who lost a life, who was eliminated (and why), the roulette, the
-- winner, and the cup's round winners and finalists. Each event shows the feed (its last lines) for
-- 10 seconds; it then fades out, until the next event.
--
-- Everything comes from the writers' replicated player states (state, lives in the revolver drum, cup
-- standing in Score): the feed works the same on a lobby host, a lobby member and a dedicated server's player.
local UI = require("ui")
local Hearts = require("hearts")
local Cup = require("cup")
local Host = require("host")

local Feed = {}

local SHOW_SECONDS = 10
local MAX_LINES = 6
local TYPEMODE_BATTLEROYAL, TYPEMODE_DUEL = 0, 2
local WS_ROULETTE, WS_EXECUTION, WS_DEAD, WS_FINISHED = 6, 7, 8, 9
local RESULT_WON, RESULT_TIMEOUT, RESULT_SHOT, RESULT_SKIP_ROULETTE = 1, 2, 3, 4
local RESULT_SKIP_REWIND, RESULT_OTHER_WON, RESULT_KNOCKDOWN, RESULT_LOGGED_OUT = 5, 6, 7, 8

local seenGs = nil
local seen = {}    -- player state address -> what the feed last saw of that writer
local lines = {}   -- the feed's lines, oldest first: { Text, Colour, Lives, Total }
local panel = nil  -- { Root, Rows = { { Line, Text, Hearts } } }
local lastEvent = -1000

local function Cause(result, typeMode)
    if result == RESULT_TIMEOUT or result == RESULT_SKIP_REWIND then return "temps écoulé" end
    if result == RESULT_SHOT then return "roulette" end
    if result == RESULT_SKIP_ROULETTE then
        return typeMode == TYPEMODE_BATTLEROYAL and "trop de fautes" or "manche perdue"
    end
    if result == RESULT_KNOCKDOWN then return "dernier de la manche" end
end

local function Add(text, colour, lives, total)
    lines[#lines + 1] = { Text = text, Colour = colour, Lives = lives, Total = total }
    while #lines > MAX_LINES do table.remove(lines, 1) end
    lastEvent = os.clock()
end

-- Compares a writer with what the feed saw last, and adds the events in between.
local function Watch(ps, typeMode, livesMode, cup)
    local key = ps:GetAddress()
    local name = UI.Name(ps)
    local state = ps.WriterState
    local lives, total = Hearts.LivesOf(ps)
    local standing = cup and Cup.Decode(ps)
    local before = seen[key]
    seen[key] = { State = state, Lives = lives, Standing = standing }
    if not before then return end  -- (first sight: nothing happened yet)

    local dead = state == WS_EXECUTION or state == WS_DEAD
    local wasDead = before.State == WS_EXECUTION or before.State == WS_DEAD
    if standing and before.Standing then
        local gained = standing.Points - before.Standing.Points
        if standing.Winner and not before.Standing.Winner then
            Add(name .. " remporte la coupe !", UI.GOLD)
        elseif standing.Finalist and not before.Standing.Finalist then
            Add(string.format("%s est finaliste ! (%d pts)", name, standing.Points), UI.ORANGE)
        elseif gained == Cup.POINTS[1] then
            Add(string.format("%s gagne la manche (+%d)", name, gained), UI.GREEN)
        end
    end
    if livesMode and not cup and lives < (before.Lives or lives) and lives > 0 and not dead then
        Add(name .. " perd une vie", UI.ORANGE, lives, total)
    end
    if dead and not wasDead then
        local result = ps.Result
        if result == RESULT_LOGGED_OUT then
            Add(name .. " a quitté la partie", UI.DIM)
        elseif result ~= RESULT_OTHER_WON and result ~= RESULT_WON then
            local cause = Cause(result, typeMode)
            Add(name .. " est éliminé(e)" .. (cause and (" · " .. cause) or ""), UI.RED, livesMode and 0 or nil, total)
        end
    elseif not livesMode and state == WS_ROULETTE and before.State ~= WS_ROULETTE then
        Add(name .. " joue à la roulette...", UI.DIM)
    elseif not livesMode and before.State == WS_ROULETTE and state ~= WS_ROULETTE and not dead then
        Add(name .. " survit à la roulette", UI.GREEN)
    end
    if state == WS_FINISHED and before.State ~= WS_FINISHED and ps.Result == RESULT_WON and not cup then
        Add(name .. " remporte la partie !", UI.GOLD)
    end
end

local function BuildPanel(hud)
    local box = UI.VBox(hud)
    local p = { Rows = {}, Hud = hud }
    for i = 1, MAX_LINES do
        local line = UI.HBox(hud)
        local row = { Line = line, Text = UI.Text(hud, 18, UI.CREAM, ""), Hearts = Hearts.NewRow(hud.WidgetTree, 15) }
        UI.AddCell(line, row.Text)
        UI.AddCell(line, row.Hearts, { Left = 10, Top = 0, Right = 0, Bottom = 0 })
        UI.AddRow(box, line, { Left = 0, Top = 2, Right = 0, Bottom = 2 })
        p.Rows[i] = row
    end
    p.Root = UI.Plate(hud, box, 0.5)
    -- Under the K.O. faces and the players counter, top right.
    UI.Place(hud, p.Root, { X = 1, Y = 0 }, { X = 1, Y = 0 }, { X = -40, Y = 175 })
    UI.Show(p.Root, false)
    return p
end

local function Fill()
    for i, row in ipairs(panel.Rows) do
        local l = lines[i]
        UI.Show(row.Line, l ~= nil)
        if l then
            UI.SetText(row.Text, l.Text)
            UI.SetColour(row.Text, l.Colour)
            Hearts.Show(row.Hearts, l.Total ~= nil and l.Lives ~= nil)
            if l.Total and l.Lives then Hearts.Set(row.Hearts, l.Lives, l.Total, true) end
        end
    end
end

-- Fades the feed out (~0.5 s), unless an event comes meanwhile: one step per 100 ms tick of main.lua.
local FADE_STEPS = 5
local fadeStep = nil

function Feed.Fade()
    if not fadeStep then return end
    local root = panel and panel.Root
    if not UI.Valid(root) then
        fadeStep = nil
        return
    end
    fadeStep = fadeStep + 1
    if os.clock() - lastEvent < SHOW_SECONDS then
        root:SetRenderOpacity(1.0)
        fadeStep = nil
    elseif fadeStep >= FADE_STEPS then
        UI.Show(root, false)
        root:SetRenderOpacity(1.0)
        fadeStep = nil
    else
        root:SetRenderOpacity(1.0 - fadeStep / FADE_STEPS)
    end
end

-- Called periodically on players' games.
function Feed.Tick()
    local gs = FindFirstOf("PGGameplayGameState")
    if not UI.Valid(gs) then
        seenGs, seen, lines = nil, {}, {}
        return
    end
    if gs:GetAddress() ~= seenGs then
        seenGs, seen, lines, lastEvent = gs:GetAddress(), {}, {}, -1000
    end
    local typeMode = gs.GameplayOptions.TypeMode
    local cup = Cup.Active(gs)
    local livesMode = Host.LivesMode
    local before = #lines > 0 and lines[#lines] or nil
    for _, ps in ipairs(UI.Writers(gs)) do Watch(ps, typeMode, livesMode, cup) end
    local hud = UI.GameplayHud()
    if not hud then return end
    if not (panel and UI.Owns(hud, panel.Root)) then panel = BuildPanel(hud) end
    local changed = #lines > 0 and lines[#lines] ~= before
    if changed then Fill() end
    if os.clock() - lastEvent < SHOW_SECONDS then
        if #lines > 0 then
            UI.Show(panel.Root, true)
            panel.Root:SetRenderOpacity(1.0)
        end
    elseif not fadeStep and panel.Root:GetVisibility() ~= UI.VIS_COLLAPSED then
        fadeStep = 0
    end
end

return Feed
