-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-157 synthetic test (the game-side half), against the real kdcmp.lua under MoonSharp.
--
--   (A) the host's area check: private / public / open (a shop) / unknown, from the engine's labels
--   (G) the stop grace: the first walk-away of a game is told, not fled
--   (C) the connection indicator: struggling at half of 10 s bad, one line after 30 s, ok again
--   (P) the snap counter: per window, per figure, per session
--   (T) talking to a copy: its hands, stance and activity reset; the placement held, released at the end
--   (L) a figure's look IK off at spawn; mp_avatar_look gives it back
--   (R) the rest top-up: a real sleep the game gave nothing gets the game's own rates; a rested sleep, a wait, a short
--       sleep and mp_sleep_rest off are left alone
--   (F) the fast-travel line names the key
--   (M) a ForceMount is never tried far from this player
--
-- Driven by Test-WO157Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-157-findings.md.

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
System.GetTerrainElevation = function(p) return 0 end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
AI.EnableUpdateLookTarget = function(id, on) LOOKT = { id = id, on = on } end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
Game = mkstub()
Calendar = mkstub()
WORLD = 1000000
Calendar.GetWorldTime = function() return WORLD end
LABELS = {}
XGenAIModule = mkstub()
XGenAIModule.IsPointInAreaWithLabel = function(p, l) return LABELS[l] == true end

STATE = { health = 50, exhaust = 40, hunger = 60 }
player = { id = 1, soul = {
    GetState = function(self, n) return STATE[n] end,
    SetState = function(self, n, v) STATE[n] = v end,
} }
player.GetWorldPos = function() return { x = 0, y = 0, z = 0 } end

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
local function countEvt(name, argPrefix, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " " .. (argPrefix or ""), 1, true) then n = n + 1 end
    end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
local P = { x = 10, y = 20, z = 30 }

-- (A) the host's own world decides a trespass -----------------------------------------------------------------------------------
do
    ERRS = {}
    LABELS = { private = true, interior = true, settlement = true }
    check("A1: a house private here", KCD2MP_W157AreaAt(P) == "private")
    LABELS = { private = true, antitrespass = true, publicServiceTrespassOverride = true }
    check("A2: a shop its keeper opened (antitrespass): open", KCD2MP_W157AreaAt(P) == "open")
    LABELS = { personal = true, publicServiceTrespassOverride = true }
    check("A3: a public service's override alone opens it too", KCD2MP_W157AreaAt(P) == "open")
    LABELS = { settlement = true }
    check("A4: a street: public", KCD2MP_W157AreaAt(P) == "public")
    check("A5: no point: unknown", KCD2MP_W157AreaAt(nil) == "unknown")
    LABELS = { private = true }
    local mark = #LOG
    check("A6: a private spot and a private figure: judged", KCD2MP_W157HostTrespass(1, 5, P, P) == true)
    LABELS = { private = true, antitrespass = true }
    check("A7: the shop open here: not a trespass", KCD2MP_W157HostTrespass(1, 6, P, nil) == false
        and lastLog("WO157-TRESPASS src=1 id=6", mark) ~= nil and lastLog("here=open", mark) ~= nil)
    NOW = NOW + 30
    check("A8: told again only after 20 s (the 8 s re-reports do not flood the log)", KCD2MP_W157HostTrespass(1, 7, P, nil) == false
        and lastLog("WO157-TRESPASS src=1 id=7", mark) ~= nil)
    local m2 = #LOG
    KCD2MP_W157HostTrespass(1, 8, P, nil)
    check("A9: ... and not within 20 s", lastLog("WO157-TRESPASS src=1 id=8", m2) == nil)
    check("A10: the switches are registered", CCMDS["mp_trespass_host"] ~= nil and CCMDS["mp_stop_grace"] ~= nil and CCMDS["mp_trespass_hud"] ~= nil
        and CCMDS["mp_w157_status"] ~= nil)
    mark = #LOG
    check("A11: mp_trespass_hud on tells the agent", KCD2MP_W157SetTrespassHud("on") == true and countEvt("w157_cfg", "trespass_hud=on", mark) == 1)
    check("A12: mp_trespass_hud needs a value", KCD2MP_W157SetTrespassHud("") == false)
    noErrs("A")
end

-- (C) the connection indicator ------------------------------------------------------------------------------------------------
do
    ERRS = {}
    local c = KCD2MP.w157.conn
    c.samples = {}; c.struggling = false; c.badSince = nil; c.toldAt = -1e9
    KCD2MP.npcSilence = KCD2MP.npcSilence or {}
    KCD2MP.npcSilence.agentAt = -1e9
    local t0 = #TOASTS
    for i = 1, 5 do NOW = NOW + 2; KCD2MP_ShowPing(60) end
    check("C1: a good connection: nothing said", KCD2MP_W157ConnNote() == "" and #TOASTS == t0)
    local mark = #LOG
    for i = 1, 5 do NOW = NOW + 2; KCD2MP_ShowPing(400) end
    check("C2: half of the last 10 s bad: the Connection line says it", KCD2MP_W157ConnNote():find("struggling %(ping 400 ms%)") ~= nil
        and lastLog("WO157-CONN struggling", mark) ~= nil)
    check("C3: no line on screen before 30 s", #TOASTS == t0)
    for i = 1, 14 do NOW = NOW + 2; KCD2MP_ShowPing(450) end
    check("C4: bad for 30 s: one line on screen", #TOASTS == t0 + 1 and TOASTS[#TOASTS]:find("connection to the other player is struggling") ~= nil)
    for i = 1, 10 do NOW = NOW + 2; KCD2MP_ShowPing(450) end
    check("C5: ... once in 5 minutes", #TOASTS == t0 + 1)
    mark = #LOG
    for i = 1, 6 do NOW = NOW + 2; KCD2MP_ShowPing(50) end
    check("C6: good again: the note goes", KCD2MP_W157ConnNote() == "" and lastLog("WO157-CONN ok", mark) ~= nil)
    KCD2MP.npcSilence.agentAt = NOW; KCD2MP.npcSilence.lagMs = 2500
    for i = 1, 5 do NOW = NOW + 2; KCD2MP.npcSilence.agentAt = NOW; KCD2MP_ShowPing(40) end
    check("C7: the agent 2.5 s behind counts as struggling too", KCD2MP_W157ConnNote() ~= "")
    KCD2MP.npcSilence.lagMs = 0
    noErrs("C")
end

-- (L) look IK off on a figure; (M) no far ForceMount ------------------------------------------------------------------------
do
    ERRS = {}
    local e = { id = 4242, actor = { SetLookIK = function(self, on) LOOKIK = on end } }
    e.GetWorldPos = function() return { x = 1000, y = 0, z = 0 } end
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    KCD2MP.ghosts["5"] = { entity = e, istate = { spawnedAtClock = -100, isRiding = true } }
    local mark = #LOG
    KCD2MP_W157AvatarLook("5", "spawn")
    check("L1: look IK and the AI look target off at spawn", LOOKIK == false and LOOKT and LOOKT.on == false and LOOKT.id == 4242
        and lastLog("WO157-LOOK id=5 why=spawn look_ik=off", mark) ~= nil)
    KCD2MP_W157SetAvatarLook("on")
    check("L2: mp_avatar_look on gives them back", LOOKIK == true and LOOKT.on == true)
    KCD2MP_W157SetAvatarLook("off")
    e.human = { ForceMount = function() MOUNTED = true end }
    KCD2MP.horseGhosts = KCD2MP.horseGhosts or {}
    KCD2MP.horseGhosts["5"] = { entity = { id = 77 } }
    MOUNTED = false
    mark = #LOG
    KCD2MP_MountNPCOnHorse("5")
    check("M1: a figure 1 km away is not force-mounted (the field's host froze on one)", MOUNTED == false and lastLog("WO157-MOUNT id=5", mark) ~= nil)
    e.GetWorldPos = function() return { x = 5, y = 0, z = 0 } end
    KCD2MP_MountNPCOnHorse("5")
    check("M2: near: mounted as before", MOUNTED == true)
    KCD2MP.ghosts["5"] = nil; KCD2MP.horseGhosts["5"] = nil
    noErrs("L/M")
end

-- (R) the rest top-up --------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    local function sleep(id, hours, gain, hgain)
        KCD2MP_W157RestLine("start", id)
        WORLD = WORLD + hours * 3600
        STATE.exhaust = STATE.exhaust + gain
        STATE.health = STATE.health + (hgain or 0)
        KCD2MP_W157RestLine("end")
    end
    STATE = { health = 50, exhaust = 40, hunger = 60 }
    local mark = #LOG
    sleep(2, 4, -8.4)   -- the game gave nothing (the awake rate)
    check("R1: a real 4 h sleep the game gave nothing: exhaust 40 + 4 x 12.45, health 50 + 4 x 7", math.abs(STATE.exhaust - 89.8) < 0.01
        and math.abs(STATE.health - 78) < 0.01 and lastLog("WO157-REST rested", mark) ~= nil, STATE.exhaust)
    STATE = { health = 90, exhaust = 80, hunger = 60 }
    sleep(2, 12, -20)
    check("R2: never over 100", STATE.exhaust == 100 and STATE.health == 100)
    STATE = { health = 50, exhaust = 40, hunger = 60 }
    mark = #LOG
    sleep(2, 4, 49.8, 50)  -- the game rested him itself
    check("R3: a sleep the game rested is its own", math.abs(STATE.exhaust - 89.8) < 0.01 and STATE.health == 100 and lastLog("WO157-REST rested", mark) == nil)
    STATE = { health = 50, exhaust = 40, hunger = 60 }
    sleep(1, 4, -8)          -- a wait
    sleep(14, 4, 0)          -- the forced sleep screen
    sleep(2, 0.25, -0.5)     -- a 15 min nap
    check("R4: a wait, the forced screen and a nap are never topped up", math.abs(STATE.exhaust - 31.5) < 0.01 and STATE.health == 50, STATE.exhaust)
    KCD2MP_W157SetSleepRest("off")
    STATE = { health = 50, exhaust = 40, hunger = 60 }
    sleep(2, 4, -8)
    check("R5: mp_sleep_rest off: the game's result only", STATE.exhaust == 32 and STATE.health == 50)
    KCD2MP_W157SetSleepRest("on")
    KCD2MP_W157RestLine("end")
    check("R6: an end with no start does nothing", STATE.exhaust == 32)
    noErrs("R")
end

-- (T) talking to a copy -------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    CMDS = {}
    local w = KCD2MP.w137
    w.talking["ttac_blacksmith"] = { since = NOW }
    local mark = #LOG
    KCD2MP_W157TalkFree("ttac_blacksmith")
    local resets = 0
    for _, c in ipairs(CMDS) do if c:find("^wh_ai_NPCStateResetElement ttac_blacksmith ") then resets = resets + 1 end end
    check("T1: its hands, activity and stance reset (the engine's own console command)", resets == 4
        and CMDS[1] == "wh_ai_NPCStateResetElement ttac_blacksmith LeftHand", CMDS[1])
    check("T2: the placement holds for the conversation", countEvt("w157_talkfree", "on ttac_blacksmith", mark) == 1 and w.talking["ttac_blacksmith"].freed == true)
    KCD2MP_W157SetTalkFree("off")
    CMDS = {}
    KCD2MP_W157TalkFree("ttac_blacksmith")
    check("T3: mp_talk_free off: nothing reset", #CMDS == 0)
    KCD2MP_W157SetTalkFree("on")
    w.talking["ttac_blacksmith"] = nil
    noErrs("T")
end

-- (F) the fast-travel line (the snap counter's own test is Test-WO1005Synthetic.lua (g)) --------------------------------------
do
    ERRS = {}
    local M = KCD2MP.w154menu
    M.menuKey = nil
    check("F1: the joiner's line is information and names the key", M.ftText(true) == "Fast travel is turned off for co-op. The host can turn it on in the mod menu (Insert).", M.ftText(true))
    check("F2: the host's line", M.ftText(false) == "Fast travel is turned off for co-op. You can turn it on in the mod menu (Insert).")
    M.menuKey = "np_add"
    check("F3: the player's own menu key", M.ftText(true):find("%(Numpad %+%)") ~= nil, M.ftText(true))
    M.menuKey = nil
    noErrs("F")
end

OUT = table.concat(RESULTS, "\n")
