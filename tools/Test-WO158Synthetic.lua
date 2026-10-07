-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-158 synthetic test (S1: the name badge on the neck bone), against the real kdcmp.lua under MoonSharp.
--
--   (B) the badge anchor chain: neck -> head -> the stream's own height; each bone read is accepted only as a plain
--       finite point, not the origin, within 3 m across and -1..+3 m up of the entity's world point
--   (L) the 8 ms label loop draws at the anchor, says which level it hangs on once, and obeys mp_name_badges /
--       mp_clean_screen exactly as before
--
-- (S2, Mutt exempt from the dog pause, is scenario (n) of Test-WO131Synthetic.lua.)
-- Driven by Test-WO158Synthetic.ps1 through the WO-77 MoonSharp driver. Live check: not run (no game was launched).

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return {} end
System.GetTerrainElevation = function(p) return 0 end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
AI.EnableUpdateLookTarget = function(id, on) LOOKT = { id = id, on = on } end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
Game = mkstub()
Calendar = mkstub()
WORLD = 1000000
Calendar.GetWorldTime = function() return WORLD end
LABELS = {}
XGenAIModule = mkstub()
XGenAIModule.IsPointInAreaWithLabel = function(p, l) return LABELS[l] == true end

STATE = { health = 50, exhaust = 40, hunger = 60 }
player = { id = 1, soul = {
    GetState = function(self, n) return STATE[n] end,
    SetState = function(self, n, v) STATE[n] = v end,
} }
player.GetWorldPos = function() return { x = 0, y = 0, z = 0 } end

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
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
local function lastLog(needle, fromLog)
    for i = #LOG, (fromLog or 0) + 1, -1 do if LOG[i]:find(needle, 1, true) then return LOG[i] end end
    return nil
end
local function near(a, b) return math.abs(a - b) < 1e-4 end

-- an avatar entity at (1000, 2000, 100) whose bones are whatever `bones` says (world space)
local function mkAvatar(bones)
    local e = { GetWorldPos = function() return { x = 1000, y = 2000, z = 100 } end }
    e.GetBonePos = function(self, name)
        local b = bones[name]
        if b == "throw" then error("no such bone") end
        return b
    end
    return e
end

-- (B) the anchor chain -------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    local x, y, z, lvl = KCD2MP_W158BadgeAnchor(mkAvatar({ Neck = { x = 1000.1, y = 2000.2, z = 101.5 }, Head = { x = 1000.1, y = 2000.2, z = 101.7 } }), 1, 2, 3)
    check("B1: the neck, 0.45 m up", lvl == "neck" and near(x, 1000.1) and near(y, 2000.2) and near(z, 101.95), string.format("%s %.3f %.3f %.3f", lvl, x, y, z))
    x, y, z, lvl = KCD2MP_W158BadgeAnchor(mkAvatar({ Neck = { x = 0, y = 0, z = 0 }, Head = { x = 1000, y = 2000, z = 101.7 } }), 1, 2, 3)
    check("B2: a neck stuck at the origin: the head", lvl == "head" and near(z, 102.15), lvl)
    x, y, z, lvl = KCD2MP_W158BadgeAnchor(mkAvatar({ Neck = "throw", Head = { x = 1000, y = 2000, z = 101.7 } }), 1, 2, 3)
    check("B3: a bone call that errors: the head", lvl == "head")
    x, y, z, lvl = KCD2MP_W158BadgeAnchor(mkAvatar({ Neck = { x = 0.1, y = 0.2, z = 1.5 }, Head = { x = 0.1, y = 0.2, z = 1.7 } }), 1, 2, 3)
    check("B4: reads in model space (nowhere near the entity): the stream's own point", lvl == "stream" and x == 1 and y == 2 and z == 3, lvl)
    x, y, z, lvl = KCD2MP_W158BadgeAnchor(mkAvatar({ Neck = { x = 1000, y = 2000, z = 0 / 0 }, Head = { x = 1000, y = 2000, z = 104 } }), 1, 2, 3)
    check("B5: a NaN neck, a head 4 m up: both refused", lvl == "stream")
    x, y, z, lvl = KCD2MP_W158BadgeAnchor(mkAvatar({ Neck = { x = 1005, y = 2000, z = 101.5 } }), 1, 2, 3)
    check("B6: a bone 5 m across from the figure (a stale cache): refused", lvl == "stream")
    x, y, z, lvl = KCD2MP_W158BadgeAnchor(mkAvatar({ Neck = { x = 1000.4, y = 2000, z = 100.2 } }), 1, 2, 3)
    check("B7: a lying figure: the neck is near the ground, the badge hangs 0.45 m over it", lvl == "neck" and near(z, 100.65), string.format("%.3f", z))
    x, y, z, lvl = KCD2MP_W158BadgeAnchor(mkAvatar({ Neck = { x = 1000, y = 2000, z = 98.5 } }), 1, 2, 3)
    check("B8: a neck 1.5 m under the entity point (a bad read): refused", lvl == "stream")
    x, y, z, lvl = KCD2MP_W158BadgeAnchor(nil, 1, 2, 3)
    check("B9: no entity: the stream's own point", lvl == "stream" and x == 1)
    x, y, z, lvl = KCD2MP_W158BadgeAnchor({ GetWorldPos = function() error("gone") end }, 1, 2, 3)
    check("B10: an entity that errors on its own position: the stream's own point", lvl == "stream")
    x, y, z, lvl = KCD2MP_W158BadgeAnchor({ GetWorldPos = function() return { x = 1, y = 2, z = 3 } end }, 7, 8, 9)
    check("B11: an entity with no bone call at all: the stream's own point", lvl == "stream" and x == 7)
    ERRS = {}   -- the refusals above are pcall-swallowed on purpose
end

-- (L) the label loop -------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    DRAWN = {}
    System.DrawLabel = function(p, size, text, r, g, b, a) DRAWN[#DRAWN + 1] = { x = p.x, y = p.y, z = p.z, size = size, text = text } end
    KCD2MP.labelRunning = true
    local ent = mkAvatar({ Neck = { x = 1000, y = 2000, z = 101.5 } })
    KCD2MP.labelCache = { ["0"] = { x = 1, y = 2, z = 3, size = 1.0, name = "Partner", ent = ent } }
    local mark = #LOG
    NOW = 100; KCD2MP_LabelTick()
    check("L1: drawn at the neck + 0.45 m", #DRAWN == 1 and near(DRAWN[1].z, 101.95) and DRAWN[1].text == "Partner", DRAWN[1] and DRAWN[1].z)
    check("L2: the level said once", lastLog("WO158-BADGE id=0 hangs on the neck bone", mark) ~= nil)
    local told = 0
    for i = mark + 1, #LOG do if LOG[i]:find("WO158-BADGE", 1, true) then told = told + 1 end end
    NOW = 101; KCD2MP_LabelTick(); KCD2MP_LabelTick()
    local told2 = 0
    for i = mark + 1, #LOG do if LOG[i]:find("WO158-BADGE", 1, true) then told2 = told2 + 1 end end
    check("L3: not again while the level holds", told == 1 and told2 == 1, told .. "/" .. told2)
    -- the bone read goes bad: back to the stream's point, said (after the 10 s hold-off)
    ent.GetBonePos = function() return { x = 0, y = 0, z = 0 } end
    DRAWN = {}; NOW = 120; KCD2MP_LabelTick()
    check("L4: a bad read falls back to the stream's own point", #DRAWN == 1 and DRAWN[1].z == 3 and DRAWN[1].x == 1, DRAWN[1] and DRAWN[1].z)
    check("L5: ...and says so", lastLog("WO158-BADGE id=0 hangs on the stream height", mark) ~= nil)
    -- a cache row with no entity (an older writer) draws as before
    KCD2MP.labelCache = { ["1"] = { x = 5, y = 6, z = 7, size = 1.0, name = "Old" } }
    DRAWN = {}; NOW = 121; KCD2MP_LabelTick()
    check("L6: a row with no entity draws at its own point", #DRAWN == 1 and DRAWN[1].z == 7)
    -- the player's own switches
    KCD2MP.labelCache = { ["0"] = { x = 1, y = 2, z = 3, size = 1.0, name = "Partner", ent = mkAvatar({ Neck = { x = 1000, y = 2000, z = 101.5 } }) } }
    KCD2MP.w154menu = KCD2MP.w154menu or {}
    local M = KCD2MP.w154menu
    M.nameBadges = false; DRAWN = {}; NOW = 122; KCD2MP_LabelTick()
    check("L7: mp_name_badges off: no badge", #DRAWN == 0)
    M.nameBadges = true; M.clean = true; DRAWN = {}; NOW = 123; KCD2MP_LabelTick()
    check("L8: mp_clean_screen: no badge", #DRAWN == 0)
    M.clean = false; DRAWN = {}; NOW = 124; KCD2MP_LabelTick()
    check("L9: both back on: drawn", #DRAWN == 1)
    KCD2MP.labelCache = { ["0"] = { x = 1, y = 2, z = 3, size = 0, name = "Far", ent = mkAvatar({ Neck = { x = 1000, y = 2000, z = 101.5 } }) } }
    DRAWN = {}; NOW = 125; KCD2MP_LabelTick()
    check("L10: size 0 (too far): not drawn", #DRAWN == 0)
    KCD2MP.labelRunning = false
    noErrs("L")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
