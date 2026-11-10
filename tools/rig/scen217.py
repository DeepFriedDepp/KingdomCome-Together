# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-217: the work order's bench scenes on two real copies (test tool, never shipped).

Every action goes through the game's console, Lua or our pipe (the agent); keys only where a player's own key is the thing under test
(the maintainer allows the bench to drive his keyboard and mouse; send-only, tools/rig/keys.py). Nothing here reads input. Frames are the
bench's own r_GetScreenShot of the bench's two games, into the run's folder (research), never shipped.

Steps (each returns (ok, reason, detail); a step is evidence, the WO's gates are the safety ones and the join):
    p0_role(r, who)          Part 0.1: as <who> (host|joiner) a talk to the end, a trade with one sale, a forced dialogue
    p0_sitters(r, secs)      Part 0.2: the tavern by night, the joiner 5-10 m from seated guests, <secs>
    drops(r, label)          G.1: each player drops a weapon, food and groschen for the other; the other picks each up; a ledger
    frame_pair(r, label, subject)   both views of one subject at the same moment, each viewer 3-8 m from it with a clear line
"""
import json, os, re, sys, threading, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mirror, rig


def _S():
    import scenarios   # the step helpers (FIND_INN, TALKABLE, OWN_TALK ...): imported late (scenarios imports this module)
    return scenarios


def roles(r, who):
    return (r.b, r.a) if who == 'joiner' else (r.a, r.b)


def note(r, _ev, **kw):
    rig.note(_ev, **{('item_kind' if k in ('kind', 'event') else k): v for k, v in kw.items()})
    with open(os.path.join(r.dir, 'w217-events.jsonl'), 'a', encoding='utf-8') as f:
        f.write(json.dumps(dict(ev=_ev, t=round(time.time(), 3), **kw)) + '\n')


# ---------------------------------------------------------------- frames: the subject framed, both views at one moment

# A spot 3-8 m from the subject with a clear line from the viewer's eye to the subject's chest (Physics.RayTraceCheck, the engine's own
# segment test), on the subject's own floor (indoors the subject's height; outdoors the terrain's when it is within 1 m of the subject's).
# Logs [W217F] <x> <y> <z> <yaw> <dist> <tries>, or [W217F] none.
FRAME_SPOT = ('local e = System.GetEntityByName("%s"); if not e then System.LogAlways("[W217F] none nobody"); return end; '
              'local q = e:GetWorldPos(); local best; local tries = 0; '
              'for _, dist in ipairs({5.0, 4.0, 6.5, 3.2, 8.0}) do for k = 0, 11 do tries = tries + 1; local a = k * math.pi / 6 + %f; '
              'local x, y = q.x + math.cos(a) * dist, q.y + math.sin(a) * dist; local z = q.z; '
              'local tz = System.GetTerrainElevation({x = x, y = y, z = 0}) or q.z; if math.abs(tz - q.z) < 1.0 then z = math.max(tz, q.z) end; '
              'local clear = false; pcall(function() clear = Physics.RayTraceCheck({x = x, y = y, z = z + 1.65}, {x = q.x, y = q.y, z = q.z + 1.1}, player.id, e.id) end); '
              'if clear then best = {x, y, z, dist}; break end end; if best then break end end; '
              'if not best then System.LogAlways("[W217F] none blocked " .. tries); return end; '
              'local yaw = math.atan2(-(q.x - best[1]), q.y - best[2]); '
              'System.LogAlways(string.format("[W217F] %%.2f %%.2f %%.2f %%.4f %%.1f %%d", best[1], best[2], best[3], yaw, best[4], tries))')

PLACE_VIEW = ('player:SetWorldPos({x = %f, y = %f, z = %f}); pcall(function() player.actor:PlayerSetViewAngles({x = %f, y = 0, z = %f}) end)')
AIM_AT = ('local e = System.GetEntityByName("%s"); if e then local p = player:GetWorldPos(); local q = e:GetWorldPos(); '
          'local yaw = math.atan2(-(q.x - p.x), q.y - p.y); local d = math.sqrt((q.x - p.x) ^ 2 + (q.y - p.y) ^ 2); '
          'local pitch = math.atan2((q.z + 1.0) - (p.z + 1.65), math.max(d, 0.5)); '
          'pcall(function() player.actor:PlayerSetViewAngles({x = pitch, y = 0, z = yaw}) end); '
          'System.LogAlways(string.format("[W217A] %%.1f", d)) else System.LogAlways("[W217A] -1") end')


VITALS = ('pcall(function() local s = player.soul; s:SetState("health", 100); s:SetState("stamina", 100); s:SetState("exhaust", 100); '
          's:SetState("hunger", 100) end)')


def shot(r, c, label):
    """One frame of copy c's view into the run folder as frame-<label>.jpg (r_GetScreenShot 1: a ~0.3 s stall, never inside a window)."""
    import shutil
    before = time.time()
    try: c.cmd('r_GetScreenShot 1')
    except Exception as e: return {'error': str(e)}
    for _ in range(20):
        time.sleep(1)
        for name in mirror.user_folders(c.id):
            d = os.path.join(mirror.SAVED_GAMES, name, 'screenshots')
            if not os.path.isdir(d): continue
            new = [os.path.join(d, f) for f in os.listdir(d) if f.lower().endswith('.jpg') and os.path.getmtime(os.path.join(d, f)) >= before - 1]
            if not new: continue
            src = max(new, key=os.path.getmtime)
            n0 = os.path.getsize(src); time.sleep(1.2)
            if os.path.getsize(src) != n0 or n0 == 0: continue
            dst = os.path.join(r.dir, 'frame-%s.jpg' % label)
            shutil.copy2(src, dst)
            return {'file': os.path.basename(dst), 'bytes': n0}
    return {'error': 'no file'}


def frame_pair(r, label, subject, spot_from='host', move=True, phase=0.0):
    """The host's view and the joiner's of <subject> at the same moment. With move, the viewers are put on one spot 3-8 m from the subject
    with a clear line (found on <spot_from>'s world), the joiner 1.1 m to the host's side, both facing it; without, each only turns to it. The two frames are
    taken together (two threads). Returns the spots and files; the frames are looked at before the scene's numbers are trusted."""
    out = {'label': label, 'subject': subject}
    for c in (r.a, r.b): c.lua(VITALS)   # the bench's Henrys rested and fed for the picture (a red, starving view frames nothing)
    if move:
        src = r.a if spot_from == 'host' else r.b
        t = src.ask(FRAME_SPOT % (subject, phase), 'W217F', 6)
        out['spot'] = t
        if not t or t.startswith('none'):
            for c in (r.a, r.b): c.lua(AIM_AT % subject)
        else:
            x, y, z, yaw, dist, _n = t.split()
            import math
            # the joiner 1.1 m to the host's side (a1-b7: on the same spot each camera sat inside the other's body)
            sx, sy = math.cos(float(yaw)) * 1.1, math.sin(float(yaw)) * 1.1
            for c, ox, oy in ((r.a, 0.0, 0.0), (r.b, sx, sy)):
                c.lua(PLACE_VIEW % (float(x) + ox, float(y) + oy, float(z) + 0.05, -0.12, float(yaw)))
            time.sleep(2.5)
            for c in (r.a, r.b): c.lua(AIM_AT % subject)
    else:
        for c in (r.a, r.b): c.lua(AIM_AT % subject)
    time.sleep(1.5)
    res = {}
    th = [threading.Thread(target=lambda c=c, k=k: res.__setitem__(k, shot(r, c, '%s-%s' % (label, k)))) for c, k in ((r.a, 'host'), (r.b, 'joiner'))]
    for t_ in th: t_.start()
    for t_ in th: t_.join(timeout=40)
    out.update(res)
    note(r, 'w217-frame', **out)
    return out


# ---------------------------------------------------------------- Part 0.1: talk, trade, forced dialogue, by role

HOME = (2176.11, 2091.96)   # save440's own spot, by Troskowitz's villagers
SHOPS_NEAR = ('local out = {}; local p = player:GetWorldPos(); for _, e in ipairs(System.GetEntitiesInSphere(p, 400) or {}) do '
              'if e.soul and e.OnTalk and e ~= player then local n = e:GetName(); local ready = false; '
              'pcall(function() ready = e.soul:HasScriptContext("shop_sellerReadyToSell") == true end); '
              'if ready and not string.find(n, "kcd2mp_", 1, true) then local q = e:GetWorldPos(); '
              'out[#out + 1] = string.format("%s@%.2f,%.2f,%.2f", n, q.x, q.y, q.z) end end end; '
              'System.LogAlways("[W217S] " .. (#out > 0 and table.concat(out, ";") or "none"))')
READY = ('local e = System.GetEntityByName("%s"); local s = "none"; if e and KCD2MP_W167Readiness then s = KCD2MP_W167Readiness(e).line end; '
         'System.LogAlways("[W217RY] " .. s)')
VILLAGER_NEAR = ('local ex = {%s}; local p = player:GetWorldPos(); local best, bd; for _, e in ipairs(System.GetEntitiesInSphere(p, 400) or {}) do '
                 'local n = e.GetName and e:GetName() or ""; if (e.class == "NPC" or e.class == "NPC_Female") and e.OnTalk and e.soul and not ex[n] '
                 'and not string.find(n, "kcd2mp_", 1, true) and not string.find(n, "DialogTwin_", 1, true) and not string.find(string.lower(n), "guard", 1, true) '
                 'and not string.find(string.lower(n), "straz", 1, true) then local h = 0; pcall(function() h = e.soul:GetState("health") end); '
                 'local rdy = true; if KCD2MP_W167Readiness then pcall(function() rdy = KCD2MP_W167Readiness(e).prompt == "yes" end) end; '
                 'local q = e:GetWorldPos(); local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; if rdy and h and h > 0 and (not bd or dd < bd) then bd = dd; best = e end end end; '
                 'if best then local q = best:GetWorldPos(); System.LogAlways(string.format("[W217V] %%s %%.2f %%.2f %%.2f", best:GetName(), q.x, q.y, q.z)) '
                 'else System.LogAlways("[W217V] none") end')
# WO-217 r9: the forced dialogue's speaker -- the nearest plain villager (a numbered man / woman: no quest of his own), alive, not ours;
# the dialogue restriction does not matter to a forced dialogue, so no readiness check
FORCED_NEAR = ('local p = player:GetWorldPos(); local best, bd; for _, e in ipairs(System.GetEntitiesInSphere(p, 400) or {}) do '
               'local n = e.GetName and e:GetName() or ""; if (e.class == "NPC" or e.class == "NPC_Female") and e.soul '
               'and (string.match(n, "^%a+_man_%d+$") or string.match(n, "^%a+_woman_%d+$")) then local h, shop = 0, false; '
               'pcall(function() shop = e.soul:HasScriptContext("shop_sellerReadyToSell") == true end); '
               'pcall(function() h = e.soul:GetState("health") end); local q = e:GetWorldPos(); local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; '
               'if h and h > 0 and not shop and (not bd or dd < bd) then bd = dd; best = e end end end; '
               'if best then local q = best:GetWorldPos(); System.LogAlways(string.format("[W217FV] %s %.2f %.2f %.2f", best:GetName(), q.x, q.y, q.z)) '
               'else System.LogAlways("[W217FV] none") end')
CLASS_TALK = ('local e = System.GetEntityByName("%s"); if e then local f = BasicAIActions and BasicAIActions.OnTalk or e.OnTalk; f(e, player, 0) end')
FORCE_DLG = ('local e = System.GetEntityByName("%s"); local how = "none"; if e then '
             'if DialogModule and DialogModule.ForceDialog then local ok, v = pcall(DialogModule.ForceDialog, e.id, player.id); how = "force:" .. tostring(ok) .. ":" .. tostring(v) end end; '
             'System.LogAlways("[W217D] " .. how)')
IN_DIALOG = ('local d = false; pcall(function() d = player.human:IsInDialog() == true end); System.LogAlways("[W217I] " .. tostring(d))')
MONEY = ('local m = -1; pcall(function() m = player.inventory:GetMoney() end); local n = 0; pcall(function() n = #(player.inventory:GetInventoryTable() or {}) end); '
         'System.LogAlways(string.format("[W217M] %d %d", m, n))')
ANY_DLG = r"New dialogue '([^' ]*)[^']* \((?:normal|ingame)[^)]* - Id: (\d+)\) \((?!AI::DoMonologue|CombatShout)([^)]*)\)'.*souls = '[^']*\bEx: Dude\b"
SCREEN_IN = r"ui_inv_screen_in|WO201-INV open|ApseOpen|Libs/UI//ApseInventoryList\.gfx|Readiness observer 'UICharacterFader' with category bitmask '129' started"   # the trade screen, every time it opens (the file is opened only the first time)
SCREEN_OUT = r"ui_inv_screen_out|WO201-INV close|ApseClose"


def _wait_any(c, off, rx, secs, step=0.1):
    t0 = time.time()
    while time.time() - t0 < secs:
        for l in c.lines(c.klog, off):
            m = re.search(rx, l)
            if m: return m, time.time() - t0
        time.sleep(step)
    return None, None


def play_out(c, cid, prefer=None, secs=150, stop_rx=None):
    """A dialogue played to its end with the dialogue keys in the talker's own window (WO-207's way): at each decision (the game's own
    "[ID: n] k) option" list) the option matching <prefer> (a trade's), else the last one (usually the goodbye): Down to it from the first
    row (the selected one), or Up once for the last (it wraps), then the use key; a line with no decision for 8 s is skipped with the use
    key. Returns (ending line or None, the options chosen, the stop match when <stop_rx> came first)."""
    import keys
    k = keys.Keys(_S().rec_of(c), 'wo217-dlg')
    off = c.size(c.klog)
    chosen, done = [], set()
    t0 = time.time()
    block, block_at = {}, None
    last_move = time.time()
    try:
        while time.time() - t0 < secs:
            for l in c.lines(c.klog, off):
                m = re.match(r'\[ID: %s\] (\d+)\) (\S+)' % cid, l)
                if m:
                    block[int(m.group(1))] = m.group(2); block_at = time.time()
                if re.search(r'\[ID: %s\] Dialog (ending|interrupted|ends)' % cid, l): return l.strip()[:160], chosen, None
                if stop_rx:
                    s = re.search(stop_rx, l)
                    if s: return None, chosen, s.group(0)
            off = c.size(c.klog)
            if block and time.time() - block_at > 0.8:
                opts = [block[i] for i in sorted(block)]
                key = (tuple(opts), len(chosen))
                if key not in done:
                    done.add(key)
                    idx = None
                    if prefer: idx = next((i for i, o in enumerate(opts) if re.search(prefer, o)), None)
                    if idx is None: idx = len(opts) - 1
                    time.sleep(0.6)
                    if idx == 0: k.front()
                    elif idx == len(opts) - 1 and len(opts) > 2: k.press('up')
                    else: k.press('down', n=idx)
                    k.press('confirm')
                    chosen.append(opts[idx])
                    last_move = time.time()
                block, block_at = {}, None
            elif not block and time.time() - last_move > 8.0:
                # r10: the use key only inside a dialogue (outside one it sat the host's Henry on a bench for the rest of acc2-b12)
                if (c.ask(IN_DIALOG, 'W217I', 3) or 'false').strip() != 'true': return 'not-in-dialog', chosen, None
                k.press('confirm'); last_move = time.time()   # a line with no decision: skipped (the use key)
            time.sleep(0.2)
        return None, chosen, None
    finally:
        k.release()


FRONT_OF = ('local e = System.GetEntityByName("%s"); if e then local q = e:GetWorldPos(); local d = e:GetDirectionVector(1); '
            'local n = math.sqrt(d.x * d.x + d.y * d.y); if n > 0.01 then d = {x = d.x / n, y = d.y / n} else d = {x = 0, y = 1} end; '
            'player:SetWorldPos({x = q.x + d.x * 1.3, y = q.y + d.y * 1.3, z = q.z + 0.05}) end')
CANCELLED = r"Canceling dialog request id \d+ from soul 'Dude'"


DATA_T = re.compile(r'\[KCD2-MP-DATA\] v2 \d+ ([\d.]+) ')


def game_start_s(lines, npc, cid):
    """The start as the game saw it: the mod's own WO137-TALK after_s (the joiner's copies), else the DATA lines' frame clock from the
    press (OnTalk / the forced call) to the dialogue's "New dialogue ... Id: <cid>" line. None when neither is there."""
    for l in lines:
        m = re.search(r'WO137-TALK (?:start|forced) npc=%s id=%s(?: after_s=([\d.]+))?' % (re.escape(npc), cid), l)
        if m: return float(m.group(1)) if m.group(1) else 0.0
    t_press = t_start = None
    last = None
    for l in lines:
        d = DATA_T.search(l)
        if d: last = float(d.group(1)); continue
        if t_press is None and (('OnTalk: Dude->%s' % npc) in l or '[W217D]' in l): t_press = last
        if t_press is not None and re.search(r"New dialogue .* - Id: %s\)" % cid, l): t_start = last; break
    if t_press is not None and t_start is not None: return round(max(0.0, t_start - t_press), 2)
    # a forced dialogue starts inside the call (its "New dialogue" line comes before the call's own [W217D] answer): no wait at all
    if any(re.search(r"New dialogue .* - Id: %s\)" % cid, l) for l in lines) and any('[W217D] force:true' in l for l in lines): return 0.0
    return None


def esc(c, k):
    """r11: one Esc in c's window (k: its keys); when the press opened the game's pause menu -- nothing else was open to close (WO-138's
    InGameMenu line in c's native log) -- a second Esc closes it again. Returns 'ok' or 'menu-closed'."""
    m0 = c.mark()
    k.press('back'); time.sleep(1.5)
    if any(re.search(r'source=7\(InGameMenu\) pause=1', l) for l in c.lines(c.nlog, m0['n'])):
        k.press('back'); time.sleep(1.0)
        return 'menu-closed'
    return 'ok'


def end_dialog(c, secs=8):
    """Whatever dialogue this player is in is closed before the next step: the game's own interrupt, then Back in that copy's window."""
    c.lua('pcall(function() if player.human:IsInDialog() then player.human:InterruptDialog() end end)')
    t0 = time.time()
    while time.time() - t0 < secs:
        if (c.ask(IN_DIALOG, 'W217I', 3) or 'false').strip() != 'true': return True
        time.sleep(1.0)
    import keys
    k = keys.Keys(_S().rec_of(c), 'wo217-enddlg')
    for _ in range(2):   # r11: the second Esc only while still in the dialogue (after it, an Esc opens the pause menu)
        esc(c, k)
        if (c.ask(IN_DIALOG, 'W217I', 3) or 'false').strip() != 'true': break
    k.release()
    return (c.ask(IN_DIALOG, 'W217I', 3) or 'false').strip() != 'true'


def talk_once(c, npc, x, y, z, how='talk'):
    """c's player 1.3 m from <npc>, facing it (the dialogue's request checks the player's view), then the class's own talk -- the use
    key's own function (or the forced dialogue). A request the game cancels is tried once more the player's own way: the use key in that
    copy's window. Returns (cid, start_s, origin, detail)."""
    # WO-207's proven staging: 1.3 m east of it, 3 s to settle, aimed at it (FRONT_OF -- along its own facing -- only for the retry: it
    # put the player inside the innkeeper's counter in p0c)
    c.teleport(x + 1.3, y)
    time.sleep(3)
    ready_wait = None
    if how != 'force':   # r10: a talk waits (up to 20 s) for this world's NPC to read can_talk=true -- a copy not streamed yet reads false
        tw = time.time()
        while time.time() - tw < 20:
            if 'can_talk=true' in (c.ask(READY % npc, 'W217RY', 3) or ''): ready_wait = round(time.time() - tw, 1); break
            time.sleep(1.0)
    c.lua(AIM_AT % npc)
    time.sleep(1.0)
    m0 = c.mark()
    t0 = time.time()
    how_used = how
    if how == 'force': c.ask(FORCE_DLG % npc, 'W217D', 4)
    else: c.lua(CLASS_TALK % npc)
    m, dt = _wait_any(c, m0['k'], ANY_DLG + '|' + CANCELLED, 25)
    if m and re.search(CANCELLED, m.group(0)) and how != 'force':
        import keys
        S = _S()
        k = keys.Keys(S.rec_of(c), 'wo217-talk')
        c.lua(FRONT_OF % npc); time.sleep(2.0)
        c.lua(AIM_AT % npc); time.sleep(0.8)
        m0 = c.mark(); t0 = time.time(); how_used = 'use-key'
        k.press('confirm'); k.release()
        m, dt = _wait_any(c, m0['k'], ANY_DLG + '|' + CANCELLED, 25)
    if m and re.search(CANCELLED, m.group(0)): m = None
    lines = [l for l in c.lines(c.klog, m0['k']) if re.search(r'OnTalk|requested dialog|Attempting to start|Cancel|timed out|New dialogue|WO157|WO216|WO160-CONV|W217D', l)][:12]
    if not m: return None, None, None, {'npc': npc, 'how': how_used, 'ready_wait_s': ready_wait, 'lines': [l[:180] for l in lines]}
    gs = game_start_s(c.lines(c.klog, m0['k']), npc, m.group(2))
    # start_s is the game's (press to "New dialogue"); bench_s the console call's (the bench's queue in it)
    return m.group(2), gs, m.group(3), {'npc': npc, 'dialog': m.group(1)[-80:], 'how': how_used, 'ready_wait_s': ready_wait, 'bench_s': round(time.time() - t0, 2),
                                         'lines': [l[:180] for l in lines]}


def p0_role(r, who, sale_keys=os.environ.get('KCDMP_RIG_SALE_KEYS', 'a,wait1.5,e,wait1.5,e,wait1.5,x,wait2')):   # the shop's own hints: A the player's side, E to the basket, E again (WO-218 D.5: a stack asks how many -- its dialog's E confirms), X close the deal
    """Part 0.1 as <who>: (1) a talk with a villager played to its end, (2) Trade with a shopkeeper of the host's world (its dialogue's trade
    option) and one sale, (3) the nearest plain villager's forced dialogue (DialogModule.ForceDialog: speaker the NPC, listener the player)
    played to its end. Each must start within 2 s and finish. The counts of WO216-NORESUME / WO216-EXEMPT and the census (the joiner's
    WO216-STATUS) are read for the window after."""
    def fn():
        c, o = roles(r, who)
        S = _S()
        r.a.teleport(*HOME); r.b.teleport(HOME[0] + 1.5, HOME[1] + 1.0)
        time.sleep(5)
        mb0, ma0 = r.b.mark(), r.a.mark()
        rows = []
        # (1) the talk
        tried = []
        row = {'what': 'talk'}
        for _ in range(5):
            # the host's world says who would talk now (a copy here reads CanTalk false while frozen: p0d's census, every copy)
            t = r.a.ask(VILLAGER_NEAR % ', '.join('["%s"]=true' % n for n in tried + ['ttkc_drozd']), 'W217V', 6)
            if not t or t == 'none': break
            npc, x, y, z = t.split(); tried.append(npc)
            row.setdefault('ready', []).append({'npc': npc, 'host': r.a.ask(READY % npc, 'W217RY', 4), 'here': c.ask(READY % npc, 'W217RY', 4)})
            cid, st, origin, det = talk_once(c, npc, float(x), float(y), float(z))
            row.update(det)
            if cid:
                row.update(cid=cid, start_s=st, origin=origin)
                if os.environ.get('KCDMP_RIG_P0_FRAMES', '1') == '1':   # the talking villager, both views (aimed only: nobody moves mid-talk)
                    row['pair'] = frame_pair(r, 'p0-%s-talk' % who, npc, move=False)
                end, chosen, _ = play_out(c, cid)
                row.update(ended=end, decisions=len(chosen))
                break
            row.setdefault('refused', []).append(npc)
        if not row.get('cid'):
            shops0 = r.a.ask(SHOPS_NEAR, 'W217S', 8) or ''
            for ent in [e for e in shops0.split(';') if '@' in e][1:4]:   # not the first: the trade step's
                npc, at = ent.split('@'); x, y, z = (float(v) for v in at.split(','))
                row['fallback'] = 'merchant-goodbye'
                cid, st, origin, det = talk_once(c, npc, x, y, z)
                row.update(det)
                if not cid: row.setdefault('refused', []).append(npc); continue
                row.update(cid=cid, start_s=st, origin=origin)
                end, chosen, _ = play_out(c, cid, prefer=r'(?i)nashledanou|sbohem|rozlouc|goodbye|konec')
                row.update(ended=end, decisions=len(chosen), chosen=chosen[-3:])
                break
        row['ok'] = bool(row.get('cid')) and (99 if row.get('start_s') is None else row['start_s']) <= 2.0 and bool(row.get('ended'))
        rows.append(row); note(r, 'w217-p0', role=who, **row)
        row['closed'] = end_dialog(c)
        time.sleep(3)
        # (2) the trade (a shopkeeper the host's world has ready to sell, by day); the talker carries something to sell
        row = {'what': 'trade'}
        row['sell_item'] = c.ask(CREATE % (KNOWN['food'][0], 3), 'W217C', 5)
        shops = r.a.ask(SHOPS_NEAR, 'W217S', 8)
        row['host_shops'] = shops
        for ent in ([] if not shops or shops == 'none' else shops.split(';'))[:4]:
            npc, at = ent.split('@'); x, y, z = (float(v) for v in at.split(','))
            money0 = c.ask(MONEY, 'W217M', 4)
            m_scr = c.mark()
            cid, st, origin, det = talk_once(c, npc, x, y, z)
            row.update(det)
            if not cid: row.setdefault('refused', []).append(npc); continue
            row.update(cid=cid, start_s=st, origin=origin, money_before=money0)
            end, chosen, scr = play_out(c, cid, prefer=r'shop\.nakupovani|obchod|\.trade|barter', secs=60, stop_rx=SCREEN_IN)
            row.update(chosen=chosen[-3:], ended=end)
            m_open, dt = _wait_any(c, m_scr['k'], SCREEN_IN, 0.1 if scr else 12)
            row['screen'] = bool(scr or m_open)
            if row['screen']:
                time.sleep(2.0)
                row['frame_shop'] = shot(r, c, 'p0-%s-shop' % who)
                if sale_keys:   # the player's own keys in the shop screen (calibrated from the frame): e.g. "e,space" -- sent to this copy only
                    import keys
                    k = keys.Keys(S.rec_of(c), 'wo217-sale')
                    for key in sale_keys.split(','):
                        if key.startswith('wait'): time.sleep(float(key[4:] or 1)); continue
                        try:
                            k.front(); k.g.tap(key, 90)
                        except KeyError:
                            row.setdefault('keys_unknown', []).append(key)   # never the step's end (acc1-b12: no X in the table)
                        time.sleep(0.8)
                    k.release()
                    time.sleep(1.5)
                    row['frame_sale'] = shot(r, c, 'p0-%s-sale' % who)
                money1 = c.ask(MONEY, 'W217M', 4)
                row['money_after'] = money1
                # leave the screen the player's way (Esc in this copy's window), then the talk ends by itself or by its goodbye
                import keys
                k = keys.Keys(S.rec_of(c), 'wo217-close')
                row['esc'] = esc(c, k); k.release()
                time.sleep(0.5)
                end2, chosen2, _ = play_out(c, cid, secs=40)
                row['ended'] = row.get('ended') or end2 or 'closed'
            break
        try:
            m0, m1 = (int(row['money_before'].split()[0]), int(row['money_after'].split()[0])) if row.get('money_after') else (None, None)
        except (AttributeError, ValueError): m0 = m1 = None
        row['sold'] = m0 is not None and m1 is not None and m1 > m0
        row['ok'] = bool(row.get('cid')) and (99 if row.get('start_s') is None else row['start_s']) <= 2.0 and row.get('screen', False)
        rows.append(row); note(r, 'w217-p0', role=who, **row)
        row['closed'] = end_dialog(c)
        time.sleep(3)
        # (3) the forced dialogue: the nearest plain villager (r9: never a quest giver -- the bailiff's dialogue played the bench save's
        # own quest scene, a fast travel and a story dialogue that stayed open for the rest of the run)
        row = {'what': 'forced'}
        t = c.ask(FORCED_NEAR, 'W217FV', 6)
        if t and t != 'none':
            npc, x, y, z = t.split()
            m_f = c.mark()
            cid, st, origin, det = talk_once(c, npc, float(x), float(y), float(z), how='force')
            row.update(det)
            if cid:
                row.update(cid=cid, start_s=st, origin=origin)
                end, chosen, _ = play_out(c, cid)
                row.update(ended=end, decisions=len(chosen))
            if any(re.search(SCREEN_IN, l) for l in c.lines(c.klog, m_f['k'])):   # r12: a shop the forced dialogue opened is closed
                import keys
                k = keys.Keys(_S().rec_of(c), 'wo217-forced-close'); row['esc'] = esc(c, k); k.release()
        row['ok'] = bool(row.get('cid')) and (99 if row.get('start_s') is None else row['start_s']) <= 2.0 and bool(row.get('ended'))
        rows.append(row); note(r, 'w217-p0', role=who, **row)
        row['closed'] = end_dialog(c)
        time.sleep(4)
        # the joiner's B counters over the window (WO216-NORESUME, WO216-EXEMPT, the census)
        r.b.cmd('mp_w216_status'); time.sleep(3)
        jl = r.b.lines(r.b.klog, mb0['k']); jn = r.b.lines(r.b.nlog, mb0['n'])
        cnt = {'noresume': sum(1 for l in jl if 'WO216-NORESUME' in l), 'exempt': sum(1 for l in jl if 'WO216-EXEMPT' in l),
               'exempt_names': sorted(set(m.group(1) for l in jl for m in [re.search(r'WO216-EXEMPT npc=(\S+) .*why=(\S+)', l)] if m))[:20],
               'brains_lines': [l[-200:] for l in jn if 'WO216-BRAINS' in l][-5:], 'status': [l[-400:] for l in jn if 'WO216-STATUS' in l][-1:]}
        detail = {'role': who, 'rows': rows, 'b': cnt}
        with open(os.path.join(r.dir, 'p0-%s.json' % who), 'w', encoding='utf-8') as f: json.dump(detail, f, indent=1)
        ok = all(x.get('ok') for x in rows)
        return True, ('ok' if ok else 'p0-%s-' % who + ','.join(x['what'] for x in rows if not x.get('ok'))), detail
    return fn


# ---------------------------------------------------------------- Part 0.2: the tavern's seated guests

SITTERS = ('local out = {}; local p = player:GetWorldPos(); for _, e in ipairs(System.GetEntitiesInSphere(p, 18) or {}) do '
           'local n = e.GetName and e:GetName() or ""; if (e.class == "NPC" or e.class == "NPC_Female") and e.soul and not string.find(n, "kcd2mp_", 1, true) then '
           'local q = e:GetWorldPos(); out[#out + 1] = string.format("%s@%.2f,%.2f,%.2f", n, q.x, q.y, q.z) end end; '
           'System.LogAlways("[W217T] " .. (#out > 0 and table.concat(out, ";") or "none"))')


def p0_sitters(r, secs=float(os.environ.get('KCDMP_RIG_SIT_S', '600'))):
    """Part 0.2: ten minutes in the tavern by night with seated guests; the joiner's player stands 5-10 m from them (6 m from the
    innkeeper's side, inside), the host beside the innkeeper. The pulls are read from the joiner's MP-NPCPULL windows afterwards (which
    seated copy, which frame, who wrote each side: the analysis tool); one framed pair of a seated guest after the window."""
    def fn():
        S = _S()
        t = r.a.ask(S.FIND_INN % os.environ.get('KCDMP_RIG_INN', 'ttkc_woman_2'), 'W207I', 6)
        if not t or t == 'none': return False, 'no-innkeeper', t
        parts = t.split()
        inn, x, y, z = parts[0], float(parts[1]), float(parts[2]), float(parts[3])
        r.a.lua('local now = Calendar.GetWorldTime(); local d = math.floor(now / 86400) * 86400 + 21 * 3600; '
                'if d < now then d = d + 86400 end; Calendar.SetWorldTime(d)')
        time.sleep(45)
        r.a.teleport(x + 1.2, y, z + 0.05)
        at = None
        for _ in range(6):
            r.b.teleport(x + 2.5, y + 1.0, z + 0.05)
            time.sleep(4)
            q = r.b.pos()
            if q and abs(q[0] - (x + 2.5)) < 3.0 and abs(q[1] - (y + 1.0)) < 3.0 and abs(q[2] - z) < 2.0: at = q; break
        time.sleep(20)
        around = r.a.ask(SITTERS, 'W217T', 6)
        t0 = time.time()
        time.sleep(secs)
        t1 = time.time()
        around2 = r.a.ask(SITTERS, 'W217T', 6)
        row = {'innkeeper': inn, 'at': [x, y, z], 'joiner_at': at, 't0_ms': int(t0 * 1000), 't1_ms': int(t1 * 1000), 'secs': secs,
               'host_npcs_18m_start': around, 'host_npcs_18m_end': around2}
        with open(os.path.join(r.dir, 'p0-sitters.json'), 'w', encoding='utf-8') as f: json.dump(row, f, indent=1)
        note(r, 'w217-sitters', **row)
        return at is not None, ('ok' if at else 'joiner-not-in-tavern'), row
    return fn


def sitter_frame(r):
    """A framed pair of one seated guest (the first host NPC within 10 m of the innkeeper whose fragment is a sitting one, by the host's
    own probe line; else the innkeeper's nearest)."""
    def fn():
        names = r.a.ask(SITTERS, 'W217T', 6)
        best = None
        if names and names != 'none':
            pick = os.environ.get('KCDMP_RIG_SITTER')
            for ent in names.split(';'):
                n = ent.split('@')[0]
                if pick and n == pick: best = n; break
                if not best and not n.startswith('ttkc_woman_2'): best = n
        if not best: return True, 'no-subject', names
        f = frame_pair(r, 'p0-sitter', best)
        return True, 'ok', f
    return fn


# ---------------------------------------------------------------- G.1: drops between the players, with a ledger

# the dropper's items: a weapon it carries (else one made), a food item (made), and money (the game's own money is no item: tried as a
# drop of the money item class when one exists, reported as such). Each drop is the transition the UI's drop makes (human:PlaceItem).
ITEMS_OF = ('local out = {}; for _, id in ipairs(player.inventory:GetInventoryTable() or {}) do local it = ItemManager.GetItem(id); '
            'if it then local nm = ""; pcall(function() nm = ItemManager.GetItemName(it.class) or "" end); '
            'out[#out + 1] = string.format("%s|%s|%s|%d", tostring(id), tostring(it.class), nm, it.amount or 1) end end; '
            'System.LogAlways("[W217J] " .. table.concat(out, ";"))')
PLACE = ('local want = "%s"; local id = nil; for _, x in ipairs(player.inventory:GetInventoryTable() or {}) do if tostring(x) == want then id = x end end; '
         'local ok, v = false, "no-item"; if id then local p = player:GetWorldPos(); local d = player:GetDirectionVector(1); local nm = "kcd2mp_ianchor_w217_" .. tostring(math.random(1, 999999)); '
         'pcall(function() System.SpawnEntity({class = "PickableItem", name = nm, position = {x = p.x + d.x * 1.2, y = p.y + d.y * 1.2, z = p.z + 0.4}, properties = {}}) end); '
         'local a = System.GetEntityByName(nm); if a then ok, v = pcall(function() return player.human:PlaceItem(id, a.id, false) end); '
         'pcall(function() System.RemoveEntity(a.id) end) else v = "no-anchor" end end; '
         'System.LogAlways("[W217P] " .. tostring(ok) .. " " .. tostring(v))')
CREATE = ('local ok, v = pcall(function() return player.inventory:CreateItem("%s", 1, %d) end); System.LogAlways("[W217C] " .. tostring(ok) .. " " .. tostring(v))')
GROUND = ('local out = {}; local p = player:GetWorldPos(); for _, e in ipairs(System.GetEntitiesInSphere(p, 12) or {}) do '
          'if e.class == "PickableItem" then local q = e:GetWorldPos(); local cls = "?"; pcall(function() local it = e.item and ItemManager.GetItem(e.item:GetId()); if it then cls = tostring(it.class) end end); '
          'if cls == "?" or cls == "nil" then pcall(function() cls = tostring(e.Properties.sItemClassId or "?") end) end; '
          'out[#out + 1] = string.format("%s|%s|%.2f,%.2f,%.2f", e:GetName(), cls, q.x, q.y, q.z) end end; System.LogAlways("[W217G] " .. table.concat(out, ";"))')
PICKUP = ('local e = System.GetEntityByName("%s"); local ok = false; if e then ok = pcall(function() PickableItem.OnUsed(e, player) end) end; '
          'System.LogAlways("[W217U] " .. tostring(e ~= nil) .. " " .. tostring(ok))')
COUNT_CLS = ('local n = 0; pcall(function() n = player.inventory:GetCountOfClass("%s") end); System.LogAlways("[W217K] " .. tostring(n))')


def _items(c):
    t = c.ask(ITEMS_OF, 'W217J', 6) or ''
    out = []
    for e in t.split(';'):
        p = e.split('|')
        if len(p) == 4: out.append({'id': p[0], 'cls': p[1], 'name': p[2], 'amt': int(p[3]) if p[3].isdigit() else 1})
    return out


KNOWN = {'food': ('4a6fa310-067a-404d-9813-bd1761d1c70d', 1), 'money': ('5ef63059-322e-4e1b-abe8-926e100c770e', 25),
         'weapon': ('c67de991-e22a-4a19-8b68-9369919c41dd', 1)}   # the mace the bench's Henry dropped in p0b (mace02)


def _pick_kind(items, kind):
    rx = {'weapon': r'sword|axe|mace|dagger|hammer|falchion|sabre|saber|club|hatchet|kord|mec|sekera|palcat',
          'food': r'apple|bread|onion|carrot|meat|cheese|cabbage|pea|jablko|chleb|cibule|mrkev|maso|syr|zeli|sausage|beet',
          'money': r'money|groschen|coin|penize|gros'}[kind]
    for it in items:
        if re.search(rx, it['name'] or '', re.I) or re.search(rx, it['cls'] or '', re.I): return it
    return None


def drops(r, label='before', kinds=('weapon', 'food', 'money')):
    """G.1 (the top rule): each player drops a weapon, food and groschen for the other; the other picks each up (PickableItem.OnUsed, the
    function the game's use action calls). The ledger: per item, dropped on the dropper (its count down), the ground copy on the other's
    screen, picked up there (its count up), gone from the dropper's ground. Written to drops-<label>.json; the result is compared with the
    other label's (identical results = the rule holds)."""
    def fn():
        S = _S()
        # the host's items are the class source for what the joiner lacks (a weapon, food)
        r.a.teleport(*HOME); r.b.teleport(HOME[0] + 2.0, HOME[1])
        time.sleep(5)
        ledger = []
        for who in ('host', 'joiner'):
            D, R = roles(r, who)
            for kind in kinds:
                items = _items(D)
                it = _pick_kind(items, kind)
                if kind == 'weapon':   # never the one in hand: the engine expires a held weapon placed on the ground (p0e: "dropped & lost")
                    have = {x['id'] for x in items}
                    D.ask(CREATE % (KNOWN['weapon'][0], 1), 'W217C', 5)
                    time.sleep(1)
                    it = next((x for x in _items(D) if x['cls'] == KNOWN['weapon'][0] and x['id'] not in have), None)
                if not it and kind in KNOWN:   # made in the dropper's own inventory: a known class (an onion; the money item; a mace)
                    D.ask(CREATE % (KNOWN[kind][0], KNOWN[kind][1]), 'W217C', 5)
                    time.sleep(1)
                    it = next((x for x in _items(D) if x['cls'] == KNOWN[kind][0]), None)
                if not it and kind != 'money':   # made in the dropper's own inventory from a class the other world has
                    src = _pick_kind(_items(R), kind) or _pick_kind(_items(r.a), kind)
                    if src:
                        D.ask(CREATE % (src['cls'], 1), 'W217C', 5)
                        time.sleep(1)
                        it = _pick_kind(_items(D), kind)
                row = {'label': label, 'dropper': who, 'kind': kind, 'item': it}
                if not it:
                    row['result'] = 'no-item'; ledger.append(row); note(r, 'w217-drop', **row); continue
                n_d0 = D.ask(COUNT_CLS % it['cls'], 'W217K', 4)
                n_r0 = R.ask(COUNT_CLS % it['cls'], 'W217K', 4)
                g0 = set((R.ask(GROUND, 'W217G', 5) or '').split(';'))
                gD0 = sum(1 for x in (D.ask(GROUND, 'W217G', 5) or '').split(';') if x and x.split('|')[1:2] == [it['cls']])   # already lying there
                mD, mR = D.mark(), R.mark()
                row['place'] = D.ask(PLACE % it['id'], 'W217P', 5)
                ev = D.wait('k', mD['k'], r'item_drop (\S+) ', 6)
                row['drop_event'] = bool(ev)
                # the other's screen: a new pickable of that class within 12 m
                seen, t0 = None, time.time()
                while time.time() - t0 < 60 and not seen:   # a background copy's queue runs late (p0e: materialized after 25 s)
                    g = [x for x in (R.ask(GROUND, 'W217G', 5) or '').split(';') if x and x not in g0]
                    seen = next((x for x in g if x.split('|')[1] == it['cls']), None)
                    if not seen and g: row['other_new_unmatched'] = g[:3]   # a new pickable of another class: recorded, not taken
                    if not seen: time.sleep(1.5)
                row['seen_by_other_s'] = round(time.time() - t0, 1) if seen else None
                row['n_dropper'] = [n_d0, D.ask(COUNT_CLS % it['cls'], 'W217K', 4)]
                if seen:
                    ent = seen.split('|')[0]
                    px, py, pz = (float(v) for v in seen.split('|')[2].split(','))
                    R.teleport(px + 0.6, py)
                    time.sleep(2)
                    row['pickup'] = R.ask(PICKUP % ent, 'W217U', 5)
                    time.sleep(4)
                    row['n_other'] = [n_r0, R.ask(COUNT_CLS % it['cls'], 'W217K', 4)]
                    claim = R.wait('k', mR['k'], r'item_claim', 8)
                    row['claim_event'] = bool(claim)
                    time.sleep(4)
                    gD = D.ask(GROUND, 'W217G', 5) or ''
                    row['left_on_dropper_ground'] = max(0, sum(1 for x in gD.split(';') if x and x.split('|')[1] == it['cls']) - gD0)   # this drop's
                try:
                    dd = int(row['n_dropper'][1]) - int(row['n_dropper'][0])
                    dr = int(row['n_other'][1]) - int(row['n_other'][0]) if row.get('n_other') else None
                except (TypeError, ValueError): dd, dr = None, None
                row['delta_dropper'], row['delta_other'] = dd, dr
                a = int(it.get('amt') or 1)   # a stack (the money item: 25) moves whole
                row['result'] = 'ok' if (dd == -a and dr == a and not row.get('left_on_dropper_ground')) else (
                    'duplicate' if dd == 0 and dr == a else
                    'not-taken' if dd == -a and dr == 0 and row.get('left_on_dropper_ground') else   # still on the dropper's ground: nobody lost it
                    'lost' if dd == -a and dr == 0 else 'not-seen' if not seen else 'other')
                if row['result'] != 'ok' and seen:   # what the taker holds and what lies there, for the record
                    row['taker_items'] = [x for x in _items(R) if x['cls'] == it['cls']][:3]
                    row['in_dialog'] = {'dropper': D.ask(IN_DIALOG, 'W217I', 3), 'taker': R.ask(IN_DIALOG, 'W217I', 3)}
                ledger.append(row); note(r, 'w217-drop', **row)
                r.a.teleport(*HOME); r.b.teleport(HOME[0] + 2.0, HOME[1])
                time.sleep(3)
        with open(os.path.join(r.dir, 'drops-%s.json' % label), 'w', encoding='utf-8') as f: json.dump(ledger, f, indent=1)
        summary = {k: sum(1 for x in ledger if x['result'] == k) for k in ('ok', 'duplicate', 'lost', 'not-taken', 'not-seen', 'no-item', 'other')}
        return True, 'ok' if summary['duplicate'] == 0 and summary['lost'] == 0 else 'ledger-dirty', {'summary': summary, 'rows': ledger}
    return fn


LONG_NAMES = ('local out, n = {}, 0; for _, cls in ipairs({"NPC", "NPC_Female", "Horse", "Dog", "Animal"}) do for _, e in ipairs(System.GetEntitiesByClass(cls) or {}) do '
              'local nm = e:GetName(); if #nm > 59 then n = n + 1; if #out < 40 then out[#out + 1] = cls .. ":" .. #nm .. ":" .. nm end end end end; '
              'System.LogAlways("[W217LN] " .. n .. " " .. table.concat(out, ";"))')


def long_names(r):
    """B.3 measure first: the bodies (people, horses, dogs, animals) whose names are over 59 characters, on each machine."""
    def fn():
        out = {'host': r.a.ask(LONG_NAMES, 'W217LN', 10), 'joiner': r.b.ask(LONG_NAMES, 'W217LN', 10)}
        with open(os.path.join(r.dir, 'b3-long-names.json'), 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)
        return True, 'ok', out
    return fn


HORSE_AT = ('local e = System.GetEntityByName("%s"); if e then local q = e:GetWorldPos(); System.LogAlways(string.format("[W217HA] %%.2f %%.2f %%.2f", q.x, q.y, q.z)) '
            'else System.LogAlways("[W217HA] none") end')


def long_names_live(r, name='horse[animalcare/horseCare6_ff15a088-7332-4a17-9536-3e8f6a8898ad]', alias='lh838eb35a', secs=40):
    """B.3 on the bench: both players 8 m from an animal-care horse (a name over 63 characters) for <secs>; then the host's DLL log and the
    joiner's: the alias streamed / bound (its lines), and the joiner's body of that horse against the host's (how far apart)."""
    def fn():
        t = r.a.ask(HORSE_AT % name, 'W217HA', 6)
        out = {'name': name, 'alias': alias, 'host_at': t}
        if not t or t == 'none': return True, 'no-horse', out
        x, y, z = (float(v) for v in t.split())
        mh, mj = r.a.mark(), r.b.mark()
        r.a.teleport(x + 8.0, y); r.b.teleport(x + 8.0, y + 2.0)
        time.sleep(secs)
        hn = [l for l in r.a.lines(r.a.nlog, mh['n']) if alias in l][:6]
        jn = [l for l in r.b.lines(r.b.nlog, mj['n']) if alias in l][:6]
        jk = [l for l in r.b.lines(r.b.klog, mj['k']) if alias in l][:6]
        j = r.b.ask(HORSE_AT % name, 'W217HA', 6)
        h2 = r.a.ask(HORSE_AT % name, 'W217HA', 6)
        try:
            jp = [float(v) for v in j.split()]; hp = [float(v) for v in h2.split()]
            out['joiner_off_m'] = round(((jp[0] - hp[0]) ** 2 + (jp[1] - hp[1]) ** 2) ** 0.5, 2)
        except (AttributeError, ValueError): out['joiner_off_m'] = None
        out.update(host_native_lines=len(hn), joiner_native_lines=len(jn), joiner_game_lines=len(jk),
                   examples=[l[-180:] for l in (hn + jn + jk)[:6]])
        with open(os.path.join(r.dir, 'b3-live.json'), 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)
        return True, 'ok', {k: v for k, v in out.items() if k != 'examples'}
    return fn


def daytime(r, hh):
    """The host's clock to hh (the joiner follows); evidence only -- a joiner slow to answer the clock read is no reason to stop (a failed
    step collects both bundles: four minutes)."""
    S = _S()
    inner = S.s_daytime(r, int(hh * 3600), settle_s=45)
    def fn():
        ok, why, d = inner()
        return True, why, d
    return fn


def plans(r, name, args):
    """The WO-217 scenario plans, merged into scenarios.run's table."""
    S = _S()
    return {
        # Part 0 on the shipped build: 0.1 both roles, 0.2 the sitters, G.1 the drops (before any WO-217 change)
        'w217p0': [('join', S.s_join(r)), ('day', daytime(r, 10.0)),
                   ('p0-joiner', p0_role(r, 'joiner')), ('p0-host', p0_role(r, 'host')),
                   ('drops-before', drops(r, 'before')),
                   ('sitters', p0_sitters(r)), ('frame-sitter', sitter_frame(r))],
        'w217p01': [('join', S.s_join(r)), ('day', daytime(r, 10.0)), ('p0-joiner', p0_role(r, 'joiner')), ('p0-host', p0_role(r, 'host')),
                    ('drops-before', drops(r, 'before')), ('long-names', long_names(r))],
        'w217p02': [('join', S.s_join(r)), ('day', daytime(r, 10.0)), ('p0-joiner', p0_role(r, 'joiner')), ('p0-host', p0_role(r, 'host')),
                    ('drops-before', drops(r, 'before'))],
        'w217sit': [('join', S.s_join(r)), ('sitters', p0_sitters(r)), ('frame-sitter', sitter_frame(r))],
        'w217drops': [('join', S.s_join(r)), ('drops', drops(r, args[0] if args else 'run'))],
        'w217lootprobe': [('join', S.s_join(r)), ('loot-probe', loot_probe(r))],
        # b1's scene set: A (the smith's house, A.0's lines + the shipped bar), 0.2 (the sitters), B.2 (the brawl's falls), B.1 (the far opponent)
        'w217a': [('join', S.s_join(r)), ('smithy', smithy(r)), ('sitters', p0_sitters(r)), ('frame-sitter', sitter_frame(r)),
                  ('evening', daytime(r, 21.0)), ('watch-on', S.s_w215_watch(r, True)), ('brawl', S.s_w216_brawl(r)), ('frame-brawl', brawl_pair(r)),
                  ('settle', lambda: (time.sleep(15), (True, 'ok', {}))[1]), ('ghost-far', ghost_far(r)), ('watch-off', S.s_w215_watch(r, False))],
        'w217events': [('join', S.s_join(r)), ('day', daytime(r, 10.0)), ('road', event_probe(r)),
                       ('caravan', event_probe(r, 'karavanyVeSvete', os.environ.get('KCDMP_RIG_CARAVAN_PLACE', 'troseckoCaravans_path2_place4')))],
        # E.1 and F on the save's own dead and its running caravan; the road robbery from afar
        # (the road robbery never fired on demand -- eprobe from 40 m, ef1-b9 from 180 m: not staged; the field's scene 7)
        'w217ef': [('join', S.s_join(r)), ('day', daytime(r, 10.0)), ('corpses', corpses(r)), ('caravan', caravan_pass(r)),
                   ('long-names-live', long_names_live(r))],
        'w217g': [('join', S.s_join(r)), ('day', daytime(r, 10.0)), ('loose', loose_items(r)), ('doors', doors(r)), ('drops-after', drops(r, args[0] if args else 'after'))],
        'w217loot': [('join', S.s_join(r)), ('day', daytime(r, 10.0)), ('loot', loot_scene(r))],
        # the acceptance: 1 talk/trade/scene (both roles), 2 the tavern's sitters, 3 the smith's house, 4 the brawl + the far opponent, 5 the loot,
        # 6 the road encounter, 7 the caravan pass, 8 the drops
        # (the save's caravan is on the road only on the first morning: scene 7 and the corpse check come first)
        'w217acc': [('join', S.s_join(r)), ('day', daytime(r, 10.0)), ('s7-caravan', caravan_pass(r)), ('s6-corpses', corpses(r)),
                    ('s1-p0-joiner', p0_role(r, 'joiner')), ('s1-p0-host', p0_role(r, 'host')),
                    ('s3-smithy', smithy(r)),
                    ('s2-sitters', p0_sitters(r)), ('s2-frame', sitter_frame(r)),
                    ('evening', daytime(r, 21.0)), ('watch-on', S.s_w215_watch(r, True)), ('s4-brawl', S.s_w216_brawl(r)), ('s4-frame', brawl_pair(r)),
                    ('settle', lambda: (time.sleep(15), (True, 'ok', {}))[1]), ('s4-ghost-far', ghost_far(r)), ('watch-off', S.s_w215_watch(r, False)),
                    ('day2', daytime(r, 10.0)), ('s5-loot', loot_scene(r)),

                    ('s8-drops', drops(r, 'acc')), ('g3-doors', doors(r))],
        'w217ghost': [('join', S.s_join(r)), ('watch-on', S.s_w215_watch(r, True)), ('ghost-far', ghost_far(r)), ('watch-off', S.s_w215_watch(r, False))],
    }.get(name)


CONTINUE_ON_FAIL = ('w217ef', 'w217p0', 'w217p01', 'w217p02', 'w217sit', 'w217drops', 'w217lootprobe', 'w217a', 'w217ghost', 'w217events', 'w217g', 'w217loot', 'w217acc')


# ---------------------------------------------------------------- C: the loot screen probe (feasibility, before C is built)

BODY_NEAR = ('local best, bd; local p = player:GetWorldPos(); for _, e in ipairs(System.GetEntitiesInSphere(p, %d) or {}) do '
             'if (e.class == "NPC" or e.class == "NPC_Female") and e.GetName and e.inventory and e.actor then local n = e:GetName(); local d = false; '
             'pcall(function() d = e.actor:IsDead() == true end); local k = 0; pcall(function() k = #(e.inventory:GetInventoryTable() or {}) end); '
             'if d and k >= %d and not string.find(n, "kcd2mp_", 1, true) and not string.find(n, "DialogTwin_", 1, true) then '
             'local q = e:GetWorldPos(); local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; if not bd or dd < bd then bd = dd; best = e end end end end; '
             'if best then local q = best:GetWorldPos(); System.LogAlways(string.format("[W217B] %%s %%.2f %%.2f %%.2f %%d", best:GetName(), q.x, q.y, q.z, '
             '#(best.inventory:GetInventoryTable() or {}))) else System.LogAlways("[W217B] none") end')
INV_OF = ('local e = System.GetEntityByName("%s"); local out = {}; if e and e.inventory then for _, id in ipairs(e.inventory:GetInventoryTable() or {}) do '
          'local it = ItemManager.GetItem(id); local nm = "?"; pcall(function() nm = ItemManager.GetItemName(it.class) or "?" end); '
          'local own = "-"; pcall(function() own = tostring(ItemManager.GetItemOwner(id)) end); '
          'out[#out + 1] = string.format("%%s|%%s|%%s|%%d|%%.2f|%%s", tostring(id), tostring(it and it.class), nm, it and it.amount or 0, it and it.health or 0, own) end end; '
          'System.LogAlways("[W217V] " .. #out .. " " .. table.concat(out, ";"))')
ESCROW = ('local e = System.GetEntityByName("kcd2mp_w217_escrow"); if not e then local p = player:GetWorldPos(); '
          'pcall(function() e = System.SpawnEntity({class = "Stash", name = "kcd2mp_w217_escrow", position = {x = p.x, y = p.y, z = p.z - 30}}) end) end; '
          'local hid = false; if e then pcall(function() e:Hide(1) end); pcall(function() hid = e:IsHidden() end) end; '
          'System.LogAlways("[W217E] " .. tostring(e ~= nil) .. " " .. tostring(e and e.inventory ~= nil) .. " hidden=" .. tostring(hid))')
MOVE = ('local a = System.GetEntityByName("%s"); local b = System.GetEntityByName("%s"); local id = nil; '
        'if a and a.inventory then local t = a.inventory:GetInventoryTable() or {}; id = t[%d] end; local ok, v = false, nil; '
        'if id and b and b.inventory then ok, v = pcall(function() return b.inventory:AddItem(id) end) end; '
        'local still = false; if id and a and a.inventory then pcall(function() still = a.inventory:HasItem(id) end) end; '
        'System.LogAlways("[W217Mv] " .. tostring(id) .. " ok=" .. tostring(ok) .. " v=" .. tostring(v) .. " still_in_source=" .. tostring(still))')
OPEN_LOOT = ('local e = System.GetEntityByName("%s"); local ok = false; if e then ok = pcall(function() BasicAIActions.OnLoot(e, player, 0) end) end; '
             'System.LogAlways("[W217O] " .. tostring(ok))')
ALIVE = 'System.LogAlways("[W217Z] alive")'
ITEM_KEYS = ('local e = System.GetEntityByName("%s"); local out = {}; if e and e.inventory then local id = (e.inventory:GetInventoryTable() or {})[1]; '
             'local it = id and ItemManager.GetItem(id); if it then for k, v in pairs(it) do out[#out + 1] = tostring(k) .. "=" .. tostring(v) end end; '
             'local own = "-"; pcall(function() own = tostring(ItemManager.GetItemOwner(id)) end); out[#out + 1] = "owner=" .. own end; '
             'table.sort(out); System.LogAlways("[W217K2] " .. table.concat(out, " "))')


def loot_probe(r):
    """C's feasibility, on the host's own screen first: a body's loot screen opened the game's way; one item moved OUT of the body into a
    hidden stash and one moved back IN, under the open screen; a frame after each (does the screen follow?); the screen closed with the
    player's own key; the game alive after; then a take through the screen's own path again. Evidence only."""
    def fn():
        out = {}
        r.a.teleport(*HOME); time.sleep(4)
        t = r.a.ask(BODY_NEAR % (900, 3), 'W217B', 8)
        out['body'] = t
        if not t or t == 'none': return True, 'no-body', out
        name, x, y, z, n = t.split()
        r.a.teleport(float(x) + 1.0, float(y), float(z) + 0.05); time.sleep(4)
        r.b.teleport(float(x) + 3.0, float(y) + 1.0, float(z) + 0.05); time.sleep(3)
        out['escrow'] = r.a.ask(ESCROW, 'W217E', 6)
        out['inv0'] = r.a.ask(INV_OF % name, 'W217V', 6)
        out['item_keys_host'] = r.a.ask(ITEM_KEYS % name, 'W217K2', 6)
        out['item_keys_joiner'] = r.b.ask(ITEM_KEYS % name, 'W217K2', 6)
        out['player_item_keys'] = r.a.ask(ITEM_KEYS % 'Dude', 'W217K2', 6)
        r.a.lua(AIM_AT % name); time.sleep(1)
        m0 = r.a.mark()
        out['open'] = r.a.ask(OPEN_LOOT % name, 'W217O', 6)
        m, dt = _wait_any(r.a, m0['k'], SCREEN_IN, 10)
        out['screen_in'] = bool(m)
        time.sleep(2)
        out['f0'] = shot(r, r.a, 'c-probe-0-open')
        out['move_out'] = r.a.ask(MOVE % (name, 'kcd2mp_w217_escrow', 1), 'W217Mv', 6)
        time.sleep(1.5)
        out['f1'] = shot(r, r.a, 'c-probe-1-moved-out')
        out['inv1'] = r.a.ask(INV_OF % name, 'W217V', 6)
        out['move_in'] = r.a.ask(MOVE % ('kcd2mp_w217_escrow', name, 1), 'W217Mv', 6)
        time.sleep(1.5)
        out['f2'] = shot(r, r.a, 'c-probe-2-moved-in')
        out['inv2'] = r.a.ask(INV_OF % name, 'W217V', 6)
        import keys
        S = _S()
        k = keys.Keys(S.rec_of(r.a), 'wo217-lootprobe')
        k.press('back'); k.release()
        mc, _ = _wait_any(r.a, m0['k'], SCREEN_OUT, 8)
        out['screen_out'] = bool(mc)
        time.sleep(4)
        out['alive_after_close'] = r.a.ask(ALIVE, 'W217Z', 8)
        # the same again: open, a take by the screen's own move (the player's inventory), close
        m1 = r.a.mark()
        r.a.ask(OPEN_LOOT % name, 'W217O', 6)
        m2, _ = _wait_any(r.a, m1['k'], SCREEN_IN, 10)
        out['screen_in2'] = bool(m2)
        out['take_to_player'] = r.a.ask(MOVE % (name, 'Dude', 1), 'W217Mv', 6)
        time.sleep(1.5)
        out['f3'] = shot(r, r.a, 'c-probe-3-taken')
        k = keys.Keys(S.rec_of(r.a), 'wo217-lootprobe2'); k.press('back'); k.release()
        time.sleep(4)
        out['alive_after_close2'] = r.a.ask(ALIVE, 'W217Z', 8)
        # the joiner's view of the same body now (its copy's items)
        out['joiner_inv'] = r.b.ask(INV_OF % name, 'W217V', 6)
        with open(os.path.join(r.dir, 'c-probe.json'), 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)
        return True, 'ok', out
    return fn


# ---------------------------------------------------------------- B.1: a ghost fails while a real opponent fights from afar

CLEAR_NEAR = ('local c = {x = %f, y = %f, z = 0}; c.z = System.GetTerrainElevation(c); local keep = {["%s"] = true, ["%s"] = true}; local n = 0; '
              'for _, e in ipairs(System.GetEntitiesInSphere(c, 20) or {}) do local nm = e.GetName and e:GetName() or ""; '
              'if (e.class == "NPC" or e.class == "NPC_Female") and not keep[nm] and not nm:find("kcd2mp_", 1, true) and e.actor and not e.actor:IsDead() then '
              'pcall(System.ExecuteCommand, "wh_ai_PauseNPC " .. nm); local q = {x = %f + n * 2, y = %f, z = 0}; q.z = System.GetTerrainElevation(q) + 0.2; '
              'pcall(function() e:SetWorldPos(q) end); n = n + 1 end end; System.LogAlways("[W217CL2] " .. n)')
DIST_TO = ('local e = System.GetEntityByName("%s"); local p = player:GetWorldPos(); local q = e and e:GetWorldPos(); '
           'System.LogAlways(string.format("[W217DIST] %%.1f", q and math.sqrt((q.x - p.x) ^ 2 + (q.y - p.y) ^ 2) or -1))')


def ghost_far(r, hold=float(os.environ.get('KCDMP_RIG_GHOST_HOLD_S', '30')), far_m=float(os.environ.get('KCDMP_RIG_FAR_M', '42'))):
    """B.1 (WO-216 H's twin case, staged): two bandits of a camp taken 60 m from it; the first (the ghost) next to the players, the second
    (a real opponent) far_m further on. Both are set on the joiner's figure; the far one is held where it stands on the host (paused: its
    fight and its target stay); once the joiner's player is in combat the near one is parked as a ghost (paused, hidden) and the joiner's
    game logs the engine's own failure line for it. With ghost_far on the fight must be kept for <hold> s (a real opponent of this player,
    42 m off); the twin (ghost_far off) ends it after E's 4 s."""
    def fn():
        S = _S()
        bt = r.a.ask(S.FIND_BANDITS3, 'W207K', 6)
        if not bt or bt.startswith('none'): return True, 'no-bandits', bt
        names, bx, by, bz = bt.split()
        nn = names.split(',')
        if len(nn) < 2: return True, 'one-bandit', bt
        n0, n1 = nn[0], nn[1]
        x, y = float(bx) + 60.0, float(by)
        r.a.lua(S.PLACE_NPC % (n0, x, y, x, y))
        r.a.lua(S.PLACE_NPC % (n1, x + far_m + 8.0, y, x + far_m + 8.0, y))
        time.sleep(3)
        r.a.teleport(x + 2.0, y + 2.0)
        r.b.teleport(x + 1.0, y + 4.0)
        time.sleep(20)
        info = {'ghost': n0, 'far': n1, 'at': [x, y]}
        for c in (r.a, r.b): c.keep_up()
        r.a.lua(S.ATTACK % (n1, S.FIGURE))
        time.sleep(0.6)
        r.a.lua('pcall(System.ExecuteCommand, "wh_ai_PauseNPC %s")' % n1)   # the far one keeps its fight and its target, where it stands
        # a1-b7: the far one ran in (or was never moved: the brawl before left it fighting) -- paused now, it is put back far_m out
        time.sleep(1.0)
        for _ in range(3):
            d = r.a.ask(DIST_TO % n1, 'W217DIST', 4)
            try: dd = float(d)
            except (TypeError, ValueError): dd = -1.0
            if dd >= far_m - 5.0: break
            r.a.lua(S.PLACE_NPC % (n1, x + far_m + 8.0, y, x + far_m + 8.0, y))
            time.sleep(1.5)
        r.a.lua(S.ATTACK % (n0, S.FIGURE))
        t_set = time.time(); inc = None
        while time.time() - t_set < 40:
            if r.b.ask(S.W215_COMBAT_Q, 'W215Q', 3) == '1': inc = round(time.time() - t_set, 1); break
            time.sleep(1)
        info['joiner_in_combat_after_s'] = inc
        info['far_dist_host_m'] = r.a.ask(DIST_TO % n1, 'W217DIST', 4)
        info['far_dist_joiner_m'] = r.b.ask(DIST_TO % n1, 'W217DIST', 4)
        if inc is None: return True, 'joiner-never-in-combat', info
        # twin1-b12: a third bandit within 15 m kept the twin's fight by WO-215's own rule -- every other living body near the players
        # (not the ghost, not the far one, not the players' figures) is paused and put 80 m away first
        info['cleared'] = r.a.ask(CLEAR_NEAR % (x, y, n0, n1, x + 80.0, y + 80.0), 'W217CL2', 6)
        time.sleep(3)
        r.a.lua('pcall(System.ExecuteCommand, "wh_ai_PauseNPC %s"); local e = System.GetEntityByName("%s"); if e then pcall(function() e:Hide(1) end) end' % (n0, n0))
        time.sleep(1)
        info['other_living_within_15m'] = r.b.ask(S.W216_NEAR_LIVING % n0, 'W216N', 4) or ''
        mb = r.b.mark()
        r.b.lua('System.LogAlways("[Error] Combat actor init failed for actor \'%s\'")' % n0)
        t_line = time.time(); out_at = None
        while time.time() - t_line < hold:
            if r.b.ask(S.W215_COMBAT_Q, 'W215Q', 3) == '0': out_at = round(time.time() - t_line, 1); break
            time.sleep(0.5)
        info['out_of_combat_after_s'] = out_at
        info['kept'] = out_at is None
        info['ghostfight_line'] = bool(r.b.wait('k', mb['k'], r'WO215-GHOSTFIGHT this player was in combat', 2))
        info['far_dist_joiner_m_end'] = r.b.ask(DIST_TO % n1, 'W217DIST', 4)
        r.b.cmd('mp_w217_status'); time.sleep(2)
        info['ghost_far_kept'] = (r.b.wait('k', mb['k'], r'WO217-STATUS .*ghost_far_kept=(\d+)', 3) or [None, None])[1] if r.b.wait('k', mb['k'], r'ghost_far_kept=(\d+)', 3) else None
        # the fight ends: the far one released, both bandits paused where they are
        r.a.lua('pcall(System.ExecuteCommand, "wh_ai_PauseNPC %s")' % n1)
        for c in (r.a, r.b): c.stop_keep()
        with open(os.path.join(r.dir, 'b1-ghostfar.json'), 'w', encoding='utf-8') as fh: json.dump(info, fh, indent=1)
        return True, 'ok' if info['kept'] else 'fight-ended', info
    return fn


# ---------------------------------------------------------------- A: the smith's house, with framed pairs

NEAR_OF = ('local c = System.GetEntityByName("%s"); local out = {}; if c then local p = c:GetWorldPos(); '
           'for _, e in ipairs(System.GetEntitiesInSphere(p, 18) or {}) do local n = e.GetName and e:GetName() or ""; '
           'if (e.class == "NPC" or e.class == "NPC_Female") and n ~= "%s" and not string.find(n, "kcd2mp_", 1, true) then '
           'local q = e:GetWorldPos(); out[#out + 1] = string.format("%%s@%%.1f", n, math.sqrt((q.x - p.x) ^ 2 + (q.y - p.y) ^ 2)) end end end; '
           'System.LogAlways("[W217NO] " .. table.concat(out, ";"))')


def smithy(r, secs=float(os.environ.get('KCDMP_RIG_SMITHY_S', '240'))):
    """A's scene (WO-215/216's smithy): the field's own smith, 4 minutes by day, 2 at midday, 2 at night; both players placed by the house.
    The windows go to w215-scenes.json (the analysis tools read them); after each window a framed pair: the smith at the anvil (day), the
    nearest other household body (midday: a diner; night: a sleeper)."""
    def fn():
        S = _S()
        smith = os.environ.get('KCDMP_RIG_SMITH', 'ttac_blacksmith')
        windows = []

        def at_time(hh):
            r.a.lua('local now = Calendar.GetWorldTime(); local d = math.floor(now / 86400) * 86400 + %d; '
                    'if d < now then d = d + 86400 end; Calendar.SetWorldTime(d)' % int(hh * 3600))

        def put_both(x, y):
            r.a.teleport(x + 3.0, y + 3.0)
            for _ in range(4):
                r.b.teleport(x + 4.0, y + 2.0)
                time.sleep(3)
                q = r.b.pos()
                if q and ((q[0] - x - 4.0) ** 2 + (q[1] - y - 2.0) ** 2) ** 0.5 < 15: return True
            return False

        for label, hh, phase_s in (('smith-day', 10.0, secs), ('midday', 12.5, secs / 2), ('night', 23.5, secs / 2)):
            at_time(hh)
            time.sleep(20)
            t = r.a.ask(S.FIND_INN % smith, 'W207I', 6)
            if not t or t == 'none': return False, 'no-smith', t
            parts = t.split()
            x, y = float(parts[1]), float(parts[2])
            put_both(x, y)
            time.sleep(15)
            t0 = time.time()
            time.sleep(phase_s)
            row = {'label': label, 'hour': hh, 'at': [round(x, 1), round(y, 1)], 't0_ms': int(t0 * 1000), 't1_ms': int(time.time() * 1000)}
            subj = smith
            if label != 'smith-day':
                near = (r.a.ask(NEAR_OF % (smith, smith), 'W217NO', 6) or '').split(';')
                near = sorted((float(e.split('@')[1]), e.split('@')[0]) for e in near if '@' in e)
                if near: subj = near[0][1]
            row['pair'] = frame_pair(r, 'a-%s' % label, subj)
            windows.append(row)
            rig.note('w215-scene', **{k: v for k, v in row.items() if k != 'pair'})
        with open(os.path.join(r.dir, 'w215-scenes.json'), 'w', encoding='utf-8') as fh: json.dump(windows, fh, indent=1)
        return True, 'ok', {'windows': windows}
    return fn


def brawl_pair(r):
    """B.2's frame: a copy on the ground beside its host NPC -- after the brawl's staged falls, the nearest downed bandit (or the nearest
    bandit) framed from both views."""
    def fn():
        S = _S()
        bt = r.a.ask(S.FIND_BANDITS3, 'W207K', 6)
        subj = bt.split()[0].split(',')[0] if bt and not bt.startswith('none') else None
        if not subj: return True, 'no-subject', bt
        return True, 'ok', frame_pair(r, 'b2-brawl', subj)
    return fn


# ---------------------------------------------------------------- E / F: measure first -- what an encounter and a caravan leave on each machine

EVENT_PLACES = ('local out = {}; for _, cls in ipairs({"RandomEventPlace", "RandomEventPlaceEntity", "EventPlace"}) do '
                'for _, e in ipairs(System.GetEntitiesByClass(cls) or {}) do local q = e:GetWorldPos(); '
                'out[#out + 1] = string.format("%s|%s|%.0f,%.0f,%.0f", cls, e:GetName(), q.x, q.y, q.z) end end; '
                'System.LogAlways("[W217EP] " .. #out .. " " .. table.concat(out, ";", 1, math.min(#out, 60)))')
AROUND = ('local out = {}; local c = {x = %f, y = %f, z = %f}; for _, e in ipairs(System.GetEntitiesInSphere(c, %d) or {}) do '
          'local n = e.GetName and e:GetName() or "?"; if not string.find(n, "kcd2mp_", 1, true) and e.class ~= "PickableItem" then '
          'local q = e:GetWorldPos(); local a = e:GetWorldAngles(); local dead = ""; pcall(function() if e.actor and e.actor:IsDead() then dead = "dead" end end); '
          'local hid = ""; pcall(function() if e:IsHidden() then hid = "hidden" end end); local arch = ""; pcall(function() arch = e:GetArchetype() or "" end); '
          'local fl = 0; pcall(function() fl = e:GetFlags() or 0 end); local sp = (math.floor(fl / 16777216) %% 2 == 1) and "spawned" or ""; '
          'local mdl = ""; pcall(function() mdl = tostring(e.Properties and e.Properties.object_Model or ""):match("([^/]*)$") or "" end); '
          'out[#out + 1] = string.format("%%s|%%s|%%s|%%.2f,%%.2f,%%.2f|%%.2f,%%.2f,%%.2f|%%s%%s|%%s|%%s", n, e.class or "?", tostring(arch), q.x, q.y, q.z, a.x, a.y, a.z, dead, hid, sp, mdl) end end; '
          'for i = 1, math.min(#out, 150), 12 do System.LogAlways("[W217AR] part " .. table.concat(out, ";", i, math.min(#out, i + 11))) end; '
          'System.LogAlways("[W217AR] end " .. #out)')


def around(c, x, y, z, rad):
    """Every entity within <rad> of a point on <c>, one log line per 12 (a long line would be cut): the rows, and the count."""
    off = c.size(c.klog)
    c.lua(AROUND % (x, y, z, rad))
    m = c.wait('k', off, r'\[W217AR\] end (\d+)', 12)
    rows = []
    for l in c.lines(c.klog, off):
        mm = re.search(r'\[W217AR\] part (.*)$', l)
        if mm: rows += [e for e in mm.group(1).strip().split(';') if e]
    return {'n': int(m.group(1)) if m else None, 'rows': rows}


def event_probe(r, event=os.environ.get('KCDMP_RIG_EVENT', 'prepadeniNaCeste'), place=os.environ.get('KCDMP_RIG_EVENT_PLACE', 'tzel_rockRoad'),
                variant=os.environ.get('KCDMP_RIG_EVENT_VARIANT', ''), wait_s=float(os.environ.get('KCDMP_RIG_EVENT_WAIT_S', '240')), far_m=40.0):
    """E / F measure first: a random event fired on the host (the game's own place and variant overrides, cooldowns off), both players
    by its place; when the host's log says it started, every entity within 40 m of its NPCs listed on BOTH machines (name, class,
    archetype, position, angles, dead, hidden): which of the event's props, corpses and carts the joiner has, and where. Frames."""
    def fn():
        out = {'event': event, 'place': place, 'variant': variant}
        out['places'] = r.a.ask(EVENT_PLACES, 'W217EP', 10)
        at = None
        for ent in (out['places'] or '').split(' ', 1)[-1].split(';'):
            p = ent.split('|')
            if len(p) == 3 and place.lower() in p[1].lower(): at = [float(v) for v in p[2].split(',')]; out['place_entity'] = p[1]; break
        mk = r.a.mark()
        for c in ('wh_pl_RandomEventsCooldownsEnabled 0', 'wh_pl_RandomEventsAutoSpawnEnabled 1', 'wh_pl_RandomEventsDebug 1',
                  'wh_pl_RandomEventsSelectEventPlaceOverride %s' % place) + (('wh_pl_RandomEventsSelectVariantOverride %s' % variant,) if variant else ()):
            r.a.cmd(c)
        if at:
            r.a.teleport(at[0] + far_m, at[1]); r.b.teleport(at[0] + far_m + 2.0, at[1] + 2.0)
        m, dt = _wait_any(r.a, mk['k'], r"Random event '%s' started|Random event '%s' with variant '([^']+)' starting" % (event, event), wait_s, step=1.0)
        out['started'] = m.group(0)[:200] if m else None
        out['started_after_s'] = dt
        evlines = [l[:220] for l in r.a.lines(r.a.klog, mk['k']) if 'RandomEvent' in l][:40]
        out['host_event_lines'] = evlines
        if not m: return True, 'not-started', out
        time.sleep(15)
        # the event's own NPCs on the host (layer names carry the event's name), their centre
        npcs = r.a.ask('local out = {}; for _, e in ipairs(System.GetEntitiesByClass("NPC") or {}) do local n = e:GetName(); '
                       'if string.find(string.lower(n), "%s", 1, true) then local q = e:GetWorldPos(); out[#out + 1] = string.format("%%s@%%.1f,%%.1f,%%.1f", n, q.x, q.y, q.z) end end; '
                       'System.LogAlways("[W217EN] " .. table.concat(out, ";"))' % event.lower()[:8], 'W217EN', 8) or ''
        out['host_event_npcs'] = npcs
        c0 = None
        for e in npcs.split(';'):
            if '@' in e: c0 = [float(v) for v in e.split('@')[1].split(',')]; break
        if not c0 and at: c0 = at
        if c0:
            r.a.teleport(c0[0] + 8.0, c0[1] + 8.0); r.b.teleport(c0[0] + 9.0, c0[1] + 6.0)
            time.sleep(20)
            out['host_around'] = around(r.a, c0[0], c0[1], c0[2], 40)
            out['joiner_around'] = around(r.b, c0[0], c0[1], c0[2], 40)
            subj = npcs.split(';')[0].split('@')[0] if '@' in npcs else None
            if subj: out['pair'] = frame_pair(r, 'e-%s' % event, subj)
        with open(os.path.join(r.dir, 'event-%s.json' % event), 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)
        return True, 'ok', {k: (v[:400] if isinstance(v, str) else v) for k, v in out.items() if k not in ('host_around', 'joiner_around', 'places')}
    return fn


# ---------------------------------------------------------------- E.1 / F: the save's own dead, the save's own caravan

DEAD_NEAR = ('local out = {}; local c = {x = %f, y = %f, z = 0}; c.z = System.GetTerrainElevation(c); for _, e in ipairs(System.GetEntitiesInSphere(c, %d) or {}) do '
             'if (e.class == "NPC" or e.class == "NPC_Female") and e.actor then local n = e:GetName(); if not n:find("kcd2mp_", 1, true) and not n:find("DialogTwin_", 1, true) '
             'and e.actor:IsDead() then local q = e:GetWorldPos(); out[#out + 1] = string.format("%%s@%%.2f,%%.2f,%%.2f", n, q.x, q.y, q.z) end end end; '
             'System.LogAlways("[W217DN] " .. (#out > 0 and table.concat(out, ";", 1, math.min(#out, 20)) or "none"))')
BODY_STATE = ('local out = {}; for n in string.gmatch("%s", "[%%w_]+") do local e = System.GetEntityByName(n); if e then local q = e:GetWorldPos(); '
              'local d = (e.actor and e.actor:IsDead()) and 1 or 0; local h = e:IsHidden() and 1 or 0; out[#out + 1] = string.format("%%s|%%d|%%d|%%.2f,%%.2f,%%.2f", n, d, h, q.x, q.y, q.z) '
              'else out[#out + 1] = n .. "|none" end end; System.LogAlways("[W217BS] " .. table.concat(out, ";"))')


def corpses(r, radius=150):
    """E.1: the save's dead near the bench's spot -- the joiner's body of each name, at once and 12 s later."""
    def fn():
        r.a.teleport(*HOME); r.b.teleport(HOME[0] + 2.0, HOME[1]); time.sleep(5)
        t = r.a.ask(DEAD_NEAR % (HOME[0], HOME[1], radius), 'W217DN', 8) or 'none'
        out = {'host_dead': t}
        if t == 'none': return True, 'no-dead', out
        names = [e.split('@')[0] for e in t.split(';') if '@' in e][:12]
        hp = {e.split('@')[0]: [float(v) for v in e.split('@')[1].split(',')] for e in t.split(';') if '@' in e}

        def read(tag):
            s = r.b.ask(BODY_STATE % ' '.join(names), 'W217BS', 8) or ''
            rows = {}
            for e in s.split(';'):
                f = e.split('|')
                if len(f) == 4:
                    q = [float(v) for v in f[3].split(',')]
                    h = hp.get(f[0]); d = round(((q[0] - h[0]) ** 2 + (q[1] - h[1]) ** 2) ** 0.5, 2) if h else None
                    rows[f[0]] = {'dead': f[1] == '1', 'hidden': f[2] == '1', 'off_m': d}
                elif len(f) == 2: rows[f[0]] = {'missing': True}
            return rows
        out['joiner_at_once'] = read('0')
        time.sleep(12)
        out['joiner_after_12s'] = read('12')
        ok = sum(1 for v in out['joiner_after_12s'].values() if v.get('dead') and not v.get('hidden') and (v.get('off_m') or 0) <= 1.5)
        out['summary'] = {'host_dead': len(names), 'joiner_dead_shown_in_place_12s': ok,
                          'joiner_at_once_dead_shown': sum(1 for v in out['joiner_at_once'].values() if v.get('dead') and not v.get('hidden'))}
        if names:
            out['pair'] = frame_pair(r, 'e1-corpse', names[0], move=True)
        with open(os.path.join(r.dir, 'e1-corpses.json'), 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)
        return True, 'ok', out['summary']
    return fn


CART_NEAR = ('local best, bd; local p = player:GetWorldPos(); for _, e in ipairs(System.GetEntitiesByClass("WHCart") or {}) do local q = e:GetWorldPos(); '
             'local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; if not bd or dd < bd then bd = dd; best = e end end; if best then local q = best:GetWorldPos(); '
             'local a = best:GetWorldAngles(); System.LogAlways(string.format("[W217CN] %s %.2f %.2f %.2f %.4f", best:GetName(), q.x, q.y, q.z, a.z)) '
             'else System.LogAlways("[W217CN] none") end')
CART_POSE = ('local e = System.GetEntityByName("%s"); local out = "none"; if e then local q = e:GetWorldPos(); local tz = System.GetTerrainElevation(q); '
             'local rs = {}; for _, r in ipairs(System.GetEntitiesInSphere(q, 3.5) or {}) do if r.class == "NPC" or r.class == "NPC_Female" then local rq = r:GetWorldPos(); '
             'local rz = System.GetTerrainElevation(rq); if (rq.x - q.x) ^ 2 + (rq.y - q.y) ^ 2 < 9 and rq.z - rz >= 0.5 then rs[#rs + 1] = r:GetName() end end end; '
             'out = string.format("%%.3f %%.3f %%.3f %%.3f %%s", q.x, q.y, q.z, q.z - tz, #rs > 0 and table.concat(rs, ",") or "-") end; System.LogAlways("[W217CP] " .. out)')


def caravan_pass(r, secs=100):
    """F (scene 7): the save's caravan cart nearest the host; both players 20 m beside its path ahead; twice a second the cart on both machines
    and the host's riders against their copies on the joiner's cart."""
    def fn():
        t = r.a.ask(CART_NEAR, 'W217CN', 8)
        out = {'cart': t}
        if not t or t == 'none': return True, 'no-cart', out
        name, x, y, z, yaw = t.split(); x, y, z, yaw = float(x), float(y), float(z), float(yaw)
        import math
        fx, fy = -math.sin(yaw), math.cos(yaw)        # its forward (the writer's convention)
        sx, sy = fy, -fx
        ax, ay = x + fx * 35 + sx * 20, y + fy * 35 + sy * 20
        r.a.teleport(ax, ay); r.b.teleport(ax + 1.5, ay + 1.0)
        time.sleep(5)
        samples = []
        t0 = time.time()
        framed = False
        while time.time() - t0 < secs:
            got = {}
            th = [threading.Thread(target=lambda c=c, k=k: got.__setitem__(k, c.ask(CART_POSE % name, 'W217CP', 2) or 'none')) for c, k in ((r.a, 'h'), (r.b, 'j'))]
            for t_ in th: t_.start()
            for t_ in th: t_.join(timeout=4)
            h, j = got.get('h', 'none'), got.get('j', 'none')
            if h != 'none' and j != 'none':
                hf, jf = h.split(), j.split()
                hp = [float(v) for v in hf[:4]]; jp = [float(v) for v in jf[:4]]
                riders = [] if hf[4] == '-' else hf[4].split(',')
                jr = set() if jf[4] == '-' else set(jf[4].split(','))
                d = math.dist(hp[:3], jp[:3])
                samples.append({'t': round(time.time() - t0, 2), 'dist_m': round(d, 3), 'h_host': hp[3], 'h_joiner': jp[3],
                                'riders': len(riders), 'seated': sum(1 for n in riders if n in jr), 'player_m': round(math.dist(hp[:2], [ax, ay]), 1)})
            if not framed and samples and samples[-1]['player_m'] <= 25:
                framed = True
                out['pair_far'] = frame_pair(r, 'f-caravan-pass', name, move=False)   # the pass as the players see it (about 20 m)
            time.sleep(0.1)
        # the WO's framed pair: both viewers 3-8 m from the cart, nothing between (after the pass: the move would change what is measured)
        out['pair'] = frame_pair(r, 'f-caravan', name, move=True)
        near = [s for s in samples if s['player_m'] <= 40] or samples
        if near:
            ds = sorted(s['dist_m'] for s in near)
            out['summary'] = {'samples': len(near), 'dist_m_max': ds[-1], 'dist_m_p95': ds[int(0.95 * (len(ds) - 1))],
                              'height_joiner_max_m': max(abs(s['h_joiner']) for s in near), 'height_host_max_m': max(abs(s['h_host']) for s in near),
                              'within_10cm_of_ground': sum(1 for s in near if abs(s['h_joiner']) <= 0.10), 'within_1m_of_host': sum(1 for s in near if s['dist_m'] <= 1.0),
                              'rider_samples': sum(s['riders'] for s in near), 'seated_samples': sum(s['seated'] for s in near)}
        out['samples'] = samples
        with open(os.path.join(r.dir, 'f-caravan.json'), 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)
        return True, 'ok', {k: v for k, v in out.items() if k != 'samples'}
    return fn


# ---------------------------------------------------------------- G.2 / G.3: loose items and doors (measure first)

DOORS = ('local out = {}; local c = {x = %f, y = %f, z = %f}; for _, e in ipairs(System.GetEntitiesInSphere(c, %d) or {}) do '
         'if e.class == "AnimDoor" then local o, l = -1, -1; pcall(function() o = e:IsOpen() and 1 or 0 end); pcall(function() l = e:IsLocked() and 1 or 0 end); '
         'local q = e:GetWorldPos(); out[#out + 1] = string.format("%%.2f,%%.2f,%%.2f|%%d|%%d", q.x, q.y, q.z, o, l) end end; table.sort(out); '
         'System.LogAlways("[W217DR] " .. #out .. " " .. table.concat(out, ";", 1, math.min(#out, 40)))')
USE_DOOR = ('local best, bd; local c = {x = %f, y = %f, z = %f}; for _, e in ipairs(System.GetEntitiesInSphere(c, 1.0) or {}) do '
            'if e.class == "AnimDoor" then local q = e:GetWorldPos(); local d = (q.x - c.x) ^ 2 + (q.y - c.y) ^ 2 + (q.z - c.z) ^ 2; if not bd or d < bd then bd = d; best = e end end end; '
            'local ok = false; if best then ok = pcall(function() best:OnUsed(player, 0) end) end; System.LogAlways("[W217DU] " .. tostring(best ~= nil) .. " " .. tostring(ok))')


def _doors(c, at, r=60):
    t = c.ask(DOORS % (at[0], at[1], at[2], r), 'W217DR', 8) or ''
    out = {}
    for d in t.split(' ', 1)[-1].split(';'):
        p = d.split('|')
        if len(p) == 3: out[p[0]] = (int(p[1]), int(p[2]))
    return out


def _match(ha, jb):
    """The joiner's door nearest each host door (within 0.3 m), and whether open/locked agree."""
    def xyz(k): return [float(v) for v in k.split(',')]
    res = []
    for hk, hv in ha.items():
        hp = xyz(hk)
        best = None
        for jk, jv in jb.items():
            jp = xyz(jk)
            d = sum((a - b) ** 2 for a, b in zip(hp, jp)) ** 0.5
            if d <= 0.3 and (best is None or d < best[0]): best = (d, jk, jv)
        res.append({'door': hk, 'host': hv, 'joiner': best[2] if best else None, 'same': bool(best and best[2] == hv)})
    return res


def doors(r, n_each=int(os.environ.get('KCDMP_RIG_DOORS_EACH', '5')), watch_s=float(os.environ.get('KCDMP_RIG_DOORS_WATCH_S', '180'))):
    """G.3: the doors within 60 m of the village spot on both machines (open, locked); each player uses n_each doors the player's way
    (AnimDoor:OnUsed, the use key's function) standing at them; after 3 s both sides compared; then watch_s of NPCs using doors, compared
    every 15 s. Mismatches reported."""
    def fn():
        # the village's houses (gprobe: no door within 60 m of the bench's own spot) -- by the tavern, 120 m round
        at = (2330.0, 2070.0, 110.5)
        r.a.teleport(at[0], at[1]); r.b.teleport(at[0] + 2.0, at[1]); time.sleep(6)
        h0, j0 = _doors(r.a, at, 120), _doors(r.b, at, 120)
        out = {'doors_host': len(h0), 'doors_joiner': len(j0), 'start': _match(h0, j0)}
        keys = sorted(h0)[:2 * n_each]
        uses = []
        for i, k in enumerate(keys):
            who = r.a if i < n_each else r.b
            x, y, z = (float(v) for v in k.split(','))
            who.teleport(x + 1.0, y, z + 0.05); time.sleep(2.5)
            before = (_doors(r.a, (x, y, z), 2), _doors(r.b, (x, y, z), 2))
            used = who.ask(USE_DOOR % (x, y, z), 'W217DU', 5)
            time.sleep(3.0)
            after = (_doors(r.a, (x, y, z), 2), _doors(r.b, (x, y, z), 2))
            m = _match(after[0], after[1])
            uses.append({'door': k, 'by': 'host' if who is r.a else 'joiner', 'used': used, 'before': [list(before[0].values()), list(before[1].values())],
                         'after': [list(after[0].values()), list(after[1].values())], 'same_after': all(x_['same'] for x_ in m) if m else None})
        out['uses'] = uses
        r.a.teleport(*HOME); r.b.teleport(HOME[0] + 2.0, HOME[1])
        samples = []
        t0 = time.time()
        while time.time() - t0 < watch_s:
            time.sleep(15)
            m = _match(_doors(r.a, at), _doors(r.b, at))
            samples.append({'t': round(time.time() - t0), 'doors': len(m), 'mismatched': [x_ for x_ in m if not x_['same']][:8]})
        out['watch'] = samples
        out['summary'] = {'uses': len(uses), 'uses_same_after': sum(1 for u in uses if u['same_after']),
                          'by_host_same': sum(1 for u in uses if u['by'] == 'host' and u['same_after']), 'by_joiner_same': sum(1 for u in uses if u['by'] == 'joiner' and u['same_after']),
                          'watch_samples': len(samples), 'watch_mismatch_samples': sum(1 for s in samples if s['mismatched']),
                          'start_mismatched': sum(1 for x_ in out['start'] if not x_['same'])}
        with open(os.path.join(r.dir, 'g3-doors.json'), 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)
        return True, 'ok', out['summary']
    return fn


PICKABLES = ('local out = {}; local c = {x = %f, y = %f, z = %f}; for _, e in ipairs(System.GetEntitiesInSphere(c, %d) or {}) do '
             'if e.class == "PickableItem" then local q = e:GetWorldPos(); local cls = "?"; pcall(function() local it = e.item and ItemManager.GetItem(e.item:GetId()); if it then cls = tostring(it.class) end end); '
          'if cls == "?" or cls == "nil" then pcall(function() cls = tostring(e.Properties.sItemClassId or "?") end) end; '
             'local nm = "?"; pcall(function() nm = ItemManager.GetItemName(cls) or "?" end); '
             'out[#out + 1] = string.format("%%s|%%s|%%s|%%.2f,%%.2f,%%.2f", e:GetName(), cls, nm, q.x, q.y, q.z) end end; '
             'System.LogAlways("[W217PI] " .. #out .. " " .. table.concat(out, ";", 1, math.min(#out, 40)))')


def _picks(c, at, r):
    t = c.ask(PICKABLES % (at[0], at[1], at[2], r), 'W217PI', 8) or ''
    out = []
    for d in t.split(' ', 1)[-1].split(';'):
        p = d.split('|')
        if len(p) == 4: out.append({'ent': p[0], 'cls': p[1], 'name': p[2], 'at': [float(v) for v in p[3].split(',')]})
    return out


def loose_items(r, n_each=3):
    """G.2: loose world items (herbs as pickables, flowers, coins) within 80 m of the village spot on both machines; each player picks up
    n_each through PickableItem.OnUsed; 5 s later: is the item gone from the other's world (WO-134: the joiner asks the host first, the
    host's pickup removes the joiner's copy)?"""
    def fn():
        at = (HOME[0], HOME[1], 110.0)
        r.a.teleport(*HOME); r.b.teleport(HOME[0] + 2.0, HOME[1]); time.sleep(6)
        hp, jp = _picks(r.a, at, 80), _picks(r.b, at, 80)
        out = {'host_pickables': len(hp), 'joiner_pickables': len(jp), 'takes': []}

        def other_has(c, it):
            for o in _picks(c, it['at'], 2):
                if o['cls'] == it['cls']: return True
            return False

        for i, it in enumerate(hp[:2 * n_each]):
            who, oth = (r.a, r.b) if i < n_each else (r.b, r.a)
            # the item as the picker's own world names it (its entity names carry a per-process counter): the nearest of that class
            mine = [o for o in _picks(who, it['at'], 2) if o['cls'] == it['cls']]
            row = {'item': {k: it[k] for k in ('cls', 'name', 'at')}, 'by': 'host' if who is r.a else 'joiner'}
            if not mine:
                row['result'] = 'not-in-the-pickers-world'
                out['takes'].append(row)
                continue
            who.teleport(it['at'][0] + 0.6, it['at'][1], it['at'][2] + 0.05)
            time.sleep(2)
            row['other_had_it'] = other_has(oth, it)
            row['used'] = who.ask(PICKUP % mine[0]['ent'], 'W217U', 5)
            time.sleep(5)
            row['gone_from_picker'] = not any(o['cls'] == it['cls'] for o in _picks(who, it['at'], 1.0))
            row['gone_from_other'] = not other_has(oth, it)
            out['takes'].append(row)
        out['summary'] = {'takes': len(out['takes']), 'left_for_other': sum(1 for t in out['takes'] if t.get('other_had_it') and not t.get('gone_from_other'))}
        with open(os.path.join(r.dir, 'g2-loose.json'), 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)
        return True, 'ok', out['summary']
    return fn


# ---------------------------------------------------------------- C / D: the loot (scene 5)

# the host's own items as class sources (a body is filled with them for the bench's 8-plus items)
CLASSES_OF = ('local out, seen = {}, {}; for _, id in ipairs(player.inventory:GetInventoryTable() or {}) do local it = ItemManager.GetItem(id); '
              'if it and it.class and not seen[tostring(it.class)] then seen[tostring(it.class)] = true; out[#out + 1] = tostring(it.class) end end; '
              'System.LogAlways("[W217CL] " .. table.concat(out, ",", 1, math.min(#out, 30)))')
FILL = ('local e = System.GetEntityByName("%s"); local n = 0; if e then for cls in string.gmatch("%s", "[^,]+") do '
        'if pcall(function() e.inventory:CreateItem(cls, 1, 1) end) then n = n + 1 end end end; System.LogAlways("[W217FI] " .. n)')
# three of the body's items made stolen goods of an owner the house belongs to (the bench's stand-in for a house chest's): SetItemOwner
# WO-218 E.3: the game registers the owner write under its documented name or the documentation's own spelling (SerItemOwner); the owner
# is a WUID (the soul's this.id, else the entity's); each item's owner is read back -- a staging counts only an owner that took
STEAL3 = ('local e = System.GetEntityByName("%s"); local o = System.GetEntityByName("%s"); local n, how = 0, "none"; '
          'if e and o then local own = (o.this and o.this.id) or o.id; local t = e.inventory:GetInventoryTable() or {}; '
          'local f = ItemManager.SetItemOwner or ItemManager.SerItemOwner; how = (ItemManager.SetItemOwner and "SetItemOwner") or (ItemManager.SerItemOwner and "SerItemOwner") or "no-bind"; '
          'for i = 1, math.min(3, #t) do if f then pcall(function() f(t[i], own, true) end) end; '
          'local back = nil; pcall(function() back = ItemManager.GetItemOwner(t[i]) end); if back and back ~= 0 and tostring(back) == tostring(own) then n = n + 1 end end end; '
          'System.LogAlways("[W217ST] " .. n .. " " .. how)')
TAKE_IDX = ('local e = System.GetEntityByName("%s"); local t = e and e.inventory:GetInventoryTable() or {}; local id = t[%d]; local ok = false; '
            'local cls = "-"; if id then local it = ItemManager.GetItem(id); cls = it and tostring(it.class) or "-"; ok = pcall(function() player.inventory:AddItem(id) end) end; '
            'System.LogAlways("[W217TK] " .. tostring(ok) .. " " .. cls)')
TAKE_CLS = ('local e = System.GetEntityByName("%s"); local id = nil; for _, x in ipairs(e and e.inventory:GetInventoryTable() or {}) do '
            'local it = ItemManager.GetItem(x); if it and tostring(it.class) == "%s" then id = x; break end end; local ok = false; '
            'if id then ok = pcall(function() player.inventory:AddItem(id) end) end; System.LogAlways("[W217TC] " .. tostring(ok))')
CLS_LIST = ('local e = System.GetEntityByName("%s"); local out = {}; for _, x in ipairs(e and e.inventory:GetInventoryTable() or {}) do '
            'local it = ItemManager.GetItem(x); if it then out[#out + 1] = tostring(it.class) .. "/" .. tostring(it.amount or 1) end end; table.sort(out); '
            'System.LogAlways("[W217CS] " .. #out .. " " .. table.concat(out, ","))')
FLAGS_OF = ('local out = {}; for _, x in ipairs(player.inventory:GetInventoryTable() or {}) do local it = ItemManager.GetItem(x); local o = nil; '
            'pcall(function() o = ItemManager.GetItemOwner(x) end); if o then local nm = "?"; pcall(function() local oe = XGenAIModule.GetEntityByWUID(o); nm = oe and oe:GetName() or "?" end); '
            'out[#out + 1] = tostring(it and it.class) .. "@" .. nm end end; System.LogAlways("[W217FL] " .. #out .. " " .. table.concat(out, ";", 1, math.min(#out, 20)))')
CLOSE_SCREEN = 'pcall(function() UIAction.CallFunction("ItemTransfer", 0, "Close", true) end)'


def _cls_list(c, name):
    t = c.ask(CLS_LIST % name, 'W217CS', 6) or '0'
    parts = t.split(' ', 1)
    return parts[1].split(',') if len(parts) > 1 and parts[1] else []


def loot_scene(r, takes_each=4):
    """C / D (scene 5): a dead body with 8 or more items (the bench fills one with the host's own classes), three of them made stolen goods of
    a villager's; both players beside it, both loot screens open (the game's own OnLoot; the joiner's through the mod's request). The host
    takes takes_each items and the joiner takes_each, one by one in turns (each take is the move the screen makes: the item added to the
    taker's pack), two of them contested -- both take the same item within 200 ms (two threads). After every take, how long until the
    other's view of the body lost it (polled every 50 ms, up to 1.5 s). Frames: both screens after a contested take; the body after.
    The ledger is read from both logs by the analysis tool (loot217.py)."""
    def fn():
        import threading
        S = _S()
        out = {'takes': []}
        r.a.teleport(*HOME); r.b.teleport(HOME[0] + 2.0, HOME[1]); time.sleep(4)
        t = r.a.ask(BODY_NEAR % (900, 1), 'W217B', 8)
        out['body'] = t
        if not t or t == 'none': return True, 'no-body', out
        name, x, y, z, n = t.split()
        r.a.teleport(float(x) + 1.0, float(y), float(z) + 0.05); r.b.teleport(float(x) - 1.0, float(y), float(z) + 0.05)
        time.sleep(6)
        classes = (r.a.ask(CLASSES_OF, 'W217CL', 6) or '').split(',')
        have = int(n)
        if have < 8 and classes and classes[0]:
            out['filled'] = r.a.ask(FILL % (name, ','.join(classes[:8 - have + 1])), 'W217FI', 8)
        owner = os.environ.get('KCDMP_RIG_STOLEN_OWNER', 'ttkc_drozd')
        out['stolen_staged'] = r.a.ask(STEAL3 % (name, owner), 'W217ST', 6)
        time.sleep(3)
        out['host_items_0'] = _cls_list(r.a, name)
        # both screens open: the host's own loot, then the joiner's (its request to the host, the live list, then the game's screen)
        for c, slot in ((r.a, 0), (r.b, 1)):
            c.lua(AIM_AT % name); time.sleep(0.5)
            m0 = c.mark()
            c.lua('local e = System.GetEntityByName("%s"); if e then BasicAIActions.OnLoot(e, player, %d) end' % (name, slot))
            m, dt = _wait_any(c, m0['k'], SCREEN_IN, 15)
            out['screen_%s' % ('host' if c is r.a else 'joiner')] = round(dt, 2) if m else None
        time.sleep(3)
        out['joiner_items_0'] = _cls_list(r.b, name)

        def gone_after(c, cls, before):
            t0 = time.time()
            while time.time() - t0 < 1.5:
                now = _cls_list(c, name)
                if sum(1 for x in now if x.startswith(cls)) < before: return round(time.time() - t0, 3)
                time.sleep(0.05)
            return None

        order = []
        for i in range(takes_each):
            order += [('host', i), ('joiner', i)]
        contested = 0
        for k, (who, i) in enumerate(order):
            hl = _cls_list(r.a, name)
            if not hl: break
            cls = hl[0].split('/')[0]
            row = {'n': k, 'by': who, 'cls': cls}
            if contested < 2 and k in (2, 5):   # the third and the sixth take: both players at once, the same item
                contested += 1
                row['by'] = 'both'
                res = {}
                th = [threading.Thread(target=lambda c=c, kk=kk: res.__setitem__(kk, c.ask(TAKE_CLS % (name, cls), 'W217TC', 5))) for c, kk in ((r.a, 'host'), (r.b, 'joiner'))]
                for t_ in th: t_.start()
                for t_ in th: t_.join(timeout=10)
                row['calls'] = res
                time.sleep(2.5)
                if contested == 1:
                    time.sleep(0.5)
                    row['frame'] = {kk: shot(r, c, 'c-contested-%s' % kk) for c, kk in ((r.a, 'host'), (r.b, 'joiner'))}
            else:
                T, O = (r.a, r.b) if who == 'host' else (r.b, r.a)
                before_o = sum(1 for x in _cls_list(O, name) if x.startswith(cls))
                row['call'] = T.ask(TAKE_CLS % (name, cls), 'W217TC', 5)
                row['other_lost_after_s'] = gone_after(O, cls, before_o)
                time.sleep(1.0)
            out['takes'].append(row)
            note(r, 'w217-loot-take', **row)
        time.sleep(4)
        out['host_items_1'] = _cls_list(r.a, name)
        out['joiner_items_1'] = _cls_list(r.b, name)
        out['joiner_flags'] = r.b.ask(FLAGS_OF, 'W217FL', 6)
        out['host_flags'] = r.a.ask(FLAGS_OF, 'W217FL', 6)
        # the screens closed the player's way (Esc in each copy's window), then the stripped body framed from both views
        import keys
        for c in (r.a, r.b):
            k = keys.Keys(S.rec_of(c), 'wo217-loot-close')
            out.setdefault('esc', {})['host' if c is r.a else 'joiner'] = esc(c, k); k.release()   # r11: a stray Esc's pause menu closed
        time.sleep(3)
        out['pair_body'] = frame_pair(r, 'c-body-after', name)
        r.a.cmd('mp_w217_status'); r.b.cmd('mp_w217_status')
        r.a.lua('if KCD2MP_W217LootStatus then KCD2MP_W217LootStatus() end'); r.b.lua('if KCD2MP_W217LootStatus then KCD2MP_W217LootStatus() end')
        with open(os.path.join(r.dir, 'c-loot.json'), 'w', encoding='utf-8') as f: json.dump(out, f, indent=1)
        return True, 'ok', {k: out[k] for k in ('body', 'screen_host', 'screen_joiner', 'stolen_staged') if k in out}
    return fn
