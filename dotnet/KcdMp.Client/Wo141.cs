using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>WO-141: one row the DLL read (kind 1 a tracked NPC, 2 the local player -- name empty).</summary>
public readonly record struct Wo141DllRow(byte Kind, string Name, ActivityState A);

/// <summary>WO-141: the pipe shapes of native wo141.h (0x27 op bodies, the unsolicited 0xA7).</summary>
public static class Wo141Codec
{
    public const byte KindNpc = 1, KindPlayer = 2;

    /// <summary>0xA7: [count]{[kind][nameLen][name][activity:30]} -- exact consumption.</summary>
    public static bool TryParseFrame(byte[] body, out List<Wo141DllRow> rows)
    {
        rows = new List<Wo141DllRow>();
        if (body.Length < 1) return false;
        int count = body[0], o = 1;
        for (int i = 0; i < count; i++)
        {
            if (o + 2 > body.Length) return false;
            byte kind = body[o]; int nl = body[o + 1]; o += 2;
            if (kind is not (KindNpc or KindPlayer) || nl > 63 || o + nl + Protocol.ActivityBytes > body.Length) return false;
            string name = Encoding.ASCII.GetString(body, o, nl); o += nl;
            if (kind == KindNpc && name.Length == 0) return false;
            if (!ActivityState.TryRead(body.AsSpan(o, Protocol.ActivityBytes), out var a)) return false;
            o += Protocol.ActivityBytes;
            rows.Add(new Wo141DllRow(kind, name, a));
        }
        return o == body.Length;
    }

    public static byte[] Name(string name)
    {
        var nb = Encoding.ASCII.GetBytes(name ?? "");
        if (nb.Length > 63) throw new ArgumentException($"name '{name}' too long for the pipe");
        var p = new byte[1 + nb.Length];
        p[0] = (byte)nb.Length; nb.CopyTo(p, 1);
        return p;
    }

    public static byte[] NameAndActivity(string name, ActivityState a)
    {
        var n = Name(name);
        var p = new byte[n.Length + Protocol.ActivityBytes];
        n.CopyTo(p, 0);
        a.Write(p.AsSpan(n.Length));
        return p;
    }

    /// <summary>A length-prefixed name up to 255 bytes (an object's level entity name runs past the 63 of an NPC's).</summary>
    public static byte[] LongName(string name)
    {
        var nb = Encoding.ASCII.GetBytes(name ?? "");
        if (nb.Length > 255) throw new ArgumentException($"name '{name}' too long for the pipe");
        var p = new byte[1 + nb.Length];
        p[0] = (byte)nb.Length; nb.CopyTo(p, 1);
        return p;
    }

    /// <summary>op 8: [tenths][unstLen][unstance][objLen][object entity name].</summary>
    public static byte[] Show(string unstance, string objectName, byte tenths)
    {
        var u = Name(unstance); var o = LongName(objectName);
        var p = new byte[1 + u.Length + o.Length];
        p[0] = tenths; u.CopyTo(p, 1); o.CopyTo(p, 1 + u.Length);
        return p;
    }

    /// <summary>A 0xA7 frame (tests; the DLL writes these).</summary>
    public static byte[] BuildFrame(IReadOnlyList<Wo141DllRow> rows)
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)rows.Count);
        foreach (var r in rows)
        {
            ms.WriteByte(r.Kind);
            var nb = Encoding.ASCII.GetBytes(r.Name);
            ms.WriteByte((byte)nb.Length); ms.Write(nb);
            ms.Write(r.A.ToBytes());
        }
        return ms.ToArray();
    }
}

/// <summary>
/// WO-141: the engine-free rules of activities (docs/WO-141-findings.md).
/// <see cref="GameBridge"/> feeds them its live flags; Wo141Tests pins them.
/// </summary>
public static class Wo141Rules
{
    /// <summary>The DLL capture mask: bit 0 the tracked NPCs (the host of a shared world), bit 1 this player's own body.</summary>
    public static byte CaptureMask(bool active, bool isHost) => (byte)(!active ? 0 : isHost ? 0b11 : 0b10);

    /// <summary>
    /// Activities run: mp_activities on, a partner, this machine's world is the shared world (the host of one,
    /// or a joiner whose loaded world is the host's), and no world is loading.
    /// </summary>
    public static bool Active(bool on, bool host, bool joiner, bool holding, bool separate) => on && (host || joiner) && !holding && !separate;

    /// <summary>A copy leaves its activity first while the host's NPC fights it, is down, or it talks (the copy's own brain has it).</summary>
    public static string? BlockReason(bool engaged, bool down, bool talking) =>
        down ? "down" : engaged ? "fight" : talking ? "talk" : null;

    /// <summary>What an avatar is given: the crouch rides the state block (WO-136), never the activity.</summary>
    public static ActivityState ForAvatar(ActivityState a) =>
        a.Stance == ActivityState.Crouch ? (a with { Stance = 0, StanceObj = 0 }).Normalised() : a.Normalised();

    public static string AvatarName(byte peer) => $"kcd2mp_{peer}";

    /// <summary>Rows in messages of at most <see cref="Protocol.ActivityMaxRows"/>.</summary>
    public static IEnumerable<List<ActivityRow>> Batches(IReadOnlyList<ActivityRow> rows)
    {
        for (int i = 0; i < rows.Count; i += Protocol.ActivityMaxRows)
            yield return rows.Skip(i).Take(Protocol.ActivityMaxRows).ToList();
    }

    /// <summary>
    /// A one-shot the player plays at an object that his NPC state never holds (the game's ActionTrigger
    /// "Animation" actions), and the NPC unstance that shows it on the other screen at the same object, for
    /// about as long as his lasts. Only the trough's face wash has a match in the game's unstance database
    /// (WashFace, observed 5.2 s): NPCs never use a grindstone, an anvil or a book stand the player's way.
    /// </summary>
    public static (string Unstance, byte Tenths)? OneShot(string action) => action switch
    {
        "WashFace" => ("housekeeper_faceWash", 60),
        _ => null,
    };

    /// <summary>The kinds this build turns on (mp_activities mask): 1 NPC stances, 2 NPC unstances, 4 players.</summary>
    public const int KindStance = 1, KindUnstance = 2, KindPlayer = 4, KindsAll = 7;

    /// <summary>An NPC row reduced to the kinds that are on (a kind that is off becomes "none" for that part).</summary>
    public static ActivityState Filter(ActivityState a, int mask)
    {
        var n = a.Normalised();
        if ((mask & KindStance) == 0) n = n with { Stance = 0, StanceObj = 0, Cart = 0 };
        if ((mask & KindUnstance) == 0) n = n with { Unstance = ActivityState.NoUnstance, UnstanceObj = 0 };
        return n.Normalised();
    }
}
