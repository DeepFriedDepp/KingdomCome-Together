# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""Read the game's thread-local stat-evaluation stack on its main thread (WO-148 s7.3). Read-only.

  python statstack.py [pid|auto] [tid|auto]         -> one line: depth=N ...

RPGModule's soul stat getter pushes the stat id onto a thread_local std::vector<uint32> (TLS block
+0x1e0), records a dependency for every id already on it, reads the stat, then pops. Between frames
the stack is empty. 0.42.5-0.42.7 leaked an entry at every faulting stamina read (WO-148 s7.4): the
depth grew into the thousands and every stat read in the game slowed with it.

The TLS slot and the block offset are the Modding Tools 1.5.5 RPGModule's. The reader checks that
build by its PE header (TimeDateStamp and SizeOfImage) and refuses any other: on another build the
same numbers would read something else. As a module: depth(pid, tid) -> (depth, ids) or raises.
"""
import ctypes, ctypes.wintypes as W, sys, collections, struct

# The Modding Tools 1.5.5 RPGModule (KCD2Mod, ReleaseSteamLTO_DLL): its identity and the two offsets.
KNOWN_BUILDS = {
    # (TimeDateStamp, SizeOfImage) of the Modding Tools 1.5.5 RPGModule.dll (read live 2026-10-02, WO-151)
    (0x69E0A38F, 0x128B000),
}
TLS_INDEX_RVA = 0x1136EAC
VECTOR_OFF = 0x1E0

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
psapi = ctypes.WinDLL('psapi', use_last_error=True)
ntdll = ctypes.WinDLL('ntdll')
k32.OpenProcess.restype = W.HANDLE
k32.OpenThread.restype = W.HANDLE
k32.ReadProcessMemory.argtypes = [W.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
psapi.EnumProcessModulesEx.argtypes = [W.HANDLE, ctypes.POINTER(W.HMODULE), W.DWORD, ctypes.POINTER(W.DWORD), W.DWORD]
psapi.GetModuleBaseNameW.argtypes = [W.HANDLE, W.HMODULE, W.LPWSTR, W.DWORD]


class TBI(ctypes.Structure):
    _fields_ = [('ExitStatus', ctypes.c_long), ('TebBaseAddress', ctypes.c_void_p), ('UniqueProcess', ctypes.c_void_p),
                ('UniqueThread', ctypes.c_void_p), ('AffinityMask', ctypes.c_size_t), ('Priority', ctypes.c_long),
                ('BasePriority', ctypes.c_long)]


def game_pid():
    import subprocess, csv, io
    out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq KingdomCome.exe', '/FO', 'CSV', '/NH'], capture_output=True, text=True).stdout
    rows = [r for r in csv.reader(io.StringIO(out)) if len(r) > 1 and r[0].lower() == 'kingdomcome.exe']
    if not rows:
        raise RuntimeError('no KingdomCome.exe running')
    return int(rows[0][1])


def main_thread(pid):
    """The process's first thread (the earliest creation time): CryEngine's frame loop runs on it."""
    TH32CS_SNAPTHREAD = 0x4

    class TE32(ctypes.Structure):
        _fields_ = [('dwSize', W.DWORD), ('cntUsage', W.DWORD), ('th32ThreadID', W.DWORD), ('th32OwnerProcessID', W.DWORD),
                    ('tpBasePri', ctypes.c_long), ('tpDeltaPri', ctypes.c_long), ('dwFlags', W.DWORD)]
    k32.CreateToolhelp32Snapshot.restype = W.HANDLE
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0)
    te = TE32(); te.dwSize = ctypes.sizeof(TE32)
    best = None
    ok = k32.Thread32First(snap, ctypes.byref(te))
    while ok:
        if te.th32OwnerProcessID == pid:
            h = k32.OpenThread(0x0800, False, te.th32ThreadID)   # THREAD_QUERY_LIMITED_INFORMATION
            if h:
                c, e, kt, ut = W.FILETIME(), W.FILETIME(), W.FILETIME(), W.FILETIME()
                if k32.GetThreadTimes(h, ctypes.byref(c), ctypes.byref(e), ctypes.byref(kt), ctypes.byref(ut)):
                    t = (c.dwHighDateTime << 32) | c.dwLowDateTime
                    if best is None or t < best[0]:
                        best = (t, te.th32ThreadID)
                k32.CloseHandle(h)
        ok = k32.Thread32Next(snap, ctypes.byref(te))
    k32.CloseHandle(snap)
    if not best:
        raise RuntimeError('no thread found for pid %d' % pid)
    return best[1]


class Reader:
    def __init__(self, pid, tid):
        self.pid, self.tid = pid, tid
        self.hp = k32.OpenProcess(0x0400 | 0x0010, False, pid)
        self.ht = k32.OpenThread(0x0040, False, tid)
        if not self.hp or not self.ht:
            raise RuntimeError('cannot open pid %d / tid %d' % (pid, tid))
        self.rpg = self._module('rpgmodule.dll')
        if not self.rpg:
            raise RuntimeError('RPGModule.dll not loaded')
        stamp, size = self._pe_identity(self.rpg)
        self.identity = (stamp, size)
        if (stamp, size) not in KNOWN_BUILDS:
            raise RuntimeError('unknown RPGModule build (TimeDateStamp=%#x SizeOfImage=%#x): the stack reader\'s offsets '
                               'are the Modding Tools 1.5.5 build\'s; refusing' % (stamp, size))
        tbi = TBI()
        ntdll.NtQueryInformationThread(self.ht, 0, ctypes.byref(tbi), ctypes.sizeof(tbi), None)
        self.teb = tbi.TebBaseAddress

    def rpm(self, addr, size):
        b = ctypes.create_string_buffer(size); n = ctypes.c_size_t(0)
        k32.ReadProcessMemory(self.hp, ctypes.c_void_p(addr), b, size, ctypes.byref(n))
        return b.raw[:n.value]

    def u64(self, a):
        r = self.rpm(a, 8)
        return int.from_bytes(r, 'little') if len(r) == 8 else 0

    def u32(self, a):
        r = self.rpm(a, 4)
        return int.from_bytes(r, 'little') if len(r) == 4 else 0

    def _module(self, name):
        arr = (W.HMODULE * 2048)(); need = W.DWORD()
        psapi.EnumProcessModulesEx(self.hp, arr, ctypes.sizeof(arr), ctypes.byref(need), 3)
        for i in range(need.value // 8):
            nm = ctypes.create_unicode_buffer(260); psapi.GetModuleBaseNameW(self.hp, arr[i], nm, 260)
            if nm.value.lower() == name:
                return arr[i]
        return None

    def _pe_identity(self, base):
        e_lfanew = self.u32(base + 0x3C)
        stamp = self.u32(base + e_lfanew + 8)
        size = self.u32(base + e_lfanew + 0x18 + 0x38)
        return stamp, size

    def depth(self):
        tls_index = self.u32(self.rpg + TLS_INDEX_RVA)
        block = self.u64(self.u64(self.teb + 0x58) + 8 * tls_index)
        begin, end = self.u64(block + VECTOR_OFF), self.u64(block + VECTOR_OFF + 8)
        if not begin or end < begin:
            return 0, []
        n = (end - begin) // 4
        raw = self.rpm(begin, min(n, 20000) * 4)
        ids = [int.from_bytes(raw[i:i + 4], 'little') for i in range(0, len(raw), 4)]
        return n, ids


if __name__ == '__main__':
    pid = game_pid() if len(sys.argv) < 2 or sys.argv[1] == 'auto' else int(sys.argv[1])
    tid = main_thread(pid) if len(sys.argv) < 3 or sys.argv[2] == 'auto' else int(sys.argv[2])
    r = Reader(pid, tid)
    n, ids = r.depth()
    top = collections.Counter(ids).most_common(4)
    print('pid=%d tid=%d rpgmodule=%#x/%#x depth=%d top=%s' % (pid, tid, r.identity[0], r.identity[1], n,
                                                               ', '.join('%#x:%d' % kv for kv in top) or '-'))
