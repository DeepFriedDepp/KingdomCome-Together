// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.LogPack.Tests;

/// <summary>WO-214: the briefing's band table and verdict (G), the owner's empty stretches (C.1), the deaths applied (D).</summary>
public class Wo214LogTests
{
    private static readonly string[] Bands =
    [
        "07:16:28.084 m=1 off=+0 MP-LINK npc-bands who=joiner window_s=10.4 fight=0:p50=0.0Hz:p10=0.0Hz 40m=4:p50=5.4Hz:p10=0.5Hz 150m=10:p50=0.5Hz:p10=0.5Hz far=0:p50=0.0Hz:p10=0.0Hz",
        "07:16:38.523 m=1 off=+0 MP-LINK npc-bands who=joiner window_s=10.4 fight=0:p50=0.0Hz:p10=0.0Hz 40m=4:p50=5.3Hz:p10=0.5Hz 150m=12:p50=0.5Hz:p10=0.5Hz far=0:p50=0.0Hz:p10=0.0Hz",
    ];
    private static readonly string[] Moving =
    [
        "07:16:28.084 m=1 off=+0 MP-LINK npc-moving who=joiner window_s=10.4 fight=0:p50=0.0Hz:p10=0.0Hz 40m=2:p50=19.6Hz:p10=17.0Hz 150m=1:p50=9.8Hz:p10=9.8Hz far=0:p50=0.0Hz:p10=0.0Hz",
        "07:16:38.523 m=1 off=+0 MP-LINK npc-moving who=joiner window_s=10.4 fight=0:p50=0.0Hz:p10=0.0Hz 40m=1:p50=18.8Hz:p10=18.8Hz 150m=0:p50=0.0Hz:p10=0.0Hz far=0:p50=0.0Hz:p10=0.0Hz",
    ];

    [Fact]
    public void The_band_table_says_the_walking_rate_in_plain_words_beside_every_npcs()
    {
        string t = LinkSection.BandTable(Bands, Moving);
        Assert.Contains("walking NPCs within 40 m reached you 19 times a second", t);
        Assert.Contains("| within 40 m | 4 | 5.3 | 18.8 / 17.0 (3 body-windows) |", t);   // the briefing's nearest-rank median of two windows is the lower
        Assert.Contains("| within 150 m | 11 | 0.5 | 9.8 / 9.8 (1 body-windows) |", t);
        // the old view alone (no npc-moving lines: an older build) -- the median of every NPC, the field's "0.5 Hz", says nothing of walkers
        Assert.Contains("(not measured: an older build)", LinkSection.BandTable(Bands, []));
        Assert.DoesNotContain("walking NPCs within 40 m reached you", LinkSection.BandTable(Bands, []));
    }

    [Fact]
    public void The_owners_watchdog_counts_the_whole_stretch_the_probe_missed()
    {
        string[] native =
        [
            "[19:09:22.700] t1 WO214-EMPTY npc=tsem_man_11 secs=10.0 dist_m=40.1 open=1 why=still-empty",
            "[19:12:03.500] t1 WO214-EMPTY npc=tsem_man_11 secs=170.8 dist_m=40.1 open=0 why=an-action-again [n=1]",
            "[19:12:10.000] t1 WO214-EMPTY npc=tsem_man_4 secs=0.8 dist_m=12.0 open=0 why=let-go [n=2]",
            "[19:13:00.000] t1 WO214-EMPTY npc=tsem_man_6 secs=20.0 dist_m=30.0 open=1 why=still-empty",   // still open at the log's end
        ];
        var e = W210Instruments.OwnerEmpty(native)!;
        Assert.Equal(3, e.Count);
        Assert.Equal("tsem_man_11", e[0].Name);
        Assert.Equal(170.8, e[0].Secs, 1);
        Assert.Equal(1, e[0].N);
        Assert.Equal(20.0, e.Single(x => x.Name == "tsem_man_6").Secs, 1);
        // an older build (no line, no WO214-CFG): null -- the probe's numbers stand; a WO-214 build with none: no empty body
        Assert.Null(W210Instruments.OwnerEmpty(["[x] t1 WO210-STATE reseat npc=a was=empty"]));
        Assert.Empty(W210Instruments.OwnerEmpty(["[x] t1 WO214-CFG bits=0x1F"])!);
    }

    [Fact]
    public void The_deaths_applied_line_counts_every_outcome_and_the_upright_ones()
    {
        string[] agent =
        [
            "19:11:41.000 m=1 off=+0 WO214-DEATH npc=a after_ms=1100 soul_dead=yes how=engine living=no(ragdoll)",
            "19:11:42.000 m=1 off=+0 WO214-DEATH npc=b after_ms=1500 soul_dead=yes how=finished living=no(ragdoll)",
            "19:11:43.000 m=1 off=+0 WO214-DEATH npc=c after_ms=1600 soul_dead=no how=ragdolled living=no(ragdoll)",
            "19:11:44.000 m=1 off=+0 WO214-DEATH npc=d after_ms=1900 soul_dead=no how=still-upright living=yes",
            "19:11:39.700 m=1 off=+0 WO214-DEATHROW npc=c row=x why=owner-owns standin=none -> no death row here: the fall at the soul's death",
        ];
        string s = W210Instruments.Section(null, [], agent, []);
        Assert.Contains("**deaths applied here** (WO-214 D): 4 -- on the ground 3 (the engine's own 1, finished the engine's way 1, ragdolled 1), **still upright 1**", s);
        Assert.Contains("death rows this copy did not take 1 (its own death row played 0, the fall at the death 1)", s);
    }

    [Fact]
    public void The_scorecard_says_how_often_the_partners_figure_held_his_weapon()
    {
        var inp = new Briefing.Input();
        inp.Logs["agent.log"] =
        [
            "08:40:01.000 m=1 off=+0 WO214-HAND figure kcd2mp_1 told drawn=1 cls=efa237c7-3905-4813-b9c3-a32b449c17ad",
            "08:40:01.100 m=2 off=+0 WO214-HAND figure kcd2mp_1 ok (held) [n=1]",
            "08:40:06.100 m=3 off=+0 WO214-HAND figure kcd2mp_1 fixed (equipped+drawn) [n=1]",
            "08:40:11.100 m=4 off=+0 WO214-HAND figure kcd2mp_1 failed (no-item) [n=1]",
            "08:40:12.000 m=5 off=+0 WO214-HAND own drawn=1 cls=efa237c7-3905-4813-b9c3-a32b449c17ad -> sent to 1 [n=4]",   // the sender's line: not a verdict
        ];
        var s = Briefing.Measure(inp).ToDictionary(x => x.Measure, x => x.Value);
        Assert.Equal("2 of 3 (1 only after a fix)", s["the partner's figure held his weapon"]);
        // an older build (no verdict line): no row at all, not a zero
        var old = new Briefing.Input();
        old.Logs["agent.log"] = ["08:40:01.000 m=1 off=+0 [combatviz] ghost 1 drew"];
        Assert.DoesNotContain(Briefing.Measure(old), x => x.Measure == "the partner's figure held his weapon");
    }
}
