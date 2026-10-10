# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-165 Stage D (never shipped): C1's live gate without input -- this machine is the HOST, the synthetic joiner's figure fights a local
guard; the player is put near the guard facing it by console (expect WO165-LOCK pair=set), then 15 m away (expect pair=removed
why=host-left-10m). No focus change, no key press.

usage: python c1gate165.py <agent KcdMpClient.dll> [guard=ttkc_man_3]
"""
import math, os, re, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live
import run165 as R
import sit165 as S


def lock_lines(n0):
    with open(live.NLOG, 'rb') as f:
        f.seek(n0)
        return [l for l in f.read().decode('utf-8', 'replace').splitlines() if 'WO165-LOCK' in l]


def put(x, y, face_x, face_y):
    z = S.ground(x, y)
    yaw = math.atan2(-(face_x - x), face_y - y)
    for _ in range(2):
        live.lua('player:SetWorldPos({x=%f, y=%f, z=%f}); player:SetWorldAngles({x=0, y=0, z=%f})' % (x, y, z + 0.3, yaw))
        time.sleep(1.2)


def main():
    agent_dll = sys.argv[1]
    guard = sys.argv[2] if len(sys.argv) > 2 else 'ttkc_man_3'
    R.stop('agent'); R.stop('peer'); time.sleep(7)
    g = S.ent_pos(guard)
    # the player starts 20 m away (no pair yet)
    put(g[0] + 20, g[1], g[0], g[1])
    gz = S.ground(g[0] + 1.6, g[1])
    plan = os.path.join(live.LOGS, 'plan.c1gate.txt')
    open(plan, 'w').write('start 3\nghost %.2f %.2f %.2f 0 1 50 0.0005 30\navatarfight 3 600 %s 2.0 0 3\n' % (g[0] + 1.6, g[1], gz, guard))
    R.agent('c1gate', agent_dll, [])
    time.sleep(10)
    R.peer('c1gate', plan, ['--duration', '400'])
    agentlog = R.load_state()['agentlog']
    avatar = None
    for _ in range(40):
        avatar = next((e for e in (S.eid_of('kcd2mp_%d' % i) for i in range(4)) if e), None)
        opp, st = S.model_opp(agentlog, guard)
        if avatar and opp == avatar: break
        time.sleep(1.5)
    print('guard engaged on the figure:', opp == avatar, 'opp', opp, 'avatar', avatar)
    n0 = os.path.getsize(live.NLOG)
    g = S.ent_pos(guard)
    put(g[0] + 4.0, g[1] + 0.5, g[0], g[1])            # 4 m, facing it
    time.sleep(3)
    set_lines = lock_lines(n0)
    me_opp_dummy = S.model_opp(agentlog, guard)
    n1 = os.path.getsize(live.NLOG)
    g = S.ent_pos(guard)
    put(g[0] + 15.0, g[1], g[0], g[1])                 # 15 m away
    time.sleep(3)
    rem_lines = lock_lines(n1)
    status = S.probe('replaystatus', agentlog, 'WO165-PROBE replay status', 1.5)
    print('SET:', set_lines)
    print('REMOVED:', rem_lines)
    print('guard after:', me_opp_dummy)
    print('status:', status[-1] if status else None)
    ok = any('pair=set' in l and 'skirmish=done' in l for l in set_lines) and any('pair=removed' in l and 'host-left-10m' in l for l in rem_lines)
    print('C1 GATE', 'PASS' if ok else 'FAIL')
    R.stop('peer'); R.stop('agent')


if __name__ == '__main__':
    main()
