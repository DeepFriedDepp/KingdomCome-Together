// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-216 -- the rest of the body (docs/WO-216-findings.md): the host's own movement request replayed on its copy (A), no brain runs on
// the joiner (B), one activity line from the host (C), outfits (D), riders on their horses (E), every pose through the one owner (F),
// fights the players are not in (G).
//
// A: the host's NPCs' movement requests, read on the host's AI update and sent on a change (and again every 2 s while one runs, every
// 6 s while none does) -- state, on the same lane as the NPC states, latest wins per body:
//
//   type up/down  name        from   body (after [target:1][joinId:4])
//   0x84 / 0x85   NpcRoute    host   [count:1]{[nameLen:1][name][rec:52]}
//
//   rec = [reqId:4][speed:1][flags:1][style:1][rsv:1][goal:3f][exact:3f][exactYaw:f][tolA:f][tolB:f][seq:4][pathId:4]  (native wo216_rules.h)
//         flags 1 a request runs, 2 with an exact end, 4 its goal known, 8 a path given with it, 16 not a man or a woman
//
// No protocol bump: a new type on the join channel. A mixed release is refused at the relay (WO-110 R9), so a peer that does not know it
// never shares a session with one that sends it.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo216.h): 160 Config [bits:4][role:1] -> [bits:4], 161 Status -> text, 162 RouteRead [max:1],
    /// 163 RouteIn, 164 Brains -> text, 165 Exempt [on:1][secs:2][nameLen:1][name].</summary>
    public const byte Wo216OpConfig = 160, Wo216OpStatus = 161, Wo216OpRouteRead = 162, Wo216OpRouteIn = 163, Wo216OpBrains = 164, Wo216OpExempt = 165,
                      Wo216OpOutfitRead = 166, Wo216OpOutfitOf = 167;

    /// <summary>D: the host's NPC's worn items (ActivityExtra kind 7, one body a frame, an event): row = [nameLen][name][n:1] n x [class:16].</summary>
    public const byte ExtraKindOutfit = 7;
    public const int OutfitMaxItems = 40;

    /// <summary>A: the host's NPCs' movement requests (host to joiner, state).</summary>
    public const byte NpcRouteUp = 0x84, NpcRouteDown = 0x85;
    public const int NpcRouteRecLen = 52;
    public const int NpcRouteMaxBodies = 24;
    public const int NpcRouteBodyMax = 1 + NpcRouteMaxBodies * (1 + MaxNpcNameLen + NpcRouteRecLen);

    /// <summary>
    /// A: the bodies in one op 163 to the DLL. The DLL's pipe takes at most 1,024 bytes a frame (pipe_server.cpp drops the connection
    /// past it): 16 bodies are 1 + 1 + 16 x 56 = 898 bytes. (b6/b8: a 24-body line went as one 1,346-byte op; the DLL dropped the
    /// agent's connection two or three times a run, and every copy with it.)
    /// </summary>
    public const int NpcRouteInMaxBodies = 16;
    public const int PipeMaxPayload = 1024;
}

/// <summary>
/// WO-216's switches. Every one ships on (`mp_w216 &lt;name&gt; on|off` in the game's console); the bench's twin turns one off for a run
/// through <see cref="EnvVar"/> -- read by both agents and both DLLs (the rig's KCDMP_RIG_TEST_ENV reaches them all).
/// </summary>
public static class W216Switch
{
    public const string EnvVar = "KCDMP_TEST_W216_OFF";

    /// <summary>The native half's bits (wo216_rules.h), in order.</summary>
    public static readonly string[] NativeNames = ["route_replay", "no_brains", "activity_line", "outfits", "riders", "poses", "npc_fights"];

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
