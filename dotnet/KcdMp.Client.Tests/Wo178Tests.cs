// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-178: the agent's rules for Part 0 (save safety, the quicksave), C1's copy offers and C4's held blows.</summary>
public class Wo178Tests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "VERSION"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void The_defaults_are_the_work_orders()
    {
        Assert.False(Wo178Rules.DefaultWorldAutosave);      // the maintainer's decision: no scheduled world saves
        Assert.True(Wo178Rules.DefaultQuicksave);
        Assert.False(Wo178Rules.DefaultHostLockAnswer);     // compared live, never flipped here
        Assert.True(Wo178Rules.DefaultFallbackHold);
        Assert.True(Wo178Rules.DefaultFallbackEngineCap);
    }

    [Fact]
    public void No_code_path_saves_through_the_resting_save()
    {
        // the static check (tools/Test-WO178NoRestingSave.py holds the same rule): no call of Game.SaveGameViaResting in the shipped Lua,
        // the agent or the launcher -- comments may name it
        string root = RepoRoot();
        var call = new Regex(@"SaveGameViaResting\s*[\(\)]|pcall\s*\(\s*Game\.SaveGameViaResting", RegexOptions.CultureInvariant);
        var hits = new List<string>();
        foreach (string dir in new[] { "kdcmp", "dotnet/KcdMp.Client", "dotnet/KcdMp.Server", "KCDMP_launcher" })
        {
            foreach (string f in Directory.EnumerateFiles(Path.Combine(root, dir), "*.*", SearchOption.AllDirectories))
            {
                if (!(f.EndsWith(".lua", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))) continue;
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                int n = 0;
                foreach (string line in File.ReadLines(f))
                {
                    n++;
                    string code = f.EndsWith(".lua", StringComparison.Ordinal) ? StripLua(line) : StripCs(line);
                    if (call.IsMatch(code)) hits.Add($"{Path.GetRelativePath(root, f)}:{n}");
                }
            }
        }
        Assert.Empty(hits);
    }

    private static string StripLua(string l) { int i = l.IndexOf("--", StringComparison.Ordinal); return i >= 0 ? l[..i] : l; }
    private static string StripCs(string l)
    {
        string t = l.TrimStart();
        if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)) return "";
        int i = l.IndexOf("//", StringComparison.Ordinal);
        return i >= 0 ? l[..i] : l;
    }

    [Theory]
    [InlineData(null, "insert", "home")]
    [InlineData("", "insert", "home")]
    [InlineData("delete", "insert", "delete")]
    [InlineData("NP_ADD", "insert", "np_add")]
    [InlineData("f10", "insert", "home")]          // F10 is the build's own debug key: never offered
    [InlineData("np_add", "np_add", "home")]       // never the menu key: the next free one
    [InlineData("home", "home", "delete")]
    public void The_quicksave_key_is_one_of_the_list_and_never_the_menu_key(string? setting, string menu, string want)
    {
        Assert.Equal(want, Wo178Rules.ResolveQuickSaveKey(setting, menu, out _));
    }

    [Fact]
    public void The_keys_pak_binds_the_quicksave_key_in_the_interaction_map()
    {
        string body = KeybindPak.PatchBody(KeybindPak.EmbeddedPatch("keybindSuperactions.append.xml"));
        Assert.Equal("home", KeybindPak.BoundKey(body, KeybindPak.QuickSaveAction));
        Assert.Contains(KeybindPak.QuickSaveAction, KeybindPak.Actions);
        foreach (string k in Wo178Rules.QuickSaveKeys)
        {
            string s = KeybindPak.WithQuickSaveKey(body, k);
            Assert.Equal(k, KeybindPak.BoundKey(s, KeybindPak.QuickSaveAction));
            Assert.Equal("insert", KeybindPak.BoundKey(s, KeybindPak.MenuToggleAction));   // the menu key is untouched
            foreach (var (a, key) in KeybindPak.DiceKeys) Assert.Equal(key, KeybindPak.BoundKey(s, a));   // so are the dice keys
        }
        Assert.Throws<ArgumentException>(() => KeybindPak.WithQuickSaveKey(body, "f10"));
        string profile = KeybindPak.PatchBody(KeybindPak.EmbeddedPatch("defaultProfile.interaction.xml"));
        Assert.Single(Regex.Matches(profile, "<action name=\"kcd2mp_quicksave\" onPress=\"1\" keyboard=\"_keybinds_ref_\" />"));
        Assert.Single(Regex.Matches(body, "<action name=\"kcd2mp_quicksave\" map=\"interaction\" />"));
    }

    [Fact]
    public void A_copy_is_offered_for_a_fight_with_a_player_and_withdrawn_out_of_combat()
    {
        const byte me = 2;
        Assert.Equal(Wo178Rules.WhyEngaged, Wo178Rules.OfferWhy(true, NpcCombatTarget.Avatar, me, me));       // fighting me
        Assert.Equal(Wo178Rules.WhyPartnerFight, Wo178Rules.OfferWhy(true, NpcCombatTarget.Host, 0xFF, me));  // fighting my host
        Assert.Equal(Wo178Rules.WhyNone, Wo178Rules.OfferWhy(false, NpcCombatTarget.Avatar, me, me));         // out of combat
        Assert.Equal(Wo178Rules.WhyNone, Wo178Rules.OfferWhy(false, NpcCombatTarget.Host, 0xFF, me));
        Assert.Equal("engaged", Wo178Rules.WhyName(Wo178Rules.WhyEngaged));
        Assert.Equal("partner-fight", Wo178Rules.WhyName(Wo178Rules.WhyPartnerFight));
        Assert.Equal("withdrawn", Wo178Rules.WhyName(Wo178Rules.WhyNone));
    }

    [Theory]
    [InlineData("quicksave:host:key", true)]
    [InlineData("world:schedule", true)]
    [InlineData("rest-sleep", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a\"); os.exit(", false)]
    [InlineData("0123456789012345678901234567890123456789x", false)]
    public void The_save_word_is_checked_before_it_reaches_the_dll_or_the_lua(string? why, bool ok) =>
        Assert.Equal(ok, Wo178Rules.IsSaveWhy(why));

    [Fact]
    public void The_answers_and_the_setting_parse_strictly()
    {
        Assert.True(Wo178Rules.TryParseAnswer("saved", out string w, out string y) && w == "saved" && y == "");
        Assert.True(Wo178Rules.TryParseAnswer("refused quicksave-off", out w, out y) && w == "refused" && y == "quicksave-off");
        Assert.True(Wo178Rules.TryParseAnswer("waited", out w, out _) && w == "waited");
        Assert.False(Wo178Rules.TryParseAnswer("deleted", out _, out _));
        Assert.False(Wo178Rules.TryParseAnswer("refused a\"b", out _, out _));
        Assert.True(Wo178Rules.TryParseSetting("quicksave=on", out bool on) && on);
        Assert.True(Wo178Rules.TryParseSetting("quicksave=off", out on) && !on);
        Assert.False(Wo178Rules.TryParseSetting("quicksave=maybe", out _));
        Assert.True(Wo178Rules.IsAskSource("key") && Wo178Rules.IsAskSource("menu") && Wo178Rules.IsAskSource("console"));
        Assert.False(Wo178Rules.IsAskSource("partner"));
        Assert.Equal("requested", Wo178Rules.SaveWord(1));
        Assert.Equal("refused", Wo178Rules.SaveWord(0));
        Assert.Equal("not-armed", Wo178Rules.SaveWord(2));
    }

    [Theory]
    [InlineData("world_autosave=on", "world_autosave", true, true)]
    [InlineData("quicksave=off", "quicksave", false, true)]
    [InlineData("joiner_lock=on", "joiner_lock", true, true)]
    [InlineData("host_lock_answer=off", "host_lock_answer", false, true)]
    [InlineData("fallback_hold=on", "fallback_hold", true, true)]
    [InlineData("fallback_engine_cap=off", "fallback_engine_cap", false, true)]
    [InlineData("rm_rf=on", "", false, false)]
    [InlineData("quicksave=1", "", false, false)]
    public void The_cfg_line_names_only_its_switches(string arg, string name, bool on, bool ok)
    {
        Assert.Equal(ok, Wo178Rules.TryParseCfg(arg, out string n, out bool o));
        if (ok) { Assert.Equal(name, n); Assert.Equal(on, o); }
    }

    [Fact]
    public void The_quicksave_kinds_are_appended_and_the_wire_version_stays()
    {
        Assert.Equal(12, Protocol.W164QuickSaveAsk);
        Assert.Equal(13, Protocol.W164QuickSaveAnswer);
        Assert.Equal(14, Protocol.W164QuickSaveSetting);
        Assert.Equal(10, Protocol.Version);   // append-only: no bump
        Assert.Equal(24, Protocol.Wo178OpOffer);
        Assert.Equal(28, Protocol.Wo178OpHeldResult);
        Assert.Equal(4, Wo178Rules.AppliedHeld);
    }

    [Fact]
    public void The_launcher_reads_the_marker_only_for_a_finished_backup()
    {
        string root = Path.Combine(Path.GetTempPath(), "wo178pick-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "20261012-221500"));
        try
        {
            Assert.Null(KCDMP_launcher.Models.Wo175Backup.ReadPick(root));   // no mark
            File.WriteAllText(Path.Combine(root, Wo178Rules.PickFileName), Wo178Rules.PickText("20261012-221500", "draw-refused", DateTime.UtcNow));
            Assert.Null(KCDMP_launcher.Models.Wo175Backup.ReadPick(root));   // the backup has no manifest: not a finished backup
            File.WriteAllText(Path.Combine(root, "20261012-221500", KCDMP_launcher.Models.Wo175Backup.ManifestName), "");
            Assert.Equal(Path.Combine(root, "20261012-221500"), KCDMP_launcher.Models.Wo175Backup.ReadPick(root));
            Assert.Equal(KCDMP_launcher.Models.Wo175Backup.PickFileName, Wo178Rules.PickFileName);   // the agent and the launcher name one file
            File.WriteAllText(Path.Combine(root, Wo178Rules.PickFileName), "..\\elsewhere\n");
            Assert.Null(KCDMP_launcher.Models.Wo175Backup.ReadPick(root));   // a mark that is not a stamp is ignored
            KCDMP_launcher.Models.Wo175Backup.ClearPick(root);
            Assert.False(File.Exists(Path.Combine(root, Wo178Rules.PickFileName)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void The_restore_marker_names_the_backup_and_why()
    {
        string t = Wo178Rules.PickText("20261012-221500", "draw-refused", new DateTime(2026, 10, 12, 22, 30, 0, DateTimeKind.Utc));
        var lines = t.Split('\n');
        Assert.Equal("20261012-221500", lines[0]);
        Assert.Equal("draw-refused", lines[1]);
        Assert.StartsWith("2026-10-12T22:30:00", lines[2]);
    }
}
