-- Picks the text to type. The server-side backend call is compiled out of the client build, so the host
-- uses its own text database and the text selection saved by the game's lobby screen.
local Texts = {}

local function ModDir()
    local source = debug.getinfo(1, "S").source:gsub("^@", ""):gsub("\\", "/")
    return source:match("^(.*)/Scripts/[^/]+$")
end

local function ToNumber(v)
    if type(v) == "number" then return v end
    return v:get()
end

-- Returns the language and text ids saved in the player's lobby options for a type mode.
-- (Iterating the text database map through UE4SS intermittently crashes the game; this doesn't.)
function Texts.SavedSelection(typeMode)
    local sg = FindFirstOf("PGSaveGame")
    if not (sg and sg:IsValid()) then return nil, {} end
    local opts = {}
    sg:GetGameplayOptionsForMode(typeMode, opts)
    local ids = {}
    for i = 1, #(opts.TextIds or {}) do ids[#ids + 1] = ToNumber(opts.TextIds[i]) end
    local lang = opts.Language and opts.Language:ToString() or ""
    if lang == "" then lang = nil end
    return lang, ids
end

function Texts.Language(typeMode)
    local lang = Texts.SavedSelection(typeMode)
    if lang then return lang end
    local gi = FindFirstOf("PGGameInstance")
    lang = gi:GetDesiredInputLanguage():ToString()
    if lang == "" then lang = gi:GetCurrentKeyboardLanguage():ToString() end
    if lang == "" then lang = "en" end
    return lang
end

-- Returns a random text id from the saved selection for the type mode, or nil.
function Texts.RandomId(typeMode)
    local _, ids = Texts.SavedSelection(typeMode)
    if #ids == 0 then return nil end
    return ids[math.random(#ids)]
end

-- Lists the game's texts for the launcher (%APPDATA%\Lanppart3\catalog.txt). True once done.
function Texts.DumpCatalog()
    local db = FindFirstOf("PGTextsDatabaseSaveGame")
    local appdata = os.getenv("APPDATA")
    if not (db and db:IsValid() and appdata) then return false end
    local id = os.getenv("LANPPART3_ID")
    local f = io.open(string.format("%s/lanppart_request_%s.txt", ModDir(), (id and id ~= "") and id or "game"), "w")
    if not f then return false end
    f:write(string.format("%x\n%s\\Lanppart3\\catalog.txt\n", db:GetAddress(), appdata))
    f:close()
    require("native").DumpTexts()
    return true
end

-- Custom texts: id -> { Text, Id, Difficulty, Title }. Only the server needs them: the session text is
-- replicated to the players, who never see it before the game starts.
local custom = {}

-- The typewriter only returns at the text's line breaks and the screen has room for about 50 characters:
-- the game's own lines are short (99% at most 49 characters). Longer lines are cut between words.
local MAX_LINE = 48

local function Wrap(line)
    if utf8.len(line) == nil or utf8.len(line) <= MAX_LINE then return { line } end
    local lines, current = {}, ""
    for word in line:gmatch("%S+") do
        while utf8.len(word) > MAX_LINE do  -- (a word longer than a line: cut it)
            if current ~= "" then lines[#lines + 1] = current current = "" end
            local cut = utf8.offset(word, MAX_LINE + 1)
            lines[#lines + 1] = word:sub(1, cut - 1)
            word = word:sub(cut)
        end
        if current == "" then
            current = word
        elseif utf8.len(current) + 1 + utf8.len(word) <= MAX_LINE then
            current = current .. " " .. word
        else
            lines[#lines + 1] = current
            current = word
        end
    end
    if current ~= "" then lines[#lines + 1] = current end
    return lines
end

-- Characters no keyboard types (the game's own texts never use them), and what to type instead.
local UNTYPEABLE = {
    ["•"] = "-", ["·"] = "-", ["–"] = "-", ["—"] = "-", ["…"] = "...",
    ["«"] = "\"", ["»"] = "\"", ["“"] = "\"", ["”"] = "\"", ["‘"] = "'", ["’"] = "'",
    ["œ"] = "oe", ["Œ"] = "OE", ["æ"] = "ae", ["Æ"] = "AE",
    ["\u{A0}"] = " ", ["\u{202F}"] = " ",
}

-- Game format: CRLF lines of at most MAX_LINE characters, rounds separated by a "***" line.
local function GameFormat(body)
    local out = {}
    for from, to in pairs(UNTYPEABLE) do body = body:gsub(from, to) end
    for line in (body:gsub("\r\n", "\n") .. "\n"):gmatch("([^\n]*)\n") do
        line = line:gsub("%s+$", "")
        if line == "" then
            -- (blank lines would be lines to type: dropped)
        else
            for _, l in ipairs(Wrap(line)) do out[#out + 1] = l end
        end
    end
    return table.concat(out, "\r\n")
end
Texts.GameFormat = GameFormat

-- texts: list of { Id, Title, Body, Difficulty } (Body: rounds separated by a "***" line).
function Texts.SetCustom(texts)
    custom = {}
    for _, t in ipairs(texts or {}) do
        local body = GameFormat(t.Body)
        custom[t.Id] = { Text = body, Id = t.Id, Difficulty = t.Difficulty or 1, Title = t.Title }
    end
end

-- Returns { Text, Id, Difficulty } for the id, or nil.
function Texts.Get(lang, id)
    if custom[id] then return custom[id] end
    local gi = FindFirstOf("PGGameInstance")
    local t = {}
    gi:GetTextDataByIdAndLanguage(lang, id, t)
    local body = t.Body and t.Body:ToString() or ""
    if body == "" then return nil end
    return { Text = body, Id = id, Difficulty = t.DifficultyLevel }
end

return Texts
