// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

// WO-202 V.1 cutscene_clock_v2 (the follower of a shared in-game cutscene). WO-194 set this copy's sequence time on every partner sample
// that was 0.2 s off; session B's joiner got the host's samples bunched and late (the one-way delay rising 35 -> 850 ms, a backlog), so
// each stale sample pulled the copy back and the next fresh one pushed it forward: 125 hard seeks in about 40 s, lines cut. Now the
// partner's clock is the median of its last 8 samples (out-of-order ones not kept, ones that arrived late dropped), the one-way delay is
// the clock sync's; a drift is closed by the movie system's own playing speed (0.9 - 1.1, SetPlayingSpeed); a jump only forward, only
// when the copy stayed more than 1.5 s behind for a second, and only when no voice line is playing. Never backward.
public partial class GameBridge
{
    private long _w202VoiceUntilMs;   // the end of the latest voice line this machine's engine started (the speech capture's poll)
    private long _w202ClockSeeks, _w202ClockSpeedSets, _w202ClockBackRefused, _w202ClockStale;

    /// <summary>The speech poll saw a line start (its age and length): a cutscene jump waits until it has ended.</summary>
    private void Wo202NoteVoice(int ageMs, float durS)
    {
        long end = Environment.TickCount64 - Math.Max(0, ageMs) + (long)(Math.Max(0f, durS) * 1000f);
        if (end > _w202VoiceUntilMs) _w202VoiceUntilMs = end;
    }

    private async Task Wo202ClockV2Async(W194Own o, byte src, double partnerS, long arrivedMs)
    {
        long runsAt = _w194Remote.TryGetValue(src, out var rr) && rr.Name == o.Name ? rr.RunsAtMs : 0;
        if (!Wo194Rules.IsFollower(o.PlayMs, runsAt)) return;   // this copy ran first: the partner follows it
        long now = Environment.TickCount64;
        double est;
        int kept;
        lock (o.V2)
        {
            o.V2.Add(new Wo202Rules.ClockSample(partnerS, arrivedMs));
            while (o.V2.Count > Wo202Rules.ClockSamples) o.V2.RemoveAt(0);
            est = Wo202Rules.PartnerNowS(o.V2, Wo161Rules.OneWayMs(_clockRttMedianMs), now);
            kept = o.V2.Count;
        }
        if (double.IsNaN(est)) { Interlocked.Increment(ref _w202ClockStale); return; }
        var g = await _combat.Wo194SeqGetAsync(o.Name);
        if (g is not { Res: 1 } own || own.T < 0) return;
        double drift = est - own.T;   // > 0: this copy is behind the partner
        o.FirstAligned = true;
        if (drift > Wo202Rules.SeekOverS) { if (o.V2BehindSinceMs == 0) o.V2BehindSinceMs = now; }
        else o.V2BehindSinceMs = 0;
        if (Wo202Rules.SeekNow(drift, o.V2BehindSinceMs, now, _w202VoiceUntilMs))
        {
            var s = await _combat.Wo194SeqSetAsync(o.Name, (float)est);
            o.V2Seeks++;
            o.V2BehindSinceMs = 0;
            long n = Interlocked.Increment(ref _w202ClockSeeks);
            string res = s is { Res: 1 } x ? FormattableString.Invariant($"set ({x.Before:F3} -> {x.After:F3})") : $"refused ({s?.Res.ToString(CultureInfo.InvariantCulture) ?? "no answer"})";
            Console.WriteLine(FormattableString.Invariant(
                $"WO202-CLOCK '{o.Name}' jump forward: this copy {own.T:F3} s, the partner's (median of {kept}) {est:F3} s, {drift:F3} s behind for over {Wo202Rules.SeekHeldMs} ms, no line playing -> {res} [scene jumps {o.V2Seeks}, n={n}]"));
        }
        if (drift < -Wo202Rules.SeekOverS) { o.V2BackRefused++; Interlocked.Increment(ref _w202ClockBackRefused); }   // ahead: slowed, never pulled back
        double want = Wo202Rules.SpeedFor(drift);
        if (o.V2Speeds < 0 || Math.Abs(want - o.V2Speed) < 0.01) return;
        var sp = await _combat.Wo194SeqSpeedAsync(o.Name, (float)want);
        if (sp is { Res: 1 })
        {
            o.V2Speed = want;
            o.V2Speeds++;
            long n = Interlocked.Increment(ref _w202ClockSpeedSets);
            if (o.V2Speeds <= 6 || o.V2Speeds % 20 == 0)
                Console.WriteLine(FormattableString.Invariant($"WO202-CLOCK '{o.Name}' speed {want:F3} (drift {drift:F3} s, this copy {own.T:F3} s, the partner's {est:F3} s) [scene speed sets {o.V2Speeds}, n={n}]"));
        }
        else if (sp is { Res: 5 })
        {
            o.V2Speeds = -1;   // said once: this build has no speed slots
            Console.WriteLine($"WO202-CLOCK '{o.Name}' the movie system's speed slots are not armed on this build -- the drift is followed by forward jumps only");
        }
    }

    private string Wo202ClockStats() => FormattableString.Invariant(
        $"clock_jumps={Interlocked.Read(ref _w202ClockSeeks)} clock_speed_sets={Interlocked.Read(ref _w202ClockSpeedSets)} clock_back_refused={Interlocked.Read(ref _w202ClockBackRefused)} clock_stale={Interlocked.Read(ref _w202ClockStale)}");
}
