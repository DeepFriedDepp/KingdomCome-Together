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
}
