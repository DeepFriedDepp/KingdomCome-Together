// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>WO-218 A: the frames' rules -- the queue's pressure (A.2) and the activity lane's coalescing (A.1). Pure; unit-tested.</summary>
public static partial class Wo218Rules
{
    // ---------------------------------------------------------------- A.2 the queue's pressure

    public const int ShedLagMs = 500, ShedRateLagMs = 1000, ShedMidLagMs = 2000;
    public const double FarM = 40.0, MidM = 20.0;
    public const long FarEveryMs = 1000, MidEveryMs = 1000, FarEveryHardMs = 3000;

    /// <summary>0 none; 1 over 500 ms: a superseded far state is dropped; 2 over 1 s: and a far body at most once a second; 3 over 2 s:
    /// far every 3 s, the 20-40 m band superseded and once a second.</summary>
    public static int ShedLevel(long lagMs) => lagMs > ShedMidLagMs ? 3 : lagMs > ShedRateLagMs ? 2 : lagMs > ShedLagMs ? 1 : 0;

    public enum Shed { Keep, Superseded, FarRate, Mid }

    /// <summary>One body's state at dequeue under <paramref name="level"/>: its distance to the nearest player, whether a newer one of it
    /// already waits, and how long since one of it was let through.</summary>
    public static Shed ShedVerdict(int level, double distM, bool superseded, long sinceAppliedMs)
    {
        if (level <= 0 || double.IsNaN(distM)) return Shed.Keep;
        if (distM > FarM)
        {
            if (superseded) return Shed.Superseded;
            if (level >= 3 && sinceAppliedMs < FarEveryHardMs) return Shed.FarRate;
            if (level >= 2 && sinceAppliedMs < FarEveryMs) return Shed.FarRate;
            return Shed.Keep;
        }
        if (level >= 3 && distM > MidM && (superseded || sinceAppliedMs < MidEveryMs)) return Shed.Mid;
        return Shed.Keep;
    }

    /// <summary>The body names of an NpcAnim payload ([count] then [nameLen][name][cm][nsc] nsc x 34); empty when malformed.</summary>
    public static List<string> NpcAnimNames(byte[] body)
    {
        var names = new List<string>();
        if (body.Length < 1) return names;
        int count = body[0], o = 1;
        for (int i = 0; i < count; i++)
        {
            if (o + 1 > body.Length) return [];
            int nl = body[o];
            if (nl == 0 || o + 1 + nl + 2 > body.Length) return [];
            names.Add(System.Text.Encoding.ASCII.GetString(body, o + 1, nl));
            o += 1 + nl;
            int nsc = body[o + 1];
            o += 2 + nsc * Protocol.NpcAnimScopeLen;
            if (o > body.Length) return [];
        }
        return o == body.Length ? names : [];
    }

    /// <summary>The NpcAnim payload with only the bodies <paramref name="keep"/> keeps (each asked once, in order); null when malformed
    /// or when every body is kept (the payload unchanged).</summary>
    public static byte[]? FilterNpcAnim(byte[] body, Func<string, bool> keep)
    {
        if (body.Length < 1) return null;
        int count = body[0], o = 1, kept = 0;
        var ms = new MemoryStream();
        ms.WriteByte(0);
        for (int i = 0; i < count; i++)
        {
            if (o + 1 > body.Length) return null;
            int nl = body[o];
            if (nl == 0 || o + 1 + nl + 2 > body.Length) return null;
            string name = System.Text.Encoding.ASCII.GetString(body, o + 1, nl);
            int nsc = body[o + 1 + nl + 1];
            int len = 1 + nl + 2 + nsc * Protocol.NpcAnimScopeLen;
            if (o + len > body.Length) return null;
            if (keep(name)) { ms.Write(body, o, len); kept++; }
            o += len;
        }
        if (o != body.Length || kept == count) return null;
        var r = ms.ToArray();
        r[0] = (byte)kept;
        return r;
    }

    // ---------------------------------------------------------------- A.1 the activity lane

    /// <summary>One row of the activity lane: an activity row (0x6B) or an extra (0x6F), with its frame's read time.</summary>
    public readonly record struct ActUnit(int Type, byte Src, byte Kind, string Name, byte Peer, ActivityRow? Act, ExtraRow? Extra, long Arrival)
    {
        /// <summary>The body the row is about (an NPC's name, or a player's avatar).</summary>
        public string Body => Type == Protocol.ActivityHostDown && Kind == Protocol.ActivityKindPlayer ? "peer:" + Peer : Name;

        /// <summary>A state (latest wins per body and kind) -- every kind but a one-shot.</summary>
        public bool IsState => !(Type == Protocol.ActivityExtraDown && Kind == Protocol.ExtraKindOneShot);
    }

    /// <summary>
    /// The waiting rows as one: each body's newest row of a state kind (at the place of its first row), every one-shot in its order.
    /// </summary>
    public static List<ActUnit> CoalesceActivity(IReadOnlyList<ActUnit> units)
    {
        var keyAt = new Dictionary<(int, byte, string), int>();
        var outList = new List<ActUnit>(units.Count);
        foreach (var u in units)
        {
            if (!u.IsState) { outList.Add(u); continue; }
            var key = (u.Type, u.Kind, u.Body);
            if (keyAt.TryGetValue(key, out int at)) outList[at] = u;
            else { keyAt[key] = outList.Count; outList.Add(u); }
        }
        return outList;
    }
}
