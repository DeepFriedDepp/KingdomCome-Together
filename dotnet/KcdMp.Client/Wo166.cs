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
