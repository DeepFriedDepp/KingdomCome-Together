// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>WO-166 (docs/WO-166-findings.md): the pure rules of 0.48.2's agent half. The bridge is GameBridge.Wo166.cs.</summary>
public static class Wo166Rules
{
    // ------------------------------------------------------------------ L1, T3: the engine's lines

    /// <summary>L1: the loot screen closing ("PlayAudio: ui_inv_screen_out_one_pane" in both field crashes).</summary>
    public const string LootScreenOutPrefix = "PlayAudio: ui_inv_screen_out";

    /// <summary>T3: the dialogue controller's request that the NPC stop its behaviour for the conversation ran out (the haggle's death).</summary>
    public const string PauseRequestsTimedOut = "PlayerDialogController::NPCPauseRequests timed out";

    // ------------------------------------------------------------------ C3 / C4 / C1

    /// <summary>C3 mp_copy_strikes, C4 mp_snap_fix: new mechanisms ship on, each with its switch (the maintainer's standing rule).</summary>
    public const bool DefaultCopyStrikes = true, DefaultSnapFix = true;

    /// <summary>C1: the figure's combat automation while held in combat (native wo166.h: 0 all off -- 0.48.0, 1 all on, 2.. byte patterns).</summary>
    public const byte DefaultAutoMode = 0, MaxAutoMode = 9;

    /// <summary>C3: a striking window's request -- the row's own timings (ms; -1 when its table has none) and its attack fields.</summary>
    public readonly record struct Strike(int StartMs, int HitMs, sbyte Type, sbyte Zone, sbyte Hand, float Strength);

    /// <summary>
    /// C3: the window for a row played on a copy. Null when the row carries no attack type (a defence, a gesture: nothing to strike with).
    /// Zone: the row's own, else the table's default (2, upper right -- WO-165's replay uses the same); strength 1.0 (lower ones round to 0
    /// in the hit core, WO-165); the right hand.
    /// </summary>
    public static Strike? StrikeFor(ActionRowCatalog.Row row)
    {
        if (row.AttackType < 0 || row.AttackType > 15) return null;
        int start = row.TimeToStart > 0 ? (int)MathF.Round(row.TimeToStart * 1000f) : -1;
        int hit = row.TimeToHit > 0 ? (int)MathF.Round(row.TimeToHit * 1000f) : -1;
        sbyte zone = (sbyte)(row.Zone is >= 0 and <= 5 ? row.Zone : 2);
        return new Strike(start, hit, (sbyte)row.AttackType, zone, 1, 1.0f);
    }

    /// <summary>
    /// C4: the writer's hold for a swing -- with mp_snap_fix the swing's own start+hit time + 250 ms (300..900 ms); otherwise, or for a row
    /// without timings, the flat 900 ms of 0.48.0. The DLL also caps a stretch of chained holds (npc_drive.cpp kHoldChainCapS).
    /// </summary>
    public static ushort SwingHoldMs(int hitLagMs, bool snapFix) =>
        (ushort)(!snapFix || hitLagMs <= 0 ? 900 : Math.Clamp(hitLagMs + 250, 300, 900));

    /// <summary>The Lua's "w166_cfg key=value ..." event.</summary>
    public static IEnumerable<(string Key, string Value)> ParseCfg(string? arg)
    {
        foreach (var part in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || eq == part.Length - 1) continue;
            string k = part[..eq], v = part[(eq + 1)..];
            if (k is "copy_strikes" or "snap_fix" && v is "on" or "off") yield return (k, v);
            else if (k == "auto_mode" && v.Length <= 2 && v.All(char.IsAsciiDigit)) yield return (k, v);
        }
    }

    /// <summary>
    /// T3: "[ID: 2122] Dialog interrupted. [Ex0: Dude Ex1: tzel_woman_9 state: WAITING_FOR_TWINS flags: 2107913]" (and the "Dialog ending" /
    /// "Dialog ends but no response" forms) -> the souls and the engine's dialogue state.
    /// </summary>
    public static bool TryParseDialogState(string line, out List<string> souls, out string state)
    {
        souls = new List<string>(); state = "";
        if (!line.StartsWith("[ID: ", StringComparison.Ordinal) || !line.Contains("] Dialog ", StringComparison.Ordinal)) return false;
        int sq = line.IndexOf("[Ex0: ", StringComparison.Ordinal);
        if (sq < 0) sq = line.IndexOf("[Nx0: ", StringComparison.Ordinal);
        if (sq < 0) return false;
        var f = line[(sq + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < f.Length; i++)
        {
            if (f[i] == "state:") { state = f[i + 1].TrimEnd(']'); break; }
            if (f[i].Length >= 4 && (f[i][0] == 'E' || f[i][0] == 'N') && f[i][1] == 'x' && f[i].EndsWith(':') && Wo137Text.IsNpc(f[i + 1])) { souls.Add(f[i + 1]); i++; }
        }
        return state.Length > 0 && souls.Count > 0;
    }
}
