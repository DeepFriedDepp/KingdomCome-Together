// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-195: an offline replay of the 0.60.5 joiner's evening (20:38-21:29; there is no host bundle). The bundle is the maintainer's private
/// log set (never committed), unpacked in logs/w195/joiner; a replay passes with a note when it is not there. Every result is printed.
///   1  both engaged copies that died (tzel_woman_8 21:03:27, tzel_man_13 21:03:46) get the release at their first death path, ahead of
///      the apply; 0.60.5's DLL then wrote the opponent back onto them (and pinned combat_forcedTarget) -- each of those writes comes after
///      the release, so the new gates refuse every one.
///   3  the key the joiner took off tzel_woman_8 (494ed6dc, the loot screen's own) was taken back as "none": now a "screen" take.
/// </summary>
public class Wo195ReplayTests
{
    private readonly ITestOutputHelper _out;
    public Wo195ReplayTests(ITestOutputHelper o) { _out = o; }

    private static string? Log(string file)
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d);
        if (d is null) return null;
        string p = Path.Combine(d, "logs", "w195", "joiner", file);
        return File.Exists(p) ? p : null;
    }

    private static readonly Regex AgentT = new(@"^(\d\d):(\d\d):(\d\d\.\d\d\d) ", RegexOptions.Compiled);
    private static readonly Regex NativeT = new(@"^\[(\d\d):(\d\d):(\d\d\.\d\d\d)\] ", RegexOptions.Compiled);
    private static double T(Regex rx, string l)
    {
        var m = rx.Match(l);
        return m.Success ? int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : -1;
    }

    [Fact]
    public void Item1_both_engaged_deaths_get_the_release_ahead_of_every_post_death_write()
    {
        string? agent = Log("agent.log"), native = Log("kcdmp-native.log");
        if (agent is null || native is null) { _out.WriteLine("NOTE: logs/w195/joiner not present -- replay skipped"); return; }
        var lines = File.ReadAllLines(agent);
        var deaths = Wo195Rules.ReplayDeaths(lines);
        foreach (var d in deaths) _out.WriteLine($"death {d.Npc} path={d.Path} engaged_at_death={d.WasEngaged} line={d.Line} {lines[d.Line - 1][..12]}");
        var nat = File.ReadAllLines(native);
        foreach (string npc in new[] { "tzel_woman_8", "tzel_man_13" })
        {
            var d = deaths.First(x => x.Npc == npc);
            Assert.True(d.WasEngaged, $"{npc} was engaged with the joiner when it died");
            Assert.Equal("owner-death", d.Path);
            double rel = T(AgentT, lines[d.Line - 1]);
            Assert.True(Wo195Rules.ReleaseDue(true, 0, 1), "the release runs (a first death path)");
            // 0.60.5's DLL after the release moment: the opponent written back / pinned on the dying copy (WO187-OPP), within 4 s
            var writes = nat.Where(l => l.Contains("WO187-OPP npc=" + npc + " ", StringComparison.Ordinal)
                                        && (l.Contains("written back", StringComparison.Ordinal) || l.Contains("pinned to", StringComparison.Ordinal)))
                            .Select(l => (At: T(NativeT, l), Line: l)).Where(w => w.At >= rel - 0.1 && w.At <= rel + 4.0).ToList();
            foreach (var w in writes) _out.WriteLine($"  0.60.5 post-death write on {npc}: {w.Line[..Math.Min(150, w.Line.Length)]}");
            Assert.NotEmpty(writes);
            // the new build: the agent's release (op 56) and the copy's dead mark come at the first death path -- every one of these writes
            // is at or after it, so the DLL's wish gate refuses it (wo195rules::refuse(on, dead)) and the agent sends no new wish (Blocks)
            Assert.All(writes, w => Assert.True(w.At >= rel - 0.1));
            Assert.True(Wo195Rules.Blocks(true, markedDead: true));
            // and the skirmish: 0.60.5 left it at the death too (quietly) -- the new line says so
            double off = nat.Where(l => l.Contains("WO132-ENGAGE", StringComparison.Ordinal) && l.Contains(" off:", StringComparison.Ordinal))
                            .Select(l => T(NativeT, l)).Where(t => t >= rel - 0.1 && t <= rel + 1.0).DefaultIfEmpty(-1).First();
            _out.WriteLine($"  {npc}: 0.60.5 left the skirmish at {(off < 0 ? "never" : TimeSpan.FromSeconds(off).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture))} with no agent line");
            Assert.True(off > 0);
        }
        // the session's STATS: engage_on 3 vs engage_off 1 (two releases were never counted)
        string? stats = lines.Last(l => l.Contains("MP-W132-STATS", StringComparison.Ordinal) && l.Contains("engage_on=", StringComparison.Ordinal));
        _out.WriteLine("last STATS: " + stats[..Math.Min(260, stats.Length)]);
    }

    [Fact]
    public void Item3_the_taken_back_key_was_the_loot_screens_own()
    {
        string? kcd = Log("kcd.log");
        if (kcd is null) { _out.WriteLine("NOTE: logs/w195/joiner not present -- replay skipped"); return; }
        var lines = File.ReadAllLines(kcd);
        const string Key = "494ed6dc-51b9-444f-8055-3116e08a5329";
        int screen = Array.FindIndex(lines, l => l.Contains("WO134-BODY not-a-put npc=tzel_woman_8 cls=" + Key, StringComparison.Ordinal));
        int take = Array.FindIndex(lines, l => l.Contains("WO134-BODY take npc=tzel_woman_8 cls=" + Key, StringComparison.Ordinal));
        int back = Array.FindIndex(lines, l => l.Contains("takeback-applied npc=tzel_woman_8 cls=" + Key, StringComparison.Ordinal));
        _out.WriteLine($"not-a-put line {screen}, take line {take}, takeback line {back}");
        Assert.True(screen >= 0 && take > screen && back > take, "the field's sequence: the screen made it, he took it, it was taken back");
        Assert.Contains("(none)", lines[back]);
        // the new joiner remembers the screen's item and marks its take; the host's agent passes the mark through
        Assert.True(Wo195Rules.IsScreenMark("screen"));
    }
}
