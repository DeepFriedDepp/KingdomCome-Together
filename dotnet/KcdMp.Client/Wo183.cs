// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>
/// WO-183 (0.50.9, docs/WO-183-findings.md): the pure rules of the playtest patch. GameBridge.Wo183.cs is the bridge.
///   1  shared scenes are pre-rendered videos only; the one move left is the partner's landing beside the triggerer after a video
///      (public ground only, never during an arrest, a bring-along held behind the partner's own scene waits at most 30 s)
///   3  the mod never saves to unstick a scene; every save the mod asks for on the joiner is the Henry snapshot, through ONE gate
///      (<see cref="SaveBlock"/>), owed and retried every 15 s when the game cannot save now
///   4b a copy is released as "left combat" only after the host's NPC has shown combat=0 for 1.5 s; a change of target is no leave
/// Wo183Tests pins these.
/// </summary>
public static class Wo183Rules
{
    // ---- 1: the landing ----
    /// <summary>The landing's rings around the triggering player, nearest first (metres): the old 3, 6 and 10, then 15 and 25.</summary>
    public static readonly float[] LandingRingsM = [3f, 6f, 10f, 15f, 25f];
    /// <summary>How many directions are tried on each ring (the DLL's own ground search uses 8).</summary>
    public const int RingDirections = 8;
    /// <summary>A bring-along held behind this player's own scene waits at most this long, then is dropped (never forever).</summary>
    public const long BringHeldMaxMs = 30_000;
    /// <summary>The toast when no ring spot is public.</summary>
    public const string NoPublicSpotText = "Couldn't bring you to your partner, they're somewhere private.";

    /// <summary>The ring spots of one ring around (x,y): <see cref="RingDirections"/> points at 45-degree steps, as the DLL's ground search takes them.</summary>
    public static (float X, float Y)[] RingPoints(float x, float y, float ringM)
    {
        var p = new (float, float)[RingDirections];
        for (int k = 0; k < RingDirections; k++)
        {
            double a = Math.PI / 4.0 * k;
            p[k] = (x + (float)(ringM * Math.Cos(a)), y + (float)(ringM * Math.Sin(a)));
        }
        return p;
    }

    /// <summary>The game's own area state of a spot ("public" | "private" | "open" | "unknown", KCD2MP_W157AreaAt): only a public one is a landing.</summary>
    public static bool IsPublicSpot(string? area) => string.Equals(area, "public", StringComparison.Ordinal);

    /// <summary>The first ring (in order) with a public spot, and that spot's index; (-1,-1) when no ring has one. areas[ring][k] is the area state.</summary>
    public static (int Ring, int Spot) FirstPublic(IReadOnlyList<IReadOnlyList<string>> areas)
    {
        for (int r = 0; r < areas.Count; r++)
            for (int k = 0; k < areas[r].Count; k++)
                if (IsPublicSpot(areas[r][k])) return (r, k);
        return (-1, -1);
    }

    // ---- 3: the one save owner on the joiner ----
    /// <summary>A save that is not done is owed and retried this often.</summary>
    public const long OwedRetryMs = 15_000;
    /// <summary>One toast when a snapshot is still owed this long after the host's save (so a real failure is not silent).</summary>
    public const long OwedToastMs = 120_000;
    /// <summary>The owed snapshot is given up after this many tries (a quarter hour) -- logged; the next host save starts it again.</summary>
    public const int OwedMaxTries = 60;
    /// <summary>A "BeforeGameSave pending" belief with no "ready" line this long is stale: the save is tried (the engine refuses harmlessly).</summary>
    public const long PendingStaleMs = 30_000;

    /// <summary>What the joiner's game is doing, for the one question "can it save now?".</summary>
    public readonly record struct SaveGate(bool InWorld, bool Downed, bool SceneRunning, bool Conversation, bool CrimeProcess, bool FaderBlack,
                                           bool Menu, bool SavePending, bool Joining);

    /// <summary>"" = the game can save now; else the first reason it cannot. A pending save of any kind comes first: the engine refuses or queues a second one.</summary>
    public static string SaveBlock(SaveGate g)
    {
        if (g.SavePending) return "another save is pending";
        if (g.Joining) return "a join is running";
        if (!g.InWorld) return "not in the world (loading)";
        if (g.Menu) return "a menu is open";
        if (g.Downed) return "downed";
        if (g.CrimeProcess) return "an arrest, a crime stop or a punishment is running";
        if (g.SceneRunning) return "a scene is running";
        if (g.Conversation) return "a conversation is running";
        if (g.FaderBlack) return "the screen is faded";
        return "";
    }

    /// <summary>
    /// The engine's own save lines, read from kcd.log: "Readiness observer 'BeforeGameSave' ... started async waiting" opens a pending
    /// save, "... is ready" closes it; the engine's refusal "... is ignored, because there is another savegame pending" means one is.
    /// </summary>
    public sealed class PendingSaves
    {
        private long _since;   // 0 = none pending

        public const string ObserverPrefix = "Readiness observer 'BeforeGameSave'";
        public const string RefusedMarker = "because there is another savegame pending";

        /// <summary>Feed one engine line; true when it was a save line.</summary>
        public bool Feed(string? line, long nowMs)
        {
            if (line is null) return false;
            if (line.StartsWith(ObserverPrefix, StringComparison.Ordinal))
            {
                if (line.Contains("started async waiting", StringComparison.Ordinal)) Volatile.Write(ref _since, Math.Max(nowMs, 1));
                else if (line.Contains("is ready", StringComparison.Ordinal)) Volatile.Write(ref _since, 0);
                return true;
            }
            if (line.Contains(RefusedMarker, StringComparison.Ordinal)) { if (Volatile.Read(ref _since) == 0) Volatile.Write(ref _since, Math.Max(nowMs, 1)); return true; }
            return false;
        }

        /// <summary>A pending save the engine has not finished (and not older than <see cref="PendingStaleMs"/>).</summary>
        public bool IsPending(long nowMs) { long s = Volatile.Read(ref _since); return s != 0 && nowMs - s < PendingStaleMs; }

        /// <summary>A pending save that has stayed pending past the stale bound (the engine's own readiness never came).</summary>
        public bool IsStuck(long nowMs) { long s = Volatile.Read(ref _since); return s != 0 && nowMs - s >= PendingStaleMs; }

        public void Clear() => Volatile.Write(ref _since, 0);
    }

    // ---- 4b: a copy is released as "left combat" only after the host's NPC held combat=0 for 1.5 s ----
    public const long LeftCombatDebounceMs = 1_500;

    /// <summary>
    /// The sender reports combat 0 for an NPC (target none) between two targets (a retarget). Tracks, per NPC, since when the report has
    /// been combat=0 without a break; a combat=1 report (any target: the host, another player) resets it.
    /// </summary>
    public sealed class LeftCombat
    {
        private readonly Dictionary<string, long> _zeroSince = new(StringComparer.Ordinal);

        /// <summary>One report. Returns true when the copy is to be released: combat=0 has held for <see cref="LeftCombatDebounceMs"/>.</summary>
        public bool Report(string npc, bool inCombat, long nowMs)
        {
            lock (_zeroSince)
            {
                if (inCombat) { _zeroSince.Remove(npc); return false; }
                if (!_zeroSince.TryGetValue(npc, out long since)) { _zeroSince[npc] = nowMs; return false; }
                return nowMs - since >= LeftCombatDebounceMs;
            }
        }

        public void Forget(string npc) { lock (_zeroSince) _zeroSince.Remove(npc); }
        public void Clear() { lock (_zeroSince) _zeroSince.Clear(); }
        public bool Zero(string npc, out long since) { lock (_zeroSince) return _zeroSince.TryGetValue(npc, out since); }
    }

    /// <summary>"WO164-SWEEP release+neutral" is never run on a copy that is engaged or in combat.</summary>
    public static bool SweepAllowed(bool copyEngaged, bool copyInCombat) => !(copyEngaged || copyInCombat);

    // ---- the crime process (arrest, crime stop, punishment) ----
    /// <summary>A scene name of a punishment (WO-179's rule, kept here for the gates): "crime_..." holders and faders.</summary>
    public static bool IsCrimeName(string? name) => name is not null && name.StartsWith("crime_", StringComparison.OrdinalIgnoreCase);

    /// <summary>The count text of the owed snapshot for the log.</summary>
    public static string Seconds(long ms) => (ms / 1000.0).ToString("F1", CultureInfo.InvariantCulture);
}
