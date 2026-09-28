-- WO-139 synthetic test: crime and guards, the Lua half, against the real kdcmp.lua
-- under MoonSharp (engine stubbed, fake clock; items and inventories as in
-- Test-WO134Synthetic.lua).
--   R  no robbing each other: pickpocket / loot on the other player's avatar refused
--      with the plain line, host and joiner, in any session; a townsperson is not
--   D  detection (joiner): a stolen loose item (the direct pickup and the host-asked
--      one), a stash taken from, a lock picked (owned / not), a horse stolen (and a
--      legal one not), a body robbed; nothing outside a joiner session
--   S  the stop (joiner): the guard's copy freed from the host's stream and its brain
--      back; the crimes planted with the game's own stimuli; the dialogue's own result;
--      the end (suspended again, the stream back later, the outcome with the paid fine);
--      refusals (far, busy, no guard); a fight; the player dies
--   H  the host: witnesses (the sight test), guards, public enemies, avatars never
--      witnesses, nobody sees = no crime; violent crimes (self-defence, bandits); the
--      guard scan; the legal horses; the held guard placed
--   L  legal horses on the joiner (the prompt lever, the crime-side event, restored)
--   T  no time skip for punishment: the skip-time data set to one second and restored
--   B  the backstop, the switches and the commands
-- What this proves: the Lua half. Live evidence: docs/WO-139-findings.md.

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

-- ================================================================ R: no robbing each other

do
    reset(); clearLog(); NOW = 10
    local av = avatar(1, 102, 100)
    local folk = mkNpc("tzel_man_3", 104, 100, 10, ZEL)
    KCD2MP_W131Tick(false, true)          -- the host: the wraps install on both roles
    BasicAIActions.OnPickpocketing(av, player)
    check("R: host: pickpocketing the partner's avatar is refused", #TALKS == 0 and logCount("WO139-ROB refused kind=pickpocket avatar=kcd2mp_1") == 1)
    check("R: ...with the plain line", TOASTS[#TOASTS] == "You can't steal from each other in co-op.", TOASTS[#TOASTS])
    BasicAIActions.OnLoot(av, player)
    check("R: host: looting the partner's avatar is refused", #TALKS == 0 and logCount("WO139-ROB refused kind=loot") == 1)
    BasicAIActions.OnPickpocketing(folk, player)
    check("R: host: a townsperson is not the partner: the game's own pickpocketing runs", TALKS[#TALKS] == "OnPickpocketing:tzel_man_3")
    reset(); clearLog(); NOW = 20
    local av2 = avatar(0, 102, 100)
    KCD2MP_W131Tick(true, true)           -- the joiner (the copy guard's own rules unchanged)
    BasicAIActions.OnPickpocketing(av2, player)
    BasicAIActions.OnLoot(av2, player)
    check("R: joiner: both refused on the host's avatar too", #TALKS == 0 and logCount("WO139-ROB refused") == 2)
    check("R: a name alone (an avatar not yet registered) is still an avatar", KCD2MP_W139IsAvatar({ GetName = function() return "kcd2mp_7" end }))
    check("R: an NPC named like one is not", not KCD2MP_W139IsAvatar({ GetName = function() return "kcd2mp_horse_1" end }))
    noErrs("R")
end

-- ================================================================ D: detection on the joiner

local function fireTimers()   -- every timer due, and the ones they set, in order
    for _ = 1, 20 do
        if #TIMERS == 0 then return end
        local t = TIMERS; TIMERS = {}
        for _, x in ipairs(t) do x.f() end
    end
end

do -- a stolen loose item: the direct pickup (the WO-134 ask not in play)
    reset(); clearLog(); NOW = 100
    joiner()
    KCD2MP_W134Tick(false, false, true, 1)   -- WO-134 not active here: the wrapped pickup runs the game's own at once
    local axe = mkPickable("axeWork02000225", "81494400-b654-4aa7-8f31-c95c689db5f6", 101, 100, 10, newItem("81494400-b654-4aa7-8f31-c95c689db5f6"))
    STEAL["axeWork02000225"] = true
    local mark = #LOG
    PickableItem.OnUsedHold(axe, player)
    check("D: nothing reported while the pick runs", #emitted("w139_crime", mark) == 0)
    fireTimers()
    local ev = emitted("w139_crime", mark)
    check("D: a stolen item: reported once, after the take", #ev == 1 and ev[1]:find("^theft 101.00 100.00 10.00 %- 81494400%-b654%-4aa7%-8f31%-c95c689db5f6 world"), ev[1])
    check("D: ...and taken (the game's own OnSteal ran)", ENTS["axeWork02000225"] == nil and player.inventory:GetCountOfClass("81494400-b654-4aa7-8f31-c95c689db5f6") == 1)
    check("D: ...recorded here for a stop", #KCD2MP.w139.crimes == 1 and KCD2MP.w139.crimes[1].cls == "81494400-b654-4aa7-8f31-c95c689db5f6")
    local legal = mkPickable("apple000001", "aaaaaaaa-0000-0000-0000-000000000001", 101, 101, 10, newItem("aaaaaaaa-0000-0000-0000-000000000001"))
    mark = #LOG
    PickableItem.OnUsed(legal, player)
    fireTimers()
    check("D: an item free to take: no report", #emitted("w139_crime", mark) == 0 and ENTS["apple000001"] == nil)
    -- the pick refused (the animation did not start, as with a console pick): no theft
    local stuck = mkPickable("axeWork02000227", "81494400-b654-4aa7-8f31-c95c689db5f6", 101, 102, 10, newItem("81494400-b654-4aa7-8f31-c95c689db5f6"))
    STEAL["axeWork02000227"] = true
    stuck.item.OnSteal = function() ENTS["axeWork02000227"] = nil; return true end   -- observed: a failed pick takes it out of the world, into no inventory
    mark = #LOG
    PickableItem.OnUsedHold(stuck, player)
    fireTimers()
    check("D: a pick that never reached this Henry is no theft, even with the piece gone from the world (checked three times, then said)",
        #emitted("w139_crime", mark) == 0 and logCount("WO139-CRIME theft not reported") == 1)
    noErrs("D1")
end

do -- the WO-134 ask path: the host answers ok, then the pickup (and the theft) happen
    reset(); clearLog(); NOW = 200
    joiner()
    KCD2MP.w131.joiner = true; KCD2MP.w131.shared = true; KCD2MP.w131.aliveAt = NOW
    KCD2MP_W134Tick(true, false, true, 1)
    local cls = "81494400-b654-4aa7-8f31-c95c689db5f6"
    local axe = mkPickable("axeWork02000226", cls, 101, 100, 10, newItem(cls))
    STEAL["axeWork02000226"] = true
    local mark = #LOG
    PickableItem.OnUsedHold(axe, player)
    check("D: the ask path: nothing reported before the host answers", #emitted("w139_crime", mark) == 0 and #emitted("w134_item", mark) == 1)
    local tok = e1("w134_item", mark):match("^(%S+)")
    KCD2MP_W134ItemResult(tok, "ok")
    fireTimers()
    local ev = emitted("w139_crime", mark)
    check("D: the ask path: reported once the host's ok ran the pickup", #ev == 1 and ev[1]:find("^theft ") ~= nil, ev[1])
    noErrs("D2")
end

do -- a stash the game offers as theft; an owned one taken from while open
    reset(); clearLog(); NOW = 300
    joiner()
    NEXTID = NEXTID + 1
    local st = { class = "Stash", id = NEXTID, bOpened = 0, bLocked = false, crimeStash = true, inventory = mkInventory("chest46") }
    st.GetName = function() return "stash_chest46" end
    st.GetWorldPos = function() return { x = 101, y = 99, z = 10 } end
    setmetatable(st, { __index = Stash })
    ENTS["stash_chest46"] = st
    local owner = mkNpc("tzel_olbram", 120, 100, 10, ZEL)
    OWNERS["chest46"] = owner.this.id
    st.inventory:CreateItem("cccccccc-0000-0000-0000-000000000001", 1, 1)
    st.inventory:CreateItem("cccccccc-0000-0000-0000-000000000002", 1, 1)
    KCD2MP_W139Install()
    Stash.OnUsed(st, player)
    check("D: a crime stash opened: watched (the owner resolved)", KCD2MP.w139.stash ~= nil and KCD2MP.w139.stash.owner == "tzel_olbram")
    local w1 = st.inventory.list[1]
    st.inventory:RemoveItem(w1); player.inventory.list[#player.inventory.list + 1] = w1
    local mark = #LOG
    KCD2MP_W139StashTick()
    local ev = emitted("w139_crime", mark)
    check("D: a piece taken from it: one theft, the owner the victim", #ev == 1 and ev[1]:find("^theft 101.00 99.00 10.00 tzel_olbram cccccccc%-0000%-0000%-0000%-000000000001 stash"), ev[1])
    Stash.OnUsed(st, player)   -- closed
    KCD2MP_W139StashTick(); KCD2MP_W139StashTick()
    check("D: closed: no longer watched", KCD2MP.w139.stash == nil)
    st.crimeStash = false
    Stash.OnUsed(st, player)
    check("D: a stash free to take: not watched", KCD2MP.w139.stash == nil)
    noErrs("D3")
end

do -- locks, horses, bodies
    reset(); clearLog(); NOW = 400
    joiner()
    KCD2MP_W139Install()
    NEXTID = NEXTID + 1
    local door = { class = "AnimDoor", id = NEXTID, animDoor = { IsEndPointInPrivateArea = function() return true end } }
    door.GetName = function() return "door_village_right4" end
    door.GetWorldPos = function() return { x = 99, y = 100, z = 10 } end
    ENTS["door_village_right4"] = door
    local mark = #LOG
    Minigame.StartLockPicking(door.id)
    local ev = emitted("w139_crime", mark)
    check("D: a private door picked: a lockpick report, and the game's own minigame still starts", #ev == 1 and ev[1]:find("^lockpick .* door$") ~= nil and LOCKPICKS == 1, ev[1])
    door.animDoor.IsEndPointInPrivateArea = function() return false end
    mark = #LOG
    Minigame.StartLockPicking(door.id)
    check("D: a public door picked: no crime", #emitted("w139_crime", mark) == 0 and LOCKPICKS == 2)

    local nag = mkNpc("tzel_horse_1", 102, 101, 10, ZEL, { class = "Horse" })
    setmetatable(nag, { __index = Horse })
    mark = #LOG
    Horse.OnMount(nag, player)
    ev = emitted("w139_crime", mark)
    check("D: a townsperson's horse mounted: horse theft (the horse names the settlement)", #ev == 1 and ev[1]:find("^horsetheft .* tzel_horse_1 %- horse$") ~= nil and MOUNTS[1] == "tzel_horse_1", ev[1])
    KCD2MP_W139LegalHorses("tzel_horse_1")
    mark = #LOG
    Horse.OnMount(nag, player)
    check("D: the same horse legal for the host's Henry: no crime", #emitted("w139_crime", mark) == 0)
    local own = mkNpc("my_horse", 102, 102, 10, ZEL, { class = "Horse" })
    setmetatable(own, { __index = Horse })
    PLAYER_HORSE = own.id
    mark = #LOG
    Horse.OnMount(own, player)
    check("D: this player's own horse: no crime", #emitted("w139_crime", mark) == 0)

    KCD2MP.w131.joiner = true; KCD2MP.w131.shared = true; KCD2MP.w131.aliveAt = NOW
    local body = mkNpc("tzel_man_6", 103, 100, 10, ZEL, { legalLoot = false })
    body.dead = true
    mark = #LOG
    KCD2MP_W131LootAllowed(body, "loot", nil)
    check("D: a host body not legal to loot: robbing it is reported", #emitted("w139_crime", mark) == 1)
    local bandit = mkNpc("bandit_1", 103, 101, 10, "bandits", { legalLoot = true })
    mark = #LOG
    KCD2MP_W131LootAllowed(bandit, "loot", nil)
    check("D: a body legal to loot (a bandit): no crime", #emitted("w139_crime", mark) == 0)

    KCD2MP_W139Session(true, false, true)   -- the host: its own crimes are its engine's
    mark = #LOG
    Minigame.StartLockPicking(door.id)
    door.animDoor.IsEndPointInPrivateArea = function() return true end
    Minigame.StartLockPicking(door.id)
    check("D: on the host nothing is reported (its crimes are its own game's)", #emitted("w139_crime", mark) == 0)
    KCD2MP_W139Session(false, true, false)
    mark = #LOG
    Minigame.StartLockPicking(door.id)
    check("D: crime sharing off (the host's mp_crime_shared): nothing reported", #emitted("w139_crime", mark) == 0)
    noErrs("D4")
end

-- ================================================================ S: the stop on the joiner

local function stopSetup()
    reset(); clearLog(); NOW = 1000
    joiner()
    KCD2MP_W139Install()
    local g = mkNpc("tzel_man_7", 104, 100, 10, GUARD_F, { ctx = GUARD_CTX })
    KCD2MP.npcPuppets["tzel_man_7"] = { nativeSent = true, owner = 0 }
    KCD2MP._npcPaused["tzel_man_7"] = NOW
    return g
end

do
    local g = stopSetup()
    local cls = "81494400-b654-4aa7-8f31-c95c689db5f6"
    player.inventory:CreateItem(cls, 1, 1)
    local victim = mkNpc("tzel_olbram", 120, 100, 10, ZEL)
    KCD2MP_W139Report("theft", 101, 100, 10, "tzel_olbram", cls, "world")
    KCD2MP_W139Report("trespass", 99, 100, 10, nil, nil, "area")
    KCD2MP_W139Report("lockpick", 99, 100, 10, nil, nil, "door")
    local mark = #LOG
    KCD2MP_W139Stop(77, "tzel_man_7", "theft:1,trespass:1,lockpick:1")
    check("S: the copy is freed from the host's stream (unbound, no longer a puppet)", KCD2MP.npcPuppets["tzel_man_7"] == nil and #emitted("npc_native", mark) == 1)
    check("S: ...its brain back (resumed), held here", cmdCount("wh_ai_ResumeNPC tzel_man_7") >= 1 and KCD2MP_W139StopHeld("tzel_man_7") == true)
    local kinds = {}
    for _, m in ipairs(MSGS) do kinds[#kinds + 1] = m.kind end
    check("S: three crimes planted, each with the game's own stimulus", table.concat(kinds, ",") == "switch:stimulus:theft,switch:stimulus:escalatedTrespass,switch:stimulus:disturbance", table.concat(kinds, ","))
    local th = MSGS[1].t
    check("S: the theft: method pick, the stolen piece on this Henry, the owner", th.method == 5 and ITEMS[th.pivot] and ITEMS[th.pivot].class == cls and th.information.perceivedWuid == th.pivot and th.owner == victim.this.id and th.immediate == true)
    check("S: the trespass: no area wuid, the trespass kind", MSGS[2].t.wuidType == 0 and MSGS[2].t.stimulusKind == 40)
    check("S: the lockpick: a disturbance with the crime table's lockpick fine", MSGS[3].t.priceOverride == 600 and MSGS[3].t.perceivedWuid == "pwuid")
    check("S: every stimulus to the guard itself", MSGS[1].to == g.this.id and MSGS[3].to == g.this.id)
    check("S: the start is logged and told", logCount("WO139-STOP start guard=tzel_man_7 id=77") == 1 and e1("w139_stop", mark) == "on tzel_man_7")
    -- the game's own flow: the dialogue, the fine paid, the dialogue's own result call
    PLAYER_IN_DIALOG = true
    NOW = NOW + 5; KCD2MP_W139StopTick()
    MONEY = 162.1
    Crime.SendResolveDialogResult({ STRAZNY_ZATYKANI = { this = { id = g.this.id } } }, enum_crime_resolutionKind.fine)
    check("S: the dialogue's own result call reaches the game unchanged", RESOLVED == 0)
    check("S: ...and is seen", KCD2MP.w139.stop ~= nil and KCD2MP.w139.stop.result == "paid")
    NOW = NOW + 1; KCD2MP_W139StopTick()
    check("S: still in the dialogue: the stop runs on", KCD2MP.w139.stop ~= nil)
    PLAYER_IN_DIALOG = false
    NOW = NOW + 3; mark = #LOG; KCD2MP_W139StopTick()
    local out = emitted("w139_outcome", mark)
    check("S: the end: the outcome with what left this Henry (500 decagroschen) and where the guard stands",
        #out == 1 and out[1] == "77 paid tzel_man_7 500 104.00 100.00 10.00", out[1])
    check("S: the copy suspended again", cmdCount("wh_ai_PauseNPC tzel_man_7") >= 1 and KCD2MP.w139.stop == nil)
    check("S: a resolution clears this machine's record of the crimes", #KCD2MP.w139.crimes == 0)
    check("S: the host's stream stays ignored a moment (the host places its guard first)", KCD2MP_W139StopHeld("tzel_man_7") == true)
    NOW = NOW + 3.5
    check("S: ...then the host's stream takes it back", KCD2MP_W139StopHeld("tzel_man_7") == false)
    noErrs("S1")
end

do -- the guard's own attack (a refused / ignored chat): the stop ends as a flight
    local g = stopSetup()
    KCD2MP_W139Stop(78, "tzel_man_7", "theft:1")
    check("S: no record here, no stolen piece on him: the host's list is planted as the fine only", #MSGS == 1 and MSGS[1].kind == "switch:stimulus:disturbance" and MSGS[1].t.priceOverride == 500)
    PLAYER_DANGER = true
    NOW = NOW + 4
    local mark = #LOG
    KCD2MP_W139StopTick()
    local out = emitted("w139_outcome", mark)
    check("S: the guard fights him: fled (resisting arrest)", #out == 1 and out[1]:find("^78 fled tzel_man_7 0") ~= nil, out[1])
    local sf = MSGS[#MSGS]
    check("S: ...its fight ends first: the game's own stopFight to the guard, the suspension after it",
        sf.kind == "stopFight" and sf.to == g.this.id and sf.t.soulCount == 1 and sf.t.messageId == "w139stop78"
        and cmdCount("wh_ai_PauseNPC tzel_man_7") == 0 and KCD2MP_W139StopHeld("tzel_man_7") == true)
    for _, t in ipairs(TIMERS) do if t.ms == 4500 then t.f() end end
    check("S: ...then suspended again (4.5 s later), the host's stream back a moment after",
        cmdCount("wh_ai_PauseNPC tzel_man_7") == 1 and KCD2MP_W139StopHeld("tzel_man_7") == true)
    NOW = NOW + 3.5
    check("S: ...and given back", KCD2MP_W139StopHeld("tzel_man_7") == false)
    noErrs("S2")
end

do -- the dialogue's own fight result
    local g = stopSetup()
    KCD2MP_W139Stop(79, "tzel_man_7", "trespass:1")
    PLAYER_IN_DIALOG = true; NOW = NOW + 5; KCD2MP_W139StopTick()
    KCD2MP_W139Resolved({}, enum_crime_resolutionKind.fight)
    PLAYER_IN_DIALOG = false; PLAYER_DANGER = true
    NOW = NOW + 1
    local mark = #LOG
    KCD2MP_W139StopTick()
    check("S: the dialogue's fight: fought", e1("w139_outcome", mark):find("^79 fought") ~= nil, e1("w139_outcome", mark))
    noErrs("S3")
end

do -- refusals and ends
    local g = stopSetup()
    g.px = 160
    local mark = #LOG
    KCD2MP_W139Stop(80, "tzel_man_7", "theft:1")
    check("S: a guard 60 m away: not now (nostop), nothing planted, the copy untouched",
        e1("w139_outcome", mark) == "80 nostop tzel_man_7 0" and #MSGS == 0 and KCD2MP.npcPuppets["tzel_man_7"] ~= nil)
    g.px = 104
    PLAYER_IN_DIALOG = true
    mark = #LOG
    KCD2MP_W139Stop(81, "tzel_man_7", "theft:1")
    check("S: in a conversation: not now", e1("w139_outcome", mark) == "81 nostop tzel_man_7 0")
    PLAYER_IN_DIALOG = false
    mark = #LOG
    KCD2MP_W139Stop(82, "nobody_here", "theft:1")
    check("S: no such guard here: not now", e1("w139_outcome", mark) == "82 nostop nobody_here 0")
    KCD2MP_W139Stop(83, "tzel_man_7", "trespass:1")
    mark = #LOG
    KCD2MP_W139Stop(84, "tzel_man_7", "trespass:1")
    check("S: one stop at a time", e1("w139_outcome", mark) == "84 nostop tzel_man_7 0" and KCD2MP.w139.stop.id == "83")
    PLAYER_DEAD = true
    mark = #LOG
    KCD2MP_W139StopTick()
    check("S: the player died in it: ended (fought)", e1("w139_outcome", mark):find("^83 fought") ~= nil)
    PLAYER_DEAD = false
    KCD2MP_W139Stop(85, "tzel_man_7", "trespass:1")
    NOW = NOW + KCD2MP.w139.stopMaxS + 1
    mark = #LOG
    KCD2MP_W139StopTick()
    check("S: a stop that never resolves ends (nostop)", e1("w139_outcome", mark):find("^85 nostop") ~= nil)
    joiner()   -- the session tick (the clock ran on past the agent's timeout above)
    KCD2MP_W139Stop(86, "tzel_man_7", "trespass:1")
    mark = #LOG
    KCD2MP_W139Executed()
    check("S: an execution ends it: executed, and this machine's record is clear", e1("w139_outcome", mark):find("^86 executed") ~= nil and #KCD2MP.w139.crimes == 0)
    noErrs("S4")
end

do -- the host's list decides what is planted; this machine's record only names the stolen piece
    local g = stopSetup()
    local cls = "81494400-b654-4aa7-8f31-c95c689db5f6"
    player.inventory:CreateItem(cls, 1, 1)
    KCD2MP_W139Report("theft", 101, 100, 10, nil, cls, "world")
    KCD2MP_W139Report("lockpick", 99, 100, 10, nil, nil, "door")     -- nobody saw it: not on the host's list
    KCD2MP_W139Stop(88, "tzel_man_7", "theft:1")
    check("S: only the host's crimes are planted (the unseen lockpick is not)", #MSGS == 1 and MSGS[1].kind == "switch:stimulus:theft")
    check("S: ...the theft still with the stolen piece on this Henry", ITEMS[MSGS[1].t.pivot] and ITEMS[MSGS[1].t.pivot].class == cls)
    KCD2MP_W139StopEnd("test", "nostop")
    local mark = #LOG
    KCD2MP_W139Cleared("paid")
    check("S: the host cleared the record: this machine's record goes too, and it is said",
        #KCD2MP.w139.crimes == 0 and logCount("WO139-CLEARED by the host (paid) -- my record here: 2 -> 0", mark) == 1)
    noErrs("S6")
end

do -- the fine choice without the money taken (the dialogue never went on): no fine was paid
    local g = stopSetup()
    KCD2MP_W139Stop(89, "tzel_man_7", "trespass:1")
    Crime.SendResolveDialogResult({ STRAZNY_ZATYKANI = { this = { id = g.this.id } } }, enum_crime_resolutionKind.fine)
    local mark = #LOG
    KCD2MP_W139StopEnd("max-time", nil)
    check("S: a fine result but nothing left this Henry: talked, the host's record stands",
        e1("w139_outcome", mark):find("^89 talked tzel_man_7 0") ~= nil and logCount("no money left this Henry: the record stands", mark) == 1,
        e1("w139_outcome", mark))
    noErrs("S7")
end

do -- violent crimes in a stop (no longer fresh: the host's guards attacked for them before)
    local g = stopSetup()
    KCD2MP_W139Stop(87, "tzel_man_7", "assault:1,murder:1,knockout:1")
    local prices = {}
    for _, m in ipairs(MSGS) do if m.kind == "switch:stimulus:disturbance" and m.t.perceivedWuid == "pwuid" then prices[#prices + 1] = m.t.priceOverride end end
    check("S: assault, murder and a knockout are planted as disturbances at the crime table's fines (murder over the branding threshold)",
        #prices == 3 and prices[1] == 1500 and prices[2] == 20001 and prices[3] == 1500, table.concat(prices, ","))
    check("S: ...the stop runs (something was planted)", KCD2MP.w139.stop ~= nil and KCD2MP.w139.stop.id == "87")
    noErrs("S5")
end

-- ================================================================ H: the host

do
    reset(); clearLog(); NOW = 2000
    host()
    local av = avatar(1, 100, 100)
    local guard = mkNpc("tzel_man_7", 110, 100, 10, GUARD_F, { ctx = GUARD_CTX, fwd = { x = -1, y = 0, z = 0 } })
    local folk = mkNpc("tzel_man_3", 100, 112, 10, ZEL, { fwd = { x = 0, y = -1, z = 0 } })
    local blind = mkNpc("tzel_eliska", 100, 88, 10, ZEL, { fwd = { x = 0, y = -1, z = 0 } })       -- looking away
    local walled = mkNpc("tzel_olbram", 90, 100, 10, ZEL, { fwd = { x = 1, y = 0, z = 0 } })
    local bandit = mkNpc("bandit_1", 100, 95, 10, "bandits", { enemy = true, fwd = { x = 0, y = 1, z = 0 } })
    local other = avatar(2, 102, 102)                                                              -- the other joiner's avatar
    RAYS["tzel_man_7"] = { hit = true, dist = 10, entity = av }
    RAYS["tzel_man_3"] = { hit = true, dist = 12, entity = av }
    RAYS["tzel_olbram"] = { hit = true, dist = 3, entity = nil }     -- a wall 3 m from its eyes
    local mark = #LOG
    KCD2MP_W139HostJudge(1, 12, "theft", 100, 100, 10, "tzel_olbram", "-", "world")
    local ev = emitted("w139_judged", mark)
    check("H: the witnesses: the guard and the villager who can see the avatar; not the one looking away, not the walled one, not the bandit, not an avatar",
        #ev == 1 and ev[1] == "1 12 theft 2 1 trosecko_settlements_zelejov tzel_man_7", ev[1])
    guard.sleeping = true
    folk.dead = true
    mark = #LOG
    KCD2MP_W139HostJudge(1, 13, "trespass", 100, 100, 10, "-", "-", "area")
    ev = emitted("w139_judged", mark)
    check("H: nobody awake and alive sees it: no crime (0 witnesses), the settlement from nobody", #ev == 1 and ev[1] == "1 13 trespass 0 0 - -", ev[1])
    guard.sleeping = false; folk.dead = false
    -- no guard saw it, no victim: the settlement of the townsfolk who saw it
    guard.sleeping = true
    mark = #LOG
    KCD2MP_W139HostJudge(1, 15, "trespass", 100, 100, 10, "-", "-", "area")
    ev = emitted("w139_judged", mark)
    check("H: only villagers saw a trespass: it is their settlement's (their report reaches its guards)",
        #ev == 1 and ev[1] == "1 15 trespass 1 0 trosecko_settlements_zelejov -", ev[1])
    guard.sleeping = false
    -- a report far from the avatar (a lagging stream): judged at the spot
    mark = #LOG
    KCD2MP_W139HostJudge(1, 14, "lockpick", 130, 100, 10, "-", "-", "door")
    check("H: a spot away from the avatar is judged at the spot", logCount("by=the spot", mark) == 1)
    -- violent crimes the host sees itself
    local victim = mkNpc("tzel_man_9", 101, 101, 10, ZEL)
    mark = #LOG
    KCD2MP_W139HostViolent(1, "tzel_man_9", "assault")
    check("H: the avatar's hit on a townsperson: judged as the joiner's assault", #emitted("w139_judged", mark) == 1 and e1("w139_judged", mark):find("^1 0 assault") ~= nil)
    mark = #LOG
    KCD2MP_W139HostViolent(1, "bandit_1", "assault")
    check("H: a bandit (a public enemy): no crime", #emitted("w139_judged", mark) == 0 and logCount("not a crime (a public enemy)", mark) == 1)
    victim.combat = true
    mark = #LOG
    KCD2MP_W139HostViolent(1, "tzel_man_9", "assault")
    check("H: someone already fighting him: self-defence, no crime", #emitted("w139_judged", mark) == 0)
    mark = #LOG
    KCD2MP_W139HostViolent(1, "tzel_man_9", "murder")
    check("H: ...but killing him is still murder", #emitted("w139_judged", mark) == 1)
    mark = #LOG
    KCD2MP_W139HostViolent(1, "kcd2mp_2", "assault")
    check("H: nothing between the players is a crime: a hit on an avatar is never judged", #emitted("w139_judged", mark) == 0)
    -- the guard scan
    mark = #LOG
    KCD2MP_W139HostGuards(1)
    ev = emitted("w139_guards", mark)
    check("H: the guard scan: name, settlement, distance, sees, on duty", #ev == 1 and ev[1] == "1 tzel_man_7:trosecko_settlements_zelejov:10:1:1", ev[1])
    mark = #LOG
    KCD2MP_W139HostGuards(9)
    check("H: no such avatar: an empty scan", e1("w139_guards", mark) == "9 -")
    -- a pursuit's fight: the game's own attack interrupt, aimed at the joiner's avatar
    MSGS = {}
    check("H: a pursuit starts the guard's own attack at the avatar (crime:attackInitiatedByConcept, a priority target)",
        KCD2MP_W139HostAttack("tzel_man_7", 1) == true and #MSGS == 1 and MSGS[1].to == guard.this.id
        and MSGS[1].kind == "crime:attackInitiatedByConcept" and MSGS[1].t.target == av.this.id and MSGS[1].t.priorityTarget == true)
    check("H: ...no such avatar, or no such guard: nothing sent",
        KCD2MP_W139HostAttack("tzel_man_7", 9) == false and KCD2MP_W139HostAttack("nobody_here", 1) == false and #MSGS == 1)
    -- the legal horses
    local own = mkNpc("host_horse", 105, 105, 10, ZEL, { class = "Horse" }); setmetatable(own, { __index = Horse })
    local lent = mkNpc("lent_horse", 106, 105, 10, ZEL, { class = "Horse" }); setmetatable(lent, { __index = Horse }); lent.mountIsLegal = true
    local nag = mkNpc("tzel_horse_1", 107, 105, 10, ZEL, { class = "Horse" }); setmetatable(nag, { __index = Horse })
    PLAYER_HORSE = own.id
    mark = #LOG
    KCD2MP_W139HostHorses()
    ev = emitted("w139_horses", mark)
    check("H: the horses the host's Henry may ride: his own and a legal one, not a townsperson's",
        #ev == 1 and ev[1]:find("host_horse", 1, true) and ev[1]:find("lent_horse", 1, true) and not ev[1]:find("tzel_horse_1", 1, true), ev[1])
    -- the held guard placed where the joiner's stop ended
    check("H: the guard placed at the stop's end", KCD2MP_W139HostPlace("tzel_man_7", 104.5, 100.5, 10) == true and guard.px == 104.5)
    check("H: ...never from a stale position 50 m off", KCD2MP_W139HostPlace("tzel_man_7", 150, 100, 10) == false and guard.px == 104.5)
    noErrs("H")
end

-- ================================================================ L: legal horses on the joiner

do
    reset(); clearLog(); NOW = 3000
    joiner()
    local h = mkNpc("host_horse", 105, 105, 10, ZEL, { class = "Horse" }); setmetatable(h, { __index = Horse })
    local mark = #LOG
    KCD2MP_W139LegalHorses("host_horse,not_here")
    check("L: the host's horse: legal here (the mount prompt says Mount), the crime side asked of the agent",
        h.mountIsLegal == true and e1("w139_horse", mark) == "host_horse 1" and #emitted("w139_horse", mark) == 1)
    KCD2MP_W139LegalHorses("host_horse")
    check("L: the same list again: nothing new", #emitted("w139_horse", mark) == 1)
    KCD2MP_W139LegalHorses("")
    check("L: off the list: its own value back, and the crime side cleared", h.mountIsLegal == false and emitted("w139_horse", mark)[2] == "host_horse 0")
    noErrs("L")
end

-- ================================================================ T: no time skip for punishment

do
    reset(); clearLog(); NOW = 4000
    NEXTID = NEXTID + 1
    local short = { class = "SkipTimeCutsceneData", id = NEXTID, Properties = { esSkipTimeType = "crime_punishmentSetup", Duration = "2h0m0s", TargetTime = "" } }
    short.GetName = function() return "crime_punishment_skipTime_short_data" end
    NEXTID = NEXTID + 1
    local long = { class = "SkipTimeCutsceneData", id = NEXTID, Properties = { esSkipTimeType = "crime_punishmentSetup", Duration = "", TargetTime = "10h0m0s" } }
    long.GetName = function() return "crime_punishment_skipTime_long_data" end
    NEXTID = NEXTID + 1
    local bath = { class = "SkipTimeCutsceneData", id = NEXTID, Properties = { esSkipTimeType = "Bath", Duration = "1h", TargetTime = "" } }
    bath.GetName = function() return "bathhouse_SkipTimeCutsceneData_1h" end
    ENTS[short:GetName()] = short; ENTS[long:GetName()] = long; ENTS[bath:GetName()] = bath
    host()
    check("T: in a session: the punishment's two skip-time cutscenes last a second, no target time",
        short.Properties.Duration == "0h0m1s" and long.Properties.Duration == "0h0m1s" and long.Properties.TargetTime == "")
    check("T: ...and nothing else's (the bath keeps its hour)", bath.Properties.Duration == "1h")
    host()
    check("T: every second: nothing changes twice", logCount("WO139-SKIPTIME crime_punishment_skipTime_short_data ->") == 1)
    short.Properties.Duration = "2h0m0s"     -- a load re-creates the entity from the level
    host()
    check("T: a load put the level's value back: set again", short.Properties.Duration == "0h0m1s" and logCount("WO139-SKIPTIME crime_punishment_skipTime_short_data ->") == 2)
    KCD2MP_W139Session(false, false, false)
    check("T: out of the session: the originals back", short.Properties.Duration == "2h0m0s" and long.Properties.TargetTime == "10h0m0s" and long.Properties.Duration == "")
    local mark = #LOG
    joiner()
    check("T: the joiner too (his clock never moves by itself), and the agent is told", short.Properties.Duration == "0h0m1s" and e1("w139_punish", mark) == "1")
    noErrs("T")
end

-- ================================================================ B: backstop, switches, commands

do
    reset(); clearLog(); NOW = 5000
    joiner()
    local g = mkNpc("tzel_man_7", 104, 100, 10, GUARD_F, { ctx = GUARD_CTX })
    KCD2MP.npcPuppets["tzel_man_7"] = { nativeSent = true }
    local h = mkNpc("host_horse", 105, 105, 10, ZEL, { class = "Horse" }); setmetatable(h, { __index = Horse })
    KCD2MP_W139LegalHorses("host_horse")
    KCD2MP_W139Stop(90, "tzel_man_7", "trespass:1")
    NOW = NOW + 5
    KCD2MP_W139Backstop()
    check("B: the agent went silent: still fresh inside the timeout", KCD2MP.w139.stop ~= nil)
    NOW = NOW + 6
    local mark = #LOG
    KCD2MP_W139Backstop()
    check("B: past the timeout: the stop ends, the horses go back", KCD2MP.w139.stop == nil and h.mountIsLegal == false and #emitted("w139_outcome", mark) == 1)
    mark = #LOG
    KCD2MP_SetCrimeShared("off")
    check("B: mp_crime_shared off reaches the agent", e1("w139_cfg", mark) == "shared=off" and KCD2MP.w139.shared == false)
    KCD2MP_SetCrimeShared("on")
    check("B: ...and back on (the default)", KCD2MP.w139.shared == true)
    check("B: a bad value is refused", KCD2MP_SetCrimeShared("maybe") == false and KCD2MP.w139.shared == true)
    KCD2MP_W139Status()
    check("B: the status line", logCount("WO139-STATUS shared=on") == 1)
    check("B: mp_crime_shared / mp_crime_status / mp_w139_test registered",
        CCMDS["mp_crime_shared"] ~= nil and CCMDS["mp_crime_shared"].body == "KCD2MP_SetCrimeShared(%line)"
        and CCMDS["mp_crime_status"] ~= nil and CCMDS["mp_crime_status"].body == "KCD2MP_W139Status()"
        and CCMDS["mp_w139_test"] ~= nil and CCMDS["mp_w139_test"].body == "KCD2MP_W139Test(%line)")
    check("B: the wraps are installed once (a second install keeps the first)", (function()
        local s, m, hm, c = Stash.OnUsed, Minigame.StartLockPicking, Horse.OnMount, Crime.SendResolveDialogResult
        KCD2MP_W139Install()
        return Stash.OnUsed == s and Minigame.StartLockPicking == m and Horse.OnMount == hm and Crime.SendResolveDialogResult == c
    end)())
    check("B: ships on", KCD2MP.w139.shared == true)
    noErrs("B")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
