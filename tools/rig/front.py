# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-207: one of the rig's own copies brought to the front, the way a player's game is in front (test tool, never shipped).

The maintainer allows WO-207's unattended runs to drive the keyboard and mouse (the joiner's swings in c1fight, a copy in
front for the queue and clock measures). Only a window owned by a process the rig itself started (its record: pid, creation
time, image) is ever brought to the front; another program's window is never read, moved or focused. Which window is in front
is asked by its owner pid only. Nothing here reads input. Everything done is logged.
"""
import ctypes, ctypes.wintypes as W, time

U = ctypes.windll.user32
ENUM = ctypes.WINFUNCTYPE(W.BOOL, W.HWND, W.LPARAM)


def owner_pid(h):
    p = W.DWORD()
    U.GetWindowThreadProcessId(h, ctypes.byref(p))
    return p.value


def main_window(pid):
    """The visible, unowned top-level window of the process (its game window), or None."""
    found = []
    U.EnumWindows(ENUM(lambda h, _l: (found.append(h) if owner_pid(h) == pid and U.IsWindowVisible(h) and not U.GetWindow(h, 4) else None) or True), 0)
    return found[0] if found else None


def in_front(pid):
    return owner_pid(U.GetForegroundWindow()) == pid


def front(rec, matches, log=print, settle_s=1.5):
    """Bring the recorded copy's game window to the front. True when it is in front afterwards."""
    if not matches(rec): log('front: the recorded process is gone or not ours -- nothing done'); return False
    h = main_window(rec['pid'])
    if not h: log('front: no game window for pid %d' % rec['pid']); return False
    if in_front(rec['pid']): return True
    U.ShowWindow(h, 9)   # SW_RESTORE
    U.SetForegroundWindow(h)
    time.sleep(0.4)
    for _ in range(3):   # the window manager may refuse a background process's request: the game's own window is switched to
        if in_front(rec['pid']): break
        U.SwitchToThisWindow(h, True)
        time.sleep(0.6)
    ok = in_front(rec['pid'])
    log('front: copy pid %d %s' % (rec['pid'], 'in front' if ok else 'NOT in front (refused by the window manager)'))
    time.sleep(settle_s)
    return ok
