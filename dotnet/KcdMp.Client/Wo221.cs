// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;

namespace KcdMp.Client;

/// <summary>WO-221's pure rules (unit-tested in Wo221Tests).</summary>
public static partial class Wo221Rules
{
    /// <summary>"name=id,name=id" (or "-" / empty): names of letters, digits and underscores, ids 0..999999. Anything else is left out.</summary>
    public static List<KeyValuePair<string, int>> ParseKeepers(string? csv)
    {
        var o = new List<KeyValuePair<string, int>>();
        if (string.IsNullOrWhiteSpace(csv) || csv == "-") return o;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = item.IndexOf('=');
            if (eq <= 0) continue;
            string name = item[..eq];
            if (name.Length > 59 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) continue;
            if (!int.TryParse(item[(eq + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id > 999_999) continue;
            if (seen.Add(name)) o.Add(new(name, id));
        }
        return o;
    }

    public static string KeepersCsv(IEnumerable<KeyValuePair<string, int>> pairs) =>
        string.Join(",", pairs.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => FormattableString.Invariant($"{p.Key}={p.Value}")));

    /// <summary>The list as message texts "part parts csv" of at most <paramref name="maxChars"/> characters of csv each ("-" for none).</summary>
    public static List<string> KeepersTexts(IReadOnlyCollection<KeyValuePair<string, int>> pairs, int maxChars = 900)
    {
        var parts = new List<List<string>>();
        var cur = new List<string>(); int len = 0;
        foreach (var p in pairs.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            string s = FormattableString.Invariant($"{p.Key}={p.Value}");
            if (cur.Count > 0 && len + s.Length + 1 > maxChars) { parts.Add(cur); cur = new(); len = 0; }
            cur.Add(s); len += s.Length + 1;
        }
        if (cur.Count > 0 || parts.Count == 0) parts.Add(cur);
        var texts = new List<string>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
            texts.Add(FormattableString.Invariant($"{i + 1} {parts.Count} {(parts[i].Count == 0 ? "-" : string.Join(",", parts[i]))}"));
        return texts;
    }

    public static bool TryParseKeepersPart(string? text, out int part, out int nparts, out List<KeyValuePair<string, int>> pairs)
    {
        part = nparts = 0; pairs = new();
        var f = (text ?? "").Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 3 || !int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out part) ||
            !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out nparts) || part < 1 || nparts < 1 || part > nparts || nparts > 50) return false;
        pairs = ParseKeepers(f[2]);
        return f[2] == "-" || pairs.Count > 0;
    }

    /// <summary>The DLL's op 192: [shop db id:4][name len:1][name].</summary>
    public static byte[] OpenBody(int dbId, string name)
    {
        byte[] n = Encoding.ASCII.GetBytes(name);
        var b = new byte[5 + n.Length];
        BitConverter.GetBytes(dbId).CopyTo(b, 0);
        b[4] = (byte)n.Length;
        n.CopyTo(b, 5);
        return b;
    }

    /// <summary>The DLL's op 198 body: [name len:1][name][six zones].</summary>
    public static byte[] BloodSetBody(string name, byte[] zones)
    {
        byte[] n = Encoding.ASCII.GetBytes(name);
        var b = new byte[1 + n.Length + 6];
        b[0] = (byte)n.Length;
        n.CopyTo(b, 1);
        Array.Copy(zones, 0, b, 1 + n.Length, 6);
        return b;
    }

    /// <summary>The wire list of wounds: "&lt;body&gt;=&lt;12 hex&gt;,..." or "-". Bodies with no blood at all are left out.</summary>
    public static string CorpseLooksCsv(IEnumerable<KeyValuePair<string, byte[]>> looks)
    {
        var parts = looks.Where(l => l.Value.Length == 6 && l.Value.Any(z => z != 0) && Wo137Text.IsNpc(l.Key))
                         .OrderBy(l => l.Key, StringComparer.Ordinal).Select(l => l.Key + "=" + Convert.ToHexString(l.Value).ToLowerInvariant()).ToList();
        return parts.Count == 0 ? "-" : string.Join(',', parts);
    }

    public static List<KeyValuePair<string, byte[]>> ParseCorpseLooks(string? csv)
    {
        var o = new List<KeyValuePair<string, byte[]>>();
        if (string.IsNullOrEmpty(csv) || csv == "-") return o;
        foreach (var part in csv.Split(','))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || part.Length - eq - 1 != 12 || !Wo137Text.IsNpc(part[..eq]) || part[..eq].Length > 63) continue;
            try { o.Add(new(part[..eq], Convert.FromHexString(part[(eq + 1)..]))); } catch (FormatException) { }
        }
        return o;
    }

    /// <summary>The mod's "w221_go" event: "&lt;name&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt; &lt;gait 0..3&gt;" -> the DLL's op 195 body, or null.</summary>
    public static byte[]? GoBody(string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 5 || !Wo137Text.IsNpc(f[0]) || f[0].Length > 63) return null;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        const System.Globalization.NumberStyles fl = System.Globalization.NumberStyles.Float;
        if (!float.TryParse(f[1], fl, c, out float x) || !float.TryParse(f[2], fl, c, out float y) || !float.TryParse(f[3], fl, c, out float z)) return null;
        if (!byte.TryParse(f[4], out byte gait) || gait > 3 || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return null;
        byte[] n = Encoding.ASCII.GetBytes(f[0]);
        var b = new byte[2 + n.Length + 12];
        b[0] = gait; b[1] = (byte)n.Length;
        n.CopyTo(b, 2);
        BitConverter.GetBytes(x).CopyTo(b, 2 + n.Length); BitConverter.GetBytes(y).CopyTo(b, 6 + n.Length); BitConverter.GetBytes(z).CopyTo(b, 10 + n.Length);
        return b;
    }

    /// <summary>The DLL's op 194: [on:1][name len:1][name].</summary>
    public static byte[] TalkBody(bool on, string name)
    {
        byte[] n = Encoding.ASCII.GetBytes(name);
        var b = new byte[2 + n.Length];
        b[0] = (byte)(on ? 1 : 0);
        b[1] = (byte)n.Length;
        n.CopyTo(b, 2);
        return b;
    }

    /// <summary>The DLL's op 193: [name len:1][name].</summary>
    public static byte[] CloseBody(string name)
    {
        byte[] n = Encoding.ASCII.GetBytes(name);
        var b = new byte[1 + n.Length];
        b[0] = (byte)n.Length;
        n.CopyTo(b, 1);
        return b;
    }
}

/// <summary>WO-221 D.4: the saddlebags' texts -- only these shapes travel and reach a game's script.</summary>
public static partial class Wo221BagRules
{
    private const string OneItem = @"[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}:\d{1,6}:\d{1,3}";
    private static readonly System.Text.RegularExpressions.Regex Items =
        new("^(-|" + OneItem + "(," + OneItem + "){0,19})$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex Cls = new(@"^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static bool Num(string s, int max) => s.Length is > 0 and <= 7 && int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int v) && v >= 0 && v <= max;
    private static bool Horse(string s) => s.Length <= 63 && Wo137Text.IsNpc(s);

    /// <summary>"&lt;horse&gt; &lt;rev&gt; &lt;part&gt; &lt;parts&gt; &lt;items&gt;" (the owner's list).</summary>
    public static bool IsBags(string? t)
    {
        var f = (t ?? "").Split(' ');
        return f.Length == 5 && Horse(f[0]) && Num(f[1], 9_999_999) && Num(f[2], 40) && Num(f[3], 40) && Items.IsMatch(f[4]);
    }
    /// <summary>"&lt;horse&gt; &lt;part&gt; &lt;parts&gt; &lt;items&gt;" (the host's list at a join).</summary>
    public static bool IsBase(string? t)
    {
        var f = (t ?? "").Split(' ');
        return f.Length == 4 && Horse(f[0]) && Num(f[1], 40) && Num(f[2], 40) && Items.IsMatch(f[3]);
    }
    /// <summary>"&lt;tok&gt; &lt;horse&gt; &lt;class&gt; &lt;units&gt;" (the partner's take).</summary>
    public static bool IsTake(string? t)
    {
        var f = (t ?? "").Split(' ');
        return f.Length == 4 && Num(f[0], 9_999_999) && Horse(f[1]) && Cls.IsMatch(f[2]) && Num(f[3], 999_999);
    }
    /// <summary>"&lt;tok&gt; grant|refuse|busy" (the owner's answer).</summary>
    public static bool IsAnswer(string? t)
    {
        var f = (t ?? "").Split(' ');
        return f.Length == 2 && Num(f[0], 9_999_999) && (f[1] is "grant" or "refuse" or "busy");
    }
    /// <summary>"&lt;peer&gt; rest..." -> the peer and the rest.</summary>
    public static bool SplitPeer(string? t, out byte peer, out string rest)
    {
        peer = 0; rest = "";
        int sp = (t ?? "").IndexOf(' ');
        if (sp <= 0 || !byte.TryParse(t![..sp], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out peer)) return false;
        rest = t[(sp + 1)..];
        return rest.Length > 0;
    }
}

/// <summary>WO-221 F: who the host's route lines go to.</summary>
public static partial class Wo221Rules
{
    /// <summary>A joiner that said "no" gets none; one that said "yes", or said nothing (an older build), gets them.</summary>
    public static List<byte> RouteReaders(IReadOnlyList<byte> peers, Func<byte, bool?> said)
    {
        var keep = new List<byte>(peers.Count);
        foreach (byte g in peers) if (said(g) != false) keep.Add(g);
        return keep;
    }
}

/// <summary>WO-221 B: the signs of a conversation that reached its choices.</summary>
public static partial class Wo221Rules
{
    /// <summary>The id of a dialogue this player is a soul of, wherever "Dude" stands in the engine's "New dialogue '... - Id: N) ...' is
    /// starting. Params: souls = '...'" line (a forced or NPC-started one names the NPC first); -1 for any other line, and for a
    /// dialogue Dude is alone in (a monologue has no choices).</summary>
    public static int DudeDialogIdAnywhere(string? line)
    {
        if (line is null || !line.StartsWith("New dialogue '", StringComparison.Ordinal)) return -1;
        int s = line.IndexOf("souls = '", StringComparison.Ordinal);
        if (s < 0) return -1;
        int e = line.IndexOf('\'', s + 9);
        string souls = e > s ? line[(s + 9)..e] : line[(s + 9)..];
        bool dude = false; int n = 0;
        foreach (string part in souls.Split(';'))
        {
            string p = part.Trim();
            if (!p.StartsWith("Ex: ", StringComparison.Ordinal)) continue;
            n++;
            string name = p[4..];
            int sp = name.IndexOf(' ');
            if ((sp < 0 ? name : name[..sp]) == "Dude") dude = true;
        }
        if (!dude || n < 2) return -1;
        int id = line.IndexOf(" - Id: ", StringComparison.Ordinal);
        if (id < 0 || id > s) return -1;
        int i = id + 7, v = 0, d = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]) && d < 9) { v = v * 10 + (line[i] - '0'); i++; d++; }
        return d > 0 ? v : -1;
    }

    /// <summary>"[ID: n] k) path [used=..." -- a choice of dialogue n offered.</summary>
    public static bool IsChoiceOf(string? line, int dialogId)
    {
        if (dialogId < 0 || line is null || !line.StartsWith("[ID: ", StringComparison.Ordinal)) return false;
        int e = line.IndexOf(']');
        if (e <= 5 || !int.TryParse(line.AsSpan(5, e - 5), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id != dialogId) return false;
        int i = e + 2, d = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]) && d < 3) { i++; d++; }
        return d > 0 && i < line.Length && line[i] == ')' && line.Contains("[used=", StringComparison.Ordinal);
    }
}

/// <summary>WO-221 D.1: a conversation's own nodes.</summary>
public static partial class Wo221Rules
{
    /// <summary>Two dialogue nodes of one quest module: the same path up to the last segment, at least "Barbora.a.b" deep, and not the
    /// same node. The open world's catch-all modules (Barbora.open_world.*) never count: unrelated barks live side by side there.</summary>
    public static bool SameQuestModule(string? capturePath, string? talkName)
    {
        if (string.IsNullOrEmpty(capturePath) || string.IsNullOrEmpty(talkName) || capturePath == talkName) return false;
        int a = capturePath.LastIndexOf('.'), b = talkName.LastIndexOf('.');
        if (a <= 0 || a != b || string.CompareOrdinal(capturePath, 0, talkName, 0, a) != 0) return false;
        string parent = capturePath[..a];
        if (parent.StartsWith("Barbora.open_world", StringComparison.Ordinal)) return false;
        return parent.Count(ch => ch == '.') >= 2;
    }
}

/// <summary>WO-221 D.1: the quest module instance that sells a horse.</summary>
public static partial class Wo221Rules
{
    /// <summary>"...horse_trader_semin.horseinstance_1.replaceplayerhorse_disposeoldifexists.replaceplayerhorse4_1" -> "...horse_trader_semin.horseinstance_1":
    /// the node's path without its last two segments (the library that holds the node, the node); "" when the path is not that deep.</summary>
    public static string HorseModuleOf(string? nodePath)
    {
        if (string.IsNullOrEmpty(nodePath) || !Wo137Text.IsPath(nodePath)) return "";
        int a = nodePath.LastIndexOf('.');
        int b = a > 0 ? nodePath.LastIndexOf('.', a - 1) : -1;
        if (b <= 0) return "";
        string m = nodePath[..b];
        return m.Count(ch => ch == '.') >= 3 ? m : "";
    }

    /// <summary>Op 199's body: [n:1]{[len:2][path]} -- at most 32 modules of at most 400 characters.</summary>
    public static byte[] HorseModulesBody(IReadOnlyList<string> modules)
    {
        var ok = modules.Where(m => m.Length is > 0 and <= 400 && Wo137Text.IsPath(m)).Distinct().Take(32).ToList();
        var b = new List<byte> { (byte)ok.Count };
        foreach (string m in ok)
        {
            byte[] p = Encoding.ASCII.GetBytes(m);
            b.Add((byte)(p.Length & 0xFF)); b.Add((byte)(p.Length >> 8));
            b.AddRange(p);
        }
        return [.. b];
    }
}
