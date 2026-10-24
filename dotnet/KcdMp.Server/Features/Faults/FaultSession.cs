// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading.Channels;
using KcdMp.Server.Features.ClientHandling;

namespace KcdMp.Server.Features.Faults;

/// <summary>
/// WO-206 B: one client's connection with the fault layer between it and the
/// relay. The relay's ClientSession reads and writes <see cref="Connection"/>
/// exactly as it reads a socket; underneath, every frame passes a
/// <see cref="FaultLink"/> per direction:
///
///   up:   socket -> framer -> Up link (held/delayed/...) -> the session's reads
///   down: the session's writes -> Down link (held/delayed/...) -> socket
///
/// A held frame waits in the link's queue, not in ClientSession's 512 KB send
/// queue: the session hands a down packet to <see cref="FaultStream"/> and its
/// write completes at once, so a hold can never make the session disconnect
/// the client on its own (RelayFaults.SessionQueueLimitBytes says why that
/// matters). TCP keeps order, and so does a link: it is one FIFO, and the head
/// gates everything behind it -- nothing is reordered, nothing is dropped
/// except by a cut, which counts what it dropped.
/// </summary>
public sealed class FaultSession
{
	private readonly RelayFaults _engine;
	private readonly RelayConnection _real;
	private readonly CancellationTokenSource _cts = new();
	private readonly Channel<byte[]> _upOut = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
	private ClientSession? _client;
	private int _closed, _closing, _started;
	private int _roleCache;   // 0 none (not ready), 1 host, 2 joiner
	private long _roleAtMs = long.MinValue;

	/// <summary>Accept order, 1 = the first connection (the jitter seed's stream id).</summary>
	public int Ordinal { get; }
	public FaultLink Up { get; }
	public FaultLink Down { get; }

	/// <summary>What the relay's ClientSession runs on: the fault stream, the real connection's identity.</summary>
	public RelayConnection Connection { get; }

	/// <summary>The down link's WorldOffer chunk counts by join id (the down pump's own; "world-sent").</summary>
	internal Dictionary<uint, int> Offers { get; } = new();

	internal FaultSession(RelayFaults engine, RelayConnection real, int ordinal)
	{
		_engine = engine;
		_real = real;
		Ordinal = ordinal;
		Up = new FaultLink(engine, this, FaultDir.Up);
		Down = new FaultLink(engine, this, FaultDir.Down);
		Connection = new RelayConnection(new FaultStream(this), real.Remote, real.IsLoopback, real.Transport, CloseGraceful, real.IdentityKey);
	}

	/// <summary>Ties this connection to its session (for its role and name) and starts the pumps.</summary>
	public void Bind(ClientSession client)
	{
		_client = client;
		if (Interlocked.Exchange(ref _started, 1) != 0) return;
		var ct = _cts.Token;
		_ = Guard(ReadUpLoopAsync(ct));
		_ = Guard(Up.PumpAsync(ct));
		_ = Guard(Down.PumpAsync(ct));
	}

	private async Task Guard(Task t)
	{
		try { await t; }
		catch (Exception) { Close(); }   // a pump that dies closes the connection rather than leave it half-faulted
	}

	public bool IsReady => _client?.IsReady == true;
	public string? Name => _client?.Name;
	public string IdText => _client is { IsReady: true } c ? c.Id.ToString() : $"conn{Ordinal}";

	/// <summary>
	/// The role a rule matches against, re-read at most every 100 ms: the damage
	/// authority is the host, any other ready client a joiner, a client before
	/// its handshake neither (only "any" rules touch it).
	/// </summary>
	public FaultRole? Role
	{
		get
		{
			var c = _client;
			if (c is null || !c.IsReady) return null;
			long now = RelayFaults.NowMs();
			int cached = Volatile.Read(ref _roleCache);
			if (cached != 0 && now - Interlocked.Read(ref _roleAtMs) < 100) return cached == 1 ? FaultRole.Host : FaultRole.Joiner;
			bool host = _engine.Clients.IsDamageAuthority(c);
			Volatile.Write(ref _roleCache, host ? 1 : 2);
			Interlocked.Exchange(ref _roleAtMs, now);
			return host ? FaultRole.Host : FaultRole.Joiner;
		}
	}

	// ---------------------------------------------------------------- up: socket -> framer -> link -> session

	private async Task ReadUpLoopAsync(CancellationToken ct)
	{
		bool first = true;
		try
		{
			var header = new byte[3];
			while (true)
			{
				if (!await ReadExactAsync(header, 0, 3, ct)) break;
				int len = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(1));
				var frame = new byte[3 + len];
				header.CopyTo(frame, 0);
				if (len > 0 && !await ReadExactAsync(frame, 3, len, ct)) break;
				if (first)
				{
					first = false;
					if (_engine.RefuseHandshake(this, frame)) { Close(); return; }
				}
				if (!Up.Enqueue(frame)) return;
			}
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
		// The client closed: what it sent before closing still arrives first (TCP does the same), then the end.
		Up.EnqueueEnd();
	}

	private async Task<bool> ReadExactAsync(byte[] buf, int offset, int count, CancellationToken ct)
	{
		int got = 0;
		while (got < count)
		{
			int n = await _real.Stream.ReadAsync(buf.AsMemory(offset + got, count - got), ct);
			if (n <= 0) return false;
			got += n;
		}
		return true;
	}

	internal ValueTask DeliverUpAsync(byte[] frame)
	{
		_upOut.Writer.TryWrite(frame);
		_engine.OnUpDelivered(this, frame);   // after the write: a cut at this step still lets the session read this frame
		return ValueTask.CompletedTask;
	}

	internal void DeliverUpEnd() => _upOut.Writer.TryComplete();

	internal ChannelReader<byte[]> UpReader => _upOut.Reader;

	// ---------------------------------------------------------------- down: session -> link -> socket

	internal bool EnqueueDown(byte[] packet) => Volatile.Read(ref _closed) == 0 && Volatile.Read(ref _closing) == 0 && Down.Enqueue(packet);

	internal async ValueTask DeliverDownAsync(byte[] frame)
	{
		try { await _real.Stream.WriteAsync(frame, _cts.Token); }
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
		{
			Close();
			return;
		}
		_engine.OnDownDelivered(this, frame);
	}

	/// <summary>The session's last packet (a refusal's 0x3D, say) has left: now the socket closes.</summary>
	internal void DeliverDownEnd() => Close();

	// ---------------------------------------------------------------- closing

	/// <summary>Drops what both links still hold (a cut loses what was in flight); returns (frames, bytes).</summary>
	internal (long Frames, long Bytes) DropQueued()
	{
		var (f1, b1) = Up.DropAll();
		var (f2, b2) = Down.DropAll();
		return (f1 + f2, b1 + b2);
	}

	/// <summary>
	/// The session is done with the connection (its Dispose). As with a socket,
	/// what it wrote before closing still goes out -- a refusal's reply must
	/// reach the client under faults too -- so the close waits behind the down
	/// link's last frame, at most <see cref="GracefulCloseMs"/> (a long down
	/// stall loses the tail, as a peer that never reads would).
	/// </summary>
	private void CloseGraceful()
	{
		if (Volatile.Read(ref _closed) != 0 || Interlocked.Exchange(ref _closing, 1) != 0) return;
		if (Volatile.Read(ref _started) == 0) { Close(); return; }
		Down.EnqueueEnd();
		_ = Task.Delay(GracefulCloseMs).ContinueWith(_ => Close(), TaskScheduler.Default);
	}

	public const int GracefulCloseMs = 5000;

	/// <summary>
	/// Closes the real connection. The session then reads what was already
	/// handed to it and the end of the stream -- an ordinary disconnect, with
	/// the relay's usual cleanup. Idempotent; also the wrapped connection's Dispose.
	/// </summary>
	public void Close()
	{
		if (Interlocked.Exchange(ref _closed, 1) != 0) return;
		try { _cts.Cancel(); } catch (ObjectDisposedException) { }
		_upOut.Writer.TryComplete();
		Up.MarkClosed();
		Down.MarkClosed();
		_real.Dispose();
		_engine.Unregister(this);
	}
}

/// <summary>
/// WO-206 B: one direction of one connection. A FIFO of frames, each with its
/// arrival time; the pump releases the head when every active rule that
/// matches this direction and this client's role lets it go:
///   lag          arrival + ms (+ the frame's seeded jitter draw)
///   jitter-hold  in a hold window (the first hold_ms of each every_ms since
///                the rule fired) the head waits for the window's end, so all
///                that arrived in it leave together: a burst
///   throttle     the link's next free time: each released frame costs
///                bytes / bytes_per_s of it
///   stall        the stall's end
/// A frame is counted against a rule the first time that rule holds it --
/// as the head, or queued behind a head the rule holds (TCP's head-of-line
/// wait is the fault too: a stall of 20 frames counts 20, not 1).
/// </summary>
public sealed class FaultLink
{
	private sealed class Entry
	{
		public Entry(byte[]? frame, long arrivedMs) { Frame = frame; ArrivedMs = arrivedMs; }
		public readonly byte[]? Frame;      // null = the end of the stream
		public readonly long ArrivedMs;
		public ulong Marks;                 // the rules that held it (bit = rule index)
		public int JitterFor = -1, JitterMs;
	}

	private readonly RelayFaults _engine;
	private readonly object _lock = new();
	private readonly Queue<Entry> _q = new();
	private readonly SemaphoreSlim _signal = new(0);
	private readonly double[] _throttleNextMs;
	private readonly long[] _holdWindowCounted;
	private readonly Random?[] _rng;
	private long _bytes;
	private bool _over, _closed;
	private ulong _blockedBy;   // the rules holding the head now: what a frame queued behind it waits for too

	public FaultDir Dir { get; }
	public FaultSession Session { get; }

	internal FaultLink(RelayFaults engine, FaultSession session, FaultDir dir)
	{
		_engine = engine;
		Session = session;
		Dir = dir;
		int n = engine.Rules.Length;
		_throttleNextMs = new double[n];
		_holdWindowCounted = Enumerable.Repeat(-1L, n).ToArray();
		_rng = new Random?[n];
	}

	public long QueuedBytes { get { lock (_lock) return _bytes; } }

	public void Signal()
	{
		if (_signal.CurrentCount == 0) _signal.Release();
	}

	internal bool Enqueue(byte[] frame)
	{
		bool over = false, cap = false;
		long held;
		lock (_lock)
		{
			if (_closed) return false;
			var e = new Entry(frame, RelayFaults.NowMs());
			if (_blockedBy != 0) Mark(e, _blockedBy, frame.Length);
			_q.Enqueue(e);
			_bytes += frame.Length;
			held = _bytes;
			if (!_over && _bytes > RelayFaults.SessionQueueLimitBytes) { _over = true; over = true; }
			cap = _bytes > RelayFaults.HardCapBytes;
		}
		if (over) _engine.NoteOverLimit(this, held);
		if (cap) { _engine.CapCut(this, held); return false; }
		Signal();
		return true;
	}

	internal void EnqueueEnd()
	{
		lock (_lock)
		{
			if (_closed) return;
			_q.Enqueue(new Entry(null, RelayFaults.NowMs()));
		}
		Signal();
	}

	internal void MarkClosed()
	{
		lock (_lock) _closed = true;
		Signal();
	}

	internal (long Frames, long Bytes) DropAll()
	{
		lock (_lock)
		{
			long f = 0, b = 0;
			foreach (var e in _q)
				if (e.Frame is { } fr) { f++; b += fr.Length; }
			_q.Clear();
			_bytes = 0;
			return (f, b);
		}
	}

	/// <summary>The active rules that act on this direction of this client now (for over-limit attribution).</summary>
	internal IEnumerable<RelayFaults.RuleState> HoldingRules()
	{
		var role = Session.Role;
		foreach (var rs in _engine.Rules)
			if (rs.IsActive && rs.Rule.Kind != FaultKind.Cut && (rs.Rule.Dir & Dir) != 0
				&& (rs.Rule.Role == FaultRole.Any || rs.Rule.Role == role))
				yield return rs;
	}

	internal async Task PumpAsync(CancellationToken ct)
	{
		try
		{
			while (!ct.IsCancellationRequested)
			{
				Entry? head;
				lock (_lock) head = _q.Count > 0 ? _q.Peek() : null;
				if (head is null)
				{
					await _signal.WaitAsync(ct);
					continue;
				}
				if (head.Frame is not { } frame)
				{
					lock (_lock) if (_q.Count > 0 && ReferenceEquals(_q.Peek(), head)) _q.Dequeue();
					if (Dir == FaultDir.Up) Session.DeliverUpEnd(); else Session.DeliverDownEnd();
					return;
				}

				long now = RelayFaults.NowMs();
				long release = ReleaseAt(head, frame.Length, now, out ulong held);
				lock (_lock)
				{
					// A rule that newly holds the head holds everything queued behind it as well.
					if ((held & ~_blockedBy) != 0)
						foreach (var q in _q)
							if (q.Frame is { } qf) Mark(q, held, qf.Length);
					_blockedBy = held;
				}
				if (release > now)
				{
					// Re-evaluated at least every 50 ms: a rule can start or end meanwhile (the engine signals too).
					await _signal.WaitAsync((int)Math.Clamp(release - now, 1, 50), ct);
					continue;
				}

				lock (_lock)
				{
					if (_q.Count == 0 || !ReferenceEquals(_q.Peek(), head)) continue;   // a cut dropped it meanwhile
					_q.Dequeue();
					_blockedBy = 0;   // the next head says what holds the queue now
					_bytes -= frame.Length;
					if (_over && _bytes < RelayFaults.SessionQueueLimitBytes / 2) _over = false;
				}
				ChargeThrottles(frame.Length, now);
				if (Dir == FaultDir.Up) await Session.DeliverUpAsync(frame);
				else await Session.DeliverDownAsync(frame);
			}
		}
		catch (OperationCanceledException) { }
	}

	/// <summary>When the head may leave: the latest gate of the active rules that match this direction and role.</summary>
	private long ReleaseAt(Entry e, int len, long now, out ulong held)
	{
		held = 0;
		long release = now;
		FaultRole? role = null;
		bool roleRead = false;
		foreach (var rs in _engine.Rules)
		{
			var r = rs.Rule;
			if (r.Kind == FaultKind.Cut || (r.Dir & Dir) == 0 || !rs.IsActive) continue;
			if (r.Role != FaultRole.Any)
			{
				if (!roleRead) { role = Session.Role; roleRead = true; }
				if (role != r.Role) continue;
			}
			long gate = now, window = -1;
			switch (r.Kind)
			{
				case FaultKind.Lag:
					if (e.JitterFor != r.Index)
					{
						// One seeded draw per frame per rule, in frame order -- the same seed is the same schedule.
						e.JitterFor = r.Index;
						e.JitterMs = r.JitterMs > 0 ? (_rng[r.Index] ??= RelayFaults.JitterRng(_engine.Plan.Seed, r.Index, Session.Ordinal, Dir)).Next(r.JitterMs + 1) : 0;
					}
					// +1: both stamps are whole milliseconds, and a frame that arrived at x.9 must still wait the full ms
					gate = e.ArrivedMs + r.LagMs + e.JitterMs + 1;
					break;
				case FaultKind.JitterHold:
					long since = now - Interlocked.Read(ref rs.FiredMs);
					long phase = since % r.EveryMs;
					if (phase < r.HoldMs) { gate = now - phase + r.HoldMs; window = since / r.EveryMs; }
					break;
				case FaultKind.Throttle:
					gate = (long)Math.Ceiling(_throttleNextMs[r.Index]);
					break;
				case FaultKind.Stall:
					gate = Interlocked.Read(ref rs.EndsMs);
					break;
			}
			if (gate <= now) continue;
			held |= 1UL << r.Index;
			if (window >= 0 && _holdWindowCounted[r.Index] != window)
			{
				_holdWindowCounted[r.Index] = window;   // the first frame a hold window catches makes it a burst
				Interlocked.Increment(ref rs.Bursts);
			}
			if (gate > release) release = gate;
		}
		lock (_lock) Mark(e, held, len);
		return release;
	}

	/// <summary>Counts a frame against each rule of <paramref name="mask"/> that had not held it yet. Under _lock.</summary>
	private void Mark(Entry e, ulong mask, int len)
	{
		ulong fresh = mask & ~e.Marks;
		if (fresh == 0) return;
		e.Marks |= fresh;
		while (fresh != 0)
		{
			int i = System.Numerics.BitOperations.TrailingZeroCount(fresh);
			fresh &= fresh - 1;
			var rs = _engine.Rules[i];
			Interlocked.Increment(ref rs.Frames);
			Interlocked.Add(ref rs.Bytes, len);
		}
	}

	private const int ThrottleSlackMs = 50;

	/// <summary>Each released frame costs the link bytes / bytes_per_s of every active throttle on it.</summary>
	private void ChargeThrottles(int len, long now)
	{
		FaultRole? role = null;
		bool roleRead = false;
		foreach (var rs in _engine.Rules)
		{
			var r = rs.Rule;
			if (r.Kind != FaultKind.Throttle || (r.Dir & Dir) == 0 || !rs.IsActive) continue;
			if (r.Role != FaultRole.Any)
			{
				if (!roleRead) { role = Session.Role; roleRead = true; }
				if (role != r.Role) continue;
			}
			// A pump that woke late (the OS timer's granularity) may catch up on up to ThrottleSlackMs of
			// lost time, so the average rate is the cap and not the cap minus the timer's lateness.
			_throttleNextMs[r.Index] = Math.Max(now - ThrottleSlackMs, _throttleNextMs[r.Index]) + len * 1000.0 / r.BytesPerS;
		}
	}
}

/// <summary>
/// WO-206 B: the stream ClientSession reads and writes when faults are on.
/// Reads come from the up link's output; a write is one whole packet (the
/// session's write loop writes one per call) handed to the down link, and
/// completes at once.
/// </summary>
internal sealed class FaultStream : Stream
{
	private readonly FaultSession _s;
	private byte[]? _cur;
	private int _off;

	public FaultStream(FaultSession s) => _s = s;

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override void Flush() { }
	public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
	{
		if (buffer.Length == 0) return 0;
		while (_cur is null || _off >= _cur.Length)
		{
			// A cancelled wait (the session's idle timeout) throws here, as a socket read would.
			if (!await _s.UpReader.WaitToReadAsync(ct)) return 0;
			if (_s.UpReader.TryRead(out var next)) { _cur = next; _off = 0; }
		}
		int n = Math.Min(buffer.Length, _cur.Length - _off);
		_cur.AsSpan(_off, n).CopyTo(buffer.Span);
		_off += n;
		return n;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
		ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

	public override int Read(byte[] buffer, int offset, int count) =>
		ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
	{
		if (!_s.EnqueueDown(buffer.ToArray())) throw new IOException("connection closed by the relay's fault layer");
		return ValueTask.CompletedTask;
	}

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
		WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

	public override void Write(byte[] buffer, int offset, int count) =>
		WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
}
