using KcdMp.Wire;
using KCDMP_launcher.Models;

namespace KcdMp.Client.Tests;

/// <summary>WO-140: sleeping together and the own-world trap -- the wire, the texts and their refusals,
/// the vote, the one-clock pull, what a separate joiner drops, the DLL's frames, the launcher's modal
/// (docs/WO-140-findings.md).</summary>
public class Wo140Tests
{
    // ---------------------------------------------------------------- the wire

    [Fact]
    public void The_sleep_vote_rides_the_join_channel_either_way()
    {
        var r = Protocol.JoinWireFor(Protocol.SleepVoteUp)!.Value;
        Assert.Equal(Protocol.SleepVoteDown, r.Down);
        Assert.Equal(Protocol.JoinFrom.Either, r.From);   // a joiner's to the host (0xFF), the host's to one joiner
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.LootFixedLen + 1, r.Min);
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.LootFixedLen + Protocol.SleepTextMax, r.Max);
        Assert.Equal(1, Protocol.JoinWire.Count(x => x.Up is 0x68 or 0x69));
        Assert.Equal(0x68, Protocol.SleepVoteUp);
        Assert.Equal(0x69, Protocol.SleepVoteDown);
    }

    [Fact]
    public void A_vote_message_round_trips_through_the_loot_shape()
    {
        var pkt = new LootMsg(Protocol.SleepBegin, 0x00010003u, Wo140Text.Begin("sleep", 1, 2.5f, true)).BuildUp(Protocol.SleepVoteUp, Protocol.JoinTargetHost);
        Assert.Equal(Protocol.SleepVoteUp, pkt[0]);
        Assert.Equal(Protocol.JoinTargetHost, pkt[3]);
        Assert.True(LootMsg.TryDecode(pkt.AsSpan(3 + Protocol.JoinHeaderLen), out var m));
        Assert.Equal(Protocol.SleepBegin, m.Kind);
        Assert.Equal(0x00010003u, m.Tok);
        Assert.True(Wo140Text.TryBegin(m.Text, out var kind, out byte asker, out float hours, out bool save));
        Assert.Equal(("sleep", (byte)1, 2.5f, true), (kind, asker, hours, save));
    }

    [Fact]
    public void The_texts_parse_and_refuse()
    {
        Assert.True(Wo140Text.TryAsk(Wo140Text.Ask("wait", 3, false), out var k, out byte a, out bool s) && k == "wait" && a == 3 && !s);
        Assert.True(Wo140Text.TryAnswer(Wo140Text.Answer("timeout", 2), out var ans, out a) && ans == "timeout" && a == 2);
        Assert.True(Wo140Text.TryCancel(Wo140Text.Cancel("woke", 1), out var why, out a) && why == "woke" && a == 1);
        Assert.False(Wo140Text.TryAsk("nap 1 0", out _, out _, out _));          // not a kind
        Assert.False(Wo140Text.TryAsk("sleep 1 2", out _, out _, out _));        // save is 0|1
        Assert.False(Wo140Text.TryAsk("sleep -1 0", out _, out _, out _));       // not an id
        Assert.False(Wo140Text.TryAnswer("maybe 1", out _, out _));
        Assert.False(Wo140Text.TryCancel("bored 1", out _, out _));
        Assert.False(Wo140Text.TryBegin("sleep 1 0 0", out _, out _, out _, out _));     // no hours
        Assert.False(Wo140Text.TryBegin("sleep 1 25 0", out _, out _, out _, out _));    // more than a day
        Assert.False(Wo140Text.TryBegin("sleep 1 NaN 0", out _, out _, out _, out _));
        Assert.False(Wo140Text.TryBegin("sleep 1 2 0 extra", out _, out _, out _, out _));
        Assert.Equal("sleep 1 2.5 1", Wo140Text.Begin("sleep", 1, 2.5f, true));   // invariant culture
    }

    // ---------------------------------------------------------------- when a vote is needed

    [Theory]
    [InlineData(true,  true,  true,  true,  false, false, 1, true)]    // the host of a shared world, a partner
    [InlineData(true,  true,  true,  true,  false, false, 0, false)]   // solo
    [InlineData(true,  true,  true,  false, false, false, 1, false)]   // host, separate worlds
    [InlineData(true,  true,  false, false, true,  true,  1, true)]    // a joiner in the host's world
    [InlineData(true,  true,  false, false, true,  false, 1, false)]   // a joiner in its own world (the trap)
    [InlineData(true,  false, false, false, true,  true,  1, false)]   // no session
    [InlineData(false, true,  true,  true,  false, false, 1, false)]   // mp_sleep_vote off
    public void A_vote_is_needed_only_in_the_shared_world_with_a_partner(bool on, bool role, bool host, bool shared, bool joinerShared, bool joined, int peers, bool want)
        => Assert.Equal(want, Wo140Rules.VoteRequired(on, role, host, shared, joinerShared, joined, peers));

    // ---------------------------------------------------------------- the vote

    private static Wo140Rules.Vote Vote(params byte[] members)
    {
        var v = new Wo140Rules.Vote { Id = 1, Asker = 1, DeadlineMs = 30_000 };
        foreach (var m in members) v.Members.Add(m);
        return v;
    }

    [Fact]
    public void Everyone_yes_is_a_go_one_no_is_a_no()
    {
        var v = Vote(2, Wo140Rules.LocalMember);
        Assert.Equal(Wo140Rules.Verdict.Pending, v.Evaluate(0));
        v.Answer(2, "yes");
        Assert.Equal(Wo140Rules.Verdict.Pending, v.Evaluate(0));
        v.Answer(Wo140Rules.LocalMember, "yes");
        Assert.Equal(Wo140Rules.Verdict.Go, v.Evaluate(0));

        var n = Vote(2, 3);
        n.Answer(2, "yes");
        n.Answer(3, "no");
        Assert.Equal(Wo140Rules.Verdict.No, n.Evaluate(0));
        Assert.Equal("no", n.NoReason);
    }

    [Fact]
    public void No_answer_in_time_is_a_no_and_a_member_who_left_too()
    {
        var v = Vote(2);
        Assert.Equal(Wo140Rules.Verdict.Pending, v.Evaluate(29_999));
        Assert.Equal(Wo140Rules.Verdict.No, v.Evaluate(30_000));
        Assert.Equal("timeout", v.NoReason);

        // WO-144: a member who left is dropped, not counted as a no (and never waited on)
        var l = Vote(2);
        l.Left(2);
        Assert.Equal(Wo140Rules.Verdict.Go, l.Evaluate(0));
        Assert.Contains((byte)2, l.Dropped);
        var b = Vote(2);
        b.Answer(2, "busy");
        Assert.Equal(Wo140Rules.Verdict.No, b.Evaluate(0));
    }

    [Fact]
    public void An_answer_counts_once_and_only_from_a_member()
    {
        var v = Vote(2);
        v.Answer(9, "no");          // not asked: ignored
        v.Answer(2, "yes");
        v.Answer(2, "no");          // a second answer: ignored
        Assert.Equal(Wo140Rules.Verdict.Go, v.Evaluate(0));
    }

    [Fact]
    public void Vote_ids_survive_the_mods_float_numbers()
    {
        // KCD2's Lua numbers are 32-bit floats: every id must be exact below 2^24
        foreach (byte g in new byte[] { 0, 1, 7, 255 })
            foreach (uint n in new uint[] { 1, 2, 65535, 65536, 70000 })
            {
                uint id = Wo140Rules.VoteId(g, n);
                Assert.True(id < (1u << 24), $"{g}/{n}");
                Assert.Equal(id, (uint)(float)id);
            }
        Assert.Equal(0x00010001u, Wo140Rules.VoteId(1, 1));
        Assert.NotEqual(Wo140Rules.VoteId(1, 1), Wo140Rules.VoteId(2, 1));
    }

    [Fact]
    public void The_plain_words()
    {
        Assert.Equal("Moose wants to sleep. Sleep too?", Wo140Rules.PromptText("Moose", "sleep"));
        Assert.Equal("Moose wants to wait. Wait too?", Wo140Rules.PromptText("Moose", "wait"));
        Assert.Equal("Waiting for other players...", Wo140Rules.WaitingText);
        Assert.Equal("Other players are not ready to sleep yet!", Wo140Rules.NotReadyText);
        Assert.Equal(30, Protocol.SleepVoteTimeoutSeconds);
    }

    [Fact]
    public void The_accepter_runs_the_games_own_sleep_or_wait()
    {
        Assert.Equal(2, Wo140Rules.AccepterSkipId("sleep"));   // the game's own sleep: its screen and its rest (observed, no bed)
        Assert.Equal(1, Wo140Rules.AccepterSkipId("wait"));
        Assert.Equal("wait", Wo140Rules.KindOfSkipId(1));
        Assert.Equal("sleep", Wo140Rules.KindOfSkipId(2));
        Assert.Equal("sleep", Wo140Rules.KindOfSkipId(4));      // reading in bed skips time like a sleep
        Assert.Equal(2f, Wo140Rules.HoursOk(2f));
        Assert.Null(Wo140Rules.HoursOk(0f));
        Assert.Null(Wo140Rules.HoursOk(24.5f));
        Assert.Null(Wo140Rules.HoursOk(float.NaN));
    }

    // ---------------------------------------------------------------- one clock

    [Fact]
    public void A_joiner_ahead_of_the_host_is_pulled_back()
    {
        // the host said 800000 ten seconds ago: now ~800150 (ratio 15)
        Assert.Equal(800150u, Wo140Rules.PullBackTarget(808000, 800000, 10, false));
        Assert.Null(Wo140Rules.PullBackTarget(800200, 800000, 10, false));    // within the 60 s tolerance (WO-144: was 300)
        Assert.Equal(800150u, Wo140Rules.PullBackTarget(800400, 800000, 10, false));   // WO-144: 250 s ahead is pulled now
        Assert.Null(Wo140Rules.PullBackTarget(790000, 800000, 10, false));    // behind: the forward sync's job
    }

    [Fact]
    public void Never_on_a_stale_report_or_during_the_hosts_skip()
    {
        Assert.Null(Wo140Rules.PullBackTarget(900000, 800000, 91, false));   // older than 90 s
        Assert.Null(Wo140Rules.PullBackTarget(900000, 800000, -1, false));   // from the future
        Assert.Null(Wo140Rules.PullBackTarget(900000, 800000, 10, true));    // the host's clock runs fast in its skip
        Assert.Null(Wo140Rules.PullBackTarget(900000, 0, 10, false));        // nothing heard yet
    }

    // ---------------------------------------------------------------- the own-world trap

    [Theory]
    [InlineData(true, false, true, true, true, false, false, true)]    // connected from its own save: separate
    [InlineData(true, false, true, true, true, true,  false, false)]   // joined the host's world
    [InlineData(true, false, true, true, false, false, false, false)]  // at the main menu: the normal join
    [InlineData(true, false, true, true, true, false, true,  false)]   // a join / rejoin / leave runs
    [InlineData(true, false, true, false, true, false, false, false)]  // the host plays separate worlds
    [InlineData(true, false, false, true, true, false, false, false)]  // the host's mode not heard yet
    [InlineData(true, true,  true, true, true, false, false, false)]   // the host itself
    public void Separate_means_a_joiner_in_a_world_that_is_not_the_hosts(bool role, bool host, bool modeKnown, bool hostShared, bool inWorld, bool joined, bool busy, bool want)
        => Assert.Equal(want, Wo140Rules.Separate(role, host, modeKnown, hostShared, inWorld, joined, busy));

    [Fact]
    public void A_separate_joiner_drops_the_hosts_world_but_keeps_the_players()
    {
        foreach (int t in new[] { Protocol.NpcStateDown, Protocol.NpcDamageDown, Protocol.DamageDown, Protocol.DeathDown, Protocol.ActionDown,
                                  Protocol.ItemDropDown, Protocol.ItemClaimDown, Protocol.LootAskDown, Protocol.LootHostDown,
                                  Protocol.QuestHostDown, Protocol.QuestAskDown, Protocol.CrimeHostDown, Protocol.CrimeAskDown,
                                  Protocol.LeashDown, Protocol.LeashStateDown })
            Assert.True(Wo140Rules.DroppedWhenSeparate(t), Wo140Rules.TypeName(t));
        foreach (int t in new[] { Protocol.Ghost, Protocol.Name, Protocol.VoiceDown, Protocol.PlayerStateDown, Protocol.TimeSkipDown,
                                  Protocol.WeatherDown, Protocol.JoinStatusDown, Protocol.SleepVoteDown, Protocol.Disconnect })
            Assert.False(Wo140Rules.DroppedWhenSeparate(t), Wo140Rules.TypeName(t));
    }

    [Fact]
    public void The_plain_message_and_the_support_line()
    {
        Assert.Equal("You loaded your own save. To play in your host's world, quit the game, start it again and wait at the main menu.", Wo140Rules.SeparateText);
        Assert.Equal("MP-JOIN joiner: connected from its own world -- NOT joined (separate)", Wo140Rules.SeparateLogLine);
        Assert.Equal(0x0100, Protocol.LeashFlagSeparate);
        Assert.Contains("separate-world", Protocol.LeashFlagsText(Protocol.LeashFlagSeparate));
    }

    [Fact]
    public void The_launcher_shows_the_own_world_modal_from_the_agents_state()
    {
        var b = new AgentStatusBanner();
        b.Start(false, 0);
        b.Apply(new ConnectionStatusData { State = "connected" }, new JoinStatusData { State = "own-world", Message = Wo140Rules.SeparateText }, true, 1);
        Assert.True(b.OwnWorld);
        Assert.Equal(Wo140Rules.SeparateText, b.JoinMessage);
        b.Apply(new ConnectionStatusData { State = "connected" }, new JoinStatusData { State = "idle" }, true, 2);
        Assert.False(b.OwnWorld);
    }

    // ---------------------------------------------------------------- the DLL's frames

    [Fact]
    public void The_dlls_sleep_frames_parse()
    {
        Assert.True(Wo140Frame.TryParse(new byte[] { 1, 2 }, out var held));
        Assert.Equal((Wo140Frame.KindHeld, (byte)2), (held.Kind, held.Id));
        var st = new byte[] { 2, Wo140Frame.EdgeBegan, 2, 0, 0, 0x80, 0x3F, 2 };   // began, sleep, 1.0 h, state 2
        Assert.True(Wo140Frame.TryParse(st, out var f));
        Assert.Equal(Wo140Frame.EdgeBegan, f.Edge);
        Assert.Equal(1f, f.Hours);
        Assert.Equal("began", Wo140Frame.EdgeName(f.Edge));
        Assert.False(Wo140Frame.TryParse(new byte[] { 2, 9, 2, 0, 0, 0, 0, 0 }, out _));   // no such edge
        Assert.False(Wo140Frame.TryParse(new byte[] { 3, 1 }, out _));
        Assert.False(Wo140Frame.TryParse(new byte[] { 2, 1, 2 }, out _));
    }
}
