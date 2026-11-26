-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-224 synthetic test, against the real kdcmp.lua under MoonSharp (harness from Test-WO154Synthetic.lua).
--   (a) defaults: every switch of the work order on and registered; nothing errors at load
--   (b) C, before the agent has said anything: the build is "not checked yet" -- never "Official"
--   (c) C, the agent's word: a signed build reads "Official build <version>", logged once; an unsigned one, a bad version, a word
--       without the flag read "Unofficial build -- not made by Kingdom Come: Together"
--   (d) C, the mod menu draws the line under its title while it is open, on the right of the screen like the rest
--   (e) the switches: mp_w224_status names every one and the build's line; the agent's list sets them
-- What this proves: the Lua half's words. The check itself (the signature, the files) is the agent's (Wo224OfficialTests.cs); the
-- clinch and chain mechanisms are the DLL's and the bench's.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; UICALLS = {}; CMDS = {}; CCMDS = {}; DRAWS = {}; LABELS = {}
CVARS = { wh_pl_FastTravelEnabled = 1 }

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetCVar = function(n) return CVARS[n] end
System.SetCVar = function(n, v) CVARS[n] = tonumber(v) end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return {} end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.DrawText = function(x, y, text, size) DRAWS[#DRAWS + 1] = { x = x, y = y, text = tostring(text), size = size } end
System.DrawLabel = function(pos, size, text) LABELS[#LABELS + 1] = tostring(text) end
System.GetViewport = function() return { x = 0, y = 0, width = 1920, height = 1080 } end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) UICALLS[#UICALLS + 1] = { panel = panel, fn = fn, text = tostring(text) } end
Calendar = { GetWorldTime = function() return 12345 end, SetWorldTime = function() end }

P = { dialog = false, dead = false, ko = false, stood = 0 }
player = { id = 1, GetName = function(self) return "Dude" end,
           GetWorldPos = function() return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return P.dead end,
                     IsUnconscious = function() return P.ko end, StandUp = function() P.stood = P.stood + 1 end },
           human = { IsInDialog = function() return P.dialog end, IsMounted = function() return false end },
           soul = { GetState = function() return 100 end } }

-- The engine's Player class table, so kdcmp.lua installs its OnAction hooks over "the game's own handler".
GAME_ONACTION = 0
Player = { Client = { OnAction = function(...) GAME_ONACTION = GAME_ONACTION + 1 end },
           OnAction = function(...) GAME_ONACTION = GAME_ONACTION + 1 end }

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return table.unpack(r)
end

-- @@KDCMP@@

-- Part 2: scenarios.

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function logCount(pat, from)
    local n = 0
    for i = (from or 0) + 1, #LOG do if string.find(LOG[i], pat, 1, true) then n = n + 1 end end
    return n
end
local function lastLog(pat)
    for i = #LOG, 1, -1 do if string.find(LOG[i], pat, 1, true) then return LOG[i] end end
    return nil
end
local function drawn(pat)
    for _, d in ipairs(DRAWS) do if string.find(d.text, pat, 1, true) then return d end end
    return nil
end
local function frame(dt)
    NOW = NOW + (dt or 0.016)
    local due = TIMERS
    TIMERS = {}
    for _, t in ipairs(due) do
        if NOW - t.at >= (t.ms / 1000) - 1e-6 then pcall(t.f) else TIMERS[#TIMERS + 1] = t end
    end
end
local function key(action)
    Player.OnAction(nil, action, "press", 1)
    Player.OnAction(nil, action, "release", 0)
end
local UNOFFICIAL = "Unofficial build -- not made by Kingdom Come: Together"
local NAMES = { "w224_clinch", "w224_chains", "w224_death", "w224_clinch_log", "w224_official" }

-- (a) defaults
do
    local all = true
    for _, n in ipairs(NAMES) do if not KCD2MP_W224On(n) then all = false end end
    check("a: every switch is on by default", all)
    local reg = CCMDS["mp_w224_status"] ~= nil
    for _, n in ipairs(NAMES) do if CCMDS["mp_" .. n] == nil then reg = false end end
    check("a: mp_w224_status and the five switches are registered", reg)
    check("a: no Lua errors at load", #ERRS == 0, ERRS[1])
end

-- (b) before the agent's word
do
    local t = KCD2MP_W224OfficialText()
    check("b: before the agent has said it, the build is 'not checked yet'", string.find(t, "not checked yet", 1, true) ~= nil, t)
    check("b: ...and never 'Official'", string.find(t, "Official build", 1, true) == nil, t)
end

-- (c) the agent's word
do
    local mark = #LOG
    check("c: the agent's word is taken", KCD2MP_W224Official(1, "0.65.5", "signed by the project's key  every file as signed") == true)
    check("c: a signed build reads 'Official build 0.65.5'", KCD2MP_W224OfficialText() == "Official build 0.65.5", KCD2MP_W224OfficialText())
    check("c: ...logged once", logCount("W224-OFFICIAL Official build 0.65.5", mark) == 1, logCount("W224-OFFICIAL", mark))
    KCD2MP_W224Official(1, "0.65.5", "signed by the project's key  every file as signed")
    check("c: ...and not again for the same word", logCount("W224-OFFICIAL", mark) == 1, logCount("W224-OFFICIAL", mark))
    KCD2MP_W224Official(0, "0.65.5", "no signature beside the binaries: a build made without the project's key")
    check("c: an unsigned build reads unofficial", KCD2MP_W224OfficialText() == UNOFFICIAL, KCD2MP_W224OfficialText())
    check("c: ...with the reason in the log", (lastLog("W224-OFFICIAL") or ""):find("no signature beside the binaries", 1, true) ~= nil, lastLog("W224-OFFICIAL"))
    KCD2MP_W224Official(1, "0.65.5\") os.exit(", "x")
    check("c: a version that is not a version reads unofficial", KCD2MP_W224OfficialText() == UNOFFICIAL, KCD2MP_W224OfficialText())
    KCD2MP_W224Official(1, "", "x")
    check("c: no version reads unofficial", KCD2MP_W224OfficialText() == UNOFFICIAL, KCD2MP_W224OfficialText())
    KCD2MP_W224Official(nil, "0.65.5", "x")
    check("c: a word without the flag reads unofficial", KCD2MP_W224OfficialText() == UNOFFICIAL, KCD2MP_W224OfficialText())
    KCD2MP_W224Official("yes", "0.65.5", "x")
    check("c: a flag that is not 1 reads unofficial", KCD2MP_W224OfficialText() == UNOFFICIAL, KCD2MP_W224OfficialText())
end

-- (d) the mod menu draws it
do
    local M = KCD2MP.w154menu
    KCD2MP_W224Official(0, "0.65.5", "no signature")
    if M.menu.open then M.close("test") end
    TIMERS = {}; DRAWS = {}
    key("kcd2mp_menu_toggle")
    check("d: the menu opens", M.menu.open == true)
    DRAWS = {}
    frame()
    local title, line = drawn("Kingdom Come: Together -- mod menu"), drawn(UNOFFICIAL)
    check("d: an unofficial build's line is drawn in the open menu", title ~= nil and line ~= nil)
    check("d: ...under the title, in its column", title ~= nil and line ~= nil and line.x == title.x and line.y > title.y, line and (line.x .. "," .. line.y))
    check("d: ...on the right of the screen (x >= 0.6 of 1920)", line ~= nil and line.x >= 1152, line and line.x)
    KCD2MP_W224Official(1, "0.65.5", "signed")
    DRAWS = {}
    frame()
    check("d: a signed build's line is drawn", drawn("Official build 0.65.5") ~= nil and drawn(UNOFFICIAL) == nil)
    NOW = NOW + 1   -- (past the key's own repeat guard)
    key("kcd2mp_menu_toggle")
    check("d: the menu closes", M.menu.open == false)
    frame(); DRAWS = {}; frame()
    check("d: closed, nothing is drawn", drawn("Official build") == nil and drawn("Unofficial build") == nil)
end

-- (e) the switches
do
    local mark = #LOG
    KCD2MP_W224Status()
    local s = lastLog("W224-STATUS") or ""
    local all = true
    for _, n in ipairs(NAMES) do if not s:find("mp_" .. n .. "=on", 1, true) then all = false end end
    check("e: mp_w224_status names every switch", all, s)
    check("e: ...and the build's line", s:find("Official build 0.65.5", 1, true) ~= nil, s)
    KCD2MP_W224Cfg("w224_clinch=on,w224_chains=off,w224_death=on,w224_clinch_log=on,w224_official=off")
    check("e: the agent's list sets them", KCD2MP_W224On("w224_chains") == false and KCD2MP_W224On("w224_official") == false and KCD2MP_W224On("w224_clinch") == true)
    KCD2MP_W224Cfg("w224_chains=on,w224_official=on,nonsense=on")
    check("e: ...and back; an unknown name is nothing", KCD2MP_W224On("w224_chains") == true and KCD2MP_W224On("w224_official") == true and KCD2MP_W224On("nonsense") == true)
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
