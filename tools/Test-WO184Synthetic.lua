-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-184 synthetic test (the joiner's saves, the mod's half: the three switches, mp_save_status, the line on screen, the snapshot bind),
-- against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock). Driven by Test-WO184Synthetic.ps1.
NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}; LISTENERS = {}

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
ENDED = {}
XGenAIModule.TryEndCombat = function(id) ENDED[#ENDED + 1] = id; return true end
IGNORES = {}
XGenAIModule.GetMyWUID = function(e) return e and e.wuid end
XGenAIModule.IgnorePerception = function(a, b, secs, wt, unint)
    if a == "refuse" then error("not a perceptor") end
    IGNORES[#IGNORES + 1] = { a = a, b = b, secs = secs }
end
STOPS = {}
XGenAIModule.MakeTableFromType = function(kind) return { kind = kind } end
XGenAIModule.SendMessageToEntityData = function(id, kind, t) STOPS[#STOPS + 1] = { id = id, kind = kind } end
EnvironmentModule = { GetRainIntensity = function() return 0 end }

PX, PY, PZ, PRZ = 100, 100, 10, 0
IN_DIALOG = false
P_DANGER = false
MOUNTED = false
player = { id = 1, class = "Player" }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PX, y = PY, z = PZ } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = PRZ } end
player.GetDefaultAuxAudioProxyID = function() return 7 end
player.ExecuteAudioTrigger = function() end
player.StopAudioTrigger = function() end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end }
player.human = { IsInDialog = function() return IN_DIALOG end, InterruptDialogs = function() IN_DIALOG = false end,
                 IsMounted = function() return MOUNTED end, ForceDismount = function() MOUNTED = false end }
player.soul = { GetState = function() return 0 end, IsInCombatDanger = function() return P_DANGER end, IsInCombatMode = function() return false end }
ENTS["Dude"] = player
Sound.GetAudioTriggerID = function() return 42 end

local NPCID = 1000
local function npc(name, cls, x, y, wuid, fighting)
    NPCID = NPCID + 1
    local e = { id = NPCID, class = cls, wuid = wuid }
    e.this = { id = e.id }
    e.GetName = function() return name end
    e.GetWorldPos = function() return { x = x, y = y, z = 10 } end
    e.soul = { IsInCombatMode = function() return fighting == true end }
    e.actor = { IsDead = function() return false end }
    return e
end

-- (the area labels of the WO-183 harness, kept: the mod reads them at load)
XGenAIModule.IsPointInAreaWithLabel = function(p, label)
    if label == "private" then return p.x < 50 end
    if label == "personal" then return false end
    if label == "antitrespass" then return p.x >= 10 and p.x < 20 end
    return false
end
QUICKSAVES = 0
Game.QuickSave = function() QUICKSAVES = QUICKSAVES + 1; return true end

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

-- (A) three switches, on by default, each with its console command; mp_save_status exists
do
    check("A1: on by default", KCD2MP.w184.saveOnLeave == true and KCD2MP.w184.recover == true and KCD2MP.w184.partnerSaved == true)
    check("A2: the console commands exist", CCMDS["mp_save_status"] ~= nil and CCMDS["mp_save_on_leave"] ~= nil and CCMDS["mp_save_recover"] ~= nil and CCMDS["mp_partner_saved"] ~= nil)
    check("A3: the commands call the mod's functions", CCMDS["mp_save_status"].body == "KCD2MP_W184Status()" and CCMDS["mp_save_on_leave"].body:find("KCD2MP_W184SetSaveOnLeave", 1, true) ~= nil)
    local mark = #LOG
    KCD2MP_W184Cfg()
    check("A4: the agent is told all three", countEvt("w184_cfg", "save_on_leave=on recover=on partner_saved=on", mark) == 1)
    noErrs("A")
end

-- (B) each switch reaches the agent; a bad word is refused
do
    local mark = #LOG
    check("B1: mp_save_on_leave off", KCD2MP_W184SetSaveOnLeave("off") == true and KCD2MP.w184.saveOnLeave == false and countEvt("w184_cfg", "save_on_leave=off", mark) == 1)
    check("B2: mp_save_recover off", KCD2MP_W184SetRecover("off") == true and KCD2MP.w184.recover == false and countEvt("w184_cfg", "save_on_leave=off recover=off", mark) == 1)
    check("B3: mp_partner_saved off", KCD2MP_W184SetPartnerSaved("off") == true and KCD2MP.w184.partnerSaved == false and countEvt("w184_cfg", "save_on_leave=off recover=off partner_saved=off", mark) == 1)
    check("B4: a bad word is refused, nothing changes", KCD2MP_W184SetRecover("maybe") == false and KCD2MP.w184.recover == false)
    check("B5: a bare command reports (no change)", KCD2MP_W184SetSaveOnLeave("") == true and KCD2MP.w184.saveOnLeave == false)
    KCD2MP_W184SetSaveOnLeave("on"); KCD2MP_W184SetRecover("on"); KCD2MP_W184SetPartnerSaved("on")
    check("B6: all on again", KCD2MP.w184.saveOnLeave and KCD2MP.w184.recover and KCD2MP.w184.partnerSaved)
    check("B7: every change logged", logCount("WO184-TOGGLE mp_", mark) >= 7)
    noErrs("B")
end

-- (C) mp_save_status asks the agent; the agent's line is shown on screen
do
    local mark = #LOG
    check("C1: mp_save_status sends w184_status", KCD2MP_W184Status() == true and countEvt("w184_status", "", mark) == 1)
    local t0 = #TOASTS
    KCD2MP_W184Say("Partner saved.")
    check("C2: the line is logged", logCount("WO184-MSG Partner saved.", mark) == 1)
    check("C3: and shown (the game's own toast)", #TOASTS > t0)
    noErrs("C")
end

-- (D) the snapshot bind: one engine QuickSave per ask, the answer is the engine's
do
    QUICKSAVES = 0
    local mark = #LOG
    check("D1: the engine takes it: ok=true", KCD2MP_Wo125Snapshot("t1") == true and QUICKSAVES == 1 and countEvt("wo124_reply", "t1 ok=true", mark) == 1)
    local was = Game.QuickSave
    Game.QuickSave = function() QUICKSAVES = QUICKSAVES + 1; return false end
    check("D2: the engine refuses it: ok=false (the agent asks again in 2 s)", KCD2MP_Wo125Snapshot("t2") == false and countEvt("wo124_reply", "t2 ok=false", mark) == 1)
    Game.QuickSave = was
    check("D3: exactly one engine call per ask", QUICKSAVES == 2)
    noErrs("D")
end

OUT = table.concat(RESULTS, "\n")
