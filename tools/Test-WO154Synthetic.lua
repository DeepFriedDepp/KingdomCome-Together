-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-154 Phase 7b synthetic test: the mod menu, against the real kdcmp.lua under MoonSharp (the WO-77 driver;
-- harness after Test-WO114/WO151Synthetic.lua).
--   (a) the build line, the four new commands and mp_menu registered with %line, the defaults, w154_menu_cfg at load
--   (b) the keys through the real Player.OnAction hook: Insert opens and closes, the game's own handler still runs
--       for every key, one press delivered by both hooks toggles once, PgUp/PgDn/End do nothing while closed, the
--       game's Esc (open_menu) and inventory key close it, the frame chain draws the title / rows / hint on the
--       right of the screen
--   (c) never opens and closes when open: a cutscene, a dialogue (not a bark), a load, a game menu (the agent's
--       edge), dead, knocked out, no world, frames that stood still; a chain a load killed reopens fresh
--   (d) PgUp / PgDn wrap round and skip the info rows; select <n|item>
--   (e) End calls the SAME function its console command names, with the console's own argument, for every item;
--       logs MP-MENU <item>=<value> (by menu) and asks the agent to remember it; the two help actions; voice;
--       the menu key cycles and is stored by the agent
--   (f) a joiner sees the host's levers locked with the host's values; End on one changes nothing
--   (g) the clean screen hides every row, the ping line, the name badges and the mod's toasts; the menu still
--       opens and says so; name badges / ping line alone hide only themselves
--   (h) fast travel: held at 0 in a co-op session, said once, re-applied, given back; the host's switch; a
--       joiner follows the host; WO-114's joiner block shares the cvar both ways; the agent silent gives it back;
--       a refusal is told
--   (i) the agent's restore: the same setters, a joiner keeps the host's levers, bad values refused, the clean
--       screen's one notice; every key the agent remembers has an item
--   (j) mp_menu status / the console verbs
-- What this does NOT prove: how it looks on a real screen, that the four keys reach the hook in the real game
-- (the keys pak and the "interaction" actionmap), the agent's half (Wo154MenuTests.cs) -- see the findings.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; UICALLS = {}; CMDS = {}; CCMDS = {}; DRAWS = {}; LABELS = {}
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
System.GetEntitiesInSphere = function() return {} end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.DrawText = function(x, y, text, size) DRAWS[#DRAWS + 1] = { x = x, y = y, text = tostring(text), size = size } end
System.DrawLabel = function(pos, size, text) LABELS[#LABELS + 1] = tostring(text) end
System.GetViewport = function() return { x = 0, y = 0, width = 1920, height = 1080 } end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) UICALLS[#UICALLS + 1] = { panel = panel, fn = fn, text = tostring(text) } end
Calendar = { GetWorldTime = function() return 12345 end, SetWorldTime = function() end }

P = { dialog = false, dead = false, ko = false, stood = 0 }
player = { id = 1, GetName = function(self) return "Dude" end,
           GetWorldPos = function() return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return P.dead end,
                     IsUnconscious = function() return P.ko end, StandUp = function() P.stood = P.stood + 1 end },
           human = { IsInDialog = function() return P.dialog end, IsMounted = function() return false end },
           soul = { GetState = function() return 100 end } }

-- The engine's Player class table, so kdcmp.lua installs its OnAction hooks over "the game's own handler".
GAME_ONACTION = 0
Player = { Client = { OnAction = function(...) GAME_ONACTION = GAME_ONACTION + 1 end },
           OnAction = function(...) GAME_ONACTION = GAME_ONACTION + 1 end }

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
local function evts(name, from)
    local out = {}
    for i = (from or 0) + 1, #LOG do
        local a = LOG[i]:match("%[KCD2%-MP%-EVT%] v1 %d+ " .. name:gsub("_", "%%_") .. " (.*)$")
        if a then out[#out + 1] = a end
    end
    return out
end
local function drawn(pat)
    for _, d in ipairs(DRAWS) do if string.find(d.text, pat, 1, true) then return d end end
    return nil
end
local function toasts(from)
    local out = {}
    for i = (from or 0) + 1, #UICALLS do if UICALLS[i].fn == "ShowInfoText" then out[#out + 1] = UICALLS[i].text end end
    return out
end
local function hasToast(text, from)
    for _, t in ipairs(toasts(from)) do if t == text then return true end end
    return false
end
-- one game frame: the timers due run (the menu's own chain among them)
local function frame(dt)
    NOW = NOW + (dt or 0.016)
    local due = TIMERS
    TIMERS = {}
    for _, t in ipairs(due) do
        if NOW - t.at >= (t.ms / 1000) - 1e-6 then pcall(t.f) else TIMERS[#TIMERS + 1] = t end
    end
end
local function frames(n, dt) for _ = 1, n do frame(dt) end end
local function key(action, both)
    Player.OnAction(nil, action, "press", 1)
    if both then Player.Client.OnAction(nil, action, "press", 1) end
    Player.OnAction(nil, action, "release", 0)
end

local M = KCD2MP.w154menu
local function item(id) for i, it in ipairs(M.ITEMS) do if it.id == id then return it, i end end end
local function row(id)
    local it, i = item(id)
    return M.rowText(it, i == M.menu.sel)
end
local function session(role, partner, ft, crime, leash)
    KCD2MP_W154MenuSession(role, partner, ft, crime, leash, "connected", "0.45.0", false, false)
end
local function reset()
    if M.menu.open then M.close("test reset") end
    session("none", false)
    M.sessionAt = nil
    M.gameScreen = false
    KCD2MP.cutsceneActive = false
    KCD2MP.w136.hold = false
    P.dialog, P.dead, P.ko = false, false, false
    ENTS["DialogTwin_Dude"] = nil
    M.menu.sel = 1
    M.note = nil
    TIMERS = {}; DRAWS = {}
end

-- (a) defaults -- read before anything changes them
do
    local b = lastLog("WO154-BUILD") or ""
    check("a: WO154-BUILD logged once", logCount("WO154-BUILD") == 1, logCount("WO154-BUILD"))
    for _, kv in ipairs({ "menu=on", "menu_key=insert", "name_badges=on", "ping_line=on", "clean_screen=off", "fast_travel=off", "items=17" }) do
        check("a: the build line says " .. kv, b:find(kv, 1, true) ~= nil, b)
    end
    check("a: w154_menu_cfg fast_travel=off told to the agent at load", evts("w154_menu_cfg")[1] == "fast_travel=off", evts("w154_menu_cfg")[1])
    for cmd, fn in pairs({ mp_menu = "KCD2MP_W154Menu", mp_name_badges = "KCD2MP_W154SetNameBadges", mp_ping_line = "KCD2MP_W154SetPingLine",
                           mp_clean_screen = "KCD2MP_W154SetCleanScreen", mp_fast_travel = "KCD2MP_W154SetFastTravel" }) do
        local c = CCMDS[cmd]
        check("a: " .. cmd .. " registered as " .. fn .. "(%line)", c ~= nil and c.body == fn .. "(%line)", c and c.body)
        check("a: ..." .. fn .. " is defined", type(_G[fn]) == "function")
    end
    check("a: mp_fast_travel's help says HOST", CCMDS.mp_fast_travel and CCMDS.mp_fast_travel.help:find("HOST", 1, true) ~= nil)
    local missing = {}
    for _, w in ipairs({ "menu", "menu_locked", "badges", "clean", "fasttravel", "menukey", "odd", "stuck" }) do
        if not CCMDS["mark_" .. w] then missing[#missing + 1] = w end
    end
    check("a: the live check's marker words are commands (mark_menu ...)", #missing == 0, table.concat(missing, ","))
    check("a: defaults: badges on, ping on, clean off, fast travel off, key insert, closed",
        M.nameBadges == true and M.pingLine == true and M.clean == false and M.fastTravel == false and M.menuKey == "insert" and M.menu.open == false)
    check("a: no Lua errors at load", #ERRS == 0, ERRS[1])
end

TIMERS = {}

-- (b) the keys through the real hook
do
    reset()
    local g0 = GAME_ONACTION
    local mark = #LOG
    key("kcd2mp_menu_toggle")
    check("b: Insert opens it (no session needed)", M.menu.open == true and logCount("MP-MENU open by=key", mark) == 1, lastLog("MP-MENU"))
    check("b: the game's own handler ran for the press and the release (nothing blocked)", GAME_ONACTION - g0 == 2, GAME_ONACTION - g0)
    DRAWS = {}
    frame()
    local title = drawn("Kingdom Come: Together -- mod menu (Insert to close)")
    check("b: the frame draws the title", title ~= nil)
    check("b: ...the key hint", drawn("PgUp/PgDn choose  End change  Insert close") ~= nil)
    check("b: ...a group header", drawn("-- Display --") ~= nil and drawn("-- Gameplay --") ~= nil and drawn("-- Help --") ~= nil)
    check("b: ...the chosen row with its marker and value", drawn("> Partner's name badge: On") ~= nil)
    check("b: ...the other rows unmarked", drawn("  Ping and clock line: On") ~= nil and drawn("  Fast travel: Off") ~= nil)
    check("b: ...the chosen item's explanation", drawn("Shows your partner's name (and health) above their figure.") ~= nil)
    local minX = 1e9
    for _, d in ipairs(DRAWS) do if d.x < minX then minX = d.x end end
    check("b: everything on the right of the screen, never its centre (x >= 0.6 of 1920)", minX >= 1152, minX)
    check("b: drawn with the 4-argument DrawText only (a size, no colour)", title ~= nil and type(title.size) == "number")
    frames(5)
    check("b: the chain keeps drawing every frame", drawn("Kingdom Come: Together") ~= nil and #TIMERS == 1, #TIMERS)
    mark = #LOG
    key("kcd2mp_menu_toggle")
    check("b: Insert again closes it", M.menu.open == false and logCount("MP-MENU close (its key)", mark) == 1, lastLog("MP-MENU"))
    frame(); DRAWS = {}; frame()
    check("b: ...and nothing more is drawn", drawn("Kingdom Come") == nil and #TIMERS == 0, #TIMERS)
    -- one press delivered by both hooks (Player.OnAction and Player.Client.OnAction) toggles once
    NOW = NOW + 1
    mark = #LOG
    key("kcd2mp_menu_toggle", true)
    check("b: one press through both hooks opens it once", M.menu.open == true and logCount("MP-MENU open", mark) == 1 and logCount("MP-MENU close", mark) == 0)
    frame()
    NOW = NOW + 0.2
    key("kcd2mp_menu_toggle")
    check("b: a real second press 0.2 s later closes it", M.menu.open == false)
    -- PgUp / PgDn / End while closed: nothing
    mark = #LOG
    key("kcd2mp_menu_down"); key("kcd2mp_menu_up"); key("kcd2mp_menu_change")
    check("b: PgUp/PgDn/End while closed change nothing", logCount("MP-MENU select", mark) == 0 and logCount("(by menu)", mark) == 0
        and M.menu.sel == 1 and M.nameBadges == true)
    -- the game's own menu keys close it, and are never consumed
    NOW = NOW + 1
    key("kcd2mp_menu_toggle"); frame()
    g0 = GAME_ONACTION
    mark = #LOG
    Player.OnAction(nil, "open_menu", "press", 1)
    check("b: the game's Esc (open_menu) closes it", M.menu.open == false and logCount("MP-MENU close (the game's own menu (open_menu))", mark) == 1, lastLog("MP-MENU close"))
    check("b: ...and the game's own handler ran", GAME_ONACTION - g0 == 1)
    NOW = NOW + 1
    key("kcd2mp_menu_toggle"); frame()
    Player.OnAction(nil, "open_apse_inventory_keyboard", "press", 1)
    check("b: the inventory key closes it too", M.menu.open == false)
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (c) never opens / closes when open
do
    reset()
    local function tryOpen() NOW = NOW + 1; key("kcd2mp_menu_toggle"); return M.menu.open end
    local mark = #LOG
    KCD2MP.cutsceneActive = true
    check("c: a cutscene: refused", tryOpen() == false and logCount("MP-MENU refused open (a cutscene) by=key", mark) == 1, lastLog("MP-MENU"))
    KCD2MP.cutsceneActive = false
    P.dialog = true
    check("c: a bark (IsInDialog, no conversation twin) is not a dialogue: it opens", tryOpen() == true)
    reset()
    P.dialog = true; ENTS["DialogTwin_Dude"] = { id = 77, GetName = function() return "DialogTwin_Dude" end }
    check("c: a dialogue: refused", tryOpen() == false and lastLog("MP-MENU refused") ~= nil and lastLog("MP-MENU refused"):find("a dialogue", 1, true) ~= nil, lastLog("MP-MENU refused"))
    reset()
    KCD2MP_W136Hold(true, 240, "test-load")
    check("c: a load (WO-136's hold): refused", tryOpen() == false and lastLog("MP-MENU refused"):find("a load", 1, true) ~= nil, lastLog("MP-MENU refused"))
    KCD2MP_W136Hold(false, 0, "test-load")
    check("c: ...after the load it opens", tryOpen() == true)
    frame()
    mark = #LOG
    KCD2MP_W154MenuGameScreen(true, "agent")
    check("c: the agent's game-menu edge closes an open menu", M.menu.open == false and logCount("MP-MENU close (the game's own screen (agent))", mark) == 1, lastLog("MP-MENU close"))
    check("c: ...and it does not open while the game's menu is up", tryOpen() == false and lastLog("MP-MENU refused"):find("a game menu", 1, true) ~= nil)
    KCD2MP_W154MenuGameScreen(false, "agent")
    check("c: ...and opens once it is gone", tryOpen() == true)
    reset()
    P.dead = true
    check("c: dead: refused", tryOpen() == false and lastLog("MP-MENU refused"):find("dead", 1, true) ~= nil)
    P.dead = false; P.ko = true
    check("c: knocked out: refused", tryOpen() == false and lastLog("MP-MENU refused"):find("knocked out", 1, true) ~= nil)
    P.ko = false
    local pl = player; player = nil
    check("c: no world (the main menu): refused", tryOpen() == false and lastLog("MP-MENU refused"):find("no world", 1, true) ~= nil)
    player = pl
    -- open, then each condition arrives: the frame chain closes it within a quarter second
    for _, case in ipairs({
        { "a cutscene", function() KCD2MP.cutsceneActive = true end },
        { "a dialogue", function() P.dialog = true; ENTS["DialogTwin_Dude"] = { id = 77 } end },
        { "dead", function() P.dead = true end },
    }) do
        reset()
        tryOpen(); frame()
        case[2]()
        frames(20, 0.016)
        check("c: open, then " .. case[1] .. ": the menu closes by itself", M.menu.open == false and lastLog("MP-MENU close"):find(case[1], 1, true) ~= nil, lastLog("MP-MENU close"))
    end
    -- frames that stood still (a game menu held the timers): closed when they resume
    reset()
    tryOpen(); frame()
    NOW = NOW + 1.5
    frame()
    check("c: frames that stood still 1.5 s: closed when they come back", M.menu.open == false and lastLog("MP-MENU close"):find("stood still", 1, true) ~= nil, lastLog("MP-MENU close"))
    -- a load kills the chain and leaves the flag: the next press opens it fresh
    reset()
    tryOpen(); frame()
    TIMERS = {}             -- the load killed every Script.SetTimer chain
    NOW = NOW + 5
    mark = #LOG
    key("kcd2mp_menu_toggle")
    check("c: a chain a load killed: the next press opens it fresh (not a close)", M.menu.open == true and logCount("its frames had stopped", mark) == 1
        and logCount("MP-MENU open by=key", mark) == 1, lastLog("MP-MENU"))
    frame()
    check("c: ...and it draws again", #TIMERS == 1)
    check("c: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (d) navigation
do
    reset()
    NOW = NOW + 1
    key("kcd2mp_menu_toggle"); frame()
    local n, sel = #M.ITEMS, 0
    for i = 1, n do if M.selectable(i) then sel = sel + 1 end end
    check("d: 17 items, 15 that can be chosen (version and connection are information; WO-164 added the partner pin)", n == 17 and sel == 15, n .. "/" .. sel)
    local mark = #LOG
    NOW = NOW + 0.1
    key("kcd2mp_menu_up")
    check("d: PgUp on the first wraps to the last that can be chosen (not an information row)", M.menu.sel == 15 and M.ITEMS[15].id == "report"
        and logCount("MP-MENU select 15 report", mark) == 1, M.menu.sel)
    NOW = NOW + 0.1
    key("kcd2mp_menu_down")
    check("d: PgDn on the last wraps to the first", M.menu.sel == 1, M.menu.sel)
    NOW = NOW + 0.1
    key("kcd2mp_menu_down"); NOW = NOW + 0.1; key("kcd2mp_menu_down")
    check("d: PgDn PgDn -> the third", M.menu.sel == 3 and M.ITEMS[3].id == "clean_screen")
    DRAWS = {}; frame()
    check("d: the marker follows the choice, with its explanation", drawn("> Clean screen: Off") ~= nil and drawn("  Partner's name badge: On") ~= nil
        and drawn("Hides everything the mod draws") ~= nil)
    check("d: mp_menu select 16 (the version) is refused", KCD2MP_W154Menu("select 16") == false and M.menu.sel == 3)
    check("d: mp_menu select fast_travel", KCD2MP_W154Menu("select fast_travel") == true and M.ITEMS[M.menu.sel].id == "fast_travel")
    check("d: mp_menu select 2", KCD2MP_W154Menu("select 2") == true and M.menu.sel == 2)
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (e) End = the console command's own function, with the console's own argument
do
    reset()
    NOW = NOW + 1
    key("kcd2mp_menu_toggle"); frame()
    -- every item's function is the one its console command's template names
    local bad = {}
    for _, it in ipairs(M.ITEMS) do
        if it.cmd then
            local c = CCMDS[it.cmd]
            local fn = c and c.body:match("^([%w_]+)%(")
            if fn ~= it.fn then bad[#bad + 1] = it.id .. ":" .. tostring(c and c.body) end
        end
    end
    check("e: every item calls the function its console command names", #bad == 0, table.concat(bad, " "))
    check("e: 'Something's wrong here' is mark_odd exactly", CCMDS.mark_odd ~= nil and CCMDS.mark_odd.body == "KCD2MP_Mark('odd')" and item("report").arg == "odd")
    -- each switch: End once flips it through that function, End again flips it back
    for _, it in ipairs(M.ITEMS) do
        if it.kind == "onoff" or it.kind == "crime" then
            local real = _G[it.fn]
            local calls = {}
            _G[it.fn] = function(a) calls[#calls + 1] = a; return real(a) end
            local _, idx = item(it.id)
            KCD2MP_W154Menu("select " .. idx)
            local before = it.get()
            local mark = #LOG
            frames(6)
            key("kcd2mp_menu_change")
            local after = it.get()
            local want = (it.kind == "crime") and (before and "individual" or "joint") or (before and "off" or "on")
            check("e: " .. it.id .. ": End called " .. it.fn .. "(\"" .. want .. "\") once", #calls == 1 and calls[1] == want, table.concat(calls, ","))
            check("e: ..." .. it.id .. " flipped", after == (not before), tostring(before) .. "->" .. tostring(after))
            check("e: ...MP-MENU " .. it.id .. "=" .. M.word(it, after) .. " (by menu)", logCount("MP-MENU " .. it.id .. "=" .. M.word(it, after) .. " (by menu)", mark) == 1, lastLog("MP-MENU"))
            check("e: ...the agent asked to remember " .. it.save .. " " .. M.word(it, after), evts("w154_menu_set", mark)[1] == it.save .. " " .. M.word(it, after), evts("w154_menu_set", mark)[1])
            frames(6)
            key("kcd2mp_menu_change")
            check("e: ...End again: back", it.get() == before and #calls == 2)
            -- the same argument through the console command's function gives the same state
            real(want)
            check("e: ...the console command's own call does the same", it.get() == after)
            real((it.kind == "crime") and (before and "joint" or "individual") or (before and "on" or "off"))
            _G[it.fn] = real
        end
    end
    -- the help actions
    local realU, uc = _G.KCD2MP_W151Unstuck, 0
    _G.KCD2MP_W151Unstuck = function(a) uc = uc + 1; return realU(a) end
    local mark = #LOG
    KCD2MP_W154Menu("select unstuck"); frames(6); key("kcd2mp_menu_change")
    check("e: I'm stuck = mp_unstuck's function (soft)", uc == 1 and logCount("WO151-UNSTUCK soft", mark) == 1 and logCount("MP-MENU unstuck (by menu)", mark) == 1, lastLog("WO151-UNSTUCK"))
    _G.KCD2MP_W151Unstuck = realU
    mark = #LOG
    local ui0 = #UICALLS
    KCD2MP_W154Menu("select report"); frames(6); key("kcd2mp_menu_change")
    check("e: Something's wrong here writes MP-MARK odd", logCount("MP-MARK odd", mark) == 1 and evts("mp_mark", mark)[1] == "odd", lastLog("MP-MARK"))
    check("e: ...and tells the player to send the logs with Report a bug before restarting",
        hasToast("Marked. Send your logs with Report a bug (in the launcher) before you restart the game.", ui0))
    DRAWS = {}; frame()
    check("e: ...the menu shows it too", drawn("Marked. Send your logs with Report a bug") ~= nil)
    -- voice
    mark = #LOG
    KCD2MP_W154Menu("select voice"); frames(6); key("kcd2mp_menu_change")
    check("e: voice: End asks the agent (w154_voice on)", evts("w154_voice", mark)[1] == "on" and row("voice"):find("On (asking...)", 1, true) ~= nil, row("voice"))
    KCD2MP_W154VoiceState(true)
    check("e: ...its answer shows", row("voice") == "> Voice chat: On", row("voice"))
    frames(6); key("kcd2mp_menu_change")
    check("e: ...End again asks for off", evts("w154_voice", mark)[2] == "off")
    KCD2MP_W154VoiceState(false)
    -- the menu key
    mark = #LOG
    KCD2MP_W154Menu("select menu_key")
    check("e: the menu key shows Insert", row("menu_key") == "> Menu key: Insert", row("menu_key"))
    frames(6); key("kcd2mp_menu_change")
    check("e: End cycles it to Numpad + and asks the agent to store it", evts("w154_menu_set", mark)[1] == "MenuKey np_add"
        and row("menu_key") == "> Menu key: Numpad + (saving...)", row("menu_key"))
    KCD2MP_W154MenuKeys("insert", "np_add", "stored")
    check("e: ...stored: it says it works from the next game start", row("menu_key") == "> Menu key: Numpad + (from the next game start)", row("menu_key"))
    DRAWS = {}; frame()
    check("e: ...while Insert still closes it in this game run", drawn("(Insert to close)") ~= nil)
    frames(6); key("kcd2mp_menu_change")
    frames(6); key("kcd2mp_menu_change")
    check("e: ...Numpad - then back to Insert", evts("w154_menu_set", mark)[2] == "MenuKey np_subtract" and evts("w154_menu_set", mark)[3] == "MenuKey insert"
        and row("menu_key") == "> Menu key: Insert", row("menu_key"))
    KCD2MP_W154MenuKeys("insert", "np_subtract", "not-saved")
    check("e: ...an agent that could not save it says so", row("menu_key"):find("could not be saved", 1, true) ~= nil, row("menu_key"))
    KCD2MP_W154MenuKeys("np_add", "np_add", "")
    DRAWS = {}; frame()
    check("e: a game run with Numpad + bound names it in the title and the hint", drawn("(Numpad + to close)") ~= nil and drawn("End change  Numpad + close") ~= nil)
    KCD2MP_W154MenuKeys("insert", "insert", "")
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (f) a joiner: the host's levers locked, the host's values
do
    reset()
    KCD2MP_FriendlyFireSession(false, "host")
    session("joiner", true, false, "individual", true)
    NOW = NOW + 1
    key("kcd2mp_menu_toggle"); frame()
    check("f: friendly fire shows the host's value, locked", row("friendly_fire") == "  Friendly fire: Off  [set by the host]", row("friendly_fire"))
    check("f: crime shows the host's (individual), locked", row("crime_mode") == "  Crime: Individual  [set by the host]", row("crime_mode"))
    check("f: fast travel shows the host's, locked", row("fast_travel") == "  Fast travel: Off  [set by the host]", row("fast_travel"))
    check("f: the leash shows the host's, locked", row("leash") == "  Keep players together: On  [set by the host]", row("leash"))
    check("f: this player's own items are not locked", row("name_badges") == "> Partner's name badge: On" and row("sleep_vote") == "  Sleep and wait together: On", row("name_badges"))
    local realFF, ffCalls = _G.KCD2MP_Wo121SetFriendlyFire, 0
    _G.KCD2MP_Wo121SetFriendlyFire = function(a) ffCalls = ffCalls + 1; return realFF(a) end
    local mark = #LOG
    KCD2MP_W154Menu("select friendly_fire"); NOW = NOW + 0.1; key("kcd2mp_menu_change")
    check("f: End on a locked lever calls nothing and remembers nothing", ffCalls == 0 and #evts("w154_menu_set", mark) == 0
        and logCount("MP-MENU friendly_fire locked: set by the host (off)", mark) == 1, lastLog("MP-MENU"))
    DRAWS = {}; frame()
    check("f: ...the menu says why", drawn("Set by the host: only the host can change this.") ~= nil)
    _G.KCD2MP_Wo121SetFriendlyFire = realFF
    session("joiner", true, nil, nil, nil)
    check("f: a host value not heard yet shows as waiting", row("crime_mode") == "  Crime: Waiting for the host  [set by the host]", row("crime_mode"))
    mark = #LOG
    KCD2MP_W154Menu("status")
    local st = lastLog("MP-MENU status") or ""
    check("f: status: the levers locked, the own items own", st:find("friendly_fire=off(host,locked)", 1, true) ~= nil and st:find("name_badges=on(own)", 1, true) ~= nil
        and st:find("role=joiner", 1, true) ~= nil, st)
    session("host", true)
    check("f: the host: its own values, unlocked", row("crime_mode") == "  Crime: Shared" and row("friendly_fire") == "> Friendly fire: On", row("friendly_fire"))
    KCD2MP_FriendlyFireSession(true, "test")
    check("f: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (g) the clean screen
do
    reset()
    session("host", true)
    KCD2MP.pingText = "Ping: 31 ms"
    KCD2MP.labelCache = { ["0"] = { x = 1, y = 2, z = 3, size = 1.0, name = "Partner  100 HP" } }
    KCD2MP.labelRunning = true
    local function labelFrame() DRAWS = {}; LABELS = {}; KCD2MP_LabelTick(); TIMERS = {} end
    KCD2MP_ShowInteractionMsg("a mod line")
    labelFrame()
    check("g: before: the ping line, the name badge and a mod row are drawn", drawn("Ping: 31 ms") ~= nil and LABELS[1] == "Partner  100 HP" and drawn("a mod line") ~= nil)
    local mark = #LOG
    check("g: mp_clean_screen on", KCD2MP_W154SetCleanScreen("on") == true and logCount("WO154-TOGGLE mp_clean_screen clean_screen=on", mark) == 1)
    labelFrame()
    check("g: the ping line is hidden", drawn("Ping:") == nil)
    check("g: the name badge is hidden", #LABELS == 0)
    check("g: the mod's rows are hidden", drawn("a mod line") == nil and #DRAWS == 0, #DRAWS)
    check("g: ...and the row logs as gone", logCount('MP-SCREEN row=msg text=""', mark) == 1)
    local ui0 = #UICALLS
    KCD2MP_ShowNativeToast("a mod toast")
    check("g: the mod's toast is not shown", #toasts(ui0) == 0)
    check("g: ...but still logged", lastLog("MP-TOAST kind=native") ~= nil and lastLog("MP-TOAST kind=native"):find("a mod toast", 1, true) ~= nil)
    NOW = NOW + 1
    key("kcd2mp_menu_toggle"); DRAWS = {}; frame()
    local t, c = drawn("Kingdom Come: Together"), drawn("Clean screen is ON: the mod shows nothing else.")
    check("g: the menu still opens, and its first row says the clean screen is on", t ~= nil and c ~= nil and c.y > t.y and drawn("> Partner's name badge") ~= nil and c.y < drawn("> Partner's name badge").y)
    check("g: mp_clean_screen off", KCD2MP_W154SetCleanScreen("off") == true)
    KCD2MP_ShowInteractionMsg("a mod line")
    labelFrame()
    check("g: ...all of it is back", drawn("Ping: 31 ms") ~= nil and LABELS[1] == "Partner  100 HP" and drawn("a mod line") ~= nil)
    KCD2MP_W154SetNameBadges("off")
    labelFrame()
    check("g: mp_name_badges off hides only the badge", #LABELS == 0 and drawn("Ping: 31 ms") ~= nil and drawn("a mod line") ~= nil)
    KCD2MP_W154SetNameBadges("on"); KCD2MP_W154SetPingLine("off")
    labelFrame()
    check("g: mp_ping_line off hides only the ping line", drawn("Ping:") == nil and LABELS[1] == "Partner  100 HP" and drawn("a mod line") ~= nil)
    KCD2MP_W154SetPingLine("on")
    check("g: bad input refused", KCD2MP_W154SetCleanScreen("maybe") == false and M.clean == false)
    KCD2MP.labelRunning = false; KCD2MP.labelCache = {}; KCD2MP.pingText = nil
    check("g: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (h) fast travel
do
    reset()
    local w114 = KCD2MP.w114
    CVARS.wh_pl_FastTravelEnabled = 2
    local mark, ui0 = #LOG, #UICALLS
    session("host", false)
    check("h: a host alone (no partner): nothing switched", CVARS.wh_pl_FastTravelEnabled == 2 and M.ftHeld ~= true)
    session("host", true)
    check("h: a partner here: wh_pl_FastTravelEnabled 0", CVARS.wh_pl_FastTravelEnabled == 0 and M.ftHeld == true)
    check("h: ...logged with the value from before", logCount("WO154-FASTTRAVEL off (session): wh_pl_FastTravelEnabled 2 -> 0 (was 2 before the session)", mark) == 1, lastLog("WO154-FASTTRAVEL"))
    check("h: ...the host is told once, plainly", hasToast(M.TEXT_FT_HOST, ui0) and M.TEXT_FT_HOST == "Fast travel is turned off for co-op. You can turn it on in the mod menu (Insert).")
    session("host", true); session("host", true)
    check("h: ...once (every push re-applies quietly)", #toasts(ui0) == 1 and logCount("WO154-FASTTRAVEL off", mark) == 1, #toasts(ui0))
    CVARS.wh_pl_FastTravelEnabled = 1   -- a load or a restart put it back
    session("host", true)
    check("h: re-applied after a load reset it", CVARS.wh_pl_FastTravelEnabled == 0 and M.ftPrev == 2)
    mark, ui0 = #LOG, #UICALLS
    check("h: mp_fast_travel on (the host)", KCD2MP_W154SetFastTravel("on") == true and M.fastTravel == true)
    check("h: ...given back at once", CVARS.wh_pl_FastTravelEnabled == 2 and M.ftHeld == false and logCount("WO154-FASTTRAVEL given back", mark) == 1)
    check("h: ...both are told, the agent too", hasToast("Fast travel is now ON in this co-op session.", ui0) and evts("w154_menu_cfg", mark)[1] == "fast_travel=on")
    KCD2MP_W154SetFastTravel("off")
    check("h: mp_fast_travel off: held again", CVARS.wh_pl_FastTravelEnabled == 0 and hasToast("Fast travel is now OFF in this co-op session.", ui0))
    mark = #LOG
    session("none", false)
    check("h: the session ends: the value from before given back", CVARS.wh_pl_FastTravelEnabled == 2 and M.ftHeld == false, CVARS.wh_pl_FastTravelEnabled)
    -- a joiner follows the host's value
    reset()
    CVARS.wh_pl_FastTravelEnabled = 1
    ui0 = #UICALLS
    session("joiner", true, nil)
    check("h: a joiner, the host's value not heard yet: held (fail-closed), nothing said yet", CVARS.wh_pl_FastTravelEnabled == 0 and #toasts(ui0) == 0)
    session("joiner", true, false)
    check("h: ...the host's off arrives: told once, plainly", hasToast(M.TEXT_FT_JOINER, ui0) and #toasts(ui0) == 1
        and M.TEXT_FT_JOINER == "Fast travel is turned off for co-op. The host can turn it on in the mod menu (Insert).")
    session("joiner", true, false)
    check("h: ...once", #toasts(ui0) == 1)
    session("joiner", true, true)
    check("h: the host allows it: given back", CVARS.wh_pl_FastTravelEnabled == 1 and M.ftHeld == false)
    ui0 = #UICALLS
    session("joiner", true, false)
    check("h: the host switches it off: held, and the joiner is told it was the host", CVARS.wh_pl_FastTravelEnabled == 0
        and hasToast("Fast travel is now OFF in this co-op session (set by the host).", ui0))
    mark = #LOG
    KCD2MP_W154SetFastTravel("on")
    check("h: a joiner's own mp_fast_travel changes nothing in the session", CVARS.wh_pl_FastTravelEnabled == 0 and logCount("the HOST's decides", mark) == 1)
    KCD2MP_W154SetFastTravel("off")
    session("none", false)
    -- WO-114's joiner block shares the cvar: whichever lets go last gives the value back
    reset()
    CVARS.wh_pl_FastTravelEnabled = 2
    KCD2MP_Wo114FastTravelBlock(true, "in the host's world")
    session("joiner", true, false)
    check("h: WO-114 first, then the session: 0, both remember 2", CVARS.wh_pl_FastTravelEnabled == 0 and w114.ftPrev == 2 and M.ftPrev == 2)
    mark = #LOG
    KCD2MP_Wo114FastTravelBlock(false, "left the host's world")
    check("h: ...WO-114 lets go first: still 0 (the session's is off)", CVARS.wh_pl_FastTravelEnabled == 0 and logCount("WO114-FASTTRAVEL released", mark) == 1, lastLog("WO114-FASTTRAVEL"))
    session("joiner", true, true)
    check("h: ...then the session lets go: 2 again", CVARS.wh_pl_FastTravelEnabled == 2)
    session("joiner", true, false)
    KCD2MP_Wo114FastTravelBlock(true, "in the host's world")
    check("h: the session first, then WO-114: WO-114 remembers 2, not 0", CVARS.wh_pl_FastTravelEnabled == 0 and w114.ftPrev == 2)
    mark = #LOG
    session("joiner", true, true)
    check("h: ...the session lets go first: WO-114 keeps it 0 (only the host travels in his world)", CVARS.wh_pl_FastTravelEnabled == 0
        and logCount("WO-114's joiner block keeps it off", mark) == 1, lastLog("WO154-FASTTRAVEL"))
    KCD2MP_Wo114FastTravelBlock(false, "left the host's world")
    check("h: ...then WO-114 lets go: 2 again", CVARS.wh_pl_FastTravelEnabled == 2)
    -- the agent went silent: the backstop gives it back
    reset()
    CVARS.wh_pl_FastTravelEnabled = 1
    session("host", true)
    NOW = NOW + 11
    KCD2MP_W154MenuBackstop()
    check("h: the agent silent 11 s: given back by the label loop's backstop", CVARS.wh_pl_FastTravelEnabled == 1 and M.ftHeld == false)
    -- a refusal is told
    session("host", true)
    ui0 = #UICALLS
    check("h: the engine refused a fast travel: told", KCD2MP_W154FastTravelTried("engine-refused") == true and hasToast(M.TEXT_FT_HOST, ui0))
    NOW = NOW + 1
    check("h: ...not again within 5 s", KCD2MP_W154FastTravelTried("engine-refused") == true and #toasts(ui0) == 1)
    NOW = NOW + 1.1; KCD2MP_W154MenuBackstop()
    check("h: ...the plain line once the map closed", KCD2MP.interactionMsg ~= nil and KCD2MP.interactionMsg.text == M.TEXT_FT_HOST)
    session("none", false)
    check("h: not held: a refusal is not ours to tell", KCD2MP_W154FastTravelTried("engine-refused") == false)
    CVARS.wh_pl_FastTravelEnabled = nil
    session("host", true)
    check("h: an unreadable cvar switches nothing and says so", M.ftHeld == false and logCount("unreadable -- fast travel NOT switched off") == 1)
    session("none", false)
    CVARS.wh_pl_FastTravelEnabled = 1
    check("h: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (i) the agent's restore
do
    reset()
    local real, calls = _G.KCD2MP_W154SetNameBadges, {}
    _G.KCD2MP_W154SetNameBadges = function(a) calls[#calls + 1] = a; return real(a) end
    local mark = #LOG
    check("i: a saved NameBadges=off goes through mp_name_badges' own function", KCD2MP_W154MenuRestore("NameBadges", "off") == true
        and calls[1] == "off" and M.nameBadges == false and logCount("MP-MENU restore name_badges=off (saved)", mark) == 1, lastLog("MP-MENU restore"))
    check("i: ...and is not sent back to be saved again", #evts("w154_menu_set", mark) == 0)
    _G.KCD2MP_W154SetNameBadges = real
    KCD2MP_W154MenuRestore("NameBadges", "on")
    session("joiner", true, false, "joint", true)
    local realFF, ff = _G.KCD2MP_Wo121SetFriendlyFire, 0
    _G.KCD2MP_Wo121SetFriendlyFire = function(a) ff = ff + 1; return realFF(a) end
    mark = #LOG
    check("i: a joiner keeps the host's levers (a saved FriendlyFire is not applied)", KCD2MP_W154MenuRestore("FriendlyFire", "off") == false and ff == 0
        and logCount("MP-MENU restore friendly_fire skipped", mark) == 1)
    session("host", true)
    check("i: the host applies it", KCD2MP_W154MenuRestore("FriendlyFire", "off") == true and ff == 1 and KCD2MP.w121.friendlyFire == false)
    KCD2MP_W154MenuRestore("FriendlyFire", "on")
    _G.KCD2MP_Wo121SetFriendlyFire = realFF
    check("i: CrimeMode individual through mp_crime_mode's function", KCD2MP_W154MenuRestore("CrimeMode", "individual") == true and KCD2MP.w151.crimeJoint == false)
    KCD2MP_W154MenuRestore("CrimeMode", "joint")
    check("i: FastTravel on through mp_fast_travel's", KCD2MP_W154MenuRestore("FastTravel", "on") == true and M.fastTravel == true)
    KCD2MP_W154MenuRestore("FastTravel", "off")
    check("i: bad values refused", KCD2MP_W154MenuRestore("NameBadges", "maybe") == false and KCD2MP_W154MenuRestore("CrimeMode", "on") == false
        and KCD2MP_W154MenuRestore("Nope", "on") == false and M.nameBadges == true and KCD2MP.w151.crimeJoint == true)
    -- every key the agent remembers (Wo154MenuRules.Settings) has its item
    local missing = {}
    for _, k in ipairs({ "NameBadges", "PingLine", "CleanScreen", "FriendlyFire", "CrimeMode", "FastTravel", "Leash", "SleepVote", "Whistle", "PartnerHerbs" }) do
        local found = false
        for _, it in ipairs(M.ITEMS) do if it.save == k then found = true end end
        if not found then missing[#missing + 1] = k end
    end
    check("i: every key the agent remembers has a menu item", #missing == 0, table.concat(missing, ","))
    local ui0 = #UICALLS
    KCD2MP_W154MenuRestore("CleanScreen", "on")
    check("i: a restored clean screen says once why nothing shows (through the clean screen)", M.clean == true
        and toasts(ui0)[1] ~= nil and toasts(ui0)[1]:find("Clean screen is on", 1, true) ~= nil, toasts(ui0)[1])
    KCD2MP_W154MenuRestore("CleanScreen", "off"); KCD2MP_W154MenuRestore("CleanScreen", "on")
    check("i: ...only once", #toasts(ui0) == 1, #toasts(ui0))
    KCD2MP_W154MenuRestore("CleanScreen", "off")
    session("none", false)
    check("i: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (j) the console verbs and the status line
do
    reset()
    local mark = #LOG
    check("j: up / change while closed are refused", KCD2MP_W154Menu("up") == false and KCD2MP_W154Menu("change") == false
        and logCount("ignored: the menu is closed", mark) == 2)
    check("j: mp_menu open", KCD2MP_W154Menu("open") == true and M.menu.open == true and logCount("MP-MENU open by=console", mark) == 1)
    frame()
    check("j: mp_menu down", KCD2MP_W154Menu("down") == true and M.menu.sel == 2)
    check("j: mp_menu change (the ping line off)", KCD2MP_W154Menu("change") == true and M.pingLine == false and logCount("MP-MENU ping_line=off (by menu)", mark) == 1)
    KCD2MP_W154Menu("change")
    check("j: bare mp_menu = status", KCD2MP_W154Menu() == true and lastLog("MP-MENU status open=yes sel=2:ping_line") ~= nil, lastLog("MP-MENU status"))
    local st = lastLog("MP-MENU status") or ""
    local n = 0
    for _ in st:gmatch("%d+:[%w_]+=") do n = n + 1 end
    check("j: the status line has every item with its whose", n == 17 and st:find("1:name_badges=on(own)", 1, true) ~= nil
        and st:find("fast_travel=off(host)", 1, true) ~= nil and st:find("14:unstuck=action(help)", 1, true) ~= nil
        and st:find("4:partner_marker=on(own)", 1, true) ~= nil
        and st:find('16:version="0.45.0"(info)', 1, true) ~= nil and st:find("17:connection=", 1, true) ~= nil, st)
    check("j: a bad verb shows the usage", KCD2MP_W154Menu("dance") == false and lastLog("mp_menu: expected open|close|up|down|change|status|select") ~= nil)
    check("j: mp_menu close", KCD2MP_W154Menu("close") == true and M.menu.open == false and logCount("MP-MENU close (by console)", mark) == 1)
    check("j: no Lua errors", #ERRS == 0, ERRS[1])
end

-- Summary, in the shared driver's contract (Test-NpcSmoothSynthetic.ps1 reads the global OUT).
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
