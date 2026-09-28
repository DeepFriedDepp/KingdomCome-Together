using System.Buffers.Binary;
using System.Text;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-141 -- activities (docs/WO-141-findings.md).
//
// Sync what the activity is, not the animation: "this body is in this stance on
// this object" (a bed, a seat, a kneeler, a cart slot) and "this body is in this
// unstance at this location object" (a lean, a guard post, a workbench), read
// from the game's own NPC-state context and applied on the other machine by the
// same state machine (docs/WO-141-findings.md s1). Objects travel as their level
// entity GUID -- the same on every machine that loaded the level.
//
// Two messages ride the WO-123 join channel (same header, same routing, rows in
// Protocol.JoinWire -- the relay needs no code of its own):
//
//   type up/down  name           from     body (after [target:1][joinId:4])
//   0x6A / 0x6B   ActivityHost   host     [kind:1][count:1]{row}   the host's NPCs (kind 1) and
//                                                                   players' own bodies (kind 2: the
//                                                                   host's, and each joiner's, forwarded)
//   0x6C / 0x6D   ActivityPeer   joiner   [kind=2][count=1]{row}   the joiner's own body, to the host
//
//   row = [peer:1][nameLen:1][name:0..64][activity:30]
//         kind 1: peer 0xFF, name = the NPC (1..64 ASCII)
//         kind 2: peer = the body's owner (its ghost id), name empty
//   activity = [stance:1][cart:1][stanceObj:8][unstance:2][unstanceObj:8]
//              [minigame:1][minigameObj:8][flags:1]   little-endian
//              stance 0 none, 2 lying, 3 sitting, 4 kneel, 6 crouch, 7 cart;
//              unstance 0xFFFF none (the game's unstance database index);
//              minigame 0xFF none; flags bit 0 = the activity owns the body's position
//
// No protocol bump: new types on the join channel. A mixed release is refused
// at the relay (WO-110 R9), so a peer that does not know them never shares a
// session with one that sends them.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte ActivityHostUp = 0x6A, ActivityHostDown = 0x6B;   // WO-141
    public const byte ActivityPeerUp = 0x6C, ActivityPeerDown = 0x6D;   // WO-141

    public const byte ActivityKindNpc = 1, ActivityKindPlayer = 2;
    public const int ActivityBytes = 30;
    public const int ActivityMaxRows = 12;
    public const int ActivityRowMin = 2 + ActivityBytes;                          // [peer][nameLen=0][30]
    public const int ActivityRowMax = 2 + MaxNpcNameLen + ActivityBytes;
    public const int ActivityBodyMin = 2 + ActivityRowMin;                        // [kind][count] + one row
    public const int ActivityBodyMax = 2 + ActivityMaxRows * ActivityRowMax;
    public const byte ActivityPeerNone = 0xFF;
}

/// <summary>WO-141: one body's activity (objects as level entity GUIDs).</summary>
public readonly record struct ActivityState(byte Stance, byte Cart, ulong StanceObj, ushort Unstance, ulong UnstanceObj,
                                            byte Minigame, ulong MinigameObj, byte Flags)
{
    public const byte Lying = 2, Sitting = 3, Kneel = 4, Crouch = 6, CartStance = 7;
    public const ushort NoUnstance = 0xFFFF;
    public const byte NoMinigame = 0xFF;
    public const byte FlagOwnsPos = 0x01;

    public static ActivityState None => new(0, 0, 0, NoUnstance, 0, NoMinigame, 0, 0);

    public static bool ObjectStance(byte s) => s is Lying or Sitting or Kneel or CartStance;
    public static bool SyncedStance(byte s) => ObjectStance(s) || s == Crouch;

    public bool OwnsPosition => (ObjectStance(Stance) && StanceObj != 0) || (Unstance != NoUnstance && UnstanceObj != 0);
    public bool IsNone { get { var n = Normalised(); return n.Stance == 0 && n.Unstance == NoUnstance && n.Minigame == NoMinigame; } }

    /// <summary>The same rules as native wo141_rules.h normalised().</summary>
    public ActivityState Normalised()
    {
        byte st = SyncedStance(Stance) ? Stance : (byte)0;
        ulong so = st == 0 ? 0 : StanceObj;
        byte cart = st == CartStance ? Cart : (byte)0;
        ulong uo = Unstance == NoUnstance ? 0 : UnstanceObj;
        ulong mo = Minigame == NoMinigame ? 0 : MinigameObj;
        var a = new ActivityState(st, cart, so, Unstance, uo, Minigame, mo, 0);
        return a with { Flags = a.OwnsPosition ? FlagOwnsPos : (byte)0 };
    }

    /// <summary>The body-visible part (what an apply changes).</summary>
    public bool SameBody(ActivityState o)
    {
        var a = Normalised(); var b = o.Normalised();
        return a.Stance == b.Stance && a.StanceObj == b.StanceObj && a.Cart == b.Cart && a.Unstance == b.Unstance && a.UnstanceObj == b.UnstanceObj;
    }

    public void Write(Span<byte> d)
    {
        var a = Normalised();
        d[0] = a.Stance; d[1] = a.Cart;
        BinaryPrimitives.WriteUInt64LittleEndian(d[2..], a.StanceObj);
        BinaryPrimitives.WriteUInt16LittleEndian(d[10..], a.Unstance);
        BinaryPrimitives.WriteUInt64LittleEndian(d[12..], a.UnstanceObj);
        d[20] = a.Minigame;
        BinaryPrimitives.WriteUInt64LittleEndian(d[21..], a.MinigameObj);
        d[29] = a.Flags;
    }

    public byte[] ToBytes() { var b = new byte[Protocol.ActivityBytes]; Write(b); return b; }

    public static bool TryRead(ReadOnlySpan<byte> s, out ActivityState a)
    {
        a = None;
        if (s.Length < Protocol.ActivityBytes) return false;
        a = new ActivityState(s[0], s[1], BinaryPrimitives.ReadUInt64LittleEndian(s[2..]), BinaryPrimitives.ReadUInt16LittleEndian(s[10..]),
                              BinaryPrimitives.ReadUInt64LittleEndian(s[12..]), s[20], BinaryPrimitives.ReadUInt64LittleEndian(s[21..]), s[29]).Normalised();
        return true;
    }

    public static string StanceName(byte s) => s switch
    {
        0 => "none", 1 => "standing", Lying => "lying", Sitting => "sitting", Kneel => "kneel", 5 => "horse", Crouch => "crouch", CartStance => "cart", _ => $"stance-{s}",
    };

    public override string ToString() => FormattableString.Invariant(
        $"{StanceName(Stance)}{(StanceObj != 0 ? $"@{StanceObj:X16}" : "")} unstance={(Unstance == NoUnstance ? "-" : Unstance.ToString())}{(UnstanceObj != 0 ? $"@{UnstanceObj:X16}" : "")}{(Minigame != NoMinigame ? $" minigame={Minigame}" : "")}{(OwnsPosition ? " owns" : "")}");
}

/// <summary>WO-141: one row (a body and its activity).</summary>
public readonly record struct ActivityRow(byte Peer, string Name, ActivityState A);

/// <summary>WO-141: the ActivityHost / ActivityPeer body (after the join header).</summary>
public static class ActivityCodec
{
    public static bool ValidName(string n) => n.Length <= Protocol.MaxNpcNameLen && n.All(c => c >= 0x20 && c <= 0x7E);

    public static byte[] BuildBody(byte kind, IReadOnlyList<ActivityRow> rows)
    {
        if (rows.Count < 1 || rows.Count > Protocol.ActivityMaxRows) throw new ArgumentOutOfRangeException(nameof(rows), $"rows {rows.Count}");
        using var ms = new MemoryStream();
        ms.WriteByte(kind); ms.WriteByte((byte)rows.Count);
        Span<byte> a = stackalloc byte[Protocol.ActivityBytes];
        foreach (var r in rows)
        {
            if (!ValidName(r.Name)) throw new ArgumentException($"activity row name '{r.Name}'");
            if (kind == Protocol.ActivityKindNpc && r.Name.Length == 0) throw new ArgumentException("an NPC row needs a name");
            ms.WriteByte(r.Peer);
            var nb = Encoding.ASCII.GetBytes(r.Name);
            ms.WriteByte((byte)nb.Length); ms.Write(nb);
            r.A.Write(a); ms.Write(a);
        }
        return ms.ToArray();
    }

    public static byte[] BuildUp(byte type, byte target, byte kind, IReadOnlyList<ActivityRow> rows)
        => Protocol.BuildJoinUp(type, target, 0, BuildBody(kind, rows));

    public static bool TryDecode(ReadOnlySpan<byte> body, out byte kind, out List<ActivityRow> rows)
    {
        rows = new List<ActivityRow>(); kind = 0;
        if (body.Length < Protocol.ActivityBodyMin || body.Length > Protocol.ActivityBodyMax) return false;
        kind = body[0];
        int count = body[1];
        if (kind is not (Protocol.ActivityKindNpc or Protocol.ActivityKindPlayer) || count < 1 || count > Protocol.ActivityMaxRows) return false;
        int o = 2;
        for (int i = 0; i < count; i++)
        {
            if (o + 2 > body.Length) return false;
            byte peer = body[o]; int nl = body[o + 1]; o += 2;
            if (nl > Protocol.MaxNpcNameLen || o + nl + Protocol.ActivityBytes > body.Length) return false;
            var nm = body.Slice(o, nl);
            foreach (byte c in nm) if (c < 0x20 || c > 0x7E) return false;
            string name = Encoding.ASCII.GetString(nm); o += nl;
            if (kind == Protocol.ActivityKindNpc && name.Length == 0) return false;
            if (!ActivityState.TryRead(body.Slice(o, Protocol.ActivityBytes), out var a)) return false;
            o += Protocol.ActivityBytes;
            rows.Add(new ActivityRow(peer, name, a));
        }
        return o == body.Length;
    }
}
