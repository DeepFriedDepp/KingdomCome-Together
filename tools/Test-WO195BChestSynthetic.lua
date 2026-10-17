-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-195B 2a synthetic test, against the real kdcmp.lua under MoonSharp (harness from Test-WO151Synthetic.lua).
-- A joiner's unlocked chest (his lockpick or his key) unlocks the host's same chest, through the door channel's ask:
--   (a) defaults: mp_chest_sync on, registered, in the build line; nothing errors at load
--   (b) the joiner: his pick (the lockpick callback, the engine's unlock inside a pick window) and his key each send ONE
--       ask carrying the chest bit; an unlock that is not his (a script's, another user's, a stale pick, a cart) asks
--       nothing; switch off or no session = single player; the door path is unchanged
--   (c) the host: the game's own Stash:Unlock on the chest at the pivot (so the lock record keeps it through a load);
--       no match / ambiguous / no claim / already / the host's own Henry at it / a failing Unlock / not the host /
--       switch off; a door and a chest at one pivot never answer for each other
-- What this proves: the Lua half, against stand-ins of the game's Stash and AnimDoor class tables. Live evidence is
-- the maintainer's two-player check.

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
System.GetEntity = function(id) for _, e in pairs(ENTS) do if e.id == id then return e end end return nil end
System.GetEntitiesInSphereByClass = function(pos, r, cls)
    local out = {}
    for _, e in pairs(ENTS) do
        if e.class == cls and ((e.px - pos.x) ^ 2 + (e.py - pos.y) ^ 2 + (e.pz - pos.z) ^ 2) <= r * r then out[#out + 1] = e end
    end
    return out
end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Physics.RayWorldIntersection = function() return nil end
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

player = { id = 1, this = { id = 777 }, GetName = function(self) return "Dude" end,
           px = 0, py = 0, pz = 0,
           GetWorldPos = function(self) return { x = player.px, y = player.py, z = player.pz } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           GetLinkedParent = function() return nil end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end },
           human = { IsMounted = function() return false end, GetHorse = function() return nil end,
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
local function runTimers()
    local due = TIMERS; TIMERS = {}
    for _, t in ipairs(due) do pcall(t.f) end
end

-- (a) defaults -- read BEFORE any scenario touches them.
do
    local b = lastLog("WO151-BUILD") or ""
    check("a: mp_chest_sync is on by default", KCD2MP.w151.chestSync == true)
    check("a: the build line says chest_sync=on", b:find("chest_sync=on", 1, true) ~= nil, b)
    check("a: the door switch is still in the build line, on", b:find("door_sync=on", 1, true) ~= nil)
    check("a: mp_chest_sync is registered", CCMDS["mp_chest_sync"] ~= nil and CCMDS["mp_chest_sync"].body:find("KCD2MP_W195BSetChestSync", 1, true) ~= nil)
    check("a: mp_door_sync is still registered", CCMDS["mp_door_sync"] ~= nil)
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
    ENTS[name] = e
    return e
end

local function reset()
    if KCD2MP.npcSilence then KCD2MP.npcSilence.tickAt = nil; KCD2MP.npcSilence.agentAt = -1e9; KCD2MP.npcSilence.silent = {} end
    KCD2MP.wo102.authorityHost = true; KCD2MP.wo102.authorityPause = true
    KCD2MP.hitSensorOn = false
    KCD2MP.npcPuppets = {}; KCD2MP.npcTracked = {}
    KCD2MP.npcPuppetRunning = false; KCD2MP._npcDivergeUntil = {}
    KCD2MP._npcPaused = {}; KCD2MP._npcPauseExec = {}; KCD2MP._npcEverPaused = {}; KCD2MP._npcResumePending = {}
    KCD2MP.ghosts = {}; KCD2MP.horseGhosts = {}
    KCD2MP.w136.ridden = {}
    ENTS = {}; SPHERE = {}; TIMERS = {}; ERRS = {}; CMDS = {}; MSGS = {}; TOASTS = {}
    clearLog()
end

-- ---- stand-ins for the game's class tables -----------------------------------------------------------------------
-- Stash (Scripts/Entities/.../AnimStash.lua): Unlock clears bLocked and tells the game's lock record
-- (lockBase:ReportUnlocked); a load / spawn (Reset) re-locks from the authored property unless that record says
-- unlocked; OnLockpicked is the pick's success callback; OnUsed unlocks with a held key.
LOCKREC = {}
MODE_DIRECT = false      -- true: the pick callback / the key write the flag without going through Unlock
Stash = {}
function Stash:Lock() self.bLocked = true; self.locks = (self.locks or 0) + 1 end
function Stash:Unlock()
    self.bLocked = false; self.unlocks = (self.unlocks or 0) + 1
    LOCKREC[self:GetName()] = "unlocked"      -- self.lockBase:ReportUnlocked()
end
function Stash:Reset() self.bLocked = (self.authoredLocked == true) and LOCKREC[self:GetName()] ~= "unlocked" end
function Stash:OnLockpicked()
    if MODE_DIRECT then self.bLocked = false else self:Unlock() end
end
function Stash:Lockpick(user, slot) self.picking = true end
function Stash:OnUsed(user, slot)
    if self.bLocked == true then
        if self.hasKey then
            if MODE_DIRECT then self.bLocked = false else self:Unlock() end
        end
        return
    end
    self.bOpened = 1
end
-- AnimDoor, as Test-WO151Synthetic.lua models it
AnimDoor = {}
function AnimDoor:DoPlayAnimation(direction, forceTime, useSound, customAnim, usePlayerAnim, userId)
    self.inUse = 1; self.nDirection = direction; self.lastUsedBy = userId; self.curAnim = "door_anim"
end
function AnimDoor:IsInUse() return self.inUse == 1 end
function AnimDoor:IsOpen() return self.nDirection > 0 end
function AnimDoor:Close() if self.nDirection == 1 then self:DoPlayAnimation(-1, nil, nil, nil, nil, player.id) end end
function AnimDoor:Lock(dontClose) if not dontClose and self:IsOpen() then self:Close() end; self.bLocked = true end
function AnimDoor:Unlock() self.bLocked = false end
function AnimDoor:Lockpick(user, slot) self.picking = true end
function AnimDoor:OnUsed(user, slot)
    local direction = -self.nDirection
    if self.bLocked == true then
        if self.hasKey then self:Unlock(); self:DoPlayAnimation(direction, nil, nil, nil, true, user.id) end
    elseif user.id == player.id then
        self:DoPlayAnimation(direction, nil, nil, nil, true, user.id)
    end
end

local function lvl(n) return "stash[Chest/" .. n .. "_c1d92081-d74b-4955-ade8-384e71325794]" end
local function dlvl(n) return "AnimDoor[structures/living/houses/unique/kopanina/" .. n .. ":Door/door_village_left1_8c208501]" end
local function K(n) return KCD2MP.w151.doorNameHash(n) end
local function mkStash(name, x, y, z, locked, cls)
    local e = mkEntity(name, x, y, z, cls or "Stash")
    setmetatable(e, { __index = Stash })
    e.bLocked = locked == true; e.authoredLocked = locked == true; e.bOpened = 0
    return e
end
local function mkDoor(name, x, y, z)
    local d = mkEntity(dlvl(name), x, y, z, "AnimDoor")
    setmetatable(d, { __index = AnimDoor })
    d.nDirection = -1; d.bLocked = false; d.inUse = 0; d.nUpdateAfterLoad = 0
    return d
end
local function askEvts() return evts("w151_door") end
local function tickJoiner() NOW = NOW + 0.01; KCD2MP_W137Session(false, true, true) end
local function tickHost() NOW = NOW + 0.01; KCD2MP_W137Session(true, false, true) end

local W = KCD2MP.w151

-- ---------------------------------------------------------------- (b) the joiner
do
    reset(); ERRS = {}; NOW = 100; MODE_DIRECT = false
    KCD2MP_W139Install()          -- the game's Stash.OnUsed wrap (the crime reports), where the use is read
    tickJoiner()
    check("b: the chest wraps go in at the session tick, once", logCount("WO195B-CHEST wrapped Stash.Unlock/OnLockpicked/Lockpick") == 1)
    tickJoiner()
    check("b: ...and stay once", logCount("WO195B-CHEST wrapped Stash.Unlock/OnLockpicked/Lockpick") == 1)
    check("b: the crime wrap on Stash.OnUsed is the only one (no stacking)", Stash.OnUsed == KCD2MP.w139.stashWrap)

    -- b1: the pick succeeds (the callback unlocks through the game's Unlock)
    local s1 = mkStash(lvl("b1"), 10.5, -20.25, 30, true)
    clearLog()
    s1:OnLockpicked()
    local e = askEvts()
    check("b1: his pick unlocks his chest and sends ONE ask with the chest bit (flags 3) and no move (dir 0)",
        s1.bLocked == false and #e == 1 and e[1] == "ask " .. K(lvl("b1")) .. " 0 3 10.50 -20.25 30.00", table.concat(e, " | "))
    check("b1: the event is the agent's wire line (ask <key> 0 3 x y z)", e[1] ~= nil and e[1]:match("^ask d%x%x%x%x%x%x%x%x 0 3 %-?%d+%.%d%d %-?%d+%.%d%d %-?%d+%.%d%d$") ~= nil, e[1])
    check("b1: logged as asked, with the reason", logCount("WO195B-CHEST asked " .. K(lvl("b1")) .. " (lockpicked here) -> the host (unlock)") == 1, lastLog("WO195B-CHEST"))
    s1:Lock(); clearLog()
    s1:OnLockpicked()
    check("b1: the same pick is not asked twice (a second report inside 2 s)", #askEvts() == 0, #askEvts())
    NOW = NOW + 3; tickJoiner(); s1:Lock(); clearLog()
    s1:OnLockpicked()
    check("b1: a later pick of the re-locked chest asks again", #askEvts() == 1)

    -- b2: the callback writes the flag itself (no Unlock): the state change is read
    MODE_DIRECT = true
    local s2 = mkStash(lvl("b2"), 11, 21, 1, true)
    clearLog()
    s2:OnLockpicked()
    e = askEvts()
    check("b2: a pick whose callback writes the flag (no Unlock) still asks", s2.bLocked == false and #e == 1 and e[1] == "ask " .. K(lvl("b2")) .. " 0 3 11.00 21.00 1.00", table.concat(e, " | "))
    MODE_DIRECT = false

    -- b3: his key
    local s3 = mkStash(lvl("b3"), 12, 22, 2, true); s3.hasKey = true
    clearLog()
    s3:OnUsed(player)
    e = askEvts()
    check("b3: his key unlocks a locked chest -> ONE ask, chest + unlock claimed", s3.bLocked == false and #e == 1 and e[1] == "ask " .. K(lvl("b3")) .. " 0 3 12.00 22.00 2.00", table.concat(e, " | "))
    check("b3: logged as the key", logCount("(key) -> the host (unlock)") == 1)
    MODE_DIRECT = true
    local s3b = mkStash(lvl("b3b"), 13, 23, 3, true); s3b.hasKey = true
    clearLog()
    s3b:OnUsed(player)
    check("b3: a key that writes the flag itself asks too", s3b.bLocked == false and #askEvts() == 1, #askEvts())
    MODE_DIRECT = false

    -- b4: not his
    local s4 = mkStash(lvl("b4"), 14, 24, 4, true); s4.hasKey = true
    clearLog()
    s4:OnUsed({ id = 99 })   -- somebody else's use (never this player)
    check("b4: another user's key unlock asks nothing", s4.bLocked == false and #askEvts() == 0, #askEvts())
    local s5 = mkStash(lvl("b5"), 15, 25, 5, true)
    clearLog()
    s5:OnUsed(player)        -- locked, no key: nothing changes
    check("b4: a locked chest he cannot open asks nothing", s5.bLocked == true and #askEvts() == 0)
    local s6 = mkStash(lvl("b6"), 16, 26, 6, false)
    clearLog()
    s6:OnUsed(player)
    check("b4: using an unlocked chest asks nothing", s6.bOpened == 1 and #askEvts() == 0)
    local own0 = W.chestStats.ownUnlocks
    local s7 = mkStash(lvl("b7"), 17, 27, 7, true)
    clearLog()
    s7:Unlock()              -- a quest's / the engine's unlock of this copy, no pick of his
    check("b4: an unlock with no pick and no key of his asks nothing", #askEvts() == 0 and W.chestStats.ownUnlocks == own0 + 1)
    check("b4: ...and says so once", logCount("WO195B-CHEST own unlock of " .. K(lvl("b7")) .. " here (no pick of his, no key): not asked") == 1)

    -- b5: the engine's unlock inside a pick window (the use action's Lockpick began it)
    local s8 = mkStash(lvl("b8"), 18, 28, 8, true)
    s8:Lockpick(player)
    NOW = NOW + 20; tickJoiner()
    clearLog()
    s8:Unlock()
    e = askEvts()
    check("b5: an unlock after his pick began asks (the engine's own success, no callback)", #e == 1 and e[1] == "ask " .. K(lvl("b8")) .. " 0 3 18.00 28.00 8.00", table.concat(e, " | "))
    s8:Lock(); NOW = NOW + 3; tickJoiner(); clearLog()
    s8:Unlock()
    check("b5: ...once per pick (the window is spent)", #askEvts() == 0)
    local s9 = mkStash(lvl("b9"), 19, 29, 9, true)
    s9:Lockpick(player)
    NOW = NOW + KCD2MP_W151_PICK_WINDOW_S + 1; tickJoiner()
    clearLog()
    s9:Unlock()
    check("b5: an unlock long after his pick began is not his", #askEvts() == 0)
    local s10 = mkStash(lvl("b10"), 20, 30, 10, true)
    s10:Lockpick({ id = 99 })
    clearLog()
    s10:Unlock()
    check("b5: another user's pick is not his", #askEvts() == 0)

    -- b6: a chest is class Stash only
    local cart = mkStash(lvl("cart"), 21, 31, 11, true, "CartStash")
    clearLog()
    cart:OnLockpicked()
    check("b6: a CartStash he picks asks nothing (class Stash only)", cart.bLocked == false and #askEvts() == 0, #askEvts())

    -- b7: the door path is unchanged on the joiner
    local d1 = mkDoor("b_door1", 5, 5, 0); d1.bLocked = true; d1.hasKey = true
    clearLog()
    d1:OnUsed(player)
    e = askEvts()
    check("b7: a door his key opens still asks with the lock bit only (flags 1), a move", #e == 1 and e[1] == "ask " .. K(dlvl("b_door1")) .. " 1 1 5.00 5.00 0.00", table.concat(e, " | "))
    local d2 = mkDoor("b_door2", 6, 6, 0); d2.bLocked = true
    d2:Lockpick(player)
    NOW = NOW + 20; tickJoiner(); clearLog()
    d2:Unlock()
    e = askEvts()
    check("b7: a lockpicked door still asks (unlock, no move, flags 1)", #e == 1 and e[1] == "ask " .. K(dlvl("b_door2")) .. " -1 1 6.00 6.00 0.00", table.concat(e, " | "))
    check("b7: and no chest line for a door", logCount("WO195B-CHEST asked " .. K(dlvl("b_door2"))) == 0)

    -- b8: the switch
    check("b8: mp_chest_sync reports and toggles", KCD2MP_W195BSetChestSync("off") and W.chestSync == false
        and KCD2MP_W195BSetChestSync("on") and W.chestSync == true and not KCD2MP_W195BSetChestSync("maybe"))
    check("b8: the report line carries the switch and the counters", logCount("WO195B-CHEST toggle mp_chest_sync chest_sync=on role=joiner") >= 1, lastLog("WO195B-CHEST toggle"))
    KCD2MP_W195BSetChestSync("off")
    local s11 = mkStash(lvl("b11"), 22, 32, 12, true)
    clearLog()
    s11:OnLockpicked()
    check("b8: mp_chest_sync off -> his own chest still unlocks (single player), nothing asked", s11.bLocked == false and #askEvts() == 0)
    KCD2MP_W195BSetChestSync("on")
    local d3 = mkDoor("b_door3", 7, 7, 0); d3.bLocked = true
    KCD2MP_W195BSetChestSync("off")
    d3:Lockpick(player); NOW = NOW + 20; tickJoiner(); clearLog()
    d3:Unlock()
    check("b8: mp_chest_sync off leaves the door path alone (a lockpicked door still asks)", #askEvts() == 1, #askEvts())
    KCD2MP_W195BSetChestSync("on")

    -- b9: no agent tick for 10 s = single player
    NOW = NOW + 11
    local s12 = mkStash(lvl("b12"), 23, 33, 13, true)
    clearLog()
    s12:OnLockpicked()
    check("b9: no agent tick for 10 s -> chests are single player again (nothing asked)", s12.bLocked == false and #askEvts() == 0)
    tickJoiner()

    -- b10: a host never asks (his own chest is his)
    tickHost()
    local s13 = mkStash(lvl("b13"), 24, 34, 14, true)
    clearLog()
    s13:OnLockpicked()
    check("b10: the host's own pick asks nothing", s13.bLocked == false and #askEvts() == 0)
    check("b10: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (c) the host
do
    reset(); ERRS = {}; NOW = 300; MODE_DIRECT = false; LOCKREC = {}
    tickHost()
    local av = mkEntity("c_avatar", 11, 20, 1)
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    KCD2MP.ghosts["1"] = { entity = av }

    -- c1: the unlock
    local c1 = mkStash(lvl("c1"), 40, 50, 6, true)
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c1")), 1, 40, 50, 6)
    check("c1: the host's chest at that pivot is unlocked through the game's own Stash:Unlock", c1.bLocked == false and c1.unlocks == 1, tostring(c1.unlocks))
    check("c1: ...which wrote the game's lock record", LOCKREC[lvl("c1")] == "unlocked")
    check("c1: logged as unlocked, the match by name", logCount("WO195B-CHEST ask from 1 for " .. K(lvl("c1")) .. " unlocked (match=name)") == 1, lastLog("WO195B-CHEST"))
    check("c1: nothing is broadcast (the host sends no chest state)", #askEvts() == 0, #askEvts())
    check("c1: the host never locks anything here", c1.locks == nil)
    c1:Reset()   -- a save and a load: the spawn re-locks from the authored property unless the lock record says unlocked
    check("c1: after a reload (Reset) it is still unlocked -- the record, not a flag, holds it", c1.bLocked == false)
    local ctl = mkStash(lvl("c1ctl"), 41, 51, 6, true)
    ctl.bLocked = false      -- the negative control: a bare write of the flag
    ctl:Reset()
    check("c1: (control) a bare write of the flag would have been undone by the reload", ctl.bLocked == true)

    -- c2: already unlocked
    local c2 = mkStash(lvl("c2"), 42, 52, 6, false)
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c2")), 1, 42, 52, 6)
    check("c2: an already unlocked chest: no Unlock call, logged already", c2.unlocks == nil and c2.bLocked == false
        and logCount("WO195B-CHEST ask from 1 for " .. K(lvl("c2")) .. " already") == 1, lastLog("WO195B-CHEST"))
    c1.unlocks = 1
    KCD2MP_W195BChestAsked(1, K(lvl("c1")), 1, 40, 50, 6)   -- a duplicate ask after the first took
    check("c2: a duplicate ask is a no-op", c1.unlocks == 1 and logCount("for " .. K(lvl("c1")) .. " already") == 1)

    -- c3: no match
    local c3 = mkStash(lvl("c3"), 43, 53, 6, true)
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c3")), 1, 90, 90, 6)
    check("c3: no chest at the pivot -> no-match, nothing unlocked", c3.bLocked == true and c3.unlocks == nil
        and logCount("no-match: no chest (none) within 1 m of (90.00, 90.00, 6.00)") == 1, lastLog("WO195B-CHEST"))
    KCD2MP_W195BChestAsked(1, "d00000000", 1, 43, 53, 6)   -- a wrong name with one chest there: the only one there (the doors' rule)
    check("c3: a wrong name with exactly one chest there is that chest (the doors' rule), logged match=only", c3.bLocked == false and logCount("unlocked (match=only)") == 1, lastLog("WO195B-CHEST"))
    local ca = mkStash(lvl("c3a"), 60, 60, 6, true)
    local cb = mkStash(lvl("c3b"), 60.6, 60, 6, true)
    clearLog()
    KCD2MP_W195BChestAsked(1, "d00000000", 1, 60.3, 60, 6)
    check("c3: two chests there and no name matches -> never a guess (no-match, ambiguous), both stay locked",
        ca.bLocked == true and cb.bLocked == true and logCount("no-match: no chest (ambiguous)") == 1, lastLog("WO195B-CHEST"))
    KCD2MP_W195BChestAsked(1, K(lvl("c3b")), 1, 60.3, 60, 6)
    check("c3: ...the name picks the right one of two", cb.bLocked == false and ca.bLocked == true)
    local far = mkStash(lvl("c3far"), 70, 70, 6, true)
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c3far")), 1, 71.2, 70, 6)   -- 1.2 m off the pivot
    check("c3: a pivot more than 1 m off finds nothing", far.bLocked == true and logCount("no-match") == 1)
    check("c3: a pivot that nothing sits at, names a hash nobody has: nothing is touched", #askEvts() == 0)

    -- c4: no claim
    local c4 = mkStash(lvl("c4"), 44, 54, 6, true)
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c4")), 0, 44, 54, 6)
    check("c4: no unlock claim -> refused, the locked chest stays locked", c4.bLocked == true and c4.unlocks == nil
        and logCount("WO195B-CHEST ask from 1 for " .. K(lvl("c4")) .. " refused: no unlock claim") == 1, lastLog("WO195B-CHEST"))

    -- c5: a door and a chest at one pivot never answer for each other
    local d5 = mkDoor("c_door5", 200, 0, 0); d5.bLocked = true
    local c5 = mkStash(lvl("c5"), 200, 0, 0.3, true)
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c5")), 1, 200, 0, 0.3)
    check("c5: a chest ask at a pivot shared with a door unlocks the chest only", c5.bLocked == false and d5.bLocked == true)
    local c5b = mkStash(lvl("c5b"), 210, 0, 0, true)
    clearLog()
    KCD2MP_W151DoorAsked(1, K(dlvl("c_nodoor")), 1, 1, 210, 0, 0)
    check("c5: a door ask at a pivot where only a chest sits unlocks nothing (the door path looks for AnimDoor)", c5b.bLocked == true
        and logCount("no such door here") == 1)
    local d5b = mkDoor("c_door5b", 220, 0, 0); d5b.bLocked = true
    local c5c = mkStash(lvl("c5c"), 220, 0, 0.3, true)
    clearLog()
    KCD2MP_W151DoorAsked(1, K(dlvl("c_door5b")), 1, 1, 220, 0, 0)
    check("c5: a door ask (flags 1) at a pivot shared with a chest unlocks the door only", d5b.bLocked == false and c5c.bLocked == true)
    local d5c = mkDoor("c_door5c", 230, 0, 0); d5c.bLocked = true
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c_nochest")), 1, 230, 0, 0)
    check("c5: a chest ask at a pivot where only a door sits unlocks nothing", d5c.bLocked == true and logCount("no-match") == 1)

    -- c6: the host's own Henry is at it
    local c6 = mkStash(lvl("c6"), 80, 80, 6, true)
    c6:Lockpick(player)                 -- his own pick began a moment ago (the pick-start wrap marks it)
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c6")), 1, 80, 80, 6)
    check("c6: the host's own pick of that chest just began -> deferred, not unlocked", c6.bLocked == true and #TIMERS == 1
        and logCount("deferred: the host's own Henry is at it (try 1)") == 1, lastLog("WO195B-CHEST"))
    NOW = NOW + 20; tickHost(); runTimers()
    check("c6: ...and applied once he is done (the retry)", c6.bLocked == false and c6.unlocks == 1 and logCount("for " .. K(lvl("c6")) .. " unlocked") == 1, lastLog("WO195B-CHEST"))
    local c6b = mkStash(lvl("c6b"), 81, 81, 6, true); c6b.bOpened = 1   -- held open, never settles
    clearLog(); TIMERS = {}
    KCD2MP_W195BChestAsked(1, K(lvl("c6b")), 1, 81, 81, 6)
    for i = 1, 10 do NOW = NOW + 2; tickHost(); runTimers() end
    check("c6: a chest he keeps open is deferred 8 times, then refused -- never unlocked under him",
        c6b.bLocked == true and logCount("deferred: the host's own Henry is at it") == 8
        and logCount("refused: the host's own Henry was at it the whole time") == 1 and #TIMERS == 0, lastLog("WO195B-CHEST"))
    -- the host's own key use goes through the same OnUsed wrap (it marks the use, as it does on a joiner)
    local c6c = mkStash(lvl("c6c"), 82, 82, 6, true)
    c6c.hasKey = true
    clearLog()
    c6c:OnUsed(player)
    check("c6: the host's own key use unlocks his chest, sends and asks nothing", c6c.bLocked == false and #askEvts() == 0)

    -- c7: a failing Unlock
    local c7 = mkStash(lvl("c7"), 83, 83, 6, true)
    c7.Unlock = function(self) error("boom") end
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c7")), 1, 83, 83, 6)
    check("c7: an Unlock that errors is refused, logged", c7.bLocked == true and logCount("refused: Stash:Unlock failed") == 1, lastLog("WO195B-CHEST"))
    ERRS = {}   -- the harness's pcall note of the expected error
    local c7b = mkStash(lvl("c7b"), 84, 84, 6, true)
    c7b.Unlock = function(self) end     -- an Unlock that does nothing
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c7b")), 1, 84, 84, 6)
    check("c7: an Unlock that leaves it locked is refused, not called a success", c7b.bLocked == true and logCount("refused: Stash:Unlock left it locked") == 1, lastLog("WO195B-CHEST"))

    -- c8: only the host unlocks
    reset(); ERRS = {}; NOW = NOW + 30; tickJoiner()
    local c8 = mkStash(lvl("c8"), 85, 85, 6, true)
    KCD2MP_W195BChestAsked(1, K(lvl("c8")), 1, 85, 85, 6)
    check("c8: asked on a joiner (never the host) -> refused, locked", c8.bLocked == true and logCount("refused: not the host here (role=joiner)") == 1, lastLog("WO195B-CHEST"))

    -- c9: the switch
    reset(); ERRS = {}; NOW = NOW + 30; tickHost()
    local c9 = mkStash(lvl("c9"), 86, 86, 6, true)
    KCD2MP_W195BSetChestSync("off")
    clearLog()
    KCD2MP_W195BChestAsked(1, K(lvl("c9")), 1, 86, 86, 6)
    check("c9: mp_chest_sync off -> inert (refused, nothing unlocked, nothing sent)", c9.bLocked == true and c9.unlocks == nil
        and logCount("refused: mp_chest_sync is off") == 1 and #askEvts() == 0, lastLog("WO195B-CHEST"))
    local d9 = mkDoor("c_door9", 87, 0, 0); d9.bLocked = true
    KCD2MP_W151DoorAsked(1, K(dlvl("c_door9")), 1, 1, 87, 0, 0)
    check("c9: ...and the door path is not affected by the chest switch (still unlocks)", d9.bLocked == false)
    KCD2MP_W195BSetChestSync("on")
    KCD2MP_W195BChestAsked(1, K(lvl("c9")), 1, 86, 86, 6)
    check("c9: back on -> the same ask unlocks", c9.bLocked == false)

    -- c10: the switch is independent of the door switch
    tickHost()
    local c10 = mkStash(lvl("c10"), 88, 88, 6, true)
    W.doorSync = false
    KCD2MP_W195BChestAsked(1, K(lvl("c10")), 1, 88, 88, 6)
    W.doorSync = true
    check("c10: mp_door_sync off does not stop the chests (their own switch)", c10.bLocked == false)

    -- c11: a pick of the host's world's own chest on the host: the wraps stay out of it
    tickHost()
    local c11 = mkStash(lvl("c11"), 89, 89, 6, true)
    clearLog()
    c11:OnLockpicked()
    check("c11: the host's own pick unlocks his chest and sends nothing", c11.bLocked == false and #askEvts() == 0 and c11.w195bAskAt == nil)
    check("c11: no Lua errors", #ERRS == 0, ERRS[1])
end

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
