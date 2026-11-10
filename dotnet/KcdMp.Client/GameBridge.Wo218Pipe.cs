// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-218 A (docs/WO-218-findings.md): the joiner's pipe, the frames' half (the batch itself is CombatPipe.Batch.cs).
//   A.1 pipe_batch: the host's NPC states (0x7B -> op 133) are posted, never awaited: their answer was unused and each held the frame
//       processor for a round trip (~100 ms in the bench's tavern); while one waits, the next is merged into it per body (the newest
//       state only). The activity lane (0x6B activity rows, 0x6F extras) takes every frame waiting at once: each body's newest row of a
//       state kind (one-shots, events, all kept in order), and the bodies' rows at the same time -- their requests ride one batch.
//   A.2 pipe_shed: while the frame processor is more than 500 ms behind, a superseded state of a body beyond 40 m is dropped at dequeue
//       (a newer one of the same body already waits); over 1 s a far body's state at most once a second, over 2 s the same for 20-40 m.
//       Never an event. What was shed is asked again of the host (its whole-body state) when the queue is back under 500 ms.
public partial class GameBridge
{
    // ---------------------------------------------------------------- A.1: the host's NPC states posted

    /// <summary>The joiner's 0x7B handler: op 133 posted into the batch (latest wins per body). False = not posted (the old await).</summary>
    private bool Wo218PostStreamIn(byte[] op)
    {
        if (!W218On("pipe_batch")) return false;
        var p = new byte[1 + op.Length];
        p[0] = Protocol.Wo208OpStreamIn;
        op.CopyTo(p, 1);
        return _combat.PostLatest(0x2B, p, "w208:133", Wo218Rules.MergeStreamIn);
    }

    // ---------------------------------------------------------------- A.2: the queue's pressure

    private readonly ConcurrentDictionary<string, long> _w218AnimNewestMs = new(StringComparer.Ordinal);   // per body: the newest 0x7B read
    private readonly ConcurrentDictionary<string, long> _w218AnimAppliedMs = new(StringComparer.Ordinal);  // per body: last state let through
    private readonly ConcurrentDictionary<string, byte> _w218Shed = new(StringComparer.Ordinal);           // bodies shed since the last ask
    private long _w218ShedSuperseded, _w218ShedRate, _w218ShedMid, _w218ShedAsks, _w218ShedOn, _w218ShedMaxLevel;
    private int _w218ShedLevel;
    private long _w218LastShedLineMs;

    /// <summary>The reader: each 0x7B body's newest arrival (the dequeue's "superseded" test).</summary>
    private void Wo218NoteAtRead(int type, byte[] payload, long arrival)
    {
        if (type != Protocol.NpcAnimDown || !W137Joiner) return;
        long at = W147StampMs(arrival);
        foreach (var n in Wo218Rules.NpcAnimNames(payload)) _w218AnimNewestMs[n] = at;
    }

    /// <summary>The shedding level from the processor's lag (0 none, 1 superseded far, 2 + far at 1 Hz, 3 + mid band too).</summary>
    private int Wo218ShedLevelNow()
    {
        if (!W218On("pipe_shed") || !W137Joiner) return 0;
        return Wo218Rules.ShedLevel(Wo202QueueLagMs());
    }

    /// <summary>
    /// The joiner's 0x7B at dequeue (A.2): the bodies that go on. The frame's own read time is the processor's current one; a body
    /// whose newer state already waits is superseded. Returns the body unchanged when nothing is shed.
    /// </summary>
    private byte[] Wo218ShedNpcAnim(byte[] body)
    {
        int level = Wo218ShedLevelNow();
        int was = Interlocked.Exchange(ref _w218ShedLevel, level);
        if (level > 0 && was == 0) Interlocked.Increment(ref _w218ShedOn);
        if (level > Interlocked.Read(ref _w218ShedMaxLevel)) Interlocked.Exchange(ref _w218ShedMaxLevel, level);
        if (level == 0)
        {
            if (was > 0) _ = Wo218AskShedAsync();
            return body;
        }
        long frameAt = Volatile.Read(ref _w147ProcAtMs), now = W147StampMs(Stopwatch.GetTimestamp());
        int sup = 0, rate = 0, mid = 0;
        var kept = Wo218Rules.FilterNpcAnim(body, name =>
        {
            double d = W164NearestPlayerM(name);
            bool superseded = _w218AnimNewestMs.TryGetValue(name, out long newest) && newest > frameAt;
            long last = _w218AnimAppliedMs.TryGetValue(name, out long l) ? l : 0;
            var verdict = Wo218Rules.ShedVerdict(level, d, superseded, now - last);
            if (verdict == Wo218Rules.Shed.Keep) { _w218AnimAppliedMs[name] = now; return true; }
            _w218Shed[name] = 1;
            if (verdict == Wo218Rules.Shed.Superseded) sup++; else if (verdict == Wo218Rules.Shed.FarRate) rate++; else mid++;
            return false;
        });
        if (sup + rate + mid > 0)
        {
            Interlocked.Add(ref _w218ShedSuperseded, sup); Interlocked.Add(ref _w218ShedRate, rate); Interlocked.Add(ref _w218ShedMid, mid);
            long t = Environment.TickCount64;
            if (t - Interlocked.Read(ref _w218LastShedLineMs) > 5000)
            {
                Interlocked.Exchange(ref _w218LastShedLineMs, t);
                Console.WriteLine($"MP-PIPE shed level={level} lag_ms={Wo202QueueLagMs()} superseded_far={Interlocked.Read(ref _w218ShedSuperseded)} far_rate={Interlocked.Read(ref _w218ShedRate)} mid={Interlocked.Read(ref _w218ShedMid)} (state of bodies beyond 40 m; no event is ever shed)");
            }
        }
        return kept ?? body;
    }

    /// <summary>The queue is back: every body shed meanwhile has its whole-body state asked again of the host.</summary>
    private async Task Wo218AskShedAsync()
    {
        var names = _w218Shed.Keys.ToList();
        foreach (var n in names) _w218Shed.TryRemove(n, out _);
        if (names.Count == 0) return;
        Interlocked.Increment(ref _w218ShedAsks);
        for (int i = 0; i < names.Count; i += 20) await Wo210AskAnimAsync(names.Skip(i).Take(20).ToList(), "w218-shed");
        Console.WriteLine($"MP-PIPE shed off: {names.Count} far bod{(names.Count == 1 ? "y" : "ies")} asked again of the host");
    }

    // ---------------------------------------------------------------- A.1: the activity lane, every waiting frame at once

    private long _w218ActBatches, _w218ActFrames, _w218ActRows, _w218ActCoalesced, _w218ActGroupsMax;
    private long _w218ActAtMs;   // the read time of the oldest frame the activity lane works on now (0: the lane is idle)

    /// <summary>How far the activity lane trails its frames' arrival now (0 when it keeps up).</summary>
    private long Wo218ActLagMs()
    {
        long at = Volatile.Read(ref _w218ActAtMs);
        return at <= 0 ? 0 : Math.Max(0, W147StampMs(Stopwatch.GetTimestamp()) - at);
    }

    /// <summary>The activity lane with the batch on: the first frame and every one already waiting, coalesced; the bodies at once.</summary>
    private async Task Wo218ActivityBatchAsync(InFrame first, ChannelReader<InFrame> lane, CancellationToken ct)
    {
        var frames = new List<InFrame> { first };
        while (frames.Count < 200 && lane.TryRead(out var more)) frames.Add(more);
        var units = new List<Wo218Rules.ActUnit>();
        foreach (var f in frames)
        {
            if (Wo140DropSeparate(f.Type)) continue;
            if (SplitJoinDown(f.Payload, out byte src, out _, out var body))
            {
                if (f.Type == Protocol.ActivityHostDown && ActivityCodec.TryDecode(body, out byte k, out var rows))
                {
                    Interlocked.Add(ref _w141RowsIn, rows.Count);
                    foreach (var r in rows) units.Add(new Wo218Rules.ActUnit(f.Type, src, k, r.Name, r.Peer, r, null, f.Arrival));
                    continue;
                }
                if (f.Type == Protocol.ActivityExtraDown && ExtraCodec.TryDecode(body, out byte xk, out var xrows))
                {
                    foreach (var r in xrows) units.Add(new Wo218Rules.ActUnit(f.Type, src, xk, r.Name, 0, null, r, f.Arrival));
                    continue;
                }
            }
            // malformed, or another type: as before, on its own
            Wo207Trace.Kind.Value = f.Type;
            try { await Wo123OnFrameAsync(f.Type, f.Payload, ct); } catch (Exception ex) { Console.WriteLine($"MP-W218 activity frame failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        int before = units.Count;
        var kept = Wo218Rules.CoalesceActivity(units);
        Interlocked.Increment(ref _w218ActBatches);
        Interlocked.Add(ref _w218ActFrames, frames.Count);
        Interlocked.Add(ref _w218ActRows, before);
        Interlocked.Add(ref _w218ActCoalesced, before - kept.Count);
        var groups = kept.GroupBy(u => u.Body).ToList();
        if (groups.Count > Interlocked.Read(ref _w218ActGroupsMax)) Interlocked.Exchange(ref _w218ActGroupsMax, groups.Count);
        using var gate = new SemaphoreSlim(24);
        await Task.WhenAll(groups.Select(async g =>
        {
            await gate.WaitAsync(ct);
            try
            {
                foreach (var u in g)
                {
                    Wo207Trace.Kind.Value = u.Type;
                    long t0 = Stopwatch.GetTimestamp();
                    try
                    {
                        if (u.Type == Protocol.ActivityHostDown) await Wo141OnRowsAsync(u.Type, u.Src, u.Kind, [u.Act!.Value]);
                        else await Wo143OnRowsAsync(u.Src, u.Kind, [u.Extra!]);
                    }
                    catch (Exception ex) { Console.WriteLine($"MP-W218 activity row failed ({u.Body}): {ex.GetType().Name}: {ex.Message}"); }
                    long wait = (t0 - u.Arrival) * 1000 / Stopwatch.Frequency;
                    Wo207Trace.Frame(u.Type, wait, (Stopwatch.GetTimestamp() - t0) * 1000 / Stopwatch.Frequency);
                }
            }
            finally { gate.Release(); Wo207Trace.Kind.Value = 0; }
        }));
    }

    // ---------------------------------------------------------------- the module's other hooks

    private string Wo218ShedLine() =>
        FormattableString.Invariant($" | shed level={Volatile.Read(ref _w218ShedLevel)} level_max={Interlocked.Exchange(ref _w218ShedMaxLevel, 0)} on={Interlocked.Read(ref _w218ShedOn)} superseded_far={Interlocked.Read(ref _w218ShedSuperseded)} far_rate={Interlocked.Read(ref _w218ShedRate)} mid={Interlocked.Read(ref _w218ShedMid)} asks={Interlocked.Read(ref _w218ShedAsks)}")
        + FormattableString.Invariant($" | act batches={Interlocked.Exchange(ref _w218ActBatches, 0)} frames={Interlocked.Exchange(ref _w218ActFrames, 0)} rows={Interlocked.Exchange(ref _w218ActRows, 0)} coalesced={Interlocked.Exchange(ref _w218ActCoalesced, 0)} bodies_max={Interlocked.Exchange(ref _w218ActGroupsMax, 0)}");

    private string Wo218MoreStats() => "";

    private void Wo218TickMore(long n) { }

    private void Wo218OnEventMore(string name, string? arg)
    {
        switch (name)
        {
            case "w218_lootout":   // E.4: the joiner's open screen lost the rows of the items the host's body no longer holds (<body> <rev> <n>)
                Console.WriteLine($"WO218-LOOT joiner-out {arg} -- the copy's rows left its screen (E.4's second stamp)");
                return;
        }
    }
}

