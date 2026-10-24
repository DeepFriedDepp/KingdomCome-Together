# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 Part A: two real copies of the Modding Tools game on one PC (test tool, never shipped).

Both copies run from mirror roots of the install (tools/rig/mirror.py), each with its own system.cfg and its own Saved
Games user folder; the second starts with -multi_inst. The player's install, settings and saves folder are only read (the
saves against the WO's hash list). The rig never reads input, never takes focus, never moves a window, and stops only
processes it started (pid + creation time + image).

    python tools/rig/rig.py machine                 record the machine (RAM, VRAM, disk, monitors)
    python tools/rig/rig.py check                   the pre-launch checks (saves hash list, mirror, lease)
    python tools/rig/rig.py up-a                    copy A to its main menu (no load, no injection)
    python tools/rig/rig.py up-b                    the A.3 checks, then copy B to its main menu
    python tools/rig/rig.py watch <minutes>         both up: device-removed lines, VRAM/RAM, ports, frame counts
    python tools/rig/rig.py down [a|b|all]          quit through the game's console, then the recorded process
    python tools/rig/rig.py status

Environment: KCD2MP_INSTALL (root A), KCDMP_RIG_ROOT_B (root B), KCDMP_RIG_RUN (the run's folder name).
"""
import json, os, re, sys, time, urllib.error, urllib.parse, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import machine, mirror, procs

REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
RIG = os.environ.get('KCDMP_RIG_DIR', os.path.join(REPO, 'release', 'w206rig'))
RUNS = os.environ.get('KCDMP_RIG_RUNS', os.path.join(REPO, 'research', 'WO-206', 'runs'))
STATE = os.path.join(RIG, 'rig-state.json')
LEASE = os.path.join(RIG, 'rig.lease')
SAVES_A = os.path.join(mirror.SAVED_GAMES, mirror.USER_FOLDER_REAL, 'saves')   # the player's real saves: never written
PROFILES_A = os.path.join(mirror.SAVED_GAMES, mirror.USER_FOLDER_REAL, 'profiles')
HASHES = os.path.join(RIG, 'saves-before.sha256')
EXE_REL = os.path.join('Bin', 'Win64ReleaseSteamLTO_DLL', 'KingdomCome.exe')
B_POOL_MB = 2048
VRAM_MARGIN_MB = 2048


def now(): return time.strftime('%H:%M:%S')
def say(*a): print(now(), *a, flush=True)


def run_dir():
    d = os.path.join(RUNS, os.environ.get('KCDMP_RIG_RUN', time.strftime('run-%m%d-%H%M')))
    os.makedirs(d, exist_ok=True)
    return d


def load_state():
    try:
        with open(STATE, encoding='utf-8') as f: return json.load(f)
    except (OSError, ValueError):
        return {}


def save_state(s):
    with open(STATE, 'w', encoding='utf-8') as f: json.dump(s, f, indent=1)


def note(event, **kv):
    """One JSON line per rig event in the run folder (what was started, sent, measured, stopped)."""
    row = dict(t=time.strftime('%Y-%m-%d %H:%M:%S'), event=event, **kv)
    with open(os.path.join(run_dir(), 'rig-events.jsonl'), 'a', encoding='utf-8') as f: f.write(json.dumps(row) + '\n')
    return row


# ---- the game's debug API ------------------------------------------------------------------------------------------

def api_up(port):
    try:
        with urllib.request.urlopen('http://localhost:%d/api/rpg/SoulList/SoulCount' % port, timeout=3) as r: r.read(64)
        return True
    except Exception:
        return False


def console(port, c, timeout=20):
    enc = urllib.parse.quote(c, safe='')
    if len(enc) > 1900: raise ValueError('command too long: %d encoded' % len(enc))
    # 503 = the game's API is busy with another request (an agent's batch): the command never ran, so it is sent again
    for attempt in range(40):
        try:
            with urllib.request.urlopen('http://localhost:%d/api/System/Console/ExecuteString?command=%s' % (port, enc), timeout=timeout) as r:
                return r.read(1 << 20).decode('utf-8', 'replace')
        except urllib.error.HTTPError as e:
            if e.code != 503 or attempt == 39: raise
            time.sleep(0.05 + 0.05 * attempt)
        except (ConnectionResetError, ConnectionAbortedError) as e:
            # a GET can run before the reset: never sent again; the step's postcondition tells whether it ran
            return '<reset after send: %s>' % e


def klog(root):
    """The copy's own log: kcd.log, or kcd(N).log -- a second instance writes kcd(1).log (and appends (1) to its user
    folder) even in its own root. The newest of them is the running copy's."""
    import glob
    logs = [p for p in glob.glob(os.path.join(root, 'kcd*.log')) if re.match(r'kcd(\(\d+\))?\.log$', os.path.basename(p))]
    return max(logs, key=os.path.getmtime) if logs else os.path.join(root, 'kcd.log')


# ---- the checks ------------------------------------------------------------------------------------------------------

def saves_ok(tag):
    """The real saves folder against the WO's hash list. A change stops the whole WO (the caller exits)."""
    changed, gone, added = procs.saves_check(SAVES_A, HASHES)
    expected, added = throwaway_split(added)
    note('saves-check', tag=tag, changed=changed[:50], gone=gone[:50], added=added[:50], throwaway_new=expected[:50])
    ok = not changed and not gone and not added
    say('real saves hash list:', 'MATCH' if ok else 'DIFFERS changed=%d gone=%d added=%d' % (len(changed), len(gone), len(added)),
        '(%d new file(s) in the throwaway %s, moved out by the rig)' % (len(expected), THROWAWAY) if expected else '')
    for k in (changed + gone + added)[:20]: say('   ', k)
    return ok


# A.5 first ran copy A on the real saves folder's throwaway playline4: the host's own world saves wrote over the loaded save
# and the game's save cap deleted three old ones (restored byte-equal). Since then both copies have their own user folders,
# so the real saves folder takes no file at all: no throwaway exemption (None), any change is a change.
THROWAWAY = None


def throwaway_split(added):
    """(new files in the throwaway playline, everything else). Pure."""
    exp = [k for k in added if THROWAWAY and os.path.normcase(k).split(os.sep)[0] == THROWAWAY]
    return exp, [k for k in added if k not in exp]


def move_throwaway_new(tag):
    """Move the copies' new autosaves out of the throwaway playline into the run folder (the hash list stays exact)."""
    _, _, added = procs.saves_check(SAVES_A, HASHES)
    exp, _ = throwaway_split(added)
    dst = os.path.join(run_dir(), 'autosaves-%s' % tag)
    for k in exp:
        os.makedirs(dst, exist_ok=True)
        os.replace(os.path.join(SAVES_A, k), os.path.join(dst, os.path.basename(k)))
    note('throwaway-moved', tag=tag, files=exp)
    return exp


def checks(for_b):
    bad = []
    if not saves_ok('pre-launch'): bad.append('the real saves differ from the hash list')
    for cid in ('A', 'B'):
        mv = mirror.verify(cid)
        if mv: bad.append('mirror root %s: %s' % (cid, '; '.join(mv[:5])))
    if for_b:
        st = load_state()
        a = st.get('a')
        if not procs.matches(a): bad.append('copy A is not running (started by this rig)')
        else:
            w = procs.LogWatch(klog(mirror.ROOT_A), since=st.get('a_since')); w.poll()
            if not w.menu: bad.append('copy A is not at its main menu')
            if w.device_lost: bad.append('copy A logged a device-removed line')
        v = machine.vram_free_mb()
        need = B_POOL_MB + VRAM_MARGIN_MB
        note('vram-before-b', **(v or {}), need_mb=need)
        if not v or v['free_mb'] is None: bad.append('free VRAM unknown')
        elif v['free_mb'] < need: bad.append('free VRAM %d MB < %d MB (the second pool + 2 GB)' % (v['free_mb'], need))
        else: say('free VRAM %d MB >= %d MB needed' % (v['free_mb'], need))
    return bad


# ---- the copies ------------------------------------------------------------------------------------------------------

def clean_env(extra=None):
    env = {k: v for k, v in os.environ.items() if not k.startswith('KCDMP_') and k != 'KCD2MP_INSTALL'}
    env.update(extra or {})
    return env


def wait_menu(watch, rec, secs, who):
    t0 = time.time()
    while time.time() - t0 < secs:
        watch.poll()
        if watch.device_lost: return 'device-lost'
        if not procs.matches(rec): return 'exited'
        if who == 'B' and watch.user_folder and not watch.user_folder.rstrip('\\').lower().endswith(mirror.USER_FOLDER_B.lower()):
            return 'wrong-user-folder'
        if watch.menu: return 'menu'
        time.sleep(1)
    return 'timeout'


# Copy A windowed without editing its player's files (the maintainer's choice, WO-206 A.4): renderer cvars on the
# command line. The game writes its profile at exit, so the profile is kept byte-equal before the launch and put back
# after the copy has gone if the game changed it.
A_WINDOWED = ['+r_Fullscreen', '0', '+r_FullscreenWindow', '0', '+r_WindowType', '0', '+r_Width', '1920', '+r_Height', '1080']
PROFILE_KEEP = os.path.join(RIG, 'profile-a-backup')


def keep_profile_a():
    import shutil
    if not os.path.isdir(PROFILE_KEEP): shutil.copytree(PROFILES_A, PROFILE_KEEP, copy_function=shutil.copy2)


def restore_profile_a():
    """Put back every profile file the game changed (only once copy A is down). Returns the files put back."""
    import shutil
    back = []
    if not os.path.isdir(PROFILE_KEEP): return back
    for d, _, fs in os.walk(PROFILE_KEEP):
        for f in fs:
            src = os.path.join(d, f)
            dst = os.path.join(PROFILES_A, os.path.relpath(src, PROFILE_KEEP))
            if not os.path.exists(dst) or procs.sha(dst) != procs.sha(src):
                shutil.copy2(src, dst); back.append(os.path.relpath(src, PROFILE_KEEP))
    note('profile-a-restore', files=back)
    return back


def up_a():
    st = load_state()
    if procs.matches(st.get('a')): say('copy A already up', st['a']['pid']); return 0
    exe = os.path.join(mirror.ROOT_A, EXE_REL)
    argv = [exe] + (A_WINDOWED if os.environ.get('KCDMP_RIG_A_WINDOWED', '1') == '1' else [])
    keep_profile_a()
    t0 = time.time()
    rec = procs.start(argv, mirror.ROOT_A, clean_env(), 'A')
    st['a'] = rec; st['a_since'] = t0; save_state(st)
    note('start', copy='A', **rec, argv=argv)
    say('copy A pid', rec['pid'])
    w = procs.LogWatch(klog(mirror.ROOT_A), since=t0)
    r = wait_menu(w, rec, 300, 'A')
    note('a-menu', result=r, user_folder=w.user_folder, http_port=w.http_port)
    say('copy A:', r, 'user folder', w.user_folder, 'port', w.http_port)
    return 0 if r == 'menu' else 1


def up_b():
    lease = procs.Lease(LEASE)
    bad = checks(for_b=True)
    if bad:
        for b in bad: say('NOT STARTING B:', b)
        note('b-refused', why=bad)
        return 1
    st = load_state()
    if procs.matches(st.get('b')): say('copy B already up', st['b']['pid']); return 0
    rootb = mirror.ROOT_B
    exe = os.path.join(rootb, EXE_REL)
    # the second copy's kcd.log must be its own: the previous one is kept in the run folder first
    import glob
    for old in glob.glob(os.path.join(rootb, 'kcd*.log')):
        if re.match(r'kcd(\(\d+\))?\.log$', os.path.basename(old)):
            os.replace(old, os.path.join(run_dir(), 'b-previous-%s-%s' % (time.strftime('%H%M%S'), os.path.basename(old))))
    a_watch = procs.LogWatch(klog(mirror.ROOT_A), from_start=False)
    t0 = time.time()
    rec = procs.start([exe, '-multi_inst'], rootb, clean_env(), 'B')
    st['b'] = rec; st['b_since'] = t0; save_state(st)
    note('start', copy='B', **rec, argv=[exe, '-multi_inst'])
    say('copy B pid', rec['pid'])
    w = procs.LogWatch(klog(rootb), since=t0)
    r = wait_menu(w, rec, 300, 'B')
    a_watch.poll()
    if r == 'wrong-user-folder':
        say('copy B uses the user folder %s -- stopping it at once' % w.user_folder)
        say('stop B:', procs.stop(rec))
    if a_watch.device_lost or w.device_lost:
        r = 'device-lost'
    note('b-menu', result=r, user_folder=w.user_folder, http_port=w.http_port, multi_lines=w.multi[:5],
         device_lost=(a_watch.device_lost + w.device_lost)[:5])
    say('copy B:', r, 'user folder', w.user_folder, 'port', w.http_port, 'instance lines', w.multi[:3])
    if r == 'device-lost': down('all')
    return 0 if r == 'menu' else 1


def frame_id(port, root, wait=3.0):
    """(engine frame number, wall time the console answered) through System.GetFrameID, read back from the line it
    writes into the copy's own log; None if it did not come back. The game's queue may run behind (a background
    copy's does), so the wall time is the one at which the line was found, an upper bound."""
    path = klog(root)
    off = os.path.getsize(path) if os.path.exists(path) else 0
    tok = 'RIGF%d' % int(time.time() * 1000)
    try: console(port, '#System.LogAlways("[%s] "..tostring(System.GetFrameID()))' % tok, timeout=5)
    except Exception: return None
    t0 = time.time()
    while time.time() - t0 < wait:
        with open(path, 'rb') as f:
            f.seek(off); data = f.read().decode('utf-8', 'replace')
        m = re.search(r'\[%s\] (\d+)' % tok, data)
        if m: return int(m.group(1)), time.time()
        time.sleep(0.1)
    return None


def fps(port, root, span=10.0):
    a = frame_id(port, root)
    if not a: return None
    time.sleep(span)
    b = frame_id(port, root)
    if not b or b[1] <= a[1]: return None
    return round((b[0] - a[0]) / (b[1] - a[1]), 1)


RAM_FLOOR_MB = 500


def watch(minutes):
    st = load_state()
    a, b = st.get('a'), st.get('b')
    wa, wb = procs.LogWatch(klog(mirror.ROOT_A), from_start=False), procs.LogWatch(klog(mirror.ROOT_B), from_start=False)
    pa = procs.LogWatch(klog(mirror.ROOT_A)); pa.poll()
    pb = procs.LogWatch(klog(mirror.ROOT_B)); pb.poll()
    ports = (pa.http_port, pb.http_port)
    t0 = time.time()
    rows = []
    verdict = 'pass'
    low_ram = 0
    while time.time() - t0 < minutes * 60:
        wa.poll(); wb.poll()
        alive = (procs.matches(a), procs.matches(b))
        row = {'s': round(time.time() - t0), 'alive': alive, 'vram': machine.vram_free_mb(), 'ram': machine.memory(),
               'ws_a': procs.working_set_mb(a['pid']) if alive[0] else None,
               'ws_b': procs.working_set_mb(b['pid']) if alive[1] else None,
               'api': (api_up(ports[0]) if ports[0] else None, api_up(ports[1]) if ports[1] else None)}
        if len(rows) % 6 == 0 and all(alive):   # a frame rate per copy every minute
            row['fps'] = (fps(ports[0], mirror.ROOT_A) if ports[0] else None, fps(ports[1], mirror.ROOT_B) if ports[1] else None)
        rows.append(row)
        note('watch', **row)
        if wa.device_lost or wb.device_lost:
            verdict = 'device-lost'
            note('device-lost', a=wa.device_lost[:5], b=wb.device_lost[:5])
            say('DEVICE-REMOVED line: stopping both copies'); down('all'); break
        if not all(alive):
            verdict = 'exited'
            say('a copy exited:', alive); break
        # the machine's own floor: two copies brought free RAM to 269 MB once (long1); a minute under the floor stops both
        low_ram = low_ram + 1 if (row['ram'] or {}).get('ram_free_mb', RAM_FLOOR_MB) < RAM_FLOOR_MB else 0
        if low_ram >= 6:
            verdict = 'low-ram'
            note('low-ram', ram=row['ram'], ws_a=row['ws_a'], ws_b=row['ws_b'])
            say('free RAM under %d MB for a minute: stopping both copies' % RAM_FLOOR_MB); down('all'); break
        if len(rows) % 6 == 1:
            say('t=%ds vram free %s MB, ram free %s MB, ws A %s / B %s, api %s, fps %s' % (
                row['s'], (row['vram'] or {}).get('free_mb'), row['ram']['ram_free_mb'],
                (row['ws_a'] or {}).get('ws_mb'), (row['ws_b'] or {}).get('ws_mb'), row['api'], row.get('fps')))
        time.sleep(10)
    note('watch-end', verdict=verdict, minutes=round((time.time() - t0) / 60, 1))
    say('watch:', verdict, 'after %.1f min' % ((time.time() - t0) / 60))
    return 0 if verdict == 'pass' else 1


def down(which='all'):
    st = load_state()
    for k in (['b', 'a'] if which == 'all' else [which]):
        rec = st.get(k)
        if not procs.matches(rec): say('copy', k.upper(), 'not running'); continue
        w = procs.LogWatch(klog(mirror.ROOT_A if k == 'a' else mirror.ROOT_B)); w.poll()
        if w.http_port:
            try: console(w.http_port, '#System.Quit()', timeout=5)
            except Exception: pass
        for _ in range(60):
            if not procs.matches(rec): break
            time.sleep(1)
        r = 'quit' if not procs.matches(rec) else procs.stop(rec)
        note('stop', copy=k.upper(), result=r, pid=rec['pid'])
        say('copy', k.upper(), r)
        if k == 'a' and os.path.isdir(PROFILE_KEEP):   # A.4 ran copy A in the install: the player's profile is put back if changed
            time.sleep(2)
            back = restore_profile_a()
            say('the player profile:', 'put back ' + ', '.join(back) if back else 'unchanged')
    return 0


def status():
    st = load_state()
    for k in ('a', 'b'):
        rec = st.get(k)
        say(k.upper(), 'up' if procs.matches(rec) else 'down', rec and rec.get('pid'))


if __name__ == '__main__':
    v = sys.argv[1] if len(sys.argv) > 1 else 'status'
    a = sys.argv[2:]
    if v == 'machine':
        r = machine.record(os.path.splitdrive(mirror.ROOT_A)[0] + '\\')
        note('machine', **r)
        print(json.dumps(r, indent=1))
    elif v == 'check':
        bad = checks(for_b=False)
        for b in bad: say('CHECK FAILED:', b)
        sys.exit(1 if bad else 0)
    elif v == 'saves': sys.exit(0 if saves_ok(a[0] if a else 'manual') else 1)
    elif v == 'up-a': sys.exit(up_a())
    elif v == 'up-b': sys.exit(up_b())
    elif v == 'watch': sys.exit(watch(float(a[0]) if a else 30))
    elif v == 'down': sys.exit(down(a[0] if a else 'all'))
    elif v == 'status': status()
    else: sys.exit(__doc__)
