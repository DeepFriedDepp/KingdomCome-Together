# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-223 Part E: the regression scenes -- nothing the old rules protect comes back (test tool, never shipped).

Each scene stages one thing a partner's figure was made to be left alone for, and then counts what the host's world did about the
figure while it ran (its "footprint", read from the host's own log: the barks the figure made and the barks made at it, the skirmish
events with it, the crime informations created, the mod's crime lines and the attention pairs WO-223 set). A scene is run as shipped
and as the twin (every WO-223 switch off = 0.65.2's behaviour); the two footprints are compared category by category
(research tool: compare223.py). Every action goes through the game's console or Lua; a player's own swings are keys (send-only,
tools/dev-input/game_input.py, the copy's own window in front, every event logged). Nothing here reads input.

    w223guard      every scene below, in order (KCDMP_RIG_W223_GUARD=ff,village,crime,animals picks some)
       ff          E.1 friendly fire off, then on: the joiner strikes the host's player ten times, the host the joiner's figure
       village     E.2 the joiner's figure alone in the village by day and by night: the street, beside the shopkeeper, inside a house
                   (the host 40 m off)
       crime       E.3 the joiner punches a villager in others' sight
       animals     E.5 a dog, a horse and (a wolf the bench spawns) beside the figure, the host 60 m off; the same wolf test for the host
"""
import collections, json, os, re, sys, threading, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rig


def _S():
    import scenarios
    return scenarios


BARK = re.compile(r"Attempting to start new dialogue \(runtime id '\d+'\) with souls '([^']*)'")
SKIRMISH = re.compile(r'Skirmish event: (\w+) on (\S+) \(target ([^)]*)\)')
INFO = re.compile(r"\[InformationNodesImplementation\]:'([^']+)' tried to create information '\d+', '(\w+)'")
FIG = re.compile(r'kcd2mp_\d+')


def footprint(c, mark, who='the figure'):
    """What <c>'s world did about a partner's figure since <mark>: counts by category, and the first three lines of each."""
    cnt, ex = collections.Counter(), {}

    def add(k, l):
        cnt[k] += 1
        ex.setdefault(k, [])
        if len(ex[k]) < 3: ex[k].append(l.strip()[:200])

    for l in c.lines(c.klog, mark['k']):
        m = BARK.search(l)
        if m and FIG.search(m.group(1)):
            for part in m.group(1).split(';'):
                if 'meta override: ' not in part: continue
                name = part.split('meta override: ')[-1].strip()
                speaker = part.split(':', 1)[1].split(' - ')[0].strip() if ':' in part else '?'
                add(('bark by the figure: ' if FIG.match(speaker) else 'bark at the figure: ') + name, l)
            continue
        m = SKIRMISH.search(l)
        if m and (FIG.match(m.group(2)) or FIG.match(m.group(3))):
            add('skirmish %s %s' % (m.group(1), 'on the figure' if FIG.match(m.group(2)) else 'at the figure'), l)
            continue
        m = INFO.search(l)
        if m: add('information ' + m.group(2), l); continue
        if 'WO223-GATE' in l: add('WO-223 gate named an NPC', l)
        elif 'WO223-NOTICE' in l: add('WO-223 notice', l)
    for l in c.lines(c.alog, mark['a']):
        if re.search(r'MP-W139 .*crime #|MP-W151 .*crime|WO139-JUDGE|WO154-JUDGE|stop #\d', l) and 'STATS' not in l: add('the mod: crime line', l)
    for l in c.lines(c.nlog, mark['n']):
        if 'WO223-ATTEND on' in l: add('WO-223 attention pair set', l)
        elif re.search(r'WO136-(FORCED|TARGET)', l): add('WO-136 retarget', l)
    return {'counts': dict(sorted(cnt.items())), 'examples': ex}


def save(r, name, det):
    with open(os.path.join(r.dir, 'guard-%s.json' % name), 'w', encoding='utf-8') as f: json.dump(det, f, indent=1)
    rig.say('  guard', name, json.dumps({k: v.get('counts') if isinstance(v, dict) and 'counts' in v else v for k, v in det.items() if k != 'detail'})[:900])


def inn(r):
    S = _S()
    t = r.a.ask(S.FIND_INN % os.environ.get('KCDMP_RIG_INN', 'ttkc_woman_2'), 'W207I', 6)
    if not t or t == 'none': return None
    p = t.split()
    return p[0], float(p[1]), float(p[2]), float(p[3])


def swings(r, c, n, tag):
    S = _S()
    sw = {'sent': 0, 'refused': 0, 'err': None}
    stop = threading.Event()
    th = threading.Thread(target=S.w211_swinger, args=(c, sw, stop, tag, n), daemon=True); th.start()
    th.join(timeout=30 + n * 3); stop.set()
    return sw


# ---------------------------------------------------------------- E.1 friendly fire

def s_ff(r, n=10):
    """Friendly fire off, then on: in the bench town by its people the joiner strikes the host's player <n> times, then the host the
    joiner's figure. The footprint on the host (the figure struck, the figure striking) and on the joiner (the host's figure there)."""
    def fn():
        S = _S()
        at = inn(r)
        if at: r.a.teleport(at[1] + 3.0, at[2] + 3.0); r.b.teleport(at[1] + 3.0, at[2] + 1.7)
        time.sleep(6)
        for c in (r.a, r.b): c.keep_up(); c.lua('if KCD2MP_W151SetCrimeMode then KCD2MP_W151SetCrimeMode("joint") end')
        det = {}
        for ff in ('off', 'on'):
            for c in (r.a, r.b): c.lua('KCD2MP_Wo121SetFriendlyFire("%s")' % ff)
            time.sleep(3)
            ma, mb = r.a.mark(), r.b.mark()
            sw = {}
            for who, c in (('joiner', r.b), ('host', r.a)):
                f = c.ask(S.FWD_Q, 'W210F', 6)
                other = r.a if c is r.b else r.b
                if f:
                    x, y, z, dx, dy = (float(v) for v in f.split()); nn = (dx * dx + dy * dy) ** 0.5 or 1.0
                    other.teleport(x + dx / nn * 1.3, y + dy / nn * 1.3)
                time.sleep(3)
                c.lua('pcall(function() player.human:DrawWeapon() end)')
                sw[who] = swings(r, c, n, 'wo223-ff-%s-%s' % (ff, who))
                time.sleep(4)
            time.sleep(12)
            det['ff_' + ff] = {'swings': sw, 'host': footprint(r.a, ma), 'joiner': footprint(r.b, mb)}
        for c in (r.a, r.b): c.lua('KCD2MP_Wo121SetFriendlyFire("on")'); c.stop_keep()
        save(r, 'ff', {k: v['host'] for k, v in det.items()} | {'detail': det})
        return True, 'ok', {k: {'host': v['host']['counts'], 'joiner': v['joiner']['counts'], 'swings': {w: s['sent'] for w, s in v['swings'].items()}} for k, v in det.items()}
    return fn


# ---------------------------------------------------------------- E.2 villagers and guards

HOUSE_Q = ('local p = player:GetWorldPos(); local best, bd; for _, e in ipairs(System.GetEntitiesInSphere(p, 250) or {}) do '
           'if e.class == "Bed" or (e.GetName and string.find(string.lower(e:GetName()), "bed", 1, true) and not e.soul) then local q = e:GetWorldPos(); '
           'local ind = false; pcall(function() ind = XGenAIModule.IsPointInAreaWithLabel({x = q.x, y = q.y, z = q.z}, "interior") == true end); '
           'local d = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; if ind and d > 100 and (not bd or d < bd) then bd = d; best = q end end end; '
           'if best then System.LogAlways(string.format("[W223HS] %.2f %.2f %.2f", best.x, best.y, best.z)) else System.LogAlways("[W223HS] none") end')
SHOP_Q = ('local p = player:GetWorldPos(); local best, bd; for _, e in ipairs(System.GetEntitiesInSphere(p, 300) or {}) do '
          'if e.soul and e ~= player then local ready = false; pcall(function() ready = e.soul:HasScriptContext("shop_sellerReadyToSell") == true end); '
          'if ready and not string.find(e:GetName(), "kcd2mp_", 1, true) then local q = e:GetWorldPos(); local d = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; '
          'if not bd or d < bd then bd = d; best = e end end end end; '
          'if best then local q = best:GetWorldPos(); System.LogAlways(string.format("[W223SH] %s %.2f %.2f %.2f", best:GetName(), q.x, q.y, q.z)) '
          'else System.LogAlways("[W223SH] none") end')
STROLL = ('W223ES = (W223ES or 0) + 1; local g, d = W223ES, 0; local p0 = player:GetWorldPos(); local dir = player:GetDirectionVector(1); '
          'local function step() if g ~= W223ES or d >= %f then return end; local ft = 0.016; pcall(function() ft = System.GetFrameTime() end); '
          'if type(ft) ~= "number" or ft <= 0 or ft > 0.1 then ft = 0.016 end; d = d + 1.3 * ft; '
          'player:SetWorldPos({x = p0.x + dir.x * d, y = p0.y + dir.y * d, z = p0.z}); Script.SetTimer(1, step) end; step()')


def s_village(r, stay=float(os.environ.get('KCDMP_RIG_W223_STAY_S', '30'))):
    """The joiner's figure alone among the village's people: on the street by the inn, beside the nearest shopkeeper, inside a house
    (a bed in an interior), each for <stay> seconds standing and a few steps; by day, then by night. The host stands 40 m off."""
    def fn():
        import scen217
        S = _S()
        at = inn(r)
        if not at: return False, 'no-inn', None
        det = {}
        for label, hour in (('day', 11.0), ('night', 23.0)):
            scen217.daytime(r, hour)()
            r.a.teleport(at[1] + 40.0, at[2] + 6.0)
            time.sleep(5)
            spots = [('street', (at[1] + 4.0, at[2] + 2.0, None))]
            r.b.teleport(at[1] + 4.0, at[2] + 2.0); time.sleep(4)
            sh = r.b.ask(SHOP_Q, 'W223SH', 6)
            if sh and sh != 'none':
                p = sh.split(); spots.append(('shop:' + p[0], (float(p[1]) + 1.4, float(p[2]) + 0.4, float(p[3]) + 0.2)))
            hs = r.b.ask(HOUSE_Q, 'W223HS', 6)
            if hs and hs != 'none':
                p = hs.split(); spots.append(('house', (float(p[0]) + 1.2, float(p[1]), float(p[2]) + 0.3)))
            for c in (r.a, r.b): c.keep_up()
            for name, (x, y, z) in spots:
                ma = r.a.mark()
                r.b.teleport(x, y, z) if z is not None else r.b.teleport(x, y)
                time.sleep(stay)
                r.b.lua_nowait(STROLL % 4.0)
                time.sleep(6)
                det['%s %s' % (label, name)] = footprint(r.a, ma)
            for c in (r.a, r.b): c.stop_keep()
        save(r, 'village', det)
        return True, 'ok', {k: v['counts'] for k, v in det.items()}
    return fn


# ---------------------------------------------------------------- E.3 a crime by the joiner

VILLAGER_Q = ('local p = player:GetWorldPos(); local best, bd; for _, e in ipairs(System.GetEntitiesInSphere(p, 40) or {}) do '
              'if e.soul and e ~= player and (e.class == "NPC" or e.class == "NPC_Female") and not string.find(e:GetName(), "kcd2mp_", 1, true) then '
              'local h = 0; pcall(function() h = e.soul:GetState("health") end); local q = e:GetWorldPos(); local d = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; '
              'if h > 0 and d > 1 and (not bd or d < bd) then bd = d; best = e end end end; '
              'if best then local q = best:GetWorldPos(); System.LogAlways(string.format("[W223V] %s %.2f %.2f %.2f", best:GetName(), q.x, q.y, q.z)) '
              'else System.LogAlways("[W223V] none") end')


def s_crime(r, n=3):
    """The joiner punches a villager by the inn (others in sight), by day; 40 s after. The host's footprint and both agents' crime lines:
    the crime counts as today (joint, judged by the host's world), the guards react as today."""
    def fn():
        import scen217
        S = _S()
        scen217.daytime(r, 11.0)()
        at = inn(r)
        if not at: return False, 'no-inn', None
        r.a.teleport(at[1] + 9.0, at[2] + 4.0); r.b.teleport(at[1] + 3.0, at[2] + 2.0)
        time.sleep(6)
        for c in (r.a, r.b): c.keep_up(); c.lua('if KCD2MP_W151SetCrimeMode then KCD2MP_W151SetCrimeMode("joint") end')
        v = r.b.ask(VILLAGER_Q, 'W223V', 6)
        if not v or v == 'none':
            for c in (r.a, r.b): c.stop_keep()
            return False, 'no-villager', v
        name, x, y, z = v.split()
        r.b.teleport(float(x) + 1.2, float(y), float(z) + 0.1); time.sleep(3)
        r.b.lua(S.FACE_TO % name)
        ma, mb = r.a.mark(), r.b.mark()
        sw = swings(r, r.b, n, 'wo223-crime')
        time.sleep(40)
        crime = lambda c, m: [l.strip()[:200] for l in c.lines(c.alog, m['a']) if re.search(r'MP-W139|MP-W151|WO154-JUDGE', l) and 'STATS' not in l and 'cfg' not in l]
        det = {'victim': name, 'swings': sw, 'host': footprint(r.a, ma), 'joiner': footprint(r.b, mb),
               'host_crime_lines': crime(r.a, ma)[:12], 'joiner_crime_lines': crime(r.b, mb)[:12]}
        # the guards and the victim left alone again
        r.a.lua('for _, e in ipairs(System.GetEntitiesInSphere(player:GetWorldPos(), 60) or {}) do if e.soul and e.this and e ~= player then '
                'local t = XGenAIModule.MakeTableFromType("stopFight"); t.soulCount = 1; t.messageId = "w223crime"; '
                'pcall(function() XGenAIModule.SendMessageToEntityData(e.this.id, "stopFight", t) end) end end')
        for c in (r.a, r.b): c.stop_keep()
        save(r, 'crime', det)
        return True, 'ok', {'victim': name, 'swings': sw['sent'], 'host': det['host']['counts'], 'crime_lines': [len(det['host_crime_lines']), len(det['joiner_crime_lines'])]}
    return fn


# ---------------------------------------------------------------- E.5 animals and allies

ANIMAL_Q = ('local p = player:GetWorldPos(); local out = {}; local seen = {}; for _, e in ipairs(System.GetEntitiesInSphere(p, 600) or {}) do '
            'local c = tostring(e.class); if (c == "Dog" or c == "Horse") and e.soul and not seen[c] then local h = 0; pcall(function() h = e.soul:GetState("health") end); '
            'local n = e:GetName(); if h > 0 and not string.find(n, "kcd2mp", 1, true) then seen[c] = true; local q = e:GetWorldPos(); '
            'out[#out + 1] = string.format("%s,%s,%.1f,%.1f,%.1f", c, n, q.x, q.y, q.z) end end end; System.LogAlways("[W223AN] " .. table.concat(out, " "))')
WOLF_SPAWN = ('local z = System.GetTerrainElevation({x = %f, y = %f, z = 0}); pcall(function() XGenAIModule.SpawnEntity({Name = "%s", ClassName = "Wolf", '
              'Pos = {%f, %f, z + 0.3}, SharedSoulGuid = "e1579e78-de5f-4f68-ad76-e9f5f3fad485"}) end); '
              'local e = System.GetEntityByName("%s"); System.LogAlways("[W223W] " .. tostring(e ~= nil))')
WOLF_GONE = 'local e = System.GetEntityByName("%s"); if e then pcall(function() System.RemoveEntity(e.id) end) end'
REL_Q = ('local e = System.GetEntityByName("%s"); local f; for k, g in pairs(KCD2MP.ghosts or {}) do f = g.entity end; local a, b = "?", "?"; '
         'if e and e.soul then pcall(function() a = tostring(e.soul:GetRelationship(player.this.id)) end); '
         'if f then pcall(function() b = tostring(e.soul:GetRelationship(f.this.id)) end) end end; System.LogAlways("[W223RL] " .. a .. " " .. b)')


def wolf_test(r, subject, name, watch_s=35.0):
    """A wolf of the game's own (its Trosky soul) spawned 22 m in front of <subject> ('host' | 'joiner'; the other player 60 m off).
    What it does in <watch_s>: the host's skirmish lines with it, how near it came. Removed after."""
    S = _S()
    c = r.a if subject == 'host' else r.b
    other = r.b if subject == 'host' else r.a
    f = c.ask(S.FWD_Q, 'W210F', 6)
    if not f: return {'err': 'no place'}
    x, y, z, dx, dy = (float(v) for v in f.split()); nn = (dx * dx + dy * dy) ** 0.5 or 1.0
    other.teleport(x - dx / nn * 60.0, y - dy / nn * 60.0)
    time.sleep(5)
    wx, wy = x + dx / nn * 22.0, y + dy / nn * 22.0
    ma = r.a.mark()
    ok = r.a.ask(WOLF_SPAWN % (wx, wy, name, wx, wy, name), 'W223W', 6)
    time.sleep(2)
    rel = r.a.ask(REL_Q % name, 'W223RL', 5)
    dmin = 999.0; t0 = time.time()
    while time.time() - t0 < watch_s:
        time.sleep(2.5)
        p, q = c.pos(), S.pos_of(r.a, name)
        if p and q: dmin = min(dmin, ((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2) ** 0.5)
    ev = collections.Counter()
    for l in r.a.lines(r.a.klog, ma['k']):
        m = SKIRMISH.search(l)
        if m and m.group(2) == name: ev['%s -> %s' % (m.group(1), m.group(3))] += 1
    r.a.lua(S.STOP % (name, 2231)); r.a.lua(WOLF_GONE % name)
    return {'spawned': ok, 'relationship_to_host_and_figure': rel, 'nearest_m': round(dmin, 1), 'events': dict(ev),
            'hunted': any(k.startswith(('Attack', 'HitTarget', 'TargetChanged')) and not k.endswith('-> -') for k in ev)}


def s_animals(r, stay=30.0):
    """A dog and a horse of the world beside the joiner's figure (the host 60 m off): do they attack it? Then a wolf (spawned) in front of
    the host's player, and one in front of the joiner's figure: does it hunt each?"""
    def fn():
        import scen217
        S = _S()
        scen217.daytime(r, 11.0)()
        det = {}
        for c in (r.a, r.b): c.keep_up()
        t = r.a.ask(ANIMAL_Q, 'W223AN', 8) or ''
        for part in t.split():
            cls, name, x, y, z = part.split(',')
            r.a.teleport(float(x) + 60.0, float(y)); r.b.teleport(float(x) + 2.5, float(y), float(z) + 0.2)
            time.sleep(4)
            ma = r.a.mark()
            time.sleep(stay)
            det['%s %s' % (cls.lower(), name)] = footprint(r.a, ma)
        # the wolves: on open ground away from anyone (the approach line's far end by the bench's camp)
        import scen223
        names, centre = scen223.camp(r)
        if centre:
            sx, sy = scen223.line_point(centre, 230.0)
            for subject in ('host', 'joiner'):
                (r.a if subject == 'host' else r.b).teleport(sx, sy)
                time.sleep(5)
                det['wolf at the ' + subject] = wolf_test(r, subject, 'w223_wolf_' + subject)
        for c in (r.a, r.b): c.stop_keep()
        save(r, 'animals', det)
        return True, 'ok', {k: (v.get('counts') if 'counts' in v else {kk: v.get(kk) for kk in ('hunted', 'nearest_m', 'relationship_to_host_and_figure', 'events', 'err')}) for k, v in det.items()}
    return fn


def plans(r, name, args):
    S = _S()
    if name != 'w223guard': return None
    which = os.environ.get('KCDMP_RIG_W223_GUARD', 'ff,village,crime,animals').split(',')
    steps = [('join', S.s_join(r))]
    table = {'ff': s_ff, 'village': s_village, 'crime': s_crime, 'animals': s_animals}
    for w in which:
        if w in table: steps.append(('guard-' + w, table[w](r)))
    return steps


CONTINUE_ON_FAIL = ('w223guard',)
