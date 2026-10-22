-- The lobby presets added to the mode list ("Lanppart Poules", "Lanppart Finales"): complete rule sets
-- edited in the launcher, which writes %APPDATA%\Lanppart3\presets.lua. Read when the game starts.
--
-- A lobby made with a preset carries it in BotsCount (never a real count): the members' windows show the
-- preset, and the host gets the preset's bots back (Presets.Marker / Presets.FromMarker).
local Presets = {}

local TYPEMODE_BATTLEROYAL = 0
local TYPEMODE_DUEL = 2
local MARKER_BASE = -1000
local LEGACY_MARKER = -2  -- V1/V2 lobbies: Lanppart Poules / Finales without bots

local DEFAULTS = {
    { Key = "poules", Name = "Lanppart Poules", Mode = TYPEMODE_BATTLEROYAL, Roulette = false, Lives = 3,
      BulletSlots = 6, InitialBullets = 1, MaxMistakesPerRound = 3, Bots = 0, BotsDifficulty = 2,
      MistakeBehaviour = 1, CapitalsMatter = false, RoundDuration = 240, PauseDuration = 10 },
    { Key = "finales", Name = "Lanppart Finales", Mode = TYPEMODE_DUEL, Roulette = false, Lives = 3,
      BulletSlots = 6, InitialBullets = 1, MaxMistakesPerRound = 3, Bots = 0, BotsDifficulty = 2,
      MistakeBehaviour = 1, CapitalsMatter = false, RoundDuration = 120, PauseDuration = 10 },
}

local function Clamp(v, lo, hi, default)
    v = tonumber(v)
    if not v then return default end
    return math.max(lo, math.min(hi, math.floor(v)))
end

-- A preset from the launcher's file, checked field by field (the defaults fill the gaps).
local function Checked(p, default)
    p = type(p) == "table" and p or {}
    local mode = Clamp(p.Mode, 0, 2, default.Mode)
    local slots = Clamp(p.BulletSlots, 1, 6, default.BulletSlots)
    return {
        Key = default.Key,
        Name = (type(p.Name) == "string" and p.Name ~= "") and p.Name or default.Name,
        Mode = mode,
        Roulette = p.Roulette == true,
        Lives = Clamp(p.Lives, 1, 6, default.Lives),
        BulletSlots = slots,
        InitialBullets = Clamp(p.InitialBullets, 0, slots - 1, 0),
        MaxMistakesPerRound = Clamp(p.MaxMistakesPerRound, 1, 10, default.MaxMistakesPerRound),
        Bots = Clamp(p.Bots, -1, 39, default.Bots),  -- -1: fill the free places
        BotsDifficulty = Clamp(p.BotsDifficulty, 0, 4, default.BotsDifficulty),
        MistakeBehaviour = Clamp(p.MistakeBehaviour, 0, 2, default.MistakeBehaviour),
        CapitalsMatter = p.CapitalsMatter == true,
        RoundDuration = Clamp(p.RoundDuration, 10, 3600, default.RoundDuration),
        PauseDuration = Clamp(p.PauseDuration, 0, 600, default.PauseDuration),
    }
end

local function Load()
    local loaded = {}
    local appdata = os.getenv("APPDATA")
    local path = appdata and (appdata .. "\\Lanppart3\\presets.lua")
    local chunk = path and loadfile(path, "t", {})
    if chunk then
        local ok, result = pcall(chunk)
        if ok and type(result) == "table" then loaded = result end
    end
    local list = {}
    for i, default in ipairs(DEFAULTS) do list[i] = Checked(loaded[i], default) end
    return list
end

Presets.List = Load()

-- BotsCount of a lobby made with preset number index.
function Presets.Marker(index)
    return MARKER_BASE - index * 100 - (Presets.List[index].Bots + 1)
end

-- Preset number and bots of a lobby's BotsCount, or nil when it isn't a preset lobby.
function Presets.FromMarker(botsCount, typeMode)
    if botsCount == LEGACY_MARKER then
        return typeMode == TYPEMODE_DUEL and 2 or 1, 0
    end
    if not botsCount or botsCount > MARKER_BASE - 100 then return nil end
    local x = MARKER_BASE - botsCount
    local index = x // 100
    if not Presets.List[index] then return nil end
    return index, x % 100 - 1
end

-- Writes the preset's rules into lobby options (UE struct, in place).
function Presets.Stamp(opts, index)
    local p = Presets.List[index]
    opts.bIsPrivate = true
    opts.TypeMode = p.Mode
    opts.BotsCount = Presets.Marker(index)
    opts.BotsDifficulty = p.BotsDifficulty
    opts.MistakeBehaviour = p.MistakeBehaviour
    opts.bCapitalsMatter = p.CapitalsMatter
    opts.bPlayRoulette = p.Roulette
    opts.BulletSlotsNum = p.Roulette and p.BulletSlots or p.Lives
    opts.InitialBulletsNum = p.Roulette and p.InitialBullets or 0
    opts.RoundDuration = p.RoundDuration
    if p.Mode == TYPEMODE_BATTLEROYAL then
        opts.MaxMistakesPerRound = p.MaxMistakesPerRound
        opts.PauseDuration = p.PauseDuration
    end
end

return Presets
