-- WO-138 synthetic test, against the real kdcmp.lua under MoonSharp.
-- Harness copied from Test-WO137Synthetic.lua.
--   (a) the host: while the DLL streams (the agent confirms it) the Lua sender
--       puts no npc_state out; otherwise it does, exactly as before; a
--       non-authority's claim is never gated
--   (b) the host: the rescan set goes out as npc_track (+ w138_cfg) on a change
--       and every 10 s; long sets are chunked; not-human is flagged
--   (c) the host: the dialogue edge (w138_dialog) on a change only
--   (d) the joiner: hold, don't hide -- no silence release while held; the
--       host's resume restarts the silence clocks; a lost link hides at once;
--       an agent that stops re-asserting lets the hold lapse; the reconcile
--       backstop respects the hold; no hold = the 3 s rule as before
--   (e) the inventory lever (wh_ui_ApsePauseRatio set to 1, restored) and the commands
-- What this proves: the Lua half. Live evidence: docs/WO-138-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; SPAWNS = {}; CCMDS = {}; LOCKS = {}; TALKS = {}
PLAYER_IN_DIALOG = false

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
CVARS = { wh_ui_ApsePauseRatio = 1000 }
System.GetCVar = function(n) return CVARS[n] ~= nil and CVARS[n] or "0" end
System.SetCVar = function(n, v) CVARS[n] = v end
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

-- the game's BasicAIActions talk/chat entry points (Scripts/Entities/AI/Shared/BasicAIActions.lua)
BasicAIActions = {}
for _, fn in ipairs({ "OnTalk", "OnChat", "OnChatWithFocus", "OnChatRequestAccepted", "OnChatOpen", "OnLoot", "OnPickpocketing" }) do
    local f = fn
    BasicAIActions[f] = function(self, user, slot) TALKS[#TALKS + 1] = f .. ":" .. tostring(self and self:GetName()) end
end
ORIG_ONTALK = BasicAIActions.OnTalk

player = { id = 1, GetName = function(self) return "Dude" end,
           GetWorldPos = function() return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end },
           human = { IsInDialog = function() return PLAYER_IN_DIALOG end } }

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
local function logCount(pat, from)
    local n = 0
    for i = (from or 0) + 1, #LOG do if string.find(LOG[i], pat, 1, true) then n = n + 1 end end
    return n
end
local function lastLog(pat)
    for i = #LOG, 1, -1 do if string.find(LOG[i], pat, 1, true) then return LOG[i] end end
    return nil
end
local function countEvt(name, argPrefix, from)
    local n = 0
    for i = (from or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " " .. (argPrefix or ""), 1, true) then n = n + 1 end
    end
    return n
end
local function clearLog() LOG = {} end
local function cmdCount(pat)
    local n = 0
    for _, c in ipairs(CMDS) do if string.find(c, pat, 1, true) then n = n + 1 end end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end

NEXTID = 5000
local function mkEntity(name, x, y, z)
    NEXTID = NEXTID + 1
    local e = { class = "NPC", id = NEXTID, px = x or 0, py = y or 0, pz = z or 0, rz = 0, dead = false, hp = 100, inDialog = false }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.GetWorldAngles = function(self) return { x = 0, y = 0, z = self.rz } end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.StartAnimation = function() end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end,
                GetCurrentAnimationState = function() return "MotionIdle" end }
    e.human = { IsWeaponDrawn = function() return false end, IsInDialog = function() return e.inDialog == true end,
                DrawWeapon = function() return true end, HolsterWeapon = function() return true end }
    e.hidden = false
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.actor.StandUp = function() end
    e.soul = { DealDamage = function() end }
    return e
end
mkEntityLate = mkEntity

local function reset()
    KCD2MP.npcPuppets = {}; KCD2MP.npcTracked = {}; KCD2MP.dragging = {}; KCD2MP.dragWatch = {}
    KCD2MP._npcPaused = {}; KCD2MP._npcResumePending = {}; KCD2MP._npcEverPaused = {}
    KCD2MP.npcPuppetRunning = false; KCD2MP._npcPuppetAliveAt = nil; KCD2MP._npcPuppetRetired = {}
    KCD2MP.npcDeathSync = true; KCD2MP._npcDeathSeen = {}; KCD2MP._npcDeathAnnounced = {}
    KCD2MP._npcDeathRemote = {}; KCD2MP._npcDeathDiverged = {}
    KCD2MP.ghosts = {}; KCD2MP.horseGhosts = {}
    KCD2MP.w131.parked = {}; KCD2MP.w131.standins = {}; KCD2MP.w131.active = false; KCD2MP.w131.guard = true
    KCD2MP.w131.reassert = false; KCD2MP.w131.standinLastAt = -1e9
    KCD2MP.wo102.authorityHost = true; KCD2MP.wo102.authorityPause = true; KCD2MP.hitSensorOn = false
    KCD2MP.w122.sharedWorld = true; KCD2MP.w122.ownerDeath = true; KCD2MP.w122.ownerReq = {}
    KCD2MP.w136.hold = false; KCD2MP.w136.souls = {}; KCD2MP.w136.soulAsked = {}
    local w = KCD2MP.w137
    w.talking = {}; w.held = {}; w.deadRefused = {}; w.deadStandinFail = {}; w.sync = true; w.talkOn = true
    ENTS = {}; SPHERE = {}; ERRS = {}; CMDS = {}; SPAWNS = {}; TOASTS = {}; TALKS = {}
    PLAYER_IN_DIALOG = false
end
local function tick()
    NOW = NOW + 0.05
    KCD2MP_NpcPuppetTick(nil, KCD2MP.npcPuppetGen)
end
local function run(seconds, fn)
    local t_end = NOW + seconds
    while NOW < t_end do if fn then fn() end; tick() end
end


local function emitted(name, from)
    local out = {}
    local pat = "[KCD2-MP-EVT] v1 "
    for i = (from or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find(pat, 1, true) then
            local rest = l:sub(#pat + 1)
            local ev, arg = rest:match("^%d+ (%S+) ?(.*)$")
            if ev == name then out[#out + 1] = arg end
        end
    end
    return out
end

-- ================================================================ WO-138

do -- (a) the host's Lua sender is quiet while the DLL streams
    reset(); clearLog(); NOW = 100
    KCD2MP.hitSensorOn = true; KCD2MP.npcSync.enabled = true; KCD2MP.npcSyncRunning = true
    KCD2MP._lastAnchors = { { x = 0, y = 0, z = 0 } }
    local e = mkEntity("tzel_man_5", 5, 0, 0); ENTS["tzel_man_5"] = e
    KCD2MP.npcTracked = { tzel_man_5 = { since = NOW } }
    KCD2MP._npcScanAt = NOW   -- no rescan in this tick
    KCD2MP_NpcSyncTick()
    check("a: no DLL: the Lua sender emits npc_state as before", #emitted("npc_state") == 1, emitted("npc_state")[1])
    KCD2MP_W138NativeSend(6)
    check("a: the agent's confirmation is logged", logCount("WO138-SEND native") == 1)
    local mark = #LOG
    e.px = 6; NOW = NOW + 0.1; KCD2MP._npcScanAt = NOW
    KCD2MP_NpcSyncTick()
    check("a: the DLL streams: the Lua sender puts nothing out", #emitted("npc_state", mark) == 0)
    check("a: ...but its bookkeeping runs (a fallback resumes where the DLL left off)", KCD2MP.npcTracked.tzel_man_5.lastX == 6)
    NOW = NOW + 7; e.px = 8; KCD2MP._npcScanAt = NOW
    mark = #LOG
    KCD2MP_NpcSyncTick()
    check("a: the confirmation lapses after 6 s (the agent / DLL went away): the Lua sender is back", #emitted("npc_state", mark) == 1)
    KCD2MP_W138NativeSend(6)
    KCD2MP.w138.nativeOn = false
    mark = #LOG; e.px = 9; NOW = NOW + 0.1; KCD2MP._npcScanAt = NOW
    KCD2MP_NpcSyncTick()
    check("a: mp_w138_native off: the Lua sender streams even with the DLL", #emitted("npc_state", mark) == 1)
    KCD2MP.w138.nativeOn = true
    noErrs("a")
end

do -- (b) the rescan set to the DLL
    reset(); clearLog(); NOW = 200
    KCD2MP.hitSensorOn = true
    ENTS["tzel_maid"] = mkEntity("tzel_maid", 1, 1, 0)
    local h = mkEntity("horse_tzel_1", 2, 2, 0); h.class = "Horse"; ENTS["horse_tzel_1"] = h
    KCD2MP.npcTracked = { tzel_maid = {}, horse_tzel_1 = {} }
    KCD2MP.w138.trackKey = ""; KCD2MP.w138.trackAt = -1e9
    KCD2MP_W138PushTrack()
    local t = emitted("npc_track")
    check("b: the set goes out once", #t == 1, t[1])
    check("b: gen part parts names, sorted, the horse flagged not human", t[1] ~= nil and t[1]:match("^%d+ 0 1 horse_tzel_1:1,tzel_maid:0$") ~= nil, t[1])
    local c = emitted("w138_cfg")
    check("b: the sender's settings ride with it (100 ms, 2 s, 5 cm, cull on 60 m, far band 150 in a shared world, 12 m)",
        c[1] == "100 2000 50 1 60 150 12", c[1])
    local mark = #LOG
    NOW = NOW + 2; KCD2MP_W138PushTrack()
    check("b: unchanged within 10 s: nothing again", #emitted("npc_track", mark) == 0)
    NOW = NOW + 9; KCD2MP_W138PushTrack()
    check("b: unchanged after 10 s: again (a restarted agent / DLL learns it)", #emitted("npc_track", mark) == 1)
    mark = #LOG
    KCD2MP.npcTracked.tzel_maid = nil
    NOW = NOW + 0.5; KCD2MP_W138PushTrack()
    local t2 = emitted("npc_track", mark)
    check("b: a change goes out at once, a new generation", #t2 == 1 and t2[1]:match(" 0 1 horse_tzel_1:1$") ~= nil and t2[1] ~= t[1], t2[1])
    mark = #LOG
    KCD2MP.npcTracked = {}
    for i = 1, 120 do local n = string.format("tzel_extra_man_%03d", i); ENTS[n] = mkEntity(n, i, 0, 0); KCD2MP.npcTracked[n] = {} end
    NOW = NOW + 0.5; KCD2MP_W138PushTrack()
    local t3 = emitted("npc_track", mark)
    local total, okLen, parts = 0, true, nil
    for _, l in ipairs(t3) do
        local g, p, n, names = l:match("^(%d+) (%d+) (%d+) (.*)$")
        parts = tonumber(n)
        if #l > 1300 then okLen = false end
        for _ in names:gmatch("[^,]+") do total = total + 1 end
    end
    check("b: 120 names: chunked lines, each under the ceiling, every name once", #t3 >= 2 and #t3 == parts and okLen and total == 120, #t3 .. " parts, " .. total .. " names")
    mark = #LOG
    KCD2MP.npcTracked = {}
    NOW = NOW + 0.5; KCD2MP_W138PushTrack()
    local t4 = emitted("npc_track", mark)
    check("b: an empty set is sent as '-'", #t4 == 1 and t4[1]:match(" 0 1 %-$") ~= nil, t4[1])
    KCD2MP.hitSensorOn = false; mark = #LOG
    KCD2MP.npcTracked = { tzel_maid = {} }; NOW = NOW + 20; KCD2MP_W138PushTrack()
    check("b: a joiner never pushes a set", #emitted("npc_track", mark) == 0)
    noErrs("b")
end

do -- (c) the dialogue edge
    reset(); clearLog(); NOW = 300
    KCD2MP.w138.dialog = nil
    KCD2MP_W138DialogTick()
    check("c: the first state is reported", emitted("w138_dialog")[1] == "0", emitted("w138_dialog")[1])
    KCD2MP_W138DialogTick(); KCD2MP_W138DialogTick()
    check("c: no change, nothing more", #emitted("w138_dialog") == 1)
    -- WO-147: a bark (a combat shout) sets IsInDialog too, without the dialogue camera's twin: no edge
    PLAYER_IN_DIALOG = true; KCD2MP_W138DialogTick()
    check("c: a bark (no dialogue camera) is no dialogue", #emitted("w138_dialog") == 1)
    ENTS["DialogTwin_Dude"] = mkEntity("DialogTwin_Dude", 0, 0, 0); KCD2MP_W138DialogTick()
    PLAYER_IN_DIALOG = false; ENTS["DialogTwin_Dude"] = nil; KCD2MP_W138DialogTick()
    local d = emitted("w138_dialog")
    check("c: in and out of a conversation: one edge each", #d == 3 and d[2] == "1" and d[3] == "0", table.concat(d, ","))
    noErrs("c")
end

local function joinerPuppet(name, x)
    ENTS[name] = mkEntity(name, x, 0, 0)
    KCD2MP_ApplyNpcState(name, x, 0, 0, 0, 100, 0, 0, 1, math.floor(NOW * 1000))
end

do -- (d) hold, don't hide
    reset(); clearLog(); NOW = 400
    KCD2MP.hitSensorOn = false
    joinerPuppet("tzel_man_10", 3)
    check("d: the copy is a puppet", KCD2MP.npcPuppets["tzel_man_10"] ~= nil)
    -- no hold: the 3 s rule, as before (single player / an ordinary stream loss)
    run(3.5)
    check("d: no hold: released for silence after 3 s (unchanged)", KCD2MP.npcPuppets["tzel_man_10"] == nil and logCount("NPC-SYNC release tzel_man_10") == 1)

    reset(); clearLog(); NOW = 500
    joinerPuppet("tzel_man_10", 3)
    run(1.0)
    KCD2MP_W138Hold(true, 2, true)   -- the host opened its inventory
    check("d: the hold is logged with the reasons", logCount("WO138-HOLD on: the host is paused (reasons 0x02)") == 1, lastLog("WO138-HOLD"))
    KCD2MP.npcSyncRunning = true
    check("d: the copy is paused (the reconcile sweep watches it)", KCD2MP._npcPaused["tzel_man_10"] ~= nil)
    for i = 1, 5 do run(2.0); KCD2MP_W138Hold(true, 2, true); KCD2MP._npcReconcileAt = -1e9; KCD2MP_NpcSyncTick() end   -- 11 s, the agent re-asserting every 2 s, the sweep each time
    check("d: held for 11 s with no packet: still a puppet (not hidden)", KCD2MP.npcPuppets["tzel_man_10"] ~= nil and logCount("NPC-SYNC release") == 0)
    check("d: ...and never parked / resumed by the reconcile backstop", logCount("MP-PAUSE-GAP") == 0 and cmdCount("wh_ai_ResumeNPC tzel_man_10") == 0)
    KCD2MP_W138Hold(false, 0, true)   -- the host resumed
    check("d: the resume is logged", logCount("WO138-HOLD off after") == 1 and logCount("the host resumed") == 1)
    run(2.0)
    check("d: after the resume the silence clock restarted: 2 s later still a puppet", KCD2MP.npcPuppets["tzel_man_10"] ~= nil)
    run(1.5)
    check("d: ...and 3 s after the resume with no stream: released (the 3 s rule is back)", KCD2MP.npcPuppets["tzel_man_10"] == nil)

    reset(); clearLog(); NOW = 600
    joinerPuppet("tzel_man_10", 3)
    KCD2MP_W138Hold(true, 1, true)
    run(5.0); KCD2MP_W138Hold(true, 1, true)
    run(1.0)
    KCD2MP_W138Hold(false, 0, false)   -- the host's link is lost
    check("d: link lost is logged", logCount("the host's link is lost") == 1)
    tick()
    check("d: link lost: released at once (the 3 s rule on the old clock)", KCD2MP.npcPuppets["tzel_man_10"] == nil)

    reset(); clearLog(); NOW = 700
    joinerPuppet("tzel_man_10", 3)
    KCD2MP_W138Hold(true, 8, true)   -- then the agent goes silent (never re-asserts)
    run(7.0)
    check("d: within 8 s of the last assert: held", KCD2MP.npcPuppets["tzel_man_10"] ~= nil)
    run(1.5)
    check("d: the agent stopped asserting: the hold lapses by itself and the copy goes", KCD2MP.npcPuppets["tzel_man_10"] == nil and KCD2MP.w138.hold == false)
    noErrs("d")
end

do -- (e) the inventory lever and the commands
    reset(); clearLog(); NOW = 800
    CVARS.wh_ui_ApsePauseRatio = 1000; KCD2MP.w138.apseOrig = nil
    KCD2MP_W138ApseRatio(true)
    check("e: in a session: the inventory's divide set to 1", tonumber(CVARS.wh_ui_ApsePauseRatio) == 1 and logCount("wh_ui_ApsePauseRatio 1000 -> 1") == 1, CVARS.wh_ui_ApsePauseRatio)
    KCD2MP_W138ApseRatio(true)
    check("e: again: no second change, the original kept", KCD2MP.w138.apseOrig == 1000 and logCount("WO138-LEVERS inventory") == 1)
    KCD2MP_W138ApseRatio(false)
    check("e: out of the session: the original is back", tonumber(CVARS.wh_ui_ApsePauseRatio) == 1000 and KCD2MP.w138.apseOrig == nil)
    KCD2MP_W138ApseRatio(false)
    check("e: off when nothing was changed: untouched", tonumber(CVARS.wh_ui_ApsePauseRatio) == 1000 and logCount("back to") == 1)
    check("e: mp_w138_status / mp_w138_native / mp_w138_levers registered",
        CCMDS["mp_w138_status"] ~= nil and CCMDS["mp_w138_status"].body == "KCD2MP_W138Status()"
        and CCMDS["mp_w138_native"] ~= nil and CCMDS["mp_w138_native"].body == "KCD2MP_W138SetNative(%line)"
        and CCMDS["mp_w138_levers"] ~= nil and CCMDS["mp_w138_levers"].body == "KCD2MP_W138SetLevers(%line)")
    local mark = #LOG
    KCD2MP_W138SetNative("off"); KCD2MP_W138SetNative("on"); KCD2MP_W138SetLevers("off"); KCD2MP_W138SetLevers("on")
    local w = emitted("w138", mark)
    check("e: the switches reach the agent", table.concat(w, "|") == "native off|native on|levers off|levers on", table.concat(w, "|"))
    KCD2MP_W138Status()
    check("e: the status line", logCount("WO138-STATUS sender=") == 1)
    check("e: both ship on, no hold, no native sender until the agent confirms one", KCD2MP.w138.nativeOn == true and KCD2MP_W138Holding() == false and KCD2MP_W138NativeActive() == false)
    noErrs("e")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
