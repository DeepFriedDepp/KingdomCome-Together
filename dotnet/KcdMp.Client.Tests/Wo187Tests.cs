// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-187: the engage-on-intent rule, the opponent wish on every target change, the per-weapon timing table, the recoil-row choice,
/// and the C1 inventory (a class that drops out of the pipeline fails here).</summary>
public class Wo187Tests
{
    private static NpcCombatEvent Ev(bool combat, NpcCombatTarget t, byte ghost = 0) =>
        new(1, new BodyState2(0, 0, combat ? BodyState2Bits.CombatMode : BodyState2Bits.None, 0, 0, 0, 0, 0, 0), t, ghost, "ttac_man_9");

    // ------------------------------------------------------------------ A1

    [Fact]
    public void EngageOnIntent_TheHostsPositionWithinRangeCounts()
    {
        Assert.True(Wo187Rules.IntentInRange(true, true, 6f, 200));
        Assert.True(Wo187Rules.IntentInRange(true, true, Wo187Rules.EngageRangeM, 0));
        Assert.False(Wo187Rules.IntentInRange(true, true, 40f, 200));                       // the host's NPC is still far: the copy's own range decides
        Assert.False(Wo187Rules.IntentInRange(true, false, 6f, 200));                       // it targets someone else
        Assert.False(Wo187Rules.IntentInRange(false, true, 6f, 200));                       // mp_engage_intent off
        Assert.False(Wo187Rules.IntentInRange(true, true, 6f, Wo187Rules.StreamFreshMs + 1)); // an old sample is no position
        Assert.False(Wo187Rules.IntentInRange(true, true, -1f, 0));
    }

    [Fact]
    public void EngageOnIntent_TheVerdictEngagesTheMomentTheHostsNpcTargetsMe()
    {
        // combat=0 target=Avatar:<me> (the field: an NPC fighting an avatar reads combat 0 on the host) engages at once; a target change to the
        // host keeps an engaged copy and does not engage a fresh one
        Assert.Equal(Wo132Rules.EngageVerdict.Engage, Wo132Rules.JudgeEngage(Ev(false, NpcCombatTarget.Avatar, 1), true, true, true, false, myGhost: 1));
        Assert.Equal(Wo132Rules.EngageVerdict.Ignore, Wo132Rules.JudgeEngage(Ev(true, NpcCombatTarget.Host), true, true, true, false, myGhost: 1));
        Assert.Equal(Wo132Rules.EngageVerdict.Engage, Wo132Rules.JudgeEngage(Ev(true, NpcCombatTarget.Host), true, true, true, true, myGhost: 1));
        // A2 (mp_opp_logical + mp_anim_guard): an engaged copy whose host NPC turns to the host leaves my skirmish (its guard and its new
        // opponent stay); back to me = engaged again
        Assert.Equal(Wo132Rules.EngageVerdict.Release, Wo132Rules.JudgeEngage(Ev(true, NpcCombatTarget.Host), true, true, true, true, myGhost: 1, leaveOnHostTarget: true));
        Assert.Equal(Wo132Rules.EngageVerdict.Ignore, Wo132Rules.JudgeEngage(Ev(true, NpcCombatTarget.Host), true, true, true, false, myGhost: 1, leaveOnHostTarget: true));
        Assert.Equal(Wo132Rules.EngageVerdict.Engage, Wo132Rules.JudgeEngage(Ev(false, NpcCombatTarget.Avatar, 1), true, true, true, false, myGhost: 1, leaveOnHostTarget: true));
        Assert.Equal(Wo132Rules.EngageVerdict.Engage, Wo132Rules.JudgeEngage(Ev(true, NpcCombatTarget.Avatar, 2), true, true, true, true, myGhost: 1, leaveOnHostTarget: true));
    }

    // ------------------------------------------------------------------ A2

    [Fact]
    public void Opponent_FollowsEveryTargetChange()
    {
        const byte me = 1, host = 0;
        var toMe = Wo187Rules.OppWish(Ev(false, NpcCombatTarget.Avatar, 1), me, host);
        var toHost = Wo187Rules.OppWish(Ev(true, NpcCombatTarget.Host), me, host);
        var toOther = Wo187Rules.OppWish(Ev(false, NpcCombatTarget.Avatar, 2), me, host);
        Assert.Equal((Wo187Rules.OppLocalPlayer, (byte)0xFF, Wo187Rules.WhyTargetsMe), toMe);
        Assert.Equal((Wo187Rules.OppFigure, host, Wo187Rules.WhyTargetsHost), toHost);
        Assert.Equal((Wo187Rules.OppFigure, (byte)2, Wo187Rules.WhyTargetsOther), toOther);
        Assert.Null(Wo187Rules.OppWish(Ev(true, NpcCombatTarget.None), me, host));   // a moment between targets: the last wish stands
        Assert.Null(Wo187Rules.OppWish(Ev(true, NpcCombatTarget.Other), me, host));
        Assert.Null(Wo187Rules.OppWish(Ev(true, NpcCombatTarget.Host), me, 0xFF));   // the host's ghost not known yet
        // me -> host -> me: every change is a different wish (the bridge sends on a change)
        var seq = new[] { toMe, toHost, toMe };
        for (int i = 1; i < seq.Length; i++) Assert.NotEqual(seq[i - 1], seq[i]);
    }

    // ------------------------------------------------------------------ C2

    [Fact]
    public void GuardHold_AnyFightOfTheHostsNpc()
    {
        Assert.True(Wo187Rules.GuardHoldWants(Ev(false, NpcCombatTarget.Host)));      // fighting the host reads combat 0: still a fight
        Assert.True(Wo187Rules.GuardHoldWants(Ev(false, NpcCombatTarget.Avatar, 1)));
        Assert.True(Wo187Rules.GuardHoldWants(Ev(true, NpcCombatTarget.None)));
        Assert.False(Wo187Rules.GuardHoldWants(Ev(false, NpcCombatTarget.None)));
        // only copies near the joiner cost pipe calls; an unknown distance counts as near
        Assert.True(Wo187Rules.Near(10f) && Wo187Rules.Near(Wo187Rules.NearM) && Wo187Rules.Near(-1f));
        Assert.False(Wo187Rules.Near(Wo187Rules.NearM + 0.1f));
    }

    [Fact]
    public void DefenceRows_AreTheNpcsOwnBlockDodgeAndPerfectBlock_NotTheCounter()
    {
        var block = new ActionRowCatalog.Row("combat_action_block", "CombatBlockGen", "r_swords+l_shield", 1, -1, -1, 6);
        var pb = new ActionRowCatalog.Row("combat_action_perfect_block", "CombatBlockPerfectGen", "r_swords", 1, -1, -1, 14);
        var master = new ActionRowCatalog.Row("combat_action_perfect_block", "CombatMasterStrikeGen", "r_swords", 1, -1, -1, 55);
        var dodge = new ActionRowCatalog.Row("combat_action_dodge", "CombatDodgeGen", "r_swords", -1, -1, -1, 9);
        var attack = new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "r_swords", 1, -1, 1, 3);
        Assert.True(Wo187Rules.IsDefenceRow(block) && Wo187Rules.IsDefenceRow(pb) && Wo187Rules.IsDefenceRow(dodge));
        Assert.False(Wo187Rules.IsDefenceRow(master));   // the master strike is a swing (WO-163)
        Assert.False(Wo187Rules.IsDefenceRow(attack));
    }

    // ------------------------------------------------------------------ B3

    [Theory]
    [InlineData("rightGuard+r_swords+l_shield", Wo187Rules.Weapon.Sword)]
    [InlineData("r_shortSwords+l_noShield", Wo187Rules.Weapon.ShortSword)]
    [InlineData("l_longsword+r_longsword", Wo187Rules.Weapon.Longsword)]
    [InlineData("l_halberd+r_halberd", Wo187Rules.Weapon.Polearm)]
    [InlineData("r_bluntWeapon+l_noShield", Wo187Rules.Weapon.Blunt)]
    [InlineData("r_mace+l_shield", Wo187Rules.Weapon.Blunt)]
    [InlineData("r_noweapon+l_noweapon", Wo187Rules.Weapon.Unarmed)]
    [InlineData("r_spear", Wo187Rules.Weapon.Other)]
    [InlineData("rightGuard+eZ2", Wo187Rules.Weapon.Unknown)]
    public void Timing_EachWeaponClassIsItsOwn(string tags, Wo187Rules.Weapon want) => Assert.Equal(want, Wo187Rules.WeaponOf(tags));

    [Fact]
    public void Timing_ContactIsTheRowsHit_StartPlusHitWhenCharged()
    {
        // a one-handed sword NPC row (start 0.33, hit 0.63): the blow lands 0.63 s after the row starts -- the DLL gets start 330 + 300
        var sword = new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "r_swords+l_shield+aZ1+slash", 1, -1, 1, 3, 0.33f, 0.63f);
        Assert.Equal(630, Wo187Rules.ContactMs(sword));
        Assert.Equal(300, Wo187Rules.StrikeHitMs(sword));
        // a mace row (start 0.36, hit 0.88)
        var mace = new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "r_bluntWeapon+l_noShield+slash", 1, -1, 1, 3, 0.36f, 0.88f);
        Assert.Equal(880, Wo187Rules.ContactMs(mace));
        Assert.Equal(520, Wo187Rules.StrikeHitMs(mace));
        // the harness's charged longsword stab (start 0.533, hit 0.7): start + hit, as WO-185 measured
        var charged = new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "l_longsword+r_longsword+stab+chargedAttack", 5, -1, 0, 3, 0.533f, 0.7f, Charged: true);
        Assert.Equal(1233, Wo187Rules.ContactMs(charged));
        Assert.Equal(700, Wo187Rules.StrikeHitMs(charged));
        // the tag alone marks a charged row too (an older catalog without the attribute)
        Assert.True(Wo187Rules.IsCharged(charged with { Charged = false }));
        // hit before start: the floor keeps a window
        Assert.Equal(50, Wo187Rules.StrikeHitMs(sword with { TimeToStart = 0.7f, TimeToHit = 0.6f }));
        // no timings: none
        Assert.Equal(-1, Wo187Rules.StrikeHitMs(sword with { TimeToStart = 0f, TimeToHit = 0f }));
        Assert.Equal(-1, Wo187Rules.ContactMs(sword with { TimeToHit = 0f }));
        // the window the DLL builds lands contact on the row's hit (native strike_window: begin = start, contact = start + hit)
        var f = Wo166Rules.StrikeFor(mace);
        Assert.NotNull(f);
        Assert.Equal(880, f!.Value.StartMs + f.Value.HitMs);
        Assert.Equal((byte)Wo187Rules.Weapon.Blunt, f.Value.Weapon);
    }

    [Fact]
    public void Timing_TableByWeapon_UnknownGetsTheDefault()
    {
        // the sword (WO-185), the mace and bare hands (WO-187's vanilla traces) are measured; the rest keep the fallback
        foreach (var w in new[] { Wo187Rules.Weapon.Sword, Wo187Rules.Weapon.Blunt, Wo187Rules.Weapon.Unarmed })
            Assert.True(Wo187Rules.Timing(w).PbMeasured);
        foreach (var w in new[] { Wo187Rules.Weapon.ShortSword, Wo187Rules.Weapon.Longsword, Wo187Rules.Weapon.Polearm, Wo187Rules.Weapon.Unknown })
            Assert.False(Wo187Rules.Timing(w).PbMeasured);
        Assert.Contains("default", Wo187Rules.Timing(Wo187Rules.Weapon.Unknown).Contact);
        Assert.DoesNotContain("default", Wo187Rules.Timing(Wo187Rules.Weapon.Blunt).Contact);
        Assert.Equal("blunt", Wo187Rules.WeaponName(Wo187Rules.Weapon.Blunt));
    }

    [Fact]
    public void Recoil_RowsOnlyForTheWeaponsTheTablesHave()
    {
        Assert.True(Wo187Rules.HasRecoilRows(Wo187Rules.Weapon.Sword) && Wo187Rules.HasRecoilRows(Wo187Rules.Weapon.ShortSword)
                    && Wo187Rules.HasRecoilRows(Wo187Rules.Weapon.Longsword) && Wo187Rules.HasRecoilRows(Wo187Rules.Weapon.Polearm));
        Assert.False(Wo187Rules.HasRecoilRows(Wo187Rules.Weapon.Blunt));
        Assert.False(Wo187Rules.HasRecoilRows(Wo187Rules.Weapon.Unarmed));
        // the catalog's own choice agrees: a mace swing finds no failed-attack row, a sword-and-shield swing does
        var g1 = Guid.NewGuid(); var g2 = Guid.NewGuid();
        var cat = ActionRowCatalog.FromRows(new[]
        {
            (g1, new ActionRowCatalog.Row("combat_action_failed_attack", "CombatAttackFailedPB", "r_swords+l_shield", -1, -1, -1, ActionRowCatalog.ActionFailedAttackOnPerfectBlock)),
            (g2, new ActionRowCatalog.Row("combat_action_failed_attack", "CombatAttackFailed", "r_swords+l_shield", -1, -1, -1, ActionRowCatalog.ActionFailedAttackOnBlock)),
        });
        var mace = new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "r_bluntWeapon+l_noShield+slash", 1, -1, 1, 3, 0.36f, 0.88f);
        var sword = new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "r_swords+l_shield+slash", 1, -1, 1, 3, 0.33f, 0.63f);
        Assert.Null(cat.FailedAttackRow(mace, perfect: true));
        Assert.Null(cat.FailedAttackRow(mace, perfect: false));
        Assert.Equal("CombatAttackFailedPB", cat.FailedAttackRow(sword, perfect: true)!.Value.Fragment);
        Assert.Equal("CombatAttackFailed", cat.FailedAttackRow(sword, perfect: false)!.Value.Fragment);
    }

    // ------------------------------------------------------------------ C1

    [Fact]
    public void Inventory_EveryClassReachesTheWatcherBothWays()
    {
        Assert.Equal(0, Wo187Rules.InventoryGaps());
        string[] classes =
        {
            "slash / stab / blunt", "combos, special and master strikes", "unarmed and fist fights", "kicks and stop moves",
            "block, parry, perfect block, dodge", "hit reactions, knockdowns, the attacker's recoil", "clinch and sync attacks", "the guard stance between moves",
        };
        foreach (var c in classes)
        {
            Assert.Contains(Wo187Rules.Inventory, r => r.Class == c && r.Side == "P");
            Assert.Contains(Wo187Rules.Inventory, r => r.Class == c && r.Side == "N");
        }
        Assert.Equal(classes.Length * 2, Wo187Rules.Inventory.Length);
    }

    // ------------------------------------------------------------------ switches and lines

    [Fact]
    public void Cfg_NamesAndValues()
    {
        Assert.True(Wo187Rules.TryParseCfg("anim_guard=off", out var n, out var on) && n == "anim_guard" && !on);
        Assert.True(Wo187Rules.TryParseCfg("opp_logical=on", out n, out on) && on);
        Assert.False(Wo187Rules.TryParseCfg("anim_guard=maybe", out _, out _));
        Assert.False(Wo187Rules.TryParseCfg("nope=on", out _, out _));
        Assert.False(Wo187Rules.DefaultAnimScopeLog);   // a trace, off
        Assert.True(Wo187Rules.DefaultOppLogical && Wo187Rules.DefaultAnimGuard && Wo187Rules.DefaultAnimIdle && Wo187Rules.DefaultAnimDefence
                    && Wo187Rules.DefaultAnimCombo && Wo187Rules.DefaultEngageIntent && Wo187Rules.DefaultOppPin && Wo187Rules.DefaultFightPos
                    && Wo187Rules.DefaultChaseFollow);
        Assert.True(Wo187Rules.TryParseCfg("chase_follow=off", out n, out on) && n == "chase_follow" && !on);
    }

    [Fact]
    public void FightPos_LinesNameBothSidesOnTheSharedClock()
    {
        string h = Wo187Rules.HostPosLine("ttac_man_9", "00:40:12.000", 10f, 0f, "avatar:1", 13f, 4f);
        Assert.Contains("role=host npc=ttac_man_9 shared=00:40:12.000", h);
        Assert.Contains("dist_m=5.00", h);
        string j = Wo187Rules.JoinerPosLine("ttac_man_9", "00:40:12.010", 4f, 0f, (10f, 0f, 120, 20, 123456u), 13f, 4f, "avatar:1");
        Assert.Contains("role=joiner", j);
        Assert.Contains("lag_m=6.00", j);        // the copy is 6 m behind the host's NPC
        Assert.Contains("pps=20", j);
        Assert.Contains("dist_stream_m=5.00", j);
        Assert.Contains("stream_at=none", Wo187Rules.JoinerPosLine("x", "?", 0, 0, null, 0, 0, "host"));
        Assert.Equal("?", Wo187Rules.SharedClock(DateTime.UtcNow, null));
        Assert.Equal("00:00:01.500", Wo187Rules.SharedClock(new DateTime(2026, 10, 15, 0, 0, 1, DateTimeKind.Utc), 500));
    }

    // ------------------------------------------------------------------ C1: a hit reaction that did not play; the player's own reaction

    private const long Man = 1578932418, Dog = 20967892;   // combat_action_hit.xml actor_class_hash: a man's rows, the 5 dog rows

    private static ActionRowCatalog.Row Hit(string frag, string tags, int zone, long actor = Man) =>
        new("combat_action_hit", frag, tags, zone, -1, -1, 7, 0f, 0f, actor);

    [Fact]
    public void HitFallback_TheBodysOwnWeaponForTheZone_ThenAnyFullBodyReaction_NeverADeathRow()
    {
        Guid failed = Guid.NewGuid(), swordZ2 = Guid.NewGuid(), swordZ1 = Guid.NewGuid(), torsoZ2 = Guid.NewGuid(), maceZ2 = Guid.NewGuid(),
             deathZ2 = Guid.NewGuid(), dogZ2 = Guid.NewGuid();
        var dogRow = Hit("CombatHit", "oppKick+dZ2+hitTypeNormal+oppMale+oppFemale", 2, Dog);
        var cat = ActionRowCatalog.FromRows(new[]
        {
            (failed, dogRow),
            (maceZ2, Hit("CombatHitGen", "l_noShield+r_bluntWeapon+sZ2+leftGuard+eZ2+dZ2+hitTypeNormal+oppMale", 2)),
            (swordZ2, Hit("CombatHitGen", "l_shield+r_swords+sZ1+rightGuard+eZ1+dZ2+hitTypeNormal+oppMale", 2)),
            (swordZ1, Hit("CombatHitGen", "l_shield+r_swords+sZ1+rightGuard+eZ1+dZ1+hitTypeNormal+oppMale", 1)),
            (torsoZ2, Hit("CombatHitTorso", "dZ2+hitTypeLow", 2)),
            (deathZ2, Hit("CombatDeathGen", "l_shield+r_swords+dZ2+hitTypeNormal+oppMale", 2)),
            (dogZ2, Hit("CombatHit", "dZ2+hitTypeNormal+oppMale+oppFemale", 2, Dog)),
        });
        // the live case: the dog's row on a man's copy holding sword and shield -> the sword reaction of the zone (a man's row)
        var swordSwing = new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "r_swords+l_shield+slash", 2, -1, 1, 3, 0.3f, 0.7f, Man);
        Assert.Equal(swordZ2, cat.HitFallbackRow(failed, dogRow, swordSwing)!.Value.Guid);
        // a man's body whose weapon is not known: a full-body reaction of the zone (any weapon's row plays with the body's own weapon),
        // never the torso row (never shown as a reaction), the death row, or a dog's row
        var unknownMan = cat.HitFallbackRow(failed, dogRow, new ActionRowCatalog.Row("combat_action_attack", "x", "", 2, -1, 1, 3, 0f, 0f, Man))!.Value;
        Assert.Equal("CombatHitGen", unknownMan.Row.Fragment);
        Assert.Equal(2, unknownMan.Row.Zone);
        // the failed row is never its own stand-in
        Assert.True(cat.TryGet(maceZ2, out var maceRow));
        Assert.NotEqual(maceZ2, cat.HitFallbackRow(maceZ2, maceRow, new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "r_bluntWeapon+l_noShield+slash", 2, -1, 1, 3, 0.36f, 0.88f, Man))!.Value.Guid);
    }

    [Fact]
    public void HitFallback_ADogRowOnAMansCopy_GetsAMansRow()
    {
        Guid dogRow = Guid.NewGuid(), torso5 = Guid.NewGuid(), dog5 = Guid.NewGuid(), fist5 = Guid.NewGuid();
        var dog = Hit("CombatHit", "oppKick+dZ5+hitTypeNormal+oppMale+oppFemale", 5, Dog);
        var cat = ActionRowCatalog.FromRows(new[] { (dogRow, dog), (dog5, Hit("CombatHit", "dZ5+hitTypeNormal", 5, Dog)), (torso5, Hit("CombatHitTorso", "dZ5+hitTypeLow", 5)),
                                                    (fist5, Hit("CombatHitGen", "r_noweapon+l_noShield+rightGuard+dZ5+hitTypeNormal+oppMale", 5)) });
        var manSwing = new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "r_noweapon+l_noShield+hook", 1, -1, 6, 3, 0.3f, 0.6f, Man);
        Assert.Equal(fist5, cat.HitFallbackRow(dogRow, dog, manSwing)!.Value.Guid);
    }

    [Fact]
    public void HitFallbackScore_OwnWeaponThenFullBodyThenZoneThenSideThenType()
    {
        var sword = new[] { "l_shield", "r_swords" };
        string failed = "l_noShield+r_bluntWeapon+rightGuard+dZ1+hitTypeNormal";
        Assert.Equal(16 + 8 + 4 + 2 + 1, Wo187Rules.HitFallbackScore(failed, 1, sword, "CombatHitGen", "l_shield+r_swords+rightGuard+dZ1+hitTypeNormal", 1));
        Assert.Equal(8 + 4, Wo187Rules.HitFallbackScore(failed, 1, sword, "CombatHitGen", "l_noShield+r_bluntWeapon+dZ1", 1));   // another weapon: still plays
        Assert.Equal(4 + 1, Wo187Rules.HitFallbackScore(failed, 1, sword, "CombatHitTorso", "dZ1+hitTypeNormal", 1));
        Assert.True(Wo187Rules.HitFallbackScore(failed, 1, sword, "CombatHitGen", "l_shield+r_swords+leftGuard+dZ2", 2)
                    > Wo187Rules.HitFallbackScore(failed, 1, sword, "CombatHitGen", "l_noShield+r_bluntWeapon+rightGuard+dZ1+hitTypeNormal", 1),
                    "the body's own weapon outweighs everything else");
    }

    [Fact]
    public void HitFallback_BareHandsGuess_TheUnarmedReactionOfTheZone()
    {
        // the live figure case: a bare-handed figure, the partner's torso reaction not shown; no weapon row of his played here yet
        Guid failed = Guid.NewGuid(), fist = Guid.NewGuid(), longsword = Guid.NewGuid(), torso0 = Guid.NewGuid();
        var torso = Hit("CombatHitTorso", "dZ2+hitTypeLow", 2);
        var cat = ActionRowCatalog.FromRows(new[] { (failed, torso), (torso0, Hit("CombatHitTorso", "dZ0+hitTypeLow", 0)),
            (longsword, Hit("CombatHitGen", "l_longsword+r_longsword+rightGuard+dZ2+hitTypeNormal+oppMale", 2)),
            (fist, Hit("CombatHitGen", "r_noweapon+rightGuard+dZ2+hitTypeNormal+l_noShield+oppMale", 2)) });
        var guess = new ActionRowCatalog.Row("guess", "", Wo187Rules.BareHandsTags, -1, -1, -1);
        Assert.Equal(fist, cat.HitFallbackRow(failed, torso, guess)!.Value.Guid);
        Assert.Equal(longsword, cat.HitFallbackRow(failed, torso, null)!.Value.Guid);   // the second try: any full-body reaction of the zone
    }

    [Fact]
    public void AvatarHit_OnAStandingFigureOnly_AndTheKindIsNew()
    {
        Assert.True(Wo187Rules.AvatarHitPlayable(figureKnown: true, figureDown: false));
        Assert.False(Wo187Rules.AvatarHitPlayable(figureKnown: true, figureDown: true));
        Assert.False(Wo187Rules.AvatarHitPlayable(figureKnown: false, figureDown: false));
        Assert.Equal(18, (int)ActionKind.AvatarHit);
        Assert.True(Wo187Rules.DefaultAnimAvatarHit && Wo187Rules.DefaultAnimHitFallback);
        Assert.True(Wo187Rules.TryParseCfg("anim_avatar_hit=off", out var n, out var on) && n == "anim_avatar_hit" && !on);
        Assert.True(Wo187Rules.TryParseCfg("anim_hit_fallback=on", out n, out on) && n == "anim_hit_fallback" && on);
    }
}
