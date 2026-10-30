-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-211 synthetic test: the Lua halves of WO-211 (the switches, the link hold, the co-op menu's Leave, the "reconnecting" badge,
-- the flight gate, the joint crime notice), against the real kdcmp.lua under MoonSharp (engine stubbed).

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

-- (a) the switches: each ships on but the flight; a flip is sent to the agent; every one has its console command
do
    LOG = {}
    check("a: mp_link_hold ships on", KCD2MP_W211IsOn("link_hold") == true)
    check("a: mp_allow_fly ships off", KCD2MP_W211IsOn("allow_fly") == false)
    check("a: a flip is sent to the agent", KCD2MP_W211Set("overflow_reseat", "off") == true and logCount("w211_cfg") >= 1 and KCD2MP_W211IsOn("overflow_reseat") == false)
    KCD2MP_W211Set("overflow_reseat", "on")
    check("a: a bad word is refused", KCD2MP_W211Set("link_hold", "maybe") == false)
    local all = true
    for _, label in pairs(W211.NAMES) do if not CCMDS[label] then all = false end end
    check("a: every switch has its console command", all and CCMDS["mp_w211_status"] ~= nil and CCMDS["mp_leave"] ~= nil)
    check("a: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (b) A.1: the hold -- the clock still, the movement held the actionmap way (the co-op menu still works), the line; off gives all back
do
    LOG = {}; TOASTS = {}
    local maps, ratios = {}, {}
    ActionMapManager = { EnableActionMap = function(name, on) maps[#maps + 1] = tostring(name) .. "=" .. tostring(on) end }
    Calendar.SetWorldTimeRatio = function(r) ratios[#ratios + 1] = r end
    check("b: the hold starts", KCD2MP_W211LinkHold(true, 60, "Connection to your host lost -- reconnecting... (60 s).") == true and KCD2MP.w211.holding == true)
    check("b: the clock stands", ratios[#ratios] == 0, ratios[#ratios])
    check("b: the player and movement maps off, nothing else", maps[1] == "player=false" and maps[2] == "movement=false" and #maps == 2, table.concat(maps, ","))
    check("b: the line is shown", KCD2MP.interactionMsg and KCD2MP.interactionMsg.text:find("reconnecting", 1, true) ~= nil)
    KCD2MP_W211LinkHold(true, 42, "x (42 s)")
    check("b: a repeat updates the line, the hold is not taken twice", #maps == 2 and KCD2MP.w211.holdLeft == 42 and KCD2MP.w211.holds == 1)
    check("b: the Leave goes to the agent during the hold", KCD2MP_W211Leave("menu") == true and logCount("w211_leave") == 1)
    check("b: the hold ends", KCD2MP_W211LinkHold(false, 0, "") == true and KCD2MP.w211.holding == false)
    check("b: the clock and the maps given back", ratios[#ratios] == 15 and maps[#maps] == "movement=true", tostring(ratios[#ratios]) .. " " .. tostring(maps[#maps]))
    check("b: no Leave without a hold", KCD2MP_W211Leave("menu") == false and logCount("w211_leave") == 1)
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (c) A.1.5: the host's held figure says "reconnecting" on its badge
do
    KCD2MP_W211FigureReconnecting("1", true)
    check("c: the badge says reconnecting", KCD2MP_W211BadgeName("1", "partner") == "partner (reconnecting)")
    KCD2MP_W211FigureReconnecting("1", false)
    check("c: and no more once back", KCD2MP_W211BadgeName("1", "partner") == "partner")
end

-- (d) E: the flight -- read from the player's own state (never from input), set back to off in a session; mp_allow_fly lets it be
do
    LOG = {}; TOASTS = {}
    local fly = { mode = 0 }
    player.player = { GetFlyMode = function(self) return fly.mode end, SetFlyMode = function(self, m) fly.mode = m end }
    KCD2MP_W154MenuSession("host", true, nil, nil, nil, "connected", "0.63.9", "", false)
    fly.mode = 1; NOW = NOW + 1
    KCD2MP_W211FlyCheck()
    check("d: in a session the flight is set back to off", fly.mode == 0 and logCount("WO211-FLY the debug flight") == 1)
    check("d: the player is told why", #TOASTS > 0 and TOASTS[#TOASTS]:find("Flying is turned off in co-op", 1, true) ~= nil, TOASTS[#TOASTS])
    KCD2MP_W211Set("allow_fly", "on")
    fly.mode = 2; NOW = NOW + 1
    KCD2MP_W211FlyCheck()
    check("d: mp_allow_fly on leaves it (testers)", fly.mode == 2 and logCount("allowed by mp_allow_fly") == 1)
    KCD2MP_W211Set("allow_fly", "off")
    fly.mode = 1; NOW = NOW + 0.05
    KCD2MP_W211FlyCheck()
    check("d: checked ten times a second at most", fly.mode == 1)
    NOW = NOW + 0.2
    KCD2MP_W211FlyCheck()
    check("d: and set back on the next check", fly.mode == 0)
    KCD2MP_W154MenuSession("none", false, nil, nil, nil, "connected", "0.63.9", "", false)
    fly.mode = 1; NOW = NOW + 1
    KCD2MP_W211FlyCheck()
    check("d: outside a session the game's own flight is left alone", fly.mode == 1)
    player.player = nil
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (e) A.2.3: placed where he stood at the drop, held over the ground while it settles
do
    local put = {}
    player.SetWorldPos = function(self, v) put[#put + 1] = v end
    player.SetWorldAngles = function() end
    TIMERS = {}
    check("e: the placement starts", KCD2MP_W211PlaceAt(10, 20, 5, 1.0) == true and #put == 1 and put[1].z >= 1.5)
    player.SetWorldPos = nil
end

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
