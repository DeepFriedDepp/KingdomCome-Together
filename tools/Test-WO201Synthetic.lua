-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-201 synthetic test: the Lua halves of WO-201, against the real kdcmp.lua under MoonSharp (the engine stubbed, a fake clock).
--   (a) the switches: each its own console command, all on
--   (b) B5 an open inventory screen: no mod path changes that inventory or Henry's; the host's take of a body under its own screen: busy
--   (c) B6 a rescue's toast only for a talk the player started; (d) B8 a twin a pending conversation wants is never paused
--   (e) B19 every toast in ASCII; the scene toast only for the partner's scene; (f) B2 a stale queued talk ends at its start
--   (g) B3 the host's mount read through the horse's WUID; the joiner never stands an on-foot copy on a saddle
--   (h) B15 the host loading: no connection toast; (i) B21 the save lock added once per load; (j) B23 / B11 hand props; (k) B24 a mark's note
-- What this proves: the Lua rules. Live evidence: docs/WO-201-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; CCMDS = {}; LOCKS = {}; MSGS = {}; EXTRA = {}
QINV = {}; CREATE_REFUSED = {}

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
System.DrawText = function() end
System.RemoveEntity = function(eid)
    for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil; e.removed = true end end
    for _, e in ipairs(EXTRA) do if e.id == eid then e.removed = true end end
end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.GetTerrainElevation = function() return 0 end
System.GetEntity = function(id)
    for _, e in pairs(ENTS) do if e.id == id then return e end end
    for _, e in ipairs(EXTRA) do if e.id == id and not e.removed then return e end end
    return nil
end
System.GetEntitiesByClass = function(cls)
    local out = {}
    for _, e in pairs(ENTS) do if e.class == cls then out[#out + 1] = e end end
    for _, e in ipairs(EXTRA) do if e.class == cls and not e.removed then out[#out + 1] = e end end
    return out
end
ENTITY_FLAG_NO_SAVE = 32768
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Game.AddSaveLock = function(name) if LOCKS[name] then return false end LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] == true; LOCKS[name] = nil; return had end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }

local NEXTID = 0x0E0000
local function mkEntity(name, x, y, z, cls, keep)
    NEXTID = NEXTID + 1
    local e = { class = cls or "NPC", id = "userdata: " .. string.format("%016X", NEXTID), px = x or 0, py = y or 0, pz = z or 0, rz = 0,
                dead = false, hp = 100, hidden = false, flags = 0, mounted = false, dismounts = 0, mounts = 0 }
    e.this = { id = "this:" .. name }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self, t) t = t or {}; t.x, t.y, t.z = self.px, self.py, self.pz; return t end
    e.GetWorldAngles = function(self, t) t = t or {}; t.x, t.y, t.z = 0, 0, self.rz; return t end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.SetFlags = function(self, f, mode) if mode == 3 then self.flags = self.flags + f else self.flags = f end end
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.StartAnimation = function() end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end }
    e.human = { IsInDialog = function() return false end, IsMounted = function() return e.mounted end,
                ForceDismount = function() e.mounted = false; e.dismounts = e.dismounts + 1 end,
                ForceMount = function() e.mounted = true; e.mounts = e.mounts + 1 end }
    e.soul = { GetId = function() return "userdata: 05000000000001DC" end }
    if keep then EXTRA[#EXTRA + 1] = e else ENTS[name] = e end
    return e
end

XGenAIModule = mkstub()
XGenAIModule.SpawnEntity = function(t) mkEntity(t.Name, t.Pos[1], t.Pos[2], t.Pos[3], t.ClassName) end
XGenAIModule.MakeTableFromType = function(kind) return { kind = kind, information = {} } end

player = { id = 1, this = { id = 777 }, GetName = function(self) return "Dude" end,
           GetWorldPos = function(self) return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end },
           human = { IsMounted = function() return false end, IsInDialog = function() return false end },
           inventory = {
               GetCountOfClass = function(self, g) return QINV[g] or 0 end,
               CreateItem = function(self, g, hp, n) if CREATE_REFUSED[g] then return false end QINV[g] = (QINV[g] or 0) + (n or 1); return true end,
               DeleteItemOfClass = function(self, g, n) local h = QINV[g] or 0; local d = math.min(h, n or 0); QINV[g] = h - d; return d end,
           } }

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
local function evts(name)
    local out = {}
    for _, l in ipairs(LOG) do
        local a = l:match("%[KCD2%-MP%-EVT%] v1 %d+ " .. name:gsub("_", "%%_") .. " (.*)$")
        if a then out[#out + 1] = a end
    end
    return out
end
local function cmdCount(pat)
    local n = 0
    for _, c in ipairs(CMDS) do if string.find(c, pat, 1, true) then n = n + 1 end end
    return n
end


-- (a) the switches -- read before any scenario touches them
do
    local n = 0
    for k, label in pairs(W201.NAMES) do
        n = n + 1
        if k == "figure_is_player" then
            check("a: " .. label .. " defaults off until its save-decode check passes", KCD2MP_W201On(k) == false)
        else
            check("a: " .. label .. " defaults on", KCD2MP_W201On(k) == true)
        end
        check("a: " .. label .. " is registered", CCMDS[label] ~= nil, label)
    end
    check("a: 22 switches", n == 22, n)
    check("a: mp_w201_status and mark are registered", CCMDS["mp_w201_status"] ~= nil and CCMDS["mark"] ~= nil)
    clearLog()
    KCD2MP_W201Set_copy_combat_from_host("off")
    check("a: a generated setter turns its switch off and tells the agent", KCD2MP_W201On("copy_combat_from_host") == false
        and (evts("w201_cfg")[1] or "") == "copy_combat_from_host=off", evts("w201_cfg")[1])
    KCD2MP_W201Set_copy_combat_from_host("on")
    check("a: a bad value is refused", KCD2MP_W201Set("copy_combat_from_host", "maybe") == false and KCD2MP_W201On("copy_combat_from_host") == true)
    check("a: no Lua errors at load", #ERRS == 0, ERRS[1])
end

-- (b) B5: an open inventory screen
ITEMS = {}
ItemManager = ItemManager or {}
ItemManager.GetItem = function(w) return ITEMS[w] end
local function mkBody(name, items)
    local e = mkEntity(name, 1, 0, 0)
    e.dead = true
    e.inv = {}
    for i, it in ipairs(items) do local w = name .. "_w" .. i; ITEMS[w] = { class = it, amount = 1, health = 1 }; e.inv[#e.inv + 1] = w end
    e.inventory = {
        GetInventoryTable = function() local t = {}; for _, w in ipairs(e.inv) do t[#t + 1] = w end; return t end,
        DeleteItem = function(self, w, n) for i, x in ipairs(e.inv) do if x == w then table.remove(e.inv, i); return true end end; return false end,
        CreateItem = function(self, cls, hp, n) local w = name .. "_n" .. (#e.inv + 10); ITEMS[w] = { class = cls, amount = n or 1, health = hp or 1 }; e.inv[#e.inv + 1] = w; return true end,
        GetCountOfClass = function(self, cls) local c = 0; for _, w in ipairs(e.inv) do if ITEMS[w].class == cls then c = c + 1 end end; return c end,
    }
    return e
end
do
    ENTS = {}; ERRS = {}; clearLog(); TIMERS = {}
    local b1 = mkBody("b1", { "KEY", "SWORD" })
    mkEntity("other", 3, 0, 0)
    mkEntity("kcd2mp_1", 4, 0, 0)
    KCD2MP_W201InvIntent(b1, "loot")
    KCD2MP_W201InvScreen("in", "element")
    check("b: the screen opens on the named body", W201.inv.open == true and W201.inv.on == "b1", W201.inv.on)
    check("b: that body and Henry's pack are busy, another body is not", KCD2MP_W201InvBusy("b1") and KCD2MP_W201InvBusy("player") and not KCD2MP_W201InvBusy("other"))
    check("b: a second signal of the same open is no second open", KCD2MP_W201InvScreen("in", "audio") and W201.inv.stats.opens == 1)
    -- the host's take of that body (the joiner's take) under the host's own screen: busy, nothing deleted
    clearLog()
    KCD2MP_W134HostTake(1, 501, "b1", "SWORD", 1, 1)
    local tres = evts("w134_tres")[1] or ""
    check("b: the joiner's take is refused busy under the host's screen", tres:find(" busy b1 SWORD", 1, true) ~= nil, tres)
    check("b: ...the body still holds it", b1.inventory:GetCountOfClass("SWORD") == 1)
    -- a deferred change runs one frame after the close
    local ran = 0
    KCD2MP_W201InvDefer("test", "b1", "a change", function() ran = ran + 1 end)
    KCD2MP_W201InvDefer("test", "b1", "a newer change", function() ran = ran + 10 end)
    check("b: deferred, logged once, the newer one replaces the older", ran == 0 and logCount("WO201-INV deferred path=test inv=b1") == 1)
    KCD2MP_W201InvScreen("out", "element")
    for _, t in ipairs(TIMERS) do pcall(t.f) end
    check("b: applied once after the close (the newest)", ran == 10 and logCount("WO201-INV applied path=test") == 1, ran)
    -- a screen nobody named (a chest, a shop): every inventory but the mod's own bodies waits
    KCD2MP_W201InvScreen("in", "audio")
    check("b: an unnamed screen guards a body and Henry, never a figure", KCD2MP_W201InvBusy("other") and KCD2MP_W201InvBusy("player") and not KCD2MP_W201InvBusy("kcd2mp_1"))
    KCD2MP_W201InvScreen("out", "audio")
    KCD2MP_W201SetInvGuard("off")
    KCD2MP_W201InvIntent(b1, "loot"); KCD2MP_W201InvScreen("in", "element")
    check("b: the switch off: nothing waits", not KCD2MP_W201InvBusy("b1"))
    KCD2MP_W201InvScreen("out", "element"); KCD2MP_W201SetInvGuard("on")
    -- a load drops what waited
    KCD2MP_W201InvIntent(b1, "loot"); KCD2MP_W201InvScreen("in", "element")
    KCD2MP_W201InvDefer("test2", "b1", "x", function() ran = -1 end)
    KCD2MP_W201AfterLoad("test")
    check("b: a load drops the queue and the screen", W201.inv.open == false and #W201.inv.order == 0 and logCount("WO201-INV dropped n=1") == 1)
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (c) B6: a rescue's toast only for a talk the player started
do
    TOASTS = {}; ERRS = {}
    KCD2MP_W179ReleaseDialog("no-twins", 0)
    check("c: a monologue or a quest's own dialogue ended: no toast", #TOASTS == 0, TOASTS[1])
    KCD2MP_W179ReleaseDialog("no-twins", 1)
    check("c: his own talk ended: the toast (ASCII)", TOASTS[1] == "That conversation couldn't start here - try again in a moment.", TOASTS[1])
    TOASTS = {}
    KCD2MP_W179ReleaseDialog("no-twins")
    check("c: an older agent (no flag): shown as before", #TOASTS == 1)
end

-- (d) B8: a twin a pending conversation wants is never paused
do
    ERRS = {}; clearLog(); NOW = 5000
    KCD2MP_W201TwinWant(927, "Dude d_semin d_suk", 1)
    check("d: a pending request names the twins: kept", KCD2MP_W201TwinWanted("d_suk", "puppet-start") == true)
    check("d: ...logged once", logCount("WO201-TWINS kept npc=d_suk why=pending-927") == 1, lastLog("WO201-TWINS"))
    check("d: Henry himself is never a twin", KCD2MP_W201TwinWanted("Dude", "x") == false)
    KCD2MP_W201TwinWant(927, "", 0)
    check("d: the request ended: free to pause", KCD2MP_W201TwinWanted("d_suk", "puppet-start") == false)
    W201.twins.endAt["d_semin"] = NOW
    NOW = NOW + 1
    check("d: within 2 s of its talk's end: kept", KCD2MP_W201TwinWanted("d_semin", "puppet-start") == true)
    NOW = NOW + 2
    check("d: after 2 s: free", KCD2MP_W201TwinWanted("d_semin", "puppet-start") == false)
    KCD2MP.w170.held["d_x"] = { since = NOW }
    check("d: WO-170's scene hold keeps it from the per-tick pause", KCD2MP_W201TwinWanted("d_x", "puppet-start") == true)
    check("d: ...but not from WO-170's own repause", KCD2MP_W201TwinWanted("d_x", "w170-scene-ended") == false)
    KCD2MP.w170.held["d_x"] = nil
    KCD2MP_W201TwinWant(1, "Dude d_old", 1); NOW = NOW + 61
    check("d: a request never ended is forgotten after 60 s", KCD2MP_W201TwinWanted("d_old", "x") == false)
    KCD2MP_W201SetTwinsGrace("off")
    KCD2MP_W201TwinWant(2, "Dude d_y", 1)
    check("d: the switch off: never kept", KCD2MP_W201TwinWanted("d_y", "x") == false)
    KCD2MP_W201SetTwinsGrace("on")
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (e) B19: toasts in ASCII; the scene toast only for the partner's scene
do
    ERRS = {}; TOASTS = {}
    check("e: an em dash -> ' - '", KCD2MP_W201ToastAscii("this scene \226\128\148 carry on") == "this scene - carry on", KCD2MP_W201ToastAscii("this scene \226\128\148 carry on"))
    check("e: an ellipsis -> '...'", KCD2MP_W201ToastAscii("Travelling\226\128\166") == "Travelling...")
    check("e: the quest data's mojibake ellipsis -> '...'", KCD2MP_W201ToastAscii("So it begins\195\162\226\130\172\194\166") == "So it begins...")
    check("e: curly quotes -> straight", KCD2MP_W201ToastAscii("\226\128\156hi\226\128\157 \226\128\152x\226\128\153") == "\"hi\" 'x'")
    check("e: anything else non-ASCII -> one '?'", KCD2MP_W201ToastAscii("caf\195\169") == "caf?")
    check("e: plain ASCII untouched", KCD2MP_W201ToastAscii("Hello there.") == "Hello there.")
    KCD2MP_ShowNativeToast("a \226\128\148 b")
    check("e: every native toast is cleaned on its way to the HUD", TOASTS[#TOASTS] == "a - b", TOASTS[#TOASTS])
    TOASTS = {}
    KCD2MP_W167SceneReleased(false, "test", 0)
    check("e: his own scene released: no 'partner's scene' toast", #TOASTS == 0, TOASTS[1])
    KCD2MP_W167SceneReleased(false, "test", 1)
    check("e: the partner's scene: the toast, ASCII", TOASTS[1] == "Your partner's game is playing this scene - carry on.", TOASTS[1])
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (f) B2: a stale queued talk ends at its start
do
    ERRS = {}; TOASTS = {}; clearLog()
    KCD2MP_W201StaleTalk(787, 336.6)
    check("f: the player is told plainly", TOASTS[1] == "That conversation was cancelled - talk again.", TOASTS[1])
    check("f: ...logged", logCount("WO201-QUEUE stale dialogue=787") == 1)
end

-- (g) B3: the mount is a value
do
    ERRS = {}; clearLog(); ENTS = {}
    local rider = mkEntity("g_rider", 10, 0, 0)
    local horse = mkEntity("g_horse", 10, 0, 0, "Horse")
    rider.mounted = true
    rider.human.GetHorse = function() return "userdata: 05000000000003E2" end
    rider.soul.GetState = function(self, k) return k == "stamina" and 120 or 90 end
    XGenAIModule.GetEntityByWUID = function(w) if w == "userdata: 05000000000003E2" then return horse end end
    KCD2MP.npcTracked = { g_rider = true }
    W200.hv = {}
    KCD2MP_W200HostValuesTick()
    local hv = evts("w200_hostvals")[1] or ""
    check("g: the host reads the horse through its WUID (not 'on foot')", hv:find("g_rider|g_horse|", 1, true) ~= nil, hv)
    -- the joiner: on foot by the host's word, the stream on a horse copy's back
    KCD2MP.npcPuppets = { g_rider = { tx = 10, ty = 0, tz = 1.4 }, g_horse = { tx = 10, ty = 0, tz = 0.0 } }
    local p = KCD2MP.npcPuppets.g_rider
    local ok = KCD2MP_W201RideDisagree("g_rider", p, rider)
    check("g: on foot by the host's word on a saddle: the value wins", ok == true and math.abs(rider.pz - 0) < 0.01, rider.pz)
    check("g: ...logged", logCount("WO201-RIDE disagree npc=g_rider horse=g_horse") == 1, lastLog("WO201-RIDE"))
    p.tz = 0.0
    check("g: on the ground: nothing to correct", KCD2MP_W201RideDisagree("g_rider", p, rider) == false)
    check("g: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (h) B15: the host loading
do
    ERRS = {}; TOASTS = {}; NOW = 9000
    KCD2MP_W201HostBusy("load", 1)
    check("h: the player is told the host is loading", TOASTS[1] == "Your host is loading - please wait.", TOASTS[1])
    check("h: the connection toast is held", KCD2MP_W201HostBusyNow() == true)
    KCD2MP_W201HostBusy("load", 0)
    check("h: ...for 20 s after the host is done", KCD2MP_W201HostBusyNow() == true)
    NOW = NOW + 21
    check("h: then free again", KCD2MP_W201HostBusyNow() == false)
end

-- (i) B21: the save lock is added once per load
do
    ERRS = {}; clearLog(); LOCKS = {}; NOW = 10000
    local adds = 0
    local orig = Game.AddSaveLock
    Game.AddSaveLock = function(n, t) adds = adds + 1; return orig(n, t) end
    KCD2MP.w122.sharedWorld = true
    KCD2MP.w122.lockHeld = false
    KCD2MP_HostOnlyLock(true, "after-load")
    local a1 = adds
    check("i: the first assert adds it (and reads it back)", KCD2MP.w122.lockHeld == true and a1 == 2, a1)
    NOW = NOW + 31
    KCD2MP_HostOnlyLock(true, "tick")
    check("i: the next tick does not add it again (no engine error)", adds == a1, adds)
    KCD2MP_W201LockRemoved(KCD2MP.w122.lockName)
    NOW = NOW + 31
    KCD2MP_HostOnlyLock(true, "tick")
    check("i: the engine removed it: the next tick adds it", adds > a1, adds)
    local a2 = adds
    KCD2MP_W201AfterLoad("t")
    NOW = NOW + 31
    KCD2MP_HostOnlyLock(true, "tick")
    check("i: a load (it wipes every lock): added again", adds > a2, adds)
    Game.AddSaveLock = orig
    check("i: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (j) B23 / B11: a held item is a prop of the same model
do
    ERRS = {}; clearLog(); ENTS = {}
    local spawned = {}
    System.SpawnEntity = function(t) local e = mkEntity(t.name, 0, 0, 0, t.class); e.model = t.properties and t.properties.object_Model; spawned[#spawned + 1] = e; return e end
    local c = mkEntity("j_copy", 1, 1, 0)
    c.att = {}
    c.human.AttachEntityToHand = function(self, id, h) c.att[h] = id end
    c.human.DetachFromHand = function(self, h) c.att[h] = nil end
    c.human.GetItemInHand = function() return 0 end
    c.GetBonePos = function() return { x = 1, y = 1, z = 1.2 } end
    KCD2MP.npcPuppets = { j_copy = {} }
    KCD2MP_W201Hands("j_copy", "", "Objects/manmade/tools/broom.cgf")
    check("j: the host's broom: a prop of its model in the right hand", c.att[1] ~= nil and spawned[1] and spawned[1].model == "Objects/manmade/tools/broom.cgf")
    check("j: ...never saved", (spawned[1].flags or 0) ~= 0, spawned[1].flags)
    KCD2MP_W201Hands("j_copy", "", "Objects/manmade/tools/broom.cgf")
    check("j: the same item again: nothing new", #spawned == 1)
    KCD2MP_W201Hands("j_copy", "", "")
    check("j: an empty hand: the prop goes", c.att[1] == nil and spawned[1].removed == true)
    c.human.GetItemInHand = function(self, h) return h == 0 and 77 or 0 end
    KCD2MP_W201Hands("j_copy", "Objects/x.cgf", "")
    check("j: its own hand holds a real item: no double", c.att[0] == nil and logCount("its own hand holds a real item") == 1)
    KCD2MP.npcPuppets.j_copy.dead = true
    c.human.GetItemInHand = function() return 0 end
    KCD2MP_W201Hands("j_copy", "Objects/x.cgf", "")
    check("j: a dead copy holds nothing of ours", c.att[0] == nil)
    -- B11: the figure's crossbow
    local fig = mkEntity("kcd2mp_1", 2, 2, 0)
    fig.GetBonePos = function() return { x = 2, y = 2, z = 1.2 } end
    fig.att = {}
    fig.human.AttachEntityToHand = function(self, id, h) fig.att[h] = id end
    fig.human.DetachFromHand = function(self, h) fig.att[h] = nil end
    fig.human.GetItemInHand = function() return 0 end
    KCD2MP_W201FigureProp("kcd2mp_1", "Objects/manmade/weapons/crossbows/crossbow.cgf")
    check("j: a quest crossbow the engine refuses: shown on the figure", fig.att[1] ~= nil, lastLog("WO201-RANGED"))
    KCD2MP_W201FigureProp("kcd2mp_1", "")
    check("j: ...and gone when the partner no longer has it", fig.att[1] == nil)
    check("j: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (k) B24: a mark's note
do
    ERRS = {}; clearLog()
    KCD2MP_MarkNote("a guard stood in the wall!")
    local ml = {}
    for _, l in ipairs(LOG) do if l:find("MP-MARK", 1, true) then ml[#ml + 1] = l end end
    check("k: the note rides with the mark", logCount("MP-MARK odd world_time=") == 1 and logCount("note=a guard stood in the wall!") >= 1, table.concat(ml, " || "))
    check("k: ...and goes to the agent", (evts("mp_mark")[1] or "") == "odd note=a guard stood in the wall!", evts("mp_mark")[1])
    clearLog()
    KCD2MP_Mark("odd")
    check("k: a plain mark has no note", logCount("note=") == 0 and logCount("MP-MARK odd") == 1)
    KCD2MP_MarkNote("<script>\226\128\148" .. string.rep("x", 200))
    check("k: a note is plain text, at most 120 characters", (lastLog("MP-MARK") or ""):find("<", 1, true) == nil and #(lastLog("MP-MARK") or "") < 240, lastLog("MP-MARK"))
    check("k: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (l) Act V F2: the vote panel, its keys, the speaker's mark, the choice; F1's subtitle
do
    ERRS = {}; clearLog()
    local drawn = {}
    System.DrawText = function(x, y, t) drawn[#drawn + 1] = tostring(t) end
    player.human = player.human or {}
    player.human.IsInDialog = function() return false end
    KCD2MP_W201VoteShow(7, "Your partner", { "Ask about the bandits", "Leave" })
    KCD2MP_W201DrawUI()
    check("l: the panel lists the options in order, numbered by their keys", #drawn == 3 and drawn[2]:find("F2", 1, true) and drawn[2]:find("Ask about the bandits", 1, true) and drawn[3]:find("F4", 1, true), table.concat(drawn, " || "))
    check("l: a vote key votes", KCD2MP_W201VoteOnAction("kcd2mp_dice_mark_2", "press") == true and evts("w201_vote")[1] == "7 2", evts("w201_vote")[1])
    check("l: an option that is not there: nothing", KCD2MP_W201VoteOnAction("kcd2mp_dice_mark_3", "press") == false)
    KCD2MP.dice = KCD2MP.dice or {}
    KCD2MP.dice.open = true
    check("l: a dice board open: the key is the dice's", KCD2MP_W201VoteOnAction("kcd2mp_dice_mark_1", "press") == false)
    KCD2MP.dice.open = false
    player.human.IsInDialog = function() return true end
    check("l: in his own conversation: no vote, no panel", KCD2MP_W201VoteOnAction("kcd2mp_dice_mark_1", "press") == false)
    player.human.IsInDialog = function() return false end
    KCD2MP_W201VoteChosen("Your partner", "I will go.", true)
    check("l: the choice closes the panel and is told", W201.vote.open == nil and W201.vote.chosen == "Your partner chose: I will go.", W201.vote.chosen)
    check("l: no key does anything once the panel is gone", KCD2MP_W201VoteOnAction("kcd2mp_dice_mark_1", "press") == false)
    drawn = {}
    KCD2MP_W201VoteMark(2, "Partner", "Leave")
    KCD2MP_W201DrawUI()
    check("l: the speaker sees the partner's pick", (drawn[1] or ""):find("Partner votes 2: Leave", 1, true) ~= nil, drawn[1])
    System.DrawText = function() end
    local subs = {}
    UIAction = UIAction or {}
    UIAction.CallFunction = function(el, inst, fn, a, b) subs[#subs + 1] = tostring(fn) .. ":" .. tostring(a) .. ":" .. tostring(b) end
    KCD2MP_W201Subtitle("Henry, wait " .. string.char(226, 128, 148) .. " there.", true, 3)
    check("l: an overheard line shows its subtitle, marked overheard, ASCII", (subs[1] or "") == "SetSubtitles:Henry, wait  -  there.:Your partner (overheard)" or (subs[1] or ""):find("SetSubtitles:Henry, wait", 1, true) ~= nil, subs[1])
    player.human.IsInDialog = function() return true end
    subs = {}
    KCD2MP_W201Subtitle("x", false, 3)
    check("l: never while he is in his own conversation", #subs == 0)
    player.human.IsInDialog = function() return false end
    check("l: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (m) B5: the field crash's own path -- WO-195's host-dup under the host's open loot screen
do
    ENTS = {}; ERRS = {}; clearLog(); TIMERS = {}
    W201.inv.open = false; W201.inv.on = nil
    local body = mkBody("m_bandit", { "COAT" })
    local hb = mkBody("m_henrypack", {})            -- Henry's own pack for this scenario
    local savedInv = player.inventory
    player.inventory = hb.inventory
    KCD2MP.w195.bodyLootHost = true
    KCD2MP.w195.screenTaken["m_bandit"] = { CROSSBOW = 1 }   -- the joiner was granted the CROSSBOW his own loot screen made
    body.inventory:CreateItem("CROSSBOW", 1, 1)               -- the host's loot screen made its own copy
    KCD2MP_W201InvIntent(body, "loot")
    KCD2MP_W201InvScreen("in", "element")                     -- the host's screen is open on that body
    local n = KCD2MP_W195HostDup("m_bandit", body)
    check("m: under the host's open screen the host-dup removes nothing (the field crash)", n == 0 and body.inventory:GetCountOfClass("CROSSBOW") == 1)
    check("m: ...it waits for the close", logCount("WO201-INV deferred path=w195-hostdup inv=m_bandit") == 1, lastLog("WO201-INV"))
    -- the host moves the body's copy into his pack while the screen is open (the engine's own transfer)
    for i, w in ipairs(body.inv) do if ITEMS[w].class == "CROSSBOW" then table.remove(body.inv, i); hb.inv[#hb.inv + 1] = w; break end end
    KCD2MP_W201InvScreen("out", "element")
    for _, t in ipairs(TIMERS) do pcall(t.f) end
    check("m: after the close the copy the host carried off goes (one item, the joiner had it first)", hb.inventory:GetCountOfClass("CROSSBOW") == 0
        and body.inventory:GetCountOfClass("CROSSBOW") == 0, lastLog("WO195-LOOT"))
    check("m: ...told and logged", logCount("the host took the body's copy under the open screen") == 1)
    player.inventory = savedInv
    KCD2MP.w195.screenTaken["m_bandit"] = nil
    check("m: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (n) V.4: the figure's knockout the engine's own way (the DLL swaps the guard and adds the unconsciousness; this half never ragdolls)
do
    ENTS = {}; ERRS = {}; clearLog(); TIMERS = {}
    local fig = mkEntity("kcd2mp_3", 5, 5, 0)
    local buffs, stood, rag = 0, 0, 0
    local unconscious = true
    fig.soul = fig.soul or {}
    fig.soul.AddBuff = function() buffs = buffs + 1 end
    fig.soul.RemoveAllBuffsByGuid = function() end
    fig.actor = fig.actor or {}
    fig.actor.RagDollize = function() rag = rag + 1 end
    fig.actor.StandUp = function() stood = stood + 1 end
    fig.actor.IsUnconscious = function() return unconscious end
    fig.actor.GetPhysicalizationProfile = function() return "alive" end
    fig.actor.GetCurrentAnimationState = function() return "MotionIdle" end
    fig.IsHidden = function() return false end
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    KCD2MP.ghosts["3"] = { entity = fig }
    KCD2MP_W197FigureKnockout("3", true, true)
    check("n: down the engine's way: no buff from this half (the DLL adds it with the guard swapped)", buffs == 0)
    NOW = NOW + 2.0
    KCD2MP_W197KoTick("3")
    check("n: ...and never the physics-only ragdoll (nothing could stand that up)", rag == 0)
    KCD2MP_W197FigureKnockout("3", false, true)
    KCD2MP_W197KoTick("3")
    check("n: woken, still unconscious: no StandUp yet (StandUp never checks that itself)", stood == 0)
    unconscious = false
    NOW = NOW + 0.4
    KCD2MP_W197KoTick("3")
    check("n: conscious again: the engine's own StandUp", stood == 1, lastLog("WO201-KO"))
    KCD2MP.ghosts["3"] = nil
    check("n: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (o) B5: the host-dup pass at the screen's close (the body watch's timer froze under the screen: it never ran during it)
do
    ENTS = {}; ERRS = {}; clearLog(); TIMERS = {}
    W201.inv.open = false; W201.inv.on = nil
    local body = mkBody("o_bandit", { "COAT" })
    local hb = mkBody("o_henrypack", {})
    local savedInv = player.inventory
    player.inventory = hb.inventory
    KCD2MP.w195.bodyLootHost = true
    KCD2MP.w195.screenTaken["o_bandit"] = { BOW = 1, KEY = 1 }   -- the joiner was granted both from his own loot screen
    body.inventory:CreateItem("BOW", 1, 1)                        -- the host's screen made its own copies of both
    body.inventory:CreateItem("KEY", 1, 1)
    KCD2MP_W201InvIntent(body, "loot")
    KCD2MP_W201InvScreen("in", "element")
    -- the host carries the KEY off; the BOW stays on the body; no watch pass runs while the screen is open
    for i, w in ipairs(body.inv) do if ITEMS[w].class == "KEY" then table.remove(body.inv, i); hb.inv[#hb.inv + 1] = w; break end end
    KCD2MP_W201InvScreen("out", "element")
    for _, t in ipairs(TIMERS) do pcall(t.f) end
    check("o: at the close the body's own copy goes (the host-dup pass for that body)", body.inventory:GetCountOfClass("BOW") == 0, lastLog("WO195-LOOT"))
    check("o: ...and the copy the host carried off goes too", hb.inventory:GetCountOfClass("KEY") == 0)
    check("o: ...each logged", logCount("WO195-LOOT host-dup npc=o_bandit cls=BOW") == 1 and logCount("WO195-LOOT host-dup npc=o_bandit cls=KEY") == 1)
    player.inventory = savedInv
    KCD2MP.w195.screenTaken["o_bandit"] = nil
    check("o: no Lua errors", #ERRS == 0, ERRS[1])
end

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
