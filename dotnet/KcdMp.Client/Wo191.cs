// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-191 -- 0.60.7: the pure rules of the hotfix (Wo191Tests pins them; Wo191ReplayTests runs the 0.60.5 session's logs through them).
///   1  a relaunched joiner's actions are never thrown away as stale: the receiver forgets a sender at every disconnect, re-naming and join,
///      and every agent run names itself in the action header (ActionGen.Revision = the run id; ActionInbox adopts a different run at once).
///      What is still refused is counted and logged per kind (WO191-ACTION, once per kind per minute).
///   2  no sweep resets a copy that fights: engaged, in combat mode, targeted by or targeting a player, or within 10 s of any of those.
///   3  an NPC or animal that targets a player and has no copy there is bound at once (the host pins it into the NPC stream); a blow that
///      still comes from an attacker with no body is counted as ghost damage (WO191-GHOST).
///   4  on the joiner, a fader that belongs to no scene is ended the game's own way after 3 s (WO191-FADER).
/// </summary>
public static class Wo191Rules
{
    // ================================================================ switches (the mod's w191_cfg; all ship on)

    public const bool DefaultActionForget = true, DefaultFightGuard = true, DefaultAttackerBind = true, DefaultFaderBound = true;

    // ================================================================ 1: the action channel

    /// <summary>1b: a fresh agent run's id, carried in ActionGen.Revision (never 0: 0 is a sender older than 0.60.7).</summary>
    public static byte NewRunId(Random? rng = null) => (byte)(rng ?? Random.Shared).Next(1, 256);

    /// <summary>1c: one WO191-ACTION line per kind per this long (the count in between is in the next line).</summary>
    public const long RejectLogEveryMs = 60_000;

    /// <summary>1c: what the receiver refused, per kind; <see cref="Note"/> says when a line is due.</summary>
    public sealed class RejectLog
    {
        private readonly Dictionary<string, (long LastMs, long Total, long Since)> _k = new(StringComparer.Ordinal);

        /// <summary>A refusal of <paramref name="kind"/> (null = undecodable) at <paramref name="nowMs"/>: the line's counts, or null when one was written within the minute.</summary>
        public (long Total, long Since)? Note(ActionKind? kind, ActionReject why, long nowMs)
        {
            string key = (kind?.ToString() ?? "?") + "|" + why;
            _k.TryGetValue(key, out var s);
            s.Total++; s.Since++;
            if (s.LastMs != 0 && nowMs - s.LastMs < RejectLogEveryMs) { _k[key] = s; return null; }
            var r = (s.Total, s.Since);
            s.LastMs = Math.Max(nowMs, 1); s.Since = 0;
            _k[key] = s;
            return r;
        }

        public long Total(ActionKind? kind, ActionReject why) =>
            _k.TryGetValue((kind?.ToString() ?? "?") + "|" + why, out var s) ? s.Total : 0;
    }

    // ================================================================ 2: no sweep on a copy that fights

    /// <summary>2: a sweep waits this long after the copy's last sign of a fight.</summary>
    public const long FightWindowMs = 10_000;

    /// <summary>
    /// 2: the fight signs of every copy (engaged, combat mode / the guard hold, a player targets it or it targets a player, a blow). A sweep
    /// of any trigger is refused while one holds or for <see cref="FightWindowMs"/> after the last; the skip is told once per copy per fight.
    /// </summary>
    public sealed class FightGuard
    {
        private readonly Dictionary<string, (long LastMs, string Why, long FightStartMs, long ToldFor)> _n = new(StringComparer.Ordinal);

        /// <summary>A sign that <paramref name="npc"/> fights (now). A sign after 10 s of quiet starts a new fight.</summary>
        public void Note(string npc, long nowMs, string why)
        {
            lock (_n)
            {
                _n.TryGetValue(npc, out var s);
                if (s.LastMs == 0 || nowMs - s.LastMs > FightWindowMs) s.FightStartMs = Math.Max(nowMs, 1);
                s.LastMs = Math.Max(nowMs, 1); s.Why = why;
                _n[npc] = s;
            }
        }

        /// <summary>Why a sweep of <paramref name="npc"/> is refused now, or null when it may run. The live flags count as a sign of now.</summary>
        public string? Blocks(string npc, long nowMs, bool engaged, bool combatMode, bool targetedByPlayer, bool targetsPlayer)
        {
            string? live = engaged ? "engaged" : combatMode ? "combat-mode" : targetedByPlayer ? "a-player-targets-it" : targetsPlayer ? "it-targets-a-player" : null;
            if (live is not null) { Note(npc, nowMs, live); return live; }
            lock (_n)
            {
                if (!_n.TryGetValue(npc, out var s) || s.LastMs == 0) return null;
                long ago = nowMs - s.LastMs;
                return ago <= FightWindowMs ? $"{s.Why}-{ago / 1000.0:F1}s-ago".Replace(',', '.') : null;
            }
        }

        /// <summary>True the first time a skip of <paramref name="npc"/> is told in its current fight.</summary>
        public bool TellSkip(string npc)
        {
            lock (_n)
            {
                if (!_n.TryGetValue(npc, out var s) || s.ToldFor == s.FightStartMs) return false;
                s.ToldFor = s.FightStartMs;
                _n[npc] = s;
                return true;
            }
        }

        public void Clear() { lock (_n) _n.Clear(); }
    }

    // ================================================================ 3: an attacker always has a body

    /// <summary>3b: a joiner's bind request for one attacker at most this often.</summary>
    public const long BindAskGapMs = 2_000;
    /// <summary>3b: the host keeps a pinned attacker in the NPC stream this long after its last sign (the Lua side holds the same).</summary>
    public const int PinHoldS = 60;

    /// <summary>
    /// 3b, the host: the engine's "[&lt;npc&gt;] Set target: kcd2mp_&lt;N&gt;" -- an NPC or animal turned on a partner's figure (the earliest
    /// line of its attack). False for every other line (the "Model: Set target" twin included).
    /// </summary>
    public static bool TryParseTargetLine(string? line, out string npc, out byte peer)
    {
        npc = ""; peer = 0;
        if (string.IsNullOrEmpty(line) || line[0] != '[') return false;
        const string mid = "] Set target: kcd2mp_";
        int m = line.IndexOf(mid, StringComparison.Ordinal);
        if (m <= 1) return false;
        string name = line[1..m];
        foreach (char c in name) if (!(char.IsAsciiLetterOrDigit(c) || c == '_')) return false;
        int i = m + mid.Length, n = 0, d = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]) && d < 3) { n = n * 10 + (line[i] - '0'); i++; d++; }
        if (d == 0 || n > 255 || (i < line.Length && !char.IsWhiteSpace(line[i]))) return false;
        npc = name; peer = (byte)n;
        return true;
    }

    /// <summary>3b: the payload of an NpcResync asking for one attacker's body ([reason][name]; an older host reads only the reason byte).</summary>
    public static byte[] AttackerResyncPayload(string npc)
    {
        var nb = System.Text.Encoding.UTF8.GetBytes(npc);
        var b = new byte[1 + Math.Min(nb.Length, Protocol.ActionPayloadMaxLen - 1)];
        b[0] = NpcResyncReason.Attacker;
        Array.Copy(nb, 0, b, 1, b.Length - 1);
        return b;
    }

    /// <summary>3b: the attacker named in an NpcResync payload, or null (another reason, no name, not an authored name).</summary>
    public static string? AttackerOf(byte[] payload)
    {
        if (payload.Length < 2 || payload[0] != NpcResyncReason.Attacker) return null;
        string n = System.Text.Encoding.UTF8.GetString(payload, 1, payload.Length - 1);
        foreach (char c in n) if (!(char.IsAsciiLetterOrDigit(c) || c == '_')) return null;
        return n;
    }

    /// <summary>3b: the joiner asks for an attacker's body: none here, the switch on, not asked in the last 2 s.</summary>
    public static bool AskBind(bool haveBody, bool on, long lastAskMs, long nowMs) =>
        on && !haveBody && (lastAskMs == 0 || nowMs - lastAskMs >= BindAskGapMs);

    // ================================================================ 4: a fader with no content

    /// <summary>4b: a joiner's fader that belongs to no scene is ended the game's own way after this long.</summary>
    public const long FaderBoundMs = 3_000;

    /// <summary>The local pause reasons that are a fader's content (a menu, the inventory/trade screens, a running dialogue, a cutscene, a load, sleep/wait/travel).</summary>
    public const byte ContentReasons = Wo138Codec.ReasonMenu | Wo138Codec.ReasonInventory | Wo138Codec.ReasonDialogue | Wo138Codec.ReasonCutscene
                                     | Wo138Codec.ReasonLoad | Wo138Codec.ReasonSkipTime;

    /// <summary>
    /// 4b: what the black shows, or null when it shows nothing (then it is ended): a scene (WO-179's label), a pause reason, a fast travel,
    /// no world (menu/loading), this player's death.
    /// </summary>
    public static string? FaderContent(string sceneLabel, byte pauseReasons, bool travelling, bool notInWorld, bool dead)
    {
        if (sceneLabel != "-") return "scene " + sceneLabel;
        if ((pauseReasons & ContentReasons) != 0) return "paused: " + Wo138Codec.Describe((byte)(pauseReasons & ContentReasons));
        if (travelling) return "fast travel";
        if (notInWorld) return "no world (menu or load)";
        if (dead) return "this player is dead";
        return null;
    }

    /// <summary>
    /// 4a: this player's conversation the engine has started but not run yet (WAITING_FOR_TWINS): its "New dialogue ... souls = 'Ex: Dude"
    /// line opens it; a twin, a command of it or a state past the wait (Wo179Rules.ConversationRunning) runs it; its end closes it.
    /// </summary>
    public sealed class PendingConversation
    {
        public int Id { get; private set; } = -1;
        public long SinceMs { get; private set; }
        public bool Running { get; private set; }

        public void OnLine(string line, long nowMs)
        {
            int id = Wo179Rules.DudeDialogId(line);
            if (id >= 0) { Id = id; SinceMs = Math.Max(nowMs, 1); Running = false; return; }
            if (Id < 0) return;
            if (line.StartsWith("[ID: " + Id.ToString(CultureInfo.InvariantCulture) + "] Dialog end", StringComparison.Ordinal)
                || line.StartsWith("[ID: " + Id.ToString(CultureInfo.InvariantCulture) + "] Dialog interrupted", StringComparison.Ordinal))
            { Id = -1; SinceMs = 0; Running = false; return; }
            if (!Running && Wo179Rules.ConversationRunning(line, Id)) Running = true;
        }

        /// <summary>The waiting conversation's id, or -1.</summary>
        public int Waiting => Id >= 0 && !Running ? Id : -1;
    }

    /// <summary>4b: the holder text of the WO191-FADER line.</summary>
    public static string HolderText(int waitingConversation) =>
        waitingConversation >= 0
            ? $"conversation id={waitingConversation.ToString(CultureInfo.InvariantCulture)} waiting for its NPC (WAITING_FOR_TWINS) -> ended the game's own way (InterruptDialogs)"
            : "nothing known (no scene, no conversation, no pause reason) -> the black lifted (wh_ui_FaderSuspend)";
}
