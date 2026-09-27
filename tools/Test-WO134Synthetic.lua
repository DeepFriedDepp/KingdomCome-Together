-- WO-134 synthetic test: the new rules for world items, against the real kdcmp.lua
-- under MoonSharp (engine stubbed; the harness is the drop suite's,
-- Test-WO134DropsSynthetic.lua, plus NPC bodies and containers).
--   B  NPC bodies: the joiner's loot is a request; the copy is set to the host's
--      list (worn items put on) and the loot screen opens on it; takes go to the
--      host; a stale take is rolled back; the host's looting updates the copy;
--      the host answers from its one body; pickpocketing stays blocked
--   I  loose world items: a joiner's pickup asks the host first (ok / gone /
--      unknown / no answer); the host matches by class + position, removes its
--      copy; the host's pickup removes the joiner's; a drop is never touched
--   C  chests: takes and puts in reach are recorded (never a grave); the join
--      ledger is applied (put back / taken out / expired left to the engine)
-- What this proves: the Lua halves. Live evidence: docs/WO-134-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}; LOCKS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
local function d2(a, b) return (a.x - b.x) ^ 2 + (a.y - b.y) ^ 2 + (a.z - b.z) ^ 2 end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetCVar = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function(p, r)
    local o = {}
    for _, e in pairs(ENTS) do
        if e.GetWorldPos and d2(e:GetWorldPos(), p) <= r * r then o[#o + 1] = e end
    end
    table.sort(o, function(a, b) return a.id < b.id end)
    return o
end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
System.AddCCommand = function(name, body, help) CCMDS[name] = true end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Game.AddSaveLock = function(name) LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) LOCKS[name] = nil; return true end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }
XGenAIModule = mkstub()

-- ---- items and inventories ----
ITEMS = {}          -- wuid -> { class, amount, health }
NEXTWUID = 100
local function newItem(cls, hp, amt)
    NEXTWUID = NEXTWUID + 1
    local w = "wuid" .. NEXTWUID
    ITEMS[w] = { class = cls, amount = amt or 1, health = hp or 1, id = w }
    return w
end
ItemManager = mkstub()
ItemManager.GetItem = function(w) return ITEMS[w] end
local function mkInventory()
    local inv = { list = {} }
    inv.GetInventoryTable = function(self) local t = {}; for i, w in ipairs(self.list) do t[i] = w end; return t end
    inv.CreateItem = function(self, cls, hp, amt) local w = newItem(cls, hp, amt); self.list[#self.list + 1] = w; return true end
    inv.AddItem = function(self, w)
        -- the engine's move: an item going into an inventory leaves the world
        for n, e in pairs(ENTS) do if e.class == "PickableItem" and e.item and e.item.GetId() == w then ENTS[n] = nil end end
        self.list[#self.list + 1] = w
    end
    inv.DeleteItem = function(self, w, n)
        for i, x in ipairs(self.list) do if x == w then table.remove(self.list, i); ITEMS[w] = nil; return true end end
        return false
    end
    inv.RemoveItem = function(self, w) for i, x in ipairs(self.list) do if x == w then table.remove(self.list, i); return w end end end
    inv.GetCountOfClass = function(self, cls) local n = 0; for _, w in ipairs(self.list) do if ITEMS[w] and ITEMS[w].class == cls then n = n + ITEMS[w].amount end end; return n end
    inv.GetId = function() return "inv" end
    return inv
end

NEXTID = 5000
ENTNUM = 2000
local function mkPickable(name, cls, amt, hp, x, y, z, wuid)
    NEXTID = NEXTID + 1
    local e = { class = "PickableItem", id = NEXTID, px = x, py = y, pz = z,
                Properties = { sItemClassId = cls, nAmount = amt, fHealth = hp } }
    e.GetName = function() return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.SetFlags = function() end
    e.item = { GetId = function() return wuid end,
               BelongsToDeadBody = function() return false end, CanSteal = function() return false end,
               CanUse = function() return true end,
               OnUsed = function(_, uid)
                   -- the engine's pickup: the ground entity goes, the item joins the user's inventory
                   if not ENTS[name] then return false end
                   ENTS[name] = nil
                   player.inventory:AddItem(wuid)
                   return true
               end }
    -- an entity's script table delegates to its class table (the engine's Delegate)
    setmetatable(e, { __index = function(_, k) return PickableItem and PickableItem[k] end })
    ENTS[name] = e
    return e
end
-- A game-like engine name for a minted pickable, as PlaceItem produces.
local function engineName(cls)
    ENTNUM = ENTNUM + 1
    return "onion00" .. ENTNUM
end

player = { id = 1, class = "Player", inventory = mkInventory() }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = 100, y = 100, z = 10 } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end }
player.human = { IsInDialog = function() return false end,
                 PlaceItem = function(self, w, anchorId, _)
                     local a = nil
                     for _, e in pairs(ENTS) do if e.id == anchorId then a = e end end
                     if not a then error("no anchor") end
                     player.inventory:RemoveItem(w)
                     local it = ITEMS[w]
                     local p = a:GetWorldPos()
                     mkPickable(engineName(it.class), it.class, it.amount, it.health, p.x, p.y, p.z, w)
                 end }
ENTS["Dude"] = player

-- The game's own PickableItem script table (Scripts/Entities/Items/PickableItem.lua,
-- the functions the use action calls) -- defined before the mod loads, as in the game.
PickableItem = {}
function PickableItem:Use(user) if user then return self.item:OnUsed(user.id) end return false end
function PickableItem:OnUsed(user) return self:Use(user) end
function PickableItem:OnUsedHold(user) return false end

System.SpawnEntity = function(t)
    local name = t.name or ("spawned" .. NEXTID)
    if t.class == "PickableItem" then
        local e = mkPickable(name, nil, 1, 1, t.position.x, t.position.y, t.position.z, nil)
        e.Properties = {}
        return e
    end
end

-- a ghost to place through (the peer's avatar)
local function mkGhost(id)
    NEXTID = NEXTID + 1
    local g = { class = "NPC", id = NEXTID, inventory = mkInventory() }
    g.GetName = function() return "kcd2mp_" .. id end
    g.GetWorldPos = function() return { x = 103, y = 100, z = 10 } end
    g.human = { PlaceItem = function(self, w, anchorId, _)
        local a = nil
        for _, e in pairs(ENTS) do if e.id == anchorId then a = e end end
        g.inventory:RemoveItem(w)
        local it = ITEMS[w]
        local p = a:GetWorldPos()
        mkPickable(engineName(it.class), it.class, it.amount, it.health, p.x, p.y, p.z, w)
    end }
    ENTS[g:GetName()] = g
    return g
end

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return table.unpack(r)
end


-- ---- NPC bodies and containers ----
local function mkBody(name, x, y, z)
    NEXTID = NEXTID + 1
    local e = { class = "NPC", id = NEXTID, px = x, py = y, pz = z, dead = true, inventory = mkInventory(), equipped = {} }
    e.GetName = function() return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end,
                EquipInventoryItem = function(_, w) e.equipped[#e.equipped + 1] = w end }
    e.human = { IsInDialog = function() return false end }
    e.Hide = function() end
    e.IsHidden = function() return false end
    ENTS[name] = e
    return e
end
local function mkChest(name, cls, x, y, z, restock)
    NEXTID = NEXTID + 1
    local e = { class = cls or "Stash", id = NEXTID, px = x, py = y, pz = z, inventory = mkInventory(), stash = {},
                Properties = { Database = { nRestockPeriodDays = restock or 7 } } }
    e.GetName = function() return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    ENTS[name] = e
    return e
end
LOOTS = {}
BasicAIActions = {}
function BasicAIActions:OnLoot(user, slot) LOOTS[#LOOTS + 1] = self:GetName() end
function BasicAIActions:OnPickpocketing(user, slot) LOOTS[#LOOTS + 1] = "pp:" .. self:GetName() end

-- @@KDCMP@@

-- Part 2: scenarios.
local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function events(kind)
    local o = {}
    for _, l in ipairs(LOG) do
        local a = string.match(l, "%[KCD2%-MP%-EVT%] v1 %d+ " .. kind .. " (.*)$")
        if a then o[#o + 1] = a end
    end
    return o
end
local function lastToast() return TOASTS[#TOASTS] end
local COAT = "a856e87a-8065-4338-919d-0aff7a63341d"
local MONEY = "5ef63059-322e-4e1b-abe8-926e100c770e"
local APPLE = "2264f217-590e-4c0f-a4c6-f50c6532b9f6"
local DICE = "b8df2253-e5c8-4e6c-9303-b4bc84192e67"
local ONION = "4a6fa310-067a-404d-9813-bd1761d1c70d"
local EGG = "7ae2e77b-bdae-46cb-b6ac-f532cf225748"
local SWORD = "b0fc8e19-af72-4771-9517-caec4f568920"

local function world()
    for n in pairs(ENTS) do ENTS[n] = nil end
    ENTS["Dude"] = player
    player.inventory = mkInventory()
    KCD2MP.itemDrops = {}; KCD2MP._itemSeen = {}
    KCD2MP.ghosts = {}; KCD2MP.npcPuppets = {}
    local w = KCD2MP.w134
    w.pendingOpen = {}; w.sessions = {}; w.partial = {}; w.stash = {}; w.itemReq = {}; w.hostTaken = {}; w.hostVerify = {}
    w.bodySig = {}; w.chestSess = {}
    w.bodies, w.items, w.chests = true, true, true
    LOG = {}; TOASTS = {}; LOOTS = {}; TIMERS = {}
end
local function asJoiner()
    KCD2MP.hitSensorOn = false
    KCD2MP.wo102.authorityHost = true
    KCD2MP_W131Tick(true, true)
    KCD2MP_W134Tick(true, false, true, 1)
    TIMERS = {}
end
local function asHost(peers)
    KCD2MP.hitSensorOn = true
    KCD2MP.w131.joiner = false
    KCD2MP_W134Tick(false, true, true, peers or 1)
    TIMERS = {}
end
local function loop() NOW = NOW + 0.25; KCD2MP.w134.loopOnce(); TIMERS = {} end
local function firstTok(kind) local e = events(kind)[1]; return e and string.match(e, "^(%S+)") end

-- ================= B: NPC bodies =================
world(); asJoiner()
local b = mkBody("bandit_7", 101, 100, 10)
b.inventory:CreateItem(COAT, 0.2, 1); b.inventory:CreateItem(APPLE, 1, 1)   -- the copy's own (stale) items
BasicAIActions.OnLoot(b, player, 1)
check("B1 a joiner's loot of a host-owned body asks the host (w134_open), nothing opens yet", events("w134_open")[1] == "bandit_7" and #LOOTS == 0, events("w134_open")[1])
check("B1 ... and it is not the old block toast", not (lastToast() and string.find(lastToast(), "Only the host can loot", 1, true)), lastToast())
KCD2MP_W134BodyState("bandit_7", "open", 1, 1, 1, { { COAT, 1, 0.7333, true }, { MONEY, 201, 1, false } })
local inv = {}
for _, w in ipairs(b.inventory.list) do inv[#inv + 1] = ITEMS[w].class .. ":" .. ITEMS[w].amount .. ":" .. ITEMS[w].health end
table.sort(inv)
check("B1 the copy now holds exactly the host body's items", #inv == 2 and string.find(inv[1], MONEY .. ":201", 1, true) ~= nil and string.find(inv[2], COAT .. ":1:0.7333", 1, true) ~= nil, table.concat(inv, " "))
check("B1 the host's worn coat is put on the copy", #b.equipped == 1 and ITEMS[b.equipped[1]] ~= nil and ITEMS[b.equipped[1]].class == COAT)
check("B1 then the loot screen opens (the game's own OnLoot)", LOOTS[1] == "bandit_7")
local mw = nil
for _, w in ipairs(b.inventory.list) do if ITEMS[w].class == MONEY then mw = w end end
b.inventory:RemoveItem(mw); player.inventory:AddItem(mw)
LOG = {}; loop()
local tk = events("w134_take")[1]
check("B2 the take is seen and sent to the host", tk ~= nil and string.find(tk, "bandit_7 " .. MONEY .. " 201", 1, true) ~= nil, tk)
local tok = firstTok("w134_take")
KCD2MP_W134BodyState("bandit_7", "update", 1, 1, 1, { { COAT, 1, 0.7333, true }, { MONEY, 201, 1, false } })
check("B2 a host state older than my take does not put the item back in the copy", b.inventory:GetCountOfClass(MONEY) == 0 and player.inventory:GetCountOfClass(MONEY) == 201)
KCD2MP_W134TakeResult(tok, "ok", "bandit_7")
check("B2 host says ok: kept on Henry", player.inventory:GetCountOfClass(MONEY) == 201)
local cw = nil
for _, w in ipairs(b.inventory.list) do if ITEMS[w].class == COAT then cw = w end end
b.inventory:RemoveItem(cw); player.inventory:AddItem(cw)
LOG = {}; loop()
tok = firstTok("w134_take")
KCD2MP_W134TakeResult(tok, "gone", "bandit_7")
check("B3 host says gone: the coat is taken back off Henry", player.inventory:GetCountOfClass(COAT) == 0)
check("B3 ... with the plain line", lastToast() == "Someone already took that.", lastToast())
KCD2MP_W134BodyState("bandit_7", "update", 1, 1, 1, {})
check("B4 the host's looting empties the copy", #b.inventory.list == 0)
b.inventory:CreateItem(APPLE, 1, 1)
LOG = {}; loop()
check("B5 a put into the body goes to the host", events("w134_put")[1] ~= nil and string.find(events("w134_put")[1], "bandit_7 " .. APPLE .. " 1", 1, true) ~= nil, events("w134_put")[1])
local v = mkBody("villager_3", 102, 100, 10); v.dead = false
TOASTS = {}; LOOTS = {}
BasicAIActions.OnPickpocketing(v, player, 1)
check("B6 pickpocketing a living host-owned NPC stays blocked", #LOOTS == 0 and lastToast() ~= nil and string.find(lastToast(), "pickpocket", 1, true) ~= nil, lastToast())
local d = mkBody("bandit_8", 103, 100, 10); d.dead = false
d.inventory:CreateItem(COAT, 1, 1)
KCD2MP_W134BodyState("bandit_8", "update", 1, 1, 1, {})
check("B7 a living copy is not stripped by a host list", #d.inventory.list == 1)
d.dead = true; loop()
check("B7 ... it is at its death", #d.inventory.list == 0)
local p1, p2 = {}, {}
for i = 1, 10 do p1[#p1 + 1] = { APPLE, 1, 1, false } end
p2[1] = { COAT, 1, 0.5, false }
local e9 = mkBody("bandit_9", 104, 100, 10)
KCD2MP_W134BodyState("bandit_9", "update", 1, 1, 2, p1)
check("B8 part 1 of 2 alone changes nothing", #e9.inventory.list == 0)
KCD2MP_W134BodyState("bandit_9", "update", 1, 2, 2, p2)
check("B8 both parts: 11 items", #e9.inventory.list == 11)
world(); asJoiner()
b = mkBody("bandit_7", 101, 100, 10)
BasicAIActions.OnLoot(b, player, 1)
for i = 1, 20 do loop() end
check("B9 no answer within 4 s: told, nothing opened", #LOOTS == 0 and lastToast() == "The host didn't answer -- try again.", lastToast())
world(); asJoiner(); KCD2MP.w134.bodies = false
b = mkBody("bandit_7", 101, 100, 10)
BasicAIActions.OnLoot(b, player, 1)
check("B10 mp_loot_bodies off: the WO-131 block as before", #events("w134_open") == 0 and lastToast() ~= nil and string.find(lastToast(), "Only the host can loot", 1, true) ~= nil, lastToast())

world(); asHost(1)
local hb = mkBody("bandit_7", 101, 100, 10)
for i = 1, 12 do hb.inventory:CreateItem(APPLE, 1, 1) end
hb.inventory:CreateItem(MONEY, 1, 201)
LOG = {}
KCD2MP_W134HostOpen(1, 55, "bandit_7")
local st = events("w134_bstate")
check("B11 the host answers with its body's 13 items in 2 parts", #st == 2 and string.find(st[1], "^1 55 bandit_7 open 1 1 2 ") ~= nil and string.find(st[2], "^1 55 bandit_7 open 1 2 2 ") ~= nil, st[1])
LOG = {}
KCD2MP_W134HostTake(1, 56, "bandit_7", MONEY, 201, 1)
check("B12 a joiner's take comes out of the host's body: ok", events("w134_tres")[1] == "1 56 ok bandit_7 " .. MONEY .. " 201" and hb.inventory:GetCountOfClass(MONEY) == 0, events("w134_tres")[1])
LOG = {}
KCD2MP_W134HostTake(2, 57, "bandit_7", MONEY, 201, 1)
check("B13 the same take again (the other player, a moment later): gone", events("w134_tres")[1] == "2 57 gone bandit_7 " .. MONEY .. " 201", events("w134_tres")[1])
check("B13 every item exists once: no money left in the body, none created", hb.inventory:GetCountOfClass(MONEY) == 0)
LOG = {}; for i = 1, 5 do loop() end
check("B14 the changed body goes to every joiner (update, peer 0)", events("w134_bstate")[1] ~= nil and string.find(events("w134_bstate")[1], "^0 0 bandit_7 update ") ~= nil, events("w134_bstate")[1])
LOG = {}; for i = 1, 5 do loop() end
check("B14 ... once (no change, no resend)", #events("w134_bstate") == 0)
local aw = hb.inventory.list[1]; hb.inventory:RemoveItem(aw); player.inventory:AddItem(aw)
LOG = {}; for i = 1, 5 do loop() end
check("B15 the host's own loot reaches the joiners", #events("w134_bstate") >= 1)
KCD2MP_W134HostPut(1, 58, "bandit_7", COAT, 1, 0.5)
check("B16 a joiner's put lands in the host's body", hb.inventory:GetCountOfClass(COAT) == 1)

-- ================= I: loose world items =================
world(); asJoiner()
local egg = mkPickable("egg000352", EGG, 1, 1, 101, 100, 10, newItem(EGG, 1, 1))
PickableItem.OnUsed(egg, player)
local ask = events("w134_item")[1]
check("I1 a joiner's pickup of a world item asks the host (class + position), nothing picked up", ask ~= nil and string.find(ask, EGG .. " 101.000 100.000 10.000 0", 1, true) ~= nil and ENTS["egg000352"] ~= nil and player.inventory:GetCountOfClass(EGG) == 0, ask)
PickableItem.OnUsed(egg, player)
check("I1 pressing again while asking sends nothing more", #events("w134_item") == 1)
KCD2MP_W134ItemResult(firstTok("w134_item"), "ok")
check("I2 host ok: picked up here by the game's own pickup", ENTS["egg000352"] == nil and player.inventory:GetCountOfClass(EGG) == 1)
local egg2 = mkPickable("egg000351", EGG, 1, 1, 101, 100.06, 10, newItem(EGG, 1, 1))
LOG = {}; PickableItem.OnUsed(egg2, player)
KCD2MP_W134ItemResult(firstTok("w134_item"), "gone")
check("I3 host gone: removed here, not picked up, told", ENTS["egg000351"] == nil and player.inventory:GetCountOfClass(EGG) == 1 and lastToast() == "Someone already took that.")
local sw = mkPickable("shortsword000628", SWORD, 1, 1, 105, 100, 10, newItem(SWORD, 1, 1))
LOG = {}; PickableItem.OnUsed(sw, player)
KCD2MP_W134ItemResult(firstTok("w134_item"), "unknown")
check("I4 host unknown (no such item there): picked up here as before (per machine)", ENTS["shortsword000628"] == nil and player.inventory:GetCountOfClass(SWORD) == 1)
local ax = mkPickable("axe000354", "1fc42528-2bef-4dde-bf8a-04febeef41c8", 1, 1, 106, 100, 10, newItem("1fc42528-2bef-4dde-bf8a-04febeef41c8", 1, 1))
LOG = {}; TOASTS = {}; PickableItem.OnUsed(ax, player)
for i = 1, 20 do loop() end
check("I5 no answer: told, the item stays", ENTS["axe000354"] ~= nil and lastToast() == "The host didn't answer -- try again.", lastToast())
KCD2MP.itemDrops["3000000009"] = { state = "ground", entName = "onion009999", mine = false }
local drop = mkPickable("onion009999", ONION, 1, 1, 102, 100, 10, newItem(ONION, 1, 1))
LOG = {}; PickableItem.OnUsed(drop, player)
check("I6 a player's drop is picked up straight away, no host request (WO-48 as before)", #events("w134_item") == 0 and ENTS["onion009999"] == nil)
local egg3 = mkPickable("egg000350", EGG, 1, 1, 103, 100, 10, newItem(EGG, 1, 1))
KCD2MP_W134ItemGone(EGG, 103.0, 100.0, 10.0)
check("I7 the host's pickup removes the joiner's copy", ENTS["egg000350"] == nil)
local drop2 = mkPickable("onion008888", ONION, 1, 1, 104, 100, 10, newItem(ONION, 1, 1))
KCD2MP.itemDrops["3000000010"] = { state = "ground", entName = "onion008888", mine = false }
KCD2MP_W134ItemGone(ONION, 104, 100, 10)
check("I7 ... never a tracked drop at the same spot", ENTS["onion008888"] ~= nil)
local far = mkPickable("egg000349", EGG, 1, 1, 110, 100, 10, newItem(EGG, 1, 1))
KCD2MP_W134ItemGone(EGG, 110.5, 100, 10)
check("I7 ... nor one 0.5 m off (no guessing)", ENTS["egg000349"] ~= nil)

world(); asHost(1)
mkPickable("egg000400", EGG, 1, 1, 101, 100, 10, newItem(EGG, 1, 1))
LOG = {}
KCD2MP_W134HostItem(1, 71, EGG, 101.0, 100.0, 10.0, 0)
check("I8 the host matches the joiner's item by class + position and removes its copy: ok", events("w134_ires")[1] ~= nil and string.find(events("w134_ires")[1], "1 71 ok ", 1, true) == 1 and ENTS["egg000400"] == nil, events("w134_ires")[1])
LOG = {}
KCD2MP_W134HostItem(2, 72, EGG, 101.0, 100.0, 10.0, 0)
check("I9 the same item asked again: gone", events("w134_ires")[1] ~= nil and string.find(events("w134_ires")[1], "2 72 gone ", 1, true) == 1, events("w134_ires")[1])
LOG = {}
KCD2MP_W134HostItem(1, 73, SWORD, 50, 50, 10, 0)
check("I10 nothing there: unknown", events("w134_ires")[1] ~= nil and string.find(events("w134_ires")[1], "1 73 unknown ", 1, true) == 1)
mkPickable("onion007777", ONION, 1, 1, 104, 100, 10, newItem(ONION, 1, 1))
KCD2MP.itemDrops["3000000011"] = { state = "ground", entName = "onion007777", mine = true }
LOG = {}
KCD2MP_W134HostItem(1, 74, ONION, 104, 100, 10, 0)
check("I11 a host's tracked drop is never handed out as a world item", ENTS["onion007777"] ~= nil and string.find(events("w134_ires")[1] or "", " unknown ", 1, true) ~= nil)
local hegg2 = mkPickable("egg000401", EGG, 1, 1, 102, 100, 10, newItem(EGG, 1, 1))
LOG = {}
PickableItem.OnUsed(hegg2, player)
loop()
check("I12 the host's own pickup: picked up, then gone for every joiner (w134_igone)", ENTS["egg000401"] == nil and events("w134_igone")[1] ~= nil and string.find(events("w134_igone")[1], EGG .. " 102.000", 1, true) == 1, events("w134_igone")[1])
local hdd = mkPickable("onion006666", ONION, 1, 1, 105, 100, 10, newItem(ONION, 1, 1))
KCD2MP.itemDrops["3000000012"] = { state = "ground", entName = "onion006666", mine = false }
LOG = {}; PickableItem.OnUsed(hdd, player); loop()
check("I13 the host picking up a drop sends no world-item line (WO-48's claim does it)", #events("w134_igone") == 0)
world(); asHost(0)
mkPickable("egg000402", EGG, 1, 1, 102, 100, 10, newItem(EGG, 1, 1))
LOG = {}; PickableItem.OnUsed(ENTS["egg000402"], player); loop()
check("I14 nobody connected: nothing is sent", #events("w134_igone") == 0 and ENTS["egg000402"] == nil)

-- ================= C: chests =================
world(); asHost(0)
local ch = mkChest("stash[Chest/chest3_c1d92081-d74b-4955-ade8-384e71325794]", "Stash", 101, 100, 10, 7)
ch.inventory:CreateItem(DICE, 1, 1); ch.inventory:CreateItem(APPLE, 1, 3)
LOG = {}; loop()
check("C1 a chest in reach is snapshotted, nothing recorded yet", #events("w134_chest") == 0)
local dw = nil
for _, w in ipairs(ch.inventory.list) do if ITEMS[w].class == DICE then dw = w end end
ch.inventory:RemoveItem(dw); player.inventory:AddItem(dw)
loop()
local ce = events("w134_chest")[1]
check("C2 the host takes the dice: recorded (+1, the world time, restock 7)", ce == "stash[Chest/chest3_c1d92081-d74b-4955-ade8-384e71325794] " .. DICE .. " 1 1.0000 1000 7", ce)
ch.inventory:CreateItem(COAT, 0.5, 1)
LOG = {}; loop()
check("C3 a put: recorded as -1", events("w134_chest")[1] ~= nil and string.find(events("w134_chest")[1], COAT .. " -1 ", 1, true) ~= nil, events("w134_chest")[1])
local grave = mkChest("kcdmp_grave_1_123", "StashCorpse", 101, 101, 10, 0)
grave.inventory:CreateItem(COAT, 1, 1)
LOG = {}; loop(); grave.inventory.list = {}; loop()
check("C4 a grave is never a chest", #events("w134_chest") == 0)
KCD2MP_W134Tick(false, true, false, 0); TIMERS = {}
ch.inventory:CreateItem(DICE, 1, 1)
LOG = {}; loop(); loop()
check("C5 not a shared world: nothing recorded", #events("w134_chest") == 0)
world(); asJoiner()
local ca = mkChest("stash[Chest/a]", "Stash", 300, 300, 10, 7)
local cb = mkChest("stash[Chest/b]", "Stash", 310, 300, 10, 0)
cb.inventory:CreateItem(DICE, 1, 1)
local cc = mkChest("stash[Chest/c]", "Stash", 320, 300, 10, 7)
WORLD_T = 1000 + 10 * 86400
LOG = {}
KCD2MP_W134ChestApply({ { "stash[Chest/a]", DICE, 1, 1, 1000 + 9 * 86400, 7 },
                        { "stash[Chest/b]", DICE, -1, 1, 1000, 0 },
                        { "stash[Chest/c]", APPLE, 2, 1, 1000, 7 },
                        { "stash[Chest/nowhere]", APPLE, 1, 1, 1000, 0 } })
check("C6 A is full again for the joiner (the host's take put back)", ca.inventory:GetCountOfClass(DICE) == 1)
check("C6 B stays empty for him (his own take)", cb.inventory:GetCountOfClass(DICE) == 0)
check("C6 an entry past its restock is left to the engine", cc.inventory:GetCountOfClass(APPLE) == 0)
check("C6 the apply line counts applied / expired / skipped", events("w134_applied")[1] == "2 1 1", events("w134_applied")[1])
player.GetWorldPos = function() return { x = 300, y = 300, z = 10 } end
KCD2MP_W134ChestApply({ { "stash[Chest/a]", DICE, 1, 1, WORLD_T, 7 } })
LOG = {}; loop(); loop()
check("C7 an apply beside a chest is never recorded as the joiner's take or put", #events("w134_chest") == 0)
player.GetWorldPos = function() return { x = 100, y = 100, z = 10 } end

-- ================= extra: order and the engine's take =================
world(); asJoiner()
local e10 = mkBody("bandit_10", 104, 100, 10)
local q1, q2 = {}, {}
for i = 1, 10 do q1[#q1 + 1] = { APPLE, 1, 1, false } end
q2[1] = { COAT, 1, 0.5, false }
KCD2MP_W134BodyState("bandit_10", "update", 1, 2, 2, q2)
KCD2MP_W134BodyState("bandit_10", "update", 1, 1, 2, q1)
check("X1 parts in any order: the list still applies (2 then 1)", #e10.inventory.list == 11)
KCD2MP_W134BodyState("bandit_10", "update", 1, 1, 1, {})
check("X1 a later whole list replaces it", #e10.inventory.list == 0)
world(); asHost(1)
local gh = mkGhost(1)
KCD2MP.ghosts["1"] = { entity = gh }
local slotw = newItem(EGG, 1, 1)
mkPickable("egg000500", EGG, 1, 1, 101, 100, 10, slotw)
LOG = {}
KCD2MP_W134HostItem(1, 81, EGG, 101.0, 100.0, 10.0, 0)
check("X2 the host takes the item away through the asker's avatar (the engine's take, no slot respawn)", ENTS["egg000500"] == nil and ITEMS[slotw] == nil and #gh.inventory.list == 0)
check("X2 ... and says so", string.find(table.concat(LOG, " "), "taken)", 1, true) ~= nil)

-- a load beside a chest: the restarted loop starts clean (found live at a rejoin)
world(); asHost(0)
player.GetWorldPos = function() return { x = 100, y = 100, z = 10 } end
local cr = mkChest("stash[Chest/reload]", "Stash", 101, 100, 10, 7)
cr.inventory:CreateItem(DICE, 1, 1)
loop()
cr.inventory.list = {}; cr.inventory:CreateItem(APPLE, 1, 1)   -- the load: the chest is the saved one now
KCD2MP.w134LoopRunning = false                                     -- the load killed the chain
KCD2MP.w134.startLoop(); TIMERS = {}
LOG = {}; loop()
check("X3 a chest that changed across a load is not recorded as a take or a put", #events("w134_chest") == 0, events("w134_chest")[1])

check("no Lua errors", #ERRS == 0, ERRS[1])
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
