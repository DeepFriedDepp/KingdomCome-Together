-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-137 synthetic test, against the real kdcmp.lua under MoonSharp.
-- Harness copied from Test-WO136Synthetic.lua (+ the WO-122 puppet-tick helpers).
--   (a) dead is dead: a corpse is never paused or detached; a copy that dies here
--       gets its brain back once; the detach refuses a corpse, not a living NPC
--   (b) a stand-in for a body the host has dead: spawned hidden, shown once it is
--       dead here, removed (never shown) if it never dies; none without owner death
--   (c) talking (joiner): Talk on a host copy frees it for the conversation, the
--       host is told, the copy is paused again at the end / on a timeout / when the
--       player left the dialogue; the request fallback; a forced conversation; an
--       NPC-NPC end; never parked mid-conversation; the gates (sync, active, copy, player)
--   (d) the host's hold: suspended while the partner talks, re-issued, released;
--       an NPC in the host's own conversation; the join pause; the agent going silent
--   (e) the kill switch and the commands
-- What this proves: the Lua half. Live evidence: docs/WO-137-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; SPAWNS = {}; CCMDS = {}; LOCKS = {}; TALKS = {}
PLAYER_IN_DIALOG = false

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
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Game.AddSaveLock = function(name, desc) if LOCKS[name] then return false end LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] == true; LOCKS[name] = nil; return had end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }
XGenAIModule = mkstub()
XGenAIModule.SpawnEntity = function(t) SPAWNS[#SPAWNS + 1] = t; ENTS[t.Name] = mkEntityLate(t.Name, t.Pos[1], t.Pos[2], t.Pos[3]) end

-- the game's BasicAIActions talk/chat entry points (Scripts/Entities/AI/Shared/BasicAIActions.lua)
BasicAIActions = {}
for _, fn in ipairs({ "OnTalk", "OnChat", "OnChatWithFocus", "OnChatRequestAccepted", "OnChatOpen", "OnLoot", "OnPickpocketing" }) do
    local f = fn
    BasicAIActions[f] = function(self, user, slot) TALKS[#TALKS + 1] = f .. ":" .. tostring(self and self:GetName()) end
end
ORIG_ONTALK = BasicAIActions.OnTalk

player = { id = 1, GetName = function(self) return "Dude" end,
           GetWorldPos = function() return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end },
           human = { IsInDialog = function() return PLAYER_IN_DIALOG end } }

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
local function logCount(pat, from)
    local n = 0
    for i = (from or 0) + 1, #LOG do if string.find(LOG[i], pat, 1, true) then n = n + 1 end end
    return n
end
local function lastLog(pat)
    for i = #LOG, 1, -1 do if string.find(LOG[i], pat, 1, true) then return LOG[i] end end
    return nil
end
local function countEvt(name, argPrefix, from)
    local n = 0
    for i = (from or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " " .. (argPrefix or ""), 1, true) then n = n + 1 end
    end
    return n
end
local function clearLog() LOG = {} end
local function cmdCount(pat)
    local n = 0
    for _, c in ipairs(CMDS) do if string.find(c, pat, 1, true) then n = n + 1 end end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end

NEXTID = 5000
local function mkEntity(name, x, y, z)
    NEXTID = NEXTID + 1
    local e = { class = "NPC", id = NEXTID, px = x or 0, py = y or 0, pz = z or 0, rz = 0, dead = false, hp = 100, inDialog = false }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.GetWorldAngles = function(self) return { x = 0, y = 0, z = self.rz } end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.StartAnimation = function() end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end,
                GetCurrentAnimationState = function() return "MotionIdle" end }
    e.human = { IsWeaponDrawn = function() return false end, IsInDialog = function() return e.inDialog == true end,
                DrawWeapon = function() return true end, HolsterWeapon = function() return true end }
    e.hidden = false
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.actor.StandUp = function() end
    e.soul = { DealDamage = function() end }
    return e
end
mkEntityLate = mkEntity

local function reset()
    KCD2MP.npcPuppets = {}; KCD2MP.npcTracked = {}; KCD2MP.dragging = {}; KCD2MP.dragWatch = {}
    KCD2MP._npcPaused = {}; KCD2MP._npcResumePending = {}; KCD2MP._npcEverPaused = {}
    KCD2MP.npcPuppetRunning = false; KCD2MP._npcPuppetAliveAt = nil; KCD2MP._npcPuppetRetired = {}
    KCD2MP.npcDeathSync = true; KCD2MP._npcDeathSeen = {}; KCD2MP._npcDeathAnnounced = {}
    KCD2MP._npcDeathRemote = {}; KCD2MP._npcDeathDiverged = {}
    KCD2MP.ghosts = {}; KCD2MP.horseGhosts = {}
    KCD2MP.w131.parked = {}; KCD2MP.w131.standins = {}; KCD2MP.w131.active = false; KCD2MP.w131.guard = true
    KCD2MP.w131.reassert = false; KCD2MP.w131.standinLastAt = -1e9
    KCD2MP.wo102.authorityHost = true; KCD2MP.wo102.authorityPause = true; KCD2MP.hitSensorOn = false
    KCD2MP.w122.sharedWorld = true; KCD2MP.w122.ownerDeath = true; KCD2MP.w122.ownerReq = {}
    KCD2MP.w136.hold = false; KCD2MP.w136.souls = {}; KCD2MP.w136.soulAsked = {}
    local w = KCD2MP.w137
    w.talking = {}; w.held = {}; w.deadRefused = {}; w.deadStandinFail = {}; w.sync = true; w.talkOn = true
    ENTS = {}; SPHERE = {}; ERRS = {}; CMDS = {}; SPAWNS = {}; TOASTS = {}; TALKS = {}
    PLAYER_IN_DIALOG = false
end
local function tick()
    NOW = NOW + 0.05
    KCD2MP_NpcPuppetTick(nil, KCD2MP.npcPuppetGen)
end
local function run(seconds, fn)
    local t_end = NOW + seconds
    while NOW < t_end do if fn then fn() end; tick() end
end

-- ================================================================ WO-137

do -- (a) dead is dead
    reset(); clearLog(); NOW = 100
    -- the field case: the Find Mutt bandit, dead on the host and lying dead here too
    local e = mkEntity("hledaniPsa_deadBody", 10, 10, 0); ENTS["hledaniPsa_deadBody"] = e
    e.dead = true
    KCD2MP_ApplyNpcState("hledaniPsa_deadBody", 10, 10, 0, 0, 0, 1, 0, 1, 1000)
    check("a: a corpse's puppet start issues no pause", cmdCount("wh_ai_PauseNPC hledaniPsa_deadBody") == 0, CMDS[1])
    check("a: ...and no detach (the lying pose is its Unstance)", cmdCount("wh_ai_NPCStateResetElement hledaniPsa_deadBody") == 0)
    check("a: the refusal is logged once", logCount("a-corpse-is-never-paused") == 1, lastLog("MP-PAUSE"))
    tick(); tick(); tick()
    check("a: the tick's re-pause gate refuses it too (logged once)", cmdCount("wh_ai_PauseNPC hledaniPsa_deadBody") == 0 and logCount("a-corpse-is-never-paused") == 1)

    -- dead on the host, still alive here (a scripted body whose behaviour has not run, or a late copy)
    local e2 = mkEntity("ttkc_man_2", 20, 20, 0); ENTS["ttkc_man_2"] = e2
    KCD2MP_ApplyNpcState("ttkc_man_2", 21, 20, 0, 0, 0, 1, 0, 1, 1000)
    tick()
    check("a: stream dead, alive here: not paused", cmdCount("wh_ai_PauseNPC ttkc_man_2") == 0)
    check("a: ...and the owner's death is asked (WO-122)", countEvt("npc_owner_dead", "ttkc_man_2") >= 1)

    -- a living copy, paused; the host kills it with no hit (a quest's kill node)
    local e3 = mkEntity("ttkc_man_3", 30, 30, 0); ENTS["ttkc_man_3"] = e3
    KCD2MP_ApplyNpcState("ttkc_man_3", 30, 30, 0, 0, 100, 0, 0, 1, 1000)
    check("a: a living copy is paused at its puppet start", cmdCount("wh_ai_PauseNPC ttkc_man_3") == 1)
    local mark = #LOG
    KCD2MP_ApplyNpcState("ttkc_man_3", 30, 30, 0, 0, 0, 1, 0, 2, 1100)
    tick()
    check("a: the scripted kill on the host: its death is asked here", countEvt("npc_owner_dead", "ttkc_man_3", mark) == 1)
    e3.dead = true   -- the agent's ApplyDeath landed
    tick()
    check("a: the corpse gets its brain back, once", cmdCount("wh_ai_ResumeNPC ttkc_man_3") == 1 and logCount("WO137-DEAD resumed npc=ttkc_man_3") == 1)
    tick(); tick()
    check("a: ...and is never paused again", cmdCount("wh_ai_PauseNPC ttkc_man_3") == 1 and cmdCount("wh_ai_ResumeNPC ttkc_man_3") == 1)

    CMDS = {}
    KCD2MP_NpcDetach("ttkc_man_3", KCD2MP.npcPuppets["ttkc_man_3"], "test")
    check("a: the detach refuses a corpse", cmdCount("wh_ai_NPCStateResetElement") == 0 and logCount("MP-DETACH npc=ttkc_man_3 stance=skipped unstance=skipped result=skipped-dead") == 1)
    local e5 = mkEntity("ttkc_man_5", 0, 5, 0); ENTS["ttkc_man_5"] = e5
    KCD2MP_NpcDetach("ttkc_man_5", {}, "test")
    check("a: a living NPC is still detached (WO-118 unchanged)", cmdCount("wh_ai_NPCStateResetElement ttkc_man_5 Stance") == 1 and cmdCount("wh_ai_NPCStateResetElement ttkc_man_5 Unstance") == 1)
    KCD2MP_NpcDetach("ttkc_man_5", { deadHint = true }, "test")
    check("a: the stream's dead bit alone is enough", cmdCount("wh_ai_NPCStateResetElement ttkc_man_5") == 2)
    noErrs("a")
end

do -- (b) a stand-in for a body the host has dead
    reset(); clearLog(); NOW = 300
    KCD2MP_W131Tick(true, true)
    KCD2MP_W136SoulFor("prepadeni_bandit_7", "29f8bb4d-87f1-465e-9ba8-679f889d4de6", "NPC")
    KCD2MP_ApplyNpcState("prepadeni_bandit_7", 40, 40, 1, 0, 0, 1, 0, 1, 1000)
    local e = ENTS["prepadeni_bandit_7"]
    check("b: a stand-in is spawned for a body the host has dead", e ~= nil and #SPAWNS == 1)
    check("b: ...hidden until it is dead here", e ~= nil and e.hidden == true and logCount("WO137-DEAD standin-hidden npc=prepadeni_bandit_7") == 1)
    check("b: ...never paused", cmdCount("wh_ai_PauseNPC prepadeni_bandit_7") == 0)
    tick()
    check("b: its death is asked", countEvt("npc_owner_dead", "prepadeni_bandit_7") == 1)
    check("b: still hidden while alive here", e.hidden == true)
    e.dead = true; tick()
    check("b: dead here: shown", e.hidden == false and logCount("WO137-DEAD standin-shown npc=prepadeni_bandit_7") == 1, lastLog("WO137-DEAD"))
    tick()
    check("b: shown once", logCount("standin-shown") == 1)

    KCD2MP_W136SoulFor("prepadeni_bandit_8", "29f8bb4d-87f1-465e-9ba8-679f889d4de6", "NPC")
    NOW = NOW + 1
    KCD2MP_ApplyNpcState("prepadeni_bandit_8", 44, 40, 1, 0, 0, 1, 0, 1, 1000)
    local e8 = ENTS["prepadeni_bandit_8"]
    check("b: a second one spawns hidden", e8 ~= nil and e8.hidden == true)
    run(16, function() KCD2MP_W131Tick(true, true); KCD2MP_ApplyNpcState("prepadeni_bandit_8", 44, 40, 1, 0, 0, 1, 0, nil, nil) end)
    check("b: never dead here in 15 s: removed, never shown", ENTS["prepadeni_bandit_8"] == nil and e8.hidden == true
        and logCount("WO137-DEAD standin-removed npc=prepadeni_bandit_8") == 1)
    local spawns = #SPAWNS
    run(2, function() KCD2MP_W131Tick(true, true); KCD2MP_ApplyNpcState("prepadeni_bandit_8", 44, 40, 1, 0, 0, 1, 0, nil, nil) end)
    check("b: no new try inside 120 s", #SPAWNS == spawns)

    KCD2MP.w122.ownerDeath = false
    KCD2MP_W136SoulFor("prepadeni_bandit_9", "29f8bb4d-87f1-465e-9ba8-679f889d4de6", "NPC")
    NOW = NOW + 1
    KCD2MP_ApplyNpcState("prepadeni_bandit_9", 48, 40, 1, 0, 0, 1, 0, 1, 1000)
    check("b: mp_owner_death off: no dead stand-in (nothing could kill it)", ENTS["prepadeni_bandit_9"] == nil)
    KCD2MP.w122.ownerDeath = true
    NOW = NOW + 1
    KCD2MP_W136SoulFor("prepadeni_bandit_10", "29f8bb4d-87f1-465e-9ba8-679f889d4de6", "NPC")
    KCD2MP_ApplyNpcState("prepadeni_bandit_10", 52, 40, 1, 0, 100, 0, 0, 1, 1000)
    local e10 = ENTS["prepadeni_bandit_10"]
    check("b: a living one is shown and paused as before (WO-131)", e10 ~= nil and e10.hidden == false and cmdCount("wh_ai_PauseNPC prepadeni_bandit_10") == 1)
    noErrs("b")
end

do -- (c) talking (joiner)
    reset(); clearLog(); NOW = 500
    KCD2MP_W131Tick(true, true)
    KCD2MP_W137Session(false, true, true)
    check("c: the talk and chat actions are wrapped", BasicAIActions.OnTalk ~= ORIG_ONTALK and logCount("WO137-TALK wrapped BasicAIActions.OnTalk") == 1
        and logCount("WO137-TALK wrapped BasicAIActions.OnChat t=") == 1,
        tostring(BasicAIActions.OnTalk ~= ORIG_ONTALK) .. "/" .. logCount("WO137-TALK wrapped BasicAIActions.OnTalk") .. "/" .. logCount("WO137-TALK wrapped BasicAIActions.OnChat t=") .. "/" .. tostring(lastLog("WO137-TALK")))
    KCD2MP_W137Session(false, true, true)
    check("c: wrapped once", logCount("WO137-TALK wrapped BasicAIActions.OnTalk") == 1)
    local e = mkEntity("tzel_olbram", 1, 1, 0); ENTS["tzel_olbram"] = e
    KCD2MP_ApplyNpcState("tzel_olbram", 1, 1, 0, 0, 100, 0, 0, 1, 1000)
    check("c: the copy is paused (a puppet)", cmdCount("wh_ai_PauseNPC tzel_olbram") == 1)
    local mark = #LOG
    BasicAIActions.OnTalk(e, player, 0)
    check("c: Talk on a copy: its brain back for the conversation", cmdCount("wh_ai_ResumeNPC tzel_olbram") == 1 and KCD2MP._npcPaused["tzel_olbram"] == nil)
    -- WO-166 T3 (mp_talk_resume_first, default on): a press on a paused copy goes on 200 ms after the resume
    local due = TIMERS; TIMERS = {}
    for _, t in ipairs(due) do if t.ms == 200 then t.f() end end
    check("c: ...the game's own OnTalk still runs (the request)", TALKS[#TALKS] == "OnTalk:tzel_olbram", TALKS[#TALKS])
    -- WO-144 1.3: the key press holds nothing on the host; the conversation's start does
    check("c: ...the host is not told yet (WO-144: only once it starts)", countEvt("w137_talk", "on tzel_olbram", mark) == 0)
    tick(); tick(); tick()
    check("c: the puppet tick does not pause it again mid-conversation", cmdCount("wh_ai_PauseNPC tzel_olbram") == 1)
    BasicAIActions.OnTalk(e, player, 0)
    check("c: a second Talk: no second resume, no notice", cmdCount("wh_ai_ResumeNPC tzel_olbram") == 1 and countEvt("w137_talk", "on tzel_olbram", mark) == 0)
    KCD2MP_W137TalkRequest(241)
    check("c: the request id is attached", KCD2MP.w137.talking["tzel_olbram"].id == 241)
    KCD2MP_W137TalkAttempt(241, "Dude tzel_olbram")
    check("c: the start is seen", KCD2MP.w137.talking["tzel_olbram"].started == true and logCount("WO137-TALK start npc=tzel_olbram id=241") == 1)
    -- WO-144 1.3: an attempt is not a start (the engine can still cancel it): told once this player is in it
    check("c: ...the host is not told on the attempt alone", countEvt("w137_talk", "on tzel_olbram", mark) == 0)
    PLAYER_IN_DIALOG = true
    KCD2MP_W137Session(false, true, true)
    check("c: ...and the host is told once the player is in the conversation, once", countEvt("w137_talk", "on tzel_olbram", mark) == 1)
    KCD2MP_W137TalkAttempt(241, "Dude tzel_olbram")
    KCD2MP_W137Session(false, true, true)
    check("c: ...a repeated start line: no second notice", countEvt("w137_talk", "on tzel_olbram", mark) == 1)
    NOW = NOW + 60; KCD2MP_W137Session(false, true, true)
    check("c: a long conversation is not cut while the player is in it", KCD2MP.w137.talking["tzel_olbram"] ~= nil)
    KCD2MP_W137DialogEnd(17, "tzel_farmhand_2 tzel_olbram")
    check("c: a conversation between NPCs ending does not end this player's", KCD2MP.w137.talking["tzel_olbram"] ~= nil)
    KCD2MP_W137DialogEnd(241, "Dude tzel_olbram")
    PLAYER_IN_DIALOG = false
    -- WO-144 3.2: an end waits endDeferS (a minigame may follow the conversation)
    check("c: the end waits a moment (WO-144: a minigame may follow)", KCD2MP.w137.talking["tzel_olbram"] ~= nil and cmdCount("wh_ai_PauseNPC tzel_olbram") == 1)
    NOW = NOW + 3; KCD2MP_W137Session(false, true, true)
    check("c: the end: paused again", cmdCount("wh_ai_PauseNPC tzel_olbram") == 2 and KCD2MP._npcPaused["tzel_olbram"] ~= nil)
    check("c: ...the host is told", countEvt("w137_talk", "off tzel_olbram", mark) == 1)
    check("c: ...logged", logCount("WO137-TALK end npc=tzel_olbram id=241 why=dialog-ended") == 1, lastLog("WO137-TALK end"))

    -- never started (the engine dropped the request)
    NOW = NOW + 3.5   -- WO-164 T2: a press under 3 s after a talk to the same copy ended is not passed on
    local markNs = #LOG
    BasicAIActions.OnTalk(e, player, 0)
    NOW = NOW + 10; KCD2MP_W137Session(false, true, true)
    check("c: not started after 10 s: still waiting", KCD2MP.w137.talking["tzel_olbram"] ~= nil)
    NOW = NOW + 16; KCD2MP_W137Session(false, true, true)
    check("c: never started in 25 s: paused again", logCount("why=never-started") == 1 and cmdCount("wh_ai_PauseNPC tzel_olbram") == 3)
    check("c: ...and the host's NPC was never held (WO-144: no on, no off)", countEvt("w137_talk", "on tzel_olbram", markNs) == 0
        and countEvt("w137_talk", "off tzel_olbram", markNs) == 0)

    -- started, then the player is out of the dialogue (the end line was missed)
    BasicAIActions.OnTalk(e, player, 0)
    KCD2MP_W137TalkAttempt(300, "Dude tzel_olbram")
    NOW = NOW + 1; KCD2MP_W137Session(false, true, true)
    NOW = NOW + 1; KCD2MP_W137Session(false, true, true)
    check("c: 1 s out of dialogue: not yet", KCD2MP.w137.talking["tzel_olbram"] ~= nil)
    NOW = NOW + 3; KCD2MP_W137Session(false, true, true)
    check("c: out of dialogue for 3 s after the start: over", KCD2MP.w137.talking["tzel_olbram"] == nil and logCount("why=player-out-of-dialogue") == 1)

    -- the gates
    CMDS = {}
    KCD2MP_SetQuestSync("off")
    BasicAIActions.OnTalk(e, player, 0)
    check("c: mp_quest_sync off: the copy stays paused (the old behaviour)", cmdCount("wh_ai_ResumeNPC") == 0)
    KCD2MP_SetQuestSync("on")
    KCD2MP_W137Session(false, true, false)
    BasicAIActions.OnTalk(e, player, 0)
    check("c: the mirror not running: untouched", cmdCount("wh_ai_ResumeNPC") == 0)
    KCD2MP_W137Session(false, true, true)
    KCD2MP_SetQuestTalk("off")
    BasicAIActions.OnTalk(e, player, 0)
    check("c: mp_quest_talk off: untouched", cmdCount("wh_ai_ResumeNPC") == 0)
    KCD2MP_SetQuestTalk("on")
    local own = mkEntity("tzel_own", 2, 2, 0); ENTS["tzel_own"] = own
    BasicAIActions.OnTalk(own, player, 0)
    check("c: an NPC that is no host copy: untouched", cmdCount("wh_ai_ResumeNPC") == 0 and KCD2MP.w137.talking["tzel_own"] == nil)
    BasicAIActions.OnTalk(e, { id = 77 }, 0)
    check("c: not this player: untouched", cmdCount("wh_ai_ResumeNPC") == 0)
    check("c: the game's own function ran every time", #TALKS >= 5)

    -- the request fallback (a conversation asked another way)
    NOW = NOW + 3   -- WO-164: not right after a talk to this game's own NPC (that request is that NPC's)
    CMDS = {}
    KCD2MP_W137TalkRequest(400)
    check("c: a request by another path: the copy in reach is freed", cmdCount("wh_ai_ResumeNPC tzel_olbram") == 1
        and KCD2MP.w137.talking["tzel_olbram"] ~= nil and KCD2MP.w137.talking["tzel_olbram"].id == 400)
    KCD2MP_W137DialogEnd(400, "Dude tzel_olbram")
    NOW = NOW + 3; KCD2MP_W137Session(false, true, true)   -- WO-144: past the deferred end

    -- a conversation the game forces on the (still paused) copy -- WO-137's own path (WO-151 3.4's
    -- mp_scene_guard resumes the copy for it instead: Test-WO151Synthetic.lua (i))
    local sceneGuardWas = KCD2MP.w151 and KCD2MP.w151.sceneGuard
    if KCD2MP.w151 then KCD2MP.w151.sceneGuard = false end
    mark = #LOG; CMDS = {}
    KCD2MP_W137TalkAttempt(500, "Dude tzel_olbram")
    PLAYER_IN_DIALOG = true; KCD2MP_W137Session(false, true, true); PLAYER_IN_DIALOG = false   -- WO-144 1.3: told once the player is in it
    local t = KCD2MP.w137.talking["tzel_olbram"]
    check("c: a forced conversation: tracked, the host told, no resume", t ~= nil and t.forced == true
        and countEvt("w137_talk", "on tzel_olbram", mark) == 1 and cmdCount("wh_ai_ResumeNPC") == 0)
    KCD2MP_W137DialogEnd(500, "Dude tzel_olbram")
    NOW = NOW + 3; KCD2MP_W137Session(false, true, true)   -- WO-144: past the deferred end
    check("c: ...its end: nothing to pause again (it stayed paused)", cmdCount("wh_ai_PauseNPC") == 0 and countEvt("w137_talk", "off tzel_olbram", mark) == 1)
    if KCD2MP.w151 then KCD2MP.w151.sceneGuard = sceneGuardWas end

    -- the host's stream stops mid-conversation: never parked until it ends
    NOW = NOW + 3.5   -- WO-164 T2: past the one-attempt-at-a-time window after the last talk with this copy
    BasicAIActions.OnTalk(e, player, 0)
    KCD2MP_W131Tick(true, true)
    local deferred = KCD2MP_W131ParkReleased("tzel_olbram", "silence")
    check("c: never parked mid-conversation", deferred == true and e.hidden == false and KCD2MP.w137.talking["tzel_olbram"].parkAfter == "silence")
    KCD2MP.npcPuppets["tzel_olbram"] = nil
    CMDS = {}
    KCD2MP_W137DialogEnd(nil, "Dude tzel_olbram")
    check("c: ...its end parks it (paused + hidden)", e.hidden == true and cmdCount("wh_ai_PauseNPC tzel_olbram") == 1)

    -- the agent goes silent mid-conversation
    local e2 = mkEntity("tzel_maid", 1, -1, 0); ENTS["tzel_maid"] = e2
    KCD2MP_ApplyNpcState("tzel_maid", 1, -1, 0, 0, 100, 0, 0, 1, 1000)
    KCD2MP_W137Session(false, true, true)
    BasicAIActions.OnChat(e2, player, 0)
    check("c: a chat frees the copy too", KCD2MP.w137.talking["tzel_maid"] ~= nil and KCD2MP.w137.talking["tzel_maid"].via == "OnChat")
    NOW = NOW + 11; CMDS = {}
    KCD2MP_W137Backstop()
    check("c: the agent went silent: the copy is paused again", KCD2MP.w137.talking["tzel_maid"] == nil and cmdCount("wh_ai_PauseNPC tzel_maid") == 1
        and logCount("WO137-TALK end npc=tzel_maid") == 1)
    noErrs("c")
end

do -- (d) the host's hold
    -- WO-151 3.6: a hold only refuses a second conversation by default (block-only, Test-WO151Synthetic.lua);
    -- this is mp_hold_freeze on, the WO-137 freeze, whose rules must still hold.
    reset(); clearLog(); NOW = 900
    KCD2MP.w151.holdFreeze = true
    KCD2MP.hitSensorOn = true
    KCD2MP_W137Session(true, false, true)
    local e = mkEntity("tzel_olbram", 1, 1, 0); ENTS["tzel_olbram"] = e
    KCD2MP_W137HostHold(true, "tzel_olbram", 1, "talk")
    check("d: the host's NPC is held while the partner talks to it", cmdCount("wh_ai_PauseNPC tzel_olbram") == 1
        and logCount("WO137-HOLD on npc=tzel_olbram peer=1 why=talk exec=ok") == 1, lastLog("WO137-HOLD"))
    NOW = NOW + 11; KCD2MP_W137Session(true, false, true)
    check("d: re-issued every 10 s (a load forgets it)", cmdCount("wh_ai_PauseNPC tzel_olbram") == 2)
    KCD2MP_W137HostHold(false, "tzel_olbram", 1, "talk")
    check("d: released at the end", cmdCount("wh_ai_ResumeNPC tzel_olbram") == 1 and logCount("WO137-HOLD off npc=tzel_olbram") == 1)
    check("d: releasing twice is a no-op", KCD2MP_W137HostHold(false, "tzel_olbram", 1, "talk") == false)
    e.inDialog = true; CMDS = {}
    KCD2MP_W137HostHold(true, "tzel_olbram", 1, "talk")
    check("d: an NPC in the host's own conversation is not frozen in it", cmdCount("wh_ai_PauseNPC") == 0 and logCount("exec=waits:in-a-conversation-here") == 1)
    e.inDialog = false
    NOW = NOW + 1; KCD2MP_W137Session(true, false, true)
    check("d: ...held once that conversation is over", cmdCount("wh_ai_PauseNPC tzel_olbram") == 1)
    NOW = NOW + 301; KCD2MP_W137Session(true, false, true)
    check("d: released after 5 min whatever happens", KCD2MP.w137.held["tzel_olbram"] == nil and logCount("why=max-time") == 1)
    check("d: no such NPC: refused", KCD2MP_W137HostHold(true, "nobody_here", 1, "talk") == false and logCount("WO137-HOLD refused npc=nobody_here") == 1)
    check("d: a bad name: refused", KCD2MP_W137HostHold(true, "bad name;", 1, "talk") == false)
    KCD2MP_W137HostHold(true, "tzel_olbram", 1, "talk")
    KCD2MP.w123.paused = true; KCD2MP.w123.npcs = { tzel_olbram = true }
    CMDS = {}
    KCD2MP_W137HostHold(false, "tzel_olbram", 1, "talk")
    check("d: a join paused the world meanwhile: its resume wakes it", cmdCount("wh_ai_ResumeNPC") == 0 and logCount("exec=left-to-the-join-pause") == 1)
    KCD2MP.w123.paused = false; KCD2MP.w123.npcs = {}
    KCD2MP_W137HostHold(true, "tzel_olbram", 1, "talk")
    NOW = NOW + 11; CMDS = {}
    KCD2MP_W137Backstop()
    check("d: the agent went silent: every hold given back", KCD2MP.w137.held["tzel_olbram"] == nil and cmdCount("wh_ai_ResumeNPC tzel_olbram") == 1)
    KCD2MP_W137Session(true, false, true)
    KCD2MP_W137HostHold(true, "tzel_olbram", 1, "talk")
    CMDS = {}
    KCD2MP_W137Session(false, false, false)
    check("d: no longer the host: released", KCD2MP.w137.held["tzel_olbram"] == nil and cmdCount("wh_ai_ResumeNPC tzel_olbram") == 1)
    KCD2MP.hitSensorOn = false
    KCD2MP.w151.holdFreeze = false
    noErrs("d")
end

do -- (e) the kill switch, the commands
    reset(); clearLog(); NOW = 1500
    check("e: mp_quest_sync / mp_quest_talk / mp_quest_status registered",
        CCMDS["mp_quest_sync"] ~= nil and CCMDS["mp_quest_sync"].body == "KCD2MP_SetQuestSync(%line)"
        and CCMDS["mp_quest_talk"] ~= nil and CCMDS["mp_quest_talk"].body == "KCD2MP_SetQuestTalk(%line)"
        and CCMDS["mp_quest_status"] ~= nil and CCMDS["mp_quest_status"].body == "KCD2MP_QuestStatusAll()",
        tostring(CCMDS["mp_quest_sync"] and CCMDS["mp_quest_sync"].body) .. "|" .. tostring(CCMDS["mp_quest_talk"] and CCMDS["mp_quest_talk"].body) .. "|" .. tostring(CCMDS["mp_quest_status"] and CCMDS["mp_quest_status"].body) .. "|" .. tostring(CCMDS["mp_w136_check"] and CCMDS["mp_w136_check"].body))
    check("e: both ship ON", KCD2MP.w137.sync == true and KCD2MP.w137.talkOn == true)
    KCD2MP_SetQuestSync("off")
    check("e: off: the agent is told at once", countEvt("w137_sync", "off") == 1 and logCount("WO137-SYNC off") == 1 and KCD2MP.w137.sync == false)
    check("e: a bad value is refused", KCD2MP_SetQuestSync("maybe") == false and KCD2MP.w137.sync == false)
    KCD2MP_SetQuestSync("on")
    check("e: on again", countEvt("w137_sync", "on") == 1 and KCD2MP.w137.sync == true)
    KCD2MP_SetQuestSync("%line")
    check("e: bare = a report", logCount("WO137-SYNC on") == 2 and KCD2MP.w137.sync == true)
    KCD2MP_QuestStatusAll()
    check("e: mp_quest_status: the shared quests' line, the agent's asked for, then WO-94's", logCount("WO137-STATUS sync=on talk=on") == 1
        and countEvt("w137_status") == 1 and logCount("QUEST sync is ") == 1)
    KCD2MP.w137.aliveAt = nil; clearLog()
    KCD2MP_W137Session(false, false, false)
    check("e: the agent (re)connecting learns this toggle", countEvt("w137_sync", "on") == 1)
    NOW = NOW + 1; KCD2MP_W137Session(false, false, false)
    check("e: ...once", countEvt("w137_sync") == 1)
    NOW = NOW + 20; KCD2MP_W137Session(false, false, false)
    check("e: ...and again after the agent was silent", countEvt("w137_sync") == 2)
    noErrs("e")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
