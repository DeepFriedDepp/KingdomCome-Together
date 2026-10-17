-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-195 synthetic test, the mod's half of the witness rule and of the dead copy (engine stubbed, fake clock; the stubs are
-- Test-WO139Synthetic.lua's), against the real kdcmp.lua under MoonSharp. Driven by Test-WO195Synthetic.ps1.
--   W  the witness rule factor by factor: asleep (IsSleeping, the host's bed read, woken by noise), crouch, night, interiors, a light,
--      the stealth skill, the game's visibility and noise stats; the acceptance pair; the switch off; a 0.60.9 joiner; the snapshot
--   D  the joiner's combat state after a death; a copy killed far away and bound later (the owner's death on its first packet)
--   C  the switches and the commands
-- What this proves: the Lua half. Live evidence: docs/WO-195-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}; LOCKS = {}; MSGS = {}; RAYS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
local function d2(a, b) return (a.x - b.x) ^ 2 + (a.y - b.y) ^ 2 + (a.z - b.z) ^ 2 end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetCVar = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntity = function(id) for _, e in pairs(ENTS) do if e.id == id then return e end end return nil end
System.GetEntitiesInSphere = function(p, r)
    local o = {}
    for _, e in pairs(ENTS) do
        if e.GetWorldPos and d2(e:GetWorldPos(), p) <= r * r then o[#o + 1] = e end
    end
    table.sort(o, function(a, b) return a.id < b.id end)
    return o
end
System.GetEntitiesByClass = function(cls)
    local o = {}
    for _, e in pairs(ENTS) do if e.class == cls then o[#o + 1] = e end end
    table.sort(o, function(a, b) return a.id < b.id end)
    return o
end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Terrain = mkstub()
Game.AddSaveLock = function(name) LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) LOCKS[name] = nil; return true end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }
Framework = { IsValidWUID = function(w) return w ~= nil and w ~= 0 and w ~= "0" end }
EntityModule = mkstub()
OWNERS = {}          -- inventory id -> owner wuid
EntityModule.GetInventoryOwner = function(inv) return OWNERS[inv] end
ent_all = 287

-- The engine's typed message tables (XGenAIModule.MakeTableFromType, dumped live on 1.5.5).
local TYPES = {
    ["switch:stimulus:theft"] = function() return { kettleType = 0, method = 0, owner = 0, treatAsPersonalSource = false, pivot = 0, count = 0,
        shouldCheckHomeStashes = false, freshlyAttributedCrime = false, isNonAttributed = false,
        information = { position = { x = 0, y = 0, z = 0 }, perceivedWuid = 0, label = "" }, immediate = true } end,
    ["switch:stimulus:escalatedTrespass"] = function() return { wuidType = 0, home = 0, trespassingRepeatedly = false, trespassArea = 0,
        isKzikTrespass = false, createInformationOnly = false, stimulusKind = 0 } end,
    ["switch:stimulus:disturbance"] = function() return { perceivedWuid = 0, priceOverride = -1, skipInitialReaction = false } end,
    ["crime:attackInitiatedByConcept"] = function() return { target = 0, priorityTarget = false } end,
    ["stopFight"] = function() return { soulCount = 0, messageId = "" } end,
}
XGenAIModule = mkstub()
XGenAIModule.MakeTableFromType = function(t) local f = TYPES[t]; return f and f() or nil end
XGenAIModule.SendMessageToEntityData = function(to, kind, t) MSGS[#MSGS + 1] = { to = to, kind = kind, t = t } end
WUIDS = {}           -- wuid -> entity
XGenAIModule.GetEntityByWUID = function(w) return WUIDS[w] end
-- WO-157: the engine's area labels at a point (the host's own trespass check). Every spot is private here unless a
-- test says otherwise; AREA_FAIL makes the engine's call fail (unknown).
AREA_LABELS = { private = true }
AREA_FAIL = false
XGenAIModule.IsPointInAreaWithLabel = function(p, l) if AREA_FAIL then error("no answer") end return AREA_LABELS[l] == true end
enum_crime_theftMethod = { unknown = 0, loot = 1, lootCorpse = 2, lootUnconsciousBody = 3, kettleEating = 4, pick = 5, pickpocket = 6, seenEquipped = 7 }
enum_crime_resolutionKind = { fine = 0, leaveUnconscious = 1, punishment = 2, questPunishment = 3, skillCheck = 4, fight = 5, secondArrest = 6 }
enum_crime_stimulusKind = { trespass = 40, escalatedTrespass = 13, theft = 38 }
enum_crime_trespassInformationWuid = { none = 0, home = 1, homeArea = 2 }

-- The ray: RAYS[npcName] = { dist = m, entity = e } (the first hit), nil = nothing hit.
Physics = mkstub()
Physics.RayWorldIntersection = function(from, dir, n, types, skip)
    for name, r in pairs(RAYS) do
        local e = ENTS[name]
        if e and e.id == skip then return r.hit and { { dist = r.dist, entity = r.entity } } or {} end
    end
    return {}
end

-- ---- items and inventories (as in Test-WO134Synthetic.lua) ----
ITEMS = {}
NEXTWUID = 100
local function newItem(cls, hp, amt)
    NEXTWUID = NEXTWUID + 1
    local w = "wuid" .. NEXTWUID
    ITEMS[w] = { class = cls, amount = amt or 1, health = hp or 1, id = w }
    return w
end
ItemManager = mkstub()
ItemManager.GetItem = function(w) return ITEMS[w] end
local function mkInventory(id)
    local inv = { list = {}, invId = id or "inv" }
    inv.GetInventoryTable = function(self) local t = {}; for i, w in ipairs(self.list) do t[i] = w end; return t end
    inv.CreateItem = function(self, cls, hp, amt) local w = newItem(cls, hp, amt); self.list[#self.list + 1] = w; return true end
    inv.AddItem = function(self, w)
        for n, e in pairs(ENTS) do if e.class == "PickableItem" and e.item and e.item.GetId() == w then ENTS[n] = nil end end
        self.list[#self.list + 1] = w
    end
    inv.RemoveItem = function(self, w) for i, x in ipairs(self.list) do if x == w then table.remove(self.list, i); return w end end end
    inv.GetCountOfClass = function(self, cls) local n = 0; for _, w in ipairs(self.list) do if ITEMS[w] and ITEMS[w].class == cls then n = n + ITEMS[w].amount end end; return n end
    inv.GetId = function(self) return self.invId end
    inv.GetMoney = function(self) return MONEY end
    return inv
end
MONEY = 212.1

NEXTID = 5000
local STEAL = {}     -- pickable name -> CanSteal answer
local function mkPickable(name, cls, x, y, z, wuid)
    NEXTID = NEXTID + 1
    local e = { class = "PickableItem", id = NEXTID, px = x, py = y, pz = z, Properties = { sItemClassId = cls, nAmount = 1, fHealth = 1 } }
    e.GetName = function() return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.SetFlags = function() end
    e.item = { GetId = function() return wuid end, BelongsToDeadBody = function() return false end,
               CanSteal = function() return STEAL[name] == true end, CanUse = function() return true end,
               GetOwnerId = function() return 0 end,
               OnUsed = function(_, uid) if not ENTS[name] then return false end ENTS[name] = nil; player.inventory:AddItem(wuid); return true end,
               OnSteal = function(_, uid) if not ENTS[name] then return false end ENTS[name] = nil; player.inventory:AddItem(wuid); return true end }
    setmetatable(e, { __index = function(_, k) return PickableItem and PickableItem[k] end })
    ENTS[name] = e
    return e
end

player = { id = 1, class = "Player", inventory = mkInventory("pinv"), this = { id = "pwuid" } }
PLAYER_POS = { x = 100, y = 100, z = 10 }
PLAYER_IN_DIALOG = false; PLAYER_DANGER = false; PLAYER_DEAD = false; PLAYER_HORSE = 0
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PLAYER_POS.x, y = PLAYER_POS.y, z = PLAYER_POS.z } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return PLAYER_DEAD end }
player.human = { IsInDialog = function() return PLAYER_IN_DIALOG end }
player.soul = { IsInCombatDanger = function() return PLAYER_DANGER end }
player.player = { GetHorseId = function() return PLAYER_HORSE end }
ENTS["Dude"] = player

-- The game's own script tables the mod wraps (defined before the mod loads, as in the game).
PickableItem = {}
function PickableItem:Use(user) if user then return self.item:OnUsed(user.id) end return false end
function PickableItem:OnUsed(user) return self:Use(user) end
function PickableItem:OnUsedHold(user) if user and self.item:CanSteal(user.id) then return self.item:OnSteal(user.id) end return false end
BasicAIActions = {}
TALKS = {}
for _, fn in ipairs({ "OnTalk", "OnChat", "OnChatWithFocus", "OnChatRequestAccepted", "OnChatOpen", "OnLoot", "OnPickpocketing",
                      "OnMercyKill", "OnKnockout", "OnStealthKill" }) do
    local f = fn
    BasicAIActions[f] = function(self, user, slot) TALKS[#TALKS + 1] = f .. ":" .. tostring(self and self:GetName()) end
end
Stash = {}
STASH_OPENED = {}
function Stash:OnUsed(user, slot) if self.bOpened ~= 1 then self.bOpened = 1; STASH_OPENED[#STASH_OPENED + 1] = self:GetName() else self.bOpened = 0 end end
function Stash:GetInventoryToOpen() return self.inventory:GetId() end
function Stash:UsesStealUiPrompt() return self.crimeStash == true end
Minigame = { StartLockPicking = function(id) LOCKPICKS = (LOCKPICKS or 0) + 1; return true end }
Horse = {}
MOUNTS = {}
function Horse:OnMount(user, slot) MOUNTS[#MOUNTS + 1] = self:GetName() end
function Horse:IsMountLegal() return self.mountIsLegal == true or self.mountIsLegalFromAI == true end
function Horse:SetMountIsLegal(v) self.mountIsLegal = v end
Crime = { SendResolveDialogResult = function(dc, action) RESOLVED = action end }

-- @@KDCMP@@

-- Part 2: scenarios.

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function logCount(pat, from)
    local n = 0
    for i = (from or 0) + 1, #LOG do if string.find(LOG[i], pat, 1, true) then n = n + 1 end end
    return n
end
local function clearLog() LOG = {} end
local function cmdCount(pat)
    local n = 0
    for _, c in ipairs(CMDS) do if string.find(c, pat, 1, true) then n = n + 1 end end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
local emitted
local function e1(name, from) return emitted(name, from)[1] or "" end
emitted = function(name, from)
    local out = {}
    local pat = "[KCD2-MP-EVT] v1 "
    for i = (from or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find(pat, 1, true) then
            local rest = l:sub(#pat + 1)
            local ev, arg = rest:match("^%d+ (%S+) ?(.*)$")
            if ev == name then out[#out + 1] = arg end
        end
    end
    return out
end

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return table.unpack(r)
end

local function mkNpc(name, x, y, z, faction, opts)
    NEXTID = NEXTID + 1
    opts = opts or {}
    local e = { class = opts.class or "NPC", id = NEXTID, px = x, py = y, pz = z or 10, hidden = false, fwd = opts.fwd or { x = 0, y = 1, z = 0 },
                this = { id = "w_" .. name }, inventory = mkInventory("inv_" .. name) }
    e.GetName = function() return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.SetWorldPos = function(self, p) self.px, self.py, self.pz = p.x, p.y, p.z end
    e.GetDirectionVector = function(self, axis) return self.fwd end
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.dead, e.ko, e.combat = false, false, false
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return e.ko end, GetHealth = function() return 100 end,
                StandUp = function() end }
    e.human = { IsInDialog = function() return false end, IsSleeping = function() return e.sleeping == true end }
    local ctx = opts.ctx or {}
    e.soul = { GetFactionID = function() return faction end, HasScriptContext = function(_, c) return ctx[c] == true end,
               IsPublicEnemy = function() return opts.enemy == true end, IsInCombatMode = function() return e.combat end,
               IsLegalToLoot = function() return opts.legalLoot ~= false end, DealDamage = function() end }
    WUIDS[e.this.id] = e
    ENTS[name] = e
    return e
end
local ZEL = "trosecko_settlements_zelejov_commonFolk_peasants_parcel01"
local GUARD_F = "trosecko_settlements_zelejov_soldiers_militia"
local GUARD_CTX = { crime_isAuthority = true, crime_isAuthorityOnDuty = true }

local function avatar(id, x, y)
    local g = mkNpc("kcd2mp_" .. id, x, y, 10, "player")
    KCD2MP.ghosts[tostring(id)] = { entity = g }
    return g
end

local function reset()
    KCD2MP.ghosts = {}; KCD2MP.horseGhosts = {}; KCD2MP.npcPuppets = {}
    KCD2MP._npcPaused = {}; KCD2MP._npcResumePending = {}; KCD2MP._npcEverPaused = {}
    KCD2MP.w131.parked = {}; KCD2MP.w131.standins = {}; KCD2MP.w131.active = false; KCD2MP.w131.guard = true
    KCD2MP.wo102.authorityHost = true; KCD2MP.wo102.authorityPause = true; KCD2MP.hitSensorOn = false
    KCD2MP.w122.sharedWorld = true
    local w = KCD2MP.w139
    w.shared = true; w.host = false; w.joiner = false; w.on = false; w.aliveAt = nil
    w.crimes = {}; w.stash = nil; w.stop = nil; w.stopped = {}; w.legal = {}; w.skipOrig = {}; w.skipGate = false
    ENTS = { Dude = player }; WUIDS = {}; RAYS = {}; STEAL = {}; OWNERS = {}; ERRS = {}; CMDS = {}; TOASTS = {}; TALKS = {}; MSGS = {}
    STASH_OPENED = {}; MOUNTS = {}; LOCKPICKS = 0; RESOLVED = nil
    player.inventory = mkInventory("pinv"); MONEY = 212.1
    PLAYER_POS = { x = 100, y = 100, z = 10 }; PLAYER_IN_DIALOG = false; PLAYER_DANGER = false; PLAYER_DEAD = false; PLAYER_HORSE = 0
end
local function joiner() KCD2MP_W139Session(false, true, true) end
local function host() KCD2MP_W139Session(true, false, true) end

-- ================================================================ WO-195 2: the witness rule, factor by factor

local function judge(id, kind, stealth)
    local mark = #LOG
    KCD2MP_W139HostJudge(1, id, kind or "trespass", 100, 100, 10, "-", "-", "area", stealth)
    local ev = emitted("w139_judged", mark)[1] or ""
    local n = tonumber(string.match(ev, "^%S+ %S+ %S+ (%d+)")) or -1
    return n, mark, ev
end
local function wline(mark, npc)
    for i = mark + 1, #LOG do if LOG[i]:find("WO195-WITNESS", 1, true) and LOG[i]:find("npc=" .. npc .. " ", 1, true) then return LOG[i] end end
    return nil
end
local STAND = "c=0,l=0,v=-99.00,n=-99.00,s=-99.00,k=-1"
local NOON, MIDNIGHT = 12 * 3600, 0
local function scene(dist, opts)
    reset(); clearLog(); NOW = 3000
    opts = opts or {}
    WORLD_T = opts.t or NOON
    INDOOR = opts.indoor == true
    host()
    local av = avatar(1, 100, 100)
    -- the NPC faces the avatar (it stands at +x, looks -x), a clear ray
    local npc = mkNpc("tzel_man_3", 100 + dist, 100, 10, ZEL, { fwd = opts.away and { x = 1, y = 0, z = 0 } or { x = -1, y = 0, z = 0 } })
    RAYS["tzel_man_3"] = { hit = true, dist = dist, entity = av }
    return av, npc
end
System.IsPointIndoors = function(p) return INDOOR == true end

do -- the baseline: standing, unlit, noon, 10 m, in view
    scene(10)
    local n, mark = judge(201, "trespass", STAND)
    check("W1 standing in daylight at 10 m: seen", n == 1, n)
    local l = wline(mark, "tzel_man_3")
    check("W1 ...and the line says why (dist, asleep, lit, dark, crouch, the radii, counted=seen)",
        l ~= nil and l:find("dist=10.0 asleep=0 lit=0 dark=day", 1, true) ~= nil and l:find("sight_m=20.0", 1, true) ~= nil and l:find("counted=seen", 1, true) ~= nil, l)
    noErrs("W1")
end

do -- 2a asleep: IsSleeping, at arm's length
    local _, npc = scene(1.2)
    npc.sleeping = true
    local n, mark = judge(202, "trespass", "c=1,l=0,v=-99.00,n=-99.00,s=-99.00,k=-1")
    check("W2 a sleeper (IsSleeping) beside a crouching figure: not a witness", n == 0, n)
    check("W2 ...ignored=asleep, how", (wline(mark, "tzel_man_3") or ""):find("asleep=1(IsSleeping)", 1, true) ~= nil and (wline(mark, "tzel_man_3") or ""):find("ignored=asleep", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    noErrs("W2")
end

do -- 2a asleep: the host's own activity read (lying on its bed) while IsSleeping says false
    scene(1.2)
    KCD2MP_W195Asleep("tzel_man_3", 1)
    local n, mark = judge(203, "trespass", "c=1,l=0,v=-99.00,n=-99.00,s=-99.00,k=-1")
    check("W3 lying on its bed (the activity read), IsSleeping false: asleep, no witness", n == 0 and (wline(mark, "tzel_man_3") or ""):find("asleep=1(bed)", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    KCD2MP_W195Asleep("tzel_man_3", 0)
    n = judge(204, "trespass", STAND)
    check("W3 out of bed again (the agent's 0): awake, it sees him", n == 1, n)
    noErrs("W3")
end

do -- 2a: a sleeper wakes for a walking figure inside the footstep radius (1.5 x sqrt(0.43) = 0.98 m), never for one standing still
    local WALK = "c=0,l=0,v=-99.00,n=0.43,s=-99.00,k=-1"
    local _, npc = scene(0.8)
    npc.sleeping = true
    local n, mark = judge(205, "trespass", WALK)
    check("W4 a sleeper at 0.8 m from a walking figure: woken, a witness", n == 1 and (wline(mark, "tzel_man_3") or ""):find("counted=woken", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    _, npc = scene(1.5)
    npc.sleeping = true
    n, mark = judge(2051, "trespass", WALK)
    check("W4 ...at 1.5 m: not (the engine wakes nobody beyond ~1 m for this sound)", n == 0 and (wline(mark, "tzel_man_3") or ""):find("ignored=asleep", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    _, npc = scene(0.5)
    npc.sleeping = true
    n, mark = judge(2052, "trespass", STAND)
    check("W4 a figure standing still at 0.5 m (no peak noise): the sleeper sleeps on", n == 0, wline(mark, "tzel_man_3"))
    noErrs("W4")
end

do -- 2c crouch: 15 m by day is seen standing, not crouching (20 x 0.5 = 10 m)
    scene(15)
    check("W5 standing at 15 m by day: seen", judge(206, "trespass", STAND) == 1)
    scene(15)
    local n, mark = judge(207, "trespass", "c=1,l=0,v=-99.00,n=-99.00,s=-99.00,k=-1")
    check("W5 crouching at 15 m by day: out of the crouch's 10 m", n == 0 and (wline(mark, "tzel_man_3") or ""):find("sight_m=10.0", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    noErrs("W5")
end

do -- 2c night outdoors: 10 m is out of the night's 6 m (20 x 0.3); a torch puts the day back
    scene(10, { t = MIDNIGHT })
    local n, mark = judge(208, "trespass", STAND)
    check("W6 standing at 10 m at midnight outdoors: not seen (night 6 m)", n == 0 and (wline(mark, "tzel_man_3") or ""):find("dark=night", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    scene(10, { t = MIDNIGHT })
    n = judge(209, "trespass", "c=0,l=1,v=-99.00,n=-99.00,s=-99.00,k=-1")
    check("W6 the same with a torch in hand: seen (a light cancels the dark)", n == 1, n)
    scene(10, { t = MIDNIGHT })
    KCD2MP.w136.avatarTorch["1"] = true
    n = judge(210, "trespass", STAND)
    KCD2MP.w136.avatarTorch["1"] = nil
    check("W6 ...or his figure's torch here (the stream's torch bit)", n == 1, n)
    noErrs("W6")
end

do -- 2c interiors (his vib unread): by day 20 x 0.7 = 14 m; at night as dark as outside
    scene(15, { indoor = true })
    local n, mark = judge(211, "trespass", STAND)
    check("W7 indoors by day at 15 m: out of the interior's 14 m", n == 0 and (wline(mark, "tzel_man_3") or ""):find("dark=interior ", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    scene(13, { indoor = true })
    check("W7 ...at 13 m: seen", judge(212, "trespass", STAND) == 1)
    scene(5, { indoor = true, t = MIDNIGHT })
    local n2, mark2 = judge(213, "trespass", "c=1,l=0,v=-99.00,n=-99.00,s=-99.00,k=-1")
    check("W7 a dark room at night, crouching, 5 m: not seen (the old scale's floor, 2.4 m)", n2 == 0 and (wline(mark2, "tzel_man_3") or ""):find("dark=interior-night", 1, true) ~= nil, wline(mark2, "tzel_man_3"))
    noErrs("W7")
end

do -- 2c stealth skill: the full skill takes 30 % off (20 x 0.7 = 14 m)
    scene(18)
    check("W8 no skill at 18 m: seen", judge(214, "trespass", STAND) == 1)
    scene(15)
    local n, mark = judge(215, "trespass", "c=0,l=0,v=-99.00,n=-99.00,s=-99.00,k=30")
    check("W8 stealth 30 at 15 m: out of 14 m", n == 0 and (wline(mark, "tzel_man_3") or ""):find("sight_m=14.0", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    noErrs("W8")
end

do -- 2c visibility (the game's own derived stat vib: stance, light, time of day and equipment are in it)
    local P = KCD2MP.w195.P
    scene(17, { t = MIDNIGHT })
    local hi = string.format("c=0,l=0,v=%.2f,n=-99.00,s=-99.00,k=-1", P.visRef + 1.5)   -- a lit spot at night: a bigger radius than by day
    check("W9 a high vib (a lit spot) at night at 17 m: seen", judge(216, "trespass", hi) == 1)
    scene(5)
    local lo = string.format("c=0,l=0,v=%.2f,n=-99.00,s=-99.00,k=-1", P.visRef - 4.0)   -- the floor (10 % of 20 m)
    local n, mark = judge(217, "trespass", lo)
    check("W9 a very low vib at 5 m by day: the floor (2 m), not seen", n == 0 and (wline(mark, "tzel_man_3") or ""):find("dark=vib", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    -- the engine's own readings of the host's Henry (standing noon 1.19, crouching noon 0.69, standing at night -0.95, crouching at night -1.45)
    local function vstr(v) return string.format("c=0,l=0,v=%.2f,n=0.00,s=0.60,k=-1", v) end
    scene(15)
    check("W9 noon standing (vib 1.19) at 15 m: seen", judge(2161, "trespass", vstr(1.19)) == 1)
    scene(15)
    check("W9 noon crouching (vib 0.69) at 15 m: seen (the engine: -0.5 on the visibility only)", judge(2162, "trespass", vstr(0.69)) == 1)
    scene(12, { t = MIDNIGHT })
    check("W9 night standing (vib -0.95) at 12 m: not seen", judge(2163, "trespass", vstr(-0.95)) == 0)
    scene(7, { t = MIDNIGHT })
    check("W9 night standing (vib -0.95) at 7 m: seen", judge(2164, "trespass", vstr(-0.95)) == 1)
    scene(8, { t = MIDNIGHT })
    check("W9 night crouching (vib -1.45) at 8 m: not seen (20 x 0.34 = 6.8 m)", judge(2165, "trespass", vstr(-1.45)) == 0)
    scene(5, { t = MIDNIGHT })
    check("W9 night crouching (vib -1.45) at 5 m: seen", judge(2166, "trespass", vstr(-1.45)) == 1)
    noErrs("W9")
end

do -- 2c noise: an awake NPC looking away hears a walking figure inside 1.5 x sqrt(peak), never one standing still
    scene(0.9, { away = true })
    local n, mark = judge(218, "trespass", "c=0,l=0,v=-99.00,n=0.43,s=-99.00,k=-1")
    check("W10 looking away, 0.9 m, walking (0.43): heard", n == 1 and (wline(mark, "tzel_man_3") or ""):find("counted=heard", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    scene(3, { away = true })
    n, mark = judge(219, "trespass", "c=0,l=0,v=-99.00,n=0.43,s=-99.00,k=-1")
    check("W10 the same at 3 m: unseen, unheard", n == 0 and (wline(mark, "tzel_man_3") or ""):find("ignored=unseen", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    scene(0.9, { away = true })
    n, mark = judge(2191, "trespass", "c=1,l=0,v=-99.00,n=0.00,s=-99.00,k=-1")
    check("W10 standing/crouching still (peak 0): unheard even at 0.9 m", n == 0 and (wline(mark, "tzel_man_3") or ""):find("ignored=unseen", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    scene(2.5, { away = true })
    n = judge(220, "trespass", "c=0,l=0,v=-99.00,n=2.50,s=-99.00,k=-1")   -- a sprint in loud armour: 1.5 x sqrt(2.5) = 2.4 m: still out at 2.5
    check("W10 a sprint (2.5) at 2.5 m looking away: just out of the 2.4 m", n == 0)
    scene(2.2, { away = true })
    n = judge(2201, "trespass", "c=0,l=0,v=-99.00,n=2.50,s=-99.00,k=-1")
    check("W10 ...at 2.2 m: heard", n == 1)
    noErrs("W10")
end

do -- the acceptance pair: a dark room at night, crouching and still beside a sleeper in bed -- then standing, lit, by day
    local _, npc = scene(1.0, { indoor = true, t = 23 * 3600 })
    KCD2MP_W195Asleep("tzel_man_3", 1)
    local n, mark = judge(221, "trespass", "c=1,l=0,v=-1.45,n=0.00,s=0.10,k=7")
    check("W11 crouching still in a dark room beside a sleeper in bed: not witnessed", n == 0, wline(mark, "tzel_man_3"))
    KCD2MP_W195Asleep("tzel_man_3", 0)
    _, npc = scene(1.0)
    n = judge(222, "trespass", "c=0,l=1,v=1.19,n=0.00,s=0.60,k=7")
    check("W11 standing, lit, in daylight, beside the same NPC awake: witnessed", n == 1, n)
    noErrs("W11")
end

do -- the switch off: 0.60.9's rule (25 m, geometry only; only IsSleeping excludes)
    scene(10, { t = MIDNIGHT })
    local m0 = #LOG
    KCD2MP_W195SetStealth("off")
    check("W12 the toggle reaches the agent", e1("w195_cfg", m0) == "dead=on stealth=off bodyloot=on", e1("w195_cfg", m0))
    local n, mark = judge(223, "trespass", "c=1,l=0,v=-99.00,n=-99.00,s=-99.00,k=30")
    check("W12 mp_stealth_witness off: the old geometry (seen at night, crouching, at 10 m), no WO195-WITNESS line", n == 1 and wline(mark, "tzel_man_3") == nil, n)
    KCD2MP_W195SetStealth("on")
    noErrs("W12")
end

do -- a 0.60.9 joiner (no stealth field): the dark and the sleepers still count
    scene(10, { t = MIDNIGHT })
    local n = judge(224, "trespass", "")
    check("W13 no stealth field at midnight, 10 m: the night's radius still applies (not seen)", n == 0, n)
    check("W13 a malformed field is nothing known", KCD2MP_W195ParseStealth("c=1;l=0").known == false and KCD2MP_W195ParseStealth(STAND).known == true)
    noErrs("W13")
end

do -- 2b the joiner's snapshot: built from his own reads, sent on a change and once in 30 s
    reset(); clearLog(); NOW = 4000
    player.soul.GetDerivedStat = function(_, n) return ({ vis = 1.25, noi = 0.8, con = 0.5 })[n] end
    player.soul.GetSkillLevel = function(_, n) return n == "stealth" and 7 or 0 end
    KCD2MP.playerSneaking = true
    KCD2MP.w136.torchLocal = false
    joiner()
    local s1 = e1("w195_stealth", 0)
    check("W14 the joiner's session tick sends his stealth", s1 == "c=1,l=0,v=1.25,n=0.80,s=0.50,k=7", s1)
    local mark = #LOG
    joiner()
    check("W14 ...not again while it is the same", #emitted("w195_stealth", mark) == 0)
    KCD2MP.playerSneaking = false
    KCD2MP.w136.torchLocal = true
    joiner()
    check("W14 ...again on a change (stood up, torch out)", e1("w195_stealth", mark) == "c=0,l=1,v=1.25,n=0.80,s=0.50,k=7", e1("w195_stealth", mark))
    NOW = NOW + 31; mark = #LOG
    joiner()
    check("W14 ...and once in 30 s anyway (a restarted agent hears it)", #emitted("w195_stealth", mark) == 1)
    player.soul.GetDerivedStat = nil; player.soul.GetSkillLevel = nil
    KCD2MP.w136.torchLocal = false
    local t = KCD2MP_W195Stealth()
    check("W14 unread stats are -99 (the host then uses its defaults)", t == "c=0,l=0,v=-99.00,n=-99.00,s=-99.00,k=-1", t)
    noErrs("W14")
end

-- ================================================================ WO-195B: the engine as the reference

do -- the joiner's noise level and its 2 s peak window (the instant reading is 0 whenever he stands still)
    local P = KCD2MP.w195.P
    check("B1 standing still is silent whatever he wears (fsm 0.43, speed 0)", KCD2MP_W195NoiseLevel(0.43, 0, 0) == 0)
    check("B1 walking in plain clothes: the footstep multiplier (0.43)", math.abs(KCD2MP_W195NoiseLevel(0.43, 0, P.speedWalk) - 0.43) < 1e-9)
    check("B1 crouch-walking: the crouch's multiplier (0.129)", math.abs(KCD2MP_W195NoiseLevel(0.129, 0, P.speedWalk) - 0.129) < 1e-9)
    check("B1 a sprint is capped (x2.5)", math.abs(KCD2MP_W195NoiseLevel(0.43, 0, 50) - 0.43 * P.speedMaxFactor) < 1e-9)
    check("B1 the instant noise stat adds when it reads", math.abs(KCD2MP_W195NoiseLevel(0.43, 0.3, 0) - 0.3) < 1e-9)
    local pk = {}
    KCD2MP_W195PeakPush(pk, 100.0, 0.0)
    KCD2MP_W195PeakPush(pk, 100.5, 0.43)
    KCD2MP_W195PeakPush(pk, 101.0, 0.0)
    check("B2 the peak of the last 2 s keeps the spike after it ends", KCD2MP_W195PeakGet(pk, 101.5) == 0.43)
    check("B2 ...and forgets it once it is older than the window", KCD2MP_W195PeakGet(pk, 102.6) == 0)
    for i = 1, 200 do KCD2MP_W195PeakPush(pk, 110 + i * 0.2, 0.1) end
    check("B2 the window holds at most the last 2 s of samples", #pk <= math.ceil(P.peakS / 0.2) + 2, #pk)
    noErrs("B1/B2")
end

do -- the sampler: his speed from his own positions, a teleport is no step, the stealth text carries the peak
    reset(); clearLog(); NOW = 6000
    KCD2MP.w195.peak = {}; KCD2MP.w195.noiseAt = -1e9; KCD2MP.w195.lastPos = nil
    player.soul.GetDerivedStat = function(_, n) return ({ fsm = 0.43, noi = 0, vib = 1.19, con = 0.6 })[n] end
    player.soul.GetSkillLevel = function(_, n) return n == "stealth" and 7 or 0 end
    PLAYER_POS = { x = 100, y = 100, z = 10 }
    KCD2MP.playerSneaking = false
    KCD2MP_W195PollNoise()                          -- first sample: no previous position, no speed
    NOW = NOW + 0.25; PLAYER_POS = { x = 100.45, y = 100, z = 10 }   -- 1.8 m/s: the walking pace
    KCD2MP_W195PollNoise()
    NOW = NOW + 0.25; PLAYER_POS = { x = 100.45, y = 100, z = 10 }   -- stands again
    KCD2MP_W195PollNoise()
    local t = KCD2MP_W195Stealth()
    check("B3 his stealth text: vib, the 2 s peak of the footsteps (0.43) although he stands now, conspicuousness, skill", t == "c=0,l=0,v=1.19,n=0.43,s=0.60,k=7", t)
    NOW = NOW + 2.6
    KCD2MP_W195PollNoise()
    t = KCD2MP_W195Stealth()
    check("B3 ...and the peak is gone 2 s after he stopped", t == "c=0,l=0,v=1.19,n=0.00,s=0.60,k=7", t)
    NOW = NOW + 0.25; PLAYER_POS = { x = 300, y = 100, z = 10 }   -- a load / a teleport: 800 m/s
    KCD2MP_W195PollNoise()
    check("B3 a teleport is no step", KCD2MP_W195PeakGet(KCD2MP.w195.peak, NOW) == 0)
    player.soul.GetDerivedStat = function(_, n) return ({ fsm = 0.129, vib = 0.69 })[n] end
    PLAYER_POS = { x = 100, y = 100, z = 10 }; KCD2MP.w195.lastPos = nil
    t = KCD2MP_W195Stealth()
    check("B3 the crouch is read from his footstep multiplier too (0.129 < 0.25) when no key flag says so", t:sub(1, 3) == "c=1", t)
    player.soul.GetDerivedStat = nil; player.soul.GetSkillLevel = nil
    PLAYER_POS = { x = 100, y = 100, z = 10 }
    noErrs("B3")
end

do -- the sight factor: his vib when it reads, the old terms when it does not; the NPC's own view radius and field of view
    local P = KCD2MP.w195.P
    local ctx = KCD2MP_W195ParseStealth("c=0,l=0,v=1.19,n=0.00,s=0.60,k=0")
    local env = { indoor = false, night = false }
    local sight, noise, word = KCD2MP_W195Reach(ctx, env, nil)
    check("B4 standing in daylight (vib 1.19): the nominal 20 m, no noise", math.abs(sight - 20.0) < 1e-6 and noise == 0 and word == "vib", string.format("%.2f %.2f %s", sight, noise, word))
    sight = KCD2MP_W195Reach(ctx, env, 28.15)
    check("B4 an alerted NPC (vir 28.15) sees farther in proportion", math.abs(sight - 28.15) < 1e-6, sight)
    ctx = KCD2MP_W195ParseStealth("c=0,l=0,v=9.00,n=0.00,s=0.60,k=0")
    sight = KCD2MP_W195Reach(ctx, env, 20)
    check("B4 the factor is capped at the engine's VisionToViewRadius (1.35)", math.abs(sight - 27.0) < 1e-6, sight)
    ctx = KCD2MP_W195ParseStealth("c=0,l=0,v=-1.45,n=0.00,s=0.60,k=0")
    local lo = KCD2MP_W195Reach(ctx, env, 20)
    check("B4 crouching in the night's dark (vib -1.45): a few metres", lo > 2 and lo < 8, lo)
    ctx = KCD2MP_W195ParseStealth("c=0,l=0,v=-99.00,n=-99.00,s=-99.00,k=-1")
    local s0, _, w0 = KCD2MP_W195Reach(ctx, { indoor = false, night = true }, nil)
    check("B4 vib unread (a 0.60.9 joiner): the old night term (20 x 0.3)", math.abs(s0 - 6.0) < 1e-6 and w0 == "night", string.format("%.2f %s", s0, w0))
    noErrs("B4")
end

do -- the NPC's own field of view (its fov stat) and view radius (its vir): a copy that reads 100 deg sees less behind it than the old 210 deg
    local _, npc = scene(10)
    npc.soul.GetDerivedStat = function(_, n) return ({ vir = 21.4, fov = 100 })[n] end
    npc.fwd = { x = 0, y = 1, z = 0 }                 -- looking along +y; the figure is at -x: 90 deg off, outside +-55 deg
    local n, mark = judge(230, "trespass", "c=0,l=0,v=1.19,n=0.00,s=0.60,k=0")
    check("B5 90 deg off the NPC's own 100 deg field of view at 10 m: not seen", n == 0, wline(mark, "tzel_man_3"))
    npc.fwd = { x = -1, y = 0, z = 0 }
    n, mark = judge(231, "trespass", "c=0,l=0,v=1.19,n=0.00,s=0.60,k=0")
    check("B5 straight ahead: seen, and the line carries its vir and fov", n == 1 and (wline(mark, "tzel_man_3") or ""):find("vir=21.4 fov=100", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    _, npc = scene(0.25)
    npc.fwd = { x = 0, y = 1, z = 0 }
    n = judge(232, "trespass", "c=0,l=0,v=1.19,n=0.00,s=0.60,k=0")
    check("B5 anyone within 0.6 m sees him whichever way it looks (a pickpocket works from behind at 1 m, not at arm's length)", n == 1, n)
    _, npc = scene(1.0)
    npc.fwd = { x = 0, y = 1, z = 0 }
    n = judge(233, "trespass", "c=0,l=0,v=1.19,n=0.00,s=0.60,k=0")
    check("B5 ...but at 1 m, out of its field of view and silent, he is not seen", n == 0, n)
    noErrs("B5")
end

do -- the game's own interior state (the engine's area label), then System.IsPointIndoors, then a roof above him
    reset(); clearLog()
    local label = false
    local origIndoors, origRay, origArea = System.IsPointIndoors, Physics.RayWorldIntersection, rawget(XGenAIModule, 'IsPointInAreaWithLabel')
    XGenAIModule.IsPointInAreaWithLabel = function(p, l) return label and l == "interior" end
    System.IsPointIndoors = function() return false end
    local env = KCD2MP_W195Env({ x = 100, y = 100, z = 10 })
    check("B6 outdoors by every read: not indoors", env.indoor == false, env.indoorSrc)
    label = true
    env = KCD2MP_W195Env({ x = 100, y = 100, z = 10 })
    check("B6 the game's own 'interior' area says indoors (System.IsPointIndoors reads false beside a bed)", env.indoor == true and env.indoorSrc == "area", env.indoorSrc)
    label = false
    Physics.RayWorldIntersection = function(o, d) if d.z > 0 then return { { dist = 3.0 } } end return nil end
    env = KCD2MP_W195Env({ x = 100, y = 100, z = 10 })
    check("B6 the engine answered 'not interior': an eave above him is no ceiling", env.indoor == false, env.indoorSrc)
    XGenAIModule.IsPointInAreaWithLabel = function() return nil end   -- the engine gives no answer
    env = KCD2MP_W195Env({ x = 100, y = 100, z = 10 })
    check("B6 a roof within 6 m above him is indoors only when the engine gives no answer", env.indoor == true and env.indoorSrc == "roof", env.indoorSrc)
    Physics.RayWorldIntersection, System.IsPointIndoors = origRay, origIndoors
    XGenAIModule.IsPointInAreaWithLabel = origArea
    noErrs("B6")
end

do -- 2c the engine's own wake: a counted sleeper makes the game's movement sound at his spot (rate limited, its own switch)
    SOUNDS = {}
    XGenAIModule.ProduceSound = function(kind, pos, mult) SOUNDS[#SOUNDS + 1] = { kind = kind, x = pos.x, y = pos.y, z = pos.z, m = mult }; return true end
    local _, npc = scene(0.8)
    npc.sleeping = true
    KCD2MP.w195.wakeAt = nil
    local n, mark = judge(240, "trespass", "c=0,l=0,v=-99.00,n=0.43,s=-99.00,k=-1")
    check("B7 the rule woke a sleeper: the engine's sound (kind 3) is made at the figure's spot", n == 1 and #SOUNDS == 1 and SOUNDS[1].kind == 3 and SOUNDS[1].x == 100, #SOUNDS)
    local l = nil
    for i = mark + 1, #LOG do if LOG[i]:find("WO195-WAKE", 1, true) then l = LOG[i] end end
    check("B7 ...and logged", l ~= nil and l:find("sound=3", 1, true) ~= nil, l)
    _, npc = scene(0.8)
    npc.sleeping = true
    judge(241, "trespass", "c=0,l=0,v=-99.00,n=0.43,s=-99.00,k=-1")
    check("B7 a second wake inside the cooldown makes no second sound", #SOUNDS == 1, #SOUNDS)
    NOW = NOW + 10
    KCD2MP_W195SetWakeEngine("off")
    _, npc = scene(0.8); NOW = NOW + 10
    npc.sleeping = true
    judge(242, "trespass", "c=0,l=0,v=-99.00,n=0.43,s=-99.00,k=-1")
    check("B7 mp_wake_engine off: counted, but no sound", #SOUNDS == 1, #SOUNDS)
    KCD2MP_W195SetWakeEngine("on")
    _, npc = scene(3)
    npc.sleeping = true
    judge(243, "trespass", "c=0,l=0,v=-99.00,n=0.43,s=-99.00,k=-1")
    check("B7 a sleeper the rule does not wake: no sound", #SOUNDS == 1, #SOUNDS)
    XGenAIModule.ProduceSound = nil
    noErrs("B7")
end

do -- 2b a takedown carries no stealth field: the host judges it with what it sees of his figure (stance, pace); a field wins
    scene(10, { t = MIDNIGHT })
    KCD2MP.ghosts["1"].istate = { stance = "c", smoothedSpeed = 0 }
    local n, mark = judge(250, "knockout", "")
    check("B8 no field, his figure crouches at night, 10 m: the dark and the crouch apply (not seen)", n == 0 and (wline(mark, "tzel_man_3") or ""):find("crouch=1", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    scene(10)
    KCD2MP.ghosts["1"].istate = { stance = "s", smoothedSpeed = 0 }
    n = judge(251, "knockout", "")
    check("B8 ...standing by day: seen", n == 1, n)
    scene(0.9, { away = true })
    KCD2MP.ghosts["1"].istate = { stance = "s", smoothedSpeed = KCD2MP.w195.P.speedWalk }
    n, mark = judge(252, "knockout", "")
    check("B8 ...walking up behind it: heard (the figure's pace is his noise)", n == 1 and (wline(mark, "tzel_man_3") or ""):find("counted=heard", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    local ctx = KCD2MP_W195AvatarCtx(99)
    check("B8 no figure: no ctx", ctx == nil)
    noErrs("B8")
end

do -- 2d an item out of a LIVING NPC is a pickpocket judged by the rule; out of a body it is not
    scene(1.0, { away = true, t = MIDNIGHT })
    KCD2MP.ghosts["1"].istate = { stance = "c", smoothedSpeed = 0 }
    local victim = ENTS.tzel_man_3
    local mark = #LOG
    pcall(KCD2MP_W195PickJudge, 1, "tzel_man_3", victim)
    local judged = false
    for i = mark + 1, #LOG do if LOG[i]:find("WO139-JUDGE", 1, true) and LOG[i]:find("theft", 1, true) then judged = true end end
    check("B9 a pickpocket from behind, crouching, at night: judged, nobody saw it (no crime)", judged and e1("w139_judged", mark):find(" theft 0 ", 1, true) ~= nil, e1("w139_judged", mark))
    scene(1.0, { t = NOON })
    KCD2MP.ghosts["1"].istate = { stance = "s", smoothedSpeed = 0 }
    mark = #LOG
    KCD2MP_W195PickJudge(1, "tzel_man_3", ENTS.tzel_man_3)
    check("B9 the same face to face by day: the victim saw it (a crime)", e1("w139_judged", mark):find(" theft 1 ", 1, true) ~= nil, e1("w139_judged", mark))
    ENTS.tzel_man_3.dead = true
    mark = #LOG
    check("B9 a body is not a pickpocket", KCD2MP_W195PickJudge(1, "tzel_man_3", ENTS.tzel_man_3) == false and #emitted("w139_judged", mark) == 0)
    noErrs("B9")
end

-- ================================================================ WO-195B: the real rule against the engine's own verdicts
-- The rows are the live ones (the host's own Henry and the engine's own sound and its night guard, docs/WO-195B-findings.md); the table
-- is generated by tools/wo118/calib195b.py, which also writes docs/WO-195B-calibration.csv.
-- @@CALIB-BEGIN@@ (generated by tools/wo118/calib195b.py from the live rows; docs/WO-195B-calibration.csv has the full table)
CALIB = {
    { kind = "sight", id = "SF-3", crouch = false, d = 3.0, a = 0, los = 1, vir = 28.1, vib = 3.50, n = 0.00, engine = true },
    { kind = "sight", id = "SF-20", crouch = false, d = 20.0, a = 0, los = 1, vir = 21.4, vib = -0.75, n = 0.00, engine = false },
    { kind = "sight", id = "SF-16", crouch = false, d = 16.0, a = 0, los = 1, vir = 21.4, vib = -0.76, n = 0.00, engine = false },
    { kind = "sight", id = "SF-12", crouch = false, d = 12.0, a = 0, los = 1, vir = 21.4, vib = -0.78, n = 0.00, engine = false },
    { kind = "sight", id = "SF-9", crouch = false, d = 9.0, a = 0, los = 1, vir = 28.1, vib = -0.79, n = 0.00, engine = true },
    { kind = "sight", id = "SB-10", crouch = false, d = 10.0, a = 180, los = 0, vir = 21.4, vib = -0.84, n = 0.00, engine = false },
    { kind = "sight", id = "SB-7", crouch = false, d = 7.0, a = 180, los = 0, vir = 21.4, vib = 3.50, n = 0.00, engine = false },
    { kind = "sight", id = "SB-5", crouch = false, d = 5.0, a = 180, los = 0, vir = 21.4, vib = 3.50, n = 0.00, engine = false },
    { kind = "sight", id = "SB-3", crouch = false, d = 3.0, a = 180, los = 0, vir = 21.4, vib = 3.50, n = 0.00, engine = false },
    { kind = "sight", id = "SB-2", crouch = false, d = 2.0, a = 180, los = 0, vir = 28.1, vib = 3.50, n = 0.00, engine = false },
    { kind = "sight", id = "SB-1.2", crouch = false, d = 1.2, a = 3, los = 1, vir = 28.1, vib = 3.50, n = 0.00, engine = false },
    { kind = "wake", id = "W1.0-1.1", crouch = false, d = 1.1, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 1.00, engine = true },
    { kind = "wake", id = "W1.0-1.1", crouch = false, d = 1.1, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 1.00, engine = true },
    { kind = "wake", id = "W1.0-1.5", crouch = false, d = 1.5, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-5.4", crouch = false, d = 5.4, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-1.5", crouch = false, d = 1.5, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 1.00, engine = true },
    { kind = "wake", id = "W1.0-2.7", crouch = false, d = 2.7, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-3.6", crouch = false, d = 3.6, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-2.1", crouch = false, d = 2.1, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-3.7", crouch = false, d = 3.7, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-1.6", crouch = false, d = 1.6, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-4.6", crouch = false, d = 4.6, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-3.1", crouch = false, d = 3.1, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-2.6", crouch = false, d = 2.6, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-4.6", crouch = false, d = 4.6, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-4.0", crouch = false, d = 4.0, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-5.2", crouch = false, d = 5.2, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-4.0", crouch = false, d = 4.0, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-2.3", crouch = false, d = 2.3, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-5.8", crouch = false, d = 5.8, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-6.0", crouch = false, d = 6.0, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W1.0-5.8", crouch = false, d = 5.8, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 1.00, engine = false },
    { kind = "wake", id = "W0.43-0.6", crouch = false, d = 0.6, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = true },
    { kind = "wake", id = "W0.43-2.4", crouch = false, d = 2.4, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-1.6", crouch = false, d = 1.6, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-0.8", crouch = false, d = 0.8, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = true },
    { kind = "wake", id = "W0.43-3.2", crouch = false, d = 3.2, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-0.9", crouch = false, d = 0.9, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-1.0", crouch = false, d = 1.0, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = true },
    { kind = "wake", id = "W0.43-4.3", crouch = false, d = 4.3, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-5.3", crouch = false, d = 5.3, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-1.1", crouch = false, d = 1.1, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-3.8", crouch = false, d = 3.8, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-4.8", crouch = false, d = 4.8, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-1.4", crouch = false, d = 1.4, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-5.2", crouch = false, d = 5.2, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-4.6", crouch = false, d = 4.6, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-1.6", crouch = false, d = 1.6, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-5.5", crouch = false, d = 5.5, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-4.8", crouch = false, d = 4.8, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-1.9", crouch = false, d = 1.9, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-5.4", crouch = false, d = 5.4, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-1.8", crouch = false, d = 1.8, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-4.7", crouch = false, d = 4.7, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-5.3", crouch = false, d = 5.3, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-3.6", crouch = false, d = 3.6, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-2.3", crouch = false, d = 2.3, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-2.6", crouch = false, d = 2.6, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-4.4", crouch = false, d = 4.4, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-3.9", crouch = false, d = 3.9, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-5.0", crouch = false, d = 5.0, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-5.8", crouch = false, d = 5.8, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-3.1", crouch = false, d = 3.1, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-4.9", crouch = false, d = 4.9, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-4.0", crouch = false, d = 4.0, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-4.1", crouch = false, d = 4.1, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-3.6", crouch = false, d = 3.6, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-4.0", crouch = false, d = 4.0, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-5.1", crouch = false, d = 5.1, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "W0.43-5.9", crouch = false, d = 5.9, a = nil, los = 0, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "Hn_28-1.1", crouch = false, d = 1.1, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = true },
    { kind = "wake", id = "Hn_28-3.0", crouch = false, d = 3.0, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "Hn_28-2.5", crouch = false, d = 2.5, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "Hn_32-0.7", crouch = false, d = 0.7, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = true },
    { kind = "wake", id = "Hn_32-3.0", crouch = false, d = 3.0, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "Hn_32-2.7", crouch = false, d = 2.7, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "Hn_32-1.1", crouch = false, d = 1.1, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "Hn_11-0.7", crouch = false, d = 0.7, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = true },
    { kind = "wake", id = "Hn_11-3.0", crouch = false, d = 3.0, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
    { kind = "wake", id = "Hn_11-2.3", crouch = false, d = 2.3, a = nil, los = 1, vir = 0.0, vib = -0.95, n = 0.43, engine = false },
}
-- @@CALIB-END@@

do
    local hit = { sight = 0, wake = 0 }
    local tot = { sight = 0, wake = 0 }
    local misses = {}
    for _, r in ipairs(CALIB) do
        local v
        if r.kind == "wake" then
            local ctx = KCD2MP_W195ParseStealth(string.format("c=0,l=0,v=-0.95,n=%.2f,s=0.60,k=7", r.n))
            v = KCD2MP_W195Verdict({ d = r.d, asleep = true, ang = nil, los = true, vir = nil, fov = 100, ctx = ctx, env = { indoor = true, night = true } })
        else
            local ctx = KCD2MP_W195ParseStealth(string.format("c=%d,l=0,v=%.2f,n=0.00,s=0.60,k=7", r.crouch and 1 or 0, r.vib))
            v = KCD2MP_W195Verdict({ d = r.d, asleep = false, ang = r.a, los = r.los ~= 0, vir = r.vir, fov = 100, ctx = ctx, env = { indoor = false, night = true } })
        end
        local yes = (v == "seen" or v == "heard" or v == "woken")
        tot[r.kind] = tot[r.kind] + 1
        if yes == r.engine then hit[r.kind] = hit[r.kind] + 1 else misses[#misses + 1] = r.id end
    end
    local all, n = hit.sight + hit.wake, tot.sight + tot.wake
    check("B10 the calibration rows are there (the live table)", n >= 60 and tot.sight >= 10 and tot.wake >= 40, string.format("sight=%d wake=%d", tot.sight, tot.wake))
    check("B10 the rule agrees with the engine on at least 90 % of all cases", n > 0 and all / n >= 0.90, string.format("%d/%d  misses: %s", all, n, table.concat(misses, " ")))
    check("B10 ...and on at least 90 % of the sight cases", tot.sight > 0 and hit.sight / tot.sight >= 0.90, string.format("%d/%d", hit.sight, tot.sight))
    check("B10 ...and on at least 90 % of the sleepers' wake cases", tot.wake > 0 and hit.wake / tot.wake >= 0.90, string.format("%d/%d", hit.wake, tot.wake))
    noErrs("B10")
end

-- ================================================================ WO-195 1: the joiner's combat state after a death; a far kill

do
    reset(); clearLog(); NOW = 5000
    PLAYER_COMBAT = true
    player.soul.IsInCombatMode = function() return PLAYER_COMBAT end
    KCD2MP_W195PlayerCheck("tzel_man_13", 0)
    check("D1 still in combat with no copy engaged: said plainly", logCount("WO195-PLAYER after npc=tzel_man_13 combat_mode=true") == 1 and logCount("STILL in combat with no copy engaged") == 1)
    PLAYER_COMBAT = false
    KCD2MP_W195PlayerCheck("tzel_man_13", 0)
    check("D1 combat ended: the game's own state", logCount("combat_mode=false combat_danger=false other_engaged=0 -- the game's own state") == 1)
    player.soul.IsInCombatMode = nil
    noErrs("D1")
end

do -- a copy killed far away and bound later: the host's death wins on its very first packet (WO-122's owner death)
    reset(); clearLog(); NOW = 6000
    KCD2MP.w122.ownerDeath = true; KCD2MP.npcDeathSync = true; KCD2MP.w122.ownerReq = {}
    local mark = #LOG
    local sent = KCD2MP_OwnerDeathCheck("tzel_far_1", true, false, 300, 300, 10, 0, "puppet")
    check("D2 a copy alive here, dead on the host's first packet: the owner's death is asked at once", sent == true and e1("npc_owner_dead", mark):find("^tzel_far_1 300.00 300.00 10.00") ~= nil, e1("npc_owner_dead", mark))
    noErrs("D2")
end

do -- the switches and the commands
    reset(); clearLog()
    check("C1 the four commands are registered", CCMDS["mp_dead_release"] ~= nil and CCMDS["mp_stealth_witness"] ~= nil and CCMDS["mp_body_loot_host"] ~= nil and CCMDS["mp_w195_status"] ~= nil)
    check("C1 all ship on", KCD2MP.w195.deadRelease and KCD2MP.w195.stealthWitness and KCD2MP.w195.bodyLootHost)
    local mark = #LOG
    KCD2MP_W195SetDeadRelease("off")
    check("C1 mp_dead_release off reaches the agent", e1("w195_cfg", mark) == "dead=off stealth=on bodyloot=on", e1("w195_cfg", mark))
    KCD2MP_W195SetDeadRelease("on")
    check("C1 a bad word is refused", KCD2MP_W195SetBodyLoot("maybe") == false and KCD2MP.w195.bodyLootHost == true)
    KCD2MP_W195Status()
    check("C1 the status line", logCount("WO195-STATUS dead_release=on stealth_witness=on body_loot_host=on") == 1)
    noErrs("C1")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
