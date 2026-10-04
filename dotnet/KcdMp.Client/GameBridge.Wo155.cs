// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-155: hits never knock a player down; his figure falls only when he dies (docs/WO-155-findings.md). The rules are
// Wo155Rules in Wo155.cs.
//
//   * An NPC's or an animal's blow on this player (the damage authority's 0x22) goes in with SuppressHitReaction:
//     mp_hit_knockdown (default off) puts 0.45.0's knockdown back.
//   * A friendly-fire hit keeps knocking the victim down on his own screen (the DLL; never twice within 5 s;
//     mp_ff_knockdown). The attacker's figure of him falls and gets up with the game's own animations -- but only for a
//     down edge that follows a hit THIS player sent him (_w155FfSentAt): any other down (a knockout) leaves the figure.
//   * A partner's death (his vitals: down, and not a knockout) lays his figure down where he died; his respawn removes
//     it and a fresh figure takes its place (the Lua half: KCD2MP_W155AvatarCollapse / KCD2MP_W155AvatarReplace).
public partial class GameBridge
{
    private volatile bool _w155HitKnockdown;          // mp_hit_knockdown (default off)
    private volatile bool _w155FfKnockdown = true;    // mp_ff_knockdown
    private readonly ConcurrentDictionary<byte, long> _w155FfSentAt = new();   // victim ghost -> TickCount64 of my last friendly-fire hit on him
    private readonly ConcurrentDictionary<byte, long> _w155Collapsed = new();  // ghost -> TickCount64 his figure was laid down (it lies dead on this screen)
    private readonly ConcurrentDictionary<byte, bool> _w155Fell = new();       // ghost -> his figure was told to fall (the knockdown of my hit)
    private long _w155HitsSuppressed, _w155HitsKnocking, _w155FfFalls, _w155FfSkipped, _w155Collapses, _w155Replaces;

    /// <summary>This player hit a partner's avatar with a friendly-fire hit that went out: the knockdown it causes is his.</summary>
    private void Wo155NoteFfSent(byte victimGhost) => _w155FfSentAt[victimGhost] = Environment.TickCount64;

    /// <summary>The flags byte of the ApplyPvpHit pipe request (the wire flags, plus the no-knockdown bit when it is off).</summary>
    private byte Wo155PvpFlags(byte wireFlags) => Wo155Rules.PvpFlags(wireFlags, _w155FfKnockdown);

    /// <summary>An NPC's or an animal's blow on this player: suppressed (no fall) unless mp_hit_knockdown is on.</summary>
    private bool Wo155SuppressNpcHit()
    {
        bool suppress = !_w155HitKnockdown;
        if (suppress) Interlocked.Increment(ref _w155HitsSuppressed); else Interlocked.Increment(ref _w155HitsKnocking);
        return suppress;
    }

    /// <summary>
    /// A partner's down edge (his DLL's Downed bit: his body is not a living entity): it makes his figure fall here only
    /// when this player's own friendly-fire hit caused it. A knockout or the death guard's knockdown leaves the figure
    /// alone (a death is the vitals' business: <see cref="Wo155OnPeerBody"/>).
    /// </summary>
    private void Wo155OnPeerDownEdge(byte ghost, bool down)
    {
        if (!_w154AvatarFalls || !_w155FfKnockdown)
        {
            Console.WriteLine($"MP-W155 peer {ghost} {(down ? "is down" : "is up again")} -- {(!_w154AvatarFalls ? "mp_avatar_falls" : "mp_ff_knockdown")} off: its figure stays as it is");
            return;
        }
        if (!down)
        {
            if (_w155Fell.TryRemove(ghost, out _))
            {
                Interlocked.Increment(ref _w154Rises);
                Console.WriteLine($"MP-W155 peer {ghost} is up again -- his figure gets up by the game's own animation (the Lua watch hands it back)");
            }
            else Console.WriteLine($"MP-W155 peer {ghost} is up again");
            return;
        }
        _w155FfSentAt.TryGetValue(ghost, out long sent);
        if (!Wo155Rules.FriendlyKnock(sent, Environment.TickCount64))
        {
            Interlocked.Increment(ref _w155FfSkipped);
            Console.WriteLine($"MP-W155 peer {ghost} is down, but not from a friendly-fire hit of mine -- his figure does not fall (only a death or a friendly-fire knockdown does)");
            return;
        }
        _w155Fell[ghost] = true;
        Interlocked.Increment(ref _w155FfFalls); Interlocked.Increment(ref _w154Falls);
        Console.WriteLine($"MP-W155 peer {ghost} is DOWN from my friendly-fire hit -> his figure falls here with the game's own animation");
        _ = ExecLuaAsync($"if KCD2MP_W155AvatarFall then KCD2MP_W155AvatarFall(\"{ghost}\") end");
    }

    /// <summary>
    /// A partner's body, from his vitals flags (both roles): a death lays his figure down where it stands and it lies
    /// there; his respawn hides it and, 1.5 s later, a fresh figure replaces it. A knockout or knockdown changes nothing.
    /// </summary>
    private void Wo155OnPeerBody(byte ghost, Wo155Rules.Body now, Wo155Rules.Body was)
    {
        if (now == Wo155Rules.Body.Death && was != Wo155Rules.Body.Death) Wo155CollapseFigure(ghost, "his vitals");
        else if (now == Wo155Rules.Body.Up && _w155Collapsed.TryGetValue(ghost, out long since)
                 && Wo155Rules.RespawnSeen(was == Wo155Rules.Body.Death, since, Environment.TickCount64))
            Wo155ReplaceFigure(ghost);
    }

    /// <summary>A partner died (his 0x24 packet or his vitals, whichever comes first; the second is a no-op): his
    /// figure collapses where it stands and lies there until he respawns.</summary>
    private void Wo155CollapseFigure(byte ghost, string source)
    {
        if (!_w154AvatarFalls)
        {
            Console.WriteLine($"MP-W155 peer {ghost} died ({source}) -- mp_avatar_falls off: his figure is hidden as before");
            return;
        }
        if (!_w155Collapsed.TryAdd(ghost, Environment.TickCount64)) return;
        Interlocked.Increment(ref _w155Collapses);
        Console.WriteLine($"MP-W155 peer {ghost} DIED ({source}) -> his figure collapses where it stands and lies there until he respawns");
        _ = ExecLuaAsync($"if KCD2MP_W155AvatarCollapse then KCD2MP_W155AvatarCollapse(\"{ghost}\", true) end");
    }

    /// <summary>A partner whose figure lies dead is back up (his respawn): the lying figure is removed now and a
    /// fresh one takes its place once his new position has streamed in.</summary>
    private void Wo155ReplaceFigure(byte ghost)
    {
        if (!_w155Collapsed.TryRemove(ghost, out _)) return;
        Interlocked.Increment(ref _w155Replaces);
        Console.WriteLine($"MP-W155 peer {ghost} respawned -> the lying figure is removed now, a fresh one in {Wo155Rules.RespawnReplaceMs / 1000.0:F1} s at the place he woke");
        _ = ExecLuaAsync($"if KCD2MP_W155AvatarCollapse then KCD2MP_W155AvatarCollapse(\"{ghost}\", false) end");
        _ = Task.Run(async () =>
        {
            await Task.Delay(Wo155Rules.RespawnReplaceMs);
            await ExecLuaAsync($"if KCD2MP_W155AvatarReplace then KCD2MP_W155AvatarReplace(\"{ghost}\") end");
        });
    }

    private void Wo155ForgetPeer(byte ghost)
    {
        _w155FfSentAt.TryRemove(ghost, out _);
        _w155Collapsed.TryRemove(ghost, out _);
        _w155Fell.TryRemove(ghost, out _);
    }

    private string Wo155StatsText() => FormattableString.Invariant(
        $"hit_knockdown={(_w155HitKnockdown ? "on" : "off")} ff_knockdown={(_w155FfKnockdown ? "on" : "off")} npc_hits_suppressed={_w155HitsSuppressed} npc_hits_knocking={_w155HitsKnocking} ff_falls={_w155FfFalls} ff_skipped={_w155FfSkipped} collapses={_w155Collapses} replaces={_w155Replaces}");

    private void Wo155OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w155_cfg":   // hit_knockdown=on|off ff_knockdown=on|off
                foreach (var kv in f)
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0 || kv[(eq + 1)..] is not ("on" or "off")) continue;
                    bool on = kv[(eq + 1)..] == "on";
                    switch (kv[..eq])
                    {
                        case "hit_knockdown":
                            _w155HitKnockdown = on;
                            Console.WriteLine($"MP-W155 mp_hit_knockdown {(on ? "ON" : "off")} -- a blow of an NPC or an animal {(on ? "knocks a player down (0.45.0)" : "only takes health and stamina: nobody is knocked down")}");
                            break;
                        case "ff_knockdown":
                            _w155FfKnockdown = on;
                            Console.WriteLine($"MP-W155 mp_ff_knockdown {(on ? "ON" : "off")} -- a friendly-fire hit {(on ? "knocks the victim down, never twice within 5 s" : "never knocks the victim down")}");
                            break;
                    }
                }
                return;
        }
    }
}
