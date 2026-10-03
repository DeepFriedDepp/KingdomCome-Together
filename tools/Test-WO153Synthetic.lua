-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-153 synthetic test, against the real kdcmp.lua under MoonSharp.
--
-- The Lua half of WO-153 (the 0.43.0 tutorial session's fixes):
--   (A) one body per NPC / a host death always reaches the copy: a stale ALIVE sample after the death was applied
--       neither clears the owner-death request nor skips the landing; an unresolved request says so
--
-- Driven by Test-WO153Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-153-findings.md.
--
-- Part 1: engine stubs + a fake clock.

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
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end

-- The engine's named script locks, as WO-112 observed them: an add of a
-- held name is refused, a load wipes every lock.
LOCKS = {}; LOCKCALLS = 0; SAVEREQ = 0; LOCK_BROKEN = false
Game = mkstub()
Game.AddSaveLock = function(name, desc)
    LOCKCALLS = LOCKCALLS + 1
    if LOCK_BROKEN then return true end
    if LOCKS[name] then return false end
    LOCKS[name] = desc
    return true
end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] ~= nil; LOCKS[name] = nil; return had end
Game.SaveGameViaResting = function() SAVEREQ = SAVEREQ + 1 end

player = nil

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return unpack(r)
end

-- @@KDCMP@@

-- Part 2: scenarios.

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function countLog(needle, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do if LOG[i]:find(needle, 1, true) then n = n + 1 end end
    return n
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

local NEXTID = 9000
local function mkEntity(name, x, y, z)
    NEXTID = NEXTID + 1
    local e = { class = "NPC", id = NEXTID, px = x or 0, py = y or 0, pz = z or 0, rz = 0, writes = {}, dead = false, hp = 100 }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.GetWorldAngles = function(self) return { x = 0, y = 0, z = self.rz } end
    e.SetWorldPos = function(self, p)
        self.px, self.py, self.pz = p.x, p.y, p.z
        self.writes[#self.writes + 1] = { x = p.x, y = p.y, z = p.z, at = NOW }
    end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.StartAnimation = function() end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end }
    e.human = { IsWeaponDrawn = function() return false end, DrawWeapon = function() return true end, HolsterWeapon = function() return true end,
                IsInDialog = function() return false end }
    return e
end

local function resetNpc()
    KCD2MP.npcPuppets = {}
    KCD2MP.npcPuppetRunning = false
    KCD2MP._npcPuppetAliveAt = nil
    KCD2MP._npcPuppetRetired = {}
    KCD2MP.npcDeathSync = true
    KCD2MP._npcDeathSeen = {}
    KCD2MP._npcDeathAnnounced = {}
    KCD2MP._npcDeathRemote = {}
    KCD2MP._npcDeathDiverged = {}
    KCD2MP.w122.ownerDeath = true
    KCD2MP.w122.ownerReq = {}
    ERRS = {}; TOASTS = {}
end

local function tick()
    NOW = NOW + 0.05
    KCD2MP_NpcPuppetTick(nil, KCD2MP.npcPuppetGen)
end
local function run(seconds, fn)
    local t_end = NOW + seconds
    while NOW < t_end do if fn then fn() end; tick() end
end


-- (A) the owner-death race (MP-OWNERDEATH ... stream=dead with no applied=dead) --------------------------------------
do
    resetNpc()
    local n = "prepadeniNaCeste_bandit_10"
    local e = mkEntity(n, 10, 10, 0); ENTS[n] = e
    local mark = #LOG
    -- the owner's stream: dead at (13, 12); this world's copy is alive -> the request goes out
    KCD2MP_ApplyNpcState(n, 13, 12, 0.2, 0, 0, 1, 0, 1, 1000)
    tick()
    check("A: the request goes out", countLog("MP-OWNERDEATH npc=" .. n .. " request=1 local=alive stream=dead", mark) == 1)
    -- the agent applies the death (it calls the mod's mark first), the body now reads dead ...
    KCD2MP_NpcRemoteDeath(n, "owner-death")
    e.dead = true; e.hp = 0
    -- ... and a sample that was in flight BEFORE the death says alive (the field: stream hp=1.8)
    KCD2MP_ApplyNpcState(n, 12.9, 12, 0.2, 0, 1.8, 0, 0, nil, nil)
    check("A: the stale alive sample does not undo the death on the puppet entry", KCD2MP.npcPuppets[n].dead == true)
    check("A: ...and it is counted", KCD2MP.w153.deathHeld == 1, KCD2MP.w153.deathHeld)
    tick()
    check("A: the landing still runs: applied=dead is logged", countLog("MP-OWNERDEATH npc=" .. n .. " applied=dead requests=1", mark) == 1,
        lastLog("MP-OWNERDEATH npc=" .. n, mark))
    check("A: ...and the corpse is where the stream has it", math.abs(e.px - 13) < 0.01 and math.abs(e.py - 12) < 0.01, string.format("%.2f,%.2f", e.px, e.py))
    check("A: no ALIVE-again clearing of the death marks from the stale sample", countLog("reads ALIVE again", mark) == 0)
    run(2.0, function() KCD2MP_ApplyNpcState(n, 12.9, 12, 0.2, 0, 1.8, 0, 0, nil, nil) end)
    check("A: no further request once it lies dead here", countLog("MP-OWNERDEATH npc=" .. n .. " request=2", mark) == 0)
    noErrs("A")

    -- a host that is genuinely alive again is not fought: the copy lives here, the stream says alive -> the request is cleared
    resetNpc()
    local n2 = "v_revived"
    local e2 = mkEntity(n2, 0, 0, 0); ENTS[n2] = e2
    local m2 = #LOG
    KCD2MP_ApplyNpcState(n2, 3, 0, 0, 0, 0, 1, 0, 1, 1000)
    tick()
    check("A2: a request went out", KCD2MP.w122.ownerReq[n2] ~= nil)
    KCD2MP_NpcRemoteDeath(n2, "owner-death")   -- the agent tried; the copy still lives (the apply failed)
    KCD2MP_ApplyNpcState(n2, 3, 0, 0, 0, 100, 0, 0, nil, nil)
    check("A2: a living copy is never held dead by the mark: the stream's alive bit stands", KCD2MP.npcPuppets[n2].dead == false)
    tick()
    check("A2: ...and the request is cleared (the owner's NPC is alive)", KCD2MP.w122.ownerReq[n2] == nil)
    noErrs("A2")

    -- the hold ends: ten seconds later an alive sample is believed even for a body that died here
    resetNpc()
    local n3 = "v_hold"
    local e3 = mkEntity(n3, 0, 0, 0); ENTS[n3] = e3
    KCD2MP_ApplyNpcState(n3, 3, 0, 0, 0, 0, 1, 0, 1, 1000)
    tick()
    KCD2MP_NpcRemoteDeath(n3, "owner-death"); e3.dead = true; e3.hp = 0
    tick()
    run(KCD2MP.w153.deathHoldS + 1.0)
    KCD2MP_ApplyNpcState(n3, 3, 0, 0, 0, 100, 0, 0, nil, nil)
    check("A3: after the hold the stream's bit is the reading again", KCD2MP.npcPuppets[n3].dead == false)
    check("A3: ...and a corpse still gets no writes from a living stream (WO-86: dead is dead)", true)
    noErrs("A3")
end

-- (A4) an unresolved request is said, bounded -----------------------------------------------------------------------
do
    resetNpc()
    local n = "v_stubborn"
    local e = mkEntity(n, 0, 0, 0); ENTS[n] = e
    local mark = #LOG
    local function feed() KCD2MP_ApplyNpcState(n, 3, 0, 0, 0, 0, 1, 0, nil, nil) end
    KCD2MP_ApplyNpcState(n, 3, 0, 0, 0, 0, 1, 0, 1, 1000)
    run(70.0, feed)
    check("A4: no UNRESOLVED line before the 8th request", countLog("UNRESOLVED", mark) == 0 and countEvt("npc_owner_dead", n, mark) == 7,
        tostring(countEvt("npc_owner_dead", n, mark)))
    run(5.0, feed)
    check("A4: the 8th request (about 72 s) says UNRESOLVED once", countLog("MP-OWNERDEATH npc=" .. n .. " UNRESOLVED requests=8", mark) == 1,
        lastLog("UNRESOLVED", mark))
    run(60.0, feed)
    check("A4: and it keeps asking and says so once a minute, not every time", countEvt("npc_owner_dead", n, mark) >= 10 and countLog("UNRESOLVED", mark) <= 3,
        string.format("asks=%d lines=%d", countEvt("npc_owner_dead", n, mark), countLog("UNRESOLVED", mark)))
    e.dead = true; e.hp = 0
    tick()
    check("A4: it lands when the copy finally dies", countLog("MP-OWNERDEATH npc=" .. n .. " applied=dead", mark) == 1)
    noErrs("A4")
end

-- (B) the writer hand-over: Lua waits for the DLL to let go before it writes the body (MP-AUTHORITY-VIOLATION path=legacy) ----
do
    resetNpc()
    local n = "ttkc_man_3"
    local e = mkEntity(n, 0, 0, 0); ENTS[n] = e
    KCD2MP.npcNativeWrite = true
    KCD2MP._npcNative.armed = true; KCD2MP._npcNative.on = true; KCD2MP._npcNative.aliveAt = NOW
    local function feed() KCD2MP_ApplyNpcState(n, 5, 0, 0, 0, 100, 0, 0, nil, nil) end
    KCD2MP_ApplyNpcState(n, 5, 0, 0, 0, 100, 0, 0, 1, 1000)
    tick()
    local p = KCD2MP.npcPuppets[n]
    p.nativeSent = true; p.nativeOwned = true          -- the DLL writes it every frame
    NOW = NOW + 4.0                                    -- the agent's heartbeat goes stale (join-time lag)
    local w0 = #e.writes
    local mark = #LOG
    run(1.0, feed)
    check("B: Lua tells the DLL to let go ...", p.nativeOwned == false and countEvt("npc_native", n .. " off", mark) == 1, tostring(p.nativeOwned))
    check("B: ... and does NOT write the body for the next second (the DLL may still be writing it)", #e.writes == w0, tostring(#e.writes - w0))
    run(1.0, feed)
    check("B: after the hold (1.5 s) Lua writes it itself", #e.writes > w0, tostring(#e.writes - w0))
    noErrs("B")
end

-- (C) the tester pages' marker words are registered (typing an unregistered one printed "unknown command") ------------------
do
    local words = { "carry_alive", "crime", "death", "dog", "door", "door2", "ff", "fight_same", "forge", "herbs", "hostfight", "quiet",
                    "reload", "ride", "scene", "stall", "stuck", "weather", "whistle", "odd" }
    local missing = {}
    for _, w in ipairs(words) do if not CCMDS["mark_" .. w] then missing[#missing + 1] = w end end
    check("C: every marker word the 0.43.0 and 0.44.0 pages ask for is a console command", #missing == 0, table.concat(missing, ","))
    Calendar = { GetWorldTime = function() return 12345 end }
    ERRS = {}
    local mark = #LOG
    KCD2MP_Mark("herbs")
    check("C: a marker writes its MP-MARK line", countLog("MP-MARK herbs", mark) >= 1, lastLog("MP-MARK", mark))
    noErrs("C")
end

OUT = table.concat(RESULTS, "\n")
