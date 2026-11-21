// KUBA 30 / 32 (Kuba's fork, not the authors'): one brain per fight -- "the brain goes where the fight is".
// Stage 1 (GameBridge.Wo210.cs asks 2/3, npc_drive.cpp): a copy whose host NPC fights only the joiner runs its own brain on the joiner.
// KUBA 32 (19.11, replaces 30f's two brains): the joiner streams those copies to the host 30 times a second (KubaDuelUp: position, yaw,
// combat mode, full and upper body, its guard: block and zones), and the host's NPC is that brain's body there -- bound to the native
// writer like a figure (the copy's place), its combat automation off and its guard the copy's (the engagement hold), every move the
// copy's brain starts installed as the same fragment one render delay later (wo208.cpp op 153). Its brain is never paused: its target,
// weapon and senses stay. The host player's blow on it (or its brain turning to him) takes it back at once: the DLL gives its automation
// back and sets him as its target, this agent unbinds it and tells the partner (W203 kind 13), whose duel ends then. The joiner's blows on
// a duel copy carry his engine's verdict (NpcDamage flags 0x10 | 0x20 blocked), which the host's replay takes instead of its own roll.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

public partial class GameBridge
{
    private const byte KubaOpDuelRead = 240, KubaOpRemoteDuel = 241, KubaOpTakeVerdict = 242, KubaOpRemoteFrame = 243, KubaOpDuelRelease = 244;
    public const byte KubaDamageFlagVerdict = 0x10, KubaDamageFlagBlocked = 0x20;
    /// <summary>KUBA 32: W203 side-channel kinds of this fork (APPEND-ONLY after the authors' 11): 12 the joiner asks the host for NPCs'
    /// worn sets ("name,name,..."), 13 the host took an NPC of a partner's duel back ("name"), 14 a joiner's duel ended ("name": KUBA 33,
    /// the host's NPC is its own at once instead of after 1 s without frames).</summary>
    public const byte KubaW203WornAsk = 200, KubaW203DuelRelease = 201, KubaW203DuelOver = 202;   // 0.65 merge: 12+ are upstream's
    /// <summary>Bytes KUBA 32 adds to every duel body (wo208.h kKubaDuelExt): [flags][guardZone][guardStance][atkZone][hpPct][rsv].</summary>
    private const int KubaDuelExt = 6;
    /// <summary>The host's writer renders the partner's copy this far behind its frame's arrival -- the partner's own figure's delay
    /// (TUNE.GHOST_NATIVE_DELAY_MS 100), so his body and the NPC he fights stay in step on the host's screen (3 frames at 30 Hz).</summary>
    private const ushort KubaHostBindDelayMs = 100;

    private sealed class KubaHostDuel { public uint Eid; public long LastMs; public byte Src; public bool Started, Bound; public ushort Seq; }
    private readonly ConcurrentDictionary<string, KubaHostDuel> _kubaHostDuels = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _kubaHostCooldown = new(StringComparer.Ordinal);   // name -> no new duel before (ms)
    private long _kubaDuelFramesOut, _kubaDuelFramesIn, _kubaDuelVerdictsOut, _kubaHostTaken, _kubaReleasesIn;
    private Task? _kubaFastLoop;
    private int _kubaHostFrameBusy;

    /// <summary>The joiner: the 100 ms tick starts the 30 Hz duel loop once (it idles while no duel runs).</summary>
    private Task KubaDuelSendTickAsync()
    {
        if (W137Joiner && _kubaFastLoop is null) _kubaFastLoop = Task.Run(KubaFastLoopAsync);
        return Task.CompletedTask;
    }

    private async Task KubaFastLoopAsync()
    {
        Wo207Trace.Kind.Value = Protocol.KubaDuelUp;   // a frame's pipe call: before the background polls at the gate
        while (true)
        {
            try
            {
                await Task.Delay(33);
                if (W137Joiner) await KubaHelloTickAsync();   // KUBA 34: does the host run this fork?
                if (W137Joiner) await KubaArmedTickAsync();   // KUBA 37: does this player hold a weapon?
                if (W137Joiner) await KubaWornTickAsync();   // KUBA 32b: the NPCs near this player dressed as the host's (every 2 s inside)
                if (!W137Joiner || _kubaDuel.IsEmpty) continue;
                var r = await _combat.Wo163Async(KubaOpDuelRead, [(byte)Protocol.KubaDuelMaxBodies]);
                if (r is not { Ok: true } rr || rr.Payload.Length < 2 || rr.Payload[0] == 0) continue;
                if (rr.Payload.Length > Protocol.KubaDuelBodyMax) continue;
                await WriteJoinAsync(Protocol.BuildJoinUp(Protocol.KubaDuelUp, Protocol.JoinTargetHost, 0, rr.Payload));
                long n = Interlocked.Increment(ref _kubaDuelFramesOut);
                if (n == 1 || n % 900 == 0) Console.WriteLine($"KUBA-32 duel frames to the host: {n} (bodies now {rr.Payload[0]}, 30 a second)");
            }
            catch (Exception ex) { Console.WriteLine($"KUBA-32 duel frame not sent: {ex.GetType().Name}: {ex.Message}"); await Task.Delay(500); }
        }
    }

    /// <summary>The joiner, before a blow on a copy goes out: his engine's verdict on it when the copy is in his local duel
    /// (flags 0x10 [| 0x20 blocked]), else 0.</summary>
    private async Task<byte> KubaDuelVerdictFlagsAsync(string npc)
    {
        if (!KubaDuelActive(npc)) return 0;
        var nb = Encoding.ASCII.GetBytes(npc);
        if (nb.Length == 0 || nb.Length > 63) return 0;
        var args = new byte[1 + nb.Length];
        args[0] = (byte)nb.Length;
        nb.CopyTo(args, 1);
        var r = await _combat.Wo163Async(KubaOpTakeVerdict, args);
        if (r is not { Ok: true } rr || rr.Payload.Length < 1 || rr.Payload[0] == 0) return 0;
        byte outcome = rr.Payload[0];   // 1 hit, 2 blocked, 3 perfect, 4 broken
        byte f = (byte)(KubaDamageFlagVerdict | (outcome is 2 or 3 ? KubaDamageFlagBlocked : 0));
        long k = Interlocked.Increment(ref _kubaDuelVerdictsOut);
        if (k <= 40 || k % 50 == 0)
            Console.WriteLine($"KUBA-30 duel blow on {npc}: this engine's verdict {(outcome switch { 1 => "hit", 2 => "blocked", 3 => "perfect", 4 => "broken", _ => "?" })} goes with it [n={k}]");
        return f;
    }

    /// <summary>The host: one KubaDuelDown frame from a joiner -- each body's place to the writer, the whole frame to the DLL (op 153).</summary>
    private async Task KubaOnDuelFrameAsync(byte src, byte[] body)
    {
        if (!W137Host || body.Length < 1) return;
        Interlocked.Increment(ref _kubaDuelFramesIn);
        Wo207Trace.Kind.Value = Protocol.KubaDuelDown;   // a frame's pipe calls: before the background polls at the gate
        int n = body[0], o = 1;
        long now = Environment.TickCount64;
        long arrival = NativeNpcFeed.Now();
        bool any = false;
        for (int i = 0; i < n && i < Protocol.KubaDuelMaxBodies; i++)
        {
            if (o + 1 > body.Length) return;
            int nl = body[o];
            if (nl == 0 || nl > Protocol.MaxNpcNameLen || o + 1 + nl + 18 > body.Length) return;
            string name = Encoding.ASCII.GetString(body, o + 1, nl);
            o += 1 + nl;
            float x = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(o)), y = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(o + 4)),
                  z = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(o + 8)), yaw = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(o + 12));
            o += 16;
            byte nsc = body[o + 1];
            o += 2;
            if (nsc > 2 || o + nsc * Protocol.NpcAnimScopeLen + KubaDuelExt > body.Length) return;
            o += nsc * Protocol.NpcAnimScopeLen + KubaDuelExt;
            if (!NpcNamePattern.IsMatch(name) || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(yaw)) continue;
            if (_kubaHostCooldown.TryGetValue(name, out long until) && now < until) continue;   // just taken back: the partner's late frames

            var d = _kubaHostDuels.GetOrAdd(name, _ => new KubaHostDuel());
            d.LastMs = now; d.Src = src;
            if (!d.Started)
            {
                d.Started = true;
                var nb = Encoding.ASCII.GetBytes(name);
                var args = new byte[3 + nb.Length];
                args[0] = 1; args[1] = (byte)nb.Length; nb.CopyTo(args, 2); args[2 + nb.Length] = src;
                var r = await _combat.Wo163Async(KubaOpRemoteDuel, args);
                d.Eid = r is { Ok: true } rr && rr.Payload.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload) : 0;
                if (d.Eid != 0)
                {
                    var (ok, reason) = await _combat.NpcBindAsync(true, d.Eid, 0, x, y, z, KubaHostBindDelayMs, name);
                    d.Bound = ok;
                    Console.WriteLine($"KUBA-32 host duel start npc={name} eid=0x{d.Eid:X} from ghost {src} -- his copy's brain fights; here the writer " +
                                      $"{(ok ? "places it" : "REFUSED it (" + NativeNpcCodec.ReasonTag(reason) + ")")} and the DLL shows his copy's moves");
                }
                else Console.WriteLine($"KUBA-32 host duel npc={name}: no such NPC here -- the partner's frames for it are ignored");
                try { await ExecLuaAsync($"if KCD2MP_KubaHostDuel then KCD2MP_KubaHostDuel(\"{EscapeLua(name)}\", 1) end"); } catch { }
            }
            if (d.Eid == 0) continue;
            any = true;
            if (d.Bound)
                _nativeFeed.Enqueue(new NativeNpcSample(src, name, x, y, z, yaw, 0, unchecked(++d.Seq), 0, arrival));
        }
        if (!any) return;
        // the whole frame to the DLL (it keeps the bodies it drives); its answer: the NPCs it took back for the host player
        if (Interlocked.Exchange(ref _kubaHostFrameBusy, 1) == 1) return;   // one at a time: a late frame is dropped, the next one carries all
        try
        {
            var args = new byte[body.Length];
            body.CopyTo(args, 0);
            var r = await _combat.Wo163Async(KubaOpRemoteFrame, args);
            if (r is { Ok: true } rr && rr.Payload.Length >= 1 && rr.Payload[0] > 0)
            {
                int k = rr.Payload[0], p = 1;
                for (int j = 0; j < k && p < rr.Payload.Length; j++)
                {
                    int l = rr.Payload[p];
                    if (l == 0 || p + 1 + l > rr.Payload.Length) break;
                    string taken = Encoding.ASCII.GetString(rr.Payload, p + 1, l);
                    p += 1 + l;
                    await KubaHostTakenAsync(taken);
                }
            }
        }
        finally { Interlocked.Exchange(ref _kubaHostFrameBusy, 0); }
    }

    /// <summary>The host: the DLL took an NPC back (the host player's blow or aggro) -- unbound, cooled down, and its partner told.</summary>
    private async Task KubaHostTakenAsync(string name)
    {
        _kubaHostCooldown[name] = Environment.TickCount64 + 4000;
        if (!_kubaHostDuels.TryRemove(name, out var d)) return;
        Interlocked.Increment(ref _kubaHostTaken);
        if (d.Bound) { try { await _combat.NpcBindAsync(false, 0, 0, 0, 0, 0, 0, name); } catch { } }
        try { await ExecLuaAsync($"if KCD2MP_KubaHostDuel then KCD2MP_KubaHostDuel(\"{EscapeLua(name)}\", 0) end"); } catch { }
        await Wo203SendSideAsync(d.Src, KubaW203DuelRelease, name);
        Console.WriteLine($"KUBA-32 host duel taken back npc={name} eid=0x{d.Eid:X}: the host player has its aggro -- unbound, partner {d.Src} told to end his duel");
    }

    /// <summary>The joiner: the host took this NPC back -- its local duel ends now (op 154; npc_drive ends it on its next check).</summary>
    private async Task KubaOnDuelReleaseAsync(string name)
    {
        if (!W137Joiner || !NpcNamePattern.IsMatch(name)) return;
        Interlocked.Increment(ref _kubaReleasesIn);
        var nb = Encoding.ASCII.GetBytes(name);
        if (nb.Length == 0 || nb.Length > 63) return;
        var args = new byte[1 + nb.Length];
        args[0] = (byte)nb.Length; nb.CopyTo(args, 1);
        Wo207Trace.Kind.Value = Protocol.KubaDuelDown;
        var r = await _combat.Wo163Async(KubaOpDuelRelease, args);
        Console.WriteLine($"KUBA-32 the host took npc={name} back (its player's aggro) -- {(r is { Ok: true } rr && rr.Payload.Length >= 1 && rr.Payload[0] == 1 ? "the duel here ends" : "no duel with it here")}");
    }

    /// <summary>The host, 10 times a second: an NPC no duel frame came for in 1 s is its own again.</summary>
    private async Task KubaHostDuelTickAsync()
    {
        if (_kubaHostDuels.IsEmpty) return;
        long now = Environment.TickCount64;
        foreach (var kv in _kubaHostDuels)
        {
            if (now - kv.Value.LastMs < 1000 && W137Host) continue;
            await KubaHostEndAsync(kv.Key, null, "no duel frame from the partner for 1 s");
        }
    }

    /// <summary>The host: a partner's duel on this NPC is over -- the DLL gives it its own brain back (its target his figure), the writer
    /// lets it go. <paramref name="src"/>: only that partner's duel (null: any).</summary>
    private async Task KubaHostEndAsync(string name, byte? src, string why)
    {
        if (src is byte s && (!_kubaHostDuels.TryGetValue(name, out var cur) || cur.Src != s)) return;
        if (!_kubaHostDuels.TryRemove(name, out var d)) return;
        var nb = Encoding.ASCII.GetBytes(name);
        var args = new byte[2 + nb.Length];
        args[0] = 0; args[1] = (byte)nb.Length; nb.CopyTo(args, 2);
        Wo207Trace.Kind.Value = Protocol.KubaDuelDown;
        try { await _combat.Wo163Async(KubaOpRemoteDuel, args); } catch { }
        if (d.Bound) { try { await _combat.NpcBindAsync(false, 0, 0, 0, 0, 0, 0, name); } catch { } }
        try { await ExecLuaAsync($"if KCD2MP_KubaHostDuel then KCD2MP_KubaHostDuel(\"{EscapeLua(name)}\", 0) end"); } catch { }
        _kubaHostCooldown[name] = Environment.TickCount64 + 1500;   // the partner's frames still on the way start nothing
        Console.WriteLine($"KUBA-32 host duel end npc={name} eid=0x{d.Eid:X} -- {why}: its own brain again");
    }

    /// <summary>KUBA 33b (joiner, 20.11: "when the NPC changes brain it fights as if with fists until I hit it, then the weapons are back"):
    /// a resumed duel brain of a villager goes for its fists, and the copy-weapon sync (WO-167 C2) believed it still held the host NPC's
    /// weapon, so nothing drew it until the next engage. At a duel's start (twice: the brain picks its fists a moment after it resumes) and
    /// at its end, the host NPC's weapon class is drawn again, forced. A host NPC with nothing (or nothing known) in its hand: left alone.</summary>
    private async Task KubaDuelWeaponAsync(string name, bool start)
    {
        if (!W137Joiner || !_w167CopyWeapons || !NpcNamePattern.IsMatch(name)) return;
        int[] waits = start ? [400, 1600] : [250];
        foreach (int ms in waits)
        {
            await Task.Delay(ms);
            if (start != _kubaDuel.ContainsKey(name)) return;   // it ended (or started again) meanwhile: that edge does its own
            string cls = Wo190Rules.WeaponToHold(_w167HostWeapon.TryGetValue(name, out var w) ? w : null);
            // KUBA 35 (20.11: "the guards walk without a weapon in hand but have one in their inventory"): their hand rows say empty, so
            // the duel's start drew nothing and the resumed brain fought with its fists. At the start, an empty or unknown hand draws the
            // copy's own weapon ("?": the class it held last, else its own draw); at the end the stream decides as before.
            if (cls is "?" or "-")
            {
                if (!start) return;
                // KUBA 37: against bare hands the brain fights with its fists (the vanilla way) -- a forced draw only came out and went
                // back a moment later; this player's weapon draws it (combat_forceArmed, and KubaOnArmed's draw)
                if (!_kubaPlayerArmed)
                {
                    long b = Interlocked.Increment(ref _kubaDuelBareSkipped);
                    if (b <= 20 || b % 50 == 0)
                        Console.WriteLine($"KUBA-37 duel start npc={name}: this player holds no weapon -- no draw, its brain's fists (fists against fists) [n={b}]");
                    return;
                }
                cls = "?";
            }
            try { await ExecLuaAsync($"if KCD2MP_W167Weapon then KCD2MP_W167Weapon(\"{EscapeLua(name)}\", \"{cls}\", \"kuba-duel-{(start ? "start" : "end")}\", true) end"); }
            catch { return; }
            long k = Interlocked.Increment(ref _kubaDuelWeaponTold);
            if (k <= 40 || k % 50 == 0)
                Console.WriteLine($"KUBA-33b duel {(start ? "start" : "end")} npc={name}: " +
                                  (cls == "?" ? "no weapon known in the host NPC's hand -- the copy's own weapon drawn" : $"the host NPC's weapon {cls[..Math.Min(8, cls.Length)]} drawn in the copy's hand again") + $" [n={k}]");
        }
    }
    private long _kubaDuelWeaponTold, _kubaDuelBareSkipped;

    /// <summary>KUBA 33d (joiner, 20.11: "watching the host fight NPCs, they always fight with fists"): the host's hand row (WO-143) names its
    /// NPC's weapon once, when it changes -- for a villager long before its fight (10:55:47, the fight at 11:01:25) -- and WO-167 C2 told the
    /// copy only on a change inside a fight, so its weapon was never drawn: fists against the host's figure. Now, as the host's NPC's fight
    /// starts (WO-164's combat word after 10 s without), its known weapon is drawn in the copy's hand, forced, and once more 1.5 s later.</summary>
    private async Task KubaHostFightWeaponAsync(string npc)
    {
        if (!W137Joiner || !_w167CopyWeapons || !NpcNamePattern.IsMatch(npc) || _kubaDuel.ContainsKey(npc)) return;
        foreach (int ms in new[] { 0, 1500 })
        {
            if (ms > 0) await Task.Delay(ms);
            string cls = Wo190Rules.WeaponToHold(_w167HostWeapon.TryGetValue(npc, out var w) ? w : null);
            if (cls is "?" or "-") return;   // nothing known in its hand, or truly empty-handed: the rows decide
            try { await ExecLuaAsync($"if KCD2MP_W167Weapon then KCD2MP_W167Weapon(\"{EscapeLua(npc)}\", \"{cls}\", \"kuba-host-fight\", true) end"); }
            catch { return; }
            long k = Interlocked.Increment(ref _kubaFightWeaponTold);
            if (k <= 40 || k % 50 == 0)
                Console.WriteLine($"KUBA-33d npc={npc}: its host NPC fights -- its weapon {cls[..Math.Min(8, cls.Length)]} drawn in the copy's hand [n={k}]");
        }
    }
    private long _kubaFightWeaponTold;

    /// <summary>KUBA 33c (host): a partner's blow on this NPC was applied here -- the DLL makes his figure its opponent (op 155: the logical
    /// target and the game's forced-target pin, 30 s after his last blow). His copy then sees its NPC fight him, and his duel takes it.</summary>
    private async Task KubaAggroToPartnerAsync(byte src, string name)
    {
        if (!W137Host || !NpcNamePattern.IsMatch(name)) return;
        var nb = Encoding.ASCII.GetBytes(name);
        if (nb.Length == 0 || nb.Length > 63) return;
        var args = new byte[2 + nb.Length];
        args[0] = src; args[1] = (byte)nb.Length; nb.CopyTo(args, 2);
        try
        {
            var r = await _combat.Wo163Async(KubaOpAggro, args);
            byte v = r is { Ok: true } rr && rr.Payload.Length >= 1 ? rr.Payload[0] : (byte)255;
            long k = Interlocked.Increment(ref _kubaAggroAsks);
            if (k <= 40 || k % 50 == 0)
                Console.WriteLine($"KUBA-33c partner {src}'s blow on {name}: {(v switch { 1 => "it fights his figure now", 2 => "his duel has it already", 0 => "nothing to switch (no figure or NPC here)", _ => "the DLL did not answer" })} [n={k}]");
        }
        catch (Exception ex) { Console.WriteLine($"KUBA-33c aggro for {name} not sent to the DLL: {ex.Message}"); }
    }
    private const byte KubaOpAggro = 245;
    private long _kubaAggroAsks;

    /// <summary>The joiner: its local duel with this copy ended (the DLL's ask 3) -- the host is told at once (W203 kind 14).</summary>
    private Task KubaDuelOverToHostAsync(string name) =>
        W137Joiner && NpcNamePattern.IsMatch(name) ? Wo203SendSideAsync(Protocol.JoinTargetHost, KubaW203DuelOver, name) : Task.CompletedTask;

    // KUBA 34 (20.11: the host played the original mod -- the joiner's copies fought with their own brain, the host's NPCs another fight):
    // the joiner says hello (W203 kind 15) every 3 s until the host answers (kind 16; an original host ignores both), then every 20 s;
    // the DLL lets a copy's brain fight its duel only while the last answer is under 75 s old (op 156, told again every 10 s).
    public const byte KubaW203Hello = 203, KubaW203HelloAck = 204;
    private const byte KubaOpHostOk = 246;
    private const string KubaForkStamp = "kuba34";
    private long _kubaHostAckMs, _kubaHelloSentMs, _kubaHostOkToldMs;
    private int _kubaHostOkTold = -1;

    /// <summary>The joiner, from the 30 Hz loop: the hello to the host, and the DLL told whether the host answers.</summary>
    private async Task KubaHelloTickAsync()
    {
        long now = Environment.TickCount64;
        long ack = Interlocked.Read(ref _kubaHostAckMs);
        bool ok = ack > 0 && now - ack < 75_000;
        if (now - _kubaHelloSentMs >= (ok ? 20_000 : 3_000))
        {
            _kubaHelloSentMs = now;
            await Wo203SendSideAsync(Protocol.JoinTargetHost, KubaW203Hello, KubaForkStamp);
        }
        int want = ok ? 1 : 0;
        if (want == _kubaHostOkTold && now - _kubaHostOkToldMs < 10_000) return;
        try
        {
            var r = await _combat.Wo163Async(KubaOpHostOk, [(byte)want]);
            if (r is not { Ok: true }) return;
            if (want != _kubaHostOkTold)
                Console.WriteLine(want == 1
                    ? "KUBA-34 the host answered the hello: it runs this fork -- the local duel (a copy's own brain) is allowed"
                    : "KUBA-34 the host does not answer the hello (the original mod, or not yet) -- no local duel, the stream drives every copy");
            _kubaHostOkTold = want; _kubaHostOkToldMs = now;
        }
        catch { }
    }

    // KUBA 37 (joiner, 20.11: "the NPC draws its weapon and a moment later puts it away"; "I had none [at first], but it did not draw when
    // I drew mine"): the duel brain fights the vanilla way -- fists against bare hands -- and it never learns of an armed assault (the
    // player's blows are judged on the host). The mod tells whether this player holds a weapon ("kuba_armed"); the DLL (op 157, on a
    // change and every 10 s) gives the duel copies the game's combat_forceArmed while he does.
    private const byte KubaOpPlayerArmed = 247;
    private volatile bool _kubaPlayerArmed;
    private int _kubaArmedTold = -1;
    private long _kubaArmedToldMs;

    private void KubaOnArmed(string arg)
    {
        bool armed = arg.Trim() == "1";
        bool was = _kubaPlayerArmed;
        _kubaPlayerArmed = armed;
        Console.WriteLine($"KUBA-37 this player {(armed ? "holds a weapon" : "holds no weapon")} (the mod's hand poll); local duels now {_kubaDuel.Count}");
        if (!W137Joiner || !armed || was) return;
        foreach (var n in _kubaDuel.Keys) { string nm = n; _ = Task.Run(() => KubaDuelWeaponAsync(nm, true)); }   // the draw at once, too
    }

    private async Task KubaArmedTickAsync()
    {
        long now = Environment.TickCount64;
        int want = _kubaPlayerArmed ? 1 : 0;
        if (want == _kubaArmedTold && now - _kubaArmedToldMs < 10_000) return;
        try
        {
            var r = await _combat.Wo163Async(KubaOpPlayerArmed, [(byte)want]);
            if (r is not { Ok: true }) return;
            _kubaArmedTold = want; _kubaArmedToldMs = now;
        }
        catch { }
    }

    /// <summary>W203 kinds 15/16: the host answers a hello; the joiner notes the answer.</summary>
    private void KubaOnHello(byte src, LootMsg m)
    {
        if (m.Kind == KubaW203Hello && W137Host) _ = Wo203SendSideAsync(src, KubaW203HelloAck, KubaForkStamp);
        else if (m.Kind == KubaW203HelloAck && W137Joiner) Interlocked.Exchange(ref _kubaHostAckMs, Environment.TickCount64);
    }

    private string KubaDuelStats() =>
        $"kuba_duel_local={_kubaDuel.Count}frames_out={Interlocked.Read(ref _kubaDuelFramesOut)} verdicts_out={Interlocked.Read(ref _kubaDuelVerdictsOut)}"
        + $" host_duels={_kubaHostDuels.Count} frames_in={Interlocked.Read(ref _kubaDuelFramesIn)} host_taken={Interlocked.Read(ref _kubaHostTaken)}"
        + $" releases_in={Interlocked.Read(ref _kubaReleasesIn)} host_verdicts_dropped={Interlocked.Read(ref _kubaDuelVerdictsDropped)} " + KubaWornStats();
}
