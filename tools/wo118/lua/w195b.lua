-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-195B live helpers (tools/wo118, never shipped). Load with: python live.py luaf lua/w195b.lua
-- One command per function (the console ceiling): no trailing comments on a code line, every function short.
--   W195B_Sig(e)                  the engine's own state flags on an NPC (recognition levels I/II, the crime interrupts, sleeping)
--   W195B_Me()                    Henry's derived stats: vib (visibility) fsm (footstep) noi (noise) con evi
--   W195B_Start(tag, names, ms, secs)   a sampler: one [W195B] line per tick with Henry's stats and each named NPC's row
--   W195B_Stop()                  stop the sampler
W195B = W195B or { smp = nil }
function W195B_Sig(e)
  local c = ""
  for _, n in ipairs({"switch_recognitionLevel_I", "switch_recognitionLevel_II", "crime_interruptWatch", "crime_interruptMindPlayer", "crime_interruptReport", "crime_interruptFlee", "crime_interruptMercy", "crime_interruptWakeUp", "crime_sleeping"}) do
    local v = false
    pcall(function() v = e.soul:HasScriptContext(n) == true end)
    c = c .. (v and "1" or "0")
  end
  return c
end
function W195B_Me()
  local o = {}
  for _, c in ipairs({"vib", "fsm", "noi", "con", "evi"}) do
    local v = nil
    pcall(function() v = player.soul:GetDerivedStat(c) end)
    o[#o + 1] = c .. "=" .. string.format("%.3f", tonumber(v) or -99)
  end
  return table.concat(o, " ")
end
function W195B_Row(e)
  local q = e:GetWorldPos()
  local p = player:GetWorldPos()
  local dx, dy = p.x - q.x, p.y - q.y
  local d = math.sqrt(dx * dx + dy * dy + (p.z - q.z) ^ 2)
  local f = e:GetDirectionVector(1)
  local ang = math.deg(math.acos(math.max(-1, math.min(1, (f.x * dx + f.y * dy) / math.max(0.01, math.sqrt(dx * dx + dy * dy))))))
  local sl = 0
  pcall(function() if e.human:IsSleeping() then sl = 1 end end)
  local v = nil
  pcall(function() v = e.soul:GetDerivedStat("vir") end)
  local aw = 0
  pcall(function()
    local r = XGenAIModule.FindLinks(XGenAIModule.GetMyWUID(e), "crime_playerAwareness")
    if type(r) == "table" and #r > 0 then aw = 1 end
  end)
  return string.format("%s d=%.1f a=%.0f dz=%.1f sl=%d sig=%s los=%d vir=%.1f aw=%d", e:GetName(), d, ang, q.z - p.z, sl, W195B_Sig(e), W195B_Los(e:GetName()), tonumber(v) or -99, aw)
end
function W195B_Tick(s)
  if W195B.smp ~= s then return end
  local t = os.clock() - s.t0
  local o = {}
  for _, n in ipairs(s.names) do
    local e = System.GetEntityByName(n)
    if e then
      local ok, r = pcall(W195B_Row, e)
      o[#o + 1] = ok and r or (n .. " err")
    end
  end
  System.LogAlways(string.format("[W195B] %s t=%.1f me[%s] %s", s.tag, t, W195B_Me(), table.concat(o, " | ")))
  if t < s.secs then Script.SetTimer(s.ms, function() W195B_Tick(s) end) else W195B.smp = nil end
end
function W195B_Start(tag, names, ms, secs)
  local s = { tag = tag, names = names, ms = ms, secs = secs, t0 = os.clock() }
  W195B.smp = s
  W195B_Tick(s)
end
function W195B_Stop()
  W195B.smp = nil
end
function W195B_Go(x, y, z, zang)
  player:SetWorldPos({ x = x, y = y, z = z })
  player:SetWorldAngles({ x = 0, y = 0, z = zang })
end
function W195B_Los(n)
  local e = System.GetEntityByName(n)
  local q = e:GetWorldPos()
  local p = player:GetWorldPos()
  local dx, dy, dz = p.x - q.x, p.y - q.y, (p.z + 1.2) - (q.z + 1.6)
  local d = math.sqrt(dx * dx + dy * dy + dz * dz)
  local hits = Physics.RayWorldIntersection({ x = q.x, y = q.y, z = q.z + 1.6 }, { x = dx, y = dy, z = dz }, 1, ent_all, e.id)
  if type(hits) ~= "table" or #hits == 0 then return 1 end
  local h = hits[1]
  if h.entity == player then return 1 end
  if (tonumber(h.dist) or 0) >= d - 0.5 then return 1 end
  return 0
end
function W195B_Npc(n)
  local e = System.GetEntityByName(n)
  local function rd(c)
    local v = nil
    pcall(function() v = e.soul:GetDerivedStat(c) end)
    return tonumber(v) or -99
  end
  local q = e:GetWorldPos()
  local f = e:GetDirectionVector(1)
  System.LogAlways(string.format("[W195B] npc %s vir=%.2f fov=%.0f pos=%.2f,%.2f,%.2f fwd=%.3f,%.3f los=%d", n, rd("vir"), rd("fov"), q.x, q.y, q.z, f.x, f.y, W195B_Los(n)))
end
function W195B_Quiet(obs, r, secs)
  local p = player:GetWorldPos()
  local tw = XGenAIModule.GetMyWUID(player)
  local n = 0
  for _, e in ipairs(System.GetEntitiesInSphere(p, r) or {}) do
    if (e.class == "NPC" or e.class == "NPC_Female") and e.GetName and e:GetName() ~= obs and e.id ~= player.id then
      local ew = XGenAIModule.GetMyWUID(e)
      if ew and pcall(function() XGenAIModule.IgnorePerception(ew, tw, secs .. "s", false, true) end) then n = n + 1 end
    end
  end
  System.LogAlways("[W195B] quiet " .. tostring(obs) .. " n=" .. n)
end
function W195B_Draw(on)
  local id = nil
  for _, w in ipairs(player.inventory:GetInventoryTable()) do
    local it = ItemManager.GetItem(w)
    if it and tostring(it.class):sub(1, 8) == "b867dd0e" then id = w end
  end
  if on then
    pcall(function() player.actor:EquipInventoryItem(id) end)
    pcall(function() player.human:DrawFromInventory(id, 0, true) end)
  else
    pcall(function() player.human:HolsterWeapon() end)
  end
  System.LogAlways("[W195B] draw " .. tostring(on) .. " drawn=" .. tostring(player.human:IsWeaponDrawn()))
end
function W195B_Floor(x, y, z)
  local fz = nil
  pcall(function()
    local hits = Physics.RayWorldIntersection({ x = x, y = y, z = z + 1.5 }, { x = 0, y = 0, z = -4 }, 15, 1)
    if hits and hits[1] then
      local h = hits[1]
      local pt = h.pt or h.pos or h.point
      if pt then fz = pt.z end
    end
  end)
  System.LogAlways("[W195B] floor " .. tostring(fz))
end
function W195B_LosCount(r)
  local p = player:GetWorldPos()
  local n, tot = 0, 0
  for _, e in ipairs(System.GetEntitiesInSphere(p, r) or {}) do
    if (e.class == "NPC" or e.class == "NPC_Female") and e.GetName then
      local sl = false
      pcall(function() sl = e.soul:HasScriptContext("crime_sleeping") end)
      if not sl then
        tot = tot + 1
        local ok, v = pcall(W195B_Los, e:GetName())
        if ok and v == 1 then n = n + 1 end
      end
    end
  end
  System.LogAlways(string.format("[W195B] loscount %d/%d", n, tot))
end
function W195B_Equip()
  for _, w in ipairs(player.inventory:GetInventoryTable()) do
    local it = ItemManager.GetItem(w)
    if it and tostring(it.class):sub(1, 8) == "b867dd0e" then pcall(function() player.actor:EquipInventoryItem(w) end) end
  end
end
function W195B_Areas(r)
  local o = {}
  local p = player:GetWorldPos()
  for _, e in ipairs(System.GetEntitiesInSphere(p, r) or {}) do
    if (e.class == "NPC" or e.class == "NPC_Female") and e.GetName then
      local sl = false
      pcall(function() sl = e.soul:HasScriptContext("crime_sleeping") end)
      local q = e:GetWorldPos()
      local a = KCD2MP_W157AreaAt(q)
      local it = false
      pcall(function() it = XGenAIModule.IsPointInAreaWithLabel({ x = q.x, y = q.y, z = q.z }, "interior") == true end)
      if a ~= "public" or it then o[#o + 1] = string.format("%s:%s:%s:%d:%.1f,%.1f,%.1f", e:GetName(), a, tostring(it), sl and 1 or 0, q.x, q.y, q.z) end
    end
  end
  System.LogAlways("[W195B] areas " .. table.concat(o, " "))
end
function W195B_Sl(tag, ox, oy, oz, r)
  local o = {}
  for _, e in ipairs(System.GetEntitiesInSphere({ x = ox, y = oy, z = oz }, r) or {}) do
    if (e.class == "NPC" or e.class == "NPC_Female") and e.GetName then
      local q = e:GetWorldPos()
      local dx, dy, dz = q.x - ox, q.y - oy, (q.z + 0.4) - oz
      local d = math.sqrt(dx * dx + dy * dy + dz * dz)
      local los = 1
      local hits = Physics.RayWorldIntersection({ x = ox, y = oy, z = oz }, { x = dx, y = dy, z = dz }, 1, ent_all, e.id)
      if type(hits) == "table" and #hits > 0 and (tonumber(hits[1].dist) or 99) < d - 0.4 then los = 0 end
      local sl = false
      pcall(function() sl = e.soul:HasScriptContext("crime_sleeping") end)
      o[#o + 1] = string.format("%s:%.1f:%.1f:%d:%d:%.1f:%.1f:%.1f", e:GetName(), d, q.z - oz, los, sl and 1 or 0, q.x, q.y, q.z)
    end
  end
  System.LogAlways("[W195B] sl " .. tag .. " " .. table.concat(o, " "))
end
function W195B_Sound(kind, x, y, z, m)
  local ok, e = pcall(function() return XGenAIModule.ProduceSound(kind, { x = x, y = y, z = z }, m) end)
  System.LogAlways("[W195B] sound " .. kind .. " m=" .. m .. " ok=" .. tostring(ok) .. " " .. tostring(e))
end
function W195B_Near(gk, npc, deg, stance, dist)
  local e = System.GetEntityByName(npc)
  local q = e:GetWorldPos()
  local f = e:GetDirectionVector(1)
  local a = math.atan2(f.y, f.x) + math.rad(deg)
  local g = KCD2MP.ghosts[gk]
  g.entity:SetWorldPos({ x = q.x + math.cos(a) * dist, y = q.y + math.sin(a) * dist, z = q.z + 0.1 })
  g.istate = g.istate or {}
  g.istate.stance = stance
  g.istate.smoothedSpeed = 0
  return e
end
function W195B_PickCase(gk, npc, deg, stance, cls, tok)
  local e = W195B_Near(gk, npc, deg, stance, 1.0)
  e.inventory:CreateItem(cls, 1, 1)
  KCD2MP_W134HostTake(gk, tok, npc, cls, 1, 1)
end
function W195B_Takedown(gk, npc, tok)
  W195B_Near(gk, npc, 180, "c", 1.2)
  KCD2MP_W135HostTakedown(gk, tok, npc, "knockout")
end
function W195B_Chests(r)
  local o = {}
  local p = player:GetWorldPos()
  for _, e in ipairs(System.GetEntitiesInSphere(p, r) or {}) do
    if tostring(e.class or "") == "Stash" and e.bLocked == true then
      local q = e:GetWorldPos()
      o[#o + 1] = string.format("%s:%.1f:%.1f:%.1f:%s", e:GetName(), q.x, q.y, q.z, tostring(e.bLocked))
    end
  end
  System.LogAlways("[W195B] chests " .. #o .. " " .. table.concat(o, " "))
end
function W195B_ChestFind(key)
  for _, e in ipairs(System.GetEntitiesInSphere(player:GetWorldPos(), 200) or {}) do
    if tostring(e.class or "") == "Stash" and string.find(e:GetName(), key, 1, true) then return e end
  end
  return nil
end
function W195B_ChestAsk(peer, key)
  local e = W195B_ChestFind(key)
  if not e then System.LogAlways("[W195B] chestask no chest " .. key); return end
  local p = e:GetWorldPos()
  local h = KCD2MP.w151.doorNameHash(e:GetName())
  local before = e.bLocked
  KCD2MP_W195BChestAsked(peer, h, 1, p.x, p.y, p.z)
  System.LogAlways(string.format("[W195B] chestask %s locked before=%s after=%s", key, tostring(before), tostring(e.bLocked)))
end
function W195B_ChestState(key)
  local e = W195B_ChestFind(key)
  System.LogAlways("[W195B] cheststate " .. key .. " " .. (e and tostring(e.bLocked) or "missing"))
end
function W195B_Doors(r)
  local o = {}
  for _, e in ipairs(System.GetEntitiesInSphere(player:GetWorldPos(), r) or {}) do
    if tostring(e.class or "") == "AnimDoor" and e.bLocked == true then
      local q = e:GetWorldPos()
      o[#o + 1] = string.format("%s:%.1f:%.1f:%.1f", e:GetName(), q.x, q.y, q.z)
    end
  end
  System.LogAlways("[W195B] doors " .. #o .. " " .. table.concat(o, " "))
end
function W195B_DoorFind(key)
  for _, e in ipairs(System.GetEntitiesInSphere(player:GetWorldPos(), 200) or {}) do
    if tostring(e.class or "") == "AnimDoor" and string.find(e:GetName(), key, 1, true) then return e end
  end
  return nil
end
function W195B_DoorAsk(peer, key)
  local e = W195B_DoorFind(key)
  if not e then System.LogAlways("[W195B] doorask no door " .. key); return end
  local p = e:GetWorldPos()
  local h = KCD2MP.w151.doorNameHash(e:GetName())
  local before = e.bLocked
  KCD2MP_W151DoorAsked(peer, h, 0, 1, p.x, p.y, p.z)
  System.LogAlways(string.format("[W195B] doorask %s locked before=%s after=%s", key, tostring(before), tostring(e.bLocked)))
end
function W195B_DoorState(key)
  local e = W195B_DoorFind(key)
  System.LogAlways("[W195B] doorstate " .. key .. " " .. (e and tostring(e.bLocked) or "missing"))
end
