// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Steam;

/// <summary>
/// WO-127: the Steam app ids the launcher offers (Settings, advanced). Both
/// players must use the same one: Steam only connects two processes, and only
/// shows a friend's rich presence, under one app id.
/// </summary>
public static class SteamApps
{
    /// <summary>KCD2 Modding Tools: the app id the game itself runs as (WO-120).</summary>
    public const uint ModdingTools = 2429020;
    /// <summary>Valve's public test app ("Spacewar").</summary>
    public const uint Spacewar = 480;
    /// <summary>Retail KCD2.</summary>
    public const uint Retail = 1771300;

    /// <summary>The launcher's default until the maintainer decides after the WO-128 test.</summary>
    public const uint Default = ModdingTools;

    public static readonly uint[] Offered = [ModdingTools, Spacewar, Retail];

    /// <summary>The virtual port the relay listens on and the agent dials (P2P ports are per app, not per machine).</summary>
    public const int RelayVirtualPort = 7778;

    /// <summary>Rich presence key the hosting relay sets: "host;&lt;release&gt;".</summary>
    public const string PresenceKey = "kcdmp";

    public static bool IsOffered(uint app) => Array.IndexOf(Offered, app) >= 0;

    public static string Name(uint app) => app switch
    {
        ModdingTools => "KCD2 Modding Tools (2429020)",
        Spacewar => "Spacewar (480)",
        Retail => "KCD2 retail (1771300)",
        _ => $"app {app}",
    };

    /// <summary>The letter a join code carries for a non-default app (none for the game's own).</summary>
    internal static char? Suffix(uint app) => app switch { Spacewar => 'S', Retail => 'R', _ => null };

    internal static uint? FromSuffix(char c) => char.ToUpperInvariant(c) switch { 'S' => Spacewar, 'R' => Retail, _ => null };
}

/// <summary>
/// WO-127: the code the host reads out. The WO-120 friend code ("ABCD-EFG",
/// 7 chars) when the host runs under the game's own app id; one more letter
/// ("ABCD-EFG-S") when it runs under another, so a joiner set to a different
/// app id gets a plain "app setting doesn't match" instead of a silent
/// 20-second timeout. Never logged (<see cref="FriendCode.Redact"/>).
/// </summary>
public static class SteamJoinCode
{
    public static string Encode(ulong steamId64, uint appId)
    {
        string code = FriendCode.Encode(steamId64);
        return SteamApps.Suffix(appId) is char c ? $"{code}-{c}" : code;
    }

    /// <summary>
    /// Parses a typed code. <paramref name="appId"/> is the host's app id as
    /// the code states it (the default when it has no letter).
    ///
    /// WO-157: forgiving of paste mistakes the format allows ("Steam code does not decode" three times in
    /// one zip): any case, spaces and dashes of any kind (also the long dashes and invisible spaces chat
    /// programs insert), quotes around it, and surrounding text ("my code is ABCD-EFG!"). The check bits
    /// still decide: a word that only looks like a code does not decode.
    /// </summary>
    public static bool TryParse(string? text, out ulong steamId64, out uint appId)
    {
        steamId64 = 0;
        appId = SteamApps.ModdingTools;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string clean = Clean(text);
        if (TryParseExact(clean, out steamId64, out appId)) return true;
        // Surrounding text: each code-shaped run of letters and digits, dashed ones first.
        foreach (var m in Candidates.Matches(clean).Cast<System.Text.RegularExpressions.Match>()
                     .OrderByDescending(m => m.Value.Contains('-')))
            if (TryParseExact(m.Value, out steamId64, out appId)) return true;
        steamId64 = 0;
        appId = SteamApps.ModdingTools;
        return false;
    }

    /// <summary>The code as the host's launcher shows it ("ABCD-EFG", "ABCD-EFG-S"), or null when it does not decode.</summary>
    public static string? Normalize(string? text) =>
        TryParse(text, out ulong id, out uint app) ? Encode(id, app) : null;

    private static readonly System.Text.RegularExpressions.Regex Candidates = new(
        @"(?<![0-9A-Za-z])(?<![0-9A-Za-z]-)[0-9A-Za-z]{4}[- ]?[0-9A-Za-z]{3}(?:[- ]?[SsRr])?(?![0-9A-Za-z])(?!-[0-9A-Za-z])",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Every dash-like character to '-', every space-like one (no-break, zero-width) to ' ', quotes dropped.</summary>
    private static string Clean(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (ch is '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212' or '_') sb.Append('-');
            else if (ch is '\u00A0' or '\u2007' or '\u202F' or '\t' or '\r' or '\n') sb.Append(' ');
            else if (ch is '\u200B' or '\u200C' or '\u200D' or '\uFEFF' or '"' or '\'' or '`' or '\u201C' or '\u201D' or '\u2018' or '\u2019') { }
            else sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    private static bool TryParseExact(string text, out ulong steamId64, out uint appId)
    {
        steamId64 = 0;
        appId = SteamApps.ModdingTools;
        var sig = new List<char>(10);
        foreach (char ch in text)
            if (ch is not ('-' or ' ')) sig.Add(ch);
        if (sig.Count == 8)
        {
            if (SteamApps.FromSuffix(sig[7]) is not uint app) return false;
            appId = app;
            sig.RemoveAt(7);
        }
        if (sig.Count != 7) return false;
        return FriendCode.TryDecode(new string(sig.ToArray()), out steamId64);
    }
}
