// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Reflection;

namespace KcdMp.Client;

/// <summary>
/// WO-189 -- 0.60.6: the pure rules (Wo189Tests pins them; Wo189ReplayTests runs the field's logs through them).
///   A  saves never stall silently: a wait that ends without its file cancels the engine's pending save (A1); a watchdog cancels any pending
///      save older than 60 s (A2); a snapshot is asked only when the engine can take it (A3: the readiness service's IsReady(BeforeGameSave),
///      no scene positioning on the host's copies, no stuck cutscene); the joiner's own 10-minute snapshot and the host's 15-minute reminder (A4).
///   B  the host's quest world layers (game profiles) mirrored to the joiner: quest profiles only, changes in play only.
///   C  a cutscene stuck at "initialized" for 10 s on the joiner is released the game's own way.
///   D  a scene's positioning still waiting 20 s after it began has its copies' FastForward stopped (the engine's own per-NPC call).
/// </summary>
public static class Wo189Rules
{
    // ================================================================ A

    /// <summary>The DLL's op 44 answer.</summary>
    public sealed record ReadyRead(bool Armed, bool Ready, bool PosArmed, byte PosState, bool PosActive, float PosSecs, string NotReady)
    {
        /// <summary>C_ScenePositioningManager's state 2: waiting for its FastForward set (WO-188 R5).</summary>
        public bool PositioningWaiting => PosArmed && PosActive && PosState == 2;
    }

    /// <summary>A2: the watchdog's period and the age at which a pending save is cancelled.</summary>
    public const long WatchdogPeriodMs = 5_000, WatchdogPendingMs = 60_000;
    /// <summary>F: a requested save still pending after this long gets its not-ready tasks named (once).</summary>
    public const long PendingNameMs = 5_000;
    /// <summary>A3: a snapshot the engine cannot take now is owed and asked again this often.</summary>
    public const long NotReadyRetryMs = 15_000;

    /// <summary>
    /// A2: one pending stretch of the engine's save (its own state, read every 5 s). Due once, when the stretch is older than
    /// <see cref="WatchdogPendingMs"/>; a new stretch (pending -> clear -> pending) is a new one.
    /// </summary>
    public sealed class PendingWatch
    {
        public long SinceMs { get; private set; }
        public bool Acted { get; private set; }
        public bool Named { get; private set; }

        /// <summary>The engine's state at <paramref name="nowMs"/>; <paramref name="knownSinceMs"/> = an earlier start the agent knows (its own request, the engine's line).</summary>
        public bool Feed(bool pending, long nowMs, long knownSinceMs = 0)
        {
            if (!pending) { SinceMs = 0; Acted = false; Named = false; return false; }
            if (SinceMs == 0) SinceMs = knownSinceMs > 0 && knownSinceMs <= nowMs ? knownSinceMs : Math.Max(nowMs, 1);
            if (Acted || nowMs - SinceMs < WatchdogPendingMs) return false;
            Acted = true;
            return true;
        }

        /// <summary>F: true once per stretch, when it has been pending <see cref="PendingNameMs"/>.</summary>
        public bool NameDue(long nowMs)
        {
            if (SinceMs == 0 || Named || nowMs - SinceMs < PendingNameMs) return false;
            Named = true;
            return true;
        }

        public void Reset() { SinceMs = 0; Acted = false; Named = false; }
    }

    /// <summary>
    /// A3: why a snapshot must not be asked now ("" = ask). <paramref name="ready"/> null = the readiness service could not be read (then the
    /// engine's own pending state decides, as in 0.60.5). A scene positioning on the host's copies: a request would make the engine cancel every
    /// NPC's FastForward under the save lock (the field: that is how the joiner got into the stuck cutscene).
    /// </summary>
    public static string AskBlock(bool? ready, string notReadyNames, bool positioningOnCopies, string stuckCutscene)
    {
        if (positioningOnCopies) return "a scene is still positioning the host's copies (a save request now would cancel every NPC's fast-forward)";
        if (stuckCutscene.Length > 0) return $"the cutscene '{stuckCutscene}' is stuck at its start (released first)";
        if (ready == false) return $"the engine's save readiness is not ready ({(notReadyNames.Length > 0 ? notReadyNames : "?")})";
        return "";
    }

    /// <summary>A1's log line (the replay looks for it).</summary>
    public static string CancelLine(string purpose, long waitedMs, string result) =>
        FormattableString.Invariant($"WO189-SAVE cancelled the pending save of {purpose} after {waitedMs / 1000.0:F1} s (no file) -- {result}");

    /// <summary>A4: the joiner's own snapshot interval and the host's reminder interval (defaults; the switches take minutes).</summary>
    public const long AutoSnapMs = 10 * 60_000, ReminderMs = 15 * 60_000;
    /// <summary>A4: the purpose of the joiner's own snapshot (its pair line; no "character saved" toast, which speaks of a host save).</summary>
    public const string AutoSnapPurpose = "the joiner's own snapshot";
    public const bool DefaultAutoSnap = true, DefaultReminder = true, DefaultProfileMirror = true, DefaultCutsceneRelease = true, DefaultPositioningBound = true;

    /// <summary>A4: is the joiner's own snapshot due? Not when a snapshot paired (or one was asked) within the interval.</summary>
    public static bool AutoSnapDue(long lastPairMs, long lastAskMs, long nowMs, long intervalMs) =>
        intervalMs > 0 && nowMs - Math.Max(lastPairMs, lastAskMs) >= intervalMs;

    /// <summary>
    /// A4: the host's reminder. Due once per interval after the partner's last save (or the session's start), only in a session with a
    /// partner, never while one is shown; a pair (the partner saved) restarts the clock.
    /// </summary>
    public sealed class Reminder
    {
        public long BaseMs { get; private set; } = -1;
        public long LastShownMs { get; private set; } = -1;
        public int Shown { get; private set; }

        public void SessionStarted(long nowMs) { if (BaseMs < 0) BaseMs = nowMs; }
        public void SessionEnded() { BaseMs = -1; LastShownMs = -1; }
        public void PartnerSaved(long nowMs) { BaseMs = nowMs; LastShownMs = -1; }

        public bool Due(long nowMs, bool inSession, long intervalMs)
        {
            if (!inSession || intervalMs <= 0 || BaseMs < 0) return false;
            long since = LastShownMs >= 0 ? LastShownMs : BaseMs;
            if (nowMs - since < intervalMs) return false;
            LastShownMs = nowMs;
            Shown++;
            return true;
        }

        public long NextMs(long intervalMs) => BaseMs < 0 ? -1 : (LastShownMs >= 0 ? LastShownMs : BaseMs) + intervalMs;
    }

    public static string ReminderText(long minutes) =>
        FormattableString.Invariant($"Your partner hasn't been saved in {minutes} minutes - press Home to save.");

    // ================================================================ B: the quest world layers

    public const string ActivatingPrefix = "[Info] C_GameProfileManager: Activating profile '";
    public const string DeactivatingPrefix = "[Info] C_GameProfileManager: Deactivating profile '";
    public const string BatchStartLine = "GameProfileManager: Starting batch update.";
    public const string BatchEndLine = "GameProfileManager: Finished batch update.";

    private static HashSet<string>? _questProfiles;

    /// <summary>The quest list shipped with the agent (quest-profiles.txt: every ProfileAsset name of the game's quest scripts).</summary>
    public static IReadOnlySet<string> QuestProfiles
    {
        get
        {
            if (_questProfiles is not null) return _questProfiles;
            var set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("KcdMp.Client.quest-profiles.txt");
                if (s is not null)
                {
                    using var r = new StreamReader(s);
                    while (r.ReadLine() is string l)
                    {
                        l = l.Trim();
                        if (l.Length > 0 && l[0] != '#') set.Add(l);
                    }
                }
            }
            catch (IOException) { }
            return _questProfiles = set;
        }
    }

    /// <summary>B1: a profile the host may mirror -- named by a quest, never weather_* or event_* (engine-driven, local).</summary>
    public static bool IsQuestProfile(string name, IReadOnlySet<string> quest) =>
        name.Length > 0 && !name.StartsWith("weather_", StringComparison.Ordinal) && !name.StartsWith("event_", StringComparison.Ordinal) && quest.Contains(name);

    /// <summary>"Activating profile 'x'." / "Deactivating profile 'x'." -> (+1 / -1, x); 0 = not such a line.</summary>
    public static (int Op, string Name) ParseProfileLine(string line)
    {
        int op = line.StartsWith(ActivatingPrefix, StringComparison.Ordinal) ? 1 : line.StartsWith(DeactivatingPrefix, StringComparison.Ordinal) ? -1 : 0;
        if (op == 0) return (0, "");
        int a = (op > 0 ? ActivatingPrefix : DeactivatingPrefix).Length;
        int b = line.IndexOf('\'', a);
        return b > a ? (op, line[a..b]) : (0, "");
    }

    /// <summary>
    /// B1 on the host: the profile lines of the host's own game, decided one by one. A change inside a load batch, or before the world is in
    /// play (a load up to "Gameplay started"), is the save's own state -- never forwarded; the join catch-up carries the active set instead.
    /// </summary>
    public sealed class ProfileCapture
    {
        public bool InBatch { get; private set; }
        public bool InPlay { get; private set; } = true;
        public void Loading() { InPlay = false; }
        public void GameplayStarted() { InPlay = true; InBatch = false; }

        /// <summary>One raw line: null when it is no profile line; else the decision (Forward = send it).</summary>
        public Decision? Feed(string line, IReadOnlySet<string> quest)
        {
            if (line.StartsWith(BatchStartLine, StringComparison.Ordinal)) { InBatch = true; return null; }
            if (line.StartsWith(BatchEndLine, StringComparison.Ordinal)) { InBatch = false; return null; }
            var (op, name) = ParseProfileLine(line);
            if (op == 0) return null;
            string why = name.StartsWith("weather_", StringComparison.Ordinal) ? "weather (engine-driven, local)"
                       : name.StartsWith("event_", StringComparison.Ordinal) ? "random event (engine-driven, local)"
                       : !quest.Contains(name) ? "not a quest profile"
                       : InBatch ? "inside a load batch (the save's own state)"
                       : !InPlay ? "while loading (the save's own state)" : "";
            return new Decision(op, name, why.Length == 0, why.Length == 0 ? "forwarded" : why);
        }
    }

    public sealed record Decision(int Op, string Name, bool Forward, string Why);

    /// <summary>B3: the join catch-up -- the host's active profiles a quest names (sorted, so the log reads the same each time).</summary>
    public static List<string> CatchUp(IEnumerable<string> active, IReadOnlySet<string> quest) =>
        active.Where(n => IsQuestProfile(n, quest)).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>The W164 kind 17 text: "+name" / "-name" / "=name" / "?" (W164TextMax).</summary>
    public static string ProfileText(char op, string name) => op == '?' ? "?" : op + name;

    public static bool TryParseProfileText(string? text, out char op, out string name)
    {
        op = '\0'; name = "";
        if (string.IsNullOrEmpty(text)) return false;
        if (text == "?") { op = '?'; return true; }
        if (text.Length < 2 || text[0] is not ('+' or '-' or '=')) return false;
        string n = text[1..];
        foreach (char c in n) if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')) return false;
        op = text[0]; name = n;
        return true;
    }

    // ================================================================ C: a cutscene stuck at "initialized"

    public const long CutsceneStuckMs = 10_000, ReleaseConfirmMs = 10_000;
    public const string InitializedMarker = "CutscenePlayer::OnCutsceneInitialized called for ";

    /// <summary>"... OnCutsceneInitialized called for Fader cutscene 'n' with holder 'h' from module 'm'" -> (type, name, holder).</summary>
    public static bool TryParseInitialized(string line, out string type, out string name, out string holder)
    {
        type = name = holder = "";
        int a = line.IndexOf(InitializedMarker, StringComparison.Ordinal);
        if (a < 0) return false;
        var rest = line[(a + InitializedMarker.Length)..];
        int c = rest.IndexOf(" cutscene '", StringComparison.Ordinal);
        if (c <= 0) return false;
        type = rest[..c];
        rest = rest[(c + " cutscene '".Length)..];
        int d = rest.IndexOf('\'');
        if (d <= 0) return false;
        name = rest[..d];
        int h = rest.IndexOf(" with holder '", d, StringComparison.Ordinal);
        if (h > 0)
        {
            int s = h + " with holder '".Length;
            int e = rest.IndexOf('\'', s);
            if (e > s) holder = rest[s..e];
        }
        return true;
    }

    /// <summary>
    /// C1: the player's active cutscene logged "initialized" and no PlayCutscene (nor its end, release or interrupt) in 10 s. One at a time:
    /// the active configuration is the newest initialized one. The live check adds the player's own "in progress" (false while stuck).
    /// </summary>
    public sealed class CutsceneWatch
    {
        public string Name { get; private set; } = "";
        public string Holder { get; private set; } = "";
        public string Type { get; private set; } = "";
        public long SinceMs { get; private set; }
        public bool Flagged { get; private set; }
        public long ReleasedMs { get; private set; }
        public int Flags { get; private set; }

        public void Initialized(string type, string name, string holder, long nowMs)
        {
            Type = type; Name = name; Holder = holder; SinceMs = Math.Max(nowMs, 1); Flagged = false; ReleasedMs = 0;
        }

        /// <summary>A stage line of the player (WO-151's feed): anything past "initialized" for the watched cutscene ends the watch.</summary>
        public void Stage(string stage, string name)
        {
            if (SinceMs == 0 || name != Name) return;
            if (stage is "PlayCutscene" or "OnCutsceneEnd" or "ReleaseScene" or "Interrupt" or "FinishCutscene" or "FinalizeCutscene") Clear();
        }

        public void Clear() { Name = Holder = Type = ""; SinceMs = 0; Flagged = false; ReleasedMs = 0; }

        /// <summary>Due once per stuck cutscene: 10 s after "initialized" with no further stage.</summary>
        public bool Due(long nowMs)
        {
            if (SinceMs == 0 || Flagged || nowMs - SinceMs < CutsceneStuckMs) return false;
            Flagged = true;
            Flags++;
            return true;
        }

        public void Released(long nowMs) => ReleasedMs = Math.Max(nowMs, 1);

        /// <summary>C2: the release was made and the normal sequence did not follow in 10 s: the last resort is due (once).</summary>
        public bool LastResortDue(long nowMs)
        {
            if (ReleasedMs == 0 || SinceMs == 0 || nowMs - ReleasedMs < ReleaseConfirmMs) return false;
            ReleasedMs = 0;
            return true;
        }

        public string StuckName => SinceMs > 0 && Flagged ? Name : "";
    }

    // ================================================================ D: scene positioning on the host's copies

    public const long PositioningBoundMs = 20_000;

    /// <summary>
    /// D: the interactive scenes the engine runs on this machine (EnqueueScene / EndScene lines, FIFO). Due once per scene when the oldest open
    /// scene has run 20 s, no conversation with this player runs, and no cutscene plays (live: the positioning manager says it waits for its
    /// FastForward set, when its object was found).
    /// </summary>
    public sealed class PositioningWatch
    {
        private readonly Queue<long> _open = new();
        private long _actedFor = -1;
        public int Acts { get; private set; }

        public int Open => _open.Count;
        public long OldestMs => _open.Count > 0 ? _open.Peek() : 0;

        public void Enqueued(long nowMs) { _open.Enqueue(Math.Max(nowMs, 1)); if (_open.Count > 32) _open.Dequeue(); }
        public void Ended() { if (_open.Count > 0) _open.Dequeue(); }
        public void Reset() { _open.Clear(); _actedFor = -1; }

        /// <summary><paramref name="positioningWaits"/>: the manager's own state (null = not read: the lines decide).</summary>
        public bool Due(long nowMs, bool conversationRuns, bool cutscenePlays, bool? positioningWaits)
        {
            if (_open.Count == 0 || conversationRuns || cutscenePlays || positioningWaits == false) return false;
            long oldest = _open.Peek();
            if (oldest == _actedFor || nowMs - oldest < PositioningBoundMs) return false;
            _actedFor = oldest;
            Acts++;
            return true;
        }
    }

    // ================================================================ F: the barrier's two waits

    /// <summary>F: the concept paths of a barrier module's two waits, for a cutscene holder named "&lt;quest&gt;_(un)streamBarrierCH" (data names).</summary>
    public static (string StopCrime, string Streaming)? BarrierPaths(string holder, string questPath)
    {
        if (!holder.EndsWith("BarrierCH", StringComparison.Ordinal) || questPath.Length == 0) return null;
        return (questPath + ".barrier.stopcrime_1.waitingForReactors", questPath + ".barrier.streamprofileshandling.streamHandling");
    }

    /// <summary>The quest path a cutscene's module names ("brambora::Barbora::utils::...") is not the instance; the joiner's own dialogue names it.</summary>
    public static string QuestOfDialogue(string dialogue)
    {
        int last = dialogue.LastIndexOf('.');
        return last > 0 ? dialogue[..last] : "";
    }

    /// <summary>The quest data's Streaming enum (streamprofileshandling): its values in their order.</summary>
    public static string StreamingName(int v) => v switch
    {
        0 => "None", 1 => "WaitingForStream", 2 => "Streamed", 3 => "WaitingToUnstream", 4 => "Unstreamed",
        _ => v.ToString(CultureInfo.InvariantCulture),
    };

    public static string Secs(long ms) => (ms / 1000.0).ToString("F1", CultureInfo.InvariantCulture);
}
