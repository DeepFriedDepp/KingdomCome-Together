// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-222 -- hits that land like single-player (docs/WO-222-findings.md).
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo222.h): 200 Config [bits:4][role:1] -> [bits:4], 201 Status [page:1] -> text.</summary>
    public const byte Wo222OpConfig = 200, Wo222OpStatus = 201;
    /// <summary>B.1: 202 Shown [eid:4] -> [state:1][our fragment:4][the full body's fragment:4][its priority:4][ms since queued:4]; state 0 none of
    /// ours, 1 ours plays on the full body, 2 another action, 3 empty, 4 unreadable.</summary>
    public const byte Wo222OpShown = 202;
}

/// <summary>
/// WO-222's switches (`mp_&lt;name&gt; on|off` in the game's console; `mp_w222_status` lists them); the bench's twin turns one off for a
/// run through <see cref="EnvVar"/> -- read by both agents and both DLLs.
/// </summary>
public static class W222Switch
{
    public const string EnvVar = "KCDMP_TEST_W222_OFF";

    /// <summary>The native word's bits (wo222.h), in order: react_parity (A: a joiner's blow is applied on the host the way the host's own
    /// blow would be), react_show (B: the host's reaction plays on its copy ahead of anything the copy holds), flee_live (D: a fleeing host
    /// NPC is still an enemy on the joiner), swing_log (E: every swing of the joiner accounted for), dead_lay (F.1: a dead copy left
    /// standing is laid down).</summary>
    public static readonly string[] NativeNames = ["react_parity", "react_show", "flee_live", "swing_log", "dead_lay"];

    /// <summary>The agent's own: def_wait (C: the host's wait for the joiner's own verdict follows the link, and a late verdict corrects
    /// the host's guess).</summary>
    public static readonly string[] AgentNames = ["def_wait"];

    public static readonly string[] Names = [.. NativeNames, .. AgentNames];

    public static bool TwinOff(string name) => TwinOff(Environment.GetEnvironmentVariable(EnvVar), name);

    public static bool TwinOff(string? env, string name) =>
        !string.IsNullOrWhiteSpace(env) && env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x == name || (x == "all" && name != "swing_log"));   // the swing log is an instrument: every run keeps it

    public static uint NativeBits(Func<string, bool> on)
    {
        uint b = 0;
        for (int i = 0; i < NativeNames.Length; i++) if (on(NativeNames[i])) b |= 1u << i;
        return b;
    }
}
