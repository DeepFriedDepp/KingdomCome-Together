// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;

namespace KcdMp.Client;

/// <summary>WO-221's pure rules (unit-tested in Wo221Tests).</summary>
public static class Wo221Rules
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
