// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-214: the agent's pure rules and the switch word, each check with the case (or the twin) that must not pass.</summary>
public class Wo214Tests
{
    [Fact]
    public void Every_switch_ships_on_and_the_twin_names_turn_one_off()
    {
        Assert.All(W214Switch.Names, n => Assert.False(W214Switch.TwinOff(null, n)));
        Assert.True(W214Switch.TwinOff("one_bind", "one_bind"));
        Assert.False(W214Switch.TwinOff("one_bind", "draw_event"));
        Assert.All(W214Switch.Names, n => Assert.True(W214Switch.TwinOff("all", n)));
        Assert.False(W214Switch.TwinOff("npc_bands", "one_bind"));   // WO-213's names are not WO-214's
    }

    [Fact]
    public void The_native_word_has_the_dlls_bit_order()
    {
        // wo214_rules.h: send_turn 1, one_bind 2, owner_base 4, combat_end 8, reaction_rows 16
        Assert.Equal(0x1Fu, W214Switch.NativeBits(_ => true));
        Assert.Equal(0x02u, W214Switch.NativeBits(n => n == "one_bind"));
        Assert.Equal(0x10u, W214Switch.NativeBits(n => n == "reaction_rows"));
        Assert.Equal(0x1Du, W214Switch.NativeBits(n => n != "one_bind"));
        Assert.Equal(0u, W214Switch.NativeBits(n => n == "draw_event"));   // the agent's own switches never reach the native word
    }

    [Fact]
    public void A_silent_body_in_the_hosts_range_is_released_after_12_s_not_3()
    {
        Assert.Equal(12_000, Wo214Rules.SilenceReleaseMs(true, 20f));
        Assert.Equal(12_000, Wo214Rules.SilenceReleaseMs(true, 140f));
        Assert.Equal(Wo147Rules.SilenceReleaseMs, Wo214Rules.SilenceReleaseMs(true, 160f));   // it left the host's range: the old 3 s
        Assert.Equal(Wo147Rules.SilenceReleaseMs, Wo214Rules.SilenceReleaseMs(true, -1f));    // not known: the old 3 s
        Assert.Equal(Wo147Rules.SilenceReleaseMs, Wo214Rules.SilenceReleaseMs(false, 20f));   // the twin: 0.64.0's 3 s everywhere
        // the field's npc-state gaps (1.9-4.4 s) and one lost 2 s heartbeat are inside the new hold, outside the old
        Assert.True(Wo147Rules.SilenceDue(10_000, 5_600, 5_600, false, Wo147Rules.SilenceReleaseMs));
        Assert.False(Wo147Rules.SilenceDue(10_000, 5_600, 5_600, false, Wo214Rules.SilenceReleaseMs(true, 20f)));
    }

    [Fact]
    public void A_death_row_is_known_by_its_fragment_or_its_tag()
    {
        Assert.True(Wo214Rules.IsDeathRow("CombatDeathGen, l_halberd+r_halberd+sZ2+death+leftGuard+dZ0+oppMale"));
        Assert.True(Wo214Rules.IsDeathRow("CombatDeathGen, sZ1+death+rightGuard+dZ1+r_bluntWeapon+l_noShield+oppMale"));
        Assert.True(Wo214Rules.IsDeathRow("CombatHitHeavy, r_sword+death+eZ1"));
        Assert.False(Wo214Rules.IsDeathRow("CombatHitGen, r_noweapon+sZ1+rightGuard+eZ2+dZ1+hitTypeNormal+l_noShield+oppMale"));
        Assert.False(Wo214Rules.IsDeathRow("CombatAttackGen, l_halberd+r_halberd+leftGuard+eZ1+aZ1+slash"));
        Assert.False(Wo214Rules.IsDeathRow(null));
    }

    [Fact]
    public void After_the_hosts_death_a_living_physics_body_is_upright_whatever_its_soul_reads()
    {
        // the field: 484 ms after the death the soul read alive (dead=0) on a living body -- 0.64.0's rule said "not needed"
        Assert.False(Wo195Rules.Upright(0, 1));
        Assert.True(Wo214Rules.UprightDead(1));
        Assert.False(Wo214Rules.UprightDead(0));   // a ragdoll lies already
        Assert.True(Wo195Rules.Upright(1, 1) && Wo214Rules.UprightDead(1));   // where both agree
    }

    [Fact]
    public void The_walking_verdict_is_plain_words()
    {
        Assert.Equal("walking NPCs within 40 m reached you 19 times a second (the slowest tenth 15; 12 body-windows)", Wo214Rules.WalkingVerdict(12, 19.2, 15.4));
        Assert.Equal("no walking NPC within 40 m was measured", Wo214Rules.WalkingVerdict(0, 0, 0));
    }

    [Fact]
    public void A_copy_still_living_after_the_engines_finish_is_laid_down_too()
    {
        // the bench: a copy killed while held in its fight was finished the engine's way, stood on, and read still-upright
        Assert.True(Wo214Rules.RagdollAfterFinish(deathGround: true, livingAfter: 1));
        Assert.False(Wo214Rules.RagdollAfterFinish(deathGround: true, livingAfter: 0));    // the finish laid it down: nothing more
        Assert.False(Wo214Rules.RagdollAfterFinish(deathGround: true, livingAfter: 255));  // unread: not guessed
        Assert.False(Wo214Rules.RagdollAfterFinish(deathGround: false, livingAfter: 1));   // the twin: the finish alone (today's)
    }

    [Fact]
    public void The_hand_text_round_trips_and_refuses_anything_else()
    {
        var sword = Guid.Parse("efa237c7-3905-4813-b9c3-a32b449c17ad");
        Assert.Equal("v1|1|efa237c7-3905-4813-b9c3-a32b449c17ad", Wo214Rules.HandText(true, sword));
        Assert.Equal("v1|0|-", Wo214Rules.HandText(false, null));
        Assert.True(Wo214Rules.TryParseHand(Wo214Rules.HandText(true, sword), out bool d, out Guid? c) && d && c == sword);
        Assert.True(Wo214Rules.TryParseHand("v1|0|-", out d, out c) && !d && c is null);
        Assert.False(Wo214Rules.TryParseHand("v2|1|-", out _, out _));        // another version
        Assert.False(Wo214Rules.TryParseHand("v1|2|-", out _, out _));        // a bad drawn field
        Assert.False(Wo214Rules.TryParseHand("v1|1|not-a-class", out _, out _));
        Assert.False(Wo214Rules.TryParseHand("v1|1", out _, out _));
        Assert.False(Wo214Rules.TryParseHand(null, out _, out _));
        // the mod's own event
        Assert.True(Wo214Rules.TryParseOwnHand("1 efa237c7-3905-4813-b9c3-a32b449c17ad", out d, out c) && d && c == sword);
        Assert.True(Wo214Rules.TryParseOwnHand("0 -", out d, out c) && !d && c is null);
        Assert.False(Wo214Rules.TryParseOwnHand("1", out _, out _));
        Assert.False(Wo214Rules.TryParseOwnHand("x -", out _, out _));
        Assert.Equal(12, Protocol.W203FigureHand);   // APPEND-ONLY: after WO-206's epoch (11)
        Assert.Equal("figure-hand", Protocol.W203KindName(Protocol.W203FigureHand));
    }

    [Fact]
    public void The_figure_keeps_the_weapon_its_partner_holds_drawn_whatever_the_outfit_packet_says()
    {
        var sword = Guid.Parse("c164f346-0463-4116-b790-094b11274e5e");
        var shirt = Guid.NewGuid();
        // the field: after an inventory screen the packet listed one class of thirteen; the weapon in his hand is kept on the figure
        Assert.Contains(sword, Wo214Rules.KeepHeld([shirt], on: true, drawn: true, held: sword));
        // sheathed, or an empty hand, or the twin: the packet alone decides (today's path)
        Assert.DoesNotContain(sword, Wo214Rules.KeepHeld([shirt], on: true, drawn: false, held: sword));
        Assert.Single(Wo214Rules.KeepHeld([shirt], on: true, drawn: true, held: null));
        Assert.DoesNotContain(sword, Wo214Rules.KeepHeld([shirt], on: false, drawn: true, held: sword));
        // the target itself is never changed in place
        var target = new HashSet<Guid> { shirt };
        _ = Wo214Rules.KeepHeld(target, true, true, sword);
        Assert.Single(target);
    }
}
