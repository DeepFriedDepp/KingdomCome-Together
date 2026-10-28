-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-210 synthetic test: the Lua halves of WO-210's shared dialogue (F.1 the wheel and the mouse confirm, F.1b the frame rule, F.2a the
-- starter's cursor as a ring, F.2b the pick's second), each with its twin, against the real kdcmp.lua under MoonSharp (engine stubbed).

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

local function fireTimers()
    local t = TIMERS
    TIMERS = {}
    for _, x in ipairs(t) do pcall(x.f) end
end

-- (a) the switches: each its own console command, all on
do
    local n = 0
    for k, label in pairs(W210.NAMES) do
        n = n + 1
        check("a: " .. label .. " defaults on", KCD2MP_W210IsOn(k) == true)
        check("a: " .. label .. " is registered", CCMDS[label] ~= nil, label)
    end
    check("a: 13 switches", n == 13, n)
    check("a: mp_w210_status is registered", CCMDS["mp_w210_status"] ~= nil)
    clearLog()
    KCD2MP_W210Set("conv_hover", "off")
    check("a: a switch goes to the agent", evts("w210_cfg")[1] == "conv_hover=off", evts("w210_cfg")[1])
    KCD2MP_W210Set("conv_hover", "on")
end

-- (b) F.1: the listener's cursor follows the mouse wheel and the mouse button votes (session E: the host voted the top row every time)
local ui = {}
UIAction.CallFunction = function(panel, inst, fn, a, b, c) ui[#ui + 1] = { fn = tostring(fn), a = a, b = b, c = c } end
UIAction.SetArray = function(panel, inst, name, arr) ui[#ui + 1] = { fn = "SetArray:" .. tostring(name), arr = arr } end
UIAction.SetVisible = function(panel, inst, clip, v) ui[#ui + 1] = { fn = "SetVisible:" .. tostring(clip), a = v } end
local function last(fn) for i = #ui, 1, -1 do if ui[i].fn == fn then return ui[i] end end return nil end
local CSV = "0~ui_a___seq1~0~-~0~0;1~ui_a___seq2~0~skill_speech~2~0;2~=Leave~1~-~0~0"
local function listen(cid, dec)
    KCD2MP.W203.cam.listening = { starter = "1", cid = tostring(cid), anchor = "x", hidden = {} }
    return KCD2MP_W204Options(1, cid, dec, CSV)
end
do
    ERRS = {}; clearLog()
    check("b: options shown", listen(300, 1) == true)
    check("b: a wheel notch down moves the cursor", KCD2MP_W204OnAction("kcd2mp_listen_wheel_down", "press") == true and last("SetActiveSequenceChanged").a == 1)
    check("b: the mouse button votes the row under the cursor", KCD2MP_W204OnAction("kcd2mp_listen_confirm_mouse", "press") == true and evts("w204_vote")[1] == "1 300 1 1", evts("w204_vote")[1])
    check("b: a wheel notch up moves back", KCD2MP_W204OnAction("kcd2mp_listen_wheel_up", "press") == true and last("SetActiveSequenceChanged").a == 0)
    -- the twin: mp_listen_wheel off -- the wheel is counted, never acted on; the click votes the top row (the field's bug)
    KCD2MP_W210Set("listen_wheel", "off")
    listen(300, 2)
    KCD2MP_W204OnAction("kcd2mp_listen_wheel_down", "press")
    KCD2MP_W204OnAction("kcd2mp_listen_confirm_mouse", "press")
    check("b (twin): with mp_listen_wheel off the click votes row 0", evts("w204_vote")[2] == "1 300 2 0", evts("w204_vote")[2])
    KCD2MP_W210Set("listen_wheel", "on")
    -- the decision's key counts are told when it ends
    clearLog()
    listen(300, 3)
    KCD2MP_W204OnAction("kcd2mp_listen_wheel_down", "press")
    KCD2MP_W204OnAction("kcd2mp_listen_wheel_down", "press")
    KCD2MP_W204OnAction("kcd2mp_listen_confirm_mouse", "press")
    KCD2MP_W204Chosen(1, 300, 3, 2)
    check("b: the decision's keys are told (wheel 2, mouse confirm 1)", logCount("WO210-LISTEN keys") >= 1 and string.find(lastLog("WO210-LISTEN keys"), "wheel_down=2", 1, true) ~= nil
          and string.find(lastLog("WO210-LISTEN keys"), "confirm_mouse=1", 1, true) ~= nil, lastLog("WO210-LISTEN keys"))
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (c) F.1b: one press through two maps arrives twice in one frame (dropped); two wheel notches in two frames are two moves
do
    local frame = 100
    System.GetFrameID = function() return frame end
    listen(301, 1)
    W207.lastListen = {}; W207.lastListenFrame = {}
    check("c: the first arrival passes", KCD2MP_W207ListenOnce("kcd2mp_listen_wheel_down", "press") == false)
    check("c: the same frame again is the duplicate", KCD2MP_W207ListenOnce("kcd2mp_listen_wheel_down", "press") == true)
    frame = 101; NOW = NOW + 0.016
    check("c: the next frame's notch passes", KCD2MP_W207ListenOnce("kcd2mp_listen_wheel_down", "press") == false)
    -- the twin (mp_listen_wheel off): the old 120 ms rule drops the second notch 16 ms later
    KCD2MP_W210Set("listen_wheel", "off")
    W207.lastListen = {}
    KCD2MP_W207ListenOnce("kcd2mp_listen_wheel_down", "press")
    frame = 102; NOW = NOW + 0.016
    check("c (twin): with the old rule the second notch is dropped", KCD2MP_W207ListenOnce("kcd2mp_listen_wheel_down", "press") == true)
    KCD2MP_W210Set("listen_wheel", "on")
    System.GetFrameID = nil
end

-- (d) F.2a: the starter's cursor as a ring on the listener's row (his own cursor and vote left as they are)
do
    ERRS = {}; ui = {}; TIMERS = {}
    listen(302, 1)
    KCD2MP_W204OnAction("kcd2mp_listen_down", "press")
    check("d: a hover for the shown decision is taken", KCD2MP_W210Hover(1, 302, 1, 2) == true)
    fireTimers()
    local arr = last("SetArray:SequenceList")
    check("d: the starter's row carries the ring (kct_hover)", arr and arr.arr[13] == "kct_hover", arr and tostring(arr.arr[13]))
    check("d: the listener's own cursor stays on his row", last("SetActiveSequenceChanged").a == 1)
    check("d: a hover for another decision is refused", KCD2MP_W210Hover(1, 302, 9, 0) == false)
    KCD2MP_W204OnAction("kcd2mp_listen_confirm", "press")
    arr = last("SetArray:SequenceList")
    check("d: his vote and the ring on two rows: both shown", arr and arr.arr[8] == "kct_vote" and arr.arr[13] == "kct_hover", arr and (tostring(arr.arr[8]) .. "/" .. tostring(arr.arr[13])))
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (e) F.2b: the pick on its row for its second; the listener's cursor stays (the twin: it jumps to the pick, no pick mark)
do
    ERRS = {}; ui = {}; TIMERS = {}
    listen(303, 1)
    KCD2MP_W204OnAction("kcd2mp_listen_down", "press")
    KCD2MP_W204Chosen(1, 303, 1, 2)
    local arr = last("SetArray:SequenceList")
    check("e: the pick shows on its row (kct_pick)", arr and arr.arr[13] == "kct_pick", arr and tostring(arr.arr[13]))
    check("e: the listener's cursor stays on his row", last("SetActiveSequenceChanged").a == 1)
    check("e: the pick is told", evts("w210_pick_shown")[#evts("w210_pick_shown")] == "1 2")
    KCD2MP_W210Set("conv_pick_hold", "off")
    ui = {}
    listen(303, 2)
    KCD2MP_W204Chosen(1, 303, 2, 2)
    check("e (twin): with mp_conv_pick_hold off the cursor jumps to the pick and no pick mark is drawn", last("SetActiveSequenceChanged").a == 2 and last("SetArray:SequenceList") ~= nil
          and last("SetArray:SequenceList").arr[13] ~= "kct_pick")
    KCD2MP_W210Set("conv_pick_hold", "on")
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (f) H.5: the sun's game time here under a mark's id (the sky's own time of day)
do
    local was = System.GetCVar
    System.GetCVar = function(n) if n == "e_TimeOfDay" then return "6.5" end return "0" end
    LOG = {}
    KCD2MP_W210Sun("k3x9q2mzt1")
    local hit = false
    for _, l in ipairs(LOG) do if l:find("WO210-SUN mark=k3x9q2mzt1 sun=06:30", 1, true) then hit = true end end
    check("f: the sun's time is logged under the mark", hit, LOG[#LOG])
    System.GetCVar = function() return nil end
    LOG = {}
    KCD2MP_W210Sun("local")
    hit = false
    for _, l in ipairs(LOG) do if l:find("WO210-SUN mark=local sun=", 1, true) then hit = true end end
    check("f: an unreadable cvar still logs (sun -1)", hit, LOG[#LOG])
    System.GetCVar = was
    check("f: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (g) C.1 / C.2: the game's own loot test on a body, logged after a killing blow
do
    local wasPlayer = player
    local asked = nil
    player = { actor = { CanLoot = function(self, id) asked = id; return id == 7 end } }
    ENTS["w210_bandit"] = { id = 7, actor = { IsDead = function() return true end } }
    ENTS["w210_living"] = { id = 8, actor = { IsDead = function() return false end } }
    LOG = {}
    check("g: a body the game offers to loot", KCD2MP_W210DeathCheck("w210_bandit", "engine") == true and asked == 7)
    local hit = false
    for _, l in ipairs(LOG) do if l:find("WO210-DEATH check npc=w210_bandit how=engine dead=yes lootable=yes", 1, true) then hit = true end end
    check("g: the check is logged with how, dead and lootable", hit, LOG[#LOG])
    LOG = {}
    KCD2MP_W210DeathCheck("w210_living", "finished")
    hit = false
    for _, l in ipairs(LOG) do if l:find("dead=no lootable=no", 1, true) then hit = true end end
    check("g: a body the game does not offer is lootable=no", hit, LOG[#LOG])
    LOG = {}
    KCD2MP_W210DeathCheck("w210_nobody", "engine")
    hit = false
    for _, l in ipairs(LOG) do if l:find("dead=? lootable=?", 1, true) then hit = true end end
    check("g: no body: unknown, never a guess", hit, LOG[#LOG])
    ENTS["w210_bandit"] = nil; ENTS["w210_living"] = nil
    player = wasPlayer
    check("g: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (h) G.1: a rider no horse here can carry is put on the ground under the stream's saddle spot, never left in the air
do
    local wasT, wasSync = System.GetTerrainElevation, KCD2MP_NpcNativeSync
    System.GetTerrainElevation = function() return 14.0 end
    local unbound = nil
    KCD2MP_NpcNativeSync = function(name, p, e, on, why) unbound = why end
    local put = nil
    local e = { class = "NPC", SetWorldPos = function(self, v) put = v end }
    local p = { tx = 10, ty = 20, tz = 15.4 }
    LOG = {}
    check("h: a rider at saddle height with no horse is grounded", KCD2MP_W210RiderGround("dummyWanderer_cuman_1", p, e, "dummyWanderer_horse_6") == true
          and put and math.abs(put.z - 14.0) < 0.01 and unbound == "rider-ground")
    local hit = false
    for _, l in ipairs(LOG) do if l:find("WO210-RIDER ground npc=dummyWanderer_cuman_1", 1, true) then hit = true end end
    check("h: the grounding is logged", hit, LOG[#LOG])
    put = nil
    check("h: a stream already on the ground is left to the stream", KCD2MP_W210RiderGround("x", { tx = 1, ty = 1, tz = 14.2 }, e, "h") == false and put == nil)
    System.GetTerrainElevation, KCD2MP_NpcNativeSync = wasT, wasSync
    check("h: no Lua errors", #ERRS == 0, ERRS[1])
end

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
