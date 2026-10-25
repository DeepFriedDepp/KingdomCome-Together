# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-207: a player's keys, sent to ONE of the rig's own copies in front (test tool, never shipped).

The maintainer allows WO-207's unattended runs to drive the keyboard and mouse. A joined conversation is played the way players
play it: the starter picks with his own dialogue keys, the listener moves and votes with his. Built on tools/dev-input/game_input.py
(its policy holds: send only, every event logged to its keys.log, the foreground checked before EVERY event); this adds only that
the one game process is the rig's record (pid, creation time, image), never "any KingdomCome.exe", and that its window is brought to
the front first (tools/rig/front.py). Nothing here reads input.

The game's default bindings (its keybindSuperactions.xml): the dialogue's up / down = W / S, confirm = E, back = Esc.
"""
import os, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..', 'dev-input'))
import front, procs
import game_input

ACTION_KEY = {'up': 'w', 'down': 's', 'confirm': 'e', 'back': 'esc', 'skip': 'space'}


class Keys:
    """One copy's keys. Every burst starts with that copy's window in front; a refusal (another window in front) stops the burst."""

    def __init__(self, rec, test, log=print):
        self.rec, self.log = rec, log
        self.g = game_input.Game(test)
        self.g.pids = {rec['pid']}   # the rig's copy only
        self.sent = 0

    def front(self):
        return front.front(self.rec, procs.matches, log=self.log, settle_s=0.6)

    def press(self, action, n=1, gap=0.3, hold_ms=80):
        """The dialogue action <n> times (up, down, confirm, back, skip). True when every press was sent."""
        if not self.front(): return False
        try:
            for i in range(n):
                self.g.tap(ACTION_KEY[action], hold_ms)
                self.sent += 1
                if i + 1 < n: time.sleep(gap)
        except game_input.NotFocused as ex:
            self.log('keys: %s refused (%s)' % (action, ex)); return False
        return True

    def release(self):
        try: self.g.release_all()
        except Exception: pass
