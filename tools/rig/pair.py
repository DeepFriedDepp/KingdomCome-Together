# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 A.5-A.7: a pair of real copies joined through our relay, started the way the launcher starts them (test tool).

Each copy has its own folder under <rig>\\copies\\<id>: its own agent (the agent tees agent.log beside its exe), its own
DLL file (the DLL's log and settings files sit beside it), its own data and backup folders; its own pipe (KCDMP_INSTANCE),
its own game API port (the one its engine logs), its own dice/version ports. The relay runs on the test port (7790; the
maintainer's own relay keeps 7778), optionally with a fault plan (WO-206 B).

The launcher's order, which the rig keeps (Home.Wo159.cs / ConnectGate.cs): the game starts; the save loads; the
copy's log says "Gameplay started"; 10 s later (ConnectGateRule.SettleS) the DLL is injected and its own log must say
the tick is live (the launcher's VerifyInjectionAsync); then the agent starts with the launcher's arguments
(--host --port --no-voice, and --hosting for the host). A rig that starts things in another order says so.

    python tools/rig/pair.py prepare                     per-copy agent + DLL folders from the rig's newest build
    python tools/rig/pair.py relay [plan.json]           the relay on the test port (a fault plan: test-only switch)
    python tools/rig/pair.py up <A|B>                    a copy to its main menu (B: the A.3 checks first; its instance box answered)
    python tools/rig/pair.py guard <A|B> [--user-folder X]   dry run: refused when the user folder resolves to the real saves
    python tools/rig/pair.py host A <playline> <save> [--agent-delay S | --at-load S] [--hitch S] [--settle-freeze] [--twin]
    python tools/rig/pair.py join B                      the launcher's joiner, from its menu
    python tools/rig/pair.py collect <tag>               both bundles (LogPack) into the run folder
    python tools/rig/pair.py down                        agents, relay, both games (recorded processes only)
"""
import glob, json, os, re, shutil, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dialogs, mirror, procs, rig

COPIES = os.path.join(rig.RIG, 'copies')
RELAY_PORT = int(os.environ.get('KCDMP_RIG_RELAY_PORT', '7790'))
RELAY_HTTP = int(os.environ.get('KCDMP_RIG_RELAY_HTTP', '7791'))
SETTLE_S = 10.0          # the launcher's ConnectGateRule.SettleS
VERIFY_S = 8.0           # the launcher's VerifyInjectionAsync window
INJECTOR = os.path.join(rig.REPO, 'native', 'build', 'KCDMP_LauncherInjector', 'KCDMP_LauncherInjector.exe')


def plan(cid):
    """Everything a copy owns. Pure (tests check two plans never share a pipe, port or path)."""
    cid = cid.upper()
    base = os.path.join(COPIES, cid)
    n = {'A': 1, 'B': 2}.get(cid, 3)
    return {
        'id': cid,
        'root': mirror.ROOT_A if cid == 'A' else mirror.ROOT_B,
        'pipe': r'\\.\pipe\kcdmp-' + cid,
        'dir': base,
        'agent_dir': os.path.join(base, 'agent'),
        'dll_dir': os.path.join(base, 'dll'),
        'data_dir': os.path.join(base, 'data'),
        'backup_dir': os.path.join(base, 'backups'),
        'bundle_dir': os.path.join(base, 'bundle'),
        'dice_port': 5900 + 10 * n + 1,      # the maintainer's launcher/agent keep 5901/5902
        'version_port': 5900 + 10 * n + 2,
        'name': {'A': 'bench-host', 'B': 'bench-joiner'}.get(cid, 'bench-' + cid.lower()),
    }


def state(): return rig.load_state()


def prepare():
    """Fresh per-copy agent and DLL folders from the rig's build (release\\w206rig\\run\\agent, dll\\KCDMP_w206_*.dll)."""
    ddir = os.path.join(mirror.BUILD, 'dll') if os.environ.get('KCDMP_RIG_BUILD') else os.path.join(rig.RIG, 'dll')
    dlls = sorted(glob.glob(os.path.join(ddir, 'KCDMP_w2[0-9]*.dll')), key=os.path.getmtime)
    if not dlls: raise SystemExit('no DLL in the rig (run deploy.ps1)')
    src_agent = os.environ.get('KCDMP_RIG_AGENT_SRC', os.path.join(mirror.BUILD, 'agent'))
    for cid in ('A', 'B'):
        p = plan(cid)
        for d in ('agent_dir', 'dll_dir'):
            if os.path.isdir(p[d]): shutil.rmtree(p[d])
        shutil.copytree(src_agent, p['agent_dir'])
        os.makedirs(p['dll_dir'], exist_ok=True)
        # a new file name per build and copy: Windows does not run a second LoadLibrary of a name already loaded
        dst = os.path.join(p['dll_dir'], '%s_%s.dll' % (os.path.splitext(os.path.basename(dlls[-1]))[0], cid))
        shutil.copy2(dlls[-1], dst)
        for d in ('data_dir', 'backup_dir', 'bundle_dir'): os.makedirs(p[d], exist_ok=True)
        rig.say('copy', cid, 'agent from', src_agent, 'dll', os.path.basename(dst))
    # the mod's pak in root B is root A's (deploy.ps1 installs root A's)
    for cid in ('A', 'B'):
        mirror.create(cid)
        keys_pak(cid)
        mirror.seed_profile(cid)
    seed_saves()


def keys_pak(cid):
    """WO-207 A.4: the launcher's order -- the agent builds Mods\\kdcmp\\Data\\kdcmp_keys.pak from the copy's game files before the
    game starts (the mod template's pak is an older build's key map). Into the copy's mirror root only."""
    p = plan(cid)
    exe = os.path.join(p['agent_dir'], 'KcdMpClient.exe')
    mod = os.path.join(p['root'], 'Mods', 'kdcmp')
    env = rig.clean_env({'KCDMP_INSTANCE': cid})
    env['DOTNET_ROOT'] = os.environ.get('DOTNET_ROOT', os.path.expandvars(r'%USERPROFILE%\.dotnet-sdk8'))
    r = subprocess.run([exe, '--keys-pak', '--game-root', p['root'], '--mod-dir', mod], cwd=p['agent_dir'], env=env,
                       capture_output=True, text=True, timeout=120)
    line = next((l for l in r.stdout.splitlines() if l.startswith('KEYS-PAK ')), r.stdout.strip()[-300:])
    rig.note('keys-pak', copy=cid, rc=r.returncode, out=line)
    rig.say('copy', cid, 'keys pak:', line)
    if r.returncode != 0: raise SystemExit('copy %s: the keys pak was not built (%s)' % (cid, line))


# The saves a pair plays: copy A (the host) the WO's throwaway save440, copy B (the joiner) a save of ANOTHER playthrough as
# its own Henry ("bring my character" refuses a copy of the host's world: the same playthrough seed). Copies, from the
# player's saves folder (read only), into each copy's own user folder.
# KCDMP_RIG_HOST=B swaps the roles: copy B (started second, the window in front -- the game caps an unfocused window's frame
# rate) hosts and copy A joins. The seeds follow the role: the host's world into the host copy, the joiner's own Henry into the other.
HOST_CID = 'B' if os.environ.get('KCDMP_RIG_HOST') == 'B' else 'A'
JOIN_CID = 'A' if HOST_CID == 'B' else 'B'
SEED = {HOST_CID: [('playline4', 'save440.whs')], JOIN_CID: [('OLD/playline1', 'autosave026.whs')]}
SEED_TO = {HOST_CID: 'playline4', JOIN_CID: 'playline3'}


def seed_saves():
    real = os.path.join(mirror.SAVED_GAMES, mirror.USER_FOLDER_REAL, 'saves')
    # the joiner copy keeps no host world of an earlier run: its newest own save is its Henry source (WO-125), and a copy of the
    # host's world there is refused. Moved aside (the rig's own mirror user folder; nothing deleted, never the real saves).
    for name in mirror.user_folders(JOIN_CID):
        hp = os.path.join(mirror.SAVED_GAMES, name, 'saves', SEED_TO[HOST_CID])
        if os.path.isdir(hp):
            aside = os.path.join(mirror.SAVED_GAMES, name, 'saves-aside', '%s-%s' % (SEED_TO[HOST_CID], time.strftime('%m%d-%H%M%S')))
            os.makedirs(os.path.dirname(aside), exist_ok=True)
            shutil.move(hp, aside)
            rig.say('joiner copy %s: %s moved aside -> %s' % (JOIN_CID, hp, aside))
    # WO-207: the host copy starts every run with its seed only -- each join writes a manual save of the host's world, and at the game's
    # own limit (100) it deleted the oldest, the seed save440 itself (d16-c4: its reload found nothing). Moved aside, never deleted.
    for name in mirror.user_folders(HOST_CID):
        if real_saves_refusal(name, mirror.SAVED_GAMES, mirror.USER_FOLDER_REAL): continue   # never the real saves (refused below)
        hp = os.path.join(mirror.SAVED_GAMES, name, 'saves', SEED_TO[HOST_CID])
        if os.path.isdir(hp) and len(os.listdir(hp)) > len(SEED[HOST_CID]) + 1:
            aside = os.path.join(mirror.SAVED_GAMES, name, 'saves-aside', '%s-%s' % (SEED_TO[HOST_CID], time.strftime('%m%d-%H%M%S')))
            os.makedirs(os.path.dirname(aside), exist_ok=True)
            n = len(os.listdir(hp))
            shutil.move(hp, aside)
            rig.say('host copy %s: %s (%d files) moved aside -> %s' % (HOST_CID, hp, n, aside))
    for cid, files in SEED.items():
        for name in mirror.user_folders(cid):
            why = real_saves_refusal(name, mirror.SAVED_GAMES, mirror.USER_FOLDER_REAL)   # WO-207 0.2: a seed never lands in the real saves
            if why: raise SystemExit('refusing to seed copy %s: %s' % (cid, why))
            dst = os.path.join(mirror.SAVED_GAMES, name, 'saves', SEED_TO[cid])
            os.makedirs(dst, exist_ok=True)
            for sub, f in files:   # every run from the same world: the host's identify save writes over its loaded save
                shutil.copy2(os.path.join(real, sub, f), os.path.join(dst, f))
    extra = os.environ.get('KCDMP_RIG_HOST_SAVE_FILE')   # B1: a world saved before a shared scene, loaded as <its file name>
    if extra and re.match(r'^/[a-zA-Z]/', extra): extra = extra[1].upper() + ':' + extra[2:]   # a git-bash path
    if extra:
        for name in mirror.user_folders(HOST_CID):
            dst = os.path.join(mirror.SAVED_GAMES, name, 'saves', SEED_TO[HOST_CID])
            os.makedirs(dst, exist_ok=True)
            shutil.copy2(extra, os.path.join(dst, os.path.basename(extra)))
    rig.say('saves seeded: host', HOST_CID, SEED[HOST_CID], 'joiner', JOIN_CID, SEED[JOIN_CID], ('+ ' + os.path.basename(extra)) if extra else '')


def relay(plan_file=None):
    st = state()
    if procs.matches(st.get('relay')): rig.say('relay already up'); return 0
    exe = os.path.join(mirror.BUILD, 'relay', 'KcdMpServer.exe')
    argv = [exe, '--port', str(RELAY_PORT), '--urls', 'http://127.0.0.1:%d' % RELAY_HTTP]
    env = rig.clean_env()
    if plan_file:
        env['KCDMP_TEST_RELAY_FAULTS'] = '1'
        argv += ['--test-faults', os.path.abspath(plan_file)]
    log = os.path.join(rig.run_dir(), 'relay.log')
    rec = procs.start(argv, os.path.dirname(exe), env, 'relay', log_path=log)
    st['relay'] = rec; rig.save_state(st)
    rig.note('start', what='relay', **rec, argv=argv)
    rig.say('relay pid', rec['pid'], 'port', RELAY_PORT, 'faults' if plan_file else '')
    time.sleep(2)
    return 0


def latest_dll(p):
    d = sorted(glob.glob(os.path.join(p['dll_dir'], '*.dll')), key=os.path.getmtime)
    return d[-1] if d else None


def wait_line(watch_path, since, rx, secs, rec=None, watch=None):
    """Wait for a line matching rx: in a log that started at or after `since` (a launch), or, with `watch`, from where that
    LogWatch already is (a later moment of the same log). Returns (line, t) or (None, None)."""
    w = watch or procs.LogWatch(watch_path, since=since)
    t0 = time.time()
    while time.time() - t0 < secs:
        for l in w.poll():
            if re.search(rx, l): return l, time.time()
        if w.device_lost: return 'DEVICE-LOST ' + w.device_lost[0], time.time()
        if rec and not procs.matches(rec): return None, None
        time.sleep(0.25)
    return None, None


def up(cid):
    """A copy to its main menu. Copy B only after the A.3 checks (the real saves' hash list, the mirror root, copy A at its
    menu, free VRAM over B's pool + 2 GB) -- and with a device-removed watch on both logs while it starts."""
    p = plan(cid)
    st = state()
    key = cid.lower()
    if procs.matches(st.get(key)): rig.say('copy', cid, 'already up'); return 0
    if cid == 'B':
        bad = rig.checks(for_b=True)
        if bad:
            for b in bad: rig.say('NOT STARTING B:', b)
            rig.note('b-refused', why=bad); return 1
    bad = user_folder_refusal(cid)
    if bad:
        rig.say('NOT STARTING %s: %s' % (cid, bad)); rig.note('refused-real-saves', copy=cid, why=bad); return 1
    exe = os.path.join(p['root'], rig.EXE_REL)
    argv = [exe] + ([] if cid == 'A' else ['-multi_inst'])
    t0 = time.time()
    genv = {'KCDMP_INSTANCE': cid}
    for kv in filter(None, os.environ.get('KCDMP_RIG_GAME_ENV', '').split(';')):   # WO-207: a native twin's test switch, KCDMP_TEST_* only
        k, _, v = kv.partition('=')
        if k.startswith('KCDMP_TEST_'): genv[k] = v
    rec = procs.start(argv, p['root'], rig.clean_env(genv), cid)
    st[key] = rec; st[key + '_since'] = t0; rig.save_state(st)
    rig.note('start', copy=cid, **rec, argv=argv, instance=cid)
    rig.say('copy', cid, 'pid', rec['pid'])
    # WO-207 0.1: a second copy stops at the game's own Yes/No start box (A.4: -multi_inst only allows it); the rig answers it
    # (tools/rig/dialogs.py: the box among this copy's own windows, by class and owner, its Yes button by control id). 30 s: none -> on.
    box = None
    if cid == 'B':
        box = dialogs.answer_in_background(rec, 30.0, log=lambda m: (rig.say('copy B', m)), matches=procs.matches)
    # a second instance writes kcd(1).log (A.4); the first writes kcd.log
    log = os.path.join(p['root'], 'kcd.log' if cid == 'A' else 'kcd(1).log')
    line, _ = wait_line(log, t0, r"^PlayVideoOnly 'main_menu|^User folder is", 420, rec)
    w = procs.LogWatch(log, since=t0)
    t1 = time.time()
    while time.time() - t1 < 420:
        w.poll()
        if w.device_lost: break
        if w.user_folder and mirror.COPIES[cid]['user_folder'].lower() not in w.user_folder.lower():
            rig.say('copy %s uses the user folder %s -- stopped at once' % (cid, w.user_folder)); procs.stop(rec); return 1
        if w.menu or not procs.matches(rec): break
        time.sleep(1)
    if box:
        box['thread'].join(timeout=35)
        rig.note('instance-box', copy=cid, **{k: v for k, v in (box.get('result') or {'found': None}).items()})
    if w.device_lost:
        rig.note('device-lost', copy=cid, lines=w.device_lost[:5]); rig.say('DEVICE-REMOVED: stopping both'); down(); return 1
    rig.note('menu', copy=cid, ok=w.menu, user_folder=w.user_folder, http_port=w.http_port)
    rig.say('copy', cid, 'menu' if w.menu else 'NOT at its menu', 'port', w.http_port, 'user folder', w.user_folder)
    return 0 if w.menu else 1


def cfg_user_folder(root):
    """The sys_user_folder the copy's own system.cfg names (the last line wins, as the engine applies the file); None = none."""
    p = os.path.join(root, 'system.cfg')
    if not os.path.exists(p): return None
    val = None
    for l in open(p, encoding='utf-8', errors='replace'):
        m = re.match(r'^\s*sys_user_folder\s*=\s*(.*?)\s*$', l, re.I)
        if m: val = m.group(1).strip('"')
    return val


def real_saves_refusal(user_folder, saved_games, real_name):
    """WO-207 0.2, pure (tested): why a copy with this user folder would touch the player's real saves, or None. The game appends
    "(N)" to a second instance's folder, and an empty or missing folder is the game's default -- the real one."""
    if not user_folder: return 'no own user folder (the game\'s default is the player\'s real one)'
    path = user_folder if os.path.isabs(user_folder) else os.path.join(saved_games, user_folder)
    norm = lambda q: os.path.normcase(os.path.realpath(q)).rstrip('\\/')
    mine, real = norm(path), norm(os.path.join(saved_games, real_name))
    if mine == real or mine.startswith(real + os.sep): return 'the user folder %s resolves into the real saves folder %s' % (user_folder, real)
    if real.startswith(mine + os.sep): return 'the user folder %s contains the real saves folder %s' % (user_folder, real)
    base = re.sub(r'\(\d+\)$', '', os.path.basename(mine))
    if os.path.dirname(mine) == os.path.dirname(real) and base == os.path.basename(real):
        return 'the user folder %s is the real one with an instance suffix' % user_folder
    return None


def user_folder_refusal(cid, override=None):
    """A copy whose user folder (its system.cfg's, or a dry run's override) resolves to the real saves folder is never started."""
    uf = override if override is not None else cfg_user_folder(plan(cid)['root'])
    return real_saves_refusal(uf, mirror.SAVED_GAMES, mirror.USER_FOLDER_REAL)


def port_of(cid):
    st = state()
    w = procs.LogWatch(rig.klog(plan(cid)['root']), since=st.get(cid.lower() + '_since')); w.poll()
    return w.http_port


HITCH = ('#System.LogAlways("[W206HITCH] begin %d x 1000000"); for a = 1, %d do for b = 1, 1000000 do end end; '
         'System.LogAlways("[W206HITCH] end")')
# Counted loops: this game's Lua numbers are single-precision floats -- os.time() moves in 128 s steps and a counter stops at
# 2^24 -- and the engine's async clock does not advance inside one Lua call. Each counter here stays far below 2^24.


def hitch_rate(port):
    """Seconds per million loop turns on this copy, timed through the console (its own ~20 ms round trip is in the noise)."""
    t = time.time(); rig.console(port, HITCH % (10, 10), timeout=60); small = time.time() - t
    t = time.time(); rig.console(port, HITCH % (1000, 1000), timeout=120); big = time.time() - t   # ~3 s on the maintainer's PC
    return max(1e-4, (big - small) / 990.0)


def hitch(port, secs):
    """A.6: the field's slow settle on demand -- the game's main thread held for <secs> by one Lua busy loop (the stall a heavy load
    hitch gives: no frame, no timer, no console answer), sent from a thread (the console call returns when the loop ends).
    The loop is sized from a calibration run first (a short hold the agent does not see: it starts after)."""
    import threading
    rate = hitch_rate(port)
    n = max(1, int(round(secs / rate)))
    threading.Thread(target=lambda: rig.console(port, HITCH % (n, n), timeout=secs * 3 + 30), daemon=True).start()
    rig.note('hitch', secs=secs, per_million_s=round(rate, 4), turns_million=n)
    rig.say('hitch: %.4f s per million turns -> %d million for %.0f s' % (rate, n, secs))


def settle_freeze(cid, port, after_wait_s=8.5):
    """A.6: the field's slow settle -- the console answers but the mod's timer chains do not tick (the field: the emitter's first
    frame 8.5 s after the agent started waiting for it). Game time stops (t_Scale 0: Lua timers halt, the console stays live) as
    the agent starts, and runs again <after_wait_s> after the agent's log says it waits for the emitter (its wait is 6 s)."""
    import threading
    rig.console(port, 't_Scale 0')
    rig.note('freeze', on=True)
    alog = os.path.join(plan(cid)['agent_dir'], 'agent.log')
    def release():
        t0 = time.time()
        while time.time() - t0 < 120:
            if os.path.exists(alog) and "waiting for the mod's state emitter" in open(alog, encoding='utf-8', errors='replace').read():
                time.sleep(after_wait_s); break
            time.sleep(0.1)
        rig.console(port, 't_Scale 1')
        rig.note('freeze', on=False, after_s=round(time.time() - t0, 1))
        rig.say('copy', cid, 'game time runs again (%.1f s after the freeze)' % (time.time() - t0))
    th = threading.Thread(target=release); th.start()
    return th


def host(cid, playline, save, agent_delay=None, twin=False, at_load=None, hitch_s=None, freeze=False):
    """The launcher's host: the save loads from the menu; 'Gameplay started'; 10 s (ConnectGateRule.SettleS); inject, verify,
    the agent with --hosting. at_load=S: the 0.62.7 field order instead -- CONNECT pressed by hand S seconds after the load
    command, while the save still loads (inject, verify, the agent), before 'Gameplay started'."""
    p = plan(cid)
    st = state()
    key = cid.lower()
    since = st[key + '_since']
    log = rig.klog(p['root'])
    port = port_of(cid)
    off_t = time.time()
    w = procs.LogWatch(log, from_start=False)   # this launch's log, from now: the load's own "Gameplay started"
    rig.console(port, 'wh_sys_LoadGame %s %s' % (playline, save))
    rig.note('send', copy=cid, what='wh_sys_LoadGame %s %s' % (playline, save))
    if at_load is not None:
        time.sleep(max(0.0, off_t + float(at_load) - time.time()))
        rig.note('connect', copy=cid, t_after_load=round(time.time() - off_t, 1))
        rig.say('copy', cid, 'CONNECT %.1f s after the load command (the field order)' % (time.time() - off_t))
        live = inject_verify(cid, p, st, key)
        start_agent(cid, 'host', port, twin)
        line, t_world = wait_line(log, None, r'^Gameplay started', 300, st.get(key), watch=w)
        rig.say('copy', cid, 'Gameplay started after %.0f s' % ((t_world or time.time()) - off_t))
        return 0 if line else 1
    line, t_world = wait_line(log, None, r'^Gameplay started', 300, st.get(key), watch=w)
    if not line:
        rig.say('copy', cid, 'LOAD NOT CONFIRMED'); rig.note('load', copy=cid, ok=False); return 1
    rig.note('load', copy=cid, ok=True, secs=round(t_world - off_t, 1))
    rig.say('copy', cid, 'Gameplay started after %.0f s' % (t_world - off_t))
    settle = SETTLE_S if agent_delay is None else float(agent_delay)
    time.sleep(max(0.0, t_world + settle - time.time()))
    rig.note('connect', copy=cid, t_after_world=round(time.time() - t_world, 1))
    if not inject_verify(cid, p, st, key): return 1
    if hitch_s:
        hitch(port, hitch_s)
        time.sleep(0.5)
        rig.say('copy', cid, 'main thread held %.1f s (the slow settle) as the agent starts' % hitch_s)
    th = settle_freeze(cid, port) if freeze else None
    start_agent(cid, 'host', port, twin)
    if th: th.join()
    if hitch_s:
        line, _ = wait_line(log, None, r'\[W206HITCH\] end', hitch_s + 30, st.get(key), watch=procs.LogWatch(log, from_start=False))
        rig.say('copy', cid, 'hitch:', (line or 'no end line').strip()[-60:])
    return 0


def join(cid):
    p = plan(cid)
    st = state()
    return join_from_menu(cid, p, st, cid.lower(), port_of(cid))


def inject_verify(cid, p, st, key):
    dll = latest_dll(p)
    t0 = time.time()
    r = subprocess.run([INJECTOR, '--pid', str(st[key]['pid']), '--dll', dll], capture_output=True, text=True)
    nlog = os.path.join(p['dll_dir'], 'kcdmp-native-%s.log' % cid)
    live = False
    while time.time() - t0 < VERIFY_S + 2:
        if os.path.exists(nlog):
            m = re.findall(r'MAIN: (\d+) frames after ~\d+ ms -- tick is live', open(nlog, encoding='utf-8', errors='replace').read())
            if m and int(m[-1]) > 0: live = True; break
        time.sleep(0.3)
    rig.note('inject', copy=cid, dll=os.path.basename(dll), out=(r.stdout + r.stderr)[-300:], live=live)
    rig.say('copy', cid, 'injected', os.path.basename(dll), 'tick live' if live else 'NOT VERIFIED (the launcher would stop here)')
    return live


def host_ready(timeout):
    """The launcher's joiner gate, read from the rig's side: the host's agent holds the authority (its own log says so)."""
    a = os.path.join(plan(HOST_CID)['agent_dir'], 'agent.log')
    t0 = time.time()
    while time.time() - t0 < timeout:
        if os.path.exists(a) and re.search(r'MP-AUTHORITY-OWNER .*authority=self', open(a, encoding='utf-8', errors='replace').read()):
            return True
        time.sleep(1)
    return False


def join_from_menu(cid, p, st, key, port):
    """The launcher's joiner (WO-140 / WO-159): the game stays at its main menu; CONNECT once the host is in (the WO-154
    gate), inject, verify, the agent; then the first-join answer the menu's Join gives ("bring": the joiner's own save,
    seeded into its own saves folder, is the Henry source)."""
    if not host_ready(300):
        rig.say('copy', cid, 'the host never became the authority'); return 1
    rig.note('joiner-gate-open', copy=cid)
    if not inject_verify(cid, p, st, key): return 1
    start_agent(cid, 'joiner', port)
    pre = os.environ.get('KCDMP_RIG_PRE_JOIN_LUA')   # a twin's switch on both copies before the join starts (B2: mp_rejoin_recover off)
    if pre:
        time.sleep(3)   # the joiner's agent reads the mod's events from now
        for c in ('A', 'B'):
            rig.console(port_of(c), '#' + pre)
            rig.note('pre-join-lua', copy=c, lua=pre)
        rig.say('pre-join Lua on both copies:', pre)
        time.sleep(2)
    url = 'http://localhost:%d/join-choice?c=bring' % p['version_port']
    t0 = time.time()
    while time.time() - t0 < 90:
        try:
            import urllib.request
            with urllib.request.urlopen(urllib.request.Request(url, method='POST'), timeout=3) as r:
                if r.status == 200:
                    rig.note('join-choice', copy=cid, choice='bring', after_s=round(time.time() - t0, 1))
                    rig.say('copy', cid, 'first-join answer sent: bring'); return 0
        except Exception:
            pass
        time.sleep(1)
    rig.say('copy', cid, 'the agent did not take the first-join answer in 90 s'); return 1


def start_agent(cid, role, port, twin=False):
    """twin: A.6's mutation twin -- the 0.62.7 field fix (keep the log tail of a loaded mod whose emitter has not ticked) off."""
    p = plan(cid)
    st = state()
    exe = os.path.join(p['agent_dir'], 'KcdMpClient.exe')
    argv = [exe, '--host', '127.0.0.1', '--port', str(RELAY_PORT), '--no-voice'] + (['--hosting'] if role == 'host' else []) + \
           ['--name', p['name'], '--game-api', 'http://localhost:%d' % port, '--dice-ipc-port', str(p['dice_port']),
            '--version-ipc-port', str(p['version_port']), '--no-discord']
    env = rig.clean_env({'KCDMP_INSTANCE': cid, 'KCD2MP_INSTALL': p['root'], 'KCDMP_DATA_DIR': p['data_dir'],
                         'KCDMP_BACKUP_DIR': p['backup_dir']})
    if twin: env['KCDMP_TEST_W206_NO_TAIL_KEEP'] = '1'
    if os.environ.get('KCDMP_RIG_RESEARCH') == '1':   # the twins' research verbs (C2: a host-only clock write; C3: a death from health;
        env['KCDMP_TEST_W200'] = '1'; env['KCDMP_TEST_W206'] = '1'; env['KCDMP_TEST_W194'] = '1'   # B1: a scene started by hand)
    for kv in filter(None, os.environ.get('KCDMP_RIG_AGENT_ENV', '').split(';')):   # a twin's test switch (K=V;...), KCDMP_TEST_* only
        k, _, v = kv.partition('=')
        if k.startswith('KCDMP_TEST_'): env[k] = v
    env['DOTNET_ROOT'] = os.environ.get('DOTNET_ROOT', os.path.expandvars(r'%USERPROFILE%\.dotnet-sdk8'))
    rec = procs.start(argv, p['agent_dir'], env, 'agent-' + cid)
    st['agent_' + cid.lower()] = rec; rig.save_state(st)
    rig.note('start', what='agent', copy=cid, **rec, argv=argv, twin=twin)
    rig.say('agent', cid, 'pid', rec['pid'], role, 'TWIN (the 0.62.7 tail fix off)' if twin else '')


def collect(tag):
    """Both copies' files into <run>\\bundles\\<tag>-<id>\\ (the launcher's bundle layout), then LogPack's bundle."""
    out = {}
    for cid in ('A', 'B'):
        p = plan(cid)
        d = os.path.join(rig.run_dir(), 'bundles', '%s-%s' % (tag, cid))
        if os.path.isdir(d): shutil.rmtree(d)
        os.makedirs(d)
        def put(src, name):
            if src and os.path.exists(src): shutil.copy2(src, os.path.join(d, name))
        put(rig.klog(p['root']), 'kcd.log')
        put(os.path.join(p['root'], 'kcdmp-native.mirror.log'), 'kcdmp-native.mirror.log')
        put(os.path.join(p['dll_dir'], 'kcdmp-native-%s.log' % cid), 'kcdmp-native.log')
        put(os.path.join(p['agent_dir'], 'agent.log'), 'agent.log')
        for f in ('kcdmp-anim.jsonl', 'kcdmp-anim.1.jsonl', 'digest.jsonl', 'applied.jsonl'):   # WO-210: the probe's rotated piece too (E.1's overlap)
            put(os.path.join(p['root'], f), f)
            put(os.path.join(p['agent_dir'], f), f)
        put(os.path.join(rig.run_dir(), 'relay.log'), 'relay.log')
        tool = os.path.join(mirror.BUILD, 'logpack', 'KcdMp.LogPack.Tool.exe')
        packed = os.path.join(d + '.packed')
        if os.path.exists(tool):
            r = subprocess.run([tool, d, packed, '--no-verify'], capture_output=True, text=True)
            rig.note('pack', copy=cid, out=r.stdout[-400:])
        out[cid] = d
        rig.say('bundle', cid, d)
    return out


def down(tag='run'):
    """Agents, relay, both games (recorded processes only); then the copies' new autosaves out of the throwaway playline, the
    real saves against the hash list, and the mirror root's links. A change in the real saves stops the WO (exit 3)."""
    st = state()
    for k in ('agent_b', 'agent_a', 'relay'):
        r = procs.stop(st.get(k))
        rig.note('stop', what=k, result=r)
        rig.say(k, r)
    rig.down('all')
    time.sleep(3)
    moved = rig.move_throwaway_new(tag)
    if moved: rig.say('moved %d new autosave(s) out of the throwaway playline' % len(moved))
    ok = rig.saves_ok('after-' + tag)
    bad = mirror.verify('A') + mirror.verify('B')
    rig.say('mirror roots:', 'PASS' if not bad else 'FAIL %s' % bad[:3])
    return 0 if ok and not bad else 3


if __name__ == '__main__':
    v = sys.argv[1] if len(sys.argv) > 1 else ''
    a = sys.argv[2:]
    if v == 'prepare': prepare()
    elif v == 'relay': sys.exit(relay(a[0] if a else None))
    elif v == 'up': sys.exit(up(a[0]))
    elif v == 'guard':   # WO-207 0.2's dry run: would this copy (or this user folder) be started? Nothing is started.
        uf = a[a.index('--user-folder') + 1] if '--user-folder' in a else None
        why = user_folder_refusal(a[0].upper(), uf)
        rig.note('guard-dry-run', copy=a[0].upper(), user_folder=uf if uf is not None else cfg_user_folder(plan(a[0])['root']), refused=why)
        rig.say('guard %s (%s): %s' % (a[0].upper(), uf if uf is not None else 'its system.cfg', 'REFUSED -- ' + why if why else 'allowed'))
        sys.exit(1 if why else 0)
    elif v == 'host':
        delay = float(a[a.index('--agent-delay') + 1]) if '--agent-delay' in a else None
        at_load = float(a[a.index('--at-load') + 1]) if '--at-load' in a else None
        hitch_s = float(a[a.index('--hitch') + 1]) if '--hitch' in a else None
        twin, freeze = '--twin' in a, '--settle-freeze' in a
        valued = ('--agent-delay', '--at-load', '--hitch')
        a = [x for i, x in enumerate(a) if x not in valued + ('--twin', '--settle-freeze') and (i == 0 or a[i - 1] not in valued)]
        sys.exit(host(a[0], a[1], a[2], delay, twin, at_load, hitch_s, freeze))
    elif v == 'join': sys.exit(join(a[0]))
    elif v == 'agent': start_agent(a[0], a[1], int(a[2]))
    elif v == 'collect': collect(a[0] if a else 'run')
    elif v == 'down': sys.exit(down(a[0] if a else 'run'))
    else: sys.exit(__doc__)
