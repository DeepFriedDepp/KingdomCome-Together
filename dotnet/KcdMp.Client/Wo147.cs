using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-147: the pure rules of the work order that let the joiner fight and made
/// the leash pull (docs/WO-147-findings.md). GameBridge.Wo147.cs owns the calls
/// into the game; this owns the decisions, and Wo147Tests pins them.
/// </summary>
public static class Wo147Rules
{
    // ================================================================ the leash: real positions

    /// <summary>A position older than this (the link backed up, or the sender stalled) is not a reading.</summary>
    public const int PositionStaleMs = 3000;

    /// <summary>Horizontal speed above this between two readings is not walking, running or riding (a horse gallops ~15 m/s).</summary>
    public const float FlyingSpeedMps = 40f;

    /// <summary>A reading this far from the previous one in one step is a jump (a teleport, a pull, a respawn).</summary>
    public const float JumpM = 200f;

    public enum Motion { Normal, Flying, Jump }

    /// <summary>
    /// WO-147: one peer's positions as they arrive, judged on the SENDER's clock. The field (the joiner
    /// 40-60 s behind on the link) delivered a minute of the joiner's flight in a few seconds: the host
    /// read 742 -> 1,481 -> 1,926 -> 2,445 m one second apart, from real positions that were long past.
    ///
    /// The lag of a sample is its delivery time minus the fastest delivery seen in the window
    /// (<c>arrival - senderMs</c> carries both machines' clock offset and the transit; its minimum is the
    /// offset plus the best transit): a backlog shows up as lag whatever the clocks are. A reading is fresh
    /// while the newest sample's lag is under <see cref="PositionStaleMs"/> and it arrived recently.
    /// Speeds are measured on the sender's clock, so a burst of old samples is never a sprint.
    /// </summary>
    public sealed class PeerPositions
    {
        private const long WindowMs = 120_000;
        private readonly Queue<(long ArrivalMs, long Skew)> _skews = new();
        private long _baseSkew = long.MaxValue;
        private (float X, float Y, uint SenderMs, long ArrivalMs)? _last;
        private long _fastSinceMs = -1;
        private int _fastSamples;

        public float X => _last?.X ?? 0;
        public float Y => _last?.Y ?? 0;
        public bool Any => _last is not null;
        /// <summary>The newest sample's lag behind the fastest delivery in the window (ms).</summary>
        public long LagMs { get; private set; }
        public Motion LastMotion { get; private set; } = Motion.Normal;
        public float LastSpeedMps { get; private set; }

        /// <summary>One sample: its position, the sender's own ms stamp (0 = none) and our arrival ms.</summary>
        public Motion Feed(float x, float y, uint senderMs, long arrivalMs)
        {
            var prev = _last;
            _last = (x, y, senderMs, arrivalMs);
            LastMotion = Motion.Normal;
            LastSpeedMps = 0;
            if (senderMs == 0) { LagMs = 0; return Motion.Normal; }   // an old sender: arrival time is all there is
            long skew = arrivalMs - senderMs;
            _skews.Enqueue((arrivalMs, skew));
            while (_skews.Count > 0 && arrivalMs - _skews.Peek().ArrivalMs > WindowMs) _skews.Dequeue();
            _baseSkew = long.MaxValue;
            foreach (var s in _skews) _baseSkew = Math.Min(_baseSkew, s.Skew);
            LagMs = Math.Max(0, skew - _baseSkew);
            if (prev is not { } p || p.SenderMs == 0) return Motion.Normal;
            long dtMs = unchecked((long)(uint)(senderMs - p.SenderMs));
            if (dtMs <= 0 || dtMs > 10_000) { _fastSinceMs = -1; _fastSamples = 0; return Motion.Normal; }
            double d = LeashLogic.Dist2D(p.X, p.Y, x, y);
            LastSpeedMps = (float)(d * 1000.0 / dtMs);
            if (d >= JumpM && dtMs <= 1500 && _fastSamples == 0) { LastMotion = Motion.Jump; return Motion.Jump; }
            if (LastSpeedMps > FlyingSpeedMps)
            {
                if (_fastSinceMs < 0) _fastSinceMs = senderMs;
                _fastSamples++;
                // Sustained: more than one sample and at least half a second of the sender's time.
                if (_fastSamples >= 2 && unchecked((long)(uint)(senderMs - (uint)_fastSinceMs)) >= 500) LastMotion = Motion.Flying;
            }
            else { _fastSinceMs = -1; _fastSamples = 0; }
            return LastMotion;
        }

        /// <summary>A reading to act on: the newest sample is current (not a backlog) and it arrived within <paramref name="maxAgeMs"/>.</summary>
        public bool Fresh(long nowMs, long maxAgeMs = 5000) =>
            _last is { } l && nowMs - l.ArrivalMs <= maxAgeMs && LagMs < PositionStaleMs;

        public void Reset()
        {
            _skews.Clear(); _baseSkew = long.MaxValue; _last = null; LagMs = 0;
            _fastSinceMs = -1; _fastSamples = 0; LastMotion = Motion.Normal; LastSpeedMps = 0;
        }
    }

    /// <summary>
    /// WO-147: the joiner's own motion, sampled every leash tick (250 ms). The field's "this game jumped
    /// 403 m by itself (a fast travel the block missed?)" was the joiner flying (the Modding Tools
    /// build's developer fly mode, F3): 3 minutes at 30-1,455 m/s, 900 m up, past the map's edge. No
    /// fast travel ran (no "FastTravel: started" line). Classify it instead of calling it one.
    /// </summary>
    public sealed class OwnMotion
    {
        private (float X, float Y, long AtMs)? _prev;
        private long _fastSinceMs = -1;
        public Motion Last { get; private set; } = Motion.Normal;
        public float SpeedMps { get; private set; }
        /// <summary>The last step's horizontal distance (m).</summary>
        public float StepM { get; private set; }
        public bool Flying => Last == Motion.Flying;

        public Motion Feed(float x, float y, long nowMs, bool mounted)
        {
            var p = _prev;
            _prev = (x, y, nowMs);
            Last = Motion.Normal;
            SpeedMps = 0;
            if (p is not { } q) return Last;
            long dt = nowMs - q.AtMs;
            if (dt <= 0 || dt > 5000) { _fastSinceMs = -1; return Last; }
            double d = LeashLogic.Dist2D(q.X, q.Y, x, y);
            SpeedMps = (float)(d * 1000.0 / dt);
            StepM = (float)d;
            if (d >= JumpM && _fastSinceMs < 0) { Last = Motion.Jump; return Last; }
            float limit = mounted ? FlyingSpeedMps * 1.5f : FlyingSpeedMps;
            if (SpeedMps > limit)
            {
                if (_fastSinceMs < 0) _fastSinceMs = q.AtMs;
                if (nowMs - _fastSinceMs >= 500) Last = Motion.Flying;
            }
            else _fastSinceMs = -1;
            return Last;
        }

        public void Reset() { _prev = null; _fastSinceMs = -1; Last = Motion.Normal; SpeedMps = 0; StepM = 0; }
    }

    /// <summary>
    /// WO-147: where a pull puts the joiner when the game has no ground beside the host (the field's
    /// "no ground in 8 directions -- NOT placed", 1.2 and 1.9 km out: the navigation mesh around the host
    /// is not loaded on the joiner's machine that far away). A spot the host itself stood on a moment ago,
    /// 1.5-6 m from where he stands now, is ground; with none (the host stood still, or just arrived) the
    /// host's own spot, 1 m toward where the joiner comes from. The caller places the joiner there exactly
    /// with the fall damage held, then on the ground beside the host once the area has loaded.
    /// </summary>
    public static (float X, float Y, float Z) PullFallbackSpot(float hx, float hy, float hz, float fromX, float fromY,
                                                               IReadOnlyList<(float X, float Y, float Z, long AtMs)> hostTrail, long nowMs)
    {
        for (int i = hostTrail.Count - 1; i >= 0; i--)
        {
            var t = hostTrail[i];
            if (nowMs - t.AtMs > 30_000) break;
            double d = LeashLogic.Dist2D(hx, hy, t.X, t.Y);
            if (d >= 1.5 && d <= 6.0 && Math.Abs(t.Z - hz) < 3.0) return (t.X, t.Y, t.Z + 0.3f);
        }
        double dx = fromX - hx, dy = fromY - hy, len = Math.Sqrt(dx * dx + dy * dy);
        if (!(len > 0.01) || !double.IsFinite(len)) { dx = 1; dy = 0; len = 1; }
        return ((float)(hx + dx / len), (float)(hy + dy / len), hz + 0.5f);
    }

    // ================================================================ the frame backlog (mp_npc_catchup)

    /// <summary>
    /// The processor runs behind when the frame it takes was read this long ago (normally a few ms). The field:
    /// the joiner's agent handled the host's frames up to 833 s late behind its NPC traffic -- every Lua batch
    /// that filled waited on the game, which the release/re-acquire churn below made slower still.
    /// </summary>
    public const int CatchupLagMs = 250;

    /// <summary>The mod's silence window for a puppet (KCD2MP.npcSync.releaseS, 3 s): the agent applies it to arrivals.</summary>
    public const int SilenceReleaseMs = 3000;

    /// <summary>A swing cue (0x08) is an event, a resync row (0x40) a one-off: a sample carrying either is never skipped.</summary>
    public const byte TransientNpcFlags = 0x08 | Protocol.NpcStateFlagResync;

    /// <summary>Behind, every NPC still gets a sample through this often (a superseded one included).</summary>
    public const int CatchupMinPushMs = 300;

    /// <summary>
    /// A sample of <paramref name="seq"/> while the processor is behind, when the newest one read for the same NPC
    /// is another (a later) sample with exactly the same flags: everything it says the newer one says too (a
    /// position, a heading, health) -- skipped, unless this NPC has had nothing through for
    /// <see cref="CatchupMinPushMs"/> (under a sustained lag every sample is superseded by the time it is taken:
    /// the live A/B starved walkers for up to 38 s). Never when the flags differ (a death, a knockout, a weapon
    /// drawn) or carry an event; never when the processor keeps up.
    /// </summary>
    public static bool SupersededUnderLag(bool on, double lagMs, ushort seq, byte flags, ushort newestSeq, byte newestFlags,
                                          double sinceLastPushMs = 0) =>
        on && lagMs >= CatchupLagMs && newestSeq != seq && newestFlags == flags && (flags & TransientNpcFlags) == 0
        && sinceLastPushMs < CatchupMinPushMs;

    /// <summary>
    /// The agent's word that an NPC's stream fell silent: nothing read for it for <paramref name="releaseMs"/>, and
    /// the processor has handed the mod its last sample (so the mod's puppet is not released ahead of its own late
    /// data -- the field's churn: released for "silence" while its samples sat in the agent's queue, started again
    /// by the next one). Once per silence.
    /// </summary>
    public static bool SilenceDue(long nowMs, long lastArrivalMs, long lastProcessedArrivalMs, bool alreadySent, int releaseMs = SilenceReleaseMs) =>
        !alreadySent && nowMs - lastArrivalMs > releaseMs && lastProcessedArrivalMs >= lastArrivalMs;

    // ================================================================ the joiner's blows go to the host (Phase 1)

    /// <summary>
    /// A measured hit that moved under half a point of health AND of stamina is contact-frame noise (WO-40/WO-58).
    /// WO-147: a blow that cost only stamina (a block, a parry on a broken weapon) is no noise -- it goes too.
    /// </summary>
    public static bool HitCarriesNothing(float health, float stamina) => health < 0.5f && stamina < 0.5f;

    /// <summary>
    /// WO-147: on a joiner only this player's own blows go to the host. The DLL's sampler reports any drop near
    /// the player (the field's host avatar hit a hidden local copy); the "by the player" bit comes from the hit
    /// hook, so without an armed hook every drop still goes (the old rule). Humans and animals alike.
    /// </summary>
    public static bool DropNotOwnBlow(bool joinerActive, bool byPlayer, bool hookArmed) => joinerActive && !byPlayer && hookArmed;

    // ================================================================ the joiner fights (Phase 1.4)

    public readonly record struct HostileCopy(string Name, float Rel, float DistM, bool Alive);
    public readonly record struct Hostiles(bool Drawn, bool On, List<HostileCopy> Copies);

    /// <summary>The mod's KCD2MP_W147Hostiles reply after its token: "drawn=1 on=1 n=2 list=a:-1.00:4.2:1;b:0.40:9.0:1".</summary>
    public static Hostiles? ParseHostiles(string reply)
    {
        // the list is the last field and runs to the end (one bad entry never takes the rest with it)
        int li = reply.IndexOf("list=", StringComparison.Ordinal);
        string list = li >= 0 ? reply[(li + 5)..] : "";
        var f = (li >= 0 ? reply[..li] : reply).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool? drawn = null, on = null;
        foreach (var kv in f)
        {
            if (kv.StartsWith("drawn=", StringComparison.Ordinal)) drawn = kv == "drawn=1";
            else if (kv.StartsWith("on=", StringComparison.Ordinal)) on = kv == "on=1";
        }
        if (drawn is null || on is null) return null;
        var copies = new List<HostileCopy>();
        foreach (var item in list.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = item.Split(':');
            if (p.Length != 4 || !Wo137Text.IsPath("Barbora." + p[0]) && !System.Text.RegularExpressions.Regex.IsMatch(p[0], "^[A-Za-z0-9_]{1,64}$")) continue;
            if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rel)) continue;
            if (!float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d)) continue;
            copies.Add(new HostileCopy(p[0], rel, d, p[3] == "1"));
        }
        return new Hostiles(drawn.Value, on.Value, copies);
    }

    public enum EngageWant { Nothing, Engage, Release, Friendly }

    /// <summary>
    /// WO-147: one copy on one tick. An enemy (relationship at or under <paramref name="relMax"/>), alive,
    /// within the engage range (12 m to start; an engaged one is kept to 15 m, the DLL's own limit) while the
    /// weapon is out: engage. An engaged one is let go when it is no enemy any more, down or dead, past 15 m,
    /// or the weapon has been away 5 s. A friend is never engaged.
    /// </summary>
    public static EngageWant JudgeHostileEngage(float rel, float relMax, bool alive, float distM, bool drawn, bool engaged, bool sheathedLong)
    {
        if (!(rel <= relMax)) return EngageWant.Friendly;
        if (!alive) return engaged ? EngageWant.Release : EngageWant.Nothing;
        if (engaged)
        {
            if (distM > 15f || sheathedLong) return EngageWant.Release;
            return EngageWant.Engage;   // kept (the DLL's hold wants a fresh state every 3 s)
        }
        return drawn && distM <= 12f ? EngageWant.Engage : EngageWant.Nothing;
    }

    /// <summary>The state held on a copy this player engaged himself: combat mode, no zones (its own guard).</summary>
    public static BodyState2 LocalEngageState() =>
        new(0, 0, BodyState2Bits.CombatMode, WireZone.Undefined, WireGuardStance.None, WireZone.Undefined, 0, 0, 0);

    // ================================================================ quest safety (Phase 2b)

    public enum Destruction { None, FailsQuest, CancelsObjective, NpcDeadOrDown }

    public static string DestructionName(Destruction d) => d switch
    {
        Destruction.FailsQuest => "fails-a-quest", Destruction.CancelsObjective => "cancels-an-objective",
        Destruction.NpcDeadOrDown => "marks-someone-dead-or-down", _ => "none",
    };

    /// <summary>The value a Set&lt;Value&gt; port sets ("SetNpcIsDead" -> "NpcIsDead"); null for anything else.</summary>
    public static string? PortValueName(string? port) =>
        port is { Length: > 3 } p && p.StartsWith("Set", StringComparison.Ordinal) ? p[3..] : null;

    /// <summary>
    /// A value that marks someone dead or unconscious, by its own name (the quest data's names are English
    /// and Czech: NpcIsDead, SoldierDied, PlayerUnconscious, MasterKnockedOut, VazounJeMrtvy, NekdoUmrel,
    /// bykZabit ...). A player's own death counts too: in the host's world the host's player did not die.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex DeadOrDownRx = new(
        "dead|death|died|dies|killed|murder|unconsc|unconc|knock|mrtv|umrel|zmrel|zabit|zabil|smrt|bezvedom",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// ...unless the name is about something else (301 of the game's value names match the rule above; these
    /// are the 36 that do not mark anyone dead or down: NobodyDead, FrancekIsNotUnconscious, DeathTimer,
    /// CheckDeadBody, PlayerFoundDeadBody, SearchUnderBodies, ZvedniMrtvoluStart (pick up the corpse),
    /// SpokeWithBailiffAboutDeadBandit, MotherKnowsAboudDeadGoclin, Knockable, DogDeathPerception ...).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex NotDeadOrDownRx = new(
        "isnot|notdead|notuncon|nobody|noone|timer|check|found|search|deadbody|spoke|told|lied|knows|mrtvol|knockable|perception|reaction|prefab|rose|healed",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>A None-type value that takes a running objective back: the type's first value, or one named so.</summary>
    private static readonly System.Text.RegularExpressions.Regex WithdrawNameRx = new(
        "abort|cancel|fail|^lost|lost$|ztracen|reset|refus|missed|disabl|none$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>With no type data: a value whose name fails or cancels (Failed, Fail, Lost, Canceled, Cancelled, Aborted).</summary>
    private static readonly System.Text.RegularExpressions.Regex FailNameRx = new(
        "fail|cancel|cancell|abort|^lost$|lost$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool Objective(string ovt) => ovt is "Started" or "Updated" or "Completed";

    /// <summary>
    /// WO-147: is this joiner request DESTRUCTIVE -- does it fail a quest, cancel an objective, or mark an NPC
    /// dead or unconscious? From the quest data: the game marks every objective value with an
    /// ObjectiveValueType (None / Started / Updated / Completed / Canceled; 598 Canceled values in 233 names,
    /// many of them Czech, so a name rule alone misses 80). Destructive:
    ///   * a value of ObjectiveValueType Canceled (the engine's QuestProgress.Failed = a quest failing);
    ///   * an objective State taken back from a Started/Updated/Completed value to a None-type one that is
    ///     the type's first value or is named for it (None, Aborted, Canceled, Failed, Reseted ...: the
    ///     field's defeatOpponent_objective SetNone 1->0). Not every None-type value: 222 names are, and
    ///     most are progress (SetDone x9 and SilentDone are silent completions, Won, ItemObtained ...);
    ///   * a value whose name marks someone dead or unconscious (the field's npcIsDead SetNpcIsDead 0->1);
    ///   * with no type data, a value whose name fails or cancels.
    /// </summary>
    public static (Destruction Kind, string Why) Destructive(QuestValueIndex? idx, string? type, string? port, int oldVal, int newVal)
    {
        string? name = PortValueName(port);
        if (name is null) return (Destruction.None, "not a Set<Value> port");
        if (DeadOrDownRx.IsMatch(name) && !NotDeadOrDownRx.IsMatch(name)) return (Destruction.NpcDeadOrDown, $"{name} marks someone dead or unconscious");
        var defs = idx?.Definitions(type) ?? Array.Empty<QuestValueIndex.Value[]>();
        bool quest = type is "wh::questmodule::QuestProgress" or "QuestProgress";
        foreach (var def in defs)
        {
            int ni = Array.FindIndex(def, v => v.Name == name);
            if (ni < 0) continue;
            if (def[ni].Objective == "Canceled")
                return (quest ? Destruction.FailsQuest : Destruction.CancelsObjective, $"{type}.{name} is a Canceled objective value");
            if (def[ni].Objective == "None" && (ni == 0 || WithdrawNameRx.IsMatch(name))
                && oldVal >= 0 && oldVal < def.Length && Objective(def[oldVal].Objective))
                return (Destruction.CancelsObjective, $"{type}.{def[oldVal].Name} -> {name} takes a running objective back");
            return (Destruction.None, $"{type}.{name} ({def[ni].Objective})");
        }
        if (defs.Count == 0 && FailNameRx.IsMatch(name))
            return (quest ? Destruction.FailsQuest : Destruction.CancelsObjective, $"{name} fails or cancels (no type data)");
        return (Destruction.None, defs.Count == 0 ? "no type data" : $"{name} is not a value of {type}");
    }

    /// <summary>
    /// WO-147 (the field: "carryingBags SetCart 4->2 (the host had 3)": the host's LAST port for that State,
    /// SetCart, fired to reach Barn): the port a correction toward <paramref name="hostVal"/> may fire --
    /// only one known to produce that value:
    ///   1. from the type data: "Set" + the name of value <paramref name="hostVal"/>, when every definition
    ///      of the type agrees on that name;
    ///   2. else a port this machine has seen produce exactly that value on this State (learned);
    ///   3. else none (the next join loads the State exactly).
    /// </summary>
    public static string? CorrectionPort(QuestValueIndex? idx, string? type, int hostVal, IReadOnlyDictionary<string, int>? learnedPortValues)
    {
        var defs = idx?.Definitions(type) ?? Array.Empty<QuestValueIndex.Value[]>();
        string? byType = null;
        foreach (var def in defs)
        {
            if (hostVal < 0 || hostVal >= def.Length) { byType = null; break; }
            string cand = "Set" + def[hostVal].Name;
            if (byType is null) byType = cand;
            else if (byType != cand) { byType = null; break; }
        }
        if (byType is not null && Wo137Text.IsPort(byType)) return byType;
        if (learnedPortValues is not null)
            foreach (var (port, val) in learnedPortValues)
                if (val == hostVal && Wo137Text.IsPort(port)) return port;
        return null;
    }

    /// <summary>
    /// WO-147: the host's verdict on a destructive request, after its own world had its chance: the host's
    /// world reached the value itself (its NPC really died or went down, its own graph led there) =
    /// already; the step came out of the joiner's own conversation = applied as a conversation's outcome;
    /// otherwise refused (the joiner's copy is corrected back).
    /// </summary>
    public enum DestructiveVerdict { Already, ApplyConversation, Refuse }

    public static DestructiveVerdict JudgeDestructive(bool hostReachedIt, bool fromConversation) =>
        hostReachedIt ? DestructiveVerdict.Already : fromConversation ? DestructiveVerdict.ApplyConversation : DestructiveVerdict.Refuse;

    /// <summary>WO-147: set on a QuestAsk request's flags by the joiner when the change came out of its own conversation.</summary>
    public const byte FlagConversation = 32;

    /// <summary>How long the host waits for its own world to reach a destructive value before it refuses.</summary>
    public const int DestructiveWaitMs = 10_000;

    /// <summary>
    /// WO-147: the joiner's own verdict on a pull: which of its holds refuse it. The unsafe ones (a load,
    /// a dialogue, a cutscene, a down); a menu never. A forced pull (the host's hold cap ran out) is
    /// refused by a load alone -- a dialogue is ended first, a cutscene or a down is pulled through.
    /// </summary>
    public static LeashLogic.Hold JoinerRefusesPull(LeashLogic.Hold joinerHold, bool forced)
    {
        var unsafeHolds = LeashLogic.Hold.JoinerLoading | LeashLogic.Hold.JoinerDialogue | LeashLogic.Hold.JoinerCutscene | LeashLogic.Hold.JoinerDowned;
        return joinerHold & (forced ? LeashLogic.Hold.JoinerLoading : unsafeHolds);
    }
}
