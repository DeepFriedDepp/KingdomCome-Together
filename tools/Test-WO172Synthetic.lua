-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-172 synthetic test (the game-side halves), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock).
--   (D) C1 a copy the host's death reached is told to the agent once and its talk / scene hold end; a revived body is told too
--   (R) R1 this player's mount answer every 2 s; a partner's figure mounted (dismounted) again after two checks, 5 tries; a horse
--       under the terrain put on it (the field: z -13.4 under a host at 31.8)
--   (F) FT1/UI1 the 60 s restore keeps the black lifted; a load gives it back; a refused quest travel's black lifted after the leash
--   (H) H1 a trader held from the dialogue's end until the trade screen closes (<= 1 s) or the player walks away; a cancelled haggle
--   (P) P1 the joiner's pickpocket: the game's minigame on the copy, every take asked of the host, 20 tries without duplication
--   (W) the switches, the status line and the agent's re-sync
-- Driven by Test-WO172Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-172-findings.md.
NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}; LISTENERS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
CVARS = {}
System.GetCVar = function(n) return CVARS[n] ~= nil and tostring(CVARS[n]) or "0" end
System.SetCVar = function(n, v) CVARS[n] = v end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function(p, r) return {} end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.RemoveEntity = function(eid) end
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
MSGS = {}; ENDCOMBAT = {}
XGenAIModule.MakeTableFromType = function(t) return { kind = t } end
XGenAIModule.SendMessageToEntityData = function(to, kind, t) MSGS[#MSGS + 1] = { to = to, kind = kind, t = t } end
XGenAIModule.TryEndCombat = function(w) ENDCOMBAT[#ENDCOMBAT + 1] = w; return true end
EnvironmentModule = { GetRainIntensity = function() return 0 end }
ItemManager = mkstub()

NEXTID = 5000
PX, PY, PZ = 100, 100, 10
IN_DIALOG = false
ORIG_TALKS = 0
P_DANGER = false
BasicAIActions = { OnTalk = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end,
                   OnChatWithFocus = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end }
player = { id = 1, class = "Player" }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PX, y = PY, z = PZ } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.GetDirectionVector = function() return { x = 1, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end, StandUp = function() end }
player.human = { IsInDialog = function() return IN_DIALOG end }
player.soul = { RestrictDialog = function() end, GetState = function() return 0 end,
                IsInCombatDanger = function() return P_DANGER end, IsInTenseCircumstance = function() return false end }
ENTS["Dude"] = player

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return unpack(r)
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
local function lastEvt(name, fromLog)
    for i = #LOG, (fromLog or 0) + 1, -1 do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " ", 1, true) then return l end
    end
    return nil
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

-- a host copy: a puppet the host streams (soul / actor / human binds as the game has them; flags settable)
local function copy(name, x, y, opts)
    opts = opts or {}
    NEXTID = NEXTID + 1
    local e = { id = NEXTID, class = "NPC", this = { id = "wuid" .. name } }
    e.GetName = function() return name end
    e.GetWorldPos = function() return { x = x, y = y, z = PZ } end
    e.actor = { IsDead = function() return false end, CanTalk = function() return opts.canTalk ~= false end }
    e.human = { IsInDialog = function() return opts.inDialog == true end, IsWeaponDrawn = function() return false end,
                InterruptDialogs = function() opts.interrupted = (opts.interrupted or 0) + 1 end }
    e.soul = { RestrictDialog = function() end,
               IsInCombatMode = function() return opts.combat == true end,
               IsInCombatDanger = function() return opts.danger == true end,
               IsDialogRestricted = function() return opts.restricted == true end,
               HasScriptContext = function(_, c) return opts.ctx ~= nil and opts.ctx[c] == true end }
    -- the game's own prompt rules (BasicAIActions:ActorCanTalk / GetCanTalkHintType) are methods of every NPC entity
    e.ActorCanTalk = function(self, user) return self.actor:CanTalk(user.id) and not self.soul:IsInCombatMode() end
    e.GetCanTalkHintType = function(self)
        if self.soul:IsDialogRestricted(player.id) then return false end
        if player.soul:IsInCombatDanger() or player.soul:IsInTenseCircumstance() then return false end
        return true
    end
    e.opts = opts
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
local function host()
    KCD2MP.w137.joiner, KCD2MP.w137.active, KCD2MP.w137.host = false, true, true
end
local function reset()
    for n in pairs(ENTS) do ENTS[n] = nil end
    ENTS["Dude"] = player
    PX, PY, PZ = 100, 100, 10
    KCD2MP.npcPuppets = {}
    KCD2MP._npcPaused = {}
    KCD2MP.w137.talking = {}
    KCD2MP.w137.ownTalkAt = nil
    KCD2MP.w166.talk.lastDlgEndAt = nil
    KCD2MP.w164.lastEnd = {}; KCD2MP.w164.hostCombat = {}; KCD2MP.w164.sweptAt = {}
    KCD2MP.w160.conv = {}
    local W = KCD2MP.w167
    W.talkFallback = false
    W.talk.lastNpc = {}; W.talk.fresh = {}; W.talk.focus = nil; W.talk.nohookAt = {}; W.talk.fbLogAt = -1e9
    W.scene.faderHeldAt = nil
    IN_DIALOG = false; P_DANGER = false
    TOASTS = {}; TIMERS = {}; CMDS = {}; MSGS = {}; ENDCOMBAT = {}; CVARS = {}
end


-- inventories: a list of { w, cls, amt, hp } per entity; ItemManager reads them
ITEMS = {}
local NEXTW = 900000
ItemManager.GetItem = function(w) return ITEMS[w] end
local function inventory(list)
    local inv = { list = list or {} }
    inv.GetInventoryTable = function(self) local t = {}; for i, w in ipairs(self.list) do t[i] = w end; return t end
    inv.DeleteItem = function(self, w, n) for i, x in ipairs(self.list) do if x == w then table.remove(self.list, i); ITEMS[w] = nil; return true end end; return false end
    inv.CreateItem = function(self, cls, hp, amt) NEXTW = NEXTW + 1; ITEMS[NEXTW] = { class = cls, amount = amt or 1, health = hp or 1 }; self.list[#self.list + 1] = NEXTW; return NEXTW end
    inv.GetMoney = function() return 0 end
    return inv
end
local function give(e, cls, amt) return e.inventory:CreateItem(cls, 1, amt or 1) end
local function countCls(e, cls) local n = 0; for _, w in ipairs(e.inventory.list) do if ITEMS[w] and ITEMS[w].class == cls then n = n + (ITEMS[w].amount or 1) end end; return n end
player.inventory = inventory({})
player.human.IsMounted = function() return MOUNTED == true end
MOUNTED = false

local function trader(name, x, y, isShop)
    local e = copy(name, x, y, { ctx = isShop and { shop_sellerReadyToSell = true } or {} })
    e.inventory = inventory({})
    return e
end

-- (D) C1: a copy the host's death reached is told once and never worked again --------------------------------------------------
do
    ERRS = {}
    reset(); joiner()
    local mark = #LOG
    local e = copy("prepadeniNaCeste_banditLeader_6", 101, 100)
    KCD2MP.w137.talking["prepadeniNaCeste_banditLeader_6"] = { since = NOW, via = "test", started = true, holdSent = true }
    KCD2MP.w170.held["prepadeniNaCeste_banditLeader_6"] = { since = NOW }
    check("D1: a death applied from the host marks the copy for the agent (w172_dead)", KCD2MP_W172CopyDied("prepadeniNaCeste_banditLeader_6", "owner-death") == true
        and countEvt("w172_dead", "prepadeniNaCeste_banditLeader_6", mark) == 1)
    check("D2: its talk ends and its scene hold is dropped (no hold kept on a corpse)", KCD2MP.w137.talking["prepadeniNaCeste_banditLeader_6"] == nil
        and KCD2MP.w170.held["prepadeniNaCeste_banditLeader_6"] == nil and countEvt("w137_talk", "off prepadeniNaCeste_banditLeader_6", mark) == 1)
    check("D3: the line says the host's death wins", lastLog("WO172-DEAD npc=prepadeniNaCeste_banditLeader_6 via=owner-death", mark) ~= nil)
    mark = #LOG
    check("D4: a body that reads alive again (a reload) is told too", KCD2MP_W172CopyAlive("prepadeniNaCeste_banditLeader_6") == true
        and countEvt("w172_alive", "prepadeniNaCeste_banditLeader_6", mark) == 1)
    KCD2MP.w172.deadWins = false
    mark = #LOG
    check("D5: mp_dead_wins off: nothing is told", KCD2MP_W172CopyDied("tvez_man_13", "x") == false and countEvt("w172_dead", nil, mark) == 0)
    KCD2MP.w172.deadWins = true
    check("D6: a bad name is refused", KCD2MP_W172CopyDied("bad name;", "x") == false and KCD2MP_W172CopyDied(nil, "x") == false)
    noErrs("D")
end

-- (R) R1: the mount answer, the figure check, the horse under the ground -------------------------------------------------------
do
    ERRS = {}
    reset()
    local W = KCD2MP.w172
    W.mountSent, W.mountAt, W.rideAt, W.tickAt = nil, -1e9, -1e9, -1e9
    local mark = #LOG
    MOUNTED = true
    NOW = NOW + 1; KCD2MP_W172Tick()
    check("R1: this player's own mount goes to the agent (w172_mount 1)", countEvt("w172_mount", "1", mark) == 1)
    NOW = NOW + 0.5; KCD2MP_W172Tick()
    check("R2: not again within 2 s", countEvt("w172_mount", "1", mark) == 1)
    NOW = NOW + 2.0; KCD2MP_W172Tick()
    check("R3: again every 2 s while mounted (the riding bit survives the engine clearing its horse tag)", countEvt("w172_mount", "1", mark) == 2)
    MOUNTED = false
    NOW = NOW + 0.3; KCD2MP_W172Tick()
    check("R4: the dismount at once (w172_mount 0)", countEvt("w172_mount", "0", mark) == 1)
    -- the pure rule
    local a, m, x = KCD2MP_W172RideRule(true, false, 0, 0, 2)
    local b, m2, x2 = KCD2MP_W172RideRule(true, false, m, x, 2)
    check("R5: riding in the stream but not mounted: mounted again on the 2nd check (4 s), not the 1st", a == nil and m == 1 and b == "mount" and m2 == 0)
    local c = KCD2MP_W172RideRule(false, true, 0, 1, 2)
    check("R6: off the horse in the stream but still mounted: dismounted on the 2nd check", c == "dismount")
    check("R7: unknown mount state or agreement: nothing", KCD2MP_W172RideRule(true, nil, 1, 0, 2) == nil and KCD2MP_W172RideRule(true, true, 1, 0, 2) == nil)
    -- the figure check against a ghost
    local spawns, dismounts = 0, 0
    local origSpawn, origDis, origRem, origMounted = KCD2MP_SpawnHorse, KCD2MP_GhostDismount, KCD2MP_RemoveHorse, KCD2MP_GhostIsMounted
    KCD2MP_SpawnHorse = function() spawns = spawns + 1 end
    KCD2MP_GhostDismount = function() dismounts = dismounts + 1 end
    KCD2MP_RemoveHorse = function() end
    FIG_MOUNTED = false
    KCD2MP_GhostIsMounted = function() return FIG_MOUNTED end
    local fig = { GetWorldPos = function() return { x = 5, y = 5, z = 40 } end }
    KCD2MP.ghosts = { [2] = { entity = fig, istate = { isRiding = true, rotZ = 0 } } }
    W.ride = {}
    mark = #LOG
    for i = 1, 4 do KCD2MP_W172RideCheck(NOW + i * 2) end
    check("R8: the field's case (the partner rides, his figure is not on a horse): the riding start again, every 4 s", spawns == 2
        and countLog("WO172-RIDE remount id=2", mark) == 2)
    for i = 5, 20 do KCD2MP_W172RideCheck(NOW + i * 2) end
    check("R9: at most 5 tries, then given up (said once) until the rider mounts or gets off again", spawns == 5 and countLog("WO172-RIDE gave-up id=2", mark) == 1)
    KCD2MP.ghosts[2].istate.isRiding = false; FIG_MOUNTED = true
    for i = 21, 24 do KCD2MP_W172RideCheck(NOW + i * 2) end
    check("R10: a new state starts new tries: off in the stream, still mounted here -> dismounted", dismounts == 2 and countLog("WO172-RIDE dismount id=2", mark) == 2)
    W.rideRepair = false
    spawns = 0; KCD2MP.ghosts[2].istate.isRiding = true; FIG_MOUNTED = false
    for i = 25, 30 do KCD2MP_W172RideCheck(NOW + i * 2) end
    check("R11: mp_ride_repair off: the check does nothing", spawns == 0)
    W.rideRepair = true
    KCD2MP_SpawnHorse, KCD2MP_GhostDismount, KCD2MP_RemoveHorse, KCD2MP_GhostIsMounted = origSpawn, origDis, origRem, origMounted
    KCD2MP.ghosts = {}
    -- the horse under the ground (the field: bound at z -13.4 under a host at 31.8)
    local hz = -13.4
    local horse = { GetWorldPos = function() return { x = 1089.7, y = 3015.9, z = hz } end, SetWorldPos = function(self, p) hz = p.z end, class = "Horse" }
    ENTS["tvid_horse_2"] = horse
    local origTerrain = System.GetTerrainElevation
    System.GetTerrainElevation = function(p) return 31.0 end
    mark = #LOG
    check("R12: a horse copy under the terrain is put on it", KCD2MP_W172GroundSnap("tvid_horse_2", nil, "test") == true and math.abs(hz - 31.05) < 0.01
        and lastLog("WO172-RIDE horse ground-snapped npc=tvid_horse_2 why=test z=-13.40 terrain=31.00", mark) ~= nil)
    check("R13: one on the terrain is left alone", KCD2MP_W172GroundSnap("tvid_horse_2", nil, "test") == false)
    hz = 30.7
    check("R14: within half a metre below (a slope, a hoof) is left alone", KCD2MP_W172GroundSnap("tvid_horse_2", nil, "test") == false)
    hz = -2
    TIMERS = {}
    KCD2MP_W172HorseReleased("tvid_horse_2", horse)
    check("R15: a released horse is looked at now and twice more while its physics settles", math.abs(hz - 31.05) < 0.01 and #TIMERS == 2)
    hz = -5; runTimers()
    check("R16: ... and put back if it fell meanwhile", math.abs(hz - 31.05) < 0.01)
    System.GetTerrainElevation = origTerrain
    noErrs("R")
end

-- (F) FT1 / UI1: the black is never put back on a timer; a refused quest travel's black is lifted; the HUD comes back ---------------
do
    ERRS = {}
    reset()
    local W = KCD2MP.w172
    local shown = 0
    UIAction.ShowElement = function(el, inst) if el == "hud" then shown = shown + 1 end end
    CVARS["wh_ui_FaderSuspend"] = 1
    KCD2MP.w167.scene.faderHeldAt = NOW
    local mark = #LOG
    KCD2MP_W167FaderRestore("60 s after the black was lifted")
    check("F1: the 60 s restore keeps the black lifted (the field: each restore put it back, 7 times)", tostring(CVARS["wh_ui_FaderSuspend"]) == "1"
        and W.faderHold == true and lastLog("WO172-FT fader kept lifted", mark) ~= nil)
    KCD2MP.w167.scene.faderHeldAt = NOW
    W.faderHold = false
    KCD2MP_W167FaderRestore("the scene ended")
    check("F2: a scene the engine ended gives the faders back as before", tostring(CVARS["wh_ui_FaderSuspend"]) == "0")
    CVARS["wh_ui_FaderSuspend"] = 1; W.faderHold = true
    TIMERS = {}
    mark = #LOG
    KCD2MP_W172OnLoad("test")
    runTimers()
    check("F3: a load gives the held faders back and shows the HUD", tostring(CVARS["wh_ui_FaderSuspend"]) == "0" and W.faderHold == false and shown == 1
        and lastLog("WO172-HUD restored why=load:test ok=true", mark) ~= nil)
    -- the forced travel: this game refused the quest's travel, then the host travelled and the leash placed this player
    CVARS["wh_ui_FaderSuspend"] = 0
    TIMERS = {}
    mark = #LOG
    KCD2MP_W172TravelRefused("engine-refused")
    KCD2MP_Wo114Msg("Your host fast-travelled.")
    check("F4: the lift waits 1.5 s after the placement", #TIMERS >= 1 and TIMERS[#TIMERS].ms == 1500 and tostring(CVARS["wh_ui_FaderSuspend"]) == "0")
    runTimers()
    check("F5: then the black is lifted, the HUD shown, the line names it", tostring(CVARS["wh_ui_FaderSuspend"]) == "1"
        and lastLog("WO172-FT fader restored name=quest-travel-refused why=leash-placed ok=true", mark) ~= nil and shown == 2)
    TIMERS = {}
    W.faderHold = false; CVARS["wh_ui_FaderSuspend"] = 0
    KCD2MP_Wo114Msg("Your host fast-travelled.")
    runTimers()
    check("F6: a host travel with no refusal here: only the HUD, the faders untouched", tostring(CVARS["wh_ui_FaderSuspend"]) == "0" and shown == 3)
    mark = #LOG
    KCD2MP_W167SceneReleased(false, "stuck-20s")
    check("F7: a scene release shows the HUD", lastLog("WO172-HUD restored why=scene-release", mark) ~= nil)
    UIAction.ShowElement = nil
    noErrs("F")
end

-- (H) H1: a trader stays until the trade screen closes -----------------------------------------------------------------------
do
    ERRS = {}
    reset(); joiner()
    local W = KCD2MP.w172
    W.screenOpen, W.screenAt = nil, -1e9
    local e = trader("ttac_vojka", 101.5, 100, true)
    local function talk(name) KCD2MP.w137.talking[name] = { since = NOW, via = "OnChatWithFocus", started = true, holdSent = true, resumed = true } end
    talk("ttac_vojka")
    local mark = #LOG
    KCD2MP_W137TalkEnd("ttac_vojka", "dialog-ended")
    check("H1: the shop chat's end (the trade screen opens) does not end the talk: held", KCD2MP.w137.talking["ttac_vojka"] ~= nil
        and KCD2MP.w137.talking["ttac_vojka"].trade ~= nil and lastLog("WO172-TRADE hold npc=ttac_vojka", mark) ~= nil)
    for i = 1, 40 do NOW = NOW + 1; KCD2MP_W137TalkTick(); KCD2MP_W172TradeTick(NOW) end
    check("H2: 40 s in the trade screen: still held, no w137_talk off (the field: released after 3 s)", KCD2MP.w137.talking["ttac_vojka"] ~= nil
        and countEvt("w137_talk", "off ttac_vojka", mark) == 0)
    -- a haggle: a forced conversation on the same trader, cancelled
    IN_DIALOG = true
    NOW = NOW + 1; KCD2MP_W172TradeTick(NOW)
    IN_DIALOG = false
    KCD2MP_W137TalkEnd("ttac_vojka", "dialog-ended")
    for i = 1, 5 do NOW = NOW + 1; KCD2MP_W137TalkTick(); KCD2MP_W172TradeTick(NOW) end
    check("H3: a cancelled haggle releases nothing", KCD2MP.w137.talking["ttac_vojka"] ~= nil and countEvt("w137_talk", "off ttac_vojka", mark) == 0)
    KCD2MP_W172TradeScreen(1)
    NOW = NOW + 3
    KCD2MP_W172TradeScreen(0)
    local closedAt = NOW
    NOW = NOW + 0.3; KCD2MP_W172TradeTick(NOW)
    check("H4: the screen's close settles 0.5 s (a screen change is not a close)", KCD2MP.w137.talking["ttac_vojka"] ~= nil)
    NOW = NOW + 0.25; KCD2MP_W172TradeTick(NOW)
    check("H5: released within 1 s of the trade screen's close (0.5 s settle + the 0.25 s tick)", KCD2MP.w137.talking["ttac_vojka"] == nil and countEvt("w137_talk", "off ttac_vojka", mark) == 1
        and lastLog("WO172-TRADE release npc=ttac_vojka why=screen-closed", mark) ~= nil and NOW - closedAt <= 1.0)
    -- walking away
    talk("ttac_vojka")
    W.screenOpen, W.screenAt = nil, -1e9
    KCD2MP_W137TalkEnd("ttac_vojka", "dialog-ended")
    PX = 120
    NOW = NOW + 0.5; KCD2MP_W172TradeTick(NOW)
    NOW = NOW + 1.1; KCD2MP_W172TradeTick(NOW)
    check("H6: walked away (4 m for 1 s): released", KCD2MP.w137.talking["ttac_vojka"] == nil and lastLog("WO172-TRADE release npc=ttac_vojka why=walked-away", mark) ~= nil)
    PX = 100
    -- not a trader: the old end
    local f = trader("tzel_man_2", 101, 100, false)
    talk("tzel_man_2")
    KCD2MP_W137TalkEnd("tzel_man_2", "dialog-ended")
    check("H7: not a trader: the dialogue's end ends it as before (deferred)", KCD2MP.w137.talking["tzel_man_2"] ~= nil and KCD2MP.w137.talking["tzel_man_2"].trade == nil
        and KCD2MP.w137.talking["tzel_man_2"].endAt ~= nil)
    KCD2MP.w137.talking["tzel_man_2"] = nil
    W.tradeHold = false
    talk("ttac_vojka")
    KCD2MP_W137TalkEnd("ttac_vojka", "dialog-ended")
    check("H8: mp_trade_hold off: as before", KCD2MP.w137.talking["ttac_vojka"].trade == nil)
    W.tradeHold = true
    KCD2MP.w137.talking["ttac_vojka"] = nil
    check("H9: the release rule: 300 s at most", KCD2MP_W172TradeDue(0, 300, 1, nil, nil, -1e9, 4, 1, 300, 0.5) == "max-time"
        and KCD2MP_W172TradeDue(0, 299, 1, nil, nil, -1e9, 4, 1, 300, 0.5) == nil)
    noErrs("H")
end

-- (P) P1: the joiner's pickpocket asks the host for every item; no duplication -------------------------------------------------
do
    ERRS = {}
    reset(); joiner()
    KCD2MP.w131.guard = true; KCD2MP.w131.lootBlock = true
    KCD2MP_W131Tick(true, true)
    KCD2MP_W134Tick(true, false, true, 1)
    local origLoop = KCD2MP.w134.startLoop
    KCD2MP.w134.startLoop = function() end
    local victim = trader("ttkc_man_11", 101, 100, false)
    local purse = give(victim, "money", 30)
    local ring = give(victim, "ring_silver", 1)
    local minigames = 0
    local ORIG = function(self, user, slot) minigames = minigames + 1 end
    local mark = #LOG
    TOASTS = {}
    local allowed = KCD2MP_W131LootAllowed(victim, "pickpocket", ORIG, player, 0)
    check("P1: the joiner's pickpocket runs the game's own minigame on the copy (no 'only the host' message)", allowed == false and minigames == 1
        and #TOASTS == 0 and lastLog("WO172-PICK start npc=ttkc_man_11 items=2 ok=true", mark) ~= nil)
    -- the minigame took the ring: the copy lost it, the player got it
    victim.inventory:DeleteItem(ring, 1); local mine = give(player, "ring_silver", 1)
    KCD2MP.w134.loopOnce()
    local take = lastEvt("w134_take", mark)
    check("P2: the take goes to the host (w134_take ttkc_man_11 ring_silver 1)", take ~= nil and take:find("ttkc_man_11 ring_silver 1", 1, true) ~= nil)
    local tok = take and take:match("w134_take (%S+) ")
    KCD2MP_W134TakeResult(tok, "ok", "ttkc_man_11")
    check("P3: the host had it: kept once", countCls(player, "ring_silver") == 1)
    -- the host's NPC no longer had it (the host stole it first): taken back off the player, told plainly
    victim.inventory:DeleteItem(purse, 1); give(player, "money", 30)
    KCD2MP.w134.loopOnce()
    take = lastEvt("w134_take", mark)
    tok = take and take:match("w134_take (%S+) ")
    TOASTS = {}
    KCD2MP_W134TakeResult(tok, "gone", "ttkc_man_11")
    check("P4: an item the host's NPC no longer has is taken back with a plain message", countCls(player, "money") == 0 and TOASTS[1] ~= nil and TOASTS[1]:find("already took", 1, true) ~= nil)
    -- 20 tries: every taken item ends either kept once (the host's NPC had it) or taken back -- never two
    local okN, goneN = 0, 0
    for i = 1, 20 do
        local w = give(victim, "apple", 1)
        KCD2MP_W131LootAllowed(victim, "pickpocket", ORIG, player, 0)
        victim.inventory:DeleteItem(w, 1); give(player, "apple", 1)
        KCD2MP.w134.loopOnce()
        local t = lastEvt("w134_take")
        local k = t and t:match("w134_take (%S+) ")
        if i % 3 == 0 then KCD2MP_W134TakeResult(k, "gone", "ttkc_man_11"); goneN = goneN + 1 else KCD2MP_W134TakeResult(k, "ok", "ttkc_man_11"); okN = okN + 1 end
    end
    check("P5: 20 tries: the player holds exactly the items the host gave up (no duplication)", countCls(player, "apple") == okN and okN == 14 and goneN == 6, countCls(player, "apple"))
    -- off: the old block and its message
    KCD2MP.w172.pickpocket = false
    TOASTS = {}
    minigames = 0
    KCD2MP_W131LootAllowed(victim, "pickpocket", ORIG, player, 0)
    check("P6: mp_joiner_pickpocket off: blocked with the old message", minigames == 0 and TOASTS[1] ~= nil and TOASTS[1]:find("Only the host can pickpocket", 1, true) ~= nil)
    KCD2MP.w172.pickpocket = true
    -- players never rob each other (WO-139 Phase 4)
    KCD2MP.w134.startLoop = origLoop
    noErrs("P")
end

-- (W) the switches and the status --------------------------------------------------------------------------------------------
do
    ERRS = {}
    reset()
    check("W1: the switches and the status command are registered", CCMDS["mp_dead_wins"] ~= nil and CCMDS["mp_dead_wins"].body == "KCD2MP_W172SetDeadWins(%line)"
        and CCMDS["mp_ride_repair"] ~= nil and CCMDS["mp_trade_hold"] ~= nil and CCMDS["mp_joiner_pickpocket"] ~= nil and CCMDS["mp_w172_status"] ~= nil)
    local W = KCD2MP.w172
    check("W2: the defaults: all on", W.deadWins == true and W.rideRepair == true and W.tradeHold == true and W.pickpocket == true)
    local mark = #LOG
    check("W3: mp_dead_wins tells the agent", KCD2MP_W172SetDeadWins("off") == true and countEvt("w172_cfg", "dead_wins=off", mark) == 1 and KCD2MP_W172SetDeadWins("on") == true)
    check("W4: a bad word changes nothing", KCD2MP_W172SetRideRepair("maybe") == false and W.rideRepair == true)
    mark = #LOG
    KCD2MP_W172Status()
    check("W5: the status line", lastLog("WO172-STATUS dead_wins=on ride_repair=on trade_hold=on joiner_pickpocket=on", mark) ~= nil)
    mark = #LOG
    KCD2MP.w137.aliveAt = nil
    KCD2MP_W137Session(false, true, true)
    check("W6: a (re)connected agent learns the switches it acts on", countEvt("w172_cfg", "dead_wins=on", mark) == 1 and countEvt("w172_cfg", "ride_repair=on", mark) == 1)
    CMDS = {}
    mark = #LOG
    KCD2MP_Mark("odd")
    -- WO-192: the mark screenshot is removed (files on a tester's PC they never took): a mark takes none, the switch is gone
    check("W7: a mark takes no screenshot (WO-192)", cmdCount("r_GetScreenShot") == 0 and lastLog("MP-MARK odd", mark) ~= nil)
    check("W8: mp_mark_shot is gone (WO-192)", CCMDS["mp_mark_shot"] == nil and KCD2MP_W172SetMarkShot == nil)
    noErrs("W")
end

OUT = table.concat(RESULTS, "\n")
