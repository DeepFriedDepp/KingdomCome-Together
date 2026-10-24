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
            t0 = time.time()
            while time.time() - t0 < 6:
                if any(tok in l for l in self.lines(self.klog, off)): return True
                time.sleep(0.2)
        rig.note('lua-lost', copy=self.id, code=code[:200])
        return False

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
            self.lua('local zz = System.GetTerrainElevation({x=%f, y=%f, z=0}); player:SetWorldPos({x=%f, y=%f, z=zz + 0.2})' % (x, y, x, y))
        else:
            self.lua('player:SetWorldPos({x=%f, y=%f, z=%f})' % (x, y, z))

    def keep_up(self):
        """A health keeper for a fight (never a real death in a throwaway world)."""
        self.lua('W206KEEP = (W206KEEP or 0) + 1; local gen = W206KEEP; local function k() if gen ~= W206KEEP then return end; '
                 'if player.soul:GetState("health") < 60 then player.soul:SetState("health", 100) end; Script.SetTimer(400, k) end; k()')

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
        except Exception as ex:   # a step that throws is a failed step with its reason
            ok, reason, detail = False, 'exception', '%s: %s' % (type(ex).__name__, ex)
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


def s_skip(r, kind, hours):
    """A shared sleep / wait. The joiner asks (its bed's own event), the host answers yes (mp_sleep_yes); then the joiner's own skip
    starts the way an accepter's does (the bench's stand-in for the hour picker: a bench agent's w206_test), its DLL's "began"
    sends the Begin, and the host's skip runs. Pass: the host's skip began and ended, the joiner's ended, the clocks agree."""
    def fn():
        ma, mb = r.a.mark(), r.b.mark()
        r.b.lua('KCD2MP_EmitEvent("w140_ask", "%s 0")' % kind)
        if not r.a.wait('a', ma['a'], r'MP-W140 .*wants to (sleep|wait).*asking this player', 40): return False, 'no-prompt', 'the host was never asked'
        time.sleep(0.6)
        r.a.cmd('mp_sleep_yes')
        if not r.b.wait('a', mb['a'], r'MP-W140 vote .*everyone said yes', 40): return False, 'no-yes', 'the joiner never heard the yes'
        r.b.lua('KCD2MP_EmitEvent("w206_test", "skip %s %d")' % (kind, hours))
        began = r.a.wait('a', ma['a'], r'MP-W140 .*chose .* h|WO140-STATE', 90)
        if not r.b.wait('a', mb['a'], r'MP-W140 vote .*this player chose', 60): return False, 'no-begin', 'the joiner never sent the Begin'
        ha = r.a.wait('n', ma['n'], r'WO140-STATE', 60)
        if not ha: return False, 'host-no-skip', 'the host skip never began'
        t0 = time.time()
        while time.time() - t0 < 240:
            na = r.a.lines(r.a.nlog, ma['n'])
            if any(re.search(r'WO140-STATE .*(-> 0|hours=0\.00)', l) for l in na): break
            time.sleep(2)
        else:
            return False, 'skip-not-ended', 'the host skip never ended'
        # the joiner's clock is pulled to the host's after a skip (WO-140): the clocks must meet within 5 game minutes in 60 s
        t1 = time.time()
        diffs = []
        while time.time() - t1 < 60:
            ca = r.a.ask('System.LogAlways(string.format("[W206T] %d", Calendar.GetWorldTime()))', 'W206T', 4)
            cb = r.b.ask('System.LogAlways(string.format("[W206T] %d", Calendar.GetWorldTime()))', 'W206T', 4)
            try: diff = abs(int(ca) - int(cb))
            except (TypeError, ValueError): diff = None
            diffs.append(diff)
            if diff is not None and diff <= 300: break
            time.sleep(5)
        ok = diff is not None and diff <= 300
        return ok, ('ok' if ok else 'clocks-differ'), {'host_time': ca, 'joiner_time': cb, 'diff_s': diff, 'diffs_s': diffs, 'began': began.group(0)[:120] if began else None}
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


CHEST_NEAR = ('local best, bd; local p = player:GetWorldPos(); for _, c in ipairs({"Stash", "CartStash", "DestroStash"}) do '
              'for _, e in ipairs(System.GetEntitiesByClass(c) or {}) do if e.inventory and #(e.inventory:GetInventoryTable() or {}) > 0 then '
              'local q = e:GetWorldPos(); local dd = (q.x - p.x) ^ 2 + (q.y - p.y) ^ 2; if dd < 250000 and (not bd or dd < bd) then bd = dd; best = e end end end end; '
              'if best then local q = best:GetWorldPos(); System.LogAlways(string.format("[W206C] %s %.2f %.2f %.2f", best:GetName(), q.x, q.y, q.z)) '
              'else System.LogAlways("[W206C] none") end')

TAKE_ONE = ('local e = System.GetEntityByName("%s"); local w = e and (e.inventory:GetInventoryTable() or {})[1]; local cls = "?"; '
            'if w then local it = ItemManager.GetItem(w); if it then cls = tostring(it.class) end; pcall(function() player.inventory:AddItem(w) end) end; '
            'System.LogAlways("[W206L] " .. tostring(w) .. " " .. cls)')


def s_loot(r):
    """One loot take: the joiner takes one item out of a chest within 500 m (moved beside it first) into its own pack, the way the
    loot screen moves it; the mod's container pass sees it (WO134-CHEST take) and the joiner's agent records it in its own ledger
    for the host's world (chests are per player, WO-134)."""
    def fn():
        t = r.b.ask(CHEST_NEAR, 'W206C', 6)
        if not t or t == 'none': return False, 'no-chest', t
        name, x, y, z = t.split()
        r.b.teleport(float(x) + 0.8, float(y), float(z) + 0.05)   # within the mod's 3 m reach, at the chest's own height (shelves)
        time.sleep(4)   # the pass snapshots a container on arrival
        ma, mb = r.a.mark(), r.b.mark()
        took = r.b.ask(TAKE_ONE % name, 'W206L', 5)
        mk = r.b.wait('k', mb['k'], r'WO134-CHEST take chest=(\S+) cls=(\S+) n=(\d+)', 20)
        if not mk: return False, 'no-take-seen', {'chest': name, 'took': took}
        # chests are per player (WO-134): the take goes into this joiner's own ledger for the host's world (stored with its next
        # Henry snapshot); the host is not told live, by design
        ml = r.b.wait('a', mb['a'], r'MP-WO134 chests: joiner took (\d+) x (\S+) from (\S+) .*ledger (\d+)', 20)
        detail = {'chest': name, 'took': took, 'joiner_lua': mk.group(0)[:160], 'joiner_agent': ml.group(0)[:200] if ml else None}
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


def s_stats(r):
    """The end of a run: both machines' epoch counters (E1 reads them from every B2 run) and their digest/probe counts."""
    def fn():
        st = {c.id: epoch_stats(c) for c in (r.a, r.b)}
        return (True, 'ok', st) if all(st.values()) else (False, 'no-stats', st)
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
        'joinloot': [('join', s_join(r)), ('loot', s_loot(r))],
        'b1twin': [('b1-twin', s_b1(r, twin=True))],
        'c2': [('c2-twin', s_c2(r))],
        'c3': [('c3-twin', s_c3(r))],
        'e2': [('e2', s_e2(r))],
        'e2twin': [('e2-twin', s_e2(r, twin=True))],
        'long': [('join', s_join(r)), ('bones', s_bones(r)), ('soak', s_soak(r, float(args[0]) if args else 20.0)), ('c2-twin', s_c2(r)),
                 ('c3-twin', s_c3(r)), ('fight', s_fight(r)), ('e2', s_e2(r)), ('e2-twin', s_e2(r, twin=True))],
        'smoke': [('join', s_join(r)), ('walk30', s_walk(r)), ('sleep', s_skip(r, 'sleep', 9)), ('wait', s_skip(r, 'wait', 3)),
                  ('fight', s_fight(r)), ('talk', s_talk(r)), ('loot', s_loot(r))],
    }.get(name)
    if plan is None: raise SystemExit('unknown scenario ' + name)
    for nm, fn in plan:
        # 'long' runs independent checks: one failing does not stop the next (a failed join does)
        if not r.step(nm, fn) and (name not in ('long', 'joinscene', 'joinfight', 'e2pair', 'c1pair', 'e2twinpair', 's1pair', 'smoke') or nm == 'join'): break
    return r.finish()


if __name__ == '__main__':
    if len(sys.argv) < 2: sys.exit(__doc__)
    sys.exit(run(sys.argv[1], sys.argv[2:]))
