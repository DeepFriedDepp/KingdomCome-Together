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
    private static readonly Regex W164New = new(@"^New dialogue '([^'(]+?)(?: / [^']*)? \(\w+ - Id: (\d+)\).*souls = 'Ex: ([^;']+); Ex: ([^;' ]+)", RegexOptions.Compiled);
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
        if (line.StartsWith("New dialogue '", StringComparison.Ordinal))
        {
            var nm = W164New.Match(line);   // the player's own talk: its dialogue's name and id (the T0 line's kind and commands)
            if (nm.Success && nm.Groups[3].Value.Trim() == "Dude" && _w164Talks.TryGetValue(nm.Groups[4].Value.Trim(), out var nt) && int.TryParse(nm.Groups[2].Value, out int nid))
            { nt.DialogId = nid; nt.Dialogue = nm.Groups[1].Value.Trim(); }
            return;
        }
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
            _ = ExecLuaAsync($"if KCD2MP_W164Preempted then KCD2MP_W164Preempted(\"{EscapeLua(soul)}\") end");   // T5: its own line goes
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
        QuestValueIndex? qidx = null;
        try { qidx = await Wo147QuestIndexAsync(); } catch { }
        foreach (var (path, m) in _w164Mism.ToArray())
        {
            if (now - m.SeenAt > 90) { _w164Mism.TryRemove(path, out _); continue; }   // no checkpoint lists it any more
            double age = now - m.FirstAt;
            if (age < minAgeS) continue;
            // the type is the DLL's to judge (it writes an int or bool State only and says type-refused otherwise); before a talk
            // (minAgeS 0) a young mismatch is corrected at once
            double judgedAge = minAgeS <= 0 ? Math.Max(age, Wo164Rules.QuestFixAfterS) : age;
            // WO-166 T2: the type is the quest's own State definition (Scripts.pak, TypeT) when the index knows it; the DLL still checks
            // the live variant (int / uint / bool only)
            string? typeDef = qidx?.StateType(path);
            string? refusal = Wo164Rules.QuestFixRefusal(true, typeDef ?? "int", judgedAge, _w154Contest.IsContested(path), false);
            if (refusal is not null)
            {
                Interlocked.Increment(ref _w164QFixRefused);
                Console.WriteLine($"WO164-QFIX var={path} joiner={m.Local} host={m.Host} applied=no why={refusal} type_def={typeDef ?? "?"}");
                continue;
            }
            var r = await _combat.Wo137SetValueAsync(Interlocked.Increment(ref _w137Tok), path, m.Host);
            bool ok = r is { } rr && Wo137Rules.WriteTook(rr.Result);
            if (ok) Interlocked.Increment(ref _w164QFixOk); else Interlocked.Increment(ref _w164QFixRefused);
            Console.WriteLine(FormattableString.Invariant(
                $"WO164-QFIX var={path} joiner={m.Local} host={m.Host} applied={(ok ? "yes" : "no")} why={(r is null ? "no-answer" : Wo137Rules.AppliedName(r.Value.Result))} type={(r?.Type ?? "?")} type_def={typeDef ?? "?"} age_s={age:F0} trigger={why}"));
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
            case Protocol.W164Escort when W164Text.TryParseEscort(m.Text, out string enpc, out bool eon):
                if (!W137Host) { Interlocked.Increment(ref _w164Malformed); return; }
                Interlocked.Increment(ref _w164EscortFollows);
                Console.WriteLine($"WO164-ESCORT npc={enpc} leader=player{src} follow={(eon ? "on" : "off")} -- the host's NPC {(eon ? "walks behind the joiner's figure" : "is its own again")}");
                await ExecLuaAsync($"if KCD2MP_W164EscortFollow then KCD2MP_W164EscortFollow(\"{EscapeLua(enpc)}\", {src}, {B(eon)}) end");
                return;
            case Protocol.W164Torch when W164Text.TryParseTorch(m.Text, out byte ghost, out bool on):
                if (W137Host && ghost != src) { Interlocked.Increment(ref _w164Malformed); return; }   // a joiner speaks for itself
                await W164TorchInAsync(ghost, on, "side-channel");
                if (W137Host) foreach (byte j in Wo134Peers()) if (j != src) await W164SendAsync(j, Protocol.W164Torch, m.Text);
                return;
            case Protocol.W164HitOutcome:   // WO-165: a joiner's engine decided the host's verdict
                if (!W137Host) { Interlocked.Increment(ref _w164Malformed); return; }
                Wo165OnOutcomeIn(src, m.Text);
                return;
            case Protocol.W164Weather when W164Text.TryParseWeather(m.Text, out float wrain, out string? wprof):   // WO-166 W1
                if (W137Host) { Interlocked.Increment(ref _w164Malformed); return; }   // only the host's weather is the session's
                await Wo166OnHostWeatherAsync(wrain, wprof);
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
    private readonly ConcurrentDictionary<string, (float X, float Y, long AtMs)> _w164NpcPos = new(StringComparer.Ordinal);   // the host's stream, at the read

    /// <summary>D2: the enemy's distance to the nearest player (this one, every fresh partner); +inf when its stream is older than 10 s.</summary>
    private double W164NearestPlayerM(string npc)
    {
        if (!_w164NpcPos.TryGetValue(npc, out var p) || Environment.TickCount64 - p.AtMs > 10_000) return double.PositiveInfinity;
        double best = Math.Sqrt((p.X - _lastX) * (p.X - _lastX) + (p.Y - _lastY) * (p.Y - _lastY));
        foreach (var (_, g) in _ghostLastPos.ToArray())
            if ((DateTime.UtcNow - g.AtUtc).TotalSeconds < 10) best = Math.Min(best, Math.Sqrt((p.X - g.X) * (p.X - g.X) + (p.Y - g.Y) * (p.Y - g.Y)));
        return best;
    }
    private long _w164FleeDisengaged;
    private readonly ConcurrentDictionary<string, double> _w164FarSince = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, long> _w164HostCombatTold = new(StringComparer.Ordinal);

    /// <summary>The host's NPC is in combat: told to the Lua at most once a second (its copy keeps its weapon).</summary>
    private void W164NoteHostCombat(string npc)
    {
        long now = Environment.TickCount64;
        if (_w164HostCombatTold.TryGetValue(npc, out long at) && now - at < 1000) return;
        _w164HostCombatTold[npc] = now;
        _ = ExecLuaAsync($"if KCD2MP_W164HostCombat then KCD2MP_W164HostCombat(\"{EscapeLua(npc)}\") end");
    }

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
            double far = W164NearestPlayerM(name);
            // no blow: a fleeing copy deals none (the flee's length), a far one none either (its distance held 20 s: the hold's age)
            double quiet = flee ? now - fs : far > Wo164Rules.WatchFarM ? now - _w164FarSince.GetOrAdd(name, now) : 0;
            if (far <= Wo164Rules.WatchFarM) _w164FarSince.TryRemove(name, out _);
            if (!Wo164Rules.WatchdogRelease(now - since, quiet, flee, far)) continue;
            double fleeFor = quiet;
            Interlocked.Increment(ref _w164FleeDisengaged);
            if (_w147Local.ContainsKey(name)) await Wo147ReleaseAsync(name, "WO-164: it fled");
            if (_w132Engaged.TryRemove(name, out var e)) { Interlocked.Increment(ref _w132EngageOff); await _combat.Wo132EngageAsync(false, e.Eid, default); }
            _w164HoldSince.TryRemove(name, out _);
            _w164FarSince.TryRemove(name, out _);
            Console.WriteLine(FormattableString.Invariant($"WO164-FLEE disengage npc={name} age={now - since:F0} why={(flee ? "host-flee" : "far")}-{fleeFor:F0}s nearest_player_m={(double.IsInfinity(far) ? "?" : far.ToString("F0"))} -- the mod's hold on a fleeing or distant enemy is let go"));
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
            case "w164_escort":   // ESC (joiner): <npc> 1|0 -- this NPC follows this player (the quest asked it); the host is told
                if (f.Length == 2 && W164Text.TryParseEscort(string.Join(' ', f), out string en, out bool eo) && W137Joiner)
                    _ = W164SendAsync(Protocol.JoinTargetHost, Protocol.W164Escort, W164Text.Escort(en, eo));
                return;
            case "w164_pin":   // C2: mp_partner_marker on|off
                if (f.Length >= 1 && f[0] is "on" or "off") _w164PinOn = f[0] == "on";
                Console.WriteLine($"WO164-CFG partner marker {(_w164PinOn ? "on" : "off")}");
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

    // ================================================================ ESC: a quest NPC that follows the joiner

    private readonly ConcurrentDictionary<(byte Peer, string Scope), double> _w164Escorts = new();
    private long _w164EscortAccepted, _w164EscortFollows;

    /// <summary>
    /// Host: a joiner's quest step the host's world would refuse (its own value is elsewhere) is applied when the joiner leads that
    /// quest's NPC -- a follow step starts the escort, its end step ends it (the 0.47.0 Mutt bait: every step of Ignatius' logic was
    /// refused because the host's own logic never started; the counter then drifted for 7 minutes).
    /// </summary>
    private bool W164EscortAccepts(byte src, QuestChange req, int hostVal, string head)
    {
        if (!_w164On) return false;
        double now = W164Now();
        foreach (var k in _w164Escorts.Keys) if (now - _w164Escorts[k] > Wo164Rules.EscortMaxS) _w164Escorts.TryRemove(k, out _);
        string? scope = Wo164Rules.EscortScope(req.Path);
        bool follow = Wo164Rules.IsFollowPort(req.Port);
        bool active = _w164Escorts.Keys.Any(k => k.Peer == src && Wo164Rules.InEscortScope(k.Scope, req.Path));
        if (!follow && !active) return false;
        if (follow && scope is not null) _w164Escorts[(src, scope)] = now;
        Interlocked.Increment(ref _w164EscortAccepted);
        Console.WriteLine(FormattableString.Invariant($"WO164-ESCORT logic={scope ?? req.Path} leader=player{src} accepted={req.Port} host_value={hostVal} -- {(follow ? "the joiner leads this quest's NPC: its steps are applied here" : "a step of the joiner's escort")}"));
        if (Wo164Rules.IsEscortEndPort(req.Port))
        {
            foreach (var k in _w164Escorts.Keys.Where(k => k.Peer == src && Wo164Rules.InEscortScope(k.Scope, req.Path)).ToList()) _w164Escorts.TryRemove(k, out _);
            _ = ExecLuaAsync($"if KCD2MP_W164EscortFollow then KCD2MP_W164EscortFollow(nil, {src}, false) end");
        }
        return true;
    }

    /// <summary>Joiner: its own step went to the host -- a follow step asks the Lua which NPC follows this player now.</summary>
    private void W164JoinerStepSent(string path, string port)
    {
        if (!_w164On) return;
        if (Wo164Rules.IsFollowPort(port)) _ = ExecLuaAsync("if KCD2MP_W164EscortFind then KCD2MP_W164EscortFind() end");
        else if (Wo164Rules.IsEscortEndPort(port)) _ = ExecLuaAsync("if KCD2MP_W164EscortEnd then KCD2MP_W164EscortEnd(\"quest-step\") end");
    }

    // ================================================================ C2: the partner's pin on the map

    private volatile bool _w164PinOn = true;   // mp_partner_marker (the Lua's switch, the mod menu's Display group)
    private readonly ConcurrentDictionary<byte, (float X, float Y)> _w164PinAt = new();
    private long _w164PinSets, _w164PinRemoves;

    /// <summary>Every 2 s: a pin at each partner in this world (moved when it moved more than 3 m); none when not in one session.</summary>
    private async Task W164PinTickAsync()
    {
        bool want = _w164PinOn && (W137Host || W137Joiner);
        var fresh = new HashSet<byte>();
        // WO-166 M1: only a live peer has a pin, and one per partner (a crash-rejoin's old id is gone from the relay's set at once)
        var keep = Wo166Rules.PinOwners(LivePartners(), _myGhostId, g => _ghostNames.TryGetValue(g, out var nm) ? nm : null,
                                        g => _ghostLastPos.TryGetValue(g, out var gp) ? gp.AtUtc : DateTime.MinValue, DateTime.UtcNow);
        if (want)
            foreach (var (g, p) in _ghostLastPos.ToArray())
            {
                if (g == _myGhostId || !keep.Contains(g) || (DateTime.UtcNow - p.AtUtc).TotalSeconds > 10) continue;
                fresh.Add(g);
                if (_w164PinAt.TryGetValue(g, out var was) && Wo164Rules.PinMoveDue(was.X, was.Y, p.X, p.Y) == false) continue;
                if (await _combat.MirrorGraveAsync(3, g, 0, p.X, p.Y, p.Z)) { _w164PinAt[g] = (p.X, p.Y); Interlocked.Increment(ref _w164PinSets); }
            }
        foreach (var g in _w164PinAt.Keys.ToArray())
        {
            if (fresh.Contains(g)) continue;
            _w164PinAt.TryRemove(g, out _);
            await _combat.MirrorGraveAsync(4, g, 0, 0, 0, 0);
            Interlocked.Increment(ref _w164PinRemoves);
            Console.WriteLine($"WO164-MAPMARK pin of player {g} removed ({(!_w164PinOn ? "mp_partner_marker off" : !(W137Host || W137Joiner) ? "not in a session" : !keep.Contains(g) ? "not a live partner (WO-166 M1)" : "no position for 10 s")})");
        }
    }

    private long _w164TickN;

    // The development harness only (tools/wo118, a synthetic host): KCDMP_TEST_JOINER_IN_WORLD=1 makes a non-authority agent a joiner
    // in the host's shared world without the join's own load. Never set by the launcher or the installer.
    private static readonly bool W164TestJoinerInWorld = Environment.GetEnvironmentVariable("KCDMP_TEST_JOINER_IN_WORLD") == "1";

    /// <summary>Once a second (the WO-151 loop).</summary>
    private async Task Wo164TickAsync()
    {
        if (W164TestJoinerInWorld && _combatRoleApplied && !_isDamageAuthority && !_joinedWorld)
        {
            _hostModeKnown = true; _hostSharedWorld = true;
            SetJoinedWorld(true);
            Console.WriteLine("WO164-TEST KCDMP_TEST_JOINER_IN_WORLD: this agent is a joiner in its host's shared world (the harness; no join load)");
        }
        if (!_w164On) return;
        long n = ++_w164TickN;
        await W164TorchOutTickAsync(false);
        await W164FleeTickAsync();
        await W164SitTickAsync();
        if (n % 2 == 0) await W164PinTickAsync();
        if (n % 10 == 0) await Wo166PushConfigAsync();   // WO-166: the DLL's copy-strike / snap-fix / automation switches
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
        if (n % 60 == 0 && Interlocked.Read(ref _w166LootCloseLines) + Interlocked.Read(ref _w166TalkTimeouts) + Interlocked.Read(ref _w166TalkBusy) > 0)
            Console.WriteLine("MP-WO166-STATS " + Wo166StatsText());
        if (n % 60 == 0 && (W137JoinerSession || W137Host)) { string? w166n = await _combat.Wo166StatusAsync(); if (w166n is not null && !w166n.Contains("requests=0 ", StringComparison.Ordinal)) Console.WriteLine("MP-WO166 native: " + w166n); }
        if (n % 60 == 0 && (_w164TalkAsks + _w164Sweeps + _w164QFixOk + _w164QFixRefused + _w164FleeDisengaged + _w164SitCleared + _w164TorchSide) > 0)
            Console.WriteLine($"MP-WO164-STATS talks={_w164TalkAsks} started={_w164TalkStarts} failed={_w164TalkFails} preempted={_w164Preempted} sweeps={_w164Sweeps} sweep_found={_w164SweepFound} sweep_still_erroring={_w164SweepStillErr} qfix_ok={_w164QFixOk} qfix_refused={_w164QFixRefused} flee_disengaged={_w164FleeDisengaged} sit_cleared={_w164SitCleared} torch_side={_w164TorchSide} pin_sets={_w164PinSets} pin_removes={_w164PinRemoves} escort_accepted={_w164EscortAccepted} escort_follows={_w164EscortFollows} marks={_w164Marks} mark_pings_in={_w164MarkPingsIn} malformed={_w164Malformed} random_event_lines={_w164RandomEvents}");
    }

    // ================================================================ ID: the host's idles on the joiner, per minute with reasons

    private readonly ConcurrentDictionary<string, long> _w164IdleWhy = new(StringComparer.Ordinal);
    private long _w164IdlePrevIn, _w164IdlePrevPlayed, _w164IdlePrevRefused, _w164IdleRefusedTotal;

    private void W164IdleRefused(string why)
    {
        Interlocked.Increment(ref _w164IdleRefusedTotal);
        _w164IdleWhy.AddOrUpdate(why, 1, (_, n) => n + 1);
    }

    /// <summary>The MP-W143 stats line's tail: one-shot (idle) rows received / played / refused since the last line, and why.</summary>
    private string W164IdleMinuteText()
    {
        long inN = Interlocked.Read(ref _w143ShotsIn), played = Interlocked.Read(ref _w143ShotsPlayed), refused = Interlocked.Read(ref _w164IdleRefusedTotal);
        string why = string.Join(",", _w164IdleWhy.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key}:{kv.Value}"));
        string t = $"idle_window in={inN - _w164IdlePrevIn} played={played - _w164IdlePrevPlayed} refused={refused - _w164IdlePrevRefused} refused_why=[{why}]";
        _w164IdlePrevIn = inN; _w164IdlePrevPlayed = played; _w164IdlePrevRefused = refused;
        _w164IdleWhy.Clear();
        return t;
    }
}
