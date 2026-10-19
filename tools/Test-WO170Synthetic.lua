-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-170 synthetic test (the game-side halves), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock).
--   (R) F3 a rider is mounted only onto a ready horse: both bound to the DLL's writer with physics, within 3 m, outside a WO-138 hold;
--       else he stays on foot, asked again every 2 s for 20 s, then given up; 20 rider spawns in a row, no fault
--   (S) F4 a talk's end does not re-pause its copy while the engine's interactive scene of that dialogue runs (until it ends, or 10 s)
--   (W) the switches, the status line and the agent's re-sync (mp_figure_guard, mp_scene_hold)
-- Driven by Test-WO170Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-170-findings.md.
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
System.GetEntitiesInSphere = function(p, r) return {} end
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
UIAction.RegisterElementListener = function(tbl, el, inst, ev, fn) LISTENERS[#LISTENERS + 1] = { tbl = tbl, el = el, ev = ev, fn = fn } end
Calendar = mkstub()
Calendar.GetWorldTime = function() return 1000000 end
XGenAIModule = mkstub()
MSGS = {}; ENDCOMBAT = {}
XGenAIModule.MakeTableFromType = function(t) return { kind = t } end
XGenAIModule.SendMessageToEntityData = function(to, kind, t) MSGS[#MSGS + 1] = { to = to, kind = kind, t = t } end
XGenAIModule.TryEndCombat = function(w) ENDCOMBAT[#ENDCOMBAT + 1] = w; return true end
EnvironmentModule = { GetRainIntensity = function() return 0 end }
ItemManager = mkstub()

NEXTID = 5000
PX, PY, PZ = 100, 100, 10
IN_DIALOG = false
ORIG_TALKS = 0
P_DANGER = false
BasicAIActions = { OnTalk = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end,
                   OnChatWithFocus = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end }
player = { id = 1, class = "Player" }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PX, y = PY, z = PZ } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.GetDirectionVector = function() return { x = 1, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end, StandUp = function() end }
player.human = { IsInDialog = function() return IN_DIALOG end }
player.soul = { RestrictDialog = function() end, GetState = function() return 0 end,
                IsInCombatDanger = function() return P_DANGER end, IsInTenseCircumstance = function() return false end }
ENTS["Dude"] = player

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return unpack(r)
end

-- @@KDCMP@@

-- WO-200 Act III: the sweeps and the geometric rider rule these scenarios pin stand down while WO-200's copy_neutral_always and
-- rider_from_host are on (the default); they are tested here with those off (Test-WO200Synthetic.lua covers the replacements).
-- WO-201 Act VI: the geometric rider rule is gone (row 7); the riders here are the host's word, so rider_from_host stays on
if KCD2MP_W200Set then KCD2MP_W200Set("copy_neutral_always", "off") end

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function lastLog(needle, fromLog)
    for i = #LOG, (fromLog or 0) + 1, -1 do if LOG[i]:find(needle, 1, true) then return LOG[i] end end
    return nil
end
local function countLog(needle, fromLog)
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
local function lastEvt(name, fromLog)
    for i = #LOG, (fromLog or 0) + 1, -1 do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " ", 1, true) then return l end
    end
    return nil
end
local function cmdCount(prefix)
    local n = 0
    for _, c in ipairs(CMDS) do if c:sub(1, #prefix) == prefix then n = n + 1 end end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
local function runTimers()
    local t = TIMERS
    TIMERS = {}
    for _, x in ipairs(t) do x.f() end
end

-- a host copy: a puppet the host streams (soul / actor / human binds as the game has them; flags settable)
local function copy(name, x, y, opts)
    opts = opts or {}
    NEXTID = NEXTID + 1
    local e = { id = NEXTID, class = "NPC", this = { id = "wuid" .. name } }
    e.GetName = function() return name end
    e.GetWorldPos = function() return { x = x, y = y, z = PZ } end
    e.actor = { IsDead = function() return false end, CanTalk = function() return opts.canTalk ~= false end }
    e.human = { IsInDialog = function() return opts.inDialog == true end, IsWeaponDrawn = function() return false end,
                InterruptDialogs = function() opts.interrupted = (opts.interrupted or 0) + 1 end }
    e.soul = { RestrictDialog = function() end,
               IsInCombatMode = function() return opts.combat == true end,
               IsInCombatDanger = function() return opts.danger == true end,
               IsDialogRestricted = function() return opts.restricted == true end,
               HasScriptContext = function(_, c) return opts.ctx ~= nil and opts.ctx[c] == true end }
    -- the game's own prompt rules (BasicAIActions:ActorCanTalk / GetCanTalkHintType) are methods of every NPC entity
    e.ActorCanTalk = function(self, user) return self.actor:CanTalk(user.id) and not self.soul:IsInCombatMode() end
    e.GetCanTalkHintType = function(self)
        if self.soul:IsDialogRestricted(player.id) then return false end
        if player.soul:IsInCombatDanger() or player.soul:IsInTenseCircumstance() then return false end
        return true
    end
    e.opts = opts
    ENTS[name] = e
    KCD2MP.npcPuppets[name] = {}
    return e
end
local function joiner()
    KCD2MP.hitSensorOn = false
    KCD2MP.wo102.authorityHost = true; KCD2MP.wo102.authorityPause = true
    KCD2MP.w137.joiner, KCD2MP.w137.active, KCD2MP.w137.host, KCD2MP.w137.sync, KCD2MP.w137.talkOn = true, true, false, true, true
    KCD2MP.w137.aliveAt = NOW
    pcall(KCD2MP_W137InstallTalk)
end
local function host()
    KCD2MP.w137.joiner, KCD2MP.w137.active, KCD2MP.w137.host = false, true, true
end
local function reset()
    for n in pairs(ENTS) do ENTS[n] = nil end
    ENTS["Dude"] = player
    PX, PY, PZ = 100, 100, 10
    KCD2MP.npcPuppets = {}
    KCD2MP._npcPaused = {}
    KCD2MP.w137.talking = {}
    KCD2MP.w137.ownTalkAt = nil
    KCD2MP.w166.talk.lastDlgEndAt = nil
    KCD2MP.w164.lastEnd = {}; KCD2MP.w164.hostCombat = {}; KCD2MP.w164.sweptAt = {}
    KCD2MP.w160.conv = {}
    local W = KCD2MP.w167
    W.talkFallback = false
    W.talk.lastNpc = {}; W.talk.fresh = {}; W.talk.focus = nil; W.talk.nohookAt = {}; W.talk.fbLogAt = -1e9
    W.scene.faderHeldAt = nil
    IN_DIALOG = false; P_DANGER = false
    TOASTS = {}; TIMERS = {}; CMDS = {}; MSGS = {}; ENDCOMBAT = {}; CVARS = {}
end


-- (R) F3 ----------------------------------------------------------------------------------------------------------------------------
-- a rider copy the host says rides that horse, as KCD2MP_W164RiderHeld (WO-200's host word) sees them
local function riderPair(tag, opts)
    opts = opts or {}
    for n in pairs(KCD2MP.npcPuppets) do if n:sub(1, 3) == "dw_" then KCD2MP.npcPuppets[n] = nil; ENTS[n] = nil end end   -- one pair at a time
    local hn, rn = "dw_horse_" .. tag, "dw_rider_" .. tag
    local horse = copy(hn, 10, 0); horse.class = "Horse"
    local rider = copy(rn, 10, 0)
    local hpp = { x = 10, y = 0, z = 0 }
    local rpp = { x = 10, y = 0, z = 1.0 }
    if opts.apart then hpp = { x = 10 + opts.apart, y = 0, z = 0 } end
    horse.GetWorldPos = function() return hpp end
    rider.GetWorldPos = function() return rpp end
    horse.PHYS = opts.horsePhys ~= false
    rider.PHYS = opts.riderPhys ~= false
    horse.GetPhysicalStats = function(self) if self.PHYS then return { mass = 500 } end return nil end
    rider.GetPhysicalStats = function(self) if self.PHYS then return { mass = 80 } end return nil end
    local mounted = { n = 0, id = nil }
    rider.human.ForceMount = function(self, id) mounted.n = mounted.n + 1; mounted.id = id end
    rider.human.IsMounted = function() return mounted.id ~= nil end
    rider.human.ForceDismount = function() mounted.id = nil end
    rider.SetWorldPos = function() end
    local hp, rp = KCD2MP.npcPuppets[hn], KCD2MP.npcPuppets[rn]
    hp.tx, hp.ty, hp.tz = 10, 0, 0
    rp.tx, rp.ty, rp.tz = 10.3, 0.2, 1.2
    hp.nativeOwned = opts.bound ~= false; rp.nativeOwned = opts.bound ~= false
    return { horse = horse, rider = rider, hp = hp, rp = rp, hn = hn, rn = rn, mounted = mounted, hpp = function(p) hpp = p end }
end
local function tick(r, n)
    local last
    -- WO-201 Act VI (WO-200 ledger row 7): no geometry makes a rider any more -- the host says this one rides his horse
    if KCD2MP_W200Mount and not r.onFoot then KCD2MP_W200Mount(r.rn, r.hn) end
    for _ = 1, n or 1 do last = KCD2MP_W164RiderHeld(r.rn, r.rp, r.rider) end
    return last
end
do
    ERRS = {}
    reset(); joiner(); PX, PY = 12, 1   -- the player is near the rider and his horse (a mount is never made far away)
    KCD2MP_NpcNativeAlive(1, 1, 2, 2)   -- the DLL's writer is healthy: a body must be bound to it
    -- R1: the field's crash -- the horse copy's physics is not there yet
    local r = riderPair("a", { horsePhys = false })
    local mark = #LOG
    tick(r, 6)
    check("R1: the horse copy has physics=none-yet: no mount call, he stays on foot, the wait is said",
        r.mounted.n == 0 and lastLog("WO200-RIDER mount=wait npc=dw_rider_a horse=dw_horse_a why=horse-physics=none-yet", mark) ~= nil and countLog("WO200-RIDER mount=wait", mark) == 1,
        lastLog("WO200-RIDER", mark))
    check("R1b: nothing of WO-164's mount line either", countLog("WO164-RIDER mount", mark) == 0)
    -- R2: asked again only every 2 s
    mark = #LOG
    NOW = NOW + 1.0; tick(r, 3)
    check("R2: inside 2 s: not asked again", countLog("WO200-RIDER mount=wait", mark) == 0 and r.mounted.n == 0)
    NOW = NOW + 1.2; tick(r, 2)
    check("R2b: 2 s later: asked again (still waiting)", countLog("WO200-RIDER mount=wait", mark) == 1 and r.mounted.n == 0)
    -- R3: the horse's physics arrives -> the next ask mounts
    r.horse.PHYS = true
    mark = #LOG
    NOW = NOW + 2.1; local held = tick(r, 1)
    check("R3: physics there, bound, within 3 m: mounts, the writer lets him go", r.mounted.n == 1 and r.mounted.id == r.horse.id and held == true
        and lastLog("WO200-RIDER mount rider=dw_rider_a horse=dw_horse_a ok=true", mark) ~= nil and lastLog("WO200-RIDER mount rider=dw_rider_a horse=dw_horse_a ok=true", mark) ~= nil,
        lastLog("WO200-RIDER", mark))
    KCD2MP.w164.riders = {}

    -- R4: given up after 20 s
    reset(); joiner(); PX, PY = 12, 1; KCD2MP_NpcNativeAlive(1, 1, 2, 2)
    r = riderPair("b", { horsePhys = false })
    mark = #LOG
    for _ = 1, 14 do tick(r, 2); NOW = NOW + 2.0; KCD2MP_NpcNativeAlive(1, 1, 2, 2) end
    check("R4: 20 s without physics: given up (said once), never a mount call", r.mounted.n == 0 and countLog("WO200-RIDER mount=gave-up", mark) == 1
        and lastLog("WO200-RIDER mount=gave-up npc=dw_rider_b horse=dw_horse_b why=horse-physics=none-yet", mark) ~= nil, lastLog("WO200-RIDER mount=gave-up", mark))
    r.horse.PHYS = true
    mark = #LOG
    NOW = NOW + 3; tick(r, 3)
    check("R4b: given up stays given up while his stream stays on that horse", r.mounted.n == 0 and countLog("WO200-RIDER mount", mark) == 0)
    KCD2MP.w164.riders = {}

    -- R5: far apart -> waits (the stand-in spawned where the horse is not); within 3 m -> mounts at the next ask
    reset(); joiner(); PX, PY = 12, 1; KCD2MP_NpcNativeAlive(1, 1, 2, 2)
    r = riderPair("c", { apart = 10 })
    mark = #LOG
    tick(r, 4)
    check("R5: 10 m apart: no mount call, why names the distance", r.mounted.n == 0 and lastLog("WO200-RIDER mount=wait npc=dw_rider_c horse=dw_horse_c why=apart-10m", mark) ~= nil, lastLog("WO200-RIDER", mark))
    r.hpp({ x = 11.5, y = 0, z = 0 })
    NOW = NOW + 2.1; tick(r, 1)
    check("R5b: brought within 3 m: mounts", r.mounted.n == 1 and lastLog("WO200-RIDER mount rider=dw_rider_c", mark) ~= nil)
    KCD2MP.w164.riders = {}

    -- R6: the rider's physics, a body not bound to the DLL's writer, a WO-138 hold, a dead horse
    reset(); joiner(); PX, PY = 12, 1; KCD2MP_NpcNativeAlive(1, 1, 2, 2)
    r = riderPair("d", { riderPhys = false })
    mark = #LOG; tick(r, 4)
    check("R6: the rider has no physics yet: waits", r.mounted.n == 0 and lastLog("why=rider-physics=none-yet", mark) ~= nil)
    KCD2MP.w164.riders = {}
    r = riderPair("e", { bound = false })
    mark = #LOG; tick(r, 4)
    check("R6b: the DLL's writer is up and the horse copy is not bound to it: waits", r.mounted.n == 0 and lastLog("why=horse-not-bound", mark) ~= nil)
    KCD2MP.w164.riders = {}
    r = riderPair("f")
    local origHold = KCD2MP_W138Holding
    KCD2MP_W138Holding = function() return true end
    mark = #LOG; tick(r, 4)
    check("R6c: inside a WO-138 hold: waits", r.mounted.n == 0 and lastLog("why=w138-hold", mark) ~= nil)
    KCD2MP_W138Holding = origHold
    KCD2MP.w164.riders = {}
    r = riderPair("g")
    r.horse.actor.IsDead = function() return true end
    mark = #LOG; tick(r, 4)
    check("R6d: a dead horse is never a mount target", r.mounted.n == 0 and lastLog("why=horse-dead", mark) ~= nil)
    KCD2MP.w164.riders = {}
    -- the DLL's writer down: Lua writes both bodies; physics is still checked
    KCD2MP._npcNative.aliveAt = nil
    r = riderPair("h", { bound = false })
    mark = #LOG; tick(r, 4)
    check("R6e: no DLL writer: not-bound does not block (Lua writes them), physics + distance still do", r.mounted.n == 1)
    KCD2MP.w164.riders = {}

    -- R7: 20 rider spawns in a row, the horse's physics pending on every other one: 0 faults, no mount call on a pending one
    reset(); joiner(); PX, PY = 12, 1; KCD2MP_NpcNativeAlive(1, 1, 2, 2)
    local pendingMounts, readyMounts, readyN = 0, 0, 0
    ERRS = {}
    for i = 1, 20 do
        local pending = (i % 2 == 1)
        local p = riderPair("s" .. i, { horsePhys = not pending })
        tick(p, 5)
        if pending then pendingMounts = pendingMounts + p.mounted.n else readyN = readyN + 1; readyMounts = readyMounts + p.mounted.n end
        NOW = NOW + 0.5; KCD2MP_NpcNativeAlive(1, 1, 2, 2)
        KCD2MP.w164.riders = {}
    end
    check("R7: 20 spawns in a row: no mount call on a pending horse, every ready one mounted once", pendingMounts == 0 and readyMounts == readyN and readyN == 10,
        string.format("pending=%d ready=%d/%d", pendingMounts, readyMounts, readyN))
    noErrs("R")
end

-- (S) F4 ----------------------------------------------------------------------------------------------------------------------------
local function talkOn(name, id)
    copy(name, 101, 100)
    KCD2MP.w137.talking[name] = { since = NOW, via = "OnTalk", resumed = true, id = id, started = true }
    KCD2MP._npcPaused[name] = nil   -- resumed for the talk
end
do
    ERRS = {}
    reset(); joiner(); PX, PY = 12, 1   -- the player is near the rider and his horse (a mount is never made far away)
    local pauses = {}
    local origPause = mp_wo102_pause
    mp_wo102_pause = function(name, p, why) pauses[#pauses + 1] = name .. ":" .. tostring(why); KCD2MP._npcPaused[name] = NOW end
    -- S1: the order, pure
    check("S1: no scene running: the normal end now", KCD2MP_W170RepauseDue(0, 10, 10.1, 10) == "scene-ended")
    check("S1b: a scene running, 4 s: keep holding", KCD2MP_W170RepauseDue(1, 10, 14, 10) == nil)
    check("S1c: a scene running, 10 s: the normal end (T4's 20 s rescue stays the backstop)", KCD2MP_W170RepauseDue(1, 10, 20.0, 10) == "10s")
    -- S2: a dialogue ends with a scene still enqueued: no re-pause until EndScene
    KCD2MP_W170Scene(1)
    talkOn("tzel_juggler_1", 700)
    local mark = #LOG
    KCD2MP_W137DialogEnd(700, "Dude tzel_juggler_1")       -- the end waits in its 2.5 s window (WO-144)
    NOW = NOW + 3; KCD2MP_W137TalkTick()
    check("S2: the dialogue ended with its scene still running: the copy is NOT paused again", #pauses == 0 and KCD2MP._npcPaused["tzel_juggler_1"] == nil
        and KCD2MP.w137.talking["tzel_juggler_1"] == nil and KCD2MP.w170.held["tzel_juggler_1"] ~= nil
        and lastLog("WO170-SCENE hold npc=tzel_juggler_1", mark) ~= nil and lastLog("left running: its dialogue's scene is still on", mark) ~= nil, pauses[1] or lastLog("WO137-TALK end", mark))
    NOW = NOW + 4; KCD2MP_W137TalkTick(); KCD2MP_W170SceneTick()
    check("S2b: still no re-pause 4 s on", #pauses == 0)
    -- S3: EndScene -> the normal end at once
    mark = #LOG
    KCD2MP_W170Scene(0)
    check("S3: EndScene: paused again at once, the hold released", #pauses == 1 and pauses[1] == "tzel_juggler_1:w170-scene-scene-ended" and KCD2MP.w170.held["tzel_juggler_1"] == nil
        and lastLog("WO170-SCENE repause npc=tzel_juggler_1 why=scene-ended", mark) ~= nil, pauses[1])
    -- S4: no scene: the ordinary end, as before
    pauses = {}; KCD2MP._npcPaused = {}; KCD2MP.w137.talking = {}
    talkOn("tzel_man_77", 701)
    KCD2MP_W137DialogEnd(701, "Dude tzel_man_77")
    NOW = NOW + 3; KCD2MP_W137TalkTick()
    check("S4: no scene running: the ordinary end paused it (w137-talk-end)", #pauses == 1 and pauses[1] == "tzel_man_77:w137-talk-end" and next(KCD2MP.w170.held) == nil, pauses[1])
    -- S5: a scene that never ends: 10 s, then the normal end
    pauses = {}; KCD2MP._npcPaused = {}; KCD2MP.w137.talking = {}
    KCD2MP_W170Scene(1)
    talkOn("tzel_man_78", 702)
    KCD2MP_W137DialogEnd(702, "Dude tzel_man_78")
    NOW = NOW + 3; KCD2MP_W137TalkTick()
    check("S5: held", KCD2MP.w170.held["tzel_man_78"] ~= nil and #pauses == 0)
    mark = #LOG
    NOW = NOW + 10.5; KCD2MP_W170SceneTick()
    check("S5b: 10 s after the end, the scene still on: paused again (why=10s)", #pauses == 1 and pauses[1] == "tzel_man_78:w170-scene-10s"
        and lastLog("WO170-SCENE repause npc=tzel_man_78 why=10s", mark) ~= nil, pauses[1])
    -- S6: talked to again while held: the new talk's own end decides, the hold is dropped
    pauses = {}; KCD2MP._npcPaused = {}; KCD2MP.w137.talking = {}; KCD2MP_W170Scene(1)
    talkOn("tzel_man_79", 703)
    KCD2MP_W137DialogEnd(703, "Dude tzel_man_79"); NOW = NOW + 3; KCD2MP_W137TalkTick()
    KCD2MP.w137.talking["tzel_man_79"] = { since = NOW, via = "OnTalk", resumed = true, id = 704, started = true }
    KCD2MP_W170SceneTick()
    check("S6: talked to again: the hold is dropped without a pause", KCD2MP.w170.held["tzel_man_79"] == nil and #pauses == 0)
    -- S7: the switch off: the ordinary end even with a scene running; a never-started talk is never held
    pauses = {}; KCD2MP._npcPaused = {}; KCD2MP.w137.talking = {}; KCD2MP_W170Scene(1)
    KCD2MP_W170SetSceneHold("off")
    talkOn("tzel_man_80", 705)
    KCD2MP_W137DialogEnd(705, "Dude tzel_man_80"); NOW = NOW + 3; KCD2MP_W137TalkTick()
    check("S7: mp_scene_hold off: paused as in 0.48.5", #pauses == 1 and pauses[1] == "tzel_man_80:w137-talk-end")
    KCD2MP_W170SetSceneHold("on")
    pauses = {}; KCD2MP._npcPaused = {}; KCD2MP.w137.talking = {}
    talkOn("tzel_man_81", 706)
    KCD2MP_W137TalkEnd("tzel_man_81", "never-started")
    check("S7b: a talk that never started is paused as before (a scene means a dialogue ran)", #pauses == 1 and next(KCD2MP.w170.held) == nil)
    KCD2MP_W170Scene(0)
    mp_wo102_pause = origPause
    noErrs("S")
end

-- (W) the switches and the status --------------------------------------------------------------------------------------------
do
    ERRS = {}
    reset()
    check("W1: the switches and the status command are registered", CCMDS["mp_figure_guard"] ~= nil and CCMDS["mp_figure_guard"].body == "KCD2MP_W170SetFigureGuard(%line)"
        and CCMDS["mp_scene_hold"] ~= nil and CCMDS["mp_scene_hold"].body == "KCD2MP_W170SetSceneHold(%line)" and CCMDS["mp_w170_status"] ~= nil)
    check("W2: the defaults: both on", KCD2MP.w170.figureGuard == true and KCD2MP.w170.sceneHold == true)
    local mark = #LOG
    check("W3: mp_figure_guard tells the agent", KCD2MP_W170SetFigureGuard("off") == true and countEvt("w166_cfg", "figure_guard=off", mark) == 1
        and lastLog("WO170-TOGGLE mp_figure_guard off", mark) ~= nil and KCD2MP_W170SetFigureGuard("on") == true)
    check("W4: a bad word changes nothing", KCD2MP_W170SetFigureGuard("maybe") == false and KCD2MP.w170.figureGuard == true
        and KCD2MP_W170SetSceneHold("maybe") == false and KCD2MP.w170.sceneHold == true)
    mark = #LOG
    KCD2MP_W170Status()
    check("W5: the status line", lastLog("WO170-STATUS figure_guard=on scene_hold=on scenes_running=0", mark) ~= nil)
    mark = #LOG
    KCD2MP.w137.aliveAt = nil
    KCD2MP_W137Session(false, true, true)
    check("W6: a (re)connected agent learns the figure guard", countEvt("w166_cfg", "figure_guard=on", mark) == 1)
    noErrs("W")
end

OUT = table.concat(RESULTS, "\n")
