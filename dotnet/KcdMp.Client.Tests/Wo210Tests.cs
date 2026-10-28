// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-210: the switches, the DLL's switch word, the copy's posture release, the asks and the whole-body re-ask.</summary>
public class Wo210Tests
{
    private static readonly ActivityState Bed = new(ActivityState.Lying, 0, 0x0EA9808879AF74EDUL, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0);
    private static readonly ActivityState Bench = new(ActivityState.Sitting, 0, 0x07A138D7DEBBE48CUL, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0);

    [Fact]
    public void A_filtered_blow_has_its_own_damage_flag_and_is_never_noise()
    {
        // E.2: the joiner's blow on a copy, not judged on his screen, travels with no numbers and the "judge it there" bit
        byte others = Protocol.DamageFlagSuppressHitReaction | Protocol.NpcDamageFlagFatal | Protocol.NpcDamageFlagAttributed;
        Assert.Equal(0, Protocol.NpcDamageFlagJudgeThere & others);
        Assert.Equal(0x08, Protocol.NpcDamageFlagJudgeThere);
        Assert.True(Wo147Rules.HitCarriesNothing(0f, 0f));   // the noise gate a filtered blow skips (it carries nothing by design)
        Assert.Equal(0x20u, Wo210Rules.ConfigBits(n => n == "copy_hit_local_none"));
    }

    [Fact]
    public void Every_switch_ships_on_and_maps_to_its_native_bit()
    {
        Assert.Equal(0xFDu, Wo210Rules.ConfigBits(_ => true));   // 0x02 (clinch v2) reserved: not in this build
        Assert.Equal(0x01u, Wo210Rules.ConfigBits(n => n == "copy_state_always"));
        Assert.Equal(0u, Wo210Rules.ConfigBits(n => n == "clinch_v2"));
        Assert.Equal(0x80u, Wo210Rules.ConfigBits(n => n == "state_one_request"));
        Assert.Equal(0u, Wo210Rules.ConfigBits(n => n is "listen_host_keys" or "conv_hover"));   // agent-only switches have no DLL bit
        foreach (var n in Wo210Rules.SwitchNames) Assert.True(Wo210Rules.Default(n, null, null));
        Assert.False(Wo210Rules.Default("copy_state_always", "copy_state_always", null));   // a twin
        Assert.Equal(138, Protocol.Wo210OpConfig);
        Assert.Equal(140, Protocol.Wo210OpAsks);
        Assert.Equal(0x7E, Protocol.NpcAnimAskUp);
        var row = Protocol.JoinWireFor(Protocol.NpcAnimAskUp);
        Assert.NotNull(row);                                    // the relay's table has it
        Assert.Equal(Protocol.JoinFrom.Joiner, row!.Value.From);
    }

    [Theory]
    [InlineData("copy_state_always=off", true, "copy_state_always", false)]
    [InlineData("conv_hover=on", true, "conv_hover", true)]
    [InlineData("clinch_v2=maybe", false, "", false)]
    [InlineData("copy_attack_ai=on", false, "", false)]
    [InlineData(null, false, "", false)]
    public void The_mods_cfg_event_parses(string? arg, bool ok, string name, bool on)
    {
        Assert.Equal(ok, Wo210Rules.TryParseCfg(arg, out string n, out bool v));
        Assert.Equal(name, n);
        Assert.Equal(on, v);
    }

    [Fact]
    public void A_copy_whose_host_npc_left_its_bed_is_released()
    {
        // session E: the host's villagers got up after the shared sleep; their copies' rows said "none" and the copies stayed in bed
        Assert.True(Wo210Rules.ReleaseOnLeave(true, false, true, Bed, ActivityState.None));
        Assert.True(Wo210Rules.ReleaseOnLeave(true, false, true, Bed, Bench));                   // from the bed to a bench: out of the bed first
        Assert.False(Wo210Rules.ReleaseOnLeave(true, false, true, Bed, Bed));                    // the same bed (the 10 s refresh): nothing
        Assert.False(Wo210Rules.ReleaseOnLeave(true, false, true, ActivityState.None, Bench));   // sitting down: the placement's own path
        Assert.False(Wo210Rules.ReleaseOnLeave(true, true, true, Bed, ActivityState.None));      // a figure: WO-160's own release
        Assert.False(Wo210Rules.ReleaseOnLeave(true, false, false, Bed, ActivityState.None));    // never placed by this agent
        Assert.False(Wo210Rules.ReleaseOnLeave(false, false, true, Bed, ActivityState.None));    // the twin: the switch off
    }

    [Fact]
    public void The_dlls_asks_parse_and_bad_rows_end_the_list()
    {
        byte[] b = [2, 1, 10, .. "ttkc_man_5"u8, 1, 13, .. "ttkc_woman_10"u8];
        var a = Wo210Rules.ParseAsks(b);
        Assert.Equal(2, a.Count);
        Assert.Equal((Protocol.Wo210AskRelease, "ttkc_man_5"), a[0]);
        Assert.Empty(Wo210Rules.ParseAsks([1, 1, 40, (byte)'x']));   // a name longer than the payload
        Assert.Empty(Wo210Rules.ParseAsks([]));
    }

    [Fact]
    public void The_whole_body_re_ask_round_trips()
    {
        var body = Wo210Rules.AnimAskBody(["ttkc_man_5", "ttkc_scribe"]);
        Assert.NotNull(body);
        Assert.True(Wo210Rules.TryParseAnimAsk(body, out var names));
        Assert.Equal(["ttkc_man_5", "ttkc_scribe"], names);
        Assert.True(Wo210Rules.TryParseAnimAsk(Wo210Rules.AnimAskBody([]), out var all));
        Assert.Empty(all);                                                // every body
        Assert.Null(Wo210Rules.AnimAskBody(["bad name"]));
        Assert.False(Wo210Rules.TryParseAnimAsk([1, 3, (byte)'a', (byte)'b'], out _));   // short
        Assert.False(Wo210Rules.TryParseAnimAsk([0, 9], out _));                         // trailing bytes
    }

    [Fact]
    public void The_benchs_pipe_block_parses_ranges_and_is_empty_when_unset()
    {
        var (t, o) = Wo210PipeBlock.Parse("types=0x10-0x12,0x40|ops=130-132, 7");
        Assert.Equal(new HashSet<int> { 0x10, 0x11, 0x12, 0x40 }, t);
        Assert.Equal(new HashSet<int> { 7, 130, 131, 132 }, o);
        var (t0, o0) = Wo210PipeBlock.Parse(null);
        Assert.Empty(t0); Assert.Empty(o0);
        Assert.Empty(Wo210PipeBlock.Parse("bogus;types=;ops=x-y").Ops);
    }

    [Fact]
    public void A_catch_up_profile_this_player_has_needs_no_dll_op()
    {
        var mine = new HashSet<string>(["kut_q_bandits", "zranenylovci_wolf_1"], StringComparer.Ordinal);
        Assert.True(Wo210Rules.ProfileAlreadyActive(true, '=', "kut_q_bandits", mine));
        Assert.False(Wo210Rules.ProfileAlreadyActive(true, '=', "kut_q_other", mine));    // missing here: the DLL enables it
        Assert.False(Wo210Rules.ProfileAlreadyActive(true, '+', "kut_q_bandits", mine));  // a live change always goes to the DLL
        Assert.False(Wo210Rules.ProfileAlreadyActive(true, '-', "kut_q_bandits", mine));
        Assert.False(Wo210Rules.ProfileAlreadyActive(true, '=', "kut_q_bandits", null));  // no snapshot: the DLL answers
        Assert.False(Wo210Rules.ProfileAlreadyActive(false, '=', "kut_q_bandits", mine)); // the twin (mp_profile_snapshot off): one op per row
    }

    [Fact]
    public void A_combos_later_swing_holds_the_defence_for_the_hosts_word()
    {
        Assert.True(Wo210Rules.ComboContinues(10_000, 11_200));
        Assert.True(Wo210Rules.ComboContinues(10_000, 10_000 + Wo210Rules.ComboGapMs));
        Assert.False(Wo210Rules.ComboContinues(10_000, 10_001 + Wo210Rules.ComboGapMs));
        Assert.False(Wo210Rules.ComboContinues(0, 500));          // no earlier blow
        Assert.False(Wo210Rules.ComboContinues(10_000, 9_000));   // out of order
        Assert.Equal(600, Wo210Rules.ComboHoldMs(0));
        Assert.Equal(640, Wo210Rules.ComboHoldMs(80));
        Assert.Equal(Wo210Rules.ComboHoldCapMs, Wo210Rules.ComboHoldMs(900));
        Assert.Equal(600, Wo210Rules.ComboHoldMs(-5));
    }

    [Fact]
    public void A_blow_with_no_swing_row_is_a_plain_slash_the_host_computes()
    {
        Assert.Equal(new Wo166Rules.Strike(-1, -1, 1, 2, 1, 1.0f), Wo210Rules.FallbackStrike(null));
    }

    [Theory]
    [InlineData("Topic index '2' activated.", 2)]
    [InlineData("Topic index '0' activated.\u0001", 0)]   // the log tail's trailing character
    [InlineData("Topic index '-1' activated.", -1)]       // the haggle's
    [InlineData("Topic index '9' activated.", -1)]
    [InlineData("Topic index '2' selected.", -1)]
    public void The_starters_cursor_line_parses(string line, int row)
    {
        Assert.Equal(row, Wo210Rules.TopicActivated(line));
    }

    [Fact]
    public void The_hover_text_round_trips_and_the_pick_holds_the_line()
    {
        string t = Wo210Rules.HoverText(7, 3, 2, 41);
        Assert.Equal("v1|7|3|2|41", t);
        Assert.True(Wo210Rules.TryParseHover(t, out int cid, out int dec, out int row, out long seq));
        Assert.Equal((7, 3, 2, 41L), (cid, dec, row, seq));
        Assert.Equal("v1|7|3|-1|42", Wo210Rules.HoverText(7, 3, 12, 42));
        Assert.False(Wo210Rules.TryParseHover("v2|7|3|2|41", out _, out _, out _, out _));
        Assert.False(Wo210Rules.TryParseHover("v1|7|3|9|41", out _, out _, out _, out _));
        Assert.Equal(6_000, Wo210Rules.LineReleaseMs(5_000, 5_100));           // the pick's second
        Assert.Equal(4_000 + Wo210Rules.LineHoldMaxMs, Wo210Rules.LineReleaseMs(5_000, 4_000));   // never more than the cap after the line
    }
}
