// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace KcdMp.Client;

/// <summary>
/// WO-207 B.1 (docs/WO-207-findings.md): where the inbound queue's time goes, per message kind. The relay reader stamps each frame
/// as it is read; the frame processor names the kind it is handling (<see cref="Kind"/>, an AsyncLocal that flows into every call the
/// handler awaits), so the Lua statements a handler queues and the DLL pipe calls it makes are charged to that kind. Per kind:
///   received -> taken by the processor (the wait in the agent's queue), the handler's own time,
///   a Lua statement queued -> its batch sent to the game (the batch queue), sent -> the game answered,
///   a pipe call's wait for the pipe's gate (one request at a time, the DLL serves ~1 a frame) and its round trip,
/// and how many of the kind wait in the queue right now. Pure bookkeeping, thread-safe; the bridge prints it (MP-W207-Q / -QK).
/// </summary>
public static class Wo207Trace
{
    /// <summary>The inbound kind the current handler serves (a frame type, or <see cref="ActionKindBase"/> + an action kind); 0 = none.</summary>
    public static readonly AsyncLocal<int> Kind = new();
    public const int ActionKindBase = 0x1000;
    /// <summary>A joined conversation's pipe calls (the starter's camera pump and its options/lines polls, the listener's lane): with
    /// mp_conv_pipe_first they count as a frame's at the pipe's gate (CombatPipe.FramesFirst), never behind the background polls.</summary>
    public const int ConvKind = 0x2000;

    public sealed class Row
    {
        public long Frames, WaitSumMs, WaitMaxMs, HandlerSumMs, HandlerMaxMs;
        public long Lua, LuaQueuedSumMs, LuaQueuedMaxMs, LuaAnswerSumMs, LuaAnswerMaxMs;
        public long Pipe, PipeGateSumMs, PipeGateMaxMs, PipeRttSumMs, PipeRttMaxMs;
        public long Dropped;   // B.2: stale at dequeue, never handed to the game
    }

    private static readonly ConcurrentDictionary<int, Row> Rows = new();
    private static readonly ConcurrentDictionary<int, long> WaitingByKind = new();
    private static readonly ConcurrentDictionary<int, Row> PipeByOp = new();

    private static Row Of(int k) => Rows.GetOrAdd(k, _ => new Row());
    private static void Max(ref long slot, long v) { long cur; while (v > (cur = Interlocked.Read(ref slot)) && Interlocked.CompareExchange(ref slot, v, cur) != cur) { } }

    // Counted BEFORE the frame is written (a processor that takes it at once must find it counted; counting after the write left a
    // kind stuck at 1 for the rest of the session on the bench), uncounted when the write fails.
    private static long _pending;
    public static void Enqueued(int type) { Interlocked.Increment(ref _pending); WaitingByKind.AddOrUpdate(type, 1, (_, n) => n + 1); }
    public static void Dequeued(int type)
    {
        long cur;
        while ((cur = Interlocked.Read(ref _pending)) > 0 && Interlocked.CompareExchange(ref _pending, cur - 1, cur) != cur) { }
        WaitingByKind.AddOrUpdate(type, 0, (_, n) => Math.Max(0, n - 1));
    }
    /// <summary>Frames waiting for the processor right now (its own queue only: lanes and frames taken at read never count).</summary>
    public static long WaitingTotal => Interlocked.Read(ref _pending);

    public static void Frame(int kind, long waitMs, long handlerMs)
    {
        var r = Of(kind);
        Interlocked.Increment(ref r.Frames);
        Interlocked.Add(ref r.WaitSumMs, waitMs); Max(ref r.WaitMaxMs, waitMs);
        Interlocked.Add(ref r.HandlerSumMs, handlerMs); Max(ref r.HandlerMaxMs, handlerMs);
    }

    public static void Dropped(int kind) => Interlocked.Increment(ref Of(kind).Dropped);

    public static void Lua(int kind, long queuedMs, long answerMs)
    {
        var r = Of(kind);
        Interlocked.Increment(ref r.Lua);
        Interlocked.Add(ref r.LuaQueuedSumMs, queuedMs); Max(ref r.LuaQueuedMaxMs, queuedMs);
        Interlocked.Add(ref r.LuaAnswerSumMs, answerMs); Max(ref r.LuaAnswerMaxMs, answerMs);
    }

    public static void Pipe(int kind, int op, long gateMs, long rttMs)
    {
        foreach (var r in new[] { Of(kind), PipeByOp.GetOrAdd(op, _ => new Row()) })
        {
            Interlocked.Increment(ref r.Pipe);
            Interlocked.Add(ref r.PipeGateSumMs, gateMs); Max(ref r.PipeGateMaxMs, gateMs);
            Interlocked.Add(ref r.PipeRttSumMs, rttMs); Max(ref r.PipeRttMaxMs, rttMs);
        }
    }

    public static string KindName(int k) => k == 0 ? "other" : k == ConvKind ? "conv" : k >= ActionKindBase ? $"act{k - ActionKindBase}" : $"0x{k:X2}";

    /// <summary>One line of the busiest kinds since the last call (then reset): name:frames/wait_avg/wait_max/handler_avg/handler_max
    /// [lua n q_avg/q_max a_avg] [pipe n gate_avg/gate_max rtt_avg] [drop n], the busiest first (by handler time + wait).</summary>
    public static string TakeLine(int top = 12)
    {
        var snap = Rows.ToArray();
        Rows.Clear();
        var ops = PipeByOp.ToArray();
        PipeByOp.Clear();
        static long Avg(long sum, long n) => n == 0 ? 0 : sum / n;
        var sb = new StringBuilder();
        foreach (var (k, r) in snap.OrderByDescending(x => x.Value.HandlerSumMs + x.Value.WaitSumMs / Math.Max(1, x.Value.Frames) + x.Value.PipeGateSumMs).Take(top))
        {
            sb.Append(' ').Append(KindName(k)).Append(':')
              .Append(r.Frames).Append('/').Append(Avg(r.WaitSumMs, r.Frames)).Append('/').Append(r.WaitMaxMs)
              .Append('/').Append(Avg(r.HandlerSumMs, r.Frames)).Append('/').Append(r.HandlerMaxMs);
            if (r.Lua > 0) sb.Append(" lua=").Append(r.Lua).Append('/').Append(Avg(r.LuaQueuedSumMs, r.Lua)).Append('/').Append(r.LuaQueuedMaxMs).Append('/').Append(Avg(r.LuaAnswerSumMs, r.Lua));
            if (r.Pipe > 0) sb.Append(" pipe=").Append(r.Pipe).Append('/').Append(Avg(r.PipeGateSumMs, r.Pipe)).Append('/').Append(r.PipeGateMaxMs).Append('/').Append(Avg(r.PipeRttSumMs, r.Pipe));
            if (r.Dropped > 0) sb.Append(" drop=").Append(r.Dropped);
            sb.Append(';');
        }
        sb.Append(" | pipe_ops");
        foreach (var (op, r) in ops.OrderByDescending(x => x.Value.Pipe).Take(10))
            sb.Append(' ').Append(string.Create(CultureInfo.InvariantCulture, $"0x{op >> 8:X2}.{op & 0xFF:X2}:{r.Pipe}/{Avg(r.PipeGateSumMs, r.Pipe)}/{r.PipeGateMaxMs}/{Avg(r.PipeRttSumMs, r.Pipe)}"));
        return sb.ToString();
    }

    /// <summary>What waits in the queue right now, by kind (the non-zero ones, most first).</summary>
    public static string WaitingLine() =>
        string.Join(",", WaitingByKind.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{KindName(kv.Key)}={kv.Value}"));

    /// <summary>Tests: start from nothing.</summary>
    public static void Reset() { Rows.Clear(); WaitingByKind.Clear(); PipeByOp.Clear(); Interlocked.Exchange(ref _pending, 0); }
}

/// <summary>WO-207 A.3: the decision timer on the listener's screen (the pure half; GameBridge.Wo207.cs sends and draws it).</summary>
public static class Wo207Rules
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>WO-207 A.4 (conv_op_frame): the arguments of the conversation ops 91 (options poll), 92 (vote mark) and 93 (a line's voice
    /// on its body). The pipe's call puts the op first, and the DLL reads its arguments after it; WO-204 put the op in the arguments too,
    /// so 93 and 92 were refused as bad requests (no line was ever voiced on the listener's bodies) and 91's arm flag read the op (non-zero
    /// by chance). Off (the twin): the doubled byte.</summary>
    public static byte[] ConvArgs(byte op, byte[] args, bool framed) => framed ? args : [op, .. args];

    /// <summary>Op 93's flags (WO-207 A.4): a body without a speak manager gets one the engine's own way; eid 0 is the entity named here.</summary>
    public const byte SpeakMake = 1, SpeakByName = 2;

    /// <summary>Op 93's arguments: [eid:4][durMs:4][pl:1][path], then [flags:1][nl:1][name] (null when the path is empty or over 200
    /// bytes, or the name over 60).</summary>
    public static byte[]? ConvSpeakArgs(uint eid, uint durMs, string path, byte flags = 0, string? name = null)
    {
        var pb = System.Text.Encoding.UTF8.GetBytes(path ?? "");
        var nb = System.Text.Encoding.UTF8.GetBytes(name ?? "");
        if (pb.Length is 0 or > 200 || nb.Length > 60) return null;
        var a = new byte[11 + pb.Length + nb.Length];
        BitConverter.GetBytes(eid).CopyTo(a, 0);
        BitConverter.GetBytes(durMs).CopyTo(a, 4);
        a[8] = (byte)pb.Length; pb.CopyTo(a, 9);
        a[9 + pb.Length] = flags;
        a[10 + pb.Length] = (byte)nb.Length; nb.CopyTo(a, 11 + pb.Length);
        return a;
    }

    /// <summary>The timer as the starter's HUD shows it (op 91's tail): its sequence (one per start and per clear), on, the HUD's last value
    /// (1 -> 0), ms since it was set, ms since its last update.</summary>
    public readonly record struct TimerPoll(uint Seq, bool On, float Limit, uint SinceSetMs, uint SinceUpdateMs)
    {
        /// <summary>The fraction left now (the HUD's value, run on by the rate since its last update), and how fast it falls (per second).</summary>
        public float RatePerS
        {
            get
            {
                long ran = (long)SinceSetMs - SinceUpdateMs;   // set -> the last update
                return On && ran >= 100 && Limit < 1f ? Math.Clamp((1f - Limit) / ran * 1000f, 0f, 10f) : 0f;
            }
        }
        public float LeftNow => On ? Math.Clamp(Limit - RatePerS * SinceUpdateMs / 1000f, 0f, 1f) : 0f;
    }

    /// <summary>Op 91's WO-207 tail after the rows and the snapshot's [snapSeq:4][onRight:1]: null from an older DLL.</summary>
    public static TimerPoll? ParseTimerTail(byte[] p, int afterSnapshot)
    {
        int o = afterSnapshot;
        if (o < 0 || o + 17 > p.Length) return null;
        return new TimerPoll(BitConverter.ToUInt32(p, o), p[o + 4] != 0, BitConverter.ToSingle(p, o + 5), BitConverter.ToUInt32(p, o + 9), BitConverter.ToUInt32(p, o + 13));
    }

    /// <summary>ConvTimer (kind 13): "v1|cid|decId|on|left|ratePerS" (left = the fraction the starter's HUD shows, 1 -> 0).</summary>
    public static string TimerText(int cid, int decId, bool on, float left, float rate) =>
        string.Create(Inv, $"v1|{cid}|{decId}|{(on ? 1 : 0)}|{left:F3}|{rate:F4}");

    public static bool TryParseTimer(string text, out int cid, out int decId, out bool on, out float left, out float rate)
    {
        cid = decId = 0; on = false; left = rate = 0;
        var f = text.Split('|');
        if (f.Length != 6 || f[0] != "v1" || !int.TryParse(f[1], NumberStyles.None, Inv, out cid) || !int.TryParse(f[2], NumberStyles.None, Inv, out decId)
            || (f[3] != "0" && f[3] != "1") || !float.TryParse(f[4], NumberStyles.Float, Inv, out left) || !float.TryParse(f[5], NumberStyles.Float, Inv, out rate)) return false;
        on = f[3] == "1";
        left = Math.Clamp(left, 0f, 1f); rate = Math.Clamp(rate, 0f, 10f);
        return float.IsFinite(left) && float.IsFinite(rate);
    }

    /// <summary>B.2: the action kinds whose body starts with the sender's own ms (WO-121's combat moments, judged against
    /// Protocol.EventStaleMs): an attack, a jump, a block, a dodge, an NPC's attack row or hit, an avatar's hit, recoil or clinch.</summary>
    public static bool StampedActionKind(byte k) => (ActionKind)k is ActionKind.Attack or ActionKind.Jump or ActionKind.BlockImpulse or ActionKind.Dodge
        or ActionKind.NpcAttack or ActionKind.NpcHit or ActionKind.AvatarHit or ActionKind.AvatarRecoil or ActionKind.AvatarClinch;

    /// <summary>B.2: how far a stamped combat action (an ActionDown payload: [sourceGhostId:1][kind:1][seq:2][phase:1][gen:4][len:1][body],
    /// the body starting with the sender's ms) is behind its sender's newest position stamp; null when it is not such an action or nothing
    /// is known to judge it by.</summary>
    public static int? ActionBehindMs(byte[] p, Func<byte, uint?> senderNewestMs)
    {
        if (p.Length < 14 || p[9] < 4 || p.Length < 10 + p[9] || !StampedActionKind(p[1])) return null;
        uint evMs = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(10));
        if (evMs == 0 || senderNewestMs(p[0]) is not uint posMs) return null;
        return unchecked((int)(posMs - evMs));
    }

    /// <summary>C.3: the host's clock now, from its skip's end and when that arrived (the world clock runs <paramref name="ratio"/> times real time).</summary>
    public static uint SkipEndTarget(uint hostEnd, DateTime receivedUtc, DateTime nowUtc, double ratio)
    {
        double s = Math.Clamp((nowUtc - receivedUtc).TotalSeconds, 0, 600);   // never more than 10 minutes on (a stale hold is the pull-in's)
        return (uint)Math.Min(uint.MaxValue, hostEnd + Math.Round(s * ratio));
    }

    /// <summary>A test twin's switch list ("name,name"): is <paramref name="name"/> in it.</summary>
    public static bool TwinOff(string? list, string name) =>
        !string.IsNullOrEmpty(list) && list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);

    /// <summary>Send a timer now? A new sequence (started or cleared) always; a running one every 500 ms (the listener runs it on by the rate).</summary>
    public static bool TimerDue(uint seq, uint lastSeq, bool on, long nowMs, long lastSentMs) => seq != lastSeq || (on && nowMs - lastSentMs >= 500);
}
