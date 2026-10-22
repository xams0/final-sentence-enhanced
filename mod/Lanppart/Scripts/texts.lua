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

-- texts: list of { Id, Title, Body, Difficulty } (Body: rounds separated by a "***" line).
function Texts.SetCustom(texts)
    custom = {}
    for _, t in ipairs(texts or {}) do
        -- The game's own texts: CRLF lines, rounds separated by a "***" line.
        local body = t.Body:gsub("\r\n", "\n"):gsub("\n", "\r\n")
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
