# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-151 Phase 0.3: the frame-rate soak that gates every installer (docs/WO-148-findings.md s7.12 item 4).

  python tools/perf/soak.py run --label <name> --throwaway <playlineN> [--minutes 10] [--fight-at 3] [--vanilla]
  python tools/perf/soak.py verdict --mod <run.json> --vanilla <run.json> [--version X]   -> tools/perf/soak-record.json
  python tools/perf/soak.py verdict --mod <run.json> --mod-only [--version X] [--record R]  (WO-154: the maintainer's call)

`run` drives the game already running on this machine (the Modding Tools build, the debug REST console on
localhost:1403), on a THROWAWAY save only: it reads which save is loaded from kcd.log first and refuses any
other playline. No key, no mouse, no window focus: everything is the game's own Lua through the console.

The scene (the 0.42.8 hunt's crowd and fight, WO-148 s7.6, made input-free):
  * from the start, three commoners 3 m around the player (no AI, never saved): 2+ souls within 15 m, the
    0.42.5-0.42.7 leak's trigger;
  * from --fight-at minutes, a fight beside them: two more commoners, AI on, the first sent the game's own
    attack interrupt at the second (crime:attackInitiatedByConcept), re-sent every 15 s, a new pair when one
    goes down. Nobody fights the player (no Game Over can stop the run); if the player loses health the
    fighters are removed and the run says so.
Every 10 s one row: the frame rate over those 10 s (the game's frame counter and clock), the depth of the game's
stat stack on its main thread (statstack.py, read-only), and the FAULT lines the DLL has written since the start.
Everything spawned is removed at the end. The rows go to <label>.json / .csv / .md beside --out.

WO-154 --partner-ctl <peer.ctl>: the same scene with a joined partner (tools/wo121/avatarpeer, its control file; the relay,
the host's agent and the peer are started beside this run). The partner's avatar stands 4 m from the player at the start,
paces 4 m back and forth all the run, and from the fight on lands an attributed hit on each pair's attacker every 15 s
(the host's world takes it, the attacker may turn on the avatar): the avatar's position stream, the native writer, the
threat and fight code all run through the soak. The crowd's third soul is swapped (the first avatar takes 4166b913).

`verdict` compares a run with the mod against the same scene without it (--vanilla: no pak, no DLL) and writes
the record Build-Installer.ps1 checks. PASS needs all of:
  * the mod's last 2 minutes within 10% of its first 2 minutes;
  * the mod's first and last 2 minutes each within 10% of the same windows without the mod;
  * the stat stack 0 in every row; no FAULT line.
The record carries the git trees of the code the soak ran (native/KCDMP, the mod's Lua and tables, the agent,
the relay, the protocol): Build-Installer.ps1 runs only when they match the tree it is building.

--mod-only (WO-154, the maintainer's instruction for 0.45.0: "only run the soak on the mod version, no vanilla
comparison"): the checks against the game without the mod are not made and the record says so; PASS needs the mod's
last 2 minutes within 10% of its first 2, the stat stack 0 in every row, no FAULT line, enough rows, the code
committed.
"""
import argparse, json, os, re, subprocess, sys, time, uuid, urllib.parse, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import statstack  # noqa: E402

API = 'http://localhost:1403'
DEFAULT_GAME = r'D:\SteamLibrary\steamapps\common\KCD2Mod'
# Roster commoners (WO-148's crowd souls) and two more for the fight: shared soul GUIDs of the game's table.
CROWD = ['4f45df7c-4667-77a0-a415-d03b0cd1e293', '46356c7b-ab60-1377-e8e4-514c8a8dcfbb', '4166b913-6b12-1965-cbb6-509a49250ba6']
PARTNER_SOUL = '7e4881d6-ffb7-416f-bbbe-49bc622747b2'   # WO-154: the partner soak's third bystander (4166b913 is the first avatar's)
TREES = ['native/KCDMP', 'kdcmp/Data/Scripts', 'kdcmp/Data/Libs', 'kdcmp/mod.manifest',
         'dotnet/KcdMp.Client', 'dotnet/KcdMp.Server', 'dotnet/KcdMp.Protocol']


def cmd(c, timeout=20):
    enc = urllib.parse.quote(c, safe='')
    if len(enc) > 1900:
        raise ValueError('command too long: %d' % len(enc))
    for attempt in range(40):
        try:
            with urllib.request.urlopen(API + '/api/System/Console/ExecuteString?command=' + enc, timeout=timeout) as r:
                return r.read(100000).decode('utf-8', 'replace')
        except urllib.error.HTTPError as e:
            if e.code != 503:
                raise
            time.sleep(0.05 + 0.05 * attempt)
        except (ConnectionError, TimeoutError, urllib.error.URLError) as e:
            # WO-151 L7: the game's console dropped one connection mid-run (WinError 10054) and the whole
            # 10-minute soak died with it; a dropped connection is retried like a busy console (503)
            if attempt == 39:
                raise
            time.sleep(0.25 + 0.1 * attempt)
    raise RuntimeError('the console did not answer after 40 attempts')


def tail(path, nbytes=600000):
    with open(path, 'rb') as f:
        f.seek(0, 2); f.seek(max(0, f.tell() - nbytes))
        return f.read().decode('utf-8', 'replace').splitlines()


class Game:
    def __init__(self, game_dir):
        self.dir = game_dir
        self.klog = os.path.join(game_dir, 'kcd.log')
        self.nlog = os.path.join(game_dir, 'kcdmp-native.mirror.log')

    def lua(self, code, wait=0.6):
        tok = uuid.uuid4().hex[:8]
        cmd('#' + "local function Q(t) System.LogAlways('W151S %s ' .. tostring(t)) end; %s" % (tok, code))
        deadline = time.time() + max(wait, 0.3) + 4.0
        time.sleep(wait)
        while True:
            got = [l.split('W151S %s ' % tok, 1)[1] for l in tail(self.klog) if ('W151S %s ' % tok) in l]
            if got or time.time() > deadline:
                return got
            time.sleep(0.2)

    def loaded_save(self):
        for l in reversed(tail(self.klog, 30000000)):
            m = re.search(r"Loading saved game '%USER%/saves/([^/']+)/([^']+)'", l)
            if m:
                return m.group(1), m.group(2)
        return None, None

    def native_size(self):
        try:
            return os.path.getsize(self.nlog)
        except OSError:
            return 0

    def faults_since(self, offset):
        if not os.path.exists(self.nlog):
            return []
        with open(self.nlog, 'rb') as f:
            f.seek(offset)
            data = f.read().decode('utf-8', 'replace')
        return [l for l in data.splitlines() if re.search(r'\] t\d+ FAULT ', l)]


SPAWN = ("local p = player:GetWorldPos(); local yaw = player:GetWorldAngles().z; local out = {{}}; "
         "local souls = {{ {souls} }}; local names = {{ {names} }}; local ai = {ai}; "
         "for i = 1, #names do local a = yaw + {base} + (i - 1) * {step}; local x, y = p.x - math.sin(a) * {r}, p.y + math.cos(a) * {r}; "
         "local z = System.GetTerrainElevation({{x=x, y=y, z=0}}) or p.z; "
         "if not System.GetEntityByName(names[i]) then XGenAIModule.SpawnEntity({{ Name = names[i], ClassName = 'NPC', Pos = {{ x, y, z }}, "
         "SharedSoulGuid = souls[i], NoAI = not ai }}) end; local e = System.GetEntityByName(names[i]); "
         "local nf = e and pcall(function() e:SetFlags(ENTITY_FLAG_NO_SAVE, 3) end); out[#out+1] = names[i] .. (e and ' ok' or ' MISSING') end; "
         "Q(table.concat(out, ', '))")


def spawn(g, names, souls, ai, base, step, r):
    code = SPAWN.format(souls=', '.join("'%s'" % s for s in souls), names=', '.join("'%s'" % n for n in names),
                        ai='true' if ai else 'false', base=base, step=step, r=r)
    return g.lua(code, 1.2)


def remove(g, names):
    code = ("local out = {}; for _, n in ipairs({ %s }) do local e = System.GetEntityByName(n); if e then "
            "pcall(System.RemoveEntity, e.id); out[#out+1] = n end end; Q(#out > 0 and table.concat(out, ',') or 'none')"
            % ', '.join("'%s'" % n for n in names))
    return g.lua(code, 0.8)


ATTACK = ("local a = System.GetEntityByName('{a}'); local b = System.GetEntityByName('{b}'); "
          "if not a or not b then Q('fighters missing'); return end; "
          "local da, db = false, false; pcall(function() da = a.actor:IsDead() end); pcall(function() db = b.actor:IsDead() end); "
          "if da or db then Q('down ' .. tostring(da) .. ' ' .. tostring(db)); return end; "
          "local t = XGenAIModule.MakeTableFromType('crime:attackInitiatedByConcept'); t.target = b.this.id; t.priorityTarget = true; "
          "local ok = pcall(function() XGenAIModule.SendMessageToEntityData(a.this.id, 'crime:attackInitiatedByConcept', t) end); "
          "local ha, hb = -1, -1; pcall(function() ha = a.actor:GetHealth() end); pcall(function() hb = b.actor:GetHealth() end); "
          "Q(string.format('attack sent=%s hp %.0f/%.0f', tostring(ok), ha, hb))")


def run(args):
    g = Game(args.game_dir)
    pl, save = g.loaded_save()
    if pl != args.throwaway:
        sys.exit('REFUSED: the loaded save is %s/%s, not the throwaway playline %s -- the soak runs only on a throwaway copy'
                 % (pl, save, args.throwaway))
    print('save: %s/%s (throwaway)' % (pl, save))
    pid = statstack.game_pid()
    tid = statstack.main_thread(pid)
    reader = statstack.Reader(pid, tid)
    print('game pid %d, main thread %d, RPGModule %#x/%#x' % (pid, tid, reader.identity[0], reader.identity[1]))
    nstart = g.native_size()
    out_dir = args.out or os.path.join(HERE, 'runs')
    os.makedirs(out_dir, exist_ok=True)
    crowd = ['w151_soak_c%d' % i for i in (1, 2, 3)]
    souls = list(CROWD)
    partner = getattr(args, 'partner_ctl', None)
    if partner:
        souls[2] = PARTNER_SOUL      # the first avatar takes 4166b913 (the roster's first soul): no soul twice
        pp = g.lua("local p = player:GetWorldPos(); Q(string.format('%.2f %.2f %.2f', p.x, p.y, p.z))")
        px, py, pz = [float(x) for x in pp[0].split()] if pp else (0.0, 0.0, 0.0)
        with open(partner, 'a') as f:
            f.write('stand %.2f %.2f %.2f 1.5708\n' % (px + 4.0, py, pz + 0.05))
        print('partner: stands 4 m east of the player (%s)' % partner)
    next_pace, pace_dir = 5.0, 1
    pairs = 0
    fighters = None
    rows, notes = [], []
    hp0 = g.lua("Q(string.format('%.1f', player.actor:GetHealth()))")
    hp0 = float(hp0[0]) if hp0 else 100.0
    print('crowd:', spawn(g, crowd, souls, False, 0.0, 2.0944, 3.0))
    t0 = time.time()
    f0 = g.lua("Q(System.GetFrameID() .. ' ' .. System.GetCurrAsyncTime())")
    last_f, last_t = [float(x) for x in f0[0].split()] if f0 else (0.0, 0.0)
    next_attack = 0.0
    fight_hp, fight_hp_at = None, 0.0   # the victim's last health, and when it last changed
    total = args.minutes * 60.0
    stopped_fight = False
    while True:
        el = time.time() - t0
        if el >= total:
            break
        # the fight
        if not stopped_fight and el >= args.fight_at * 60.0:
            if fighters is None or el >= next_attack:
                if fighters is None:
                    pairs += 1
                    fighters = ['w151_soak_a%d' % pairs, 'w151_soak_b%d' % pairs]
                    print('fight pair %d:' % pairs, spawn(g, fighters, CROWD[:2], True, 3.1416, 0.6, 7.0))
                    time.sleep(1.0)
                r = g.lua(ATTACK.format(a=fighters[0], b=fighters[1]), 0.8)
                if partner:
                    with open(partner, 'a') as f:
                        f.write('npchit %s 3 8\n' % fighters[0])
                msg = r[0] if r else 'no answer'
                next_attack = el + 15.0
                m = re.search(r'hp (-?\d+)/(-?\d+)', msg)
                hb = int(m.group(2)) if m else None
                stalled = hb is not None and hb == fight_hp and el - fight_hp_at >= 45.0
                if hb is not None and hb != fight_hp:
                    fight_hp, fight_hp_at = hb, el
                if msg.startswith('down') or msg.startswith('fighters missing') or stalled or (hb is not None and hb < 12):
                    notes.append('%.0fs pair %d %s%s' % (el, pairs, msg, ' (stalled 45 s)' if stalled else ''))
                    remove(g, fighters)
                    fighters = None
                    fight_hp, fight_hp_at = None, el
                    next_attack = el
        if partner and el >= next_pace:
            with open(partner, 'a') as f:     # 4 m one way at a walk, the next time back
                f.write('move 1.0 %.4f 4\n' % (-1.5708 if pace_dir > 0 else 1.5708))
            pace_dir, next_pace = -pace_dir, el + 10.0
        time.sleep(max(0.0, 10.0 - ((time.time() - t0) % 10.0)))
        r = g.lua("Q(System.GetFrameID() .. ' ' .. System.GetCurrAsyncTime() .. ' ' .. player.actor:GetHealth())", 0.4)
        if not r:
            notes.append('%.0fs no answer from the game (a menu, a load?)' % (time.time() - t0))
            continue
        f, t, hp = [float(x) for x in r[0].split()]
        fps = (f - last_f) / (t - last_t) if t > last_t else 0.0
        last_f, last_t = f, t
        try:
            depth, ids, first_read = reader.settled_depth()   # WO-154: between frames (the smallest of a few reads)
            if first_read != depth:
                notes.append('%.0fs one stack read caught a frame mid-stat-call (%d, settled %d)' % (time.time() - t0, first_read, depth))
        except Exception as e:   # noqa: BLE001
            depth, ids = -1, []
            notes.append('stack read failed: %s' % e)
        faults = len(g.faults_since(nstart)) if not args.vanilla else 0
        row = {'t_s': round(time.time() - t0), 'fps': round(fps, 1), 'stack': depth, 'faults': faults,
               'phase': 'fight' if (fighters is not None or pairs > 0) and not stopped_fight else 'crowd', 'player_hp': round(hp, 1)}
        rows.append(row)
        print('%(t_s)4ds  fps=%(fps)5.1f  stack=%(stack)d  faults=%(faults)d  %(phase)s  hp=%(player_hp).0f' % row)
        if hp < hp0 - 5 and not stopped_fight:
            stopped_fight = True
            notes.append('%.0fs the player lost health (%.0f -> %.0f): the fighters were removed' % (time.time() - t0, hp0, hp))
            if fighters:
                remove(g, fighters)
                fighters = None
    print('cleanup:', remove(g, crowd + ['w151_soak_a%d' % i for i in range(1, pairs + 1)] + ['w151_soak_b%d' % i for i in range(1, pairs + 1)]))
    fault_lines = g.faults_since(nstart) if not args.vanilla else []
    res = {'label': args.label, 'mode': 'vanilla' if args.vanilla else ('mod+partner' if partner else 'mod'), 'save': '%s/%s' % (pl, save),
           'minutes': args.minutes, 'fight_at_min': args.fight_at, 'pairs': pairs, 'rows': rows, 'notes': notes,
           'fault_lines': fault_lines[:50], 'rpgmodule': '%#x/%#x' % reader.identity,
           'when': time.strftime('%Y-%m-%d %H:%M:%S'), 'git_head': git('rev-parse', 'HEAD')}
    base = os.path.join(out_dir, args.label)
    json.dump(res, open(base + '.json', 'w'), indent=1)
    with open(base + '.csv', 'w') as f:
        f.write('t_s,fps,stack,faults,phase,player_hp\n')
        for r in rows:
            f.write('%(t_s)d,%(fps).1f,%(stack)d,%(faults)d,%(phase)s,%(player_hp).1f\n' % r)
    with open(base + '.md', 'w') as f:
        f.write(table(res))
    print('wrote', base + '.{json,csv,md}')


def window(rows, lo, hi):
    v = [r['fps'] for r in rows if lo <= r['t_s'] < hi and r['fps'] > 0]
    return sum(v) / len(v) if v else 0.0


def table(res):
    lines = ['| t (s) | FPS | stat stack | FAULT lines | phase | player hp |', '|---|---|---|---|---|---|']
    for r in res['rows']:
        lines.append('| %(t_s)d | %(fps).1f | %(stack)d | %(faults)d | %(phase)s | %(player_hp).0f |' % r)
    return '\n'.join(lines) + '\n'


def git(*a):
    return subprocess.run(['git', '-C', REPO] + list(a), capture_output=True, text=True).stdout.strip()


def trees(rev='HEAD'):
    return {p: git('rev-parse', '%s:%s' % (rev, p)) for p in TREES}


def verdict(args):
    mod = json.load(open(args.mod))
    mod_only = getattr(args, 'mod_only', False)
    if not mod_only and not args.vanilla:
        sys.exit('verdict needs --vanilla <run.json> or --mod-only')
    van = None if mod_only else json.load(open(args.vanilla))
    total = mod['minutes'] * 60.0
    m_first, m_last = window(mod['rows'], 0, 120), window(mod['rows'], total - 120, total + 1)
    v_first, v_last = (0.0, 0.0) if mod_only else (window(van['rows'], 0, 120), window(van['rows'], total - 120, total + 1))
    checks = []

    def chk(ok, text):
        checks.append(('PASS' if ok else 'FAIL', text))

    def within(a, b):
        return b > 0 and abs(a - b) / b <= 0.10
    chk(within(m_last, m_first), 'the last 2 minutes (%.1f FPS) within 10%% of the first 2 (%.1f)' % (m_last, m_first))
    if not mod_only:
        chk(within(m_first, v_first), 'the first 2 minutes (%.1f) within 10%% of the game without the mod (%.1f)' % (m_first, v_first))
        chk(within(m_last, v_last), 'the last 2 minutes (%.1f) within 10%% of the game without the mod (%.1f)' % (m_last, v_last))
    smax = max((r['stack'] for r in mod['rows']), default=-1)
    chk(smax == 0 and all(r['stack'] == 0 for r in mod['rows']), 'the stat stack 0 in every row (max %d)' % smax)
    chk(not mod['fault_lines'] and all(r['faults'] == 0 for r in mod['rows']), 'no FAULT line (%d)' % len(mod['fault_lines']))
    if mod_only:
        chk(len(mod['rows']) >= mod['minutes'] * 5, 'enough rows (%d)' % len(mod['rows']))
    else:
        chk(mod['save'].split('/')[0] == van['save'].split('/')[0], 'both runs on the same throwaway playline (%s, %s)' % (mod['save'], van['save']))
        chk(len(mod['rows']) >= mod['minutes'] * 5 and len(van['rows']) >= van['minutes'] * 5, 'enough rows (%d, %d)' % (len(mod['rows']), len(van['rows'])))
    ok = all(c[0] == 'PASS' for c in checks)
    dirty = git('status', '--porcelain', '--', *TREES)
    rec = {'verdict': 'PASS' if ok else 'FAIL', 'version': args.version, 'checks': checks,
           'mod': {'label': mod['label'], 'save': mod['save'], 'when': mod['when'], 'first2_fps': round(m_first, 1), 'last2_fps': round(m_last, 1),
                   'pairs': mod['pairs'], 'notes': mod['notes']},
           'vanilla': ({'waived': "the maintainer's instruction (WO-154, 0.45.0): only the mod version is soaked, no comparison with the game without the mod",
                        'first2_fps': None, 'last2_fps': None} if mod_only else
                       {'label': van['label'], 'save': van['save'], 'when': van['when'], 'first2_fps': round(v_first, 1), 'last2_fps': round(v_last, 1)}),
           'mode': mod.get('mode', 'mod'),
           'git_head': mod.get('git_head'), 'trees': trees(mod.get('git_head') or 'HEAD'),
           'dirty_at_verdict': dirty.splitlines() if dirty else [],
           'table_mod': table(mod), 'table_vanilla': '' if mod_only else table(van)}
    if dirty:
        rec['verdict'] = 'FAIL'
        checks.append(('FAIL', 'the soaked code is committed (uncommitted changes in: %s)' % ' '.join(l[3:] for l in dirty.splitlines())))
    out = args.record or os.path.join(HERE, 'soak-record.json')
    json.dump(rec, open(out, 'w'), indent=1)
    for c in checks:
        print('  %s  %s' % c)
    print('%s -> %s' % (rec['verdict'], out))
    sys.exit(0 if rec['verdict'] == 'PASS' else 1)


def check(args):
    """Build-Installer.ps1's gate: the record says PASS for exactly the code trees of this checkout."""
    path = args.record or os.path.join(HERE, 'soak-record.json')
    if not os.path.exists(path):
        sys.exit('NO SOAK: %s is missing -- run the frame-rate soak (tools/perf/README.md) first' % path)
    rec = json.load(open(path))
    if rec.get('verdict') != 'PASS':
        sys.exit('SOAK FAILED: the last soak record says %s' % rec.get('verdict'))
    now = trees('HEAD')
    diff = [p for p in TREES if now.get(p) != rec.get('trees', {}).get(p)]
    if diff:
        sys.exit('SOAK STALE: the code changed since the soak passed (%s) -- run it again' % ', '.join(diff))
    dirty = git('status', '--porcelain', '--', *[p for p in TREES if p != 'kdcmp/Data'])
    dirty = [l for l in dirty.splitlines() if not l.endswith('kdcmp.pak')]
    if dirty:
        sys.exit('SOAK STALE: uncommitted changes in the soaked code: %s' % ' '.join(l[3:] for l in dirty))
    van = rec.get('vanilla') or {}
    print('soak PASS for this code (%s, mod %s FPS first/last 2 min %.1f/%.1f, %s)' % (
        rec.get('version'), rec['mod']['label'], rec['mod']['first2_fps'], rec['mod']['last2_fps'],
        'the game without the mod not compared: ' + van['waived'] if van.get('waived') else
        'without the mod %.1f/%.1f' % (van['first2_fps'], van['last2_fps'])))


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest='verb', required=True)
    r = sub.add_parser('run')
    r.add_argument('--label', required=True)
    r.add_argument('--throwaway', required=True, help='the throwaway playline folder name, e.g. playline9')
    r.add_argument('--minutes', type=float, default=10.0)
    r.add_argument('--fight-at', type=float, default=3.0)
    r.add_argument('--vanilla', action='store_true', help='the game without the mod (no pak, no DLL): no FAULT lines to read')
    r.add_argument('--game-dir', default=DEFAULT_GAME)
    r.add_argument('--out')
    r.add_argument('--partner-ctl', help='WO-154: a joined scripted partner (tools/wo121/avatarpeer control file)')
    v = sub.add_parser('verdict')
    v.add_argument('--mod', required=True)
    v.add_argument('--vanilla')
    v.add_argument('--mod-only', action='store_true', help="WO-154: no comparison with the game without the mod (the maintainer's call)")
    v.add_argument('--version', default='')
    v.add_argument('--record')
    c = sub.add_parser('check')
    c.add_argument('--record')
    a = ap.parse_args()
    {'run': run, 'verdict': verdict, 'check': check}[a.verb](a)
