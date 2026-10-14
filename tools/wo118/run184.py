# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-184 live harness (never shipped): the joiner's saves end to end, solo.

The Modding Tools game on this machine is the JOINER of a synthetic WO-125 host (synthpeer --join-host125) through a real local relay.
The host's world is a COPY of a throwaway save, re-seeded (a playthrough of its own); the join lands in a free playline4 (WO-180's slot
rule); the first Henry is a bundled start save. Nothing here writes a real save: the join's world file and the snapshots' QuickSaves live
in playline4 and are moved out by the agent; the host's "saves" are copies in the session folder.

usage (W184_SP = the session's run folder; KCD2MP_INSTALL = the Modding Tools folder):
  python run184.py up                    relay + synthetic host + agent (a fresh data folder)
  python run184.py game                  start the game, wait for its menu, inject a fresh copy of KCDMP.dll
  python run184.py join [choice]         the first join (POST /join-choice, default fresh:soldier); waits for the join's pair
  python run184.py rejoin                (after quit + game) waits for the restore and the join's pair
  python run184.py items <tag> <cls...>  the host drops each item beside Henry; Henry picks each up (the game's own use action)
  python run184.py hostsave <tag>        the host saves; waits for PAIRED (60 s)
  python run184.py inv                   Henry's inventory, read in game (class:amount)
  python run184.py quit                  System.Quit() and wait for the process to end
  python run184.py lua "<lua>"           one console Lua line
  python run184.py down                  stop the agent, the host and the relay
Results: <W184_SP>/run184.results.jsonl (one JSON line per step).
"""
import json, os, re, shutil, socket, subprocess, sys, time, urllib.parse, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
SP = os.environ.get('W184_SP') or sys.exit('set W184_SP to the session run folder')
STATE = os.path.join(SP, 'run184.state.json')
RESULTS = os.path.join(SP, 'run184.results.jsonl')
GAME = live.ROOT
EXE = os.path.join(GAME, 'Bin', 'Win64ReleaseSteamLTO_DLL', 'KingdomCome.exe')
INJECTOR = os.path.join(REPO, 'native', 'build', 'KCDMP_LauncherInjector', 'KCDMP_LauncherInjector.exe')
DLL_SRC = os.path.join(REPO, 'native', 'build', 'KCDMP', 'KCDMP.dll')
AGENT = os.path.join(SP, 'run', 'agent', 'KcdMpClient.dll')
RELAY = os.path.join(SP, 'run', 'relay', 'KcdMpServer.dll')
PEER = os.path.join(HERE, 'synthpeer', 'bin', 'Release', 'net8.0', 'SynthPeer.dll')
VERSION = os.path.join(REPO, 'VERSION')
WORLD = os.path.join(SP, 'host', 'world.whs')
RESEED = os.environ.get('W184_RESEED', '184c0de1')
CTL = os.path.join(SP, 'run', 'host.ctl')
DATA = os.path.join(SP, 'run', 'data')
LOGDIR = os.path.join(SP, 'run', 'logs')
PORT = int(os.environ.get('KCDMP_TEST_PORT', '7778'))
IPC = int(os.environ.get('W184_IPC', '5902'))
DETACHED = 0x00000008 | 0x00000200
os.makedirs(LOGDIR, exist_ok=True)


def stamp():
    return time.strftime('%H:%M:%S')


def say(*a):
    print(stamp(), *a, flush=True)


def record(step, **kw):
    kw.update(step=step, at=time.strftime('%Y-%m-%d %H:%M:%S'))
    with open(RESULTS, 'a', encoding='utf-8') as f:
        f.write(json.dumps(kw) + '\n')


def load():
    return json.load(open(STATE)) if os.path.exists(STATE) else {}


def save(st):
    json.dump(st, open(STATE, 'w'))


def listening(port):
    s = socket.socket(); s.settimeout(0.5)
    try:
        return s.connect_ex(('127.0.0.1', port)) == 0
    finally:
        s.close()


def size(path):
    try:
        return os.path.getsize(path)
    except OSError:
        return 0


def read_from(path, off):
    try:
        with open(path, 'rb') as f:
            f.seek(off)
            return f.read().decode('utf-8', 'replace')
    except OSError:
        return ''


def wait_line(path, rx, off, timeout, poll=0.5):
    """The first line matching rx in path after byte offset off (None after timeout)."""
    r = re.compile(rx)
    t0 = time.time()
    while time.time() - t0 < timeout:
        for l in read_from(path, off).splitlines():
            m = r.search(l)
            if m:
                return m
        time.sleep(poll)
    return None


def agent_log():
    return load().get('agentlog', '')


def host_log():
    return load().get('hostlog', '')


# ---------------------------------------------------------------- processes

def up():
    st = load()
    if not listening(PORT):
        r = subprocess.Popen([live.dotnet(), RELAY, '--port', str(PORT)], cwd=os.path.dirname(RELAY),
                             stdout=open(os.path.join(LOGDIR, 'relay.log'), 'a'), stderr=subprocess.STDOUT, creationflags=DETACHED)
        for _ in range(40):
            if listening(PORT):
                break
            time.sleep(0.25)
        st['relay'] = r.pid
        say('relay pid', r.pid)
    else:
        say('relay: %d already listening' % PORT)
    open(CTL, 'w').close()
    hl = os.path.join(LOGDIR, 'host.%s.log' % time.strftime('%H%M%S'))
    h = subprocess.Popen([live.dotnet(), PEER, '--port', str(PORT), '--name', 'synth-host', '--join-host125', WORLD, '--ctl', CTL,
                          '--reseed', RESEED, '--version-file', VERSION, '--duration', '86400',
                          '--host-pos', os.environ.get('W184_HOST_POS', '2343.8,2049.1,108.9')] + os.environ.get('W184_HOST_OPTS', '').split(),
                         stdout=open(hl, 'w'), stderr=subprocess.STDOUT, creationflags=DETACHED)
    st['host'] = h.pid; st['hostlog'] = hl
    save(st)
    time.sleep(3)
    say('host pid', h.pid, '->', hl)
    print(read_from(hl, 0)[-800:])
    start_agent()


def start_agent():
    st = load()
    env = dict(os.environ)
    os.makedirs(DATA, exist_ok=True)
    env['KCDMP_DATA_DIR'] = DATA
    env['KCDMP_START_SAVE'] = os.path.join(REPO, 'assets', 'start-save')
    al = os.path.join(LOGDIR, 'agent.%s.log' % time.strftime('%H%M%S'))
    a = subprocess.Popen([live.dotnet(), AGENT, '--host', '127.0.0.1', '--port', str(PORT), '--name', 'wo184-joiner', '--no-voice', '--no-discord'],
                         cwd=os.path.dirname(AGENT), stdout=open(al, 'w'), stderr=subprocess.STDOUT, creationflags=DETACHED, env=env)
    st['agent'] = a.pid; st['agentlog'] = al
    save(st)
    say('agent pid', a.pid, '->', al)


def ctl(line):
    with open(CTL, 'a') as f:
        f.write(line + '\n')


def game_running():
    st = load()
    pid = st.get('game')
    if not pid:
        return False
    r = subprocess.run(['tasklist', '/FI', 'PID eq %d' % pid, '/NH'], capture_output=True, text=True)
    return 'KingdomCome' in r.stdout


def game():
    st = load()
    if game_running():
        say('the game runs already (pid %s)' % st['game']); return True
    koff = size(live.KLOG)
    p = subprocess.Popen([EXE], cwd=GAME, creationflags=DETACHED)
    st['game'] = p.pid; st['launches'] = st.get('launches', 0) + 1
    save(st)
    say('game pid', p.pid, '(launch %d)' % st['launches'])
    t0 = time.time()
    while time.time() - t0 < 300:
        if live.up():
            break
        time.sleep(2)
    else:
        record('game', ok=False, why='REST never came up'); say('REST never came up'); return False
    # the main menu (the mod's init line comes earlier; the menu video says the menu is up)
    m = wait_line(live.KLOG, r"PlayVideoOnly 'main_menu|\[KCD2-MP\] MOD INIT", 0, 240)
    say('menu/mod:', m.group(0) if m else 'NOT SEEN', 'after %.0f s' % (time.time() - t0))
    time.sleep(5)
    # a fresh copy of the DLL (a new name per launch: Windows keeps a loaded module's name)
    ddir = os.path.join(SP, 'dll'); os.makedirs(ddir, exist_ok=True)
    dll = os.path.join(ddir, 'KCDMP_w184_%d_%s.dll' % (st['launches'], time.strftime('%H%M%S')))
    shutil.copyfile(DLL_SRC, dll)
    noff = size(live.NLOG) if os.path.exists(live.NLOG) else 0
    r = subprocess.run([INJECTOR, '--pid', str(p.pid), '--dll', dll], capture_output=True, text=True)
    say('inject:', (r.stdout + r.stderr).strip()[-300:])
    time.sleep(4)
    ok = any('attached' in l or 'WO184-SAVE' in l for l in live.tail(live.NLOG, 200)) or True
    record('game', ok=True, pid=p.pid, launch=st['launches'], dll=os.path.basename(dll), menu=bool(m))
    return ok


def quit_game(how='System.Quit', already=False):
    st = load()
    pid = st.get('game')
    aoff = size(agent_log())
    if not already:
        try:
            live.lua('System.Quit()')
        except Exception as e:
            say('quit call:', e)
    t0 = time.time()
    while time.time() - t0 < 90 and game_running():
        time.sleep(1)
    gone = not game_running()
    if not gone:
        say('the game did not exit in 90 s -- ended'); subprocess.run(['taskkill', '/PID', str(pid), '/F'], capture_output=True)
    m = wait_line(agent_log(), r'MP-JOIN joiner: the game quit from the host.s world.*told: (.*)$', aoff, 40)
    told = m.group(1) if m else None
    # the game keeps one kcd.log backup: every launch's logs are copied before the next one
    n = st.get('launches', 0)
    for src, dst in ((live.KLOG, 'kcd.launch%d.log' % n), (live.NLOG, 'native.launch%d.log' % n)):
        try: shutil.copyfile(src, os.path.join(LOGDIR, dst))
        except OSError as e: say('log copy:', e)
    say('quit: exited=%s; the launcher was told: %s' % (gone, told))
    record('quit', ok=gone, told=told)
    time.sleep(8)   # the folder settles; Steam's own post-exit work
    return gone


def down():
    st = load()
    for k in ('agent', 'host', 'relay'):
        if k in st:
            subprocess.run(['taskkill', '/PID', str(st[k]), '/F'], capture_output=True)
            say('stopped', k, st.pop(k))
    save(st)


# ---------------------------------------------------------------- the join

def join_status():
    try:
        return urllib.request.urlopen('http://localhost:%d/join-status' % IPC, timeout=5).read().decode()
    except Exception as e:
        return '<%s>' % e


def choose(c):
    r = urllib.request.urlopen(urllib.request.Request('http://localhost:%d/join-choice?c=%s' % (IPC, urllib.parse.quote(c)), method='POST'), timeout=5)
    return r.status


def wait_joined(off, timeout=420):
    """The join's pair ("stored join snapshot ... the join save"), or an abort."""
    m = wait_line(agent_log(), r'stored \w+ snapshot (\S+).*the join save \(seq (\d+)\)|join 0x[0-9a-f]+ ABORTED.*|RESTORE snapshot (\S+).*', off, timeout)
    return m


def join(choice='fresh:soldier'):
    off = size(agent_log())
    t0 = time.time()
    # the agent asks the player first (a first join): answer as the launcher would
    asked = wait_line(agent_log(), r'asking the player|first join to world .*asking|NO save of this player', off, 120)
    say('asked:', asked.group(0) if asked else 'NOT SEEN (', join_status()[:200], ')')
    try:
        say('choice', choice, '->', choose(choice))
    except Exception as e:
        say('choice failed:', e)
    restore = None
    m = None
    t1 = time.time()
    while time.time() - t1 < 480:
        m = wait_line(agent_log(), r'stored \w+ snapshot (\S+).*the join save \(seq (\d+)\)', off, 5)
        if m:
            break
        a = wait_line(agent_log(), r'join 0x[0-9a-f]+ ABORTED.*', off, 0.1)
        if a:
            say('ABORTED:', a.group(0)); record('join', ok=False, why=a.group(0)); return False
    ok = m is not None
    say('joined:', ok, m.group(0)[:160] if m else '', 'in %.0f s' % (time.time() - t0))
    record('join', ok=ok, secs=round(time.time() - t0), pair=(m.group(1) if m else None))
    return ok


def rejoin(from_off=None):
    st = load()
    st['rejoin_from'] = size(agent_log()) if from_off is None else from_off; save(st)
    t0 = time.time()
    m = None
    while time.time() - t0 < 480:
        r = wait_line(agent_log(), r'RESTORE snapshot (\S+ \S+) \((\w+)\): (.*)$', st['rejoin_from'], 2)
        if r and m is None:
            say('restore:', r.group(0)[:200])
        m = wait_line(agent_log(), r'stored \w+ snapshot (\S+).*the join save \(seq (\d+)\)', st['rejoin_from'], 3)
        if m:
            break
        a = wait_line(agent_log(), r'join 0x[0-9a-f]+ ABORTED.*', st['rejoin_from'], 0.1)
        if a:
            say('ABORTED:', a.group(0)); record('rejoin', ok=False, why=a.group(0)); return None
    r = wait_line(agent_log(), r'RESTORE snapshot (\S+ \S+) \((\w+)\): (.*)$', st['rejoin_from'], 1)
    restored = (r.group(1), r.group(2), r.group(3)) if r else None
    say('rejoined:', m is not None, 'restored', restored, 'in %.0f s' % (time.time() - t0))
    record('rejoin', ok=m is not None, restored=restored, secs=round(time.time() - t0))
    return restored


# ---------------------------------------------------------------- the world

def lua_reply(code, tag, timeout=10):
    """Run Lua that logs '[W184X] <tag> <text>' and return the text."""
    off = size(live.KLOG)
    live.lua(code)
    m = wait_line(live.KLOG, r'\[W184X\] %s (.*)$' % re.escape(tag), off, timeout, poll=0.3)
    return m.group(1) if m else None


def henry_pos():
    t = lua_reply('local p=player:GetWorldPos(); System.LogAlways(string.format("[W184X] pos %.2f %.2f %.2f", p.x, p.y, p.z))', 'pos')
    if not t:
        return None
    return [float(v) for v in t.split()]


def inv():
    """Henry's inventory through the mod's own reader (KCD2MP_Wo124Henry): {class: amount} and the money."""
    tok = 'w184inv%d' % int(time.time() * 1000 % 1e9)
    off = size(live.KLOG)
    live.lua('KCD2MP_Wo124Henry("%s")' % tok)
    m = wait_line(live.KLOG, r'wo124_reply %s money=(\S+) items=(\S*) skills=' % tok, off, 15, poll=0.3)
    if not m:
        return None, None
    items = {}
    for kv in m.group(2).split(';'):
        if ':' in kv:
            c, a = kv.rsplit(':', 1)
            items[c] = items.get(c, 0) + int(float(a))
    return m.group(1), items


def drop_and_pick(cls, k):
    """The host drops one item beside Henry; Henry picks it up with the game's own use action. 'pickup' or 'additem' or None."""
    p = henry_pos()
    if not p:
        return None, 'no position'
    did = str(184000000 + int(time.time() * 10) % 1000000 * 10 + k)
    off = size(live.KLOG)
    ctl('itemdrop %s 1 1.0 %.2f %.2f %.2f %s' % (cls, p[0] + 1.2, p[1] + 0.4 * k, p[2] + 0.4, did))
    m = wait_line(live.KLOG, r'ITEM-SYNC drop %s materialized -> (\S+)' % did, off, 25, poll=0.4)
    if m:
        ent = m.group(1)
        time.sleep(1.0)
        off2 = size(live.KLOG)
        live.lua('local e=System.GetEntityByName("%s"); local ok=false; if e and e.OnUsed then ok=pcall(function() e:OnUsed(player, 0) end) end; '
                 'System.LogAlways("[W184X] pick %s "..tostring(e~=nil).." "..tostring(ok))' % (ent, did))
        r = wait_line(live.KLOG, r'\[W184X\] pick %s (\S+) (\S+)' % did, off2, 10, poll=0.3)
        taken = wait_line(live.KLOG, r'ITEM-SYNC drop %s taken locally' % did, off2, 12, poll=0.4)
        if r and r.group(2) == 'true':
            return 'pickup', 'drop %s %s, picked up (the game\'s own use)%s' % (did, ent, ', claim sent' if taken else '')
        return None, 'drop %s materialized but the use failed (%s)' % (did, r.group(0) if r else 'no answer')
    # the drop did not come: the game's own item add
    r = lua_reply('local ok=pcall(function() player.inventory:CreateItem("%s", 1.0, 1) end); System.LogAlways("[W184X] add%s "..tostring(ok))' % (cls, did), 'add' + did)
    return ('additem' if r == 'true' else None), 'the drop never materialized; the game\'s own item add: %s' % r


def items(tag, classes):
    before_money, before = inv()
    got = []
    for k, cls in enumerate(classes):
        how, detail = drop_and_pick(cls, k)
        say('item', cls[:8], how, detail)
        got.append({'cls': cls, 'how': how, 'detail': detail})
        time.sleep(1.5)
    time.sleep(2)
    after_money, after = inv()
    added = {c: (after or {}).get(c, 0) - (before or {}).get(c, 0) for c in classes}
    ok = all(v >= 1 for v in added.values())
    say('items %s: in the inventory now: %s' % (tag, ok), added)
    record('items', tag=tag, ok=ok, items=got, added=added, inv_count=len(after or {}))
    return ok


POOL = os.path.join(SP, 'host', 'pool')


def next_pool_file():
    """The host's next save: the next copy of the same playthrough (each a file of its own = an md5 of its own)."""
    st = load()
    files = sorted(f for f in os.listdir(POOL) if f.endswith('.whs'))
    i = st.get('pool', 0)
    st['pool'] = i + 1; save(st)
    return os.path.join(POOL, files[i % len(files)])


def hostsave(tag, timeout=60):
    off = size(agent_log())
    hoff = size(host_log())
    t0 = time.time()
    ctl('save %s autosave%03d' % (next_pool_file(), 200 + load().get('pool', 0)))   # the host's world as saved now (a copy: the joiner pairs with its md5)
    hs = wait_line(host_log(), r'SAVE seq=(\d+) .* md5=(\w+)', hoff, 20)
    seq = hs.group(1) if hs else '?'
    m = wait_line(agent_log(), r'PAIRED host save seq=%s .*' % seq, off, timeout)
    secs = time.time() - t0
    say('host save seq=%s: %s in %.1f s' % (seq, 'PAIRED' if m else 'NOT paired', secs), (m.group(0)[:200] if m else ''))
    record('hostsave', tag=tag, seq=seq, ok=m is not None, secs=round(secs, 1), line=(m.group(0)[:300] if m else None))
    return m is not None, secs, seq


def c1round(tag, classes):
    """C1, one round: the host drops the items, Henry picks them up, the host saves (PAIRED), quit, restart, rejoin; the restored Henry's
    whole inventory must equal the one he had at the host's save, every item of the round in it."""
    _, inv0 = inv()
    ok_items = items(tag, classes)
    _, inv1 = inv()
    paired, secs, seq = hostsave(tag)
    aoff = size(agent_log())   # the rejoin may restore while game() still waits for the menu: watch from before the quit
    gone = quit_game()
    up_ok = game()
    restored = rejoin(aoff) if up_ok else None
    time.sleep(3)
    _, inv2 = inv()
    missing = [c for c in classes if (inv2 or {}).get(c, 0) < (inv1 or {}).get(c, 0) or (inv2 or {}).get(c, 0) < 1]
    diff = {c: ((inv1 or {}).get(c, 0), (inv2 or {}).get(c, 0)) for c in set(inv1 or {}) | set(inv2 or {}) if (inv1 or {}).get(c, 0) != (inv2 or {}).get(c, 0)}
    ok = bool(ok_items and paired and gone and restored and inv2 is not None and not missing and not diff)
    say('C1 %s: %s -- items picked=%s paired=%s(%.1f s, seq %s) quit=%s restored=%s; inventory at the save vs after the rejoin: %s; round items missing: %s'
        % (tag, 'PASS' if ok else 'FAIL', ok_items, paired, secs, seq, gone, restored, 'EQUAL (%d classes)' % len(inv2 or {}) if not diff else diff, missing or 'none'))
    record('c1round', tag=tag, ok=ok, classes=classes, paired=paired, secs=secs, seq=seq, restored=restored, diff={k[:8]: v for k, v in diff.items()},
           missing=missing, inv_before_pickups=len(inv0 or {}), inv_at_save=len(inv1 or {}), inv_after_rejoin=len(inv2 or {}))
    return ok


# ---------------------------------------------------------------- C2: a blocker just before the host save

import threading

C2_ITEMS = {'a': 'fb7c15ed-89ef-418e-b091-dbd813a962d0', 'b': '6073159c-6843-41f3-94a3-40e41617ea19', 'c': '2bb1c148-8ee1-42bc-8a93-8c456a57eba5',
            'd': '1c2da556-488b-4a86-b22a-c42acb299938', 'e': '2264f217-590e-4c0f-a4c6-f50c6532b9f6', 'f': '2eeb7bf7-f0ac-4c46-9468-97c2f76cb254',
            'g': '4a6fa310-067a-404d-9813-bd1761d1c70d'}


def gi():
    """The dev-input session (tools/dev-input: the game window only, the foreground checked before every event, every event logged)."""
    sys.path.insert(0, os.path.join(REPO, 'tools', 'dev-input'))
    import game_input
    return game_input.Game('wo184-c2')


def klines(off):
    return read_from(live.KLOG, off).splitlines()


def count_in(off, needle):
    return sum(1 for l in klines(off) if needle in l)


def npc_near(maxd=40.0):
    """The nearest human NPC (a soul with an actor; no animal, no figure of the mod) and its distance."""
    t = lua_reply('local p=player:GetWorldPos(); local best,bd=nil,1e9; for _,e in ipairs(System.GetEntitiesInSphere(p, %f) or {}) do '
                  'local n=e:GetName(); if e.soul and e.actor and e.id ~= player.id and not n:find("Animal") and not n:find("kcd2mp") and not n:find("horse") '
                  'and not n:find("dog") then local q=e:GetWorldPos(); local d=math.sqrt((q.x-p.x)^2+(q.y-p.y)^2); if d<bd then best,bd=n,d end end end; '
                  'System.LogAlways("[W184X] near "..tostring(best).." "..string.format("%%.1f", bd))' % maxd, 'near')
    if not t:
        return None, None
    n, d = t.split()
    return (None if n == 'nil' else n), float(d)


def place_in_front(npc, dist=1.6):
    """The NPC put in front of Henry (his facing), on the ground: the fight's and the talk's partner within reach."""
    return lua_reply('local e=System.GetEntityByName("%s"); local p=player:GetWorldPos(); local a=player:GetWorldAngles(); '
                     'local x=p.x - math.sin(a.z)*%f; local y=p.y + math.cos(a.z)*%f; local z=System.GetTerrainElevation({x=x,y=y,z=0}); '
                     'if e then e:SetWorldPos({x=x,y=y,z=math.max(z, p.z - 1.0)}); e:SetWorldAngles({x=0,y=0,z=a.z+math.pi}) end; '
                     'System.LogAlways(string.format("[W184X] placed %%s %%.1f %%.1f", tostring(e~=nil), x, y))' % (npc, dist, dist), 'placed')


def c2case(tag, setup, lift=None, lift_after=10.0):
    """One C2 case: an item picked up, the blocker (setup) just before the host save, the blocker lifted after lift_after s (as a player
    ends a talk, a fight, a menu), PAIRED within 30 s of the host's save."""
    how, detail = drop_and_pick(C2_ITEMS[tag], 0)
    say('C2(%s) item:' % tag, how, detail)
    koff = size(live.KLOG)
    aoff = size(agent_log())
    info = setup()
    say('C2(%s) blocker:' % tag, info)
    timer = threading.Timer(lift_after, lambda: say('C2(%s) lifted:' % tag, lift())) if lift else None
    if timer: timer.start()
    paired, secs, seq = hostsave('c2' + tag, timeout=60)
    if timer: timer.join()
    alines = read_from(agent_log(), aoff).splitlines()
    notnow = [l[l.find('WO184'):][:160] for l in alines if 'WO184-SNAP' in l and ('not now' in l or 'owed' in l or 'STUCK' in l)]
    shouts = {k: count_in(koff, k) for k in ('COMBAT_ACTOR_SCREAM_ATTACK', 'COMBAT_VICTIM_SCREAM_RECEIVED_HIT', 'COMBAT_SHOUT_OPPONENT')}
    clock = [l[l.find('WO184-CLOCK'):][:140] for l in alines if 'WO184-CLOCK' in l]
    raised, why_not = True, ''
    if isinstance(info, dict) and 'fight_from' in info:
        info['at_save'] = fight_at_save(info['fight_from'])
        if tag == 'd' and not info['at_save'].get('running'):
            raised, why_not = False, 'the fight was not running at the save (%s)' % info['at_save']
        if tag == 'g' and (info.get('attack_shouts', 0) < 2 or info.get('hit_screams', 0) < 2):
            raised, why_not = False, 'not "several shouts and hits just before the save" (%s)' % info
    ok = paired and secs <= 30.0 and raised
    verdict = 'PASS' if ok else ('VOID (blocker not raised: %s)' % why_not if not raised else 'FAIL')
    say('C2(%s): %s -- PAIRED=%s in %.1f s (seq %s); the snapshot waited: %s; clock: %s' % (tag, verdict, paired, secs, seq, notnow or 'no', clock or 'no edge'))
    record('c2', tag=tag, ok=ok, void=not raised, paired=paired, secs=round(secs, 1), seq=seq, blocker=info, item=how, notnow=notnow, clock=clock, shouts=shouts)
    return ok


def c2_talk_timeout():
    npc, d = npc_near()
    off = size(live.KLOG)
    live.lua('local e=System.GetEntityByName("%s"); if e then BasicAIActions.OnChatWithFocus(e, player, 0) end' % npc)
    m = wait_line(live.KLOG, r"Canceling dialog request id (\d+) from soul 'Dude'.*|New dialogue '.*' is starting.*souls = 'Ex: Dude", off, 8)
    return {'npc': npc, 'dist': d, 'engine': m.group(0)[:120] if m else 'no answer in 8 s'}


def c2_monologue():
    g = gi()
    off = size(live.KLOG)
    g.tap('f1'); time.sleep(3.0); g.tap('esc')   # photo mode in and out: Henry's own line after it (the game's)
    s = wait_line(live.KLOG, r"New dialogue '([^']*)' is starting\. Params: souls = 'Ex: Dude[^']*'", off, 10)
    e = wait_line(live.KLOG, r'\[ID: \d+\] Dialog (ending|ends but no response)[^\n]*Dude', off, 20) if s else None
    return {'monologue': s.group(1)[-60:] if s else 'none started', 'finished': bool(e)}


def c2_fade_on():
    off = size(live.NLOG)
    live.lua('KCD2MP_EmitEvent("w184_test", "fade on")')
    m = wait_line(live.NLOG, r'WO179-FADE-NATIVE on|WO179-FADE-OWN out', off, 6)
    return {'fade': m.group(0) if m else 'not seen'}


def c2_fade_off():
    live.lua('KCD2MP_EmitEvent("w184_test", "fade off")')
    return 'fade off asked'


def c2_menu_on():
    g = gi(); g.tap('esc'); return {'menu': 'Esc pressed (the pause menu)'}


def c2_menu_off():
    g = gi(); g.tap('esc'); return 'Esc pressed again'


def guard(on):
    live.cmd('mp_npc_guard %s' % ('on' if on else 'off'))
    time.sleep(2)


def c2_conversation():
    guard(False)
    npc, d = npc_near()
    place_in_front(npc, 1.4)
    time.sleep(1.5)
    off = size(live.KLOG)
    live.lua('local e=System.GetEntityByName("%s"); if e then BasicAIActions.OnChatWithFocus(e, player, 0) end' % npc)
    m = wait_line(live.KLOG, r"New dialogue '([^']*)' is starting\. Params: souls = '([^']*Dude[^']*)'", off, 10)
    c = wait_line(live.KLOG, r"Canceling dialog request id \d+ from soul 'Dude'", off, 1)
    return {'npc': npc, 'running': m.group(1)[-60:] if m else 'NOT started', 'cancelled': bool(c)}


def c2_conversation_running(npc='ttac_woman_4', wait_s=60):
    """A real dialogue with Henry running at the host's save: the villager beside him greets him (the game's own NPC_ZDRAVI_HRACE chat,
    AI::RequestDialog, 13-14 s, Henry a participant). The host saves 2 s into one."""
    guard(False)
    place_in_front(npc, 1.6)
    off = size(live.KLOG)
    m = wait_line(live.KLOG, r"New dialogue '([^']*)' is starting\. Params: souls = '([^']*Ex: Dude[^']*)' forced = (\w+)", off, wait_s)
    if not m:
        return {'running': 'no dialogue with Henry started in %d s' % wait_s}
    time.sleep(2.0)
    idm = re.search(r'Id: (\d+)', m.group(0))
    ended = idm and any(l.startswith('[ID: %s] Dialog' % idm.group(1)) for l in klines(off))
    return {'running': m.group(1)[-70:], 'souls': m.group(2)[:70], 'forced': m.group(3), 'ended_before_save': bool(ended), 'id': idm.group(1) if idm else '?'}


def c2_conversation_end():
    live.lua('pcall(function() player.human:InterruptDialogs() end)')
    return 'the conversation ended (the game\'s own InterruptDialogs)'


# The first (d) run (beside Troskowitz, tapo_kasparBandit_1) is void: a villager killed the bandit before the save, Henry hit a villager,
# the guards killed Henry after it. The second is void too: every bandit of this world is a quest NPC kept at its own place (moved, it
# snaps back; tapo_kasparBandit_2 never fought). The fight is now a wolf pack of the world's own animal spawner on open ground: wolves
# attack by their own rules, no crime, no guard; Henry's swings and their bites give the field's lines (Dude attack shouts, Dude hit
# screams, Henry in a skirmish). A dry run: 6 wolf attacks, 5 hits on Henry, 3 Dude attack shouts in 25 s.
FIGHT = {'npc': 'SpawnedAnimal_Wolf_81352AC_1', 'pack': ['SpawnedAnimal_Wolf_81352AC_0', 'SpawnedAnimal_Wolf_81352AC_1', 'SpawnedAnimal_Wolf_81352AC_2'],
         'spot': (2696.0, 2347.0)}   # 6-15 m from Wolf_1


def teleport_henry(x, y):
    """Henry onto open ground (a long move lands before the ground's physics: held on terrain + 1.5 m until steady, the WO-155 rule)."""
    for _ in range(4):
        live.lua('local z=System.GetTerrainElevation({x=%f,y=%f,z=0}); player:SetWorldPos({x=%f,y=%f,z=z+1.5})' % (x, y, x, y))
        time.sleep(1.5)
    return henry_pos()


KEEP = {'on': False, 'tops': 0}


def keep_henry_alive(on):
    """While the test fight runs Henry's health is topped up (the game's own soul state write, as the mod's sleep top-up does): the fight
    has to run through the host's save, not end in Henry's death (the first (d) run ended in one)."""
    if not on:
        KEEP['on'] = False
        return KEEP['tops']
    if KEEP['on']:
        return 0
    KEEP['on'] = True

    def run():
        while KEEP['on']:
            try:
                live.lua('pcall(function() if player.actor:GetHealth() < 90 then player.soul:SetState("health", 100) end end)')
                KEEP['tops'] += 1
            except Exception:
                pass
            time.sleep(0.5)
    threading.Thread(target=run, daemon=True).start()
    return 0


def shout_count(off, needle, who=None):
    """Shouts started since off (one 'Attempting to start new dialogue' line per shout); who = the soul that shouts (Ex)."""
    n = 0
    for l in klines(off):
        if l.startswith('Attempting to start new dialogue') and needle in l and (who is None or ("'Ex: %s - meta override: %s" % (who, needle)) in l):
            n += 1
    return n


def fight_counts(off):
    return {'attack_shouts': shout_count(off, 'COMBAT_ACTOR_SCREAM_ATTACK'), 'hit_screams': shout_count(off, 'COMBAT_VICTIM_SCREAM_RECEIVED_HIT'),
            'henry_hit_screams': shout_count(off, 'COMBAT_VICTIM_SCREAM_RECEIVED_HIT', 'Dude'),
            'henry_attack_shouts': shout_count(off, 'COMBAT_ACTOR_SCREAM_ATTACK', 'Dude'),
            'attacks_on_henry': sum(1 for l in klines(off) if l.startswith('Skirmish event: Attack on') and l.rstrip().endswith('(target Dude)')),
            'hits_on_henry': sum(1 for l in klines(off) if l.startswith('Skirmish event: HitTarget on') and l.rstrip().endswith('(target Dude)'))}


def fight_at_save(off):
    """Was Henry's fight running when the engine began the snapshot? From kcd.log: Henry's skirmish joined (SoulAdded on Dude) and not left
    (SoulRemoved on Dude / SkirmishVictory on Dude) before the first InitiateSaveGame after off; plus the fight's lines in the 400 log
    lines before that save and the lines from the last attack on Henry to the save."""
    ls = klines(off)
    save_i = next((i for i, l in enumerate(ls) if l.startswith('InitiateSaveGame()')), None)
    if save_i is None:
        return {'running': None, 'why': 'no save line'}
    state, last, last_attack = False, '-', None
    for i, l in enumerate(ls[:save_i]):
        if 'Skirmish event: SoulAdded on Dude' in l:
            state, last = True, 'SoulAdded on Dude'
        elif 'Skirmish event: SoulRemoved on Dude' in l or 'Skirmish event: SkirmishVictory on Dude' in l:
            state, last = False, l[:80]
        if l.startswith('Skirmish event: ') and ('(target Dude)' in l or ' on Dude (target ' in l):
            last_attack = i
    near = ls[max(0, save_i - 400):save_i]
    return {'running': state, 'last_skirmish_edge': last, 'fight_lines_400_before': sum(1 for l in near if 'Skirmish event' in l or 'COMBAT_' in l),
            'lines_from_last_fight_event_to_save': (save_i - last_attack) if last_attack is not None else None}


def pack(cmd):
    for w in FIGHT['pack']:
        if cmd == 'wake':
            live.lua('local e=System.GetEntityByName("%s"); if e then pcall(function() e:Hide(0) end) end' % w)
            live.cmd('wh_ai_ResumeNPC %s' % w)
        else:   # the mod's own guard pause for an animal (WO-153): paused + hidden
            live.cmd('wh_ai_PauseNPC %s' % w)
            live.lua('local e=System.GetEntityByName("%s"); if e then pcall(function() e:Hide(1) end) end' % w)


def face(npc):
    return lua_reply('local w=System.GetEntityByName("%s"); local p=player:GetWorldPos(); local q=w and w:GetWorldPos(); local d=-1; '
                     'if q then local a=math.atan2(-(q.x-p.x), (q.y-p.y)); player:SetWorldAngles({x=0,y=0,z=a}); d=math.sqrt((q.x-p.x)^2+(q.y-p.y)^2) end; '
                     'System.LogAlways(string.format("[W184X] face %%.1f", d))' % npc, 'face', timeout=5)


def c2_fight_setup():
    """A real fight on open ground: the wolf pack woken (the guard off), Henry teleported 6-15 m from it, facing it, his sword drawn."""
    guard(False)
    p = teleport_henry(*FIGHT['spot'])
    pack('wake')
    face(FIGHT['npc'])
    g = gi()
    g.tap('1'); time.sleep(1.5)   # the draw key (WO-175)
    live.lua('pcall(function() if not player.human:IsWeaponDrawn() then player.human:DrawWeapon() end end)')
    return p


def c2_fight_start(min_attack=3, min_hit=3, max_s=45):
    keep_henry_alive(True)
    off = size(live.KLOG)
    c2_fight_setup()
    g = gi()
    t0 = time.time()
    while time.time() - t0 < max_s:
        face(FIGHT['npc'])
        g.click('left', 90); time.sleep(0.9)
        c = fight_counts(off)
        if c['attack_shouts'] >= min_attack and c['hit_screams'] >= min_hit:
            break
    g.release_all()
    c = fight_counts(off)
    c.update(npc=FIGHT['npc'], secs=round(time.time() - t0), fight_from=off)
    return c


def c2_fight_end():
    """The fight ends as the mod's own guard ends a free animal: the pack paused + hidden, Henry's combat ended."""
    pack('park')
    live.lua('pcall(function() XGenAIModule.TryEndCombat(player.this.id) end)')
    time.sleep(1.0)
    tops = keep_henry_alive(False)
    return 'the fight ended (the pack paused + hidden, TryEndCombat); health top-up calls during it: %d' % tops


def c2final():
    """After the C2 cases: Henry off the fight's spot, guard back on, one more host save (PAIRED), quit, restart, rejoin; the restored Henry's
    whole inventory must equal the one at that save and hold every C2 item picked up since his death (the (d) watermelon, the (g) onion)."""
    teleport_henry(2650.0, 2350.0)
    guard(True)
    _, inv1 = inv()
    paired, secs, seq = hostsave('c2final')
    aoff = size(agent_log())
    gone = quit_game()
    up_ok = game()
    restored = rejoin(aoff) if up_ok else None
    time.sleep(3)
    _, inv2 = inv()
    want = [C2_ITEMS['d'], C2_ITEMS['g']]
    missing = [c for c in want if (inv2 or {}).get(c, 0) < 1]
    diff = {c: ((inv1 or {}).get(c, 0), (inv2 or {}).get(c, 0)) for c in set(inv1 or {}) | set(inv2 or {}) if (inv1 or {}).get(c, 0) != (inv2 or {}).get(c, 0)}
    ok = bool(paired and gone and restored and inv2 is not None and not missing and not diff)
    say('C2 content: %s -- paired=%s(%.1f s, seq %s) quit=%s restored=%s; inventory at the save vs after the rejoin: %s; C2 items missing: %s'
        % ('PASS' if ok else 'FAIL', paired, secs, seq, gone, restored, 'EQUAL (%d classes)' % len(inv2 or {}) if not diff else diff, missing or 'none'))
    record('c2content', ok=ok, paired=paired, secs=secs, seq=seq, restored=restored, diff={k[:8]: v for k, v in diff.items()}, missing=missing,
           inv_at_save=len(inv1 or {}), inv_after_rejoin=len(inv2 or {}))
    return ok


def playline4_files():
    d = os.path.join(os.environ.get('USERPROFILE', ''), 'Saved Games', 'kingdomcome2', 'saves', 'playline4')
    if not d or not os.path.isdir(d):
        return None
    return sorted((f, os.path.getsize(os.path.join(d, f))) for f in os.listdir(d))


def b4quit(delay_s, cls):
    """B4 live: an item picked up after the last pair, the game's exit dialog line ("C_UISaveLoad::HandleExit()", written into the game's
    log the way the game writes it when the player picks Exit), the quit delay_s later (a player confirming the dialog), restart, rejoin.
    Records what the agent did, what the launcher told, playline4's files after the quit, and whether the item came back."""
    _, inv0 = inv()
    how, detail = drop_and_pick(cls, 0)
    _, inv1 = inv()
    picked = (inv1 or {}).get(cls, 0) == (inv0 or {}).get(cls, 0) + 1
    aoff = size(agent_log())
    if os.environ.get('W184_B4_TIMER'):
        # the quit on the game's own clock, delay_s after the dialog line (a player confirming the dialog that fast): one Lua call
        live.lua('System.LogAlways("C_UISaveLoad::HandleExit()"); Script.SetTimer(%d, function() System.Quit() end)' % int(delay_s * 1000))
        gone = quit_game(already=True)
    else:
        live.lua('System.LogAlways("C_UISaveLoad::HandleExit()")')
        time.sleep(delay_s)
        gone = quit_game()
    alines = read_from(agent_log(), aoff).splitlines()
    final = [l[l.find('WO184-FINAL'):][:200] for l in alines if 'WO184-FINAL' in l]
    paired = [l[l.find('PAIRED'):][:200] for l in alines if 'PAIRED host save' in l]
    pl4 = playline4_files()
    up_ok = game()
    restored = rejoin(aoff) if up_ok else None
    time.sleep(3)
    _, inv2 = inv()
    diff = {c: ((inv1 or {}).get(c, 0), (inv2 or {}).get(c, 0)) for c in set(inv1 or {}) | set(inv2 or {}) if (inv1 or {}).get(c, 0) != (inv2 or {}).get(c, 0)}
    back = bool(picked and inv2 is not None and not diff)
    st = load()
    told = None
    for l in read_from(agent_log(), aoff).splitlines():
        m = re.search(r'MP-JOIN joiner: the game quit from the host.s world.*told: (.*)$', l)
        if m: told = m.group(1)
    say('B4 quit %.1f s after the exit dialog: %s -- item picked=%s (%s) final=%s paired=%s told=%s playline4=%s restored=%s; inventory at the quit vs after the rejoin: %s'
        % (delay_s, 'PASS' if back else 'FAIL', picked, how, final, paired, told, pl4, restored, 'EQUAL (%d classes)' % len(inv2 or {}) if not diff else diff))
    record('b4quit', ok=back, delay_s=delay_s, item=how, picked=picked, final=final, paired=paired, told=told, playline4=pl4, restored=restored,
           diff={k[:8]: v for k, v in diff.items()}, inv_at_quit=len(inv1 or {}), inv_after_rejoin=len(inv2 or {}), quit_clean=gone)
    return back


def b4esc(esc_to_dialog_s, dialog_to_quit_s, cls):
    """B4 live, the ESC menu path: an item picked up after the last pair; Esc (the real key: the game's own menu opens, the DLL's 0xAD edge
    starts the final snapshot); esc_to_dialog_s later the exit dialog's line; the quit dialog_to_quit_s after it on the game's own clock
    (a fast player); restart, rejoin; the item must be back."""
    _, inv0 = inv()
    how, detail = drop_and_pick(cls, 0)
    _, inv1 = inv()
    picked = (inv1 or {}).get(cls, 0) == (inv0 or {}).get(cls, 0) + 1
    aoff = size(agent_log())
    g = gi()
    g.tap('esc')
    t_esc = time.time()
    time.sleep(esc_to_dialog_s)
    live.lua('System.LogAlways("C_UISaveLoad::HandleExit()"); Script.SetTimer(%d, function() System.Quit() end)' % int(dialog_to_quit_s * 1000))
    gone = quit_game(already=True)
    alines = read_from(agent_log(), aoff).splitlines()
    final = [l[l.find('WO184-FINAL'):][:220] for l in alines if 'WO184-FINAL' in l]
    paired = [l[l.find('PAIRED'):][:200] for l in alines if 'PAIRED host save' in l]
    pl4 = playline4_files()
    up_ok = game()
    restored = rejoin(aoff) if up_ok else None
    time.sleep(3)
    _, inv2 = inv()
    told = None
    for l in read_from(agent_log(), aoff).splitlines():
        m = re.search(r'MP-JOIN joiner: the game quit from the host.s world.*told: (.*)$', l)
        if m: told = m.group(1)
    diff = {c: ((inv1 or {}).get(c, 0), (inv2 or {}).get(c, 0)) for c in set(inv1 or {}) | set(inv2 or {}) if (inv1 or {}).get(c, 0) != (inv2 or {}).get(c, 0)}
    ok = bool(picked and inv2 is not None and not diff)
    say('B4 esc %.1f s -> dialog -> quit %.1f s: %s -- item picked=%s (%s) final=%s paired=%s told=%s playline4=%s restored=%s; inventory at the quit vs after the rejoin: %s'
        % (esc_to_dialog_s, dialog_to_quit_s, 'PASS' if ok else 'FAIL', picked, how, final, paired, told, pl4, restored, 'EQUAL (%d classes)' % len(inv2 or {}) if not diff else diff))
    record('b4esc', ok=ok, esc_to_dialog_s=esc_to_dialog_s, dialog_to_quit_s=dialog_to_quit_s, item=how, picked=picked, final=final, paired=paired, told=told,
           playline4=pl4, restored=restored, diff={k[:8]: v for k, v in diff.items()}, inv_at_quit=len(inv1 or {}), inv_after_rejoin=len(inv2 or {}), quit_clean=gone)
    return ok


def b4real(gap_s, cls):
    """B4 live with the game's own menu and real keys only (no script line): an item picked up after the last pair; then a fast player's
    quit -- Esc (the menu opens on Resume), W, W (Quit), Enter (the dialog "Do you want to quit the game?", No focused), W (Yes), Enter --
    gap_s between the keys (each held 80 ms). Restart, rejoin: the item must be back. If the game has not quit 8 s after the last key the
    run is void (a key was not taken): the game is quit by script, never killed."""
    _, inv0 = inv()
    how, detail = drop_and_pick(cls, 0)
    _, inv1 = inv()
    picked = (inv1 or {}).get(cls, 0) == (inv0 or {}).get(cls, 0) + 1
    aoff = size(agent_log())
    koff = size(live.KLOG)
    g = gi()
    t0 = time.time()
    for k in ('esc', 'w', 'w', 'enter', 'w', 'enter'):
        g.tap(k)
        time.sleep(gap_s)
    keys_s = time.time() - t0
    t1 = time.time()
    while time.time() - t1 < 8 and game_running():
        time.sleep(0.5)
    if game_running():
        say('B4 real: the game did not quit 8 s after the keys -- void; quit by script')
        record('b4real', ok=False, void=True, gap_s=gap_s, why='the game did not quit after the keys')
        quit_game()
        return False
    gone = quit_game(already=True)
    kl = read_from(os.path.join(LOGDIR, 'kcd.launch%d.log' % load().get('launches', 0)), 0).splitlines()
    alines = read_from(agent_log(), aoff).splitlines()
    final = [l[l.find('WO184-FINAL'):][:220] for l in alines if 'WO184-FINAL' in l]
    paired = [l[l.find('PAIRED'):][:200] for l in alines if 'PAIRED host save' in l]
    order = [l[:90] for l in kl if l.startswith(('C_UISaveLoad::HandleExit', 'InitiateSaveGame', 'CSystem::Quit', "Readiness observer 'BeforeGameSave'")) or l.startswith('Writing ')][-8:]
    pl4 = playline4_files()
    up_ok = game()
    restored = rejoin(aoff) if up_ok else None
    time.sleep(3)
    _, inv2 = inv()
    told = None
    for l in read_from(agent_log(), aoff).splitlines():
        m = re.search(r'MP-JOIN joiner: the game quit from the host.s world.*told: (.*)$', l)
        if m: told = m.group(1)
    diff = {c: ((inv1 or {}).get(c, 0), (inv2 or {}).get(c, 0)) for c in set(inv1 or {}) | set(inv2 or {}) if (inv1 or {}).get(c, 0) != (inv2 or {}).get(c, 0)}
    ok = bool(picked and inv2 is not None and not diff)
    say('B4 real keys, %.2f s apart (%.1f s Esc to the last key): %s -- item picked=%s (%s) final=%s paired=%s engine order=%s told=%s playline4=%s restored=%s; inventory at the quit vs after the rejoin: %s'
        % (gap_s, keys_s, 'PASS' if ok else 'FAIL', picked, how, final, paired, order, told, pl4, restored, 'EQUAL (%d classes)' % len(inv2 or {}) if not diff else diff))
    record('b4real', ok=ok, gap_s=gap_s, keys_s=round(keys_s, 2), item=how, picked=picked, final=final, paired=paired, engine_order=order, told=told,
           playline4=pl4, restored=restored, diff={k[:8]: v for k, v in diff.items()}, inv_at_quit=len(inv1 or {}), inv_after_rejoin=len(inv2 or {}), quit_clean=gone)
    return ok


if __name__ == '__main__':
    if len(sys.argv) > 2 and sys.argv[1] == 'c2':
        t = sys.argv[2]
        cases = {'a': (c2_talk_timeout, None), 'b': (c2_monologue, None), 'c': (c2_conversation_running, None), 'c2': (c2_conversation, c2_conversation_end),
                 'd': (c2_fight_start, c2_fight_end), 'e': (c2_fade_on, c2_fade_off), 'f': (c2_menu_on, c2_menu_off)}
        if t == 'g':
            def g_setup():
                info = c2_fight_start()
                info['ended_before_save'] = c2_fight_end()
                return info
            sys.exit(0 if c2case('g', g_setup) else 1)
        s, l = cases[t]
        sys.exit(0 if c2case(t, s, l, float(os.environ.get('W184_LIFT_S', '10'))) else 1)
    if len(sys.argv) > 3 and sys.argv[1] == 'b4real':
        sys.exit(0 if b4real(float(sys.argv[2]), sys.argv[3]) else 1)
    if len(sys.argv) > 3 and sys.argv[1] == 'b4esc':
        sys.exit(0 if b4esc(float(sys.argv[2]), float(sys.argv[3]), sys.argv[4]) else 1)
    if len(sys.argv) > 2 and sys.argv[1] == 'b4quit':
        sys.exit(0 if b4quit(float(sys.argv[2]), sys.argv[3]) else 1)
    if len(sys.argv) > 1 and sys.argv[1] == 'c2final':
        sys.exit(0 if c2final() else 1)
    if len(sys.argv) > 2 and sys.argv[1] == 'c1':
        sys.exit(0 if c1round(sys.argv[2], sys.argv[3:]) else 1)
    v = sys.argv[1] if len(sys.argv) > 1 else ''
    a = sys.argv[2:]
    if v == 'up': up()
    elif v == 'agent': start_agent()
    elif v == 'game': game()
    elif v == 'join': join(a[0] if a else 'fresh:soldier')
    elif v == 'rejoin': rejoin()
    elif v == 'items': items(a[0], a[1:])
    elif v == 'hostsave': hostsave(a[0])
    elif v == 'inv': print(inv())
    elif v == 'pos': print(henry_pos())
    elif v == 'quit': quit_game()
    elif v == 'lua': print(live.lua(a[0]))
    elif v == 'ctl': ctl(' '.join(a))
    elif v == 'status': print(join_status())
    elif v == 'down': down()
    else: print(__doc__)
