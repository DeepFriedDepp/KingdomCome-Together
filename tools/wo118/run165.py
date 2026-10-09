# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-165 live harness (never shipped): the Modding Tools game on this machine as the JOINER (or the host) of a synthetic peer.

usage:
  python run165.py game <exe>                       start the game (cwd = the install root); NO focus change by this script
  python run165.py inject <injector.exe> <dll>      inject a COPY of KCDMP.dll (a new file name per rebuild) into the running game
  python run165.py load <playline> <save>           wh_sys_LoadGame, then print the "Loading saved game" line that names the save
  python run165.py relay <KcdMpServer.dll>          relay on 7778 (if free)
  python run165.py peer <tag> <plan> [opts...]      the synthetic peer (plan mode, --claim-host = the authority), connected FIRST
  python run165.py agent <tag> <KcdMpClient.dll> [ENV=VAL ...]   the agent (KCDMP_TEST_JOINER_IN_WORLD=1 = a joiner in the peer's world)
  python run165.py stop peer|agent|relay|all
  python run165.py con <console command>            one console command through the game's REST API
  python run165.py frame [n]                        the DLL's last n FRAME lines (ours_us_mean)
  python run165.py snap [--since HH:MM:SS]          MP-FIGHTSNAP p50/p90/max of this run (fightsnap165.py on the native mirror log)

Environment: KCD2MP_INSTALL (the Modding Tools folder holding kcd.log), DOTNET_ROOT. Logs: tools/wo118/logs (git-ignored).
"""
import json, os, re, socket, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live

PEER = os.environ.get('SYNTHPEER') or os.path.join(live.HERE, 'synthpeer', 'bin', 'Release', 'net8.0', 'SynthPeer.dll')
STATE = os.path.join(live.LOGS, 'run165.state.json')
DETACHED = 0x00000008 | 0x00000200
VERSION = os.path.join(live.HERE, '..', '..', 'VERSION')


def launch(path):
    return [path] if path.lower().endswith('.exe') else [live.dotnet(), path]


def listening(port):
    s = socket.socket(); s.settimeout(0.5)
    try:
        return s.connect_ex(('127.0.0.1', port)) == 0
    finally:
        s.close()


def load_state():
    return json.load(open(STATE)) if os.path.exists(STATE) else {}


def save_state(st):
    json.dump(st, open(STATE, 'w'))


def game(exe):
    st = load_state()
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(exe))))
    p = subprocess.Popen([exe], cwd=root, creationflags=DETACHED)
    st['game'] = p.pid; save_state(st)
    print('game pid', p.pid)
    for _ in range(240):
        if live.up(): print('REST up'); return
        time.sleep(1)
    print('REST not up after 240 s')


def inject(injector, dll):
    st = load_state()
    pid = st.get('game')
    if not pid: sys.exit('no game pid recorded')
    r = subprocess.run([injector, '--pid', str(pid), '--dll', os.path.abspath(dll)], capture_output=True, text=True)
    print(r.stdout[-2000:], r.stderr[-2000:])
    time.sleep(3)
    for l in live.tail(live.NLOG, 30, r'attached|WO165-REPLAY|WO118-NATIVE|MOD INIT'):
        print('native', l[:200])


def load(playline, save):
    print(live.cmd('wh_sys_LoadGame %s %s' % (playline, save)))
    for _ in range(120):
        hits = live.tail(live.KLOG, 3, r"Loading saved game")
        if hits: print('kcd ', hits[-1][-160:])
        if any(('playline%s' % playline) in h and save in h for h in hits): return
        time.sleep(1)


def relay(dll):
    st = load_state()
    if listening(7778):
        print('7778 already listening'); return
    r = subprocess.Popen(launch(os.path.abspath(dll)) + ['--port', '7778'], cwd=os.path.dirname(os.path.abspath(dll)),
                         stdout=open(os.path.join(live.LOGS, 'relay165.log'), 'a'), stderr=subprocess.STDOUT, creationflags=DETACHED)
    for _ in range(40):
        if listening(7778): break
        time.sleep(0.25)
    st['relay'] = r.pid; save_state(st)
    print('relay pid', r.pid)


def peer(tag, plan, opts):
    st = load_state()
    log = os.path.join(live.LOGS, 'run165.peer.%s.log' % tag)
    h = subprocess.Popen([live.dotnet(), PEER, '--port', '7778', '--name', 'synth-host', '--plan', os.path.abspath(plan),
                          '--version-file', os.path.abspath(VERSION)] + opts,
                         stdout=open(log, 'w'), stderr=subprocess.STDOUT, creationflags=DETACHED)
    st['peer'] = h.pid; st['peerlog'] = log; save_state(st)
    time.sleep(2)
    print('peer pid', h.pid, '->', log)
    print(open(log).read()[-1500:])


def agent(tag, dll, envs):
    st = load_state()
    env = dict(os.environ)
    for kv in envs:
        k, v = kv.split('=', 1)
        env[k] = v
    log = os.path.join(live.LOGS, 'run165.agent.%s.log' % tag)
    a = subprocess.Popen(launch(os.path.abspath(dll)) + ['--host', '127.0.0.1', '--port', '7778', '--name', 'wo165-agent', '--no-voice', '--no-discord'],
                         cwd=os.path.dirname(os.path.abspath(dll)), stdout=open(log, 'w'), stderr=subprocess.STDOUT,
                         creationflags=DETACHED, env=env)
    st['agent'] = a.pid; st['agentlog'] = log; save_state(st)
    print('agent pid', a.pid, '->', log)


def stop(what):
    st = load_state()
    for k in (['peer', 'agent', 'relay'] if what == 'all' else [what]):
        if k in st:
            subprocess.run(['taskkill', '/PID', str(st[k]), '/F'], capture_output=True)
            print('stopped', k, st.pop(k))
    save_state(st)


def frame(n):
    for l in live.tail(live.NLOG, n, r'FRAME '):
        print(l[:260])


def snap(since):
    args = [sys.executable, os.path.join(live.HERE, 'fightsnap165.py'), '--native', live.NLOG]
    st = load_state()
    if 'agentlog' in st: args += ['--agent', st['agentlog']]
    if since:
        # a copy of the lines after the stamp, so a long-running log measures only this run
        out = os.path.join(live.LOGS, 'run165.snap.native.log')
        keep = False
        with open(live.NLOG, encoding='utf-8', errors='replace') as f, open(out, 'w', encoding='utf-8') as g:
            for l in f:
                if not keep and l.startswith('[') and l[1:9] >= since: keep = True
                if keep: g.write(l)
        args[3] = out
    print(subprocess.run(args, capture_output=True, text=True).stdout)


if __name__ == '__main__':
    v = sys.argv[1]
    if v == 'game': game(sys.argv[2])
    elif v == 'inject': inject(sys.argv[2], sys.argv[3])
    elif v == 'load': load(sys.argv[2], sys.argv[3])
    elif v == 'relay': relay(sys.argv[2])
    elif v == 'peer': peer(sys.argv[2], sys.argv[3], sys.argv[4:])
    elif v == 'agent': agent(sys.argv[2], sys.argv[3], sys.argv[4:])
    elif v == 'stop': stop(sys.argv[2])
    elif v == 'con': print(live.cmd(' '.join(sys.argv[2:])))
    elif v == 'frame': frame(int(sys.argv[2]) if len(sys.argv) > 2 else 5)
    elif v == 'snap': snap(sys.argv[3] if len(sys.argv) > 3 and sys.argv[2] == '--since' else None)
