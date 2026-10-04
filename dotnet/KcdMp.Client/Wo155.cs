// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-155: hits never knock a player down; a player's figure falls only when he dies.
///
/// 0.45.0 applied every NPC blow on the joiner as the game's scripted hit (TakeDamage with two arguments), which
/// knocks a player down whenever he is not in a combat stance: the joiner went down 10 times in 13 minutes, 3-4 s
/// each, and the host mirrored each one (a ragdoll and Revive: the figure sank and stood up in a T-pose). Now
/// an NPC's or an animal's blow goes in through the four-argument call with SuppressHitReaction (health and stamina
/// exact, no fall); a player's figure falls on the other screen only for his death, and for a knockdown a friendly
/// fire hit of the watcher's own caused (the game's own Fall and get-up, never a ragdoll and Revive).
/// </summary>
public static class Wo155Rules
{
    /// <summary>A knockdown the victim's DLL reports this soon after OUR friendly-fire hit on him is the hit's own
    /// (hit -> his debounce 0.15 s + the state block + the relay: well under a second; 3 s is generous).</summary>
    public const long FriendlyKnockWindowMs = 3000;

    /// <summary>Whether a partner's down edge at <paramref name="nowMs"/> is the knockdown of a friendly-fire hit this
    /// player sent him at <paramref name="sentAtMs"/> (0 = none sent).</summary>
    public static bool FriendlyKnock(long sentAtMs, long nowMs) =>
        sentAtMs > 0 && nowMs >= sentAtMs && nowMs - sentAtMs <= FriendlyKnockWindowMs;

    /// <summary>The pipe bit that tells the DLL no friendly-fire hit may knock the victim down (mp_ff_knockdown off).</summary>
    public const byte PvpNoKnockdownBit = 0x80;

    /// <summary>The flags byte of an ApplyPvpHit: the wire's own flags, plus the pipe bit when knockdowns are off.</summary>
    public static byte PvpFlags(byte wireFlags, bool ffKnockdown) =>
        (byte)((wireFlags & ~PvpNoKnockdownBit) | (ffKnockdown ? 0 : PvpNoKnockdownBit));

    /// <summary>What a partner's vitals flags say about his body: a death (down, not a knockout or the death guard's
    /// knockdown), a knockout/knockdown (down and knocked down), or up.</summary>
    public enum Body { Up, Death, Knocked }

    public static Body BodyOf(bool down, bool knockedDown) => !down ? Body.Up : knockedDown ? Body.Knocked : Body.Death;

    /// <summary>A "he is up" sample after a death: his respawn when it follows a death in his vitals, or when the
    /// figure has lain at least <see cref="MinDeadMs"/> (the death packet may come before his vitals say down, and a stale
    /// "alive" heartbeat in that gap is no respawn; a real one takes the 6 s black hold at least).</summary>
    public static bool RespawnSeen(bool vitalsHadDeath, long collapsedAtMs, long nowMs) =>
        vitalsHadDeath || nowMs - collapsedAtMs >= MinDeadMs;

    public const long MinDeadMs = 10_000;

    /// <summary>How long after a partner's respawn the dead figure is replaced by a fresh one (the new position has
    /// streamed in by then; WO-132's own 1.5 s).</summary>
    public const int RespawnReplaceMs = 1500;
}
