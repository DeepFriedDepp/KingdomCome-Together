using System.Buffers.Binary;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>WO-132: one NPC's combat state as the DLL read it (frame 0x9B / op 6). Zones are table ids, -1 = none.</summary>
public readonly record struct NpcCombatState(uint Eid, bool Combat, sbyte GuardZone, sbyte GuardStance, sbyte AtkZone,
                                             bool Block, bool OpponentIsPlayer, uint OpponentEid, string Name, bool HasCombatActor = true)
{
    /// <summary>[eid:4][combat][gz][gs][atk][block][oppIsPlayer][oppEid:4][nameLen][name]</summary>
    public static bool TryParse(ReadOnlySpan<byte> b, out NpcCombatState s)
    {
        s = default;
        if (b.Length < 15 || b.Length != 15 + b[14]) return false;
        static sbyte Z(byte v) => v == 0xFF ? (sbyte)-1 : (sbyte)v;
        s = new NpcCombatState(BinaryPrimitives.ReadUInt32LittleEndian(b), b[4] == 1, Z(b[5]), Z(b[6]), Z(b[7]), b[8] == 1, b[9] == 1,
                               BinaryPrimitives.ReadUInt32LittleEndian(b[10..]), System.Text.Encoding.ASCII.GetString(b.Slice(15, b[14])));
        return true;
    }

    public override string ToString() => FormattableString.Invariant(
        $"combat={(Combat ? 1 : 0)} guard={GuardZone}/{GuardStance} atk={AtkZone} block={(Block ? 1 : 0)} opponent={(OpponentIsPlayer ? "player" : OpponentEid == 0 ? "-" : $"0x{OpponentEid:X}")}");
}

/// <summary>
/// WO-132: who may be hurt, and when. One gate per player: a DOWN (the death
/// guard floored him, or he died) blocks every forward until he is UP again
/// (the respawn announcement, or the downed flag clearing on a knockdown wake)
/// plus <see cref="GraceAfterWake"/>. The host keeps one per peer (nothing is
/// forwarded to a dead or waking joiner); the joiner keeps one for itself and
/// refuses a forwarded hit in the same window (belt and braces).
/// </summary>
public sealed class LifeGate
{
    public static readonly TimeSpan GraceAfterWake = TimeSpan.FromSeconds(5);
    /// <summary>A down that never sees an up (the peer quit, a lost packet) stops blocking after this.</summary>
    public static readonly TimeSpan MaxDown = TimeSpan.FromSeconds(180);

    private DateTime? _downAt, _upAt;

    public bool IsDown => _downAt is not null;

    /// <summary>True when this is a new down.</summary>
    public bool Down(DateTime now)
    {
        if (_downAt is not null) return false;
        _downAt = now;
        return true;
    }

    /// <summary>True when this ends a down (a repeat up only restarts the grace).</summary>
    public bool Up(DateTime now)
    {
        bool was = _downAt is not null;
        _downAt = null;
        _upAt = now;
        return was;
    }

    public enum Why { Open, Down, Waking }

    public Why Check(DateTime now)
    {
        if (_downAt is DateTime d)
        {
            if (now - d < MaxDown) return Why.Down;
            _downAt = null;   // stale: a lost up must never block him for the rest of the session
        }
        if (_upAt is DateTime u && now - u < GraceAfterWake) return Why.Waking;
        return Why.Open;
    }

    public static string Tag(Why w) => w switch { Why.Down => "down", Why.Waking => "waking", _ => "open" };
}

/// <summary>WO-132: the agent's pure rules (docs/WO-132-findings.md).</summary>
public static class Wo132Rules
{
    public enum GhostHitVerdict { Forward, SupersededByNative, Tick, Blocked }

    /// <summary>
    /// The Lua health sampler's <c>ghost_hit</c> (any drop of an avatar's
    /// health, a bleed tick included). With the DLL's hit watch armed, the
    /// real hits come from the hit chokepoint (0x9A) and this path sends
    /// nothing; without it, a drop under <see cref="MinSamplerHit"/> is a tick,
    /// never a hit (the field: 93 bleed ticks of ~0.05 hp, a second death).
    /// </summary>
    public static GhostHitVerdict JudgeGhostHit(bool nativeWatchArmed, float loss, LifeGate.Why gate)
    {
        if (nativeWatchArmed) return GhostHitVerdict.SupersededByNative;
        if (gate != LifeGate.Why.Open) return GhostHitVerdict.Blocked;
        if (!(loss >= MinSamplerHit)) return GhostHitVerdict.Tick;
        return GhostHitVerdict.Forward;
    }

    public const float MinSamplerHit = 1.0f;

    /// <summary>Whether a LocalHit on a body goes out on the guid route: never for an avatar (its hits have their own flows).</summary>
    public static bool GuidRouteAllowed(string? npcName) =>
        npcName is null || !npcName.StartsWith("kcd2mp_", StringComparison.Ordinal);

    /// <summary>The NpcCombat event for one NPC state read on the host.</summary>
    public static NpcCombatEvent ToEvent(NpcCombatState s, uint senderMs, Func<uint, byte?> ghostOfEid)
    {
        var bits = BodyState2Bits.None;
        if (s.Combat) bits |= BodyState2Bits.CombatMode;
        if (s.Block) bits |= BodyState2Bits.BlockHeld;
        if (s.OpponentIsPlayer || s.OpponentEid != 0) bits |= BodyState2Bits.Locked;
        var st = new BodyState2(0, 0, bits, Protocol.ZoneFromTableId(s.GuardZone), Protocol.StanceFromTableId(s.GuardStance),
                                Protocol.ZoneFromTableId(s.AtkZone), 0, 0, 0);
        NpcCombatTarget t = NpcCombatTarget.None;
        byte g = 0;
        if (s.OpponentIsPlayer) t = NpcCombatTarget.Host;
        else if (s.OpponentEid != 0)
        {
            if (ghostOfEid(s.OpponentEid) is byte gid) { t = NpcCombatTarget.Avatar; g = gid; }
            else t = NpcCombatTarget.Other;
        }
        return new NpcCombatEvent(senderMs, st, t, g, s.Name);
    }

    public enum EngageVerdict { Engage, Release, Ignore }

    /// <summary>
    /// The joiner's decision for one host NpcCombat event. The copy is engaged
    /// with THIS player only while the host's NPC is in combat and the copy is
    /// the host's (guarded and natively bound -- never a free copy); the DLL
    /// then refuses it past 15 m. Anything else releases an engaged copy.
    /// </summary>
    public static EngageVerdict JudgeEngage(NpcCombatEvent e, bool joinerActive, bool guarded, bool nativeBound, bool engagedNow)
    {
        bool want = joinerActive && e.InCombat && guarded && nativeBound;
        if (want) return EngageVerdict.Engage;
        return engagedNow ? EngageVerdict.Release : EngageVerdict.Ignore;
    }

    /// <summary>No host event for this long: an engaged copy is released (the host's NPC left combat, or its stream stopped).</summary>
    public static readonly TimeSpan EngageStale = TimeSpan.FromSeconds(3);

    /// <summary>The NPCs the host watches: a live, drawn NPC it streams (the engaged / drawn bits of its own npc_state).</summary>
    public static bool HostWatches(byte npcStateFlags) =>
        (npcStateFlags & Protocol.NpcStateFlagDead) == 0 && (npcStateFlags & 0x04) != 0;   // 0x04 = the mod's weapon-drawn bit
}
