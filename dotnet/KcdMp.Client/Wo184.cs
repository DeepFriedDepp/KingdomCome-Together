// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-184 (0.60.0, docs/WO-184-findings.md): the joiner's progress is saved, every time. The pure rules; GameBridge.Wo184.cs is the bridge.
///   B1 the snapshot is asked for at every host save. Two hard blocks only: a save pending on this machine (the engine's own state), and
///      not in the world (loading, a join or the host's reload running). The engine's own "not now" (its QuickSave refused: CanSave false)
///      is asked again every 2 s. No mod flag (conversation, scene, fader, crime, downed, menu) blocks a save any more.
///   B2 the conversation clock counts only a real conversation of the player's, and every way it can end ends it (a cap of 3 minutes).
///   B3 a request with no file within 60 s is stuck: a hard block, logged, told; the engine's own cancel (the one a load runs) recovers it.
///   B4 nothing is dropped silently: a final snapshot when the joiner leaves the host's world; no cap on the retries.
///   B5 the health check: every host save ends in a pair or in one logged reason within 60 s; two in a row unpaired = both players told.
/// Wo184Tests pins these.
/// </summary>
public static class Wo184Rules
{
    // ---------------------------------------------------------------- B1: the gate
    /// <summary>What the joiner's game is doing, for the one question "may the snapshot be asked for now?".</summary>
    public readonly record struct SaveGate(bool InWorld, bool Joining, bool Rewinding, bool SavePending);

    /// <summary>"" = ask now; else the hard block. Nothing else blocks: the engine itself waits (or says "not now") when it cannot save.</summary>
    public static string SaveBlock(SaveGate g)
    {
        if (g.SavePending) return "a save is pending on this machine";
        if (g.Joining) return "a join is running";
        if (g.Rewinding) return "the host is reloading";
        if (!g.InWorld) return "not in the world (loading)";
        return "";
    }

    /// <summary>The engine refused the QuickSave (its CanSave said no): asked again this often, while the snapshot is owed.</summary>
    public const long EngineRetryMs = 2_000;
    /// <summary>B3: a request with no "ready" and no file this long after it was taken is stuck.</summary>
    public const long StuckMs = 60_000;
    /// <summary>B5: every host save ends in a pair or in a logged reason within this.</summary>
    public const long HealthMs = 60_000;
    /// <summary>B4: a final snapshot when leaving waits at most this long for its pair.</summary>
    public const long FinalWaitMs = 15_000;
    /// <summary>A snapshot's file that lands after its wait ended (a stuck one, a leave's) still pairs within this window (never a "leak").</summary>
    public const long LateFileMs = 120_000;

    /// <summary>The engine's own save lines (kcd.log): a save waiting for its readiness, its readiness, the refusal of a second save.</summary>
    public const string ObserverPrefix = "Readiness observer 'BeforeGameSave'";
    public const string RefusedMarker = "because there is another savegame pending";
    /// <summary>The game's exit dialog (GUIModule): a quit follows when the player confirms ("CSystem::Quit invoked ... C_UISaveLoad::ExitGame()").</summary>
    public const string ExitDialogLine = "C_UISaveLoad::HandleExit()";

    // ---------------------------------------------------------------- B2: the conversation clock

    /// <summary>A dialogue line of the engine, read for the clock.</summary>
    public enum DialogKind { None, Chat, Request, Attempt, Start, End, Interrupted, Cancel }

    public readonly record struct DialogLine(DialogKind Kind, int Id, bool Dude, bool Monolog, bool Bark, string Npc, bool Forced = false);

    private static readonly Regex RxChat = new(@"^OnChatWithFocus: Dude->(?<npc>[A-Za-z0-9_.\-]+)", RegexOptions.CultureInvariant);
    private static readonly Regex RxRequest = new(@"^Soul 'Dude' requested dialog\. Assigned id is (?<id>\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex RxAttempt = new(@"^Attempting to start new dialogue \(runtime id '(?<id>\d+)'\) with souls '(?<souls>[^']*)'", RegexOptions.CultureInvariant);
    private static readonly Regex RxStart = new(@"^New dialogue '(?<name>.*?) \((?<kind>[^()]*?) - Id: (?<id>\d+)\)(?<rest>[^']*)' is starting\. Params: souls = '(?<souls>[^']*)'(?: forced = (?<forced>true|false))?", RegexOptions.CultureInvariant);
    private static readonly Regex RxEnd = new(@"^\[ID: (?<id>\d+)\] Dialog (?<how>ending|ends but no response was played|interrupted)", RegexOptions.CultureInvariant);
    private static readonly Regex RxInterrupting = new(@"^Interrupting dialog '.*? - Id: (?<id>\d+)\) \[(?<souls>[^\]]*)\]", RegexOptions.CultureInvariant);
    private static readonly Regex RxCancel = new(@"^Canceling dialog request id (?<id>\d+) from soul '(?<soul>[^']*)'", RegexOptions.CultureInvariant);

    /// <summary>"Dude" among the souls of a souls text ("Ex: Dude; Ex: x", "Ex0: Dude Ex1: x state: ...").</summary>
    public static bool HasDude(string souls) =>
        Regex.IsMatch(souls, @"(^|[\s;\[])(Ex|Nx)\d*: Dude(\b|$)", RegexOptions.CultureInvariant);

    /// <summary>One engine line as the clock reads it (None for anything else).</summary>
    public static DialogLine Parse(string? line)
    {
        if (string.IsNullOrEmpty(line)) return default;
        int Id(Match m) => int.TryParse(m.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : -1;
        Match m;
        if ((m = RxChat.Match(line)).Success) return new DialogLine(DialogKind.Chat, -1, true, false, false, m.Groups["npc"].Value);
        if ((m = RxRequest.Match(line)).Success) return new DialogLine(DialogKind.Request, Id(m), true, false, false, "");
        if ((m = RxAttempt.Match(line)).Success)
        {
            string souls = m.Groups["souls"].Value;
            return new DialogLine(DialogKind.Attempt, Id(m), HasDude(souls), false, Wo137Rules.IsBarkAttempt(souls) || souls.Contains("meta override: COMBAT_", StringComparison.Ordinal), "");
        }
        if ((m = RxStart.Match(line)).Success)
        {
            string souls = m.Groups["souls"].Value;
            bool monolog = m.Groups["kind"].Value.Contains("monolog", StringComparison.OrdinalIgnoreCase)
                           || m.Groups["rest"].Value.Contains("DoMonologue", StringComparison.Ordinal);
            bool bark = Wo137Rules.IsBarkAttempt(souls) || souls.Contains("meta override: COMBAT_", StringComparison.Ordinal);
            return new DialogLine(DialogKind.Start, Id(m), HasDude(souls), monolog, bark, "", m.Groups["forced"].Value == "true");
        }
        if ((m = RxEnd.Match(line)).Success)
        {
            int b = line.IndexOf('[', 2);
            string souls = b >= 0 ? line[b..] : "";
            return new DialogLine(m.Groups["how"].Value == "interrupted" ? DialogKind.Interrupted : DialogKind.End, Id(m), HasDude(souls), false, false, "");
        }
        if ((m = RxInterrupting.Match(line)).Success) return new DialogLine(DialogKind.Interrupted, Id(m), HasDude(m.Groups["souls"].Value), false, false, "");
        if ((m = RxCancel.Match(line)).Success) return new DialogLine(DialogKind.Cancel, Id(m), m.Groups["soul"].Value == "Dude", false, false, "");
        return default;
    }

    /// <summary>The literal starts of the lines WO-184 reads (the log tail must forward each; Wo184Tests pins it).</summary>
    public static readonly string[] ClockLinePrefixes =
    [
        "OnChatWithFocus: Dude->", "Soul 'Dude' requested dialog. Assigned id is ", "Attempting to start new dialogue (runtime id '",
        "New dialogue '", "Interrupting dialog '", "Canceling dialog request id ", ObserverPrefix, ExitDialogLine,
    ];

    /// <summary>The clock's edge: the conversation started, or ended (with the reason), or nothing changed.</summary>
    public readonly record struct Edge(bool Started, bool Ended, string Why)
    {
        public static readonly Edge None = new(false, false, "");
        public bool Any => Started || Ended;
    }

    /// <summary>
    /// B2: is the player in a real conversation? It starts ONLY on a talk the player asked for (the engine's "OnChatWithFocus: Dude-&gt;x",
    /// the mod's talk ask / start) or a dialogue that really starts with Henry in it ("New dialogue ... is starting" with Dude, not a
    /// monologue, not a bark). NEVER on a combat shout, a bark, a monologue or a bare "Attempting to start new dialogue". It ends on: the
    /// dialogue's end (ending / no response / interrupted), "Canceling dialog request id N" (a request that timed out), "Interrupting
    /// dialog", the mod's talk end, an attempt (or the ask) followed by no "New dialogue ... is starting" within 5 s, and in any case after
    /// 3 minutes. Thread-safe; every call returns the edge it caused.
    /// </summary>
    public sealed class ConversationClock
    {
        public const long NoStartMs = 5_000;
        public const long MaxMs = 180_000;

        private readonly object _gate = new();
        private bool _open, _confirmed;
        private long _startMs, _waitFromMs;
        private int _id = -1;
        private string _npc = "";
        private string _why = "";
        private readonly Queue<int> _closedIds = new();
        private readonly HashSet<int> _closedSet = new();
        private readonly Queue<int> _otherIds = new();      // a bark's or a monologue's dialogue id: its end never ends a talk
        private readonly HashSet<int> _otherSet = new();

        public bool Running { get { lock (_gate) return _open; } }
        public long StartMs { get { lock (_gate) return _open ? _startMs : 0; } }
        public string Describe(long nowMs)
        {
            lock (_gate)
                return _open ? FormattableString.Invariant($"running {(nowMs - _startMs) / 1000.0:F0} s (id {_id}, {(_npc.Length > 0 ? _npc : "?")}, {_why}{(_confirmed ? ", started" : ", not started yet")})") : "none";
        }

        private void Remember(int id)
        {
            if (id < 0 || !_closedSet.Add(id)) return;
            _closedIds.Enqueue(id);
            while (_closedIds.Count > 64) _closedSet.Remove(_closedIds.Dequeue());
        }

        private void Other(int id)
        {
            if (id < 0 || !_otherSet.Add(id)) return;
            _otherIds.Enqueue(id);
            while (_otherIds.Count > 64) _otherSet.Remove(_otherIds.Dequeue());
        }

        private Edge Open(long now, int id, string npc, string why, bool confirmed)
        {
            _open = true; _confirmed = confirmed; _startMs = now; _waitFromMs = now; _id = id; _npc = npc; _why = why;
            return new Edge(true, false, why);
        }

        private Edge Close(string why)
        {
            if (!_open) return Edge.None;
            Remember(_id);
            _open = false; _confirmed = false; _id = -1; _npc = ""; _why = "";
            return new Edge(false, true, why);
        }

        /// <summary>The player asked to talk (the engine's OnChatWithFocus, the mod's talk ask).</summary>
        public Edge Ask(string npc, long now)
        {
            lock (_gate)
            {
                if (_open && (_npc.Length == 0 || _npc == npc)) { if (_npc.Length == 0) _npc = npc; return Edge.None; }
                if (_open) { Close("a new talk"); return Open(now, -1, npc, "talk asked", false) with { Ended = true }; }
                return Open(now, -1, npc, "talk asked", false);
            }
        }

        /// <summary>The mod's talk start (WO-137 / WO-164: the conversation runs on this copy).</summary>
        public Edge TalkStart(string npc, int id, long now)
        {
            lock (_gate)
            {
                if (id >= 0 && _closedSet.Contains(id)) return Edge.None;   // the engine already cancelled or ended that request
                if (_open) { if (_id < 0) _id = id; if (_npc.Length == 0) _npc = npc; return Edge.None; }
                return Open(now, id, npc, "talk start", false);
            }
        }

        /// <summary>The mod's talk end.</summary>
        public Edge TalkEnd(string npc)
        {
            lock (_gate)
                return _open && (_npc.Length == 0 || _npc == npc) ? Close("the talk ended") : Edge.None;
        }

        /// <summary>One engine dialogue line (Parse's result).</summary>
        public Edge Feed(DialogLine d, long now)
        {
            if (d.Kind == DialogKind.Chat) return Ask(d.Npc, now);
            lock (_gate)
            {
                switch (d.Kind)
                {
                    case DialogKind.Request:
                        if (_open && _id < 0) _id = d.Id;
                        return Edge.None;
                    case DialogKind.Attempt:
                        // never a start: a bark's id is remembered (its end must not end a talk); a talk already open learns its id and waits
                        if (d.Bark) { Other(d.Id); return Edge.None; }
                        if (_open && d.Dude && !_confirmed) { if (_id < 0) _id = d.Id; _waitFromMs = now; }
                        return Edge.None;
                    case DialogKind.Start:
                        if (d.Monolog || d.Bark) { Other(d.Id); return Edge.None; }
                        if (!d.Dude) return Edge.None;
                        if (d.Id >= 0 && _closedSet.Contains(d.Id)) return Edge.None;
                        if (_open) { _id = d.Id; _confirmed = true; return Edge.None; }   // a talk's chat handing over to its haggle: the same conversation
                        // by itself (no talk asked) only a FORCED dialogue with Henry is a conversation of his (a quest's, an NPC's address)
                        return d.Forced ? Open(now, d.Id, "", "a forced dialogue with Henry started", true) : Edge.None;
                    case DialogKind.End:
                    case DialogKind.Interrupted:
                        if (d.Id >= 0 && _otherSet.Contains(d.Id)) return Edge.None;
                        if (!_open) { Remember(d.Id); return Edge.None; }
                        if (d.Id == _id || (_id < 0 && d.Dude)) return Close(d.Kind == DialogKind.End ? "the dialogue ended" : "the dialogue was interrupted");
                        return Edge.None;
                    case DialogKind.Cancel:
                        Remember(d.Id);
                        if (_open && d.Dude && (d.Id == _id || _id < 0)) return Close("the talk request was cancelled (timed out)");
                        return Edge.None;
                }
            }
            return Edge.None;
        }

        /// <summary>The time rules: no real start within 5 s of the ask or the last attempt, and the 3-minute cap.</summary>
        public Edge Tick(long now)
        {
            lock (_gate)
            {
                if (!_open) return Edge.None;
                if (now - _startMs >= MaxMs) return Close("3 minutes (the cap)");
                if (!_confirmed && now - _waitFromMs >= NoStartMs) return Close("no 'New dialogue ... is starting' within 5 s");
                return Edge.None;
            }
        }

        /// <summary>A load or the session's end: every conversation ends.</summary>
        public Edge Reset(string why) { lock (_gate) return Close(why); }
    }

    // ---------------------------------------------------------------- B3: stuck

    /// <summary>A taken request is stuck when, this long after it was taken, there is no file and the engine has not said it is ready.</summary>
    public static bool IsStuck(long requestedAtMs, bool fileSeen, bool readySeen, long nowMs) =>
        requestedAtMs > 0 && !fileSeen && !readySeen && nowMs - requestedAtMs >= StuckMs;

    // ---------------------------------------------------------------- B5: the health of every host save

    /// <summary>
    /// Every host save that reaches the joiner while it is joined ends, within <see cref="HealthMs"/>, in a pair or in one logged reason.
    /// "Covered" = paired, or superseded by a newer host save that paired (the snapshot pairs with the newest host save it follows).
    /// Two host saves in a row that end unpaired = both players are told (once per run of failures).
    /// </summary>
    public sealed class SaveHealth
    {
        public sealed class Entry
        {
            public uint Seq;
            public string Md5 = "";
            public long ArrivedMs;
            public bool Paired;
            public uint PairedBySeq;
            public string Reason = "";
            public bool Reported;   // its 60 s line was written
            public bool Failed;
        }

        private readonly object _gate = new();
        private readonly List<Entry> _l = new();
        private int _failRun;
        private bool _toldRun;
        public long PairedCount, FailedCount;

        public Entry Arrived(uint seq, string md5, long now)
        {
            lock (_gate)
            {
                var e = new Entry { Seq = seq, Md5 = md5, ArrivedMs = now };
                _l.Add(e);
                if (_l.Count > 64) _l.RemoveAt(0);
                return e;
            }
        }

        /// <summary>
        /// The snapshot paired with host save <paramref name="seq"/> (its newest arrival): it, and every save that arrived before it and is
        /// still unresolved (superseded: the snapshot pairs with the newest host save it follows), are covered.
        /// </summary>
        public void Paired(uint seq)
        {
            lock (_gate)
            {
                int at = _l.FindLastIndex(e => e.Seq == seq);
                for (int i = 0; i <= at; i++)
                {
                    var e = _l[i];
                    if (i == at || (!e.Paired && !e.Failed && e.Reason.Length == 0)) { e.Paired = true; e.PairedBySeq = seq; }
                }
                PairedCount++;
                _failRun = 0;
                _toldRun = false;
            }
        }

        /// <summary>A terminal reason for host save <paramref name="seq"/> (no snapshot will be taken for it).</summary>
        public void Reason(uint seq, string why)
        {
            lock (_gate)
                foreach (var e in _l)
                    if (e.Seq == seq && !e.Paired && e.Reason.Length == 0) e.Reason = why;
        }

        /// <summary>
        /// 1 s: the saves that reached 60 s with neither a pair nor a reason get <paramref name="now"/>'s reason (the current state) and a
        /// log line (returned); a failed one counts toward the run. True in <paramref name="tellBoth"/> once a run reaches two.
        /// </summary>
        public List<Entry> Tick(long now, Func<string> currentState, out bool tellBoth)
        {
            tellBoth = false;
            var due = new List<Entry>();
            lock (_gate)
            {
                foreach (var e in _l)
                {
                    if (e.Reported || e.Paired || now - e.ArrivedMs < HealthMs) continue;
                    e.Reported = true;
                    if (e.Reason.Length == 0) e.Reason = currentState();
                    e.Failed = true;
                    FailedCount++;
                    _failRun++;
                    due.Add(e);
                }
                if (_failRun >= 2 && !_toldRun) { _toldRun = true; tellBoth = true; }
            }
            return due;
        }

        public Entry? LastPaired() { lock (_gate) return _l.LastOrDefault(e => e.Paired); }
        public Entry? Newest() { lock (_gate) return _l.LastOrDefault(); }
        public int FailRun { get { lock (_gate) return _failRun; } }
        public void Clear() { lock (_gate) { _l.Clear(); _failRun = 0; _toldRun = false; } }
    }

    // ---------------------------------------------------------------- B4: leaving

    /// <summary>
    /// B4: a final snapshot when the joiner leaves the host's world: taken when he is in it and nothing is pending (the late rule: it pairs
    /// with the newest host save it follows). <paramref name="always"/> (mp_save_on_leave, default on) takes it at every leave; off takes it
    /// only when one is owed. "" = take it; else why not (said to the player).
    /// </summary>
    public static string FinalSnapshotBlock(bool joined, bool inWorld, bool pending, bool owed, bool always, bool haveHostSave)
    {
        if (!joined) return "not in the host's world";
        if (!haveHostSave) return "no host save to pair it with";
        if (!always && !owed) return "-";   // nothing owed and the switch takes it only when owed: not a failure
        if (!inWorld) return "the world is not loaded";
        if (pending) return "a save is pending on this machine";
        return "";
    }

    /// <summary>
    /// B4, the quit: the final snapshot starts when the ESC menu opens (the DLL's 0xAD edge), not only at the exit dialog. Live: the exit
    /// dialog's line comes about 1 s before the quit (the field: 0.95 s), a snapshot needs ~0.25 s to be asked and ~1.5 s for its file;
    /// a quit 0.3 s after the dialog line ended the game before the file was written (nothing paired; nothing left behind). The menu
    /// opens seconds before the dialog. At most one per menu opening; none within MenuMinGapMs of the last pair (the player was in the
    /// menu or had just been saved: nothing new to keep). "" = start it; "-" = not wanted here; else why not (logged).
    /// </summary>
    public static string MenuSnapshotBlock(bool joiner, bool joined, bool saveOnLeave, bool inFlight, long sinceLastPairMs)
    {
        if (!joiner || !joined) return "-";
        if (!saveOnLeave) return "mp_save_on_leave is off";
        if (inFlight) return "a snapshot is on its way already";
        if (sinceLastPairMs >= 0 && sinceLastPairMs < MenuMinGapMs) return "paired moments ago";
        return "";
    }

    /// <summary>The ESC menu snapshot's purpose (the pair's "How"); the quit's notice reads it back.</summary>
    public const string MenuPurpose = "the ESC menu";

    /// <summary>The host's "Partner saved.": shown for a pair with a host save it has not shown yet -- a later snapshot of the same save
    /// (the joiner's menu or leave) is logged only.</summary>
    public static bool PartnerSavedShows(bool known, uint prevSeq, string prevMd5, uint seq, string md5) => !(known && prevSeq == seq && prevMd5 == md5);

    public const long MenuMinGapMs = 5_000;

    /// <summary>
    /// The menu's own snapshot is the final one when the player has been in the menu since it paired: the menu was closed for at most
    /// MenuClosedSlackMs in all since that pair (the exit dialog may close and reopen it; the quit may close it). Then the exit dialog
    /// starts no second save racing the quit, and the quit's notice may say "up to the moment you quit".
    /// </summary>
    public static bool MenuPairCoversQuit(bool lastPairIsMenu, long closedSincePairMs) => lastPairIsMenu && closedSincePairMs <= MenuClosedSlackMs;

    public const long MenuClosedSlackMs = 2_000;

    /// <summary>The time the ESC menu was closed since the last pair (its edges: the DLL's 0xAD). Thread-safe; the bridge feeds it.</summary>
    public sealed class MenuTime
    {
        private readonly object _g = new();
        private bool _open;
        private long _closedAt = -1, _accum, _session, _lastCloseAt = -1;
        public long Session { get { lock (_g) return _session; } }
        public bool Open { get { lock (_g) return _open; } }

        /// <summary>The player is in the menu now (or left it a moment ago: the quit itself may close it).</summary>
        public bool InMenu(long now) { lock (_g) return _open || (_session > 0 && _lastCloseAt >= 0 && now - _lastCloseAt <= MenuClosedSlackMs); }

        /// <summary>The ESC menu opened: a new menu session (its number).</summary>
        public long Opened(long now)
        {
            lock (_g)
            {
                if (_closedAt >= 0) _accum += Math.Max(0, now - _closedAt);
                _closedAt = -1;
                _open = true;
                return ++_session;
            }
        }

        public void Closed(long now) { lock (_g) { if (_open) { _open = false; _closedAt = now; _lastCloseAt = now; } } }

        /// <summary>A pair: the count starts again (the menu's state is kept).</summary>
        public void Paired(long now) { lock (_g) { _accum = 0; _closedAt = _open ? -1 : now; } }

        /// <summary>The time the menu was closed since the last pair (all of it when no menu edge was ever seen: long.MaxValue).</summary>
        public long ClosedSincePair(long now)
        {
            lock (_g)
            {
                if (_session == 0) return long.MaxValue;
                return _accum + (_closedAt >= 0 ? Math.Max(0, now - _closedAt) : 0);
            }
        }
    }

    // ---------------------------------------------------------------- the words

    public const string JoinerStuckText = "Your character can't be saved right now; restart your game before the host saves again.";
    public const string HostStuckText = "Your partner's character can't be saved right now; they should restart their game before your next save.";
    public const string JoinerTwoUnpairedText = "Your character wasn't saved with your host's last two saves. Use mp_save_status to see why.";
    public const string HostTwoUnpairedText = "Your partner's character wasn't saved with your last two saves.";
    public const string HostPartnerSavedText = "Partner saved.";

    /// <summary>The W164 kind 16 text (joiner -> host): "paired &lt;seq&gt; &lt;md5_8&gt;" | "unpaired &lt;n&gt;" | "stuck" | "recovered".</summary>
    public static string PairedText(uint seq, string md5) => FormattableString.Invariant($"paired {seq} {Md5Short(md5)}");
    public static string UnpairedText(int n) => FormattableString.Invariant($"unpaired {n}");
    public const string StuckWord = "stuck", RecoveredWord = "recovered";

    public static string Md5Short(string md5) => md5.Length >= 8 ? md5[..8] : md5;

    /// <summary>Parses kind 16's text; false for anything malformed (counted, ignored).</summary>
    public static bool TryParseHealth(string? text, out string word, out uint seq, out string md5, out int n)
    {
        word = ""; seq = 0; md5 = ""; n = 0;
        if (string.IsNullOrEmpty(text) || text.Length > 40) return false;
        var f = text.Split(' ');
        switch (f[0])
        {
            case "paired" when f.Length == 3 && uint.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out seq) && Regex.IsMatch(f[2], "^[0-9a-f]{8}$"):
                word = "paired"; md5 = f[2]; return true;
            case "unpaired" when f.Length == 2 && int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out n) && n is > 0 and < 1000:
                word = "unpaired"; return true;
            case StuckWord or RecoveredWord when f.Length == 1:
                word = f[0]; return true;
        }
        return false;
    }

    public static string Seconds(long ms) => (ms / 1000.0).ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>"2 min ago" / "40 s ago" for the status text.</summary>
    public static string Ago(long ms) => ms < 0 ? "-" : ms < 120_000 ? FormattableString.Invariant($"{ms / 1000} s ago") : FormattableString.Invariant($"{ms / 60_000} min ago");
}
