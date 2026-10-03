// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-154 Phase 1: a quest State that each world re-derives by itself.
///
/// Until 0.44.0 a quest change made on an AI worker thread (a distance trigger, a duel's state, a
/// farmhand's evening) carried no port, so it was never mirrored either way: the joiner's mirror
/// applied 5 of 54 of the host's changes in the field evening, and none of his own fight steps
/// reached the host. With the port recorded on every thread (native wo137.cpp) those changes now
/// mirror both ways -- and a State whose value each world computes from its OWN player (how close
/// he stands to something) would bounce: the joiner's value goes to the host, the host's world puts
/// its own value back, that comes to the joiner, his world puts his back, and so on.
///
/// The rule: when the other world's value was put on a State here (a host change applied on a
/// joiner, a joiner's request applied on the host) and this world's own graph puts the State
/// straight back to the value it had before, within <see cref="WindowMs"/>, the State is
/// CONTESTED for the rest of the session: each world keeps its own value, nothing about it is
/// applied, asked or corrected any more. A story step is never put back like that (a counter that
/// moves on is progress, not a return), so only per-player States are caught; the first bounce is
/// the cost of finding one.
/// </summary>
public sealed class Wo154Contest
{
    public const long WindowMs = 30_000;
    public const int MaxTracked = 2000;

    private readonly object _lock = new();
    private readonly Dictionary<string, (int Before, int After, long AtMs)> _imposed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _contested = new(StringComparer.Ordinal);

    public int Count { get { lock (_lock) return _contested.Count; } }

    /// <summary>The other world's value was put on this State here.</summary>
    public void NoteImposed(string path, int before, int after, long nowMs)
    {
        if (before == after || string.IsNullOrEmpty(path)) return;
        lock (_lock)
        {
            if (_contested.ContainsKey(path)) return;
            _imposed[path] = (before, after, nowMs);
            if (_imposed.Count > MaxTracked)
                foreach (var k in _imposed.Where(kv => nowMs - kv.Value.AtMs > WindowMs).Select(kv => kv.Key).ToList())
                    _imposed.Remove(k);
        }
    }

    /// <summary>
    /// This world's own change (a root, never a mirror). True when it is a NEW contest: the State went
    /// straight back from the imposed value to the one before it, within the window.
    /// </summary>
    public bool NoteLocal(string path, int oldVal, int newVal, long nowMs, out string why)
    {
        why = "";
        lock (_lock)
        {
            if (_contested.ContainsKey(path) || !_imposed.TryGetValue(path, out var im)) return false;
            if (nowMs - im.AtMs > WindowMs) { _imposed.Remove(path); return false; }
            if (oldVal != im.After || newVal != im.Before) return false;
            why = string.Format(CultureInfo.InvariantCulture, "this world put it back from {0} to {1} {2:F1} s after the other world's value arrived",
                                im.After, im.Before, (nowMs - im.AtMs) / 1000.0);
            _contested[path] = why;
            _imposed.Remove(path);
            return true;
        }
    }

    public bool IsContested(string path) { lock (_lock) return _contested.ContainsKey(path); }

    /// <summary>The other side found it contested. True when it is new here.</summary>
    public bool Mark(string path, string why)
    {
        lock (_lock)
        {
            _imposed.Remove(path);
            return _contested.TryAdd(path, why);
        }
    }

    public void Reset()
    {
        lock (_lock) { _imposed.Clear(); _contested.Clear(); }
    }
}

/// <summary>WO-154: the pure rules of this WO (docs/WO-154-findings.md).</summary>
public static class Wo154Rules
{
    /// <summary>The host's verdict for a joiner's request on a contested State (the joiner stops asking and correcting it).</summary>
    public const string VerdictContested = "contested";

    /// <summary>
    /// Phase 1: a joiner's steps on one State made by an AI behaviour (worker flag), merged into their net result
    /// before the host judges them. Live L1: the field's Moravian fight replayed step by step put the host's own duel
    /// into InProgress, whose own logic (the host's Henry was not in the arena) resolved it Lost at once; the joiner's
    /// Won that came 106 ms later was refused. Merged, 0->1->2 is one Won, 0->1->0 is nothing. A State waits until
    /// <see cref="QuietMs"/> after its last step (at most <see cref="MaxWaitMs"/> after its first).
    /// </summary>
    public sealed class WorkerCoalescer
    {
        public const long QuietMs = 1500, MaxWaitMs = 5000;
        public sealed record Item(byte Src, string Path, int FirstOld, int LastNew, string LastPort, byte Flags, int QuestLen,
                                  List<uint> Toks, long FirstAtMs, long LastAtMs);
        private readonly List<Item> _items = new();
        public int Count { get { lock (_items) return _items.Count; } }

        /// <summary>One step in. True when it joined a State already waiting (a merge).</summary>
        public bool Add(byte src, uint tok, QuestChange req, long nowMs)
        {
            lock (_items)
            {
                int i = _items.FindIndex(x => x.Src == src && x.Path == req.Path);
                if (i < 0)
                {
                    _items.Add(new Item(src, req.Path, req.Old, req.New, req.Port, req.Flags, req.QuestLen, new List<uint> { tok }, nowMs, nowMs));
                    return false;
                }
                var x = _items[i];
                x.Toks.Add(tok);
                _items[i] = x with { LastNew = req.New, LastPort = req.Port, LastAtMs = nowMs };
                return true;
            }
        }

        /// <summary>The States whose wait is over (all of them with <paramref name="all"/>), in the order they first arrived.</summary>
        public List<Item> TakeReady(long nowMs, bool all = false)
        {
            lock (_items)
            {
                var ready = _items.Where(x => all || nowMs - x.LastAtMs >= QuietMs || nowMs - x.FirstAtMs >= MaxWaitMs).ToList();
                foreach (var x in ready) _items.Remove(x);
                return ready;
            }
        }

        /// <summary>The merged request (its worker flag cleared: judged as one step, never merged again).</summary>
        public static QuestChange Merged(Item x) =>
            new(0, (byte)(x.Flags & ~QuestChange.FWorker), x.FirstOld, x.LastNew, x.LastPort, "", x.Path, x.QuestLen);
    }

    /// <summary>
    /// Phase 2: a partner's Downed bit, edge by edge. The SENDER's DLL debounces the bit (it reads every frame;
    /// a body that flickers through a ragdoll does not set it), and the state block travels only on a change
    /// (plus a 1 s heartbeat while non-zero), so a receiver-side wait for a second clear sample would never end
    /// (live L1: the avatar never stood up). Down on the first set sample, up on the first clear one.
    /// </summary>
    public sealed class DownEdge
    {
        public const long DownAfterMs = 0, UpAfterMs = 0;
        public bool Down { get; private set; }
        private long _bitSince = -1, _clearSince = -1;

        /// <summary>One sample. True = it just went down, false = it just came up, null = no change.</summary>
        public bool? Feed(bool bit, long nowMs)
        {
            if (bit) { _clearSince = -1; if (_bitSince < 0) _bitSince = nowMs; }
            else { _bitSince = -1; if (_clearSince < 0) _clearSince = nowMs; }
            if (!Down && bit && nowMs - _bitSince >= DownAfterMs) { Down = true; return true; }
            if (Down && !bit && nowMs - _clearSince >= UpAfterMs) { Down = false; return false; }
            return null;
        }
    }

    /// <summary>
    /// Phase 3: the host's guards leave a partner alone while he is down, for <see cref="RespiteMs"/> after he is up
    /// again (a death's respawn, a knockout's or a knockdown's wake), and for <see cref="UnstuckRespiteMs"/> after he
    /// ended his fights (mp_unstuck): no stop, no pursuit, and a stop's "fought"/"fled" is no resist then. The field:
    /// an arrest at his body 56 ms after he went down, and a stop judged "fled" 48 s after his respawn that sent three
    /// guards at him again (his third death). A down that never sees an up stops counting after <see cref="MaxDownMs"/>
    /// (WO-132's LifeGate rule: a lost up never blocks a partner for the rest of the session).
    /// </summary>
    public sealed class GuardRespite
    {
        public const long RespiteMs = 120_000, UnstuckRespiteMs = 30_000, MaxDownMs = 180_000;
        private long _downAt = -1, _until = -1;

        public bool IsDown(long nowMs) => _downAt >= 0 && nowMs - _downAt < MaxDownMs;
        public void Down(long nowMs) => _downAt = nowMs;
        public void Up(long nowMs) { _downAt = -1; _until = Math.Max(_until, nowMs + RespiteMs); }
        public void Unstuck(long nowMs) => _until = Math.Max(_until, nowMs + UnstuckRespiteMs);

        /// <summary>null: the guards may act on him. Otherwise why not, in a word or two.</summary>
        public string? Blocked(long nowMs) =>
            IsDown(nowMs) ? "down" : nowMs < _until ? FormattableString.Invariant($"respite {(_until - nowMs + 999) / 1000} s") : null;
    }

    /// <summary>
    /// Phase 4.4: why a first join has no save of this player's to bring, as a code the launcher puts in plain words
    /// (AgentStatusBanner.PlainReason). The field: "Your saves are from 1.5.5 -- host 1.5.5" (the regular game's saves,
    /// build 15315, beside the Modding Tools' in the shared saves folder), and a tester whose only Modding Tools saves
    /// were copies of the host's world. Counts are of the saves folder's engine saves.
    /// </summary>
    public static string NoSaveReason(int allSaves, int ownSaves, int ownSameBuild, int ownRegularGame, string? hostBuild)
    {
        if (allSaves == 0) return "no-saves";
        if (ownSaves == 0) return "only-host-copies";
        if (hostBuild is not null && ownSameBuild == 0) return ownRegularGame == ownSaves ? "regular-game-saves" : "wrong-build";
        return "no-henry";   // own saves of the host's build, none with Henry in it (the prologue: its player is Godwin)
    }

    /// <summary>The regular game's BuildInfo carries a build number ("1.5.5-15315-release_1_5"); the Modding Tools' does not.</summary>
    public static bool IsRegularGameBuild(string? build) =>
        build is not null && System.Text.RegularExpressions.Regex.IsMatch(build, @"^\d+(?:\.\d+)+-\d+(?:-|$)");

    /// <summary>The joiner's own words for <see cref="NoSaveReason"/> (the in-game toast; the launcher has its own).</summary>
    public static string NoSaveMessage(string reason, bool canFresh, string? theirs, string? host)
    {
        string why = reason switch
        {
            "no-saves" => "You have no Modding Tools saves yet.",
            "only-host-copies" => "Your only Modding Tools saves are copies of this same world.",
            "regular-game-saves" => "Your saves are from the regular game, not the Modding Tools.",
            "wrong-build" => Wo135Rules.WrongBuildMessage(theirs, host ?? ""),
            _ => "Your Modding Tools saves have no character of yours in them yet (only the prologue).",
        };
        if (canFresh) return why + " You can join with a new character.";
        return reason == "wrong-build" ? why : why + " Start a new game in the Modding Tools once and play past the prologue, then join again.";
    }

    /// <summary>
    /// Phase 4.2: a joiner's load is given up only on a responsive main menu, never while the game is busy. The field:
    /// a load that held the main thread 50+ s (the console runs nothing then, and kcd.log's "Loading saved game" came
    /// late); the 20 s give-up deleted the world file under it. Feed every where-probe answer ("menu", "world",
    /// "loading", "busy" = no answer in time, "unknown"); the verdict is Wait until the game answers, not loading,
    /// <see cref="IdleAnswersToAbort"/> times in a row (Abort: the load never took), or answers from a world once the
    /// engine had accepted the load (InWorld: it got there, its log line late). Before the engine accepted the load a
    /// world answer is the player's own world still (a join from it): it counts as idle. <see cref="MaxWaitS"/> bounds
    /// the wait of a game that never answers again.
    /// </summary>
    public sealed class JoinLoadWatch
    {
        public const int IdleAnswersToAbort = 3;
        public const double MaxWaitS = 900;
        public enum Verdict { Wait, Abort, InWorld, GaveUpBusy }
        private int _idle;
        public string Last { get; private set; } = "";

        public Verdict Feed(string where, double waitedS, bool loadAccepted)
        {
            Last = where;
            if (where == "world" && loadAccepted) return Verdict.InWorld;
            _idle = where is "menu" or "world" ? _idle + 1 : 0;
            if (_idle >= IdleAnswersToAbort) return Verdict.Abort;
            return waitedS >= MaxWaitS ? Verdict.GaveUpBusy : Verdict.Wait;
        }
    }

    /// <summary>
    /// Phase 4.2 (live L7, and the field's hung join): a load from the main menu can freeze for good -- the main thread waits
    /// for the render thread at the loading screen, the render thread waits inside the game's video player (Bink) for its
    /// decode threads, and those wait too: no CPU, no log line, the menu's last frame on screen. A slow load works (the L6
    /// load used the CPU for 46 s); a frozen one does not. Frozen = the game has been busy (no console answer) for at least
    /// <see cref="BusyMinS"/> and used less than <see cref="CpuMaxS"/> of CPU in the last <see cref="WindowS"/>.
    /// No CPU reading (the process not found or not readable) never declares a freeze.
    /// </summary>
    public sealed class FrozenWatch
    {
        public const double BusyMinS = 90, WindowS = 60, CpuMaxS = 1.5;
        private readonly List<(double T, double Cpu)> _samples = new();
        private double _busySince = -1;
        public double BusyForS { get; private set; }
        /// <summary>The game's CPU seconds over the window, scaled to <see cref="WindowS"/>.</summary>
        public double CpuInWindowS { get; private set; } = double.NaN;

        /// <summary>One look: busy = no console answer; cpuS = the game process's total CPU seconds, or null.</summary>
        public bool Feed(bool busy, double nowS, double? cpuS)
        {
            if (!busy || cpuS is not double cpu) { _samples.Clear(); _busySince = -1; BusyForS = 0; CpuInWindowS = double.NaN; return false; }
            if (_busySince < 0) _busySince = nowS;
            BusyForS = nowS - _busySince;
            _samples.Add((nowS, cpu));
            // the window starts at the newest look that is at least WindowS old: a full window whatever the looks'
            // spacing (live L9: a look every ~6.05 s kept 9 gaps, 54.5 s, and a "55 s at least" never came)
            while (_samples.Count > 2 && nowS - _samples[1].T >= WindowS) _samples.RemoveAt(0);
            var first = _samples[0];
            double span = nowS - first.T;
            if (span < WindowS) return false;   // not a full window yet
            CpuInWindowS = (cpu - first.Cpu) * WindowS / span;
            return BusyForS >= BusyMinS && CpuInWindowS < CpuMaxS;
        }
    }

    public const string FrozenLoadText = "Your game froze while loading your host's world (the game's own video player stopped at the loading screen; your own saves are untouched). Close the game - if it won't close, end it in Task Manager - then start it again and join again.";

    /// <summary>Phase 4.2: a placed world file may go now -- no load was asked, or the engine is past it, or it answers from a menu or a world.</summary>
    public static bool PlacedFileMayGo(bool loadCommanded, bool gameplayStarted, bool loadFailed, string where) =>
        !loadCommanded || gameplayStarted || loadFailed || where is "menu" or "world";

    /// <summary>Phase 3.4: a joiner's mp_unstuck asks the host to end every fight against his avatar (crime-ask kind 4).</summary>
    public static string EndFightsText(string why) => Wo139Text.IsWord(why) ? why : "unstuck";

    public static bool TryParseEndFights(string? text, out string why)
    {
        why = (text ?? "").Trim();
        return Wo139Text.IsWord(why);
    }

    // ------------------------------------------------------------------ Phase 6.3: a joiner's own wait or sleep

    /// <summary>
    /// Phase 6.3: shown on a joiner's screen when his own wait or sleep (not a shared one) was set back to the host's
    /// clock. The field's "53 dropped skips" were the joiner's once-a-minute clock announces, not skips; the one way a
    /// joiner's own skip is refused without a word is this set-back (a held picker that the host refuses already says
    /// <see cref="Wo140Rules.NotReadyText"/>).
    /// </summary>
    public const string SkipUndoneText = "Time was set back to your host's clock: in a shared world only the host's clock moves time. Ask your host to wait or sleep, and it passes for you both.";
    public const long SkipTellWindowMs = 60_000, SkipTellQuietMs = 60_000;

    /// <summary>
    /// A set-back is told when it follows this player's own skip (ended at most <see cref="SkipTellWindowMs"/> ago, not
    /// a shared one) and nothing was told in the last <see cref="SkipTellQuietMs"/>. A set-back after a cutscene's own
    /// time jump or the host's reload is not this player's doing: nothing is said (the field: a 2 h set-back at the end
    /// of the lake cutscene).
    /// </summary>
    public static bool TellSkipUndone(bool on, bool sharedSkipNow, long nowMs, long localEndedMs, bool localWasShared, long lastToldMs) =>
        on && !sharedSkipNow && localEndedMs > 0 && !localWasShared && nowMs - localEndedMs <= SkipTellWindowMs
        && (lastToldMs <= 0 || nowMs - lastToldMs >= SkipTellQuietMs);

    /// <summary>A joiner's clock announce (the connect/new-peer sync, re-sent once a minute; the relay passes it on as a quiet done): not a time skip.</summary>
    public static bool IsClockAnnounce(byte phase, byte kind) =>
        kind == Protocol.TimeSkipKindUnknown && (phase == Protocol.TimeSkipPhaseSync || phase == Protocol.TimeSkipPhaseDoneQuiet);
}
