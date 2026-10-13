// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;

namespace KcdMp.Client;

// WO-177 (docs/WO-177-findings.md): the bridge. The rules are Wo177Rules (Wo177.cs); the Lua half is kdcmp.lua's WO-177 block.
//
//   A5 no load while a fight is running: Wo177WaitQuietAsync is called before every load this agent issues (join, leave, exit-to-menu, the
//      repaired copy's load). One question a second to the Lua (KCD2MP_W177LoadQuiet -> event w177_load_quiet "<why> ok|fight|hit"); the
//      first "not quiet" answer is told to the player; three minutes of fight or hit = the load is not issued. WO-180: no world loaded (the main
//      menu) = go at once without asking; a script that does not answer for 10 s = go.
//        MP-W177 load why=<w> waiting reason=<fight|hit|no-answer>
//        MP-W177 load why=<w> go after <ms> ms            MP-W177 load why=<w> NOT issued: ...
//        MP-W177 load why=<w> go: no world loaded (no fight possible)      MP-W177 load why=<w> go after 10 s with no answer (the script did not reply)
public partial class GameBridge
{
    private TaskCompletionSource<string>? _w177Probe;
    private long _w177Waits, _w177Gaveup, _w177Unanswered;

    private long _w177CorpseAsks, _w177CorpseAnswers, _w177CorpseGone;
    private volatile bool _w177ToolSwing = Wo177Rules.DefaultToolSwing;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _w177Calm = new(StringComparer.Ordinal);
    private long _w177ToolSwings;

    /// <summary>B3: true = this hit on a figure is a working NPC's tool swing (the hammer at the anvil): no damage goes to the partner.</summary>
    private bool Wo177IsToolSwing(string attacker, bool missile)
    {
        if (attacker.Length == 0) return false;
        long since = _w177Calm.TryGetValue(attacker, out long t) ? t : 0;
        return Wo177Rules.IsToolSwing(_w177ToolSwing, since, Environment.TickCount64, missile);
    }

    private void Wo177OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w177_load_quiet":
            {
                string? reason = Wo177Rules.ParseReason(arg);
                if (reason is not null) _w177Probe?.TrySetResult(reason);
                return;
            }
            case "w177_status":
                Console.WriteLine(Wo177StatsText());
                return;
            case "w177_calm_cfg":     // WO-177 B3: mp_tool_swing
                _w177ToolSwing = arg is not null && arg.StartsWith("on", StringComparison.Ordinal);
                Console.WriteLine($"MP-W177 mp_tool_swing {(_w177ToolSwing ? "on" : "off")}");
                return;
            case "w177_calm":         // WO-177 B3 (host): the NPCs near a figure that are not in a fight
            {
                long now = Environment.TickCount64;
                var (calm, fighting) = Wo177Rules.ParseCalm(arg);
                foreach (string n in calm) _w177Calm[n] = now;
                foreach (string n in fighting) _w177Calm.TryRemove(n, out _);   // it turned to a fight: its blows count from now on
                return;
            }
            case "w177_corpse_ask":   // joiner: "<npc>:<x>:<y> ..." -> the host
                if (W137Joiner && W164Text.TryParseCorpseAsk(arg, out _))
                {
                    Interlocked.Increment(ref _w177CorpseAsks);
                    _ = W164SendAsync(Protocol.JoinTargetHost, Protocol.W164CorpseAsk, arg!);
                }
                return;
            case "w177_corpse_ans":   // host: "<peer> <npc=v ...>" -> that joiner
            {
                if (!W137Host || arg is null) return;
                int sp = arg.IndexOf(' ');
                if (sp <= 0 || !byte.TryParse(arg.AsSpan(0, sp), out byte peer)) return;
                string text = arg[(sp + 1)..];
                if (!W164Text.TryParseCorpseAnswer(text, out _)) return;
                Interlocked.Increment(ref _w177CorpseAnswers);
                _ = W164SendAsync(peer, Protocol.W164CorpseAnswer, text);
                return;
            }
            case "w177_corpse_gone":   // host: "<npc=2 ...>" -> every joiner
                if (W137Host && W164Text.TryParseCorpseAnswer(arg, out _))
                {
                    Interlocked.Increment(ref _w177CorpseGone);
                    foreach (byte j in Wo134Peers()) _ = W164SendAsync(j, Protocol.W164CorpseAnswer, arg!);
                }
                return;
        }
    }

    /// <summary>One question to the Lua; null = no answer in time (the game is busy or the script is not there).</summary>
    private async Task<string?> Wo177ProbeAsync(string why)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _w177Probe = tcs;
        try
        {
            await ExecLuaAsync($"if KCD2MP_W177LoadQuiet then KCD2MP_W177LoadQuiet(\"{EscapeLua(why)}\") else KCD2MP_EmitEvent(\"w177_load_quiet\", \"{EscapeLua(why)} ok\") end");
            var done = await Task.WhenAny(tcs.Task, Task.Delay(Wo177Rules.ProbeTimeoutMs));
            return done == tcs.Task ? tcs.Task.Result : null;
        }
        catch { return null; }
        finally { if (ReferenceEquals(_w177Probe, tcs)) _w177Probe = null; }
    }

    /// <summary>WO-180: the game is at its main menu (or between: no level is loaded), so no fight is possible and the Lua cannot be asked
    /// (a menu gate holds every push into the mod until a world loads).</summary>
    private bool Wo177NoWorldLoaded() => _where == GameWhere.Menu || (_where == GameWhere.Unknown && _startedAtMenu);

    /// <summary>true = nothing is running and the load may be issued; false = a fight never ended (the caller does not load).</summary>
    internal async Task<bool> Wo177WaitQuietAsync(string why)
    {
        var sw = Stopwatch.StartNew();
        var gate = new Wo177Rules.Gate();
        string? told = null;
        while (true)
        {
            bool noWorld = Wo177NoWorldLoaded();
            string? reason = noWorld ? null : await Wo177ProbeAsync(why);   // no world: the Lua is not asked
            switch (gate.Step(noWorld, reason, sw.ElapsedMilliseconds))
            {
                case Wo177Rules.Verdict.GoNoWorld:
                    Console.WriteLine($"MP-W177 load why={why} go: no world loaded (no fight possible)");
                    return true;
                case Wo177Rules.Verdict.GoNoAnswer:
                    Interlocked.Increment(ref _w177Unanswered);
                    Console.WriteLine($"MP-W177 load why={why} go after {Wo177Rules.NoAnswerGoMs / 1000} s with no answer (the script did not reply)");
                    return true;
                case Wo177Rules.Verdict.Go:
                    if (told is not null) Console.WriteLine($"MP-W177 load why={why} go after {sw.ElapsedMilliseconds} ms (it was waiting for: {told})");
                    return true;
            }
            string shown = reason ?? "no-answer";
            if (reason is null) Interlocked.Increment(ref _w177Unanswered);
            if (told is null)
            {
                told = shown;
                Interlocked.Increment(ref _w177Waits);
                Console.WriteLine($"MP-W177 load why={why} waiting reason={shown} -- no load while a fight is running or within {Wo177Rules.QuietAfterHitS:F0} s of a hit");
            }
            if (gate.HasRealAnswer && Wo177Rules.GaveUp(sw.ElapsedMilliseconds))   // a fight or a hit that never ended; a silent script goes after 10 s instead
            {
                Interlocked.Increment(ref _w177Gaveup);
                Console.WriteLine($"MP-W177 load why={why} NOT issued: still {shown} after {sw.ElapsedMilliseconds / 1000} s");
                try { await ExecLuaAsync($"if KCD2MP_Wo124Msg then KCD2MP_Wo124Msg(\"{EscapeLua(Wo177Rules.GaveUpText)}\") end"); } catch { }
                return false;
            }
            await Task.Delay(Wo177Rules.PollMs);
        }
    }

    // The development harness only (tools/wo118): KCDMP_TEST_W177_GATE=<why> asks the load gate every 15 s and prints the verdict and the
    // time it took, so the gate can be watched at the main menu and in a world without a join. Never set by the launcher or the installer.
    private static readonly string? W177TestGate = Environment.GetEnvironmentVariable("KCDMP_TEST_W177_GATE");
    private long _w177TestNext;
    private int _w177TestBusy;

    private Task Wo177TestTickAsync()
    {
        if (string.IsNullOrEmpty(W177TestGate) || Environment.TickCount64 < _w177TestNext || Interlocked.Exchange(ref _w177TestBusy, 1) == 1) return Task.CompletedTask;
        _ = Task.Run(async () =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                string where = _where.ToString();
                bool go = await Wo177WaitQuietAsync(W177TestGate);
                Console.WriteLine($"MP-W177-TEST gate why={W177TestGate} -> {(go ? "GO" : "NOT ISSUED")} after {sw.ElapsedMilliseconds} ms (the game was: {where})");
            }
            finally { _w177TestNext = Environment.TickCount64 + 15_000; Volatile.Write(ref _w177TestBusy, 0); }
        });
        return Task.CompletedTask;
    }

    internal string Wo177StatsText() => FormattableString.Invariant(
        $"MP-W177-STATS load_waits={Interlocked.Read(ref _w177Waits)} load_gaveup={Interlocked.Read(ref _w177Gaveup)} probe_unanswered={Interlocked.Read(ref _w177Unanswered)} corpse_asks={Interlocked.Read(ref _w177CorpseAsks)} corpse_answers={Interlocked.Read(ref _w177CorpseAnswers)} corpse_gone={Interlocked.Read(ref _w177CorpseGone)} tool_swings={Interlocked.Read(ref _w177ToolSwings)}");
}
