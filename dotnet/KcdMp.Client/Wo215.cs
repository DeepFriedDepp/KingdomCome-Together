// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>WO-215 (docs/WO-215-findings.md): the agent's pure rules. KcdMp.Client.Tests/Wo215Tests.cs checks them.</summary>
public static class Wo215Rules
{
    /// <summary>D.1/E: the engine's own line for a body whose combat actor it could not set up ("[Error] Combat actor init failed for actor 'X'").</summary>
    public const string GhostMarker = "Combat actor init failed for actor '";

    /// <summary>The actor named in a <see cref="GhostMarker"/> line, or null.</summary>
    public static string? GhostActor(string? line)
    {
        if (line is null) return null;
        int i = line.IndexOf(GhostMarker, StringComparison.Ordinal);
        if (i < 0) return null;
        int s = i + GhostMarker.Length, e = line.IndexOf('\'', s);
        if (e <= s || e - s > 63) return null;
        string n = line[s..e];
        foreach (char c in n) if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')) return null;
        return n;
    }

    /// <summary>The mod's "w215_tune" event: "&lt;corrector cm&gt; &lt;per-frame 0|1|-&gt; [&lt;min ms&gt; &lt;step cm&gt; &lt;max ms&gt; &lt;lead ms&gt;]" ("-" leaves a value
    /// as it is) -> op 153's body: [op][cm:2][pf:1] or [op][cm:2][pf:1][min:2][step:2][max:2][lead:2] (the op byte is the pipe's).</summary>
    public static bool TryParseTune(string? arg, out byte[] body)
    {
        body = [];
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length is not (2 or 6 or 7)) return false;
        static bool U16(string s, int max, out ushort v)
        {
            v = 0xFFFF;
            if (s == "-") return true;
            return ushort.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v <= max;
        }
        if (!U16(f[0], 2000, out ushort cm) || f[1] is not ("0" or "1" or "-")) return false;
        byte pf = f[1] == "-" ? (byte)0xFF : (byte)(f[1] == "1" ? 1 : 0);
        var list = new List<byte>(BitConverter.GetBytes(cm == 0xFFFF ? (ushort)0 : cm)) { pf };
        if (f.Length >= 6)
        {
            int[] max = [5000, 1000, 10000, 5000, 1000];
            for (int i = 0; i < f.Length - 2; i++)
            {
                if (!U16(f[2 + i], max[i], out ushort v)) return false;
                list.AddRange(BitConverter.GetBytes(v));
            }
        }
        body = [.. list];
        return true;
    }

    /// <summary>A ghost is told to the mod at most this often per body (the field: 1,606 lines for one Cuman).</summary>
    public const int GhostTellEveryMs = 5_000;

    /// <summary>A.0's probe: mode 1 order (exact when gait has bit 0x80), 2 stop, 3 follow a line (a new order every frame), 4 release.</summary>
    public readonly record struct Probe(byte Mode, string Name, float X, float Y, float Z, float Yaw, byte Gait, ushort Secs);

    /// <summary>The mod's "w215_probe" event: "&lt;mode&gt; &lt;name&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt; &lt;yaw rad&gt; &lt;gait&gt; &lt;secs&gt;".</summary>
    public static bool TryParseProbe(string? arg, out Probe p)
    {
        p = default;
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 8) return false;
        if (!byte.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte mode) || mode is < 1 or > 4) return false;
        string name = f[1];
        if (name.Length is 0 or > 63) return false;
        var v = new float[4];
        for (int i = 0; i < 4; i++)
            if (!float.TryParse(f[2 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !float.IsFinite(v[i])) return false;
        if (!byte.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte gait) || (gait & 0x7F) > 3) return false;
        if (!ushort.TryParse(f[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort secs) || secs > 600) return false;
        p = new Probe(mode, name, v[0], v[1], v[2], v[3], gait, secs);
        return true;
    }
}
