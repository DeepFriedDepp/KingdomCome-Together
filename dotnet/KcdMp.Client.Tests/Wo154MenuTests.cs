// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.Json.Nodes;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-154 Phase 7b: the mod menu's agent half -- the player's choices remembered in mod-settings.json (through
/// SettingsJson), the host's levers pushed back only when this player hosts, the session push, the menu key in the
/// launcher's settings.json. The keys pak: Wo154MenuKeysTests. The Lua half: tools/Test-WO154Synthetic.lua.
/// </summary>
public class Wo154MenuTests
{
    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "kcdmp-wo154m-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // ---------------------------------------------------------------- remembering

    [Theory]
    [InlineData("NameBadges off", "NameBadges", "off")]
    [InlineData("PingLine on", "PingLine", "on")]
    [InlineData("CleanScreen on", "CleanScreen", "on")]
    [InlineData("FriendlyFire off", "FriendlyFire", "off")]
    [InlineData("CrimeMode individual", "CrimeMode", "individual")]
    [InlineData("CrimeMode joint", "CrimeMode", "joint")]
    [InlineData("FastTravel on", "FastTravel", "on")]
    [InlineData("Leash off", "Leash", "off")]
    [InlineData("SleepVote off", "SleepVote", "off")]
    [InlineData("Whistle off", "Whistle", "off")]
    [InlineData("PartnerHerbs on", "PartnerHerbs", "on")]
    [InlineData("MenuKey np_add", "MenuKey", "np_add")]
    public void The_mods_choices_parse(string line, string key, string word)
    {
        Assert.True(Wo154MenuRules.TryParseSet(line, out string k, out string w, out JsonNode? v));
        Assert.Equal(key, k);
        Assert.Equal(word, w);
        Assert.NotNull(v);
        if (word is "on" or "off") Assert.Equal(word == "on", v!.GetValue<bool>());   // stored as JSON true/false
        else Assert.Equal(word, v!.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NameBadges")]
    [InlineData("NameBadges maybe")]
    [InlineData("NameBadges true")]
    [InlineData("CrimeMode on")]
    [InlineData("FastTravel joint")]
    [InlineData("Unknown on")]
    [InlineData("namebadges on")]
    [InlineData("NameBadges on extra")]
    [InlineData("NameBadges on\") os.exit() --")]
    [InlineData("MenuKey scrolllock")]
    [InlineData("MenuKey pause")]
    [InlineData("MenuKey f11")]
    [InlineData("MenuKey Insert")]
    public void Anything_else_from_the_log_channel_is_refused(string? line)
    {
        Assert.False(Wo154MenuRules.TryParseSet(line, out _, out _, out _));
    }

    [Fact]
    public void Choices_round_trip_through_mod_settings_json_and_the_players_other_keys_stay()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, Wo154MenuRules.ModSettingsFile);
            const string Before = "{\r\n  \"Mine\": [1, 2],\r\n  \"NameBadges\": true\r\n}\r\n";
            File.WriteAllText(path, Before);
            foreach (string line in new[] { "NameBadges off", "CrimeMode individual", "FastTravel on" })
            {
                Assert.True(Wo154MenuRules.TryParseSet(line, out string k, out _, out JsonNode? v));
                Assert.True(SettingsJson.Update(path, new[] { SettingsJson.Edit.Set(k, v) }).Ok);
            }
            string after = File.ReadAllText(path);
            Assert.StartsWith("{\r\n  \"Mine\": [1, 2],\r\n  \"NameBadges\": false,", after);   // the player's key and the layout kept
            Assert.Contains("\"CrimeMode\": \"individual\"", after);
            Assert.Contains("\"FastTravel\": true", after);
            var plan = Wo154MenuRules.RestorePlan(SettingsJson.Read(path), hosting: true);
            Assert.Equal(new[] { ("NameBadges", "off"), ("CrimeMode", "individual"), ("FastTravel", "on") }, plan);
            // the same choice again writes nothing
            var stamp = File.GetLastWriteTimeUtc(path);
            Assert.True(Wo154MenuRules.TryParseSet("FastTravel on", out string k2, out _, out JsonNode? v2));
            Assert.Equal(SettingsJson.Outcome.Unchanged, SettingsJson.Update(path, new[] { SettingsJson.Edit.Set(k2, v2) }).Outcome);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_first_choice_creates_the_mods_own_file()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, Wo154MenuRules.ModSettingsFile);
            Assert.True(Wo154MenuRules.TryParseSet("PingLine off", out string k, out _, out JsonNode? v));
            Assert.Equal(SettingsJson.Outcome.Created, SettingsJson.Update(path, new[] { SettingsJson.Edit.Set(k, v) }).Outcome);
            Assert.Equal("{\"PingLine\":false}", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static JsonObject AllSaved() => new()
    {
        ["NameBadges"] = false, ["PingLine"] = false, ["CleanScreen"] = true, ["FriendlyFire"] = false, ["CrimeMode"] = "individual",
        ["FastTravel"] = true, ["Leash"] = false, ["SleepVote"] = false, ["Whistle"] = false, ["PartnerHerbs"] = true,
    };

    [Fact]
    public void The_hosts_levers_are_pushed_only_when_this_player_hosts()
    {
        var joiner = Wo154MenuRules.RestorePlan(AllSaved(), hosting: false);
        Assert.Equal(new[] { "NameBadges", "PingLine", "CleanScreen", "SleepVote", "Whistle", "PartnerHerbs" }, joiner.Select(p => p.Key));
        var host = Wo154MenuRules.RestorePlan(AllSaved(), hosting: true);
        Assert.Equal(Wo154MenuRules.Settings.Select(s => s.Key), host.Select(p => p.Key));
        Assert.Contains(("CrimeMode", "individual"), host);
        Assert.Contains(("FastTravel", "on"), host);
        // became the host after the world's push: only the levers are still owed
        var owed = Wo154MenuRules.RestorePlan(AllSaved(), hosting: true, own: false);
        Assert.Equal(new[] { "FriendlyFire", "CrimeMode", "FastTravel", "Leash" }, owed.Select(p => p.Key));
        Assert.All(Wo154MenuRules.Settings.Where(s => s.HostOwned), s => Assert.Contains(s.Key, owed.Select(p => p.Key)));
    }

    [Fact]
    public void A_value_a_key_does_not_take_is_skipped_never_guessed()
    {
        var file = new JsonObject
        {
            ["NameBadges"] = "maybe", ["CrimeMode"] = true, ["FastTravel"] = "on", ["Leash"] = 1, ["PingLine"] = null,
            ["Whistle"] = new JsonObject { ["a"] = 1 }, ["SomethingElse"] = true,
        };
        Assert.Equal(new[] { ("FastTravel", "on") }, Wo154MenuRules.RestorePlan(file, hosting: true));   // a hand-written "on" is fine
    }

    [Fact]
    public void No_file_or_a_file_that_is_not_one_object_pushes_nothing()
    {
        Assert.Empty(Wo154MenuRules.RestorePlan(null, hosting: true));
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, Wo154MenuRules.ModSettingsFile);
            File.WriteAllText(path, "[true, false]");
            Assert.Empty(Wo154MenuRules.RestorePlan(SettingsJson.Read(path), hosting: true));
            // and a choice is never written over it
            Assert.True(Wo154MenuRules.TryParseSet("NameBadges off", out string k, out _, out JsonNode? v));
            Assert.Equal(SettingsJson.Outcome.Refused, SettingsJson.Update(path, new[] { SettingsJson.Edit.Set(k, v) }).Outcome);
            Assert.Equal("[true, false]", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Every_remembered_key_round_trips_through_the_mods_words()
    {
        foreach (var s in Wo154MenuRules.Settings)
        {
            string[] words = s.Kind == Wo154MenuRules.Kind.Crime ? new[] { "joint", "individual" } : new[] { "on", "off" };
            foreach (string w in words)
            {
                Assert.True(Wo154MenuRules.TryParseSet(s.Key + " " + w, out _, out string word, out JsonNode? v), s.Key + " " + w);
                Assert.Equal(w, Wo154MenuRules.LuaWord(s, v));
                Assert.Equal(w, word);
            }
        }
    }

    // ---------------------------------------------------------------- the calls into the mod

    [Fact]
    public void The_lua_calls_carry_the_fixed_vocabulary_only()
    {
        Assert.Equal("if KCD2MP_W154MenuRestore then KCD2MP_W154MenuRestore(\"NameBadges\", \"off\") end", Wo154MenuRules.RestoreLua("NameBadges", "off"));
        Assert.Equal("if KCD2MP_W154MenuSession then KCD2MP_W154MenuSession(\"joiner\", true, false, \"joint\", nil, \"connected\", \"0.45.0\", false, true) end",
            Wo154MenuRules.SessionLua("joiner", true, false, true, null, "connected", "0.45.0", false, true));
        Assert.Equal("if KCD2MP_W154MenuSession then KCD2MP_W154MenuSession(\"host\", false, nil, nil, true, \"starting\", \"0.45.0\", true, false) end",
            Wo154MenuRules.SessionLua("host", false, null, null, true, "waiting-for-game", "0.45.0", true, false));
        // nothing outside the vocabulary reaches the Lua text
        string evil = Wo154MenuRules.SessionLua("x\") os.exit() --", true, null, false, null, "y\")", "0.45.0\") os.exit(", false, false);
        Assert.Equal("if KCD2MP_W154MenuSession then KCD2MP_W154MenuSession(\"none\", true, nil, \"individual\", nil, \"starting\", \"?\", false, false) end", evil);
        Assert.Equal("if KCD2MP_W154MenuKeys then KCD2MP_W154MenuKeys(\"np_add\", \"np_add\", \"stored\") end", Wo154MenuRules.KeysLua("np_add", "bogus", "stored"));
        Assert.Equal("if KCD2MP_W154MenuKeys then KCD2MP_W154MenuKeys(\"insert\", \"np_subtract\", \"\") end", Wo154MenuRules.KeysLua("f9", "np_subtract", "anything"));
    }

    [Fact]
    public void The_session_push_is_a_level_statement_the_batch_queue_may_replace()
    {
        var (kind, key) = BatchPolicy.Classify(Wo154MenuRules.SessionLua("host", true, null, null, null, "connected", "0.45.0", false, false));
        Assert.Equal(BatchKind.Level, kind);
        Assert.Equal("KCD2MP_W154MenuSession|\"host\"", key);
        Assert.Equal(BatchKind.Edge, BatchPolicy.Classify(Wo154MenuRules.RestoreLua("NameBadges", "off")).Kind);   // each restore runs once
    }

    [Theory]
    [InlineData("connected", "connected")]
    [InlineData("connecting", "connecting")]
    [InlineData("failed", "failed")]
    [InlineData("waiting-for-game", "starting")]
    [InlineData("starting", "starting")]
    public void The_link_word(string state, string word) => Assert.Equal(word, Wo154MenuRules.LinkWord(state));

    [Fact]
    public void The_new_session_settings_are_appended_never_renumbered()
    {
        Assert.Equal(1, SessionSettingKey.FriendlyFire);
        Assert.Equal(2, SessionSettingKey.CrimeMode);
        Assert.Equal(3, SessionSettingKey.FastTravel);
        Assert.Equal("crime_mode", SessionSettingKey.Name(SessionSettingKey.CrimeMode));
        Assert.Equal("fast_travel", SessionSettingKey.Name(SessionSettingKey.FastTravel));
        Assert.Equal("unknown-9", SessionSettingKey.Name(9));
        Assert.False(Wo154MenuRules.FastTravelDefault);   // 0.45.0: fast travel off in a co-op session unless the host allows it
    }

    // ---------------------------------------------------------------- the menu key in the launcher's settings.json

    [Fact]
    public void The_menu_key_is_set_in_an_existing_launcher_file_and_never_creates_one()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, Wo154MenuRules.LauncherSettingsFile);
            Assert.True(Wo154MenuRules.TryParseSet("MenuKey np_add", out string k, out _, out JsonNode? v));
            Assert.Equal(SettingsJson.Outcome.Refused, SettingsJson.Update(path, new[] { SettingsJson.Edit.Set(k, v) }, createIfMissing: false).Outcome);
            Assert.False(File.Exists(path));
            Assert.Null(KeybindPak.ReadMenuKeySetting(path));
            const string Launcher = "{\"GamePath\":\"X:\\\\Games\\\\KCD2Mod\",\"DllPath\":\"KCDMP.dll\",\"VoiceChatEnabled\":true}";
            File.WriteAllText(path, Launcher);
            Assert.Equal(SettingsJson.Outcome.Written, SettingsJson.Update(path, new[] { SettingsJson.Edit.Set(k, v) }, createIfMissing: false).Outcome);
            Assert.Equal(Launcher[..^1] + ",\"MenuKey\":\"np_add\"}", File.ReadAllText(path));
            Assert.Equal("np_add", KeybindPak.ReadMenuKeySetting(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_log_line_keeps_no_folder_of_this_machine()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string msg = @"Access to the path 'X:\Games\KCDMP\mod-settings.json.tmp-1' is denied (" + home + @"\x)";
        Assert.Equal(@"Access to the path '<install>\mod-settings.json.tmp-1' is denied (<home>\x)", Wo154MenuRules.Scrub(msg, @"X:\Games\KCDMP\"));
    }
}
