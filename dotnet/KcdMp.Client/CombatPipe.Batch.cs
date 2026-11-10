// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Diagnostics;

namespace KcdMp.Client;

/// <summary>
/// WO-218 A.1 (pipe_batch; docs/WO-218-findings.md): the pipe's batch, the agent's half.
///
/// The DLL used to answer one request a round trip, about one a frame, and every lane of the agent (the frame processor, the activity
/// and conversation lanes) and every background poll waited for its turn at one gate: in the bench's packed tavern the joiner's NPC
/// animation frames cost ~100 ms each (one round trip plus the gate), the activity frames ~260 ms, and the queue ran 60-100 s behind.
/// Now every request of the generic op families (0x21..0x2B) goes into one line here; one pump sends everything waiting as ONE frame
/// (kBatch 0x2D, wo218_rules.h) -- reliable requests first, then latest-wins state -- and the DLL runs the whole batch in one main-thread
/// turn (2 ms a frame at most) and answers once. One batch is in flight at a time; what arrives meanwhile rides the next. Latest-wins
/// posts (<see cref="PostLatest"/>) are merged per body while they wait: only the newest state of a body is ever sent. The single
/// requests (the position read, the native feed, damage and deaths) keep the gate; the batch never takes its place in their sequence.
/// Off (<see cref="BatchOn"/> false, the twin) or with an older DLL: every request through the gate, as before.
/// </summary>
public sealed partial class CombatPipe
{
    private const byte Batch = 0x2D, BatchReply = 0xAF;
    private const int BatchBudgetBytes = 3800;   // under the DLL's 4 KB read buffer
    private const int BatchMaxItems = 64;

    /// <summary>The switch (mp_pipe_batch; the twin's KCDMP_TEST_W218_OFF=pipe_batch).</summary>
    public bool BatchOn { get; set; } = true;

    // 0 unknown (not probed since this connection), 1 the DLL serves batches, -1 an older DLL
    private volatile int _batchCap;

    /// <summary>The DLL was asked (op 180) since this connection.</summary>
    public bool BatchProbed => _batchCap != 0;

    /// <summary>True when requests go in batches now (the switch on and the DLL serves them).</summary>
    public bool BatchActive => BatchOn && _batchCap == 1 && IsConnected;

    /// <summary>The bridge's probe after a connect (op 181 answered = batches are served).</summary>
    public void SetBatchSupported(bool ok)
    {
        int was = _batchCap;
        _batchCap = ok ? 1 : -1;
        if (was != _batchCap) Console.WriteLine(ok ? "MP-PIPE the DLL serves batches (pipe_batch)" : "MP-PIPE an older KCDMP.dll: no batches -- every request goes one at a time");
    }

    private sealed class BItem
    {
        public byte Type;
        public byte[] Payload = [];
        public byte Want;
        public TaskCompletionSource<byte[]?> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Key;
        public Func<byte[], byte[], byte[]?>? Merge;
        public bool Reliable;
        public long EnqueuedAt;
        public int Kind;   // Wo207Trace.Kind at the call (the frame kind it is charged to)
    }

    private readonly object _bLock = new();
    private readonly LinkedList<BItem> _bReliable = new(), _bState = new();
    private readonly Dictionary<string, LinkedListNode<BItem>> _bKeyed = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _bSignal = new(0, int.MaxValue);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Task? _bPump;
    private ushort _bNextId;
    private ushort _bWaitId;
    private TaskCompletionSource<byte[]?>? _bWait;

    // MP-PIPE counters (the bridge prints them every 10 s)
    private long _bBatches, _bSubs, _bMaxSubs, _bPosted, _bMerged, _bSuperseded, _bFailed, _bRttSumMs, _bRttMaxMs, _bWaitSumMs, _bWaitMaxMs, _bBytesMax;

    private static bool BatchableType(byte type) => type >= 0x21 && type <= 0x2B;

    private bool UseBatch(byte type) => BatchActive && BatchableType(type) && !Priority.Value;

    /// <summary>One counter line, reset on read.</summary>
    public string TakeBatchLine()
    {
        long b = Interlocked.Exchange(ref _bBatches, 0), s = Interlocked.Exchange(ref _bSubs, 0);
        long rtt = Interlocked.Exchange(ref _bRttSumMs, 0), wait = Interlocked.Exchange(ref _bWaitSumMs, 0);
        int waiting;
        lock (_bLock) waiting = _bReliable.Count + _bState.Count;
        return FormattableString.Invariant(
            $"on={(BatchOn ? 1 : 0)} dll={_batchCap} batches={b} subs={s} subs_per_batch={(b > 0 ? (double)s / b : 0):F1} subs_max={Interlocked.Exchange(ref _bMaxSubs, 0)} ")
            + FormattableString.Invariant($"rtt_ms_mean={(b > 0 ? rtt / b : 0)} rtt_ms_max={Interlocked.Exchange(ref _bRttMaxMs, 0)} wait_ms_mean={(s > 0 ? wait / s : 0)} wait_ms_max={Interlocked.Exchange(ref _bWaitMaxMs, 0)} ")
            + FormattableString.Invariant($"posted={Interlocked.Exchange(ref _bPosted, 0)} merged={Interlocked.Exchange(ref _bMerged, 0)} superseded={Interlocked.Exchange(ref _bSuperseded, 0)} failed={Interlocked.Exchange(ref _bFailed, 0)} bytes_max={Interlocked.Exchange(ref _bBytesMax, 0)} waiting={waiting}");
    }

    /// <summary>A request of a generic family through the batch: the reply body (the single path's), or null (no answer).</summary>
    private async Task<(byte[]? Body, PipeReason Fail)> BatchSendAsync(byte type, byte[] payload, byte want, CancellationToken ct)
    {
        var it = new BItem { Type = type, Payload = payload, Want = want, Reliable = true, EnqueuedAt = Stopwatch.GetTimestamp(), Kind = Wo207Trace.Kind.Value };
        Enqueue(it);
        var done = await Task.WhenAny(it.Tcs.Task, Task.Delay(ReplyDeadline + ReplyDeadline, ct));
        if (done != it.Tcs.Task) { it.Tcs.TrySetResult(null); Interlocked.Increment(ref _bFailed); return (null, PipeReason.NoAnswer); }
        var body = await it.Tcs.Task;
        return body is null ? (null, PipeReason.NoAnswer) : (body, PipeReason.Ok);
    }

    /// <summary>
    /// A latest-wins state for the DLL, never awaited (its answer unused): while it waits, a newer post of the same <paramref name="key"/>
    /// is merged into it (<paramref name="merge"/>(older, newer) -> merged, or null when they cannot be merged: the newer one waits on its
    /// own) or replaces it. Its place in the line is the first one's. False = not posted (the batch is off or the DLL older): the caller
    /// sends it the old way.
    /// </summary>
    public bool PostLatest(byte type, byte[] payload, string key, Func<byte[], byte[], byte[]?>? merge = null)
    {
        if (!UseBatch(type)) return false;
        Interlocked.Increment(ref _bPosted);
        var it = new BItem { Type = type, Payload = payload, Want = 0, Key = key, Merge = merge, EnqueuedAt = Stopwatch.GetTimestamp(), Kind = Wo207Trace.Kind.Value };
        lock (_bLock)
        {
            if (_bKeyed.TryGetValue(key, out var node))
            {
                var old = node.Value;
                byte[]? merged = merge is null ? payload : merge(old.Payload, payload);
                if (merged is not null && merged.Length <= 1500)
                {
                    old.Payload = merged;
                    if (merge is null) Interlocked.Increment(ref _bSuperseded); else Interlocked.Increment(ref _bMerged);
                    return true;
                }
                _bKeyed.Remove(key);   // too big to merge: the older one waits as it is, the newer is the key's from now
            }
            _bKeyed[key] = _bState.AddLast(it);
        }
        EnsurePump();
        _bSignal.Release();
        return true;
    }

    private void Enqueue(BItem it)
    {
        lock (_bLock) _bReliable.AddLast(it);
        EnsurePump();
        _bSignal.Release();
    }

    private void EnsurePump()
    {
        if (_bPump is { IsCompleted: false }) return;
        lock (_bLock)
        {
            if (_bPump is { IsCompleted: false }) return;
            _bPump = Task.Run(BatchPumpAsync);
        }
    }

    // Reliable first (in order), then the state posts (in order), up to the byte budget and the item cap; a single oversized item goes alone.
    private List<BItem> TakeBatch()
    {
        var take = new List<BItem>();
        int bytes = 3;
        lock (_bLock)
        {
            foreach (var list in new[] { _bReliable, _bState })
            {
                while (list.First is { } n && take.Count < BatchMaxItems)
                {
                    int sz = 3 + n.Value.Payload.Length;
                    if (take.Count > 0 && bytes + sz > BatchBudgetBytes) return take;
                    list.RemoveFirst();
                    if (n.Value.Key is { } k && _bKeyed.TryGetValue(k, out var kn) && kn == n) _bKeyed.Remove(k);
                    take.Add(n.Value);
                    bytes += sz;
                }
            }
        }
        return take;
    }

    private async Task BatchPumpAsync()
    {
        while (true)
        {
            await _bSignal.WaitAsync();   // the pump never ends: an exit racing a new request would strand it
            if (!BatchActive) { FailAllWaiting(); continue; }
            var take = TakeBatch();
            if (take.Count == 0) continue;
            await SendBatchAsync(take);
            lock (_bLock) if (_bReliable.Count + _bState.Count > 0) _bSignal.Release();   // more waiting: the next batch at once
        }
    }

    private void FailAllWaiting()
    {
        List<BItem> all;
        lock (_bLock) { all = [.. _bReliable, .. _bState]; _bReliable.Clear(); _bState.Clear(); _bKeyed.Clear(); }
        foreach (var it in all) it.Tcs.TrySetResult(null);
        if (all.Count > 0) Interlocked.Add(ref _bFailed, all.Count);
    }

    private async Task SendBatchAsync(List<BItem> take)
    {
        ushort id = unchecked(++_bNextId);
        var body = new byte[3 + take.Sum(t => 3 + t.Payload.Length)];
        BinaryPrimitives.WriteUInt16LittleEndian(body, id);
        body[2] = (byte)take.Count;
        int o = 3;
        foreach (var t in take)
        {
            body[o] = t.Type;
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(o + 1), (ushort)t.Payload.Length);
            t.Payload.CopyTo(body, o + 3);
            o += 3 + t.Payload.Length;
        }
        var wait = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_bLock) { _bWaitId = id; _bWait = wait; }
        long sentAt = Stopwatch.GetTimestamp();
        byte[]? reply = null;
        try
        {
            await WriteFrameAsync(Batch, body, CancellationToken.None);
            var done = await Task.WhenAny(wait.Task, Task.Delay(ReplyDeadline));
            if (done == wait.Task) reply = await wait.Task;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Drop();
        }
        lock (_bLock) { if (_bWait == wait) _bWait = null; }
        long now = Stopwatch.GetTimestamp();
        long rttMs = (now - sentAt) * 1000 / Stopwatch.Frequency;
        Interlocked.Increment(ref _bBatches);
        Interlocked.Add(ref _bSubs, take.Count);
        InterlockedMax(ref _bMaxSubs, take.Count);
        Interlocked.Add(ref _bRttSumMs, rttMs);
        InterlockedMax(ref _bRttMaxMs, rttMs);
        InterlockedMax(ref _bBytesMax, body.Length);
        var subs = reply is null ? null : Wo218Rules.SplitReply(reply, id);
        if (subs is null)
        {
            Interlocked.Add(ref _bFailed, take.Count);
            if (reply is null) Console.WriteLine($"MP-PIPE batch {id} ({take.Count} requests) got no answer in {rttMs} ms");
            else Console.WriteLine($"MP-PIPE batch {id} ({take.Count} requests): the DLL refused it ({reply.Length} bytes)");
        }
        for (int i = 0; i < take.Count; i++)
        {
            var t = take[i];
            long waitMs = (sentAt - t.EnqueuedAt) * 1000 / Stopwatch.Frequency;
            Interlocked.Add(ref _bWaitSumMs, waitMs);
            InterlockedMax(ref _bWaitMaxMs, waitMs);
            Wo207Trace.Pipe(t.Kind, (t.Type << 8) | (t.Payload.Length > 0 ? t.Payload[0] : 0), waitMs, rttMs);
            byte[]? r = subs is not null && i < subs.Count && (t.Want == 0 || subs[i].Type == t.Want) ? subs[i].Body : null;
            t.Tcs.TrySetResult(r);
        }
    }

    /// <summary>The reader: a batch's answer (never through the single requests' reply line).</summary>
    private void OnBatchReply(byte[] body)
    {
        if (body.Length < 4) return;
        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(1));
        TaskCompletionSource<byte[]?>? w;
        lock (_bLock) w = id == _bWaitId ? _bWait : null;
        if (w is null) { Console.WriteLine($"MP-PIPE a batch answer {id} nobody waits for (late, or an earlier connection's)"); return; }
        w.TrySetResult(body);
    }

    private void OnDropBatch()
    {
        _batchCap = 0;
        TaskCompletionSource<byte[]?>? w;
        lock (_bLock) { w = _bWait; _bWait = null; }
        w?.TrySetResult(null);
        FailAllWaiting();
    }

    private static void InterlockedMax(ref long target, long v)
    {
        long cur;
        while (v > (cur = Interlocked.Read(ref target)) && Interlocked.CompareExchange(ref target, v, cur) != cur) { }
    }
}

/// <summary>WO-218: the pipe's batch and the queue's pressure, the pure rules (unit-tested).</summary>
public static partial class Wo218Rules
{
    /// <summary>The DLL's answer [ok][id:2][n] n x ([type][len:2][body]) -> the sub-replies in order; null when it is not this batch's or malformed
    /// (ok 0: the DLL refused the batch as malformed).</summary>
    public static List<(byte Type, byte[] Body)>? SplitReply(byte[] r, ushort id)
    {
        if (r.Length < 4 || r[0] != 1 || BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(1)) != id) return null;
        int n = r[3], o = 4;
        var list = new List<(byte, byte[])>(n);
        for (int i = 0; i < n; i++)
        {
            if (o + 3 > r.Length) return null;
            int len = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(o + 1));
            if (o + 3 + len > r.Length) return null;
            list.Add((r[o], r.AsSpan(o + 3, len).ToArray()));
            o += 3 + len;
        }
        return o == r.Length ? list : null;
    }

    /// <summary>
    /// Two op-133 posts (the host's NPC states for the copies' owner: [133][n] n x ([eid:4][cm][nsc] nsc x 34)) as one: each body's
    /// newest state, in the older post's order, the newer post's new bodies after. Null when either is malformed or the merge would carry
    /// more than 255 bodies.
    /// </summary>
    public static byte[]? MergeStreamIn(byte[] older, byte[] newer)
    {
        var a = ReadStates(older);
        var b = ReadStates(newer);
        if (a is null || b is null) return null;
        var order = new List<uint>();
        var by = new Dictionary<uint, byte[]>();
        foreach (var (eid, rec) in a) { if (!by.ContainsKey(eid)) order.Add(eid); by[eid] = rec; }
        foreach (var (eid, rec) in b) { if (!by.ContainsKey(eid)) order.Add(eid); by[eid] = rec; }
        if (order.Count > 255) return null;
        var ms = new MemoryStream();
        ms.WriteByte(older[0]);
        ms.WriteByte((byte)order.Count);
        foreach (var e in order) ms.Write(by[e]);
        return ms.ToArray();
    }

    /// <summary>[op][n] n x ([eid:4][cm][nsc] nsc x 34) -> (eid, whole record). Null = malformed.</summary>
    public static List<(uint Eid, byte[] Rec)>? ReadStates(byte[] p)
    {
        if (p.Length < 2) return null;
        int n = p[1], o = 2;
        var list = new List<(uint, byte[])>(n);
        for (int i = 0; i < n; i++)
        {
            if (o + 6 > p.Length) return null;
            int nsc = p[o + 5];
            int len = 6 + nsc * KcdMp.Wire.Protocol.NpcAnimScopeLen;
            if (o + len > p.Length) return null;
            list.Add((BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(o)), p.AsSpan(o, len).ToArray()));
            o += len;
        }
        return o == p.Length ? list : null;
    }
}
