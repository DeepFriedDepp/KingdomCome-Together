-- WO-136 synthetic test, against the real kdcmp.lua under MoonSharp.
-- Harness copied from Test-WO135Synthetic.lua.
--   (a) the load hold: nothing applied while a world loads, the newest replayed
--   (b) animal stand-ins: the host's own soul and class (Wolf, Boar ...)
--   (c) the rider owns the horse: released on mount, the host's again after
--   (d) the spawn preset comes off (the empty preset)
--   (e) the torch: the hand -> the state bit; the avatar holds the game's torch
-- What this proves: the Lua halves. Live evidence: docs/WO-136-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; SPAWNS = {}; CCMDS = {}; LOCKS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetCVar = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return SPHERE end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.DrawText = function(x, y, text, size) DRAWS[#DRAWS + 1] = { x = x, y = y, text = tostring(text) } end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Game.AddSaveLock = function(name, desc) if LOCKS[name] then return false end LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] == true; LOCKS[name] = nil; return had end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }
XGenAIModule = mkstub()
XGenAIModule.SpawnEntity = function(t) SPAWNS[#SPAWNS + 1] = t; ENTS[t.Name] = mkEntityLate(t.Name, t.Pos[1], t.Pos[2], t.Pos[3]) end

player = { id = 1, GetName = function(self) return "Dude" end,
           GetWorldPos = function() return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end } }

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return table.unpack(r)
end

-- @@KDCMP@@

-- Part 2: scenarios.

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function logCount(pat)
    local n = 0
    for _, l in ipairs(LOG) do if string.find(l, pat, 1, true) then n = n + 1 end end
    return n
end
local function lastLog(pat)
    for i = #LOG, 1, -1 do if string.find(LOG[i], pat, 1, true) then return LOG[i] end end
    return nil
end
local function clearLog() LOG = {} end
NEXTID = 5000
local function mkEntity(name, x, y, z)
    NEXTID = NEXTID + 1
    local e = { class = "NPC", id = NEXTID, px = x or 0, py = y or 0, pz = z or 0, rz = 0, dead = false, hp = 100 }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.GetWorldAngles = function(self) return { x = 0, y = 0, z = self.rz } end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.StartAnimation = function() end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end }
    e.human = { IsWeaponDrawn = function() return false end, IsInDialog = function() return false end }
    e.hidden = false
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.standups = 0
    e.actor.StandUp = function() e.standups = e.standups + 1 end
    e.actor.IsUnconscious = function() return e.ko == true end
    e.soul = { DealDamage = function() end }
    return e
end
mkEntityLate = mkEntity


local function cmdCount(pat)
    local n = 0
    for _, c in ipairs(CMDS) do if string.find(c, pat, 1, true) then n = n + 1 end end
    return n
end
local function reset()
    KCD2MP.npcPuppets = {}; KCD2MP.npcTracked = {}; KCD2MP.dragging = {}; KCD2MP.dragWatch = {}
    KCD2MP._npcPaused = {}; KCD2MP._npcResumePending = {}; KCD2MP._npcEverPaused = {}
    KCD2MP.ghosts = {}; KCD2MP.horseGhosts = {}
    KCD2MP.w131.parked = {}; KCD2MP.w131.standins = {}; KCD2MP.w131.active = false; KCD2MP.w131.guard = true
    KCD2MP.w131.reassert = false; KCD2MP.w131.standinLastAt = -1e9
    KCD2MP.wo102.authorityHost = true; KCD2MP.hitSensorOn = false
    KCD2MP.w122.sharedWorld = true
    ENTS = {}; SPHERE = {}; ERRS = {}; CMDS = {}; SPAWNS = {}; TOASTS = {}
end


-- ================================================================ WO-136
local function emits(name)
    local n = 0
    for _, l in ipairs(LOG) do if string.find(l, " " .. name .. " ", 1, true) then n = n + 1 end end
    return n
end

do -- (a) the load hold: queue the newest per NPC, replay once, expire
    reset(); clearLog(); NOW = 100
    KCD2MP.w136.hold = false; KCD2MP.w136.queue = {}; KCD2MP.w136.queueN = 0
    check("a: no hold at rest", KCD2MP_W136Held() == false)
    KCD2MP_W136Hold(true, 240, "load-join")
    check("a: a load holds", KCD2MP_W136Held() == true and logCount("WO136-HOLD on why=load-join") == 1)
    KCD2MP_W136Hold(true, 240, "load-join")
    check("a: holding twice logs once", logCount("WO136-HOLD on") == 1)
    local before = #SPAWNS
    KCD2MP_ApplyNpcState("ttkc_man_1", 1, 2, 3, 0, 100, 0, 0, 1, 10)
    KCD2MP_ApplyNpcState("ttkc_man_1", 4, 5, 6, 0, 90, 0, 0, 2, 20)
    KCD2MP_ApplyNpcState("prepadeniNaCeste_wolf_1", 7, 8, 9, 0, 100, 128, 0, 3, 30)
    check("a: under the hold nothing is applied or spawned", #SPAWNS == before and KCD2MP.npcPuppets["ttkc_man_1"] == nil)
    check("a: the newest sample per NPC waits", KCD2MP.w136.queueN == 2 and KCD2MP.w136.queue["ttkc_man_1"][2] == 4, KCD2MP.w136.queueN)
    local replayed = {}
    local real = KCD2MP_ApplyNpcState
    KCD2MP_ApplyNpcState = function(name, x) replayed[#replayed + 1] = name .. "@" .. tostring(x) end
    KCD2MP_W136Hold(false, 0, "gameplay+settle")
    KCD2MP_ApplyNpcState = real
    table.sort(replayed)
    check("a: released: each NPC replayed once, the newest", #replayed == 2 and replayed[2] == "ttkc_man_1@4", table.concat(replayed, ","))
    check("a: ...logged", logCount("WO136-HOLD off why=gameplay+settle") == 1 and logCount("replayed=2") == 1)
    check("a: released twice is a no-op", KCD2MP_W136Hold(false, 0, "again") == false)
    KCD2MP_W136Hold(true, 5, "load-test"); NOW = 106
    check("a: a hold nobody lifts expires (ttl)", KCD2MP_W136Held() == false and KCD2MP.w136.stats.expired >= 1)
    KCD2MP_W136Hold(true, 5, "load-test")
    clearLog(); KCD2MP_W131Tick(true, true)
    check("a: the copy guard does nothing under the hold", logCount("WO131") == 0)
    KCD2MP_W136Hold(false, 0, "done")
    check("a: no Lua errors", #ERRS == 0, ERRS[1])
end

do -- (b) animal stand-ins: the host's own soul and class
    reset(); clearLog(); NOW = 200
    KCD2MP.w136.souls = {}; KCD2MP.w136.soulAsked = {}; KCD2MP.w136.soulN = 0
    local s, c, k = KCD2MP_W136StandInSpec("prepadeniNaCeste_wolf_1", true)
    check("b: an unknown name asks the agent first", s == nil and emits("w136_soul") == 1)
    NOW = 201; s = KCD2MP_W136StandInSpec("prepadeniNaCeste_wolf_1", true)
    check("b: ...and waits (no second ask within 10 s)", s == nil and emits("w136_soul") == 1)
    KCD2MP_W136SoulFor("prepadeniNaCeste_wolf_1", "2981ff4d-d38f-4df8-a87a-0a0e2ab1b7fe", "Wolf")
    s, c, k = KCD2MP_W136StandInSpec("prepadeniNaCeste_wolf_1", true)
    check("b: the answer: the host's soul, class Wolf", s == "2981ff4d-d38f-4df8-a87a-0a0e2ab1b7fe" and c == "Wolf" and k == "animal", tostring(c) .. "/" .. tostring(k))
    KCD2MP_W136SoulFor("tzel_horse_1", "4c687543-c385-40f7-9597-be04db4caf4a", "Horse")
    check("b: a horse never gets a stand-in", KCD2MP_W136StandInSpec("tzel_horse_1", true) == nil)
    KCD2MP_W136SoulFor("crimeScene_boar_2", "", "")
    s, c, k = KCD2MP_W136StandInSpec("crimeScene_boar_2", true)
    check("b: unknown to the tables: a species soul by name", c == "Boar" and k == "animal", tostring(c))
    KCD2MP_W136SoulFor("oddcreature_1", "", "")
    check("b: a creature the file does not know: nothing", KCD2MP_W136StandInSpec("oddcreature_1", true) == nil)
    KCD2MP_W136SoulFor("tzel_man_6", "11111111-2222-3333-4444-555555555555", "NPC")
    s, c, k = KCD2MP_W136StandInSpec("tzel_man_6", false)
    check("b: a human the tables know: exact", c == "NPC" and k == "exact")
    NOW = 300; s = KCD2MP_W136StandInSpec("ttkc_unknown_9", false)
    check("b: an unknown human waits for the answer", s == nil)
    NOW = 300.5; s = KCD2MP_W136StandInSpec("ttkc_unknown_9", false)
    check("b: ...within the wait", s == nil)
    NOW = 302; s, c, k = KCD2MP_W136StandInSpec("ttkc_unknown_9", false)
    check("b: ...then WO-131's guess", c == "NPC" and string.sub(tostring(k), 1, 6) == "guess:", tostring(k))
    check("b: animal classes", KCD2MP_W136IsAnimalClass("Wolf") and KCD2MP_W136IsAnimalClass("WildDog") and not KCD2MP_W136IsAnimalClass("Horse"))
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

do -- (c) the rider owns the horse: release on mount, the host's again after the dismount
    reset(); clearLog(); NOW = 400
    KCD2MP.w136.ridden = {}
    local h = mkEntity("tzel_horse_1", 10, 10, 0); h.class = "Horse"; ENTS["tzel_horse_1"] = h; h.hidden = true
    KCD2MP.npcPuppets["tzel_horse_1"] = { dead = false, everPacket = true }
    local WUID = setmetatable({}, { __tostring = function() return "05000000000004D1" end })
    local mounted = false
    player.human = { IsMounted = function() return mounted end, GetHorse = function() return WUID end, IsInDialog = function() return false end }
    System.GetEntity = function(id) return nil end   -- GetHorse is a WUID, not an entity id (J1)
    XGenAIModule.GetEntityByWUID = function(w) if w == WUID then return h end end
    KCD2MP_W131Tick(true, true)
    check("c: not mounted: nothing", KCD2MP_W136MountedHorse() == nil)
    mounted = true
    check("c: mounted: the horse by its WUID", KCD2MP_W136MountedHorse() == "tzel_horse_1")
    CMDS = {}; KCD2MP_W136RideTick()
    check("c: the mount releases the copy: no puppet, brain back, shown",
        KCD2MP.npcPuppets["tzel_horse_1"] == nil and cmdCount("wh_ai_ResumeNPC tzel_horse_1") == 1 and h.hidden == false)
    check("c: ...told to the agent", logCount("w136_ride tzel_horse_1 1") == 1 and logCount("WO136-RIDE take npc=tzel_horse_1") == 1)
    local spawns = #SPAWNS
    KCD2MP_ApplyNpcState("tzel_horse_1", 50, 50, 0, 0, 100, 128, 0, 9, 90)
    check("c: while ridden the host's stream is ignored", KCD2MP.npcPuppets["tzel_horse_1"] == nil and h.px == 10 and #SPAWNS == spawns)
    KCD2MP_W136RideTick()
    check("c: riding on: taken once", KCD2MP.w136.stats.rides >= 1 and logCount("WO136-RIDE take") == 1)
    mounted = false; h.px = 30; KCD2MP_W136RideTick()
    check("c: the dismount starts the settle", logCount("WO136-RIDE dismount npc=tzel_horse_1") == 1 and KCD2MP_W136Ridden("tzel_horse_1") == true)
    NOW = 402; KCD2MP_W136RideTick()
    check("c: still the rider's inside the settle", KCD2MP_W136Ridden("tzel_horse_1") == true)
    NOW = 403.5; KCD2MP_W136RideTick()
    check("c: after 3 s the host's again", KCD2MP.w136.ridden["tzel_horse_1"] == nil and logCount("w136_ride tzel_horse_1 0") == 1)
    check("c: nothing teleported the horse back", h.px == 30)
    local own = mkEntity("my_own_horse", 0, 0, 0); ENTS["my_own_horse"] = own
    XGenAIModule.GetEntityByWUID = function() return own end
    mounted = true; clearLog(); KCD2MP_W136RideTick()
    check("c: an ordinary local ride is left alone", logCount("WO136-RIDE") == 0 and KCD2MP.w136.ridden["my_own_horse"] == nil)
    mounted = false; player.human = nil; System.GetEntity = function() return nil end
    check("c: no Lua errors", #ERRS == 0, ERRS[1])
end

do -- (d) the spawn preset comes off: the avatar wears only the partner's items
    reset(); clearLog()
    local av = mkEntity("kcd2mp_0", 1, 0, 0); ENTS["kcd2mp_0"] = av
    local presets = {}
    av.actor.EquipClothingPreset = function(self, p) presets[#presets + 1] = p end
    KCD2MP.ghosts["0"] = { entity = av }
    check("d: cleared with the empty preset", KCD2MP_W136ClearPreset("0", "test") == true and presets[1] == "dc000004-0000-0000-0000-000000000000")
    check("d: ...logged", logCount("WO136-OUTFIT clear id=0 why=test ok=true") == 1)
    check("d: no avatar: refused plainly", KCD2MP_W136ClearPreset("7", "test") == false and logCount("WO136-OUTFIT clear id=7 -- no avatar") == 1)
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

do -- (e) the torch: this player's hand -> the state bit; the avatar holds the game's torch
    reset(); clearLog(); NOW = 500
    KCD2MP.w136.torchLocal = false; KCD2MP.w136.avatarTorch = {}
    local inHand = { [0] = nil, [1] = nil }
    ItemManager = { GetItem = function(it) return it and { class = it.cls } or nil end }
    player.human = { GetItemInHand = function(self, hand) return inHand[hand] end, IsInDialog = function() return false end }
    KCD2MP_W136TorchTick()
    check("e: empty hands: nothing sent", emits("w136_torch") == 0)
    inHand[1] = { cls = "4cea28a0-0814-405a-bf24-4fd711f7eb63" }
    KCD2MP_W136TorchTick(); KCD2MP_W136TorchTick()
    check("e: a torch in the left hand: sent once", emits("w136_torch") == 1 and logCount("w136_torch 1") == 1)
    inHand[1] = { cls = "some-sword" }; KCD2MP_W136TorchTick()
    check("e: a sword is not a torch", logCount("w136_torch 0") == 1)
    local av = mkEntity("kcd2mp_0", 1, 0, 0); ENTS["kcd2mp_0"] = av
    local avHand, created, drawn, holstered = nil, 0, {}, {}
    local item = { cls = "4cea28a0-0814-405a-bf24-4fd711f7eb63" }
    local have = false
    av.inventory = { FindItem = function(self, c) return have and item or nil end,
                     CreateItem = function(self, c, n, q) created = created + 1; have = true end }
    av.human = { GetItemInHand = function(self, hand) return hand == 1 and avHand or nil end,
                 DrawFromInventory = function(self, it, hand, anim) drawn = { it, hand, anim }; avHand = it end,
                 HolsterToInventory = function(self, hand, anim) holstered = { hand, anim }; avHand = nil end }
    KCD2MP.ghosts["0"] = { entity = av }
    KCD2MP_W136AvatarTorch("0", true)
    check("e: the avatar gets the game's torch and draws it into the left hand", created == 1 and drawn[1] == item and drawn[2] == 1 and drawn[3] == true)
    check("e: ...logged", logCount("WO136-TORCH avatar id=0 on -> drawn into the left hand") == 1)
    KCD2MP_W136AvatarTorch("0", false)
    check("e: put away with (hand, animate)", holstered[1] == 1 and holstered[2] == true and logCount("id=0 off -> put away") == 1)
    KCD2MP_W136AvatarTorch("0", false)
    check("e: nothing in hand: said so", logCount("no torch in hand") == 1)
    player.human = nil
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

do -- (f) the console commands
    check("f: mp_w136_check and mp_w136_status registered", CCMDS["mp_w136_check"] ~= nil and CCMDS["mp_w136_status"] ~= nil)
    clearLog(); KCD2MP_W136Check("status")
    check("f: a check goes to the agent", logCount("w136_check status") == 1)
    KCD2MP_W136Status()
    check("f: the status line", logCount("WO136-STATUS hold=") == 1)
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
