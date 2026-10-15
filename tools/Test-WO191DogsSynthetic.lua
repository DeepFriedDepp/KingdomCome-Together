-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- The WO-191 follow-up synthetic test (village dogs in the NPC stream, mp_dog_sync): the switch, the host's class gate in the rescan
-- (a village dog tracked and streamed as not human, the player's own dog never), the joiner's side (its own dog never driven by the
-- host, a released dog copy paused and shown, not hidden), the enemy list left alone, the stand-in class. Against the real kdcmp.lua
-- under MoonSharp (engine stubbed, fake clock). Driven by Test-WO191DogsSynthetic.ps1.
NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}; SPHERE = {}

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
System.GetEntitiesInSphere = function(p, r) return SPHERE end
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
SPAWNS = {}
XGenAIModule.SpawnEntity = function(t) SPAWNS[#SPAWNS + 1] = t end
EnvironmentModule = { GetRainIntensity = function() return 0 end }
XGenAIModule.IsPointInAreaWithLabel = function(p, label) return false end

player = { id = 1, class = "Player" }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = 0, y = 0, z = 0 } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.GetDirectionVector = function() return { x = 1, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end }
player.human = { IsInDialog = function() return false end, InterruptDialogs = function() end, IsWeaponDrawn = function() return false end }
player.soul = { GetState = function() return 0 end, GetId = function() return "player-soul" end, IsInCombatDanger = function() return false end, IsInCombatMode = function() return false end }
ENTS["Dude"] = player

local NEXTID = 1000
local function body(name, cls, x, y, rel)
    NEXTID = NEXTID + 1
    local e = { class = cls, id = NEXTID, px = x or 0, py = y or 0, pz = 0, rz = 0, hidden = false, hides = 0 }
    e.this = { id = e.id }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self) return { x = e.px, y = e.py, z = e.pz } end
    e.GetWorldAngles = function(self) return { x = 0, y = 0, z = e.rz } end
    e.SetWorldPos = function(self, p) e.px, e.py, e.pz = p.x, p.y, p.z end
    e.SetWorldAngles = function(self, a) e.rz = a.z end
    e.Hide = function(self, h) e.hidden = (h == 1); if h == 1 then e.hides = e.hides + 1 end end
    e.IsHidden = function(self) return e.hidden end
    e.StartAnimation = function() end
    e.actor = { IsDead = function() return false end, IsUnconscious = function() return false end, GetHealth = function() return 100 end }
    e.human = { IsInDialog = function() return false end, IsWeaponDrawn = function() return false end }
    e.soul = { IsInCombatMode = function() return false end, IsPublicEnemy = function() return false end,
               GetRelationship = function() return rel or 0.2 end }
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
local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function logCount(needle, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do if LOG[i]:find(needle, 1, true) then n = n + 1 end end
    return n
end
local function lastLog(needle)
    for i = #LOG, 1, -1 do if LOG[i]:find(needle, 1, true) then return LOG[i] end end
    return nil
end
local function cmdCount(needle)
    local n = 0
    for _, c in ipairs(CMDS) do if c == needle then n = n + 1 end end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end

local function hostRescan(t)
    KCD2MP.hitSensorOn = true
    KCD2MP.wo102.authorityHost = true
    KCD2MP.wo102.npcScanNative = false   -- the Lua walk (the native scan's candidates are the C++ half; see the findings)
    KCD2MP.npcSync.enabled = true; KCD2MP.npcSyncRunning = true
    KCD2MP.ghosts = {}
    NOW = t; KCD2MP._npcScanAt = 0
    KCD2MP_NpcSyncTick()
end

-- (A) the switch: on by default, its console command, a bad word refused
do
    check("A1: on by default", KCD2MP.dogSync.on == true)
    check("A2: the console command exists", CCMDS["mp_dog_sync"] ~= nil and CCMDS["mp_dog_sync"].body:find("KCD2MP_DogSyncSet", 1, true) ~= nil)
    local mark = #LOG
    check("A3: off", KCD2MP_DogSyncSet("off") == true and KCD2MP.dogSync.on == false and logCount("DOG-SYNC toggle mp_dog_sync off", mark) == 1)
    check("A4: a bad word is refused", KCD2MP_DogSyncSet("maybe") == false and KCD2MP.dogSync.on == false)
    KCD2MP_DogSyncSet("on")
    check("A5: on again", KCD2MP.dogSync.on == true)
    noErrs("A")
end

-- (B) the class gate
do
    check("B1: a village dog (class Dog) is streamed", KCD2MP_DogSyncStreams("Dog", "ttkc_dog_1") == true)
    check("B2: the player's dog by its soul name is not", KCD2MP_DogSyncStreams("Dog", "tvez_vorech") == false)
    check("B3: nor by the game's companion entity name", KCD2MP_DogSyncStreams("Dog", "player_dogCompanion_vorech") == false
          and KCD2MP_DogSyncStreams("Dog", "player_dogCompanion_2") == false)
    check("B4: other classes are not this gate's", KCD2MP_DogSyncStreams("Wolf", "w") == false and KCD2MP_DogSyncStreams("NPC", "n") == false
          and KCD2MP_DogSyncStreams("Cat", "c") == false)
    check("B5: Dog is not an encounter animal (the enemy set is unchanged)", KCD2MP_W136IsAnimalClass("Dog") == false
          and KCD2MP_W136IsAnimalClass("WildDog") == true)
    KCD2MP_DogSyncSet("off")
    check("B6: off: no dog is streamed", KCD2MP_DogSyncStreams("Dog", "ttkc_dog_1") == false)
    KCD2MP_DogSyncSet("on")
    noErrs("B")
end

-- (C) the host's rescan: a village dog is tracked and streams as not human (0x80); the player's dog never; off lets go
do
    local dog = body("c_dog", "Dog", 5, 0)
    local mutt = body("tvez_vorech", "Dog", 3, 0)
    local man = body("c_man", "NPC", 4, 0)
    local crate = body("c_crate", "BasicEntity", 2, 0)
    SPHERE = { dog, mutt, man, crate }
    local mark = #LOG
    local streamed0 = KCD2MP.dogSync.stats.streamed
    hostRescan(100)
    check("C1: the village dog is tracked", KCD2MP.npcTracked["c_dog"] ~= nil and logCount("NPC-SYNC tracking c_dog", mark) == 1)
    check("C2: counted as a streamed dog", KCD2MP.dogSync.stats.streamed == streamed0 + 1)
    check("C3: the human beside it too (control)", KCD2MP.npcTracked["c_man"] ~= nil)
    check("C4: the player's own dog is never tracked", KCD2MP.npcTracked["tvez_vorech"] == nil)
    check("C5: nor a crate", KCD2MP.npcTracked["c_crate"] == nil)
    local st = lastLog("npc_state c_dog ")
    local flags = st and tonumber(st:match("npc_state c_dog %S+ %S+ %S+ %S+ %S+ (%d+)"))
    check("C6: it streams flagged not human (0x80)", flags ~= nil and (math.floor(flags / 128) % 2) == 1, st)
    local st2 = lastLog("npc_state c_man ")
    local f2 = st2 and tonumber(st2:match("npc_state c_man %S+ %S+ %S+ %S+ %S+ (%d+)"))
    check("C7: the human does not (control)", f2 ~= nil and (math.floor(f2 / 128) % 2) == 0, st2)
    KCD2MP_DogSyncSet("off")
    mark = #LOG
    hostRescan(102)
    check("C8: off: the dog is let go at the next rescan", KCD2MP.npcTracked["c_dog"] == nil and logCount("NPC-SYNC untracking c_dog", mark) == 1)
    check("C9: off: the human stays", KCD2MP.npcTracked["c_man"] ~= nil)
    -- off, the WO-191 pin still streams an attacking dog (0.60.7)
    check("C10: off: a dog that attacks is still pinned (WO-191 3b)", KCD2MP_W191PinTrack("c_dog", "targets-player-1") == true and KCD2MP.npcTracked["c_dog"] ~= nil)
    KCD2MP.w191.pins = {}
    KCD2MP_DogSyncSet("on")
    KCD2MP.npcSyncRunning = false; KCD2MP.hitSensorOn = false; KCD2MP.wo102.authorityHost = false
    KCD2MP.npcTracked = {}
    noErrs("C")
end

-- (D) the joiner: this world's own dog is never the host's copy; a village dog is
do
    KCD2MP.npcPuppets = {}
    body("tvez_vorech", "Dog", 3, 0)
    body("d_dog", "Dog", 6, 0)
    local mark = #LOG
    NOW = 200
    KCD2MP_ApplyNpcState("tvez_vorech", 3.5, 0, 0, 0, 100, 128, 0)
    KCD2MP_ApplyNpcState("player_dogCompanion_vorech", 3.5, 0, 0, 0, 100, 128, 0)
    check("D1: the host's stream for the player's dog is refused", KCD2MP.npcPuppets["tvez_vorech"] == nil
          and logCount("DOG-SYNC refused npc=tvez_vorech", mark) == 1)
    check("D2: by either of its names", KCD2MP.npcPuppets["player_dogCompanion_vorech"] == nil)
    KCD2MP_ApplyNpcState("d_dog", 6.5, 0, 0, 0, 100, 128, 0)
    check("D3: a village dog's stream makes a copy (puppet)", KCD2MP.npcPuppets["d_dog"] ~= nil)
    KCD2MP_DogSyncSet("off")
    KCD2MP_ApplyNpcState("tvez_vorech", 3.5, 0, 0, 0, 100, 128, 0)
    check("D4: off: 0.60.7's path (no refusal of its own)", logCount("DOG-SYNC refused npc=tvez_vorech", mark) == 1)
    KCD2MP_DogSyncSet("on")
    KCD2MP.npcPuppets = {}
    noErrs("D")
end

-- (E) the joiner's release: a dog copy the stream lets go of is paused where it stands and stays shown; a human is parked (hidden)
do
    local w = KCD2MP.w131
    w.guard, w.joiner, w.shared, w.aliveAt = true, true, true, NOW   -- the guard's own gate: a joiner of a shared world under host authority
    KCD2MP.hitSensorOn = false; KCD2MP.wo102.authorityHost = true
    local dog = body("e_dog", "Dog", 8, 0)
    local man = body("e_man", "NPC", 9, 0)
    CMDS = {}
    local mark = #LOG
    check("E1: handled", KCD2MP_W131ParkReleased("e_dog", "silence") == true)
    check("E2: paused", cmdCount("wh_ai_PauseNPC e_dog") == 1)
    check("E3: not hidden", dog.hides == 0 and dog.hidden == false)
    check("E4: kept as a paused dog (the sweep re-pauses it), not parked", w.pausedAnimals["e_dog"] ~= nil and w.parked["e_dog"] == nil)
    check("E5: logged", logCount("DOG-SYNC released npc=e_dog why=silence", mark) == 1)
    KCD2MP_W131ParkReleased("e_man", "silence")
    check("E6: a human copy is parked (hidden) as before (control)", man.hides == 1 and w.parked["e_man"] ~= nil)
    KCD2MP_DogSyncSet("off")
    local dog2 = body("e_dog2", "Dog", 10, 0)
    KCD2MP_W131ParkReleased("e_dog2", "silence")
    check("E7: off: 0.60.7's park (hidden)", dog2.hides == 1 and w.parked["e_dog2"] ~= nil)
    KCD2MP_DogSyncSet("on")
    -- the guard's sweep: the released dog stays a paused dog and is never hidden
    CMDS = {}
    SPHERE = { dog }
    NOW = NOW + (w.repauseS or 5) + 1
    KCD2MP_W131Tick(true, true)
    check("E8: the guard's sweep runs and never hides it", w.active == true and dog.hides == 0 and dog.hidden == false)
    w.guard, w.joiner, w.shared, w.active = false, false, false, false
    KCD2MP.wo102.authorityHost = false
    w.parked, w.pausedAnimals = {}, {}
    noErrs("E")
end

-- (F) the enemy list (WO-147): a village dog's copy reads the game's relationship like a villager; a wolf's is -1
do
    KCD2MP.npcPuppets = { f_dog = {}, f_wolf = {} }
    body("f_dog", "Dog", 3, 0, 0.3)
    body("f_wolf", "Wolf", 4, 0, 0.3)
    local mark = #LOG
    KCD2MP_W147Hostiles("tok1")
    local l = lastLog("wo124_reply")
    check("F1: the dog reads its relationship (not an enemy)", l ~= nil and l:find("f_dog:0.30:", 1, true) ~= nil, l)
    check("F2: the wolf reads -1 (control)", l ~= nil and l:find("f_wolf:-1.00:", 1, true) ~= nil, l)
    KCD2MP.npcPuppets = {}
    noErrs("F")
end

-- (G) a stand-in for a dog this world lacks: the host's own soul, in class Dog
do
    KCD2MP_W136SoulFor("g_dog", "aaaaaaaa-0000-0000-0000-000000000001", "Dog")
    local soul, cls, kind = KCD2MP_W136StandInSpec("g_dog", true)
    check("G1: class Dog, the host's own soul", soul == "aaaaaaaa-0000-0000-0000-000000000001" and cls == "Dog" and kind == "animal", tostring(cls))
    KCD2MP_DogSyncSet("off")
    soul, cls = KCD2MP_W136StandInSpec("g_dog", true)
    check("G2: off: none (0.60.7)", soul == nil)
    KCD2MP_DogSyncSet("on")
    noErrs("G")
end

OUT = table.concat(RESULTS, "\n")
