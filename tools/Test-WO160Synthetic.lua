-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-160 synthetic test (the game-side half), against the real kdcmp.lua under MoonSharp.
--
--   (S) the own-world verdict: silent at the main menu and during a load, spoken in a loaded world
--
-- Driven by Test-WO160Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-160-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return {} end
System.GetTerrainElevation = function(p) return 0 end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
AI.EnableUpdateLookTarget = function(id, on) LOOKT = { id = id, on = on } end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
Game = mkstub()
Calendar = mkstub()
WORLD = 1000000
Calendar.GetWorldTime = function() return WORLD end
LABELS = {}
XGenAIModule = mkstub()
XGenAIModule.IsPointInAreaWithLabel = function(p, l) return LABELS[l] == true end

STATE = { health = 50, exhaust = 40, hunger = 60 }
player = { id = 1, soul = {
    GetState = function(self, n) return STATE[n] end,
    SetState = function(self, n, v) STATE[n] = v end,
} }
player.GetWorldPos = function() return { x = 0, y = 0, z = 0 } end

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return unpack(r)
end

-- @@KDCMP@@

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function lastLog(needle, fromLog)
    for i = #LOG, (fromLog or 0) + 1, -1 do if LOG[i]:find(needle, 1, true) then return LOG[i] end end
    return nil
end
local function countEvt(name, argPrefix, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " " .. (argPrefix or ""), 1, true) then n = n + 1 end
    end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
local P = { x = 10, y = 20, z = 30 }

-- (S) the own-world verdict needs a loaded world --------------------------------------------------------------------------------
do
    ERRS = {}
    local savedPlayer = player
    player = nil
    System.GetEntityByName = function(n) return ENTS[n] end
    ENTS = {}
    local t0, mark = #TOASTS, #LOG
    KCD2MP_Wo124SessionMode(true)
    KCD2MP_W140Separate(true, "You loaded your own save.")
    check("S1: a session-mode packet at the main menu, then the agent's verdict: no toast", #TOASTS == t0 and lastLog('WO140-SEPARATE "', mark) == nil)
    check("S2: ... and the hold is logged", lastLog("WO160-SEPARATE held at the menu", mark) ~= nil)
    check("S3: ... nothing is left to draw", KCD2MP.w140.sep == nil)
    Game.IsLoadingEngineSaveGame = function() return true end
    player = savedPlayer
    KCD2MP.w140.sepHeld = 0
    mark = #LOG
    KCD2MP_W140Separate(true, "You loaded your own save.")
    check("S4: during a load (the join's own): no toast either", #TOASTS == t0 and KCD2MP.w140.sep == nil and lastLog("WO160-SEPARATE held at the loading", mark) ~= nil)
    Game.IsLoadingEngineSaveGame = function() return false end
    mark = #LOG
    KCD2MP_W140Separate(true, "You loaded your own save.")
    check("S5: in a loaded world it still fires (the real own-save case)", #TOASTS == t0 + 1 and KCD2MP.w140.sep ~= nil and lastLog('WO140-SEPARATE "You loaded your own save."', mark) ~= nil)
    KCD2MP_W140Separate(false, "")
    check("S6: the agent clears it when the join lands", KCD2MP.w140.sep == nil and lastLog("WO140-SEPARATE off", mark) ~= nil)
    check("S7: where is read without an event", (function() local m = #LOG; KCD2MP_Wo124WhereNow(); return countEvt("wo124_where", "", m) == 0 end)())
    noErrs("S")
end

OUT = table.concat(RESULTS, "\n")
