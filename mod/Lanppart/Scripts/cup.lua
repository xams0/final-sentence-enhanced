-- Lanppart Coupe, a cup as in Trackmania: K.O. rounds where nobody is eliminated. The finish order of each
-- round scores points (10, 6, 4, 3, 2, 1, then 0: the last one is cut off as in K.O.). Reaching the points
-- limit makes a writer a finalist; a finalist who then finishes a round first wins the cup and the match.
--
-- lanppart_native.c counts the finishes in their exact order. A writer's standing travels in its
-- replicated PlayerState Score, an exact integer in a float:
--   bit 0 cup marker, bit 1 finalist, bit 2 winner, bits 3-12 points, bits 13-22 points limit.
-- The host gives every writer the marker and the limit (Cup.HostTick); the players' games show the
-- standings on the left of the screen (Cup.ClientTick).
local UI = require("ui")

local Cup = {}

Cup.POINTS = { 10, 6, 4, 3, 2, 1 }
local MARKER, FINALIST, WINNER = 1, 2, 4
local MAX_LIMIT = 1000
local DELTA_SECONDS = 9  -- how long a row shows the points it just won

Cup.Limit = nil  -- points limit of the cup this game hosts, nil when it hosts no cup

local function Score(ps)
    local s = ps.Score
    return (type(s) == "number" and s > 0) and math.floor(s + 0.5) or 0
end

-- A writer's cup standing { Points, Limit, Finalist, Winner }, or nil outside a cup.
function Cup.Decode(ps)
    local v = Score(ps)
    if v & MARKER == 0 then return nil end
    return { Points = (v >> 3) & 1023, Limit = (v >> 13) & 1023, Finalist = v & FINALIST ~= 0, Winner = v & WINNER ~= 0 }
end

-- Whether the session is a cup (from the writers' standings).
function Cup.Active(gs)
    for _, ps in ipairs(UI.Writers(gs)) do
        if Cup.Decode(ps) then return true end
    end
    return false
end

function Cup.ClampLimit(points)
    return math.max(10, math.min(MAX_LIMIT - 1, math.floor(tonumber(points) or 50)))
end

-- Host, called periodically: every writer of the cup gets its standing (once: the points are the native's).
function Cup.HostTick(gs)
    if not Cup.Limit then return end
    local base = MARKER | (Cup.ClampLimit(Cup.Limit) << 13)
    for _, ps in ipairs(UI.Writers(gs)) do
        if Score(ps) & MARKER == 0 then
            ps.Score = base
            ps:ForceNetUpdate()
        end
    end
end

-- The standings, best first: the winner, the finalists, then by points.
function Cup.Standings(gs)
    local list = {}
    for _, ps in ipairs(UI.Writers(gs)) do
        local s = Cup.Decode(ps)
        if s then
            s.Ps, s.Name, s.Key = ps, UI.Name(ps), ps:GetAddress()
            list[#list + 1] = s
        end
    end
    table.sort(list, function(a, b)
        if a.Winner ~= b.Winner then return a.Winner end
        if a.Finalist ~= b.Finalist then return a.Finalist end
        if a.Points ~= b.Points then return a.Points > b.Points end
        return a.Name:lower() < b.Name:lower()
    end)
    return list
end

-- For the server's status file: "name:points:F|W|-" entries, best first.
function Cup.StatusLine(gs)
    local parts = {}
    for _, s in ipairs(Cup.Standings(gs)) do
        parts[#parts + 1] = string.format("%s:%d:%s", (s.Name:gsub("[:\t]", " ")), s.Points,
            s.Winner and "W" or s.Finalist and "F" or "-")
    end
    return table.concat(parts, "\t")
end

-- ---- Players: the standings panel ----
local MAX_ROWS = 8
local panel = nil  -- { Root, Title, Rows = { { Rank, Name, Points, Delta } } }
local lastPoints = {}  -- player state address -> { Points, Delta, Since }

local function BuildPanel(hud)
    local box = UI.VBox(hud)
    local p = { Rows = {} }
    p.Title = UI.Text(hud, 19, UI.ORANGE, "COUPE")
    UI.AddRow(box, p.Title, { Left = 0, Top = 0, Right = 0, Bottom = 6 })
    for i = 1, MAX_ROWS do
        local line = UI.HBox(hud)
        local row = {
            Line = line,
            Rank = UI.Text(hud, 17, UI.DIM, ""),
            Name = UI.Text(hud, 17, UI.CREAM, ""),
            Points = UI.Text(hud, 17, UI.CREAM, ""),
            Delta = UI.Text(hud, 15, UI.GREEN, ""),
        }
        UI.AddCell(line, UI.Fixed(hud, row.Rank, 30))
        UI.AddCell(line, UI.Fixed(hud, row.Name, 230))
        UI.AddCell(line, UI.Fixed(hud, row.Points, 150))
        UI.AddCell(line, row.Delta)
        UI.AddRow(box, line, { Left = 0, Top = 1, Right = 0, Bottom = 1 })
        p.Rows[i] = row
    end
    p.Root = UI.Plate(hud, box, 0.45)
    UI.Place(hud, p.Root, { X = 0, Y = 0.5 }, { X = 0, Y = 0 }, { X = 32, Y = 50 })
    p.Hud = hud
    return p
end

-- Called periodically on players' games.
function Cup.ClientTick()
    local gs = FindFirstOf("PGGameplayGameState")
    local hud = UI.GameplayHud()
    local standings = (UI.Valid(gs) and hud) and Cup.Standings(gs) or {}
    if #standings == 0 then
        if panel then UI.Show(panel.Root, false) end
        lastPoints = {}
        return
    end
    if not (panel and UI.Owns(hud, panel.Root)) then panel = BuildPanel(hud) end
    UI.Show(panel.Root, true)
    local me = UI.LocalPlayerState()
    local myKey = me and me:GetAddress()
    UI.SetText(panel.Title, string.format("COUPE · finaliste à %d pts", standings[1].Limit))
    local now = os.clock()
    for i, row in ipairs(panel.Rows) do
        local s = standings[i]
        UI.Show(row.Line, s ~= nil)
        if s then
            local seen = lastPoints[s.Key]
            if not seen then
                seen = { Points = s.Points, Delta = 0, Since = 0 }
                lastPoints[s.Key] = seen
            elseif s.Points ~= seen.Points then
                seen.Delta, seen.Since, seen.Points = s.Points - seen.Points, now, s.Points
            end
            UI.SetText(row.Rank, tostring(i))
            UI.SetText(row.Name, UI.Short(s.Name, 15))
            UI.SetColour(row.Name, s.Key == myKey and UI.GOLD or UI.CREAM)
            if s.Winner then
                UI.SetText(row.Points, "VAINQUEUR")
                UI.SetColour(row.Points, UI.GOLD)
            elseif s.Finalist then
                UI.SetText(row.Points, "FINALISTE")
                UI.SetColour(row.Points, UI.ORANGE)
            else
                UI.SetText(row.Points, string.format("%d pts", s.Points))
                UI.SetColour(row.Points, UI.CREAM)
            end
            local fresh = seen.Delta > 0 and now - seen.Since < DELTA_SECONDS
            UI.SetText(row.Delta, fresh and string.format("+%d", seen.Delta) or "")
        end
    end
end

return Cup
