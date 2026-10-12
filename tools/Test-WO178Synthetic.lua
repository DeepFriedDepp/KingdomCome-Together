-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-178 synthetic test (Part 0's Lua half: the save path, the quicksave key and its waits, the joiner's request, the draw probe, the
-- switches), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock). Driven by Test-WO178Synthetic.ps1.
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
RESTSAVES = 0
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Game.SaveGameViaResting = function() RESTSAVES = RESTSAVES + 1 end   -- must never be called (WO-178 Part 0)
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
Calendar = mkstub()
Calendar.GetWorldTime = function() return 1000000 end
XGenAIModule = mkstub()
XGenAIModule.TryEndCombat = function(w) return true end
EnvironmentModule = { GetRainIntensity = function() return 0 end }

-- the inventory: a sword (a melee weapon by its database name) unless a case takes it away
ITEMS = { { id = 9001, class = "sword_c", name = "sword_hunting" } }
ItemManager = mkstub()
ItemManager.GetItem = function(id) for _, it in ipairs(ITEMS) do if it.id == id then return { id = it.id, class = it.class, health = 1 } end end return nil end
ItemManager.GetItemName = function(class) for _, it in ipairs(ITEMS) do if it.class == class then return it.name end end return "" end

PX, PY, PZ = 100, 100, 10
IN_DIALOG = false
P_DANGER = false
CONTEXTS = {}
DRAWN = false
DRAW_WORKS = true
DRAWS = 0
player = { id = 1, class = "Player" }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PX, y = PY, z = PZ } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end, EquipInventoryItem = function() end }
player.human = { IsInDialog = function() return IN_DIALOG end, IsWeaponDrawn = function() return DRAWN end,
                 DrawWeapon = function() DRAWS = DRAWS + 1; if DRAW_WORKS then DRAWN = true end end, HolsterWeapon = function() DRAWN = false end }
player.soul = { GetState = function() return 0 end, IsInCombatDanger = function() return P_DANGER end, IsInCombatMode = function() return false end,
                HasScriptContext = function(_, c) return CONTEXTS[c] == true end }
player.inventory = { GetInventoryTable = function() local t = {} for i, it in ipairs(ITEMS) do t[i] = it.id end return t end }
ENTS["Dude"] = player

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
local function advance(s) NOW = NOW + s; runTimers() end
local function lastToast() return TOASTS[#TOASTS] or "" end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
local M = KCD2MP.w154menu
local function role(r, up)
    M.roleNow = function() return r end
    M.sessionUp = function() return up == true end
end
local function idle()
    IN_DIALOG = false; P_DANGER = false; CONTEXTS = {}; NEAR = {}
    KCD2MP.w178.waiting = nil
    KCD2MP.w178.quicksave = true
    KCD2MP.w178.hostQuicksave = nil
    KCD2MP.w178.lastKeyAt = -1e9
    TIMERS = {}
end

-- (P) the defaults and the switches
do
    local w = KCD2MP.w178
    check("P1: mp_world_autosave is off by default (saving is the player's choice)", w.worldAutosave == false)
    check("P2: mp_quicksave (the host's setting) is on by default", w.quicksave == true)
    check("P3: the quicksave key defaults to Home (F10 is this build's own debug key)", w.qsKey == "home" and W178.KEY_NAMES.home == "Home")
    check("P4: the console commands exist", CCMDS["mp_quicksave"] ~= nil and CCMDS["mp_world_autosave"] ~= nil and CCMDS["mp_joiner_lock"] ~= nil
        and CCMDS["mp_host_lock_answer"] ~= nil and CCMDS["mp_fallback_hold"] ~= nil and CCMDS["mp_fallback_engine_cap"] ~= nil and CCMDS["mp_w178_status"] ~= nil)
    local mark = #LOG
    check("P5: mp_world_autosave on reaches the agent", KCD2MP_W178SetWorldAutosave("on") == true and w.worldAutosave == true
        and countEvt("w178_cfg", "world_autosave=on", mark) == 1)
    check("P6: ...and off again", KCD2MP_W178SetWorldAutosave("off") == true and w.worldAutosave == false and countEvt("w178_cfg", "world_autosave=off", mark) == 1)
    check("P7: a bad word is refused", KCD2MP_W178SetWorldAutosave("maybe") == false and w.worldAutosave == false)
    mark = #LOG
    check("P8: the DLL's switches go through the agent", KCD2MP_W178SetJoinerLock("off") == true and countEvt("w178_cfg", "joiner_lock=off", mark) == 1
        and KCD2MP_W178SetHostLockAnswer("on") == true and countEvt("w178_cfg", "host_lock_answer=on", mark) == 1
        and KCD2MP_W178SetFallbackHold("on") == true and countEvt("w178_cfg", "fallback_hold=on", mark) == 1
        and KCD2MP_W178SetFallbackEngineCap("off") == true and countEvt("w178_cfg", "fallback_engine_cap=off", mark) == 1)
    check("P9: a bad word for a DLL switch is refused", KCD2MP_W178SetJoinerLock("perhaps") == false)
    noErrs("P")
end

-- (S) every save the mod asks for is the schnapps save
do
    idle(); ERRS = {}
    local mark = #LOG
    check("S1: the request goes to the agent as w178_save, and the agent checks the file (S4)", KCD2MP_W178RequestSave("quicksave:host:key") == true
        and countEvt("w178_save", "quicksave:host:key", mark) == 1 and countEvt("w175_check", "save", mark) == 1)
    check("S2: logged as the schnapps path", lastLog("WO178-SAVE asked why=quicksave:host:key path=schnapps", mark) ~= nil)
    mark = #LOG
    check("S3: the host's world save asks the same way", KCD2MP_HostWorldSave("schedule", 1) == true and countEvt("w178_save", "world:schedule", mark) == 1)
    mark = #LOG
    KCD2MP_W140RestSave()
    check("S4: the shared sleep's save asks the same way", countEvt("w178_save", "rest-sleep", mark) == 1)
    check("S5: Game.SaveGameViaResting is never called", RESTSAVES == 0)
    -- an arranged fight set up near the player defers the world save (S3's player contexts do too: WO-175's own suite)
    NEAR = { { id = 50, actor = {}, soul = { HasScriptContext = function(_, c) return c == "combat_arrangedFight" end }, GetName = function() return "ttac_man_3" end } }
    mark = #LOG
    check("S6: an arranged fight near: the world save waits, logged", KCD2MP_HostWorldSave("schedule", 1) == false and countEvt("w178_save", "world:", mark) == 0
        and lastLog("WO175-SAVE deferred reason=arranged-fight-near:ttac_man_3", mark) ~= nil)
    NEAR = {}
    noErrs("S")
end

-- (Q) the quicksave: idle, waits, the host's setting
do
    idle(); ERRS = {}
    role("host", true)
    local mark = #LOG
    check("Q1: idle: the key saves at once", KCD2MP_W178QuickSave("key") == true and countEvt("w178_save", "quicksave:host:key", mark) == 1
        and lastLog("WO178-QUICKSAVE by=host result=saving why=key", mark) ~= nil)
    mark = #LOG
    KCD2MP_W178SaveDone("saved", "playline4/save001.whs", "quicksave:host:key")
    check("Q2: the agent's answer: saved, said on screen", lastLog("WO178-QUICKSAVE by=host result=saved file=playline4/save001.whs", mark) ~= nil and lastToast() == "Game saved.")
    -- in a dialogue: it waits and saves after, with a one-line note
    idle(); IN_DIALOG = true
    mark = #LOG
    check("Q3: in a dialogue the quicksave waits (no request), the player is told once", KCD2MP_W178QuickSave("key") == true
        and countEvt("w178_save", "quicksave:", mark) == 0 and lastLog("WO178-QUICKSAVE by=host result=waited why=dialogue", mark) ~= nil
        and lastToast() == "The game will save after the conversation.")
    advance(1.0)
    check("Q4: still talking: still waiting", countEvt("w178_save", "quicksave:", mark) == 0 and KCD2MP.w178.waiting ~= nil)
    IN_DIALOG = false
    advance(1.0)
    check("Q5: the dialogue ended: it saves now", countEvt("w178_save", "quicksave:host:key", mark) == 1 and KCD2MP.w178.waiting == nil
        and lastLog("WO178-QUICKSAVE by=host result=saving why=after-wait", mark) ~= nil and lastToast() == "Saving now.")
    -- in a fight, and in a quest's arranged fight
    idle(); P_DANGER = true
    mark = #LOG
    KCD2MP_W178QuickSave("key")
    check("Q6: in a fight it waits", lastLog("WO178-QUICKSAVE by=host result=waited why=combat", mark) ~= nil and lastToast() == "The game will save after the fight.")
    mark = #LOG
    KCD2MP_W178QuickSave("key")
    check("Q7: a second press while one waits does not stack", logCount("result=waited why=already-waiting", mark) == 1 and countEvt("w178_save", "quicksave:", mark) == 0)
    idle(); CONTEXTS["combat_arrangedFight"] = true
    mark = #LOG
    KCD2MP_W178QuickSave("key")
    check("Q8: in a quest's arranged fight it waits", lastLog("WO178-QUICKSAVE by=host result=waited why=arranged-fight", mark) ~= nil)
    idle(); NEAR = { { id = 51, actor = {}, soul = { HasScriptContext = function(_, c) return c == "combat_forceUnarmedWhileInUnarmedSkirmish" end }, GetName = function() return "tzel_man_2" end } }
    mark = #LOG
    KCD2MP_W178QuickSave("key")
    check("Q9: an arranged fight set up near him: it waits", lastLog("WO178-QUICKSAVE by=host result=waited why=arranged-fight-near:tzel_man_2", mark) ~= nil
        and lastToast() == "The game will save after the quest's fight.")
    -- a mod transaction in flight
    idle(); KCD2MP.w174.busy = true
    mark = #LOG
    KCD2MP_W178QuickSave("key")
    check("Q10: a mod transaction in flight: it waits", lastLog("WO178-QUICKSAVE by=host result=waited why=weapon-repair", mark) ~= nil)
    KCD2MP.w174.busy = false
    -- the host's setting off
    idle()
    mark = #LOG
    check("Q11: mp_quicksave off: the host's setting, said", KCD2MP_W178QuickSave("off") == true and KCD2MP.w178.quicksave == false and countEvt("w178_cfg", "quicksave=off", mark) == 1)
    mark = #LOG
    check("Q12: with mp_quicksave off the key says so and saves nothing", KCD2MP_W178QuickSave("key") == false and countEvt("w178_save", "", mark) == 0
        and lastLog("WO178-QUICKSAVE by=host result=refused why=mp_quicksave-off", mark) ~= nil and lastToast():find("Quicksave is off", 1, true) ~= nil)
    KCD2MP_W178QuickSave("on")
    -- the console command with no argument saves now (the console passes %line)
    idle()
    mark = #LOG
    check("Q13: bare mp_quicksave saves now", KCD2MP_W178QuickSave("%line") == true and countEvt("w178_save", "quicksave:host:console", mark) == 1)
    -- the menu's Save now
    idle()
    mark = #LOG
    check("Q14: the menu's Save now", KCD2MP_W178QuickSave("menu") == true and countEvt("w178_save", "quicksave:host:menu", mark) == 1)
    -- a waiting quicksave gives up after 15 minutes
    idle(); IN_DIALOG = true
    KCD2MP_W178QuickSave("key")
    mark = #LOG
    advance(901)
    check("Q15: a wait that never ends gives up after 15 minutes, said", KCD2MP.w178.waiting == nil and lastLog("result=refused why=waited-too-long:dialogue", mark) ~= nil)
    -- a load ends a waiting quicksave
    idle(); IN_DIALOG = true
    KCD2MP_W178QuickSave("key")
    KCD2MP_W178AfterLoad("reload")
    check("Q16: a load ends a waiting quicksave", KCD2MP.w178.waiting == nil)
    idle()
    check("Q17: no resting save anywhere in this section", RESTSAVES == 0)
    noErrs("Q")
end

-- (K) the key: its own action, press only, one press once
do
    idle(); ERRS = {}
    role("host", true)
    local mark = #LOG
    check("K1: the key's action is used", KCD2MP_W178OnAction("kcd2mp_quicksave", "press") == true and countEvt("w178_save", "quicksave:host:key", mark) == 1)
    NOW = NOW + 0.1
    KCD2MP_W178OnAction("kcd2mp_quicksave", "press")
    check("K2: a second delivery of the same press (both hooks) saves once", countEvt("w178_save", "quicksave:", mark) == 1)
    check("K3: a release is used but does nothing", KCD2MP_W178OnAction("kcd2mp_quicksave", "release") == true and countEvt("w178_save", "quicksave:", mark) == 1)
    check("K4: another action is not ours", KCD2MP_W178OnAction("use", "press") == false)
    mark = #LOG
    KCD2MP_W178QsKeys("home", "delete", "stored")
    check("K5: the agent's answer: bound now / stored for the next start", KCD2MP.w178.qsKey == "home" and KCD2MP.w178.qsKeyStored == "delete"
        and lastLog("WO178-KEY quicksave_key=delete stored", mark) ~= nil)
    noErrs("K")
end

-- (J) the joiner asks the host; the host saves and answers
do
    idle(); ERRS = {}
    role("joiner", true)
    local mark = #LOG
    check("J1: a joiner's key asks the host's game (no save here)", KCD2MP_W178QuickSave("key") == true and countEvt("w178_qs_ask", "key", mark) == 1
        and countEvt("w178_save", "", mark) == 0 and lastToast() == "Asked your host's game to save.")
    KCD2MP_W178HostSetting(false)
    mark = #LOG
    check("J2: the host's setting off: the joiner is told, nothing asked", KCD2MP_W178QuickSave("key") == false and countEvt("w178_qs_ask", "", mark) == 0
        and lastToast() == "Quicksave is off in this session (the host's setting).")
    KCD2MP_W178HostSetting(true)
    KCD2MP_W178PartnerAnswer("waited", "")
    check("J3: the host's answer 'waited' is said", lastToast():find("will save after", 1, true) ~= nil)
    KCD2MP_W178PartnerAnswer("refused", "quicksave-off")
    check("J4: the host's 'refused quicksave-off' is remembered and said", KCD2MP.w178.hostQuicksave == false)
    -- the host's side
    idle(); role("host", true)
    mark = #LOG
    check("J5: the host's game saves for the partner and answers", KCD2MP_W178PartnerQuickSave(2) == true and countEvt("w178_save", "quicksave:joiner:partner", mark) == 1
        and countEvt("w178_qs_answer", "saved", mark) == 1)
    KCD2MP_W178SaveDone("saved", "playline4/save002.whs", "quicksave:joiner:partner")
    check("J6: the host sees 'Your partner saved the game'", lastToast() == "Your partner saved the game." and lastLog("WO178-QUICKSAVE by=joiner result=saved", mark) ~= nil)
    idle(); IN_DIALOG = true
    mark = #LOG
    KCD2MP_W178PartnerQuickSave(2)
    check("J7: the host in a dialogue: the partner's save waits, the answer says so", countEvt("w178_qs_answer", "waited", mark) == 1 and countEvt("w178_save", "", mark) == 0)
    idle(); KCD2MP.w178.quicksave = false
    mark = #LOG
    KCD2MP_W178PartnerQuickSave(2)
    check("J8: the host's mp_quicksave off: refused with the reason, nothing saved", countEvt("w178_qs_answer", "refused quicksave-off", mark) == 1 and countEvt("w178_save", "", mark) == 0)
    idle()
    noErrs("J")
end

-- (D) the draw probe after a load
do
    idle(); ERRS = {}
    DRAWN = false; DRAW_WORKS = true; DRAWS = 0
    local mark = #LOG
    KCD2MP_W178AfterLoad("reload")
    advance(0.5)    -- the probe starts
    advance(0.1)    -- the first poll sees the draw
    check("D1: a good save: the probe draws, holsters at once, says ok", lastLog("WO178-DRAWPROBE result=ok", mark) ~= nil and DRAWN == false and DRAWS == 1)
    check("D2: nothing on screen for a good save", countEvt("w178_stuck", "", mark) == 0 and not (lastToast():find("stuck", 1, true)))
    mark = #LOG
    KCD2MP_W178AfterLoad("settle")   -- the settle of the same load: no second probe
    advance(0.5); advance(0.1)
    check("D3: one probe per load (the settle belongs to the load before it)", logCount("WO178-DRAWPROBE", mark) == 0)
    -- a stuck save the repair cannot free
    DRAWN = false; DRAW_WORKS = false; DRAWS = 0
    mark = #LOG
    KCD2MP_W178AfterLoad("reload")
    for i = 1, 80 do advance(0.1) end
    for i = 1, 20 do advance(0.6) end
    for i = 1, 10 do advance(2.5) end
    check("D4: refused: mp_repair_combat ran", lastLog("WO178-DRAWPROBE draw refused after 2.4 s -- running mp_repair_combat", mark) ~= nil and lastLog("WO174-REPAIR", mark) ~= nil)
    check("D5: still refused: the player is told plainly, the backup is marked", lastLog("WO178-DRAWPROBE result=refused", mark) ~= nil
        and countEvt("w178_stuck", "draw-refused", mark) == 1)
    local told = false
    for _, t in ipairs(TOASTS) do if t == W178.STUCK_TEXT then told = true end end
    check("D6: the message is the work order's words", told and W178.STUCK_TEXT:find("RESTORE SAVES", 1, true) ~= nil)
    -- no melee weapon: no probe
    ITEMS = { { id = 9002, class = "bread_c", name = "bread" } }
    mark = #LOG
    KCD2MP_W178AfterLoad("reload")
    advance(0.5)
    check("D7: no melee weapon: the probe is skipped", lastLog("WO178-DRAWPROBE result=skipped why=no-melee-weapon", mark) ~= nil)
    ITEMS = { { id = 9001, class = "sword_c", name = "sword_hunting" } }
    -- in a dialogue right after a load: skipped
    DRAW_WORKS = true; IN_DIALOG = true
    mark = #LOG
    KCD2MP_W178AfterLoad("reload")
    advance(0.5)
    check("D8: busy (a dialogue): the probe is skipped", lastLog("WO178-DRAWPROBE result=skipped why=dialogue", mark) ~= nil)
    IN_DIALOG = false
    check("D9: no resting save in the probe", RESTSAVES == 0)
    ERRS = {}   -- the repair's own refusals above are its pcalls doing their job (WO-174's suite pins the repair)
end

-- (M) the menu: the host's setting, the key, Save now
do
    ERRS = {}
    local ids = {}
    for _, it in ipairs(M.ITEMS) do ids[it.id] = it end
    check("M1: Gameplay has the host's Quicksave setting", ids.quicksave ~= nil and ids.quicksave.host == true and ids.quicksave.save == "Quicksave" and ids.quicksave.group == "Gameplay")
    check("M2: the quicksave key is in Voice and keys", ids.quicksave_key ~= nil and ids.quicksave_key.kind == "qskey")
    check("M3: Save now calls mp_quicksave's own function", ids.save_now ~= nil and ids.save_now.fn == "KCD2MP_W178QuickSave" and ids.save_now.arg == "menu"
        and CCMDS["mp_quicksave"].body:find("KCD2MP_W178QuickSave", 1, true) ~= nil)
    noErrs("M")
end

check("Z: Game.SaveGameViaResting was never called in the whole run", RESTSAVES == 0)

OUT = table.concat(RESULTS, "\n")
