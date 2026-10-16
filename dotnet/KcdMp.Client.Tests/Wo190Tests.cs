// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-190: the agent half's rules -- the switches, the trace mode, the stand-in's weapon, the torch guard, the engage hold, the clinch.</summary>
public class Wo190Tests
{
    [Fact]
    public void Every_mechanism_ships_on_and_the_trace_off()
    {
        Assert.False(Wo190Rules.DefaultAnimTrace);
        Assert.Equal(Wo190Rules.ScopeLogAuto, Wo190Rules.DefaultScopeLog);
        Assert.True(Wo190Rules.DefaultIdleWatch && Wo190Rules.DefaultStandinWeapon && Wo190Rules.DefaultCopyWeaponHold && Wo190Rules.DefaultRequeueGate
                    && Wo190Rules.DefaultGaitHold && Wo190Rules.DefaultSwingTakeover && Wo190Rules.DefaultEngageHold);
    }

    [Theory]
    [InlineData("anim_trace=on", "anim_trace", true)]
    [InlineData("engage_hold=off", "engage_hold", false)]
    [InlineData("copy_swing_takeover=on", "copy_swing_takeover", true)]
    public void Cfg_parses(string arg, string name, bool on)
    {
        Assert.True(Wo190Rules.TryParseCfg(arg, out var n, out var v));
        Assert.Equal(name, n);
        Assert.Equal(on, v);
    }

    [Theory]
    [InlineData("anim_trace=auto")]
    [InlineData("nope=on")]
    [InlineData("anim_trace")]
    [InlineData(null)]
    public void Cfg_refuses(string? arg) => Assert.False(Wo190Rules.TryParseCfg(arg, out _, out _));

    [Fact]
    public void Scope_log_words_and_the_trace_mode()
    {
        Assert.Equal(Wo190Rules.ScopeLogAuto, Wo190Rules.ScopeLogMode("auto"));
        Assert.Equal(Wo190Rules.ScopeLogOn, Wo190Rules.ScopeLogMode("on"));
        Assert.Equal(Wo190Rules.ScopeLogOff, Wo190Rules.ScopeLogMode("off"));
        Assert.Null(Wo190Rules.ScopeLogMode("maybe"));
        Assert.Equal((byte)2, Wo190Rules.TraceMode(true, Wo190Rules.ScopeLogOff));    // mp_anim_trace on: every body
        Assert.Equal((byte)1, Wo190Rules.TraceMode(false, Wo190Rules.ScopeLogAuto));  // auto: the bodies in a fight
        Assert.Equal((byte)1, Wo190Rules.TraceMode(false, Wo190Rules.ScopeLogOn));
        Assert.Equal((byte)0, Wo190Rules.TraceMode(false, Wo190Rules.ScopeLogOff));
    }

    [Fact]
    public void Standin_never_bare_hands_on_an_armed_body()
    {
        Assert.False(Wo190Rules.StandinAllowed(Wo190Rules.HandsArmed, "r_noweapon+l_noShield+hitTypeNormal"));
        Assert.True(Wo190Rules.StandinAllowed(Wo190Rules.HandsArmed, "r_swords+l_shield+leftGuard"));
        Assert.False(Wo190Rules.StandinAllowed(Wo190Rules.HandsBare, "r_longsword+l_longsword"));
        Assert.True(Wo190Rules.StandinAllowed(Wo190Rules.HandsBare, "r_noweapon+l_noShield"));
        Assert.True(Wo190Rules.StandinAllowed(Wo190Rules.HandsUnknown, "r_noweapon+l_noShield"));
        Assert.True(Wo190Rules.StandinAllowed(Wo190Rules.HandsUnknown, "r_longsword+l_longsword"));
    }

    [Fact]
    public void Standin_body_comes_from_the_hands_when_read()
    {
        var b = Wo190Rules.BodyFromHands(Wo190Rules.HandsArmed, "r_swords+l_shield");
        Assert.NotNull(b);
        Assert.Equal("r_swords+l_shield", b!.Value.Tags);
        Assert.Null(Wo190Rules.BodyFromHands(Wo190Rules.HandsUnknown, "r_swords+l_shield"));
        Assert.Null(Wo190Rules.BodyFromHands(Wo190Rules.HandsArmed, ""));
        Assert.Equal(1234L, Wo190Rules.BodyFromHands(Wo190Rules.HandsArmed, "r_swords+l_shield", 1234)!.Value.ActorClass);   // the body's own class kept
    }

    [Fact]
    public void Standin_scores_the_held_weapon_first()
    {
        // the held weapon's row beats a bare-handed one, which the armed filter drops anyway
        var held = Wo190Rules.BodyFromHands(Wo190Rules.HandsArmed, "r_swords+l_shield")!.Value;
        var weapon = Wo165Rules.WeaponTags(held.Tags);
        int sword = Wo187Rules.HitFallbackScore("r_longsword+l_longsword+hitTypeNormal", 1, weapon, "CombatHitGen", "r_swords+l_shield+hitTypeNormal", 1);
        int bare = Wo187Rules.HitFallbackScore("r_longsword+l_longsword+hitTypeNormal", 1, weapon, "CombatHitGen", "r_noweapon+l_noShield+hitTypeNormal", 1);
        Assert.True(sword > bare);
    }

    [Fact]
    public void Torch_is_never_the_weapon_to_hold()
    {
        Assert.Equal("?", Wo190Rules.WeaponToHold("e95bb3ae-38ce-41fd-948a-e471673b47e5"));   // the NPCs' torch tool
        Assert.Equal("?", Wo190Rules.WeaponToHold("4CEA28A0-0814-405A-BF24-4FD711F7EB63"));   // a torch weapon, any case
        Assert.Equal("?", Wo190Rules.WeaponToHold("bdf14d9c-7264-434c-96af-748ff2779c1b"));   // the lamp
        Assert.Equal("?", Wo190Rules.WeaponToHold(null));
        Assert.Equal("65a211bd-2c7e-40a8-984f-66c8730444e4", Wo190Rules.WeaponToHold("65a211bd-2c7e-40a8-984f-66c8730444e4"));   // a mace stays
        Assert.Equal("-", Wo190Rules.WeaponToHold("-"));
    }

    [Fact]
    public void Engage_hold_lasts_three_seconds_after_the_turn()
    {
        Assert.True(Wo190Rules.HoldEngaged(true, true, NpcCombatTarget.Host, 1000, 1000));
        Assert.True(Wo190Rules.HoldEngaged(true, true, NpcCombatTarget.Host, 1000, 3999));
        Assert.False(Wo190Rules.HoldEngaged(true, true, NpcCombatTarget.Host, 1000, 4000));      // 3 s: released as before
        Assert.False(Wo190Rules.HoldEngaged(false, true, NpcCombatTarget.Host, 1000, 1500));     // off
        Assert.False(Wo190Rules.HoldEngaged(true, false, NpcCombatTarget.Host, 1000, 1500));     // not engaged: nothing to hold
        Assert.False(Wo190Rules.HoldEngaged(true, true, NpcCombatTarget.Avatar, 1000, 1500));    // it fights me (or another): no turn
    }

    [Fact]
    public void Engage_hold_replays_the_field_flap()
    {
        // the 0.60.7 field: ttac_man_10 engaged with the joiner; its host NPC turned to the host at 23:58:44 and back to the joiner at 23:58:45.
        // Before: released at :44 (paused, three blows with no animation), engaged again at :45. Now: held through the second.
        long turned = 44_000;
        bool releasedAt44 = !Wo190Rules.HoldEngaged(true, true, NpcCombatTarget.Host, turned, 44_000);
        bool releasedAt445 = !Wo190Rules.HoldEngaged(true, true, NpcCombatTarget.Host, turned, 44_500);
        Assert.False(releasedAt44);
        Assert.False(releasedAt445);
        // with the switch off the old behaviour comes back
        Assert.False(Wo190Rules.HoldEngaged(false, true, NpcCombatTarget.Host, turned, 44_000));
    }

    [Fact]
    public void Clinch_refusal_is_read_soon_after_the_row()
    {
        Assert.InRange(Wo190Rules.ClinchLookMs, 100, 600);                      // the plain attack still lands near the row's own start
        Assert.True(Wo190Rules.ClinchWithinMs > (uint)Wo190Rules.ClinchLookMs);   // the look sees a refusal made since the row
    }
}
