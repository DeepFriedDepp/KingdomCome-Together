// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KCDMP_launcher.Models;
using W = KCDMP_launcher.Models.MenuTakeoverRule.WorldEntry;

namespace KcdMp.Client.Tests;

/// <summary>WO-159: the launcher's half of the main menu -- the menu's choices read back, the model it pushes, the joiner's words.</summary>
public class Wo159MenuTests
{
    [Theory]
    [InlineData("[KCD2-MP-EVT] v1 12 w159 newadv", "newadv", -1, "", "")]
    [InlineData("<12:01:02> [KCD2-MP-EVT] v1 13 w159 load 2 autosave118", "load", 2, "autosave118", "")]
    [InlineData("[KCD2-MP-EVT] v1 14 w159 join fresh", "join", -1, "", "fresh")]
    [InlineData("[KCD2-MP-EVT] v1 15 w159 join bring", "join", -1, "", "bring")]
    [InlineData("[KCD2-MP-EVT] v1 16 w159 cancel", "cancel", -1, "", "")]
    public void The_menus_choices_are_read_from_its_log_line(string line, string kind, int pl, string name, string join)
    {
        var c = MenuTakeoverRule.Parse(line);
        Assert.NotNull(c);
        Assert.Equal((kind, pl, name, join), (c!.Kind, c.Playline, c.Name, c.Join));
    }

    [Theory]
    [InlineData("[KCD2-MP-EVT] v1 12 w158 newadv")]                     // another event
    [InlineData("[KCD2-MP-EVT] v1 x w159 newadv")]                      // no sequence number
    [InlineData("[KCD2-MP-EVT] v1 12 w159 load 5 autosave118")]         // only playlines 0-4 exist
    [InlineData("[KCD2-MP-EVT] v1 12 w159 load 2 ..\\..\\x")]           // not a save name
    [InlineData("[KCD2-MP-EVT] v1 12 w159 load 2 a;quit")]
    [InlineData("[KCD2-MP-EVT] v1 12 w159 join somebody")]
    [InlineData("[KCD2-MP-EVT] v1 12 w159 lo")]                         // a line cut short
    [InlineData("[KCD2-MP] WO159-MENU chose newadv")]                   // the human-readable line is not the event
    public void Anything_else_is_not_a_choice(string line) => Assert.Null(MenuTakeoverRule.Parse(line));

    [Fact]
    public void Lua_strings_are_escaped_and_ascii_only()
    {
        Assert.Equal("\"a \\\"b\\\" \\\\ c\"", MenuTakeoverRule.Lua("a \"b\" \\ c"));
        Assert.Equal("\"x y z\"", MenuTakeoverRule.Lua("x\ny—z"));   // a newline and a dash the menu font may not have
    }

    private static readonly List<W> Five = Enumerable.Range(0, 7).Select(i => new W(i % 5, $"autosave{i:000}", $"Playline {i % 5} (Oct 0{i} 14:02) and a very long label that runs on")).ToList();

    [Fact]
    public void The_model_fits_one_console_call_and_redraws_only_on_change()
    {
        string m = MenuTakeoverRule.ModelCall(true, Five, "Not shown: 3 from the regular game (not the Modding Tools); 2 are copies of a host's world you joined; 1 still in the prologue.",
            new W(3, "permanent002", ""), "", "idle", true, "");
        Assert.True(Uri.EscapeDataString("#" + m).Length <= MenuTakeoverRule.MaxEncoded, $"{Uri.EscapeDataString(m).Length} encoded characters");
        Assert.Equal(5, m.Split("{pl=").Length - 2);   // five worlds + the new adventure
        Assert.StartsWith("if KCD2MP_W159Model then KCD2MP_W159Model({sig=\"", m);
        Assert.Contains("newadv={pl=3,name=\"permanent002\"}", m);
        Assert.DoesNotContain("runs on", m);   // labels cut to 48
        string again = MenuTakeoverRule.ModelCall(true, Five, "Not shown: 3 from the regular game (not the Modding Tools); 2 are copies of a host's world you joined; 1 still in the prologue.",
            new W(3, "permanent002", ""), "", "idle", true, "");
        Assert.Equal(m, again);
        string joining = MenuTakeoverRule.ModelCall(false, [], "", null, "No start save is installed (run Setup again).", "waiting", false, "x");
        Assert.Contains("role=\"join\"", joining);
        Assert.DoesNotContain("newadv=", joining);
        Assert.Contains("join={state=\"waiting\",bring=false", joining);
        Assert.NotEqual(Sig(m), Sig(joining));
    }

    private static string Sig(string call) => call.Split("sig=\"")[1][..12];

    [Fact]
    public void The_joiners_choice_names_the_real_difference()
    {
        Assert.Equal((true, ""), MenuTakeoverRule.JoinChoice(1, 0, 0));
        Assert.Contains("regular game, not the Modding Tools", MenuTakeoverRule.JoinChoice(0, 2, 0).Msg);
        Assert.Contains("copies of a host's world", MenuTakeoverRule.JoinChoice(0, 0, 1).Msg);
        Assert.Contains("copies of a host's world", MenuTakeoverRule.JoinChoice(0, 3, 1).Msg);
        Assert.Contains("no Modding Tools save of your own yet", MenuTakeoverRule.JoinChoice(0, 0, 0).Msg);
        Assert.All(new[] { (0, 2, 0), (0, 0, 1), (0, 0, 0) }, t => Assert.EndsWith("Join with a new character.", MenuTakeoverRule.JoinChoice(t.Item1, t.Item2, t.Item3).Msg));
    }

    [Fact]
    public void A_load_disarms_the_menu_and_uses_the_WO124_call()
    {
        Assert.Equal("if KCD2MP_W159Disarm then KCD2MP_W159Disarm(\"load\") end; if KCD2MP_Wo124LoadGame then KCD2MP_Wo124LoadGame(3,\"permanent002\",\"w159\") end",
            MenuTakeoverRule.LoadCall(3, "permanent002"));
        Assert.True(MenuTakeoverRule.IsSaveName("quicksave030"));
        Assert.False(MenuTakeoverRule.IsSaveName("quick save"));
        Assert.False(MenuTakeoverRule.IsSaveName(""));
    }

    [Fact]
    public void A_missing_settings_key_means_the_menu_is_on()
    {
        var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"GamePath\":\"x\"}")!;
        Assert.True(s.MenuTakeover);
        Assert.Equal("start-save", s.StartSavePath);
    }
}
