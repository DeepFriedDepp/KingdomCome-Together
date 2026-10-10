-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-165 synthetic test (the game-side half), against the real kdcmp.lua under MoonSharp.
--
--   (W) the three switches: registered, their defaults (host lock ON, victim decides OFF, block recoil OFF -- the probes' verdicts),
--       each toggle tells the agent (w165_cfg key=on|off), a bad word changes nothing, a bare command repeats the state
--   (N) a WO-165 piece switched off by a fault is said on the game's own HUD
--
-- Driven by Test-WO165Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-165-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
CVARS = { wh_pl_RandomEventsAutoSpawnEnabled = "1" }
System.GetCVarValue = function(n) return CVARS[n] or "0" end
System.GetEntityByName = function(n) return ENTS[n] end
SPHERE = {}
System.GetEntitiesInSphere = function() return SPHERE end
System.GetTerrainElevation = function(p) return 0 end
System.GetFrameTime = function() return 1 / 60 end
System.ExecuteCommand = function(s)
    CMDS[#CMDS + 1] = tostring(s)
    local n, v = tostring(s):match("^(wh_pl_RandomEventsAutoSpawnEnabled) (%S+)$")
    if n then CVARS[n] = v end
end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
Game = mkstub()
Calendar = mkstub()
Calendar.GetWorldTime = function() return 1000000 end
XGenAIModule = mkstub()

ORIG_TALKS = 0
BasicAIActions = { OnTalk = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end }
RESTRICT = {}
PLAYER_RESTRICTED = false
player = { id = 1,
    soul = {
        RestrictDialog = function(self, on) RESTRICT[#RESTRICT + 1] = "player " .. tostring(on); PLAYER_RESTRICTED = on end,
        IsDialogRestricted = function(self) return PLAYER_RESTRICTED end,
        GetState = function() return 0 end,
    },
    human = { IsInDialog = function() return false end },
    actor = { StandUp = function() end },
}
PX, PY = 0, 0
DIRX, DIRY = 1, 0
player.GetWorldPos = function() return { x = PX, y = PY, z = 0 } end
player.GetDirectionVector = function() return { x = DIRX, y = DIRY, z = 0 } end
player.GetName = function() return "Dude" end

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
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end

-- (W) the switches ----------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    check("W1: the switches are registered", CCMDS["mp_host_lock"] ~= nil and CCMDS["mp_host_lock"].body == "KCD2MP_W165SetHostLock(%line)"
        and CCMDS["mp_victim_decides"] ~= nil and CCMDS["mp_victim_decides"].body == "KCD2MP_W165SetVictimDecides(%line)"
        and CCMDS["mp_block_recoil"] ~= nil and CCMDS["mp_block_recoil"].body == "KCD2MP_W165SetBlockRecoil(%line)")
    check("W2: the defaults: all three on",
        KCD2MP.w165.hostLock == true and KCD2MP.w165.victimDecides == true and KCD2MP.w165.blockRecoil == true)
    check("W3: the two not proven with two players say so in their help", CCMDS["mp_victim_decides"].help:find("UNTESTED", 1, true) ~= nil
        and CCMDS["mp_block_recoil"].help:find("UNTESTED", 1, true) ~= nil)
    local mark = #LOG
    check("W4: mp_host_lock off", KCD2MP_W165SetHostLock("off") == true and KCD2MP.w165.hostLock == false
        and countEvt("w165_cfg", "host_lock=off", mark) == 1 and lastLog("WO165-TOGGLE mp_host_lock off", mark) ~= nil)
    mark = #LOG
    check("W5: mp_victim_decides off / mp_block_recoil off tell the agent", KCD2MP_W165SetVictimDecides("off") == true and KCD2MP_W165SetBlockRecoil("off") == true
        and countEvt("w165_cfg", "victim_decides=off", mark) == 1 and countEvt("w165_cfg", "block_recoil=off", mark) == 1)
    mark = #LOG
    check("W6: a bad word changes nothing and tells nobody", KCD2MP_W165SetHostLock("maybe") == false and KCD2MP.w165.hostLock == false
        and countEvt("w165_cfg", "", mark) == 0)
    mark = #LOG
    check("W7: the bare command repeats the state", KCD2MP_W165SetHostLock(nil) == true and countEvt("w165_cfg", "host_lock=off", mark) == 1)
    KCD2MP_W165SetHostLock("on"); KCD2MP_W165SetVictimDecides("on"); KCD2MP_W165SetBlockRecoil("on")
    check("W8: back to the defaults", KCD2MP.w165.hostLock == true and KCD2MP.w165.victimDecides == true and KCD2MP.w165.blockRecoil == true)
    noErrs("W")
end

-- (N) the notice -----------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    local mark = #LOG
    local before = #TOASTS
    KCD2MP_W165Say("The victim-decides replay was switched off by a fault")
    check("N1: said on the HUD and logged", #TOASTS > before and lastLog("WO165-NOTICE The victim-decides replay was switched off by a fault", mark) ~= nil)
    noErrs("N")
end

OUT = table.concat(RESULTS, "\n")
