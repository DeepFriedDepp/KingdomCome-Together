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

OUT = table.concat(RESULTS, "\n")
