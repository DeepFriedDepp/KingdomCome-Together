using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KcdMp.Client;

/// <summary>WO-138: one row of the DLL's native NPC sender (pipe 0xA0).</summary>
public readonly record struct Wo138Row(byte Flags, float X, float Y, float Z, float Rot, float Hp, string Name);

/// <summary>WO-138: the DLL's frame meter (pipe 0xA1). World 0 running, 1 slowed, 2 frozen.</summary>
public readonly record struct Wo138WorldState(byte World, ushort ScalePermille, ushort HeldMask)
{
    public bool Frozen => World == 2;
}

/// <summary>WO-138: the op 4 status (native wo138.h Status, 48 bytes).</summary>
public readonly record struct Wo138Status(bool On, byte World, ushort Tracked, ushort Resolved, ushort RowsLastSec, uint RowsTotal,
    uint Ticks, ushort LastTickAgeMs, ushort CostP50Us, ushort CostMaxUs, ushort ScalePermille, ushort Fps, bool GateArmed, bool GateOn,
    uint Declined, uint PauseCalls, ushort HeldMask, ushort LastSource, bool LastPause, bool HoldAll, ushort AttrP50Us, uint Frames)
{
    /// <summary>The sender is on and ticked within the last second (the frame hook is alive).</summary>
    public bool Sending => On && LastTickAgeMs < 1000;
}

/// <summary>The sender's settings (the Lua sender's values, carried by w138_cfg).</summary>
public readonly record struct Wo138Cfg(ushort EmitMs, ushort HeartbeatMs, ushort MoveEpsMm, bool Cull, ushort CullRadiusM, ushort FarBandM, byte EngageM)
{
    public static readonly Wo138Cfg Default = new(100, 2000, 50, true, 60, 0, 12);
}

/// <summary>
/// WO-138: the wire of the native NPC sender and the pause levers (native wo138.h), and
/// the rules the agent applies (docs/WO-138-findings.md). Pure, so the unit tests check
/// exactly what ships.
/// </summary>
public static class Wo138Codec
{
    public const byte OpConfig = 1, OpTrack = 2, OpAnchors = 3, OpStatus = 4, OpLevers = 5, OpHold = 6, OpText = 7, OpRead = 8, OpPause = 9;

    // ---- PauseUp (0x1C) state byte: 0 = running, else the reasons (bits) ----
    public const byte ReasonMenu      = 0x01;   // the ESC menu (or mp_pause)
    public const byte ReasonInventory = 0x02;   // the Apse screens: inventory, map, journal
    public const byte ReasonDialogue  = 0x04;   // this player is in a dialogue (the world clock stands)
    public const byte ReasonCutscene  = 0x08;   // a rendered cutscene
    public const byte ReasonLoad      = 0x10;   // a save is loading
    public const byte ReasonSkipTime  = 0x20;   // sleep / wait / fast travel
    public const byte ReasonFrozen    = 0x40;   // the DLL's meter: this world does not run (whatever the cause)
    public const byte ReasonClock     = 0x80;   // WO-144 3.3: this world's clock stands (Calendar.IsWorldTimePaused): a joiner's stands with it

    public static byte Reasons(bool menu, bool inventory, bool dialogue, bool cutscene, bool loading, bool skipTime, bool frozen) =>
        (byte)((menu ? ReasonMenu : 0) | (inventory ? ReasonInventory : 0) | (dialogue ? ReasonDialogue : 0) |
               (cutscene ? ReasonCutscene : 0) | (loading ? ReasonLoad : 0) | (skipTime ? ReasonSkipTime : 0) |
               (frozen ? ReasonFrozen : 0));

    public static string Describe(byte r)
    {
        if (r == 0) return "running";
        var p = new List<string>();
        if ((r & ReasonMenu) != 0) p.Add("menu");
        if ((r & ReasonInventory) != 0) p.Add("inventory");
        if ((r & ReasonDialogue) != 0) p.Add("dialogue");
        if ((r & ReasonCutscene) != 0) p.Add("cutscene");
        if ((r & ReasonLoad) != 0) p.Add("load");
        if ((r & ReasonSkipTime) != 0) p.Add("skip-time");
        if ((r & ReasonFrozen) != 0) p.Add("frozen");
        if ((r & ReasonClock) != 0) p.Add("clock");
        return string.Join("+", p);
    }

    // ---- the PauseGame sources (CCryAction::PauseGame on this build) ----
    public const int SrcVideoMode = 4, SrcInGameMenu = 7;
    /// <summary>What the gate declines in a session: the ESC menu and a rendered video.</summary>
    public const uint DefaultMask = (1u << SrcInGameMenu) | (1u << SrcVideoMode);

    // ---- the rules ----

    /// <summary>
    /// The joiner holds the host's copies visible (no silence release) while the host
    /// announced a pause, its link is alive (a packet of any kind within
    /// <paramref name="linkTimeoutMs"/>), and the pause is not older than the cap.
    /// </summary>
    public static bool Hold(byte hostReasons, long nowMs, long lastFromHostMs, long pausedSinceMs,
                            int linkTimeoutMs = 6000, long maxHoldMs = 15 * 60 * 1000)
    {
        if (hostReasons == 0 || lastFromHostMs <= 0) return false;
        if (nowMs - lastFromHostMs > linkTimeoutMs) return false;
        return nowMs - pausedSinceMs <= maxHoldMs;
    }

    /// <summary>The levers (no menu stops the world) are on only in a session with a partner here.</summary>
    public static bool LeversOn(bool relayConnected, bool pipeUp, bool partnerFresh, bool switchOn) =>
        relayConnected && pipeUp && partnerFresh && switchOn;

    /// <summary>While the DLL streams, the mod's own npc_state lines are dropped -- except a resync (0x40).</summary>
    public static bool DropLuaLine(bool nativeActive, byte flags) => nativeActive && (flags & 0x40) == 0;

    // ---- the mod's lines ----

    /// <summary>"gen part parts name:f,name:f,..." ("-" = an empty set).</summary>
    public static bool TryParseTrack(string arg, out ushort gen, out int part, out int parts, out List<(string Name, byte Flags)> names)
    {
        gen = 0; part = parts = 0; names = [];
        var f = arg.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 4 || !ushort.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out gen)
            || !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out part)
            || !int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out parts)
            || parts < 1 || parts > 255 || part >= parts) return false;
        if (f[3] == "-") return true;
        foreach (var item in f[3].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int c = item.LastIndexOf(':');
            string n = c > 0 ? item[..c] : item;
            byte fl = c > 0 && byte.TryParse(item[(c + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var b) ? b : (byte)0;
            if (n.Length == 0 || n.Length > 63) return false;
            foreach (char ch in n) if (!(char.IsAsciiLetterOrDigit(ch) || ch == '_')) return false;
            names.Add((n, fl));
        }
        return true;
    }

    /// <summary>"emitMs heartbeatMs epsMm cull cullRadiusM farBandM engageM".</summary>
    public static bool TryParseCfg(string arg, out Wo138Cfg cfg)
    {
        cfg = Wo138Cfg.Default;
        var f = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 7) return false;
        var v = new ushort[7];
        for (int i = 0; i < 7; i++)
            if (!ushort.TryParse(f[i], NumberStyles.None, CultureInfo.InvariantCulture, out v[i])) return false;
        if (v[0] < 20 || v[1] < 200 || v[6] > 255) return false;
        cfg = new Wo138Cfg(v[0], v[1], v[2], v[3] != 0, v[4], v[5], (byte)v[6]);
        return true;
    }

    // ---- pipe bodies (after the op byte) ----

    public static byte[] ConfigBody(bool on, Wo138Cfg c, ushort attrMs = 200)
    {
        var b = new byte[15];
        b[0] = (byte)(on ? 1 : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1), c.EmitMs);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(3), c.HeartbeatMs);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(5), c.MoveEpsMm);
        b[7] = (byte)(c.Cull ? 1 : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), c.CullRadiusM);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(10), c.FarBandM);
        b[12] = c.EngageM;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(13), attrMs);
        return b;
    }

    /// <summary>The Track op in parts of at most <paramref name="maxBytes"/> (one generation).</summary>
    public static List<byte[]> TrackBodies(ushort gen, IReadOnlyList<(string Name, byte Flags)> names, int maxBytes = 900)
    {
        var groups = new List<List<(string, byte)>> { new() };
        int size = 5;
        foreach (var n in names)
        {
            int need = 2 + Encoding.ASCII.GetByteCount(n.Name);
            if (size + need > maxBytes || groups[^1].Count == 255) { groups.Add(new()); size = 5; }
            groups[^1].Add(n); size += need;
        }
        var bodies = new List<byte[]>();
        for (int i = 0; i < groups.Count; i++)
        {
            var ms = new MemoryStream();
            Span<byte> h = stackalloc byte[5];
            BinaryPrimitives.WriteUInt16LittleEndian(h, gen);
            h[2] = (byte)i; h[3] = (byte)groups.Count; h[4] = (byte)groups[i].Count;
            ms.Write(h);
            foreach (var (name, fl) in groups[i])
            {
                var nb = Encoding.ASCII.GetBytes(name);
                ms.WriteByte(fl); ms.WriteByte((byte)nb.Length); ms.Write(nb);
            }
            bodies.Add(ms.ToArray());
        }
        return bodies;
    }

    public static byte[] AnchorsBody(IReadOnlyList<(float X, float Y, float Z)> anchors)
    {
        int n = Math.Min(anchors.Count, 8);
        var b = new byte[1 + n * 12];
        b[0] = (byte)n;
        for (int i = 0; i < n; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(1 + i * 12), anchors[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(5 + i * 12), anchors[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(9 + i * 12), anchors[i].Z);
        }
        return b;
    }

    public static byte[] LeversBody(bool on, uint mask)
    {
        var b = new byte[5];
        b[0] = (byte)(on ? 1 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(1), mask);
        return b;
    }

    // ---- the DLL's frames ----

    /// <summary>0xA0: [count:1]{[flags:1][x:4f][y:4f][z:4f][rot:4f][hp:4f][nameLen:1][name]}.</summary>
    public static bool TryParseStream(ReadOnlySpan<byte> b, out List<Wo138Row> rows)
    {
        rows = [];
        if (b.Length < 1) return false;
        int n = b[0], o = 1;
        for (int i = 0; i < n; i++)
        {
            if (o + 22 > b.Length) return false;
            int nl = b[o + 21];
            if (nl == 0 || nl > 63 || o + 22 + nl > b.Length) return false;
            rows.Add(new Wo138Row(b[o],
                BinaryPrimitives.ReadSingleLittleEndian(b[(o + 1)..]), BinaryPrimitives.ReadSingleLittleEndian(b[(o + 5)..]),
                BinaryPrimitives.ReadSingleLittleEndian(b[(o + 9)..]), BinaryPrimitives.ReadSingleLittleEndian(b[(o + 13)..]),
                BinaryPrimitives.ReadSingleLittleEndian(b[(o + 17)..]), Encoding.ASCII.GetString(b.Slice(o + 22, nl))));
            o += 22 + nl;
        }
        return o == b.Length;
    }

    /// <summary>0xA1: [world:1][scalePermille:2][heldMask:2].</summary>
    public static bool TryParseWorld(ReadOnlySpan<byte> b, out Wo138WorldState w)
    {
        w = default;
        if (b.Length < 5 || b[0] > 2) return false;
        w = new Wo138WorldState(b[0], BinaryPrimitives.ReadUInt16LittleEndian(b[1..]), BinaryPrimitives.ReadUInt16LittleEndian(b[3..]));
        return true;
    }

    public static bool TryParseStatus(ReadOnlySpan<byte> b, out Wo138Status s)
    {
        s = default;
        if (b.Length < 48) return false;
        s = new Wo138Status(b[0] != 0, b[1], U16(b, 2), U16(b, 4), U16(b, 6), U32(b, 8), U32(b, 12), U16(b, 16), U16(b, 18),
            U16(b, 20), U16(b, 22), U16(b, 24), b[26] != 0, b[27] != 0, U32(b, 28), U32(b, 32), U16(b, 36), U16(b, 38),
            b[40] != 0, b[41] != 0, U16(b, 42), U32(b, 44));
        return true;
    }

    static ushort U16(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b[o..]);
    static uint U32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b[o..]);
}
