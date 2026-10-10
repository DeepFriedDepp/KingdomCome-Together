// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-166 (docs/WO-166-findings.md): 0.48.2's agent rules -- T2 (the quest direct write: the reply's type at its real offset, the
/// hibernated write counted as applied, uint a safe type, the State's declared type from the quest file, the field's 635 cases), T3
/// (the engine's dialogue state lines). The Lua halves are tools/Test-WO166Synthetic.lua; the native half native/tests/wo166_tests.cpp.
/// </summary>
public class Wo166Tests
{
    // ---------------------------------------------------------------- T2: the reply parser

    /// <summary>The DLL's op 2 / op 9 reply, byte for byte as wo137.cpp builds it.</summary>
    private static byte[] Reply(byte result, int before, int after, string type)
    {
        var b = new List<byte> { result, 1 };
        b.AddRange(BitConverter.GetBytes(before));
        b.Add(1);
        b.AddRange(BitConverter.GetBytes(after));
        b.Add((byte)type.Length);
        b.AddRange(System.Text.Encoding.ASCII.GetBytes(type));
        return b.ToArray();
    }

    [Fact]
    public void The_reply_type_is_read_at_byte_11()
    {
        Assert.Equal("uint", Wo137Rules.ReplyType(Reply(10, 1, 0, "uint")));   // the field's type-refused case: the DLL said uint
        Assert.Equal("int", Wo137Rules.ReplyType(Reply(0, 2, 1, "int")));
        Assert.Equal("", Wo137Rules.ReplyType(Reply(6, 0, 0, "")));
        Assert.Equal("", Wo137Rules.ReplyType(new byte[11]));                   // too short: no type
        var cut = Reply(0, 1, 3, "bool");
        Assert.Equal("", Wo137Rules.ReplyType(cut[..13]));                       // a truncated name is not read past the end
    }

    [Fact]
    public void The_old_offset_printed_an_empty_type_for_every_small_value()
    {
        // what the 0.47.6 parser did: the new value's top byte as the length
        var b = Reply(10, 1, 0, "uint");
        Assert.Equal(0, b[10]);
    }

    [Fact]
    public void A_hibernated_write_counts_as_applied()
    {
        Assert.True(Wo137Rules.WriteTook(0));
        Assert.True(Wo137Rules.WriteTook(1));
        Assert.True(Wo137Rules.WriteTook(11));
        Assert.False(Wo137Rules.WriteTook(6));
        Assert.False(Wo137Rules.WriteTook(10));
        Assert.Equal("changed-hibernated", Wo137Rules.AppliedName(11));
    }

    [Fact]
    public void Uint_is_a_safe_type_enums_and_floats_are_not()
    {
        Assert.True(Wo164Rules.QuestFixTypeSafe("uint"));
        Assert.True(Wo164Rules.QuestFixTypeSafe("int"));
        Assert.True(Wo164Rules.QuestFixTypeSafe("bool"));
        Assert.False(Wo164Rules.QuestFixTypeSafe("float"));
        Assert.False(Wo164Rules.QuestFixTypeSafe("Progress"));
        Assert.False(Wo164Rules.QuestFixTypeSafe("wh::questmodule::QuestProgress"));
        Assert.False(Wo164Rules.QuestFixTypeSafe(null));
    }

    // ---------------------------------------------------------------- T2: the State's declared type

    private const string SmithXml =
        "<Database Name=\"brambora\"><Skald><Module Name=\"porovnani_kvality\"><Nodes>" +
        "<State Name=\"kvalitaMece\" PositionY=\"-20\" PositionX=\"-410\" TypeT=\"uint\"><Constant Name=\"DefaultValue\" Value=\"1\" /></State>" +
        "<State Name=\"hotovo\" TypeT=\"bool\" />" +
        "<State Name=\"postup\" TypeT=\"Progress\"></State>" +
        "<State Name=\"bezTypu\"></State>" +
        "</Nodes></Module></Skald></Database>";

    [Fact]
    public void The_quest_file_names_each_States_type_by_its_engine_path()
    {
        var idx = new QuestValueIndex();
        int n = idx.AddStates("Quests/Final/Barbora/trosecko/kovar/hibernace/porovnani_kvality.xml", SmithXml);
        Assert.Equal(3, n);
        Assert.Equal("uint", idx.StateType("Barbora.trosecko.kovar.hibernace.porovnani_kvality.kvalitaMece"));
        Assert.Equal("bool", idx.StateType("Barbora.trosecko.kovar.hibernace.porovnani_kvality.hotovo"));
        Assert.Equal("Progress", idx.StateType("Barbora.trosecko.kovar.hibernace.porovnani_kvality.postup"));
        Assert.Null(idx.StateType("Barbora.trosecko.kovar.hibernace.porovnani_kvality.bezTypu"));
        Assert.Null(idx.StateType("Barbora.trosecko.kovar.hibernace.porovnani_kvality.nic"));
    }

    [Fact]
    public void Only_the_games_own_quests_are_indexed()
    {
        var idx = new QuestValueIndex();
        Assert.Equal(0, idx.AddStates("Quests/Debug/test/x.xml", SmithXml));
        Assert.Equal(0, idx.AddStates("Quests/Testing/x/y.xml", SmithXml));
        Assert.Equal(3, idx.AddStates("Quests\\Final\\Barbora\\a\\b.xml", SmithXml));   // a backslash path reads the same
        Assert.Equal("uint", idx.StateType("Barbora.a.b.kvalitaMece"));
    }

    [Fact]
    public void The_fields_635_cases_now_pass_the_agents_gate()
    {
        // docs/WO-166-findings.md 0.3: one variable, declared uint, mismatch ages 10 s and up (the 10 s trigger); counts only
        int pass = 0;
        foreach (double age in Enumerable.Range(0, 635).Select(i => 10.0 + i * 2.0))
            if (Wo164Rules.QuestFixRefusal(true, "uint", age, false, false) is null) pass++;
        Assert.Equal(635, pass);
        Assert.Equal("type-not-safe", Wo164Rules.QuestFixRefusal(true, "Progress", 30, false, false));
        Assert.Equal("too-young", Wo164Rules.QuestFixRefusal(true, "uint", 5, false, false));
    }

    // ---------------------------------------------------------------- T3: the engine's dialogue state

    [Fact]
    public void A_dialogue_state_line_names_its_souls_and_state()
    {
        Assert.True(Wo166Rules.TryParseDialogState("[ID: 2122] Dialog interrupted. [Ex0: Dude Ex1: tzel_woman_9 state: WAITING_FOR_TWINS flags: 2107913]", out var s, out var st));
        Assert.Equal("WAITING_FOR_TWINS", st);
        Assert.Equal(new[] { "Dude", "tzel_woman_9" }, s);
        Assert.True(Wo166Rules.TryParseDialogState("[ID: 1966] Dialog ending [Ex0: tzel_woman_10 state: CLEANUP flags: 9104]", out s, out st));
        Assert.Equal("CLEANUP", st);
        Assert.Equal(new[] { "tzel_woman_10" }, s);
        Assert.True(Wo166Rules.TryParseDialogState("[ID: 79] Dialog ends but no response was played. (Forced: 'N') [Ex0: Dude Ex1: ttac_smith state: POSITIONING flags: 1]", out s, out st));
        Assert.Equal("POSITIONING", st);
        Assert.False(Wo166Rules.TryParseDialogState("Soul 'Dude' requested dialog. Assigned id is 2017", out _, out _));
        Assert.False(Wo166Rules.TryParseDialogState("[ID: 5] Dialog interrupted. no souls here", out _, out _));
    }

    [Fact]
    public void The_engine_line_markers_are_the_fields_spellings()
    {
        Assert.StartsWith(Wo166Rules.LootScreenOutPrefix, "PlayAudio: ui_inv_screen_out_one_pane");
        Assert.Contains(Wo166Rules.PauseRequestsTimedOut, "[Warning] PlayerDialogController::NPCPauseRequests timed out after 20.008070.");
        Assert.Contains("PlayAudio: ui_inv_screen_out", LogTailGameTransport.Wo144Prefixes);
        Assert.Contains("PlayerDialogController::NPCPauseRequests timed out", LogTailGameTransport.Wo144Contains);
    }
}
