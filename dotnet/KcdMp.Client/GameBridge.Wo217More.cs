// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-217's parts that live in the agent: B.1 (the opponent mirror's opponents of this player, for the ghost fight).
public partial class GameBridge
{
    // ---------------------------------------------------------------- B.1 ghost_far
    // The host's opponent stream (WO-209 A, NpcOpp 0x7D) names, per host NPC, whom it fights; a host NPC fighting THIS player's figure there
    // is a real opponent here, whatever its distance (WO-216 H: a mirrored bandit 42 m off kept a real fight the 30 m look would have ended).
    private readonly ConcurrentDictionary<string, long> _w217OppMe = new(StringComparer.Ordinal);
    private long _w217GhostTicks, _w217GhostWithFar;

    /// <summary>The joiner, on every opponent frame: the host NPCs that fight this player now (and the ones that stopped).</summary>
    private void Wo217NoteOpponents(byte[] body)
    {
        long now = Environment.TickCount64;
        foreach (var (name, me) in Wo217Rules.OpponentsOfMe(body, _myGhostId))
        {
            if (me) _w217OppMe[name] = now;
            else _w217OppMe.TryRemove(name, out _);
        }
    }

    /// <summary>The names the mirror holds as this player's opponents now (fresh within the mirror's own 4 s).</summary>
    private string Wo217OpponentNames()
    {
        long now = Environment.TickCount64;
        foreach (var kv in _w217OppMe) if (now - kv.Value > Wo209Rules.MirrorFightFreshMs) _w217OppMe.TryRemove(kv.Key, out _);
        return string.Join(',', _w217OppMe.Keys.Take(24));
    }

    /// <summary>B.1: the mod's ghost-fight check, with the mirror's opponents when ghost_far is on (the twin: as WO-216).</summary>
    private string Wo217GhostFightCall()
    {
        Interlocked.Increment(ref _w217GhostTicks);
        if (!W217On("ghost_far")) return "if KCD2MP_W215GhostFightTick then KCD2MP_W215GhostFightTick() end";
        string names = Wo217OpponentNames();
        if (names.Length > 0) Interlocked.Increment(ref _w217GhostWithFar);
        return $"if KCD2MP_W215GhostFightTick then KCD2MP_W215GhostFightTick(\"{names}\") end";
    }

    private string Wo217MoreStats() =>
        $" ghost_ticks={Interlocked.Read(ref _w217GhostTicks)} ghost_ticks_with_mirror_opponents={Interlocked.Read(ref _w217GhostWithFar)} opp_me_now={_w217OppMe.Count}"
        + Wo217LootStats();

    private void Wo217TickMore(long n) { Wo217LootTick(n); }

    private void Wo217OnEventMore(string name, string? arg)
    {
        if (Wo217LootEvent(name, arg)) return;
        Console.WriteLine($"MP-W217 event '{name}' not known here");
    }
}

/// <summary>WO-217's pure rules on the agent's side (tests: KcdMp.Client.Tests).</summary>
public static class Wo217Rules
{
    /// <summary>route_skip: the joiner drops a route line (0x85) when the switch is on and this machine's mp_route (WO-215 A.1, the replay
    /// that reads the lines) is off; with route_skip off, or mp_route on, every line goes to the DLL as in 0.64.3.</summary>
    public static bool SkipRoute(bool routeSkipOn, bool mpRouteOn) => routeSkipOn && !mpRouteOn;

    /// <summary>
    /// B.1: from the host's opponent wire body ([count] per body [nameLen][name][kind][ghost][oppNameLen][oppName]), each NPC and whether it
    /// fights this player's figure (kind = a peer's figure, that figure's ghost = mine). Empty when malformed.
    /// </summary>
    public static List<(string Name, bool Me)> OpponentsOfMe(ReadOnlySpan<byte> wire, byte myGhost)
    {
        var out_ = new List<(string, bool)>();
        if (wire.Length < 1) return out_;
        int count = wire[0], o = 1;
        for (int i = 0; i < count; i++)
        {
            if (o + 1 > wire.Length) return [];
            int nl = wire[o];
            if (nl == 0 || o + 1 + nl + 3 > wire.Length) return [];
            string name = System.Text.Encoding.ASCII.GetString(wire.Slice(o + 1, nl));
            o += 1 + nl;
            byte kind = wire[o], ghost = wire[o + 1];
            int onl = wire[o + 2];
            o += 3 + onl;
            if (o > wire.Length || kind > Wo209Rules.HostNpc) return [];
            out_.Add((name, kind == Wo209Rules.HostFigure && myGhost != 0xFF && ghost == myGhost));
        }
        return o == wire.Length ? out_ : [];
    }
}
