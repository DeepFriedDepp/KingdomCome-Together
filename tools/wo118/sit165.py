# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-165: the maintainer's probe sitting as ONE scripted run (never shipped). The tester switches to the game once and follows the
instruction line in the top-left corner; nothing here asks for an answer in chat until the end, and nothing presses a key or takes focus.

  phase J (this machine = the joiner of a synthetic host): P9 -- a 3-copy fight round the player for 3 minutes, the window in front;
                                                          P8 -- a queued CombatAttackFailed row on a copy whose swing is in flight
  phase H (this machine = the host, the synthetic peer = a joiner whose figure fights a local guard):
                                                          P6 -- the explicit hostile pair (skirmish_add(host, guard, 1)), the tester
                                                                locks on, the guard must stay on the figure >= 10 s
                                                          P7 -- the tester's first blow on that guard: crime lines, the relation at 5 s

usage: python sit165.py <agent KcdMpClient.dll> <relay KcdMpServer.dll> [--skip-j] [--skip-h]
Writes logs/sit165.json (every read, timestamped) beside the agent/peer logs.
"""
import json, math, os, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live
import run165 as R

LOG = os.path.join(live.LOGS, 'sit165.json')
REC = {'t0': time.strftime('%H:%M:%S'), 'events': []}
GUARD = 'ttkc_man_3'
COPIES = ['ttkc_man_31', 'ttkc_man_30', 'ttkc_man_3']
PUNCH = '3e27732b-b152-4a27-97d2-1ebba717a588'
PUNCH_SPEC = 'FreeAttack, l_noweapon+r_noweapon+freeGuard+endFreeGuard+punch+attack_heavy'
FAILED_SPECS = ['CombatAttackFailed, aZ5+r_shortSwords+l_noShield+oppMale', 'CombatAttackFailed, l_longsword+r_longsword+aZ5+oppMale']


def note(kind, **kv):
    kv.update(kind=kind, at=time.strftime('%H:%M:%S'))
    REC['events'].append(kv)
    print(kind, kv, flush=True)
    json.dump(REC, open(LOG, 'w'), indent=1)


def say(text):
    """The instruction line (top-left, where the ping is drawn); the ping writer is held off while the sitting runs."""
    t = text.replace('\\', '/').replace('"', "'")
    live.lua('W165_origPing = W165_origPing or KCD2MP_ShowPing; KCD2MP_ShowPing = function() end; KCD2MP.pingText = "WO-165  %s"' % t)


def release_line():
    live.lua('if W165_origPing then KCD2MP_ShowPing = W165_origPing end')


def kcd_lines(rx, since_len):
    with open(live.KLOG, 'rb') as f:
        f.seek(since_len)
        data = f.read().decode('utf-8', 'replace').splitlines()
    r = re.compile(rx)
    return [l for l in data if r.search(l)]


def klen():
    return os.path.getsize(live.KLOG)


def ask(code, tag, timeout=3.0):
    """Run Lua that logs '[W165Q] <tag> <payload>' and return the payload."""
    n = klen()
    live.lua(code.replace('@TAG', '[W165Q] ' + tag))
    end = time.time() + timeout
    while time.time() < end:
        hits = kcd_lines(r'\[W165Q\] ' + re.escape(tag) + ' ', n)
        if hits: return hits[-1].split('[W165Q] ' + tag + ' ', 1)[1].strip()
        time.sleep(0.1)
    return None


def player_pos():
    s = ask('local p=player:GetWorldPos(); System.LogAlways(string.format("@TAG %.2f %.2f %.2f", p.x, p.y, p.z))', 'pos%d' % time.time_ns())
    return tuple(map(float, s.split())) if s else None


def ent_pos(name):
    s = ask('local e=System.GetEntityByName("%s"); local p=e and e:GetWorldPos(); System.LogAlways(p and string.format("@TAG %%.2f %%.2f %%.2f", p.x, p.y, p.z) or "@TAG none")' % name,
            'ep%d' % time.time_ns())
    return tuple(map(float, s.split())) if s and s != 'none' else None


def ring(cx, cy, r, n=12):
    s = ask('local t={}; for i=0,%d do local a=i*2*math.pi/%d; t[#t+1]=string.format("%%.2f", System.GetTerrainElevation({x=%f+%f*math.cos(a), y=%f+%f*math.sin(a), z=0})) end; System.LogAlways("@TAG "..table.concat(t," "))'
            % (n - 1, n, cx, r, cy, r), 'ring%d' % time.time_ns())
    return [float(v) for v in s.split()]


def ground(x, y):
    s = ask('System.LogAlways(string.format("@TAG %%.2f", System.GetTerrainElevation({x=%f, y=%f, z=0})))' % (x, y), 'g%d' % time.time_ns())
    return float(s)


def health(name):
    s = ask('local e=System.GetEntityByName("%s"); System.LogAlways(string.format("@TAG %%.2f", e and e.actor and e.actor:GetHealth() or -1))' % name, 'h%d' % time.time_ns())
    return float(s) if s else -1.0


def probe(arg, agentlog, grep, wait=1.5):
    n = os.path.getsize(agentlog) if os.path.exists(agentlog) else 0
    live.lua('KCD2MP_W163Probe("%s")' % arg)
    time.sleep(wait)
    with open(agentlog, 'rb') as f:
        f.seek(n)
        lines = [l for l in f.read().decode('utf-8', 'replace').splitlines() if grep in l]
    return lines


def model_opp(agentlog, who):
    ls = probe('model %s' % who, agentlog, 'WO163-MODEL npc=%s ' % who, 1.0)
    if not ls: return None, None
    m = re.search(r'opp=0x([0-9A-F]+)', ls[-1]); st = re.search(r'state=(\d+)', ls[-1])
    return (int(m.group(1), 16) if m else None), (int(st.group(1)) if st else None)


def eid_of(name):
    s = ask('local e=System.GetEntityByName("%s"); System.LogAlways("@TAG "..tostring(e and e.id))' % name, 'id%d' % time.time_ns())
    m = re.search(r'([0-9A-Fa-f]{8,16})', s or '')
    return int(m.group(1), 16) & 0xFFFFFFFF if m else None


def countdown(secs, text):
    end = time.time() + secs
    while True:
        left = int(round(end - time.time()))
        if left <= 0: break
        say('%s  (%d:%02d)' % (text, left // 60, left % 60))
        time.sleep(min(5, max(0.5, end - time.time())))


# ---------------------------------------------------------------------------------------------------- phase J

def phase_j(agent_dll):
    say('Hello! Stay in the game from now on. Walk to the stranger (synth-host) east of you, stand next to him, then stand still.')
    note('J-start')
    ghost = ent_pos('kcd2mp_0')
    t_end = time.time() + 120
    last, still_since = None, None
    while time.time() < t_end:
        p = player_pos()
        if p and ghost and math.hypot(p[0] - ghost[0], p[1] - ghost[1]) < 3.5:
            if last and math.hypot(p[0] - last[0], p[1] - last[1]) < 0.15:
                still_since = still_since or time.time()
                if time.time() - still_since > 3: break
            else:
                still_since = None
            last = p
        time.sleep(0.7)
    p = player_pos()
    note('J-center', pos=p, ghost=ghost)
    cx, cy = p[0], p[1]
    say('Thanks -- stand still here. Setting up the fight round you (about 40 s)...')
    rings = {r: ring(cx, cy, r) for r in (2.6, 3.0, 3.4)}
    gx, gy = cx + 6.0, cy
    gz = ground(gx, gy)
    L = ['# WO-165 P9/P8 (generated by sit165.py): 3 copies round the player on the ground, then a queued failed-attack on one',
         'start 20', 'ghost %.2f %.2f %.2f 0 1 50 0.0005 30' % (gx, gy, gz)]
    for i, (n, r) in enumerate(zip(COPIES, (2.6, 3.0, 3.4))):
        L.append('fightz %s %.2f %.2f %.1f %.3f %s' % (n, cx, cy, r, i * 2 * math.pi / 3, ' '.join('%.2f' % z for z in rings[r])))
        L.append('ncombat 0 260 %s avatar:1 3 1 0 3' % n)
    for k in range(40):
        v, hp, st = ('hit', 1, 0) if k % 2 == 0 else ('blocked', 0, 4)
        L.append('swingat %.1f %s local %s 220 %s %d %d' % (20.0 + 4.5 * k, COPIES[k % 3], PUNCH, v, hp, st))
    plan = os.path.join(live.LOGS, 'plan.sit165.j.txt')
    open(plan, 'w').write('\n'.join(L) + '\n')
    R.stop('agent'); R.stop('peer'); time.sleep(7)
    R.peer('sitj', plan, ['--duration', '300', '--claim-host'])
    R.agent('sitj', agent_dll, ['KCDMP_TEST_JOINER_IN_WORLD=1'])
    agentlog = R.load_state()['agentlog']
    time.sleep(12)
    live.cmd('mp_leash off')
    # the fight: t=20..200 s of the plan's stream (the peer started ~12 s ago and streams from t=20 of its own clock)
    time.sleep(8)
    snap_from = time.strftime('%H:%M:%S')
    note('P9-start', snap_from=snap_from)
    countdown(185, 'P9: just watch the fight. Keep the game in front, do not move or press anything.')
    note('P9-end')
    # P8: a punch, then a failed-attack row on the copy 0.3 s later (its swing still in flight)
    for i, spec in enumerate(FAILED_SPECS * 2):
        say('P8 (%d of 4): watch the man swinging at you from the NORTH side. Does his swing bounce back off you?' % (i + 1))
        time.sleep(2)
        ls = probe('swing2 %s 300 %s | %s' % (COPIES[0], PUNCH_SPEC, spec), agentlog, 'WO163-PROBE swing2', 3.0)
        note('P8', spec=spec, result=ls[-1] if ls else 'no answer')
        time.sleep(4)
    say('Part 1 done. Stay in the game -- switching roles for part 2 (about 40 s)...')
    R.stop('peer'); R.stop('agent')
    return snap_from


# ---------------------------------------------------------------------------------------------------- phase H

def phase_h(agent_dll):
    time.sleep(7)
    g = ent_pos(GUARD)
    gx, gy = g[0] + 1.6, g[1]
    gz = ground(gx, gy)
    L = ['# WO-165 P6/P7 (generated by sit165.py): this machine is the HOST; the synthetic joiner\'s figure stands by the guard and strikes him',
         '# (attributed, stamina only: the guard\'s health then moves only for the host\'s own blow)',
         'start 3', 'ghost %.2f %.2f %.2f 0 1 50 0.0005 30' % (gx, gy, gz), 'avatarfight 3 600 %s 2.0 0 3' % GUARD]
    plan = os.path.join(live.LOGS, 'plan.sit165.h.txt')
    open(plan, 'w').write('\n'.join(L) + '\n')
    R.agent('sith', agent_dll, [])            # the agent first: in a shared world it claims host and is the authority
    time.sleep(10)
    R.peer('sith', plan, ['--duration', '900'])  # the joiner: its figure fights the guard
    agentlog = R.load_state()['agentlog']
    time.sleep(10)
    guard_eid = eid_of(GUARD)
    # the figure is named after the peer's relay id (the agent connected first here, so the peer is usually 1)
    avatar_eid = next((e for e in (eid_of('kcd2mp_%d' % i) for i in range(4)) if e), None)
    note('H-ids', guard=guard_eid, avatar=avatar_eid)
    say('Part 2 (P6): wait -- the stranger is starting a fight with the guard...')
    t_end = time.time() + 60
    while time.time() < t_end:
        opp, st = model_opp(agentlog, GUARD)
        if opp and avatar_eid and opp == avatar_eid: break
        time.sleep(1.5)
    note('P6-guard-engaged', guard_opp=opp, guard_state=st)
    hp0 = health(GUARD)
    pr = probe('pair %s on 1' % GUARD, agentlog, 'WO163-PROBE pair', 1.5)
    note('P6-pair', result=pr[-1] if pr else 'no answer', guard_hp=hp0)
    say('P6: walk to the guard fighting the stranger. Get within 6 m, face him, weapon drawn, LOCK ON to him. Do NOT hit him.')
    locked_at = None
    t_end = time.time() + 150
    reads = []
    while time.time() < t_end:
        p = player_pos(); gp = ent_pos(GUARD)
        d = math.hypot(p[0] - gp[0], p[1] - gp[1]) if p and gp else -1
        me_opp, me_st = model_opp(agentlog, 'me')
        g_opp, g_st = model_opp(agentlog, GUARD)
        reads.append({'t': time.strftime('%H:%M:%S'), 'dist': round(d, 2), 'me_opp': me_opp, 'guard_opp': g_opp})
        if me_opp and guard_eid and me_opp == guard_eid and not locked_at:
            locked_at = time.time()
            note('P6-locked', dist=d)
        if locked_at:
            left = 10 - (time.time() - locked_at)
            if left <= 0: break
            say('P6: LOCKED -- keep holding the lock, do not hit (%d s)' % math.ceil(left))
        time.sleep(0.8)
    note('P6-end', locked=bool(locked_at), reads=reads[-20:], guard_stayed_on_avatar=all(r['guard_opp'] == avatar_eid for r in reads[-12:]) if locked_at else None)
    # P7: the host's first blow
    say('P7: now hit the guard ONCE, then stop and step back.')
    hit_at = None
    n0 = klen()
    t_end = time.time() + 90
    while time.time() < t_end:
        h = health(GUARD)
        if 0 <= h < hp0 - 0.5:
            hit_at = time.time(); note('P7-hit', hp_before=hp0, hp_after=h); break
        hp0 = max(hp0, h) if h >= 0 else hp0
        time.sleep(0.3)
    if hit_at:
        say('P7: got it -- stop fighting, step back and wait (15 s)')
        time.sleep(max(0, 5 - (time.time() - hit_at)))
        rel = probe('relation %s' % GUARD, agentlog, 'WO163-PROBE relation', 1.5)
        note('P7-relation-5s', result=rel[-1] if rel else 'no answer')
        time.sleep(10)
        crime = kcd_lines(r'WO139|WO163-JUDGE|crime|Crime|assault|Assault|witness', n0)
        note('P7-crime-lines', lines=[l[:220] for l in crime[-40:]])
        g_opp, g_st = model_opp(agentlog, GUARD)
        note('P7-guard-after', guard_opp=g_opp, avatar=avatar_eid, host=None)
    else:
        note('P7-no-hit')
    probe('pair %s off' % GUARD, agentlog, 'WO163-PROBE pair', 1.0)
    say('ALL DONE -- thank you! You can switch to chat now. Tell me: P8, did the swing bounce back? Anything odd?')
    note('done')


if __name__ == '__main__':
    agent_dll, relay_dll = sys.argv[1], sys.argv[2]
    if not R.listening(7778): R.relay(relay_dll)
    try:
        if '--skip-j' not in sys.argv: phase_j(agent_dll)
        if '--skip-h' not in sys.argv: phase_h(agent_dll)
    except Exception as e:
        note('error', what='%s: %s' % (type(e).__name__, e))
        try: say('Something went wrong on my side -- you can switch to chat now. Sorry!')
        except Exception: pass
        raise
