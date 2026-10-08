// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-161: the hit verdict (docs/WO-161-findings.md). The pure rules; the bridge half is GameBridge.Wo161.cs.
///
/// A joiner's copy of an enemy is a puppet: its swing fires no hit event on the joiner (161 of 161 field windows), so the
/// victim's own engine cannot decide the hit and the host's engine still does, against the avatar's replayed guard. What
/// this adds is the bookkeeping the hit never had: a swing id (the host's ledger of the rows it sent), a verdict (hit or
/// blocked) and an exactly-once hit id on the wire, and on the victim a check of every damage line against the swing it
/// was shown with -- or the logged reason it had none. Damage is never dropped for lacking a swing.
/// </summary>
public static class Wo161Rules
{
    /// <summary>Host: for a swing whose row states no lag of its own (the catalog knows the row but its table has no timings),
    /// the 0.46.5 rule -- a swing committed this soon before the hit is the swing the hit belongs to. A row the catalog does
    /// not know at all is never paired by it (WO-162 Q3.2: the window had matched an unreadable swing).</summary>
    public const long SwingWindowMs = 1200;

    /// <summary>WO-163 (A3): a hit belongs to the swing whose own <c>attack_time_to_start + attack_time_to_hit</c> it matches, +-this
    /// (WO-162 Q3.2: 33 of the 35 field hits with a catalogued row within 2 s fit it; the fit's median error 0.16 s).</summary>
    public const long PairToleranceMs = 350;

    /// <summary>WO-163 (A3): swings older than this are never candidates (the longest start+hit seen is a sync attack's 3.2 s).</summary>
    public const long LookbackMs = 4000;

    /// <summary>Victim: a played row fits a verdict when the time between them is its lag +- <see cref="PairToleranceMs"/> plus this
    /// (two messages on one ordered stream, one native call a frame after the row arrived).</summary>
    public const long VictimSkewMs = 250;

    /// <summary>The reasons the host and the victim name. no-swing-captured = no row of this NPC at all; swing-unmatched = rows
    /// exist but none fits this blow's timing (a chain follow-up, a blow the row's table does not time).</summary>
    public const string ReasonNone = "no-swing-captured", ReasonUnmatched = "swing-unmatched";

    /// <summary>A swing whose row the catalog does not know (lag unknown, never paired); and one the catalog knows with no lag.</summary>
    public const int LagUnknownRow = -2, LagNone = -1;

    /// <summary>Victim (A4): the reasons that mean "damage with nothing shown" and so get a generic lunge on the copy. A named
    /// attacker only -- not a missile, not a bare legacy hit, not a row the copy refused to play.</summary>
    public static bool WantsGenericSwing(bool shown, string reason) =>
        !shown && (reason == ReasonNone || reason == ReasonUnmatched || reason == "row-not-received" || reason == "row-stale");

    /// <summary>Victim: a swing row played on the copy this soon before the verdict is "the swing shown with it".</summary>
    public const long RowBeforeMs = 1500;

    /// <summary>Victim: or this soon after (the two paths are two frames on one ordered stream, but a row may be played
    /// by the copy's native call a frame after its verdict is applied).</summary>
    public const long RowAfterMs = 500;

    /// <summary>Victim: a hit id seen this recently is a duplicate and is never applied twice.</summary>
    public const long DupWindowMs = 30_000;

    /// <summary>A guard's absorbed blow measured at the host: health lost below this is "none" (the field: -0.0).</summary>
    public const float NoHealthLoss = 0.05f;
    /// <summary>... and the stamina it cost must be at least this for the blow to read as blocked (not as nothing).</summary>
    public const float BlockedMinStamina = 0.5f;

    /// <summary>The verdict of a hit measured at the host: no health lost but stamina paid = Blocked, anything else = Hit
    /// (damage is damage: a hit is never classed away). Parried and Missed are the victim's own verdicts, reserved: the
    /// victim cannot hit-test a puppet.</summary>
    public static HitVerdict Classify(float health, float stamina) =>
        health < NoHealthLoss && stamina >= BlockedMinStamina ? HitVerdict.Blocked : HitVerdict.Hit;

    /// <summary>Victim: whether the swing was shown with this hit, and the reason it was not.</summary>
    /// <param name="swingKnown">the host's flag: its ledger had a swing of this NPC just before the hit</param>
    /// <param name="dispatchedAgoMs">ms from a row played on the copy (OK) to the verdict's arrival; null = none; negative = after</param>
    /// <param name="receivedAgoMs">ms from a row of this NPC received (played or not) to the verdict; null = none</param>
    /// <param name="refusedTag">the dispatcher's reason when a received row was not played</param>
    /// <param name="dispatchedFits">WO-163 (A3): the played row is the one whose own start+hit lag the time since it fits -- it is
    /// shown however long that lag is (a sync attack's 3.2 s), where the 0.46.5 bound would call it stale</param>
    public static (bool Shown, string Reason) Judge(bool swingKnown, bool missile, bool noAttacker,
                                                    long? dispatchedAgoMs, long? receivedAgoMs, string? refusedTag, bool dispatchedFits = false)
    {
        if (dispatchedAgoMs is long d && (dispatchedFits || (d >= -RowAfterMs && d <= RowBeforeMs))) return (true, "-");
        if (noAttacker) return (false, "no-attacker");
        if (missile) return (false, "missile");
        if (receivedAgoMs is long r && r >= -RowAfterMs && r <= RowBeforeMs)
            return (false, string.IsNullOrEmpty(refusedTag) ? "row-not-played" : "row-refused-" + refusedTag);
        if (receivedAgoMs is not null || dispatchedAgoMs is not null) return (false, "row-stale");
        return (false, swingKnown ? "row-not-received" : "no-swing-captured");
    }

    /// <summary>WO-163 (A3), victim: of the rows played on a copy, the ms since the one this verdict belongs to -- the one whose own
    /// lag the time since it fits (best fit wins), else the newest (the 0.46.5 view, which <see cref="Judge"/> still bounds).</summary>
    public static (long Ago, bool Fits)? BestPlayedAgo(IReadOnlyList<(long Ago, int LagMs)> played)
    {
        long? fit = null; long fitErr = long.MaxValue, newest = long.MaxValue;
        foreach (var (ago, lag) in played)
        {
            if (ago < newest) newest = ago;
            if (lag < 0) continue;
            long err = Math.Abs(ago - lag);
            if (err <= PairToleranceMs + VictimSkewMs && err < fitErr) { fit = ago; fitErr = err; }
        }
        if (fit is long f) return (f, true);
        return newest == long.MaxValue ? null : (newest, false);
    }

    /// <summary>Host: the verdict's flags byte.</summary>
    public static byte Flags(bool swingKnown, bool missile, bool noAttacker) =>
        (byte)((swingKnown ? Protocol.HitFlagSwingKnown : 0) | (missile ? Protocol.HitFlagMissile : 0) | (noAttacker ? Protocol.HitFlagNoAttacker : 0));
}

/// <summary>WO-161, host: the swings it sent for its NPCs (the rows of ActionKind.NpcAttack), per NPC, with an id each.</summary>
public sealed class Wo161SwingLedger
{
    /// <summary><paramref name="LagMs"/>: the row's own start+hit lag; <see cref="Wo161Rules.LagNone"/> / <see cref="Wo161Rules.LagUnknownRow"/> when it has none.</summary>
    public readonly record struct Swing(uint Id, Guid Row, long AtMs, int LagMs = Wo161Rules.LagNone);

    /// <summary>A hit's pairing: the swing it belongs to (null = none) and, when none, why.</summary>
    public readonly record struct Pairing(Swing? Swing, string Why, long ErrMs);

    private const int PerNpc = 8, MaxNpcs = 256;
    private readonly Dictionary<string, List<Swing>> _by = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private uint _next;

    /// <summary>A swing the host sent (its id, from 1). <paramref name="lagMs"/> comes from the row's own timings.</summary>
    public uint Record(string npc, Guid row, long nowMs, int lagMs = Wo161Rules.LagNone)
    {
        lock (_gate)
        {
            if (++_next == 0) _next = 1;
            if (!_by.TryGetValue(npc, out var l))
            {
                if (_by.Count >= MaxNpcs) Prune(nowMs);
                if (_by.Count >= MaxNpcs) _by.Clear();   // a bound, never a leak
                _by[npc] = l = new List<Swing>(PerNpc);
            }
            if (l.Count >= PerNpc) l.RemoveAt(0);
            l.Add(new Swing(_next, row, nowMs, lagMs));
            return _next;
        }
    }

    /// <summary>
    /// WO-163 (A3): the swing of <paramref name="npc"/> a hit at <paramref name="nowMs"/> belongs to. Not "the newest inside a
    /// window": the swing whose own lag (<c>attack_time_to_start + attack_time_to_hit</c>) the hit's age matches within
    /// <see cref="Wo161Rules.PairToleranceMs"/>, the best fit winning; a swing whose row states no lag falls back to
    /// <see cref="Wo161Rules.SwingWindowMs"/>; a row the catalog does not know is never paired. No match: <c>no-swing-captured</c> when the
    /// NPC has no swing in <see cref="Wo161Rules.LookbackMs"/>, else <c>swing-unmatched</c>.
    /// </summary>
    public Pairing Match(string npc, long nowMs)
    {
        lock (_gate)
        {
            if (!_by.TryGetValue(npc, out var l)) return new Pairing(null, Wo161Rules.ReasonNone, 0);
            Swing? best = null; long bestErr = long.MaxValue; bool any = false;
            foreach (var s in l)
            {
                long age = nowMs - s.AtMs;
                if (age < 0 || age > Wo161Rules.LookbackMs) continue;   // a clock that ran backwards matches nothing
                any = true;
                long err;
                if (s.LagMs >= 0) { err = Math.Abs(age - s.LagMs); if (err > Wo161Rules.PairToleranceMs) continue; }
                else if (s.LagMs == Wo161Rules.LagNone) { if (age > Wo161Rules.SwingWindowMs) continue; err = age; }
                else continue;                                           // an unreadable row: it cannot be matched
                if (err < bestErr || (err == bestErr && best is { } b && s.AtMs > b.AtMs)) { best = s; bestErr = err; }
            }
            return best is { } hit ? new Pairing(hit, "-", bestErr) : new Pairing(null, any ? Wo161Rules.ReasonUnmatched : Wo161Rules.ReasonNone, 0);
        }
    }

    public int Npcs { get { lock (_gate) return _by.Count; } }

    public void Clear() { lock (_gate) _by.Clear(); }

    private void Prune(long nowMs)
    {
        foreach (var k in _by.Where(kv => kv.Value.Count == 0 || nowMs - kv.Value[^1].AtMs > 60_000).Select(kv => kv.Key).ToArray()) _by.Remove(k);
    }
}

/// <summary>WO-163 (A3), victim: the rows played on one NPC's copy (when, and the row's own lag), so a verdict can be judged against the row it belongs to.</summary>
public sealed class Wo161PlayedLog
{
    private const int Keep = 8;
    private readonly List<(long At, int LagMs)> _rows = new(Keep);
    private readonly object _gate = new();

    public void Note(long atMs, int lagMs)
    {
        lock (_gate)
        {
            if (_rows.Count >= Keep) _rows.RemoveAt(0);
            _rows.Add((atMs, lagMs));
        }
    }

    /// <summary>The ms since the played row this verdict belongs to, and whether its own lag fits (see <see cref="Wo161Rules.BestPlayedAgo"/>); null = none played.</summary>
    public (long Ago, bool Fits)? Best(long nowMs)
    {
        lock (_gate) return Wo161Rules.BestPlayedAgo(_rows.Select(r => (nowMs - r.At, r.LagMs)).ToList());
    }
}

/// <summary>WO-161, host: the numbering of its hits on avatars (from 1, per session; never 0).</summary>
public sealed class Wo161HitIds
{
    private long _n;

    public Wo161HitIds(long start = 0) { _n = start; }
    public uint Next()
    {
        uint v = unchecked((uint)Interlocked.Increment(ref _n));
        if (v == 0) v = unchecked((uint)Interlocked.Increment(ref _n));
        return v;
    }
}

/// <summary>WO-161, victim: a hit id applied once. An id seen within <see cref="Wo161Rules.DupWindowMs"/> is a duplicate.</summary>
public sealed class Wo161Dedupe
{
    private readonly Dictionary<(byte Src, uint Id), long> _seen = new();
    private readonly object _gate = new();
    private const int Max = 512;

    /// <summary>True the first time (apply it); false for a duplicate (never apply twice).</summary>
    public bool Accept(byte source, uint hitId, long nowMs)
    {
        lock (_gate)
        {
            if (_seen.Count >= Max) Prune(nowMs);
            if (_seen.Count >= Max) _seen.Clear();
            if (_seen.TryGetValue((source, hitId), out long at) && nowMs - at <= Wo161Rules.DupWindowMs) return false;
            _seen[(source, hitId)] = nowMs;
            return true;
        }
    }

    public void Clear() { lock (_gate) _seen.Clear(); }

    private void Prune(long nowMs)
    {
        foreach (var k in _seen.Where(kv => nowMs - kv.Value > Wo161Rules.DupWindowMs).Select(kv => kv.Key).ToArray()) _seen.Remove(k);
    }
}

/// <summary>WO-161: the counters of MP-WO161-STATS, both roles (a role fills only its own half).</summary>
public sealed class Wo161Stats
{
    private long _out, _fallback, _noDamage, _in, _applied, _refused, _dup, _malformed, _shown, _notShown, _hit, _blocked, _none, _rowsPlayed, _generic;
    private long _hpX100, _stX100;
    private readonly Dictionary<string, long> _why = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Out() => Interlocked.Increment(ref _out);
    public void Fallback() => Interlocked.Increment(ref _fallback);
    public void NoDamage() => Interlocked.Increment(ref _noDamage);
    public void Malformed() => Interlocked.Increment(ref _malformed);
    public void Dup() => Interlocked.Increment(ref _dup);
    /// <summary>Victim: an NPC's swing row was played on its copy (a swing with no verdict after it is a miss, not a fault).</summary>
    public void RowPlayed() => Interlocked.Increment(ref _rowsPlayed);
    /// <summary>WO-163 (A4), victim: a blow with no swing of its own was shown as a generic lunge on the copy.</summary>
    public void Generic() => Interlocked.Increment(ref _generic);

    /// <summary>A verdict received on the victim: whether it was applied, what it was and whether its swing was shown.</summary>
    public void In(bool applied, HitVerdict v, float hp, float st, bool shown, string reason)
    {
        Interlocked.Increment(ref _in);
        if (hp <= 0 && st <= 0) Interlocked.Increment(ref _none);   // a verdict of no damage (a parry or a miss, the reserved ones): nothing to apply, nothing refused
        else if (applied)
        {
            Interlocked.Increment(ref _applied);
            if (v == HitVerdict.Hit) Interlocked.Increment(ref _hit); else if (v == HitVerdict.Blocked) Interlocked.Increment(ref _blocked);
            Interlocked.Add(ref _hpX100, (long)MathF.Round(hp * 100f));
            Interlocked.Add(ref _stX100, (long)MathF.Round(st * 100f));
        }
        else Interlocked.Increment(ref _refused);
        if (shown) Interlocked.Increment(ref _shown);
        else
        {
            Interlocked.Increment(ref _notShown);
            lock (_gate) { _why[reason] = _why.GetValueOrDefault(reason) + 1; }
        }
    }

    public long Applied => Interlocked.Read(ref _applied);
    public long Dups => Interlocked.Read(ref _dup);
    public long Shown => Interlocked.Read(ref _shown);
    public long GenericShown => Interlocked.Read(ref _generic);
    public long NotShown => Interlocked.Read(ref _notShown);
    public long HpTotalX100 => Interlocked.Read(ref _hpX100);
    public long StTotalX100 => Interlocked.Read(ref _stX100);

    public bool Quiet => Interlocked.Read(ref _out) == 0 && Interlocked.Read(ref _in) == 0 && Interlocked.Read(ref _dup) == 0
                         && Interlocked.Read(ref _malformed) == 0 && Interlocked.Read(ref _noDamage) == 0 && Interlocked.Read(ref _fallback) == 0;

    public string Line()
    {
        string why;
        lock (_gate) why = _why.Count == 0 ? "-" : string.Join(",", _why.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}:{kv.Value}"));
        return string.Concat(
            FormattableString.Invariant($"MP-WO161-STATS out={Interlocked.Read(ref _out)} out_fallback={Interlocked.Read(ref _fallback)} out_nodamage={Interlocked.Read(ref _noDamage)} "),
            FormattableString.Invariant($"in={Interlocked.Read(ref _in)} applied={Interlocked.Read(ref _applied)} refused={Interlocked.Read(ref _refused)} no_damage={Interlocked.Read(ref _none)} dup_ignored={Interlocked.Read(ref _dup)} malformed={Interlocked.Read(ref _malformed)} rows_played={Interlocked.Read(ref _rowsPlayed)} generic={Interlocked.Read(ref _generic)} "),
            FormattableString.Invariant($"hit={Interlocked.Read(ref _hit)} blocked={Interlocked.Read(ref _blocked)} dmg_hp={Interlocked.Read(ref _hpX100) / 100.0:F1} dmg_st={Interlocked.Read(ref _stX100) / 100.0:F1} "),
            FormattableString.Invariant($"shown={Interlocked.Read(ref _shown)} not_shown={Interlocked.Read(ref _notShown)} not_shown_why=[{why}]"));
    }
}
