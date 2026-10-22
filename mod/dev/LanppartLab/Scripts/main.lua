-- Dev-only REPL: executes Lua chunks dropped in lab_cmd_<LAB_ID>.lua, output goes to lab_out_<LAB_ID>.txt
-- (in LAB_DIR, else the current folder). Driven by _work/lab.sh.
local LAB_ID = os.getenv("LAB_ID") or "host"
local DIR = os.getenv("LAB_DIR") and (os.getenv("LAB_DIR") .. "\\") or ""
local CMD, OUT = DIR .. "lab_cmd_" .. LAB_ID .. ".lua", DIR .. "lab_out_" .. LAB_ID .. ".txt"

local function Out(...)
    local parts = {}
    for i = 1, select("#", ...) do parts[#parts + 1] = tostring(select(i, ...)) end
    local f = io.open(OUT, "a")
    if f then f:write(table.concat(parts, "\t") .. "\n") f:close() end
end
_G.Out = Out

LoopAsync(500, function()
    local f = io.open(CMD, "r")
    if not f then return false end
    local code = f:read("*a")
    f:close()
    os.remove(CMD)
    ExecuteInGameThread(function()
        local chunk, err = load(code, "lab")
        if not chunk then Out("COMPILE ERROR: " .. tostring(err)) Out("<<END>>") return end
        local ok, res = pcall(chunk)
        if not ok then Out("RUNTIME ERROR: " .. tostring(res)) end
        Out("<<END>>")
    end)
    return false
end)
