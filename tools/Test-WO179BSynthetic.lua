-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-179B synthetic test (the shared video's Lua half: the switches, the copy's hold and its skip check, a move that is no input,
-- the length fallback, the ignore window's animals, the host's protection of a watching joiner's figure), against the real kdcmp.lua
-- under MoonSharp (engine stubbed, fake clock). Driven by Test-WO179BSynthetic.ps1.
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
local function runTimers()
    local t = TIMERS
    TIMERS = {}
    for _, x in ipairs(t) do x.f() end
end
-- the copy's tick runs every 0.1 s: advance in ticks
local function ticks(n) for _ = 1, n do NOW = NOW + 0.1; runTimers() end end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
local W = KCD2MP.w179
local function fresh()
    TIMERS = {}; W.vid = nil; W.hold = false; W.holdUntil = 0; W.ign = nil
    PX, PY, PZ, PRZ = 100, 100, 10, 0
end

-- (P) the defaults and the switches
do
    check("P1: mp_scene_skip_both and mp_scene_watch_safe are on by default", W.skipBoth == true and W.watchSafe == true)
    check("P2: the console commands exist", CCMDS["mp_scene_skip_both"] ~= nil and CCMDS["mp_scene_watch_safe"] ~= nil)
    local mark = #LOG
    check("P3: off reaches the agent", KCD2MP_W179SetSkipBoth("off") == true and W.skipBoth == false and countEvt("w179_cfg", "", mark) == 1
        and logCount("skip_both=off watch_safe=on", mark) == 1)
    check("P4: on again, and the other one", KCD2MP_W179SetSkipBoth("on") == true and KCD2MP_W179SetWatchSafe("off") == true
        and logCount("skip_both=on watch_safe=off", mark) == 1 and KCD2MP_W179SetWatchSafe("on") == true and W.watchSafe == true)
    check("P5: a bad word is refused", KCD2MP_W179SetWatchSafe("maybe") == false and W.watchSafe == true)
    noErrs("P")
end

-- (H) the hold: the ladder's dismount and turn under a hold are never a skip; after the release the pose is taken again
do
    fresh()
    local mark = #LOG
    KCD2MP_W179PlayVideo(5, "Videos/m03/x/x.bk2", 60)
    ticks(16)   -- 1.6 s: the pose is taken
    check("H1: the copy plays and its pose is taken after 1.5 s", W.vid ~= nil and W.vid.pose ~= nil)
    KCD2MP_W179VideoHold(true)
    PX = PX + 1.2; PRZ = 0.8   -- the dismount: a step and a turn
    ticks(5)
    check("H2: under the hold a step and a turn end nothing", W.vid ~= nil and countEvt("w179", "video-end", mark) == 0)
    KCD2MP_W179VideoHold(false)
    PX = PX + 0.3   -- still settling inside the grace
    ticks(3)
    check("H3: inside the 0.5 s grace after the release nothing ends either", W.vid ~= nil and countEvt("w179", "video-end", mark) == 0)
    ticks(5)
    check("H4: the pose is taken again after the grace", W.vid ~= nil and W.vid.pose ~= nil and math.abs(W.vid.pose.x - PX) < 0.01)
    PX = PX + 1   -- the player's own movement now: his copy ends here
    ticks(1)
    check("H5: a real move after that ends the copy (skipped, local)", W.vid == nil and countEvt("w179", "video-end 5 skipped", mark) == 1)
    check("H6: ...and no skip key is reported for it", countEvt("w179", "skip-key", mark) == 0)
    noErrs("H")
end

-- (J) a move of more than 4 m in one tick is the game or a mod (a pull, an unstuck), never a skip
do
    fresh()
    local mark = #LOG
    KCD2MP_W179PlayVideo(6, "Videos/m03/x/x.bk2", 60)
    ticks(16)
    PX = PX + 25
    ticks(1)
    check("J1: a 25 m jump under the copy is taken as a new pose, not a skip", W.vid ~= nil and W.stats.moveRetakes >= 1
        and logCount("not a skip", mark) == 1)
    ticks(3)
    check("J2: standing still after it ends nothing", W.vid ~= nil)
    KCD2MP_W179StopVideo("partner-ended")
    check("J3: the triggerer's end stops the movie (the game's own stop command)", W.vid == nil and CMDS[#CMDS] == "wh_ui_StopMovie")
    noErrs("J")
end

-- (E) a hold never released expires after 10 s; the length fallback is 1.5 s past the length (the agent ends it first, fade first)
do
    fresh()
    local mark = #LOG
    KCD2MP_W179PlayVideo(7, "Videos/m03/x/x.bk2", 30)
    ticks(16)
    KCD2MP_W179VideoHold(true)
    ticks(101)
    check("E1: an unreleased hold expires by itself after 10 s", W.hold == false and logCount("hold expired", mark) == 1)
    fresh()
    mark = #LOG
    KCD2MP_W179PlayVideo(8, "Videos/m03/x/x.bk2", 5)
    ticks(60)   -- 6.0 s
    check("E2: at length + 1 s the copy still plays (the agent's end comes first)", W.vid ~= nil)
    ticks(6)
    check("E3: at length + 1.5 s the fallback ends it", W.vid == nil and countEvt("w179", "video-end 8 ended", mark) == 1)
    noErrs("E")
end

-- (K) the skip key is told once per press and never consumed
do
    local mark = #LOG
    check("K1: the skip action is not consumed", KCD2MP_W179OnAction("cutscene_skip", "press") == false)
    check("K2: ...and told to the agent", countEvt("w179", "skip-key cutscene_skip", mark) == 1)
    KCD2MP_W179OnAction("cutscene_skip", "release")
    KCD2MP_W179OnAction("moveforward", "press")
    check("K3: a release or another action is not a skip", countEvt("w179", "skip-key", mark) == 1)
    noErrs("K")
end

-- (A) the ignore window takes the animals near him too (logged once each, whether the game's call takes them or not)
do
    fresh()
    NEAR = { npc("bandit_a", "NPC", 110, 100, "w-bandit", true), npc("wolf_1", "Wolf", 105, 100, "w-wolf", false),
             npc("dog_1", "WildDog", 104, 100, "refuse", false), npc("horse_1", "Horse", 103, 100, "w-horse", false) }
    player.wuid = "w-henry"
    local mark = #LOG
    IGNORES = {}
    check("A1: the window starts", KCD2MP_W179Ignore("player", PX, PY, PZ, 600) == true)
    local a = {}
    for _, x in ipairs(IGNORES) do a[x.a] = x.b end
    check("A2: the bandit and the wolf ignore Henry", a["w-bandit"] == "w-henry" and a["w-wolf"] == "w-henry")
    check("A3: a horse is not asked", a["w-horse"] == nil)
    check("A3b: the timeout is a time span ('600s', the game's own form; a bare number lapsed in ~10 s live)", IGNORES[1] and IGNORES[1].secs == "600s", IGNORES[1] and IGNORES[1].secs)
    check("A4: each animal is logged with the call's answer", logCount("WO179-IGNORE animal wolf_1 class=Wolf wuid=yes ignore=ok", mark) == 1
        and logCount("WO179-IGNORE animal dog_1 class=WildDog wuid=yes ignore=REFUSED", mark) == 1)
    ERRS = {}   -- the refused call above is a swallowed error on purpose
    ticks(31)   -- the 3 s re-apply
    check("A5: the re-apply does not log the animals again", logCount("WO179-IGNORE animal", mark) == 2)
    KCD2MP_W179IgnoreEnd("test")
    noErrs("A")
end

-- (W) the host protects a watching joiner's figure: the fight round it ends the game's own way, its people leave it alone
do
    fresh()
    local fig = npc("kcd2mp_1", "NPC", 300, 300, "w-fig", false)
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    KCD2MP.ghosts["1"] = { entity = fig }
    local bandit = npc("bandit_b", "NPC", 305, 300, "w-bandit2", true)
    NEAR = { bandit }
    STOPS = {}; ENDED = {}; IGNORES = {}
    local mark = #LOG
    check("W1: the protection runs", KCD2MP_W179Protect("1", 300, 300, 10, 600) == true)
    check("W2: the bandit fighting round the figure gets the game's stopFight", #STOPS == 1 and STOPS[1].kind == "stopFight" and STOPS[1].id == bandit.id)
    check("W3: the figure's own combat ends (TryEndCombat on the figure, not Henry)", #ENDED == 1 and ENDED[1] == fig.id)
    check("W4: the bandit ignores the figure", #IGNORES == 1 and IGNORES[1].a == "w-bandit2" and IGNORES[1].b == "w-fig")
    check("W5: logged", logCount("WO179-PROTECT the partner's figure 1", mark) == 1 and logCount("TryEndCombat on the partner's figure", mark) == 1)
    KCD2MP_W179IgnoreEnd("test")
    KCD2MP_W179SetWatchSafe("off")
    STOPS = {}
    check("W6: mp_scene_watch_safe off: nothing is done", KCD2MP_W179Protect("1", 300, 300, 10, 600) == false and #STOPS == 0)
    KCD2MP_W179SetWatchSafe("on")
    KCD2MP.ghosts["1"] = nil
    noErrs("W")
end

OUT = table.concat(RESULTS, "\n")
