// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-176 (0.50.0, docs/WO-176-findings.md): the pure rules of the agent's half. GameBridge.Wo176.cs is the bridge; native/KCDMP/wo176.* the
/// DLL half; Wo176Tests pins these.
///   A1 (host) a quest's arranged fight is the story's: the DLL refuses a figure's blow into it (real-hit fallback 11 "arranged-fight", or
///      WO-121's reason 9 on the older path) -- neither the engine nor the raw path runs, no engage, no brain message, a fatal packet for
///      that actor is not applied either; the joiner is told once (W164 kind 9).
///   B3a (joiner) a blow the host's NPC blocked / parried / had its block broken by, with no reaction row of the host's own coming, plays the
///      NPC's own block row on its copy (mp_copy_blocks).
/// </summary>
public static class Wo176Rules
{
    /// <summary>The switches (each with its console switch): new pieces ship on (the maintainer's standing rule).</summary>
    public const bool DefaultArrangedGuard = true, DefaultCopyBlocks = true;

    /// <summary>What the joiner is told, once per story fight.</summary>
    public const string StoryFightText = "This fight belongs to the story - your partner fights it.";

    /// <summary>The DLL's answers that mean "kept out of an arranged fight": op 18's fallback 11, WO-121's apply_attributed reason 9.</summary>
    public const byte FallbackArranged = 11, AttribReasonKept = 9;

    public static bool KeptOut(byte fallbackOrReason) => fallbackOrReason == FallbackArranged || fallbackOrReason == AttribReasonKept;

    /// <summary>A new story fight for a joiner = no kept-out blow of his for this long (then he is told again).</summary>
    public const long TellGapMs = 10 * 60_000;

    /// <summary>A fatal packet for an actor the DLL kept out within this long is not applied either (the story decides its death).</summary>
    public const long ArrangedMemoryMs = 60_000;

    public static bool ShouldTell(long lastKeptAt, long now) => lastKeptAt <= 0 || now - lastKeptAt >= TellGapMs;

    public static bool StillArranged(long keptAt, long now) => keptAt > 0 && now - keptAt >= 0 && now - keptAt < ArrangedMemoryMs;

    /// <summary>"w176_cfg key=on|off ...": arranged_guard (host, the DLL) and copy_blocks (joiner).</summary>
    public static IEnumerable<(string Key, bool On)> ParseCfg(string? arg)
    {
        foreach (var part in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || eq == part.Length - 1) continue;
            string k = part[..eq], v = part[(eq + 1)..];
            if (k is "arranged_guard" or "copy_blocks" && v is "on" or "off") yield return (k, v == "on");
        }
    }

    /// <summary>B3a: the block row action for the host's verdict on the joiner's blow; -1 = nothing of ours to play.</summary>
    /// <remarks>react 1 = the host's engine queued the NPC's own reaction, whose row reaches this copy through the host's capture (WO-151):
    /// then nothing is added (never two answers to one blow).</remarks>
    public static int BlockActionFor(HitVerdict v, byte react)
    {
        if (react == 1) return -1;
        return v switch
        {
            HitVerdict.Blocked or HitVerdict.Parried => ActionRowCatalog.ActionBlock,
            HitVerdict.PerfectBlock => ActionRowCatalog.ActionPerfectBlock,
            HitVerdict.Broken => ActionRowCatalog.ActionBlockBroken,
            _ => -1,
        };
    }
}
