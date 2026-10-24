# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 A.3 / A.7: the rig's processes, its lease, and its safety checks (test tool, never shipped).

* A process the rig starts is recorded as (pid, creation time, image path). It is only ever stopped through that
  record: the pid must still be that image AND have that creation time. Nothing is ever killed by image name.
* One rig at a time: a lease file holding the owner's (pid, creation time); a lease whose owner is gone is stale.
* The real saves: the WO's byte-equal backup's hash list must match before a launch and after every run.
* A device-removed or driver-reset line in either copy's kcd.log stops both copies.
Nothing here reads input, takes focus, or moves a window.
"""
import ctypes, ctypes.wintypes as W, hashlib, json, os, re, subprocess, time

K32 = ctypes.windll.kernel32
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
PROCESS_TERMINATE = 0x0001
SYNCHRONIZE = 0x00100000

K32.OpenProcess.restype = W.HANDLE
K32.OpenProcess.argtypes = [W.DWORD, W.BOOL, W.DWORD]


def _creation(h):
    c, e, k, u = W.FILETIME(), W.FILETIME(), W.FILETIME(), W.FILETIME()
    if not K32.GetProcessTimes(h, ctypes.byref(c), ctypes.byref(e), ctypes.byref(k), ctypes.byref(u)): return None
    return (c.dwHighDateTime << 32) | c.dwLowDateTime


def _image(h):
    buf = ctypes.create_unicode_buffer(1024)
    n = W.DWORD(1024)
    if not K32.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(n)): return None
    return buf.value


def _still_running(h):
    code = W.DWORD()
    return bool(K32.GetExitCodeProcess(h, ctypes.byref(code))) and code.value == 259   # STILL_ACTIVE


def identify(pid):
    """(creation time, image) of a live pid, or None."""
    h = K32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not h: return None
    try:
        if not _still_running(h): return None
        return _creation(h), _image(h)
    finally:
        K32.CloseHandle(h)


def record(pid, role):
    ident = identify(pid)
    if ident is None: return None
    return {'pid': pid, 'created': ident[0], 'image': ident[1], 'role': role}


def matches(rec):
    """The recorded process is still the same process (pid, creation time and image)."""
    if not rec: return False
    ident = identify(rec['pid'])
    return bool(ident) and ident[0] == rec['created'] and (ident[1] or '').lower() == (rec['image'] or '').lower()


def stop(rec, wait=30.0):
    """Terminate exactly the recorded process. Returns 'gone', 'stopped', 'not ours' or 'failed'."""
    if not rec: return 'gone'
    ident = identify(rec['pid'])
    if ident is None: return 'gone'
    if ident[0] != rec['created'] or (ident[1] or '').lower() != (rec['image'] or '').lower(): return 'not ours'
    h = K32.OpenProcess(PROCESS_TERMINATE | SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, False, rec['pid'])
    if not h: return 'failed'
    try:
        if _creation(h) != rec['created']: return 'not ours'   # the pid was reused between the two opens
        K32.TerminateProcess(h, 1)
        K32.WaitForSingleObject(h, int(wait * 1000))
        return 'stopped'
    finally:
        K32.CloseHandle(h)


def start(argv, cwd, env, role, log_path=None):
    """Start a process detached from this tool's console (a stopped tool must not take a game with it)."""
    flags = 0x00000008 | 0x00000200   # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP
    out = open(log_path, 'ab') if log_path else subprocess.DEVNULL
    p = subprocess.Popen(argv, cwd=cwd, env=env, creationflags=flags, stdout=out, stderr=subprocess.STDOUT if log_path else subprocess.DEVNULL,
                         stdin=subprocess.DEVNULL)
    for _ in range(50):
        r = record(p.pid, role)
        if r: return r
        time.sleep(0.1)
    return {'pid': p.pid, 'created': None, 'image': None, 'role': role}


# ---- the lease -----------------------------------------------------------------------------------------------------

class Lease:
    def __init__(self, path):
        self.path = path
        self.held = False

    def take(self, what):
        me = record(os.getpid(), 'rig')
        for _ in range(2):
            try:
                fd = os.open(self.path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                with os.fdopen(fd, 'w', encoding='utf-8') as f:
                    json.dump({'owner': me, 'what': what, 'since': time.strftime('%Y-%m-%d %H:%M:%S')}, f)
                self.held = True
                return True
            except FileExistsError:
                try:
                    with open(self.path, encoding='utf-8') as f: cur = json.load(f)
                except (OSError, ValueError):
                    cur = {}
                if matches(cur.get('owner')): return False   # another rig runs
                os.remove(self.path)                          # its owner is gone: a stale lease
        return False

    def give_back(self):
        if self.held and os.path.exists(self.path): os.remove(self.path)
        self.held = False


# ---- the real saves -------------------------------------------------------------------------------------------------

def sha(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        for b in iter(lambda: f.read(1 << 20), b''): h.update(b)
    return h.hexdigest()


def saves_check(saves_dir, hash_list):
    """(changed, gone, added) against the hash list written at the WO's backup ("sha  size  relpath" lines)."""
    before = {}
    with open(hash_list, encoding='utf-8') as f:
        for l in f:
            h, n, k = l.rstrip('\n').split('  ', 2)
            before[k] = (int(n), h)
    now = {}
    for d, _, fs in os.walk(saves_dir):
        for fn in fs:
            p = os.path.join(d, fn)
            k = os.path.relpath(p, saves_dir)
            st = os.path.getsize(p)
            b = before.get(k)
            # only a file whose size matches is hashed; a size change is already a change
            now[k] = (st, sha(p) if b and b[0] == st else None)
    changed = sorted(k for k in before if k in now and now[k] != before[k])
    gone = sorted(k for k in before if k not in now)
    added = sorted(k for k in now if k not in before)
    return changed, gone, added


# ---- the logs ---------------------------------------------------------------------------------------------------------

DEVICE_LOST = re.compile(r'DEVICE_REMOVED|DEVICE_HUNG|DEVICE_RESET|[Dd]evice (?:was )?removed|[Dd]evice lost|'
                         r'GetDeviceRemovedReason|\bTDR\b|driver (?:has )?(?:stopped|reset)|DRIVER_INTERNAL_ERROR')
USER_FOLDER = re.compile(r"^User folder is '([^']*)'")
HTTP_PORT = re.compile(r"Starting HTTP server on port '(\d+)'")
MAIN_MENU = re.compile(r"^PlayVideoOnly 'main_menu")
MULTI_PROMPT = re.compile(r'another instance', re.I)


LOG_STARTED = re.compile(r'Log Started at (\d{4}-\d{2}-\d{2})\s+(\d{2}:\d{2}:\d{2})')


class LogWatch:
    """Follows one kcd.log from an offset; remembers what the rig asks about. With `since` (a time.time()), nothing is
    read until the file in place is a log that game started at or after it (the engine starts a new kcd.log at each
    launch; until then the file is the previous game's, which ends at its menu and would read as "at the menu")."""
    def __init__(self, path, from_start=True, since=None):
        self.path = path
        self.since = since
        self.ours = since is None
        self.off = 0 if from_start else (os.path.getsize(path) if os.path.exists(path) else 0)
        self.device_lost = []
        self.user_folder = None
        self.http_port = None
        self.menu = False
        self.multi = []
        self.lines = 0

    def poll(self):
        if not os.path.exists(self.path): return []
        size = os.path.getsize(self.path)
        if size < self.off: self.off = 0   # a new log
        if not self.ours:
            with open(self.path, 'rb') as f: head = f.read(600).decode('utf-8', 'replace')
            m = LOG_STARTED.search(head)
            if not m: return []
            started = time.mktime(time.strptime(m.group(1) + ' ' + m.group(2), '%Y-%m-%d %H:%M:%S'))
            if started < self.since - 30: return []
            self.ours = True
            self.off = 0
        with open(self.path, 'rb') as f:
            f.seek(self.off)
            data = f.read()
        cut = data.rfind(b'\n')
        if cut < 0: return []
        self.off += cut + 1
        new = data[:cut + 1].decode('utf-8', 'replace').splitlines()
        for l in new:
            self.lines += 1
            l = l.rstrip('\r')
            if DEVICE_LOST.search(l): self.device_lost.append(l[:300])
            m = USER_FOLDER.match(l)
            if m: self.user_folder = m.group(1)
            m = HTTP_PORT.search(l)
            if m: self.http_port = int(m.group(1))
            if MAIN_MENU.match(l): self.menu = True
            if MULTI_PROMPT.search(l): self.multi.append(l[:300])
        return new


def working_set_mb(pid):
    class PMC(ctypes.Structure):
        _fields_ = [('cb', W.DWORD), ('PageFaultCount', W.DWORD), ('PeakWorkingSetSize', ctypes.c_size_t),
                    ('WorkingSetSize', ctypes.c_size_t), ('a', ctypes.c_size_t), ('b', ctypes.c_size_t),
                    ('c', ctypes.c_size_t), ('d', ctypes.c_size_t), ('PagefileUsage', ctypes.c_size_t),
                    ('PeakPagefileUsage', ctypes.c_size_t), ('PrivateUsage', ctypes.c_size_t)]
    h = K32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not h: return None
    try:
        m = PMC(); m.cb = ctypes.sizeof(m)
        if not ctypes.windll.psapi.GetProcessMemoryInfo(h, ctypes.byref(m), m.cb): return None
        return {'ws_mb': m.WorkingSetSize // 2**20, 'private_mb': m.PrivateUsage // 2**20}
    finally:
        K32.CloseHandle(h)
