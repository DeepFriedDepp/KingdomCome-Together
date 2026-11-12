-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-206 D / WO-220: the bone sampler (test bench only, loaded into a copy by tools/rig; never part of the mod). The nearest bodies
-- within 40 m of the player, N times a second: joints in world space (Entity.GetBonePos), one log line per body:
-- [W206B] <engine seconds> <name> root=x,y,z lfoot=... rfoot=... (and spine, head, lhand, rhand when asked for).
-- WO-220: this game's human skeleton names its joints Hips / Spine / Head / LeftHand / RightHand / LeftFoot / RightFoot. The names
-- asked for before ("Bip01 ...") do not exist on it: every row since WO-206 was a name with no joints. A body whose skeleton gives
-- no joint now writes NO line (an empty row is no sample), and the start line says how many of the first bodies answered.
-- tools/rig/bones.py merges the lines into the probe's samples; tools/rig/scoreboard.py reads the step and the planted foot's slide.
-- (three console sends: the console truncates past ~2,100 encoded characters -- the tables, the scan, the timer)
W206B_BONES = { root = "Hips", spine = "Spine", head = "Head", lhand = "LeftHand", rhand = "RightHand",
                lfoot = "LeftFoot", rfoot = "RightFoot" }
W206B_ORDER = { "root", "spine", "head", "lhand", "rhand", "lfoot", "rfoot" }
function W206Bones_Line(e, t)
    local parts = {}
    for _, k in ipairs(W206B_USE or W206B_ORDER) do
        local ok, v = pcall(e.GetBonePos, e, W206B_BONES[k])
        if ok and v then parts[#parts + 1] = string.format("%s=%.3f,%.3f,%.3f", k, v.x, v.y, v.z) end
    end
    if #parts == 0 then return false end
    System.LogAlways(string.format("[W206B] %.3f %s %s", t, tostring(e:GetName()), table.concat(parts, " ")))
    return true
end
-- @@SPLIT@@
function W206Bones_Scan(n)
    local pp = player:GetWorldPos()
    local list = {}
    for _, e in ipairs(System.GetEntitiesInSphere(pp, 40) or {}) do
        if e.GetBonePos and (e.soul or e == player) then
            local q = e:GetWorldPos()
            list[#list + 1] = { e = e, d = (q.x - pp.x) ^ 2 + (q.y - pp.y) ^ 2 }
        end
    end
    table.sort(list, function(a, b) return a.d < b.d end)
    local t = System.GetCurrTime()
    local asked, answered = 0, 0
    for i = 1, math.min(n, #list) do
        asked = asked + 1
        if W206Bones_Line(list[i].e, t) then answered = answered + 1 end
    end
    return asked, answered
end
-- @@SPLIT@@
function W206Bones_Start(maxBodies, everyMs, joints)
    W206B_GEN = (W206B_GEN or 0) + 1
    local gen, n, every = W206B_GEN, tonumber(maxBodies) or 6, tonumber(everyMs) or 200
    W206B_USE = nil
    if type(joints) == "string" and joints ~= "" then
        W206B_USE = {}
        for k in string.gmatch(joints, "[^,]+") do if W206B_BONES[k] then W206B_USE[#W206B_USE + 1] = k end end
    end
    local first = true
    local function tick()
        if gen ~= W206B_GEN then return end
        local ok, asked, answered = pcall(W206Bones_Scan, n)
        if ok and first then
            first = false
            System.LogAlways(string.format("[W206B-FIRST] bodies=%d with_joints=%d", asked, answered))
        end
        Script.SetTimer(every, tick)
    end
    System.LogAlways(string.format("[W206B-SYNC] %.3f", System.GetCurrTime()))
    tick()
end
function W206Bones_Stop() W206B_GEN = (W206B_GEN or 0) + 1 end
