-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-177 synthetic test (the game-side halves), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock). Driven by Test-WO177Synthetic.ps1.
NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}; LISTENERS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
CVARS = {}
System.GetCVar = function(n) return CVARS[n] ~= nil and tostring(CVARS[n]) or "0" end
System.SetCVar = function(n, v) CVARS[n] = v end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function(p, r) return {} end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.RemoveEntity = function(eid) end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.GetTerrainElevation = function(p) return 0 end
System.GetFrameTime = function() return 1 / 60 end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
UIAction.RegisterElementListener = function(tbl, el, inst, ev, fn) LISTENERS[#LISTENERS + 1] = { tbl = tbl, el = el, ev = ev, fn = fn } end
Calendar = mkstub()
Calendar.GetWorldTime = function() return 1000000 end
XGenAIModule = mkstub()
MSGS = {}; ENDCOMBAT = {}
XGenAIModule.MakeTableFromType = function(t) return { kind = t } end
XGenAIModule.SendMessageToEntityData = function(to, kind, t) MSGS[#MSGS + 1] = { to = to, kind = kind, t = t } end
XGenAIModule.TryEndCombat = function(w) ENDCOMBAT[#ENDCOMBAT + 1] = w; return true end
EnvironmentModule = { GetRainIntensity = function() return 0 end }
ItemManager = mkstub()

NEXTID = 5000
PX, PY, PZ = 100, 100, 10
IN_DIALOG = false
ORIG_TALKS = 0
P_DANGER = false
BasicAIActions = { OnTalk = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end,
                   OnChatWithFocus = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end }
player = { id = 1, class = "Player" }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PX, y = PY, z = PZ } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.GetDirectionVector = function() return { x = 1, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end, StandUp = function() end }
player.human = { IsInDialog = function() return IN_DIALOG end }
player.soul = { RestrictDialog = function() end, GetState = function() return 0 end,
                IsInCombatDanger = function() return P_DANGER end, IsInTenseCircumstance = function() return false end }
ENTS["Dude"] = player

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
local function countEvt(name, argPrefix, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " " .. (argPrefix or ""), 1, true) then n = n + 1 end
    end
    return n
end
local function runTimers()
    local t = TIMERS
    TIMERS = {}
    for _, x in ipairs(t) do x.f() end
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end

-- (A1) the partner's figure has a soul of its own
do
    check("S1: the twelve mod souls are defined", KCD2MP.partnerSouls ~= nil and #KCD2MP.partnerSouls == 12)
    local seen, okGuid = {}, true
    for i, s in ipairs(KCD2MP.partnerSouls) do
        if seen[s.guid] or s.soulName ~= "kdcmp_partner_" .. i or not s.guid:match("^4b43444d%-7301%-4d67%-b1a5%-9e2f6d8c0a%x%x$") or s.className ~= "NPC" then okGuid = false end
        seen[s.guid] = true
    end
    check("S2: distinct guids, the table's names, class NPC", okGuid)
    check("S3: default ON", KCD2MP.w177.figureSoul == true)
    check("S4: a name always picks the same soul", KCD2MP_PickFigureFace("synth-host").guid == KCD2MP_PickFigureFace("synth-host").guid)
    local kinds = {}
    for _, n in ipairs({ "a", "bb", "ccc", "Jonas", "Maintainer", "synth-host", "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9" }) do kinds[KCD2MP_PickFigureFace(n).guid] = true end
    local nk = 0; for _ in pairs(kinds) do nk = nk + 1 end
    check("S5: different names spread over several souls", nk >= 4, nk)
    check("S6: the pick is a mod soul, never a village roster soul", KCD2MP_PickFigureFace("synth-host").mod == true)
    check("S7: mp_figure_soul is a console command with its default in the help", CCMDS["mp_figure_soul"] ~= nil and CCMDS["mp_figure_soul"].help:find("default on", 1, true) ~= nil)
    check("S8: off = the old village roster", KCD2MP_W177SetFigureSoul("off") == true and KCD2MP_PickFigureFace("synth-host").mod == nil
        and KCD2MP_PickFigureFace("synth-host").guid == KCD2MP_PickFaceForPlayer("synth-host").guid)
    check("S9: a wrong word is refused", KCD2MP_W177SetFigureSoul("maybe") == false and KCD2MP.w177.figureSoul == false)
    check("S10: on again", KCD2MP_W177SetFigureSoul("on") == true and KCD2MP_PickFigureFace("synth-host").mod == true)
    noErrs("S")
end

-- (A5) no load while a fight is running
do
    P_DANGER = false
    KCD2MP.w177.hitAt, KCD2MP.w177.figHitAt = nil, nil
    check("L1: nothing running -> ok", KCD2MP_W177LoadReason() == "ok")
    P_DANGER = true
    check("L2: this Henry in combat danger -> fight", KCD2MP_W177LoadReason() == "fight")
    P_DANGER = false
    ENTS["kcd2mp_1"] = { id = 77, soul = { IsInCombatMode = function() return true end } }
    check("L3: a figure in combat mode -> fight", KCD2MP_W177LoadReason() == "fight")
    ENTS["kcd2mp_1"] = nil
    KCD2MP.w177.hitAt = NOW - 3
    check("L4: Henry hit 3 s ago -> hit", KCD2MP_W177LoadReason() == "hit")
    KCD2MP.w177.hitAt = NOW - 10.5
    check("L5: 10.5 s after the hit -> ok", KCD2MP_W177LoadReason() == "ok")
    KCD2MP.w177.figHitAt = NOW - 2
    check("L6: a figure hit 2 s ago -> hit", KCD2MP_W177LoadReason() == "hit")
    KCD2MP.w177.figHitAt = nil
    local mark = #LOG
    P_DANGER = true
    check("L7: the question is answered as the event w177_load_quiet and the wait is said once", KCD2MP_W177LoadQuiet("join") == "fight"
        and countEvt("w177_load_quiet", "join fight", mark) == 1 and lastLog("WO177-LOADWAIT join reason=fight", mark) ~= nil and #TOASTS >= 1)
    local toasts = #TOASTS
    KCD2MP_W177LoadQuiet("join")
    check("L8: asked again 1 s later the toast does not repeat", #TOASTS == toasts)
    P_DANGER = false
    check("L9: ok when it is over", KCD2MP_W177LoadQuiet("join") == "ok")
    local hp = 100
    player.actor.GetHealth = function() return hp end
    player.actor.IsUnconscious = function() return false end
    KCD2MP.w177.hitAt, KCD2MP.w177.hpPrev = nil, nil
    KCD2MP_ReadSelfVitals()
    hp = 80
    KCD2MP_ReadSelfVitals()
    check("L10: a drop in Henry's health is a hit (read in the vitals the emit loop already reads)", KCD2MP.w177.hitAt == NOW)
    KCD2MP.w177.hitAt = nil
    hp = 90
    KCD2MP_ReadSelfVitals()
    check("L11: healing is no hit", KCD2MP.w177.hitAt == nil)
    noErrs("L")
end

-- (B2) corpses agree
do
    local function npc(name, x, y, dead)
        local e = { id = #name * 7 + x, class = "NPC", GetName = function() return name end, GetWorldPos = function() return { x = x, y = y, z = 10 } end }
        e.actor = { IsDead = function() return dead end }
        return e
    end
    local removed = {}
    System.RemoveEntity = function(id) removed[#removed + 1] = id end
    local c = KCD2MP.w177.corpse
    check("C1: mp_corpse_sync is a console command, default on", CCMDS["mp_corpse_sync"] ~= nil and CCMDS["mp_corpse_sync"].help:find("default on", 1, true) ~= nil and c.on == true)
    check("C2: off / on / a wrong word", KCD2MP_W177SetCorpseSync("off") == true and c.on == false and KCD2MP_W177SetCorpseSync("on") == true and c.on == true and KCD2MP_W177SetCorpseSync("x") == false)
    -- the joiner asks about the dead copies near him, three at a time, never the same one again within 20 s
    local a, b, d, e4, live = npc("tzel_a", 105, 100, true), npc("tzel_b", 110, 100, true), npc("tzel_c", 90, 100, true), npc("tzel_d", 95, 105, true), npc("tzel_alive", 101, 100, false)
    ENTS["tzel_a"], ENTS["tzel_b"], ENTS["tzel_c"], ENTS["tzel_d"], ENTS["tzel_alive"] = a, b, d, e4, live
    System.GetEntitiesInSphere = function(p, r) return { a, b, d, e4, live, player } end
    c.asked = {}
    local t1 = KCD2MP_W177CorpseAskText(100)
    local n1 = 0; for _ in t1:gmatch("%S+") do n1 = n1 + 1 end
    check("C3: at most three dead copies per question, each as name:x:y in whole metres, never an alive one", n1 == 3 and t1:find("tzel_alive", 1, true) == nil and t1:match("^[%w_]+:%-?%d+:%-?%d+") ~= nil, t1)
    local t2 = KCD2MP_W177CorpseAskText(105)
    check("C4: the next question is the remaining one (nobody is asked twice within 20 s)", select(2, t2:gsub("tzel_", "")) == 1 and t2 ~= t1, t2)
    check("C5: nothing left to ask within 20 s", KCD2MP_W177CorpseAskText(110) == "")
    check("C6: due again after 20 s", KCD2MP_W177CorpseAskText(130) ~= "")
    -- the chain asks only as the shared world's joiner and emits one event per question
    KCD2MP_W134JoinerActive = function() return true end
    c.asked = {}; c.lastAskAt = nil
    local mark = #LOG
    KCD2MP_W177CorpseTick(c.gen)
    check("C7: the joiner's tick emits w177_corpse_ask", countEvt("w177_corpse_ask", "tzel_", mark) == 1)
    NOW = NOW + 2
    c.asked = {}
    mark = #LOG
    KCD2MP_W177CorpseTick(c.gen)
    check("C7b: the chain ticks every 2 s but the joiner asks only every 5 s (a second tick 2 s later asks nothing)", countEvt("w177_corpse_ask", "", mark) == 0 and c.tickS == 2 and c.askEveryS == 5)
    KCD2MP_W134JoinerActive = function() return false end
    mark = #LOG
    KCD2MP_W177CorpseTick(c.gen)
    check("C8: not a joiner of a shared world -> nothing is asked", countEvt("w177_corpse_ask", "", mark) == 0)
    -- the host answers
    PX, PY = 100, 100
    mark = #LOG
    KCD2MP_W177HostCorpseAsk(1, "tzel_a:105:100 tzel_alive:101:100 tzel_gone:106:100 tzel_far:900:900")
    local ans = lastLog("w177_corpse_ans", mark)
    check("C9: a corpse the host has -> 1, an alive one -> 3, one it lacks within 60 m of its player -> 2, one far from its player -> 0",
        ans ~= nil and ans:find("1 tzel_a=1 tzel_alive=3 tzel_gone=2 tzel_far=0", 1, true) ~= nil, ans)
    -- the joiner applies only a 2, only to a corpse
    removed = {}
    local nrem = KCD2MP_W177CorpseAnswer("tzel_a=1 tzel_alive=2 tzel_b=2 tzel_nobody=2 tzel_c=3 tzel_d=0")
    check("C10: only an answer 2 removes, only a dead copy here (an alive one and an unknown name are left)", nrem == 1 and #removed == 1 and removed[1] == b.id, tostring(nrem))
    check("C11: it is said", lastLog("WO177-CORPSE gone on the host: removed here npc=tzel_b", 0) ~= nil)
    KCD2MP_W177SetCorpseSync("off")
    check("C12: off -> an answer changes nothing", KCD2MP_W177CorpseAnswer("tzel_a=2") == 0 and #removed == 1)
    KCD2MP_W177SetCorpseSync("on")
    -- the host's own watch: a corpse seen within 55 m that is gone later is told to every joiner, once
    c.dead = {}
    System.GetEntitiesInSphere = function(p, r) return { a, b, player } end
    ENTS["tzel_a"], ENTS["tzel_b"] = a, b
    KCD2MP_W177HostWatch(200)
    mark = #LOG
    System.GetEntitiesInSphere = function(p, r) return { a, player } end
    ENTS["tzel_b"] = nil
    KCD2MP_W177HostWatch(205)
    check("C13: the host's corpse that went is told to every joiner (w177_corpse_gone name=2)", countEvt("w177_corpse_gone", "tzel_b=2", mark) == 1)
    mark = #LOG
    KCD2MP_W177HostWatch(210)
    check("C14: and only once", countEvt("w177_corpse_gone", "", mark) == 0)
    -- a corpse that is out of the scan but still in the world is not a removal; one that goes while the host is far away is not told
    c.dead = { tzel_x = { x = 100, y = 100, at = 0 }, tzel_far = { x = 900, y = 900, at = 0 } }
    ENTS["tzel_x"] = npc("tzel_x", 100, 100, false)
    mark = #LOG
    KCD2MP_W177HostWatch(220)
    check("C15: a corpse alive again is dropped from the watch, one far from the host is never reported", countEvt("w177_corpse_gone", "", mark) == 0 and c.dead.tzel_x == nil)
    System.GetEntitiesInSphere = function(p, r) return {} end
    noErrs("C")
end

-- (B1) the joiner's pickup of a world item: exact item, exact quantity
do
    local function apple(name, x, amt)
        local e = { id = 4000 + x * 100, class = "PickableItem", Properties = { sItemClassId = "apple-class", nAmount = amt }, npcOnly = false, item = { GetId = function() return 1 end } }
        e.GetName = function() return name end
        e.GetWorldPos = function() return { x = x, y = 0, z = 0 } end
        return e
    end
    local a1, a2, a3 = apple("apple1", 0.00, 1), apple("apple2", 0.05, 2), apple("apple3", 3.00, 1)
    System.GetEntitiesInSphere = function(p, r) return { a1, a2, a3 } end
    local nerr0 = #ERRS
    local e1 = W134.findItem("apple-class", 0.01, 0, 0, 0.35)
    check("P1: no amount asked -> the nearest item of the class (as before)", e1 == a1)
    local e2 = W134.findItem("apple-class", 0.01, 0, 0, 0.35, 2)
    check("P2: an amount asked -> the nearest of that amount within the match radius (the pile of two, not the single apple beside it)", e2 == a2)
    local e3 = W134.findItem("apple-class", 0.01, 0, 0, 0.35, 5)
    check("P3: an amount nobody has -> the nearest, as before (the amount never makes an item unknown)", e3 == a1)
    local e4 = W134.findItem("apple-class", 3.0, 0, 0, 0.35, 2)
    check("P4: the amount never reaches past the match radius", e4 == a3)
    -- the host's answer says what it matched, and both amounts
    check("P4b: findItem raised nothing", #ERRS == nerr0, ERRS[nerr0 + 1])
    ENTS["apple1"], ENTS["apple2"], ENTS["apple3"] = a1, a2, a3
    local mark = #LOG
    System.GetEntityByName = function(n) return ENTS[n] end
    local okFn, err = pcall(KCD2MP_W134HostItem, 1, "7", "apple-class", 0.01, 0, 0, 0, 2)
    check("P5: the host takes the item of the asked amount and says both amounts", okFn and lastLog("WO134-ITEM host-take", mark) ~= nil
        and lastLog("WO134-ITEM host-take", mark):find("amount joiner=2 host=2", 1, true) ~= nil, tostring(err) .. " " .. tostring(lastLog("WO134-ITEM host-take", mark)))
    check("P6: the answer ok goes to the agent (w134_ires)", countEvt("w134_ires", "1 7 ok apple-class", mark) == 1)
    System.GetEntitiesInSphere = function(p, r) return {} end
    noErrs("P")
end

-- (B3) a working NPC's tool swing
do
    local function npc(name, fight)
        local e = { id = 3000 + #name, class = "NPC", GetName = function() return name end, GetWorldPos = function() return { x = 50, y = 50, z = 10 } end }
        e.soul = { IsInCombatMode = function() return fight end, IsInCombatDanger = function() return false end }
        return e
    end
    local figure = { id = 777, class = "NPC", GetName = function() return "kcd2mp_1" end, GetWorldPos = function() return { x = 50, y = 50, z = 10 } end }
    local smith, bandit, villager = npc("ttac_blacksmith", false), npc("tapo_bandit_1", true), npc("tzel_man_2", false)
    KCD2MP.ghosts = KCD2MP.ghosts or {}
    KCD2MP.ghosts["1"] = { entity = figure }
    System.GetEntitiesInSphere = function(p, r) return { figure, smith, bandit, villager } end
    check("T1: mp_tool_swing is a console command, default on", CCMDS["mp_tool_swing"] ~= nil and CCMDS["mp_tool_swing"].help:find("default on", 1, true) ~= nil and KCD2MP.w177.calm.on == true)
    local names = KCD2MP_W177CalmNames()
    check("T2: the NPCs near a figure that are not in a fight are calm (the smith, the villager), the bandit is in a fight, the figure is neither",
        #names == 2 and names[1] ~= names[2] and names.fighting ~= nil and #names.fighting == 1 and names.fighting[1] == "tapo_bandit_1", #names)
    local mark = #LOG
    KCD2MP_W177CalmTick(KCD2MP.w177.calm.gen)
    check("T3: the tick tells the agent \"<calm> ! <fighting>\" (w177_calm)", countEvt("w177_calm", "", mark) == 1 and lastLog("w177_calm", mark):find(" ! tapo_bandit_1", 1, true) ~= nil, lastLog("w177_calm", mark))
    mark = #LOG
    KCD2MP.ghosts["1"] = nil
    KCD2MP_W177CalmTick(KCD2MP.w177.calm.gen)
    check("T4: no figure in the world -> nothing is listed", countEvt("w177_calm", "", mark) == 0)
    KCD2MP.ghosts["1"] = { entity = figure }
    mark = #LOG
    check("T5: off is accepted and told to the agent", KCD2MP_W177SetToolSwing("off") == true and countEvt("w177_calm_cfg", "off", mark) == 1)
    mark = #LOG
    KCD2MP_W177CalmTick(KCD2MP.w177.calm.gen)
    check("T6: off -> nothing is listed", countEvt("w177_calm", "", mark) == 0)
    check("T7: on again; a wrong word is refused", KCD2MP_W177SetToolSwing("on") == true and KCD2MP_W177SetToolSwing("sometimes") == false and KCD2MP.w177.calm.on == true)
    KCD2MP.ghosts["1"] = nil
    System.GetEntitiesInSphere = function(p, r) return {} end
    noErrs("T")
end

-- (status) the one line that says where each piece stands
do
    local mark = #LOG
    check("X1: mp_w177_status is a console command", CCMDS["mp_w177_status"] ~= nil)
    check("X2: it says every piece's state in one line and tells the agent", KCD2MP_W177Status() == true
        and lastLog("WO177-STATUS figure_soul=on corpse_sync=on", mark) ~= nil and lastLog("tool_swing=on", mark) ~= nil and countEvt("w177_status", "", mark) == 1,
        lastLog("WO177-STATUS", mark))
    noErrs("X")
end

-- (B4) mp_unstuck ends a station's use
do
    local aborted, stood = {}, 0
    local station = { id = 901, class = "InteractiveObjectEx", GetName = function() return "wash_trough_1" end,
                      AbortUse = function(self, user) aborted[#aborted + 1] = user and user.id or "nil" end }
    local bystander = { id = 902, class = "NPC", GetName = function() return "someone" end }
    System.GetEntitiesInSphere = function(p, r) return { station, bystander, player } end
    player.actor.StandUp = function() stood = stood + 1 end
    local mark = #LOG
    KCD2MP_W151Unstuck("")
    check("U1: step 1 calls the game's AbortUse on the interactive object within reach, with this Henry", #aborted == 1 and aborted[1] == 1, #aborted)
    check("U2: and stands him up", stood >= 1)
    check("U3: the station is named in the unstuck line", lastLog("WO151-UNSTUCK", mark) ~= nil and lastLog("WO151-UNSTUCK", mark):find("station-end(wash_trough_1)", 1, true) ~= nil, lastLog("WO151-UNSTUCK", mark))
    check("U4: step 1 is no hit reaction", countEvt("w151", "takedamage", mark) == 0)
    mark = #LOG
    KCD2MP_W151Unstuck("")
    check("U5: pressed again within 10 s (step 2): the hit reaction too, no damage (what freed the field guest)", countEvt("w151", "takedamage 0 0 0 0 0", mark) == 1)
    check("U6: a bystander without AbortUse is never touched", #aborted == 2)
    System.GetEntitiesInSphere = function(p, r) return {} end
    noErrs("U")
end

OUT = table.concat(RESULTS, "\n")
