// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Steam;
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
    // ---------------------------------------------------------------- C2: the stale rule

    [Fact]
    public void The_fields_46_blows_through_the_new_rule()
    {
        // docs/WO-166-findings.md 0.4 (the OCE pair, joined by hit id; counts only): 27 the host captured no swing for (an older row of the
        // NPC played 3-20 s before), 2 the host could not pair (likewise), 15 shown with their row, 2 with no row at all
        var blows = new List<(bool Known, long? Played, long? Recv, bool Fits)>();
        for (int i = 0; i < 27; i++) blows.Add((false, 6000 + i * 500, 6000 + i * 500, false));
        for (int i = 0; i < 2; i++) blows.Add((false, 4000, 4000, false));
        for (int i = 0; i < 15; i++) blows.Add((true, 1200, 1210, true));
        for (int i = 0; i < 2; i++) blows.Add((false, null, null, false));
        int stale = 0, generic = 0, shown = 0;
        foreach (var b in blows)
        {
            var (s, why) = Wo161Rules.Judge(b.Known, false, false, b.Played, b.Recv, null, b.Fits);
            if (why == "row-stale") stale++;
            if (s) shown++;
            if (Wo161Rules.WantsGenericSwing(s, why)) generic++;
        }
        Assert.Equal(46, blows.Count);
        Assert.Equal(0, stale);                       // field: 29 (63 %); required < 10 %
        Assert.Equal(15, shown);
        Assert.Equal(31, generic);                    // a blow with no row of its own still gets the generic lunge before its damage
    }

    [Fact]
    public void A_late_row_fits_up_to_its_hit_time_plus_half_a_second_plus_the_one_way_latency()
    {
        // a row whose own start+hit is 1200 ms, played 1840 ms before the verdict: 640 ms late
        var played = new List<(long, int)> { (1840, 1200) };
        Assert.False(Wo161Rules.BestPlayedAgo(played, 0)!.Value.Fits);       // 640 > 600 (the WO-163 tolerance) and > 500 + 0
        Assert.True(Wo161Rules.BestPlayedAgo(played, 150)!.Value.Fits);      // 640 <= 500 + 150
        Assert.False(Wo161Rules.BestPlayedAgo(new List<(long, int)> { (500, 1200) }, 500)!.Value.Fits);   // early stays within 600
        Assert.Equal(77, Wo161Rules.OneWayMs(154));
        Assert.Equal(0, Wo161Rules.OneWayMs(null));
        Assert.Equal(1000, Wo161Rules.OneWayMs(5000));
    }

    [Fact]
    public void A_host_without_a_swing_is_never_stale_a_known_one_still_is()
    {
        Assert.Equal((false, Wo161Rules.ReasonNone), Wo161Rules.Judge(false, false, false, 9000, 9000, null));
        Assert.Equal((false, "row-stale"), Wo161Rules.Judge(true, false, false, 9000, 9000, null));
    }

    // ---------------------------------------------------------------- C3 / C4 / C1

    private static ActionRowCatalog.Row Row(int type, int zone, float start, float hit) =>
        new("combat_action_attack", "frag", "tags", zone, 0, type, 1, start, hit);

    [Fact]
    public void A_strike_carries_the_rows_timings_and_attack_fields()
    {
        var s = Wo166Rules.StrikeFor(Row(1, 3, 0.4f, 0.3f))!.Value;
        Assert.Equal((400, 300, (sbyte)1, (sbyte)3, (sbyte)1, 1.0f), (s.StartMs, s.HitMs, s.Type, s.Zone, s.Hand, s.Strength));
        var d = Wo166Rules.StrikeFor(Row(2, -1, 0f, 0f))!.Value;
        Assert.Equal((-1, -1, (sbyte)2), (d.StartMs, d.HitMs, d.Zone));    // no timings: the DLL's default window; zone 2 by default
        Assert.Null(Wo166Rules.StrikeFor(Row(-1, 2, 0.4f, 0.3f)));         // nothing to strike with
        Assert.Null(Wo166Rules.StrikeFor(Row(16, 2, 0.4f, 0.3f)));
    }

    [Fact]
    public void The_swing_hold_ends_with_the_swing()
    {
        Assert.Equal(900, Wo166Rules.SwingHoldMs(700, false));             // mp_snap_fix off: 0.48.0's flat 900 ms
        Assert.Equal(900, Wo166Rules.SwingHoldMs(-1, true));               // no timings: 900
        Assert.Equal(650, Wo166Rules.SwingHoldMs(400, true));              // start+hit 400 + 250
        Assert.Equal(300, Wo166Rules.SwingHoldMs(10, true));               // never below 300
        Assert.Equal(900, Wo166Rules.SwingHoldMs(3200, true));             // never above 900 (a sync attack's 3.2 s)
    }

    [Fact]
    public void The_switch_events_parse_and_refuse_garbage()
    {
        var all = Wo166Rules.ParseCfg("copy_strikes=off snap_fix=on auto_mode=3").ToList();
        Assert.Equal(new[] { ("copy_strikes", "off"), ("snap_fix", "on"), ("auto_mode", "3") }, all);
        Assert.Empty(Wo166Rules.ParseCfg("copy_strikes=maybe auto_mode=x1 auto_mode=123 =on snap_fix="));
        Assert.True(Wo166Rules.DefaultCopyStrikes);
        Assert.True(Wo166Rules.DefaultSnapFix);
        Assert.False(Wo165Rules.DefaultVictimDecides);                     // WO-166: stays off
    }
    // ---------------------------------------------------------------- W1, M1, R1, L2

    [Fact]
    public void The_weather_text_round_trips_and_refuses_garbage()
    {
        Assert.Equal("0.62 semicloudy_clear_B", W164Text.Weather(0.6249f, "semicloudy_clear_B"));
        Assert.Equal("1.00 -", W164Text.Weather(1.7f, null));
        Assert.Equal("0.00 -", W164Text.Weather(float.NaN, "bad name!"));
        Assert.True(W164Text.TryParseWeather("0.62 semicloudy_clear_B", out float r, out string? p));
        Assert.Equal((0.62f, "semicloudy_clear_B"), (r, p));
        Assert.True(W164Text.TryParseWeather("0.10 -", out r, out p));
        Assert.Null(p);
        Assert.False(W164Text.TryParseWeather("1.5 x", out _, out _));
        Assert.False(W164Text.TryParseWeather("0.5", out _, out _));
        Assert.False(W164Text.TryParseWeather("0.5 a;b", out _, out _));
        Assert.Equal("weather", Protocol.W164KindName(Protocol.W164Weather));
        Assert.Equal(5, Protocol.W164Weather);   // appended: kinds 1..4 unchanged
        Assert.Contains("[Info] C_GameProfileManager: Activating profile 'weather_", LogTailGameTransport.Wo144Prefixes);
    }

    [Fact]
    public void A_pin_is_only_for_a_live_partner_and_one_per_partner()
    {
        var names = new Dictionary<byte, string> { [1] = "Partner", [2] = "Partner", [3] = "Other" };
        var seen = new Dictionary<byte, DateTime> { [1] = new DateTime(2026, 1, 1, 0, 0, 0), [2] = new DateTime(2026, 1, 1, 0, 0, 5), [3] = new DateTime(2026, 1, 1) };
        var keep = Wo166Rules.PinOwners(new byte[] { 0, 1, 2, 3 }, 0, g => names.TryGetValue(g, out var n) ? n : null,
                                        g => seen.TryGetValue(g, out var t) ? t : DateTime.MinValue, DateTime.UtcNow);
        Assert.Equal(new byte[] { 2, 3 }, keep.OrderBy(x => x).ToArray());     // the crash-rejoin's old id 1 loses to 2; never myself
        var keep2 = Wo166Rules.PinOwners(new byte[] { 2 }, 0, g => names[g], g => seen[g], DateTime.UtcNow);
        Assert.Equal(new byte[] { 2 }, keep2.ToArray());                       // id 1 not live any more: no pin, whatever its position
    }

    [Fact]
    public void The_amnesty_names_the_recent_strikers()
    {
        var struck = new Dictionary<string, long> { ["ttkc_drozd"] = 100_000, ["ttkc_old"] = 100_000 - 200_000, ["ttkc_guard"] = 150_000 };
        Assert.Equal(new[] { "ttkc_guard", "ttkc_drozd" }, Wo166Rules.RecentStrikers(struck, 160_000));
        Assert.Empty(Wo166Rules.RecentStrikers(struck, 500_000));
    }

    [Fact]
    public void A_friends_presence_says_ready_or_starting()
    {
        Assert.True(SteamApps.TryParsePresence("host;0.48.2", out var st, out var rel));
        Assert.Equal(("ready", "0.48.2"), (st, rel));
        Assert.True(SteamApps.TryParsePresence("starting;0.48.2", out st, out rel));
        Assert.Equal(("starting", "0.48.2"), (st, rel));
        Assert.False(SteamApps.TryParsePresence("playing", out _, out _));
        Assert.False(SteamApps.TryParsePresence(null, out _, out _));
    }
}
