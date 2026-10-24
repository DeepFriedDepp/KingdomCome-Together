// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.Json;
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-206 C: the agent's digest must be tools/digest/digest.py's byte for byte (the analyzer compares the two machines' rows).
/// The expected buckets were printed by digest.buckets() for the same four entries.
/// </summary>
public class Wo206DigestTests
{
    private static readonly Dictionary<string, string> Sample = new()
    {
        ["jaunt/watch_1300"] = "clk:2", ["Barbora.a.b"] = "sv:3", ["ttkc_man_14"] = "dead", ["x|y"] = "-2",
    };

    [Fact]
    public void Matches_the_analyzers_function()
    {
        Assert.Equal(0xaf63dc4c8601ec8cUL, Wo206Digest.Fnv1a64("a"));
        var b = Wo206Digest.Buckets(Sample);
        Assert.Equal(new SortedDictionary<int, string> { [14] = "623add4f", [89] = "613be756", [136] = "197e4b59", [232] = "b3bdb67c" }, b);
    }

    [Fact]
    public void One_changed_value_changes_one_bucket_and_order_does_not_matter()
    {
        var a = Wo206Digest.Buckets(Sample);
        var changed = new Dictionary<string, string>(Sample) { ["ttkc_man_14"] = "alive" };
        var c = Wo206Digest.Buckets(changed);
        Assert.Single(a.Keys.Union(c.Keys).Where(k => !a.TryGetValue(k, out var x) || !c.TryGetValue(k, out var y) || x != y));
        Assert.Equal(a, Wo206Digest.Buckets(Sample.Reverse()));
    }

    [Fact]
    public void A_row_has_the_analyzers_shape()
    {
        string row = Wo206Digest.Row(1000, "host", Wo206Digest.Quest, 2, 7, Sample, full: true);
        using var d = JsonDocument.Parse(row);
        var r = d.RootElement;
        Assert.Equal("host", r.GetProperty("m").GetString());
        Assert.Equal("quest", r.GetProperty("cat").GetString());
        Assert.Equal(2, r.GetProperty("epoch").GetInt32());
        Assert.Equal(7, r.GetProperty("seq").GetInt32());
        Assert.Equal(4, r.GetProperty("n").GetInt32());
        Assert.Equal("623add4f", r.GetProperty("b").GetProperty("14").GetString());
        Assert.Equal("clk:2", r.GetProperty("e").GetProperty("jaunt/watch_1300").GetString());
        // twin: without the full map the row carries no keys (the analyzer carries the last map forward)
        using var d2 = JsonDocument.Parse(Wo206Digest.Row(1000, "host", Wo206Digest.Quest, 2, 8, Sample, full: false));
        Assert.Equal(JsonValueKind.Null, d2.RootElement.GetProperty("e").ValueKind);
    }

    [Fact]
    public void The_marker_and_the_dead_scan_parse()
    {
        Assert.True(Wo206Digest.TryParseMarker(Wo206Digest.MarkerText(3, 41), out int e, out int s) && e == 3 && s == 41);
        Assert.False(Wo206Digest.TryParseMarker("v1|3|0", out _, out _));
        Assert.True(Wo206Digest.TryParseDeadScan("41 ttkc_man_14:1,ttkc_woman_2:0", out int seq, out var map));
        Assert.Equal(41, seq);
        Assert.Equal("dead", map["ttkc_man_14"]);
        Assert.False(map.ContainsKey("ttkc_woman_2"));   // the living are no keys
        Assert.True(Wo206Digest.TryParseDeadScan("43 DialogTwin_Dude:1,ttkc_man_14:1", out _, out var helper) && !helper.ContainsKey("DialogTwin_Dude"));
        Assert.True(Wo206Digest.TryParseDeadScan("42", out _, out var none) && none.Count == 0);
        Assert.Equal("clk:2", Wo206Digest.QuestValue(Wo206Digest.ClockKind, 2));
        Assert.Equal("sv:5", Wo206Digest.QuestValue(0, 5));
        Assert.Null(Wo206Digest.QuestEntry(Wo206Digest.ClockKind, 0));          // a clock at rest is no key
        Assert.Equal("clk:1", Wo206Digest.QuestEntry(Wo206Digest.ClockKind, 1));
        Assert.Equal("sv:0", Wo206Digest.QuestEntry(0, 0));                      // a State at 0 is a key
        Assert.Equal("780", Wo206Digest.ClockValue(785 * 60 + 59 + 120));   // 13:07:59 -> 780 (rounded down to 15)
    }

    [Fact]
    public void The_dead_scan_reads_death_the_way_the_mods_observer_does()
    {
        string lua = Wo206Digest.DeadScanLua(10.5f, 20f, 30f, 7);
        Assert.Contains("e.actor:IsDead()", lua);                 // what the mod's own death observer reads
        Assert.Contains("e.soul:GetState('health')<=0", lua);     // the fallback
        Assert.DoesNotContain("soul:IsDead", lua);                // twin: not a soul method here -- every body read alive (the bench)
        Assert.Contains("w206_dead 7 ", lua);
        Assert.Contains("x=10.50,y=20.00,z=30.00", lua);
    }

    [Fact]
    public void A_load_keeps_the_keyspace_only_a_dll_without_paths_registers_again()
    {
        Assert.False(Wo206Digest.KeyspaceLost(10252));
        Assert.True(Wo206Digest.KeyspaceLost(0));   // twin: a DLL that came back holds nothing
    }

    [Fact]
    public void The_keyspace_holds_the_clocks()
    {
        var idx = new QuestValueIndex();
        idx.AddStates("Quests/Final/Barbora/a/b.xml", "<State Name=\"s1\" TypeT=\"uint\">");
        idx.AddClocks("Quests/Final/Barbora/a/b.xml", "<Timer Name=\"timer1\" PositionY=\"0\"><TimeOfDayWatch Name=\"tod\">");
        Assert.Contains("Barbora.a.b.s1", idx.StatePaths);
        Assert.Contains("Barbora.a.b.timer1", idx.ClockPaths);
        Assert.Contains("Barbora.a.b.tod", idx.ClockPaths);
        Assert.Equal(0, idx.AddClocks("Quests/Testing/x.xml", "<Timer Name=\"t\">"));   // twin: outside Quests/Final no key
    }
}
