// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-206 E: epochs (WO-205's findings 5.2) -- a statement older than the current one never wins. The pure rules; the bridge
/// is GameBridge.Wo206.cs.
///   join epoch   the host bumps it at every join start and every abort. A joiner message carries its attempt's joinId, which the
///                host maps to the epoch it started under (the joiner picks the id, so the id alone cannot be ordered); a message of
///                an attempt that is not the live one at the current epoch is dropped. The joiner's own reported state (LeashState)
///                carries the join epoch it knew in the slot WO-114 left 0. This is what makes WO-202 III.3 (a joiner shown
///                "loading" for good after the host aborted) impossible: an abort bumps the epoch, so nothing of that attempt counts.
///   world epoch  the host bumps it at every load of its own. It travels in the stream itself (W203 kind 11, both ways): TCP keeps
///                the order host -> relay -> joiner, so every frame READ after the announcement was SENT after it. The reader
///                stamps each frame with the epoch known at that moment; the apply points (the frame processor, the WO-136 load
///                hold's replay, the quest queue's drain, the chest ledger's apply) drop a frame whose stamp is no longer current.
///                That catches exactly what TCP order cannot: frames read before the announcement but applied after it (a backlog
///                in the processor, the load hold, the quest queue). The joiner answers each announcement ("ack"), so the host
///                stamps a joiner's frames with the epoch that joiner had seen: one sent before it heard of the load never lands.
///   scene        the cutscene bus's samples already carry the sender's play seq; a P/J/X of an older play of the same scene, or
///                of a play the bus already ended (Z), is dropped.
/// mp_epoch_check off: the stale item is applied as before and counted applied_old (the twin run's proof that it got through).
/// </summary>
public static class Wo206Rules
{
    // ---- the switches (the mod's w206_cfg; mp_w206_status prints them) ----
    public static readonly string[] SwitchNames = ["epoch_check", "research_death_from_health"];

    /// <summary>epoch_check ships on; the research switch is off and only works with <see cref="ResearchEnv"/>=1 (never a shipped config).</summary>
    public static bool DefaultOn(string name) => name == "epoch_check";

    public const string ResearchEnv = "KCDMP_TEST_W206";

    /// <summary>The C3 twin's switch takes effect only on a test machine (the env var set to exactly "1").</summary>
    public static bool ResearchEffective(bool switchOn, string? envValue) => switchOn && envValue == "1";

    /// <summary>B2: the joiner's join pipeline stops when its attempt is no longer the live one (aborted while it ran). The twin's
    /// environment value ("1", a test machine only) lets it run on as before.</summary>
    public static bool StopAbortedJoin(bool stillLive, string? twinEnv) => !stillLive && twinEnv != "1";

    /// <summary>B2: a join request still waiting for the host's world when the relay link drops is over (the host ends it as
    /// joiner-gone); it must not block the next connection's automatic request. The twin's value "1" keeps it (the old behaviour).</summary>
    public static bool ForgetRequestOnRelayLoss(uint outstandingJoinId, string? twinEnv) => outstandingJoinId != 0 && twinEnv != "1";

    /// <summary>The bench (a joined conversation, release-gate smoke): one refused speech poll during the join's hold -- a pipe call
    /// that failed while the world was held -- silenced this machine's conversations for the whole session. A refused poll is tried
    /// again after this delay: 10 s more per refusal in a row, 60 s at most. The twin's "1" keeps the old behaviour (never again).</summary>
    public static long PollRetryDelayMs(int refusalsInRow, string? twinEnv) =>
        twinEnv == "1" ? long.MaxValue : Math.Min(60_000L, 10_000L * Math.Max(1, refusalsInRow));

    /// <summary>A refused poll is logged the first time and every tenth time in a row.</summary>
    public static bool LogPollRefusal(int refusalsInRow) => refusalsInRow == 1 || refusalsInRow % 10 == 0;

    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        if (string.IsNullOrEmpty(arg)) return false;
        int eq = arg.IndexOf('=');
        if (eq <= 0) return false;
        name = arg[..eq];
        string v = arg[(eq + 1)..];
        if (Array.IndexOf(SwitchNames, name) < 0 || v is not ("on" or "off")) return false;
        on = v == "on";
        return true;
    }

    // ---------------------------------------------------------------- the announcement (W203 kind 11)

    public const string Ack = "ack";
    public const int AnnounceEveryMs = 10_000;

    /// <summary>"v1|&lt;world&gt;|&lt;join&gt;|&lt;why&gt;": the host's epochs (why = what bumped, or "tick"); a joiner's answer has why "ack".</summary>
    public static string EpochText(uint world, uint join, string why) =>
        string.Create(CultureInfo.InvariantCulture, $"v1|{world}|{join}|{Why(why)}");

    private static string Why(string why)
    {
        var sb = new StringBuilder();
        foreach (char c in why) if (char.IsAsciiLetterOrDigit(c) || c == '-') sb.Append(c);
        string t = sb.ToString();
        return t.Length == 0 ? "-" : t.Length > 24 ? t[..24] : t;
    }

    public static bool TryParseEpoch(string text, out uint world, out uint join, out string why)
    {
        world = join = 0; why = "";
        var f = text.Split('|');
        if (f.Length < 4 || f[0] != "v1") return false;   // later fields may be appended
        if (!uint.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out world)
            || !uint.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out join)) return false;
        if (f[3].Length is 0 or > 24 || !f[3].All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
        why = f[3];
        return true;
    }

    /// <summary>The reader: a W203Down frame of kind 11 -> its sender and epochs. False for anything else.</summary>
    public static bool TryReadEpochFrame(int type, byte[] payload, out byte src, out uint world, out uint join, out string why)
    {
        src = 0; world = join = 0; why = "";
        if (type != Protocol.W203Down || !Protocol.TrySplitJoinDown(payload, out src, out _, out _, out var body)) return false;
        if (body.Length < 1 || body[0] != Protocol.W203Epoch || !LootMsg.TryDecode(body, out var m)) return false;
        return TryParseEpoch(m.Text, out world, out join, out why);
    }

    // ---------------------------------------------------------------- which frames the world epoch guards

    /// <summary>The category a frame's world write falls in, or null when the world epoch does not guard it. The payload is the down
    /// payload as read ([sourceGhostId] first); join-channel kinds are the LootMsg kind at payload[6].</summary>
    public static Wo206Cat? WorldCategory(int type, byte[] p)
    {
        if (type == Protocol.NpcStateDown) return Wo206Cat.Npc;
        if (type == Protocol.NpcDamageDown)
        {
            if (p.Length < 2 + 1 + Protocol.NpcDamageFixedTail || p.Length != 2 + p[1] + Protocol.NpcDamageFixedTail) return Wo206Cat.Npc;
            return (p[2 + p[1] + 8] & Protocol.NpcDamageFlagFatal) != 0 ? Wo206Cat.Death : Wo206Cat.Npc;
        }
        if (p.Length < 7) return null;
        byte kind = p[6];
        if (type == Protocol.QuestHostDown && kind is Protocol.QuestHostChange or Protocol.QuestHostCheckpoint or Protocol.QuestHostResult) return Wo206Cat.Quest;
        if (type == Protocol.QuestAskDown && kind == Protocol.QuestAskRequest) return Wo206Cat.Quest;
        if (type == Protocol.LootHostDown && kind == Protocol.LootHostLedger) return Wo206Cat.Loot;
        return null;
    }

    // ---------------------------------------------------------------- the joiner's reported state

    /// <summary>
    /// The join epoch a joiner's LeashState carries. Its "loading" bit comes from a join attempt in flight, from where its game is
    /// (a load, the menu), or from the host's reload (WO-125's rewinding). Only the last outlives an abort by itself (WO-202 III.3:
    /// it stayed set for good), so a state that is "loading" only because of it carries the epoch the reload began under: once
    /// the host bumped past it (a rejoin started, or aborted), that loading is not the current one and the host drops it.
    /// </summary>
    public static uint JoinerStateStamp(uint knownJoinEpoch, uint rewindEpoch, bool loadingOnlyFromRewind) =>
        loadingOnlyFromRewind ? rewindEpoch : knownJoinEpoch;

    /// <summary>A host status the joiner gets for an attempt (states 1..7; 8-10 are session-wide): one for an attempt that is not
    /// live here is stale -- except the end (Resumed) of an attempt this joiner did make.</summary>
    public static bool JoinerStatusStale(byte state, bool liveAttempt, bool everOurs) =>
        state is >= Protocol.JoinStateDeferred and <= Protocol.JoinStateRefused && !liveAttempt
        && !(state == Protocol.JoinStateResumed && everOurs);

    // ---------------------------------------------------------------- the C3 twin's research path

    /// <summary>WO-202 II.3 moved the death announcement from a health reading to the soul's own dead state. The research path (only
    /// with <see cref="ResearchEnv"/>=1) announces from the health reading again: the observer's hp at or below 0.</summary>
    public static bool DeathFromHealth(string? hp) =>
        float.TryParse(hp, NumberStyles.Float, CultureInfo.InvariantCulture, out float h) && float.IsFinite(h) && h <= 0f;

    /// <summary>Does an observed death go out? The research path: the health reading alone (the soul is not asked); otherwise WO-202
    /// II.3's rule (the soul reads dead, or an older DLL gave no answer).</summary>
    public static bool DeathAnnounced(bool researchEffective, byte? soulDead, string? hp) =>
        researchEffective ? DeathFromHealth(hp) : Wo202Rules.AnnounceDeath(soulDead);
}

public enum Wo206Cat { Npc, Quest, Loot, Death }

/// <summary>WO-206 E: one machine's epochs, the decisions and the counters. Thread-safe (the reader, the processor and the loops share it).</summary>
public sealed class Wo206Epochs
{
    /// <summary>mp_epoch_check.</summary>
    public volatile bool Check = true;

    // ---------------------------------------------------------------- world epoch

    private uint _world;   // the host: its own (from 1); a joiner: the host's as last announced (0 = none yet)
    private readonly ConcurrentDictionary<byte, uint> _peerAck = new();   // the host: what each joiner acknowledged

    public uint World => Volatile.Read(ref _world);

    /// <summary>The host's epochs start at 1 (0 means "none known" on the wire and in a stamp).</summary>
    public void EnsureHost()
    {
        Interlocked.CompareExchange(ref _world, 1, 0);
        Interlocked.CompareExchange(ref _join, 1, 0);
    }

    /// <summary>The host loaded a save: everything stamped before is the old world.</summary>
    public uint BumpWorld() { EnsureHost(); return Interlocked.Increment(ref _world); }

    /// <summary>A joiner's reader: the host's announcement. True when the world epoch changed.</summary>
    public bool LearnWorld(uint world, uint join)
    {
        Volatile.Write(ref _join, join);
        return Interlocked.Exchange(ref _world, world) != world;
    }

    /// <summary>The host's reader: a joiner acknowledged this world epoch.</summary>
    public void NoteAck(byte peer, uint world) => _peerAck[peer] = world;

    public void ForgetPeer(byte peer) => _peerAck.TryRemove(peer, out _);

    /// <summary>The stamp of a frame read now from <paramref name="src"/>: on the host, the epoch that joiner had seen when it sent it;
    /// anywhere else, the host's as known now. 0 = none (never stale).</summary>
    public uint StampFor(byte src, bool host) => host ? (_peerAck.TryGetValue(src, out uint a) ? a : 0) : World;

    public bool WorldStale(uint stamp) => stamp != 0 && stamp != World;

    private readonly long[] _droppedWorld = new long[4];
    private long _appliedOld, _droppedScene, _droppedJoin;
    private readonly ConcurrentDictionary<string, long> _droppedJoinKinds = new(StringComparer.Ordinal);

    public long DroppedWorld(Wo206Cat c) => Interlocked.Read(ref _droppedWorld[(int)c]);
    public long AppliedOld => Interlocked.Read(ref _appliedOld);
    public long DroppedScene => Interlocked.Read(ref _droppedScene);
    public long DroppedJoin => Interlocked.Read(ref _droppedJoin);
    public long DroppedJoinKind(string kind) => _droppedJoinKinds.TryGetValue(kind, out long n) ? n : 0;

    /// <summary>A world write stamped <paramref name="stamp"/>: true = apply it. Stale with the check on: dropped and counted; with it
    /// off: applied and counted applied_old.</summary>
    public bool AdmitWorld(Wo206Cat cat, uint stamp)
    {
        if (!WorldStale(stamp)) return true;
        if (!Check) { Interlocked.Increment(ref _appliedOld); return true; }
        Interlocked.Increment(ref _droppedWorld[(int)cat]);
        return false;
    }

    // ---------------------------------------------------------------- join epoch

    private uint _join;    // the host: its own (from 1); a joiner: the host's as last announced
    private readonly object _joinGate = new();
    private readonly Dictionary<uint, uint> _attempts = new();   // the host: joinId -> the epoch its attempt started under
    private readonly Queue<uint> _attemptOrder = new();
    private uint _live;    // the host's live attempt (0 = none)
    private const int AttemptsKept = 64;

    public uint Join => Volatile.Read(ref _join);

    /// <summary>The host starts a join: a new epoch, and this attempt is the live one.</summary>
    public uint StartJoin(uint joinId)
    {
        EnsureHost();
        lock (_joinGate)
        {
            uint e = Interlocked.Increment(ref _join);
            if (!_attempts.ContainsKey(joinId)) { _attemptOrder.Enqueue(joinId); while (_attemptOrder.Count > AttemptsKept) _attempts.Remove(_attemptOrder.Dequeue()); }
            _attempts[joinId] = e;
            _live = joinId;
            return e;
        }
    }

    /// <summary>The host's join ended: an abort (anything but the joiner's ready) is a new epoch; either way the attempt is closed.</summary>
    public uint EndJoin(uint joinId, bool aborted)
    {
        lock (_joinGate)
        {
            if (_live == joinId) _live = 0;
            return aborted ? Interlocked.Increment(ref _join) : Join;
        }
    }

    /// <summary>A joiner message of attempt <paramref name="joinId"/> on the host: stale when that attempt is known and is not the live
    /// one at the current epoch. An id this host never started is not judged here (the join's own match refuses it).</summary>
    public bool JoinAttemptStale(uint joinId)
    {
        lock (_joinGate)
            return _attempts.TryGetValue(joinId, out uint e) && !(joinId == _live && e == Join);
    }

    /// <summary>A joiner's reported state stamped <paramref name="stamp"/> (its LeashState): stale when it is not the current join epoch.</summary>
    public bool JoinerStateStale(uint stamp) => stamp != 0 && stamp != Join;

    /// <summary>A join message or a joiner's state of kind <paramref name="kind"/>: true = take it. Counted per kind when dropped.</summary>
    public bool AdmitJoin(string kind, bool stale)
    {
        if (!stale) return true;
        if (!Check) { Interlocked.Increment(ref _appliedOld); return true; }
        Interlocked.Increment(ref _droppedJoin);
        _droppedJoinKinds.AddOrUpdate(kind, 1, (_, n) => n + 1);
        return false;
    }

    // ---------------------------------------------------------------- the host's view of a joiner (what it shows)

    private readonly ConcurrentDictionary<byte, (uint Stamp, bool Loading)> _joinerView = new();

    /// <summary>The host: a joiner's state report. False = dropped (a stale stamp with the check on); the view keeps what it had.</summary>
    public bool NoteJoinerState(byte joiner, uint stamp, bool loading)
    {
        if (!AdmitJoin("state", JoinerStateStale(stamp))) return false;
        _joinerView[joiner] = (stamp, loading);
        return true;
    }

    /// <summary>The host shows this joiner loading: its last admitted report said so and, with the check on, belongs to the current
    /// join epoch (an abort since makes it history, whatever arrives late).</summary>
    public bool ShowsJoinerLoading(byte joiner) =>
        _joinerView.TryGetValue(joiner, out var v) && v.Loading && (!Check || !JoinerStateStale(v.Stamp));

    /// <summary>The joiners whose kept state belongs to an older join epoch (dropped from the host's view after a bump).</summary>
    public List<byte> StaleJoinerViews()
    {
        var o = new List<byte>();
        if (!Check) return o;
        foreach (var kv in _joinerView) if (JoinerStateStale(kv.Value.Stamp)) o.Add(kv.Key);
        foreach (byte b in o) _joinerView.TryRemove(b, out _);
        return o;
    }

    public void ForgetJoiner(byte joiner) => _joinerView.TryRemove(joiner, out _);

    // ---------------------------------------------------------------- scene instances

    public readonly Wo206Scenes Scenes = new();

    /// <summary>A cutscene bus message of an older play: true = take it.</summary>
    public bool AdmitScene(bool stale)
    {
        if (!stale) return true;
        if (!Check) { Interlocked.Increment(ref _appliedOld); return true; }
        Interlocked.Increment(ref _droppedScene);
        return false;
    }

    // ---------------------------------------------------------------- the stats line

    public string StatsLine(long applied) => string.Create(CultureInfo.InvariantCulture,
        $"MP-W206-EPOCH join={Join} world={World} scene={Scenes.Instances} dropped_join={DroppedJoin} dropped_world_npc={DroppedWorld(Wo206Cat.Npc)} " +
        $"dropped_world_quest={DroppedWorld(Wo206Cat.Quest)} dropped_world_loot={DroppedWorld(Wo206Cat.Loot)} dropped_world_death={DroppedWorld(Wo206Cat.Death)} " +
        $"dropped_scene={DroppedScene} applied_old={AppliedOld} epoch_check={(Check ? "on" : "off")} " +
        $"dropped_join_kinds={(_droppedJoinKinds.IsEmpty ? "-" : string.Join(',', _droppedJoinKinds.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}:{k.Value}")))} applied={applied}");
}

/// <summary>
/// WO-206 E: the scene instances one machine has seen per partner and scene. The bus's C (started) and G (runs) name the sender's play
/// by its seq (one counter per sending agent, so it grows play by play; a restarted agent starts again with a new C); Z ends it.
/// </summary>
public sealed class Wo206Scenes
{
    private readonly object _gate = new();
    private readonly Dictionary<(byte, string), (int Seq, bool Ended)> _cur = new();
    private long _instances;

    public long Instances => Interlocked.Read(ref _instances);

    /// <summary>C or G: this play is the scene's current one from this partner (a C always: a new play, even one with a lower seq).</summary>
    public void Begin(byte src, string name, int seq)
    {
        lock (_gate)
        {
            if (_cur.TryGetValue((src, name), out var c) && c.Seq == seq) return;
            _cur[(src, name)] = (seq, false);
            _instances++;
        }
    }

    /// <summary>Z: that play is over (nothing of it may land any more).</summary>
    public void End(byte src, string name, int seq)
    {
        lock (_gate)
            if (_cur.TryGetValue((src, name), out var c) && c.Seq == seq) _cur[(src, name)] = (seq, true);
    }

    /// <summary>P, J or X: from a play older than the current one, or from the current one after its end. A newer seq (its C not seen,
    /// e.g. this agent started mid-scene) becomes the current play.</summary>
    public bool Stale(byte src, string name, int seq)
    {
        lock (_gate)
        {
            if (!_cur.TryGetValue((src, name), out var c) || seq > c.Seq) { _cur[(src, name)] = (seq, false); _instances++; return false; }
            return seq < c.Seq || c.Ended;
        }
    }

    public void Clear() { lock (_gate) _cur.Clear(); }
}

/// <summary>
/// WO-206 Part C: the APPLIED log. Every mirrored write this machine applies (the quest-State mirror, a death announcement, the chest
/// ledger) writes one console line and one JSON line to applied.jsonl beside the agent's exe (the evidence bundle collects it; one
/// session per file: truncated at agent start, nothing else is touched). The digest recorder reads <see cref="WorldEpoch"/>,
/// <see cref="JoinEpoch"/> and sets <see cref="CheckpointSeq"/>.
/// </summary>
public static class Wo206Applied
{
    public const string FileName = "applied.jsonl";

    private static int _seq;
    private static long _world, _join, _written;

    /// <summary>The checkpoint sequence the digest recorder is at (it sets this; every APPLIED record carries it).</summary>
    public static int CheckpointSeq { get => Volatile.Read(ref _seq); set => Volatile.Write(ref _seq, value); }

    /// <summary>This machine's world epoch (the host: its own; a joiner: the host's as last announced; 0 = none yet).</summary>
    public static uint WorldEpoch => (uint)Interlocked.Read(ref _world);

    /// <summary>This machine's join epoch (as <see cref="WorldEpoch"/>).</summary>
    public static uint JoinEpoch => (uint)Interlocked.Read(ref _join);

    public static long Written => Interlocked.Read(ref _written);

    /// <summary>The bridge keeps these current (every bump, every announcement); nothing else sets them.</summary>
    public static void SetEpochs(uint world, uint join)
    {
        Interlocked.Exchange(ref _world, world);
        Interlocked.Exchange(ref _join, join);
    }

    private static readonly object Gate = new();
    private static StreamWriter? _file;
    private static string? _path;

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, FileName);
    public static string? CurrentPath { get { lock (Gate) return _path; } }

    /// <summary>The agent's start: applied.jsonl is truncated (one session per file). A file that cannot be opened leaves the console line only.</summary>
    public static void StartSession(string? path = null)
    {
        lock (Gate)
        {
            _file?.Dispose();
            _file = null;
            _path = path ?? DefaultPath;
            try { _file = new StreamWriter(new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true }; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine($"MP-APPLIED {FileName} not opened ({ex.Message}) -- the console lines only"); }
        }
    }

    public static void EndSession() { lock (Gate) { _file?.Dispose(); _file = null; } }

    /// <summary>One applied write: the console line and the JSON line. <paramref name="epoch"/> is the world epoch the item carried
    /// (null: the current one).</summary>
    public static void Write(bool host, string cat, string key, string cause, string? before, string? after, uint? epoch = null)
    {
        uint e = epoch is uint x && x != 0 ? x : WorldEpoch;
        int seq = CheckpointSeq;
        Console.WriteLine(Line(cat, key, cause, before, after, e, seq));
        string json = Json(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), host, cat, key, cause, before, after, e, seq);
        lock (Gate)
        {
            try { _file?.WriteLine(json); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        }
        Interlocked.Increment(ref _written);
    }

    /// <summary>"MP-APPLIED cat=.. key=.. cause=.. before=.. after=.. epoch=.. seq=.." (a missing value is "-"; spaces become '_').</summary>
    public static string Line(string cat, string key, string cause, string? before, string? after, uint epoch, int seq) =>
        string.Create(CultureInfo.InvariantCulture,
            $"MP-APPLIED cat={Tok(cat)} key={Tok(key)} cause={Tok(cause)} before={Tok(before)} after={Tok(after)} epoch={epoch} seq={seq}");

    private static string Tok(string? s) => string.IsNullOrEmpty(s) ? "-" : string.Join('_', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static readonly JsonWriterOptions JsonOpts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>{"t":unix ms,"m":"host"|"joiner","cat":..,"key":..,"cause":..,"before":str|null,"after":str|null,"epoch":int,"seq":int}</summary>
    public static string Json(long unixMs, bool host, string cat, string key, string cause, string? before, string? after, uint epoch, int seq)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, JsonOpts))
        {
            w.WriteStartObject();
            w.WriteNumber("t", unixMs);
            w.WriteString("m", host ? "host" : "joiner");
            w.WriteString("cat", cat);
            w.WriteString("key", key);
            w.WriteString("cause", cause);
            if (before is null) w.WriteNull("before"); else w.WriteString("before", before);
            if (after is null) w.WriteNull("after"); else w.WriteString("after", after);
            w.WriteNumber("epoch", epoch);
            w.WriteNumber("seq", seq);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>A quest State value as the log writes it.</summary>
    public static string Sv(int v) => "sv:" + v.ToString(CultureInfo.InvariantCulture);
}
