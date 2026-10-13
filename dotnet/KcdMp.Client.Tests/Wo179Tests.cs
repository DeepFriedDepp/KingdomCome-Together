// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-179 (docs/WO-179-findings.md): the scene bus's text, the bounds of the scene guard, the relocation context, the video table.</summary>
public class Wo179Tests
{
    [Fact]
    public void New_pieces_ship_on_with_their_switches()
    {
        Assert.True(Wo179Rules.DefaultBus && Wo179Rules.DefaultVideo && Wo179Rules.DefaultBring && Wo179Rules.DefaultIgnored && Wo179Rules.DefaultFightClear);
    }

    [Fact]
    public void Bus_messages_round_trip_and_fit_the_w164_text()
    {
        Assert.True(Wo179Rules.TryParse(Wo179Rules.Start(7, 'V', "m03_trosky_journey"), out var s));
        Assert.Equal(('S', 7, 'V', "m03_trosky_journey"), (s.Op, s.Seq, s.Kind, s.Name));
        Assert.True(Wo179Rules.TryParse(Wo179Rules.Dest(7, 1690.04f, 2600f, -12.5f), out var r));
        Assert.Equal('R', r.Op); Assert.Equal(1690.0f, r.X, 1); Assert.Equal(-12.5f, r.Z, 1);
        Assert.True(Wo179Rules.TryParse(Wo179Rules.End(7, "skip"), out var e)); Assert.Equal("skip", e.How);
        Assert.True(Wo179Rules.TryParse("T 7 7200", out var t)); Assert.Equal(7200, t.Value);   // WO-183: read from an older build, ignored by the bridge
        Assert.True(Wo179Rules.TryParse(Wo179Rules.SkipKey(7), out var k)); Assert.Equal('K', k.Op);
        Assert.True(Wo179Rules.TryParse(Wo179Rules.Landed(7, 1, 2, 3), out var l)); Assert.Equal('L', l.Op);
        // the longest scene name in the game's video table still fits
        Assert.True(Wo179Rules.Start(65535, 'V', new string('a', 60)).Length <= Protocol.W164TextMax);
    }

    [Theory]
    [InlineData("")]
    [InlineData("S 1 V")]
    [InlineData("S 1 X name")]
    [InlineData("S 1 V bad name")]
    [InlineData("S x V name")]
    [InlineData("R 1 1 2")]
    [InlineData("R 1 1 2 NaN")]
    [InlineData("E 1 maybe")]
    [InlineData("T 1 -5")]
    [InlineData("T 1 9999999")]
    [InlineData("Q 1")]
    public void Malformed_bus_text_is_refused(string text) => Assert.False(Wo179Rules.TryParse(text, out _));

    [Fact]
    public void Crime_scenes_are_detected_and_not_shared()
    {
        Assert.True(Wo179Rules.IsCrimeScene("crime_pillory_trosecko_firstRun"));
        Assert.False(Wo179Rules.IsCrimeScene("m03_trosky_journey"));
        Assert.Equal('V', Wo179Rules.KindOf("Rendered"));
        Assert.Equal('R', Wo179Rules.KindOf("Fader"));
    }

    [Fact]
    public void The_positioning_wait_is_bounded_two_seconds_past_the_later_content_end()
    {
        Assert.Equal(12_000, Wo179Rules.PositioningDeadline(10_000, 0));
        Assert.Equal(15_000, Wo179Rules.PositioningDeadline(10_000, 13_000));
        Assert.True(Wo179Rules.ConversationSceneBoundMs < Wo167Rules.SceneStuckMs);
        Assert.False(Wo167Rules.SceneStuck(1, 0, 1000, 1000, 0, 2500, Wo179Rules.ConversationSceneBoundMs));
        Assert.True(Wo167Rules.SceneStuck(1, 0, 1000, 1000, 0, 3000, Wo179Rules.ConversationSceneBoundMs));
        Assert.False(Wo167Rules.SceneStuck(1, 0, 1000, 1000, 0, 3000));   // the old 20 s when the guard is off
    }

    [Fact]
    public void The_video_table_names_the_games_own_files_and_lengths()
    {
        Assert.True(Wo179Rules.VideoCount > 70);
        Assert.True(Wo179Rules.TryVideo("crime_pillory_trosecko_firstRun", out string p, out double s));
        Assert.EndsWith(".bk2", p);
        Assert.InRange(s, 57.0, 59.0);   // the field's pillory video ran 57.9 s
        Assert.False(Wo179Rules.TryVideo("no_such_video", out _, out _));
        Assert.Equal([3f, 6f, 10f, 15f, 25f], Wo183Rules.LandingRingsM);   // WO-183: the old three, then 15 and 25
    }

    // ---- WO-179B ----

    [Fact]
    public void B_new_switches_ship_on_and_the_watch_message_round_trips()
    {
        Assert.True(Wo179Rules.DefaultSkipBoth && Wo179Rules.DefaultWatchSafe);
        Assert.True(Wo179Rules.TryParse(Wo179Rules.Watch(9, 2048.5f, 1024.25f, 33f), out var w));
        Assert.Equal(('W', 9), (w.Op, w.Seq));
        Assert.Equal(2048.5f, w.X, 1);
        Assert.True(Wo179Rules.Watch(65535, -12345.6f, -12345.6f, -1234.5f).Length <= Protocol.W164TextMax);
        Assert.False(Wo179Rules.TryParse("W 1 1 2", out _));
    }

    [Fact]
    public void B_the_partners_black_is_bounded_three_seconds_and_one_second_past_the_report()
    {
        Assert.Equal(13_000, Wo179Rules.PartnerFadeDeadline(10_000, 0));          // no report: 3 s
        Assert.Equal(11_600, Wo179Rules.PartnerFadeDeadline(10_000, 10_600));     // report at 0.6 s: 1 s past it
        Assert.Equal(13_000, Wo179Rules.PartnerFadeDeadline(10_000, 12_500));     // report at 2.5 s: still 3 s in all
        Assert.Equal(11_000, Wo179Rules.PartnerFadeDeadline(10_000, 9_000));      // a report kept from before the black
        Assert.True(Wo179Rules.PartnerFadeMaxMs <= 3_000);
    }

    [Theory]
    [InlineData("skip-key", false)]
    [InlineData("skipped", false)]          // movement inside the copy: local only (the agent sends no skip for it)
    [InlineData("partner-ended", true)]
    [InlineData("triggerer-skipped", false)]   // its key may only jump a chapter: the end message ends the copy
    [InlineData("both-skipped", false)]
    [InlineData("ended", true)]
    [InlineData("replaced", false)]
    public void B_only_an_end_from_the_triggerers_side_brings_the_black(string why, bool fades)
    {
        Assert.Equal(fades, Wo179Rules.CopyEndFades(why));
    }

    [Fact]
    public void B_the_skip_is_the_second_press_within_four_seconds_like_the_games_own_key()
    {
        Assert.False(Wo179Rules.SkipConfirmed(0, 5_000));          // the first press only arms
        Assert.True(Wo179Rules.SkipConfirmed(5_000, 5_400));
        Assert.True(Wo179Rules.SkipConfirmed(5_000, 9_000));
        Assert.False(Wo179Rules.SkipConfirmed(5_000, 9_001));      // the prompt has gone: this press arms again
    }
}
