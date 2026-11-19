# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-222: the bench scenes of "hits that land like single-player" (test tool, never shipped).

Every action goes through the game's console, Lua or our pipe (the agent); the players' own attacks and blocks are keys (the maintainer
allows the bench to drive the keyboard and mouse; send-only through tools/dev-input/game_input.py, the copy's own window in front, every
event logged). Nothing here reads input.

    w222ref     A.0 / scene 1 (the duel): one bandit; the host's player lands blows on it, then the joiner's -- each in two situations
                (the bandit fresh, the bandit's stamina held low); the host's DLL writes one W222-BLOW line per blow (blow_ledger.cpp)
    w222def     scene 3: two bandits on the joiner only while he blocks, dodges and is hit (WO-208's defence scene, two attackers)
    w222camp    scene 2: both players against three bandits for 3 minutes (the players' keys by turns: one window is in front)
    w222chase   scene 4: a bandit fights the joiner, is brought low on the host until it flees; the joiner follows it and swings until it
                is dead; then the same with the host following
    w222all     every scene above, in that order
"""
import json, os, re, sys, threading, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rig


def _S():
    import scenarios
    return scenarios


def note(r, _ev, **kw):
    with open(os.path.join(r.dir, 'w222-events.jsonl'), 'a', encoding='utf-8') as f:
        f.write(json.dumps(dict(ev=_ev, t=round(time.time(), 3), **kw)) + '\n')


KEEP_NPC = ('W222NK = (W222NK or 0) + 1; local gen = W222NK; local function k() if gen ~= W222NK then return end; '
            'local e = System.GetEntityByName("%s"); if e and e.soul then pcall(function() '
            'if e.soul:GetState("health") < %f then e.soul:SetState("health", %f) end; '
            '%s end) end; Script.SetTimer(400, k) end; k()')
STOP_KEEP_NPC = 'W222NK = (W222NK or 0) + 1'
LOW_ST = 'if e.soul:GetState("stamina") > 12 then e.soul:SetState("stamina", 8) end'
DRAW = 'pcall(function() if not player.human:IsWeaponDrawn() then player.human:DrawWeapon() end end)'


PLACE_NEAR = ('local e = System.GetEntityByName("%s"); if e then local q = e:GetWorldPos(); '
              'player:SetWorldPos({x = q.x + %f, y = q.y + %f, z = q.z + 0.4}) end')
DIST_Q = ('local e = System.GetEntityByName("%s"); local d = -1; if e then local p, q = player:GetWorldPos(), e:GetWorldPos(); '
          'd = math.sqrt((p.x - q.x) ^ 2 + (p.y - q.y) ^ 2 + (p.z - q.z) ^ 2) end; System.LogAlways(string.format("[W222D] %%.1f", d))')

CM_Q = ('local e = System.GetEntityByName("%s"); local c = "?"; if e and e.soul then pcall(function() c = tostring(e.soul:IsInCombatMode()) end) end; '
        'System.LogAlways("[W222C] " .. c)')


def place_near(c, npc, dx, dy, tries=4):
    """The player put beside <npc> at the NPC's own height (the camp's rocks are above the terrain: a terrain-height teleport slid the
    first reference run's host 30 m down the slope), checked after the ground caught it. Returns the distance (-1 unknown)."""
    d = -1.0
    for _ in range(tries):
        c.lua(PLACE_NEAR % (npc, dx, dy))
        time.sleep(2.5)
        try: d = float(c.ask(DIST_Q % npc, 'W222D', 3) or -1)
        except ValueError: d = -1.0
        if 0 <= d <= max(4.0, 1.5 * (dx * dx + dy * dy) ** 0.5): return d
    return d


def count(c, off, pat):
    return sum(1 for l in c.lines(c.nlog, off) if pat in l)


def swinger(r, who, swings, stop, tag):
    S = _S()
    return threading.Thread(target=S.host_swinger if who == 'host' else S.joiner_swinger, args=(r, swings, stop, tag), daemon=True)


def ref_phase(r, striker, bandit, x, y, want, secs, low):
    """One phase of the reference: <striker> ('host' | 'joiner') lands blows on <bandit> until <want> blows are in the ledger or <secs>."""
    S = _S()
    a, b = r.a, r.b
    me, other = (a, b) if striker == 'host' else (b, a)
    dme = place_near(me, bandit, 1.8, 0.0)
    place_near(other, bandit, 5.0, 2.5)
    for c in (a, b): c.keep_up()
    me.lua(DRAW)
    time.sleep(2.0)
    a.lua(KEEP_NPC % (bandit, 35.0, 90.0, LOW_ST if low else ''))
    off = a.size(a.nlog)
    tgt = 'player' if striker == 'host' else S.FIGURE
    me.lua(S.FACE_TO % bandit)
    a.lua(S.ATTACK % (bandit, tgt))
    swings = {'sent': 0, 'refused': 0, 'err': None}
    stop = threading.Event()
    th = swinger(r, striker, swings, stop, 'wo222-ref-' + striker)
    th.start()
    t0 = last = time.time()
    n = 0
    frames = []
    while time.time() - t0 < secs:
        time.sleep(3)
        n = count(a, off, 'W222-BLOW by=%s ' % striker)
        # B's frames: right after a blow of the striker drew the host's hit reaction, both screens (the other player turned to it)
        if len(frames) < int(os.environ.get('KCDMP_RIG_W222_FRAMES', '2')):
            hits = [l for l in a.lines(a.nlog, off) if 'W222-BLOW by=%s ' % striker in l and ' next=hit ' in l]
            if len(hits) > len(frames) * 3:
                import scen217
                frames.append(scen217.frame_pair(r, 'react-%s-%d%s' % (striker, len(frames) + 1, '-low' if low else ''), bandit, move=False))
        if n >= want: break
        me.lua_nowait(S.FACE_TO % bandit)
        if time.time() - last > 10:
            last = time.time()
            try: d = float(me.ask(DIST_Q % bandit, 'W222D', 3) or -1)
            except ValueError: d = -1.0
            if d < 0 or d > 3.0:
                # drifted off (a knock-back, a slide): put back, the weapon out again (a move holsters it), turned to it
                stop.set(); th.join(timeout=10)
                place_near(me, bandit, 1.8, 0.0); me.lua(DRAW); time.sleep(1.5); me.lua(S.FACE_TO % bandit)
                stop = threading.Event(); th = swinger(r, striker, swings, stop, 'wo222-ref-' + striker); th.start()
                swings['replaced'] = swings.get('replaced', 0) + 1
            cm = a.ask(CM_Q % bandit, 'W222C', 3)
            if cm != 'true': a.lua(S.ATTACK % (bandit, tgt))
    stop.set(); th.join(timeout=10)
    a.lua(STOP_KEEP_NPC)
    for c in (a, b): c.stop_keep()
    d = {'striker': striker, 'bandit': bandit, 'low_stamina': low, 'blows': n, 'placed_m': dme, 'frames': frames, 'swings': swings, 'secs': round(time.time() - t0)}
    note(r, 'ref-phase', **d)
    rig.say('  ref phase', d)
    return d


def s_ref(r, want=int(os.environ.get('KCDMP_RIG_W222_N', '40')), secs=float(os.environ.get('KCDMP_RIG_W222_S', '300'))):
    """A.0: the reference -- the host's own blows and the joiner's on one bandit, fresh and at low stamina. Pass: both strikers' blows in
    the host's ledger."""
    def fn():
        S = _S()
        t = r.a.ask(S.FIND_BANDIT1, 'W210B', 6)
        if not t or t == 'none': return False, 'no-bandit', t
        bandit, x, y, z = t.split()
        x, y = float(x), float(y)
        phases = []
        for striker in ('host', 'joiner'):
            for low in (False, True):
                phases.append(ref_phase(r, striker, bandit, x, y, want // 2, secs / 2, low))
                r.a.lua(S.STOP % (bandit, len(phases)))
                time.sleep(3)
        hb = sum(p['blows'] for p in phases if p['striker'] == 'host')
        jb = sum(p['blows'] for p in phases if p['striker'] == 'joiner')
        det = {'host_blows': hb, 'joiner_blows': jb, 'phases': phases}
        return (hb > 0 and jb > 0), ('ok' if hb > 0 and jb > 0 else 'no-blows'), det
    return fn


def s_camp(r, secs=float(os.environ.get('KCDMP_RIG_W222_CAMP_S', '180')), turn=20.0):
    """Scene 2: both players against three bandits of one camp for <secs>; the players' keys by turns (one window in front at a time).
    The bandits are kept above a third of their health (the scene measures blows and reactions, not kills). Pass: blows of both."""
    def fn():
        S = _S()
        t = r.a.ask(S.FIND_BANDITS3, 'W207K', 6)
        if not t or t.startswith('none'): return False, 'no-bandits', t
        names, x, y, z = t.split()
        bandits = names.split(',')[:3]
        x, y = float(x), float(y)
        place_near(r.a, bandits[0], 2.5, 0.0); place_near(r.b, bandits[0], 2.5, 2.5)
        for c in (r.a, r.b): c.keep_up(); c.lua(DRAW)
        time.sleep(2)
        for b in bandits: r.a.lua(KEEP_NPC.replace('W222NK', 'W222NK_' + b) % (b, 35.0, 90.0, ''))
        off = r.a.size(r.a.nlog)
        for i, b in enumerate(bandits): r.a.lua(S.ATTACK % (b, 'player' if i % 2 == 0 else S.FIGURE))
        t0 = time.time(); last = t0; k = 0; turns = []
        while time.time() - t0 < secs:
            who = 'host' if k % 2 == 0 else 'joiner'
            sw = {'sent': 0, 'refused': 0, 'err': None}
            stop = threading.Event()
            th = swinger(r, who, sw, stop, 'wo222-camp-' + who); th.start()
            stop.wait(turn); stop.set(); th.join(timeout=10)
            turns.append({'who': who, 'sent': sw['sent'], 'err': sw['err']})
            k += 1
            if time.time() - last > 30:
                for i, b in enumerate(bandits): r.a.lua(S.ATTACK % (b, 'player' if i % 2 == 0 else S.FIGURE))
                last = time.time()
        for b in bandits: r.a.lua('W222NK_' + b + ' = (W222NK_' + b + ' or 0) + 1')
        for i, b in enumerate(bandits): r.a.lua(S.STOP % (b, 50 + i))
        for c in (r.a, r.b): c.stop_keep()
        hb, jb = count(r.a, off, 'W222-BLOW by=host '), count(r.a, off, 'W222-BLOW by=joiner ')
        d = {'bandits': bandits, 'host_blows': hb, 'joiner_blows': jb, 'turns': turns}
        note(r, 'camp', **d)
        return (hb > 0 and jb > 0), ('ok' if hb > 0 and jb > 0 else 'no-blows'), d
    return fn


POS_Q = ('local e = System.GetEntityByName("%s"); if e then local p = e:GetWorldPos(); System.LogAlways(string.format("[W222P] %%.2f %%.2f %%.2f", p.x, p.y, p.z)) '
         'else System.LogAlways("[W222P] none") end')
FLEE_Q = ('local e = System.GetEntityByName("%s"); local f = "?"; if e and e.soul then pcall(function() local s = e.soul; '
          'f = "flee:" .. tostring(s:HasScriptContext("combat_flee")) .. "/" .. tostring(s:HasScriptContext("crime_interruptFlee")) .. '
          '",surrender:" .. tostring(s:HasScriptContext("combat_surrender")) .. "/" .. tostring(s:HasScriptContext("crime_interruptMercy")) end) end; '
          'System.LogAlways("[W222F] " .. f)')
FOLLOW = ('local p = player:GetWorldPos(); local dx, dy = %f - p.x, %f - p.y; local n = math.sqrt(dx * dx + dy * dy); '
          'if n > 2.2 then player:SetWorldPos({x = %f - dx / n * 1.6, y = %f - dy / n * 1.6, z = %f + 0.1}) end')


# D (the chase): one named body's hips and left foot, 20 times a second, into the game's log (bench only) -- the WO's step tool
# reads the step period the scoreboard's way (the left foot's forward offset from the hips, autocorrelated) on both games
STEP_ON = ('W222S = (W222S or 0) + 1; local g = W222S; local n = "%s"; local function k() if g ~= W222S then return end; '
           'local e = System.GetEntityByName(n); if e and e.GetBonePos then local ok, h = pcall(e.GetBonePos, e, "Hips"); '
           'local ok2, f = pcall(e.GetBonePos, e, "LeftFoot"); if ok and ok2 and h and f then System.LogAlways(string.format('
           '"[W222S] %%.3f %%s %%.3f %%.3f %%.3f %%.3f", System.GetCurrTime(), n, h.x, h.y, f.x, f.y)) end end; Script.SetTimer(50, k) end; k()')
STEP_OFF = 'W222S = (W222S or 0) + 1'


def s_chase(r, chaser, secs=float(os.environ.get('KCDMP_RIG_W222_CHASE_S', '420'))):
    """Scene 4: a bandit is brought low on the host (health 14, stamina 5) and struck by the HOST until the game's own flee takes it
    (its soul's combat_flee context: the game decides a flight when a low-health enemy is struck, and the host's blows land far more
    often than the joiner's on the bench); then <chaser> ('joiner' | 'host') follows it (a step behind it on his own screen, turned to
    it) and swings until it is dead on the host. Frames of the flight on both screens. Pass: it fled and it died."""
    def fn():
        S = _S()
        import collections, scen217
        t = r.a.ask(S.FIND_BANDIT1, 'W210B', 6)
        if not t or t == 'none': return False, 'no-bandit', t
        bandit, x, y, z = t.split()
        x, y = float(x), float(y)
        me = r.b if chaser == 'joiner' else r.a
        place_near(r.a, bandit, 1.8, 0.0)
        if chaser == 'joiner': place_near(r.b, bandit, 3.0, 1.5)
        else: place_near(r.b, bandit, 7.0, 4.0)
        for c in (r.a, r.b): c.keep_up(); c.lua(DRAW)
        time.sleep(2); r.a.lua(S.FACE_TO % bandit); me.lua(S.FACE_TO % bandit)
        tgt = 'player'   # it fights the host until it breaks
        ma, mb = r.a.mark(), r.b.mark()
        r.a.lua(S.ATTACK % (bandit, tgt))
        sw = {'sent': 0, 'refused': 0, 'err': None}
        cur = ['host']   # who swings now: the host until the flight, then the chaser
        stop = threading.Event()
        th = swinger(r, 'host', sw, stop, 'wo222-chase-break'); th.start()
        time.sleep(8)
        fled_at = None; low_tries = 0; t0 = time.time(); frames = []; dead = False; hops = 0

        def restart(who):
            nonlocal stop, th
            stop.set(); th.join(timeout=10)
            cur[0] = who
            stop = threading.Event(); th = swinger(r, who, sw, stop, 'wo222-chase-' + who); th.start()

        def pause_for_frame(label):
            nonlocal stop, th
            stop.set(); th.join(timeout=10)
            frames.append(scen217.frame_pair(r, label, bandit, move=True))
            stop = threading.Event(); th = swinger(r, cur[0], sw, stop, 'wo222-chase-' + cur[0]); th.start()

        while time.time() - t0 < secs:
            if fled_at is None:
                f = r.a.ask(FLEE_Q % bandit, 'W222F', 3) or '?'
                if 'true' in f:
                    note(r, 'flee-kind', contexts=f)
                    fled_at = time.time(); note(r, 'fled', bandit=bandit, after_s=round(fled_at - t0, 1))
                    rig.say('  chase: %s flees (%s)' % (bandit, f))
                    for c in (r.a, r.b): c.lua(STEP_ON % bandit)
                    cur[0] = chaser   # from now on the chaser swings
                    pause_for_frame('chase-%s-flee' % chaser)
                else:
                    # the game decides a flight or a surrender when it is STRUCK at low health (handleStimulusHit): the chaser keeps at it
                    try: dd = float(r.a.ask(DIST_Q % bandit, 'W222D', 3) or -1)
                    except ValueError: dd = -1.0
                    if dd < 0 or dd > 3.0:
                        stop.set(); th.join(timeout=10)
                        place_near(r.a, bandit, 1.8, 0.0); r.a.lua(DRAW); time.sleep(1.5); r.a.lua(S.FACE_TO % bandit)
                        restart('host')
                    r.a.lua_nowait(S.FACE_TO % bandit)
                    if low_tries < 40:
                        r.a.lua(S.SET_STATE % (bandit, 'health', 14.0)); r.a.lua(S.SET_STATE % (bandit, 'stamina', 5.0)); low_tries += 1
                    if r.a.ask(CM_Q % bandit, 'W222C', 3) != 'true': r.a.lua(S.ATTACK % (bandit, tgt))
            else:
                # the chaser follows the body on his own screen: a step behind it, turned to it (the bench's chase: a hop)
                p = me.ask(POS_Q % bandit, 'W222P', 3)
                if p and p != 'none':
                    bx, by, bz = map(float, p.split())
                    me.lua(FOLLOW % (bx, by, bx, by, bz))
                    me.lua_nowait(S.FACE_TO % bandit); hops += 1
                if len(frames) < 2 and time.time() - fled_at > 6: pause_for_frame('chase-%s-run' % chaser)
            dead = r.a.ask(S.DEAD_Q % bandit, 'W210D', 3) == 'true'
            if dead: break
            time.sleep(1.2)
        stop.set(); th.join(timeout=10)
        for c in (r.a, r.b): c.stop_keep(); c.lua(STEP_OFF)
        time.sleep(5)
        jdead = r.b.ask(S.DEAD_Q % bandit, 'W210D', 3) == 'true'
        if dead and len(frames) < 3: frames.append(scen217.frame_pair(r, 'chase-%s-dead' % chaser, bandit, move=True))
        swings = [l for l in r.b.lines(r.b.nlog, mb['n']) if 'W222-SWING ' in l]
        reasons = collections.Counter(m.group(1) for l in swings for m in [re.search(r'reason=(\S+)', l)] if m)
        d = {'chaser': chaser, 'bandit': bandit, 'fled': fled_at is not None, 'fled_after_s': round(fled_at - t0, 1) if fled_at else None,
             'dead_host': dead, 'dead_joiner': jdead, 'hops': hops, 'swings_sent': sw['sent'], 'swing_err': sw['err'],
             'joiner_swing_reasons': dict(reasons),
             'host_blows_on_it': count(r.a, ma['n'], 'W222-BLOW by=%s npc=%s ' % (chaser, bandit)), 'frames': frames}
        note(r, 'chase', **d)
        rig.say('  chase', d)
        if fled_at is None: return False, 'never-fled', d
        return dead, ('ok' if dead else 'not-killed'), d
    return fn


TOUCH_SWITCHES = os.environ.get('KCDMP_RIG_W222_TOUCH', 'base,mp_copy_hit_local_none,mp_npc_native_write,mp_drive,mp_anim_defence,mp_copy_blocks,'
                                'mp_npc_reactions,mp_copy_engine_state,mp_copy_guard_engine,base').split(',')


def s_touch(r, window=float(os.environ.get('KCDMP_RIG_W222_TOUCH_S', '50'))):
    """E (research): the joiner swings at one engaged copy in windows of <window> s; in each window one of the joiner's switches is off
    (its console command, put back after). Per window: his attacks, touches and the swing log's reasons. A switch whose window touches
    far more than the others' is what keeps his blade from the copy."""
    def fn():
        S = _S()
        t = r.a.ask(S.FIND_BANDIT1, 'W210B', 6)
        if not t or t == 'none': return False, 'no-bandit', t
        bandit = t.split()[0]
        place_near(r.a, bandit, 9.0, 3.0)
        place_near(r.b, bandit, 1.8, 0.0)
        for c in (r.a, r.b): c.keep_up()
        r.b.lua(DRAW); time.sleep(2); r.b.lua(S.FACE_TO % bandit)
        r.a.lua(KEEP_NPC % (bandit, 35.0, 90.0, ''))
        r.a.lua(S.ATTACK % (bandit, S.FIGURE))
        rows = []
        for sw in TOUCH_SWITCHES:
            if sw != 'base': r.b.cmd(sw + ' off'); time.sleep(1.5)
            off = r.b.size(r.b.nlog)
            sent = {'sent': 0, 'refused': 0, 'err': None}
            stop = threading.Event()
            th = swinger(r, 'joiner', sent, stop, 'wo222-touch'); th.start()
            t0 = time.time()
            while time.time() - t0 < window:
                time.sleep(8)
                r.b.lua_nowait(S.FACE_TO % bandit)
                try: d = float(r.b.ask(DIST_Q % bandit, 'W222D', 3) or -1)
                except ValueError: d = -1.0
                if d < 0 or d > 3.0:
                    stop.set(); th.join(timeout=10)
                    place_near(r.b, bandit, 1.8, 0.0); r.b.lua(DRAW); time.sleep(1.5); r.b.lua(S.FACE_TO % bandit)
                    stop = threading.Event(); th = swinger(r, 'joiner', sent, stop, 'wo222-touch'); th.start()
                if r.a.ask(CM_Q % bandit, 'W222C', 3) != 'true': r.a.lua(S.ATTACK % (bandit, S.FIGURE))
            stop.set(); th.join(timeout=10)
            if sw != 'base': r.b.cmd(sw + ' on'); time.sleep(1.5)
            time.sleep(2)
            lines = [l for l in r.b.lines(r.b.nlog, off) if 'W222-SWING ' in l]
            reasons = {}
            for l in lines:
                m = re.search(r'reason=(\S+)', l)
                if m: reasons[m.group(1)] = reasons.get(m.group(1), 0) + 1
            row = {'off': sw, 'attacks': len(lines), 'touched': sum(v for k, v in reasons.items() if k.startswith('touched')), 'reasons': reasons, 'clicks': sent['sent']}
            rows.append(row); note(r, 'touch', **row); rig.say('  touch window', row)
        r.a.lua(STOP_KEEP_NPC)
        for c in (r.a, r.b): c.stop_keep()
        return True, 'ok', {'bandit': bandit, 'windows': rows}
    return fn


def plans(r, name, args):
    S = _S()
    import scen217
    day = ('day', scen217.daytime(r, 10.0))   # the bench's save is at night: frames need the day
    return {
        'w222ref': [('join', S.s_join(r)), day, ('ref', s_ref(r))],
        'w222camp': [('join', S.s_join(r)), day, ('camp', s_camp(r))],
        'w222touch': [('join', S.s_join(r)), day, ('touch', s_touch(r))],
        'w222def': [('join', S.s_join(r)), day, ('defence', S.s_defence(r, attackers=2))],
        'w222chase': [('join', S.s_join(r)), day, ('chase-joiner', s_chase(r, 'joiner')), ('chase-host', s_chase(r, 'host'))],
        'w222scenes': [('join', S.s_join(r)), day, ('ref', s_ref(r)), ('camp', s_camp(r)), ('defence', S.s_defence(r, attackers=2)),
                       ('chase-joiner', s_chase(r, 'joiner')), ('chase-host', s_chase(r, 'host'))],
    }.get(name)


CONTINUE_ON_FAIL = ('w222touch', 'w222ref', 'w222camp', 'w222def', 'w222chase', 'w222scenes')
