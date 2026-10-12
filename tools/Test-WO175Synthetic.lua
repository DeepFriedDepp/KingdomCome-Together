-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-175 synthetic test (save safety, the game-side halves), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock).
--   (G) S3: the mod's world save waits during a dialogue, a scene, a fight, an arranged (quest) fight or a mod change
--   (L) S3: the game's saves wait under a script lock while a mod change is in flight; 10 s cap with a roll-back
--   (H) S4: the agent's verdicts are said plainly; load / save / repair ask the agent to check the file; the switches
-- Driven by Test-WO175Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-175-findings.md.
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


-- the stubs the repair needs ---------------------------------------------------------------------------------------------------
HOSTILE = {}          -- "a|b" -> true
EQUIPPED = nil        -- the item id in the weapon set (nil = the set is empty)
DRAWN = false
LOCKED_BY_LINK = false -- a hostile figure link blocks the draw until it is removed
IN_COMBAT = false
SPHERE = {}
INV = {}
AI.IsPersonallyHostile = function(a, b) return HOSTILE[tostring(a) .. "|" .. tostring(b)] == true end
AI.RemovePersonallyHostile = function(a, b) HOSTILE[tostring(a) .. "|" .. tostring(b)] = nil end
player.player = { IsLockedToOpponent = function(self, id) return HOSTILE["1|" .. tostring(id)] == true end }
System.GetEntitiesInSphere = function(p, r) return SPHERE end
player.id = 1
player.this = { id = "wuid-dude" }
player.soul.IsInCombatDanger = function() return IN_COMBAT end
player.soul.IsInCombatMode = function() return false end
player.human.IsWeaponDrawn = function() return DRAWN end
player.human.DrawWeapon = function()
    if EQUIPPED ~= nil and not LOCKED_BY_LINK then DRAWN = true end
end
player.human.HolsterWeapon = function() DRAWN = false end
player.actor.EquipInventoryItem = function(self, id) EQUIPPED = id end
player.inventory = { GetInventoryTable = function() local t = {}; for i = 1, #INV do t[i] = i end; return t end }
ItemManager.GetItem = function(i) local it = INV[i]; return it and { class = it.name, id = it.id, health = it.health } or nil end
ItemManager.GetItemName = function(c) return c end
local function inv(list) INV = {}; for i, x in ipairs(list) do INV[i] = { name = x[1], health = x[2], id = "item" .. i } end end
local function drive()   -- run every timer to exhaustion (the repair is a chain of timers)
    for _ = 1, 40 do
        if #TIMERS == 0 then break end
        runTimers()
    end
end

LOCKS = {}
Game.AddSaveLock = function(name, text) if LOCKS[name] then return false end LOCKS[name] = text; return true end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] ~= nil; LOCKS[name] = nil; return had end
SAVES = 0
Game.SaveGameViaResting = function() SAVES = SAVES + 1 end
CONTEXTS = {}
player.soul.HasScriptContext = function(self, name) return CONTEXTS[name] == true end
local function fresh()
    IN_COMBAT = false; IN_DIALOG = false; CONTEXTS = {}; LOCKS = {}; SAVES = 0; TIMERS = {}; TOASTS = {}
    KCD2MP.w140.held = nil
    KCD2MP.w174.busy = false
    KCD2MP.w134.sessions = {}; KCD2MP.w134.itemReq = {}; KCD2MP.w134.pendingOpen = {}
    KCD2MP.w170.sceneRunning = 0
    local w = KCD2MP.w175
    w.guard = true; w.health = true; w.lockHeld = false; w.lockSince = nil; w.lockReason = nil; w.tickAt = -1e9; w.toldAt = -1e9
end

-- (G) the mod's own world save -----------------------------------------------------------------------------------------------
do
    ERRS = {}
    fresh()
    local mark = #LOG
    KCD2MP_HostWorldSave("schedule", 1)
    -- WO-178 Part 0: asked the schnapps way (w178_save -> the DLL's ManualSave), never Game.SaveGameViaResting
    check("G1: idle: the mod's world save is asked for, and the agent is asked to check the file", SAVES == 0 and countEvt("w178_save", "world:schedule", mark) == 1
          and countEvt("w175_check", "save", mark) == 1)
    local cases = {
        { "dialogue", function() IN_DIALOG = true end },
        { "scene", function() KCD2MP.w170.sceneRunning = 1 end },
        { "combat", function() IN_COMBAT = true end },
        { "arranged-fight", function() CONTEXTS["combat_arrangedFight"] = true end },
        { "arranged-fight", function() CONTEXTS["combat_forceUnarmedWhileInUnarmedSkirmish"] = true end },
        { "sleep-hold", function() KCD2MP.w140.held = { at = 0 } end },
        { "weapon-repair", function() KCD2MP.w174.busy = true end },
        { "loot-take", function() KCD2MP.w134.sessions["b1"] = { pend = { t1 = {} }, puts = {} } end },
        { "item-take", function() KCD2MP.w134.itemReq["t2"] = {} end },
    }
    for i, c in ipairs(cases) do
        fresh(); c[2]()
        mark = #LOG
        local r = KCD2MP_HostWorldSave("schedule", 1)
        check("G2." .. i .. ": " .. c[1] .. ": the world save waits (no request), logged", r == false and SAVES == 0 and countEvt("w178_save", "world:schedule", mark) == 0
            and lastLog("WO175-SAVE deferred reason=" .. c[1] .. " kind=world-save:schedule", mark) ~= nil, lastLog("WO175-SAVE", mark))
    end
    fresh(); IN_DIALOG = true
    KCD2MP_W175SetGuard("off")
    mark = #LOG
    KCD2MP_HostWorldSave("schedule", 1)
    check("G3: mp_save_guard off: the old behaviour (asked at once)", SAVES == 0 and countEvt("w178_save", "world:schedule", mark) == 1)
    KCD2MP_W175SetGuard("on")
    check("G4: a bad value is refused", KCD2MP_W175SetGuard("maybe") == false and KCD2MP.w175.guard == true)
    noErrs("G")
end

-- (L) the game's saves wait while a mod change is in flight -------------------------------------------------------------------
do
    ERRS = {}
    fresh()
    local mark = #LOG
    KCD2MP_W175Tick()
    check("L1: nothing in flight: no lock, the chain goes on", next(LOCKS) == nil and #TIMERS == 1)
    TIMERS = {}
    KCD2MP.w134.sessions["b1"] = { pend = { t1 = {} } }
    NOW = 100; KCD2MP_W175Tick()
    check("L2: a loot take in flight: the script save lock is on, logged, the player told", LOCKS["kcdmp_w175_change"] ~= nil
        and lastLog("WO175-SAVE deferred reason=loot-take kind=game-save lock=on", mark) ~= nil and #TOASTS == 0 or LOCKS["kcdmp_w175_change"] ~= nil)
    KCD2MP.w134.sessions = {}
    NOW = 101; TIMERS = {}; KCD2MP_W175Tick()
    check("L3: the take done: the lock goes, logged", LOCKS["kcdmp_w175_change"] == nil and lastLog("WO175-SAVE resumed reason=loot-take", mark) ~= nil)
    local dropped = 0
    local was = KCD2MP_W140Drop
    KCD2MP_W140Drop = function(msg) dropped = dropped + 1; KCD2MP.w140.held = nil end
    KCD2MP.w140.held = { at = 0 }
    NOW = 200; TIMERS = {}; KCD2MP_W175Tick()
    check("L4: a held bed: locked", LOCKS["kcdmp_w175_change"] ~= nil)
    NOW = 205; TIMERS = {}; KCD2MP_W175Tick()
    check("L5: 5 s later: still locked, nothing rolled back", LOCKS["kcdmp_w175_change"] ~= nil and dropped == 0)
    NOW = 211; TIMERS = {}; KCD2MP_W175Tick()
    check("L6: past 10 s: rolled back (the bed goes back to the game) and the lock goes", dropped == 1 and LOCKS["kcdmp_w175_change"] == nil
        and lastLog("WO175-SAVE max-wait reason=sleep-hold", mark) ~= nil)
    KCD2MP_W140Drop = was
    fresh(); mark = #LOG
    KCD2MP.w175.lockHeld = true
    KCD2MP_W175AfterLoad("settle")
    -- four timers since WO-177: WO-175's chain, WO-176's figure check, WO-177's corpse census and calm-NPC list (all re-armed by the same after-load hook)
    -- WO-178 0.c: + the draw probe's timer (5)
    check("L7: after a load: state reset, the chain runs, w175_check load", KCD2MP.w175.lockHeld == false and #TIMERS == 5 and countEvt("w175_check", "load", mark) == 1, #TIMERS)
    KCD2MP_W175SetHealth("off"); mark = #LOG
    KCD2MP_W175AfterLoad("settle")
    check("L8: mp_save_health off: no file check asked", countEvt("w175_check", "load", mark) == 0)
    KCD2MP_W175SetHealth("on")
    noErrs("L")
end

-- (H) the verdicts and the repair step -------------------------------------------------------------------------------------
do
    ERRS = {}
    fresh()
    local mark = #LOG
    KCD2MP_W175Health("load", "ok", "none", "", false)
    check("H1: ok: one log line, nothing said", lastLog("WO175-HEALTH when=load result=ok what=none", mark) ~= nil and #TOASTS == 0)
    KCD2MP_W175Health("load", "repaired", "stuck-quest-fight:konecZabomysichValek/souboj_na_poli", "playline4/autosave335_fixed.whs", true)
    check("H2: repaired on a load: logged with the file, the player told in plain words",
        lastLog("WO175-HEALTH when=load result=repaired what=stuck-quest-fight:konecZabomysichValek/souboj_na_poli file=playline4/autosave335_fixed.whs", mark) ~= nil
        and KCD2MP.w175.stats.repaired >= 1)
    KCD2MP_W175Health("save", "failed", "unreadable", "", false)
    check("H3: failed: logged and counted", lastLog("WO175-HEALTH when=save result=failed what=unreadable", mark) ~= nil and KCD2MP.w175.stats.failed >= 1)
    mark = #LOG
    KCD2MP.w174.busy = false
    KCD2MP_W174Repair("")
    check("H4: mp_repair_combat asks the agent to check the save for a stuck quest fight", countEvt("w175_check", "repair", mark) == 1)
    KCD2MP.w174.busy = false; TIMERS = {}
    check("H5: the switches are registered", CCMDS["mp_save_guard"] ~= nil and CCMDS["mp_save_guard"].body == "KCD2MP_W175SetGuard(%line)"
        and CCMDS["mp_save_health"] ~= nil and CCMDS["mp_save_health"].body == "KCD2MP_W175SetHealth(%line)")
    check("H6: both default on", KCD2MP.w175.guard == true and KCD2MP.w175.health == true)
    noErrs("H")
end

OUT = table.concat(RESULTS, "\n")
