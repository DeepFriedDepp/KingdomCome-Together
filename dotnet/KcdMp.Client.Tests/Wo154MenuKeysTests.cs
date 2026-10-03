// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-154 Phase 7b: the keys pak carries the mod menu's four actions (Insert / the player's MenuKey, PgUp, PgDn, End)
/// beside WO-148's dice keys, which stay exactly as they were; the MenuKey comes from the launcher's settings.json and
/// anything not allowed is Insert, said in the outcome. The menu itself: Wo154MenuTests, tools/Test-WO154Synthetic.lua.
/// </summary>
public class Wo154MenuKeysTests
{
    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "kcdmp-wo154k-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // ---------------------------------------------------------------- the keys pak: the menu's actions, the dice keys unchanged

    private static string ProfileBody() => KeybindPak.PatchBody(KeybindPak.EmbeddedPatch("defaultProfile.interaction.xml"));
    private static string SuperBody() => KeybindPak.PatchBody(KeybindPak.EmbeddedPatch("keybindSuperactions.append.xml"));
    private static readonly string[] MenuActions = { "kcd2mp_menu_toggle", "kcd2mp_menu_up", "kcd2mp_menu_down", "kcd2mp_menu_change" };

    // A made-up stand-in for the game's files: the same shape, none of their content.
    private const string FakeProfile =
        "<profile version=\"0\">\r\n" +
        "\t<actionmap name=\"interaction\" priority=\"pure_include\" exclusivity=\"0\">\r\n" +
        "\t\t<action name=\"use\" onPress=\"1\" keyboard=\"_keybinds_ref_\" />\r\n" +
        "\t</actionmap>\r\n" +
        "</profile>\r\n";
    private const string FakeSuperactions =
        "<keybinds>\r\n\t<superaction name=\"use\" ui_group=\"general\">\r\n\t\t<action name=\"use\" map=\"interaction\" />\r\n" +
        "\t\t<control input=\"e\" controller=\"keyboard\" />\r\n\t</superaction>\r\n</keybinds>\r\n";

    [Fact]
    public void The_menus_four_actions_are_in_both_patch_files_in_the_interaction_map()
    {
        string p = ProfileBody(), s = SuperBody();
        foreach (string a in MenuActions)
        {
            Assert.Contains(a, KeybindPak.Actions);
            Assert.Single(Regex.Matches(p, $"<action name=\"{a}\" onPress=\"1\" keyboard=\"_keybinds_ref_\" />"));
            Assert.Single(Regex.Matches(s, $"<action name=\"{a}\" map=\"interaction\" />"));
            Assert.Single(Regex.Matches(s, $"<superaction name=\"{a}\" ui_group=\"\" keyboard=\"hidden\">"));
        }
        Assert.Equal("insert", KeybindPak.BoundKey(s, KeybindPak.MenuToggleAction));
        foreach (var (a, k) in KeybindPak.MenuNavKeys) Assert.Equal(k, KeybindPak.BoundKey(s, a));
        Assert.Equal(new[] { "pgup", "pgdn", "end" }, KeybindPak.MenuNavKeys.Select(x => x.Key));
        Assert.Equal(14, KeybindPak.Actions.Length);
    }

    [Fact]
    public void The_dice_keys_are_exactly_as_before_whatever_the_menu_key()
    {
        Assert.Equal(new[] { "f2", "f4", "f5", "f6", "f7", "f8", "f9", "f11", "f12", "u" }, KeybindPak.DiceKeys.Select(d => d.Key));
        Assert.Equal(KeybindPak.Actions.Take(10), KeybindPak.DiceKeys.Select(d => d.Action));
        foreach (string mk in KeybindPak.MenuKeys)
        {
            string s = KeybindPak.WithMenuKey(SuperBody(), mk);
            foreach (var (a, k) in KeybindPak.DiceKeys) Assert.Equal(k, KeybindPak.BoundKey(s, a));
        }
        // the dice lines of the profile patch are WO-148's, byte for byte
        string p = ProfileBody();
        Assert.Contains("\t\t<action name=\"kcd2mp_dice_bank\" onPress=\"1\" onRelease=\"1\" keyboard=\"_keybinds_ref_\" />\r\n", p);
        Assert.Contains("\t\t<action name=\"kcd2mp_dice_cancel\" onPress=\"1\" keyboard=\"_keybinds_ref_\" />\r\n", p);
        Assert.True(p.IndexOf("kcd2mp_dice_cancel", StringComparison.Ordinal) < p.IndexOf("kcd2mp_menu_toggle", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "insert", false)]
    [InlineData("", "insert", false)]
    [InlineData("insert", "insert", false)]
    [InlineData("INSERT", "insert", false)]
    [InlineData(" np_add ", "np_add", false)]
    [InlineData("np_subtract", "np_subtract", false)]
    [InlineData("scrolllock", "insert", true)]
    [InlineData("pause", "insert", true)]
    [InlineData("f9", "insert", true)]
    [InlineData("pgup", "insert", true)]
    [InlineData(@"C:\Users\someone\secret", "insert", true)]
    public void The_menu_key_setting_resolves_or_falls_back_to_insert_with_a_note(string? setting, string want, bool noted)
    {
        Assert.Equal(want, KeybindPak.ResolveMenuKey(setting, out string note));
        Assert.Equal(noted, note.Length > 0);
        if (noted)
        {
            Assert.EndsWith("insert used", note);
            Assert.DoesNotContain("\\", note);   // a value that is not a key name is not repeated
        }
    }

    [Fact]
    public void WithMenuKey_changes_only_the_toggles_one_control()
    {
        string s = SuperBody();
        string t = KeybindPak.WithMenuKey(s, "np_add");
        Assert.Equal("np_add", KeybindPak.BoundKey(t, KeybindPak.MenuToggleAction));
        Assert.Equal(s.Length - "insert".Length + "np_add".Length, t.Length);
        Assert.Equal(s, KeybindPak.WithMenuKey(t, "insert"));   // and back, byte for byte
        Assert.Throws<ArgumentException>(() => KeybindPak.WithMenuKey(s, "f9"));
        Assert.Throws<InvalidDataException>(() => KeybindPak.WithMenuKey("<superaction name=\"x\" />", "insert"));
    }

    [Fact]
    public void The_allowed_menu_keys_are_none_of_the_mods_other_keys_nor_the_engines_own()
    {
        var mods = KeybindPak.DiceKeys.Select(d => d.Key).Concat(KeybindPak.MenuNavKeys.Select(n => n.Key)).ToHashSet();
        // the engine's own (ScrollLock, Pause, the console), the Modding Tools' debug keys and H (WO-6), Esc
        string[] engine = { "scrolllock", "pause", "tilde", "f1", "f3", "f10", "np_divide", "np_multiply", "h", "escape" };
        foreach (string k in KeybindPak.MenuKeys)
        {
            Assert.DoesNotContain(k, mods);
            Assert.DoesNotContain(k, engine);
        }
        Assert.Equal(KeybindPak.MenuKeyDefault, KeybindPak.MenuKeys[0]);
        Assert.Equal(3, KeybindPak.MenuKeys.Length);   // short on purpose
    }

    [Fact]
    public void Merged_into_the_games_files_every_action_is_named_exactly_once_more()
    {
        string? p = KeybindPak.MergeProfile(FakeProfile, ProfileBody(), out string why);
        Assert.NotNull(p);
        Assert.Equal("ok", why);
        string? s = KeybindPak.MergeSuperactions(FakeSuperactions, KeybindPak.WithMenuKey(SuperBody(), "np_subtract"), out why);
        Assert.NotNull(s);
        Assert.Equal("np_subtract", KeybindPak.BoundKey(s!, KeybindPak.MenuToggleAction));
        Assert.Equal("e", KeybindPak.BoundKey(s!, "use"));   // the game's own line untouched
        Assert.Null(KeybindPak.MergeSuperactions(s!, SuperBody(), out why));   // never twice
        Assert.Contains("already", why);
    }

    [Fact]
    public void The_cli_binds_the_launchers_menu_key_and_the_pak_reads_it_back()
    {
        string root = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            using (var z = ZipFile.Open(Path.Combine(root, "Data", "IPL_GameData.pak"), ZipArchiveMode.Create))
            {
                foreach (var (entry, text) in new[] { (KeybindPak.ProfileEntry, FakeProfile), (KeybindPak.SuperactionsEntry, FakeSuperactions) })
                    using (var w = new StreamWriter(z.CreateEntry(entry).Open(), new UTF8Encoding(false)))
                        w.Write(text);
            }
            string mod = Path.Combine(root, "Mods", "kdcmp");
            Directory.CreateDirectory(mod);
            string settings = Path.Combine(root, "settings.json");
            string pak = Path.Combine(mod, "Data", KeybindPak.PakFileName);
            string[] Cli() => new[] { "--keys-pak", "--game-root", root, "--mod-dir", mod, "--settings", settings };

            File.WriteAllText(settings, "{\"GamePath\":\"x\",\"MenuKey\":\"np_subtract\"}");
            var sw = new StringWriter();
            Assert.Equal(0, KeybindPak.RunCli(Cli(), sw));
            Assert.Contains("menu key np_subtract", sw.ToString());
            Assert.Equal("np_subtract", KeybindPak.BoundMenuKeyInPak(pak));
            using (var z = ZipFile.OpenRead(pak))
            {
                string supa = new StreamReader(z.GetEntry(KeybindPak.SuperactionsEntry)!.Open()).ReadToEnd();
                foreach (var (a, k) in KeybindPak.DiceKeys) Assert.Equal(k, KeybindPak.BoundKey(supa, a));
                foreach (var (a, k) in KeybindPak.MenuNavKeys) Assert.Equal(k, KeybindPak.BoundKey(supa, a));
                string prof = new StreamReader(z.GetEntry(KeybindPak.ProfileEntry)!.Open()).ReadToEnd();
                foreach (string a in KeybindPak.Actions) Assert.Single(Regex.Matches(prof, $"name=\"{a}\""));
            }

            // the launcher reads the line's JSON (Home.Wo148.cs KeysPakResult)
            static JsonObject Line(StringWriter w) => (JsonObject)JsonNode.Parse(w.ToString().Trim()["KEYS-PAK ".Length..])!;

            // an unknown value: Insert, and the outcome says why (and nothing of the machine)
            File.WriteAllText(settings, "{\"MenuKey\":\"scrolllock\"}");
            sw = new StringWriter();
            Assert.Equal(0, KeybindPak.RunCli(Cli(), sw));
            Assert.Equal("14 actions in both files; menu key insert (MenuKey 'scrolllock' is not one of insert|np_add|np_subtract -- insert used)",
                         Line(sw)["detail"]!.GetValue<string>());
            Assert.Equal("insert", KeybindPak.BoundMenuKeyInPak(pak));

            // no launcher file at all: Insert, quietly
            File.Delete(settings);
            sw = new StringWriter();
            Assert.Equal(0, KeybindPak.RunCli(Cli(), sw));
            Assert.Equal("14 actions in both files; menu key insert", Line(sw)["detail"]!.GetValue<string>());
            Assert.Equal("unchanged", Line(sw)["action"]!.GetValue<string>());   // the same content: not rewritten
            Assert.Null(KeybindPak.BoundMenuKeyInPak(Path.Combine(root, "none.pak")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
