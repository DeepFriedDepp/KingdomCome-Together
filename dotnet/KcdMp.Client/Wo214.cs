// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>WO-214 (docs/WO-214-findings.md): the agent's pure rules. KcdMp.Client.Tests/Wo214Tests.cs checks them.</summary>
public static class Wo214Rules
{
    // ---------------------------------------------------------------- B: one bind per body (the joiner's agent)

    /// <summary>The host sends every NPC within 150 m of a player (the bands' cull); a body whose last sample is within this of the joiner or
    /// the host's figure is in that range -- its silence is a lost heartbeat or a stall, not the body leaving.</summary>
    public const float InRangeM = 140f;

    /// <summary>A silent stream of a body in range releases its puppet only after this (the old 3 s against the host's 2 s heartbeat: one
    /// lost heartbeat, or the field's 1.9-4.4 s npc-state gaps, released it and bound it afresh at the next sample).</summary>
    public const int HoldSilentMs = 12_000;

    /// <summary>B: the silence after which the mod releases a puppet: 3 s for a body that left the host's range (or with the twin off),
    /// 12 s for one in range. <paramref name="dMinM"/> &lt; 0 = not known (the old 3 s).</summary>
    public static int SilenceReleaseMs(bool on, float dMinM) => on && dMinM >= 0 && dMinM <= InRangeM ? HoldSilentMs : Wo147Rules.SilenceReleaseMs;

    // ---------------------------------------------------------------- D: deaths

    /// <summary>D.1: a row that carries the host NPC's death reaction (its fragment, or the death tag).</summary>
    public static bool IsDeathRow(string? spec) =>
        spec is not null && (spec.StartsWith("CombatDeath", StringComparison.Ordinal) || spec.Contains("+death", StringComparison.Ordinal)
                             || spec.Contains(" death+", StringComparison.Ordinal));

    /// <summary>D.3: after a death the host decided, a body whose physics is still a living entity is upright (its soul's own read may lag).</summary>
    public static bool UprightDead(byte living) => living == 1;

    /// <summary>D.3: how long between the reads of a soul the host declared dead that does not read dead yet (three at most).</summary>
    public const int SoulDeadRecheckMs = 200;

    /// <summary>D.3: a copy playing its death reaction gets this much longer before its fall is forced (the reaction's own fall).</summary>
    public const int DeathReactionGraceMs = 800;

    /// <summary>
    /// D.3: after the engine's own finish of a dead copy (WO-210's lethal branch), a body whose physics is still a living entity gets the
    /// ragdoll too. The bench: a copy killed while held in its fight was "finished", stood on, and its death read still-upright (the
    /// field's "dead NPC standing for the joiner"). The twin (death_ground off): the finish alone, as before.
    /// </summary>
    public static bool RagdollAfterFinish(bool deathGround, byte livingAfter) => deathGround && livingAfter == 1;

    // ---------------------------------------------------------------- E: the host NPC's weapon in its copy's hand

    /// <summary>E: a weapon drawn on a copy within this of the host's draw counts as in time (the WO's 500 ms).</summary>
    public const int DrawInTimeMs = 500;

    // ---------------------------------------------------------------- F: the hand as a value (both roles)

    /// <summary>F: the W203 FigureHand text -- "v1|&lt;drawn 0|1&gt;|&lt;main-hand item class, D format, or -&gt;".</summary>
    public static string HandText(bool drawn, Guid? cls) => $"v1|{(drawn ? 1 : 0)}|{(cls is Guid g ? g.ToString("D") : "-")}";

    /// <summary>F: a FigureHand text read back; anything else (another version, a bad field) is refused.</summary>
    public static bool TryParseHand(string? text, out bool drawn, out Guid? cls)
    {
        drawn = false; cls = null;
        var f = (text ?? "").Split('|');
        if (f.Length != 3 || f[0] != "v1" || f[1] is not ("0" or "1")) return false;
        drawn = f[1] == "1";
        if (f[2] == "-") return true;
        if (!Guid.TryParseExact(f[2], "D", out var g)) return false;
        cls = g;
        return true;
    }

    /// <summary>F: the mod's own "w214_hand" event -- "&lt;0|1&gt; &lt;class|-&gt;".</summary>
    public static bool TryParseOwnHand(string? arg, out bool drawn, out Guid? cls)
    {
        drawn = false; cls = null;
        var p = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 2 || p[0] is not ("0" or "1")) return false;
        drawn = p[0] == "1";
        if (p[1] == "-") return true;
        if (!Guid.TryParse(p[1], out var g)) return false;
        cls = g;
        return true;
    }

    /// <summary>
    /// F: the partner's figure keeps the weapon its partner holds drawn, whatever his outfit packet says: the field's packet after an
    /// inventory screen listed one class of thirteen and the converge took the rest off. A class he does not hold drawn is not kept.
    /// </summary>
    public static HashSet<Guid> KeepHeld(IEnumerable<Guid> target, bool on, bool drawn, Guid? held)
    {
        var s = new HashSet<Guid>(target);
        if (on && drawn && held is Guid g) s.Add(g);
        return s;
    }

    /// <summary>F: the partner's last hand message counts for this long (a heartbeat comes every 10 s while drawn).</summary>
    public const int HandFreshMs = 25_000;

    // ---------------------------------------------------------------- G: the band table's verdict

    /// <summary>A.0/G: a sample counts as the body walking when it moved the body at least this far since its last one.</summary>
    public const float MovingStepM = 0.02f;
    /// <summary>A.0/G: a body counts as walking in a 10 s window when it walked at least this long of it (its rate is over that time).</summary>
    public const double MovingWindowS = 2.0;

    /// <summary>G: the briefing's plain-words line for the walking bodies within 40 m.</summary>
    public static string WalkingVerdict(int bodies, double p50Hz, double p10Hz) => bodies == 0
        ? "no walking NPC within 40 m was measured"
        : FormattableString.Invariant($"walking NPCs within 40 m reached you {p50Hz:F0} times a second (the slowest tenth {p10Hz:F0}; {bodies} body-windows)");
}
