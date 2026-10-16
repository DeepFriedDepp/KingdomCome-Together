// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>
/// WO-192 -- saves, simplified (docs/WO-192-findings.md). The pure rules; GameBridge.Wo192.cs is the bridge. Wo192Tests pins them.
///   1  the joiner's character snapshot is asked at every host save; the only reasons not to ask now are (a) a save pending on this machine
///      and (b) not in the host's world (Wo184Rules.SaveBlock). A scene positioning the host's copies, or the readiness service's "not
///      ready", is a log line only (SceneNote). The ESC menu's early snapshot, the joiner's own 10-minute snapshot and the host's reminder
///      are gone; the host's autosave (2) replaces them.
///   2  the host's autosave: every 30 minutes of play in a session, a save the Saviour Schnapps way (WO-178's path, no item used); the
///      game's own rules decide when (a fight, a dialogue, a scene, a menu, the engine's CanSave or a pending save: tried again every 60 s);
///      any host save (the autosave, Home, the player's own) starts the next 30 minutes.
///   4b the joiner's wake waits for the host's "woke", at most 15 s.
/// </summary>
public static class Wo192Rules
{
    // ================================================================ 1: the joiner's snapshot

    /// <summary>
    /// The joiner's ask: "" = ask now; else the one hard block. (a) a save pending on this machine, (b) not in the host's world. Every other
    /// state (a scene, a conversation, the readiness service, a menu) is NOT a reason -- the engine's own "not now" (its CanSave) still answers.
    /// </summary>
    public static string AskBlock(bool savePending, bool inWorld, bool joining, bool rewinding) =>
        Wo184Rules.SaveBlock(new Wo184Rules.SaveGate(InWorld: inWorld, Joining: joining, Rewinding: rewinding, SavePending: savePending));

    /// <summary>
    /// The log note for a snapshot asked while a scene runs ("" = none): the request may cancel the copies' fast-forward (accepted: WO-189 C
    /// releases a cutscene left at "initialized", backups cover the rest). Never a block.
    /// </summary>
    public static string SceneNote(bool positioningWaits, int openScenes, bool? ready, string notReady, string stuckCutscene)
    {
        var parts = new List<string>();
        if (positioningWaits) parts.Add("the positioning manager waits for its fast-forward set");
        if (openScenes > 0) parts.Add(FormattableString.Invariant($"{openScenes} interactive scene(s) open"));
        if (ready == false) parts.Add($"the save readiness says not ready ({(notReady.Length > 0 ? notReady : "?")})");
        if (stuckCutscene.Length > 0) parts.Add($"the cutscene '{stuckCutscene}' sits at its start");
        return parts.Count == 0 ? "" : string.Join("; ", parts);
    }

    public static string SceneLine(string purpose, uint seq, string note) =>
        FormattableString.Invariant($"WO192-SAVE during a scene: the {purpose} for host save seq={seq} is asked all the same -- {note} (it may cancel the copies' fast-forward; WO-189 C releases a stuck start)");

    // ================================================================ 2: the host's autosave

    public const bool DefaultHostAutosave = true;
    public const double DefaultAutosaveMinutes = 30;
    /// <summary>The game cannot save now: asked again this often (the mod never pushes).</summary>
    public const long RetryMs = 60_000;
    /// <summary>The save word the schnapps path carries (Wo178Rules.IsSaveWhy accepts it).</summary>
    public const string AutosaveWhy = "autosave";

    /// <summary>"30" / "1.5" / "on" / "off" -> minutes (0 = off); null = not a value.</summary>
    public static double? ParseMinutes(string? v)
    {
        if (v is null) return null;
        if (v == "on") return DefaultAutosaveMinutes;
        if (v == "off") return 0;
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double m) && m >= 0 && m <= 600 ? m : null;
    }

    /// <summary>
    /// The host's autosave clock: play time in a session (in the world, not at the menu or loading) since the last host save. Due when it
    /// reaches the interval and no retry is waiting; a wait (the game can't save now) asks again 60 s later; any host save starts again.
    /// Not thread-safe: the 1 s tick owns it.
    /// </summary>
    public sealed class Autosave
    {
        public long PlayMs { get; private set; }
        public long NextTryMs { get; private set; }
        public bool InSession { get; private set; }
        public bool Asked { get; private set; }
        public long AskedAtMs { get; private set; }
        public int Done { get; private set; }
        public int Waits { get; private set; }
        public string LastWait { get; private set; } = "";
        private long _lastTickMs = -1;

        /// <summary>One tick: the session's state and whether the player is in the world now.</summary>
        public void Tick(long nowMs, bool inSession, bool inWorld)
        {
            if (!inSession) { if (InSession) Reset(); InSession = false; _lastTickMs = nowMs; return; }
            if (!InSession) { InSession = true; PlayMs = 0; NextTryMs = 0; Asked = false; }
            if (_lastTickMs >= 0 && inWorld) PlayMs += Math.Clamp(nowMs - _lastTickMs, 0, 5_000);   // a stall never counts as minutes of play
            _lastTickMs = nowMs;
        }

        public bool Due(long nowMs, long intervalMs) => InSession && intervalMs > 0 && !Asked && PlayMs >= intervalMs && nowMs >= NextTryMs;

        /// <summary>The game can't save now: the next try in 60 s.</summary>
        public void Wait(long nowMs, string why) { Waits++; LastWait = why; NextTryMs = nowMs + RetryMs; Asked = false; }

        /// <summary>The save was asked (its result follows: Saved, or Wait).</summary>
        public void Ask(long nowMs) { Asked = true; AskedAtMs = nowMs; }

        /// <summary>Any host save (the autosave's own, Home, the player's): the next interval starts now.</summary>
        public void Saved(bool wasAutosave) { if (wasAutosave) Done++; PlayMs = 0; NextTryMs = 0; Asked = false; LastWait = ""; }

        public void Reset() { PlayMs = 0; NextTryMs = 0; Asked = false; LastWait = ""; }

        /// <summary>Minutes of play left (0 = due).</summary>
        public long LeftMs(long intervalMs) => Math.Max(0, intervalMs - PlayMs);
    }

    public static string DoneLine(string file, long playMs) =>
        FormattableString.Invariant($"WO192-AUTOSAVE done file={file} after {playMs / 60_000.0:F1} min of play -- the next one in the full interval");

    public static string WaitLine(string why, long playMs) =>
        FormattableString.Invariant($"WO192-AUTOSAVE waiting why={why} ({playMs / 60_000.0:F1} min of play since the last save) -- asked again in {RetryMs / 1000} s, never pushed");

    /// <summary>The Lua's answer to the ask ("asked" | "wait:&lt;why&gt;") -> (asked, why).</summary>
    public static (bool Asked, string Why) ParseAskAnswer(string? a)
    {
        if (a == "asked") return (true, "");
        if (a is not null && a.StartsWith("wait:", StringComparison.Ordinal) && a.Length > 5) return (false, a[5..]);
        return (false, string.IsNullOrEmpty(a) ? "no answer from the mod" : a == "timeout" ? "the mod did not answer" : a);
    }

    // ================================================================ 4b: the wake waits for the host

    /// <summary>The joiner's wake is held until the host's "woke", at most this long.</summary>
    public const long WakeHoldMaxMs = 15_000;

    /// <summary>
    /// The joiner's wake: hold (true) while the host's "woke" has not arrived and the bound has not passed since the joiner's own wake moment.
    /// </summary>
    public static bool HoldWake(bool hostWoke, long ownWakeAtMs, long nowMs) => !hostWoke && nowMs - ownWakeAtMs < WakeHoldMaxMs;

    /// <summary>After the host's "woke", the hold lasts this much longer: the clock pin lands behind the fade.</summary>
    public const long WakeSettleMs = 1_000;

    /// <summary>
    /// The hold the rule gives one shared skip (the replay): the joiner's own wake and the host's, both on the host's clock (ms). 0 = no
    /// hold (the host woke first); else the time held, bounded by <see cref="WakeHoldMaxMs"/>.
    /// </summary>
    public static long HoldFor(long joinerWokeMs, long hostWokeMs) =>
        hostWokeMs <= joinerWokeMs ? 0 : Math.Min(WakeHoldMaxMs, hostWokeMs - joinerWokeMs + WakeSettleMs);

    // ================================================================ 4c: a station's save lock is never left behind

    /// <summary>The game's own save locks of the stations the mod shows as a one-shot (the trough's wash, a water resource).</summary>
    public static readonly string[] StationLocks = ["waterTub"];
    /// <summary>A station lock held this long (a wash takes ~5.2 s) is the game's interaction that never ended: released.</summary>
    public const long StationBoundMs = 15_000;

    public const string AddLockPrefix = "Adding script save lock '", RemoveLockPrefix = "Removing script save lock '";

    /// <summary>"Adding script save lock 'x', uiDescription ..." -> (+1, x); "Removing script save lock 'x'" -> (-1, x); (0, "") otherwise.</summary>
    public static (int Op, string Name) ParseLockLine(string line)
    {
        int op = line.StartsWith(AddLockPrefix, StringComparison.Ordinal) ? 1 : line.StartsWith(RemoveLockPrefix, StringComparison.Ordinal) ? -1 : 0;
        if (op == 0) return (0, "");
        int a = (op > 0 ? AddLockPrefix : RemoveLockPrefix).Length;
        int b = line.IndexOf('\'', a);
        return b > a ? (op, line[a..b]) : (0, "");
    }

    /// <summary>The station locks held now and since when; due once per hold when it passes the bound. Thread-safe.</summary>
    public sealed class StationLockWatch
    {
        private readonly object _g = new();
        private readonly Dictionary<string, (long Since, bool Acted)> _held = new(StringComparer.Ordinal);

        public void Line(string line, long nowMs)
        {
            var (op, name) = ParseLockLine(line);
            if (op == 0 || Array.IndexOf(StationLocks, name) < 0) return;
            lock (_g)
            {
                if (op > 0) { if (!_held.ContainsKey(name)) _held[name] = (Math.Max(nowMs, 1), false); }
                else _held.Remove(name);
            }
        }

        /// <summary>A load wipes every script save lock.</summary>
        public void Clear() { lock (_g) _held.Clear(); }

        public bool Held(string name) { lock (_g) return _held.ContainsKey(name); }

        /// <summary>The locks held past the bound and not acted on yet (each once per hold), with how long.</summary>
        public List<(string Name, long HeldMs)> Due(long nowMs)
        {
            var due = new List<(string, long)>();
            lock (_g)
                foreach (var k in _held.Keys.ToList())
                {
                    var (since, acted) = _held[k];
                    if (acted || nowMs - since < StationBoundMs) continue;
                    _held[k] = (since, true);
                    due.Add((k, nowMs - since));
                }
            return due;
        }
    }
}
