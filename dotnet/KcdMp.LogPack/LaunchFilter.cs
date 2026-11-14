// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-221 F: the briefing counts this launch only. The DLL's logs of several launches end up side by side -- a bundle carries the
/// native log and its mirror, a probe file's kept predecessor outlives its launch -- and a count over them mixed sessions (a field
/// briefing reported 5,834 s of empty bodies where the session itself had about 160 s). Every native log line now carries its launch
/// (<c>[HH:MM:SS.mmm] tNNNNNN Lxxxxxx text</c>) and every probe row too (<c>"L":"xxxxxx"</c>); these helpers keep the newest launch's.
/// A file written by an older DLL has no launch anywhere: it is kept whole, as before.
/// </summary>
public static class LaunchFilter
{
    private static readonly Regex NativeTag = new(@"^\[\d\d:\d\d:\d\d\.\d{3}\] t\d+ L([0-9a-f]{6}) ", RegexOptions.Compiled);
    private static readonly Regex ProbeTag = new("\"L\":\"([0-9a-f]{6})\"", RegexOptions.Compiled);

    /// <summary>What was kept: the lines, the launch they belong to (null: no line named one), how many lines of other launches were left out.</summary>
    public sealed record Result(List<string> Lines, string? Launch, int LeftOut);

    /// <summary>The native log's lines of the newest launch (the last line that names one). A line with no prefix of its own right
    /// after a kept line is that line's continuation and is kept; one after a dropped line is dropped with it.</summary>
    public static Result NativeNewest(IEnumerable<string> lines)
    {
        var all = lines as IList<string> ?? lines.ToList();
        string? newest = null;
        for (int i = all.Count - 1; i >= 0 && newest is null; i--)
        {
            var m = NativeTag.Match(all[i]);
            if (m.Success) newest = m.Groups[1].Value;
        }
        if (newest is null) return new Result(all.ToList(), null, 0);
        var keep = new List<string>(all.Count);
        int left = 0;
        bool keeping = false;
        foreach (var l in all)
        {
            var m = NativeTag.Match(l);
            if (m.Success) keeping = m.Groups[1].Value == newest;
            else if (l.Length > 0 && l[0] == '[') keeping = false;   // a prefixed line of a DLL that named no launch: an earlier build's, an earlier launch's
            if (l.Length == 0) continue;   // an empty line carries nothing
            if (keeping) keep.Add(l); else left++;
        }
        return new Result(keep, newest, left);
    }

    /// <summary>The probe's rows of the newest launch (the last row that names one). Rows that name none are an older DLL's: kept only
    /// when no row names a launch.</summary>
    public static Result ProbeNewest(IEnumerable<string> rows)
    {
        var all = rows as IList<string> ?? rows.ToList();
        string? newest = null;
        for (int i = all.Count - 1; i >= 0 && newest is null; i--)
        {
            var m = ProbeTag.Match(all[i]);
            if (m.Success) newest = m.Groups[1].Value;
        }
        if (newest is null) return new Result(all.ToList(), null, 0);
        string want = "\"L\":\"" + newest + "\"";
        var keep = new List<string>(all.Count);
        int left = 0;
        foreach (var l in all)
        {
            if (l.Contains(want, StringComparison.Ordinal)) keep.Add(l);
            else if (!string.IsNullOrWhiteSpace(l)) left++;
        }
        return new Result(keep, newest, left);
    }

    /// <summary>Which of a bundle's native logs to count: the DLL's own file, else its mirror -- never both (they hold the same lines).</summary>
    public static string? PickNative(IEnumerable<string> entries)
    {
        var e = entries.Where(k => k.StartsWith("kcdmp-native", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        return e.FirstOrDefault(k => k.Equals("kcdmp-native.log", StringComparison.OrdinalIgnoreCase))
            ?? e.FirstOrDefault(k => k.Equals("kcdmp-native.mirror.log", StringComparison.OrdinalIgnoreCase))
            ?? e.FirstOrDefault();
    }
}
