// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

// WO-133: quest safety in a shared world -- the agent's half
// (docs/WO-133-findings.md).
//
// In a shared-world session (Wo133Rules.SharedWorldSession) none of the old
// separate-worlds quest layer acts, on either machine:
//   * the mod is told (KCD2MP_Wo133Gate) and turns off the catch-up prompt,
//     F11/F12, the mp_quest_* fires, approach announcements, divergence /
//     WAITING_FOR_PEER and the quest-gap toasts;
//   * this agent reports no story divergence and compares no fingerprints;
//     the joiner sends neither its (solo-world) story marker nor fingerprints;
//   * the host drops every time skip that is not its own (hazard H2): only
//     the host's clock moves the world. The joiner's own clock-jump report is
//     already withheld by WO-114 (ReportClockJumpAsync).
// Outside one (solo, mp_shared_world off) nothing here changes anything.
public partial class GameBridge
{
    private volatile bool _w133On;
    private bool _w133Pushed;              // the current state has reached the mod at least once
    private volatile bool _w133PushDue;    // the mod's Lua was reborn
    private DateTime _w133LastPushUtc = DateTime.MinValue;
    private long _w133SkipsDropped, _w133DivergencesSkipped, _w133FingerprintsSkipped, _w133MarkersWithheld;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _w133SkipByWhere = new(StringComparer.Ordinal);
    private static readonly TimeSpan W133Heartbeat = TimeSpan.FromSeconds(5);

    private bool Wo133SharedWorld => Wo133Rules.SharedWorldSession(_combatRoleApplied, _isDamageAuthority, _sharedWorld, JoinerSharedEffective);
    private bool Wo133HostOfSharedWorld => Wo133Rules.HostOfSharedWorld(_combatRoleApplied, _isDamageAuthority, _sharedWorld);
    private bool Wo133JoinerQuiet => Wo133Rules.JoinerQuietStory(Wo133SharedWorld, _isDamageAuthority);

    /// <summary>1 s (the WO-122 tick) and on every role change: the gate to the mod, on change and as a heartbeat.</summary>
    private async Task Wo133TickAsync()
    {
        bool on = Wo133SharedWorld;
        string role = !_combatRoleApplied ? "none" : _isDamageAuthority ? "host" : "joiner";
        string why = Wo133Rules.Why(_combatRoleApplied, _isDamageAuthority, _sharedWorld, _hostModeKnown, _hostSharedWorld);
        bool changed = on != _w133On || !_w133Pushed;
        if (changed)
        {
            _w133On = on;
            Console.WriteLine(on
                ? $"MP-WO133 shared world ({role}, {why}): the old quest layer is OFF -- no catch-up prompt or Haste fire, no divergence, no fingerprints; {(role == "host" ? "time skips from joiners are dropped" : "this machine's clock follows the host's")}"
                : $"MP-WO133 no shared world ({why}): the old quest layer behaves as before");
        }
        if (!changed && !_w133PushDue && DateTime.UtcNow - _w133LastPushUtc < W133Heartbeat) return;
        _w133PushDue = false;
        _w133LastPushUtc = DateTime.UtcNow;
        try { await ExecLuaAsync(Wo133Rules.GateLua(on, role, why)); _w133Pushed = true; }
        catch (Exception ex) { Console.WriteLine($"MP-WO133 could not tell the mod: {ex.Message}"); }
    }

    private async Task Wo133OnDisconnectAsync()
    {
        _w133On = false;
        _w133Pushed = false;
        Console.WriteLine($"MP-WO133 session ended: the old quest layer behaves as before ({W133Stats()})");
        try { await ExecLuaAsync(Wo133Rules.GateLua(false, "none", "no-session")); } catch { }
    }

    private string W133Stats() => FormattableString.Invariant(
        $"skips_dropped={Interlocked.Read(ref _w133SkipsDropped)} divergences_skipped={Interlocked.Read(ref _w133DivergencesSkipped)} fingerprints_skipped={Interlocked.Read(ref _w133FingerprintsSkipped)} markers_withheld={Interlocked.Read(ref _w133MarkersWithheld)}");

    /// <summary>The host's drop of a joiner's time skip (any phase). True = dropped, logged.</summary>
    private bool Wo133DropTimeSkip(byte source, byte phase, byte kind, uint worldTime)
    {
        bool host = Wo133HostOfSharedWorld;
        int hostId = _hostModeKnown && _hostModeFrom != 0xFF ? _hostModeFrom : -1;
        if (!Wo133Rules.DropInboundTimeSkip(host, Wo133JoinerQuiet, hostId, source, _myGhostId)) return false;
        long n = Interlocked.Increment(ref _w133SkipsDropped);
        string who = (_ghostNames.TryGetValue(source, out var dn) ? dn : $"player {source}") + (host ? "" : $" (not the host, ghost {hostId})");
        string ph = phase switch { Protocol.TimeSkipPhaseStart => "start", Protocol.TimeSkipPhaseSync => "sync", Protocol.TimeSkipPhaseDoneQuiet => "done-quiet", _ => "done" };
        Console.WriteLine($"[timeskip] DROPPED {who}'s time skip ({ph} kind={kind} t={worldTime}) -- WO-133: in a shared world only the host's clock moves the world (#{n}; our clock {(_lastPolledWorldTime is uint c ? c.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?")})");
        return true;
    }

    /// <summary>Divergence detection is off in a shared world. True = skipped (counted; logged once per 50).</summary>
    private bool Wo133SkipDivergence(string where)
    {
        if (!Wo133SharedWorld) return false;
        long n = Interlocked.Increment(ref _w133DivergencesSkipped);
        if (W133FirstOrEvery(where, 50))
            Console.WriteLine($"[story] {where}: no peer comparison in a shared world (WO-133; {n} skipped)");
        return true;
    }

    /// <summary>Fingerprints are off in a shared world (compare on both machines; read + send on the joiner).</summary>
    private bool Wo133SkipFingerprint(string where)
    {
        // the host may still fingerprint its own saves; nobody compares
        if (where == "send" ? !Wo133JoinerQuiet : !Wo133SharedWorld) return false;
        long n = Interlocked.Increment(ref _w133FingerprintsSkipped);
        if (W133FirstOrEvery("fingerprint " + where, 20))
            Console.WriteLine($"[quest] fingerprint {where} skipped in a shared world (WO-133; {n} skipped)");
        return true;
    }

    /// <summary>Log the first skip of each kind, then every Nth of that kind.</summary>
    private bool W133FirstOrEvery(string where, int every)
    {
        long k = _w133SkipByWhere.AddOrUpdate(where, 1, (_, v) => v + 1);
        return k == 1 || k % every == 0;
    }

    /// <summary>The joiner's story marker is its solo world's: not sent in a shared world.</summary>
    private bool Wo133WithholdMarker()
    {
        if (!Wo133JoinerQuiet) return false;
        long n = Interlocked.Increment(ref _w133MarkersWithheld);
        if (n == 1 || n % 20 == 0)
            Console.WriteLine($"[story] our story marker is not sent: a joiner in a shared world (WO-133; {n} withheld)");
        return true;
    }
}
