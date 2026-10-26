-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-208 synthetic test: the Lua halves of WO-208, against the real kdcmp.lua under MoonSharp (the engine stubbed, a fake clock).
--   (a) the switches; (b) C.3 a player in a talk of his own does not listen to the partner's (mp_listen_own_talk_first) and the dialog
--   map detector; (c) C.5 the listener's own head (mp_listen_show_head); (d) A the owned-copy test the Lua copy writers ask.
-- What this proves: the Lua rules. Live evidence: docs/WO-208-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; CCMDS = {}; LOCKS = {}; MSGS = {}; EXTRA = {}
QINV = {}; CREATE_REFUSED = {}
IN_DIALOG = false
MAPS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
CVARS = { sys_SleepIfInactiveWH = "1", wh_pl_ShowHead = "0" }
SETS = {}
System.GetCVar = function(n) return CVARS[n] or "0" end
System.SetCVar = function(n, v) CVARS[n] = tostring(v); SETS[#SETS + 1] = n .. "=" .. tostring(v) end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return SPHERE end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.DrawText = function() end
CCMDS_N = {}
System.AddCCommand = function(name, body, help) CCMDS_N[name] = (CCMDS_N[name] or 0) + 1; CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.GetTerrainElevation = function() return 0 end
System.GetEntity = function(id) for _, e in pairs(ENTS) do if e.id == id then return e end end return nil end
System.GetEntitiesByClass = function(cls)
    local out = {}
    for _, e in pairs(ENTS) do if e.class == cls then out[#out + 1] = e end end
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
ActionMapManager = { EnableActionMap = function(m, on) MAPS[m] = on end }
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }
XGenAIModule = mkstub()
XGenAIModule.MakeTableFromType = function(kind) return { kind = kind, information = {} } end

player = { id = 1, this = { id = 777 }, GetName = function(self) return "Dude" end,
           GetWorldPos = function(self) return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end },
           human = { IsMounted = function() return false end, IsInDialog = function() return IN_DIALOG == true end },
           soul = { GetState = function() return 100 end, IsInCombatDanger = function() return false end },
           inventory = { GetCountOfClass = function() return 0 end } }

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
local function logged(pat) for _, l in ipairs(LOG) do if l:find(pat, 1, true) then return true end end return false end

-- (a) the switches
do
    local bad = {}
    for k, label in pairs(W208.NAMES) do
        if CCMDS_N[label] ~= 1 or not (CCMDS[label] and CCMDS[label].body == "KCD2MP_W208Set_" .. k .. "(%line)") then bad[#bad + 1] = label end
    end
    check("a: every WO-208 switch is registered once, its own, a bare-line command", #bad == 0, table.concat(bad, ","))
    for _, k in ipairs({ "conv_cam_player_view", "target_in_fight", "defence_local", "listen_show_head", "joiner_player_triggers",
                         "quest_wait_aside", "defence_recoil", "clock_stands_with_host", "listen_own_talk_first", "copy_anim_owner",
                         "conv_autojoin" }) do
        check("a: " .. k .. " has a name and a help line", W208.NAMES[k] ~= nil and W208.HELP[k] ~= nil, k)
    end
    check("a: the owner starts off (until its acceptance), the rest on", W208.DEFAULT_OFF.copy_anim_owner == true and not W208.DEFAULT_OFF.defence_local)
    clearLog()
    KCD2MP_W208Set("quest_wait_aside", "off")
    check("a: a switch goes to the agent", evts("w208_cfg")[1] == "quest_wait_aside=off", evts("w208_cfg")[1])
    KCD2MP_W208Set("quest_wait_aside", "on")
    check("a: an unknown switch is refused", KCD2MP_W208Set("nope", "on") == false)
end

-- (b) C.3: his own talk first
do
    ERRS = {}; clearLog(); TIMERS = {}
    KCD2MP_ShowNativeToast = function() end
    KCD2MP.W203.cam.listening = nil
    IN_DIALOG = true
    KCD2MP_W203Enter(1, 233, "ttkc_drozd")
    check("b: in a talk of his own, the partner's talk is not entered", KCD2MP.W203.cam.listening == nil and MAPS["kcd2mp_listen"] == nil)
    check("b: the agent hears it as a leave (the view goes, the starter is told)", evts("w203_leave")[1] == "1 233 own-talk", evts("w203_leave")[1])
    check("b: and it is said in the log", logged("WO208-LISTEN refused partner=1 cid=233"))
    IN_DIALOG = false; clearLog()
    KCD2MP_W203Enter(1, 233, "ttkc_drozd")
    check("b: out of his own talk, he listens (the listen map on)", KCD2MP.W203.cam.listening ~= nil and MAPS["kcd2mp_listen"] == true)
    KCD2MP_W203End(1, 233, "ended")
    check("b: the talk's end takes the listen map off", KCD2MP.W203.cam.listening == nil and MAPS["kcd2mp_listen"] == false)
    -- twin: the switch off -- he listens from inside his own talk, the game's dialog map still on beside the listen map (every key twice)
    W208.on.listen_own_talk_first = false
    IN_DIALOG = true; clearLog()
    KCD2MP_W203Enter(1, 234, "ttkc_drozd")
    check("b twin: listen_own_talk_first off, he listens from inside his own talk", KCD2MP.W203.cam.listening ~= nil and evts("w203_leave")[1] == nil)
    KCD2MP_W203End(1, 234, "ended")
    W208.on.listen_own_talk_first = nil
    IN_DIALOG = false
    -- the detector: a listen action with nobody listening and no talk of his own -- only the game's dialog map carries it
    clearLog(); NOW = 900
    KCD2MP_W207ListenOnce("kcd2mp_listen_down", "press")
    check("b: the dialog-map detector tells it (log and agent)", logged("WO208-DIALOGMAP kcd2mp_listen_down") and evts("w208_dialogmap")[1] == "kcd2mp_listen_down")
    IN_DIALOG = true; clearLog(); NOW = 1000
    KCD2MP_W207ListenOnce("kcd2mp_listen_down", "press")
    check("b: in his own talk the dialog map is his talk's: not told", not logged("WO208-DIALOGMAP"))
    IN_DIALOG = false
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (c) C.5: the listener's own head
do
    ERRS = {}; clearLog(); SETS = {}
    CVARS.wh_pl_ShowHead = "0"
    KCD2MP_W203Enter(1, 240, "ttkc_woman_2")
    check("c: listening shows his own head", CVARS.wh_pl_ShowHead == "1", CVARS.wh_pl_ShowHead)
    KCD2MP_W203End(1, 240, "ended")
    check("c: the talk's end puts his own value back", CVARS.wh_pl_ShowHead == "0", CVARS.wh_pl_ShowHead)
    W208.on.listen_show_head = false; SETS = {}
    KCD2MP_W203Enter(1, 241, "ttkc_woman_2")
    local touched = false
    for _, s in ipairs(SETS) do if s:find("wh_pl_ShowHead", 1, true) then touched = true end end
    check("c twin: listen_show_head off, the head stays hidden", CVARS.wh_pl_ShowHead == "0" and not touched, table.concat(SETS, ","))
    KCD2MP_W203End(1, 241, "ended")
    W208.on.listen_show_head = nil
    check("c: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (d) A: the owned-copy test the mod's own copy writers ask
do
    KCD2MP.npcPuppets = KCD2MP.npcPuppets or {}
    KCD2MP.npcPuppets["ttkc_man_5"] = { name = "ttkc_man_5" }
    KCD2MP_W208Probe = nil
    KCD2MP_W208SetOwnerAll(false)
    check("d: the owner off: no copy is the owner's", KCD2MP_W208Owned("ttkc_man_5") == false)
    KCD2MP_W208SetOwnerAll(true)
    check("d: the owner on every copy: a copy is the owner's", KCD2MP_W208Owned("ttkc_man_5") == true)
    check("d: never a body that is not a copy", KCD2MP_W208Owned("ttkc_nobody") == false)
    NOW = NOW + 10
    check("d: no word from the agent for 6 s: the old writers again", KCD2MP_W208Owned("ttkc_man_5") == false)
    KCD2MP_W208Probe = "ttkc_drozd"
    check("d: the bench's A.1 probe copy is the owner's alone", KCD2MP_W208Owned("ttkc_drozd") == true and KCD2MP_W208Owned("ttkc_man_5") == false)
    KCD2MP_W208Probe = nil
end

-- (e) C.4: a conversation that started by itself takes the partner in when he is near and free
do
    ERRS = {}; clearLog()
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    local FX = 10
    KCD2MP.ghosts["1"] = { entity = { id = 4242, GetWorldPos = function() return { x = FX, y = 0, z = 0 } end, GetName = function() return "fig" end } }
    KCD2MP.W203.cam.listening = nil; KCD2MP.W203.cam.offers = {}
    KCD2MP_W203Offer(1, 792, 1, "tsem_suk", 1)
    check("e: a forced talk 10 m away: he listens in by himself", evts("w203_join")[1] == "1 792 auto", evts("w203_join")[1])
    check("e: and it is said in the log", logged("WO208-AUTOJOIN partner=1 cid=792 listening in"))
    clearLog()
    KCD2MP_W203Offer(1, 793, 1, "tsem_suk", 0)
    check("e: a talk the partner started himself: the prompt only, no join", evts("w203_join")[1] == nil and KCD2MP.W203.cam.offers["1"] ~= nil)
    clearLog(); FX = 40
    KCD2MP_W203Offer(1, 794, 1, "tsem_suk", 1)
    check("e: the partner 40 m away: no join", evts("w203_join")[1] == nil and logged("farther than 30 m"))
    clearLog(); FX = 10; IN_DIALOG = true
    KCD2MP_W203Offer(1, 795, 1, "tsem_suk", 1)
    check("e: in a talk of his own: no join", evts("w203_join")[1] == nil and logged("in a talk of his own"))
    IN_DIALOG = false; clearLog()
    KCD2MP.W203.cam.listening = { starter = "1", cid = "1", anchor = "", hidden = {} }
    KCD2MP_W203Offer(1, 796, 1, "tsem_suk", 1)
    check("e: listening already: no second join", evts("w203_join")[1] == nil and logged("listening already"))
    KCD2MP.W203.cam.listening = nil
    -- twin (mp_conv_autojoin off): the agent offers the talk with auto 0 -- the prompt only, as for any talk (the 0 case above)
    clearLog()
    KCD2MP_W203Offer(1, 797, 1, "tsem_suk", 0)
    check("e twin: conv_autojoin off, a forced talk near: no join", evts("w203_join")[1] == nil)
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
