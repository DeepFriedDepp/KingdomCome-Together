// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-183 -- 0.50.9: the bridge half (Wo183Rules has the pure rules; docs/WO-183-findings.md has the evidence).
//
//   the save owner (item 3)   the joiner's game is asked to save ONLY for the Henry snapshot paired with a host save, through one gate:
//                             nothing pending, no scene, no arrest / crime stop / punishment, not downed, no menu, not loading. A snapshot
//                             the game cannot take now is OWED and retried every 15 s with the newest host save it follows; a log line per
//                             owed snapshot and one toast when it is still owed after 2 minutes. The scene rescue saves (WO-151, WO-167 T4,
//                             WO-179's positioning lift) are gone: in the field one of them, asked mid-arrest, stayed pending and the engine
//                             refused every later save of that machine (34 rescues and both of the host's snapshots).
//   the crime process         an arrest, a crime stop or a punishment is read from the game's own words (a crime_ scene, a guard's stop,
//                             a punishment dialogue); nothing of the mod moves a player in it (the landing waits, the leash does not pull).
//   the landing's area check  KCD2MP_W183AreasAt asks the game's own area labels (WO-157's) for each ring spot.
public partial class GameBridge
{
    private readonly Wo183Rules.PendingSaves _w183Pending = new();
    private long _w183PunishDialogueMs;
    private long _w183SnapOwed, _w183SnapLate, _w183SnapDropped, _w183SnapToasts, _w183GateRefusals;

    private sealed class W183Owed
    {
        public WorldSaved W;
        public byte Src;
        public long SinceMs, LastTryMs;
        public int Tries;
        public bool Toasted;
        public string Why = "";
    }
    private W183Owed? _w183Owed;
    private readonly object _w183Lock = new();

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

    // ================================================================ the save gate

    private Wo183Rules.SaveGate W183Gate()
    {
        long now = Environment.TickCount64;
        byte ps = Wo138PauseState();
        bool scene = _w179Cur is not null || (ps & Wo138Codec.ReasonCutscene) != 0
                     || Wo170Rules.ScenesRunning(Interlocked.Read(ref _w167IsmEnq), Interlocked.Read(ref _w167IsmEnd)) > 0
                     || _w151Scenes.Values.Any(s => s.EndAtMs == 0 && now - s.PlayAtMs < 5 * 60_000);
        return new Wo183Rules.SaveGate(
            InWorld: _where is not (GameWhere.Menu or GameWhere.Loading),
            Downed: _localDowned,
            SceneRunning: scene,
            Conversation: _w179DudeStartMs > 0 || (ps & Wo138Codec.ReasonDialogue) != 0,
            CrimeProcess: W183CrimeProcess() != "",
            FaderBlack: _w179Faded,
            Menu: (ps & (Wo138Codec.ReasonMenu | Wo138Codec.ReasonLoad)) != 0,
            SavePending: _w183Pending.IsPending(now),
            Joining: _jj is not null || _rewinding);
    }

    /// <summary>"" = the joiner's game can save now (the one gate every save request goes through); else why not.</summary>
    private string W183SaveBlock() => Wo183Rules.SaveBlock(W183Gate());

    /// <summary>The engine's own save lines (the log tail forwards them): a pending save, its readiness, the refusal of a second one.</summary>
    private void Wo183OnEngineLine(string line)
    {
        long now = Environment.TickCount64;
        bool wasPending = _w183Pending.IsPending(now);
        if (_w183Pending.Feed(line, now))
        {
            bool pending = _w183Pending.IsPending(now);
            if (pending != wasPending || line.Contains(Wo183Rules.PendingSaves.RefusedMarker, StringComparison.Ordinal))
                Console.WriteLine($"WO183-SAVE engine: {(line.Contains(Wo183Rules.PendingSaves.RefusedMarker, StringComparison.Ordinal) ? "a save was REFUSED because another is pending" : pending ? "a save is pending (BeforeGameSave waiting)" : "the pending save is ready")}");
            return;
        }
        if (line.StartsWith("New dialogue '", StringComparison.Ordinal) && line.Contains(".nextnextgenpunishment.", StringComparison.Ordinal))
            Volatile.Write(ref _w183PunishDialogueMs, now);
    }

    // ================================================================ the owed snapshot

    /// <summary>The snapshot cannot be taken now: owed, retried every 15 s with the newest host save it follows.</summary>
    private void Wo183Owe(WorldSaved w, byte src, string why, long sinceMs = 0)
    {
        long now = Environment.TickCount64;
        lock (_w183Lock)
        {
            if (_w183Owed is { } o)
            {
                Console.WriteLine($"WO183-SNAP still owed: host save seq={w.Seq} supersedes seq={o.W.Seq} (the snapshot pairs with the newest host save it follows); why={why}");
                o.W = w; o.Src = src; o.Why = why;
                return;
            }
            _w183Owed = new W183Owed { W = w, Src = src, SinceMs = sinceMs > 0 ? sinceMs : now, LastTryMs = now, Why = why };
        }
        Interlocked.Increment(ref _w183SnapOwed);
        Console.WriteLine($"WO183-SNAP owed: host save seq={w.Seq} -- the game cannot save now ({why}); retried every {Wo183Rules.OwedRetryMs / 1000} s, no save request meanwhile");
    }

    /// <summary>1 s (the WO-151 loop): retry the owed snapshot every 15 s; one toast when it is still owed after 2 minutes.</summary>
    private async Task Wo183TickAsync()
    {
        W183Owed? o;
        lock (_w183Lock) o = _w183Owed;
        if (o is null) return;
        long now = Environment.TickCount64;
        if (!_joinedWorld || _where is GameWhere.Menu or GameWhere.Loading)
        {
            lock (_w183Lock) _w183Owed = null;
            Console.WriteLine($"WO183-SNAP owed host save seq={o.W.Seq} dropped: this game left the host's world (the progress kept is up to the previous pair)");
            return;
        }
        if (now - o.LastTryMs < Wo183Rules.OwedRetryMs) return;
        o.LastTryMs = now;
        o.Tries++;
        if (o.Tries > Wo183Rules.OwedMaxTries)
        {
            lock (_w183Lock) _w183Owed = null;
            Interlocked.Increment(ref _w183SnapDropped);
            Console.WriteLine($"WO183-SNAP owed host save seq={o.W.Seq} given up after {o.Tries - 1} tries ({Wo183Rules.Seconds(now - o.SinceMs)} s): the progress kept is up to the previous pair; the next host save starts again");
            return;
        }
        string block = W183SaveBlock();
        if (block != "")
        {
            Interlocked.Increment(ref _w183GateRefusals);
            if (block != o.Why) { Console.WriteLine($"WO183-SNAP still owed (try {o.Tries}, {Wo183Rules.Seconds(now - o.SinceMs)} s): {block}"); o.Why = block; }
            if (!o.Toasted && now - o.SinceMs >= Wo183Rules.OwedToastMs)
            {
                o.Toasted = true;
                Interlocked.Increment(ref _w183SnapToasts);
                Console.WriteLine($"WO183-SNAP still owed after {Wo183Rules.Seconds(now - o.SinceMs)} s ({block}) -- the player is told once");
                await W164NoticeAsync("Your character's progress hasn't been saved for a while. It is saved as soon as the game allows.");
            }
            return;
        }
        lock (_w183Lock) _w183Owed = null;
        Interlocked.Increment(ref _w183SnapLate);
        Console.WriteLine($"WO183-SNAP the game can save now: owed host save seq={o.W.Seq} is taken {Wo183Rules.Seconds(now - o.SinceMs)} s late (try {o.Tries})");
        await Wo125OnHostWorldSavedAsync(o.W, o.Src, o.SinceMs);
    }

    /// <summary>A load, a rejoin or the session's end: nothing owed survives it.</summary>
    private void Wo183Reset(string why)
    {
        W183Owed? o;
        lock (_w183Lock) { o = _w183Owed; _w183Owed = null; }
        _w183Pending.Clear();
        if (o is not null) Console.WriteLine($"WO183-SNAP owed host save seq={o.W.Seq} dropped ({why})");
    }

    private string Wo183StatsText() =>
        $"snap_owed={Interlocked.Read(ref _w183SnapOwed)} snap_late={Interlocked.Read(ref _w183SnapLate)} snap_dropped={Interlocked.Read(ref _w183SnapDropped)} snap_toasts={Interlocked.Read(ref _w183SnapToasts)} gate_refusals={Interlocked.Read(ref _w183GateRefusals)} pending_now={On(_w183Pending.IsPending(Environment.TickCount64))}";

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
