# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""The WO-191 follow-up's live harness (village dogs in the NPC stream, mp_dog_sync; never shipped). Unattended: no focus change, no key
press; the throwaway playline4 only. Uses run165.py (relay, peer, agent, stop), run191.py's nudge and sit165.py's readers.

usage (W191_SP = the session's rig folder holding run/agent and run/relay; KCD2MP_INSTALL = the Modding Tools folder):
  python run191dogs.py dogs [r]          the class-Dog bodies within r m of Henry (name, position, hidden, dead) -- no agent needed
  python run191dogs.py h1 [secs]         host role, a synthetic joiner beside Henry: every village dog in range is tracked, streams as not
                                         human (flag 128) and reaches the joiner; its activity rows (WO141-CAPTURE) and how far each dog moved
  python run191dogs.py h2 [secs]         host role: frame rate with mp_dog_sync on / off / on (FRAME fps, ours_us_mean; tracked count per phase)
  python run191dogs.py j1 <dog> <stance> <stanceObjHex> <unstanceId|-> <unstObjHex>
                                         joiner role: the synthetic host walks <dog> 8 m, then holds it and sends the given activity (as h1
                                         captured it on a real host); the copy binds, follows, takes the activity; when the stream stops the
                                         dog is paused where it stands and stays shown
Results: <W191_SP>/run191dogs.results.jsonl
"""
import json, math, os, re, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live
import run165 as R
import sit165 as S
from run191 import nudge, lines_from, nlines, klines, henry

SP = os.environ.get('W191_SP') or sys.exit('set W191_SP to the session rig folder')
AGENT = os.path.join(SP, 'run', 'agent', 'KcdMpClient.dll')
RELAY = os.path.join(SP, 'run', 'relay', 'KcdMpServer.dll')
RESULTS = os.path.join(SP, 'run191dogs.results.jsonl')
R.VERSION = os.environ.get('W191_VERSION_FILE') or R.VERSION   # the rig's relay/agent build's own VERSION (the relay refuses another)


def say(*a): print(time.strftime('%H:%M:%S'), *a, flush=True)


def record(step, **kw):
    kw.update(step=step, at=time.strftime('%H:%M:%S'))
    open(RESULTS, 'a').write(json.dumps(kw) + '\n')
    print(json.dumps(kw, indent=1))


def dogs(r=120):
    """[(name, x, y, z, hidden, dead)] of the class-Dog bodies within r m of Henry."""
    s = S.ask('local p = player:GetWorldPos(); local out = {}; for _, e in ipairs(System.GetEntitiesInSphere(p, %d) or {}) do '
              'if e.class == "Dog" then local q = e:GetWorldPos(); local h = e:IsHidden() and 1 or 0; '
              'local d = (e.actor and e.actor:IsDead()) and 1 or 0; '
              'out[#out + 1] = string.format("%%s:%%.2f:%%.2f:%%.2f:%%d:%%d", e:GetName(), q.x, q.y, q.z, h, d) end end; '
              'System.LogAlways("@TAG " .. table.concat(out, ","))' % r, 'dogs%d' % time.time_ns(), timeout=5.0)
    out = []
    for item in (s or '').split(','):
        f = item.split(':')
        if len(f) == 6: out.append((f[0], float(f[1]), float(f[2]), float(f[3]), f[4] == '1', f[5] == '1'))
    return out


def tracked_count():
    s = S.ask('local n, d = 0, 0; for k in pairs(KCD2MP.npcTracked) do n = n + 1; local e = System.GetEntityByName(k); '
              'if e and e.class == "Dog" then d = d + 1 end end; System.LogAlways(string.format("@TAG %d %d", n, d))', 'tc%d' % time.time_ns())
    return tuple(map(int, s.split())) if s else (-1, -1)


def ghost_plan(tag, secs):
    p = henry()
    plan = os.path.join(live.LOGS, 'plan.run191dogs.%s.txt' % tag)
    open(plan, 'w').write('# the WO-191 follow-up %s: an idle synthetic joiner beside Henry\nstart 2\nghost %.2f %.2f %.2f 0 1 50 0.0005 30\n'
                          % (tag, p[0] + 3, p[1], p[2]))
    return plan


# ================================================================ H1: every village dog in range is streamed (host role)

def h1(secs=90):
    R.stop('peer'); R.stop('agent'); time.sleep(6)
    R.relay(RELAY)
    live.lua('KCD2MP_DogSyncSet("on")')
    ds = dogs()
    say('dogs within 120 m:', [d[0] for d in ds])
    if not ds: record('h1', ok=False, why='no class-Dog body near Henry'); return False
    koff = os.path.getsize(live.KLOG)
    noff = os.path.getsize(live.NLOG)
    R.agent('w191dogs_h1', AGENT, [])
    nudge()
    R.peer('w191dogs_h1', ghost_plan('h1', secs), ['--duration', str(secs + 30)])
    start = time.time()
    track = {d[0]: [(0.0, d[1], d[2])] for d in ds}
    while time.time() - start < secs:
        time.sleep(5)
        for d in dogs():
            if d[0] in track: track[d[0]].append((round(time.time() - start, 1), d[1], d[2]))
    tr = S.ask('local t = {}; for k in pairs(KCD2MP.npcTracked) do t[#t + 1] = k end; System.LogAlways("@TAG " .. table.concat(t, ","))',
               'tr%d' % time.time_ns(), timeout=5.0) or ''
    trackedNow = set(tr.split(','))
    kl = klines(koff)
    with open(live.NLOG, 'rb') as f:
        f.seek(noff); nl = f.read().decode('utf-8', 'replace').splitlines()
    plines = open(R.load_state()['peerlog'], encoding='utf-8', errors='replace').read().splitlines()
    per = {}
    for name, pts in track.items():
        moved = max((math.hypot(x - pts[0][1], y - pts[0][2]) for _, x, y in pts), default=0.0)
        got = [l for l in plines if l.startswith('SYNTH got npc %s ' % name)]
        gflags = [int(m.group(1)) for m in (re.search(r'flags=(\d+)', l) for l in got) if m]
        per[name] = {'tracked': name in trackedNow, 'joiner_got': got[-1][10:] if got else None,
                     'not_human_128': bool(gflags) and all((f // 128) % 2 == 1 for f in gflags),
                     'joiner_activity': [l[10:] for l in plines if l.startswith('SYNTH got activity npc %s ' % name)][:4],
                     'moved_m': round(moved, 1),
                     'capture': [l[l.find('WO141-CAPTURE'):][:200] for l in nl if ('WO141-CAPTURE npc %s:' % name) in l][:4]}
    mutt = [l[:160] for l in kl if 'NPC-SYNC tracking tvez_vorech' in l or 'NPC-SYNC tracking player_dogCompanion' in l] +            [l[:120] for l in plines if 'SYNTH got npc tvez_vorech' in l or 'SYNTH got npc player_dogCompanion' in l]
    ok = all(v['tracked'] and v['joiner_got'] and v['not_human_128'] for v in per.values()) and not mutt
    record('h1', ok=ok, dogs=per, mutt_tracked=mutt, toggles=[l[:200] for l in kl if 'DOG-SYNC' in l][:3])
    R.stop('peer'); R.stop('agent')
    return ok


# ================================================================ H2: the frame rate, mp_dog_sync on / off / on (host role)

def h2(secs=90):
    R.stop('peer'); R.stop('agent'); time.sleep(6)
    R.relay(RELAY)
    R.agent('w191dogs_h2', AGENT, [])
    nudge()
    R.peer('w191dogs_h2', ghost_plan('h2', secs), ['--duration', str(3 * secs + 120)])
    time.sleep(70)   # warm-up: the figure's spawn and the session start's bursts fall outside the measured windows
    out = {}
    for phase, val in (('on', 'on'), ('off', 'off'), ('on2', 'on')):
        live.lua('KCD2MP_DogSyncSet("%s")' % val)
        time.sleep(8)   # the next rescans take (or let go of) the dogs
        tc = tracked_count()
        t0 = time.strftime('%H:%M:%S')
        time.sleep(secs)
        frames = [l for l in live.tail(live.NLOG, 400, r'FRAME window') if l[1:9] > t0]
        vals = [(float(re.search(r'fps=([\d.]+)', l).group(1)), int(re.search(r'ours_us_mean=(\d+)', l).group(1))) for l in frames]
        out[phase] = {'tracked': tc[0], 'tracked_dogs': tc[1], 'windows': len(vals), 'fps': [v[0] for v in vals], 'ours_us_mean': [v[1] for v in vals]}
        say(phase, out[phase])
    live.lua('KCD2MP_DogSyncSet("on")')
    mean = lambda xs: sum(xs) / len(xs) if xs else None
    on = [f for k in ('on', 'on2') for f in out[k]['fps']]
    off = out['off']['fps']
    us_on = [u for k in ('on', 'on2') for u in out[k]['ours_us_mean']]
    us_off = out['off']['ours_us_mean']
    ratio = mean(on) / mean(off) if on and off else None
    record('h2', ok=ratio is not None and ratio >= 0.9, fps_ratio=round(ratio, 3) if ratio else None,
           ours_us_on=round(mean(us_on)) if us_on else None, ours_us_off=round(mean(us_off)) if us_off else None, **out)
    R.stop('agent'); R.stop('peer')
    return ratio is not None and ratio >= 0.9


# ================================================================ J1: a dog copy walks, lies down, and stays when let go (joiner role)

def j1(dog, stance='0', sobj='0', unst='-', uobj='0', walk=8.0):
    R.stop('peer'); R.stop('agent'); time.sleep(6)
    R.relay(RELAY)
    live.lua('KCD2MP_DogSyncSet("on")')
    g = S.ent_pos(dog)
    if not g: raise SystemExit('no dog ' + dog)
    hx, hy, hz = henry()
    # the walk: from the dog's own spot, 8 m away from Henry's side, at 1.2 m/s; then it holds there and lies down
    ux, uy = g[0] - hx, g[1] - hy
    n = math.hypot(ux, uy) or 1.0
    ux, uy = ux / n, uy / n
    ex, ey = g[0] + ux * walk, g[1] + uy * walk
    ez = S.ground(ex, ey)
    t_walk = walk / 1.2
    t_lie = 10 + t_walk + 4
    dur = int(t_lie + 30)
    plan = os.path.join(live.LOGS, 'plan.run191dogs.j1.txt')
    L = ['# the WO-191 follow-up J1: the host walks %s %.0f m, then it lies down' % (dog, walk), 'start 2',
         'timed %s %.4f 0 %.2f %.2f %.2f 10 %.2f %.2f %.2f %.1f %.2f %.2f %.2f %d %.2f %.2f %.2f'
         % (dog, math.atan2(-ux, uy), g[0], g[1], g[2], g[0], g[1], g[2], 10 + t_walk, ex, ey, ez, dur, ex, ey, ez),
         'act %.1f %s %s %s %s %s' % (t_lie, dog, stance, sobj, unst, uobj)]
    open(plan, 'w').write('\n'.join(L) + '\n')
    koff = os.path.getsize(live.KLOG)
    noff = os.path.getsize(live.NLOG)
    R.peer('w191dogs_j1', plan, ['--duration', str(dur), '--claim-host'])
    time.sleep(2)
    R.agent('w191dogs_j1', AGENT, ['KCDMP_TEST_JOINER_IN_WORLD=1'])
    agentlog = R.load_state()['agentlog']
    nudge()
    start = time.time() - 9
    samples = []
    while time.time() - start < dur + 12:
        p = S.ent_pos(dog)
        if p: samples.append((round(time.time() - start, 1), round(p[0], 2), round(p[1], 2)))
        time.sleep(1.0)
    time.sleep(8)   # the stream has stopped: the release
    hidden = S.ask('local e = System.GetEntityByName("%s"); System.LogAlways("@TAG " .. tostring(e and e:IsHidden()))' % dog, 'hid%d' % time.time_ns())
    kl = klines(koff)
    with open(live.NLOG, 'rb') as f:
        f.seek(noff); nl = f.read().decode('utf-8', 'replace').splitlines()
    ag = lines_from(agentlog, 0)
    dist_end = [math.hypot(x - ex, y - ey) for t, x, y in samples if 10 + t_walk + 2 <= t <= dur]
    dist_start = [math.hypot(x - g[0], y - g[1]) for t, x, y in samples if t <= 9]
    res = {'dog': dog, 'walk_to': [round(ex, 2), round(ey, 2)],
           'bind': [l[l.find('MP-NPCBIND'):][:200] for l in nl if ('MP-NPCBIND npc=%s ' % dog) in l][:2],
           'puppet': any(('puppet %s' % dog) in l for l in ag) or any(('MP-AUTHORITY npc=%s event=acquire' % dog) in l for l in kl),
           'at_start_max_m': round(max(dist_start), 2) if dist_start else None,
           'at_end_max_m': round(max(dist_end), 2) if dist_end else None,
           'apply': [l[l.find('WO141-APPLY'):][:220] for l in nl if ('WO141-APPLY %s' % dog) in l][:4],
           'released': [l[:200] for l in kl if ('DOG-SYNC released npc=%s' % dog) in l][:2],
           'hidden_after': hidden, 'samples': samples[::3]}
    ok = bool(res['bind']) and 'result=ok' in res['bind'][0] and dist_end and max(dist_end) < 1.0 and hidden == 'false'
    record('j1', ok=bool(ok), **res)
    R.stop('agent'); R.stop('peer')
    return bool(ok)


if __name__ == '__main__':
    a = sys.argv[1:]
    if not a: sys.exit(__doc__)
    if a[0] == 'dogs':
        for d in dogs(int(a[1]) if len(a) > 1 else 120): print(d)
        sys.exit(0)
    fn = {'h1': h1, 'h2': h2, 'j1': j1}[a[0]]
    args = [int(x) if x.isdigit() else x for x in a[1:]]
    sys.exit(0 if fn(*args) else 1)
