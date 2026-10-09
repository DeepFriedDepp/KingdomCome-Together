-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-164 synthetic test (the game-side half), against the real kdcmp.lua under MoonSharp.
--
--   (S) the sweep: a host copy only, the release + the agent's event with its hold, the 20 s cooldown, the switch
--   (K) after a skip: every copy within 300 m queued nearest first, 4 per tick
--   (F) the focus pre-warm: the copy this player faces within 4 m, not one behind him
--   (D) one talk attempt at a time: a press under 3 s after the same copy's talk ended is not passed on
--   (P) a copy freed for a talk drops its own unfinished requests first (RestrictDialog on, then off)
--   (R) a request open 4 s: cancelled, swept, asked once more through the same action -- never twice; "Try again" after
--   (L) the flee limiter: none while the host's NPC flees, 20 s of rest after 3 re-asserts in 10 s
--   (E) a joiner's own random events off in a session, back after it
--   (U) unstuck: step 1 resets this player's seat, a second press within 10 s asks for step 2
--   (M) the mark snapshot: at most 40 lines, game facts only, one end line
--
-- Driven by Test-WO164Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-164-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
CVARS = { wh_pl_RandomEventsAutoSpawnEnabled = "1" }
System.GetCVarValue = function(n) return CVARS[n] or "0" end
System.GetEntityByName = function(n) return ENTS[n] end
SPHERE = {}
System.GetEntitiesInSphere = function() return SPHERE end
System.GetTerrainElevation = function(p) return 0 end
System.GetFrameTime = function() return 1 / 60 end
System.ExecuteCommand = function(s)
    CMDS[#CMDS + 1] = tostring(s)
    local n, v = tostring(s):match("^(wh_pl_RandomEventsAutoSpawnEnabled) (%S+)$")
    if n then CVARS[n] = v end
end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
Game = mkstub()
Calendar = mkstub()
Calendar.GetWorldTime = function() return 1000000 end
XGenAIModule = mkstub()

ORIG_TALKS = 0
BasicAIActions = { OnTalk = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end }
RESTRICT = {}
PLAYER_RESTRICTED = false
player = { id = 1,
    soul = {
        RestrictDialog = function(self, on) RESTRICT[#RESTRICT + 1] = "player " .. tostring(on); PLAYER_RESTRICTED = on end,
        IsDialogRestricted = function(self) return PLAYER_RESTRICTED end,
        GetState = function() return 0 end,
    },
    human = { IsInDialog = function() return false end },
    actor = { StandUp = function() end },
}
PX, PY = 0, 0
DIRX, DIRY = 1, 0
player.GetWorldPos = function() return { x = PX, y = PY, z = 0 } end
player.GetDirectionVector = function() return { x = DIRX, y = DIRY, z = 0 } end
player.GetName = function() return "Dude" end

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
local function cmdCount(prefix)
    local n = 0
    for _, c in ipairs(CMDS) do if c:sub(1, #prefix) == prefix then n = n + 1 end end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end

local function copy(name, x, y)
    local e = { id = #ENTS + 100, GetName = function() return name end, actor = { IsDead = function() return false end },
                human = { IsInDialog = function() return false end, IsWeaponDrawn = function() return false end },
                soul = { RestrictDialog = function(self, on) RESTRICT[#RESTRICT + 1] = name .. " " .. tostring(on) end } }
    e.GetWorldPos = function() return { x = x, y = y, z = 0 } end
    ENTS[name] = e
    KCD2MP.npcPuppets[name] = {}
    return e
end
local function joiner(on)
    KCD2MP.w137.joiner, KCD2MP.w137.active, KCD2MP.w137.host, KCD2MP.w137.sync, KCD2MP.w137.talkOn = on, on, false, true, true
end
local function frame(dt)
    NOW = NOW + (dt or 0.3)
    KCD2MP_W164Frame()
end

-- (S) the sweep -----------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    joiner(true)
    copy("ttac_blacksmith", 50, 0)
    CMDS = {}
    local mark = #LOG
    check("S1: a host copy is swept: released (Unstance, Stance) and the agent told with its hold", KCD2MP_W164Sweep("ttac_blacksmith", "adaptive") == true
        and cmdCount("wh_ai_NPCStateResetElement ttac_blacksmith Unstance") == 1 and cmdCount("wh_ai_NPCStateResetElement ttac_blacksmith Stance") == 1
        and countEvt("w164_sweep", "ttac_blacksmith adaptive 120", mark) == 1, CMDS[1])
    check("S2: ... logged", lastLog("WO164-SWEEP-LUA npc=ttac_blacksmith trigger=adaptive released=true hold_s=120", mark) ~= nil)
    check("S3: not again inside 20 s", KCD2MP_W164Sweep("ttac_blacksmith", "adaptive") == false)
    NOW = NOW + 21
    check("S4: ... and again after", KCD2MP_W164Sweep("ttac_blacksmith", "focus") == true)
    check("S5: never a body that is not a host copy", KCD2MP_W164Sweep("ttac_stranger", "adaptive") == false)
    KCD2MP_W164SetSweep("off")
    NOW = NOW + 21
    check("S6: mp_talk_sweep off: nothing", KCD2MP_W164Sweep("ttac_blacksmith", "adaptive") == false)
    KCD2MP_W164SetSweep("on")
    check("S7: the switches are registered", CCMDS["mp_talk_sweep"] ~= nil and CCMDS["mp_talk_guard"] ~= nil and CCMDS["mp_flee_limit"] ~= nil
        and CCMDS["mp_joiner_events"] ~= nil and CCMDS["mp_w164_status"] ~= nil and CCMDS["mp_w164_sweep"].body == "KCD2MP_W164SweepCmd(%line)")
    CMDS = {}
    NOW = NOW + 21
    check("S8: the console form takes the quoted %line", KCD2MP_W164SweepCmd('"ttac_blacksmith"') == true and cmdCount("wh_ai_NPCStateResetElement ttac_blacksmith") == 2)
    noErrs("S")
end

-- (K) after a skip ---------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    for i = 1, 9 do copy("tk_" .. i, 10 * i, 0) end
    copy("tk_far", 400, 0)
    NOW = NOW + 30
    local mark = #LOG
    local n = KCD2MP_W164AfterSkip("the host's announced skip was applied")
    check("K1: every copy within 300 m queued (the far one not)", n >= 10 and countLog("tk_far", mark) == 0, n)
    frame(0.3)
    check("K2: 4 per tick", countEvt("w164_sweep", nil, mark) == 4, countEvt("w164_sweep", nil, mark))
    check("K3: nearest first", countEvt("w164_sweep", "tk_1 skip 0", mark) == 1)
    frame(0.3); frame(0.3)
    check("K4: the queue drains", #KCD2MP.w164.queue == 0 and lastLog("WO164-SWEEP after-skip: the queue is done", mark) ~= nil)
    check("K5: a hitch tick waits (a long frame gap)", (function()
        KCD2MP_W164AfterSkip("x"); local m2 = #LOG; frame(2.0); return countEvt("w164_sweep", nil, m2) == 0 end)())
    KCD2MP.w164.queue = {}
    for i = 1, 9 do ENTS["tk_" .. i] = nil; KCD2MP.npcPuppets["tk_" .. i] = nil end
    ENTS["tk_far"] = nil; KCD2MP.npcPuppets["tk_far"] = nil
    noErrs("K")
end

-- (F) the focus pre-warm -----------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    copy("tf_front", 3, 0)
    copy("tf_behind", -2, 0)
    NOW = NOW + 30
    local mark = #LOG
    frame(0.6)
    check("F1: the copy faced within 4 m is swept (focus, 20 s hold)", countEvt("w164_sweep", "tf_front focus 20", mark) == 1)
    check("F2: not the one behind", countEvt("w164_sweep", "tf_behind", mark) == 0)
    ENTS["tf_front"] = nil; KCD2MP.npcPuppets["tf_front"] = nil; ENTS["tf_behind"] = nil; KCD2MP.npcPuppets["tf_behind"] = nil
    noErrs("F")
end

-- (D) one attempt at a time, (P) the copy's own requests, (R) the retry ------------------------------------------------------------
do
    ERRS = {}
    local e = copy("tvid_huntsman", 1, 0)
    PX, PY = 0, 0
    KCD2MP_W137Session(false, true, true)   -- installs the talk wraps
    local presses = 0
    local origWas = KCD2MP.w137.talkWraps and KCD2MP.w137.talkWraps.OnTalk
    check("D0: the talk wraps are installed", origWas ~= nil)
    RESTRICT = {}
    local mark = #LOG
    BasicAIActions.OnTalk(e, player, 0)
    check("P1: the copy's own requests dropped first (its soul: restrict on, then off)", RESTRICT[1] == "tvid_huntsman true" and RESTRICT[2] == "tvid_huntsman false", RESTRICT[1])
    check("P2: ... logged", lastLog("WO164-TALK preclear npc=tvid_huntsman soul_requests=deleted", mark) ~= nil)
    check("T0: the agent is told of the ask", countEvt("w164_talk", "ask tvid_huntsman OnTalk", mark) == 1)
    KCD2MP_W137TalkRequest(90)
    check("R0: the request's id is kept", KCD2MP.w137.talking["tvid_huntsman"].id == 90)
    frame(3.0)
    check("R1: not retried before 4 s", countEvt("w164_talk", "retry", mark) == 0)
    RESTRICT = {}
    frame(1.5)
    check("R2: open 4 s: retried once -- the player's request cancelled (restrict on/off), the copy swept, the press asked again",
        countEvt("w164_talk", "retry tvid_huntsman cancel=ok reissue=asked", mark) == 1 and RESTRICT[1] == "player true" and RESTRICT[2] == "player false"
        and countEvt("w164_sweep", "tvid_huntsman retry 30", mark) == 1, RESTRICT[1])
    check("R3: the old id no longer ends the talk (the engine's cancel line)", KCD2MP.w137.talking["tvid_huntsman"].id == nil and KCD2MP_W137TalkDropped(90) == false)
    local tm = TIMERS[#TIMERS]
    check("R4: the re-issue is the same game action, 300 ms later", tm ~= nil and tm.ms == 300)
    if tm then tm.f() end
    check("R5: ... and it ran", lastLog("WO164-TALK retry npc=tvid_huntsman reissue=ok", mark) ~= nil)
    frame(6.0)
    check("R6: never a second retry", countEvt("w164_talk", "retry", mark) == 1)
    NOW = NOW + 30; KCD2MP_W137Session(false, true, true)
    check("R7: still nothing: ended never-started, the agent told, the player told to try again",
        countEvt("w164_talk", "end tvid_huntsman never-started", mark) == 1 and TOASTS[#TOASTS] == "Try again in a moment.", TOASTS[#TOASTS])
    local m2 = #LOG
    BasicAIActions.OnTalk(e, player, 0)
    check("D1: a press under 3 s after: not passed on, told once", countEvt("w164_talk", "ask", m2) == 0 and TOASTS[#TOASTS] == "Wait a moment...", TOASTS[#TOASTS])
    NOW = NOW + 3.5
    BasicAIActions.OnTalk(e, player, 0)
    check("D2: after 3 s: a new attempt", countEvt("w164_talk", "ask tvid_huntsman", m2) == 1)
    KCD2MP_W137TalkEndAll("test")
    KCD2MP_W164SetTalkGuard("off")
    NOW = NOW + 0.5
    BasicAIActions.OnTalk(e, player, 0)
    check("D3: mp_talk_guard off: no window", countEvt("w164_talk", "ask tvid_huntsman", m2) == 2)
    KCD2MP_W137TalkEndAll("test")
    KCD2MP_W164SetTalkGuard("on")
    ENTS["tvid_huntsman"] = nil; KCD2MP.npcPuppets["tvid_huntsman"] = nil
    noErrs("D/P/R")
end

-- (L) the flee limiter -----------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    local mark = #LOG
    check("L1: re-asserts pass at first", KCD2MP_W164ReassertBlocked("tbuk_man_3", NOW) == false)
    KCD2MP_W164ReassertNote("tbuk_man_3", NOW)
    check("L2: not within 2 s", KCD2MP_W164ReassertBlocked("tbuk_man_3", NOW + 1.0) == true)
    KCD2MP_W164ReassertNote("tbuk_man_3", NOW + 2.1)
    KCD2MP_W164ReassertNote("tbuk_man_3", NOW + 4.2)
    check("L3: the third in 10 s: 20 s of rest, logged", KCD2MP_W164ReassertBlocked("tbuk_man_3", NOW + 10) == true
        and lastLog("WO164-FLEE npc=tbuk_man_3 backoff 20 s", mark) ~= nil)
    check("L4: ... over after 20 s", KCD2MP_W164ReassertBlocked("tbuk_man_3", NOW + 25) == false)
    KCD2MP_W164Flee("tbuk_man_5", true)
    check("L5: none while the host's NPC flees", KCD2MP_W164ReassertBlocked("tbuk_man_5", NOW) == true)
    KCD2MP_W164Flee("tbuk_man_5", false)
    check("L6: ... and again after", KCD2MP_W164ReassertBlocked("tbuk_man_5", NOW) == false)
    KCD2MP_W164SetFleeLimit("off")
    KCD2MP_W164Flee("tbuk_man_5", true)
    check("L7: mp_flee_limit off: no limiter", KCD2MP_W164ReassertBlocked("tbuk_man_5", NOW) == false)
    KCD2MP_W164SetFleeLimit("on"); KCD2MP_W164Flee("tbuk_man_5", false)
    noErrs("L")
end

-- (E) random events ---------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    local mark = #LOG
    joiner(true)
    KCD2MP.w164.reOff = nil
    CVARS.wh_pl_RandomEventsAutoSpawnEnabled = "1"
    frame(0.3)
    check("E1: a joiner in a session: this game's own random events off", CVARS.wh_pl_RandomEventsAutoSpawnEnabled == "0"
        and lastLog("WO164-EVENTS joiner: this game's own random events OFF", mark) ~= nil)
    joiner(false)
    frame(0.3)
    check("E2: the session over: back to what it was", CVARS.wh_pl_RandomEventsAutoSpawnEnabled == "1" and lastLog("random events back", mark) ~= nil)
    KCD2MP.w137.host, KCD2MP.w137.active = true, true
    frame(0.3)
    check("E3: never on the host", CVARS.wh_pl_RandomEventsAutoSpawnEnabled == "1")
    KCD2MP.w137.host, KCD2MP.w137.active = false, false
    KCD2MP_W164SetJoinerEvents("off"); joiner(true); frame(0.3)
    check("E4: mp_joiner_events off: a joiner keeps its own", CVARS.wh_pl_RandomEventsAutoSpawnEnabled == "1")
    joiner(false); frame(0.3); KCD2MP_W164SetJoinerEvents("on")
    noErrs("E")
end

-- (U) unstuck ----------------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    CMDS = {}
    local mark = #LOG
    NOW = NOW + 60
    KCD2MP_W151Unstuck("")
    check("U1: step 1 resets this player's seat (Unstance, Stance) and frees as before", cmdCount("wh_ai_NPCStateResetElement Dude Unstance") == 1
        and cmdCount("wh_ai_NPCStateResetElement Dude Stance") == 1 and lastLog("WO164-UNSTUCK step=1 stood=asked moved=no", mark) ~= nil
        and lastLog("WO151-UNSTUCK soft:", mark) ~= nil)
    NOW = NOW + 5
    KCD2MP_W151Unstuck("")
    check("U2: again within 10 s: step 2 asks the agent to put him beside his partner", countEvt("w164_unstuck2", "", mark) == 1
        and lastLog("WO164-UNSTUCK step=2", mark) ~= nil and lastLog("beside-partner", mark) ~= nil)
    NOW = NOW + 5
    KCD2MP_W151Unstuck("")
    check("U3: a third press starts again at step 1", countEvt("w164_unstuck2", "", mark) == 1)
    noErrs("U")
end

-- (M) the mark snapshot ------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    joiner(true)
    copy("tm_near", 5, 5)
    KCD2MP.w137.talking["tm_near"] = { since = NOW - 3, id = 7 }
    SPHERE = { { GetName = function() return "dummyWanderer_horseRider_1" end }, { GetName = function() return "ttac_man_2" end } }
    KCD2MP_ShowNativeToast("This person can't talk to you right now.")
    local mark = #LOG
    local n = KCD2MP_W164MarkSnap("ab12cd34ef", "local")
    local lines = countLog("MP-MARK-SNAP mark=ab12cd34ef lua ", mark)
    check("M1: a block of at most 40 lines plus its end line", n >= 6 and n <= 40 and lines == n + 1, n)
    check("M2: it carries the talk, the copy nearby, the event actor, the toast", lastLog("talk npc=tm_near", mark) ~= nil and lastLog("copy npc=tm_near", mark) ~= nil
        and lastLog("event_actor dummyWanderer_horseRider_1 owner=this-game", mark) ~= nil and lastLog("toast", mark) ~= nil)
    local bad = 0
    for i = mark + 1, #LOG do if LOG[i]:find(":\\", 1, true) or LOG[i]:find("@", 1, true) then bad = bad + 1 end end
    check("M3: no path, no address", bad == 0)
    check("M4: the id is cleaned", (function() local m2 = #LOG; KCD2MP_W164MarkSnap("x/../y z", "peer0"); return lastLog("mark=xyz lua", m2) ~= nil end)())
    KCD2MP.w137.talking["tm_near"] = nil; ENTS["tm_near"] = nil; KCD2MP.npcPuppets["tm_near"] = nil
    KCD2MP_W164Status()
    check("M5: the status line", lastLog("WO164-STATUS sweep=true talk_guard=true") ~= nil)
    KCD2MP_W164Notice("Back with your host")
    check("N1: a notice is the game's own HUD line", TOASTS[#TOASTS] == "Back with your host" and lastLog("WO164-NOTICE Back with your host") ~= nil)
    noErrs("M")
end

OUT = table.concat(RESULTS, "\n")
