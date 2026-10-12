// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-177 (docs/WO-177-findings.md): the agent's pure rules. The Lua half is tools/Test-WO177Synthetic.lua.</summary>
public class Wo177Tests
{
    // ---- A5: no load while a fight is running

    [Theory]
    [InlineData("join ok", "ok")]
    [InlineData("leave fight", "fight")]
    [InlineData("w175-repaired hit", "hit")]
    [InlineData("  exit-to-menu   fight  ", "fight")]
    public void The_lua_answer_is_read_as_the_last_word(string arg, string expected) => Assert.Equal(expected, Wo177Rules.ParseReason(arg));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("join")]
    [InlineData("join maybe")]
    [InlineData("join OK")]
    public void An_answer_that_is_not_one_of_the_three_words_is_no_answer_never_ok(string? arg) => Assert.Null(Wo177Rules.ParseReason(arg));

    [Fact]
    public void Only_ok_lets_a_load_start()
    {
        Assert.True(Wo177Rules.IsQuiet("ok"));
        Assert.False(Wo177Rules.IsQuiet("fight"));
        Assert.False(Wo177Rules.IsQuiet("hit"));
        Assert.False(Wo177Rules.IsQuiet(null));   // an unanswered question is not "quiet"
        Assert.False(Wo177Rules.IsQuiet(""));
    }

    [Fact]
    public void The_wait_is_bounded_and_the_quiet_window_is_ten_seconds()
    {
        Assert.False(Wo177Rules.GaveUp(Wo177Rules.MaxWaitMs - 1));
        Assert.True(Wo177Rules.GaveUp(Wo177Rules.MaxWaitMs));
        Assert.Equal(180_000, Wo177Rules.MaxWaitMs);
        Assert.Equal(10.0, Wo177Rules.QuietAfterHitS);
        Assert.True(Wo177Rules.ProbeTimeoutMs > Wo177Rules.PollMs / 2 && Wo177Rules.PollMs >= 500);
    }

    // ---- B2: corpses agree (W164 kinds 10 and 11)

    [Fact]
    public void The_corpse_kinds_are_appended_after_story_fight_and_named()
    {
        Assert.Equal(10, Protocol.W164CorpseAsk);
        Assert.Equal(11, Protocol.W164CorpseAnswer);
        Assert.Equal("corpse-ask", Protocol.W164KindName(Protocol.W164CorpseAsk));
        Assert.Equal("corpse-answer", Protocol.W164KindName(Protocol.W164CorpseAnswer));
        Assert.Equal("story-fight", Protocol.W164KindName(Protocol.W164StoryFight));   // nothing before them moved
        Assert.Equal(10, Protocol.Version);                                            // append-only: no bump
    }

    [Fact]
    public void A_corpse_question_round_trips_and_fits_the_text_maximum()
    {
        var entries = new[] { ("tzel_frantisek", 1577, 2576), ("ttac_procek_long_name_x", -12, 100000), ("tapo_kasparBandit_12", 5, -5), ("fourth", 1, 1) };
        string text = W164Text.CorpseAsk(entries);
        Assert.True(text.Length <= Protocol.W164TextMax, text);
        Assert.True(W164Text.TryParseCorpseAsk(text, out var back));
        Assert.Equal(2, back.Count);   // the third would pass the text maximum: it waits for the next question
        Assert.Equal(("tzel_frantisek", 1577, 2576), back[0]);
        Assert.Equal(("ttac_procek_long_name_x", -12, 100000), back[1]);
        Assert.Equal(3, W164Text.CorpseAsk(new[] { ("a", 1, 1), ("b", 2, 2), ("c", 3, 3), ("d", 4, 4) }).Split(' ').Length);   // at most three
    }

    [Theory]
    [InlineData("")]
    [InlineData("tzel_a")]
    [InlineData("tzel_a:1")]
    [InlineData("tzel_a:1:x")]
    [InlineData("tzel a:1:2")]
    [InlineData("tzel_a:100001:2")]
    [InlineData("a:1:2 b:1:2 c:1:2 d:1:2")]
    public void A_malformed_corpse_question_is_refused(string text) => Assert.False(W164Text.TryParseCorpseAsk(text, out _));

    [Fact]
    public void A_corpse_answer_round_trips_and_only_the_four_values_pass()
    {
        string text = W164Text.CorpseAnswer(new[] { ("tzel_a", (byte)1), ("tzel_b", (byte)2), ("tzel_c", (byte)3), ("tzel_d", (byte)0), ("tzel_e", (byte)2) });
        Assert.Equal("tzel_a=1 tzel_b=2 tzel_c=3 tzel_d=0", text);   // four per message
        Assert.True(text.Length <= Protocol.W164TextMax);
        Assert.True(W164Text.TryParseCorpseAnswer(text, out var back));
        Assert.Equal(("tzel_b", (byte)2), back[1]);
        Assert.False(W164Text.TryParseCorpseAnswer("tzel_a=4", out _));
        Assert.False(W164Text.TryParseCorpseAnswer("tzel_a=", out _));
        Assert.False(W164Text.TryParseCorpseAnswer("tzel_a", out _));
        Assert.False(W164Text.TryParseCorpseAnswer("a=1 b=1 c=1 d=1 e=1", out _));
    }

    [Fact]
    public void A_corpse_question_and_answer_survive_the_join_channel_framing()
    {
        var up = new LootMsg(Protocol.W164CorpseAsk, 9, "tzel_a:1:2").BuildUp(Protocol.W164Up, Protocol.JoinTargetHost);
        Assert.True(LootMsg.TryDecode(up.AsSpan(3 + Protocol.JoinHeaderLen).ToArray(), out var m));
        Assert.Equal(Protocol.W164CorpseAsk, m.Kind);
        Assert.True(W164Text.TryParseCorpseAsk(m.Text, out _));
    }

    // ---- B3: a working NPC's tool swing

    [Fact]
    public void The_calm_line_names_the_calm_and_the_ones_that_turned_to_a_fight()
    {
        var (calm, fighting) = Wo177Rules.ParseCalm("ttac_blacksmith tzel_man_2 ! tapo_bandit_1 not-a-name! wolf_2");
        Assert.Equal(new[] { "ttac_blacksmith", "tzel_man_2" }, calm);
        Assert.Equal(new[] { "tapo_bandit_1", "wolf_2" }, fighting);   // a word that is no NPC name is skipped
        var (c2, f2) = Wo177Rules.ParseCalm(null);
        Assert.Empty(c2); Assert.Empty(f2);
        var (c3, f3) = Wo177Rules.ParseCalm("only_calm");
        Assert.Single(c3); Assert.Empty(f3);
    }

    [Theory]
    [InlineData(true, 1_000L, 1_000L, false, true)]    // listed calm just now: a tool swing
    [InlineData(true, 1_000L, 5_999L, false, true)]    // 5 s minus 1 ms later still calm
    [InlineData(true, 1_000L, 6_000L, false, false)]   // the listing expired (5 s)
    [InlineData(true, 0L, 6_000L, false, false)]       // never listed: a fight's blow (fail open: damage counts)
    [InlineData(false, 1_000L, 1_000L, false, false)]  // mp_tool_swing off
    [InlineData(true, 1_000L, 1_000L, true, false)]    // an arrow is never a tool
    [InlineData(true, 2_000L, 1_000L, false, false)]   // a clock that went back is not "calm"
    public void A_hit_is_a_tool_swing_only_for_an_npc_listed_calm_a_moment_ago(bool on, long since, long now, bool missile, bool expected)
        => Assert.Equal(expected, Wo177Rules.IsToolSwing(on, since, now, missile));

    [Fact]
    public void The_tool_swing_gate_ships_on()
    {
        Assert.True(Wo177Rules.DefaultToolSwing);
        Assert.Equal(5_000, Wo177Rules.CalmTtlMs);
    }

    [Fact]
    public void The_player_is_told_in_plain_words_which_wait_it_is()
    {
        Assert.Contains("fight", Wo177Rules.WaitText("fight"));
        Assert.Contains("hit", Wo177Rules.WaitText("hit"));
        Assert.Contains("nothing was loaded", Wo177Rules.GaveUpText);
    }
}
