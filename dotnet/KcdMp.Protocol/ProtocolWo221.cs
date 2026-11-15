// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-221 -- bodies that walk right, and a horse each (docs/WO-221-findings.md).
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo221.h): 190 Config [bits:4][role:1] -> [bits:4], 191 Status -> text.</summary>
    public const byte Wo221OpConfig = 190, Wo221OpStatus = 191;
    /// <summary>shops.h: 192 Open [shop db id:4][name len:1][name] -> [code:1]; 193 Close [name len:1][name] -> [code:1].
    /// Codes: 1 opened, 2 a keeper already, 3 no such body here, 4 no such shop, 5 not armed, 6 queued, 7 closed, 8 not opened by us.</summary>
    public const byte Wo221OpShopOpen = 192, Wo221OpShopClose = 193;
    /// <summary>talk_hold.h: 194 Talk [on:1][name len:1][name] -> [code:1] (1 held, 2 released, 3 not held, 4 the switch is off).</summary>
    public const byte Wo221OpTalk = 194;
    /// <summary>body_order.h: 195 Go [gait:1][name len:1][name][x y z:3f] -> [code:1][order:4]; 196 Stop [name len:1][name] -> [code:1].
    /// Codes: 1 ordered / stopped, 2 no such body here, 3 the request is not armed on this build, 4 the switch is off.</summary>
    public const byte Wo221OpGo = 195, Wo221OpStop = 196;
    /// <summary>corpse_look.h: 197 Read [name len:1][name] -> [code:1][six zones 0..255]; 198 Set [name len:1][name][six zones] -> [code:1][changed:1].
    /// Codes: 1 ok, 2 no such body here, 3 not armed, 4 the switch is off, 5 the body has no helper (never bloodied).</summary>
    public const byte Wo221OpBloodRead = 197, Wo221OpBloodSet = 198;
    /// <summary>D.1 (the host): the quest module instances that sell a joiner's recorded horse: [n:1]{[len:2][path]}.</summary>
    public const byte Wo221OpHorseModules = 199;
}

/// <summary>
/// WO-221's switches (`mp_&lt;name&gt; on|off` in the game's console; `mp_w221_status` lists them); the bench's twin turns one off for a
/// run through <see cref="EnvVar"/> -- read by both agents and both DLLs.
/// </summary>
public static class W221Switch
{
    public const string EnvVar = "KCDMP_TEST_W221_OFF";

    /// <summary>The native word's bits (wo221.h), in order: combat_action_end (A: no combat action of ours outlives its body's combat
    /// part), talk_hold (B: a copy woken for a talk has its own schedule held), drive (C: a walking human copy is moved by the engine's
    /// per-frame movement request), holders (C.3: one table of who holds a body), own_horse (D: each player's own horse -- the record,
    /// the quest gate's horse rule, the call, the saddlebags).</summary>
    public static readonly string[] NativeNames = ["combat_action_end", "talk_hold", "drive", "holders", "own_horse", "corpse_look"];

    /// <summary>The agent's and the mod's own switches (the agent tells the mod): shop_keeper (B: a copy of a shopkeeper keeps the host
    /// keeper's shop -- the host says which, the joiner's DLL opens it with the copy by the shop's own call), talk_force (B: the
    /// mod's own: a talk the copy never takes is started the game's forced way), ragdoll_once (A: a fresh corpse is laid down by one
    /// ragdoll at a time, only after the game's own death action had its time -- the joiner's crashes came ~1 s after a ragdoll of a
    /// body 0.65 s dead).</summary>
    public static readonly string[] AgentNames = ["shop_keeper", "talk_force", "horse_drop", "saddlebags", "route_quiet", "cart_seat", "cart_wheels", "ragdoll_once"];

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
