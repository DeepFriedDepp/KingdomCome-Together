// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-218 -- the release candidate (docs/WO-218-findings.md): the joiner's pipe (A), stability (B), the bodies' and the world's open items.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo218.h): 180 Config [bits:4][role:1] -> [bits:4][caps:1], 181 Status -> text.</summary>
    public const byte Wo218OpConfig = 180, Wo218OpStatus = 181;
}

/// <summary>
/// WO-218's switches (`mp_&lt;name&gt; on|off` in the game's console); the bench's twin turns one off for a run through <see cref="EnvVar"/>
/// -- read by both agents and both DLLs.
/// </summary>
public static class W218Switch
{
    public const string EnvVar = "KCDMP_TEST_W218_OFF";

    /// <summary>The native word's bits (wo218.h), in order.</summary>
    public static readonly string[] NativeNames = ["takeover_stop", "drift_release", "main_budget"];

    /// <summary>The agent's own switches: pipe_batch (A.1: one round trip carries every waiting request; latest-wins state merged per
    /// body; the activity lanes' rows per body at once), pipe_shed (A.2: a queue older than 500 ms sheds superseded far state first), door_sweep
    /// (D.1, the mod's own: the agent only tells the mod -- the host's doors swept near the players, the joiner's own door put back), listen_reassert (B.3, the mod's own: the listen keys
    /// asserted again once a second while listening in).</summary>
    public static readonly string[] AgentNames = ["pipe_batch", "pipe_shed", "door_sweep", "listen_reassert"];

    public static readonly string[] Names = [.. NativeNames, .. AgentNames];

    public static bool TwinOff(string name) => TwinOff(Environment.GetEnvironmentVariable(EnvVar), name);

    public static bool TwinOff(string? env, string name) =>
        !string.IsNullOrWhiteSpace(env) && env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x == name || x == "all");

    public static uint NativeBits(Func<string, bool> on)
    {
        uint b = 0;
        for (int i = 0; i < NativeNames.Length; i++) if (on(NativeNames[i])) b |= 1u << i;
        return b;
    }
}
