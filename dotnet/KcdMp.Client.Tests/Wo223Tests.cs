// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

public class Wo223Tests
{
    [Fact]
    public void TheSwitchesAndTheirBits()
    {
        Assert.Equal(["w223_perceive", "w223_copy_target", "w223_combat_mode", "w223_guard", "w223_notice_log"], W223Switch.NativeNames);
        Assert.Equal(0x1Fu, W223Switch.NativeBits(_ => true));
        Assert.Equal(0x1Eu, W223Switch.NativeBits(n => n != "w223_perceive"));
        Assert.Equal(0u, W223Switch.NativeBits(_ => false));
        Assert.Contains("w223_split", W223Switch.Names);                 // the mod's own: no native bit, the DLL's word unchanged
        Assert.DoesNotContain("w223_split", W223Switch.NativeNames);
        Assert.True(W223Switch.TwinOff("all", "w223_split"));
    }

    [Fact]
    public void TheTwinTurnsEverythingOffButTheInstrument()
    {
        Assert.True(W223Switch.TwinOff("all", "w223_perceive"));
        Assert.True(W223Switch.TwinOff("all", "w223_copy_target"));
        Assert.False(W223Switch.TwinOff("all", "w223_notice_log"));
        Assert.True(W223Switch.TwinOff("w223_guard, w223_perceive", "w223_guard"));
        Assert.False(W223Switch.TwinOff("w223_guard", "w223_perceive"));
        Assert.False(W223Switch.TwinOff(null, "w223_perceive"));
        Assert.False(W223Switch.TwinOff("", "w223_perceive"));
    }

    [Fact]
    public void TheOpsAndTheSideChannelKindAreAppendOnly()
    {
        Assert.Equal(210, Protocol.Wo223OpConfig);
        Assert.Equal(211, Protocol.Wo223OpStatus);
        Assert.Equal(212, Protocol.Wo223OpAttend);
        Assert.Equal(213, Protocol.Wo223OpAttendSet);
        Assert.Equal(215, Protocol.Wo223OpCallPin);
        Assert.True(Wo223Rules.TryCallName("tkop_vrahBandit_3", out string called) && called == "tkop_vrahBandit_3");
        Assert.False(Wo223Rules.TryCallName("x\"); os.exit()", out _));
        Assert.False(Wo223Rules.TryCallName("", out _));
        Assert.Equal(13, Protocol.W203Stealth);   // APPEND-ONLY: after WO-214's figure hand (12)
        Assert.Equal("stealth", Protocol.W203KindName(Protocol.W203Stealth));
    }

    [Theory]
    [InlineData("kcd2mp_1|tkop_vrahBandit_3,tkop_vrahBandit_6", "kcd2mp_1|tkop_vrahBandit_3,tkop_vrahBandit_6")]
    [InlineData("kcd2mp_12|", "kcd2mp_12|")]                       // nobody attends any more: the figure's pairs are cleared
    [InlineData("kcd2mp_1|SpawnedAnimal_Wolf_6CD09AF1_2", "kcd2mp_1|SpawnedAnimal_Wolf_6CD09AF1_2")]
    public void AnAttendLineOfTheModsOwnFormIsTaken(string arg, string want)
    {
        Assert.True(Wo223Rules.TryAttendText(arg, out string text));
        Assert.Equal(want, text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tkop_vrahBandit_3")]                 // no figure
    [InlineData("Dude|tkop_vrahBandit_3")]            // the figure is never the local player
    [InlineData("kcd2mp_|tkop_vrahBandit_3")]
    [InlineData("kcd2mp_x|tkop_vrahBandit_3")]
    [InlineData("kcd2mp_1|a b")]                      // a name the game never gives
    [InlineData("kcd2mp_1|a\"b")]
    [InlineData("kcd2mp_1|a;System.Quit()")]
    public void AnythingElseIsRefusedWhole(string? arg) => Assert.False(Wo223Rules.TryAttendText(arg, out _));

    [Theory]
    [InlineData("minR=2.500,minVis=-2.000,tP=4.000,tConPos=-2.000,tConNeg=-4.000,gain=0.1000,decay=-0.500,npcMod=0.000,skT=0.600,skR=0.300", true)]
    [InlineData("tP=4", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("tP=4\") System.Quit() --", false)]   // only names and numbers ever reach a console line
    [InlineData("tP=nan", false)]
    [InlineData("tP=1e9", false)]
    [InlineData("t P=4", false)]
    [InlineData("tP=", false)]
    [InlineData("=4", false)]
    public void TheEngineTextIsNamesAndNumbersOnly(string? s, bool ok) => Assert.Equal(ok, Wo223Rules.TryEngineText(s, out _));

    [Fact]
    public void TooManyNamesAreRefused()
    {
        string many = "kcd2mp_1|" + string.Join(',', Enumerable.Range(0, Wo223Rules.MaxNames + 1).Select(i => "npc_" + i));
        Assert.False(Wo223Rules.TryAttendText(many, out _));
        string ok = "kcd2mp_1|" + string.Join(',', Enumerable.Range(0, Wo223Rules.MaxNames).Select(i => "npc_" + i));
        Assert.True(Wo223Rules.TryAttendText(ok, out _));
    }

    [Fact]
    public void TheStealthSnapshotGoesOnAChangeAndEveryTenSeconds()
    {
        const string a = "c=0,l=0,v=1.19,n=0.43,s=0.60,k=7", b = "c=1,l=0,v=0.69,n=0.13,s=0.10,k=7";
        Assert.True(Wo223Rules.StealthDue(a, "", 5_000));       // the first one
        Assert.False(Wo223Rules.StealthDue(a, a, 5_000));       // unchanged, sent 5 s ago
        Assert.True(Wo223Rules.StealthDue(a, a, 10_000));       // unchanged: every ten seconds
        Assert.True(Wo223Rules.StealthDue(b, a, 1_000));        // changed, a second since the last
        Assert.False(Wo223Rules.StealthDue(b, a, 400));         // changed, but at most once a second
        Assert.False(Wo223Rules.StealthDue("", a, 60_000));     // the mod never said anything
    }

    [Fact]
    public void TheWaitMayRunLongOnlyWhileHeAnswersNearlyEveryBlow()
    {
        Assert.Equal(1500, Wo223Rules.DefCapMs(0, 0, 1500));      // nothing known yet: WO-222's cap
        Assert.Equal(1500, Wo223Rules.DefCapMs(9, 9, 1500));      // fewer than ten waits known
        Assert.Equal(3000, Wo223Rules.DefCapMs(10, 10, 1500));
        Assert.Equal(3000, Wo223Rules.DefCapMs(36, 40, 1500));    // 90 %
        Assert.Equal(1500, Wo223Rules.DefCapMs(35, 40, 1500));    // under 90 %: half his copies are not in his fight -- every wait costs
        Assert.Equal(3000, Wo222Rules.DefHoldMs([200, 2600, 300, 250, 220], 0, 3000) > 1500 ? 3000 : 0);   // the slowest answer + the margin, to the long cap
        Assert.Equal(1500, Wo222Rules.DefHoldMs([200, 2600, 300, 250, 220]));                              // WO-222's own cap unchanged
    }

    [Fact]
    public void ALateAnswerCountsAsAnAnswer()
    {
        var t = new Wo223Rules.AnswerTally(4);
        t.Add(true); t.Add(false); t.Add(true);
        Assert.Equal((2, 3), t.Read());
        t.LateAnswer();
        Assert.Equal((3, 3), t.Read());
        t.LateAnswer();                          // nothing left to answer
        Assert.Equal((3, 3), t.Read());
        t.Add(false); t.Add(false);              // the ring holds the last four
        Assert.Equal((2, 4), t.Read());
    }

    [Fact]
    public void TheOldestWaitingBlowTakesAnAnswerFirst()
    {
        // two blows of one attacker wait (landed at 1000 and 2000); his answer arrives at 2050
        Assert.True(Wo223Rules.OldestFirst(2050, 1000, [1000, 2000], 900));     // the older blow may take it
        Assert.False(Wo223Rules.OldestFirst(2050, 2000, [1000, 2000], 900));    // the younger one leaves it to the older
        Assert.True(Wo223Rules.OldestFirst(2050, 2000, [2000], 900));           // alone: its own
        // an answer that came long before the older blow (over the early window) is no answer of the older one's
        Assert.True(Wo223Rules.OldestFirst(50, 2000, [1000, 2000], 900));
    }

    [Fact]
    public void AGrabsBlowIsTheHostsAtOnce()
    {
        Assert.True(Wo223Rules.GrabRow("CombatAttackSyncGen", "clinch1+eZ1+endClinch1+aZ2+slash+attack_heavy+r_stickWeapon+l_noShield+step0+oppMale"));
        Assert.False(Wo223Rules.GrabRow("CombatAttackGen", "rightGuard+eZ2+aZ2+slash+attack_heavy+r_bluntWeapon+l_noShield+oppMale"));   // a plain swing
        Assert.False(Wo223Rules.GrabRow("CombatAttackComboGen", "c03+sZ2+leftGuard+eZ0+aZ5+slash"));                                       // a combo
        Assert.False(Wo223Rules.GrabRow("CombatAttackSyncGen", "rightGuard+eZ2+aZ2+slash"));                                               // paired, but no grab
    }

    [Fact]
    public void NothingLandedAnswersOnlyABlowThatCameBeforeIt()
    {
        Assert.False(Wo223Rules.NoneAnswers(900, 1000, 2000));    // told before the blow: it speaks of the attack before
        Assert.False(Wo223Rules.NoneAnswers(1100, 1000, 1200));   // told after it, but a verdict may still be a frame behind
        Assert.True(Wo223Rules.NoneAnswers(1100, 1000, 1400));    // the grace is over: the host's own verdict goes
    }
}
