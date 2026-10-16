-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-195 synthetic test, item 3: the key a body's loot screen makes (the NPC's home key, which no inventory holds), against the real
-- kdcmp.lua under MoonSharp (the stubs are Test-WO134Synthetic.lua's). Driven by Test-WO195LootSynthetic.ps1.
--   K  the joiner: the screen's key is remembered, kept through a host update, and its take goes to the host marked "screen"
--   H  the host: a screen take is granted once per body (ok / mine / gone), an unmarked one is WO-136's "none" as before, the host's own
--      screen copy is removed after a joiner's take, a key really in the inventory comes out of the host's body, the host's own looting first
-- What this proves: the Lua half. Live evidence: docs/WO-195-findings.md.

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

local KEY = "494ed6dc-51b9-444f-8055-3116e08a5329"   -- the NPC's home key (key_home), as the field's loot screen made it
local function count(inv, cls) return inv:GetCountOfClass(cls) end
local function closeScreen()
    TIMERS = {}
    if KCD2MP_W166LootClosed then KCD2MP_W166LootClosed("element") end
    for _, t in ipairs(TIMERS) do t.f() end
    TIMERS = {}
end
local function moveTo(from, to, cls)
    for _, w in ipairs(from.list) do if ITEMS[w].class == cls then from:RemoveItem(w); to:AddItem(w); return w end end
end

-- ================= K: the loot screen's key, the joiner =================
world(); asJoiner()
local b = mkBody("tzel_woman_8", 101, 100, 10)
BasicAIActions.OnLoot(b, player, 1)
KCD2MP_W134BodyState("tzel_woman_8", "open", 1, 1, 1, {})            -- the host's body: nothing (the field's host_items=0)
check("K1 the loot screen opens on the host's (empty) body", LOOTS[1] == "tzel_woman_8")
b.inventory:CreateItem(KEY, 1, 1)                                      -- the game's loot screen puts the home key in, 62 ms later
LOG = {}; loop()
check("K1 the screen's key is no put (WO-136) and is remembered as the loot screen's own (WO195-LOOT screen-item)",
    #events("w134_put") == 0 and #LOG > 0 and table.concat(LOG, "\n"):find("WO195-LOOT screen-item npc=tzel_woman_8 cls=" .. KEY, 1, true) ~= nil)
KCD2MP_W134BodyState("tzel_woman_8", "update", 1, 1, 1, {})          -- a host update before the take (the host's list never has it)
check("K2 a host update keeps the screen's key in the body (not deleted as 'not the host's')", count(b.inventory, KEY) == 1, count(b.inventory, KEY))
moveTo(b.inventory, player.inventory, KEY)
LOG = {}; loop()
local tk = events("w134_take")[1]
check("K3 the key's take goes to the host marked as the loot screen's own", tk ~= nil and tk:sub(-#("tzel_woman_8 " .. KEY .. " 1 1.0000 screen")) == "tzel_woman_8 " .. KEY .. " 1 1.0000 screen", tk)
local tok = firstTok("w134_take")
KCD2MP_W134TakeResult(tok, "ok", "tzel_woman_8")
closeScreen()
check("K3 the host grants it: the key stays on Henry", count(player.inventory, KEY) == 1)
check("K no Lua errors (joiner)", #ERRS == 0, ERRS[1])

-- the same with the switch off: 0.60.9's take (no mark; the host then says "none" and it is taken back)
world(); asJoiner()
KCD2MP_W195SetBodyLoot("off")
b = mkBody("tzel_woman_9", 101, 100, 10)
BasicAIActions.OnLoot(b, player, 1)
KCD2MP_W134BodyState("tzel_woman_9", "open", 1, 1, 1, {})
b.inventory:CreateItem(KEY, 1, 1)
loop()
moveTo(b.inventory, player.inventory, KEY)
LOG = {}; loop()
tk = events("w134_take")[1]
check("K4 mp_body_loot_host off: no mark (0.60.9's take)", tk ~= nil and tk:find(" screen", 1, true) == nil, tk)
KCD2MP_W195SetBodyLoot("on")

-- ================= H: the host's verdict =================
world(); asHost(2)
local hb = mkBody("tzel_woman_8", 101, 100, 10)                         -- the host's body: no key (it never held one)
LOG = {}
KCD2MP_W134HostTake(1, 70, "tzel_woman_8", KEY, 1, 1, true)
check("H1 a loot-screen take of an item the body never held: ok (the joiner keeps it)", events("w134_tres")[1] == "1 70 ok tzel_woman_8 " .. KEY .. " 1", events("w134_tres")[1])
check("H1 ...said (WO195-LOOT host-screen ... -> ok)", table.concat(LOG, "\n"):find("WO195-LOOT host-screen npc=tzel_woman_8 cls=" .. KEY .. " amt=1 from=1 -> ok", 1, true) ~= nil)
LOG = {}
KCD2MP_W134HostTake(1, 71, "tzel_woman_8", KEY, 1, 1, true)
check("H2 the same joiner again (take all + a single take): mine, taken back quietly", events("w134_tres")[1] == "1 71 mine tzel_woman_8 " .. KEY .. " 1", events("w134_tres")[1])
LOG = {}
KCD2MP_W134HostTake(2, 72, "tzel_woman_8", KEY, 1, 1, true)
check("H3 another joiner's screen key from the same body: gone (one key per body)", events("w134_tres")[1] == "2 72 gone tzel_woman_8 " .. KEY .. " 1", events("w134_tres")[1])
LOG = {}
KCD2MP_W134HostTake(1, 73, "tzel_woman_8", APPLE, 1, 1)
check("H4 an unmarked take of an item the body never held: none, as before (WO-136)", events("w134_tres")[1] == "1 73 none tzel_woman_8 " .. APPLE .. " 1", events("w134_tres")[1])
-- the host's own loot screen makes the key again: removed (no duplicate)
hb.inventory:CreateItem(KEY, 1, 1)
LOG = {}
KCD2MP_W195HostDup("tzel_woman_8", hb)
check("H5 the host's own screen copy of a key the joiner took is removed", count(hb.inventory, KEY) == 0 and table.concat(LOG, "\n"):find("WO195-LOOT host-dup npc=tzel_woman_8", 1, true) ~= nil)
-- a key really in the host body's inventory (a quest's or a placed one): the normal take -- out of the host's body
local hb2 = mkBody("tzel_man_13", 103, 100, 10)
hb2.inventory:CreateItem(KEY, 1, 1)
LOG = {}
KCD2MP_W134HostTake(1, 74, "tzel_man_13", KEY, 1, 1)
check("H6 a key in the host body's inventory: ok, and the host's body no longer has it", events("w134_tres")[1] == "1 74 ok tzel_man_13 " .. KEY .. " 1" and count(hb2.inventory, KEY) == 0, events("w134_tres")[1])
-- the host took its own screen key first (seen in a watch pass, then gone): a joiner's screen take of it is gone
local hb3 = mkBody("tzel_man_14", 105, 100, 10)
KCD2MP.w134.bodySig = {}
hb3.inventory:CreateItem(KEY, 1, 1)
NOW = NOW + 1.1; loop()                                                   -- the host's watch (once a second) sees the key (its own screen made it)
for _, w in ipairs(hb3.inventory.list) do if ITEMS[w].class == KEY then hb3.inventory:RemoveItem(w); player.inventory:AddItem(w) break end end
NOW = NOW + 1.1; loop()                                                   -- ...and sees it go (the host's own looting)
LOG = {}
KCD2MP_W134HostTake(1, 75, "tzel_man_14", KEY, 1, 1, true)
check("H7 the host looted its own screen key first: a joiner's screen take of it is gone", events("w134_tres")[1] == "1 75 gone tzel_man_14 " .. KEY .. " 1", events("w134_tres")[1])
KCD2MP_W195SetBodyLoot("off")
LOG = {}
KCD2MP_W134HostTake(1, 76, "tzel_woman_10", KEY, 1, 1, true)
check("H8 mp_body_loot_host off on the host: the mark is ignored (0.60.9's verdict)", (events("w134_tres")[1] or ""):find(" none ", 1, true) ~= nil or (events("w134_tres")[1] or ""):find(" gone ", 1, true) ~= nil, events("w134_tres")[1])
KCD2MP_W195SetBodyLoot("on")

check("no Lua errors", #ERRS == 0, ERRS[1])
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
