-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-132 synthetic test, against the real kdcmp.lua under MoonSharp.
-- Harness (stubs) copied from Test-WO131Synthetic.lua.
--
--   (a) a peer's down on the host: its avatar's bleeding is healed (every body
--       part) and it is hidden where it fell
--   (b) its wake: shown again
--   (c) the heal after a hit: bleeding healed, the avatar stays shown
--   (d) no avatar, no soul, a soul without HealBleeding: nothing breaks
--
-- What this proves: the Lua half. The agent's gate and the native watch are
-- in Wo132RulesTests.cs; live evidence in docs/WO-132-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; SPAWNS = {}; CCMDS = {}; LOCKS = {}

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
System.DrawText = function(x, y, text, size) DRAWS[#DRAWS + 1] = { x = x, y = y, text = tostring(text) } end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Game.AddSaveLock = function(name, desc) if LOCKS[name] then return false end LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] == true; LOCKS[name] = nil; return had end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }
XGenAIModule = mkstub()
XGenAIModule.SpawnEntity = function(t) SPAWNS[#SPAWNS + 1] = t; ENTS[t.Name] = mkEntityLate(t.Name, t.Pos[1], t.Pos[2], t.Pos[3]) end

player = { id = 1, GetName = function(self) return "Dude" end,
           GetWorldPos = function() return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end } }

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

local function mkGhost(withSoul, withHeal)
    local e = { id = 7001, hidden = false, heals = {} }
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    if withSoul then
        e.soul = {}
        if withHeal then e.soul.HealBleeding = function(self, amount, bp) e.heals[#e.heals + 1] = { amount, bp } end end
    end
    return e
end

do
    local g = mkGhost(true, true)
    KCD2MP.ghosts["1"] = { entity = g }
    KCD2MP_W132AvatarDown("1", true)
    check("a: down -> hidden", g.hidden == true)
    check("a: down -> bleeding healed on every body part (0..9, full)", #g.heals == 10 and g.heals[1][1] == 1.0 and g.heals[10][2] == 9, #g.heals)
    check("a: down is logged", logCount("WO132-AVATAR down id=1 bleed_healed=10 hidden=true") == 1)
    check("a: marked down", KCD2MP.w132.avatarDown["1"] ~= nil)
    KCD2MP_W132AvatarDown(1, false)
    check("b: up -> shown", g.hidden == false)
    check("b: up is logged, mark cleared", logCount("WO132-AVATAR up id=1") == 1 and KCD2MP.w132.avatarDown["1"] == nil)
    g.heals = {}
    KCD2MP_W132AvatarHeal("1")
    check("c: heal after a hit: 10 body parts, still shown", #g.heals == 10 and g.hidden == false)
end

do
    KCD2MP.ghosts["2"] = { entity = mkGhost(false, false) }
    KCD2MP_W132AvatarDown("2", true)
    check("d: no soul: still hidden, nothing healed", KCD2MP.ghosts["2"].entity.hidden == true and logCount("WO132-AVATAR down id=2 bleed_healed=0") == 1)
    KCD2MP.ghosts["3"] = { entity = mkGhost(true, false) }
    KCD2MP_W132AvatarHeal("3")
    KCD2MP_W132AvatarDown("9", true)   -- no such ghost
    KCD2MP_W132AvatarHeal("9")
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
