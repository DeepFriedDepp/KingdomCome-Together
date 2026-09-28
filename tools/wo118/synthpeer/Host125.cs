// WO-125 live test tool (tools/wo118, never shipped): a synthetic HOST for the
// continuity tests. The real game is the joiner; this plays a WO-125 host agent:
//   * announces its session mode WITH its world's identity (JoinStatus state 8:
//     the playthrough seed in the joinId slot, SessionSeedKnown|SessionHenryWorld);
//   * serves joins (the WO-123 status sequence), replaying its branch as
//     WorldSaved entries with the branch bit right before every WorldOffer;
//   * refuses a join in a non-Henry world without "pausing";
//   * takes commands from a control file, one per line, each run once in order:
//       save <file> [name]        a host world save: WorldSaved (seq, the file's footer md5), the branch grows
//                                 (parent = the head); the file is the world served from now on. name =
//                                 e.g. autosave061 (the engine-style name the joiner logs), default autosave<seq>
//       reload <file> [reseed]    the host loads a save: "reloading" to every peer, 3 s, then that file is the
//                                 world and the branch head (a known md5 keeps its parents)
//       world <file> <reseed>     the same, another playthrough (give a synthetic seed in hex)
//       mode shared|separate      the session mode
//       leave                     disconnect (the joiner must leave the world)
//       pos <x> <y> <z>           (WO-114) the host's streamed position from now on
//       leash warn|cancel         (WO-114) a Leash message to every joiner (0x58), with the current position
//       leash countdown|hold <s>  ... seconds left
//       leash pull [fast]         ... a pull beside the host's position (fast = the fast-travel reason)
//       leash config on|off <warn> <pull>
//       timeskip <worldTime>      (WO-114) a fast-travel time skip (TimeSkip start + done, kind fast-travel)
//       story objective|fingerprint|approach <text>   (WO-133) a StoryBeatUp (0x37) of that kind
//       itemdrop <cls> <amt> <hp> <x> <y> <z> [dropId]  (WO-134) an ItemDropUp (0x32): the host dropped this
//       itemclaim <dropId>        (WO-134) an ItemClaimUp (0x34): the host picked that drop up
//                                 (every ItemDropDown 0x33 / ItemClaimDown 0x35 received is logged)
//       bodyset <name> <items|->  (WO-134) the host's body <name> holds these items (cls:amt:hp[:w],...): a joiner's
//                                 BodyOpen is answered with them, its BodyTake comes out of them (ok) or not (gone)
//       bodypush <name>           (WO-134) a BodyState "update" of that body to every joiner (the host looted it)
//       witem <cls> <x> <y> <z>   (WO-134) a world item the host still has: a joiner's ItemTake of it is ok (once)
//       witemtaken <cls> <x> <y> <z>  (WO-134) one the host already took: an ItemTake of it is gone
//       itemgone <cls> <x> <y> <z>    (WO-134) an ItemGone to every joiner (the host picked it up)
//       npc <name> <x> <y> <z> <yaw> <hp> <flags> [secs]  (WO-134) stream that NPC (NpcStateUp 0x26) every 200 ms for
//                                 secs (default 30): flags 1 = dead (the joiner applies the host's death to its copy)
//       ledger <rows|->           (WO-134) the host's chest ledger (container|cls|n|hp|worldT|restockDays;...),
//                                 sent to the joiner at every Ready (and at once with ledgersend)
//       npcmove <name> <vx> <vy> [vz]  (WO-136) a running npc stream moves at that velocity (m/s) from now on
//       npcrow <name> <rowGuid>   (WO-136) the host's NPC committed that attack row (ActionKind.NpcAttack)
//       npccombat <name> on|off [host|avatar:N|none] [gz]  (WO-136) the host's NPC combat state (ActionKind.NpcCombat),
//                                 repeated every 1 s while on (the WO-132 heartbeat)
//       hstate k=v ...            (WO-135/136) the host avatar's state block: crouch=0|1 torch=0|1 combat=0|1
//       appearance <guid,...>     (WO-136) the host's outfit (an Appearance 0x1A)
//       quest change <flags> <old> <new> <port|-> <questLen> <path>   (WO-137) a QuestHost Change to every joiner,
//                                 numbered by this host (its own seq, from 1)
//       quest mode on|off <why>   (WO-137) the host's quest sync (QuestHost Mode)
//       quest hold on|off <npc>   (WO-137) a QuestHost Hold (tok 0)
//       quest result <tok> <verdict> <hostVal> <port|-> <path>   (WO-137) a QuestHost Result by hand
//       quest checkpoint          (WO-137) a Checkpoint of every State this host has sent or applied
//       quest auto on|off         (WO-137) answer QuestAsk like a real host (default on): a Request is judged
//                                 against this host's own table (unknown = at the joiner's old value) and applied
//                                 (its Change goes out, mirror-flagged, then Result applied) / already / refused;
//                                 a Talk is answered with a Hold; a Resync with a Checkpoint
//       quest set <path> <val> <port|->   (WO-137) this host's own value for a State (no message)
//       questfile <file>          (WO-137) replay a host's recorded Change texts ("<seq> <flags> <old> <new> <port|->
//                                 <questLen> <path>" per line, as avatarpeer --quest-rec writes them), renumbered, in order
//                                 (every QuestAskDown 0x63 received is logged: QUESTASK ...)
//     [reseed] = a synthetic seed (hex) written into the save's body 0x01FB, re-signed: a second
//     "playthrough" made from a copy. Files are COPIES of real host saves; never logged by path.
//
// usage: SynthPeer --join-host125 <file> --ctl <control file> [--port 7778] [--name synth-host]
//                  [--duration 3600] [--host-pos x,y,z] [--reseed hex]
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using KcdMp.Client;
using KcdMp.Wire;

static class Host125
{
    static string Arg(string[] a, string k, string d) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : d; }
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static void Say(string s) => Console.WriteLine(FormattableString.Invariant($"SYNTH125 t={Clock.Elapsed.TotalSeconds:F2}s {s}"));

    sealed class World
    {
        public byte[] Bytes = [];
        public string Md5 = "";
        public uint? Seed;
        public bool Henry;
        public string Player = "?";
        public string Label = "";
    }

    static World Load(string path, string? reseed)
    {
        var b = File.ReadAllBytes(path);
        if (reseed is { Length: > 0 } rs && rs != "keep")
        {
            uint seed = uint.Parse(rs, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var c = WhsSave.Inflate(b);
            var n = WhsSave.PathGet(c.Raw, 0x01F4, 0x01FB) ?? throw new InvalidDataException("no seed chunk");
            BinaryPrimitives.WriteUInt32LittleEndian(c.Raw.AsSpan(n.PayloadOff), seed);
            b = WhsSave.Deflate(c.DescBytes, c.Raw, c.FooterTail);
        }
        var v = WhsSave.Verify(b);
        if (!v.Ok) throw new InvalidDataException($"{Path.GetFileName(path)} does not verify: {v.Reason}");
        var raw = WhsSave.Inflate(b).Raw;
        var who = WhsSave.PlayerOf(raw);
        var w = new World { Bytes = b, Md5 = v.Md5.ToLowerInvariant(), Seed = WhsSave.ReadSeed(raw), Henry = who.IsHenry, Player = who.Player, Label = Path.GetFileName(path) };
        return w;
    }

    static (byte Src, uint JoinId, byte[] Body)? Split(byte[] p) =>
        Protocol.TrySplitJoinDown(p, out byte src, out _, out uint jid, out var body) ? (src, jid, body.ToArray()) : null;

    static string Tag(World w) => w.Seed is uint s ? WhsSave.SeedTag(s) : "-";

    public static async Task<int> RunAsync(string[] a, string release)
    {
        string host = Arg(a, "--host", "127.0.0.1"); int port = int.Parse(Arg(a, "--port", "7778"), CultureInfo.InvariantCulture);
        string name = Arg(a, "--name", "synth-host");
        string ctl = Arg(a, "--ctl", "");
        double duration = double.Parse(Arg(a, "--duration", "3600"), CultureInfo.InvariantCulture);
        var hp = Arg(a, "--host-pos", "0,0,0").Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        byte leashSeq = 0;
        // WO-134: the scripted host world
        var bodies = new Dictionary<string, List<Wo134Rules.Item>>(StringComparer.Ordinal);
        var witems = new List<(Guid Cls, float X, float Y, float Z, bool Taken)>();
        var npcFlags = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);   // WO-135: a running npc stream's flags, changeable
        string? hostBuild = null;
        BodyState2? hostSt2 = null;
        var npcVel = new System.Collections.Concurrent.ConcurrentDictionary<string, (float Vx, float Vy, float Vz)>(StringComparer.Ordinal);   // WO-136: npcmove
        var npcCombatOn = new System.Collections.Concurrent.ConcurrentDictionary<string, NpcCombatEvent>(StringComparer.Ordinal);            // WO-136: npccombat
        var actOut = new ActionOutbox();                                                                                                      // WO-136: NpcAttack / NpcCombat                                                                                    // WO-135: hstate crouch=1 -- the host avatar's state block                                                                                      // WO-135: announced to every joiner every 10 s
        var ledger = new Wo134Rules.Ledger();
        uint qseq = 0;                                                                                                                         // WO-137: this host's change numbers
        bool qauto = true;                                                                                                                     // WO-137: quest auto on|off
        var qstate = new System.Collections.Concurrent.ConcurrentDictionary<string, (int Val, string Port, int QuestLen)>(StringComparer.Ordinal);   // WO-137: this host's States
        var leashSeen = new Dictionary<byte, LeashState>();   // WO-114: the last LeashState per joiner
        var world = Load(Arg(a, "--join-host125", ""), Arg(a, "--reseed", ""));
        bool shared = true;
        bool loading = false;   // like a WO-125 host agent: silent, and joins deferred, while a load runs
        uint seq = 0;
        var parent = new Dictionary<string, string?>(StringComparer.Ordinal) { [world.Md5] = null };
        string head = world.Md5;
        var names = new Dictionary<string, (byte Kind, byte Pl, ushort Idx)>(StringComparer.Ordinal);

        using var hard = new CancellationTokenSource(TimeSpan.FromSeconds(duration));
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(host, port);
        var st = tcp.GetStream();
        {
            var nb = Encoding.UTF8.GetBytes(name); var rb = Encoding.UTF8.GetBytes(release);
            int hl = 2 + nb.Length + rb.Length; var hs = new byte[3 + hl];
            hs[0] = Protocol.Handshake; BinaryPrimitives.WriteUInt16LittleEndian(hs.AsSpan(1), (ushort)hl);
            hs[3] = Protocol.Version; hs[4] = (byte)nb.Length; nb.CopyTo(hs, 5); rb.CopyTo(hs, 5 + nb.Length);
            await st.WriteAsync(hs);
        }
        var inbox = Channel.CreateUnbounded<(byte Type, byte[] Payload)>();
        async Task<(byte, byte[])> Read()
        {
            var h = new byte[3]; await Exact(h);
            var b = new byte[BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(1))]; await Exact(b);
            return (h[0], b);
        }
        async Task Exact(byte[] b) { int got = 0; while (got < b.Length) { int n = await st.ReadAsync(b.AsMemory(got), hard.Token); if (n <= 0) throw new IOException("closed"); got += n; } }
        var (t0, ack) = await Read();
        if (t0 != Protocol.Ack) { Say("refused by the relay"); return 1; }
        Say($"host connected id={ack[0]} world={world.Label} ({world.Bytes.Length} B) tag={Tag(world)} player={world.Player} henry={(world.Henry ? "yes" : "no")} md5={world.Md5[..8]}");
        var wlock = new SemaphoreSlim(1, 1);
        async Task W(byte[] pkt) { await wlock.WaitAsync(); try { await st.WriteAsync(pkt, hard.Token); } finally { wlock.Release(); } }
        async Task Announce()
        {
            if (loading) return;
            ushort flags = world.Seed is null ? (ushort)0 : (ushort)(Protocol.SessionSeedKnown | (world.Henry ? Protocol.SessionHenryWorld : 0));
            for (byte g = 1; g < 8; g++)
                await W(JoinStatusCodec.Build(g, shared ? world.Seed ?? 0 : 0, Protocol.JoinStateSession, Protocol.JoinReasonId(shared ? "shared-world" : "separate"), shared ? flags : (ushort)0));
        }
        byte[] WsPacket(WorldSaved ws)
        {
            var body = ws.Encode();
            var p = new byte[3 + body.Length];
            p[0] = Protocol.WorldSavedUp; BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), (ushort)body.Length); body.CopyTo(p, 3);
            return p;
        }
        List<string> Branch()
        {
            var o = new List<string>(); string? cur = head;
            while (cur is not null && o.Count < 64 && !o.Contains(cur)) { o.Add(cur); cur = parent.GetValueOrDefault(cur); }
            return o;
        }
        // reader
        _ = Task.Run(async () =>
        {
            try { while (true) { var p = await Read(); await inbox.Writer.WriteAsync(p); } }
            catch (Exception ex) { Say($"connection ended: {ex.Message}"); hard.Cancel(); }
        });
        // heartbeat
        _ = Task.Run(async () =>
        {
            int n = 0;
            try
            {
                while (!hard.IsCancellationRequested)
                {
                    await W(PositionCodec.BuildPosition(hp[0], hp[1], hp[2], 0, false, false, hostSt2, hostClaim: true));   // WO-127: the synthetic host claims the session like a real one (WO-135: + its state block)
                    if (n++ % 5 == 0) await Announce();
                    await Task.Delay(1000, hard.Token);
                }
            }
            catch { }
        });
        // control file
        var joinQ = Channel.CreateUnbounded<(byte Joiner, uint JoinId)>();
        var joinMsgs = Channel.CreateUnbounded<(byte Type, byte[] Body)>();
        _ = Task.Run(async () =>
        {
            int done = 0;
            while (!hard.IsCancellationRequested && ctl != "")
            {
                try
                {
                    var lines = File.Exists(ctl) ? File.ReadAllLines(ctl) : [];
                    for (; done < lines.Length; done++)
                    {
                        var p = lines[done].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length == 0 || p[0].StartsWith('#')) continue;
                        switch (p[0])
                        {
                            case "save":
                            {
                                var nw = Load(p[1], null);
                                if (nw.Seed != world.Seed && world.Seed is uint cs) nw = Load(p[1], cs.ToString("x8"));   // a save of THIS playthrough
                                string nm = p.Length > 2 ? p[2] : $"autosave{100 + (int)seq:D3}";
                                var id = WorldSaved.ParsePath(Path.Combine("playline1", nm + ".whs")) ?? (Protocol.SaveKindAuto, (byte)1, (ushort)seq);
                                parent.TryAdd(nw.Md5, head);
                                head = nw.Md5;
                                names[nw.Md5] = id;
                                world = nw;
                                var ws = new WorldSaved(++seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id.Item1, id.Item2, id.Item3, Convert.FromHexString(nw.Md5));
                                await W(WsPacket(ws));
                                Say($"SAVE seq={seq} as playline{id.Item2}/{ws.FileName} md5={nw.Md5[..8]} (from {nw.Label}); branch depth {Branch().Count}");
                                break;
                            }
                            case "reload":
                            case "world":
                            {
                                loading = true;
                                for (byte g = 1; g < 8; g++) await W(JoinStatusCodec.Build(g, 0, Protocol.JoinStateReloading, Protocol.JoinReasonId("reloading"), 0));
                                Say($"{p[0].ToUpperInvariant()} {Path.GetFileName(p[1])}: 'reloading' sent to every peer; loading 3 s");
                                await Task.Delay(3000, hard.Token);
                                var nw = Load(p[1], p.Length > 2 ? p[2] : null);
                                if (!parent.ContainsKey(nw.Md5)) parent[nw.Md5] = null;
                                head = nw.Md5;
                                world = nw;
                                loading = false;
                                await Announce();
                                Say($"{p[0].ToUpperInvariant()} done: world {nw.Label} tag={Tag(nw)} player={nw.Player} md5={nw.Md5[..8]} branch depth {Branch().Count}; announced");
                                break;
                            }
                            case "mode":
                                shared = p.Length > 1 && p[1] == "shared";
                                await Announce();
                                Say($"MODE {(shared ? "shared-world" : "separate")} announced");
                                break;
                            case "pos":   // WO-114
                                hp = [float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture)];
                                await W(PositionCodec.BuildPosition(hp[0], hp[1], hp[2], 0, false, false, null, hostClaim: true));
                                Say(FormattableString.Invariant($"POS the host is now at ({hp[0]:F1}, {hp[1]:F1}, {hp[2]:F1})"));
                                break;
                            case "leash":   // WO-114
                            {
                                byte kind = p[1] switch { "warn" => Protocol.LeashKindWarn, "countdown" => Protocol.LeashKindCountdown, "cancel" => Protocol.LeashKindCancel,
                                                          "pull" => Protocol.LeashKindPull, "config" => Protocol.LeashKindConfig, "hold" => Protocol.LeashKindHold, _ => (byte)0 };
                                ushort arg = 0; byte sq = 0; ushort dist = 0;
                                if (kind is Protocol.LeashKindCountdown or Protocol.LeashKindHold) arg = ushort.Parse(p[2], CultureInfo.InvariantCulture);
                                if (kind == Protocol.LeashKindWarn) arg = 600;
                                if (kind == Protocol.LeashKindPull) { sq = ++leashSeq; arg = p.Length > 2 && p[2] == "fast" ? Protocol.LeashReasonFastTravel : Protocol.LeashReasonDistance; }
                                if (kind == Protocol.LeashKindConfig) { sq = (byte)(p[2] == "on" ? 1 : 0); arg = ushort.Parse(p[3], CultureInfo.InvariantCulture); dist = ushort.Parse(p[4], CultureInfo.InvariantCulture); }
                                if (kind == 0) { Say($"LEASH unknown kind {p[1]}"); break; }
                                for (byte g = 1; g < 8; g++)
                                    await W(new LeashCommand(kind, sq, arg, hp[0], hp[1], hp[2], dist).Build(g));
                                Say(FormattableString.Invariant($"LEASH {Protocol.LeashKindName(kind)} seq={sq} arg={arg} host=({hp[0]:F1}, {hp[1]:F1}, {hp[2]:F1}) sent"));
                                break;
                            }
                            case "timeskip":   // WO-114: the host's fast travel's time skip
                            {
                                uint wt = uint.Parse(p[1], CultureInfo.InvariantCulture);
                                foreach (byte ph in new[] { Protocol.TimeSkipPhaseStart, Protocol.TimeSkipPhaseDone })
                                {
                                    var pk = new byte[3 + Protocol.TimeSkipUpPayloadLen];
                                    pk[0] = Protocol.TimeSkipUp; BinaryPrimitives.WriteUInt16LittleEndian(pk.AsSpan(1), Protocol.TimeSkipUpPayloadLen);
                                    pk[3] = ph; pk[4] = Protocol.TimeSkipKindFastTravel;
                                    BinaryPrimitives.WriteUInt32LittleEndian(pk.AsSpan(5), ph == Protocol.TimeSkipPhaseStart ? 0 : wt);
                                    await W(pk);
                                }
                                Say($"TIMESKIP fast-travel start + done t={wt} sent");
                                break;
                            }
                            case "story":   // WO-133: story objective|fingerprint|approach <text>
                            {
                                byte sk = p[1] switch { "fingerprint" => Protocol.StoryBeatKindFingerprint, "approach" => Protocol.StoryBeatKindApproach, _ => Protocol.StoryBeatKindObjective };
                                var body = StoryBeat.BuildUpPayload(sk, string.Join(' ', p.Skip(2)));
                                var sp = new byte[3 + body.Length]; sp[0] = Protocol.StoryBeatUp;
                                BinaryPrimitives.WriteUInt16LittleEndian(sp.AsSpan(1), (ushort)body.Length); body.CopyTo(sp, 3);
                                await W(sp);
                                Say($"STORY {p[1]} sent ({body.Length} bytes)");
                                break;
                            }
                            case "itemdrop":   // WO-134
                            {
                                uint did = p.Length > 7 ? uint.Parse(p[7], CultureInfo.InvariantCulture) : (uint)Random.Shared.Next(1, int.MaxValue);
                                var dp = new byte[3 + Protocol.ItemDropUpPayloadLen]; dp[0] = Protocol.ItemDropUp;
                                BinaryPrimitives.WriteUInt16LittleEndian(dp.AsSpan(1), (ushort)Protocol.ItemDropUpPayloadLen);
                                BinaryPrimitives.WriteUInt32LittleEndian(dp.AsSpan(3), did);
                                Guid.Parse(p[1]).TryWriteBytes(dp.AsSpan(7, 16));
                                BinaryPrimitives.WriteUInt16LittleEndian(dp.AsSpan(23), ushort.Parse(p[2], CultureInfo.InvariantCulture));
                                for (int k = 0; k < 4; k++) BinaryPrimitives.WriteSingleLittleEndian(dp.AsSpan(25 + 4 * k), float.Parse(p[3 + k], CultureInfo.InvariantCulture));
                                await W(dp);
                                Say($"ITEMDROP sent drop={did} class={p[1]} x{p[2]}");
                                break;
                            }
                            case "itemclaim":   // WO-134
                            {
                                var cp = new byte[3 + Protocol.ItemClaimUpPayloadLen]; cp[0] = Protocol.ItemClaimUp;
                                BinaryPrimitives.WriteUInt16LittleEndian(cp.AsSpan(1), (ushort)Protocol.ItemClaimUpPayloadLen);
                                BinaryPrimitives.WriteUInt32LittleEndian(cp.AsSpan(3), uint.Parse(p[1], CultureInfo.InvariantCulture));
                                await W(cp);
                                Say($"ITEMCLAIM sent drop={p[1]}");
                                break;
                            }
                            case "npc":   // WO-134: npc <name> <x> <y> <z> <yaw> <hp> <flags> [secs]
                            {
                                string nm = p[1];
                                float nx = float.Parse(p[2], CultureInfo.InvariantCulture), ny = float.Parse(p[3], CultureInfo.InvariantCulture), nz = float.Parse(p[4], CultureInfo.InvariantCulture);
                                float nyaw = float.Parse(p[5], CultureInfo.InvariantCulture), nhp = float.Parse(p[6], CultureInfo.InvariantCulture);
                                byte nfl = byte.Parse(p[7], CultureInfo.InvariantCulture);
                                double secs = p.Length > 8 ? double.Parse(p[8], CultureInfo.InvariantCulture) : 30;
                                bool running = npcFlags.ContainsKey(nm);
                                npcFlags[nm] = nfl;
                                if (!running) _ = Task.Run(async () =>
                                {
                                    ushort nseq = 0; var t0 = Clock.Elapsed.TotalSeconds; double tl = t0;
                                    float cx = nx, cy = ny, cz = nz;
                                    while (Clock.Elapsed.TotalSeconds - t0 < secs && !hard.IsCancellationRequested)
                                    {
                                        double tn = Clock.Elapsed.TotalSeconds; float dt = (float)(tn - tl); tl = tn;
                                        if (npcVel.TryGetValue(nm, out var v)) { cx += v.Vx * dt; cy += v.Vy * dt; cz += v.Vz * dt; }
                                        float yaw2 = npcVel.TryGetValue(nm, out var v2) && (v2.Vx != 0 || v2.Vy != 0) ? MathF.Atan2(-v2.Vx, v2.Vy) : nyaw;
                                        await W(P.BuildUp(nm, cx, cy, cz, yaw2, nhp, npcFlags.GetValueOrDefault(nm, nfl), ++nseq, (uint)Clock.ElapsedMilliseconds));
                                        await Task.Delay(npcVel.ContainsKey(nm) ? 100 : 200);
                                    }
                                    npcFlags.TryRemove(nm, out _);
                                });
                                Say($"NPC {nm} streamed at {p[2]},{p[3]},{p[4]} hp={p[6]} flags={p[7]} for {secs} s");
                                break;
                            }
                            case "hstate":   // WO-135/136: hstate crouch=0|1 torch=0|1 combat=0|1 -- the host's state block rides its position packets (1 s heartbeat)
                            {
                                var bits = hostSt2?.Bits ?? BodyState2Bits.None;
                                foreach (var kv in p.Skip(1))
                                {
                                    var q = kv.Split('='); if (q.Length != 2) continue;
                                    var b = q[0] switch { "crouch" => BodyState2Bits.Crouched, "torch" => BodyState2Bits.TorchLit, "combat" => BodyState2Bits.CombatMode, _ => BodyState2Bits.None };
                                    bits = q[1] == "1" ? bits | b : bits & ~b;
                                }
                                hostSt2 = new BodyState2(0, 0, bits, WireZone.Undefined, WireGuardStance.None, WireZone.Undefined, 0, 0, 0);
                                await W(PositionCodec.BuildPosition(hp[0], hp[1], hp[2], 0, false, false, hostSt2, hostClaim: true));
                                Say($"HSTATE bits={bits}");
                                break;
                            }
                            case "npcmove":   // WO-136: npcmove <name> <vx> <vy> [vz]
                                npcVel[p[1]] = (float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture),
                                                p.Length > 4 ? float.Parse(p[4], CultureInfo.InvariantCulture) : 0f);
                                Say($"NPCMOVE {p[1]} v=({p[2]},{p[3]})");
                                break;
                            case "npcrow":   // WO-136: npcrow <name> <rowGuid>
                                await W(actOut.Build(ActionKind.NpcAttack, ActionPhase.Commit, new RowEvent((uint)Clock.ElapsedMilliseconds, 0, Guid.Parse(p[2]), p[1]).ToBytes()));
                                Say($"NPCROW {p[1]} row={p[2]}");
                                break;
                            case "npccombat":   // WO-136: npccombat <name> on|off [host|avatar:N|none] [gz]
                            {
                                bool on = p[2] == "on";
                                var tg = p.Length > 3 && p[3].StartsWith("avatar:", StringComparison.Ordinal) ? NpcCombatTarget.Avatar
                                       : p.Length > 3 && p[3] == "none" ? NpcCombatTarget.None : NpcCombatTarget.Host;
                                byte tgh = tg == NpcCombatTarget.Avatar ? byte.Parse(p[3][7..], CultureInfo.InvariantCulture) : (byte)0;
                                var gz = p.Length > 4 ? (WireZone)byte.Parse(p[4], CultureInfo.InvariantCulture) : WireZone.UpperRight;
                                var ev = new NpcCombatEvent((uint)Clock.ElapsedMilliseconds,
                                    new BodyState2(0, 0, on ? BodyState2Bits.CombatMode | BodyState2Bits.Locked : BodyState2Bits.None, gz, WireGuardStance.Left, gz, 0, 0, 0),
                                    on ? tg : NpcCombatTarget.None, tgh, p[1]);
                                await W(actOut.Build(ActionKind.NpcCombat, ActionPhase.Commit, ev.ToBytes()));
                                if (on && !npcCombatOn.ContainsKey(p[1]))
                                {
                                    npcCombatOn[p[1]] = ev;
                                    string cn = p[1];
                                    _ = Task.Run(async () =>
                                    {
                                        while (npcCombatOn.TryGetValue(cn, out var e) && !hard.IsCancellationRequested)
                                        {
                                            await Task.Delay(1000);
                                            if (!npcCombatOn.TryGetValue(cn, out e)) break;
                                            await W(actOut.Build(ActionKind.NpcCombat, ActionPhase.Commit, (e with { SenderMs = (uint)Clock.ElapsedMilliseconds }).ToBytes()));
                                        }
                                    });
                                }
                                else if (on) npcCombatOn[p[1]] = ev;
                                else npcCombatOn.TryRemove(p[1], out _);
                                Say($"NPCCOMBAT {p[1]} {(on ? "on" : "off")} target={tg}{(tg == NpcCombatTarget.Avatar ? ":" + tgh : "")}");
                                break;
                            }
                            case "appearance":   // WO-136: appearance <guid,...> -- the host's outfit
                            {
                                var gs = p[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
                                var ap = new byte[3 + 1 + gs.Length * 16]; ap[0] = Protocol.AppearanceUp;
                                BinaryPrimitives.WriteUInt16LittleEndian(ap.AsSpan(1), (ushort)(1 + gs.Length * 16));
                                ap[3] = (byte)gs.Length;
                                for (int gi = 0; gi < gs.Length; gi++) Guid.Parse(gs[gi]).ToByteArray().CopyTo(ap, 4 + gi * 16);
                                await W(ap);
                                Say($"APPEARANCE sent ({gs.Length} classes)");
                                break;
                            }
                            case "quest":   // WO-137
                            {
                                string rest = string.Join(' ', p.Skip(2));
                                switch (p[1])
                                {
                                    case "change":
                                    {
                                        string text = $"{++qseq} {rest}";
                                        if (!Wo137Rules.TryParseChangeText(text, out var qc)) { Say($"QUEST change refused (malformed): {rest}"); break; }
                                        qstate[qc.Path] = (qc.New, qc.Port, qc.QuestLen);
                                        for (byte g = 1; g < 8; g++) await W(new LootMsg(Protocol.QuestHostChange, 0, text).BuildUp(Protocol.QuestHostUp, g));
                                        Say($"QUEST change #{qseq} sent: {text}");
                                        break;
                                    }
                                    case "mode":
                                    case "hold":
                                    {
                                        byte k = p[1] == "mode" ? Protocol.QuestHostMode : Protocol.QuestHostHold;
                                        for (byte g = 1; g < 8; g++) await W(new LootMsg(k, 0, rest).BuildUp(Protocol.QuestHostUp, g));
                                        Say($"QUEST {p[1]} sent: {rest}");
                                        break;
                                    }
                                    case "result":
                                    {
                                        uint tk = uint.Parse(p[2], CultureInfo.InvariantCulture);
                                        string text = string.Join(' ', p.Skip(3));
                                        for (byte g = 1; g < 8; g++) await W(new LootMsg(Protocol.QuestHostResult, tk, text).BuildUp(Protocol.QuestHostUp, g));
                                        Say($"QUEST result tok={tk} sent: {text}");
                                        break;
                                    }
                                    case "checkpoint":
                                        foreach (var t in Wo137Rules.CheckpointTexts(qstate.Select(kv => new Wo137Rules.CheckpointEntry(kv.Key, kv.Value.Val, kv.Value.Port)).ToList()))
                                            for (byte g = 1; g < 8; g++) await W(new LootMsg(Protocol.QuestHostCheckpoint, 0, t).BuildUp(Protocol.QuestHostUp, g));
                                        Say($"QUEST checkpoint of {qstate.Count} State(s) sent");
                                        break;
                                    case "auto":
                                        qauto = p.Length > 2 && p[2] == "on";
                                        Say($"QUEST auto {(qauto ? "on" : "off")}");
                                        break;
                                    case "set":
                                        qstate[p[2]] = (int.Parse(p[3], CultureInfo.InvariantCulture), p[4] == "-" ? "" : p[4], p[2].IndexOf(".h.", StringComparison.Ordinal) is int hi and > 0 ? hi : 0);
                                        Say($"QUEST set {p[2]} = {p[3]} ({p[4]})");
                                        break;
                                    default:
                                        Say($"QUEST unknown verb {p[1]}");
                                        break;
                                }
                                break;
                            }
                            case "questfile":   // WO-137: replay a recorded host change stream, renumbered, in order
                            {
                                int n = 0, bad = 0;
                                foreach (var raw in File.ReadAllLines(p[1]))
                                {
                                    var q = raw.Trim();
                                    if (q.Length == 0 || q.StartsWith('#')) continue;
                                    var f5 = q.Split(' ', 2);
                                    string text = $"{++qseq} {(f5.Length > 1 ? f5[1] : "")}";
                                    if (!Wo137Rules.TryParseChangeText(text, out var qc)) { bad++; qseq--; continue; }
                                    qstate[qc.Path] = (qc.New, qc.Port, qc.QuestLen);
                                    for (byte g = 1; g < 8; g++) await W(new LootMsg(Protocol.QuestHostChange, 0, text).BuildUp(Protocol.QuestHostUp, g));
                                    n++;
                                    await Task.Delay(10, hard.Token);
                                }
                                Say($"QUESTFILE {Path.GetFileName(p[1])}: {n} recorded host change(s) replayed as #{qseq - n + 1}..#{qseq}{(bad > 0 ? $", {bad} malformed skipped" : "")}");
                                break;
                            }
                            case "npcflags":   // WO-135: npcflags <name> <flags> -- a running npc stream's flags (2 = knocked out, 0 = up, 1 = dead)
                                npcFlags[p[1]] = byte.Parse(p[2], CultureInfo.InvariantCulture);
                                Say($"NPCFLAGS {p[1]} = {p[2]}");
                                break;
                            case "build":   // WO-135: build <BuildInfo> -- the host world's game build, to every joiner every 10 s
                                hostBuild = p[1];
                                _ = Task.Run(async () =>
                                {
                                    string mine = p[1];
                                    while (hostBuild == mine && !hard.IsCancellationRequested)
                                    {
                                        for (byte g = 1; g < 8; g++) await W(new LootMsg(Protocol.LootHostBuild, 0, mine).BuildUp(Protocol.LootHostUp, g));
                                        await Task.Delay(10_000);
                                    }
                                });
                                Say($"BUILD {p[1]} announced to every joiner");
                                break;
                            case "bodyset":   // WO-134
                                bodies[p[1]] = Wo134Rules.ParseItems(p.Length > 2 ? p[2] : "-") ?? [];
                                Say($"BODY {p[1]} = {bodies[p[1]].Count} item(s)");
                                break;
                            case "bodypush":   // WO-134
                            {
                                var items = bodies.GetValueOrDefault(p[1]) ?? [];
                                for (byte g = 1; g < 8; g++)
                                    await W(new LootMsg(Protocol.LootHostBodyState, 0, $"{p[1]} update 1 1 1 {Wo134Rules.FormatItems(items)}").BuildUp(Protocol.LootHostUp, g));
                                Say($"BODYPUSH {p[1]} ({items.Count} item(s)) to every joiner");
                                break;
                            }
                            case "witem":
                            case "witemtaken":   // WO-134
                                witems.Add((Guid.Parse(p[1]), float.Parse(p[2], CultureInfo.InvariantCulture), float.Parse(p[3], CultureInfo.InvariantCulture), float.Parse(p[4], CultureInfo.InvariantCulture), p[0] == "witemtaken"));
                                Say($"WITEM {p[1]} at {p[2]},{p[3]},{p[4]} taken={p[0] == "witemtaken"}");
                                break;
                            case "itemgone":   // WO-134
                                for (byte g = 1; g < 8; g++)
                                    await W(new LootMsg(Protocol.LootHostItemGone, 0, $"{p[1]} {p[2]} {p[3]} {p[4]}").BuildUp(Protocol.LootHostUp, g));
                                Say($"ITEMGONE {p[1]} at {p[2]},{p[3]},{p[4]} to every joiner");
                                break;
                            case "ledger":   // WO-134
                            {
                                ledger = new Wo134Rules.Ledger();
                                if (p.Length > 1 && p[1] != "-")
                                    foreach (var r in p[1].Split(';')) if (Wo134Rules.ParseRow(r) is { } e) ledger.Entries.Add(e);
                                Say($"LEDGER {ledger.Entries.Count} entries");
                                break;
                            }
                            case "ledgersend":   // WO-134
                                for (byte g = 1; g < 8; g++)
                                    foreach (var lp in Wo134Rules.LedgerParts(ledger)) await W(new LootMsg(Protocol.LootHostLedger, 0, lp).BuildUp(Protocol.LootHostUp, g));
                                Say($"LEDGERSEND {ledger.Entries.Count} entries to every joiner");
                                break;
                            case "leave":
                                Say("LEAVE: disconnecting -- the joiner must leave the host's world");
                                tcp.Close();
                                hard.Cancel();
                                return;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { Say($"control line {done + 1} failed: {ex.Message}"); done++; }
                try { await Task.Delay(500, hard.Token); } catch { return; }
            }
        });
        // joins, one at a time
        _ = Task.Run(async () =>
        {
            await foreach (var (joiner, joinId) in joinQ.Reader.ReadAllAsync(hard.Token))
            {
                while (loading) await Task.Delay(500, hard.Token);   // a real host defers a join through its load
                var w0 = world;
                if (!shared) { await W(JoinStatusCodec.Build(joiner, joinId, Protocol.JoinStateRefused, Protocol.JoinReasonId("shared-world-off"), 0)); Say("refused: shared-world-off"); continue; }
                if (!w0.Henry)
                {
                    await W(JoinStatusCodec.Build(joiner, joinId, Protocol.JoinStateRefused, Protocol.JoinReasonId("not-henry"), 0));
                    Say($"JOIN 0x{joinId:x8} refused: this world's player is not Henry ({w0.Player}) -- NOT paused");
                    continue;
                }
                double tr = Clock.Elapsed.TotalSeconds;
                await W(JoinStatusCodec.Build(joiner, joinId, Protocol.JoinStatePaused, 0, 0));
                await W(JoinStatusCodec.Build(joiner, joinId, Protocol.JoinStateSaving, 0, 0));
                Say($"JOIN 0x{joinId:x8} from {joiner}: 'paused' (synthetic); serving {w0.Label} md5={w0.Md5[..8]} (the join save = the current world)");
                var tx = new WorldSender(w0.Bytes, joinId, joiner, seq, Convert.FromHexString(w0.Md5));
                await W(JoinStatusCodec.Build(joiner, joinId, Protocol.JoinStateSending, 0, 0));
                var br = Branch(); br.Reverse();
                for (int i = 0; i < br.Count; i++)
                {
                    var id = names.TryGetValue(br[i], out var x) ? x : (Protocol.SaveKindAuto, (byte)1, (ushort)0);
                    await W(WsPacket(new WorldSaved((uint)br.Count, i, (byte)(Protocol.SaveKindBranchFlag | id.Item1), id.Item2, id.Item3, Convert.FromHexString(br[i]))));
                }
                Say($"JOIN 0x{joinId:x8}: branch replayed ({br.Count}, newest {br[^1][..8]})");
                await W(tx.BuildOfferPacket());
                bool done = false; double tDone = 0; string end = "";
                while (end == "")
                {
                    foreach (var pkt in tx.TakeSendable()) await W(pkt);
                    var rt = joinMsgs.Reader.ReadAsync(hard.Token).AsTask();
                    double left = done ? 300 - (Clock.Elapsed.TotalSeconds - tDone) : 60;
                    if (await Task.WhenAny(rt, Task.Delay(TimeSpan.FromSeconds(Math.Max(1, left)), hard.Token)) != rt)
                    { await W(WorldReceiver.BuildAbort(joiner, joinId, Protocol.JoinAbortTimeout)); end = "timeout"; break; }
                    var (ty, body) = await rt;
                    switch (ty)
                    {
                        case Protocol.WorldAckDown: tx.OnAck(BinaryPrimitives.ReadUInt32LittleEndian(body), out _); break;
                        case Protocol.WorldDoneDown:
                            done = true; tDone = Clock.Elapsed.TotalSeconds;
                            await W(JoinStatusCodec.Build(joiner, joinId, Protocol.JoinStateWaitingReady, 0, 0));
                            Say(FormattableString.Invariant($"JOIN 0x{joinId:x8}: Done after {tDone - tr:F2} s; waiting for Ready"));
                            break;
                        case Protocol.JoinerReadyDown: end = "ready"; break;
                        case Protocol.JoinAbortDown: end = "abort " + Protocol.JoinAbortName(body.Length > 0 ? body[0] : (byte)0); break;
                    }
                }
                double now = Clock.Elapsed.TotalSeconds;
                await W(JoinStatusCodec.Build(joiner, joinId, Protocol.JoinStateResumed, Protocol.JoinReasonId(end == "ready" ? "ready" : "failed"), (ushort)(now - tr)));
                Say(FormattableString.Invariant($"JOIN 0x{joinId:x8} ended: {end} after {now - tr:F1} s -- THE HOST RESUMES"));
                if (end == "ready")   // WO-134: this world's chest ledger, as a real host sends it
                {
                    var lps = Wo134Rules.LedgerParts(ledger);
                    foreach (var lp in lps) await W(new LootMsg(Protocol.LootHostLedger, 0, lp).BuildUp(Protocol.LootHostUp, joiner));
                    Say($"LEDGER sent to {joiner}: {ledger.Entries.Count} entries in {lps.Count} part(s)");
                }
            }
        });
        try
        {
            await foreach (var (type, p) in inbox.Reader.ReadAllAsync(hard.Token))
            {
                if (type == Protocol.ItemDropDown && p.Length == Protocol.ItemDropDownPayloadLen)   // WO-134
                {
                    Say(FormattableString.Invariant($"GOT ItemDrop from={p[0]} drop={BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(1))} class={new Guid(p.AsSpan(5, 16))} x{BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(21))} hp={BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(23)):F2} at=({BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(27)):F2}, {BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(31)):F2}, {BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(35)):F2})"));
                    continue;
                }
                if (type == Protocol.ItemClaimDown && p.Length == Protocol.ItemClaimDownPayloadLen)   // WO-134
                {
                    Say($"GOT ItemClaim claimer={p[0]} drop={BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(1))}");
                    continue;
                }
                if (!Protocol.IsJoinDown(type, p.Length) || Split(p) is not var (src, jid, body)) continue;
                if (type == Protocol.LeashStateDown)   // WO-114: logged on a change only
                {
                    if (LeashState.TryDecode(body, out var ls) && (!leashSeen.TryGetValue(src, out var prev) || prev != ls))
                    {
                        leashSeen[src] = ls;
                        Say($"LEASHSTATE from {src}: flags={Protocol.LeashFlagsText(ls.Flags)} pull #{ls.PullSeq} {Protocol.LeashResultName(ls.Result)} from={ls.FromM} to={ls.ToM} residual_cm={ls.ResidualCm}");
                    }
                    continue;
                }
                if (type == Protocol.LootAskDown && LootMsg.TryDecode(body, out var la))   // WO-134: answered like a real host
                {
                    var f = la.Text.Split(' ');
                    Say($"LOOTASK {Protocol.LootAskName(la.Kind)} tok={la.Tok} from {src}: {la.Text}");
                    switch (la.Kind)
                    {
                        case Protocol.LootAskBodyOpen:
                        {
                            var items = bodies.GetValueOrDefault(f[0]);
                            string bst = items is null ? $"{f[0]} open 2 1 1 -" : $"{f[0]} open 1 1 1 {Wo134Rules.FormatItems(items)}";
                            await W(new LootMsg(Protocol.LootHostBodyState, la.Tok, bst).BuildUp(Protocol.LootHostUp, src));
                            Say($"  -> BodyState {bst}");
                            break;
                        }
                        case Protocol.LootAskBodyTake:
                        {
                            var items = bodies.GetValueOrDefault(f[0]);
                            var cls = Guid.Parse(f[1]); int amt = int.Parse(f[2], CultureInfo.InvariantCulture);
                            int i = items?.FindIndex(x => x.Cls == cls && x.Amt >= amt) ?? -1;
                            string v = i >= 0 ? "ok" : "gone";
                            if (i >= 0) { var it = items![i]; if (it.Amt == amt) items.RemoveAt(i); else items[i] = it with { Amt = it.Amt - amt }; }
                            await W(new LootMsg(Protocol.LootHostTakeResult, la.Tok, $"{v} {f[0]} {f[1]} {f[2]}").BuildUp(Protocol.LootHostUp, src));
                            Say($"  -> TakeResult {v}");
                            break;
                        }
                        case Protocol.LootAskBodyPut:
                            if (bodies.TryGetValue(f[0], out var pitems)) pitems.Add(new Wo134Rules.Item(Guid.Parse(f[1]), int.Parse(f[2], CultureInfo.InvariantCulture), float.Parse(f[3], CultureInfo.InvariantCulture), false));
                            break;
                        case Protocol.LootAskTakedown:   // WO-135: the host's world performs it -- here: ok, and the NPC's stream says the result
                        {
                            string res = f.Length == 2 && npcFlags.ContainsKey(f[0]) ? "ok" : "refused";
                            if (res == "ok") npcFlags[f[0]] = f[1] == "knockout" ? (byte)2 : (byte)1;
                            await W(new LootMsg(Protocol.LootHostTakedownResult, la.Tok, $"{res} {f[0]} {(f.Length > 1 ? f[1] : "mercy")}").BuildUp(Protocol.LootHostUp, src));
                            Say($"  -> TakedownResult {res} (stream flags now {npcFlags.GetValueOrDefault(f[0])})");
                            break;
                        }
                        case Protocol.LootAskItemTake:
                        {
                            var cls = Guid.Parse(f[0]);
                            float x = float.Parse(f[1], CultureInfo.InvariantCulture), y = float.Parse(f[2], CultureInfo.InvariantCulture), z = float.Parse(f[3], CultureInfo.InvariantCulture);
                            int i = witems.FindIndex(w => w.Cls == cls && (w.X - x) * (w.X - x) + (w.Y - y) * (w.Y - y) + (w.Z - z) * (w.Z - z) <= 0.35f * 0.35f);
                            string v = i < 0 ? "unknown" : witems[i].Taken ? "gone" : "ok";
                            if (i >= 0) witems[i] = witems[i] with { Taken = true };
                            await W(new LootMsg(Protocol.LootHostItemResult, la.Tok, $"{v} {f[0]} {f[1]} {f[2]} {f[3]}").BuildUp(Protocol.LootHostUp, src));
                            Say($"  -> ItemResult {v}");
                            break;
                        }
                    }
                    continue;
                }
                if (type == Protocol.QuestAskDown && LootMsg.TryDecode(body, out var qa))   // WO-137: answered like a real host
                {
                    Say($"QUESTASK {Protocol.QuestAskName(qa.Kind)} tok={qa.Tok} from {src}: {qa.Text}");
                    if (!qauto) continue;
                    switch (qa.Kind)
                    {
                        case Protocol.QuestAskRequest when Wo137Rules.TryParseRequestText(qa.Text, out var rq):
                        {
                            bool known = qstate.TryGetValue(rq.Path, out var cur);
                            int hv = known ? cur.Val : rq.Old;
                            var v = Wo137Rules.Judge(true, hv, rq.Old, rq.New);
                            if (v == Wo137Rules.Verdict.Apply)
                            {
                                string ct = $"{++qseq} {QuestChange.FNotify | QuestChange.FOldOk | QuestChange.FNewOk | QuestChange.FMirror} {rq.Old} {rq.New} {rq.Port} {rq.QuestLen} {rq.Path}";
                                qstate[rq.Path] = (rq.New, rq.Port, rq.QuestLen);
                                for (byte g = 1; g < 8; g++) await W(new LootMsg(Protocol.QuestHostChange, 0, ct).BuildUp(Protocol.QuestHostUp, g));
                                await W(new LootMsg(Protocol.QuestHostResult, qa.Tok, Wo137Rules.ResultText("applied", rq.New, rq.Port, rq.Path)).BuildUp(Protocol.QuestHostUp, src));
                                Say($"  -> applied (change #{qseq} to every joiner, then Result applied)");
                            }
                            else
                            {
                                string verdict = v == Wo137Rules.Verdict.Already ? "already" : "refused";
                                await W(new LootMsg(Protocol.QuestHostResult, qa.Tok, Wo137Rules.ResultText(verdict, hv, known ? cur.Port : "", rq.Path)).BuildUp(Protocol.QuestHostUp, src));
                                Say($"  -> {verdict} (this host has {hv})");
                            }
                            break;
                        }
                        case Protocol.QuestAskTalk when Wo137Rules.TryParseTalkText(qa.Text, out bool ton, out string tnpc):
                            await W(new LootMsg(Protocol.QuestHostHold, qa.Tok, Wo137Rules.TalkText(ton, tnpc)).BuildUp(Protocol.QuestHostUp, src));
                            Say($"  -> Hold {(ton ? "on" : "off")} {tnpc} (this host's {tnpc} {(ton ? "is busy" : "is free again")})");
                            break;
                        case Protocol.QuestAskResync:
                            foreach (var t in Wo137Rules.CheckpointTexts(qstate.Select(kv => new Wo137Rules.CheckpointEntry(kv.Key, kv.Value.Val, kv.Value.Port)).ToList()))
                                await W(new LootMsg(Protocol.QuestHostCheckpoint, 0, t).BuildUp(Protocol.QuestHostUp, src));
                            Say($"  -> Checkpoint of {qstate.Count} State(s)");
                            break;
                        default:
                            Say("  -> not answered (malformed)");
                            break;
                    }
                    continue;
                }
                if (type == Protocol.JoinRequestDown) { Say($"JoinRequest 0x{jid:x8} from {src}"); await joinQ.Writer.WriteAsync((src, jid)); }
                else await joinMsgs.Writer.WriteAsync((type, body));
            }
        }
        catch (OperationCanceledException) { Say("stopped"); }
        finally { tcp.Dispose(); }
        return 0;
    }
}
