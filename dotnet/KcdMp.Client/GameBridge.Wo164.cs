// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-164: the 0.46.5 / 0.47.0 tester findings, agent half (docs/WO-164-findings.md). The pure rules are Wo164Rules (Wo164.cs);
// the game-side half is kdcmp.lua's WO-164 section; the native half is wo141.cpp (sweep, sit truth), wo137.cpp (direct write)
// and respawn_actions.cpp (the map marker gate).
public partial class GameBridge
{
    private static double W164Now() => Environment.TickCount64 / 1000.0;
    private static string W164I(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

    private volatile bool _w164On = true;   // mp_w164 (the agent's half of every WO-164 item; the Lua toggles each piece)

    // ================================================================ engine lines (talk, planner, dialogue commands)

    private readonly ConcurrentDictionary<string, List<double>> _w164Err = new(StringComparer.Ordinal);   // copy -> refusal times
    private readonly ConcurrentDictionary<string, double> _w164SweptAt = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, int> _w164DialogCmds = new();                              // dialog id -> commands
    private long _w164RandomEvents, _w164MinigameLines;

    private static readonly Regex W164Running = new(@"^Running dialogue '([^'(]+?)(?: / [^']*)? \(\w+ - Id: (\d+)\) \[Ex0: (\S+)(?: [EN]x1: (\S+))?[^']*'.* with soul '([^']+)'", RegexOptions.Compiled);
    private static readonly Regex W164Cmd = new(@"\] DialogCommand-\w+: dialogId=(\d+)", RegexOptions.Compiled);

    /// <summary>LogTailGameTransport.Wo144Line routes these engine lines here first (Wo144OnEngineLine).</summary>
    private void Wo164OnEngineLine(string line)
    {
        if (!_w164On) return;
        string? npc = Wo160Rules.PlannerErrorNpc(line, out bool refused);
        if (npc is not null)
        {
            if (!refused) return;
            double now = W164Now();
            var list = _w164Err.GetOrAdd(npc, _ => new List<double>());
            bool due;
            lock (list)
            {
                list.Add(now);
                if (list.Count > 64) list.RemoveRange(0, list.Count - 64);
                due = Wo164Rules.AdaptiveDue(list, now, _w164SweptAt.GetValueOrDefault(npc, -1e9));
            }
            if (due && W141Joiner)
            {
                _w164SweptAt[npc] = now;   // the Lua's own cooldown too; this keeps the agent from asking every line
                _ = ExecLuaAsync($"if KCD2MP_W164Sweep then KCD2MP_W164Sweep(\"{EscapeLua(npc)}\", \"adaptive\") end");
            }
            return;
        }
        if (line.StartsWith("Running dialogue '", StringComparison.Ordinal)) { W164OnRunningDialogue(line); return; }
        var cm = W164Cmd.Match(line);
        if (cm.Success && int.TryParse(cm.Groups[1].Value, out int did)) { _w164DialogCmds.AddOrUpdate(did, 1, (_, n) => n + 1); return; }
        if (line.StartsWith("<RandomEvent>", StringComparison.Ordinal)) { Interlocked.Increment(ref _w164RandomEvents); return; }
        if (line.Contains("of minigame entity", StringComparison.Ordinal))
        {
            // WO-164 DI: the dice table's own entity (and every other minigame's) enabled / disabled for this player
            if (Interlocked.Increment(ref _w164MinigameLines) <= 40 || line.StartsWith("Enable", StringComparison.Ordinal))
                Console.WriteLine($"WO164-TALK kind=minigame line=\"{line.Trim()}\"");
        }
    }

    private int W164ErrorsBetween(string npc, double from, double to)
    {
        if (!_w164Err.TryGetValue(npc, out var list)) return 0;
        lock (list) return Wo164Rules.ErrorsBetween(list, from, to);
    }

    // ================================================================ T0: one line per talk attempt and per outcome

    private sealed class W164Talk
    {
        public string Npc = "";
        public double AskedAt;
        public string Via = "";
        public bool PausedBefore;
        public int Err10s, OpenRequests, QuestMismatch;
        public string FirstMismatch = "-";
        public bool HostBusy;
        public string Placed = "none";
        public int DialogId = -1;
        public string Dialogue = "";
        public string PreemptedBy = "";
        public double StartedAt = -1;
        public string Retry = "";
    }

    private readonly ConcurrentDictionary<string, W164Talk> _w164Talks = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _w164LastTalks = new();
    private readonly ConcurrentDictionary<string, bool> _w164HostTalks = new(StringComparer.Ordinal);   // the host's own conversations
    private long _w164TalkAsks, _w164TalkStarts, _w164TalkFails, _w164Preempted;

    /// <summary>WO-160 3's QuestHostConverse: the host's player talks to <paramref name="npc"/> (true) or stopped (false).</summary>
    private void W164NoteHostConverse(string npc, bool on)
    {
        if (on) _w164HostTalks[npc] = true; else _w164HostTalks.TryRemove(npc, out _);
    }

    private void W164OnRunningDialogue(string line)
    {
        var m = W164Running.Match(line);
        if (!m.Success) return;
        string dlg = m.Groups[1].Value.Trim();
        int.TryParse(m.Groups[2].Value, out int id);
        string ex0 = m.Groups[3].Value, ex1 = m.Groups[4].Value, soul = m.Groups[5].Value;
        if (ex0 == "Dude" && _w164Talks.TryGetValue(ex1, out var t))
        {
            t.DialogId = id; t.Dialogue = dlg;
            return;
        }
        // the copy's own dialogue (a greeting, a bark) while this player's request to it is still open: T5's case
        if (ex0 != "Dude" && _w164Talks.TryGetValue(soul, out var t2) && t2.StartedAt < 0 && t2.PreemptedBy.Length == 0)
        {
            t2.PreemptedBy = dlg.Contains('.') ? dlg[(dlg.LastIndexOf('.') + 1)..] : dlg;
            Interlocked.Increment(ref _w164Preempted);
        }
    }

    private void W164OnTalkEvent(string[] f)
    {
        if (f.Length < 2 || !Wo137Text.IsNpc(f[1])) return;
        string npc = f[1];
        double now = W164Now();
        switch (f[0])
        {
            case "ask":
            {
                var t = new W164Talk { Npc = npc, AskedAt = now, Via = f.Length > 2 ? f[2] : "?", PausedBefore = f.Length > 3 && f[3] == "1" };
                t.Err10s = W164ErrorsBetween(npc, now - 10, now);
                t.OpenRequests = 1 + _w164Talks.Count(kv => kv.Value.StartedAt < 0);
                double oldest = _w164Talks.Values.Where(x => x.StartedAt < 0).Select(x => now - x.AskedAt).DefaultIfEmpty(0).Max();
                t.HostBusy = _w164HostTalks.ContainsKey(npc);
                t.Placed = _w141HostRows.TryGetValue(npc, out var a) ? (a.IsNone ? "none" : a.ToString()) : "no-row";
                t.QuestMismatch = _w164Mism.Count;
                t.FirstMismatch = _w164Mism.Keys.FirstOrDefault() is string fm ? fm[(fm.LastIndexOf('.') + 1)..] : "-";
                _w164Talks[npc] = t;
                Interlocked.Increment(ref _w164TalkAsks);
                Console.WriteLine(FormattableString.Invariant(
                    $"WO164-TALK npc={npc} kind=talk via={t.Via} placed={t.Placed.Replace(' ', '_')} paused_before={(t.PausedBefore ? 1 : 0)} planner_err_10s={t.Err10s} open_requests={t.OpenRequests} oldest_request_s={oldest:F1} quest_mismatch={t.QuestMismatch} first_mismatch={t.FirstMismatch} host_busy={(t.HostBusy ? 1 : 0)}"));
                // T6 (2): before a talk, every current no-port mismatch is corrected first (the young ones too)
                if (t.QuestMismatch > 0 && W137Joiner) _ = W164QuestFixAllAsync("before-talk", 0);
                return;
            }
            case "start":
                if (_w164Talks.TryGetValue(npc, out var ts) && ts.StartedAt < 0)
                {
                    ts.StartedAt = now;
                    Interlocked.Increment(ref _w164TalkStarts);
                }
                return;
            case "retry":
                if (_w164Talks.TryGetValue(npc, out var tr)) tr.Retry = string.Join(',', f.Skip(2));
                return;
            case "end":
            {
                if (!_w164Talks.TryRemove(npc, out var te)) return;
                string why = f.Length > 2 ? f[2] : "?";
                bool ok = why == "dialog-ended" || te.StartedAt >= 0;
                int cmds = te.DialogId >= 0 ? _w164DialogCmds.GetValueOrDefault(te.DialogId) : 0;
                if (te.DialogId >= 0) _w164DialogCmds.TryRemove(te.DialogId, out _);
                string kind = Wo164Rules.TalkKind(te.Dialogue);
                string cause = ok ? "-" : Wo164Rules.FailCause(te.HostBusy || _w164HostTalks.ContainsKey(npc), te.PreemptedBy, te.QuestMismatch, te.Err10s, te.OpenRequests);
                if (!ok) Interlocked.Increment(ref _w164TalkFails);
                string started = te.StartedAt >= 0 ? ((int)((te.StartedAt - te.AskedAt) * 1000)).ToString(CultureInfo.InvariantCulture) : "never";
                string line = FormattableString.Invariant(
                    $"WO164-TALK npc={npc} kind={kind} -> started_ms={started} | ended why={why} commands={cmds} cause={cause} preempted_by={(te.PreemptedBy.Length > 0 ? te.PreemptedBy : "-")} quest_mismatch={te.QuestMismatch} first_mismatch={te.FirstMismatch} planner_err_during={W164ErrorsBetween(npc, te.AskedAt, now)} retry={(te.Retry.Length > 0 ? te.Retry : "-")} dialogue={(te.Dialogue.Length > 0 ? te.Dialogue : "-")}");
                Console.WriteLine(line);
                _w164LastTalks.Enqueue($"{npc} {kind} {(ok ? "ok" : "failed:" + cause)} started_ms={started}");
                while (_w164LastTalks.Count > 5) _w164LastTalks.TryDequeue(out _);
                return;
            }
        }
    }

    // ================================================================ T1: the sweep (the Lua released the body; the DLL neutralises)

    private long _w164SweepReports;

    private async Task W164OnSweepEventAsync(string[] f)
    {
        if (f.Length < 2 || !Wo137Text.IsNpc(f[0])) return;
        string npc = f[0], trigger = f[1];
        int hold = f.Length > 2 && int.TryParse(f[2], out int h) ? h : 0;
        double t0 = W164Now();
        _w164SweptAt[npc] = t0;
        var r = await _combat.Wo141SweepAsync(npc, hold);
        Interlocked.Increment(ref _w164Sweeps);
        if (r is { Found: true }) Interlocked.Increment(ref _w164SweepFound);
        int before = W164ErrorsBetween(npc, t0 - 10, t0);
        if (Interlocked.Increment(ref _w164SweepReports) > 400) { Interlocked.Decrement(ref _w164SweepReports); return; }
        try
        {
            await Task.Delay(Wo164Rules.SweepReportMs);
            int after = W164ErrorsBetween(npc, t0 + 0.05, W164Now());
            if (after > 0) Interlocked.Increment(ref _w164SweepStillErr);
            Console.WriteLine(FormattableString.Invariant(
                $"WO164-SWEEP npc={npc} op=release+neutral trigger={trigger} hold_s={hold} native={(r is null ? "no-answer" : r.Value.Found ? "ok" : "not-found")} refusals_before={(r?.Refusals ?? 0)} errors_before={before} errors_after_6s={after}"));
        }
        finally { Interlocked.Decrement(ref _w164SweepReports); }
    }

    private long _w164Sweeps, _w164SweepFound, _w164SweepStillErr;

    // ================================================================ T6: quest values without a port

    private readonly ConcurrentDictionary<string, (int Host, int Local, double FirstAt, double SeenAt)> _w164Mism = new(StringComparer.Ordinal);
    private long _w164QFixOk, _w164QFixRefused;

    /// <summary>Wo137CompareCheckpointAsync: a mismatch no port corrects (null port).</summary>
    private void W164NoteMismatch(string path, int host, int local)
    {
        double now = W164Now();
        _w164Mism.AddOrUpdate(path, _ => (host, local, now, now), (_, o) => o.Host == host ? (host, local, o.FirstAt, now) : (host, local, now, now));
    }

    /// <summary>A checkpoint that compared <paramref name="path"/> equal: the mismatch is over.</summary>
    private void W164NoteMatch(string path) => _w164Mism.TryRemove(path, out _);

    private async Task W164QuestFixAllAsync(string why, double minAgeS)
    {
        if (!W137Joiner) return;
        double now = W164Now();
        foreach (var (path, m) in _w164Mism.ToArray())
        {
            if (now - m.SeenAt > 90) { _w164Mism.TryRemove(path, out _); continue; }   // no checkpoint lists it any more
            double age = now - m.FirstAt;
            if (age < minAgeS) continue;
            // the type is the DLL's to judge (it writes an int or bool State only and says type-refused otherwise); before a talk
            // (minAgeS 0) a young mismatch is corrected at once
            double judgedAge = minAgeS <= 0 ? Math.Max(age, Wo164Rules.QuestFixAfterS) : age;
            string? refusal = Wo164Rules.QuestFixRefusal(true, "int", judgedAge, _w154Contest.IsContested(path), false);
            if (refusal is not null)
            {
                Interlocked.Increment(ref _w164QFixRefused);
                Console.WriteLine($"WO164-QFIX var={path} joiner={m.Local} host={m.Host} applied=no why={refusal}");
                continue;
            }
            var r = await _combat.Wo137SetValueAsync(Interlocked.Increment(ref _w137Tok), path, m.Host);
            bool ok = r is { Result: 0 or 1 };
            if (ok) Interlocked.Increment(ref _w164QFixOk); else Interlocked.Increment(ref _w164QFixRefused);
            Console.WriteLine(FormattableString.Invariant(
                $"WO164-QFIX var={path} joiner={m.Local} host={m.Host} applied={(ok ? "yes" : "no")} why={(r is null ? "no-answer" : Wo137Rules.AppliedName(r.Value.Result))} type={(r?.Type ?? "?")} age_s={age:F0} trigger={why}"));
            if (ok) _w164Mism.TryRemove(path, out _);
        }
    }

    // ================================================================ M: the mark snapshot

    private readonly Random _w164Rnd = new();
    private long _w164Marks, _w164MarkPingsIn;

    private async Task W164MarkAsync(string markId, string origin)
    {
        Interlocked.Increment(ref _w164Marks);
        var lines = new List<string>();
        string role = W137Host ? "host" : W137Joiner ? "joiner" : _sharedWorld ? "shared-world-no-session" : "solo";
        lines.Add($"origin={origin} role={role} build={ReleaseVersionInfo.Current} peers={Wo134Peers().Count}");
        double now = W164Now();
        var open = _w164Talks.Values.Where(t => t.StartedAt < 0).ToList();
        lines.Add(FormattableString.Invariant($"talk open_requests={open.Count} oldest_s={(open.Count > 0 ? open.Max(t => now - t.AskedAt) : 0):F1} targets={(open.Count > 0 ? string.Join(",", open.Select(t => t.Npc)) : "-")} asks={_w164TalkAsks} starts={_w164TalkStarts} fails={_w164TalkFails} preempted={_w164Preempted}"));
        foreach (var l in _w164LastTalks) lines.Add("talk last " + l);
        lines.Add($"sweeps={_w164Sweeps} found={_w164SweepFound} still_erroring={_w164SweepStillErr}");
        foreach (var (npc, list) in _w164Err.ToArray()
                     .Select(kv => (kv.Key, n: W164ErrorsBetween(kv.Key, now - 30, now))).Where(x => x.n > 0).OrderByDescending(x => x.n).Take(5))
            lines.Add($"planner_err_30s npc={npc} n={list} placed={(_w141HostRows.TryGetValue(npc, out var pa) ? pa.ToString().Replace(' ', '_') : "no-row")}");
        foreach (var (peer, row) in _w141PeerRows.ToArray())
        {
            if (peer == _myGhostId) continue;
            var body = await _combat.Wo141ReadAsync(Wo141Rules.AvatarName(peer));
            lines.Add($"avatar peer={peer} wanted={row.ToString().Replace(' ', '_')} body={(body is ActivityState b ? b.ToString().Replace(' ', '_') : "unread")}");
        }
        foreach (var (name, e) in _w132Engaged.ToArray()) lines.Add(FormattableString.Invariant($"combat engaged(w132) npc={name} age_s={(DateTime.UtcNow - e.At).TotalSeconds:F0}"));
        foreach (var (name, e) in _w147Local.ToArray()) lines.Add(FormattableString.Invariant($"combat engaged(w147) npc={name} seen_s={(Environment.TickCount64 - e.SeenMs) / 1000.0:F0} rel={e.Rel:F1}"));
        foreach (var (path, m) in _w164Mism.ToArray().Take(5)) lines.Add(FormattableString.Invariant($"quest_mismatch var={path} joiner={m.Local} host={m.Host} age_s={now - m.FirstAt:F0}"));
        string? st = await _combat.Wo151StatusAsync();
        if (st is not null) { int i = st.IndexOf("markers=", StringComparison.Ordinal); lines.Add("map " + (i >= 0 ? st[i..] : "markers=?")); }
        string? w141 = await _combat.Wo141StatusAsync();
        if (w141 is not null) { int i = w141.IndexOf("applies", StringComparison.Ordinal); lines.Add("activities " + (i >= 0 ? w141[i..] : w141)); }
        lines.Add($"random_event_lines={_w164RandomEvents} flee_backoffs={_w164FleeDisengaged} sit_cleared={_w164SitCleared}");
        foreach (var l in Wo164Rules.SnapBlock(markId, "agent", lines)) Console.WriteLine(l);
        await ExecLuaAsync($"if KCD2MP_W164MarkSnap then KCD2MP_W164MarkSnap(\"{markId}\", \"{origin}\") end");
        if (origin == "local")
        {
            if (W137Host) foreach (byte j in Wo134Peers()) await W164SendAsync(j, Protocol.W164MarkPing, markId);
            else if (W137JoinerSession) await W164SendAsync(Protocol.JoinTargetHost, Protocol.W164MarkPing, markId);
        }
    }

    private async Task W164SendAsync(byte target, byte kind, string text)
    {
        try { await WriteJoinAsync(new LootMsg(kind, Interlocked.Increment(ref _w137Tok), text).BuildUp(Protocol.W164Up, target)); }
        catch (Exception ex) { Console.WriteLine($"WO164 {Protocol.W164KindName(kind)} not sent to {target}: {ex.Message}"); }
    }

    private long _w164Malformed;

    private async Task Wo164OnFrameAsync(byte src, byte[] body)
    {
        if (!LootMsg.TryDecode(body, out var m)) { Interlocked.Increment(ref _w164Malformed); return; }
        switch (m.Kind)
        {
            case Protocol.W164MarkPing when W164Text.IsMarkId(m.Text):
                Interlocked.Increment(ref _w164MarkPingsIn);
                Console.WriteLine($"MP-MARK ping from player {src}: mark={m.Text} -- this machine's block follows");
                await W164MarkAsync(m.Text, $"peer{src}");
                // the host passes a joiner's ping on to the other joiners
                if (W137Host) foreach (byte j in Wo134Peers()) if (j != src) await W164SendAsync(j, Protocol.W164MarkPing, m.Text);
                return;
            case Protocol.W164Torch when W164Text.TryParseTorch(m.Text, out byte ghost, out bool on):
                if (W137Host && ghost != src) { Interlocked.Increment(ref _w164Malformed); return; }   // a joiner speaks for itself
                await W164TorchInAsync(ghost, on, "side-channel");
                if (W137Host) foreach (byte j in Wo134Peers()) if (j != src) await W164SendAsync(j, Protocol.W164Torch, m.Text);
                return;
            default:
                Interlocked.Increment(ref _w164Malformed);
                return;
        }
    }

    // ================================================================ TR: the torch side-channel

    private readonly ConcurrentDictionary<byte, bool> _w164TorchShown = new();
    private long _w164TorchSentMs;
    private bool _w164TorchSentOn;
    private long _w164TorchSide;

    /// <summary>The state block's own edge was applied (Wo136OnPeerState2): remembered so the side-channel does not apply it twice.</summary>
    private void W164NoteTorchShown(byte ghost, bool on) => _w164TorchShown[ghost] = on;

    private async Task W164TorchInAsync(byte ghost, bool on, string via)
    {
        bool? shown = _w164TorchShown.TryGetValue(ghost, out bool s) ? s : null;
        if (Wo164Rules.TorchSideEdge(shown, on) is not bool edge) return;
        _w164TorchShown[ghost] = edge;
        Interlocked.Increment(ref _w164TorchSide);
        Console.WriteLine($"MP-W136 peer {ghost} torch {(edge ? "OUT" : "away")} -> its avatar {(edge ? "holds and lights the game's torch" : "puts it away")} ({via})");
        await ExecLuaAsync($"if KCD2MP_W136AvatarTorch then KCD2MP_W136AvatarTorch(\"{ghost}\", {B(edge)}) end");
    }

    private async Task W164TorchOutTickAsync(bool edge)
    {
        if (!(W137Host || W137JoinerSession)) return;
        long now = Environment.TickCount64;
        bool on = _w136TorchLocal;
        if (!edge && !(on && now - _w164TorchSentMs >= Protocol.W164TorchRepeatMs)) return;
        if (!edge && on == _w164TorchSentOn && !on) return;
        _w164TorchSentMs = now; _w164TorchSentOn = on;
        string text = W164Text.Torch(_myGhostId, on);
        if (W137Host) foreach (byte j in Wo134Peers()) await W164SendAsync(j, Protocol.W164Torch, text);
        else await W164SendAsync(Protocol.JoinTargetHost, Protocol.W164Torch, text);
    }

    // ================================================================ D1 / D2: fleeing enemies

    private readonly ConcurrentDictionary<ushort, string> _w164UnstName = new();
    private readonly ConcurrentDictionary<string, double> _w164FleeSince = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, double> _w164HoldSince = new(StringComparer.Ordinal);
    private long _w164FleeDisengaged;

    private async Task<string?> W164UnstanceNameAsync(ushort id)
    {
        if (id == ActivityState.NoUnstance) return null;
        if (_w164UnstName.TryGetValue(id, out var n)) return n;
        string? name = await _combat.Wo141UnstanceNameAsync(id);
        _w164UnstName[id] = name ?? "";
        return name;
    }

    private async Task W164FleeTickAsync()
    {
        if (!W141Joiner) { _w164FleeSince.Clear(); _w164HoldSince.Clear(); return; }
        double now = W164Now();
        foreach (var (name, a) in _w141HostRows.ToArray())
        {
            bool flee = Wo164Rules.IsFleeUnstance(await W164UnstanceNameAsync(a.Unstance));
            bool was = _w164FleeSince.ContainsKey(name);
            if (flee && !was) { _w164FleeSince[name] = now; await ExecLuaAsync($"if KCD2MP_W164Flee then KCD2MP_W164Flee(\"{EscapeLua(name)}\", true) end"); }
            else if (!flee && was) { _w164FleeSince.TryRemove(name, out _); await ExecLuaAsync($"if KCD2MP_W164Flee then KCD2MP_W164Flee(\"{EscapeLua(name)}\", false) end"); }
        }
        // D2: every hold the mod made (an engagement of WO-132 or WO-147) that a fleeing enemy outlived
        var held = _w132Engaged.Keys.Concat(_w147Local.Keys).Distinct().ToList();
        foreach (var name in _w164HoldSince.Keys) if (!held.Contains(name)) _w164HoldSince.TryRemove(name, out _);
        foreach (var name in held)
        {
            double since = _w164HoldSince.GetOrAdd(name, now);
            bool flee = _w164FleeSince.TryGetValue(name, out double fs);
            double fleeFor = flee ? now - fs : 0;   // a fleeing copy deals no blow: the flee's length stands in for "no blow"
            if (!Wo164Rules.WatchdogRelease(now - since, fleeFor, flee, 0)) continue;
            Interlocked.Increment(ref _w164FleeDisengaged);
            if (_w147Local.ContainsKey(name)) await Wo147ReleaseAsync(name, "WO-164: it fled");
            if (_w132Engaged.TryRemove(name, out var e)) { Interlocked.Increment(ref _w132EngageOff); await _combat.Wo132EngageAsync(false, e.Eid, default); }
            _w164HoldSince.TryRemove(name, out _);
            Console.WriteLine(FormattableString.Invariant($"WO164-FLEE disengage npc={name} age={now - since:F0} why=host-flee-{fleeFor:F0}s -- the mod's hold on a fleeing enemy is let go"));
        }
    }

    // ================================================================ S2 / S4: the avatars' and this player's seats

    private readonly ConcurrentDictionary<byte, double> _w164PeerUnseatedAt = new();
    private long _w164SitCleared;
    private ActivityState _w164MineWas = ActivityState.None;

    /// <summary>A peer's own activity row arrived (WO-141): when it leaves a seat or bed the time is kept for the stale check.</summary>
    private void W164OnPeerRow(byte peer, ActivityState a)
    {
        bool seated = Wo141Rules.ForAvatar(a).OwnsPosition && a.Stance is 2 or 3;
        if (seated) _w164PeerUnseatedAt.TryRemove(peer, out _);
        else _w164PeerUnseatedAt.TryAdd(peer, W164Now());
    }

    private async Task W164SitTickAsync()
    {
        double now = W164Now();
        foreach (var (peer, at) in _w164PeerUnseatedAt.ToArray())
        {
            if (now - at < Wo164Rules.StaleSitS) continue;
            var body = await _combat.Wo141ReadAsync(Wo141Rules.AvatarName(peer));
            bool bodySeated = body is ActivityState b && b.Stance is 2 or 3;
            if (Wo164Rules.StaleSit(bodySeated, false, now - at)) await W164ClearAvatarSeatAsync(peer, "stale");
            _w164PeerUnseatedAt.TryRemove(peer, out _);   // one look per change of the stream
        }
    }

    private async Task W164ClearAvatarSeatAsync(byte peer, string why)
    {
        string av = Wo141Rules.AvatarName(peer);
        await _combat.Wo141LeaveAsync(av);
        await ExecLuaAsync($"if KCD2MP_W160Release then KCD2MP_W160Release(\"{av}\", \"w164-{why}\") end");
        Interlocked.Increment(ref _w164SitCleared);
        Console.WriteLine($"WO164-SIT cleared why={why} body={av} -- stance and unstance reset, the stream takes the figure");
    }

    /// <summary>This player's own activity changed (the DLL's capture): a seat, a bed, a work station or a minigame entered or left.</summary>
    private async Task W164OnLocalActivityAsync(ActivityState now)
    {
        var was = _w164MineWas;
        _w164MineWas = now;
        bool seatWas = was.Stance is 2 or 3 && was.StanceObj != 0, seatNow = now.Stance is 2 or 3 && now.StanceObj != 0;
        string t = W164I(W164Now());
        if (seatWas != seatNow || (seatNow && was.StanceObj != now.StanceObj))
            Console.WriteLine($"WO164-SITSTATE {(seatNow ? "enter" : "leave")} obj={(seatNow ? now.StanceObj : was.StanceObj):X16} stance={(seatNow ? now.Stance : was.Stance)} t={t}");
        if (was.Unstance != now.Unstance)
        {
            if (was.Unstance != ActivityState.NoUnstance) Console.WriteLine($"WO164-USE leave obj={was.UnstanceObj:X16} kind={await W164UnstanceNameAsync(was.Unstance) ?? was.Unstance.ToString(CultureInfo.InvariantCulture)} result=- t={t}");
            if (now.Unstance != ActivityState.NoUnstance) Console.WriteLine($"WO164-USE enter obj={now.UnstanceObj:X16} kind={await W164UnstanceNameAsync(now.Unstance) ?? now.Unstance.ToString(CultureInfo.InvariantCulture)} result=- t={t}");
        }
        if (was.Minigame != now.Minigame)
            Console.WriteLine($"WO164-USE {(now.Minigame != ActivityState.NoMinigame ? "enter" : "leave")} obj={(now.Minigame != ActivityState.NoMinigame ? now.MinigameObj : was.MinigameObj):X16} kind=minigame-{(now.Minigame != ActivityState.NoMinigame ? now.Minigame : was.Minigame)} result=- t={t}");
    }

    // ================================================================ RL / C1: what the player is told

    private bool _w164MarkersOffTold;

    private Task W164NoticeAsync(string text) =>
        ExecLuaAsync($"if KCD2MP_W164Notice then KCD2MP_W164Notice(\"{EscapeLua(text)}\") end");

    // ================================================================ the events, the tick

    private void Wo164OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w164_talk": W164OnTalkEvent(f); return;
            case "w164_sweep": _ = W164OnSweepEventAsync(f); return;
            case "w164_unstuck2":   // S3 step 2: this player beside the partner (the leash's own placement)
                _ = W164UnstuckBesideAsync();
                return;
            case "w164_cfg":
                if (f.Length >= 1 && f[0] is "on" or "off") _w164On = f[0] == "on";
                Console.WriteLine($"WO164-CFG agent half {(_w164On ? "on" : "off")}");
                return;
        }
    }

    private async Task W164UnstuckBesideAsync()
    {
        byte pick = W137Joiner ? (byte)0 : _ghostLastPos.Keys.OrderBy(k => k).FirstOrDefault();
        if (!_ghostLastPos.TryGetValue(pick, out var gp) || (DateTime.UtcNow - gp.AtUtc).TotalSeconds > 10)
        { Console.WriteLine("WO164-UNSTUCK step=2 stood=- moved=no why=no-partner-position"); await W164NoticeAsync("No partner to bring you to."); return; }
        var pr = await _combat.JoinPlaceAsync(gp.X, gp.Y, gp.Z, 2.0f);
        bool ok = pr is { Ok: true } || pr is { Snapped: true };
        Console.WriteLine(FormattableString.Invariant($"WO164-UNSTUCK step=2 stood=- moved={(ok ? "yes" : "no")} to=({gp.X:F1},{gp.Y:F1},{gp.Z:F1}) partner={pick}"));
        if (ok) await W164NoticeAsync("You are beside your partner.");
    }

    private long _w164TickN;

    /// <summary>Once a second (the WO-151 loop).</summary>
    private async Task Wo164TickAsync()
    {
        if (!_w164On) return;
        long n = ++_w164TickN;
        await W164TorchOutTickAsync(false);
        await W164FleeTickAsync();
        await W164SitTickAsync();
        if (n % 2 == 0 && _w164Mism.Count > 0) await W164QuestFixAllAsync("mismatch-10s", Wo164Rules.QuestFixAfterS);
        if (n % 10 == 0 && !_w164MarkersOffTold)
        {
            string? st = await _combat.Wo151StatusAsync();
            if (st is not null && st.Contains("markers=off", StringComparison.Ordinal))
            {
                _w164MarkersOffTold = true;
                Console.WriteLine("WO164-MAPMARK the DLL turned map markers off (a fault in the game's map add): told on screen once");
                await W164NoticeAsync("Map markers are off this session (a game error).");
            }
        }
        if (n % 60 == 0 && (_w164TalkAsks + _w164Sweeps + _w164QFixOk + _w164QFixRefused + _w164FleeDisengaged + _w164SitCleared + _w164TorchSide) > 0)
            Console.WriteLine($"MP-WO164-STATS talks={_w164TalkAsks} started={_w164TalkStarts} failed={_w164TalkFails} preempted={_w164Preempted} sweeps={_w164Sweeps} sweep_found={_w164SweepFound} sweep_still_erroring={_w164SweepStillErr} qfix_ok={_w164QFixOk} qfix_refused={_w164QFixRefused} flee_disengaged={_w164FleeDisengaged} sit_cleared={_w164SitCleared} torch_side={_w164TorchSide} marks={_w164Marks} mark_pings_in={_w164MarkPingsIn} malformed={_w164Malformed} random_event_lines={_w164RandomEvents}");
    }
}
