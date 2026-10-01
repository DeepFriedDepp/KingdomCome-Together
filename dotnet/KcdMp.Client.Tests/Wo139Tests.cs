// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-139: crime and guards -- the wire, the texts and their refusals, the host's record,
/// the stop's outcome rules, the crime table (docs/WO-139-findings.md).</summary>
public class Wo139Tests
{
    // ---------------------------------------------------------------- the wire

    [Fact]
    public void The_two_crime_types_ride_the_join_channel_one_way_each()
    {
        var ask = Protocol.JoinWireFor(Protocol.CrimeAskUp)!.Value;
        var host = Protocol.JoinWireFor(Protocol.CrimeHostUp)!.Value;
        Assert.Equal(Protocol.CrimeAskDown, ask.Down);
        Assert.Equal(Protocol.CrimeHostDown, host.Down);
        Assert.Equal(Protocol.JoinFrom.Joiner, ask.From);   // a joiner reports, the host judges
        Assert.Equal(Protocol.JoinFrom.Host, host.From);    // only the host stops, clears, pursues
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.LootFixedLen + 1, ask.Min);
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.LootFixedLen + Protocol.CrimeTextMax, host.Max);
        Assert.True(Protocol.IsJoinDown(Protocol.CrimeHostDown, Protocol.JoinHeaderLen + Protocol.LootFixedLen + 10 + 1));
        // the next free type is after them; nothing else uses 0x64..0x67
        Assert.Equal(2, Protocol.JoinWire.Count(r => r.Up is 0x64 or 0x66));
    }

    [Fact]
    public void A_crime_message_round_trips_through_the_loot_shape()
    {
        string text = Wo139Rules.ReportText(new Wo139Rules.Report("theft", 1766.2f, 1973.8f, 42.3f, "tzel_olbram", "81494400-b654-4aa7-8f31-c95c689db5f6", "world"));
        var pkt = new LootMsg(Protocol.CrimeAskReport, 12, text).BuildUp(Protocol.CrimeAskUp, Protocol.JoinTargetHost);
        Assert.Equal(Protocol.CrimeAskUp, pkt[0]);
        var body = pkt.AsSpan(3 + Protocol.JoinHeaderLen).ToArray();
        Assert.True(LootMsg.TryDecode(body, out var m));
        Assert.Equal(Protocol.CrimeAskReport, m.Kind);
        Assert.Equal(12u, m.Tok);
        Assert.True(Wo139Rules.TryParseReport(m.Text, out var r));
        Assert.Equal("theft", r.Crime);
        Assert.Equal("tzel_olbram", r.Victim);
        Assert.Equal(1766.2f, r.X, 1);
        Assert.Equal("world", r.Where);
    }

    [Fact]
    public void Kind_names_are_stable()
    {
        Assert.Equal("report", Protocol.CrimeAskName(Protocol.CrimeAskReport));
        Assert.Equal("outcome", Protocol.CrimeAskName(Protocol.CrimeAskOutcome));
        Assert.Equal("stop", Protocol.CrimeHostName(Protocol.CrimeHostStop));
        Assert.Equal("horses", Protocol.CrimeHostName(Protocol.CrimeHostHorses));
        Assert.StartsWith("unknown-", Protocol.CrimeHostName(99));
    }

    // ---------------------------------------------------------------- texts: detection reports

    [Theory]
    [InlineData("theft", "world")]
    [InlineData("theft", "stash")]
    [InlineData("lockpick", "door")]
    [InlineData("trespass", "area")]
    [InlineData("horsetheft", "horse")]
    [InlineData("robbody", "body")]
    public void Every_reported_crime_round_trips(string crime, string where)
    {
        var r = new Wo139Rules.Report(crime, -12.5f, 3000.25f, 41.9f, "", "", where);
        Assert.True(Wo139Rules.TryParseReport(Wo139Rules.ReportText(r), out var back));
        Assert.Equal(crime, back.Crime);
        Assert.Equal(where, back.Where);
        Assert.Equal("", back.Victim);
        Assert.Equal("", back.Item);
    }

    [Theory]
    [InlineData("assault 1 2 3 - - body")]          // a violent crime is the host's own detection, never a report
    [InlineData("murder 1 2 3 - - body")]
    [InlineData("theft 1 2 3 - - moon")]             // no such place
    [InlineData("theft 1 2 3 tzel\"x - world")]     // a quote: never reaches a Lua string
    [InlineData("theft 1 2 3 a;b - world")]
    [InlineData("theft 1 2 3 - not-a-guid world")]
    [InlineData("theft 1 2 nan - - world")]
    [InlineData("theft 1 2 1e9 - - world")]
    [InlineData("theft 1 2 3 - - world extra")]
    [InlineData("theft 1 2 3 - -")]
    [InlineData("")]
    public void A_hostile_or_malformed_report_is_refused(string text) =>
        Assert.False(Wo139Rules.TryParseReport(text, out _));

    // ---------------------------------------------------------------- texts: the host's messages

    [Fact]
    public void Judged_stop_pursue_record_cleared_mode_round_trip()
    {
        Assert.True(Wo139Rules.TryParseJudged(Wo139Rules.JudgedText("theft", 3, 1, true, "trosecko_settlements_zelejov"), out var c, out int w, out int g, out bool k, out string s));
        Assert.Equal(("theft", 3, 1, true, "trosecko_settlements_zelejov"), (c, w, g, k, s));
        Assert.True(Wo139Rules.TryParseJudged(Wo139Rules.JudgedText("murder", 0, 0, false, null), out c, out w, out g, out k, out s));
        Assert.Equal(("murder", 0, 0, false, ""), (c, w, g, k, s));

        Assert.True(Wo139Rules.TryParseStop(Wo139Rules.StopText("tzel_man_7", ["theft", "theft", "trespass"], 1250), out string guard, out var crimes, out int fine));
        Assert.Equal("tzel_man_7", guard);
        Assert.Equal(1250, fine);
        Assert.Contains(("theft", 2), crimes);
        Assert.Contains(("trespass", 1), crimes);

        Assert.True(Wo139Rules.TryParsePursue(Wo139Rules.PursueText(true, "tzel_man_4"), out bool on, out string pg));
        Assert.True(on); Assert.Equal("tzel_man_4", pg);

        Assert.True(Wo139Rules.TryParseRecord(Wo139Rules.RecordText(2, ["theft", "assault"], ["trosecko_settlements_zelejov"]), out int open, out var rc, out var sts));
        Assert.Equal(2, open);
        Assert.Equal(2, rc.Count);
        Assert.Equal(["trosecko_settlements_zelejov"], sts);
        Assert.True(Wo139Rules.TryParseRecord(Wo139Rules.RecordText(0, [], []), out open, out rc, out sts));
        Assert.Equal(0, open); Assert.Empty(rc); Assert.Empty(sts);

        Assert.True(Wo139Rules.TryParseCleared(Wo139Rules.ClearedText("paid", "trosecko_settlements_zelejov"), out string why, out string cst));
        Assert.Equal(("paid", "trosecko_settlements_zelejov"), (why, cst));
        Assert.True(Wo139Rules.TryParseMode(Wo139Rules.ModeText(false, "mp_crime_shared-off"), out bool mon, out string mwhy));
        Assert.False(mon); Assert.Equal("mp_crime_shared-off", mwhy);
    }

    [Theory]
    [InlineData("tzel_man_7 - 500")]                  // a stop needs at least one crime
    [InlineData("tzel_man_7 theft:0 500")]
    [InlineData("tzel_man_7 theft:2 -5")]
    [InlineData("tzel\"x theft:1 500")]
    [InlineData("tzel_man_7 witchcraft:1 500")]
    [InlineData("tzel_man_7 theft:1")]
    public void A_malformed_stop_is_refused(string text) =>
        Assert.False(Wo139Rules.TryParseStop(text, out _, out _, out _));

    [Fact]
    public void Outcomes_round_trip_and_only_known_results_pass()
    {
        foreach (var res in Wo139Text.Results)
        {
            Assert.True(Wo139Rules.TryParseOutcome(Wo139Rules.OutcomeText(res, "tzel_man_7", 500), out string r, out string g, out int fine));
            Assert.Equal((res, "tzel_man_7", 500), (r, g, fine));
        }
        Assert.True(Wo139Rules.TryParseOutcome(Wo139Rules.OutcomeText("executed", "", 0), out _, out string none, out _));
        Assert.Equal("", none);
        Assert.False(Wo139Rules.TryParseOutcome("bribed_the_king tzel_man_7 500", out _, out _, out _));
        Assert.False(Wo139Rules.TryParseOutcome("paid tzel_man_7 -1", out _, out _, out _));
    }

    [Fact]
    public void Horse_lists_split_into_parts_that_parse_back_whole()
    {
        var names = Enumerable.Range(0, 120).Select(i => $"tzel_horse_{i}").ToList();
        var texts = Wo139Rules.HorsesTexts(names, maxChars: 300);
        Assert.True(texts.Count > 1);
        var all = new List<string>();
        for (int i = 0; i < texts.Count; i++)
        {
            Assert.True(Wo139Rules.TryParseHorses(texts[i], out int part, out int nparts, out var got));
            Assert.Equal(i + 1, part);
            Assert.Equal(texts.Count, nparts);
            all.AddRange(got);
        }
        Assert.Equal(names.OrderBy(x => x, StringComparer.Ordinal), all.OrderBy(x => x, StringComparer.Ordinal));
        // none: one part with a dash, and the joiner's list empties
        var empty = Wo139Rules.HorsesTexts([]);
        Assert.Single(empty);
        Assert.True(Wo139Rules.TryParseHorses(empty[0], out _, out _, out var e));
        Assert.Empty(e);
        // an unsafe name is dropped on the way out and refused on the way in
        Assert.DoesNotContain(Wo139Rules.HorsesTexts(["a\"b", "ok_horse"]), t => t.Contains('"'));
        Assert.False(Wo139Rules.TryParseHorses("1 1 ok_horse,a;b", out _, out _, out _));
    }

    // ---------------------------------------------------------------- the crime table, settlements

    [Fact]
    public void The_crime_table_is_the_games_own()
    {
        Assert.Equal(500, Wo139Rules.RowOf("theft").Fine);
        Assert.Equal(600, Wo139Rules.RowOf("lockpick").Fine);
        Assert.Equal(250, Wo139Rules.RowOf("trespass").Fine);
        Assert.Equal(2000, Wo139Rules.RowOf("horsetheft").Fine);
        Assert.Equal(1500, Wo139Rules.RowOf("assault").Fine);
        Assert.Equal(20000, Wo139Rules.RowOf("murder").Fine);
        Assert.Equal("theft", Wo139Rules.LabelOf("robbody"));   // looting a body is the game's theft (lootCorpse)
        Assert.Equal("assault", Wo139Rules.LabelOf("knockout"));
        Assert.True(Wo139Rules.IsViolent("assault") && Wo139Rules.IsViolent("murder") && Wo139Rules.IsViolent("knockout"));
        Assert.False(Wo139Rules.IsViolent("theft") || Wo139Rules.IsViolent("trespass") || Wo139Rules.IsViolent("lockpick") || Wo139Rules.IsViolent("horsetheft"));
    }

    [Theory]
    [InlineData("trosecko_settlements_zelejov_soldiers_militia", "trosecko_settlements_zelejov")]
    [InlineData("trosecko_settlements_zelejov_commonFolk_peasants_parcel01", "trosecko_settlements_zelejov")]
    [InlineData("eventNPCs_civilians_friends", "eventNPCs_civilians")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("loner", null)]
    public void The_settlement_is_read_from_the_faction(string? faction, string? settlement) =>
        Assert.Equal(settlement, Wo139Rules.SettlementOf(faction));

    // ---------------------------------------------------------------- the host's record

    private static JoinerCrimeRecord.Crime C(string kind, string st, int wit = 1, int guards = 0, long at = 0) =>
        new() { Kind = kind, Settlement = st, Witnesses = wit, Guards = guards, AtMs = at };

    [Fact]
    public void A_crime_nobody_saw_is_no_crime()
    {
        var rec = new JoinerCrimeRecord();
        Assert.False(rec.Add(C("theft", "z", wit: 0)));
        Assert.Equal(0, rec.Count);
    }

    [Fact]
    public void A_guard_witness_knows_at_once_a_civilians_report_takes_time()
    {
        var rec = new JoinerCrimeRecord();
        Assert.True(rec.Add(C("theft", "z", wit: 2, guards: 1, at: 0)));
        Assert.True(rec.Add(C("trespass", "z", wit: 1, guards: 0, at: 0)));
        Assert.Single(rec.KnownIn("z"));
        Assert.Empty(rec.Tick(JoinerCrimeRecord.ReportDelayMs - 1, 0));
        var became = rec.Tick(JoinerCrimeRecord.ReportDelayMs, 0);
        Assert.Single(became);
        Assert.Equal("trespass", became[0].Kind);
        Assert.Equal(2, rec.KnownIn("z").Count);
    }

    [Fact]
    public void A_theft_is_arrested_a_fresh_assault_is_fought_per_settlement()
    {
        var rec = new JoinerCrimeRecord();
        rec.Add(C("theft", "a", guards: 1, at: 1000));
        rec.Add(C("assault", "b", guards: 1, at: 1000));
        Assert.False(rec.AnyHostileKnownIn("a", 1000));
        Assert.Single(rec.ArrestableIn("a", 1000));
        Assert.True(rec.AnyHostileKnownIn("b", 1000));      // the game's ChooseReaction: attack for a fresh violent crime
        Assert.Empty(rec.ArrestableIn("b", 1000));
    }

    [Fact]
    public void A_violent_crime_is_fresh_for_two_minutes_then_the_guards_arrest_for_it()
    {
        var rec = new JoinerCrimeRecord();
        rec.Add(C("murder", "b", guards: 0, at: 0));
        Assert.False(rec.AnyHostileKnownIn("b", 10), "not known yet: nobody attacks");
        rec.Tick(JoinerCrimeRecord.ReportDelayMs, 0);                         // a witness told the guards: fresh from now
        Assert.True(rec.AnyHostileKnownIn("b", JoinerCrimeRecord.ReportDelayMs + JoinerCrimeRecord.FreshMs - 1));
        long later = JoinerCrimeRecord.ReportDelayMs + JoinerCrimeRecord.FreshMs;
        Assert.False(rec.AnyHostileKnownIn("b", later));
        Assert.Equal("murder", Assert.Single(rec.ArrestableIn("b", later)).Kind);   // a stop, the violent crime in it
    }

    [Fact]
    public void A_resist_escalates_for_the_games_five_minutes_and_calm_ends_a_fight()
    {
        var rec = new JoinerCrimeRecord();
        rec.Add(C("theft", "a", guards: 1, at: 0));
        rec.MarkResisted("a", 5000);                                          // he fled a stop
        Assert.True(rec.AnyHostileKnownIn("a", 5000 + JoinerCrimeRecord.EscalationMs - 1));
        Assert.Empty(rec.ArrestableIn("a", 5000));
        Assert.False(rec.AnyHostileKnownIn("a", 5000 + JoinerCrimeRecord.EscalationMs));
        Assert.Single(rec.ArrestableIn("a", 5000 + JoinerCrimeRecord.EscalationMs));
        // a fight over a fresh assault ends when he goes down or dies: the record stands, arrested next time
        rec.Add(C("assault", "a", guards: 1, at: 6000));
        rec.MarkResisted("a", 6000);
        Assert.True(rec.AnyHostileKnownIn("a", 6000));
        rec.Calm(null);
        Assert.False(rec.AnyHostileKnownIn("a", 6000));
        Assert.Equal(2, rec.ArrestableIn("a", 6000).Count);
        Assert.Equal(2, rec.Count);
        // a new resist after the calm escalates again
        rec.MarkResisted("a", 7000);
        Assert.True(rec.AnyHostileKnownIn("a", 7000));
    }

    [Fact]
    public void A_trespass_is_one_crime_per_settlement_however_often_it_is_re_reported()
    {
        var rec = new JoinerCrimeRecord();
        Assert.True(rec.Add(C("trespass", "z", wit: 1, guards: 0, at: 0)));
        Assert.True(rec.Add(C("trespass", "z", wit: 2, guards: 0, at: 8000)));    // the joiner's re-report while inside
        Assert.Equal(1, rec.Count);
        Assert.Empty(rec.KnownIn("z"));
        Assert.True(rec.Add(C("trespass", "z", wit: 1, guards: 1, at: 16000)));   // a guard walked in on him
        Assert.Equal(1, rec.Count);
        var t = Assert.Single(rec.KnownIn("z"));
        Assert.Equal(2, t.Witnesses);
        Assert.Equal(16000, t.KnownAtMs);
        Assert.Equal(250, JoinerCrimeRecord.FineOf(rec.KnownIn("z")));             // one trespass fine, not three
        Assert.True(rec.Add(C("trespass", "other", wit: 1, at: 0)));               // another settlement's house: its own
        Assert.True(rec.Add(C("theft", "z", wit: 1, at: 0)));                      // other crimes are never merged
        Assert.Equal(3, rec.Count);
    }

    [Fact]
    public void A_resolution_clears_one_settlement_an_execution_clears_all()
    {
        var rec = new JoinerCrimeRecord();
        rec.Add(C("theft", "a", guards: 1));
        rec.Add(C("theft", "a", guards: 1));
        rec.Add(C("trespass", "b", guards: 1));
        Assert.Equal(2, rec.Clear("a"));
        Assert.Equal(1, rec.Count);
        Assert.Equal(["b"], rec.Settlements);
        Assert.Equal(1, rec.Clear(null));
        Assert.Equal(0, rec.Count);
    }

    [Fact]
    public void Crimes_expire_after_the_games_own_expiration()
    {
        var rec = new JoinerCrimeRecord();
        var c = C("trespass", "a", guards: 1);
        c.AtWorldS = 1000;
        rec.Add(c);
        rec.Tick(0, 1000 + 2 * 86400 - 1);   // trespass: 2 days
        Assert.Equal(1, rec.Count);
        rec.Tick(0, 1000 + 2 * 86400 + 1);
        Assert.Equal(0, rec.Count);
    }

    [Fact]
    public void The_stop_fine_is_the_tables_sum_and_the_record_is_bounded()
    {
        Assert.Equal(500 + 250 + 600, JoinerCrimeRecord.FineOf([C("theft", "a"), C("trespass", "a"), C("lockpick", "a")]));
        var rec = new JoinerCrimeRecord();
        for (int i = 0; i < 250; i++) rec.Add(C("theft", "a", guards: 1));
        Assert.Equal(200, rec.Count);
    }

    [Theory]
    [InlineData("paid", Wo139Rules.OutcomeEffect.Clear)]
    [InlineData("punished", Wo139Rules.OutcomeEffect.Clear)]
    [InlineData("persuaded", Wo139Rules.OutcomeEffect.Clear)]
    [InlineData("executed", Wo139Rules.OutcomeEffect.Clear)]
    [InlineData("bribed", Wo139Rules.OutcomeEffect.Clear)]
    [InlineData("fought", Wo139Rules.OutcomeEffect.Resist)]
    [InlineData("fled", Wo139Rules.OutcomeEffect.Resist)]
    [InlineData("talked", Wo139Rules.OutcomeEffect.Keep)]
    [InlineData("nostop", Wo139Rules.OutcomeEffect.Keep)]
    [InlineData("refused", Wo139Rules.OutcomeEffect.Keep)]
    public void A_stops_outcome_does_what_the_game_would(string result, Wo139Rules.OutcomeEffect effect) =>
        Assert.Equal(effect, Wo139Rules.EffectOf(result));
}
