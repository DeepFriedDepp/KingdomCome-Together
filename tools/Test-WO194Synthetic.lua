-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-194 synthetic test (the mod's half: the seven in-game cutscene switches and the partner figure hidden while a cutscene plays here),
-- against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock). Driven by Test-WO194Synthetic.ps1.
NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
CVARS = {}
System.GetCVar = function(n) return CVARS[n] ~= nil and tostring(CVARS[n]) or "0" end
System.SetCVar = function(n, v) CVARS[n] = v end
System.GetEntityByName = function(n) return ENTS[n] end
NEAR = {}
System.GetEntitiesInSphere = function(p, r) return NEAR end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.RemoveEntity = function(eid) end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.GetTerrainElevation = function(p) return 0 end
System.GetFrameTime = function() return 1 / 60 end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
Calendar = mkstub()
Calendar.GetWorldTime = function() return 1000000 end
XGenAIModule = mkstub()
EnvironmentModule = { GetRainIntensity = function() return 0 end }
XGenAIModule.IsPointInAreaWithLabel = function(p, label) return false end

PX, PY, PZ = 100, 100, 10
DRAWN = false
player = { id = 1, class = "Player" }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PX, y = PY, z = PZ } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.GetDirectionVector = function() return { x = 1, y = 0, z = 0 } end   -- facing +x
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end }
player.human = { IsInDialog = function() return false end, InterruptDialogs = function() end, IsWeaponDrawn = function() return DRAWN end }
player.soul = { GetState = function() return 0 end, IsInCombatDanger = function() return false end, IsInCombatMode = function() return false end }
ENTS["Dude"] = player

local NPCID = 1000
local function npc(name, cls, x, y, fighting)
    NPCID = NPCID + 1
    local e = { id = NPCID, class = cls }
    e.this = { id = e.id }
    e.GetName = function() return name end
    e.GetWorldPos = function() return { x = x, y = y, z = 10 } end
    e.soul = { IsInCombatMode = function() return fighting and fighting() == true or false end }
    e.actor = { IsDead = function() return false end }
    e.human = { IsInDialog = function() return false end }
    ENTS[name] = e
    return e
end

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
local function logCount(needle, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do if LOG[i]:find(needle, 1, true) then n = n + 1 end end
    return n
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

-- (A) seven switches, on by default, each with its console command; the agent hears them
do
    local w = KCD2MP.w194
    check("A1: on by default", w.shared and w.hold and w.ffstop and w.timegate and w.nudge and w.figure and w.skip)
    local names = { "shared", "hold", "ffstop", "time_host", "nudge", "figure_hide", "skip_both" }
    local all = true
    for _, n in ipairs(names) do all = all and CCMDS["mp_scene_ingame_" .. n] ~= nil end
    check("A2: the seven console commands exist", all)
    local mark = #LOG
    KCD2MP_W194Cfg()
    check("A3: the agent is told all seven", countEvt("w194_cfg", "shared=on hold=on ffstop=on timegate=on nudge=on figure=on skip=on", mark) == 1)
    check("A4: a switch off reaches the agent", KCD2MP_W194SetHold("off") == true and w.hold == false and countEvt("w194_cfg", "shared=on hold=off", mark) == 1)
    check("A5: a bad word is refused", KCD2MP_W194SetShared("maybe") == false and w.shared == true)
    KCD2MP_W194SetHold("on")
    check("A6: mp_scene_ingame_time_host is the time gate", KCD2MP_W194SetTime("off") == true and w.timegate == false)
    KCD2MP_W194SetTime("on")
    noErrs("A")
end

-- (B) the partner figure: hidden while a cutscene plays here, only the ones this hid are shown again
do
    local function fig(id)
        local e = { id = 5000 + id, hidden = false }
        e.IsHidden = function(self) return self.hidden end
        e.Hide = function(self, v) self.hidden = (v == 1) end
        return e
    end
    local a, b = fig(1), fig(2)
    b.hidden = true   -- already hidden by someone else (a downed avatar)
    KCD2MP.ghosts = { ["1"] = { entity = a }, ["2"] = { entity = b } }
    local mark = #LOG
    check("B1: one figure hidden (the other was hidden already)", KCD2MP_W194FigureHide(true) == 1 and a.hidden == true and b.hidden == true)
    check("B2: a second call hides nothing more", KCD2MP_W194FigureHide(true) == 0)
    check("B3: shown again at the end: only the one this hid", KCD2MP_W194FigureHide(false) == 1 and a.hidden == false and b.hidden == true)
    check("B4: logged", logCount("WO194-FIGURE hid 1", mark) == 1 and logCount("WO194-FIGURE showed 1", mark) == 1)
    -- the name over a hidden figure is not drawn either (the live screenshot: "synth-host" floated over the scene)
    local drawn = {}
    System.DrawLabel = function(p, size, name) drawn[#drawn + 1] = name end
    KCD2MP.labelRunning = true
    KCD2MP.labelCache = { ["1"] = { x = 1, y = 1, z = 1, size = 1.2, name = "partner" } }
    KCD2MP_W194FigureHide(true)
    KCD2MP_LabelTick()
    local hiddenDrawn = #drawn
    KCD2MP_W194FigureHide(false)
    drawn = {}
    KCD2MP_LabelTick()
    check("B5: no name badge over a figure hidden for a cutscene; it is back after", hiddenDrawn == 0 and #drawn == 1, hiddenDrawn .. "/" .. #drawn)
    KCD2MP.labelRunning = false
    KCD2MP.labelCache = {}
    KCD2MP.ghosts = {}
    check("B6: no figure: nothing", KCD2MP_W194FigureHide(true) == 0 and KCD2MP_W194FigureHide(false) == 0)
    noErrs("B")
end

OUT = table.concat(RESULTS, "\n")
