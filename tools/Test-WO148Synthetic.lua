-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-148 synthetic test, against the real kdcmp.lua under MoonSharp.
-- Harness copied from Test-WO147Synthetic.lua.
--   (a) this player's carry: the game's grab-body interaction names the body; the grab and the
--       set-down (after the settle) reach the agent with where the body lay and where it came to rest
--   (b) a partner's grab on its avatar: the game's own pick-up on this machine's copy; nothing alive
--       is ever picked up; a copy far from the avatar is fetched only from near the pick-up spot
--   (c) while carried the stream, the puppet, the silence release and the copy guard leave it alone
--   (d) the set-down lands the body where the carrier left it; in the air or under the ground it
--       goes back where it was picked up (the rules mirror Wo148Rules)
--   (e) the loser's game puts its carry down and back; a carrier who leaves sets everything down
--   (f) a Held for a carry whose avatar dropped it picks it up again (three times at most)
--   (g) sacks: a pile pick-up, a deposit and a drop reach the agent; a partner's dropped sack lies
--       where it landed (a prop), gone when he picks it up again
--   (h) the switches, the game's own carry link (the stealth route), the console stand-ins
-- What this proves: the Lua half. Live evidence: docs/WO-148-findings.md.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; SPAWNS = {}; CCMDS = {}; LOCKS = {}; TALKS = {}
PLAYER_IN_DIALOG = false
GROUND_Z = 0          -- the first surface under any point (nil = nothing under it)
TERRAIN_Z = 0
LINKS = {}            -- FindLinks(player, 'carriedBody') result
WUIDS = {}            -- wuid -> entity

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
System.GetTerrainElevation = function(p) return TERRAIN_Z end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Physics.RayWorldIntersection = function(o, d, flags, n)
    if GROUND_Z == nil then return nil end
    if GROUND_Z > o.z or GROUND_Z < o.z + d.z then return nil end
    return { { pt = { x = o.x, y = o.y, z = GROUND_Z } } }
end
Game.AddSaveLock = function(name, desc) if LOCKS[name] then return false end LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] == true; LOCKS[name] = nil; return had end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }
XGenAIModule = mkstub()
XGenAIModule.SpawnEntity = function(t) SPAWNS[#SPAWNS + 1] = t; ENTS[t.Name] = mkEntityLate(t.Name, t.Pos[1], t.Pos[2], t.Pos[3]) end
XGenAIModule.FindLinks = function(wuid, tag) if tag == "carriedBody" then return LINKS end return {} end
XGenAIModule.GetEntityByWUID = function(w) return WUIDS[w] end
System.SpawnEntity = function(t)
    local e = mkEntityLate(t.name, t.position.x, t.position.y, t.position.z)
    e.class = t.class; e.props = t.properties
    ENTS[t.name] = e
    SPAWNS[#SPAWNS + 1] = t
    return e
end

-- the game's BasicAIActions entry points (Scripts/Entities/AI/Shared/BasicAIActions.lua)
BasicAIActions = {}
GRABS = {}
for _, fn in ipairs({ "OnTalk", "OnChat", "OnChatWithFocus", "OnChatRequestAccepted", "OnChatOpen", "OnLoot", "OnPickpocketing" }) do
    local f = fn
    BasicAIActions[f] = function(self, user, slot) TALKS[#TALKS + 1] = f .. ":" .. tostring(self and self:GetName()) end
end
BasicAIActions.OnGrabCorpse = function(self, user, slot) GRABS[#GRABS + 1] = self:GetName(); user.actor:RequestGrabCorpse(self.id) end
-- the carry-item piles and the sack on the ground (Scripts/Entities/WH/Others/CarryItemPile.lua, Items/CarryableItem.lua)
PILE_CALLS = {}
CarryItemPile = {
    OnPickUp = function(self, user, slot) PILE_CALLS[#PILE_CALLS + 1] = "pick:" .. self:GetName() end,
    OnDeposit = function(self, user, slot) PILE_CALLS[#PILE_CALLS + 1] = "deposit:" .. self:GetName() end,
}
CarryableItem = { OnPickUp = function(self, user, slot) PILE_CALLS[#PILE_CALLS + 1] = "ground:" .. self:GetName() end }

PLAYER_CARRYING = false
PLAYER_PUTS = 0
PLAYER_GRABBED = nil
HAND = nil
player = { id = 1, this = { id = 777 }, GetName = function(self) return "Dude" end,
           px = 0, py = 0, pz = 0,
           GetWorldPos = function(self) return { x = player.px, y = player.py, z = player.pz } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end,
                     IsCarryingCorpse = function() return PLAYER_CARRYING end,
                     CanGrabCorpse = function() return true end, CanPutCorpse = function() return PLAYER_CARRYING end,
                     RequestGrabCorpse = function(self, id) PLAYER_GRABBED = id; PLAYER_CARRYING = true; return true end,
                     RequestPutCorpse = function() PLAYER_PUTS = PLAYER_PUTS + 1; PLAYER_CARRYING = false; return true end },
           human = { IsInDialog = function() return PLAYER_IN_DIALOG end, GetItemInHand = function(self, h) if h == 1 then return HAND end return nil end } }

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
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
-- the carry events without the 2 s "still carrying" confirmations
local function carries(from)
    local out = {}
    for _, a in ipairs(emitted("w148_carry", from)) do if a:sub(1, 5) ~= "held " then out[#out + 1] = a end end
    return out
end
local function helds(from)
    local n = 0
    for _, a in ipairs(emitted("w148_carry", from)) do if a:sub(1, 5) == "held " then n = n + 1 end end
    return n
end

NEXTID = 5000
local function mkEntity(name, x, y, z)
    NEXTID = NEXTID + 1
    local e = { class = "NPC", id = NEXTID, px = x or 0, py = y or 0, pz = z or 0, rz = 0, dead = false, ko = false, hp = 100, inDialog = false }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self) return { x = self.px, y = self.py, z = self.pz } end
    e.GetWorldAngles = function(self) return { x = 0, y = 0, z = self.rz } end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z; self.moves = (self.moves or 0) + 1 end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.StartAnimation = function() end
    e.carrying = false; e.grabs = 0; e.puts = 0
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return e.ko end, GetHealth = function() return e.hp end,
                GetCurrentAnimationState = function() return "MotionIdle" end,
                IsCarryingCorpse = function() return e.carrying end,
                CanGrabCorpse = function() return true end,
                RequestGrabCorpse = function(self, id) e.grabs = e.grabs + 1; e.grabbed = id; if e.takes ~= false then e.carrying = true end; return true end,
                RequestPutCorpse = function() e.puts = e.puts + 1; e.carrying = false; return true end }
    e.human = { IsWeaponDrawn = function() return false end, IsInDialog = function() return e.inDialog == true end,
                DrawWeapon = function() return true end, HolsterWeapon = function() return true end }
    e.hidden = false
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.actor.StandUp = function() end
    e.soul = { DealDamage = function() end }
    e.SetFlags = function() end
    return e
end
mkEntityLate = mkEntity

local function fireTimers()
    local again = true
    while again do
        again = false
        for i, t in ipairs(TIMERS) do
            if t and not t.done and NOW >= t.at + t.ms / 1000 then
                t.done = true; again = true
                t.f()
            end
        end
    end
end
local function advance(seconds)
    local t_end = NOW + seconds
    while NOW < t_end do
        NOW = NOW + 0.1
        KCD2MP_W148Tick()
        fireTimers()
    end
end
local function avatar(id, x, y, z)
    local e = mkEntity("kcd2mp_" .. id, x, y, z)
    KCD2MP.ghosts[tostring(id)] = { entity = e }
    ENTS["kcd2mp_" .. id] = e
    return e
end
local function reset()
    KCD2MP.ghosts = {}; KCD2MP.npcPuppets = {}
    ENTS = {}; SPHERE = {}; ERRS = {}; CMDS = {}; SPAWNS = {}; TOASTS = {}; TIMERS = {}; GRABS = {}; PILE_CALLS = {}
    LINKS = {}; WUIDS = {}
    GROUND_Z = 0; TERRAIN_Z = 0
    PLAYER_CARRYING = false; PLAYER_PUTS = 0; PLAYER_GRABBED = nil; HAND = nil
    player.px, player.py, player.pz = 0, 0, 0
    local W = KCD2MP.w148
    W.carrySync = true; W.carryObjects = true; W.session = true; W.isHost = true
    W.av = {}; W.holds = {}; W.mine = nil; W.ended = nil; W.lastGrab = nil; W.tickAt = 0
    W.objPick = nil; W.objMine = nil; W.objDeposit = nil; W.putItemAt = nil; W.depositAt = nil
    for k, pr in pairs(W.props) do W.props[k] = nil end
    KCD2MP_W148Session(true, true)
end

-- ================================================================ (a) this player's carry
do
    reset()
    local body = mkEntity("bandit_camp_3", 3, 4, 0); body.dead = true; ENTS[body:GetName()] = body
    local from = #LOG
    check("a: the session installs the grab wrap", BasicAIActions.OnGrabCorpse == KCD2MP.w148.grabWrap)
    BasicAIActions.OnGrabCorpse(body, player, 0)                 -- the game's interaction
    check("a: the game's own grab still runs", GRABS[#GRABS] == "bandit_camp_3" and PLAYER_GRABBED == body.id)
    body.px, body.py, body.pz = 0.2, 0.1, 1.4                     -- on the shoulder
    advance(0.5)
    local g = carries(from)
    check("a: the grab reaches the agent with where the body lay", g[1] == "grab dead bandit_camp_3 3.000 4.000 0.000", g[1])
    check("a: the carried body is held here", KCD2MP_W148Holds("bandit_camp_3"))
    advance(2.0)
    check("a: no second grab while it is carried", #carries(from) == 1)
    check("a: ...the game confirms the carry every 2 s", helds(from) >= 1, helds(from))
    -- set down 1.5 m ahead
    PLAYER_CARRYING = false
    advance(0.5)
    check("a: no set-down before the body settles", #carries(from) == 1)
    body.px, body.py, body.pz = 0, 1.5, 0.05
    advance(1.6)
    local p = carries(from)
    check("a: the set-down reaches the agent with where it came to rest", p[2] == "put put dead bandit_camp_3 0.000 1.500 0.050", p[2])
    check("a: held for the grace after the set-down", KCD2MP_W148Holds("bandit_camp_3"))
    advance(5.5)
    check("a: ...and free after it", not KCD2MP_W148Holds("bandit_camp_3"))
    -- no session: shown nowhere, still held locally
    reset(); KCD2MP_W148Session(false, false)
    local b2 = mkEntity("villager_9", 1, 0, 0); b2.ko = true; ENTS["villager_9"] = b2
    from = #LOG
    BasicAIActions.OnGrabCorpse(b2, player, 0)
    advance(0.5)
    check("a: without a session nothing goes out", #carries(from) == 0)
    check("a: ...the local line is still written", logCount("MP-CARRY local grab ko villager_9", from) == 1)
    -- a load replaced the world mid-carry: lost, not put
    reset()
    local b3 = mkEntity("corpse_q", 1, 1, 0); b3.dead = true; ENTS["corpse_q"] = b3
    BasicAIActions.OnGrabCorpse(b3, player, 0); advance(0.3)
    from = #LOG
    ENTS["corpse_q"] = nil; PLAYER_CARRYING = false
    advance(2.0)
    local l = carries(from)
    check("a: a carry whose body vanished (a load) ends as lost", l[1] ~= nil and l[1]:find("^put lost dead corpse_q") ~= nil, l[1])
    noErrs("a")
end

-- ================================================================ (b) a partner's grab on its avatar
do
    reset()
    local av = avatar(2, 10, 10, 0)
    local body = mkEntity("bandit_camp_3", 11, 10, 0); body.dead = true; ENTS["bandit_camp_3"] = body
    local from = #LOG
    KCD2MP_W148Apply("2", "grab", "dead", "bandit_camp_3", 11, 10, 0)
    check("b: the avatar picks the copy up with the game's own call", av.grabs == 1 and av.grabbed == body.id)
    check("b: the copy is held while carried", KCD2MP_W148Holds("bandit_camp_3"))
    check("b: nothing moved to start it (near the avatar)", (body.moves or 0) == 0)
    advance(3.0)
    check("b: the avatar's carry is confirmed", logCount("avatar 2 grab bandit_camp_3 -> carrying", from) == 1, lastLog("avatar 2 grab"))
    check("b: the agent hears it was shown", #emitted("w148_result", from) >= 1 and emitted("w148_result", from)[1] == "2 grab bandit_camp_3 ok -")
    -- the same grab again (a repeat): nothing new
    KCD2MP_W148Apply("2", "grab", "dead", "bandit_camp_3", 11, 10, 0)
    check("b: a repeat changes nothing", av.grabs == 1)

    -- alive: never
    reset()
    av = avatar(2, 0, 0, 0)
    local alive = mkEntity("villager_1", 1, 0, 0); ENTS["villager_1"] = alive
    from = #LOG
    KCD2MP_W148Apply("2", "grab", "ko", "villager_1", 1, 0, 0)
    check("b: a living body is never picked up", av.grabs == 0 and (alive.moves or 0) == 0)
    check("b: ...and the refusal says why", emitted("w148_result", from)[1] == "2 grab villager_1 refused alive")

    -- far: fetched from near the pick-up spot, refused when far from both
    reset()
    av = avatar(2, 0, 0, 0)
    local drift = mkEntity("corpse_d", 20, 0, 0); drift.dead = true; ENTS["corpse_d"] = drift
    KCD2MP_W148Apply("2", "grab", "dead", "corpse_d", 1, 0, 0)
    check("b: a drifted copy is fetched to where the carrier picked it up", drift.px == 1 and av.grabs == 1)
    reset()
    av = avatar(2, 0, 0, 0)
    local far = mkEntity("corpse_f", 200, 0, 0); far.dead = true; ENTS["corpse_f"] = far
    from = #LOG
    KCD2MP_W148Apply("2", "grab", "dead", "corpse_f", 1, 0, 0)
    check("b: a copy far from the avatar and the pick-up spot is not moved", far.px == 200 and av.grabs == 0)
    check("b: ...refused as far", emitted("w148_result", from)[1] == "2 grab corpse_f refused far")
    -- no avatar, no body
    reset()
    from = #LOG
    KCD2MP_W148Apply("3", "grab", "dead", "nobody", 0, 0, 0)
    check("b: no avatar: refused", emitted("w148_result", from)[1] == "3 grab nobody refused no-avatar")
    avatar(3, 0, 0, 0)
    KCD2MP_W148Apply("3", "grab", "dead", "nobody", 0, 0, 0)
    check("b: no body: refused", emitted("w148_result", from)[2] == "3 grab nobody refused no-body")
    noErrs("b")
end

-- ================================================================ (c) held: nothing else moves it
do
    reset()
    local av = avatar(2, 0, 0, 0)
    local body = mkEntity("bandit_camp_3", 1, 0, 0); body.dead = true; ENTS["bandit_camp_3"] = body
    KCD2MP_W148Apply("2", "grab", "dead", "bandit_camp_3", 1, 0, 0)
    local moves = body.moves or 0
    KCD2MP_ApplyNpcState("bandit_camp_3", 50, 50, 0, 0, 0, 1, 1, 7, 1000)
    check("c: the host's stream sample is not applied to a carried body", (body.moves or 0) == moves and KCD2MP.npcPuppets["bandit_camp_3"] == nil)
    KCD2MP_ApplyNpcState("bandit_camp_3", 50, 50, 0, 0, 0, 65, 1, 8, 1000)   -- a resync
    check("c: ...nor a resync", (body.moves or 0) == moves)
    local p = { lastPacketAt = -100 }
    check("c: a carried body's puppet is never released for silence", KCD2MP_NpcSilenceRelease("bandit_camp_3", p, NOW) == false)
    KCD2MP.w131.guard = true; KCD2MP.w131.joiner = true; KCD2MP.w131.shared = true; KCD2MP.w131.aliveAt = NOW
    KCD2MP.wo102.authorityHost = true; KCD2MP.hitSensorOn = false
    body.ko = true; body.dead = false
    check("c: ...and the copy guard never hides it", KCD2MP_W131ParkReleased("bandit_camp_3", "silence") == true and body.hidden == false)
    noErrs("c")
end

-- ================================================================ (d) where it lands
do
    -- the landing rule (mirrors Wo148Rules.Decide)
    local function D(rz, gr, cz, gc, dx)
        return KCD2MP_W148Landing({ x = 0, y = 0, z = rz }, gr, { x = dx or 0, y = 0, z = cz }, gc)
    end
    check("d: rule: 1.5 m from the carrier's spot -> moved there", D(5, 5, 5, 5, 1.5) == "carrier")
    check("d: rule: within the half metre -> kept", D(5, 5, 5, 5, 0.3) == "keep")
    check("d: rule: in the air, the carrier's spot no better -> back", D(8, 5, 8, 5) == "back")
    check("d: rule: under the ground -> back", D(3, 5, 3, 5) == "back")
    check("d: rule: nothing under it -> back", D(3, nil, 3, nil) == "back")
    check("d: rule: a bad rest, a good carrier spot -> moved there", D(3, 5, 5.1, 5, 0.2) == "carrier")
    check("d: rule: the carrier's spot bad here, the rest fine -> kept", D(5, 5, 9, 5, 10) == "keep")

    -- a whole set-down: rest 1.5 m off the carrier's spot -> moved there
    reset()
    local av = avatar(2, 0, 0, 0)
    local body = mkEntity("bandit_camp_3", 1, 0, 0); body.dead = true; ENTS["bandit_camp_3"] = body
    KCD2MP_W148Apply("2", "grab", "dead", "bandit_camp_3", 1, 0, 0)
    advance(3)
    body.px, body.py, body.pz = 5, 5, 0
    local from = #LOG
    KCD2MP_W148Apply("2", "put", "dead", "bandit_camp_3", 6.5, 5, 0, "put")
    check("d: the avatar puts it down with the game's own call", av.puts == 1)
    advance(2.5)
    check("d: the body lands where the carrier left it", body.px == 6.5 and body.py == 5, string.format("%.1f %.1f", body.px, body.py))
    check("d: ...logged as moved", logCount("-> moved to the carrier's spot", from) == 1, lastLog("land bandit_camp_3"))
    -- in the air here and at the carrier's spot -> back where it was picked up
    reset()
    av = avatar(2, 0, 0, 0)
    body = mkEntity("corpse_air", 1, 0, 0); body.dead = true; ENTS["corpse_air"] = body
    KCD2MP_W148Apply("2", "grab", "dead", "corpse_air", 1, 0, 0)
    advance(3)
    body.px, body.py, body.pz = 4, 4, 9      -- held up in the air
    GROUND_Z = 0
    from = #LOG
    KCD2MP_W148Apply("2", "put", "dead", "corpse_air", 4, 4, 9, "put")
    advance(2.5)
    check("d: a body that would stay in the air goes back where it was picked up", body.px == 1 and body.py == 0 and body.pz == 0)
    check("d: ...logged", logCount("-> put back where it was picked up", from) == 1, lastLog("land corpse_air"))
    -- under the ground
    reset()
    av = avatar(2, 0, 0, 0)
    body = mkEntity("corpse_under", 2, 0, 0); body.dead = true; ENTS["corpse_under"] = body
    KCD2MP_W148Apply("2", "grab", "dead", "corpse_under", 2, 0, 0)
    advance(3)
    body.px, body.py, body.pz = 3, 3, -2.5
    KCD2MP_W148Apply("2", "put", "dead", "corpse_under", 3, 3, -2.5, "put")
    advance(2.5)
    check("d: a body under the ground goes back where it was picked up", body.px == 2 and body.pz == 0)
    -- woke on the way: left alone
    reset()
    av = avatar(2, 0, 0, 0)
    body = mkEntity("villager_ko", 1, 0, 0); body.ko = true; ENTS["villager_ko"] = body
    KCD2MP_W148Apply("2", "grab", "ko", "villager_ko", 1, 0, 0)
    advance(3)
    body.ko = false; body.px = 7
    local moves = body.moves or 0
    KCD2MP_W148Apply("2", "put", "ko", "villager_ko", 9, 0, 0, "put")
    advance(2.5)
    check("d: a body that woke on the way is never moved", (body.moves or 0) == moves and body.px == 7)
    noErrs("d")
end

-- ================================================================ (e) the loser, the leaver
do
    reset()
    local body = mkEntity("corpse_x", 2, 2, 0); body.dead = true; ENTS["corpse_x"] = body
    BasicAIActions.OnGrabCorpse(body, player, 0)
    advance(0.3)
    body.px, body.py, body.pz = 0, 0, 1.4
    local from = #LOG
    KCD2MP_W148Loser("corpse_x", "the host gave it to player 1")
    check("e: the loser's game puts its carry down", PLAYER_PUTS == 1)
    advance(2.5)
    check("e: ...and back where it was picked up", body.px == 2 and body.py == 2 and body.pz == 0)
    check("e: ...with no set-down sent for it", #emitted("w148_carry", from) == 0)
    -- the host's grab arrives while this player carries the same body: the avatar shows the host's
    reset()
    local av = avatar(1, 0, 0, 0)
    body = mkEntity("corpse_y", 1, 0, 0); body.dead = true; ENTS["corpse_y"] = body
    BasicAIActions.OnGrabCorpse(body, player, 0)
    advance(0.3)
    KCD2MP_W148Apply("1", "grab", "dead", "corpse_y", 1, 0, 0)
    check("e: the host's grab of this player's body: this player puts it down", PLAYER_PUTS == 1)
    check("e: ...and the host's avatar carries it", av.grabs == 1)
    local movesY = body.moves or 0
    from = #LOG
    advance(2.5)
    check("e: ...the loser's put-back leaves the avatar's carry alone", (body.moves or 0) == movesY, lastLog("loser corpse_y"))
    check("e: ...logged", logCount("loser corpse_y -> player 1's avatar has it now: not put back", from) == 1)
    advance(6.0)
    check("e: ...and the body stays held while the avatar carries it", KCD2MP_W148Holds("corpse_y"))
    -- a carrier who leaves
    reset()
    av = avatar(2, 0, 0, 0)
    body = mkEntity("corpse_z", 1, 0, 0); body.dead = true; ENTS["corpse_z"] = body
    KCD2MP_W148Apply("2", "grab", "dead", "corpse_z", 1, 0, 0)
    advance(3)
    body.px, body.py, body.pz = 4, 4, 0
    KCD2MP_W148AvatarGone("2")
    check("e: a leaving carrier's avatar sets it down", av.puts == 1)
    advance(2.5)
    check("e: ...where it is (reachable)", body.px == 4 and body.py == 4)
    noErrs("e")
end

-- ================================================================ (f) Held picks up again
do
    reset()
    local av = avatar(2, 0, 0, 0)
    local body = mkEntity("corpse_h", 1, 0, 0); body.dead = true; ENTS["corpse_h"] = body
    KCD2MP_W148Apply("2", "grab", "dead", "corpse_h", 1, 0, 0)
    advance(3)
    av.carrying = false        -- the game ended the avatar's carry on the way
    NOW = NOW + 5
    KCD2MP_W148Apply("2", "held", "dead", "corpse_h", 1, 0, 0)
    check("f: a Held while the avatar is not carrying picks it up again", av.grabs == 2)
    for i = 1, 6 do av.carrying = false; NOW = NOW + 5; KCD2MP_W148Apply("2", "held", "dead", "corpse_h", 1, 0, 0) end
    check("f: ...three times at most (the first grab and three again)", av.grabs == 4, av.grabs)
    -- a Held for a carry never seen here is a grab
    reset()
    local av2 = avatar(3, 0, 0, 0)
    local b2 = mkEntity("corpse_n", 1, 0, 0); b2.dead = true; ENTS["corpse_n"] = b2
    KCD2MP_W148Apply("3", "held", "dead", "corpse_n", 1, 0, 0)
    check("f: a Held first heard shows the carry", av2.grabs == 1)
    noErrs("f")
end

-- ================================================================ (g) sacks
do
    reset()
    local pile = mkEntity("mlyn_pytle_source", 5, 5, 0); pile.class = "CarryItemPile"
    check("g: the pile and sack pick-ups are wrapped", CarryItemPile.OnPickUp == KCD2MP.w148.pileOnPickUpWrap and CarryableItem.OnPickUp == KCD2MP.w148.groundOnPickUpWrap)
    local from = #LOG
    CarryItemPile.OnPickUp(pile, player, 0)
    check("g: the game's own pick-up still runs", PILE_CALLS[#PILE_CALLS] == "pick:mlyn_pytle_source")
    HAND = 4242
    advance(1.5)
    local g = emitted("w148_carry", from)
    check("g: a sack from a pile reaches the agent", g[1] == "grab object mlyn_pytle_source 5.000 5.000 0.000", g[1])
    local target = mkEntity("mlyn_pytle_target", 9, 9, 0); target.class = "CarryItemPile"
    CarryItemPile.OnDeposit(target, player, 0)
    HAND = nil
    advance(0.5)
    g = emitted("w148_carry", from)
    check("g: the deposit into a pile is a set-down at the pile", g[2] == "put put object mlyn_pytle_source 9.000 9.000 0.000", g[2])
    -- a drop on the ground (the put_item key)
    reset()
    pile = mkEntity("mlyn_pytle_source", 5, 5, 0); pile.class = "CarryItemPile"
    CarryItemPile.OnPickUp(pile, player, 0)
    HAND = 4242
    advance(1.5)
    from = #LOG
    KCD2MP_W148OnAction("put_item", "press")
    HAND = nil
    local sack = mkEntity("sack_dropped_1", 0.5, 0.8, 0); sack.class = "CarryableItem"
    SPHERE = { sack }
    advance(2.0)
    g = emitted("w148_carry", from)
    check("g: a dropped sack is a drop where the game's sack lies", g[1] == "put drop object mlyn_pytle_source 0.500 0.800 0.000", g[1])
    -- the other screen: a partner's dropped sack lies there; his next pick-up takes it away
    reset()
    local av = avatar(2, 0, 0, 0)
    KCD2MP_W148Apply("2", "grab", "object", "mlyn_pytle_source", 5, 5, 0, "-")
    KCD2MP_W148Apply("2", "put", "object", "mlyn_pytle_source", 1, 1, 0, "drop")
    local prop = nil
    for _, s in ipairs(SPAWNS) do if s.class == "BasicEntity" then prop = s end end
    check("g: a partner's dropped sack is shown where it landed", prop ~= nil and prop.position.x == 1 and prop.position.y == 1)
    check("g: ...the game's sack model, no physics", prop ~= nil and prop.properties.object_Model == KCD2MP.w148.SACK_MODEL and prop.properties.Physics.bPhysicalize == false)
    local n = 0
    for _ in pairs(KCD2MP.w148.props) do n = n + 1 end
    check("g: ...one prop", n == 1)
    KCD2MP_W148Apply("2", "grab", "object", "sack", 1.2, 1.1, 0, "-")
    n = 0
    for _ in pairs(KCD2MP.w148.props) do n = n + 1 end
    check("g: his pick-up of it takes the prop away", n == 0)
    -- a deposit shows no prop; a drop in the air is shown at the avatar's feet
    KCD2MP_W148Apply("2", "put", "object", "x", 3, 3, 0, "put")
    check("g: a deposit into a pile leaves no prop", next(KCD2MP.w148.props) == nil)
    av.px, av.py, av.pz = 7, 7, 0
    KCD2MP_W148Apply("2", "put", "object", "x", 3, 3, 9, "drop")
    local last = SPAWNS[#SPAWNS]
    check("g: a drop spot in the air is shown at the avatar's feet instead", last.class == "BasicEntity" and last.position.x == 7)
    -- objects off
    KCD2MP_W148SetCarryObjects("off")
    local before = #SPAWNS
    KCD2MP_W148Apply("2", "put", "object", "x", 2, 2, 0, "drop")
    check("g: mp_carry_objects off shows no sack", #SPAWNS == before)
    KCD2MP_W148SetCarryObjects("on")
    noErrs("g")
end

-- ================================================================ (h) the switches, the link, the stand-ins
do
    reset()
    check("h: mp_carry_sync is registered", CCMDS["mp_carry_sync"] ~= nil and CCMDS["mp_carry_sync"].body:find("KCD2MP_W148SetCarrySync(%line)", 1, true) ~= nil)
    check("h: mp_carry_objects, mp_carry_test, mp_carry_status are registered", CCMDS["mp_carry_objects"] ~= nil and CCMDS["mp_carry_test"] ~= nil and CCMDS["mp_carry_status"] ~= nil)
    check("h: the build line names the defaults", lastLog("WO148-BUILD carry_sync=on carry_objects=on") ~= nil)
    local from = #LOG
    KCD2MP_W148SetCarrySync("off")
    check("h: off is told to the agent", emitted("w148_cfg", from)[1] == "carry_sync=off carry_objects=on")
    local av = avatar(2, 0, 0, 0)
    local body = mkEntity("corpse_s", 1, 0, 0); body.dead = true; ENTS["corpse_s"] = body
    KCD2MP_W148Apply("2", "grab", "dead", "corpse_s", 1, 0, 0)
    check("h: off: a partner's grab is not shown", av.grabs == 0)
    KCD2MP_W148SetCarrySync("bogus")
    check("h: a bad value changes nothing", KCD2MP.w148.carrySync == false)
    KCD2MP_W148SetCarrySync("on")
    -- the stealth route: no interaction seen, the game's own link names the body
    reset()
    local held = mkEntity("bandit_stealth", 0.3, 0, 1.4); held.dead = true; ENTS["bandit_stealth"] = held
    LINKS = { 9001 }; WUIDS[9001] = held
    PLAYER_CARRYING = true
    from = #LOG
    advance(0.3)
    local g = emitted("w148_carry", from)
    check("h: the carried body is found by the game's carriedBody link", g[1] ~= nil and g[1]:find("^grab dead bandit_stealth") ~= nil, g[1])
    -- the console stand-ins
    reset()
    local b2 = mkEntity("corpse_t", 1, 0, 0); b2.dead = true; ENTS["corpse_t"] = b2
    KCD2MP_W148Test("grab corpse_t")
    check("h: mp_carry_test grab makes this player pick the body up", PLAYER_GRABBED == b2.id and PLAYER_CARRYING)
    KCD2MP_W148Test("put")
    check("h: mp_carry_test put sets it down", PLAYER_PUTS == 1 and not PLAYER_CARRYING)
    KCD2MP_W148Test("status")
    check("h: status lines", lastLog("WO148-STATUS carry_sync=on") ~= nil and lastLog("WO148-STATUS objects=on") ~= nil)
    -- a name the wire refuses is never sent
    reset()
    local odd = mkEntity("odd name", 1, 0, 0); odd.dead = true; ENTS["odd name"] = odd
    from = #LOG
    BasicAIActions.OnGrabCorpse(odd, player, 0)
    advance(0.3)
    check("h: a body with a name the wire refuses is not sent", #emitted("w148_carry", from) == 0)
    noErrs("h")
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. "\n" .. string.format("%d passed, %d failed", pass, fail)
