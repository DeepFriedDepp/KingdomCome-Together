# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-207 0.1: the rig answers the second copy's own "start another instance?" box (test tool, never shipped).

A second copy started with -multi_inst stops at the game's own Yes/No message box (WO-206 A.4: the engine's instance check
only ALLOWS a second copy, through the box). For the ONE process the rig just started -- its record (pid, creation time,
image) -- this lists that process's top-level windows, matches the box by its class (the system dialog class) and its owner
process, finds the Yes button among the dialog's own children by its control id, and sends it BM_CLICK. If the box is still
there after the click, the dialog is sent the command the button itself would send (WM_COMMAND IDYES, BN_CLICKED). Never by
title alone.

The keylogger rule: nothing here reads input. It only sends (BM_CLICK, WM_COMMAND) to a window of the rig's own process; the
class and text of a window are read only after its owner pid matched the record (another program's windows are never read);
everything found and sent is logged. Nothing takes focus or moves a window.

    python tools/rig/dialogs.py <pid> [timeout s]      (the record is made from the pid; a test aid -- pair.py calls answer())
"""
import ctypes, ctypes.wintypes as W, sys, threading, time

U = ctypes.windll.user32
DIALOG_CLASS = '#32770'
IDYES, IDNO = 6, 7
BM_CLICK = 0x00F5
WM_COMMAND = 0x0111
BN_CLICKED = 0
SMTO_ABORTIFHUNG = 0x0002
ENUM = ctypes.WINFUNCTYPE(W.BOOL, W.HWND, W.LPARAM)
U.GetDlgCtrlID.restype = ctypes.c_int
U.SendMessageTimeoutW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM, W.UINT, W.UINT, ctypes.POINTER(ctypes.c_size_t)]
U.PostMessageW.argtypes = [W.HWND, W.UINT, W.WPARAM, W.LPARAM]


def owner_pid(h):
    p = W.DWORD()
    U.GetWindowThreadProcessId(h, ctypes.byref(p))
    return p.value


def class_of(h):
    buf = ctypes.create_unicode_buffer(64)
    U.GetClassNameW(h, buf, 64)
    return buf.value


def text_of(h):
    buf = ctypes.create_unicode_buffer(512)
    U.GetWindowTextW(h, buf, 512)
    return buf.value


def children(h):
    """(hwnd, control id, class, text) of a dialog's own child windows (the dialog is the rig's process's: checked by the caller)."""
    out = []
    U.EnumChildWindows(h, ENUM(lambda c, _l: out.append((c, U.GetDlgCtrlID(c), class_of(c), text_of(c))) or True), 0)
    return out


def match_box(owner, cls, kids):
    """The decision, pure (tested): a top-level window of the copy's own process, of the system dialog class, with a Yes AND a No
    button among its own children. Returns the Yes button's hwnd or None. The title is never part of the match."""
    if not owner or cls != DIALOG_CLASS: return None
    yes = [h for h, cid, kc, _t in kids if cid == IDYES and kc.lower() == 'button']
    no = [h for h, cid, kc, _t in kids if cid == IDNO and kc.lower() == 'button']
    return yes[0] if yes and no else None


def find_boxes(pid):
    """[(dialog hwnd, yes hwnd, children)] among the top-level windows owned by pid. Another process's window: only its owner pid
    is asked, nothing of it is read."""
    tops = []
    U.EnumWindows(ENUM(lambda h, _l: tops.append(h) or True), 0)
    out = []
    for h in tops:
        if owner_pid(h) != pid: continue
        cls = class_of(h)
        if cls != DIALOG_CLASS: continue
        kids = children(h)
        yes = match_box(True, cls, kids)
        if yes: out.append((h, yes, kids))
    return out


def click_yes(dlg, yes):
    """BM_CLICK to the Yes button; if the box is still up 3 s later, the dialog gets the button's own command. Returns what was sent."""
    res = ctypes.c_size_t()
    sent = ['BM_CLICK to the Yes button 0x%x (%s)' % (yes, 'answered' if U.SendMessageTimeoutW(yes, BM_CLICK, 0, 0, SMTO_ABORTIFHUNG, 2000, ctypes.byref(res)) else 'no answer in 2 s')]
    t0 = time.time()
    while time.time() - t0 < 3 and U.IsWindow(dlg): time.sleep(0.1)
    if U.IsWindow(dlg):
        ok = U.PostMessageW(dlg, WM_COMMAND, (BN_CLICKED << 16) | IDYES, yes)
        sent.append('WM_COMMAND IDYES/BN_CLICKED posted to the dialog 0x%x (%s): the box was still up after the click' % (dlg, 'ok' if ok else 'FAILED'))
        t0 = time.time()
        while time.time() - t0 < 3 and U.IsWindow(dlg): time.sleep(0.1)
    return sent, not U.IsWindow(dlg)


def answer(rec, timeout=30.0, log=print, matches=None):
    """Watch the recorded process for its instance box; answer Yes once. Returns a dict of what was found and sent (found=False and
    nothing sent when no box came within `timeout`, or the process is no longer the recorded one)."""
    t0 = time.time()
    while time.time() - t0 < timeout:
        if matches is not None and not matches(rec):
            r = {'found': False, 'why': 'the recorded process is gone or not ours', 'secs': round(time.time() - t0, 1)}
            log('instance box: %s' % r['why']); return r
        boxes = find_boxes(rec['pid'])
        if boxes:
            dlg, yes, kids = boxes[0]
            text = ' '.join(' / '.join(t for _h, _c, kc, t in kids if t and kc.lower() == 'static').split())[:200]
            sent, gone = click_yes(dlg, yes)
            r = {'found': True, 'pid': rec['pid'], 'dialog': '0x%x' % dlg, 'yes': '0x%x' % yes, 'text': text, 'sent': sent,
                 'closed': gone, 'secs': round(time.time() - t0, 1)}
            log('instance box of pid %d found after %.1f s (%s): %s -> %s' % (rec['pid'], r['secs'], text[:80], '; '.join(sent),
                                                                               'closed' if gone else 'STILL OPEN'))
            return r
        time.sleep(0.25)
    r = {'found': False, 'why': 'no box within %.0f s' % timeout, 'secs': timeout}
    log('instance box: none within %.0f s (carrying on)' % timeout)
    return r


def answer_in_background(rec, timeout=30.0, log=print, matches=None):
    """answer() on a thread; the result lands in the returned dict under 'result' when done."""
    holder = {}
    th = threading.Thread(target=lambda: holder.__setitem__('result', answer(rec, timeout, log, matches)), daemon=True)
    th.start()
    holder['thread'] = th
    return holder


if __name__ == '__main__':
    import os
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import procs
    rec = procs.record(int(sys.argv[1]), 'manual')
    if not rec: sys.exit('no such process')
    r = answer(rec, float(sys.argv[2]) if len(sys.argv) > 2 else 30.0, matches=procs.matches)
    sys.exit(0 if r.get('found') and r.get('closed') else 1)
