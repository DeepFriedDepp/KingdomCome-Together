// WO-121 live test tool (tools/wo121, never shipped): a synthetic v8 PEER whose
// avatar does scripted things -- the stand-in for the second player in a solo
// session. Connects to a real local relay FIRST (lowest id, so it is the
// session's authority/host when two loopback clients are present, relay rule 2),
// then plays a timed scenario:
//
//   at <t> stand <x> <y> <z> <yawRad>          put the avatar there, still
//   at <t> move <speedMps> <headingRad> <secs> [zEnd]  walk/run on a heading (zEnd: z moves linearly to it; the state
//                                              block's speed = speed; facing = heading)
//   at <t> strafe <speedMps> <moveDirRad> <secs>  move at moveDir relative to the facing
//   at <t> state k=v ...                       bits/zones: crouch=0|1 combat=0|1 block=0|1
//                                              locked=0|1 gz=<zone> gs=<left|right|none> az=<zone>
//                                              (zones by table name: head upper_left upper_right
//                                              lower_left lower_right lower undefined)
//   at <t> jump [heightM] [secs]               a Jump event + a REAL Z arc on the stream
//   at <t> attack <rowGuid> [zone]             a v8 Attack event with that row
//   at <t> block_impulse <rowGuid> [perfect]   a BlockImpulse row event
//   at <t> dodge <rowGuid>                     a Dodge row event
//   at <t> ff on|off                           a SessionSetting (friendly fire) -- valid only as host
//   at <t> hit <hp> <st> [unarmed]             a PlayerHit 0x44 on the joiner (the agent's id)
//   at <t> leash on|off [flag,...]             (WO-114) act as a JOINER for the host's leash: a LeashState
//                                              (0x5A) every second with these flags (default in-world;
//                                              names: in-world downed loading cutscene dialogue menu mounted)
//   at <t> leash flags <flag,...>              change the reported flags (none = not in the world)
//   at <t> leash obey on|busy|fail             what a Pull does: move 3 m east of the host and report
//                                              placed (on), refuse as busy, or report not-placed (fail)
//   at <t> death                               (WO-131) a PlayerDeath (0x23): "I died"
//   at <t> respawned <x> <y> <z>               (WO-131) a PlayerRespawned (0x3E), then stand there
//   at <t> vitals <hp> <st> [downed]           (WO-131; WO-132 downed = the unconscious bit 0x01) a PlayerState (0x1F): the owner's own health (hp > 0 clears
//                                              the host's death tag, as a real joiner's vitals do); also sent
//                                              every 2 s at 100/100 once `vitals` or `respawned` ran
//   at <t> appearance mirror|<guid,...>        (WO-131) an Appearance (0x1A): the host's own item classes
//                                              as last received (mirror), or the given item-class GUIDs
//   at <t> timeskip start|done|sync <kind> <worldTime>  (WO-133) a TimeSkipUp (0x28) as a joiner's sleep/wait/
//                                              clock jump would send it (kind 0 sleep, 1 wait, 2 fast travel)
//   at <t> story objective|fingerprint|approach <text>  (WO-133) a StoryBeatUp (0x37) of that kind
//   at <t> itemdrop <classGuid> <amount> <health> <x> <y> <z> [dropId]  (WO-134) an ItemDropUp (0x32): "I dropped this"
//   at <t> itemclaim <dropId>                  (WO-134) an ItemClaimUp (0x34): "I picked that drop up"
//                                              every ItemDropDown (0x33) / ItemClaimDown (0x35) received is printed
//   at <t> loot open <body> | take|put <body> <cls> <amt> <hp> | item <cls> <x> <y> <z> [fromBody]
//                                              (WO-134) a LootAsk (0x5C) to the host, as a joiner's agent sends it (tok = a counter);
//                                              every LootHostDown (0x5F) received is printed
//   at <t> takedown <body> <mercy|knockout|stealth>  (WO-135) a LootAsk Takedown (0x5C kind 5): the game's takedown on a host NPC
//   at <t> ride <horseName>|off                (WO-136) mounted on that (host) horse: a HorseInfo (0x2A) and the riding
//                                              flag on every Position packet; off = HorseInfo "-", riding cleared
//   at <t> torch on|off                        (WO-136) the state block's torch bit (0x20): the player holds a lit torch
//   at <t> quest request <flags> <old> <new> <port> <questLen> <path>   (WO-137) a QuestAsk Request to the host, as a
//                                              joiner's agent sends its own quest step (tok = a counter)
//   at <t> quest talk on|off <npc>             (WO-137) a QuestAsk Talk: this joiner talks to its copy of that NPC
//   at <t> quest resync <why>                  (WO-137) a QuestAsk Resync: "my world has just loaded"
//   at <t> crime report <kind> <x> <y> <z> <victim|-> <cls|-> <where>   (WO-139) a CrimeAsk Report: this joiner's
//                                              crime, as his agent sends it (tok = a counter)
//   at <t> crime outcome <stopTok> <result> <guard|-> <fine> <x> <y> <z>   (WO-139) a CrimeAsk Outcome: the stop's end
//   at <t> crime resync <why>                  (WO-139) a CrimeAsk Resync; every CrimeHostDown (0x67) received is printed
//                                              every QuestHostDown (0x61) received is printed; --quest-rec F appends
//                                              each Change's text to F (synthpeer `questfile F` replays it)
//   at <t> sleep auto yes|no|busy|none        (WO-140) answer every SleepVote Ask by itself (default none: no answer)
//   at <t> sleep ask sleep|wait <save 0|1>     (WO-140) this joiner asks the host to sleep (tok = id<<16 | n)
//   at <t> sleep answer <tok> yes|no|busy      (WO-140) answer an Ask
//   at <t> sleep begin <tok> <hours> <save>    (WO-140) this joiner (the asker) chose the length
//   at <t> sleep cancel <tok> <why>            (WO-140) backed-out | woke | ...; every SleepVoteDown (0x69) is printed
//   end <t>                                    stop
//
// Position packets: every 30 ms while moving, 2 s heartbeat still; the state
// block is change-gated exactly like the agent (and heartbeated at 1 s while
// non-zero). Every received PlayerHit (0x45) and ActionDown is printed.
//
// usage: AvatarPeer --scenario s.txt [--host 127.0.0.1] [--port 7778] [--name wo121-peer] [--skew-ms N]
//   --skew-ms N  (WO-129) every sender stamp this peer writes runs N ms ahead: an artificial clock skew
//   --record F   (WO-131) append every NpcStateDown (0x27), ActionDown (0x3C), NpcDamageDown (0x31) and (WO-138) PauseDown (0x1D)
//                this peer receives to F as "<ms> <type hex> <payload hex>" -- the host's outgoing
//                stream, for tools/wo131/replay.py -> synthpeer `raw` lines into a joiner game
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using KcdMp.Client;
using KcdMp.Wire;

static class P
{
    static string Arg(string[] a, string k, string d) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : d; }
    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    static long SkewMs;   // WO-129: --skew-ms
    static uint Ms() => unchecked((uint)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency + SkewMs));

    record Step(double T, string[] F);

    static WireZone Zone(string n) => n switch
    {
        "head" => WireZone.Head, "upper_left" => WireZone.UpperLeft, "upper_right" => WireZone.UpperRight,
        "lower_left" => WireZone.LowerLeft, "lower_right" => WireZone.LowerRight, "lower" => WireZone.Lower, _ => WireZone.Undefined,
    };
    static WireGuardStance Stance(string n) => n switch { "left" => WireGuardStance.Left, "right" => WireGuardStance.Right, _ => WireGuardStance.None };

    static async Task<int> Main(string[] a)
    {
        string host = Arg(a, "--host", "127.0.0.1"); int port = int.Parse(Arg(a, "--port", "7778"));
        string name = Arg(a, "--name", "wo121-peer");
        SkewMs = long.Parse(Arg(a, "--skew-ms", "0"), CultureInfo.InvariantCulture);
        string release = File.ReadAllText(FindUp("VERSION")).Trim();
        var steps = new List<Step>(); double endT = 60;
        foreach (var raw in File.ReadAllLines(Arg(a, "--scenario", "scenario.txt")))
        {
            var t = raw.Trim(); if (t.Length == 0 || t.StartsWith('#')) continue;
            var f = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f[0] == "end") { endT = double.Parse(f[1], CultureInfo.InvariantCulture); continue; }
            if (f[0] != "at") continue;
            steps.Add(new Step(double.Parse(f[1], CultureInfo.InvariantCulture), f[2..]));
        }
        steps.Sort((x, y) => x.T.CompareTo(y.T));

        using var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(host, port);
        var st = tcp.GetStream();
        var nb = Encoding.UTF8.GetBytes(name); var rb = Encoding.UTF8.GetBytes(release);
        int hl = 2 + nb.Length + rb.Length; var hs = new byte[3 + hl];
        hs[0] = Protocol.Handshake; BinaryPrimitives.WriteUInt16LittleEndian(hs.AsSpan(1), (ushort)hl);
        hs[3] = Protocol.Version; hs[4] = (byte)nb.Length; nb.CopyTo(hs, 5); rb.CopyTo(hs, 5 + nb.Length);
        await st.WriteAsync(hs);
        var (ty, pl) = await ReadPacket(st, CancellationToken.None);
        if (ty != Protocol.Ack) { Console.WriteLine($"PEER refused: 0x{ty:X2} {Encoding.UTF8.GetString(pl)}"); return 1; }
        byte myId = pl[0];
        Console.WriteLine($"PEER connected id={myId} release={release} protocol=v{Protocol.Version} steps={steps.Count}");

        byte? joiner = null;
        var leashIn = new System.Collections.Concurrent.ConcurrentQueue<(byte Src, LeashCommand C)>();   // WO-114
        var sleepIn = new System.Collections.Concurrent.ConcurrentQueue<(byte Src, LootMsg M)>();        // WO-140
        string sleepAuto = "none";
        uint sleepN = 0;
        using var cts = new CancellationTokenSource();
        var inbox = new ActionInbox();
        string recPath = Arg(a, "--record", "");
        string questRecPath = Arg(a, "--quest-rec", "");   // WO-137
        StreamWriter? questRec = questRecPath.Length > 0 ? new StreamWriter(questRecPath, append: true) { AutoFlush = true } : null;
        StreamWriter? rec = recPath.Length > 0 ? new StreamWriter(recPath, append: true) { AutoFlush = true } : null;
        var recClock = Stopwatch.StartNew();
        byte[]? hostAppearance = null;   // WO-131: the last AppearanceDown classes (for `appearance mirror`)
        BodyState2Bits lastHostBits = BodyState2Bits.None;   // WO-135
        var reader = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var (t2, b2) = await ReadPacket(st, cts.Token);
                    if (rec is not null && (t2 == Protocol.NpcStateDown || t2 == Protocol.ActionDown || t2 == Protocol.NpcDamageDown || t2 == Protocol.PauseDown || t2 == Protocol.ActivityHostDown || t2 == Protocol.ActivityExtraDown))   // WO-138: + the pause announcement; WO-141: + activities; WO-143: + hands, gaits, one-shots, looks
                        lock (rec) rec.WriteLine($"{recClock.ElapsedMilliseconds} {t2:X2} {Convert.ToHexString(b2)}");
                    if (t2 == Protocol.AppearanceDown && b2.Length >= 2 && b2[0] != myId) hostAppearance = b2.AsSpan(1).ToArray();   // [src][count][classes]
                    if (t2 == Protocol.Name && b2.Length >= 2 && b2[0] != myId) { joiner ??= b2[0]; }
                    else if (t2 == Protocol.Ghost && b2.Length >= 1 && b2[0] != myId)
                    {
                        joiner ??= b2[0];
                        // WO-135: the other player's state bits as they arrive (crouch capture, end to end)
                        if (PositionCodec.TryDecodeGhost(b2, out var gs) && gs.State2 is BodyState2 gst && gst.Bits != lastHostBits)
                        {
                            lastHostBits = gst.Bits;
                            Console.WriteLine($"PEER t={recClock.Elapsed.TotalSeconds:F1} got host state bits={gst.Bits} crouch={(gst.Bits.HasFlag(BodyState2Bits.Crouched) ? 1 : 0)}");
                        }
                    }
                    else if (t2 == Protocol.PlayerHitDown && b2.Length == Protocol.PlayerHitDownPayloadLen)   // WO-132: an NPC's hit on my avatar, from the host
                        Console.WriteLine(FormattableString.Invariant($"PEER t={recClock.Elapsed.TotalSeconds:F1} got NPC hit (0x22) hp={BinaryPrimitives.ReadSingleLittleEndian(b2):F2} st={BinaryPrimitives.ReadSingleLittleEndian(b2.AsSpan(4)):F2}"));
                    else if (t2 == Protocol.DamageDown && b2.Length == Protocol.DamageDownPayloadLen)   // WO-132: the guid route (a second path per hit)
                        Console.WriteLine(FormattableString.Invariant($"PEER t={recClock.Elapsed.TotalSeconds:F1} got GUID-ROUTE damage (0x13) from={b2[0]} soul={new Guid(b2.AsSpan(1, 16))} hp={BinaryPrimitives.ReadSingleLittleEndian(b2.AsSpan(21)):F2}"));
                    else if (t2 == Protocol.PlayerHitV8Down && PlayerHitV8.TryDecodeDown(b2, out byte att, out var h))
                        Console.WriteLine($"PEER got PlayerHit from ghost {att}: {h}");
                    else if (t2 == Protocol.ActionDown && inbox.Accept(b2, out _) is InboundAction ia)
                        Console.WriteLine($"PEER got action kind={ia.Kind} phase={ia.Phase} from={ia.SourceGhostId} len={ia.Payload.Length}");
                    else if (t2 == Protocol.TimeSkipDown && b2.Length == Protocol.TimeSkipDownPayloadLen)   // WO-133: the host's skips reach this peer
                        Console.WriteLine(FormattableString.Invariant($"PEER t={recClock.Elapsed.TotalSeconds:F1} got TimeSkip from={b2[0]} phase={b2[1]} kind={b2[2]} t={BinaryPrimitives.ReadUInt32LittleEndian(b2.AsSpan(3))}"));
                    else if (t2 == Protocol.ItemDropDown && b2.Length == Protocol.ItemDropDownPayloadLen)   // WO-134: a drop reaches this peer
                        Console.WriteLine(FormattableString.Invariant($"PEER t={recClock.Elapsed.TotalSeconds:F1} got ItemDrop from={b2[0]} drop={BinaryPrimitives.ReadUInt32LittleEndian(b2.AsSpan(1))} class={new Guid(b2.AsSpan(5, 16))} x{BinaryPrimitives.ReadUInt16LittleEndian(b2.AsSpan(21))} hp={BinaryPrimitives.ReadSingleLittleEndian(b2.AsSpan(23)):F2} at=({BinaryPrimitives.ReadSingleLittleEndian(b2.AsSpan(27)):F2}, {BinaryPrimitives.ReadSingleLittleEndian(b2.AsSpan(31)):F2}, {BinaryPrimitives.ReadSingleLittleEndian(b2.AsSpan(35)):F2})"));
                    else if (t2 == Protocol.ItemClaimDown && b2.Length == Protocol.ItemClaimDownPayloadLen)   // WO-134: a claim echo
                        Console.WriteLine($"PEER t={recClock.Elapsed.TotalSeconds:F1} got ItemClaim claimer={b2[0]}{(b2[0] == myId ? " (me)" : "")} drop={BinaryPrimitives.ReadUInt32LittleEndian(b2.AsSpan(1))}");
                    else if (t2 == Protocol.LootHostDown && b2.Length > 1 + Protocol.JoinHeaderLen
                             && LootMsg.TryDecode(b2.AsSpan(1 + Protocol.JoinHeaderLen), out var lm))   // WO-134
                        Console.WriteLine($"PEER t={recClock.Elapsed.TotalSeconds:F1} got LootHost {Protocol.LootHostName(lm.Kind)} tok={lm.Tok} from={b2[0]}: {lm.Text}");
                    else if (t2 == Protocol.QuestHostDown && b2.Length > 1 + Protocol.JoinHeaderLen
                             && LootMsg.TryDecode(b2.AsSpan(1 + Protocol.JoinHeaderLen), out var qm))   // WO-137
                    {
                        Console.WriteLine($"PEER t={recClock.Elapsed.TotalSeconds:F1} got QuestHost {Protocol.QuestHostName(qm.Kind)} tok={qm.Tok} from={b2[0]}: {qm.Text}");
                        if (questRec is not null && qm.Kind == Protocol.QuestHostChange) lock (questRec) questRec.WriteLine(qm.Text);
                    }
                    else if (t2 == Protocol.CrimeHostDown && b2.Length > 1 + Protocol.JoinHeaderLen
                             && LootMsg.TryDecode(b2.AsSpan(1 + Protocol.JoinHeaderLen), out var cm))   // WO-139
                        Console.WriteLine($"PEER t={recClock.Elapsed.TotalSeconds:F1} got CrimeHost {Protocol.CrimeHostName(cm.Kind)} tok={cm.Tok} from={b2[0]}: {cm.Text}");
                    else if (t2 == Protocol.ActivityExtraDown && b2.Length > 1 + Protocol.JoinHeaderLen
                             && ExtraCodec.TryDecode(b2.AsSpan(1 + Protocol.JoinHeaderLen), out byte xkind, out var xrows))   // WO-143
                    {
                        foreach (var xr in xrows)
                            Console.WriteLine($"PEER t={recClock.Elapsed.TotalSeconds:F1} got Extra kind={xkind} npc {xr.Name}: " + xkind switch
                            {
                                Protocol.ExtraKindHands => $"L={ExtraRow.ClassText(xr.Left)} R={ExtraRow.ClassText(xr.Right)}",
                                Protocol.ExtraKindGaits => $"gaits=0x{xr.Gaits:X3}",
                                Protocol.ExtraKindOneShot => $"one-shot {xr.Fragment} tags '{xr.Tags}' align {xr.AlignGuid:X16} flags {xr.Flags}",
                                _ => $"looks at kind {xr.TargetKind} '{xr.Target}'",
                            });
                    }
                    else if (t2 == Protocol.ActivityHostDown && b2.Length > 1 + Protocol.JoinHeaderLen
                             && ActivityCodec.TryDecode(b2.AsSpan(1 + Protocol.JoinHeaderLen), out byte akind, out var arows))   // WO-141
                    {
                        foreach (var ar in arows)
                            Console.WriteLine($"PEER t={recClock.Elapsed.TotalSeconds:F1} got Activity {(akind == Protocol.ActivityKindNpc ? "npc " + ar.Name : "player " + ar.Peer)}: {ar.A}");
                    }
                    else if (t2 == Protocol.SleepVoteDown && b2.Length > 1 + Protocol.JoinHeaderLen
                             && LootMsg.TryDecode(b2.AsSpan(1 + Protocol.JoinHeaderLen), out var svm))   // WO-140
                    {
                        Console.WriteLine($"PEER t={recClock.Elapsed.TotalSeconds:F1} got SleepVote {Protocol.SleepVoteName(svm.Kind)} tok=0x{svm.Tok:X8} from={b2[0]}: {svm.Text}");
                        sleepIn.Enqueue((b2[0], svm));
                    }
                    else if (t2 == Protocol.LeashDown && b2.Length > 1 + Protocol.JoinHeaderLen
                             && LeashCommand.TryDecode(b2.AsSpan(1 + Protocol.JoinHeaderLen), out var lc))
                        leashIn.Enqueue((b2[0], lc));   // WO-114: handled on the main loop
                }
            }
            catch { }
        });

        var outbox = new ActionOutbox();
        uint lootTok = 0;   // WO-134
        var sw = Stopwatch.StartNew();
        float x = 0, y = 0, z = 0, yaw = 0, speed = 0, head = 0, moveDir = 0, zRate = 0; double moveUntil = -1;
        double jumpT0 = -1, jumpDur = 0.8; float jumpH = 0.5f, zBase = 0;
        var s2 = new BodyState2(0, 0, BodyState2Bits.None, WireZone.Undefined, WireGuardStance.None, WireZone.Undefined, 0, 0, 0);
        BodyState2? lastSent = null; double lastSentT = -9, lastPos = -9, lastPing = 0; int si = 0; bool placed = false, frozen = false;
        bool riding = false;   // WO-136: ride <horse>
        double lastT = 0;
        float vitalsHp = -1, vitalsSt = -1; double lastVitals = -9;   // WO-131
        byte vitalsFlags = 0;   // WO-132: vitals <hp> <st> downed -> the unconscious bit (0x01), as a floored joiner sends
        // WO-114: the joiner's side of the leash (off until "leash on").
        bool leashOn = false; ushort leashFlags = Protocol.LeashFlagInWorld; string leashObey = "on";
        byte leashSeq = 0, leashResult = 0; ushort leashFrom = 0, leashTo = 0, leashRes = 0; double lastLeash = -9;
        static ushort LeashFlagsOf(string csv)
        {
            ushort f = 0;
            foreach (var n in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
                f |= n switch
                {
                    "in-world" => Protocol.LeashFlagInWorld, "downed" => Protocol.LeashFlagDowned, "loading" => Protocol.LeashFlagLoading,
                    "cutscene" => Protocol.LeashFlagCutscene, "dialogue" => Protocol.LeashFlagDialogue, "menu" => Protocol.LeashFlagMenu,
                    "mounted" => Protocol.LeashFlagMounted, "separate" => Protocol.LeashFlagSeparate, _ => (ushort)0,   // WO-140: in its own world
                };
            return f;
        }
        string? control = Arg(a, "--control", "") is { Length: > 0 } c ? c : null;
        long controlPos = 0; double lastPoll = 0;
        if (control is not null) File.WriteAllText(control, "");
        while (sw.Elapsed.TotalSeconds < endT)
        {
            double t = sw.Elapsed.TotalSeconds, dt = t - lastT; lastT = t;
            // Live control: every line appended to the control file runs now.
            if (control is not null && t - lastPoll > 0.05)
            {
                lastPoll = t;
                try
                {
                    using var fs = new FileStream(control, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fs.Length > controlPos)
                    {
                        fs.Seek(controlPos, SeekOrigin.Begin);
                        var buf = new byte[fs.Length - controlPos]; int n = fs.Read(buf, 0, buf.Length); controlPos += n;
                        int at = si;   // keep the file's order within one batch
                        foreach (var line in Encoding.UTF8.GetString(buf, 0, n).Split((char)10))
                        {
                            var ln = line.Trim(); if (ln.Length == 0 || ln.StartsWith('#')) continue;
                            if (ln == "quit") { endT = 0; break; }
                            steps.Insert(at++, new Step(t, ln.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
                        }
                    }
                }
                catch (IOException) { }
            }
            while (si < steps.Count && steps[si].T <= t)
            {
                var f = steps[si++].F;
                switch (f[0])
                {
                    case "freeze": frozen = true; break;      // stop sending (photo-mode shots: the native writer keeps rendering)
                    case "unfreeze": frozen = false; break;
                    case "stand": frozen = false; x = F(f[1]); y = F(f[2]); z = zBase = F(f[3]); yaw = F(f[4]); speed = 0; moveUntil = -1; placed = true; break;
                    case "move": speed = F(f[1]); head = F(f[2]); yaw = head; moveDir = 0; moveUntil = t + F(f[3]);
                        zRate = f.Length > 4 ? (F(f[4]) - z) / F(f[3]) : 0; break;   // WO-129: [zEnd] keeps a slope walk on the ground
                    case "strafe": speed = F(f[1]); moveDir = F(f[2]); head = yaw + moveDir; moveUntil = t + F(f[3]); break;
                    case "state":
                        foreach (var kv in f[1..])
                        {
                            var p = kv.Split('='); if (p.Length != 2) continue;
                            BodyState2Bits Bit(string k) => k switch { "crouch" => BodyState2Bits.Crouched, "combat" => BodyState2Bits.CombatMode, "block" => BodyState2Bits.BlockHeld, "locked" => BodyState2Bits.Locked, "torch" => BodyState2Bits.TorchLit, _ => 0 };
                            switch (p[0])
                            {
                                case "gz": s2 = s2 with { GuardZone = Zone(p[1]) }; break;
                                case "gs": s2 = s2 with { GuardStance = Stance(p[1]) }; break;
                                case "az": s2 = s2 with { AtkZone = Zone(p[1]) }; break;
                                default:
                                    var b = Bit(p[0]);
                                    s2 = s2 with { Bits = p[1] == "1" ? s2.Bits | b : s2.Bits & ~b };
                                    break;
                            }
                        }
                        Console.WriteLine($"PEER t={t:F1} state {s2}");
                        break;
                    case "jump":
                        if (f.Length > 1) jumpH = F(f[1]);
                        if (f.Length > 2) jumpDur = double.Parse(f[2], CultureInfo.InvariantCulture);
                        jumpT0 = t; zBase = z;
                        await Send(st, outbox.Build(ActionKind.Jump, ActionPhase.Commit, BitConverter.GetBytes(Ms())));
                        Console.WriteLine($"PEER t={t:F1} jump h={jumpH} dur={jumpDur}");
                        break;
                    case "attack":
                    {
                        var ev = new AttackEvent(Ms(), 1, f.Length > 2 ? Zone(f[2]) : WireZone.UpperRight, 1, 0, Guid.Parse(f[1]));
                        await Send(st, outbox.Build(ActionKind.Attack, ActionPhase.Commit, ev.ToBytes()));
                        Console.WriteLine($"PEER t={t:F1} attack {ev}");
                        break;
                    }
                    case "block_impulse":
                        await Send(st, outbox.Build(ActionKind.BlockImpulse, ActionPhase.Commit,
                            new RowEvent(Ms(), (byte)(f.Length > 2 && f[2] == "perfect" ? RowEvent.FlagPerfect : 0), Guid.Parse(f[1]), "").ToBytes()));
                        Console.WriteLine($"PEER t={t:F1} block_impulse {f[1]}");
                        break;
                    case "dodge":
                        await Send(st, outbox.Build(ActionKind.Dodge, ActionPhase.Commit, new RowEvent(Ms(), 0, Guid.Parse(f[1]), "").ToBytes()));
                        Console.WriteLine($"PEER t={t:F1} dodge {f[1]}");
                        break;
                    case "draw":
                    case "sheathe":
                    {
                        // The 0x2C combat event (v2: [event][sid:2]) -- the avatar's weapon draw/sheathe.
                        var ce = new byte[3 + 3]; ce[0] = Protocol.CombatEventUp; BinaryPrimitives.WriteUInt16LittleEndian(ce.AsSpan(1), 3);
                        ce[3] = f[0] == "draw" ? Protocol.CombatEventWeaponDrawn : Protocol.CombatEventWeaponSheathed;
                        await Send(st, ce);
                        Console.WriteLine($"PEER t={t:F1} {f[0]}");
                        break;
                    }
                    case "ff":
                        await Send(st, outbox.Build(ActionKind.SessionSetting, ActionPhase.Commit, [SessionSettingKey.FriendlyFire, f[1] == "on" ? (byte)1 : (byte)0]));
                        Console.WriteLine($"PEER t={t:F1} ff {f[1]} (as host)");
                        break;
                    case "npchit":   // npchit <npcName> <hp> <st>: this peer's attributed hit on a world NPC (0x30, WO-121 Phase 5)
                    {
                        var nhb = Encoding.UTF8.GetBytes(f[1]);
                        var pk = new byte[3 + 1 + nhb.Length + Protocol.NpcDamageFixedTail];
                        pk[0] = Protocol.NpcDamageUp;
                        BinaryPrimitives.WriteUInt16LittleEndian(pk.AsSpan(1), (ushort)(1 + nhb.Length + Protocol.NpcDamageFixedTail));
                        pk[3] = (byte)nhb.Length; nhb.CopyTo(pk, 4);
                        int o = 4 + nhb.Length;
                        BinaryPrimitives.WriteSingleLittleEndian(pk.AsSpan(o), F(f[3]));
                        BinaryPrimitives.WriteSingleLittleEndian(pk.AsSpan(o + 4), F(f[2]));
                        pk[o + 8] = (byte)(Protocol.DamageFlagSuppressHitReaction | Protocol.NpcDamageFlagAttributed);
                        await Send(st, pk);
                        Console.WriteLine($"PEER t={t:F1} npchit {f[1]} hp={f[2]} st={f[3]} attributed");
                        break;
                    }
                    case "hit":
                        if (joiner is byte j)
                        {
                            byte fl = (byte)(f.Length > 3 && f[3] == "unarmed" ? PlayerHitV8.FlagUnarmed : 0);
                            await Send(st, new PlayerHitV8(j, F(f[2]), F(f[1]), fl, 0).BuildUp());
                            Console.WriteLine($"PEER t={t:F1} hit ghost {j} hp={f[1]} st={f[2]} flags={fl}");
                        }
                        else Console.WriteLine($"PEER t={t:F1} hit: no joiner seen yet");
                        break;
                    case "vitals":   // WO-131
                        vitalsHp = F(f[1]); vitalsSt = F(f[2]); lastVitals = -9;
                        vitalsFlags = f.Length > 3 && f[3] == "downed" ? Protocol.PlayerStateFlagUnconscious : (byte)0;
                        Console.WriteLine($"PEER t={t:F1} vitals {vitalsHp}/{vitalsSt}{(vitalsFlags != 0 ? " DOWNED" : "")}");
                        break;
                    case "death":   // WO-131
                    {
                        var dp = new byte[3]; dp[0] = Protocol.PlayerDeathUp;
                        await Send(st, dp);
                        Console.WriteLine($"PEER t={t:F1} death sent");
                        break;
                    }
                    case "respawned":   // WO-131: respawned <x> <y> <z>
                    {
                        x = F(f[1]); y = F(f[2]); z = zBase = F(f[3]); speed = 0; moveUntil = -1; placed = true; frozen = false;
                        var rp = new byte[3 + Protocol.PlayerRespawnedUpPayloadLen]; rp[0] = Protocol.PlayerRespawnedUp;
                        BinaryPrimitives.WriteUInt16LittleEndian(rp.AsSpan(1), (ushort)Protocol.PlayerRespawnedUpPayloadLen);
                        BinaryPrimitives.WriteSingleLittleEndian(rp.AsSpan(3), x); BinaryPrimitives.WriteSingleLittleEndian(rp.AsSpan(7), y);
                        BinaryPrimitives.WriteSingleLittleEndian(rp.AsSpan(11), z); rp[15] = 1;
                        await Send(st, rp);
                        await Send(st, PositionCodec.BuildPosition(x, y, z, yaw, false, false, null, Ms()));
                        lastPos = t;
                        vitalsHp = 100; vitalsSt = 100; lastVitals = -9; vitalsFlags = 0;
                        Console.WriteLine(FormattableString.Invariant($"PEER t={t:F1} respawned at ({x:F1}, {y:F1}, {z:F1})"));
                        break;
                    }
                    case "appearance":   // WO-131: appearance mirror | <guid,...>
                    {
                        byte[]? classes = null;
                        if (f.Length > 1 && f[1] == "mirror") classes = hostAppearance;
                        else if (f.Length > 1)
                        {
                            var gs = f[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
                            classes = new byte[1 + gs.Length * 16]; classes[0] = (byte)gs.Length;
                            for (int gi = 0; gi < gs.Length; gi++) Guid.Parse(gs[gi]).ToByteArray().CopyTo(classes, 1 + gi * 16);
                        }
                        if (classes is null) { Console.WriteLine($"PEER t={t:F1} appearance: nothing to mirror yet"); break; }
                        var ap = new byte[3 + classes.Length]; ap[0] = Protocol.AppearanceUp;
                        BinaryPrimitives.WriteUInt16LittleEndian(ap.AsSpan(1), (ushort)classes.Length); classes.CopyTo(ap, 3);
                        await Send(st, ap);
                        Console.WriteLine($"PEER t={t:F1} appearance sent ({classes[0]} item classes)");
                        break;
                    }
                    case "timeskip":   // WO-133: timeskip start|done|sync <kind> <worldTime>
                    {
                        byte ph = f[1] switch { "start" => Protocol.TimeSkipPhaseStart, "sync" => Protocol.TimeSkipPhaseSync, _ => Protocol.TimeSkipPhaseDone };
                        var tp = new byte[3 + Protocol.TimeSkipUpPayloadLen]; tp[0] = Protocol.TimeSkipUp;
                        BinaryPrimitives.WriteUInt16LittleEndian(tp.AsSpan(1), (ushort)Protocol.TimeSkipUpPayloadLen);
                        tp[3] = ph; tp[4] = byte.Parse(f[2], CultureInfo.InvariantCulture);
                        BinaryPrimitives.WriteUInt32LittleEndian(tp.AsSpan(5), uint.Parse(f[3], CultureInfo.InvariantCulture));
                        await Send(st, tp);
                        Console.WriteLine($"PEER t={t:F1} timeskip sent {f[1]} kind={f[2]} t={f[3]}");
                        break;
                    }
                    case "story":   // WO-133: story objective|fingerprint|approach <text>
                    {
                        byte sk = f[1] switch { "fingerprint" => Protocol.StoryBeatKindFingerprint, "approach" => Protocol.StoryBeatKindApproach, _ => Protocol.StoryBeatKindObjective };
                        var body = StoryBeat.BuildUpPayload(sk, string.Join(' ', f.Skip(2)));
                        var sp = new byte[3 + body.Length]; sp[0] = Protocol.StoryBeatUp;
                        BinaryPrimitives.WriteUInt16LittleEndian(sp.AsSpan(1), (ushort)body.Length); body.CopyTo(sp, 3);
                        await Send(st, sp);
                        Console.WriteLine($"PEER t={t:F1} story sent {f[1]} ({body.Length} bytes)");
                        break;
                    }
                    case "itemdrop":   // WO-134: itemdrop <cls> <amt> <hp> <x> <y> <z> [dropId]
                    {
                        uint did = f.Length > 7 ? uint.Parse(f[7], CultureInfo.InvariantCulture) : (uint)Random.Shared.Next(1, int.MaxValue);
                        var dp = new byte[3 + Protocol.ItemDropUpPayloadLen]; dp[0] = Protocol.ItemDropUp;
                        BinaryPrimitives.WriteUInt16LittleEndian(dp.AsSpan(1), (ushort)Protocol.ItemDropUpPayloadLen);
                        BinaryPrimitives.WriteUInt32LittleEndian(dp.AsSpan(3), did);
                        Guid.Parse(f[1]).TryWriteBytes(dp.AsSpan(7, 16));
                        BinaryPrimitives.WriteUInt16LittleEndian(dp.AsSpan(23), ushort.Parse(f[2], CultureInfo.InvariantCulture));
                        BinaryPrimitives.WriteSingleLittleEndian(dp.AsSpan(25), F(f[3]));
                        BinaryPrimitives.WriteSingleLittleEndian(dp.AsSpan(29), F(f[4]));
                        BinaryPrimitives.WriteSingleLittleEndian(dp.AsSpan(33), F(f[5]));
                        BinaryPrimitives.WriteSingleLittleEndian(dp.AsSpan(37), F(f[6]));
                        await Send(st, dp);
                        Console.WriteLine($"PEER t={t:F1} itemdrop sent drop={did} class={f[1]} x{f[2]}");
                        break;
                    }
                    case "itemclaim":   // WO-134: itemclaim <dropId>
                    {
                        var cp = new byte[3 + Protocol.ItemClaimUpPayloadLen]; cp[0] = Protocol.ItemClaimUp;
                        BinaryPrimitives.WriteUInt16LittleEndian(cp.AsSpan(1), (ushort)Protocol.ItemClaimUpPayloadLen);
                        BinaryPrimitives.WriteUInt32LittleEndian(cp.AsSpan(3), uint.Parse(f[1], CultureInfo.InvariantCulture));
                        await Send(st, cp);
                        Console.WriteLine($"PEER t={t:F1} itemclaim sent drop={f[1]}");
                        break;
                    }
                    case "loot":   // WO-134
                    {
                        lootTok++;
                        byte k = f[1] switch { "open" => Protocol.LootAskBodyOpen, "take" => Protocol.LootAskBodyTake, "put" => Protocol.LootAskBodyPut, _ => Protocol.LootAskItemTake };
                        string text = f[1] == "item" && f.Length == 6 ? string.Join(' ', f.Skip(2)) + " 0" : string.Join(' ', f.Skip(2));
                        await Send(st, new LootMsg(k, lootTok, text).BuildUp(Protocol.LootAskUp, Protocol.JoinTargetHost));
                        Console.WriteLine($"PEER t={t:F1} loot {Protocol.LootAskName(k)} tok={lootTok}: {text}");
                        break;
                    }
                    case "takedown":   // WO-135
                    {
                        lootTok++;
                        string text = $"{f[1]} {f[2]}";
                        await Send(st, new LootMsg(Protocol.LootAskTakedown, lootTok, text).BuildUp(Protocol.LootAskUp, Protocol.JoinTargetHost));
                        Console.WriteLine($"PEER t={t:F1} takedown tok={lootTok}: {text}");
                        break;
                    }
                    case "ride":   // WO-136: ride <horse>|off
                    {
                        string hn = f.Length > 1 && f[1] != "off" ? f[1] : "-";
                        riding = hn != "-";
                        var hnb = Encoding.UTF8.GetBytes(hn);
                        var hip = new byte[3 + 1 + hnb.Length]; hip[0] = Protocol.HorseInfoUp;
                        BinaryPrimitives.WriteUInt16LittleEndian(hip.AsSpan(1), (ushort)(1 + hnb.Length));
                        hip[3] = (byte)hnb.Length; hnb.CopyTo(hip, 4);
                        await Send(st, hip);
                        await Send(st, PositionCodec.BuildPosition(x, y, z, yaw, riding, false, null, Ms()));
                        lastPos = t;
                        Console.WriteLine($"PEER t={t:F1} ride {(riding ? hn : "off")} (HorseInfo + the riding flag)");
                        break;
                    }
                    case "quest":   // WO-137: quest request ... | talk on|off <npc> | resync <why>
                    {
                        lootTok++;
                        byte k = f[1] switch { "request" => Protocol.QuestAskRequest, "talk" => Protocol.QuestAskTalk, "resync" => Protocol.QuestAskResync, _ => (byte)0 };
                        if (k == 0) { Console.WriteLine($"PEER t={t:F1} quest: unknown verb {f[1]}"); break; }
                        string text = string.Join(' ', f.Skip(2));
                        await Send(st, new LootMsg(k, lootTok, text).BuildUp(Protocol.QuestAskUp, Protocol.JoinTargetHost));
                        Console.WriteLine($"PEER t={t:F1} quest {Protocol.QuestAskName(k)} tok={lootTok}: {text}");
                        break;
                    }
                    case "sleep":   // WO-140: sleep auto|ask|answer|begin|cancel
                    {
                        if (f.Length > 2 && f[1] == "auto") { sleepAuto = f[2]; Console.WriteLine($"PEER t={t:F1} sleep auto={sleepAuto}"); break; }
                        byte sk; uint stok; string stext;
                        if (f[1] == "ask" && f.Length > 3) { sk = Protocol.SleepAsk; stok = ((uint)myId << 16) | (++sleepN & 0xFFFF); stext = Wo140Text.Ask(f[2], myId, f[3] == "1"); }
                        else if (f[1] == "answer" && f.Length > 3) { sk = Protocol.SleepAnswer; stok = Convert.ToUInt32(f[2], 16); stext = Wo140Text.Answer(f[3], (byte)(stok >> 16)); }
                        else if (f[1] == "begin" && f.Length > 4) { sk = Protocol.SleepBegin; stok = Convert.ToUInt32(f[2], 16); stext = Wo140Text.Begin("sleep", myId, float.Parse(f[3], CultureInfo.InvariantCulture), f[4] == "1"); }
                        else if (f[1] == "cancel" && f.Length > 3) { sk = Protocol.SleepCancel; stok = Convert.ToUInt32(f[2], 16); stext = Wo140Text.Cancel(f[3], myId); }
                        else { Console.WriteLine($"PEER t={t:F1} sleep: bad verb"); break; }
                        await Send(st, new LootMsg(sk, stok, stext).BuildUp(Protocol.SleepVoteUp, Protocol.JoinTargetHost));
                        Console.WriteLine($"PEER t={t:F1} sleep {Protocol.SleepVoteName(sk)} tok=0x{stok:X8}: {stext}");
                        break;
                    }
                    case "crime":   // WO-139: crime report ... | outcome <tok> ... | resync <why>
                    {
                        byte k = f[1] switch { "report" => Protocol.CrimeAskReport, "outcome" => Protocol.CrimeAskOutcome, "resync" => Protocol.CrimeAskResync, _ => (byte)0 };
                        if (k == 0) { Console.WriteLine($"PEER t={t:F1} crime: unknown verb {f[1]}"); break; }
                        uint ctok;
                        string text;
                        if (k == Protocol.CrimeAskOutcome) { ctok = uint.Parse(f[2], CultureInfo.InvariantCulture); text = string.Join(' ', f.Skip(3)); }
                        else { ctok = ++lootTok; text = string.Join(' ', f.Skip(2)); }
                        await Send(st, new LootMsg(k, ctok, text).BuildUp(Protocol.CrimeAskUp, Protocol.JoinTargetHost));
                        Console.WriteLine($"PEER t={t:F1} crime {Protocol.CrimeAskName(k)} tok={ctok}: {text}");
                        break;
                    }
                    case "activity":   // WO-141: activity none | stance <id> <objGuidHex> [cart] | unstance <id> <locGuidHex> -- this peer's own body
                    {
                        var act = ActivityState.None;
                        if (f.Length >= 4 && f[1] == "stance")
                            act = act with { Stance = byte.Parse(f[2], CultureInfo.InvariantCulture), StanceObj = ulong.Parse(f[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture) };
                        else if (f.Length >= 4 && f[1] == "unstance")
                            act = act with { Unstance = ushort.Parse(f[2], CultureInfo.InvariantCulture), UnstanceObj = ulong.Parse(f[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture) };
                        else if (f.Length >= 4 && f[1] == "minigame")   // WO-143: activity minigame <type> <objGuidHex> [stance <id> <objGuidHex>]
                        {
                            act = act with { Minigame = byte.Parse(f[2], CultureInfo.InvariantCulture), MinigameObj = ulong.Parse(f[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture) };
                            if (f.Length >= 7 && f[4] == "stance")
                                act = act with { Stance = byte.Parse(f[5], CultureInfo.InvariantCulture), StanceObj = ulong.Parse(f[6], NumberStyles.HexNumber, CultureInfo.InvariantCulture) };
                        }
                        act = act.Normalised();
                        await Send(st, ActivityCodec.BuildUp(Protocol.ActivityPeerUp, Protocol.JoinTargetHost, Protocol.ActivityKindPlayer, [new ActivityRow(myId, "", act)]));
                        Console.WriteLine($"PEER t={t:F1} activity {act}");
                        break;
                    }
                    case "torch":   // WO-136: torch on|off -- the state block's torch bit
                        s2 = s2 with { Bits = f.Length > 1 && f[1] == "on" ? s2.Bits | BodyState2Bits.TorchLit : s2.Bits & ~BodyState2Bits.TorchLit };
                        Console.WriteLine($"PEER t={t:F1} torch {(f.Length > 1 ? f[1] : "?")} state {s2}");
                        break;
                    case "leash":   // WO-114
                        if (f.Length > 1 && f[1] is "on" or "off") { leashOn = f[1] == "on"; if (f.Length > 2) leashFlags = LeashFlagsOf(f[2]); lastLeash = -9; }
                        else if (f.Length > 2 && f[1] == "flags") { leashFlags = LeashFlagsOf(f[2]); lastLeash = -9; }
                        else if (f.Length > 2 && f[1] == "obey") leashObey = f[2];
                        Console.WriteLine($"PEER t={t:F1} leash on={leashOn} flags={Protocol.LeashFlagsText(leashFlags)} obey={leashObey}");
                        break;
                }
            }
            // WO-140: the host's sleep votes; with `sleep auto` every Ask is answered here.
            while (sleepIn.TryDequeue(out var svq))
            {
                if (svq.M.Kind != Protocol.SleepAsk || sleepAuto is not ("yes" or "no" or "busy")) continue;
                if (!Wo140Text.TryAsk(svq.M.Text, out var sKind, out byte sAsker, out _)) continue;
                await Send(st, new LootMsg(Protocol.SleepAnswer, svq.M.Tok, Wo140Text.Answer(sleepAuto, sAsker)).BuildUp(Protocol.SleepVoteUp, Protocol.JoinTargetHost));
                Console.WriteLine($"PEER t={t:F1} sleep answer tok=0x{svq.M.Tok:X8}: {sleepAuto} (auto, to {sAsker}'s {sKind})");
            }
            // WO-114: the host's leash messages; a pull moves the avatar beside the host.
            while (leashIn.TryDequeue(out var li))
            {
                var lcmd = li.C;
                double dh = Math.Sqrt((x - lcmd.HostX) * (x - lcmd.HostX) + (y - lcmd.HostY) * (y - lcmd.HostY));
                Console.WriteLine(FormattableString.Invariant(
                    $"PEER t={t:F1} leash from host {li.Src}: {Protocol.LeashKindName(lcmd.Kind)} seq={lcmd.Seq} arg={lcmd.Arg} host_d={lcmd.DistM} m (own view {dh:F0} m) host=({lcmd.HostX:F1}, {lcmd.HostY:F1}, {lcmd.HostZ:F1})"));
                if (lcmd.Kind != Protocol.LeashKindPull) continue;
                leashSeq = lcmd.Seq; leashFrom = LeashCommand.Metres(dh);
                if (leashObey == "busy") { leashResult = Protocol.LeashResultBusy; leashTo = leashFrom; }
                else if (leashObey == "fail") { leashResult = Protocol.LeashResultNotPlaced; leashTo = leashFrom; }
                else
                {
                    x = lcmd.HostX + 3; y = lcmd.HostY; z = zBase = lcmd.HostZ; speed = 0; moveUntil = -1; placed = true; frozen = false;
                    leashResult = Protocol.LeashResultPlaced; leashTo = 3; leashRes = 0;
                    await Send(st, PositionCodec.BuildPosition(x, y, z, yaw, false, false, null, Ms()));
                    lastPos = t;
                }
                Console.WriteLine($"PEER t={t:F1} MP-LEASH pulled from={leashFrom} to={leashTo} residual=0.00 result={Protocol.LeashResultName(leashResult)} (synthetic joiner)");
                lastLeash = -9;
            }
            if (leashOn && t - lastLeash >= 1.0)
            {
                lastLeash = t;
                await Send(st, new LeashState(leashFlags, leashSeq, leashResult, leashFrom, leashTo, leashRes).Build());
            }
            if (vitalsHp >= 0 && t - lastVitals >= 2.0)
            {
                lastVitals = t;
                var vp = new byte[3 + Protocol.PlayerStateUpPayloadLen]; vp[0] = Protocol.PlayerStateUp;
                BinaryPrimitives.WriteUInt16LittleEndian(vp.AsSpan(1), (ushort)Protocol.PlayerStateUpPayloadLen);
                BinaryPrimitives.WriteSingleLittleEndian(vp.AsSpan(3), vitalsHp); BinaryPrimitives.WriteSingleLittleEndian(vp.AsSpan(7), vitalsSt);
                vp[11] = vitalsFlags;
                await Send(st, vp);
            }
            if (!placed || frozen) { await Task.Delay(5); continue; }
            bool moving = moveUntil > t && speed > 0;
            if (moving)
            {
                if (jumpT0 < 0) { z += zRate * (float)dt; zBase = z; }
                x += (float)(-Math.Sin(head) * speed * dt);
                y += (float)(Math.Cos(head) * speed * dt);
            }
            float zNow = zBase;
            if (jumpT0 >= 0)
            {
                double u = (t - jumpT0) / jumpDur;
                if (u >= 1) { jumpT0 = -1; }
                else zNow = zBase + (float)(4 * jumpH * u * (1 - u));   // a real arc: rises, peaks at jumpH, lands
            }
            else zBase = z;
            z = zNow;
            int dirQ = (int)Math.Round(moveDir * 128 / Math.PI); if (dirQ > 127) dirQ -= 256;
            s2 = s2 with { SpeedCm = (ushort)(moving ? speed * 100 : 0), MoveDir = (sbyte)(moving ? dirQ : 0) };
            bool changed = lastSent is not BodyState2 l || l != s2;
            bool nonZero = s2.SpeedCm != 0 || s2.Bits != 0;
            bool hb = nonZero && t - lastSentT >= 1.0;
            bool due = changed || hb;
            double period = moving || jumpT0 >= 0 ? 0.030 : 2.0;
            if (t - lastPos >= period || due)
            {
                lastPos = t;
                BodyState2? block = due ? s2 : null;
                if (due) { lastSent = s2; lastSentT = t; }
                await Send(st, PositionCodec.BuildPosition(x, y, z, yaw, riding, false, block, Ms()));
            }
            if (t - lastPing > 2) { lastPing = t; var ping = new byte[11]; ping[0] = Protocol.Ping; BinaryPrimitives.WriteUInt16LittleEndian(ping.AsSpan(1), 8); await Send(st, ping); }
            await Task.Delay(5);
        }
        Console.WriteLine("PEER done");
        cts.Cancel();
        return 0;
    }

    static readonly SemaphoreSlim WriteLock = new(1, 1);
    static async Task Send(NetworkStream s, byte[] p) { await WriteLock.WaitAsync(); try { await s.WriteAsync(p); } finally { WriteLock.Release(); } }

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
}
