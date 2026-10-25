// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

/// <summary>
/// WO-207 C.2: the game keeps running while its window is not in front, during a session (the mod's mp_keep_awake: the engine's
/// sys_SleepIfInactiveWH 0 while the session runs, the player's own value back after it). On by default. The one rule, shared by the
/// launcher (its settings, the agent's command line) and the agent (what it tells the mod).
/// </summary>
public static class KeepAwakeSetting
{
    public const string Label = "Keep the game running when it isn't the active window (recommended for co-op)";

    /// <summary>The agent's command-line flag. Always explicit from the launcher; an agent started without one keeps the game awake.</summary>
    public static string AgentFlag(bool on) => on ? "--keep-awake" : "--no-keep-awake";
}
