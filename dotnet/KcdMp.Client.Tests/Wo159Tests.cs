// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using Spec = KcdMp.Client.Tests.WhsSaveTests.Spec;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-159: Start Game without the prologue, the agent's half -- the host's worlds (the WO-125/157 rules, reused), the
/// start save staged into a free playline with a new playthrough seed (two new adventures = two worlds), taken back when
/// never played, and the start-save recipe check. Synthetic saves only (WhsSaveTests' builder).
/// </summary>
public class Wo159Tests : IDisposable
{
    private const string Mt = "1.5.5-release_1_5", Regular = "1.5.5-15315-release_1_5";
    private const uint StartSeed = 0x5717;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "kcdmp-wo159-" + Guid.NewGuid().ToString("N"));
    private string Saves => Path.Combine(_root, "saves");
    private string Ledger => Path.Combine(_root, "data", "w159-staged.json");
    private string Source => Path.Combine(_root, "start-save");

    public Wo159Tests()
    {
        Directory.CreateDirectory(Saves);
        Directory.CreateDirectory(Source);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>A save whose header carries an account name and a used-mods list, as the game writes them.</summary>
    private static byte[] WithGameHeader(byte[] file, long saveTime = 100)
    {
        var c = WhsSave.Inflate(file);
        string desc = $"<C_SaveGameDescription SaveType=\"PermanentSave\" SaveId=\"2\" SaveTime=\"{saveTime}\" LevelName=\"trosecko\" BuildInfo=\"{Mt}\" GameMode=\"normal\">\n" +
                      "\t<UsedMods>\n\t\t<wh::S_ModInfo Name=\"Some Mod\" Author=\"someone\" Version=\"1\" />\n\t</UsedMods>\n" +
                      "\t<DebugInfoHistory>\n\t\t<S_SavegameDebugInfo BuildTime=\"x\" BuildComputer=\"y\" BuildDescription=\"z\" Configuration=\"c\" BuildInfo=\"b\" UserName=\"someaccount\" />\n\t</DebugInfoHistory>\n" +
                      "</C_SaveGameDescription>\n";
        return WhsSave.Deflate(Encoding.UTF8.GetBytes(desc), c.Raw, c.FooterTail);
    }

    private void PutStart(Spec? s = null) =>
        File.WriteAllBytes(Path.Combine(Source, "permanent002.whs"), WithGameHeader(WhsSaveTests.File(s ?? new Spec { Seed = StartSeed, Pristine = true })));

    private void Put(int pl, string name, uint seed, long t, string build = Mt, bool bohuta = false)
    {
        Directory.CreateDirectory(Path.Combine(Saves, $"playline{pl}"));
        File.WriteAllBytes(Path.Combine(Saves, $"playline{pl}", name), WhsSaveTests.File(new Spec { Seed = seed, SaveTime = t, Build = build, Bohuta = bohuta }));
    }

    private static bool IsHenry(string p) => WhsSave.PlayerOf(WhsSave.Inflate(File.ReadAllBytes(p)).Raw).IsHenry;

    // ------------------------------------------------------------------ re-key

    [Fact]
    public void Rekey_changes_only_the_seed_and_clears_the_account_name_and_the_mods_list()
    {
        var src = WithGameHeader(WhsSaveTests.File(new Spec { Seed = StartSeed, Pristine = true }));
        var o = Wo159.Rekey(src, 0xC0FFEE);
        Assert.True(WhsSave.Verify(o).Ok);   // re-signed footer, framing, Henry
        var a = WhsSave.Inflate(src).Raw;
        var b = WhsSave.Inflate(o).Raw;
        Assert.Equal(a.Length, b.Length);
        Assert.Equal(0xC0FFEEu, WhsSave.ReadSeed(b));
        int diff = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) diff++;
        Assert.InRange(diff, 1, 4);   // the four seed bytes, nothing else
        string d = WhsSave.Inflate(o).Desc;
        Assert.Contains("UserName=\"\"", d);
        Assert.Contains("BuildComputer=\"\"", d);
        Assert.DoesNotContain("someaccount", d);
        Assert.DoesNotContain("S_ModInfo", d);
        Assert.Contains("BuildInfo=\"" + Mt + "\"", d);   // the rest of the header stands
        Assert.Contains("SaveType=\"PermanentSave\"", d);
    }

    // ------------------------------------------------------------------ stage

    [Fact]
    public void New_adventure_twice_gives_two_worlds_with_two_keys_in_two_free_playlines()
    {
        PutStart();
        Put(0, "autosave001.whs", 0x1111, 50);   // playline0 is somebody's
        var r1 = Wo159.Stage(Saves, Source, Ledger);
        var r2 = Wo159.Stage(Saves, Source, Ledger);
        Assert.True(r1.Ok, r1.Why);
        Assert.True(r2.Ok, r2.Why);
        Assert.Equal((1, 2), (r1.Playline, r2.Playline));
        Assert.Equal("permanent002", r1.Name);
        Assert.NotEqual(r1.SeedTag, r2.SeedTag);
        Assert.NotEqual(WhsSave.SeedTag(StartSeed), r1.SeedTag);
        Assert.NotEqual(WhsSave.SeedTag(StartSeed), r2.SeedTag);
        foreach (var r in new[] { r1, r2 })
        {
            string f = Path.Combine(Saves, $"playline{r.Playline}", "permanent002.whs");
            Assert.True(WhsSave.VerifyFile(f).Ok);
            Assert.Equal(r.SeedTag, WhsSave.SeedTag(WhsSave.ReadSeedFromFile(f)!.Value));
            Assert.Contains(GameBridge.ListOwnSaves(Saves), s => s.FullPath == f);   // an engine-named save the game lists
            Assert.False(File.Exists(f + ".part"));
        }
        Assert.Equal(2, Wo159.ReadLedger(Ledger).Count);
        Assert.Single(Directory.GetFiles(Path.Combine(Saves, "playline0")));   // somebody's playline untouched
    }

    [Fact]
    public void Staging_never_uses_a_playline_with_any_save_in_it_and_says_so_when_all_five_hold_saves()
    {
        PutStart();
        for (int pl = 0; pl <= 4; pl++)
        {
            Directory.CreateDirectory(Path.Combine(Saves, $"playline{pl}"));
            File.WriteAllBytes(Path.Combine(Saves, $"playline{pl}", pl == 3 ? "my old save.whs" : "save001.whs"), [1, 2, 3]);   // any .whs counts
        }
        var before = Directory.GetFiles(Saves, "*", SearchOption.AllDirectories).Length;
        var r = Wo159.Stage(Saves, Source, Ledger);
        Assert.False(r.Ok);
        Assert.Contains("All five save slots", r.Why);
        Assert.Equal(before, Directory.GetFiles(Saves, "*", SearchOption.AllDirectories).Length);
        Assert.Empty(Wo159.ReadLedger(Ledger));
        // a playline folder with no save in it is free
        File.Delete(Path.Combine(Saves, "playline3", "my old save.whs"));
        File.WriteAllText(Path.Combine(Saves, "playline3", "notes.txt"), "x");
        Assert.Equal(3, Wo159.Stage(Saves, Source, Ledger).Playline);
    }

    [Fact]
    public void Staging_refuses_what_is_not_one_good_start_save()
    {
        Assert.Contains("no start save is installed", Wo159.Stage(Saves, Source, Ledger).Why);
        PutStart();
        File.WriteAllBytes(Path.Combine(Source, "permanent003.whs"), WhsSaveTests.File(new Spec()));
        Assert.Contains("holds 2 saves", Wo159.Stage(Saves, Source, Ledger).Why);
        File.Delete(Path.Combine(Source, "permanent003.whs"));
        var bad = File.ReadAllBytes(Path.Combine(Source, "permanent002.whs"));
        bad[bad.Length - 50] ^= 0xFF;   // the footer's md5 no longer matches
        File.WriteAllBytes(Path.Combine(Source, "permanent002.whs"), bad);
        Assert.Contains("does not verify", Wo159.Stage(Saves, Source, Ledger).Why);
        Assert.Empty(Directory.GetDirectories(Saves));
    }

    // ------------------------------------------------------------------ unstage

    [Fact]
    public void A_start_save_never_played_is_taken_back_one_that_was_played_stays_a_normal_save()
    {
        PutStart();
        var unused = Wo159.Stage(Saves, Source, Ledger);
        var played = Wo159.Stage(Saves, Source, Ledger);
        var loaded = Wo159.Stage(Saves, Source, Ledger);
        Put(played.Playline, "autosave001.whs", 0x2222, 500);   // the game saved beside it
        string loadedPath = Path.Combine(Saves, $"playline{loaded.Playline}", "permanent002.whs");
        Assert.True(Wo159.MarkUsed(Ledger, loadedPath));        // the launcher loaded it (no save yet)
        var u = Wo159.Unstage(Ledger);
        Assert.Equal((1, 1), (u.Removed, u.Kept));
        Assert.False(Directory.Exists(Path.Combine(Saves, $"playline{unused.Playline}")));   // the folder it made, gone again
        Assert.True(File.Exists(Path.Combine(Saves, $"playline{played.Playline}", "permanent002.whs")));
        Assert.True(File.Exists(loadedPath));
        Assert.Empty(Wo159.ReadLedger(Ledger));
        Assert.Equal(0, Wo159.Unstage(Ledger).Removed);   // nothing left to do
    }

    [Fact]
    public void A_staged_file_whose_bytes_changed_is_kept()
    {
        PutStart();
        var r = Wo159.Stage(Saves, Source, Ledger);
        string f = Path.Combine(Saves, $"playline{r.Playline}", "permanent002.whs");
        File.WriteAllBytes(f, WhsSaveTests.File(new Spec { Seed = 0x3333 }));   // the game overwrote it (a save into the same slot)
        var u = Wo159.Unstage(Ledger);
        Assert.Equal((0, 1), (u.Removed, u.Kept));
        Assert.True(File.Exists(f));
    }

    // ------------------------------------------------------------------ the host's worlds

    [Fact]
    public void Start_Game_lists_one_world_per_playline_and_names_what_it_hides()
    {
        Put(0, "autosave010.whs", 0x1000, 300);
        Put(0, "autosave009.whs", 0x1000, 200);
        Put(1, "autosave001.whs", 0x2000, 900, build: Regular);            // the regular game (the shared saves folder)
        Put(2, "quicksave004.whs", StartSeed, 800);                         // a world this install only joined
        Put(3, "permanent001.whs", 0x4000, 700, bohuta: true);              // the prologue only
        Put(4, "permanent001.whs", 0x5000, 100, bohuta: true);
        Put(4, "autosave002.whs", 0x5000, 600);                             // past it
        var r = Wo159.MenuSaves(Saves, t => t == WhsSave.SeedTag(StartSeed), IsHenry);
        Assert.Equal(new[] { (4, "autosave002"), (0, "autosave010") }, r.Usable.Select(u => (u.Playline, u.Name)).ToArray());   // newest world first
        Assert.Equal((1, 1, 1), (r.HiddenRegular, r.HiddenCopies, r.HiddenPrologue));
        Assert.Equal("Not shown: 1 from the regular game (not the Modding Tools); 1 is a copy of a host's world you joined; 1 still in the prologue.", r.Hidden);
        Assert.Empty(r.Free);
        Assert.StartsWith("Playline 4 (", r.Usable[0].Label);
    }

    [Fact]
    public void A_world_this_install_hosted_is_its_own_and_a_staged_start_save_is_not_a_world_yet()
    {
        Put(2, "quicksave004.whs", StartSeed, 800);
        PutStart();
        var st = Wo159.Stage(Saves, Source, Ledger);
        var skip = Wo159.ReadLedger(Ledger).Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var r = Wo159.MenuSaves(Saves, _ => false, IsHenry, skip);   // hosted here: not a copy
        Assert.Equal(new[] { 2 }, r.Usable.Select(u => u.Playline).ToArray());
        Assert.DoesNotContain(r.Usable, u => u.Playline == st.Playline);
        Assert.Equal(new[] { 1, 3, 4 }, r.Free.ToArray());
    }

    // ------------------------------------------------------------------ the start-save recipe

    [Fact]
    public void Validate_passes_a_clean_post_prologue_Henry_and_names_every_failure()
    {
        Assert.All(Wo159.Validate(WhsSaveTests.File(new Spec { Pristine = true })), c => Assert.True(c.Pass, c.Name + ": " + c.Detail));
        var prologue = Wo159.Validate(WhsSaveTests.File(new Spec { Bohuta = true }));
        Assert.Contains(prologue, c => c.Name == "after the prologue, as Henry" && !c.Pass && c.Detail.Contains("player_bohuta"));
        var regular = Wo159.Validate(WhsSaveTests.File(new Spec { Build = Regular }));
        Assert.Contains(regular, c => c.Name == "Modding Tools build" && !c.Pass);
        var partner = Wo159.Validate(WhsSaveTests.File(new Spec { World = "kcd2mp_6-world" }));
        Assert.Contains(partner, c => c.Name == "no partner / mod data in the world" && !c.Pass);
        var header = Wo159.Validate(WithGameHeader(WhsSaveTests.File(new Spec())));
        Assert.Contains(header, c => c.Name == "no account or machine name in the header" && !c.Pass);
        Assert.Contains(header, c => c.Name == "no mods listed in the header" && !c.Pass);
        // the scrubbed copy passes the header checks
        var c0 = WhsSave.Inflate(WithGameHeader(WhsSaveTests.File(new Spec())));
        var scrubbed = WhsSave.Deflate(Encoding.UTF8.GetBytes(Wo159.ScrubDescription(c0.Desc)), c0.Raw, c0.FooterTail);
        Assert.All(Wo159.Validate(scrubbed), c => Assert.True(c.Pass, c.Name));
        Assert.Single(Wo159.Validate([1, 2, 3]));   // unreadable: one failed check, nothing else tried
    }
}
