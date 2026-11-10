-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-217 synthetic test: C (loot_live) and D (item_flags) against the real kdcmp.lua under MoonSharp (WO-195's loot stubs, with the
-- engine's move: an item added to one inventory leaves the one it was in). Driven by Test-WO217Synthetic.ps1.
--   J  the joiner: the live list opens the screen on the host's items by id (moves only, no deletes in the copy); a take goes into the
--      escrow and enters the pack only on the grant; a refusal deletes it and says "Already taken"; no answer: cancelled, a late grant
--      ignored; a put is not swept out by an older list
--   H  the host: the live list carries item ids, owners and stolen marks; a take is granted once (out of the body into the escrow), the
--      second asker refused; the ack deletes the escrow's copy, a cancel puts it back into the body; the host's own take is logged
--   T  the twin (loot_live off): nothing of this runs
-- What this proves: the Lua half (the ledger's rules). Live evidence: docs/WO-217-findings.md.

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

-- ---- items and inventories: the engine's move (AddItem takes the item out of the inventory that held it) ----
ITEMS = {}          -- wuid -> { class, amount, health, owner, stolen }
NEXTWUID = 100
INVS = {}
local function newItem(cls, hp, amt)
    NEXTWUID = NEXTWUID + 1
    local w = "wuid" .. NEXTWUID
    ITEMS[w] = { class = cls, amount = amt or 1, health = hp or 1, id = w }
    return w
end
ItemManager = mkstub()
ItemManager.GetItem = function(w) return ITEMS[w] end
ItemManager.GetItemOwner = function(w) return ITEMS[w] and ITEMS[w].owner or nil end
ItemManager.SetItemOwner = function(w, o, stolen) if ITEMS[w] then ITEMS[w].owner = o; ITEMS[w].stolen = stolen end end
local function mkInventory()
    local inv = { list = {} }
    INVS[#INVS + 1] = inv
    inv.GetInventoryTable = function(self) local t = {}; for i, w in ipairs(self.list) do t[i] = w end; return t end
    inv.CreateItem = function(self, cls, hp, amt) local w = newItem(cls, hp, amt); self.list[#self.list + 1] = w; return true end
    inv.AddItem = function(self, w)
        if not ITEMS[w] then error("no such item") end
        for _, other in ipairs(INVS) do
            for i, x in ipairs(other.list) do if x == w then table.remove(other.list, i); break end end
        end
        self.list[#self.list + 1] = w
    end
    inv.HasItem = function(self, w) for _, x in ipairs(self.list) do if x == w then return true end end; return false end
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

player = { id = 1, class = "Player", inventory = mkInventory(), this = { id = "wuid_player" } }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = 100, y = 100, z = 10 } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end,
                 UnequipInventoryItem = function(_, w) UNEQUIPPED = (UNEQUIPPED or 0) + 1 end }
player.human = { IsInDialog = function() return false end }
ENTS["Dude"] = player

System.SpawnEntity = function(t)
    NEXTID = NEXTID + 1
    local e = { class = t.class, id = NEXTID, px = t.position.x, py = t.position.y, pz = t.position.z, inventory = mkInventory(), hidden = false }
    e.GetName = function() return t.name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.Hide = function(self, v) self.hidden = v == 1 end
    e.IsHidden = function(self) return self.hidden end
    ENTS[t.name] = e
    return e
end

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return table.unpack(r)
end

local function mkBody(name, x, y, z)
    NEXTID = NEXTID + 1
    local e = { class = "NPC", id = NEXTID, px = x, py = y, pz = z, dead = true, inventory = mkInventory(), equipped = {}, this = { id = "wuid_" .. name } }
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
XGenAIModule = mkstub()
XGenAIModule.GetEntityByWUID = function(w) for _, e in pairs(ENTS) do if e.this and e.this.id == w then return e end end end
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
local function ledger(ev)
    local o = {}
    for _, l in ipairs(LOG) do if string.find(l, "WO217-LEDGER", 1, true) and string.find(l, " ev=" .. ev .. " ", 1, true) then o[#o + 1] = l end end
    return o
end
local SWORD = "b0fc8e19-af72-4771-9517-caec4f568920"
local APPLE = "2264f217-590e-4c0f-a4c6-f50c6532b9f6"
local COAT = "a856e87a-8065-4338-919d-0aff7a63341d"
local ONION = "4a6fa310-067a-404d-9813-bd1761d1c70d"
local EGG = "7ae2e77b-bdae-46cb-b6ac-f532cf225748"

local function world()
    for n in pairs(ENTS) do ENTS[n] = nil end
    ENTS["Dude"] = player
    INVS = {}
    player.inventory = mkInventory()
    KCD2MP.ghosts = {}; KCD2MP.npcPuppets = {}
    local w = KCD2MP.w134
    w.pendingOpen = {}; w.sessions = {}; w.partial = {}; w.stash = {}; w.itemReq = {}; w.hostTaken = {}; w.hostVerify = {}
    w.bodySig = {}; w.chestSess = {}
    w.bodies, w.items, w.chests = true, true, true
    W217.L.sess = {}; W217.L.hostLive = {}; W217.L.hostEsc = {}; W217.L.partial = {}; W217.L.fellBack = {}; W217.L.active = 0
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
local function count(inv, cls) return inv:GetCountOfClass(cls) end
local function moveTo(from, to, cls)
    for _, w in ipairs(from.list) do if ITEMS[w].class == cls then to:AddItem(w); return w end end
end
local function key(w) return (string.gsub(tostring(w), "[^%w]", "_")) end
local function esc() return System.GetEntityByName("kcd2mp_w217_escrow") end

-- ================= J: the joiner =================
world(); asJoiner()
local b = mkBody("bandit_1", 101, 100, 10)
b.inventory:CreateItem(SWORD, 1, 1)
b.inventory:CreateItem(COAT, 0.5, 1)                         -- the copy's own coat: the host's body no longer has it
BasicAIActions.OnLoot(b, player, 1)
check("J0 the loot is a request to the host (WO-134's open)", #events("w134_open") == 1 and #LOOTS == 0, #LOOTS)
KCD2MP_W217BodyList("bandit_1", 1, 1, 1, { { id = "h1", cls = SWORD, amt = 1, hp = 1, worn = 0, stolen = 0, owner = "-" },
                                           { id = "h2", cls = APPLE, amt = 3, hp = 1, worn = 0, stolen = 0, owner = "-" } })
check("J1 the live list opens the game's loot screen on the host's items", LOOTS[1] == "bandit_1", LOOTS[1])
check("J1 the copy holds the host's items: the sword kept, the apples made (through the escrow), the coat out",
    count(b.inventory, SWORD) == 1 and count(b.inventory, APPLE) == 3 and count(b.inventory, COAT) == 0,
    string.format("%d %d %d", count(b.inventory, SWORD), count(b.inventory, APPLE), count(b.inventory, COAT)))
check("J1 nothing was created in the copy itself (made in the escrow, moved in)", esc() ~= nil and esc():IsHidden() and #esc().inventory.list == 0)
check("J1 the agent is told a session lives (its poll)", events("w217_lootlive")[1] == "1", events("w217_lootlive")[1])
-- a take: the screen moves the sword into the pack
local sw = moveTo(b.inventory, player.inventory, SWORD)
LOG = {}
KCD2MP_W217LootPoll()
local rq = events("w217_treq")[1]
local tok = rq and string.match(rq, "^(%S+)")
check("J2 the take is a request with the host's item id", rq ~= nil and rq:find(" bandit_1 h1 " .. SWORD .. " 1", 1, true) ~= nil, rq)
check("J2 the sword is in the escrow, not the pack, until the grant", count(player.inventory, SWORD) == 0 and esc().inventory:HasItem(sw))
check("J2 ledger: request", #ledger("request") == 1)
LOG = {}
KCD2MP_W217TakeResult(tok, "grant", "bandit_1", "h1", "-", 0)
check("J3 granted: the sword enters the pack (the same item)", player.inventory:HasItem(sw) and not esc().inventory:HasItem(sw))
check("J3 the host is told it was applied; ledger: applied", (events("w217_tack")[1] or ""):find(" applied", 1, true) ~= nil and #ledger("applied") == 1)
-- a refused take
local ap = moveTo(b.inventory, player.inventory, APPLE)
LOG = {}
KCD2MP_W217LootPoll()
tok = string.match(events("w217_treq")[1] or "", "^(%S+)")
KCD2MP_W217TakeResult(tok, "refuse", "bandit_1", "h2", "gone", 0)
check("J4 refused: nothing in the pack, the escrow's item deleted, 'Already taken' shown",
    count(player.inventory, APPLE) == 0 and not esc().inventory:HasItem(ap) and ITEMS[ap] == nil and TOASTS[#TOASTS] == "Already taken", TOASTS[#TOASTS])
check("J4 ledger: refused", #ledger("refused") == 1)
-- no answer: cancelled; a late grant changes nothing
local s1 = W217.L.sess["bandit_1"]
b.inventory:CreateItem(EGG, 1, 1)
NOW = NOW + 0.1
KCD2MP_W217BodyList("bandit_1", 2, 1, 1, { { id = "h3", cls = EGG, amt = 1, hp = 1, worn = 0, stolen = 0, owner = "-" } })
local eg = moveTo(b.inventory, player.inventory, EGG)
LOG = {}
KCD2MP_W217LootPoll()
tok = string.match(events("w217_treq")[1] or "", "^(%S+)")
NOW = NOW + 6
KCD2MP_W217LootPoll()
check("J5 no answer in 5 s: cancelled -- the escrow's item deleted, the host told to put it back", ITEMS[eg] == nil and (events("w217_tack")[1] or ""):find(" cancel", 1, true) ~= nil and #ledger("cancel") == 1)
KCD2MP_W217TakeResult(tok, "grant", "bandit_1", "h3", "-", 0)
check("J5 a late grant applies nothing", count(player.inventory, EGG) == 0 and #ledger("late") == 1)
-- a put: an item of the pack into the copy goes to the host (WO-134's put) and an older list does not sweep it out
player.inventory:CreateItem(ONION, 1, 1)
KCD2MP_W217LootPoll()                                         -- the pack's snapshot
local on = moveTo(player.inventory, b.inventory, ONION)
LOG = {}
KCD2MP_W217LootPoll()
check("J6 the put goes to the host", (events("w134_put")[1] or ""):find(" bandit_1 " .. ONION .. " 1", 1, true) ~= nil, events("w134_put")[1])
KCD2MP_W217BodyList("bandit_1", 3, 1, 1, {})
check("J6 a list without it (it predates the put) leaves it in the copy", b.inventory:HasItem(on))
check("J no Lua errors (joiner)", #ERRS == 0, ERRS[1])

-- ================= H: the host =================
world(); asHost(2)
local smith = mkBody("ttac_blacksmith", 140, 100, 10); smith.dead = false
local hb = mkBody("bandit_2", 101, 100, 10)
hb.inventory:CreateItem(SWORD, 1, 1)
hb.inventory:CreateItem(APPLE, 1, 2)
local hsw, hap = hb.inventory.list[1], hb.inventory.list[2]
ItemManager.SetItemOwner(hap, "wuid_ttac_blacksmith", true)    -- stolen goods (the bench stages them from a house)
LOG = {}
KCD2MP_W217HostOpen(1, "bandit_2")
local bl = events("w217_blist")[1] or ""
check("H1 the live list goes to the joiner with item ids", bl:find("^1 bandit_2 1 1 1 ") ~= nil and bl:find(key(hsw) .. ":" .. SWORD, 1, true) ~= nil, bl)
check("H1 ... the stolen apples carry their owner and the mark (item_flags)", bl:find(key(hap) .. ":" .. APPLE .. ":2:1.0000:0:1:ttac_blacksmith", 1, true) ~= nil, bl)
LOG = {}
KCD2MP_W217HostTakeReq(1, "77", "bandit_2", key(hsw), SWORD, 1)
check("H2 granted once: out of the body into the escrow", (events("w217_tres")[1] or "") == "1 77 grant bandit_2 " .. key(hsw) .. " - 0" and not hb.inventory:HasItem(hsw) and esc().inventory:HasItem(hsw), events("w217_tres")[1])
check("H2 ledger: request, grant, removed", #ledger("request") == 1 and #ledger("grant") == 1 and #ledger("removed") == 1)
LOG = {}
KCD2MP_W217HostTakeReq(2, "78", "bandit_2", key(hsw), SWORD, 1)
check("H3 the second asker is refused (gone)", (events("w217_tres")[1] or "") == "2 78 refuse bandit_2 " .. key(hsw) .. " gone 0", events("w217_tres")[1])
KCD2MP_W217HostAck(1, "77", "applied")
check("H4 the joiner applied it: the escrow's copy deleted", ITEMS[hsw] == nil)
LOG = {}
KCD2MP_W217HostTakeReq(1, "79", "bandit_2", key(hap), APPLE, 2)
check("H5 a stolen item's grant carries its owner and mark", (events("w217_tres")[1] or "") == "1 79 grant bandit_2 " .. key(hap) .. " ttac_blacksmith 1", events("w217_tres")[1])
KCD2MP_W217HostAck(1, "79", "cancel")
check("H6 a cancel puts the granted item back into the body", hb.inventory:HasItem(hap) and #ledger("cancelled") == 1)
-- the host's own take, logged through the same ledger
player.inventory:AddItem(hap)
LOG = {}
KCD2MP_W217LootPoll()
check("H7 the host's own take: request, grant, removed, applied (peer=host)", #ledger("applied") == 1 and table.concat(LOG, "\n"):find("peer=host", 1, true) ~= nil)
check("H no Lua errors (host)", #ERRS == 0, ERRS[1])

-- ================= B3: a long name's alias =================
world()
local HORSE = "horse[animalcare/horseCare6_ff15a088-7332-4a17-9536-3e8f6a8898ad]"
local hz = mkBody(HORSE, 120, 100, 10); hz.class = "Horse"; hz.dead = false
System.GetEntitiesByClass = function(cls) local o = {}; for _, e in pairs(ENTS) do if e.class == cls then o[#o + 1] = e end end; return o end
check("B3 the mod's alias is the DLL's (lh838eb35a)", KCD2MP_W217Alias(HORSE) == "lh838eb35a", KCD2MP_W217Alias(HORSE))
check("B3 the alias finds the long-named body", System.GetEntityByName("lh838eb35a") == hz)
CMDS = {}
System.ExecuteCommand("wh_ai_PauseNPC lh838eb35a")
check("B3 a console command with the alias runs on the real name", CMDS[#CMDS] == "wh_ai_PauseNPC " .. HORSE, CMDS[#CMDS])
KCD2MP_W217Set("long_names", "off")
W217.alias.map = {}; W217.alias.at = -1e9
check("B3 long_names off: an alias is no name", System.GetEntityByName("lhe580b514") == nil)
KCD2MP_W217Set("long_names", "on")

-- ================= T: the twin =================
world(); asHost(1)
KCD2MP_W217Set("loot_live", "off")
local tb = mkBody("bandit_3", 101, 100, 10)
tb.inventory:CreateItem(SWORD, 1, 1)
LOG = {}
check("T1 loot_live off: the host makes no live session", KCD2MP_W217HostOpen(1, "bandit_3") == false and #events("w217_blist") == 0)
KCD2MP_W217Set("loot_live", "on")

check("no Lua errors", #ERRS == 0, ERRS[1])
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
