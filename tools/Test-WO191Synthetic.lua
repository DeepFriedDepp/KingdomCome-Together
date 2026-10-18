-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-191 synthetic test (the mod's half: the four switches, the fight guard on every sweep trigger and on the context release, the
-- attacker pin into the NPC stream), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock). Driven by Test-WO191Synthetic.ps1.
NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}

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
NEAR = {}
System.GetEntitiesInSphere = function(p, r) return NEAR end
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
Calendar = mkstub()
Calendar.GetWorldTime = function() return 1000000 end
XGenAIModule = mkstub()
EnvironmentModule = { GetRainIntensity = function() return 0 end }
XGenAIModule.IsPointInAreaWithLabel = function(p, label) return false end

PX, PY, PZ = 100, 100, 10
DRAWN = false
player = { id = 1, class = "Player" }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PX, y = PY, z = PZ } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.GetDirectionVector = function() return { x = 1, y = 0, z = 0 } end   -- facing +x
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end }
player.human = { IsInDialog = function() return false end, InterruptDialogs = function() end, IsWeaponDrawn = function() return DRAWN end }
player.soul = { GetState = function() return 0 end, IsInCombatDanger = function() return false end, IsInCombatMode = function() return false end }
ENTS["Dude"] = player

local NPCID = 1000
local function npc(name, cls, x, y, fighting)
    NPCID = NPCID + 1
    local e = { id = NPCID, class = cls }
    e.this = { id = e.id }
    e.GetName = function() return name end
    e.GetWorldPos = function() return { x = x, y = y, z = 10 } end
    e.soul = { IsInCombatMode = function() return fighting and fighting() == true or false end }
    e.actor = { IsDead = function() return false end }
    e.human = { IsInDialog = function() return false end }
    ENTS[name] = e
    return e
end

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return unpack(r)
end

-- @@KDCMP@@

-- WO-200 Act III: the sweeps and the geometric rider rule these scenarios pin stand down while WO-200's copy_neutral_always and
-- rider_from_host are on (the default); they are tested here with those off (Test-WO200Synthetic.lua covers the replacements).
if KCD2MP_W200Set then KCD2MP_W200Set("copy_neutral_always", "off"); KCD2MP_W200Set("rider_from_host", "off") end
local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function logCount(needle, fromLog)
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
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end

-- (A) four switches, on by default, each with its console command; the agent hears them
do
    local w = KCD2MP.w191
    check("A1: on by default", w.forget == true and w.fightGuard == true and w.attackerBind == true and w.faderBound == true)
    check("A2: the console commands exist", CCMDS["mp_action_forget"] ~= nil and CCMDS["mp_sweep_fight_guard"] ~= nil and CCMDS["mp_attacker_bind"] ~= nil and CCMDS["mp_fader_bound"] ~= nil)
    local mark = #LOG
    KCD2MP_W191Cfg()
    check("A3: the agent is told all four", countEvt("w191_cfg", "forget=on fightguard=on attackerbind=on faderbound=on", mark) == 1)
    check("A4: a switch off reaches the agent", KCD2MP_W191SetFaderBound("off") == true and w.faderBound == false and countEvt("w191_cfg", "forget=on fightguard=on attackerbind=on faderbound=off", mark) == 1)
    check("A5: a bad word is refused", KCD2MP_W191SetForget("maybe") == false and w.forget == true)
    KCD2MP_W191SetFaderBound("on")
    noErrs("A")
end

-- (B) the fight guard: each sign, the 10 s window, the switch
do
    NOW = 100
    local inCombat = false
    npc("b_copy", "NPC", 102, 100, function() return inCombat end)       -- 2 m in front of the player
    npc("b_behind", "NPC", 98, 100)                                        -- 2 m behind
    check("B1: a quiet copy may be reset", KCD2MP_W191Fighting("b_copy") == nil)
    inCombat = true
    check("B2: combat mode (the guard hold) blocks", KCD2MP_W191Fighting("b_copy") == "combat-mode")
    inCombat = false
    NOW = 105
    local r = KCD2MP_W191Fighting("b_copy")
    check("B3: 5 s after the last sign it still blocks", r ~= nil and r:find("combat-mode", 1, true) ~= nil, r)
    NOW = 111
    check("B4: 11 s after, it may be reset again", KCD2MP_W191Fighting("b_copy") == nil)
    KCD2MP.w164.hostCombat["b_copy"] = NOW - 3
    check("B5: the host's NPC in combat 3 s ago blocks", KCD2MP_W191Fighting("b_copy") == "host-combat")
    KCD2MP.w164.hostCombat["b_copy"] = nil
    NOW = 130
    DRAWN = true
    check("B6: this player faces it within 4 m with a weapon drawn: blocks", KCD2MP_W191Fighting("b_copy") == "a-player-faces-it-armed")
    check("B7: armed but facing away: not targeted", KCD2MP_W191Fighting("b_behind") == nil)
    DRAWN = false
    NOW = 150
    KCD2MP_W191FightMark("b_behind", "engaged")
    NOW = 152
    r = KCD2MP_W191Fighting("b_behind")
    check("B8: the agent's mark (engaged) blocks for 10 s", r ~= nil and r:find("engaged", 1, true) ~= nil, r)
    KCD2MP_W191SetFightGuard("off")
    check("B9: mp_sweep_fight_guard off: nothing blocks", KCD2MP_W191Fighting("b_behind") == nil)
    KCD2MP_W191SetFightGuard("on")
    noErrs("B")
end

-- (C) every sweep trigger, and the context release, refuse a copy that fights; once quiet they run; the skip is told once per fight
do
    KCD2MP.w164.sweep = true
    KCD2MP.w160.release = true
    local triggers = { "focus", "skip", "adaptive", "retry", "hostconv", "console" }
    NOW = 1000
    for i, t in ipairs(triggers) do
        local name = "c_" .. t
        npc(name, "NPC", 200, 200)
        KCD2MP.npcPuppets[name] = { cx = 200, cy = 200 }
        KCD2MP_W191FightMark(name, "engaged")
    end
    local mark = #LOG
    local refused = 0
    for _, t in ipairs(triggers) do
        if KCD2MP_W164Sweep("c_" .. t, t) == false then refused = refused + 1 end
    end
    check("C1: every trigger refused mid-fight (focus, skip, adaptive, retry, hostconv, console)", refused == #triggers, refused)
    check("C2: no sweep event reached the agent", countEvt("w164_sweep", "", mark) == 0)
    check("C3: no stance reset was issued", #CMDS == 0, CMDS[1])
    check("C4: each copy's skip told once", logCount("WO191-SWEEP-LUA npc=c_", mark) == #triggers)
    KCD2MP_W164Sweep("c_skip", "skip")
    check("C5: a second skip in the same fight is not told again", logCount("WO191-SWEEP-LUA npc=c_skip", mark) == 1)
    check("C6: the context release (activity change) refused mid-fight", KCD2MP_W160Release("c_focus", "activity-changed") == false and #CMDS == 0)
    NOW = 1011
    mark = #LOG
    check("C7: 11 s later the retry sweep runs", KCD2MP_W164Sweep("c_retry", "retry") == true and countEvt("w164_sweep", "c_retry retry", mark) == 1)
    check("C8: and the context release runs", KCD2MP_W160Release("c_focus", "activity-changed") == true and #CMDS >= 2)
    npc("kcd2mp_1", "NPC", 210, 200)
    KCD2MP_W191FightMark("kcd2mp_1", "engaged")
    local before = #CMDS
    check("C9: a partner's figure's release (its seat) is never gated", KCD2MP_W160Release("kcd2mp_1", "w164-stale") == true and #CMDS == before + 2)
    NOW = 1012
    KCD2MP_W191FightMark("c_adaptive", "a-player-hit-it")
    mark = #LOG
    KCD2MP_W164Sweep("c_adaptive", "adaptive")
    check("C10: a new fight (after 10 s of quiet) is told again", logCount("WO191-SWEEP-LUA npc=c_adaptive", mark) == 1)
    NOW = 1100
    npc("c_stale", "NPC", 220, 200, function() return true end)   -- its own combat flag only (stale after the host's talk)
    KCD2MP.npcPuppets["c_stale"] = { cx = 220, cy = 200 }
    mark = #LOG
    check("C11: the host's talk ended: a copy whose only sign is its own combat flag is swept (WO-167 T2 clears it next)",
        KCD2MP_W164Sweep("c_stale", "hostconv") == true and countEvt("w164_sweep", "c_stale hostconv", mark) == 1)
    NOW = 1130
    check("C12: ... but any other trigger refuses it (combat mode)", KCD2MP_W164Sweep("c_stale", "focus") == false)
    noErrs("C")
end

-- (D) the attacker pin: the host streams an NPC or animal that targets a partner at once, any living class, 60 s after its last sign
do
    NOW = 2000
    KCD2MP.wo102.authorityHost = true
    KCD2MP.hitSensorOn = true
    npc("d_dog", "Dog", 300, 300)
    npc("d_crate", "BasicEntity", 300, 301)
    local mark = #LOG
    check("D1: a village dog (class Dog) is pinned", KCD2MP_W191PinTrack("d_dog", "targets-player-1") == true and KCD2MP.npcTracked["d_dog"] ~= nil)
    check("D2: logged as tracked (the stream's own line)", logCount("NPC-SYNC tracking d_dog", mark) == 1 and logCount("WO191-BIND pinned npc=d_dog class=Dog", mark) == 1)
    check("D3: not a body class: refused", KCD2MP_W191PinTrack("d_crate", "targets-player-1") == false and KCD2MP.npcTracked["d_crate"] == nil)
    check("D4: no such entity: refused", KCD2MP_W191PinTrack("d_nobody", "x") == false)
    check("D5: our own figure's name is never pinned", KCD2MP_W191PinTrack("kcd2mp_1", "x") == false)
    local keep = {}
    NOW = 2059
    KCD2MP_W191KeepPinned(keep)
    check("D6: kept by the rescan 59 s after", keep["d_dog"] == true)
    keep = {}
    NOW = 2061
    KCD2MP_W191KeepPinned(keep)
    check("D7: let go 61 s after (the rescan's own rules again)", keep["d_dog"] == nil and KCD2MP.w191.pins["d_dog"] == nil)
    KCD2MP_W191SetAttackerBind("off")
    npc("d_wolf", "Wolf", 310, 300)
    check("D8: mp_attacker_bind off: nothing pinned", KCD2MP_W191PinTrack("d_wolf", "x") == false and KCD2MP.npcTracked["d_wolf"] == nil)
    KCD2MP_W191SetAttackerBind("on")
    KCD2MP.hitSensorOn = false
    check("D9: never on a machine that does not own the NPCs (the joiner)", KCD2MP_W191PinTrack("d_wolf", "x") == false)
    noErrs("D")
end

OUT = table.concat(RESULTS, "\n")
