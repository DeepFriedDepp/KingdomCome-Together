// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-151's pure rules (docs/WO-151-findings.md): unit-tested in KcdMp.Client.Tests, used by
/// GameBridge.Wo151.cs and the WO-131/141/143 paths it touches.
/// </summary>
public static class Wo151Rules
{
    /// <summary>
    /// 1.1: a copy stays "in a fight" this long after the host's NPC last reported combat or this
    /// joiner last held it in combat mode -- its activity, tools and gait context wait that long, so a
    /// lull between blows does not hand it a hoe.
    /// </summary>
    public const long FightHoldMs = 10_000;

    /// <summary>
    /// WO-153 2: which guarded copies hold the "no own hit reaction" contexts. Only a copy in a fight (the host's NPC in
    /// combat, or held in combat mode here, and for <see cref="FightHoldMs"/> after). 0.43.0 set them on every guarded
    /// copy: 216 in the tutorial run, 4 of which ever fought (the rest villagers and horses).
    /// </summary>
    public static IEnumerable<KeyValuePair<string, uint>> CopiesNeedingContexts(
        IEnumerable<KeyValuePair<string, uint>> guarded, Func<string, bool> inFight) =>
        guarded.Where(kv => kv.Value != 0 && inFight(kv.Key));

    /// <summary>A name the mod's wire carries ([A-Za-z0-9_], 1..64): the fight list goes into a Lua string.</summary>
    public static bool IsWireName(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 64) return false;
        foreach (char c in s)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')) return false;
        return true;
    }

    public enum CatchUpVerdict { Apply, Already, AlreadyPassed, Refused }

    /// <summary>
    /// 3.1: the host's verdict on a joiner's "old -> new" with the State's own history in this world since
    /// its last load (<paramref name="passed"/>: the values it held before its current one). A step to a value
    /// the host has already passed is "already" -- never applied again, never refused: the field's joiner
    /// re-ran deliverWater 3->2 and trackHorse 2->1 (both APPLIED: they put the host's quest back) and recounted
    /// a counter from 0 (12 refusals). A bool goes back and forth by design: only a step to a value it is not
    /// at now and has passed counts as passed.
    /// </summary>
    public static CatchUpVerdict Judge(bool hostOk, int hostVal, int reqOld, int reqNew, bool isBool, IReadOnlyCollection<int>? passed)
    {
        if (!hostOk) return CatchUpVerdict.Refused;
        if (hostVal == reqNew) return CatchUpVerdict.Already;
        bool wasPassed = passed is not null && passed.Contains(reqNew);
        if (hostVal == reqOld) return !isBool && wasPassed ? CatchUpVerdict.AlreadyPassed : CatchUpVerdict.Apply;
        return wasPassed ? CatchUpVerdict.AlreadyPassed : CatchUpVerdict.Refused;
    }

    /// <summary>3.1: how long a joiner waits for the host's first checkpoint part after asking (none = nothing to catch up).</summary>
    public const long CatchUpFirstPartMs = 10_000;
    /// <summary>3.1: the longest a joiner waits to be caught up (an incomplete catch-up is logged as such).</summary>
    public const long CatchUpMaxMs = 60_000;

    /// <summary>
    /// 3.9: a w151_door event's body as Lua formats it -- <c>state|ask name dir flag x y z</c> -- or null
    /// for anything else (the name goes back into a Lua string on the far side: door names only).
    /// </summary>
    public static (bool Ask, DoorEvent Ev)? ParseDoorEvent(string? arg, uint senderMs)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 7 || f[0] is not ("state" or "ask")) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!DoorEvent.IsDoorName(f[1]) || !sbyte.TryParse(f[2], System.Globalization.NumberStyles.AllowLeadingSign, inv, out sbyte dir) || dir is < -1 or > 1
            || f[3] is not ("0" or "1")
            || !float.TryParse(f[4], System.Globalization.NumberStyles.Float, inv, out float x) || !float.IsFinite(x)
            || !float.TryParse(f[5], System.Globalization.NumberStyles.Float, inv, out float y) || !float.IsFinite(y)
            || !float.TryParse(f[6], System.Globalization.NumberStyles.Float, inv, out float z) || !float.IsFinite(z))
            return null;
        return (f[0] == "ask", new DoorEvent(senderMs, dir, f[3] == "1" ? DoorEvent.FlagLocked : (byte)0, x, y, z, f[1]));
    }
}
