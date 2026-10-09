// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-154 Phase 7b: the mod menu's agent rules (docs/WO-154-findings.md; the player's page: docs/MOD-MENU.md).
///
/// The menu lives in the mod (kdcmp.lua, KCD2MP.w154menu). The agent keeps what the player chose there, in
/// the mod's own <c>mod-settings.json</c> beside the agent (never in a game save), written only through
/// <see cref="SettingsJson"/> (key by key; unknown keys and the layout kept). The mod says each change as
/// <c>w154_menu_set &lt;Key&gt; &lt;value&gt;</c>; at every session start and after every world load the agent
/// pushes the saved values back (<c>KCD2MP_W154MenuRestore</c>, which calls the same setter as the console
/// command). The host's levers are pushed only while this player hosts: on a joiner the host's value applies.
/// The menu key is the launcher's: <c>MenuKey</c> in its settings.json (the keys-pak verb reads it), set here
/// only when that file exists.
/// </summary>
public static class Wo154MenuRules
{
    public const string ModSettingsFile = "mod-settings.json";
    public const string LauncherSettingsFile = "settings.json";

    public enum Kind { OnOff, Crime }

    /// <summary>One remembered menu choice: its key in mod-settings.json, its kind, whose it is.</summary>
    public sealed record Setting(string Key, Kind Kind, bool HostOwned);

    /// <summary>Every choice the menu remembers. The keys are the mod's (KCD2MP.w154menu ITEMS' <c>save</c>).</summary>
    public static readonly Setting[] Settings =
    {
        new("NameBadges", Kind.OnOff, false),
        new("PingLine", Kind.OnOff, false),
        new("CleanScreen", Kind.OnOff, false),
        new("FriendlyFire", Kind.OnOff, true),
        new("CrimeMode", Kind.Crime, true),
        new("FastTravel", Kind.OnOff, true),
        new("Leash", Kind.OnOff, true),
        new("SleepVote", Kind.OnOff, false),
        new("Whistle", Kind.OnOff, false),
        new("PartnerHerbs", Kind.OnOff, false),
        new("PartnerMarker", Kind.OnOff, false),   // WO-164 C2: mp_partner_marker
    };

    /// <summary>WO-154: the session's fast travel ships OFF (0.45.0): the host turns it on in the menu.</summary>
    public const bool FastTravelDefault = false;

    public static Setting? Find(string key) => Array.Find(Settings, s => s.Key == key);

    /// <summary>
    /// A <c>w154_menu_set</c> line: "&lt;Key&gt; &lt;value&gt;" -- a remembered choice ("NameBadges off", "CrimeMode joint")
    /// or the menu key ("MenuKey np_add"). False for anything else; nothing from the log channel is written unchecked.
    /// </summary>
    public static bool TryParseSet(string? arg, out string key, out string word, out JsonNode? value)
    {
        key = word = "";
        value = null;
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 2) return false;
        if (f[0] == KeybindPak.MenuKeySetting)
        {
            if (Array.IndexOf(KeybindPak.MenuKeys, f[1]) < 0) return false;
            key = f[0]; word = f[1]; value = JsonValue.Create(f[1]);
            return true;
        }
        var s = Find(f[0]);
        if (s is null) return false;
        if (s.Kind == Kind.OnOff)
        {
            if (f[1] is not ("on" or "off")) return false;
            key = s.Key; word = f[1]; value = JsonValue.Create(f[1] == "on");
            return true;
        }
        if (f[1] is not ("joint" or "individual")) return false;
        key = s.Key; word = f[1]; value = JsonValue.Create(f[1]);
        return true;
    }

    /// <summary>The mod's word for a stored value (on/off, joint/individual), or null when it is not one this key takes
    /// (a hand-edited file may hold anything).</summary>
    public static string? LuaWord(Setting s, JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (s.Kind == Kind.OnOff)
        {
            if (v.TryGetValue(out bool b)) return b ? "on" : "off";
            if (v.TryGetValue(out string? t) && t is "on" or "off") return t;
            return null;
        }
        return v.TryGetValue(out string? c) && c is "joint" or "individual" ? c : null;
    }

    /// <summary>
    /// What to push into the mod from the file: every remembered key with a value it takes, in <see cref="Settings"/>'
    /// order. The host's levers only when <paramref name="hosting"/> (on a joiner the host's value is the session's);
    /// <paramref name="own"/> = false leaves this player's own choices out (the role became host after the world's
    /// push: only the host's levers are still owed).
    /// </summary>
    public static List<(string Key, string Word)> RestorePlan(JsonObject? file, bool hosting, bool own = true)
    {
        var plan = new List<(string, string)>();
        if (file is null) return plan;
        foreach (var s in Settings)
        {
            if (s.HostOwned ? !hosting : !own) continue;
            if (!file.TryGetPropertyValue(s.Key, out JsonNode? node)) continue;
            if (LuaWord(s, node) is string w) plan.Add((s.Key, w));
        }
        return plan;
    }

    /// <summary>The restore call for one remembered value (key and word come from the fixed vocabulary above).</summary>
    public static string RestoreLua(string key, string word) =>
        $"if KCD2MP_W154MenuRestore then KCD2MP_W154MenuRestore(\"{key}\", \"{word}\") end";

    private static string LuaBool(bool? b) => b is null ? "nil" : b.Value ? "true" : "false";

    private static readonly Regex VersionText = new("^[0-9A-Za-z.\\-]{1,24}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The once-a-second push: role (host|joiner|none), a partner connected, the host's levers as a joiner knows them
    /// (null = not known yet), the link, the version, the game's own screens (its menu, inventory, map, skip-time, a
    /// cutscene) and this player down (the WO-113 guard). A Level statement in the batch queue (BatchPolicy).
    /// </summary>
    public static string SessionLua(string role, bool partner, bool? hostFastTravel, bool? hostCrimeJoint, bool? hostLeash,
                                    string link, string version, bool gameScreen, bool downed)
    {
        string r = role is "host" or "joiner" ? role : "none";
        string l = link is "connected" or "connecting" or "starting" or "failed" ? link : "starting";
        string v = VersionText.IsMatch(version) ? version : "?";
        string crime = hostCrimeJoint is null ? "nil" : hostCrimeJoint.Value ? "\"joint\"" : "\"individual\"";
        return string.Create(CultureInfo.InvariantCulture,
            $"if KCD2MP_W154MenuSession then KCD2MP_W154MenuSession(\"{r}\", {LuaBool(partner)}, {LuaBool(hostFastTravel)}, {crime}, {LuaBool(hostLeash)}, \"{l}\", \"{v}\", {LuaBool(gameScreen)}, {LuaBool(downed)}) end");
    }

    /// <summary>The menu keys for the mod: the one bound in this game run, the one stored for the next start, and the
    /// answer to a change ("stored", "not-saved", or "" for none).</summary>
    public static string KeysLua(string active, string stored, string result)
    {
        string a = Array.IndexOf(KeybindPak.MenuKeys, active) >= 0 ? active : KeybindPak.MenuKeyDefault;
        string s = Array.IndexOf(KeybindPak.MenuKeys, stored) >= 0 ? stored : a;
        string r = result is "stored" or "not-saved" ? result : "";
        return $"if KCD2MP_W154MenuKeys then KCD2MP_W154MenuKeys(\"{a}\", \"{s}\", \"{r}\") end";
    }

    /// <summary>The agent's link word for the mod (AgentConnectionStatus' states).</summary>
    public static string LinkWord(string state) => state switch
    {
        "connected" => "connected",
        "connecting" => "connecting",
        "failed" => "failed",
        _ => "starting",
    };

    /// <summary>A file system message for agent.log (it goes into bug reports): no folder of this machine.</summary>
    public static string Scrub(string text, string baseDir)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (dir, label) in new[] { (baseDir, "<install>"), (home, "<home>") })
        {
            string d = (dir ?? "").TrimEnd('\\', '/');
            if (d.Length >= 3) text = text.Replace(d, label, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }
}
