// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace KcdMp.Steam;

/// <summary>Steam's own view of a live connection (GetConnectionRealTimeStatus). Quality is 0..1, -1 unknown.</summary>
public readonly record struct SteamLinkStatus(int PingMs, float QualityLocal, float QualityRemote,
    float OutBytesPerSec, float InBytesPerSec, int SendRateBytesPerSec, int PendingReliable,
    int SentUnackedReliable, long QueueTimeUsec)
{
    // WO-213 A.3: the rest of Steam's numbers, and the two lanes' own queues when the connection has them (B)
    public float OutPacketsPerSec { get; init; }
    public float InPacketsPerSec { get; init; }
    public int PendingUnreliable { get; init; }
    public (int PendingUnreliable, int PendingReliable, long QueueTimeUsec)[] Lanes { get; init; } = [];

    /// <summary>
    /// WO-214 A.0: Steam's queue time as milliseconds, or null when Steam has no estimate -- it reports int64's maximum then (the
    /// field's briefing printed queue_ms=9223372036854775807 on every line). Negative values are no estimate either.
    /// </summary>
    public static double? QueueMs(long usec) => usec < 0 || usec >= long.MaxValue / 2 ? null : usec / 1000.0;

    /// <summary>WO-214 A.0: <see cref="QueueMs"/> for a log line: "-" when Steam has no estimate.</summary>
    public static string QueueMsText(long usec) =>
        QueueMs(usec) is { } ms ? ms.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "-";
}

/// <summary>WO-213 B: what a connection's config says after we set it (read back), and Steam's answer to each set.</summary>
public sealed record SteamLinkConfig(int? SendRateMin, int? SendRateMax, int? NagleTime, int? SendBufferSize, int Lanes, string Log);

/// <summary>A P2P listen socket on one virtual port. Incoming connections are accepted as they arrive.</summary>
public sealed class SteamP2PListener : IDisposable
{
    private readonly SteamSession _session;
    private readonly Channel<SteamP2PConnection> _incoming = Channel.CreateUnbounded<SteamP2PConnection>();
    private volatile bool _closed;

    internal uint Handle { get; }
    public int VirtualPort { get; }

    internal SteamP2PListener(SteamSession session, uint handle, int virtualPort)
    {
        _session = session;
        Handle = handle;
        VirtualPort = virtualPort;
    }

    /// <summary>Called on the pump thread when a peer knocks: accept now, hand it out once connected.</summary>
    internal bool Offer(SteamP2PConnection c)
    {
        if (_closed) return false;
        int r = SteamNative.SteamAPI_ISteamNetworkingSockets_AcceptConnection(_session.Sockets, c.Handle);
        if (r != SteamNative.ResultOk)
        {
            _session.EmitLog($"steam accept refused result={r}");
            return false;
        }
        c.WhenConnected.ContinueWith(t => { if (t.Result) _incoming.Writer.TryWrite(c); }, TaskScheduler.Default);
        return true;
    }

    public ValueTask<SteamP2PConnection> AcceptAsync(CancellationToken ct) => _incoming.Reader.ReadAsync(ct);

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _incoming.Writer.TryComplete();
        SteamNative.SteamAPI_ISteamNetworkingSockets_CloseListenSocket(_session.Sockets, Handle);
        _session.Forget(this);
    }
}

/// <summary>
/// One ISteamNetworkingSockets connection. Messages are reliable and
/// ordered (k_nSteamNetworkingSend_ReliableNoNagle), which is the TCP
/// guarantee the protocol was written against. <see cref="GetStream"/> gives
/// the byte-stream view the agent and relay already speak.
/// </summary>
public sealed class SteamP2PConnection : IDisposable
{
    private readonly SteamSession _session;
    private readonly Channel<byte[]> _inbox = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile int _state = SteamNative.StateConnecting;
    private int _closedFlag;
    private SteamConnectionStream? _stream;

    internal uint Handle { get; }
    internal ulong RemoteSteamId { get; }
    public bool Incoming { get; }

    /// <summary>
    /// WO-144: who is on the other end, as a key the relay can compare (the same
    /// player's new connection replaces his old one) -- a one-way hash of the
    /// remote SteamID, never the id itself, so it can never reach a log as one.
    /// </summary>
    public string PeerKey
    {
        get
        {
            var h = System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(RemoteSteamId ^ 0x6B63746F67657468UL));
            return Convert.ToHexString(h, 0, 8);
        }
    }

    /// <summary>ESteamNetworkingConnectionState as last reported.</summary>
    public int State => _state;
    public bool IsConnected => _state == SteamNative.StateConnected;

    /// <summary>k_ESteamNetConnectionEnd_* and Steam's own text, once closed. Safe to log (no ids).</summary>
    public int EndReason { get; private set; }
    public string EndDebug { get; private set; } = "";

    /// <summary>true once connected; false if it closed first.</summary>
    public Task<bool> WhenConnected => _connected.Task;

    public long BytesSent, BytesReceived, MessagesSent, MessagesReceived, SendRetries;

    internal SteamP2PConnection(SteamSession session, uint handle, ulong remote, bool incoming)
    {
        _session = session;
        Handle = handle;
        RemoteSteamId = remote;
        Incoming = incoming;
    }

    internal void OnState(int state, int endReason, string endDebug)
    {
        _state = state;
        switch (state)
        {
            case SteamNative.StateConnected:
                _connected.TrySetResult(true);
                break;
            case SteamNative.StateClosedByPeer:
            case SteamNative.StateProblemDetectedLocally:
                EndReason = endReason;
                EndDebug = endDebug;
                _session.EmitLog($"steam connection {(state == SteamNative.StateClosedByPeer ? "closed-by-peer" : "problem-detected-locally")} reason={endReason} debug=\"{endDebug}\"");
                Close(null);
                break;
        }
    }

    /// <summary>Pump thread: move every waiting message into the inbox.</summary>
    internal void Drain(IntPtr[] buf)
    {
        if (Volatile.Read(ref _closedFlag) != 0) return;
        while (true)
        {
            int n = SteamNative.SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(_session.Sockets, Handle, buf, buf.Length);
            if (n <= 0) return;
            for (int i = 0; i < n; i++)
            {
                IntPtr m = buf[i];
                IntPtr data = Marshal.ReadIntPtr(m, SteamNative.Msg.Data);
                int size = Marshal.ReadInt32(m, SteamNative.Msg.Size);
                var bytes = new byte[size];
                if (size > 0) Marshal.Copy(data, bytes, 0, size);
                SteamNative.SteamAPI_SteamNetworkingMessage_t_Release(m);
                MessagesReceived++;
                BytesReceived += size;
                _inbox.Writer.TryWrite(bytes);
            }
            if (n < buf.Length) return;
        }
    }

    /// <summary>
    /// Sends one reliable, ordered message. Waits (never drops) when Steam's
    /// send buffer is full: the TCP path blocks in the same place.
    /// </summary>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        for (int off = 0; off < data.Length; )
        {
            int len = Math.Min(SteamNative.MaxMessageSize, data.Length - off);
            while (true)
            {
                if (Volatile.Read(ref _closedFlag) != 0) throw new IOException("Steam connection closed. " + EndDebug);
                int r = SendOnce(data.Slice(off, len));
                if (r == SteamNative.ResultOk) break;
                if (r == SteamNative.ResultLimitExceeded)
                {
                    Interlocked.Increment(ref SendRetries);
                    await Task.Delay(2, ct);
                    continue;
                }
                throw new IOException($"Steam send failed (EResult {r}).");
            }
            off += len;
        }
    }

    private unsafe int SendOnce(ReadOnlyMemory<byte> chunk)
    {
        fixed (byte* p = chunk.Span)
        {
            int r = SteamNative.SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(
                _session.Sockets, Handle, p, (uint)chunk.Length, SteamNative.SendReliableNoNagle, out _);
            if (r == SteamNative.ResultOk)
            {
                Interlocked.Increment(ref MessagesSent);
                Interlocked.Add(ref BytesSent, chunk.Length);
            }
            return r;
        }
    }

    public ValueTask<byte[]> ReceiveAsync(CancellationToken ct) => _inbox.Reader.ReadAsync(ct);
    internal ChannelReader<byte[]> Inbox => _inbox.Reader;

    public SteamLinkStatus? RealTimeStatus()
    {
        int lanes = Volatile.Read(ref _lanes);
        SteamNative.RealTimeStatus s;
        SteamNative.LaneStatus[]? ls = null;
        int r;
        if (lanes > 0)
        {
            ls = new SteamNative.LaneStatus[lanes];
            r = SteamNative.SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus(_session.Sockets, Handle, out s, lanes, ls);
        }
        else r = SteamNative.SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus(_session.Sockets, Handle, out s, 0, IntPtr.Zero);
        return r == SteamNative.ResultOk
            ? new SteamLinkStatus(s.Ping, s.QualityLocal, s.QualityRemote, s.OutBytesPerSec, s.InBytesPerSec,
                                  s.SendRateBytesPerSecond, s.PendingReliable, s.SentUnackedReliable, s.QueueTimeUsec)
            {
                OutPacketsPerSec = s.OutPacketsPerSec, InPacketsPerSec = s.InPacketsPerSec, PendingUnreliable = s.PendingUnreliable,
                Lanes = ls is null ? [] : ls.Select(l => (l.PendingUnreliable, l.PendingReliable, l.QueueTimeUsec)).ToArray(),
            }
            : null;
    }

    // ---- WO-213 B: the connection's limits and its two lanes ----------------------------------------------------------

    private int _lanes;
    public int LaneCount => Volatile.Read(ref _lanes);
    public long UnreliableSent, UnreliableRefused, LaneSendFailures;

    /// <summary>
    /// Sets what is given (null = leave Steam's own value), splits the connection into two lanes when <paramref name="lanes"/> (lane 0
    /// the events: first priority; lane 1 the state: second, so state never holds an event back), and reads every value back at
    /// the connection's scope. Each refusal is in the log text with Steam's answer.
    /// </summary>
    public SteamLinkConfig ApplyLinkConfig(int? sendRateMin, int? sendRateMax, int? nagleUs, int? sendBuffer, bool lanes,
        int statePriority = 1, ushort eventWeight = 1, ushort stateWeight = 1)
    {
        var log = new System.Text.StringBuilder();
        void Set(int v, int? val)
        {
            if (val is not int x) return;
            bool ok = SteamNative.SteamAPI_ISteamNetworkingUtils_SetConnectionConfigValueInt32(_session.NetUtils, Handle, v, x);
            if (!ok) log.Append($" set {_session.ConfigName(v) ?? v.ToString()}={x} REFUSED;");
        }
        // the max before the min: Steam clamps a min above the current max
        Set(SteamNative.CfgSendRateMax, sendRateMax);
        Set(SteamNative.CfgSendRateMin, sendRateMin);
        Set(SteamNative.CfgNagleTime, nagleUs);
        Set(SteamNative.CfgSendBufferSize, sendBuffer);
        if (lanes)
        {
            int r = SteamNative.SteamAPI_ISteamNetworkingSockets_ConfigureConnectionLanes(_session.Sockets, Handle, 2, [0, statePriority], [eventWeight, stateWeight]);
            if (r == SteamNative.ResultOk) Volatile.Write(ref _lanes, 2);
            else log.Append($" lanes REFUSED result={r};");
        }
        return ReadLinkConfig(log.ToString());
    }

    public SteamLinkConfig ReadLinkConfig(string log = "")
    {
        int? Get(int v, out string text)
        {
            int? x = _session.GetConfigInt(v, SteamNative.ScopeConnection, Handle, out int res);
            text = $"{_session.ConfigName(v) ?? "#" + v}={(x is int y ? y.ToString() : "?")}{(res == SteamNative.GetCfgOkInherited ? "(default)" : res == SteamNative.GetCfgOk ? "" : $"(result {res})")}";
            return x;
        }
        var min = Get(SteamNative.CfgSendRateMin, out string t1);
        var max = Get(SteamNative.CfgSendRateMax, out string t2);
        var nag = Get(SteamNative.CfgNagleTime, out string t3);
        var buf = Get(SteamNative.CfgSendBufferSize, out string t4);
        return new SteamLinkConfig(min, max, nag, buf, LaneCount, $"{t1} {t2} {t3} {t4} lanes={LaneCount}{log}");
    }

    /// <summary>
    /// One message on a lane, never waiting: reliable (resent until it arrives, in order within its lane) or unreliable (lost if a
    /// packet is; no Nagle either way). False when Steam refused it (a full send buffer: k_EResultLimitExceeded) -- an unreliable
    /// one is then simply not sent (the next state replaces it), a reliable one the caller tries again.
    /// </summary>
    public unsafe bool TrySendOnLane(ReadOnlySpan<byte> data, bool reliable, int lane, out int result)
    {
        result = SteamNative.ResultOk;
        if (Volatile.Read(ref _closedFlag) != 0) { result = SteamNative.ResultNoConnection; return false; }
        if (lane >= LaneCount) lane = 0;
        IntPtr m = SteamNative.SteamAPI_ISteamNetworkingUtils_AllocateMessage(_session.NetUtils, data.Length);
        if (m == IntPtr.Zero) { result = SteamNative.ResultLimitExceeded; Interlocked.Increment(ref LaneSendFailures); return false; }
        IntPtr buf = Marshal.ReadIntPtr(m, SteamNative.MsgOut.Data);
        fixed (byte* p = data) Buffer.MemoryCopy(p, (void*)buf, data.Length, data.Length);
        Marshal.WriteInt32(m, SteamNative.MsgOut.Conn, unchecked((int)Handle));
        Marshal.WriteInt32(m, SteamNative.MsgOut.Flags, reliable ? SteamNative.SendReliableNoNagle : SteamNative.SendNoNagle);
        Marshal.WriteInt16(m, SteamNative.MsgOut.Lane, (short)lane);
        var res = new long[1];
        SteamNative.SteamAPI_ISteamNetworkingSockets_SendMessages(_session.Sockets, 1, [m], res);   // Steam owns (and frees) the message now
        if (res[0] > 0)
        {
            Interlocked.Increment(ref MessagesSent);
            Interlocked.Add(ref BytesSent, data.Length);
            if (!reliable) Interlocked.Increment(ref UnreliableSent);
            return true;
        }
        result = (int)-res[0];
        if (!reliable) Interlocked.Increment(ref UnreliableRefused);
        else Interlocked.Increment(ref LaneSendFailures);
        return false;
    }

    /// <summary>A reliable message on a lane, waiting (as SendAsync) while Steam's send buffer is full.</summary>
    public async ValueTask SendOnLaneAsync(ReadOnlyMemory<byte> data, int lane, CancellationToken ct = default)
    {
        while (true)
        {
            if (Volatile.Read(ref _closedFlag) != 0) throw new IOException("Steam connection closed. " + EndDebug);
            if (TrySendOnLane(data.Span, reliable: true, lane, out int r)) return;
            if (r != SteamNative.ResultLimitExceeded) throw new IOException($"Steam send failed (EResult {r}).");
            Interlocked.Increment(ref SendRetries);
            await Task.Delay(2, ct);
        }
    }

    /// <summary>Round-trip time Steam measures itself, in ms; -1 before it knows.</summary>
    public int PingMs => RealTimeStatus()?.PingMs ?? -1;

    /// <summary>k_nSteamNetworkConnectionInfoFlags_*; Relayed (16) means through Valve's relays.</summary>
    public int InfoFlags()
    {
        var info = new byte[SteamNative.ConnInfo.Size];
        return SteamNative.SteamAPI_ISteamNetworkingSockets_GetConnectionInfo(_session.Sockets, Handle, info)
            ? BitConverter.ToInt32(info, SteamNative.ConnInfo.Flags) : -1;
    }

    public Stream GetStream() => _stream ??= new SteamConnectionStream(this);

    public void Close(string? why)
    {
        if (Interlocked.Exchange(ref _closedFlag, 1) != 0) return;
        _state = _state is SteamNative.StateClosedByPeer or SteamNative.StateProblemDetectedLocally ? _state : SteamNative.StateNone;
        // linger=true: a Close right after a final Send still delivers it, like a TCP FIN after the last write.
        SteamNative.SteamAPI_ISteamNetworkingSockets_CloseConnection(_session.Sockets, Handle, 0, why, true);
        _connected.TrySetResult(false);
        _inbox.Writer.TryComplete();
        _session.Forget(this);
    }

    public void Dispose() => Close("disposed");
}

/// <summary>
/// The byte-stream view of a Steam connection, so code written against a
/// NetworkStream carries over unchanged.
///
/// Write = one reliable message per call (the agent and relay write whole
/// frames under a lock, so a frame never straddles two writers). Read hands
/// out received bytes in order, in whatever sizes the caller asks for: the
/// reader reassembles frames exactly as it does from TCP, so a message
/// boundary is never load-bearing. Read returns 0 once the peer is gone,
/// which the existing loops already treat as end of stream.
/// </summary>
public sealed class SteamConnectionStream : Stream
{
    private readonly SteamP2PConnection _conn;
    private byte[]? _current;
    private int _offset;

    internal SteamConnectionStream(SteamP2PConnection conn) => _conn = conn;

    public SteamP2PConnection Connection => _conn;

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;

    /// <summary>Idle-read cutoff in ms, as NetworkStream.ReadTimeout (Timeout.Infinite = none).</summary>
    public override int ReadTimeout { get; set; } = Timeout.Infinite;
    public override int WriteTimeout { get; set; } = Timeout.Infinite;
    public override bool CanTimeout => true;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (buffer.Length == 0) return 0;
        while (_current is null || _offset >= _current.Length)
        {
            _current = null;
            try
            {
                if (ReadTimeout is Timeout.Infinite or <= 0)
                {
                    _current = await _conn.Inbox.ReadAsync(ct);
                }
                else
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(ReadTimeout);
                    try { _current = await _conn.Inbox.ReadAsync(cts.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new IOException("Steam read timed out.", new TimeoutException());
                    }
                }
            }
            catch (ChannelClosedException) { return 0; }
            _offset = 0;
        }
        int n = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, n).CopyTo(buffer);
        _offset += n;
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
        buffer.Length == 0 ? ValueTask.CompletedTask : _conn.SendAsync(buffer, ct);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _conn.Close("stream disposed");
        base.Dispose(disposing);
    }
}

/// <summary>
/// WO-213 B/C: a Steam connection as the scheduler's path. Events: reliable on lane 0; state: unreliable on lane 1 when the connection
/// has its two lanes (a refused one is simply not sent: the next state replaces it). Status = Steam's own real-time numbers.
/// </summary>
public sealed class SteamLinkPath(SteamP2PConnection conn) : KcdMp.Wire.ILinkPath
{
    public SteamP2PConnection Connection => conn;
    public string Transport => "steam";
    public bool CanSendUnreliable => conn.LaneCount >= 2;
    public bool StateOwnLane => CanSendUnreliable;

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, bool state, CancellationToken ct)
    {
        if (state && CanSendUnreliable)
        {
            conn.TrySendOnLane(frame.Span, reliable: false, lane: 1, out _);
            return ValueTask.CompletedTask;
        }
        return conn.SendOnLaneAsync(frame, 0, ct);
    }

    // With the state on its own lane the scheduler's queue check is that lane's and the events' lane is reported apart (an event burst on
    // lane 0 -- a world transfer -- does not hold state back: Steam shares the rate between the lanes 3:1); one lane: everything Steam holds.
    public KcdMp.Wire.LinkPathStatus Status() => conn.RealTimeStatus() is not { } s ? default
        : StateOwnLane && s.Lanes.Length >= 2
            ? new KcdMp.Wire.LinkPathStatus(true, s.SendRateBytesPerSec, s.Lanes[1].PendingUnreliable + s.Lanes[1].PendingReliable, (int)Math.Min(int.MaxValue, SteamLinkStatus.QueueMs(s.Lanes[1].QueueTimeUsec) ?? 0))
              { EventPendingBytes = s.Lanes[0].PendingUnreliable + s.Lanes[0].PendingReliable }
            : new KcdMp.Wire.LinkPathStatus(true, s.SendRateBytesPerSec, s.PendingReliable + s.PendingUnreliable, (int)Math.Min(int.MaxValue, SteamLinkStatus.QueueMs(s.QueueTimeUsec) ?? 0));
}
