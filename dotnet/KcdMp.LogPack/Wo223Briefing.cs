// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-223 F: the briefing's two lines on enemies and the joiner. The host's mod writes one WO223-NOTICE line per enemy that turned
/// alert (whom it most likely noticed, how far he was, his pace); the joiner's DLL keeps WO223-COPYSUM (the copies whose host NPC
/// fights this player: in his fight, aimed at him, and for how long not). Pure: lines in, words out.
/// </summary>
public static class Wo223Briefing
{
    private static readonly Regex Notice = new(@"WO223-NOTICE npc=(\S+) noticed=(host|joiner)\S* at=([\d.]+) pace=([\d.]+) crouch=([01])", RegexOptions.Compiled);
    private static readonly Regex CopySum = new(@"WO223-COPYSUM me_samples=(\d+) aimed=(\d+) \(([\d.]+) %\) in_my_fight=(\d+) \(([\d.]+) %\) both=(\d+) \(([\d.]+) %\) wrong_s=([\d.]+) of ([\d.]+)", RegexOptions.Compiled);

    /// <summary>"enemies noticed the host / the joiner first": the counts and each side's mean distance; null when no enemy turned
    /// alert in this session (or this machine was no host).</summary>
    public static string? NoticeValue(IEnumerable<string> gameLog)
    {
        var rows = gameLog.Select(x => Notice.Match(x)).Where(m => m.Success)
            .Select(m => (Who: m.Groups[2].Value, D: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))).ToList();
        if (rows.Count == 0) return null;
        static string Side(List<double> d) => d.Count == 0 ? "0" : string.Create(CultureInfo.InvariantCulture, $"{d.Count} (at {d.Average():F0} m on average)");
        return $"the host first: {Side(rows.Where(r => r.Who == "host").Select(r => r.D).ToList())}; the joiner first: {Side(rows.Where(r => r.Who == "joiner").Select(r => r.D).ToList())}";
    }

    /// <summary>"copies fighting the wrong player: N seconds": from the DLL's newest summary line; null when no host NPC fought this
    /// player in this session (or this machine was no joiner).</summary>
    public static string? CopyValue(IEnumerable<string> nativeLog)
    {
        var m = nativeLog.Select(x => CopySum.Match(x)).LastOrDefault(x => x.Success);
        if (m is null) return null;
        double wrong = double.Parse(m.Groups[8].Value, CultureInfo.InvariantCulture), of = double.Parse(m.Groups[9].Value, CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture,
            $"{wrong:F0} s of the {of:F0} s enemies fought this player (in his fight and aimed at him in {m.Groups[7].Value} % of {m.Groups[1].Value} samples)");
    }
}
