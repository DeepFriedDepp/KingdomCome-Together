# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-197 Part D (never shipped): WO-195B's lock check 2a, scripted. Host role (live195b.py up first), the throwaway playline4 only.

  python lock197.py

Near Henry: two locked chests and two locked doors. The first of each is claimed as the joiner's unlock (the agent's own entry points,
KCD2MP_W195BChestAsked / KCD2MP_W151DoorAsked, unlock=1); the second of each is the control. Then a host save (the mod's own path: the
schnapps save, KCD2MP_HostWorldSave), a reload of that save, and the four read again. PASS: the claimed two unlocked after the reload, the
controls still locked. Results: <W195B_SP>/lock197.results.json
"""
import json, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
os.environ.setdefault('W195_SP', os.environ.get('W195B_SP', ''))
import live
import run165 as R
import run195b as B

OUT = os.path.join(B.SP, 'lock197.results.json')


def say(*a): print(time.strftime('%H:%M:%S'), *a, flush=True)


SCAN = ('local p = player:GetWorldPos(); local st, dr = {}, {}; '
        'for _, e in ipairs(System.GetEntitiesInSphere(p, 60) or {}) do '
        '  local c = e.class; if e.bLocked == true and (c == "Stash" or c == "AnimDoor") then '
        '    local q = e:GetWorldPos(); local d = math.sqrt((q.x-p.x)^2 + (q.y-p.y)^2); '
        '    local row = string.format("%s,%.2f,%.2f,%.2f,%d,%.1f", e:GetName(), q.x, q.y, q.z, tonumber(e.nDirection) or 0, d); '
        '    if c == "Stash" then st[#st+1] = row else dr[#dr+1] = row end end end; '
        'System.LogAlways("@TAG " .. table.concat(st, ";") .. "|" .. table.concat(dr, ";"))')


def locked(names):
    code = ('local r = {}; for _, n in ipairs({%s}) do local e = System.GetEntityByName(n); '
            'r[#r+1] = n .. "=" .. (e and tostring(e.bLocked == true) or "missing") end; System.LogAlways("@TAG " .. table.concat(r, ","))'
            % ','.join('"%s"' % n for n in names))
    s = B.Q(code, 'lk', 6) or ''
    return dict(kv.split('=') for kv in s.strip().split(',') if '=' in kv)


def rows(part):
    out = []
    for r in part.split(';'):
        f = r.split(',')
        if len(f) == 6: out.append({'name': f[0], 'x': float(f[1]), 'y': float(f[2]), 'z': float(f[3]), 'dir': int(f[4]), 'd': float(f[5])})
    return sorted(out, key=lambda r: r['d'])


def main():
    res = {'at': time.strftime('%H:%M:%S')}
    s = B.Q(SCAN, 'scan', 8) or '|'
    st, dr = (rows(x) for x in s.strip().split('|', 1)) if '|' in s else ([], [])
    say('locked chests near:', [r['name'] for r in st][:6], 'doors:', [r['name'] for r in dr][:6])
    if len(st) < 2 or len(dr) < 2:
        res['result'] = 'NOT RUN: fewer than two locked chests / doors within 60 m'; say(res['result'])
        json.dump(res, open(OUT, 'w'), indent=1); return
    chest, chest_c, door, door_c = st[0], st[1], dr[0], dr[1]
    names = [chest['name'], chest_c['name'], door['name'], door_c['name']]
    res['entities'] = names
    res['before'] = locked(names); say('before', res['before'])
    live.lua('KCD2MP_W195BChestAsked("w197test", "%s", 1, %f, %f, %f)' % (chest['name'], chest['x'], chest['y'], chest['z']))
    live.lua('KCD2MP_W151DoorAsked("w197test", "%s", %d, 1, %f, %f, %f)' % (door['name'], door['dir'], door['x'], door['y'], door['z']))
    time.sleep(2)
    res['claimed'] = locked(names); say('after the claims', res['claimed'])
    pl4 = os.path.join(os.environ['USERPROFILE'], 'Saved Games', 'kingdomcome2', 'saves', 'playline4')
    have = set(n for n in os.listdir(pl4) if n.endswith('.whs'))
    live.lua('KCD2MP_HostWorldSave("w197lock", 1)')
    f, t0 = None, time.time()
    while time.time() - t0 < 60 and not f:
        time.sleep(2)
        new = [n for n in os.listdir(pl4) if n.endswith('.whs') and n not in have]
        if new:
            n = max(new, key=lambda n: os.path.getmtime(os.path.join(pl4, n)))
            s0 = os.path.getsize(os.path.join(pl4, n)); time.sleep(2)
            if os.path.getsize(os.path.join(pl4, n)) == s0 and s0 > 0: f = 'playline4/' + n
    res['save'] = f; say('host save', f)
    if not f or f == 'none':
        res['result'] = 'FAIL: no host save'; json.dump(res, open(OUT, 'w'), indent=1); say(res['result']); return
    save = os.path.splitext(os.path.basename(f.replace('\\', '/')))[0]
    R.load('4', save); time.sleep(30)
    res['reloaded'] = locked(names); say('after the reload of', save, res['reloaded'])
    a = res['reloaded']
    ok = a.get(names[0]) == 'false' and a.get(names[2]) == 'false' and a.get(names[1]) == 'true' and a.get(names[3]) == 'true'
    ok_before = all(v == 'true' for v in res['before'].values())
    res['result'] = ('PASS' if ok and ok_before else 'FAIL') + ': chest %s / door %s unlocked on the claim and after save + reload; controls %s / %s locked' % (names[0], names[2], names[1], names[3])
    say(res['result'])
    json.dump(res, open(OUT, 'w'), indent=1)


if __name__ == '__main__':
    main()
