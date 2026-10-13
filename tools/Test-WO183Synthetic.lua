-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-183 synthetic test (the playtest patch's Lua half: the landing's area check, mp_replay_blocked, the sweep that never runs mid-fight,
-- no save request from a scene release), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock).
-- Driven by Test-WO183Synthetic.ps1.
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

-- WO-183: the area labels (private below x=50; a shop opened by its keeper between x=10 and 20; "personal" nowhere)
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

-- (A) the landing's area check: one answer per spot, the game's own labels
do
    local mark = #LOG
    KCD2MP_W183AreasAt("tok1", "10.0,5.0,3.0;30.0,5.0,3.0;60.0,5.0,3.0;15.5,0.0,0.0")
    check("A1: one reply with one state per spot", countEvt("wo124_reply", "tok1 areas=open,private,public,open", mark) == 1)
    mark = #LOG
    KCD2MP_W183AreasAt("tok2", "-5.0,-5.0,0.0;80.0,2.0,0.0")
    check("A2: negative coordinates parse; inside / outside", countEvt("wo124_reply", "tok2 areas=private,public", mark) == 1)
    mark = #LOG
    KCD2MP_W183AreasAt("tok3", "")
    check("A3: no spots = an empty answer, never an error", countEvt("wo124_reply", "tok3 areas=", mark) == 1)
    local savedModule = XGenAIModule
    XGenAIModule = {}   -- a plain table: the engine without the area call
    mark = #LOG
    KCD2MP_W183AreasAt("tok4", "10.0,5.0,3.0")
    check("A4: the engine without the area call: unknown (so no landing is made on a guess)", countEvt("wo124_reply", "tok4 areas=unknown", mark) == 1)
    XGenAIModule = savedModule
    noErrs("A")
end

-- (B) mp_replay_blocked: ships on, its own switch, reaches the agent
do
    check("B1: on by default, the console command exists", KCD2MP.w165.replayBlocked == true and CCMDS["mp_replay_blocked"] ~= nil)
    local mark = #LOG
    check("B2: off reaches the agent", KCD2MP_W165SetReplayBlocked("off") == true and KCD2MP.w165.replayBlocked == false and countEvt("w165_cfg", "replay_blocked=off", mark) == 1)
    check("B3: on again", KCD2MP_W165SetReplayBlocked("on") == true and KCD2MP.w165.replayBlocked == true and countEvt("w165_cfg", "replay_blocked=on", mark) == 1)
    check("B4: a bad word is refused", KCD2MP_W165SetReplayBlocked("maybe") == false and KCD2MP.w165.replayBlocked == true)
    noErrs("B")
end

-- (C) the sweep never runs on a copy in combat
do
    KCD2MP.npcPuppets = KCD2MP.npcPuppets or {}
    KCD2MP.npcPuppets["tvez_man_15"] = { }
    KCD2MP.npcPuppets["tvez_man_16"] = { }
    ENTS["tvez_man_15"] = npc("tvez_man_15", "NPC", 20, 20, "w15", true)    -- the copy is in combat mode
    ENTS["tvez_man_16"] = npc("tvez_man_16", "NPC", 22, 20, "w16", false)
    local mark = #LOG
    check("C1: a copy in combat mode is not swept", KCD2MP_W164Sweep("tvez_man_15", "focus") == false and countEvt("w164_sweep", "tvez_man_15", mark) == 0)
    check("C2: said in the log", logCount("SKIPPED: the copy is in combat", mark) == 1)
    NOW = NOW + 100
    KCD2MP_W164HostCombat("tvez_man_16")
    check("C3: a copy whose host NPC fought in the last 10 s is not swept (the retarget gap)", KCD2MP_W164Sweep("tvez_man_16", "focus") == false and countEvt("w164_sweep", "tvez_man_16", mark) == 0)
    NOW = NOW + 11
    mark = #LOG
    check("C4: 11 s later it is swept as before", KCD2MP_W164Sweep("tvez_man_16", "focus") == true and countEvt("w164_sweep", "tvez_man_16", mark) == 1)
    noErrs("C")
end

-- (D) no save request from a scene release
do
    QUICKSAVES = 0
    KCD2MP_W167SceneReleased(false, "wo179-positioning-2s")
    KCD2MP_W167SceneReleased(true, "stuck-20s")
    check("D1: a stuck scene's release asks for no save", QUICKSAVES == 0)
    noErrs("D")
end

OUT = table.concat(RESULTS, "\n")
