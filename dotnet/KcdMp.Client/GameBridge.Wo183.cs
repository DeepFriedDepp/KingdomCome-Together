// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-183 -- 0.50.9: the bridge half (Wo183Rules has the pure rules; docs/WO-183-findings.md has the evidence).
//
//   the save owner (item 3)   WO-184 (0.60.0) replaced it: its gate waited on mod flags with no end (a conversation clock that combat shouts
//                             started and nothing ended) and silently stopped every later snapshot. GameBridge.Wo184.cs. The removal of the
//                             scene rescue saves (WO-151, WO-167 T4, WO-179's positioning lift) stays.
//   the crime process         an arrest, a crime stop or a punishment is read from the game's own words (a crime_ scene, a guard's stop,
//                             a punishment dialogue); nothing of the mod moves a player in it (the landing waits, the leash does not pull).
//   the landing's area check  KCD2MP_W183AreasAt asks the game's own area labels (WO-157's) for each ring spot.
public partial class GameBridge
{
    private long _w183PunishDialogueMs;

    // 4b: the "left combat" clock per NPC (a retarget is combat=0 for a moment), the releases waiting on it
    private readonly Wo183Rules.LeftCombat _w183Left = new();
    private readonly ConcurrentDictionary<string, long> _w183LeftWatch = new(StringComparer.Ordinal);
    private long _w183LeftHeld, _w183SweepSkipped;

    // ================================================================ the crime process

    /// <summary>
    /// "" = none; else what runs: a punishment scene (crime_*), a guard's crime stop on this joiner, or a punishment dialogue in the last
    /// 20 s. Nothing of the mod moves or saves a player while it runs.
    /// </summary>
    private string W183CrimeProcess()
    {
        long now = Environment.TickCount64;
        foreach (var s in _w151Scenes.Values)
            if (Wo183Rules.IsCrimeName(s.Name) && now - s.PlayAtMs < 5 * 60_000) return $"punishment scene '{s.Name}'";
        var cur = _w179Cur;
        if (cur is not null && Wo183Rules.IsCrimeName(cur.Name) && !cur.Ended && now - cur.StartMs < 5 * 60_000) return $"punishment scene '{cur.Name}'";
        if (!_w139StoppedHere.IsEmpty) return "a guard's crime stop";
        if (now - Volatile.Read(ref _w183PunishDialogueMs) < 20_000) return "a punishment dialogue";
        return "";
    }

    /// <summary>"" = a bring-along may move this player now; else why not (his own scene, an arrest).</summary>
    private string Wo183MoveBlock()
    {
        string crime = W183CrimeProcess();
        if (crime != "") return $"this player is in an arrest ({crime})";
        var own = _w179Cur;
        if (own is not null && !own.Ended) return $"this player's own scene '{own.Name}' runs first";
        return "";
    }

    // ================================================================ the engine's lines

    /// <summary>A punishment dialogue starting (the crime process's 20 s window). WO-184: the save lines are GameBridge.Wo184.cs's now.</summary>
    private void Wo183OnEngineLine(string line)
    {
        if (line.StartsWith("New dialogue '", StringComparison.Ordinal) && line.Contains(".nextnextgenpunishment.", StringComparison.Ordinal))
            Volatile.Write(ref _w183PunishDialogueMs, Environment.TickCount64);
    }

    // ================================================================ the landing's area check

    /// <summary>
    /// The game's own area state of each spot ("public" | "private" | "open" | "unknown"), one answer per spot in order -- the WO-157 labels
    /// (XGenAIModule.IsPointInAreaWithLabel). No answer from the mod = every spot unknown (a landing then needs no luck: it is refused).
    /// </summary>
    private async Task<List<string>> Wo183AreasAtAsync(IReadOnlyList<(float X, float Y, float Z)> pts)
    {
        string spec = string.Join(';', pts.Select(p => FormattableString.Invariant($"{p.X:F1},{p.Y:F1},{p.Z:F1}")));
        string r = await AskModAsync("KCD2MP_W183AreasAt", 3000, $"\"{spec}\"");
        var res = new List<string>(pts.Count);
        const string key = "areas=";
        if (r.StartsWith(key, StringComparison.Ordinal))
            res.AddRange(r[key.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries));
        while (res.Count < pts.Count) res.Add("unknown");
        return res;
    }
}
