// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KCDMP_launcher.Models;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-154: the launcher's settings.json, custom_servers.json and favorites.json belong to the
/// player -- a load and a save change nothing, a change touches its own key or entry only.
/// </summary>
public class Wo154SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kcdmp-wo154s-" + Guid.NewGuid().ToString("N"));
    private string P(string name) => Path.Combine(_dir, name);

    public Wo154SettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // A 0.43.0 player's file, edited by hand: CRLF and two-space indentation, a custom game
    // path, the player's name and servers under keys the launcher does not know, a comment,
    // non-ASCII text -- and every key the 0.43.0 launcher wrote.
    private const string HandEdited043 =
        "{\r\n" +
        "  // my install\r\n" +
        "  \"GamePath\": \"D:\\\\Spiele\\\\KCD2Mod\\\\Bin\\\\Win64ReleaseSteamLTO_DLL\\\\KingdomCome.exe\",\r\n" +
        "  \"PlayerName\": \"Jindřich z Skalice\",\r\n" +
        "  \"DllPath\": \"KCDMP.dll\",\r\n" +
        "  \"AgentPath\": \"KcdMpClient.exe\",\r\n" +
        "  \"InjectDelaySeconds\": 20,\r\n" +
        "  \"MasterServerUrl\": \"http://127.0.0.1:5100\",\r\n" +
        "  \"ServerInfoPort\": 5273,\r\n" +
        "  \"Servers\": [ { \"Name\": \"Friday\", \"Ip\": \"host.example.invalid\", \"Port\": 7778 } ],\r\n" +
        "  \"DiceIpcPort\": 5901,\r\n" +
        "  \"VersionIpcPort\": 5902,\r\n" +
        "  \"Language\": \"en\",\r\n" +
        "  \"RelayPath\": \"KcdMpServer.exe\",\r\n" +
        "  \"MasterServerPath\": \"MasterServer\\\\KcdMpMasterServer.exe\",\r\n" +
        "  \"HostPort\": 7778,\r\n" +
        "  \"VoiceChatEnabled\": true,\r\n" +
        "  \"Window\": { \"Maximized\": true },\r\n" +
        "  \"HostAllowSteam\": true,\r\n" +
        "  \"SteamAppId\": 2429020\r\n" +
        "}\r\n";

    // What the 0.44.0 launcher's whole-object save wrote (compact, no newline).
    private static string Compact044() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["GamePath"] = @"C:\Program Files (x86)\Steam\steamapps\common\KCD2Mod\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe",
        ["DllPath"] = "KCDMP.dll", ["AgentPath"] = "KcdMpClient.exe", ["InjectDelaySeconds"] = 20,
        ["MasterServerUrl"] = "http://127.0.0.1:5100", ["ServerInfoPort"] = 5273, ["DiceIpcPort"] = 5901,
        ["VersionIpcPort"] = 5902, ["Language"] = "en", ["RelayPath"] = "KcdMpServer.exe",
        ["MasterServerPath"] = @"MasterServer\KcdMpMasterServer.exe", ["HostPort"] = 7778,
        ["VoiceChatEnabled"] = false, ["HostAllowSteam"] = true, ["SteamAppId"] = 2429020,
    });

    private static string Default(string key)
    {
        var (_, prop) = LauncherSettingsStore.Keys.First(k => k.Name == key);
        return JsonNode.Parse(JsonSerializer.Serialize(prop.GetValue(new AppSettings()), prop.PropertyType))!.ToJsonString();
    }

    /// <summary>The keys a save adds to a file that lacks them (new settings since the file was written), in the order they are added.</summary>
    private static List<string> MissingFrom(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var have = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        return LauncherSettingsStore.Keys.Select(k => k.Name).Where(n => !have.Contains(n)).ToList();
    }

    private void Put(string name, string text) => File.WriteAllBytes(P(name), Encoding.UTF8.GetBytes(text));
    private string Get(string name) => Encoding.UTF8.GetString(File.ReadAllBytes(P(name)));

    // ------------------------------------------------------------------ settings.json

    [Fact]
    public void A_hand_edited_0_43_0_file_is_byte_identical_after_a_load_and_a_save_without_a_change()
    {
        Put("settings.json", HandEdited043);
        var stamp = File.GetLastWriteTimeUtc(P("settings.json"));
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        Assert.Equal(@"D:\Spiele\KCD2Mod\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe", s.GamePath);

        var r = store.Save(s);
        Assert.Equal(SettingsJson.Outcome.Unchanged, r.Result.Outcome);
        Assert.Equal(Encoding.UTF8.GetBytes(HandEdited043), File.ReadAllBytes(P("settings.json")));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(P("settings.json")));
    }

    [Fact]
    public void A_change_rewrites_its_own_value_only_and_adds_only_the_keys_the_file_lacks()
    {
        Put("settings.json", HandEdited043);
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        s.HostPort = 7779;
        var r = store.Save(s);
        Assert.True(r.Wrote);
        Assert.Equal(new[] { "HostPort" }, r.Set);

        // The file's own bytes with the one value changed; any setting newer than the file
        // (none in 0.43.0's set, the WO-154 ones later) appended at the end in the file's own layout.
        var missing = MissingFrom(HandEdited043);
        Assert.Equal(missing, r.Filled);
        string fills = string.Concat(missing.Select(k => $",\r\n  \"{k}\": {Default(k)}"));
        string expected = HandEdited043.Replace("\"HostPort\": 7778,", "\"HostPort\": 7779,")
                                       .Replace("\"SteamAppId\": 2429020\r\n}", "\"SteamAppId\": 2429020" + fills + "\r\n}");
        Assert.Equal(expected, Get("settings.json"));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp-*"));

        // and a second save without a change writes nothing
        var stamp = File.GetLastWriteTimeUtc(P("settings.json"));
        Assert.Equal(SettingsJson.Outcome.Unchanged, store.Save(s).Result.Outcome);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(P("settings.json")));
    }

    [Fact]
    public void The_0_44_0_compact_file_survives_a_load_and_save_and_a_change_keeps_every_other_byte()
    {
        string f = Compact044();
        Put("settings.json", f);
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        Assert.Equal(SettingsJson.Outcome.Unchanged, store.Save(s).Result.Outcome);
        Assert.Equal(f, Get("settings.json"));

        s.HostAllowSteam = false;
        Assert.True(store.Save(s).Wrote);
        string fills = string.Concat(MissingFrom(f).Select(k => $",\"{k}\":{Default(k)}"));
        Assert.Equal(f.Replace("\"HostAllowSteam\":true", "\"HostAllowSteam\":false")[..^1] + fills + "}", Get("settings.json"));
    }

    [Fact]
    public void Setups_seed_file_keeps_its_bytes_and_gains_the_change_and_the_missing_keys()
    {
        // KCDMP.iss SeedSettings: one line, CRLF, nothing but the game path.
        string seed = "{\"GamePath\":\"E:\\\\SteamLibrary\\\\steamapps\\\\common\\\\KCD2Mod\\\\Bin\\\\Win64ReleaseSteamLTO_DLL\\\\KingdomCome.exe\"}\r\n";
        Put("settings.json", seed);
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        s.HostAllowSteam = false;
        var r = store.Save(s);
        Assert.Equal(new[] { "HostAllowSteam" }, r.Set);
        string after = Get("settings.json");
        Assert.StartsWith(seed[..seed.IndexOf('}')], after);
        Assert.EndsWith("}\r\n", after);
        var o = JsonNode.Parse(after)!.AsObject();
        Assert.False(o["HostAllowSteam"]!.GetValue<bool>());
        Assert.Equal(LauncherSettingsStore.Keys.Count, o.Count);   // every setting is now in the file
    }

    [Fact]
    public void A_value_another_program_wrote_meanwhile_is_kept_by_a_save()
    {
        Put("settings.json", Compact044());
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        // the agent, from the mod menu, while the launcher is open
        SettingsJson.Update(P("settings.json"), new[] { SettingsJson.Edit.Set("VoiceChatEnabled", true), SettingsJson.Edit.Set("MenuKey", "f7") });
        s.HostPort = 7790;
        store.Save(s);
        var o = JsonNode.Parse(Get("settings.json"))!.AsObject();
        Assert.True(o["VoiceChatEnabled"]!.GetValue<bool>());
        Assert.Equal("f7", o["MenuKey"]!.GetValue<string>());
        Assert.Equal(7790, o["HostPort"]!.GetValue<int>());
    }

    [Fact]
    public void Opening_the_settings_takes_what_another_program_wrote_unless_the_player_changed_that_key()
    {
        Put("settings.json", Compact044());
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        s.HostPort = 7791;   // the player's pending edit
        SettingsJson.Update(P("settings.json"), new[] { SettingsJson.Edit.Set("VoiceChatEnabled", true), SettingsJson.Edit.Set("HostPort", 7000) });
        var taken = store.Refresh(s);
        Assert.Equal(new[] { "VoiceChatEnabled" }, taken);
        Assert.True(s.VoiceChatEnabled);
        Assert.Equal(7791, s.HostPort);
        // the taken value is not the player's change: a save writes only HostPort
        Assert.Equal(new[] { "HostPort" }, store.Save(s).Set);
        Assert.Equal(7791, JsonNode.Parse(Get("settings.json"))!["HostPort"]!.GetValue<int>());
    }

    [Fact]
    public void Cancel_puts_the_unsaved_edits_back()
    {
        Put("settings.json", Compact044());
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        s.HostPort = 1234;
        s.Language = "cs";
        Assert.Equal(new[] { "Language", "HostPort" }, store.Revert(s).OrderByDescending(k => k).ToArray());
        Assert.Equal(7778, s.HostPort);
        Assert.Equal("en", s.Language);
        Assert.Empty(store.Changed(s));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public void A_file_that_is_not_one_json_object_is_never_written_over(string text)
    {
        Put("settings.json", text);
        var store = new LauncherSettingsStore(P("settings.json"));
        var problems = new List<string>();
        var s = store.Load(problems);
        Assert.Single(problems);
        Assert.Equal(new AppSettings().HostPort, s.HostPort);
        s.HostPort = 7792;
        var r = store.Save(s);
        Assert.False(r.Ok);
        Assert.Equal(text, Get("settings.json"));
    }

    [Fact]
    public void A_value_of_the_wrong_type_keeps_its_default_in_memory_and_its_text_in_the_file()
    {
        string f = Compact044().Replace("\"HostPort\":7778", "\"HostPort\":\"7779\"");
        Put("settings.json", f);
        var store = new LauncherSettingsStore(P("settings.json"));
        var problems = new List<string>();
        var s = store.Load(problems);
        Assert.Contains(problems, p => p.StartsWith("HostPort ", StringComparison.Ordinal));
        Assert.Equal(7778, s.HostPort);
        Assert.Equal(@"C:\Program Files (x86)\Steam\steamapps\common\KCD2Mod\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe", s.GamePath);   // one bad value no longer resets every setting
        s.Language = "de";
        store.Save(s);
        Assert.Contains("\"HostPort\":\"7779\"", Get("settings.json"));
    }

    [Fact]
    public void The_launchers_own_corrections_are_never_written_for_it()
    {
        // a stale default the launcher migrates in memory
        string f = Compact044().Replace("http://127.0.0.1:5100", "http://localhost:5000/servers/servers_list");
        Put("settings.json", f);
        var store = new LauncherSettingsStore(P("settings.json"), s => { if (s.MasterServerUrl.StartsWith("http://localhost:5000", StringComparison.Ordinal)) s.MasterServerUrl = "http://127.0.0.1:5100"; });
        var s = store.Load();
        Assert.Equal("http://127.0.0.1:5100", s.MasterServerUrl);
        Assert.Empty(store.Changed(s));

        // a game path setup found while the saved one does not work: held for this run only
        s.GamePath = @"F:\Other\KingdomCome.exe";
        store.Accept(s, nameof(AppSettings.GamePath));
        s.HostPort = 7793;
        Assert.Equal(new[] { "HostPort" }, store.Save(s).Set);
        Assert.Empty(store.Refresh(s));   // the file did not change: the correction stays in memory
        Assert.Equal(@"F:\Other\KingdomCome.exe", s.GamePath);
        string after = Get("settings.json");
        Assert.Contains("http://localhost:5000/servers/servers_list", after);
        Assert.Contains(@"C:\\Program Files (x86)", after);
    }

    [Fact]
    public void An_unset_game_path_is_told_apart_from_one_the_player_set()
    {
        var store = new LauncherSettingsStore(P("settings.json"));
        Assert.True(store.IsUnsetOnDisk("GamePath"));                       // no file
        Put("settings.json", "{\"HostPort\":7778}");
        Assert.True(store.IsUnsetOnDisk("GamePath"));                       // no key
        Put("settings.json", "{\"GamePath\":\"\"}");
        Assert.True(store.IsUnsetOnDisk("GamePath"));                       // the never-set value
        Put("settings.json", "{\"GamePath\":null}");
        Assert.True(store.IsUnsetOnDisk("GamePath"));
        Put("settings.json", "{\"GamePath\":\"X:\\\\gone\\\\KingdomCome.exe\"}");
        Assert.False(store.IsUnsetOnDisk("GamePath"));                      // the player's, even if it no longer works
        Put("settings.json", "{broken");
        Assert.False(store.IsUnsetOnDisk("GamePath"));                      // unreadable: never written over
    }

    [Fact]
    public void An_empty_game_path_is_filled_as_that_one_key()
    {
        string f = Compact044().Replace(@"C:\\Program Files (x86)\\Steam\\steamapps\\common\\KCD2Mod\\Bin\\Win64ReleaseSteamLTO_DLL\\KingdomCome.exe", "");
        Assert.Contains("\"GamePath\":\"\"", f);
        Put("settings.json", f);
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        s.GamePath = @"G:\KCD2Mod\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe";
        Assert.Equal(new[] { "GamePath" }, store.Save(s, new[] { "GamePath" }).Set);
        string fills = string.Concat(MissingFrom(f).Select(k => $",\"{k}\":{Default(k)}"));
        Assert.Equal(f.Replace("\"GamePath\":\"\"", "\"GamePath\":\"G:\\\\KCD2Mod\\\\Bin\\\\Win64ReleaseSteamLTO_DLL\\\\KingdomCome.exe\"")[..^1] + fills + "}",
                     Get("settings.json"));
    }

    [Fact]
    public void A_scoped_save_writes_its_key_only_and_leaves_other_pending_edits_pending()
    {
        Put("settings.json", Compact044());
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        s.HostPort = 9000;           // an edit in the settings window, not saved
        s.HostAllowSteam = false;    // the Host window's switch, saved at once
        Assert.Equal(new[] { "HostAllowSteam" }, store.Save(s, new[] { "HostAllowSteam" }).Set);
        Assert.Contains("\"HostPort\":7778", Get("settings.json"));
        Assert.Equal(new[] { "HostPort" }, store.Changed(s));
    }

    [Fact]
    public void No_file_no_change_nothing_written_and_a_change_starts_the_file_with_every_key()
    {
        var store = new LauncherSettingsStore(P("settings.json"));
        var s = store.Load();
        Assert.Equal(SettingsJson.Outcome.Unchanged, store.Save(s).Result.Outcome);
        Assert.False(File.Exists(P("settings.json")));
        s.HostPort = 7794;
        Assert.Equal(SettingsJson.Outcome.Created, store.Save(s).Result.Outcome);
        var o = JsonNode.Parse(Get("settings.json"))!.AsObject();
        Assert.Equal(LauncherSettingsStore.Keys.Count, o.Count);
        Assert.Equal(7794, o["HostPort"]!.GetValue<int>());
        Assert.False(o.ContainsKey("LastSteamCode"));   // this run only, never written
    }

    // ------------------------------------------------------------------ favorites.json, custom_servers.json

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[]? b) => b is null ? "(null)" : Encoding.UTF8.GetString(b);

    [Fact]
    public void A_star_touches_its_own_address_only()
    {
        byte[] f = B("[\"10.0.0.2\",\"host.example.invalid\",42]");
        Assert.Equal("[\"10.0.0.2\",\"host.example.invalid\",42,\"10.0.0.9\"]", S(PlayerListFiles.ApplyFavorite(f, "10.0.0.9", true, out _)));
        Assert.Equal("[\"10.0.0.2\",42]", S(PlayerListFiles.ApplyFavorite(f, "host.example.invalid", false, out _)));
        Assert.Null(PlayerListFiles.ApplyFavorite(f, "10.0.0.2", true, out _));     // starred already
        Assert.Null(PlayerListFiles.ApplyFavorite(f, "10.0.0.7", false, out _));    // not starred
        Assert.Equal("[\"10.0.0.9\"]", S(PlayerListFiles.ApplyFavorite(null, "10.0.0.9", true, out _)));   // no file yet
        Assert.Null(PlayerListFiles.ApplyFavorite(B("{\"a\":1}"), "10.0.0.9", true, out var why));
        Assert.StartsWith("not one JSON array", why);
    }

    private const string Servers =
        "[{\"Token\":null,\"Name\":\"Friday\",\"Ip\":\"host.example.invalid\",\"Port\":7778,\"InfoPort\":0,\"MapName\":\"Kuttenberg\",\"Players\":1,\"MaxPlayers\":64,\"Ping\":31,\"IsOnline\":true,\"Note\":\"the player's own\"}," +
        "{\"Name\":\"LAN\",\"Ip\":\"192.0.2.10\",\"Port\":7778}]";

    [Fact]
    public void A_server_edit_changes_the_fields_the_window_has_and_keeps_the_rest_of_the_entry()
    {
        var now = new ServerInfo { Name = "Friday night", Ip = "host2.example.invalid", Port = 7780, MapName = "Kuttenberg", Ping = 5, Players = 9 };
        string after = S(PlayerListFiles.ApplyServerEdit(B(Servers), "host.example.invalid", 7778, now, out _));
        var a = JsonNode.Parse(after)!.AsArray();
        Assert.Equal("Friday night", a[0]!["Name"]!.GetValue<string>());
        Assert.Equal("host2.example.invalid", a[0]!["Ip"]!.GetValue<string>());
        Assert.Equal(7780, a[0]!["Port"]!.GetValue<int>());
        Assert.Equal("the player's own", a[0]!["Note"]!.GetValue<string>());   // a field the launcher does not know
        Assert.Equal(31, a[0]!["Ping"]!.GetValue<int>());                       // the live values are not written
        Assert.Equal(JsonNode.Parse(Servers)!.AsArray()[1]!.ToJsonString(), a[1]!.ToJsonString());
        // an edit that changes nothing writes nothing
        var same = new ServerInfo { Name = "LAN", Ip = "192.0.2.10", Port = 7778, MapName = "" };
        var withMap = S(PlayerListFiles.ApplyServerEdit(B(Servers), "192.0.2.10", 7778, same, out _));
        Assert.Contains("\"MapName\":\"\"", withMap);   // the map field the window has is set
        Assert.Null(PlayerListFiles.ApplyServerEdit(B(withMap), "192.0.2.10", 7778, same, out _));
    }

    [Fact]
    public void A_server_added_or_removed_touches_its_own_entry_only()
    {
        var added = PlayerListFiles.ApplyServerAdd(B(Servers), new ServerInfo { Name = "New", Ip = "203.0.113.5", Port = 7778 }, out _);
        var a = JsonNode.Parse(S(added))!.AsArray();
        Assert.Equal(3, a.Count);
        Assert.Equal("the player's own", a[0]!["Note"]!.GetValue<string>());
        Assert.False(a[2]!.AsObject().ContainsKey("SteamCode"));
        Assert.Null(PlayerListFiles.ApplyServerAdd(B(Servers), new ServerInfo { Ip = "192.0.2.10", Port = 7778 }, out _));

        var removed = S(PlayerListFiles.ApplyServerRemove(B(Servers), "192.0.2.10", 7778, out _));
        Assert.Equal(JsonNode.Parse(Servers)!.AsArray()[0]!.ToJsonString(), JsonNode.Parse(removed)!.AsArray().Single()!.ToJsonString());
        Assert.Null(PlayerListFiles.ApplyServerRemove(B(Servers), "192.0.2.99", 7778, out _));
    }

    [Fact]
    public void An_indented_list_stays_indented_and_a_bom_stays()
    {
        byte[] f = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(B("[\r\n  \"10.0.0.2\"\r\n]")).ToArray();
        var r = PlayerListFiles.ApplyFavorite(f, "10.0.0.3", true, out _)!;
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, r.Take(3).ToArray());
        string t = Encoding.UTF8.GetString(r, 3, r.Length - 3);
        Assert.Contains("\n", t);
        Assert.Equal(new[] { "10.0.0.2", "10.0.0.3" }, JsonNode.Parse(t)!.AsArray().Select(n => n!.GetValue<string>()).ToArray());
    }

    [Fact]
    public void On_disk_a_list_is_written_atomically_only_when_it_changes_and_never_over_a_broken_file()
    {
        string p = P("favorites.json");
        Assert.Equal(SettingsJson.Outcome.Created, PlayerListFiles.Update(p, (byte[]? c, out string w) => PlayerListFiles.ApplyFavorite(c, "10.0.0.2", true, out w)).Outcome);
        var stamp = File.GetLastWriteTimeUtc(p);
        Assert.Equal(SettingsJson.Outcome.Unchanged, PlayerListFiles.Update(p, (byte[]? c, out string w) => PlayerListFiles.ApplyFavorite(c, "10.0.0.2", true, out w)).Outcome);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(p));
        Assert.Equal(SettingsJson.Outcome.Written, PlayerListFiles.Update(p, (byte[]? c, out string w) => PlayerListFiles.ApplyFavorite(c, "10.0.0.3", true, out w)).Outcome);
        Assert.Equal("[\"10.0.0.2\",\"10.0.0.3\"]", Get("favorites.json"));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp-*"));

        Put("custom_servers.json", "[{\"Name\":\"x\",");
        Assert.Equal(SettingsJson.Outcome.Refused, PlayerListFiles.Update(P("custom_servers.json"),
            (byte[]? c, out string w) => PlayerListFiles.ApplyServerAdd(c, new ServerInfo { Ip = "203.0.113.5", Port = 7778 }, out w)).Outcome);
        Assert.Equal("[{\"Name\":\"x\",", Get("custom_servers.json"));
    }
}
