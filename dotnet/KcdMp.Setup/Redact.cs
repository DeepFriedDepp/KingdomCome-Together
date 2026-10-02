// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdMp.Setup;

/// <summary>
/// Everything setup writes to the launcher log goes through here first, so
/// neither app*.log nor a Report-a-bug zip carries anything personal.
///
/// Warhorse's tool and the Steam API print the player's identity:
/// "SteamInternal_SetMinidumpSteamID: Caching Steam ID: &lt;17 digits&gt;" and
/// "Game installed in: &lt;full path&gt;". Removed here: Steam IDs (64-bit,
/// [U:1:n], steamid:n), the Windows profile path, the account and machine
/// names. Steam library paths stay (they are what a setup report is about,
/// and "D:\SteamLibrary" names nobody) -- with any profile path or user name
/// inside them replaced like everywhere else.
/// </summary>
public static partial class Redact
{
    [GeneratedRegex(@"(?i)steamid:\s*\d+|\[U:\d:\d+\]|\b\d{15,20}\b")]
    private static partial Regex SteamIds();

    // Any Windows profile, not just this account's: C:\Users\<name>\...  or  C:/Users/<name>/...
    [GeneratedRegex(@"(?i)\b([A-Z]):[\\/]+Users[\\/]+(?!Public\b|Default\b)[^\\/:*?""<>|\r\n]+")]
    private static partial Regex ProfilePaths();

    private static readonly string Profile = Safe(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    private static readonly string UserName = Safe(() => Environment.UserName);
    private static readonly string MachineName = Safe(() => Environment.MachineName);

    public static string Text(string? text) => Text(text, UserName, MachineName, Profile);

    /// <summary>The testable core: the names to remove are parameters, not this machine's.</summary>
    public static string Text(string? text, string? userName, string? machineName, string? profile)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = SteamIds().Replace(text, "<steam-id>");
        if (!string.IsNullOrEmpty(profile) && profile.Length > 3)
        {
            s = s.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            s = s.Replace(profile.Replace('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        s = ProfilePaths().Replace(s, m => $"{m.Groups[1].Value}:\\Users\\<user>");
        // the machine first: it often contains the user name ("ALEX-DESKTOP")
        s = Word(s, machineName, "<pc>");
        s = Word(s, userName, "<user>");
        return s;
    }

    private static string Word(string s, string? word, string replacement)
    {
        if (string.IsNullOrEmpty(word) || word.Length < 3) return s;
        return Regex.Replace(s, @"(?<![A-Za-z0-9])" + Regex.Escape(word) + @"(?![A-Za-z0-9])", replacement, RegexOptions.IgnoreCase);
    }

    private static string Safe(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }
}
