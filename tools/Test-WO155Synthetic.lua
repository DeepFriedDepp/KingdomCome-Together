-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-155 synthetic test (the game-side half), against the real kdcmp.lua under MoonSharp.
--
--   (F) a friendly-fire knockdown: the engine's own Actor.Fall, the writer let go; the figure is handed back only once the
--       engine says it is up and idle (not while the get-up blend runs: the T-pose), never with Revive or a ragdoll;
--       a fall that is never seen is not waited for; one that never settles is replaced by a fresh figure
--   (C) a death: the figure collapses (RagDollize, never Revive), is held on the ground (a body under the terrain is put
--       back on it, one lying on it is never touched), and the respawn hides it and replaces it by a fresh figure
--   (S) the switches: mp_avatar_falls off, mp_hit_knockdown, mp_ff_knockdown
--
-- Driven by Test-WO155Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-155-findings.md.
--
-- Part 1: engine stubs + a fake clock.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}; REMOVED = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntity = function(id) for _, e in pairs(ENTS) do if e.id == id then return e end end return nil end
System.RemoveEntity = function(id) for n, e in pairs(ENTS) do if e.id == id then ENTS[n] = nil; REMOVED[#REMOVED + 1] = n end end end
System.GetEntitiesInSphere = function() return {} end
TERRAIN = nil
System.GetTerrainElevation = function(p) return TERRAIN end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
Game = mkstub()

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
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end

local NEXTID = 9000
local function mkAvatar(id, x, y, z)
    NEXTID = NEXTID + 1
    local name = "kcd2mp_" .. tostring(id)
    local e = { class = "NPC", id = NEXTID, px = x or 0, py = y or 0, pz = z or 0, rz = 0, hidden = false,
                ragdolls = 0, falls = 0, revives = 0, standups = 0, prof = "alive", anim = "MotionIdle", sets = 0 }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.GetWorldAngles = function(self) return { x = 0, y = 0, z = self.rz } end
    e.SetWorldPos = function(self, p) self.px, self.py, self.pz = p.x, p.y, p.z; self.sets = self.sets + 1 end
    e.IsHidden = function(self) return self.hidden end
    e.Hide = function(self, v) self.hidden = (v == 1) end
    e.actor = {
        IsDead = function() return false end,
        IsUnconscious = function() return false end,
        GetHealth = function() return 100 end,
        RagDollize = function(self) e.ragdolls = e.ragdolls + 1; e.prof = "ragdoll" end,
        Fall = function(self, pos) e.falls = e.falls + 1; e.fellAt = { x = pos.x, y = pos.y, z = pos.z }; e.prof = "sleep" end,
        Revive = function(self, full) e.revives = e.revives + 1 end,
        StandUp = function(self) e.standups = e.standups + 1 end,
        GetPhysicalizationProfile = function() return e.prof end,
        GetCurrentAnimationState = function() return e.anim end,
    }
    ENTS[name] = e
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    KCD2MP.ghosts[tostring(id)] = { entity = e, entityId = e.id, spawnName = name, istate = { cx = e.px, cy = e.py, cz = e.pz } }
    return e, KCD2MP.ghosts[tostring(id)]
end
-- run every timer that is due; each tick of a watch re-arms itself, so one pass is one tick
local function tick(dt)
    NOW = NOW + dt
    local t = TIMERS; TIMERS = {}
    for _, x in ipairs(t) do x.f() end
end
local function reset()
    ERRS = {}; REMOVED = {}; TIMERS = {}
    local W = KCD2MP.w154
    W.down = {}; W.fall = {}; W.dead = {}; W.falls = true
    for id in pairs(KCD2MP.ghosts or {}) do KCD2MP.ghosts[id] = nil end
    ENTS = {}
end

-- (F1) a friendly-fire fall: the engine's own Fall; handed back only when up AND idle (+0.8 s), never a ragdoll or a Revive ---
do
    reset()
    local e, g = mkAvatar(1, 10, 20, 30)
    g.istate.nativeSent = true; g.istate.nativeOwned = true
    local mark = #LOG
    KCD2MP_W155AvatarFall("1")
    check("F1: the writer lets go first", g.istate.nativeOwned == false and countEvt("npc_native", "kcd2mp_1 off", mark) == 1)
    check("F1: the engine's own Actor.Fall, where it stands", e.falls == 1 and e.fellAt.x == 10 and e.fellAt.y == 20 and e.fellAt.z == 30, e.falls)
    check("F1: no ragdoll, no Revive", e.ragdolls == 0 and e.revives == 0)
    check("F1: WO155-FALL logged", lastLog("WO155-FALL avatar=kcd2mp_1 ok=true", mark) ~= nil)
    check("F1: while it falls it is a body for the ghost tick", mp_ghost_is_corpse("1", g) == true and KCD2MP_W154IsDown("1"))
    tick(0.4); tick(0.4); tick(0.4)                                   -- 'sleep': down
    check("F1: still down at 1.2 s", KCD2MP_W154IsDown("1"))
    e.prof = "alive"; e.anim = "MotionIdle"                           -- the body woke, the blend has not started: reads idle (live PF1/PF2)
    tick(0.4); tick(0.4); tick(0.4); tick(0.4)
    check("F1: 'alive' + idle BEFORE the get-up blend is not up: the writer does not take it (the T-pose of PF1/PF2)", KCD2MP_W154IsDown("1"))
    e.prof = "alive"; e.anim = "BlendRagdoll"                         -- the get-up blend: the T-pose window
    tick(0.4); tick(0.4); tick(0.4); tick(0.4)
    check("F1: NOT handed back while the get-up blend runs (the T-pose of the field)", KCD2MP_W154IsDown("1"))
    e.anim = "MotionIdle"                                             -- up and idle
    tick(0.4)
    check("F1: idle for 0.4 s: not yet", KCD2MP_W154IsDown("1"))
    tick(0.4); tick(0.4)
    check("F1: idle for 1.2 s minus a tick: not yet", KCD2MP_W154IsDown("1"))
    tick(0.4); tick(0.4)
    check("F1: idle for 1.2 s after the blend -> the fallen body is let go (the writer takes it back)", not KCD2MP_W154IsDown("1") and mp_ghost_is_corpse("1", g) == false)
    check("F1: WO155-RISE says the game's own animation got it up", (lastLog("WO155-RISE avatar=kcd2mp_1", mark) or ""):find("got up by the game's own animation", 1, true) ~= nil,
        lastLog("WO155-RISE", mark))
    check("F1: ... the same figure, never replaced, never Revive'd or ragdolled", #REMOVED == 0 and KCD2MP.ghosts["1"] ~= nil and e.revives == 0 and e.ragdolls == 0)
    local before = e.falls
    KCD2MP_W155AvatarFall("1"); tick(0.4)
    check("F1: a second fall afterwards works again", e.falls == before + 1)
    noErrs("F1")
end

-- (F2) a fall the engine never shows: not waited for ------------------------------------------------------------------------
do
    reset()
    local e, g = mkAvatar(2, 0, 0, 0)
    e.actor.Fall = function(self, pos) e.falls = e.falls + 1 end     -- an Actor.Fall that changed nothing
    local mark = #LOG
    KCD2MP_W155AvatarFall("2")
    for i = 1, 12 do tick(0.4) end
    check("F2: no 'sleep' or blend seen in 4 s -> released, nothing to wait for", not KCD2MP_W154IsDown("2") and #REMOVED == 0, tostring(KCD2MP_W154IsDown("2")))
    check("F2: ... and says so", (lastLog("WO155-RISE avatar=kcd2mp_2", mark) or ""):find("no fall", 1, true) ~= nil, lastLog("WO155-RISE", mark))
    noErrs("F2")
end

-- (F3) a fall that never settles: a fresh figure replaces it (no frozen pose) ---------------------------------------------------
do
    reset()
    local e, g = mkAvatar(3, 0, 0, 0)
    local mark = #LOG
    KCD2MP_W155AvatarFall("3")
    e.prof = "alive"; e.anim = "BlendRagdoll"                         -- stuck in the blend
    for i = 1, 60 do tick(0.4) end
    check("F3: never settled in 20 s -> the figure is replaced", not KCD2MP_W154IsDown("3") and REMOVED[1] == "kcd2mp_3", tostring(REMOVED[1]))
    check("F3: ... logged with what the engine said", (lastLog("WO155-RISE avatar=kcd2mp_3", mark) or ""):find("never settled (profile=alive anim=BlendRagdoll)", 1, true) ~= nil)
    noErrs("F3")
end

-- (F6) a body that wakes but never shows the blend: not handed back on a guess -- replaced at the give-up time -----------------------
do
    reset()
    local e, g = mkAvatar(7, 0, 0, 0)
    local mark = #LOG
    KCD2MP_W155AvatarFall("7")
    tick(0.4); tick(0.4)
    e.prof = "alive"; e.anim = "MotionIdle"
    for i = 1, 40 do tick(0.4) end
    check("F6: 'alive' + idle with no blend for 16 s: still not handed back", KCD2MP_W154IsDown("7"))
    for i = 1, 20 do tick(0.4) end
    check("F6: at the give-up time a fresh figure replaces it", not KCD2MP_W154IsDown("7") and REMOVED[1] == "kcd2mp_7", tostring(REMOVED[1]))
    noErrs("F6")
end

-- (F4) the guards: hidden, dead, no body, fall off, already falling -------------------------------------------------------------
do
    reset()
    local e, g = mkAvatar(4, 0, 0, 0)
    e.hidden = true
    KCD2MP_W155AvatarFall("4")
    check("F4: a hidden figure does not fall", e.falls == 0)
    e.hidden = false
    KCD2MP_W155AvatarFall("4"); KCD2MP_W155AvatarFall("4")
    check("F4: a second call while it falls is a no-op", e.falls == 1, e.falls)
    KCD2MP_W155AvatarFall("9")
    check("F4: no body -> nothing", lastLog("WO155-FALL avatar=kcd2mp_9 no body") ~= nil)
    KCD2MP.w154.falls = false
    local e5 = mkAvatar(5, 0, 0, 0)
    KCD2MP_W155AvatarFall("5")
    check("F4: mp_avatar_falls off -> no fall", e5.falls == 0)
    KCD2MP.w154.falls = true
    e.actor.Fall = nil
    KCD2MP.w154.fall = {}; KCD2MP.w154.down = {}
    KCD2MP_W155AvatarFall("4")
    check("F4: a build without Actor.Fall says so and does not fall", lastLog("WO155-FALL avatar=kcd2mp_4 Actor.Fall is not registered") ~= nil and not KCD2MP_W154IsDown("4"))
    noErrs("F4")
end

-- (F5) the backstop: a watch whose timer chain died (a load) never leaves a figure frozen -------------------------------------
do
    reset()
    local e, g = mkAvatar(6, 0, 0, 0)
    KCD2MP_W155AvatarFall("6")
    TIMERS = {}                                                       -- the chain dies
    NOW = NOW + 10
    check("F5: still a body inside the give-up time", KCD2MP_W154IsDown("6"))
    NOW = NOW + 20
    check("F5: past it (+5 s) the writer has the figure back", KCD2MP_W154IsDown("6") == false and (lastLog("WO155-RISE avatar=kcd2mp_6 the watch is stale") ~= nil))
    noErrs("F5")
end

-- (C1) a death: the figure collapses and lies; a body under the terrain is put back, a settling one is not ------------------------------
do
    reset()
    TERRAIN = 49.7                                                    -- the terrain's height under the body
    local e, g = mkAvatar(1, 100, 200, 50)
    g.istate.nativeSent = true; g.istate.nativeOwned = true
    local mark = #LOG
    KCD2MP_W155AvatarCollapse("1", true)
    check("C1: the writer lets go first", g.istate.nativeOwned == false)
    check("C1: the engine's ragdoll, once; no Fall, no Revive, no StandUp", e.ragdolls == 1 and e.falls == 0 and e.revives == 0 and e.standups == 0)
    check("C1: WO155-COLLAPSE logged", lastLog("WO155-COLLAPSE avatar=kcd2mp_1 ok=true", mark) ~= nil)
    check("C1: it is a body", mp_ghost_is_corpse("1", g) == true)
    check("C1: not hidden: it lies where it fell (a death is no longer a vanishing)", e.hidden == false)
    -- a ragdoll's pivot wanders while it settles (49.8 -> 51.2 -> 49.5): none of it is a sink, whatever the pivot did
    e.pz = 49.8; tick(1.0); e.pz = 51.2; tick(1.0); e.pz = 49.5; tick(1.0); e.pz = 50.4; tick(1.0); e.pz = 49.4; tick(1.0)
    check("C1: a body that wanders but lies on the terrain is never touched (the hover of PD3)", e.sets == 0, e.sets)
    e.pz = 49.45; tick(1.0)
    check("C1: 0.25 m under the terrain's surface is a body lying in a hollow (live: a resting pivot 0.2 m under it), not a sink", e.sets == 0)
    e.pz = 48.9; mark = #LOG; tick(1.0)
    check("C1: a body 0.8 m under the terrain is put back on it", e.sets == 1 and math.abs(e.pz - 49.5) < 1e-6 and e.px == 100 and e.py == 200, string.format("%s %s", e.sets, e.pz))
    check("C1: ... and says so", lastLog("WO155-HOLD avatar=kcd2mp_1 sank", mark) ~= nil)
    check("C1: still no Revive after all of it", e.revives == 0 and e.standups == 0)
    TERRAIN = nil
    e.pz = 10; tick(1.0)
    check("C1: no terrain reading (indoors, no answer) -> nothing is moved", e.sets == 1)
    KCD2MP_W155AvatarCollapse("1", true)
    check("C1: collapsing twice ragdolls once", e.ragdolls == 1)
    noErrs("C1")
end

-- (C2) the respawn: hidden at once, a fresh figure 1.5 s later; never a Revive ------------------------------------------------
do
    reset()
    local e, g = mkAvatar(1, 0, 0, 0)
    KCD2MP_W155AvatarCollapse("1", true)
    KCD2MP_W155AvatarReplace("1")
    check("C2: Replace before the respawn does nothing (the body keeps lying)", #REMOVED == 0 and KCD2MP.w154.dead["1"] ~= nil)
    local mark = #LOG
    KCD2MP_W155AvatarCollapse("1", false)
    check("C2: the respawn hides the lying figure at once", e.hidden == true and e.revives == 0 and e.standups == 0)
    check("C2: ... and logs it", lastLog("WO155-COLLAPSE avatar=kcd2mp_1 respawned", mark) ~= nil)
    tick(1.0)                                                        -- the hold's next tick: stops once released
    check("C2: the hold stops at the respawn", e.sets == 0)
    KCD2MP_W155AvatarReplace("1")
    check("C2: 1.5 s later the body is removed (the next position update spawns a fresh figure)", REMOVED[1] == "kcd2mp_1" and KCD2MP.ghosts["1"] == nil)
    check("C2: ... the dead mark and the corpse state are gone", KCD2MP.w154.dead["1"] == nil and not KCD2MP_W154IsDown("1"))
    check("C2: ... logged", lastLog("WO155-REPLACE avatar=kcd2mp_1") ~= nil)
    check("C2: the engine's Revive never ran on the figure", e.revives == 0)
    noErrs("C2")
end

-- (C3) the guards: mp_avatar_falls off, a hidden body, no body, a fall in progress when he dies -----------------------------------
do
    reset()
    KCD2MP.w154.falls = false
    local e, g = mkAvatar(1, 0, 0, 0)
    KCD2MP_W155AvatarCollapse("1", true)
    check("C3: mp_avatar_falls off -> nothing collapses (the host hides it as before)", e.ragdolls == 0 and KCD2MP.w154.dead["1"] == nil)
    KCD2MP.w154.falls = true
    e.hidden = true
    KCD2MP_W155AvatarCollapse("1", true)
    check("C3: a hidden figure: marked dead, nothing laid down", e.ragdolls == 0 and KCD2MP.w154.dead["1"] ~= nil and lastLog("WO155-COLLAPSE avatar=kcd2mp_1 is hidden") ~= nil)
    reset()
    local e2 = mkAvatar(2, 0, 0, 0)
    KCD2MP_W155AvatarFall("2")
    KCD2MP_W155AvatarCollapse("2", true)
    check("C3: a death during a fall takes over (the fall watch ends, the ragdoll lies)", KCD2MP.w154.fall["2"] == nil and e2.ragdolls == 1 and KCD2MP.w154.dead["2"] ~= nil)
    tick(0.4)
    check("C3: ... and no later fall tick lets the writer take the lying body back", KCD2MP_W154IsDown("2"))
    KCD2MP_W155AvatarCollapse("8", true)
    check("C3: no body -> marked dead, no error", lastLog("WO155-COLLAPSE avatar=kcd2mp_8 no body here") ~= nil)
    noErrs("C3")
end

-- (S) the switches ------------------------------------------------------------------------------------------------------------
do
    reset()
    local e, g = mkAvatar(1, 0, 0, 0)
    KCD2MP_W155AvatarFall("1")
    local e2 = mkAvatar(2, 0, 0, 0)
    KCD2MP_W155AvatarCollapse("2", true)
    local mark = #LOG
    KCD2MP_W154SetFalls("off")
    check("S1: mp_avatar_falls off: the falling figure is replaced, never Revive'd", not KCD2MP_W154IsDown("1") and REMOVED[1] == "kcd2mp_1" and e.revives == 0)
    check("S1: ... the dead figure too", KCD2MP.w154.dead["2"] == nil and REMOVED[2] == "kcd2mp_2")
    check("S1: ... the agent is told", countEvt("w154_falls", "off", mark) == 1)
    KCD2MP_W154SetFalls("on")
    check("S2: mp_avatar_falls / mp_hit_knockdown / mp_ff_knockdown are console commands taking %line",
        CCMDS["mp_avatar_falls"] and CCMDS["mp_hit_knockdown"] and CCMDS["mp_ff_knockdown"]
        and CCMDS["mp_hit_knockdown"].body:find("%line", 1, true) ~= nil and CCMDS["mp_ff_knockdown"].body:find("%line", 1, true) ~= nil)
    mark = #LOG
    KCD2MP_W155SetHitKnockdown("on")
    check("S2: mp_hit_knockdown on tells the agent", countEvt("w155_cfg", "hit_knockdown=on", mark) == 1)
    KCD2MP_W155SetHitKnockdown("off")
    check("S2: ... and off", countEvt("w155_cfg", "hit_knockdown=off", mark) == 1)
    KCD2MP_W155SetFfKnockdown("off")
    check("S2: mp_ff_knockdown off tells the agent", countEvt("w155_cfg", "ff_knockdown=off", mark) == 1)
    KCD2MP_W155SetFfKnockdown("on")
    mark = #LOG
    check("S2: a bad value is refused, nothing sent", KCD2MP_W155SetHitKnockdown("maybe") == false and KCD2MP_W155SetFfKnockdown("2") == false and countEvt("w155_cfg", "", mark) == 0)
    check("S2: no value: reports, sends nothing", KCD2MP_W155SetHitKnockdown("") == true and countEvt("w155_cfg", "", mark) == 0 and lastLog("WO155-TOGGLE hit_knockdown=off") ~= nil)
    check("S2: defaults: an NPC's blow knocks nobody down, friendly fire does", KCD2MP.w154.hitKnockdown == false and KCD2MP.w154.ffKnockdown == true)
    noErrs("S")
end

-- (R) removing a ghost clears every WO-155 mark (the next occupant of the id starts standing) --------------------------------------
do
    reset()
    local e = mkAvatar(3, 0, 0, 0)
    KCD2MP_W155AvatarCollapse("3", true)
    KCD2MP_RemoveGhost("3")
    check("R1: RemoveGhost clears the corpse marks", KCD2MP.w154.dead["3"] == nil and KCD2MP.w154.down["3"] == nil and KCD2MP.w154.fall["3"] == nil)
    noErrs("R1")
end

OUT = table.concat(RESULTS, "\n")
