// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace KcdMp.Setup;

/// <summary>
/// WO-159 Phase 4: has this player accepted the game's two first-run pages (the EULA and the telemetry choice)?
/// Read-only, from the active profile's <c>attributes.xml</c> under <c>&lt;Saved Games&gt;\kingdomcome2\profiles</c>
/// (the active profile is named in <c>profiles\settings.xml</c>). Nothing here ever writes them: consent is the
/// player's (WO-158 P5). The launcher only tells a player whose pages are not accepted to start the game once from
/// Steam and accept them.
/// </summary>
public static class ConsentFlags
{
    /// <summary>
    /// The EULA version an accepted profile holds on the Modding Tools build 1.5.5 (observed: <c>eula_confirmed_version
    /// = 2</c>). The game's own current version is the cvar <c>wh_ui_eulaCurrentVersion</c>, readable only inside a
    /// running game; a lower stored version is "stale" (the game shows its page again), a higher one is accepted.
    /// </summary>
    public const int KnownEulaVersion = 2;

    public enum State { Accepted, Missing, Stale, NoProfile }

    public sealed record Result(State State, string Detail);

    public const string AcceptMessage =
        "Kingdom Come: Deliverance II asks every new player to accept two pages the first time it starts (its licence " +
        "agreement and the telemetry choice). This computer has not accepted them yet. Start the game once from Steam " +
        "(the Modding Tools entry), accept the two pages, close it, then come back and press Host or Join again.";

    /// <summary>&lt;Saved Games&gt;\kingdomcome2 (the game's user folder), or null.</summary>
    public static string? UserFolder()
    {
        string? root = null;
        try
        {
            if (SHGetKnownFolderPath(new Guid("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4"), 0, IntPtr.Zero, out var p) == 0)
            {
                root = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
            }
        }
        catch { }
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
        return Path.Combine(root, "kingdomcome2");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

    /// <summary>The flags of the active profile under <paramref name="userFolder"/> (&lt;Saved Games&gt;\kingdomcome2).</summary>
    public static Result Read(string? userFolder)
    {
        if (userFolder is null) return new(State.NoProfile, "the game's user folder is not known");
        string profiles = Path.Combine(userFolder, "profiles");
        string settings = Path.Combine(profiles, "settings.xml");
        if (!Directory.Exists(profiles)) return new(State.Missing, "the game has no profile yet (it has never been started on this account)");
        string? active = null;
        try
        {
            if (File.Exists(settings))
                active = XDocument.Load(settings).Descendants().FirstOrDefault(e =>
                    string.Equals((string?)e.Attribute("name"), "ActiveProfile", StringComparison.OrdinalIgnoreCase))?.Attribute("value")?.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { return new(State.NoProfile, "profiles\\settings.xml is unreadable"); }
        if (string.IsNullOrWhiteSpace(active) || active.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || active is "." or "..")
            return new(State.NoProfile, "the active profile is not named in profiles\\settings.xml");
        string attrs = Path.Combine(profiles, active, "attributes.xml");
        if (!File.Exists(attrs)) return new(State.Missing, "the active profile has no attributes.xml");
        Dictionary<string, string> a;
        try
        {
            a = XDocument.Load(attrs).Descendants("Attr")
                .Where(e => e.Attribute("name") is not null)
                .GroupBy(e => e.Attribute("name")!.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Attribute("value")?.Value ?? "", StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { return new(State.NoProfile, "the active profile's attributes.xml is unreadable"); }
        bool eulaSet = a.TryGetValue("eula_confirmed_version", out var ev) && int.TryParse(ev, out _);
        bool telSet = a.TryGetValue("telemetry_confirmed", out var tv) && tv.Trim() is "1" or "true";
        if (!eulaSet && !telSet) return new(State.Missing, "neither page is accepted (no eula_confirmed_version, no telemetry_confirmed)");
        if (!eulaSet) return new(State.Missing, "the licence page is not accepted (no eula_confirmed_version)");
        if (!telSet) return new(State.Missing, "the telemetry page is not answered (no telemetry_confirmed)");
        int ver = int.Parse(ev!);
        if (ver < KnownEulaVersion) return new(State.Stale, $"the licence accepted is version {ver}; this game asks for {KnownEulaVersion}");
        return new(State.Accepted, $"accepted (licence version {ver}, telemetry answered)");
    }
}
