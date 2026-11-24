// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>WO-222's pure rules (docs/WO-222-findings.md).</summary>
public static class Wo222Rules
{
    // ---------------------------------------------------------------- C: the host's wait for the joiner's own verdict

    /// <summary>With fewer answers measured than this, the wait is <see cref="DefHoldUnknownMs"/>.</summary>
    public const int DefMinSamples = 5;
    public const int DefHoldUnknownMs = 1000;
    /// <summary>Never shorter than this (the bench at the field's link: answers up to 600 ms, a fifth more sent just before the host's
    /// 600 ms wait ran out); never longer than the cap.</summary>
    public const int DefHoldFloorMs = 800, DefHoldCapMs = 1500;
    /// <summary>The margin over the slowest answer seen: a frame of his and the link's spread.</summary>
    public const int DefMarginMs = 250;
    /// <summary>An answer counts for the sample when it came at most this long after its blow (two of the field's stalls back to back are
    /// 1.8 s). A wait that went by the joiner's link going quiet was tried and dropped: his position stream is quiet whenever he stands.</summary>
    public const int DefStallCapMs = 3000;

    /// <summary>
    /// How long the host waits for the joiner's own verdict: the slowest of his last answers (arrival minus the host's blow: his copy's
    /// swing lagging behind the host's NPC, the link both ways with its stalls, his frames -- answers that came after a wait ran out are
    /// in the sample too) plus a margin, between 800 ms and 2 s; before five answers are known, 1 s. <paramref name="comboMs"/> (WO-210
    /// E.3's combo hold) is a floor when the blow continues a combo.
    /// </summary>
    public static int DefHoldMs(IReadOnlyList<int> delaysMs, int comboMs = 0, int capMs = DefHoldCapMs)
    {
        int h = delaysMs.Count < DefMinSamples ? DefHoldUnknownMs : Math.Clamp(delaysMs.Max() + DefMarginMs, DefHoldFloorMs, Math.Max(DefHoldFloorMs, capMs));
        return Math.Max(h, comboMs);
    }

    /// <summary>A delay worth learning from: answers to the blow they answer, never a stale or early one.</summary>
    public static bool DelaySample(long arrivedMs, long blowMs) => arrivedMs >= blowMs && arrivedMs - blowMs <= DefStallCapMs;

    /// <summary>A bounded ring of the last answers' delays (ms) for one joiner.</summary>
    public sealed class DelayRing
    {
        private readonly int[] _v;
        private int _n, _at;
        public DelayRing(int size = 40) { _v = new int[size]; }
        public void Add(int ms) { lock (_v) { _v[_at] = ms; _at = (_at + 1) % _v.Length; if (_n < _v.Length) _n++; } }
        public int[] Snapshot() { lock (_v) { var r = new int[_n]; for (int i = 0; i < _n; i++) r[i] = _v[i]; return r; } }
    }

    // ---------------------------------------------------------------- D: a fleeing enemy

    /// <summary>A touch of the joiner's blade on a copy held as a fleeing enemy goes to the host when the copy is at most this far from its
    /// host NPC's newest streamed place (a runner's copy trails it; WO-131's 3 m is for a body that stands or walks).</summary>
    public const float FleeHitMaxDistM = 8.0f;

    /// <summary>The host keeps watching a body this long after it was last watched for its fight (a runner holsters its weapon).</summary>
    public const int FleeWatchKeepMs = 120_000;
    public static bool KeepWatching(long lastFightWatchMs, long nowMs) => nowMs - lastFightWatchMs >= 0 && nowMs - lastFightWatchMs <= FleeWatchKeepMs;

    // ---------------------------------------------------------------- C: the joiner gives back a guess its own engine overruled

    /// <summary>A host verdict this joiner applied (the host's guess: its wait ran out).</summary>
    public sealed record Applied(string Attacker, uint HitId, float Hp, float St, long AtMs, byte HostVerdict) { public bool Settled { get; set; } }

    /// <summary>The joiner's own engine judged a blow of <paramref name="attacker"/> this long after a host verdict for it was applied:
    /// the same blow (one blow, one judge) -- the verdict's damage is given back. The copy's swing lands here at most a stall after the
    /// host's.</summary>
    public const int CorrectWithinMs = 3000;

    /// <summary>The oldest unsettled applied verdict of <paramref name="attacker"/> that the joiner's own judgement at <paramref name="nowMs"/>
    /// overrules, or null.</summary>
    public static Applied? Overruled(IEnumerable<Applied> applied, string attacker, long nowMs) =>
        applied.Where(a => !a.Settled && a.Attacker == attacker && nowMs - a.AtMs >= 0 && nowMs - a.AtMs <= CorrectWithinMs)
               .OrderBy(a => a.AtMs).FirstOrDefault();

    /// <summary>Did the joiner's own outcome contradict the host's guess (a different verdict: a block, a parry, a miss against a hit, or
    /// the reverse)? A hit for a hit only doubled the damage -- given back all the same, counted apart.</summary>
    public static bool Contradicts(byte ownOutcome, HitVerdict hostSaid) => ownOutcome switch
    {
        Wo208Rules.DefHit => hostSaid != HitVerdict.Hit,
        Wo208Rules.DefBlocked => hostSaid != HitVerdict.Blocked,
        Wo208Rules.DefBroken => hostSaid != HitVerdict.Broken,
        _ => true,   // a perfect block, a miss, "none"
    };
}
