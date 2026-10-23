-- Pages. The typewriter draws the whole session text on one page: 81 rows of 52 characters, with a
-- blank row between rounds. Rows past the page are never drawn, and the writers can't type them.
-- A session text is therefore at most one page. With lives in K.O. and Duel, the server turns the page
-- once the writers alive are done with it: the rest of the text, then the next texts of the pool.
local Native = require("native")
local Texts = require("texts")

local Pages = {}

local PAGE_ROWS = 80  -- (of 81: one spare)
local TYPEMODE_KNOCKDOWN = 1
local TYPEMODE_DUEL = 2
local SESSION_GAMEPLAY = 4
local WS_AWAIT, WS_BETWEEN_ROUNDS, WS_EXECUTION = 4, 5, 7

-- Rounds of a text in the game format (CRLF lines, rounds separated by a "***" line), as lists of lines.
local function Rounds(body)
    local rounds, current = {}, {}
    for line in (body:gsub("\r\n", "\n") .. "\n"):gmatch("([^\n]*)\n") do
        if line:match("^%s*%*%*%*%s*$") then
            if #current > 0 then rounds[#rounds + 1] = current end
            current = {}
        else
            current[#current + 1] = line
        end
    end
    while #current > 0 and current[#current]:match("^%s*$") do current[#current] = nil end
    if #current > 0 then rounds[#rounds + 1] = current end
    return rounds
end

-- The text cut into page-sized texts, at round boundaries (a round taller than a page is cut too).
function Pages.Split(body)
    local pages, page, rows = {}, {}, 0
    local function Flush()
        if #page > 0 then pages[#pages + 1] = table.concat(page, "\r\n***\r\n") end
        page, rows = {}, 0
    end
    local function Add(lines)
        local needed = #lines + (#page > 0 and 1 or 0)
        if rows + needed > PAGE_ROWS then
            Flush()
            needed = #lines
        end
        page[#page + 1] = table.concat(lines, "\r\n")
        rows = rows + needed
    end
    for _, round in ipairs(Rounds(body)) do
        for first = 1, #round, PAGE_ROWS do
            Add(table.move(round, first, math.min(first + PAGE_ROWS - 1, #round), 1, {}))
        end
    end
    Flush()
    return pages
end

local paging = false  -- the hosted session turns pages
local queue = {}      -- its next pages: { Text, Id, Difficulty, Page, Pages }
local bag = {}        -- the next texts of the pool, drawn without repeats
local language, typeMode, currentId
local playing = {}    -- the page being played: { Id, Page, Pages }

local function Queue(text)
    local bodies = Pages.Split(text.Text)
    for i, body in ipairs(bodies) do
        queue[#queue + 1] = { Text = body, Id = text.Id, Difficulty = text.Difficulty, Page = i, Pages = #bodies }
    end
end

-- The text being played, its page and its number of pages (for the launcher), or nil.
function Pages.Playing()
    return playing.Id, playing.Page, playing.Pages
end

-- Server: the first page of the session text ({ Text, Id, Difficulty }); the rest waits in the queue
-- when the session turns pages, and is dropped otherwise.
function Pages.Start(text, lang, opts, lives)
    typeMode = opts.TypeMode
    paging = lives and (typeMode == TYPEMODE_KNOCKDOWN or typeMode == TYPEMODE_DUEL)
    language, currentId, queue, bag = lang, text.Id, {}, {}
    Queue(text)
    local first = table.remove(queue, 1) or text
    playing = { Id = text.Id, Page = 1, Pages = paging and #queue + 1 or 1 }
    if not paging and #queue > 0 then
        print(string.format("[Lanppart] text %d is longer than a page: only its first page is played\n", text.Id))
        queue = {}
    elseif #queue > 0 then
        print(string.format("[Lanppart] text %d: %d pages\n", text.Id, #queue + 1))
    end
    return first
end

-- The next text of the pool, avoiding the one just played when there is a choice.
local function NextText(pool)
    if #bag == 0 then
        for i, id in ipairs(pool) do bag[i] = id end
        for i = #bag, 2, -1 do
            local j = math.random(i)
            bag[i], bag[j] = bag[j], bag[i]
        end
        if #bag > 1 and bag[#bag] == currentId then bag[1], bag[#bag] = bag[#bag], bag[1] end
    end
    local id = table.remove(bag)
    return id and Texts.Get(language, id)
end

local function TurnPage(gm, pool)
    if #queue == 0 then
        local text = NextText(pool(typeMode) or {})
        if not text then
            print("[Lanppart] no text for the next page\n")
            paging = false
            return
        end
        currentId = text.Id
        Queue(text)
    end
    local page = table.remove(queue, 1)
    playing = { Id = page.Id, Page = page.Page, Pages = page.Pages }
    gm:OnGetRandomText({ bSuccessful = true, ErrCode = 200, ErrMsg = "" },
        { Text = page.Text, text_id = page.Id, text_difficulty = page.Difficulty })
    Native.NewPage()
    print(string.format("[Lanppart] page turned: text %d\n", page.Id))
end

-- Server, called periodically: turns the page once every writer alive has finished its last round
-- (waiting for the others, or between rounds after a lost life). pool(typeMode): the text ids to go on with.
function Pages.ServerTick(gm, gs, pool)
    if not paging or gs.SessionState ~= SESSION_GAMEPLAY then return end
    local pageEnd = 0
    gs.TextLensPerRound:ForEach(function(_, v) pageEnd = v:get() end)
    if pageEnd <= 0 then return end
    local alive, done = 0, true
    gs.PlayerArray:ForEach(function(_, p)
        local ps = p:get()
        if not (ps:IsValid() and ps:IsA("/Script/TheEnd.PGGameplayPlayerState")) or ps.WriterId < 0 then return end
        local state = ps.WriterState
        if state < 2 or state >= WS_EXECUTION then return end
        alive = alive + 1
        if not ((state == WS_AWAIT or state == WS_BETWEEN_ROUNDS) and ps.Carriage >= pageEnd) then done = false end
    end)
    -- (A last writer alive wins instead: lanppart_server_tick.)
    if alive >= 2 and done then TurnPage(gm, pool) end
end

-- Players: when the session text changes during a game, the typewriter keeps its page: clear it and
-- draw the new text.
local function Redraw()
    local krl = StaticFindObject("/Script/Engine.Default__KismetRenderingLibrary")
    for _, tw in ipairs(FindAllOf("bp_typewriter_C") or {}) do
        if tw:IsValid() then
            for _, name in ipairs({ "TextPage_All", "TextPage_Typed", "TextPage_MistakeTicks" }) do
                local page = tw[name]
                if page and page:IsValid() and page.CanvasRenderTarget:IsValid() then
                    krl:ClearRenderTarget2D(tw, page.CanvasRenderTarget, page.ClearColor)
                end
            end
            tw.CarriagePosX = 0
            tw.CarriagePosY = 0
            tw.PrevX = 0
            tw.PrevY = 0
            tw.UILine = 0
            tw.UILine_Source = 0
            tw.UILine_Target = 0
            tw.AsyncRenderIndex = 0
            tw._LastRowErrors = 0
            tw:OnOriginalTextIsReady()
        end
    end
    print("[Lanppart] new page drawn\n")
end

local seenState, seenText = nil, nil
local redrawIn = 0

-- Called periodically on players' games. (The redraw waits a tick for the server's carriage reset.)
function Pages.ClientTick()
    local gs = FindFirstOf("PGGameplayGameState")
    if not (gs and gs:IsValid()) then
        seenState, seenText, redrawIn = nil, nil, 0
        return
    end
    local text = gs.TextToType:ToString()
    if gs:GetAddress() ~= seenState then
        seenState, seenText, redrawIn = gs:GetAddress(), text, 0
        return
    end
    if text ~= seenText then
        -- (The first text of a game is drawn by the game itself.)
        if seenText ~= "" and text ~= "" then redrawIn = 2 end
        seenText = text
    end
    if redrawIn > 0 then
        redrawIn = redrawIn - 1
        if redrawIn == 0 then Redraw() end
    end
end

return Pages
