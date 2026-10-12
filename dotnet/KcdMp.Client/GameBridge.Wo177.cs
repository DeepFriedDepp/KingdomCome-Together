// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;

namespace KcdMp.Client;

// WO-177 (docs/WO-177-findings.md): the bridge. The rules are Wo177Rules (Wo177.cs); the Lua half is kdcmp.lua's WO-177 block.
//
//   A5 no load while a fight is running: Wo177WaitQuietAsync is called before every load this agent issues (join, leave, exit-to-menu, the
//      repaired copy's load). One question a second to the Lua (KCD2MP_W177LoadQuiet -> event w177_load_quiet "<why> ok|fight|hit"); the
//      first "not quiet" answer is told to the player; three minutes without a quiet answer = the load is not issued.
//        MP-W177 load why=<w> waiting reason=<fight|hit|no-answer>
//        MP-W177 load why=<w> go after <ms> ms            MP-W177 load why=<w> NOT issued: ...
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

    /// <summary>true = nothing is running and the load may be issued; false = a fight never ended (the caller does not load).</summary>
    internal async Task<bool> Wo177WaitQuietAsync(string why)
    {
        var sw = Stopwatch.StartNew();
        string? told = null;
        while (true)
        {
            string? reason = await Wo177ProbeAsync(why);
            if (Wo177Rules.IsQuiet(reason))
            {
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
            if (Wo177Rules.GaveUp(sw.ElapsedMilliseconds))
            {
                Interlocked.Increment(ref _w177Gaveup);
                Console.WriteLine($"MP-W177 load why={why} NOT issued: still {shown} after {sw.ElapsedMilliseconds / 1000} s");
                try { await ExecLuaAsync($"if KCD2MP_Wo124Msg then KCD2MP_Wo124Msg(\"{EscapeLua(Wo177Rules.GaveUpText)}\") end"); } catch { }
                return false;
            }
            await Task.Delay(Wo177Rules.PollMs);
        }
    }

    internal string Wo177StatsText() => FormattableString.Invariant(
        $"MP-W177-STATS load_waits={Interlocked.Read(ref _w177Waits)} load_gaveup={Interlocked.Read(ref _w177Gaveup)} probe_unanswered={Interlocked.Read(ref _w177Unanswered)} corpse_asks={Interlocked.Read(ref _w177CorpseAsks)} corpse_answers={Interlocked.Read(ref _w177CorpseAnswers)} corpse_gone={Interlocked.Read(ref _w177CorpseGone)} tool_swings={Interlocked.Read(ref _w177ToolSwings)}");
}
