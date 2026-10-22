// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-203 -- join the conversation (Act I) and every fix from session C (Act II) (docs/WO-203-findings.md).
//
// Two messages ride the WO-123 join channel (same header, same routing, rows in Protocol.JoinWire; "Either": a joiner's goes to
// the host (target 0xFF), the host's to one joiner by id -- the relay needs no code of its own):
//
//   type up/down  name   up body (after [target:1][joinId:4])
//   0x76 / 0x77   Conv   [kind:1][tok:4][text:1..ConvTextMax]     Act I: one conversation, the starter <-> the listener
//   0x78 / 0x79   W203   [kind:1][tok:4][text:1..W203TextMax]     Act II's side channel (defence, engage, worn sets, poses, ...)
//
// The LootMsg shape: printable ASCII text, checked field by field on both ends (StringNames, soul and entity names, voice paths,
// invariant-culture numbers; no localized text on the wire). Every Conv text starts with the conversation id cid.
//
// Conv kinds (APPEND-ONLY), Act I.1:
//   1 Announce  starter -> partner    cid, dialogue id, kind (normal|forced|fader), participant souls, anchor soul, the starter's
//                                     position, joinable -- at WAITING_FOR_POSITIONING, and again when joinability changes
//   2 Join      listener -> starter   cid -- the listener asks to join
//   3 Snapshot  starter -> listener   cid, the lines so far, an open decision, the camera pose, scope fragments, a running timer
//                                     (a snapshot longer than ConvTextMax is split into parts)
//   4 Line      starter -> listener   cid, line id, speaker soul, voice file, StringName, duration, start time (the host's clock)
//   5 Cam       starter -> listener   cid, seq, time, pose relative to the anchor (position, rotation, FOV), cut -- at most 30 Hz
//   6 Anim      starter -> listener   cid, body soul, scope (full|upper), fragment and tags, start time, the twins' positions
//   7 Options   starter -> listener   cid, decision id, rows (sequence id, prompt StringName, marks), timer seconds
//   8 Vote      listener -> starter   cid, decision id, sequence id or "clear"
//   9 Chosen    starter -> listener   cid, decision id, the chosen sequence id
//  10 Skill     starter -> listener   cid, the skill-check result as the HUD shows it
//  11 Leave     listener -> starter   cid
//  12 End       starter -> listener   cid, reason (ended|cancelled|combat|lost)
//  13 Timer     starter -> listener   cid, decision id, the running decision timer (seconds left of its total)
//
// W203 kinds (APPEND-ONLY), Act II:
//   1 DefenceSwing    host -> joiner   II.2: an attack aimed at the joiner's figure -- its hit id, the attacker and its row
//   2 DefenceOutcome  joiner -> host   II.2: hit id, outcome, the health and stamina damage the joiner's own engine applied
//   3 GuardState      joiner -> host   II.2: the joiner's guard / block / parry state as rows (the figure blocks on the host)
//   4 EngageEarly     host -> joiner   II.2: an NPC targets or engages the figure (the copy engages before any swing)
//   5 NpcWorn         host -> joiner   II.8: an NPC's worn set, one item class per equipment slot
//   6 NpcPose         host -> joiner   II.9: an NPC's full-body fragment and tags, plus its posture
//   7 JoinProgress    joiner -> host   II.1: the joiner's load progress (percent)
//   8 FocusEdge       joiner -> host   II.4: a focus / observe trigger's edge
//   9 TravelRoute     host -> joiner   II.12: a fast travel's route and state
//
// II.5 -- the relay passes every message type. RelayUp is THE list of the client -> relay types that are NOT join rows, each with
// the payload lengths the relay takes; JoinWire is the rest. IsKnownUp / IsUpLenOk answer for both (plus the Handshake, the first
// frame only), and ClientSession gates every frame on them right after its header: an unknown type is counted "unknown-type", a
// known one of a wrong length "wrong-length:<len>" -- the old catch-all "unknown-or-wrong-length" could not tell them apart
// (session C's 463 drops of 0x74 were a known type that was too long). RelayDown / IsKnownDown are the agent's mirror of the
// down types. RelayTableGateTests (KcdMp.Relay.Tests, tools\Test-RelayTable.ps1) compares all of it with the protocol's
// constants by reflection and puts every row through the real relay; a gap fails the build.
//
// No protocol bump: two new types on the join channel. A mixed release is refused at the relay (WO-110 R9), so a peer that does
// not know them never shares a session with one that sends them.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte ConvUp = 0x76, ConvDown = 0x77;   // WO-203 Act I
    public const byte W203Up = 0x78, W203Down = 0x79;   // WO-203 Act II

    public const int ConvTextMax = 1400;
    public const int W203TextMax = 1400;

    // ---- Conv kinds (APPEND-ONLY) ----
    public const byte ConvAnnounce = 1, ConvJoin = 2, ConvSnapshot = 3, ConvLine = 4, ConvCam = 5, ConvAnim = 6, ConvOptions = 7,
                      ConvVote = 8, ConvChosen = 9, ConvSkill = 10, ConvLeave = 11, ConvEnd = 12, ConvTimer = 13;

    // ---- WO-203B: the conversation camera's native ops (WO-163 family, wo203.h conv range; native wo203_cam.cpp) ----
    public const byte Wo203OpConvView = 94;      // the listener's view: enter / samples / leave / status / readback
    public const byte Wo203OpConvCamPoll = 95;   // the starter's sampler: arm / disarm / poll / status

    public static string ConvKindName(byte k) => k switch
    {
        ConvAnnounce => "announce", ConvJoin => "join", ConvSnapshot => "snapshot", ConvLine => "line", ConvCam => "cam",
        ConvAnim => "anim", ConvOptions => "options", ConvVote => "vote", ConvChosen => "chosen", ConvSkill => "skill",
        ConvLeave => "leave", ConvEnd => "end", ConvTimer => "timer",
        _ => $"unknown-{k}",
    };

    // ---- W203 kinds (APPEND-ONLY) ----
    public const byte W203DefenceSwing = 1, W203DefenceOutcome = 2, W203GuardState = 3, W203EngageEarly = 4, W203NpcWorn = 5,
                      W203NpcPose = 6, W203JoinProgress = 7, W203FocusEdge = 8, W203TravelRoute = 9;

    public static string W203KindName(byte k) => k switch
    {
        W203DefenceSwing => "defence-swing", W203DefenceOutcome => "defence-outcome", W203GuardState => "guard-state",
        W203EngageEarly => "engage-early", W203NpcWorn => "npc-worn", W203NpcPose => "npc-pose", W203JoinProgress => "join-progress",
        W203FocusEdge => "focus-edge", W203TravelRoute => "travel-route",
        _ => $"unknown-{k}",
    };

    // ---------------------------------------------------------------- II.5: the relay's table

    /// <summary>The largest payload a frame can carry (its length is a u16).</summary>
    public const int FramePayloadMax = ushort.MaxValue;

    /// <summary>The Ping (0x04) payload: the sender's 8-byte stamp, echoed in the Pong.</summary>
    public const int PingPayloadLen = 8;

    /// <summary>The Handshake (0x00) payload minimum: [version:1][nameLen:1] (the name and the release follow).</summary>
    public const int HandshakeMinLen = 2;

    /// <summary>
    /// WO-203 II.5: THE list of the client -> relay (up) types that are not WO-123 join rows, with the payload lengths the relay
    /// accepts (inclusive). The relay's gates in ClientSession apply exactly these; a type whose shape changes changes here.
    /// Position and CombatEvent take only some lengths in their range (IsUpLenOk applies their predicate); the WO-2/WO-5
    /// interaction types take any length from their minimum up (the session layer reads what it needs). Field checks inside a
    /// body (a name length, an item count, a len byte, the authority) are the gates' own and counted by them.
    /// </summary>
    public static readonly (byte Up, string Name, int Min, int Max)[] RelayUp =
    {
        (Position,          "position",          PositionPayloadLen, PositionPayloadLenMax),   // 17, 21, 29, 33 only (IsPositionPayloadLen)
        (Ping,              "ping",              PingPayloadLen, PingPayloadLen),
        (VoiceUp,           "voice",             VoiceFrameLen, VoiceFrameLen),
        // WO-2 interactions + WO-5 dice: [target][kind][config...] / [session:2][accept] / [session:2][event...] / [session:2][reason] / [session:2][intent][data...]
        (Invite,            "invite",            2, FramePayloadMax),
        (InviteResponse,    "invite-response",   3, FramePayloadMax),
        (SessionEventUp,    "session-event",     2, FramePayloadMax),
        (SessionLeave,      "session-leave",     3, FramePayloadMax),
        (DiceIntent,        "dice-intent",       3, FramePayloadMax),
        (DamageUp,          "damage",            DamageUpPayloadLen, DamageUpPayloadLen),
        (DeathUp,           "death",             DeathUpPayloadLen, DeathUpPayloadLen),
        (AppearanceUp,      "appearance",        1, 1 + MaxAppearanceItems * ItemClassLen),     // and exactly 1 + count * 16
        (PauseUp,           "pause",             PauseUpPayloadLen, PauseUpPayloadLen),
        (PlayerStateUp,     "player-state",      PlayerStateUpPayloadLen, PlayerStateUpPayloadLen),
        (PlayerHitUp,       "player-hit",        PlayerHitUpPayloadLen, PlayerHitUpPayloadLen),   // the damage authority only
        (PlayerDeathUp,     "player-death",      PlayerDeathUpPayloadLen, PlayerDeathUpPayloadLen),
        (NpcStateUp,        "npc-state",         1 + 1 + NpcStateFixedTail, 1 + MaxNpcNameLen + NpcStateFixedTail),   // and the nameLen byte agrees
        (TimeSkipUp,        "time-skip",         TimeSkipUpPayloadLen, TimeSkipUpPayloadLen),
        (HorseInfoUp,       "horse-info",        1, 1 + MaxHorseNameLen),                       // and the nameLen byte agrees
        (CombatEventUp,     "combat-event",      CombatEventUpPayloadLen, CombatEventUpPayloadLenV2),   // 1 or 3 only
        (WeatherUp,         "weather",           1 + 2, 1 + MaxWeatherNameLen + 2),             // and the nameLen byte agrees
        (NpcDamageUp,       "npc-damage",        1 + 1 + NpcDamageFixedTail, 1 + MaxNpcNameLen + NpcDamageFixedTail),   // and the nameLen byte agrees
        (ItemDropUp,        "item-drop",         ItemDropUpPayloadLen, ItemDropUpPayloadLen),
        (ItemClaimUp,       "item-claim",        ItemClaimUpPayloadLen, ItemClaimUpPayloadLen),
        (StoryBeatUp,       "story-beat",        2, 2 + MaxStoryBeatTextLen),                   // and the len byte agrees
        (ClockSyncUp,       "clock-sync",        ClockSyncUpPayloadLen, ClockSyncUpPayloadLen),
        (ActionUp,          "action",            ActionUpHeaderLen, ActionUpHeaderLen + ActionPayloadMaxLen),   // and the len byte agrees
        (PlayerRespawnedUp, "player-respawned",  PlayerRespawnedUpPayloadLen, PlayerRespawnedUpPayloadLen),
        (GraveAddUp,        "grave-add",         GraveAddUpPayloadLen, GraveAddUpPayloadLen),
        (GraveRemoveUp,     "grave-remove",      GraveRemoveUpPayloadLen, GraveRemoveUpPayloadLen),
        (PlayerHitV8Up,     "player-hit-v8",     PlayerHitV8.UpLen, PlayerHitV8.UpLen),
        (WorldSavedUp,      "world-saved",       WorldSavedUpPayloadLen, WorldSavedUpPayloadLen),   // the damage authority only
    };

    /// <summary>The RelayUp row for an up type, or null (a join row, the Handshake, or not an up type).</summary>
    public static (byte Up, string Name, int Min, int Max)? RelayUpFor(int upType)
    {
        foreach (var r in RelayUp) if (r.Up == upType) return r;
        return null;
    }

    /// <summary>
    /// WO-203 II.5: the relay -> client (down) types that are not WO-123 join rows (the join rows' downs are JoinWire's). The
    /// agent's catch-all reads it to tell an unknown type from a known one of a wrong length.
    /// </summary>
    public static readonly byte[] RelayDown =
    {
        Ghost, Name, Pong, Disconnect, VoiceDown, VersionMismatch, InviteReceived, SessionStart, SessionEventDown, SessionEnd,
        DamageDown, DeathDown, DiceState, DiceError, DiceEnd, AppearanceDown, PauseDown, ReleaseVersion, PlayerStateDown,
        PlayerHitDown, PlayerDeathDown, CombatRole, NpcStateDown, TimeSkipDown, HorseInfoDown, CombatEventDown, WeatherDown,
        NpcDamageDown, ItemDropDown, ItemClaimDown, ServerFull, StoryBeatDown, ClockSyncDown, ActionDown, ReleaseVersionMismatch,
        PlayerRespawnedDown, GraveAddDown, GraveRemoveDown, PlayerHitV8Down, WorldSavedDown, Ack,
    };

    // One lookup per type byte, built on first use (not in a field initializer: JoinWire lives in another part of this partial
    // class, and the order static initializers of different parts run in is unspecified).
    private readonly record struct UpRule(bool Known, int Min, int Max, bool Join);
    private static readonly Lazy<UpRule[]> s_upRules = new(BuildUpRules);
    private static readonly Lazy<bool[]> s_knownDown = new(BuildKnownDown);

    private static UpRule[] BuildUpRules()
    {
        var t = new UpRule[256];
        t[Handshake] = new UpRule(true, HandshakeMinLen, FramePayloadMax, false);
        foreach (var r in RelayUp) t[r.Up] = new UpRule(true, r.Min, r.Max, false);
        foreach (var r in JoinWire) t[r.Up] = new UpRule(true, r.Min, r.Max, true);
        return t;
    }

    private static bool[] BuildKnownDown()
    {
        var t = new bool[256];
        foreach (byte d in RelayDown) t[d] = true;
        foreach (var r in JoinWire) t[r.Down] = true;
        return t;
    }

    /// <summary>WO-203 II.5: a type a client may send the relay (a RelayUp or JoinWire row, or the Handshake).</summary>
    public static bool IsKnownUp(int type) => type is >= 0 and <= 0xFF && s_upRules.Value[type].Known;

    /// <summary>
    /// WO-203 II.5: a payload length the relay takes for that up type -- the row's range, and for Position and CombatEvent only
    /// their listed lengths. False for an unknown type. The Handshake: at least [version][nameLen] (the relay reads it first
    /// and only once; a second one in the stream is dropped as "handshake-again").
    /// </summary>
    public static bool IsUpLenOk(int type, int len)
    {
        if (!IsKnownUp(type) || len < 0) return false;
        var r = s_upRules.Value[type];
        if (len < r.Min || len > r.Max) return false;
        return type switch
        {
            Position => IsPositionPayloadLen(len),
            CombatEventUp => len is CombatEventUpPayloadLen or CombatEventUpPayloadLenV2,
            _ => true,
        };
    }

    /// <summary>WO-203 II.5: a type the relay sends a client (a RelayDown type or a JoinWire row's down).</summary>
    public static bool IsKnownDown(int type) => type is >= 0 and <= 0xFF && s_knownDown.Value[type];

    /// <summary>
    /// WO-203 II.5: the up types whose body carries a kind byte the relay names in a drop key ("0x74/k21:..."): the LootMsg-shaped
    /// side channels many features share, where the type alone does not say whose message it was.
    /// </summary>
    public static bool DropKeyHasKind(int upType) => upType is W164Up or ConvUp or W203Up;

    /// <summary>The kind byte of such a body ([target][joinId:4][kind]...), or -1 when the body is too short to hold one.</summary>
    public static int JoinBodyKind(ReadOnlySpan<byte> upBody) => upBody.Length > JoinHeaderLen ? upBody[JoinHeaderLen] : -1;
}
