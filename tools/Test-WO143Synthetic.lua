-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-143 synthetic test: activities part 2, the Lua half, against the real kdcmp.lua under
-- MoonSharp (engine stubbed, fake clock; the stubs are Test-WO141Synthetic.lua's).
--   A  the five switches (mp_hand_items, mp_activity_gaits, mp_oneshots, mp_player_minigames,
--      mp_idles): default on, their lines, the agent's sync, bad arguments
--   T  temporary tools: a copy that does not own the host's tool gets one in its own inventory
--      (never twice, never for an avatar); it is deleted once out of the hand; still in the hand:
--      "inhand" to the agent; the end of the session takes back only those out of a hand
--   K  looks: the forced look on the copy (this player, an avatar, an NPC), cleared, mp_idles off
--   S  the status line
-- What this proves: the Lua half. Live evidence: docs/WO-143-findings.md.

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


-- WO-140: the game's bed trigger as the level has it (Scripts/Entities/WH/Triggers/BedTrigger.lua,
-- ActionTrigger's ReportUse merged in): instances resolve ReportUse through the class table.
BED_CALLS = {}
BED_CAN = true
BedTrigger = {}
function BedTrigger:IsLyingAction(action) return action.esActionType == "Stance" and action.sAction == "lying" end
function BedTrigger:CanSleep(user, accurate) return BED_CAN, BED_CAN and nil or "@ui_playerCantSkiptime_combat" end
function BedTrigger:ReportUse(user, item, action) BED_CALLS[#BED_CALLS + 1] = self:GetName() .. ":" .. tostring(action.sAction) end
local function mkBed(name, x, y)
    NEXTID = NEXTID + 1
    local e = { class = "BedTrigger", id = NEXTID, px = x, py = y, pz = 10,
                Properties = { Click = { esActionType = "Stance", sAction = "sitting" }, Hold = { esActionType = "Stance", sAction = "lying" } } }
    e.GetName = function() return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    setmetatable(e, { __index = function(_, k) return BedTrigger[k] end })
    ENTS[name] = e
    return e
end
SAVE_BEDS = {}
EntityModule.WillSleepingOnThisBedSave = function(id) return SAVE_BEDS[id] == true end
LAYING = false; INTERRUPTS = 0
player.player.IsLaying = function() return LAYING end
player.player.InterruptSitting = function() INTERRUPTS = INTERRUPTS + 1; LAYING = false end
RESTSAVES = 0
Game.SaveGameViaResting = function() RESTSAVES = RESTSAVES + 1 end

-- WO-141: the game's action triggers as the level has them (Scripts/Entities/WH/Triggers/
-- ActionTrigger.lua; WaterTubeActionTrigger merges ActionTrigger's functions into its own table).
TRIGGER_CALLS = {}
ActionTrigger = {}
function ActionTrigger:ReportUse(user, item, action) TRIGGER_CALLS[#TRIGGER_CALLS + 1] = self:GetName() .. ":" .. tostring(action.sAction) end
function ActionTrigger:GetLinkedSmartObject() return self.link end
WaterTubeActionTrigger = {}
for k, v in pairs(ActionTrigger) do WaterTubeActionTrigger[k] = v end
local function mkTrigger(cls, name, linkName, click)
    NEXTID = NEXTID + 1
    local link = nil
    if linkName then link = { id = NEXTID + 1000, GetName = function() return linkName end } end
    local e = { class = cls, id = NEXTID, link = link, Properties = { Click = click } }
    e.GetName = function() return name end
    setmetatable(e, { __index = function(_, k) return _G[cls][k] end })
    ENTS[name] = e
    return e
end

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
local emitted = function(name, from)
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
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end

DELETE_COUNTS = {}
-- a copy of the host's NPC: its own inventory (FindItem / CreateItem / DeleteItem as the game binds them)
-- and its hands (GetItemInHand: 0 right, 1 left)
local HOE = "4d444b36-afde-42c9-8107-88ec448d4158"
local SAW = "49200aeb-5676-45eb-9fb2-402df0d09aa9"
local function mkCopy(name, id)
    local inv = { list = {} }
    inv.FindItem = function(self, cls) for _, w in ipairs(self.list) do if ITEMS[w] and ITEMS[w].class == cls then return w end end return nil end
    inv.CreateItem = function(self, cls, hp, amt) NEXTWUID = NEXTWUID + 1; local w = "wuid" .. NEXTWUID; ITEMS[w] = { class = cls, id = w }; self.list[#self.list + 1] = w; return true end
    inv.DeleteItem = function(self, w, n) DELETE_COUNTS[#DELETE_COUNTS + 1] = n; for i, x in ipairs(self.list) do if x == w then table.remove(self.list, i); ITEMS[w] = nil; return true end end return false end
    local e = { class = "NPC", id = id, inventory = inv, hands = {}, looking = nil, lookSets = 0, lookClears = 0 }
    e.GetName = function() return name end
    e.GetWorldPos = function() return { x = 100, y = 104, z = 10 } end
    e.human = { GetItemInHand = function(_, h) return e.hands[h] end }
    e.actor = { SetForcedLookObjectId = function(_, tid) e.looking = tid; e.lookSets = e.lookSets + 1 end,
                ClearForcedLookObjectId = function() e.looking = nil; e.lookClears = e.lookClears + 1 end }
    ENTS[name] = e
    return e
end

-- ================================================================ A: the switches

do
    ERRS = {}
    local w = KCD2MP.w143
    check("A: the first five default on", w.hands == true and w.gaits == true and w.oneshots == true and w.minigames == true and w.idles == true)
    check("A: WO-153: mp_avatar_herbs defaults OFF (the herb loop ended the 0.43.0 joiner crashes)", w.herbs == false, tostring(w.herbs))
    check("A: ...and its command is registered", CCMDS["mp_avatar_herbs"] ~= nil and CCMDS["mp_avatar_herbs"].body == "KCD2MP_SetAvatarHerbs(%line)",
          CCMDS["mp_avatar_herbs"] and CCMDS["mp_avatar_herbs"].body)
    check("A: the commands are registered", CCMDS["mp_hand_items"] ~= nil and CCMDS["mp_activity_gaits"] ~= nil and CCMDS["mp_oneshots"] ~= nil and
          CCMDS["mp_player_minigames"] ~= nil and CCMDS["mp_idles"] ~= nil and CCMDS["mp_activity2_status"] ~= nil)
    check("A: ...with the argument unquoted (the placeholder arrives quoted)", CCMDS["mp_hand_items"] ~= nil and CCMDS["mp_hand_items"].body == "KCD2MP_SetHandItems(%line)",
          CCMDS["mp_hand_items"] and CCMDS["mp_hand_items"].body)
    local cases = { { KCD2MP_SetHandItems, "hands", "mp_hand_items" }, { KCD2MP_SetActivityGaits, "gaits", "mp_activity_gaits" },
                    { KCD2MP_SetOneShots, "oneshots", "mp_oneshots" }, { KCD2MP_SetPlayerMinigames, "minigames", "mp_player_minigames" },
                    { KCD2MP_SetIdles, "idles", "mp_idles" }, { KCD2MP_SetAvatarHerbs, "herbs", "mp_avatar_herbs" } }
    for _, c in ipairs(cases) do
        local fn, key, cmd = c[1], c[2], c[3]
        local mark = #LOG
        check("A: " .. cmd .. " off", fn("off") == true and w[key] == false and emitted("w143", mark)[1] == key .. " off", emitted("w143", mark)[1])
        check("A: ...says what it means", logCount("WO143-SWITCH " .. cmd .. " off --", mark) == 1)
        mark = #LOG
        check("A: " .. cmd .. " on", fn("on") == true and w[key] == true and emitted("w143", mark)[1] == key .. " on")
        check("A: " .. cmd .. " with a bad argument changes nothing", fn("maybe") == false and w[key] == true)
        mark = #LOG
        check("A: " .. cmd .. " bare: says the state, changes nothing", fn(nil) == true and w[key] == true and logCount("WO143-SWITCH " .. cmd .. " on", mark) == 1)
    end
    KCD2MP_SetAvatarHerbs("off")   -- the loop above left every switch on; herbs ships off
    local mark = #LOG
    KCD2MP_W143Sync(true, false, true, true, true, false)
    check("A: the agent's sync: the one it has off hears on", #emitted("w143", mark) == 1 and emitted("w143", mark)[1] == "gaits on", emitted("w143", mark)[1])
    mark = #LOG
    KCD2MP_W143Sync(true, true, true, true, true, false)
    check("A: ...in step: nothing", #emitted("w143", mark) == 0)
    mark = #LOG
    KCD2MP_W143Sync(true, true, true, true, true, true)
    check("A: the agent has herbs ON and this game OFF: the agent hears off", #emitted("w143", mark) == 1 and emitted("w143", mark)[1] == "herbs off", emitted("w143", mark)[1])
    noErrs("A")
end

-- ================================================================ T: temporary tools

do
    ERRS = {}
    local w = KCD2MP.w143
    local farmer = mkCopy("ttkc_man_28", 901)
    local mark = #LOG
    KCD2MP_W143Provide("ttkc_man_28", HOE)
    local given = farmer.inventory:FindItem(HOE)
    check("T: a copy without the hoe gets one in its own inventory", given ~= nil and #farmer.inventory.list == 1, tostring(given))
    check("T: ...logged", logCount("WO143-TEMP give ttkc_man_28 " .. HOE .. " -> " .. tostring(given), mark) == 1)
    KCD2MP_W143Provide("ttkc_man_28", HOE)
    check("T: never twice (it owns one now)", #farmer.inventory.list == 1 and logCount("already owns", mark) == 1)
    local wood = mkCopy("ttkc_woodworker", 902)
    wood.inventory:CreateItem(SAW, 1, 1)
    KCD2MP_W143Provide("ttkc_woodworker", SAW)
    check("T: a copy that owns the tool gets nothing (and nothing of its is ever deleted)", #wood.inventory.list == 1 and (w.temps["ttkc_woodworker"] == nil or w.temps["ttkc_woodworker"][SAW] == nil))
    local av = mkCopy("kcd2mp_1", 903)
    local oldMod = mp_is_mod_entity
    mp_is_mod_entity = function(e) return e == av end
    KCD2MP_W143Provide("kcd2mp_1", HOE)
    mp_is_mod_entity = oldMod
    check("T: never for an avatar", #av.inventory.list == 0)
    -- the hoe in the left hand: still held -> "inhand" to the agent, nothing deleted
    farmer.hands[1] = given
    mark = #LOG
    KCD2MP_W143Release("ttkc_man_28", HOE, 2)
    check("T: still in the hand: not deleted", farmer.inventory:FindItem(HOE) == given)
    check("T: ...the agent hears inhand with the try count", emitted("w143", mark)[1] == "inhand ttkc_man_28 " .. HOE .. " 2", emitted("w143", mark)[1])
    -- out of the hand -> deleted, released to the agent
    farmer.hands[1] = nil
    mark = #LOG
    KCD2MP_W143Release("ttkc_man_28", HOE, 3)
    check("T: out of the hand: the temporary tool is deleted", farmer.inventory:FindItem(HOE) == nil and #farmer.inventory.list == 0)
    check("T: WO-153: DeleteItem is given its count (the engine logged a script error for each take without one)", #DELETE_COUNTS >= 1 and DELETE_COUNTS[#DELETE_COUNTS] == 1, tostring(DELETE_COUNTS[#DELETE_COUNTS]))
    check("T: ...the agent hears released", emitted("w143", mark)[1] == "released ttkc_man_28 " .. HOE)
    check("T: ...logged", logCount("WO143-TEMP take ttkc_man_28 " .. HOE, mark) == 1)
    mark = #LOG
    KCD2MP_W143Release("ttkc_man_28", HOE, 0)
    check("T: a second release of the same: nothing (it is gone)", #emitted("w143", mark) == 0)
    KCD2MP_W143Release("ttkc_woodworker", SAW, 0)
    check("T: a tool the copy owned is never released", #wood.inventory.list == 1)
    -- the end of the session: only those out of a hand
    local sweeper = mkCopy("ttkc_woman_3", 904)
    KCD2MP_W143Provide("ttkc_woman_3", "11111111-2222-3333-4444-555555555555")
    KCD2MP_W143Provide("ttkc_man_28", HOE)
    farmer.hands[1] = farmer.inventory:FindItem(HOE)
    mark = #LOG
    KCD2MP_W143ReleaseAll()
    check("T: take-all: the broom out of a hand goes, the hoe in a hand stays", #sweeper.inventory.list == 0 and #farmer.inventory.list == 1)
    check("T: ...one line with both counts", logCount("WO143-TEMP take-all: 1 deleted, 1 still in a hand", mark) == 1)
    noErrs("T")
end

-- ================================================================ K: looks

do
    ERRS = {}
    local w = KCD2MP.w143
    local guard = mkCopy("ttkc_man_5", 905)
    local avatar = mkCopy("kcd2mp_0", 906)
    local other = mkCopy("ttkc_inkeeper", 907)
    KCD2MP_W143Look("ttkc_man_5", "player")
    check("K: at this machine's player", guard.looking == player.id and w.looks["ttkc_man_5"] == "player")
    KCD2MP_W143Look("ttkc_man_5", "kcd2mp_0")
    check("K: at the host's avatar", guard.looking == avatar.id)
    KCD2MP_W143Look("ttkc_man_5", "ttkc_inkeeper")
    check("K: at another NPC's copy", guard.looking == other.id)
    KCD2MP_W143Look("ttkc_man_5", "")
    check("K: at nobody: the forced look is cleared", guard.looking == nil and guard.lookClears == 1 and w.looks["ttkc_man_5"] == nil)
    KCD2MP_W143Look("ttkc_man_5", "")
    check("K: ...only once", guard.lookClears == 1)
    KCD2MP_W143Look("ttkc_man_5", "nobody_here")
    check("K: a body this world does not have: nothing forced", guard.looking == nil and guard.lookSets == 3)
    KCD2MP_W143Look("ttkc_man_5", "ttkc_man_5")
    check("K: never at itself", guard.looking == nil)
    KCD2MP_W143Look("ttkc_man_5", "player")
    KCD2MP_SetIdles("off")
    check("K: mp_idles off clears every forced look", guard.looking == nil and next(w.looks) == nil)
    KCD2MP_W143Look("ttkc_man_5", "player")
    check("K: ...and forces none while off", guard.looking == nil)
    KCD2MP_SetIdles("on")
    KCD2MP_W143Look("no_such_copy", "player")
    check("K: a copy that is not here: nothing happens", #ERRS == 0)
    -- J3: a forced look on a paused copy (a puppet the host drives) swings its head and flashes its weapon
    KCD2MP.npcPuppets = KCD2MP.npcPuppets or {}
    KCD2MP_W143Look("ttkc_man_5", "player")
    check("K: (a free copy still looks)", guard.looking == player.id)
    KCD2MP.npcPuppets["ttkc_man_5"] = { eid = 905 }
    local skipped = w.stats.skipped or 0
    KCD2MP_W143Look("ttkc_man_5", "ttkc_inkeeper")
    check("K: a puppet is never forced to look (its forced look is cleared)", guard.looking == nil and w.looks["ttkc_man_5"] == nil)
    check("K: ...and the skip is counted", (w.stats.skipped or 0) == skipped + 1)
    KCD2MP.npcPuppets["ttkc_man_5"] = nil
    noErrs("K")
end

-- ================================================================ S: the status line

do
    ERRS = {}
    local mark = #LOG
    KCD2MP_W143Status()
    check("S: the status names the switches, the tools and the looks", logCount("WO143-STATUS hands=on gaits=on oneshots=on minigames=on idles=on | temporary tools 1", mark) == 1)
    check("S: ...and asks the agent for its own", emitted("w143", mark)[1] == "status")
    noErrs("S")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, string.char(10)) .. string.format(string.char(10) .. "%d passed, %d failed", pass, fail)
