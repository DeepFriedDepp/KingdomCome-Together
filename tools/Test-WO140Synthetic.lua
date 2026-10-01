-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-140 synthetic test: sleeping together, the Lua half, against the real kdcmp.lua
-- under MoonSharp (engine stubbed, fake clock; the stubs are Test-WO139Synthetic.lua's).
--   H  the bed: a lie-down in a session is held before it runs (the agent asked, the
--      waiting line); everyone's yes runs it unchanged; a no runs nothing (one line)
--   N  never held: sitting, someone else, the game's own refusal, no vote needed, the switch off
--   P  the prompt on the other player's screen: text, seconds, keys, yes / no to the agent
--   S  the own-world trap: the plain message, repeated, and off
--   K  the backstop, the held-too-long drop, getting up after a kept picker, the rest
--      save, the switch and the commands, the wrap re-installed after a reload
-- What this proves: the Lua half. Live evidence: docs/WO-140-findings.md.

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
-- the draw loop's rows: every System.DrawText of one frame
local DRAWN = {}
System.DrawText = function(x, y, text, size) DRAWN[#DRAWN + 1] = tostring(text) end
local function frame() DRAWN = {}; KCD2MP_DrawInteractionUI(); return table.concat(DRAWN, " | ") end
local function reset()
    local w = KCD2MP.w140
    w.on = false; w.vote = true; w.aliveAt = nil; w.held = nil; w.waiting = nil; w.prompt = nil; w.sep = nil
    BED_CALLS = {}; BED_CAN = true; SAVE_BEDS = {}; LAYING = false; INTERRUPTS = 0; RESTSAVES = 0
    ERRS = {}; TOASTS = {}; ENTS = { Dude = player }
end
local function session(on) KCD2MP_W140Session(on) end
local LYING = { esActionType = "Stance", sAction = "lying" }
local SITTING = { esActionType = "Stance", sAction = "sitting" }

-- ================================================================ H: the bed, held before the lie-down

do
    reset(); NOW = 10
    local bed = mkBed("BedTrigger9[Bed.bed_low41]", 101, 100)
    session(true)
    check("H: the session wraps BedTrigger.ReportUse", logCount("WO140-INSTALL BedTrigger.ReportUse wrapped") == 1)
    local mark = #LOG
    bed:ReportUse(player, {}, LYING)
    check("H: a lie-down in a session is held: the game's own ReportUse did not run", #BED_CALLS == 0, BED_CALLS[1])
    check("H: ...and the agent is asked (w140_ask sleep 0)", emitted("w140_ask", mark)[1] == "sleep 0", emitted("w140_ask", mark)[1])
    check("H: ...the hold is logged with the bed", logCount("WO140-HOLD bed=BedTrigger9[Bed.bed_low41] save=0", mark) == 1)
    KCD2MP_W140Waiting(true, "Waiting for other players...")
    check("H: \"Waiting for other players...\" on screen", frame():find("Waiting for other players...", 1, true) ~= nil)
    check("H: ...and as the game's own centered text", TOASTS[#TOASTS] == "Waiting for other players...", TOASTS[#TOASTS])
    mark = #LOG
    bed:ReportUse(player, {}, LYING)
    check("H: a second lie-down while one is held: nothing new asked", #emitted("w140_ask", mark) == 0 and #BED_CALLS == 0)
    KCD2MP_W140Go()
    check("H: everyone said yes: the held call runs unchanged (the lie-down, the picker)", #BED_CALLS == 1 and BED_CALLS[1] == "BedTrigger9[Bed.bed_low41]:lying", BED_CALLS[1])
    check("H: ...the waiting line is gone", frame():find("Waiting", 1, true) == nil)
    check("H: ...and nothing stays held", KCD2MP.w140.held == nil)
    noErrs("H")
end

do
    reset(); NOW = 30
    local bed = mkBed("BedTrigger12[Bed.bed_low21]", 101, 100)
    SAVE_BEDS[bed.id] = true
    session(true)
    local mark = #LOG
    bed:ReportUse(player, {}, LYING)
    check("H: a bed that saves: the ask says so (save 1)", emitted("w140_ask", mark)[1] == "sleep 1", emitted("w140_ask", mark)[1])
    KCD2MP_W140Drop("Other players are not ready to sleep yet!", false)
    check("H: a no: the lie-down never runs", #BED_CALLS == 0)
    check("H: ...one plain line", TOASTS[#TOASTS] == "Other players are not ready to sleep yet!", TOASTS[#TOASTS])
    check("H: ...logged: no sleep, no time skip, no rest", logCount("WO140-DROP bed=BedTrigger12[Bed.bed_low21] -- no sleep, no time skip, no rest") == 1)
    check("H: ...nothing held, nothing waiting", KCD2MP.w140.held == nil and KCD2MP.w140.waiting == nil)
    KCD2MP_W140Go()
    check("H: a late yes after the no runs nothing", #BED_CALLS == 0)
    noErrs("H2")
end

-- ================================================================ N: what is never held

do
    reset(); NOW = 50
    local bed = mkBed("BedTrigger9[Bed.bed_high3]", 101, 100)
    session(true)
    bed:ReportUse(player, {}, SITTING)
    check("N: sitting on a bed is not a sleep: runs at once", #BED_CALLS == 1 and BED_CALLS[1]:find(":sitting") ~= nil)
    local npc = { id = 77, GetName = function() return "tzel_man_1" end }
    bed:ReportUse(npc, {}, LYING)
    check("N: someone else lying down: runs at once", #BED_CALLS == 2)
    BED_CAN = false
    bed:ReportUse(player, {}, LYING)
    check("N: the game says the player can't sleep now: its own refusal runs (it says why)", #BED_CALLS == 3 and KCD2MP.w140.held == nil)
    BED_CAN = true
    session(false)
    bed:ReportUse(player, {}, LYING)
    check("N: no vote needed (solo, separate worlds): the lie-down runs at once", #BED_CALLS == 4 and KCD2MP.w140.held == nil)
    session(true)
    KCD2MP_SetSleepVote("off")
    bed:ReportUse(player, {}, LYING)
    check("N: mp_sleep_vote off: runs at once", #BED_CALLS == 5)
    KCD2MP_SetSleepVote("on")
    noErrs("N")
end

-- ================================================================ P: the prompt on the other player's screen

do
    reset(); NOW = 80
    session(true)
    local mark = #LOG
    KCD2MP_W140Prompt(16777217, "Moose wants to sleep. Sleep too?", 30)
    local f = frame()
    check("P: \"Moose wants to sleep. Sleep too?\" on screen, with the seconds left", f:find("Moose wants to sleep. Sleep too?  (30s)", 1, true) ~= nil, f)
    check("P: ...the keys", f:find("F11 yes / F12 no", 1, true) ~= nil)
    check("P: ...and as the game's own centered text", TOASTS[#TOASTS] == "Moose wants to sleep. Sleep too?")
    NOW = NOW + 12; session(true)   -- the agent's 1 s heartbeat (a silent agent clears the prompt: K)
    check("P: the seconds count down", frame():find("(18s)", 1, true) ~= nil)
    check("P: yes goes to the agent with the vote id", KCD2MP_W140Answer(true) == true and emitted("w140_answer", mark)[1] == "16777217 yes", emitted("w140_answer", mark)[1])
    check("P: ...and the prompt is gone", frame():find("Moose", 1, true) == nil)
    check("P: an answer with no prompt up does nothing", KCD2MP_W140Answer(false) == false and #emitted("w140_answer", mark) == 1)
    KCD2MP_W140Prompt(5, "Moose wants to wait. Wait too?", 30)
    KCD2MP_W140Answer(false)
    check("P: no goes to the agent too", emitted("w140_answer", mark)[2] == "5 no")
    KCD2MP_W140Prompt(6, "Moose wants to sleep. Sleep too?", 30)
    KCD2MP_W140PromptHide(6)
    check("P: the agent can take a prompt down (a timeout, a cancel)", KCD2MP.w140.prompt == nil)
    KCD2MP_W140Prompt(7, "Moose wants to sleep. Sleep too?", 30)
    NOW = NOW + 31; session(true)
    frame()
    check("P: past its seconds the prompt leaves the screen (the agent answers timeout)", KCD2MP.w140.prompt == nil)
    noErrs("P")
end

-- ================================================================ S: the own-world trap

do
    reset(); NOW = 200
    local text = "You loaded your own save. To play in your host's world, quit the game, start it again and wait at the main menu."
    KCD2MP_W140Separate(true, text)
    local f = frame()
    check("S: the plain message on screen", f:find(text, 1, true) ~= nil, f)
    check("S: ...with the line about separate worlds (the ghosts still show)", f:find("You and your host are in separate worlds right now.", 1, true) ~= nil)
    check("S: ...and as the game's own centered text", TOASTS[#TOASTS] == text)
    check("S: logged once", logCount("WO140-SEPARATE") == 1)
    KCD2MP_W140Separate(true, text)
    check("S: said again every few seconds (the agent re-sends): the centered text again, one log line", #TOASTS == 2 and logCount("WO140-SEPARATE") == 1)
    KCD2MP_W140Separate(false, "")
    check("S: off: gone from the screen", frame():find(text, 1, true) == nil and logCount("WO140-SEPARATE off") == 1)
    noErrs("S")
end

-- ================================================================ K: the backstop, the save, the switches

do
    reset(); NOW = 300
    local bed = mkBed("BedTrigger9[Bed.bed_low60]", 101, 100)
    session(true)
    bed:ReportUse(player, {}, LYING)
    KCD2MP_W140Prompt(9, "Moose wants to sleep. Sleep too?", 30)
    NOW = NOW + 5
    KCD2MP_W140Backstop()
    check("K: the agent silent inside its timeout: still held", KCD2MP.w140.held ~= nil)
    NOW = NOW + 6
    KCD2MP_W140Backstop()
    check("K: past it: dropped (nothing stays held), the prompt gone", KCD2MP.w140.held == nil and KCD2MP.w140.prompt == nil and #BED_CALLS == 0)
    check("K: ...with the plain line", TOASTS[#TOASTS] == "Other players are not ready to sleep yet!")
    session(true)
    bed:ReportUse(player, {}, LYING)
    for i = 1, 7 do NOW = NOW + 9; session(true) end
    check("K: a held bed nobody answered in 60 s is dropped by the session tick", KCD2MP.w140.held == nil and logCount("WO140-DROP a held bed got no answer") == 1)
    KCD2MP_W140Drop("Other players are not ready to sleep yet!", true)
    check("K: a kept picker refused: logged, and he is left as the game's own Back leaves him", logCount("WO140-DROP the kept picker (a sleep from a bed he lay on stays lying") == 1)
    KCD2MP_W140Waiting(true, "Waiting for other players...")
    NOW = NOW + 46; session(true)
    check("K: a waiting line is never left behind (gone after 45 s)", frame():find("Waiting", 1, true) == nil)
    KCD2MP_W140RestSave()
    check("K: the host's rest save is the game's own", RESTSAVES == 1 and logCount("WO140-RESTSAVE Game.SaveGameViaResting called") == 1)
    local mark = #LOG
    KCD2MP_SetSleepVote("off")
    check("K: mp_sleep_vote off reaches the agent", emitted("w140_cfg", mark)[1] == "vote=off")
    KCD2MP_SetSleepVote("on")
    check("K: ...and back on (the default)", KCD2MP.w140.vote == true)
    check("K: a bad value is refused", KCD2MP_SetSleepVote("maybe") == false and KCD2MP.w140.vote == true)
    KCD2MP_W140Status()
    check("K: the status line", logCount("WO140-STATUS session=true") == 1)
    check("K: mp_sleep_yes / mp_sleep_no / mp_sleep_vote / mp_sleep_status registered",
        CCMDS["mp_sleep_yes"] ~= nil and CCMDS["mp_sleep_yes"].body == "KCD2MP_W140Answer(true)"
        and CCMDS["mp_sleep_no"] ~= nil and CCMDS["mp_sleep_no"].body == "KCD2MP_W140Answer(false)"
        and CCMDS["mp_sleep_vote"] ~= nil and CCMDS["mp_sleep_vote"].body == "KCD2MP_SetSleepVote(%line)"
        and CCMDS["mp_sleep_status"] ~= nil and CCMDS["mp_sleep_status"].body == "KCD2MP_W140Status()")
    local w = KCD2MP.w140.wrapper
    KCD2MP_W140Install()
    check("K: the wrap is installed once (a second install keeps it)", BedTrigger.ReportUse == w)
    BedTrigger.ReportUse = function() end
    session(true)
    check("K: a script reload replaced it: the next session tick wraps again", BedTrigger.ReportUse == KCD2MP.w140.wrapper and logCount("WO140-INSTALL") >= 2)
    check("K: ships on", KCD2MP.w140.vote == true)
    mark = #LOG
    KCD2MP_Mark("sleep")
    check("K: mark_sleep writes one MP-MARK line and tells the agent", logCount("MP-MARK sleep world_time=", mark) == 1 and emitted("mp_mark", mark)[1] == "sleep")
    KCD2MP_Mark("bad word!")
    check("K: a marker is one word (no spaces or symbols reach the log)", emitted("mp_mark", mark)[2] == "badword")
    check("K: every checklist marker is a console command", (function()
        for _, w in ipairs(KCD2MP_MARKS) do
            local c = CCMDS["mark_" .. w]
            if not c or c.body ~= "KCD2MP_Mark('" .. w .. "')" then return false end
        end
        return #KCD2MP_MARKS >= 40
    end)())
    noErrs("K")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
