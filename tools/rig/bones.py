# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 D: the bone prototype's lines (tools/rig/bones.lua, in the copy's kcd log) merged into the probe's samples as "b".

    python tools/rig/bones.py <kcd log> <kcdmp-anim.jsonl> <sync wall ms> <out.jsonl>

<sync wall ms>: the wall time (unix ms) at which the rig read the [W206B-SYNC] line (the engine clock is mapped to wall time
with that one offset; the console's own delay, ~20 ms, is the error). A sample takes the body's bone line nearest in time,
within 150 ms; a sample with none keeps no "b" (the detectors' bone variants then skip it).
"""
import bisect, json, re, sys

LINE = re.compile(r'^\[W206B\] (\d+\.\d+) (\S+) (.*)$')
SYNC = re.compile(r'^\[W206B-SYNC\] (\d+\.\d+)')
PART = re.compile(r'(\w+)=(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)')


def read_bones(kcd_log, sync_wall_ms):
    sync = None
    per = {}
    with open(kcd_log, encoding='utf-8', errors='replace') as f:
        for l in f:
            l = l.rstrip('\r\n')
            m = SYNC.match(l)
            if m and sync is None: sync = float(m.group(1)); continue
            m = LINE.match(l)
            if not m or sync is None: continue
            t_ms = sync_wall_ms + (float(m.group(1)) - sync) * 1000.0
            bones = {k: [float(x), float(y), float(z)] for k, x, y, z in PART.findall(m.group(3))}
            per.setdefault(m.group(2), []).append((t_ms, bones))
    for v in per.values(): v.sort(key=lambda r: r[0])
    return per


def merge(anim_in, per, out, window_ms=150.0):
    n = hit = 0
    with open(anim_in, encoding='utf-8') as f, open(out, 'w', encoding='utf-8') as g:
        for l in f:
            try: row = json.loads(l)
            except ValueError: continue
            for a in row.get('a', []):
                rows = per.get(a.get('n'))
                n += 1
                if not rows: continue
                ts = [r[0] for r in rows]
                i = bisect.bisect_left(ts, row['t'])
                best = min((j for j in (i - 1, i) if 0 <= j < len(rows)), key=lambda j: abs(ts[j] - row['t']), default=None)
                if best is not None and abs(ts[best] - row['t']) <= window_ms:
                    a['b'] = rows[best][1]; hit += 1
            g.write(json.dumps(row, separators=(',', ':')) + '\n')
    return n, hit


if __name__ == '__main__':
    if len(sys.argv) != 5: sys.exit(__doc__)
    per = read_bones(sys.argv[1], float(sys.argv[3]))
    n, hit = merge(sys.argv[2], per, sys.argv[4])
    print('bodies in the bone lines: %d; actor samples %d, with bones %d' % (len(per), n, hit))
