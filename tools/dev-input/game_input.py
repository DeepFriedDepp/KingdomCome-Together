#!/usr/bin/env python3
# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-176 DEV-ONLY: real keys AND mouse (attack = left button, block = right button, the attack/guard direction = mouse movement) sent to
the game, for tests of the player's own input path. A module (the WO-176 harness imports it) and a small CLI.

NEVER SHIPPED (tools/ is not in the installer payload; Build-Installer.ps1's no-input-tooling gate fails the build if anything like this
appears in a shipped file). Never called by the launcher, the agent, the DLL or a shipped script.

The maintainer's input policy (2026-10-11), enforced here -- every rule:
  * only the game's own window (a KingdomCome.exe process) is ever brought to the front; nothing else is touched (no other window is
    moved, focused, clicked or read; no clipboard; no OS setting; the game's own key bindings are never changed);
  * before EVERY input event the foreground window must belong to that process -- otherwise nothing is sent, all input stops (an
    exception ends the caller) and the refusal is logged;
  * send only: no hooks, no key-state polling, no raw-input registration, nothing of the user's input is read;
  * every event sent is logged (what, when, which test) to tools/dev-input/keys.log (git-ignored).

    python tools/dev-input/game_input.py <test> key <name> [hold_ms] [--front] [--back]
    python tools/dev-input/game_input.py <test> click left|right [hold_ms] [--front] [--back]
    python tools/dev-input/game_input.py <test> move <dx> <dy> [--front] [--back]
"""
import ctypes
import ctypes.wintypes as wt
import datetime
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
LOG = os.path.join(HERE, 'keys.log')
user32 = ctypes.WinDLL('user32', use_last_error=True)

# DirectInput games read scan codes: KEYEVENTF_SCANCODE.
SCAN = {'1': 0x02, '2': 0x03, '3': 0x04, '4': 0x05, 'b': 0x30, 'r': 0x13, 'f': 0x21, 'e': 0x12, 'esc': 0x01, 'tab': 0x0F, 'i': 0x17,
        'w': 0x11, 's': 0x1F, 'a': 0x1E, 'd': 0x20, 'space': 0x39, 'enter': 0x1C, 'q': 0x10, 'h': 0x23,
        'home': 0x47, 'delete': 0x53, 'insert': 0x52, 'end': 0x4F, 'pgup': 0x49, 'pgdn': 0x51}
EXTENDED = {'home', 'delete', 'insert', 'end', 'pgup', 'pgdn'}   # WO-178: the navigation keys are extended scan codes
KEYEVENTF_EXTENDEDKEY = 0x0001
KEYEVENTF_SCANCODE, KEYEVENTF_KEYUP, INPUT_MOUSE, INPUT_KEYBOARD = 0x0008, 0x0002, 0, 1
MOUSEEVENTF_MOVE, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP = 0x0001, 0x0002, 0x0004, 0x0008, 0x0010


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [('wVk', wt.WORD), ('wScan', wt.WORD), ('dwFlags', wt.DWORD), ('time', wt.DWORD), ('dwExtraInfo', ctypes.c_size_t)]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [('dx', wt.LONG), ('dy', wt.LONG), ('mouseData', wt.DWORD), ('dwFlags', wt.DWORD), ('time', wt.DWORD), ('dwExtraInfo', ctypes.c_size_t)]


class _U(ctypes.Union):
    _fields_ = [('ki', KEYBDINPUT), ('mi', MOUSEINPUT), ('pad', ctypes.c_byte * 32)]


class INPUT(ctypes.Structure):
    _fields_ = [('type', wt.DWORD), ('u', _U)]


class NotFocused(Exception):
    pass


def log(test, what):
    with open(LOG, 'a', encoding='utf-8') as f:
        f.write('%s test=%s %s\n' % (datetime.datetime.now().isoformat(timespec='milliseconds'), test, what))


def game_pids():
    out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq KingdomCome.exe', '/FO', 'CSV', '/NH'], capture_output=True, text=True).stdout
    return {int(l.split('","')[1]) for l in out.splitlines() if l.startswith('"KingdomCome.exe"')}


def game_window(pids):
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, _):
        pid = wt.DWORD()
        user32.GetWindowThreadProcessId(h, ctypes.byref(pid))
        if pid.value in pids and user32.IsWindowVisible(h) and user32.GetWindow(h, 4) == 0:   # GW_OWNER: a top-level window
            found.append(h)
        return True
    user32.EnumWindows(cb, 0)
    return found[0] if found else None


def foreground_is_game(pids):
    h = user32.GetForegroundWindow()
    pid = wt.DWORD()
    user32.GetWindowThreadProcessId(h, ctypes.byref(pid))
    return pid.value in pids


class Game:
    """One test's input session: the game's pids found once; every event checks the foreground first."""

    def __init__(self, test):
        self.test = test
        self.pids = game_pids()
        if not self.pids:
            raise SystemExit('the game is not running')
        self.sent = 0

    def front(self, settle_s=3.0):
        h = game_window(self.pids)
        if not h:
            raise SystemExit('no game window')
        user32.ShowWindow(h, 9)   # SW_RESTORE
        user32.SetForegroundWindow(h)
        log(self.test, 'front: the game window brought to the front')
        time.sleep(0.5)
        # WO-178: Windows may refuse a background process's SetForegroundWindow (the desktop app keeps the foreground); the game's own
        # window is then switched to (it alone is touched -- still no input event before the foreground check)
        for _ in range(3):
            if foreground_is_game(self.pids): break
            user32.SwitchToThisWindow(h, True)
            log(self.test, 'front: switched to the game window (the first request was refused by the window manager)')
            time.sleep(0.7)
        if foreground_is_game(self.pids):
            self.aim()
        time.sleep(settle_s)      # the game takes a moment to accept input after it regains focus

    def back(self):
        h = game_window(self.pids)
        if h:
            user32.ShowWindow(h, 6)   # SW_MINIMIZE
            log(self.test, 'back: the game window minimised')

    def _check(self, what):
        if not foreground_is_game(self.pids):
            fpid = wt.DWORD()
            user32.GetWindowThreadProcessId(user32.GetForegroundWindow(), ctypes.byref(fpid))   # the id only: nothing of that window is read
            log(self.test, 'REFUSED %s: the foreground window is not the game (foreground pid %d) -- all input stopped' % (what, fpid.value))
            raise NotFocused('foreground is not the game: stopped (%s)' % what)

    def _send(self, inp, what):
        self._check(what)
        if user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT)) != 1:
            log(self.test, 'FAILED %s err=%d' % (what, ctypes.get_last_error()))
            raise SystemExit('SendInput failed')
        self.sent += 1
        log(self.test, what)

    def key(self, name, down):
        i = INPUT(type=INPUT_KEYBOARD)
        ext = KEYEVENTF_EXTENDEDKEY if name in EXTENDED else 0
        i.u.ki = KEYBDINPUT(0, SCAN[name], KEYEVENTF_SCANCODE | ext | (0 if down else KEYEVENTF_KEYUP), 0, 0)
        self._send(i, 'key=%s %s' % (name, 'down' if down else 'up'))

    def tap(self, name, hold_ms=80):
        self.key(name, True)
        time.sleep(hold_ms / 1000.0)
        self.key(name, False)

    def aim(self):
        """The cursor onto the centre of the game's own window (its rect is the only thing read): a button event can then only reach
        the game, never a window that happens to lie under the cursor (22:19:36 -- a click's down took the focus to another window)."""
        h = game_window(self.pids)
        r = wt.RECT()
        if not h or not user32.GetWindowRect(h, ctypes.byref(r)):
            log(self.test, 'REFUSED aim: no game window rect -- all input stopped')
            raise NotFocused('no game window rect')
        cx, cy = (r.left + r.right) // 2, (r.top + r.bottom) // 2
        user32.SetCursorPos(cx, cy)
        # the window under that point must be the game's own (a dialog of anything else on top = no input at all)
        user32.WindowFromPoint.restype = wt.HWND
        under = user32.WindowFromPoint(wt.POINT(cx, cy))
        upid = wt.DWORD()
        user32.GetWindowThreadProcessId(under, ctypes.byref(upid))
        if upid.value not in self.pids:
            log(self.test, 'REFUSED aim: the window under the game window centre is not the game (pid %d) -- all input stopped' % upid.value)
            raise NotFocused('not the game under the cursor')

    def button(self, which, down):
        flag = {('left', True): MOUSEEVENTF_LEFTDOWN, ('left', False): MOUSEEVENTF_LEFTUP,
                ('right', True): MOUSEEVENTF_RIGHTDOWN, ('right', False): MOUSEEVENTF_RIGHTUP}[(which, down)]
        self._check('aim before mouse=%s %s' % (which, 'down' if down else 'up'))
        self.aim()
        i = INPUT(type=INPUT_MOUSE)
        i.u.mi = MOUSEINPUT(0, 0, 0, flag, 0, 0)
        self._send(i, 'mouse=%s %s' % (which, 'down' if down else 'up'))

    def click(self, which, hold_ms=80):
        self.button(which, True)
        time.sleep(hold_ms / 1000.0)
        self.button(which, False)

    def move(self, dx, dy):
        i = INPUT(type=INPUT_MOUSE)
        i.u.mi = MOUSEINPUT(int(dx), int(dy), 0, MOUSEEVENTF_MOVE, 0, 0)
        self._send(i, 'mouse=move %d %d' % (dx, dy))

    def release_all(self):
        """Best effort at the end of a test: buttons up (only while the game is in front -- never into another window)."""
        if not foreground_is_game(self.pids):
            log(self.test, 'release_all skipped: the game is not in front (nothing sent)')
            return
        for w in ('left', 'right'):
            try:
                self.button(w, False)
            except NotFocused:
                return


def main(argv):
    args = [a for a in argv if not a.startswith('--')]
    test, verb = args[0], args[1]
    g = Game(test)
    if '--front' in argv:
        g.front()
    try:
        if verb == 'key':
            g.tap(args[2].lower(), int(args[3]) if len(args) > 3 else 80)
        elif verb == 'click':
            g.click(args[2], int(args[3]) if len(args) > 3 else 80)
        elif verb == 'move':
            g.move(int(args[2]), int(args[3]))
        else:
            raise SystemExit('unknown verb ' + verb)
    finally:
        if '--back' in argv:
            g.back()
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
