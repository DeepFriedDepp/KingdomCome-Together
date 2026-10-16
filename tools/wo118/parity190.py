# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-190 parity analyzer (never shipped): reads run190.py's results (<rig>/results/<scenario>.<mode>.{json,native.log,agent.log}) and
writes the per-category parity table (markdown) for one mode or both side by side.

usage: python parity190.py <rig> <out.md> [before|after|both]

Per scenario (same machine: the agent's and the DLL's clocks are the same wall clock):
  * a SOURCE CHANGE is a move the watcher's body was sent: an inbound row (MP-ACTION section=inbound kind=Attack|NpcAttack|NpcHit|AvatarHit|
    BlockImpulse|Dodge, the agent's arrival time); locomotion scenarios count the gait class changes against the source's own stops and
    turns (the plan's), with the DLL's locomotion counters for the frames;
  * SHOWN: the body's trace (WO190-ANIM) installs an action of priority >= 5 on any scope within 0.6 s of the row's arrival (WO-187's move
    watcher rule); DELAY: that install's time minus the arrival;
  * GAPS: the body's full-body scope empty longer than 0.25 s (held_ms of the action=none stretch);
  * OVERFLOWS: the game's "Animation-queue overflow" lines in the run.
"""
import json, os, re, statistics, sys
from datetime import datetime

CATS = [('locomotion', ['figwalk', 'copywalk']), ('combat', ['figfight', 'copyfight', 'copyduel', 'opp', 'clinch', 'night']),
        ('hit, down and death', ['fighits', 'fighitsarmed', 'copyhits']), ('NPC routines (idle copies)', ['town', 'townwalk'])]
SHOWN_S = 0.6
GAP_S = 0.25


def hms(s):
    """'16:12:53.123' -> seconds of the day."""
    h, m, rest = s.split(':')
    return int(h) * 3600 + int(m) * 60 + float(rest)


def load(rig, scen, mode):
    base = os.path.join(rig, 'results', '%s.%s' % (scen, mode))
    if not os.path.exists(base + '.json'): return None
    res = json.load(open(base + '.json'))
    nat = open(base + '.native.log', encoding='utf-8', errors='replace').read().splitlines() if os.path.exists(base + '.native.log') else []
    ag = open(base + '.agent.log', encoding='utf-8', errors='replace').read().splitlines() if os.path.exists(base + '.agent.log') else []
    return res, nat, ag


ANIM = re.compile(r'^\[(\d\d:\d\d:\d\d\.\d+)\] \S+ WO190-ANIM utc=\d+ body=(\S+) role=(\S+) scope=(\S+) action=(\S+) frag=(\d+) prio=(-?\d+) flags=0x[0-9A-F]+ '
                  r'was=(\S+) held_ms=(-?[0-9.]+)')
INB = re.compile(r'^(\d\d:\d\d:\d\d\.\d+) .*MP-ACTION section=inbound (?:ghost=(\d+) )?kind=(\w+) (?:npc=(\S+) )?row=([0-9a-f-]{36})')


def trace(nat):
    out = []
    for l in nat:
        m = ANIM.search(l)
        if m:
            out.append({'t': hms(m.group(1)), 'body': m.group(2), 'role': m.group(3), 'scope': m.group(4), 'action': m.group(5), 'frag': int(m.group(6)),
                        'prio': int(m.group(7)), 'was': m.group(8), 'held_ms': float(m.group(9))})
    return out


def inbound(ag):
    out = []
    for l in ag:
        m = INB.search(l)
        if not m or 'dispatch=' not in l: continue
        body = m.group(4) or ('kcd2mp_' + m.group(2) if m.group(2) else None)
        if not body: continue
        out.append({'t': hms(m.group(1)), 'kind': m.group(3), 'body': body, 'row': m.group(5), 'line': l})
    return out


def gaps(tr, body=None):
    g = []
    for e in tr:
        if e['scope'] != 'FullBody' or (body and e['body'] != body): continue
        if e['was'].startswith('none') and e['held_ms'] > GAP_S * 1000: g.append(e['held_ms'] / 1000.0)
    return g


def p95(xs):
    if not xs: return None
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(round(0.95 * (len(xs) - 1))))]


SWING = re.compile(r'^\[(\d\d:\d\d:\d\d\.\d+)\] \S+ SWING: entity=(\d+) queued fragment (\d+) .*spec="([^"]*)"')


def swings(nat):
    """The rows the DLL queued on a body here (one line per row: the watcher's own moment the move was asked for)."""
    out = []
    for l in nat:
        m = SWING.search(l)
        if m: out.append({'t': hms(m.group(1)), 'eid': int(m.group(2)), 'frag': int(m.group(3)), 'spec': m.group(4)})
    return out


def moves(res, tr, nat):
    """Per move (the rows queued on one body within 0.9 s are one move: a reaction and its stand-in): shown when the trace shows one of its
    fragments installed with priority >= 5 within 0.6 s of its row; the delay from the first row; else what the full body held."""
    sw = swings(nat)
    groups = []
    for s in sw:
        if groups and groups[-1][-1]['eid'] == s['eid'] and s['t'] - groups[-1][0]['t'] < 0.9: groups[-1].append(s)
        else: groups.append([s])
    out = []
    for g in groups:
        t0 = g[0]['t']
        frags = {s['frag'] for s in g}
        ins = [e for e in tr if e['frag'] in frags and e['prio'] >= 5 and e['action'] != 'none' and t0 - 0.05 <= e['t'] <= g[-1]['t'] + SHOWN_S]
        # WO-187's move watcher: an action that left its scope within 0.1 s was never animated (the scope's NEXT change says how long it held)
        def lasted(e):
            nxt = next((x for x in tr if x['body'] == e['body'] and x['scope'] == e['scope'] and x['t'] > e['t']), None)
            return nxt is None or nxt['held_ms'] >= 100
        ins = [e for e in ins if lasted(e)]
        if ins:
            out.append({'kind': g[0]['spec'].split(',')[0], 'shown': True, 'delay': max(0.0, ins[0]['t'] - t0), 'standin': len(g) > 1})
        else:
            fb = [e for e in tr if e['scope'] == 'FullBody' and e['t'] <= t0 + SHOWN_S and e['role'] != 'player']
            out.append({'kind': g[0]['spec'].split(',')[0], 'shown': False, 'installed': fb[-1]['action'] if fb else '?', 'standin': len(g) > 1})
    return out


def scen_row(rig, scen, mode):
    L = load(rig, scen, mode)
    if not L: return None
    res, nat, ag = L
    tr = trace(nat)
    row = {'scen': scen, 'overflows': res.get('overflows', 0), 'gaps': len(gaps(tr)), 'gap_max': max(gaps(tr), default=0.0), 'lines': len(tr)}
    if scen in ('figwalk', 'copywalk'):
        lo = res.get('loco') or [None, None]
        k = 0 if scen == 'figwalk' else 1
        c = lo[k] if lo and lo[k] else None
        if c and c[0] > 0:
            row['frames'] = c[0]; row['glide_pct'] = 100.0 * c[1] / c[0]; row['inplace_pct'] = 100.0 * c[2] / c[0]; row['class_changes'] = c[3]
        info = res.get('info') or {}
        src = len(info.get('stops') or []) * 2 if scen == 'copywalk' else None
        row['source_changes'] = src
        if src and c: row['shown_pct'] = min(100.0, 100.0 * c[3] / src)
    else:
        mv = moves(res, tr, nat)
        row['source_changes'] = len(mv)
        sh = [m for m in mv if m['shown']]
        row['shown_pct'] = 100.0 * len(sh) / len(mv) if mv else None
        d = [m['delay'] * 1000.0 for m in sh]
        row['delay_med_ms'] = statistics.median(d) if d else None
        row['delay_p95_ms'] = p95(d)
        row['not_shown'] = [m['installed'] for m in mv if not m['shown']][:6]
    if scen == 'copyduel':
        n = (res.get('info') or {}).get('swings', 8) + 1   # the plan's first swing (before any reaction) and the 8 after a reaction
        row['windups'] = res.get('windups'); row['swings'] = n
        row['refused'] = len(res.get('refused_state') or [])
        row['takeovers'] = sum(1 for l in res.get('takeover') or [] if 'the new swing winds up' in l)
        row['pb_slots'] = res.get('pb_slots')
    if scen in ('opp', 'night'):
        row['engage'] = (res.get('engage_on'), res.get('engage_off'), res.get('engage_holds'))
        row['planned'] = res.get('plan_planned'); row['pb_slots'] = res.get('pb_slots'); row['windups'] = res.get('windups')
        row['torch'] = len(res.get('torch') or [])
    if scen == 'clinch':
        row['syncfallback'] = sum(1 for l in res.get('syncfallback') or [] if 'played' in l or 'WO167-SYNCFALLBACK' in l)
        row['clinch_kept'] = sum(1 for l in res.get('syncfallback') or [] if 'kept' in l)
        row['clinch_native'] = res.get('clinch_native')
    if scen in ('fighits', 'fighitsarmed', 'copyhits'):
        row['standins'] = len(res.get('hitfallback') or [])
        row['standin_bare_on_armed'] = sum(1 for l in res.get('hitfallback') or [] if 'r_noweapon' in l and 'its hands' in l and 'bare' not in l)
    row['idle_restores'] = len(res.get('idle') or [])
    row['faults'] = len(res.get('faults') or [])
    return row


def fmt(v, d=0, suffix=''):
    if v is None: return '-'
    if isinstance(v, float): return ('%.' + str(d) + 'f%s') % (v, suffix)
    return '%s%s' % (v, suffix)


def table(rig, mode):
    lines = ['| category | scenario | source changes | shown | delay median / p95 | full-body gaps > 0.25 s (max) | overflows | notes |',
             '|---|---|---|---|---|---|---|---|']
    for cat, scens in CATS:
        for s in scens:
            r = scen_row(rig, s, mode)
            if not r:
                lines.append('| %s | %s | not run | | | | | |' % (cat, s)); continue
            notes = []
            if 'glide_pct' in r: notes.append('glide %.2f %% / in place %.2f %% of %d frames, %d class changes' % (r['glide_pct'], r['inplace_pct'], r['frames'], r['class_changes']))
            if s == 'copyduel': notes.append('wind-ups %s of %s swings, refused %s, takeovers %s, shield windows %s' % (r['windups'], r['swings'], r['refused'], r['takeovers'], r['pb_slots']))
            if s == 'clinch': notes.append('plain fallbacks played %s, refused rows kept (shown anyway) %s' % (r['syncfallback'], r['clinch_kept']))
            if s in ('opp', 'night'):
                notes.append('engage on/off/holds %s/%s/%s, wind-ups %s, shield windows planned %s, engine slots %s' % (r['engage'] + (r['windups'], r['planned'], r['pb_slots'])))
                if s == 'night': notes.append('torch put away %d' % r['torch'])
            if 'standins' in r: notes.append('stand-ins %d' % r['standins'])
            if r.get('not_shown'): notes.append('not shown -> full body held: %s' % ', '.join(r['not_shown']))
            if r['idle_restores']: notes.append('idle restores %d' % r['idle_restores'])
            if r['faults']: notes.append('FAULT lines %d' % r['faults'])
            lines.append('| %s | %s | %s | %s | %s / %s | %d (%s s) | %s | %s |' % (
                cat, s, fmt(r.get('source_changes')), fmt(r.get('shown_pct'), 0, ' %'), fmt(r.get('delay_med_ms'), 0, ' ms'), fmt(r.get('delay_p95_ms'), 0, ' ms'),
                r['gaps'], fmt(r['gap_max'], 2), r['overflows'], '; '.join(notes)))
    return '\n'.join(lines)


if __name__ == '__main__':
    rig, out = sys.argv[1], sys.argv[2]
    mode = sys.argv[3] if len(sys.argv) > 3 else 'both'
    parts = ['# WO-190 parity (%s) -- generated %s by parity190.py' % (mode, datetime.now().strftime('%Y-%m-%d %H:%M')), '']
    for m in (['before', 'after'] if mode == 'both' else [mode]):
        parts += ['## %s' % m, '', table(rig, m), '']
    tb = os.path.join(rig, 'results', 'townbase.before.json')
    if os.path.exists(tb):
        t = json.load(open(tb))
        parts += ['Host baseline (no agent, no copies, 60 s at the same spot): %d overflows %s' % (t['overflows'], t.get('by_body')), '']
    open(out, 'w', encoding='utf-8').write('\n'.join(parts) + '\n')
    print('\n'.join(parts))
