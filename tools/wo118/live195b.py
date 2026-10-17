# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-195B live checks 2a-2d on the host (never shipped): the throwaway playline4 only. The host's own world, a synthetic joiner's figure
from the WO-195 rig (relay + agent + peer), the host calls the Lua entry points the agent calls.

usage (W195B_SP = the rig; KCD2MP_INSTALL = the Modding Tools folder):
  python live195b.py up                 relay, agent and the synthetic joiner's figure (a fresh night is NOT set here)
  python live195b.py c2c                sleepers: a walking figure beside one wakes it the game's way; a still one beside another does not
  python live195b.py c2d                pickpocket: from behind, crouching, nothing seen; face to face, a witness
  python live195b.py c2b                takedown: a knockout on a guard from behind: knocked out (not dead), judged, and it wakes later
  python live195b.py c2w                WO-197: three more walking trials beside sleepers c2c did not use
  python live195b.py c2a                chest: a locked chest unlocked on the joiner's claim; saved; reloaded
Results: <W195B_SP>/live195b.results.jsonl
"""
import json, math, os, re, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.environ.setdefault('W195_SP', os.environ.get('W195B_SP', ''))
import live
import sit165 as S
import run165 as R
import run195 as H
import run195b as B

RESULTS = os.path.join(B.SP, 'live195b.results.jsonl')
KEY = '494ed6dc-51b9-444f-8055-3116e08a5329'


def say(*a): print(time.strftime('%H:%M:%S'), *a, flush=True)


def rec(step, **kw):
    kw.update(step=step, at=time.strftime('%H:%M:%S'))
    open(RESULTS, 'a').write(json.dumps(kw) + '\n')
    say(step, json.dumps(kw)[:400])


def up(spot=(2344.0, 2052.0, 108.5)):
    R.stop('peer'); R.stop('agent'); time.sleep(5)
    R.relay(H.RELAY)
    R.agent('w195b', H.AGENT, [])
    plan = os.path.join(live.LOGS, 'plan.live195b.txt')
    open(plan, 'w').write('# WO-195B live checks: the synthetic joiner stands near Henry\nstart 2\nghost %.2f %.2f %.2f 1 0 0.01 0.01\n' % spot)
    R.peer('w195b', plan, ['--duration', '3000'])
    time.sleep(8)
    ok = B.Q('local g=KCD2MP.ghosts and KCD2MP.ghosts["1"]; System.LogAlways("@TAG ghost "..tostring(g and g.entity ~= nil))', 'gh', 5)
    say('ghost', ok)


GK = None


def gk():
    """The key of the synthetic joiner's figure in KCD2MP.ghosts (the relay numbers the peers: 0 or 1)."""
    global GK
    if GK is None:
        s = B.Q('local k=nil; for key,v in pairs(KCD2MP.ghosts or {}) do k=key end; System.LogAlways("@TAG "..tostring(k))', 'gk', 4)
        GK = s.strip() if s else '1'
    return GK


def ghost_to(x, y, z, stance='s'):
    live.lua('local g=KCD2MP.ghosts["%s"]; if g and g.entity then g.entity:SetWorldPos({x=%f, y=%f, z=%f}); g.istate = g.istate or {}; g.istate.stance = "%s"; g.istate.smoothedSpeed = 0 end' % (gk(), x, y, z, stance))
    time.sleep(1.2)


def ghost_at(x, y, z, stance='s'):
    """The synthetic joiner's figure streamed to (x, y, z) by a restarted peer (the stream owns the figure's place); stance set on its state."""
    R.stop('peer'); time.sleep(3)
    plan = os.path.join(live.LOGS, 'plan.live195b.txt')
    open(plan, 'w').write('# WO-195B live checks: the synthetic joiner at the spot\nstart 1\nghost %.2f %.2f %.2f 1 0 0.01 0.01\n' % (x, y, z))
    R.peer('w195b', plan, ['--duration', '3000'])
    for _ in range(20):
        time.sleep(1.5)
        s = B.Q('local g=KCD2MP.ghosts["%s"]; local p=g and g.entity and g.entity:GetWorldPos(); System.LogAlways(p and string.format("@TAG %%.2f %%.2f %%.2f", p.x, p.y, p.z) or "@TAG none")' % gk(), 'gp', 3)
        if s and s != 'none':
            f = list(map(float, s.split()))
            if math.hypot(f[0] - x, f[1] - y) < 1.5: break
    live.lua('local g=KCD2MP.ghosts["%s"]; if g then g.istate = g.istate or {}; g.istate.stance = "%s"; g.istate.smoothedSpeed = 0 end' % (gk(), stance))
    time.sleep(1.0)


def judge(kind, spot, stealth, tag, victim='-', where='body'):
    k0 = S.klen()
    live.lua('KCD2MP_W139HostJudge(%s, %d, "%s", %.2f, %.2f, %.2f, "%s", "-", "%s", %s)' % (gk(), tag, kind, spot[0], spot[1], spot[2], victim, where, ('"%s"' % stealth) if stealth else 'nil'))
    time.sleep(2.0)
    return S.kcd_lines(r'WO139-JUDGE src=%s id=%d |WO195-WITNESS id=%d |WO195-WAKE|WO195-PICK|WO157-TRESPASS src=%s id=%d' % (gk(), tag, tag, gk(), tag), k0), k0


def flags(names):
    out = {}
    for n in names:
        s = B.Q('local e=System.GetEntityByName("%s"); local sl=false; pcall(function() sl=e.soul:HasScriptContext("crime_sleeping") end); local q=e:GetWorldPos(); System.LogAlways(string.format("@TAG %%s %%.2f %%.2f %%.2f", tostring(sl), q.x, q.y, q.z))' % n, 'fl', 4)
        if s:
            f = s.split()
            out[n] = (f[0] == 'true', float(f[1]), float(f[2]), float(f[3]))
    return out


def sleepers(maxn=6):
    allp = [x for x in B.sl_scan('all', 2344.0, 2049.2, 108.3, 140) if x[4] == 1]
    cl = B.clusters(allp, 6.0)
    return [c['m'][0] for c in cl[:maxn]]


def c2c():
    ss = sleepers()
    say('sleepers (one per house):', [(s[0], round(s[5]), round(s[6])) for s in ss])
    out = []
    for i, (s0, stealth, label) in enumerate([(ss[0], 'c=0,l=0,v=-0.95,n=0.43,s=0.60,k=7', 'walking'), (ss[1], 'c=0,l=0,v=-0.95,n=0.00,s=0.60,k=7', 'still'),
                                                (ss[2], 'c=1,l=0,v=-1.45,n=0.129,s=0.10,k=7', 'crouch-walking')]):
        x, y, z = s0[5], s0[6], s0[7]
        ox = x + 0.7
        ghost_to(ox, y, z + 0.1)
        live.lua('W195B_Go(%f,%f,%f,0)' % (x + 3.0, y, z + 0.3)); time.sleep(2.5)
        before = flags([s0[0]]).get(s0[0])
        lines, k0 = judge('theft', (ox, y, z), stealth, 940 + i)
        time.sleep(6)
        after = flags([s0[0]]).get(s0[0])
        moved = None
        if before and after: moved = round(math.hypot(after[1] - before[1], after[2] - before[2]) + abs(after[3] - before[3]), 2)
        wit = [l.strip()[:300] for l in lines if 'WO195-WITNESS' in l and s0[0] in l]
        wake = [l.strip()[:200] for l in lines if 'WO195-WAKE' in l]
        rec('c2c', label=label, npc=s0[0], stealth=stealth, was_asleep=before[0] if before else None, asleep_after=after[0] if after else None, moved_m=moved, witness=wit, wake=wake)
        out.append((label, before, after, wit, wake))
    return out


def c2d(npc='ttkc_man_5'):
    live.luaf(B.LUA)
    out = []
    for label, deg, stance in (('behind, crouching', 180, 'c'), ('behind, standing', 180, 's'), ('face to face, standing', 0, 's')):
        k0 = S.klen()
        live.lua('W195B_PickCase("%s", "%s", %d, "%s", "%s", %d)' % (gk(), npc, deg, stance, KEY, 950 + len(out)))
        time.sleep(2.5)
        ls = S.kcd_lines(r'WO195-PICK|WO139-JUDGE src=%s id=0 |WO195-WITNESS id=0 |WO134-BODY host-take|w134_tres' % gk(), k0)
        geo = B.Q('local g=KCD2MP.ghosts["%s"]; local p=g.entity:GetWorldPos(); local e=System.GetEntityByName("%s"); local q=e:GetWorldPos(); System.LogAlways(string.format("@TAG avatar-victim %%.2f m, victim at %%.1f,%%.1f", math.sqrt((p.x-q.x)^2+(p.y-q.y)^2), q.x, q.y))' % (gk(), npc), 'geo', 3)
        rec('c2d', label=label, npc=npc, geometry=geo, lines=[l.strip()[:330] for l in ls if (npc in l or 'WO139-JUDGE' in l or 'WO195-PICK' in l or 'host-take' in l)][:8], witnesses_logged=len([1 for l in ls if 'WO195-WITNESS' in l]))
        out.append((label, ls))
        time.sleep(3)
    return out


def c2b(npc='ttkc_man_5'):
    live.luaf(B.LUA)
    k0 = S.klen()
    live.lua('W195B_Takedown("%s", "%s", 960)' % (gk(), npc))
    t0 = time.time(); state = []
    while time.time() - t0 < 12:
        s = B.Q('local e=System.GetEntityByName("%s"); local d,u=false,false; pcall(function() d=e.actor:IsDead() end); pcall(function() u=e.actor:IsUnconscious() end); System.LogAlways("@TAG dead="..tostring(d).." ko="..tostring(u))' % npc, 'ko', 3)
        state.append((round(time.time() - t0), s)); time.sleep(2)
    ls = S.kcd_lines(r'WO135-TAKEDOWN host|w135_tdres', k0)
    live.lua('KCD2MP_W139HostViolent(%s, "%s", "knockout")' % (gk(), npc)); time.sleep(2)
    jl = S.kcd_lines(r'WO139-JUDGE src=%s|WO195-WITNESS id=0' % gk(), k0)
    rec('c2b', npc=npc, takedown=[l.strip()[:220] for l in ls][:6], state=state, judge=[l.strip()[:260] for l in jl][:6])
    # wakes later?
    t0 = time.time(); woke = None
    while time.time() - t0 < 240:
        s = B.Q('local e=System.GetEntityByName("%s"); local u=false; pcall(function() u=e.actor:IsUnconscious() end); local d=false; pcall(function() d=e.actor:IsDead() end); System.LogAlways("@TAG ko="..tostring(u).." dead="..tostring(d))' % npc, 'ko', 3)
        if s and 'ko=false' in s: woke = round(time.time() - t0); break
        time.sleep(5)
    rec('c2b-wake', npc=npc, woke_after_real_s=woke)


def c2w(skip=3, n=3):
    """WO-197 Part D: more walking trials (n = 0.43, 0.7 m) beside sleepers c2c did not use; the engine's hearing wakes most, not all
    (WO-195B: 81 % of its sounds)."""
    ss = sleepers()[skip:skip + n]
    say('sleepers:', [(s[0], round(s[5]), round(s[6])) for s in ss])
    for i, s0 in enumerate(ss):
        x, y, z = s0[5], s0[6], s0[7]
        ox = x + 0.7
        ghost_to(ox, y, z + 0.1)
        live.lua('W195B_Go(%f,%f,%f,0)' % (x + 3.0, y, z + 0.3)); time.sleep(2.5)
        before = flags([s0[0]]).get(s0[0])
        lines, k0 = judge('theft', (ox, y, z), 'c=0,l=0,v=-0.95,n=0.43,s=0.60,k=7', 970 + i)
        time.sleep(8)
        after = flags([s0[0]]).get(s0[0])
        moved = None
        if before and after: moved = round(math.hypot(after[1] - before[1], after[2] - before[2]) + abs(after[3] - before[3]), 2)
        wit = [l.strip()[:300] for l in lines if 'WO195-WITNESS' in l and s0[0] in l]
        wake = [l.strip()[:200] for l in lines if 'WO195-WAKE' in l]
        rec('c2w', npc=s0[0], was_asleep=before[0] if before else None, asleep_after=after[0] if after else None, moved_m=moved, witness=wit, wake=wake)


if __name__ == '__main__':
    v = sys.argv[1]
    if v == 'up': up()
    elif v == 'c2c': c2c()
    elif v == 'c2w': c2w()
    elif v == 'c2d': c2d(*sys.argv[2:3])
    elif v == 'c2b': c2b(*sys.argv[2:3])
