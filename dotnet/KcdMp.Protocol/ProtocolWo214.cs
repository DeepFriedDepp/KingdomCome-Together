// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-214 -- after 0.64.0 in the field (docs/WO-214-findings.md): one bind per body, a base for every owned copy, a combat action that
// ends with its fight, a death that ends on the ground, the host NPC's weapon in its copy's hand. No new message type: the native half
// takes a switch word (op 148) and answers a status (op 149). F (figure_hands) adds one kind on the WO-203 side channel, either way:
// W203FigureHand = 12, text "v1|<drawn 0|1>|<main-hand item class|->" (Wo214Rules.HandText) -- the relay passes it as every W203 row.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo214.h): 148 Config [bits:4] -> [bits:4], 149 Status -> text.</summary>
    public const byte Wo214OpConfig = 148, Wo214OpStatus = 149;
}

/// <summary>
/// WO-214's switches. Every one ships on (`mp_w214 &lt;name&gt; on|off` in the game's console); the bench's twin turns one off for a run
/// through <see cref="EnvVar"/> -- read by both agents and both DLLs (the rig's KCDMP_RIG_TEST_ENV reaches them all).
/// </summary>
public static class W214Switch
{
    public const string EnvVar = "KCDMP_TEST_W214_OFF";

    /// <summary>The native half's bits (wo214_rules.h), in order.</summary>
    public static readonly string[] NativeNames = ["send_turn", "one_bind", "owner_base", "combat_end", "reaction_rows"];

    /// <summary>Every switch: the native ones, then the agent's and the mod's own.</summary>
    public static readonly string[] Names = [.. NativeNames, "death_row", "death_ground", "draw_event", "figure_hands", "figure_turn", "anim_hold"];

    public static bool TwinOff(string name) => TwinOff(Environment.GetEnvironmentVariable(EnvVar), name);

    public static bool TwinOff(string? env, string name) =>
        !string.IsNullOrWhiteSpace(env) && env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x == name || x == "all");

    /// <summary>The native word for the switch states <paramref name="on"/> gives.</summary>
    public static uint NativeBits(Func<string, bool> on)
    {
        uint b = 0;
        for (int i = 0; i < NativeNames.Length; i++) if (on(NativeNames[i])) b |= 1u << i;
        return b;
    }
}
