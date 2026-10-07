// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Steam;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-157: the first public-beta patch (agent half).</summary>
public class Wo157Tests
{
    private const ulong SomeAccount = 0x0110000100000000UL | 0xFFFFFFF0u;   // synthetic, as Wo127Tests

    // ------------------------------------------------------------ 2.5 a pasted Steam code

    public static IEnumerable<object[]> Pastes()
    {
        string c = SteamJoinCode.Encode(SomeAccount, SteamApps.ModdingTools);   // "ABCD-EFG"
        string bare = c.Replace("-", "");
        yield return new object[] { c.ToLowerInvariant() };
        yield return new object[] { $"my code is {c}!" };
        yield return new object[] { $"Code: \"{c}\"" };
        yield return new object[] { c.Replace("-", "–") };              // a chat program's long dash
        yield return new object[] { c.Replace("-", " ") };              // a no-break space
        yield return new object[] { "​" + c + "​" };               // invisible spaces around it
        yield return new object[] { $"{bare[..4]} {bare[4..]}" };
        yield return new object[] { $"here:\r\n{c}\r\nthanks" };
        yield return new object[] { $"`{c}`" };
    }

    [Theory]
    [MemberData(nameof(Pastes))]
    public void A_pasted_code_with_the_mistakes_the_format_allows_decodes(string pasted)
    {
        Assert.True(SteamJoinCode.TryParse(pasted, out ulong id, out uint app), pasted);
        Assert.Equal((SomeAccount, SteamApps.ModdingTools), (id, app));
        Assert.Equal(SteamJoinCode.Encode(SomeAccount, SteamApps.ModdingTools), SteamJoinCode.Normalize(pasted));
    }

    [Fact]
    public void A_pasted_code_for_another_app_keeps_its_letter()
    {
        string c = SteamJoinCode.Encode(SomeAccount, SteamApps.Retail);   // "ABCD-EFG-R"
        Assert.True(SteamJoinCode.TryParse($"join me: {c} (retail)", out ulong id, out uint app));
        Assert.Equal((SomeAccount, SteamApps.Retail), (id, app));
    }

    [Theory]
    [InlineData("hello there, how are you")]
    [InlineData("76561198000000000")]            // a profile number
    [InlineData("12345678")]                     // Steam's own friend code (digits)
    [InlineData("ABCD-EFG")]                     // check bits wrong
    public void Text_that_only_looks_like_a_code_does_not_decode(string text)
    {
        Assert.False(SteamJoinCode.TryParse(text, out _, out _));
        Assert.Null(SteamJoinCode.Normalize(text));
    }

    [Fact]
    public void The_bad_code_message_says_what_a_code_looks_like_and_where_the_host_finds_it()
    {
        var e = PlainConnectionError.For(ConnectionTrouble.BadCode);
        Assert.Contains("7 letters and digits, like ABCD-EFG", e.Sentence);
        Assert.Contains("HOST GAME", e.NextStep);
        Assert.Contains("Your code", e.NextStep);
    }

    // ------------------------------------------------------------ 1.2 a returning joiner's kept record

    [Fact]
    public void A_returning_joiners_own_trespass_reports_leave_his_record_and_every_other_crime_stays()
    {
        var rec = new JoinerCrimeRecord();
        rec.Add(new JoinerCrimeRecord.Crime { Id = 7, Kind = "trespass", Settlement = "tachov", Witnesses = 3, Guards = 1 });   // his machine's report (a shop)
        rec.Add(new JoinerCrimeRecord.Crime { Id = 0, Kind = "assault", Settlement = "tachov", Witnesses = 2 });                // seen by the host's world
        rec.Add(new JoinerCrimeRecord.Crime { Id = 9, Kind = "theft", Settlement = "tachov", Witnesses = 1 });                  // a theft he reported
        rec.Add(new JoinerCrimeRecord.Crime { Id = 0, Kind = "trespass", Settlement = "kuttenberg", Witnesses = 1 });          // the host's own detection
        Assert.Equal(4, rec.Count);
        Assert.Equal(1, rec.DropReportedTrespasses());
        Assert.Equal(3, rec.Count);
        Assert.DoesNotContain(rec.Open, c => c.Kind == "trespass" && c.Id != 0);
        Assert.Contains(rec.Open, c => c.Kind == "assault");
        Assert.Contains(rec.Open, c => c.Kind == "theft");
        Assert.Contains(rec.Open, c => c.Kind == "trespass" && c.Id == 0);
        Assert.Equal(0, rec.DropReportedTrespasses());
    }
}
