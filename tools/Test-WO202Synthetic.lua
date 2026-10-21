-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-202 synthetic test: the Lua halves of WO-202, against the real kdcmp.lua under MoonSharp (the engine stubbed, a fake clock).
--   (a) the switches: each its own console command, all on       (b) II.3 a dead body still upright is laid down by the ragdoll
--   (c) II.3 a load clears the announced death marks              (d) VI the mark key marks once, the partner sees "Marked"
--   (e) VI the whole outfit in one call                           (f) V.3 a key taken goes out once; a partner's key is made once
--   (g) II.2 the early release says the partner is loading       (h) V.3 a chest unlock in is refused outside a joiner session
-- What this proves: the Lua rules. Live evidence: docs/WO-202-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; CCMDS = {}; LOCKS = {}; MSGS = {}; EXTRA = {}
QINV = {}; CREATE_REFUSED = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetCVar = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return SPHERE end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.DrawText = function() end
System.RemoveEntity = function(eid)
    for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil; e.removed = true end end
    for _, e in ipairs(EXTRA) do if e.id == eid then e.removed = true end end
end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.GetTerrainElevation = function() return 0 end
System.GetEntity = function(id)
    for _, e in pairs(ENTS) do if e.id == id then return e end end
    for _, e in ipairs(EXTRA) do if e.id == id and not e.removed then return e end end
    return nil
end
System.GetEntitiesByClass = function(cls)
    local out = {}
    for _, e in pairs(ENTS) do if e.class == cls then out[#out + 1] = e end end
    for _, e in ipairs(EXTRA) do if e.class == cls and not e.removed then out[#out + 1] = e end end
    return out
end
ENTITY_FLAG_NO_SAVE = 32768
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Game.AddSaveLock = function(name) if LOCKS[name] then return false end LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] == true; LOCKS[name] = nil; return had end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }

local NEXTID = 0x0E0000
local function mkEntity(name, x, y, z, cls, keep)
    NEXTID = NEXTID + 1
    local e = { class = cls or "NPC", id = "userdata: " .. string.format("%016X", NEXTID), px = x or 0, py = y or 0, pz = z or 0, rz = 0,
                dead = false, hp = 100, hidden = false, flags = 0, mounted = false, dismounts = 0, mounts = 0 }
    e.this = { id = "this:" .. name }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self, t) t = t or {}; t.x, t.y, t.z = self.px, self.py, self.pz; return t end
    e.GetWorldAngles = function(self, t) t = t or {}; t.x, t.y, t.z = 0, 0, self.rz; return t end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.SetFlags = function(self, f, mode) if mode == 3 then self.flags = self.flags + f else self.flags = f end end
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.StartAnimation = function() end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end }
    e.human = { IsInDialog = function() return false end, IsMounted = function() return e.mounted end,
                ForceDismount = function() e.mounted = false; e.dismounts = e.dismounts + 1 end,
                ForceMount = function() e.mounted = true; e.mounts = e.mounts + 1 end }
    e.soul = { GetId = function() return "userdata: 05000000000001DC" end }
    if keep then EXTRA[#EXTRA + 1] = e else ENTS[name] = e end
    return e
end

XGenAIModule = mkstub()
XGenAIModule.SpawnEntity = function(t) mkEntity(t.Name, t.Pos[1], t.Pos[2], t.Pos[3], t.ClassName) end
XGenAIModule.MakeTableFromType = function(kind) return { kind = kind, information = {} } end

player = { id = 1, this = { id = 777 }, GetName = function(self) return "Dude" end,
           GetWorldPos = function(self) return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end },
           human = { IsMounted = function() return false end, IsInDialog = function() return false end },
           inventory = {
               GetCountOfClass = function(self, g) return QINV[g] or 0 end,
               CreateItem = function(self, g, hp, n) if CREATE_REFUSED[g] then return false end QINV[g] = (QINV[g] or 0) + (n or 1); return true end,
               DeleteItemOfClass = function(self, g, n) local h = QINV[g] or 0; local d = math.min(h, n or 0); QINV[g] = h - d; return d end,
           } }

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return table.unpack(r)
end

-- @@KDCMP@@

-- Part 2: scenarios.

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function logCount(pat)
    local n = 0
    for _, l in ipairs(LOG) do if string.find(l, pat, 1, true) then n = n + 1 end end
    return n
end
local function lastLog(pat)
    for i = #LOG, 1, -1 do if string.find(LOG[i], pat, 1, true) then return LOG[i] end end
    return nil
end
local function clearLog() LOG = {} end
local function evts(name)
    local out = {}
    for _, l in ipairs(LOG) do
        local a = l:match("%[KCD2%-MP%-EVT%] v1 %d+ " .. name:gsub("_", "%%_") .. " (.*)$")
        if a then out[#out + 1] = a end
    end
    return out
end
local function cmdCount(pat)
    local n = 0
    for _, c in ipairs(CMDS) do if string.find(c, pat, 1, true) then n = n + 1 end end
    return n
end


-- (a) the switches -- read before any scenario touches them
do
    local n, bad = 0, {}
    for k, label in pairs(W202.NAMES) do
        n = n + 1
        if not CCMDS[label] then bad[#bad + 1] = label .. " (no command)" end
        if not KCD2MP_W202On(k) then bad[#bad + 1] = label .. " (off)" end
    end
    check("a: every WO-202 switch is its own console command and on by default", n >= 10 and #bad == 0, table.concat(bad, ","))
    check("a: mp_w202_status registered", CCMDS["mp_w202_status"] ~= nil)
end

-- (b) II.3: a body the engine reads dead but still upright is laid down by the game's own ragdoll; a living one is never touched
do
    clearLog()
    local e = mkEntity("d_bandit", 10, 10, 0)
    local rag = 0
    e.RagDollize = function(self, slot) rag = rag + 1 end
    e.dead = false
    check("b: a living body is refused", KCD2MP_W202DeathFall("d_bandit") == false and rag == 0, lastLog("WO202-DEATHFALL"))
    e.dead = true
    check("b: a dead upright body gets the ragdoll", KCD2MP_W202DeathFall("d_bandit") == true and rag == 1, lastLog("WO202-DEATHFALL"))
    KCD2MP_W202Set("death_engine_only", "off")
    check("b: the switch off: nothing", KCD2MP_W202DeathFall("d_bandit") == false and rag == 1)
    KCD2MP_W202Set("death_engine_only", "on")
    check("b: no body: refused", KCD2MP_W202DeathFall("nobody_here") == false)
end

-- (c) II.3: a load (the dead timer chain's restart) clears the announced death marks with the seen ones
do
    clearLog()
    KCD2MP._npcDeathAnnounced["c_villager"] = "lua"
    KCD2MP._npcDeathRemote["c_villager"] = { via = "x", at = 0 }
    KCD2MP._npcDeathSeen["c_villager"] = true
    NOW = NOW + 30
    KCD2MP_OnChainDeadRestart("test")
    check("c: the announced mark goes with the load", KCD2MP._npcDeathAnnounced["c_villager"] == nil, lastLog("NPC-DEATH reload"))
    check("c: ...the remote and seen marks too", KCD2MP._npcDeathRemote["c_villager"] == nil and KCD2MP._npcDeathSeen["c_villager"] == nil)
end

-- (d) VI: the mark key -- one press marks once (a press 0.5 s later is ignored), other actions are not its; the partner's mark shows here
do
    clearLog(); TOASTS = {}
    NOW = NOW + 10
    check("d: another action is not the mark key's", KCD2MP_W202OnAction("kcd2mp_quicksave", "press") == false)
    check("d: the mark key's press is taken", KCD2MP_W202OnAction("kcd2mp_mark", "press") == true)
    check("d: ...an odd mark is logged and sent", logCount("MP-MARK odd") == 1 and #evts("mp_mark") == 1, lastLog("MP-MARK"))
    local msg = KCD2MP.interactionMsg and KCD2MP.interactionMsg.text or ""
    check("d: ...with a Marked toast", string.find(msg, "Marked", 1, true) ~= nil, msg)
    NOW = NOW + 0.5
    KCD2MP_W202OnAction("kcd2mp_mark", "press")
    check("d: a second press within 1 s is not a second mark", logCount("MP-MARK odd") == 1)
    KCD2MP_W202OnAction("kcd2mp_mark", "release")
    check("d: a release marks nothing", logCount("MP-MARK odd") == 1)
    KCD2MP.interactionMsg = nil
    KCD2MP_W202PartnerMarked("your host")
    local pm = KCD2MP.interactionMsg and KCD2MP.interactionMsg.text or ""
    check("d: the partner's mark: Marked here too", string.find(pm, "Marked", 1, true) ~= nil, pm)
end

-- (e) VI: the partner's new pieces all go on in one call, each through WO-144's inventory equip
do
    clearLog()
    local fig = mkEntity("kcd2mp_7", 0, 0, 0)
    local inv, worn = {}, {}
    fig.inventory = { FindItem = function(self, c) return inv[c] end, CreateItem = function(self, c) inv[c] = "item:" .. c end }
    fig.actor.EquipInventoryItem = function(self, it) worn[#worn + 1] = it end
    local n = KCD2MP_W202EquipAll("kcd2mp_7", "11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222")
    check("e: both pieces on in the one call", n == 2 and #worn == 2, lastLog("WO202-OUTFIT"))
end

-- (f) V.3: a key this player takes goes out once; one that came from the partner is never sent back; a partner's key is made once
do
    clearLog(); TOASTS = {}
    local K1, K2 = "043dbb89-6a87-4dd7-a6ee-ca8c094acc18", "0c057351-28f6-418a-95ab-66d001000000"
    KCD2MP_W202KeyClassReset()
    KCD2MP_W202KeyClassAdd(K1 .. " " .. K2)
    QINV[K1], QINV[K2] = 0, 0
    KCD2MP_W202KeysTick()                       -- the first read primes
    check("f: the first read sends nothing", #evts("w202_key") == 0)
    QINV[K1] = 1
    KCD2MP_W202KeysTick()
    check("f: a key taken here goes out once", #evts("w202_key") == 1 and evts("w202_key")[1] == K1, evts("w202_key")[1])
    KCD2MP_W202KeysTick()
    check("f: ...and only once", #evts("w202_key") == 1)
    check("f: the partner's key is made here", KCD2MP_W202KeyIn(K2, "your partner") == true and QINV[K2] == 1, lastLog("WO202-KEY"))
    KCD2MP_W202KeysTick()
    check("f: a key that came from the partner is never sent back", #evts("w202_key") == 1)
    KCD2MP_W202KeyIn(K2, "your partner")
    check("f: a key already here is not made twice", QINV[K2] == 1 and logCount("has it already") == 1)
    check("f: not a key class: refused", KCD2MP_W202KeyIn("99999999-9999-9999-9999-999999999999", "x") == false)
    -- a load: the keys the loaded save holds are not taken now
    NOW = NOW + 30
    KCD2MP_OnChainDeadRestart("load")
    QINV[K1] = 3
    KCD2MP_W202KeysTick()
    check("f: after a load the first read only primes again", #evts("w202_key") == 1)
end

-- (g) II.2: the agent's early release ("sent"): the world runs again and the toast says the partner is loading
do
    clearLog(); TOASTS = {}
    local w = KCD2MP.w123
    if w and KCD2MP_JoinResume then
        w.paused, w.joinId, w.partner, w.pausedAt = true, "abcd1234", "partner", NOW
        KCD2MP_JoinResume("abcd1234", "sent")
        local gm = KCD2MP.interactionMsg and KCD2MP.interactionMsg.text or ""
        check("g: the early release's toast says the partner is loading", string.find(gm, "loading", 1, true) ~= nil, gm)
        check("g: ...and the event says sent", #evts("join_resumed") >= 1 and string.find(evts("join_resumed")[#evts("join_resumed")], "sent", 1, true) ~= nil)
    else
        check("g: the join pause exists", false, "KCD2MP.w123 / KCD2MP_JoinResume missing")
    end
end

-- (h) V.3: a chest unlock from the host is applied only on a joiner in a session
do
    clearLog()
    check("h: outside a joiner session nothing is unlocked", KCD2MP_W202ChestIn("chest_1", 1, 2, 3) == false)
end

check("no Lua errors", #ERRS == 0, ERRS[1])

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
