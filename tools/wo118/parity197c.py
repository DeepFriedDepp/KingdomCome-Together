# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-197 Part C analyzer (never shipped): a replay run of real host rows (rec197.py -> run197.py creplay) on this joiner -> the parity table.

  python parity197c.py <results dir> <mode> [out.md]

SOURCE CHANGES are the host's own rows as replayed (the recording's packets): an NPC's activity change (0x6A rows), a hands / gait / one-shot /
look row (0x6E), a door state (DoorState). Each is sorted into a category:
  seats and beds         an activity row whose stance is not none (sitting, lying, sleeping ...)
  work stations          an activity row with a minigame
  NPC routines           an activity row with an unstance (work at an object: chopping, hoeing ...), a gait row, a one-shot that is not food/drink
  hand items             a hands row; a one-shot that eats or drinks
  emotes and gestures    a look row (who an NPC turns its head to while talking)
  doors                  a door state
SHOWN: the joiner's DLL applied it and the body reads what was wanted (WO141-APPLY ok / in step; WO143-HANDS not refused; WO143-SHOT finished
or still playing when its body's next activity came; WO143-GAIT applied; a door: WO197-DOOR moved / already). DELAY: that line's time minus the
row's send time (the synthetic host's `raw ... at=` clock, the same machine). Rows the DLL settles without a line (a hands row whose body
already holds them, a look) are counted from the DLL's own counters where it has them.
"""
import os, re, statistics, sys
from collections import defaultdict

WINDOW_S = 3.0
FOOD = re.compile(r'(?i)eat|drink|mash|tub|water|beer|wine|bread|soup|meal')


def hms(s):
    h, m, rest = s.split(':')
    return int(h) * 3600 + int(m) * 60 + float(rest)


def lines(p):
    return open(p, encoding='utf-8', errors='replace').read().splitlines() if os.path.exists(p) else []


def p95(v):
    v = sorted(v); return v[min(len(v) - 1, int(round(0.95 * (len(v) - 1))))] if v else None


def main():
    d, mode = sys.argv[1], sys.argv[2]
    out = sys.argv[3] if len(sys.argv) > 3 else None
    pre = os.path.join(d, 'creplay.%s' % mode)
    nat, peer, kcd = lines(pre + '.native.log'), lines(pre + '.peer.log'), lines(pre + '.kcd.log')
    sends = {'6A': [], '6E': [], 'door': []}
    for l in peer:
        m = re.search(r'raw join type=(6A|6E) kind=(\d+) n=(\d+) at=(\d\d:\d\d:\d\d\.\d+)', l)
        if m: sends[m.group(1)].append(hms(m.group(4)))
        m = re.search(r'raw action kind=16 at=(\d\d:\d\d:\d\d\.\d+)', l)
        if m: sends['door'].append(hms(m.group(1)))
    for k in sends: sends[k].sort()

    def delay(src, t):
        prev = [s for s in sends[src] if s <= t]
        return (t - prev[-1]) * 1000.0 if prev and t - prev[-1] <= WINDOW_S else None

    cats = defaultdict(lambda: {'n': 0, 'ok': 0, 'dl': []})
    seen_activity = {}
    for l in nat:
        m = re.match(r'^\[(\d\d:\d\d:\d\d\.\d+)\] \S+ (WO141-APPLY|WO143-SHOT|WO143-HANDS|WO143-GAIT|WO197-DOOR) (.*)$', l)
        if not m: continue
        t, tag, rest = hms(m.group(1)), m.group(2), m.group(3)
        if tag == 'WO141-APPLY':
            npc = rest.split(' ')[0]
            ok = ' in step' in rest[:len(npc) + 9] or rest[len(npc):].startswith(' -> ok')
            want = re.search(r'wanted (stance=\S+ obj=\S+ unstance=\S+ obj=\S+ minigame=\d+)', rest) or re.search(r'in step: (stance=\S+ obj=\S+ unstance=\S+ obj=\S+ minigame=\d+)', rest)
            w = want.group(1) if want else ''
            key = (npc, w)
            if key in seen_activity and seen_activity[key]: continue   # one source change = the first apply that reads ok (retries are one change)
            st = re.search(r'stance=(\S+)', w); un = re.search(r'unstance=(\S+)', w); mg = re.search(r'minigame=(\d+)', w)
            cat = ('work stations' if mg and mg.group(1) != '255' else 'seats and beds' if st and st.group(1) not in ('none', '-') else
                   'NPC routines' if un and un.group(1) != '-' else 'NPC routines')
            if key not in seen_activity: cats[cat]['n'] += 1
            seen_activity[key] = ok
            if ok:
                cats[cat]['ok'] += 1
                dl = delay('6A', t)
                if dl is not None: cats[cat]['dl'].append(dl)
        elif tag == 'WO143-SHOT':
            frag = re.search(r"'([^']*)'", rest)
            fr = frag.group(1) if frag else ''
            cat = 'hand items' if FOOD.search(fr) else 'NPC routines'
            cats[cat]['n'] += 1
            if ' finished ' in rest or ' carried the body ' in rest:
                cats[cat]['ok'] += 1
        elif tag == 'WO143-HANDS':
            if 'capture' in rest: continue
            cats['hand items']['n'] += 1
            if 'refuses' not in rest and 'FAILED' not in rest:
                cats['hand items']['ok'] += 1
                dl = delay('6E', t)
                if dl is not None: cats['hand items']['dl'].append(dl)
        elif tag == 'WO143-GAIT':
            if 'capture' in rest or 'cleared' in rest: continue
            cats['NPC routines']['n'] += 1; cats['NPC routines']['ok'] += 1
            dl = delay('6E', t)
            if dl is not None: cats['NPC routines']['dl'].append(dl)
        elif tag == 'WO197-DOOR':
            cats['doors']['n'] += 1
            if re.search(r'-> (moved|already)', rest):
                cats['doors']['ok'] += 1
                dl = delay('door', t)
                if dl is not None: cats['doors']['dl'].append(dl)
    for l in kcd:   # doors: the joiner's Lua apply (kcd.log has no wall clock: no delay here; the apply runs in the arrival's own Lua call)
        m = re.search(r'WO197-DOOR \S+ dir=(-?\d+) was=\S+ -> (\S+)', l)
        if not m or m.group(2) == 'lock-only': continue
        cats['doors']['n'] += 1
        if m.group(2) in ('moved', 'already'): cats['doors']['ok'] += 1
    gaps = 0
    for l in nat:
        m = re.search(r'WO190-ANIM .* role=copy scope=FullBody action=\S+ .* was=none:\d+ held_ms=(-?[0-9.]+)', l)
        if m and float(m.group(1)) > 250: gaps += 1
    overflows = sum(1 for l in kcd if 'Animation-queue overflow' in l)
    rows = ['| category | source changes (applied) | shown | delay median / p95 | full-body gaps > 0.25 s | overflows |', '|---|---|---|---|---|---|']
    for cat in ('doors', 'seats and beds', 'hand items', 'work stations', 'NPC routines'):
        c = cats.get(cat, {'n': 0, 'ok': 0, 'dl': []})
        rows.append('| %s | %d | %s | %s | %d | %d |' % (cat, c['n'], ('%d %%' % round(100.0 * c['ok'] / c['n'])) if c['n'] else '-',
                    ('%.0f / %.0f ms' % (statistics.median(c['dl']), p95(c['dl']))) if c['dl'] else '-', gaps, overflows))
    txt = '\n'.join(['replayed: %d activity batches, %d extra batches, %d door states' % (len(sends['6A']), len(sends['6E']), len(sends['door'])), ''] + rows)
    print(txt)
    if out: open(out, 'w', encoding='utf-8').write(txt + '\n')


if __name__ == '__main__':
    main()
