// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using System.Text.Json.Nodes;
using KCDMP_launcher.Models;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-154 (Phase 6.7): voice chat is off unless the player chose it (docs/WO-154-voice-decision.md).</summary>
public class Wo154VoiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kcdmp-wo154v-" + Guid.NewGuid().ToString("N"));
    private string P(string name) => Path.Combine(_dir, name);

    public Wo154VoiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // ------------------------------------------------------------------ the rule

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]    // the old default true: not a choice
    [InlineData(false, true, false)]    // chose off
    [InlineData(true, true, true)]      // chose on
    public void Effective_is_on_only_when_the_player_chose_on(bool enabled, bool chosen, bool expected)
    {
        Assert.Equal(expected, VoiceSetting.Effective(enabled, chosen));
    }

    [Theory]
    [InlineData("{}", false)]                                                     // a new install
    [InlineData("{\"VoiceChatEnabled\":true}", false)]                            // every pre-0.45.0 file that kept the default
    [InlineData("{\"VoiceChatEnabled\":false}", false)]
    [InlineData("{\"VoiceChatEnabled\":true,\"VoiceChatChosen\":true}", true)]
    [InlineData("{\"VoiceChatEnabled\":false,\"VoiceChatChosen\":true}", false)]
    [InlineData("{\"VoiceChatEnabled\":\"true\",\"VoiceChatChosen\":\"true\"}", false)]   // not JSON bools
    [InlineData("{\"VoiceChatEnabled\":1,\"VoiceChatChosen\":1}", false)]
    [InlineData("{\"VoiceChatEnabled\":null,\"VoiceChatChosen\":true}", false)]
    public void Effective_from_a_settings_file(string json, bool expected)
    {
        Assert.Equal(expected, VoiceSetting.Effective(JsonNode.Parse(json) as JsonObject));
        Assert.False(VoiceSetting.Effective((JsonObject?)null));
    }

    [Fact]
    public void The_agent_flag_is_always_explicit()
    {
        Assert.Equal("--voice", VoiceSetting.AgentFlag(true));
        Assert.Equal("--no-voice", VoiceSetting.AgentFlag(false));
    }

    [Fact]
    public void A_choice_sets_both_keys_and_touches_nothing_else()
    {
        string f = "{\"GamePath\":\"X\",\"VoiceChatEnabled\":true,\"HostPort\":7778}";
        var r = SettingsJson.Apply(Encoding.UTF8.GetBytes(f), VoiceSetting.ChoiceEdits(false), out _, out var changed);
        Assert.Equal("{\"GamePath\":\"X\",\"VoiceChatEnabled\":false,\"HostPort\":7778,\"VoiceChatChosen\":true}", Encoding.UTF8.GetString(r!));
        Assert.Equal(new[] { "VoiceChatEnabled", "VoiceChatChosen" }, changed);
        Assert.All(VoiceSetting.ChoiceEdits(true), e => Assert.False(e.OnlyIfMissing));
    }

    // ------------------------------------------------------------------ the launcher

    [Fact]
    public void A_new_install_is_off_and_a_click_is_the_players_choice()
    {
        var s = new AppSettings();
        Assert.False(s.VoiceChatEnabled);
        Assert.False(s.VoiceChatChosen);
        Assert.False(s.VoiceChatOn);
        s.VoiceChatOn = true;
        Assert.True(s.VoiceChatEnabled && s.VoiceChatChosen && s.VoiceChatOn);
        s.VoiceChatOn = false;
        Assert.False(s.VoiceChatEnabled);
        Assert.True(s.VoiceChatChosen);   // off is a choice too
        Assert.DoesNotContain(LauncherSettingsStore.Keys, k => k.Name == nameof(AppSettings.VoiceChatOn));   // never written as such
    }

    [Fact]
    public void An_upgrade_keeps_the_stored_value_reads_it_as_off_and_fills_chosen_only_with_another_change()
    {
        string f = "{\"GamePath\":\"X\",\"HostPort\":7778,\"VoiceChatEnabled\":true,\"HostAllowSteam\":true}";
        File.WriteAllText(P("settings.json"), f);
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        Assert.True(s.VoiceChatEnabled);
        Assert.False(s.VoiceChatOn);
        Assert.Equal(SettingsJson.Outcome.Unchanged, store.Save(s).Result.Outcome);   // no write for the upgrade itself
        Assert.Equal(f, File.ReadAllText(P("settings.json")));

        s.HostPort = 7779;   // a change for another reason
        var r = store.Save(s);
        Assert.Equal(new[] { "HostPort" }, r.Set);
        Assert.Contains("VoiceChatChosen", r.Filled);
        var o = JsonNode.Parse(File.ReadAllText(P("settings.json")))!.AsObject();
        Assert.True(o["VoiceChatEnabled"]!.GetValue<bool>());      // never changed on upgrade
        Assert.False(o["VoiceChatChosen"]!.GetValue<bool>());
        Assert.False(VoiceSetting.Effective(o));
    }

    [Fact]
    public void Turning_voice_on_in_the_settings_window_is_kept_across_a_restart()
    {
        File.WriteAllText(P("settings.json"), "{\"VoiceChatEnabled\":true}");
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        s.VoiceChatOn = true;
        store.Save(s);
        var again = new LauncherSettingsStore(P("settings.json")).Load();
        Assert.True(again.VoiceChatOn);
        Assert.True(VoiceSetting.Effective(JsonNode.Parse(File.ReadAllText(P("settings.json"))) as JsonObject));
    }

    [Fact]
    public void The_settings_window_shows_what_the_mod_menu_chose()
    {
        File.WriteAllText(P("settings.json"), "{\"HostPort\":7778}");
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        Assert.False(s.VoiceChatOn);
        // the agent, from the mod menu
        Assert.Equal("updated", GameBridge.Wo154PersistVoice(P("settings.json"), true));
        var taken = store.Refresh(s);
        Assert.True(s.VoiceChatOn);
        Assert.Contains("VoiceChatChosen", taken);
        Assert.Empty(store.Changed(s));   // what the mod menu wrote is not written again
    }

    // ------------------------------------------------------------------ the agent

    [Fact]
    public void The_agent_is_off_unless_told()
    {
        Assert.False(new ClientConfig().VoiceChatEnabled);
        var on = new ClientConfig();
        on.ApplyCommandLine(new[] { "--host", "127.0.0.1", "--port", "7778", "--voice" });
        Assert.True(on.VoiceChatEnabled);
        var off = new ClientConfig { VoiceChatEnabled = true };   // an old kcdmp-client.json said true
        off.ApplyCommandLine(new[] { "--host", "127.0.0.1", "--port", "7778", "--no-voice" });
        Assert.False(off.VoiceChatEnabled);
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData(" off ", false)]
    [InlineData("", null)]
    [InlineData("yes", null)]
    [InlineData(null, null)]
    public void The_mod_menus_switch_parses(string? arg, bool? expected)
    {
        Assert.Equal(expected, GameBridge.ParseVoiceSwitch(arg));
    }

    [Fact]
    public void The_agent_records_the_choice_without_creating_a_file_and_without_touching_other_keys()
    {
        Assert.Equal("not found (not created)", GameBridge.Wo154PersistVoice(P("settings.json"), true));
        Assert.False(File.Exists(P("settings.json")));

        string f = "{\r\n  \"GamePath\": \"X\",\r\n  \"Unknown\": [1, 2],\r\n  \"VoiceChatEnabled\": true\r\n}";
        File.WriteAllText(P("settings.json"), f);
        Assert.Equal("updated", GameBridge.Wo154PersistVoice(P("settings.json"), false));
        Assert.Equal("{\r\n  \"GamePath\": \"X\",\r\n  \"Unknown\": [1, 2],\r\n  \"VoiceChatEnabled\": false,\r\n  \"VoiceChatChosen\": true\r\n}",
                     File.ReadAllText(P("settings.json")));
        Assert.Equal("had it already", GameBridge.Wo154PersistVoice(P("settings.json"), false));

        File.WriteAllText(P("settings.json"), "{broken");
        Assert.StartsWith("not written", GameBridge.Wo154PersistVoice(P("settings.json"), true));
        Assert.Equal("{broken", File.ReadAllText(P("settings.json")));
    }

    [Fact]
    public void The_voice_state_reaches_the_mod_at_the_main_menu_too()
    {
        Assert.True(GameBridge.IsMenuSafeLua("if KCD2MP_W154VoiceState then KCD2MP_W154VoiceState(true) end"));
    }
}
