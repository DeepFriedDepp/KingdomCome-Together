# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-224: the rig's scenes for grabs (Part A) -- test tool, never shipped.

  w224grab     the grab drill, one player at a time (KCDMP_RIG_W224_WHO = host | joiner | both, default both): one bandit set on that
               player; he locks on, walks into it (the game's clinch starts by itself when two fighters in guard meet), makes a clinch
               move, steps back; until KCDMP_RIG_W224_N clinches of his own (default 20) or KCDMP_RIG_W224_S seconds (default 420).
               The other player stands by and watches. Read from both DLLs' W224-STATE lines (one PC: one clock):
                 * every clinch the player was in on his own machine: did the other screen show it (his figure AND the bandit's body
                   there in a clinch within SHOWN_S), and how long after its end the other screen left it (stuck over STUCK_S);
                 * the host's world: every clinch of the joiner's figure -- was the joiner's own player in one then;
                 * the engine's clinch test's pass rate per kind of pair on each machine (the DLL's status pages).
               KCDMP_RIG_W224_FRAME=1 takes a host/joiner frame pair at the first clinches. KCDMP_RIG_W224_CLICK=0: he does not swing.

Keys are send-only, to the bench's own game windows (tools/dev-input/game_input.py; the maintainer's allowance).
"""
import json, os, re, sys, threading, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rig

SHOWN_S, STUCK_S = 1.5, 1.0
T = re.compile(r'^\[(\d\d):(\d\d):(\d\d)\.(\d\d\d)\]')
STATE_IN = re.compile(r'W224-STATE (\S+) in a clinch: master=(\d) guard_type=(-?\d+) opponent=(\S+)')
STATE_OUT = re.compile(r'W224-STATE (\S+) out of the clinch after (\d+) ms')


LURE_M = float(os.environ.get('KCDMP_RIG_W224_LURE_M', '45'))
# the bandit's place and the unit direction away from the other bandits within 50 m of it (alone: east)
LURE_Q = ('local e = System.GetEntityByName("%s"); if e then local q = e:GetWorldPos(); local sx, sy, n = 0, 0, 0; '
          'for _, x in ipairs(System.GetEntitiesByClass("NPC") or {}) do local nm = string.lower(x:GetName()); '
          'if x ~= e and x.soul and string.find(nm, "bandit", 1, true) then local p = x:GetWorldPos(); '
          'if (p.x - q.x) ^ 2 + (p.y - q.y) ^ 2 < 2500 then sx = sx + p.x; sy = sy + p.y; n = n + 1 end end end; '
          'local dx, dy = 1, 0; if n > 0 then dx, dy = q.x - sx / n, q.y - sy / n; local l = math.sqrt(dx * dx + dy * dy); '
          'if l > 0.5 then dx, dy = dx / l, dy / l else dx, dy = 1, 0 end end; '
          'System.LogAlways(string.format("[W224L] %%.2f %%.2f %%.2f %%.3f %%.3f %%d", q.x, q.y, q.z, dx, dy, n)) end')


# every other living bandit within 60 m of the staged one: its fight stopped the game's own way (the stopFight message)
OTHERS_STOP = ('local e = System.GetEntityByName("%s"); if e then local q = e:GetWorldPos(); W224OS = (W224OS or 0) + 1; '
               'for _, x in ipairs(System.GetEntitiesByClass("NPC") or {}) do local nm = string.lower(x:GetName()); '
               'if x ~= e and x.soul and string.find(nm, "bandit", 1, true) then local p = x:GetWorldPos(); '
               'if (p.x - q.x) ^ 2 + (p.y - q.y) ^ 2 < 3600 then local t = XGenAIModule.MakeTableFromType("stopFight"); t.soulCount = 1; '
               't.messageId = "w224os" .. W224OS .. nm; pcall(function() XGenAIModule.SendMessageToEntityData(x.this.id, "stopFight", t) end) end end end end')


def _S():
    import scenarios
    return scenarios


def _t(l):
    m = T.match(l)
    return int(m.group(1)) * 3600 + int(m.group(2)) * 60 + int(m.group(3)) + int(m.group(4)) / 1000.0 if m else None


def spells(lines):
    """{body: [(t_in, t_out or None, master, opponent)]} from one machine's W224-STATE lines."""
    open_, done = {}, {}
    for l in lines:
        t = _t(l)
        if t is None: continue
        m = STATE_IN.search(l)
        if m:
            open_[m.group(1)] = (t, int(m.group(2)), m.group(4))
            continue
        m = STATE_OUT.search(l)
        if m and m.group(1) in open_:
            t0, ms, opp = open_.pop(m.group(1))
            done.setdefault(m.group(1), []).append((t0, t, ms, opp))
    for b, (t0, ms, opp) in open_.items(): done.setdefault(b, []).append((t0, None, ms, opp))
    return done


def pair_up(mine, other, my_figure_there):
    """A player's own clinches (his machine's spells of 'the-local-player') against the other machine's spells: per clinch, was his
    figure there in one within SHOWN_S, the body he clinched too, and how long after his end the figure there left it."""
    rows = []
    for (t0, t1, master, opp) in mine.get('the-local-player', []):
        fig = [x for x in other.get(my_figure_there, []) if t0 - 0.5 <= x[0] <= t0 + SHOWN_S]
        body = [x for x in other.get(opp, []) if t0 - 0.5 <= x[0] <= t0 + SHOWN_S]
        late = None
        if fig and t1 is not None: late = (fig[0][1] - t1) if fig[0][1] is not None else 999.0
        rows.append({'t': round(t0, 3), 'secs': round(t1 - t0, 2) if t1 else None, 'master': master, 'with': opp,
                     'figure_shown_after_s': round(fig[0][0] - t0, 2) if fig else None, 'body_shown_after_s': round(body[0][0] - t0, 2) if body else None,
                     'both_shown': bool(fig and body), 'other_left_after_s': None if late is None else round(late, 2),
                     'stuck': late is not None and late > STUCK_S})
    return rows


def last_pages(c, mark):
    out = {}
    for l in c.lines(c.alog, mark['a']):
        m = re.search(r'MP-W224-NATIVE ([^:]{1,40}): (.*)', l)
        if m: out[m.group(1)] = m.group(2)[:1200]
        if 'MP-W224-STATS' in l: out['stats'] = l.split('MP-W224-STATS', 1)[1].strip()[:600]
    return out


def grab_keys(r, c, stop, st, tag):
    """One player's drill (send-only keys to his own window): lock on, walk in, a clinch move, step back."""
    S = _S()
    sys.path.insert(0, os.path.join(rig.REPO, 'tools', 'dev-input'))
    import game_input
    g = None
    try:
        g = game_input.Game(tag)
        g.pids = {S.rec_of(c)['pid']}
        if not S.put_front(c): st['err'] = 'not in front'; return
        n = 0
        while not stop.is_set():
            try:
                if n % 4 == 0: g.tap('tab', 80)
                g.tap('w', int(os.environ.get('KCDMP_RIG_W224_IN_MS', '1300')))    # into him: the clinch starts by itself
                stop.wait(float(os.environ.get('KCDMP_RIG_W224_HOLD_S', '0.9')))
                # a clinch move (or a swing when no clinch came). KCDMP_RIG_W224_CLICK=0: none -- the host's own swings beat a lone
                # bandit out of the fight in 2-4 minutes (a8-a10: its attack rows stop for minutes; 3-6 clinches in a run)
                if n % 2 == 0 and os.environ.get('KCDMP_RIG_W224_CLICK', '1') != '0': g.click('left', 90)
                stop.wait(0.9)
                g.tap('s', int(os.environ.get('KCDMP_RIG_W224_OUT_MS', '900')))    # apart again
                st['rounds'] = st.get('rounds', 0) + 1
            except game_input.NotFocused:
                st['refused'] = st.get('refused', 0) + 1
                if st['refused'] > 20 or not S.put_front(c): break
                try: g.release_all()
                except game_input.NotFocused: pass
            n += 1
            stop.wait(0.6)
    except BaseException as ex:
        st['err'] = '%s: %s' % (type(ex).__name__, ex)
    finally:
        try:
            if g: g.release_all()
        except BaseException: pass


def grab_phase(r, who, want, secs):
    S = _S()
    import scen222
    a, b = r.a, r.b
    me, other = (a, b) if who == 'host' else (b, a)
    t = a.ask(S.FIND_BANDIT1, 'W210B', 6)
    if not t or t == 'none': return {'who': who, 'err': 'no-bandit'}
    bandit = t.split()[0]
    for c in (a, b): c.keep_up()
    a.lua(scen222.KEEP_NPC % (bandit, 35.0, 90.0, ''))
    ma, mb = a.mark(), b.mark()
    # one bandit, away from its camp: the player stands LURE_M out on the side away from the other bandits, the bandit is set on him and
    # comes; the rest of the camp is too far to notice (three bandits around the joiner body-blocked his walk in the first drill)
    lured = False
    lt = a.ask(LURE_Q % bandit, 'W224L', 6)
    try:
        qx, qy, qz, dx, dy, near = lt.split(); qx, qy, dx, dy = float(qx), float(qy), float(dx), float(dy)
        lx, ly = qx + dx * LURE_M, qy + dy * LURE_M
        me.teleport(lx, ly); other.teleport(lx + dx * 6.0 - dy * 3.0, ly + dy * 6.0 + dx * 3.0)
        time.sleep(5)
        me.lua(scen222.DRAW)
        a.lua(S.ATTACK % (bandit, 'player' if who == 'host' else S.FIGURE))
        t_l = time.time()
        while time.time() - t_l < 40:
            time.sleep(1.5)
            try: d = float(me.ask(scen222.DIST_Q % bandit, 'W222D', 3) or -1)
            except ValueError: d = -1.0
            if 0 <= d <= 4.5: lured = True; break
    except (AttributeError, ValueError):
        pass
    if not lured:
        scen222.place_near(me, bandit, 2.2, 0.0); scen222.place_near(other, bandit, 0.0, 7.0)
        me.lua(scen222.DRAW)
        a.lua(S.ATTACK % (bandit, 'player' if who == 'host' else S.FIGURE))
        time.sleep(3)
    me.lua(S.FACE_TO % bandit)
    st = {}
    stop = threading.Event()
    th = threading.Thread(target=grab_keys, args=(r, me, stop, st, 'wo224-grab-' + who), daemon=True)
    th.start()
    t0 = last = time.time(); n = 0; frames = []
    mk = ma if who == 'host' else mb
    while time.time() - t0 < secs and th.is_alive():
        time.sleep(1.0)
        n_now = sum(1 for l in me.lines(me.nlog, mk['n']) if 'W224-STATE the-local-player in a clinch' in l)
        if n_now > n and os.environ.get('KCDMP_RIG_W224_FRAME') and len(frames) < 3:
            try:
                import scen217
                frames.append(scen217.frame_pair(r, 'grab-%s-%d' % (who, n_now), bandit, move=False))
            except BaseException as ex:
                frames.append('%s: %s' % (type(ex).__name__, ex))
        n = n_now
        if n >= want: break
        if time.time() - last > 12:
            last = time.time()
            try: d = float(me.ask(scen222.DIST_Q % bandit, 'W222D', 3) or -1)
            except ValueError: d = -1.0
            if d < 0 or d > 5.0: scen222.place_near(me, bandit, 2.2, 0.0)
            me.lua(scen222.DRAW)   # (a blow that put him down left his weapon away: two minutes without a clinch in one run)
            me.lua(S.FACE_TO % bandit)
            # the rest of the camp is kept out of it: four bandits on the joiner held him 7 m off his own (a6: 4 clinches in 5 min)
            a.lua(OTHERS_STOP % bandit)
            if int(time.time() - t0) % 36 < 12: a.lua(S.ATTACK % (bandit, 'player' if who == 'host' else S.FIGURE))
    stop.set(); th.join(timeout=12)
    a.lua(scen222.STOP_KEEP_NPC); a.lua(S.STOP % (bandit, 2240))
    for c in (a, b): c.stop_keep()
    time.sleep(3.0)
    sa, sb = spells(a.lines(a.nlog, ma['n'])), spells(b.lines(b.nlog, mb['n']))
    rows = pair_up(sa, sb, 'kcd2mp_0') if who == 'host' else pair_up(sb, sa, 'kcd2mp_1')
    out = {'who': who, 'bandit': bandit, 'lured': lured, 'keys': st, 'secs': round(time.time() - t0), 'own_clinches': len(rows),
           'both_shown': sum(1 for x in rows if x['both_shown']), 'stuck': sum(1 for x in rows if x['stuck']), 'rows': rows, 'frames': frames}
    if who == 'joiner':
        # the host's world: the clinches its engine ran between the bandit and his figure -- was he in one himself then
        hf = sa.get('kcd2mp_1', [])
        jp = sb.get('the-local-player', [])
        out['host_world_figure_clinches'] = len(hf)
        out['joiner_in_it_himself'] = sum(1 for (t1, _, _, _) in hf if any(t1 - 1.0 <= x[0] <= t1 + SHOWN_S for x in jp))
    rig.say('  grab', who, 'own clinches', out['own_clinches'], 'both halves shown on the other screen', out['both_shown'], 'stuck', out['stuck'],
            'keys', json.dumps(st)[:120])
    return out


def s_grab(r, who, want, secs):
    def fn():
        a, b = r.a, r.b
        ma, mb = a.mark(), b.mark()
        phases = [grab_phase(r, w, want, secs) for w in (('host', 'joiner') if who == 'both' else (who,))]
        time.sleep(32)   # the DLL's status pages come every 30 s
        det = {'phases': phases, 'pages': {'host': last_pages(a, ma), 'joiner': last_pages(b, mb)}}
        with open(os.path.join(r.dir, 'grab.json'), 'w', encoding='utf-8') as f: json.dump(det, f, indent=1)
        ok = all(not p.get('err') and p.get('own_clinches', 0) > 0 for p in phases)
        short = {p['who']: '%d own, %d shown, %d stuck' % (p.get('own_clinches', 0), p.get('both_shown', 0), p.get('stuck', 0)) if not p.get('err') else p['err'] for p in phases}
        return ok, ('ok' if ok else 'no-clinch'), short
    return fn


def plans(r, name, args):
    S = _S()
    if name == 'w224grab':
        import scen217
        return [('join', S.s_join(r)), ('day', scen217.daytime(r, 10.0)),
                ('grab', s_grab(r, os.environ.get('KCDMP_RIG_W224_WHO', 'both'), int(os.environ.get('KCDMP_RIG_W224_N', '20')),
                                float(os.environ.get('KCDMP_RIG_W224_S', '420'))))]
    return None


CONTINUE_ON_FAIL = ('w224grab',)
