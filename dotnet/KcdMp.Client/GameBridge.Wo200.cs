// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-200: one world, one story -- actions, not results. The rules are Wo200Rules (Wo200.cs); the DLL's half is native/KCDMP/wo200.cpp.
//
//   I.1 s1_replay (joiner): the DLL's tap records every dialogue out-port pulse; one of this player's own conversations (the engine's
//       "New dialogue" line names him among two or more souls) goes to the host as an OutPortReplay. The State steps that pulse made here
//       (the DLL marks them: flag 128 + the closure's id) are not asked of the host while it runs the same pulse (WO200-SUPPRESS); a
//       refusal sends them as today's requests instead.
//   I.2 s1_replay (host): the replay runs on the host's own node (the DLL's op 73) behind any load or catch-up; its closure is marked, so
//       the DLL's node gate keeps the host's player out of it (no follow-on dialogue, monologue, focus camera, tutorial, track view).
//   I.3 s1_route: talker-only rewards, money and body effects (the DLL's gate on both machines); quest items go to both (qitem_mirror).
//   I.4 joiner_quiet_clocks: the joiner's clock fires are dropped by the DLL; each is compared for 30 s with what the host's mirror brings
//       (WO200-QUIET agreed | host-differs).
//   I.5 joiner_no_puppet_quests: the DLL's node gate (the switch only).
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w200On = new(StringComparer.Ordinal);
    private bool W200(string name) => _w200On.TryGetValue(name, out bool v) ? v : Wo200Rules.DefaultOn(name);

    private long _w200TickN;
    private uint _w200PollSeq;
    private Wo200QuestGraph? _w200Graph;
    private bool _w200GraphTried;
    private bool _w200Connected;

    // this player's conversations (from the engine's own log lines): dialogue id -> names, start, end (0 = running)
    private readonly ConcurrentDictionary<int, (string[] Names, long StartMs, long EndMs)> _w200Talks = new();

    private sealed class W200Capture
    {
        public uint Closure, Seq;
        public string Path = "", Port = "";
        public long AtMs;
        public string Fate = "pending";   // pending | replayed | local
        public string Ack = "";
        public long AckMs;
        public readonly List<QuestChange> Held = new();
        public IReadOnlyList<string> StaticStates = Array.Empty<string>();
    }
    private readonly ConcurrentDictionary<uint, W200Capture> _w200Caps = new();
    private readonly object _w200Lock = new();
    private readonly List<(QuestChange C, long AtMs)> _w200Parked = new();

    private sealed class W200Quiet { public string Clock = "", Port = ""; public long AtMs; public IReadOnlyList<string> States = Array.Empty<string>(); public readonly List<string> HostDid = new(); }
    private readonly List<W200Quiet> _w200Quiet = new();

    // the host's replay queue (behind a load or a catch-up)
    private readonly ConcurrentQueue<(byte Src, uint Tok, uint Closure, uint Seq, string Port, string Path, long AtMs)> _w200HostQueue = new();

    private long _w200Captured, _w200CapturedOwn, _w200ReplaysSent, _w200AckRan, _w200AckRefused, _w200Suppressed, _w200FellBack,
                 _w200QuietDrops, _w200QuietAgreed, _w200QuietDiffers, _w200ReplaysIn, _w200ReplaysRan, _w200ReplaysRefused, _w200Parks;
    private readonly ConcurrentDictionary<string, long> _w200Refusals = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _w200ClosureClasses = new(StringComparer.Ordinal);

    private static long W200Now() => Environment.TickCount64;

    // ---------------------------------------------------------------- switches and events

    private void Wo200OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w200_cfg":
                if (!Wo200Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W200 cfg refused: '{arg}'"); return; }
                _w200On[cfg] = on;
                Console.WriteLine($"MP-W200 cfg {cfg}={(on ? "on" : "off")}");
                _ = Wo200PushConfigAsync();
                return;
            case "w200_status":
                _ = Wo200StatusAsync();
                return;
            case "w200_hostvals":   // II.1 / II.2 (host)
                _ = Wo200OnHostValuesAsync(arg);
                return;
            case "w200_qitems":     // I.3 (host): its Henry's quest-item counts that changed
                _ = Wo200OnHostQItemsAsync(arg);
                return;
            case "w200_noise":      // Act IV (joiner): noise of his the host's world must hear
                _ = Wo200SendNoiseAsync(arg);
                return;
            case "w200_test":   // harness only (KCDMP_TEST_W200=1): read <path> | fire <path> <port> | dump <path> | drop <path>
                if (Environment.GetEnvironmentVariable("KCDMP_TEST_W200") != "1" && !(arg ?? "").StartsWith("read ", StringComparison.Ordinal)) return;
                _ = Wo200TestAsync(arg ?? "");
                return;
        }
    }

    private byte Wo200Role() => W137Host ? Wo200Rules.RoleHost : W137JoinerSession ? Wo200Rules.RoleJoiner : Wo200Rules.RoleNone;

    private async Task Wo200PushConfigAsync()
    {
        try
        {
            bool s1 = W200("s1_replay");
            byte role = Wo200Role();
            byte tap = Wo200Rules.TapBits(s1, W200("joiner_quiet_clocks"));
            byte gate = (byte)(Wo200Rules.GateBits(W200("joiner_no_puppet_quests"), s1, W200("s1_route")) | (W201("joiner_no_story_barks") ? 8 : 0));   // WO-201 B7: bit 8
            await _combat.Wo163Async(Protocol.Wo200OpConfig, [role, (byte)(W137Joiner ? 1 : 0), tap, gate, 1, Wo200ValueBits()]);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W200 config not pushed ({ex.GetType().Name})"); }
    }

    private void Wo200OnConnect(CancellationToken ct)
    {
        _w200Connected = true;
        _ = Wo200LoopAsync(ct);
    }

    private async Task Wo200LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _w200Connected)
        {
            try { await Task.Delay(250, ct); } catch { break; }
            try
            {
                long n = ++_w200TickN;
                if (n % 8 == 1) await Wo200PushConfigAsync();
                if (Wo200Role() != Wo200Rules.RoleNone) await Wo200PollAsync();
                if (n % 4 == 2) { await Wo200ParkTickAsync(); Wo200QuietTick(); await Wo200HostQueueTickAsync(); }
                if (n % 4 == 0) await Wo200ClockTickAsync();   // II.7: the host's clock every 2 s
                if (n % 12 == 6) await Wo200QItemsTickAsync();   // I.3: quest-item counts every 3 s
                if (n % 240 == 120) await Wo200StatusAsync();
            }
            catch (Exception ex) { Console.WriteLine($"MP-W200 loop: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private void Wo200OnDisconnect()
    {
        _w200Connected = false;
        _w200QPushed = false;   // I.3: the classes again at the next connection (a new game's script)
        if (_w200Slewing) { _w200Ratio = 15.0; _w200Slewing = false; _ = ExecLuaAsync("pcall(function() Calendar.SetWorldTimeRatio(15.0) end)"); }   // II.7: the game's own pace back
        _w200Talks.Clear();
        _w200Caps.Clear();
        lock (_w200DlgLock) { _w200DudeConv.Clear(); _w200DudeMono.Clear(); _w200DlgSent = (-1, -1); }
        lock (_w200Lock) { _w200Parked.Clear(); _w200Quiet.Clear(); }
        while (_w200HostQueue.TryDequeue(out _)) { }
    }

    private Wo200QuestGraph? Wo200Graph()
    {
        if (_w200Graph is not null || _w200GraphTried) return _w200Graph;
        _w200GraphTried = true;
        try { _w200Graph = Wo200QuestGraph.FromScriptsPak(QuestValueIndex.FindScriptsPak()); }
        catch (Exception ex) { Console.WriteLine($"MP-W200 quest graph not read ({ex.GetType().Name}: {ex.Message}) -- closures are the DLL's marks alone"); }
        if (_w200Graph is null) Console.WriteLine("MP-W200 Scripts.pak not found -- closures are the DLL's marks alone");
        return _w200Graph;
    }

    // ---------------------------------------------------------------- the engine's own lines: this player's conversations

    // I.6: the engine's start attempt of this player's conversation (A3's bound counts from it when joiner_scoped_twins is on)
    private long _w200AttemptMs, _w200Attempts, _w200AttemptsMulti;

    private void Wo200OnDudeAttempt(int souls)
    {
        Volatile.Write(ref _w200AttemptMs, Environment.TickCount64);
        Interlocked.Increment(ref _w200Attempts);
        if (souls >= 3) Interlocked.Increment(ref _w200AttemptsMulti);
    }

    // Act IV: this player's open dialogues by kind (a real conversation vs his own monologue or a bark), told to the game's script when it
    // changes -- a save waits for a conversation, never for Henry's stomach growl (the field's only wait)
    private readonly HashSet<int> _w200DudeConv = new(), _w200DudeMono = new();
    private readonly object _w200DlgLock = new();
    private (int Conv, int Mono) _w200DlgSent = (-1, -1);

    private void Wo200NoteDialogKinds(string line)
    {
        var d = Wo184Rules.Parse(line);
        if (d.Kind is not (Wo184Rules.DialogKind.Start or Wo184Rules.DialogKind.End or Wo184Rules.DialogKind.Interrupted or Wo184Rules.DialogKind.Cancel)) return;
        (int, int) now;
        lock (_w200DlgLock)
        {
            if (d.Kind == Wo184Rules.DialogKind.Start)
            {
                if (!d.Dude || d.Id < 0) return;
                if (d.Monolog || d.Bark) _w200DudeMono.Add(d.Id); else _w200DudeConv.Add(d.Id);
            }
            else if (d.Id >= 0) { _w200DudeConv.Remove(d.Id); _w200DudeMono.Remove(d.Id); }
            if (_w200DudeConv.Count + _w200DudeMono.Count > 64) { _w200DudeConv.Clear(); _w200DudeMono.Clear(); }
            now = (_w200DudeConv.Count, _w200DudeMono.Count);
            if (now == _w200DlgSent) return;
            _w200DlgSent = now;
        }
        _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W200DialogKinds then KCD2MP_W200DialogKinds({now.Item1}, {now.Item2}) end"));
    }

    private void Wo200OnEngineLine(string line)
    {
        Wo200NoteDialogKinds(line);
        if (Wo200Rules.TryParseTalkStart(line, out string[] names, out int id))
        {
            _w200Talks[id] = (names, W200Now(), 0);
            if (_w200Talks.Count > 64)
                foreach (var k in _w200Talks.Where(kv => W200Now() - kv.Value.StartMs > 600_000).Select(kv => kv.Key).ToList()) _w200Talks.TryRemove(k, out _);
            return;
        }
        if (Wo200Rules.TryParseTalkEnd(line, out int eid) && _w200Talks.TryGetValue(eid, out var t))
            _w200Talks[eid] = (t.Names, t.StartMs, W200Now());
    }

    /// <summary>A capture's node is one of this player's conversations, running or ended within 3 s.</summary>
    private bool Wo200IsOwnTalk(string path)
    {
        long now = W200Now();
        foreach (var t in _w200Talks.Values)
        {
            if (t.EndMs != 0 && now - t.EndMs > 3000) continue;
            if (now - t.StartMs > 900_000) continue;
            foreach (var n in t.Names) if (Wo200Rules.PathMatchesTalk(path, n)) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- the DLL's events (op 72)

    private async Task Wo200PollAsync()
    {
        for (int round = 0; round < 8; round++)
        {
            var a = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(a, _w200PollSeq);
            var r = await _combat.Wo163Async(Protocol.Wo200OpPoll, a);
            if (r is not { Ok: true } x || x.Payload.Length < 6) return;
            var b = x.Payload;
            uint last = BinaryPrimitives.ReadUInt32LittleEndian(b);
            bool more = b[4] != 0;
            int n = b[5], o = 6;
            for (int i = 0; i < n && o + 4 + 1 + 4 + 4 + 1 <= b.Length; i++)
            {
                uint seq = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)); o += 4;
                byte kind = b[o++];
                uint closure = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)); o += 4;
                uint age = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)); o += 4;
                int al = b[o++];
                if (o + al + 2 > b.Length) break;
                string sa = Encoding.ASCII.GetString(b, o, al); o += al;
                int bl = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)); o += 2;
                if (o + bl > b.Length) break;
                string sb = Encoding.ASCII.GetString(b, o, bl); o += bl;
                if (seq <= _w200PollSeq && _w200PollSeq != 0 && seq + 1000 > _w200PollSeq) continue;
                await Wo200OnDllEventAsync(kind, closure, age, sa, sb);
            }
            _w200PollSeq = last;
            if (!more) return;
        }
    }

    private async Task Wo200OnDllEventAsync(byte kind, uint closure, uint ageMs, string a, string b)
    {
        switch (kind)
        {
            case 1:   // a dialogue node's out-port pulsed here (b = the node's path, a = the port)
                Interlocked.Increment(ref _w200Captured);
                await Wo200OnCaptureAsync(closure, b, a);
                return;
            case 2:   // a clock's fire dropped (joiner)
                Interlocked.Increment(ref _w200QuietDrops);
                Wo200OnQuietDrop(b, a);
                return;
            case 3:   // the node gate refused a node
            {
                string key = a.Split(' ').Take(3).Aggregate((p, q) => p + " " + q);
                _w200Refusals.AddOrUpdate(key, 1, (_, v) => v + 1);
                Console.WriteLine($"MP-W200 gate: {b} not run here ({a}){(closure != 0 ? FormattableString.Invariant($" in closure {closure:X8}") : "")}");
                return;
            }
            case 4:   // a class a closure met, the first time
                _w200ClosureClasses[b] = a;
                Console.WriteLine($"MP-W200 closure {closure:X8} meets {b} ({a})");
                return;
            case 5:   // the host ran a replay (its own log)
                return;
        }
    }

    // ---------------------------------------------------------------- I.1 capture -> replay (joiner)

    private async Task Wo200OnCaptureAsync(uint closure, string path, string port)
    {
        if (_w200Caps.TryGetValue(closure, out var first))
        {
            // a continuation of a conversation already captured (its waiter finished: the departure, 48 s on): the same replay goes on
            if (first.Fate != "replayed" || (first.Ack.Length > 0 && Wo200Rules.FallsBack(first.Ack)) || !W137Joiner || !Wo137Text.IsPath(path) || !Wo137Text.IsPort(port)) return;
            uint seq = ++first.Seq;
            Interlocked.Increment(ref _w200ReplaysSent);
            Interlocked.Exchange(ref _w200LastOwnReplayMs, W200Now());   // I.3: his own quest-item changes stand meanwhile
            Console.WriteLine(FormattableString.Invariant($"MP-W200 joiner -> host replay {closure:X8} continues: {path} :: {port} (its scene ended here; the host finishes its own waiter)"));
            await Wo137SendAsync(Protocol.QuestAskUp, Protocol.JoinTargetHost, Protocol.QuestAskOutPortReplay, closure, Wo200Rules.ReplayText(closure, seq, port, path));
            return;
        }
        var cap = new W200Capture { Closure = closure, Seq = closure & 0x7FFFFFFF, Path = path, Port = port, AtMs = W200Now() };
        bool own = Wo200IsOwnTalk(path);
        bool send = W137Joiner && W200("s1_replay") && own && Wo137Text.IsPath(path) && Wo137Text.IsPort(port);
        var g = Wo200Graph();
        if (g is not null)
        {
            try { cap.StaticStates = g.Of(path, port).States; } catch (Exception ex) { Console.WriteLine($"MP-W200 closure of {path} {port} not read: {ex.GetType().Name}"); }
        }
        cap.Fate = send ? "replayed" : "local";
        _w200Caps[closure] = cap;
        if (_w200Caps.Count > 400)
            foreach (var k in _w200Caps.Where(kv => W200Now() - kv.Value.AtMs > (long)Wo200Rules.SuppressTtl.TotalMilliseconds).Select(kv => kv.Key).ToList())
                _w200Caps.TryRemove(k, out _);
        if (!send)
        {
            if (W137Joiner && own) Console.WriteLine($"MP-W200 joiner: his conversation's out-port {path} :: {port} ran here only (s1_replay off or not sendable)");
            return;
        }
        Interlocked.Increment(ref _w200CapturedOwn);
        Interlocked.Increment(ref _w200ReplaysSent);
        Interlocked.Exchange(ref _w200LastOwnReplayMs, W200Now());   // I.3: his own quest-item changes stand meanwhile
        string states = cap.StaticStates.Count == 0 ? "none read" : string.Join(",", cap.StaticStates.Take(8).Select(s => s[(s.LastIndexOf('.') + 1)..])) + (cap.StaticStates.Count > 8 ? FormattableString.Invariant($" +{cap.StaticStates.Count - 8}") : "");
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W200 joiner -> host replay {closure:X8}: {path} :: {port} -- this player's conversation; the host runs it on its own node (closure States: {states})"));
        await Wo137SendAsync(Protocol.QuestAskUp, Protocol.JoinTargetHost, Protocol.QuestAskOutPortReplay, closure, Wo200Rules.ReplayText(closure, cap.Seq, port, path));
    }

    /// <summary>
    /// The joiner's own State step made inside a closure (flag 128): suppressed when its conversation's replay went to the host, asked as
    /// today when it did not; parked a moment when the capture's fate is not known yet. True = not asked now.
    /// </summary>
    private bool Wo200HoldLocal(QuestChange c)
    {
        if (!c.InClosure || !W137Joiner) return false;
        if (_w200Caps.TryGetValue(c.Closure, out var cap))
        {
            if (cap.Fate == "replayed" && !(cap.Ack.Length > 0 && Wo200Rules.FallsBack(cap.Ack)))
            {
                lock (_w200Lock) cap.Held.Add(c);
                Interlocked.Increment(ref _w200Suppressed);
                Console.WriteLine(FormattableString.Invariant(
                    $"WO200-SUPPRESS joiner: {c.Path} {c.Port} {c.Old}->{c.New} not asked -- inside replay {c.Closure:X8} (the host runs {cap.Path} :: {cap.Port} itself){(cap.Ack.Length > 0 ? ", host: " + cap.Ack : "")}"));
                return true;
            }
            return false;
        }
        lock (_w200Lock) _w200Parked.Add((c, W200Now()));
        Interlocked.Increment(ref _w200Parks);
        return true;
    }

    /// <summary>Parked changes: their capture is known now (suppress or ask), or the wait ran out (ask, as today).</summary>
    private async Task Wo200ParkTickAsync()
    {
        List<QuestChange> ask = new();
        lock (_w200Lock)
        {
            long now = W200Now();
            for (int i = _w200Parked.Count - 1; i >= 0; i--)
            {
                var (c, at) = _w200Parked[i];
                bool known = _w200Caps.ContainsKey(c.Closure);
                if (!known && now - at < (long)Wo200Rules.ParkMax.TotalMilliseconds) continue;
                _w200Parked.RemoveAt(i);
                if (known && _w200Caps[c.Closure].Fate == "replayed") { _w200Caps[c.Closure].Held.Add(c); Interlocked.Increment(ref _w200Suppressed);
                    Console.WriteLine(FormattableString.Invariant($"WO200-SUPPRESS joiner: {c.Path} {c.Port} {c.Old}->{c.New} not asked -- inside replay {c.Closure:X8}")); }
                else ask.Insert(0, c);
            }
        }
        foreach (var c in ask) await Wo137SendRequestAsync(c with { Flags = (byte)(c.Flags & ~QuestChange.FClosure) });
    }

    /// <summary>The host's answer to a replay: ran (the held steps were redundant) or refused (they go out as today's requests).</summary>
    private async Task Wo200OnAckAsync(string text)
    {
        if (!Wo200Rules.TryParseAckText(text, out uint closure, out string verdict, out int rt, out string path)) return;
        if (!_w200Caps.TryGetValue(closure, out var cap)) { Console.WriteLine($"MP-W200 joiner: the host's answer {verdict} for an unknown replay {closure:X8} ({path})"); return; }
        cap.Ack = verdict; cap.AckMs = W200Now();
        List<QuestChange> held;
        lock (_w200Lock) { held = cap.Held.ToList(); if (Wo200Rules.FallsBack(verdict)) cap.Held.Clear(); }
        if (verdict == "ran")
        {
            Interlocked.Increment(ref _w200AckRan);
            Console.WriteLine(FormattableString.Invariant($"MP-W200 joiner: the host RAN replay {closure:X8} ({path} :: {cap.Port}) -- {held.Count} local step(s) of it were never asked"));
            return;
        }
        if (verdict == "queued") { Console.WriteLine(FormattableString.Invariant($"MP-W200 joiner: the host queued replay {closure:X8} (it is loading or catching up)")); return; }
        Interlocked.Increment(ref _w200AckRefused);
        Console.WriteLine(FormattableString.Invariant($"MP-W200 joiner: the host REFUSED replay {closure:X8} ({verdict}, runtime {rt}) -- its {held.Count} step(s) go out as requests (today's path)"));
        cap.Fate = "local";
        foreach (var c in held) { Interlocked.Increment(ref _w200FellBack); await Wo137SendRequestAsync(c with { Flags = (byte)(c.Flags & ~QuestChange.FClosure) }); }
    }

    // ---------------------------------------------------------------- I.2 the host's replay

    private void Wo200OnReplayAsk(byte src, uint tok, string text)
    {
        if (!Wo200Rules.TryParseReplayText(text, out uint closure, out uint seq, out string port, out string path))
        {
            Console.WriteLine($"MP-W200 host: a replay from ghost {src} refused (malformed)");
            return;
        }
        Interlocked.Increment(ref _w200ReplaysIn);
        if (!W200("s1_replay"))
        {
            _ = Wo137SendAsync(Protocol.QuestHostUp, src, Protocol.QuestHostReplayAck, tok, Wo200Rules.AckText(closure, "off", -1, path));
            Console.WriteLine($"MP-W200 host: replay {closure:X8} from ghost {src} ({path} :: {port}) refused -- s1_replay is off here");
            return;
        }
        _w200HostQueue.Enqueue((src, tok, closure, seq, port, path, W200Now()));
        _ = Wo200HostQueueTickAsync();
    }

    private int _w200HostBusy;
    private async Task Wo200HostQueueTickAsync()
    {
        if (Interlocked.Exchange(ref _w200HostBusy, 1) == 1) return;
        try
        {
            while (_w200HostQueue.TryPeek(out var q))
            {
                if (Wo136Holding || (_w151CatchUp && !_w151CaughtUp && W137JoinerSession))   // behind a load or a catch-up
                {
                    if (W200Now() - q.AtMs > 120_000)
                    {
                        _w200HostQueue.TryDequeue(out _);
                        _ = Wo137SendAsync(Protocol.QuestHostUp, q.Src, Protocol.QuestHostReplayAck, q.Tok, Wo200Rules.AckText(q.Closure, "refused-notloaded", -1, q.Path));
                    }
                    return;
                }
                _w200HostQueue.TryDequeue(out _);
                uint hc = Wo200Rules.HostClosure(q.Src, q.Closure);
                byte[] pb = Encoding.ASCII.GetBytes(q.Path), qb = Encoding.ASCII.GetBytes(q.Port);
                var a = new byte[4 + 2 + pb.Length + 1 + qb.Length];
                BinaryPrimitives.WriteUInt32LittleEndian(a, hc);
                BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(4), (ushort)pb.Length);
                pb.CopyTo(a, 6);
                a[6 + pb.Length] = (byte)qb.Length;
                qb.CopyTo(a, 7 + pb.Length);
                var r = await _combat.Wo163Async(Protocol.Wo200OpReplay, a);
                byte res = r is { Ok: true, Payload.Length: >= 2 } x ? x.Payload[0] : (byte)6;
                int rt = r is { Ok: true, Payload.Length: >= 2 } y ? y.Payload[1] : -1;
                string verdict = Wo200Rules.VerdictOf(res);
                if (verdict == "ran") Interlocked.Increment(ref _w200ReplaysRan); else Interlocked.Increment(ref _w200ReplaysRefused);
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-W200 host: replay {q.Closure:X8} from ghost {q.Src} ({q.Path} :: {q.Port}) -> {verdict} (closure {hc:X8}{(verdict == "ran" ? "; its consequences run in this world and reach every joiner as changes" : "; the joiner falls back to its requests")})"));
                await Wo137SendAsync(Protocol.QuestHostUp, q.Src, Protocol.QuestHostReplayAck, q.Tok, Wo200Rules.AckText(q.Closure, verdict, rt, q.Path));
            }
        }
        finally { Interlocked.Exchange(ref _w200HostBusy, 0); }
    }

    // ---------------------------------------------------------------- I.4 quiet clocks: the comparison

    private long _w201QuietNoState;   // WO-201 B12

    private void Wo200OnQuietDrop(string clock, string port)
    {
        var q = new W200Quiet { Clock = clock, Port = port, AtMs = W200Now() };
        var g = Wo200Graph();
        if (g is not null) { try { q.States = g.Of(clock, port).States; } catch { } }
        lock (_w200Lock) { _w200Quiet.Add(q); if (_w200Quiet.Count > 64) _w200Quiet.RemoveAt(0); }
    }

    /// <summary>A host change applied here: does it answer a dropped clock fire?</summary>
    private void Wo200NoteHostChange(QuestChange c)
    {
        lock (_w200Lock)
            foreach (var q in _w200Quiet)
                if (q.States.Contains(c.Path) && q.HostDid.Count < 12) q.HostDid.Add(FormattableString.Invariant($"{c.Path[(c.Path.LastIndexOf('.') + 1)..]} {c.Port} {c.Old}->{c.New}"));
    }

    private void Wo200QuietTick()
    {
        List<W200Quiet> due;
        lock (_w200Lock)
        {
            long now = W200Now();
            due = _w200Quiet.Where(q => now - q.AtMs >= (long)Wo200Rules.QuietCompare.TotalMilliseconds).ToList();
            foreach (var q in due) _w200Quiet.Remove(q);
        }
        foreach (var q in due)
        {
            // WO-201 B12: a pulse whose closure holds no State (a timer's OnRunning / OnStopped / OnExec) has nothing to compare: it is
            // reported as such, never as a difference (the field: 18 of its 22 host-differs)
            if (q.States.Count == 0)
            {
                Interlocked.Increment(ref _w201QuietNoState);
                Console.WriteLine($"WO200-QUIET no-state: {q.Clock} :: {q.Port} dropped here; its closure holds no State -- nothing to compare");
                continue;
            }
            if (q.HostDid.Count > 0)
            {
                Interlocked.Increment(ref _w200QuietAgreed);
                Console.WriteLine($"WO200-QUIET agreed: {q.Clock} :: {q.Port} dropped here; the host's world did it within 30 s ({string.Join("; ", q.HostDid)})");
            }
            else
            {
                Interlocked.Increment(ref _w200QuietDiffers);
                Console.WriteLine($"WO200-QUIET host-differs: {q.Clock} :: {q.Port} dropped here; nothing of its closure ({q.States.Count} States) came from the host in 30 s -- the host's clock did not do it (its world decides)");
            }
        }
    }

    // ---------------------------------------------------------------- status and the harness

    private async Task Wo200StatusAsync()
    {
        string? n = null;
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo200OpStatus, []);
            if (r is { Ok: true } x) n = Encoding.ASCII.GetString(x.Payload);
        }
        catch { }
        string sw = string.Join(' ', Wo200Rules.SwitchNames.Select(s => $"{s}={On(W200(s))}"));
        string refusals = string.Join(",", _w200Refusals.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key.Replace(' ', ':')}={k.Value}"));
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W200-STATS role={Wo200Role()} joined={On(W137Joiner)} {sw} captured={_w200Captured} own={_w200CapturedOwn} replays_sent={_w200ReplaysSent} ack_ran={_w200AckRan} ack_refused={_w200AckRefused} suppressed={_w200Suppressed} fell_back={_w200FellBack} parks={_w200Parks} quiet_drops={_w200QuietDrops} quiet_agreed={_w200QuietAgreed} quiet_differs={_w200QuietDiffers} quiet_no_state={Interlocked.Read(ref _w201QuietNoState)} replays_in={_w200ReplaysIn} replays_ran={_w200ReplaysRan} replays_refused={_w200ReplaysRefused} attempts={_w200Attempts} attempts_multi={_w200AttemptsMulti} twin_releases={Interlocked.Read(ref _w179TwinReleases)} refusals={refusals} closure_classes={_w200ClosureClasses.Count} {Wo200ValuesStats()} {Wo200ItemsStats()} | native: {n ?? "no answer"}"));
    }

    private async Task Wo200TestAsync(string arg)
    {
        var f = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 2 || !Wo137Text.IsPath(f[1])) return;
        byte[] pb = Encoding.ASCII.GetBytes(f[1]);
        try
        {
            switch (f[0])
            {
                case "clockset" when f.Length == 3 && byte.TryParse(f[2], out byte cv):
                {
                    var b = new byte[4 + pb.Length];
                    b[0] = 6;
                    BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1), (ushort)pb.Length);
                    pb.CopyTo(b, 3);
                    b[3 + pb.Length] = cv;
                    var r = await _combat.Wo163Async(Protocol.Wo200OpResearch, b);
                    Console.WriteLine($"MP-W200 test clockset {f[1]} {cv} -> {(r is { Ok: true, Payload.Length: >= 1 } x ? (x.Payload[0] == 1 ? "written" : "refused") : "no answer")}");
                    return;
                }
                case "hib":
                case "wake":
                {
                    var b = new byte[3 + pb.Length];
                    b[0] = (byte)(f[0] == "hib" ? 4 : 5);
                    BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1), (ushort)pb.Length);
                    pb.CopyTo(b, 3);
                    var r = await _combat.Wo163Async(Protocol.Wo200OpResearch, b);
                    Console.WriteLine($"MP-W200 test {f[0]} {f[1]} -> {(r is { Ok: true, Payload.Length: >= 4 } x ? $"found={x.Payload[0]} allowed={x.Payload[1]} ran={x.Payload[2]} runtime={x.Payload[3]}" : "no answer")}");
                    return;
                }
                case "read":
                {
                    var a = new byte[2 + pb.Length];
                    BinaryPrimitives.WriteUInt16LittleEndian(a, (ushort)pb.Length); pb.CopyTo(a, 2);
                    var r = await _combat.Wo163Async(Protocol.Wo200OpReadNode, a);
                    if (r is { Ok: true, Payload.Length: >= 8 } x)
                        Console.WriteLine(FormattableString.Invariant(
                            $"MP-W200 read {f[1]} found={x.Payload[0]} kind={x.Payload[1]} runtime={x.Payload[2]} value={BinaryPrimitives.ReadInt32LittleEndian(x.Payload.AsSpan(3))} class={Encoding.ASCII.GetString(x.Payload, 8, Math.Min(x.Payload[7], x.Payload.Length - 8))}"));
                    else Console.WriteLine($"MP-W200 read {f[1]} -> no answer");
                    return;
                }
                case "fire" when f.Length == 3 && Wo137Text.IsPort(f[2]):
                case "dump":
                case "drop":
                {
                    byte sub = f[0] switch { "drop" => 1, "dump" => 2, _ => 3 };
                    byte[] qb = f.Length == 3 ? Encoding.ASCII.GetBytes(f[2]) : Array.Empty<byte>();
                    var a = new byte[1 + 2 + pb.Length + (qb.Length > 0 ? 1 + qb.Length : 0)];
                    a[0] = sub;
                    BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(1), (ushort)pb.Length); pb.CopyTo(a, 3);
                    if (qb.Length > 0) { a[3 + pb.Length] = (byte)qb.Length; qb.CopyTo(a, 4 + pb.Length); }
                    var r = await _combat.Wo163Async(Protocol.Wo200OpResearch, a);
                    Console.WriteLine($"MP-W200 test {arg} -> {(r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0].ToString(CultureInfo.InvariantCulture) : "no answer")}");
                    return;
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"MP-W200 test {arg} failed: {ex.GetType().Name}"); }
    }
}
