-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-173 synthetic test (the game-side halves), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock).
--   (S) mp_real_hits: registered, its default (off in 0.49.0), told to the agent, refused words, the status line, the re-sync
--   (D) KCD2MP_W173Down: the host's NPC knocked out / up / dead -> the copy's state follows at once; the WO-135 tick does not ask again;
--       an "up" makes the WO-135 wake stand the copy up after 0.3 s (not 1.5 s); a plain WO-135 wake keeps its 1.5 s
-- Driven by Test-WO173Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-173-findings.md.
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

-- (S) the switch ------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    reset()
    check("S1: mp_real_hits and mp_w173_status are registered", CCMDS["mp_real_hits"] ~= nil and CCMDS["mp_real_hits"].body == "KCD2MP_W173SetRealHits(%line)"
        and CCMDS["mp_w173_status"] ~= nil and CCMDS["mp_w173_status"].body == "KCD2MP_W173Status()")
    check("S2: the default: ON (0.49.1, WO-174 D; kept for 0.50.0 by the maintainer)", KCD2MP.w173.realHits == true and CCMDS["mp_real_hits"].help:find("default ON since 0.49.1", 1, true) ~= nil)
    local mark = #LOG
    check("S3: mp_real_hits on tells the agent", KCD2MP_W173SetRealHits("on") == true and KCD2MP.w173.realHits == true
        and countEvt("w173_cfg", "real_hits=on", mark) == 1 and lastLog("WO173-TOGGLE mp_real_hits on", mark) ~= nil)
    check("S4: a bad word changes nothing", KCD2MP_W173SetRealHits("maybe") == false and KCD2MP.w173.realHits == true)
    mark = #LOG
    check("S5: mp_real_hits off tells the agent", KCD2MP_W173SetRealHits("off") == true and countEvt("w173_cfg", "real_hits=off", mark) == 1)
    mark = #LOG
    KCD2MP_W173Status()
    check("S6: the status line", lastLog("WO173-STATUS real_hits=off", mark) ~= nil and countEvt("w173_status", "", mark) == 1)
    mark = #LOG
    KCD2MP.w137.aliveAt = nil
    KCD2MP_W137Session(true, false, true)
    check("S7: a (re)connected agent learns the switch", countEvt("w173_cfg", "real_hits=off", mark) == 1)
    noErrs("S")
end

-- (D) the host's down / up on the copy -------------------------------------------------------------------------------------------
do
    ERRS = {}
    reset()
    joiner()
    local e = copy("ttkc_man_5", 110, 100)
    local unconscious = false
    e.actor.IsUnconscious = function() return unconscious end
    e.actor.Revive = function() end
    local p = KCD2MP.npcPuppets["ttkc_man_5"]
    p.everPacket = true
    local mark = #LOG
    KCD2MP_W173Down("ttkc_man_5", 1)
    check("D1: knocked out on the host -> the copy is marked down at once", p.ko == true and lastLog("WO173-DOWN npc=ttkc_man_5 state=1 copy=yes", mark) ~= nil)
    check("D2: the WO-135 tick will not ask again for it", KCD2MP.w135.ko["ttkc_man_5"] ~= nil and KCD2MP.w135.ko["ttkc_man_5"].want == true)
    unconscious = true
    KCD2MP_W173Down("ttkc_man_5", 0)
    check("D3: up on the host -> the copy is marked up, a fast wake noted", p.ko == false and KCD2MP.w173.fastWake["ttkc_man_5"] ~= nil
        and KCD2MP.w135.ko["ttkc_man_5"].want == false)
    unconscious = false
    TIMERS = {}
    KCD2MP_W135KoDone("ttkc_man_5", false, true)
    check("D4: the wake stands it up after 0.3 s (the host's NPC is up: within 0.5 s)", #TIMERS == 1 and TIMERS[1].ms == 300 and KCD2MP.w173.fastWake["ttkc_man_5"] == nil)
    TIMERS = {}
    KCD2MP_W135KoDone("ttkc_man_5", false, true)
    check("D5: a plain WO-135 wake keeps its 1.5 s", #TIMERS == 1 and TIMERS[1].ms == 1500)
    local ko0 = p.ko
    local dead0 = KCD2MP.w173.stats.dead
    KCD2MP_W173Down("ttkc_man_5", 2)
    check("D6: dead on the host: counted, the copy's state left to WO-172's death path", p.ko == ko0 and KCD2MP.w173.stats.dead == dead0 + 1)
    mark = #LOG
    KCD2MP_W173Down("nobody_1", 1)
    check("D7: no copy here: said, nothing else", lastLog("WO173-DOWN npc=nobody_1 state=1 copy=no", mark) ~= nil)
    noErrs("D")
end

OUT = table.concat(RESULTS, "\n")
