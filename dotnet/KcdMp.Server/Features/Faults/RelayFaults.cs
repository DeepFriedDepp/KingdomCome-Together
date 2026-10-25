// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using KcdMp.Server.Features.ClientHandling;
using ILogger = Serilog.ILogger;

namespace KcdMp.Server.Features.Faults;

/// <summary>
/// WO-206 B: the fault engine -- the plan's rules, when each fires and ends,
/// the join steps the relay has seen, and the counters that say what the
/// faults actually did. Exists only when both halves of the switch are present
/// (RelayFaultSwitch); every connection then runs through a FaultSession.
///
/// NON-HOLLOW by construction: a fault that never touched a frame is a failed
/// run, not a pass, so every rule logs one "RELAY-FAULT &lt;rule&gt; fired" line
/// and one "ended" line with what it did, and one "RELAY-FAULTS" line every
/// 10 s and at exit counts everything, including "hollow=" -- the rules that
/// never fired or fired and touched nothing.
/// </summary>
public sealed class RelayFaults
{
	/// <summary>
	/// The join steps a rule can start at ("at": "step:&lt;name&gt;"), in join order.
	/// Each is detected where the relay's own logic sees it: an up message when
	/// the fault layer hands it to the session (after any up fault), a down
	/// message when it is written to the client's socket (after any down fault).
	///   request    JoinRequest (0x48) up, from the joiner
	///   paused     JoinStatus (0x56) up, from the host, state = JoinStatePaused (2)
	///   world-sent the WorldChunk (0x4D) down to the joiner whose index is the
	///              last of the WorldOffer (0x4B) that went down before it (chunkCount - 1)
	///   received   WorldDone (0x50) up, from the joiner
	///   loaded     the joiner's W203 JoinProgress (0x78, kind 7) up whose text
	///              "v1|&lt;joinId&gt;|&lt;where&gt;|&lt;secs&gt;" says where = "world" (the agent's
	///              GameWhere.World: Gameplay started). It is sent every 5 s
	///              from the world's arrival until ready (GameBridge.Wo204), so it
	///              trails the real load by up to 5 s; when a joiner reaches ready
	///              without one, "loaded" fires just before "ready" (via=ready-fallback).
	///   ready      JoinerReady (0x54) up, from the joiner
	/// </summary>
	public static readonly string[] Steps = { "request", "paused", "world-sent", "received", "loaded", "ready" };

	/// <summary>
	/// ClientSession.MaxQueuedBytes (its private constant, WO-110 4.4): past this
	/// much queued the relay disconnects a client. The fault layer holds frames
	/// in ITS OWN queue, so a hold never trips that disconnect by itself; a
	/// direction holding more than this is counted and logged ("over-limit")
	/// instead, so a run can tell "the real relay would have cut here".
	/// </summary>
	public const int SessionQueueLimitBytes = 512 * 1024;

	/// <summary>The fault layer's own ceiling per direction: past it the client is cut, logged as "cap-cut".</summary>
	public const int HardCapBytes = 64 * 1024 * 1024;

	public const int SummaryEveryMs = 10_000;

	public sealed class RuleState
	{
		public RuleState(FaultRule rule) => Rule = rule;
		public FaultRule Rule { get; }
		internal int State;   // 0 pending, 1 active, 2 ended
		internal long FiredMs, EndsMs = long.MaxValue;
		internal long Frames, Bytes, Bursts, Cuts, Refused, OverLimit, CapCuts, CutDroppedFrames, CutDroppedBytes;
		public bool IsActive => Volatile.Read(ref State) == 1;
		public string StateName => Volatile.Read(ref State) switch { 0 => "pending", 1 => "active", _ => "ended" };
		/// <summary>What the rule did: frames it held for the link faults, cuts and refusals for a cut.</summary>
		public long Effect => Rule.Kind == FaultKind.Cut ? Interlocked.Read(ref Cuts) + Interlocked.Read(ref Refused) : Interlocked.Read(ref Frames);
		public bool Hollow => Volatile.Read(ref State) == 0 || Effect == 0;
		public long FramesHeld => Interlocked.Read(ref Frames);
		public long BytesHeld => Interlocked.Read(ref Bytes);
		public long BurstCount => Interlocked.Read(ref Bursts);
		public long CutCount => Interlocked.Read(ref Cuts);
		public long RefusedCount => Interlocked.Read(ref Refused);
		public long OverLimitCount => Interlocked.Read(ref OverLimit);
	}

	private readonly ILogger _logger;
	private readonly object _lock = new();
	private readonly List<FaultSession> _sessions = new();
	private readonly long[] _stepCounts = new long[Steps.Length];
	private readonly HashSet<uint> _loadedJoins = new();
	private readonly Dictionary<string, (RuleState Rule, long UntilMs)> _refused = new(StringComparer.Ordinal);
	private readonly ConcurrentQueue<string> _recent = new();
	private long _t0Ms = -1;
	private int _ordinal;
	private int _finalDone;

	public RelayFaultPlan Plan { get; }
	public ClientHandler Clients { get; }
	public RuleState[] Rules { get; }

	public RelayFaults(ILogger logger, ClientHandler clients, RelayFaultPlan plan)
	{
		_logger = logger;
		Clients = clients;
		Plan = plan;
		Rules = plan.Rules.Select(r => new RuleState(r)).ToArray();
	}

	private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

	/// <summary>A monotonic millisecond clock (Stopwatch, not the 15.6 ms TickCount).</summary>
	public static long NowMs() => (long)(Stopwatch.GetTimestamp() * MsPerTick);

	/// <summary>
	/// The seeded jitter source of one (rule, connection, direction): the same
	/// seed gives the same draws in frame order. <paramref name="ordinal"/> is the
	/// connection's place in accept order (1 = the first), so a bench that
	/// connects host then joiner gets the same schedule every run.
	/// </summary>
	public static Random JitterRng(int seed, int ruleIndex, int ordinal, FaultDir dir) =>
		new(unchecked(seed * 1_000_003 ^ (ruleIndex + 1) * 7_919 ^ ordinal * 104_729 ^ (int)dir * 31));

	/// <summary>The first <paramref name="n"/> extra delays a lag rule's jitter_ms adds, as the link draws them.</summary>
	public static int[] JitterDraws(int seed, int ruleIndex, int ordinal, FaultDir dir, int jitterMs, int n)
	{
		var rng = JitterRng(seed, ruleIndex, ordinal, dir);
		var d = new int[n];
		for (int i = 0; i < n; i++) d[i] = rng.Next(jitterMs + 1);
		return d;
	}

	// ---------------------------------------------------------------- connections

	/// <summary>A new connection: its streams now run through a FaultSession. The first one starts the "t:+" clock.</summary>
	public FaultSession Attach(RelayConnection conn)
	{
		int ord;
		lock (_lock)
		{
			if (_t0Ms < 0) _t0Ms = NowMs();
			ord = ++_ordinal;
		}
		var s = new FaultSession(this, conn, ord);
		lock (_lock) _sessions.Add(s);
		return s;
	}

	internal void Unregister(FaultSession s)
	{
		lock (_lock) _sessions.Remove(s);
	}

	private FaultSession[] Sessions()
	{
		lock (_lock) return [.. _sessions];
	}

	private void SignalAll()
	{
		foreach (var s in Sessions()) { s.Up.Signal(); s.Down.Signal(); }
	}

	private static bool RoleMatches(FaultRole want, FaultRole? have) => want == FaultRole.Any || have == want;

	// ---------------------------------------------------------------- rules

	/// <summary>Seconds since the first connection (0 before it), for the log lines.</summary>
	private string T(long now)
	{
		long t0 = Interlocked.Read(ref _t0Ms);
		return (t0 < 0 ? 0 : (now - t0) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
	}

	/// <summary>The 50 ms tick (RelayFaultsService): time rules start, windows end.</summary>
	public void Tick()
	{
		long now = NowMs();
		long t0 = Interlocked.Read(ref _t0Ms);
		foreach (var rs in Rules)
		{
			int st = Volatile.Read(ref rs.State);
			if (st == 0 && rs.Rule.AtStep is null && t0 >= 0 && now >= t0 + rs.Rule.AtMs) Fire(rs, "time", null);
			else if (st == 1 && now >= Interlocked.Read(ref rs.EndsMs)) End(rs, rs.Rule.Kind == FaultKind.Cut ? "healed" : "window");
		}
	}

	private void Fire(RuleState rs, string via, FaultSession? from)
	{
		var r = rs.Rule;
		long now = NowMs();
		var targets = new List<FaultSession>();
		lock (_lock)
		{
			if (rs.State != 0) return;
			Interlocked.Exchange(ref rs.FiredMs, now);
			Interlocked.Exchange(ref rs.EndsMs, r.ForMs > 0 ? now + r.ForMs : long.MaxValue);
			Volatile.Write(ref rs.State, 1);
			foreach (var s in _sessions)
				if (RoleMatches(r.Role, s.Role) && (r.Kind != FaultKind.Cut || s.IsReady)) targets.Add(s);
			if (r.Kind == FaultKind.Cut && r.ForMs > 0)
				foreach (var s in targets)
					if (s.Name is { } n) _refused[n] = (rs, now + r.ForMs);
		}

		string extra = r.Kind switch
		{
			FaultKind.Lag => $" ms={r.LagMs} jitter_ms={r.JitterMs} seed={Plan.Seed}",
			FaultKind.JitterHold => $" hold_ms={r.HoldMs} every_ms={r.EveryMs}",
			FaultKind.Throttle => $" bytes_per_s={r.BytesPerS}",
			_ => "",
		};
		string fromText = from is null ? "" : $" from={RoleText(from.Role)}:{from.IdText}";
		Emit(false, $"RELAY-FAULT {r.Name} fired kind={FaultRule.KindName(r.Kind)} role={FaultRule.RoleName(r.Role)} dir={FaultRule.DirName(r.Dir)} at={r.At} via={via}{fromText} t={T(now)}s for_s={(r.ForMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)}{extra} targets={targets.Count}");

		if (r.Kind == FaultKind.Cut)
		{
			foreach (var s in targets) CutSession(s, rs, "cut");
			if (r.ForMs == 0) End(rs, "one-shot");
		}
		SignalAll();
	}

	private void End(RuleState rs, string reason)
	{
		long now = NowMs();
		lock (_lock)
		{
			if (rs.State != 1) return;
			Volatile.Write(ref rs.State, 2);
			foreach (var k in _refused.Where(kv => ReferenceEquals(kv.Value.Rule, rs)).Select(kv => kv.Key).ToList()) _refused.Remove(k);
		}
		long active = now - Interlocked.Read(ref rs.FiredMs);
		bool hollow = rs.Effect == 0;
		Emit(hollow, $"RELAY-FAULT {rs.Rule.Name} ended reason={reason} kind={FaultRule.KindName(rs.Rule.Kind)} t={T(now)}s active_s={(active / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)} " +
			$"frames={rs.FramesHeld} bytes={rs.BytesHeld} bursts={rs.BurstCount} cuts={rs.CutCount} refused={rs.RefusedCount} overlimit={rs.OverLimitCount} capcuts={Interlocked.Read(ref rs.CapCuts)} effect={rs.Effect} hollow={(hollow ? 1 : 0)}");
		SignalAll();
	}

	private void CutSession(FaultSession s, RuleState rs, string what)
	{
		var (f, b) = s.DropQueued();
		Interlocked.Increment(ref rs.Cuts);
		Interlocked.Add(ref rs.CutDroppedFrames, f);
		Interlocked.Add(ref rs.CutDroppedBytes, b);
		Emit(false, $"RELAY-FAULT {rs.Rule.Name} {what} id={s.IdText} role={RoleText(s.Role)} dropped={f}f/{b}B t={T(NowMs())}s");
		s.Close();
	}

	private static string RoleText(FaultRole? r) => r is { } x ? FaultRule.RoleName(x) : "none";

	// ---------------------------------------------------------------- what the links report

	/// <summary>The first frame of a connection (the Handshake): refused while its player's cut is not yet healed.</summary>
	internal bool RefuseHandshake(FaultSession s, byte[] frame)
	{
		// [0x00][len:2][version:1][nameLen:1][name]...
		if (frame.Length < 5) return false;
		int nameLen = frame[4];
		if (5 + nameLen > frame.Length) return false;
		string name = ClientSession.SanitizeName(Encoding.UTF8.GetString(frame, 5, nameLen));
		RuleState rs;
		long until, now = NowMs();
		lock (_lock)
		{
			if (!_refused.TryGetValue(name, out var e) || e.UntilMs <= now) return false;
			(rs, until) = e;
		}
		Interlocked.Increment(ref rs.Refused);
		Emit(false, $"RELAY-FAULT {rs.Rule.Name} refused a reconnect of the cut client (at its handshake) t={T(now)}s heal_in_s={((until - now) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)}");
		return true;
	}

	/// <summary>A frame the fault layer just handed to the relay's session (up): the join steps it carries.</summary>
	internal void OnUpDelivered(FaultSession s, byte[] frame)
	{
		if (frame.Length < 3) return;
		byte type = frame[0];
		var p = frame.AsSpan(3);
		if (Protocol.JoinWireFor(type) is not { } row || p.Length < row.Min || p.Length > row.Max) return;   // the relay's own gate would drop it
		switch (type)
		{
			case Protocol.JoinRequestUp:
				Step("request", s, "join-request");
				break;
			case Protocol.JoinStatusUp when p[Protocol.JoinHeaderLen] == Protocol.JoinStatePaused:
				Step("paused", s, "join-status");
				break;
			case Protocol.WorldDoneUp:
				Step("received", s, "world-done");
				break;
			case Protocol.W203Up when p[Protocol.JoinHeaderLen] == Protocol.W203JoinProgress:
				// [target][joinId:4][kind][tok:4][text] -- the text is "v1|<joinId hex>|<where>|<secs>" (Wo204Rules.JoinProgressText)
				var f = Encoding.ASCII.GetString(p[(Protocol.JoinHeaderLen + Protocol.LootFixedLen)..]).Split('|');
				if (f.Length == 4 && f[0] == "v1" && f[2] == "world"
					&& uint.TryParse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint jid) && FirstLoad(jid))
					Step("loaded", s, "w203-join-progress");
				break;
			case Protocol.JoinerReadyUp:
				if (FirstLoad(BinaryPrimitives.ReadUInt32LittleEndian(p[1..]))) Step("loaded", s, "ready-fallback");
				Step("ready", s, "joiner-ready");
				break;
		}
	}

	private bool FirstLoad(uint joinId)
	{
		lock (_lock) return _loadedJoins.Add(joinId);
	}

	/// <summary>A frame just written to a client's socket (down): the offer's chunk count, the last chunk.</summary>
	internal void OnDownDelivered(FaultSession s, byte[] frame)
	{
		if (frame.Length < 3) return;
		byte type = frame[0];
		var p = frame.AsSpan(3);   // [source][target][joinId:4][body]
		const int body = 1 + Protocol.JoinHeaderLen;
		if (type == Protocol.WorldOfferDown && p.Length == body + WorldOffer.BodyLen)
			s.Offers[BinaryPrimitives.ReadUInt32LittleEndian(p[2..])] = BinaryPrimitives.ReadInt32LittleEndian(p[(body + 8)..]);
		else if (type == Protocol.WorldChunkDown && p.Length > body + 4
			&& s.Offers.TryGetValue(BinaryPrimitives.ReadUInt32LittleEndian(p[2..]), out int n)
			&& BinaryPrimitives.ReadInt32LittleEndian(p[body..]) == n - 1)
			Step("world-sent", s, "world-chunk");
	}

	private void Step(string step, FaultSession from, string via)
	{
		int i = Array.IndexOf(Steps, step);
		long n = Interlocked.Increment(ref _stepCounts[i]);
		foreach (var rs in Rules)   // WO-207 C.4: a rule with #n fires at the step's n-th time
			if (rs.Rule.AtStep == step && n == rs.Rule.AtStepNth && Volatile.Read(ref rs.State) == 0) Fire(rs, via, from);
	}

	public long StepCount(string step) => Interlocked.Read(ref _stepCounts[Array.IndexOf(Steps, step)]);

	/// <summary>A direction first held more than the session queue's limit (SessionQueueLimitBytes).</summary>
	internal void NoteOverLimit(FaultLink link, long held)
	{
		var who = new List<string>();
		foreach (var rs in link.HoldingRules())
		{
			Interlocked.Increment(ref rs.OverLimit);
			who.Add(rs.Rule.Name);
		}
		Emit(true, $"RELAY-FAULT {(who.Count > 0 ? string.Join(",", who) : "-")} over-limit dir={FaultRule.DirName(link.Dir)} id={link.Session.IdText} role={RoleText(link.Session.Role)} " +
			$"held={held}B limit={SessionQueueLimitBytes}B t={T(NowMs())}s -- the real relay's queue would have disconnected this client here; the fault layer keeps holding");
	}

	/// <summary>A direction passed the fault layer's own ceiling: the client is cut, and it says so.</summary>
	internal void CapCut(FaultLink link, long held)
	{
		var rules = link.HoldingRules().ToList();
		foreach (var rs in rules) Interlocked.Increment(ref rs.CapCuts);
		Emit(true, $"RELAY-FAULT {(rules.Count > 0 ? string.Join(",", rules.Select(r => r.Rule.Name)) : "-")} cap-cut dir={FaultRule.DirName(link.Dir)} id={link.Session.IdText} held={held}B cap={HardCapBytes}B t={T(NowMs())}s");
		link.Session.Close();
	}

	// ---------------------------------------------------------------- lines

	/// <summary>The last lines this engine logged (newest last), for the tests and the bench's own checks.</summary>
	public IReadOnlyList<string> RecentLines() => _recent.ToArray();

	private void Emit(bool warn, string line)
	{
		_recent.Enqueue(line);
		while (_recent.Count > 2000 && _recent.TryDequeue(out _)) { }
		if (warn) _logger.Warning("{Line:l}", line);
		else _logger.Information("{Line:l}", line);
	}

	public void LogPlanLoaded()
	{
		var rules = string.Join(", ", Plan.Rules.Select(r =>
			$"{r.Name}({FaultRule.KindName(r.Kind)} {FaultRule.RoleName(r.Role)} {FaultRule.DirName(r.Dir)} at={r.At} for_s={(r.ForMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)})"));
		Emit(true, $"RELAY-FAULTS loaded plan={Plan.Source} seed={Plan.Seed} rules={Plan.Rules.Count}: {rules} -- TEST FAULT MODE, never a shipped relay");
	}

	/// <summary>The one summary line: every 10 s, and at exit with final=1.</summary>
	public string SummaryLine(bool final)
	{
		long now = NowMs();
		long Sum(FaultKind k, Func<RuleState, long> f) => Rules.Where(r => r.Rule.Kind == k).Sum(f);
		string Fb(FaultKind k) => $"{Sum(k, r => r.FramesHeld)}/{Sum(k, r => r.BytesHeld)}B";
		long inflight = Sessions().Sum(s => s.Up.QueuedBytes + s.Down.QueuedBytes);
		string steps = string.Join(",", Steps.Select((n, i) => $"{n}:{Interlocked.Read(ref _stepCounts[i])}"));
		string perRule = string.Join(",", Rules.Select(r => $"{r.Rule.Name}:{r.StateName}:{r.Effect}"));
		return $"RELAY-FAULTS t={T(now)}s final={(final ? 1 : 0)} plan={Plan.Source} seed={Plan.Seed} rules={Rules.Length} " +
			$"pending={Rules.Count(r => r.StateName == "pending")} active={Rules.Count(r => r.IsActive)} ended={Rules.Count(r => r.StateName == "ended")} hollow={Rules.Count(r => r.Hollow)} " +
			$"delayed={Fb(FaultKind.Lag)} held={Fb(FaultKind.JitterHold)} bursts={Sum(FaultKind.JitterHold, r => r.BurstCount)} stalled={Fb(FaultKind.Stall)} throttled={Fb(FaultKind.Throttle)} " +
			$"cut={Rules.Sum(r => r.CutCount)} refused={Rules.Sum(r => r.RefusedCount)} overlimit={Rules.Sum(r => r.OverLimitCount)} capcuts={Rules.Sum(r => Interlocked.Read(ref r.CapCuts))} " +
			$"inflight={inflight}B steps={steps} per_rule={perRule}";
	}

	public void LogSummary(bool final)
	{
		string line = SummaryLine(final);
		Emit(final && Rules.Any(r => r.Hollow), line);
	}

	/// <summary>At exit: every active rule ends (reason=exit), a never-fired one says so, then the final summary.</summary>
	public void Final()
	{
		if (Interlocked.Exchange(ref _finalDone, 1) != 0) return;
		foreach (var rs in Rules)
		{
			if (rs.IsActive) End(rs, "exit");
			else if (rs.StateName == "pending")
				Emit(true, $"RELAY-FAULT {rs.Rule.Name} never fired at={rs.Rule.At} effect=0 hollow=1");
		}
		LogSummary(true);
	}
}

/// <summary>
/// WO-206 B: runs the fault engine's clock -- time rules, window ends, the
/// 10 s summary and the final one at exit. Registered only with the switch on.
/// </summary>
public sealed class RelayFaultsService : BackgroundService
{
	private readonly RelayFaults _faults;

	public RelayFaultsService(RelayFaults faults) => _faults = faults;

	protected override async Task ExecuteAsync(CancellationToken ct)
	{
		_faults.LogPlanLoaded();
		long nextSummary = RelayFaults.NowMs() + RelayFaults.SummaryEveryMs;
		try
		{
			while (!ct.IsCancellationRequested)
			{
				await Task.Delay(50, ct);
				_faults.Tick();
				if (RelayFaults.NowMs() >= nextSummary)
				{
					_faults.LogSummary(false);
					nextSummary += RelayFaults.SummaryEveryMs;
				}
			}
		}
		catch (OperationCanceledException) { }
		finally { _faults.Final(); }
	}
}
