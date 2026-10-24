-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-206 D: the bone prototype (test bench only, loaded into a copy by tools/rig; never part of the mod). Up to 6 bodies within
-- 40 m of the player, 5 times a second: root, spine, head, both hands and both feet in world space (Entity.GetBonePos), one log
-- line per body: [W206B] <engine seconds> <name> root=x,y,z spine=... head=... lhand=... rhand=... lfoot=... rfoot=...
-- tools/rig/bones.py merges the lines into the probe's samples ("b") by name and nearest time (the engine clock is mapped to
-- wall time once, from a [W206B-SYNC] line the rig times as it reads it).
-- (two console sends: the console truncates past ~2,100 encoded characters -- the tables first, the functions second)
W206B_BONES = { root = "Bip01", spine = "Bip01 Spine1", head = "Bip01 Head", lhand = "Bip01 L Hand", rhand = "Bip01 R Hand",
                lfoot = "Bip01 L Foot", rfoot = "Bip01 R Foot" }
W206B_ORDER = { "root", "spine", "head", "lhand", "rhand", "lfoot", "rfoot" }
function W206Bones_Line(e, t)
    local parts = {}
    for _, k in ipairs(W206B_ORDER) do
        local ok, v = pcall(e.GetBonePos, e, W206B_BONES[k])
        if ok and v then parts[#parts + 1] = string.format("%s=%.3f,%.3f,%.3f", k, v.x, v.y, v.z) end
    end
    System.LogAlways(string.format("[W206B] %.3f %s %s", t, tostring(e:GetName()), table.concat(parts, " ")))
end
-- @@SPLIT@@
function W206Bones_Start(maxBodies, everyMs)
    W206B_GEN = (W206B_GEN or 0) + 1
    local gen, n, every = W206B_GEN, tonumber(maxBodies) or 6, tonumber(everyMs) or 200
    local function tick()
        if gen ~= W206B_GEN then return end
        pcall(function()
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
            for i = 1, math.min(n, #list) do W206Bones_Line(list[i].e, t) end
        end)
        Script.SetTimer(every, tick)
    end
    System.LogAlways(string.format("[W206B-SYNC] %.3f", System.GetCurrTime()))
    tick()
end
function W206Bones_Stop() W206B_GEN = (W206B_GEN or 0) + 1 end
