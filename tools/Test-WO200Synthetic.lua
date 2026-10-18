-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-200 synthetic test: the Lua half of "actions go to the authority; values come back", against the real kdcmp.lua under MoonSharp.
--
--   (a) the switches: 15 names, each its own console command; V.1-V.3 ship off, the rest on; one-argument setters
--   (b) I.6 scoped twins: every copy named in this player's conversation is woken for it (scene guard on or off), logged, and paused
--       again at its end; the switch off with the scene guard off wakes only what WO-151 did
--   (c) I.3 the quest-item mirror: the host reports its changed counts (and all it holds every 60 s); the joiner compares (the game
--       allows a quest item only through its AddQuestItem effect): differs / agreed again / pending while his own replay may be confirmed
--   (d) II.5 stand-ins: a stand-in carries a token and is kept out of saves; after a load the world's own NPC of that name is never
--       removed (the field's deleted lords); a load forgets the table; a stand-in whose world NPC is back is retired
--   (e) II.2 the host's word on a rider: "on foot" dismounts a mounted copy; the same horse keeps him (no geometry decides)
--
-- What this proves: the Lua rules. Live evidence: docs/WO-200-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; CCMDS = {}; LOCKS = {}; MSGS = {}; EXTRA = {}
QINV = {}; CREATE_REFUSED = {}

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
System.DrawText = function() end
System.RemoveEntity = function(eid)
    for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil; e.removed = true end end
    for _, e in ipairs(EXTRA) do if e.id == eid then e.removed = true end end
end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.GetTerrainElevation = function() return 0 end
System.GetEntity = function(id)
    for _, e in pairs(ENTS) do if e.id == id then return e end end
    for _, e in ipairs(EXTRA) do if e.id == id and not e.removed then return e end end
    return nil
end
System.GetEntitiesByClass = function(cls)
    local out = {}
    for _, e in pairs(ENTS) do if e.class == cls then out[#out + 1] = e end end
    for _, e in ipairs(EXTRA) do if e.class == cls and not e.removed then out[#out + 1] = e end end
    return out
end
ENTITY_FLAG_NO_SAVE = 32768
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Game.AddSaveLock = function(name) if LOCKS[name] then return false end LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] == true; LOCKS[name] = nil; return had end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }

local NEXTID = 0x0E0000
local function mkEntity(name, x, y, z, cls, keep)
    NEXTID = NEXTID + 1
    local e = { class = cls or "NPC", id = "userdata: " .. string.format("%016X", NEXTID), px = x or 0, py = y or 0, pz = z or 0, rz = 0,
                dead = false, hp = 100, hidden = false, flags = 0, mounted = false, dismounts = 0, mounts = 0 }
    e.this = { id = "this:" .. name }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self, t) t = t or {}; t.x, t.y, t.z = self.px, self.py, self.pz; return t end
    e.GetWorldAngles = function(self, t) t = t or {}; t.x, t.y, t.z = 0, 0, self.rz; return t end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.SetFlags = function(self, f, mode) if mode == 3 then self.flags = self.flags + f else self.flags = f end end
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.StartAnimation = function() end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end }
    e.human = { IsInDialog = function() return false end, IsMounted = function() return e.mounted end,
                ForceDismount = function() e.mounted = false; e.dismounts = e.dismounts + 1 end,
                ForceMount = function() e.mounted = true; e.mounts = e.mounts + 1 end }
    e.soul = { GetId = function() return "userdata: 05000000000001DC" end }
    if keep then EXTRA[#EXTRA + 1] = e else ENTS[name] = e end
    return e
end

XGenAIModule = mkstub()
XGenAIModule.SpawnEntity = function(t) mkEntity(t.Name, t.Pos[1], t.Pos[2], t.Pos[3], t.ClassName) end
XGenAIModule.MakeTableFromType = function(kind) return { kind = kind, information = {} } end

player = { id = 1, this = { id = 777 }, GetName = function(self) return "Dude" end,
           GetWorldPos = function(self) return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end },
           human = { IsMounted = function() return false end, IsInDialog = function() return false end },
           inventory = {
               GetCountOfClass = function(self, g) return QINV[g] or 0 end,
               CreateItem = function(self, g, hp, n) if CREATE_REFUSED[g] then return false end QINV[g] = (QINV[g] or 0) + (n or 1); return true end,
               DeleteItemOfClass = function(self, g, n) local h = QINV[g] or 0; local d = math.min(h, n or 0); QINV[g] = h - d; return d end,
           } }

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

-- (a) the switches -- read before any scenario touches them
do
    local names = { "s1_replay", "s1_route", "qitem_mirror", "joiner_quiet_clocks", "joiner_no_puppet_quests", "joiner_scoped_twins",
                    "copy_health_from_host", "rider_from_host", "copy_hands_from_host", "copy_neutral_always", "world_clock_from_host",
                    "stream_dogs", "joiner_receive_only", "figure_is_player", "nearest_player_triggers" }
    local off = { joiner_receive_only = true, figure_is_player = true, nearest_player_triggers = true }
    for _, n in ipairs(names) do
        check("a: " .. n .. " defaults " .. (off[n] and "off" or "on"), KCD2MP_W200On(n) == not off[n])
        check("a: mp_" .. n .. " is registered", CCMDS["mp_" .. n] ~= nil)
    end
    check("a: mp_w200_status is registered", CCMDS["mp_w200_status"] ~= nil)
    clearLog()
    KCD2MP_W200SetScopedTwins("off")
    check("a: a one-argument setter turns its switch off and tells the agent", KCD2MP_W200On("joiner_scoped_twins") == false
        and (evts("w200_cfg")[1] or "") == "joiner_scoped_twins=off", evts("w200_cfg")[1])
    KCD2MP_W200SetScopedTwins("on")
    check("a: ...and on again", KCD2MP_W200On("joiner_scoped_twins") == true)
    check("a: a bad value is refused", KCD2MP_W200Set("joiner_scoped_twins", "maybe") == false and KCD2MP_W200On("joiner_scoped_twins") == true)
    check("a: no Lua errors at load", #ERRS == 0, ERRS[1])
end

local function resetTalk()
    KCD2MP.npcPuppets = {}; KCD2MP._npcPaused = {}; KCD2MP._npcResumePending = {}; KCD2MP._npcEverPaused = {}; KCD2MP._npcPauseExec = {}
    KCD2MP.w137.talking = {}
    ENTS = {}; EXTRA = {}; CMDS = {}; ERRS = {}
    clearLog()
end

-- (b) I.6: the trialog's second speaker is a paused copy too
do
    resetTalk(); NOW = 1000
    KCD2MP_W137Session(false, true, true)
    KCD2MP.w151.sceneGuard = false   -- the scene guard off: only I.6 can wake the second speaker
    for _, n in ipairs({ "b_semin", "b_suk" }) do
        mkEntity(n, 2, 0, 0)
        KCD2MP.npcPuppets[n] = { owner = 0 }
        KCD2MP._npcPaused[n] = NOW
    end
    KCD2MP_W137TalkResume("b_semin", "OnTalk")   -- this player's talk wakes the target (WO-137)
    CMDS = {}
    KCD2MP_W137TalkAttempt(2699, "Dude b_semin b_suk")
    local suk = KCD2MP.w137.talking["b_suk"]
    check("b: the second speaker (a paused copy) is woken for the conversation", suk ~= nil and suk.twin == true and suk.started == true
        and cmdCount("wh_ai_ResumeNPC b_suk") == 1, table.concat(CMDS, " | "))
    check("b: ...the talk target is not woken twice", cmdCount("wh_ai_ResumeNPC b_semin") == 0)
    check("b: ...logged as a scoped twin", logCount("WO200-TWINS wake npc=b_suk id=2699") == 1, lastLog("WO200-TWINS"))
    check("b: ...the talk target keeps its own talk record", KCD2MP.w137.talking["b_semin"] ~= nil and KCD2MP.w137.talking["b_semin"].twin == nil)
    CMDS = {}
    KCD2MP_W137TalkEndNow("b_suk", "dialog-ended")
    check("b: at its end the twin is paused again", cmdCount("wh_ai_PauseNPC b_suk") == 1 and KCD2MP.w137.talking["b_suk"] == nil, table.concat(CMDS, " | "))
    check("b: ...logged", logCount("WO200-TWINS end npc=b_suk") == 1, lastLog("WO200-TWINS end"))

    -- the switch off and the scene guard off: the second speaker stays paused (WO-151's behaviour with its guard off)
    resetTalk(); NOW = 1100
    KCD2MP_W200Set("joiner_scoped_twins", "off")
    mkEntity("b2_suk", 2, 0, 0)
    KCD2MP.npcPuppets["b2_suk"] = { owner = 0 }
    KCD2MP._npcPaused["b2_suk"] = NOW
    KCD2MP_W137TalkAttempt(2700, "Dude b2_suk")
    check("b: the switch off (scene guard off): the copy is not woken", cmdCount("wh_ai_ResumeNPC b2_suk") == 0 and logCount("WO200-TWINS") == 0)
    KCD2MP_W200Set("joiner_scoped_twins", "on")
    KCD2MP.w151.sceneGuard = true
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (c) I.3: quest items go to both; the host's counts decide
do
    local G1, G2, G3 = "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "33333333-3333-3333-3333-333333333333"
    clearLog(); ERRS = {}; NOW = 2000; QINV = {}
    KCD2MP_W200QClassReset()
    KCD2MP_W200QClassAdd(G1 .. " " .. G2 .. " " .. G3 .. " not-a-guid")
    check("c: the agent's classes are taken (malformed ones dropped)", KCD2MP.w200 == nil or true)
    -- the host: its Henry gets G1 x1 -> reported; nothing changed -> nothing; 60 s -> everything it holds
    QINV[G1] = 1
    KCD2MP_W200QItemsTick(true)
    local ev = evts("w200_qitems")
    check("c: host: a changed count is reported", #ev == 1 and ev[1]:find(G1 .. ":1", 1, true) ~= nil, ev[1])
    clearLog(); NOW = NOW + 3
    KCD2MP_W200QItemsTick(true)
    check("c: host: nothing changed -> nothing sent", #evts("w200_qitems") == 0)
    QINV[G1] = 0
    clearLog(); NOW = NOW + 3
    KCD2MP_W200QItemsTick(true)
    check("c: host: a removal is reported as count 0", (evts("w200_qitems")[1] or ""):find(G1 .. ":0", 1, true) ~= nil, evts("w200_qitems")[1])
    QINV[G2] = 2
    KCD2MP_W200QItemsTick(true)
    clearLog(); NOW = NOW + 61
    KCD2MP_W200QItemsTick(true)
    local full = evts("w200_qitems")[1] or ""
    check("c: host: every 60 s all it holds (for a late joiner), never the zeros", full:find(G2 .. ":2", 1, true) ~= nil and full:find(G1, 1, true) == nil, full)

    -- the joiner: primed first (his own quest items are not "changes" at the start); he compares, he never writes a quest item
    -- (the game allows a quest item only through its AddQuestItem effect -- the quest-state mirror carries it)
    KCD2MP_W200QClassReset(); KCD2MP_W200QClassAdd(G1 .. " " .. G2 .. " " .. G3)
    QINV = { [G3] = 1 }
    clearLog(); NOW = 3000
    KCD2MP_W200QItemsTick(false)
    check("c: joiner: his tick never reports", #evts("w200_qitems") == 0)
    KCD2MP_W200QItemHost(G1, 1, 0)
    check("c: joiner: a count the host holds and he does not: differs, logged once, nothing written", (QINV[G1] or 0) == 0
        and logCount("WO200-QITEM differs class=" .. G1 .. " host=1 here=0") == 1, lastLog("WO200-QITEM"))
    KCD2MP_W200QItemHost(G1, 1, 0)
    check("c: joiner: ...the same difference is not logged again", logCount("WO200-QITEM differs class=" .. G1) == 1)
    QINV[G1] = 1   -- the mirrored quest value activated his AddQuestItem effect
    KCD2MP_W200QItemHost(G1, 1, 0)
    check("c: joiner: agreed again once the quest value arrives", logCount("WO200-QITEM agreed again class=" .. G1) == 1, lastLog("WO200-QITEM"))
    KCD2MP_W200QItemHost(G3, 0, 0)
    check("c: joiner: one the host does not hold is NOT taken (the game forbids it): differs", (QINV[G3] or 0) == 1
        and logCount("WO200-QITEM differs class=" .. G3) == 1)
    -- his own conversation gave him G2 (a local change); the host's count (0) arrives while his replay may still be confirmed
    QINV[G2] = 1
    NOW = NOW + 3
    KCD2MP_W200QItemsTick(false)
    NOW = NOW + 10
    KCD2MP_W200QItemHost(G2, 0, 1)
    check("c: joiner: his own change is pending (not a difference) while his replay may be confirmed", logCount("WO200-QITEM pending class=" .. G2) == 1
        and logCount("WO200-QITEM differs class=" .. G2) == 0, lastLog("WO200-QITEM"))
    NOW = NOW + 301
    KCD2MP_W200QItemHost(G2, 0, 1)
    check("c: joiner: ...at most 5 minutes, then a difference", logCount("WO200-QITEM differs class=" .. G2) == 1)
    KCD2MP_W200Set("qitem_mirror", "off")
    clearLog()
    KCD2MP_W200QItemHost(G1, 0, 0)
    check("c: the switch off: nothing compared", logCount("WO200-QITEM") == 0)
    KCD2MP_W200Set("qitem_mirror", "on")
    check("c: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (d) II.5: a stand-in never outlives a join; the world's own NPC is never removed by name
do
    clearLog(); ERRS = {}; NOW = 4000; ENTS = {}; EXTRA = {}
    KCD2MP.wo102.authorityHost = true
    KCD2MP.hitSensorOn = false
    KCD2MP.w131.guard = true
    KCD2MP_W131Tick(true, true)
    KCD2MP.w131.standins = {}
    KCD2MP.w136.souls["d_lord"] = { cls = "NPC", guid = "41d4149c-0000-4000-8000-000000000000" }
    local e = KCD2MP_W131StandIn("d_lord", 10, 10, 0, 0, 0)
    local entry = KCD2MP.w131.standins["d_lord"]
    check("d: a stand-in is spawned for a missing NPC", e ~= nil and entry ~= nil, lastLog("WO131-STANDIN"))
    check("d: ...it carries its token and is kept out of every save", e ~= nil and e.kcdmpStandIn == entry.token and e.flags >= 32768, e and e.flags)
    -- the field: the rejoin's load replaced every body; the table outlived it; the world's own lord now answers to the name
    ENTS["d_lord"] = nil
    local lord = mkEntity("d_lord", 50, 50, 0)
    KCD2MP_W131RemoveStandIn("d_lord", "agent-silent")
    check("d: the world's own NPC of that name is never removed (the field's deleted lords)", ENTS["d_lord"] == lord and not lord.removed
        and logCount("WO200-STANDIN kept npc=d_lord why=agent-silent") == 1, lastLog("WO200-STANDIN"))
    check("d: ...and the table forgot it", KCD2MP.w131.standins["d_lord"] == nil)
    -- its own body is removed when the stream stops
    ENTS = {}; NOW = NOW + 1   -- (one spawn per 0.5 s)
    local e2 = KCD2MP_W131StandIn("d_lord", 10, 10, 0, 0, 0)
    NOW = NOW + 1
    KCD2MP_W131RemoveStandIn("d_lord", "stream-stopped")
    check("d: the stand-in's own body is removed", e2 ~= nil and e2.removed == true and ENTS["d_lord"] == nil)
    -- a load forgets the table (nothing removed)
    ENTS = {}; NOW = NOW + 1
    KCD2MP_W131StandIn("d_lord", 10, 10, 0, 0, 0)
    local n = KCD2MP_W131ForgetStandIns("load:label")
    check("d: a load forgets every stand-in, removing nothing", n == 1 and next(KCD2MP.w131.standins) == nil and ENTS["d_lord"] ~= nil
        and logCount("WO200-STANDIN forget n=1") == 1, lastLog("WO200-STANDIN forget"))
    -- the world's NPC comes back (its layer loaded again) while a stand-in stands: the stand-in is retired, the NPC stays
    ENTS = {}; NOW = NOW + 1
    local s3 = KCD2MP_W131StandIn("d_lord", 10, 10, 0, 0, 0)
    local back = mkEntity("d_lord", 60, 60, 0, "NPC", true)   -- a second body of that name (the world's own)
    NOW = NOW + 6
    KCD2MP_W131StandInSweep()
    check("d: a stand-in whose world NPC is back is retired", s3 ~= nil and s3.removed == true and not back.removed
        and KCD2MP.w131.standins["d_lord"] == nil and logCount("WO200-STANDIN retired npc=d_lord") == 1, lastLog("WO200-STANDIN"))
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (e) II.2: the host's word on a rider
do
    clearLog(); ERRS = {}; NOW = 5000; ENTS = {}; EXTRA = {}
    local r = mkEntity("e_rider", 0, 0, 0)
    local h = mkEntity("e_horse", 0, 0, 0, "Horse")
    r.mounted = true
    KCD2MP.w164.riders["e_rider"] = { horse = "e_horse", mounted = true, n = 3 }
    local p = { owner = 0 }
    KCD2MP_W200Mount("e_rider", "e_horse")
    check("e: the host says he rides this horse: he stays mounted (no geometry decides)", KCD2MP_W164RiderHeld("e_rider", p, r) == true and r.dismounts == 0)
    KCD2MP_W200Mount("e_rider", "")
    check("e: the host says on foot: he is dismounted", KCD2MP_W164RiderHeld("e_rider", p, r) == false and r.dismounts == 1
        and logCount("WO200-RIDER dismount rider=e_rider") == 1, lastLog("WO200-RIDER"))
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (f) Act IV: a save waits for a conversation, never for the player's own monologue or a bark; a waiting save says why; the noise actions
do
    clearLog(); ERRS = {}; NOW = 6000
    local inDialog = true
    player.human.IsInDialog = function() return inDialog end
    KCD2MP.w175.guard = true
    local r = W175.busyReason()
    check("f: no agent word yet: IsInDialog alone decides (as before)", r == "dialogue", r)
    KCD2MP_W200DialogKinds(0, 1)   -- the agent: his only open dialogue is a monologue (the field's stomach growl)
    r = W175.busyReason()
    check("f: his own monologue does not block a save", r == nil, r)
    check("f: ...logged", logCount("WO200-SAVEWAIT none") == 1, lastLog("WO200-SAVEWAIT"))
    KCD2MP_W200DialogKinds(1, 1)   -- a real conversation is open as well
    check("f: a real conversation still blocks", W175.busyReason() == "dialogue")
    clearLog()
    check("f: the mod's own save deferred while it waits", KCD2MP_W175SaveAllowed("test") == false)
    check("f: ...WO200-SAVEWAIT reason=dialogue logged", logCount("WO200-SAVEWAIT reason=dialogue kind=test") == 1, lastLog("WO200-SAVEWAIT"))
    check("f: the toast names its reason", KCD2MP_W200SaveWaitText("loot-take") == "Saving waits a moment: an item is changing hands."
        and KCD2MP_W200SaveWaitText("dialogue") == "Saving waits a moment: you are in a conversation.")
    KCD2MP_W200DialogKinds(0, 0); inDialog = false
    -- noise: the host makes the engine's own perception sounds
    local SOUNDS = {}
    XGenAIModule.ProduceSound = function(id, pos, mult) SOUNDS[#SOUNDS + 1] = { id = id, x = pos.x } end
    TIMERS = {}
    KCD2MP_W200NoisePulses("lockpick", 10, 20, 0, 1)
    check("f: a joiner's lockpick: the engine's lockpick sound (2) at once", #SOUNDS == 1 and SOUNDS[1].id == 2, #SOUNDS)
    for _, t in ipairs(TIMERS) do pcall(t.f) end
    check("f: ...and three more over the picking", #SOUNDS == 4, #SOUNDS)
    check("f: the same noise within 1 s is not doubled", KCD2MP_W200NoisePulses("lockpick", 10, 20, 0, 1) == false)
    NOW = NOW + 2
    KCD2MP_W200NoisePulses("door", 11, 20, 0, 1)
    check("f: a joiner's door: the engine's door sound (7)", SOUNDS[#SOUNDS].id == 7)
    mkEntity("kcd2mp_1", 5, 5, 0)
    check("f: a joiner's swing on his figure: the combat sound (1) at the figure", KCD2MP_W200NoiseAtBody("combat", "kcd2mp_1") == true and SOUNDS[#SOUNDS].id == 1 and SOUNDS[#SOUNDS].x == 5)
    check("f: ...one per 1.5 s", KCD2MP_W200NoiseAtBody("combat", "kcd2mp_1") == false)
    KCD2MP_W200SetNoise("off")
    NOW = NOW + 5
    check("f: mp_joiner_noise off: silent", KCD2MP_W200NoiseSound("door", 1, 1, 1, 1) == false and CCMDS["mp_joiner_noise"] ~= nil)
    KCD2MP_W200SetNoise("on")
    check("f: no Lua errors", #ERRS == 0, ERRS[1])
end

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
