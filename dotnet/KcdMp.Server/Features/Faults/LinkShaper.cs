// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Server.Features.Faults;

/// <summary>
/// WO-213 F.1: one direction of a link like the field's, between the relay and one client (the fault mode's "link" rule; test only).
///
///   send buffer  a writer's frame waits here; while it holds buffer_bytes the writer itself waits (backpressure, as Steam's send
///                buffer and a socket's do -- the 0.63.9 relay's queue then grows behind it)
///   the wire     frames leave in order at bytes_per_s (each takes its length / rate); nothing leaves during a stall (the field's
///                ping spikes: the whole link holds, both directions, stall_per_min times a minute for stall_min..max ms)
///   the flight   one-way ms + a uniform jitter_ms; loss_pct of the frames are lost: an unreliable state frame is gone, a reliable
///                one arrives rto_ms later and the reliable frames behind it wait for it (in-order delivery, head of line)
///
/// Frames are whole (one per write), so a lost state frame never splits the stream the agent reassembles. Every frame's time from
/// the send buffer to its delivery is kept for the summary line: the queue that builds is in it.
/// </summary>
public sealed class LinkShaper
{
	/// <summary>The link's stalls, shared by its two directions (a stall holds the whole link).</summary>
	public sealed class Stalls
	{
		private readonly LinkShape _s;
		private readonly Random _rng;
		private readonly object _lock = new();
		private double _start = double.NaN, _end = double.NaN;
		public long Count;
		public double TotalMs;

		public Stalls(LinkShape s, int seed) { _s = s; _rng = new Random(seed); }

		/// <summary>When a frame ready at <paramref name="t"/> may leave: the end of a stall that covers t, else t.</summary>
		public double Clear(double t)
		{
			if (_s.StallPerMin <= 0 || _s.StallMaxMs <= 0) return t;
			lock (_lock)
			{
				if (double.IsNaN(_start)) Schedule(t);
				while (t >= _end) Schedule(_end);
				return t >= _start ? _end : t;
			}
		}

		private void Schedule(double after)
		{
			double meanGapMs = 60_000.0 / _s.StallPerMin;
			double gap = -Math.Log(1.0 - _rng.NextDouble()) * meanGapMs;
			double len = _s.StallMinMs + _rng.NextDouble() * (_s.StallMaxMs - _s.StallMinMs);
			_start = after + gap; _end = _start + len;
			Count++; TotalMs += len;
		}
	}

	private const double SlackMs = 50;
	private readonly LinkShape _s;
	private readonly Stalls _stalls;
	private readonly Random _rng;
	private readonly Func<byte[], ValueTask> _deliver;
	private readonly Action<int> _onFrame;
	private readonly object _lock = new();
	private readonly Queue<(byte[] F, bool U, double At)> _send = new();
	private readonly PriorityQueue<(byte[] F, bool U, double Queued), (double At, long Seq)> _flight = new();
	private readonly SemaphoreSlim _work = new(0), _space = new(0);
	private int _sendBytes;
	private double _wireFree, _lastReliable;
	private long _seq;
	private bool _closed;

	// counters (the summary)
	public long FramesIn, BytesIn, LostUnreliable, Retransmits, Delivered;
	public int MaxBuffered;
	private readonly List<double> _delays = new(1024);

	public string Dir { get; }

	public LinkShaper(LinkShape shape, Stalls stalls, int seed, string dir, Func<byte[], ValueTask> deliver, Action<int> onFrame)
	{
		_s = shape; _stalls = stalls; _rng = new Random(seed); Dir = dir; _deliver = deliver; _onFrame = onFrame;
	}

	public int RateBytesPerS => (int)Math.Min(int.MaxValue, _s.BytesPerS);
	public int PendingBytes { get { lock (_lock) return _sendBytes; } }

	/// <summary>The send buffer's oldest frame's wait (ms), as Steam's queue time.</summary>
	public int QueueMs
	{
		get { lock (_lock) return _send.Count == 0 ? 0 : (int)Math.Max(0, RelayFaults.NowMs() - _send.Peek().At); }
	}

	/// <summary>One whole frame into the send buffer; waits while the buffer is full (an empty buffer always takes one).</summary>
	public async ValueTask EnqueueAsync(byte[] frame, bool unreliable, CancellationToken ct)
	{
		while (true)
		{
			lock (_lock)
			{
				if (_closed) throw new IOException("the shaped link is closed");
				if (_sendBytes == 0 || _sendBytes + frame.Length <= _s.BufferBytes)
				{
					_send.Enqueue((frame, unreliable, RelayFaults.NowMs()));
					_sendBytes += frame.Length;
					if (_sendBytes > MaxBuffered) MaxBuffered = _sendBytes;
					FramesIn++; BytesIn += frame.Length;
					break;
				}
			}
			await _space.WaitAsync(20, ct);
		}
		_onFrame(frame.Length);
		if (_work.CurrentCount == 0) _work.Release();
	}

	public void Close()
	{
		lock (_lock) { _closed = true; _send.Clear(); _sendBytes = 0; _flight.Clear(); }
		_work.Release(); _space.Release();
	}

	public async Task PumpAsync(CancellationToken ct)
	{
		try
		{
			while (!ct.IsCancellationRequested)
			{
				double now = RelayFaults.NowMs();
				double wake = now + 50;
				// the wire: the head leaves once the wire is free and no stall holds the link
				while (true)
				{
					(byte[] F, bool U, double At) head;
					lock (_lock)
					{
						if (_closed) return;
						if (_send.Count == 0) break;
						head = _send.Peek();
					}
					// a pump that woke late (the OS timer's ~15.6 ms) catches up on up to SlackMs of lost wire time, so the
					// average rate is the cap and not the cap minus the timer's lateness (WO-206's throttle does the same)
					double start = _stalls.Clear(Math.Max(Math.Max(head.At, _wireFree), now - SlackMs));   // never before it was queued
					if (start > now) { wake = Math.Min(wake, start); break; }
					lock (_lock) { _send.Dequeue(); _sendBytes -= head.F.Length; }
					if (_space.CurrentCount == 0) _space.Release();
					double sent = start + head.F.Length * 1000.0 / _s.BytesPerS;
					_wireFree = sent;
					double at = sent + _s.Ms + (_s.JitterMs > 0 ? _rng.NextDouble() * _s.JitterMs : 0);
					bool lost = _s.LossPct > 0 && _rng.NextDouble() * 100.0 < _s.LossPct;
					if (lost && head.U) { LostUnreliable++; continue; }
					if (lost) { Retransmits++; at += _s.RtoMs; }
					if (!head.U) { at = Math.Max(at, _lastReliable); _lastReliable = at; }
					lock (_lock) _flight.Enqueue((head.F, head.U, head.At), (at, _seq++));
				}
				// the flight: every frame whose arrival time has come, in arrival order
				while (true)
				{
					(byte[] F, bool U, double Queued) f;
					double at;
					lock (_lock)
					{
						if (!_flight.TryPeek(out f, out var key)) break;
						at = key.At;
						if (at > now) { wake = Math.Min(wake, at); break; }
						_flight.Dequeue();
					}
					await _deliver(f.F);
					Delivered++;
					lock (_lock) if (_delays.Count < 50_000) _delays.Add(now - f.Queued);
				}
				int ms = (int)Math.Clamp(Math.Ceiling(wake - RelayFaults.NowMs()), 1, 50);
				await _work.WaitAsync(ms, ct);
			}
		}
		catch (OperationCanceledException) { }
	}

	/// <summary>The 10 s summary of this direction (and a new window for the delays).</summary>
	public string TakeSummary(string who)
	{
		List<double> d;
		lock (_lock) { d = new List<double>(_delays); _delays.Clear(); }
		d.Sort();
		double Q(double q) => d.Count == 0 ? 0 : d[Math.Min(d.Count - 1, (int)Math.Round(q * (d.Count - 1)))];
		return string.Create(CultureInfo.InvariantCulture,
			$"RELAY-SHAPER {who} dir={Dir} rate={_s.BytesPerS} buffered={PendingBytes} max_buffered={MaxBuffered} queue_ms={QueueMs} frames={FramesIn} bytes={BytesIn} " +
			$"delivered={Delivered} lost_unreliable={LostUnreliable} retransmits={Retransmits} delay_p50={Q(0.5):F0} delay_p90={Q(0.9):F0} delay_p99={Q(0.99):F0} delay_max={(d.Count > 0 ? d[^1] : 0):F0} " +
			$"stalls={_stalls.Count} stall_ms={_stalls.TotalMs:F0}");
	}
}
