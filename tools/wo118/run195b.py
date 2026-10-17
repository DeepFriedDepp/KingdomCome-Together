# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-195B live harness (never shipped): the engine as the reference for the joiner's witness rule. The throwaway playline4 only.

The engine's own "noticed" signal for the host's Henry, found in this WO: at night, in a settlement, an awake NPC that perceives a Henry
with no torch drawn plays a bark (metarole NPC_REAGUJE_NA_HRACE_BEZ_POCHODNE; handleAwareness_player.xml, offence noTorch); the same tree
plays NPC_VIDI_HRACE_SE_ZBRANI_* for a drawn weapon. Sleepers: the engine's crime_sleeping context and an NPC view radius (derived stat
vir) of 0. Henry's own stealth stats are derived stats vib (visibility), fsm (footstep), noi (noise), con, evi.

usage (W195B_SP = the rig folder; KCD2MP_INSTALL = the Modding Tools folder):
  python run195b.py night <npc> [d,d,..] [deg,deg,..] [stand|crouch]    the awake-NPC grid at night (one observer)
Results: <W195B_SP>/run195b.results.jsonl (one JSON object per case)
"""
import json, math, os, re, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live
import sit165 as S

SP = os.environ.get('W195B_SP') or sys.exit('set W195B_SP to the rig folder')
RESULTS = os.path.join(SP, 'run195b.results.jsonl')
LUA = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'lua', 'w195b.lua')
GUARD_BUFF = '4b43444d-7113-4d67-b1a5-9e2f6d8c0a13'   # the mod's own death-guard buff (imm=1, upr=1): the harness only, so Henry outlives a guard
WUID = 'XGenAIModule.GetMyWUID(player)'


def say(*a): print(time.strftime('%H:%M:%S'), *a, flush=True)


def Q(code, tag, t=5.0): return S.ask(code, tag + str(time.time_ns() % 1000000), t)


def record(**kw):
    kw['at'] = time.strftime('%H:%M:%S')
    open(RESULTS, 'a').write(json.dumps(kw) + '\n')


def setup(hour=23.5):
    """After a load: the helpers, the guard buff, the night (forward only: a set-back is ignored by the game)."""
    live.luaf(LUA)
    Q('player.soul:AddBuff("%s"); System.LogAlways("@TAG ok")' % GUARD_BUFF, 'buff')
    Q('local t=Calendar.GetWorldTime(); local d=math.floor(t/86400); local h=(t/3600)%%24; if h > %.2f then d=d+1 end; Calendar.SetWorldTime(d*86400+%.2f*3600); System.LogAlways("@TAG set")' % (hour, hour), 'time')
    time.sleep(2)


def freeze(on=True):
    """The world clock almost stopped (the NPCs keep their night shift for the whole run); off = the ratio the game had."""
    if on:
        Q('W195B.ratio = Calendar.GetWorldTimeRatio(); Calendar.SetWorldTimeRatio(0.0005); System.LogAlways("@TAG ratio was "..tostring(W195B.ratio))', 'fz')
    else:
        Q('Calendar.SetWorldTimeRatio(W195B.ratio or 1); System.LogAlways("@TAG back "..tostring(Calendar.GetWorldTimeRatio()))', 'fz')


def pose(npc):
    s = Q('local e=System.GetEntityByName("%s"); local f=e:GetDirectionVector(1); local q=e:GetWorldPos(); System.LogAlways(string.format("@TAG %%.3f %%.3f %%.3f %%.4f %%.4f", q.x,q.y,q.z,f.x,f.y))' % npc, 'pz')
    return list(map(float, s.split())) if s else None


def me():
    s = Q('System.LogAlways("@TAG "..W195B_Me())', 'me', 4)
    return dict((kv.split('=')[0], float(kv.split('=')[1])) for kv in (s or '').split()) if s else {}


def hour():
    s = Q('local t=Calendar.GetWorldTime(); System.LogAlways(string.format("@TAG %.2f", (t/3600)%24))', 'hr', 4)
    return float(s) if s else -1


def place(npc, d, deg):
    """Henry d m from the NPC, deg degrees off its facing (0 = in front, 180 = behind), looking at it."""
    x0, y0, z0, fx, fy = pose(npc)
    a = math.atan2(fy, fx) + math.radians(deg)
    x, y = x0 + math.cos(a) * d, y0 + math.sin(a) * d
    z = S.ground(x, y)
    ang = math.atan2(-(x0 - x), (y0 - y))
    live.lua('W195B_Go(%f,%f,%f,%f)' % (x, y, z + 0.3, ang))
    return x, y, z


def away(npc):
    """Henry far from the observer (it forgets him), the observer allowed to go back to its post."""
    x0, y0, z0, fx, fy = pose(npc)
    x, y = x0 - 150, y0 - 90
    live.lua('W195B_Go(%f,%f,%f,0)' % (x, y, S.ground(x, y) + 0.3))


NOTICE = re.compile(r'NPC_REAGUJE_NA_HRACE_BEZ_POCHODNE|NPC_VIDI_HRACE_SE_ZBRANI')


def timeline(n0, npc):
    """The log from n0 on, in order: (tick t, d, a, los, vir, vib, fsm, noi, con) per sampler row and the notice barks of npc by tick."""
    with open(live.KLOG, 'rb') as f:
        f.seek(n0)
        lines = f.read().decode('utf-8', 'replace').splitlines()
    ticks, barks, cur = [], [], -1
    for l in lines:
        m = re.search(r'\[W195B\] c t=([\d.]+) me\[vib=([-\d.]+) fsm=([-\d.]+) noi=([-\d.]+) con=([-\d.]+) evi=([-\d.]+)\] %s d=([\d.]+) a=(\d+) dz=([-\d.]+) sl=(\d) sig=(\d+) los=(\d) vir=([-\d.]+) aw=(\d)' % npc, l)
        if m:
            g = m.groups()
            ticks.append(dict(t=float(g[0]), vib=float(g[1]), fsm=float(g[2]), noi=float(g[3]), con=float(g[4]), d=float(g[6]), a=int(g[7]), dz=float(g[8]),
                              sl=int(g[9]), sig=g[10], los=int(g[11]), vir=float(g[12]), aw=int(g[13])))
            cur = len(ticks) - 1
            continue
        if re.search(r"Attempting to start new dialogue .*Ex: %s[ ']" % npc, l):
            mm = re.search(r'meta override: (\w+)', l)
            kind = mm.group(1) if mm else '-'
            if NOTICE.search(kind): barks.append((cur, kind))
    return ticks, barks


def wait_post(npc, post, tmax=75):
    t0 = time.time()
    while time.time() - t0 < tmax:
        p = pose(npc)
        if p and math.hypot(p[0] - post[0], p[1] - post[1]) < 1.5: return True
        time.sleep(4)
    return False


def case(npc, d, deg, stance, window=12.0, tag='night', post=None):
    place(npc, d, deg)
    time.sleep(1.5)
    n0 = S.klen()
    live.lua('W195B_Start("c",{"%s"},1000,%d)' % (npc, int(window)))
    time.sleep(window + 2.0)
    ticks, barks = timeline(n0, npc)
    first = barks[0][0] if barks else None
    use = ticks[max(first, 0)] if (first is not None and ticks and first < len(ticks)) else (ticks[len(ticks) // 2] if ticks else {})
    rec = dict(tag=tag, npc=npc, d_req=d, deg_req=deg, stance=stance, hour=hour(), yes=bool(barks), barks=len(barks), first_tick=first,
               kinds=sorted(set(k for _, k in barks)), d=use.get('d'), a=use.get('a'), los=use.get('los'), vir=use.get('vir'), vib=use.get('vib'),
               vib_min=min([t['vib'] for t in ticks]) if ticks else None, vib_max=max([t['vib'] for t in ticks]) if ticks else None,
               fsm=use.get('fsm'), noi_peak=max([t['noi'] for t in ticks]) if ticks else None, con=use.get('con'),
               d_min=min([t['d'] for t in ticks]) if ticks else None, d_max=max([t['d'] for t in ticks]) if ticks else None, ticks=len(ticks))
    record(**rec)
    say('%s %s d=%s deg=%s -> %s (barks=%d first=%s) a=%s los=%s vir=%s vib=%s' % (tag, stance, d, deg, 'YES' if barks else 'no', len(barks), first, rec['a'], rec['los'], rec['vir'], rec['vib']))
    return rec


def block(npc, ds, degs, stance, tag='night', window=12.0):
    """One block: stance fixed, each facing, the distances far to near; the block stops at the first YES (nearer is surely a YES)."""
    post = pose(npc)
    if stance == 'crouch':
        Q('PlayerStateHandler.ChangeStance(%s, "crouch"); System.LogAlways("@TAG x")' % WUID, 'cr')
        time.sleep(2)
    for deg in degs:
        for d in sorted(ds, reverse=True):
            r = case(npc, d, deg, stance, window, tag, post)
            away(npc)
            time.sleep(4)
            if not wait_post(npc, post): say('observer is off its post:', pose(npc))
            if r['yes']: break


def light(mode):
    """day = the real clock; night = a faked night (the lighting and the game's own night test; the NPCs keep their day shift)."""
    if mode == 'night':
        Q('Calendar.SetFakeTimeOfDay(23.5); System.LogAlways("@TAG x")', 'lt')
    else:
        Q('Calendar.UnfakeTimeOfDay(); System.LogAlways("@TAG x")', 'lt')
    time.sleep(5)


BUCKETS = {'front': [0, 20, -20, 35, -35], 'side': [90, -90, 70, -70, 110, -110], 'back': [180, 160, -160, 145, -145]}


def pick(npc, d, bucket):
    """Teleport Henry to a spot d m from the NPC in the facing bucket with a clear line (front/side) or any line (back); returns the angle used."""
    x0, y0, z0, fx, fy = pose(npc)
    for off in BUCKETS[bucket]:
        a = math.atan2(fy, fx) + math.radians(off)
        x, y = x0 + math.cos(a) * d, y0 + math.sin(a) * d
        tz = S.ground(x, y)
        if abs(tz - z0) > 3.0: continue
        z = z0
        ang = math.atan2(-(x0 - x), (y0 - y))
        live.lua('W195B_Go(%f,%f,%f,%f)' % (x, y, z + 0.1, ang))
        time.sleep(0.6)
        los = Q('System.LogAlways("@TAG "..W195B_Los("%s"))' % npc, 'los', 3)
        if bucket == 'back' or los == '1': return off
    return None


def wcase(npc, d, bucket, stance, lt, window=10.0):
    off = pick(npc, d, bucket)
    if off is None:
        say(npc, d, bucket, 'no spot with a clear line'); return None
    live.lua('W195B_Quiet("%s", 45, 600)' % npc)
    live.lua('W195B_Draw(true)')
    time.sleep(1.0)
    n0 = S.klen()
    live.lua('W195B_Start("c",{"%s"},1000,%d)' % (npc, int(window)))
    time.sleep(window + 2.0)
    ticks, barks = timeline(n0, npc)
    live.lua('W195B_Draw(false)')
    first = barks[0][0] if barks else None
    use = ticks[first] if (first is not None and first < len(ticks)) else (ticks[len(ticks) // 2] if ticks else {})
    rec = dict(tag='weapon', npc=npc, d_req=d, bucket=bucket, off=off, stance=stance, light=lt, yes=bool(barks), barks=len(barks), first_tick=first,
               kinds=sorted(set(k for _, k in barks)), d=use.get('d'), a=use.get('a'), los=use.get('los'), vir=use.get('vir'), vib=use.get('vib'),
               vib_min=min([t['vib'] for t in ticks]) if ticks else None, vib_max=max([t['vib'] for t in ticks]) if ticks else None,
               fsm=use.get('fsm'), noi_peak=max([t['noi'] for t in ticks]) if ticks else None, con=use.get('con'), ticks=len(ticks),
               night=Q('System.LogAlways("@TAG "..tostring(Calendar.IsNightTimeOfDay()))', 'nt', 3))
    record(**rec)
    say('%s %s %s %s d=%s -> %s (barks=%d first=%s) a=%s los=%s vir=%s vib=%s night=%s' % (npc, lt, stance, bucket, d, 'YES' if barks else 'no', len(barks), first, rec['a'], rec['los'], rec['vir'], rec['vib'], rec['night']))
    return rec


def wblock(npc, lt, stance, cells):
    """cells: [(bucket, [d..])]: each facing, the distances far to near, stopping at the first YES (nearer is surely a YES)."""
    post = pose(npc)
    light(lt)
    if stance == 'crouch':
        Q('PlayerStateHandler.ChangeStance(%s, "crouch"); System.LogAlways("@TAG x")' % WUID, 'cr')
        time.sleep(2)
    for bucket, ds in cells:
        for d in sorted(ds, reverse=True):
            r = wcase(npc, d, bucket, stance, lt)
            away(npc)
            time.sleep(3)
            if not wait_post(npc, post, 40): say('observer is off its post:', pose(npc))
            if r and r['yes']: break


def draw_real(g, tries=2):
    """The WO-184 recipe: the game's own draw key (1) readies the weapon, DrawWeapon completes it; IsWeaponDrawn is polled."""
    for _ in range(tries):
        g.tap('1'); time.sleep(1.0)
        live.lua('pcall(function() if not player.human:IsWeaponDrawn() then player.human:DrawWeapon() end end)')
        for _i in range(8):
            time.sleep(0.5)
            if Q('System.LogAlways("@TAG "..tostring(player.human:IsWeaponDrawn()))', 'dw', 2) == 'true': return True
    return False


def ticks_of(tag, n0):
    return len(S.kcd_lines(r'\[W195B\] %s t=' % tag, n0))


def crowd(cond, radius=32, secs=45, tag='crowd', weapon=True):
    """One condition on a fresh load: Henry still at the yard spot with the weapon drawn (the real key); every NPC within radius sampled at 1 Hz:
    d, a (its facing to Henry), los, vir and the recognition bits of the engine (sig bit0 = level I, bit1 = level II)."""
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'dev-input'))
    import game_input
    lt, stance = cond
    spot = (2336.0, 2070.0, 111.0)
    setup_only()
    if lt == 'realnight':
        setup(21.0)
        time.sleep(20)
    else:
        light(lt)
    if stance == 'crouch':
        Q('PlayerStateHandler.ChangeStance(%s, "crouch"); System.LogAlways("@TAG x")' % WUID, 'cr')
        time.sleep(2)
    live.lua('W195B_Go(%f,%f,%f,0)' % spot)
    time.sleep(4)
    names = Q('local o={}; local p=player:GetWorldPos(); for _,e in ipairs(System.GetEntitiesInSphere(p, %d) or {}) do if (e.class=="NPC" or e.class=="NPC_Female") and e.GetName then o[#o+1]=string.format("%%q", e:GetName()) end end; System.LogAlways("@TAG {"..table.concat(o,",").."}")' % radius, 'nm', 6)
    n0 = S.klen()
    live.lua('W195B_Start("%s", %s, 1000, %d)' % (tag, names, secs))
    time.sleep(3)
    if not weapon:
        time.sleep(secs + 3)
        return n0, False, spot, cond, tag
    live.lua('W195B_Equip()'); time.sleep(1.0)
    g = game_input.Game('wo195b-crowd')
    g.front(2.0)
    ok = draw_real(g)
    g.back()
    t0 = time.time(); last = 0; stalls = 0
    while time.time() - t0 < secs + 6:
        time.sleep(3)
        k = ticks_of(tag, n0)
        if k == last and k < secs:
            stalls += 1
            if stalls >= 2:
                g.front(1.5); g.tap('esc'); g.back(); stalls = 0
                say('the sampler stalled: Escape sent (a popup)')
        else: stalls = 0
        last = k
    return n0, ok, spot, cond, tag


def parse_crowd(n0, tag):
    with open(live.KLOG, 'rb') as f:
        f.seek(n0); lines = f.read().decode('utf-8', 'replace').splitlines()
    series = {}
    me_rows = []
    barks = {}
    cur_t = 0.0
    for l in lines:
        bm = re.search(r"Attempting to start new dialogue .*Ex: (\S+) - meta override: (NPC_REAGUJE_NA_HRACE_BEZ_POCHODNE|NPC_VIDI_HRACE_SE_ZBRANI\w*|NPC_REAGUJE_ZANDANI_ZBRANE|NPC_REAGUJE_NA_STAV_I+_?\w*)", l)
        if bm: barks.setdefault(bm.group(1), []).append((cur_t, bm.group(2)))
        m = re.search(r'\[W195B\] %s t=([\d.]+) me\[vib=([-\d.]+) fsm=([-\d.]+) noi=([-\d.]+) con=([-\d.]+) evi=([-\d.]+)\] (.*)$' % tag, l)
        if not m: continue
        t = float(m.group(1)); cur_t = t; me_rows.append([t] + [float(x) for x in m.groups()[1:6]])
        for part in m.group(7).split(' | '):
            r = re.match(r'(\S+) d=([\d.]+) a=(\d+) dz=([-\d.]+) sl=(\d) sig=(\d+) los=(\d) vir=([-\d.]+) aw=(\d)', part.strip())
            if r:
                g = r.groups()
                series.setdefault(g[0], []).append(dict(t=t, d=float(g[1]), a=int(g[2]), dz=float(g[3]), sig=g[5], los=int(g[6]), vir=float(g[7]), aw=int(g[8])))
    return series, me_rows, barks


def setup_only():
    """A clean load, the helpers, the guard buff (the harness only)."""
    import subprocess
    r = subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'run195.py'), 'load', 'save033'], capture_output=True, text=True, timeout=240)
    time.sleep(12)
    live.luaf(LUA)
    Q('player.soul:AddBuff("%s"); System.LogAlways("@TAG ok")' % GUARD_BUFF, 'buff')


def crowd_report(series, me_rows, cond, tag, barks=None):
    barks = barks or {}
    out = []
    for n, rows in series.items():
        vir = max(r['vir'] for r in rows)
        lvl1 = next((r['t'] for r in rows if r['sig'][0] == '1'), None)
        lvl2 = next((r['t'] for r in rows if r['sig'][1] == '1'), None)
        sleeping = rows[0]['sig'][8] == '1'
        near = min(rows, key=lambda r: r['d'])
        aw0 = rows[0]['aw']; awt = next((r['t'] for r in rows if r['aw']), None)
        out.append(dict(npc=n, cond=cond, sleeping=sleeping, vir=vir, lvl1=lvl1, lvl2=lvl2, aw0=aw0, aw_t=awt, barks=barks.get(n, []), d_min=near['d'], d_med=sorted(r['d'] for r in rows)[len(rows) // 2],
                        a_med=sorted(r['a'] for r in rows)[len(rows) // 2], los_frac=sum(r['los'] for r in rows) / len(rows), ticks=len(rows)))
    vib = [r[1] for r in me_rows]
    record(tag='crowd', cond=list(cond), vib_min=min(vib) if vib else None, vib_max=max(vib) if vib else None, fsm=me_rows[-1][2] if me_rows else None, npcs=out)
    return out


def sl_scan(tag, ox, oy, oz, r):
    """[(name, d, dz, los, asleep, x, y, z)] around an origin (W195B_Sl)."""
    n = S.klen()
    live.lua('W195B_Sl("%s", %f, %f, %f, %f)' % (tag, ox, oy, oz, r))
    time.sleep(1.2)
    ls = S.kcd_lines(r'\[W195B\] sl %s ' % tag, n)
    out = []
    if ls:
        for part in ls[-1].split('sl %s ' % tag, 1)[1].split(' '):
            f = part.split(':')
            if len(f) == 8: out.append((f[0], float(f[1]), float(f[2]), int(f[3]), int(f[4]), float(f[5]), float(f[6]), float(f[7])))
    return out


def clusters(sleepers, rad=7.0):
    cl = []
    for s_ in sleepers:
        for c in cl:
            if math.hypot(s_[5] - c['x'], s_[6] - c['y']) < rad and abs(s_[7] - c['z']) < 2.5:
                c['m'].append(s_); n_ = len(c['m']); c['x'] += (s_[5] - c['x']) / n_; c['y'] += (s_[6] - c['y']) / n_; break
        else:
            cl.append(dict(x=s_[5], y=s_[6], z=s_[7], m=[s_]))
    return sorted(cl, key=lambda c: -len(c['m']))


def wake_grid(mult, kind=3, ncl=3, wait=9.0):
    """One fresh night: for the largest sleeper clusters one engine sound (ProduceSound kind, mult) at the cluster's centre; which sleepers
    got up within `wait` s, with their distance, height difference and line from the sound."""
    setup_only()
    setup(23.0)
    time.sleep(55)
    hx, hy, hz = 2344.0, 2049.2, 108.3
    allp = [x for x in sl_scan('all', hx, hy, hz, 140) if x[4] == 1]
    cl = [c for c in clusters(allp) if len(c['m']) >= 2][:ncl]
    say('mult=%s kind=%s: %d sleepers, clusters %s' % (mult, kind, len(allp), [(round(c['x']), round(c['y']), len(c['m'])) for c in cl]))
    res = []
    for c in cl:
        ox, oy, oz = c['x'], c['y'], c['z'] + 0.3
        live.lua('W195B_Go(%f,%f,%f,0)' % (ox + 8, oy + 8, S.ground(ox + 8, oy + 8) + 0.5)); time.sleep(3)
        before = {x[0]: x for x in sl_scan('b', ox, oy, oz, 30) if x[4] == 1}
        live.lua('W195B_Sound(%d, %f, %f, %f, %s)' % (kind, ox, oy, oz, mult))
        time.sleep(wait)
        after = {x[0]: x for x in sl_scan('a', ox, oy, oz, 30)}
        for n_, b in before.items():
            a = after.get(n_)
            woke = (a is not None and a[4] == 0)
            rec = dict(tag='wake', kind=kind, mult=mult, npc=n_, d=b[1], dz=b[2], los=b[3], woke=woke, origin=[round(ox, 1), round(oy, 1), round(oz, 1)])
            record(**rec); res.append(rec)
        say('  cluster (%.0f,%.0f): %s' % (ox, oy, ' '.join('%s d=%.1f los=%d %s' % (r['npc'][5:], r['d'], r['los'], 'WOKE' if r['woke'] else 'asleep') for r in res[-len(before):])))
    return res


def wake_pairs(mult, kind=3, wait=9.0, settle=100, schedule=(0.5, 0.7, 0.9, 1.1, 1.3, 1.5, 1.8, 2.2, 2.6, 3.0, 4.0, 5.0)):
    """One fresh night: each sleeper cluster gets ONE engine sound (ProduceSound kind, mult) at a scheduled distance from its first sleeper (a spot
    with a clear line from the sleeper when there is one); which sleepers got up within `wait` s. Distances are real (sleeper to sound)."""
    setup_only()
    setup(23.0)
    time.sleep(settle)
    hx, hy, hz = 2344.0, 2049.2, 108.3
    allp = [x for x in sl_scan('all', hx, hy, hz, 140) if x[4] == 1]
    cl = clusters(allp, 6.0)
    say('mult=%s kind=%s: %d sleepers in %d clusters' % (mult, kind, len(allp), len(cl)))
    out = []
    for ci, c in enumerate(cl[:len(schedule)]):
        d_want = schedule[ci]
        s0 = c['m'][0]
        sx, sy, sz = s0[5], s0[6], s0[7]
        best = None
        for deg in (0, 90, 180, 270):
            a = math.radians(deg)
            ox, oy, oz = sx + math.cos(a) * d_want, sy + math.sin(a) * d_want, sz + 0.8
            ls = sl_scan('p', ox, oy, oz, d_want + 1.0)
            me = [x for x in ls if x[0] == s0[0]]
            if me and me[0][3] == 1: best = (ox, oy, oz, me[0]); break
            if best is None and me: best = (ox, oy, oz, me[0])
        if not best: continue
        ox, oy, oz, me0 = best
        live.lua('W195B_Go(%f,%f,%f,0)' % (ox + 2.5, oy, oz - 0.5))   # Henry close (the engine only runs the AI near him)
        time.sleep(2.5)
        before = {x[0]: x for x in sl_scan('b', ox, oy, oz, 30) if x[4] == 1}
        live.lua('W195B_Sound(%d, %f, %f, %f, %s)' % (kind, ox, oy, oz, mult))
        time.sleep(wait)
        after = {x[0]: x for x in sl_scan('a', ox, oy, oz, 40)}
        out.append((ox, oy, oz, before, after))
    res = []
    for ox, oy, oz, before, after in out:
        for n_, b in before.items():
            a = after.get(n_)
            woke = (a is not None and a[4] == 0)
            rec = dict(tag='wake', kind=kind, mult=mult, npc=n_, d=b[1], dz=b[2], los=b[3], woke=woke, origin=[round(ox, 1), round(oy, 1), round(oz, 1)])
            record(**rec); res.append(rec)
    res.sort(key=lambda r: r['d'])
    say(' '.join('%.1f%s%s' % (r['d'], 'L' if r['los'] else '', '*' if r['woke'] else '.') for r in res))
    return res


def walk_wake(trials=(('stand', 'walk'), ('stand', 'walk'), ('stand', 'walk')), d0=3.0, secs=6.0, settle=100):
    """Henry walks toward a sleeper with the game's own movement key (real input to the game window), 4 Hz samples of the distance, the
    sleeper's engine flag (crime_sleeping) and Henry's stealth stats: the distance at which the sleeper got up is the engine's answer."""
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'dev-input'))
    import game_input
    setup_only(); setup(23.0); time.sleep(settle)
    hx, hy, hz = 2344.0, 2049.2, 108.3
    allp = [x for x in sl_scan('all', hx, hy, hz, 140) if x[4] == 1]
    cl = clusters(allp, 6.0)
    say('%d sleepers in %d clusters' % (len(allp), len(cl)))
    g = game_input.Game('wo195b-walkwake')
    out = []
    used = 0
    for stance, mode in trials:
        if stance == 'crouch' and not any(t[0] == 'crouch' for t in out):
            Q('PlayerStateHandler.ChangeStance(%s, "crouch"); System.LogAlways("@TAG x")' % WUID, 'cr'); time.sleep(2)
        got = None
        while used < len(cl) and not got:
            c = cl[used]; used += 1
            s0 = c['m'][0]
            sx, sy, sz = s0[5], s0[6], s0[7]
            for deg in (0, 90, 180, 270):
                a = math.radians(deg)
                ox, oy = sx + math.cos(a) * d0, sy + math.sin(a) * d0
                ls = sl_scan('p', ox, oy, sz + 1.0, d0 + 1.0)
                me = [x for x in ls if x[0] == s0[0]]
                if me and me[0][3] == 1:   # a clear line between the spot and the sleeper
                    got = (ox, oy, sz, s0, deg); break
        if not got:
            say('no sleeper with a clear approach left'); break
        ox, oy, sz, s0, deg = got
        face = math.atan2(-(s0[5] - ox), (s0[6] - oy))
        live.lua('W195B_Go(%f,%f,%f,%f)' % (ox, oy, sz + 0.3, face)); time.sleep(3.0)
        n0 = S.klen()
        live.lua('W195B_Start("ww",{"%s"},250,%d)' % (s0[0], int(secs)))
        g.front(1.5)
        g.key('w', True)
        time.sleep(secs + 0.5)
        g.key('w', False)
        g.back()
        rows = S.kcd_lines(r'\[W195B\] ww t=', n0)
        ticks = []
        for l in rows:
            m = re.search(r'ww t=([\d.]+) me\[vib=([-\d.]+) fsm=([-\d.]+) noi=([-\d.]+) .*? d=([\d.]+) a=\d+ dz=[-\d.]+ sl=\d sig=(\d+)', l)
            if m: ticks.append((float(m.group(1)), float(m.group(2)), float(m.group(3)), float(m.group(4)), float(m.group(5)), m.group(6)))
        woke = next((t for t in ticks if t[5][8] == '0'), None)
        rec = dict(tag='walkwake', stance=stance, mode=mode, npc=s0[0], d0=d0, ticks=len(ticks), woke=bool(woke), d_wake=woke[4] if woke else None,
                   t_wake=woke[0] if woke else None, d_min=min([t[4] for t in ticks]) if ticks else None, fsm=ticks[0][2] if ticks else None,
                   vib0=ticks[0][1] if ticks else None, vib_walk=max([t[1] for t in ticks]) if ticks else None, noi_max=max([t[3] for t in ticks]) if ticks else None,
                   series=[(t[0], round(t[4], 2)) for t in ticks[::2]])
        record(**rec); out.append((stance, mode, rec))
        say('%s %s -> %s d_wake=%s d_min=%s fsm=%s vib %s..%s noi_max=%s' % (stance, mode, 'WOKE' if woke else 'asleep', rec['d_wake'], rec['d_min'], rec['fsm'], rec['vib0'], rec['vib_walk'], rec['noi_max']))
    return out


def approach(npc, ds, deg, stance, window=10.0, hour_set=22.0, settle=22):
    """A fresh night (the shift of the guard starts): Henry, with no torch, is put at each distance in turn on one line toward the NPC (never
    sent away between), far to near; the first bark is the engine's answer and the series stops there. One record per distance."""
    setup_only(); setup(hour_set); time.sleep(settle)
    if stance == 'crouch':
        Q('PlayerStateHandler.ChangeStance(%s, "crouch"); System.LogAlways("@TAG x")' % WUID, 'cr'); time.sleep(2)
    out = []
    for d in sorted(ds, reverse=True):
        r = case(npc, d, deg, stance, window, 'approach')
        out.append(r)
        if r['yes']: break
    return out


if __name__ == '__main__':
    v = sys.argv[1]
    if v == 'setup': setup(float(sys.argv[2]) if len(sys.argv) > 2 else 23.5)
    elif v == 'freeze': freeze(sys.argv[2] != 'off' if len(sys.argv) > 2 else True)
    elif v == 'night':
        npc = sys.argv[2]
        ds = [float(x) for x in (sys.argv[3] if len(sys.argv) > 3 else '3,8,14,20').split(',')]
        degs = [float(x) for x in (sys.argv[4] if len(sys.argv) > 4 else '0').split(',')]
        block(npc, ds, degs, sys.argv[5] if len(sys.argv) > 5 else 'stand')
    elif v == 'wblock':
        cells = [(c.split(':')[0], [float(x) for x in c.split(':')[1].split(',')]) for c in sys.argv[5].split(';')]
        wblock(sys.argv[2], sys.argv[3], sys.argv[4], cells)
    elif v == 'crowd':
        cond = (sys.argv[2], sys.argv[3])
        n0, ok, spot, cond, tag = crowd(cond, weapon=(len(sys.argv) < 5 or sys.argv[4] != 'none'))
        series, me_rows, barks = parse_crowd(n0, 'crowd')
        rep = crowd_report(series, me_rows, cond, 'crowd', barks)
        say('drawn=%s npcs=%d' % (ok, len(rep)))
        for r in sorted(rep, key=lambda r: r['d_min'])[:40]: print('%-34s sl=%d vir=%4.1f d_min=%5.1f d_med=%5.1f a=%3d los=%.2f lvl1=%s lvl2=%s barks=%d first=%s' % (r['npc'], r['sleeping'], r['vir'], r['d_min'], r['d_med'], r['a_med'], r['los_frac'], r['lvl1'], r['lvl2'], len(r['barks']), r['barks'][0][0] if r['barks'] else None))
    elif v == 'wake':
        wake_grid(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 3)
    elif v == 'wakepairs':
        wake_pairs(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 3)
    elif v == 'walkwake':
        walk_wake()
    elif v == 'approach':
        approach(sys.argv[2], [float(x) for x in sys.argv[3].split(',')], float(sys.argv[4]), sys.argv[5], float(sys.argv[6]) if len(sys.argv) > 6 else 10.0)
    elif v == 'me': print(me(), hour())
