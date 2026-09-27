namespace KcdMp.Client.Tests;

/// <summary>WO-133: the shared-world quest gate and the host-only time-skip rule (docs/WO-133-findings.md).</summary>
public class Wo133Tests
{
    // ---------------------------------------------------------------- the gate

    [Fact]
    public void No_session_is_never_a_shared_world_whatever_the_toggles_say()
    {
        Assert.False(Wo133Rules.SharedWorldSession(roleKnown: false, isHost: true, localShared: true, joinerSharedEffective: true));
        Assert.False(Wo133Rules.SharedWorldSession(roleKnown: false, isHost: false, localShared: true, joinerSharedEffective: true));
    }

    [Fact]
    public void The_host_follows_its_own_toggle()
    {
        Assert.True(Wo133Rules.SharedWorldSession(true, isHost: true, localShared: true, joinerSharedEffective: false));
        Assert.False(Wo133Rules.SharedWorldSession(true, isHost: true, localShared: false, joinerSharedEffective: true));   // mp_shared_world off
    }

    [Fact]
    public void The_joiner_follows_the_hosts_mode_not_its_own_toggle()
    {
        Assert.True(Wo133Rules.SharedWorldSession(true, isHost: false, localShared: false, joinerSharedEffective: true));
        Assert.False(Wo133Rules.SharedWorldSession(true, isHost: false, localShared: true, joinerSharedEffective: false));   // host runs separate worlds
    }

    [Fact]
    public void Only_a_joiner_withholds_its_marker_and_fingerprints()
    {
        Assert.True(Wo133Rules.JoinerQuietStory(sharedWorldSession: true, isHost: false));
        Assert.False(Wo133Rules.JoinerQuietStory(sharedWorldSession: true, isHost: true));    // the host's own saves are its world
        Assert.False(Wo133Rules.JoinerQuietStory(sharedWorldSession: false, isHost: false));  // separate worlds: as before
    }

    [Fact]
    public void The_gate_line_is_one_guarded_Lua_call_with_fixed_words()
    {
        Assert.Equal("if KCD2MP_Wo133Gate then KCD2MP_Wo133Gate(true, \"host\", \"host-shared-world\") end",
                     Wo133Rules.GateLua(true, "host", "host-shared-world"));
        Assert.Equal("if KCD2MP_Wo133Gate then KCD2MP_Wo133Gate(false, \"none\", \"no-session\") end",
                     Wo133Rules.GateLua(false, "none", "no-session"));
    }

    [Theory]
    [InlineData(false, true, true, false, false, "no-session")]
    [InlineData(true, true, true, false, false, "host-shared-world")]
    [InlineData(true, true, false, false, false, "host-separate-worlds")]
    [InlineData(true, false, false, true, true, "host-announced-shared-world")]
    [InlineData(true, false, true, true, false, "host-announced-separate-worlds")]
    [InlineData(true, false, true, false, false, "local-shared-world")]
    [InlineData(true, false, false, false, false, "local-separate-worlds")]
    public void Why_names_the_deciding_input(bool roleKnown, bool isHost, bool localShared, bool hostModeKnown, bool hostShared, string want)
        => Assert.Equal(want, Wo133Rules.Why(roleKnown, isHost, localShared, hostModeKnown, hostShared));

    // ---------------------------------------------------------------- only the host moves the clock

    [Fact]
    public void The_host_of_a_shared_world_drops_every_skip_that_is_not_its_own()
    {
        bool host = Wo133Rules.HostOfSharedWorld(roleKnown: true, isHost: true, localShared: true);
        Assert.True(host);
        Assert.True(Wo133Rules.DropInboundTimeSkip(host, sourceGhostId: 2, myGhostId: 1));
        Assert.True(Wo133Rules.DropInboundTimeSkip(host, sourceGhostId: 3, myGhostId: 1));
        Assert.False(Wo133Rules.DropInboundTimeSkip(host, sourceGhostId: 1, myGhostId: 1));   // an echo of our own
    }

    [Fact]
    public void A_joiner_and_separate_worlds_apply_peer_skips_as_before()
    {
        Assert.False(Wo133Rules.HostOfSharedWorld(roleKnown: true, isHost: false, localShared: true));   // the joiner applies the host's skips
        Assert.False(Wo133Rules.HostOfSharedWorld(roleKnown: true, isHost: true, localShared: false));   // mp_shared_world off: any player's sleep counts
        Assert.False(Wo133Rules.HostOfSharedWorld(roleKnown: false, isHost: true, localShared: true));   // no session yet
        Assert.False(Wo133Rules.DropInboundTimeSkip(false, sourceGhostId: 2, myGhostId: 1));
    }
}
