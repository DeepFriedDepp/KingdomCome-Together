# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-197 Part B analyzer (never shipped): the B scenarios' runs (run197.py) -> shown / delay per item.

  python parity197b.py <results dir> [mode] [out.md]

A SOURCE CHANGE is a row the watcher's body was sent: the DLL's own `SWING: entity=<id> queued fragment <f> ... spec="<fragment>, <tags>"`
line (the harness agent logs carry no clock), sorted by the row's fragment: CombatAttackFailed* = B1's recoil, CombatHitSync* / CombatHitCombo*
/ CombatStopSlave* / CombatRiposteHit* = B2's victim half, CombatClinch* = B3, the b6moves rows = B6. SHOWN: the trace (WO190-ANIM) installs
that fragment id with priority >= 5 on any scope within 0.6 s and holds it >= 0.1 s (WO-187's rule), DELAY: that install minus the queue.
"""
import os, re, statistics, sys

SHOWN_S, HOLD_S = 0.6, 0.1


def hms(s):
    h, m, rest = s.split(':')
    return int(h) * 3600 + int(m) * 60 + float(rest)


def lines(p):
    return open(p, encoding='utf-8', errors='replace').read().splitlines() if os.path.exists(p) else []


ANIM = re.compile(r'^\[(\d\d:\d\d:\d\d\.\d+)\] \S+ WO190-ANIM utc=\d+ body=(\S+) role=(\S+) scope=(\S+) action=(\S+) frag=(\d+) prio=(-?\d+) flags=0x[0-9A-F]+ '
                  r'was=(\S+?):(\d+) held_ms=(-?[0-9.]+)')
SWING = re.compile(r'^\[(\d\d:\d\d:\d\d\.\d+)\] \S+ SWING: entity=(\d+) queued fragment (\d+) .*spec="([^,"]+), ?([^"]*)"')


def analyse(nat, want):
    tr = []
    for l in nat:
        m = ANIM.search(l)
        if m: tr.append({'t': hms(m.group(1)), 'body': m.group(2), 'scope': m.group(4), 'frag': int(m.group(6)), 'prio': int(m.group(7)),
                         'was': int(m.group(9)), 'held': float(m.group(10))})
    # when each install ended (the next change on the same body + scope) -> its hold
    ends = {}
    for i, a in enumerate(tr):
        for b in tr[i + 1:]:
            if b['body'] == a['body'] and b['scope'] == a['scope']: ends[i] = b['t']; break
    ev = []
    for l in nat:
        m = SWING.search(l)
        if m and want(m.group(4)): ev.append((hms(m.group(1)), int(m.group(3)), m.group(4)))
    delays, ok = [], 0
    for (t, frag, name) in ev:
        for i, a in enumerate(tr):
            if a['frag'] == frag and a['prio'] >= 5 and t - 0.05 <= a['t'] <= t + SHOWN_S and ends.get(i, a['t'] + 9) - a['t'] >= HOLD_S:
                ok += 1; delays.append(max(0.0, (a['t'] - t) * 1000.0)); break
    return ev, ok, delays


def p95(v):
    v = sorted(v); return v[min(len(v) - 1, int(round(0.95 * (len(v) - 1))))] if v else None


ITEMS = [('B1 recoil (synthetic partner)', 'b1recoil', lambda f: f.startswith('CombatAttackFailed')),
         ('B1 recoil (echo of this player, engine automation)', 'echosword', lambda f: f.startswith('CombatAttackFailed')),
         ('B2 victim half (copy <-> figure)', 'b2pair', lambda f: f.startswith(('CombatHitSync', 'CombatHitCombo', 'CombatStop', 'CombatRiposteHit'))),
         ('B2 attacker rows (same run)', 'b2pair', lambda f: f.startswith('CombatAttackSync')),
         ('B3 clinch rows (synthetic partner)', 'b3clinch', lambda f: f.startswith('CombatClinch')),
         ('B3 clinch (echo of this player, fists)', 'echofists', lambda f: f.startswith('CombatClinch')),
         ('B6 kicks, stops, combos, master strikes', 'b6moves', lambda f: True)]


def main():
    d = sys.argv[1]
    mode = sys.argv[2] if len(sys.argv) > 2 else 'after'
    out = sys.argv[3] if len(sys.argv) > 3 else None
    rows = ['| item | scenario | source changes | shown | delay median / p95 |', '|---|---|---|---|---|']
    for item, scen, want in ITEMS:
        nat = lines(os.path.join(d, '%s.%s.native.log' % (scen, mode)))
        if not nat: rows.append('| %s | %s | not run | | |' % (item, scen)); continue
        ev, ok, dl = analyse(nat, want)
        rows.append('| %s | %s | %d | %s | %s |' % (item, scen, len(ev), ('%d %%' % round(100.0 * ok / len(ev))) if ev else '-',
                    ('%.0f / %.0f ms' % (statistics.median(dl), p95(dl))) if dl else '-'))
    txt = '\n'.join(rows)
    print(txt)
    if out: open(out, 'w', encoding='utf-8').write(txt + '\n')


if __name__ == '__main__':
    main()
