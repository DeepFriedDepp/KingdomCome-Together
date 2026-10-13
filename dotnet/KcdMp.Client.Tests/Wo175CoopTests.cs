// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KCDMP_launcher.Models;
using Spec = KcdMp.Client.Tests.WhsSaveTests.Spec;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-175 S1 as WO-180 left it: the host plays the save he picked, directly (no copy, no placeholder, nothing written -- the menu's
/// list shows every save as it is, old co-op playlines included); the joiner's slot: the playline that already holds the host's
/// world, else the lowest free, else refused before any request; the switch. Synthetic saves in a temp folder only (WhsSaveTests' builder).
/// </summary>
public class Wo175CoopTests : IDisposable
{
    private const uint WorldA = 0xA11CE, WorldB = 0xB0B, Other = 0x0DD;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "kcdmp-wo175coop-" + Guid.NewGuid().ToString("N"));
    private string Saves => Path.Combine(_root, "saves");

    public Wo175CoopTests() => Directory.CreateDirectory(Saves);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Put(int pl, string name, uint seed, long t)
    {
        string dir = Path.Combine(Saves, $"playline{pl}");
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, name);
        File.WriteAllBytes(f, WhsSaveTests.File(new Spec { Seed = seed, SaveTime = t }));
        return f;
    }

    private static bool IsHenry(string p) => WhsSave.PlayerOf(WhsSave.Inflate(File.ReadAllBytes(p)).Raw).IsHenry;

    private Dictionary<string, byte[]> Snapshot() =>
        Directory.GetFiles(Saves, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private static string Tag(uint seed) => WhsSave.SeedTag(seed);

    // ------------------------------------------------------------------ the host (fix 2)

    [Fact]
    public void With_all_five_playlines_full_every_save_is_listed_as_it_is_and_nothing_is_written_or_removed()
    {
        // five playlines, two of them the "co-op copies" an older WO-175 made (the same world twice)
        Put(0, "autosave005.whs", WorldA, 500);
        Put(1, "autosave005.whs", WorldA, 510);   // an old co-op copy of playline0's world
        Put(2, "autosave003.whs", WorldB, 300);
        Put(3, "quicksave002.whs", Other, 200);
        Put(4, "permanent001.whs", Other + 1, 100);
        var before = Snapshot();
        var list = Wo159.MenuSaves(Saves, _ => false, IsHenry);
        Assert.Equal(5, list.Usable.Count);   // the old co-op playline is an ordinary entry
        Assert.All(list.Usable, u => Assert.StartsWith("Playline ", u.Label));   // no "Together:" label any more
        Assert.Empty(list.Free);
        var after = Snapshot();
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));   // no file appeared, none went
        foreach (var (k, v) in before) Assert.Equal(v, after[k]);
        Assert.Empty(Directory.GetFiles(Saves, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public void The_host_side_of_the_copy_is_gone_from_the_agents_helper()
    {
        var m = typeof(Wo175Coop).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Select(x => x.Name).ToHashSet();
        foreach (var gone in new[] { "Prepare", "Commit", "Drop", "LabelTogether", "PendingPaths", "CopyName", "Remember" })
            Assert.DoesNotContain(gone, m);
    }

    // ------------------------------------------------------------------ the switch

    [Fact]
    public void The_switch_is_on_unless_mod_settings_says_off()
    {
        string f = Path.Combine(_root, "mod-settings.json");
        Assert.True(Wo175Coop.SwitchOn(f));   // no file
        File.WriteAllText(f, "{ \"NameBadges\": true }");
        Assert.True(Wo175Coop.SwitchOn(f));   // no key
        File.WriteAllText(f, "{ \"CoopPlayline\": false }");
        Assert.False(Wo175Coop.SwitchOn(f));
        File.WriteAllText(f, "{ \"CoopPlayline\": \"off\" }");
        Assert.False(Wo175Coop.SwitchOn(f));
        File.WriteAllText(f, "{ \"NameBadges\": true, \"CoopPlayline\": false }");
        Wo175Coop.SetSwitch(f, true);
        Assert.True(Wo175Coop.SwitchOn(f));
        Assert.Contains("NameBadges", File.ReadAllText(f));   // the player's other keys kept
        Wo175Coop.SetSwitch(f, false);
        Assert.False(Wo175Coop.SwitchOn(f));
        Assert.True(Wo175Coop.TryParseSwitch(" ON ", out bool on) && on);
        Assert.False(Wo175Coop.TryParseSwitch("maybe", out _));
    }

    [Fact]
    public void The_launcher_reads_the_switch_from_the_mods_event_line()
    {
        Assert.Equal((12L, true), MenuTakeoverRule.ParseCoopSwitch("<12:00:01> [KCD2-MP-EVT] v1 12 w175_coop on"));
        Assert.Equal((13L, false), MenuTakeoverRule.ParseCoopSwitch("[KCD2-MP-EVT] v1 13 w175_coop off"));
        Assert.Null(MenuTakeoverRule.ParseCoopSwitch("[KCD2-MP-EVT] v1 14 w175_coop status"));
        Assert.Null(MenuTakeoverRule.ParseCoopSwitch("[KCD2-MP-EVT] v1 15 w159 load 0 autosave005"));
        Assert.Null(MenuTakeoverRule.Parse("[KCD2-MP-EVT] v1 12 w175_coop on"));   // not a menu choice
    }

    // ------------------------------------------------------------------ the joiner (fix 3)

    [Fact]
    public void A_playline_that_already_holds_the_hosts_world_is_reused_before_any_free_one()
    {
        Put(0, "quicksave010.whs", Other, 100);   // the joiner's own playthrough
        Put(2, "mpworld12345678.whs", WorldA, 300);   // the mod's transient file: not a save
        Put(2, "quicksave011.whs", WorldA, 310);      // a save of the host's world
        var pick = Wo175Coop.ChooseForJoin(Saves, Tag(WorldA));
        Assert.Equal((true, 2, true), (pick.Ok, pick.Playline, pick.Existing));   // playline1 is free and lower, but 2 holds this world
    }

    [Fact]
    public void No_match_takes_the_lowest_free_playline_and_another_hosts_world_never_the_players_own()
    {
        Put(0, "quicksave010.whs", Other, 100);
        Put(1, "autosave001.whs", WorldB, 200);   // another host's world
        var pick = Wo175Coop.ChooseForJoin(Saves, Tag(WorldA));
        Assert.Equal((true, 2, false), (pick.Ok, pick.Playline, pick.Existing));
        // a playline that holds the world AND another playthrough's save is not this world's
        Put(2, "quicksave011.whs", WorldA, 310);
        Put(2, "permanent002.whs", Other + 1, 400);
        pick = Wo175Coop.ChooseForJoin(Saves, Tag(WorldA));
        Assert.Equal((true, 3, false), (pick.Ok, pick.Playline, pick.Existing));
    }

    [Fact]
    public void Five_full_with_no_match_is_refused_before_a_request_with_the_plain_message_and_nothing_is_touched()
    {
        for (int pl = 0; pl <= 4; pl++) Put(pl, "quicksave001.whs", Other + (uint)pl, 100 + pl);
        var before = Snapshot();
        Assert.False(Wo175Coop.ChooseForJoin(Saves, Tag(WorldA)).Ok);
        Assert.False(Wo175Coop.SlotAvailable(Saves, Tag(WorldA)));
        Assert.Equal("All five save slots are full. Delete one in the game's Load Game list, then join again.", Wo175Coop.JoinerNoFreeText);
        var after = Snapshot();
        Assert.Equal(before.Count, after.Count);
        foreach (var (k, v) in before) Assert.Equal(v, after[k]);
        // five full but one of them is this host's world: a slot, the join goes there
        Put(3, "quicksave002.whs", Other + 3, 50);   // (still another playthrough)
        File.Delete(Path.Combine(Saves, "playline3", "quicksave001.whs"));
        File.Delete(Path.Combine(Saves, "playline3", "quicksave002.whs"));
        Put(3, "quicksave003.whs", WorldA, 600);
        Assert.True(Wo175Coop.SlotAvailable(Saves, Tag(WorldA)));
        Assert.Equal(3, Wo175Coop.ChooseForJoin(Saves, Tag(WorldA)).Playline);
    }

    [Fact]
    public void A_free_playline_is_a_slot_without_looking_at_any_save()
    {
        Put(0, "quicksave001.whs", Other, 100);
        Assert.True(Wo175Coop.SlotAvailable(Saves, Tag(WorldA)));
        Assert.False(Wo175Coop.HoldsWorld(Saves, 5, Tag(WorldA)));   // never an index above 4
        Assert.False(Wo175Coop.HoldsWorld(Saves, 1, Tag(WorldA)));   // an empty or missing folder holds nothing
    }
}
