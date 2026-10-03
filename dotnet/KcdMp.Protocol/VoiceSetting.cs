// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.Json.Nodes;

namespace KcdMp.Wire;

/// <summary>
/// WO-154 (Phase 6.7): the mod's voice chat is off unless the player chose it. The one rule,
/// shared by the launcher (its settings, the agent's command line) and the agent (the in-game
/// mod menu's switch). docs/WO-154-voice-decision.md says why it is this rule.
/// <list type="bullet">
/// <item>Until 0.44.0 the launcher wrote <c>VoiceChatEnabled</c> into settings.json on every save,
///   with the default <c>true</c>: a <c>true</c> in a player's file cannot be told apart from that
///   old default, so it is not read as a choice.</item>
/// <item><c>VoiceChatChosen</c> is written only when the player picks a value (the launcher's
///   settings, or the mod menu through the agent). A missing key reads as false.</item>
/// <item>The effective value is <c>VoiceChatChosen &amp;&amp; VoiceChatEnabled</c>. An upgrade never
///   changes <c>VoiceChatEnabled</c>.</item>
/// </list>
/// </summary>
public static class VoiceSetting
{
    public const string EnabledKey = "VoiceChatEnabled";
    public const string ChosenKey = "VoiceChatChosen";

    /// <summary>On only when the player chose it, and chose on.</summary>
    public static bool Effective(bool enabled, bool chosen) => chosen && enabled;

    /// <summary>The effective value of a settings file's root object. Missing, null or not a JSON bool reads as false.</summary>
    public static bool Effective(JsonObject? settings) =>
        Effective(ReadBool(settings, EnabledKey), ReadBool(settings, ChosenKey));

    /// <summary>The player's own choice: both keys, as <see cref="SettingsJson.Edit.Set"/> (never a fill).</summary>
    public static IReadOnlyList<SettingsJson.Edit> ChoiceEdits(bool on) =>
        new[] { SettingsJson.Edit.Set(EnabledKey, on), SettingsJson.Edit.Set(ChosenKey, true) };

    /// <summary>The agent's command-line flag. Always explicit: the agent's own default never decides.</summary>
    public static string AgentFlag(bool effective) => effective ? "--voice" : "--no-voice";

    private static bool ReadBool(JsonObject? o, string key)
    {
        if (o is null || !o.TryGetPropertyValue(key, out var node) || node is not JsonValue v) return false;
        try { return v.TryGetValue<bool>(out bool b) && b; }
        catch (InvalidOperationException) { return false; }
    }
}
