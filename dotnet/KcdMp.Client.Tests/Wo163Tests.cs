// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-163 Stage A (docs/WO-163-findings.md): the sync-attack capture (A1), master strikes as swings (A2), the hit-to-swing
/// pairing by the row's own timing (A3), the generic swing (A4). The design source is research/WO-162/combat-RE.md.
/// The field set is the 45 host-forwarded NPC blows on avatars of the two sessions WO-162 counted, each with the age of every
/// swing row its NPC had sent in the 4 s before it and that row's own start+hit lag from the game's table.
/// </summary>
public class Wo163Tests
{
    private sealed record FieldHit(int[] Ages, int[] Lags, string Old, string New);

    // ages: ms from each swing's row to the hit (oldest first); lags: the row's attack_time_to_start + attack_time_to_hit in ms
    // (-2 = a row no table knows -- the sync attacks the 0.46.5 capture could not read; -1 = the table states no lag)
    private static readonly FieldHit[] Field =
    [
        new([1123, 250], [1069, 1089], "paired-1.2s", "paired-lag"),
        new([1968, 1095, 310], [1069, 1089, 1790], "paired-1.2s", "paired-lag"),
        new([3008, 2135, 1350], [1069, 1089, 1790], "no-swing-captured", "swing-no-timing-fit"),
        new([3353, 2568, 979], [1089, 1790, 1447], "paired-1.2s", "swing-no-timing-fit"),
        new([1314, 396], [1099, 1465], "paired-1.2s", "paired-lag"),
        new([2186, 1268], [1099, 1465], "no-swing-captured", "paired-lag"),
        new([3191, 2273, 979], [1099, 1465, 1142], "paired-1.2s", "paired-lag"),
        new([3817, 2523, 1525], [1465, 1142, 1447], "no-swing-captured", "paired-lag"),
        new([3855, 2857, 1287, 449], [1142, 1447, 1510, 1427], "paired-1.2s", "paired-lag"),
        new([3674, 2104, 1266, 419], [1447, 1510, 1427, 1427], "paired-1.2s", "paired-lag"),
        new([2941, 2103, 1256, 419], [1510, 1427, 1427, 1790], "paired-1.2s", "paired-lag"),
        new([3245, 2398, 1561], [1427, 1427, 1790], "no-swing-captured", "paired-lag"),
        new([1191], [1099], "paired-1.2s", "paired-lag"),
        new([1197, 174], [1033, 1424], "paired-1.2s", "paired-lag"),
        new([2285, 1262, 69], [1033, 1424, -2], "paired-1.2s(unreadable-row)", "paired-lag"),
        new([1125, 262], [1069, 1417], "paired-1.2s", "paired-lag"),
        new([2140, 1277, 382], [1069, 1417, 1417], "paired-1.2s", "paired-lag"),
        new([3139, 2276, 1381, 515], [1069, 1417, 1417, 1417], "paired-1.2s", "paired-lag"),
        new([3856, 2993, 2098, 1232], [1069, 1417, 1417, 1417], "no-swing-captured", "paired-lag"),
        new([2720], [-2], "no-swing-captured", "sync-unreadable"),
        new([1238, 372], [1069, 1417], "paired-1.2s", "paired-lag"),
        new([2212, 1346, 459], [1069, 1417, 1089], "paired-1.2s", "paired-lag"),
        new([2853, 1987, 1100, 299], [1069, 1417, 1089, -2], "paired-1.2s(unreadable-row)", "paired-lag"),
        new([3894, 3007, 2206], [1417, 1089, -2], "no-swing-captured", "swing-no-timing-fit"),
        new([], [], "no-swing-captured", "no-row"),
        new([1465, 295], [1166, 1439], "paired-1.2s", "paired-lag"),
        new([1276, 298], [1166, 1161], "paired-1.2s", "paired-lag"),
        new([2275, 1297, 296], [1166, 1161, -2], "paired-1.2s(unreadable-row)", "paired-lag"),
        new([2984, 2006, 1005], [1166, 1161, -2], "paired-1.2s(unreadable-row)", "swing-no-timing-fit"),
        new([1175, 337], [895, 1545], "paired-1.2s", "paired-lag"),
        new([2225, 1387, 164], [895, 1545, 1184], "paired-1.2s", "paired-lag"),
        new([3214, 2376, 1153, 149], [895, 1545, 1184, 870], "paired-1.2s", "paired-lag"),
        new([1282, 298], [1166, 1161], "paired-1.2s", "paired-lag"),
        new([2272, 1288, 281], [1166, 1161, 1168], "paired-1.2s", "paired-lag"),
        new([3254, 2270, 1263], [1166, 1161, 1168], "no-swing-captured", "paired-lag"),
        new([1231, 431], [1045, 1229], "paired-1.2s", "paired-lag"),
        new([2114, 1314, 108], [1045, 1229, 1213], "paired-1.2s", "paired-lag"),
        new([3399, 2599, 1393, 141], [1045, 1229, 1213, 1180], "paired-1.2s", "paired-lag"),
        new([3826, 2620, 1368], [1229, 1213, 1180], "no-swing-captured", "paired-lag"),
        new([], [], "no-swing-captured", "no-row"),
        new([], [], "no-swing-captured", "no-row"),
        new([], [], "no-swing-captured", "no-row"),
        new([], [], "no-swing-captured", "no-row"),
        new([], [], "no-swing-captured", "no-row"),
        new([], [], "no-swing-captured", "no-row"),
    ];

    private static (Wo161SwingLedger.Pairing Pair, bool OldPaired) Run(FieldHit h)
    {
        const long now = 100_000;
        var l = new Wo161SwingLedger();
        for (int i = 0; i < h.Ages.Length; i++) l.Record("npc", Guid.NewGuid(), now - h.Ages[i], h.Lags[i]);
        return (l.Match("npc", now), h.Ages.Length > 0 && h.Ages.Min() <= Wo161Rules.SwingWindowMs);   // the 0.46.5 rule: the newest swing within 1.2 s
    }

    [Fact]
    public void The_field_set_pairs_33_of_45_blows_by_the_rows_own_lag_and_names_the_rest()
    {
        int paired = 0, unmatched = 0, none = 0, oldPaired = 0;
        foreach (var h in Field)
        {
            var (p, old) = Run(h);
            if (old) oldPaired++;
            if (p.Swing is { } s) { paired++; Assert.True(p.ErrMs <= Wo161Rules.PairToleranceMs, "a pairing is only made inside the tolerance"); Assert.True(s.LagMs >= 0); }
            else if (p.Why == Wo161Rules.ReasonUnmatched) unmatched++;
            else { none++; Assert.Equal(Wo161Rules.ReasonNone, p.Why); Assert.Empty(h.Ages.Where(a => a <= Wo161Rules.LookbackMs)); }   // no-swing-captured only with no row at all
            // the offline analysis labelled each blow the same way
            Assert.Equal(h.New == "paired-lag" ? "-" : h.New == "no-row" ? Wo161Rules.ReasonNone : Wo161Rules.ReasonUnmatched, p.Swing is not null ? "-" : p.Why);
        }
        Assert.Equal(45, Field.Length);
        Assert.Equal((33, 5, 7), (paired, unmatched, none));    // WO-162 Q3.2: 33 of the 35 hits with a catalogued row fit
        Assert.Equal(29, oldPaired);                             // the 0.46.5 window paired 29 (4 of them to an unreadable sync row), left 16 with no swing
        Assert.Equal(45 - 29, Field.Length - oldPaired);
    }

    [Fact]
    public void A_blow_belongs_to_the_swing_whose_own_lag_it_matches_not_to_the_newest()
    {
        var l = new Wo161SwingLedger();
        Guid slow = Guid.NewGuid(), quick = Guid.NewGuid();
        l.Record("g", slow, 10_000, 1500);     // lands 1.5 s after it starts
        l.Record("g", quick, 10_900, 1100);    // a follow-up swing, lands 1.1 s after ITS start (at 12 000)
        Assert.Equal(slow, l.Match("g", 11_500).Swing!.Value.Row);    // 1.5 s after the first, 0.6 s after the second (not 1.1): the first
        Assert.Equal(quick, l.Match("g", 12_000).Swing!.Value.Row);   // 2.0 s after the first (0.5 s off), 1.1 s after the second (0 off): the second
        var none = l.Match("g", 13_900);                              // 3.9 s / 3.0 s old: both far from their lags
        Assert.Null(none.Swing); Assert.Equal(Wo161Rules.ReasonUnmatched, none.Why);
        Assert.Equal(Wo161Rules.ReasonNone, l.Match("g", 14_901).Why); // beyond the look-back: no row at all
    }

    [Fact]
    public void The_best_fit_wins_inside_the_tolerance_and_an_unreadable_row_is_never_paired()
    {
        var l = new Wo161SwingLedger();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), unk = Guid.NewGuid();
        l.Record("n", a, 1_000, 1000);          // hit at 2 300: age 1 300, lag 1 000: 300 off
        l.Record("n", b, 1_200, 1000);          // age 1 100, lag 1 000: 100 off
        l.Record("n", unk, 1_300, Wo161Rules.LagUnknownRow);   // age 1 000: could be anything, so it is nothing
        var m = l.Match("n", 2_300);
        Assert.Equal(b, m.Swing!.Value.Row); Assert.Equal(100, m.ErrMs);
        var only = new Wo161SwingLedger();
        only.Record("n", unk, 1_000, Wo161Rules.LagUnknownRow);
        var u = only.Match("n", 1_300);
        Assert.Null(u.Swing); Assert.Equal(Wo161Rules.ReasonUnmatched, u.Why);   // rows exist, none can be matched: not "no swing captured"
    }

    [Fact]
    public void A_row_that_states_no_lag_keeps_the_old_window_and_a_clock_that_ran_backwards_matches_nothing()
    {
        var l = new Wo161SwingLedger();
        l.Record("n", Guid.NewGuid(), 5_000, Wo161Rules.LagNone);
        Assert.NotNull(l.Match("n", 5_000 + Wo161Rules.SwingWindowMs).Swing);
        Assert.Null(l.Match("n", 5_000 + Wo161Rules.SwingWindowMs + 1).Swing);
        Assert.Null(l.Match("n", 4_999).Swing);
    }

    [Fact]
    public void The_victim_judges_a_verdict_against_the_played_row_its_lag_fits()
    {
        // a sync attack's row played 2.9 s before its blow's verdict: the 0.46.5 bound (1.5 s) calls that stale, the fit shows it
        var log = new Wo161PlayedLog();
        long now = 50_000;
        log.Note(now - 2_900, 2_900);
        log.Note(now - 200, 1_100);                      // a later swing, not due yet
        var best = log.Best(now)!.Value;
        Assert.Equal((2_900L, true), (best.Ago, best.Fits));
        Assert.Equal((true, "-"), Wo161Rules.Judge(false, false, false, best.Ago, best.Ago, null, best.Fits));
        Assert.Equal((false, "row-stale"), Wo161Rules.Judge(false, false, false, best.Ago, best.Ago, null, false));   // without the fit it was stale
        // nothing fits: the newest played row is the answer the old window judges
        var late = new Wo161PlayedLog();
        late.Note(now - 4_000, 800);
        Assert.Equal((4_000L, false), (late.Best(now)!.Value.Ago, late.Best(now)!.Value.Fits));
        Assert.Null(new Wo161PlayedLog().Best(now));
        Assert.Null(Wo161Rules.BestPlayedAgo([]));
    }

    // ------------------------------------------------------------------------------------------------------------ A1

    // (the row's GUID read at descriptor +0x7C, what the legacy read at +0x84 held) of the distinct sync-attack row dumps in the field logs
    private static readonly (string At7C, string At84)[] SyncDumps =
    [
        ("5810bf9a45e9e838ad87b5471c939c53", "ad87b5471c939c539a99d93f01010064"),
        ("afbd33aab7ca8e38a33c6778b9162de9", "a33c6778b9162de90e11d13f01010000"),
        ("23ab84478aea1e39913e80961260fe4c", "913e80961260fe4c4144c43f0101006d"),
        ("320014ad3f484332bd7e76caa14032c6", "bd7e76caa14032c6dfdd1d4001010000"),
        ("1dfd7af57da31e35a6b77509f8220ad1", "a6b77509f8220ad1babb3b4001010069"),
        ("f2a2b4f9b983d43b92a26de00478fe19", "92a26de00478fe19cdcccc3f0101006c"),
        ("e40f94f0c323e832b2578794acac3d0b", "b2578794acac3d0b0e11d13f01010061"),
        ("32b883c70d9fc436b593173b99fa39c9", "b593173b99fa39c96666064001010067"),
        ("90dbf5c661b73b3cba625c45617aef32", "ba625c45617aef328788684001010072"),
        ("040ddc21934415948641a19334ed0f03", "8641a19334ed0f03333333400101002e"),
        ("d1a77e19e0c0674365d0315a4f8c598e", "65d0315a4f8c598edfdd3d4001010074"),
        ("193a05576f55ca35886ec90ca9e6c585", "886ec90ca9e6c5856666663f01010070"),
        ("0cd58220f2c2723e8304cd327636b41d", "8304cd327636b41dbabb3b4001010020"),
        ("4a782eece223a33c87d435fe8f78d731", "87d435fe8f78d731acaa0a400101006f"),
        ("d85b823ce571a232b46494c37aea46a5", "b46494c37aea46a5acaa0a4001010000"),
        ("a95fc0722169ee36bbd18647478dfc21", "bbd18647478dfc219a99993f0101005f"),
        ("e44f88432dcc233715c507e05a4bfc87", "15c507e05a4bfc87babb1b4001010000"),
        ("d99c851062763b3ca7ae63d38d11ad3c", "a7ae63d38d11ad3c6666e63f01010038"),
        ("acfe450b76ad70399c3d7e54f4bfb9a0", "9c3d7e54f4bfb9a08788484001010070"),
        ("cbd9a49c50a6bc399bf0ce4bb7c5fe20", "9bf0ce4bb7c5fe20878848400101006e"),
        ("ccd184adbbaf1d0593d6f7ffb4924784", "93d6f7ffb4924784acaa2a4001010072"),
        ("5d45265912af1f348fd09209ff604d09", "8fd09209ff604d094644644001010022"),
        ("096120ac4a7dac3f8829c0d41c42c47d", "8829c0d41c42c47dcdcccc3f01010033"),
        ("800fb141336d3e3195c75eb0cc9dfb0c", "95c75eb0cc9dfb0c6666064001010033"),
    ];

    [Fact]
    public void The_legacy_read_straddles_the_guid_which_sits_at_0x7C()
    {
        Assert.Equal(24, SyncDumps.Length);
        foreach (var (g7c, g84) in SyncDumps)
        {
            var a = Convert.FromHexString(g7c); var b = Convert.FromHexString(g84);
            // +0x84 starts 8 bytes into the GUID at +0x7C: the legacy read returned half a GUID and 8 bytes of the next field
            Assert.Equal(a[8..], b[..8]);
        }
        Assert.Equal(24, SyncDumps.Select(d => d.At7C).Distinct().Count());
    }

    private static ActionRowCatalog? RealCatalog()
    {
        string? pak = WeaponSwingCatalog.FindTablesPak();
        return pak is null ? null : ActionRowCatalog.LoadFrom(pak);   // no install on this machine: the pak-bound checks are skipped
    }

    [Fact]
    public void Every_field_sync_dump_resolves_at_0x7C_in_the_sync_table_and_none_at_0x84()
    {
        var cat = RealCatalog();
        if (cat is null) return;
        foreach (var (g7c, g84) in SyncDumps)
        {
            var good = new Guid(Convert.FromHexString(g7c));
            Assert.True(cat.TryGet(good, out var row), "the +0x7C read is a row of the game's own tables");
            Assert.Equal("combat_action_sync_attack", row.Table);
            var legacy = new Guid(Convert.FromHexString(g84));
            Assert.False(cat.TryGet(legacy, out _), "the legacy read is in no table");
            Assert.False(cat.BothResolve(good, legacy), "both reads hitting the catalog never happens in the field");
            Assert.True(row.HitLagMs >= 0 || row.TimeToStart == 0);
        }
    }

    [Fact]
    public void The_local_action_frame_carries_the_legacy_guid_as_an_optional_tail()
    {
        Guid g = Guid.Parse("c2af142a-5e9c-33e5-b5d1-dba0277a2540"), alt = Guid.Parse("0f0f0f0f-1111-2222-3333-444444444444");
        byte[] Frame(string name, Guid? tail)
        {
            var n = Encoding.UTF8.GetBytes(name);
            var b = new byte[27 + n.Length + (tail is null ? 0 : 16)];
            b[0] = (byte)ActionKind.Attack; b[1] = (byte)ActionPhase.Commit; b[26] = (byte)n.Length;
            g.TryWriteBytes(b.AsSpan(6)); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(22), 0x80D4);
            n.CopyTo(b, 27);
            tail?.TryWriteBytes(b.AsSpan(27 + n.Length));
            return b;
        }
        Assert.True(LocalActionFrame.TryParse(Frame("ttkc_man_5", null), out var plain));
        Assert.Equal((g, Guid.Empty), (plain.Row, plain.AltRow));
        Assert.True(LocalActionFrame.TryParse(Frame("ttkc_man_5", alt), out var tailed));
        Assert.Equal((g, alt, "ttkc_man_5"), (tailed.Row, tailed.AltRow, tailed.Name));
        var bad = Frame("x", alt); Array.Resize(ref bad, bad.Length - 3);       // a torn tail
        Assert.False(LocalActionFrame.TryParse(bad, out _));
        var longer = Frame("x", alt); Array.Resize(ref longer, longer.Length + 1);
        Assert.False(LocalActionFrame.TryParse(longer, out _));
    }

    // ------------------------------------------------------------------------------------------------------------ A2

    [Fact]
    public void Only_the_masters_counter_is_a_swing()
    {
        Assert.True(ActionRowCatalog.IsMasterStrike(55));    // CombatMasterStrikeGen
        Assert.True(ActionRowCatalog.IsMasterStrike(62));    // CombatMasterStrikeDeathGen (the one that kills)
        foreach (int notSwing in new[] { 14, 23, 24, 56, 63, -1, 3, 26 }) Assert.False(ActionRowCatalog.IsMasterStrike(notSwing));
        var cat = RealCatalog();
        if (cat is null) return;
        // the game's own table: 45 + 45 master-strike rows, none of the perfect block's or its sync half's
        string? pak = WeaponSwingCatalog.FindTablesPak();
        using var zip = ZipFile.OpenRead(pak!);
        using var s = zip.GetEntry("Libs/Tables/combat/combat_action_perfect_block.xml")!.Open();
        var rows = System.Xml.Linq.XDocument.Load(s).Descendants().Where(e => e.Attribute("mn_fragment_guid") is not null).ToList();
        int master = 0;
        foreach (var e in rows)
        {
            Assert.True(cat.TryGet(Guid.Parse((string)e.Attribute("mn_fragment_guid")!), out var r));
            Assert.Equal(int.Parse((string)e.Attribute("action_type_id")!), r.ActionType);
            if (ActionRowCatalog.IsMasterStrike(r.ActionType)) master++;
        }
        Assert.Equal((206, 90), (rows.Count, master));
    }

    // ------------------------------------------------------------------------------------------------------------ A4

    private static string SyntheticPak(params string[] attackRowXml)
    {
        string path = Path.Combine(Path.GetTempPath(), "wo163-" + Guid.NewGuid().ToString("N") + ".pak");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var e = zip.CreateEntry("Libs/Tables/combat/combat_action_attack.xml");
        using var w = new StreamWriter(e.Open());
        w.Write("<root>" + string.Concat(attackRowXml) + "</root>");
        return path;
    }

    private static string AttackRow(string guid, string frag, string tags, int attackType, int actionType, string rw = "-1", string lw = "-1") =>
        $"<r mn_fragment_guid=\"{guid}\" mn_fragment_id=\"{frag}\" mn_tags=\"{tags}\" attack_type_id=\"{attackType}\" action_type_id=\"{actionType}\" actor_class_hash=\"1\" attack_time_to_start=\"0.3\" attack_time_to_hit=\"0.7\" r_weapon_class_id=\"{rw}\" l_weapon_class_id=\"{lw}\"/>";

    [Fact]
    public void The_generic_row_is_one_animal_bite_and_one_unarmed_punch_chosen_by_content()
    {
        string pak = SyntheticPak(
            AttackRow("aaaaaaaa-0000-4000-8000-000000000001", "CombatAttack", "oppLying+aZ1+bite+attack_heavy", 8, 3),
            AttackRow("aaaaaaaa-0000-4000-8000-000000000002", "CombatAttack", "aZ1+bite+attack_heavy+oppMale+oppFemale", 8, 3),
            AttackRow("aaaaaaaa-0000-4000-8000-000000000003", "CombatAttack", "oppSitting+aZ1+bite+attack_heavy", 8, 3),
            AttackRow("bbbbbbbb-0000-4000-8000-000000000001", "FreeAttack", "l_torch+r_noweapon+freeGuard+punch+attack_heavy", 5, 26, "12", "11"),
            AttackRow("bbbbbbbb-0000-4000-8000-000000000002", "FreeAttack", "l_noweapon+r_noweapon+freeGuard+endFreeGuard+punch+attack_heavy", 5, 26, "12", "12"),
            AttackRow("cccccccc-0000-4000-8000-000000000001", "CombatAttackGen", "aZ2+slash+r_swords", 1, 3));
        try
        {
            var cat = ActionRowCatalog.LoadFrom(pak);
            var animal = cat.GenericRow(animal: true)!.Value;
            Assert.Equal("CombatAttack, aZ1+bite+attack_heavy+oppMale+oppFemale", animal.Spec);   // the plain bite: no lying / sitting condition
            Assert.Equal(8, animal.AttackType);
            var man = cat.GenericRow(animal: false)!.Value;
            Assert.Contains("l_noweapon+r_noweapon", man.Tags); Assert.Equal("FreeAttack", man.Fragment); Assert.Equal(5, man.AttackType);
            Assert.Equal(1000, animal.HitLagMs);                                                 // 0.3 + 0.7 s: the row's own lag is read
            Assert.Equal(animal.Spec, cat.GenericRow(true)!.Value.Spec);                         // deterministic
        }
        finally { File.Delete(pak); }
        string empty = SyntheticPak(AttackRow("cccccccc-0000-4000-8000-000000000001", "CombatAttackGen", "aZ2+slash+r_swords", 1, 3));
        try { var c = ActionRowCatalog.LoadFrom(empty); Assert.Null(c.GenericRow(true)); Assert.Null(c.GenericRow(false)); }   // none in the table: the caller says so and shows nothing
        finally { File.Delete(empty); }
    }

    [Fact]
    public void The_installed_game_tables_hold_both_generic_rows()
    {
        var cat = RealCatalog();
        if (cat is null) return;
        var animal = cat.GenericRow(true);
        var man = cat.GenericRow(false);
        Assert.NotNull(animal); Assert.NotNull(man);
        Assert.Contains("bite", animal!.Value.Tags);
        Assert.DoesNotContain("oppLying", animal.Value.Tags);
        Assert.Contains("l_noweapon+r_noweapon", man!.Value.Tags);
        Assert.Equal("combat_action_attack", animal.Value.Table); Assert.Equal("combat_action_attack", man.Value.Table);
    }

    [Theory]
    [InlineData(false, "no-swing-captured", true)]      // an animal's bite, or a blow nothing captured
    [InlineData(false, "swing-unmatched", true)]        // rows exist, none fits this blow
    [InlineData(false, "row-not-received", true)]
    [InlineData(false, "row-stale", true)]
    [InlineData(false, "row-not-played", false)]        // the copy had the row and could not play it: a second lunge would not help
    [InlineData(false, "row-refused-no-entity", false)]
    [InlineData(false, "missile", false)]               // not a melee blow
    [InlineData(false, "no-attacker", false)]           // nobody to move
    [InlineData(false, "legacy", false)]
    [InlineData(true, "-", false)]                      // a swing was shown
    public void A_generic_lunge_is_for_damage_with_nothing_shown_only(bool shown, string reason, bool wants) =>
        Assert.Equal(wants, Wo161Rules.WantsGenericSwing(shown, reason));

    [Fact]
    public void The_stats_line_counts_generic_swings()
    {
        var stats = new Wo161Stats();
        stats.Generic(); stats.Generic();
        stats.In(true, HitVerdict.Hit, 8.9f, 0f, true, "generic");
        Assert.Equal(2, stats.GenericShown);
        Assert.Contains("generic=2", stats.Line());
        Assert.Contains("shown=1", stats.Line());
    }
}
