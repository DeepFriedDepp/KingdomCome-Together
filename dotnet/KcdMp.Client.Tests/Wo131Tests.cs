using Act = KcdMp.Client.LeashLogic.Act;
using Hold = KcdMp.Client.LeashLogic.Hold;
using V = KcdMp.Client.Wo131Rules.HitVerdict;

namespace KcdMp.Client.Tests;

/// <summary>WO-131: combat and bodies -- the agent's rules (docs/WO-131-findings.md).</summary>
public class Wo131Tests
{
    private static Wo131HitCheck Bound(float dist = 0.4f, ushort ageMs = 120, byte flags = 0, float hp = 80, bool? guarded = true)
        => new(true, ageMs, dist, flags, hp, guarded);

    // ---------------------------------------------------------------- 1b the hit gate

    [Fact]
    public void A_hit_on_a_bound_fresh_near_copy_is_forwarded_unchanged()
    {
        var (v, h) = Wo131Rules.GateJoinerHit(Bound(), 12.5f);
        Assert.Equal(V.Forward, v);
        Assert.Equal(12.5f, h);
    }

    [Fact]
    public void A_copy_the_stream_does_not_drive_is_never_the_host_npc()
    {
        // The field split: the joiner's free copy of a wandering NPC 470 m
        // from the host's -- no binding, so no hit.
        var free = new Wo131HitCheck(false, 65535, -1, 0, 60, null);
        Assert.Equal(V.DropNotBound, Wo131Rules.GateJoinerHit(free, 30).Verdict);
    }

    [Fact]
    public void A_far_copy_is_refused_even_when_bound()
    {
        Assert.Equal(V.Forward, Wo131Rules.GateJoinerHit(Bound(dist: 2.99f), 10).Verdict);
        Assert.Equal(V.DropFar, Wo131Rules.GateJoinerHit(Bound(dist: 3.01f), 10).Verdict);
        Assert.Equal(V.DropFar, Wo131Rules.GateJoinerHit(Bound(dist: 470f), 10).Verdict);
        Assert.Equal(V.DropFar, Wo131Rules.GateJoinerHit(Bound(dist: -1f), 10).Verdict);   // no position: never assumed near
    }

    [Fact]
    public void A_stale_stream_is_refused()
    {
        Assert.Equal(V.Forward, Wo131Rules.GateJoinerHit(Bound(ageMs: 2900), 10).Verdict);   // a 2 s heartbeat + jitter
        Assert.Equal(V.DropStale, Wo131Rules.GateJoinerHit(Bound(ageMs: 3100), 10).Verdict);
    }

    [Fact]
    public void No_answer_from_the_dll_is_a_drop_never_a_pass()
    {
        Assert.Equal(V.DropNoAnswer, Wo131Rules.GateJoinerHit(null, 50).Verdict);
    }

    [Fact]
    public void A_body_dead_on_the_host_takes_no_more_hits()
    {
        Assert.Equal(V.DropDeadOnHost, Wo131Rules.GateJoinerHit(Bound(flags: 0x01), 50).Verdict);
        Assert.Equal(V.Forward, Wo131Rules.GateJoinerHit(Bound(flags: 0x04 | 0x20), 50).Verdict);   // drawn + engaged: alive
    }

    // ---------------------------------------------------------------- 1c host-only death

    [Fact]
    public void A_guarded_copy_at_the_imm_floor_forwards_one_more_point_for_the_host_to_decide()
    {
        // The copy cannot show a lethal blow (imm floors it at 1): the measured
        // drop plus one goes to the host, whose own health decides the death.
        var (v, h) = Wo131Rules.GateJoinerHit(Bound(hp: 1.0f), 8.0f);
        Assert.Equal(V.Forward, v);
        Assert.Equal(9.0f, h);
        Assert.Equal(8.0f, Wo131Rules.GateJoinerHit(Bound(hp: 40f), 8.0f).Health);        // not at the floor
        Assert.Equal(8.0f, Wo131Rules.GateJoinerHit(Bound(hp: 1.0f, guarded: false), 8.0f).Health);   // no guard: nothing hidden
        Assert.Equal(8.0f, Wo131Rules.GateJoinerHit(Bound(hp: 1.0f, guarded: null), 8.0f).Health);    // unreadable: no guess
    }

    [Fact]
    public void Health_follows_the_host_only_on_a_real_change_and_never_for_the_dead()
    {
        Assert.True(Wo131Rules.FollowHpDue(float.NaN, 80, 10_000, dead: false));     // first sample
        Assert.False(Wo131Rules.FollowHpDue(80, 80.3f, 10_000, dead: false));        // under the epsilon
        Assert.True(Wo131Rules.FollowHpDue(80, 60, 10_000, dead: false));            // the host applied a hit
        Assert.False(Wo131Rules.FollowHpDue(80, 60, 100, dead: false));              // rate limit
        Assert.False(Wo131Rules.FollowHpDue(80, 0, 10_000, dead: true));             // death comes as its own order
        Assert.False(Wo131Rules.FollowHpDue(80, -1, 10_000, dead: false));           // unreadable on the host
    }

    [Fact]
    public void The_avatar_restore_asks_for_full_health()
    {
        // SetState clamps to the soul's max; anything at or above 100 is "full".
        Assert.True(Wo131Rules.AvatarRestoreHp >= 100f);
    }

    // ---------------------------------------------------------------- Phase 3 the leash

    private static LeashLogic New() => new() { Config = LeashLogic.Settings.Default };
    private static List<LeashLogic.Action> Run(LeashLogic l, ref long t, int ms, double? d, Hold h = Hold.None)
    {
        var all = new List<LeashLogic.Action>();
        for (int i = 0; i < ms / 250; i++) { t += 250; all.AddRange(l.Tick(t, d, h)); }
        return all;
    }

    [Fact]
    public void The_countdown_does_not_flap_on_the_650_line()
    {
        // The field: start and cancel every few seconds riding along 650 m.
        var l = New(); long t = 0;
        var start = Run(l, ref t, 250, 651);
        Assert.Contains(start, x => x.Kind == Act.Countdown && x.Arg == 10);
        foreach (double d in new[] { 648, 652, 645, 655, 640, 631 })
            Assert.DoesNotContain(Run(l, ref t, 250, d), x => x.Kind == Act.Cancel);
        Assert.True(l.CountdownActive);
        var back = Run(l, ref t, 250, 629.9);
        Assert.Equal(Act.Cancel, Assert.Single(back).Kind);
    }

    [Fact]
    public void The_cancel_margin_follows_custom_settings()
    {
        var l = new LeashLogic { Config = new LeashLogic.Settings(true, 300, 400) };
        long t = 0;
        Run(l, ref t, 500, 420);
        Assert.True(l.CountdownActive);
        Assert.DoesNotContain(Run(l, ref t, 250, 385), x => x.Kind == Act.Cancel);
        Assert.Equal(Act.Cancel, Assert.Single(Run(l, ref t, 250, 379)).Kind);
    }

    [Fact]
    public void Between_630_and_650_nothing_starts()
    {
        var l = New(); long t = 0;
        Run(l, ref t, 500, 610);   // the warning
        Assert.Empty(Run(l, ref t, 20_000, 640));
        Assert.False(l.CountdownActive);
    }
}
