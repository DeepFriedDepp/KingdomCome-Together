-- WO-135 synthetic test, against the real kdcmp.lua under MoonSharp.
-- Harness copied from Test-WO131Synthetic.lua.
--   (a) mp_avatar_quiet: mask, groups, the event to the agent
--   (b) knockouts follow the host's world on a joiner; the wake revives
--   (c) the game's takedowns on a host-owned copy become requests
--   (d) the host performs them with the joiner's avatar (and its checks)
--   (e) a ragdolled avatar is stood up
-- What this proves: the Lua halves. Live evidence: docs/WO-135-findings.md.

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
local function lastLog(pat)
    for i = #LOG, 1, -1 do if string.find(LOG[i], pat, 1, true) then return LOG[i] end end
    return nil
end
local function clearLog() LOG = {} end
NEXTID = 5000
local function mkEntity(name, x, y, z)
    NEXTID = NEXTID + 1
    local e = { class = "NPC", id = NEXTID, px = x or 0, py = y or 0, pz = z or 0, rz = 0, dead = false, hp = 100 }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.GetWorldAngles = function(self) return { x = 0, y = 0, z = self.rz } end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.StartAnimation = function() end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end }
    e.human = { IsWeaponDrawn = function() return false end, IsInDialog = function() return false end }
    e.hidden = false
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.standups = 0
    e.actor.StandUp = function() e.standups = e.standups + 1 end
    e.actor.IsUnconscious = function() return e.ko == true end
    e.soul = { DealDamage = function() end }
    return e
end
mkEntityLate = mkEntity


local function cmdCount(pat)
    local n = 0
    for _, c in ipairs(CMDS) do if string.find(c, pat, 1, true) then n = n + 1 end end
    return n
end
local function reset()
    KCD2MP.npcPuppets = {}; KCD2MP.npcTracked = {}; KCD2MP.dragging = {}; KCD2MP.dragWatch = {}
    KCD2MP._npcPaused = {}; KCD2MP._npcResumePending = {}; KCD2MP._npcEverPaused = {}
    KCD2MP.ghosts = {}; KCD2MP.horseGhosts = {}
    KCD2MP.w131.parked = {}; KCD2MP.w131.standins = {}; KCD2MP.w131.active = false; KCD2MP.w131.guard = true
    KCD2MP.w131.reassert = false; KCD2MP.w131.standinLastAt = -1e9
    KCD2MP.wo102.authorityHost = true; KCD2MP.hitSensorOn = false
    KCD2MP.w122.sharedWorld = true
    ENTS = {}; SPHERE = {}; ERRS = {}; CMDS = {}; SPAWNS = {}; TOASTS = {}
end


-- ================================================================ WO-135
local function emits(name)
    local n = 0
    for _, l in ipairs(LOG) do if string.find(l, " " .. name .. " ", 1, true) then n = n + 1 end end
    return n
end
local function fireTimers()
    local t = TIMERS; TIMERS = {}
    for _, x in ipairs(t) do pcall(x.f) end
end

do -- (a) mp_avatar_quiet: the mask, its groups, the event to the agent
    reset(); clearLog()
    check("a: default all four groups", KCD2MP.w135.quiet == 15)
    KCD2MP_W135SetQuiet("6")
    check("a: a mask is taken", KCD2MP.w135.quiet == 6 and logCount("WO135-QUIET quiet=6 (witness,react)") == 1)
    check("a: ...and told to the agent", logCount("w135_cfg quiet=6") == 1)
    KCD2MP_W135SetQuiet("16"); check("a: out of range refused", KCD2MP.w135.quiet == 6)
    KCD2MP_W135SetQuiet("on"); check("a: on = 15", KCD2MP.w135.quiet == 15)
    KCD2MP_W135SetQuiet("off"); check("a: off = 0", KCD2MP.w135.quiet == 0)
    clearLog(); KCD2MP_W135SetQuiet(""); check("a: bare = report, nothing sent", logCount("w135_cfg") == 0 and logCount("WO135-QUIET") == 1)
    KCD2MP_W135SetQuiet("on")
    check("a: the console commands are registered", CCMDS["mp_avatar_quiet"] ~= nil and CCMDS["mp_npc_ko_sync"] ~= nil and CCMDS["mp_w135_check"] ~= nil)
    check("a: no Lua errors", #ERRS == 0, ERRS[1])
end

do -- (b) knockouts follow the host's world (joiner)
    reset(); clearLog(); NOW = 300
    KCD2MP.w135.ko = {}
    local c = mkEntity("ttkc_drozd", 5, 0, 0); ENTS["ttkc_drozd"] = c
    KCD2MP.npcPuppets["ttkc_drozd"] = { dead = false, ko = true, everPacket = true }
    KCD2MP_W131Tick(true, true)
    check("b: the host has it down, it is up here: asked once", emits("w135_ko") == 1 and logCount("w135_ko ttkc_drozd 1") == 1)
    NOW = 301; KCD2MP_W131Tick(true, true)
    check("b: not asked again within the retry window", emits("w135_ko") == 1)
    NOW = 305; KCD2MP_W131Tick(true, true)
    check("b: asked again after 4 s while it still differs", emits("w135_ko") == 2)
    c.ko = true; NOW = 306; KCD2MP_W131Tick(true, true)
    check("b: settled: no more asks", emits("w135_ko") == 2 and KCD2MP.w135.ko["ttkc_drozd"] == nil)
    check("b: a knocked-out copy is never stood up by the stuck-pose rule", c.standups == 0)
    KCD2MP.npcPuppets["ttkc_drozd"].ko = false; NOW = 310; clearLog(); KCD2MP_W131Tick(true, true)
    check("b: the host's NPC is up: asked to wake", logCount("w135_ko ttkc_drozd 0") == 1)
    c.ko = false; c.revived = 0; c.actor.Revive = function() c.revived = c.revived + 1 end
    TIMERS = {}; KCD2MP_W135KoDone("ttkc_drozd", false, true); fireTimers()
    check("b: woken -> the engine's Revive puts it on its feet", c.revived == 1 and logCount("Revive(false) ok=true") == 1)
    local dead = mkEntity("ttkc_man_4", 6, 0, 0); ENTS["ttkc_man_4"] = dead; dead.dead = true
    KCD2MP.npcPuppets["ttkc_man_4"] = { dead = false, ko = true, everPacket = true }
    clearLog(); NOW = 320; KCD2MP_W131Tick(true, true)
    check("b: a body dead here is never knocked out", logCount("w135_ko ttkc_man_4") == 0)
    KCD2MP.w135.koSync = false; KCD2MP.npcPuppets["ttkc_drozd"].ko = true; clearLog(); NOW = 330; KCD2MP_W131Tick(true, true)
    check("b: mp_npc_ko_sync off: nothing asked", logCount("w135_ko") == 0)
    KCD2MP.w135.koSync = true
    KCD2MP_W131Tick(false, true); clearLog(); NOW = 340; KCD2MP_W135KoTick()
    check("b: never on the host (no copy guard)", logCount("w135_ko") == 0)
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

do -- (c) takedowns on a host-owned copy are requests (joiner)
    reset(); clearLog(); NOW = 400
    local called = {}
    BasicAIActions = {
        OnMercyKill = function(self, user) called[#called + 1] = "mercy" end,
        OnKnockout = function(self, user) called[#called + 1] = "knockout" end,
        OnStealthKill = function(self, user) called[#called + 1] = "stealth" end,
        OnLoot = function() end, OnPickpocketing = function() end,
    }
    KCD2MP.w135.OnMercyKillWrap = nil; KCD2MP.w135.OnKnockoutWrap = nil; KCD2MP.w135.OnStealthKillWrap = nil
    local c = mkEntity("ttkc_drozd", 5, 0, 0); ENTS["ttkc_drozd"] = c; c.ko = true
    KCD2MP_W131Tick(true, true)
    check("c: the three takedowns are wrapped", logCount("WO135-TAKEDOWN wrapped") == 3)
    BasicAIActions.OnMercyKill(c, player, 0)
    check("c: finishing a lying copy: no local kill", #called == 0)
    check("c: ...a request to the host", logCount("w135_takedown ttkc_drozd mercy") == 1)
    BasicAIActions.OnKnockout(c, player, 0)
    check("c: a knockout too", logCount("w135_takedown ttkc_drozd knockout") == 1 and #called == 0)
    local av = mkEntity("kcd2mp_0", 1, 0, 0); ENTS["kcd2mp_0"] = av
    BasicAIActions.OnMercyKill(av, player, 0)
    check("c: an avatar is not a host-owned copy: the game's own action runs", #called == 1)
    KCD2MP_W131Tick(false, true)
    BasicAIActions.OnMercyKill(c, player, 0)
    check("c: on the host (no guard) the game's own action runs", #called == 2)
    KCD2MP_W135TakedownResult("ttkc_drozd", "mercy", "refused")
    check("c: a refusal is told plainly", TOASTS[#TOASTS] == "Your host's world did not allow that.", TOASTS[#TOASTS])
    check("c: no Lua errors", #ERRS == 0, ERRS[1])
end

do -- (d) the host performs a joiner's takedown with the joiner's avatar
    reset(); clearLog(); NOW = 500; TIMERS = {}
    local av = mkEntity("kcd2mp_1", 1, 0, 0); ENTS["kcd2mp_1"] = av
    KCD2MP.ghosts[1] = { entity = av }
    local b = mkEntity("ttkc_drozd", 2, 0, 0); ENTS["ttkc_drozd"] = b; b.ko = true
    local asked = nil
    av.actor.RequestMercyKill = function(self, id) asked = id; b.dead = true end
    KCD2MP_W135HostTakedown(1, 9, "ttkc_drozd", "mercy")
    check("d: the avatar performs the game's own mercy kill", asked == b.id)
    fireTimers(); fireTimers()
    check("d: done -> ok to the joiner", logCount("w135_tdres 1 9 ok ttkc_drozd mercy") == 1)
    clearLog()
    local up = mkEntity("ttkc_man_7", 2, 0, 0); ENTS["ttkc_man_7"] = up
    KCD2MP_W135HostTakedown(1, 10, "ttkc_man_7", "mercy")
    check("d: finishing a standing NPC: refused", logCount("w135_tdres 1 10 refused") == 1 and logCount("refused (not-down)") == 1)
    local far = mkEntity("ttkc_man_8", 50, 0, 0); ENTS["ttkc_man_8"] = far; far.ko = true
    KCD2MP_W135HostTakedown(1, 11, "ttkc_man_8", "mercy")
    check("d: out of the avatar's reach: refused", logCount("w135_tdres 1 11 refused") == 1)
    KCD2MP_W135HostTakedown(2, 12, "ttkc_man_8", "mercy")
    check("d: no avatar for that peer: refused", logCount("w135_tdres 2 12 refused") == 1)
    clearLog(); TIMERS = {}
    local k = mkEntity("ttkc_man_9", 2, 0, 0); ENTS["ttkc_man_9"] = k; k.ko = true
    k.soul.GetState = function() return 40 end
    k.soul.DealDamage = function(self, st, hp) if hp >= 40 then k.dead = true end end
    av.actor.RequestMercyKill = function() end
    KCD2MP_W135HostTakedown(1, 13, "ttkc_man_9", "mercy"); fireTimers(); fireTimers()
    check("d: the avatar's request did not take: applied directly, then ok", logCount("applied directly") == 1 and logCount("w135_tdres 1 13 ok") == 1)
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

do -- (e) a ragdolled avatar is stood up (host)
    reset(); clearLog(); NOW = 600
    local av = mkEntity("kcd2mp_3", 1, 0, 0); ENTS["kcd2mp_3"] = av
    KCD2MP.ghosts["3"] = { entity = av, istate = { nativeSent = true, nativeOwned = true } }
    KCD2MP_NpcNativeAck("kcd2mp_3", 0, "not-living")
    check("e: the writer refuses a lying avatar -> StandUp", av.standups == 1 and logCount("WO135-STANDUP avatar=kcd2mp_3") == 1)
    NOW = 601; KCD2MP_NpcNativeAck("kcd2mp_3", 0, "not-living")
    check("e: throttled (3 s)", av.standups == 1)
    NOW = 605; av.dead = true; KCD2MP_NpcNativeAck("kcd2mp_3", 0, "not-living")
    check("e: never a dead body", av.standups == 1)
    check("e: never an NPC name", KCD2MP_W135AvatarStandUp("ttkc_man_1") == false)
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
