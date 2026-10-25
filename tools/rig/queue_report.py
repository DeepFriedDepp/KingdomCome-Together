# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-207 B.1 / B.5: an agent's inbound queue from its own log (test tool, never shipped).

    python tools/rig/queue_report.py <agent.log> [--from HH:MM:SS] [--to HH:MM:SS] [--json]

The lag series: the agent's once-a-second MP-W207-Q line (WO-207's build) when there is one, else its ping line (every 2 s; the lag is
printed from 500 ms on, a ping without it counts as 0). Prints p50 / p90 / p99 / max per minute and for the whole window; with
MP-W207-QK lines, the kinds summed over the window (frames, wait, handler time, Lua queue / answer, pipe gate / round trip, drops) and
the pipe ops by count. Nothing here touches a game.
"""
import json, re, sys
from collections import defaultdict

TS = re.compile(r'^(\d\d):(\d\d):(\d\d)\.(\d{3}) ')


def secs(line):
    m = TS.match(line)
    return (int(m.group(1)) * 3600 + int(m.group(2)) * 60 + int(m.group(3)) + int(m.group(4)) / 1000.0) if m else None


def hms(s):
    h, m, x = (int(v) for v in s.split(':'))
    return h * 3600 + m * 60 + x


def pct(xs, p):
    if not xs: return None
    s = sorted(xs)
    return s[min(len(s) - 1, int(round(p / 100.0 * (len(s) - 1))))]


def parse(path, t0=None, t1=None):
    w207, ping, kinds, ops = [], [], defaultdict(lambda: defaultdict(float)), defaultdict(lambda: defaultdict(float))
    for l in open(path, encoding='utf-8', errors='replace'):
        t = secs(l)
        if t is None or (t0 is not None and t < t0) or (t1 is not None and t > t1): continue
        m = re.search(r'MP-W207-Q lag_ms=(\d+) waiting=(\d+)', l)
        if m:
            # the worst of the processor's lag and the console's / the pipe's oldest frame-driven wait (WO-207 B.5, when printed)
            extra = [int(x) for x in re.findall(r' (?:lua_ms|pipe_ms)=(\d+)', l)]
            w207.append((t, max([int(m.group(1))] + extra), int(m.group(2)))); continue
        if '[ping]' in l and '(network)' in l:
            m = re.search(r"the game's queue is (\d+) ms behind", l)
            ping.append((t, int(m.group(1)) if m else 0, None)); continue
        if 'MP-W207-QK' in l:
            body, _, pipe = l.partition('| pipe_ops')
            for k in re.finditer(r' (\S+?):(\d+)/(\d+)/(\d+)/(\d+)/(\d+)((?: \w+=[\d/]+)*);', body):
                r = kinds[k.group(1)]
                n = int(k.group(2))
                r['frames'] += n; r['wait_sum'] += n * int(k.group(3)); r['wait_max'] = max(r['wait_max'], int(k.group(4)))
                r['handler_sum'] += n * int(k.group(5)); r['handler_max'] = max(r['handler_max'], int(k.group(6)))
                for e in re.finditer(r' (lua|pipe|drop)=([\d/]+)', k.group(7)):
                    v = [int(x) for x in e.group(2).split('/')]
                    if e.group(1) == 'drop': r['drop'] += v[0]; continue
                    r[e.group(1)] += v[0]; r[e.group(1) + '_a_sum'] += v[0] * v[1]; r[e.group(1) + '_a_max'] = max(r[e.group(1) + '_a_max'], v[2])
                    r[e.group(1) + '_b_sum'] += v[0] * v[3]
            for o in re.finditer(r' (0x[0-9A-F]{2}\.[0-9A-F]{2}):(\d+)/(\d+)/(\d+)/(\d+)', pipe):
                r = ops[o.group(1)]
                n = int(o.group(2))
                r['n'] += n; r['gate_sum'] += n * int(o.group(3)); r['gate_max'] = max(r['gate_max'], int(o.group(4))); r['rtt_sum'] += n * int(o.group(5))
    return w207, ping, kinds, ops


def report(path, t0=None, t1=None):
    w207, ping, kinds, ops = parse(path, t0, t1)
    series, src = (w207, 'MP-W207-Q') if w207 else (ping, 'ping')
    lags = [x[1] for x in series]
    out = {'source': src, 'n': len(lags), 'p50_ms': pct(lags, 50), 'p90_ms': pct(lags, 90), 'p99_ms': pct(lags, 99), 'max_ms': max(lags) if lags else None,
           'minutes': []}
    by = defaultdict(list)
    for t, lag, _w in series: by[int(t // 60)].append(lag)
    for mnt in sorted(by):
        v = by[mnt]
        out['minutes'].append({'at': '%02d:%02d' % (mnt // 60 % 24, mnt % 60), 'p50': pct(v, 50), 'p99': pct(v, 99), 'max': max(v)})
    av = lambda s, n: round(s / n, 1) if n else 0
    out['kinds'] = sorted(({'kind': k, 'frames': int(r['frames']), 'wait_avg': av(r['wait_sum'], r['frames']), 'wait_max': int(r['wait_max']),
                            'handler_avg': av(r['handler_sum'], r['frames']), 'handler_sum_s': round(r['handler_sum'] / 1000.0, 1),
                            'handler_max': int(r['handler_max']), 'lua': int(r['lua']), 'lua_queued_avg': av(r['lua_a_sum'], r['lua']),
                            'lua_answer_avg': av(r['lua_b_sum'], r['lua']), 'pipe': int(r['pipe']), 'pipe_gate_avg': av(r['pipe_a_sum'], r['pipe']),
                            'pipe_gate_max': int(r['pipe_a_max']), 'pipe_rtt_avg': av(r['pipe_b_sum'], r['pipe']), 'drop': int(r['drop'])}
                           for k, r in kinds.items()), key=lambda x: -x['handler_sum_s'])
    out['pipe_ops'] = sorted(({'op': o, 'n': int(r['n']), 'gate_avg': av(r['gate_sum'], r['n']), 'gate_max': int(r['gate_max']),
                               'rtt_avg': av(r['rtt_sum'], r['n'])} for o, r in ops.items()), key=lambda x: -x['n'])
    return out


if __name__ == '__main__':
    a = sys.argv[1:]
    if not a: sys.exit(__doc__)
    t0 = hms(a[a.index('--from') + 1]) if '--from' in a else None
    t1 = hms(a[a.index('--to') + 1]) if '--to' in a else None
    r = report(a[0], t0, t1)
    if '--json' in a: print(json.dumps(r, indent=1)); sys.exit(0)
    print('%s: %s samples, p50 %s ms, p90 %s, p99 %s, max %s' % (r['source'], r['n'], r['p50_ms'], r['p90_ms'], r['p99_ms'], r['max_ms']))
    for m in r['minutes']: print('  %s p50 %5s p99 %6s max %6s' % (m['at'], m['p50'], m['p99'], m['max']))
    if r['kinds']:
        print('kind        frames wait_avg wait_max handler_avg handler_s handler_max   lua q_avg a_avg   pipe gate_avg gate_max rtt_avg drop')
        for k in r['kinds'][:20]:
            print('%-10s %7d %8s %8d %11s %9s %11d %5d %5s %5s %6d %8s %8d %7s %4d' % (
                k['kind'], k['frames'], k['wait_avg'], k['wait_max'], k['handler_avg'], k['handler_sum_s'], k['handler_max'], k['lua'],
                k['lua_queued_avg'], k['lua_answer_avg'], k['pipe'], k['pipe_gate_avg'], k['pipe_gate_max'], k['pipe_rtt_avg'], k['drop']))
    if r['pipe_ops']:
        print('pipe op      n gate_avg gate_max rtt_avg')
        for o in r['pipe_ops'][:15]: print('%-8s %6d %8s %8d %7s' % (o['op'], o['n'], o['gate_avg'], o['gate_max'], o['rtt_avg']))
