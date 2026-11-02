// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KcdMp.Server.Features.Faults;

// =============================================================================
// WO-206 B: the relay's TEST-ONLY fault mode -- the two-copy test bench impairs
// the link between a real host and a real joiner.
//
// The relay is plain TCP (docs/NETWORKING.md): nothing is lost or reordered on
// the wire, so the faults are the ones TCP can really show a game -- a delay, a
// hold-then-burst, a capped rate, a stall with the socket open, a cut. Each one
// acts per DIRECTION (named from the client's view: "up" = client -> relay,
// "down" = relay -> client) on the clients of one ROLE ("host" = the damage
// authority, "joiner" = any other ready client, "any"), from a named MOMENT for
// a time window.
//
// THE SWITCH. A plan loads only when BOTH are present:
//   * the environment variable KCDMP_TEST_RELAY_FAULTS=1, and
//   * the command-line option  --test-faults <plan.json>.
// Neither half is a configuration key: the option is taken out of the argument
// list before any configuration source sees it, and nothing here reads
// IConfiguration -- so no appsettings file, shipped or not, can turn faults on
// (tools/Test-RelayFaultsGate.ps1 checks the shipped files and the launcher /
// installer scripts never carry either half). With either half missing the
// relay is the relay as before: no fault service, no wrapped stream, the
// forwarding path untouched.
//
// THE PLAN (JSON; unknown keys are refused so a typo cannot quietly load nothing):
//   {
//     "seed": 206,                       optional, default 0: the only source of randomness (lag jitter_ms)
//     "rules": [ {
//       "name": "lag-down",              optional, default "r<index>"; [A-Za-z0-9_-]{1,32}, unique
//       "kind": "lag" | "jitter-hold" | "throttle" | "stall" | "cut",
//       "role": "host" | "joiner" | "any",            default "any"
//       "dir":  "up" | "down" | "both",               default "both" (ignored by cut)
//       "at":   "t:+<seconds>" | "step:<name>" | "step:<name>#<n>",  default "t:+0" (#n: the step's n-th time, WO-207 C.4's
//               cut during the rejoin after a host reload = the join's second "world-sent"; plain "step:<name>" = #1)
//       "for_s": <seconds>,                           default 0 = until the relay exits (cut: one-shot, no refusal)
//       lag:         "ms" (> 0), "jitter_ms" (>= 0, default 0; a seeded uniform extra per frame)
//       jitter-hold: "hold_ms" (> 0), "every_ms" (> hold_ms)
//       throttle:    "bytes_per_s" (> 0)
//       stall:       for_s > 0 (the stall's length)
//       cut:         for_s = how long reconnects of the cut client are refused (0 = none); "heal" is its end
//       link:        WO-213 F.1, a link like the field's (LinkShaper): "bytes_per_s" (> 0, the send rate), "buffer_bytes" (the send
//                    buffer: a writer waits while it is full, default 524288 = Steam's own), "ms" (one-way latency), "jitter_ms",
//                    "loss_pct" (0..50: a lost unreliable state frame is gone, a lost reliable one comes rto_ms later and holds the
//                    reliable frames behind it), "stall_per_min" + "stall_min_ms" / "stall_max_ms" (the whole link holds: the
//                    field's ping spikes), "rto_ms" (default 2 * ms + 100). for_s must be 0: a link is the session's.
//     } ]
//   }
//
// "t:+<s>" counts from the FIRST client connection the relay accepts (not the
// relay's own start: a bench starts the relay, then the games, and the first
// connection is the host's agent -- the moment the session exists). Steps are
// the join's milestones as the relay sees them (RelayFaults.Steps). A rule
// fires once: at its first moment, never again.
// =============================================================================

public enum FaultKind { Lag, JitterHold, Throttle, Stall, Cut, Link }

/// <summary>WO-206 B: which client a rule acts on. Resolved per frame: authority can move.</summary>
public enum FaultRole { Any, Host, Joiner }

[Flags]
public enum FaultDir { Up = 1, Down = 2, Both = Up | Down }

/// <summary>One parsed rule of a plan. Times are in milliseconds.</summary>
public sealed record FaultRule(
	int Index, string Name, FaultKind Kind, FaultRole Role, FaultDir Dir,
	string? AtStep, long AtMs, long ForMs,
	int LagMs, int JitterMs, int HoldMs, int EveryMs, long BytesPerS, int AtStepNth = 1, LinkShape? Link = null)
{
	/// <summary>The rule's "at" as the plan wrote it, for the log lines.</summary>
	public string At => AtStep is { } s ? "step:" + s + (AtStepNth > 1 ? "#" + AtStepNth.ToString(CultureInfo.InvariantCulture) : "") : "t:+" + (AtMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);

	public static string KindName(FaultKind k) => k switch
	{
		FaultKind.Lag => "lag", FaultKind.JitterHold => "jitter-hold", FaultKind.Throttle => "throttle",
		FaultKind.Stall => "stall", FaultKind.Link => "link", _ => "cut",
	};

	public static string RoleName(FaultRole r) => r switch { FaultRole.Host => "host", FaultRole.Joiner => "joiner", _ => "any" };

	public static string DirName(FaultDir d) => d switch { FaultDir.Up => "up", FaultDir.Down => "down", _ => "both" };
}

/// <summary>WO-213 F.1: a link rule's shape (LinkShaper). Times in ms, the rate and buffer in bytes.</summary>
public sealed record LinkShape(long BytesPerS, int BufferBytes, int Ms, int JitterMs, double LossPct, double StallPerMin, int StallMinMs, int StallMaxMs, int RtoMs);

/// <summary>WO-206 B: a validated fault plan. <see cref="Parse"/> throws <see cref="FormatException"/> on anything off.</summary>
public sealed class RelayFaultPlan
{
	/// <summary>A plan's rule count is capped so a frame's "held by" set fits one 64-bit mask.</summary>
	public const int MaxRules = 64;

	public int Seed { get; }
	public IReadOnlyList<FaultRule> Rules { get; }
	public string Source { get; }

	private RelayFaultPlan(int seed, IReadOnlyList<FaultRule> rules, string source)
	{
		Seed = seed;
		Rules = rules;
		Source = source;
	}

	private static readonly Regex NamePattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant);
	private static readonly HashSet<string> TopKeys = new(StringComparer.Ordinal) { "seed", "rules" };
	private static readonly HashSet<string> RuleKeys = new(StringComparer.Ordinal)
		{ "name", "kind", "role", "dir", "at", "for_s", "ms", "jitter_ms", "hold_ms", "every_ms", "bytes_per_s",
		  "buffer_bytes", "loss_pct", "stall_per_min", "stall_min_ms", "stall_max_ms", "rto_ms" };   // WO-213 F.1: the link's

	public static RelayFaultPlan Load(string path) => Parse(File.ReadAllText(path), Path.GetFileName(path));

	public static RelayFaultPlan Parse(string json, string source = "(inline)")
	{
		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
		}
		catch (JsonException ex) { throw new FormatException($"fault plan {source}: not JSON ({ex.Message})"); }

		using (doc)
		{
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object) throw new FormatException($"fault plan {source}: the top level must be an object");
			foreach (var p in root.EnumerateObject())
				if (!TopKeys.Contains(p.Name)) throw new FormatException($"fault plan {source}: unknown key '{p.Name}'");

			int seed = root.TryGetProperty("seed", out var s) ? Int(s, "seed", source) : 0;
			if (!root.TryGetProperty("rules", out var rs) || rs.ValueKind != JsonValueKind.Array)
				throw new FormatException($"fault plan {source}: 'rules' must be an array");
			int n = rs.GetArrayLength();
			if (n == 0) throw new FormatException($"fault plan {source}: no rules -- a plan that impairs nothing is a mistake, not a test");
			if (n > MaxRules) throw new FormatException($"fault plan {source}: {n} rules, at most {MaxRules}");

			var rules = new List<FaultRule>(n);
			var names = new HashSet<string>(StringComparer.Ordinal);
			int i = 0;
			foreach (var r in rs.EnumerateArray())
			{
				var rule = ParseRule(r, i, source);
				if (!names.Add(rule.Name)) throw new FormatException($"fault plan {source}: rule name '{rule.Name}' used twice");
				rules.Add(rule);
				i++;
			}
			return new RelayFaultPlan(seed, rules, source);
		}
	}

	private static FaultRule ParseRule(JsonElement r, int index, string source)
	{
		string where = $"fault plan {source}, rule {index}";
		if (r.ValueKind != JsonValueKind.Object) throw new FormatException($"{where}: must be an object");
		foreach (var p in r.EnumerateObject())
			if (!RuleKeys.Contains(p.Name)) throw new FormatException($"{where}: unknown key '{p.Name}'");

		string name = Str(r, "name", where) ?? $"r{index}";
		if (!NamePattern.IsMatch(name)) throw new FormatException($"{where}: name '{name}' must be 1-32 of A-Z a-z 0-9 _ -");
		where = $"fault plan {source}, rule '{name}'";

		var kind = (Str(r, "kind", where) ?? throw new FormatException($"{where}: 'kind' is required")) switch
		{
			"lag" => FaultKind.Lag,
			"jitter-hold" => FaultKind.JitterHold,
			"throttle" => FaultKind.Throttle,
			"stall" => FaultKind.Stall,
			"cut" => FaultKind.Cut,
			"link" => FaultKind.Link,
			var k => throw new FormatException($"{where}: unknown kind '{k}' (lag, jitter-hold, throttle, stall, cut, link)"),
		};
		var role = (Str(r, "role", where) ?? "any") switch
		{
			"any" => FaultRole.Any, "host" => FaultRole.Host, "joiner" => FaultRole.Joiner,
			var x => throw new FormatException($"{where}: unknown role '{x}' (host, joiner, any)"),
		};
		var dir = (Str(r, "dir", where) ?? "both") switch
		{
			"up" => FaultDir.Up, "down" => FaultDir.Down, "both" => FaultDir.Both,
			var x => throw new FormatException($"{where}: unknown dir '{x}' (up, down, both)"),
		};

		string at = Str(r, "at", where) ?? "t:+0";
		string? atStep = null;
		long atMs = 0;
		int nth = 1;
		if (at.StartsWith("step:", StringComparison.Ordinal))
		{
			atStep = at[5..];
			int hash = atStep.IndexOf('#');
			if (hash >= 0)
			{
				if (!int.TryParse(atStep[(hash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out nth) || nth < 1 || nth > 99)
					throw new FormatException($"{where}: '{at}': the step's occurrence must be #1..#99");
				atStep = atStep[..hash];
			}
			if (Array.IndexOf(RelayFaults.Steps, atStep) < 0)
				throw new FormatException($"{where}: unknown step '{atStep}' ({string.Join(", ", RelayFaults.Steps)})");
		}
		else if (at.StartsWith("t:+", StringComparison.Ordinal)
			&& double.TryParse(at[3..], NumberStyles.Float, CultureInfo.InvariantCulture, out double sec) && sec >= 0 && sec < 86400)
			atMs = (long)Math.Round(sec * 1000);
		else
			throw new FormatException($"{where}: 'at' must be \"t:+<seconds>\" or \"step:<name>\", not '{at}'");

		double forS = r.TryGetProperty("for_s", out var f) ? Num(f, "for_s", where) : 0;
		if (forS < 0 || forS > 86400) throw new FormatException($"{where}: for_s {forS} out of range (0..86400)");
		long forMs = (long)Math.Round(forS * 1000);

		int lagMs = 0, jitterMs = 0, holdMs = 0, everyMs = 0;
		long bps = 0;
		switch (kind)
		{
			case FaultKind.Lag:
				lagMs = Need(r, "ms", where);
				if (lagMs <= 0 || lagMs > 60000) throw new FormatException($"{where}: ms {lagMs} out of range (1..60000)");
				jitterMs = r.TryGetProperty("jitter_ms", out var j) ? Int(j, "jitter_ms", where) : 0;
				if (jitterMs < 0 || jitterMs > 60000) throw new FormatException($"{where}: jitter_ms {jitterMs} out of range (0..60000)");
				break;
			case FaultKind.JitterHold:
				holdMs = Need(r, "hold_ms", where);
				everyMs = Need(r, "every_ms", where);
				if (holdMs <= 0 || everyMs <= holdMs || everyMs > 3600000)
					throw new FormatException($"{where}: needs 0 < hold_ms < every_ms (got {holdMs}, {everyMs})");
				break;
			case FaultKind.Throttle:
				bps = Need(r, "bytes_per_s", where);
				if (bps <= 0) throw new FormatException($"{where}: bytes_per_s must be > 0");
				break;
			case FaultKind.Stall:
				if (forMs <= 0) throw new FormatException($"{where}: a stall needs for_s > 0 (its length) -- an endless stall is a cut");
				break;
		}
		LinkShape? link = null;
		if (kind == FaultKind.Link)
		{
			if (forMs != 0) throw new FormatException($"{where}: a link is the whole session's (for_s must be 0)");
			int ms = r.TryGetProperty("ms", out var lm) ? Int(lm, "ms", where) : 0;
			int jit = r.TryGetProperty("jitter_ms", out var lj) ? Int(lj, "jitter_ms", where) : 0;
			long rate = Need(r, "bytes_per_s", where);
			int buf = r.TryGetProperty("buffer_bytes", out var lb) ? Int(lb, "buffer_bytes", where) : 512 * 1024;
			double loss = r.TryGetProperty("loss_pct", out var ll) ? Num(ll, "loss_pct", where) : 0;
			double spm = r.TryGetProperty("stall_per_min", out var ls) ? Num(ls, "stall_per_min", where) : 0;
			int smin = r.TryGetProperty("stall_min_ms", out var ls1) ? Int(ls1, "stall_min_ms", where) : 0;
			int smax = r.TryGetProperty("stall_max_ms", out var ls2) ? Int(ls2, "stall_max_ms", where) : 0;
			int rto = r.TryGetProperty("rto_ms", out var lr) ? Int(lr, "rto_ms", where) : 2 * ms + 100;
			if (rate <= 0 || buf < 1024 || ms < 0 || ms > 10000 || jit < 0 || jit > 10000 || loss < 0 || loss > 50 || spm < 0 || spm > 600
				|| smin < 0 || smax < smin || smax > 30000 || rto < 1 || rto > 30000)
				throw new FormatException($"{where}: a link needs bytes_per_s > 0, buffer_bytes >= 1024, ms/jitter_ms 0..10000, loss_pct 0..50, " +
					"stall_per_min 0..600, 0 <= stall_min_ms <= stall_max_ms <= 30000, rto_ms 1..30000");
			link = new LinkShape(rate, buf, ms, jit, loss, spm, smin, smax, rto);
		}
		// A key of another kind is refused: "ms" on a throttle is a plan that does not say what it means.
		foreach (var (key, kinds) in KindKeys)
			if (r.TryGetProperty(key, out _) && Array.IndexOf(kinds, kind) < 0)
				throw new FormatException($"{where}: '{key}' does not apply to {FaultRule.KindName(kind)}");

		return new FaultRule(index, name, kind, role, dir, atStep, atMs, forMs, lagMs, jitterMs, holdMs, everyMs, bps, nth, link);
	}

	private static readonly (string Key, FaultKind[] Kinds)[] KindKeys =
	{
		("ms", new[] { FaultKind.Lag, FaultKind.Link }), ("jitter_ms", new[] { FaultKind.Lag, FaultKind.Link }),
		("hold_ms", new[] { FaultKind.JitterHold }), ("every_ms", new[] { FaultKind.JitterHold }),
		("bytes_per_s", new[] { FaultKind.Throttle, FaultKind.Link }),
		("buffer_bytes", new[] { FaultKind.Link }), ("loss_pct", new[] { FaultKind.Link }), ("stall_per_min", new[] { FaultKind.Link }),
		("stall_min_ms", new[] { FaultKind.Link }), ("stall_max_ms", new[] { FaultKind.Link }), ("rto_ms", new[] { FaultKind.Link }),
	};

	private static string? Str(JsonElement r, string key, string where)
	{
		if (!r.TryGetProperty(key, out var v)) return null;
		if (v.ValueKind != JsonValueKind.String) throw new FormatException($"{where}: '{key}' must be a string");
		return v.GetString();
	}

	private static int Need(JsonElement r, string key, string where) =>
		r.TryGetProperty(key, out var v) ? Int(v, key, where) : throw new FormatException($"{where}: '{key}' is required");

	private static int Int(JsonElement v, string key, string where) =>
		v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int x) ? x : throw new FormatException($"{where}: '{key}' must be an integer");

	private static double Num(JsonElement v, string key, string where) =>
		v.ValueKind == JsonValueKind.Number ? v.GetDouble() : throw new FormatException($"{where}: '{key}' must be a number");
}

/// <summary>
/// WO-206 B: the test-only switch. Both halves or nothing; see the header of
/// this file. The option is always removed from the argument list, so even a
/// half-present switch never reaches a configuration source.
/// </summary>
public static class RelayFaultSwitch
{
	public const string EnvVar = "KCDMP_TEST_RELAY_FAULTS";
	public const string Option = "--test-faults";

	/// <summary>
	/// Removes "--test-faults &lt;path&gt;" (or "--test-faults=&lt;path&gt;") from
	/// <paramref name="args"/> and returns the plan path when the environment
	/// variable is exactly "1" too; null otherwise. <paramref name="note"/> says
	/// why a present option did not load (logged once at startup); it stays
	/// null when the option is absent, so a machine that merely has the
	/// variable set logs nothing at all.
	/// </summary>
	public static string? Resolve(ref string[] args, string? envValue, out string? note)
	{
		note = null;
		string? path = null;
		bool optionSeen = false;
		var rest = new List<string>(args.Length);
		for (int i = 0; i < args.Length; i++)
		{
			string a = args[i];
			if (a.Equals(Option, StringComparison.OrdinalIgnoreCase))
			{
				optionSeen = true;
				if (i + 1 < args.Length) path = args[++i];
				continue;
			}
			if (a.StartsWith(Option + "=", StringComparison.OrdinalIgnoreCase))
			{
				optionSeen = true;
				path = a[(Option.Length + 1)..];
				continue;
			}
			rest.Add(a);
		}
		args = [.. rest];

		if (!optionSeen) return null;
		if (string.IsNullOrWhiteSpace(path)) { note = $"RELAY-FAULTS off: {Option} was given without a plan file"; return null; }
		if (envValue != "1") { note = $"RELAY-FAULTS off: {Option} was given but {EnvVar} is not 1 -- the fault mode needs both"; return null; }
		return path;
	}
}
