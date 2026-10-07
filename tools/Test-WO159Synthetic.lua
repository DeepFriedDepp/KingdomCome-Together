-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-159 synthetic test (the main menu's Start Game / Join Game), against the real kdcmp.lua under MoonSharp.
--
--   (N) a game nobody armed: loading the mod touches no menu function and registers no listener
--   (R) the launcher's first tick: listeners, the root page in our order, the shipped entries greyed, no debug entries
--   (W) menu-only: a player entity or a running load leaves the menu alone
--   (H) Start Game: the host page (worlds, New adventure, the hidden-saves note), a load, the playstyle page, the
--       prologue page (Skip on top, Watch with its length)
--   (G) the game's own pages: no redraw while one is open; Back to the root redraws on the next tick
--   (J) Join Game: the new-character default and its playstyle, waiting + Cancel, joining with the join's own line
--   (D) disarm before a load; nothing sends input to the menu
--   (P) the prologue's rendered cutscenes in the world: one after another, hold E for a second skips, a join waits
--       ("prologue", minutes left), a dead timer chain ends it
-- Driven by Test-WO159Synthetic.ps1 through the WO-77 MoonSharp driver.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ERRS = {}; CMDS = {}; CCMDS = {}; UI = {}; LISTEN = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetEntityByName = function(n) return nil end
System.GetEntitiesInSphere = function() return {} end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub(); Calendar = mkstub(); XGenAIModule = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(el, inst, fn, ...) UI[#UI + 1] = { el = el, fn = fn, args = { ... } } end
UIAction.RegisterElementListener = function(tbl, el, inst, ev, cb) LISTEN[#LISTEN + 1] = { tbl = tbl, el = el, ev = ev, cb = cb } end
LOADING = false
Game = mkstub()
Game.IsLoadingEngineSaveGame = function() return LOADING end

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return unpack(r)
end

-- the menu's startup runs at mod load; whatever it called is not ours to judge
local UI_AT_LOAD, LISTEN_AT_LOAD

-- @@KDCMP@@

player = nil   -- the main menu: no player entity

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function menuCalls(from)
    local o = {}
    for i = (from or 0) + 1, #UI do if UI[i].el == "Menu" then o[#o + 1] = UI[i] end end
    return o
end
-- the buttons of the last build: { id, cont, text, tip, off } in order
local function lastPage()
    local start = 0
    for i = #UI, 1, -1 do if UI[i].fn == "ClearAll" then start = i; break end end
    local o, head = {}, nil
    for i = start, #UI do
        local c = UI[i]
        if c.fn == "PreparePage" then head = c.args[4] end
        if c.fn == "AddBasicButton" then o[#o + 1] = { id = c.args[1], cont = c.args[2], text = c.args[3], tip = c.args[4], off = c.args[5] } end
    end
    return o, head
end
local function find(page, id) for _, b in ipairs(page) do if b.id == id then return b end end return nil end
local function lastSel()
    for i = #UI, 1, -1 do if UI[i].fn == "SelectButton" then return UI[i].args[1] end end
    return nil
end
local function events(name)
    local o = {}
    for _, l in ipairs(LOG) do
        local n, a = l:match("^%[KCD2%-MP%-EVT%] v1 %d+ (%S+) (.*)$")
        if n == name then o[#o + 1] = a end
    end
    return o
end
local function press(id) KCD2MP.w159.OnMenu(KCD2MP.w159, "Menu", 0, "OnButton", { [0] = id }) end
local function confirm(id, res) KCD2MP.w159.OnMenu(KCD2MP.w159, "Menu", 0, "OnConfirm", { [0] = id, [1] = res }) end
local function count(fn, from) local n = 0; for i = (from or 0) + 1, #UI do if UI[i].fn == fn then n = n + 1 end end return n end

-- (N) nobody armed it -------------------------------------------------------------------------------------------------------
do
    local mine = 0
    for _, c in ipairs(UI) do if c.el == "Menu" then mine = mine + 1 end end
    check("N1: loading the mod calls no function of the game's menu", mine == 0, mine)
    check("N2: ...and registers no menu listener", #LISTEN == 0, #LISTEN)
    check("N3: the menu state starts unarmed", KCD2MP.w159.armed == false)
    local before = #LOG
    press("MP_StartGame")
    check("N4: an event while unarmed does nothing", #UI == 0 and #events("w159") == 0)
end

-- (R) the launcher's first tick --------------------------------------------------------------------------------------------
do
    ERRS = {}
    KCD2MP_W159Model({ sig = "h1", role = "host", saves = {}, newadv = { pl = 3, name = "permanent002" } })
    local r = KCD2MP_W159Tick()
    check("R1: the first tick arms at the menu", r == "root" and KCD2MP.w159.armed == true, r)
    local evs = {}
    for _, l in ipairs(LISTEN) do if l.el == "Menu" then evs[l.ev] = true end end
    check("R2: listeners on OnButton / OnConfirm / OnCreditsHide / OnHelpOverlayClose", evs.OnButton and evs.OnConfirm and evs.OnCreditsHide and evs.OnHelpOverlayClose)
    local p = lastPage()
    check("R3: our two entries first", p[1] and p[1].id == "MP_StartGame" and p[2].id == "MP_JoinGame", p[1] and p[1].id)
    check("R4: Continue / New Game / Load Game greyed with the hint", find(p, "Continue").off and find(p, "NewGame").off and find(p, "LoadGame").off
        and find(p, "Continue").tip == KCD2MP_W159_HINT)
    local dbg = false
    for _, b in ipairs(p) do if b.id:find("NewGameDebug", 1, true) then dbg = true end end
    check("R5: no Modding Tools debug entry", not dbg)
    check("R6: Settings / Help / DLCs / Credits keep their own ids, enabled", find(p, "Settings") and not find(p, "Settings").off and find(p, "HelpOverlays") and find(p, "DLC") and find(p, "Credits"))
    check("R7: Quit in the bottom container", find(p, "Exit") and find(p, "Exit").cont == 1)
    check("R8: the host's Start Game is enabled and selected; Join Game greyed", not find(p, "MP_StartGame").off and find(p, "MP_JoinGame").off and lastSel() == "MP_StartGame")
    check("R9: the disclaimer is on the selected entry", find(p, "MP_StartGame").tip:find("not affiliated", 1, true) ~= nil)
    check("R10: the game's own text keys", find(p, "Continue").text == "@ui_Continue" and find(p, "Exit").text == "@ui_Exit")
    local n = count("ClearAll")
    KCD2MP_W159Tick()
    check("R11: a second tick with nothing new draws nothing", count("ClearAll") == n)
    check("R12: no swallowed Lua errors", #ERRS == 0, ERRS[1])
end

-- (W) menu-only ---------------------------------------------------------------------------------------------------------------
do
    local n = count("ClearAll")
    player = { id = 1 }
    check("W1: a player entity: away, nothing drawn", KCD2MP_W159Tick() == "away" and count("ClearAll") == n and KCD2MP.w159.armed == false)
    player = nil
    LOADING = true
    check("W2: a load running: away", KCD2MP_W159Tick() == "away" and count("ClearAll") == n)
    LOADING = false
    check("W3: back at the menu: re-armed, the root drawn again", KCD2MP_W159Tick() == "root" and count("ClearAll") == n + 1)
end

-- (H) Start Game --------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    KCD2MP_W159Model({ sig = "h2", role = "host", saves = { { pl = 2, name = "autosave118", label = "Playline 2 (Oct 07 14:02)" },
        { pl = 0, name = "quicksave030", label = "Playline 0 (Sep 30 21:10)" } },
        hidden = "3 saves are copies of a host's world or from the regular game.", newadv = { pl = 3, name = "permanent002" },
        styles = { soldier = true, scout = true }, recapMin = 16 })
    KCD2MP_W159Tick()
    press("MP_StartGame")
    local p, head = lastPage()
    check("H1: the host page, built at once", KCD2MP.w159.page == "host" and head == "Start Game")
    check("H2: worlds first, newest first", p[1].id == "MP_Load_1" and p[1].text == "Playline 2 (Oct 07 14:02)" and p[2].id == "MP_Load_2")
    check("H3: New adventure enabled", find(p, "MP_NewAdv") and not find(p, "MP_NewAdv").off)
    check("H4: the hidden saves named, greyed", find(p, "MP_Hidden") and find(p, "MP_Hidden").off and find(p, "MP_Hidden").tip:find("copies", 1, true))
    check("H5: Back is ours (the game's own Back would leave the page)", find(p, "MP_Back") and find(p, "MP_Back").cont == 1 and find(p, "Back") == nil)
    check("H6: the newest world selected", lastSel() == "MP_Load_1")
    press("MP_Load_2")
    local e = events("w159")
    check("H7: a world: the launcher is told which", e[#e] == "load 0 quicksave030", e[#e])
    press("MP_NewAdv")
    p, head = lastPage()
    check("H8: New adventure: the playstyle page", KCD2MP.w159.page == "style" and head == "Choose your playstyle"
        and p[1].id == "MP_Style_soldier" and p[2].id == "MP_Style_adviser" and p[3].id == "MP_Style_scout")
    check("H8b: each playstyle says what it raises", find(p, "MP_Style_scout").tip:find("stealth", 1, true) ~= nil)
    check("H8c: a playstyle whose start save is not installed is greyed", find(p, "MP_Style_adviser").off and not find(p, "MP_Style_soldier").off)
    local nev = #events("w159")
    press("MP_Style_adviser")
    check("H8d: ...and pressing it does nothing", #events("w159") == nev and KCD2MP.w159.page == "style")
    press("MP_Style_scout")
    p, head = lastPage()
    check("H9: then the prologue page: Skip on top and selected, Watch with its length", KCD2MP.w159.page == "prologue" and p[1].id == "MP_Prologue_skip"
        and lastSel() == "MP_Prologue_skip" and p[2].text == "Watch the prologue's cutscenes (16 min)")
    check("H9b: Skip recommends itself for a partner; Watch says a partner waits and how to skip", p[1].tip:find("partner", 1, true)
        and p[2].tip:find("Hold E", 1, true) and p[2].tip:find("no conversations or choices", 1, true))
    press("MP_Back")
    check("H9c: Back from the prologue page: the playstyles again", KCD2MP.w159.page == "style")
    press("MP_Style_scout")
    press("MP_Prologue_watch")
    e = events("w159")
    check("H10: the launcher is told the playstyle and Watch", e[#e] == "newadv scout watch", e[#e])
    press("MP_Prologue_skip")
    e = events("w159")
    check("H10b: ...or Skip", e[#e] == "newadv scout skip", e[#e])
    check("H10c: no question box any more (the pages are the question)", count("AddConfirmation") == 0)
    press("MP_Back"); press("MP_Back")
    check("H10d: Back, Back: the playstyles, then Start Game", KCD2MP.w159.page == "host")
    -- no start save: New adventure greyed with the reason; no world: New adventure selected
    KCD2MP_W159Model({ sig = "h3", role = "host", saves = {}, newwhy = "All five save slots are in use." })
    KCD2MP_W159Tick()
    p = lastPage()
    check("H11: no new adventure: greyed, the reason as its tip", find(p, "MP_NewAdv").off and find(p, "MP_NewAdv").tip == "All five save slots are in use.")
    nev = #events("w159")
    press("MP_NewAdv")
    check("H12: ...and pressing it asks nothing", #events("w159") == nev)
    KCD2MP_W159Model({ sig = "h4", role = "host", saves = {}, newadv = { pl = 1, name = "permanent002" } })
    KCD2MP_W159Tick()
    check("H13: no world of one's own: New adventure is the selection", lastSel() == "MP_NewAdv")
    press("MP_Back")
    p = lastPage()
    check("H14: our Back: the root again", KCD2MP.w159.page == "root" and p[1].id == "MP_StartGame")
    check("H15: no swallowed Lua errors", #ERRS == 0, ERRS[1])
end

-- (G) the game's own pages --------------------------------------------------------------------------------------------------
do
    press("Settings")
    check("G1: Settings: one level down", KCD2MP.w159.depth == 1)
    press("GraphicSettings")
    check("G2: a settings page: two down", KCD2MP.w159.depth == 2)
    KCD2MP_W159Model({ sig = "g1", role = "host", saves = {}, newadv = { pl = 1, name = "permanent002" } })
    local n = count("ClearAll")
    KCD2MP_W159Tick()
    check("G3: a new model while the game's page is open: not drawn over it", count("ClearAll") == n)
    press("Back")
    KCD2MP_W159Tick()
    check("G4: Back to Settings: still not drawn", count("ClearAll") == n and KCD2MP.w159.depth == 1)
    press("Back")
    check("G5: Back to the root: not drawn in the listener (the game redraws its own root after it)", count("ClearAll") == n and KCD2MP.w159.dirty)
    KCD2MP_W159Tick()
    check("G6: ...but on the next tick", count("ClearAll") == n + 1 and lastPage()[1].id == "MP_StartGame")
    press("Credits")
    KCD2MP.w159.OnMenu(KCD2MP.w159, "Menu", 0, "OnCreditsHide", {})
    KCD2MP_W159Tick()
    check("G7: the credits closed: redrawn", count("ClearAll") == n + 2)
    press("MP_StartGame")
    press("Back")
    KCD2MP_W159Tick()
    check("G8: Esc on our page (the game's Back): our root on the next tick", KCD2MP.w159.page == "root" and lastPage()[1].id == "MP_StartGame")
end

-- (J) Join Game ---------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    KCD2MP_W159Model({ sig = "j1", role = "join", join = { state = "idle", bring = false,
        msg = "Your saves are from the regular game, not the Modding Tools." }, styles = { soldier = true, adviser = true, scout = true } })
    KCD2MP_W159Tick()
    local p = lastPage()
    check("J1: the joiner's root: Join Game enabled and selected, Start Game greyed", not find(p, "MP_JoinGame").off and find(p, "MP_StartGame").off and lastSel() == "MP_JoinGame")
    press("MP_JoinGame")
    p = lastPage()
    check("J2: no own save: Join with a new character first and selected", p[1].id == "MP_JoinFresh" and lastSel() == "MP_JoinFresh")
    check("J3: Bring greyed with the plain reason", find(p, "MP_JoinBring").off and find(p, "MP_JoinBring").tip:find("regular game", 1, true))
    press("MP_JoinFresh")
    check("J4a: a new character: the same playstyle page", KCD2MP.w159.page == "style" and lastPage()[1].id == "MP_Style_soldier")
    local nev = #events("w159")
    press("MP_Back")
    check("J4b: Back from the playstyles: the join page, nothing chosen", KCD2MP.w159.page == "join" and #events("w159") == nev)
    press("MP_JoinFresh")
    press("MP_Style_adviser")
    local e = events("w159")
    check("J4: the choice and its playstyle go to the launcher", e[#e] == "join fresh adviser", e[#e])
    p = lastPage()
    check("J5: Waiting for the host... with a Cancel", find(p, "MP_JoinWait") and find(p, "MP_JoinWait").off and find(p, "MP_JoinCancel") and lastSel() == "MP_JoinCancel")
    -- the launcher's next push still says idle (it has not read the click yet): the page stays waiting
    KCD2MP_W159Model({ sig = "j1", role = "join", join = { state = "idle", bring = false }, styles = { soldier = true, adviser = true, scout = true } })
    KCD2MP_W159Tick()
    check("J6: a stale model does not undo the click", KCD2MP.w159.joinState == "waiting")
    press("MP_JoinCancel")
    e = events("w159")
    check("J7: Cancel: told, the choice page again", e[#e] == "cancel" and lastPage()[1].id == "MP_JoinFresh")
    press("MP_JoinFresh"); press("MP_Style_scout")
    press("MP_Back")
    e = events("w159")
    check("J8: Back while waiting cancels too", e[#e] == "cancel" and KCD2MP.w159.page == "root")
    KCD2MP_W159Model({ sig = "j2", role = "join", join = { state = "idle", bring = true } })
    KCD2MP_W159Tick()
    press("MP_JoinGame")
    check("J9: an own save to bring: Bring is the default", lastSel() == "MP_JoinBring" and not find(lastPage(), "MP_JoinBring").off)
    KCD2MP_W159Model({ sig = "j3", role = "join", join = { state = "joining", bring = true,
        status = "Your host is watching the prologue (about 9 min left)." } })
    KCD2MP_W159Tick()
    p = lastPage()
    check("J10: joining: one greyed line, no Cancel", #p == 1 and p[1].id == "MP_JoinWait" and p[1].text:find("Joining", 1, true))
    check("J10b: ...with the join's own status as its line", p[1].tip:find("watching the prologue", 1, true) ~= nil, p[1].tip)
    check("J11: no swallowed Lua errors", #ERRS == 0, ERRS[1])
end

-- (D) disarm, recap ------------------------------------------------------------------------------------------------------------
do
    KCD2MP_W159Disarm("load")
    local n, nev = #UI, #events("w159")
    press("MP_StartGame"); press("MP_JoinFresh")
    check("D1: disarmed (a load runs): our entries do nothing", #UI == n and #events("w159") == nev)
    local ms = 0
    for _, c in ipairs(UI) do if c.fn == "SetInput" then ms = ms + 1 end end
    check("D3: nothing ever sends input to the menu", ms == 0)
end

-- (P) the prologue's cutscenes in the world ---------------------------------------------------------------------------------
do
    ERRS = {}
    player = { id = 1 }
    KCD2MP.w122.sharedWorld = true
    CMDS = {}; TIMERS = {}
    NOW = 1000
    local spec = "story_switch_to_trosecko|169.7;zachrana_fall_dream|149.9;m03_trosky_journey|172.7"
    TOASTS = {}
    local uiBefore = #UI
    check("P1: started", KCD2MP_W159RecapStart(spec) == true)
    local hint = UI[#UI]
    check("P1b: first the hint on the HUD (a video covers the UI)", #UI == uiBefore + 1 and hint.el == "hud" and hint.fn == "ShowInfoText"
        and tostring(hint.args[1]):find("Hold E", 1, true) ~= nil and #CMDS == 0)
    local function tick() local t = TIMERS[#TIMERS]; TIMERS[#TIMERS] = nil; if t then t.f() end end
    NOW = 1002; tick()
    check("P1c: no video during the hint", #CMDS == 0)
    NOW = 1004.05; tick()
    check("P2: the first video, by the game's cutscene player (its audio set up)", #CMDS == 1 and CMDS[1] == "wh_ui_PlayCutscene story_switch_to_trosecko", CMDS[#CMDS])
    check("P3: a join waits: 'prologue'", KCD2MP_JoinBusyReason() == "prologue", KCD2MP_JoinBusyReason())
    check("P4: about 9 min left", math.ceil(KCD2MP_W159RecapLeftS() / 60) == 9, KCD2MP_W159RecapLeftS())
    NOW = 1004 + 100; tick()
    check("P5: still the first one at 100 s", #CMDS == 1)
    NOW = 1004 + 170.1; tick()
    check("P6: the second one after the first's length (the first stopped, should it still run)", CMDS[#CMDS] == "wh_ui_PlayCutscene zachrana_fall_dream"
        and CMDS[#CMDS - 1] == "wh_ui_StopCutscene", CMDS[#CMDS])
    local played = 0
    for _, c in ipairs(CMDS) do if c:find("PlayMovie", 1, true) then played = played + 1 end end
    check("P6b: never the bare movie player (no audio setup)", played == 0)
    -- E pressed briefly: nothing; held for a second: skipped
    KCD2MP_W159OnAction("use", "press"); NOW = NOW + 0.5; tick(); KCD2MP_W159OnAction("use", "release"); NOW = NOW + 1; tick()
    check("P7: a short press does not skip", KCD2MP.w159.recap ~= nil)
    check("P8: other keys are not taken", KCD2MP_W159OnAction("attack", "press") == false)
    KCD2MP_W159OnAction("use", "press"); NOW = NOW + 1.05; tick()
    check("P9: E held for a second: the rest is skipped, the video stopped", KCD2MP.w159.recap == nil and CMDS[#CMDS] == "wh_ui_StopCutscene")
    local e = events("w159")
    check("P10: the launcher is told", e[#e] == "recap skipped", e[#e])
    check("P11: a join no longer waits for it", KCD2MP_JoinBusyReason() ~= "prologue")
    check("P12: E is the game's own again", KCD2MP_W159OnAction("use", "press") == false)
    -- played to the end
    NOW = 5000
    KCD2MP_W159RecapStart("zachrana_prespani_data1|16.3")
    NOW = 5004.05; tick(); NOW = 5004.05 + 16.7; tick()
    e = events("w159")
    check("P13: played to the end: finished", KCD2MP.w159.recap == nil and e[#e] == "recap finished", e[#e])
    -- a load kills the timer chain: the recap is over (no join waits forever)
    NOW = 6000
    KCD2MP_W159RecapStart("zachrana_prespani_data1|16.3")
    TIMERS = {}
    NOW = 6000 + 3
    check("P14: a dead timer chain: over", KCD2MP_JoinBusyReason() ~= "prologue" and KCD2MP.w159.recap == nil)
    check("P15: a bad spec is refused", KCD2MP_W159RecapStart("../x.bk2|5") == false and KCD2MP_W159RecapStart("intro new|5") == false)
    check("P16: no swallowed Lua errors", #ERRS == 0, ERRS[1])
    player = nil
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
