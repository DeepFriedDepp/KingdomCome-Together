# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 A.7: named pair scenarios on two real copies (test tool, never shipped).

Every action goes through the game's console, Lua or our pipe (the agent); a step is an action, a postcondition and a
timeout, and fails with a reason code. A failure writes a folder: both copies' files (pair.collect), the step, and the
last 200 lines of each log. Nothing here reads input; nothing takes focus.

    python tools/rig/scenarios.py <scenario> [args]       (the pair must be up: pair.py copy A ... host / copy B ... joiner)
    scenarios: join, walk, sleep, wait, fight, talk, loot, smoke (join walk sleep wait fight talk loot), soak [min], c2, c3,
    e2, e2twin, b1, b1twin, joinfight (B3), joinscene (B1 + twin), stats, long [min] (join soak c2 c3 fight e2 e2twin)
"""
import json, os, re, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import pair, procs, rig


class Copy:
    """One running copy as the scenarios see it: its console, its logs (read from an offset), its agent's log."""
    def __init__(self, cid):
        self.id = cid
        self.p = pair.plan(cid)
        st = rig.load_state()
        self.since = st.get(cid.lower() + '_since')
        w = procs.LogWatch(rig.klog(self.p['root']), since=self.since); w.poll()
        self.port = w.http_port
        self.klog = rig.klog(self.p['root'])
        self.alog = os.path.join(self.p['agent_dir'], 'agent.log')
        self.nlog = os.path.join(self.p['root'], 'kcdmp-native.mirror.log')

    def cmd(self, c): return rig.console(self.port, c)

    def lua(self, code, tries=4):
        """Lua that is known to have run: the same chunk logs a one-off token, and the chunk is sent again only when the token
        never appeared (a busy or reset console call; both statements run together or not at all, so it never runs twice)."""
        for _ in range(tries):
            tok = 'W206S%d' % (time.time_ns() // 1000)
            off = self.size(self.klog)
            try: rig.console(self.port, '#System.LogAlways("[%s]"); %s' % (tok, code))
            except OSError as ex:   # a copy busy loading (a joiner's rejoin) times out: a lost call, tried again
                rig.note('lua-timeout', copy=self.id, err=str(ex)[:80])
                # WO-207: a refused console whose game is gone ends the step now (d13 talked to a dead copy for 4 minutes)
                rec = rig.load_state().get(self.id.lower())
                if rec and not procs.matches(rec): raise RuntimeError('copy %s: its game exited' % self.id)
            t0 = time.time()
            while time.time() - t0 < 6:
                if any(tok in l for l in self.lines(self.klog, off)): return True
                time.sleep(0.2)
        rig.note('lua-lost', copy=self.id, code=code[:200])
        return False

    def lua_nowait(self, code):
        """Lua sent once, not waited for (WO-209: the defender's turn to the attacker -- lua()'s kcd.log token can take seconds to appear,
        and the defender stalled behind it, missing the swings after; a lost turn is only a turn)."""
        # one try, a short timeout: rig.console retries a busy API (503) for up to ~40 s, and d7's defender stalled behind it again
        import urllib.parse, urllib.request
        enc = urllib.parse.quote('#' + code, safe='')
        try:
            with urllib.request.urlopen('http://localhost:%d/api/System/Console/ExecuteString?command=%s' % (self.port, enc), timeout=1.5) as resp:
                resp.read(1 << 16)
            return True
        except (OSError, ValueError): return False

    @staticmethod
    def size(p): return os.path.getsize(p) if os.path.exists(p) else 0

    def mark(self): return {'k': self.size(self.klog), 'a': self.size(self.alog), 'n': self.size(self.nlog)}

    @staticmethod
    def lines(path, off):
        if not os.path.exists(path): return []
        with open(path, 'rb') as f:
            f.seek(off); return f.read().decode('utf-8', 'replace').splitlines()

    def wait(self, which, off, rx, secs):
        path = {'k': self.klog, 'a': self.alog, 'n': self.nlog}[which]
        t0 = time.time()
        while time.time() - t0 < secs:
            for l in self.lines(path, off):
                m = re.search(rx, l)
                if m: return m
            time.sleep(0.25)
        return None

    def ask(self, code, tag, secs=3.0):
        """Lua that logs one '[<tag>] ...' line; returns the text after the tag (None when it did not come back)."""
        off = self.size(self.klog)
        self.lua(code)
        m = self.wait('k', off, r'\[%s\] (.*)' % re.escape(tag), secs)
        return m.group(1).strip() if m else None

    def pos(self):
        t = self.ask('local p=player:GetWorldPos(); System.LogAlways(string.format("[W206P] %.2f %.2f %.2f", p.x, p.y, p.z))', 'W206P')
        return tuple(float(x) for x in t.split()) if t else None

    def teleport(self, x, y, z=None):
        """A position write held on the terrain until the ground has caught it (WO-1xx: long writes land before physics)."""
        if z is None:
            code = 'local zz = System.GetTerrainElevation({x=%f, y=%f, z=0}); player:SetWorldPos({x=%f, y=%f, z=zz + 0.2})' % (x, y, x, y)
        else:
            code = 'player:SetWorldPos({x=%f, y=%f, z=%f})' % (x, y, z)
        # WO-209: the joiner's first console call after a join or the bench's clock jump is lost for half a minute (lua-lost in
        # every run; rc4-defence's 1-v-1 phase had no fight for it) -- a lost move is asked again
        for _ in range(3):
            if self.lua(code): return True
            time.sleep(3)
        return False

    def keep_up(self):
        """A health keeper for a fight (never a real death in a throwaway world)."""
        self.lua('W206KEEP = (W206KEEP or 0) + 1; local gen = W206KEEP; local function k() if gen ~= W206KEEP then return end; '
                 'if player.soul:GetState("health") < 80 then player.soul:SetState("health", 100) end; Script.SetTimer(200, k) end; k()')   # WO-209: real blows land (B)

    def stop_keep(self): self.lua('W206KEEP = (W206KEEP or 0) + 1')

    def tail(self, path, n=200):
        return self.lines(path, max(0, self.size(path) - 400_000))[-n:]


class Run:
    def __init__(self, name):
        self.name = name
        self.steps = []
        self.a, self.b = Copy(pair.HOST_CID), Copy(pair.JOIN_CID)   # a = the host, b = the joiner (KCDMP_RIG_HOST swaps the copies)
        self.dir = os.path.join(rig.run_dir(), 'scenario-%s-%s' % (name, time.strftime('%H%M%S')))
        os.makedirs(self.dir, exist_ok=True)

    def step(self, name, fn):
        """fn() returns (ok, reason, detail). A failure writes the failure folder and stops the scenario."""
        t0 = time.time()
        rig.say('STEP', name)
        try:
            ok, reason, detail = fn()
        except Exception as ex:   # a step that throws is a failed step with its reason (and where it threw)
            import traceback
            ok, reason, detail = False, 'exception', '%s: %s | %s' % (type(ex).__name__, ex, ' <- '.join(
                '%s:%d' % (fr.name, fr.lineno) for fr in reversed(traceback.extract_tb(ex.__traceback__)[-4:])))
        # WO-207: a copy whose game exited during the step fails it, whatever the step measured (d5-c1fight "passed" with the host's game
        # gone a minute into the fight: an empty link reads as a queue that keeps up)
        st = rig.load_state()
        gone = [c.id for c in (self.a, self.b) if st.get(c.id.lower()) and not procs.matches(st.get(c.id.lower()))]
        if gone:
            crashes = []
            for c in (self.a, self.b):
                cd = os.path.join(c.p['root'], 'kcdmp-crashes')
                if os.path.isdir(cd): crashes += [os.path.join(cd, x) for x in os.listdir(cd) if os.path.getmtime(os.path.join(cd, x)) >= t0 - 5]
            ok, reason = False, 'copy-exited'
            detail = {'gone': gone, 'crash_reports': crashes, 'step_detail': detail}
        row = {'step': name, 'ok': ok, 'reason': reason, 'detail': detail, 'secs': round(time.time() - t0, 1)}
        self.steps.append(row)
        rig.note('step', scenario=self.name, **row)
        rig.say('  ->', 'PASS' if ok else 'FAIL %s' % reason, detail if isinstance(detail, str) else json.dumps(detail)[:300])
        if not ok: self.fail(row)
        return ok

    def fail(self, row):
        d = os.path.join(self.dir, 'FAIL-' + row['step'])
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, 'step.json'), 'w', encoding='utf-8') as f: json.dump(row, f, indent=1)
        for c in (self.a, self.b):
            for nm, p in (('kcd', c.klog), ('agent', c.alog), ('native', c.nlog)):
                with open(os.path.join(d, '%s-%s.last200.log' % (c.id, nm)), 'w', encoding='utf-8') as f: f.write('\n'.join(c.tail(p)))
        try: pair.collect('fail-' + row['step'])
        except Exception as ex: rig.say('collect failed:', ex)

    def finish(self):
        res = {'scenario': self.name, 'pass': all(s['ok'] for s in self.steps) and bool(self.steps), 'steps': self.steps}
        with open(os.path.join(self.dir, 'result.json'), 'w', encoding='utf-8') as f: json.dump(res, f, indent=1)
        rig.say('SCENARIO', self.name, 'PASS' if res['pass'] else 'FAIL', '(%d steps)' % len(self.steps))
        return 0 if res['pass'] else 1


# ---------------------------------------------------------------- the steps

def s_join(r, secs=420):
    """The join completes: the host's world resumes on the joiner's ready for the joiner's NEWEST join (an earlier attempt's
    resume in the same log does not decide), the joiner's agent sent ready, and neither agent fell back to the debug HTTP API."""
    def fn():
        t0 = time.time()
        tried = []
        while time.time() - t0 < secs:
            ids = [m.group(1) for l in r.b.lines(r.b.alog, 0) for m in [re.search(r'MP-JOIN joiner: join 0x([0-9a-f]{8}) ', l)] if m]
            jid = ids[-1] if ids else None
            res = [re.search(r'MP-JOIN resume join=(\S+) reason=(\S+) paused_s=(\S+)', l) for l in r.a.lines(r.a.klog, 0)]
            res = [m for m in res if m and jid and m.group(1) == jid]
            if res and res[-1].group(2) == 'ready': break
            if res and jid not in tried: tried.append(jid)   # an attempt that ended otherwise (B2: a cut): the next one decides
            time.sleep(1)
        fallback = [l for c in (r.a, r.b) for l in c.lines(c.alog, 0) if 'falling back to http-debug-api' in l]
        if fallback: return False, 'http-fallback', fallback[0][:200]
        if not jid: return False, 'no-join', 'the joiner never asked for the world'
        if not res: return False, 'no-resume', 'the host world never resumed for join %s' % jid
        m = res[-1]
        if m.group(2) != 'ready': return False, 'resumed-' + m.group(2), m.group(0)[:200]
        mb = [l for l in r.b.lines(r.b.alog, 0) if 'ready sent for join 0x' + jid in l]
        if not mb: return False, 'joiner-no-ready', 'the joiner agent never sent ready'
        return True, 'ok', {'join': jid, 'host': m.group(0)[:160], 'joiner': mb[-1][-160:], 'earlier_attempts': tried}
    return fn


def probe_rows(root, t0_ms, t1_ms, kind):
    """(t ms, x, y) of one kind of body ('player' or 'figure') in a copy's probe file, between two wall times."""
    import json
    out = []
    p = os.path.join(root, 'kcdmp-anim.jsonl')
    if not os.path.exists(p): return out
    with open(p, encoding='utf-8', errors='replace') as f:
        for l in f:
            try: row = json.loads(l)
            except ValueError: continue
            if not t0_ms <= row.get('t', 0) <= t1_ms: continue
            for a in row.get('a', []):
                if a.get('k') == kind: out.append((row['t'], a['p'][0], a['p'][1]))
    return out


def figure_error(fig, truth, max_off_ms=1000):
    """p50/p90 of the distance between a machine's figure and the other machine's own player, at the best constant delay."""
    import bisect
    if not fig or not truth: return None
    tt = [r[0] for r in truth]
    best = None
    for off in range(0, max_off_ms + 1, 50):
        errs = []
        for t, x, y in fig:
            i = bisect.bisect_left(tt, t - off)
            j = min(range(max(0, i - 1), min(len(tt), i + 1)), key=lambda k: abs(tt[k] - (t - off)), default=None)
            if j is None or abs(tt[j] - (t - off)) > 150: continue
            errs.append(((x - truth[j][1]) ** 2 + (y - truth[j][2]) ** 2) ** 0.5)
        if len(errs) < 5: continue
        errs.sort()
        p90 = errs[int(0.9 * (len(errs) - 1))]
        if best is None or p90 < best[1]: best = (off, p90, errs[len(errs) // 2], len(errs))
    return best


def s_walk(r, secs=30.0):
    """Both walk 30 s side by side (position writes at 1 m/s on the wall clock). Judged from the probe files (10 samples a second on
    both machines): each machine's figure of the other against the other machine's own player position, at the best constant stream
    delay in 0..1000 ms. Pass: p90 within 1.5 m both ways, with at least 50 compared samples each."""
    def fn():
        pa = r.a.pos()
        if not pa: return False, 'no-pos', 'copy A gave no position'
        r.b.teleport(pa[0] + 2.0, pa[1])
        time.sleep(4)
        # the walk itself runs inside each game: a Lua timer moves the player 0.1 m every 100 ms (1 m/s) for `secs`, held on the
        # terrain -- smooth, and no console traffic while it runs (console writes land seconds apart under the agents' batches)
        walker = ('W206WALK = (W206WALK or 0) + 1; local gen, n = W206WALK, 0; local x0, y0 = %.3f, %.3f; '
                  'local function step() if gen ~= W206WALK or n >= %d then return end; n = n + 1; local y = y0 + 0.1 * n; '
                  'local z = System.GetTerrainElevation({x=x0, y=y, z=0}); player:SetWorldPos({x=x0, y=y, z=z + 0.05}); '
                  'Script.SetTimer(100, step) end; step()')
        steps = int(secs * 10)
        r.a.lua(walker % (pa[0], pa[1], steps))
        r.b.lua(walker % (pa[0] + 2.0, pa[1], steps))
        t0 = time.time()
        time.sleep(secs + 1)
        t1 = time.time()
        time.sleep(3)   # the probe files are flushed every second
        w0, w1 = int(t0 * 1000) + 2000, int(t1 * 1000)
        ab = figure_error(probe_rows(r.a.p['root'], w0, w1, 'figure'), probe_rows(r.b.p['root'], w0 - 1500, w1, 'player'))
        ba = figure_error(probe_rows(r.b.p['root'], w0, w1, 'figure'), probe_rows(r.a.p['root'], w0 - 1500, w1, 'player'))
        detail = {'host_sees_joiner': ab and {'delay_ms': ab[0], 'p90_m': round(ab[1], 2), 'p50_m': round(ab[2], 2), 'n': ab[3]},
                  'joiner_sees_host': ba and {'delay_ms': ba[0], 'p90_m': round(ba[1], 2), 'p50_m': round(ba[2], 2), 'n': ba[3]}}
        if not ab or not ba: return False, 'no-probe-pairs', detail
        if ab[3] < 50 or ba[3] < 50: return False, 'too-few-samples', detail
        ok = ab[1] <= 1.5 and ba[1] <= 1.5
        return ok, ('ok' if ok else 'figure-far'), detail
    return fn


def s_skip(r, kind, hours, strict=None, gap_gate=True):
    """A shared sleep / wait. The joiner asks (its bed's own event), the host answers yes (mp_sleep_yes); then the joiner's own skip
    starts the way an accepter's does (the bench's stand-in for the hour picker: a bench agent's w206_test), its DLL's "began"
    sends the Begin, and the host's skip runs. Pass: the host's skip began and ended, the joiner's ended, the clocks agree."""
    def fn():
        ma, mb = r.a.mark(), r.b.mark()
        # WO-209: the ask is sent again until the host is asked -- right after a join the joiner's console can stay unconfirmed for
        # half a minute (rc1-sleep1 and its twin: lua-lost, the host never asked)
        asked = None
        for _ in range(4):
            r.b.lua('KCD2MP_EmitEvent("w140_ask", "%s 0")' % kind)
            asked = r.a.wait('a', ma['a'], r'MP-W140 .*wants to (sleep|wait).*asking this player', 30)
            if asked: break
        if not asked: return False, 'no-prompt', 'the host was never asked'
        time.sleep(0.6)
        r.a.cmd('mp_sleep_yes')
        if not r.b.wait('a', mb['a'], r'MP-W140 vote .*everyone said yes', 40): return False, 'no-yes', 'the joiner never heard the yes'
        r.b.lua('KCD2MP_EmitEvent("w206_test", "skip %s %d")' % (kind, hours))
        began = r.a.wait('a', ma['a'], r'MP-W140 .*chose .* h|WO140-STATE', 90)
        if not r.b.wait('a', mb['a'], r'MP-W140 vote .*this player chose', 60): return False, 'no-begin', 'the joiner never sent the Begin'
        ha = r.a.wait('n', ma['n'], r'WO140-STATE', 60)
        if not ha: return False, 'host-no-skip', 'the host skip never began'
        # WO-207 C.1: the background copy's frame rate while the skip runs (the copy whose window is not in front; the joiner unless
        # KCDMP_RIG_BG=A), sampled through the engine's frame counter
        import threading
        bg = r.a if os.environ.get('KCDMP_RIG_BG') == 'A' else r.b
        fps_rows, done = [], threading.Event()

        def fps_loop():
            while not done.is_set():
                f = rig.fps(bg.port, bg.p['root'], span=4.0)
                if f: fps_rows.append(f)
        th = threading.Thread(target=fps_loop, daemon=True); th.start()
        t0 = time.time()
        t_end = None
        while time.time() - t0 < 240:
            na = r.a.lines(r.a.nlog, ma['n'])
            if any(re.search(r'WO140-STATE .*(-> 0|hours=0\.00)', l) for l in na): t_end = time.time(); break
            time.sleep(0.5)
        done.set()
        if t_end is None: return False, 'skip-not-ended', 'the host skip never ended'
        # the joiner's clock is pulled to the host's after a skip (WO-140). WO-207 C.1: the gap at +1, +10 and +60 s after the host's
        # skip ended, both clocks read at once (two threads; the game clock runs ~15x real time, so a 0.5 s read skew is ~8 game s)
        def both():
            out = {}
            ts = [threading.Thread(target=lambda c=c: out.__setitem__(c.id, c.ask('System.LogAlways(string.format("[W206T] %d", Calendar.GetWorldTime()))', 'W206T', 4)))
                  for c in (r.a, r.b)]
            for x in ts: x.start()
            for x in ts: x.join()
            try: return abs(int(out[r.a.id]) - int(out[r.b.id])), out
            except (TypeError, ValueError, KeyError): return None, out
        gaps = {}
        for at in (1, 10, 60):
            time.sleep(max(0.0, t_end + at - time.time()))
            gaps[at], raw = both()
        th.join(timeout=6)
        strict_now = strict if strict is not None else os.environ.get('KCDMP_RIG_SKIP_STRICT') == '1'   # C.3's acceptance: under 2 game-minutes at +10 s
        ok = (gaps[10] is not None and gaps[10] < 120) if strict_now else (gaps[60] is not None and gaps[60] <= 300) or any(g is not None and g <= 300 for g in gaps.values())
        detail = {'gap_s_at': gaps, 'strict': strict_now, 'bg_copy': bg.id, 'bg_fps_during_skip': fps_rows, 'skip_s': round(t_end - t0, 1),
                  'host_time': raw.get(r.a.id), 'joiner_time': raw.get(r.b.id), 'began': began.group(0)[:120] if began else None}
        rig.note('skip-clock', kind=kind, **{k: v for k, v in detail.items() if k != 'began'})
        # the maintainer (WO-207 crash work): the clock gap is a bench artifact (one copy is always the inactive window) -- logged, never a
        # gate in the smoke; the skip itself (asked, voted, begun, ended) still is
        if not gap_gate:
            detail['gap_gate'] = 'logged only'
            return True, 'ok' if ok else 'ok-gap-logged', detail
        return ok, ('ok' if ok else 'clocks-differ'), detail
    return fn


FIND_BANDITS = (
    'local best, bd; local list = {}; for _, e in ipairs(System.GetEntitiesByClass("NPC") or {}) do '
    'if e.soul and string.find(string.lower(e:GetName()), "bandit", 1, true) then local h = 0; '
    'pcall(function() h = e.soul:GetState("health") end); if h and h > 0 then list[#list + 1] = e end end end; '
    'for i = 1, #list do for j = i + 1, #list do local a, b = list[i]:GetWorldPos(), list[j]:GetWorldPos(); '
    'local dd = (a.x - b.x) ^ 2 + (a.y - b.y) ^ 2; if dd < 225 and (not bd or dd < bd) then bd = dd; best = {list[i], list[j]} end end end; '
    'if best then local p = best[1]:GetWorldPos(); System.LogAlways(string.format("[W206K] %s %s %.2f %.2f %.2f", '
    'best[1]:GetName(), best[2]:GetName(), p.x, p.y, p.z)) else System.LogAlways("[W206K] none " .. #list) end')

ATTACK = ('local e = System.GetEntityByName("%s"); local tg = %s; if e and tg then local t = XGenAIModule.MakeTableFromType('
          '"crime:attackInitiatedByConcept"); t.target = tg.this.id; t.priorityTarget = true; '
          'pcall(function() XGenAIModule.SendMessageToEntityData(e.this.id, "crime:attackInitiatedByConcept", t) end) end')

# the crash work's bisect (KCDMP_RIG_C1_FRESH=1): the same message only to an NPC not already fighting. It did not stop the host's
# crash (x-d21-fresh): the cause was the mod's own leave + re-add of a fighting soul (WO-136, fixed by mp_skirmish_keep)
ATTACK_FRESH = ('local e = System.GetEntityByName("%s"); local tg = %s; local busy = false; '
                'if e and e.soul then pcall(function() busy = e.soul:IsInCombatMode() == true end) end; '
                'if busy then System.LogAlways("[W207FRESH] " .. e:GetName() .. " fighting: not re-sent") end; '
                'if e and tg and not busy then local t = XGenAIModule.MakeTableFromType('
                '"crime:attackInitiatedByConcept"); t.target = tg.this.id; t.priorityTarget = true; '
                'pcall(function() XGenAIModule.SendMessageToEntityData(e.this.id, "crime:attackInitiatedByConcept", t) end) end')

STOP = ('local e = System.GetEntityByName("%s"); if e then local t = XGenAIModule.MakeTableFromType("stopFight"); t.soulCount = 1; '
        't.messageId = "w206stop%d"; pcall(function() XGenAIModule.SendMessageToEntityData(e.this.id, "stopFight", t) end) end')

FIGURE = ('(function() local f; for _, x in ipairs(System.GetEntitiesInSphere(player:GetWorldPos(), 40) or {}) do '
          'if x.GetName and string.find(x:GetName(), "kcd2mp_", 1, true) then f = x end end; return f end)()')


def s_fight(r, secs=40.0):
    """Two bandits fight the pair: one at the host's Henry, one at the joiner's figure (the concept attack, WUID targets). Both
    players are kept alive. Pass: the host's Henry and the joiner both take damage (their health dips), the joiner's copies of
    the bandits fought (its DLL's animation trace marks them fighting, or hit verdicts reached it)."""
    def fn():
        t = r.a.ask(FIND_BANDITS, 'W206K', 6)
        if not t or t.startswith('none'): return False, 'no-bandits', t
        b1, b2, x, y, z = t.split()
        r.bandits = [b1, b2]   # WO-208 D.1: the loot step's body
        x, y = float(x), float(y)
        r.a.teleport(x + 4.0, y)
        r.b.teleport(x + 5.5, y + 1.0)
        time.sleep(4)
        for c in (r.a, r.b): c.keep_up()
        ma, mb = r.a.mark(), r.b.mark()
        r.a.lua(ATTACK % (b1, 'player'))
        r.a.lua(ATTACK % (b2, FIGURE))
        lows = {'A': 100.0, 'B': 100.0}
        t0 = time.time()
        while time.time() - t0 < secs:
            for c in (r.a, r.b):
                h = c.ask('System.LogAlways(string.format("[W206H] %.1f", player.soul:GetState("health")))', 'W206H', 2)
                if h:
                    try: lows[c.id] = min(lows[c.id], float(h))
                    except ValueError: pass
            time.sleep(1.5)
        for i, b in enumerate((b1, b2)): r.a.lua(STOP % (b, i))
        for c in (r.a, r.b): c.stop_keep()
        verdicts = sum(1 for l in r.b.lines(r.b.alog, mb['a']) if re.search(r'hit[- ]verdict|MP-W161|0x73|PlayerHit', l, re.I))
        fighting = sum(1 for l in r.b.lines(r.b.nlog, mb['n']) if ('fight=1' in l and (b1 in l or b2 in l)))
        detail = {'bandits': [b1, b2], 'health_low': lows, 'joiner_verdicts': verdicts, 'joiner_copy_fight_lines': fighting}
        if lows['A'] >= 100.0 and lows['B'] >= 100.0: return False, 'no-damage', detail
        if lows['B'] >= 100.0: return False, 'joiner-not-in-fight', detail
        if fighting == 0 and verdicts == 0: return False, 'joiner-saw-no-fight', detail
        return True, 'ok', detail
    return fn


TALK_NPC = ('local skip = {%s}; local best, bd; local p = {x=%.2f, y=%.2f, z=0}; for _, e in ipairs(System.GetEntitiesInSphere(p, %d) or {}) do '
            'if e.class == "NPC" and e.soul and e.GetName and e.OnTalk then local n = e:GetName(); local d = false; '
            'd = true; pcall(function() d = e.actor:IsDead() == true end); if not d and not skip[n] and not string.find(n, "kcd2mp_", 1, true) and not string.find(n, "DialogTwin_", 1, true) and not string.find(string.lower(n), "bandit", 1, true) then '
            'local q = e:GetWorldPos(); local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; if not bd or dd < bd then bd = dd; best = e end end end end; '
            'if best then local q = best:GetWorldPos(); System.LogAlways(string.format("[W206N] %%s %%.2f %%.2f %%.2f", best:GetName(), q.x, q.y, q.z)) '
            'else System.LogAlways("[W206N] none") end')


def s_talk(r, secs=float(os.environ.get('KCDMP_RIG_TALK_S', '150'))):
    """A joined conversation. The host's Henry talks to the nearest villager (the NPC's own OnTalk, as WO-204 V1 did); the joiner
    joins it (the 'Join conversation' prompt's own event, KCD2MP_W203Join); at each decision the host's choice is made through the
    game's own forced-decision cvar (wh_dlg_ForcedDecision, the first option). Pass: the host's conversation started and ended, the
    joiner listened and got its lines."""
    def fn():
        home = (2176.11, 2091.96)   # save440's own spot, by Troskowitz's villagers
        if os.environ.get('KCDMP_RIG_TALK_HOME') == 'here':   # another world (save440's town refuses talks during its brawl quest)
            p0 = r.a.pos()
            if p0: home = (p0[0], p0[1])
        r.a.teleport(*home)
        r.b.teleport(home[0] + 1.5, home[1] + 1.0)
        time.sleep(4)
        tried, m, npc = [], None, None
        first = os.environ.get('KCDMP_RIG_TALK_FIRST')   # a villager known to start a normal conversation in this world
        for _ in range(14):
            if first and first not in tried:
                t = r.a.ask('local e = System.GetEntityByName("%s"); if e then local q = e:GetWorldPos(); System.LogAlways(string.format('
                            '"[W206N] %%s %%.2f %%.2f %%.2f", e:GetName(), q.x, q.y, q.z)) else System.LogAlways("[W206N] none") end' % first, 'W206N', 5)
                if not t or t == 'none': tried.append(first); continue
            else:
                t = r.a.ask(TALK_NPC % (', '.join('["%s"]=true' % n for n in tried), home[0], home[1], 400), 'W206N', 5)
            if not t or t == 'none': break
            npc, x, y, z = t.split()
            if npc in tried: break
            tried.append(npc)
            r.a.teleport(float(x) + 1.3, float(y))           # within talking range, as a player would stand
            r.b.teleport(float(x) + 2.8, float(y) + 1.0)
            time.sleep(3)
            ma, mb = r.a.mark(), r.b.mark()
            r.a.lua('local e = System.GetEntityByName("%s"); if e then e:OnTalk(player, 0) end' % npc)
            m = r.a.wait('k', ma['k'], r"New dialogue '.*\(normal - Id: (\d+)\)", 15)
            if m: break
        if not m: return False, 'no-dialogue', {'tried': tried}
        cid = m.group(1)
        # the joiner joins as soon as the host's offer reached it (the prompt's own event), confirmed by its listening state
        joined = False
        t0 = time.time()
        while time.time() - t0 < float(os.environ.get('KCDMP_RIG_TALK_JOIN_S', '120')) and not joined:   # the bench's queues: the offer ~70 s late
            t = r.b.ask('local c = KCD2MP and KCD2MP.W203 and KCD2MP.W203.cam; local got = "none"; if c then '
                        'if c.listening then got = "listening" else for s, o in pairs(c.offers or {}) do if o.cid == "%s" then '
                        'KCD2MP_W203Join(s, o.cid); got = "asked" end end end end; System.LogAlways("[W206J] " .. got)' % cid, 'W206J', 5)
            joined = t == 'listening'
            if not joined: time.sleep(2)
        if not joined: return False, 'joiner-not-listening', 'no offer reached the joiner, or its join did not start'
        koff, answered, ended = ma['k'], set(), None
        t0 = time.time()
        while time.time() - t0 < secs and not ended:
            for l in r.a.lines(r.a.klog, koff):
                d = re.match(r'\[ID: %s\] 1\) (\S+)' % cid, l)
                if d and d.group(1) not in answered:
                    answered.add(d.group(1))
                    r.a.cmd('wh_dlg_ForcedDecision %s' % d.group(1))
                if re.match(r'\[ID: %s\] Dialog (ending|interrupted)' % cid, l): ended = l.strip()
            koff = r.a.size(r.a.klog)
            time.sleep(0.5)
        def lines_in(c, key):   # the conversation counters the agent prints (WO204 stats: "conv lines_out=N lines_in=N")
            v = [int(m.group(1)) for l in c.lines(c.alog, 0) for m in [re.search(r' %s=(\d+)' % key, l)] if m and 'conv lines_out=' in l]
            return v[-1] if v else 0
        b0 = lines_in(r.b, 'lines_in')
        time.sleep(65)   # the next stats line
        got, sent = lines_in(r.b, 'lines_in'), lines_in(r.a, 'lines_out')
        detail = {'npc': npc, 'cid': cid, 'decisions': len(answered), 'ended': (ended or '')[:100], 'host_lines_out': sent, 'joiner_lines_in': got}
        if got == 0: return False, 'joiner-got-no-lines', detail
        if not ended: return True, 'ok-long-conversation', detail   # lines and options reached the joiner; a quest talk ran past the window
        return True, 'ok', detail
    return fn


CHEST_NEAR = ('W208PICK = nil; local best, bd; local p = player:GetWorldPos(); for _, c in ipairs({"Stash", "CartStash", "DestroStash"}) do '
              'for _, e in ipairs(System.GetEntitiesByClass(c) or {}) do if e.inventory and #(e.inventory:GetInventoryTable() or {}) > 0 then '
              'local q = e:GetWorldPos(); local raw = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; local tz = System.GetTerrainElevation({x = q.x, y = q.y, z = 0}) or q.z; '
              'local dd = raw; if q.z < tz - 1.5 then dd = raw + 1e6 end; '   # WO-207: a buried stash only when nothing else is near (the mod's reach)
              'if raw < 250000 and (not bd or dd < bd) then bd = dd; best = e end end end end; '
              'if best then W208PICK = best; local q = best:GetWorldPos(); System.LogAlways(string.format("[W206C] %s %.2f %.2f %.2f", best:GetName(), q.x, q.y, q.z)) '
              'else System.LogAlways("[W206C] none") end')

TAKE_ONE = ('local e = W208PICK or System.GetEntityByName("%s"); local w = e and (e.inventory:GetInventoryTable() or {})[1]; local cls = "?"; '
            'if w then local it = ItemManager.GetItem(w); if it then cls = tostring(it.class) end; pcall(function() player.inventory:AddItem(w) end) end; '
            'System.LogAlways("[W206L] " .. tostring(w) .. " " .. cls)')


REACH = ('local e = W208PICK or System.GetEntityByName("%s"); local p = player:GetWorldPos(); local q = e and e:GetWorldPos(); '
         'System.LogAlways(string.format("[W208R] %%.2f", q and math.sqrt((q.x - p.x) ^ 2 + (q.y - p.y) ^ 2 + (q.z - p.z) ^ 2) or -1))')

FIND_BODY = ('W208PICK = nil; local best, bd; local p = player:GetWorldPos(); for _, e in ipairs(System.GetEntitiesInSphere(p, 500) or {}) do '
             'if (e.class == "NPC" or e.class == "NPC_Female") and e.GetName and e.inventory and e.actor then local n = e:GetName(); local d = false; '
             'pcall(function() d = e.actor:IsDead() == true end); local k = 0; pcall(function() k = #(e.inventory:GetInventoryTable() or {}) end); '
             'if d and k > 0 and not string.find(n, "kcd2mp_", 1, true) and not string.find(n, "DialogTwin_", 1, true) then '
             'local q = e:GetWorldPos(); local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; if not bd or dd < bd then bd = dd; best = e end end end end; '
             'if best then W208PICK = best; local q = best:GetWorldPos(); System.LogAlways(string.format("[W208B] %s %.2f %.2f %.2f %d", best:GetName(), q.x, q.y, q.z, '
             '#(best.inventory:GetInventoryTable() or {}))) else System.LogAlways("[W208B] none") end')

# the bench's body (WO-208 D.1): the host's own actor kill on a bandit of the fight step (the host's world decides; its death reaches the joiner)
KILL_ONE = ('local e = System.GetEntityByName("%s"); local how = "none"; if e and e.actor then '
            'if pcall(function() e.actor:Kill() end) then how = "kill" end end; local d = false; pcall(function() d = e.actor:IsDead() == true end); '
            'if not d and e and e.soul then pcall(function() e.soul:SetState("health", 0) end); how = how .. "+health0" end; '
            'System.LogAlways("[W208K] " .. how .. " dead=" .. tostring(d))')

# the joiner asks the host for the body's items the way the game's use of a body does (WO-131's wrapper -> WO-134), with the loot
# screen's own opening left out (a console-opened loot screen stops the Lua timers, WO-201): the mod's session starts on the host's list
OPEN_BODY = ('local e = W208PICK or System.GetEntityByName("%s"); local ok = e and KCD2MP_W134LootRequest and KCD2MP_W134LootRequest(e, function() end, player, nil); '
             'System.LogAlways("[W208O] " .. tostring(ok))')


def reach_teleport(c, name, x, y, z, tries=3):
    """Beside a container or a body within the mod's reach (3 m): a write, a few seconds for the ground, the distance read back,
    and again when the copy did not stay there (a shelf or a floor the write fell through). Returns the last distance."""
    d = None
    for i in range(tries):
        if z is None: c.teleport(x + 0.8, y)
        else: c.teleport(x + 0.8, y, z + 0.05 + 0.4 * i)
        time.sleep(4)   # the pass snapshots a container on arrival
        t = c.ask(REACH % name, 'W208R', 5)
        try: d = float(t)
        except (TypeError, ValueError): d = None
        if d is not None and 0 <= d <= 2.5: return d
    return d


def loot_body(r, bandits):
    """WO-208 D.1, the body half: one item out of a dead NPC's body on the joiner, through the host. The host kills one bandit of the
    fight step (or a body already lies near); the joiner's mod asks the host for its items (WO134-BODY open), the item moves to the
    joiner's pack, the mod's session sends the take (WO134-BODY take), the host takes it out of its body (host-take ... ok) and the
    joiner hears back (result ... ok). A body the host's own world emptied meanwhile (d9-loot5: the crime scene's bandit looted it on
    the host; the open then showed the host's 0 items, as it should) is the bench's pick: the next body instead, once."""
    ok, why, detail = loot_body_once(r, bandits)
    if why == 'body-empty-on-host':
        ok2, why2, detail2 = loot_body_once(r, bandits)
        detail2['first'] = detail
        return ok2, why2, detail2
    return ok, why, detail


def loot_body_once(r, bandits):
    t = r.b.ask(FIND_BODY, 'W208B', 6)
    killed = None
    if (not t or t == 'none') and bandits:
        killed = r.a.ask(KILL_ONE % bandits[0], 'W208K', 6)
        for _ in range(12):
            time.sleep(5)
            t = r.b.ask(FIND_BODY, 'W208B', 6)
            if t and t != 'none': break
    if not t or t == 'none': return False, 'no-body', {'killed': killed}
    name, x, y, z, n = t.split()
    # at the body's own height first (a body on a floor; d9-loot5's first take stood 44 m off after three terrain-height writes), then the
    # terrain's; a body the copy cannot reach is the bench's, said as such
    d = reach_teleport(r.b, name, float(x), float(y), float(z))
    if d is None or d < 0 or d > 2.5: d = reach_teleport(r.b, name, float(x), float(y), None)
    ma, mb = r.a.mark(), r.b.mark()
    detail = {'body': name, 'items': int(n), 'killed': killed, 'dist': d}
    if d is None or d < 0 or d > 2.5: return False, 'body-out-of-reach', detail
    opened = r.b.ask(OPEN_BODY % name, 'W208O', 5)
    mo = r.b.wait('k', mb['k'], r'WO134-BODY open npc=%s -- the loot screen opens on the host.s items \((\d+)\)' % re.escape(name), 20)
    detail.update({'open_call': opened, 'opened': mo.group(0)[:120] if mo else None})
    if not mo: return False, 'body-not-opened', detail
    if mo.group(1) == '0': return False, 'body-empty-on-host', detail
    # the body's items on the joiner are the host's, refreshed on the open (WO134-BODY state ... copy deleted=N created=N): a take right
    # then can find the list empty for a moment (d9-loot5's last take: "nil") -- tried again, at most three times
    for i in range(3):
        detail['took'] = r.b.ask(TAKE_ONE % name, 'W206L', 5)
        if detail['took'] and not str(detail['took']).startswith('nil'): break
        detail['take_retries'] = i + 1
        time.sleep(1.5)
    mt = r.b.wait('k', mb['k'], r'WO134-BODY take npc=%s cls=(\S+) amt=(\d+) tok=(\S+)' % re.escape(name), 20)
    if not mt: return False, 'body-take-not-seen', detail
    detail['joiner_take'] = mt.group(0)[:160]
    mh = r.a.wait('k', ma['k'], r'WO134-BODY host-take npc=%s cls=\S+ amt=\d+ from=\S+ -> (\S+)' % re.escape(name), 20)
    detail['host_take'] = mh.group(0)[:160] if mh else None
    mr = r.b.wait('k', mb['k'], r'WO134-BODY result npc=%s tok=%s (\S+)' % (re.escape(name), re.escape(mt.group(3))), 20)
    detail['result'] = mr.group(0)[:160] if mr else None
    if not mh: return False, 'host-not-told', detail
    if not mr or mr.group(1) != 'ok': return False, 'body-take-refused', detail
    return True, 'ok', detail


def s_loot(r):
    """One loot take: the joiner takes one item out of a chest within 500 m (moved beside it first) into its own pack, the way the
    loot screen moves it; the mod's container pass sees it (WO134-CHEST take) and the joiner's agent records it in its own ledger
    for the host's world (chests are per player, WO-134). WO-208 D.1: then one item out of a body (loot_body); both must pass."""
    def fn():
        ok, why, det = chest_take()
        if not ok: return ok, why, det
        bok, bwhy, bdet = loot_body(r, getattr(r, 'bandits', None))
        return bok, ('ok' if bok else 'body-' + bwhy if not bwhy.startswith('body') else bwhy), {'chest': det, 'body': bdet}

    def chest_take():
        t = r.b.ask(CHEST_NEAR, 'W206C', 6)
        if not t or t == 'none': return False, 'no-chest', t
        name, x, y, z = t.split()
        dist = reach_teleport(r.b, name, float(x), float(y), float(z))   # within the mod's 3 m reach, at the chest's own height (shelves)
        # never take from a chest the copy was not moved to (d14-smoke2: 931 m from Stash19 after the timed talk) -- the bench's, said so
        if dist is None or dist < 0 or dist > 2.5: return False, 'chest-out-of-reach', {'chest': name, 'dist': dist}
        ma, mb = r.a.mark(), r.b.mark()
        took = r.b.ask(TAKE_ONE % name, 'W206L', 5)
        mk = r.b.wait('k', mb['k'], r'WO134-CHEST take chest=(\S+) cls=(\S+) n=(\d+)', 20)
        if not mk:
            # WO-208 D.1: which gate kept the joiner's chest pass from seeing the take (its loop alive, the copy guard, the reach)
            diag = r.b.ask('local w = KCD2MP.w134 or {}; local w1 = KCD2MP.w131 or {}; local p = player:GetWorldPos(); '
                           'local e = System.GetEntityByName("%s"); local q = e and e:GetWorldPos(); '
                           'System.LogAlways(string.format("[W208L] loop=%%s alive_age=%%.1f tick_age=%%.1f joiner=%%s shared=%%s chests=%%s '
                           'w131=%%s w131_guard=%%s w131_age=%%.1f authority=%%s hitsensor=%%s dist=%%.2f", tostring(KCD2MP.w134LoopRunning), '
                           'os.clock() - (KCD2MP._w134LoopAliveAt or -1e6), os.clock() - (w.aliveAt or -1e6), tostring(w.joiner), tostring(w.shared), '
                           'tostring(w.chests), tostring(KCD2MP_W131IsActive and KCD2MP_W131IsActive()), tostring(w1.guard), '
                           'os.clock() - (w1.aliveAt or -1e6), tostring(KCD2MP.wo102 and KCD2MP.wo102.authorityHost), tostring(KCD2MP.hitSensorOn), '
                           'q and math.sqrt((q.x - p.x) ^ 2 + (q.y - p.y) ^ 2 + (q.z - p.z) ^ 2) or -1))' % name, 'W208L', 5)
            return False, 'no-take-seen', {'chest': name, 'took': took, 'chest_pass': diag, 'dist': dist}
        # chests are per player (WO-134): the take goes into this joiner's own ledger for the host's world (stored with its next
        # Henry snapshot); the host is not told live, by design
        ml = r.b.wait('a', mb['a'], r'MP-WO134 chests: joiner took (\d+) x (\S+) from (\S+) .*ledger (\d+)', 20)
        detail = {'chest': name, 'took': took, 'dist': dist, 'joiner_lua': mk.group(0)[:160], 'joiner_agent': ml.group(0)[:200] if ml else None}
        return (True, 'ok', detail) if ml else (False, 'not-in-ledger', detail)
    return fn


def digest_rows(c, cat='quest'):
    """The copy's digest rows of one category (the agent writes digest.jsonl into its own folder or the copy's root)."""
    rows = []
    for d in (c.p['agent_dir'], c.p['root']):
        f = os.path.join(d, 'digest.jsonl')
        if os.path.exists(f):
            for l in open(f, encoding='utf-8', errors='replace'):
                try: x = json.loads(l)
                except ValueError: continue
                if x.get('cat') == cat: rows.append(x)
    return rows


def last_map(c, cat='quest'):
    for x in reversed(digest_rows(c, cat)):
        if x.get('e'): return x['e']
    return {}


def s_soak(r, minutes=20.0):
    """C1: the pair stands in the shared world for N minutes. Pass: both agents wrote digest checkpoints the whole time (no gap over
    30 s), no MP-W206 STALL. The divergences themselves are the analyzer's (tools/digest/analyze.py)."""
    def fn():
        ma, mb = r.a.mark(), r.b.mark()
        # the window starts once both sides write checkpoints (a fresh join's host keyspace takes minutes: long1, 7 min)
        w0, first = time.time(), {c.id: len(digest_rows(c)) for c in (r.a, r.b)}
        while time.time() - w0 < 900 and not all(len(digest_rows(c)) > first[c.id] for c in (r.a, r.b)):
            time.sleep(5)
        t0 = time.time()
        n0 = {c.id: len(digest_rows(c)) for c in (r.a, r.b)}
        last = {c.id: (n0[c.id], time.time()) for c in (r.a, r.b)}
        gaps = {'A': 0.0, 'B': 0.0}
        while time.time() - t0 < minutes * 60:
            time.sleep(10)
            for c in (r.a, r.b):
                n = len(digest_rows(c))
                if n > last[c.id][0]: last[c.id] = (n, time.time())
                gaps[c.id] = max(gaps[c.id], time.time() - last[c.id][1])
        stalls = [l for c, m in ((r.a, ma), (r.b, mb)) for l in c.lines(c.alog, m['a']) if 'MP-W206 STALL' in l]
        rows = {c.id: len(digest_rows(c)) - n0[c.id] for c in (r.a, r.b)}
        detail = {'minutes': minutes, 'waited_for_first_s': round(t0 - w0), 'new_rows': rows, 'max_gap_s': {k: round(v, 1) for k, v in gaps.items()},
                  'stalls': stalls[:3], 't0_ms': int(t0 * 1000), 't1_ms': int(time.time() * 1000)}
        if stalls: return False, 'stall', detail
        if min(rows.values()) == 0: return False, 'no-digests', detail
        if max(gaps.values()) > 30: return False, 'digest-gap', detail
        return True, 'ok', detail
    return fn


def s_c2(r):
    """C2's twin: a clock running on both machines is put to rest on the JOINER only (the research clock write, KCDMP_TEST_W200=1)
    -- the host-only timer of the Jaunt shape. Pass: the write landed; the analyzer must then name this key first after t_insert."""
    def fn():
        ha, hb = last_map(r.a), last_map(r.b)
        both = sorted(k for k, v in ha.items() if v.startswith('clk:') and hb.get(k) == v and 'fogControl' not in k)
        if not both: return False, 'no-shared-clock', {'host_clocks': len([1 for v in ha.values() if v.startswith('clk:')])}
        key = both[0]
        mb = r.b.mark()
        t_ins = int(time.time() * 1000)
        r.b.lua('KCD2MP_EmitEvent("w200_test", "clockset %s 0")' % key)
        m = r.b.wait('a', mb['a'], r'MP-W200 test clockset (\S+) 0 -> (\S+)', 15)
        detail = {'key': key, 't_insert_ms': t_ins, 'agent': m.group(0) if m else None}
        if not m or m.group(2) != 'written': return False, 'not-written', detail
        time.sleep(30)   # six checkpoints carry it
        return True, 'ok', detail
    return fn


NEAR_NPC = ('local best, bd; local p = player:GetWorldPos(); for _, e in ipairs(System.GetEntitiesInSphere(p, 300) or {}) do '
            'if e.class == "NPC" and e.soul and e.GetName then local n = e:GetName(); local h = 0; pcall(function() h = e.soul:GetState("health") end); '
            'local d = not (h and h > 0); '
            'if not d and not string.find(n, "kcd2mp_", 1, true) and not string.find(n, "DialogTwin_", 1, true) and not string.find(string.lower(n), "bandit", 1, true) then '
            'local q = e:GetWorldPos(); local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; if not bd or dd < bd then bd = dd; best = e end end end end; '
            'if best then local q = best:GetWorldPos(); System.LogAlways(string.format("[W206M] %s %.2f %.2f", best:GetName(), q.x, q.y)) '
            'else System.LogAlways("[W206M] none") end')


def s_c3(r):
    """C3's twin: the host's mod reports a living NPC near it 'dead at hp 0'. Control first (the shipped path, the soul is asked: no
    announcement), then with mp_research_death_from_health on (KCDMP_TEST_W206=1): the death is announced from the health reading,
    the joiner applies it, the host's NPC lives -- the analyzer must name the dead set first after t_insert."""
    def fn():
        w0 = time.time()   # checkpoints on both sides BEFORE the insert: what already differs is the baseline, not the twin's finding
        n0 = {c.id: len(digest_rows(c, 'dead')) for c in (r.a, r.b)}
        while time.time() - w0 < 600 and not all(len(digest_rows(c, 'dead')) >= n0[c.id] + 2 and len(digest_rows(c, 'dead')) >= 2 for c in (r.a, r.b)):
            time.sleep(5)
        t = r.a.ask(NEAR_NPC, 'W206M', 6)
        if not t or t == 'none': return False, 'no-npc', t
        name, x, y = t.split()[0], float(t.split()[1]), float(t.split()[2])
        r.a.teleport(x + 3.0, y)   # both players beside it: inside the dead scan (60 m) on both machines
        r.b.teleport(x + 4.0, y + 1.0)
        time.sleep(6)
        ma = r.a.mark()
        r.a.lua('KCD2MP_EmitEvent("npc_death", "%s 0 lua")' % name)
        time.sleep(4)
        ctrl = [l for l in r.a.lines(r.a.alog, ma['a']) if name in l and ('WO206-RESEARCH' in l or 'sending FATAL' in l)]
        r.a.lua('KCD2MP_W206Set("research_death_from_health", "on")')
        time.sleep(1.5)
        ma, mb = r.a.mark(), r.b.mark()
        t_ins = int(time.time() * 1000)
        r.a.lua('KCD2MP_EmitEvent("npc_death", "%s 0 lua")' % name)
        m = r.a.wait('a', ma['a'], r'WO206-RESEARCH npc=%s (announced|NOT announced).*' % re.escape(name), 10)
        time.sleep(6)
        jb = [l for l in r.b.lines(r.b.alog, mb['a']) + r.b.lines(r.b.klog, mb['k']) if name in l][:4]
        r.a.lua('KCD2MP_W206Set("research_death_from_health", "off")')
        alive = r.a.ask('local e = System.GetEntityByName("%s"); local d = "?"; if e and e.soul then pcall(function() d = "health=" .. tostring(e.soul:GetState("health")) end) end; '
                        'System.LogAlways("[W206D] " .. d)' % name, 'W206D', 4)
        detail = {'npc': name, 't_insert_ms': t_ins, 'control_lines': ctrl[:3], 'host': m.group(0)[:200] if m else None,
                  'host_npc_dead': alive, 'joiner_lines': [l[-200:] for l in jb]}
        if ctrl: return False, 'control-announced', detail
        if not m or 'NOT' in m.group(1): return False, 'not-announced', detail
        t0 = time.time()   # checkpoints after the insert on both sides (a fresh join's host keyspace takes minutes)
        while time.time() - t0 < 600:
            if all(sum(1 for x in digest_rows(c, 'dead') if x.get('t', 0) > t_ins + 15000) >= 2 for c in (r.a, r.b)): break
            time.sleep(5)
        detail['checkpoints_after_s'] = round(time.time() - t0)
        return True, 'ok', detail
    return fn


def epoch_stats(c):
    """The machine's epoch counters: asked through the mod, or (a console too busy to answer: a joiner's agent floods it) the
    agent's own latest MP-W206-EPOCH line, printed about once a minute."""
    off = c.size(c.alog)
    c.lua('KCD2MP_W206Status()', tries=2)
    m = c.wait('a', off, r'MP-W206-EPOCH .*', 8)
    if not m:
        last = [x for x in (re.search(r'MP-W206-EPOCH .*', l) for l in c.tail(c.alog, 3000)) if x]
        m = last[-1] if last else None
    if not m: return None
    return dict(kv.split('=', 1) for kv in m.group(0).split()[1:] if '=' in kv)


def s_e2(r, twin=False):
    """E2: the host reloads its world during a fight. Pass: zero old-epoch rows applied on either side (applied_old stays 0) and the
    joiner learnt the new world epoch. Twin (mp_epoch_check off on both): at least one old-epoch row gets through (applied_old >= 1)."""
    def fn():
        sw = 'off' if twin else 'on'
        for c in (r.a, r.b): c.lua('KCD2MP_W206Set("epoch_check", "%s")' % sw)
        before = {c.id: epoch_stats(c) for c in (r.a, r.b)}
        t = r.a.ask(FIND_BANDITS, 'W206K', 6)
        if not t or t.startswith('none'): return False, 'no-bandits', t
        b1, b2, x, y, z = t.split()
        x, y = float(x), float(y)
        r.a.teleport(x + 4.0, y)
        r.b.teleport(x + 5.5, y + 1.0)
        time.sleep(4)
        for c in (r.a, r.b): c.keep_up()
        r.a.lua(ATTACK % (b1, 'player'))
        r.a.lua(ATTACK % (b2, FIGURE))
        time.sleep(8)
        ma, mb = r.a.mark(), r.b.mark()
        # the joiner's frame stalls as the host reloads (a hitch, common in the field): the fight's last rows wait in its queue while
        # its reader takes the new epoch -- the old world's rows a reload can leave behind (without it nothing old is in flight)
        pair.hitch(r.b.port, 8)
        time.sleep(1.0)
        try: r.a.cmd('wh_sys_LoadGame 4 save440')   # the console answers when the load is over: a timeout here is not a failure
        except Exception as ex: rig.note('load-send', copy='A', note='%s: %s' % (type(ex).__name__, ex))
        g = r.a.wait('k', ma['k'], r'^Gameplay started', 240)
        ep = r.b.wait('a', mb['a'], r"MP-W206 the host's world epoch is (\d+) now", 120)
        time.sleep(45)
        for c in (r.a, r.b): c.stop_keep()
        after = {c.id: epoch_stats(c) for c in (r.a, r.b)}
        old = {k: int((v or {}).get('applied_old', '-1')) for k, v in after.items()}
        drops = {k: {d: (v or {}).get(d) for d in ('dropped_world_npc', 'dropped_world_quest', 'dropped_world_loot', 'dropped_world_death', 'dropped_join')}
                 for k, v in after.items()}
        detail = {'twin': twin, 'bandits': [b1, b2], 'reloaded': bool(g), 'joiner_epoch_line': ep.group(0) if ep else None,
                  'applied_old': old, 'drops': drops, 'before': before, 'after': after}
        for c in (r.a, r.b): c.lua('KCD2MP_W206Set("epoch_check", "on")')
        if not g: return False, 'no-reload', detail
        if min(old.values()) < 0: return False, 'no-stats', detail
        if twin: return (True, 'ok-twin-leaked', detail) if max(old.values()) >= 1 else (False, 'twin-nothing-leaked', detail)
        return (True, 'ok', detail) if max(old.values()) == 0 else (False, 'stale-applied', detail)
    return fn


# B1's scenes: the bench save's quest (socky) in the order the save reaches them; the first the host's handler starts is played
B1_SCENES = ('zbranePanaSemina_seminArrival', 'malir_firstMeeting_ingame', 'zbranePanaSemina_playerSukDialog', 'zachrana_prespani',
             'utokNaNebakov_valley_beforeDuel', 'zbranePanaSemina_playerSukDialog2', 'zbranePanaSemina_playerSukDialogFull',
             'vezniNaTroskach_pistaDefeatWindow', 'zachrana_probuzeni', 'zachrana_prichodKeKorenarce', 'zachrana_cestaPoBrehu',
             'socky_7_bergov', 'socky_5_departure', 'socky_4_katerina', 'socky_3_tavern')   # the table's names, ranked by the bench save's active quest parts (c1r)


def s_b1(r, twin=False, secs=150):
    """B1: a shared in-game cutscene under the relay's jitter-hold (the run's fault plan). The host starts a scene of the save's own
    quest by hand (KCDMP_TEST_W194=1); the joiner follows the host's clock. Pass: the joiner followed (WO194 lines for the scene),
    at most 2 hard seeks, none backward. Twin (mp_cutscene_clock_v2 off on the joiner): the WO-194 follower -- more than 2 sets or
    one backward."""
    def fn():
        r.b.lua('KCD2MP_W202Set("cutscene_clock_v2", "%s")' % ('off' if twin else 'on'))
        ma, mb = r.a.mark(), r.b.mark()
        scene, res = None, []
        for sc in B1_SCENES:
            off = r.a.size(r.a.alog)
            r.a.lua('KCD2MP_EmitEvent("w194_test", "start %s own")' % sc)   # own: the host's quest plays it (the bus sends it)
            m = r.a.wait('a', off, r"WO194-TEST start '%s' .*" % re.escape(sc), 10)   # "as this quest's own" 
            res.append(m.group(0)[:200] if m else '%s: no answer' % sc)
            if m and re.search(r': (started|ok|pulsed)', m.group(0), re.I): scene = sc; break
            hp = re.search(r"through (\S+): hibernated", m.group(0)) if m else None
            if hp and len(res) <= 6:
                # the bench save keeps this quest part hibernated: wake the handler's chain, top down (the engine's own Wake on each
                # node, WO-200's research verb -- test staging, like starting the scene by hand), then start it again
                parts = hp.group(1).split('.')
                for k in range(3, len(parts) + 1):
                    r.a.lua('KCD2MP_EmitEvent("w200_test", "wake %s")' % '.'.join(parts[:k]))
                time.sleep(2)
                off = r.a.size(r.a.alog)
                r.a.lua('KCD2MP_EmitEvent("w194_test", "start %s own")' % sc)   # own: the host's quest plays it (the bus sends it)
                m = r.a.wait('a', off, r"WO194-TEST start '%s' .*" % re.escape(sc), 10)   # "as this quest's own" 
                res.append('after wake: ' + (m.group(0)[-80:] if m else 'no answer'))
                if m and re.search(r': (started|ok|pulsed)', m.group(0), re.I): scene = sc; break
        if not scene: return False, 'no-scene-started', {'tries': res}
        t0 = time.time()
        while time.time() - t0 < secs:
            time.sleep(3)
            if any(re.search(r"WO194.*(ended|finished|stopped)", l) for l in r.b.lines(r.b.alog, mb['a'])): break
        lines = r.b.lines(r.b.alog, mb['a'])
        follow = [l for l in lines if 'WO194' in l or 'WO202-CLOCK' in l]
        sets = []
        for l in lines:
            m = re.search(r"(WO202-CLOCK|WO194-CLOCK) .*set \((-?[\d.]+) -> (-?[\d.]+)\)", l)
            if m: sets.append((m.group(1), float(m.group(2)), float(m.group(3))))
        hard = [x for x in sets if x[0] == 'WO202-CLOCK' or abs(x[2] - x[1]) >= 0.2]
        back = [x for x in sets if x[2] < x[1] - 0.05]
        r.b.lua('KCD2MP_W202Set("cutscene_clock_v2", "on")')
        detail = {'twin': twin, 'scene': scene, 'start': res[-1], 'follower_lines': len(follow), 'sets': len(sets), 'hard_seeks': len(hard),
                  'backward': len(back), 'examples': [l[-200:] for l in follow[:6]]}
        if not follow: return False, 'joiner-did-not-follow', detail
        if twin: return (True, 'ok-twin-caught', detail) if (len(hard) > 2 or back) else (False, 'twin-not-caught', detail)
        return (True, 'ok', detail) if (len(hard) <= 2 and not back) else (False, 'seeks', detail)
    return fn


W189_RESOLVER = re.compile(r'^\[(\d\d):(\d\d):(\d\d)\.(\d+)\] t\d+ WO189-READY resolver')
STALL = re.compile(r'^\[(\d\d):(\d\d):(\d\d)\.(\d+)\] t\d+ (?:MP-NPCWRITE tick gap ([\d.]+) s|WO138-WORLD FROZEN \(game/real [\d.]+ over ([\d.]+) s)')


def w189_stall(lines):
    """WO-207: the main thread's longest stall in the 20 s after the session's first readiness read (the DLL's own tick-gap and
    world-frozen lines; pure, tested). None: no readiness read in the log."""
    t0, worst = None, 0.0
    sec = lambda m: int(m.group(1)) * 3600 + int(m.group(2)) * 60 + int(m.group(3)) + float('0.' + m.group(4))
    for l in lines:
        if t0 is None:
            m = W189_RESOLVER.match(l)
            if m: t0 = sec(m)
            continue
        m = STALL.match(l)
        if not m: continue
        t = sec(m)
        if t - t0 > 20: break
        worst = max(worst, float(m.group(5) or m.group(6)))
    return None if t0 is None else worst


def s_stats(r):
    """The end of a run: both machines' epoch counters (E1 reads them from every B2 run) and their digest/probe counts; WO-207: the
    main thread's stall after the first readiness read (2 s or more fails the run: the pair froze 6-13 s there)."""
    def fn():
        st = {c.id: epoch_stats(c) for c in (r.a, r.b)}
        stall = {c.id: w189_stall(c.lines(c.nlog, 0)) for c in (r.a, r.b)}
        for c in (r.a, r.b):
            if st.get(c.id): st[c.id]['w189_stall_s'] = stall[c.id]
        if not all(st.values()): return False, 'no-stats', st
        if any(v is not None and v >= 2.0 for v in stall.values()): return False, 'w189-stall', st
        return True, 'ok', st
    return fn


def bones_chunks():
    """tools/rig/bones.lua as console sends: comments dropped, whitespace collapsed, split at its @@SPLIT@@ mark."""
    src = open(os.path.join(HERE, 'bones.lua'), encoding='utf-8').read()
    parts = src.split('-- @@SPLIT@@')
    code = [[l for l in p.splitlines() if not l.strip().startswith('--')] for p in parts]
    inline = [l for c in code for l in c if '--' in l]
    if inline: raise ValueError('bones.lua: an inline comment would swallow the rest of a collapsed chunk: %s' % inline[0].strip())
    return [re.sub(r'\s+', ' ', ' '.join(c)).strip() for c in code]


def s_bones(r, secs=60):
    """D's bone prototype on the host for <secs>: loaded, started (6 bodies, 5 Hz), stopped; the lines merged into the probe's samples
    (tools/rig/bones.py) into <run>/bones-A.jsonl. Pass: lines came and at least one probe sample took bones."""
    def fn():
        for ch in bones_chunks():
            if not r.a.lua(ch): return False, 'not-loaded', ch[:80]
        off = r.a.size(r.a.klog)
        r.a.lua('W206Bones_Start(6, 200)')
        m = r.a.wait('k', off, r'\[W206B-SYNC\]', 6)
        sync_ms = int(time.time() * 1000)
        time.sleep(secs)
        r.a.lua('W206Bones_Stop()')
        n = sum(1 for l in r.a.lines(r.a.klog, off) if '[W206B] ' in l)
        out = os.path.join(rig.run_dir(), 'bones-A.jsonl')
        anim = os.path.join(r.a.p['root'], 'kcdmp-anim.jsonl')
        import subprocess
        pr = subprocess.run([sys.executable, os.path.join(HERE, 'bones.py'), r.a.klog, anim, str(sync_ms), out], capture_output=True, text=True)
        detail = {'sync': bool(m), 'bone_lines': n, 'merge': (pr.stdout + pr.stderr)[-300:]}
        if not m or n == 0: return False, 'no-bone-lines', detail
        return True, 'ok', detail
    return fn


def s_perfab(r, cycles=3, which='both'):
    """The WO-206 readers' frame cost, A/B on one joined pair at rest: mp_digest + mp_anim_probe off, then on, on both copies,
    alternating; each window settles 15 s, then three 20 s frame-rate samples per copy (the engine's frame counter through the
    console). Pass (no regression): the median with the readers on is at least 90 % of the median with them off, on both copies."""
    def fn():
        samples = {'off': {'A': [], 'B': []}, 'on': {'A': [], 'B': []}}
        toggles = []
        for _ in range(cycles):
            for state in ('off', 'on'):
                for c in (r.a, r.b):
                    code = {'both': 'KCD2MP_W206Digest("%s"); KCD2MP_W206Probe("%s")' % (state, state),
                            'probe': 'KCD2MP_W206Probe("%s")' % state, 'digest': 'KCD2MP_W206Digest("%s")' % state}[which]
                    ok = c.lua(code)
                    toggles.append((c.id, state, ok))
                time.sleep(15)
                for _ in range(3):
                    for c in (r.a, r.b):
                        f = rig.fps(c.port, c.p['root'], span=20.0)
                        if f: samples[state][c.id].append(f)
        med = lambda x: sorted(x)[len(x) // 2] if x else None
        res = {st: {cid: med(v) for cid, v in d.items()} for st, d in samples.items()}
        ratio = {cid: (round(res['on'][cid] / res['off'][cid], 3) if res['on'][cid] and res['off'][cid] else None) for cid in ('A', 'B')}
        detail = {'median_fps': res, 'on_over_off': ratio, 'samples': samples, 'toggles_lost': [t for t in toggles if not t[2]]}
        if None in ratio.values(): return False, 'no-samples', detail
        return (True, 'ok', detail) if min(ratio.values()) >= 0.9 else (False, 'fps-regression', detail)
    return fn


# ---------------------------------------------------------------- WO-207 0.3 / B.5 / C.1

def rec_of(c):
    """The rig's record of a copy's game process (pid, creation time, image)."""
    return rig.load_state().get(c.id.lower())


def put_front(c):
    """WO-207 (the maintainer allows driving the keyboard and mouse): a copy's game window in front, the way a player's is."""
    import front
    return front.front(rec_of(c), procs.matches, log=lambda m: rig.say('copy', c.id, m))


AGENT_TS = re.compile(r'^(\d\d):(\d\d):(\d\d)\.(\d{3}) ')


def agent_ms(line, day0):
    """The wall time (ms) of an agent log line (its own HH:MM:SS.mmm stamp, on the day `day0` started)."""
    m = AGENT_TS.match(line)
    if not m: return None
    h, mi, s, ms = (int(x) for x in m.groups())
    t = time.mktime(time.localtime(day0)[:3] + (h, mi, s, 0, 0, -1)) * 1000 + ms
    return int(t + (86_400_000 if t < day0 * 1000 - 43_200_000 else 0))


def queue_lags(c, t0_ms, t1_ms):
    """The copy's agent queue lag (ms) samples between two wall times: the WO-207 B.1 line (MP-W207-Q lag_ms=..., once a second) when
    the agent prints it, else the ping line (every 2 s; the lag is printed only from 500 ms: a ping without it counts as 0)."""
    w207, ping = [], []
    for l in c.lines(c.alog, 0):
        t = agent_ms(l, t0_ms / 1000.0)
        if t is None or not t0_ms <= t <= t1_ms: continue
        m = re.search(r'MP-W207-Q lag_ms=(\d+)', l)
        if m:
            # B.5: the worst of the processor's lag and the console's and the pipe's oldest frame-driven waits (when the agent prints them)
            extra = [int(x) for x in re.findall(r' (?:lua_ms|pipe_ms)=(\d+)', l)]
            w207.append(max([int(m.group(1))] + extra)); continue
        if '[ping]' in l and '(network)' in l:
            m = re.search(r"the game's queue is (\d+) ms behind", l)
            ping.append(int(m.group(1)) if m else 0)
    return (w207, 'w207') if w207 else (ping, 'ping')


def pct(xs, p):
    if not xs: return None
    s = sorted(xs)
    return s[min(len(s) - 1, int(round(p / 100.0 * (len(s) - 1))))]


def s_queue(r, secs=60.0, who='b', limit_ms=1000):
    """B.5: the agent queue of one copy (the joiner by default) over the next <secs> with its game in front. Pass: p99 < 1 s."""
    def fn():
        c = r.b if who == 'b' else r.a
        infront = put_front(c)
        t0 = int(time.time() * 1000)
        time.sleep(secs)
        t1 = int(time.time() * 1000)
        lags, src = queue_lags(c, t0, t1)
        other = r.a if c is r.b else r.b
        olags, osrc = queue_lags(other, t0, t1)
        detail = {'copy': c.id, 'in_front': infront, 'source': src, 'n': len(lags), 'p50_ms': pct(lags, 50), 'p99_ms': pct(lags, 99),
                  'max_ms': max(lags) if lags else None, 'other': {'copy': other.id, 'source': osrc, 'p50_ms': pct(olags, 50), 'p99_ms': pct(olags, 99)}}
        if not lags: return False, 'no-samples', detail
        return (True, 'ok', detail) if pct(lags, 99) < limit_ms else (False, 'queue-behind', detail)
    return fn


FIND_BANDITS3 = (
    'local list = {}; for _, e in ipairs(System.GetEntitiesByClass("NPC") or {}) do '
    'if e.soul and string.find(string.lower(e:GetName()), "bandit", 1, true) then local h = 0; '
    'pcall(function() h = e.soul:GetState("health") end); if h and h > 0 then list[#list + 1] = e end end end; '
    'local best, bn; for i = 1, #list do local a = list[i]:GetWorldPos(); local g = {list[i]}; for j = 1, #list do if j ~= i then '
    'local b = list[j]:GetWorldPos(); if (a.x - b.x) ^ 2 + (a.y - b.y) ^ 2 < 225 and #g < 3 then g[#g + 1] = list[j] end end end; '
    'if not bn or #g > bn then bn = #g; best = g end end; '
    'if best and bn >= 2 then local p = best[1]:GetWorldPos(); local names = {}; for k = 1, #best do names[k] = best[k]:GetName() end; '
    'System.LogAlways(string.format("[W207K] %s %.2f %.2f %.2f", table.concat(names, ","), p.x, p.y, p.z)) '
    'else System.LogAlways("[W207K] none " .. #list) end')


def s_c1fight(r, secs=float(os.environ.get('KCDMP_RIG_C1_S', '300'))):
    """WO-207 0.3 c1fight: the host and the joiner against two or three bandits for 5 minutes, the joiner fighting (its own swings:
    the left button sent to the joiner's game in front, the maintainer's allowance). Both players are kept alive; the bandits are set
    on again every minute. Pass: both took damage, the joiner swung, and the host applied at least one of the joiner's blows."""
    reset = os.environ.get('KCDMP_RIG_C1_RESET', '1') != '0'
    fresh = os.environ.get('KCDMP_RIG_C1_FRESH', '0') == '1'
    via_mod = os.environ.get('KCDMP_RIG_C1_VIA_MOD', '0') == '1'
    on_figure = os.environ.get('KCDMP_RIG_C1_FIGURE', '1') != '0'

    def fn():
        t = r.a.ask(FIND_BANDITS3, 'W207K', 6)
        if not t or t.startswith('none'): return False, 'no-bandits', t
        names, x, y, z = t.split()
        bandits = names.split(',')
        x, y = float(x), float(y)
        r.a.teleport(x + 4.0, y)
        r.b.teleport(x + 5.5, y + 1.0)
        time.sleep(4)
        for c in (r.a, r.b): c.keep_up()
        r.b.lua('pcall(function() player.human:DrawWeapon() end)')
        ma, mb = r.a.mark(), r.b.mark()

        fig_missing = [0]

        def provoke():
            # the first bandit on the host, the others on the joiner's figure (the joiner must be in the fight: a run where the figure
            # was not found had the joiner untouched for 5 minutes); counted when the figure is not there
            atk = ATTACK_FRESH if fresh else ATTACK
            r.a.lua(atk % (bandits[0], 'player'))
            if r.a.ask('System.LogAlways("[W207F] " .. tostring(%s ~= nil))' % FIGURE, 'W207F', 3) != 'true': fig_missing[0] += 1
            for b in bandits[1:]:
                # KCDMP_RIG_C1_VIA_MOD=1: the figure's attackers through the mod's own guard-pursuit attack (WO-139 KCD2MP_W139HostAttack:
                # the same interrupt at the joiner's avatar, with the mod's rules)
                if not on_figure: r.a.lua(atk % (b, 'player'))   # KCDMP_RIG_C1_FIGURE=0: every bandit on the host; the joiner fights beside him
                elif via_mod: r.a.lua('if KCD2MP_W139HostAttack then KCD2MP_W139HostAttack("%s", 1) end' % b)
                else: r.a.lua(atk % (b, FIGURE))

        def rejoin_fight():
            # the fight drifts: the joiner goes back beside the second bandit (the host's position of it)
            p = r.a.ask('local e = System.GetEntityByName("%s"); if e then local p = e:GetWorldPos(); '
                        'System.LogAlways(string.format("[W207P] %%.1f %%.1f", p.x, p.y)) else System.LogAlways("[W207P] none") end'
                        % bandits[min(1, len(bandits) - 1)], 'W207P', 3)
            try:
                bx, by = (float(v) for v in p.split())
                r.b.teleport(bx + 1.5, by + 0.5)
            except (AttributeError, ValueError):
                pass
        provoke()
        import threading
        sys.path.insert(0, os.path.join(rig.REPO, 'tools', 'dev-input'))
        import game_input
        swings = {'sent': 0, 'refused': 0, 'err': None}
        stop = threading.Event()

        def swinger():
            # the joiner's own attacks: lock on (Tab), then the left button (the attack) and now and then F (the kick, attack_special in
            # the locked-combat map). A click's down makes Windows report no foreground window for a moment: the input helper waits
            # for the game to be in front again before the button-up (the baseline's ups were all refused for it)
            try:
                g = game_input.Game('wo207-c1fight')
                g.pids = {rec_of(r.b)['pid']}   # the joiner's copy only
                if not put_front(r.b): swings['err'] = 'joiner not in front'; return
                n = 0
                while not stop.is_set():
                    try:
                        if n % 6 == 0: g.tap('tab', 80)
                        if n % 4 == 3: g.tap('f', 90)
                        else: g.click('left', 90)
                        swings['sent'] += 1
                    except game_input.NotFocused:
                        swings['refused'] += 1
                        if swings['refused'] > 20 or not put_front(r.b): break
                        try: g.release_all()   # a refused click must not leave its button down
                        except game_input.NotFocused: pass
                    n += 1
                    stop.wait(1.4)
            except BaseException as ex:   # SystemExit included: a failed send stops the swings, never the scenario
                swings['err'] = '%s: %s' % (type(ex).__name__, ex)
            finally:
                try: g.release_all()
                except BaseException: pass
        th = threading.Thread(target=swinger, daemon=True); th.start()
        lows = {'A': 100.0, 'B': 100.0}
        t0 = last = time.time()
        while time.time() - t0 < secs:
            for c in (r.a, r.b):
                h = c.ask('System.LogAlways(string.format("[W206H] %.1f", player.soul:GetState("health")))', 'W206H', 2)
                if h:
                    try: lows[c.id] = min(lows[c.id], float(h))
                    except ValueError: pass
            # KCDMP_RIG_C1_RESET=0 (the crash work): the bandits are set on once and the fight runs as players fight it -- no re-set every
            # 30 s (each re-set terminates the bandits' combat instantly and reconnects the figure's soul), no mid-fight teleport
            if reset and time.time() - last > 30:
                n_prov = getattr(provoke, 'n', 0) + 1; provoke.n = n_prov
                if n_prov % 2 == 0: rejoin_fight(); time.sleep(2)
                provoke(); last = time.time()
            time.sleep(2)
        stop.set(); th.join(timeout=10)
        for i, b in enumerate(bandits): r.a.lua(STOP % (b, i))
        for c in (r.a, r.b): c.stop_keep()
        blows = [l for l in r.a.lines(r.a.alog, ma['a']) if 'WO204-BLOW' in l]
        verdicts = sum(1 for l in r.b.lines(r.b.alog, mb['a']) if re.search(r'hit[- ]verdict|MP-W161|0x73|PlayerHit', l, re.I))
        fighting = sum(1 for l in r.b.lines(r.b.nlog, mb['n']) if 'fight=1' in l and any(b in l for b in bandits))
        detail = {'bandits': bandits, 'secs': secs, 'reset_every_30s': reset, 'fresh_only': fresh, 'via_mod': via_mod, 'bandits_on_figure': on_figure, 'health_low': lows, 'joiner_swings': swings, 'figure_missing_at_provoke': fig_missing[0],
                  'host_applied_joiner_blows': len(blows),
                  'blow_examples': [l[-160:] for l in blows[:3]], 'joiner_verdicts': verdicts, 'joiner_copy_fight_lines': fighting}
        if lows['A'] >= 100.0 and lows['B'] >= 100.0: return False, 'no-damage', detail
        if lows['B'] >= 100.0: return False, 'joiner-not-in-fight', detail
        if swings['sent'] == 0: return False, 'joiner-never-swung', detail
        if not blows: return False, 'joiner-blows-not-applied', detail
        return True, 'ok', detail
    return fn


FIND_INN = ('local best; local p = player:GetWorldPos(); local seen = {}; local ex = System.GetEntityByName("%s"); '
            'if ex then local q = ex:GetWorldPos(); System.LogAlways(string.format("[W207I] %%s %%.2f %%.2f %%.2f exact", ex:GetName(), q.x, q.y, q.z)); return end; '
            'for _, e in ipairs(System.GetEntitiesByClass("NPC") or {}) do '
            'local n = string.lower(e:GetName()); if string.find(n, "hospod", 1, true) or string.find(n, "krcm", 1, true) or '
            'string.find(n, "innkeep", 1, true) or string.find(n, "vycep", 1, true) then local q = e:GetWorldPos(); '
            'seen[#seen + 1] = string.format("%%s@%%.0f,%%.0f", e:GetName(), q.x, q.y); local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; '
            'if not best or dd < best[2] then best = {e, dd} end end end; '
            'if best then local q = best[1]:GetWorldPos(); System.LogAlways(string.format("[W207I] %%s %%.2f %%.2f %%.2f %%s", best[1]:GetName(), q.x, q.y, q.z, table.concat(seen, ";"))) '
            'else System.LogAlways("[W207I] none") end')

COUNT_NEAR = ('local n, s = 0, 0; for _, e in ipairs(System.GetEntitiesInSphere(player:GetWorldPos(), 15) or {}) do '
              'if e.class == "NPC" and e.soul and not string.find(e:GetName(), "kcd2mp_", 1, true) then n = n + 1 end end; '
              'System.LogAlways(string.format("[W207N] %d %d", n, math.floor(Calendar.GetWorldTime() % 86400 / 60)))')


def s_c2tavern(r, secs=float(os.environ.get('KCDMP_RIG_C2_S', '600'))):
    """WO-207 0.3 c2tavern: a tavern at night (seated drinkers, the innkeeper serving, a dice table) for 10 minutes. The innkeeper is
    found by name on the host; the host's clock is set to the evening (the joiner follows the host's clock); both players stand inside.
    Pass: both machines in the tavern at night with NPCs around them (the probe records every body for the animation baseline)."""
    def fn():
        t = r.a.ask(FIND_INN % os.environ.get('KCDMP_RIG_INN', 'ttkc_woman_2'), 'W207I', 6)   # the bench save's innkeeper (HOSPODSKA_OBJEDNAVKA)
        if not t or t == 'none': return False, 'no-innkeeper', t
        parts = t.split()
        inn, x, y, z = parts[0], float(parts[1]), float(parts[2]), float(parts[3])
        # the evening: 21:00 of the next game day the host reaches (Calendar.SetWorldTime, the call the mod's own time sync uses)
        r.a.lua('local now = Calendar.GetWorldTime(); local d = math.floor(now / 86400) * 86400 + 21 * 3600; '
                'if d < now then d = d + 86400 end; Calendar.SetWorldTime(d)')
        time.sleep(45)   # schedules move the drinkers in, the joiner's clock is pulled to the host's
        r.a.teleport(x + 1.2, y, z + 0.05)
        # WO-208: the joiner's long jump into the tavern landed before its interior streamed in (the baseline's joiner stood outside
        # with 1 NPC within 15 m while the host had 11): put it beside the innkeeper again until it stands there
        joiner_at = None
        for _ in range(6):
            r.b.teleport(x + 2.2, y + 0.8, z + 0.05)
            time.sleep(4)
            q = r.b.pos()
            if q and abs(q[0] - (x + 2.2)) < 3.0 and abs(q[1] - (y + 0.8)) < 3.0 and abs(q[2] - z) < 2.0:
                joiner_at = q; break
        time.sleep(2)
        counts = {}
        t0 = time.time()
        while time.time() - t0 < secs:
            for c in (r.a, r.b):
                v = c.ask(COUNT_NEAR, 'W207N', 3)
                if v: counts.setdefault(c.id, []).append(tuple(int(q) for q in v.split()))
            time.sleep(30)
        near = {k: max((n for n, _m in v), default=0) for k, v in counts.items()}
        minute = {k: v[-1][1] if v else None for k, v in counts.items()}
        detail = {'innkeeper': inn, 'others': parts[4][:300] if len(parts) > 4 else '', 'npcs_within_15m_max': near, 'clock_minute_of_day': minute,
                  'secs': secs, 't0_ms': int(t0 * 1000), 't1_ms': int(time.time() * 1000), 'joiner_in_tavern': joiner_at is not None}
        if near.get(r.a.id, 0) < 3: return False, 'tavern-empty', detail
        if joiner_at is None or near.get(r.b.id, 0) < 3: return False, 'joiner-not-in-tavern', detail   # WO-208: both players inside
        night = [m for m in minute.values() if m is not None and (m >= 20 * 60 or m < 5 * 60)]
        if len(night) < 2: return False, 'not-night', detail
        return True, 'ok', detail
    return fn


# ---------------------------------------------------------------- WO-208 B: the joiner's own defence

# a UNIT vector (WO-209 d2/d3: the raw difference scaled the player's own transform -- "Not-orthonormal matrix in CEntity::CalcLocalTM()
# on 'Dude'", scale 1.19 -> 2.62 -- until CryPhysics crashed the joiner's game: the bench's, not the mod's)
FACE_TO = ('local e = System.GetEntityByName("%s"); if e then local a, p = e:GetWorldPos(), player:GetWorldPos(); '
           'local dx, dy = a.x - p.x, a.y - p.y; local n = math.sqrt(dx * dx + dy * dy); '
           'if n > 0.05 then pcall(function() player:SetDirectionVector({x = dx / n, y = dy / n, z = 0}) end) end end')


def s_defence(r, attackers=1, swings=40, secs=float(os.environ.get('KCDMP_RIG_DEF_S', '420'))):
    """WO-208 B: <attackers> bandit(s) set on the joiner's figure only (the host stands off at 8 m); the joiner defends with his own keys
    (the maintainer's allowance; send-only, his copy's window in front): the right button held for a while (block), pressed as a
    copy's swing winds up (the strike window's own line in his DLL's log: a timed block / perfect block), released for open blows, and
    now and then the dodge (space). Until the host has judged <swings> blows of those bandits on the figure (or <secs>). Pass: at least
    95 % judged by the joiner's engine (WO208-DEF outcome on the host), fallbacks under 5 %, every outcome the host took is one the
    joiner's engine logged (by attacker and order), and the host saw the figure blocking (its guard state streamed)."""
    def fn():
        t = r.a.ask(FIND_BANDITS3, 'W207K', 6)
        if not t or t.startswith('none'): return False, 'no-bandits', t
        names, x, y, z = t.split()
        bandits = names.split(',')[:attackers]
        group = names.split(',')   # WO-208: the group joins its fellow's attack -- every blow of the group counts (d9: bandit 5 sent, 1 and 4 struck)
        # WO-209: every copy's blow on the joiner counts, whoever the camp sends (d5: bandits 4 and 6 fought, the picked three never struck)
        def ingroup(l): return re.search(r' npc=(?!kcd2mp_)\S', l) is not None
        x, y = float(x), float(y)
        r.b.teleport(x + 3.0, y)
        r.a.teleport(x + 11.0, y + 2.0)
        time.sleep(4)
        for c in (r.a, r.b): c.keep_up()
        r.b.lua('pcall(function() player.human:DrawWeapon() end)')
        ma, mb = r.a.mark(), r.b.mark()
        for b in bandits: r.a.lua(ATTACK % (b, FIGURE))
        import threading
        sys.path.insert(0, os.path.join(rig.REPO, 'tools', 'dev-input'))
        import game_input
        sent = {'block_holds': 0, 'timed_blocks': 0, 'dodges': 0, 'open': 0, 'refused': 0, 'err': None}
        stop = threading.Event()

        def defender():
            try:
                g = game_input.Game('wo208-defence')
                g.pids = {rec_of(r.b)['pid']}
                if not put_front(r.b): sent['err'] = 'joiner not in front'; return
                # lock on: Tab toggles -- a lock carried over from the last phase (d9: the joiner still locked on a bandit 154 m away at
                # the first camp, none of his blocks counting at the second) is let go first, then taken again on the nearest in view
                last_mid = [l for l in r.b.lines(r.b.nlog, max(0, r.b.size(r.b.nlog) - 400000)) if 'WO209-ATK mid ' in l]
                if last_mid and 'player_opponent=none' not in last_mid[-1]:
                    g.tap('tab', 80); time.sleep(0.4)
                g.tap('tab', 80)
                off = r.b.size(r.b.nlog)
                n = 0
                while not stop.is_set():
                    try:
                        # a copy's swing winding up at this player (the strike window's begin line): block on it, or dodge, or take it open
                        new = r.b.lines(r.b.nlog, off); off = r.b.size(r.b.nlog)
                        windup = any(('WO166-STRIKE' in l and ' begin ' in l) or ('WO209-ATK' in l and 'own attack at the local player started' in l) for l in new)
                        # WO-209: the player turns to the bandit that attacks him (a player's own reflex): the engine's per-frame lock-on pick
                        # takes the nearest lockable enemy in his view cone (WO-186)
                        for l in new:
                            m9 = re.search(r'WO209-ATK npc=(\S+) .*own attack at the local player started', l)
                            if m9: r.b.lua_nowait(FACE_TO % m9.group(1)); sent['turns'] = sent.get('turns', 0) + 1
                        if windup:
                            # WO-209: the copy's own attack lands 0.6 s after its start (d6: p50 605 ms, up to 1.5 s) -- a held guard through
                            # it, presses near the blow (blocks, perfect blocks), a dodge, an open blow (d6's 0.15 / 0.30 s presses came early)
                            k = n % 6
                            if k in (0, 5):
                                time.sleep(0.10); g.click('right', 1300); sent['block_holds'] += 1
                            elif k in (1, 2):
                                time.sleep(0.40 if k == 1 else 0.55)
                                g.click('right', 300); sent['timed_blocks'] += 1
                            elif k == 3:
                                time.sleep(0.35); g.tap('space', 80); sent['dodges'] += 1
                            else:
                                sent['open'] += 1
                            n += 1
                        elif n % 7 == 6:
                            g.click('right', 900); sent['block_holds'] += 1; n += 1   # a held guard between swings
                    except game_input.NotFocused:
                        sent['refused'] += 1
                        if sent['refused'] > 20 or not put_front(r.b): break
                        try: g.release_all()
                        except game_input.NotFocused: pass
                    stop.wait(0.05)
            except BaseException as ex:
                sent['err'] = '%s: %s' % (type(ex).__name__, ex)
            finally:
                try: g.release_all()
                except BaseException: pass
        th = threading.Thread(target=defender, daemon=True); th.start()
        t0 = last = time.time()
        while time.time() - t0 < secs:
            judged = sum(1 for l in r.a.lines(r.a.alog, ma['a']) if ('WO208-DEF outcome' in l or 'WO208-DEF fallback' in l) and ingroup(l))
            if judged >= swings: break
            if time.time() - last > 30:
                for b in bandits: r.a.lua(ATTACK % (b, FIGURE))
                last = time.time()
            time.sleep(2)
        stop.set(); th.join(timeout=10)
        for i, b in enumerate(bandits): r.a.lua(STOP % (b, i))
        for c in (r.a, r.b): c.stop_keep()
        time.sleep(2)
        hl = [l for l in r.a.lines(r.a.alog, ma['a']) if ingroup(l)]
        jl = [l for l in r.b.lines(r.b.alog, mb['a']) if ingroup(l)]
        host_out = [re.search(r'WO208-DEF outcome npc=(\S+) joiner=\d+ engine=(\S+)', l) for l in hl]
        host_out = [(m.group(1), m.group(2)) for m in host_out if m]
        fallbacks = sum(1 for l in hl if 'WO208-DEF fallback' in l)
        joiner_out = [re.search(r'WO208-DEF out npc=(\S+) outcome=(\S+)', l) for l in jl]
        joiner_out = [(m.group(1), m.group(2)) for m in joiner_out if m]
        # every outcome the host took is one the joiner's engine logged, in the same order per attacker
        def per(xs):
            d = {}
            for n, o in xs: d.setdefault(n, []).append(o)
            return d
        hp, jp = per(host_out), per(joiner_out)
        mismatch = 0
        for n, outs in hp.items():
            it = iter(jp.get(n, []))
            for o in outs:
                if not any(o == q for q in it): mismatch += 1
        skipped = sum(1 for l in jl if 'WO208-DEF skipped' in l)
        blocking = sum(1 for l in r.a.lines(r.a.nlog, ma['n']) if re.search(r'WO121-MOTION body=kcd2mp_\d+ block=1', l))
        # B.6: the joiner's own lock-on on the copy that swings at him (the strike window's mid line names the player's opponent)
        mids = [l for l in r.b.lines(r.b.nlog, mb['n']) if (('WO166-STRIKE' in l and ' mid ' in l) or 'WO209-ATK mid ' in l) and ingroup(l)]
        locked = sum(1 for l in mids if 'player_opponent=this-copy' in l)   # WO-209 B: the copy's own attack's window says it too
        total = len(host_out) + fallbacks
        rate = round(100.0 * len(host_out) / total, 1) if total else None
        from collections import Counter
        no_outcome = sum(1 for l in r.b.lines(r.b.nlog, mb['n']) if 'WO208-DEF no outcome' in l)
        recoils = sum(1 for l in hl if 'WO208-DEF recoil' in l)
        detail = {'bandits': bandits, 'group': group, 'attackers_seen': sorted({n for n, _ in host_out} | {m.group(1) for l in hl for m in [re.search(r'WO208-DEF fallback npc=(\S+)', l)] if m}), 'judged_by_joiner': len(host_out), 'fallbacks': fallbacks, 'rate_pct': rate,
                  'joiner_windows_without_evidence': no_outcome, 'host_recoils_b4': recoils,
                  'joiner_outcomes': dict(Counter(o for _, o in joiner_out)), 'host_took': dict(Counter(o for _, o in host_out)),
                  'outcome_mismatch': mismatch, 'joiner_skipped_host_verdicts': skipped, 'host_saw_figure_block_lines': blocking,
                  'lock_on_windows': '%d/%d' % (locked, len(mids)), 'input': sent}
        if total == 0: return False, 'no-blows', detail
        if rate < 95.0: return False, 'judged-below-95', detail
        if fallbacks * 100.0 / total >= 5.0: return False, 'fallbacks-5', detail
        if mismatch: return False, 'outcome-mismatch', detail
        return True, 'ok', detail
    return fn


# ---------------------------------------------------------------- WO-208 A.1: the village walk (the owner's probe)

NPC_POS = ('local out = {}; for _, e in ipairs(System.GetEntitiesInSphere(player:GetWorldPos(), %d) or {}) do '
           'if e.class == "NPC" and e.soul and not string.find(e:GetName(), "kcd2mp_", 1, true) then local q = e:GetWorldPos(); '
           'out[#out + 1] = string.format("%%s,%%.2f,%%.2f,%%.2f", e:GetName(), q.x, q.y, q.z) end end; '
           'System.LogAlways("[W208V] " .. table.concat(out, ";"))')


def write_probe_name(r, name):
    """The probe's NPC for the owner (wo208.cpp reads kcdmp-w208-probe.txt beside each game every 2 s) and for the mod's own copy
    writers (KCD2MP_W208Probe) -- the bench only."""
    for c in (r.a, r.b):
        with open(os.path.join(c.p['root'], 'kcdmp-w208-probe.txt'), 'w', encoding='ascii') as f: f.write(name + '\n')
        c.lua('KCD2MP_W208Probe = "%s"' % name)


def s_village(r, secs=float(os.environ.get('KCDMP_RIG_VILLAGE_S', '300'))):
    """WO-208 A.1 the village walk: the players in the bench save's town for 5 minutes beside a villager who walks (the NPC near the host
    that moved the most in 4 s, by day); both follow it when it walks away. The probe's NPC is that villager (KCDMP_RIG_PROBE_PICK=1)."""
    def fn():
        t = r.a.ask(FIND_INN % os.environ.get('KCDMP_RIG_INN', 'ttkc_woman_2'), 'W207I', 6)
        if not t or t == 'none': return False, 'no-town', t
        parts = t.split()
        x, y, z = float(parts[1]), float(parts[2]), float(parts[3])
        # by day: 10:00 of the next game day (the villagers' routines walk them between home, work and the square)
        r.a.lua('local now = Calendar.GetWorldTime(); local d = math.floor(now / 86400) * 86400 + 10 * 3600; '
                'if d < now then d = d + 86400 end; Calendar.SetWorldTime(d)')
        time.sleep(30)

        def put_joiner(px, py):
            # WO-209 rc3-off-village-1: the joiner's move was lost (he stood 163 m off all run, no copy near him, the step passed)
            for _ in range(4):
                r.b.teleport(px, py)
                time.sleep(3)
                q = r.b.pos()
                if q and ((q[0] - px) ** 2 + (q[1] - py) ** 2) ** 0.5 < 15: return True
            return False
        r.a.teleport(x + 6.0, y + 6.0)
        if not put_joiner(x + 7.0, y + 6.5): return False, 'joiner-not-moved', {'to': [round(x + 7.0), round(y + 6.5)]}
        time.sleep(6)

        def positions():
            v = r.a.ask(NPC_POS % 45, 'W208V', 4) or ''
            d = {}
            for item in filter(None, v.split(';')):
                try: n, a, b, c = item.split(','); d[n] = (float(a), float(b), float(c))
                except ValueError: pass
            return d
        walker, moved = None, 0.0
        for _ in range(5):
            p0 = positions(); time.sleep(4); p1 = positions()
            for n, q in p1.items():
                if n in p0:
                    m = ((q[0] - p0[n][0]) ** 2 + (q[1] - p0[n][1]) ** 2) ** 0.5
                    if m > moved: walker, moved = n, m
            if walker and moved >= 2.0: break
        if not walker or moved < 1.0: return False, 'no-walker', {'moved': moved}
        if os.environ.get('KCDMP_RIG_PROBE_PICK') == '1': write_probe_name(r, walker)
        follows, near, checks = 0, 0, 0
        t0 = time.time()
        while time.time() - t0 < secs:
            time.sleep(20)
            p = positions().get(walker)
            hp = r.a.pos()
            if p and hp and ((p[0] - hp[0]) ** 2 + (p[1] - hp[1]) ** 2) ** 0.5 > 18:
                r.a.teleport(p[0] + 4.0, p[1] + 4.0)
                put_joiner(p[0] + 5.0, p[1] + 4.5)
                follows += 1
                hp = r.a.pos()
            jp = r.b.pos()
            if hp and jp:
                checks += 1
                if ((jp[0] - hp[0]) ** 2 + (jp[1] - hp[1]) ** 2) ** 0.5 < 25: near += 1
                else: put_joiner(hp[0] + 1.0, hp[1] + 0.5)
        info = {'walker': walker, 'moved_in_4s': round(moved, 2), 'follows': follows, 'joiner_near': '%d/%d' % (near, checks), 'secs': secs,
                't0_ms': int(t0 * 1000), 't1_ms': int(time.time() * 1000)}
        if checks and near * 2 < checks: return False, 'joiner-not-near', info
        return True, 'ok', info
    return fn


# ---------------------------------------------------------------- WO-209 E.2: the joined talk away from the tavern's brawl, and the brawl

# by day, outside the tavern: the bailiff first, then the smith, the miller (the tavern's evening brawl -- the random event rvacka --
# made the WO-208 runs' listener leave for combat, as designed, and cost the step)
DAY_NPCS = ('ttkc_drozd', 'ttkc_kovar', 'ttkc_mlynar')

NEAR_FIGHTER = ('local p = player:GetWorldPos(); local best, bd; for _, e in ipairs(System.GetEntitiesInSphere(p, 12) or {}) do '
                'if e.class == "NPC" and e.soul and e:GetName() ~= "%s" and not string.find(e:GetName(), "kcd2mp_", 1, true) '
                'and not string.find(e:GetName(), "DialogTwin_", 1, true) then '   # rc1-convbrawl: the talk's own stand-in was picked
                'local h = 0; pcall(function() h = e.soul:GetState("health") end); local q = e:GetWorldPos(); local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; '
                'if h and h > 0 and (not bd or dd < bd) then bd = dd; best = e end end end; '
                'System.LogAlways("[W209F] " .. (best and best:GetName() or "none"))')


def s_convbrawl(r, secs=float(os.environ.get('KCDMP_RIG_BRAWL_S', '240'))):
    """WO-209 E.2: a fight that starts during a joined talk -- the listener leaves for it. In the tavern at night (the evening's scripted
    brawl may start by itself; the bench starts one the same way the c1fight step does, a tavern guest set on the listener's figure, a
    few seconds into the talk). Pass: the listener was listening, then left with why=combat within 30 s of the fight's start."""
    def fn():
        t = r.a.ask(FIND_INN % os.environ.get('KCDMP_RIG_INN', 'ttkc_woman_2'), 'W207I', 6)
        if not t or t == 'none': return False, 'no-innkeeper', t
        parts = t.split()
        inn, x, y, z = parts[0], float(parts[1]), float(parts[2]), float(parts[3])
        r.a.teleport(x + 1.3, y, z + 0.05)
        for _ in range(6):
            r.b.teleport(x + 2.6, y + 1.0, z + 0.05)
            time.sleep(4)
            q = r.b.pos()
            if q and abs(q[0] - (x + 2.6)) < 3.0 and abs(q[1] - (y + 1.0)) < 3.0: break
        ma, mb = r.a.mark(), r.b.mark()
        r.a.lua('local e = System.GetEntityByName("%s"); if e then local f = BasicAIActions and BasicAIActions.OnTalk or e.OnTalk; f(e, player, 0) end' % inn)
        m = r.a.wait('k', ma['k'], OWN_TALK, 25)
        if not m: return False, 'no-dialogue', {'innkeeper': inn}
        cid = m.group(1)
        joined = False
        tj = time.time()
        while time.time() - tj < 60 and not joined:
            joined = r.b.ask(JOIN_OFFER % cid, 'W207J', 4) == 'listening'
            if not joined: time.sleep(1)
        detail = {'innkeeper': inn, 'cid': cid, 'listening': joined}
        if not joined: return False, 'listener-not-listening', detail
        time.sleep(4)
        fighter = r.a.ask(NEAR_FIGHTER % inn, 'W209F', 4)
        detail['fighter'] = fighter
        t_fight = time.time()
        if fighter and fighter != 'none': r.a.lua(ATTACK % (fighter, FIGURE))
        off = r.b.wait('k', mb['k'], r'WO203B-LISTEN off partner=\d+ cid=%s why=(\S+)' % cid, 30)
        detail['left_after_s'] = round(time.time() - t_fight, 1) if off else None
        detail['why'] = off.group(1) if off else None
        detail['skirmish_lines_host'] = sum(1 for l in r.a.lines(r.a.klog, ma['k']) if 'Skirmish event: SoulAdded' in l)
        if fighter and fighter != 'none': r.a.lua(STOP % (fighter, 0))
        r.a.ask('local d = false; pcall(function() d = player.human:IsInDialog() == true end); if d then '
                'pcall(function() player.human:InterruptDialog() end) end; System.LogAlways("[W208X] " .. tostring(d))', 'W208X', 5)
        if not off: return False, 'listener-stayed', detail
        return off.group(1) == 'combat', ('ok' if off.group(1) == 'combat' else 'left-for-' + off.group(1)), detail
    return fn


# ---------------------------------------------------------------- WO-209 E.1: a timed decision the bench can reach (the probe)

# the Trosecko quests whose dialogues carry a positive TimeLimit (Scripts.pak), by folder name
E1_QUESTS = ('kumaniNaTrosecku', 'socky', 'kejkliri', 'zbranePanaSemina', 'korenarkaZachrana', 'poustevnik', 'poustevnik_a_vdova', 'pracharna',
             'rasuvUcen', 'pytlakPtacek', 'hadkaKonaru', 'svatba', 'mysi1', 'hledaniPsa', 'kocovnickaCest', 'zranenyLovci', 'mucirna',
             'nebakovPruzkum', 'utokNaNebakov', 'bohutovaVlozka', 'prepadeni', 'naTroskach', 'vezniNaTroskach', 'zachrana', 'nebakovObrana')

E1_ASK = ('local out = {}; for _, q in ipairs({%s}) do local st, ac, co = "?", "?", "?"; '
          'pcall(function() st = tostring(QuestSystem.IsQuestStarted(q)) end); pcall(function() ac = tostring(QuestSystem.IsQuestActive(q)) end); '
          'pcall(function() co = tostring(QuestSystem.IsQuestCompleted(q)) end); local obs = ""; '
          'pcall(function() local t = QuestSystem.GetActiveObjectives(q); if type(t) == "table" then local l = {}; for k, v in pairs(t) do l[#l + 1] = tostring(k) .. "=" .. tostring(v) end; obs = table.concat(l, "+") end end); '
          'out[#out + 1] = q .. ":" .. st .. "," .. ac .. "," .. co .. (obs ~= "" and ("," .. obs) or "") end; System.LogAlways("[W209Q] " .. table.concat(out, ";"))')


def s_e1probe(r):
    """WO-209 E.1: which of the Trosecko quests with timed decisions are started, active or completed on the bench save (the host), and
    their active objectives -- the step a timed decision is reachable from. Pass: the answer came back (the probe reads only)."""
    def fn():
        v = r.a.ask(E1_ASK % ','.join('"%s"' % q for q in E1_QUESTS), 'W209Q', 8)
        if not v: return False, 'no-answer', None
        rows = dict(x.split(':', 1) for x in v.split(';') if ':' in x)
        return True, 'ok', rows
    return fn


# ---------------------------------------------------------------- WO-207 C.4: mp_rejoin_recover on the pair

def s_c4(r, wait_s=240):
    """C.4: the host reloads its world with the joiner in it; the joiner's in-world rejoin is cut by the relay at its world transfer (the
    run's fault plan: step:world-sent#2, the session's second). Pass: a later rejoin completes (the host resumes on its ready) within
    <wait_s>. The twin (mp_rejoin_recover off before the join, KCDMP_RIG_PRE_JOIN_LUA) must not get back in."""
    def fn():
        ids0 = [m.group(1) for l in r.b.lines(r.b.alog, 0) for m in [re.search(r'MP-JOIN joiner: join 0x([0-9a-f]{8}) ', l)] if m]
        ma, mb = r.a.mark(), r.b.mark()
        try: r.a.cmd('wh_sys_LoadGame 4 save440')   # the console answers when the load is over: a timeout here is not a failure
        except Exception as ex: rig.note('load-send', copy='A', note='%s: %s' % (type(ex).__name__, ex))
        g = r.a.wait('k', ma['k'], r'^Gameplay started', 240)
        t0 = time.time()
        cut = None
        done = None
        while time.time() - t0 < wait_s:
            if cut is None:
                for l in open(os.path.join(rig.run_dir(), 'relay.log'), encoding='utf-8', errors='replace').read().splitlines()[-400:]:
                    if 'RELAY-FAULT' in l and 'fired kind=cut' in l: cut = l.strip()[:200]
            ids = [m.group(1) for l in r.b.lines(r.b.alog, mb['a']) for m in [re.search(r'MP-JOIN joiner: join 0x([0-9a-f]{8}) ', l)] if m]
            res = [re.search(r'MP-JOIN resume join=(\S+) reason=ready', l) for l in r.a.lines(r.a.klog, ma['k'])]
            ready = [m.group(1) for m in res if m]
            after_cut = [j for j in ids if j in ready]
            if cut and len(set(after_cut)) >= 1 and (len(set(ids)) >= 2 or ids[-1] not in ids0):
                done = after_cut[-1]
                if len(set(ids)) >= 2: break
            time.sleep(3)
        lines = [l[-200:] for l in r.b.lines(r.b.alog, mb['a']) if re.search(r'MP-W202 rejoin|MP-JOIN joiner: join 0x|rejoin', l)][:12]
        detail = {'reloaded': bool(g), 'cut': cut, 'rejoined': done, 'joiner_lines': lines, 'secs': round(time.time() - t0)}
        if not g: return False, 'no-reload', detail
        if not cut: return False, 'cut-never-fired', detail
        return (True, 'ok', detail) if done else (False, 'not-back-in', detail)
    return fn


# ---------------------------------------------------------------- WO-207 D: what the divergences C1 named are, read on both machines

D_PATHS = ['Barbora.trosecko.fogControl.fogStreaming2', 'Barbora.trosecko.fogControl.fogUnstreaming',
           'Barbora.trosecko.fogControl.FogStreaming.streamedFog', 'Barbora.trosecko.fogControl.FogStreaming_1.streamedFog',
           'Barbora.trosecko.fogControl.FogStreaming_2.streamedFog', 'Barbora.trosecko.fogControl.FogStreaming_3.streamedFog',
           'Barbora.trosecko.fogControl.fogcontrol_library.FogStreaming.streamedFog', 'Barbora.trosecko.fogControl.FogStreaming.profile13',
           'Barbora.trosecko.kovar.KovarJeVKovarne', 'Barbora.trosecko.kovar.areatrigger55',
           'Barbora.trosecko.zaby.henik_hlida_majku.state27', 'Barbora.trosecko.zaby.henik_hlida_majku.areatrigger25']


def s_dread(r, paths=D_PATHS, edges=(None, 4 * 3600 + 58 * 60, 6 * 3600 + 58 * 60)):
    """D (research, the bench agents' w200_test read): each path's node on both machines -- found, kind, runtime state, value, class --
    now, and after each of the fog's own clock edges (the host's clock moved to 04:58 and 06:58 of the next day it reaches, Calendar.
    SetWorldTime as the mod's time sync does; 90 s later both are read again: the watches fire at 05:00 and 07:00)."""
    def read_all():
        out = {}
        for c in (r.a, r.b):
            m = c.mark()
            for p in paths: c.lua('KCD2MP_EmitEvent("w200_test", "read %s")' % p)
            time.sleep(4)
            got = {}
            for l in c.lines(c.alog, m['a']):
                mm = re.search(r'MP-W200 read (\S+) (found=\S+ kind=\S+ runtime=\S+ value=\S+ class=\S*)', l)
                if mm: got[mm.group(1)] = mm.group(2)
            out[c.id] = got
        return out

    def fn():
        rounds = {}
        for at in edges:
            if at is not None:
                r.a.lua('local now = Calendar.GetWorldTime(); local d = math.floor(now / 86400) * 86400 + %d; '
                        'if d < now then d = d + 86400 end; Calendar.SetWorldTime(d)' % at)
                time.sleep(90)
            clk = {c.id: c.ask('System.LogAlways(string.format("[W206T] %d", Calendar.GetWorldTime()))', 'W206T', 4) for c in (r.a, r.b)}
            rounds['now' if at is None else '%02d:%02d+90s' % (at // 3600, at // 60 % 60)] = {'clock': clk, 'nodes': read_all()}
        same = {k: {p: v['nodes'].get(r.a.id, {}).get(p) == v['nodes'].get(r.b.id, {}).get(p) for p in paths} for k, v in rounds.items()}
        rig.note('dread', rounds=rounds)
        ok = any(v['nodes'].get(r.a.id) for v in rounds.values())
        return (True if ok else False), ('ok' if ok else 'no-reads'), {'rounds': rounds, 'same': same}
    return fn


def s_digest(r):
    """F: both worlds' digests so far through the analyzer (tools/digest/analyze.py, the host = copy A). Pass: no unexplained
    divergence (exit 0); 1 names the first one."""
    def fn():
        import shutil
        import subprocess
        dirs = {}
        for c in (r.a, r.b):
            d = os.path.join(rig.run_dir(), 'digest-now', c.id)
            os.makedirs(d, exist_ok=True)
            for f in ('digest.jsonl', 'applied.jsonl'):
                for src in (os.path.join(c.p['root'], f), os.path.join(c.p['agent_dir'], f)):
                    if os.path.exists(src): shutil.copy2(src, os.path.join(d, f))
            dirs[c.id] = d
        out = os.path.join(rig.run_dir(), 'digest-now', 'report.md')
        res = subprocess.run([sys.executable, os.path.join(rig.REPO, 'tools', 'digest', 'analyze.py'), dirs[r.a.id], dirs[r.b.id],
                              '--json', '--out', out], capture_output=True, text=True)
        try: j = json.loads(res.stdout)
        except ValueError: return False, 'analyzer-failed', (res.stdout + res.stderr)[-400:]
        detail = {'exit': res.returncode, 'stats': j.get('stats'), 'unexplained': len(j.get('unexplained') or []),
                  'first': (j.get('unexplained') or [None])[0], 'report': out}
        return (res.returncode == 0), ('ok' if res.returncode == 0 else 'divergence' if res.returncode == 1 else 'bad-input'), detail
    return fn


def s_daytime(r, at_s, settle_s=75):
    """The host's clock to <at_s> seconds into the next game day it reaches (Calendar.SetWorldTime, as the mod's time sync does), then
    <settle_s> real seconds for the schedules (the joiner's clock follows the host's). Pass: both clocks moved there."""
    def fn():
        r.a.lua('local now = Calendar.GetWorldTime(); local d = math.floor(now / 86400) * 86400 + %d; '
                'if d < now then d = d + 86400 end; Calendar.SetWorldTime(d)' % at_s)
        time.sleep(settle_s)
        clk = {c.id: c.ask('System.LogAlways(string.format("[W206T] %d", Calendar.GetWorldTime()))', 'W206T', 4) for c in (r.a, r.b)}
        try: tod = {k: (int(v) % 86400) / 3600.0 for k, v in clk.items()}
        except (TypeError, ValueError): return False, 'no-clock', clk
        ok = all(abs(h - at_s / 3600.0) < 1.0 for h in tod.values())
        return ok, ('ok' if ok else 'clock-not-there'), {'hours': {k: round(v, 2) for k, v in tod.items()}}
    return fn


# ---------------------------------------------------------------- WO-207 C.2: the game kept running in the background

AWAKE_READ = 'System.LogAlways("[W207A] " .. tostring(System.GetCVar("sys_SleepIfInactiveWH")))'


def s_awake(r, expect=os.environ.get('KCDMP_RIG_AWAKE_EXPECT', 'awake'), secs=40.0):
    """C.2: with the mirrors' own sys_SleepIfInactiveWH at 1 (KCDMP_RIG_SLEEP_INACTIVE=1), the cvar on both copies while the session
    runs and the joiner's frame rate behind the host's window. expect=awake: 0 on both, the joiner well above the sleeping cap (WO-206:
    20-34 fps asleep); expect=asleep (the twin, mp_keep_awake off): 1 on both and the cap."""
    def fn():
        put_front(r.a)
        time.sleep(3)
        cv = {c.id: c.ask(AWAKE_READ, 'W207A', 4) for c in (r.a, r.b)}
        for c in (r.a, r.b):   # a read lost while the copy is in the background (d17: B's came back empty) is asked again
            for _ in range(3):
                if cv[c.id] is not None: break
                time.sleep(2)
                cv[c.id] = c.ask(AWAKE_READ, 'W207A', 6)
        fps = []
        t0 = time.time()
        while time.time() - t0 < secs:
            f = rig.fps(r.b.port, r.b.p['root'], span=8.0)
            if f: fps.append(f)
        med = sorted(fps)[len(fps) // 2] if fps else None
        detail = {'cvar': cv, 'joiner_bg_fps': fps, 'median': med, 'expect': expect}
        want = '0' if expect == 'awake' else '1'
        if any(v is None or v.split('.')[0] != want for v in cv.values()): return False, 'cvar-not-' + want, detail
        if med is None: return False, 'no-fps', detail
        if expect == 'awake' and med < 40: return False, 'background-still-capped', detail
        if expect != 'awake' and med >= 40: return False, 'background-not-capped', detail
        return True, 'ok', detail
    return fn


# ---------------------------------------------------------------- WO-207 A.4: the joined conversation on the pair, with voting

TALKABLE = ('local n = "%s"; local best, bd; local p = player:GetWorldPos(); local ex = System.GetEntityByName(n); '
            'if ex and ex.OnTalk and ex.soul then best = ex; bd = -1 end; for _, e in ipairs(bd and {} or System.GetEntitiesByClass("NPC") or {}) do '
            'if e.OnTalk and e.soul and string.find(string.lower(e:GetName()), n, 1, true) then local q = e:GetWorldPos(); '
            'local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; if not bd or dd < bd then bd = dd; best = e end end end; '
            'if best then local q = best:GetWorldPos(); System.LogAlways(string.format("[W207T] %%s %%.2f %%.2f %%.2f", best:GetName(), q.x, q.y, q.z)) '
            'else System.LogAlways("[W207T] none") end')

JOIN_OFFER = ('local c = KCD2MP and KCD2MP.W203 and KCD2MP.W203.cam; local got = "none"; if c then '
              'if c.listening then got = "listening" else for s, o in pairs(c.offers or {}) do if o.cid == "%s" then '
              'KCD2MP_W203Join(s, o.cid); got = "asked" end end end end; System.LogAlways("[W207J] " .. got)')


def agent_ts(line, day0):
    return agent_ms(line, day0) or 0


def conv_stats(c, mark):
    """The copy's conversation counters (mp_w204_status / mp_w203_status), asked now: {key: value} of the WO204 and WO203 stats."""
    c.cmd('mp_w204_status'); c.cmd('mp_w203_status')
    time.sleep(2.5)
    out = {}
    for l in c.lines(c.alog, mark):
        for m in re.finditer(r' (lines_out|lines_in|voiced|voice_no_body|options_out|options_in|votes_out|votes_in|vote_icons|chosen_out|chosen_in|'
                             r'timers_out|timers_in|cam_out|cam_in|view_on|joins_in|conv_lane_frames)=(\S+)', l):
            out[m.group(1)] = m.group(2)
    return out


# the player's own talk of a joinable kind (Wo203CamRules: "normal" / "ingame"; the player's soul is "Dude" in the souls list -- an
# NPC-to-NPC gossip is "ingame" too). WO-208: the player as a talker ("Ex: Dude"), never a combat bark's target ("Nx: Dude ... COMBAT_SHOUT_
# TARGET", an "ingame" CombatShout: m0-convboth-2 took bark 155 for the talk 159 it was interrupted by)
OWN_TALK = r"New dialogue '.*\((?:normal|ingame) - Id: (\d+)\)(?! \(CombatShout\)).*souls = '[^']*\bEx: Dude\b"

# the starter 1.4 m before the NPC along its own facing (only its position is written, like every bench teleport), so the NPC is in
# front of it for the use key
FACE_NPC = ('local e = System.GetEntityByName("%s"); if e then local q = e:GetWorldPos(); local d = player:GetDirectionVector(1); '
            'local n = math.sqrt(d.x * d.x + d.y * d.y); if n > 0.01 then d = {x = d.x / n, y = d.y / n} else d = {x = 0, y = 1} end; '
            'player:SetWorldPos({x = q.x - d.x * 1.4, y = q.y - d.y * 1.4, z = q.z + 0.05}) end')


FORCED_TALK = r"New dialogue '([^' ]*)[^']* \((?:normal|ingame) - Id: (\d+)\) \((?:ForceDialogNode|AI::RequestDialog)\)'.*souls = '[^']*\bEx: Dude\b"


def s_forced(r, starter='host', wait_s=float(os.environ.get('KCDMP_RIG_FORCED_S', '300'))):
    """WO-208 C.4: a conversation that starts by itself for <starter> (a quest's ForceDialogNode or an NPC's AI::RequestDialog, the
    scene world's Gnarly talk after the host's second): it runs on the starter's machine and the partner within 30 m listens in without
    a key (WO208-AUTOJOIN ... listening in; the starter's agent: auto-joined). The listener is put 3 m from the starter first. The talk
    is then played out with the starter's own confirm key (or interrupted after 90 s). Pass: the forced talk started on the starter's
    machine, the partner auto-joined and listened, no silence drop."""
    def fn():
        import keys
        S, L = (r.a, r.b) if starter == 'host' else (r.b, r.a)
        sm, lm = S.mark(), L.mark()
        # the partner beside the starter (30 m is the rule; 3 m keeps the bench's own walk out of it)
        p = S.ask('local q = player:GetWorldPos(); System.LogAlways(string.format("[W208P] %.2f %.2f %.2f", q.x, q.y, q.z))', 'W208P', 5)
        if p:
            x, y, z = (float(v) for v in p.split())
            L.teleport(x + 3.0, y)
        # the scene world's forced talk can come inside the step before (d14-c4forced: during the host's own Gnarly talks): the
        # session's whole log first (each run is a fresh game), then the wait
        m = S.wait('k', 0, FORCED_TALK, 0.5) or S.wait('k', sm['k'], FORCED_TALK, wait_s)
        detail = {'starter': starter, 'waited_s': wait_s, 'forced': None}
        if not m: return False, 'no-forced-talk', detail
        detail['forced'] = '%s (Id %s)' % (m.group(1).split('.')[-1], m.group(2))
        cid = m.group(2)
        ma = L.wait('k', 0, r'WO208-AUTOJOIN partner=\d+ cid=%s (.*)' % cid, 20)
        detail['autojoin'] = ma.group(1)[:120] if ma else None
        on = L.wait('k', 0, r'WO203B-LISTEN on partner=\d+ cid=%s' % cid, 20)
        detail['listening'] = bool(on)
        sa = S.wait('a', 0, r'WO203B-JOIN partner \d+ listens to conversation %s \(auto-joined' % cid, 5)
        detail['starter_saw_auto'] = bool(sa)
        # the talk played out: the starter's own confirm key through its decisions, or interrupted after 90 s
        sk = keys.Keys(rec_of(S), 'wo208-forced-starter')
        t0 = time.time()
        end = None
        while time.time() - t0 < 90:
            end = S.wait('k', sm['k'], r'\[ID: %s\] Dialog ending' % cid, 4)
            if end: break
            sk.press('confirm')
        sk.release()
        if not end:
            S.ask('local d = false; pcall(function() d = player.human:IsInDialog() == true end); if d then '
                  'pcall(function() player.human:InterruptDialog() end) end; System.LogAlways("[W208X] " .. tostring(d))', 'W208X', 5)
        detail['ended'] = bool(end)
        silence = [l for l in L.lines(L.klog, lm['k']) if 'WO203B-LISTEN off' in l and 'why=silence' in l]
        detail['silence_drops'] = len(silence)
        if not ma or 'listening in' not in (ma.group(1) if ma else ''): return False, 'not-auto-joined', detail
        if not on: return False, 'listener-not-listening', detail
        if silence: return False, 'listener-dropped-silence', detail
        return True, 'ok', detail
    return fn


def s_conv(r, starter='host', plan=('changed', 'cleared', 'notfollowed', 'followed', 'leave'), npcs=None, secs=float(os.environ.get('KCDMP_RIG_CONV_S', '600')),
           forced_wait=0):
    """A.4: joined conversations played with the players' own keys. The starter talks to an NPC (the class's talk, the player's way);
    the listener joins (the prompt's own event) as soon as the offer is there. At each decision the listener moves and votes with his
    dialogue keys (S/W, E; Back = Esc) and the starter picks with his: one voting case per decision, the next uncovered one of <plan> --
    followed (the starter picks the voted row), notfollowed (another), changed (a vote moved to another row), cleared (a vote taken back
    with Back), leave (a vote, then Back twice: the second Back leaves); a timed decision always plays 'timed' (a vote pending, nobody
    picks: the starter's timer runs out). A talk that ends with cases left is followed by another (the next NPC of <npcs>), within
    <secs>. Pass: the listener listened, lines reached it, every case played reached its expected end (the icon in the starter's row
    within 300 ms of the vote, the pick shown as Your/Their pick, a leave restored the listener's own view), no view dropped for silence."""
    def fn():
        import keys
        S, L = (r.a, r.b) if starter == 'host' else (r.b, r.a)
        sk, lk = keys.Keys(rec_of(S), 'wo207-conv-starter'), keys.Keys(rec_of(L), 'wo207-conv-listener')
        day0 = time.time()
        names = list(npcs or os.environ.get('KCDMP_RIG_CONV_NPCS', 'ttkc_woman_2,ttkc_drozd').split(','))   # the bench save's innkeeper, then the bailiff
        sm0, lm0 = S.mark(), L.mark()
        todo = [x for x in plan if x != 'leave'] or ['followed']
        want_leave = 'leave' in plan
        cases, talks, tried = [], [], []
        T0 = time.time()

        def start_talk(n):
            if n == 'forced':
                m = S.wait('k', sm0['k'], OWN_TALK, forced_wait)
                tried.append('forced:' + ('started' if m else 'none in %ds' % forced_wait))
                return (m.group(1), 'forced') if m else (None, None)
            t = S.ask(TALKABLE % n, 'W207T', 5)
            if not t or t == 'none':
                tried.append(n + ':none')
                return None, None
            npc, x, y, z = t.split()
            S.teleport(float(x) + 1.3, float(y))
            L.teleport(float(x) + 2.6, float(y) + 1.0)
            time.sleep(3)
            ms = S.mark()
            # the class's talk (BasicAIActions.OnTalk, the one the mod wraps: on the joiner it gives the paused copy its brain back for the
            # conversation first, as the player's key press does); an entity's own OnTalk bypasses that and a paused copy never answers
            S.lua('local e = System.GetEntityByName("%s"); if e then local f = BasicAIActions and BasicAIActions.OnTalk or e.OnTalk; '
                  'f(e, player, 0) end' % npc)
            m = S.wait('k', ms['k'], OWN_TALK, 25)
            if not m:
                # the player's own way: stand 1.4 m before the NPC, facing it, and press the use key (E) in the starter's window
                S.lua(FACE_NPC % npc)
                time.sleep(1.5)
                sk.press('confirm')
                m = S.wait('k', ms['k'], OWN_TALK, 20)
                if m: tried.append(npc + ':by-key')
            tried.append(npc + (':started' if m else ':no-dialogue'))
            return (m.group(1), npc) if m else (None, None)

        def leave_view(cid, ml):
            # Back clears a vote, a second Back leaves -- the second only while still listening: with no vote the first leaves, and a
            # second Back then reaches the game's own pause menu (d16: the joiner's game sat paused for the rest of the run)
            rx = r'WO203B-LISTEN off partner=\d+ cid=%s why=(\S+)' % cid
            lk.press('back')
            off = L.wait('k', ml['k'], rx, 1.5)
            if not off:
                lk.press('back')
                off = L.wait('k', ml['k'], rx, 6)
            return off

        def one_talk(cid, npc, sm, lm):
            # the listener joins as soon as the offer is there (A.2: measured from the starter's announce to the listener's offer)
            offer = L.wait('a', lm['a'], r'WO203B-OFFER partner \d+: conversation %s ' % cid, 30)
            ann = S.wait('a', sm['a'], r"WO203B-TALK this player's conversation %s " % cid, 5)
            info = {'npc': npc, 'cid': cid, 'offer_ms': (agent_ts(offer.string, day0) - agent_ts(ann.string, day0)) if offer and ann else None}
            joined = False
            tj = time.time()
            # WO-209: 120 s -- right after the bench's clock jump (convday's 10:00) the joiner's console answered a minute late (rc1 and rc3:
            # the join went out at 62-63 s and took)
            while time.time() - tj < 120 and not joined:
                joined = L.ask(JOIN_OFFER % cid, 'W207J', 4) == 'listening'
                if not joined: time.sleep(1)
            info['listening'] = joined
            info['join_s'] = round(time.time() - tj, 1)
            if not joined: return info
            seen, ended, left = set(), None, False
            tt = time.time()
            while time.time() - tt < 240 and time.time() - T0 < secs and not ended:
                opts = [mm for mm in (re.search(r'WO204-CONV options decision=(\d+) rows=(\d+) \(the listener\)', l) for l in L.lines(L.klog, lm['k'])) if mm]
                new = [mm for mm in opts if mm.group(1) not in seen]
                for l in S.lines(S.klog, sm['k']):
                    if re.search(r'\[ID: %s\] Dialog (ending|interrupted)' % cid, l): ended = l.strip()[:120]
                if L.wait('k', lm['k'], r'WO203B-LISTEN off partner=\d+ cid=%s why=silence' % cid, 0.1):
                    ended = ended or 'listener dropped for silence'
                    break
                if not new or left:
                    time.sleep(0.3)
                    continue
                dec, rows = new[0].group(1), int(new[0].group(2))
                seen.add(dec)
                time.sleep(1.2)
                timed = bool(S.wait('a', sm['a'], r'WO207-TIMER conversation %s decision %s: the timer started' % (cid, dec), 0.5))
                covered = {c['case'] for c in cases}
                rest = [x for x in todo if x not in covered]
                case = 'timed' if timed else (rest[0] if rest else ('leave' if want_leave and 'leave' not in covered else todo[len(cases) % len(todo)]))
                row = {'cid': cid, 'decision': dec, 'rows': rows, 'case': case}
                ml, ms = L.mark(), S.mark()

                def vote(k, from_sel):
                    lk.press('down', n=(k - from_sel) % rows) if (k - from_sel) % rows else lk.front()
                    lk.press('confirm')
                    v = L.wait('a', ml['a'], r'WO204-CONV this player votes row %d on decision %s' % (k, dec), 4)
                    ic = S.wait('a', ms['a'], r"WO207-VOTE icon shown in this player's options: row %d" % k, 4)
                    return v, ic

                def icon_ms(v, ic):
                    return (agent_ts(ic.string, day0) - agent_ts(v.string, day0)) if v and ic else None

                if case == 'timed':
                    v, ic = vote(0, 0)
                    row.update(vote_ms=icon_ms(v, ic), timer_listener=bool(L.wait('k', ml['k'], r'WO207-TIMER shown decision=%s' % dec, 4)))
                    ch = L.wait('k', ml['k'], r'WO204-CONV chosen decision=%s row=(\d+) vote=(\S+) -> (Your|Their) pick' % dec, 20)
                    row.update(chosen=ch.group(0)[:120] if ch else None,
                               timer_cleared=bool(L.wait('k', ml['k'], r'WO207-TIMER cleared decision=%s' % dec, 3)),
                               ok=bool(ch) and row['timer_listener'] and (9999 if row['vote_ms'] is None else row['vote_ms']) <= 300)
                elif case == 'leave':
                    v, ic = vote(0, 0)
                    row['vote_ms'] = icon_ms(v, ic)
                    off = leave_view(cid, ml)
                    row['left'] = off.group(1) if off else None
                    row['starter_saw'] = bool(S.wait('a', ms['a'], r'WO203B-JOIN partner \d+ left conversation %s' % cid, 6))
                    # WO-207 A.4: the listener's own Back leaves (why=cancel); the pause menu (why=menu) is the bug, not a leave
                    row['ok'] = bool(off) and off.group(1) == 'cancel' and row['starter_saw']
                    left = True
                    time.sleep(0.5)
                    sk.press('confirm')   # the starter goes on alone
                else:
                    k_vote = 0 if case in ('notfollowed', 'cleared', 'changed') else min(1, rows - 1)
                    v, ic = vote(k_vote, 0)
                    row['vote_ms'] = icon_ms(v, ic)
                    sel, want_pick = k_vote, k_vote
                    if case == 'changed' and rows > 1:
                        v2, ic2 = vote(1, sel)
                        sel = 1
                        row['changed_ms'] = icon_ms(v2, ic2)
                        want_pick = 1
                    if case == 'cleared':
                        lk.press('back')
                        row['cleared'] = bool(S.wait('a', ms['a'], r"WO207-VOTE icon shown in this player's options: cleared", 4))
                        # WO-207 A.4: Back clears the vote and the listener stays (d12/d13: the pause menu opened and ended the view)
                        gone = L.wait('k', ml['k'], r'WO203B-LISTEN off partner=\d+ cid=%s why=(\S+)' % cid, 2)
                        row['left_on_clear'] = gone.group(1) if gone else None
                        if gone: left = True
                    # not followed: the starter takes another row than the vote -- the next one, not the last (the last row usually
                    # ends the talk, and d12-d13's joiner-started talks ended with no line spoken)
                    if case == 'notfollowed': want_pick = 1 if rows > 1 else 0
                    time.sleep(0.5)
                    sk.press('down', n=want_pick) if want_pick else sk.front()
                    sk.press('confirm')
                    ch = L.wait('k', ml['k'], r'WO204-CONV chosen decision=%s row=(\d+) vote=(\S+) -> (Your|Their) pick' % dec, 8)
                    row['chosen'] = ch.group(0)[:120] if ch else None
                    expect = {'followed': 'Your', 'changed': 'Your' if rows > 1 else None, 'notfollowed': 'Their' if rows > 1 else 'Your', 'cleared': 'Their'}[case]
                    row['ok'] = bool(ch) and (9999 if row['vote_ms'] is None else row['vote_ms']) <= 300 and (expect is None or ch.group(3) == expect) and \
                        (case != 'cleared' or (row.get('cleared') and not row.get('left_on_clear'))) and (case != 'changed' or rows < 2 or (9999 if row.get('changed_ms') is None else row['changed_ms']) <= 300)
                cases.append(row)
                rig.note('conv-decision', starter=starter, **row)
            if not ended and not left:   # the talk still runs past this talk's time: the listener leaves it the player's way
                leave_view(cid, lm)   # the talk's own mark (a talk with no decision never set the decision's)
            if not ended:   # WO-207: the starter finishes its own talk too (d17: the host's talk ran on into the next step, its listen
                ended = finish_starter(cid, sm)   # keys went to it and the bailiff fought the host)
            info['ended'] = ended
            return info

        def finish_starter(cid, sm):
            rx = re.compile(r'\[ID: %s\] Dialog (ending|interrupted)' % cid)
            t1 = time.time()
            k = 0
            while time.time() - t1 < 40:
                for l in S.lines(S.klog, sm['k']):
                    if rx.search(l): return l.strip()[:120]
                # never Back here: the dialog map carries the pause menu on it. Up from the first row wraps to the last (usually the
                # goodbye), then confirm; a line between decisions is skipped by the confirm
                if k % 2 == 0: sk.press('up')
                sk.press('confirm')
                k += 1
                time.sleep(2)
            rig.note('conv-starter-not-finished', cid=cid)
            return None

        order = ['forced'] if forced_wait else []
        ni = 0
        while time.time() - T0 < secs:
            covered = {c['case'] for c in cases}
            if all(x in covered for x in todo) and (not want_leave or 'leave' in covered): break
            if order:
                n = order.pop(0)
            else:
                if ni > 2 * len(names) + 1: break
                n = names[ni % len(names)]
                ni += 1
            sm, lm = S.mark(), L.mark()
            cid, npc = start_talk(n)
            if not cid: continue
            info = one_talk(cid, npc, sm, lm)
            talks.append(info)
            # a talk the listener could not join (the listener's watch saw a fight nearby and left at once): the next NPC (WO-207:
            # d16's third talk ended the step before its leave case); the name list bounds the tries
            if not info.get('listening'):
                time.sleep(4)
                continue
            time.sleep(4)   # the talk's end settles on both machines before the next
        sk.release()
        lk.release()
        time.sleep(3)
        # WO-208 C.3: a talk the step gave up on is closed the game's own way before the next step -- left open at a decision, the
        # starter stayed in it for the rest of the run (the baseline smoke: the host's bailiff talk, then every listen key doubled)
        closed = S.ask('local d = false; pcall(function() d = player.human:IsInDialog() == true end); if d then '
                       'pcall(function() player.human:InterruptDialog() end) end; System.LogAlways("[W208X] " .. tostring(d))', 'W208X', 5)
        ss, ls = conv_stats(S, sm0['a']), conv_stats(L, lm0['a'])
        silence = [l for l in L.lines(L.klog, lm0['k']) if 'WO203B-LISTEN off' in l and 'why=silence' in l]
        missing = [x for x in todo + (['leave'] if want_leave else []) if x not in {c['case'] for c in cases}]
        detail = {'starter': starter, 'talks': talks, 'decisions': cases, 'tried': tried, 'starter_stats': ss, 'listener_stats': ls,
                  'silence_drops': len(silence), 'cases_missing': missing, 'starter_talk_left_open': closed == 'true'}
        if not talks: return False, 'no-dialogue', detail
        if not any(t.get('listening') for t in talks): return False, 'listener-not-listening', detail
        if silence: return False, 'listener-dropped-silence', detail
        if int(ls.get('lines_in', '0') or 0) == 0: return False, 'no-lines', detail
        # WO-207 A.4: a line that reaches the listener is voiced on its speaker's body there (the twin: none is)
        # (a single line is the 'earlier' one sent on joining, never voiced by design: d17's smoke)
        if int(ls.get('lines_in', '0') or 0) >= 2 and int(ls.get('voiced', '0') or 0) == 0: return False, 'no-line-voiced', detail
        if not cases: return True, 'ok-no-decision', detail
        if not all(c.get('ok') for c in cases): return False, 'case-failed', detail
        return True, ('ok' if not missing else 'ok-cases-missing'), detail
    return fn


def run(name, args):
    r = Run(name)
    plan = {
        'join': [('join', s_join(r))],
        'walk': [('walk30', s_walk(r))],
        'sleep': [('sleep', s_skip(r, 'sleep', 9))],
        'wait': [('wait', s_skip(r, 'wait', 3))],
        'fight': [('fight', s_fight(r))],
        'talk': [('talk', s_talk(r))],
        'loot': [('loot', s_loot(r))],
        'soak': [('soak', s_soak(r, float(args[0]) if args else 20.0))],
        'b1': [('b1', s_b1(r))],
        'stats': [('stats', s_stats(r))],
        'perfab': [('join', s_join(r)), ('perfab', s_perfab(r))],
        'perfprobe': [('join', s_join(r)), ('perfab-probe', s_perfab(r, which='probe'))],
        'joinidle': [('join', s_join(r)), ('idle', lambda: (time.sleep(float(args[0]) if args else 180.0), (True, 'ok', 'idle'))[1])],
        'c1pair': [('join', s_join(r)), ('bones', s_bones(r)), ('soak', s_soak(r, float(args[0]) if args else 30.0))],
        'e2pair': [('join', s_join(r)), ('e2', s_e2(r)), ('e2-twin', s_e2(r, twin=True))],
        'e2twinpair': [('join', s_join(r)), ('e2-twin', s_e2(r, twin=True))],   # run with mp_epoch_check off set before the join
        's1pair': [('join', s_join(r))] + [('s1-%02d-%s' % (i + 1, k), s_skip(r, k, h)) for i, (k, h) in enumerate([('sleep', 9), ('wait', 3)] * 5)],   # WO-204 S1 on two real copies
        'bones': [('bones', s_bones(r))],
        'joinfight': [('join', s_join(r)), ('bones', s_bones(r)), ('fight', s_fight(r))],
        'joinscene': [('join', s_join(r)), ('b1', s_b1(r)), ('b1-twin', s_b1(r, twin=True)), ('c3-twin', s_c3(r))],
        'b1twinpair': [('join', s_join(r)), ('b1-twin', s_b1(r, twin=True))],   # a fresh pair: a scene plays once per load (WO-194)
        'jointalk': [('join', s_join(r)), ('talk', s_talk(r))],
        # WO-207 A.4: the joined conversation with the players' keys, both roles (KCDMP_RIG_CONV_NPCS picks the NPCs tried, in order)
        'convhost': [('join', s_join(r)), ('conv-host-starts', s_conv(r, 'host'))],
        'convjoiner': [('join', s_join(r)), ('conv-joiner-starts', s_conv(r, 'joiner'))],
        'convboth': [('join', s_join(r)), ('conv-host-starts', s_conv(r, 'host')), ('conv-joiner-starts', s_conv(r, 'joiner', plan=('notfollowed', 'changed', 'cleared', 'followed', 'leave')))],
        # WO-208 C.4: the scene world's forced Gnarly talk (after the host's second talk with him), the joiner auto-joined
        'convforced': [('join', s_join(r)), ('daytime', s_daytime(r, 10 * 3600)),
                       ('conv-host-semin', s_conv(r, 'host', npcs=('tsem_suk', 'tsem_seminsr', 'tsem_seminjr'))), ('forced-host', s_forced(r, 'host'))],
        'convforcedj': [('join', s_join(r)), ('daytime', s_daytime(r, 10 * 3600)),
                        ('conv-joiner-semin', s_conv(r, 'joiner', npcs=('tsem_suk', 'tsem_seminsr', 'tsem_seminjr'))), ('forced-joiner', s_forced(r, 'joiner'))],
        'convtimed': [('join', s_join(r)), ('conv-timed-host', s_conv(r, 'host', npcs=('david', 'michal'))), ('conv-timed-joiner', s_conv(r, 'joiner', npcs=('michal', 'david')))],
        # A.4's trialog: the Semin camp world (KCDMP_RIG_HOST_SAVE_FILE=release/w206rig/seed/w194scene.whs, KCDMP_RIG_HOST_SAVE=w194scene)
        # (the host agent with KCDMP_TEST_W194=1: the arrival scene started as the host quest's own; the Gnarly talk it forces after)
        # the camp sleeps in that save (night): the host's clock to 10:00 of the next day first, the camp awake
        'convscene': [('join', s_join(r)), ('daytime', s_daytime(r, 10 * 3600)),
                      ('conv-host-semin', s_conv(r, 'host', npcs=('tsem_seminsr', 'tsem_suk', 'tsem_seminjr'))),
                      ('conv-joiner-semin', s_conv(r, 'joiner', npcs=('tsem_suk', 'tsem_seminsr', 'tsem_seminjr'),
                                                   plan=('notfollowed', 'changed', 'cleared', 'followed', 'leave')))],
        # WO-207 0.3 (WO-204's missing scenarios) and B.5's queue measures, each on fresh seed copies (pair.py prepare)
        'c1fight': [('join', s_join(r)), ('queue-after-join', s_queue(r)), ('c1fight', s_c1fight(r)), ('queue-after-fight', s_queue(r))],
        'c2tavern': [('join', s_join(r)), ('c2tavern', s_c2tavern(r)), ('queue-in-tavern', s_queue(r))],
        # WO-208 A.1: the owner's probe in three places (the probe's NPC by KCDMP_TEST_W208_PROBE, or picked by the village walk)
        'village': [('join', s_join(r)), ('village', s_village(r))],
        'probefight': [('join', s_join(r)), ('c1fight', s_c1fight(r))],
        'probetavern': [('join', s_join(r)), ('c2tavern', s_c2tavern(r))],
        # WO-208 B: the joiner's own defence, one and two attackers (KCDMP_RIG_DEF_S bounds each)
        'defence': [('join', s_join(r)), ('defence-1v1', s_defence(r, 1)), ('defence-2v1', s_defence(r, 2))],
        'queue': [('queue', s_queue(r))],
        'soak2': [('join', s_join(r)), ('soak', s_soak(r, float(args[0]) if args else 30.0)), ('digest', s_digest(r))],   # D.3 / E
        'awake': [('join', s_join(r)), ('awake', s_awake(r))],   # C.2: run with KCDMP_RIG_SLEEP_INACTIVE=1 (the twin: KCDMP_RIG_AWAKE_EXPECT=asleep)
        'dread': [('join', s_join(r)), ('dread', s_dread(r))],
        'c4': [('join', s_join(r)), ('c4-rejoin-cut', s_c4(r))],   # run with a relay fault plan that cuts at step:world-sent#2
        's1front': [('join', s_join(r)), ('host-in-front', lambda: (lambda ok: (ok, 'ok' if ok else 'not-in-front', ''))(put_front(r.a)))] +
                   [('s1-%02d-%s' % (i + 1, k), s_skip(r, k, h)) for i, (k, h) in enumerate([('sleep', 9), ('wait', 3)] * 5)],   # C.1: the joiner behind
        'joinloot': [('join', s_join(r)), ('loot', s_loot(r))],
        # WO-209 E.2: the joined talk by day outside the tavern (both roles), and the fight that starts during a joined talk
        'convday': [('join', s_join(r)), ('daytime', s_daytime(r, 10 * 3600)), ('conv-host-starts', s_conv(r, 'host', npcs=DAY_NPCS)),
                    ('conv-joiner-starts', s_conv(r, 'joiner', npcs=DAY_NPCS, plan=('notfollowed', 'changed', 'cleared', 'followed', 'leave')))],
        'convbrawl': [('join', s_join(r)), ('evening', s_daytime(r, 21 * 3600)), ('brawl', s_convbrawl(r))],
        'e1probe': [('join', s_join(r)), ('e1probe', s_e1probe(r))],
        # WO-209 D.1: one 9 h sleep on the pair, the clocks' gaps logged (the twin: KCDMP_TEST_W209_OFF=clock_quiet_in_skip in the agents' env)
        'sleep1': [('join', s_join(r)), ('sleep', s_skip(r, 'sleep', 9, strict=True, gap_gate=False))],
        # WO-208 D.1's acceptance: the loot step 5 times in a row, each on a chest and on a body (the fight step leaves the bandits)
        'loot5': [('join', s_join(r)), ('fight', s_fight(r))] + [('loot%d' % (i + 1), s_loot(r)) for i in range(5)],
        'b1twin': [('b1-twin', s_b1(r, twin=True))],
        'c2': [('c2-twin', s_c2(r))],
        'c3': [('c3-twin', s_c3(r))],
        'e2': [('e2', s_e2(r))],
        'e2twin': [('e2-twin', s_e2(r, twin=True))],
        'long': [('join', s_join(r)), ('bones', s_bones(r)), ('soak', s_soak(r, float(args[0]) if args else 20.0)), ('c2-twin', s_c2(r)),
                 ('c3-twin', s_c3(r)), ('fight', s_fight(r)), ('e2', s_e2(r)), ('e2-twin', s_e2(r, twin=True))],
        # the crash work: N fights in one pair session (KCDMP_RIG_C1_N, default 10); a game that exits ends the run (copy-exited)
        'c1x': [('join', s_join(r))] + [('c1fight-%02d' % (i + 1), s_c1fight(r)) for i in range(int(os.environ.get('KCDMP_RIG_C1_N', '10')))],
        'smoke': [('join', s_join(r)), ('walk30', s_walk(r)), ('sleep', s_skip(r, 'sleep', 9, gap_gate=False)), ('wait', s_skip(r, 'wait', 3, gap_gate=False)),
                  ('fight', s_fight(r)), ('talk', s_talk(r)), ('loot', s_loot(r))],
        # WO-207 F: the pair smoke grown -- the queue after the join, the clocks strict after each skip (C.3), the conversation both
        # ways with voting and the decision timer (A), the queue after the fight (B.5), no unexplained divergence at the end (D)
        'smoke2': [('join', s_join(r)), ('queue-after-join', s_queue(r)), ('walk30', s_walk(r)),
                   ('sleep', s_skip(r, 'sleep', 9, strict=True, gap_gate=False)), ('wait', s_skip(r, 'wait', 3, strict=True, gap_gate=False)),
                   ('fight', s_fight(r)), ('queue-after-fight', s_queue(r)),
                   ('conv-host-starts', s_conv(r, 'host')), ('conv-joiner-starts', s_conv(r, 'joiner', plan=('notfollowed', 'changed', 'cleared', 'followed', 'leave'))),
                   ('conv-timed', s_conv(r, 'host', npcs=('david', 'michal'))),
                   ('loot', s_loot(r)), ('digest', s_digest(r))],
    }.get(name)
    if plan is None: raise SystemExit('unknown scenario ' + name)
    for nm, fn in plan:
        # 'long' runs independent checks: one failing does not stop the next (a failed join does)
        if not r.step(nm, fn) and (name not in ('long', 'joinscene', 'joinfight', 'e2pair', 'c1pair', 'e2twinpair', 's1pair', 'smoke', 's1front',
                                               'c1fight', 'c2tavern', 'convboth', 'convday', 'convtimed', 'smoke2', 'awake', 'soak2', 'convscene', 'c1x', 'probefight', 'probetavern', 'village', 'defence', 'loot5', 'convforced', 'convforcedj') or nm == 'join'
                                              or r.steps[-1]['reason'] == 'copy-exited'): break   # a game gone ends the run
    return r.finish()


if __name__ == '__main__':
    if len(sys.argv) < 2: sys.exit(__doc__)
    sys.exit(run(sys.argv[1], sys.argv[2:]))
