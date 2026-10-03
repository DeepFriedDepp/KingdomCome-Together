// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-137: one quest State change from the DLL's detector (pipe 0x9E, native wo137.h).
/// <code>
///   [seq:4][flags:1][old:4][new:4][portLen:1][port][typeLen:1][type][pathLen:2][path][questLen:2]
/// </code>
/// flags: 1 notify, 2 mirror (caused while an Apply ran here), 4 old ok, 8 new ok,
/// 16 cascade (made by another State's consumers), 64 worker (WO-154: made on an AI
/// worker thread, not the frame thread). questLen: the quest root's prefix of Path
/// (only States under a C_Quest are recorded). 32 is a request's own flag (Wo147Rules.FlagConversation).
/// </summary>
public readonly record struct QuestChange(uint Seq, byte Flags, int Old, int New, string Port, string Type, string Path, int QuestLen)
{
    public const byte FNotify = 1, FMirror = 2, FOldOk = 4, FNewOk = 8, FCascade = 16, FWorker = 64;
    public bool Notify => (Flags & FNotify) != 0;
    public bool Mirror => (Flags & FMirror) != 0;
    public bool Cascade => (Flags & FCascade) != 0;
    /// <summary>WO-154: made on a worker thread (an AI behaviour's change).</summary>
    public bool Worker => (Flags & FWorker) != 0;
    public bool NewOk => (Flags & FNewOk) != 0;
    public bool OldOk => (Flags & FOldOk) != 0;
    /// <summary>"Barbora.trosecko.hledaniPsa" for "Barbora.trosecko.hledaniPsa.h.findVorech".</summary>
    public string Quest => QuestLen > 0 && QuestLen <= Path.Length ? Path[..QuestLen] : Path;

    public static bool TryParse(ReadOnlySpan<byte> b, out QuestChange c)
    {
        c = default;
        if (b.Length < 4 + 1 + 4 + 4 + 1 + 1 + 2 + 2) return false;
        int o = 0;
        uint seq = BinaryPrimitives.ReadUInt32LittleEndian(b); o += 4;
        byte flags = b[o++];
        int old = BinaryPrimitives.ReadInt32LittleEndian(b[o..]); o += 4;
        int nw = BinaryPrimitives.ReadInt32LittleEndian(b[o..]); o += 4;
        int pl = b[o++];
        if (o + pl + 1 > b.Length) return false;
        string port = Encoding.ASCII.GetString(b.Slice(o, pl)); o += pl;
        int tl = b[o++];
        if (o + tl + 2 > b.Length) return false;
        string type = Encoding.ASCII.GetString(b.Slice(o, tl)); o += tl;
        int ph = BinaryPrimitives.ReadUInt16LittleEndian(b[o..]); o += 2;
        if (ph == 0 || o + ph + 2 != b.Length) return false;
        string path = Encoding.ASCII.GetString(b.Slice(o, ph)); o += ph;
        int ql = BinaryPrimitives.ReadUInt16LittleEndian(b[o..]);
        c = new QuestChange(seq, flags, old, nw, port, type, path, ql);
        return true;
    }
}

/// <summary>WO-137: the pure rules of shared quests (docs/WO-137-findings.md).</summary>
public static class Wo137Rules
{
    /// <summary>State types that belong to each machine (its own streaming, its own camera): never synced either way.</summary>
    public static readonly HashSet<string> PerMachineTypes = new(StringComparer.Ordinal)
    {
        "Streaming", "ExtrasStreaming", "OnOffFocusCamControlEffect", "OnOffFocusCamControl",
    };

    /// <summary>The quest roots Tables/Scripts mark RequiredDLC (HibernateMode DLC): DLC stays out.</summary>
    public static readonly HashSet<string> DlcQuests = new(StringComparer.Ordinal)
    {
        "Barbora.kutnohorsko.navstevaLekare",
        "Barbora.kutnohorsko.kovarske_mikroquesty.katuvSleh",
        "Barbora.trosecko.zavodniPodkovy",
    };

    public static bool IsDlc(string path)
    {
        foreach (var q in DlcQuests)
            if (path.Length >= q.Length && path.StartsWith(q, StringComparison.Ordinal) && (path.Length == q.Length || path[q.Length] == '.')) return true;
        foreach (var seg in path.Split('.'))
            if (seg.StartsWith("dlc", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>A per-machine State: its type, or a module whose job is this machine's own streaming.</summary>
    public static bool PerMachine(QuestChange c) =>
        PerMachineTypes.Contains(c.Type) || c.Path.Contains(".streamprofileshandling.", StringComparison.Ordinal)
        || PlayerMinigame(c.Path, c.Type);

    /// <summary>
    /// WO-154 6.5: a State whose OWN name ends in "_minigame" is the quest's record of a minigame, not the
    /// minigame: the game's data has exactly one, the knight's dice in utokNaNebakov (...kostky_s_rytirem.
    /// dice_minigame, a DiceState None/InProgress/Won/Lost whose Won/Lost branch the quest), and WO-151's rule
    /// kept it per machine by its name. On (default): it is shared like any quest State; only a "_minigame"
    /// MODULE (a segment followed by '.') is the player's own. mp_minigame_outcome on|off (off = WO-151's rule).
    /// </summary>
    public static volatile bool MinigameOutcomeShared = true;

    /// <summary>
    /// WO-151 4.1: a minigame's own tutorial states are the player's at that minigame, never the world's.
    /// The field: the joiner's blacksmithing tutorial was mirrored through the host, whose copy was not at
    /// the anvil -- "tutorialState Exec 2->5: refused (host value 4)", put back 5->4, and the tutorial looped:
    /// he was stuck at the anvil. A path segment ending in "_minigame", or a *TutorialProgress State.
    /// WO-154 6.5: what it keeps per machine in the game's own quest data (Scripts.pak, 8,919 States): the 16
    /// States of the blacksmith's tutorial module (kovar...blacksmithing_minigame.*) and the four tutorials'
    /// progress drivers (Blacksmithing, Masterstrike, Combat, Alchemy TutorialProgress). No main-quest journal
    /// objective (0 of 626); four journal objectives of the blacksmith's tutorial (heat the forge, to the anvil,
    /// finish the falchion, quench the blade) are views over its module's States: each player's own anvil.
    /// </summary>
    public static bool PlayerMinigame(string path, string type) => PlayerMinigame(path, type, MinigameOutcomeShared);

    public static bool PlayerMinigame(string path, string type, bool outcomeShared)
    {
        if (type.EndsWith("TutorialProgress", StringComparison.Ordinal)) return true;
        int i = path.IndexOf("_minigame", StringComparison.Ordinal);
        while (i >= 0)
        {
            int end = i + "_minigame".Length;
            if (end < path.Length && path[end] == '.') return true;
            if (end == path.Length && !outcomeShared) return true;
            i = path.IndexOf("_minigame", end, StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>Host: why this change does NOT go to the joiners (null = it goes).</summary>
    public static string? HostSendVeto(QuestChange c)
    {
        if (!Wo137Text.IsPath(c.Path)) return "bad-path";
        if (c.QuestLen <= 0) return "not-a-quest";
        if (IsDlc(c.Path)) return "dlc";
        if (PerMachine(c)) return "per-machine";
        if (!c.Notify) return "silent";
        return null;
    }

    /// <summary>
    /// Joiner: why this local change does NOT become a request to the host (null = it does).
    /// Only ROOT transitions the world made here (examine, pickup, area, a conversation's
    /// outcome) are asked for; a mirrored change and a cascade are the host's graph's own.
    /// </summary>
    public static string? JoinerAskVeto(QuestChange c)
    {
        if (c.Mirror) return "mirror";
        if (c.Cascade) return "cascade";
        if (!Wo137Text.IsPath(c.Path)) return "bad-path";
        if (c.QuestLen <= 0) return "not-a-quest";
        if (IsDlc(c.Path)) return "dlc";
        if (PerMachine(c)) return "per-machine";
        if (!c.Notify) return "silent";
        if (!Wo137Text.IsPort(c.Port)) return "no-port";
        return null;
    }

    /// <summary>
    /// The mirror runs (detection, sends, applies, requests): never while a world loads or with
    /// this machine's mp_quest_sync off; the host with at least one partner while it plays Henry;
    /// a joiner in the host's world while the host's announced mode is on and it plays Henry
    /// (a Godwin stretch on either side holds it: S3).
    /// </summary>
    public static bool MirrorActive(bool holding, bool syncOn, bool host, bool joiner, int player, int peers, bool hostModeOn)
    {
        if (holding || !syncOn || player != 0) return false;
        if (host) return peers > 0;
        if (joiner) return hostModeOn;
        return false;
    }

    public enum Verdict { Apply, Already, Refused }

    /// <summary>
    /// The host's verdict on a joiner's "old -> new" transition, from the host's own current value:
    /// already there = counted once (dedupe); at the joiner's starting value = apply; anything else =
    /// the host's world is elsewhere (a step already past, or not reached) = refused.
    /// </summary>
    public static Verdict Judge(bool hostOk, int hostVal, int reqOld, int reqNew)
    {
        if (!hostOk) return Verdict.Refused;
        if (hostVal == reqNew) return Verdict.Already;
        return hostVal == reqOld ? Verdict.Apply : Verdict.Refused;
    }

    public static string VerdictName(Verdict v) => v switch { Verdict.Apply => "applied", Verdict.Already => "already", _ => "refused" };

    /// <summary>The DLL's apply results (native wo137.h Applied).</summary>
    public static string AppliedName(byte r) => r switch
    {
        0 => "changed", 1 => "unchanged", 2 => "no-node", 3 => "not-a-state", 4 => "no-port", 5 => "port-refused",
        6 => "asleep", 7 => "fault", 8 => "unarmed", 9 => "not-a-quest", _ => $"code-{r}",
    };

    /// <summary>An apply result worth another try later (the module is hibernating here, or the DLL did not answer).</summary>
    public static bool Retryable(byte r) => r is 6 or 255;

    // ------------------------------------------------------------------ texts (LootMsg shape, space-separated)

    static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    static string P(string port) => string.IsNullOrEmpty(port) ? "-" : port;

    public static string ChangeText(QuestChange c) =>
        $"{c.Seq.ToString(CultureInfo.InvariantCulture)} {c.Flags.ToString(CultureInfo.InvariantCulture)} {I(c.Old)} {I(c.New)} {P(c.Port)} {I(c.QuestLen)} {c.Path}";

    public static bool TryParseChangeText(string text, out QuestChange c)
    {
        c = default;
        var f = text.Split(' ');
        if (f.Length != 7) return false;
        if (!uint.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint seq)) return false;
        if (!byte.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out byte fl)) return false;
        if (!Wo137Text.TryInt(f[2], out int old) || !Wo137Text.TryInt(f[3], out int nw)) return false;
        if (!Wo137Text.IsPortOrDash(f[4])) return false;
        if (!Wo137Text.TryInt(f[5], out int ql) || ql < 0) return false;
        if (!Wo137Text.IsPath(f[6]) || ql > f[6].Length) return false;
        c = new QuestChange(seq, fl, old, nw, f[4] == "-" ? "" : f[4], "", f[6], ql);
        return true;
    }

    public static string RequestText(QuestChange c) =>
        $"{c.Flags.ToString(CultureInfo.InvariantCulture)} {I(c.Old)} {I(c.New)} {c.Port} {I(c.QuestLen)} {c.Path}";

    public static bool TryParseRequestText(string text, out QuestChange c)
    {
        c = default;
        var f = text.Split(' ');
        if (f.Length != 6) return false;
        if (!byte.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte fl)) return false;
        if (!Wo137Text.TryInt(f[1], out int old) || !Wo137Text.TryInt(f[2], out int nw)) return false;
        if (!Wo137Text.IsPort(f[3])) return false;
        if (!Wo137Text.TryInt(f[4], out int ql) || ql <= 0) return false;
        if (!Wo137Text.IsPath(f[5]) || ql > f[5].Length) return false;
        c = new QuestChange(0, fl, old, nw, f[3], "", f[5], ql);
        return true;
    }

    public static readonly string[] Verdicts = ["applied", "already", "refused", "failed", "off", "held", "notquest", Wo154Rules.VerdictContested];

    public static string ResultText(string verdict, int hostVal, string hostPort, string path) => $"{verdict} {I(hostVal)} {P(hostPort)} {path}";

    public static bool TryParseResultText(string text, out string verdict, out int hostVal, out string hostPort, out string path)
    {
        verdict = hostPort = path = ""; hostVal = 0;
        var f = text.Split(' ');
        if (f.Length != 4 || Array.IndexOf(Verdicts, f[0]) < 0) return false;
        if (!Wo137Text.TryInt(f[1], out hostVal) || !Wo137Text.IsPortOrDash(f[2]) || !Wo137Text.IsPath(f[3])) return false;
        verdict = f[0]; hostPort = f[2] == "-" ? "" : f[2]; path = f[3];
        return true;
    }

    public readonly record struct CheckpointEntry(string Path, int Val, string Port);

    /// <summary>The host's checkpoint as message texts ("part nparts val:port:path ..."), each at most maxText long.</summary>
    public static List<string> CheckpointTexts(IReadOnlyList<CheckpointEntry> entries, int maxText = Protocol.QuestTextMax)
    {
        var bodies = new List<StringBuilder>();
        var cur = new StringBuilder();
        foreach (var e in entries)
        {
            string item = $"{I(e.Val)}:{P(e.Port)}:{e.Path}";
            if (cur.Length > 0 && cur.Length + 1 + item.Length + 12 > maxText) { bodies.Add(cur); cur = new StringBuilder(); }
            if (cur.Length > 0) cur.Append(' ');
            cur.Append(item);
        }
        if (cur.Length > 0) bodies.Add(cur);
        var outp = new List<string>(bodies.Count);
        for (int i = 0; i < bodies.Count; i++) outp.Add($"{I(i + 1)} {I(bodies.Count)} {bodies[i]}");
        return outp;
    }

    public static bool TryParseCheckpointText(string text, out int part, out int nparts, out List<CheckpointEntry> entries)
    {
        part = nparts = 0; entries = [];
        var f = text.Split(' ');
        if (f.Length < 3 || !Wo137Text.TryInt(f[0], out part) || !Wo137Text.TryInt(f[1], out nparts)) return false;
        if (part < 1 || nparts < 1 || part > nparts || nparts > 1000) return false;
        for (int i = 2; i < f.Length; i++)
        {
            var g = f[i].Split(':');
            if (g.Length != 3 || !Wo137Text.TryInt(g[0], out int v) || !Wo137Text.IsPortOrDash(g[1]) || !Wo137Text.IsPath(g[2])) return false;
            entries.Add(new CheckpointEntry(g[2], v, g[1] == "-" ? "" : g[1]));
        }
        return true;
    }

    public static string TalkText(bool on, string npc) => $"{(on ? "on" : "off")} {npc}";

    public static bool TryParseResyncText(string text, out string why)
    {
        why = text;
        return Wo137Text.IsWord(text);
    }

    /// <summary>
    /// A host change is kept only while this game's world is the host's, or is being loaded for a
    /// join: the join brings the host's world exactly as it is, so a change from before that load is
    /// already in it -- replayed onto it, an older step would set a State back (and its consumers run twice).
    /// </summary>
    public static bool KeepHostChange(bool joinedWorld, string? joinPhase) =>
        joinedWorld || joinPhase is "loading" or "post-load";

    /// <summary>
    /// A checkpoint entry is the host's value AND the port that made it, from the same sent change.
    /// A live value that differs from the last one sent is a change still on its way: left out this round.
    /// </summary>
    public static bool CheckpointConsistent(bool seen, int lastSentVal, bool liveOk, int liveVal) =>
        seen && liveOk && liveVal == lastSentVal;

    public static bool TryParseTalkText(string text, out bool on, out string npc)
    {
        on = false; npc = "";
        var f = text.Split(' ');
        if (f.Length != 2 || !Wo137Text.IsOnOff(f[0]) || !Wo137Text.IsNpc(f[1])) return false;
        on = f[0] == "on"; npc = f[1];
        return true;
    }

    public static bool TryParseModeText(string text, out bool on, out string why)
    {
        on = false; why = "";
        var f = text.Split(' ', 2);
        if (f.Length < 1 || !Wo137Text.IsOnOff(f[0])) return false;
        on = f[0] == "on"; why = f.Length > 1 ? f[1] : "";
        foreach (char ch in why) if (ch < 0x20 || ch > 0x7E) return false;
        return true;
    }

    /// <summary>
    /// The Set port that gives a State this value, when the host's change named no port
    /// (a checkpoint entry, a correction): the host's own last port for that path.
    /// </summary>
    public static string? PortForCorrection(string? lastHostPort) => Wo137Text.IsPort(lastHostPort) ? lastHostPort : null;

    // ------------------------------------------------------------------ the engine's dialogue lines

    /// <summary>WO-147: Bark = an attempt whose meta overrides are all combat shouts or the player's own barks
    /// (<see cref="IsBarkAttempt"/>): never a conversation (the field held a hostile bandit on the host at every scream).</summary>
    public readonly record struct QuestLineInfo(string Kind, int Id, string[] Souls, int Player, bool Bark = false);

    /// <summary>
    /// WO-147: a dialogue attempt that is a bark, not a conversation: at least one soul carries a
    /// " - meta override: " and every override is a combat shout (COMBAT_*, SKIRMISH_*) or the player's own
    /// bark (HRAC_*: tired, hungry, the horse). In the field's game logs 1,707 such attempts with the player
    /// opened the dialogue camera twice (coincidences); other overrides are real conversations -- the dice
    /// player's KOSTKAR_UNISEX opened it 31 times of 31, and bargaining (SMLOUVANI) is one too.
    /// </summary>
    public static bool IsBarkAttempt(string souls)
    {
        const string mo = " - meta override: ";
        bool any = false;
        int i = 0;
        while ((i = souls.IndexOf(mo, i, StringComparison.Ordinal)) >= 0)
        {
            i += mo.Length;
            int end = i;
            while (end < souls.Length && (char.IsAsciiLetterOrDigit(souls[end]) || souls[end] == '_')) end++;
            if (end == i) continue;   // an empty override says nothing
            var tag = souls.AsSpan(i, end - i);
            if (!(tag.StartsWith("COMBAT_", StringComparison.Ordinal) || tag.StartsWith("SKIRMISH_", StringComparison.Ordinal)
                  || tag.StartsWith("HRAC_", StringComparison.Ordinal))) return false;
            any = true;
        }
        return any;
    }

    /// <summary>
    /// "Soul 'Dude' requested dialog. Assigned id is 241"                                 -> request 241
    /// "Attempting to start new dialogue (runtime id '241') with souls 'Ex: Dude; Ex: x'" -> attempt 241 [Dude, x]
    /// "[ID: 246] Dialog ending [Ex0: a Ex1: b state: CLEANUP flags: 9104]"               -> end 246 [a, b]
    /// "Switching to player 1"                                                            -> player 1
    /// A soul's " - meta override: X" suffix is dropped. Names are checked like any peer text.
    /// </summary>
    public static bool TryParseQuestLine(string line, out QuestLineInfo info)
    {
        info = default;
        static bool Num(string s, out int v) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v);
        const string req = "Soul 'Dude' requested dialog. Assigned id is ";
        const string att = "Attempting to start new dialogue (runtime id '";
        const string sw = "Switching to player ";
        if (line.StartsWith(req, StringComparison.Ordinal))
        {
            if (!Num(line[req.Length..].Trim(), out int id)) return false;
            info = new QuestLineInfo("request", id, [], -1);
            return true;
        }
        if (line.StartsWith(att, StringComparison.Ordinal))
        {
            int q = line.IndexOf('\'', att.Length);
            if (q < 0 || !Num(line[att.Length..q], out int id)) return false;
            int s0 = line.IndexOf("with souls '", q, StringComparison.Ordinal);
            if (s0 < 0) return false;
            int s1 = line.IndexOf('\'', s0 + 12);
            string souls = s1 > s0 ? line[(s0 + 12)..s1] : line[(s0 + 12)..];
            var names = new List<string>();
            foreach (var part in souls.Split(';'))
            {
                var t = part.Trim();
                if (t.StartsWith("Ex: ", StringComparison.Ordinal) || t.StartsWith("Nx: ", StringComparison.Ordinal)) t = t[4..];
                int sp = t.IndexOf(' ');
                if (sp > 0) t = t[..sp];
                if (Wo137Text.IsNpc(t)) names.Add(t);
            }
            info = new QuestLineInfo("attempt", id, [.. names], -1, IsBarkAttempt(souls));
            return true;
        }
        if (line.StartsWith("[ID: ", StringComparison.Ordinal))
        {
            // "[ID: 246] Dialog ending [Ex0: a Ex1: b state: ...]" and, for a dialogue that ended
            // before any response played, "[ID: 79] Dialog ends but no response was played. (Forced: 'N') [Ex0: a ...]"
            int b = line.IndexOf(']');
            int d = line.IndexOf("] Dialog end", StringComparison.Ordinal);
            if (b < 0 || d != b || !Num(line[5..b], out int id)) return false;
            int sq = line.IndexOf("[Ex0: ", b, StringComparison.Ordinal);
            if (sq < 0) sq = line.IndexOf("[Nx0: ", b, StringComparison.Ordinal);
            if (sq < 0) return false;
            var names = new List<string>();
            var f = line[(sq + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < f.Length; i++)
            {
                if (f[i] == "state:") break;
                if (f[i].Length >= 4 && (f[i][0] == 'E' || f[i][0] == 'N') && f[i][1] == 'x' && f[i].EndsWith(':') && Wo137Text.IsNpc(f[i + 1])) { names.Add(f[i + 1]); i++; }
            }
            info = new QuestLineInfo("end", id, [.. names], -1);
            return true;
        }
        if (line.StartsWith(sw, StringComparison.Ordinal))
        {
            var t = line[sw.Length..].Trim();
            int sp = t.IndexOf(' ');
            if (sp > 0) t = t[..sp];
            if (!Num(t, out int pl)) return false;
            info = new QuestLineInfo("player", 0, [], pl);
            return true;
        }
        return false;
    }
}

/// <summary>
/// WO-137: the joiner's apply queue -- the host's changes in the host's order (seq),
/// never applied while a world loads (WO-136's hold), each applied once.
/// </summary>
public sealed class QuestApplyQueue
{
    private readonly object _gate = new();
    private readonly LinkedList<QuestChange> _q = new();
    private uint _lastSeq;          // the newest host seq taken in (a host that restarted counts from 1 again: Reset)
    public int MaxQueued { get; init; } = 4096;
    public long Dropped { get; private set; }
    public long Duplicates { get; private set; }

    public int Count { get { lock (_gate) return _q.Count; } }

    /// <summary>In the host's order. A seq at or below the newest one already taken is a duplicate (a resend) and is ignored.</summary>
    public bool Enqueue(QuestChange c)
    {
        lock (_gate)
        {
            if (c.Seq != 0 && c.Seq <= _lastSeq) { Duplicates++; return false; }
            if (c.Seq != 0) _lastSeq = c.Seq;
            if (_q.Count >= MaxQueued) { _q.RemoveFirst(); Dropped++; }
            _q.AddLast(c);
            return true;
        }
    }

    /// <summary>A correction goes to the front (the next apply) -- it is the host's value now.</summary>
    public void EnqueueFront(QuestChange c)
    {
        lock (_gate) _q.AddFirst(c);
    }

    public bool TryPeek(out QuestChange c)
    {
        lock (_gate)
        {
            if (_q.First is { } n) { c = n.Value; return true; }
            c = default; return false;
        }
    }

    public void Pop() { lock (_gate) { if (_q.First is not null) _q.RemoveFirst(); } }

    /// <summary>
    /// Removes the change that was peeked and applied -- not blindly the front: a correction
    /// put in front meanwhile (EnqueueFront, from another task) must stay for the next apply.
    /// </summary>
    public bool Remove(QuestChange c)
    {
        lock (_gate)
        {
            for (var n = _q.First; n is not null; n = n.Next)
                if (n.Value.Equals(c)) { _q.Remove(n); return true; }
            return false;
        }
    }

    /// <summary>The newest host seq taken in (0 after a reset).</summary>
    public uint LastSeq { get { lock (_gate) return _lastSeq; } }

    /// <summary>A new host world (a host reload, a rejoin): the old queue is the old world's.</summary>
    public int Reset()
    {
        lock (_gate) { int n = _q.Count; _q.Clear(); _lastSeq = 0; return n; }
    }
}
