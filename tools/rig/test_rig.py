# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 A: the rig's offline checks (python tools/rig/test_rig.py; one RESULT line, exit 0/1).

A.2's synthetic half: two copies' plans never share a pipe, a port or a folder; the agent and DLL halves are
dotnet/KcdMp.Client.Tests/Wo206BenchTests.cs and native/tests/wo206_tests.cpp. Every check has a twin that must fail.
"""
import os, sys, tempfile, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mirror, pair, procs, rig

passed = failed = 0


def check(cond, what):
    global passed, failed
    if cond: passed += 1
    else: failed += 1; print('FAIL', what)


def disjoint(p, q):
    """The things two copies must never share."""
    keys_path = ('dir', 'agent_dir', 'dll_dir', 'data_dir', 'backup_dir', 'bundle_dir', 'root')
    clash = [k for k in ('pipe', 'dice_port', 'version_port', 'name') if p[k] == q[k]]
    clash += [k for k in keys_path if os.path.normcase(p[k]) == os.path.normcase(q[k])]
    ports = {p['dice_port'], p['version_port'], q['dice_port'], q['version_port']}
    clash += ['a port is the maintainer\'s'] if ports & {5901, 5902, 7778, 1403} else []
    clash += ['the relay port is the maintainer\'s'] if pair.RELAY_PORT == 7778 else []
    return clash


a, b = pair.plan('A'), pair.plan('B')
check(disjoint(a, b) == [], 'two copies share nothing: %s' % disjoint(a, b))
check(disjoint(a, dict(a)) != [], 'twin: a plan against itself is caught')
check(a['pipe'].endswith('kcdmp-A') and b['pipe'].endswith('kcdmp-B'), 'the pipes follow instance.h')
check(os.path.normcase(a['root']) != os.path.normcase(mirror.INSTALL) and os.path.normcase(b['root']) != os.path.normcase(mirror.INSTALL),
      'neither copy runs in the player install')

# system.cfg: the first root's text, the rig's block appended once, later lines win
base = 'r_TexturesStreamPoolSize = 8192\nsys_MaxFPS = 0\n'
cv = mirror.COPIES['B']['cvars']
cfg = mirror.copy_system_cfg(base, cv)
check(cfg.count(mirror.BEGIN) == 1 and mirror.copy_system_cfg(cfg, cv).count(mirror.BEGIN) == 1, 'the block is written once')
check(cfg.rfind('sys_user_folder = KingdomCome2_B') > cfg.find(mirror.BEGIN), 'its own user folder')
cfga = mirror.copy_system_cfg(base, mirror.COPIES['A']['cvars'])
check('sys_user_folder = KingdomCome2_A' in cfga and 'r_TexturesStreamPoolSize = 4096' in cfga, 'copy A: its own folder, a 4 GB pool')
check(mirror.COPIES['A']['user_folder'] != mirror.USER_FOLDER_REAL and mirror.COPIES['B']['user_folder'] != mirror.USER_FOLDER_REAL,
      'neither copy uses the player user folder')
check(cfg.rfind('r_TexturesStreamPoolSize = 2048') > cfg.rfind('r_TexturesStreamPoolSize = 8192'), 'the 2 GB pool comes last')
check('sys_spec_textureresolution = 4' in cfg, 'the texture level is pinned (A.4: the renderer picks 8192 otherwise)')
check(base.find('KingdomCome2_B') < 0, 'twin: the first root\'s text has no such line')

x = '<Attributes>\n\t<Attr name="res_fs" value="2" />\n\t<Attr name="sound_master" value="0.3" />\n</Attributes>'
y = mirror.copy_attributes(x, ('1280', '720'))
check('name="res_fs" value="0"' in y and 'name="sound_master" value="0"' in y and 'name="res_x" value="1280"' in y,
      'the profile: windowed, 1280x720, silent')
check('value="2"' in x, 'twin: the source is untouched')
check(set(mirror.user_folders('B')) == {'KingdomCome2_B', 'KingdomCome2_B(1)'}, 'the profile goes under both names')
check('name="sys_spec_Texture" value="1"' in y, 'the dynamic texture pool kept at 2 GB through a load (sys_spec_Texture 1)')

# the copy's own log (kcd(1).log for a second instance) and a log from before the launch is not "at the menu"
d = tempfile.mkdtemp()
old = os.path.join(d, 'kcd.log'); new = os.path.join(d, 'kcd(1).log')
open(old, 'w').write("Log Started at 2020-01-01  10:00:00\nPlayVideoOnly 'main_menu_x'\n")
os.utime(old, (time.time() - 600, time.time() - 600))
open(new, 'w').write("Log Started at %s\nUser folder is 'X(1)'\nStarting HTTP server on port '1404'\n" % time.strftime('%Y-%m-%d  %H:%M:%S'))
open(os.path.join(d, 'kcd_launcher.log'), 'w').write('x\n')
check(os.path.basename(rig.klog(d)) == 'kcd(1).log', 'the newest game log, never kcd_launcher.log')
w = procs.LogWatch(old, since=time.time()); w.poll()
check(not w.menu, 'a previous launch\'s log does not read as "at the menu"')
w2 = procs.LogWatch(old); w2.poll()
check(w2.menu, 'twin: without the start time it does')
w3 = procs.LogWatch(new, since=time.time() - 5); w3.poll()
check(w3.http_port == 1404 and w3.user_folder == 'X(1)', 'the port and user folder of this launch')

# the device-removed watch
w4 = procs.LogWatch(os.path.join(d, 'dev.log'))
open(os.path.join(d, 'dev.log'), 'w').write('[Error] D3D12: DXGI_ERROR_DEVICE_REMOVED (GetDeviceRemovedReason 0x887A0006)\nTDR detected\n')
w4.poll()
check(len(w4.device_lost) == 2, 'device-removed and TDR lines are caught')
w5 = procs.LogWatch(os.path.join(d, 'ok.log'))
open(os.path.join(d, 'ok.log'), 'w').write('Item pigMeatDried ... TDRx tdr_none\n')
w5.poll()
check(w5.device_lost == [], 'twin: ordinary lines are not')

# the throwaway rule: new files only in playline4
e, rest = rig.throwaway_split([os.path.join('playline4', 'autosave440.whs'), os.path.join('playline0', 'autosave1.whs')])
check(e == [] and len(rest) == 2, 'no throwaway exemption: every new file in the real saves folder is a change')

# the rig's processes: only a recorded (pid, creation time, image) is ever stopped
me = procs.record(os.getpid(), 'self')
check(procs.matches(me), 'this process matches its own record')
fake = dict(me); fake['created'] = me['created'] + 1
check(not procs.matches(fake) and procs.stop(fake) == 'not ours', 'twin: another creation time is never stopped')

# WO-207 0.1: the instance box is matched by class and owner, its Yes by control id -- never by title
import dialogs
kids = [(101, dialogs.IDYES, 'Button', '&Yes'), (102, dialogs.IDNO, 'Button', '&No'), (103, 0xFFFF, 'Static', 'start another instance ?')]
check(dialogs.match_box(True, '#32770', kids) == 101, 'the Yes button of the copy\'s own Yes/No dialog')
check(dialogs.match_box(False, '#32770', kids) is None, 'twin: another process\'s dialog is never answered')
check(dialogs.match_box(True, 'KingdomComeWnd', kids) is None, 'twin: a window of another class is never answered')
check(dialogs.match_box(True, '#32770', [(101, 1, 'Button', 'OK'), (103, 0xFFFF, 'Static', 'already running')]) is None,
      'twin: an OK-only box (the game refusing a second copy) is not a Yes/No box')
check(dialogs.match_box(True, '#32770', [(101, dialogs.IDYES, 'Button', 'Nein'), (102, dialogs.IDNO, 'Button', 'Ja')]) == 101,
      'the button is found by its control id, whatever its text')

# WO-207 0.2: a copy whose user folder resolves to the real saves folder is never started
sg = tempfile.mkdtemp()
os.makedirs(os.path.join(sg, 'KingdomCome2', 'saves', 'playline4'))
ref = lambda uf: pair.real_saves_refusal(uf, sg, 'KingdomCome2')
check(ref('KingdomCome2_B') is None and ref('KingdomCome2_A') is None, 'the copies\' own folders are allowed')
check(ref('KingdomCome2') is not None, 'twin: the real user folder is refused')
check(ref('kingdomcome2') is not None, 'twin: the real one in another case is refused')
check(ref(os.path.join('KingdomCome2', 'saves', 'playline4')) is not None, 'twin: a playline inside it is refused')
check(ref(os.path.join(sg, 'KingdomCome2')) is not None, 'twin: the real one as an absolute path is refused')
check(ref('KingdomCome2(1)') is not None, 'twin: the real one with a second instance\'s suffix is refused')
check(ref('') is not None and ref(None) is not None, 'twin: no folder (the game\'s default, the real one) is refused')
check(ref('.') is not None, 'twin: a folder that contains the real one is refused')
cfgd = tempfile.mkdtemp()
open(os.path.join(cfgd, 'system.cfg'), 'w').write('sys_user_folder = KingdomCome2\n-- x\nsys_user_folder = KingdomCome2_B\n')
check(pair.cfg_user_folder(cfgd) == 'KingdomCome2_B', 'the copy\'s system.cfg: the last line wins')

# WO-207: the main thread's stall after the first readiness read
import scenarios as _sc
_log = ['[16:42:28.951] t013704 WO206-PROBE cost',
        '[16:42:29.056] t013704 WO189-READY resolver: CSystem found, C_ReadinessService found',
        '[16:42:42.530] t013704 MP-NPCWRITE tick gap 13.5 s (a stall) -- no silence of the streams',
        '[16:42:42.556] t013704 WO138-WORLD FROZEN (game/real 0.015 over 13.68 s, 13 frames, held=0x0)',
        '[16:43:30.000] t013704 MP-NPCWRITE tick gap 40.0 s (a stall)']
check(_sc.w189_stall(_log) == 13.68, 'the stall after the first readiness read is measured (the worst within 20 s)')
check(_sc.w189_stall(_log[:2]) == 0.0, 'no stall after the read: 0')
check(_sc.w189_stall(_log[2:]) is None, 'no readiness read in the log: None')
check(_sc.w189_stall([_log[2], _log[1]]) == 0.0, 'a stall before the read is not the one it caused')

print('RESULT: %d passed, %d failed' % (passed, failed))
sys.exit(1 if failed else 0)
