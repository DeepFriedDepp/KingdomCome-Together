// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.LogPack;

namespace KcdMp.LogPack.Tests;

/// <summary>WO-221 F: the briefing reads only this launch -- the native log's lines and the probes' rows of the newest launch.</summary>
public class Wo221LogTests
{
    static string N(string launch, string text, int s = 0) => $"[12:00:{s % 60:00}.000] t001234 L{launch} {text}";

    [Fact]
    public void Native_lines_of_the_newest_launch_only_with_their_continuations()
    {
        var lines = new[]
        {
            N("aaaaaa", "WO215-PROBE one"), "  a continuation of the old launch", N("aaaaaa", "FAULT old"),
            N("bbbbbb", "WO215-PROBE two"), "  a continuation of this launch", N("bbbbbb", "MP-NPCPULL x"), "",
        };
        var r = LaunchFilter.NativeNewest(lines);
        Assert.Equal("bbbbbb", r.Launch);
        Assert.Equal(new[] { lines[3], lines[4], lines[5] }, r.Lines);
        Assert.Equal(3, r.LeftOut);
    }

    [Fact]
    public void A_log_that_names_no_launch_is_kept_whole()
    {
        var lines = new[] { "[12:00:00.000] t001234 WO215-PROBE one", "[12:00:01.000] t001234 FAULT x" };
        var r = LaunchFilter.NativeNewest(lines);
        Assert.Null(r.Launch);
        Assert.Equal(lines, r.Lines);
        Assert.Equal(0, r.LeftOut);
    }

    [Fact]
    public void An_older_builds_lines_before_this_launch_are_left_out()
    {
        var lines = new[] { "[11:00:00.000] t001234 WO215-PROBE of a build that named no launch", N("cccccc", "WO221-CFG this launch") };
        var r = LaunchFilter.NativeNewest(lines);
        Assert.Equal(new[] { lines[1] }, r.Lines);
        Assert.Equal(1, r.LeftOut);
    }

    [Fact]
    public void Probe_rows_of_the_newest_launch_only()
    {
        static string Row(string? l, int t) => "{\"t\":" + t + ",\"m\":\"joiner\",\"f\":1,\"s\":1," + (l is null ? "" : "\"L\":\"" + l + "\",") + "\"a\":[]}";
        var rows = new[] { Row(null, 1), Row("aaaaaa", 2), Row("aaaaaa", 3), Row("bbbbbb", 4), Row("bbbbbb", 5) };
        var r = LaunchFilter.ProbeNewest(rows);
        Assert.Equal("bbbbbb", r.Launch);
        Assert.Equal(new[] { rows[3], rows[4] }, r.Lines);
        Assert.Equal(3, r.LeftOut);
        var old = LaunchFilter.ProbeNewest(new[] { Row(null, 1), Row(null, 2) });
        Assert.Null(old.Launch);
        Assert.Equal(2, old.Lines.Count);
    }

    [Fact]
    public void One_native_file_is_counted_never_the_log_and_its_mirror()
    {
        Assert.Equal("kcdmp-native.log", LaunchFilter.PickNative(new[] { "agent.log", "kcdmp-native.mirror.log", "kcdmp-native.log", "kcdmp-native.1.log" }));
        Assert.Equal("kcdmp-native.mirror.log", LaunchFilter.PickNative(new[] { "kcdmp-native.mirror.log", "kcd.log" }));
        Assert.Null(LaunchFilter.PickNative(new[] { "kcd.log" }));
    }

    [Fact]
    public void The_briefing_says_which_launch_it_counted_and_counts_only_it()
    {
        var inp = new Briefing.Input();
        inp.Logs["agent.log"] = new[] { "12:00:00.000 m=1 off=+0 MP-WO122 cfg shared_world=on role=joiner connected=on" };
        // the same faults in the log and in its mirror, and an earlier launch's faults before them
        var native = new[]
        {
            N("aaaaaa", "FAULT engine::rd: 0xC0000005 reading 0x0 at KCDMP.dll+0x1 (1st) [read site]"),
            N("aaaaaa", "FAULT engine::rd: 0xC0000005 reading 0x0 at KCDMP.dll+0x1 (2nd) [read site]"),
            N("bbbbbb", "WO221-CFG bits=0xF"),
        };
        inp.Logs["kcdmp-native.log"] = native;
        inp.Logs["kcdmp-native.mirror.log"] = native;
        string b = Briefing.Build(inp);
        Assert.Contains("**This launch only:** the native log (kcdmp-native.log): launch Lbbbbbb, 1 line(s) counted, 2 of other launches left out", b);
    }
}
