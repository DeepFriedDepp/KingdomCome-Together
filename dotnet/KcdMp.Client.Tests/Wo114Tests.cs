using KcdMp.Wire;
using Act = KcdMp.Client.LeashLogic.Act;
using Hold = KcdMp.Client.LeashLogic.Hold;

namespace KcdMp.Client.Tests;

/// <summary>WO-114: the leash rules (LeashLogic) and its wire (docs/WO-114-findings.md).</summary>
public class Wo114Tests
{
    private static LeashLogic New() => new() { Config = LeashLogic.Settings.Default };

    /// <summary>Runs ticks every 250 ms from t for `ms`, all at distance d; returns every action.</summary>
    private static List<LeashLogic.Action> Run(LeashLogic l, ref long t, int ms, double? d, Hold h = Hold.None)
    {
        var all = new List<LeashLogic.Action>();
        for (int i = 0; i < ms / 250; i++) { t += 250; all.AddRange(l.Tick(t, d, h)); }
        return all;
    }

    // ---------------------------------------------------------------- thresholds

    [Fact]
    public void Inside_the_warning_nothing_happens()
    {
        var l = New(); long t = 0;
        Assert.Empty(Run(l, ref t, 5000, 599.9));
        Assert.False(l.CountdownActive);
    }

    [Fact]
    public void Warning_once_at_600_and_never_again_in_the_same_excursion()
    {
        var l = New(); long t = 0;
        var a = Run(l, ref t, 1000, 600.0);
        Assert.Single(a);
        Assert.Equal(Act.Warn, a[0].Kind);
        Assert.Equal(600, a[0].Arg);
        Assert.Empty(Run(l, ref t, 5000, 620));
        Assert.Empty(Run(l, ref t, 2000, 560));   // back under 600 but not under 550: no re-arm
        Assert.Empty(Run(l, ref t, 2000, 610));
    }

    [Fact]
    public void Hysteresis_rearms_only_under_550()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 500, 605);
        Assert.False(l.WarnArmed);
        Run(l, ref t, 500, 550.1);
        Assert.False(l.WarnArmed);
        Run(l, ref t, 500, 549.9);
        Assert.True(l.WarnArmed);
        var a = Run(l, ref t, 500, 601);
        Assert.Equal(Act.Warn, Assert.Single(a).Kind);
    }

    [Fact]
    public void Countdown_at_650_ticks_ten_seconds_then_pulls()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 500, 620);                        // warned
        var a = Run(l, ref t, 250, 650.0);
        Assert.Equal(new LeashLogic.Action(Act.Countdown, 10), Assert.Single(a));
        var rest = Run(l, ref t, 10_000, 700);
        var secs = rest.Where(x => x.Kind == Act.Countdown).Select(x => x.Arg).ToList();
        Assert.Equal(new[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 }, secs);
        var pull = Assert.Single(rest, x => x.Kind == Act.Pull);
        Assert.Equal(Protocol.LeashReasonDistance, pull.Reason);
        Assert.Equal(1, pull.Arg);   // the first pull's seq
        Assert.True(l.PullInFlight);
    }

    [Fact]
    public void Straight_past_650_warns_and_counts_down_together()
    {
        var l = New(); long t = 0;
        var a = Run(l, ref t, 250, 800);
        Assert.Equal(new[] { Act.Warn, Act.Countdown }, a.Select(x => x.Kind).ToArray());
    }

    [Fact]
    public void Back_inside_650_during_the_countdown_cancels_it()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 3000, 700);
        Assert.True(l.CountdownActive);
        var a = Run(l, ref t, 250, 649.9);
        Assert.Equal(Act.Cancel, Assert.Single(a).Kind);
        Assert.False(l.CountdownActive);
        Assert.Empty(Run(l, ref t, 20_000, 640));      // warned already, inside the pull line: quiet, no pull
        var again = Run(l, ref t, 250, 660);           // out again: a fresh 10 s countdown (no second warning)
        Assert.Equal(new LeashLogic.Action(Act.Countdown, 10), Assert.Single(again));
    }

    [Fact]
    public void Leash_off_cancels_a_running_countdown_and_does_nothing()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 2000, 700);
        l.Config = l.Config with { On = false };
        var a = Run(l, ref t, 250, 700);
        Assert.Equal(Act.Cancel, Assert.Single(a).Kind);
        Assert.Empty(Run(l, ref t, 30_000, 5000));
    }

    [Fact]
    public void Thresholds_follow_the_settings()
    {
        var l = new LeashLogic { Config = new LeashLogic.Settings(true, 300, 400) };
        long t = 0;
        Assert.Equal(Act.Warn, Assert.Single(Run(l, ref t, 250, 300)).Kind);
        Assert.Equal(Act.Countdown, Assert.Single(Run(l, ref t, 250, 400)).Kind);
        Run(l, ref t, 250, 240);                       // cancel; 240 < 300 - 50: re-armed
        Assert.True(l.WarnArmed);
    }

    // ---------------------------------------------------------------- holds

    [Theory]
    [InlineData(Hold.HostDowned)]
    [InlineData(Hold.JoinerDowned)]
    [InlineData(Hold.JoinerLoading)]
    [InlineData(Hold.HostCutscene)]
    [InlineData(Hold.JoinerDialogue)]
    [InlineData(Hold.JoinerMenu)]
    [InlineData(Hold.NonHenry)]
    [InlineData(Hold.HostReloading)]
    public void A_hold_freezes_the_countdown_and_it_resumes_where_it_was(Hold h)
    {
        var l = New(); long t = 0;
        Run(l, ref t, 250, 700);                       // 10
        Run(l, ref t, 3000, 700);                      // ... 7 s left
        Assert.Equal(7, l.SecondsLeft);
        var held = Run(l, ref t, 60_000, 700, h);      // a minute downed / in a dialogue / ...
        var hold = Assert.Single(held);
        Assert.Equal(Act.Hold, hold.Kind);
        Assert.Equal(7, hold.Arg);
        Assert.False(l.PullInFlight);
        Assert.Equal(7, l.SecondsLeft);
        var resumed = Run(l, ref t, 250, 700);
        Assert.Equal(new LeashLogic.Action(Act.Countdown, 7), Assert.Single(resumed));   // said again at once
        var rest = Run(l, ref t, 7000, 700);
        Assert.Contains(rest, x => x.Kind == Act.Pull);
    }

    [Fact]
    public void Nothing_starts_while_held_and_the_reason_is_logged_once()
    {
        var l = New(); long t = 0;
        var a = Run(l, ref t, 10_000, 900, Hold.JoinerDowned);
        Assert.Equal(Act.Hold, Assert.Single(a).Kind);
        Assert.Equal("joiner-downed", a[0].Note);
        Assert.Equal(0, a[0].Arg);                     // no countdown was running
        var b = Run(l, ref t, 1000, 900, Hold.JoinerDowned | Hold.JoinerLoading);   // the reasons changed: one more line
        Assert.Equal("joiner-downed,joiner-loading", Assert.Single(b).Note);
        Assert.False(l.CountdownActive);
    }

    [Fact]
    public void A_respawn_hold_never_ends_in_a_pull()
    {
        // WO-114 Phase 2: the countdown is paused throughout a respawn. The
        // player dies 700 m out, is held for the whole 6 s black screen, and
        // wakes within the leash (the wake-spot filter): no pull.
        var l = New(); long t = 0;
        Run(l, ref t, 4000, 700);
        var during = Run(l, ref t, 8000, 700, Hold.JoinerDowned);
        Assert.DoesNotContain(during, x => x.Kind == Act.Pull);
        var after = Run(l, ref t, 30_000, 420);        // woke 420 m from the partner
        Assert.Equal(Act.Cancel, Assert.Single(after).Kind);
    }

    [Fact]
    public void No_position_is_a_hold_not_a_cancel()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 2000, 700);
        var a = Run(l, ref t, 5000, null);
        Assert.Equal("no-joiner-position", Assert.Single(a).Note);
        Assert.True(l.CountdownActive);
    }

    [Fact]
    public void Joiner_flags_map_to_holds()
    {
        Assert.Equal(Hold.None, LeashLogic.JoinerHold(Protocol.LeashFlagInWorld | Protocol.LeashFlagMounted));
        Assert.Equal(Hold.JoinerLoading, LeashLogic.JoinerHold(0));   // not in the host's world yet
        Assert.Equal(Hold.JoinerLoading, LeashLogic.JoinerHold(Protocol.LeashFlagInWorld | Protocol.LeashFlagLoading));
        Assert.Equal(Hold.JoinerDowned | Hold.JoinerCutscene | Hold.JoinerDialogue | Hold.JoinerMenu,
            LeashLogic.JoinerHold(Protocol.LeashFlagInWorld | Protocol.LeashFlagDowned | Protocol.LeashFlagCutscene
                                  | Protocol.LeashFlagDialogue | Protocol.LeashFlagMenu));
    }

    [Fact]
    public void A_long_agent_hitch_never_eats_the_countdown()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 250, 700);                       // 10 s left
        t += 60_000;                                   // the agent stalled for a minute
        var a = l.Tick(t, 700, Hold.None);
        Assert.DoesNotContain(a, x => x.Kind == Act.Pull);
        Assert.Equal(8, l.SecondsLeft);                // at most 2 s taken
    }

    // ---------------------------------------------------------------- pull results

    private static LeashLogic Pulled(out long t)
    {
        var l = New(); t = 0;
        Run(l, ref t, 11_000, 700);
        Assert.True(l.PullInFlight);
        return l;
    }

    [Fact]
    public void Placed_ends_the_pull_and_the_warning_rearms_beside_the_host()
    {
        var l = Pulled(out long t);
        var r = l.OnPullResult(t, l.PullSeq, Protocol.LeashResultPlaced);
        Assert.Equal("placed", Assert.Single(r).Note);
        Assert.False(l.PullInFlight);
        Run(l, ref t, 500, 3);
        Assert.True(l.WarnArmed);
        Assert.Equal(0, l.Failures);
    }

    [Fact]
    public void A_stale_or_foreign_result_is_ignored()
    {
        var l = Pulled(out long t);
        Assert.Empty(l.OnPullResult(t, (byte)(l.PullSeq + 1), Protocol.LeashResultPlaced));
        Assert.Empty(l.OnPullResult(t, l.PullSeq, Protocol.LeashResultNone));
        Assert.True(l.PullInFlight);
    }

    [Fact]
    public void Three_failed_pulls_disarm_the_pulls_but_not_the_warning()
    {
        var l = Pulled(out long t);
        for (int i = 1; i <= 3; i++)
        {
            var r = l.OnPullResult(t, l.PullSeq, Protocol.LeashResultNotPlaced);
            Assert.StartsWith("failed:", r[0].Note);
            if (i < 3)
            {
                Assert.Empty(Run(l, ref t, 19_000, 700));       // the 20 s cool-down
                Run(l, ref t, 12_000, 700);                     // countdown again -> pull
                Assert.True(l.PullInFlight);
            }
            else Assert.Equal(Act.Disarmed, r[1].Kind);
        }
        Assert.True(l.Disarmed);
        Assert.Empty(Run(l, ref t, 60_000, 700));               // no more countdowns
        Run(l, ref t, 500, 100);                                // back beside, then out again
        var a = Run(l, ref t, 500, 700);
        Assert.Equal(Act.Warn, Assert.Single(a).Kind);         // warnings still come
        l.Config = l.Config with { On = false };
        l.Tick(t += 250, 700, Hold.None);
        l.Config = l.Config with { On = true };                 // mp_leash off/on clears the disarm
        Assert.False(l.Disarmed);
    }

    [Fact]
    public void No_answer_in_12_s_is_a_failure()
    {
        var l = Pulled(out long t);
        var a = Run(l, ref t, 12_500, 700);
        Assert.Contains(a, x => x.Kind == Act.PullResult && x.Note == "failed:no-answer");
        Assert.Equal(1, l.Failures);
    }

    [Fact]
    public void A_busy_refusal_is_not_a_failure_and_the_countdown_comes_back()
    {
        var l = Pulled(out long t);
        var r = l.OnPullResult(t, l.PullSeq, Protocol.LeashResultBusy);
        Assert.Equal("busy", Assert.Single(r).Note);
        Assert.Equal(0, l.Failures);
        var a = Run(l, ref t, 250, 700);
        Assert.Equal(new LeashLogic.Action(Act.Countdown, 10), Assert.Single(a));
    }

    // ---------------------------------------------------------------- fast travel

    [Fact]
    public void Host_fast_travel_pulls_at_once_with_no_countdown()
    {
        var l = New(); long t = 0;
        l.NoteHostFastTravel();
        var a = Run(l, ref t, 250, 2400);
        var p = Assert.Single(a);
        Assert.Equal(Act.Pull, p.Kind);
        Assert.Equal(Protocol.LeashReasonFastTravel, p.Reason);
    }

    [Fact]
    public void Host_fast_travel_waits_for_a_downed_joiner_then_pulls()
    {
        var l = New(); long t = 0;
        l.NoteHostFastTravel();
        var held = Run(l, ref t, 8000, 2400, Hold.JoinerDowned);
        Assert.Equal(Act.Hold, Assert.Single(held).Kind);
        Assert.True(l.FastTravelPending);
        var a = Run(l, ref t, 250, 2400);
        Assert.Equal(Act.Pull, Assert.Single(a).Kind);
    }

    [Fact]
    public void Host_fast_travel_to_beside_the_joiner_pulls_nobody()
    {
        var l = New(); long t = 0;
        l.NoteHostFastTravel();
        Assert.Equal(Act.AlreadyBeside, Assert.Single(Run(l, ref t, 250, 20)).Kind);
        Assert.False(l.PullInFlight);
    }

    [Fact]
    public void Host_fast_travel_replaces_a_running_countdown()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 3000, 700);
        l.NoteHostFastTravel();
        var a = Run(l, ref t, 250, 3000);
        Assert.Equal(Protocol.LeashReasonFastTravel, Assert.Single(a).Reason);
        Assert.False(l.CountdownActive);
    }

    [Fact]
    public void A_busy_fast_travel_pull_is_owed_again()
    {
        var l = New(); long t = 0;
        l.NoteHostFastTravel();
        Run(l, ref t, 250, 2400);
        l.OnPullResult(t, l.PullSeq, Protocol.LeashResultBusy);
        Assert.True(l.FastTravelPending);
        Assert.Equal(Protocol.LeashReasonFastTravel, Assert.Single(Run(l, ref t, 250, 2400)).Reason);
    }

    // ---------------------------------------------------------------- the wire

    [Fact]
    public void Leash_command_round_trips_and_fits_its_row()
    {
        var c = new LeashCommand(Protocol.LeashKindPull, 200, Protocol.LeashReasonFastTravel, -12.5f, 3000.25f, 99f, 65535);
        var pkt = c.Build(3);
        var row = Protocol.JoinWireFor(pkt[0])!.Value;
        Assert.Equal(Protocol.LeashDown, row.Down);
        Assert.Equal(Protocol.JoinFrom.Host, row.From);
        Assert.Equal(pkt.Length - 3, row.Min);
        Assert.True(Protocol.TrySplitJoinDown(new byte[] { 0 }.Concat(pkt.Skip(3)).ToArray(), out _, out byte target, out uint jid, out var body));
        Assert.Equal(3, target);
        Assert.Equal(0u, jid);
        Assert.True(LeashCommand.TryDecode(body, out var back));
        Assert.Equal(c, back);
        Assert.False(LeashCommand.TryDecode(body[..^1], out _));
    }

    [Fact]
    public void Leash_command_refuses_a_non_finite_position()
    {
        var pkt = new LeashCommand(Protocol.LeashKindPull, 1, 1, float.NaN, 0, 0, 0).Build(1);
        Assert.False(LeashCommand.TryDecode(pkt.AsSpan(3 + Protocol.JoinHeaderLen), out _));
    }

    [Fact]
    public void Leash_state_round_trips_and_goes_to_the_host()
    {
        var s = new LeashState(0x00FF, 9, Protocol.LeashResultNotPlaced, 700, 690, 250);
        var pkt = s.Build();
        var row = Protocol.JoinWireFor(pkt[0])!.Value;
        Assert.Equal(Protocol.JoinFrom.Joiner, row.From);
        Assert.Equal(Protocol.JoinTargetHost, pkt[3]);
        Assert.True(LeashState.TryDecode(pkt.AsSpan(3 + Protocol.JoinHeaderLen), out var back));
        Assert.Equal(s, back);
        Assert.Equal("in-world,downed,loading,cutscene,dialogue,menu,mounted,fast-travel-refused", Protocol.LeashFlagsText(0x00FF));
    }

    [Fact]
    public void Metres_clamp()
    {
        Assert.Equal((ushort)0, LeashCommand.Metres(-5));
        Assert.Equal((ushort)651, LeashCommand.Metres(650.6));
        Assert.Equal(ushort.MaxValue, LeashCommand.Metres(1e9));
        Assert.Equal((ushort)0, LeashCommand.Metres(double.NaN));
    }

    [Fact]
    public void Distance_is_horizontal_like_the_recorder()
    {
        Assert.Equal(5.0, LeashLogic.Dist2D(0, 0, 3, 4), 6);
        Assert.Equal(LeashCsv.Dist2D(10, 20, 13, 24), (float)LeashLogic.Dist2D(10, 20, 13, 24), 4);
    }
}
