using System.Text;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-143 -- activities, part 2 (docs/WO-143-findings.md).
//
// What else the host's NPCs' bodies do, said the game's own way and placed by
// the joiner's game (native wo143.h): the tool in each hand, the gait context,
// the one-shot fragment, and who they look at. The players' own minigames need
// no new message: WO-141's player rows already carry the minigame and its
// object (0x6A / 0x6C); the avatar is shown them on the machine that receives
// those rows.
//
// One message rides the WO-123 join channel (same header, same routing, its
// row in Protocol.JoinWire -- the relay needs no code of its own):
//
//   type up/down  name              from   body (after [target:1][joinId:4])
//   0x6E / 0x6F   ActivityExtra     host   [kind:1][count:1]{row}
//
//   kind 1 hands    row = [nameLen][name][left:16][right:16]          item class ids; zero = an empty hand
//   kind 2 gaits    row = [nameLen][name][mask:2]                     bit i = actorCondition_* context i (wo143_rules.h)
//   kind 3 one-shot row = [nameLen][name][fragLen][frag][tagsLen][tags][alignGuid:8][flags:1]   (count = 1)
//                         the align object as its level entity GUID (0 = in place); flags bit 0 = aligned
//   kind 4 looks    row = [nameLen][name][targetKind:1][targetLen][target]
//                         0 nobody, 1 the host's own player, 2 a player's avatar (target = its ghost id), 3 an NPC
//
// No protocol bump: a new type on the join channel. A mixed release is refused
// at the relay (WO-110 R9), so a peer that does not know it never shares a
// session with one that sends it.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte ActivityExtraUp = 0x6E, ActivityExtraDown = 0x6F;   // WO-143

    public const byte ExtraKindHands = 1, ExtraKindGaits = 2, ExtraKindOneShot = 3, ExtraKindLooks = 4;
    public const int ExtraMaxRows = 12;
    public const int ExtraFragMax = 95, ExtraTagsMax = 159;
    public const int ExtraBodyMin = 2 + 1 + 1 + 2;                                    // [kind][count] + a 1-char name + a gait mask
    public const int ExtraBodyMax = 2 + ExtraMaxRows * (1 + MaxNpcNameLen + 1 + 1 + MaxNpcNameLen);   // the widest kind (looks: two names)

    public const byte LookNobody = 0, LookHostPlayer = 1, LookPeer = 2, LookNpc = 3;
}

/// <summary>WO-143: one extra row (only the fields of its kind are used).</summary>
public sealed record ExtraRow
{
    public string Name { get; init; } = "";
    public byte[] Left { get; init; } = new byte[16];
    public byte[] Right { get; init; } = new byte[16];
    public ushort Gaits { get; init; }
    public string Fragment { get; init; } = "";
    public string Tags { get; init; } = "";
    public ulong AlignGuid { get; init; }
    public byte Flags { get; init; }
    public byte TargetKind { get; init; }
    public string Target { get; init; } = "";

    public bool HandsEmpty => Left.All(b => b == 0) && Right.All(b => b == 0);

    /// <summary>A class id the way Lua's ItemManager.GetItem(..).class prints it (a Windows GUID in memory).</summary>
    public static string ClassText(byte[] c) => c.All(b => b == 0) ? "-" : new Guid(c).ToString("D");
    public static byte[] ClassBytes(string text) => Guid.TryParse(text, out var g) ? g.ToByteArray() : new byte[16];
}

/// <summary>WO-143: the ActivityExtra body (after the join header).</summary>
public static class ExtraCodec
{
    static bool ValidText(string s, int max) => s.Length <= max && s.All(c => c >= 0x20 && c <= 0x7E);

    static void PutText(MemoryStream ms, string s, int max)
    {
        if (!ValidText(s, max)) throw new ArgumentException($"text '{s}'");
        var b = Encoding.ASCII.GetBytes(s);
        ms.WriteByte((byte)b.Length); ms.Write(b);
    }

    public static byte[] BuildBody(byte kind, IReadOnlyList<ExtraRow> rows)
    {
        if (rows.Count < 1 || rows.Count > Protocol.ExtraMaxRows) throw new ArgumentOutOfRangeException(nameof(rows), $"rows {rows.Count}");
        if (kind == Protocol.ExtraKindOneShot && rows.Count != 1) throw new ArgumentException("a one-shot travels alone");
        using var ms = new MemoryStream();
        ms.WriteByte(kind); ms.WriteByte((byte)rows.Count);
        foreach (var r in rows)
        {
            if (r.Name.Length == 0) throw new ArgumentException("a row needs a name");
            PutText(ms, r.Name, Protocol.MaxNpcNameLen);
            switch (kind)
            {
                case Protocol.ExtraKindHands:
                    if (r.Left.Length != 16 || r.Right.Length != 16) throw new ArgumentException("class ids are 16 bytes");
                    ms.Write(r.Left); ms.Write(r.Right);
                    break;
                case Protocol.ExtraKindGaits:
                    ms.WriteByte((byte)(r.Gaits & 0xFF)); ms.WriteByte((byte)(r.Gaits >> 8));
                    break;
                case Protocol.ExtraKindOneShot:
                    if (r.Fragment.Length == 0) throw new ArgumentException("a one-shot needs its fragment");
                    PutText(ms, r.Fragment, Protocol.ExtraFragMax);
                    PutText(ms, r.Tags, Protocol.ExtraTagsMax);
                    for (int i = 0; i < 8; i++) ms.WriteByte((byte)(r.AlignGuid >> (8 * i)));
                    ms.WriteByte(r.Flags);
                    break;
                case Protocol.ExtraKindLooks:
                    if (r.TargetKind > Protocol.LookNpc) throw new ArgumentException($"look target kind {r.TargetKind}");
                    ms.WriteByte(r.TargetKind);
                    PutText(ms, r.Target, Protocol.MaxNpcNameLen);
                    break;
                default: throw new ArgumentException($"extra kind {kind}");
            }
        }
        return ms.ToArray();
    }

    public static byte[] BuildUp(byte target, byte kind, IReadOnlyList<ExtraRow> rows)
        => Protocol.BuildJoinUp(Protocol.ActivityExtraUp, target, 0, BuildBody(kind, rows));

    static bool GetText(ReadOnlySpan<byte> b, ref int o, int max, bool allowEmpty, out string s)
    {
        s = "";
        if (o >= b.Length) return false;
        int n = b[o++];
        if (n > max || o + n > b.Length || (!allowEmpty && n == 0)) return false;
        var t = b.Slice(o, n);
        foreach (byte c in t) if (c < 0x20 || c > 0x7E) return false;
        s = Encoding.ASCII.GetString(t); o += n;
        return true;
    }

    public static bool TryDecode(ReadOnlySpan<byte> body, out byte kind, out List<ExtraRow> rows)
    {
        rows = new List<ExtraRow>(); kind = 0;
        if (body.Length < Protocol.ExtraBodyMin || body.Length > Protocol.ExtraBodyMax) return false;
        kind = body[0];
        int count = body[1];
        if (kind is < Protocol.ExtraKindHands or > Protocol.ExtraKindLooks || count < 1 || count > Protocol.ExtraMaxRows) return false;
        if (kind == Protocol.ExtraKindOneShot && count != 1) return false;
        int o = 2;
        for (int i = 0; i < count; i++)
        {
            if (!GetText(body, ref o, Protocol.MaxNpcNameLen, false, out var name)) return false;
            switch (kind)
            {
                case Protocol.ExtraKindHands:
                    if (o + 32 > body.Length) return false;
                    rows.Add(new ExtraRow { Name = name, Left = body.Slice(o, 16).ToArray(), Right = body.Slice(o + 16, 16).ToArray() });
                    o += 32;
                    break;
                case Protocol.ExtraKindGaits:
                    if (o + 2 > body.Length) return false;
                    rows.Add(new ExtraRow { Name = name, Gaits = (ushort)(body[o] | (body[o + 1] << 8)) });
                    o += 2;
                    break;
                case Protocol.ExtraKindOneShot:
                {
                    if (!GetText(body, ref o, Protocol.ExtraFragMax, false, out var frag)) return false;
                    if (!GetText(body, ref o, Protocol.ExtraTagsMax, true, out var tags)) return false;
                    if (o + 9 > body.Length) return false;
                    ulong g = 0; for (int k = 7; k >= 0; k--) g = (g << 8) | body[o + k];
                    rows.Add(new ExtraRow { Name = name, Fragment = frag, Tags = tags, AlignGuid = g, Flags = body[o + 8] });
                    o += 9;
                    break;
                }
                case Protocol.ExtraKindLooks:
                {
                    if (o >= body.Length) return false;
                    byte tk = body[o++];
                    if (tk > Protocol.LookNpc) return false;
                    if (!GetText(body, ref o, Protocol.MaxNpcNameLen, true, out var target)) return false;
                    rows.Add(new ExtraRow { Name = name, TargetKind = tk, Target = target });
                    break;
                }
            }
        }
        return o == body.Length;
    }
}
