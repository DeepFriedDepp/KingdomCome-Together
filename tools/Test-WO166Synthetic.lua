-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-166 synthetic test (the game-side halves), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock).
--   (L) L1 never rewrite a corpse copy under an open loot screen: an update while the screen is open waits, is applied one frame
--       after the game's ItemTransfer OnClosed (or a fallback close); a refused take waits too; 30 open/update/close cycles
--   (T) T1 the request-fallback is suppressed in / just after a dialogue and while the player's own request is open; the player's
--       press ends a talk the mod attached; the WO-164 retry never fires for one of ours
--   (R) T3 resume first: a press on a paused copy resumes it and passes the press on 200 ms later; the stream holds a copy that is
--       asked (6 s) or talking; a never-started talk reports its facts
--   (B) T4 a copy whose host NPC is busy (the host's talk, a fight) is not asked; the player is told
--   (W) the switch mp_talk_resume_first, the status line
-- Driven by Test-WO166Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-166-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}; LISTENERS = {}

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
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.GetTerrainElevation = function(p) return 0 end
System.GetFrameTime = function() return 1 / 60 end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
UIAction.RegisterElementListener = function(tbl, el, inst, ev, fn) LISTENERS[#LISTENERS + 1] = { tbl = tbl, el = el, ev = ev, fn = fn } end
Calendar = mkstub()
Calendar.GetWorldTime = function() return 1000000 end
XGenAIModule = mkstub()

-- ---- items and inventories (the WO-134 suite's) ----
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
local function mkInventory()
    local inv = { list = {} }
    inv.GetInventoryTable = function(self) local t = {}; for i, w in ipairs(self.list) do t[i] = w end; return t end
    inv.CreateItem = function(self, cls, hp, amt) local w = newItem(cls, hp, amt); self.list[#self.list + 1] = w; return true end
    inv.AddItem = function(self, w) self.list[#self.list + 1] = w end
    inv.DeleteItem = function(self, w, n)
        for i, x in ipairs(self.list) do if x == w then table.remove(self.list, i); ITEMS[w] = nil; return true end end
        return false
    end
    inv.RemoveItem = function(self, w) for i, x in ipairs(self.list) do if x == w then table.remove(self.list, i); return w end end end
    inv.GetId = function() return "inv" end
    return inv
end

NEXTID = 5000
PX, PY, PZ = 100, 100, 10
IN_DIALOG = false
ORIG_TALKS = 0
LOOTS = {}
BasicAIActions = { OnTalk = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end }
function BasicAIActions:OnLoot(user, slot) LOOTS[#LOOTS + 1] = self:GetName() end
function BasicAIActions:OnPickpocketing(user, slot) LOOTS[#LOOTS + 1] = "pp:" .. self:GetName() end
player = { id = 1, class = "Player", inventory = mkInventory() }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PX, y = PY, z = PZ } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.GetDirectionVector = function() return { x = 1, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end, StandUp = function() end }
player.human = { IsInDialog = function() return IN_DIALOG end }
player.soul = { RestrictDialog = function() end, GetState = function() return 0 end }
ENTS["Dude"] = player

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return unpack(r)
end

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

-- @@KDCMP@@

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function lastLog(needle, fromLog)
    for i = #LOG, (fromLog or 0) + 1, -1 do if LOG[i]:find(needle, 1, true) then return LOG[i] end end
    return nil
end
local function countLog(needle, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do if LOG[i]:find(needle, 1, true) then n = n + 1 end end
    return n
end
local function countEvt(name, argPrefix, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " " .. (argPrefix or ""), 1, true) then n = n + 1 end
    end
    return n
end
local function cmdCount(prefix)
    local n = 0
    for _, c in ipairs(CMDS) do if c:sub(1, #prefix) == prefix then n = n + 1 end end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
local function runTimers()
    local t = TIMERS
    TIMERS = {}
    for _, x in ipairs(t) do x.f() end
end
local function classes(e)
    local o = {}
    for _, w in ipairs(e.inventory.list) do if ITEMS[w] then o[#o + 1] = ITEMS[w].class end end
    table.sort(o)
    return table.concat(o, ",")
end
local function fireClosed()
    for _, l in ipairs(LISTENERS) do
        if l.el == "ItemTransfer" and l.ev == "OnClosed" then l.tbl[l.fn](l.tbl, "ItemTransfer", -1, "OnClosed", {}) end
    end
end
local COAT = "a856e87a-8065-4338-919d-0aff7a63341d"
local MONEY = "5ef63059-322e-4e1b-abe8-926e100c770e"
local APPLE = "2264f217-590e-4c0f-a4c6-f50c6532b9f6"
local SWORD = "b0fc8e19-af72-4771-9517-caec4f568920"

local function world()
    for n in pairs(ENTS) do ENTS[n] = nil end
    ENTS["Dude"] = player
    PX, PY, PZ = 100, 100, 10
    player.inventory = mkInventory()
    KCD2MP.ghosts = {}; KCD2MP.npcPuppets = {}
    local w = KCD2MP.w134
    w.pendingOpen = {}; w.sessions = {}; w.partial = {}; w.stash = {}; w.itemReq = {}; w.hostTaken = {}; w.hostVerify = {}
    w.bodySig = {}; w.chestSess = {}
    w.bodies, w.items, w.chests = true, true, true
    local L = KCD2MP.w166.loot
    L.openOn = nil; L.deferred = {}; L.takebacks = {}
    TOASTS = {}; LOOTS = {}; TIMERS = {}
end
local function asJoiner()
    KCD2MP.hitSensorOn = false
    KCD2MP.wo102.authorityHost = true
    KCD2MP_W131Tick(true, true)
    KCD2MP_W134Tick(true, false, true, 1)
    TIMERS = {}
end

-- (L) L1 ---------------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    world(); asJoiner()
    local b = mkBody("tbuk_man_1", 101, 100, 10)
    b.inventory:CreateItem(COAT, 1, 1)
    BasicAIActions.OnLoot(b, player, 1)
    KCD2MP_W134BodyState("tbuk_man_1", "open", 1, 1, 1, { { COAT, 1, 1, false }, { SWORD, 1, 1, false } })
    check("L1: the loot screen opens on the host's items", #LOOTS == 1 and classes(b) == table.concat({ COAT, SWORD }, ","), classes(b))
    check("L2: the mod knows the screen is open on that body", KCD2MP_W166LootOpenOn("tbuk_man_1") == true and KCD2MP_W166LootOpenOn("other") == false)
    check("L3: the close listener was armed on ItemTransfer", #LISTENERS >= 1 and LISTENERS[1].el == "ItemTransfer" and LISTENERS[1].ev == "OnClosed")
    local mark = #LOG
    KCD2MP_W134BodyState("tbuk_man_1", "update", 1, 1, 1, { { COAT, 1, 1, false } })   -- the host took the sword meanwhile
    check("L4: a host update under the open screen waits: the copy is NOT rewritten", classes(b) == table.concat({ COAT, SWORD }, ",")
        and lastLog("WO166-LOOT deferred npc=tbuk_man_1 reason=update host_items=1", mark) ~= nil, classes(b))
    KCD2MP_W134BodyState("tbuk_man_1", "update", 1, 1, 1, { { COAT, 1, 1, false }, { APPLE, 1, 1, false } })
    check("L5: a second update replaces the waiting one (the newest list wins)", KCD2MP.w166.loot.deferred["tbuk_man_1"] ~= nil
        and #KCD2MP.w166.loot.deferred["tbuk_man_1"].list == 2 and classes(b) == table.concat({ COAT, SWORD }, ","))
    mark = #LOG
    fireClosed()
    check("L6: the close is seen; nothing applied in the same frame", KCD2MP.w166.loot.openOn == nil and classes(b) == table.concat({ COAT, SWORD }, ",") and #TIMERS == 1)
    runTimers()
    local want = { APPLE, COAT }; table.sort(want)
    check("L7: one frame later the host's list is applied", classes(b) == table.concat(want, ",")
        and lastLog("WO166-LOOT applied npc=tbuk_man_1 reason=update host_items=2", mark) ~= nil, classes(b))
    mark = #LOG
    KCD2MP_W134BodyState("tbuk_man_1", "update", 1, 1, 1, { { APPLE, 1, 1, false } })
    check("L8: with the screen closed an update is applied at once", classes(b) == APPLE and countLog("WO166-LOOT deferred", mark) == 0, classes(b))
    noErrs("L-a")
end
do
    ERRS = {}
    world(); asJoiner()
    local b = mkBody("tbuk_man_2", 101, 100, 10)
    BasicAIActions.OnLoot(b, player, 1)
    KCD2MP_W134BodyState("tbuk_man_2", "open", 1, 1, 1, { { SWORD, 1, 1, false } })
    -- the player takes the sword: the loop sends the take; the host says someone else had it
    local sw = b.inventory.list[1]
    b.inventory:RemoveItem(sw); player.inventory:AddItem(sw)
    NOW = NOW + 0.25; KCD2MP.w134.loopOnce(); TIMERS = {}
    local tok = nil
    for _, l in ipairs(LOG) do tok = tok or l:match("w134_take (%d+) tbuk_man_2") end
    local mark = #LOG
    KCD2MP_W134TakeResult(tok, "gone", "tbuk_man_2")
    check("L9: a refused take under the open screen is NOT taken back off Henry yet", #player.inventory.list == 1
        and lastLog("WO166-LOOT takeback-deferred npc=tbuk_man_2", mark) ~= nil, tostring(tok))
    fireClosed(); runTimers()
    check("L10: after the close it is taken back, and the player told", #player.inventory.list == 0 and TOASTS[#TOASTS] == "Someone already took that."
        and lastLog("WO166-LOOT takeback-applied npc=tbuk_man_2", mark) ~= nil)
    noErrs("L-b")
end
do
    ERRS = {}
    world(); asJoiner()
    local b = mkBody("tbuk_man_3", 101, 100, 10)
    BasicAIActions.OnLoot(b, player, 1)
    KCD2MP_W134BodyState("tbuk_man_3", "open", 1, 1, 1, { { SWORD, 1, 1, false } })
    PX = 110   -- nobody walks with a menu open: the screen is closed
    local mark = #LOG
    KCD2MP_W134BodyState("tbuk_man_3", "update", 1, 1, 1, { { COAT, 1, 1, false } })
    runTimers()
    check("L11: the 5 m rule closes a screen whose close was never seen", KCD2MP.w166.loot.openOn == nil and classes(b) == COAT
        and countLog("WO166-LOOT deferred", mark) == 0, classes(b))
    world(); asJoiner()
    b = mkBody("tbuk_man_4", 101, 100, 10)
    BasicAIActions.OnLoot(b, player, 1)
    KCD2MP_W134BodyState("tbuk_man_4", "open", 1, 1, 1, { { SWORD, 1, 1, false } })
    mark = #LOG
    KCD2MP_W166LootClosed("audio")   -- the agent's ui_inv_screen_out line
    runTimers()
    check("L12: the agent's close line closes it too", KCD2MP.w166.loot.openOn == nil)
    noErrs("L-c")
end
do
    ERRS = {}
    world(); asJoiner()
    local b = mkBody("tbuk_zibrid", 101, 100, 10)
    local bad = 0
    for i = 1, 30 do
        BasicAIActions.OnLoot(b, player, 1)
        KCD2MP_W134BodyState("tbuk_zibrid", "open", 1, 1, 1, { { SWORD, 1, 1, false }, { COAT, 1, 1, false } })
        local before = classes(b)
        KCD2MP_W134BodyState("tbuk_zibrid", "update", 1, 1, 1, { { COAT, 1, 1, false } })
        if classes(b) ~= before then bad = bad + 1 end
        fireClosed(); runTimers()
        if classes(b) ~= COAT then bad = bad + 1 end
        NOW = NOW + 1
        KCD2MP_W131Tick(true, true); KCD2MP_W134Tick(true, false, true, 1); TIMERS = {}   -- the agent's ticks, once a second
    end
    check("L13: 30 open / update / close cycles: never rewritten under the screen, always applied after", bad == 0, bad)
    noErrs("L-d")
end

-- (T) T1 ---------------------------------------------------------------------------------------------------------------------
local function copy(name, x, y)
    NEXTID = NEXTID + 1
    local e = { id = NEXTID, class = "NPC", GetName = function() return name end, actor = { IsDead = function() return false end },
                human = { IsInDialog = function() return false end, IsWeaponDrawn = function() return false end, InterruptDialogs = function() end },
                soul = { RestrictDialog = function() end } }
    e.GetWorldPos = function() return { x = x, y = y, z = PZ } end
    ENTS[name] = e
    KCD2MP.npcPuppets[name] = {}
    return e
end
local function joiner()
    KCD2MP.hitSensorOn = false
    KCD2MP.wo102.authorityHost = true; KCD2MP.wo102.authorityPause = true
    KCD2MP.w137.joiner, KCD2MP.w137.active, KCD2MP.w137.host, KCD2MP.w137.sync, KCD2MP.w137.talkOn = true, true, false, true, true
    KCD2MP.w137.aliveAt = NOW
    pcall(KCD2MP_W137InstallTalk)
end
local function resetTalk()
    KCD2MP.w137.talking = {}
    KCD2MP.w166.talk.lastDlgEndAt = nil
    KCD2MP.w137.ownTalkAt = nil
    if KCD2MP.w164 then KCD2MP.w164.lastEnd = {}; KCD2MP.w164.hostCombat = {} end
    if KCD2MP.w160 then KCD2MP.w160.conv = {} end
    IN_DIALOG = false
end
do
    ERRS = {}
    world(); joiner(); resetTalk()
    copy("tzel_vavrinec", 101, 100)
    KCD2MP._npcPaused["tzel_vavrinec"] = NOW
    IN_DIALOG = true
    local mark = #LOG
    KCD2MP_W137TalkRequest(2058)
    check("T1: no request-fallback while the player is in a dialogue", KCD2MP.w137.talking["tzel_vavrinec"] == nil
        and lastLog("WO166-TALK own-request suppressed via=request-fallback npc=tzel_vavrinec id=2058 why=in-dialogue", mark) ~= nil)
    IN_DIALOG = false
    KCD2MP_W137DialogEnd(2039, "Dude tzel_woman_11")
    NOW = NOW + 1
    mark = #LOG
    KCD2MP_W137TalkRequest(2059)
    check("T2: ... nor within 3 s after his dialogue ended", KCD2MP.w137.talking["tzel_vavrinec"] == nil
        and lastLog("why=after-dialogue", mark) ~= nil)
    NOW = NOW + 3
    KCD2MP.w137.talking["tzel_woman_11"] = { since = NOW, via = "OnTalk", resumed = false, id = 2050 }
    mark = #LOG
    KCD2MP_W137TalkRequest(2060)
    check("T3: ... nor while his own request is open", KCD2MP.w137.talking["tzel_vavrinec"] == nil and lastLog("why=player-request-open", mark) ~= nil)
    KCD2MP.w137.talking = {}
    KCD2MP_W137TalkRequest(2061)
    check("T4: otherwise the fallback still attaches (a press that did not come through the wrapped actions)",
        KCD2MP.w137.talking["tzel_vavrinec"] ~= nil and KCD2MP.w137.talking["tzel_vavrinec"].via == "request-fallback")
    -- the WO-164 retry never fires for it
    NOW = NOW + 5
    local rbefore = (KCD2MP.w164.stats.retries or 0)
    pcall(KCD2MP_W164Frame)
    check("T5: the WO-164 retry never fires for a talk the mod attached", (KCD2MP.w164.stats.retries or 0) == rbefore)
    -- the player presses talk on another copy: the attached one ends first
    copy("tzel_woman_9", 102, 100)
    mark = #LOG
    local before = ORIG_TALKS
    BasicAIActions.OnTalk(ENTS["tzel_woman_9"], player, 1)
    runTimers()
    check("T6: the player's own press ends the talk the mod attached", KCD2MP.w137.talking["tzel_vavrinec"] == nil
        and lastLog("WO166-TALK own-request cancelled npc=tzel_vavrinec for=tzel_woman_9", mark) ~= nil and ORIG_TALKS == before + 1, ORIG_TALKS - before)
    noErrs("T")
end

-- (R) T3 ---------------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    world(); joiner(); resetTalk()
    copy("ttac_blacksmith", 101, 100)
    KCD2MP._npcPaused["ttac_blacksmith"] = NOW
    CMDS = {}
    local before = ORIG_TALKS
    local mark = #LOG
    BasicAIActions.OnTalk(ENTS["ttac_blacksmith"], player, 1)
    check("R1: a press on a paused copy resumes it first", cmdCount("wh_ai_ResumeNPC ttac_blacksmith") == 1 and KCD2MP.w137.talking["ttac_blacksmith"] ~= nil
        and lastLog("WO166-TALK resume-first npc=ttac_blacksmith", mark) ~= nil)
    check("R2: ... and the press is not passed on in the same frame", ORIG_TALKS == before)
    local delayed = nil
    for _, t in ipairs(TIMERS) do if t.ms == 200 then delayed = t end end
    check("R3: it waits 200 ms for the copy's brain", delayed ~= nil)
    runTimers()
    check("R4: then the game's own talk runs once", ORIG_TALKS == before + 1, ORIG_TALKS - before)
    -- the stream hold
    local holds = 0
    KCD2MP_NpcNativeHold = function(n, s) holds = holds + 1 end
    local p, e = KCD2MP.npcPuppets["ttac_blacksmith"], ENTS["ttac_blacksmith"]
    check("R5: the stream does not move a copy that is being asked", KCD2MP_W166TalkHeld("ttac_blacksmith", p, e) == true and holds == 1)
    NOW = NOW + 0.5
    KCD2MP_W166TalkHeld("ttac_blacksmith", p, e)
    check("R6: the native hold is renewed once a second, not every frame", holds == 1)
    NOW = NOW + 6
    check("R7: a request that has not become a conversation in 6 s is no longer held", KCD2MP_W166TalkHeld("ttac_blacksmith", p, e) == false)
    KCD2MP.w137.talking["ttac_blacksmith"].started = true
    check("R8: a conversation that runs is held", KCD2MP_W166TalkHeld("ttac_blacksmith", p, e) == true)
    KCD2MP_W166SetTalkResumeFirst("off")
    check("R9: mp_talk_resume_first off: no hold", KCD2MP_W166TalkHeld("ttac_blacksmith", p, e) == false)
    KCD2MP_W166SetTalkResumeFirst("on")
    -- the timeout facts
    KCD2MP.w137.talking["ttac_blacksmith"].started = false
    mark = #LOG
    KCD2MP_W137TalkEnd("ttac_blacksmith", "never-started")
    check("R10: a never-started talk reports the copy's facts to the agent", countEvt("w166_talkstate", "ttac_blacksmith paused=0 copy_dialog=0 dead=0 resumed_for_talk=1", mark) == 1,
        lastLog("w166_talkstate", mark))
    -- an unpaused copy: the press goes straight through
    resetTalk()
    copy("ttac_woman_1", 101, 101)
    before = ORIG_TALKS
    BasicAIActions.OnTalk(ENTS["ttac_woman_1"], player, 1)
    check("R11: a copy that is not paused is asked at once", ORIG_TALKS == before + 1)
    noErrs("R")
end

-- (B) T4 ---------------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    world(); joiner(); resetTalk()
    copy("ttkc_smith", 101, 100)
    KCD2MP.w160.conv["ttkc_smith"] = { since = NOW, holdAt = NOW }
    local before = ORIG_TALKS
    local mark = #LOG
    BasicAIActions.OnTalk(ENTS["ttkc_smith"], player, 1)
    runTimers()
    check("B1: the host's own talk with that NPC: no request, the player told", ORIG_TALKS == before and TOASTS[#TOASTS] == "They're busy with your partner."
        and lastLog("WO166-TALK busy npc=ttkc_smith why=host-talking", mark) ~= nil and KCD2MP.w137.talking["ttkc_smith"] == nil)
    KCD2MP.w160.conv = {}
    KCD2MP.w164.hostCombat["ttkc_smith"] = NOW
    mark = #LOG
    BasicAIActions.OnTalk(ENTS["ttkc_smith"], player, 1)
    check("B2: the host's NPC in a fight: busy too", ORIG_TALKS == before and lastLog("why=host-fighting", mark) ~= nil)
    NOW = NOW + 4
    BasicAIActions.OnTalk(ENTS["ttkc_smith"], player, 1)
    runTimers()
    check("B3: the fight 3 s over: asked", ORIG_TALKS == before + 1)
    noErrs("B")
end

-- (W) the switch and the status ----------------------------------------------------------------------------------------------
do
    ERRS = {}
    check("W1: the switch and the status command are registered", CCMDS["mp_talk_resume_first"] ~= nil and CCMDS["mp_talk_resume_first"].body == "KCD2MP_W166SetTalkResumeFirst(%line)"
        and CCMDS["mp_w166_status"] ~= nil)
    check("W2: the defaults: resume first on", KCD2MP.w166.talkResumeFirst == true)
    check("W3: a bad word changes nothing", KCD2MP_W166SetTalkResumeFirst("maybe") == false and KCD2MP.w166.talkResumeFirst == true)
    local mark = #LOG
    KCD2MP_W166Status()
    check("W4: the status line", lastLog("WO166-STATUS loot open=", mark) ~= nil)
    noErrs("W")
end

OUT = table.concat(RESULTS, "\n")
