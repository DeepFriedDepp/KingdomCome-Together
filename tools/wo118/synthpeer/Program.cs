// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-118 live test tool (tools/wo118, never shipped): a synthetic AUTHORITY peer.
//
// Connects to a real local relay FIRST (lowest id -> the authority when two
// loopback clients are present, relay rule 2), then streams NpcStateUp (0x26)
// for scripted NPCs exactly like the mod's emitter: 100 ms while moving
// (> 5 cm), a 2 s heartbeat when still, per-NPC seq, the sender's ms stamp.
// Optional network noise: base delay + uniform jitter + spikes, applied per
// packet AFTER the sender stamp (so reordering and bunching happen like a
// real link; TCP keeps the order the packets are handed to it).
//
// usage: SynthPeer --plan plan.txt [--host 127.0.0.1] [--port 7778] [--name synth-host]
//                  [--duration 60] [--emit-ms 100] [--delay-ms 0] [--jitter-ms 0]
//                  [--spike-pct 0] [--spike-ms 0] [--seed 1] [--sender-clock qpc|tick]
//                  [--ghost-sender-ms on|off]   (the ghost's Position carries its stamp, flag 0x08)
//                  [--version-file path]   (default: the first VERSION found walking up from
//                                           the working directory, then from this binary)
//        SynthPeer --join ...  | --join-host ...   (WO-123: the synthetic joiner / host, JoinPeer.cs)
//        SynthPeer --join-host125 ...               (WO-125: the continuity host, Host125.cs)
// plan lines:
//   line <npc> <x0> <y0> <z0> <ux> <uy> <len> <speed> [pingpong]
//   hold <npc> <x> <y> <z> <yaw> [drawn]                       (WO-132: drawn = the weapon-drawn bit 0x04)
//   dead <npc> <x> <y> <z> <yaw>                                (WO-122: a corpse: hold + dead bit 0x01, hp 0)
//   saved <t> <kind> <playline> <idx>                           (WO-122: a WorldSaved 0x46 at stream time t, as the host)
//   path <npc> <speed> <x1> <y1> <z1> <x2> <y2> <z2> ...        (ping-pong)
//   timed <npc> <yaw> t0 x0 y0 z0 t1 x1 y1 z1 ...              (piecewise linear in time)
//   fight <npc> <cx> <cy> <cz> <r> [phase]   (circles; flag 0x04 drawn, a 0x08 swing cue every 2.5 s; WO-165 phase in radians)
//   ghost <x0> <y0> <z0> <ux> <uy> <len> <speed> [ms]          (a player position stream, ping-pong)
//   gcircle <cx> <cy> <cz> <r> <period_s> [ms] / gspin <x> <y> <z> <period_s> [ms]   (WO-171: the ghost walks a circle / turns on the spot, clockwise)
//   ride <t0_s> <t1_s>                                         (WO-124 6a: the ghost rides between t0 and t1)
//   start <seconds>                                             (delay before streaming)
//   die <t_s> <npc>                                             (WO-131: that mover streams dead, hp 0, from t on)
//   hp <t_s> <npc> <hp>                                         (WO-131: that mover streams this hp from t on)
//   ncombat <t0_s> <t1_s> <npc> <host|avatar:N|other|none> [gz] [gs] [block] [atk]
//                                                               (WO-132: the host NPC's combat state, NpcCombat on the
//                                                                action channel, every 1 s from t0 to t1 with combat on,
//                                                                then one "off"; zones are table ids, -1 none)
//   swingat / verdict (pb|broken, hid=N) / avatarfight / flee / teleport   (WO-165: Wo165Verbs.cs; --selftest-wo165 checks their packets)
//   gcombat <t0_s> <t1_s> [gz]                                  (WO-166 C1: the ghost's Position carries a state block with the
//                                                                combat bit between t0 and t1 -- the joiner "in combat", which
//                                                                holds its figure in combat on the host)
//   jattack <t_s> <rowGuid>                                    (WO-170: joiner role -- this peer's figure swings: an Attack action stamped when it is
//                                                                sent, as the joiner's game sends one)
//   jhit <t_s> <npc> <rowGuid> <lag_ms> <hp> <st> [every_s n]  (WO-173: joiner role -- a swing (jattack) and, lag_ms later, its attributed
//                                                                hit on the host's NPC (avatarfight's packet); every_s/n repeat it n times)
//   ko <t0_s> <t1_s> <npc>                                     (WO-173: that mover streams the knocked-out bit 0x02 between t0 and t1)
//   raw <t_s> <typeHex> <payloadHex> [stampOffset]              (WO-131: send this packet as-is at stream time t;
//                                                                 the 4 bytes at stampOffset are re-stamped with this
//                                                                 peer's clock -- a recorded host stream replayed,
//                                                                 tools/wo131/replay.py)
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using KcdMp.Wire;

static class P
{
    static string Arg(string[] a, string k, string d) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : d; }
    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    sealed class NCombat
    {
        public double T0, T1, LastSent = -1e9; public string Npc = ""; public NpcCombatTarget Target; public byte Ghost;
        public int Gz, Gs = -1, Atk = -1; public bool Block, OffSent, Logged;
    }

    abstract class Mover
    {
        public string Name = "";
        public ushort Seq;
        public double LastSent = -1e9, LastSentX, LastSentY, LastSentZ;
        public bool SentDead; public float SentHp = 100f;   // WO-131
        public bool SentKo;   // WO-173
        public double T0 = double.NegativeInfinity, T1 = double.PositiveInfinity;   // WO-167: `window` -- streamed only inside [T0, T1)
        public abstract (float x, float y, float z, float yaw) At(double t);
    }
    sealed class Line : Mover
    {
        public float X0, Y0, Z0, Ux, Uy, Len, Speed; public bool PingPong;
        public override (float, float, float, float) At(double t)
        {
            double s = Speed * t;
            double dir = 1;
            if (PingPong && Len > 0)
            {
                double period = 2 * Len;
                double m = s % period;
                if (m <= Len) { s = m; dir = 1; } else { s = period - m; dir = -1; }
            }
            else if (s > Len) s = Len;
            float yaw = (float)Math.Atan2(-Ux * dir, Uy * dir);
            return ((float)(X0 + Ux * s), (float)(Y0 + Uy * s), Z0, yaw);
        }
    }
    // WO-171: `gcircle <cx> <cy> <cz> <r> <period_s> [ms]` -- the ghost walks a circle clockwise (seen from above, compass heading =
    // its position angle + 90 degrees); `gspin <x> <y> <z> <period_s> [ms]` -- it stands and turns clockwise through all 360 degrees.
    sealed class Circle : Mover
    {
        public float Cx, Cy, Cz, R, Period; public bool Spin;
        public override (float, float, float, float) At(double t)
        {
            double a = 2 * Math.PI * t / Math.Max(0.5, Period);                  // compass angle, clockwise from north
            if (Spin) return (Cx, Cy, Cz, (float)(-a));                         // the engine's yaw is the negative of the compass heading
            return ((float)(Cx + R * Math.Sin(a)), (float)(Cy + R * Math.Cos(a)), Cz, (float)(-(a + Math.PI / 2)));
        }
    }
    sealed class Hold : Mover
    {
        public float X, Y, Z, Yaw; public bool Dead, Drawn;
        public override (float, float, float, float) At(double t) => (X, Y, Z, Yaw);
    }
    sealed class PathM : Mover
    {
        public List<(float x, float y, float z)> Pts = new(); public float Speed; double[] cum = [];
        public void Init() { cum = new double[Pts.Count]; for (int i = 1; i < Pts.Count; i++) { var a = Pts[i - 1]; var b = Pts[i]; cum[i] = cum[i - 1] + Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y)); } }
        public override (float, float, float, float) At(double t)
        {
            double L = cum[^1]; double s = Speed * t; double period = 2 * L; double m = L > 0 ? s % period : 0; int dir = 1;
            if (m > L) { m = period - m; dir = -1; }
            int i = 1; while (i < Pts.Count - 1 && cum[i] < m) i++;
            var a = Pts[i - 1]; var b = Pts[i]; double seg = cum[i] - cum[i - 1]; double u = seg > 0 ? (m - cum[i - 1]) / seg : 0;
            float dx = (b.x - a.x) * dir, dy = (b.y - a.y) * dir;
            float yaw = (float)Math.Atan2(-dx, dy);
            return ((float)(a.x + (b.x - a.x) * u), (float)(a.y + (b.y - a.y) * u), (float)(a.z + (b.z - a.z) * u), yaw);
        }
    }

    // timed <npc> <yaw> t0 x0 y0 z0 t1 x1 y1 z1 ...  -- piecewise linear in time, holds at the ends
    sealed class Timed : Mover
    {
        public List<(double t, float x, float y, float z)> K = new(); public float Yaw;
        public override (float, float, float, float) At(double t)
        {
            if (t <= K[0].t) return (K[0].x, K[0].y, K[0].z, Yaw);
            for (int i = 1; i < K.Count; i++)
            {
                if (t <= K[i].t)
                {
                    var a = K[i - 1]; var b = K[i]; double u = (t - a.t) / Math.Max(1e-6, b.t - a.t);
                    float dx = b.x - a.x, dy = b.y - a.y;
                    float yaw = (dx * dx + dy * dy) > 1e-6 ? (float)Math.Atan2(-dx, dy) : Yaw;
                    return ((float)(a.x + dx * u), (float)(a.y + dy * u), (float)(a.z + (b.z - a.z) * u), yaw);
                }
            }
            var l = K[^1]; return (l.x, l.y, l.z, Yaw);
        }
    }

    // fight <npc> <cx> <cy> <cz> <radius> : strafes a circle at 1.2 m/s facing the centre, weapon drawn, a swing cue every 2.5 s
    sealed class Fight : Mover
    {
        public float Cx, Cy, Cz, R, Phase;   // WO-165: Phase (radians) spreads several fighters round one centre
        public float[]? Zs;                  // WO-165 fightz: the ground height every 360/n degrees round the circle (no floating on a slope)
        public override (float, float, float, float) At(double t)
        {
            double w = 1.2 / Math.Max(0.5, R);
            double a = Phase + w * t + 0.4 * Math.Sin(t * 0.9);
            float x = (float)(Cx + R * Math.Cos(a)), y = (float)(Cy + R * Math.Sin(a));
            float yaw = (float)Math.Atan2(-(Cx - x), Cy - y);
            return (x, y, Zs is { Length: > 0 } ? Wo165Verbs.RingZ(Zs, a) : Cz, yaw);
        }
        public byte FlagsAt(double t) => (byte)(0x04 | (((int)(t / 2.5)) != (int)((t - 0.1) / 2.5) ? 0x08 : 0));
    }

    internal static byte[] BuildUp(string npc, float x, float y, float z, float rot, float hp, byte flags, ushort seq, uint ms)
    {
        var nb = Encoding.UTF8.GetBytes(npc);
        int payloadLen = 1 + nb.Length + Protocol.NpcStateFixedTail;
        var p = new byte[3 + payloadLen];
        p[0] = Protocol.NpcStateUp;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), (ushort)payloadLen);
        p[3] = (byte)nb.Length; nb.CopyTo(p, 4);
        int o = 4 + nb.Length;
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o), x);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 8), z);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 12), rot);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 16), hp);
        p[o + Protocol.NpcStateFlagsOffset] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + Protocol.NpcStateSeqOffset), seq);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(o + Protocol.NpcStateSenderMsOffset), ms);
        return p;
    }

    /// <summary>WO-167: a W164 message this peer received, as one line (null = not one).</summary>
    static string? W164Note(byte[] payload)
    {
        if (!Protocol.TrySplitJoinDown(payload, out byte src, out _, out _, out var body) || !LootMsg.TryDecode(body, out var m)) return null;
        return $"SYNTH got W164 from={src} kind={Protocol.W164KindName(m.Kind)} {m.Text}";
    }

    /// <summary>WO-200: a QuestHost message this peer received (a replay's ack, an item count), as one line.</summary>
    static string? QuestHostNote(byte[] payload)
    {
        if (!Protocol.TrySplitJoinDown(payload, out byte src, out _, out _, out var body) || !LootMsg.TryDecode(body, out var m)) return null;
        return $"SYNTH got QuestHost from={src} kind={Protocol.QuestHostName(m.Kind)} {m.Text}";
    }

    static async Task<(byte, byte[])> ReadPacket(NetworkStream s, CancellationToken ct)
    {
        var h = new byte[3]; await ReadExact(s, h, ct);
        int len = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(1));
        var b = new byte[len]; await ReadExact(s, b, ct);
        return (h[0], b);
    }
    static async Task ReadExact(NetworkStream s, byte[] b, CancellationToken ct)
    {
        int got = 0; while (got < b.Length) { int n = await s.ReadAsync(b.AsMemory(got), ct); if (n <= 0) throw new IOException("closed"); got += n; }
    }

    static string FindUp(string file)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
            {
                var f = Path.Combine(d.FullName, file);
                if (File.Exists(f)) return f;
            }
        return file;
    }

    static readonly KcdMp.Client.ActionOutbox s_actions = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> s_npcIn = new();             // the WO-191 follow-up
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ActivityState> s_actIn = new();

    static async Task<int> Main(string[] a)
    {
        string host = Arg(a, "--host", "127.0.0.1"); int port = int.Parse(Arg(a, "--port", "7778"));
        string name = Arg(a, "--name", "synth-host"); double duration = double.Parse(Arg(a, "--duration", "60"), CultureInfo.InvariantCulture);
        int emitMs = int.Parse(Arg(a, "--emit-ms", "100"));
        double delayMs = double.Parse(Arg(a, "--delay-ms", "0"), CultureInfo.InvariantCulture);
        double jitterMs = double.Parse(Arg(a, "--jitter-ms", "0"), CultureInfo.InvariantCulture);
        double spikePct = double.Parse(Arg(a, "--spike-pct", "0"), CultureInfo.InvariantCulture);
        double spikeMs = double.Parse(Arg(a, "--spike-ms", "0"), CultureInfo.InvariantCulture);
        var rng = new Random(int.Parse(Arg(a, "--seed", "1")));
        // qpc = what the agent sends since WO-118 (1 ms QPC time); tick = the 15.6 ms
        // Environment.TickCount64 stamps of earlier agents.
        string senderClock = Arg(a, "--sender-clock", "qpc");
        // The agent stamps every Position since the WO-118 follow-up; off = an older sender.
        bool ghostSenderMs = Arg(a, "--ghost-sender-ms", "on") != "off";
        // WO-131: the ghost's Position carries the host-claim bit (0x40): against a real agent that
        // claims host in its own world, the relay's tie-break (lowest id) keeps this peer the authority.
        bool claimHost = a.Contains("--claim-host");
        if (a.Contains("--selftest-wo165")) return Wo165Verbs.SelfTest() == 0 ? 0 : 1;
        string versionFile = Arg(a, "--version-file", FindUp("VERSION"));
        string release = File.ReadAllText(versionFile).Trim();
        // WO-123: the synthetic joiner and the transfer-ceiling host (JoinPeer.cs).
        if (a.Contains("--join-host125")) return await Host125.RunAsync(a, release);   // WO-125: the continuity host (Host125.cs)
        if (a.Contains("--join")) return await JoinPeer.RunJoinerAsync(a, release);
        if (a.Contains("--join-host")) return await JoinPeer.RunHostAsync(a, release);

        var movers = new List<Mover>(); double startDelay = 0; Mover? ghost = null; int ghostMs = 30;
        var rides = new List<(double T0, double T1)>();
        // WO-121: `row <t_s> <npc> <rowGuid>` -- the host NPC committed that
        // attack row at stream time t: an NpcAttack action event (v8).
        var rows = new List<(double T, string Npc, Guid Row)>();
        var reacts = new List<(double T, string Npc, Guid Row)>();   // WO-187 C1: react <t> <npc> <rowGuid> -- the host NPC's hit reaction (NpcHit)
        var verdicts = new List<(double T, HitVerdict V, float Hp, float St, string Npc, uint Swing, byte Tgt, uint Id)>();   // WO-163: plan verb `verdict`
        uint verdictId = 0;
        var saves = new List<(double T, byte Kind, byte Playline, ushort Idx)>(); uint wsSeq = 0;
        var raws = new List<(double T, byte Type, byte[] Body, int Stamp)>();   // WO-131
        // WO-164: `act <t> <npc|-player> <stance> <stanceObjHex|0> <unstanceId|-> <unstObjHex|0> [night]` (a host activity row),
        // `w164 <t> <kind> <text...>` (a W164 message), `qcp <t> <value> <path>` (a host quest checkpoint of one State)
        // WO-167: `w164h <t> <kind> <text...>` -- a W164 message to the host (this peer as a joiner: the target 0xFF);
        //         `qconv <t> on|off <npc>` -- the host's conversation with an NPC (QuestHostConverse, WO-160);
        //         `hands <t> <npc> <left|-> <right|->` -- the host NPC's hand rows (WO-143 ExtraKindHands: item classes);
        //         `qmode <t> on|off` -- the host's quest-sync mode (QuestHostMode, WO-137), as a real host announces it;
        //         `window <npc> <t0> <t1>` -- a mover declared earlier is streamed only between t0 and t1 (a spawn that appears, then leaves)
        var joinMsgs = new List<(double T, byte[] Pkt, string Note)>();
        var jattacks = new List<(double T, Guid Row)>();   // WO-170
        var jhits = new List<(double T, string Npc, float Hp, float St)>();   // WO-173: the swing's attributed hit
        uint joinTok = 0;
        var ncombats = new List<NCombat>();   // WO-132
        var dies = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);   // WO-131
        var kos = new Dictionary<string, List<(double T0, double T1)>>(StringComparer.OrdinalIgnoreCase);   // WO-173
        var hps = new Dictionary<string, List<(double T, float Hp)>>(StringComparer.OrdinalIgnoreCase);   // WO-131
        var fights = new List<(double T0, double T1, string Npc, double Every, float Hp, float St)>();       // WO-165 avatarfight
        var lastFight = new Dictionary<int, double>();
        var tports = new Dictionary<string, List<(double T, float Dx, float Dy)>>(StringComparer.OrdinalIgnoreCase);   // WO-165 teleport
        var gcombats = new List<(double T0, double T1, int Gz)>();   // WO-166 C1
        Wo187Verbs.Echo? echo = null;                                 // WO-187 C3: the echo partner
        var fmoves = new List<(double T, string What, Guid Row)>();   // WO-187 C3: the figure's block / dodge / attack rows
        uint swingSeq = 0;
        foreach (var raw in File.ReadAllLines(Arg(a, "--plan", "plan.txt")))
        {
            var t = raw.Trim(); if (t.Length == 0 || t.StartsWith('#')) continue;
            var f = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (f[0])
            {
                case "line": movers.Add(new Line { Name = f[1], X0 = F(f[2]), Y0 = F(f[3]), Z0 = F(f[4]), Ux = F(f[5]), Uy = F(f[6]), Len = F(f[7]), Speed = F(f[8]), PingPong = f.Length > 9 && f[9] == "pingpong" }); break;
                case "hold": movers.Add(new Hold { Name = f[1], X = F(f[2]), Y = F(f[3]), Z = F(f[4]), Yaw = F(f[5]), Drawn = f.Length > 6 && f[6] == "drawn" }); break;
                case "dead": movers.Add(new Hold { Name = f[1], X = F(f[2]), Y = F(f[3]), Z = F(f[4]), Yaw = F(f[5]), Dead = true }); break;
                case "path":
                {
                    var pm = new PathM { Name = f[1], Speed = F(f[2]) };
                    for (int i = 3; i + 2 < f.Length; i += 3) pm.Pts.Add((F(f[i]), F(f[i + 1]), F(f[i + 2])));
                    pm.Init(); movers.Add(pm); break;
                }
                case "timed":
                {
                    var tm = new Timed { Name = f[1], Yaw = F(f[2]) };
                    for (int i = 3; i + 3 < f.Length; i += 4) tm.K.Add((double.Parse(f[i], CultureInfo.InvariantCulture), F(f[i + 1]), F(f[i + 2]), F(f[i + 3])));
                    movers.Add(tm); break;
                }
                case "fightz":   // WO-165: fightz <npc> <cx> <cy> <r> <phase> <z0> <z1> ... (ground heights round the circle, from angle 0)
                    movers.Add(new Fight { Name = f[1], Cx = F(f[2]), Cy = F(f[3]), R = F(f[4]), Phase = F(f[5]), Zs = f.Skip(6).Select(F).ToArray() });
                    break;
                case "fight": movers.Add(new Fight { Name = f[1], Cx = F(f[2]), Cy = F(f[3]), Cz = F(f[4]), R = F(f[5]), Phase = f.Length > 6 ? F(f[6]) : 0f }); break;
                case "ghost":
                    ghost = new Line { Name = "ghost", X0 = F(f[1]), Y0 = F(f[2]), Z0 = F(f[3]), Ux = F(f[4]), Uy = F(f[5]), Len = F(f[6]), Speed = F(f[7]), PingPong = true };
                    ghostMs = f.Length > 8 ? int.Parse(f[8]) : 30;
                    break;
                case "gcircle":
                    ghost = new Circle { Name = "ghost", Cx = F(f[1]), Cy = F(f[2]), Cz = F(f[3]), R = F(f[4]), Period = F(f[5]) };
                    ghostMs = f.Length > 6 ? int.Parse(f[6]) : 30;
                    break;
                case "gspin":
                    ghost = new Circle { Name = "ghost", Cx = F(f[1]), Cy = F(f[2]), Cz = F(f[3]), Period = F(f[4]), Spin = true };
                    ghostMs = f.Length > 5 ? int.Parse(f[5]) : 30;
                    break;
                // WO-124 (6a): `ride <t0_s> <t1_s>` -- the ghost's Position carries the
                // riding flag between t0 and t1 of the stream (mount, ride, dismount).
                case "ride":
                    rides.Add((F(f[1]), F(f[2])));
                    break;
                case "start": startDelay = double.Parse(f[1], CultureInfo.InvariantCulture); break;
                case "row": rows.Add((double.Parse(f[1], CultureInfo.InvariantCulture), f[2], Guid.Parse(f[3]))); break;
                case "react": reacts.Add((double.Parse(f[1], CultureInfo.InvariantCulture), f[2], Guid.Parse(f[3]))); break;   // WO-187 C1
                case "verdict":   // verdict <t> <hit|blocked|pb|broken> <hp> <st> <npc|-> [swing=0] [joinerId=1] [hid=N]  (WO-161 0x72; WO-165 values and ids)
                {
                    if (!Wo165Verbs.TryVerdict(f[2], out var vv)) throw new FormatException($"verdict: unknown value {f[2]}");
                    var pos = f.Skip(6).Where(x => !x.StartsWith("hid=", StringComparison.Ordinal)).ToArray();
                    var hidTok = f.FirstOrDefault(x => x.StartsWith("hid=", StringComparison.Ordinal));
                    uint vid = hidTok is not null ? uint.Parse(hidTok[4..], CultureInfo.InvariantCulture) : ++verdictId;
                    verdicts.Add((double.Parse(f[1], CultureInfo.InvariantCulture), vv,
                                  float.Parse(f[3], CultureInfo.InvariantCulture), float.Parse(f[4], CultureInfo.InvariantCulture), f[5] == "-" ? "" : f[5],
                                  pos.Length > 0 ? uint.Parse(pos[0], CultureInfo.InvariantCulture) : 0u, pos.Length > 1 ? byte.Parse(pos[1], CultureInfo.InvariantCulture) : (byte)1, vid));
                    break;
                }
                case "swingat":   // WO-165: swingat <t> <npc> local <rowGuid> <lag_ms> <hit|blocked|pb|broken> <hp> <st> [joinerId=1]
                {
                    if (f[3] != "local") throw new FormatException("swingat <t> <npc> local ...");
                    if (!Wo165Verbs.TryVerdict(f[6], out var sv)) throw new FormatException($"swingat: unknown verdict {f[6]}");
                    double t0 = Wo165Verbs.D(f[1]);
                    rows.Add((t0, f[2], Guid.Parse(f[4])));
                    uint sid = ++swingSeq;
                    verdicts.Add((t0 + Wo165Verbs.D(f[5]) / 1000.0, sv, Wo165Verbs.F(f[7]), Wo165Verbs.F(f[8]), f[2], sid,
                                  f.Length > 9 ? byte.Parse(f[9], CultureInfo.InvariantCulture) : (byte)1, ++verdictId));
                    break;
                }
                case "avatarfight":   // WO-165: avatarfight <t0> <t1> <npc> <every_s> <hp> <st>
                    fights.Add((Wo165Verbs.D(f[1]), Wo165Verbs.D(f[2]), f[3], Wo165Verbs.D(f[4]), Wo165Verbs.F(f[5]), Wo165Verbs.F(f[6])));
                    break;
                case "flee":   // WO-165: flee <t0> <npc> <x0> <y0> <z0> <ux> <uy> <speed> <len>
                {
                    var tm = new Timed { Name = f[2], Yaw = 0 };
                    tm.K.AddRange(Wo165Verbs.FleeKeys(Wo165Verbs.D(f[1]), F(f[3]), F(f[4]), F(f[5]), F(f[6]), F(f[7]), F(f[8]), F(f[9])));
                    movers.Add(tm);
                    break;
                }
                case "teleport":   // WO-165: teleport <t> <npc|ghost> <dx> <dy>
                    if (!tports.TryGetValue(f[2], out var tl)) tports[f[2]] = tl = new();
                    tl.Add((Wo165Verbs.D(f[1]), F(f[3]), F(f[4])));
                    break;
                case "gcombat":   // WO-166 C1: gcombat <t0> <t1> [gz]
                    gcombats.Add((double.Parse(f[1], CultureInfo.InvariantCulture), double.Parse(f[2], CultureInfo.InvariantCulture),
                                  f.Length > 3 ? int.Parse(f[3], CultureInfo.InvariantCulture) : 1));
                    break;
                case "saved": saves.Add((double.Parse(f[1], CultureInfo.InvariantCulture), byte.Parse(f[2]), byte.Parse(f[3]), ushort.Parse(f[4]))); break;
                case "die": dies[f[2]] = double.Parse(f[1], CultureInfo.InvariantCulture); break;
                case "ko":   // WO-173: ko <t0> <t1> <npc> -- that mover streams the knocked-out bit (0x02) between t0 and t1
                    if (!kos.TryGetValue(f[3], out var kl)) kos[f[3]] = kl = new();
                    kl.Add((double.Parse(f[1], CultureInfo.InvariantCulture), double.Parse(f[2], CultureInfo.InvariantCulture)));
                    break;
                case "hp":
                    if (!hps.TryGetValue(f[2], out var hl2)) hps[f[2]] = hl2 = new();
                    hl2.Add((double.Parse(f[1], CultureInfo.InvariantCulture), F(f[3]))); break;
                case "ncombat":
                {
                    var tg = f[4].Split(':');
                    var nc = new NCombat
                    {
                        T0 = double.Parse(f[1], CultureInfo.InvariantCulture), T1 = double.Parse(f[2], CultureInfo.InvariantCulture), Npc = f[3],
                        Target = tg[0] switch { "host" => NpcCombatTarget.Host, "avatar" => NpcCombatTarget.Avatar, "other" => NpcCombatTarget.Other, _ => NpcCombatTarget.None },
                        Ghost = tg.Length > 1 ? byte.Parse(tg[1], CultureInfo.InvariantCulture) : (byte)0,
                        Gz = f.Length > 5 ? int.Parse(f[5], CultureInfo.InvariantCulture) : 0, Gs = f.Length > 6 ? int.Parse(f[6], CultureInfo.InvariantCulture) : -1,
                        Block = f.Length > 7 && f[7] == "1", Atk = f.Length > 8 ? int.Parse(f[8], CultureInfo.InvariantCulture) : -1,
                    };
                    ncombats.Add(nc);
                    break;
                }
                case "act":
                {
                    bool player = f[2] == "-player";
                    ushort un = f[5] == "-" ? ActivityState.NoUnstance : ushort.Parse(f[5], CultureInfo.InvariantCulture);
                    byte fl = f.Length > 7 && f[7] == "night" ? ActivityState.FlagNightDress : (byte)0;
                    var actSt = new ActivityState(byte.Parse(f[3], CultureInfo.InvariantCulture), 0, Convert.ToUInt64(f[4], 16), un, Convert.ToUInt64(f[6], 16),
                                               ActivityState.NoMinigame, 0, fl);
                    var rowsA = new List<ActivityRow> { new(0, player ? "" : f[2], actSt) };
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture),
                                  ActivityCodec.BuildUp(Protocol.ActivityHostUp, 1, player ? Protocol.ActivityKindPlayer : Protocol.ActivityKindNpc, rowsA),
                                  $"ACT {f[2]} {actSt}"));
                    break;
                }
                case "w164":
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture),
                                  new LootMsg(byte.Parse(f[2], CultureInfo.InvariantCulture), ++joinTok, string.Join(' ', f.Skip(3))).BuildUp(Protocol.W164Up, 1),
                                  $"W164 kind={f[2]} {string.Join(' ', f.Skip(3))}"));
                    break;
                case "window":   // WO-167: a mover streamed only inside [t0, t1)
                    foreach (var mw in movers.Where(mv => mv.Name == f[1])) { mw.T0 = double.Parse(f[2], CultureInfo.InvariantCulture); mw.T1 = double.Parse(f[3], CultureInfo.InvariantCulture); }
                    break;
                case "qmode":   // WO-167: the host's quest-sync mode (a real host announces it at once)
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture),
                                  new LootMsg(Protocol.QuestHostMode, ++joinTok, $"{f[2]} synthetic-host").BuildUp(Protocol.QuestHostUp, 1),
                                  $"QMODE {f[2]}"));
                    break;
                case "hands":   // WO-167 C2: the host NPC's hands (the item classes it holds)
                {
                    var er = new ExtraRow { Name = f[2], Left = f[3] == "-" ? new byte[16] : ExtraRow.ClassBytes(f[3]), Right = f[4] == "-" ? new byte[16] : ExtraRow.ClassBytes(f[4]) };
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture), ExtraCodec.BuildUp(1, Protocol.ExtraKindHands, new List<ExtraRow> { er }),
                                  $"HANDS {f[2]} left={f[3]} right={f[4]}"));
                    break;
                }
                case "qconv":   // WO-167 T2: the host talks to an NPC / is done with it
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture),
                                  new LootMsg(Protocol.QuestHostConverse, ++joinTok, $"{f[2]} {f[3]}").BuildUp(Protocol.QuestHostUp, 1),
                                  $"QCONV {f[2]} {f[3]}"));
                    break;
                case "w164h":   // WO-167: a joiner's W164 message to the host
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture),
                                  new LootMsg(byte.Parse(f[2], CultureInfo.InvariantCulture), ++joinTok, string.Join(' ', f.Skip(3))).BuildUp(Protocol.W164Up, Protocol.JoinTargetHost),
                                  $"W164->host kind={f[2]} {string.Join(' ', f.Skip(3))}"));
                    break;
                case "mount":   // WO-200 II.2: mount <t> <npc> <horse|-> -- the host's NPC is on that horse (or on foot)
                {
                    var er = new ExtraRow { Name = f[2], Target = f[3] == "-" ? "" : f[3] };
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture), ExtraCodec.BuildUp(1, Protocol.ExtraKindMount, new List<ExtraRow> { er }),
                                  $"MOUNT {f[2]} {f[3]}"));
                    break;
                }
                case "vitals":  // WO-200 II.1: vitals <t> <npc> <hp> <st> -- the host's own health and stamina of its NPC
                {
                    var er = new ExtraRow { Name = f[2], Health = F(f[3]), Stamina = F(f[4]) };
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture), ExtraCodec.BuildUp(1, Protocol.ExtraKindVitals, new List<ExtraRow> { er }),
                                  $"VITALS {f[2]} hp={f[3]} st={f[4]}"));
                    break;
                }
                case "qask":    // WO-200: qask <t> <kind> <text...> -- a joiner's QuestAsk to the host (1 request, 5 out-port replay)
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture),
                                  new LootMsg(byte.Parse(f[2], CultureInfo.InvariantCulture), ++joinTok, string.Join(' ', f.Skip(3))).BuildUp(Protocol.QuestAskUp, Protocol.JoinTargetHost),
                                  $"QASK->host kind={f[2]} {string.Join(' ', f.Skip(3))}"));
                    break;
                case "qhost":   // WO-200: qhost <t> <kind> <text...> -- the host's QuestHost to the joiner (1 change, 7 replay ack, 8 item count)
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture),
                                  new LootMsg(byte.Parse(f[2], CultureInfo.InvariantCulture), ++joinTok, string.Join(' ', f.Skip(3))).BuildUp(Protocol.QuestHostUp, 1),
                                  $"QHOST kind={f[2]} {string.Join(' ', f.Skip(3))}"));
                    break;
                case "jattack": jattacks.Add((double.Parse(f[1], CultureInfo.InvariantCulture), Guid.Parse(f[2]))); break;   // WO-170
                case "echo": echo = new Wo187Verbs.Echo { DelayMs = double.Parse(f[1], CultureInfo.InvariantCulture) * 1000.0, Ahead = double.Parse(f[2], CultureInfo.InvariantCulture) }; break;   // WO-187 C3
                case "fmove": fmoves.Add((double.Parse(f[1], CultureInfo.InvariantCulture), f[2], Guid.Parse(f[3]))); break;   // WO-187 C3
                case "fclinch":   // WO-197 B3: fclinch <t> <masterRow> <slaveRow> <master 0|1> -- a clinch move of his, both rows
                    fmoves.Add((double.Parse(f[1], CultureInfo.InvariantCulture), "fclinch" + f[4] + ":" + Guid.Parse(f[3]).ToString("N"), Guid.Parse(f[2]))); break;
                case "pvit":   // WO-197 B4: pvit <t> <hp> <st> <flagsHex> -- this peer's PlayerState (0x1F) at t, then every 2 s with the same flags
                    fmoves.Add((double.Parse(f[1], CultureInfo.InvariantCulture), "pvit:" + f[2] + ":" + f[3] + ":" + f[4], Guid.Empty)); break;
                case "jhit":   // WO-173: jhit <t> <npc> <row> <lag_ms> <hp> <st> [every_s n]
                {
                    double t0 = Wo165Verbs.D(f[1]), lag = Wo165Verbs.D(f[4]) / 1000.0;
                    double every = f.Length > 7 ? Wo165Verbs.D(f[7]) : 1.0;
                    int reps = f.Length > 8 ? int.Parse(f[8], CultureInfo.InvariantCulture) : 1;
                    for (int ri = 0; ri < reps; ri++)
                    {
                        jattacks.Add((t0 + ri * every, Guid.Parse(f[3])));
                        jhits.Add((t0 + ri * every + lag, f[2], Wo165Verbs.F(f[5]), Wo165Verbs.F(f[6])));
                    }
                    break;
                }
                case "qcp":
                    joinMsgs.Add((double.Parse(f[1], CultureInfo.InvariantCulture),
                                  new LootMsg(Protocol.QuestHostCheckpoint, ++joinTok, $"1 1 {f[2]}:-:{f[3]}").BuildUp(Protocol.QuestHostUp, 1),
                                  $"QCP {f[3]}={f[2]}"));
                    break;
                case "raw": raws.Add((double.Parse(f[1], CultureInfo.InvariantCulture), Convert.ToByte(f[2], 16), Convert.FromHexString(f[3] == "-" ? "" : f[3]), f.Length > 4 ? int.Parse(f[4]) : -1)); break;
            }
        }

        raws.Sort((p, q) => p.T.CompareTo(q.T));
        using var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(host, port);
        var st = tcp.GetStream();
        var nb = Encoding.UTF8.GetBytes(name); var rb = Encoding.UTF8.GetBytes(release);
        int hl = 2 + nb.Length + rb.Length; var hs = new byte[3 + hl];
        hs[0] = Protocol.Handshake; BinaryPrimitives.WriteUInt16LittleEndian(hs.AsSpan(1), (ushort)hl);
        hs[3] = Protocol.Version; hs[4] = (byte)nb.Length; nb.CopyTo(hs, 5); rb.CopyTo(hs, 5 + nb.Length);
        await st.WriteAsync(hs);
        var (ty, pl) = await ReadPacket(st, CancellationToken.None);
        if (ty != Protocol.Ack) { Console.WriteLine($"SYNTH refused: 0x{ty:X2} {Encoding.UTF8.GetString(pl)}"); return 1; }
        Console.WriteLine($"SYNTH connected id={pl[0]} release={release} npcs={movers.Count} emit={emitMs}ms delay={delayMs} jitter={jitterMs} spike={spikePct}%/{spikeMs}ms");
        if (echo is not null) { echo.MyId = pl[0]; Console.WriteLine(FormattableString.Invariant($"SYNTH echo partner: every other peer's stream back {echo.DelayMs / 1000.0:F1} s late, {echo.Ahead:F1} m in front of him")); }
        var echoClock = Stopwatch.StartNew();

        using var cts = new CancellationTokenSource();
        long rx = 0;
        var reader = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var (rt, rb2) = await ReadPacket(st, cts.Token); rx++;
                    if (echo is not null && rt == Protocol.Ghost) Wo187Verbs.OnGhost(echo, rb2, echoClock.Elapsed.TotalMilliseconds);            // WO-187 C3
                    else if (echo is not null && rt == Protocol.ActionDown) Wo187Verbs.OnAction(echo, s_actions, rb2, echoClock.Elapsed.TotalMilliseconds);
                    // WO-187 C1: another player's own hit reaction, as his partner receives it ([src][kind][seq:2][phase][gen:4][len][body])
                    if (rt == Protocol.ActionDown && rb2.Length >= 10 && rb2[1] == (byte)ActionKind.AvatarHit && rb2.Length >= 10 + rb2[9]
                        && RowEvent.TryFromBytes(rb2.AsSpan(10, rb2[9]).ToArray(), out var ahe))
                        Console.WriteLine($"SYNTH got AvatarHit from={rb2[0]} row={ahe.Row}");
                    // WO-122: the host's world-save announcement, as a joiner receives it.
                    if (rt == Protocol.WorldSavedDown && WorldSaved.TryDecode(rb2, down: true, out byte wsrc) is WorldSaved ws)
                        Console.WriteLine(FormattableString.Invariant(
                            $"SYNTH WorldSaved from={wsrc} file=playline{ws.Playline}/{ws.FileName} seq={ws.Seq} md5={Convert.ToHexString(ws.Md5)[..8].ToLowerInvariant()} age_ms={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ws.SenderUnixMs}"));
                    else if (rt == Protocol.NpcDamageDown && rb2.Length >= 3 && rb2.Length >= 2 + rb2[1] + 9)
                    {
                        // WO-131: a joiner's forwarded hit, as the host would receive it.
                        string dn = Encoding.UTF8.GetString(rb2, 2, rb2[1]);
                        int o = 2 + rb2[1];
                        Console.WriteLine(FormattableString.Invariant(
                            $"SYNTH got hit from={rb2[0]} npc={dn} st={BinaryPrimitives.ReadSingleLittleEndian(rb2.AsSpan(o)):F1} hp={BinaryPrimitives.ReadSingleLittleEndian(rb2.AsSpan(o + 4)):F1} flags=0x{rb2[o + 8]:X2}"));
                    }
                    else if (rt == Protocol.CombatRole && rb2.Length == 1)
                        Console.WriteLine($"SYNTH role={(rb2[0] == 1 ? "authority (host)" : "not the authority (joiner)")}");
                    else if (rt == Protocol.W164Down && W164Note(rb2) is string w164Note)
                        Console.WriteLine(w164Note);   // WO-167
                    else if (rt == Protocol.QuestHostDown && QuestHostNote(rb2) is string qhNote)
                        Console.WriteLine(qhNote);   // WO-200: acks, item counts
                    // The WO-191 follow-up: which of the host's NPCs reach this joiner, and their activity rows (the first of each name,
                    // then every 200th sample): SYNTH got npc <name> n=<k> flags=<f> | SYNTH got activity npc <name> <state>
                    else if (rt == Protocol.NpcStateDown && rb2.Length >= 2 && rb2.Length >= 2 + rb2[1] + 21)
                    {
                        string nn = Encoding.UTF8.GetString(rb2, 2, rb2[1]);
                        int k = s_npcIn.TryGetValue(nn, out int c) ? c + 1 : 1;
                        s_npcIn[nn] = k;
                        if (k == 1 || k % 200 == 0)
                            Console.WriteLine(FormattableString.Invariant($"SYNTH got npc {nn} n={k} flags={rb2[2 + rb2[1] + 20]}"));
                    }
                    else if (rt == Protocol.ActivityHostDown && rb2.Length > 1 && ActivityCodec.TryDecode(rb2.AsSpan(1), out byte akind, out var arows)
                             && akind == Protocol.ActivityKindNpc)
                    {
                        foreach (var ar in arows)
                            if (!s_actIn.TryGetValue(ar.Name, out var prev) || prev != ar.A)
                            { s_actIn[ar.Name] = ar.A; Console.WriteLine($"SYNTH got activity npc {ar.Name} {ar.A}"); }
                    }
                }
            }
            catch { }
        });

        var sw = Stopwatch.StartNew();
        var queue = new PriorityQueue<byte[], double>();
        double lastPing = 0, lastReport = 0, lastGhost = -1e9; long sent = 0, emitted = 0;
        bool ghostWasRiding = false;
        double streamT0 = startDelay;
        while (sw.Elapsed.TotalSeconds < duration + startDelay)
        {
            double now = sw.Elapsed.TotalMilliseconds;
            double ts = sw.Elapsed.TotalSeconds;
            if (ts >= streamT0)
            {
                double t = ts - streamT0;
                for (int ri = rows.Count - 1; ri >= 0; ri--)
                {
                    if (rows[ri].T > t) continue;
                    var ev = new RowEvent(unchecked((uint)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency)), 0, rows[ri].Row, rows[ri].Npc);
                    await st.WriteAsync(s_actions.Build(ActionKind.NpcAttack, ActionPhase.Commit, ev.ToBytes()));
                    Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s NpcAttack npc={rows[ri].Npc} row={rows[ri].Row}"));
                    rows.RemoveAt(ri);
                }
                for (int ri = reacts.Count - 1; ri >= 0; ri--)   // WO-187 C1: the host NPC's hit reaction rows
                {
                    if (reacts[ri].T > t) continue;
                    var ev = new RowEvent(unchecked((uint)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency)), 0, reacts[ri].Row, reacts[ri].Npc);
                    await st.WriteAsync(s_actions.Build(ActionKind.NpcHit, ActionPhase.Commit, ev.ToBytes()));
                    Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s NpcHit npc={reacts[ri].Npc} row={reacts[ri].Row}"));
                    reacts.RemoveAt(ri);
                }
                for (int ai = jattacks.Count - 1; ai >= 0; ai--)   // WO-170: the joiner's swings, stamped now
                {
                    if (jattacks[ai].T > t) continue;
                    var ae = new AttackEvent(unchecked((uint)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency)), 1, WireZone.UpperRight, 0, 0, jattacks[ai].Row);
                    await st.WriteAsync(s_actions.Build(ActionKind.Attack, ActionPhase.Commit, ae.ToBytes()));
                    Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s Attack row={jattacks[ai].Row}"));
                    jattacks.RemoveAt(ai);
                }
                for (int mi = fmoves.Count - 1; mi >= 0; mi--)   // WO-187 C3: the figure's rows, stamped now
                {
                    if (fmoves[mi].T > t) continue;
                    if (fmoves[mi].What.StartsWith("pvit:", StringComparison.Ordinal))
                    {
                        var pv = fmoves[mi].What.Split(':');
                        var vp = new byte[3 + Protocol.PlayerStateUpPayloadLen]; vp[0] = Protocol.PlayerStateUp;
                        BinaryPrimitives.WriteUInt16LittleEndian(vp.AsSpan(1), (ushort)Protocol.PlayerStateUpPayloadLen);
                        BinaryPrimitives.WriteSingleLittleEndian(vp.AsSpan(3), float.Parse(pv[1], CultureInfo.InvariantCulture));
                        BinaryPrimitives.WriteSingleLittleEndian(vp.AsSpan(7), float.Parse(pv[2], CultureInfo.InvariantCulture));
                        vp[11] = Convert.ToByte(pv[3], 16);
                        await st.WriteAsync(vp);
                        Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s PVITALS {fmoves[mi].What}"));
                        string hb = fmoves[mi].What.EndsWith(":hb", StringComparison.Ordinal) ? fmoves[mi].What : fmoves[mi].What + ":hb";
                        if (!fmoves[mi].What.EndsWith(":hb", StringComparison.Ordinal))   // a new sample ends the older one's heartbeat
                            for (int hi = fmoves.Count - 1; hi >= 0; hi--) if (hi != mi && fmoves[hi].What.EndsWith(":hb", StringComparison.Ordinal)) { fmoves.RemoveAt(hi); if (hi < mi) mi--; }
                        fmoves[mi] = (t + 2.0, hb, Guid.Empty);   // the heartbeat
                        continue;
                    }
                    if (fmoves[mi].What.StartsWith("fclinch", StringComparison.Ordinal))
                    {
                        var fc = fmoves[mi].What.Split(':');
                        await st.WriteAsync(Wo187Verbs.ClinchMove(s_actions, fmoves[mi].Row, Guid.ParseExact(fc[1], "N"), fc[0].EndsWith('1')));
                    }
                    else
                    await st.WriteAsync(Wo187Verbs.Move(s_actions, fmoves[mi].What, fmoves[mi].Row));
                    Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s FMOVE {fmoves[mi].What} row={fmoves[mi].Row}"));
                    fmoves.RemoveAt(mi);
                }
                for (int ji = joinMsgs.Count - 1; ji >= 0; ji--)   // WO-164: activity rows, W164 messages, checkpoints, when due
                {
                    if (joinMsgs[ji].T > t) continue;
                    await st.WriteAsync(joinMsgs[ji].Pkt);
                    Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s {joinMsgs[ji].Note}"));
                    joinMsgs.RemoveAt(ji);
                }
                for (int vi = verdicts.Count - 1; vi >= 0; vi--)   // WO-163: the verdicts of the plan, when due
                {
                    var vd = verdicts[vi];
                    if (vd.T > t) continue;
                    byte vflags = (byte)((vd.Swing != 0 ? Protocol.HitFlagSwingKnown : 0) | (vd.Npc.Length == 0 ? Protocol.HitFlagNoAttacker : 0));
                    await st.WriteAsync(new HitVerdictMsg(vd.Id, vd.V, vflags, 0, vd.Swing, vd.Hp, vd.St, vd.Npc).BuildUp(vd.Tgt));
                    Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s HVERDICT id={vd.Id} {vd.V} hp={vd.Hp} st={vd.St} npc={(vd.Npc.Length == 0 ? "-" : vd.Npc)} swing={vd.Swing} -> ghost {vd.Tgt}"));
                    verdicts.RemoveAt(vi);
                }
                for (int hi = jhits.Count - 1; hi >= 0; hi--)   // WO-173: a swing's attributed hit, its row's lag after the swing
                {
                    if (jhits[hi].T > t) continue;
                    await st.WriteAsync(Wo165Verbs.AttributedHit(jhits[hi].Npc, jhits[hi].St, jhits[hi].Hp));
                    Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s JHIT npc={jhits[hi].Npc} hp={jhits[hi].Hp} st={jhits[hi].St} (attributed, after its swing)"));
                    jhits.RemoveAt(hi);
                }
                for (int fi = 0; fi < fights.Count; fi++)   // WO-165: the avatar's attributed blows on a host NPC
                {
                    var fg = fights[fi];
                    if (t < fg.T0 || t >= fg.T1) continue;
                    if (lastFight.TryGetValue(fi, out double lf) && t - lf < fg.Every) continue;
                    lastFight[fi] = t;
                    await st.WriteAsync(Wo165Verbs.AttributedHit(fg.Npc, fg.St, fg.Hp));
                    Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s AVATARFIGHT hit npc={fg.Npc} hp={fg.Hp} st={fg.St} (attributed)"));
                }
                // WO-132: the host NPC's combat state, 1 Hz while on, one "off" at the end.
                foreach (var nc in ncombats)
                {
                    if (t < nc.T0 || nc.OffSent) continue;
                    bool on = t < nc.T1;
                    // back-to-back windows for one NPC: the next one takes over, no "off" in between
                    if (!on && ncombats.Any(o => o != nc && o.Npc == nc.Npc && o.T0 <= t && t < o.T1)) { nc.OffSent = true; continue; }
                    if (on && t - nc.LastSent < 1.0) continue;
                    nc.LastSent = t;
                    var bits = on ? BodyState2Bits.CombatMode | (nc.Block ? BodyState2Bits.BlockHeld : 0) | (nc.Target != NpcCombatTarget.None ? BodyState2Bits.Locked : 0) : BodyState2Bits.None;
                    var st2 = new BodyState2(0, 0, bits, Protocol.ZoneFromTableId(nc.Gz), Protocol.StanceFromTableId(nc.Gs), Protocol.ZoneFromTableId(nc.Atk), 0, 0, 0);
                    var ev = new NpcCombatEvent(unchecked((uint)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency)), st2, on ? nc.Target : NpcCombatTarget.None, nc.Ghost, nc.Npc);
                    await st.WriteAsync(s_actions.Build(ActionKind.NpcCombat, ActionPhase.Commit, ev.ToBytes()));
                    if (!on) nc.OffSent = true;
                    if (!on || !nc.Logged) Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s NpcCombat {ev}"));
                    nc.Logged = true;
                }
                // WO-131: a recorded host stream, in order.
                int rawSent = 0;
                while (raws.Count > 0 && raws[0].T <= t)
                {
                    var r = raws[0]; raws.RemoveAt(0);
                    var body = (byte[])r.Body.Clone();
                    if (r.Stamp >= 0 && r.Stamp + 4 <= body.Length)
                        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(r.Stamp), unchecked((uint)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency)));
                    var rp = new byte[3 + body.Length]; rp[0] = r.Type;
                    BinaryPrimitives.WriteUInt16LittleEndian(rp.AsSpan(1), (ushort)body.Length); body.CopyTo(rp, 3);
                    await st.WriteAsync(rp);
                    if (r.Type == Protocol.ActionUp) Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s raw action kind={body[0]} at={DateTime.Now:HH:mm:ss.fff}"));
                    else if (r.Type is 0x6A or 0x6E && body.Length > 6)   // WO-197 Part C: the replayed activity / extra rows, with the wall clock
                        Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s raw join type={r.Type:X2} kind={body[5]} n={body[6]} at={DateTime.Now:HH:mm:ss.fff}"));
                    rawSent++; sent++;
                }
                for (int si = saves.Count - 1; si >= 0; si--)
                {
                    if (saves[si].T > t) continue;
                    var md5 = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes($"synthetic-{saves[si].Idx}"));
                    var ws = new WorldSaved(++wsSeq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), saves[si].Kind, saves[si].Playline, saves[si].Idx, md5);
                    var body = ws.Encode();
                    var wp = new byte[3 + body.Length]; wp[0] = Protocol.WorldSavedUp;
                    BinaryPrimitives.WriteUInt16LittleEndian(wp.AsSpan(1), (ushort)body.Length); body.CopyTo(wp, 3);
                    await st.WriteAsync(wp);
                    Console.WriteLine(FormattableString.Invariant($"SYNTH t={t:F1}s WorldSaved sent file=playline{ws.Playline}/{ws.FileName}"));
                    saves.RemoveAt(si);
                }
                foreach (var m in movers)
                {
                    if (t < m.T0 || t >= m.T1) continue;   // WO-167: outside its window this NPC is not streamed (it appears / leaves)
                    if (now - m.LastSent < emitMs) continue;
                    var (x, y, z, yaw) = m.At(t);
                    if (tports.TryGetValue(m.Name, out var mtp)) { var (odx, ody) = Wo165Verbs.Offset(mtp, t); x += odx; y += ody; }
                    double dx = x - m.LastSentX, dy = y - m.LastSentY, dz = z - m.LastSentZ;
                    bool moved = Math.Abs(dx) > 0.05 || Math.Abs(dy) > 0.05 || Math.Abs(dz) > 0.05;
                    bool deadNow = dies.TryGetValue(m.Name, out double dieT) && t >= dieT;   // WO-131
                    float hpNow = 100f;
                    if (hps.TryGetValue(m.Name, out var hpl)) foreach (var (ht, hv) in hpl) if (t >= ht) hpNow = hv;
                    bool koNow = kos.TryGetValue(m.Name, out var kwin) && kwin.Any(w => t >= w.T0 && t < w.T1);   // WO-173
                    bool lifeChanged = deadNow != m.SentDead || Math.Abs(hpNow - m.SentHp) > 0.5f || koNow != m.SentKo;
                    if (!moved && !lifeChanged && now - m.LastSent < 2000) continue;
                    m.SentDead = deadNow; m.SentHp = hpNow; m.SentKo = koNow;
                    m.LastSent = now; m.LastSentX = x; m.LastSentY = y; m.LastSentZ = z;
                    m.Seq++;
                    byte fl = m is Fight fm ? fm.FlagsAt(t) : m is Hold { Dead: true } ? Protocol.NpcStateFlagDead : m is Hold { Drawn: true } ? (byte)0x04 : (byte)0;
                    if (deadNow) fl = Protocol.NpcStateFlagDead;
                    else if (koNow) fl |= 0x02;   // WO-173: the knocked-out bit
                    uint sms = senderClock == "tick" ? unchecked((uint)Environment.TickCount64)
                             : unchecked((uint)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency));
                    var pkt = BuildUp(m.Name, x, y, z, yaw, (fl & Protocol.NpcStateFlagDead) != 0 ? 0f : hpNow, fl, m.Seq, sms);
                    double d = delayMs + rng.NextDouble() * jitterMs + (rng.NextDouble() * 100 < spikePct ? spikeMs : 0);
                    queue.Enqueue(pkt, now + d);
                    emitted++;
                }
            }
            if (ghost is not null && ts >= streamT0 && now - lastGhost >= ghostMs)
            {
                lastGhost = now;
                var (gx, gy, gz, gyaw) = ghost.At(ts - streamT0);
                if (tports.TryGetValue("ghost", out var gtp)) { var (odx, ody) = Wo165Verbs.Offset(gtp, ts - streamT0); gx += odx; gy += ody; }
                double gtc = ts - streamT0;
                var gc = gcombats.FirstOrDefault(c => gtc >= c.T0 && gtc < c.T1);
                bool gInCombat = gcombats.Any(c => gtc >= c.T0 && gtc < c.T1);   // WO-166 C1
                int glen = Protocol.PositionPayloadLen + (gInCombat ? Protocol.BodyState2Len : 0) + (ghostSenderMs ? Protocol.SenderMsLen : 0);
                var gp = new byte[3 + glen]; gp[0] = Protocol.Position; BinaryPrimitives.WriteUInt16LittleEndian(gp.AsSpan(1), (ushort)glen);
                BinaryPrimitives.WriteSingleLittleEndian(gp.AsSpan(3), gx); BinaryPrimitives.WriteSingleLittleEndian(gp.AsSpan(7), gy);
                BinaryPrimitives.WriteSingleLittleEndian(gp.AsSpan(11), gz); BinaryPrimitives.WriteSingleLittleEndian(gp.AsSpan(15), gyaw);
                gp[19] = ghostSenderMs ? Protocol.PositionFlagSenderMs : (byte)0;
                double gt = ts - streamT0;
                bool riding = rides.Any(r => gt >= r.T0 && gt < r.T1);
                if (riding) gp[19] |= Protocol.PositionFlagRiding;
                if (claimHost) gp[19] |= Protocol.PositionFlagHostClaim;
                if (riding != ghostWasRiding) { Console.WriteLine(FormattableString.Invariant($"SYNTH ghost riding={(riding ? "ON" : "OFF")} at t={gt:F1}s at={DateTime.Now:HH:mm:ss.fff}")); ghostWasRiding = riding; }
                int go = 20;
                if (gInCombat)
                {
                    gp[19] |= Protocol.PositionFlagBodyState2;
                    new BodyState2(0, 0, BodyState2Bits.CombatMode, Protocol.ZoneFromTableId(gc.Gz), WireGuardStance.Left, Protocol.ZoneFromTableId(gc.Gz), 0, 0, 0).Write(gp.AsSpan(go));
                    go += Protocol.BodyState2Len;
                }
                // Stamped at the sample, before the injected delay: the jitter then
                // shows as lateness against the stamp, exactly as on a real link.
                if (ghostSenderMs)
                    BinaryPrimitives.WriteUInt32LittleEndian(gp.AsSpan(go), senderClock == "tick" ? unchecked((uint)Environment.TickCount64)
                        : unchecked((uint)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency)));
                double d = delayMs + rng.NextDouble() * jitterMs + (rng.NextDouble() * 100 < spikePct ? spikeMs : 0);
                queue.Enqueue(gp, now + d);
            }
            if (echo is not null)   // WO-187 C3: the echoed packets that are due, re-stamped as they go
            {
                double en = echoClock.Elapsed.TotalMilliseconds;
                while (echo.Out.TryPeek(out var eo) && eo.Due <= en && echo.Out.TryDequeue(out eo))
                {
                    await st.WriteAsync(Wo187Verbs.Restamp(eo.Pkt));
                    sent++;
                    if (eo.Note.Length > 0) Console.WriteLine(FormattableString.Invariant($"SYNTH t={ts:F1}s {eo.Note}"));
                }
            }
            while (queue.TryPeek(out var p, out double at) && at <= now)
            {
                queue.Dequeue();
                await st.WriteAsync(p);
                sent++;
            }
            if (now - lastPing > 2000)
            {
                lastPing = now;
                var ping = new byte[3 + 8]; ping[0] = Protocol.Ping; BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(1), 8);
                BinaryPrimitives.WriteInt64LittleEndian(ping.AsSpan(3), DateTime.UtcNow.Ticks);
                await st.WriteAsync(ping);
            }
            if (now - lastReport > 10000) { lastReport = now; Console.WriteLine($"SYNTH t={ts:F0}s emitted={emitted} sent={sent} rx={rx} queued={queue.Count}" + (echo is null ? "" : $" echo_positions={echo.Positions} echo_actions={echo.Actions} echo_dropped={echo.Dropped}")); }
            Thread.Sleep(1);
        }
        Console.WriteLine($"SYNTH done emitted={emitted} sent={sent} rx={rx}");
        cts.Cancel();
        return 0;
    }
}
