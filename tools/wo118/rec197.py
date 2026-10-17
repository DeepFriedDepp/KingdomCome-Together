# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-197 Part C (never shipped): the everyday categories, measured on real host rows.

  python rec197.py host <secs> <recording>
      the Modding Tools game is the HOST (its agent connects first: the authority); tools/wo121/avatarpeer is the joiner, recording
      everything the host sends (avatarpeer --record: NPC states 0x27, actions 0x3C -- NpcAttack, NpcCombat, DoorState, Emote --, activities
      0x6B, extras 0x6F) while the town's NPCs live their routines. Lua moves the clock forward on the way (morning work, evening seats,
      night beds) when W197_REC_HOURS is set ("8:120,19:120,23:90" = at hour 8 for 120 s, then hour 19 ...).
  python rec197.py plan <recording> <out plan> [target=1]
      the recording -> a synthpeer plan (raw lines at the recorded times, each Down packet turned back into its Up form, join packets
      addressed to the joiner id `target`)
  python rec197.py count <recording>
      the source changes per category in a recording (the analysis's denominators)
Nothing here ships."""
import json, os, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live
import run165 as R

HERE = os.path.dirname(os.path.abspath(__file__))
AVATARPEER = os.path.join(HERE, '..', 'wo121', 'avatarpeer', 'bin', 'Release', 'net8.0', 'AvatarPeer.dll')
SP = os.environ.get('W197_SP', '')
DET = 0x00000008 | 0x00000200


def say(*a): print(time.strftime('%H:%M:%S'), *a, flush=True)


def host(secs, rec):
    agent = os.path.join(SP, 'run', 'agent', 'KcdMpClient.dll')
    relay = os.path.join(SP, 'run', 'relay', 'KcdMpServer.dll')
    R.stop('agent'); R.stop('peer')
    time.sleep(5)
    R.relay(relay)
    R.agent('r197host', agent, [])   # first: the authority (the host)
    time.sleep(12)
    p = live.lua('local p = player:GetWorldPos(); System.LogAlways(string.format("[W197REC] %.2f %.2f %.2f", p.x, p.y, p.z))')
    time.sleep(1)
    pos = None
    for l in live.tail(live.KLOG, 50, r'\[W197REC\]'):
        m = re.search(r'\[W197REC\] (-?[0-9.]+) (-?[0-9.]+) (-?[0-9.]+)', l)
        if m: pos = tuple(float(x) for x in m.groups())
    if not pos: raise SystemExit('no player position')
    scen = os.path.join(live.LOGS, 'avatar.rec197.txt')
    open(scen, 'w').write('# WO-197 rec\nat 0 stand %.2f %.2f %.2f 0\nend %d\n' % (pos[0] + 2.0, pos[1] + 2.0, pos[2], int(secs) + 5))
    if os.path.exists(rec): os.remove(rec)
    log = os.path.join(live.LOGS, 'avatarpeer.rec197.log')
    lf = open(log, 'w')
    ap = subprocess.Popen([live.dotnet(), AVATARPEER, '--scenario', scen, '--name', 'w197rec', '--record', rec], stdout=lf, stderr=subprocess.STDOUT,
                          creationflags=DET)
    say('avatarpeer pid', ap.pid, 'recording ->', rec)
    t0 = time.time()
    hours = [(float(h), float(s)) for h, s in (x.split(':') for x in os.environ.get('W197_REC_HOURS', '').split(',') if ':' in x)]
    for h, s in hours:
        live.lua('local now = Calendar.GetWorldTime(); local day = math.floor(now / 86400) * 86400; local want = day + %d * 3600; '
                 'if want <= now then want = want + 86400 end; Calendar.SetWorldTime(want)' % int(h))
        say('clock -> hour', h, 'for', s, 's')
        time.sleep(s)
    while time.time() - t0 < secs: time.sleep(1)
    try: ap.wait(timeout=30)
    except Exception: ap.kill()
    R.stop('agent')
    n = sum(1 for _ in open(rec)) if os.path.exists(rec) else 0
    say('recorded', n, 'packets')
    print(json.dumps(count(rec), indent=1))


def count(rec):
    c = {'npc_state': 0, 'npc_attack': 0, 'npc_combat': 0, 'door': 0, 'emote': 0, 'activity_npc_rows': 0, 'activity_player_rows': 0, 'extra': {}}
    for line in open(rec, encoding='utf-8'):
        p = line.split()
        if len(p) != 3: continue
        typ, b = p[1].upper(), bytes.fromhex(p[2])
        if typ == '27': c['npc_state'] += 1
        elif typ == '3C' and len(b) > 2:
            k = b[1]
            if k == 13: c['npc_attack'] += 1
            elif k == 14: c['npc_combat'] += 1
            elif k == 16: c['door'] += 1
            elif k == 3: c['emote'] += 1
        elif typ == '6B' and len(b) > 7:
            kind, n = b[6], b[7]   # [src][target][joinId:4][kind][count]...
            c['activity_npc_rows' if kind == 1 else 'activity_player_rows'] += n
        elif typ == '6F' and len(b) > 7:
            kind, n = b[6], b[7]
            c['extra'][str(kind)] = c['extra'].get(str(kind), 0) + n
    return c


UP = {'27': '26', '3C': '3B', '6B': '6A', '6F': '6E', '31': '30'}


def plan(rec, out, target=1, start=6.0):
    rows = []
    for line in open(rec, encoding='utf-8'):
        p = line.split()
        if len(p) != 3: continue
        ms, typ, b = int(p[0]), p[1].upper(), bytes.fromhex(p[2])
        if typ not in UP or len(b) < 2: continue
        if typ == '3C' and b[1] not in (3, 13, 14, 16): continue   # the host's NPC rows, its doors and emotes; never its own avatar's
        body = bytearray(b[1:])                                     # the source byte dropped: the Up form
        stamp = -1
        if typ in ('6B', '6F'): body[0] = target                    # [target][joinId][body]: to the joiner
        elif typ == "27": stamp = body[0] + 24                      # [nameLen][name][5 floats][flags][seq:2][senderMs:4]
        elif typ == '3C': stamp = 9                                 # [kind][seq:2][phase][gen:4][len][payload: senderMs first]
        rows.append((ms, UP[typ], bytes(body).hex(), stamp))
    if not rows: raise SystemExit('nothing to replay')
    t0 = rows[0][0]
    L = ['# WO-197 Part C replay of %s (generated by rec197.py)' % os.path.basename(rec), 'start %.1f' % start, 'qmode 1 on']
    for ms, typ, hx, st in rows:
        L.append('raw %.3f %s %s%s' % ((ms - t0) / 1000.0, typ, hx, (' %d' % st) if st >= 0 else ''))
    open(out, 'w').write('\n'.join(L) + '\n')
    print('plan', out, len(rows), 'packets over %.0f s' % ((rows[-1][0] - t0) / 1000.0))


if __name__ == '__main__':
    v = sys.argv[1]
    if v == 'host': host(float(sys.argv[2]), sys.argv[3])
    elif v == 'plan': plan(sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 1)
    elif v == 'count': print(json.dumps(count(sys.argv[2]), indent=1))
    else: raise SystemExit(__doc__)
