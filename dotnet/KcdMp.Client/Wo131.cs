// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>WO-131 op 1: what the DLL knows about one NPC copy at the moment of a hit.</summary>
/// <param name="Bound">the DLL drives this body from the host's stream</param>
/// <param name="AgeMs">since the newest accepted sample (65535 = none)</param>
/// <param name="DistM">the body to that sample, horizontal (-1 = unknown)</param>
/// <param name="Flags">the newest sample's stream flags (bit 0 dead)</param>
/// <param name="Hp">the copy's own health now (-1 unreadable)</param>
/// <param name="Guarded">the copy carries the imm guard (null = unreadable)</param>
public sealed record Wo131HitCheck(bool Bound, ushort AgeMs, float DistM, byte Flags, float Hp, bool? Guarded);

/// <summary>
/// WO-131: the rules of "one world, the host's", as pure functions so the
/// tests pin them (dotnet/KcdMp.Client.Tests/Wo131Tests.cs).
/// </summary>
public static class Wo131Rules
{
    /// <summary>1b: a joiner's hit counts only on a copy this close to the host NPC's streamed position.</summary>
    public const float HitGateMaxDistM = 3.0f;
    /// <summary>
    /// 1b: ... whose newest host sample is this fresh. WO-147: 6 s (was 3 s) -- a standing NPC's stream is a
    /// 2 s heartbeat, and 3 s left one second for every delay: the field dropped a hit on a standing archer
    /// 0.00 m from its sample as stale (age 3,538 ms).
    /// </summary>
    public const int HitGateMaxAgeMs = 6000;
    /// <summary>1c: a guarded copy at or under this health took a blow it could not show (the imm floor is 1).</summary>
    public const float GuardFloorHp = 1.05f;

    public enum HitVerdict { Forward, DropNoAnswer, DropNotBound, DropStale, DropFar, DropDeadOnHost }

    public static string Tag(HitVerdict v) => v switch
    {
        HitVerdict.Forward => "forward",
        HitVerdict.DropNoAnswer => "no-answer",
        HitVerdict.DropNotBound => "not-bound",
        HitVerdict.DropStale => "stale-stream",
        HitVerdict.DropFar => "far-from-host",
        HitVerdict.DropDeadOnHost => "dead-on-host",
        _ => "?",
    };

    /// <summary>
    /// 1b + 1c: the joiner's gate for one measured hit on a host-owned NPC copy.
    /// Forward only a hit on a body the host's stream drives, fresh, within
    /// <see cref="HitGateMaxDistM"/> of the host's position and alive there.
    /// The joiner never sends a kill: the forwarded damage is the host's to
    /// apply and the host decides death. A guarded copy pinned at the imm floor
    /// could not show the rest of the blow, so one more point goes with it and
    /// the host's own health decides.
    /// </summary>
    public static (HitVerdict Verdict, float Health) GateJoinerHit(Wo131HitCheck? hc, float measured)
    {
        if (hc is null) return (HitVerdict.DropNoAnswer, 0);
        if (!hc.Bound) return (HitVerdict.DropNotBound, 0);
        if (hc.AgeMs > HitGateMaxAgeMs) return (HitVerdict.DropStale, 0);
        if (hc.DistM < 0 || hc.DistM > HitGateMaxDistM) return (HitVerdict.DropFar, 0);
        if ((hc.Flags & 0x01) != 0) return (HitVerdict.DropDeadOnHost, 0);
        float h = measured;
        if (hc.Guarded == true && hc.Hp >= 0 && hc.Hp <= GuardFloorHp) h += 1.0f;
        return (HitVerdict.Forward, h);
    }

    /// <summary>1c: the joiner writes the host's health onto its copy when it moved by more than this.</summary>
    public const float FollowHpEpsilon = 0.5f;
    /// <summary>1c: ... at most this often per NPC.</summary>
    public const int FollowHpMinIntervalMs = 250;

    public static bool FollowHpDue(float lastWritten, float streamHp, long msSinceLast, bool dead)
    {
        if (dead || streamHp < 0) return false;
        if (msSinceLast < FollowHpMinIntervalMs) return false;
        return float.IsNaN(lastWritten) || Math.Abs(streamHp - lastWritten) > FollowHpEpsilon;
    }

    /// <summary>1e: the host puts an avatar's health back to this after a measured NPC hit (SetState clamps to max).</summary>
    public const float AvatarRestoreHp = 1000f;
}
