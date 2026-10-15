-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-167 synthetic test (the game-side halves), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock).
--   (T) T1 the generic request-fallback is off by default; on, never within 3 s of a dialogue's end and never to the NPC just talked
--       to; a press inside the last talk's end window is a fresh talk with no re-pause; the WO-164 retry waits 3 s after a dialogue
--   (H) T2 the host's conversation with an NPC ends: its copy is swept, a combat/alert state is ended (only when the host's NPC is not
--       fighting), its shop contexts are read, and the next talk resumes it first; the host's collision bark by a partner's figure
--   (N) T3 the timeout line names the copy's readiness; NOHOOK names a copy stood in front of whose talk was never pressed
--   (S) T4 a released dialogue scene: the faders back, the player stood up, told; the faders restored after a lifted black
--   (W) the switches and the status line
-- Driven by Test-WO167Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-167-findings.md.

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
System.GetEntitiesInSphere = function(p, r) return {} end
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
UIAction.RegisterElementListener = function(tbl, el, inst, ev, fn) LISTENERS[#LISTENERS + 1] = { tbl = tbl, el = el, ev = ev, fn = fn } end
Calendar = mkstub()
Calendar.GetWorldTime = function() return 1000000 end
XGenAIModule = mkstub()
MSGS = {}; ENDCOMBAT = {}
XGenAIModule.MakeTableFromType = function(t) return { kind = t } end
XGenAIModule.SendMessageToEntityData = function(to, kind, t) MSGS[#MSGS + 1] = { to = to, kind = kind, t = t } end
XGenAIModule.TryEndCombat = function(w) ENDCOMBAT[#ENDCOMBAT + 1] = w; return true end
EnvironmentModule = { GetRainIntensity = function() return 0 end }
ItemManager = mkstub()

NEXTID = 5000
PX, PY, PZ = 100, 100, 10
IN_DIALOG = false
ORIG_TALKS = 0
P_DANGER = false
BasicAIActions = { OnTalk = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end,
                   OnChatWithFocus = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end }
player = { id = 1, class = "Player" }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PX, y = PY, z = PZ } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.GetDirectionVector = function() return { x = 1, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end, StandUp = function() end }
player.human = { IsInDialog = function() return IN_DIALOG end }
player.soul = { RestrictDialog = function() end, GetState = function() return 0 end,
                IsInCombatDanger = function() return P_DANGER end, IsInTenseCircumstance = function() return false end }
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
local function lastEvt(name, fromLog)
    for i = #LOG, (fromLog or 0) + 1, -1 do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " ", 1, true) then return l end
    end
    return nil
end
local function cmdCount(prefix)
    local n = 0
    for _, c in ipairs(CMDS) do if c:sub(1, #prefix) == prefix then n = n + 1 end end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
local function runTimers()
    local t = TIMERS
    TIMERS = {}
    for _, x in ipairs(t) do x.f() end
end

-- a host copy: a puppet the host streams (soul / actor / human binds as the game has them; flags settable)
local function copy(name, x, y, opts)
    opts = opts or {}
    NEXTID = NEXTID + 1
    local e = { id = NEXTID, class = "NPC", this = { id = "wuid" .. name } }
    e.GetName = function() return name end
    e.GetWorldPos = function() return { x = x, y = y, z = PZ } end
    e.actor = { IsDead = function() return false end, CanTalk = function() return opts.canTalk ~= false end }
    e.human = { IsInDialog = function() return opts.inDialog == true end, IsWeaponDrawn = function() return false end,
                InterruptDialogs = function() opts.interrupted = (opts.interrupted or 0) + 1 end }
    e.soul = { RestrictDialog = function() end,
               IsInCombatMode = function() return opts.combat == true end,
               IsInCombatDanger = function() return opts.danger == true end,
               IsDialogRestricted = function() return opts.restricted == true end,
               HasScriptContext = function(_, c) return opts.ctx ~= nil and opts.ctx[c] == true end }
    -- the game's own prompt rules (BasicAIActions:ActorCanTalk / GetCanTalkHintType) are methods of every NPC entity
    e.ActorCanTalk = function(self, user) return self.actor:CanTalk(user.id) and not self.soul:IsInCombatMode() end
    e.GetCanTalkHintType = function(self)
        if self.soul:IsDialogRestricted(player.id) then return false end
        if player.soul:IsInCombatDanger() or player.soul:IsInTenseCircumstance() then return false end
        return true
    end
    e.opts = opts
    ENTS[name] = e
    KCD2MP.npcPuppets[name] = {}
    return e
end
local function joiner()
    KCD2MP.hitSensorOn = false
    KCD2MP.wo102.authorityHost = true; KCD2MP.wo102.authorityPause = true
    KCD2MP.w137.joiner, KCD2MP.w137.active, KCD2MP.w137.host, KCD2MP.w137.sync, KCD2MP.w137.talkOn = true, true, false, true, true
    KCD2MP.w137.aliveAt = NOW
    pcall(KCD2MP_W137InstallTalk)
end
local function host()
    KCD2MP.w137.joiner, KCD2MP.w137.active, KCD2MP.w137.host = false, true, true
end
local function reset()
    for n in pairs(ENTS) do ENTS[n] = nil end
    ENTS["Dude"] = player
    PX, PY, PZ = 100, 100, 10
    KCD2MP.npcPuppets = {}
    KCD2MP._npcPaused = {}
    KCD2MP.w137.talking = {}
    KCD2MP.w137.ownTalkAt = nil
    KCD2MP.w166.talk.lastDlgEndAt = nil
    KCD2MP.w164.lastEnd = {}; KCD2MP.w164.hostCombat = {}; KCD2MP.w164.sweptAt = {}
    KCD2MP.w160.conv = {}
    local W = KCD2MP.w167
    W.talkFallback = false
    W.talk.lastNpc = {}; W.talk.fresh = {}; W.talk.focus = nil; W.talk.nohookAt = {}; W.talk.fbLogAt = -1e9
    W.scene.faderHeldAt = nil
    IN_DIALOG = false; P_DANGER = false
    TOASTS = {}; TIMERS = {}; CMDS = {}; MSGS = {}; ENDCOMBAT = {}; CVARS = {}
end

-- (T) T1 ---------------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    reset(); joiner()
    copy("tzel_woman_10", 101, 100)
    KCD2MP._npcPaused["tzel_woman_10"] = NOW
    local mark = #LOG
    KCD2MP_W137TalkRequest(184)
    check("T1: the generic request-fallback is off by default: a request no talk action made attaches to nobody",
        KCD2MP.w137.talking["tzel_woman_10"] == nil and cmdCount("wh_ai_ResumeNPC") == 0 and KCD2MP.w167.talkFallback == false)
    check("T2: ... and the line says so (once a minute)", lastLog("WO167-TALK fallback-off id=184 nearest=tzel_woman_10", mark) ~= nil)
    mark = #LOG
    NOW = NOW + 5
    KCD2MP_W137TalkRequest(185)
    check("T3: ... the second within the minute is counted, not logged", countLog("WO167-TALK fallback-off", mark) == 0 and KCD2MP.w167.talk.stats.fallbackOff >= 2)
    -- the 09:07:56 case: a solo request of the player's own (Henry's line) never resumes the copy in reach
    check("T4: the field's solo request (09:07:56): the copy stays paused", KCD2MP._npcPaused["tzel_woman_10"] ~= nil)
    -- switched on: WO-166's rules plus the NPC just talked to
    KCD2MP_W167SetTalkFallback("on")
    KCD2MP_W137DialogEnd(166, "Dude tzel_woman_10")
    NOW = NOW + 4   -- past WO-166's 3 s
    mark = #LOG
    KCD2MP_W137TalkRequest(186)
    check("T5: on: never to the NPC whose dialogue with this player just ended (10 s)", KCD2MP.w137.talking["tzel_woman_10"] == nil
        and lastLog("WO166-TALK own-request suppressed via=request-fallback npc=tzel_woman_10 id=186 why=last-npc", mark) ~= nil)
    KCD2MP_W137DialogEnd(167, "Dude tzel_woman_11")
    NOW = NOW + 1
    copy("tzel_woman_9", 100.5, 100)
    mark = #LOG
    KCD2MP_W137TalkRequest(187)
    check("T6: on: never within 3 s after any dialogue ended", KCD2MP.w137.talking["tzel_woman_9"] == nil and lastLog("why=after-dialogue", mark) ~= nil)
    NOW = NOW + 11
    KCD2MP_W137TalkRequest(188)
    check("T7: on, and nothing in the way: it attaches (the switch is real)", KCD2MP.w137.talking["tzel_woman_9"] ~= nil
        and KCD2MP.w137.talking["tzel_woman_9"].via == "request-fallback")
    KCD2MP_W167SetTalkFallback("off")
    noErrs("T-a")
end
do
    -- the 09:00:17 sequence: the shop dialogue ends, the player presses again inside the 2.5 s end window
    ERRS = {}
    reset(); joiner()
    copy("tzel_woman_10", 101, 100)
    KCD2MP._npcPaused["tzel_woman_10"] = NOW
    BasicAIActions.OnTalk(ENTS["tzel_woman_10"], player, 1)   -- resume first (WO-166), the press 200 ms later
    runTimers()
    KCD2MP_W137TalkRequest(166)
    KCD2MP_W137TalkAttempt(166, "Dude tzel_woman_10")
    IN_DIALOG = true; NOW = NOW + 1; KCD2MP_W137TalkTick()
    check("T8: the first talk started on the resumed copy", KCD2MP.w137.talking["tzel_woman_10"].started == true and KCD2MP._npcPaused["tzel_woman_10"] == nil)
    IN_DIALOG = false
    KCD2MP_W137DialogEnd(166, "Dude tzel_woman_10")
    check("T9: its end waits in the 2.5 s window (WO-144)", KCD2MP.w137.talking["tzel_woman_10"].endAt ~= nil)
    NOW = NOW + 1
    CMDS = {}
    local before = ORIG_TALKS
    local mark = #LOG
    BasicAIActions.OnTalk(ENTS["tzel_woman_10"], player, 1)   -- pressed again
    runTimers()
    local t = KCD2MP.w137.talking["tzel_woman_10"]
    check("T10: a press inside the end window is a fresh talk", t ~= nil and t.endAt == nil and t.started ~= true and t.id == nil
        and lastLog("WO167-TALK restart npc=tzel_woman_10 via=OnTalk", mark) ~= nil, lastLog("WO167-TALK", mark))
    check("T11: ... the copy is NOT paused in between (the field: the old end paused it right after the new request)",
        cmdCount("wh_ai_PauseNPC tzel_woman_10") == 0 and KCD2MP._npcPaused["tzel_woman_10"] == nil, CMDS[1])
    check("T12: ... the old talk is ended for the agent, the game's own talk runs once", countEvt("w164_talk", "end tzel_woman_10 talked-again", mark) == 1
        and ORIG_TALKS == before + 1)
    KCD2MP_W137TalkRequest(184)
    check("T13: ... and the new request attaches to the player's own talk (no fallback, none needed)", KCD2MP.w137.talking["tzel_woman_10"].id == 184
        and KCD2MP.w137.talking["tzel_woman_10"].via == "OnTalk")
    -- the WO-164 retry waits until 3 s after the dialogue's end
    KCD2MP.w166.talk.lastDlgEndAt = NOW
    NOW = NOW + 4.5   -- the press is 4.5 s old: due, but a dialogue ... (set just now) ended under 3 s ago? no: 4.5 s ago
    KCD2MP.w166.talk.lastDlgEndAt = NOW - 1.0   -- a dialogue ended 1 s ago
    local rb = KCD2MP.w164.stats.retries
    mark = #LOG
    pcall(KCD2MP_W164Frame)
    check("T14: the WO-164 retry waits within 3 s of a dialogue's end", KCD2MP.w164.stats.retries == rb and lastLog("WO167-TALK retry-held npc=tzel_woman_10", mark) ~= nil)
    NOW = NOW + 2.5
    pcall(KCD2MP_W164Frame)
    check("T15: ... and runs after (the player's own press, once)", KCD2MP.w164.stats.retries == rb + 1)
    noErrs("T-b")
end

-- (H) T2 ---------------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    reset(); joiner()
    local e = copy("tzel_woman_10", 101, 100, { combat = true, ctx = { shop_sellerReadyToSell = true } })
    KCD2MP._npcPaused["tzel_woman_10"] = NOW
    KCD2MP_W160Conversation(true, "tzel_woman_10")
    check("H1: the host talks to her: her copy is held (WO-160)", KCD2MP.w160.conv["tzel_woman_10"] ~= nil)
    NOW = NOW + 8
    local mark = #LOG
    KCD2MP_W160Conversation(false, "tzel_woman_10")
    check("H2: the host's trade ends: the copy is swept (release + neutral)", countEvt("w164_sweep", "tzel_woman_10 hostconv 20", mark) == 1)
    check("H3: ... a combat state on the copy is ended the game's own way while the host's NPC is not fighting",
        ENDCOMBAT[1] == "wuidtzel_woman_10" and MSGS[1] ~= nil and MSGS[1].kind == "stopFight" and countEvt("w167_clear", "tzel_woman_10", mark) == 1)
    check("H4: ... the line names it, the shop context read (it stays: her Trade option)", lastLog("WO167-T2 npc=tzel_woman_10 swept=true combat=true danger=false host_fighting=false ended=try_end_combat=true,stop_fight=", mark) ~= nil
        and lastLog("shop=shop_sellerReadyToSell", mark) ~= nil, lastLog("WO167-T2", mark))
    -- the next talk resumes it first even though the mod thinks it runs
    KCD2MP._npcPaused["tzel_woman_10"] = nil
    CMDS = {}
    local before = ORIG_TALKS
    mark = #LOG
    BasicAIActions.OnTalk(e, player, 1)
    check("H5: the next press resumes the copy first although it is not paused here", cmdCount("wh_ai_ResumeNPC tzel_woman_10") == 1
        and lastLog("WO167-T2 fresh-resume npc=tzel_woman_10", mark) ~= nil and ORIG_TALKS == before)
    runTimers()
    check("H6: ... then the press goes on (200 ms)", ORIG_TALKS == before + 1)
    noErrs("H-a")
end
do
    ERRS = {}
    reset(); joiner()
    copy("tbuk_guard_1", 101, 100, { combat = true })
    KCD2MP.w164.hostCombat["tbuk_guard_1"] = NOW   -- the host's NPC fights
    KCD2MP_W160Conversation(true, "tbuk_guard_1")
    NOW = NOW + 2
    local mark = #LOG
    KCD2MP_W160Conversation(false, "tbuk_guard_1")
    -- WO-191 2: and it is not swept either (a copy whose host NPC fights is never reset, whatever the trigger)
    check("H7: a copy whose host NPC is fighting keeps its combat state (and is not swept: WO-191)", #ENDCOMBAT == 0 and #MSGS == 0 and countEvt("w167_clear", "", mark) == 0
        and lastLog("WO167-T2 npc=tbuk_guard_1 swept=false combat=true danger=false host_fighting=true ended=none", mark) ~= nil, lastLog("WO167-T2", mark))
    reset(); joiner()
    copy("tzel_man_10", 101, 100)
    KCD2MP_W160Conversation(true, "tzel_man_10")
    mark = #LOG
    KCD2MP_W160Conversation(false, "tzel_man_10")
    check("H8: a calm copy: swept, nothing ended", #ENDCOMBAT == 0 and lastLog("WO167-T2 npc=tzel_man_10 swept=true combat=false danger=false host_fighting=false ended=none shop=-", mark) ~= nil)
    NOW = NOW + 130
    CMDS = {}
    KCD2MP._npcPaused["tzel_man_10"] = nil
    BasicAIActions.OnTalk(ENTS["tzel_man_10"], player, 1)
    check("H9: the fresh resume lapses after 2 minutes", cmdCount("wh_ai_ResumeNPC") == 0)
    noErrs("H-b")
end
do
    -- host: who bumped her?
    ERRS = {}
    reset(); host()
    local e = copy("tzel_woman_10", 100.6, 100)
    KCD2MP.npcPuppets["tzel_woman_10"] = nil   -- the host's own NPC
    local fig = { id = 9001, GetName = function() return "kcd2mp_1" end, GetWorldPos = function() return { x = 112, y = 100, z = PZ } end }
    ENTS["kcd2mp_1"] = fig
    local mark = #LOG
    KCD2MP_W167Bump("tzel_woman_10")
    check("H10: the field's bark: the host's own Henry 0.6 m away, the figure 12 m: the game's reaction stands",
        lastLog("WO167-BUMP npc=tzel_woman_10 by=host d_host=0.60", mark) ~= nil and (e.opts.interrupted or 0) == 0, lastLog("WO167-BUMP", mark))
    PX = 120
    fig.GetWorldPos = function() return { x = 101.4, y = 100, z = PZ } end
    mark = #LOG
    KCD2MP_W167Bump("tzel_woman_10")
    check("H11: a partner's figure bumped her: her reaction is ended (she is not bumped by this player)",
        lastLog("WO167-BUMP npc=tzel_woman_10 by=figure", mark) ~= nil and e.opts.interrupted == 1 and KCD2MP.w167.talk.stats.bumpsFigure == 1, lastLog("WO167-BUMP", mark))
    noErrs("H-c")
end

-- (N) T3 ---------------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    reset(); joiner()
    copy("tzel_rowdy_3", 101.5, 100, { canTalk = false })   -- the dice player: the game offers no Talk
    local mark = #LOG
    KCD2MP_W167FocusTick(NOW)
    NOW = NOW + 0.5; KCD2MP_W167FocusTick(NOW)
    NOW = NOW + 0.75; KCD2MP_W167FocusTick(NOW)
    check("N1: stood 1.25 s in front of him: nothing yet (the focus has not ended)", countLog("WO167-NOHOOK", mark) == 0)
    PX = 90   -- walked away
    NOW = NOW + 0.25; KCD2MP_W167FocusTick(NOW)
    check("N2: ... the focus ends without a press: NOHOOK names why the game offered no Talk",
        lastLog("WO167-NOHOOK npc=tzel_rowdy_3 d=1.5", mark) ~= nil and lastLog("prompt=no reason=can-talk-false", mark) ~= nil, lastLog("WO167-NOHOOK", mark))
    -- a press that reaches the hook: no line
    reset(); joiner()
    copy("tzel_woman_11", 101.5, 100)
    mark = #LOG
    KCD2MP_W167FocusTick(NOW); NOW = NOW + 1.2; KCD2MP_W167FocusTick(NOW)
    BasicAIActions.OnTalk(ENTS["tzel_woman_11"], player, 1)
    KCD2MP.w137.talking = {}
    PX = 90; NOW = NOW + 0.25; KCD2MP_W167FocusTick(NOW)
    check("N3: a press that reached the mod: no NOHOOK line", countLog("WO167-NOHOOK", mark) == 0)
    -- walking past (not standing still): no line
    reset(); joiner()
    copy("tzel_man_4", 102, 100)
    mark = #LOG
    KCD2MP_W167FocusTick(NOW); PX = 101.2; NOW = NOW + 1.2; KCD2MP_W167FocusTick(NOW)
    PX = 104; NOW = NOW + 0.25; KCD2MP_W167FocusTick(NOW)
    check("N4: walking past a copy is not standing in front of it: no line", countLog("WO167-NOHOOK", mark) == 0)
    -- a long stare is told once after 6 s, with the readiness: the player in combat danger greys the hint
    reset(); joiner()
    copy("tzel_david", 101, 100)
    P_DANGER = true
    mark = #LOG
    for _ = 1, 30 do KCD2MP_W167FocusTick(NOW); NOW = NOW + 0.25 end
    check("N5: a 7 s stare is told once, the prompt greyed by the player's own combat danger",
        countLog("WO167-NOHOOK npc=tzel_david", mark) == 1 and lastLog("prompt=no reason=player-combat-danger", mark) ~= nil, lastLog("WO167-NOHOOK", mark))
    noErrs("N-a")
end
do
    ERRS = {}
    reset(); joiner()
    copy("tzel_woman_10", 101, 100, { restricted = true, ctx = { shop_sellerReadyToSell = true } })
    KCD2MP.w137.talking["tzel_woman_10"] = { since = NOW, via = "OnTalk", resumed = true, id = 609 }
    KCD2MP_W167FocusTick(NOW)
    NOW = NOW + 30
    local mark = #LOG
    KCD2MP_W137TalkEnd("tzel_woman_10", "never-started")
    local ev = lastEvt("w166_talkstate", mark) or ""
    check("N6: the timeout line names the copy's readiness: the prompt rule, CanTalk, restricted, shop, the player's minigame, the faced copy",
        ev:find("tzel_woman_10 paused=0 copy_dialog=0 dead=0 resumed_for_talk=1", 1, true) ~= nil
        and ev:find("prompt=no reason=dialog-restricted combat=0 danger=0 can_talk=1 restricted=1 shop=1", 1, true) ~= nil
        and ev:find("player_minigame=0 target=", 1, true) ~= nil, ev)
    noErrs("N-b")
end

-- (S) T4 ---------------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    reset(); joiner()
    CVARS["wh_ui_FaderSuspend"] = 1   -- the field: the player lifted the black himself
    local mark = #LOG
    KCD2MP_W167SceneReleased(true, "stuck-20s")
    check("S1: the engine ended the scene: the faders back to normal (the player's suspend undone)", tonumber(CVARS["wh_ui_FaderSuspend"]) == 0)
    check("S2: ... the player stood up (the unstuck step 1)", cmdCount("wh_ai_NPCStateResetElement Dude Unstance") == 1 and cmdCount("wh_ai_NPCStateResetElement Dude Stance") == 1)
    check("S3: ... and told", TOASTS[#TOASTS] == "That scene couldn't play here \226\128\148 your partner's world runs it."
        and lastLog("WO167-SCENE released ended=true fader=restored stood=asked why=stuck-20s", mark) ~= nil, TOASTS[#TOASTS])
    reset(); joiner()
    mark = #LOG
    KCD2MP_W167SceneReleased(false, "not-ended")
    check("S4: the engine did not end it: the black is lifted (wh_ui_FaderSuspend 1)", tonumber(CVARS["wh_ui_FaderSuspend"]) == 1
        and lastLog("WO167-SCENE released ended=false fader=suspended", mark) ~= nil)
    check("S5: ... and given back when the scene ends", KCD2MP_W167FaderRestore("scene-ended") == true and tonumber(CVARS["wh_ui_FaderSuspend"]) == 0
        and KCD2MP_W167FaderRestore("again") == false)
    noErrs("S")
end

-- (C) Phase 2: C1 the stand-in's pulse is asked, C2 the weapon a copy holds -------------------------------------------------
local SWORD = "1fc42528-2bef-4dde-bf8a-04febeef41c8"
local function armedCopy(name)
    local e = copy(name, 101, 100)
    local inv = { items = {}, created = {} }
    inv.FindItem = function(_, cls) return inv.items[cls] end
    inv.CreateItem = function(_, cls) inv.created[#inv.created + 1] = cls; inv.items[cls] = "item:" .. cls end
    e.inventory = inv
    e.drawnFrom, e.draws, e.holsters = {}, 0, 0
    e.human.DrawFromInventory = function(_, it) e.drawnFrom[#e.drawnFrom + 1] = it end
    e.human.DrawWeapon = function() e.draws = e.draws + 1 end
    e.human.HolsterWeapon = function() e.holsters = e.holsters + 1 end
    return e
end
do
    ERRS = {}
    reset(); joiner()
    local mark = #LOG
    KCD2MP_W167StandInSpawned("prepadeniNaCeste_bandit_7", {})
    check("C1: a stand-in's spawn is told to the agent (its walk pulse once bound)", countEvt("w167_standin", "prepadeniNaCeste_bandit_7", mark) == 1)
    local e = armedCopy("prepadeniNaCeste_bandit_7")
    mark = #LOG
    KCD2MP_W167Weapon("prepadeniNaCeste_bandit_7", SWORD, "hands")
    check("C2: the host's NPC holds a sword the stand-in never had: created in its inventory, drawn from it",
        e.inventory.created[1] == SWORD and e.drawnFrom[1] == "item:" .. SWORD and e.draws == 0
        and lastLog("WO167-WEAPON npc=prepadeniNaCeste_bandit_7 hand=1fc42528 owned=created drawn=this-class why=hands", mark) ~= nil, lastLog("WO167-WEAPON", mark))
    KCD2MP_W167Weapon("prepadeniNaCeste_bandit_7", SWORD, "row")
    KCD2MP_W167Weapon("prepadeniNaCeste_bandit_7", "?", "row")
    check("C3: held: the same weapon again (a row, the hands) draws nothing again", #e.drawnFrom == 1 and #e.inventory.created == 1 and e.draws == 0)
    KCD2MP_W167Weapon("prepadeniNaCeste_bandit_7", "-", "hands")
    KCD2MP_W167Weapon("prepadeniNaCeste_bandit_7", "-", "row")
    check("C4: nothing in the host NPC's hand: holstered once", e.holsters == 1 and KCD2MP.npcPuppets["prepadeniNaCeste_bandit_7"].appliedDrawn == false)
    KCD2MP_W167Weapon("prepadeniNaCeste_bandit_7", "?", "row")
    check("C5: an armed row with the class known from before: the same sword, from its own inventory", #e.drawnFrom == 2 and #e.inventory.created == 1)
    check("C6: C2 holds the hand: the stream's drawn flag and the old re-assert leave the copy alone", KCD2MP_W167WeaponHeld("prepadeniNaCeste_bandit_7", NOW) == true
        and KCD2MP_W167WeaponHeld("prepadeniNaCeste_bandit_7", NOW + 20) == false)
    local u = armedCopy("tzel_rowdy_2")
    KCD2MP_W167Weapon("tzel_rowdy_2", "?", "row")
    check("C7: armed, no class known anywhere: the copy draws what it has (the game's DrawWeapon)", u.draws == 1 and #u.drawnFrom == 0)
    KCD2MP_W167SetCopyWeapons("off")
    local v = armedCopy("tzel_rowdy_4")
    check("C8: mp_copy_weapons off: nothing is touched", KCD2MP_W167Weapon("tzel_rowdy_4", SWORD, "hands") == false and v.draws == 0 and #v.drawnFrom == 0)
    KCD2MP_W167SetCopyWeapons("on")
    -- the soak's stale mark: a held weapon on a body that is no longer the one it was set on
    local w = armedCopy("tzel_rowdy_6")
    KCD2MP_W167Weapon("tzel_rowdy_6", SWORD, "hands")
    check("C9: held on this body", #w.drawnFrom == 1)
    local w2 = armedCopy("tzel_rowdy_6")   -- the same name, a new body and puppet record (re-streamed)
    KCD2MP_W167Weapon("tzel_rowdy_6", SWORD, "hands")
    check("C10: a new body under the same name: the host's weapon is drawn again (never 'held already')", #w2.drawnFrom == 1)
    KCD2MP_W167Weapon("tzel_rowdy_6", SWORD, "hands")
    check("C11: ...and then held on it", #w2.drawnFrom == 1)
    KCD2MP.w137.aliveAt = nil
    KCD2MP_W137Session(false, true, true)   -- a fresh session (the agent reconnected)
    KCD2MP_W167Weapon("tzel_rowdy_6", SWORD, "hands")
    check("C12: a fresh session: nothing a copy held before is trusted", #w2.drawnFrom == 2)
    noErrs("C")
end

-- (G) the standing rule: a WO-167 action the engine throws on is counted, its site logged, its feature off for the session, said once
do
    ERRS = {}
    reset(); joiner()
    local e = armedCopy("prepadeniNaCeste_bandit_9")
    e.human.HolsterWeapon = function() e.holsters = e.holsters + 1; error("engine refused") end
    KCD2MP_W167Weapon("prepadeniNaCeste_bandit_9", SWORD, "hands")   -- drawn
    local mark = #LOG
    local toasts = #TOASTS
    KCD2MP_W167Weapon("prepadeniNaCeste_bandit_9", "-", "hands")     -- the holster throws
    check("G1: the engine threw on a copy's holster: counted, its site logged, the agent told",
        lastLog("WO167-FAULT site=c2-holster feature=copy-weapons", mark) ~= nil and countEvt("w167_fault", "copy-weapons c2-holster", mark) == 1
        and KCD2MP.w167.faults.n == 1, lastLog("WO167-FAULT", mark))
    check("G2: ...the feature is off for the session and said on screen once", KCD2MP.w167.copyWeapons == false
        and TOASTS[#TOASTS] == "Enemy weapon copying is off this session (a game error)." and #TOASTS == toasts + 1)
    local before = e.holsters
    KCD2MP.w167.copyWeapons = true   -- even switched back on by hand, a faulted feature stays off this session
    KCD2MP_W167Weapon("prepadeniNaCeste_bandit_9", SWORD, "hands")
    KCD2MP_W167Weapon("prepadeniNaCeste_bandit_9", "-", "hands")
    check("G3: ...never tried again, never said again", e.holsters == before and #TOASTS == toasts + 1)
    KCD2MP_W167Status()
    check("G4: the status line names the fault", lastLog("WO167-STATUS faults=1 off=copy-weapons") ~= nil, lastLog("WO167-STATUS faults"))
    check("G: the only Lua error is the one the test threw", #ERRS == 1 and tostring(ERRS[1]):find("engine refused", 1, true) ~= nil, ERRS[1])
    KCD2MP.w167.faults = { n = 0, off = {} }
    KCD2MP.w167.copyWeapons = true
end

-- (F) Phase 3: one shared fast travel ---------------------------------------------------------------------------------------
local M154 = KCD2MP.w154menu
local function session154(role, partner, ft)
    KCD2MP_W154MenuSession(role, partner, ft, nil, nil, "connected", "0.48.5", false, false)
end
do
    ERRS = {}
    reset()
    local F = KCD2MP.w167.ft
    F.mapAt, F.hiId, F.hiAt, F.travelling = -1e9, nil, -1e9, false
    check("F1: fast travel is ON by default in a session (the host's switch)", M154.fastTravel == true)
    -- the host turns it off: the engine's switch is held only while a player picks on the travel map
    CVARS["wh_pl_FastTravelEnabled"] = 1
    M154.fastTravel = false
    session154("host", true)
    check("F2: off, nobody picking: the switch is NOT held -- a quest's own forced travel goes through", tonumber(CVARS["wh_pl_FastTravelEnabled"]) == 1 and M154.ftHeld ~= true)
    check("F3: ...and the host is told once that travel is off", TOASTS[#TOASTS] == M154.TEXT_FT_HOST)
    KCD2MP.w167.OnFtHighlight(KCD2MP.w167, "ApseMap", -1, "OnHighlightFastTravelPoint", { [0] = "fastTravel_trosecko", [1] = "Troskowitz" })
    check("F4: a travel point highlighted on the map: held now (before any double click)", tonumber(CVARS["wh_pl_FastTravelEnabled"]) == 0 and M154.ftHeld == true)
    NOW = NOW + 31
    session154("host", true)
    check("F5: 30 s after the last pick on the map: given back", tonumber(CVARS["wh_pl_FastTravelEnabled"]) == 1 and M154.ftHeld == false)
    M154.fastTravel = true
    session154("host", true)
    KCD2MP.w167.OnFtHighlight(KCD2MP.w167, "ApseMap", -1, "OnHighlightFastTravelPoint", { [0] = "fastTravel_trosecko" })
    check("F6: on (the default): never held, picking or not", tonumber(CVARS["wh_pl_FastTravelEnabled"]) == 1 and M154.ftHeld ~= true)
    -- the host's own pick names its travel's destination: the travel point's world position
    ENTS["fastTravel_trosecko"] = { id = 7001, GetWorldPos = function() return { x = 1551.54, y = 2277.36, z = 17.6 } end }
    local mark = #LOG
    KCD2MP.w167.OnFtPick(KCD2MP.w167, "ApseMap", -1, "OnDoubleClicked", { [0] = "fastTravel_trosecko" })
    check("F7: a double click: the pick goes to the agent as a destination", countEvt("w167_ftpick", "1551.5,2277.4,17.6 OnDoubleClicked", mark) == 1)
    mark = #LOG
    KCD2MP.w167.OnFtPick(KCD2MP.w167, "ApseModalDialog", -1, "OnQuestionDialogConfirmClicked", {})
    check("F8: the travel question's confirm names nothing: the point last highlighted", countEvt("w167_ftpick", "1551.5,2277.4,17.6 OnQuestionDialogConfirmClicked", mark) == 1)
    local GUIDS = { ["0f989ea3-f4e2-464f-a777-2fa58f9bd6aa"] = { GetWorldPos = function() return { x = 2433.0, y = 4092.26, z = 120.0 } end } }
    local savedTG = System.GetEntityByTextGUID
    System.GetEntityByTextGUID = function(g) return GUIDS[g] end
    check("F8b: an entity's text GUID -> its position; an unknown id, a position at the origin, junk -> unknown (-)",
        KCD2MP_W167FtDest("0f989ea3-f4e2-464f-a777-2fa58f9bd6aa") == "2433.0,4092.3,120.0"
        and KCD2MP_W167FtDest("{0f989ea3-f4e2-464f-a777-2fa58f9bd6aa}") == "2433.0,4092.3,120.0"
        and KCD2MP_W167FtDest("11111111-2222-3333-4444-555555555555") == "-" and KCD2MP_W167FtDest("12") == "-"
        and KCD2MP_W167FtDest("bad id;x") == "-" and KCD2MP_W167FtDest(nil) == "-" and KCD2MP_W167FtDest(string.rep("a", 65)) == "-")
    ENTS["origin_point"] = { GetWorldPos = function() return { x = 0, y = 0, z = 0 } end }
    check("F8c: an entity at the world's origin (not placed) is not a destination", KCD2MP_W167FtDest("origin_point") == "-")
    ENTS["origin_point"] = nil
    System.GetEntityByTextGUID = savedTG
    check("F9: the console travel for each kind of destination", KCD2MP_W167FtCommand("@fastTravel_trosecko") == "wh_pl_FastTravelTo fastTravel_trosecko"
        and KCD2MP_W167FtCommand("1551.5,2277.4,17.6") == "wh_pl_FastTravelTo 1551.5 2277.4 17.6" and KCD2MP_W167FtCommand("-") == nil
        and KCD2MP_W167FtCommand("@bad;cmd") == nil)
    session154("none", false)
    noErrs("F-a")
end
do
    ERRS = {}
    reset()
    local F = KCD2MP.w167.ft
    -- the host's rules for a joiner's pick
    CVARS["wh_pl_FastTravelEnabled"] = 1
    local mark = #LOG
    check("F10: the host can travel: ok", KCD2MP_W167FtCanTravel("t1") == "ok" and countEvt("wo124_reply", "t1 ok", mark) == 1)
    CVARS["wh_pl_FastTravelEnabled"] = 0
    check("F10b: ...not while this game's own engine switch is off (the engine would open its map, then refuse)", KCD2MP_W167FtCanTravel("t1b") == "switch-off")
    CVARS["wh_pl_FastTravelEnabled"] = 1
    P_DANGER = true
    check("F11: ...not in combat (its own rules: the partner is told why)", KCD2MP_W167FtCanTravel("t2") == "combat")
    P_DANGER = false
    IN_DIALOG = true
    check("F12: ...not in a dialogue", KCD2MP_W167FtCanTravel("t3") == "dialogue")
    IN_DIALOG = false
    KCD2MP_W167FtTravelling(true, true)
    check("F13: ...not while it travels already; the host told when it travels for its partner's pick", KCD2MP_W167FtCanTravel("t4") == "travelling"
        and TOASTS[#TOASTS] == KCD2MP.w167.TEXT_FT_WITH)
    KCD2MP_W167FtTravelling(false, false)
    CMDS = {}
    check("F14: the host travels to the joiner's pick by the engine's own travel", KCD2MP_W167FtGo("1551.5,2277.4,17.6", 1) == true
        and cmdCount("wh_pl_FastTravelTo 1551.5 2277.4 17.6") == 1)
    -- the joiner runs the same travel
    CMDS = {}
    CVARS["wh_pl_FastTravelEnabled"] = 0
    KCD2MP.w114.ftBlocked = true; KCD2MP.w114.ftPrev = 1
    mark = #LOG
    KCD2MP_W167FtMirror(5, "1551.5,2277.4,17.6")
    check("F15: the joiner's switch let go for the shared travel, the same travel run here, the partner told",
        tonumber(CVARS["wh_pl_FastTravelEnabled"]) == 1 and cmdCount("wh_pl_FastTravelTo 1551.5 2277.4 17.6") == 1
        and TOASTS[#TOASTS] == KCD2MP.w167.TEXT_FT_WITH and lastLog("WO167-FT mirror seq=5", mark) ~= nil)
    CMDS = {}
    KCD2MP_W167FtMirror(6, "-")
    check("F16: a quest's travel on the host (no destination known): told only, nothing run here", cmdCount("wh_pl_FastTravelTo") == 0
        and TOASTS[#TOASTS] == KCD2MP.w167.TEXT_FT_WITH)
    KCD2MP_W167FtRefused("combat")
    check("F17: the host could not travel: the joiner told plainly", TOASTS[#TOASTS] == "Your partner can't travel right now.")
    check("F18: the texts", KCD2MP.w167.TEXT_FT_WITH == "Travelling with your partner\226\128\166")
    KCD2MP_W167FtNotStarted("joiner")
    check("F18b: a travel this mod asked for never began: the player is told to close the map", TOASTS[#TOASTS] == KCD2MP.w167.TEXT_FT_NOSTART
        and string.find(KCD2MP.w167.TEXT_FT_NOSTART, "close the map", 1, true) ~= nil)
    noErrs("F-b")
end
do
    -- FT4 on a joiner: its own engine's refusal (WO-114's block) is a request when the session's travel is on; off -> the off message
    ERRS = {}
    reset()
    KCD2MP.w114.ftBlocked = true
    KCD2MP.w166.ft.confirmAt, KCD2MP.w166.ft.told = NOW, false
    session154("joiner", true, true)
    local t0 = #TOASTS
    local mark = #LOG
    check("F19: travel on: the joiner's refusal says nothing (the pick is asked of the host)", KCD2MP_Wo114FastTravelTried("engine-refused") == true
        and #TOASTS == t0 and lastLog("the pick is asked of the host", mark) ~= nil)
    session154("joiner", true, false)
    KCD2MP.w167.OnFtHighlight(KCD2MP.w167, "ApseMap", -1, "OnHighlightFastTravelPoint", { [0] = "fastTravel_trosecko" })
    KCD2MP.w166.ft.confirmAt, KCD2MP.w166.ft.told = NOW, false
    t0 = #TOASTS
    KCD2MP_Wo114FastTravelTried("engine-refused")
    check("F20: travel off (the host's switch): the off message, never 'only the host'", TOASTS[#TOASTS] == M154.TEXT_FT_JOINER
        and TOASTS[#TOASTS] ~= "Only the host can fast travel in co-op.", TOASTS[#TOASTS])
    session154("none", false)
    KCD2MP.w114.ftBlocked = false
    noErrs("F-c")
end

-- (W) the switches and the status --------------------------------------------------------------------------------------------
do
    ERRS = {}
    reset()
    check("W1: the switches and the status command are registered", CCMDS["mp_talk_fallback"] ~= nil and CCMDS["mp_talk_fallback"].body == "KCD2MP_W167SetTalkFallback(%line)"
        and CCMDS["mp_scene_release"] ~= nil and CCMDS["mp_scene_release"].body == "KCD2MP_W167SetSceneRelease(%line)" and CCMDS["mp_w167_status"] ~= nil
        and CCMDS["mp_copy_weapons"] ~= nil and CCMDS["mp_swing_watch"] ~= nil and CCMDS["mp_sync_fallback"] ~= nil)
    check("W1b: the combat switches default on", KCD2MP.w167.copyWeapons == true and KCD2MP.w167.swingWatch == true and KCD2MP.w167.syncFallback == true)
    check("W2: the defaults: the fallback off, the scene release on", KCD2MP.w167.talkFallback == false and KCD2MP.w167.sceneRelease == true)
    local mark = #LOG
    check("W3: a toggle tells the agent", KCD2MP_W167SetSceneRelease("off") == true and countEvt("w167_cfg", "scene_release=off", mark) == 1
        and KCD2MP_W167SetSceneRelease("on") == true)
    check("W4: a bad word changes nothing", KCD2MP_W167SetTalkFallback("maybe") == false and KCD2MP.w167.talkFallback == false)
    mark = #LOG
    KCD2MP_W167Status()
    check("W5: the status line", lastLog("WO167-STATUS talk fallback=off", mark) ~= nil)
    mark = #LOG
    KCD2MP.w137.aliveAt = nil
    KCD2MP_W137Session(false, true, true)
    check("W6: a (re)connected agent learns the switches", countEvt("w167_cfg", "scene_release=on", mark) == 1 and countEvt("w167_cfg", "talk_fallback=off", mark) == 1
        and countEvt("w167_cfg", "copy_weapons=on", mark) == 1 and countEvt("w167_cfg", "swing_watch=on", mark) == 1 and countEvt("w167_cfg", "sync_fallback=on", mark) == 1)
    noErrs("W")
end

OUT = table.concat(RESULTS, "\n")
