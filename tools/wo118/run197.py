# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-197 live harness (never shipped): the game's start (inject the rig's newest DLL), the throwaway load, and the WO-197 scenarios on top
of run190.py's. Unattended: no focus change, no key press; the throwaway playline4 only.

usage (W197_SP = the session's rig folder: run/agent, run/relay, dll/; W190_SP set to the same folder):
  python run197.py game                start the Modding Tools game, inject the newest dll/KCDMP_w197_*.dll
  python run197.py load [save]         wh_sys_LoadGame 4 <save> (quicksave022: Troskowitz, hour 8)
  python run197.py <scenario> <mode>   a run190.py scenario, or one of WO-197's (below)
"""
import glob, json, os, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live
import run165 as R

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
SP = os.environ.get('W197_SP') or sys.exit('set W197_SP to the session rig folder')
EXE = os.path.join(live.ROOT, 'Bin', 'Win64ReleaseSteamLTO_DLL', 'KingdomCome.exe')
INJECTOR = os.path.join(REPO, 'native', 'build', 'KCDMP_LauncherInjector', 'KCDMP_LauncherInjector.exe')
DET = 0x00000008 | 0x00000200


def say(*a): print(time.strftime('%H:%M:%S'), *a, flush=True)


def klines(pat, off):
    with open(live.KLOG, 'rb') as f:
        f.seek(off); return [l for l in f.read().decode('utf-8', 'replace').splitlines() if re.search(pat, l)]


def game():
    dlls = sorted(glob.glob(os.path.join(SP, 'dll', 'KCDMP_w197_*.dll')), key=os.path.getmtime)
    if not dlls: raise SystemExit('no rig DLL')
    p = subprocess.Popen([EXE], cwd=live.ROOT, creationflags=DET)
    st = R.load_state(); st['game'] = p.pid; R.save_state(st)
    say('game pid', p.pid, 'dll', os.path.basename(dlls[-1]))
    t0 = time.time()
    while time.time() - t0 < 300 and not live.up(): time.sleep(2)
    say('REST up' if live.up() else 'REST NOT up', 'after %.0f s' % (time.time() - t0))
    for _ in range(120):
        if klines(r"PlayVideoOnly 'main_menu|\[KCD2-MP\] MOD INIT", 0): break
        time.sleep(2)
    time.sleep(5)
    r = subprocess.run([INJECTOR, '--pid', str(p.pid), '--dll', dlls[-1]], capture_output=True, text=True)
    say('inject:', (r.stdout + r.stderr).strip()[-300:])


def load(save='quicksave022'):
    R.load('4', save)
    time.sleep(20)
    say('loaded')


# ================================================================ WO-197 Part B scenarios (joiner role: the run165 flow, run190's helpers)
LS_ATK = '323020d1-2ec5-39d8-811f-4448e91cf9bd'     # a longsword CombatAttackGen row (the figure's swing)
FA_LS_PB, FA_LS_BLOCK = '13df04f2-b9aa-43ab-9c1c-86914f5cdd71', 'cdaf144b-7fff-0000-96eb-1b59f87f0000'   # its failed-attack rows
CLINCH_PUNCH = '762a5ab3-1871-3630-9924-f670fd974b10'   # r_noweapon clinch punch (CombatAttackSyncGen)
CLINCH_THROW = 'ad140032-483f-3243-bd7e-76caa14032c6'
NONE = '00000000-0000-0000-0000-000000000000'
# the clinch moves (unarmed, guard type 4): "master:slave" row pairs of CombatClinchTransitionMaster/SlaveGen
CLINCH_MOVES = [tuple(x.split(':')) for x in os.environ.get('W197_CLINCH_MOVES', '').split(',') if ':' in x]


def b_plan(scen, z):
    import run187 as P
    CX, CY = P.CX, P.CY
    vil = os.environ.get('W190_VILLAGER', 'ttkc_man_3')
    if scen == 'b1recoil':
        # the figure (sword drawn, in combat) swings 8 times; 0.35 s after each, his recoil: 4 with no row (picked for his swing's weapon),
        # 2 with his engine's own row, 2 perfect-block kinds
        t0, every = 22.0, 3.0
        L = ['# WO-197 b1recoil', 'start 6', 'ghost %.2f %.2f %.2f 0 1 2 0.0005 30' % (CX, CY + 2.6, z + 0.3), 'qmode 1 on', 'gcombat 8 %d 1' % int(t0 + 8 * every + 20)]
        kinds = [('recoil', NONE), ('precoil', NONE), ('recoil', FA_LS_BLOCK), ('precoil', FA_LS_PB)] * 2
        for k, (w, row) in enumerate(kinds):
            L.append('fmove %.1f attack %s' % (t0 + k * every, LS_ATK))
            L.append('fmove %.2f %s %s' % (t0 + k * every + 0.35, w, row))
        return L, t0 + 8 * every + 6, {'recoils': len(kinds)}
    if scen == 'b2pair':
        # a village copy fights the HOST's figure 0.8 m in front of it (the victim is the figure, never this player); the host NPC's paired
        # brawl rows on the copy: the figure must play each victim half; then the figure's own paired rows: the copy plays the victim half
        vx, vy = CX + 3.0, CY + 3.0
        vz = P.ground(vx, vy)
        t0, every = 26.0, 4.0
        seq = [CLINCH_PUNCH, CLINCH_THROW] * 3
        end = t0 + len(seq) * every + 4 * every + 6
        L = ['# WO-197 b2pair', 'start 6', 'ghost %.2f %.2f %.2f 0 -1 0.0005 0.0005 30' % (vx, vy + 0.8, vz + 0.3), 'qmode 1 on', 'gcombat 8 %d 1' % int(end + 10),
             'hold %s %.2f %.2f %.2f 0' % (vil, vx, vy, vz), 'ncombat 0 %d %s host 3 1 0 3' % (int(end + 30), vil)]
        for k, g in enumerate(seq): L.append('row %.1f %s %s' % (t0 + k * every, vil, g))
        t1 = t0 + len(seq) * every
        for k in range(4): L.append('fmove %.1f attack %s' % (t1 + k * every, seq[k]))
        return L, end, {'npc_rows': len(seq), 'figure_rows': 4, 'villager': vil}
    if scen == 'b3clinch':
        vx, vy = CX + 3.0, CY + 3.0
        vz = P.ground(vx, vy)
        t0 = 26.0
        L = ['# WO-197 b3clinch', 'start 6', 'ghost %.2f %.2f %.2f 0 -1 0.0005 0.0005 30' % (vx, vy + 0.7, vz + 0.3), 'qmode 1 on', 'gcombat 8 %d 1' % int(t0 + 40),
             'hold %s %.2f %.2f %.2f 0' % (vil, vx, vy, vz), 'ncombat 0 %d %s host 3 1 0 3' % (int(t0 + 60), vil),
             'fmove %.1f clinchenter %s' % (t0, NONE)]
        for k, (m, sl) in enumerate(CLINCH_MOVES):
            L.append('fclinch %.1f %s %s 1' % (t0 + 3 + k * 4, m, sl))
        L.append('fmove %.1f clinchleave %s' % (t0 + 3 + len(CLINCH_MOVES) * 4 + 2, NONE))
        return L, t0 + 3 + len(CLINCH_MOVES) * 4 + 8, {'moves': len(CLINCH_MOVES), 'villager': vil}
    if scen == 'b6moves':
        # B6: kicks, stop moves, master strikes -- the host's figure (a longsword drawn) and a longsword copy fighting it play each
        cx, cy = CX + 3.0, CY + 3.0
        cz = P.ground(cx, cy)
        t0, every = 26.0, 3.5
        moves = [('stop', '384159e3-4658-82d8-8700-507ec0c748ad'), ('combo', '06318b34-0dc7-35d8-aac5-c5d5f1f3e62d'),
                 ('master', 'd3638b8d-a647-3ee5-963f-d3ee24ee333b'), ('kick', P.KICK)]
        end = t0 + 2 * len(moves) * every + 6
        L = ['# WO-197 b6moves', 'start 10', 'ghost %.2f %.2f %.2f 0 -1 0.0005 0.0005 30' % (cx, cy + 0.9, cz + 0.3), 'qmode 1 on', 'gcombat 8 %d 1' % int(end + 10),
             'hold %s %.2f %.2f %.2f 0 drawn' % (P.COPY, cx, cy, cz), 'hands 12 %s - %s' % (P.COPY, P.SWORD), 'ncombat 0 %d %s host 3 1 0 3' % (int(end + 30), P.COPY)]
        for k, (_, g) in enumerate(moves): L.append('row %.1f %s %s' % (t0 + k * every, P.COPY, g))
        t1 = t0 + len(moves) * every
        for k, (_, g) in enumerate(moves): L.append('fmove %.1f attack %s' % (t1 + k * every, g))
        return L, end, {'moves': [m for m, _ in moves]}
    if scen == 'b7ride':
        # Part C riding: the partner walks, mounts (the riding flag), rides at walk / trot / gallop speeds along a 60 m line, dismounts
        L = ['# WO-197 b7ride (Part C riding)', 'start 6', 'ghost %.2f %.2f %.2f 1 0 40 3.0 30' % (CX - 20.0, CY + 6.0, z + 0.3), 'qmode 1 on', 'ride 14 54', 'ride 64 84']
        return L, 96.0, {'rides': 2}
    if scen == 'b8emote':
        # Part C emotes: the partner whistles 6 times standing, 4 times walking
        L = ['# WO-197 b8emote (Part C emotes)', 'start 6', 'ghost %.2f %.2f %.2f 0 1 0.0005 0.0005 30' % (CX + 1.5, CY + 3.0, z + 0.3), 'qmode 1 on']
        for k in range(10): L.append('fmove %.1f whistle %s' % (16 + k * 4.0, NONE))
        return L, 60.0, {'whistles': 10}
    if scen == 'b4ko':
        # the partner knocked out (the engine bit + unconscious), lying 20 s, awake; then a second time
        L = ['# WO-197 b4ko', 'start 6', 'ghost %.2f %.2f %.2f 0 1 0.0005 0.0005 30' % (CX + 1.5, CY + 2.5, z + 0.3), 'qmode 1 on',
             'pvit 12 100 100 00', 'pvit 20 60 10 0D', 'pvit 40 70 40 00', 'pvit 55 60 10 0D', 'pvit 70 80 50 00']
        return L, 90.0, {'knockouts': 2}
    raise SystemExit('unknown scenario ' + scen)


def b_run(scen, mode='after'):
    import run187 as P
    import run190 as Q
    tag = '%s.%s' % (scen, mode)
    say('== WO-197', tag)
    R.stop('agent'); R.stop('peer')
    time.sleep(5)
    R.relay(Q.RELAY)
    z = P.ground(P.CX, P.CY)
    P.place_player(z)
    if scen in ('b2pair', 'b3clinch', 'b6moves'):   # this player 4 m off: the copy's opponent is the host's figure, never him
        live.lua('local p = player:GetWorldPos(); player:SetWorldPos({x = p.x - 4, y = p.y, z = p.z})')
    copy_z = P.ground(P.CX, P.CY + 1.8)
    L, end, info = b_plan(scen, copy_z)
    plan = os.path.join(live.LOGS, 'plan.run197.%s.txt' % tag)
    open(plan, 'w').write('\n'.join(L) + '\n')
    noff, koff = Q.size(live.NLOG), Q.size(live.KLOG)
    R.peer('r197' + tag, plan, ['--duration', str(int(end + 20)), '--claim-host'])
    t_peer = time.time()
    R.agent('r197' + tag, Q.AGENT, ['KCDMP_TEST_JOINER_IN_WORLD=1'])
    time.sleep(12)
    live.cmd('mp_leash off'); live.cmd('mp_anim_trace on'); live.cmd('mp_w197_status')
    Q.keep_up()
    if scen in ('b1recoil', 'b6moves'):   # the figure holds a longsword (fighitsarmed's arming)
        arm = ('local e = System.GetEntityByName("kcd2mp_0"); if e and e.inventory and e.human then '
               'pcall(function() e.inventory:CreateItem("%s", 1, 1) end); local ok, id = pcall(function() return e.inventory:FindItem("%s") end); '
               'if ok and id then pcall(function() e.human:DrawFromInventory(id, 0, true) end) end; System.LogAlways("[W197A] figure armed") end' % (P.SWORD, P.SWORD))
        for _ in range(3): live.lua(arm); time.sleep(1.5)
    deadline = t_peer + 10 + end + 4
    nxt = t_peer + 10 + 26.4 if scen == 'b6moves' else deadline + 1
    while time.time() < deadline:
        time.sleep(0.5)
        if time.time() >= nxt and nxt < t_peer + 10 + 26 + 4 * 3.5:   # B5 live: EnsureGuardAction on the fighting copy, 0.4 s into each of its rows
            live.cmd('mp_w197_check ensure %s' % P.COPY); nxt += 3.5
    live.cmd('mp_w197_status'); live.cmd('mp_w190_status')
    time.sleep(4)
    live.lua('W187KEEP = (W187KEEP or 0) + 1')
    live.cmd('mp_anim_trace off')
    nat = Q.read_from(live.NLOG, noff)
    kl = Q.read_from(live.KLOG, koff)
    st = R.load_state()
    ag = open(st['agentlog'], encoding='utf-8', errors='replace').read().splitlines() if st.get('agentlog') else []
    OUT = Q.OUT
    open(os.path.join(OUT, tag + '.native.log'), 'w', encoding='utf-8').write('\n'.join(nat))
    open(os.path.join(OUT, tag + '.agent.log'), 'w', encoding='utf-8').write('\n'.join(ag))
    open(os.path.join(OUT, tag + '.kcd.log'), 'w', encoding='utf-8').write('\n'.join(l for l in kl if 'KCD2-MP' in l or 'WO197' in l
                                                                                 or re.search(r'(?i)align|interrupt|sync|clinch|opponent', l)))
    if st.get('peerlog') and os.path.exists(st['peerlog']):
        open(os.path.join(OUT, tag + '.peer.log'), 'w', encoding='utf-8').write(open(st['peerlog'], encoding='utf-8', errors='replace').read())
    res = {'scenario': scen, 'mode': mode, 'info': info,
           'recoil_in': [l[:260] for l in ag if 'kind=AvatarRecoil' in l][:12],
           'emote_in': [l[:220] for l in ag if 'kind=Emote' in l or 'whistle' in l.lower()][:12],
           'ride': [l[:220] for l in ag + kl if 'ride' in l.lower() and ('MP-' in l or 'WO1' in l)][:16],
           'clinch_in': [l[:300] for l in ag if 'kind=AvatarClinch' in l][:12],
           'pair': [l[:300] for l in ag if l.startswith('MP-W197 pair')][:16],
           'check': [l[:240] for l in ag if l.startswith('MP-W197 check')][:12],
           'w197_stats': [l[:1200] for l in ag if l.startswith('MP-W197-STATS')][-1:],
           'native': [l.strip()[15:260] for l in nat if 'WO197-' in l][:30],
           'ko': [l[:240] for l in kl if 'WO197-KO' in l][:30], 'agent_ko': [l[:200] for l in ag if 'MP-W197 peer' in l][:8],
           'anim_figure': sum(1 for l in nat if 'WO190-ANIM' in l and 'kcd2mp_0' in l),
           'faults': [l.strip()[15:200] for l in nat if ('FAULT' in l and 'FAULT-SUM' not in l) or 'switched OFF' in l][:10]}
    json.dump(res, open(os.path.join(OUT, tag + '.json'), 'w'), indent=1)
    R.stop('agent'); R.stop('peer')
    print(json.dumps(res, indent=1)[:6000])
    return res


# ================================================================ B1 / B3 source capture, no input: this player fights by the engine's own combat
# automation (WO-154's hostfight lever) against a spawned test NPC; the echo partner (run187's echo) sends his stream back 2 s late as its figure.
ECHO_SOUL = '4f45df7c-4667-77a0-a415-d03b0cd1e293'   # a roster commoner (run187's vanilla opponent)
ECHO_NPC = 'w154_w197echo'


def echo_run(weapon='sword', secs=70, mode='after'):
    import run187 as P
    import run190 as Q
    tag = 'echo%s.%s' % (weapon, mode)
    say('== WO-197', tag)
    R.stop('agent'); R.stop('peer')
    time.sleep(5)
    R.relay(Q.RELAY)
    z = P.ground(P.CX, P.CY)
    P.place_player(z)
    plan = os.path.join(live.LOGS, 'plan.run197.%s.txt' % tag)
    open(plan, 'w').write('\n'.join(['# WO-197 echo (generated by run197.py)', 'start 4', 'qmode 1 on', 'echo 2.0 2.6']) + '\n')
    noff, koff = Q.size(live.NLOG), Q.size(live.KLOG)
    # HOST role (the joiner role pauses every NPC the host does not stream -- the opponent too): this game's agent first (the authority),
    # then the echo partner as the joiner (run187's hostpos order)
    R.agent('r197' + tag, Q.AGENT, [])
    time.sleep(1)
    live.lua('local p = player:GetWorldPos(); player:SetWorldPos({x = p.x + 0.3, y = p.y, z = p.z + 0.2})')   # a state frame for the agent
    time.sleep(10)
    R.peer('r197' + tag, plan, ['--duration', str(int(secs + 60))])
    time.sleep(14)
    live.cmd('mp_leash off'); live.cmd('mp_anim_trace on'); live.cmd('mp_w197_status')
    Q.keep_up()
    # the opponent: a commoner 2.5 m to the right (the echo figure stands 2.6 m in front), attacking this player
    live.lua('local p = player:GetWorldPos(); local yaw = player:GetWorldAngles().z; local x, y = p.x + math.cos(yaw) * 2.5, p.y + math.sin(yaw) * 2.5; '
             'local z = System.GetTerrainElevation({x=x, y=y, z=0}) or p.z; '
             'if not System.GetEntityByName("%s") then XGenAIModule.SpawnEntity({ Name = "%s", ClassName = "NPC", Pos = { x, y, z }, '
             'SharedSoulGuid = "%s", NoAI = false }) end; local e = System.GetEntityByName("%s"); '
             'if e then pcall(function() e:SetFlags(ENTITY_FLAG_NO_SAVE, 3) end) end' % (ECHO_NPC, ECHO_NPC, ECHO_SOUL, ECHO_NPC))
    time.sleep(2)
    if weapon == 'sword':   # this player and the opponent with longswords (the recoil rows exist for longswords)
        live.lua('pcall(function() player.inventory:CreateItem("%s", 1, 1) end); local ok, id = pcall(function() return player.inventory:FindItem("%s") end); '
                 'if ok and id then pcall(function() player.actor:EquipInventoryItem(id) end) end' % (P.SWORD, P.SWORD))
        live.lua('local e = System.GetEntityByName("%s"); if e then pcall(function() e.inventory:CreateItem("%s", 1, 1) end); '
                 'local ok, id = pcall(function() return e.inventory:FindItem("%s") end); if ok and id then pcall(function() e.actor:EquipInventoryItem(id) end); '
                 'pcall(function() e.human:DrawFromInventory(id, 0, true) end) end end' % (ECHO_NPC, P.SWORD, P.SWORD))
    else:   # bare hands: both holster (a brawl: combos, clinches, paired punches)
        live.lua('pcall(function() player.human:HolsterToInventory(0, false) end); pcall(function() player.human:HolsterToInventory(1, false) end)')
    time.sleep(1)
    attack = ('local e = System.GetEntityByName("%s"); if e then local t = XGenAIModule.MakeTableFromType("crime:attackInitiatedByConcept"); '
              't.target = player.this.id; t.priorityTarget = true; '
              'pcall(function() XGenAIModule.SendMessageToEntityData(e.this.id, "crime:attackInitiatedByConcept", t) end) end' % ECHO_NPC)
    live.lua(attack)
    time.sleep(2)
    live.cmd('mp_w154_check hostfight %s %d' % (ECHO_NPC, min(250, secs)))
    live.cmd('mp_w178_trace %d' % min(600, secs + 10))
    t_end = time.time() + secs
    last = time.time()
    while time.time() < t_end:
        time.sleep(1)
        if time.time() - last > 15: live.lua(attack); last = time.time()
    live.cmd('mp_w154_check hostfight off'); live.cmd('mp_w178_trace 0')
    live.cmd('mp_w197_status')
    time.sleep(5)
    live.lua('W187KEEP = (W187KEEP or 0) + 1; local e = System.GetEntityByName("%s"); if e then pcall(System.RemoveEntity, e.id) end' % ECHO_NPC)
    live.cmd('mp_anim_trace off')
    nat = Q.read_from(live.NLOG, noff)
    kl = Q.read_from(live.KLOG, koff)
    st = R.load_state()
    ag = open(st['agentlog'], encoding='utf-8', errors='replace').read().splitlines() if st.get('agentlog') else []
    OUT = Q.OUT
    open(os.path.join(OUT, tag + '.native.log'), 'w', encoding='utf-8').write('\n'.join(nat))
    open(os.path.join(OUT, tag + '.agent.log'), 'w', encoding='utf-8').write('\n'.join(ag))
    res = {'scenario': 'echo', 'weapon': weapon, 'mode': mode,
           'recoil_native': [l.strip()[15:260] for l in nat if 'WO197-RECOIL' in l][:20],
           'clinch_native': [l.strip()[15:260] for l in nat if 'WO197-CLINCH' in l][:20],
           'rowdump': [l.strip()[15:400] for l in nat if 'WO197-ROWDUMP' in l][:4],
           'out': [l[:260] for l in ag if 'section=outbound' in l and ('AvatarRecoil' in l or 'AvatarClinch' in l)][:20],
           'out_attacks': sum(1 for l in ag if 'section=outbound kind=Attack' in l),
           'in': [l[:300] for l in ag if 'section=inbound' in l and ('AvatarRecoil' in l or 'AvatarClinch' in l)][:20],
           'pair': [l[:300] for l in ag if l.startswith('MP-W197 pair')][:12],
           'w197_stats': [l[:1400] for l in ag if l.startswith('MP-W197-STATS')][-1:],
           'trace_act': [m.group(1) for l in nat for m in [re.search(r'WO178-TRACE .* act=(\S*)', l)] if m][:400],
           'fight': [l.strip()[15:200] for l in nat if 'WO154-HOSTFIGHT' in l][:4],
           'faults': [l.strip()[15:200] for l in nat if ('FAULT' in l and 'FAULT-SUM' not in l) or 'switched OFF' in l][:10]}
    acts = res.pop('trace_act')
    from collections import Counter
    res['trace_act_counts'] = dict(Counter(acts).most_common(12))
    json.dump(res, open(os.path.join(OUT, tag + '.json'), 'w'), indent=1)
    R.stop('agent'); R.stop('peer')
    print(json.dumps(res, indent=1)[:7000])
    return res


# ================================================================ Part C: a recorded host stream (rec197.py) replayed onto this joiner
def c_replay(mode='after', plan_src=None):
    import run190 as Q
    plan_src = plan_src or os.environ.get('W197_REPLAY_PLAN', os.path.join(SP, 'rec', 'replay.plan.txt'))
    tag = 'creplay.%s' % mode
    say('== WO-197', tag, plan_src)
    R.stop('agent'); R.stop('peer')
    time.sleep(5)
    R.relay(Q.RELAY)
    L = open(plan_src).read().splitlines()
    last = max((float(l.split()[1]) for l in L if l.startswith('raw ')), default=60.0)
    start = next((float(l.split()[1]) for l in L if l.startswith('start ')), 6.0)
    end = start + last + 10
    # the player stands where the host's Henry stood when it was recorded (the rows name the town's NPCs around him)
    pos = os.environ.get('W197_REC_POS', '')
    if pos:
        x, y, zz = (float(v) for v in pos.split(','))
        for _ in range(3): live.lua('player:SetWorldPos({x=%f, y=%f, z=%f})' % (x, y, zz + 0.3)); time.sleep(1.2)
    noff, koff = Q.size(live.NLOG), Q.size(live.KLOG)
    for c in ('seats', 'work', 'routines'): live.cmd('mp_anim_%s %s' % (c, 'off' if mode == 'before' else 'on'))   # before: the 0.60.9 apply
    R.peer('r197' + tag, plan_src, ['--duration', str(int(end + 20)), '--claim-host'])
    t_peer = time.time()
    agent = os.path.join(SP, 'run', 'agent2', 'KcdMpClient.dll')   # a newer agent published beside the batch's own, when there is one
    R.agent('r197' + tag, agent if os.path.exists(agent) else Q.AGENT, ['KCDMP_TEST_JOINER_IN_WORLD=1'])
    for i in range(10):   # the agent stays on the log tail (its events, the console switches) only when the player moves in its first seconds
        live.lua('local p = player:GetWorldPos(); player:SetWorldPos({x = p.x + %s, y = p.y, z = p.z + 0.2})' % ('0.3' if i % 2 == 0 else '-0.3'))
        time.sleep(1.0)
    time.sleep(2)
    live.cmd('mp_leash off'); live.cmd('mp_anim_trace on'); live.cmd('mp_w197_status')
    for c in ('seats', 'work', 'routines'): live.cmd('mp_anim_%s %s' % (c, 'off' if mode == 'before' else 'on'))
    Q.keep_up()
    deadline = t_peer + end + 6
    while time.time() < deadline: time.sleep(0.5)
    live.cmd('mp_w197_status'); live.cmd('mp_w190_status')
    time.sleep(4)
    live.lua('W187KEEP = (W187KEEP or 0) + 1')
    live.cmd('mp_anim_trace off')
    nat = Q.read_from(live.NLOG, noff)
    kl = Q.read_from(live.KLOG, koff)
    st = R.load_state()
    ag = open(st['agentlog'], encoding='utf-8', errors='replace').read().splitlines() if st.get('agentlog') else []
    OUT = Q.OUT
    open(os.path.join(OUT, tag + '.native.log'), 'w', encoding='utf-8').write('\n'.join(nat))
    open(os.path.join(OUT, tag + '.agent.log'), 'w', encoding='utf-8').write('\n'.join(ag))
    open(os.path.join(OUT, tag + '.kcd.log'), 'w', encoding='utf-8').write('\n'.join(kl))
    if st.get('peerlog') and os.path.exists(st['peerlog']):
        open(os.path.join(OUT, tag + '.peer.log'), 'w', encoding='utf-8').write(open(st['peerlog'], encoding='utf-8', errors='replace').read())
    res = {'scenario': 'creplay', 'mode': mode, 'plan': plan_src, 'secs': end,
           'applies': sum(1 for l in nat if 'WO141-APPLY' in l), 'shots': sum(1 for l in nat if 'WO143-SHOT' in l),
           'hands': sum(1 for l in nat if 'WO143-HANDS' in l), 'doors': sum(1 for l in kl if 'WO151-DOOR' in l),
           'overflows': sum(1 for l in kl if 'Animation-queue overflow' in l),
           'faults': [l.strip()[15:200] for l in nat if ('FAULT' in l and 'FAULT-SUM' not in l) or 'switched OFF' in l][:10]}
    json.dump(res, open(os.path.join(OUT, tag + '.json'), 'w'), indent=1)
    R.stop('agent'); R.stop('peer')
    print(json.dumps(res, indent=1))
    return res


# ================================================================ Part D's real-input sitting (the WO allows real input for tests): this player fights
# with real keys and mouse (tools/dev-input/game_input.py: the game's own window only, foreground-checked before every event, every event logged);
# HOST role with the echo partner (his stream back 2 s late as a figure). Phase 1: a longsword duel with a commoner that blocks (B1's recoil
# from his own engine; the echo test's moves; the shield windows when it swings); phase 2: holstered, a brawl (B3's clinches, B2's paired
# punches, combos). Only when the machine is idle (no user input for W197_IDLE_S, default 1200 s) and the game is already in front.
def real_sitting(mode='after', secs1=75, secs2=75, stab=False, auto_s=0):
    import run187 as P
    import run190 as Q
    sys.path.insert(0, os.path.join(REPO, 'tools', 'dev-input'))
    import game_input as gi
    import ctypes
    class LII(ctypes.Structure): _fields_ = [('cbSize', ctypes.c_uint), ('dwTime', ctypes.c_uint)]
    lii = LII(); lii.cbSize = ctypes.sizeof(LII); ctypes.windll.user32.GetLastInputInfo(ctypes.byref(lii))
    idle = (ctypes.windll.kernel32.GetTickCount() - lii.dwTime) / 1000.0
    need = float(os.environ.get('W197_IDLE_S', '1200'))
    tag = ('stab.%s' if stab else 'real.%s') % mode
    g = gi.Game('wo197-real-%s' % time.strftime('%H%M%S'))
    if idle < need or not gi.foreground_is_game(g.pids):
        res = {'scenario': 'real', 'skipped': True, 'idle_s': idle, 'game_in_front': gi.foreground_is_game(g.pids)}
        json.dump(res, open(os.path.join(Q.OUT, tag + '.json'), 'w'), indent=1); print(json.dumps(res)); return res
    say('== WO-197 real-input sitting; idle %.0f s, the game in front' % idle)
    R.stop('agent'); R.stop('peer')
    time.sleep(5)
    R.relay(Q.RELAY)
    z = P.ground(P.CX, P.CY)
    P.place_player(z)
    plan = os.path.join(live.LOGS, 'plan.run197.%s.txt' % tag)
    open(plan, 'w').write('\n'.join(['# WO-197 real (generated by run197.py)', 'start 4', 'qmode 1 on', 'echo 2.0 2.6']) + '\n')
    noff, koff = Q.size(live.NLOG), Q.size(live.KLOG)
    R.agent('r197' + tag, Q.AGENT, [])
    for i in range(8):   # state frames in the agent's first seconds: it stays on the log tail (its events, the console switches)
        live.lua('local p = player:GetWorldPos(); player:SetWorldPos({x = p.x + %s, y = p.y, z = p.z + 0.2})' % ('0.3' if i % 2 == 0 else '-0.3'))
        time.sleep(1.0)
    time.sleep(3)
    R.peer('r197' + tag, plan, ['--duration', str(int(secs1 + secs2 + 120))])
    time.sleep(14)
    live.cmd('mp_anim_trace on'); live.cmd('mp_w197_status')
    Q.keep_up()
    npc = 'w197_real'
    spawn = ('local p = player:GetWorldPos(); local yaw = player:GetWorldAngles().z; local x, y = p.x + math.cos(yaw) * 2.2, p.y + math.sin(yaw) * 2.2; '
             'local z = System.GetTerrainElevation({x=x, y=y, z=0}) or p.z; '
             'if not System.GetEntityByName("%s") then XGenAIModule.SpawnEntity({ Name = "%s", ClassName = "NPC", Pos = { x, y, z }, '
             'SharedSoulGuid = "%s", NoAI = false }) end; local e = System.GetEntityByName("%s"); '
             'if e then pcall(function() e:SetFlags(ENTITY_FLAG_NO_SAVE, 3) end) end' % (npc, npc, ECHO_SOUL, npc))
    attack = ('local e = System.GetEntityByName("%s"); if e then local t = XGenAIModule.MakeTableFromType("crime:attackInitiatedByConcept"); '
              't.target = player.this.id; t.priorityTarget = true; '
              'pcall(function() XGenAIModule.SendMessageToEntityData(e.this.id, "crime:attackInitiatedByConcept", t) end) end' % npc)
    arm_npc = ('local e = System.GetEntityByName("%s"); if e then pcall(function() e.inventory:CreateItem("%s", 1, 1) end); '
               'local ok, id = pcall(function() return e.inventory:FindItem("%s") end); if ok and id then pcall(function() e.actor:EquipInventoryItem(id) end); '
               'pcall(function() e.human:DrawFromInventory(id, 0, true) end) end end' % (npc, P.SWORD, P.SWORD))
    live.lua(spawn); time.sleep(2)
    live.lua('pcall(function() player.inventory:CreateItem("%s", 1, 1) end); local ok, id = pcall(function() return player.inventory:FindItem("%s") end); '
             'if ok and id then pcall(function() player.actor:EquipInventoryItem(id) end); pcall(function() player.human:DrawFromInventory(id, 0, true) end) end' % (P.SWORD, P.SWORD))
    time.sleep(2.5)   # the sword drawn the game's own way first (the draw key alone left the first sitting bare-handed)
    live.lua(arm_npc); time.sleep(1)
    live.lua(attack)
    live.cmd('mp_w178_trace %d' % int(auto_s + secs1 + secs2 + 30))
    script = []
    if auto_s:   # B1 follow-up: first the engine fights for this player with the sword drawn (WO-154's hostfight lever; no input at all)
        live.cmd('mp_w154_check hostfight %s %d' % (npc, min(250, auto_s)))
        t_end, last = time.time() + auto_s, time.time()
        while time.time() < t_end:
            time.sleep(1)
            if time.time() - last > 12: live.lua(attack); last = time.time()
        live.cmd('mp_w154_check hostfight off')
        script.append('auto %d s' % auto_s)
        time.sleep(2)
    try:
        g.front(settle_s=3.0)   # the game's own window restored and in front (it alone is touched), then aimed
        if not gi.foreground_is_game(g.pids): raise gi.NotFocused('the game did not come to the front')
        g.aim()
        if not P.drawn(): g.tap('1', 120); time.sleep(2.0)
        script.append('drawn' if P.drawn() else 'NOT drawn')
        t_end = time.time() + secs1
        k, last = 0, time.time()
        dirs = ((-60, 0), (60, 0), (0, -50), (0, 50), (-40, -40), (40, -40))
        # B1 follow-up: the game's failed-attack rows are all stabs (attack zone 5, the bottom of the cursor); the down-move above never
        # selected it, so the stab sitting steps the cursor down in several moves of growing size right before each click
        downs = (3, 5, 8, 12)
        while time.time() < t_end:   # attack into its guard from six directions; now and then hold the block (the shield window)
            if stab:
                n = downs[k % len(downs)]
                for _ in range(n): g.move(0, 40); time.sleep(0.015)
                time.sleep(0.08); g.click('left', 90); script.append('stab %d' % n); time.sleep(1.8)
                k += 1
                if time.time() - last > 12: live.lua(attack); last = time.time()
                continue
            dx, dy = dirs[k % len(dirs)]
            g.move(dx, dy); time.sleep(0.12); g.click('left', 90); script.append('attack'); time.sleep(1.6)
            if k % 4 == 3:
                g.button('right', True); time.sleep(1.4); g.button('right', False); script.append('block'); time.sleep(0.6)
            k += 1
            if time.time() - last > 12: live.lua(attack); last = time.time()
        if stab: secs2 = 0
        g.tap('1', 120); time.sleep(2.5); script.append('holster')
        live.lua('local e = System.GetEntityByName("%s"); if e then pcall(function() e.human:HolsterToInventory(0, false) end); '
                 'pcall(function() e.human:HolsterToInventory(1, false) end) end' % npc)
        live.lua(attack)
        t_end = time.time() + secs2
        while time.time() < t_end:   # a brawl: punches, quick chains (combos), holds (clinch tries)
            g.click('left', 80); script.append('fist'); time.sleep(0.5)
            g.click('left', 80); time.sleep(0.5)
            g.click('left', 80); script.append('chain'); time.sleep(1.4)
            if time.time() - last > 12: live.lua(attack); last = time.time()
    except gi.NotFocused as e:
        import ctypes.wintypes as wt
        h = gi.game_window(g.pids); r = wt.RECT()
        under = '?'
        if h and ctypes.windll.user32.GetWindowRect(h, ctypes.byref(r)):
            ctypes.windll.user32.WindowFromPoint.restype = wt.HWND
            u = ctypes.windll.user32.WindowFromPoint(wt.POINT((r.left + r.right) // 2, (r.top + r.bottom) // 2))
            pid = wt.DWORD(); ctypes.windll.user32.GetWindowThreadProcessId(u, ctypes.byref(pid)); under = pid.value
        script.append('stopped: %s (game rect %d,%d-%d,%d; the pid under its centre %s)' % (e, r.left, r.top, r.right, r.bottom, under))
    finally:
        g.release_all()
        time.sleep(3)
    live.cmd('mp_w178_trace 0'); live.cmd('mp_w197_status')
    time.sleep(5)
    live.lua('W187KEEP = (W187KEEP or 0) + 1; local e = System.GetEntityByName("%s"); if e then pcall(System.RemoveEntity, e.id) end' % npc)
    live.cmd('mp_anim_trace off')
    nat = Q.read_from(live.NLOG, noff)
    kl = Q.read_from(live.KLOG, koff)
    st = R.load_state()
    ag = open(st['agentlog'], encoding='utf-8', errors='replace').read().splitlines() if st.get('agentlog') else []
    open(os.path.join(Q.OUT, tag + '.native.log'), 'w', encoding='utf-8').write('\n'.join(nat))
    open(os.path.join(Q.OUT, tag + '.agent.log'), 'w', encoding='utf-8').write('\n'.join(ag))
    open(os.path.join(Q.OUT, tag + '.kcd.log'), 'w', encoding='utf-8').write('\n'.join(l for l in kl if 'KCD2-MP' in l or re.search(r'(?i)align|interrupt|sync|clinch|Perfect|Slot', l)))
    if st.get('peerlog') and os.path.exists(st['peerlog']):
        open(os.path.join(Q.OUT, tag + '.peer.log'), 'w', encoding='utf-8').write(open(st['peerlog'], encoding='utf-8', errors='replace').read())
    out_rows = [l for l in ag if 'MP-ACTION section=outbound' in l]
    res = {'scenario': 'real', 'mode': mode, 'idle_s': idle, 'script': script[:12] + ['... %d events' % len(script)], 'input_events': g.sent,
           'out': len(out_rows), 'out_kinds': {k: sum(1 for l in out_rows if 'kind=%s ' % k in l) for k in ('Attack', 'BlockImpulse', 'Dodge', 'AvatarHit', 'AvatarRecoil', 'AvatarClinch')},
           'back': sum(1 for l in ag if 'MP-SWING hop=recv' in l) + sum(1 for l in ag if 'section=inbound' in l and ('AvatarRecoil' in l or 'AvatarClinch' in l or 'AvatarHit' in l)),
           'recoil_native': [l.strip()[15:260] for l in nat if 'WO197-RECOIL' in l][:20],
           'blows': [l.strip()[15:330] for l in nat if 'WO197-B1BLOW' in l][:40],
           'out_zones': {z: sum(1 for l in out_rows if 'kind=Attack ' in l and 'zone=%s ' % z in l) for z in ('Head', 'UpperLeft', 'UpperRight', 'Lower')},
           'out_recoils': [l[:300] for l in out_rows if 'kind=AvatarRecoil' in l][:20],
           'hosthit': {o: sum(1 for l in nat if 'WO176-HOSTHIT' in l and 'outcome=%s ' % o in l) for o in ('hit', 'blocked', 'pb')},
           'clinch_native': [l.strip()[15:260] for l in nat if 'WO197-CLINCH' in l][:20],
           'rowdump': [l.strip()[15:420] for l in nat if 'WO197-ROWDUMP' in l][:4],
           'in': [l[:300] for l in ag if 'section=inbound' in l and ('AvatarRecoil' in l or 'AvatarClinch' in l)][:20],
           'figure_failed_rows': [l.strip()[15:300] for l in nat if 'role=figure' in l and 'scope=FullBody' in l and 'FailedAttack' in l][:10],
           'pb_slots': sum(1 for l in kl if 'Perfect' in l and 'Slot started' in l),
           'faults': [l.strip()[15:200] for l in nat if ('FAULT' in l and 'FAULT-SUM' not in l) or 'switched OFF' in l][:10]}
    json.dump(res, open(os.path.join(Q.OUT, tag + '.json'), 'w'), indent=1)
    R.stop('agent'); R.stop('peer')
    print(json.dumps(res, indent=1)[:6000])
    return res


# ================================================================ Part D (host role, after live195b.py up): WO-192's host autosave (1 min) and marker
def hostauto(secs=150):
    st = R.load_state()
    alog = st.get('agentlog')
    if not alog or not os.path.exists(alog): raise SystemExit('no agent log (run live195b.py up first)')
    a0 = os.path.getsize(alog)
    live.cmd('mp_host_autosave_min 1')
    # the partner walks a little (the marker follows a moving partner: a new pin entity per move, WO192-MAPMARK)
    t_end = time.time() + secs
    while time.time() < t_end: time.sleep(2)
    live.cmd('mp_host_autosave_min 30')
    with open(alog, 'rb') as f:
        f.seek(a0); ag = f.read().decode('utf-8', 'replace').splitlines()
    nat = [l for l in open(live.NLOG, encoding='utf-8', errors='replace').read().splitlines()[-4000:] if 'WO192-MAPMARK' in l]
    res = {'step': 'hostauto', 'secs': secs,
           'autosave': [l[:240] for l in ag if 'autosave' in l.lower()][:12],
           'worldsaved': [l[:200] for l in ag if 'WorldSaved' in l][:8],
           'verified': sum(1 for l in ag if 'why=autosave' in l and 'verified' in l),
           'mapmark': [l.strip()[15:200] for l in nat][-6:] + [l[:200] for l in ag if 'MAPMARK' in l][:6]}
    open(os.path.join(SP, 'results', 'hostauto.json'), 'w').write(json.dumps(res, indent=1))
    print(json.dumps(res, indent=1)[:4000])
    return res


if __name__ == '__main__':
    v = sys.argv[1] if len(sys.argv) > 1 else ''
    if v == 'game': game()
    elif v == 'load': load(*(sys.argv[2:3] or ['quicksave022']))
    elif v == 'echo': echo_run(*(sys.argv[2:3] or ['sword']), secs=int(sys.argv[3]) if len(sys.argv) > 3 else 70)
    elif v in ('echosword', 'echofists'): echo_run(v[4:], secs=70, mode=sys.argv[2] if len(sys.argv) > 2 else 'after')
    elif v == 'hostauto': hostauto(int(sys.argv[2]) if len(sys.argv) > 2 else 150)
    elif v == 'real': real_sitting(sys.argv[2] if len(sys.argv) > 2 else 'after')
    elif v == 'stab': real_sitting(sys.argv[2] if len(sys.argv) > 2 else 'after', secs1=int(sys.argv[3]) if len(sys.argv) > 3 else 75, secs2=0,
                                   stab=True, auto_s=int(sys.argv[4]) if len(sys.argv) > 4 else 60)
    elif v == 'creplay': c_replay(sys.argv[2] if len(sys.argv) > 2 else 'after')
    elif v == 'b5duel':   # B0 / B5: the copy duel with the duel trace on (WO178-TRACE act=, WO190-TAKEOVER director=, WO190-ANIM dir=)
        import run190
        import glob, shutil
        live.cmd('mp_w178_trace 240')
        run190.run('copyduel', 'b5')
        live.cmd('mp_w178_trace 0')
        for f in glob.glob(os.path.join(run190.OUT, 'copyduel.b5.*')):   # as b5duel.<mode>.* for batch197
            shutil.copy(f, os.path.join(run190.OUT, os.path.basename(f).replace('copyduel.b5.', 'b5duel.%s.' % (sys.argv[2] if len(sys.argv) > 2 else 'after'))))
    elif v.startswith('b') and v[1:2].isdigit(): b_run(v, *(sys.argv[2:3] or ['after']))
    else:
        import run190
        run190.run(sys.argv[1], sys.argv[2])
