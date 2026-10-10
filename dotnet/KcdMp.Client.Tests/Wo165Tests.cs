// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Text;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-165 (docs/WO-165-findings.md): the victim-decides replay's rules (C2: preconditions, the engine's answer, who applies the blow --
/// never twice, never dropped), the attacker's recoil (C3: the failed-attack row by weapon tags), the switches and the wire values
/// (HitVerdict PerfectBlock / Broken, the W164 HitOutcome text). The native half is native/tests/wo165_tests.cpp.
/// </summary>
public class Wo165Tests
{
    private static HitVerdictMsg V(string npc = "ttkc_man_3", byte flags = Protocol.HitFlagSwingKnown, HitVerdict v = HitVerdict.Hit) =>
        new(7, v, npc.Length == 0 ? Protocol.HitFlagNoAttacker : flags, 0, 4, 12f, 0f, npc);

    // ---------------------------------------------------------------- C2 preconditions

    [Fact]
    public void Every_precondition_names_its_reason_in_order()
    {
        Assert.Equal("off", Wo165Rules.ReplayRefusal(false, V(), true, true, true, false));
        Assert.Equal("missile", Wo165Rules.ReplayRefusal(true, V(flags: Protocol.HitFlagSwingKnown | Protocol.HitFlagMissile), true, true, true, false));
        Assert.Equal("no-attacker", Wo165Rules.ReplayRefusal(true, V(npc: ""), true, true, true, false));
        Assert.Equal("no-copy", Wo165Rules.ReplayRefusal(true, V(), false, true, true, false));
        Assert.Equal("copy-not-engaged", Wo165Rules.ReplayRefusal(true, V(), true, false, true, false));
        Assert.Equal("no-row", Wo165Rules.ReplayRefusal(true, V(), true, true, false, false));
        Assert.Equal("player-down", Wo165Rules.ReplayRefusal(true, V(), true, true, true, true));
        Assert.Null(Wo165Rules.ReplayRefusal(true, V(), true, true, true, false));
    }

    [Fact]
    public void The_switches_default_on()
    {
        // all on (the maintainer: testing needs a real partner -- new pieces ship on, each with its switch)
        Assert.True(Wo165Rules.DefaultHostLock);
        Assert.True(Wo165Rules.DefaultVictimDecides);
        Assert.True(Wo165Rules.DefaultBlockRecoil);
        Assert.Equal(0x11, Wo165Rules.ReplayFlags);     // skip the repeat filter + refer a blocking victim (P3) to the host's verdict
    }

    // ---------------------------------------------------------------- the DLL's answer

    [Theory]
    [InlineData("seq=12 engine=hit returned=1 core=ran blk=0 pb=0", 12u, "hit", "")]
    [InlineData("seq=3 engine=blocked returned=1 core=ran", 3u, "blocked", "")]
    [InlineData("refused=no-attacker-combat-actor", 0u, "", "no-attacker-combat-actor")]
    [InlineData("refused=switched-off", 0u, "", "switched-off")]
    [InlineData("", 0u, "", "no-answer")]
    [InlineData("garbage", 0u, "", "unreadable-answer")]
    public void The_replay_answer_parses(string text, uint seq, string engine, string refused)
    {
        bool ok = Wo165Rules.TryParseReplay(text, out uint s, out string e, out string r);
        Assert.Equal(refused.Length == 0, ok);
        Assert.Equal(seq, s);
        Assert.Equal(engine, e);
        Assert.Equal(refused, r);
    }

    [Theory]
    [InlineData("hit", HitVerdict.Hit)]
    [InlineData("blocked", HitVerdict.Blocked)]
    [InlineData("pb", HitVerdict.PerfectBlock)]
    [InlineData("broken", HitVerdict.Broken)]
    [InlineData("filtered", HitVerdict.None)]
    [InlineData("none", HitVerdict.None)]
    public void The_engine_word_is_a_verdict_or_nothing(string w, HitVerdict v) => Assert.Equal(v, Wo165Rules.Outcome(w));

    // ---------------------------------------------------------------- who applies the blow: once, never dropped

    [Theory]
    // the live P3 numbers: the engine applied the hit itself (hp -16.73 st -40)
    [InlineData(HitVerdict.Hit, 2, 16.73f, 40f, Wo165Rules.DamageCall.Engine)]
    [InlineData(HitVerdict.Blocked, 2, 0f, 18f, Wo165Rules.DamageCall.Engine)]
    [InlineData(HitVerdict.PerfectBlock, 2, 0f, 0f, Wo165Rules.DamageCall.Engine)]     // a perfect block costs nothing: not a dropped blow
    [InlineData(HitVerdict.Blocked, 2, 0f, 0f, Wo165Rules.DamageCall.Engine)]
    // a hit the engine applied NOTHING for (P3's low strengths): the host's verdict is applied instead, once
    [InlineData(HitVerdict.Hit, 2, 0f, 0f, Wo165Rules.DamageCall.FallbackApplyHost)]
    [InlineData(HitVerdict.Broken, 2, 0f, 0f, Wo165Rules.DamageCall.FallbackApplyHost)]
    // the measure never came: a hit falls back (no damage dropped); a block keeps the engine's word
    [InlineData(HitVerdict.Hit, 0, 0f, 0f, Wo165Rules.DamageCall.FallbackApplyHost)]
    [InlineData(HitVerdict.Hit, 1, 0f, 0f, Wo165Rules.DamageCall.FallbackApplyHost)]
    [InlineData(HitVerdict.Blocked, 1, 0f, 0f, Wo165Rules.DamageCall.Engine)]
    // merged into an earlier replay's measure on the same victim: applied by the engine, never applied again
    [InlineData(HitVerdict.Hit, 3, 0f, 0f, Wo165Rules.DamageCall.EngineMerged)]
    public void The_blow_is_applied_by_exactly_one_side(HitVerdict o, byte state, float hp, float st, Wo165Rules.DamageCall want) =>
        Assert.Equal(want, Wo165Rules.Decide(o, state, hp, st));

    [Fact]
    public void No_outcome_both_applies_the_engine_and_the_host()
    {
        // every (outcome, state, damage) combination yields ONE caller: Engine/EngineMerged (the DLL measured or merged it) xor the host
        foreach (var o in new[] { HitVerdict.Hit, HitVerdict.Blocked, HitVerdict.PerfectBlock, HitVerdict.Broken })
            foreach (byte s in new byte[] { 0, 1, 2, 3 })
                foreach (float hp in new[] { 0f, 5f })
                {
                    var c = Wo165Rules.Decide(o, s, hp, 0f);
                    bool engine = c is Wo165Rules.DamageCall.Engine or Wo165Rules.DamageCall.EngineMerged;
                    bool host = c == Wo165Rules.DamageCall.FallbackApplyHost;
                    Assert.True(engine ^ host);
                    if (s == 2 && hp > 0) Assert.True(engine);   // the engine measurably applied damage: the host's never follows
                }
    }

    // ---------------------------------------------------------------- C3

    [Theory]
    [InlineData(HitVerdict.Blocked, true, false)]
    [InlineData(HitVerdict.PerfectBlock, true, true)]
    [InlineData(HitVerdict.Hit, false, false)]
    [InlineData(HitVerdict.Broken, false, false)]   // a broken block staggers the victim; the attacker has no row (WO-162 Q4)
    public void Only_a_block_recoils(HitVerdict v, bool recoils, bool perfect)
    {
        Assert.Equal(recoils, Wo165Rules.Recoils(v, out bool p));
        Assert.Equal(perfect, p);
    }

    [Fact]
    public void Weapon_tags_are_the_l_and_r_tags()
    {
        Assert.Equal(new[] { "l_longsword", "r_longsword" }, Wo165Rules.WeaponTags("l_longsword+r_longsword+aZ5+oppMale"));
        Assert.Equal(new[] { "r_shortSwords", "l_noShield" }, Wo165Rules.WeaponTags("aZ5+r_shortSwords+l_noShield+oppMale"));
        Assert.Empty(Wo165Rules.WeaponTags("aZ5+oppMale"));
    }

    private static string SyntheticPak(params (string guid, int action, string tags)[] rows)
    {
        string path = Path.Combine(Path.GetTempPath(), $"wo165-{Guid.NewGuid():N}.pak");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("Libs/Tables/combat/combat_action_failed_attack.xml");
            using var w = new StreamWriter(e.Open(), Encoding.UTF8);
            w.Write("<database><table>");
            foreach (var (g, a, t) in rows)
                w.Write($"<combat_action_failed_attack action_type_id=\"{a}\" mn_fragment_guid=\"{g}\" mn_fragment_id=\"{(a == 15 ? "CombatAttackFailedPB" : "CombatAttackFailed")}\" mn_tags=\"{t}\" />");
            w.Write("</table></database>");
        }
        return path;
    }

    private static ActionRowCatalog.Row Swing(string tags) => new("combat_action_attack", "CombatAttack", tags, 2, 1, 1, 26);

    [Fact]
    public void The_recoil_row_is_the_one_whose_weapons_the_swing_holds()
    {
        string pak = SyntheticPak(
            ("00000000-0000-0000-0000-000000000001", 27, "l_halberd+r_halberd+aZ5+oppMale"),
            ("00000000-0000-0000-0000-000000000002", 27, "l_longsword+r_longsword+aZ5+oppMale"),
            ("00000000-0000-0000-0000-000000000003", 27, "aZ5+r_shortSwords+l_noShield+oppMale"),
            ("00000000-0000-0000-0000-000000000004", 27, "l_shield+aZ5+r_swords+oppMale"),
            ("00000000-0000-0000-0000-000000000005", 15, "l_longsword+r_longsword+aZ5+oppMale"));
        try
        {
            var cat = ActionRowCatalog.LoadFrom(pak);
            Assert.Equal("CombatAttackFailed, l_longsword+r_longsword+aZ5+oppMale", cat.FailedAttackRow(Swing("l_longsword+r_longsword+sZ2+attack_heavy"), false)!.Value.Spec);
            Assert.Equal("CombatAttackFailedPB, l_longsword+r_longsword+aZ5+oppMale", cat.FailedAttackRow(Swing("l_longsword+r_longsword+sZ2"), true)!.Value.Spec);
            Assert.Equal("CombatAttackFailed, aZ5+r_shortSwords+l_noShield+oppMale", cat.FailedAttackRow(Swing("r_shortSwords+l_noShield+aZ1"), false)!.Value.Spec);
            Assert.Null(cat.FailedAttackRow(Swing("l_noweapon+r_noweapon+punch"), false));   // unarmed: no recoil exists in the game's tables
            Assert.Null(cat.FailedAttackRow(Swing("r_shortSwords+l_noShield"), true));       // no PB row for short swords in this pak
            Assert.Null(cat.FailedAttackRow(Swing("r_swords+aZ1"), false));                  // the shield row needs the shield too
        }
        finally { File.Delete(pak); }
    }

    [Fact]
    public void The_installed_tables_give_a_recoil_for_the_four_weapon_sets_only()
    {
        string? p = WeaponSwingCatalog.FindTablesPak();
        if (p is null) return;   // no install on this machine
        var cat = ActionRowCatalog.LoadFrom(p);
        foreach (var w in new[] { "l_halberd+r_halberd", "l_longsword+r_longsword", "r_shortSwords+l_noShield", "l_shield+r_swords" })
        {
            Assert.NotNull(cat.FailedAttackRow(Swing(w + "+aZ2"), false));
            Assert.NotNull(cat.FailedAttackRow(Swing(w + "+aZ2"), true));
        }
        Assert.Null(cat.FailedAttackRow(Swing("l_noweapon+r_noweapon+freeGuard+punch+attack_heavy"), false));
    }

    // ---------------------------------------------------------------- the switches

    [Fact]
    public void The_cfg_event_names_only_the_three_switches()
    {
        var d = Wo165Rules.ParseCfg("host_lock=off victim_decides=on block_recoil=maybe other=on junk");
        Assert.Equal(2, d.Count);
        Assert.False(d["host_lock"]);
        Assert.True(d["victim_decides"]);
        Assert.Empty(Wo165Rules.ParseCfg(null));
    }

    // ---------------------------------------------------------------- the wire

    [Theory]
    [InlineData(HitVerdict.PerfectBlock, "pb")]
    [InlineData(HitVerdict.Broken, "broken")]
    public void The_new_verdict_values_round_trip(HitVerdict v, string name)
    {
        var pkt = new HitVerdictMsg(9, v, Protocol.HitFlagSwingKnown, 0, 2, 0f, 7f, "ttkc_man_3").BuildUp(1);
        Assert.True(HitVerdictMsg.TryDecode(9, pkt.AsSpan(3 + 5), out var m));
        Assert.Equal(v, m.Verdict);
        Assert.Equal(name, HitVerdictMsg.VerdictName(v));
        Assert.True(HitVerdictMsg.TryParseVerdict(name, out var back) && back == v);
    }

    [Fact]
    public void A_verdict_past_the_last_value_is_still_refused()
    {
        var pkt = new HitVerdictMsg(9, HitVerdict.Hit, Protocol.HitFlagSwingKnown, 0, 2, 5f, 0f, "ttkc_man_3").BuildUp(1);
        var body = pkt.AsSpan(3 + 5).ToArray();
        body[1] = 7;
        Assert.False(HitVerdictMsg.TryDecode(9, body, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HitVerdictMsg(9, (HitVerdict)7, 0, 0, 0, 1f, 0f, "a").BuildUp(1));
    }

    [Fact]
    public void The_outcome_text_round_trips_and_refuses_the_rest()
    {
        string t = W164Text.HitOutcome(31, HitVerdict.Blocked, 0f, 18f);
        Assert.Equal("31 blocked 0.0 18.0", t);
        Assert.True(W164Text.TryParseHitOutcome(t, out uint hid, out var v, out float hp, out float st));
        Assert.Equal((31u, HitVerdict.Blocked, 0f, 18f), (hid, v, hp, st));
        Assert.Equal("4 hit -1.0 -1.0", W164Text.HitOutcome(4, HitVerdict.Hit, float.NaN, -3f));   // not separable / unknown: -1
        Assert.True(W164Text.TryParseHitOutcome("4 hit -1.0 -1.0", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseHitOutcome("4 hit -2 0", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseHitOutcome("4 hit 5000 0", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseHitOutcome("4 missed 0 0", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseHitOutcome("x hit 0 0", out _, out _, out _, out _));
        Assert.False(W164Text.TryParseHitOutcome("4 hit 0", out _, out _, out _, out _));
        Assert.Equal("hit-outcome", Protocol.W164KindName(Protocol.W164HitOutcome));
    }

    [Fact]
    public void The_stats_line_counts_agreement()
    {
        var s = new Wo165Stats();
        Assert.True(s.Quiet);
        s.Replay(HitVerdict.Blocked, Wo165Rules.DamageCall.Engine, HitVerdict.Blocked);
        s.Replay(HitVerdict.Hit, Wo165Rules.DamageCall.Engine, HitVerdict.Blocked);
        s.Fallback("no-row");
        string l = s.Line();
        Assert.Contains("replays=2", l);
        Assert.Contains("agree_host=1", l);
        Assert.Contains("disagree_host=1", l);
        Assert.Contains("not_replayed=[no-row:1]", l);
    }
}
