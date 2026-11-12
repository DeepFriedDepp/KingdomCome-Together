# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-220: the scoreboard's one scenario on two real copies (test tool, never shipped). tools/rig/scoreboard.py reads its run.

FIXED. A work order is judged by this scenario and that tool; it does not change either. A change to the measure is its own work order
and re-runs the base line (docs/WO-220-findings.md records the 0.65.0 base line and its spread).

    python tools/rig/scenarios.py score          (the pair up and joined by pair.py; KCDMP_RIG_LOW=B: the joiner at low settings)

Two runs, the same every time (this bench's memory holds two games for about 35 minutes, and a crash must not take both parts):
    python tools/rig/scenarios.py score          the three scenes below (about 27 minutes with the join)
    python tools/rig/scenarios.py score-works    the works-at-all checks, the staged fight, the loot take (about 15 minutes)
The scoreboard reads the scenes' run and takes the checks from the other:  scoreboard.py <scenes run> --works <checks run>.
    the smith's house by day (3 min)             the smith at his anvil, both players by the house; a framed pair
    the village by day (6 min)                  both players beside a villager who walks, followed; the bone sampler on both copies
                                                (20 Hz, the 10 nearest bodies, every skeleton updated); a framed pair of the walker
    the tavern by night (5 min)                 the seated guests (the scene starts when three have come), the joiner 2.5 m inside; a
                                                framed pair of a seated guest
    the works-at-all checks                     one line each, yes / no / not run (the scoreboard's works=n/10)
    one staged fight (80 s)                     three bandits on both players, two knocked down and up by the game's own calls; then
                                                the knockdown's check and the loot take (the fight's body)

Each scene writes its window (wall ms) to score-windows.json and each check its row to score-works.json in the scenario's folder.
Every action goes through the game's console or Lua. Keys are sent (send-only, tools/rig/keys.py: the rig's own copy, in front, checked
before every event) in ONE check, the trade screen (the dialogue's trade option has no console door); KCDMP_RIG_NO_KEYS=1 leaves that
check out ("not run"). Nothing here reads input. A check that could not be staged is "not run", never "no".
"""
import json, os, re, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rig

VILLAGE_S = 360.0
SMITHY_S = 180.0
TAVERN_S = 300.0
BRAWL_S = 80.0
BONE_BODIES, BONE_MS, BONE_JOINTS = 10, 50, 'root,lfoot,rfoot'


def _S():
    import scenarios
    return scenarios


def _T():
    import scen217
    return scen217


def _put(r, name, row):
    p = os.path.join(r.dir, name)
    rows = []
    if os.path.exists(p):
        with open(p, encoding='utf-8') as f: rows = json.load(f)
    rows.append(row)
    with open(p, 'w', encoding='utf-8') as f: json.dump(rows, f, indent=1)


def window(r, label, t0, t1, **kw):
    row = dict(label=label, t0_ms=int(t0 * 1000), t1_ms=int(t1 * 1000), **kw)
    _put(r, 'score-windows.json', row)
    rig.note('score-window', **{k: v for k, v in row.items() if k != 'pair'})
    return row


def works(r, check, ok, why, detail=None):
    """ok: True (yes), False (no), None (not run: the bench could not stage it)."""
    row = dict(check=check, ok=ok, why=why, detail=detail)
    _put(r, 'score-works.json', row)
    rig.note('score-works', check=check, ok=ok, why=why)
    return row


# ---------------------------------------------------------------- the players are left alone while a scene is measured

# save440's Henry is wanted: guards come for the bench's players within a minute wherever they stand. Two things keep a scene a
# scene: the engine's own switch that makes its AI leave the player out (ai_IgnorePlayer, set on the host at the scenario's start and
# taken back at its end), and, while a scene is measured, a fight on a player ended once every 2 s (the game's own stopFight message
# to every fighter within 30 m). How often a player was in a fight is counted -- the scoreboard prints it: a scene that was mostly a
# fight around the players measures that.
KEEPER = ('W220KEEP = (W220KEEP or 0) + 1; W220DANGER = 0; W220CHECKS = 0; local gen = W220KEEP; local function k() '
          'if gen ~= W220KEEP then return end; local d = false; pcall(function() local s = player.soul; '
          'd = s:IsInCombatDanger() == true or s:IsInCombatMode() == true end); '
          'W220CHECKS = W220CHECKS + 1; if d then W220DANGER = W220DANGER + 1; '
          'for _, e in ipairs(System.GetEntitiesInSphere(player:GetWorldPos(), 30) or {}) do if e.soul and e.this and e ~= player then '
          'local c = false; pcall(function() c = e.soul:IsInCombatMode() == true end); if c then '
          'local t = XGenAIModule.MakeTableFromType("stopFight"); t.soulCount = 1; t.messageId = "w220keep"; '
          'pcall(function() XGenAIModule.SendMessageToEntityData(e.this.id, "stopFight", t) end) end end end end; '
          'Script.SetTimer(2000, k) end; k()')
KEEPER_OFF = 'W220KEEP = (W220KEEP or 0) + 1; System.LogAlways(string.format("[W220D] %d %d", W220DANGER or 0, W220CHECKS or 0))'


def ignore_player(r, on):
    def fn():
        r.a.cmd('ai_IgnorePlayer %d' % (1 if on else 0))
        v = r.a.ask('System.LogAlways("[W220I] " .. tostring(System.GetCVar("ai_IgnorePlayer")))', 'W220I', 5)
        return True, 'ok', {'ai_IgnorePlayer': v}
    return fn


def peace(r, on):
    """The keeper on both copies (each for its own player); off returns {'host': 'danger/checks', 'joiner': ...}."""
    if on:
        for c in (r.a, r.b):
            c.lua(KEEPER); c.keep_up()
        return None
    out = {}
    for nm, c in (('host', r.a), ('joiner', r.b)):
        out[nm] = (c.ask(KEEPER_OFF, 'W220D', 5) or '? ?').replace(' ', '/')
        c.stop_keep()
    return out


def put_joiner(r, x, y, z=None, tol=15.0):
    for _ in range(4):
        r.b.teleport(x, y, z) if z is not None else r.b.teleport(x, y)
        time.sleep(3)
        q = r.b.pos()
        if q and ((q[0] - x) ** 2 + (q[1] - y) ** 2) ** 0.5 < tol: return True
    return False


def host_time(r, hh):
    r.a.lua('local now = Calendar.GetWorldTime(); local d = math.floor(now / 86400) * 86400 + %d; '
            'if d < now then d = d + 86400 end; Calendar.SetWorldTime(d)' % int(hh * 3600))


# ---------------------------------------------------------------- the bone sampler (tools/rig/bones.lua) on both copies

def bones_on(r):
    S = _S()
    sync = {}
    for c in (r.a, r.b):
        c.cmd('ca_ForceUpdateSkeletons 1')   # a skeleton out of view is not updated: its feet travel rigidly (WO-201)
        for ch in S.bones_chunks(): c.lua(ch)
    for c in (r.a, r.b):
        off = c.size(c.klog)
        c.lua('W206Bones_Start(%d, %d, "%s")' % (BONE_BODIES, BONE_MS, BONE_JOINTS))
        m = c.wait('k', off, r'\[W206B-SYNC\]', 6)
        first = c.wait('k', off, r'\[W206B-FIRST\] bodies=(\d+) with_joints=(\d+)', 6)
        sync[c.id] = {'role': 'host' if c is r.a else 'joiner', 'wall_ms': int(time.time() * 1000), 'seen': bool(m), 'log': c.klog, 'off': off,
                      'first': first.group(0) if first else None}
    return sync


def bones_off(r, sync):
    for c in (r.a, r.b):
        c.lua('W206Bones_Stop()')
        sync[c.id]['end'] = c.size(c.klog)
        c.cmd('ca_ForceUpdateSkeletons 0')
    with open(os.path.join(r.dir, 'bones-sync.json'), 'w', encoding='utf-8') as f: json.dump(sync, f, indent=1)
    return sync


# ---------------------------------------------------------------- the scenes

def smithy(r, secs=SMITHY_S):
    def fn():
        S, T = _S(), _T()
        smith = os.environ.get('KCDMP_RIG_SMITH', 'ttac_blacksmith')
        t = r.a.ask(S.FIND_INN % smith, 'W207I', 6)
        if not t or t == 'none': return False, 'no-smith', t
        parts = t.split(); x, y = float(parts[1]), float(parts[2])
        r.a.teleport(x + 3.0, y + 3.0)
        at = put_joiner(r, x + 4.0, y + 2.0)
        time.sleep(15)
        peace(r, True)
        t0 = time.time(); time.sleep(secs); t1 = time.time()
        danger = peace(r, False)
        pair = T.frame_pair(r, 'score-smith', smith)
        row = window(r, 'smithy', t0, t1, subject=smith, joiner_there=at, danger=danger, pair=pair)
        return True, 'ok' if at else 'joiner-not-there', row
    return fn


def village(r, secs=VILLAGE_S):
    def fn():
        S, T = _S(), _T()
        t = r.a.ask(S.FIND_INN % os.environ.get('KCDMP_RIG_INN', 'ttkc_woman_2'), 'W207I', 6)
        if not t or t == 'none': return False, 'no-town', t
        parts = t.split(); x, y = float(parts[1]), float(parts[2])
        r.a.teleport(x + 6.0, y + 6.0)
        if not put_joiner(r, x + 7.0, y + 6.5): return False, 'joiner-not-moved', {'to': [round(x + 7.0), round(y + 6.5)]}
        time.sleep(8)

        def positions():
            v = r.a.ask(S.NPC_POS % 45, 'W208V', 4) or ''
            d = {}
            for item in filter(None, v.split(';')):
                try: n, a, b, c = item.split(','); d[n] = (float(a), float(b), float(c))
                except ValueError: pass
            return d

        def pick():
            walker, moved = None, 0.0
            for _ in range(6):
                p0 = positions(); time.sleep(4); p1 = positions()
                for n, q in p1.items():
                    if n in p0 and not n.startswith('kcd2mp_') and 'guard' not in n.lower() and 'soldier' not in n.lower():   # a guard coming for a player is no villager
                        m = ((q[0] - p0[n][0]) ** 2 + (q[1] - p0[n][1]) ** 2) ** 0.5
                        if m > moved: walker, moved = n, m
                if walker and moved >= 3.0: break
            return walker, moved
        walker, moved = pick()
        peace(r, True)
        sync = bones_on(r)
        t0 = time.time(); follows = 0; walkers = [walker]
        while time.time() - t0 < secs:
            time.sleep(10)
            p = positions().get(walker) if walker else None
            hp = r.a.pos()
            if not p or (hp and ((p[0] - hp[0]) ** 2 + (p[1] - hp[1]) ** 2) ** 0.5 > 12):
                if not p:
                    walker, moved = pick(); p = positions().get(walker) if walker else None
                    if walker and walker not in walkers: walkers.append(walker)
                if p:
                    r.a.teleport(p[0] + 3.0, p[1] + 3.0); r.b.teleport(p[0] + 4.0, p[1] + 3.5); follows += 1
        t1 = time.time()
        bones_off(r, sync)
        danger = peace(r, False)
        pair = T.frame_pair(r, 'score-walker', walker) if walker else None
        row = window(r, 'village', t0, t1, walkers=walkers, follows=follows, danger=danger,
                     bones={k: {'seen': v['seen'], 'first': v['first']} for k, v in sync.items()}, pair=pair)
        return True, 'ok', row
    return fn


def tavern(r, secs=TAVERN_S):
    def fn():
        S, T = _S(), _T()
        host_time(r, 21.0)
        time.sleep(45)
        # the tavern's own keeper (the bench save's Troskowitz: ttkc_inkeeper serves there in the evening; the rig's older scenes stood by
        # ttkc_woman_2, who is in the street at this hour)
        t = r.a.ask(S.FIND_INN % os.environ.get('KCDMP_RIG_TAVERN', 'ttkc_inkeeper'), 'W207I', 6)
        if not t or t == 'none': return False, 'no-innkeeper', t
        parts = t.split(); inn, x, y, z = parts[0], float(parts[1]), float(parts[2]), float(parts[3])
        r.a.teleport(x + 1.2, y, z + 0.05)
        at = None
        for _ in range(6):
            r.b.teleport(x + 2.5, y + 1.0, z + 0.05)
            time.sleep(4)
            q = r.b.pos()
            if q and abs(q[0] - (x + 2.5)) < 3.0 and abs(q[1] - (y + 1.0)) < 3.0 and abs(q[2] - z) < 2.0: at = q; break
        # the evening's guests walk in after the clock's jump: the scene starts when at least three bodies besides the innkeeper are
        # within 18 m of the players (looked at every 10 s, 3 minutes at most), so the five minutes are the tavern's, not its filling up
        t_wait = time.time(); around = None; guests = 0
        while time.time() - t_wait < 180:
            time.sleep(10)
            t2 = (r.a.ask(S.FIND_INN % inn, 'W207I', 6) or '').split()   # the keeper still on his way: the players go with him
            if len(t2) >= 4 and ((float(t2[1]) - x) ** 2 + (float(t2[2]) - y) ** 2) ** 0.5 > 5.0:
                x, y, z = float(t2[1]), float(t2[2]), float(t2[3])
                r.a.teleport(x + 1.2, y, z + 0.05); r.b.teleport(x + 2.5, y + 1.0, z + 0.05)
                time.sleep(4)
            around = r.a.ask(T.SITTERS, 'W217T', 6) or ''
            guests = sum(1 for e_ in around.split(';') if '@' in e_ and not e_.startswith(inn) and 'guard' not in e_.lower())
            if guests >= 3 and time.time() - t_wait >= 20: break
        peace(r, True)
        t0 = time.time(); time.sleep(secs); t1 = time.time()
        danger = peace(r, False)
        ok, why, pair = sitter_frame(r, inn)
        row = window(r, 'tavern', t0, t1, innkeeper=inn, joiner_there=bool(at), guests_at_start=guests, waited_s=round(t0 - t_wait), host_npcs_18m=around,
                     danger=danger, pair=pair)
        return True, 'ok' if at else 'joiner-not-in-tavern', row
    return fn


def sitter_frame(r, inn):
    """A framed pair of one seated guest: the host NPC within 18 m whose full-body action is a sitting one (the game's own word for it),
    else the nearest that is neither the innkeeper nor a guard."""
    T = _T()
    q = ('local out = {}; local p = player:GetWorldPos(); for _, e in ipairs(System.GetEntitiesInSphere(p, 18) or {}) do '
         'local n = e.GetName and e:GetName() or ""; if (e.class == "NPC" or e.class == "NPC_Female") and e.soul and e.actor and not string.find(n, "kcd2mp_", 1, true) then '
         'local f = "?"; pcall(function() f = tostring(e.actor:GetCurrentAnimationState()) end); out[#out + 1] = n .. "@" .. f end end; '
         'System.LogAlways("[W220S] " .. (#out > 0 and table.concat(out, ";") or "none"))')
    names = r.a.ask(q, 'W220S', 6) or ''
    rows = [e_.split('@') for e_ in names.split(';') if '@' in e_]
    rows = [x for x in rows if not x[0].startswith(inn) and 'guard' not in x[0].lower()]
    best = next((x[0] for x in rows if 'sit' in x[1].lower()), None) or (rows[0][0] if rows else None)
    if not best: return True, 'no-subject', {'seen': names[:300]}
    f = T.frame_pair(r, 'score-sitter', best)
    f['seen'] = names[:300]
    return True, 'ok', f


def brawl(r, secs=BRAWL_S):
    def fn():
        S = _S()
        ok, why, info = S.s_w216_brawl(r, secs)()
        if not isinstance(info, dict) or 't0_ms' not in info:
            works(r, 'knockdown', None, 'the fight could not be staged (%s)' % why, info)
            return True, why, info
        r.bandits = info.get('bandits')
        window(r, 'fight', info['t0_ms'] / 1000.0, info['t1_ms'] / 1000.0, bandits=info.get('bandits'))
        falls = info.get('falls') or []
        fell = [f for f in falls if f.get('copy_fell_after_ms') is not None]
        up = [f for f in falls if f.get('copy_up_after_ms') is not None]
        if not falls: works(r, 'knockdown', None, 'no fall was staged', None)
        else:
            works(r, 'knockdown', bool(fell) and len(up) == len(fell) and len(fell) == len(falls),
                  '%d of %d falls shown, %d get-ups' % (len(fell), len(falls), len(up)),
                  [{k: f.get(k) for k in ('npc', 'copy_fell_after_host_ms', 'copy_up_after_host_ms', 'copy_fell_after_ms', 'copy_up_after_ms')} for f in falls])
        return True, 'ok', info
    return fn


# ---------------------------------------------------------------- the works-at-all checks (the joiner's side unless said)

LEAVE = 'pcall(function() if player.human:IsInDialog() then player.human:InterruptDialog() end end)'


def _in_dialog(c):
    return (c.ask(_T().IN_DIALOG, 'W217I', 3) or 'false').strip() == 'true'


def _leave(c, keys_ok):
    c.lua(LEAVE)
    t0 = time.time()
    while time.time() - t0 < 8:
        if not _in_dialog(c): return True
        time.sleep(1.0)
    if keys_ok: return _T().end_dialog(c)
    return not _in_dialog(c)


def _start_talk(c, npc, x, y, z, force=False):
    """c's player 1.3 m from <npc>, facing it; the class's own talk (the use key's function) or the forced dialogue. No key.
    Returns (dialogue id or None, the mark before the start, detail)."""
    T = _T()
    c.teleport(x + 1.3, y)
    time.sleep(3)
    ready = None
    if not force:
        tw = time.time()
        while time.time() - tw < 20:
            if 'can_talk=true' in (c.ask(T.READY % npc, 'W217RY', 3) or ''): ready = round(time.time() - tw, 1); break
            time.sleep(1.0)
    c.lua(T.AIM_AT % npc)
    time.sleep(1.0)
    m0 = c.mark()
    if force: c.ask(T.FORCE_DLG % npc, 'W217D', 4)
    else: c.lua(T.CLASS_TALK % npc)
    m, _dt = T._wait_any(c, m0['k'], T.ANY_DLG + '|' + T.CANCELLED, 25)
    if not m or re.search(T.CANCELLED, m.group(0)): return None, m0, {'npc': npc, 'ready_wait_s': ready, 'cancelled': bool(m)}
    return m.group(2), m0, {'npc': npc, 'ready_wait_s': ready, 'dialog': m.group(1)[-80:]}


def w_talk(r, keys_ok):
    """A talk to its choices: the joiner talks to a villager of the host's world; the game lists the dialogue's decisions."""
    def fn():
        T = _T()
        c = r.b
        r.a.teleport(*T.HOME); r.b.teleport(T.HOME[0] + 1.5, T.HOME[1] + 1.0)
        time.sleep(5)
        tried, rows = [], []
        for _ in range(4):
            t = r.a.ask(T.VILLAGER_NEAR % ', '.join('["%s"]=true' % n for n in tried + ['ttkc_drozd']), 'W217V', 6)
            if not t or t == 'none': break
            npc, x, y, z = t.split(); tried.append(npc)
            cid, m0, det = _start_talk(c, npc, float(x), float(y), float(z))
            if cid:
                m, dt = T._wait_any(c, m0['k'], r'\[ID: %s\] (\d+)\) (\S+)|\[ID: %s\] Dialog (ending|interrupted|ends)' % (cid, cid), 45)
                det['choices'] = bool(m and m.group(1)); det['after_s'] = round(dt, 1) if dt else None
            rows.append(det)
            _leave(c, keys_ok)
            time.sleep(2)
            if det.get('choices'): works(r, 'talk', True, 'the choices came', rows); return True, 'ok', rows
        if not tried: works(r, 'talk', None, 'no villager ready to talk on the host', rows)
        else: works(r, 'talk', False, 'no talk reached its choices (%d tried)' % len(tried), rows)
        return True, 'no-choices', rows
    return fn


def w_trade(r, keys_ok):
    """A trade screen: the joiner picks a shopkeeper's trade option (the player's own dialogue keys) and the shop's screen opens."""
    def fn():
        T, S = _T(), _S()
        if not keys_ok:
            works(r, 'trade', None, 'keys are off (KCDMP_RIG_NO_KEYS=1)'); return True, 'not-run', None
        c = r.b
        shops = r.a.ask(T.SHOPS_NEAR, 'W217S', 8)
        rows = []
        for ent in ([] if not shops or shops == 'none' else shops.split(';'))[:2]:
            npc, at = ent.split('@'); x, y, z = (float(v) for v in at.split(','))
            cid, m0, det = _start_talk(c, npc, x, y, z)
            if cid:
                end, chosen, scr = T.play_out(c, cid, prefer=r'shop\.nakupovani|obchod|\.trade|barter', secs=30, stop_rx=T.SCREEN_IN)
                m_open, _dt = T._wait_any(c, m0['k'], T.SCREEN_IN, 0.1 if scr else 12)
                det.update(chosen=chosen[-3:], screen=bool(scr or m_open))
                if det['screen']:
                    time.sleep(2.0)
                    det['frame'] = T.shot(r, c, 'score-shop')
                    import keys
                    k = keys.Keys(S.rec_of(c), 'wo220-shop-close')
                    det['esc'] = T.esc(c, k); k.release()
                    time.sleep(0.5)
            rows.append(det)
            _leave(c, keys_ok)
            time.sleep(2)
            if det.get('screen'): works(r, 'trade', True, 'the shop screen opened', rows); return True, 'ok', rows
        if not rows: works(r, 'trade', None, 'no shopkeeper ready to sell on the host', shops)
        else: works(r, 'trade', False, 'no shop screen (%d shopkeepers tried)' % len(rows), rows)
        return True, 'no-screen', rows
    return fn


def w_forced(r, keys_ok):
    """A forced dialogue: the nearest plain villager's dialogue forced on the joiner's player starts."""
    def fn():
        T = _T()
        c = r.b
        t = c.ask(T.FORCED_NEAR, 'W217FV', 6)
        if not t or t == 'none':
            works(r, 'forced', None, 'no plain villager near the joiner'); return True, 'not-run', None
        npc, x, y, z = t.split()
        cid, m0, det = _start_talk(c, npc, float(x), float(y), float(z), force=True)
        if any(re.search(T.SCREEN_IN, l) for l in c.lines(c.klog, m0['k'])) and keys_ok:
            import keys
            k = keys.Keys(_S().rec_of(c), 'wo220-forced-close'); det['esc'] = T.esc(c, k); k.release()
        _leave(c, keys_ok)
        works(r, 'forced', bool(cid), 'the dialogue started' if cid else 'no dialogue started', det)
        return True, 'ok' if cid else 'no-dialogue', det
    return fn


def w_drop(r):
    """A drop picked up by the other: each player drops food, the other picks it up; nothing duplicated or lost."""
    def fn():
        T = _T()
        ok, why, det = T.drops(r, 'score', kinds=('food',))()
        s = (det or {}).get('summary') or {}
        rows = [{k: x.get(k) for k in ('dropper', 'result', 'seen_by_other_s')} for x in (det or {}).get('rows', [])]
        if s.get('no-item') == len(rows) and rows: works(r, 'drop', None, 'no item to drop', rows)
        else: works(r, 'drop', s.get('ok') == len(rows) and bool(rows), '%s of %d drops taken by the other' % (s.get('ok'), len(rows)), rows)
        return True, why, s
    return fn


def w_door(r):
    """A door use: the joiner uses an unlocked door, then the host uses it; after each, both worlds show the same door state, changed."""
    def fn():
        T = _T()
        at = (2330.0, 2070.0, 110.5)
        r.a.teleport(at[0], at[1]); r.b.teleport(at[0] + 2.0, at[1]); time.sleep(6)
        h0 = T._doors(r.a, at, 120)
        rows = []
        for k in [k for k in sorted(h0) if h0[k][1] == 0][:3]:
            x, y, z = (float(v) for v in k.split(','))
            row = {'door': k, 'uses': []}
            for who, nm in ((r.b, 'joiner'), (r.a, 'host')):
                who.teleport(x + 1.0, y, z + 0.05); time.sleep(2.5)
                b = (T._doors(r.a, (x, y, z), 2), T._doors(r.b, (x, y, z), 2))
                used = who.ask(T.USE_DOOR % (x, y, z), 'W217DU', 5)
                time.sleep(3.0)
                a = (T._doors(r.a, (x, y, z), 2), T._doors(r.b, (x, y, z), 2))
                m = T._match(a[0], a[1])
                changed = sorted(b[0].values()) != sorted(a[0].values()) or sorted(b[1].values()) != sorted(a[1].values())
                row['uses'].append({'by': nm, 'used': used, 'changed': changed, 'same_after': bool(m) and all(x_['same'] for x_ in m)})
            rows.append(row)
            if all(u['changed'] and u['same_after'] for u in row['uses']):
                works(r, 'door', True, 'used by both, the same on both', rows); return True, 'ok', rows
        if not rows: works(r, 'door', None, 'no unlocked door near the tavern', {'doors': len(h0)})
        else: works(r, 'door', False, 'a use did not change the door on both (%d doors tried)' % len(rows), rows)
        return True, 'no-door', rows
    return fn


def w_mount(r):
    """A mount: a host NPC put on a host horse by the game's own mount; the joiner's copy of the rider is mounted, and off again."""
    def fn():
        S = _S()
        t = r.a.ask(S.FIND_INN % os.environ.get('KCDMP_RIG_INN', 'ttkc_woman_2'), 'W207I', 6)
        if not t or t == 'none':
            works(r, 'mount', None, 'no town'); return True, 'not-run', t
        parts = t.split(); x, y = float(parts[1]), float(parts[2])
        r.a.teleport(x + 6.0, y + 6.0); time.sleep(5)
        pick = (r.a.ask(S.W216_HORSE_PICK, 'W216H', 6) or 'none none').split()
        horse, rider = pick[0], pick[1] if len(pick) > 1 else 'none'
        if horse == 'none' or rider == 'none':
            works(r, 'mount', None, 'no horse or rider on the host', pick); return True, 'not-run', pick
        jx, jy = x + 20.0, y + 6.0
        put_joiner(r, jx, jy); r.a.teleport(jx - 4.0, jy + 8.0)
        a0 = (jx - 8.0, jy + 3.0)
        r.a.lua(S.PLACE_NPC % (horse, a0[0], a0[1], a0[0], a0[1]))
        r.a.lua(S.PLACE_NPC % (rider, a0[0] + 1.0, a0[1], a0[0] + 1.0, a0[1]))
        time.sleep(5)
        r.a.lua('local r = System.GetEntityByName("%s"); local h = System.GetEntityByName("%s"); if r and h then pcall(function() r.human:ForceMount(h.id) end) end' % (rider, horse))

        def state(c):
            q = (c.ask(S.W216_RIDE_Q % (rider, horse), 'W216R', 4) or '').split()
            return int(q[0]) if len(q) == 4 else -1

        def until(c, want, secs):
            t0 = time.time()
            while time.time() - t0 < secs:
                if state(c) == want: return round(time.time() - t0, 1)
                time.sleep(1.0)
            return None
        det = {'horse': horse, 'rider': rider, 'host_mounted_s': until(r.a, 1, 20)}
        if det['host_mounted_s'] is None:
            works(r, 'mount', None, 'the host\'s rider did not mount', det); return True, 'not-run', det
        det['copy_mounted_s'] = until(r.b, 1, 25)
        r.a.lua('local r = System.GetEntityByName("%s"); if r then pcall(function() r.human:ForceDismount() end) end' % rider)
        det['host_off_s'] = until(r.a, 0, 20)
        det['copy_off_s'] = until(r.b, 0, 25) if det['copy_mounted_s'] is not None else None
        ok = det['copy_mounted_s'] is not None and det['copy_off_s'] is not None
        works(r, 'mount', ok, 'the copy mounted and dismounted' if ok else 'the copy did not follow the mount', det)
        return True, 'ok' if ok else 'no-mount', det
    return fn


def w_loot(r):
    """A loot take: the joiner takes one item from a chest (its own ledger) and one from a body through the host (request, grant)."""
    def fn():
        S = _S()
        ok, why, det = S.s_loot(r)()
        staged = why not in ('no-chest', 'chest-out-of-reach', 'body-no-body', 'no-body', 'body-out-of-reach', 'body-body-out-of-reach')
        works(r, 'loot', ok if staged else None, why, det if not ok else None)
        return True, why, det
    return fn


def w_sleep(r):
    """A sleep vote: the joiner asks, the host says yes, the skip begins and ends on the host."""
    def fn():
        S = _S()
        ok, why, det = S.s_skip(r, 'sleep', 9, gap_gate=False)()
        works(r, 'sleep', bool(ok), why, det if not ok else {k: det.get(k) for k in ('skip_s', 'gap_s_at')} if isinstance(det, dict) else det)
        return True, why, det
    return fn


def w_join(r):
    def fn():
        j = next((s for s in r.steps if s['step'] == 'join'), None)
        works(r, 'join', bool(j and j['ok']), (j or {}).get('reason', 'no join step'))
        return True, 'ok', None
    return fn


LOW_GROUPS = ('Characters', 'GlobalIllumination', 'Light', 'ObjectDetail', 'Particles', 'PostProcessing', 'Shading', 'Shadows', 'Vegetation',
              'VolumetricEffects')


def low_settings(r):
    """The copies named in KCDMP_RIG_LOW at the lowest quality: every quality group set to 1 through the console once the world is up (the
    profile's own values are not what the game applies at a load: the first runs' joiner stayed at 2 with a profile that said 1)."""
    def fn():
        import mirror
        done = {}
        q = 'System.LogAlways("[W220L] " .. tostring(System.GetCVar("sys_spec_Shading")) .. tostring(System.GetCVar("sys_spec_Vegetation")))'
        for c in (r.a, r.b):
            if c.id not in mirror.LOW: continue
            # asked again until the copy says it took (right after a join the joiner's console can stay unanswered for half a minute)
            for k in range(8):
                for g in LOW_GROUPS: c.cmd('sys_spec_%s 1' % g)
                time.sleep(2)
                if (c.ask(q, 'W220L', 5) or '') == '11': done[c.id] = k + 1; break
                time.sleep(5)
        time.sleep(8)
        missing = [c for c in mirror.LOW if c not in done]
        return not missing, 'ok' if not missing else 'not-taken', {'low_after_tries': done, 'not_taken': missing}
    return fn


def gfx(r):
    """What each copy runs at (the bench's joiner at low settings): the quality groups as the engine has them now. Asked again when
    a copy does not answer (right after a join the joiner's console can stay unanswered for half a minute)."""
    def fn():
        q = ('local out = {}; for _, k in ipairs({"sys_spec_Shading", "sys_spec_Shadows", "sys_spec_ObjectDetail", "sys_spec_Vegetation", "r_Width", "r_Height"}) do '
             'out[#out + 1] = k .. "=" .. tostring(System.GetCVar(k)) end; System.LogAlways("[W220G] " .. table.concat(out, " "))')
        out = {}
        for nm, c in (('host', r.a), ('joiner', r.b)):
            for _ in range(4):
                out[nm] = c.ask(q, 'W220G', 5)
                if out[nm]: break
                time.sleep(5)
        with open(os.path.join(r.dir, 'score-gfx.json'), 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)
        return True, 'ok', out
    return fn


def plans(r, name, args):
    S, T = _S(), _T()
    import scen218
    keys_ok = os.environ.get('KCDMP_RIG_NO_KEYS') != '1'
    return {
        'score': [('join', S.s_join(r)), ('works-join', w_join(r)), ('cost-on', scen218.cost(r, True)), ('ignore-player', ignore_player(r, True)),
                  ('low-settings', low_settings(r)), ('day', T.daytime(r, 10.0)), ('gfx', gfx(r)),
                  ('smithy', smithy(r)), ('village', village(r)), ('tavern', tavern(r)),
                  ('ignore-player-off', ignore_player(r, False)), ('cost-off', scen218.cost(r, False))],
        'score-works': [('join', S.s_join(r)), ('works-join', w_join(r)), ('ignore-player', ignore_player(r, True)),
                        ('low-settings', low_settings(r)), ('day', T.daytime(r, 10.0)),
                        ('works-drop', w_drop(r)), ('works-door', w_door(r)), ('works-mount', w_mount(r)),
                        ('works-talk', w_talk(r, keys_ok)), ('works-trade', w_trade(r, keys_ok)), ('works-forced', w_forced(r, keys_ok)),
                        ('works-sleep', w_sleep(r)), ('fight', brawl(r)), ('works-loot', w_loot(r)),
                        ('ignore-player-off', ignore_player(r, False))],
    }.get(name)


CONTINUE_ON_FAIL = ('score', 'score-works')
