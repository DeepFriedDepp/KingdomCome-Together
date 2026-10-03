-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-154 synthetic test (the game-side phases), against the real kdcmp.lua under MoonSharp.
--
--   (A) Phase 2: a partner who is knocked down falls on this screen, lies there with his writer held, and
--       stands up when he does; a hidden (dead) avatar never falls; mp_avatar_falls off stands every figure up
--   (A5) Phase 2, fail closed: an avatar without its native protections has its brain paused until it has them
--
-- Driven by Test-WO154GameSynthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-154-findings.md.
--
-- Part 1: engine stubs + a fake clock.

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
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
Game = mkstub()

player = nil

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return unpack(r)
end

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

local NEXTID = 9000
local function mkAvatar(id, x, y, z)
    NEXTID = NEXTID + 1
    local name = "kcd2mp_" .. tostring(id)
    local e = { class = "NPC", id = NEXTID, px = x or 0, py = y or 0, pz = z or 0, rz = 0, hidden = false, falls = 0, stands = 0 }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.GetWorldAngles = function(self) return { x = 0, y = 0, z = self.rz } end
    e.SetWorldPos = function(self, p) self.px, self.py, self.pz = p.x, p.y, p.z end
    e.IsHidden = function(self) return self.hidden end
    e.Hide = function(self, v) self.hidden = (v == 1) end
    e.actor = {
        IsDead = function() return false end,
        IsUnconscious = function() return false end,
        GetHealth = function() return 100 end,
        RagDollize = function(self) e.falls = e.falls + 1; e.fallAt = { x = e.px, y = e.py, z = e.pz } end,
        Revive = function(self, full) e.stands = e.stands + 1; e.reviveFull = full end,
    }
    ENTS[name] = e
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    KCD2MP.ghosts[tostring(id)] = { entity = e, istate = { cx = e.px, cy = e.py, cz = e.pz } }
    return e, KCD2MP.ghosts[tostring(id)]
end

-- (A) Phase 2: a partner knocked down falls here, lies, and stands up with him -----------------------------------------------
do
    ERRS = {}
    KCD2MP.w154.down = {}
    local e, g = mkAvatar(1, 10, 20, 30)
    g.istate.nativeSent = true; g.istate.nativeOwned = true   -- the DLL writes it every frame
    check("A: a standing partner is no corpse", mp_ghost_is_corpse("1", g) == false)
    local mark = #LOG
    KCD2MP_W154AvatarDowned("1", true)
    check("A: down -> the writer lets go first", g.istate.nativeOwned == false and countEvt("npc_native", "kcd2mp_1 off", mark) == 1,
        tostring(g.istate.nativeOwned))
    check("A: down -> the engine's own ragdoll, where it stands", e.falls == 1 and e.fallAt and e.fallAt.x == 10 and e.fallAt.y == 20, tostring(e.falls))
    check("A: down -> WO154-FALL logged ok", lastLog("WO154-FALL avatar=kcd2mp_1 ok=true", mark) ~= nil, lastLog("WO154-FALL", mark))
    check("A: while down it is frozen like a body (nothing moves or animates it)", mp_ghost_is_corpse("1", g) == true)
    check("A: while down WO-135's not-living stand-up refuses", KCD2MP_W135AvatarStandUp("kcd2mp_1") == false and e.stands == 0, tostring(e.stands))
    NOW = NOW + 6.0
    mark = #LOG
    KCD2MP_W154AvatarDowned("1", false)
    check("A: up -> the engine's own Revive(false) stands it", e.stands == 1 and e.reviveFull == false, tostring(e.stands))
    check("A: up -> WO154-RISE logged with how long he lay", (lastLog("WO154-RISE avatar=kcd2mp_1 ok=true", mark) or ""):find("down 6.0 s", 1, true) ~= nil,
        lastLog("WO154-RISE", mark))
    check("A: up -> no longer frozen", mp_ghost_is_corpse("1", g) == false)
    noErrs("A")
end

-- (A2) a hidden avatar (a death or an execution: WO-132 hides it at the death spot) never falls -------------------------------
do
    ERRS = {}
    KCD2MP.w154.down = {}
    local e, g = mkAvatar(2, 0, 0, 0)
    e.hidden = true
    local mark = #LOG
    KCD2MP_W154AvatarDowned("2", true)
    check("A2: a hidden figure does not fall", e.falls == 0, tostring(e.falls))
    check("A2: ... and says why", lastLog("WO154-FALL avatar=kcd2mp_2 is hidden", mark) ~= nil)
    noErrs("A2")
end

-- (A3) mp_avatar_falls off: every fallen figure stands, the agent is told; on again is reported --------------------------------
do
    ERRS = {}
    KCD2MP.w154.down = {}
    local e, g = mkAvatar(3, 0, 0, 0)
    KCD2MP_W154AvatarDowned("3", true)
    local mark = #LOG
    KCD2MP_W154SetFalls("off")
    check("A3: off stands the fallen figure up at once", e.stands == 1 and not KCD2MP_W154IsDown("3"), tostring(e.stands))
    check("A3: off tells the agent", countEvt("w154_falls", "off", mark) == 1)
    mark = #LOG
    KCD2MP_W154SetFalls("on")
    check("A3: on tells the agent", countEvt("w154_falls", "on", mark) == 1)
    check("A3: a bad value is refused", KCD2MP_W154SetFalls("maybe") == false)
    check("A3: the console command is registered", CCMDS["mp_avatar_falls"] ~= nil and CCMDS["mp_avatar_falls"].body:find("%line", 1, true) ~= nil)
    noErrs("A3")
end

-- (A4) an avatar with no body yet is only marked (its spawn later finds it down) ------------------------------------------
do
    ERRS = {}
    KCD2MP.w154.down = {}
    local mark = #LOG
    KCD2MP_W154AvatarDowned("7", true)
    check("A4: no body -> marked down, nothing called", KCD2MP_W154IsDown("7") and lastLog("WO154-FALL avatar=kcd2mp_7 no body", mark) ~= nil)
    KCD2MP_W154AvatarDowned("7", false)
    check("A4: up clears the mark", not KCD2MP_W154IsDown("7"))
    noErrs("A4")
end

-- (A5) fail closed: no native protections -> the brain is paused; with them -> resumed ------------------------------------
do
    ERRS = {}; CMDS = {}
    local mark = #LOG
    KCD2MP_W154AvatarIdentity("4", false)
    check("A5: no protections -> wh_ai_PauseNPC on the avatar", CMDS[1] == "wh_ai_PauseNPC kcd2mp_4", CMDS[1])
    KCD2MP_W154AvatarIdentity("4", false)
    check("A5: ... once", #CMDS == 1, tostring(#CMDS))
    check("A5: ... logged", lastLog("WO154-FAILCLOSED avatar=kcd2mp_4 paused ok=true", mark) ~= nil)
    KCD2MP_W154AvatarIdentity("4", true)
    check("A5: protections on -> wh_ai_ResumeNPC", CMDS[2] == "wh_ai_ResumeNPC kcd2mp_4", CMDS[2])
    KCD2MP_W154AvatarIdentity("5", true)
    check("A5: an avatar that always had them is never touched", #CMDS == 2, tostring(#CMDS))
    noErrs("A5")
end

-- Phase 3 stubs: this machine's player and a plain NPC.
local function mkPlayer(x, y)
    local p = { class = "Player", id = 7777, px = x or 0, py = y or 0, pz = 0, danger = false, combat = false, dead = false, money = 100, stood = 0 }
    p.GetName = function() return "Dude" end
    p.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    p.soul = { IsInCombatDanger = function() return p.danger end, IsInCombatMode = function() return p.combat end }
    p.actor = { IsDead = function() return p.dead end, StandUp = function() p.stood = p.stood + 1 end }
    p.human = { IsInDialog = function() return false end }
    p.inventory = { GetMoney = function() return p.money end }
    player = p
    return p
end
local function mkNpc(name, x, y)
    NEXTID = NEXTID + 1
    local n = { class = "NPC", id = NEXTID, px = x or 0, py = y or 0, pz = 0, dead = false, fighting = false }
    n.GetName = function() return name end
    n.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    n.actor = { IsDead = function() return n.dead end, IsUnconscious = function() return false end }
    n.soul = { IsInCombatMode = function() return n.fighting end, IsPublicEnemy = function() return false end,
               HasScriptContext = function() return false end, GetFactionID = function() return "trosecko_settlements_semin" end }
    n.this = { id = n.id }
    ENTS[name] = n
    return n
end
local function runTimers()
    local t = TIMERS; TIMERS = {}
    for _, x in ipairs(t) do x.f() end
end

-- (B1) Phase 3.3, joiner: a stop the guard turned into an attack is "fled" only when he moved away --------------------
do
    ERRS = {}
    local p = mkPlayer(2, 0)
    local s = { p0 = { x = 0, y = 0, z = 0 } }
    local mark = #LOG
    check("B1: stood (2 m) -> attacked, no resist", KCD2MP_W154StopResult(s, "fled") == "attacked", KCD2MP_W154StopResult(s, "fled"))
    check("B1: ... logged", lastLog("WO154-STOP attacked moved=2.0", mark) ~= nil)
    p.px = 12
    check("B1: moved 12 m away -> fled", KCD2MP_W154StopResult(s, "fled") == "fled")
    check("B1: a fight result stays a fight", KCD2MP_W154StopResult(s, "fought") == "fought")
    p.px = 0
    KCD2MP.w154.guardRespite = false
    check("B1: mp_guard_respite off -> 0.44.0's fled", KCD2MP_W154StopResult(s, "fled") == "fled")
    KCD2MP.w154.guardRespite = true
    noErrs("B1")
end

-- (B2) the stop tick: the guard attacks a player who stands -> "attacked"; a death during the stop -> "died" ------------
do
    ERRS = {}; CMDS = {}
    XGenAIModule = { MakeTableFromType = function(kind) return { kind = kind } end, SendMessageToEntityData = function() end }
    local w = KCD2MP.w139
    w.joiner, w.host, w.on, w.aliveAt = true, false, true, NOW
    local p = mkPlayer(0, 0)
    local g = mkNpc("tsem_man_9", 3, 0)
    p.danger = true
    w.stop = { id = "8", guard = "tsem_man_9", since = NOW - 5, money0 = 100, planted = {}, result = nil, sawDialog = false, p0 = { x = 0, y = 0, z = 0 } }
    local mark = #LOG
    KCD2MP_W139StopTick()
    check("B2: the guard attacks a standing player -> outcome 'attacked' (no resist)", countEvt("w139_outcome", "8 attacked tsem_man_9", mark) == 1,
        lastLog("w139_outcome", mark))
    w.stop = { id = "9", guard = "tsem_man_9", since = NOW - 5, money0 = 100, planted = {}, result = nil, sawDialog = false, p0 = { x = 0, y = 0, z = 0 } }
    p.dead = true
    mark = #LOG
    KCD2MP_W139StopTick()
    check("B2: he died during the stop -> outcome 'died'", countEvt("w139_outcome", "9 died tsem_man_9", mark) == 1, lastLog("w139_outcome", mark))
    w.stop = { id = "10", guard = "tsem_man_9", since = NOW - 5, money0 = 100, planted = {}, result = nil, sawDialog = false, p0 = { x = 0, y = 0, z = 0 } }
    KCD2MP.w154.guardRespite = false
    mark = #LOG
    KCD2MP_W139StopTick()
    check("B2: mp_guard_respite off -> 0.44.0's 'fought' on a death", countEvt("w139_outcome", "10 fought tsem_man_9", mark) == 1, lastLog("w139_outcome", mark))
    KCD2MP.w154.guardRespite = true
    w.joiner, w.stop = false, nil
    noErrs("B2")
end

-- (B3) Phase 3.1, host: a murder only on a death, an assault judged 5 s later (no crime if it fights by then) ----------
do
    ERRS = {}; TIMERS = {}
    local w = KCD2MP.w139
    w.host, w.on, w.aliveAt = true, true, NOW
    mkPlayer(0, 0)
    local v1 = mkNpc("taborVictim_1", 5, 5)
    local mark = #LOG
    KCD2MP_W139HostViolent(1, "taborVictim_1", "assault")
    check("B3: an assault is not judged at once", countLog("WO139-JUDGE src=1 id=0 assault", mark) == 0 and #TIMERS == 1 and TIMERS[1].ms == 5000,
        tostring(#TIMERS))
    runTimers()
    check("B3: ... but 5 s later, when the victim does not fight", countLog("WO139-JUDGE src=1 id=0 assault", mark) == 1, lastLog("WO139-JUDGE", mark))
    local v2 = mkNpc("zbranePanaSemina_moravak_jurko", 5, 5)
    mark = #LOG
    KCD2MP_W139HostViolent(1, "zbranePanaSemina_moravak_jurko", "assault")
    v2.fighting = true   -- the host's own brawl starts 2.9 s later
    runTimers()
    check("B3: a victim that fights by then (a quest brawl) -> not a crime", countLog("WO139-JUDGE src=1 id=0 assault", mark) == 0
        and lastLog("WO154-JUDGE src=1 assault on zbranePanaSemina_moravak_jurko -- not a crime", mark) ~= nil, lastLog("JUDGE", mark))
    local v3 = mkNpc("taboryUCesty_duel_kunes", 5, 5)
    v3.fighting = true
    mark = #LOG
    for _ = 1, 11 do KCD2MP_W139HostViolent(1, "taboryUCesty_duel_kunes", "hit") end   -- the duel's 0-hp hits
    runTimers()
    check("B3: 0-hp hits on a living victim are never murder", countLog("murder", mark) == 0, lastLog("JUDGE", mark))
    v3.dead = true
    mark = #LOG
    KCD2MP_W139HostViolent(1, "taboryUCesty_duel_kunes", "hit")
    KCD2MP_W139HostViolent(1, "taboryUCesty_duel_kunes", "hit")
    check("B3: the victim's death is the murder -- judged once", countLog("WO139-JUDGE src=1 id=0 murder", mark) == 1, tostring(countLog("murder", mark)))
    local v4 = mkNpc("taborVictim_4", 5, 5)
    mark = #LOG
    KCD2MP_W139HostViolent(1, "taborVictim_4", "hit")
    v4.dead = true   -- the fatal blow lands a frame later
    runTimers()
    check("B3: a death a moment after the hit is still the murder", countLog("WO139-JUDGE src=1 id=0 murder", mark) == 1)
    mkNpc("taborVictim_5", 5, 5)
    mark = #LOG
    KCD2MP_W139HostViolent(1, "taborVictim_5", "knockout")
    check("B3: a takedown's knockout is judged at once, as before", countLog("WO139-JUDGE src=1 id=0 knockout", mark) == 1)
    KCD2MP.w154.fairCrime = false
    mkNpc("taborVictim_6", 5, 5)
    mark = #LOG; TIMERS = {}
    KCD2MP_W139HostViolent(1, "taborVictim_6", "assault")
    check("B3: mp_fair_crime off -> 0.44.0: judged at once", countLog("WO139-JUDGE src=1 id=0 assault", mark) == 1 and #TIMERS == 0)
    KCD2MP.w154.fairCrime = true
    w.host = false
    noErrs("B3")
end

-- (B4) Phase 3.4: mp_unstuck ends a fight he is in (and only then, unless asked) ---------------------------------------
do
    ERRS = {}
    local p = mkPlayer(0, 0)
    local mark = #LOG
    KCD2MP_W151Unstuck("")
    check("B4: not fighting -> no end-fights step", countEvt("w154_endfights", "", mark) == 0)
    p.danger = true
    mark = #LOG
    KCD2MP_W151Unstuck("")
    check("B4: in a fight -> the agent ends it (w154_endfights)", countEvt("w154_endfights", "unstuck", mark) == 1)
    check("B4: ... and the log says so", (lastLog("WO151-UNSTUCK soft", mark) or ""):find("end-fights", 1, true) ~= nil, lastLog("WO151-UNSTUCK", mark))
    p.danger = false
    mark = #LOG
    KCD2MP_W151Unstuck("fight")
    check("B4: 'mp_unstuck fight' ends fights even when the game says none", countEvt("w154_endfights", "unstuck", mark) == 1)
    noErrs("B4")
end

-- (B5) Phase 3's switches: each tells the agent, refuses a bad value, and is a console command -------------------------
do
    ERRS = {}
    local cases = { { "mp_host_target", KCD2MP_W154SetHostTarget, "host_target" }, { "mp_guard_respite", KCD2MP_W154SetGuardRespite, "guard_respite" },
                    { "mp_fair_crime", KCD2MP_W154SetFairCrime, "fair_crime" }, { "mp_scene_resume", KCD2MP_W154SetSceneResume, "scene_resume" } }
    for _, c in ipairs(cases) do
        local mark = #LOG
        c[2]("off")
        check("B5: " .. c[1] .. " off tells the agent", countEvt("w154_cfg", c[3] .. "=off", mark) == 1)
        mark = #LOG
        c[2]("on")
        check("B5: " .. c[1] .. " on tells the agent", countEvt("w154_cfg", c[3] .. "=on", mark) == 1)
        check("B5: " .. c[1] .. " refuses a bad value", c[2]("maybe") == false)
        check("B5: " .. c[1] .. " is registered with an unquoted %line", CCMDS[c[1]] ~= nil and CCMDS[c[1]].body:find("(%line)", 1, true) ~= nil)
    end
    KCD2MP_W154SetSceneResume("off")   -- its default
    check("B5: the defaults are on, on, on, off", KCD2MP.w154.hostTarget and KCD2MP.w154.guardRespite and KCD2MP.w154.fairCrime and not KCD2MP.w154.sceneResume)
    noErrs("B5")
end

-- (C1) Phase 4.1, host: the join bar through the engine's hold is the game's tutorial panel -----------------------------
do
    ERRS = {}
    local calls = {}
    local saved = UIAction.CallFunction
    UIAction.CallFunction = function(panel, inst, fn, a1, a2, a3, ...) calls[#calls + 1] = { panel = panel, fn = fn, id = a1, html = a2, ms = a3 } end
    local mark = #LOG
    check("C1: the panel is pushed", KCD2MP_W154JoinPanel("joiner-one", "sending", 50, 4) == true)
    check("C1: the queue is flushed first (HideAllTutorials, then ShowTutorial)", #calls == 2 and calls[1].fn == "HideAllTutorials" and calls[2].fn == "ShowTutorial",
        tostring(#calls))
    local html = calls[2] and calls[2].html or ""
    check("C1: its own id, the stage's own words, a long safety lifetime", calls[2].id == "kcd2mp_join" and html:find("Sending the world to joiner-one... 50%", 1, true) ~= nil
        and calls[2].ms == 600000, html)
    check("C1: logged as the join's screen row", lastLog('MP-SCREEN panel=join text="Sending the world to joiner-one... 50%', mark) ~= nil)
    calls = {}
    KCD2MP_W154JoinPanel("<b>x</b>", "loading", 100, 30)
    check("C1: markup in a name is escaped", (calls[2] and calls[2].html or ""):find("&lt;b&gt;x", 1, true) ~= nil)
    -- the drawn bar stands down while the panel shows (one bar, not two)
    KCD2MP.w123.paused = true; KCD2MP.w123.pausedAt = NOW; KCD2MP.w123.timeoutS = 600
    local draws = 0
    local savedDraw = System.DrawText
    System.DrawText = function(...) draws = draws + 1 end
    KCD2MP_JoinDrawUI()
    check("C1: while the panel shows, no drawn bar", draws == 0, tostring(draws))
    calls = {}
    KCD2MP_W154JoinPanelHide("join in")
    check("C1: the join's end hides it", #calls >= 2 and calls[1].fn == "HideTutorial" and calls[1].id == "kcd2mp_join" and not KCD2MP.w154.joinPanelOn)
    KCD2MP_JoinDrawUI()
    check("C1: ...and without the panel the drawn bar is back", draws == 3, tostring(draws))
    System.DrawText = savedDraw
    KCD2MP.w123.paused = false
    UIAction.CallFunction = saved
    local m2 = #LOG
    KCD2MP_W154SetJoinPanel("off")
    check("C1: mp_join_panel tells the agent", countEvt("w154_cfg", "join_panel=off", m2) == 1)
    KCD2MP_W154SetJoinPanel("on")
    check("C1: mp_join_panel is a console command", CCMDS["mp_join_panel"] ~= nil and CCMDS["mp_join_panel"].body:find("(%line)", 1, true) ~= nil)
    noErrs("C1")
end

-- (C2) Phase 4.2, joiner: the where probe answers the agent's token ---------------------------------------------------
do
    ERRS = {}
    player = nil
    local mark = #LOG
    KCD2MP_W154Where("ab12cd34")
    check("C2: no player -> menu, under the agent's token", countEvt("wo124_reply", "ab12cd34 menu", mark) == 1)
    mkPlayer(0, 0)
    mark = #LOG
    KCD2MP_W154Where("ef56")
    check("C2: a player -> world", countEvt("wo124_reply", "ef56 world", mark) == 1)
    local m2 = #LOG
    KCD2MP_W154SetJoinPatient("off")
    check("C2: mp_join_patient tells the agent", countEvt("w154_cfg", "join_patient=off", m2) == 1)
    KCD2MP_W154SetJoinPatient("on")
    check("C2: mp_join_patient is a console command", CCMDS["mp_join_patient"] ~= nil)
    noErrs("C2")
end

OUT = table.concat(RESULTS, "\n")
