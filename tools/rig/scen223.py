# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-223: the bench scenes of "enemies notice the joiner, and fight him on both screens" (test tool, never shipped).

Every action goes through the game's console, Lua or our pipe (the agent); a player's own walking, sprinting, crouching, attacks and
blocks are keys (the maintainer allows the bench to drive the keyboard and mouse; send-only through tools/dev-input/game_input.py, the
copy's own window in front, every event logged). Nothing here reads input.

    w223approach   Part A / scene 1 (the approach): a player walks a fixed line from 80 m into a bandit camp at one pace; a reader on the
                   host's game writes, five times a second, every camp bandit's distance to him, its drawn weapon, its combat state and
                   its alert signs ([W223A] lines); the engine's own lines (barks, skirmish events) are read from the host's log.
                   KCDMP_RIG_W223_WHO=host|joiner|both|ahead, KCDMP_RIG_W223_PACE=walk|sprint|crouch, KCDMP_RIG_W223_RUNS=n
    w223camp       scene 2 (the two-player camp): both approach side by side (the host by his keys, the joiner walked beside him); from the
                   fight's start <KCDMP_RIG_W223_FIGHT_S> seconds of fighting, the players' keys by turns; who each bandit fights 5 s into
                   the fight (the host's own skirmish lines), the blows on the joiner judged by his own engine or by the host's guess,
                   the lock-on both ways. KCDMP_RIG_W223_RUNS=n, each from a fresh camp.
    w223swap       scene 4 (the swap): one bandit on the host; the joiner strikes it until it turns to him, the host strikes it until it
                   turns back, <KCDMP_RIG_W223_CYCLES> times; how long its copy on the joiner's screen takes to turn each time.
    w223table      Part A's table: KCDMP_RIG_W223_PLAN="who:pace:hour:runs[:host metres behind the start];..." -- every run from a freshly
                   loaded camp (the host's save loaded again; the joiner's rejoin waited out when he is in the run)
"""
import json, math, os, re, sys, threading, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rig


def _S():
    import scenarios
    return scenarios


def note(r, _ev, **kw):
    with open(os.path.join(r.dir, 'w223-events.jsonl'), 'a', encoding='utf-8') as f:
        f.write(json.dumps(dict(ev=_ev, t=round(time.time(), 3), **kw)) + '\n')


CAMP = os.environ.get('KCDMP_RIG_W223_CAMP', 'tkop_vrahbandit')
BEARING = float(os.environ.get('KCDMP_RIG_W223_BEARING', '120'))   # the approach line's bearing out from the camp's centre (degrees)
START_M = float(os.environ.get('KCDMP_RIG_W223_START', '80'))
HOLD_BACK_M = float(os.environ.get('KCDMP_RIG_W223_BACK', '120'))   # A.1: the host held this far behind the start, on the line

CAMP_Q = ('local out = {}; for _, e in ipairs(System.GetEntitiesByClass("NPC") or {}) do local n = e:GetName(); '
          'if e.soul and string.find(string.lower(n), "%s", 1, true) then local h = 0; pcall(function() h = e.soul:GetState("health") end); '
          'if h and h > 0 then local q = e:GetWorldPos(); out[#out + 1] = string.format("%%s,%%.1f,%%.1f,%%.1f", n, q.x, q.y, q.z) end end end; '
          'System.LogAlways("[W223C] " .. table.concat(out, " "))')

# the host's reader: one [W223A] line per sample -- the clock, the subject's place, its visibility and footstep stats, then per bandit
# i:distance:bits:view radius:x,y (bits: 1 drawn, 2 combat mode, 4 combat danger, 8 / 16 the brain's recognition levels I / II)
READER_SET = 'W223B = W223B or {}; W223B.names = {%s}; W223B.subj = "%s"'
READER_BITS = ('W223B.b = function(e) local so, b = e.soul, 0; local function t(v, f) local ok, r = pcall(f); if ok and r == true then b = b + v end end; '
               't(1, function() return e.human:IsWeaponDrawn() end); t(2, function() return so:IsInCombatMode() end); '
               't(4, function() return so:IsInCombatDanger() end); t(8, function() return so:HasScriptContext("switch_recognitionLevel_I") end); '
               't(16, function() return so:HasScriptContext("switch_recognitionLevel_II") end); return b end')
READER_FN = ('W223B.f = function() local s = W223B.subj == "player" and player or System.GetEntityByName(W223B.subj); if not s then return end; '
             'local sp = s:GetWorldPos(); local o = {}; for i, n in ipairs(W223B.names) do local e = System.GetEntityByName(n); '
             'if e and e.soul then local q = e:GetWorldPos(); local vr = -1; pcall(function() vr = e.soul:GetDerivedStat("vir") end); '
             'o[#o + 1] = string.format("%d:%.1f:%d:%.1f:%.1f,%.1f", i, math.sqrt((sp.x - q.x) ^ 2 + (sp.y - q.y) ^ 2), W223B.b(e), vr, q.x, q.y) end end; '
             'local vb, fs = -9, -9; pcall(function() vb = s.soul:GetDerivedStat("vib"); fs = s.soul:GetDerivedStat("fsm") end); '
             'local pp = player:GetWorldPos(); System.LogAlways(string.format("[W223A] %.2f %.1f %.1f %.2f %.3f %s P:%.1f,%.1f", System.GetCurrTime(), sp.x, sp.y, vb, fs, table.concat(o, " "), pp.x, pp.y)) end')
READER_GO = 'W223B.g = (W223B.g or 0) + 1; local g = W223B.g; local function k() if g ~= W223B.g then return end; pcall(W223B.f); Script.SetTimer(200, k) end; k()'
READER_STOP = 'W223B.g = (W223B.g or 0) + 1'

# A.2: the joiner walked along the line in his own game (a step every frame, on the ground)
PAIR_WALK = ('W223BW = (W223BW or 0) + 1; local g, d = W223BW, 0; local x0, y0, ux, uy, v, len = %.3f, %.3f, %.5f, %.5f, %.3f, %.2f; '
             'local function step() if g ~= W223BW or d >= len then return end; local ft = 0.016; '
             'pcall(function() ft = System.GetFrameTime() end); if type(ft) ~= "number" or ft <= 0 or ft > 0.1 then ft = 0.016 end; '
             'd = d + v * ft; local x, y = x0 + ux * d, y0 + uy * d; local z = System.GetTerrainElevation({x=x, y=y, z=0}); '
             'player:SetWorldPos({x=x, y=y, z=z + 0.05}); Script.SetTimer(1, step) end; step()')
PAIR_STOP = 'W223BW = (W223BW or 0) + 1'

AIM = ('local p = player:GetWorldPos(); local yaw = math.atan2(-(%f - p.x), %f - p.y); '
       'pcall(function() player.actor:PlayerSetViewAngles({x = -0.03, y = 0, z = yaw}) end)')
OWN_Q = ('local p = player:GetWorldPos(); local vb, fs, cn = -9, -9, -9; pcall(function() vb = player.soul:GetDerivedStat("vib"); '
         'fs = player.soul:GetDerivedStat("fsm"); cn = player.soul:GetDerivedStat("con") end); '
         'System.LogAlways(string.format("[W223O] %.2f %.2f %.2f %.2f %.3f %.3f %.2f", p.x, p.y, p.z, vb, fs, cn, (Calendar.GetWorldTime() % 86400) / 3600))')

SAMPLE = re.compile(r'\[W223A\] ([\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ?(.*)')
BARK = re.compile(r"Attempting to start new dialogue \(runtime id '\d+'\) with souls '([^']*)'")
SKIRMISH = re.compile(r'Skirmish event: (\w+) on (\S+) \(target ([^)]*)\)')


def camp(r):
    """(names, centre) of the camp's living bandits on the host; (None, None) when it reads nothing."""
    t = r.a.ask(CAMP_Q % CAMP, 'W223C', 8)
    rows = [x.split(',') for x in (t or '').split()]
    if not rows: return None, None
    names = [x[0] for x in rows]
    cx = sum(float(x[1]) for x in rows) / len(rows); cy = sum(float(x[2]) for x in rows) / len(rows)
    return names, (cx, cy)


def line_point(centre, dist, bearing=None):
    b = math.radians(BEARING if bearing is None else bearing)
    return centre[0] + dist * math.cos(b), centre[1] + dist * math.sin(b)


def walker(r, c, pace, target, stop, out, tag):
    """The subject's own movement: his copy's window in front, the forward key held (with the sprint key for a sprint; the crouch toggle
    first for a crouch), his view kept on the target. Send-only."""
    S = _S()
    sys.path.insert(0, os.path.join(rig.REPO, 'tools', 'dev-input'))
    import game_input
    g, held = None, []
    try:
        g = game_input.Game(tag)
        g.pids = {S.rec_of(c)['pid']}
        if not S.put_front(c): out['err'] = 'not in front'; return
        time.sleep(2.0)   # the game takes a moment to accept input after it regains the front
        c.lua(AIM % target)
        time.sleep(0.4)
        # his stance first, read from his own footstep stat (0.43 standing, 0.13 crouching): crouched for a crouch, standing otherwise
        for attempt in range(3):
            own = (c.ask(OWN_Q, 'W223O', 5) or '').split()
            crouched = len(own) >= 5 and float(own[4]) < 0.2
            out['crouched'] = crouched
            if crouched == (pace == 'crouch'): break
            g.tap('c', 90); out['crouch_key'] = out.get('crouch_key', 0) + 1
            time.sleep(1.5)
        if out.get('crouched') != (pace == 'crouch'): out['err'] = 'stance: wanted %s' % pace; return
        # a copy just brought to the front may miss a key's down edge (the joiner's first dry run stood for 150 s with the key "held"):
        # the forward key is pressed, the walk checked by his own position, and pressed again when he has not moved
        for attempt in range(6):
            p0 = c.pos()
            out['t_down'] = time.time()
            g.key('w', True); held.append('w')
            if pace == 'sprint': g.key('lshift', True); held.append('lshift')
            time.sleep(1.2)
            p1 = c.pos()
            if p0 and p1 and math.hypot(p1[0] - p0[0], p1[1] - p0[1]) > 0.6: break
            for k in reversed(held): g.key(k, False)
            held[:] = []
            out.pop('t_down', None)
            out['repress'] = attempt + 1
            time.sleep(0.8)
        if not held: out['err'] = 'the forward key never moved him'; return
        out['t_keys'] = time.time()
        while not stop.is_set():
            c.lua_nowait(AIM % target)
            if out.get('nudge'):
                # held by a tree or a rock: a step aside, right then left by turns, the forward key still down
                k = 'd' if out.get('nudges', 0) % 2 == 0 else 'a'
                out['nudge'] = False; out['nudges'] = out.get('nudges', 0) + 1
                g.key(k, True); held.append(k)
                time.sleep(0.7)
                g.key(k, False); held.remove(k)
            stop.wait(0.35)
    except game_input.NotFocused as ex:
        out['err'] = 'refused: %s' % ex
    except BaseException as ex:
        out['err'] = '%s: %s' % (type(ex).__name__, ex)
    finally:
        # the keys up again, only into the copy's own window (brought back in front when something else took it)
        try:
            if g and held:
                if not game_input.foreground_is_game(g.pids): S.put_front(c)
                for k in reversed(held): g.key(k, False)
        except BaseException as ex:
            out['release_err'] = '%s: %s' % (type(ex).__name__, ex)


# a bandit's barks that are a reaction to someone (its own chatter, yawns and songs are not): the game's own bark names
REACT_BARKS = ('NPC_REAGUJE', 'NPC_VIDI', 'BANDITA_', 'SVEDEK_', 'COMBAT_', 'SKIRMISH_', 'NPC_VARUJE', 'NPC_HROZI', 'CAMPER_', 'CAMP_TRESPASS')
WARN_BARKS = ('NPC_REAGUJE_NA_', 'NPC_VIDI_NEPRITELE', 'BANDITA_VOLA', 'NPC_VARUJE', 'NPC_HROZI', 'CAMPER_', 'CAMP_TRESPASS')
# the game's "recognising" bark (stealth/recognitionState_I_reaction: recognition level I of the PLAYER): a first sign, no alert
SIGN_BARKS = ('NPC_REAGUJE_NA_STAV_I',)


def bark_name(text, speaker):
    """The bark <speaker> makes in a dialogue-start line's soul list ("Ex: a - meta override: X; Nx: b - meta override: Y")."""
    for part in text.split(';'):
        if (': ' + speaker + ' ') in part and 'meta override: ' in part: return part.split('meta override: ')[-1].strip()
    return ''


def read_run(lines, names, subject_name):
    """The reader's samples and the engine's own lines of one approach, as rows. Each engine line takes the clock of the sample before it."""
    samples, events = [], []
    t_last = None
    for l in lines:
        m = SAMPLE.search(l)
        if m:
            t_last = float(m.group(1))
            row = {'t': t_last, 'x': float(m.group(2)), 'y': float(m.group(3)), 'vib': float(m.group(4)), 'fsm': float(m.group(5)), 'b': {}}
            for part in m.group(6).split():
                if part.startswith('P:'):
                    hx, hy = part[2:].split(',')
                    row['hx'], row['hy'] = float(hx), float(hy)
                    continue
                f = part.split(':')
                if len(f) < 5: continue
                bx, by = f[4].split(',')
                row['b'][int(f[0])] = {'d': float(f[1]), 'bits': int(f[2]), 'vir': float(f[3]), 'x': float(bx), 'y': float(by)}
            samples.append(row)
            continue
        if t_last is None: continue
        if 'WO223-MODEL ' in l or 'WO223-GATE ' in l or 'WO223-CAMP ' in l or 'WO223-NOTICE ' in l or 'WO223-FIG ' in l:
            x = l.strip()
            events.append({'t': t_last, 'kind': 'model' if 'WO223-MODEL ' in l else 'fig' if 'WO223-FIG ' in l else 'gate', 'text': x[x.find('WO223-'):][:300]})
            continue
        m = BARK.search(l)
        if m and any(n in m.group(1) for n in names):
            events.append({'t': t_last, 'kind': 'bark', 'text': m.group(1)[:160]})
            continue
        m = SKIRMISH.search(l)
        if m and (m.group(2) in names or m.group(2) == subject_name):
            events.append({'t': t_last, 'kind': 'skirmish', 'what': m.group(1), 'on': m.group(2), 'target': m.group(3)})
            continue
        if 'tried to create information' in l and any(n in l for n in names):
            events.append({'t': t_last, 'kind': 'info', 'text': l.strip()[:200]})
    return samples, events


def summarise(samples, events, names, subject_name):
    """Per bandit: the first time and distance of each stage. sign = the game's "recognising" bark (a player only; no alert);
    notice = the alert (a recognition level, the alerted view radius, combat danger, or a reaction bark other than the recognising
    one); warn = its first warning bark; draw; charge = combat mode and closing."""
    if not samples: return {}
    t0 = walk_start(samples)
    out = {}
    base_vir = {i: s['vir'] for i, s in samples[0]['b'].items()}
    for i, n in enumerate(names, 1):
        st = {}
        prev = None
        # a bandit that carries its weapon drawn before the walk (a guard on its round) does not "draw" for him
        b0 = samples[0]['b'].get(i)
        predrawn = bool(b0 and b0['bits'] & 1)
        for s in samples:
            b = s['b'].get(i)
            if not b: continue
            def mark(k, extra=None):
                if k not in st:
                    st[k] = dict(t=round(s['t'] - t0, 2), d=b['d'], **(extra or {}))
                    if 'hx' in s: st[k]['dh'] = round(math.hypot(s['hx'] - b['x'], s['hy'] - b['y']), 1)
            if b['bits'] & 24: mark('notice', {'how': 'recognition %d' % ((b['bits'] & 24) >> 3)})
            # 28.1 = the engine's alerted view radius (awake 18.7 .. 21.4; a sleeper reads 0 and wakes to 21: not a notice)
            if b['vir'] >= 25.0 and base_vir.get(i, 0) < 25.0: mark('notice', {'how': 'view radius %.1f' % b['vir']})
            if b['bits'] & 4: mark('notice', {'how': 'combat danger'})
            if b['bits'] & 1 and not predrawn: mark('draw')
            if b['bits'] & 2:
                mark('combat')
                if prev and prev['d'] - b['d'] > 0.25 and math.hypot(b['x'] - prev['x'], b['y'] - prev['y']) > 0.25: mark('charge')
            prev = b
        for e in events:
            if e['kind'] == 'bark':
                bk = bark_name(e['text'], n)
                if not bk.startswith(REACT_BARKS): continue
                s = min(samples, key=lambda q: abs(q['t'] - e['t']))
                b = s['b'].get(i)
                if not b: continue
                row = {'t': round(e['t'] - t0, 2), 'd': b['d'], 'bark': bk[:60]}
                if bk.startswith(SIGN_BARKS):
                    if 'sign' not in st: st['sign'] = row
                    continue
                if 'bark1' not in st: st['bark1'] = row
                if bk.startswith(WARN_BARKS) and 'warn' not in st: st['warn'] = row
                if 'notice' not in st or row['t'] < st['notice']['t']: st['notice'] = dict(row, how='bark')
            if e['kind'] == 'skirmish' and e['on'] == n:
                s = min(samples, key=lambda q: abs(q['t'] - e['t']))
                b = s['b'].get(i)
                if not b: continue
                if e['target'] not in ('-', '') and 'target' not in st: st['target'] = {'t': round(e['t'] - t0, 2), 'd': b['d'], 'who': e['target'], 'what': e['what']}
                if e['what'] == 'Attack' and 'attack' not in st: st['attack'] = {'t': round(e['t'] - t0, 2), 'd': b['d'], 'who': e['target']}
        # charge = the bandit comes for him: it closes on him in combat mode, or its first attack (he may have walked into its reach)
        if 'attack' in st and ('charge' not in st or st['attack']['t'] < st['charge']['t']): st['charge'] = dict(st['attack'], how='attack')
        out[n] = st
    return out


def walk_start(samples):
    """The clock of the last sample before the subject first moved (the stages' zero); the first sample's when he never moved."""
    # a subject who began further out than the start (a joiner the session left beside the host walks the whole line in): the clock's
    # zero is the moment he passes the start distance from the camp's centre -- the same point of the same line for every run
    bs = [b for b in samples[0]['b'].values() if 'x' in b]
    if bs:
        cx, cy = sum(b['x'] for b in bs) / len(bs), sum(b['y'] for b in bs) / len(bs)
        if math.hypot(samples[0]['x'] - cx, samples[0]['y'] - cy) > START_M + 5.0:
            for s in samples:
                if math.hypot(s['x'] - cx, s['y'] - cy) <= START_M: return s['t']
    x0, y0 = samples[0]['x'], samples[0]['y']
    for i, s in enumerate(samples):
        if math.hypot(s['x'] - x0, s['y'] - y0) > 0.5: return samples[max(0, i - 1)]['t']
    return samples[0]['t']


def pace_of(samples):
    """The subject's speed while he moved: the median over one-second steps of the samples that moved at all."""
    v = []
    for i in range(5, len(samples), 5):
        dt = samples[i]['t'] - samples[i - 5]['t']
        d = math.hypot(samples[i]['x'] - samples[i - 5]['x'], samples[i]['y'] - samples[i - 5]['y'])
        if dt > 0.3 and d / dt > 0.3: v.append(d / dt)
    v.sort()
    return round(v[len(v) // 2], 2) if v else 0.0


def first_of(summary, key):
    best = None
    for n, st in summary.items():
        if key in st and (best is None or st[key]['t'] < best[1]['t']): best = (n, st[key])
    return best


def arm_walkers(a, b, walkers):
    """The walkers' weapons: drawn (the default: the bench host's save carries his drawn) or sheathed (KCDMP_RIG_W223_ARMS) -- the same
    for both players. A walker whose draw is refused has his weapon set put right the mod's own way first (WO-174's repair: the bench
    joiner; a weapon created into a joiner's inventory is gone before his draw). Returns (arms, what each game and the figure read)."""
    arms = os.environ.get('KCDMP_RIG_W223_ARMS', 'drawn')
    ARM = ('pcall(function() if not player.human:IsWeaponDrawn() then player.human:DrawWeapon() end end)' if arms == 'drawn'
           else 'pcall(function() if player.human:IsWeaponDrawn() then player.human:HolsterWeapon() end end)')
    DRAWN_Q = 'local d = "?"; pcall(function() d = tostring(player.human:IsWeaponDrawn()) end); System.LogAlways("[W223W] " .. d)'
    for c in walkers: c.lua(ARM)
    time.sleep(3.0)
    gave = {}
    if arms == 'drawn':
        for c in walkers:
            if c.ask(DRAWN_Q, 'W223W', 5) == 'true': continue
            mk = c.mark()
            c.lua('if KCD2MP_W174Repair then KCD2MP_W174Repair("wo223-bench") end')
            hit = c.wait('k', mk['k'], r'WO174-REPAIR step=result result=(.*?) draw_ok=(yes|no)|WO174-SAY Nothing was wrong', 25)
            gave[c.id] = hit.group(0)[:120] if hit else 'no answer'
            time.sleep(4.0); c.lua(ARM); time.sleep(3.5)
            if c.ask(DRAWN_Q, 'W223W', 5) != 'true': c.lua(ARM); time.sleep(3.0)
    armed = {c.id: c.ask(DRAWN_Q, 'W223W', 5) for c in (a, b)}
    if gave: armed['given'] = gave
    armed['figure'] = a.ask('local d = "?"; for k, g in pairs(KCD2MP.ghosts or {}) do pcall(function() d = tostring(g.entity.human:IsWeaponDrawn()) end) end; '
                            'System.LogAlways("[W223W] " .. d)', 'W223W', 5)
    return arms, armed


def set_stance(r, c, crouch, tag):
    """<c>'s stance by his own crouch key (his window in front for it), read back from his footstep stat (0.43 standing, 0.13
    crouching). Returns what was done; send-only."""
    S = _S()
    sys.path.insert(0, os.path.join(rig.REPO, 'tools', 'dev-input'))
    import game_input
    out = {}
    try:
        g = game_input.Game(tag)
        g.pids = {S.rec_of(c)['pid']}
        for attempt in range(3):
            own = (c.ask(OWN_Q, 'W223O', 5) or '').split()
            out['crouched'] = len(own) >= 5 and float(own[4]) < 0.2
            if out['crouched'] == crouch: break
            if not S.put_front(c): out['err'] = 'not in front'; break
            time.sleep(2.0)
            g.tap('c', 90); out['crouch_key'] = out.get('crouch_key', 0) + 1
            time.sleep(1.5)
    except BaseException as ex:
        out['err'] = '%s: %s' % (type(ex).__name__, ex)
    return out


def approach_once(r, who, pace, tag, max_s=150.0, back=None):
    """One approach. who: 'host' (the host's own player walks), 'joiner' (his figure; the host held back on the line), 'both' (side by
    side: the joiner walks by keys, the host is moved along at his side), 'ahead' (the joiner 10 m ahead of the host)."""
    S = _S()
    a, b = r.a, r.b
    names, centre = camp(r)
    if not names: return {'err': 'no camp'}
    start = line_point(centre, START_M)
    fig = a.ask('local n = "none"; for k, g in pairs(KCD2MP.ghosts or {}) do if g.entity then n = g.entity:GetName() end end; System.LogAlways("[W223G] " .. n)', 'W223G', 6)
    subj_copy = b if who == 'joiner' else a
    subject_name = 'Dude' if who == 'host' else fig
    for c in (a, b): c.keep_up()
    # the places: the walker on the start; the other player out of the camp's sight on the line
    if who == 'host':
        a.teleport(*start); b.teleport(*line_point(centre, START_M + HOLD_BACK_M))
    elif who == 'joiner':
        b.teleport(*start)
        at = os.environ.get('KCDMP_RIG_W223_HOSTAT')   # "bearing,metres" from the camp's centre: the host somewhere off the line
        if at: a.teleport(*line_point(centre, float(at.split(',')[1]), float(at.split(',')[0])))
        else: a.teleport(*line_point(centre, START_M + (HOLD_BACK_M if back is None else back)))
    else:
        # A.2: the host on the line; the joiner 1.5 m to his side, or 10 m ahead of him (nearer the camp)
        side = (-math.sin(math.radians(BEARING)) * 1.5, math.cos(math.radians(BEARING)) * 1.5)
        a.teleport(*start)
        jp = line_point(centre, START_M - (10.0 if who == 'ahead' else 0.0))
        jstart = (jp[0] + side[0], jp[1] + side[1])
        b.teleport(*jstart)
    time.sleep(6.0)
    if who == 'joiner':
        # his first console call after a rejoin or a clock jump can be lost: the place is read back and written again
        for _try in range(4):
            jp0 = b.pos()
            if jp0 and math.hypot(jp0[0] - start[0], jp0[1] - start[1]) < 4.0: break
            b.teleport(*start); time.sleep(5.0)
    walkers = (a,) if who == 'host' else (b,) if who == 'joiner' else (a, b)
    arms, armed = arm_walkers(a, b, walkers)
    own = {c.id: c.ask(OWN_Q, 'W223O', 5) for c in (a, b)}
    a.lua(READER_SET % (', '.join('"%s"' % n for n in names), 'player' if who == 'host' else fig))
    a.lua(READER_BITS); a.lua(READER_FN)
    # the host-model calibration log (the mod's recognition rule kept for the host's own player) for the runs he walks himself
    if who != 'joiner': a.lua('if W223 and W223.P then W223.P.hostModel = true; W223.hostMeter = {}; W223.hostAt = nil end')
    if who != 'host': a.lua('if W223 and W223.P then W223.P.gateLog = true end')
    off = a.size(a.klog)
    noff = a.size(a.nlog)
    # the solo fight (KCDMP_RIG_W223_SWING_S): once he is fought, his walk ends and he swings for that long, kept on his feet
    swing_s = float(os.environ.get('KCDMP_RIG_W223_SWING_S', '0') or 0) if who == 'joiner' else 0.0
    sw = None
    a.lua(READER_GO)
    out = {'who': who, 'pace': pace, 'names': names, 'centre': centre, 'start': start, 'own': own, 'figure': fig, 'arms': arms, 'armed': armed}
    con_shift = os.environ.get('KCDMP_RIG_W223_CON_SHIFT', '').strip()
    a.lua('if W223_PS then KCD2MP_W195ParseStealth = W223_PS; W223_PS = nil end')
    vis_shift = os.environ.get('KCDMP_RIG_W223_VIS_SHIFT', '').strip()   # the same for his visibility (his gear reads darker than the host's)
    if (con_shift or vis_shift) and who != 'host':
        a.lua('W223_PS = KCD2MP_W195ParseStealth; KCD2MP_W195ParseStealth = function(t) local c = W223_PS(t); '
              'if c and c.con and c.con > -99 then c.con = c.con + (%.3f) end; if c and c.vis and c.vis > -99 then c.vis = c.vis + (%.3f) end; '
              'return c end' % (float(con_shift or 0), float(vis_shift or 0)))
        if con_shift: out['con_shift'] = float(con_shift)
        if vis_shift: out['vis_shift'] = float(vis_shift)
    pset = [kv.strip() for kv in os.environ.get('KCDMP_RIG_W223_P', '').split(';') if '=' in kv]
    if pset:
        a.lua('if W223 and W223.P then %s end' % ' '.join('W223.P.%s' % kv for kv in pset))
        out['p'] = pset
    if who in ('both', 'ahead'): out['joiner_stance'] = set_stance(r, b, pace == 'crouch', tag)
    stop = threading.Event()
    wk = {}
    th = threading.Thread(target=walker, args=(r, subj_copy, pace, centre, stop, wk, tag), daemon=True)
    th.start()
    mover = None
    if who in ('both', 'ahead'):
        # the keys are the host's (one window is in front); the joiner walks the same line in his own game: a step every frame at the
        # host's pace from the moment the host's keys go down, held on the ground, stopped 4 m short of the camp's centre
        def follow():
            while not stop.is_set() and not wk.get('t_down') and not wk.get('err'): stop.wait(0.05)
            if stop.is_set() or wk.get('err'): return
            v = float(os.environ.get('KCDMP_RIG_W223_PAIR_MPS', {'walk': '2.9', 'sprint': '4.4', 'crouch': '1.8'}.get(pace, '3.05')))
            ux, uy = -math.cos(math.radians(BEARING)), -math.sin(math.radians(BEARING))
            length = max(0.0, math.hypot(jstart[0] - centre[0], jstart[1] - centre[1]) - 4.0)
            b.lua_nowait(PAIR_WALK % (jstart[0], jstart[1], ux, uy, v, length))
        mover = threading.Thread(target=follow, daemon=True); mover.start()
    t0 = time.time(); fight_at = None; why = 'timeout'; moved_at = None; moved_pos = (0.0, 0.0); reached_at = None
    while time.time() - t0 < max_s:
        time.sleep(0.5)
        if wk.get('err'): why = 'walker: ' + wk['err']; break
        last = None
        for l in reversed(a.lines(a.klog, max(off, a.size(a.klog) - 6000))):
            m = SAMPLE.search(l)
            if m: last = m; break
        if not last: continue
        dmin, fighting, alert = 999.0, False, []
        for part in last.group(6).split():
            f = part.split(':')
            if len(f) >= 3 and not part.startswith('P:'):
                dmin = min(dmin, float(f[1])); fighting = fighting or (int(f[2]) & 2) != 0
                # (alert by the engine's own word: its raised view radius, combat mode or combat danger)
                if (len(f) >= 4 and float(f[3]) >= 25.0) or (int(f[2]) & 6) != 0: alert.append(int(f[0]))
        if fighting and fight_at is None:
            fight_at = time.time()
            if swing_s > 0:
                stop.set(); th.join(timeout=15)
                b.keep_up()
                sw = {'swings': {'sent': 0, 'refused': 0, 'err': None}, 'stop': threading.Event()}
                sw['th'] = threading.Thread(target=_S().joiner_swinger, args=(r, sw['swings'], sw['stop'], 'wo223-solo'), daemon=True)
                sw['th'].start()
        # the frame: at the first alert (the pictures take two seconds: by then it draws and comes at him), not at the first blow
        if alert and 'frame' not in out and os.environ.get('KCDMP_RIG_W223_FRAME') and who != 'host':
            if True:
                try:
                    import scen217
                    out['frame'] = {'asked': True}
                    b.lua('System.SetCVar("wh_ai_PostLoadErrorDebugDraw", 0)')
                    bn = names[alert[0] - 1]
                    if who == 'joiner':   # the host stands 120 m back: his camera beside the bandit for the picture
                        t = a.ask(scen217.FRAME_SPOT % (bn, 0.0), 'W217F', 6)
                        if t and not t.startswith('none'):
                            x, y, z, yaw, _d, _n = t.split()
                            a.lua(scen217.PLACE_VIEW % (float(x), float(y), float(z) + 0.05, -0.12, float(yaw)))
                    out['frame'] = scen217.frame_pair(r, 'approach-%s' % tag, bn, move=False)
                except BaseException as ex:
                    out['frame'] = {'error': '%s: %s' % (type(ex).__name__, ex)}
        if fight_at and time.time() - fight_at > (swing_s if swing_s > 0 else float(os.environ.get('KCDMP_RIG_W223_AFTER_S', '6'))): why = 'fight'; break
        cd = math.hypot(float(last.group(2)) - centre[0], float(last.group(3)) - centre[1])
        if cd < 3.0:
            # he is in the camp: his keys up, the reader on for a while (a slow reaction comes after he arrived)
            if reached_at is None: reached_at = time.time(); stop.set()
            if fight_at is None and time.time() - reached_at > float(os.environ.get('KCDMP_RIG_W223_ARRIVED_S', '12')): why = 'reached the camp'; break
            continue
        # a walker held by a tree or a rock for 8 s after he walked: the run ends where he stands
        pos = (float(last.group(2)), float(last.group(3)))
        if moved_at is None or math.hypot(pos[0] - moved_pos[0], pos[1] - moved_pos[1]) > 0.5:
            if moved_at is not None and wk.get('nudges'): wk['nudged_free'] = wk.get('nudged_free', 0) + 1; wk['nudges'] = 0
            moved_at, moved_pos = time.time(), pos
        elif wk.get('t_keys') and not fight_at and time.time() - max(moved_at, wk['t_keys']) > 2.5:
            if wk.get('nudges', 0) >= 6 or time.time() - max(moved_at, wk['t_keys']) > 14.0: why = 'stuck'; break
            if not wk.get('nudge') and time.time() - wk.get('nudge_at', 0) > 2.0: wk['nudge'] = True; wk['nudge_at'] = time.time()
    if sw:
        sw['stop'].set(); sw['th'].join(timeout=10); b.stop_keep()
        hits = [l for l in a.lines(a.nlog, noff) if 'W222-BLOW by=joiner ' in l and 'verdict=hit' in l]
        how = {}
        for l in hits:
            m = re.search(r' react=([a-z-]+)', l)
            how[m.group(1) if m else '?'] = how.get(m.group(1) if m else '?', 0) + 1
        out['solo'] = {'swing_s': swing_s, 'swings': sw['swings'], 'clean_hits': len(hits), 'in_combat_mode': sum(1 for l in hits if ' cm=1 ' in l),
                       'reacted': sum(1 for l in hits if ' next=hit ' in l), 'react': how}
    stop.set(); th.join(timeout=15)
    if mover: mover.join(timeout=5); b.lua(PAIR_STOP)
    time.sleep(1.0)
    a.lua(READER_STOP)
    a.lua('if W223 and W223.P then W223.P.hostModel = false; W223.P.gateLog = false end')
    lines = a.lines(a.klog, off)
    samples, events = read_run(lines, names, subject_name)
    summ = summarise(samples, events, names, subject_name)
    out.update({'why': why, 'secs': round(time.time() - t0, 1), 'walker': wk, 'samples': len(samples), 'summary': summ,
                'first': {k: first_of(summ, k) for k in ('sign', 'notice', 'warn', 'draw', 'combat', 'charge', 'target', 'attack')},
                'speed_mps': pace_of(samples)})
    with open(os.path.join(r.dir, 'approach-%s.json' % tag), 'w', encoding='utf-8') as f:
        json.dump({'out': out, 'samples': samples, 'events': events}, f)
    note(r, 'approach', tag=tag, **{k: v for k, v in out.items() if k not in ('summary',)})
    rig.say('  approach', tag, why, out['secs'], 'first:', json.dumps(out['first'])[:600])
    return out


def reset_camp(r, names):
    """The fight ended and both players taken off the line (the camp left to settle)."""
    S = _S()
    for i, n in enumerate(names): r.a.lua(S.STOP % (n, 2230 + i))
    for c in (r.a, r.b): c.stop_keep()


def s_approach(r, who, pace, runs=1, tag='a'):
    def fn():
        rows = []
        for k in range(runs):
            o = approach_once(r, who, pace, '%s-%s-%s-%d' % (tag, who, pace, k + 1))
            rows.append({kk: o.get(kk) for kk in ('why', 'secs', 'first', 'speed_mps', 'err')})
            if o.get('names'): reset_camp(r, o['names'])
        ok = all(not x.get('err') for x in rows)
        return ok, ('ok' if ok else 'error'), rows
    return fn


def join_resumed(a, off, secs):
    """True once the host's world has resumed, the joiner ready, for the NEWEST join in the host's log since <off> (a join the reload
    cut resumes with another reason, and a new one follows)."""
    t0 = time.time()
    while time.time() - t0 < secs:
        lines = a.lines(a.klog, off)
        ids = [m.group(1) for l in lines for m in [re.search(r'MP-JOIN pause join=(\S+)', l)] if m]
        if ids and any(('MP-JOIN resume join=%s reason=ready' % ids[-1]) in l for l in lines): return True
        time.sleep(1.0)
    return False


def fresh_camp(r, who, hour):
    """A clean camp for one approach: the host's save loaded again (the camp as saved, nobody alerted), the session's clock at <hour>;
    for a run with the joiner in it, his own rejoin waited out."""
    a, b = r.a, r.b
    had = a.ask('local n = 0; for k, g in pairs(KCD2MP.ghosts or {}) do n = n + 1 end; System.LogAlways("[W223H] " .. n)', 'W223H', 6)
    if had not in (None, '0'):
        # a joiner still in his fight when the host loads waits for it to end before he joins again (the mod's load-quiet rule), and
        # gives up after three minutes: the last run's fight is ended on the host and he is taken 250 m away first
        S = _S()
        nm, _c = camp(r)
        for i, n in enumerate(nm or []): a.lua(S.STOP % (n, 2230 + i))
        hp = a.pos()
        if hp and _c:
            ux, uy = hp[0] - _c[0], hp[1] - _c[1]; un = math.hypot(ux, uy) or 1.0
            far = 250.0
            b.teleport(_c[0] + ux / un * far, _c[1] + uy / un * far)
            if un < 150.0: a.teleport(_c[0] + ux / un * (far - 4.0), _c[1] + uy / un * (far - 4.0))
        # ... and the fight is ended on his own screen too (his copies), then his game is asked until it says no fight is running
        FLAGS = ('local function f(e) if not (e and e.soul) then return "-" end local d, m = false, false; pcall(function() d = e.soul:IsInCombatDanger() end); '
                 'pcall(function() m = e.soul:IsInCombatMode() end); return (d and "D" or "d") .. (m and "M" or "m") end; '
                 'System.LogAlways("[W223F] me=" .. f(player) .. " fig0=" .. f(System.GetEntityByName("kcd2mp_0")) .. " quiet=" .. tostring(KCD2MP_W177LoadReason and KCD2MP_W177LoadReason()))')
        # ... and while they calm down the host's enemies are no longer set on his figure (shipped runs only: a twin has it off already)
        shipped = not any(k in os.environ.get('KCDMP_RIG_TEST_ENV', '') for k in ('W223_OFF=all', 'w223_perceive'))   # (a one-switch twin keeps the gate)
        if shipped: a.lua('KCD2MP_W223Set("w223_perceive", "off")')
        t_c = time.time(); seen = []
        while time.time() - t_c < float(os.environ.get('KCDMP_RIG_W223_CALM_S', '150')):
            for i, n in enumerate(nm or []): b.lua(S.STOP % (n, 2260 + i))
            time.sleep(4)
            fl = b.ask(FLAGS, 'W223F', 6) or ''
            if fl not in seen[-1:]: seen.append(fl)
            if fl.endswith('quiet=ok'): break
            # his own "in danger" flag lingering with no fight left (no combat mode on him or his host figure): the bench waives the
            # mod's load-quiet answer on his copy for this rejoin (he would wait three minutes and give up -- a session lost)
            if time.time() - t_c > 40 and fl.startswith('me=Dm') and 'quiet=fight' in fl:
                b.lua('if not W223_LR then W223_LR = KCD2MP_W177LoadReason; KCD2MP_W177LoadReason = function() return "ok" end end')
                seen.append('waived'); break
        rig.say('  calm before the reload:', ' -> '.join(seen)[:200], 'after %.0f s' % (time.time() - t_c))
        if shipped: a.lua('KCD2MP_W223Set("w223_perceive", "on")')
    offa, offb = a.size(a.klog), b.size(b.klog)
    try: a.cmd('wh_sys_LoadGame 4 save440')
    except Exception as ex: rig.note('w223-load', err=str(ex)[:80])   # the console answers when the load is over: a timeout is not a failure
    if not a.wait('k', offa, r'^Gameplay started', 240): return False, 'the host never said Gameplay started'
    # a joiner in the session joins the reloaded world again: the host's world is paused and his input held until the joiner is ready
    # (the first table's host stood for it: "the forward key never moved him") -- waited out for every run, whoever walks
    back = True
    if had not in (None, '0'):
        # (tw1's second run: the pause began 50 s after "Gameplay started" -- a 40 s wait for it let the walk start into the pause)
        if not a.wait('k', offa, r'MP-JOIN pause join=', 240): return False, 'the joiner never asked for the reloaded world'
        back = join_resumed(a, offa, 360)
        if back and who != 'host': back = bool(b.wait('k', offb, r'WO136-HOLD off why=gameplay.settle', 120))
        b.lua('if W223_LR then KCD2MP_W177LoadReason = W223_LR; W223_LR = nil end')
    elif who != 'host': back = False
    time.sleep(8)
    a.lua('local now = Calendar.GetWorldTime(); local d = math.floor(now / 86400) * 86400 + %d; if d < now then d = d + 86400 end; '
          'if math.abs(((now %% 86400) / 3600) - %f) > 0.75 then Calendar.SetWorldTime(d) end' % (int(hour * 3600), hour))
    time.sleep(float(os.environ.get('KCDMP_RIG_W223_SETTLE_S', '30')))
    tod = a.ask('local n = 0; for k, g in pairs(KCD2MP.ghosts or {}) do n = n + 1 end; System.LogAlways(string.format("[W223T] %.2f %d", (Calendar.GetWorldTime() % 86400) / 3600, n))', 'W223T', 6)
    if not back: return False, 'the joiner never came back after the reload (%s)' % tod
    if who != 'host' and (not tod or tod.split()[-1] == '0'): return False, 'no figure on the host after the reload (%s)' % tod
    return True, tod


def plan_tags(plan):
    """[(tag, who, pace, hour, back)] of a plan ("who:pace:hour:runs[:back]" joined by ';'), in its order."""
    out = []
    for item in plan.split(';'):
        f = item.split(':')
        who, pace, hour, runs = f[0], f[1], float(f[2]), int(f[3])
        back = float(f[4]) if len(f) > 4 else None
        for k in range(runs):
            out.append(('%s-%s-%02d-%s%d' % (who, pace, int(hour), ('b%d-' % back) if back is not None else '', k + 1), who, pace, hour, back))
    return out


def void_reason(row):
    """Why a run measured nothing (None = it counts): the walker never walked or stood wrong, or a tree held him before anyone noticed
    him. A joiner nobody noticed who reached the camp is a result, not a void run."""
    w = (row.get('walker') or {}).get('err')
    if row.get('err'): return row['err']
    if w: return 'walker: ' + w
    if row.get('why') in ('stuck', 'timeout') and not (row.get('first') or {}).get('notice'): return row['why']
    return None


def load_done(path):
    if path and os.path.exists(path):
        with open(path, encoding='utf-8') as f: return json.load(f)
    return {'rows': {}, 'tries': {}}


def owed(plan, done, max_tries=3):
    return [t for t in plan_tags(plan) if t[0] not in done['rows'] and done['tries'].get(t[0], 0) < max_tries]


def s_table(r, plan):
    """Part A's table: every item of <plan>, each run from a fresh camp. With KCDMP_RIG_W223_DONE=<file> the finished runs are kept
    there and a later session does only what is owed (a void run is owed again, three tries)."""
    def fn():
        path = os.environ.get('KCDMP_RIG_W223_DONE')
        done = load_done(path)
        rows, fails, last = [], 0, None
        for tag, who, pace, hour, back in owed(plan, done):
            if tag in done['rows']: continue   # a tag the plan names twice, finished earlier in this session
            # a camp the last run never disturbed (nobody noticed anyone) is the same camp: no reload, the same hour
            if last and last.get('undisturbed') and last.get('hour') == hour and not void_reason(last): ok, info = True, 'not reloaded (undisturbed)'
            else: ok, info = fresh_camp(r, who, hour)
            if not ok:
                rows.append({'tag': tag, 'err': info}); rig.say('  table', tag, 'NOT RUN:', info)
                fails += 1; last = None
                if fails >= 2: rig.say('  table: two resets failed in a row -- this session is over (the pair is started again)'); break
                continue
            fails = 0
            o = approach_once(r, who, pace, tag, back=back)
            row = {'tag': tag, 'who': who, 'pace': pace, 'hour': hour, 'back': back, 'clock': info, 'session': os.path.basename(os.path.dirname(r.dir))}
            row.update({kk: o.get(kk) for kk in ('why', 'secs', 'first', 'speed_mps', 'err', 'own', 'walker', 'summary', 'arms', 'armed')})
            row['undisturbed'] = not any((o.get('first') or {}).get(k2) for k2 in ('notice', 'warn', 'draw', 'combat', 'target'))
            row['void'] = void_reason(row)
            rows.append(row); last = row
            for c in (r.a, r.b): c.stop_keep()
            done['tries'][tag] = done['tries'].get(tag, 0) + 1
            if not row['void']: done['rows'][tag] = row
            else: rig.say('  table', tag, 'VOID (%s) -- owed again' % row['void'])
            if path:
                with open(path, 'w', encoding='utf-8') as fh: json.dump(done, fh, indent=1)
            with open(os.path.join(r.dir, 'table.json'), 'w', encoding='utf-8') as fh: json.dump(rows, fh, indent=1)
        left = owed(plan, done)
        return (not left), ('ok' if not left else 'runs-owed'), {'ran': len(rows), 'done': len(done['rows']), 'owed': [t[0] for t in left][:12]}
    return fn


# ---------------------------------------------------------------- scenes 2 and 4, the lock-on

LOCK_RX = r"Player: Opponent change from '([^']*)' to '([^']*)'"
NEAR_Q = ('local best, bd; local p = player:GetWorldPos(); for _, n in ipairs({%s}) do local e = System.GetEntityByName(n); if e then '
          'local q = e:GetWorldPos(); local d = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; if not bd or d < bd then bd = d; best = n end end end; '
          'System.LogAlways("[W223K] " .. tostring(best) .. " " .. string.format("%%.1f", math.sqrt(bd or 0)))')


def targets_at(events, names, t):
    """Whom each bandit fights at reader clock <t>: its newest TargetChanged before it ('-' = nobody)."""
    out = {}
    for e in events:
        if e['kind'] == 'skirmish' and e['what'] == 'TargetChanged' and e['on'] in names and e['t'] <= t: out[e['on']] = e['target']
    return out


def lock_try(r, c, wanted, tag, near=False):
    """<c>'s player turned to the nearest of <wanted> (enemies that fight his partner) and the lock key pressed (twice when the first
    press let an old lock go). Returns (the name the game's own line says he is locked on to | None, was it one of <wanted>)."""
    S = _S()
    sys.path.insert(0, os.path.join(rig.REPO, 'tools', 'dev-input'))
    import game_input
    if not wanted: return None, False
    t = c.ask(NEAR_Q % ', '.join('"%s"' % n for n in wanted), 'W223K', 5)
    npc = (t or 'nil').split()[0]
    if npc == 'nil': return None, False
    g = game_input.Game(tag)
    g.pids = {S.rec_of(c)['pid']}
    if near:
        import scen222
        scen222.place_near(c, npc, 2.6, 0.0)
    if not S.put_front(c): rig.note('w223-lock', copy=c.id, npc=npc, err='not in front'); return None, False
    time.sleep(1.5)
    got, seen, err = None, [], None
    off0 = c.size(c.klog)
    try:
        for _ in range(4):
            c.lua_nowait(S.FACE_TO % npc); time.sleep(0.6)
            off = c.size(c.klog)
            g.tap('tab', 90)
            m = c.wait('k', off, LOCK_RX, 2.5)
            if m: seen.append('%s->%s' % (m.group(1), m.group(2)))
            if m and m.group(2) not in ('<none>', ''): got = m.group(2); break
    except game_input.NotFocused as ex:
        err = 'refused: %s' % ex
    finally:
        # the lock let go again (the swingers take their own)
        try:
            if got: g.tap('tab', 90)
        except Exception: pass
    rig.note('w223-lock', copy=c.id, npc=npc, wanted=wanted, got=got, seen=seen, err=err,
             lines=sum(1 for l in c.lines(c.klog, off0) if 'Opponent change' in l))
    return got, (got in wanted)


def s_lock(r, tries=5):
    """C.3: the lock-on both ways. Two bandits of a camp are set on the joiner's figure, the host 12 m off and in no fight: the host,
    put beside one, turns to it and presses the lock key (<tries> times; the game's own line says whom he is locked on to). Then one
    bandit is set on the host's player and the joiner does the same with its copy."""
    def fn():
        S = _S()
        import scen222
        a, b = r.a, r.b
        t = a.ask(S.FIND_BANDITS3, 'W207K', 6)
        if not t or t.startswith('none'): return False, 'no-bandits', t
        names, x, y, z = t.split()
        bandits = names.split(',')[:3]
        x, y = float(x), float(y)
        b.teleport(x + 3.0, y); a.teleport(x + 14.0, y + 3.0)
        time.sleep(4)
        for c in (a, b): c.keep_up()
        for n in bandits: a.lua(scen222.KEEP_NPC.replace('W222NK', 'W223LK_' + n) % (n, 35.0, 90.0, ''))
        ma, mb = a.mark(), b.mark()
        for n in bandits[:2]: a.lua(S.ATTACK % (n, S.FIGURE))
        time.sleep(8)
        host = [lock_try(r, a, bandits[:2], 'wo223-lock-host', near=True) for _ in range(tries)]
        # the host's player back off, one bandit set on him: the joiner locks on to its copy (it fights the host's figure on his screen)
        if len(bandits) > 2: third = bandits[2]
        else: third = bandits[1]
        a.lua(S.ATTACK % (third, 'player'))
        time.sleep(8)
        joiner = [lock_try(r, b, [third], 'wo223-lock-joiner', near=True) for _ in range(tries)]
        for i, n in enumerate(bandits):
            a.lua('W223LK_' + n + ' = (W223LK_' + n + ' or 0) + 1'); a.lua(S.STOP % (n, 2270 + i))
        for c in (a, b): c.stop_keep()
        ans = lambda c, m, why: sum(1 for l in c.lines(c.nlog, m['n']) if 'WO178-HOSTILE answer=yes' in l and 'why=' + why in l)
        det = {'bandits': bandits, 'host_tries': [h[0] for h in host], 'host_ok': sum(1 for h in host if h[1]),
               'joiner_tries': [j[0] for j in joiner], 'joiner_ok': sum(1 for j in joiner if j[1]),
               'host_answers_figure_fight': ans(a, ma, 'figure-fight'), 'joiner_answers_partner_fight': ans(b, mb, 'partner-fight'),
               'joiner_answers_engaged': ans(b, mb, 'engaged')}
        with open(os.path.join(r.dir, 'lock.json'), 'w', encoding='utf-8') as f: json.dump(det, f, indent=1)
        ok = det['host_ok'] > 0 and det['joiner_ok'] > 0
        return ok, ('ok' if ok else 'no-lock'), det
    return fn


def def_counts(a, ma, b=None, mb=None):
    """The host agent's lines since <ma>: NPC blows on the joiner's figure judged by his own engine / by the host's guess; with the
    joiner's agent (<b> since <mb>) also what he did with the guesses: applied them, or skipped them (his own engine had judged)."""
    L = a.lines(a.alog, ma['a'])
    judged = sum(1 for l in L if 'WO208-DEF outcome ' in l)
    fb = [l for l in L if 'WO208-DEF fallback ' in l]
    none = sum(1 for l in fb if 'joiner=none' in l)
    out = {'judged_by_his_engine': judged, 'host_fallback': len(fb), 'fallback_none': none,
           'fallback_pct': round(100.0 * len(fb) / (judged + len(fb)), 1) if judged + len(fb) else None,
           'grab_blows_the_hosts_at_once': sum(1 for l in L if 'WO208-DEF grab ' in l),
           'swings_already_judged': sum(1 for l in L if 'WO208-DEF swing ' in l)}
    if b is not None and mb is not None:
        LB = b.lines(b.alog, mb['a'])
        out['joiner_skipped'] = sum(1 for l in LB if 'WO208-DEF skipped ' in l)
        out['joiner_applied'] = sum(1 for l in LB if 'WO161-HIT victim=me' in l)
        out['applied_pct'] = round(100.0 * out['joiner_applied'] / (judged + len(fb)), 1) if judged + len(fb) else None
    return out


def last_status(c, mark, tag):
    """The newest '<tag> ...' line of <c>'s agent since <mark> (the DLL's status pages come every 30 s)."""
    L = [l for l in c.lines(c.alog, mark['a']) if tag in l]
    return L[-1].split(tag, 1)[1].strip()[:400] if L else None


def camp2_once(r, tag, fight_s):
    """One two-player camp: both walk in from the start side by side; the fight is the camp's own doing."""
    S = _S()
    import scen222
    a, b = r.a, r.b
    names, centre = camp(r)
    if not names: return {'err': 'no camp'}
    fig = a.ask('local n = "none"; for k, g in pairs(KCD2MP.ghosts or {}) do if g.entity then n = g.entity:GetName() end end; System.LogAlways("[W223G] " .. n)', 'W223G', 6)
    start = line_point(centre, START_M)
    side = (-math.sin(math.radians(BEARING)) * 1.5, math.cos(math.radians(BEARING)) * 1.5)
    jstart = (start[0] + side[0], start[1] + side[1])
    for c in (a, b): c.keep_up()
    a.teleport(*start); b.teleport(*jstart)
    time.sleep(6.0)
    arms, armed = arm_walkers(a, b, (a, b))
    a.lua(READER_SET % (', '.join('"%s"' % n for n in names), fig)); a.lua(READER_BITS); a.lua(READER_FN)
    ma, mb = a.mark(), b.mark()
    off = a.size(a.klog)
    a.lua(READER_GO)
    stance = set_stance(r, b, False, tag)
    stop = threading.Event(); wk = {}
    th = threading.Thread(target=walker, args=(r, a, 'walk', centre, stop, wk, tag), daemon=True)
    th.start()
    while not wk.get('t_down') and not wk.get('err') and th.is_alive(): time.sleep(0.05)
    if wk.get('err'): stop.set(); a.lua(READER_STOP); return {'err': 'walker: ' + wk['err']}
    ux, uy = -math.cos(math.radians(BEARING)), -math.sin(math.radians(BEARING))
    b.lua_nowait(PAIR_WALK % (jstart[0], jstart[1], ux, uy, float(os.environ.get('KCDMP_RIG_W223_PAIR_MPS', '2.9')),
                              max(0.0, math.hypot(jstart[0] - centre[0], jstart[1] - centre[1]) - 4.0)))
    # until a bandit takes a player as its target (the fight's start), or the walk is over
    t0 = time.time(); t_fight = None
    while time.time() - t0 < 90:
        time.sleep(0.4)
        for l in a.lines(a.klog, off):
            m = SKIRMISH.search(l)
            if m and m.group(1) == 'TargetChanged' and m.group(2) in names and m.group(3) in ('Dude', fig): t_fight = time.time(); break
        if t_fight: break
    stop.set(); th.join(timeout=15); b.lua(PAIR_STOP)
    out = {'tag': tag, 'names': names, 'figure': fig, 'fight': bool(t_fight), 'walker': wk, 'arms': arms, 'armed': armed, 'joiner_stance': stance}
    if not t_fight:
        a.lua(READER_STOP)
        return out
    for n in names: a.lua(scen222.KEEP_NPC.replace('W222NK', 'W223NK_' + n) % (n, 35.0, 90.0, ''))
    for c in (a, b): c.lua(scen222.DRAW)
    locks = []
    k = 0; turns = []
    while time.time() - t_fight < fight_s:
        who = 'host' if k % 2 == 0 else 'joiner'
        sw = {'sent': 0, 'refused': 0, 'err': None}
        st = threading.Event()
        t2 = scen222.swinger(r, who, sw, st, 'wo223-camp-' + who); t2.start()
        st.wait(min(20.0, max(1.0, fight_s - (time.time() - t_fight)))); st.set(); t2.join(timeout=10)
        turns.append({'who': who, 'sent': sw['sent'], 'err': sw['err']})
        k += 1
        if k in (1, 2, 3) and os.environ.get('KCDMP_RIG_W223_FRAME'):
            # (three moments of the fight, a pair for each side at each: the frames are looked at and the clearest kept; the bench
            # joiner's own post-load debug text is switched off for the picture)
            try:
                import scen217
                b.lua('System.SetCVar("wh_ai_PostLoadErrorDebugDraw", 0)')
                s0, e0 = read_run(a.lines(a.klog, off), names, fig)
                tg0 = targets_at(e0, names, s0[-1]['t']) if s0 else {}
                fr = out.setdefault('frames', {})
                for label, want in (('on-joiner', fig), ('on-host', 'Dude')):
                    on = [n for n, t in tg0.items() if t == want]
                    if on: fr['%s-%d' % (label, k)] = scen217.frame_pair(r, 'camp-%s-%s-%d' % (tag, label, k), on[0], move=False)
            except BaseException as ex:
                out.setdefault('frames', {})['error-%d' % k] = '%s: %s' % (type(ex).__name__, ex)
        # C.3, twice in a long fight: each player locks on to an enemy that fights his partner
        if fight_s >= 60 and k in (2, 5):
            samples, events = read_run(a.lines(a.klog, off), names, fig)
            if samples:
                tg = targets_at(events, names, samples[-1]['t'])
                on_fig = [n for n, t in tg.items() if t == fig]
                on_host = [n for n, t in tg.items() if t == 'Dude']
                hl = lock_try(r, a, on_fig, 'wo223-lock-host')
                jl = lock_try(r, b, on_host, 'wo223-lock-joiner')
                locks.append({'at_s': round(time.time() - t_fight), 'enemies_on_joiner': on_fig, 'enemies_on_host': on_host,
                              'host_locked': hl[0], 'host_ok': hl[1], 'joiner_locked': jl[0], 'joiner_ok': jl[1]})
    time.sleep(1.0)
    a.lua(READER_STOP)
    samples, events = read_run(a.lines(a.klog, off), names, fig)
    for i, n in enumerate(names):
        a.lua('W223NK_' + n + ' = (W223NK_' + n + ' or 0) + 1'); a.lua(S.STOP % (n, 2290 + i))
    for c in (a, b): c.stop_keep()
    # the fight's start on the reader's clock = the first bandit that took a player
    first = [e for e in events if e['kind'] == 'skirmish' and e['what'] == 'TargetChanged' and e['on'] in names and e['target'] in ('Dude', fig)]
    split5 = {}
    if first:
        tg = targets_at(events, names, first[0]['t'] + 5.0)
        split5 = {'host': sum(1 for t in tg.values() if t == 'Dude'), 'joiner': sum(1 for t in tg.values() if t == fig), 'by_bandit': tg,
                  'first': first[0]['target']}
    out.update({'split5': split5, 'each_has_one': bool(split5) and split5['host'] >= 1 and split5['joiner'] >= 1, 'turns': turns, 'locks': locks,
                'defence': def_counts(a, ma, b, mb), 'summary_first': {kk: first_of(summarise(samples, events, names, fig), kk) for kk in ('notice', 'warn', 'draw', 'target', 'charge')},
                'copies': last_status(b, mb, 'MP-W223-NATIVE F copies:'), 'figure_combat': last_status(a, ma, 'MP-W223-NATIVE D figure fights:'),
                'secs': round(time.time() - t0)})
    with open(os.path.join(r.dir, 'camp2-%s.json' % tag), 'w', encoding='utf-8') as f: json.dump({'out': out, 'events': events}, f)
    rig.say('  camp2', tag, 'split5:', json.dumps(split5)[:300], 'defence:', out['defence'], 'locks:', json.dumps(locks)[:300])
    return out


def s_camp2(r, runs, fight_s, hour=10.0):
    def fn():
        rows = []
        for k in range(runs):
            tag = 'camp-%d' % (k + 1)
            ok, info = fresh_camp(r, 'both', hour)
            if not ok: rows.append({'tag': tag, 'err': info}); rig.say('  camp2', tag, 'NOT RUN:', info); continue
            o = camp2_once(r, tag, fight_s)
            rows.append(o)
            with open(os.path.join(r.dir, 'camp2.json'), 'w', encoding='utf-8') as fh: json.dump(rows, fh, indent=1)
        fights = [x for x in rows if x.get('fight')]
        both = sum(1 for x in fights if x.get('each_has_one'))
        det = {'runs': len(rows), 'fights': len(fights), 'each_player_has_an_enemy_at_5s': both, 'errors': [x.get('err') for x in rows if x.get('err')]}
        return bool(fights), ('ok' if fights else 'no-fight'), det
    return fn


def s_swap(r, cycles, per_s=30.0):
    """Scene 4: one bandit set on the host; the joiner strikes it until the host's world says it fights his figure, then the host strikes
    it until it fights him again. Per turn: how long the host's NPC took to turn, and (the joiner's DLL) how long its copy fought the
    wrong one."""
    def fn():
        S = _S()
        import scen222
        a, b = r.a, r.b
        t = a.ask(S.FIND_BANDIT1, 'W210B', 6)
        if not t or t == 'none': return False, 'no-bandit', t
        bandit = t.split()[0]
        fig = a.ask('local n = "none"; for k, g in pairs(KCD2MP.ghosts or {}) do if g.entity then n = g.entity:GetName() end end; System.LogAlways("[W223G] " .. n)', 'W223G', 6)
        scen222.place_near(a, bandit, 1.9, 0.0); scen222.place_near(b, bandit, 0.0, 1.9)
        for c in (a, b): c.keep_up(); c.lua(scen222.DRAW)
        a.lua(scen222.KEEP_NPC % (bandit, 35.0, 90.0, ''))
        ma, mb = a.mark(), b.mark()
        a.lua(S.ATTACK % (bandit, 'player'))
        time.sleep(4)
        rows = []
        for k in range(cycles):
            striker = 'joiner' if k % 2 == 0 else 'host'
            want = fig if striker == 'joiner' else 'Dude'
            me = b if striker == 'joiner' else a
            me.lua(S.FACE_TO % bandit)
            off = a.size(a.klog)
            sw = {'sent': 0, 'refused': 0, 'err': None}
            st = threading.Event()
            th = scen222.swinger(r, striker, sw, st, 'wo223-swap-' + striker); th.start()
            t0 = time.time(); turned = None
            while time.time() - t0 < per_s and turned is None:
                time.sleep(0.3)
                for l in a.lines(a.klog, off):
                    m = SKIRMISH.search(l)
                    if m and m.group(1) == 'TargetChanged' and m.group(2) == bandit and m.group(3) == want: turned = time.time() - t0; break
            st.set(); th.join(timeout=10)
            rows.append({'striker': striker, 'turned_after_s': round(turned, 1) if turned is not None else None, 'swings': sw['sent'], 'err': sw['err']})
            if turned is None:   # it never turned for his blows: put on him by the bench, so the next turn has something to turn back
                a.lua(S.ATTACK % (bandit, S.FIGURE if striker == 'joiner' else 'player'))
            time.sleep(3.0)
            try:
                d = float(me.ask(scen222.DIST_Q % bandit, 'W222D', 3) or -1)
            except ValueError: d = -1.0
            if d < 0 or d > 3.0: scen222.place_near(a, bandit, 1.9, 0.0); scen222.place_near(b, bandit, 0.0, 1.9)
        a.lua(scen222.STOP_KEEP_NPC); a.lua(S.STOP % (bandit, 2299))
        for c in (a, b): c.stop_keep()
        time.sleep(32)   # the DLL's status pages come every 30 s
        spells = [l for l in b.lines(b.nlog, mb['n']) if 'WO223-COPY npc=' + bandit in l]
        wrong = [float(m.group(1)) for l in spells for m in [re.search(r'right again after ([\d.]+) s', l)] if m]
        det = {'bandit': bandit, 'turns': rows, 'turned': sum(1 for x in rows if x['turned_after_s'] is not None),
               'copy_wrong_spells_s': wrong, 'copies': last_status(b, mb, 'MP-W223-NATIVE F copies:'),
               'mirror': last_status(b, mb, 'MP-W209-NATIVE'), 'defence': def_counts(a, ma, b, mb),
               'eyes': sum(1 for l in b.lines(b.klog, mb['k']) if 'WO223-EYES npc=' in l and 'fights you' in l),
               'heals': sum(1 for l in b.lines(b.nlog, mb['n']) if 'WO210-HEAL npc=' in l)}
        m = re.search(r'both=\d+ \(([\d.]+) %\)', det['copies'] or '')
        det['aimed_and_in_his_fight_pct'] = float(m.group(1)) if m else None
        with open(os.path.join(r.dir, 'swap.json'), 'w', encoding='utf-8') as f: json.dump(det, f, indent=1)
        ok = det['aimed_and_in_his_fight_pct'] is not None and det['aimed_and_in_his_fight_pct'] >= 98.0
        return ok, ('ok' if ok else 'copies-wrong'), det
    return fn


PROBE_REL = ('local fig; for k, g in pairs(KCD2MP.ghosts or {}) do fig = g.entity end; local t = {}; '
             'for _, n in ipairs({%s}) do local e = System.GetEntityByName(n); if e and e.soul then local a, b, c = "?", "?", "?"; '
             'pcall(function() a = string.format("%%.2f", e.soul:GetRelationship(player.this.id)) end); '
             'pcall(function() if fig then b = string.format("%%.2f", e.soul:GetRelationship(fig.this.id)) end end); '
             'pcall(function() c = tostring(e.soul:IsPublicEnemy()) end); t[#t + 1] = n:sub(-1) .. ":" .. a .. ":" .. b .. ":" .. c end end; '
             'System.LogAlways("[W223P1] " .. table.concat(t, " "))')
PROBE_ARM = ('local fig; for k, g in pairs(KCD2MP.ghosts or {}) do fig = g.entity end; local a, b, c, d = "?", "?", "?", "?"; '
             'pcall(function() a = tostring(player.human:IsWeaponDrawn()) end); pcall(function() b = tostring(fig.human:IsWeaponDrawn()) end); '
             'pcall(function() c = string.format("%%.2f", player.soul:GetRelationship(fig.this.id)) end); '
             'pcall(function() d = tostring(player.soul:IsPublicEnemy()) end); '
             'System.LogAlways("[W223P2] host_drawn=" .. a .. " figure_drawn=" .. b .. " host_to_figure=" .. c .. " host_public_enemy=" .. d)')
PROBE_AREA = ('local t = {}; for d = %d, %d, 4 do local p = { x = %f + d * %f, y = %f + d * %f, z = 0 }; '
              'pcall(function() p.z = System.GetTerrainElevation(p) + 1.0 end); local s = ""; '
              'for i, l in ipairs({%s}) do local r = nil; pcall(function() r = XGenAIModule.IsPointInAreaWithLabel(p, l) end); '
              'if r == true or (type(r) == "number" and r ~= 0) then s = s .. i end end; t[#t + 1] = d .. "=" .. (s == "" and "-" or s) end; '
              'System.LogAlways("[W223P3] " .. table.concat(t, " "))')
PROBE_LABELS = ('private', 'personal', 'prohibited', 'semipersonal', 'semipublic', 'public', 'camp', 'home', 'antitrespass')


def s_probe(r):
    def fn():
        a = r.a
        names, centre = camp(r)
        if not names: return False, 'no camp', {}
        out = {'names': names, 'centre': centre, 'labels': list(PROBE_LABELS)}
        out['rel'] = a.ask(PROBE_REL % ', '.join('"%s"' % n for n in names), 'W223P1', 8)
        out['arm'] = a.ask(PROBE_ARM, 'W223P2', 8)
        ux, uy = math.cos(math.radians(BEARING)), math.sin(math.radians(BEARING))
        lab = ', '.join('"%s"' % l for l in PROBE_LABELS)
        out['area_0_40'] = a.ask(PROBE_AREA % (0, 40, centre[0], ux, centre[1], uy, lab), 'W223P3', 8)
        out['area_44_84'] = a.ask(PROBE_AREA % (44, 84, centre[0], ux, centre[1], uy, lab), 'W223P3', 8)
        for k in ('rel', 'arm', 'area_0_40', 'area_44_84'): rig.say('  probe', k, out.get(k))
        with open(os.path.join(r.dir, 'probe.json'), 'w', encoding='utf-8') as fh: json.dump(out, fh, indent=1)
        return True, 'ok', {k: out.get(k) for k in ('rel', 'arm')}
    return fn


def plans(r, name, args):
    S = _S()
    if name == 'w223probe':
        plan = os.environ.get('KCDMP_RIG_W223_PLAN', 'host:crouch:10:1')
        return [('join', S.s_join(r)), ('probe', s_probe(r)), ('table', s_table(r, plan)), ('probe2', s_probe(r))]
    if name == 'w223approach':
        # the scenario table reads a first argument as a number: this scene's own choices come by the environment
        who = os.environ.get('KCDMP_RIG_W223_WHO', 'host')
        pace = os.environ.get('KCDMP_RIG_W223_PACE', 'walk')
        runs = int(os.environ.get('KCDMP_RIG_W223_RUNS', '1'))
        return [('approach-%s-%s' % (who, pace), s_approach(r, who, pace, runs))]
    if name == 'w223camp':
        return [('join', S.s_join(r)), ('camp', s_camp2(r, int(os.environ.get('KCDMP_RIG_W223_RUNS', '1')), float(os.environ.get('KCDMP_RIG_W223_FIGHT_S', '12')),
                                                       float(os.environ.get('KCDMP_RIG_W223_HOUR', '10'))))]
    if name == 'w223lock':
        import scen217
        return [('join', S.s_join(r)), ('day', scen217.daytime(r, 10.0)), ('lock', s_lock(r))]
    if name == 'w223swap':
        import scen217
        return [('join', S.s_join(r)), ('day', scen217.daytime(r, 10.0)), ('swap', s_swap(r, int(os.environ.get('KCDMP_RIG_W223_CYCLES', '8'))))]
    if name == 'w223table':
        plan = os.environ.get('KCDMP_RIG_W223_PLAN', 'host:walk:10:5;host:sprint:10:5;host:crouch:10:5;host:crouch:23:5')
        return [('join', S.s_join(r)), ('table', s_table(r, plan))]
    return None


CONTINUE_ON_FAIL = ('w223approach', 'w223table', 'w223camp', 'w223swap', 'w223lock', 'w223probe')
