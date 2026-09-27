using KcdMp.Wire;
using V = KcdMp.Client.Wo132Rules.GhostHitVerdict;
using E = KcdMp.Client.Wo132Rules.EngageVerdict;

namespace KcdMp.Client.Tests;

/// <summary>WO-132: damage safety and engagement rules (docs/WO-132-findings.md s2-s3).</summary>
public class Wo132RulesTests
{
    private static readonly DateTime T0 = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------- nobody is hit while dead or waking

    [Fact]
    public void A_down_player_is_blocked_until_five_seconds_after_the_wake()
    {
        var g = new LifeGate();
        Assert.Equal(LifeGate.Why.Open, g.Check(T0));
        Assert.True(g.Down(T0));
        Assert.Equal(LifeGate.Why.Down, g.Check(T0.AddSeconds(6)));      // the black screen, the grave, the wake 100 m away
        Assert.Equal(LifeGate.Why.Down, g.Check(T0.AddSeconds(60)));
        Assert.True(g.Up(T0.AddSeconds(61)));
        Assert.Equal(LifeGate.Why.Waking, g.Check(T0.AddSeconds(61)));
        Assert.Equal(LifeGate.Why.Waking, g.Check(T0.AddSeconds(65.9)));
        Assert.Equal(LifeGate.Why.Open, g.Check(T0.AddSeconds(66.1)));
    }

    [Fact]
    public void A_second_down_or_up_does_not_restart_the_down_but_an_up_restarts_the_grace()
    {
        var g = new LifeGate();
        Assert.True(g.Down(T0));
        Assert.False(g.Down(T0.AddSeconds(3)));                          // the death after the downed bit: one down
        Assert.True(g.Up(T0.AddSeconds(10)));                            // the respawn announcement
        Assert.False(g.Up(T0.AddSeconds(12)));                           // the downed bit clearing: the grace runs from here
        Assert.Equal(LifeGate.Why.Waking, g.Check(T0.AddSeconds(16.5)));
        Assert.Equal(LifeGate.Why.Open, g.Check(T0.AddSeconds(17.1)));
    }

    [Fact]
    public void A_down_that_never_wakes_stops_blocking_after_three_minutes()
    {
        var g = new LifeGate();
        g.Down(T0);
        Assert.Equal(LifeGate.Why.Down, g.Check(T0 + LifeGate.MaxDown - TimeSpan.FromSeconds(1)));
        Assert.Equal(LifeGate.Why.Open, g.Check(T0 + LifeGate.MaxDown + TimeSpan.FromSeconds(1)));
        Assert.False(g.IsDown);
    }

    // ---------------------------------------------------------------- no tick forwarding

    [Fact]
    public void The_sampler_never_forwards_when_the_native_watch_is_armed()
    {
        Assert.Equal(V.SupersededByNative, Wo132Rules.JudgeGhostHit(true, 26f, LifeGate.Why.Open));
        Assert.Equal(V.SupersededByNative, Wo132Rules.JudgeGhostHit(true, 0.05f, LifeGate.Why.Open));
    }

    [Fact]
    public void Without_the_native_watch_a_bleed_tick_is_never_a_hit()
    {
        // The field: 93 drops of ~0.05 hp every ~0.3 s -- a second death, "bleeding".
        Assert.Equal(V.Tick, Wo132Rules.JudgeGhostHit(false, 0.05f, LifeGate.Why.Open));
        Assert.Equal(V.Tick, Wo132Rules.JudgeGhostHit(false, 0.99f, LifeGate.Why.Open));
        Assert.Equal(V.Tick, Wo132Rules.JudgeGhostHit(false, float.NaN, LifeGate.Why.Open));
        Assert.Equal(V.Forward, Wo132Rules.JudgeGhostHit(false, 12f, LifeGate.Why.Open));
        Assert.Equal(V.Blocked, Wo132Rules.JudgeGhostHit(false, 26f, LifeGate.Why.Down));
        Assert.Equal(V.Blocked, Wo132Rules.JudgeGhostHit(false, 12f, LifeGate.Why.Waking));
    }

    // ---------------------------------------------------------------- one path per hit

    [Fact]
    public void An_avatar_hit_never_goes_out_on_the_guid_route()
    {
        Assert.False(Wo132Rules.GuidRouteAllowed("kcd2mp_1"));
        Assert.False(Wo132Rules.GuidRouteAllowed("kcd2mp_12"));
        Assert.True(Wo132Rules.GuidRouteAllowed(null));                  // a body the name lookup could not name
        Assert.True(Wo132Rules.GuidRouteAllowed("ttkc_man_5"));
    }

    // ---------------------------------------------------------------- the host's NPC combat state

    [Fact]
    public void A_combat_read_becomes_the_wire_event_with_its_target()
    {
        byte? Ghost(uint eid) => eid == 0x77 ? (byte)1 : null;
        var host = Wo132Rules.ToEvent(new NpcCombatState(5, true, 0, 1, 2, true, true, 0x7777, "tpod_bandit_2"), 100, Ghost);
        Assert.True(host.InCombat);
        Assert.True(host.State.BlockHeld);
        Assert.Equal(NpcCombatTarget.Host, host.Target);
        Assert.Equal(WireZone.Head, host.State.GuardZone);
        Assert.Equal(WireGuardStance.Right, host.State.GuardStance);
        Assert.Equal(WireZone.UpperRight, host.State.AtkZone);

        var av = Wo132Rules.ToEvent(new NpcCombatState(5, true, -1, -1, -1, false, false, 0x77, "tpod_bandit_2"), 100, Ghost);
        Assert.Equal(NpcCombatTarget.Avatar, av.Target);
        Assert.Equal(1, av.TargetGhost);
        Assert.Equal(WireZone.Undefined, av.State.GuardZone);

        var other = Wo132Rules.ToEvent(new NpcCombatState(5, true, -1, -1, -1, false, false, 0x99, "tpod_bandit_2"), 100, Ghost);
        Assert.Equal(NpcCombatTarget.Other, other.Target);
        var none = Wo132Rules.ToEvent(new NpcCombatState(5, false, -1, -1, -1, false, false, 0, "tpod_bandit_2"), 100, Ghost);
        Assert.False(none.InCombat);
        Assert.Equal(NpcCombatTarget.None, none.Target);
    }

    [Fact]
    public void The_npc_combat_event_round_trips_and_refuses_malformed_bytes()
    {
        var st = new BodyState2(0, 0, BodyState2Bits.CombatMode | BodyState2Bits.BlockHeld, WireZone.Head, WireGuardStance.Left, WireZone.Lower, 0, 0, 0);
        var e = new NpcCombatEvent(1234, st, NpcCombatTarget.Avatar, 3, "prepadeniNaCeste_bandit_9");
        var b = e.ToBytes();
        Assert.True(NpcCombatEvent.TryFromBytes(b, out var back));
        Assert.Equal(e, back);
        Assert.False(NpcCombatEvent.TryFromBytes(b.AsSpan(0, b.Length - 1), out _));   // length lies
        var bad = (byte[])b.Clone(); bad[16] = 9;                                       // unknown target
        Assert.False(NpcCombatEvent.TryFromBytes(bad, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NpcCombatEvent(1, st, NpcCombatTarget.None, 0, "").ToBytes());
    }

    [Fact]
    public void The_dll_combat_frame_parses()
    {
        var name = "tpod_bandit_2"u8.ToArray();
        var b = new byte[15 + name.Length];
        BitConverter.GetBytes(0x80D6u).CopyTo(b, 0);
        b[4] = 1; b[5] = 0; b[6] = 0xFF; b[7] = 3; b[8] = 1; b[9] = 0;
        BitConverter.GetBytes(0x1234u).CopyTo(b, 10);
        b[14] = (byte)name.Length; name.CopyTo(b, 15);
        Assert.True(NpcCombatState.TryParse(b, out var s));
        Assert.Equal(new NpcCombatState(0x80D6, true, 0, -1, 3, true, false, 0x1234, "tpod_bandit_2"), s);
        Assert.False(NpcCombatState.TryParse(b.AsSpan(0, b.Length - 1), out _));
    }

    [Fact]
    public void The_host_watches_only_live_drawn_npcs()
    {
        Assert.True(Wo132Rules.HostWatches(0x04));
        Assert.True(Wo132Rules.HostWatches(0x04 | 0x20));
        Assert.False(Wo132Rules.HostWatches(0x00));
        Assert.False(Wo132Rules.HostWatches(0x04 | Protocol.NpcStateFlagDead));
    }

    // ---------------------------------------------------------------- the engagement rules

    private static NpcCombatEvent Ev(bool combat) =>
        new(1, new BodyState2(0, 0, combat ? BodyState2Bits.CombatMode : BodyState2Bits.None, 0, 0, 0, 0, 0, 0), combat ? NpcCombatTarget.Avatar : NpcCombatTarget.None, 1, "npc");

    [Fact]
    public void Only_the_hosts_bound_guarded_copy_in_a_fight_is_engaged()
    {
        Assert.Equal(E.Engage, Wo132Rules.JudgeEngage(Ev(true), joinerActive: true, guarded: true, nativeBound: true, engagedNow: false));
        // never a free copy: not guarded, not bound, or not a joiner session
        Assert.Equal(E.Ignore, Wo132Rules.JudgeEngage(Ev(true), true, false, true, false));
        Assert.Equal(E.Ignore, Wo132Rules.JudgeEngage(Ev(true), true, true, false, false));
        Assert.Equal(E.Ignore, Wo132Rules.JudgeEngage(Ev(true), false, true, true, false));
        // the host's NPC left combat, or the copy stopped being the host's: released
        Assert.Equal(E.Release, Wo132Rules.JudgeEngage(Ev(false), true, true, true, true));
        Assert.Equal(E.Release, Wo132Rules.JudgeEngage(Ev(true), true, false, true, true));
        Assert.Equal(E.Ignore, Wo132Rules.JudgeEngage(Ev(false), true, true, true, false));
    }

    [Fact]
    public void An_engagement_with_no_host_state_goes_stale_in_three_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), Wo132Rules.EngageStale);
    }
}
