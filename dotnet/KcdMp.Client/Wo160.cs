// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-160: the engine-free rules of the 2026-10-07 tester findings (docs/WO-160-findings.md).
/// <see cref="GameBridge"/> feeds them its live flags; Wo160Tests pins them.
/// </summary>
public static class Wo160Rules
{
    // ------------------------------------------------------------------ 2: the false "You loaded your own save"

    /// <summary>
    /// A join's own activity (the request, the transfer, the load, the after-load check) keeps the own-world verdict
    /// silent, and so do the 60 s after it ended: the field's toast came ~20 s before the join's load even started.
    /// </summary>
    public const long SeparateQuietMs = 60_000;

    /// <summary>How long the mod's own "world" answer stands as proof that a world is loaded in THIS game process.</summary>
    public const long WhereFreshMs = 12_000;

    /// <summary>While a verdict (or its precondition) holds, the mod is asked again this often.</summary>
    public const long WhereRefreshMs = 5_000;

    /// <summary>
    /// The own-world verdict ("You loaded your own save", "Your host is in a shared world, quit and start again") may
    /// speak only when the MOD ITSELF said a world is loaded, a moment ago -- never a value the agent kept from a game
    /// that quit or crashed without a quit line (the field's stale "World", 2026-10-07: the new process was still at its
    /// main menu) -- and no join is, or just was, under way. At the main menu, during a join's load, or with a game
    /// that does not answer, it is silent.
    /// </summary>
    /// <param name="msSinceWorldConfirmed">Milliseconds since the mod answered "world" (or a "Gameplay started" line landed); negative = never.</param>
    /// <param name="joinBusy">A join, a rejoin, a leave or a transfer is in flight now.</param>
    /// <param name="msSinceJoinActivity">Milliseconds since a join last was in flight; negative = none since this agent began.</param>
    public static bool SeparateVerdictAllowed(long msSinceWorldConfirmed, bool joinBusy, long msSinceJoinActivity)
        => msSinceWorldConfirmed >= 0 && msSinceWorldConfirmed <= WhereFreshMs
           && !joinBusy
           && (msSinceJoinActivity < 0 || msSinceJoinActivity >= SeparateQuietMs);

    /// <summary>
    /// The "your host is in a shared world, quit and start again" line (the player loaded a world before the host's
    /// session was known) needs the same proof of a loaded world; it has no join-activity gate (a join in flight
    /// returns before it is reached).
    /// </summary>
    public static bool NeedsMenuToldAllowed(long msSinceWorldConfirmed)
        => msSinceWorldConfirmed >= 0 && msSinceWorldConfirmed <= WhereFreshMs;

    /// <summary>True when the mod should be asked where the game is again (never confirmed, or the proof is aging).</summary>
    public static bool WhereRefreshDue(long msSinceWorldConfirmed, long msSinceAsked)
        => msSinceAsked >= 3_000 && (msSinceWorldConfirmed < 0 || msSinceWorldConfirmed >= WhereRefreshMs);

    // ------------------------------------------------------------------ 5: the herb proxy

    /// <summary>
    /// The avatar bends and picks with a plain clip while its player gathers herbs (mp_avatar_herbs on, the default): true for the
    /// herb minigame only. The engine's own PickingHerbs fragment is never played (AvatarShow returns null for it).
    /// </summary>
    public static bool HerbProxyWanted(byte minigame, byte herbMinigame, bool herbsOn) => herbsOn && minigame == herbMinigame;

    // ------------------------------------------------------------------ 1: the joiner's NPC copies (one context root cause)

    /// <summary>
    /// A copy's stance and unstance are released (the game's own reset, KCD2MP_W160Release) before a placement that CHANGES the
    /// activity of a body this agent already placed: the game's planner has no way out of a work activity (field 2026-10-07: 5,308 of
    /// 8,316 placements refused). The first placement of a copy follows the puppet start's own release (WO-118); an avatar is a
    /// figure of this mod, never in the game's NPC schedule; a repeated row of the same activity (the host's 10 s refresh) asks for
    /// nothing at all.
    /// </summary>
    public static bool ReleaseBeforeApply(bool isAvatar, bool placedBefore, bool activityChanged)
        => !isAvatar && placedBefore && activityChanged;

    /// <summary>
    /// "[Error] /ai//(NPC)ttkc_woman_12/NPC Context [NPCContext]:Couldn't find actions to get NPC into game loaded state" (refused) or
    /// "...:Execution of 1 actions from load counldn't reach the loaded state in 3 updates" (unreached) -> the NPC's name; null for
    /// every other line. Both are the game's planner failing a placement this mod asked for.
    /// </summary>
    public static string? PlannerErrorNpc(string line, out bool refused)
    {
        refused = false;
        const string marker = "/NPC Context [NPCContext]:";
        int m = line.IndexOf(marker, StringComparison.Ordinal);
        if (m < 0) return null;
        string tail = line.Substring(m + marker.Length);
        if (tail.StartsWith("Couldn't find actions to get NPC into game loaded state", StringComparison.Ordinal)) refused = true;
        else if (!(tail.StartsWith("Execution of ", StringComparison.Ordinal) && tail.Contains("counldn't reach the loaded state", StringComparison.Ordinal))) return null;
        const string npc = "(NPC)";
        int a = line.IndexOf(npc, StringComparison.Ordinal);
        if (a < 0 || a + npc.Length >= m) return null;
        string name = line.Substring(a + npc.Length, m - (a + npc.Length));
        return name.Length > 0 && name.IndexOf('/') < 0 ? name : null;
    }
}
