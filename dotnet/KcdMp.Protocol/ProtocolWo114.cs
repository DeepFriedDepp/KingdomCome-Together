using System.Buffers.Binary;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-114 -- the leash (docs/WO-114-findings.md). Protocol v10.
//
// The host decides: it has both positions and the world is only alive around
// it. Two messages ride the WO-123 join channel (same header, same routing,
// rows in Protocol.JoinWire, so the relay's gate needs no code of its own):
//
//   type up/down  name         up body (after [target:1][joinId:4])                          sent by
//   0x58 / 0x59   Leash        [kind:1][seq:1][arg:2][hostX:4f][hostY:4f][hostZ:4f][distM:2] (23) host -> one joiner
//   0x5A / 0x5B   LeashState   [flags:2][pullSeq:1][result:1][fromM:2][toM:2][residualCm:2] (15) joiner -> the host
//
// joinId is 0 on both (unused). A v9 relay would drop both types (unknown),
// so this WO bumps the protocol to 10: v9 and v10 refuse each other at
// Handshake, the WO-123 way.
//
// Leash kinds (APPEND-ONLY):
//   1 Warn       the joiner passed mp_leash_warn_m; arg = the warn distance
//   2 Countdown  arg = seconds left (10..1), sent once a second while it runs
//   3 Cancel     back inside mp_leash_pull_m during the countdown
//   4 Pull       teleport beside (hostX, hostY, hostZ) now; arg = LeashReason*
//   5 Config     the host's leash settings: arg = warn m, distM = pull m,
//                seq bit 0 = mp_leash on (the joiner's respawn filter reads it)
//   6 Hold       the countdown is paused; arg = seconds left (once per pause)
// distM = the host's view of the distance (metres, clamped to u16).
//
// LeashState flags: the joiner's own reasons not to be pulled, plus what it
// knows about itself. Sent once a second while connected and at once on a
// change or after a pull (result = LeashResult*, for pull pullSeq).
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte LeashUp        = 0x58, LeashDown      = 0x59;   // WO-114
    public const byte LeashStateUp   = 0x5A, LeashStateDown = 0x5B;   // WO-114

    public const int LeashBodyLen = 1 + 1 + 2 + 12 + 2;
    public const int LeashStateBodyLen = 2 + 1 + 1 + 2 + 2 + 2;

    // ---- Leash kinds (APPEND-ONLY) ----
    public const byte LeashKindWarn = 1, LeashKindCountdown = 2, LeashKindCancel = 3, LeashKindPull = 4,
                      LeashKindConfig = 5, LeashKindHold = 6;

    // ---- Pull reasons (APPEND-ONLY) ----
    public const ushort LeashReasonDistance = 1, LeashReasonFastTravel = 2;

    // ---- LeashState flags ----
    public const ushort LeashFlagInWorld  = 0x0001,   // this game's world is the host's (joined)
                        LeashFlagDowned   = 0x0002,   // downed / respawning (WO-113)
                        LeashFlagLoading  = 0x0004,   // a join or a load in progress
                        LeashFlagCutscene = 0x0008,
                        LeashFlagDialogue = 0x0010,
                        LeashFlagMenu     = 0x0020,   // a menu or the emitter silent (WO-99)
                        LeashFlagMounted  = 0x0040,
                        LeashFlagFastTravelRefused = 0x0080;   // the joiner tried to fast travel (count bumps)

    // ---- Pull results (APPEND-ONLY) ----
    public const byte LeashResultNone = 0, LeashResultPlaced = 1, LeashResultBusy = 2, LeashResultNotPlaced = 3,
                      LeashResultNoPlugin = 4, LeashResultMounted = 5, LeashResultNoHost = 6;

    public static string LeashKindName(byte k) => k switch
    {
        LeashKindWarn => "warn", LeashKindCountdown => "countdown", LeashKindCancel => "cancel", LeashKindPull => "pull",
        LeashKindConfig => "config", LeashKindHold => "hold", _ => $"unknown-{k}",
    };

    public static string LeashResultName(byte r) => r switch
    {
        LeashResultNone => "none", LeashResultPlaced => "placed", LeashResultBusy => "busy", LeashResultNotPlaced => "not-placed",
        LeashResultNoPlugin => "no-plugin", LeashResultMounted => "still-mounted", LeashResultNoHost => "no-host", _ => $"unknown-{r}",
    };

    public static string LeashFlagsText(ushort f)
    {
        var parts = new List<string>();
        if ((f & LeashFlagInWorld) != 0) parts.Add("in-world");
        if ((f & LeashFlagDowned) != 0) parts.Add("downed");
        if ((f & LeashFlagLoading) != 0) parts.Add("loading");
        if ((f & LeashFlagCutscene) != 0) parts.Add("cutscene");
        if ((f & LeashFlagDialogue) != 0) parts.Add("dialogue");
        if ((f & LeashFlagMenu) != 0) parts.Add("menu");
        if ((f & LeashFlagMounted) != 0) parts.Add("mounted");
        if ((f & LeashFlagFastTravelRefused) != 0) parts.Add("fast-travel-refused");
        return parts.Count == 0 ? "none" : string.Join(',', parts);
    }
}

/// <summary>WO-114: the host's leash message to one joiner (body after [target][joinId]).</summary>
public readonly record struct LeashCommand(byte Kind, byte Seq, ushort Arg, float HostX, float HostY, float HostZ, ushort DistM)
{
    public byte[] Build(byte target)
    {
        var b = new byte[Protocol.LeashBodyLen];
        b[0] = Kind; b[1] = Seq;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), Arg);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(4), HostX);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8), HostY);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(12), HostZ);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(16), DistM);
        return Protocol.BuildJoinUp(Protocol.LeashUp, target, 0, b);
    }

    public static bool TryDecode(ReadOnlySpan<byte> body, out LeashCommand c)
    {
        c = default;
        if (body.Length != Protocol.LeashBodyLen) return false;
        float x = BinaryPrimitives.ReadSingleLittleEndian(body[4..]), y = BinaryPrimitives.ReadSingleLittleEndian(body[8..]),
              z = BinaryPrimitives.ReadSingleLittleEndian(body[12..]);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return false;
        c = new LeashCommand(body[0], body[1], BinaryPrimitives.ReadUInt16LittleEndian(body[2..]), x, y, z,
                             BinaryPrimitives.ReadUInt16LittleEndian(body[16..]));
        return true;
    }

    public static ushort Metres(double m) => (ushort)Math.Clamp(Math.Round(double.IsFinite(m) ? m : 0), 0, ushort.MaxValue);
}

/// <summary>WO-114: the joiner's leash state for the host (body after [target][joinId]).</summary>
public readonly record struct LeashState(ushort Flags, byte PullSeq, byte Result, ushort FromM, ushort ToM, ushort ResidualCm)
{
    public byte[] Build()
    {
        var b = new byte[Protocol.LeashStateBodyLen];
        BinaryPrimitives.WriteUInt16LittleEndian(b, Flags);
        b[2] = PullSeq; b[3] = Result;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), FromM);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6), ToM);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), ResidualCm);
        return Protocol.BuildJoinUp(Protocol.LeashStateUp, Protocol.JoinTargetHost, 0, b);
    }

    public static bool TryDecode(ReadOnlySpan<byte> body, out LeashState s)
    {
        s = default;
        if (body.Length != Protocol.LeashStateBodyLen) return false;
        s = new LeashState(BinaryPrimitives.ReadUInt16LittleEndian(body), body[2], body[3],
                           BinaryPrimitives.ReadUInt16LittleEndian(body[4..]), BinaryPrimitives.ReadUInt16LittleEndian(body[6..]),
                           BinaryPrimitives.ReadUInt16LittleEndian(body[8..]));
        return true;
    }
}
