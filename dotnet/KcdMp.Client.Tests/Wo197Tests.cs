// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-197: the switches, the recoil kind, the paired move's victim row (the game's pairing key and the mirrored tags), the clinch
/// payload and the knockout bit's edges.</summary>
public class Wo197Tests
{
    [Fact]
    public void Switches_EveryNameParses_AndShipsOn()
    {
        foreach (var n in Wo197Rules.SwitchNames)
        {
            Assert.True(Wo197Rules.TryParseCfg(n + "=off", out var name, out bool on));
            Assert.Equal(n, name);
            Assert.False(on);
        }
        Assert.False(Wo197Rules.TryParseCfg("anim_nothing=on", out _, out _));
        Assert.False(Wo197Rules.TryParseCfg("anim_recoil=maybe", out _, out _));
        Assert.True(Wo197Rules.DefaultRecoil && Wo197Rules.DefaultPaired && Wo197Rules.DefaultClinch && Wo197Rules.DefaultKnockout && Wo197Rules.DefaultGuardEngine);
        Assert.Contains("anim_seats", Wo197Rules.SwitchNames);
        Assert.Contains("anim_routines", Wo197Rules.SwitchNames);
        Assert.DoesNotContain("anim_doors", Wo197Rules.SwitchNames);   // no fix, no switch
    }

    [Fact]
    public void Recoil_KindReadOrBlock()
    {
        Assert.True(Wo197Rules.RecoilIsPerfect(0x03));
        Assert.False(Wo197Rules.RecoilIsPerfect(0x01));   // a perfect bit the sender did not read: a plain block's recoil
        Assert.False(Wo197Rules.RecoilIsPerfect(0x02));
        Assert.Equal("pb", Wo197Rules.RecoilKindName(0x03));
        Assert.Equal("block", Wo197Rules.RecoilKindName(0x02));
        Assert.Equal("unknown", Wo197Rules.RecoilKindName(0x00));
        Assert.Equal(19, (int)ActionKind.AvatarRecoil);
        Assert.Equal(20, (int)ActionKind.AvatarClinch);
    }

    [Fact]
    public void Mirror_TheAttackersTagsAsTheVictimsRowNamesThem()
    {
        var m = Wo197Rules.MirrorTags("l_halberd+r_halberd+c07+ol_longsword+or_longsword+sZ1+rightGuard+eZ2+slash+attack_heavy+oppRightGuard+oppMale", null);
        Assert.Contains("ol_halberd", m);
        Assert.Contains("or_halberd", m);
        Assert.Contains("l_longsword", m);
        Assert.Contains("r_longsword", m);
        Assert.Contains("oppSZ1", m);
        Assert.Contains("oppSlash", m);
        Assert.Contains("rightGuard", m);
        Assert.Contains("c07", m);
        // the victim's real weapon wins over the attacker row's guess
        var m2 = Wo197Rules.MirrorTags("l_halberd+r_halberd+ol_longsword+or_longsword+sZ1", new[] { "r_swords", "l_shield" });
        Assert.Contains("r_swords", m2);
        Assert.DoesNotContain("r_longsword", m2);
    }

    private static readonly Guid A1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid H1 = Guid.Parse("00000000-0000-0000-0000-0000000000b1"), H2 = Guid.Parse("00000000-0000-0000-0000-0000000000b2"),
                                 H3 = Guid.Parse("00000000-0000-0000-0000-0000000000b3"), H4 = Guid.Parse("00000000-0000-0000-0000-0000000000b4");

    private static ActionRowCatalog Cat() => ActionRowCatalog.FromRows(new[]
    {
        (A1, new ActionRowCatalog.Row("combat_action_sync_attack", "CombatAttackComboGen",
            "l_halberd+r_halberd+c07+ol_longsword+or_longsword+sZ1+rightGuard+eZ2+slash+attack_heavy+oppRightGuard+oppMale", 1, 1, 1, ActorClass: Wo197Rules.ManClass, ComboId: "c07", ComboStep: -1)),
        // the right one: the victim holds a longsword, his right guard
        (H1, new ActionRowCatalog.Row("combat_action_sync_hit", "CombatHitComboGen",
            "l_longsword+r_longsword+c07+ol_halberd+or_halberd+oppSZ1+rightGuard+eZ2+oppSlash+oppRightGuard+oppMale", -1, -1, -1, ActorClass: Wo197Rules.ManClass, ComboId: "c07", ComboStep: -1)),
        // his left guard
        (H2, new ActionRowCatalog.Row("combat_action_sync_hit", "CombatHitComboGen",
            "l_longsword+r_longsword+c07+ol_halberd+or_halberd+oppSZ1+leftGuard+eZ2+oppSlash+oppRightGuard+oppMale", -1, -1, -1, ActorClass: Wo197Rules.ManClass, ComboId: "c07", ComboStep: -1)),
        // another combo: never
        (H3, new ActionRowCatalog.Row("combat_action_sync_hit", "CombatHitComboGen",
            "l_longsword+r_longsword+c05+ol_halberd+or_halberd+oppSZ1+rightGuard+eZ2+oppSlash+oppRightGuard+oppMale", -1, -1, -1, ActorClass: Wo197Rules.ManClass, ComboId: "c05", ComboStep: -1)),
        // a short-sword victim
        (H4, new ActionRowCatalog.Row("combat_action_sync_hit", "CombatHitComboGen",
            "c07+ol_halberd+or_halberd+oppSZ1+rightGuard+eZ2+oppSlash+r_shortSwords+oppRightGuard+l_noShield+oppMale", -1, -1, -1, ActorClass: Wo197Rules.ManClass, ComboId: "c07", ComboStep: -1)),
    });

    [Fact]
    public void VictimRow_ThePairingKeyAndTheMirroredTags()
    {
        var cat = Cat();
        Assert.True(cat.TryGet(A1, out var atk));
        var v = cat.VictimRow(atk, null, Wo197Rules.ManClass);
        Assert.NotNull(v);
        Assert.Equal(H1, v!.Value.Guid);   // his right guard, the attacker's longsword guess
        var v2 = cat.VictimRow(atk, new[] { "r_shortSwords", "l_noShield" }, Wo197Rules.ManClass);
        Assert.Equal(H4, v2!.Value.Guid);  // the victim's real weapon
    }

    [Fact]
    public void VictimRow_NoneForAPlainRowOrAMissingCombo()
    {
        var cat = Cat();
        var plain = new ActionRowCatalog.Row("combat_action_attack", "CombatAttackGen", "r_swords+aZ1", 1, 1, 1);
        Assert.Null(cat.VictimRow(plain, null, 0));
        var other = new ActionRowCatalog.Row("combat_action_sync_attack", "CombatAttackComboGen", "c99+l_halberd+r_halberd", 1, 1, 1, ComboId: "c99");
        Assert.Null(cat.VictimRow(other, null, 0));
        Assert.Empty(Wo197Rules.VictimFragments("CombatAttackGen"));
        Assert.Contains("CombatStopSlave", Wo197Rules.VictimFragments("CombatStopMaster"));
        Assert.DoesNotContain("CombatStopDeathSlave", Wo197Rules.VictimFragments("CombatStopMaster"));
        Assert.DoesNotContain("CombatHitSyncDeathGen", Wo197Rules.VictimFragments("CombatAttackSyncGen"));   // live b2pair: a punch's victim died
        Assert.Contains("CombatHitComboDeathGen", Wo197Rules.VictimFragments("CombatAttackComboDeathGen"));
    }

    [Fact]
    public void Clinch_PayloadRoundTrips()
    {
        var m = Guid.NewGuid(); var s = Guid.NewGuid();
        var e = Wo197Rules.ClinchEvent(7, (byte)(Wo197Rules.ClinchMove | Wo197Rules.ClinchRows | Wo197Rules.ClinchMaster), m, s);
        Assert.True(RowEvent.TryFromBytes(e.ToBytes(), out var back));
        Assert.Equal(m, back.Row);
        Assert.Equal(s, Wo197Rules.ClinchSlaveRow(back));
        Assert.Equal("move", Wo197Rules.ClinchPhaseName(back.Flags));
        var enter = Wo197Rules.ClinchEvent(7, Wo197Rules.ClinchEnter, Guid.Empty, Guid.Empty);
        Assert.True(RowEvent.TryFromBytes(enter.ToBytes(), out var eb));
        Assert.Equal(Guid.Empty, Wo197Rules.ClinchSlaveRow(eb));
        Assert.Equal(4, Wo197Rules.ClinchGuardType(-1, true));
        Assert.Equal(24, Wo197Rules.ClinchGuardType(-1, false));
        Assert.Equal(25, Wo197Rules.ClinchGuardType(25, true));
    }

    [Fact]
    public void ClinchGuardRows_BothHalvesOfOneFamilyAndGuardType()
    {
        Guid G(int n) => Guid.Parse($"00000000-0000-0000-0000-0000000001{n:D2}");
        var cat = ActionRowCatalog.FromRows(new[]
        {
            (G(1), new ActionRowCatalog.Row("combat_action_guard_sync_movement", "CombatClinchStartGuardMasterGen", "r_noweapon+or_noweapon+clinch+l_noShield+ol_noShield+oppMale", -1, -1, -1, GuardType: 4)),
            (G(2), new ActionRowCatalog.Row("combat_action_guard_sync_movement", "CombatClinchStartGuardSlaveGen", "r_noweapon+or_noweapon+clinch+l_noShield+ol_noShield+oppMale", -1, -1, -1, GuardType: 4)),
            (G(3), new ActionRowCatalog.Row("combat_action_guard_sync_movement", "CombatClinchStartGuardMasterGen", "l_longsword+r_longsword+clinch1+oppMale", -1, -1, -1, GuardType: 24)),
        });
        var bare = cat.ClinchGuardRows("CombatClinchStartGuard", 4, new[] { "r_noweapon" });
        Assert.NotNull(bare);
        Assert.Equal("CombatClinchStartGuardSlaveGen", bare!.Value.Slave.Fragment);
        Assert.Null(cat.ClinchGuardRows("CombatClinchStartGuard", 24, null));   // no slave half for guard 24 here
        Assert.Null(cat.ClinchGuardRows("CombatClinchGuard", 4, null));         // no loop rows for the bare-handed clinch in the game's tables
    }

    [Fact]
    public void Knockout_TheEngineBitsEdges()
    {
        const byte ko = Protocol.PlayerStateFlagEngineKnockout;
        Assert.Equal(Wo197Rules.KnockEdge.Down, Wo197Rules.KnockoutEdge(0, (byte)(ko | Protocol.PlayerStateFlagUnconscious)));
        Assert.Equal(Wo197Rules.KnockEdge.Up, Wo197Rules.KnockoutEdge(ko, 0));
        Assert.Equal(Wo197Rules.KnockEdge.None, Wo197Rules.KnockoutEdge(ko, ko));
        // the death guard's knockdown alone is not the engine's knockout
        Assert.Equal(Wo197Rules.KnockEdge.None, Wo197Rules.KnockoutEdge(0, (byte)(Protocol.PlayerStateFlagUnconscious | Protocol.PlayerStateFlagKnockedDown)));
        Assert.True(Wo197Rules.EngineKnockout(true, false));
        Assert.False(Wo197Rules.EngineKnockout(true, true));
        Assert.False(Wo197Rules.EngineKnockout(null, false));
    }
}
