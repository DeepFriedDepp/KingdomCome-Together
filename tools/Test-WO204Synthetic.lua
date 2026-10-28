-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-204 synthetic test: the Lua halves of WO-204, against the real kdcmp.lua under MoonSharp (the engine stubbed, a fake clock).
--   (a) the switches; (b) III.3 the listener's options, moves, votes, the cancel, the choice; (c) III.4 the lines and the earlier ones;
--   (d) III.3 the starter's vote icon; (e) IV.1 the join's safety timer renewed by the joiner's progress; (f) III.2 / IV.1 what is gone
-- What this proves: the Lua rules. Live evidence: docs/WO-204-findings.md.

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


-- (a) the switches: each its own console command, all on; WO-201's nearest_player_triggers off until its soak passes
do
    local n = 0
    for k, label in pairs(W204.NAMES) do
        n = n + 1
        check("a: " .. label .. " defaults on", KCD2MP_W204On(k) == true)
        check("a: " .. label .. " is registered", CCMDS[label] ~= nil, label)
    end
    check("a: 9 switches", n == 9, n)
    check("a: mp_w204_status is registered", CCMDS["mp_w204_status"] ~= nil)
    clearLog()
    KCD2MP_W204Set("conv_no_barks", "off")
    check("a: a switch goes to the agent", evts("w204_cfg")[1] == "conv_no_barks=off", evts("w204_cfg")[1])
    KCD2MP_W204Set("conv_no_barks", "on")
    check("a: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (b) III.3: the listener's options -- drawn through the HUD, moved and voted with his own listen actions, a vote cleared, the choice
do
    ERRS = {}; clearLog()
    local ui = {}
    UIAction.CallFunction = function(panel, inst, fn, a, b, c) ui[#ui + 1] = { fn = tostring(fn), a = a, b = b, c = c } end
    UIAction.SetArray = function(panel, inst, name, arr) ui[#ui + 1] = { fn = "SetArray:" .. tostring(name), arr = arr } end
    UIAction.SetVisible = function(panel, inst, clip, v) ui[#ui + 1] = { fn = "SetVisible:" .. tostring(clip), a = v } end
    local function last(fn) for i = #ui, 1, -1 do if ui[i].fn == fn then return ui[i] end end return nil end
    KCD2MP.W203.cam.listening = nil
    check("b: options for a conversation he does not listen to: nothing", KCD2MP_W204Options(1, 233, 1, "0~ui_x___seq1_A~0~-~0~0") == false)
    KCD2MP.W203.cam.listening = { starter = "1", cid = "233", anchor = "ttkc_barbora", hidden = {} }
    local ok = KCD2MP_W204Options(1, 233, 1, "0~ui_barbora___seq1_7WVl~0~-~0~0;1~=Leave (you go)~1~-~0~0;2~ui_barbora___seq3_x~0~skill_speech~2~0")
    local arr = last("SetArray:SequenceList")
    check("b: the rows go to the HUD's own list, in order, with their loc keys", ok and arr and arr.arr[1] == "@ui_barbora___seq1_7WVl" and arr.arr[6] == "Leave (you go)" and #arr.arr == 15,
          arr and table.concat({ tostring(arr.arr[1]), tostring(arr.arr[6]), tostring(#arr.arr) }, " "))
    check("b: the same marks (used, the skill icon, its difficulty)", arr and arr.arr[7] == true and arr.arr[13] == "skill_speech" and arr.arr[14] == 2)
    check("b: the list is shown (DialogRight) and the first row highlighted", last("SetVisible:DialogRight") and last("SetVisible:DialogRight").a == true
          and last("SetActiveSequenceChanged") and last("SetActiveSequenceChanged").a == 0)
    check("b: a move is the HUD's highlight", KCD2MP_W204OnAction("kcd2mp_listen_down", "press") == true and last("SetActiveSequenceChanged").a == 1)
    check("b: up from the top wraps to the bottom", KCD2MP_W204OnAction("kcd2mp_listen_up", "press") and KCD2MP_W204OnAction("kcd2mp_listen_up", "press") and last("SetActiveSequenceChanged").a == 2)
    KCD2MP_W204OnAction("kcd2mp_listen_down", "press"); KCD2MP_W204OnAction("kcd2mp_listen_down", "press")
    clearLog()
    check("b: confirm votes for the highlighted row", KCD2MP_W204OnAction("kcd2mp_listen_confirm", "press") == true and evts("w204_vote")[1] == "1 233 1 1", evts("w204_vote")[1])
    arr = last("SetArray:SequenceList")
    check("b: the voted row shows the vote icon, the others not", arr and arr.arr[8] == "kct_vote" and arr.arr[3] == "", arr and tostring(arr.arr[8]))
    check("b: the highlight stays on the voted row", last("SetActiveSequenceChanged").a == 1)
    KCD2MP_W204OnAction("kcd2mp_listen_down", "press")
    KCD2MP_W204OnAction("kcd2mp_listen_confirm", "press")
    arr = last("SetArray:SequenceList")
    -- WO-210 F.2c (mp_conv_mark_over_icon, on): the vote shows over a row's own icon while it applies ("couldnt see vote": every row of
    -- that decision was a skill check); off (the twin) the row keeps its own icon and the vote shows nowhere
    check("b: the vote shows over a row's own icon (WO-210 F.2c)", arr and arr.arr[13] == "kct_vote" and arr.arr[8] == "", arr and tostring(arr.arr[13]))
    KCD2MP_W210Set("conv_mark_over_icon", "off")
    KCD2MP_W204VoteIcon("0~ui_barbora___seq1_7WVl~0~-~0~0;1~=Leave (you go)~1~-~0~0;2~ui_barbora___seq3_x~0~skill_speech~2~0", 2, 2, true)   -- the starter's path: no vote event
    local arr2 = last("SetArray:SequenceList")
    check("b: mp_conv_mark_over_icon off: a row with its own icon keeps it (the twin)", arr2 and arr2.arr[13] == "skill_speech" and arr2.arr[8] == "")
    KCD2MP_W210Set("conv_mark_over_icon", "on")
    check("b: voting again moves the vote", evts("w204_vote")[2] == "1 233 1 2", evts("w204_vote")[2])
    check("b: the cancel clears a vote (not a leave)", KCD2MP_W204OnAction("kcd2mp_listen_cancel", "press") == true and evts("w204_vote")[3] == "1 233 1 clear", evts("w204_vote")[3])
    check("b: with no vote the cancel is WO-203B's (it leaves)", KCD2MP_W204OnAction("kcd2mp_listen_cancel", "press") == false)
    check("b: a release is not a press", KCD2MP_W204OnAction("kcd2mp_listen_down", "release") == true and #evts("w204_vote") == 3)
    check("b: another conversation's choice: nothing", KCD2MP_W204Chosen(1, 999, 1, 0) == false)
    KCD2MP_W204OnAction("kcd2mp_listen_confirm", "press")   -- the vote on row 2 again
    TIMERS = {}
    TOASTS = {}
    KCD2MP_ShowNativeToast = function(t) TOASTS[#TOASTS + 1] = t end
    check("b: the choice is highlighted and told", KCD2MP_W204Chosen(1, 233, 1, 2) == true and last("SetActiveSequenceChanged").a == 2 and TOASTS[1] == "Your pick", TOASTS[1])
    local due = TIMERS; TIMERS = {}
    for _, t in ipairs(due) do pcall(t.f) end
    check("b: ...and the list goes after it", W204.conv.rows == nil and last("ClearDialogSequences") ~= nil)
    KCD2MP_W204Options(1, 233, 2, "0~ui_a___seq1_q~0~-~0~0;1~ui_a___seq2_q~0~-~0~0")
    KCD2MP_W204Chosen(1, 233, 2, 0)
    check("b: the partner's pick is told as theirs", TOASTS[#TOASTS] == "Their pick", TOASTS[#TOASTS])
    check("b: no listen action is taken while no options are up", (function() W204.conv.rows = nil; return KCD2MP_W204OnAction("kcd2mp_listen_up", "press") == false end)())
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (c) III.4: the lines -- the subtitle through the HUD with the line's loc key, the earlier ones marked and paced
do
    ERRS = {}; TIMERS = {}
    local ui = {}
    UIAction.CallFunction = function(panel, inst, fn, a, b, c) ui[#ui + 1] = { fn = tostring(fn), a = a, b = b, c = c } end
    KCD2MP.W203.cam.listening = { starter = "1", cid = "233", anchor = "ttkc_barbora", hidden = {} }
    check("c: a line of the conversation he listens to: its subtitle", KCD2MP_W204Line(1, 233, "", false, "zpet_hejtman_s_tak_co_syn_JIUh", 2400, false) == true
          and ui[#ui].fn == "SetSubtitles" and ui[#ui].a == "@zpet_hejtman_s_tak_co_syn_JIUh", ui[#ui] and tostring(ui[#ui].a))
    check("c: the starter's Henry's line is the player's (his name)", KCD2MP_W204Line(1, 233, "Partner", true, "hen_a_x", 1000, false) and ui[#ui].b == "Partner" and ui[#ui].c == true)
    check("c: a line with a broken key: nothing", KCD2MP_W204Line(1, 233, "", false, "bad key<>", 1000, false) == false)
    check("c: another conversation's line: nothing", KCD2MP_W204Line(1, 234, "", false, "a_b", 1000, false) == false)
    local before = #ui
    TIMERS = {}
    KCD2MP_W204Line(1, 233, "", false, "early_one", 1000, true)
    KCD2MP_W204Line(1, 233, "", false, "early_two", 1000, true)
    check("c: the earlier lines wait their turn (none at once)", #ui == before and #TIMERS == 2, #TIMERS)
    check("c: ...0.8 s apart", TIMERS[2] and TIMERS[1] and TIMERS[2].ms - TIMERS[1].ms >= 700, TIMERS[2] and TIMERS[2].ms)
    local t = TIMERS; TIMERS = {}
    for _, x in ipairs(t) do pcall(x.f) end
    check("c: ...each marked (earlier)", ui[#ui].fn == "SetSubtitles" and ui[#ui].b == "(earlier)" and ui[#ui].a == "@early_two", ui[#ui] and tostring(ui[#ui].a))
    KCD2MP_W204ConvClear()
    check("c: leaving clears the subtitle", ui[#ui].fn == "ClearSubtitles")
    check("c: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (d) III.3, the starter: the listener's vote in his own options -- the copied rows again with the icon, the highlight put back
do
    ERRS = {}
    local ui = {}
    UIAction.CallFunction = function(panel, inst, fn, a, b, c) ui[#ui + 1] = { fn = tostring(fn), a = a, b = b, c = c } end
    UIAction.SetArray = function(panel, inst, name, arr) ui[#ui + 1] = { fn = "SetArray:" .. tostring(name), arr = arr } end
    check("d: the vote icon in the starter's voted row", KCD2MP_W204VoteIcon("0~ui_a___seq1_q~0~-~0~0;1~ui_a___seq2_q~1~-~0~0", 1, 0, false) == true
          and ui[1].arr[8] == "kct_vote" and ui[1].arr[3] == "")
    check("d: on the side the starter's rows were shown", ui[2].fn == "SetDialogSequences" and ui[2].a == false)
    check("d: the starter's own highlight put back", ui[3].fn == "SetActiveSequenceChanged" and ui[3].a == 0)
    ui = {}
    KCD2MP_W204VoteIcon("0~ui_a___seq1_q~0~-~0~0;1~ui_a___seq2_q~1~-~0~0", -1, 1, true)
    check("d: a cleared vote: no icon", ui[1].arr[8] == "" and ui[1].arr[3] == "")
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (e) IV.1: the joiner's load progress renews the paused join's safety timer
do
    ERRS = {}; TIMERS = {}
    local w = KCD2MP.w123
    w.paused, w.joinId, w.safetyGen = true, "00ab12cd", 5
    KCD2MP_JoinProgress("00ab12cd", 100, "loading")
    check("e: a progress report arms a 75 s safety timer", #TIMERS == 1 and TIMERS[1].ms == 75000 and w.safetyGen == 6, #TIMERS)
    KCD2MP_JoinProgress("00ab12cd", 100, "loading")
    check("e: ...the newest one wins (the older is stale)", #TIMERS == 2 and w.safetyGen == 7)
    w.paused = false
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (f) III.2: the F2 vote panel and the overheard subtitles are gone; the short join hold's switch is gone
do
    check("f: no vote panel, no overheard subtitle", KCD2MP_W201VoteShow == nil and KCD2MP_W201Subtitle == nil and KCD2MP_W201DrawUI == nil)
    check("f: no mp_join_short_hold", W202.NAMES.join_short_hold == nil and CCMDS["mp_join_short_hold"] == nil)
    check("f: nearest_player_triggers (nearest_player_v2) is on: its time-skip soak passed (S1)", KCD2MP_W201On("nearest_player_triggers") == true)
end

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
