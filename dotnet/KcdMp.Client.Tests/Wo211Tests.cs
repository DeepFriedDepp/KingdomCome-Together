// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-211: the link hold, the seat release, the over-full queue line, the joint crime's notice, the build id.</summary>
public class Wo211Tests
{
    private const ulong Seat = 0x0D0221C02B7B7F08UL, OtherSeat = 0x00170223B5E192CAUL, Work = 0x44ECDF3725428A2BUL;
    private static ActivityState Sit(ulong seat, ushort unstance = ActivityState.NoUnstance, ulong uobj = 0) =>
        new(ActivityState.Sitting, 0, seat, unstance, uobj, ActivityState.NoMinigame, 0, 0);
    private static ActivityState AtWork(ushort unstance, ulong obj) => new(0, 0, 0, unstance, obj, ActivityState.NoMinigame, 0, 0);

    [Fact]
    public void A_seated_gesture_in_the_same_seat_is_no_leave()
    {
        // the field's ttkc_man_10: sitting@0D02... unstance - -> 119 -> -, released every half minute by "path 15"
        Assert.False(Wo211Rules.LeftPosture(Sit(Seat), Sit(Seat, 119)));
        Assert.False(Wo211Rules.LeftPosture(Sit(Seat, 119), Sit(Seat)));
        Assert.False(Wo211Rules.LeftPosture(Sit(Seat), Sit(Seat, 484, Seat)));         // the seat named as the unstance's object too
        Assert.False(Wo211Rules.LeftPosture(AtWork(446, Work), AtWork(447, Work)));    // another step at the same workstation
        Assert.True(Wo211Rules.LeftPosture(Sit(Seat), ActivityState.None));             // stood up
        Assert.True(Wo211Rules.LeftPosture(Sit(Seat), Sit(OtherSeat)));                 // moved to another seat
        Assert.True(Wo211Rules.LeftPosture(AtWork(117, Work), ActivityState.None));     // the work ended
        Assert.False(Wo211Rules.LeftPosture(ActivityState.None, Sit(Seat)));            // nothing was held
        var (s, w) = Wo211Rules.HeldObjects(Sit(Seat, 484, Seat));
        Assert.Equal(Seat, s); Assert.Equal(Seat, w);
    }

    [Fact]
    public void The_engines_overfull_line_names_the_copy()
    {
        Assert.Equal("ttkc_man_10", Wo211Rules.OverflowEntity("[Warning] Validator: --- Mannequin Error, attempting to queue too many actions on entity 'ttkc_man_10' (queue size = 11, but only up to 10 allowed) ---"));
        Assert.Null(Wo211Rules.OverflowEntity("[Warning] Validator:     11 actions in queue:"));
        Assert.Null(Wo211Rules.OverflowEntity("attempting to queue too many actions on entity 'a b' (x)"));
        Assert.True(LogTailGameTransport.Wo144Passes("[Warning] Validator: --- Mannequin Error, attempting to queue too many actions on entity 'x' (queue size = 11"));
    }

    [Fact]
    public void A_dropped_link_holds_only_a_joiner_in_the_hosts_world()
    {
        Assert.True(Wo211Rules.HoldOnLoss(true, joinedWorld: true, joining: false, leaving: false, quitting: false));
        Assert.False(Wo211Rules.HoldOnLoss(false, true, false, false, false));   // the switch
        Assert.False(Wo211Rules.HoldOnLoss(true, false, false, false, false));   // not in the host's world
        Assert.False(Wo211Rules.HoldOnLoss(true, true, true, false, false));     // mid-join: the join's own abort
        Assert.False(Wo211Rules.HoldOnLoss(true, true, false, true, false));     // already leaving
        Assert.False(Wo211Rules.HoldOnLoss(true, true, false, false, true));     // the game is quitting
        // growing gaps, all inside the window; 3 s outside a hold
        int sum = 0;
        for (int i = 0, prev = 0; i < 12 && sum < Wo211Rules.HoldWindowS * 1000; i++) { int d = Wo211Rules.ReconnectDelayMs(i, true); Assert.True(d >= prev); prev = d; sum += d; }
        Assert.Equal(1000, Wo211Rules.ReconnectDelayMs(0, true));
        Assert.Equal(3000, Wo211Rules.ReconnectDelayMs(5, false));
        Assert.True(Wo211Rules.ResumeInPlace(3, 3));
        Assert.False(Wo211Rules.ResumeInPlace(3, 4));   // the host reloaded meanwhile: the in-world rejoin
        Assert.False(Wo211Rules.ResumeInPlace(0, 0));   // no epoch known: never "the same world"
        Assert.Contains("reconnecting", Wo211Rules.HoldText(42));
        Assert.Contains("(42 s)", Wo211Rules.HoldText(42));
        Assert.Contains("couldn't be saved (the game refused)", Wo211Rules.SnapshotLostText("the game refused"));
    }

    [Fact]
    public void A_stop_over_the_partners_crime_says_so()
    {
        var own = new HashSet<string>(StringComparer.Ordinal) { "theft" };
        Assert.Equal(new[] { "lockpick" }, Wo211Rules.PartnerKinds(new[] { "lockpick", "theft", "lockpick" }, own));
        Assert.Empty(Wo211Rules.PartnerKinds(new[] { "theft" }, own));
        Assert.Equal("Shared crime is on: the guard holds you to account for your partner's lockpicking.", Wo211Rules.JointStopText(new[] { "lockpick" }));
    }

    [Fact]
    public void Switches_ship_on_but_the_flight()
    {
        Assert.True(Wo211Rules.Default("link_hold", null));
        Assert.True(Wo211Rules.Default("ui_lua_main_only", null));
        Assert.False(Wo211Rules.Default("allow_fly", null));
        Assert.False(Wo211Rules.Default("link_hold", "link_hold"));
        Assert.Equal(0x07u, Wo211Rules.ConfigBits(_ => true));
        Assert.Equal(0x02u, Wo211Rules.ConfigBits(n => n == "overflow_reseat"));
        Assert.True(Wo211Rules.TryParseCfg("allow_fly=on", out var n, out bool on) && n == "allow_fly" && on);
        Assert.False(Wo211Rules.TryParseCfg("allow_flying=on", out _, out _));
    }

    [Fact]
    public void A_build_id_rides_the_release_string()
    {
        Assert.Equal("0.63.9+d4f2cb4-2610300412", BuildId.Wire("0.63.9", "d4f2cb4-2610300412"));
        Assert.Equal("0.63.9", BuildId.Wire("0.63.9", "dev"));
        Assert.Equal("0.63.9", BuildId.Wire("0.63.9", "bad id"));
        Assert.True(BuildId.SameVersionOtherBuild("0.63.9+a", "0.63.9+b"));
        Assert.True(BuildId.SameVersionOtherBuild("0.63.9", "0.63.9+b"));
        Assert.False(BuildId.SameVersionOtherBuild("0.63.8+a", "0.63.9+a"));
        Assert.Equal("Your partner is on a different 0.63.9 build (b1); install the same one.", BuildId.MismatchText("0.63.9+a1", "0.63.9+b1"));
        Assert.Contains("both players need the same version", BuildId.MismatchText("0.63.8", "0.63.9+b1"));
        Assert.True(BuildId.IsBuildId(BuildId.Current));
        // the launcher compares versions only: a same-build partner's "0.63.9+<id>" is the same version, not "versions don't match"
        Assert.Equal(ReleaseVersionComparison.Equal, ReleaseVersionCompare.Compare("0.63.9", "0.63.9+4744d98-2610301016"));
        Assert.Equal(ReleaseVersionComparison.LocalIsOlder, ReleaseVersionCompare.Compare("0.63.9", "0.64.0+abc"));
    }
}
