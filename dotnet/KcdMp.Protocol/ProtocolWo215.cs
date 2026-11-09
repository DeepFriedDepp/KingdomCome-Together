// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-215 -- the animation substrate (docs/WO-215-findings.md): copies move by the engine's own movement request (A), never an empty body
// (B), one activity line per NPC (C), only live NPCs become copies and they arrive dressed (D), the player never held in a fight with a
// ghost (E), riders on their horses (F), the instruments (G). No new message type and no protocol bump: the native half takes a switch
// word (op 150), answers a status (op 151), runs A.0's probe (op 152) and takes the route's tuning (op 153).
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo215.h): 150 Config [bits:4] -> [bits:4], 151 Status -> text, 152 Probe, 153 Tune.</summary>
    public const byte Wo215OpConfig = 150, Wo215OpStatus = 151, Wo215OpProbe = 152, Wo215OpTune = 153;
    /// <summary>D.1: the owner's watchdog asks (op 140) kind 3 -- the host NPC's whole body empty past 2 s: dormant on the host, the copy parked.</summary>
    public const byte Wo215AskDormant = 3;
}

/// <summary>
/// WO-215's switches. Every one ships on (`mp_w215 &lt;name&gt; on|off` in the game's console); the bench's twin turns one off for a run
/// through <see cref="EnvVar"/> -- read by both agents and both DLLs (the rig's KCDMP_RIG_TEST_ENV reaches them all).
/// </summary>
public static class W215Switch
{
    public const string EnvVar = "KCDMP_TEST_W215_OFF";

    /// <summary>The native half's bits (wo215_rules.h), in order.</summary>
    public static readonly string[] NativeNames = ["route", "gait", "ladder", "letgo_cull", "activity_line", "dormant", "ghost_fight", "probe_all"];

    /// <summary>Every switch: the native ones, then the agent's and the mod's own.</summary>
    public static readonly string[] Names = [.. NativeNames];

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
