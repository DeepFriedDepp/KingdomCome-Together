// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-217 -- the world around the bodies (docs/WO-217-findings.md): one writer for a seated copy (0.2), an activity's placement (A), the
// small carries (B), a body's loot live for both (C), stolen is stolen for both (D), what an encounter leaves (E), carts (F).
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo217.h): 170 Config [bits:4][role:1] -> [bits:4], 171 Status -> text.</summary>
    public const byte Wo217OpConfig = 170, Wo217OpStatus = 171;
    /// <summary>F: the host's carts, the joiner's DLL plays them back. [host_ms:4][n:1] n x ([len:1][name][x y z ax ay az: 6 floats]).</summary>
    public const byte Wo217OpCarts = 172;

    // ---- C: WO-134's loot messages, new kinds (append-only; no protocol bump: a mixed release is refused at the relay, WO-110 R9) ----
    //   ask (joiner -> host)  6 TakeReq "<body> <item> <cls> <amt>"   7 TakeAck "<body> <applied|cancel>"   8 LiveEnd "<body>"
    //   host (host -> joiner) 8 BodyList "<body> <rev> <part> <nparts> <items|->"  items = item:cls:amt:hp:worn:stolen:owner,...
    //                         9 TakeVerdict "<grant|refuse> <body> <item> <owner|-|gone|busy> <stolen>"
    public const byte LootAskTakeReq = 6, LootAskTakeAck = 7, LootAskLiveEnd = 8;
    public const byte LootHostBodyList = 8, LootHostTakeVerdict = 9;
    //                         10 Corpses "<name:x:y:z:yaw,...|->"   E.1: the dead bodies near the players (the host's census; latest wins)
    public const byte LootHostCorpses = 10;
    //                         12 Carts "<host_ms> <name|x|y|z|ax|ay|az|riders;...|->"   F: the host's carts near the players (latest wins)
    public const byte LootHostCarts = 12;
    //                         11 Prop "<name|class|arch|model|x|y|z|ax|ay|az>"   13 PropGone "<name>"   E.2: what an encounter adds
    public const byte LootHostProp = 11, LootHostPropGone = 13;
}

/// <summary>
/// WO-217's switches. Every one ships on (`mp_&lt;name&gt; on|off` in the game's console); the bench's twin turns one off for a run through
/// <see cref="EnvVar"/> -- read by both agents and both DLLs (the rig's KCDMP_RIG_TEST_ENV reaches them all).
/// </summary>
public static class W217Switch
{
    public const string EnvVar = "KCDMP_TEST_W217_OFF";

    /// <summary>The native word's bits (wo217_rules.h), in order.</summary>
    public static readonly string[] NativeNames =
        ["seat_writer", "placement", "ghost_far", "kd_follow", "long_names", "layer_watch", "loot_live", "item_flags", "encounter_world", "carts", "dialog_twins"];

    /// <summary>The agent's own switches (no native bit): route_skip -- the joiner's agent drops the host's route lines (WO-216 A, 0x85)
    /// while this machine's mp_route is off, instead of handing each to its DLL (op 163) where nothing reads them (a pipe round trip each;
    /// in a busy village they filled the joiner's queue 30-100 s behind).</summary>
    public static readonly string[] AgentNames = ["route_skip"];

    /// <summary>Every switch (the native word carries the native ones; the agent's and the mod's parts read the same names).</summary>
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
