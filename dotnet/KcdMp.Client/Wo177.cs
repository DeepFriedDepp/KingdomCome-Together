// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-177 (docs/WO-177-findings.md): the pure rules of the agent's half. GameBridge.Wo177.cs is the bridge; Wo177Tests pins these.
///   A5  no load while a fight is running: every load the mod issues (a join, a rejoin, the way back to the player's own save, the repaired
///       copy's load, a harness's) first asks the Lua whether this Henry or a partner's figure is in a fight or was hit within 10 s
///       (kdcmp.lua KCD2MP_W177LoadQuiet, answered as the event "w177_load_quiet &lt;why&gt; ok|fight|hit"), and waits, one note, until it says ok.
/// </summary>
public static class Wo177Rules
{
    /// <summary>A load that waits longer than this is not issued at all (the player is told; nothing is loaded behind a fight that never ends).</summary>
    public const int MaxWaitMs = 180_000;

    /// <summary>One question a second while waiting; an unanswered question is logged and counted as "not quiet" (never as ok).</summary>
    public const int PollMs = 1_000, ProbeTimeoutMs = 2_500;

    /// <summary>The Lua's quiet window after a hit (kdcmp.lua KCD2MP.w177.quietS).</summary>
    public const double QuietAfterHitS = 10.0;

    public const string ReasonOk = "ok", ReasonFight = "fight", ReasonHit = "hit";

    /// <summary>"&lt;why&gt; &lt;reason&gt;" -> reason; null for anything else (an unknown word is not "ok").</summary>
    public static string? ParseReason(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return null;
        var parts = arg.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string last = parts[^1];
        return last is ReasonOk or ReasonFight or ReasonHit ? last : null;
    }

    public static bool IsQuiet(string? reason) => reason == ReasonOk;

    /// <summary>Out of time: the load is not issued.</summary>
    public static bool GaveUp(long waitedMs) => waitedMs >= MaxWaitMs;

    /// <summary>What the player is told while the load waits (the Lua shows the same words once as a toast).</summary>
    public static string WaitText(string? reason) => reason == ReasonHit
        ? "Something hit you a moment ago: the load waits a few seconds."
        : "A fight is on: the load waits until it is over.";

    // ---- B3: a working NPC's tool swing is no damage on a figure

    /// <summary>Default of mp_tool_swing (the maintainer's standing rule: new pieces ship on).</summary>
    public const bool DefaultToolSwing = true;

    /// <summary>The Lua lists the calm NPCs near a figure every 2 s; a name stays calm this long after its last listing.</summary>
    public const int CalmTtlMs = 5_000;

    /// <summary>"calm calm ! fighting fighting" (the w177_calm event) -> the calm names and the names that turned to a fight (they stop being calm at
    /// once); anything that is not a plain NPC name is skipped.</summary>
    public static (List<string> Calm, List<string> Fighting) ParseCalm(string? arg)
    {
        var calm = new List<string>();
        var fighting = new List<string>();
        var into = calm;
        foreach (var n in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (n == "!") { into = fighting; continue; }
            if (KcdMp.Wire.W164Text.IsNpcName(n)) into.Add(n);
        }
        return (calm, fighting);
    }

    /// <summary>A hit on a figure by this attacker is a working NPC's tool swing, not a blow of a fight: the switch is on, the attacker was listed
    /// calm within <see cref="CalmTtlMs"/>, and it is not a missile (an arrow is always a fight's).</summary>
    public static bool IsToolSwing(bool switchOn, long calmSince, long now, bool missile) =>
        switchOn && !missile && calmSince > 0 && now - calmSince >= 0 && now - calmSince < CalmTtlMs;

    public const string GaveUpText = "A fight was still on after three minutes, so nothing was loaded. Load your save yourself when it is over.";
}
