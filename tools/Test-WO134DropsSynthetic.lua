-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-134 Phase 1: the DROP REGRESSION, against the real kdcmp.lua under MoonSharp.
-- Players dropping items for each other must keep working exactly as it did
-- before WO-134 (WO-48's item sync). This suite is written against the WO-48
-- behaviour and was run green on the pre-WO-134 code (docs/WO-134-progress.md);
-- every later change must keep it green. It never names a WO-134 function
-- except through `if ... then` guards, so it runs unchanged on both sides.
--
-- Each scenario runs in three session configurations -- solo, the JOINER of a
-- shared world (the copy guard active), the HOST of a shared world -- so a new
-- rule that grabs a player's drop in a session shows up here:
--   D1 a peer's drop: pending -> materialized through a ghost -> picked up with
--      PickableItem.OnUsed (the function the game's use action calls) -> claim
--      event -> the relay's echo names me -> resolved, the item stays with me
--   D2 the same, but the echo names the other player (lost race): rolled back
--   D3 my own drop: detected (new pickable + my count went down) -> item_drop
--      event -> registered -> the peer's claim removes my ground copy, no echo claim
--   D4 a claim for a drop not yet materialized: it never appears
--   D5 my own drop, picked back up by me before the peer: claim event, kept on echo
--   D6 the new rules' own hooks (when they exist) see a tracked drop and leave it
--      alone: a host "world item gone" for the drop's class and spot removes
--      nothing; a joiner pickup of a drop never becomes a host request

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
local function anyEvent(pat)
    for _, l in ipairs(LOG) do if string.find(l, "[KCD2-MP-EVT]", 1, true) and string.find(l, pat, 1, true) then return l end end
    return nil
end
local function runTimers()
    local t = TIMERS; TIMERS = {}
    for _, x in ipairs(t) do x.f() end
end
local ONION = "4a6fa310-067a-404d-9813-bd1761d1c70d"
local BANDAGE = "9fa3000e-3807-48a8-bed8-81427f0bda55"

local function tick()
    NOW = NOW + 0.75
    -- one item-sync tick, exactly as its own timer runs it
    KCD2MP_ItemSyncTick()
    TIMERS = {}
end

-- The session configurations. Each one is set the way the agent sets it.
local CONFIGS = {
    solo = function()
        if KCD2MP.w131 then KCD2MP.w131.joiner = false; KCD2MP.w131.aliveAt = nil end
        KCD2MP.hitSensorOn = false
        if KCD2MP.w122 then KCD2MP.w122.sharedWorld = false end
        if KCD2MP_W134Tick then KCD2MP_W134Tick(false, false, false, 0) end
    end,
    joiner = function()
        KCD2MP.hitSensorOn = false
        if KCD2MP.wo102 then KCD2MP.wo102.authorityHost = true end
        if KCD2MP_W131Tick then KCD2MP_W131Tick(true, true) end
        if KCD2MP_W134Tick then KCD2MP_W134Tick(true, false, true, 1) end
    end,
    host = function()
        KCD2MP.hitSensorOn = true
        if KCD2MP.w122 then KCD2MP.w122.sharedWorld = true end
        if KCD2MP.w131 then KCD2MP.w131.joiner = false end
        if KCD2MP_W134Tick then KCD2MP_W134Tick(false, true, true, 1) end
    end,
}

local function freshWorld()
    for n in pairs(ENTS) do ENTS[n] = nil end
    ENTS["Dude"] = player
    player.inventory = mkInventory()
    KCD2MP.itemDrops = {}; KCD2MP._itemSeen = {}; KCD2MP._itemInvCounts = nil
    KCD2MP.ghosts = {}
    local g = mkGhost(7)
    KCD2MP.ghosts["7"] = { entity = g }
    KCD2MP.itemSync.enabled = true
    KCD2MP.itemSyncRunning = true
    KCD2MP._itemRestartSweep = false
    LOG = {}
    tick()   -- the baseline tick: everything accounted for, nothing emitted
end

for _, cfgName in ipairs({ "solo", "joiner", "host" }) do
    local C = "[" .. cfgName .. "] "
    -- ---- D1: a peer's drop, picked up here, echo names me ----
    freshWorld(); CONFIGS[cfgName]()
    KCD2MP_ItemDropAdd("3000000001", ONION, 1, 0.8, 102, 100, 10, "7")
    tick()   -- spawn: anchor + CreateItem in the ghost + PlaceItem
    tick()   -- finalize: adopt the engine-minted entity
    local d = KCD2MP.itemDrops["3000000001"]
    check(C .. "D1 the peer's drop materializes (state ground)", d and d.state == "ground", d and d.state)
    local ent = d and d.entName and ENTS[d.entName]
    check(C .. "D1 a real ground entity of that class, off the ghost", ent and ent.Properties.sItemClassId == ONION and #KCD2MP.ghosts["7"].entity.inventory.list == 0)
    local before = player.inventory:GetCountOfClass(ONION)
    local used = ent and PickableItem.OnUsed(ent, player)
    check(C .. "D1 the game's pickup picks it up here (no gate, no request)", used == true and player.inventory:GetCountOfClass(ONION) == before + 1,
          tostring(used) .. " " .. before .. "->" .. player.inventory:GetCountOfClass(ONION))
    tick()
    check(C .. "D1 the watcher sends the claim", events("item_claim")[1] == "3000000001", table.concat(events("item_claim"), ","))
    KCD2MP_ItemDropClaimed("3000000001", "1", true)
    check(C .. "D1 the echo naming me resolves it; the item stays", d.state == "resolved" and player.inventory:GetCountOfClass(ONION) == before + 1)
    check(C .. "D1 no other event about it (no host request, no world-item line)",
          not anyEvent("w134_") and #events("item_drop") == 0, anyEvent("w134_"))

    -- ---- D2: the same drop, but the other player won ----
    freshWorld(); CONFIGS[cfgName]()
    KCD2MP_ItemDropAdd("3000000002", BANDAGE, 2, 1, 102, 101, 10, "7")
    tick(); tick()
    d = KCD2MP.itemDrops["3000000002"]
    ent = d and d.entName and ENTS[d.entName]
    before = player.inventory:GetCountOfClass(BANDAGE)
    if ent then PickableItem.OnUsed(ent, player) end
    tick()
    KCD2MP_ItemDropClaimed("3000000002", "7", false)
    check(C .. "D2 lost race: the pickup is rolled back", d and d.state == "resolved" and player.inventory:GetCountOfClass(BANDAGE) == before,
          before .. "->" .. player.inventory:GetCountOfClass(BANDAGE))
    local msg = KCD2MP.interactionMsg and KCD2MP.interactionMsg.text
    check(C .. "D2 the loser is told", msg and string.find(msg, "someone already took that", 1, true) ~= nil, msg)

    -- ---- D3: my own drop ----
    freshWorld(); CONFIGS[cfgName]()
    player.inventory:CreateItem(ONION, 1, 1)
    tick()   -- counts now include the onion
    local w = nil
    for _, x in ipairs(player.inventory.list) do if ITEMS[x].class == ONION then w = x end end
    local anchor = mkPickable("anchor1", nil, 1, 1, 102, 99, 10, nil)
    player.human:PlaceItem(w, anchor.id, false)
    ENTS["anchor1"] = nil
    tick()
    local ev = events("item_drop")
    check(C .. "D3 my drop is detected: item_drop with class, amount, name", #ev == 1 and string.find(ev[1], ONION, 1, true) == 1, ev[1])
    local entName = ev[1] and string.match(ev[1], "(%S+)$")
    KCD2MP_ItemDropRegistered("3000000003", entName)
    d = KCD2MP.itemDrops["3000000003"]
    check(C .. "D3 registered and tracked as mine", d and d.mine and d.state == "ground" and d.entName == entName)
    LOG = {}
    KCD2MP_ItemDropClaimed("3000000003", "7", false)
    check(C .. "D3 the peer's claim removes my ground copy", ENTS[entName] == nil and d.state == "resolved")
    tick()
    check(C .. "D3 ... without a claim echo from me", #events("item_claim") == 0)

    -- ---- D4: claimed before it ever materialized ----
    freshWorld(); CONFIGS[cfgName]()
    KCD2MP_ItemDropAdd("3000000004", ONION, 1, 1, 102, 100, 10, "7")
    KCD2MP_ItemDropClaimed("3000000004", "7", false)
    tick(); tick()
    d = KCD2MP.itemDrops["3000000004"]
    local any = false
    for _, e in pairs(ENTS) do if e.class == "PickableItem" and e.Properties.sItemClassId == ONION then any = true end end
    check(C .. "D4 a drop claimed while pending never appears", d.state == "resolved" and not any)

    -- ---- D5: my own drop, picked back up by me ----
    freshWorld(); CONFIGS[cfgName]()
    player.inventory:CreateItem(ONION, 1, 1)
    tick()
    for _, x in ipairs(player.inventory.list) do if ITEMS[x].class == ONION then w = x end end
    anchor = mkPickable("anchor2", nil, 1, 1, 102, 99, 10, nil)
    player.human:PlaceItem(w, anchor.id, false)
    ENTS["anchor2"] = nil
    tick()
    entName = events("item_drop")[1] and string.match(events("item_drop")[1], "(%S+)$")
    KCD2MP_ItemDropRegistered("3000000005", entName)
    LOG = {}
    local r5 = ENTS[entName] and PickableItem.OnUsed(ENTS[entName], player)
    tick()
    check(C .. "D5 picking my own drop back up: the game's pickup, then my claim", r5 == true and events("item_claim")[1] == "3000000005")
    KCD2MP_ItemDropClaimed("3000000005", "1", true)
    check(C .. "D5 kept on my echo", player.inventory:GetCountOfClass(ONION) == 1 and KCD2MP.itemDrops["3000000005"].state == "resolved")
    check(C .. "D5 no WO-134 request for it", not anyEvent("w134_"), anyEvent("w134_"))

    -- ---- D6: the new rules' hooks leave a tracked drop alone ----
    freshWorld(); CONFIGS[cfgName]()
    KCD2MP_ItemDropAdd("3000000006", ONION, 1, 1, 102, 100, 10, "7")
    tick(); tick()
    d = KCD2MP.itemDrops["3000000006"]
    ent = d and d.entName and ENTS[d.entName]
    if KCD2MP_W134ItemGone and ent then
        KCD2MP_W134ItemGone(ONION, 102, 100, 10)
        check(C .. "D6 a host 'world item gone' at the drop's spot does not remove the drop", ENTS[d.entName] ~= nil)
    else
        check(C .. "D6 (no WO-134 world-item hook in this build)", true)
    end
    if KCD2MP_W134IsWorldItem and ent then
        check(C .. "D6 a tracked drop is not a world item", KCD2MP_W134IsWorldItem(ent) == false)
    end
end

check("no Lua errors", #ERRS == 0, ERRS[1])

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
