// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-210 (docs/WO-210-findings.md): the bridge's half -- the switches (Lua w210_cfg, mp_w210_status), the DLL's switch word, A's posture
// release (a copy whose host NPC left the bed, seat or workstation its own body still holds), the DLL's asks (op 140: a copy the owner's
// watchdog found holding a posture its host NPC left), and A.3/A.4's re-ask of the host's whole-body states (NpcAnimAsk, 0x7E -> 0x7F).
// The pure rules are Wo210Rules (Wo210.cs).
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w210On = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w210ReleasedAt = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w210EngineSignal = new(StringComparer.Ordinal);   // C.1: NPC -> when its blow's ClientHit was queued
    private readonly ConcurrentDictionary<(byte, string), long> _w210LastBlow = new();   // E.3: (figure, attacker) -> its last blow on the figure
    private long _w210ComboHolds;
    private long _w210LeaveReleases, _w210AskReleases, _w210AsksPolled, _w210AnimAsksOut, _w210AnimAsksIn, _w210AnimAskBad;
    private volatile uint _w210DllEff = uint.MaxValue;
    private long _w210OppWithRow;   // D.4: the opponents read right behind an NPC's attack row

    /// <summary>A WO-210 switch: on unless the mod said off (a test twin's KCDMP_TEST_W210_OFF=name,name turns it off from the start).</summary>
    private bool W210(string name) =>
        _w210On.TryGetValue(name, out bool v) ? v
            : Wo210Rules.Default(name, Environment.GetEnvironmentVariable("KCDMP_TEST_W210_OFF"), Environment.GetEnvironmentVariable("KCDMP_TEST_W210_ON"));

    /// <summary>A switch with a DLL bit, as the DLL runs it: the agent's word and the DLL's (a game-side twin turns the DLL's off).</summary>
    private bool W210Eff(string name, uint bit) => W210(name) && (_w210DllEff & bit) != 0;

    private async Task Wo210PushConfigAsync()
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo210OpConfig, BitConverter.GetBytes(Wo210Rules.ConfigBits(W210)));
            if (r is { Ok: true, Payload.Length: >= 4 } x) _w210DllEff = BitConverter.ToUInt32(x.Payload, 0);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W210 config not sent to the DLL: {ex.Message}"); }
    }

    private async Task Wo210NativeStatusAsync()
    {
        var r = await _combat.Wo163Async(Protocol.Wo210OpStatus, []);
        if (r is { Ok: true } rr) Console.WriteLine($"MP-W210-NATIVE {System.Text.Encoding.ASCII.GetString(rr.Payload)}");
    }

    /// <summary>The 4 Hz session tick (Wo201Tick).</summary>
    private void Wo210Tick(long n)
    {
        if (n % 8 == 6) _ = Wo210PushConfigAsync();
        if (n % 120 == 9 && (W137Host || W137Joiner)) _ = Wo210NativeStatusAsync();
        if (W137Joiner && n % 4 == 1) _ = Wo210AsksTickAsync();   // 1 Hz: the joiner's pipe serves about one request a frame (the copies' samples too)
    }

    // ---------------------------------------------------------------- A: the copy's own posture, released when its host NPC left it

    /// <summary>
    /// The joiner, before a host row is applied to a copy (W160ApplyAsync): the copy's own body holds a posture its host NPC left (a bed,
    /// a seat, a workstation) -- released the game's own way first (KCD2MP_W160Release: the stance and unstance resets), so the game's
    /// planner stands it up and the owner gives it the host's current state. True when released.
    /// </summary>
    private async Task<bool> Wo210ReleaseOnLeaveAsync(string name, bool had, ActivityState prev, ActivityState next)
    {
        bool avatar = name.StartsWith("kcd2mp_", StringComparison.Ordinal);
        if (!Wo210Rules.ReleaseOnLeave(W210("copy_state_always"), avatar, had, prev, next)) return false;
        try { await ExecLuaAsync($"if KCD2MP_W160Release then KCD2MP_W160Release(\"{EscapeLua(name)}\", \"w210-host-left\") end"); }
        catch { return false; }
        _w210ReleasedAt[name] = Environment.TickCount64;
        long k = Interlocked.Increment(ref _w210LeaveReleases);
        if (k <= 200 || k % 50 == 0)
            Console.WriteLine($"WO210-STATE handover npc={name} path=15-host-base posture={prev.Normalised()} -> {next.Normalised()}: the copy's own posture released the game's way [n={k}]");
        return true;
    }

    /// <summary>The joiner, 4 times a second: the owner's watchdog asks (op 140) -- a copy holding a posture of its own its host NPC
    /// left, after a reseat that could not lay over it -- released the game's way, at most once per copy per 5 s.</summary>
    private async Task Wo210AsksTickAsync()
    {
        if (!W210("copy_state_always")) return;
        var r = await _combat.Wo163Async(Protocol.Wo210OpAsks, [8]);
        Interlocked.Increment(ref _w210AsksPolled);
        if (r is not { Ok: true } rr || rr.Payload.Length < 1 || rr.Payload[0] == 0) return;
        long now = Environment.TickCount64;
        foreach (var (kind, name) in Wo210Rules.ParseAsks(rr.Payload))
        {
            if (kind != Protocol.Wo210AskRelease) continue;
            if (_w210ReleasedAt.TryGetValue(name, out long at) && now - at < 5000) continue;
            _w210ReleasedAt[name] = now;
            try { await ExecLuaAsync($"if KCD2MP_W160Release then KCD2MP_W160Release(\"{EscapeLua(name)}\", \"w210-reseat\") end"); }
            catch { continue; }
            long k = Interlocked.Increment(ref _w210AskReleases);
            if (k <= 200 || k % 50 == 0)
                Console.WriteLine($"WO210-STATE handover npc={name} path=watchdog: the copy's own posture its host NPC left released the game's way [n={k}]");
        }
    }

    // ---------------------------------------------------------------- A.3 / A.4: the host's whole-body states asked again

    /// <summary>The joiner: ask the host for these bodies' state again (an empty list: every body), after a time skip or a load, or for a
    /// posture the stream has not confirmed.</summary>
    private async Task Wo210AskAnimAsync(IReadOnlyList<string> names, string why)
    {
        if (!W137Joiner || !W210("copy_state_always")) return;
        byte[]? body = Wo210Rules.AnimAskBody(names);
        if (body is null) return;
        try
        {
            await WriteJoinAsync(Protocol.BuildJoinUp(Protocol.NpcAnimAskUp, 0, 0, body));
            long k = Interlocked.Increment(ref _w210AnimAsksOut);
            if (k <= 40 || k % 25 == 0) Console.WriteLine($"WO210-STATE ask npc-anim why={why} bodies={(names.Count == 0 ? "all" : string.Join(',', names))} [n={k}]");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W210 npc-anim ask not sent: {ex.Message}"); }
    }

    /// <summary>A.4: after a time skip, a load or a join -- the host's whole-body states asked afresh (every body), twice: once now and once
    /// when the world has settled (the skip's own end and the wake's fader come a few seconds apart).</summary>
    private async Task Wo210AfterSkipAsync(string why)
    {
        await Wo210AskAnimAsync([], why);
        await Task.Delay(4000);
        await Wo210AskAnimAsync([], why + "+4s");
    }

    /// <summary>The host: a joiner's NpcAnimAsk -- those bodies' (or every body's) state is sent again at the next 10 Hz poll.</summary>
    private async Task Wo210OnAnimAskAsync(byte src, byte[] body)
    {
        if (!W137Host) return;
        if (!Wo210Rules.TryParseAnimAsk(body, out var names)) { Interlocked.Increment(ref _w210AnimAskBad); return; }
        Interlocked.Increment(ref _w210AnimAsksIn);
        await _combat.Wo163Async(Protocol.Wo210OpStreamForget, Wo210Rules.AnimAskBody(names) ?? [0]);
        Console.WriteLine($"WO210-STATE npc-anim asked by peer {src}: {(names.Count == 0 ? "every body" : string.Join(',', names))} sent again");
    }

    // ---------------------------------------------------------------- H.5: the clocks at every mark, and a gap outside a skip

    private (uint Host, DateTime At, double Ratio)? _w210HostClock;
    private DateTime _w210LastGapLine = DateTime.MinValue, _w210SkipSeenUtc = DateTime.MinValue;
    private long _w210GapLines;

    /// <summary>This machine's world time now (the agent's last poll moved on by the game's own ratio); null before the first poll.</summary>
    private double? Wo210MyWorldTime() => _lastPolledWorldTime is uint mine ? mine + (DateTime.UtcNow - _lastPollUtc).TotalSeconds * _w200Ratio : null;

    /// <summary>The host's world time now, from its last clock message (the joiner); null without one.</summary>
    private double? Wo210HostWorldTime() => _w210HostClock is { } h ? h.Host + (DateTime.UtcNow - h.At).TotalSeconds * h.Ratio : null;

    private static string Wo210Hm(double? t) => t is double v ? FormattableString.Invariant($"{(int)(v % 86400) / 3600:00}:{(int)(v % 3600) / 60:00}") : "?";

    /// <summary>The joiner, at each host clock message (WO-200 II.7): kept for the marks; a gap over 2 game-minutes outside a sleep or a
    /// wait (and the minute after one) is told, at most every 30 s.</summary>
    private void Wo210OnHostClock(uint host, double ratio, double errGameS, bool skipping)
    {
        _w210HostClock = (host, DateTime.UtcNow, ratio);
        if (skipping) { _w210SkipSeenUtc = DateTime.UtcNow; return; }
        if (Math.Abs(errGameS) <= 120 || (DateTime.UtcNow - _w210SkipSeenUtc).TotalSeconds < 60 || (DateTime.UtcNow - _w210LastGapLine).TotalSeconds < 30) return;
        _w210LastGapLine = DateTime.UtcNow;
        long k = Interlocked.Increment(ref _w210GapLines);
        Console.WriteLine(FormattableString.Invariant($"WO210-TIME gap this={Wo210Hm(Wo210MyWorldTime())} host={Wo210Hm(host)} gap_game_s={errGameS:+0;-0} -- over 2 game-minutes outside a sleep or wait [n={k}]"));
    }

    /// <summary>At every mark (this player's or the partner's ping): both machines' world time as this machine knows them, and the gap.</summary>
    private void Wo210TimeAtMark(string markId)
    {
        double? mine = Wo210MyWorldTime(), host = W137Host ? mine : Wo210HostWorldTime();
        string gap = mine is double a && host is double b ? FormattableString.Invariant($"{a - b:+0;-0}") : "?";
        Console.WriteLine(FormattableString.Invariant($"WO210-TIME mark={markId} this={Wo210Hm(mine)} host={Wo210Hm(host)} gap_game_s={gap} role={(W137Host ? "host" : "joiner")}"));
        // the sun's game time on this machine (the sky's own time of day, e_TimeOfDay), logged by the game under the same mark
        if (Wo210Rules.SafeMarkId(markId))
            _ = ExecLuaAsync($"if KCD2MP_W210Sun then KCD2MP_W210Sun(\"{markId}\") end");
    }

    // ---------------------------------------------------------------- C.1 / C.2: a body dead without the death reaction, finished the engine's way

    private long _w210Finished, _w210FinishAlready, _w210FinishOther;

    /// <summary>Op 142 on a dead body (by entity id, else by name): its result (Protocol.Wo210Finish*) and whether it now holds the engine's
    /// dead state (the game's loot test needs it); null when the DLL did not answer.</summary>
    private async Task<(byte Result, bool DeadState)?> Wo210FinishBodyAsync(string npc, uint eid = 0)
    {
        byte[] name = System.Text.Encoding.ASCII.GetBytes(npc);
        if (name.Length == 0 || name.Length > 63) return null;
        var a = new byte[5 + name.Length];
        BitConverter.GetBytes(eid).CopyTo(a, 0);
        a[4] = (byte)name.Length;
        name.CopyTo(a, 5);
        var r = await _combat.Wo163Async(Protocol.Wo210OpFinishBody, a);
        if (r is not { Ok: true, Payload.Length: >= 2 } x) return null;
        Interlocked.Increment(ref x.Payload[0] == Protocol.Wo210FinishDone ? ref _w210Finished : ref x.Payload[0] == Protocol.Wo210FinishAlready ? ref _w210FinishAlready : ref _w210FinishOther);
        return (x.Payload[0], x.Payload[1] != 0);
    }

    private static string Wo210FinishHow((byte Result, bool DeadState)? f, bool upright) => f is not { } v ? "no-answer"
        : v.Result == Protocol.Wo210FinishAlready ? "engine" : v.Result == Protocol.Wo210FinishDone ? (upright ? "finished-upright" : "finished") : $"refused-{v.Result}";

    /// <summary>The game's own loot test on the body, logged by the game (WO210-DEATH check npc= how= dead= lootable=).</summary>
    private Task Wo210DeathCheckAsync(string npc, string how) =>
        ExecLuaAsync($"if KCD2MP_W210DeathCheck then KCD2MP_W210DeathCheck(\"{EscapeLua(npc)}\", \"{how}\") end");

    // ---------------------------------------------------------------- H.6: the catch-up's profiles this player already has

    private volatile HashSet<string>? _w210OwnProfiles;
    private long _w210OwnProfilesAt, _w210ProfileSkips;

    /// <summary>H.6: this player's own active profiles, read once per catch-up (Wo210Rules.ProfileSnapshotMaxAgeMs); null = no answer.</summary>
    private async Task<IReadOnlySet<string>?> Wo210OwnProfilesAsync()
    {
        long now = Environment.TickCount64;
        if (_w210OwnProfiles is { } s && now - Interlocked.Read(ref _w210OwnProfilesAt) < Wo210Rules.ProfileSnapshotMaxAgeMs) return s;
        var list = await _combat.Wo189ActiveProfilesAsync();
        if (list is null) return null;
        var set = new HashSet<string>(list, StringComparer.Ordinal);
        Interlocked.Exchange(ref _w210OwnProfilesAt, now);
        _w210OwnProfiles = set;
        return set;
    }

    private void Wo210ForgetOwnProfiles() => _w210OwnProfiles = null;

    /// <summary>H.6: a catch-up row of a profile this player already has -- answered here, no DLL op. True when answered.</summary>
    private async Task<bool> Wo210ProfileSkipAsync(char op, string name)
    {
        if (op != '=' || !W210("profile_snapshot")) return false;
        if (!Wo210Rules.ProfileAlreadyActive(true, op, name, await Wo210OwnProfilesAsync())) return false;
        long k = Interlocked.Increment(ref _w210ProfileSkips);
        if (k <= 3 || k % 50 == 0)
            Console.WriteLine($"WO189-PROFILE host '{op}{name}' on the joiner: already active here (left to its own quest; this player's snapshot, no DLL op) [{k} so far]");
        return true;
    }

    private string Wo210Stats() =>
        $"leave_releases={Interlocked.Read(ref _w210LeaveReleases)} ask_releases={Interlocked.Read(ref _w210AskReleases)} asks_polled={Interlocked.Read(ref _w210AsksPolled)}"
        + $" anim_asks_out={Interlocked.Read(ref _w210AnimAsksOut)} anim_asks_in={Interlocked.Read(ref _w210AnimAsksIn)} anim_ask_bad={Interlocked.Read(ref _w210AnimAskBad)}"
        + $" combo_holds={Interlocked.Read(ref _w210ComboHolds)} profile_skips={Interlocked.Read(ref _w210ProfileSkips)} opp_with_row={Interlocked.Read(ref _w210OppWithRow)}"
        + $" bodies_finished={Interlocked.Read(ref _w210Finished)} already_dead_state={Interlocked.Read(ref _w210FinishAlready)} finish_other={Interlocked.Read(ref _w210FinishOther)}";

    private void Wo210OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w210_cfg":
                if (!Wo210Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W210 cfg refused: '{arg}'"); return; }
                _w210On[cfg] = on;
                Console.WriteLine($"MP-W210 cfg {cfg}={(on ? "on" : "off")}");
                _ = Wo210PushConfigAsync();
                return;
            case "w210_listen_keys":     // F.1: the listener's keys of one decision (the Lua's WO210-LISTEN line)
                Console.WriteLine($"WO210-VOTE key counts up/down/wheel_up/wheel_down/confirm/confirm_mouse={arg}");
                return;
            case "w210_hover_shown":     // F.2a: the starter's row drawn on this listener's rows
                Console.WriteLine($"WO210-HOVER shown dec/row={arg}");
                return;
            case "w210_pick_shown":      // F.2b: the pick drawn for its second
                Console.WriteLine($"WO210-PICK shown dec/row={arg}");
                return;
            case "w210_status":
                _ = Wo210NativeStatusAsync();
                Console.WriteLine($"MP-W210-STATS {string.Join(' ', Wo210Rules.SwitchNames.Select(n => $"{n}={(W210(n) ? "on" : "off")}"))} | {Wo210Stats()}");
                return;
        }
    }
}
