// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>WO-208 (docs/WO-208-findings.md): the agent's pure rules -- the switches and the DLL's switch word.</summary>
public static class Wo208Rules
{
    /// <summary>Every WO-208 switch the agent knows (the mod's mp_* commands send "name=on|off" as w208_cfg). New mechanisms ship on.</summary>
    public static readonly string[] SwitchNames = ["conv_cam_player_view", "target_in_fight", "defence_local", "copy_anim_owner", "listen_show_head", "joiner_player_triggers", "quest_wait_aside", "defence_recoil", "clock_stands_with_host", "listen_own_talk_first", "conv_autojoin"];

    /// <summary>The switches that start off (the owner until its acceptance passes on the pair); the bench's KCDMP_TEST_W208_ON names one on.</summary>
    public static readonly string[] DefaultOff = ["copy_anim_owner"];

    /// <summary>A switch the mod has not set: on, unless it starts off (DefaultOff) and the bench's on-list does not name it, or a twin names it off.</summary>
    public static bool Default(string name, string? twinOff, string? benchOn) =>
        !Wo207Rules.TwinOff(twinOff, name) && (!DefaultOff.Contains(name, StringComparer.Ordinal) || Wo207Rules.TwinOff(benchOn, name));

    /// <summary>The DLL's switch word (op 130), the same bits as wo208_rules.h kBit*.</summary>
    public static uint ConfigBits(Func<string, bool> on) => (on("conv_cam_player_view") ? 1u : 0u) | (on("target_in_fight") ? 2u : 0u) | (on("defence_local") ? 4u : 0u)
                                                             | (on("copy_anim_owner") ? 8u : 0u);

    // ---------------------------------------------------------------- C.4 conversations that start by themselves

    /// <summary>A conversation the player did not start: a quest forcing it (ForceDialogNode) or an NPC opening a full dialogue with the
    /// player (AI::RequestDialog). The engine's own origin, from its "New dialogue" line; the starter's announce carries it.</summary>
    public static bool StartsByItself(string origin) => origin is "ForceDialogNode" or "AI::RequestDialog";

    /// <summary>The partner within this many metres of the starter listens in to such a conversation by itself (the mod's side checks
    /// the distance and the partner's own state: no talk of his own, no fight, not listening already).</summary>
    public const double AutoJoinRangeM = 30.0;

    // ---------------------------------------------------------------- B the joiner's own defence

    /// <summary>The outcome his engine gave a copy's blow (the native numbers, wo208_rules.h kDef*).</summary>
    public const byte DefHit = 1, DefBlocked = 2, DefPerfect = 3, DefBroken = 4, DefMissed = 5;
    /// <summary>The joiner's window closed with nothing landed and no dodge: no judgement there, told so the host applies its own verdict at
    /// once instead of holding it to the cap (the bench: every blow but about one a fight waited the full 450 ms for nothing).</summary>
    public const byte DefNone = 6;
    public static string DefName(byte o) => o switch
    {
        DefHit => "hit", DefBlocked => "blocked", DefPerfect => "perfect-block", DefBroken => "guard-broken", DefMissed => "missed", DefNone => "none", _ => "?",
    };

    /// <summary>An outcome the joiner's engine judged (the host sends no verdict; the joiner applies none on that attacker for a moment).
    /// DefNone is not one: the host's verdict goes, at once.</summary>
    public static bool IsJudgement(byte o) => o is >= DefHit and <= DefMissed;

    /// <summary>The host waits for the joiner's outcome at most this long after its own blow landed on the figure (the swing's window
    /// plus one round trip, capped: the WO's 450 ms).</summary>
    public const int HoldCapMs = 450;
    /// <summary>An outcome counts for a host blow of the same attacker that landed up to this long after it arrived (the joiner's copy
    /// swings ~30 ms after the host's NPC: its blow is judged there a little later than here, or earlier when the host's frame was late).</summary>
    public const int OutcomeEarlyMs = 900;
    /// <summary>The joiner applies no host verdict for an attacker whose blow its own engine judged within this long (one blow, one judge).</summary>
    public const int JudgedHereMs = 1500;

    public static int HoldMs(int windowMs, int rttMs) => Math.Clamp(windowMs + Math.Max(0, rttMs), 0, HoldCapMs);

    /// <summary>"v1|&lt;npc&gt;|&lt;outcome&gt;|&lt;hp&gt;|&lt;st&gt;|&lt;seq&gt;" -- the W203 DefenceOutcome text.</summary>
    public static string DefenceText(string npc, byte outcome, float hp, float st, uint seq) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"v1|{npc}|{outcome}|{hp:F2}|{st:F2}|{seq}");

    public static bool TryParseDefence(string? text, out string npc, out byte outcome, out float hp, out float st, out uint seq)
    {
        npc = ""; outcome = 0; hp = 0; st = 0; seq = 0;
        var p = (text ?? "").Split('|');
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (p.Length != 6 || p[0] != "v1" || p[1].Length == 0 || p[1].Length > 64 || !p[1].All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return false;
        if (!byte.TryParse(p[2], out outcome) || outcome < DefHit || outcome > DefNone) return false;
        if (!float.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out hp) || !float.TryParse(p[4], System.Globalization.NumberStyles.Float, ci, out st)) return false;
        if (!uint.TryParse(p[5], out seq) || hp < 0 || st < 0 || hp > 1000 || st > 1000) return false;
        npc = p[1];
        return true;
    }

    /// <summary>The host: does an outcome that arrived at <paramref name="arrivedMs"/> answer a blow that landed at <paramref name="blowMs"/>?</summary>
    public static bool Answers(long arrivedMs, long blowMs, int holdMs) => arrivedMs >= blowMs - OutcomeEarlyMs && arrivedMs <= blowMs + holdMs;

    /// <summary>The joiner: skip the host's verdict on this attacker -- its blow was judged here a moment ago.</summary>
    public static bool JudgedHere(long judgedMs, long nowMs, bool on) => on && judgedMs > 0 && nowMs - judgedMs <= JudgedHereMs;

    /// <summary>The attack zone of a stab (the game's combat_action_failed_attack rows are all attack zone 5: a blocked slash runs on).</summary>
    public const byte StabZone = 5;

    /// <summary>
    /// B.4 (switch defence_recoil): the host's NPC plays the game's own answer to a blocked or perfectly blocked stab -- its failed-attack
    /// recoil -- when the joiner's engine blocked it and the host's own engine had it as a hit (a host block ran that recoil itself).
    /// </summary>
    public static bool HostRecoils(bool on, byte outcome, byte zone, HitVerdict hostSaid) =>
        on && (outcome is DefBlocked or DefPerfect) && zone == StabZone && hostSaid == HitVerdict.Hit;

    // ---------------------------------------------------------------- A.2 the host's NPCs as state

    /// <summary>
    /// The joiner: the host's NpcAnim payload ([count] then [nameLen][name][cm][nsc] nsc x 34) re-keyed by this world's copies -- the DLL's
    /// op 133 arguments ([n] then [eid:4][cm][nsc] nsc x 34), the bodies with no copy here left out (counted). False = malformed.
    /// </summary>
    public static bool TryRekeyNpcAnim(ReadOnlySpan<byte> body, Func<string, uint> eidOf, out byte[] op, out int known, out int unknown)
    {
        op = []; known = 0; unknown = 0;
        if (body.Length < 1) return false;
        int count = body[0], o = 1;
        var ms = new MemoryStream();
        ms.WriteByte(0);
        for (int i = 0; i < count; i++)
        {
            if (o + 1 > body.Length) return false;
            int nl = body[o];
            if (nl == 0 || nl > 64 || o + 1 + nl + 2 > body.Length) return false;
            var nm = body.Slice(o + 1, nl);
            foreach (byte c in nm) if (!(c is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9' or (byte)'_')) return false;
            string name = System.Text.Encoding.ASCII.GetString(nm);
            o += 1 + nl;
            byte cm = body[o], nsc = body[o + 1];
            o += 2;
            if (nsc > 2 || o + nsc * Wire.Protocol.NpcAnimScopeLen > body.Length) return false;
            uint eid = eidOf(name);
            if (eid != 0 && known < 255)
            {
                Span<byte> e = stackalloc byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(e, eid);
                ms.Write(e); ms.WriteByte(cm); ms.WriteByte(nsc);
                ms.Write(body.Slice(o, nsc * Wire.Protocol.NpcAnimScopeLen));
                known++;
            }
            else unknown++;
            o += nsc * Wire.Protocol.NpcAnimScopeLen;
        }
        if (o != body.Length) return false;
        op = ms.ToArray();
        op[0] = (byte)known;
        return true;
    }

    // ---------------------------------------------------------------- D.2 the clock

    /// <summary>
    /// D.2 (switch clock_stands_with_host): the joiner's gentle clock correction (WO-200) may set a ratio -- not while this clock stands
    /// with the host's (WO-144 set it to 0 for the host's dialogue): a correction then would run it on. Off (the twin): it runs on.
    /// </summary>
    public static bool ClockSlewAllowed(bool standingWithHost, bool on) => !(on && standingWithHost);

    // ---------------------------------------------------------------- D.2 the joiner's quest queue

    /// <summary>One host quest change on this copy: applied (or dropped for good), its node asleep here, or no answer from the DLL.</summary>
    public enum QuestStep { Done, NodeWaits, NoAnswer }

    /// <summary>A change whose node sleeps here is tried again this often (aside, it no longer holds the queue: once a second is enough).</summary>
    public const int QuestAsideEveryMs = 1000;
    /// <summary>The tries a sleeping node's change gets: 600 ticks of 50 ms in the queue's head (the old 30 s), 30 aside (the same 30 s).</summary>
    public static int QuestAsleepTries(bool aside) => aside ? 30_000 / QuestAsideEveryMs : 600;

    /// <summary>
    /// D.2 (switch quest_wait_aside): one drain tick of the joiner's quest queue. On: a change whose node sleeps here waits aside and
    /// the host's later changes apply in the host's order past it -- a later change of the same node stays behind it. Off (the twin):
    /// the first change that waits holds every later one (the bench's tavern run: 30 s per sleeping node; one host change applied 2 min 8 s after it came).
    /// No answer from the DLL stops the tick either way (the next tick tries again). Returns the changes applied or dropped.
    /// </summary>
    public static async Task<int> DrainQuestTickAsync(QuestApplyQueue q, Func<QuestChange, Task<QuestStep>> apply, Func<bool> mayGoOn, bool waitAside, int budget)
    {
        HashSet<string>? aside = waitAside ? new(StringComparer.Ordinal) : null;
        int done = 0;
        while (budget-- > 0 && mayGoOn() && q.TryPeekPast(aside, out var c))
        {
            var r = await apply(c);
            if (r == QuestStep.Done) { q.Remove(c); done++; continue; }   // exactly this one: a correction put in front meanwhile stays
            if (r == QuestStep.NodeWaits && aside is not null) { aside.Add(c.Path); continue; }
            break;
        }
        return done;
    }

    /// <summary>"name=on|off" from the mod: false when malformed or not one of <see cref="SwitchNames"/>.</summary>
    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        int eq = (arg ?? "").IndexOf('=');
        if (eq <= 0) return false;
        name = arg![..eq];
        string v = arg[(eq + 1)..];
        if (v is not ("on" or "off") || !SwitchNames.Contains(name, StringComparer.Ordinal)) return false;
        on = v == "on";
        return true;
    }
}
