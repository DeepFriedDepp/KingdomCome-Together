-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-151 synthetic test, against the real kdcmp.lua under MoonSharp (harness from Test-WO118/WO148Synthetic.lua).
--   (a) defaults: the WO151-BUILD marker, the w151_cfg event, every new console command registered
--   (b) 1.1: a copy a blow ragdolled (not-living) lies 2.5 s unwritten, then Lua places it; mp_copy_fight off
--       places it at once; the fight list (KCD2MP_W151Fights); no temporary tool in a fight
--   (c) 1.4: a horse puppet's gait has hysteresis (no walk/gallop/idle flapping around one speed)
--   (d) Phase 2: the local mount (the game's GetHorse, resolved as a WUID only) never becomes a puppet; the
--       mount start (Horse.OnMount) takes a puppet at once; a ridden horse is never parked nor frozen by the
--       join; an avatar's adoption takes the puppet, its ride ends with the avatar, not this player's dismount
--   (e) 3.6: a hold is block-only by default (no pause): the host's own talk to that NPC is refused with a
--       toast; a partner who leaves gives his holds back
--   (f) 5.2: one pick is one theft (the same item entity reported once)
--   (g) Phase 5: joint responsibility -- the host reports its own crimes (judged here, w151_crime own), the
--       joiner's witnessed crime is planted in the host's witnesses, the host's own resolution clears the
--       partner (w151_crime resolved), a cleared record is forgotten (crime:forgetCrimesData; a paused
--       guard copy woken for it), mp_crime_mode individual = WO-139
--   (i) 3.4: the scene guard's Lua half -- the scene's paused copies resumed and held until the release; a
--       dialogue the game forces on a paused copy resumes it (the twins' 20 s wait)
--   (h) 3.9: doors -- the host's world owns them: its real changes go out (never a load's restore), a joiner's
--       own use moves his door and asks, the host answers in the avatar's name, a door locked there refuses
-- What this proves: the Lua half. Live evidence: docs/WO-151-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; SPAWNS = {}; CCMDS = {}; LOCKS = {}; MSGS = {}
WUIDS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetCVar = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return SPHERE end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.DrawText = function(x, y, text, size) DRAWS[#DRAWS + 1] = { x = x, y = y, text = tostring(text) } end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.GetTerrainElevation = function(p) return 0 end
WRONG_ENTITY = nil   -- what System.GetEntity answers (the live L4 trap: an unrelated entity for a WUID)
System.GetEntity = function(id) if WRONG_ENTITY then return WRONG_ENTITY end for _, e in pairs(ENTS) do if e.id == id then return e end end return nil end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Physics.RayWorldIntersection = function() return nil end   -- nothing in the way: every witness in range sees
Game.AddSaveLock = function(name, desc) if LOCKS[name] then return false end LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] == true; LOCKS[name] = nil; return had end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }
XGenAIModule = mkstub()
XGenAIModule.GetEntityByWUID = function(w) return WUIDS[w] end
XGenAIModule.MakeTableFromType = function(kind) return { kind = kind, information = {} } end
XGenAIModule.SendMessageToEntityData = function(id, kind, t) MSGS[#MSGS + 1] = { id = id, kind = kind, t = t } end

MOUNTED = false
HORSE_WUID = nil
player = { id = 1, this = { id = 777 }, GetName = function(self) return "Dude" end,
           px = 0, py = 0, pz = 0,
           GetWorldPos = function(self) return { x = player.px, y = player.py, z = player.pz } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           GetLinkedParent = function() return nil end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end },
           human = { IsMounted = function() return MOUNTED end, GetHorse = function() return HORSE_WUID end,
                     IsInDialog = function() return false end, IsRiding = function() return nil end },
           inventory = { GetCountOfClass = function() return 0 end } }

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
local function logCount(pat)
    local n = 0
    for _, l in ipairs(LOG) do if string.find(l, pat, 1, true) then n = n + 1 end end
    return n
end
local function lastLog(pat)
    for i = #LOG, 1, -1 do if string.find(LOG[i], pat, 1, true) then return LOG[i] end end
    return nil
end
local function clearLog() LOG = {} end
local function evts(name)
    local out = {}
    for _, l in ipairs(LOG) do
        local a = l:match("%[KCD2%-MP%-EVT%] v1 %d+ " .. name:gsub("_", "%%_") .. " (.*)$")
        if a then out[#out + 1] = a end
    end
    return out
end
local function cmdCount(pat)
    local n = 0
    for _, c in ipairs(CMDS) do if string.find(c, pat, 1, true) then n = n + 1 end end
    return n
end
local function runTimers()
    local due = TIMERS; TIMERS = {}
    for _, t in ipairs(due) do pcall(t.f) end
end

-- (a) defaults -- read BEFORE any scenario touches them.
do
    local b = lastLog("WO151-BUILD") or ""
    check("a: WO151-BUILD logged once", logCount("WO151-BUILD") == 1, logCount("WO151-BUILD"))
    for _, kv in ipairs({ "copy_fight=on", "npc_reactions=on", "hold_freeze=off", "ride_owner=on", "crime_mode=joint", "fault_switchoff=on", "main_cost=off", "door_sync=on", "scene_guard=on", "whistle=on", "carry_living=on", "minigame_align=off", "quest_catchup=on" }) do
        check("a: the build line says " .. kv, b:find(kv, 1, true) ~= nil, b)
    end
    local cfg = evts("w151_cfg")[1] or ""
    check("a: w151_cfg mirrors the defaults to the agent", cfg:find("copy_fight=on", 1, true) and cfg:find("npc_reactions=on", 1, true)
        and cfg:find("crime_mode=joint", 1, true) and cfg:find("scene_guard=on", 1, true) and cfg:find("minigame_align=off", 1, true) ~= nil, cfg)
    for _, c in ipairs({ "mp_copy_fight", "mp_npc_reactions", "mp_ride_owner", "mp_crime_mode", "mp_hold_freeze", "mp_fault_switchoff",
                         "mp_main_cost", "mp_fault_status", "mp_fault_test", "mp_test_takedamage", "mp_test_row", "mp_test_copyfight", "mp_door_sync",
                         "mp_scene_guard", "mp_whistle", "mp_carry_living", "mp_minigame_align" }) do
        check("a: " .. c .. " is registered", CCMDS[c] ~= nil)
    end
    check("a: no Lua errors at load", #ERRS == 0, ERRS[1])
end

local NEXTID = 0x0E0000
local function mkEntity(name, x, y, z, cls)
    NEXTID = NEXTID + 1
    local e = { class = cls or "NPC", id = "userdata: " .. string.format("%016X", NEXTID), px = x or 0, py = y or 0, pz = z or 0, rz = 0,
                writes = {}, anims = {}, dead = false, hp = 100, hidden = false, ctx = {} }
    e.this = { id = "this:" .. name }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self, t) t = t or {}; t.x, t.y, t.z = self.px, self.py, self.pz; return t end
    e.GetWorldAngles = function(self, t) t = t or {}; t.x, t.y, t.z = 0, 0, self.rz; return t end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z; self.writes[#self.writes + 1] = { x = pos.x, y = pos.y, z = pos.z, at = NOW } end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.StartAnimation = function(self, slot, anim) self.anims[#self.anims + 1] = tostring(anim) end
    e.GetAnimationLength = function() return 0.8 end
    e.GetDirectionVector = function() return { x = 0, y = 1, z = 0 } end
    e.SetFlags = function() end
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end,
                GetCurrentAnimationState = function() return "MotionIdle" end, StandUp = function() end }
    e.human = { IsWeaponDrawn = function() return false end, DrawWeapon = function() return true end, HolsterWeapon = function() return true end,
                IsInDialog = function() return e.inDialog == true end, IsSleeping = function() return false end }
    e.soul = { GetId = function() return "userdata: 05000000000001DC" end, IsPublicEnemy = function() return false end,
               IsInCombatMode = function() return false end, GetFactionID = function() return "trosecko_settlements_zelejov_folk" end,
               HasScriptContext = function(self, c) return e.ctx[c] == true end }
    ENTS[name] = e
    return e
end
mkEntityLate = mkEntity

local function reset()
    if KCD2MP.npcSilence then KCD2MP.npcSilence.tickAt = nil; KCD2MP.npcSilence.agentAt = -1e9; KCD2MP.npcSilence.silent = {} end
    KCD2MP.wo102.authorityHost = true; KCD2MP.wo102.authorityPause = true
    KCD2MP.hitSensorOn = false
    KCD2MP.npcPuppets = {}; KCD2MP.npcTracked = {}
    KCD2MP.npcPuppetRunning = false; KCD2MP._npcDivergeUntil = {}
    KCD2MP._npcPaused = {}; KCD2MP._npcPauseExec = {}; KCD2MP._npcEverPaused = {}; KCD2MP._npcResumePending = {}
    KCD2MP._authViolationAt = {}; KCD2MP._authViolationN = {}
    KCD2MP._npcDeathRemote = {}; KCD2MP._npcDeathDiverged = {}; KCD2MP._npcDeathSeen = {}
    KCD2MP.npcDiverge = true; KCD2MP.npcYield.enabled = false
    KCD2MP.npcReplica.enabled = false; KCD2MP._npcReplicas = {}
    KCD2MP.npcSmooth = true; KCD2MP.npcPuppetTickMs = 50
    KCD2MP.npcSenderClock = true; KCD2MP._senderClock = {}
    KCD2MP.npcNativeWrite = true; KCD2MP.npcDetach = false; KCD2MP.cutsceneActive = false
    KCD2MP._npcNative = { aliveAt = nil, armed = false, on = false, bound = 0, writing = 0, binds = 0, acks = 0, nacks = 0, holds = 0, unbinds = 0 }
    KCD2MP._npcDetachStats = { issued = 0, skipped = 0, changed = 0, unchanged = 0 }
    KCD2MP.ghosts = {}; KCD2MP.horseGhosts = {}
    KCD2MP.w136.ridden = {}
    KCD2MP.w151.localMount = nil; KCD2MP.w151.mountingName = nil; KCD2MP.w151.fight = {}
    KCD2MP.w151.copyFight = true; KCD2MP.w151.rideOwner = true; KCD2MP.w151.crimeJoint = true; KCD2MP.w151.holdFreeze = false
    KCD2MP._mountedHorseName = nil
    MOUNTED = false; HORSE_WUID = nil; WRONG_ENTITY = nil; WUIDS = {}
    ENTS = {}; SPHERE = {}; TIMERS = {}; ERRS = {}; CMDS = {}; MSGS = {}; TOASTS = {}
    clearLog()
end

local SEQ = 0
local function packet(name, sx, sy, sz, flags)
    SEQ = SEQ + 1
    KCD2MP_ApplyNpcState(name, sx, sy, sz or 0, 0, 100, flags or 0, 1, SEQ % 65536, math.floor(NOW * 1000))
end
local function tick(name, sx, sy, sz, flags)
    NOW = NOW + 0.05
    packet(name, sx, sy, sz, flags)
    KCD2MP.npcPuppetRunning = true
    KCD2MP_NpcPuppetTick("ext")
end
local function alive() KCD2MP_NpcNativeAlive(1, 1, 0, 0) end

-- ---------------------------------------------------------------- (b) 1.1 the lying hold
do
    reset(); NOW = 100
    local e = mkEntity("b_npc", 10, 10, 0)
    alive()
    for i = 1, 4 do alive(); tick("b_npc", 10 + i * 0.05, 10, 0) end
    KCD2MP_NpcNativeAck("b_npc", 1, "ok")
    for i = 1, 4 do alive(); tick("b_npc", 10.2 + i * 0.05, 10, 0) end
    local w0 = #e.writes
    KCD2MP_NpcNativeAck("b_npc", 0, "not-living")   -- a local blow ragdolled the bound copy
    for i = 1, 40 do alive(); tick("b_npc", 10.4, 10, 0) end   -- 2.0 s
    check("b: a ragdolled copy is not placed while it lies (2 s)", #e.writes == w0, #e.writes - w0)
    for i = 1, 50 do alive(); tick("b_npc", 10.4, 10, 0) end   -- past 2.5 s and the re-bind's 1 s wait
    local rebind = 0
    for _, v in ipairs(evts("npc_native")) do if v:find("b_npc on ", 1, true) then rebind = rebind + 1 end end
    check("b: ...then it is the host's again: re-offered to the native writer, Lua places it meanwhile", rebind >= 2 and #e.writes > w0, "binds=" .. rebind .. " writes=" .. (#e.writes - w0))
    check("b: no Lua errors", #ERRS == 0, ERRS[1])

    reset(); NOW = 200
    KCD2MP.w151.copyFight = false
    local e2 = mkEntity("b2_npc", 20, 20, 0)
    alive()
    for i = 1, 4 do alive(); tick("b2_npc", 20 + i * 0.05, 20, 0) end
    KCD2MP_NpcNativeAck("b2_npc", 1, "ok")
    for i = 1, 4 do alive(); tick("b2_npc", 20.2 + i * 0.05, 20, 0) end
    local w1 = #e2.writes
    KCD2MP_NpcNativeAck("b2_npc", 0, "not-living")
    alive(); tick("b2_npc", 20.4, 20, 0)
    check("b: mp_copy_fight off -> placed at once (the WO-118 rule)", #e2.writes == w1 + 1, #e2.writes - w1)

    reset(); NOW = 300
    check("b: the fight list counts its names", KCD2MP_W151Fights("tbuk_man_1,tbuk_man_3") == 2)
    check("b: ...and logs who is in a fight", logCount("WO151-FIGHT tbuk_man_1 in a fight") == 1)
    KCD2MP.w143.hands = true
    KCD2MP_W143Provide("tbuk_man_1", "21dfed98-995c-418d-a22e-3c456b68412f")
    check("b: no temporary tool for a copy in a fight", logCount("WO151-FIGHT tbuk_man_1 is in a fight: no temporary") == 1, lastLog("WO151-FIGHT"))
    KCD2MP_W151Fights("")
    check("b: out of the fight is logged", logCount("WO151-FIGHT tbuk_man_1 out of the fight") == 1)
    check("b: no Lua errors (fights)", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (c) 1.4 the horse puppet's gait
do
    reset(); NOW = 400
    local h = mkEntity("c_horse", 0, 0, 0, "Horse")
    alive()
    -- a steady 3.6 m/s trot, the band between walk and gallop: once walking, it keeps walking
    local x = 0
    for i = 1, 60 do x = x + 3.6 * 0.05; alive(); tick("c_horse", x, 0, 0, 128) end
    local first = h.anims[#h.anims]
    local flips = 0
    local last = first
    for i = 1, 60 do x = x + (i % 2 == 0 and 3.8 or 3.4) * 0.05; alive(); tick("c_horse", x, 0, 0, 128)
        if h.anims[#h.anims] ~= last then flips = flips + 1; last = h.anims[#h.anims] end end
    check("c: a speed wobbling around 3.6 m/s never flips the horse's gait", flips == 0, tostring(first) .. " flips=" .. flips)
    check("c: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (d) Phase 2 one owner for a ridden horse
do
    reset(); NOW = 500
    local hz = mkEntity("d_horse", 5, 5, 0, "Horse")
    WUIDS["wuid:d_horse"] = hz
    -- the game's own answer: mounted, GetHorse = the WUID; System.GetEntity(wuid) answers an UNRELATED entity
    MOUNTED = true; HORSE_WUID = "wuid:d_horse"
    WRONG_ENTITY = mkEntity("randomEvent_x", 99, 99, 0, "SmartObject")
    check("d: the mount is named through the WUID, not System.GetEntity", KCD2MP_W136MountedHorse() == "d_horse", KCD2MP_W136MountedHorse())
    WRONG_ENTITY = nil
    KCD2MP.w151.localMount = "d_horse"
    alive()
    for i = 1, 10 do alive(); tick("d_horse", 5 + i * 0.05, 5, 0, 128) end
    check("d: the local mount never becomes the host's puppet", KCD2MP.npcPuppets["d_horse"] == nil)
    check("d: ...its samples are dropped", KCD2MP.w151.rideStats.samplesDropped >= 10, KCD2MP.w151.rideStats.samplesDropped)
    check("d: ...nothing paused it", cmdCount("wh_ai_PauseNPC d_horse") == 0)

    -- the mount start (Horse.OnMount): a puppet of the host's horse is taken at once
    reset(); NOW = 600
    local h2 = mkEntity("d2_horse", 6, 6, 0, "Horse")
    alive()
    for i = 1, 10 do alive(); tick("d2_horse", 6 + i * 0.05, 6, 0, 128) end
    check("d: (setup) the host's horse is a puppet here", KCD2MP.npcPuppets["d2_horse"] ~= nil)
    KCD2MP_W151OnMountStart(h2, player)
    check("d: the mount start takes the puppet (the rider owns it)", KCD2MP.npcPuppets["d2_horse"] == nil and KCD2MP.w136.ridden["d2_horse"] ~= nil)
    check("d: ...logged", logCount("WO151-RIDE mount starts npc=d2_horse") == 1 and logCount("WO136-RIDE take npc=d2_horse") == 1)
    local ev = evts("w136_ride")
    check("d: ...the agent is told (w136_ride 1)", ev[#ev] == "d2_horse 1", ev[#ev])
    for i = 1, 6 do alive(); tick("d2_horse", 6.6 + i * 0.05, 6, 0, 128) end
    check("d: ...its stream is ignored while ridden", KCD2MP.npcPuppets["d2_horse"] == nil)

    -- a ridden horse is never parked by the copy guard, never frozen by the join
    reset(); NOW = 700
    local h3 = mkEntity("d3_horse", 7, 7, 0, "Horse")
    KCD2MP.w151.localMount = "d3_horse"
    local saveActive = KCD2MP_W131IsActive
    KCD2MP_W131IsActive = function() return true end
    local r = KCD2MP_W131ParkReleased("d3_horse", "silence")
    KCD2MP_W131IsActive = saveActive
    check("d: the copy guard does not park a ridden horse", r == true and not h3.hidden and cmdCount("wh_ai_PauseNPC d3_horse") == 0)
    SPHERE = { h3 }
    KCD2MP.w123.npcs = {}
    KCD2MP_JoinScanNpcs(true)
    check("d: the join freeze does not pause a ridden horse", cmdCount("wh_ai_PauseNPC d3_horse") == 0 and KCD2MP.w151.rideStats.freezesRefused >= 1)

    -- an avatar adopts a horse: the puppet is taken; this player's own riding check never ends the avatar's ride
    reset(); NOW = 800
    local h4 = mkEntity("d4_horse", 8, 8, 0, "Horse")
    alive()
    for i = 1, 10 do alive(); tick("d4_horse", 8 + i * 0.05, 8, 0, 128) end
    KCD2MP_W151AvatarTakesHorse("d4_horse")
    check("d: an avatar's adoption takes the puppet", KCD2MP.npcPuppets["d4_horse"] == nil and KCD2MP.w136.ridden["d4_horse"].by == "avatar")
    local nat = evts("npc_native")
    check("d: ...unbound from the native writer", (nat[#nat] or ""):find("d4_horse off ridden-by-avatar", 1, true) ~= nil, nat[#nat])
    KCD2MP.w131.joiner = true; KCD2MP.w131.guard = true; KCD2MP.w131.aliveAt = NOW; KCD2MP.w131.shared = true
    KCD2MP_W136RideTick()
    check("d: this player's ride check does not end the avatar's ride", KCD2MP.w136.ridden["d4_horse"] ~= nil and KCD2MP.w136.ridden["d4_horse"].untilAt == nil)
    KCD2MP_W151AvatarLeavesHorse("d4_horse")
    NOW = NOW + 3.5
    KCD2MP_W136RideTick()
    check("d: the avatar's release hands it back after the settle", KCD2MP.w136.ridden["d4_horse"] == nil)
    ev = evts("w136_ride")
    check("d: ...w136_ride 1 then 0", ev[1] == "d4_horse 1" and ev[#ev] == "d4_horse 0", table.concat(ev, "|"))
    check("d: mp_ride_owner off -> nothing is ridden", (function() KCD2MP.w151.rideOwner = false; local x = KCD2MP_W151HorseRidden("d4_horse"); KCD2MP.w151.rideOwner = true; return x == false end)())
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (e) 3.6 block-only holds
do
    reset(); NOW = 900
    KCD2MP.hitSensorOn = true
    KCD2MP_W137Session(true, false, true)
    local e = mkEntity("e_smith", 1, 1, 0)
    KCD2MP_W137HostHold(true, "e_smith", 1, "talk")
    check("e: a hold is block-only by default (no pause)", cmdCount("wh_ai_PauseNPC e_smith") == 0 and logCount("exec=block-only") == 1, lastLog("WO137-HOLD"))
    NOW = NOW + 11; KCD2MP_W137Session(true, false, true)
    check("e: ...never re-paused by the hold tick", cmdCount("wh_ai_PauseNPC e_smith") == 0)
    check("e: the host's own talk to that NPC is refused", KCD2MP_W151HostTalkBlocked(e, player) == true)
    check("e: ...with a toast", (TOASTS[#TOASTS] or ""):find("partner", 1, true) ~= nil, TOASTS[#TOASTS])
    check("e: another NPC is not blocked", KCD2MP_W151HostTalkBlocked(mkEntity("e_other", 2, 2, 0), player) == false)
    check("e: the partner leaves -> his holds are given back", KCD2MP_W151ReleasePeerHolds(1) == 1 and KCD2MP.w137.held["e_smith"] == nil)
    check("e: ...the release resumes nothing (nothing was paused)", cmdCount("wh_ai_ResumeNPC e_smith") == 0 and logCount("exec=was-not-paused") == 1)
    KCD2MP.hitSensorOn = false
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (f) 5.2 one pick is one theft
do
    reset(); NOW = 1000
    KCD2MP.w139.stoleSeen = {}
    KCD2MP_W139Session(false, true, true)
    local reports = KCD2MP.w139.stats.reports
    local rec = { id = "item:1", cls = nil, x = 1, y = 2, z = 3, owner = nil }
    KCD2MP_W139Stole(rec)
    KCD2MP_W139Stole(rec)   -- OnUsed and OnUsedHold both start the check for the same item
    NOW = NOW + 1.6; runTimers()
    check("f: the same picked item is reported once", KCD2MP.w139.stats.reports == reports + 1, KCD2MP.w139.stats.reports - reports)
    check("f: ...the second is logged as the same pick", logCount("already reported (the same pick)") == 1)
    check("f: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (g) Phase 5 joint responsibility
do
    -- the host reports its own crime: judged here (a witness near the host), w151_crime own
    reset(); NOW = 1100
    KCD2MP_W139Session(true, false, true)
    check("g: the host reports crimes in joint mode", KCD2MP_W151CrimeReports() == "host")
    local wit = mkEntity("g_witness", 2, 0, 0)
    SPHERE = { wit }
    KCD2MP_W139Report("theft", 0, 0, 0, "-", "-", "stash")
    local own = evts("w151_crime")
    check("g: the host's own seen crime goes to the agent (w151_crime own)", (own[#own] or ""):find("^own theft 1 0 ") ~= nil, own[#own])
    check("g: ...not as a joiner report (no w139_crime)", #evts("w139_crime") == 0)
    KCD2MP.w151.crimeJoint = false
    check("g: individual -> the host reports nothing (WO-139)", KCD2MP_W151CrimeReports() == nil and KCD2MP_W139Report("theft", 0, 0, 0, "-", "-", "stash") == false)
    KCD2MP.w151.crimeJoint = true

    -- the joiner's witnessed crime is planted in the host's own witness (the guard among them)
    reset(); NOW = 1200
    KCD2MP_W139Session(true, false, true)
    local guard = mkEntity("g_guard", 3, 0, 0)
    guard.ctx["crime_isAuthority"] = true
    SPHERE = { guard }
    KCD2MP_W139HostJudge(1, 7, "lockpick", 0, 0, 0, "-", "-", "lock")
    local planted = 0
    for _, m in ipairs(MSGS) do if m.id == guard.this.id and m.kind == "switch:stimulus:disturbance" then planted = planted + 1 end end
    check("g: the joiner's witnessed crime is planted in the host's guard (the host's Henry too)", planted == 1, #MSGS)
    check("g: ...logged", logCount("WO151-CRIME joint lockpick raised in this world: planted in 1 of 1") == 1, lastLog("WO151-CRIME"))
    KCD2MP.w151.crimeJoint = false; MSGS = {}
    KCD2MP_W139HostJudge(1, 8, "lockpick", 0, 0, 0, "-", "-", "lock")
    check("g: individual -> nothing planted in the host's world", #MSGS == 0, #MSGS)
    KCD2MP.w151.crimeJoint = true

    -- the host's own crime dialogue: a fine clears the partner too
    reset(); NOW = 1300
    KCD2MP_W139Session(true, false, true)
    KCD2MP_W139Resolved({}, 0)   -- action 0 = paid
    local rs = evts("w151_crime")
    check("g: the host paid -> the partner's record clears too (w151_crime resolved paid)", rs[#rs] == "resolved paid", rs[#rs])

    -- a cleared record is forgotten here: a running NPC directly, a paused guard copy woken for it
    reset(); NOW = 1400
    local civ = mkEntity("g_civ", 4, 0, 0)
    local gcopy = mkEntity("g_gcopy", 5, 0, 0)
    gcopy.ctx["crime_isAuthority"] = true
    local pcopy = mkEntity("g_pcopy", 6, 0, 0)
    KCD2MP._npcPaused["g_gcopy"] = NOW; KCD2MP._npcPaused["g_pcopy"] = NOW
    SPHERE = { civ, gcopy, pcopy }
    KCD2MP_W139Cleared("paid")
    local forget = {}
    for _, m in ipairs(MSGS) do if m.kind == "crime:forgetCrimesData" then forget[m.id] = (m.t.self == m.id) end end
    check("g: a running NPC forgets (crime:forgetCrimesData, self = it)", forget[civ.this.id] == true)
    check("g: a paused guard copy forgets", forget[gcopy.this.id] == true)
    check("g: a paused copy that is no guard is left alone", forget[pcopy.this.id] == nil)
    check("g: ...the guard copy was woken for it", cmdCount("wh_ai_ResumeNPC g_gcopy") == 1)
    NOW = NOW + 1.6; runTimers()
    check("g: ...and paused again after", cmdCount("wh_ai_PauseNPC g_gcopy") == 1)
    check("g: logged", logCount("WO151-CRIME forget why=cleared:paid npcs=2 woke_guard_copies=1") == 1, lastLog("WO151-CRIME forget"))
    check("g: mp_crime_mode individual|joint toggles", KCD2MP_W151SetCrimeMode("individual") and KCD2MP.w151.crimeJoint == false
        and KCD2MP_W151SetCrimeMode("joint") and KCD2MP.w151.crimeJoint == true)
    check("g: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (h) 3.9: doors -- the host's world owns every door. The class-table wraps (AnimDoor's own shape:
--     DoPlayAnimation is the state change; Lock/Unlock; OnUsed for the player's use).
do
    -- the live shape of a level door's name (L5): a prefab-instance path, 100+ characters
    local function lvl(n) return "AnimDoor[structures/living/houses/unique/kopanina/" .. n .. ":Door/door_village_left1[structures/living/houses/unique/kopanina/" .. n .. "]_8c208501-e163-0591-3da5-308055599181]" end
    local function K(n) return KCD2MP.w151.doorNameHash(n) end
    AnimDoor = {}
    function AnimDoor:DoPlayAnimation(direction, forceTime, useSound, customAnim, usePlayerAnim, userId)
        self.inUse = 1; self.nDirection = direction; self.lastUsedBy = userId; self.curAnim = "door_anim"
        self.plays[#self.plays + 1] = { dir = direction, force = forceTime, user = userId, playerAnim = usePlayerAnim }
    end
    function AnimDoor:IsInUse() return self.inUse == 1 end
    function AnimDoor:IsOpen() return self.nDirection > 0 end
    function AnimDoor:Close() if self.nDirection == 1 then self:DoPlayAnimation(-1, nil, nil, nil, nil, player.id) end end
    function AnimDoor:Lock(dontClose) if not dontClose and self:IsOpen() then self:Close() end; self.bLocked = true end
    function AnimDoor:Unlock() self.bLocked = false end
    function AnimDoor:OnUsed(user, slot)
        local direction = -self.nDirection
        if self.bLocked == true then
            if self.hasKey then self:Unlock(); self:DoPlayAnimation(direction, nil, nil, nil, true, user.id) end
        elseif user.id == player.id then
            self:DoPlayAnimation(direction, nil, nil, nil, true, user.id)
        end
    end
    local function mkDoor(name, x, y, z)
        local d = mkEntity(lvl(name), x, y, z, "AnimDoor")
        setmetatable(d, { __index = AnimDoor })
        d.nDirection = -1; d.bLocked = false; d.inUse = 0; d.nUpdateAfterLoad = 0; d.plays = {}
        return d
    end
    local function doorEvts() return evts("w151_door") end
    System.GetEntitiesInSphereByClass = function(pos, r, cls)
        local out = {}
        for _, e in pairs(ENTS) do
            if e.class == cls and ((e.px - pos.x) ^ 2 + (e.py - pos.y) ^ 2 + (e.pz - pos.z) ^ 2) <= r * r then out[#out + 1] = e end
        end
        return out
    end
    local W = KCD2MP.w151
    check("h: the door key is a short level-safe hash of the name", K(lvl("h_door1")):match("^d%x%x%x%x%x%x%x%x$") ~= nil and K(lvl("h_door1")) ~= K(lvl("h_door2")), K(lvl("h_door1")))

    check("h: mp_door_sync is on by default", W.doorSync == true)

    -- host
    reset(); clearLog(); ERRS = {}; NOW = 2000
    local d1 = mkDoor("h_door1", 10, 20, 1)
    KCD2MP_W137Session(true, false, true)
    check("h: the wraps go in at the session tick", logCount("WO151-DOOR wrapped AnimDoor.DoPlayAnimation") == 1
        and logCount("WO151-DOOR wrapped AnimDoor.OnUsed/Lock/Unlock") == 1)
    KCD2MP_W137Session(true, false, true)
    check("h: ...once", logCount("WO151-DOOR wrapped AnimDoor.DoPlayAnimation") == 1)
    d1:DoPlayAnimation(1, nil, nil, nil, false, "npc:7")   -- an NPC opens a door in the host's world
    local e = doorEvts()
    check("h: host: a door an NPC opens is sent", #e == 1 and e[1] == "state " .. K(lvl("h_door1")) .. " 1 0 10.00 20.00 1.00", e[1])
    d1:DoPlayAnimation(1, nil, nil, nil, false, "npc:7")
    check("h: host: the same state again is not news", #doorEvts() == 1, #doorEvts())
    d1:Lock()   -- closes, then locks: two real changes
    e = doorEvts()
    check("h: host: Lock on an open door = closed, then locked", #e == 3 and e[2] == "state " .. K(lvl("h_door1")) .. " -1 0 10.00 20.00 1.00"
        and e[3] == "state " .. K(lvl("h_door1")) .. " -1 1 10.00 20.00 1.00", table.concat(e, " | "))
    d1:Lock()   -- the load's re-lock of a locked door
    check("h: host: Lock on a locked door (a load) sends nothing", #doorEvts() == 3)
    d1.nUpdateAfterLoad = 2; d1:DoPlayAnimation(1, 1.0, false, "")   -- the load's restore
    check("h: host: the load's own restore (forceTime 1.0, nUpdateAfterLoad) sends nothing", #doorEvts() == 3)
    d1.nUpdateAfterLoad = 0
    local bad = mkDoor("long/name:with[brackets]", 0, 0, 0)
    bad:DoPlayAnimation(1, nil, nil, nil, false, "npc:8")
    check("h: host: a door with a long level name is sent too (the key, not the name)", #doorEvts() == 4, #doorEvts())
    W.doorSync = false
    local d0 = mkDoor("h_door0", 0, 0, 0)
    d0:DoPlayAnimation(1, nil, nil, nil, false, "npc:9")
    check("h: host: mp_door_sync off -> nothing sent", #doorEvts() == 4)
    W.doorSync = true

    -- host: a joiner's ask
    local av = mkEntity("h_avatar", 11, 20, 1)
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    KCD2MP.ghosts["1"] = { entity = av }
    local d5 = mkDoor("h_door5", 30, 0, 0)
    clearLog()
    KCD2MP_W151DoorAsked(1, K(lvl("h_door5")), 1, 0, 30, 0, 0)
    local p = d5.plays[#d5.plays]
    check("h: host: a joiner's ask opens the host's door as the game's own Open (this player the user, no player animation)",
        d5.nDirection == 1 and p and p.user == player.id and not p.playerAnim, p and tostring(p.user))
    e = doorEvts()
    check("h: host: ...and the host's result goes back to the joiners", #e == 1 and e[1] == "state " .. K(lvl("h_door5")) .. " 1 0 30.00 0.00 0.00", e[1])
    local d6 = mkDoor("h_door6", 40, 0, 0); d6.bLocked = true
    clearLog()
    KCD2MP_W151DoorAsked(1, K(lvl("h_door6")), 1, 0, 40, 0, 0)
    e = doorEvts()
    check("h: host: an ask at a door locked here is refused, the joiner's copy told the host's state",
        d6.nDirection == -1 and d6.bLocked and #e == 1 and e[1] == "state " .. K(lvl("h_door6")) .. " -1 1 40.00 0.00 0.00", e[1])
    clearLog()
    KCD2MP_W151DoorAsked(1, K(lvl("h_door6")), 1, 1, 40, 0, 0)   -- his key / his lockpick unlocked it there
    e = doorEvts()
    check("h: host: an ask that unlocked it there unlocks and opens it here", d6.nDirection == 1 and not d6.bLocked
        and #e == 1 and e[1] == "state " .. K(lvl("h_door6")) .. " 1 0 40.00 0.00 0.00", e[1])
    KCD2MP_W151DoorAsked(1, "h_nowhere", 1, 0, 1, 1, 1)
    check("h: host: an ask for a door that is not here is logged", logCount("WO151-DOOR ask from 1 for h_nowhere: no such door here") == 1)

    -- joiner
    reset(); clearLog(); NOW = 2100
    KCD2MP_W137Session(false, true, true)
    local j1 = mkDoor("j_door1", 10, 20, 1)
    KCD2MP_W151DoorApply(K(lvl("j_door1")), 1, 0, 10, 20, 1)
    p = j1.plays[#j1.plays]
    check("h: joiner: the host's state moves the copy (no player animation, as the game's own Open)",
        j1.nDirection == 1 and p and p.user == player.id and not p.playerAnim and p.force == nil)
    check("h: joiner: ...and asks nothing back", #doorEvts() == 0, doorEvts()[1])
    KCD2MP_W151DoorApply(K(lvl("j_door1")), -1, 1, 10, 20, 1)
    check("h: joiner: closed and locked like the host's", j1.nDirection == -1 and j1.bLocked == true and #doorEvts() == 0)
    -- two doors of one name: the pivot decides
    local ja = mkDoor("j_leafL", 50, 0, 0)
    local jb = mkDoor("j_leafR", 50.6, 0, 0)
    KCD2MP_W151DoorApply(K(lvl("j_leafR")), 1, 0, 50.6, 0, 0)
    check("h: joiner: of two leaves at one doorway, the one whose name hashes to the key moves", jb.nDirection == 1 and ja.nDirection == -1)
    KCD2MP_W151DoorApply("j_gone", 1, 0, 99, 99, 99)
    check("h: joiner: a door that is not here is counted, logged", logCount("WO151-DOOR the host's j_gone: no such door here") == 1)

    -- the joiner's own use: it moves at once, the host is asked
    local j2 = mkDoor("j_door2", 5, 5, 0)
    j2:OnUsed(player)
    e = doorEvts()
    check("h: joiner: his own use opens his door at once (the player animation, as in single player)", j2.nDirection == 1 and j2.plays[1].playerAnim == true)
    check("h: joiner: ...and asks the host (no unlock claimed)", #e == 1 and e[1] == "ask " .. K(lvl("j_door2")) .. " 1 0 5.00 5.00 0.00", e[1])
    -- the host's answer arrives while his own swing still runs: it waits, then settles
    local def0 = W.doorStats.deferred
    KCD2MP_W151DoorApply(K(lvl("j_door2")), -1, 0, 5, 5, 0)
    check("h: joiner: the host's answer waits for his own swing", j2.nDirection == 1 and W.doorStats.deferred == def0 + 1, W.doorStats.deferred - def0)
    j2.inUse = 0; runTimers()
    check("h: joiner: ...then settles to the host's", j2.nDirection == -1)
    local j3 = mkDoor("j_door3", 6, 6, 0); j3.bLocked = true; j3.hasKey = true
    clearLog()
    j3:OnUsed(player)
    e = doorEvts()
    check("h: joiner: his key opens a locked door -> one ask, unlock claimed", #e == 1 and e[1] == "ask " .. K(lvl("j_door3")) .. " 1 1 6.00 6.00 0.00", table.concat(e, " | "))
    local j4 = mkDoor("j_door4", 7, 7, 0); j4.bLocked = true
    clearLog()
    j4:Unlock()   -- the lockpick minigame's success (the engine unlocks it)
    e = doorEvts()
    check("h: joiner: a lockpicked door asks the host (unlock, no move)", #e == 1 and e[1] == "ask " .. K(lvl("j_door4")) .. " -1 1 7.00 7.00 0.00", e[1])
    local j5 = mkDoor("j_door5", 8, 8, 0); j5.bLocked = true
    clearLog()
    j5:OnUsed(player)   -- locked, no key: nothing changes
    check("h: joiner: a locked door he cannot open asks nothing", #doorEvts() == 0)
    local j6 = mkDoor("j_door6", 9, 9, 0)
    clearLog()
    j6:DoPlayAnimation(1, nil, nil, nil, false, "npc:3")   -- his world's own (paused copies, scripts): never news
    check("h: joiner: a door his own world moves is not sent", #doorEvts() == 0)

    -- the session ends: nothing either way
    NOW = NOW + 11
    clearLog()
    local j7 = mkDoor("j_door7", 1, 2, 3)
    j7:OnUsed(player)
    check("h: no agent tick for 10 s -> doors are single player again (nothing asked)", j7.nDirection == 1 and #doorEvts() == 0)
    check("h: mp_door_sync reports and toggles", KCD2MP_W151SetDoorSync("off") and W.doorSync == false
        and KCD2MP_W151SetDoorSync("on") and W.doorSync == true and not KCD2MP_W151SetDoorSync("maybe"))
    check("h: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (i) 3.4: the scene guard's Lua half -- the paused copies near this player are resumed and held (never
--     paused or parked) until the release; a dialogue the game forces on a paused copy resumes it.
do
    local W = KCD2MP.w151
    reset(); clearLog(); ERRS = {}; NOW = 3000; CMDS = {}
    check("i: mp_scene_guard is on by default and registered", W.sceneGuard == true and CCMDS["mp_scene_guard"] ~= nil)
    player.px, player.py, player.pz = 0, 0, 0
    local near1 = mkEntity("i_near1", 5, 0, 0); local near2 = mkEntity("i_near2", 0, 30, 0); local far = mkEntity("i_far", 300, 0, 0)
    for _, e in ipairs({ near1, near2, far }) do
        KCD2MP.npcPuppets[e:GetName()] = { owner = 0 }
        KCD2MP._npcPaused[e:GetName()] = NOW
    end
    local n = KCD2MP_W151SceneResume("zranenyLovci_hideout", 40)
    check("i: the paused copies within 40 m are resumed (the far one is not)", n == 2 and cmdCount("wh_ai_ResumeNPC i_near1") == 1
        and cmdCount("wh_ai_ResumeNPC i_near2") == 1 and cmdCount("wh_ai_ResumeNPC i_far") == 0, n)
    check("i: ...held", KCD2MP_W151SceneHolds("i_near1") and KCD2MP_W151SceneHolds("i_near2") and not KCD2MP_W151SceneHolds("i_far"))
    check("i: ...logged", logCount("WO151-SCENE resume scene=zranenyLovci_hideout copies=2") == 1, lastLog("WO151-SCENE"))
    CMDS = {}
    KCD2MP_W151SceneRelease("ReleaseScene")
    check("i: the release pauses them again", cmdCount("wh_ai_PauseNPC i_near1") == 1 and cmdCount("wh_ai_PauseNPC i_near2") == 1
        and not KCD2MP_W151SceneHolds("i_near1"), table.concat(CMDS, " | "))
    check("i: ...logged", logCount("WO151-SCENE release why=ReleaseScene copies=2 paused_again=2") == 1, lastLog("WO151-SCENE release"))
    -- a load: dropped, never paused (a load forgets suspensions; the puppet system pauses again)
    KCD2MP._npcPaused["i_near1"] = NOW
    KCD2MP_W151SceneResume("s2", 40)
    CMDS = {}
    KCD2MP_W151SceneRelease("load", true)
    check("i: a load drops the holds without a pause", cmdCount("wh_ai_PauseNPC") == 0 and not KCD2MP_W151SceneHolds("i_near1"))
    -- a hold never outlives sceneHoldMaxS
    KCD2MP._npcPaused["i_near2"] = NOW
    KCD2MP_W151SceneResume("s3", 40)
    NOW = NOW + W.sceneHoldMaxS + 1
    check("i: a hold older than sceneHoldMaxS is dropped", not KCD2MP_W151SceneHolds("i_near2"))
    -- off: nothing resumed
    W.sceneGuard = false
    KCD2MP._npcPaused["i_near1"] = NOW
    CMDS = {}
    check("i: mp_scene_guard off -> nothing resumed", KCD2MP_W151SceneResume("s4", 40) == 0 and cmdCount("wh_ai_ResumeNPC") == 0)
    W.sceneGuard = true

    -- a dialogue the game forces on a paused copy (the twins' 20 s wait): the copy runs for it
    reset(); clearLog(); NOW = 3500; CMDS = {}
    KCD2MP_W137Session(false, true, true)
    local h = mkEntity("tvid_hunter", 3, 0, 0)
    KCD2MP.npcPuppets["tvid_hunter"] = { owner = 0 }
    KCD2MP._npcPaused["tvid_hunter"] = NOW
    KCD2MP_W137TalkAttempt(957, "Dude,tvid_hunter")
    local t = KCD2MP.w137.talking["tvid_hunter"]
    check("i: a forced dialogue on a paused copy resumes it for the conversation", t ~= nil and t.forced and t.started and t.resumed
        and cmdCount("wh_ai_ResumeNPC tvid_hunter") == 1, t and tostring(t.resumed))
    check("i: ...logged", logCount("(WO151: resumed for it)") == 1, lastLog("WO137-TALK forced"))
    CMDS = {}
    KCD2MP_W137TalkEndNow("tvid_hunter", "dialog-ended")
    check("i: ...and paused again at its end", cmdCount("wh_ai_PauseNPC tvid_hunter") == 1, table.concat(CMDS, " | "))
    check("i: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (j) 3.5: the whistle -- this player's call press goes out once (never a held key's stream); the partner's
--     plays the game's own trigger at his avatar.
do
    local W = KCD2MP.w151
    reset(); clearLog(); ERRS = {}; NOW = 4000
    check("j: mp_whistle is on by default and registered", W.whistle == true and CCMDS["mp_whistle"] ~= nil)
    KCD2MP_W151OnAction("call", "press")
    KCD2MP_W151OnAction("call", "hold")
    KCD2MP_W151OnAction("call", "press")   -- within 1.5 s
    KCD2MP_W151OnAction("use", "press")
    local e = evts("w151_emote")
    check("j: one call press -> one whistle out", #e == 1 and e[1] == "whistle", #e)
    NOW = NOW + 2; KCD2MP_W151OnAction("call", "press")
    check("j: ...the next press later is another", #evts("w151_emote") == 2)
    local PLAYED = {}
    AudioUtils = { PlayAudioTrigger = function(ent, trig) PLAYED[#PLAYED + 1] = { ent = ent, trig = trig } end }
    local av = mkEntity("j_avatar", 10, 0, 0)
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    KCD2MP.ghosts["2"] = { entity = av }
    check("j: the partner's whistle plays at his avatar (v_horse_whistle)", KCD2MP_W151PlayEmote(2, "whistle") == true
        and #PLAYED == 1 and PLAYED[1].ent == av and PLAYED[1].trig == "v_horse_whistle")
    check("j: no avatar -> nothing played, counted", KCD2MP_W151PlayEmote(5, "whistle") == false and W.whistleStats.noAvatar >= 1)
    W.whistle = false
    KCD2MP_W151OnAction("call", "press"); NOW = NOW + 2; KCD2MP_W151OnAction("call", "press")
    check("j: mp_whistle off -> nothing sent, nothing played", #evts("w151_emote") == 2 and KCD2MP_W151PlayEmote(2, "whistle") == false)
    W.whistle = true
    check("j: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (k) 3.1: a joiner not yet caught up starts no conversation with the host's people (a toast); caught up, he does.
do
    local W = KCD2MP.w151
    reset(); clearLog(); ERRS = {}; NOW = 5000; TOASTS = {}
    check("k: mp_quest_catchup is on by default and registered", W.questCatchUp == true and CCMDS["mp_quest_catchup"] ~= nil)
    KCD2MP_W137Session(false, true, true)
    local smith = mkEntity("k_smith", 2, 0, 0)
    KCD2MP.npcPuppets["k_smith"] = { owner = 0 }
    KCD2MP_W151CaughtUp(false)
    check("k: not caught up -> the talk to a host NPC waits", KCD2MP_W151HostTalkBlocked(smith, player) == true
        and TOASTS[#TOASTS] == "Catching up with the host's world -- try again in a moment.", TOASTS[#TOASTS])
    local own = mkEntity("k_local", 3, 0, 0)   -- not a host copy: this game's own
    check("k: ...a person who is not the host's is never gated", KCD2MP_W151HostTalkBlocked(own, player) == false)
    KCD2MP_W151CaughtUp(true)
    check("k: caught up -> the talk goes", KCD2MP_W151HostTalkBlocked(smith, player) == false)
    check("k: ...logged both ways", logCount("WO151-CATCHUP catching up") == 1 and logCount("WO151-CATCHUP caught up") == 1)
    W.questCatchUp = false; KCD2MP_W151CaughtUp(false)
    check("k: mp_quest_catchup off -> never gated", KCD2MP_W151HostTalkBlocked(smith, player) == false)
    W.questCatchUp = true; KCD2MP_W151CaughtUp(true)
    check("k: no Lua errors", #ERRS == 0, ERRS[1])
end

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
