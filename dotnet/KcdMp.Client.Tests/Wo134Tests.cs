using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-134: world items -- the wire, the checks, the chest ledgers (docs/WO-134-findings.md).</summary>
public class Wo134Tests
{
    private const string Coat = "a856e87a-8065-4338-919d-0aff7a63341d";
    private const string Money = "5ef63059-322e-4e1b-abe8-926e100c770e";
    private const string Dice = "b8df2253-e5c8-4e6c-9303-b4bc84192e67";
    private const string Chest = "stash[Chest/chest3_c1d92081-d74b-4955-ade8-384e71325794]";

    // ---------------------------------------------------------------- the wire

    [Fact]
    public void Both_loot_types_are_join_channel_rows_with_the_right_sender()
    {
        var ask = Protocol.JoinWireFor(Protocol.LootAskUp);
        var host = Protocol.JoinWireFor(Protocol.LootHostUp);
        Assert.NotNull(ask); Assert.NotNull(host);
        Assert.Equal(Protocol.JoinFrom.Joiner, ask!.Value.From);
        Assert.Equal(Protocol.JoinFrom.Host, host!.Value.From);
        Assert.Equal(Protocol.LootAskDown, ask.Value.Down);
        Assert.Equal(Protocol.LootHostDown, host.Value.Down);
        // never a type an earlier WO uses
        Assert.Equal(1, Protocol.JoinWire.Count(r => r.Up == Protocol.LootAskUp || r.Down == Protocol.LootAskUp));
        Assert.DoesNotContain(new byte[] { Protocol.ItemDropUp, Protocol.ItemDropDown, Protocol.ItemClaimUp, Protocol.ItemClaimDown },
                              b => b is Protocol.LootAskUp or Protocol.LootAskDown or Protocol.LootHostUp or Protocol.LootHostDown);
    }

    [Fact]
    public void A_loot_message_round_trips_and_rejects_non_printable_text()
    {
        var m = new LootMsg(Protocol.LootAskBodyTake, 12345, $"bandit_7 {Coat} 1 0.7333");
        var pkt = m.BuildUp(Protocol.LootAskUp, Protocol.JoinTargetHost);
        Assert.Equal(Protocol.LootAskUp, pkt[0]);
        Assert.Equal(Protocol.JoinTargetHost, pkt[3]);
        var body = pkt.AsSpan(3 + Protocol.JoinHeaderLen).ToArray();
        Assert.True(LootMsg.TryDecode(body, out var got));
        Assert.Equal(m, got);
        Assert.Throws<ArgumentException>(() => new LootMsg(1, 0, "a\"b\nc").BuildUp(Protocol.LootAskUp, 0xFF));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LootMsg(1, 0, new string('x', Protocol.LootTextMax + 1)).BuildUp(Protocol.LootAskUp, 0xFF));
        body[^1] = 0x0A;
        Assert.False(LootMsg.TryDecode(body, out _));
    }

    // ---------------------------------------------------------------- item lists

    [Fact]
    public void An_item_list_parses_formats_and_refuses_anything_odd()
    {
        var l = Wo134Rules.ParseItems($"{Coat}:1:0.7333:w,{Money}:201:1");
        Assert.NotNull(l);
        Assert.Equal(2, l!.Count);
        Assert.True(l[0].Worn);
        Assert.Equal(201, l[1].Amt);
        Assert.Equal($"{Coat}:1:0.7333:w,{Money}:201:1", Wo134Rules.FormatItems(l));
        Assert.Empty(Wo134Rules.ParseItems("-")!);
        Assert.Null(Wo134Rules.ParseItems($"{Coat}:1"));                          // no health
        Assert.Null(Wo134Rules.ParseItems($"{Coat}:0:1"));                        // zero pieces
        Assert.Null(Wo134Rules.ParseItems($"{Coat}:1:9"));                        // health out of range
        Assert.Null(Wo134Rules.ParseItems("not-a-guid:1:1"));
        Assert.Null(Wo134Rules.ParseItems($"{Coat}\"):1:1"));
        Assert.Null(Wo134Rules.ParseItems(string.Join(",", Enumerable.Repeat($"{Coat}:1:1", Wo134Rules.MaxBodyItems + 1))));
    }

    [Fact]
    public void Worn_is_marked_once_per_worn_class_from_the_hosts_equipment()
    {
        var l = Wo134Rules.ParseItems($"{Coat}:1:0.5,{Coat}:1:0.9,{Money}:3:1")!;
        var m = Wo134Rules.MarkWorn(l, [Guid.Parse(Coat)]);
        Assert.Equal(1, m.Count(i => i.Worn));
        Assert.True(m[0].Worn);
        Assert.False(m[2].Worn);
        Assert.All(Wo134Rules.MarkWorn(l, null), i => Assert.False(i.Worn));   // the read failed: nothing marked, nothing put on
    }

    [Fact]
    public void The_lua_item_table_is_quoted_and_fits_a_console_command()
    {
        var l = Wo134Rules.ParseItems(string.Join(",", Enumerable.Repeat($"{Coat}:1:0.7333:w", Wo134Rules.BodyPartItems)))!;
        var s = Wo134Rules.LuaItems(l);
        Assert.StartsWith("{{\"" + Coat + "\",1,0.7333,true},", s);
        string call = $"if KCD2MP_W134BodyState then KCD2MP_W134BodyState(\"prepadeniNaCeste_bandit_9\", \"update\", 1, 1, 1, {s}) end";
        Assert.True(Uri.EscapeDataString("#" + call).Length < 2100, $"{Uri.EscapeDataString("#" + call).Length} encoded chars");
    }

    [Fact]
    public void Names_are_checked_body_names_are_engine_names_containers_are_the_levels()
    {
        Assert.Matches(Wo134Rules.BodyName, "ttkc_man_28");
        Assert.DoesNotMatch(Wo134Rules.BodyName, "a\"b");
        Assert.DoesNotMatch(Wo134Rules.BodyName, "Stash3[x]");
        Assert.Matches(Wo134Rules.ContainerName, Chest);
        Assert.Matches(Wo134Rules.ContainerName, "Stash3[Stash/sack_apples10_edbefb99-8c81-4327-bb3e-05fd3e9cb037]");
        Assert.DoesNotMatch(Wo134Rules.ContainerName, "chest\") os.exit(");
        Assert.DoesNotMatch(Wo134Rules.ContainerName, "chest with space");
    }

    // ---------------------------------------------------------------- ledgers

    private static Wo134Rules.Entry E(string c, string cls, int n, long t = 1000, int r = 7) => new() { C = c, Cls = cls, N = n, Hp = 1f, T = t, R = r };

    [Fact]
    public void A_chest_event_parses_only_when_every_field_is_sound()
    {
        var e = Wo134Rules.ParseChestEvent($"{Chest} {Dice} 1 1.0000 752647 7");
        Assert.NotNull(e);
        Assert.Equal(1, e!.N);
        Assert.Equal(7, e.R);
        Assert.Equal(-2, Wo134Rules.ParseChestEvent($"{Chest} {Dice} -2 1 752647 0")!.N);   // a put
        Assert.Null(Wo134Rules.ParseChestEvent($"{Chest} {Dice} 0 1 752647 7"));             // nothing moved
        Assert.Null(Wo134Rules.ParseChestEvent($"bad\"name {Dice} 1 1 752647 7"));
        Assert.Null(Wo134Rules.ParseChestEvent($"{Chest} notaguid 1 1 752647 7"));
        Assert.Null(Wo134Rules.ParseChestEvent($"{Chest} {Dice} 1 1 752647"));
    }

    [Fact]
    public void Takes_and_puts_on_one_container_merge_within_an_hour_and_cancel_out()
    {
        var l = new Wo134Rules.Ledger();
        l.Add(E(Chest, Dice, 1, 1000));
        l.Add(E(Chest, Dice, 2, 1200));
        Assert.Single(l.Entries);
        Assert.Equal(3, l.Entries[0].N);
        l.Add(E(Chest, Dice, -3, 1300));
        Assert.Empty(l.Entries);   // taken and put back: nothing to remember
        l.Add(E(Chest, Dice, 1, 1000));
        l.Add(E(Chest, Dice, 1, 1000 + 7200));   // two hours later: its own restock clock
        Assert.Equal(2, l.Entries.Count);
    }

    [Fact]
    public void An_entry_expires_after_its_containers_restock_period_never_with_restock_zero()
    {
        var l = new Wo134Rules.Ledger();
        l.Add(E(Chest, Dice, 1, 0, 7));
        l.Add(E("stash[Chest/b]", Dice, 1, 0, 0));
        Assert.Equal(0, l.PruneExpired(7 * 86400));
        Assert.Equal(1, l.PruneExpired(7 * 86400 + 1));
        Assert.Single(l.Entries);
        Assert.Equal(0, l.Entries[0].R);
        Assert.Equal(0, l.PruneExpired(long.MaxValue / 2));
    }

    [Fact]
    public void After_a_join_the_hosts_takes_come_back_and_the_joiners_own_go()
    {
        // The maintainer's example: the host steals the dice from chest A; the joiner
        // takes the same dice from chest B in an earlier session.
        var host = new Wo134Rules.Ledger(); host.Add(E("A", Dice, 1));
        var joiner = new Wo134Rules.Ledger(); joiner.Add(E("B", Dice, 1));
        var rows = Wo134Rules.JoinRows(host, joiner);
        Assert.Contains(rows, r => r.C == "A" && r.N == 1);    // A is full for the joiner
        Assert.Contains(rows, r => r.C == "B" && r.N == -1);   // B stays empty for him
        // The host put its sword into chest C: not in the joiner's copy of C.
        var h2 = new Wo134Rules.Ledger(); h2.Add(E("C", Coat, -1));
        Assert.Equal(-1, Wo134Rules.JoinRows(h2, new Wo134Rules.Ledger()).Single().N);
        // Both took the same dice from one chest: the joiner's copy lacks it (he took it).
        var hb = new Wo134Rules.Ledger(); hb.Add(E("D", Dice, 1));
        var jb = new Wo134Rules.Ledger(); jb.Add(E("D", Dice, 1));
        Assert.Equal(0, Wo134Rules.JoinRows(hb, jb).Sum(r => r.N));
    }

    [Fact]
    public void A_ledger_crosses_in_parts_within_the_wire_limit_and_comes_back_whole()
    {
        var l = new Wo134Rules.Ledger();
        for (int i = 0; i < 300; i++) l.Entries.Add(E($"stash[Chest/chest{i}_c1d92081-d74b-4955-ade8-384e71325794]", Dice, i % 5 + 1, 752647 + i, i % 3 * 7));
        var parts = Wo134Rules.LedgerParts(l);
        Assert.True(parts.Count > 1);
        Assert.All(parts, p => Assert.True(p.Length <= Protocol.LootTextMax, $"{p.Length}"));
        var back = new List<Wo134Rules.Entry>();
        foreach (var p in parts)
        {
            var r = Wo134Rules.ParseLedgerPart(p);
            Assert.NotNull(r);
            Assert.Equal(parts.Count, r!.Value.N);
            back.AddRange(r.Value.Rows);
        }
        Assert.Equal(l.Entries.Select(Wo134Rules.RowText), back.Select(Wo134Rules.RowText));
        Assert.Equal("1 1 -", Wo134Rules.LedgerParts(new Wo134Rules.Ledger()).Single());
        Assert.Empty(Wo134Rules.ParseLedgerPart("1 1 -")!.Value.Rows);
        Assert.Null(Wo134Rules.ParseLedgerPart($"1 1 {Chest}|{Dice}|1|1|0"));   // a short row
    }

    [Fact]
    public void Every_apply_call_fits_a_console_command()
    {
        var rows = Enumerable.Range(0, 37).Select(i => E($"Stash3[Stash/sack_apples{i}_edbefb99-8c81-4327-bb3e-05fd3e9cb037]", Dice, -(i + 1), 7526470 + i, 7)).ToList();
        var calls = Wo134Rules.ApplyCalls(rows);
        Assert.True(calls.Count >= 4);
        var longName = "stash[profession/seller/shop_inside11:profession/seller/shop_base2[profession/seller/shop_inside11]:Chest/chest3[profession/seller/shop_inside11:profession/seller/shop_base2[profession/seller/shop_inside11]]_fec43193-646b-4717-b4ee-d645060d0765]";
        Assert.Matches(Wo134Rules.ContainerName, longName);
        Assert.Matches(Wo134Rules.ContainerName, "Stash2[AnimalCare.henCare7:henCare_coop1[AnimalCare.henCare7]_1a3999d2-e9dd-0261-30f1-45781e1ff5e2]");
        var longCalls = Wo134Rules.ApplyCalls(Enumerable.Range(0, 25).Select(i => E(longName, Dice, 1, 7526470 + i, 7)).ToList());
        Assert.All(longCalls, c => Assert.True(Uri.EscapeDataString("#" + c).Length < 2100, $"{Uri.EscapeDataString("#" + c).Length}"));
        Assert.Equal(25, longCalls.Sum(c => System.Text.RegularExpressions.Regex.Matches(c, "fec43193").Count));
        Assert.All(calls, c => Assert.True(Uri.EscapeDataString("#" + c).Length < 2100, $"{Uri.EscapeDataString("#" + c).Length}"));
        Assert.Single(Wo134Rules.ApplyCalls([]));   // nothing to apply still tells the mod (the apply line)
    }

    [Fact]
    public void A_broken_ledger_file_is_an_empty_ledger_and_bad_rows_are_dropped()
    {
        Assert.Empty(Wo134Rules.Ledger.FromJson("{not json").Entries);
        Assert.Empty(Wo134Rules.Ledger.FromJson(null).Entries);
        var l = Wo134Rules.Ledger.FromJson("{\"Entries\":[{\"C\":\"x\\\"y\",\"Cls\":\"" + Dice + "\",\"N\":1,\"Hp\":1,\"T\":1,\"R\":7},{\"C\":\"ok\",\"Cls\":\"" + Dice + "\",\"N\":1,\"Hp\":1,\"T\":1,\"R\":7}]}");
        Assert.Single(l.Entries);
        Assert.Equal("ok", l.Entries[0].C);
    }

    [Fact]
    public void The_host_store_pairs_a_ledger_with_each_save_and_gives_it_back_at_that_load()
    {
        var root = Path.Combine(Path.GetTempPath(), "wo134-" + Guid.NewGuid().ToString("N"));
        try
        {
            var st = new ChestLedgerStore(root);
            string tag = "0123456789", md5a = new string('a', 32), md5b = new string('b', 32);
            var l = new Wo134Rules.Ledger(); l.Add(E(Chest, Dice, 1));
            st.SaveCurrent(tag, l);
            st.Pair(tag, md5a, l);
            l.Add(E("stash[Chest/b]", Coat, 1));
            st.Pair(tag, md5b, l);
            Assert.Single(st.Paired(tag, md5a)!.Entries);
            Assert.Equal(2, st.Paired(tag, md5b)!.Entries.Count);
            Assert.Null(st.Paired(tag, new string('c', 32)));
            Assert.Single(st.Current(tag).Entries);
            Assert.Throws<ArgumentException>(() => st.Current("../x"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void The_joiners_ledger_lives_and_dies_with_its_henry_snapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "wo134h-" + Guid.NewGuid().ToString("N"));
        try
        {
            var hs = new HenryStore(root);
            var snap = new HenryStore.Snapshot("0123456789", "snap-20260927080000000-" + new string('a', 32) + "-snapshot.hblk", new string('a', 32), DateTime.UtcNow, "snapshot", 1);
            Directory.CreateDirectory(hs.WorldDir(snap.Tag));
            Assert.Null(hs.LoadChestLedger(snap));   // a snapshot from before WO-134
            var l = new Wo134Rules.Ledger(); l.Add(E("B", Dice, 1));
            hs.StoreChestLedger(snap, l.ToJson());
            Assert.Single(Wo134Rules.Ledger.FromJson(hs.LoadChestLedger(snap)).Entries);
            Assert.DoesNotContain(".hblk", Path.GetFileName(hs.ChestLedgerPath(snap)));   // never mistaken for a snapshot
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
