-- WO-147 synthetic test, against the real kdcmp.lua under MoonSharp.
-- Harness copied from Test-WO138Synthetic.lua.
--   (a) the joiner's hostile copies: the host's copies near this player with the
--       game's own relationship to this player (an encounter animal is always an
--       enemy), alive or down, within the range; dead, far and non-copies left out;
--       nothing while mp_hostile_engage is off
--   (b) a forced leash pull ends this player's conversation first; a bark
--       (no dialogue camera) is no conversation
--   (c) the switches: commands registered, settings flip, the agent is told
--   (d) the frame backlog: a puppet is released for silence on the agent's word while
--       it is fresh; the 3 s rule when the agent is gone or mp_npc_catchup is off
--   (e) a freeze (a stretch the puppet tick did not run) is no silence
-- What this proves: the Lua half. Live evidence: docs/WO-147-findings.md.

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


player.soul = { GetId = function() return "henry-soul" end }
INTERRUPTS = 0
player.human.InterruptDialogs = function() INTERRUPTS = INTERRUPTS + 1; PLAYER_IN_DIALOG = false; ENTS["DialogTwin_Dude"] = nil end

local function copy(name, x, rel, cls)
    local e = mkEntity(name, x, 0, 0)
    e.class = cls or "NPC"
    e.soul.GetRelationship = function(self, other)
        if other ~= "henry-soul" then error("wrong soul " .. tostring(other)) end
        if rel == "error" then error("no relationship for this soul") end
        return rel
    end
    e.soul.IsPublicEnemy = function(self) return rel == "enemy" end
    ENTS[name] = e
    KCD2MP.npcPuppets[name] = { cx = x, cy = 0, cz = 0 }
    return e
end

local function hostiles(tok)
    local mark = #LOG
    KCD2MP_W147Hostiles(tok)
    local r = emitted("wo124_reply", mark)
    return r[1]
end

do -- (a) the hostile copies
    reset(); clearLog(); NOW = 100
    KCD2MP.w147.hostileEngage = true
    KCD2MP.weaponDrawn = true
    copy("prepadeni_bandit_1", 4, "enemy")                      -- a bandit: a public enemy (its relationship reads 0 live)
    copy("tzel_man_3", 3, 0.4)
    copy("tzel_angry", 5, -0.6)
    copy("wolf_pack_2", 9.5, "error", "Wolf")                   -- an animal: never asked, the class decides
    copy("prepadeni_bandit_far", 20, -1)
    local down = copy("prepadeni_bandit_down", 6, -1)
    down.actor.IsUnconscious = function() return true end
    copy("prepadeni_bandit_dead", 5, -1); KCD2MP.npcPuppets["prepadeni_bandit_dead"].dead = true
    ENTS["tzel_not_a_copy"] = mkEntity("tzel_not_a_copy", 2, 0, 0)  -- this world's own NPC, not the host's copy
    local r = hostiles("t1")
    check("a: the reply carries the token, the weapon and the switch", r ~= nil and r:match("^t1 drawn=1 on=1 n=5 list=") ~= nil, r)
    local seen = {}
    for item in ((r or ""):match("list=(.*)$") or ""):gmatch("[^;]+") do
        local n, rel, d, alive = item:match("^([%w_]+):([%-%d%.]+):([%d%.]+):(%d)$")
        if n then seen[n] = { rel = tonumber(rel), d = tonumber(d), alive = alive } end
    end
    check("a: a bandit (a public enemy) is an enemy", seen.prepadeni_bandit_1 ~= nil and seen.prepadeni_bandit_1.rel == -1 and seen.prepadeni_bandit_1.d == 4.0)
    check("a: a villager turned on this player is an enemy by the relationship", seen.tzel_angry ~= nil and seen.tzel_angry.rel == -0.6)
    check("a: a villager is listed with a friendly relationship", seen.tzel_man_3 ~= nil and seen.tzel_man_3.rel == 0.4)
    check("a: a wolf is an enemy by its class", seen.wolf_pack_2 ~= nil and seen.wolf_pack_2.rel == -1 and seen.wolf_pack_2.alive == "1")
    check("a: a down copy is listed as not alive", seen.prepadeni_bandit_down ~= nil and seen.prepadeni_bandit_down.alive == "0")
    check("a: far, dead and non-copies are left out", seen.prepadeni_bandit_far == nil and seen.prepadeni_bandit_dead == nil and seen.tzel_not_a_copy == nil)
    KCD2MP.weaponDrawn = false
    check("a: the weapon away is reported", (hostiles("t2") or ""):match("^t2 drawn=0 ") ~= nil)
    KCD2MP.w147.hostileEngage = false
    check("a: mp_hostile_engage off: nothing listed", hostiles("t3") == "t3 drawn=0 on=0 n=0 list=", hostiles("t4"))
    KCD2MP.w147.hostileEngage = true
    noErrs("a")
end

do -- (b) a forced pull ends the conversation first
    reset(); clearLog(); NOW = 200; INTERRUPTS = 0
    PLAYER_IN_DIALOG = true; ENTS["DialogTwin_Dude"] = mkEntity("DialogTwin_Dude", 0, 0, 0)
    local mark = #LOG
    KCD2MP_W147EndDialog("e1")
    local r = emitted("wo124_reply", mark)[1]
    check("b: in a conversation: it is interrupted", INTERRUPTS == 1 and r == "e1 was=yes how=interrupted in_dialog=no", r)
    check("b: the line is logged", logCount("WO147-ENDDIALOG was=yes how=interrupted in_dialog=no") == 1)
    PLAYER_IN_DIALOG = true   -- a combat shout: IsInDialog without the dialogue camera
    mark = #LOG
    KCD2MP_W147EndDialog("e2")
    r = emitted("wo124_reply", mark)[1]
    check("b: a bark is no conversation: nothing interrupted", INTERRUPTS == 1 and r == "e2 was=no how=none in_dialog=no", r)
    PLAYER_IN_DIALOG = false
    noErrs("b")
end

do -- (c) the switches
    check("c: the three commands are registered with an unquoted %line",
        CCMDS["mp_hostile_engage"] ~= nil and CCMDS["mp_hostile_engage"].body == "KCD2MP_SetHostileEngage(%line)"
        and CCMDS["mp_quest_safety"] ~= nil and CCMDS["mp_quest_safety"].body == "KCD2MP_SetQuestSafety(%line)"
        and CCMDS["mp_leash_cap_s"] ~= nil and CCMDS["mp_leash_cap_s"].body == "KCD2MP_SetLeashCap(%line)")
    check("c: all three ship on (60 s)", KCD2MP.w147.hostileEngage == true and KCD2MP.w147.questSafety == true and KCD2MP.w147.leashCapS == 60)
    clearLog()
    local mark = #LOG
    KCD2MP_SetHostileEngage("off"); KCD2MP_SetQuestSafety("off"); KCD2MP_SetLeashCap("0")
    local c = emitted("w147_cfg", mark)
    check("c: each switch tells the agent", #c == 3 and c[3] == "hostile_engage=off quest_safety=off leash_cap_s=0 range_m=12 rel_max=-0.10 npc_catchup=on", c[3])
    check("c: a bad value changes nothing", KCD2MP_SetLeashCap("abc") == false and KCD2MP_SetLeashCap("-5") == false and KCD2MP_SetQuestSafety("maybe") == false and KCD2MP.w147.leashCapS == 0)
    KCD2MP_SetHostileEngage(nil); KCD2MP_SetQuestSafety("%line")
    check("c: bare = report only", KCD2MP.w147.hostileEngage == false and KCD2MP.w147.questSafety == false and logCount("WO147-TOGGLE mp_quest_safety") == 2)
    KCD2MP_SetHostileEngage("on"); KCD2MP_SetQuestSafety("on"); KCD2MP_SetLeashCap("60")
    check("c: back on", KCD2MP.w147.hostileEngage == true and KCD2MP.w147.questSafety == true and KCD2MP.w147.leashCapS == 60)
    mark = #LOG
    KCD2MP_SetNpcCatchup("off")
    local c2 = emitted("w147_cfg", mark)
    check("c: mp_npc_catchup is registered and reaches the agent",
        CCMDS["mp_npc_catchup"] ~= nil and CCMDS["mp_npc_catchup"].body == "KCD2MP_SetNpcCatchup(%line)"
        and #c2 == 1 and c2[1]:find("npc_catchup=off", 1, true) ~= nil, c2[1])
    KCD2MP_SetNpcCatchup("on")
    check("c: mp_npc_catchup ships on", KCD2MP.w147.npcCatchup == true)
    noErrs("c")
end

local function puppet(name, x)
    ENTS[name] = mkEntity(name, x, 0, 0)
    KCD2MP_ApplyNpcState(name, x, 0, 0, 0, 100, 0, 0, 1, math.floor(NOW * 1000))
end
local function freshSilence()
    local ns = KCD2MP.npcSilence
    ns.tickAt = nil; ns.agentAt = -1e9; ns.silent = {}; ns.nSilent = 0; ns.wordOn = false; ns.gaps = 0
end
local function word() KCD2MP_NpcSilenceAgent(4000) end

do -- (d) the frame backlog: a puppet is released for silence on the agent's word
    reset(); clearLog(); freshSilence(); NOW = 900
    KCD2MP.hitSensorOn = false; KCD2MP.w147.npcCatchup = true
    puppet("tzel_man_20", 3)
    check("d: a puppet", KCD2MP.npcPuppets["tzel_man_20"] ~= nil)
    word()
    run(4.0, word)   -- the agent runs 4 s behind: this puppet's samples wait in its queue
    check("d: the agent's word fresh: no release after 4 s of quiet here", KCD2MP.npcPuppets["tzel_man_20"] ~= nil)
    check("d: the word is logged once", logCount("NPC-SYNC agent silence word on") == 1)
    KCD2MP_NpcStreamSilent("tzel_man_20")
    run(0.2, word)
    check("d: the agent says silent: released", KCD2MP.npcPuppets["tzel_man_20"] == nil and logCount("NPC-SYNC release tzel_man_20") == 1)

    reset(); clearLog(); freshSilence(); NOW = 1000
    puppet("tzel_man_21", 3)
    word(); KCD2MP_NpcStreamSilent("tzel_man_21")
    NOW = NOW + 0.1
    KCD2MP_ApplyNpcState("tzel_man_21", 3.2, 0, 0, 0, 100, 0, 0, 2, math.floor(NOW * 1000))   -- a newer sample after the mark
    run(1.0, word)
    check("d: a mark older than the newest sample releases nothing", KCD2MP.npcPuppets["tzel_man_21"] ~= nil)

    reset(); clearLog(); freshSilence(); NOW = 1100
    puppet("tzel_man_22", 3)
    word()
    run(6.0)   -- no word any more
    check("d: a word 6 s old is still fresh (a hitch outlives nothing)", KCD2MP.npcPuppets["tzel_man_22"] ~= nil)
    run(4.5)
    check("d: no word for 10 s (the agent gone): the 3 s rule releases", KCD2MP.npcPuppets["tzel_man_22"] == nil)

    reset(); clearLog(); freshSilence(); NOW = 1200
    puppet("tzel_man_23", 3)
    run(29.0, word)
    check("d: fresh word, 29 s quiet here: kept", KCD2MP.npcPuppets["tzel_man_23"] ~= nil)
    run(1.5, word)
    check("d: fresh word, 30 s quiet here: released (the safety)", KCD2MP.npcPuppets["tzel_man_23"] == nil)

    reset(); clearLog(); freshSilence(); NOW = 1300
    puppet("tzel_man_24", 3)
    word(); KCD2MP_NpcSilenceAgent(-1)
    run(3.5)
    check("d: the agent's word off: the 3 s rule releases", KCD2MP.npcPuppets["tzel_man_24"] == nil and logCount("NPC-SYNC agent silence word off") == 1)

    reset(); clearLog(); freshSilence(); NOW = 1400
    KCD2MP.w147.npcCatchup = false
    puppet("tzel_man_25", 3)
    run(3.5, word)
    check("d: mp_npc_catchup off here: the 3 s rule even with a fresh word", KCD2MP.npcPuppets["tzel_man_25"] == nil)
    KCD2MP.w147.npcCatchup = true
    noErrs("d")
end

do -- (e) a freeze is no silence
    reset(); clearLog(); freshSilence(); NOW = 1500
    puppet("tzel_man_26", 3)
    run(0.5)        -- the chain runs (no agent word: the 3 s rule)
    NOW = NOW + 10  -- the game froze 10 s: no tick ran
    run(0.2)
    check("e: a 10 s freeze releases nothing", KCD2MP.npcPuppets["tzel_man_26"] ~= nil)
    check("e: the gap is logged", logCount("NPC-SYNC tick gap") == 1, lastLog("NPC-SYNC tick gap"))
    run(3.5)        -- then a real silence
    check("e: a real silence after it: the 3 s rule releases", KCD2MP.npcPuppets["tzel_man_26"] == nil)

    reset(); clearLog(); freshSilence(); NOW = 1600
    KCD2MP.w147.npcCatchup = false
    puppet("tzel_man_27", 3)
    run(0.5); NOW = NOW + 10; run(0.2)
    check("e: mp_npc_catchup off: as before (released after the freeze)", KCD2MP.npcPuppets["tzel_man_27"] == nil)
    KCD2MP.w147.npcCatchup = true

    reset(); clearLog(); freshSilence(); NOW = 1700
    puppet("tzel_man_28", 3)
    run(0.5); NOW = NOW + 4; run(0.2)
    check("e: a 4 s hitch is no freeze: the 3 s rule as before (no agent)", KCD2MP.npcPuppets["tzel_man_28"] == nil and logCount("NPC-SYNC tick gap") == 0)
    noErrs("e")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
