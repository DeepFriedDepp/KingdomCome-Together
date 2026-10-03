// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using System.Text.Json.Nodes;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-154: the player's settings file is edited key by key and never rewritten.</summary>
public class SettingsJsonTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

    // a 0.43.0 launcher's file, compact, with the player's own servers and an unknown key
    private const string Compact =
        "{\"GamePath\":\"X:\\\\Games\\\\KCD2Mod\\\\Bin\\\\KingdomCome.exe\",\"DllPath\":\"KCDMP.dll\",\"HostPort\":7778," +
        "\"VoiceChatEnabled\":true,\"Servers\":[{\"Name\":\"Ours\",\"Ip\":\"example.invalid\",\"Port\":7778}],\"SomethingNew\":{\"a\":1}}";

    [Fact]
    public void Nothing_changes_nothing_is_written()
    {
        var r = SettingsJson.Apply(B(Compact), new[] { SettingsJson.Edit.Set("HostPort", 7778), SettingsJson.Edit.Fill("DllPath", "other.dll") }, out var why, out var changed);
        Assert.Null(r);
        Assert.Equal("nothing to change", why);
        Assert.Empty(changed);
    }

    [Fact]
    public void A_missing_key_is_appended_and_every_other_byte_is_kept()
    {
        var r = SettingsJson.Apply(B(Compact), new[] { SettingsJson.Edit.Fill("VoiceChatChosen", false) }, out _, out var changed);
        Assert.NotNull(r);
        string t = S(r!);
        Assert.Equal(Compact[..^1] + ",\"VoiceChatChosen\":false}", t);
        Assert.Equal(new[] { "VoiceChatChosen" }, changed);
    }

    [Fact]
    public void A_fill_never_replaces_a_value_that_is_there()
    {
        var r = SettingsJson.Apply(B(Compact), new[] { SettingsJson.Edit.Fill("VoiceChatEnabled", false) }, out var why, out _);
        Assert.Null(r);
        Assert.Equal("nothing to change", why);
    }

    [Fact]
    public void A_set_replaces_exactly_its_own_value()
    {
        var r = SettingsJson.Apply(B(Compact), new[] { SettingsJson.Edit.Set("VoiceChatEnabled", false) }, out _, out var changed);
        Assert.Equal(Compact.Replace("\"VoiceChatEnabled\":true", "\"VoiceChatEnabled\":false"), S(r!));
        Assert.Equal(new[] { "VoiceChatEnabled" }, changed);
    }

    [Fact]
    public void An_indented_hand_edited_file_keeps_its_layout_and_the_new_key_follows_it()
    {
        string f = "{\r\n  \"Name\": \"A player\",\r\n  \"Custom\": [ 1, 2 ],\r\n  \"HostPort\": 7779\r\n}\r\n";
        var r = SettingsJson.Apply(B(f), new[] { SettingsJson.Edit.Set("HostPort", 7780), SettingsJson.Edit.Fill("MenuKey", "insert") }, out _, out _);
        Assert.Equal("{\r\n  \"Name\": \"A player\",\r\n  \"Custom\": [ 1, 2 ],\r\n  \"HostPort\": 7780,\r\n  \"MenuKey\": \"insert\"\r\n}\r\n", S(r!));
    }

    [Fact]
    public void A_byte_order_mark_stays()
    {
        byte[] f = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(B("{\"A\":1}")).ToArray();
        var r = SettingsJson.Apply(f, new[] { SettingsJson.Edit.Fill("B", 2) }, out _, out _);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, r!.Take(3).ToArray());
        Assert.Equal("{\"A\":1,\"B\":2}", S(r!.Skip(3).ToArray()));
    }

    [Fact]
    public void An_empty_object_gets_its_first_key_without_a_comma()
    {
        var r = SettingsJson.Apply(B("{}"), new[] { SettingsJson.Edit.Fill("A", true) }, out _, out _);
        Assert.Equal("{\"A\":true}", S(r!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"A\":1")]
    [InlineData("{\"A\":1} trailing")]
    public void A_file_that_is_not_one_json_object_is_never_written_over(string text)
    {
        var r = SettingsJson.Apply(B(text), new[] { SettingsJson.Edit.Set("A", 2) }, out var why, out _);
        Assert.Null(r);
        Assert.NotEqual("nothing to change", why);
    }

    [Fact]
    public void Nested_values_of_other_keys_are_untouched_and_a_nested_value_can_be_set()
    {
        var r = SettingsJson.Apply(B(Compact), new[] { SettingsJson.Edit.Set("SomethingNew", new JsonObject { ["a"] = 2 }) }, out _, out _);
        Assert.Equal(Compact.Replace("\"SomethingNew\":{\"a\":1}", "\"SomethingNew\":{\"a\":2}"), S(r!));
    }

    [Fact]
    public void Update_on_disk_unchanged_written_created_and_refused()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kcdmp-wo154-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string p = Path.Combine(dir, "settings.json");
            File.WriteAllText(p, Compact);
            var stamp = File.GetLastWriteTimeUtc(p);
            Assert.Equal(SettingsJson.Outcome.Unchanged, SettingsJson.Update(p, new[] { SettingsJson.Edit.Fill("HostPort", 1) }).Outcome);
            Assert.Equal(Compact, File.ReadAllText(p));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(p));

            var w = SettingsJson.Update(p, new[] { SettingsJson.Edit.Fill("VoiceChatChosen", false) });
            Assert.Equal(SettingsJson.Outcome.Written, w.Outcome);
            Assert.Equal(Compact[..^1] + ",\"VoiceChatChosen\":false}", File.ReadAllText(p));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));

            string q = Path.Combine(dir, "mod-settings.json");
            Assert.Equal(SettingsJson.Outcome.Created, SettingsJson.Update(q, new[] { SettingsJson.Edit.Set("NameBadges", true) }).Outcome);
            Assert.Equal("{\"NameBadges\":true}", File.ReadAllText(q));
            Assert.Equal(SettingsJson.Outcome.Refused, SettingsJson.Update(Path.Combine(dir, "none.json"), new[] { SettingsJson.Edit.Set("A", 1) }, createIfMissing: false).Outcome);

            File.WriteAllText(p, "{broken");
            Assert.Equal(SettingsJson.Outcome.Refused, SettingsJson.Update(p, new[] { SettingsJson.Edit.Set("A", 1) }).Outcome);
            Assert.Equal("{broken", File.ReadAllText(p));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Read_returns_the_root_object_or_null()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kcdmp-wo154-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string p = Path.Combine(dir, "settings.json");
            File.WriteAllText(p, Compact);
            Assert.True(SettingsJson.Read(p)!["VoiceChatEnabled"]!.GetValue<bool>());
            Assert.Null(SettingsJson.Read(Path.Combine(dir, "none.json")));
            File.WriteAllText(p, "{broken");
            Assert.Null(SettingsJson.Read(p));
        }
        finally { Directory.Delete(dir, true); }
    }
}
