# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 A.1: the copies' roots, mirrors of the Modding Tools install (test tool, never shipped).

Both copies run from a mirror root beside the install (the maintainer's call during A.5: the host's own world saves always
write into the playline it plays, so copy A in the install would write the real saves folder). The install itself is only
read: its big folders (Data, Localization, Engine, Bin) are hard links in each mirror (no space, never copied, never written:
a write through a link would change the install's file, so `verify` stats every linked file against the list made at
creation and fails on any change). Mods\\kdcmp (the rig's own build of the mod), system.cfg, the two small root files the
game reads, and every log are real per-copy files. The roots sit outside steamapps\\common on purpose: the agent's kcd.log
finder scans that folder for "KCD"/"Kingdom" names and takes the newest log.

The install's system.cfg is never edited. A copy's is the install's text plus an appended block that the later lines win
with (the engine applies a system.cfg top to bottom). Each copy has its own Saved Games user folder (KingdomCome2_A,
KingdomCome2_B; a second instance gets "(1)" appended by the engine) and its own profile seeded from the player's.

    python tools/rig/mirror.py create   [A|B|all]
    python tools/rig/mirror.py verify   [A|B|all]
    python tools/rig/mirror.py profile  [A|B|all]   seed the copies' own profiles (their user folders only)
"""
import json, os, re, shutil, sys

INSTALL = os.environ.get('KCD2MP_INSTALL', r'D:\SteamLibrary\steamapps\common\KCD2Mod')
LINKED = ('Data', 'Localization', 'Engine', 'Bin')
COPIED_FILES = ('steam_appid.txt', 'whdlversions.json')
SAVED_GAMES = os.path.join(os.environ.get('USERPROFILE', ''), 'Saved Games')
USER_FOLDER_REAL = 'KingdomCome2'   # the player's own: read (its profile is the copies' template), never written
LIST = 'kcdmp-rig-links.json'
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
RIG = os.environ.get('KCDMP_RIG_DIR', os.path.join(REPO, 'release', 'w206rig'))
# The mod a copy runs: the WO's backup of the install's Mods\kdcmp (manifest, keys pak, localization) with the rig's own
# freshly built kdcmp.pak (release\w206rig\deploy.ps1 puts it in run\mod).
MOD_TEMPLATE = os.path.join(RIG, 'mods-backup-kdcmp')
# WO-207: KCDMP_RIG_BUILD names a build folder of its own (dll\, agent\, relay\, logpack\, mod\kdcmp.pak): a run on another build
# while the rig's own run\ stays the one it was
BUILD = os.environ.get('KCDMP_RIG_BUILD') or os.path.join(RIG, 'run')
MOD_PAK = os.path.join(BUILD, 'mod', 'kdcmp.pak')


def _cvars(user_folder, tex_level, pool_mb, w, h, x, y):
    # Every name is a cvar the engine has (system.cfg's own, the renderer's, or the strings of the module that owns it:
    # wh_snd_muteall is CryAudioImplFmod's). The texture pool: r_TexturesStreamPoolSize alone does not hold -- at texture
    # init the renderer applies the sys_spec_TextureResolution group it picks from the card (12 GB -> level 8 = 8192 MB,
    # seen in A.4), so the level is pinned (4 = 2048 MB, 6 = 4096 MB in the engine's own group file); Warhorse's dynamic
    # pool is capped too (the profile's sys_spec_Texture 1 keeps it there when the groups are applied again at a load).
    return [
        ('sys_user_folder', user_folder),
        ('sys_spec_textureresolution', str(tex_level)),
        ('r_TexturesStreamPoolSize', str(pool_mb)),
        ('r_TexturesStreamDynamicPoolMaxSizeWH', '2048'),
        ('r_Fullscreen', '0'), ('r_FullscreenWindow', '0'), ('r_WindowType', '0'),
        ('r_Width', str(w)), ('r_Height', str(h)), ('r_WinPosX', str(x)), ('r_WinPosY', str(y)),
        ('wh_snd_muteall', '1'),
        # the game sleeps while its window is inactive (default 1): the copy behind ran at 20-34 fps and its agent's queue fell
        # 18-90 s behind -- every "host lag" of the bench's first night. 0: both copies at full rate, no focus taken (34 -> 173 fps).
        # WO-207 C.1: KCDMP_RIG_SLEEP_INACTIVE=1 runs the copies as a player's game runs (the engine's default 1: asleep behind)
        ('sys_SleepIfInactiveWH', os.environ.get('KCDMP_RIG_SLEEP_INACTIVE', '0')),
    ]


COPIES = {
    'A': {'root': os.environ.get('KCDMP_RIG_ROOT_A', r'D:\SteamLibrary\kcdmp_rig_A'), 'user_folder': 'KingdomCome2_A',
          'cvars': _cvars('KingdomCome2_A', 6, 4096, 1600, 900, 80, 60), 'res': ('1600', '900')},
    'B': {'root': os.environ.get('KCDMP_RIG_ROOT_B', r'D:\SteamLibrary\kcdmp_rig_B'), 'user_folder': 'KingdomCome2_B',
          'cvars': _cvars('KingdomCome2_B', 4, 2048, 1280, 720, os.environ.get('KCDMP_RIG_B_X', '2600'), os.environ.get('KCDMP_RIG_B_Y', '60')),
          'res': ('1280', '720')},
}
ROOT_A, ROOT_B = COPIES['A']['root'], COPIES['B']['root']
USER_FOLDER_A, USER_FOLDER_B = COPIES['A']['user_folder'], COPIES['B']['user_folder']
BEGIN = '-- ==== KCDMP rig: this copy (WO-206 A.1) ===='


def user_folders(cid):
    """The names a copy's engine may use: its own, and its own with "(1)" when it starts as a second instance (A.4)."""
    u = COPIES[cid]['user_folder']
    return (u, u + '(1)')


def say(*a): print(*a, flush=True)


def _files(root, sub):
    base = os.path.join(root, sub)
    for d, _, fs in os.walk(base):
        for f in fs:
            yield os.path.relpath(os.path.join(d, f), root)


def _stat(p):
    s = os.stat(p)
    return [s.st_size, int(s.st_mtime), s.st_ino]


def copy_system_cfg(install_text, cvars):
    """The install's system.cfg text with a copy's block appended once (pure; tested)."""
    keep = install_text.split(BEGIN)[0].rstrip('\r\n')
    return '\n'.join([keep, '', BEGIN] + ['%s = %s' % kv for kv in cvars]) + '\n'


def copy_attributes(xml, res):
    """The player's profile attributes with a copy's display, volume and texture values (pure; tested)."""
    want = {'res_fs': '0', 'res_x': res[0], 'res_y': res[1], 'sound_master': '0', 'sound_music': '0', 'sound_sound': '0',
            'vsync': '0', 'sys_spec_Texture': '1'}
    out = xml
    for k, v in want.items():
        rx = re.compile(r'(<Attr name="%s" value=")[^"]*(")' % re.escape(k))
        if rx.search(out): out = rx.sub(lambda m: m.group(1) + v + m.group(2), out)
        else: out = out.replace('</Attributes>', '\t<Attr name="%s" value="%s" />\n</Attributes>' % (k, v))
    return out


def create(cid):
    c = COPIES[cid]
    a, b = INSTALL, c['root']
    if os.path.splitdrive(os.path.abspath(a))[0].lower() != os.path.splitdrive(os.path.abspath(b))[0].lower():
        raise SystemExit('hard links need the install and the mirror on one volume')
    os.makedirs(b, exist_ok=True)
    made = linked = 0
    lst = {}
    for sub in LINKED:
        for rel in _files(a, sub):
            src, dst = os.path.join(a, rel), os.path.join(b, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            if os.path.exists(dst):
                if os.stat(dst).st_ino != os.stat(src).st_ino:
                    raise SystemExit('%s exists and is not a link to the install' % dst)
            else:
                os.link(src, dst); made += 1
            linked += 1
            lst[rel] = _stat(src)
    for f in COPIED_FILES:
        if os.path.exists(os.path.join(a, f)): shutil.copy2(os.path.join(a, f), os.path.join(b, f))
    mods = os.path.join(b, 'Mods', 'kdcmp')
    if os.path.isdir(mods): shutil.rmtree(mods)
    shutil.copytree(MOD_TEMPLATE, mods)
    if os.path.exists(MOD_PAK): shutil.copy2(MOD_PAK, os.path.join(mods, 'Data', 'kdcmp.pak'))
    with open(os.path.join(a, 'system.cfg'), encoding='utf-8', errors='replace') as f: atext = f.read()
    with open(os.path.join(b, 'system.cfg'), 'w', encoding='utf-8', newline='\r\n') as f: f.write(copy_system_cfg(atext, c['cvars']))
    with open(os.path.join(b, LIST), 'w', encoding='utf-8') as f: json.dump(lst, f)
    say('mirror root', cid, b, ': %d files linked (%d new), Mods\\kdcmp = the rig\'s build%s, system.cfg written' % (
        linked, made, '' if os.path.exists(MOD_PAK) else ' (NO run\\mod\\kdcmp.pak: the backup\'s pak)'))
    return linked


def verify(cid):
    """Every linked file still one file with the install's, unchanged since creation; no file added under a linked folder
    of either root. Returns a list of problems (empty = pass)."""
    b = COPIES[cid]['root']
    p = os.path.join(b, LIST)
    if not os.path.exists(p): return []   # never created: nothing linked
    with open(p, encoding='utf-8') as f: lst = json.load(f)
    bad = []
    for rel, st in lst.items():
        pa, pb = os.path.join(INSTALL, rel), os.path.join(b, rel)
        if not os.path.exists(pa) or not os.path.exists(pb): bad.append('missing ' + rel); continue
        sa = _stat(pa)
        if sa[:2] != st[:2]: bad.append('changed ' + rel)
        if os.stat(pb).st_ino != sa[2]: bad.append('no longer linked ' + rel)
    for sub in LINKED:
        for root in (INSTALL, b):
            for rel in _files(root, sub):
                if rel not in lst: bad.append('new file %s in %s' % (rel, root))
    return bad


def seed_profile(cid):
    """A copy's own profile, from the player's, written only under the copy's own user folders (both names)."""
    src = os.path.join(SAVED_GAMES, USER_FOLDER_REAL, 'profiles')
    with open(os.path.join(src, 'default', 'attributes.xml'), encoding='ascii', errors='replace') as f: x = f.read()
    for name in user_folders(cid):
        dst = os.path.join(SAVED_GAMES, name, 'profiles')
        if os.path.normcase(os.path.dirname(dst)) == os.path.normcase(os.path.dirname(src)): raise SystemExit('refusing: the player\'s folder')
        os.makedirs(os.path.join(dst, 'default'), exist_ok=True)
        shutil.copy2(os.path.join(src, 'settings.xml'), os.path.join(dst, 'settings.xml'))
        shutil.copy2(os.path.join(src, 'default', 'profile.xml'), os.path.join(dst, 'default', 'profile.xml'))
        with open(os.path.join(dst, 'default', 'attributes.xml'), 'w', encoding='ascii', newline='\r\n') as f:
            f.write(copy_attributes(x, COPIES[cid]['res']))
        os.makedirs(os.path.join(SAVED_GAMES, name, 'saves'), exist_ok=True)
        say('profile seeded', dst)


if __name__ == '__main__':
    v = sys.argv[1] if len(sys.argv) > 1 else 'verify'
    which = ['A', 'B'] if len(sys.argv) < 3 or sys.argv[2] == 'all' else [sys.argv[2].upper()]
    rc = 0
    for cid in which:
        if v == 'create': create(cid)
        elif v == 'profile': seed_profile(cid)
        elif v == 'verify':
            bad = verify(cid)
            say('mirror verify', cid + ':', 'PASS' if not bad else 'FAIL %d' % len(bad))
            for x in bad[:30]: say(' ', x)
            rc |= 1 if bad else 0
    sys.exit(rc)
