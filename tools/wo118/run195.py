# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-195 live harness (never shipped). Unattended: no focus change, no key press; the throwaway playline4 only. Uses run165.py (relay,
peer, agent, stop) and sit165.py's readers.

usage (W195_SP = the session's rig folder holding run/agent and run/relay; KCD2MP_INSTALL = the Modding Tools folder):
  python run195.py game                  start the game, wait for its menu, inject a fresh copy of KCDMP.dll
  python run195.py load <save>           wh_sys_LoadGame 4 <save>
  python run195.py near [r]              the NPCs within r m of Henry (name, distance, class, dead)
  python run195.py j1 <npc> <far_npc>    joiner role (the synthetic host): <npc> 2 m in front of Henry fights him (npc-combat, target this
                                         avatar) and dies while engaged; <far_npc> ~100 m away dies early; Henry then walks up to it.
                                         Checks: WO195-RELEASE (engaged), WO195-FALL within 1 s (combat off, ragdoll), WO195-PLAYER within
                                         2 s (combat over), no post-death WO187-OPP write, refusals; the far copy reads dead and lying.
  python run195.py j3 <npc>              joiner role, after j1 (its host still up): the game's loot screen on the dead copy through the mod's
                                         own path (the host's list: empty) -> the screen's key is remembered; its take goes out marked
  python run195.py h2                    host role: a synthetic joiner's figure; night; a sleeper in bed found; the host's own judge
                                         (KCD2MP_W139HostJudge, what the agent calls) with the figure crouched beside it, then standing lit
                                         at noon. WO195-WITNESS lines.
  python run195.py h3 <npc> <npc2>       host role: a key in <npc>'s inventory, <npc> and <npc2> killed (mp_test_hit); the host's take
                                         entry: the key comes out of <npc>'s body; <npc2>'s loot-screen key is granted once
  python run195.py status | down
Results: <W195_SP>/run195.results.jsonl
"""
import json, math, os, re, shutil, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live
import run165 as R
import sit165 as S

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
SP = os.environ.get('W195_SP') or sys.exit('set W195_SP to the session rig folder')
AGENT = os.path.join(SP, 'run', 'agent', 'KcdMpClient.dll')
RELAY = os.path.join(SP, 'run', 'relay', 'KcdMpServer.dll')
RESULTS = os.path.join(SP, 'run195.results.jsonl')
EXE = os.path.join(live.ROOT, 'Bin', 'Win64ReleaseSteamLTO_DLL', 'KingdomCome.exe')
INJECTOR = os.path.join(REPO, 'native', 'build', 'KCDMP_LauncherInjector', 'KCDMP_LauncherInjector.exe')
DLL_SRC = os.path.join(REPO, 'native', 'build', 'KCDMP', 'KCDMP.dll')
KEY = '494ed6dc-51b9-444f-8055-3116e08a5329'   # key_home, as the field's loot screen made it
GSTATE = os.path.join(SP, 'run195.game.json')
DET = 0x00000008 | 0x00000200


def say(*a): print(time.strftime('%H:%M:%S'), *a, flush=True)


def record(step, **kw):
    kw.update(step=step, at=time.strftime('%H:%M:%S'))
    open(RESULTS, 'a').write(json.dumps(kw) + '\n')
    print(json.dumps(kw, indent=1))


def gst(): return json.load(open(GSTATE)) if os.path.exists(GSTATE) else {}
def gput(s): json.dump(s, open(GSTATE, 'w'))


def alines(path, n0=0):
    with open(path, encoding='utf-8', errors='replace') as f:
        return f.read().splitlines()[n0:]


def asize(path):
    with open(path, encoding='utf-8', errors='replace') as f:
        return sum(1 for _ in f)


def nlog_from(off):
    with open(live.NLOG, 'rb') as f:
        f.seek(off); return f.read().decode('utf-8', 'replace').splitlines()


def game():
    s = gst()
    p = subprocess.Popen([EXE], cwd=live.ROOT, creationflags=DET)
    s['game'] = p.pid; s['launches'] = s.get('launches', 0) + 1; gput(s)
    say('game pid', p.pid)
    t0 = time.time()
    while time.time() - t0 < 300 and not live.up(): time.sleep(2)
    say('REST up' if live.up() else 'REST NOT up', 'after %.0f s' % (time.time() - t0))
    for _ in range(120):
        if S.kcd_lines(r"PlayVideoOnly 'main_menu|\[KCD2-MP\] MOD INIT", 0): break
        time.sleep(2)
    time.sleep(5)
    ddir = os.path.join(SP, 'dll'); os.makedirs(ddir, exist_ok=True)
    dll = os.path.join(ddir, 'KCDMP_w195_%d_%s.dll' % (s['launches'], time.strftime('%H%M%S')))
    shutil.copyfile(DLL_SRC, dll)
    r = subprocess.run([INJECTOR, '--pid', str(p.pid), '--dll', dll], capture_output=True, text=True)
    say('inject:', (r.stdout + r.stderr).strip()[-300:])
    record('game', pid=p.pid, dll=os.path.basename(dll))


def load(save):
    R.load('4', save)
    time.sleep(20)
    say('loaded; henry at', S.player_pos())


def near(r=30):
    s = S.ask('local p=player:GetWorldPos(); local t={}; for _,e in ipairs(System.GetEntitiesInSphere(p, %f) or {}) do if (e.class=="NPC" or e.class=="NPC_Female") and e.GetName then local q=e:GetWorldPos(); local d=math.sqrt((q.x-p.x)^2+(q.y-p.y)^2); local dead=false; pcall(function() dead=e.actor:IsDead() end); local sl=false; pcall(function() sl=e.human:IsSleeping() end); t[#t+1]=string.format("%%s:%%.0f:%%s:%%s", e:GetName(), d, dead and "dead" or "alive", sl and "asleep" or "-") end end; System.LogAlways("@TAG "..table.concat(t," "))' % r,
              'near%d' % time.time_ns(), 5.0)
    for x in (s or '').split(): print(x)


def nudge(seconds=8):
    for i in range(seconds):
        live.lua('local p = player:GetWorldPos(); player:SetWorldPos({x = p.x + %s, y = p.y, z = p.z + 0.2})' % ('0.3' if i % 2 == 0 else '-0.3'))
        time.sleep(1.0)


# ================================================================ J1: an engaged copy dies; a far copy dies and is approached

def j1(npc, far):
    R.stop('peer'); R.stop('agent'); time.sleep(5)
    R.relay(RELAY)
    g = S.ent_pos(npc); fg = S.ent_pos(far)
    if not g or not fg: raise SystemExit('no npc %s / %s' % (npc, far))
    hx, hy = g[0] - 2.0, g[1]
    hz = S.ground(hx, hy)
    for _ in range(3):
        live.lua('player:SetWorldPos({x=%f, y=%f, z=%f}); player:SetWorldAngles({x=0, y=0, z=-1.5708})' % (hx, hy, hz + 0.3)); time.sleep(1.2)
    dfar = math.hypot(fg[0] - hx, fg[1] - hy)
    say('henry', hx, hy, 'npc', g, 'far', fg, 'far at %.0f m' % dfar)
    nz, fz = S.ground(g[0], g[1]), S.ground(fg[0], fg[1])
    T_COMBAT, T_DIE, T_END, T_FAR = 8, 22, 40, 6
    plan = os.path.join(live.LOGS, 'plan.run195.j1.txt')
    open(plan, 'w').write('\n'.join([
        '# WO-195 J1 (generated by run195.py): joiner role; %s fights this Henry and dies engaged; %s dies %.0f m away' % (npc, far, dfar),
        'start 2',
        'hold %s %.2f %.2f %.2f 1.5708 drawn' % (npc, g[0], g[1], nz),
        'ncombat %d %d %s avatar:1' % (T_COMBAT, T_END, npc),
        'die %d %s' % (T_DIE, npc),
        'hold %s %.2f %.2f %.2f 0' % (far, fg[0], fg[1], fz),
        'die %d %s' % (T_FAR, far)]) + '\n')
    R.peer('w195j1', plan, ['--duration', '400', '--claim-host'])
    time.sleep(2)
    koff = S.klen(); noff = os.path.getsize(live.NLOG)
    R.agent('w195j1', AGENT, ['KCDMP_TEST_JOINER_IN_WORLD=1'])
    alog = R.load_state()['agentlog']
    nudge(6)
    t0 = time.time()
    while time.time() - t0 < T_END + 8:
        e = S.ent_pos(npc)
        if e and time.time() - t0 < T_DIE: live.lua('player:SetWorldAngles({x=0, y=0, z=%f})' % math.atan2(-(e[0] - hx), (e[1] - hy)))
        time.sleep(1.0)
    ag = alines(alog)
    nat = nlog_from(noff)
    kl = S.kcd_lines(r'WO195-|MP-OWNERDEATH|NPC-DEATH', koff)
    def grab(lines, needle, n=6): return [l[:260] for l in lines if needle in l][:n]
    rel = grab(ag, 'WO195-RELEASE npc=%s ' % npc)
    fall = grab(ag, 'WO195-FALL npc=%s ' % npc)
    player = grab(kl, 'WO195-PLAYER after npc=%s ' % npc)
    engaged = grab(ag, 'MP-W132 engage on %s' % npc, 2)
    refused = grab(ag, 'WO195-DEAD npc=%s refused' % npc, 2)
    # 0.60.5's defect: a WO187-OPP write after the death -- must be none now
    t_rel = None
    for l in nat:
        if 'WO195-RELEASE npc=%s ' % npc in l: t_rel = l[1:13]; break
    post = [l[:200] for l in nat if t_rel and l[1:13] > t_rel and ('WO187-OPP npc=%s ' % npc in l) and ('written back' in l or 'pinned to' in l or ' was=' in l)]
    nat_rel = grab(nat, 'WO195-RELEASE npc=%s ' % npc, 3)
    nat_dead = grab(nat, 'WO195-DEAD npc=%s ' % npc, 4)
    # the far copy: walk up to it
    fx, fy = fg[0] - 2.0, fg[1]
    for _ in range(3):
        live.lua('player:SetWorldPos({x=%f, y=%f, z=%f})' % (fx, fy, S.ground(fx, fy) + 0.3)); time.sleep(1.5)
    time.sleep(8)
    a0 = asize(alog); k1 = S.klen()
    live.lua('KCD2MP_W195Check("%s"); KCD2MP_W195Check("%s")' % (far, npc))
    time.sleep(3)
    chk = [l[:260] for l in alines(alog, a0) if 'WO195-CHECK' in l]
    chk_lua = S.kcd_lines(r'WO195-CHECK', k1)
    far_death = [l[:220] for l in S.kcd_lines(r'MP-OWNERDEATH npc=%s|NPC-DEATH %s' % (far, far), koff)][:6]
    far_dead = any('npc=%s ' % far in l and 'dead=yes' in l for l in chk)
    far_lying = any('npc=%s ' % far in l and 'living=no' in l for l in chk)
    fall_ok = any('combat_mode=off' in l and 'living=no' in l and 'fight_held=no' in l for l in fall)
    ms = [int(m.group(1)) for l in fall for m in [re.search(r'after_ms=(\d+)', l)] if m]
    ok = bool(rel) and bool(engaged) and fall_ok and (ms and ms[0] <= 1000) and not post \
        and any('combat_mode=false' in l for l in player) and far_dead and far_lying
    record('j1', ok=ok, npc=npc, far=far, far_m=round(dfar), engaged=engaged, release=rel, native_release=nat_rel, fall=fall, player=player,
           refusals=refused, native_dead=nat_dead, post_death_wish_writes=post, far_check=chk, far_check_lua=[l.strip()[:200] for l in chk_lua],
           far_death=far_death)


# ================================================================ J3: the game's loot screen on the dead copy, the screen's key

def body_list(name):
    return S.ask('local e=System.GetEntityByName("%s"); local t={}; if e and e.inventory then for _,w in ipairs(e.inventory:GetInventoryTable()) do local it=ItemManager.GetItem(w); if it then t[#t+1]=tostring(it.class) end end end; table.sort(t); System.LogAlways("@TAG "..table.concat(t,","))' % name,
                 'bl%d' % time.time_ns(), 4.0) or ''


def j3(npc):
    alog = R.load_state()['agentlog']
    g = S.ent_pos(npc)
    for _ in range(2):
        live.lua('player:SetWorldPos({x=%f, y=%f, z=%f})' % (g[0] - 1.2, g[1], S.ground(g[0] - 1.2, g[1]) + 0.3)); time.sleep(1.2)
    koff = S.klen(); a0 = asize(alog)
    before = body_list(npc)
    live.lua('local e=System.GetEntityByName("%s"); if e then BasicAIActions.OnLoot(e, player, 1) end' % npc)
    time.sleep(0.5)
    live.lua('KCD2MP_W134BodyState("%s", "open", 1, 1, 1, {})' % npc)   # the host's list: nothing (the field's host_items=0)
    time.sleep(2.0)
    opened = body_list(npc)
    has_key = KEY in opened
    took = None
    if has_key:
        # the take: the key from the body into Henry's pack (what the loot screen's take does)
        took = S.ask('local e=System.GetEntityByName("%s"); local w=nil; for _,x in ipairs(e.inventory:GetInventoryTable()) do local it=ItemManager.GetItem(x); if it and tostring(it.class)=="%s" then w=x end end; local ok=false; if w then ok=pcall(function() player.inventory:AddItem(w) end) end; System.LogAlways("@TAG "..tostring(w).." "..tostring(ok))' % (npc, KEY),
                     'tk%d' % time.time_ns(), 4.0)
        time.sleep(2.0)
    after = body_list(npc)
    pack = S.ask('System.LogAlways("@TAG "..tostring(player.inventory:GetCountOfClass("%s")))' % KEY, 'pk%d' % time.time_ns(), 3.0)
    live.lua('UIAction.CallFunction("ItemTransfer", -1, "Close", true)')
    time.sleep(2)
    kl = S.kcd_lines(r'WO134-BODY|WO195-LOOT|w134_take', koff)
    ag = [l[:240] for l in alines(alog, a0) if 'WO134' in l or 'LOOT' in l][:10]
    marked = [l.strip()[:240] for l in kl if 'w134_take' in l and ' screen' in l]
    ok = has_key and bool(marked) and any('WO195-LOOT screen-item' in l for l in kl)
    record('j3', ok=ok, npc=npc, before=before, opened=opened, screen_key=has_key, take=took, after=after, key_in_pack=pack,
           lua=[l.strip()[:240] for l in kl][:14], marked_take=marked, agent=ag)


# ================================================================ H2: the host's witness rule in its own world

def judge(spot, stealth, tag):
    k0 = S.klen()
    live.lua('KCD2MP_W139HostJudge(1, %d, "trespass", %.2f, %.2f, %.2f, "-", "-", "area", "%s")' % (tag, spot[0], spot[1], spot[2], stealth))
    time.sleep(1.5)
    return S.kcd_lines(r'WO139-JUDGE src=1 id=%d |WO195-WITNESS id=%d ' % (tag, tag), k0)


def h2(hours_wait=150):
    R.stop('peer'); R.stop('agent'); time.sleep(5)
    R.relay(RELAY)
    R.agent('w195h2', AGENT, [])
    nudge(6)
    hp = S.player_pos()
    # night: the game's own clock to 23:30, then the town's people go to bed (their own schedules)
    t = S.ask('local t=Calendar.GetWorldTime(); local d=math.floor(t/86400); Calendar.SetWorldTime(d*86400 + 23.5*3600); System.LogAlways("@TAG "..tostring(Calendar.GetWorldTime()))', 'wt%d' % time.time_ns())
    say('world time ->', t)
    plan = os.path.join(live.LOGS, 'plan.run195.h2.txt')
    open(plan, 'w').write('# WO-195 H2: the synthetic joiner stands near Henry\nstart 2\nghost %.2f %.2f %.2f 1 0 0.01 0.01\n' % (hp[0] + 3, hp[1], hp[2]))
    R.peer('w195h2', plan, ['--duration', '900'])
    alog = R.load_state()['agentlog']
    # wait for a sleeper: IsSleeping, or the host's own activity read lying on a bed (the agent's KCD2MP_W195Asleep)
    t0 = time.time(); sleeper = None
    while time.time() - t0 < hours_wait and not sleeper:
        s = S.ask('local p=player:GetWorldPos(); local best=nil; for _,e in ipairs(System.GetEntitiesInSphere(p, 60) or {}) do if (e.class=="NPC" or e.class=="NPC_Female") and e.GetName then local a,h=KCD2MP_W195IsAsleep(e, e:GetName()); local dead=false; pcall(function() dead=e.actor:IsDead() end); if a and not dead then local q=e:GetWorldPos(); best=string.format("%s %.2f %.2f %.2f %s %s", e:GetName(), q.x, q.y, q.z, h, tostring(System.IsPointIndoors and System.IsPointIndoors(q))) end end end; System.LogAlways("@TAG "..tostring(best))',
                  'sl%d' % time.time_ns(), 4.0)
        if s and s != 'nil': sleeper = s.split()
        else: time.sleep(5)
    if not sleeper:
        record('h2', ok=False, why='no sleeper within 60 m after %d s' % hours_wait); return
    name, sx, sy, sz, how, indoor = sleeper[0], float(sleeper[1]), float(sleeper[2]), float(sleeper[3]), sleeper[4], sleeper[5]
    say('sleeper', name, how, 'indoor', indoor)
    # the figure beside the bed (1.0 m), crouched, no light, a stealth of 7 -- the host's judge as the agent calls it
    spot = (sx + 1.0, sy, sz)
    live.lua('local g=KCD2MP.ghosts and KCD2MP.ghosts["1"]; if g and g.entity then g.entity:SetWorldPos({x=%f, y=%f, z=%f}) end' % spot)
    time.sleep(1)
    night = judge(spot, 'c=1,l=0,v=1.00,n=1.00,s=1.00,k=7', 951)
    # noon, standing, a torch: the same spot, the same NPC (awake by day or not: the line says)
    live.lua('local t=Calendar.GetWorldTime(); local d=math.floor(t/86400); Calendar.SetWorldTime(d*86400 + 12*3600)')
    time.sleep(3)
    noon = judge(spot, 'c=0,l=1,v=1.00,n=1.00,s=1.00,k=7', 952)
    def w(lines, npc): return [l.strip()[:240] for l in lines if 'npc=%s ' % npc in l]
    jn = [l.strip()[:240] for l in night if 'WO139-JUDGE' in l]
    jd = [l.strip()[:240] for l in noon if 'WO139-JUDGE' in l]
    ok = any('ignored=asleep' in l for l in w(night, name)) and bool(jd) and any('counted=' in l for l in w(noon, name) + [l for l in noon if 'counted=' in l])
    record('h2', ok=ok, sleeper=name, how=how, indoor=indoor, night_judge=jn, night_sleeper=w(night, name), night_all=[l.strip()[:200] for l in night][:12],
           noon_judge=jd, noon_sleeper=w(noon, name), noon_all=[l.strip()[:200] for l in noon][:12],
           asleep_feed=[l[:200] for l in alines(alog) if 'WO195' in l][:6])


# ================================================================ H3: the host's take of a body's key

def h3(npc, npc2):
    live.lua('local e=System.GetEntityByName("%s"); if e and e.inventory then e.inventory:CreateItem("%s", 1, 1) end' % (npc, KEY))
    time.sleep(1)
    with_key = body_list(npc)
    for n in (npc, npc2):
        for _ in range(3):
            live.lua('KCD2MP_W147TestHit("%s 400 100")' % n); time.sleep(1.5)
    time.sleep(3)
    dead = [S.ask('local e=System.GetEntityByName("%s"); System.LogAlways("@TAG "..tostring(e and e.actor:IsDead()))' % n, 'dd%d' % time.time_ns()) for n in (npc, npc2)]
    k0 = S.klen()
    live.lua('KCD2MP_W134HostTake(1, 961, "%s", "%s", 1, 1)' % (npc, KEY))
    time.sleep(1)
    after = body_list(npc)
    live.lua('KCD2MP_W134HostTake(1, 962, "%s", "%s", 1, 1, true)' % (npc2, KEY))
    time.sleep(1)
    live.lua('KCD2MP_W134HostTake(2, 963, "%s", "%s", 1, 1, true)' % (npc2, KEY))
    time.sleep(1)
    kl = [l.strip()[:240] for l in S.kcd_lines(r'w134_tres|WO134-BODY host-take|WO195-LOOT', k0)]
    ok = KEY in with_key and KEY not in after and any(' 961 ok ' in l for l in kl) and any(' 962 ok ' in l for l in kl) and any(' 963 gone ' in l for l in kl)
    record('h3', ok=ok, npc=npc, npc2=npc2, dead=dead, body_with_key=with_key, body_after=after, lines=kl[:12])


def status():
    live.lua('KCD2MP_W195Status()')
    time.sleep(1)
    print(S.kcd_lines(r'WO195-STATUS', S.klen() - 200000)[-1:])


if __name__ == '__main__':
    v = sys.argv[1]
    if v == 'game': game()
    elif v == 'load': load(sys.argv[2])
    elif v == 'near': near(float(sys.argv[2]) if len(sys.argv) > 2 else 30)
    elif v == 'j1': j1(sys.argv[2], sys.argv[3])
    elif v == 'j3': j3(sys.argv[2])
    elif v == 'h2': h2()
    elif v == 'h3': h3(sys.argv[2], sys.argv[3])
    elif v == 'status': status()
    elif v == 'down': R.stop('all')
