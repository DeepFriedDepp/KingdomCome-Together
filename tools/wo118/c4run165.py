# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-165 C4 (never shipped): one 3-copy synthetic fight with a named set of snapping fixes, measured by fightsnap165.py.

usage: python c4run165.py <tag> <fixes: none|hold|pose|hold,pose> <agent KcdMpClient.dll> <plan> [--reload]
The plan is a 3-copy fight round a centre (sit165.py's generated plan.sit165.j.txt); with --reload the throwaway save is loaded
again first and the player is put at the plan's centre (a console teleport: no input). No focus change, no key press.
Writes logs/c4.<tag>.json (the measure) and prints it.
"""
import json, os, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live
import run165 as R
import sit165 as S


def centre_of(plan):
    for l in open(plan):
        if l.startswith('fightz '):
            f = l.split()
            return float(f[2]), float(f[3])
    raise SystemExit('no fightz line in the plan')


def place_player(cx, cy):
    z = S.ground(cx, cy)
    for _ in range(3):
        live.lua('player:SetWorldPos({x=%f, y=%f, z=%f})' % (cx, cy, z + 0.3))
        time.sleep(1.5)
    return S.player_pos()


def main():
    tag, fixes, agent_dll, plan = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
    R.stop('agent'); R.stop('peer')
    time.sleep(7)
    if '--reload' in sys.argv:
        R.load('4', 'quicksave022')
        for _ in range(120):
            if any('Gameplay started' in l for l in live.tail(live.KLOG, 30)): break
            time.sleep(2)
        time.sleep(8)
    cx, cy = centre_of(plan)
    pos = place_player(cx, cy)
    print('player at', pos)
    R.peer('c4' + tag, plan, ['--duration', '240', '--claim-host'])
    t_peer = time.time()
    R.agent('c4' + tag, agent_dll, ['KCDMP_TEST_JOINER_IN_WORLD=1', 'KCDMP_W165_SNAP=' + fixes])
    time.sleep(12)
    live.cmd('mp_leash off')
    # the plan streams from t=20 of the peer's clock; its 40 swings run t=20..200
    time.sleep(max(0, t_peer + 20 - time.time()))
    since = time.strftime('%H:%M:%S')
    time.sleep(182)
    until = time.strftime('%H:%M:%S')
    native = os.path.join(live.LOGS, 'c4.%s.native.log' % tag)
    with open(live.NLOG, encoding='utf-8', errors='replace') as f, open(native, 'w', encoding='utf-8') as g:
        for l in f:
            if l.startswith('[') and since <= l[1:9] <= until: g.write(l)
    agentlog = R.load_state()['agentlog']
    out = subprocess.run([sys.executable, os.path.join(live.HERE, 'fightsnap165.py'), '--native', native, '--agent', agentlog], capture_output=True, text=True).stdout
    res = json.loads(out)
    hits = open(agentlog, encoding='utf-8', errors='replace').read()
    res['tag'] = tag; res['fixes'] = fixes; res['window'] = [since, until]
    res['wo161_hit_lines'] = len(re.findall(r'WO161-HIT victim=me', hits))
    res['applied_dup'] = len(re.findall(r'WO161-HIT victim=me.*applied=dup', hits))
    res['mp_dmg_anomalies'] = len(re.findall(r'MP-DMG.*(anomal|REFUSED|refused)', hits))
    res['frame'] = [l[l.find('fps='):l.find(' faults=')] for l in open(native, encoding='utf-8') if 'FRAME ' in l]
    res['holds_logged'] = len(re.findall(r'MP-WO165 cfg|snap_', hits))
    json.dump(res, open(os.path.join(live.LOGS, 'c4.%s.json' % tag), 'w'), indent=1)
    print(json.dumps({k: res[k] for k in ('tag', 'fixes', 'window', 'fight', 'holds', 'wo161_hit_lines', 'applied_dup', 'mp_dmg_anomalies', 'frame')}, indent=1))
    R.stop('agent'); R.stop('peer')


if __name__ == '__main__':
    main()
