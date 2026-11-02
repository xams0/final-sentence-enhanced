-- Observer mode. Dead, a player could only look around from their own chair (the game's "Mode
-- Spectateur", reached through the results screen). Now, a few seconds after their death, they follow the
-- writers still in the game: a camera over the watched writer's shoulder, a warm light on their desk (the
-- room is dark away from one's own lamp), and a panel with their lives, round, progress, mistakes and the
-- line they are typing, live.
--   Left / Right arrows: previous / next writer.  Space: free view from one's own chair (and back).
-- The other writers' typewriters are not drawn on a player's game (only their own is): the line being typed
-- comes from the session text and the writer's replicated carriage.
local UI = require("ui")
local Hearts = require("hearts")
local Cup = require("cup")
local Host = require("host")

local Observe = {}

local SESSION_GAMEPLAY = 4
local WS_WAITING, WS_ROULETTE, WS_EXECUTION, WS_DEAD = 1, 6, 7, 8
local AUTO_SECONDS = 6       -- after one's death, before following the others
local CLICK_SECONDS = 3      -- between "Proceed" and "Observer" (the results screen's page change plays meanwhile)
local GIVE_UP_SECONDS = 20   -- ... and how long to try
local NEXT_SECONDS = 4       -- after the watched writer's death, before following another one
local BLEND_SECONDS = 0.6
-- In a writer's frame (from its pawn: X forward, Y right, Z up): the camera behind and above the shoulder,
-- the typewriter's paper it looks at, and the light over the desk. (Every seat is the same.)
local CAMERA_OFFSET = { X = -140, Y = 30, Z = 60 }
local PAPER_OFFSET = { X = 13, Y = -4, Z = -54 }
local LIGHT_OFFSET = { X = -10, Y = 10, Z = 20 }
local LIGHT_INTENSITY = 2600
local MOBILITY_MOVABLE = 2
local FIELD_OF_VIEW = 80
local STATES = {
    [1] = "attend", [2] = "écrit", [3] = "corrige une faute", [4] = "a fini la manche", [5] = "entre deux manches",
    [6] = "roulette !", [7] = "exécution", [8] = "éliminé(e)", [9] = "a terminé",
}

local cameras = {}       -- two camera actors: switching writers blends from one to the other
local cameraIndex = 1
local light = nil
local watched = nil      -- address of the watched writer's player state
local watchedDeadSince = nil
local freeView = false
local deadSince = nil
local spectateAsked = false
local proceededAt = nil
local observing = false  -- Observe.Fast updates the view and the panel
local panel = nil
local orders = {}        -- keys pressed since the last update: "Next", "Previous", "Free"

local function Now() return os.clock() end

-- Keys. (UE4SS key binds watch the keyboard itself, whichever window has the focus: only orders given
-- while observing count.)
local function Order(name)
    return function()
        if observing then orders[#orders + 1] = name end
    end
end
RegisterKeyBind(Key.RIGHT_ARROW, Order("Next"))
RegisterKeyBind(Key.LEFT_ARROW, Order("Previous"))
RegisterKeyBind(Key.SPACE, Order("Free"))

-- The writer pawns of the map, by player state address.
local function Pawns()
    local pawns = {}
    for _, w in ipairs(FindAllOf("PGPlayableWriter") or {}) do
        local ps = w:IsValid() and w.PlayerState
        if UI.Valid(ps) then pawns[ps:GetAddress()] = w end
    end
    return pawns
end

local function InGame(ps) return ps.WriterState >= WS_WAITING and ps.WriterState <= WS_ROULETTE end

-- The writers one can follow (still in the game, oneself excepted), in seat order.
local function Candidates(gs, me)
    local list = {}
    for _, ps in ipairs(UI.Writers(gs)) do
        if ps:GetAddress() ~= me:GetAddress() and InGame(ps) then list[#list + 1] = ps end
    end
    table.sort(list, function(a, b) return a.WriterId < b.WriterId end)
    return list
end

local function Leader(list)
    local best
    for _, ps in ipairs(list) do
        if not best or ps.Round > best.Round or (ps.Round == best.Round and ps.Carriage > best.Carriage) then best = ps end
    end
    return best
end

local function Find(gs, key)
    for _, ps in ipairs(UI.Writers(gs)) do
        if ps:GetAddress() == key then return ps end
    end
end

local function InWorld(offset, origin, yaw)
    local a = math.rad(yaw)
    return { X = origin.X + offset.X * math.cos(a) - offset.Y * math.sin(a),
             Y = origin.Y + offset.X * math.sin(a) + offset.Y * math.cos(a), Z = origin.Z + offset.Z }
end

local function Spawn(pc, class)
    local statics = StaticFindObject("/Script/Engine.Default__GameplayStatics")
    local xf = { Rotation = { X = 0, Y = 0, Z = 0, W = 1 }, Translation = { X = 0, Y = 0, Z = 0 }, Scale3D = { X = 1, Y = 1, Z = 1 } }
    local actor = statics:BeginDeferredActorSpawnFromClass(pc, StaticFindObject(class), xf, 1, nil, 0)
    if not UI.Valid(actor) then return nil end
    return statics:FinishSpawningActor(actor, xf, 0)
end

local function Camera(pc, i)
    local cam = cameras[i]
    if UI.Valid(cam) then return cam end
    cam = Spawn(pc, "/Script/Engine.CameraActor")
    if not cam then return nil end
    cam.CameraComponent:SetFieldOfView(FIELD_OF_VIEW)
    cameras[i] = cam
    return cam
end

local function Light(pc)
    if UI.Valid(light) then return light end
    light = Spawn(pc, "/Script/Engine.PointLight")
    if not light then return nil end
    local c = light.PointLightComponent
    c:SetMobility(MOBILITY_MOVABLE)  -- (a light actor is static: it would stay where it was spawned)
    c:SetIntensity(LIGHT_INTENSITY)
    c:SetAttenuationRadius(250)
    c:SetLightColor({ R = 1.0, G = 0.75, B = 0.45, A = 1.0 }, true)
    return light
end

local function ShowLight(shown)
    if UI.Valid(light) then light.PointLightComponent:SetVisibility(shown, false) end
end

local function IsOurCamera(actor)
    if not UI.Valid(actor) then return false end
    for _, cam in pairs(cameras) do
        if UI.Valid(cam) and cam:GetAddress() == actor:GetAddress() then return true end
    end
    return false
end

-- Moves the view over the writer's shoulder (blending from the previous view), and the light to their desk.
local function LookAt(pc, ps, pawns)
    local pawn = pawns[ps:GetAddress()]
    if not UI.Valid(pawn) then return false end
    cameraIndex = 3 - cameraIndex
    local cam = Camera(pc, cameraIndex)
    if not cam then return false end
    local origin, yaw = pawn:K2_GetActorLocation(), pawn:K2_GetActorRotation().Yaw
    local at, paper = InWorld(CAMERA_OFFSET, origin, yaw), InWorld(PAPER_OFFSET, origin, yaw)
    cam:K2_SetActorLocation(at, false, {}, false)
    cam:K2_SetActorRotation(StaticFindObject("/Script/Engine.Default__KismetMathLibrary"):FindLookAtRotation(at, paper), false)
    pc:SetViewTargetWithBlend(cam, BLEND_SECONDS, 0, 0, false)
    local lamp = Light(pc)
    if lamp then
        lamp:K2_SetActorLocation(InWorld(LIGHT_OFFSET, origin, yaw), false, {}, false)
        ShowLight(true)
    end
    return true
end

local function OwnView(pc, blend)
    ShowLight(false)
    if IsOurCamera(pc:GetViewTarget()) and UI.Valid(pc.Pawn) then pc:SetViewTargetWithBlend(pc.Pawn, blend or 0, 0, 0, false) end
end

-- ---- Panel, bottom centre ----
local function BuildPanel(hud)
    local box = UI.VBox(hud)
    local p = { Hud = hud }
    p.Title = UI.Text(hud, 15, UI.DIM, "OBSERVATION")
    UI.AddRow(box, p.Title)
    local head = UI.HBox(hud)
    p.Name = UI.Text(hud, 26, UI.CREAM, "")
    UI.AddCell(head, p.Name)
    p.Hearts = Hearts.NewRow(hud.WidgetTree, 20)
    UI.AddCell(head, p.Hearts, { Left = 14, Top = 0, Right = 0, Bottom = 0 })
    UI.AddRow(box, head, { Left = 0, Top = 2, Right = 0, Bottom = 2 })
    p.Info = UI.Text(hud, 17, UI.DIM, "")
    UI.AddRow(box, p.Info)
    local line = UI.HBox(hud)
    p.Typed = UI.Text(hud, 22, UI.GREEN, "")
    p.Rest = UI.Text(hud, 22, UI.DIM, "")
    UI.AddCell(line, p.Typed)
    UI.AddCell(line, p.Rest)
    UI.AddRow(box, line, { Left = 0, Top = 8, Right = 0, Bottom = 4 })
    p.Hint = UI.Text(hud, 14, UI.DIM, "Flèches gauche / droite : changer de joueur  ·  Espace : vue libre  ·  Échap : quitter")
    UI.AddRow(box, p.Hint, { Left = 0, Top = 6, Right = 0, Bottom = 0 })
    p.Root = UI.Plate(hud, box, 0.55)
    UI.Place(hud, p.Root, { X = 0.5, Y = 1 }, { X = 0.5, Y = 1 }, { X = 0, Y = -36 })
    UI.Show(p.Root, false)
    return p
end

-- The line around the writer's carriage (typed part, part left), their round, the rounds of the page, and
-- their progress in the round (%).
local function CurrentLine(gs, ps)
    local lens = {}
    gs.TextLensPerRound:ForEach(function(_, v) lens[#lens + 1] = v:get() end)
    local round = math.max(0, math.min(ps.Round, #lens - 1))
    local first, last = round > 0 and lens[round] or 0, lens[round + 1] or 0
    local text = gs.TextToType:ToString()
    local chars = utf8.len(text) or #text
    local car = math.max(first, math.min(ps.Carriage, last, chars))
    local function Byte(i) return utf8.offset(text, i + 1) or (#text + 1) end
    local roundStart, roundEnd, at = Byte(first), Byte(last), Byte(car)
    local lineStart = roundStart
    for i = at - 1, roundStart, -1 do
        if text:byte(i) == 10 then
            lineStart = i + 1
            break
        end
    end
    local lineEnd = roundEnd - 1
    local nl = text:find("\n", at, true)
    if nl and nl <= lineEnd then lineEnd = nl - 1 end
    local progress = last > first and math.floor((car - first) * 100 / (last - first) + 0.5) or 0
    return text:sub(lineStart, at - 1), text:sub(at, lineEnd), round + 1, #lens, progress
end

local function Fill(gs, ps, cup)
    local p = panel
    UI.SetText(p.Title, freeView and "OBSERVATION · vue libre (Espace pour revenir)" or "OBSERVATION")
    UI.SetText(p.Name, "<   " .. UI.Name(ps) .. "   >")
    local lives, total = Hearts.LivesOf(ps)
    local showHearts = Host.LivesMode and not cup
    Hearts.Show(p.Hearts, showHearts)
    if showHearts then Hearts.Set(p.Hearts, lives, total, true) end
    local typed, rest, round, rounds, progress = CurrentLine(gs, ps)
    local parts = { string.format("Manche %d / %d", round, rounds), progress .. " %",
                    ps.Mistakes .. (ps.Mistakes == 1 and " faute" or " fautes"), STATES[ps.WriterState] or "" }
    local standing = cup and Cup.Decode(ps)
    if standing then
        table.insert(parts, 1, standing.Winner and (standing.Winners > 1 and "VAINQUEUR " .. Cup.PlaceText(standing.Place) or "VAINQUEUR") or standing.Finalist and "FINALISTE" or (standing.Points .. " pts"))
    end
    UI.SetText(p.Info, table.concat(parts, "  ·  "))
    UI.SetText(p.Typed, typed)
    UI.SetText(p.Rest, rest)
end

-- ---- Following ----
local function Switch(list, step)
    if #list == 0 then return nil end
    local index = 0
    for i, ps in ipairs(list) do
        if ps:GetAddress() == watched then index = i end
    end
    if index == 0 then return Leader(list) end
    return list[(index - 1 + step) % #list + 1]
end

local function Stop()
    observing = false
    watched, watchedDeadSince, freeView = nil, nil, false
    orders = {}
    local _, pc = UI.LocalPlayerState()
    if pc then OwnView(pc, 0) else ShowLight(false) end
    if panel then UI.Show(panel.Root, false) end
end

-- One update while observing (every 100 ms). Returns false once there is nothing to observe.
local function Update()
    local gs = FindFirstOf("PGGameplayGameState")
    local me, pc = UI.LocalPlayerState()
    if not (UI.Valid(gs) and me and pc and gs.SessionState == SESSION_GAMEPLAY and me.bIsSpectating) then return false end
    local hud = UI.GameplayHud()
    if not hud then return false end
    if not (panel and UI.Owns(hud, panel.Root)) then panel = BuildPanel(hud) end
    local list = Candidates(gs, me)
    local function Follow(ps)
        if not ps then return end
        watched, watchedDeadSince = ps:GetAddress(), nil
        if not freeView then LookAt(pc, ps, Pawns()) end
    end
    local current = watched and Find(gs, watched)
    -- The watched writer was eliminated: stay on them a moment (the execution), then follow another one.
    if current and not InGame(current) then
        watchedDeadSince = watchedDeadSince or Now()
        if Now() - watchedDeadSince >= NEXT_SECONDS then current = nil end
    end
    if not current then
        if #list == 0 then
            UI.Show(panel.Root, false)
            return true
        end
        Follow(Leader(list))
    end
    local pending = orders
    orders = {}
    for _, order in ipairs(pending) do
        if order == "Next" then
            Follow(Switch(list, 1))
        elseif order == "Previous" then
            Follow(Switch(list, -1))
        elseif order == "Free" then
            freeView = not freeView
            if freeView then OwnView(pc, BLEND_SECONDS) else Follow(Find(gs, watched)) end
        end
    end
    current = watched and Find(gs, watched)
    if not current then return true end
    UI.Show(panel.Root, true)
    Fill(gs, current, Cup.Active(gs))
    return true
end

local function StartObserving()
    if observing then return end
    observing = true
    print("[Lanppart] observer mode\n")
end

-- Called every 100 ms on the game thread (main.lua).
function Observe.Fast()
    if not observing then return end
    local ok, result = pcall(Update)
    if not ok then print("[Lanppart] observer: " .. tostring(result) .. "\n") end
    if not ok or not result then Stop() end
end

-- One step towards the game's own spectator mode, the way its results screen goes there: standings,
-- "Proceed", then "Observer". (The screen ignores it until it is shown: called again on the next ticks.)
local function SpectateStep(pc)
    local results = pc.MatchResultWidget
    if not UI.Valid(results) then
        pc:StartSpectating()
    elseif results.IsInStandingsMode then
        results:ForceProceedToPlayerResult()
        proceededAt = Now()
    elseif results.IsInPlayerResultMode and Now() - (proceededAt or 0) >= CLICK_SECONDS then
        results:BndEvt__wbp_matchresult_BUT_PlayerResult_Spectate_K2Node_ComponentBoundEvent_2_CommonButtonBaseClicked__DelegateSignature(results.BUT_PlayerResult_Spectate)
        print("[Lanppart] dead: observing the writers left\n")
    end
end

-- Called periodically on players' games.
function Observe.Tick()
    local gs = FindFirstOf("PGGameplayGameState")
    local me, pc = UI.LocalPlayerState()
    if not (UI.Valid(gs) and me and pc and gs.SessionState == SESSION_GAMEPLAY) then
        deadSince, spectateAsked, proceededAt = nil, false, nil
        if observing then Stop() end
        return
    end
    if me.WriterState == WS_DEAD or me.WriterState == WS_EXECUTION then
        deadSince = deadSince or Now()
    else
        deadSince, spectateAsked = nil, false
    end
    if me.bIsSpectating then
        spectateAsked = true
        StartObserving()
    elseif deadSince and not spectateAsked and #Candidates(gs, me) > 0
            and pc:GetClass():GetFName():ToString() == "pc_gameplay_C" then
        local dead = Now() - deadSince
        if dead >= AUTO_SECONDS then SpectateStep(pc) end
        if dead >= AUTO_SECONDS + GIVE_UP_SECONDS then spectateAsked = true end  -- (left on the results screen)
    end
end

return Observe
