# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 Part 0.2 / A.3: what this machine has, from the OS and DXGI only (test tool, never shipped).

Reads: physical memory (GlobalMemoryStatusEx), every DXGI adapter's dedicated video memory and the OS's budget and
current usage for it (IDXGIAdapter3::QueryVideoMemoryInfo, for this process: on a process that holds no GPU memory the
budget is what the OS would grant now), the system-wide GPU memory counters (the "GPU Adapter Memory" performance
counters, as typeperf prints them), free disk on a volume, and the monitor count. Nothing here reads input of any kind.

    python tools/rig/machine.py [--disk D:\\] [--json]
"""
import ctypes, ctypes.wintypes as W, json, shutil, subprocess, sys


class MEMORYSTATUSEX(ctypes.Structure):
    _fields_ = [('dwLength', W.DWORD), ('dwMemoryLoad', W.DWORD), ('ullTotalPhys', ctypes.c_ulonglong),
                ('ullAvailPhys', ctypes.c_ulonglong), ('ullTotalPageFile', ctypes.c_ulonglong),
                ('ullAvailPageFile', ctypes.c_ulonglong), ('ullTotalVirtual', ctypes.c_ulonglong),
                ('ullAvailVirtual', ctypes.c_ulonglong), ('ullAvailExtendedVirtual', ctypes.c_ulonglong)]


def memory():
    m = MEMORYSTATUSEX(); m.dwLength = ctypes.sizeof(m)
    ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
    return {'ram_total_mb': m.ullTotalPhys // 2**20, 'ram_free_mb': m.ullAvailPhys // 2**20,
            'commit_free_mb': m.ullAvailPageFile // 2**20}


class GUID(ctypes.Structure):
    _fields_ = [('a', ctypes.c_uint32), ('b', ctypes.c_uint16), ('c', ctypes.c_uint16), ('d', ctypes.c_ubyte * 8)]

    @staticmethod
    def of(s):
        s = s.strip('{}').replace('-', '')
        g = GUID(int(s[0:8], 16), int(s[8:12], 16), int(s[12:16], 16))
        for i in range(8): g.d[i] = int(s[16 + 2 * i:18 + 2 * i], 16)
        return g


class DXGI_ADAPTER_DESC1(ctypes.Structure):
    _fields_ = [('Description', ctypes.c_wchar * 128), ('VendorId', ctypes.c_uint), ('DeviceId', ctypes.c_uint),
                ('SubSysId', ctypes.c_uint), ('Revision', ctypes.c_uint), ('DedicatedVideoMemory', ctypes.c_size_t),
                ('DedicatedSystemMemory', ctypes.c_size_t), ('SharedSystemMemory', ctypes.c_size_t),
                ('LuidLow', ctypes.c_uint32), ('LuidHigh', ctypes.c_int32), ('Flags', ctypes.c_uint)]


class DXGI_QUERY_VIDEO_MEMORY_INFO(ctypes.Structure):
    _fields_ = [('Budget', ctypes.c_uint64), ('CurrentUsage', ctypes.c_uint64),
                ('AvailableForReservation', ctypes.c_uint64), ('CurrentReservation', ctypes.c_uint64)]


IID_IDXGIFactory1 = GUID.of('770aae78-f26f-4dba-a829-253c83d1b387')
IID_IDXGIAdapter3 = GUID.of('645967a4-1392-4310-a798-8053ce3e93fd')


def _vcall(obj, index, restype, *argtypes):
    vtbl = ctypes.cast(obj, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    return ctypes.WINFUNCTYPE(restype, ctypes.c_void_p, *argtypes)(vtbl[index])


def adapters():
    """Every hardware adapter: name, dedicated MB, and (adapter 3) the local budget / usage this process sees."""
    out = []
    dxgi = ctypes.windll.dxgi
    fac = ctypes.c_void_p()
    if dxgi.CreateDXGIFactory1(ctypes.byref(IID_IDXGIFactory1), ctypes.byref(fac)) != 0: return out
    i = 0
    while True:
        ad = ctypes.c_void_p()
        # IDXGIFactory1::EnumAdapters1 = slot 12
        if _vcall(fac, 12, ctypes.c_long, ctypes.c_uint, ctypes.POINTER(ctypes.c_void_p))(fac, i, ctypes.byref(ad)) != 0: break
        d = DXGI_ADAPTER_DESC1()
        _vcall(ad, 10, ctypes.c_long, ctypes.POINTER(DXGI_ADAPTER_DESC1))(ad, ctypes.byref(d))   # GetDesc1
        row = {'index': i, 'name': d.Description, 'dedicated_mb': d.DedicatedVideoMemory // 2**20,
               'software': bool(d.Flags & 2), 'luid': '%08x_%08x' % (d.LuidHigh & 0xffffffff, d.LuidLow)}
        a3 = ctypes.c_void_p()
        if _vcall(ad, 0, ctypes.c_long, ctypes.POINTER(GUID), ctypes.POINTER(ctypes.c_void_p))(ad, ctypes.byref(IID_IDXGIAdapter3), ctypes.byref(a3)) == 0:
            info = DXGI_QUERY_VIDEO_MEMORY_INFO()
            # IDXGIAdapter3::QueryVideoMemoryInfo = slot 14 (node 0, segment group 0 = local)
            if _vcall(a3, 14, ctypes.c_long, ctypes.c_uint, ctypes.c_int, ctypes.POINTER(DXGI_QUERY_VIDEO_MEMORY_INFO))(a3, 0, 0, ctypes.byref(info)) == 0:
                row['os_budget_mb'] = info.Budget // 2**20
            _vcall(a3, 2, ctypes.c_ulong)(a3)
        _vcall(ad, 2, ctypes.c_ulong)(ad)
        if not row['software']: out.append(row)
        i += 1
    _vcall(fac, 2, ctypes.c_ulong)(fac)
    return out


def gpu_dedicated_usage_mb():
    """System-wide dedicated GPU memory in use, per adapter LUID, from the performance counters (one sample)."""
    try:
        r = subprocess.run(['typeperf', r'\GPU Adapter Memory(*)\Dedicated Usage', '-sc', '1'], capture_output=True,
                           text=True, timeout=30)
    except Exception:
        return {}
    lines = [l for l in r.stdout.splitlines() if l.startswith('"')]
    if len(lines) < 2: return {}
    head = [h.strip('"') for h in lines[0].split('","')]
    vals = [v.strip('"') for v in lines[1].split('","')]
    out = {}
    for h, v in zip(head[1:], vals[1:]):
        # "\\HOST\GPU Adapter Memory(luid_0x00000000_0x0000F0D3_phys_0)\Dedicated Usage"
        lo = h.find('luid_0x')
        if lo < 0: continue
        parts = h[lo:].split('_')
        try: luid = '%s_%s' % (parts[1][2:].lower(), parts[2][2:].lower())
        except IndexError: continue
        try: out[luid] = out.get(luid, 0) + float(v) / 2**20
        except ValueError: pass
    return {k: int(v) for k, v in out.items()}


def monitors():
    return ctypes.windll.user32.GetSystemMetrics(80)   # SM_CMONITORS


class MONITORINFO(ctypes.Structure):
    _fields_ = [('cbSize', W.DWORD), ('rcMonitor', W.RECT), ('rcWork', W.RECT), ('dwFlags', W.DWORD)]


def monitor_rects():
    """[(left, top, right, bottom, primary)] of every monitor (desktop layout only)."""
    out = []
    proc = ctypes.WINFUNCTYPE(W.BOOL, W.HMONITOR, W.HDC, ctypes.POINTER(W.RECT), W.LPARAM)

    def cb(h, _dc, _r, _l):
        mi = MONITORINFO(); mi.cbSize = ctypes.sizeof(mi)
        ctypes.windll.user32.GetMonitorInfoW(h, ctypes.byref(mi))
        r = mi.rcWork
        out.append((r.left, r.top, r.right, r.bottom, bool(mi.dwFlags & 1)))
        return True
    ctypes.windll.user32.EnumDisplayMonitors(None, None, proc(cb), 0)
    return out


def vram_free_mb():
    """(adapter name, dedicated, in use system-wide, free) for the adapter with the most dedicated memory."""
    ads = adapters()
    if not ads: return None
    a = max(ads, key=lambda x: x['dedicated_mb'])
    use = gpu_dedicated_usage_mb()
    used = use.get(a['luid'])
    free = a['dedicated_mb'] - used if used is not None else a.get('os_budget_mb')
    return {'adapter': a['name'], 'dedicated_mb': a['dedicated_mb'], 'used_mb': used, 'free_mb': free,
            'os_budget_mb': a.get('os_budget_mb')}


def record(disk='D:\\'):
    du = shutil.disk_usage(disk)
    return {'memory': memory(), 'adapters': adapters(), 'vram': vram_free_mb(), 'monitors': monitors(),
            'monitor_work_areas': monitor_rects(),
            'disk': {'volume': disk, 'free_gb': round(du.free / 2**30, 1), 'total_gb': round(du.total / 2**30, 1)}}


if __name__ == '__main__':
    disk = sys.argv[sys.argv.index('--disk') + 1] if '--disk' in sys.argv else 'D:\\'
    r = record(disk)
    print(json.dumps(r, indent=1))
