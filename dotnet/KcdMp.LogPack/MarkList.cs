// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-201 B24: the marks a player typed ("Something's wrong here", mark_&lt;word&gt;, or mark &lt;note&gt;), as the agent logged them:
/// "HH:MM:SS.fff ... MP-MARK &lt;word&gt;[ note=&lt;text&gt;] -- the tester's marker (typed on this machine)". The MANIFEST lists each.
/// </summary>
public static class MarkList
{
    private static readonly Regex Rx = new(@"^(?<t>\d\d:\d\d:\d\d\.\d+) .*?MP-MARK (?<w>[A-Za-z0-9_]+)(?: note=(?<n>.*?))? -- the tester's marker",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void Collect(string entry, IEnumerable<string> lines, List<string> into)
    {
        foreach (var l in lines)
        {
            var m = Rx.Match(l);
            if (!m.Success) continue;
            string note = m.Groups["n"].Success ? m.Groups["n"].Value.Trim() : "";
            into.Add($"{entry} {m.Groups["t"].Value} {m.Groups["w"].Value}{(note.Length > 0 ? " \"" + note + "\"" : "")}");
        }
    }
}
