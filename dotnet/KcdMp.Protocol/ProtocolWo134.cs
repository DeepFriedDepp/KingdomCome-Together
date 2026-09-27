using System.Buffers.Binary;
using System.Text;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-134 -- world items (docs/WO-134-findings.md).
//
// Two messages ride the WO-123 join channel (same header, same routing, rows in
// Protocol.JoinWire, so the relay's gate needs no code of its own):
//
//   type up/down  name        up body (after [target:1][joinId:4])        sent by
//   0x5C / 0x5D   LootAsk     [kind:1][tok:4][text:1..LootTextMax]       a joiner -> the host
//   0x5E / 0x5F   LootHost    [kind:1][tok:4][text:1..LootTextMax]       the host -> one joiner
//
// joinId is 0 (unused). tok pairs an answer with its request (0 = unsolicited).
// The text is ASCII, space-separated, and is checked field by field on both
// ends (LootText): item classes are GUIDs, names are engine names
// ([A-Za-z0-9_]), numbers are invariant decimals. Nothing a peer sends ever
// reaches a Lua string without passing those checks.
//
// Ask kinds (APPEND-ONLY):
//   1 BodyOpen   "<body>"                         the joiner looks at a host-owned body
//   2 BodyTake   "<body> <cls> <amt> <hp>"        it took that out of its copy
//   3 BodyPut    "<body> <cls> <amt> <hp>"        it put that into its copy
//   4 ItemTake   "<cls> <x> <y> <z> <fromBody>"   it wants this loose world item
// Host kinds (APPEND-ONLY):
//   1 BodyState  "<body> <reason> <flags> <part> <nparts> <items|->"   items = cls:amt:hp[:w],...
//                reason open|update; flags 1 = dead or down here, 2 = no such body; w = worn
//   2 TakeResult "<ok|gone> <body> <cls> <amt>"
//   3 ItemResult "<ok|gone|unknown> <cls> <x> <y> <z>"
//   4 ItemGone   "<cls> <x> <y> <z>"              the host (or another joiner) took it
//   5 Ledger     "<part> <nparts> <rows|->"       the host's chest ledger for this world,
//                rows = container|cls|n|hp|worldT|restockDays;... (after the joiner's Ready)
//
// No protocol bump: two new types on the join channel. A mixed release is
// refused at the relay (WO-110 R9), so a peer that does not know them never
// shares a session with one that sends them.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte LootAskUp  = 0x5C, LootAskDown  = 0x5D;   // WO-134
    public const byte LootHostUp = 0x5E, LootHostDown = 0x5F;   // WO-134

    public const int LootTextMax = 1400;
    public const int LootFixedLen = 1 + 4;

    // ---- ask kinds (APPEND-ONLY) ----
    public const byte LootAskBodyOpen = 1, LootAskBodyTake = 2, LootAskBodyPut = 3, LootAskItemTake = 4;
    // ---- host kinds (APPEND-ONLY) ----
    public const byte LootHostBodyState = 1, LootHostTakeResult = 2, LootHostItemResult = 3, LootHostItemGone = 4, LootHostLedger = 5;

    public static string LootAskName(byte k) => k switch
    {
        LootAskBodyOpen => "body-open", LootAskBodyTake => "body-take", LootAskBodyPut => "body-put", LootAskItemTake => "item-take", _ => $"unknown-{k}",
    };

    public static string LootHostName(byte k) => k switch
    {
        LootHostBodyState => "body-state", LootHostTakeResult => "take-result", LootHostItemResult => "item-result",
        LootHostItemGone => "item-gone", LootHostLedger => "ledger", _ => $"unknown-{k}",
    };
}

/// <summary>WO-134: one LootAsk or LootHost message (the body after the join header).</summary>
public readonly record struct LootMsg(byte Kind, uint Tok, string Text)
{
    public byte[] BuildUp(byte type, byte target)
    {
        var tb = Encoding.ASCII.GetBytes(Text ?? "");
        if (tb.Length == 0 || tb.Length > Protocol.LootTextMax) throw new ArgumentOutOfRangeException(nameof(Text), $"loot text length {tb.Length}");
        foreach (byte c in tb) if (c < 0x20 || c > 0x7E) throw new ArgumentException("loot text must be printable ASCII", nameof(Text));
        int len = Protocol.JoinHeaderLen + Protocol.LootFixedLen + tb.Length;
        var p = new byte[3 + len];
        p[0] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), (ushort)len);
        p[3] = target;                         // joinId [4..7] = 0
        p[8] = Kind;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(9), Tok);
        tb.CopyTo(p, 13);
        return p;
    }

    /// <summary>The body after the join header (a down body with the source id already split off).</summary>
    public static bool TryDecode(ReadOnlySpan<byte> body, out LootMsg m)
    {
        m = default;
        if (body.Length < Protocol.LootFixedLen + 1 || body.Length > Protocol.LootFixedLen + Protocol.LootTextMax) return false;
        var t = body[Protocol.LootFixedLen..];
        foreach (byte c in t) if (c < 0x20 || c > 0x7E) return false;
        m = new LootMsg(body[0], BinaryPrimitives.ReadUInt32LittleEndian(body[1..]), Encoding.ASCII.GetString(t));
        return true;
    }
}
