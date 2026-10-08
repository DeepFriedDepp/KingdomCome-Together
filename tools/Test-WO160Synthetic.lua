-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-160 synthetic test (the game-side half), against the real kdcmp.lua under MoonSharp.
--
--   (S) the own-world verdict: silent at the main menu and during a load, spoken in a loaded world
--   (R) the release before a placement: stance + unstance the game's own way, never a corpse, a talker, a dialogue, a cutscene
--   (C) a conversation stands the NPC still on the other machine (host talk -> joiner copy held; joiner talk -> host's NPC frozen)
--   (H) the avatar's herb gathering as a plain clip, never the minigame fragment
--   (M) the mount gate: a horse is polled every 0.25 s for up to 5 s before the avatar's ForceMount
--   (T) talking to a copy: the two resets the game has (no LeftHand/RightHand: "Unsupported element type to reset!")
--
-- Driven by Test-WO160Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-160-findings.md.

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

-- (S) the own-world verdict needs a loaded world --------------------------------------------------------------------------------
do
    ERRS = {}
    local savedPlayer = player
    player = nil
    System.GetEntityByName = function(n) return ENTS[n] end
    ENTS = {}
    local t0, mark = #TOASTS, #LOG
    KCD2MP_Wo124SessionMode(true)
    KCD2MP_W140Separate(true, "You loaded your own save.")
    check("S1: a session-mode packet at the main menu, then the agent's verdict: no toast", #TOASTS == t0 and lastLog('WO140-SEPARATE "', mark) == nil)
    check("S2: ... and the hold is logged", lastLog("WO160-SEPARATE held at the menu", mark) ~= nil)
    check("S3: ... nothing is left to draw", KCD2MP.w140.sep == nil)
    Game.IsLoadingEngineSaveGame = function() return true end
    player = savedPlayer
    KCD2MP.w140.sepHeld = 0
    mark = #LOG
    KCD2MP_W140Separate(true, "You loaded your own save.")
    check("S4: during a load (the join's own): no toast either", #TOASTS == t0 and KCD2MP.w140.sep == nil and lastLog("WO160-SEPARATE held at the loading", mark) ~= nil)
    Game.IsLoadingEngineSaveGame = function() return false end
    mark = #LOG
    KCD2MP_W140Separate(true, "You loaded your own save.")
    check("S5: in a loaded world it still fires (the real own-save case)", #TOASTS == t0 + 1 and KCD2MP.w140.sep ~= nil and lastLog('WO140-SEPARATE "You loaded your own save."', mark) ~= nil)
    KCD2MP_W140Separate(false, "")
    check("S6: the agent clears it when the join lands", KCD2MP.w140.sep == nil and lastLog("WO140-SEPARATE off", mark) ~= nil)
    check("S7: where is read without an event", (function() local m = #LOG; KCD2MP_Wo124WhereNow(); return countEvt("wo124_where", "", m) == 0 end)())
    noErrs("S")
end

-- (R) the release before a placement -------------------------------------------------------------------------------------------
do
    ERRS = {}
    local function body(name, dead)
        local e = { id = 90, actor = { IsDead = function() return dead == true end }, human = { IsInDialog = function() return false end } }
        ENTS[name] = e
        KCD2MP.npcPuppets[name] = {}
        return e
    end
    body("ttkc_scribe")
    CMDS = {}
    local mark = #LOG
    check("R1: a placed copy: Unstance then Stance, the game's own reset", KCD2MP_W160Release("ttkc_scribe", "activity-changed") == true
        and #CMDS == 2 and CMDS[1] == "wh_ai_NPCStateResetElement ttkc_scribe Unstance" and CMDS[2] == "wh_ai_NPCStateResetElement ttkc_scribe Stance", CMDS[1])
    check("R2: ... one log line", lastLog("WO160-REL npc=ttkc_scribe why=activity-changed unstance=ok stance=ok", mark) ~= nil)
    body("ttkc_corpse", true)
    CMDS = {}
    check("R3: never a corpse (its lying pose IS its unstance)", KCD2MP_W160Release("ttkc_corpse", "x") == false and #CMDS == 0)
    body("ttkc_talker")
    KCD2MP.w137.talking["ttkc_talker"] = { since = NOW }
    CMDS = {}
    check("R4: nor a copy this player is talking to (the talk's own reset ran)", KCD2MP_W160Release("ttkc_talker", "x") == false and #CMDS == 0)
    KCD2MP.w137.talking["ttkc_talker"] = nil
    local d = body("ttkc_dialogue")
    d.human.IsInDialog = function() return true end
    CMDS = {}
    check("R5: nor a body in a dialogue", KCD2MP_W160Release("ttkc_dialogue", "x") == false and #CMDS == 0)
    KCD2MP.cutsceneActive = true
    CMDS = {}
    check("R6: nor during a cutscene", KCD2MP_W160Release("ttkc_scribe", "x") == false and #CMDS == 0)
    KCD2MP.cutsceneActive = false
    check("R7: a name with no body: nothing", KCD2MP_W160Release("ttkc_nobody", "x") == false)
    KCD2MP_W160SetRelease("off")
    CMDS = {}
    check("R8: mp_ctx_release off: nothing is released", KCD2MP_W160Release("ttkc_scribe", "x") == false and #CMDS == 0)
    KCD2MP_W160SetRelease("on")
    check("R9: the switch is registered", CCMDS["mp_ctx_release"] ~= nil and CCMDS["mp_ctx_release"].body == "KCD2MP_W160SetRelease(%line)")
    for _, n in ipairs({ "ttkc_scribe", "ttkc_corpse", "ttkc_talker", "ttkc_dialogue" }) do ENTS[n] = nil; KCD2MP.npcPuppets[n] = nil end
    noErrs("R")
end

-- (T) talking to a copy ----------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    CMDS = {}
    KCD2MP.w137.talking["ttac_blacksmith"] = { since = NOW }
    local mark = #LOG
    KCD2MP_W157TalkFree("ttac_blacksmith")
    check("T1: only the two resets the game has: Unstance and Stance", #CMDS == 2 and CMDS[1] == "wh_ai_NPCStateResetElement ttac_blacksmith Unstance"
        and CMDS[2] == "wh_ai_NPCStateResetElement ttac_blacksmith Stance", CMDS[1])
    check("T2: the log says exactly what was reset", lastLog("reset=Unstance=ok,Stance=ok placement=held", mark) ~= nil)
    KCD2MP.w137.talking["ttac_blacksmith"] = nil
    noErrs("T")
end

-- (C) conversations ----------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    local w = KCD2MP.w160
    local DIALOG = false
    local npc = { GetName = function() return "ttkc_man_11" end, human = { IsInDialog = function() return DIALOG end } }
    ENTS["ttkc_man_11"] = npc
    KCD2MP.w137.host, KCD2MP.w137.active, KCD2MP.w137.joiner = true, true, false
    w.hostTalks = {}
    local mark = #LOG
    KCD2MP_W160HostTalk(npc, { id = player.id }, "OnTalk")
    check("C1: the host's talk is told to the joiner (w160_conv on)", countEvt("w160_conv", "on ttkc_man_11", mark) == 1 and w.hostTalks["ttkc_man_11"] ~= nil
        and lastLog("WO160-CONV host-talk start npc=ttkc_man_11", mark) ~= nil)
    KCD2MP_W160HostTalk(npc, { id = player.id }, "OnChat")
    check("C2: a second press is not told again", countEvt("w160_conv", "on ttkc_man_11", mark) == 1)
    KCD2MP_W160HostTalk(npc, { id = 99 }, "OnTalk")
    w.hostTalks["ttkc_other"] = nil
    check("C3: only this player's own talk counts", w.hostTalks["ttkc_other"] == nil)
    DIALOG = true
    NOW = NOW + 1; KCD2MP_W160ConvTick()
    check("C4: while the dialogue runs: nothing ends", w.hostTalks["ttkc_man_11"] ~= nil and w.hostTalks["ttkc_man_11"].saw == true)
    DIALOG = false
    NOW = NOW + 1; KCD2MP_W160ConvTick()
    check("C5: 1 s out of the dialogue: still held", w.hostTalks["ttkc_man_11"] ~= nil)
    mark = #LOG
    NOW = NOW + 4; KCD2MP_W160ConvTick()
    check("C6: out for 3 s+: the conversation is over, the joiner is told (w160_conv off)", w.hostTalks["ttkc_man_11"] == nil
        and countEvt("w160_conv", "off ttkc_man_11", mark) == 1 and lastLog("host-talk end npc=ttkc_man_11 why=dialog-ended", mark) ~= nil)
    KCD2MP_W160HostTalk(npc, { id = player.id }, "OnTalk")
    mark = #LOG
    NOW = NOW + 26; KCD2MP_W160ConvTick()
    check("C7: a talk that never reached a dialogue ends in 25 s", w.hostTalks["ttkc_man_11"] == nil and lastLog("why=never-started", mark) ~= nil)
    KCD2MP.w137.host, KCD2MP.w137.active = false, false
    mark = #LOG
    KCD2MP_W160HostTalk(npc, { id = player.id }, "OnTalk")
    check("C8: not a host in a session: nothing is told", w.hostTalks["ttkc_man_11"] == nil and countEvt("w160_conv", "on", mark) == 0)

    -- the joiner: the host talks to its copy
    local p = { nativeOwned = true }
    KCD2MP.npcPuppets["ttkc_man_11"] = p
    npc.GetWorldPos = function() return { x = 5, y = 6, z = 7 } end
    mark = #LOG
    check("C9: the copy is held", KCD2MP_W160Conversation(true, "ttkc_man_11") == true and w.conv["ttkc_man_11"] ~= nil
        and lastLog("WO160-CONV copy-held npc=ttkc_man_11 at 5.00 6.00 7.00", mark) ~= nil)
    check("C10: the native writer is held at once", countEvt("npc_native_hold", "ttkc_man_11 2000", mark) == 1)
    mark = #LOG
    check("C11: the puppet tick skips every writer for it", KCD2MP_W160ConvHeld("ttkc_man_11", p, npc) == true)
    NOW = NOW + 1.2
    KCD2MP_W160ConvHeld("ttkc_man_11", p, npc)
    check("C12: ... and holds the native writer again every second", countEvt("npc_native_hold", "ttkc_man_11 2000", mark) == 1)
    mark = #LOG
    KCD2MP_W160Conversation(false, "ttkc_man_11")
    check("C13: the host's conversation over: the copy is free", w.conv["ttkc_man_11"] == nil and KCD2MP_W160ConvHeld("ttkc_man_11", p, npc) == false
        and lastLog("WO160-CONV copy-released npc=ttkc_man_11", mark) ~= nil)
    KCD2MP_W160Conversation(true, "ttkc_man_11")
    NOW = NOW + 301
    mark = #LOG
    check("C14: no end ever comes: released at convMaxS", KCD2MP_W160ConvHeld("ttkc_man_11", p, npc) == false and w.conv["ttkc_man_11"] == nil
        and lastLog("past 300 s", mark) ~= nil)
    mark = #LOG
    check("C15: a name with no streamed copy: refused, logged", KCD2MP_W160Conversation(true, "ttkc_nobody") == false and lastLog("copy-held refused npc=ttkc_nobody", mark) ~= nil)
    check("C16: a hostile name is refused", KCD2MP_W160Conversation(true, "x; quit") == false)
    KCD2MP_W160Conversation(true, "ttkc_man_11")
    KCD2MP_W160ConvReleaseAll("test")
    check("C17: release-all gives every copy back", next(w.conv) == nil)

    -- the host's NPC while the joiner talks
    KCD2MP.w151 = KCD2MP.w151 or {}
    KCD2MP.w151.holdFreeze = false
    KCD2MP.w137.held = {}
    CMDS = {}
    mark = #LOG
    KCD2MP_W137HostHold(true, "ttkc_man_11", 1, "talk")
    check("C18: the joiner talks: the host's NPC is FROZEN for the conversation", CMDS[1] == "wh_ai_PauseNPC ttkc_man_11" and KCD2MP.w137.held["ttkc_man_11"].paused == true
        and lastLog("WO137-HOLD on npc=ttkc_man_11 peer=1 why=talk exec=ok", mark) ~= nil, CMDS[1])
    CMDS = {}
    KCD2MP_W137HostHold(false, "ttkc_man_11", 1, "talk")
    check("C19: and resumed when the talk ends", CMDS[1] == "wh_ai_ResumeNPC ttkc_man_11")
    CMDS = {}
    mark = #LOG
    KCD2MP_W137HostHold(true, "ttkc_man_11", 1, "w139-stop")
    check("C20: a guard's stop stays block-only, and the log says why", #CMDS == 0 and KCD2MP.w137.held["ttkc_man_11"].blockOnly == true
        and lastLog("exec=block-only(why=w139-stop,hold_freeze=off)", mark) ~= nil)
    KCD2MP_W137HostHold(false, "ttkc_man_11", 1, "w139-stop")
    KCD2MP_W160SetConvHold("off")
    CMDS = {}
    mark = #LOG
    KCD2MP_W137HostHold(true, "ttkc_man_11", 1, "talk")
    check("C21: mp_conv_hold off: block-only again, with that reason", #CMDS == 0 and lastLog("exec=block-only(why=talk,hold_freeze=off,mp_conv_hold=off)", mark) ~= nil)
    KCD2MP_W137HostHold(false, "ttkc_man_11", 1, "talk")
    KCD2MP_W160HostTalk(npc, { id = player.id }, "OnTalk")
    check("C22: ... and the host's talk is not told", w.hostTalks["ttkc_man_11"] == nil)
    KCD2MP_W160SetConvHold("on")
    check("C23: the switch is registered", CCMDS["mp_conv_hold"] ~= nil and CCMDS["mp_conv_hold"].body == "KCD2MP_W160SetConvHold(%line)")
    ENTS["ttkc_man_11"] = nil; KCD2MP.npcPuppets["ttkc_man_11"] = nil
    noErrs("C")
end

-- (H) the herb proxy ---------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    local clips = {}
    local ent = { StartAnimation = function(self, layer, name, a, blend, speed, loop) clips[#clips + 1] = { name = name, loop = loop } end }
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    KCD2MP.ghosts["7"] = { entity = ent, istate = { animLoopName = "walk" } }
    local g = KCD2MP.ghosts["7"]
    local mark = #LOG
    check("H1: the proxy starts a plain looped clip on the avatar", KCD2MP_W160HerbProxy(7, true) == true and #clips == 1 and clips[1].name == "herbs_picking_area_loop"
        and clips[1].loop == true and g.istate.herbProxy ~= nil and lastLog("WO160-HERB id=7 on clip=herbs_picking_area_loop", mark) ~= nil)
    check("H2: the locomotion update is held off meanwhile (oneShotUntil)", g.istate.oneShotUntil ~= nil and g.istate.oneShotUntil > NOW)
    check("H3: a second 'on' is nothing", KCD2MP_W160HerbProxy(7, true) == true and #clips == 1)
    NOW = NOW + 1
    check("H4: the hold keeps it off and does not restart the clip within 4 s", KCD2MP_W160HerbHold("7", g, g.istate) == true and #clips == 1)
    NOW = NOW + 4
    check("H5: ... restarts it as a keep-alive after 4 s", KCD2MP_W160HerbHold("7", g, g.istate) == true and #clips == 2)
    mark = #LOG
    KCD2MP_W160HerbProxy(7, false)
    check("H6: off: the proxy is gone, the locomotion loop starts again, the DLL is asked for the T-pose pulse", g.istate.herbProxy == nil and g.istate.animLoopName == nil
        and g.istate.oneShotUntil == nil and countEvt("w160_loopstop", "7 herb clip stopped", mark) == 1 and lastLog("WO160-HERB id=7 off", mark) ~= nil)
    check("H7: not held once off", KCD2MP_W160HerbHold("7", g, g.istate) == false)
    check("H8: no avatar: nothing", KCD2MP_W160HerbProxy(99, true) == false)
    KCD2MP_W160HerbProxy(7, true)
    NOW = NOW + 601
    check("H9: no end ever came: released after 600 s", KCD2MP_W160HerbHold("7", g, g.istate) == false and g.istate.herbProxy == nil)
    check("H10: mp_avatar_herbs is ON by default now", KCD2MP.w143.herbs == true)
    KCD2MP.ghosts["7"] = nil
    noErrs("H")
end

-- (M) the mount gate -----------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    local HID, ACT, HX = false, true, 12
    local horse = { GetWorldPos = function() return { x = HX, y = 0, z = 0 } end, IsHidden = function() return HID end, IsActive = function() return ACT end }
    local ghost = { entity = { human = {}, GetWorldPos = function() return { x = 10, y = 0, z = 0 } end }, istate = {} }
    local again = 0
    local function cb() again = again + 1 end
    local function timers() local n = 0; for _, t in ipairs(TIMERS) do if t.at == NOW then n = n + 1 end end return n end
    local mark = #LOG
    TIMERS = {}
    check("M1: a ready horse: go ahead at once", KCD2MP_W160MountGate(7, ghost, horse, cb) == true and #TIMERS == 0)
    HID = true
    NOW = NOW + 10
    TIMERS = {}
    check("M2: a hidden horse: not yet, polled again in 250 ms", KCD2MP_W160MountGate(7, ghost, horse, cb) == false and #TIMERS == 1 and TIMERS[1].ms == 250)
    NOW = NOW + 1.0
    HID = false
    mark = #LOG
    check("M3: it shows after 1 s: ready, and the wait is logged", KCD2MP_W160MountGate(7, ghost, horse, cb) == true and lastLog("WO160-MOUNT id=7 ready after 1.00 s", mark) ~= nil)
    NOW = NOW + 10
    ACT = false
    TIMERS = {}
    KCD2MP_W160MountGate(7, ghost, horse, cb)
    NOW = NOW + 5.1
    TIMERS = {}
    mark = #LOG
    check("M4: still not ready after 5 s: refused with its reason, retried in 3 s", KCD2MP_W160MountGate(7, ghost, horse, cb) == false and #TIMERS == 1 and TIMERS[1].ms == 3000
        and lastLog("WO160-MOUNT id=7 refused after 5.1 s: horse-inactive", mark) ~= nil)
    ACT = true
    HX = 40
    NOW = NOW + 10
    local ok, why = KCD2MP_W160MountReady(ghost, horse)
    check("M5: a horse 30 m from the rider is not ready (the engine rolls that mount back)", ok == false and why:find("from-the-rider", 1, true) ~= nil, why)
    HX = 12
    ghost.entity.human = nil
    ok, why = KCD2MP_W160MountReady(ghost, horse)
    check("M6: a rider with no body is not ready", ok == false and why == "rider-has-no-body", why)
    ghost.entity.human = {}
    horse.GetWorldPos = function() return { x = 0, y = 0, z = 0 } end
    ok, why = KCD2MP_W160MountReady(ghost, horse)
    check("M7: a horse not streamed in (the origin) is not ready", ok == false and why == "horse-not-streamed-in", why)
    check("M8: the stats count it", KCD2MP.w160.mountStats.ready >= 2 and KCD2MP.w160.mountStats.refused == 1 and KCD2MP.w160.mountStats.waited == 1)
    noErrs("M")
end

OUT = table.concat(RESULTS, "\n")
