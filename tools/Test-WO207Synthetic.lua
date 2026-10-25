-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-207 synthetic test: the Lua halves of WO-207, against the real kdcmp.lua under MoonSharp (the engine stubbed, a fake clock).
--   (a) the switches; (b) C.2 mp_keep_awake: the engine's inactive-window sleep off during a session, the player's own value back after
--   it (the session's end, the agent's silence, the switch off); (c) A.3 the listener's decision timer; (d) A.4 the vote icon's moment
-- What this proves: the Lua rules. Live evidence: docs/WO-207-findings.md.

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
CVARS = { sys_SleepIfInactiveWH = "1" }
SETS = {}
System.GetCVar = function(n) return CVARS[n] or "0" end
System.SetCVar = function(n, v) CVARS[n] = tostring(v); SETS[#SETS + 1] = n .. "=" .. tostring(v) end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return SPHERE end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.DrawText = function() end
System.RemoveEntity = function(eid)
    for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil; e.removed = true end end
    for _, e in ipairs(EXTRA) do if e.id == eid then e.removed = true end end
end
CCMDS_N = {}
System.AddCCommand = function(name, body, help) CCMDS_N[name] = (CCMDS_N[name] or 0) + 1; CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
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
local function clearLog() LOG = {} end
local function evts(name)
    local out = {}
    for _, l in ipairs(LOG) do
        local a = l:match("%[KCD2%-MP%-EVT%] v1 %d+ " .. name:gsub("_", "%%_") .. " (.*)$")
        if a then out[#out + 1] = a end
    end
    return out
end
local function runTimers()
    local due = TIMERS; TIMERS = {}
    for _, t in ipairs(due) do pcall(t.f) end
end

-- (a) the switches
do
    for _, label in ipairs({ "mp_keep_awake", "mp_conv_lane", "mp_queue_drop_stale", "mp_queue_decouple", "mp_w207_status" }) do
        check("a: " .. label .. " is registered", CCMDS[label] ~= nil, label)
    end
    -- a WO-207 switch never takes over another area's command (mp_conv_hold was WO-160's: its switch became WO-207's)
    local taken = {}
    for k, label in pairs(W207.NAMES) do
        if CCMDS_N[label] ~= 1 or not (CCMDS[label] and CCMDS[label].body == "KCD2MP_W207Set_" .. k .. "(%line)") then taken[#taken + 1] = label end
    end
    check("a: every WO-207 switch name is registered once, its own", #taken == 0, table.concat(taken, ","))
    check("a: WO-160's mp_conv_hold is still WO-160's", CCMDS["mp_conv_hold"] and CCMDS["mp_conv_hold"].body == "KCD2MP_W160SetConvHold(%line)")
    check("a: the setters take the bare line (Fn(%line), never quoted)", CCMDS["mp_conv_lane"] ~= nil and CCMDS["mp_conv_lane"].body == "KCD2MP_W207Set_conv_lane(%line)",
          CCMDS["mp_conv_lane"] and CCMDS["mp_conv_lane"].body)
    clearLog()
    KCD2MP_W207Set("conv_lane", "off")
    check("a: a switch goes to the agent", evts("w207_cfg")[1] == "conv_lane=off", evts("w207_cfg")[1])
    KCD2MP_W207Set("conv_lane", "on")
    check("a: an unknown switch is refused", KCD2MP_W207Set("nope", "on") == false)
    check("a: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (a7) one key press, one listen action
do
    W207.lastListen = {}
    NOW = 500.0
    check("a7: the first press goes through", KCD2MP_W207ListenOnce("kcd2mp_listen_down", "press") == false)
    check("a7: the same press through a second key map (same moment) is dropped", KCD2MP_W207ListenOnce("kcd2mp_listen_down", "press") == true)
    NOW = 500.3
    check("a7: a real second press (300 ms later) goes through", KCD2MP_W207ListenOnce("kcd2mp_listen_down", "press") == false)
    check("a7: another action at the same moment goes through", KCD2MP_W207ListenOnce("kcd2mp_listen_confirm", "press") == false)
    check("a7: a release is never dropped", KCD2MP_W207ListenOnce("kcd2mp_listen_confirm", "release") == false)
    check("a7: not a listen action: never dropped", KCD2MP_W207ListenOnce("use", "press") == false and KCD2MP_W207ListenOnce("use", "press") == false)
    W207.on.listen_once = false   -- the twin
    NOW = 600.0
    KCD2MP_W207ListenOnce("kcd2mp_listen_cancel", "press")
    check("a7 twin: listen_once off, the doubled press goes through (the smoke's two rows and clear-then-leave)", KCD2MP_W207ListenOnce("kcd2mp_listen_cancel", "press") == false)
    W207.on.listen_once = nil
    check("a7: mp_listen_once is registered, its own", CCMDS["mp_listen_once"] and CCMDS["mp_listen_once"].body == "KCD2MP_W207Set_listen_once(%line)")
end

-- (b) C.2 keep awake
do
    ERRS = {}; clearLog(); SETS = {}
    CVARS.sys_SleepIfInactiveWH = "1"; NOW = 100
    KCD2MP_W207KeepAwake(1, 1)
    check("b: a session sets the engine's sleep off", CVARS.sys_SleepIfInactiveWH == "0" and #SETS == 1, CVARS.sys_SleepIfInactiveWH)
    KCD2MP_W207KeepAwake(1, 1)
    check("b: the next word sets nothing again", #SETS == 1, #SETS)
    KCD2MP_W207KeepAwake(0, 1)
    check("b: the session's end puts the player's own value back", CVARS.sys_SleepIfInactiveWH == "1", CVARS.sys_SleepIfInactiveWH)
    CVARS.sys_SleepIfInactiveWH = "0"; SETS = {}
    KCD2MP_W207KeepAwake(1, 1); KCD2MP_W207KeepAwake(0, 1)
    check("b: a player who already runs awake: only his own value is ever written back", #SETS <= 1 and CVARS.sys_SleepIfInactiveWH == "0", table.concat(SETS, ","))
    CVARS.sys_SleepIfInactiveWH = "1"; SETS = {}
    KCD2MP_W207KeepAwake(1, 0)
    check("b: the launcher's setting off: the game keeps its own sleep", CVARS.sys_SleepIfInactiveWH == "1" and #SETS == 0, table.concat(SETS, ","))
    KCD2MP_W207KeepAwake(1, 1); NOW = 110
    KCD2MP_W207AwakeWatch()
    check("b: 10 s of agent silence: still awake", CVARS.sys_SleepIfInactiveWH == "0")
    NOW = 125
    KCD2MP_W207AwakeWatch()
    check("b: 25 s of agent silence: the player's own value back", CVARS.sys_SleepIfInactiveWH == "1", CVARS.sys_SleepIfInactiveWH)
    KCD2MP_W207KeepAwake(1, 1)
    check("b: on again with the next word", CVARS.sys_SleepIfInactiveWH == "0")
    clearLog()
    KCD2MP_W207SetKeepAwake("off")
    check("b: twin: mp_keep_awake off puts the player's value back at once", CVARS.sys_SleepIfInactiveWH == "1" and evts("w207_cfg")[1] == "keep_awake=off", evts("w207_cfg")[1])
    SETS = {}
    KCD2MP_W207KeepAwake(1, 1)
    check("b: twin: off, a session's word sets nothing", #SETS == 0 and CVARS.sys_SleepIfInactiveWH == "1", table.concat(SETS, ","))
    KCD2MP_W207SetKeepAwake("on")
    check("b: on again: awake at once in the running session", CVARS.sys_SleepIfInactiveWH == "0")
    KCD2MP_W207KeepAwake(0, 1)
    check("b: the switch is a bare-line command", CCMDS["mp_keep_awake"] and CCMDS["mp_keep_awake"].body == "KCD2MP_W207SetKeepAwake(%line)")
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (c) A.3 the listener's timer, and (d) the icon's moment
do
    ERRS = {}; clearLog(); TIMERS = {}
    local ui = {}
    UIAction.CallFunction = function(panel, inst, fn, a) ui[#ui + 1] = { fn = tostring(fn), a = a } end
    UIAction.SetArray = function() end
    UIAction.SetVisible = function() end
    local function count(fn) local n = 0; for _, u in ipairs(ui) do if u.fn == fn then n = n + 1 end end; return n end
    local function last(fn) for i = #ui, 1, -1 do if ui[i].fn == fn then return ui[i] end end return nil end
    KCD2MP.W203.cam.listening = nil
    check("c: a timer of a conversation he does not listen to: nothing", KCD2MP_W207Timer(1, 233, 1, 1, 1, 0.2) == false and count("SetDialogTimeLimit") == 0)
    KCD2MP.W203.cam.listening = { starter = "1", cid = "233", anchor = "ttkc_drozd", hidden = {} }
    NOW = 200
    check("c: the starter's timer shows on the listener's HUD", KCD2MP_W207Timer(1, 233, 1, 1, 0.9, 0.2) == true and count("SetDialogTimeLimit") == 1)
    NOW = 201
    runTimers()
    local u = last("UpdateDialogTimeLimit")
    check("c: it runs on by the starter's rate between messages", u ~= nil and math.abs(u.a - 0.7) < 0.01, u and u.a)
    KCD2MP_W207Timer(1, 233, 1, 1, 0.5, 0.2)
    check("c: a new message is not a second timer", count("SetDialogTimeLimit") == 1)
    NOW = 205; runTimers()
    u = last("UpdateDialogTimeLimit")
    check("c: it never goes under zero", u ~= nil and u.a == 0, u and u.a)
    KCD2MP_W207Timer(1, 233, 1, 0, 0, 0)
    check("c: the starter's clear clears it", count("ClearDialogTimeLimit") == 1)
    local before = count("UpdateDialogTimeLimit")
    runTimers()
    check("c: a cleared timer stops running", count("UpdateDialogTimeLimit") == before)
    KCD2MP_W207Timer(1, 233, 2, 1, 1, 0.1)
    KCD2MP_W204Options(1, 233, 2, "0~ui_a___seq1_q~0~-~0~0;1~ui_a___seq2_q~0~-~0~0")
    KCD2MP_ShowNativeToast = function() end
    KCD2MP_W204Chosen(1, 233, 2, 0)
    check("c: the choice takes the timer down (the starter's clock decided)", count("ClearDialogTimeLimit") == 2, count("ClearDialogTimeLimit"))
    KCD2MP_W207Timer(1, 233, 3, 1, 1, 0.1)
    KCD2MP_W204ConvClear()
    check("c: leaving takes the timer too", count("ClearDialogTimeLimit") == 3, count("ClearDialogTimeLimit"))
    clearLog()
    KCD2MP_W204VoteIcon("0~ui_a___seq1_q~0~-~0~0;1~ui_a___seq2_q~0~-~0~0", 1, 0, true)
    KCD2MP_W204VoteIcon("0~ui_a___seq1_q~0~-~0~0;1~ui_a___seq2_q~0~-~0~0", -1, 0, true)
    check("d: the icon's moment goes to the agent (a row, then a clear)", evts("w207_icon")[1] == "1" and evts("w207_icon")[2] == "clear",
          tostring(evts("w207_icon")[1]) .. "/" .. tostring(evts("w207_icon")[2]))
    check("c/d: no Lua errors", #ERRS == 0, ERRS[1])
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
