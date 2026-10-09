// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-164: the talk line, the sweep triggers, the quest fix, the flee watchdog, the seats, the mark snapshot, the wire.</summary>
public class Wo164Tests
{
    // ---------------------------------------------------------------- the wire

    [Fact]
    public void The_w164_pair_is_in_the_join_table_either_way_append_only()
    {
        Assert.Equal(((byte)0x74, (byte)0x75), (Protocol.W164Up, Protocol.W164Down));
        var row = Protocol.JoinWireFor(Protocol.W164Up)!.Value;
        Assert.Equal(Protocol.W164Down, row.Down);
        Assert.Equal(Protocol.JoinFrom.Either, row.From);
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.LootFixedLen + Protocol.W164TextMax, row.Max);
        Assert.Single(Protocol.JoinWire, x => x.Up == Protocol.W164Up);
        Assert.DoesNotContain(Protocol.JoinWire, x => x.Up != Protocol.W164Up && (x.Up is 0x74 || x.Down is 0x75));
        Assert.Equal(10, Protocol.Version);   // append-only: no bump
        Assert.Equal("mark-ping", Protocol.W164KindName(Protocol.W164MarkPing));
        Assert.Equal("torch", Protocol.W164KindName(Protocol.W164Torch));
        Assert.Equal("unknown-9", Protocol.W164KindName(9));
    }

    [Fact]
    public void A_mark_ping_round_trips_and_a_receiver_ignores_an_unknown_kind()
    {
        string id = Wo164Rules.NewMarkId(new Random(7));
        var up = new LootMsg(Protocol.W164MarkPing, 5, id).BuildUp(Protocol.W164Up, Protocol.JoinTargetHost);
        Assert.Equal(Protocol.W164Up, up[0]);
        // the relay turns an up into a down: [type][len][src][joinId:4][body]; the agent decodes the body after the header
        var body = up.AsSpan(3 + Protocol.JoinHeaderLen).ToArray();
        Assert.True(LootMsg.TryDecode(body, out var m));
        Assert.Equal(Protocol.W164MarkPing, m.Kind);
        Assert.True(W164Text.IsMarkId(m.Text));
        var odd = new LootMsg(9, 5, "whatever").BuildUp(Protocol.W164Up, Protocol.JoinTargetHost);
        Assert.True(LootMsg.TryDecode(odd.AsSpan(3 + Protocol.JoinHeaderLen).ToArray(), out var m2));
        Assert.Equal("unknown-9", Protocol.W164KindName(m2.Kind));   // GameBridge counts it as malformed and does nothing
    }

    [Theory]
    [InlineData("ab12cd34ef", true)]
    [InlineData("abcdef", true)]
    [InlineData("abcde", false)]
    [InlineData("ABCDEF12", false)]
    [InlineData("ab/12cd34", false)]
    [InlineData("01234567890123456", false)]
    public void A_mark_id_is_short_lowercase_alphanumerics(string s, bool ok) => Assert.Equal(ok, W164Text.IsMarkId(s));

    [Fact]
    public void The_torch_text_parses_strictly()
    {
        Assert.True(W164Text.TryParseTorch(W164Text.Torch(0, true), out byte g, out bool on));
        Assert.Equal((0, true), (g, on));
        Assert.True(W164Text.TryParseTorch("3 0", out g, out on));
        Assert.Equal((3, false), (g, on));
        Assert.False(W164Text.TryParseTorch("3 2", out _, out _));
        Assert.False(W164Text.TryParseTorch("x 1", out _, out _));
        Assert.False(W164Text.TryParseTorch("1 1 1", out _, out _));
        Assert.Null(Wo164Rules.TorchSideEdge(true, true));
        Assert.True(Wo164Rules.TorchSideEdge(null, true));
        Assert.False(Wo164Rules.TorchSideEdge(true, false));
    }

    // ---------------------------------------------------------------- T0

    [Theory]
    [InlineData(null, "talk")]
    [InlineData("Barbora.open_world.shop.smlouvani_v_obchode", "haggle")]
    [InlineData("Barbora.open_world.shop.nakupovani_z_chatu", "chat")]
    [InlineData("Barbora.open_world.minigames.KOSTKY_hraj", "dice")]
    [InlineData("Barbora.trosecko.hledaniPsa.h.dialogy_s_pastevci.zibrid.pokec_s_pastevcem_o_vlcich_1", "quest")]
    [InlineData("generic_greeting", "other")]
    public void A_talk_is_classified_from_the_dialogue_name(string? dlg, string kind) => Assert.Equal(kind, Wo164Rules.TalkKind(dlg));

    [Fact]
    public void A_failed_talk_names_its_cause_in_order()
    {
        Assert.Equal("busy", Wo164Rules.FailCause(true, "greeting", 2, 4, 3));
        Assert.Equal("preempted", Wo164Rules.FailCause(false, "NPC_ZDRAVI_HRACE", 2, 4, 3));
        Assert.Equal("quest", Wo164Rules.FailCause(false, null, 1, 4, 3));
        Assert.Equal("torn", Wo164Rules.FailCause(false, "", 0, 4, 3));
        Assert.Equal("wedged", Wo164Rules.FailCause(false, null, 0, 0, 2));
        Assert.Equal("neither", Wo164Rules.FailCause(false, null, 0, 0, 1));   // Manka / Procek: the third cause, named next round
    }

    // ---------------------------------------------------------------- T1

    [Fact]
    public void The_adaptive_sweep_needs_five_refusals_in_ten_seconds_and_a_cooldown()
    {
        var t = new List<double> { 100, 101.5, 103, 104.5 };
        Assert.False(Wo164Rules.AdaptiveDue(t, 105, -1e9));      // four
        t.Add(106);
        Assert.True(Wo164Rules.AdaptiveDue(t, 106, -1e9));       // five inside 10 s
        Assert.False(Wo164Rules.AdaptiveDue(t, 106, 95));        // swept 11 s ago: the 20 s cooldown
        Assert.False(Wo164Rules.AdaptiveDue(t, 112, -1e9));      // the window moved on: 103..112 holds four
        // the field's pace (1.5 s, then 15 s): the first burst triggers, the 15 s tail does not
        var field = new List<double> { 0, 1.5, 3, 4.5, 6, 21, 36, 51 };
        Assert.True(Wo164Rules.AdaptiveDue(field.Take(5).ToList(), 6, -1e9));
        Assert.False(Wo164Rules.AdaptiveDue(field, 51, -1e9));
        Assert.Equal(3, Wo164Rules.ErrorsBetween(field, 0, 3));
        Assert.Equal(120, Wo164Rules.SweepHoldS);
    }

    // ---------------------------------------------------------------- T6

    [Fact]
    public void The_direct_quest_write_is_for_a_joiner_an_old_enough_mismatch_and_a_safe_type_only()
    {
        Assert.Null(Wo164Rules.QuestFixRefusal(true, "int", 10.5, false, false));
        Assert.Null(Wo164Rules.QuestFixRefusal(true, "bool", 60, false, false));
        Assert.Equal("not-a-joiner", Wo164Rules.QuestFixRefusal(false, "int", 60, false, false));   // never on the host
        Assert.Equal("contested", Wo164Rules.QuestFixRefusal(true, "int", 60, true, false));
        Assert.Equal("a-port-corrects-it", Wo164Rules.QuestFixRefusal(true, "int", 60, false, true));
        Assert.Equal("type-not-safe", Wo164Rules.QuestFixRefusal(true, "Progress", 60, false, false));
        Assert.Equal("type-not-safe", Wo164Rules.QuestFixRefusal(true, "Tribool", 60, false, false));
        Assert.Equal("too-young", Wo164Rules.QuestFixRefusal(true, "int", 9.9, false, false));
    }

    // ---------------------------------------------------------------- D2

    [Fact]
    public void The_flee_watchdog_lets_go_of_an_old_quiet_hold_on_a_fleeing_or_far_enemy()
    {
        Assert.True(Wo164Rules.WatchdogRelease(46, 21, true, 5));
        Assert.True(Wo164Rules.WatchdogRelease(46, 21, false, 31));
        Assert.False(Wo164Rules.WatchdogRelease(44, 60, true, 50));   // too young
        Assert.False(Wo164Rules.WatchdogRelease(60, 19, true, 50));   // a blow in the last 20 s
        Assert.False(Wo164Rules.WatchdogRelease(60, 60, false, 29));  // standing near, not fleeing: a fight
        Assert.True(Wo164Rules.IsFleeUnstance("FleeLookingAround"));
        Assert.True(Wo164Rules.IsFleeUnstance("fleeing_scared"));
        Assert.False(Wo164Rules.IsFleeUnstance("guard"));
        Assert.False(Wo164Rules.IsFleeUnstance(null));
    }

    // ---------------------------------------------------------------- S2 / S3

    [Fact]
    public void A_stale_seat_is_cleared_after_one_and_a_half_seconds_and_unstuck_has_two_steps()
    {
        Assert.True(Wo164Rules.StaleSit(true, false, 1.6));
        Assert.False(Wo164Rules.StaleSit(true, false, 1.4));
        Assert.False(Wo164Rules.StaleSit(true, true, 9));
        Assert.False(Wo164Rules.StaleSit(false, false, 9));
        Assert.Equal(1, Wo164Rules.UnstuckStep(-1));
        Assert.Equal(2, Wo164Rules.UnstuckStep(4));
        Assert.Equal(1, Wo164Rules.UnstuckStep(10.5));
    }

    // ---------------------------------------------------------------- M

    [Fact]
    public void The_snapshot_block_is_capped_scrubbed_and_closed()
    {
        var lines = Enumerable.Range(0, 80).Select(i => $"line {i}").ToList();
        lines[3] = @"path C:\Users\someone\Documents\x.log and 192.168.1.20 and a@b.c";
        var block = Wo164Rules.SnapBlock("ab12cd34ef", "agent", lines);
        Assert.Equal(Wo164Rules.SnapMaxLines, block.Count);
        Assert.All(block, l => Assert.StartsWith("MP-MARK-SNAP mark=ab12cd34ef agent ", l));
        Assert.EndsWith("end lines=59 cut=21", block[^1]);
        Assert.DoesNotContain(block, l => l.Contains("someone") || l.Contains("192.168") || l.Contains("a@b.c"));
        Assert.Contains(block, l => l.Contains("<path>") && l.Contains("<ip>") && l.Contains("<addr>"));
        var small = Wo164Rules.SnapBlock("abcdef", "agent", ["a", "", "b"]);
        Assert.Equal(3, small.Count);
        Assert.EndsWith("end lines=2", small[^1]);
        Assert.Matches("^[a-z0-9]{10}$", Wo164Rules.NewMarkId(new Random(1)));
        Assert.NotEqual(Wo164Rules.NewMarkId(new Random(1)), Wo164Rules.NewMarkId(new Random(2)));
    }

    [Fact]
    public void The_reload_lines_are_plain()
    {
        Assert.Equal("Your host is reloading the world - please wait", Wo164Rules.ReloadStartText);
        Assert.Equal("Back with your host", Wo164Rules.ReloadBackText);
    }
}
