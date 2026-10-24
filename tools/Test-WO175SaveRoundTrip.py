#!/usr/bin/env python3
# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-175 S5: the save round-trip gate, live part. Every future WO touching inventory, equipment, sleep, looting,
pickpocketing or fights keeps it green (the synthetic part is Test-WO175Synthetic.ps1, the agent's Wo175 tests and
tools/savegame/test_whs_repair.py, all in Build-Installer.ps1).

Live: a running Modding Tools game (REST on localhost:1403) with a THROWAWAY save loaded from playline4 (anything else is
refused: never a player's save, never a playline above 4). Steps: the draw test (holster, Human.DrawWeapon, read 2.5 s
later) -> Game.SaveGameViaResting -> the new file has no stuck quest fight (tools/savegame/whs_repair.py check) ->
reload it -> the draw test again. Without a game it prints SKIPPED with the reason and exits 3 (WO-179: the release build fails on a skip unless waived) -- never silently.
The player's own draw key (weapon slot 1) is tested by hand or with tools/dev-input (never shipped).

    python tools/Test-WO175SaveRoundTrip.py [--action "<lua run before the save>"]
"""
import os
import http.client
import re
import subprocess
import sys
import time
import urllib.parse
import urllib.request

API = os.environ.get('KCD2MP_API', 'http://localhost:1403')
GAME = os.environ.get('KCD2MP_INSTALL', r'D:\SteamLibrary\steamapps\common\KCD2Mod')
# KCD2MP_SAVES: the saves folder of the game that answers (the WO-206 bench's copy runs with its own user folder); default the game's own
SAVES = os.environ.get('KCD2MP_SAVES') or os.path.join(os.environ.get('USERPROFILE', ''), 'Saved Games', 'kingdomcome2', 'saves')
HERE = os.path.dirname(os.path.abspath(__file__))


def up():
    try:
        urllib.request.urlopen(API + '/api/rpg/SoulList/SoulCount', timeout=3).read(64)
        return True
    except Exception:
        return False


def lua(code, starts=None):
    """One console line. starts = what the line starts (a save, a load): the game may cut that request while it saves or loads
    (WO-184: a ConnectionReset on Game.SaveGameViaResting() while the save was written and healthy) -- then the outcome is read from
    the game's own log by the caller, as always; any other request must answer."""
    try:
        return urllib.request.urlopen(API + '/api/System/Console/ExecuteString?command=' + urllib.parse.quote('#' + code, safe=''), timeout=20).read(4096)
    except (OSError, http.client.HTTPException) as e:
        if starts is None:
            raise
        print('  (the game cut the request that starts %s: %s -- its outcome is read from the game log)' % (starts, type(e).__name__))
        return b''


def klog():
    with open(os.path.join(GAME, 'kcd.log'), 'rb') as f:
        return f.read().decode('utf-8', 'replace').splitlines()


def wait(rx, after, timeout):
    r = re.compile(rx)
    t0 = time.time()
    while time.time() - t0 < timeout:
        for l in klog()[after:]:
            m = r.search(l)
            if m:
                return m
        time.sleep(1)
    return None


DRAW = ('local h=player.human; pcall(function() if h:IsWeaponDrawn() then h:HolsterWeapon() end end); '
        'Script.SetTimer(2000,function() pcall(function() h:DrawWeapon() end); Script.SetTimer(2500,function() '
        'local d=false; pcall(function() d=h:IsWeaponDrawn()==true end); System.LogAlways("[WO175-RT] draw "..(d and "DRAWN" or "REFUSED")); '
        'if d then pcall(function() h:HolsterWeapon() end) end end) end)')


def draw():
    n = len(klog())
    lua(DRAW)
    m = wait(r'\[WO175-RT\] draw (\w+)', n, 30)
    return m.group(1) if m else 'NO-ANSWER'


def main(argv):
    if not up():
        print('WO175 save round-trip (live): SKIPPED -- no Modding Tools game answers on %s (start one with a throwaway playline4 save loaded to run it)' % API)
        return 3   # WO-179 0.2: a skip is not a pass -- the release build fails on it unless waived on the record
    loads = [l for l in klog() if l.startswith("Loading saved game '")]
    m = re.search(r"saves/(playline\d)/([^']+)\.whs", loads[-1]) if loads else None
    if not m or m.group(1) != 'playline4':
        print('WO175 save round-trip (live): REFUSED -- the loaded save is not a throwaway in playline4 (%s)' % (m.group(0) if m else 'none loaded'))
        return 1
    fails = []
    d0 = draw()
    print('  draw before: ' + d0)
    if '--action' in argv:
        lua(argv[argv.index('--action') + 1])
        time.sleep(3)
    n = len(klog())
    lua('Game.SaveGameViaResting()', starts='the save')
    s = wait(r"SaveGame: '%USER%/saves/(playline4)/([^']+)\.whs' done", n, 60)
    if not s:
        print('WO175 save round-trip (live): FAIL -- no save was written')
        return 1
    time.sleep(3)
    path = os.path.join(SAVES, s.group(1), s.group(2) + '.whs')
    chk = subprocess.run([sys.executable, os.path.join(HERE, 'savegame', 'whs_repair.py'), 'check', path], capture_output=True, text=True)
    print('  health of %s/%s: %s' % (s.group(1), s.group(2), chk.stdout.strip()[:200]))
    if chk.returncode != 0:
        fails.append('the new save has a stuck quest fight or does not read')
    n = len(klog())
    lua('System.ExecuteCommand("wh_sys_LoadGame 4 %s")' % s.group(2), starts='the reload')
    if not wait(r'\[CryAction\] LoadGame|LOAD GAME\] LoadGame', n, 120):
        fails.append('the reload did not start')
    time.sleep(25)
    d1 = draw()
    print('  draw after save + reload: ' + d1)
    if d0 == 'DRAWN' and d1 != 'DRAWN':
        fails.append('the weapons drew before the save and not after the reload')
    print('WO175 save round-trip (live): ' + ('PASS' if not fails else 'FAIL -- ' + '; '.join(fails)))
    return 1 if fails else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
