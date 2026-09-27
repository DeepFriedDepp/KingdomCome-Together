using System.Buffers.Binary;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-132 -- combat engagement (docs/WO-132-findings.md).
//
// The host's NPC in a fight, for the joiner: ActionKind.NpcCombat on the
// action channel (forwarded verbatim by the relay, from the damage authority
// only). The host samples the NPC's own combat model; the joiner holds the
// same state on its bound, suspended copy (combat mode, guard zone and stance,
// attack zone, block) so its own game treats that NPC as an opponent -- the
// cause, not an animation. Outcomes stay the host's (0x21 / 0x30 / deaths).
//
//   payload [senderMs:4][state:12 BodyState2][target:1 NpcCombatTarget][targetGhost:1][nameLen:1][name]
//
// Sent on a change and every second while the NPC is in combat; one last
// "combat off" when it leaves combat. No protocol bump: a new kind on the
// action channel, ignored by an older receiver (and the relay refuses mixed
// releases anyway).
// ---------------------------------------------------------------------------

/// <summary>WO-132: who the host's NPC is fighting. APPEND-ONLY.</summary>
public enum NpcCombatTarget : byte
{
    None = 0,
    /// <summary>The host's own player.</summary>
    Host = 1,
    /// <summary>A peer's avatar on the host (targetGhost = that peer's ghost id).</summary>
    Avatar = 2,
    /// <summary>Someone else (another NPC, an animal).</summary>
    Other = 3,
}

/// <summary>WO-132: the NpcCombat event payload.</summary>
public readonly record struct NpcCombatEvent(uint SenderMs, BodyState2 State, NpcCombatTarget Target, byte TargetGhost, string Name)
{
    public const int FixedLen = 4 + BodyState2.Len + 1 + 1 + 1;
    public const int MaxNameLen = Protocol.ActionPayloadMaxLen - FixedLen;

    public bool InCombat => State.CombatMode;

    public byte[] ToBytes()
    {
        var nb = System.Text.Encoding.UTF8.GetBytes(Name ?? "");
        if (nb.Length == 0 || nb.Length > MaxNameLen) throw new ArgumentOutOfRangeException(nameof(Name));
        var b = new byte[FixedLen + nb.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(b, SenderMs);
        State.Write(b.AsSpan(4));
        b[16] = (byte)Target;
        b[17] = TargetGhost;
        b[18] = (byte)nb.Length;
        nb.CopyTo(b, FixedLen);
        return b;
    }

    public static bool TryFromBytes(ReadOnlySpan<byte> b, out NpcCombatEvent e)
    {
        e = default;
        if (b.Length < FixedLen) return false;
        int n = b[18];
        if (n == 0 || n > MaxNameLen || b.Length != FixedLen + n) return false;
        if (b[16] > (byte)NpcCombatTarget.Other) return false;
        e = new NpcCombatEvent(BinaryPrimitives.ReadUInt32LittleEndian(b), BodyState2.Read(b.Slice(4, BodyState2.Len)),
                               (NpcCombatTarget)b[16], b[17], System.Text.Encoding.UTF8.GetString(b.Slice(FixedLen, n)));
        return true;
    }

    public override string ToString() => $"npc={Name} {State} target={Target}{(Target == NpcCombatTarget.Avatar ? ":" + TargetGhost : "")}";
}
