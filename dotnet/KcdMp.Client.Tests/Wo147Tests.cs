using KcdMp.Wire;
using Act = KcdMp.Client.LeashLogic.Act;
using Hold = KcdMp.Client.LeashLogic.Hold;
using Motion = KcdMp.Client.Wo147Rules.Motion;

namespace KcdMp.Client.Tests;

/// <summary>WO-147: the joiner can fight, and the leash pulls (docs/WO-147-findings.md).</summary>
public class Wo147Tests
{
    private static LeashLogic New() => new() { Config = LeashLogic.Settings.Default };

    private static List<LeashLogic.Action> Run(LeashLogic l, ref long t, int ms, double? d, Hold h = Hold.None)
    {
        var all = new List<LeashLogic.Action>();
        for (int i = 0; i < ms / 250; i++) { t += 250; all.AddRange(l.Tick(t, d, h)); }
        return all;
    }

    // ================================================================ the leash: who holds

    [Theory]
    [InlineData(Hold.HostMenu)]
    [InlineData(Hold.HostDialogue)]
    [InlineData(Hold.HostCutscene)]
    [InlineData(Hold.HostDowned)]
    [InlineData(Hold.JoinerMenu)]
    [InlineData(Hold.HostMenu | Hold.HostDialogue | Hold.HostCutscene | Hold.JoinerMenu)]
    public void The_hosts_menus_dialogues_and_cutscenes_and_the_joiners_menu_no_longer_hold(Hold h)
    {
        // The field: "held (host-menu) seconds_left=2 d=1053 m -- no pull while it lasts", for 40 minutes.
        var l = New(); long t = 0;
        var a = Run(l, ref t, 11_000, 1053, h);
        Assert.DoesNotContain(a, x => x.Kind == Act.Hold);
        var pull = Assert.Single(a, x => x.Kind == Act.Pull);
        Assert.Equal(Protocol.LeashReasonDistance, pull.Reason);   // a plain pull, not forced
    }

    [Fact]
    public void A_fast_travel_pull_is_not_owed_forever_behind_the_hosts_menu()
    {
        // The field: after the host's jump, "fast-travel pull owed" behind host-menu for 40 minutes.
        var l = New(); long t = 0;
        l.NoteHostFastTravel();
        var a = Run(l, ref t, 250, 843, Hold.HostMenu);
        Assert.Equal(Protocol.LeashReasonFastTravel, Assert.Single(a, x => x.Kind == Act.Pull).Reason);
        Assert.False(l.FastTravelPending);
    }

    // ================================================================ the cap

    [Theory]
    [InlineData(Hold.JoinerDialogue)]
    [InlineData(Hold.JoinerCutscene)]
    [InlineData(Hold.JoinerDowned)]
    [InlineData(Hold.HostTravelling)]
    public void A_hold_ends_after_a_minute_then_the_countdown_runs_and_the_pull_is_forced(Hold h)
    {
        var l = New(); long t = 0;
        Run(l, ref t, 250, 700);                            // countdown 10
        Run(l, ref t, 2000, 700);                           // 8 s left
        var held = Run(l, ref t, LeashLogic.HoldCapMs - 250, 700, h);
        Assert.DoesNotContain(held, x => x.Kind == Act.Pull);
        Assert.DoesNotContain(held, x => x.Kind == Act.HoldCapped);
        var cap = Run(l, ref t, 500, 700, h);
        var capped = Assert.Single(cap, x => x.Kind == Act.HoldCapped);
        Assert.True(capped.Arg >= 60);
        Assert.True(l.HoldIsCapped);
        var rest = Run(l, ref t, 9000, 700, h);             // still in it: the 8 s left run anyway
        var pull = Assert.Single(rest, x => x.Kind == Act.Pull);
        Assert.Equal(Protocol.LeashReasonDistance | Protocol.LeashReasonForced, pull.Reason);
        Assert.Single(cap.Concat(rest), x => x.Kind == Act.HoldCapped);   // said once
    }

    [Theory]
    [InlineData(Hold.JoinerLoading)]
    [InlineData(Hold.HostLoading)]
    [InlineData(Hold.HostReloading)]
    [InlineData(Hold.NonHenry)]
    public void A_load_or_a_non_henry_stretch_is_never_capped(Hold h)
    {
        var l = New(); long t = 0;
        Run(l, ref t, 2000, 700);
        var a = Run(l, ref t, 5 * 60_000, 700, h);
        Assert.DoesNotContain(a, x => x.Kind is Act.Pull or Act.HoldCapped);
        Assert.False(l.HoldIsCapped);
    }

    [Fact]
    public void A_missing_position_is_never_capped()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 2000, 700);
        var a = Run(l, ref t, 3 * 60_000, null);
        Assert.DoesNotContain(a, x => x.Kind is Act.Pull or Act.HoldCapped);
        Assert.True(l.CountdownActive);
    }

    [Fact]
    public void A_capped_dialogue_that_turns_into_a_load_holds_again()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 2000, 700);
        Run(l, ref t, LeashLogic.HoldCapMs + 500, 700, Hold.JoinerDialogue);
        Assert.True(l.HoldIsCapped);
        var a = Run(l, ref t, 30_000, 700, Hold.JoinerDialogue | Hold.JoinerLoading);
        Assert.DoesNotContain(a, x => x.Kind == Act.Pull);
        Assert.False(l.HoldIsCapped);
    }

    [Fact]
    public void The_cap_starts_over_after_the_hold_ends()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 2000, 700);
        Run(l, ref t, 40_000, 700, Hold.JoinerDialogue);   // 40 s held
        Run(l, ref t, 250, 640);                            // a moment free (inside the pull line, no pull)
        var a = Run(l, ref t, 40_000, 700, Hold.JoinerDialogue);   // another 40 s: a new hold, under the cap
        Assert.DoesNotContain(a, x => x.Kind is Act.Pull or Act.HoldCapped);
    }

    [Fact]
    public void An_owed_fast_travel_pull_is_forced_at_the_cap()
    {
        var l = New(); long t = 0;
        l.NoteHostFastTravel();
        var a = Run(l, ref t, LeashLogic.HoldCapMs + 1000, 2400, Hold.JoinerCutscene);
        var pull = Assert.Single(a, x => x.Kind == Act.Pull);
        Assert.Equal(Protocol.LeashReasonFastTravel | Protocol.LeashReasonForced, pull.Reason);
    }

    [Fact]
    public void A_busy_answer_to_a_forced_fast_travel_pull_owes_it_again()
    {
        var l = New(); long t = 0;
        l.NoteHostFastTravel();
        Run(l, ref t, LeashLogic.HoldCapMs + 1000, 2400, Hold.JoinerCutscene);
        Assert.True(l.PullInFlight);
        l.OnPullResult(t, l.PullSeq, Protocol.LeashResultBusy);
        Assert.True(l.FastTravelPending);
    }

    // ================================================================ timeouts and failures

    [Theory]
    [InlineData(null, 12_000)]
    [InlineData(1.0, 12_000)]
    [InlineData(4000.0, 14_000)]
    [InlineData(40_000.0, 90_000)]
    [InlineData(double.NaN, 12_000)]
    public void The_pulls_timeout_follows_the_round_trip(double? rtt, int want) =>
        Assert.Equal(want, LeashLogic.TimeoutForRtt(rtt));

    [Fact]
    public void A_late_answer_inside_a_lag_aware_timeout_is_not_a_failure()
    {
        // The field: pull #1 sent 23:22:10.5, the joiner's answer ~19 s later; the flat 12 s called it no-answer.
        var l = New(); long t = 0;
        Run(l, ref t, 11_000, 971);
        Assert.True(l.PullInFlight);
        l.PullTimeoutMs = 2 * 20_000 + 4000;                // the partner's samples ran 20 s behind
        var mid = Run(l, ref t, 19_000, 971);
        Assert.DoesNotContain(mid, x => x.Kind == Act.PullResult);
        var r = l.OnPullResult(t, l.PullSeq, Protocol.LeashResultPlaced);
        Assert.Equal("placed", Assert.Single(r).Note);
        Assert.Equal(0, l.Failures);
    }

    [Fact]
    public void Three_failures_pause_the_pulls_then_they_come_back_by_themselves()
    {
        var l = New(); long t = 0;
        for (int i = 0; i < 3; i++)
        {
            Run(l, ref t, 11_000, 900);
            if (!l.PullInFlight) Run(l, ref t, 11_000, 900);
            Assert.True(l.PullInFlight);
            l.OnPullResult(t, l.PullSeq, Protocol.LeashResultNotPlaced);
            if (i < 2) Run(l, ref t, LeashLogic.FailCooldownMs, 900);
        }
        Assert.True(l.Disarmed);
        Assert.DoesNotContain(Run(l, ref t, LeashLogic.FailPauseMs - 5000, 900), x => x.Kind == Act.Countdown);
        var back = Run(l, ref t, 6000, 900);
        Assert.Contains(back, x => x.Kind == Act.Rearmed);
        Assert.Contains(back, x => x.Kind == Act.Countdown);
        Assert.False(l.Disarmed);
    }

    // ================================================================ the joiner's verdict on a pull

    [Fact]
    public void The_joiner_refuses_only_unsafe_pulls_and_a_forced_one_only_while_loading()
    {
        Assert.Equal(Hold.None, Wo147Rules.JoinerRefusesPull(Hold.JoinerMenu, false));
        Assert.Equal(Hold.JoinerDialogue, Wo147Rules.JoinerRefusesPull(Hold.JoinerDialogue | Hold.JoinerMenu, false));
        Assert.Equal(Hold.JoinerCutscene | Hold.JoinerDowned, Wo147Rules.JoinerRefusesPull(Hold.JoinerCutscene | Hold.JoinerDowned, false));
        Assert.Equal(Hold.None, Wo147Rules.JoinerRefusesPull(Hold.JoinerDialogue | Hold.JoinerCutscene | Hold.JoinerDowned | Hold.JoinerMenu, true));
        Assert.Equal(Hold.JoinerLoading, Wo147Rules.JoinerRefusesPull(Hold.JoinerLoading | Hold.JoinerDialogue, true));
        Assert.Equal(Hold.JoinerLoading, Wo147Rules.JoinerRefusesPull(LeashLogic.JoinerHold(0), true));   // not in the host's world
    }

    [Fact]
    public void The_forced_bit_rides_the_reason_and_names_itself()
    {
        ushort r = Protocol.LeashReasonDistance | Protocol.LeashReasonForced;
        Assert.Equal(Protocol.LeashReasonDistance, Protocol.LeashReasonBase(r));
        Assert.Equal("distance,forced", Protocol.LeashReasonName(r));
        Assert.Equal("fast-travel", Protocol.LeashReasonName(Protocol.LeashReasonFastTravel));
        var c = new LeashCommand(Protocol.LeashKindPull, 7, (ushort)(Protocol.LeashReasonFastTravel | Protocol.LeashReasonForced), 1, 2, 3, 900);
        var pkt = c.Build(1);
        Assert.True(LeashCommand.TryDecode(pkt.AsSpan(3 + Protocol.JoinHeaderLen), out var back));
        Assert.Equal(c, back);
        Assert.Equal("in-world,flying", Protocol.LeashFlagsText(Protocol.LeashFlagInWorld | Protocol.LeashFlagFlying));
    }

    // ================================================================ real positions (the distance sanity check)

    [Fact]
    public void Samples_that_arrive_a_minute_late_are_not_a_reading()
    {
        var p = new Wo147Rules.PeerPositions();
        long arrival = 1_000_000; uint sender = 50_000;
        for (int i = 0; i < 40; i++) { p.Feed(1000 + i, 1000, sender, arrival); sender += 250; arrival += 250; }   // live, 4 Hz
        Assert.True(p.Fresh(arrival));
        Assert.True(p.LagMs < 100);
        // The link backs up: a minute of samples (each sent 250 ms apart) lands in 3 s.
        long burstStart = arrival + 60_000;
        for (int i = 0; i < 240; i++) p.Feed(1040 + i * 10, 1000, sender + (uint)(i * 250), burstStart + i * 12);
        Assert.True(p.LagMs > Wo147Rules.PositionStaleMs);
        Assert.False(p.Fresh(burstStart + 240 * 12));
    }

    [Fact]
    public void A_burst_of_old_walking_samples_is_never_a_sprint()
    {
        // Speed is measured on the SENDER's clock: 10 m per 250 ms of the sender's time is 40 m/s at most,
        // however fast the burst arrives (the field's host read 742 -> 1,481 -> 1,926 m a second apart).
        var p = new Wo147Rules.PeerPositions();
        uint s = 1000; long a = 5000;
        for (int i = 0; i < 20; i++) { p.Feed(100 + i * 1.5f, 100, s, a); s += 250; a += 250; }   // 6 m/s
        for (int i = 0; i < 40; i++) { var m = p.Feed(130 + i * 1.5f, 100, s, a + i); s += 250; Assert.Equal(Motion.Normal, m); }
        Assert.True(p.LastSpeedMps < 7);
    }

    [Fact]
    public void A_sustained_flight_is_flying_and_a_single_step_is_a_jump()
    {
        var p = new Wo147Rules.PeerPositions();
        uint s = 1000; long a = 5000;
        p.Feed(0, 0, s, a);
        s += 250; a += 250;
        Assert.Equal(Motion.Jump, p.Feed(400, 0, s, a));           // 400 m in one sample: a teleport
        for (int i = 1; i <= 6; i++) { s += 250; a += 250; p.Feed(400 + i * 125, 0, s, a); }   // 500 m/s for 1.5 s
        Assert.Equal(Motion.Flying, p.LastMotion);
        Assert.True(p.LastSpeedMps > 400);
        for (int i = 1; i <= 3; i++) { s += 250; a += 250; p.Feed(1150 + i * 1.2f, 0, s, a); }
        Assert.Equal(Motion.Normal, p.LastMotion);
    }

    [Fact]
    public void An_old_sender_without_stamps_is_judged_by_arrival_only()
    {
        var p = new Wo147Rules.PeerPositions();
        p.Feed(1, 1, 0, 1000);
        Assert.Equal(0, p.LagMs);
        Assert.True(p.Fresh(2000));
        Assert.False(p.Fresh(7000));   // nothing new for 5 s
    }

    [Fact]
    public void The_joiners_own_flight_is_told_from_a_ride_and_a_teleport()
    {
        var m = new Wo147Rules.OwnMotion();
        long t = 0;
        m.Feed(0, 0, t, mounted: true);
        for (int i = 1; i <= 20; i++) Assert.Equal(Motion.Normal, m.Feed(i * 4f, 0, t += 250, mounted: true));   // a gallop, 16 m/s
        Assert.Equal(Motion.Jump, m.Feed(80 + 460, 0, t += 250, mounted: false));   // a wake 460 m away
        for (int i = 1; i <= 4; i++) m.Feed(540 + i * 30f, 0, t += 250, mounted: false);   // F3: 120 m/s
        Assert.True(m.Flying);
        Assert.Equal(Motion.Normal, m.Feed(660.5f, 0, t += 250, mounted: false));
        Assert.False(m.Flying);
    }

    // ================================================================ the pull's fallback spot

    [Fact]
    public void The_fallback_spot_is_where_the_host_just_stood()
    {
        var trail = new List<(float X, float Y, float Z, long AtMs)>
        {
            (90, 100, 50, 1_000), (97, 100, 50, 20_000), (98.5f, 100, 50.2f, 21_000), (100, 100, 50.5f, 22_000),
        };
        var s = Wo147Rules.PullFallbackSpot(100, 100, 50.5f, 800, 800, trail, 22_000);
        Assert.Equal(98.5f, s.X);          // 1.5 m away, the newest such point
        Assert.Equal(50.5f, s.Z, 3);       // + 0.3 m
    }

    [Fact]
    public void With_no_trail_the_fallback_is_a_metre_toward_the_joiner()
    {
        var s = Wo147Rules.PullFallbackSpot(100, 100, 50, 100, 700, new List<(float, float, float, long)>(), 0);
        Assert.Equal(100f, s.X, 3);
        Assert.Equal(101f, s.Y, 3);
        Assert.Equal(50.5f, s.Z, 3);
        var old = new List<(float, float, float, long)> { (97, 100, 50, 0) };   // older than 30 s: not used
        var s2 = Wo147Rules.PullFallbackSpot(100, 100, 50, 100, 100, old, 40_000);
        Assert.Equal(101f, s2.X, 3);       // the joiner on the host's spot: +X
    }

    [Fact]
    public void A_trail_point_on_another_level_is_not_used()
    {
        var trail = new List<(float X, float Y, float Z, long AtMs)> { (97, 100, 40, 1000) };   // 10 m below (a stair, a cellar)
        var s = Wo147Rules.PullFallbackSpot(100, 100, 50, 100, 90, trail, 2000);
        Assert.Equal(99f, s.Y, 3);
    }

    // ================================================================ the host's stuck "paused" (the field's 45 minutes)

    private delegate void SpanLine(ReadOnlySpan<char> line);

    private static void Feed(LogTailGameTransport tail, string line)
    {
        var process = typeof(LogTailGameTransport).GetMethod("ProcessLine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((SpanLine)Delegate.CreateDelegate(typeof(SpanLine), tail, process))(line.AsSpan());
    }

    [Fact]
    public void A_skip_the_engine_cancels_ends_the_pause()
    {
        // The field: a quest sleep that cut to a cutscene -- "started async waiting", then "has canceled async
        // waiting", never "is ready": the host read "paused: skip-time" for 45 minutes.
        var tail = new LogTailGameTransport(new HttpGameTransport("http://127.0.0.1:1"), "unused.log");
        var skip = new List<bool>(); var pause = new List<bool>();
        tail.SkipTimeStateChanged += a => skip.Add(a);
        tail.PauseStateChanged += p => pause.Add(p);
        Feed(tail, "Readiness observer 'AfterSkipTime' with category bitmask '16273' started async waiting");
        Feed(tail, "Readiness observer 'AfterSkipTime' with category bitmask '16273' has canceled async waiting");
        Assert.Equal(new[] { true, false }, skip);
        Assert.Equal(new[] { true, false }, pause);
        Assert.False(tail.SkipTimeActive);
        // an ordinary skip still ends on "is ready"
        Feed(tail, "Readiness observer 'AfterSkipTime' with category bitmask '16273' started async waiting");
        Feed(tail, "Readiness observer 'AfterSkipTime' with category bitmask '16273' is ready");
        Assert.Equal(new[] { true, false, true, false }, skip);
    }

    [Fact]
    public void A_skip_marker_left_open_is_cleared_after_three_minutes()
    {
        var tail = new LogTailGameTransport(new HttpGameTransport("http://127.0.0.1:1"), "unused.log");
        var skip = new List<bool>();
        tail.SkipTimeStateChanged += a => skip.Add(a);
        Feed(tail, "Readiness observer 'AfterSkipTime' with category bitmask '16273' started async waiting");
        Feed(tail, "[KCD2-MP] DATA 1 2 3");
        Assert.True(tail.SkipTimeActive);   // a real skip resolves in seconds; nothing yet
        typeof(LogTailGameTransport).GetField("_skipTimeSinceUtc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(tail, DateTime.UtcNow.AddSeconds(-(LogTailGameTransport.SkipTimeMaxOpenSeconds + 5)));
        Feed(tail, "[KCD2-MP] DATA 1 2 3");
        Assert.False(tail.SkipTimeActive);
        Assert.Equal(new[] { true, false }, skip);
    }

    // ================================================================ the frame backlog (mp_npc_catchup)

    [Fact]
    public void Behind_a_sample_a_newer_one_of_the_same_npc_supersedes_is_skipped()
    {
        // the field: the joiner's processor ran minutes behind; every queued position went to the game in turn
        Assert.True(Wo147Rules.SupersededUnderLag(true, 900, seq: 10, flags: 0x04, newestSeq: 14, newestFlags: 0x04));
        // keeping up: nothing is skipped
        Assert.False(Wo147Rules.SupersededUnderLag(true, 40, 10, 0x04, 14, 0x04));
        Assert.False(Wo147Rules.SupersededUnderLag(true, Wo147Rules.CatchupLagMs - 1, 10, 0x04, 14, 0x04));
        // the newest one itself always goes
        Assert.False(Wo147Rules.SupersededUnderLag(true, 900, 14, 0x04, 14, 0x04));
        // switched off
        Assert.False(Wo147Rules.SupersededUnderLag(false, 900, 10, 0x04, 14, 0x04));
    }

    [Theory]
    [InlineData(0x04, 0x05)]   // the newer one is dead: this one (alive) carries the transition -- kept
    [InlineData(0x00, 0x04)]   // the weapon drawn in between
    [InlineData(0x02, 0x00)]   // a knockout that ended
    public void A_change_of_flags_is_never_skipped(byte flags, byte newest) =>
        Assert.False(Wo147Rules.SupersededUnderLag(true, 900, 10, flags, 14, newest));

    [Theory]
    [InlineData(0x08)]                            // a swing cue: an event
    [InlineData(Protocol.NpcStateFlagResync)]     // a resync row: a one-off
    public void An_event_or_a_resync_is_never_skipped(byte transient) =>
        Assert.False(Wo147Rules.SupersededUnderLag(true, 900, 10, (byte)(0x04 | transient), 14, (byte)(0x04 | transient)));

    [Fact]
    public void A_stream_is_silent_on_the_agents_word_only_once_the_mod_has_its_last_sample()
    {
        // nothing read for 3.5 s, the last sample handed on: silent (once)
        Assert.True(Wo147Rules.SilenceDue(nowMs: 10_000, lastArrivalMs: 6_500, lastProcessedArrivalMs: 6_500, alreadySent: false));
        Assert.False(Wo147Rules.SilenceDue(10_000, 6_500, 6_500, alreadySent: true));
        // the last sample still in the queue (the field: released while it waited, started again by it): not yet
        Assert.False(Wo147Rules.SilenceDue(10_000, 6_500, 5_000, false));
        // a sample read 2 s ago: the stream is alive
        Assert.False(Wo147Rules.SilenceDue(10_000, 8_000, 8_000, false));
        Assert.Equal(3000, Wo147Rules.SilenceReleaseMs);
    }
}
