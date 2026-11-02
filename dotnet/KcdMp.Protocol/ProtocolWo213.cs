// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-213 -- the internet stream (docs/WO-213-findings.md): the link between an agent and the relay, measured (Part A) and
// scheduled (Parts B-D).
//
//   type up/down  name        body
//   0x80 / 0x81   LinkHello   up [version:1][caps:2][flags:1]; down [version:1][caps:2][transport:1][rateKBps:4]
//                             The agent says, right after the Ack, which link features it speaks; the relay answers with the ones
//                             both speak and how this connection is carried (0 tcp, 1 steam, 2 the test shaper). A feature is used
//                             only when both said it. Relay-local (never forwarded).
//   -    / 0x83   NpcStateQ   the relay's compact NpcStateDown (D.3), only to a client that said CapCompactNpc -- the agent turns it
//                             back into the 0x27 payload at the socket (NpcCompact), so nothing behind the reader changes.
//
// The release string's build id (WO-211) already refuses another build at the handshake, so these never meet an agent or a relay
// that does not know them; the hello is the explicit negotiation the WO asks for, and the switches' twins can turn a feature off
// on one side only.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte LinkHelloUp = 0x80, LinkHelloDown = 0x81;
    public const byte NpcStateQDown = 0x83;
    public const int LinkHelloUpLen = 4, LinkHelloDownLen = 8;
    public const byte LinkVersion = 1;

    /// <summary>LinkHello caps: state as "latest wins" and unreliable on a link that has it (C), compact NPC states (D.3).</summary>
    public const ushort LinkCapLatestWins = 0x0001, LinkCapUnreliableState = 0x0002, LinkCapCompactNpc = 0x0004;
    public const ushort LinkCapsAll = LinkCapLatestWins | LinkCapUnreliableState | LinkCapCompactNpc;

    public const byte LinkTransportTcp = 0, LinkTransportSteam = 1, LinkTransportShaped = 2;

    /// <summary>E.2: the owner's ask (WO-210 op 140's kind 2): a body's state is late past the hold -- ask the host for it again.</summary>
    public const byte Wo213AskPostureLate = 2;

    /// <summary>The DLL's ops (native/KCDMP/wo213.h): 146 Config [bits:4] or [bits:4][7 x u16 bands] -> [bits:4], 147 Status -> text.</summary>
    public const byte Wo213OpConfig = 146, Wo213OpStatus = 147;

    public static byte[] BuildLinkHelloUp(ushort caps)
    {
        var p = new byte[3 + LinkHelloUpLen];
        p[0] = LinkHelloUp; p[1] = LinkHelloUpLen; p[3] = LinkVersion;
        p[4] = (byte)caps; p[5] = (byte)(caps >> 8);
        return p;
    }

    public static byte[] BuildLinkHelloDown(ushort caps, byte transport, int rateBytesPerS)
    {
        var p = new byte[3 + LinkHelloDownLen];
        p[0] = LinkHelloDown; p[1] = LinkHelloDownLen; p[3] = LinkVersion;
        p[4] = (byte)caps; p[5] = (byte)(caps >> 8); p[6] = transport;
        BitConverter.TryWriteBytes(p.AsSpan(7), Math.Max(0, rateBytesPerS / 1024));
        return p;
    }

    public static bool TryParseLinkHello(ReadOnlySpan<byte> payload, out byte version, out ushort caps, out byte transport, out int rateKBps)
    {
        version = 0; caps = 0; transport = 0; rateKBps = 0;
        if (payload.Length < LinkHelloUpLen) return false;
        version = payload[0]; caps = (ushort)(payload[1] | payload[2] << 8);
        if (payload.Length >= LinkHelloDownLen) { transport = payload[3]; rateKBps = BitConverter.ToInt32(payload[4..]); }
        return true;
    }

    public static string LinkTransportName(byte t) => t switch { LinkTransportSteam => "steam", LinkTransportShaped => "shaped", _ => "tcp" };
}

/// <summary>
/// WO-213 A.1: what kind of message a frame is, for the counters and the scheduler -- the relay table's own names (RelayUp, JoinWire),
/// the side channels by their kind byte ("w203:12"), the action channel by its action ("action:NpcCombat"). Up and down frames of
/// one message share a kind.
/// </summary>
public static class LinkKinds
{
    private static readonly string?[] s_names = BuildNames();

    private static string?[] BuildNames()
    {
        var t = new string?[256];
        foreach (var r in Protocol.RelayUp) { t[r.Up] = r.Name; if (r.Up + 1 < 256 && t[r.Up + 1] is null) t[r.Up + 1] = r.Name; }
        foreach (var r in Protocol.JoinWire) { t[r.Up] = r.Name; t[r.Down] = r.Name; }
        t[Protocol.Position] = "pos"; t[Protocol.Ghost] = "pos";
        t[Protocol.NpcStateUp] = "npc-state"; t[Protocol.NpcStateDown] = "npc-state"; t[Protocol.NpcStateQDown] = "npc-state";
        t[Protocol.Ping] = "ping"; t[Protocol.Pong] = "ping"; t[Protocol.ClockSyncUp] = "ping"; t[Protocol.ClockSyncDown] = "ping";
        t[Protocol.PlayerStateUp] = "vitals"; t[Protocol.PlayerStateDown] = "vitals";
        t[Protocol.LinkHelloUp] = "link"; t[Protocol.LinkHelloDown] = "link";
        t[Protocol.Name] = "name"; t[Protocol.ReleaseVersion] = "name"; t[Protocol.CombatRole] = "name"; t[Protocol.Ack] = "name";
        t[Protocol.Disconnect] = "name";
        return t;
    }

    /// <summary>Is this type a relay down type (its body starts with the source's id)?</summary>
    public static bool IsDown(int type) => Protocol.IsKnownDown(type) || type == Protocol.NpcStateQDown;

    /// <summary>The kind of one frame (type + payload, without the 3-byte header).</summary>
    public static string Of(int type, ReadOnlySpan<byte> p)
    {
        if (type is < 0 or > 255) return "?";
        string name = s_names[type] ?? "0x" + type.ToString("X2", CultureInfo.InvariantCulture);
        if (type == Protocol.ActionUp) return p.Length > 0 ? "action:" + ActionName(p[0]) : "action";
        if (type == Protocol.ActionDown) return p.Length > 1 ? "action:" + ActionName(p[1]) : "action";
        if (type is Protocol.W203Up or Protocol.W203Down or Protocol.ConvUp or Protocol.ConvDown or Protocol.W164Up or Protocol.W164Down
                 or Protocol.ActivityExtraUp or Protocol.ActivityExtraDown)
        {
            int k = (Protocol.JoinWireFor(type) is null ? 1 : 0) + Protocol.JoinHeaderLen;   // a down type carries [src] first
            if (p.Length > k) return name + ":" + p[k].ToString(CultureInfo.InvariantCulture);
        }
        return name;
    }

    public static string ActionName(int k) => k >= 0 && Enum.IsDefined(typeof(ActionKind), (byte)k) ? ((ActionKind)k).ToString() : k.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// WO-213: the switches. Every mechanism ships on; a bench twin turns one off for a whole process with KCDMP_TEST_W213_OFF=name,name
/// (the agent's and the relay's environment; the mod's mp_w213 command turns the agent's at runtime).
///   link_sched (C.1/C.3)  state_unreliable (C.2)  steam_cfg (B)  link_budget (D.1)  npc_bands (D.2)  npc_compact (D.3)  figure_band (D.4)
/// </summary>
public static class W213Switch
{
    public const string EnvVar = "KCDMP_TEST_W213_OFF";

    public static bool TwinOff(string name) => TwinOff(Environment.GetEnvironmentVariable(EnvVar), name);

    public static bool TwinOff(string? env, string name) =>
        !string.IsNullOrWhiteSpace(env) && env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x == name || x == "all");

    // B: the Steam connection's limits (record of the choice: docs/WO-213-findings.md, Part B). This SDK does not estimate bandwidth --
    // its send rate IS SendRateMin (the probe: max 1 MB/s with min 256 KB/s still sent 253 KB/s) -- so the min is the rate.
    public const int SteamSendRateMin = 262_144, SteamSendRateMax = 1_048_576, SteamNagleUs = 0;
    // B: two lanes, events and state at one priority, events weighted 3:1 -- strict priority starved the state lane entirely behind a
    // transfer (the probe: 0 of 39 state messages during 1.5 MB), the weights kept it at 4 ms and the transfer at its rate
    public const int SteamStatePriority = 0;
    public const ushort SteamEventWeight = 3, SteamStateWeight = 1;
}

/// <summary>
/// WO-213 C.1: the message types that carry a body's state -- a newer one of the same body replaces it ("latest wins") and it may
/// travel unreliably (C.2): positions, NPC states, the owner's scopes and postures, opponents. Everything else is an event: in order,
/// complete, reliable -- WO-143's activity extras too: repeated as state every 3 s, each repeat cost the joiner's game a frame (a pipe call
/// on its main thread per frame, a health write per vitals row), and they change seldom. LinkScheduler splits the state by body.
/// </summary>
public static class LinkState
{
    public static bool IsStateType(int type) => type is Protocol.Position or Protocol.Ghost or Protocol.NpcStateUp or Protocol.NpcStateDown
        or Protocol.NpcStateQDown or Protocol.NpcAnimUp or Protocol.NpcAnimDown or Protocol.NpcOppUp or Protocol.NpcOppDown;
}

/// <summary>
/// WO-213 C.2 on the receiving side: the relay repeats each posture and opponent about once a second while state travels unreliably
/// (a lost one is soon replaced). A repeat of what this machine already applied for that body costs its game a frame for nothing (the
/// owner's stream-in is a main-thread pipe call), so an item equal to the last one applied for its body within <see cref="WindowMs"/> is
/// left out; the host's own resend (every 2 s) still passes, and so does a repeat whose original was lost (it differs from the last applied).
/// </summary>
public sealed class LinkRepeatFilter
{
    private readonly Dictionary<string, (byte[] Item, long AtMs)> _last = new(StringComparer.Ordinal);
    private readonly List<StatePiece> _pieces = new();
    public int WindowMs { get; init; } = 1500;
    public long Dropped { get; private set; }

    /// <summary>The payload without the repeated items (the same array when none was); null = every item was a repeat.</summary>
    public byte[]? Filter(int type, byte[] payload, long nowMs)
    {
        if (type is not (Protocol.NpcAnimDown or Protocol.NpcOppDown)) return payload;
        var frame = new byte[3 + payload.Length];
        frame[0] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), (ushort)payload.Length);
        payload.CopyTo(frame, 3);
        _pieces.Clear();
        if (!LinkStateCodec.Split(frame, _pieces) || _pieces.Count == 0 || !_pieces[0].Batched) return payload;
        var keep = new List<byte[]>(_pieces.Count);
        foreach (var p in _pieces)
        {
            if (_last.TryGetValue(p.Slot, out var l) && nowMs - l.AtMs < WindowMs && l.Item.AsSpan().SequenceEqual(p.Item)) { Dropped++; continue; }
            _last[p.Slot] = (p.Item, nowMs);
            keep.Add(p.Item);
        }
        if (_last.Count > 8192) _last.Clear();   // bodies come and go; a cleared table only lets one repeat through
        if (keep.Count == _pieces.Count) return payload;
        if (keep.Count == 0) return null;
        return LinkStateCodec.Build(type, _pieces[0].Prefix, keep, true)[3..];
    }
}

/// <summary>
/// WO-213 A.1: messages and bytes per kind and direction, in 10 s windows (MP-LINK kinds). Thread-safe; the counting is a dictionary
/// update under one lock (a few hundred frames a second at most).
/// </summary>
public sealed class LinkMeter
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (long Msgs, long Bytes)> _out = new(StringComparer.Ordinal), _in = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Msgs, long Bytes)> _outTotal = new(StringComparer.Ordinal), _inTotal = new(StringComparer.Ordinal);
    private long _winStart = Stopwatch.GetTimestamp();

    public void Count(bool outbound, string kind, int bytes)
    {
        lock (_lock)
        {
            var w = outbound ? _out : _in;
            var t = outbound ? _outTotal : _inTotal;
            w[kind] = w.TryGetValue(kind, out var a) ? (a.Msgs + 1, a.Bytes + bytes) : (1, bytes);
            t[kind] = t.TryGetValue(kind, out var b) ? (b.Msgs + 1, b.Bytes + bytes) : (1, bytes);
        }
    }

    /// <summary>One frame (header included) by its type and payload.</summary>
    public void CountFrame(bool outbound, int type, ReadOnlySpan<byte> payload) => Count(outbound, LinkKinds.Of(type, payload), payload.Length + 3);

    public double WindowSeconds => Stopwatch.GetElapsedTime(Volatile.Read(ref _winStart)).TotalSeconds;

    /// <summary>
    /// The window's line and a new window: "MP-LINK kinds who=... window_s=10.0 out_Bps=.. out_mps=.. in_Bps=.. in_mps=.. out=k:Bps/mps,...
    /// in=..." with at most <paramref name="top"/> kinds by bytes per second each way.
    /// </summary>
    public string TakeLine(string who, int top = 12)
    {
        Dictionary<string, (long Msgs, long Bytes)> o, i;
        double s;
        lock (_lock)
        {
            s = Math.Max(0.001, Stopwatch.GetElapsedTime(_winStart).TotalSeconds);
            o = new(_out, StringComparer.Ordinal); i = new(_in, StringComparer.Ordinal);
            _out.Clear(); _in.Clear();
            _winStart = Stopwatch.GetTimestamp();
        }
        return Line(who, s, o, i, top, "kinds");
    }

    /// <summary>The whole connection so far, the same shape ("MP-LINK kinds-total").</summary>
    public string TotalLine(string who, double seconds, int top = 24)
    {
        Dictionary<string, (long Msgs, long Bytes)> o, i;
        lock (_lock) { o = new(_outTotal, StringComparer.Ordinal); i = new(_inTotal, StringComparer.Ordinal); }
        return Line(who, Math.Max(0.001, seconds), o, i, top, "kinds-total");
    }

    private static string Line(string who, double s, Dictionary<string, (long Msgs, long Bytes)> o, Dictionary<string, (long Msgs, long Bytes)> i, int top, string tag)
    {
        var ci = CultureInfo.InvariantCulture;
        string Part(Dictionary<string, (long Msgs, long Bytes)> d) => d.Count == 0 ? "-" : string.Join(',', d.OrderByDescending(kv => kv.Value.Bytes).Take(top)
            .Select(kv => string.Create(ci, $"{kv.Key}:{kv.Value.Bytes / s:F0}/{kv.Value.Msgs / s:F1}")));
        long ob = o.Values.Sum(v => v.Bytes), om = o.Values.Sum(v => v.Msgs), ib = i.Values.Sum(v => v.Bytes), im = i.Values.Sum(v => v.Msgs);
        return string.Create(ci, $"MP-LINK {tag} who={who} window_s={s:F1} out_Bps={ob / s:F0} out_mps={om / s:F1} in_Bps={ib / s:F0} in_mps={im / s:F1} out={Part(o)} in={Part(i)}");
    }
}

/// <summary>
/// WO-213 A.5: the receiver's view of each kind -- the time between arrivals (per body for a state kind: one NPC's samples, one
/// peer's positions; per kind for the rest) and, for a sender-stamped kind, how much later than the fastest arrival seen a sample
/// arrived (its age past the link's best latency: queueing and jitter, independent of the two clocks). A rolling two-window minimum
/// per source, as the native writer's sender clock (npc_drive.cpp sender_stamp).
/// </summary>
public sealed class ArrivalStats
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<double>> _ia = new(StringComparer.Ordinal), _age = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _last = new(StringComparer.Ordinal);
    private readonly (double CurMin, double PrevMin, long CurStart, bool Init, bool HavePrev)[] _clock = new (double, double, long, bool, bool)[256];
    private readonly Dictionary<string, (long Gaps, double MaxGapMs)> _gaps = new(StringComparer.Ordinal);
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    /// <summary>A gap longer than this between two samples of one body is listed as a gap (A.6), not an inter-arrival.</summary>
    public const double GapMs = 1000.0;
    /// <summary>A state body's inter-arrival over this is an idle heartbeat or a silence, not the stream's pace (still a gap if over GapMs).</summary>
    public const double PaceCapMs = 1000.0;

    /// <summary>
    /// One arrival. <paramref name="body"/> null = the kind is its own stream (an event kind: no gaps counted); <paramref name="senderMs"/>
    /// 0 = no stamp; <paramref name="moved"/> = the body kept moving through the time since its last sample (the caller's rule) -- a gap
    /// over a second counts only then (a body that stood with its 2 s heartbeat, or stopped and walked on, is no silence of its stream).
    /// </summary>
    public void Arrive(string kind, string? body, long ts, uint senderMs = 0, byte src = 0, bool moved = true)
    {
        lock (_lock)
        {
            string key = body is null ? kind : kind + "|" + body;
            if (_last.TryGetValue(key, out long prev))
            {
                double ia = (ts - prev) * TicksToMs;
                if (ia > GapMs && body is not null && moved)
                {
                    var g = _gaps.TryGetValue(kind, out var gg) ? gg : (0, 0);
                    _gaps[kind] = (g.Gaps + 1, Math.Max(g.MaxGapMs, ia));
                }
                if (ia <= PaceCapMs || body is null) Add(_ia, kind, ia);
            }
            _last[key] = ts;
            if (senderMs != 0)
            {
                double now = ts * TicksToMs, off = now - senderMs;
                ref var c = ref _clock[src];
                if (!c.Init) c = (off, double.MaxValue, ts, true, false);
                if (off < c.CurMin) c.CurMin = off;
                if ((ts - c.CurStart) * TicksToMs > 30_000) { c.PrevMin = c.CurMin; c.HavePrev = true; c.CurMin = off; c.CurStart = ts; }
                double best = c.HavePrev ? Math.Min(c.CurMin, c.PrevMin) : c.CurMin;
                Add(_age, kind, Math.Max(0, off - best));
            }
        }
    }

    private static void Add(Dictionary<string, List<double>> d, string k, double v)
    {
        if (!d.TryGetValue(k, out var l)) d[k] = l = new List<double>(256);
        if (l.Count < 20_000) l.Add(v);
    }

    public static double Quantile(List<double> v, double q)
    {
        if (v.Count == 0) return double.NaN;
        v.Sort();
        return v[Math.Min(v.Count - 1, (int)Math.Round(q * (v.Count - 1)))];
    }

    /// <summary>The window's lines, one per kind with at least <paramref name="minSamples"/> arrivals; the window starts over.</summary>
    public IEnumerable<string> TakeLines(string who, int minSamples = 5)
    {
        Dictionary<string, List<double>> ia, age;
        Dictionary<string, (long Gaps, double MaxGapMs)> gaps;
        lock (_lock)
        {
            ia = new(_ia, StringComparer.Ordinal); age = new(_age, StringComparer.Ordinal); gaps = new(_gaps, StringComparer.Ordinal);
            _ia.Clear(); _age.Clear(); _gaps.Clear();
        }
        var ci = CultureInfo.InvariantCulture;
        foreach (var kv in ia.OrderByDescending(kv => kv.Value.Count))
        {
            if (kv.Value.Count < minSamples) continue;
            var a = age.TryGetValue(kv.Key, out var al) ? al : null;
            var g = gaps.TryGetValue(kv.Key, out var gg) ? gg : (0, 0);
            string head = string.Create(ci, $"MP-LINK arrival who={who} kind={kv.Key} n={kv.Value.Count} ia_p50={Quantile(kv.Value, 0.5):F0} ia_p90={Quantile(kv.Value, 0.9):F0} ia_p99={Quantile(kv.Value, 0.99):F0}");
            string ages = a is { Count: > 0 } ? string.Create(ci, $" age_p50={Quantile(a, 0.5):F0} age_p90={Quantile(a, 0.9):F0} age_p99={Quantile(a, 0.99):F0}") : "";
            yield return head + ages + string.Create(ci, $" gaps_over_1s={g.Gaps} gap_max_ms={g.MaxGapMs:F0}");
        }
    }
}
