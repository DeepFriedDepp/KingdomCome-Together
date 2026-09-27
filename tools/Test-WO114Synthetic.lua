-- WO-114 synthetic test, against the real kdcmp.lua under MoonSharp.
--
-- The leash is decided by the HOST's agent (LeashLogic, pinned by
-- dotnet/KcdMp.Client.Tests/Wo114Tests.cs); the mod owns the settings, the
-- words, the countdown row, the busy/mount answers, the dismount and the
-- joiner's fast-travel switch. This suite pins that half:
--   (a) shipped defaults: leash ON, 600 m / 650 m; the WO114-BUILD marker once;
--       the wo114_cfg event emitted at load
--   (b) the three console commands, %line, documented
--   (c) mp_leash off/on: flag, one wo114_cfg event each, a report line
--   (d) mp_leash_warn_m / mp_leash_pull_m: accepted in range, refused when the
--       warning would not stay below the pull (either way), bad input refused
--   (e) the countdown row: drawn with the number, gone 2.5 s after the last
--       update, cleared by 0; one WO114-COUNTDOWN line per number
--   (f) busy: dialogue and mounted read from the player; KCD2MP.isRiding counts
--   (g) the dismount: ForceDismount once, read back, asked again only after 3 s
--   (h) fast travel: blocked with the cvar (0) and the previous value given back;
--       a refusal is told once per 5 s and reported to the agent
--   (i) presets: legacy = leash off, clean = leash on
--
-- Driven by Test-WO114Synthetic.ps1 through the WO-77 MoonSharp driver.
-- What it does NOT prove: anything the engine does with the cvar or the
-- dismount -- see docs/WO-114-findings.md for the live runs.
--
-- Part 1: engine stubs + a fake clock (the WO-108 harness, verbatim) + cvars.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; SPAWNS = {}; CCMDS = {}
CVARS = { wh_pl_FastTravelEnabled = 1 }

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetCVar = function(n) return CVARS[n] end
System.SetCVar = function(n, v) CVARS[n] = tonumber(v) end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return SPHERE end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.DrawText = function(x, y, text, size) DRAWS[#DRAWS + 1] = { x = x, y = y, text = tostring(text) } end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end }
XGenAIModule = mkstub()
XGenAIModule.SpawnEntity = function(t)
    SPAWNS[#SPAWNS + 1] = t
    local e = { class = t.ClassName, id = 9000 + #SPAWNS }
    e.GetName = function(self) return t.Name end
    e.GetWorldPos = function(self) return { x = 0, y = 0, z = 0 } end
    ENTS[t.Name] = e
    return e
end

HUMAN = { dialog = false, mounted = false, dismounts = 0, dismountLands = true }
player = { id = 1, GetName = function(self) return "Dude" end,
           GetWorldPos = function() return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end },
           human = { IsInDialog = function(self) return HUMAN.dialog end,
                     IsMounted = function(self) return HUMAN.mounted end,
                     ForceDismount = function(self) HUMAN.dismounts = HUMAN.dismounts + 1; if HUMAN.dismountLands then HUMAN.mounted = false end end } }

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
local function evtCount(name, arg)
    local n = 0
    for _, l in ipairs(LOG) do
        if string.find(l, "[KCD2-MP-EVT] v1 ", 1, true) and string.find(l, " " .. name .. " " .. arg, 1, true) then n = n + 1 end
    end
    return n
end
local function drawn(pat)
    for _, d in ipairs(DRAWS) do if string.find(d.text, pat, 1, true) then return d end end
    return nil
end

local w = KCD2MP.w114

-- (a) defaults + marker + the load-time cfg event
check("a: leash ships ON", w.leash == true, tostring(w.leash))
check("a: 600 m warning, 650 m pull", w.warnM == 600 and w.pullM == 650, tostring(w.warnM) .. "/" .. tostring(w.pullM))
check("a: WO114-BUILD logged once", logCount("WO114-BUILD leash=on warn_m=600 pull_m=650 countdown_s=10 rearm_m=550") == 1, lastLog("WO114-BUILD"))
check("a: wo114_cfg emitted at load", evtCount("wo114_cfg", "leash=on warn=600 pull=650") >= 1, lastLog("wo114_cfg"))

-- (b) console registration
for _, name in ipairs({ "mp_leash", "mp_leash_warn_m", "mp_leash_pull_m" }) do
    local c = CCMDS[name]
    check("b: " .. name .. " registered with %line", c ~= nil and string.find(c.body, "(%line)", 1, true) ~= nil and not string.find(c.body, "%LINE", 1, true), c and c.body)
    check("b: " .. name .. " says HOST", c ~= nil and string.find(c.help, "HOST", 1, true) ~= nil, c and c.help)
end

-- (c) on/off
clearLog()
check("c: off accepted", KCD2MP_SetLeash("off") == true and w.leash == false)
check("c: one cfg event with leash=off", evtCount("wo114_cfg", "leash=off warn=600 pull=650") == 1, lastLog("[KCD2-MP-EVT]"))
check("c: report line", logCount("WO114-TOGGLE mp_leash leash=off") == 1, lastLog("WO114-TOGGLE"))
clearLog()
check("c: on accepted", KCD2MP_SetLeash("on") == true and w.leash == true)
check("c: one cfg event with leash=on", evtCount("wo114_cfg", "leash=on warn=600 pull=650") == 1)
clearLog()
check("c: bare reports, changes nothing", KCD2MP_SetLeash("%line") == true and w.leash == true and logCount("WO114-TOGGLE mp_leash leash=on") == 1)
check("c: bad refused", KCD2MP_SetLeash("maybe") == false and w.leash == true and logCount("mp_leash: expected on|off") == 1)

-- (d) the distances
clearLog()
check("d: warn 500 accepted", KCD2MP_SetLeashWarn("500") == true and w.warnM == 500)
check("d: cfg carries warn=500", evtCount("wo114_cfg", "leash=on warn=500 pull=650") == 1, lastLog("[KCD2-MP-EVT]"))
check("d: warn 650 refused (not below the pull)", KCD2MP_SetLeashWarn("650") == false and w.warnM == 500 and logCount("is not below mp_leash_pull_m (650)") == 1)
check("d: pull 500 refused (not above the warning)", KCD2MP_SetLeashPull("500") == false and w.pullM == 650 and logCount("is not above mp_leash_warn_m (500)") == 1)
check("d: pull 900 accepted", KCD2MP_SetLeashPull("900") == true and w.pullM == 900)
check("d: bad metres refused", KCD2MP_SetLeashWarn("12.5") == false and KCD2MP_SetLeashPull("nine") == false and KCD2MP_SetLeashWarn("10") == false)
check("d: bare reports", KCD2MP_SetLeashPull("") == true and w.pullM == 900)
KCD2MP_SetLeashWarn("600"); KCD2MP_SetLeashPull("650")
check("d: back to 600/650", w.warnM == 600 and w.pullM == 650)

-- (e) the countdown row
clearLog(); DRAWS = {}
NOW = 100
KCD2MP_Wo114Countdown(10)
KCD2MP_Wo114DrawUI()
check("e: row drawn with 10", drawn("Bringing you back to your host in 10...") ~= nil, DRAWS[1] and DRAWS[1].text)
check("e: one countdown line", logCount("WO114-COUNTDOWN 10") == 1)
NOW = 101; DRAWS = {}
KCD2MP_Wo114Countdown(9); KCD2MP_Wo114DrawUI()
check("e: row updates to 9", drawn("in 9...") ~= nil)
NOW = 103.6; DRAWS = {}
KCD2MP_Wo114DrawUI()
check("e: row gone 2.5 s after the last update (a hold)", #DRAWS == 0, DRAWS[1] and DRAWS[1].text)
NOW = 104; DRAWS = {}
KCD2MP_Wo114Countdown(4); KCD2MP_Wo114Countdown(0); KCD2MP_Wo114DrawUI()
check("e: 0 clears at once", #DRAWS == 0 and logCount("WO114-COUNTDOWN cleared") == 1)
check("e: the label loop draws the row", KCD2MP_DrawInteractionUI ~= nil)
DRAWS = {}; KCD2MP_Wo114Countdown(3); KCD2MP_DrawInteractionUI()
check("e: ... through KCD2MP_DrawInteractionUI", drawn("in 3...") ~= nil)
KCD2MP_Wo114Countdown(0)

-- (f) busy
clearLog()
KCD2MP_Wo114Busy()
check("f: idle: d=0 m=0", evtCount("wo114_busy", "d=0 m=0") == 1, lastLog("wo114_busy"))
HUMAN.dialog = true; HUMAN.mounted = true
-- WO-131 Phase 3: a conversation the player is IN also has the engine's
-- DialogTwin_<player> stand-in (a bark near a rider has none: no hold).
clearLog(); KCD2MP_Wo114Busy()
check("f: IsInDialog alone (a bark) + mounted: no dialogue hold (WO-131)", evtCount("wo114_busy", "d=0 m=1") == 1, lastLog("wo114_busy"))
ENTS["DialogTwin_Dude"] = { id = 991, GetName = function() return "DialogTwin_Dude" end }
clearLog(); KCD2MP_Wo114Busy()
check("f: dialogue + mounted", evtCount("wo114_busy", "d=1 m=1") == 1, lastLog("wo114_busy"))
ENTS["DialogTwin_Dude"] = nil
HUMAN.dialog = false; HUMAN.mounted = false; KCD2MP.isRiding = true
clearLog(); KCD2MP_Wo114Busy()
check("f: the riding detector counts as mounted", evtCount("wo114_busy", "d=0 m=1") == 1, lastLog("wo114_busy"))
KCD2MP.isRiding = false
clearLog(); KCD2MP_Wo114BusyNow("ab12")
check("f: BusyNow answers with the token", evtCount("wo124_reply", "ab12 d=0 m=0") == 1, lastLog("wo124_reply"))

-- (g) the dismount
HUMAN.mounted = true; HUMAN.dismountLands = false; HUMAN.dismounts = 0
NOW = 200; clearLog()
KCD2MP_Wo114Dismount("t1")
check("g: ForceDismount asked", HUMAN.dismounts == 1)
check("g: still mounted is reported", evtCount("wo124_reply", "t1 was=yes force=ok mounted=yes") == 1, lastLog("wo124_reply"))
NOW = 201; KCD2MP_Wo114Dismount("t2")
check("g: not asked again within 3 s", HUMAN.dismounts == 1)
HUMAN.dismountLands = true
NOW = 203.5; clearLog(); KCD2MP_Wo114Dismount("t3")
check("g: asked again after 3 s, and it lands", HUMAN.dismounts == 2 and evtCount("wo124_reply", "t3 was=yes force=ok mounted=no") == 1, lastLog("wo124_reply"))
clearLog(); KCD2MP_Wo114Dismount("t4")
check("g: not mounted: nothing asked", HUMAN.dismounts == 2 and evtCount("wo124_reply", "t4 was=no force=none mounted=no") == 1, lastLog("wo124_reply"))

-- (h) fast travel
CVARS.wh_pl_FastTravelEnabled = 2
clearLog()
check("h: block accepted", KCD2MP_Wo114FastTravelBlock(true, "test") == true)
check("h: cvar 0", CVARS.wh_pl_FastTravelEnabled == 0, tostring(CVARS.wh_pl_FastTravelEnabled))
check("h: logged with the previous value", logCount("WO114-FASTTRAVEL off (test): wh_pl_FastTravelEnabled 2 -> 0 (was 2 before co-op)") == 1, lastLog("WO114-FASTTRAVEL"))
clearLog(); KCD2MP_Wo114FastTravelBlock(true, "again")
check("h: re-assert is quiet when already 0", logCount("WO114-FASTTRAVEL") == 0)
CVARS.wh_pl_FastTravelEnabled = 1      -- something turned it back on (a load, the console)
KCD2MP_Wo114FastTravelBlock(true, "re-assert")
check("h: re-assert puts it back to 0, previous value kept", CVARS.wh_pl_FastTravelEnabled == 0 and w.ftPrev == 2)
NOW = 300; clearLog(); TOASTS = {}; DRAWS = {}
check("h: a refusal is logged", KCD2MP_Wo114FastTravelTried("map") == true and logCount("WO114-FASTTRAVEL refused (map)") == 1, lastLog("WO114-FASTTRAVEL"))
check("h: ... the game's own HUD toast at once", TOASTS[1] == "Only the host can fast travel in co-op.", TOASTS[1])
check("h: ... and reported", evtCount("wo114_ft_try", "map") == 1)
check("h: the plain line waits for the draw loop (the map holds the timers)", logCount("WO114-MSG Only the host") == 0)
NOW = 302; KCD2MP_Wo114FastTravelTried("map")
check("h: not told again within 5 s", #TOASTS == 1 and logCount("WO114-FASTTRAVEL refused") == 1)
NOW = 340; KCD2MP_Wo114DrawUI()   -- the map closed 40 s later: the line still comes, once
check("h: ... then shows, once", logCount("WO114-MSG Only the host can fast travel in co-op.") == 1 and KCD2MP.interactionMsg ~= nil and KCD2MP.interactionMsg.shownAt == 340)
KCD2MP_Wo114DrawUI()
check("h: ... and only once", logCount("WO114-MSG Only the host") == 1)
clearLog()
KCD2MP_Wo114FastTravelBlock(false, "left")
check("h: given back to the previous value", CVARS.wh_pl_FastTravelEnabled == 2 and logCount("WO114-FASTTRAVEL given back (left): wh_pl_FastTravelEnabled -> 2") == 1, lastLog("WO114-FASTTRAVEL"))
check("h: not blocked: a try is not told", KCD2MP_Wo114FastTravelTried("map") == false)
CVARS.wh_pl_FastTravelEnabled = nil
check("h: an unreadable cvar blocks nothing and says so", KCD2MP_Wo114FastTravelBlock(true, "x") == false and logCount("unreadable -- fast travel NOT blocked") == 1)
KCD2MP_Wo114FastTravelBlock(false, "x")
CVARS.wh_pl_FastTravelEnabled = 1

-- (i) presets
clearLog()
KCD2MP_ApplyPreset("legacy")
check("i: legacy = leash off", w.leash == false and logCount("MP-PRESET name=legacy set=leash from=true to=false") == 1, lastLog("set=leash"))
KCD2MP_ApplyPreset("clean")
check("i: clean = leash on", w.leash == true and logCount("MP-PRESET name=clean set=leash from=false to=true") == 1, lastLog("set=leash"))

check("z: no Lua errors", #ERRS == 0, ERRS[1])

-- Summary.
local pass, fail = 0, 0
for _, res in ipairs(RESULTS) do if res:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
