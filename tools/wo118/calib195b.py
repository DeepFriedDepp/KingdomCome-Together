# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-195B: the calibration table (never shipped). Reads the live rows (run195b.results*.jsonl), scores WO-195's rule and the calibrated one
against the engine's own verdicts, writes docs/WO-195B-calibration.csv and the generated rows inside tools/Test-WO195Synthetic.lua (between
the CALIB markers: the Lua test then scores the REAL rule functions against the same rows).

usage: python calib195b.py <rig folder> [--write]
"""
import json, math, os, re, sys

RIG = sys.argv[1]
WRITE = '--write' in sys.argv
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))

# ---- the two rules (WO-195's, and the calibrated one: kdcmp.lua's P table) -------------------------------------------------
def clamp(v, lo, hi): return max(lo, min(hi, v))


def old_verdict(row):
    """WO-195 as it stood: the 0.60.9-style snapshot (vis unread, n = the noise stat), 25 m base, a 210 deg field, 4 m / 0.6 m footsteps."""
    skill = 7.0
    dark = -0.7 if row['night'] else 0.0
    f = clamp(1 + dark + (-0.5 if row['crouch'] else 0.0), 0.12, 1.3)
    sight = 25.0 * f * (1 - 0.3 * skill / 30)
    nf = clamp(row.get('n', 0.0) / 1.0, 0.5, 1.6)
    noise = (0.6 if row['crouch'] else 4.0) * nf * (1 - 0.6 * skill / 30)
    if row['asleep']:
        return 'woken' if row['d'] <= noise else 'asleep'
    in_view = row['d'] <= sight and (row['d'] <= 2.0 or row['a'] is None or row['a'] <= 104.4775)
    if in_view and row['los'] != 0: return 'seen'
    if row['d'] <= noise: return 'heard'
    return 'far' if row['d'] > sight else 'unseen'


P = dict(baseM=20.0, visRef=1.19, visSlope=0.25, minFactor=0.10, maxFactor=1.35, fovDeg=100, fovPad=5, closeM=0.6, skillToRadius=0.3,
         skillMax=30, noiseK=1.5, noiseMaxM=4.0)


def new_verdict(row):
    skill = 7.0
    f = clamp(1 + (row['vib'] - P['visRef']) * P['visSlope'], P['minFactor'], P['maxFactor'])
    base = row['vir'] if row.get('vir') and row['vir'] > 0 else P['baseM']
    sight = base * f * (1 - P['skillToRadius'] * skill / P['skillMax'])
    noise = min(P['noiseMaxM'], P['noiseK'] * math.sqrt(max(0.0, row.get('n', 0.0))))
    half = (row.get('fov') or P['fovDeg']) / 2 + P['fovPad']
    if row['asleep']:
        return 'woken' if row['d'] <= noise else 'asleep'
    in_view = row['d'] <= sight and (row['d'] <= P['closeM'] or row['a'] is None or row['a'] <= half)
    if in_view and row['los'] != 0: return 'seen'
    if row['d'] <= noise: return 'heard'
    return 'far' if row['d'] > sight else 'unseen'


def noticed(v): return v in ('seen', 'heard', 'woken')


# ---- the rows -------------------------------------------------------------------------------------------------------------------
def load(fn):
    p = os.path.join(RIG, fn)
    return [json.loads(l) for l in open(p, encoding='utf-8')] if os.path.exists(p) else []


main = load('run195b.results.jsonl')
early = load('run195b.results.exploratory1.jsonl') + load('run195b.results.contaminated.jsonl')
rows = []

# sight: a torchless Henry, at night, in front of / behind the awake night guard (the guard's torchless-player bark is the engine's "noticed")
for r in early:
    if r.get('tag') == 'night' and r.get('at', '') < '16:40:00' and r.get('stance') == 'stand' and r.get('d_req') in (3.0, 8.0) and 'barks' in r:
        rows.append(dict(kind='sight', id='SF-%g' % r['d_req'], what='night, standing, no torch, in front', crouch=False, night=True, asleep=False, d=r['d_req'], a=0,
                         los=r['los'], vir=r['vir'], fov=100, vib=r['vib'], n=0.0, engine=r['barks'] > 0, usable=r['barks'] > 0, window_s=8))   # a "no" in 8 s is inconclusive: the 9 m YES needed the 10th second
for r in main:
    if r.get('tag') != 'approach' or r['at'] < '18:53:00': continue
    front = r['deg_req'] == 0.0
    crouch = r['stance'] == 'crouch'
    d = r['d_req']
    a = r['a']
    if r['yes']:
        a = 0   # the guard walked toward him before it barked: the geometry at the start of the window is the case
    rows.append(dict(kind='sight', id='%s%s-%g' % ('C' if crouch else 'S', 'F' if front else 'B', d),
                     what='night, %s, no torch, %s' % ('crouching' if crouch else 'standing', 'in front' if front else 'behind it'), crouch=crouch, night=True, asleep=False,
                     d=d, a=a, los=r['los'], vir=r['vir'], fov=100, vib=r['vib'], n=0.0, engine=bool(r['yes']), usable=not crouch, window_s=int(r['ticks']) - 1))
# the sleepers' wake by the engine's own movement sound (kind 3) at a measured distance
for r in main:
    if r.get('tag') == 'wake' and r['at'] >= '18:10:00' and r['d'] <= 6.0:
        rows.append(dict(kind='wake', id='W%s-%.1f' % (r['mult'], r['d']), what='sleeper, engine movement sound x%s' % r['mult'], crouch=False, night=True, asleep=True, d=r['d'],
                         a=None, los=r['los'], vir=0.0, fov=100, vib=-0.95, n=float(r['mult']), engine=bool(r['woke']), usable=True, window_s=9))

# Henry's own footsteps (the game window's walk key): the distances he was at while the sleeper slept, and the one at which it got up
for r in main:
    if r.get('tag') == 'walkwake' and r.get('woke') and r.get('series'):
        rows.append(dict(kind='wake', id='H%s-%.1f' % (r['npc'][-4:], r['d_wake']), what='sleeper, Henry walking (real keys)', crouch=False, night=True, asleep=True, d=r['d_wake'],
                         a=None, los=1, vir=0.0, fov=100, vib=-0.95, n=0.43, engine=True, usable=True, window_s=6))
        seen = set()
        for t, d in r['series']:
            if t >= (r.get('t_wake') or 0) or d < r['d_wake'] + 0.3 or round(d, 1) in seen: continue
            seen.add(round(d, 1))
            rows.append(dict(kind='wake', id='H%s-%.1f' % (r['npc'][-4:], d), what='sleeper, Henry walking (real keys)', crouch=False, night=True, asleep=True, d=d,
                             a=None, los=1, vir=0.0, fov=100, vib=-0.95, n=0.43, engine=False, usable=True, window_s=6))

for r in rows:
    r['before'] = noticed(old_verdict(r))
    r['after'] = noticed(new_verdict(r))

use = [r for r in rows if r['usable']]
def score(sel, key): return sum(1 for r in sel if r[key] == r['engine']), len(sel)
for fam in ('sight', 'wake', None):
    sel = [r for r in use if fam is None or r['kind'] == fam]
    b, n = score(sel, 'before'); a, _ = score(sel, 'after')
    print('%-6s cases=%3d  before %3d (%.1f %%)  after %3d (%.1f %%)' % (fam or 'ALL', n, b, 100.0 * b / n, a, 100.0 * a / n))
print('not usable (the engine observable does not apply to a crouching Henry):', len(rows) - len(use))

# ---- outputs --------------------------------------------------------------------------------------------------------------------
if WRITE:
    csv = os.path.join(REPO, 'docs', 'WO-195B-calibration.csv')
    with open(csv, 'w', encoding='utf-8', newline='\n') as f:
        f.write('id,kind,situation,usable,distance_m,angle_deg,line_clear,npc_vir,vib,noise_level,engine_noticed,rule_before,rule_after,window_s\n')
        for r in rows:
            f.write('%s,%s,"%s",%d,%.1f,%s,%s,%.1f,%.2f,%.2f,%d,%d,%d,%d\n' % (r['id'], r['kind'], r['what'], r['usable'], r['d'], '' if r['a'] is None else r['a'],
                    '' if r['los'] is None else r['los'], r['vir'], r['vib'], r['n'], r['engine'], r['before'], r['after'], r['window_s']))
    lua = ['CALIB = {']
    for r in use:
        lua.append('    { kind = "%s", id = "%s", crouch = %s, d = %.1f, a = %s, los = %s, vir = %.1f, vib = %.2f, n = %.2f, engine = %s },' % (
            r['kind'], r['id'], 'true' if r['crouch'] else 'false', r['d'], 'nil' if r['a'] is None else r['a'], 'nil' if r['los'] is None else r['los'], r['vir'], r['vib'],
            r['n'], 'true' if r['engine'] else 'false'))
    lua.append('}')
    t = os.path.join(REPO, 'tools', 'Test-WO195Synthetic.lua')
    s = open(t, encoding='utf-8').read()
    a = s.index('-- @@CALIB-BEGIN@@'); b = s.index('-- @@CALIB-END@@')
    s = s[:a] + '-- @@CALIB-BEGIN@@ (generated by tools/wo118/calib195b.py from the live rows; docs/WO-195B-calibration.csv has the full table)\n' + '\n'.join(lua) + '\n' + s[b:]
    open(t, 'w', encoding='utf-8', newline='\n').write(s)
    print('wrote', csv, 'and the CALIB rows in', t, '(', len(use), 'usable rows )')
# the sight rows as a markdown table
print('\n| case | engine | rule before | rule after |\n|---|---|---|---|')
for r in rows:
    if r['kind'] == 'sight':
        print('| %s %.0f m (vib %.2f, %s) | %s | %s | %s |' % (r['what'], r['d'], r['vib'], 'line clear' if r['los'] else 'line blocked',
              ('noticed' if r['engine'] else 'not noticed') + ('' if r['usable'] else ' (not usable)'), 'noticed' if r['before'] else 'not noticed', 'noticed' if r['after'] else 'not noticed'))
