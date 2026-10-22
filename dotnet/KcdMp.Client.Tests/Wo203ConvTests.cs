// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-203 I.3b: the starter's gesture lines, the ConvAnim text, the clip pick (Wo203ConvRules).</summary>
public class Wo203ConvTests
{
    private const string New = "New dialogue 'Barbora.trosecko.zebrackaBarbora.hibernable.barbora__welcome_to_ow_dialog (normal - Id: 233) (Actor::RequestDialog)' is starting. Params: souls = 'Ex: Dude; Ex: ttkc_barbora' forced = false, forced decision = none";

    [Fact]
    public void OwnConversationNeedsThisPlayerAndAnother()
    {
        var c = Wo203ConvRules.ParseOwnConversation(New);
        Assert.NotNull(c);
        Assert.Equal(233, c!.Value.Cid);
        Assert.Equal(["ttkc_barbora"], c.Value.Others);
        Assert.Null(Wo203ConvRules.ParseOwnConversation("New dialogue 'x.y (normal - Id: 9) (AI::DoMonologue)' is starting. Params: souls = 'Ex: Dude' forced = false"));
        Assert.Null(Wo203ConvRules.ParseOwnConversation("New dialogue 'x.y (CombatShout - Id: 9) (z)' is starting. Params: souls = 'Ex: a - meta override: X; Nx: b' forced = false"));
        var three = Wo203ConvRules.ParseOwnConversation("New dialogue 'q (normal - Id: 12) (z)' is starting. Params: souls = 'Ex: Dude; Ex: a_1; Nx: b_2 - meta override: M' forced = false");
        Assert.Equal(["a_1", "b_2"], three!.Value.Others);
    }

    [Fact]
    public void AnimAndEndLines()
    {
        var a = Wo203ConvRules.ParseAnim("[ID: 233] DialogCommand-Anim: dialogId=233, who=BARA, anim=ADLG_Disagree");
        Assert.Equal((233, "BARA", "ADLG_Disagree"), a);
        Assert.Null(Wo203ConvRules.ParseAnim("[ID: 233] DialogCommand-Look: dialogId=233, from=BARA, to=HENRY"));
        Assert.Equal(233, Wo203ConvRules.ParseEnd("[ID: 233] Dialog ending [Ex0: Dude Ex1: ttkc_barbora state: CLEANUP flags: 9104]"));
        Assert.Equal(7, Wo203ConvRules.ParseEnd("[ID: 7] Dialog interrupted. [Ex0: Dude Ex1: x state: WAITING_FOR_TWINS flags: 1]"));
        Assert.Equal(-1, Wo203ConvRules.ParseEnd("[ID: 7] DialogCommand-Anim: dialogId=7, who=HENRY, anim=ADLG_Nod"));
    }

    [Fact]
    public void PlaceTwoPersonAndMore()
    {
        Assert.Equal(("@", "ttkc_barbora"), Wo203ConvRules.Place("HENRY", ["ttkc_barbora"]));
        Assert.Equal(("ttkc_barbora", "@"), Wo203ConvRules.Place("BARA", ["ttkc_barbora"]));
        Assert.Equal(("@", "a"), Wo203ConvRules.Place("HRAC", ["a", "b"]));
        Assert.Null(Wo203ConvRules.Place("BARA", ["a", "b"]));   // three souls: a role is not a soul
    }

    [Fact]
    public void AnimTextRoundTripsAndFits()
    {
        string t = Wo203ConvRules.AnimText(233, 2, "@", "ADLG_Confusion", "ttkc_barbora");
        Assert.Equal("v1|233|2|@|ADLG_Confusion|ttkc_barbora", t);
        Assert.True(Wo203ConvRules.TryParseAnim(t, out int cid, out byte st, out string body, out string frag, out string face));
        Assert.Equal((233, (byte)2, "@", "ADLG_Confusion", "ttkc_barbora"), (cid, st, body, frag, face));
        Assert.False(Wo203ConvRules.TryParseAnim("v1|233|2|@|ADLG_Nod|@", out _, out _, out _, out _, out _));        // the figure faces itself
        Assert.False(Wo203ConvRules.TryParseAnim("v1|233|2|a\"b|ADLG_Nod|@", out _, out _, out _, out _, out _));     // not a soul name
        Assert.False(Wo203ConvRules.TryParseAnim("v1|233|2|a|Nod|@", out _, out _, out _, out _, out _));             // not a fragment
        string worst = Wo203ConvRules.AnimText(int.MaxValue, 255, new string('n', 64), "ADLG_" + new string('f', 60), "@");
        Assert.True(worst.Length <= Protocol.ConvTextMax);
        _ = new LootMsg(Protocol.ConvAnim, 1, worst).BuildUp(Protocol.ConvUp, Protocol.JoinTargetHost);   // refuses an oversize text
    }

    private const string Adb = """
        <ADLG_Nod>
          <Fragment BlendOutDuration="0.2" Tags="noScope"><AnimLayer><Blend ExitTime="0" /><Animation name="dlg_male_neutral_stand_nod03_upper" /></AnimLayer></Fragment>
          <Fragment BlendOutDuration="0.2" Tags="pursy"><AnimLayer><Blend ExitTime="0" /><Animation name="dlg_male_pursy_stand_nod" /></AnimLayer></Fragment>
          <Fragment BlendOutDuration="0.2"><AnimLayer><Blend ExitTime="0" /><Animation name="dlg_male_neutral_bench_nod" /></AnimLayer></Fragment>
          <Fragment BlendOutDuration="0.2"><AnimLayer><Blend ExitTime="0" /><Animation name="dlg_male_neutral_stand_nod01" /></AnimLayer></Fragment>
          <Fragment BlendOutDuration="0.2"><AnimLayer><Blend ExitTime="0" /><Animation name="dlg_male_neutral_stand_weapon_nod" /></AnimLayer></Fragment>
          <Fragment BlendOutDuration="0.2"><AnimLayer><Blend ExitTime="0" /><Animation name="dlg_male_neutral_stand_nod02" /></AnimLayer></Fragment>
        </ADLG_Nod>
        <ADLG_Sit>
          <Fragment><AnimLayer><Blend ExitTime="0" /><Animation name="dlg_male_happy_bench_sit" /></AnimLayer></Fragment>
        </ADLG_Sit>
        <ADLG_Laugh>
          <Fragment><AnimLayer><Blend ExitTime="0" /><Animation name="dlg_male_happy_stand_laugh" /></AnimLayer></Fragment>
        </ADLG_Laugh>
        """;

    [Fact]
    public void PickTheStandingNeutralVariantByCount()
    {
        var db = Wo203ConvRules.ParseAdb(Adb);
        Assert.Equal(6, db["ADLG_Nod"].Count);
        Assert.Equal("dlg_male_neutral_stand_nod01", Wo203ConvRules.Pick(db, "ADLG_Nod", 0));
        Assert.Equal("dlg_male_neutral_stand_nod02", Wo203ConvRules.Pick(db, "ADLG_Nod", 1));
        Assert.Equal("dlg_male_neutral_stand_nod01", Wo203ConvRules.Pick(db, "ADLG_Nod", 2));
        Assert.Equal("dlg_male_happy_stand_laugh", Wo203ConvRules.Pick(db, "ADLG_Laugh", 5));   // no neutral one: any good one
        Assert.Null(Wo203ConvRules.Pick(db, "ADLG_Sit", 0));                                   // bench only: never on a standing body
        Assert.Null(Wo203ConvRules.Pick(db, "ADLG_Unknown", 0));
    }
}
