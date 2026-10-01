-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-133 synthetic test: the old quest layer is off in a shared world.
--   (a) no gate (solo / mp_shared_world off): the WO-94/96 layer as before --
--       the test prompt shows, F11 fires wh_concept_HasteTrigger, an approach
--       is announced, a divergence enters WAITING_FOR_PEER, a gap toasts
--   (b) the gate on: whatever stood (prompt, parked offer, waiting rows, gaps)
--       is cleared at once
--   (c) with the gate on, no route fires Haste: F11 / F12, confirm, cancel,
--       ui_accept, ui_cancel, dialog_answer1/2 presses, mp_quest_yes / no,
--       mp_quest_fire, mp_quest_test_prompt -- the typed ones answer with the
--       one plain line, on screen too; the game's own handler still runs
--   (d) no approach announcement, no deferred offer, no divergence line, no
--       WAITING_FOR_PEER, no quest-gap toast
--   (e) mp_quest_off stops the quest-gap toasts too (it did not before)
--   (f) the gate off again: the layer behaves as before; (g) the status line
--
-- Driven by Test-WO133Synthetic.ps1 through the WO-77 MoonSharp driver (the
-- real kdcmp.lua spliced in at the marker, engine stubbed, fake clock).
-- What it does NOT prove: that the agent pushes the gate at the right time
-- (Wo133Tests.cs, and the live runs in docs/WO-133-findings.md).
--
-- Part 1 (before the marker): engine stubs + a fake clock (as WO-94's).

NOW = 0                                  -- the fake wall clock, seconds
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}                                -- System.ExecuteCommand strings since a scenario reset
ALLCMDS = {}                             -- every System.ExecuteCommand string of the whole run, never reset
DRAWS = {}                               -- every System.DrawText call since the last reset
ORIG_ONACTION_CALLS = 0                  -- the game's own OnAction handler, chained before ours

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return SPHERE end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s); ALLCMDS[#ALLCMDS + 1] = tostring(s) end
System.DrawText = function(x, y, text, size) DRAWS[#DRAWS + 1] = { x = x, y = y, text = tostring(text) } end
System.RemoveEntity = function(eid)
    for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end
end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end }

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return unpack(r)
end

PPOS = { x = 0, y = 0, z = 0 }
PDEAD = false
player = {
    GetWorldPos   = function() return { x = PPOS.x, y = PPOS.y, z = PPOS.z } end,
    GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
    actor = { GetHealth = function() return 100 end,
              IsDead = function() return PDEAD end,
              IsUnconscious = function() return false end },
    human = { IsWeaponDrawn = function() return false end },
    inventory = { GetMoney = function() return 1000 end },
}
-- The engine's Player class table, so kdcmp.lua installs its OnAction hooks;
-- the pre-existing handler stands in for the game's own.
Player = { Client = { OnAction = function(...) ORIG_ONACTION_CALLS = ORIG_ONACTION_CALLS + 1 end } }

-- @@KDCMP@@

-- Part 2: scenarios.

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function countLog(needle, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do if LOG[i]:find(needle, 1, true) then n = n + 1 end end
    return n
end
local function countEvt(name, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " ", 1, true) then n = n + 1 end
    end
    return n
end
local function haste(fromN)
    local n = 0
    for i = (fromN or 0) + 1, #ALLCMDS do if ALLCMDS[i]:find("wh_concept_HasteTrigger", 1, true) then n = n + 1 end end
    return n
end
local function press(action)
    Player.Client.OnAction(nil, action, "press", 1)
    Player.Client.OnAction(nil, action, "release", 0)
end
local function noErrs(label)
    check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1])
    ERRS = {}
end
local Q = KCD2MP.quest
local function reset()
    Q.enabled = true; Q.level = nil; Q.current = nil
    Q.announced = {}; Q.declined = {}; Q.prompt = nil; Q.pendingPrompt = nil
    Q.fired = {}; Q.lastPromptAt = {}; Q.waiting = {}; Q.gap = {}
    Q.catchup = nil; Q.catchupRemote = {}; Q.lastTickAt = 0; Q.lastPos = nil
    KCD2MP.invite = nil; KCD2MP.interactionMsg = nil
    if KCD2MP.dice then KCD2MP.dice.open = false end
    ERRS = {}; TOASTS = {}
end
local function second() NOW = NOW + 1.0; KCD2MP_QuestProximityTick() end
local OFF = "Quest catch-up is off in a shared world."

-- a fixed-point registry beat, and a second quest to diverge onto
local BQ, BEAT, BX, BY = nil, nil, nil, nil
for _, q in ipairs(KCD2MP_MAINQUESTS) do
    for _, b in ipairs(q.beats or {}) do
        if not b.e and b.x and not BQ then BQ, BEAT, BX, BY = q, q.name .. "." .. b.t, b.x, b.y end
    end
end
local OTHER = nil
for _, q in ipairs(KCD2MP_MAINQUESTS) do if q ~= BQ and q.beats and #q.beats > 0 then OTHER = q; break end end
check("setup: a positioned registry beat and a second quest exist", BQ ~= nil and OTHER ~= nil, BEAT)
check("setup: the plain line is the WO's text", KCD2MP_W133_OFF_TEXT == OFF)
check("the gate starts off (a fresh Lua is solo until the agent says otherwise)", KCD2MP_QuestSharedWorld() == false)

-- ---------------------------------------------------------------------------
-- (a) no gate: as before.
-- ---------------------------------------------------------------------------
do
    reset()
    local c0 = #ALLCMDS
    check("(a) the test prompt shows", KCD2MP_QuestTestPrompt(BEAT) == true and Q.prompt ~= nil)
    press("kcd2mp_dice_bank")
    check("(a) F11 fires wh_concept_HasteTrigger for the beat", haste(c0) == 1 and ALLCMDS[#ALLCMDS] == "wh_concept_HasteTrigger " .. BEAT, ALLCMDS[#ALLCMDS])
    Q.catchup = nil
    local m = #LOG
    Q.current = BQ.name; PPOS.x, PPOS.y = BX, BY
    second()
    check("(a) standing on the beat announces it", countEvt("quest_approach", m) == 1)
    m = #LOG
    local r = KCD2MP_QuestDivergence("7", "Peer", OTHER.name:lower(), "their objective", "our objective", "ahead", "")
    check("(a) a divergence enters WAITING_FOR_PEER", r == "waiting" and countLog("WAITING_FOR_PEER entered", m) == 1, r)
    TOASTS = {}
    r = KCD2MP_QuestObjectiveGap("7", "Peer", BQ.name:lower(), "Title", "objective A", "", "")
    check("(a) a gap toasts", #TOASTS == 1 and countLog("QUEST-GAP with Peer", m) == 1, r)
    noErrs("(a)")
end

-- ---------------------------------------------------------------------------
-- (b) the gate on clears what stood.
-- ---------------------------------------------------------------------------
do
    reset()
    KCD2MP_QuestTestPrompt(BEAT)
    Q.pendingPrompt = { ghostId = "7", who = "Peer", beat = BEAT, reason = "divergence" }
    KCD2MP_QuestDivergence("7", "Peer", OTHER.name:lower(), "their objective", "our objective", "ahead", "")
    Q.gap["7"] = { who = "Peer" }
    local m = #LOG
    check("(b) the agent's gate call returns on", KCD2MP_Wo133Gate(true, "host", "host-shared-world") == true)
    check("(b) the gate reads on", KCD2MP_QuestSharedWorld() == true)
    check("(b) the standing prompt, parked offer, waiting rows and gaps are gone",
        Q.prompt == nil and Q.pendingPrompt == nil and next(Q.waiting) == nil and next(Q.gap) == nil)
    check("(b) one WO133-GATE line says so", countLog("WO133-GATE shared world (host, host-shared-world): the old quest layer is OFF", m) == 1)
    KCD2MP_Wo133Gate(true, "host", "host-shared-world")
    check("(b) a heartbeat of the same state logs nothing", countLog("WO133-GATE", m) == 1)
    noErrs("(b)")
end

-- ---------------------------------------------------------------------------
-- (c) no route fires Haste.
-- ---------------------------------------------------------------------------
do
    reset()
    local c0, m, orig = #ALLCMDS, #LOG, ORIG_ONACTION_CALLS
    check("(c) mp_quest_test_prompt refuses", KCD2MP_QuestTestPrompt(BEAT) == false and Q.prompt == nil)
    check("(c) ...with the plain line", countLog("mp_quest_test_prompt refused: " .. OFF, m) == 1)
    check("(c) ...on screen too", KCD2MP.interactionMsg ~= nil and KCD2MP.interactionMsg.text == OFF)
    check("(c) a prompt cannot be raised by the agent path either", KCD2MP_QuestShowPrompt("7", "Peer", BEAT, 1, "divergence") == false and Q.prompt == nil)
    -- a prompt forced into the table anyway (an older path, a race): the keys still do nothing
    local keys = { "kcd2mp_dice_bank", "kcd2mp_dice_yield", "confirm", "cancel", "ui_accept", "ui_cancel", "dialog_answer1", "dialog_answer2" }
    for _, k in ipairs(keys) do
        Q.prompt = { ghostId = "7", who = "Peer", beat = BEAT, title = "t", shownAt = NOW, reason = "divergence" }
        press(k)
    end
    check("(c) F11/F12/confirm/cancel/ui_accept/ui_cancel/dialog_answer1/2: no Haste command", haste(c0) == 0, tostring(haste(c0)))
    check("(c) ...no QUEST-CATCHUP FIRE line", countLog("QUEST-CATCHUP FIRE", m) == 0)
    check("(c) ...no decline recorded either", next(Q.declined) == nil)
    check("(c) the game's own handler ran on every press", ORIG_ONACTION_CALLS - orig == 2 * #keys, ORIG_ONACTION_CALLS - orig)
    m = #LOG
    check("(c) mp_quest_fire refuses", KCD2MP_QuestFire(BEAT) == false and haste(c0) == 0)
    check("(c) ...with the plain line", countLog("mp_quest_fire refused: " .. OFF, m) == 1)
    check("(c) ...and opens no hazard window", Q.catchup == nil and next(Q.fired) == nil)
    m = #LOG
    Q.prompt = { ghostId = "7", who = "Peer", beat = BEAT, title = "t", shownAt = NOW, reason = "divergence" }
    check("(c) mp_quest_yes refuses", KCD2MP_QuestAnswer(true) == false and haste(c0) == 0)
    check("(c) mp_quest_no refuses", KCD2MP_QuestAnswer(false) == false and next(Q.declined) == nil)
    check("(c) ...each with the plain line", countLog("mp_quest_yes refused: " .. OFF, m) == 1
        and countLog("mp_quest_no refused: " .. OFF, m) == 1)
    check("(c) a refused answer leaves no prompt behind", Q.prompt == nil)
    noErrs("(c)")
end

-- ---------------------------------------------------------------------------
-- (d) no announcements, divergence, waiting or gap toasts.
-- ---------------------------------------------------------------------------
do
    reset()
    local m = #LOG
    Q.current = BQ.name; PPOS.x, PPOS.y = BX, BY
    for _ = 1, 5 do second() end
    check("(d) standing on a beat for 5 s announces nothing", countEvt("quest_approach", m) == 0 and countLog("QUEST-APPROACH", m) == 0)
    Q.waiting["7"] = { who = "Peer", rel = "behind", key = "k", pendingBeat = BEAT, since = NOW }
    Q.lastPromptAt["7"] = -1e9
    second()
    check("(d) a deferred offer is never raised", Q.prompt == nil)
    Q.waiting = {}
    local r = KCD2MP_QuestDivergence("7", "Peer", OTHER.name:lower(), "their objective", "our objective", "behind", BEAT)
    check("(d) a divergence is refused", r == "shared-world" and next(Q.waiting) == nil and Q.prompt == nil, r)
    check("(d) ...no QUEST-DIVERGENCE or WAITING_FOR_PEER line, no toast",
        countLog("QUEST-DIVERGENCE #", m) == 0 and countLog("WAITING_FOR_PEER", m) == 0 and #TOASTS == 0)
    r = KCD2MP_QuestObjectiveGap("7", "Peer", BQ.name:lower(), "Title", "objective A", "", "")
    check("(d) a gap is refused, no toast, no QUEST-GAP line", r == "shared-world" and #TOASTS == 0 and countLog("QUEST-GAP with", m) == 0, r)
    check("(d) the refusals are logged (throttled), never silent",
        countLog("WO133-OFF QUEST-DIVERGENCE refused", m) == 1 and countLog("WO133-OFF QUEST-GAP refused", m) == 1)
    noErrs("(d)")
end

-- ---------------------------------------------------------------------------
-- (e) mp_quest_off stops the gap toasts (outside a shared world).
-- ---------------------------------------------------------------------------
do
    KCD2MP_Wo133Gate(false, "none", "no-session")
    reset()
    KCD2MP_QuestSetSync("off")
    local m = #LOG
    local r = KCD2MP_QuestObjectiveGap("7", "Peer", BQ.name:lower(), "Title", "objective A", "", "")
    check("(e) mp_quest_off: a gap is ignored, no toast", r == "off" and #TOASTS == 0 and countLog("QUEST-GAP with", m) == 0, r)
    check("(e) ...and says so once", countLog("QUEST-GAP ignored (mp_quest_sync off)", m) == 1)
    KCD2MP_QuestSetSync("on")
    noErrs("(e)")
end

-- ---------------------------------------------------------------------------
-- (f) the gate off again: as before. (g) the status line.
-- ---------------------------------------------------------------------------
do
    reset()
    local m = #LOG
    KCD2MP_Wo133Gate(true, "joiner", "host-announced-shared-world")
    KCD2MP_Wo133Gate(false, "none", "no-session")
    check("(f) the gate off logs the way back", countLog("WO133-GATE no shared world (no-session)", m) == 1 and KCD2MP_QuestSharedWorld() == false)
    local c0 = #ALLCMDS
    check("(f) the test prompt shows again", KCD2MP_QuestTestPrompt(BEAT) == true)
    press("kcd2mp_dice_bank")
    check("(f) F11 fires again", haste(c0) == 1)
    Q.catchup = nil
    m = #LOG
    Q.current = BQ.name; Q.announced = {}; PPOS.x, PPOS.y = BX, BY
    second()
    check("(f) the approach is announced again", countEvt("quest_approach", m) == 1)
    KCD2MP_QuestSetSync("")
    check("(g) without the gate the status line carries no shared-world note", countLog("OFF in this shared world", m) == 0)
    KCD2MP_Wo133Gate(true, "host", "host-shared-world")
    KCD2MP_QuestSetSync("")
    check("(g) with the gate on, mp_quest_sync says the layer is off here", countLog("but OFF in this shared world (WO-133)", m) == 1)
    KCD2MP_Wo133Gate(false, "none", "no-session")
    noErrs("(f)")
end

OUT = table.concat(RESULTS, "\n")
