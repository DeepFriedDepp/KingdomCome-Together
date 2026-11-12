# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-220: the scoreboard -- how far the joiner's screen is from the host's, as one table and one line (test tool, never shipped).

FIXED. It reads one run of the scenario `score` (tools/rig/scen220.py) and nothing else decides a work order's result. No work order
changes the measure it is judged by; a change here is its own work order and re-runs the base line (docs/WO-220-findings.md).

    python tools/rig/scoreboard.py <run folder>                    one table and the SCORE line (also <run folder>/score.json, score.md)
    python tools/rig/scoreboard.py --spread <run> <run> [<run>..]  several runs of one build side by side, their mean and spread
    python tools/rig/scoreboard.py <run folder> --all              (a run of another scenario: the whole run as one window)
    python tools/rig/scoreboard.py <scenes run> --works <checks run>   the works-at-all checks from their own run (scenario score-works);
                                                                   with --spread: <scenes run>+<checks run>

A run folder is what tools/rig/pair.py collect leaves: bundles/<tag>-A (the host's files), bundles/<tag>-B (the joiner's), and the
scenario's own folder (score-windows.json, score-works.json, bones-sync.json). Both games run on one PC: one clock.

What is measured, over the scenario's four scenes (the smith's house, the village walk, the tavern, the staged fight):

place   Every sample the joiner's probe took of a body that is one of the host's NPCs (by name), against the host's own probe samples
        of that NPC around the same moment (linear between the host's two nearest samples, within 0.6 s): the horizontal distance.
        Shares within 0.5 m, within 1 m, over 2 m -- in all, by the copy's distance from the joiner's player (under 15 m, 15-40 m,
        beyond), and by what the host's NPC is doing (stands, walks, anything else: its full-body action's name).
thing   The share of those samples where the copy's full-body action is the host NPC's. Variations of one activity count as the same
        (FooVAR1 / Foo_Loop = Foo); a walk's start and stop count as the walk. A strict measure: a copy 0.4 s late is different for 0.4 s.
step    For every body that walks (over 0.5 m/s for 4 s or more) on both games during the village walk: its left foot's step period
        (autocorrelation of the foot's forward offset from the hips, 20 Hz) on the joiner over the same on the host -- the median over
        bodies. slide: the planted foot's speed over the ground (the slowest third of its samples: it should stand still) as a share of
        the body's speed -- the joiner's median; the host's own walkers' beside it. A bone line without joints is no sample.
pulls   Per body-minute: the 10 s windows of one copy (the joiner's MP-NPCPULL lines) that held a single-frame pull over 50 cm.
flips   Per body-minute: those windows with a pull over 5 m in which the copy moved in over 40 % of its frames.
        (A build that moves a copy by another mechanism than the position writer logs the same line for it: the largest single-frame
        jump of the body, its frames, the frames it moved in.)
lanes   On the joiner: the frame processor's lag and the activity lane's age (MP-W207-Q lag_ms / act_lag_ms), p50 / p99; the position
        writer's play-out allowance (MP-NPCPULL jitter_allow_ms). lane99 = the larger p99 of the first two.
cost    Our main-thread time a frame on both machines (the DLL's FRAME lines): the mean, and the 95th percentile (the mean of the
        one-minute windows' own p95). cost95 = the host's. And the frame rate each game held.
works   Ten checks, yes / no / not run: the join; a talk to its choices; a trade screen; a forced dialogue; a loot take; a door use;
        a drop picked up by the other; a knockdown and get-up; a mount; a sleep vote.

    SCORE place=<% within 0.5 m> thing=<% the same activity> step=<joiner/host step period> slide=<% of body speed> pulls=<per
          body-minute> flips=<per body-minute> lane99=<ms> cost95=<ms> works=<n>/10 [EXITED=<host|joiner>@<step>]
(EXITED is there only when a game exited during the run: the rig's step that found it gone.)
"""
import bisect, collections, glob, json, math, os, re, sys, time

MAXDT = 0.6
CHECKS = ('join', 'talk', 'trade', 'forced', 'loot', 'door', 'drop', 'knockdown', 'mount', 'sleep')
CHECK_TEXT = {'join': 'the join', 'talk': 'a talk to its choices', 'trade': 'a trade screen', 'forced': 'a forced dialogue', 'loot': 'a loot take',
              'door': 'a door use', 'drop': 'a drop picked up by the other', 'knockdown': 'a knockdown and get-up', 'mount': 'a mount',
              'sleep': 'a sleep vote'}
SCENES = ('smithy', 'village', 'tavern', 'fight')


# ---------------------------------------------------------------- the run's files

def find_run(run, host='A'):
    b = os.path.join(run, 'bundles')
    tags = sorted(d for d in os.listdir(b) if os.path.isdir(os.path.join(b, d)) and not d.endswith('.packed')) if os.path.isdir(b) else []
    # the newest collect that is not a failure folder's
    full = [d for d in tags if not d.startswith('fail-')] or tags
    ha = [d for d in full if d.endswith('-' + host)]
    jo = [d for d in full if d.endswith('-' + ('B' if host == 'A' else 'A'))]
    if not ha or not jo: raise SystemExit('no bundles in %s (pair.py collect)' % run)
    pick = lambda v: os.path.join(b, max(v, key=lambda d: os.path.getmtime(os.path.join(b, d))))
    scen = sorted(glob.glob(os.path.join(run, 'scenario-score-*')), key=os.path.getmtime)
    return {'host': pick(ha), 'joiner': pick(jo), 'scen': scen[-1] if scen else None}


def jload(p, default=None):
    if not p or not os.path.exists(p): return default
    with open(p, encoding='utf-8') as f: return json.load(f)


def sod(ms):
    """Wall ms -> seconds of the local day (the logs' own clock)."""
    lt = time.localtime(ms / 1000.0)
    return lt.tm_hour * 3600 + lt.tm_min * 60 + lt.tm_sec + (ms % 1000) / 1000.0


def pct(v, q):
    if not v: return None
    v = sorted(v)
    return v[min(len(v) - 1, int(round(q * (len(v) - 1))))]


def med(v):
    v = sorted(x for x in v if x is not None)
    return v[len(v) // 2] if v else None


def in_windows(t, wins):
    for lab, t0, t1 in wins:
        if t0 <= t <= t1: return lab
    return None


# ---------------------------------------------------------------- place and thing

def stem(fn):
    if fn in ('IdleToMove', 'MoveToIdle'): return 'MotionMovement'
    f = re.sub(r'_?VAR[A-Za-z0-9]*$', '', fn)
    f = re.sub(r'_?Loop$', '', f)
    return f or fn


def doing(fn):
    if fn in ('MotionIdle', 'MotionTurn', 'MotionTurnBig', '-', '?'): return 'stands'
    if fn == 'MotionMovement': return 'walks'
    return 'anything else'


def probe_rows(bundle):
    for name in ('kcdmp-anim-session.1.jsonl', 'kcdmp-anim-session.jsonl'):
        p = os.path.join(bundle, name)
        if not os.path.exists(p): continue
        with open(p, encoding='utf-8', errors='replace') as f:
            for line in f:
                try: o = json.loads(line)
                except ValueError: continue
                t = o.get('t')
                for a in o.get('a', []): yield t, a


def full_body(a):
    for s in a.get('sc', []):
        if s.get('s') == 'FullBody': return stem(s.get('fn') or '?')
    return '-'


def place_thing(run, wins):
    host = collections.defaultdict(list)
    for t, a in probe_rows(run['host']):
        if a.get('k') == 'npc' and 'p' in a: host[a['n']].append((t, a['p'][0], a['p'][1], full_body(a), a.get('dead')))
    for v in host.values(): v.sort()
    ht = {n: [r[0] for r in v] for n, v in host.items()}
    jplayer, jrows = [], []
    for t, a in probe_rows(run['joiner']):
        k = a.get('k')
        if k == 'player': jplayer.append((t, a['p'][0], a['p'][1]))
        elif k in ('copy', 'npc') and 'p' in a: jrows.append((t, a))
    jplayer.sort(); jpt = [r[0] for r in jplayer]

    def host_at(name, t):
        ts, arr = ht.get(name), host.get(name)
        if not ts: return None
        i = bisect.bisect_left(ts, t)
        lo = arr[i - 1] if i > 0 else None
        hi = arr[i] if i < len(arr) else None
        nb = None
        for r_ in (lo, hi):
            if r_ is not None and (nb is None or abs(r_[0] - t) < abs(nb[0] - t)): nb = r_
        if nb is None or abs(nb[0] - t) > MAXDT * 1000: return None
        if lo is not None and hi is not None and hi[0] > lo[0] and (hi[0] - lo[0]) <= 2 * MAXDT * 1000:
            u = (t - lo[0]) / (hi[0] - lo[0])
            return lo[1] + (hi[1] - lo[1]) * u, lo[2] + (hi[2] - lo[2]) * u, nb[3], nb[4]
        return nb[1], nb[2], nb[3], nb[4]

    stats = collections.defaultdict(lambda: dict(n=0, d=[], same=0))
    miss = collections.Counter()
    unmatched = 0
    bodies = set()
    for t, a in jrows:
        lab = in_windows(t, wins)
        if lab is None: continue
        h = host_at(a['n'], t)
        if h is None: unmatched += 1; continue
        hx, hy, hfn, hdead = h
        if hdead or a.get('dead'): continue
        if not jpt: continue
        i = bisect.bisect_left(jpt, t)
        jp = min((jplayer[j] for j in (i - 1, i) if 0 <= j < len(jplayer)), key=lambda r_: abs(r_[0] - t))
        dj = math.hypot(a['p'][0] - jp[1], a['p'][1] - jp[2])
        d = math.hypot(a['p'][0] - hx, a['p'][1] - hy)
        fn = full_body(a)
        band = 'under 15 m' if dj < 15 else '15-40 m' if dj < 40 else 'beyond 40 m'
        bodies.add(a['n'])
        for key in ('all', 'band:' + band, 'doing:' + doing(hfn), 'scene:' + lab):
            s = stats[key]; s['n'] += 1; s['d'].append(d)
            if fn == hfn: s['same'] += 1
        if fn != hfn and dj < 40: miss[(hfn, fn)] += 1
    out = {}
    for key, s in stats.items():
        n, d = s['n'], s['d']
        out[key] = {'samples': n, 'within_05': 100.0 * sum(1 for x in d if x <= 0.5) / n, 'within_1': 100.0 * sum(1 for x in d if x <= 1.0) / n,
                    'over_2': 100.0 * sum(1 for x in d if x > 2.0) / n, 'p50_m': pct(d, .5), 'p90_m': pct(d, .9), 'same_thing': 100.0 * s['same'] / n}
    return {'rows': out, 'bodies': len(bodies), 'no_host_sample': unmatched,
            'commonest_differences': [{'host': h, 'copy': j, 'samples': n} for (h, j), n in miss.most_common(12)]}


# ---------------------------------------------------------------- step and slide

BONE = re.compile(r'\[W2(?:06|19)B\] (\d+\.\d+) (\S+) (.*)$')
PART = re.compile(r'(\w+)=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)')
MIN_WALK_S = 4.0


def bone_rows(log, off, end=None):
    per = collections.defaultdict(list)
    empty = 0
    if not os.path.exists(log): return per, empty
    with open(log, 'rb') as f:
        f.seek(off)
        data = f.read(end - off) if end else f.read()
    for raw in data.decode('utf-8', 'replace').splitlines():
        m = BONE.search(raw)
        if not m: continue
        b = {k: (float(x), float(y), float(z)) for k, x, y, z in PART.findall(m.group(3))}
        if 'root' in b and 'lfoot' in b: per[m.group(2)].append((float(m.group(1)), b))
        else: empty += 1          # a row without the joints is no sample
    for v in per.values(): v.sort(key=lambda r_: r_[0])
    return per, empty


def stretches(rows):
    out, cur = [], []
    for i in range(len(rows)):
        j = i
        while j > 0 and rows[i][0] - rows[j][0] < 0.5: j -= 1
        dt = rows[i][0] - rows[j][0]
        if dt <= 0.2: sp, d = 0.0, (0.0, 0.0)
        else:
            dx, dy = rows[i][1]['root'][0] - rows[j][1]['root'][0], rows[i][1]['root'][1] - rows[j][1]['root'][1]
            sp = math.hypot(dx, dy) / dt
            d = (dx / (sp * dt), dy / (sp * dt)) if sp > 0 else (0.0, 0.0)
        gap = cur and rows[i][0] - cur[-1][0] > 0.8
        if sp > 0.5 and not gap: cur.append((rows[i][0], rows[i][1], sp, d))
        else:
            if cur and cur[-1][0] - cur[0][0] >= MIN_WALK_S: out.append(cur)
            cur = [(rows[i][0], rows[i][1], sp, d)] if sp > 0.5 else []
    if cur and cur[-1][0] - cur[0][0] >= MIN_WALK_S: out.append(cur)
    return out


def period(seg):
    t0, t1 = seg[0][0], seg[-1][0]
    n = int((t1 - t0) * 20)
    if n < 40: return None
    xs, k = [], 0

    def off(r_):
        fx, fy = r_[1]['lfoot'][0] - r_[1]['root'][0], r_[1]['lfoot'][1] - r_[1]['root'][1]
        return fx * r_[3][0] + fy * r_[3][1]
    for i in range(n):
        t = t0 + i / 20.0
        while k + 1 < len(seg) and seg[k + 1][0] < t: k += 1
        a, b = seg[k], seg[min(k + 1, len(seg) - 1)]
        u = 0.0 if b[0] <= a[0] else min(1.0, max(0.0, (t - a[0]) / (b[0] - a[0])))
        xs.append(off(a) + (off(b) - off(a)) * u)
    m = sum(xs) / len(xs); xs = [x - m for x in xs]
    var = sum(x * x for x in xs)
    if var <= 1e-6: return None
    top = min(70, n // 2)
    c = {lag: sum(xs[i] * xs[i + lag] for i in range(n - lag)) / var * (n / (n - lag)) for lag in range(7, top + 1)}
    best = max(c[lag] for lag in range(8, top))
    if best <= 0.2: return None                     # no cycle in the foot at all
    for lag in range(8, top):                       # the first strong peak, not a multiple of it
        if c[lag] >= 0.85 * best and c[lag] >= c[lag - 1] and c[lag] >= c[lag + 1]: return lag / 20.0
    return None


def slide(seg):
    v = []
    for a, b in zip(seg, seg[1:]):
        dt = b[0] - a[0]
        if dt <= 0 or dt > 0.3: continue
        v.append(math.hypot(b[1]['lfoot'][0] - a[1]['lfoot'][0], b[1]['lfoot'][1] - a[1]['lfoot'][1]) / dt)
    if len(v) < 10: return None
    v.sort(); third = v[:max(3, len(v) // 3)]
    return third[len(third) // 2]


def step_slide(run):
    sync = jload(os.path.join(run['scen'], 'bones-sync.json')) if run['scen'] else None
    if not sync: return {'error': 'no bone sampler window in this run'}
    per_copy = {}
    info = {}
    for cid, s in sync.items():
        role = s.get('role') or ('host' if cid == sorted(sync)[0] else 'joiner')
        log = os.path.join(run[role], 'kcd.log')
        if not os.path.exists(log): log = s['log']
        per, empty = bone_rows(log, s['off'], s.get('end'))
        rows = collections.defaultdict(list)
        for name, r_ in per.items():
            if name.startswith('kcd2mp_') or name == 'Dude': continue
            for seg in stretches(r_):
                p = period(seg)
                sp = sum(x[2] for x in seg) / len(seg)
                sl = slide(seg)
                if p: rows[name].append((seg[-1][0] - seg[0][0], sp, p, (sl / sp) if sl is not None and sp > 0 else None))
        per_copy[role] = rows
        info[role] = {'bodies_with_joints': len(per), 'rows_without_joints': empty, 'bodies_that_walked': len(rows),
                      'sampler_first': s.get('first'), 'sync_seen': s.get('seen')}
    h, j = per_copy.get('host', {}), per_copy.get('joiner', {})
    both = []
    for name in sorted(set(h) & set(j)):
        hp, jp = med([x[2] for x in h[name]]), med([x[2] for x in j[name]])
        both.append({'body': name, 'host_s': round(sum(x[0] for x in h[name]), 1), 'host_mps': med([x[1] for x in h[name]]), 'host_period_s': hp,
                     'host_slide': med([x[3] for x in h[name]]), 'joiner_s': round(sum(x[0] for x in j[name]), 1), 'joiner_mps': med([x[1] for x in j[name]]),
                     'joiner_period_s': jp, 'joiner_slide': med([x[3] for x in j[name]]), 'ratio': jp / hp})
    own = [x for v in h.values() for x in v]
    return {'info': info, 'bodies': both, 'n': len(both),
            'step': med([b['ratio'] for b in both]), 'host_period_s': med([b['host_period_s'] for b in both]),
            'joiner_period_s': med([b['joiner_period_s'] for b in both]),
            'slide': med([b['joiner_slide'] for b in both]), 'host_slide': med([b['host_slide'] for b in both]),
            'host_own_walkers': {'stretches': len(own), 'period_s': med([x[2] for x in own]), 'slide': med([x[3] for x in own])}}


# ---------------------------------------------------------------- pulls and flips; the lanes; the cost

NAT_T = re.compile(r'^\[(\d\d):(\d\d):(\d\d)\.(\d+)\]')
AG_T = re.compile(r'^(\d\d):(\d\d):(\d\d)\.(\d+) ')


def _lines(path):
    if not os.path.exists(path): return []
    with open(path, encoding='utf-8', errors='replace') as f: return f.read().splitlines()


def _t(m): return int(m.group(1)) * 3600 + int(m.group(2)) * 60 + int(m.group(3)) + float('0.' + m.group(4))


def pulls_flips(run, swins):
    rows = []
    for l in _lines(os.path.join(run['joiner'], 'kcdmp-native.log')):
        if 'MP-NPCPULL' not in l: continue
        m = NAT_T.match(l)
        d = re.search(r'npc=(\S+) .*max_cm=([\d.]+) frames=(\d+) moved_frames=(\d+)', l)
        w = re.search(r'window_s=(\d+)', l); ja = re.search(r'jitter_allow_ms=(\d+)', l)
        if not m or not d: continue
        t = _t(m)
        lab = in_windows(t, swins)
        if lab is None: continue
        rows.append((lab, d.group(1), float(d.group(2)), int(d.group(3)), int(d.group(4)), int(w.group(1)) if w else 10, int(ja.group(1)) if ja else None))
    if not rows: return {'windows': 0, 'body_minutes': 0.0, 'pulls': None, 'flips': None}
    bm = sum(r_[5] for r_ in rows) / 60.0
    pw = [r_ for r_ in rows if r_[2] > 50.0]
    fw = [r_ for r_ in rows if r_[2] > 500.0 and r_[3] and r_[4] / r_[3] > 0.4]
    by = {}
    for lab in sorted({r_[0] for r_ in rows}):
        w = [r_ for r_ in rows if r_[0] == lab]; m_ = sum(r_[5] for r_ in w) / 60.0
        by[lab] = {'windows': len(w), 'body_minutes': round(m_, 1), 'pull_windows': sum(1 for r_ in w if r_[2] > 50.0),
                   'flip_windows': sum(1 for r_ in w if r_[2] > 500.0 and r_[3] and r_[4] / r_[3] > 0.4)}
    worst = collections.Counter(r_[1] for r_ in pw).most_common(5)
    return {'windows': len(rows), 'bodies': len({r_[1] for r_ in rows}), 'body_minutes': round(bm, 1), 'pull_windows': len(pw), 'flip_windows': len(fw),
            'pulls': len(pw) / bm, 'flips': len(fw) / bm, 'by_scene': by, 'most_pulled': worst,
            'playout_ms_p50': pct([r_[6] for r_ in rows if r_[6] is not None], .5), 'playout_ms_p99': pct([r_[6] for r_ in rows if r_[6] is not None], .99)}


def lanes(run, swins):
    fr, act = [], []
    for l in _lines(os.path.join(run['joiner'], 'agent.log')):
        if 'MP-W207-Q ' not in l: continue
        m = AG_T.match(l)
        if not m or in_windows(_t(m), swins) is None: continue
        a = re.search(r' lag_ms=(\d+)', l); b = re.search(r'act_lag_ms=(\d+)', l)
        if a: fr.append(int(a.group(1)))
        if b: act.append(int(b.group(1)))
    out = {'rows': len(fr), 'frame_ms_p50': pct(fr, .5), 'frame_ms_p99': pct(fr, .99), 'activity_ms_p50': pct(act, .5), 'activity_ms_p99': pct(act, .99)}
    v = [x for x in (out['frame_ms_p99'], out['activity_ms_p99']) if x is not None]
    out['lane99'] = max(v) if v else None
    return out


def cost(run, swins):
    out = {}
    for role in ('host', 'joiner'):
        fr = []
        for l in _lines(os.path.join(run[role], 'kcdmp-native.log')):
            if ' FRAME ' not in l: continue
            m = NAT_T.match(l)
            if not m: continue
            t = _t(m)
            d = dict(re.findall(r'(\w+)=([\d.]+)', l))
            # a FRAME line closes a window: it counts when the window lies inside a scene
            w = float(d.get('window_s', 60))
            if in_windows(t, swins) is None or in_windows(t - w, swins) is None: continue
            if 'ours_us_mean' in d: fr.append(d)
        if not fr: out[role] = None; continue
        frames = sum(float(d['frames']) for d in fr)
        out[role] = {'windows': len(fr), 'fps': sum(float(d['fps']) * float(d['frames']) for d in fr) / frames,
                     'fps_min': min(float(d['fps']) for d in fr),
                     'ours_ms_mean': sum(float(d['ours_us_mean']) * float(d['frames']) for d in fr) / frames / 1000.0,
                     'ours_ms_p95': (sum(float(d['ours_us_p95']) for d in fr if 'ours_us_p95' in d) / max(1, sum(1 for d in fr if 'ours_us_p95' in d))) / 1000.0,
                     'ours_ms_p95_worst': max((float(d['ours_us_p95']) for d in fr if 'ours_us_p95' in d), default=0.0) / 1000.0,
                     'faults': sum(int(float(d.get('faults', 0))) for d in fr)}
    return out


# ---------------------------------------------------------------- one run

def works_file(run_dir):
    """The newest score-works.json under a run folder (the checks' own run), and that scenario's result.json."""
    best = None
    for root, _d, files in os.walk(run_dir):
        if 'score-works.json' in files:
            p = os.path.join(root, 'score-works.json')
            if best is None or os.path.getmtime(p) > os.path.getmtime(best): best = p
    return best


def score(run_dir, whole=False, host='A', works_dir=None):
    if '+' in run_dir and not os.path.isdir(run_dir): run_dir, works_dir = run_dir.split('+', 1)
    run = find_run(run_dir, host)
    wj = jload(os.path.join(run['scen'], 'score-windows.json'), []) if run['scen'] else []
    if whole or not wj:
        ts = [t for t, _a in probe_rows(run['joiner'])]
        if not ts: raise SystemExit('no probe samples in the joiner\'s bundle')
        wj = [{'label': 'whole run', 't0_ms': min(ts), 't1_ms': max(ts)}]
    wins = [(w['label'], w['t0_ms'], w['t1_ms']) for w in wj]
    swins = [(lab, sod(t0), sod(t1)) for lab, t0, t1 in wins]
    res = {'run': os.path.basename(os.path.normpath(run_dir)), 'scenes': [{k: w.get(k) for k in ('label', 't0_ms', 't1_ms', 'danger', 'joiner_there')} for w in wj],
           'minutes': round(sum(t1 - t0 for _l, t0, t1 in wins) / 60000.0, 1)}
    res['place_thing'] = place_thing(run, wins)
    res['step_slide'] = step_slide(run)
    res['pulls_flips'] = pulls_flips(run, swins)
    res['lanes'] = lanes(run, swins)
    res['cost'] = cost(run, swins)
    wf = works_file(works_dir) if works_dir else (os.path.join(run['scen'], 'score-works.json') if run['scen'] else None)
    wk = {w['check']: w for w in (jload(wf, []) if wf else [])}
    res['works_run'] = os.path.basename(os.path.normpath(works_dir)) if works_dir else None
    if works_dir and wf:   # the checks' own run: did a game exit there
        wr = jload(os.path.join(os.path.dirname(wf), 'result.json'), {})
        wx = next((st for st in wr.get('steps', []) if st.get('reason') == 'copy-exited'), None)
        res['works_exited'] = {'step': wx['step'], 'copies': (wx.get('detail') or {}).get('gone'),
                               'crash_reports': [os.path.basename(c) for c in (wx.get('detail') or {}).get('crash_reports', [])]} if wx else None
    res['works'] = [{'check': c, 'ok': wk[c]['ok'] if c in wk else None, 'why': wk[c]['why'] if c in wk else 'the run did not reach it'} for c in CHECKS]
    res['gfx'] = jload(os.path.join(run['scen'], 'score-gfx.json')) if run['scen'] else None
    # a game that exited during the run (the rig's own word: a step failed with copy-exited)
    rj = jload(os.path.join(run['scen'], 'result.json'), {}) if run['scen'] else {}
    ex = next((st for st in rj.get('steps', []) if st.get('reason') == 'copy-exited'), None)
    res['exited'] = {'step': ex['step'], 'copies': (ex.get('detail') or {}).get('gone'),
                     'crash_reports': [os.path.basename(c) for c in (ex.get('detail') or {}).get('crash_reports', [])]} if ex else None
    pt = res['place_thing']['rows'].get('all')
    ss, pf, ln, co = res['step_slide'], res['pulls_flips'], res['lanes'], res['cost']
    res['score'] = {'place': pt['within_05'] if pt else None, 'thing': pt['same_thing'] if pt else None,
                    'step': ss.get('step'), 'slide': 100.0 * ss['slide'] if ss.get('slide') is not None else None,
                    'pulls': pf.get('pulls'), 'flips': pf.get('flips'), 'lane99': ln.get('lane99'),
                    'cost95': co['host']['ours_ms_p95'] if co.get('host') else None,
                    'works': sum(1 for w in res['works'] if w['ok'] is True),
                    'exited': ('%s@%s' % ('+'.join('host' if c == host else 'joiner' for c in (res['exited']['copies'] or ['?'])), res['exited']['step'])) if res['exited'] else None}
    return res


def f(v, fmt='%.1f', none='-'):
    return none if v is None else fmt % v


def score_line(s):
    return 'SCORE place=%s thing=%s step=%s slide=%s pulls=%s flips=%s lane99=%s cost95=%s works=%d/10%s' % (
        f(s['place']), f(s['thing']), f(s['step'], '%.2f'), f(s['slide'], '%.0f'), f(s['pulls'], '%.2f'), f(s['flips'], '%.3f'),
        f(s['lane99'], '%.0f'), f(s['cost95'], '%.1f'), s['works'], (' EXITED=' + s['exited']) if s.get('exited') else '')


def table(res):
    o = []
    o.append('## Scoreboard: %s (%s minutes measured)' % (res['run'], res['minutes']))
    o.append('')
    o.append('`%s`' % score_line(res['score']))
    o.append('')
    if res.get('exited'):
        o.append('**A game exited during the run: %s, at the step `%s` (%s). Every check after it is "not run".**' % (
            res['score']['exited'].split('@')[0], res['exited']['step'], ', '.join(res['exited']['crash_reports']) or 'no crash report'))
        o.append('')
    pt = res['place_thing']
    o.append('| the same place, the same thing | samples | within 0.5 m | within 1 m | over 2 m | median m | the same activity |')
    o.append('|---|---:|---:|---:|---:|---:|---:|')
    order = ['all', 'band:under 15 m', 'band:15-40 m', 'band:beyond 40 m', 'doing:stands', 'doing:walks', 'doing:anything else'] + ['scene:' + s for s in SCENES] + ['scene:whole run']
    names = {'all': 'every body (%d bodies)' % pt['bodies'], 'doing:stands': 'the host\'s NPC stands', 'doing:walks': 'the host\'s NPC walks',
             'doing:anything else': 'the host\'s NPC does anything else'}
    for k in order:
        r_ = pt['rows'].get(k)
        if not r_: continue
        nm = names.get(k) or (k.split(':', 1)[1] + (' from the joiner' if k.startswith('band:') else ' (scene)'))
        o.append('| %s | %d | %.1f %% | %.1f %% | %.1f %% | %.2f | %.1f %% |' % (nm, r_['samples'], r_['within_05'], r_['within_1'], r_['over_2'], r_['p50_m'], r_['same_thing']))
    o.append('')
    ss = res['step_slide']
    if ss.get('error'): o.append('The step: %s.' % ss['error'])
    else:
        o.append('| the step (the village walk) | on the host | its copy on the joiner |')
        o.append('|---|---:|---:|')
        o.append('| bodies that walked on both | %d | %d |' % (ss['n'], ss['n']))
        o.append('| median step period | %s s | %s s (x%s) |' % (f(ss['host_period_s'], '%.2f'), f(ss['joiner_period_s'], '%.2f'), f(ss['step'], '%.2f')))
        o.append('| median planted-foot slide, of the body\'s speed | %s %% | %s %% |' % (f(100 * ss['host_slide'] if ss['host_slide'] is not None else None, '%.0f'),
                                                                                         f(100 * ss['slide'] if ss['slide'] is not None else None, '%.0f')))
        hw = ss['host_own_walkers']
        o.append('| every walking stretch of the host\'s own NPCs | %d stretches: %s s, slide %s %% | |' % (hw['stretches'], f(hw['period_s'], '%.2f'),
                                                                                                            f(100 * hw['slide'] if hw['slide'] is not None else None, '%.0f')))
        for role in ('host', 'joiner'):
            i = ss['info'].get(role) or {}
            o.append('| the sampler on the %s | %s bodies with joints, %s that walked, %s rows without joints | %s |' % (
                role, i.get('bodies_with_joints'), i.get('bodies_that_walked'), i.get('rows_without_joints'), i.get('sampler_first') or 'no first line'))
        for b in ss['bodies']:
            o.append('| %s | %.2f m/s, %.2f s, slide %s %% | %.2f m/s, %.2f s, slide %s %% (x%.2f) |' % (
                b['body'], b['host_mps'], b['host_period_s'], f(100 * b['host_slide'] if b['host_slide'] is not None else None, '%.0f'),
                b['joiner_mps'], b['joiner_period_s'], f(100 * b['joiner_slide'] if b['joiner_slide'] is not None else None, '%.0f'), b['ratio']))
    o.append('')
    pf = res['pulls_flips']
    o.append('| pulls and flips (the joiner\'s copies) | |')
    o.append('|---|---|')
    if not pf.get('windows'): o.append('| windows | none: the writer logged no MP-NPCPULL line in the scenes |')
    else:
        o.append('| 10 s windows of one copy | %d (%d bodies, %.1f body-minutes) |' % (pf['windows'], pf['bodies'], pf['body_minutes']))
        o.append('| with a single-frame pull over 50 cm | %d = %.2f per body-minute |' % (pf['pull_windows'], pf['pulls']))
        o.append('| flips (a pull over 5 m, moves in over 40 %% of the frames) | %d = %.3f per body-minute |' % (pf['flip_windows'], pf['flips']))
        o.append('| by scene (windows / pulls / flips) | %s |' % '; '.join('%s %d / %d / %d' % (k, v['windows'], v['pull_windows'], v['flip_windows']) for k, v in pf['by_scene'].items()))
        o.append('| most pulled | %s |' % (', '.join('%s (%d)' % x for x in pf['most_pulled']) or '-'))
    o.append('')
    ln, co = res['lanes'], res['cost']
    o.append('| the lanes on the joiner, the cost on both | p50 | p99 |')
    o.append('|---|---:|---:|')
    o.append('| the frame processor\'s lag | %s ms | %s ms |' % (f(ln['frame_ms_p50'], '%d'), f(ln['frame_ms_p99'], '%d')))
    o.append('| the activity lane\'s age | %s ms | %s ms |' % (f(ln['activity_ms_p50'], '%d'), f(ln['activity_ms_p99'], '%d')))
    o.append('| the position writer\'s play-out allowance | %s ms | %s ms |' % (f(pf.get('playout_ms_p50'), '%d'), f(pf.get('playout_ms_p99'), '%d')))
    o.append('')
    o.append('| our main-thread time a frame | mean | p95 (mean of the minutes\' own) | worst minute\'s p95 | the game held | faults |')
    o.append('|---|---:|---:|---:|---:|---:|')
    for role in ('host', 'joiner'):
        c = co.get(role)
        if not c: o.append('| the %s | no FRAME line inside a scene | | | | |' % role); continue
        o.append('| the %s | %.2f ms | %.2f ms | %.2f ms | %.0f fps (lowest minute %.0f) | %d |' % (role, c['ours_ms_mean'], c['ours_ms_p95'], c['ours_ms_p95_worst'], c['fps'], c['fps_min'], c['faults']))
    o.append('')
    if res.get('works_run'):
        o.append('The checks are from their own run, `%s`%s.' % (res['works_run'], (': a game exited there at the step `%s` (%s) -- every check after it is "not run"' % (
            res['works_exited']['step'], ', '.join(res['works_exited']['crash_reports']) or 'no crash report')) if res.get('works_exited') else ''))
        o.append('')
    o.append('| works at all | | |')
    o.append('|---|---|---|')
    for w in res['works']:
        o.append('| %s | %s | %s |' % (CHECK_TEXT[w['check']], 'yes' if w['ok'] is True else 'no' if w['ok'] is False else 'not run', w['why']))
    o.append('')
    d = [s for s in res['scenes'] if s.get('danger')]
    if d: o.append('The players left alone (fights on a player ended / checks, per scene): %s.' % '; '.join(
        '%s host %s joiner %s' % (s['label'], s['danger'].get('host'), s['danger'].get('joiner')) for s in d))
    if res.get('gfx'): o.append('Settings: host `%s`; joiner `%s`.' % (res['gfx'].get('host'), res['gfx'].get('joiner')))
    if pt['commonest_differences']:
        o.append('')
        o.append('The commonest differences within 40 m (host -> copy: samples): ' + '; '.join('%s -> %s: %d' % (x['host'], x['copy'], x['samples']) for x in pt['commonest_differences'][:8]) + '.')
    return '\n'.join(o)


def spread(runs):
    rs = [score(r_) for r_ in runs]
    keys = ('place', 'thing', 'step', 'slide', 'pulls', 'flips', 'lane99', 'cost95', 'works')
    fm = {'place': '%.1f', 'thing': '%.1f', 'step': '%.2f', 'slide': '%.0f', 'pulls': '%.2f', 'flips': '%.3f', 'lane99': '%.0f', 'cost95': '%.1f', 'works': '%d'}
    o = ['| | ' + ' | '.join(r_['run'] for r_ in rs) + ' | mean | spread (max - min) |', '|---|' + '---:|' * (len(rs) + 2)]
    out = {}
    for k in keys:
        v = [r_['score'][k] for r_ in rs]
        have = [x for x in v if x is not None]
        mean = sum(have) / len(have) if have else None
        sp = (max(have) - min(have)) if len(have) > 1 else None
        out[k] = {'values': v, 'mean': mean, 'spread': sp}
        o.append('| %s | %s | %s | %s |' % (k, ' | '.join(f(x, fm[k]) for x in v), f(mean, fm[k] if k != 'works' else '%.1f'), f(sp, fm[k])))
    o.append('')
    for r_ in rs: o.append('`%s`  (%s)' % (score_line(r_['score']), r_['run']))
    mean_s = {k: out[k]['mean'] for k in keys}
    mean_s['works'] = int(round(mean_s['works'])) if mean_s['works'] is not None else 0
    mean_s['exited'] = None
    o.append('`%s`  (mean of %d)' % (score_line(mean_s), len(rs)))
    return '\n'.join(o), out


if __name__ == '__main__':
    a = sys.argv[1:]
    if not a: sys.exit(__doc__)
    if a[0] == '--spread':
        text, _ = spread(a[1:])
        print(text)
    else:
        res = score(a[0], whole='--all' in a, host='B' if '--host-b' in a else 'A', works_dir=a[a.index('--works') + 1] if '--works' in a else None)
        text = table(res)
        with open(os.path.join(a[0], 'score.json'), 'w', encoding='utf-8') as fh: json.dump(res, fh, indent=1)
        with open(os.path.join(a[0], 'score.md'), 'w', encoding='utf-8') as fh: fh.write(text + '\n')
        print(text)
