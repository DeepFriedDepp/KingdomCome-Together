using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-140: the engine-free rules of sleeping together and of the "own world"
/// trap (docs/WO-140-findings.md). <see cref="GameBridge"/> feeds them its live
/// flags; Wo140Tests pins them.
/// </summary>
public static class Wo140Rules
{
    // ------------------------------------------------------------------ the vote

    /// <summary>
    /// A sleep (or a wait) needs everyone's yes: mp_sleep_vote on, a session with
    /// at least one partner, and this machine's world is the shared world (the
    /// host of a shared world, or a joiner whose loaded world is the host's).
    /// Solo, separate worlds and a joiner still in its own save: unchanged.
    /// </summary>
    public static bool VoteRequired(bool on, bool roleKnown, bool isHost, bool sharedWorld, bool joinerShared, bool joinedWorld, int peers)
        => on && roleKnown && peers > 0 && (isHost ? sharedWorld : joinerShared && joinedWorld);

    public enum Verdict { Pending, Go, No }

    /// <summary>One vote: who asked, who must still answer, and the answers so far.</summary>
    public sealed class Vote
    {
        public uint Id { get; init; }
        public byte Asker { get; init; }
        public string Kind { get; init; } = "sleep";
        public bool Save { get; init; }
        /// <summary>This machine's own player asked.</summary>
        public bool Mine { get; init; }
        /// <summary>The host collects a joiner's vote for it: the joiner to answer (0xFF = none).</summary>
        public byte ReplyTo { get; init; } = 0xFF;
        public long DeadlineMs { get; set; }
        /// <summary>Who must say yes (ghost ids; <see cref="LocalMember"/> for this machine's own player).</summary>
        public HashSet<byte> Members { get; } = new();
        public Dictionary<byte, string> Answers { get; } = new();
        public string NoReason { get; private set; } = "";

        public void Answer(byte who, string answer)
        {
            if (!Members.Contains(who) || Answers.ContainsKey(who)) return;
            Answers[who] = answer;
        }

        public Verdict Evaluate(long nowMs)
        {
            foreach (var (_, a) in Answers)
                if (a != "yes") { NoReason = a; return Verdict.No; }
            if (Members.All(Answers.ContainsKey)) return Verdict.Go;
            if (nowMs >= DeadlineMs) { NoReason = "timeout"; return Verdict.No; }
            return Verdict.Pending;
        }

        /// <summary>A member left the session: its answer can never come (counted as a no).</summary>
        public void Left(byte who) { if (Members.Contains(who) && !Answers.ContainsKey(who)) Answers[who] = "left"; }
    }

    /// <summary>
    /// A vote id: the asker's ghost id and a counter, (ghost &lt;&lt; 16) | n. Below 2^24 on purpose: the
    /// game's Lua numbers are 32-bit floats (observed: id 16777217 came back from the mod as 16777216),
    /// and the mod carries the id to the answer.
    /// </summary>
    public static uint VoteId(byte ghost, uint n) => ((uint)ghost << 16) | (n & 0xFFFF);

    /// <summary>The member id that stands for this machine's own player in a vote the host collects.</summary>
    public const byte LocalMember = 0xFE;

    /// <summary>The prompt on the other player's screen.</summary>
    public static string PromptText(string who, string kind) => kind == "wait"
        ? $"{who} wants to wait. Wait too?"
        : $"{who} wants to sleep. Sleep too?";

    public const string WaitingText = "Waiting for other players...";
    public const string NotReadyText = "Other players are not ready to sleep yet!";

    /// <summary>
    /// The skip the accepter's game runs: the game's own sleep (C_SkipTime id 2,
    /// the sleep screen and its rest) for a sleep, its own wait (id 1) for a wait.
    /// </summary>
    public static byte AccepterSkipId(string kind) => kind == "wait" ? (byte)1 : (byte)2;

    /// <summary>The asker's own C_SkipTime id -> the vote's kind (1 wait, 2 sleep, anything else: sleep).</summary>
    public static string KindOfSkipId(int id) => id == 1 ? "wait" : "sleep";

    /// <summary>The picker's hours as the wire carries them: 0.25 .. 24, else null (nothing starts).</summary>
    public static float? HoursOk(float h) => float.IsFinite(h) && h >= 0.25f && h <= Wo140Text.MaxHours ? h : null;

    // ------------------------------------------------------------------ one clock

    /// <summary>
    /// The fall-safe: a joiner's clock ahead of the host's is pulled back to the
    /// host's at once. The host's clock is extrapolated from its last report
    /// (<paramref name="hostAt"/>, <paramref name="hostAgeS"/> seconds ago) at the
    /// natural ratio. Returns the target (world seconds) when this joiner is more
    /// than <paramref name="toleranceS"/> ahead, else null. Never on a stale or
    /// future report, and never while the host is inside a skip (its clock runs
    /// faster than the ratio there, so the extrapolation would undershoot).
    /// </summary>
    public static uint? PullBackTarget(uint mine, uint hostAt, double hostAgeS, bool hostSkipping,
                                       double ratio = 15.0, uint toleranceS = 300, double maxAgeS = 90)
    {
        if (hostAt == 0 || hostSkipping || hostAgeS < 0 || hostAgeS > maxAgeS) return null;
        double hostNow = hostAt + hostAgeS * ratio;
        if (mine <= hostNow + toleranceS) return null;
        return (uint)Math.Round(hostNow);
    }

    // ------------------------------------------------------------------ the own-world trap

    /// <summary>
    /// A joiner connected from its own world: the host runs a shared world, this
    /// game is in a world, and that world is not the host's (no join, rejoin or
    /// leave running). The host's world must not be applied to it.
    /// </summary>
    public static bool Separate(bool roleKnown, bool isHost, bool hostModeKnown, bool hostShared, bool inWorld, bool joinedWorld, bool joinBusy)
        => roleKnown && !isHost && hostModeKnown && hostShared && inWorld && !joinedWorld && !joinBusy;

    /// <summary>
    /// The host's world, frame by frame: what a separate joiner drops (its NPCs,
    /// their hits and deaths, their actions, the items, loot, quests, crime and
    /// the leash). Ghosts, names, the voice, the players' own vitals, the clock and
    /// the weather still pass: the players still see each other.
    /// </summary>
    public static bool DroppedWhenSeparate(int type) =>
        type == Protocol.NpcStateDown || type == Protocol.NpcDamageDown || type == Protocol.DamageDown || type == Protocol.DeathDown
        || type == Protocol.ActionDown || type == Protocol.ItemDropDown || type == Protocol.ItemClaimDown
        || type == Protocol.LootAskDown || type == Protocol.LootHostDown || type == Protocol.QuestHostDown || type == Protocol.QuestAskDown
        || type == Protocol.CrimeHostDown || type == Protocol.CrimeAskDown || type == Protocol.LeashDown || type == Protocol.LeashStateDown;

    public const string SeparateText = "You loaded your own save. To play in your host's world, quit the game, start it again and wait at the main menu.";
    public const string SeparateLogLine = "MP-JOIN joiner: connected from its own world -- NOT joined (separate)";

    public static string TypeName(int type) => type switch
    {
        Protocol.NpcStateDown => "npc-state", Protocol.NpcDamageDown => "npc-damage", Protocol.DamageDown => "damage",
        Protocol.DeathDown => "death", Protocol.ActionDown => "action", Protocol.ItemDropDown => "item-drop",
        Protocol.ItemClaimDown => "item-claim", Protocol.LootAskDown or Protocol.LootHostDown => "loot",
        Protocol.QuestHostDown or Protocol.QuestAskDown => "quest", Protocol.CrimeHostDown or Protocol.CrimeAskDown => "crime",
        Protocol.LeashDown or Protocol.LeashStateDown => "leash", _ => "0x" + type.ToString("X2", CultureInfo.InvariantCulture),
    };
}

/// <summary>WO-140: one 0xA5 frame of the DLL's sleep half (native wo140.h).</summary>
public readonly record struct Wo140Frame(byte Kind, byte Edge, byte Id, float Hours, byte State)
{
    public const byte KindHeld = 1, KindState = 2;
    public const byte EdgeNone = 0, EdgeOpened = 1, EdgeBegan = 2, EdgeBackedOut = 3, EdgeEnded = 4;

    public static bool TryParse(ReadOnlySpan<byte> b, out Wo140Frame f)
    {
        f = default;
        if (b.Length == 2 && b[0] == KindHeld) { f = new Wo140Frame(KindHeld, 0, b[1], 0, 0); return true; }
        if (b.Length == 8 && b[0] == KindState && b[1] <= EdgeEnded)
        {
            f = new Wo140Frame(KindState, b[1], b[2], System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(b[3..]), b[7]);
            return true;
        }
        return false;
    }

    public static string EdgeName(byte e) => e switch
    {
        EdgeOpened => "opened", EdgeBegan => "began", EdgeBackedOut => "backed-out", EdgeEnded => "ended", _ => "none",
    };
}
