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
        l ~= nil and l:find("dist=10.0 asleep=0 lit=0 dark=day crouch=0 sight_m=25.0", 1, true) ~= nil and l:find("counted=seen", 1, true) ~= nil, l)
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

do -- 2a: a sleeper woken by noise (standing, 1.5 m: inside the 4 m footstep radius)
    local _, npc = scene(1.5)
    npc.sleeping = true
    local n, mark = judge(205, "trespass", STAND)
    check("W4 a sleeper within a standing figure's noise: woken, a witness", n == 1 and (wline(mark, "tzel_man_3") or ""):find("counted=woken", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    noErrs("W4")
end

do -- 2c crouch: 15 m by day is seen standing, not crouching (25 x 0.5 = 12.5 m)
    scene(15)
    check("W5 standing at 15 m by day: seen", judge(206, "trespass", STAND) == 1)
    scene(15)
    local n, mark = judge(207, "trespass", "c=1,l=0,v=-99.00,n=-99.00,s=-99.00,k=-1")
    check("W5 crouching at 15 m by day: out of the crouch's 12.5 m", n == 0 and (wline(mark, "tzel_man_3") or ""):find("sight_m=12.5", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    noErrs("W5")
end

do -- 2c night outdoors: 10 m is out of the night's 7.5 m (25 x 0.3); a torch puts the day back
    scene(10, { t = MIDNIGHT })
    local n, mark = judge(208, "trespass", STAND)
    check("W6 standing at 10 m at midnight outdoors: not seen (night 7.5 m)", n == 0 and (wline(mark, "tzel_man_3") or ""):find("dark=night", 1, true) ~= nil, wline(mark, "tzel_man_3"))
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

do -- 2c interiors: by day 25 x 0.7 = 17.5 m; at night as dark as outside
    scene(19, { indoor = true })
    local n, mark = judge(211, "trespass", STAND)
    check("W7 indoors by day at 19 m: out of the interior's 17.5 m", n == 0 and (wline(mark, "tzel_man_3") or ""):find("dark=interior ", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    scene(17, { indoor = true })
    check("W7 ...at 17 m: seen", judge(212, "trespass", STAND) == 1)
    scene(5, { indoor = true, t = MIDNIGHT })
    local n2, mark2 = judge(213, "trespass", "c=1,l=0,v=-99.00,n=-99.00,s=-99.00,k=-1")
    check("W7 a dark room at night, crouching, 5 m: not seen (the floor of the scale, 3 m)", n2 == 0 and (wline(mark2, "tzel_man_3") or ""):find("dark=interior-night", 1, true) ~= nil, wline(mark2, "tzel_man_3"))
    noErrs("W7")
end

do -- 2c stealth skill: the full skill takes 30 % off (25 x 0.7 = 17.5 m)
    scene(20)
    check("W8 no skill at 20 m: seen", judge(214, "trespass", STAND) == 1)
    scene(20)
    local n, mark = judge(215, "trespass", "c=0,l=0,v=-99.00,n=-99.00,s=-99.00,k=30")
    check("W8 stealth 30 at 20 m: out of 17.5 m", n == 0 and (wline(mark, "tzel_man_3") or ""):find("sight_m=17.5", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    noErrs("W8")
end

do -- 2c visibility (the game's own derived stat): above the reference shows him farther, below hides him
    local P = KCD2MP.w195.P
    scene(20, { t = MIDNIGHT })
    local hi = string.format("c=0,l=0,v=%.2f,n=-99.00,s=-99.00,k=-1", P.visRef + 2.0)   -- +1.0 on the factor: night 0.3 -> 1.3 (the cap)
    check("W9 a high visibility (bright clothes) at night at 20 m: seen", judge(216, "trespass", hi) == 1)
    scene(5)
    local lo = string.format("c=0,l=0,v=%.2f,n=-99.00,s=-99.00,k=-1", P.visRef - 4.0)   -- -2.0: the floor
    local n, mark = judge(217, "trespass", lo)
    check("W9 a very low visibility at 5 m by day: the floor (3 m), not seen", n == 0, wline(mark, "tzel_man_3"))
    noErrs("W9")
end

do -- 2c noise: an awake NPC looking away hears a standing figure within 4 m, not a crouching one
    scene(3, { away = true })
    local n, mark = judge(218, "trespass", STAND)
    check("W10 looking away, 3 m, standing: heard", n == 1 and (wline(mark, "tzel_man_3") or ""):find("counted=heard", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    scene(3, { away = true })
    n, mark = judge(219, "trespass", "c=1,l=0,v=-99.00,n=-99.00,s=-99.00,k=-1")
    check("W10 the same crouching: unseen, unheard", n == 0 and (wline(mark, "tzel_man_3") or ""):find("ignored=unseen", 1, true) ~= nil, wline(mark, "tzel_man_3"))
    scene(5, { away = true })
    local loud = "c=0,l=0,v=-1.00,n=1.50,s=-1.00,k=-1"   -- loud armour: 4 x 1.5 = 6 m
    check("W10 loud armour (noise 1.5) at 5 m: heard", judge(220, "trespass", loud) == 1)
    noErrs("W10")
end

do -- the acceptance pair: a dark room at night, crouching beside a sleeper in bed -- then standing, lit, by day
    local _, npc = scene(1.0, { indoor = true, t = 23 * 3600 })
    KCD2MP_W195Asleep("tzel_man_3", 1)
    local n, mark = judge(221, "trespass", "c=1,l=0,v=1.00,n=1.00,s=1.00,k=7")
    check("W11 crouching in a dark room beside a sleeper in bed: not witnessed", n == 0, wline(mark, "tzel_man_3"))
    KCD2MP_W195Asleep("tzel_man_3", 0)
    _, npc = scene(1.0)
    n = judge(222, "trespass", "c=0,l=1,v=1.00,n=1.00,s=1.00,k=7")
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
