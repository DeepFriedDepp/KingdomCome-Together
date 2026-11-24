-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-223 synthetic test, against the real kdcmp.lua under MoonSharp (harness from Test-WO195BChestSynthetic.lua).
-- B, the host's half in the mod: which hostile NPCs are named to attend to a joiner's figure, and the engine's detail radius.
--   (a) defaults: every switch on, registered; nothing errors at load
--   (b) the radius wanted for a figure far from the host (pure): the engine's own when near or beyond the cap, 30 m steps between
--   (b2) the engine's rule for a player (pure): the radius by his visibility and skill, the seconds of clear view, the meter
--   (c) the gate: a hostile NPC is named when it has had the clear view of him the engine would need for a player with the joiner's
--       own values; a villager, an NPC out of his reach, one that looks away, one behind a wall, a dead one are not; an NPC already
--       fighting is; crouched in the dark his reach is short; the names are kept until it has lost him and keepS more; his steps are
--       made where a hostile NPC is in earshot; this near a bandit he is noticed anyway
--   (d) the radius cvars: raised for a far figure, put back when he is near, and when the session ends
--   (e) the switch off, or not the host: nobody named, the engine's own radius
--   (f) F: one notice line per enemy that turned alert, saying whom it most likely noticed
-- What this proves: the Lua half's decisions against stand-ins of the game's entities. The engine's part (the attention pairs, what
-- an NPC then does) is the bench's.

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

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function lastLog(pat)
    for i = #LOG, 1, -1 do if string.find(LOG[i], pat, 1, true) then return LOG[i] end end
    return nil
end
local function logCount(pat)
    local n = 0
    for _, l in ipairs(LOG) do if string.find(l, pat, 1, true) then n = n + 1 end end
    return n
end
local function clearLog() LOG = {} end
local function attends()
    local out = {}
    for _, l in ipairs(LOG) do
        local a = l:match("%[KCD2%-MP%-EVT%] v1 %d+ w223_attend (.*)$")
        if a then out[#out + 1] = a end
    end
    return out
end

-- (a) defaults
do
    check("a: every switch is on by default", KCD2MP_W223On("w223_perceive") and KCD2MP_W223On("w223_copy_target") and KCD2MP_W223On("w223_combat_mode")
        and KCD2MP_W223On("w223_guard") and KCD2MP_W223On("w223_notice_log") and KCD2MP_W223On("w223_split"))
    check("a: mp_w223_status and the six switches are registered", CCMDS["mp_w223_status"] ~= nil and CCMDS["mp_w223_perceive"] ~= nil
        and CCMDS["mp_w223_copy_target"] ~= nil and CCMDS["mp_w223_combat_mode"] ~= nil and CCMDS["mp_w223_guard"] ~= nil and CCMDS["mp_w223_notice_log"] ~= nil
        and CCMDS["mp_w223_split"] ~= nil)
    check("a: no Lua errors at load", #ERRS == 0, ERRS[1])
end

-- (b) the radius wanted (pure)
do
    local P = W223.P
    check("b: a figure near the host needs nothing (the engine's own)", KCD2MP_W223LodWant(0, 150, P) == 150 and KCD2MP_W223LodWant(100, 150, P) == 150)
    check("b: 130 m away -> 210 m (his distance + 60, in 30 m steps)", KCD2MP_W223LodWant(130, 150, P) == 210, KCD2MP_W223LodWant(130, 150, P))
    check("b: 300 m away -> 360 m", KCD2MP_W223LodWant(300, 150, P) == 360, KCD2MP_W223LodWant(300, 150, P))
    check("b: beyond the cap nothing is gained: the engine's own", KCD2MP_W223LodWant(395, 150, P) == 150 and KCD2MP_W223LodWant(900, 150, P) == 150)
    check("b: never under the engine's own", KCD2MP_W223LodWant(101, 400, P) == 400)
end

-- (b2) the engine's rule for a player (pure): the radius, the seconds of clear view, the meter
do
    local E = W223.E
    local function near(a, b, eps) return math.abs(a - b) <= (eps or 0.001) end
    check("b2: the built-in numbers are this build's", E.minR == 2.5 and E.minVis == -2.0 and E.tP == 4.0 and E.gain == 0.1 and E.src == "built-in")
    check("b2: the curve is 0 at his feet, a half in the middle, 1 at the edge", near(KCD2MP_W223Curve(0, 0.1), 0) and near(KCD2MP_W223Curve(0.5, 0.1), 0.5) and near(KCD2MP_W223Curve(1, 0.1), 1))
    check("b2: and steep between (0.05 at a quarter, 0.95 at three quarters)", near(KCD2MP_W223Curve(0.25, 0.1), 0.05) and near(KCD2MP_W223Curve(0.75, 0.1), 0.95),
        KCD2MP_W223Curve(0.25, 0.1) .. "/" .. KCD2MP_W223Curve(0.75, 0.1))
    check("b2: by day he is seen at the NPC's whole view radius", near(KCD2MP_W223ViewR(20, 1.69, 0, E), 20))
    check("b2: visibility -1 halves it; -3 leaves the minimum radius", near(KCD2MP_W223ViewR(20, -1.0, 0, E), 10) and near(KCD2MP_W223ViewR(20, -3.0, 0, E), 2.5))
    check("b2: the full stealth skill takes 30 % off", near(KCD2MP_W223ViewR(20, 0.5, 1.0, E), 14))
    check("b2: at the edge an inconspicuous player needs 4 s of clear view", near(KCD2MP_W223RecogT(20, 20, 0, 0, E), 4))
    check("b2: conspicuous 0.5: 3 s; -0.5: 6 s; full skill: 6.4 s", near(KCD2MP_W223RecogT(20, 20, 0.5, 0, E), 3) and near(KCD2MP_W223RecogT(20, 20, -0.5, 0, E), 6)
        and near(KCD2MP_W223RecogT(20, 20, 0, 1, E), 6.4))
    check("b2: inside the minimum radius, or very conspicuous: at once", KCD2MP_W223RecogT(2.0, 20, 0.1, 0, E) == 0 and KCD2MP_W223RecogT(20, 20, 3.0, 0, E) == 0)
    check("b2: a visibility above 0 shortens it: half the ceiling, half the time; at the ceiling, at once; below 0, nothing changes",
        near(KCD2MP_W223RecogT(20, 20, 0, 0, E, 1.75), 2) and KCD2MP_W223RecogT(20, 20, 0, 0, E, 3.5) == 0 and near(KCD2MP_W223RecogT(20, 20, 0, 0, E, -1.0), 4))
    check("b2: the meter rises by dt / seconds in view", near(KCD2MP_W223Meter(0, 0.5, true, 20, 20, 0, 0, E), 0.125))
    check("b2: and falls by half a second's worth out of view, never under 0", near(KCD2MP_W223Meter(1, 0.5, false, 20, 20, 0, 0, E), 0.75) and KCD2MP_W223Meter(0.1, 0.5, false, 20, 20, 0, 0, E) == 0)
    check("b2: at once inside the minimum radius whatever the step", KCD2MP_W223Meter(0, 0, true, 2.0, 20, 0, 0, E) == 1)
    check("b2: the DLL's numbers are taken whole or not at all", KCD2MP_W223Engine("minR=2.500,minVis=-2.000,tP=4.000") == false and W223.E.src == "built-in")
    check("b2: a text that is not numbers is refused", KCD2MP_W223Engine("minR=x") == false)
    check("b2: the engine's own numbers replace the built-in ones", KCD2MP_W223Engine("minR=2.500,minVis=-2.000,tP=4.000,tConPos=-2.000,tConNeg=-4.000,gain=0.1000,decay=-0.500,npcMod=0.000,skT=0.600,skR=0.300") == true
        and W223.E.src == "engine" and W223.E.tP == 4.0 and W223.E.decay == -0.5)
    check("b2: and the log says so", (lastLog("WO223-ENGINE the gate uses the engine's own numbers") or "") ~= "")
end

-- the stand-ins
local BLOCKED = false   -- a wall between every NPC and the figure
Physics = Physics or {}
Physics.RayWorldIntersection = function() if BLOCKED then return { { dist = 1.0 } } end return {} end
local CVARS = { WH_AI_LOD_MaxDetailDistance = "150", WH_AI_LOD_MaxCountDetail = "70" }
System.GetCVar = function(n) return CVARS[n] or "0" end
System.SetCVar = function(n, v) CVARS[n] = tostring(v) end
local SOUNDS = {}
local HOLDS = {}   -- every "do not perceive" the gate asked: npc -> count
XGenAIModule.GetMyWUID = function(e) return e.this and e.this.id end
local HOLD_T = {}  -- npc -> the length of its last hold
local HELD_UNTIL = {}   -- npc -> the end of the hold the engine took
local DROPS = {}        -- npc -> holds asked for while one ran (the engine drops them)
local BAD_T = {}        -- lengths not written as a whole number of ms
local GAPS = {}         -- npc -> seconds a watched NPC was not held (see fineBeats)
local WATCH = {}        -- the NPCs whose un-held time is summed
XGenAIModule.IgnorePerception = function(npc, target, timeout, world, hard)
    local ms = tonumber(string.match(tostring(timeout), "^(%d+)ms$"))
    HOLDS.last = { npc = npc, target = target, timeout = timeout, world = world, hard = hard, ms = ms }
    if not ms then BAD_T[#BAD_T + 1] = tostring(timeout); return end
    if NOW < (HELD_UNTIL[npc] or -1) - 0.0001 then DROPS[npc] = (DROPS[npc] or 0) + 1; return end
    HOLDS[npc] = (HOLDS[npc] or 0) + 1
    HOLD_T[npc] = ms
    HELD_UNTIL[npc] = NOW + ms / 1000
end
local function blind(npc) return NOW < (HELD_UNTIL[npc] or -1) - 0.0001 end
-- n beats of half a second: time runs in 0.05 s steps with the fast hold tick at each (the ghost interpolation timer's place), the
-- gate's own beat at every tenth; a watched NPC's un-held time is summed
local function fineBeats(n)
    for _ = 1, n do
        for i = 1, 10 do
            NOW = NOW + 0.05
            if i < 10 then KCD2MP_W223HoldTick() else KCD2MP_W223Tick() end
            for _, npc in ipairs(WATCH) do
                if not blind(npc) then GAPS[npc] = (GAPS[npc] or 0) + 0.05 end
            end
        end
    end
end
XGenAIModule.ProduceSoundWUID = function(kind, w, mult) SOUNDS[#SOUNDS + 1] = { kind = kind, w = w, mult = mult } end
local PRIVATE = false   -- the figure stands on private ground of the host's world
XGenAIModule.IsPointInAreaWithLabel = function(p, l) return PRIVATE == true and l == "private" end
System.IsPointIndoors = function() return false end
WORLD_T = 12 * 3600   -- noon

local NEXTID = 0x0E0000
local function mkNpc(name, x, y, facing, rel, cls)
    NEXTID = NEXTID + 1
    local e = { class = cls or "NPC", id = "userdata: " .. string.format("%016X", NEXTID), px = x, py = y, pz = 0, face = facing or { x = 1, y = 0 },
                rel = rel, vir = 20.0, fov = 100, combat = false, danger = false, dead = false, asleep = false }
    e.this = { id = "this:" .. name }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self, t) t = t or {}; t.x, t.y, t.z = self.px, self.py, self.pz; return t end
    e.GetDirectionVector = function(self) return { x = self.face.x, y = self.face.y, z = 0 } end
    -- to the host's own player: an enemy unless a test says otherwise (relP: 0 = a camp's NPC, above 0 = a friend)
    e.soul = { GetRelationship = function(s, w) if w == "this:player" then return e.relP or -1 end return e.rel end,
               GetDerivedStat = function(s, n) if n == "vir" then return e.vir elseif n == "fov" then return e.fov end return 0 end,
               IsInCombatMode = function() return e.combat end, IsInCombatDanger = function() return e.danger end,
               HasScriptContext = function() return false end }
    e.actor = { IsDead = function() return e.dead end }
    e.human = { IsSleeping = function() return e.asleep end, IsWeaponDrawn = function() return e.armed == true end }
    ENTS[name] = e
    return e
end
local function sphere(...) SPHERE = { ... } end

-- the host of a shared world, one joiner's figure 200 m east of the host's player
KCD2MP.hitSensorOn = true
KCD2MP.w122 = KCD2MP.w122 or {}
KCD2MP.w122.sharedWorld = true
player.px, player.py, player.pz = 0, 0, 0
player.soul = { GetDerivedStat = function() return 0.43 end }
player.this = { id = "this:player" }
local fig = mkNpc("kcd2mp_1", 200, 0, { x = 1, y = 0 }, 1.0)
KCD2MP.ghosts = { [1] = { entity = fig, istate = { stance = "s", smoothedSpeed = 3.0 } } }
local DAY_WALK = "c=0,l=0,v=0.00,n=0.72,s=0.60,k=5"      -- walking in the shade (visibility 0: the plain time)
local DAY_SPRINT = "c=0,l=0,v=3.19,n=1.08,s=0.60,k=5"    -- sprinting in the open
local NIGHT_CROUCH = "c=1,l=0,v=-1.45,n=0.10,s=0.10,k=5"

-- (c) the gate
do
    NOW = 100
    check("c: his own stealth text is taken", KCD2MP_W223Stealth("1", DAY_WALK) == true)
    check("c: a text that is not his mod's is refused", KCD2MP_W223Stealth("1", "v=1;System.Quit()") == false)
    local bandit = mkNpc("bandit_see", 210, 0, { x = -1, y = 0 }, -1.0)      -- 10 m away, facing him
    local villager = mkNpc("villager", 205, 0, { x = -1, y = 0 }, 0.5)       -- a friend, 5 m away
    local far = mkNpc("bandit_far", 240, 0, { x = -1, y = 0 }, -1.0)         -- 40 m: beyond his reach by day
    local away = mkNpc("bandit_away", 200, 10, { x = 0, y = 1 }, -1.0)       -- 10 m, looking away from him
    local fighting = mkNpc("bandit_fight", 230, 0, { x = 1, y = 0 }, -1.0)   -- 30 m, looking away, but in a fight
    fighting.combat = true
    local corpse = mkNpc("bandit_dead", 203, 0, { x = -1, y = 0 }, -1.0)
    corpse.dead = true
    sphere(fig, bandit, villager, far, away, fighting, corpse, player)
    clearLog()
    KCD2MP_W223Tick()
    local a = attends()
    check("c: at first sight only the one already fighting is named: a player is not recognised at once", a[#a] == "kcd2mp_1|bandit_fight", a[#a])
    -- walking by day (conspicuous 0.6, skill 5), 10 m from a bandit that faces him: about two seconds of clear view
    local function beats(n) fineBeats(n) end
    check("c: at that first beat the one that has him in view gets a short hold (its eyes are due within the look-ahead: 1.2 s at most)",
        (HOLDS["this:bandit_see"] or 0) == 1 and HOLD_T["this:bandit_see"] == 1200, tostring(HOLDS["this:bandit_see"]) .. "/" .. tostring(HOLD_T["this:bandit_see"]))
    WATCH = { "this:bandit_far", "this:bandit_away" }; GAPS = {}
    beats(2)
    check("c: an enemy that has him out of reach, or looks away, has its own sight of the figure held off three seconds at a time (the game's own call, interruptible), the length written in whole ms",
        (HOLDS["this:bandit_far"] or 0) == 1 and (HOLDS["this:bandit_away"] or 0) == 1 and HOLDS.last.target == "this:kcd2mp_1" and HOLD_T["this:bandit_far"] == 3000
        and HOLD_T["this:bandit_away"] == 3000 and HOLDS.last.world == false and HOLDS.last.hard == false and #BAD_T == 0, tostring(HOLDS["this:bandit_far"]) .. "/" .. tostring(BAD_T[1]))
    check("c: never a friend's, a dead one's, or one's that fights him",
        HOLDS["this:villager"] == nil and HOLDS["this:bandit_dead"] == nil and HOLDS["this:bandit_fight"] == nil)
    a = attends()
    check("c: after 1 s in view (a player needs 2.1 s here: inside the naming lead before the fill): the bandit that sees him is named -- nobody else",
        a[#a] == "kcd2mp_1|bandit_fight,bandit_see", a[#a])
    check("c: named, its eyes are still held: the hold runs to its planned end", blind("this:bandit_see") == true and (HOLDS["this:bandit_see"] or 0) == 1)
    beats(2)
    a = attends()
    check("c: after 2 s it stays named and its eyes are free: the second hold was only as long as what was left until then (the 2.1 s it needs less 0.6 s)",
        a[#a] == "kcd2mp_1|bandit_fight,bandit_see" and blind("this:bandit_see") == false
        and (HOLDS["this:bandit_see"] or 0) == 2 and HOLD_T["this:bandit_see"] >= 200 and HOLD_T["this:bandit_see"] <= 400, tostring(HOLDS["this:bandit_see"]) .. "/" .. tostring(HOLD_T["this:bandit_see"]))
    beats(6)
    check("c: over five seconds the two that do not see him were never un-held for a moment: each next hold is taken as the last one ends (the engine drops one asked for while one runs)",
        (GAPS["this:bandit_far"] or 0) < 0.11 and (GAPS["this:bandit_away"] or 0) < 0.11 and (HOLDS["this:bandit_far"] or 0) >= 2 and (DROPS["this:bandit_far"] or 0) >= 50,
        string.format("gaps %.2f / %.2f s, holds %s, dropped %s", GAPS["this:bandit_far"] or 0, GAPS["this:bandit_away"] or 0, tostring(HOLDS["this:bandit_far"]), tostring(DROPS["this:bandit_far"])))
    WATCH = {}
    -- the one that looked away turns to him at 10 m: named after its view of him, and the hold on it is ended
    away.face = { x = 0, y = -1 }
    beats(4)
    check("c: an enemy that turns to him is named after its own view of him",
        (lastLog("WO223-GATE figure=kcd2mp_1 npc=bandit_away named: seen") or "") ~= "", lastLog("WO223-GATE"))
    away.face = { x = 0, y = 1 }
    check("c: the gate's line says why and how far", (lastLog("WO223-GATE figure=kcd2mp_1 npc=bandit_see named: seen at 10.0 m (sight 19.0 m") or "") ~= "", lastLog("WO223-GATE figure=kcd2mp_1 npc=bandit_see"))
    -- sprinting in the open (visibility 3.19) the same bandit needs a fifth of a second: named at the first beat in view
    W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}
    KCD2MP_W223Stealth("1", DAY_SPRINT)
    NOW = 108; clearLog(); KCD2MP_W223Tick()
    a = attends()
    check("c: sprinting in the open he is no sooner known than walking (no visibility term): not at the first beat", lastLog("WO223-GATE figure=kcd2mp_1 npc=bandit_see named") == nil, a[#a])
    W223.P.visTerm = true
    W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}
    NOW = 108.5; clearLog(); KCD2MP_W223Tick()
    check("c: with the visibility term on (the option): at once", (lastLog("WO223-GATE figure=kcd2mp_1 npc=bandit_see named: seen at 10.0 m") or "") ~= "", lastLog("WO223-GATE"))
    W223.P.visTerm = false
    KCD2MP_W223Stealth("1", DAY_WALK)
    -- sprinting towards a bandit from 40 m (2.2 m a beat): the look ahead shortens the hold 2.5 s before it would recognise him, and
    -- names it half a second before -- so the engine's alert comes as he enters its reach, as it does for a player
    W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}; W223.dprev = {}
    local runner = mkNpc("bandit_run", 240, 0, { x = -1, y = 0 }, -1.0)
    sphere(fig, runner, player)
    KCD2MP_W223Stealth("1", DAY_SPRINT)
    HOLDS = {}
    NOW = 400; clearLog(); KCD2MP_W223Tick()
    local namedAt, freeAtD, fullAtD, gap = nil, nil, nil, 0
    HELD_UNTIL = {}
    for i = 1, 14 do
        for k = 1, 10 do
            NOW = NOW + 0.05; runner.px = runner.px - 0.22
            if k < 10 then KCD2MP_W223HoldTick() else KCD2MP_W223Stealth("1", DAY_SPRINT); KCD2MP_W223Tick() end
            local dnow = runner.px - 200
            if not freeAtD then
                if HELD_UNTIL["this:bandit_run"] and not blind("this:bandit_run") then
                    if (W223.plan["kcd2mp_1"]["bandit_run"] or {}).freeAt and NOW >= W223.plan["kcd2mp_1"]["bandit_run"].freeAt - 0.11 then freeAtD = dnow else gap = gap + 0.05 end
                end
            end
        end
        local dnow = runner.px - 200
        if not namedAt and lastLog("WO223-GATE figure=kcd2mp_1 npc=bandit_run named: seen") then namedAt = dnow end
        if not fullAtD and (W223.meter["kcd2mp_1"]["bandit_run"] or 0) >= 1 then fullAtD = dnow end
    end
    check("b3: a sprinter is named before the meter fills (1.75 s: the pair is set before its eyes are free), near the edge of its reach (19 m): between 15 and 21 m",
        namedAt ~= nil and namedAt >= 15 and namedAt <= 21, namedAt)
    check("b3: its eyes are free about 0.9 s (4 m of his run) before the meter is full -- the engine's alert then comes as the meter fills",
        freeAtD ~= nil and fullAtD ~= nil and freeAtD - fullAtD >= 2.0 and freeAtD - fullAtD <= 6.5, tostring(freeAtD) .. " / full at " .. tostring(fullAtD))
    check("b3: and never un-held before that", gap < 0.11, gap)
    check("b3: pure: standing still out of reach nothing fills; closing at 4.4 m/s from 21 m it does within a second -- stepped along his way in, not judged at its end (from 23 m it does not)",
        KCD2MP_W223FillWithin(1.0, 0, 23, 0, 19, true, 0.6, 0.17, W223.E, 3.19) == false and KCD2MP_W223FillWithin(1.0, 0, 21, 4.4, 19, true, 0.6, 0.17, W223.E, 3.19) == true
        and KCD2MP_W223FillWithin(1.0, 0, 23, 4.4, 19, true, 0.6, 0.17, W223.E, 3.19) == false)
    check("b3: pure: an enemy that looks away never fills; a full meter is full", KCD2MP_W223FillWithin(1.0, 0, 5, 4.4, 19, false, 0.6, 0.17, W223.E, 3.19) == false
        and KCD2MP_W223FillWithin(1.0, 1, 50, 0, 19, false, 0.6, 0.17, W223.E, 0) == true)
    runner.px = 290
    sphere(fig, bandit, villager, far, away, fighting, corpse, player)
    KCD2MP_W223Stealth("1", DAY_WALK)
    NOW = 109
    -- the line of sight (the mod's ray, on as shipped): a wall between them and nothing gathers
    W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}
    fighting.combat = false
    BLOCKED = true
    check("c: as shipped the meter has a line of sight: one ray", W223.P.ray == "one")
    NOW = 110; KCD2MP_W223Tick(); beats(12)
    a = attends()
    check("c: six seconds behind a wall: nobody named", #a == 0 or a[#a] == "kcd2mp_1|", a[#a])
    W223.P.ray = "any"
    NOW = NOW + 0.5; KCD2MP_W223Tick(); beats(6)
    a = attends()
    check("c: nor with the six-line form", #a == 0 or a[#a] == "kcd2mp_1|", a[#a])
    W223.P.ray = "none"
    W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}
    NOW = 122; KCD2MP_W223Tick(); beats(5)
    a = attends()
    check("c: with no line of sight in the meter (the option) it fills by reach and field of view", a[#a] == "kcd2mp_1|bandit_see", a[#a])
    W223.P.ray = "one"
    BLOCKED = false
    -- crouched in the dark his reach is short (5.2 m of this bandit's 20): at 10 m it never sees him, at 5 m after about four seconds
    W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}
    KCD2MP_W223Stealth("1", NIGHT_CROUCH)
    NOW = 130; clearLog(); KCD2MP_W223Tick(); beats(20)
    a = attends()
    check("c: crouched at night, 10 m from the bandit for ten seconds: not named", #a == 0 or a[#a] == "kcd2mp_1|", a[#a])
    bandit.px = 205
    W223.dprev = {}   -- (the test moves the bandit in one step: no closing speed to look ahead by)
    clearLog(); beats(4)
    a = attends()
    check("c: crouched at night, 5 m from it, two seconds: not yet", #a == 0 or a[#a] == "kcd2mp_1|", a[#a])
    beats(6)
    a = attends()
    check("c: after five seconds: named", a[#a] == "kcd2mp_1|bandit_see", a[#a])
    check("c: the line carries his own values", (lastLog("WO223-GATE figure=kcd2mp_1 npc=bandit_see named: seen at 5.0 m (sight 5.2 m") or ""):find("vib -1.45, con 0.10, skill 5, crouch 1", 1, true) ~= nil, lastLog("WO223-GATE"))
    -- inside the engine's minimum radius (2.5 m) a bandit that faces him sees him at once
    W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}
    bandit.px = 202
    NOW = 140; clearLog(); KCD2MP_W223Tick()
    a = attends()
    check("c: 2 m in front of it: named at once", a[#a] == "kcd2mp_1|bandit_see", a[#a])
    -- out of view what it gathered falls away (2 s), then the name is kept keepS and let go (an empty set is sent once)
    bandit.px = 260
    local t0 = NOW
    NOW = t0 + 1.5; KCD2MP_W223Tick()
    NOW = t0 + 1.5 + W223.P.keepS - 0.5
    clearLog(); KCD2MP_W223Tick(); a = attends()
    check("c: still named while it has not lost him", a[#a] == "kcd2mp_1|bandit_see", a[#a])
    NOW = NOW + W223.P.keepS + 1.0
    clearLog(); KCD2MP_W223Tick(); a = attends()
    check("c: let go after that: the empty set is sent", a[#a] == "kcd2mp_1|", a[#a])
    clearLog(); KCD2MP_W223Tick()
    check("c: an empty set is sent once, not every beat", #attends() == 0)
    -- his steps: a hostile NPC in earshot (0.9 m from him, looking away) hears what his own game says he makes
    NOW = 179
    KCD2MP_W223Stealth("1", DAY_WALK)
    local ear = mkNpc("bandit_ear", 200.9, 0, { x = 1, y = 0 }, -1.0)
    sphere(fig, ear, player)
    SOUNDS = {}
    NOW = 180; KCD2MP_W223Tick()
    check("c: his step is made as the engine's own movement sound, from his figure, as loud as his game says", #SOUNDS == 1 and SOUNDS[1].kind == 3
        and SOUNDS[1].w == "this:kcd2mp_1" and math.abs(SOUNDS[1].mult - 0.72) < 0.001, #SOUNDS)
    check("c: and the bandit that hears him is named", (lastLog("WO223-GATE figure=kcd2mp_1 npc=bandit_ear named: heard at 0.9 m (sight 19.0 m, noise 1.3 m, vib 0.00") or "") ~= "", lastLog("WO223-GATE"))
    NOW = 180.2; KCD2MP_W223Tick()
    check("c: at most twice a second", #SOUNDS == 1, #SOUNDS)
    -- crouched and still in the dark, 1.5 m behind a bandit: this near he is noticed whichever way it looks
    KCD2MP_W223Stealth("1", "c=1,l=0,v=-1.45,n=0.00,s=0.10,k=5")
    ear.px = 201.5
    W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}
    NOW = 190; clearLog(); KCD2MP_W223Tick()
    check("c: 1.5 m behind a bandit: noticed", (lastLog("WO223-GATE figure=kcd2mp_1 npc=bandit_ear named: close at 1.5 m") or "") ~= "", lastLog("WO223-GATE"))
    ear.px = 203
    W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}
    NOW = 195; clearLog(); KCD2MP_W223Tick(); NOW = 195.5; KCD2MP_W223Tick()
    check("c: 3 m behind it, silent: not noticed", lastLog("WO223-GATE figure=kcd2mp_1 npc=bandit_ear") == nil, lastLog("WO223-GATE"))
    ear.px = 290
    check("c: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (d) the radius cvars
do
    check("d: a figure 200 m away: the radius reaches him (270 m), the budget is the far one", CVARS.WH_AI_LOD_MaxDetailDistance == "270" and CVARS.WH_AI_LOD_MaxCountDetail == "120",
        CVARS.WH_AI_LOD_MaxDetailDistance .. "/" .. CVARS.WH_AI_LOD_MaxCountDetail)
    fig.px = 185; NOW = 140; KCD2MP_W223Tick()
    check("d: a little nearer: not narrowed for less than two steps", CVARS.WH_AI_LOD_MaxDetailDistance == "270", CVARS.WH_AI_LOD_MaxDetailDistance)
    fig.px = 50; NOW = 141; KCD2MP_W223Tick()
    check("d: near the host again: the engine's own radius and budget", CVARS.WH_AI_LOD_MaxDetailDistance == "150" and CVARS.WH_AI_LOD_MaxCountDetail == "70",
        CVARS.WH_AI_LOD_MaxDetailDistance .. "/" .. CVARS.WH_AI_LOD_MaxCountDetail)
    fig.px = 300; NOW = 142; KCD2MP_W223Tick()
    check("d: far again: raised", CVARS.WH_AI_LOD_MaxDetailDistance == "360", CVARS.WH_AI_LOD_MaxDetailDistance)
    KCD2MP_W223Reset("the session ended")
    check("d: the session's end puts the engine's own back", CVARS.WH_AI_LOD_MaxDetailDistance == "150" and CVARS.WH_AI_LOD_MaxCountDetail == "70")
    check("d: and says so", (lastLog("WO223-RESET the session ended") or "") ~= "")
end

-- (e) the switch off, or not the host
do
    local bandit = ENTS["bandit_see"]
    fig.px = 200; bandit.px = 205
    sphere(fig, bandit, player)
    KCD2MP_W223Stealth("1", DAY_WALK)
    NOW = 199.5; KCD2MP_W223Tick()
    NOW = 200; KCD2MP_W223Tick()
    local a = attends()
    check("e: on: named, raised", a[#a] == "kcd2mp_1|bandit_see" and CVARS.WH_AI_LOD_MaxDetailDistance == "270", a[#a])
    KCD2MP_W223Cfg("w223_perceive=off")
    HOLDS = {}
    clearLog(); NOW = 201; KCD2MP_W223Tick()
    check("e: w223_perceive off: nothing is held off", next(HOLDS) == nil)
    a = attends()
    check("e: w223_perceive off: the set is emptied once and the radius is the engine's own", a[#a] == "kcd2mp_1|" and CVARS.WH_AI_LOD_MaxDetailDistance == "150", a[#a])
    clearLog(); NOW = 202; KCD2MP_W223Tick()
    check("e: off: nothing more is sent", #attends() == 0)
    KCD2MP_W223Cfg("w223_perceive=on")
    KCD2MP.hitSensorOn = false   -- a joiner's game
    clearLog(); NOW = 203; KCD2MP_W223Tick()
    check("e: not the host: nobody named, nothing raised", #attends() == 0 and CVARS.WH_AI_LOD_MaxDetailDistance == "150")
    KCD2MP.hitSensorOn = true
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (e2) E, the guard: a friend of the host's own player is no enemy of his partner
do
    local ally = mkNpc("ally_of_host", 205, 0, { x = -1, y = 0 }, -1.0)   -- 5 m from the figure, facing it; enemy to the figure by faction
    ally.relP = 1.0                                                      -- ... and a friend of the host's
    fig.px = 200
    sphere(fig, ally, player)
    KCD2MP_W223Stealth("1", DAY_SPRINT)
    HOLDS = {}
    W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}
    clearLog(); NOW = 250; KCD2MP_W223Tick(); NOW = 250.5; KCD2MP_W223Tick(); NOW = 251; KCD2MP_W223Tick()
    local a = attends()
    check("e2: a friend of the host's that reads enemy to the figure is never named", #a == 0 or a[#a] == "kcd2mp_1|", a[#a])
    check("e2: and its own sight of the figure is held off, a minute at a time", (HOLDS["this:ally_of_host"] or 0) == 1 and HOLD_T["this:ally_of_host"] == 60000,
        tostring(HOLDS["this:ally_of_host"]) .. "/" .. tostring(HOLD_T["this:ally_of_host"]))
    check("e2: said once", logCount("WO223-GUARD figure=kcd2mp_1 npc=ally_of_host") == 1, logCount("WO223-GUARD"))
    -- the twin: the guard off -> it is an enemy like any other and is named at once (sprinting in the open, 5 m in front of it)
    KCD2MP_W223Cfg("w223_guard=off")
    clearLog(); NOW = 251.5; KCD2MP_W223Tick()
    a = attends()
    check("e2: w223_guard off (the twin): it is named", a[#a] == "kcd2mp_1|ally_of_host", a[#a])
    KCD2MP_W223Cfg("w223_guard=on")
    ally.px = 290
    NOW = 260; KCD2MP_W223Tick()
    check("e2: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (e3) E, the camp rule: an NPC that is no enemy of the host's own player fights his partner as the game's camp routine fights a player
do
    local TEXT = DAY_SPRINT
    local function fresh(t)
        W223.att = {}; W223.lastSent = {}; W223.meter = {}; W223.figAt = {}; W223.dprev = {}; W223.caught = {}; W223.plan = {}; HOLDS = {}; HELD_UNTIL = {}; DROPS = {}
        GAPS = {}; WATCH = { "this:camper" }; clearLog()
        NOW = t; KCD2MP_W223Stealth("1", TEXT)
    end
    local function beats(n) fineBeats(n) end
    local camper = mkNpc("camper", 206, 0, { x = -1, y = 0 }, -1.0)   -- 6 m from the figure, facing it; enemy to the figure by faction
    camper.relP = 0                                                   -- ... and nothing to the host's own player
    fig.px = 200
    sphere(fig, camper, player)
    local keepPatience = W223.P.campPatienceS
    W223.P.campPatienceS = 3.0
    -- off the camp's ground: never, however plain he is to see
    PRIVATE = false; fig.armed = true
    fresh(300); KCD2MP_W223Tick(); beats(8)
    local a = attends()
    check("e3: off its private ground a camp's NPC is never set on him, armed and sprinting in its view for 4 s", #a == 0 or a[#a] == "kcd2mp_1|", a[#a])
    check("e3: and its own sight of the figure is held off all the while", blind("this:camper") == true and (GAPS["this:camper"] or 0) < 0.11 and (HOLDS["this:camper"] or 0) >= 2,
        string.format("gap %.2f s, holds %s", GAPS["this:camper"] or 0, tostring(HOLDS["this:camper"])))
    -- on its ground, armed and standing: at once
    PRIVATE = true
    fresh(320); KCD2MP_W223Tick()
    a = attends()
    check("e3: on its ground, armed and standing: it fights him at once", a[#a] == "kcd2mp_1|camper"
        and (lastLog("WO223-GATE figure=kcd2mp_1 npc=camper named: caught armed on its ground at 6.0 m") or "") ~= "", a[#a])
    check("e3: the log says who caught him and what follows", (lastLog("WO223-CAMP figure=kcd2mp_1 caught on private ground by camper at 6.0 m (seen; armed 1, crouch 0): armed and standing, it fights him at once") or "") ~= "",
        lastLog("WO223-CAMP"))
    -- on its ground, unarmed: after the camp's patience (3 s here), held until then
    fig.armed = false
    local SOUNDS0 = #SOUNDS
    fresh(340); KCD2MP_W223Tick(); beats(2)
    a = attends()
    check("e3: on its ground, unarmed: caught at once (it would know a player 6 m in front of it), not set on him a second later", (#a == 0 or a[#a] == "kcd2mp_1|")
        and logCount("WO223-CAMP figure=kcd2mp_1 caught") == 1, a[#a])
    beats(2)
    check("e3: its eyes stay held through the camp's patience (3 s here) less the engine's lag: held at 2 s, never un-held till then",
        blind("this:camper") == true and (GAPS["this:camper"] or 0) < 0.11, string.format("gap %.2f s", GAPS["this:camper"] or 0))
    check("e3: and nothing is sounded from his figure while its patience runs", #SOUNDS == SOUNDS0, #SOUNDS - SOUNDS0)
    beats(2)
    a = attends()
    check("e3: at 3 s its eyes are free", blind("this:camper") == false)
    check("e3: when its patience is spent it fights him", a[#a] == "kcd2mp_1|camper" and (lastLog("WO223-GATE figure=kcd2mp_1 npc=camper named: stayed on its ground") or "") ~= "", a[#a])
    -- unarmed, he walks up to it (first had at 6 m, now within 2 m): at once
    fresh(350); KCD2MP_W223Tick(); beats(1)
    camper.px = 201.8
    beats(1)
    a = attends()
    check("e3: unarmed, he comes at it (within 2 m): it fights him", a[#a] == "kcd2mp_1|camper" and (lastLog("WO223-GATE figure=kcd2mp_1 npc=camper named: came at it on its ground at 1.8 m") or "") ~= "", a[#a])
    camper.px = 206
    -- armed but crouching: the patience, not at once
    fig.armed = true
    TEXT = "c=1,l=0,v=3.19,n=1.08,s=0.60,k=5"
    fresh(360); KCD2MP_W223Tick(); beats(2)
    a = attends()
    check("e3: armed but crouching: not at once", #a == 0 or a[#a] == "kcd2mp_1|", a[#a])
    -- he leaves its ground before its patience is spent: nothing; back after a minute, the count starts again
    TEXT = DAY_SPRINT; fig.armed = false
    fresh(380); KCD2MP_W223Tick(); beats(2)
    PRIVATE = false; beats(8)
    a = attends()
    check("e3: he left its ground before its patience was spent: never set on him", #a == 0 or a[#a] == "kcd2mp_1|", a[#a])
    check("e3: its count of him is kept for a minute off its ground", W223.caught["kcd2mp_1"] ~= nil)
    NOW = NOW + 61; KCD2MP_W223Tick()
    check("e3: and started again after", W223.caught["kcd2mp_1"] == nil)
    -- a fight it is in already names it whatever the rule (the host fights the camp)
    camper.combat = true
    fresh(500); KCD2MP_W223Tick()
    a = attends()
    check("e3: in a fight already: named", a[#a] == "kcd2mp_1|camper", a[#a])
    camper.combat = false
    -- the twin: the guard off -> an enemy by faction, on sight, wherever he stands
    KCD2MP_W223Cfg("w223_guard=off")
    fresh(520); KCD2MP_W223Tick()
    a = attends()
    check("e3: w223_guard off (the twin): set on him on sight, off its ground", a[#a] == "kcd2mp_1|camper" and (lastLog("WO223-GATE figure=kcd2mp_1 npc=camper named: seen") or "") ~= "", a[#a])
    KCD2MP_W223Cfg("w223_guard=on")
    W223.P.campPatienceS = keepPatience
    PRIVATE = false; fig.armed = false
    camper.px = 290
    NOW = 540; KCD2MP_W223Tick()
    check("e3: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (b3) the split: with the host's own player beside an enemy and nothing after him, one enemy on the figure is enough for a moment
do
    local saved = { soul = player.soul, px = player.px, py = player.py, sneak = KCD2MP.playerSneaking }
    local DANGER = false
    player.soul = { IsInCombatDanger = function() return DANGER end }
    player.px, player.py = 100, 0
    W223.split = {}
    local np = { x = 104, y = 0, z = 0 }
    clearLog()
    check("b3: nobody has him yet: the first enemy is his", KCD2MP_W223SplitHold("kcd2mp_1", "bandit_a", np, 700, 0) == false)
    check("b3: one has him, the host stands 4 m from the next with nothing after him: left to the host",
        KCD2MP_W223SplitHold("kcd2mp_1", "bandit_b", np, 700, 1) == true and (lastLog("WO223-SPLIT figure=kcd2mp_1 npc=bandit_b left to the host: he stands 4.0 m") or "") ~= "")
    check("b3: still held two seconds on, one line for it", KCD2MP_W223SplitHold("kcd2mp_1", "bandit_b", np, 702, 1) == true and logCount("WO223-SPLIT") == 1, logCount("WO223-SPLIT"))
    DANGER = true
    check("b3: the host has an enemy on him: it is named for the figure", KCD2MP_W223SplitHold("kcd2mp_1", "bandit_b", np, 702.5, 1) == false
        and (lastLog("WO223-SPLIT figure=kcd2mp_1 over after 2.5 s: the host has an enemy on him") or "") ~= "")
    DANGER = false
    check("b3: over is over: no second hold in that fight", KCD2MP_W223SplitHold("kcd2mp_1", "bandit_c", np, 703, 1) == false)
    W223.split = {}
    check("b3: a new split holds ...", KCD2MP_W223SplitHold("kcd2mp_1", "bandit_b", np, 800, 2) == true)
    check("b3: ... five seconds at most", KCD2MP_W223SplitHold("kcd2mp_1", "bandit_b", np, 805.1, 2) == false and (lastLog("over after 5.1 s: its time is up") or "") ~= "")
    W223.split = {}
    player.px = 130
    check("b3: the host is not at that enemy (26 m): named", KCD2MP_W223SplitHold("kcd2mp_1", "bandit_b", np, 900, 1) == false)
    player.px = 100; KCD2MP.playerSneaking = true
    check("b3: a host who crouches is offered to nobody: named", KCD2MP_W223SplitHold("kcd2mp_1", "bandit_b", np, 900, 1) == false)
    KCD2MP.playerSneaking = saved.sneak
    KCD2MP_W223Cfg("w223_split=off")
    check("b3: w223_split off (the twin): named", KCD2MP_W223SplitHold("kcd2mp_1", "bandit_b", np, 900, 1) == false)
    KCD2MP_W223Cfg("w223_split=on")
    W223.split = {}
    player.soul, player.px, player.py = saved.soul, saved.px, saved.py
    check("b3: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (b4) the camp's call: an enemy fights the figure, nothing is after the host: one alert enemy near the host is called on him
do
    local saved = { soul = player.soul, px = player.px, py = player.py, sneak = KCD2MP.playerSneaking, human = player.human, actor = player.actor }
    local DANGER, ARMED = false, true
    player.soul = { IsInCombatDanger = function() return DANGER end }
    player.human = { IsWeaponDrawn = function() return ARMED end }
    player.px, player.py = 100, 0
    local near = mkNpc("call_near", 106, 0, { x = -1, y = 0 }, -1.0); near.vir = 28.1          -- alert, 6 m from the host
    local far = mkNpc("call_far", 120, 0, { x = -1, y = 0 }, -1.0); far.vir = 28.1             -- alert, 20 m from him
    local calm = mkNpc("call_calm", 104, 3, { x = -1, y = 0 }, -1.0)                           -- 5 m, not alert: by its clear line
    local camp = mkNpc("call_camp", 103, 0, { x = -1, y = 0 }, -1.0); camp.vir = 28.1          -- a camp's NPC, 3 m from him
    local function cand(e, isCamp) return { e = e, nm = e:GetName(), np = { x = e.px, y = e.py, z = 0 }, camp = isCamp == true } end
    local function calls() local n = 0; for _, m in ipairs(MSGS) do if m.kind == "crime:attackInitiatedByConcept" then n = n + 1 end end return n end
    local function fresh() W223.call = {}; MSGS = {}; clearLog() end
    fresh()
    check("b4: nobody fights the figure: no call", KCD2MP_W223SplitCall("kcd2mp_1", 1000, { cand(near) }, 0) == nil and calls() == 0)
    check("b4: one fights the figure, the host has nobody: the alert enemy 6 m from him is called on him (the far one is not)",
        KCD2MP_W223SplitCall("kcd2mp_1", 1000, { cand(far), cand(near) }, 1) == "call_near" and calls() == 1
        and MSGS[#MSGS].id == "this:call_near" and MSGS[#MSGS].t.target == "this:player" and MSGS[#MSGS].t.priorityTarget == true, calls())
    check("b4: and the agent is told, for the DLL's pin", (lastLog("w223_call call_near") or lastLog("w223_call")) ~= nil, lastLog("w223_call"))
    check("b4: the line says who, how far and why",
        (lastLog("WO223-SPLIT figure=kcd2mp_1 npc=call_near called on the host: he stands 6.0 m from it (alert; its enemy) with no enemy on him while 1 fight the figure sent=true") or "") ~= "", lastLog("WO223-SPLIT"))
    check("b4: the enemy called is the host's for eight seconds: not the figure's", KCD2MP_W223Called("kcd2mp_1", "call_near", 1000.5) == true
        and KCD2MP_W223Called("kcd2mp_1", "call_near", 1008.5) == false and KCD2MP_W223Called("kcd2mp_1", "call_calm", 1000.5) == false)
    check("b4: not again within three seconds", KCD2MP_W223SplitCall("kcd2mp_1", 1001, { cand(near), cand(calm) }, 1) == nil and calls() == 1)
    check("b4: nothing came of it: a second call, to another enemy (not alert, 5 m, a clear line)",
        KCD2MP_W223SplitCall("kcd2mp_1", 1003.5, { cand(near), cand(calm) }, 1) == "call_calm" and calls() == 2 and (lastLog("in clear view") or "") ~= "", calls())
    check("b4: no third in that fight", KCD2MP_W223SplitCall("kcd2mp_1", 1010, { cand(near), cand(calm), cand(far) }, 2) == nil and calls() == 2)
    fresh(); DANGER = true
    check("b4: the host has an enemy on him: no call", KCD2MP_W223SplitCall("kcd2mp_1", 1100, { cand(near) }, 1) == nil and calls() == 0)
    DANGER = false; KCD2MP.playerSneaking = true
    check("b4: a host who crouches is offered to nobody", KCD2MP_W223SplitCall("kcd2mp_1", 1100, { cand(near) }, 1) == nil and calls() == 0)
    KCD2MP.playerSneaking = saved.sneak
    fresh()
    check("b4: only the far one (20 m): no call", KCD2MP_W223SplitCall("kcd2mp_1", 1200, { cand(far) }, 1) == nil and calls() == 0)
    fresh(); PRIVATE = false
    check("b4: a camp's NPC, the host armed but not on its ground: no call", KCD2MP_W223SplitCall("kcd2mp_1", 1300, { cand(camp, true) }, 1) == nil and calls() == 0)
    PRIVATE = true; ARMED = false
    check("b4: a camp's NPC, the host on its ground, unarmed: no call (the camp's own patience runs)", KCD2MP_W223SplitCall("kcd2mp_1", 1300, { cand(camp, true) }, 1) == nil and calls() == 0)
    ARMED = true
    check("b4: a camp's NPC, the host armed on its ground: called (the camp's own rule fights him at once)",
        KCD2MP_W223SplitCall("kcd2mp_1", 1300, { cand(camp, true) }, 1) == "call_camp" and calls() == 1 and (lastLog("(alert; armed on its ground)") or "") ~= "", lastLog("WO223-SPLIT"))
    PRIVATE = false
    fresh(); KCD2MP_W223Cfg("w223_split=off")
    check("b4: w223_split off (the twin): no call", KCD2MP_W223SplitCall("kcd2mp_1", 1400, { cand(near) }, 1) == nil and calls() == 0)
    KCD2MP_W223Cfg("w223_split=on")
    fresh()
    for _, n in ipairs({ "call_near", "call_far", "call_calm", "call_camp" }) do ENTS[n] = nil end
    player.soul, player.px, player.py, player.human, player.actor = saved.soul, saved.px, saved.py, saved.human, saved.actor
    check("b4: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (c2) the joiner: a copy whose host NPC fights this player has its own eyes held off the other players' figures
do
    local savedGhosts, savedHeld, savedHolds = KCD2MP.ghosts, HELD_UNTIL, HOLDS
    HELD_UNTIL = {}; HOLDS = {}
    local hostFig = { this = { id = 0xF160 }, GetName = function() return "kcd2mp_0" end }
    local copy = { this = { id = 0xC0B1 }, GetName = function() return "bandit_copy" end }
    ENTS["bandit_copy"] = copy
    KCD2MP.ghosts = { ["0"] = { entity = hostFig } }
    W223.eyes = nil
    clearLog(); NOW = 1000
    KCD2MP_W223CopyEyes("bandit_copy")
    check("c2: named by the agent: its eyes are held off the host's figure for 1.5 s", HOLDS[0xC0B1] == 1 and HOLDS.last.target == 0xF160 and HOLDS.last.timeout == "1500ms",
        tostring(HOLDS[0xC0B1]) .. " " .. tostring(HOLDS.last and HOLDS.last.timeout))
    check("c2: one line says so", (lastLog("WO223-EYES npc=bandit_copy its host NPC fights you: its own eyes are held off 1 other figure(s)") or "") ~= "")
    NOW = 1000.5; KCD2MP_W223HoldTick()
    check("c2: no second hold while the first runs (the engine would drop it)", HOLDS[0xC0B1] == 1 and (DROPS[0xC0B1] or 0) == 0, HOLDS[0xC0B1])
    NOW = 1001; KCD2MP_W223CopyEyes("bandit_copy")
    NOW = 1001.5; KCD2MP_W223HoldTick()
    check("c2: the next hold as the first one ends", HOLDS[0xC0B1] == 2 and (DROPS[0xC0B1] or 0) == 0, HOLDS[0xC0B1])
    NOW = 1002; KCD2MP_W223CopyEyes("")
    check("c2: its host NPC turned away: no longer named, one line", (lastLog("WO223-EYES npc=bandit_copy no longer (after 2.0 s)") or "") ~= "")
    NOW = 1003.5; KCD2MP_W223HoldTick()
    check("c2: and nothing more is held", HOLDS[0xC0B1] == 2, HOLDS[0xC0B1])
    NOW = 1010; KCD2MP_W223CopyEyes("bandit_copy")
    check("c2: named again: held again", HOLDS[0xC0B1] == 3, HOLDS[0xC0B1])
    NOW = 1014; KCD2MP_W223HoldTick()
    check("c2: the agent stopped telling for over 3 s: nothing is held any more", HOLDS[0xC0B1] == 3, HOLDS[0xC0B1])
    KCD2MP_W223Cfg("w223_copy_target=off")
    NOW = 1020; KCD2MP_W223CopyEyes("bandit_copy")
    check("c2: w223_copy_target off (the twin): no hold", HOLDS[0xC0B1] == 3, HOLDS[0xC0B1])
    KCD2MP_W223Cfg("w223_copy_target=on")
    W223.eyes = nil
    ENTS["bandit_copy"] = nil
    KCD2MP.ghosts, HELD_UNTIL, HOLDS = savedGhosts, savedHeld, savedHolds
    check("c2: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (f) the notice log
do
    local bandit = ENTS["bandit_see"]
    fig.px = 200; bandit.px = 212; bandit.face = { x = -1, y = 0 }   -- 12 m from the figure, facing it; the host is 212 m behind it
    sphere(fig, bandit, player)
    clearLog(); NOW = 300; KCD2MP_W223Tick()
    check("f: a calm enemy writes no notice line", logCount("WO223-NOTICE") == 0)
    bandit.vir = 28.1
    NOW = 300.5; KCD2MP_W223Tick()
    local l = lastLog("WO223-NOTICE npc=bandit_see") or ""
    check("f: the enemy that turned alert noticed the joiner, 12 m away, walking", l:find("noticed=joiner:1 at=12.0 pace=3.0 crouch=0 in_view=1 other=host", 1, true) ~= nil, l)
    NOW = 301; KCD2MP_W223Tick()
    check("f: one line per alert", logCount("WO223-NOTICE") == 1, logCount("WO223-NOTICE"))
    -- an enemy near the host's own player, the joiner far: it noticed the host
    local b2 = mkNpc("bandit_host", 8, 0, { x = -1, y = 0 }, -1.0)
    b2.vir = 28.1
    System.GetEntitiesInSphere = function(p) if p.x < 100 then return { b2, player } end return { fig, bandit, player } end
    NOW = 302; KCD2MP_W223Tick()
    l = lastLog("WO223-NOTICE npc=bandit_host") or ""
    check("f: an enemy beside the host's player noticed the host", l:find("noticed=host at=8.0", 1, true) ~= nil and l:find("other=joiner:1", 1, true) ~= nil, l)
    KCD2MP_W223Cfg("w223_notice_log=off")
    local b3 = mkNpc("bandit_quiet", 9, 0, { x = -1, y = 0 }, -1.0)
    b3.vir = 28.1
    System.GetEntitiesInSphere = function(p) if p.x < 100 then return { b3, player } end return { fig, player } end
    NOW = 303; KCD2MP_W223Tick()
    check("f: w223_notice_log off: no line", lastLog("WO223-NOTICE npc=bandit_quiet") == nil)
    KCD2MP_W223Cfg("w223_notice_log=on")
    KCD2MP_W223Status()
    check("f: the status line lists the switches and the host's numbers", (lastLog("WO223-STATUS") or ""):find("mp_w223_perceive=on", 1, true) ~= nil)
    check("f: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (g) the scans' idle rate: with no enemy in any scan for six beats the scans run on every second beat; an enemy puts them back
do
    local villager = mkNpc("villager_g", 205, 0, { x = -1, y = 0 }, 0.5)
    fig.px = 200
    local calls = 0
    local keep = System.GetEntitiesInSphere
    System.GetEntitiesInSphere = function(p) calls = calls + 1; return { fig, villager, player } end
    W223.quietBeats = 0
    NOW = 400
    for _ = 1, 6 do NOW = NOW + 0.5; KCD2MP_W223Tick() end
    local busy = calls
    calls = 0
    for _ = 1, 8 do NOW = NOW + 0.5; KCD2MP_W223Tick() end
    check("g: the first six quiet beats scan on every beat", busy >= 6, busy)
    check("g: after them the scans run on every second beat", calls >= 1 and calls <= (busy / 6) * 4 + 1, tostring(calls) .. " after " .. tostring(busy))
    check("g: the status line counts the idle beats", (W223.stats.idleBeats or 0) >= 3, W223.stats.idleBeats)
    -- an enemy in reach: every beat again once a scan has met it
    local bandit = mkNpc("bandit_g", 230, 0, { x = 1, y = 0 }, -1.0)
    System.GetEntitiesInSphere = function(p) calls = calls + 1; return { fig, villager, bandit, player } end
    NOW = NOW + 0.5; KCD2MP_W223Tick(); NOW = NOW + 0.5; KCD2MP_W223Tick()   -- one of the two is a scan
    calls = 0
    for _ = 1, 4 do NOW = NOW + 0.5; KCD2MP_W223Tick() end
    check("g: an enemy in reach puts the scans back on every beat", calls >= 4, calls)
    -- the notice log shares the gate's scan for a pair that stands together (one query a beat)
    local hx = player.px
    player.px = fig.px + 5
    calls = 0
    NOW = NOW + 0.5; KCD2MP_W223Tick()
    check("g: a pair standing together costs one query a beat", calls == 1, calls)
    player.px = hx
    System.GetEntitiesInSphere = keep
    check("g: no Lua errors", #ERRS == 0, ERRS[1])
end

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
