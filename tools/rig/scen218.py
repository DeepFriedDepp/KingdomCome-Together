# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-218: the release candidate's bench scenes on two real copies (test tool, never shipped).

Every action goes through the game's console, Lua or our pipe (the agent); keys only where a player's own key is the thing under test
(the maintainer allows the bench to drive the keyboard and mouse; send-only, tools/rig/keys.py). Nothing here reads input. Window
management (a rig-owned copy's window minimised or restored) touches only processes the rig itself started (front.py's rule).

Steps (each returns (ok, reason, detail)):
    host_throttle(r, on)   A.0: the host's window minimised and its frame rate capped at 5 fps (the engine's own inactive-window
                           throttle: sys_SleepIfInactiveWH 1, sys_MaxFPSThrotteled 5) -- the joiner alone on the GPU, as far as one PC allows
    hold(r, secs, label)   both players stay where they are for <secs> (the measure window)
    cost(r, on)            the DLL's per-task frame-cost meter on both copies (MAIN-COST lines: the pipe's items a frame, our time a frame)
"""
import json, os, sys, time
import ctypes, ctypes.wintypes as W

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rig


def _S():
    import scenarios
    return scenarios


def _T():
    import scen217
    return scen217


def note(r, _ev, **kw):
    rig.note(_ev, **{('item_kind' if k in ('kind', 'event') else k): v for k, v in kw.items()})
    with open(os.path.join(r.dir, 'w218-events.jsonl'), 'a', encoding='utf-8') as f:
        f.write(json.dumps(dict(ev=_ev, t=round(time.time(), 3), **kw)) + '\n')


def _window(c):
    import front, procs
    rec = _S().rec_of(c)
    if not rec or not procs.matches(rec): return None
    return front.main_window(rec['pid'])


def host_throttle(r, on):
    def fn():
        U = ctypes.windll.user32
        h = _window(r.a)
        if on:
            r.a.cmd('sys_MaxFPSThrotteled 5')
            r.a.cmd('sys_SleepIfInactiveWH 1')
            if h: U.ShowWindow(h, 6)   # SW_MINIMIZE (a rig-owned copy only)
        else:
            if h: U.ShowWindow(h, 9)   # SW_RESTORE
            r.a.cmd('sys_SleepIfInactiveWH 0')
            r.a.cmd('sys_MaxFPSThrotteled 60')
        time.sleep(5)
        note(r, 'w218-throttle', on=on, window=bool(h))
        return True, 'ok', {'on': on, 'window': bool(h)}
    return fn


def hold(r, secs, label):
    def fn():
        t0 = time.time()
        time.sleep(secs)
        note(r, 'w218-hold', label=label, t0_ms=int(t0 * 1000), t1_ms=int(time.time() * 1000))
        return True, 'ok', {'label': label, 'secs': secs}
    return fn


def cost(r, on):
    """The cost meter on both copies, asked again until each game's log says it took (right after a join the joiner's console can stay
    unanswered for half a minute: A.0's joiner never got its first ask)."""
    def fn():
        w = 'on' if on else 'off'
        took = {}
        for c in (r.a, r.b):
            took[c.id] = False
            for _ in range(5):
                m = c.mark()
                c.cmd('mp_main_cost %s' % w)
                if c.wait('k', m['k'], r'WO151-TOGGLE mp_main_cost main_cost=%s' % w, 12): took[c.id] = True; break
        return True, 'ok', {'on': on, 'took': took}
    return fn


WALKER = ('W218WALK = (W218WALK or 0) + 1; local gen, d = W218WALK, 0; local cx, cy, rr, len = %.3f, %.3f, %.1f, %.1f; '
          'local function step() if gen ~= W218WALK or d >= len then return end; local ft = 0.016; '
          'pcall(function() ft = System.GetFrameTime() end); if type(ft) ~= "number" or ft <= 0 or ft > 0.1 then ft = 0.016 end; '
          'd = d + 1.4 * ft; local a = d / rr; local x, y = cx + rr * math.cos(a), cy + rr * math.sin(a); '
          'local z = System.GetTerrainElevation({x=x, y=y, z=0}); player:SetWorldPos({x=x, y=y, z=z + 0.05}); '
          'Script.SetTimer(1, step) end; step()')


def village_walk(r, secs, radius=25.0):
    """B.4: the scripted partner (the joiner's player) walks a 25 m circle through the village's middle for <secs> at walking pace
    (a position every frame, as a walking player's), the host standing at the circle's centre."""
    def fn():
        T = _T()
        cx, cy = T.HOME
        r.a.teleport(cx, cy)
        time.sleep(3)
        r.b.lua(WALKER % (cx, cy, radius, secs * 1.4))
        t0 = time.time()
        time.sleep(secs)
        r.b.lua('W218WALK = (W218WALK or 0) + 1')
        note(r, 'w218-walk', secs=secs, t0_ms=int(t0 * 1000), t1_ms=int(time.time() * 1000))
        return True, 'ok', {'secs': secs}
    return fn


def until_minute(r, t_start, minute, label):
    """B.4: the village walk until <minute> minutes after the soak's start (the soak's length holds whatever the scenes took)."""
    def fn():
        left = t_start[0] + minute * 60 - time.time()
        if left <= 5: return True, 'ok', {'label': label, 'walked_s': 0}
        return village_walk(r, left)()
    return fn


def soak_start(r, t_start):
    def fn():
        t_start[0] = time.time()
        note(r, 'w218-soak', at='start', t_ms=int(t_start[0] * 1000))
        for c in (r.a, r.b): c.cmd('mp_w218_status')
        return True, 'ok', {'t_ms': int(t_start[0] * 1000)}
    return fn


def soak_end(r, t_start):
    def fn():
        t1 = time.time()
        note(r, 'w218-soak', at='end', t_ms=int(t1 * 1000), minutes=round((t1 - t_start[0]) / 60, 1))
        for c in (r.a, r.b): c.cmd('mp_w218_status')
        return True, 'ok', {'minutes': round((t1 - t_start[0]) / 60, 1)}
    return fn


def plans(r, name, args):
    S, T = _S(), _T()
    t_start = [time.time()]
    soak_min = float(os.environ.get('KCDMP_RIG_SOAK_MIN', '60'))
    return {
        # A's test: the village's talks (both roles), the packed tavern 10 min, the brawl 80 s (its falls and get-ups)
        'w218a': [('join', S.s_join(r)), ('cost-on', cost(r, True)), ('day', T.daytime(r, 10.0)),
                  ('talks-joiner', T.p0_role(r, 'joiner')), ('talks-host', T.p0_role(r, 'host')),
                  ('tavern', T.p0_sitters(r, secs=float(os.environ.get('KCDMP_RIG_SIT_S', '600')))),
                  ('watch-on', S.s_w215_watch(r, True)), ('brawl', S.s_w216_brawl(r)), ('frame-brawl', T.brawl_pair(r)),
                  ('watch-off', S.s_w215_watch(r, False)), ('cost-off', cost(r, False))],
        # the acceptance: every default as it ships (or every WO-218 switch off: the twin, KCDMP_RIG_ACC_SOAK=0 leaves the soak out), the
        # field's link shape; the caravan and the corpses first (the save's caravan runs only on the first morning), then the talks with a
        # sale, the smith's house, the tavern, the brawl and the far ghost, the loot, the drops, the doors and the loose items, the ambush;
        # then the 60-minute soak (B.4)
        'w218acc': ([('join', S.s_join(r)), ('cost-on', cost(r, True)), ('day', T.daytime(r, 10.0)), ('s8-caravan', T.caravan_pass(r)), ('s7-corpses', T.corpses(r)),
                     ('s2-talks-joiner', T.p0_role(r, 'joiner')), ('s2-talks-host', T.p0_role(r, 'host')),
                     ('s3-smithy', T.smithy(r)),
                     ('s1-tavern', T.p0_sitters(r)), ('s1-frame', T.sitter_frame(r)),
                     ('evening', T.daytime(r, 21.0)), ('watch-on', S.s_w215_watch(r, True)), ('s4-brawl', S.s_w216_brawl(r)), ('s4-frame', T.brawl_pair(r)),
                     ('settle', lambda: (time.sleep(15), (True, 'ok', {}))[1]), ('s11-ghost-far', T.ghost_far(r)),
                     ('s5-ambush', S.s_w216_ambush(r)), ('watch-off', S.s_w215_watch(r, False)),
                     ('day2', T.daytime(r, 10.0)), ('s6-loot', T.loot_scene(r)),
                     ('s10-drops', T.drops(r, 'acc')), ('s9-doors', T.doors(r)), ('s9-loose', T.loose_items(r))]
                    + ([('soak-start', soak_start(r, t_start)),
                        ('walk-1', village_walk(r, 300)), ('soak-talks-joiner', T.p0_role(r, 'joiner')), ('soak-talks-host', T.p0_role(r, 'host')),
                        ('walk-2', until_minute(r, t_start, 15, 'walk-2')),
                        ('watch-on2', S.s_w215_watch(r, True)), ('fight-1', S.s_w216_brawl(r)), ('watch-off2', S.s_w215_watch(r, False)),
                        ('walk-3', until_minute(r, t_start, 25, 'walk-3')), ('soak-loot', S.s_loot(r)),
                        ('walk-4', until_minute(r, t_start, 35, 'walk-4')), ('sleep-vote', S.s_skip(r, 'sleep', 9, gap_gate=False)),
                        ('walk-5', until_minute(r, t_start, 45, 'walk-5')),
                        ('watch-on3', S.s_w215_watch(r, True)), ('fight-2', S.s_w216_brawl(r)), ('watch-off3', S.s_w215_watch(r, False)),
                        ('walk-6', until_minute(r, t_start, soak_min, 'walk-6')), ('soak-end', soak_end(r, t_start))]
                       if os.environ.get('KCDMP_RIG_ACC_SOAK', '1') != '0' else [])
                    + [('cost-off', cost(r, False))]),
        # C.3 / D.1 / B.2 check: the smith's house (A's three windows: placed-then-lost), the doors (26 watched, 10 uses), the cost meter on
        'w218cd': [('join', S.s_join(r)), ('cost-on', cost(r, True)), ('smithy', T.smithy(r)), ('day', T.daytime(r, 10.0)), ('doors', T.doors(r)),
                   ('cost-off', cost(r, False))],
        # B.1: 30 minutes of staged fights in one session (WO-209's crash gate): the host and the joiner against two or three bandits,
        # the joiner swinging (send-only keys), the copies attacking him, five minutes each; any game gone fails the run
        'w218fights': [('join', S.s_join(r)), ('cost-on', cost(r, True))] + [('fight-%02d' % (i + 1), S.s_c1fight(r)) for i in range(int(os.environ.get('KCDMP_RIG_C1_N', '6')))],
        # the release build's own check (b6: the lane per body, the 60 m door sweep): A's test, then the doors watched 5 minutes
        'w218a2': [('join', S.s_join(r)), ('cost-on', cost(r, True)), ('day', T.daytime(r, 10.0)),
                   ('talks-joiner', T.p0_role(r, 'joiner')), ('talks-host', T.p0_role(r, 'host')),
                   ('tavern', T.p0_sitters(r, secs=float(os.environ.get('KCDMP_RIG_SIT_S', '600')))),
                   ('watch-on', S.s_w215_watch(r, True)), ('brawl', S.s_w216_brawl(r)), ('watch-off', S.s_w215_watch(r, False)),
                   ('day2', T.daytime(r, 10.0)), ('doors', T.doors(r)), ('cost-off', cost(r, False))],
        # B.4: 60 minutes joined -- the partner walking the village, two fights, one talk each, one loot, one sleep vote
        'w218soak': [('join', S.s_join(r)), ('cost-on', cost(r, True)), ('day', T.daytime(r, 10.0)), ('soak-start', soak_start(r, t_start)),
                     ('walk-1', village_walk(r, 300)), ('talks-joiner', T.p0_role(r, 'joiner')), ('talks-host', T.p0_role(r, 'host')),
                     ('walk-2', until_minute(r, t_start, 15, 'walk-2')),
                     ('watch-on', S.s_w215_watch(r, True)), ('fight-1', S.s_w216_brawl(r)), ('watch-off', S.s_w215_watch(r, False)),
                     ('walk-3', until_minute(r, t_start, 25, 'walk-3')), ('loot', S.s_loot(r)),
                     ('walk-4', until_minute(r, t_start, 35, 'walk-4')), ('sleep-vote', S.s_skip(r, 'sleep', 9, gap_gate=False)),
                     ('walk-5', until_minute(r, t_start, 45, 'walk-5')),
                     ('watch-on2', S.s_w215_watch(r, True)), ('fight-2', S.s_w216_brawl(r)), ('watch-off2', S.s_w215_watch(r, False)),
                     ('walk-6', until_minute(r, t_start, soak_min, 'walk-6')), ('soak-end', soak_end(r, t_start)), ('cost-off', cost(r, False))],
        # A.0: the packed tavern 5 min (both games at full rate), then 5 min with the host minimised and capped at 5 fps, then the brawl
        'w218a0': [('join', S.s_join(r)), ('cost-on', cost(r, True)),
                   ('tavern', T.p0_sitters(r, secs=float(os.environ.get('KCDMP_RIG_A0_S', '300')))),
                   ('throttle-on', host_throttle(r, True)), ('tavern-alone', hold(r, float(os.environ.get('KCDMP_RIG_A0_S', '300')), 'tavern-alone')),
                   ('throttle-off', host_throttle(r, False)),
                   ('watch-on', S.s_w215_watch(r, True)), ('brawl', S.s_w216_brawl(r)), ('watch-off', S.s_w215_watch(r, False)),
                   ('cost-off', cost(r, False))],
    }.get(name)


CONTINUE_ON_FAIL = ('w218a0', 'w218a', 'w218soak', 'w218fights', 'w218cd', 'w218acc', 'w218a2')
