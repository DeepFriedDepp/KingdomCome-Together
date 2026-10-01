-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-141 synthetic test: activities and animal attacks, the Lua half, against the real
-- kdcmp.lua under MoonSharp (engine stubbed, fake clock; the stubs are Test-WO140Synthetic.lua's).
--   A  the switches: mp_activities / mp_animal_attacks, their lines, the agent's sync
--   W  the player's one-shots: the trough's WashFace reaches the agent (w141 anim) with its
--      object; the game's own call always runs; nobody else's, no other action, no link,
--      the switch off: nothing; the wrap is installed once per class and again after a reload
--   L  the avatar's nameplate key (the ghost tables' string id)
--   S  the status line
-- What this proves: the Lua half. Live evidence: docs/WO-141-findings.md.

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
local WASH = { esActionType = "Animation", sAction = "WashFace" }
local SIT = { esActionType = "Stance", sAction = "sitting" }
local TROUGH = "SmartObjectHolder6[WaterTub.WaterTub3_554b41ef-bbe9-47f2-8a38-9a851785cfcc]"

-- ================================================================ A: the switches

do
    ERRS = {}
    local w = KCD2MP.w141
    check("A: activities default on", w.on == true)
    check("A: animal attacks default on", w.bites == true)
    check("A: the commands are registered", CCMDS["mp_activities"] ~= nil and CCMDS["mp_animal_attacks"] ~= nil and CCMDS["mp_activity_status"] ~= nil)
    local mark = #LOG
    check("A: mp_activities off", KCD2MP_SetActivities("off") == true and w.on == false)
    check("A: ...tells the agent (w141 off)", emitted("w141", mark)[1] == "off", emitted("w141", mark)[1])
    check("A: ...and says what it means", logCount("WO141-ACTIVITIES off -- bodies stand where they are", mark) == 1)
    mark = #LOG
    check("A: mp_activities on", KCD2MP_SetActivities("on") == true and w.on == true and emitted("w141", mark)[1] == "on")
    check("A: a bad argument changes nothing", KCD2MP_SetActivities("maybe") == false and w.on == true)
    mark = #LOG
    check("A: mp_animal_attacks off", KCD2MP_SetAnimalAttacks("off") == true and w.bites == false and emitted("w141", mark)[1] == "bites off")
    mark = #LOG
    check("A: mp_animal_attacks on", KCD2MP_SetAnimalAttacks("on") == true and w.bites == true and emitted("w141", mark)[1] == "bites on")
    mark = #LOG
    KCD2MP_W141Sync(false, true)
    check("A: the agent's sync: an agent that has it off hears on", emitted("w141", mark)[1] == "on" and #emitted("w141", mark) == 1, #emitted("w141", mark))
    mark = #LOG
    KCD2MP_W141Sync(true, true)
    check("A: ...and in step: nothing", #emitted("w141", mark) == 0)
    noErrs("A")
end

-- ================================================================ W: the player's one-shots

do
    ERRS = {}; TRIGGER_CALLS = {}
    check("W: the sync wrapped both trigger classes once", logCount("WO141-INSTALL WaterTubeActionTrigger.ReportUse wrapped") == 1 and logCount("WO141-INSTALL ActionTrigger.ReportUse wrapped") == 1)
    local trough = mkTrigger("WaterTubeActionTrigger", "ActionTrigger1[WaterTub.WaterTub3_554b41ef-bbe9-47f2-8a38-9a851785cfcc]", TROUGH, WASH)
    local bench = mkTrigger("ActionTrigger", "ActionTrigger[Bench/bench_1place_low10]", "smartObject[Bench/bench_1place_low10]", SIT)
    local mark = #LOG
    trough:ReportUse(player, {}, WASH)
    check("W: the game's own call runs (the wash)", TRIGGER_CALLS[1] == trough:GetName() .. ":WashFace", TRIGGER_CALLS[1])
    check("W: ...and the agent hears the action at its object", emitted("w141", mark)[1] == "anim WashFace " .. TROUGH, emitted("w141", mark)[1])
    check("W: ...logged", logCount("WO141-ANIM WashFace at " .. TROUGH, mark) == 1)
    mark = #LOG
    local npc = { id = 77, GetName = function() return "tzel_woman_2" end }
    trough:ReportUse(npc, {}, WASH)
    check("W: someone else's use: the game's call runs, nothing is sent", #TRIGGER_CALLS == 2 and #emitted("w141", mark) == 0)
    mark = #LOG
    bench:ReportUse(player, {}, SIT)
    check("W: a stance action (the bench): the game's call runs, nothing is sent (the DLL reads a stance)", #TRIGGER_CALLS == 3 and #emitted("w141", mark) == 0)
    local lonely = mkTrigger("ActionTrigger", "ActionTrigger7[nothing]", nil, WASH)
    mark = #LOG
    lonely:ReportUse(player, {}, WASH)
    check("W: no linked object: nothing is sent", #TRIGGER_CALLS == 4 and #emitted("w141", mark) == 0)
    KCD2MP_SetActivities("off")
    mark = #LOG
    trough:ReportUse(player, {}, WASH)
    check("W: mp_activities off: the wash runs, nothing is sent", #TRIGGER_CALLS == 5 and #emitted("w141", mark) == 0)
    KCD2MP_SetActivities("on")
    mark = #LOG
    KCD2MP_W141Sync(true, true)
    check("W: the next sync does not wrap twice", logCount("WO141-INSTALL", mark) == 0)
    -- a script reload replaces the class function: the next sync wraps the new one
    function WaterTubeActionTrigger:ReportUse(user, item, action) TRIGGER_CALLS[#TRIGGER_CALLS + 1] = "reloaded:" .. tostring(action.sAction) end
    mark = #LOG
    KCD2MP_W141Sync(true, true)
    check("W: after a reload the sync wraps the new function", logCount("WO141-INSTALL WaterTubeActionTrigger.ReportUse wrapped", mark) == 1 and logCount("WO141-INSTALL ActionTrigger.ReportUse", mark) == 0)
    mark = #LOG
    trough:ReportUse(player, {}, WASH)
    check("W: ...which runs and is heard", TRIGGER_CALLS[#TRIGGER_CALLS] == "reloaded:WashFace" and emitted("w141", mark)[1] == "anim WashFace " .. TROUGH)
    noErrs("W")
end

-- ================================================================ L: the nameplate key

do
    ERRS = {}
    local w = KCD2MP.w141
    KCD2MP_W141AvatarStance(0, 3)
    check("L: stored under the ghost id as KCD2MP.ghosts has it (the string '0')", w.avatarStance["0"] == 3 and w.avatarStance[0] == nil, tostring(w.avatarStance["0"]))
    KCD2MP_W141AvatarStance("2", 1)
    check("L: a string id too", w.avatarStance["2"] == 1)
    KCD2MP_W141AvatarStance(0, 0)
    check("L: standing again: 0 (the stream's point)", w.avatarStance["0"] == 0)
    KCD2MP_W141AvatarStance("x", 3)
    check("L: a bad id stores nothing", w.avatarStance["x"] == nil)
    noErrs("L")
end

-- ================================================================ S: the status line

do
    ERRS = {}
    local mark = #LOG
    KCD2MP_W141Status()
    check("S: the status names the switches and the wraps", logCount("WO141-STATUS activities=on animal_attacks=on wrapped WaterTubeActionTrigger=true ActionTrigger=true", mark) == 1)
    check("S: ...and asks the agent for its own", emitted("w141", mark)[1] == "status")
    noErrs("S")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, string.char(10)) .. string.format(string.char(10) .. "%d passed, %d failed", pass, fail)
