// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-175 S1, as WO-180 left it: the joiner's slot, and the <c>mp_coop_playline</c> switch (default ON; remembered as
/// <c>CoopPlayline</c> in mod-settings.json beside the agent).
///
/// <list type="bullet">
/// <item><b>The host</b> plays the save he picked, directly, in its own playline (WO-180: the host-side copy, its prepared
///   copies, the placeholder and the clean-up that took them back are gone; the launcher issues <c>wh_sys_LoadGame</c> for the pick).
///   Nothing here writes, moves or deletes a save. The co-op playlines the old copies made stay where they are, ordinary saves.</item>
/// <item><b>The joiner</b>'s spliced world (WO-124 mpworld file) goes into the playline that already holds this host's world
///   (the same seed tag), else the lowest free playline (<see cref="ChooseForJoin"/>). None of the five: refused before anything is
///   asked of the host (<see cref="SlotAvailable"/>, <see cref="JoinerNoFreeText"/>). Nothing is ever deleted to make room.</item>
/// </list>
/// Never an index above 4 (wh_sys_LoadGame 5 crashes the game).
/// </summary>
public static class Wo175Coop
{
    public const string CliTag = "W175C";

    /// <summary>The switch's key in mod-settings.json (beside the agent; the WO-154 file).</summary>
    public const string SwitchKey = "CoopPlayline";

    /// <summary>The joiner has no slot for the host's world (said in the game and in the launcher, before any request).</summary>
    public const string JoinerNoFreeText = "All five save slots are full. Delete one in the game's Load Game list, then join again.";

    // ------------------------------------------------------------------ the switch

    /// <summary>mod-settings.json beside this agent (the WO-154 menu's file).</summary>
    public static string ModSettingsBesideAgent() => Path.Combine(AppContext.BaseDirectory, Wo154MenuRules.ModSettingsFile);

    /// <summary>On unless the file says off (missing file, missing key, a value it cannot read: on).</summary>
    public static bool SwitchOn(string modSettings)
    {
        if (SettingsJson.Read(modSettings) is not JsonObject f || !f.TryGetPropertyValue(SwitchKey, out JsonNode? n) || n is not JsonValue v) return true;
        if (v.TryGetValue(out bool b)) return b;
        if (v.TryGetValue(out string? s)) return s.Trim().ToLowerInvariant() is not ("off" or "false" or "0");
        return true;
    }

    /// <summary>"on" / "off" (the mod's <c>w175_coop</c> event, the console command's word).</summary>
    public static bool TryParseSwitch(string? arg, out bool on)
    {
        string a = (arg ?? "").Trim().ToLowerInvariant();
        on = a == "on";
        return a is "on" or "off";
    }

    public static SettingsJson.Result SetSwitch(string modSettings, bool on) =>
        SettingsJson.Update(modSettings, [SettingsJson.Edit.Set(SwitchKey, JsonValue.Create(on))], createIfMissing: true);

    // ------------------------------------------------------------------ the joiner's slot

    /// <summary>The mod's own transient files (WO-124 mpworld, WO-125 mpexit): not a save of anybody's.</summary>
    private static bool IsTransient(string file)
    {
        string n = Path.GetFileName(file);
        return n.StartsWith("mpworld", StringComparison.OrdinalIgnoreCase) || n.StartsWith("mpexit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Playline <paramref name="pl"/> already holds world <paramref name="tag"/>: 0..4, at least one save in it, and every save
    /// in it is of that seed (a playline that came to hold another playthrough is not this world's; a save that cannot be read is not either).</summary>
    public static bool HoldsWorld(string saves, int pl, string tag)
    {
        if (pl is < 0 or > 4 || tag.Length == 0) return false;
        string dir = Path.Combine(saves, $"playline{pl}");
        if (!Directory.Exists(dir)) return false;
        bool any = false;
        foreach (var f in Directory.EnumerateFiles(dir, "*.whs"))
        {
            if (IsTransient(f)) continue;
            if (WhsSave.ReadSeedFromFile(f) is not uint s || WhsSave.SeedTag(s) != tag) return false;
            any = true;
        }
        return any;
    }

    /// <summary>"playline2/autosave041" (no extension).</summary>
    public static string Display(string path) =>
        $"{Path.GetFileName(Path.GetDirectoryName(path))}/{Path.GetFileNameWithoutExtension(path)}";

    public sealed record JoinPick(bool Ok, int Playline, bool Existing);

    /// <summary>
    /// Where the joiner's spliced copy of the host's world (<paramref name="hostTag"/>, the plain seed tag) goes: the playline that
    /// already holds this world (the lowest, when two do), else the lowest free playline; none: refused.
    /// </summary>
    public static JoinPick ChooseForJoin(string saves, string hostTag)
    {
        for (int pl = 0; pl <= 4; pl++)
            if (HoldsWorld(saves, pl, hostTag)) return new(true, pl, true);
        var free = Wo159.FreePlaylines(saves);
        return free.Count > 0 ? new(true, free[0], false) : new(false, -1, false);
    }

    /// <summary>The pre-request question, cheap while a playline is free: is there a slot for this host's world (an existing one or a free one)?</summary>
    public static bool SlotAvailable(string saves, string hostTag) =>
        Wo159.FreePlaylines(saves).Count > 0 || ChooseForJoin(saves, hostTag).Ok;

    // ------------------------------------------------------------------ CLI

    /// <summary><c>--w175coop switch [on|off]</c>. One line: <c>W175C {json}</c>.</summary>
    public static int RunCli(string[] args, TextWriter w)
    {
        string? Opt(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        int at = Array.IndexOf(args, "--w175coop");
        string cmd = at >= 0 && at + 1 < args.Length ? args[at + 1] : "";
        string settings = Opt("--settings") ?? ModSettingsBesideAgent();
        void Out(object o) => w.WriteLine(CliTag + " " + JsonSerializer.Serialize(o));
        if (cmd == "switch")
        {
            string? v = at + 2 < args.Length ? args[at + 2] : null;
            if (TryParseSwitch(v, out bool on))
            {
                var r = SetSwitch(settings, on);
                Out(new { ok = r.Outcome != SettingsJson.Outcome.Refused, on = SwitchOn(settings), outcome = r.Outcome.ToString().ToLowerInvariant() });
                return 0;
            }
            Out(new { ok = true, on = SwitchOn(settings) });
            return 0;
        }
        Out(new { ok = false, why = "usage: --w175coop switch [on|off]" });
        return 2;
    }
}
