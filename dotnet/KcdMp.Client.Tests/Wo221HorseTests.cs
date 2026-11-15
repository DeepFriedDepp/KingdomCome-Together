// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-221 D.1: whose horse is whose, and that the record follows the host's saves.</summary>
public class Wo221HorseTests
{
    [Fact]
    public void One_horse_per_player_and_a_recorded_horse_is_never_taken_over()
    {
        var r = new HorseRecord();
        Assert.True(r.Set(0xA1, "the joiner", "tsem_horseForSale_2", "bought", 100));
        Assert.False(r.Set(0xA1, "the joiner", "tsem_horseForSale_2", "bought", 101));   // already his
        Assert.Equal("tsem_horseForSale_2", r.HorseOf(0xA1));
        Assert.Equal(0xA1u, r.OwnerOf("tsem_horseForSale_2")!.Owner);
        Assert.False(r.Set(0xB2, "another", "tsem_horseForSale_2", "told", 102));        // first come first served
        Assert.Null(r.HorseOf(0xB2));
        Assert.True(r.Set(0xA1, "the joiner", "tsem_horseForSale_3", "bought", 103));    // a new one replaces the one before
        Assert.Equal("tsem_horseForSale_3", r.HorseOf(0xA1));
        Assert.Null(r.OwnerOf("tsem_horseForSale_2"));
        Assert.Single(r.Entries);
        Assert.True(r.Clear(0xA1));
        Assert.False(r.Clear(0xA1));
    }

    [Fact]
    public void Only_a_horses_name_and_a_real_key_are_recorded()
    {
        var r = new HorseRecord();
        Assert.False(r.Set(0, "nobody", "horse_1", "told", 1));
        Assert.False(r.Set(5, "x", "kcd2mp_0", "told", 1));           // a figure is never a horse
        Assert.False(r.Set(5, "x", "bad name", "told", 1));
        Assert.False(r.Set(5, "x", new string('h', 60), "told", 1));
        Assert.Empty(r.Entries);
    }

    [Fact]
    public void The_wire_list_round_trips()
    {
        var r = new HorseRecord();
        r.Set(0xDEADBEEF, "a", "ttkc_horse_2", "bought", 1);
        r.Set(0x00000007, "b", "ttkc_horse_1", "given", 2);
        Assert.Equal("ttkc_horse_1=00000007,ttkc_horse_2=deadbeef", r.Csv());
        var p = HorseRecord.ParseCsv(r.Csv());
        Assert.Equal(2, p.Count);
        Assert.Equal("ttkc_horse_2", p[1].Key); Assert.Equal(0xDEADBEEFu, p[1].Value);
        Assert.Empty(HorseRecord.ParseCsv("-"));
        Assert.Empty(HorseRecord.ParseCsv("kcd2mp_1=00000007,x=0,y=zz,=5"));
        Assert.Equal("-", new HorseRecord().Csv());
    }

    [Fact]
    public void A_broken_file_is_an_empty_record()
    {
        Assert.Empty(HorseRecord.FromJson("{not json").Entries);
        Assert.Empty(HorseRecord.FromJson(null).Entries);
        var r = new HorseRecord();
        r.Set(9, "p", "horse_a", "bought", 5);
        var back = HorseRecord.FromJson(r.ToJson());
        Assert.Equal("horse_a", back.HorseOf(9));
        Assert.Equal("bought", back.OwnerOf("horse_a")!.How);
    }

    [Fact]
    public void The_record_follows_the_hosts_saves()
    {
        string root = Path.Combine(Path.GetTempPath(), "kcdmp-w221-horses-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new HorseStore(root);
            const string tag = "0a1b2c3d";
            string md5Before = new string('a', 32), md5After = new string('b', 32);
            var r = s.Current(tag);
            Assert.Empty(r.Entries);
            s.Pair(tag, md5Before, r);                                  // the host saves before the purchase
            r.Set(0x11, "the joiner", "horse_x", "bought", 10);         // the joiner buys a horse
            s.SaveCurrent(tag, r);
            s.Pair(tag, md5After, r);                                   // the host saves again
            Assert.Equal("horse_x", s.Current(tag).HorseOf(0x11));     // a restart of the agent: still his
            Assert.Equal("horse_x", s.Paired(tag, md5After)!.HorseOf(0x11));   // a reload of that save: still his
            Assert.Null(s.Paired(tag, md5Before)!.HorseOf(0x11));      // a load of the save before the purchase: not bought there
            Assert.Null(s.Paired(tag, new string('c', 32)));           // a save the record never saw
            Assert.Throws<ArgumentException>(() => s.Current("not a tag"));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public void The_mods_ask_is_answered_from_the_hosts_last_word()
    {
        Assert.Null(Wo221HorseAsk.Answer("?"));                                     // not told yet on this connection: ask the host
        Assert.Null(Wo221HorseAsk.Answer(null));
        Assert.Equal("", Wo221HorseAsk.Answer(""));                                 // the record names no horse of his
        Assert.Equal("tsem_horseForSale_2", Wo221HorseAsk.Answer("tsem_horseForSale_2"));   // after a host's reload: his own again
        Assert.Null(Wo221HorseAsk.Answer("kcd2mp_horse_1"));                        // never a mod's own body
        Assert.Null(Wo221HorseAsk.Answer("bad\"name"));
    }
}
