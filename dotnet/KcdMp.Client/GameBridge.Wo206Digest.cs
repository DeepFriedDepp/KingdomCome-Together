// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-206 C: checkpoint digests of the shared world, the same moment on both machines, for tools/digest/analyze.py.
//
// The host takes a checkpoint every Wo206Digest.CheckpointEveryS at a quiet point (in its world, no load, no join, no time skip)
// and tells the joiner (W203 kind 10, "v1|<epoch>|<seq>"); the joiner takes its own at the marker if it is quiet too. Each writes one
// digest.jsonl row per category beside the agent (the bundle carries it):
//   quest  every State, Timer and TimeOfDayWatch node of the installed quest files (the whole keyspace: the Jaunt's 13:00 watch was
//          outside the State mirror), read by the DLL's sweep (native wo206.cpp) and handed back only when a value changed;
//   dead   every soul within 60 m of the host's Henry, dead or alive (a Lua scan; the joiner scans around the host's figure);
//   loot   the chest ledger (the host's for this joiner, the joiner's own), container|class -> net count;
//   clock  the world clock, minutes rounded to 5;
//   join   the join state.
// A line `MP-DIGEST category=.. epoch=.. seq=.. n=.. buckets=..` goes to the agent's log too. Switch: mp_digest (on).
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, string> _w206dQuest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _w206dLastRow = new(StringComparer.Ordinal);   // cat -> the last row's bucket text
    private List<string>? _w206dKeys;          // the registered keyspace, by index
    private bool _w206dRegistered, _w206dArmed, _w206dRegistering;
    private uint _w206dPasses, _w206dPassesAtArm;
    private volatile bool _w206dSynced;   // a full sweep since arming has been drained: the quest map is whole
    private int _w206dEpoch, _w206dSeq;
    private int _w206dPendingDeadSeq;
    private long _w206dNextCheckpointMs, _w206dRows, _w206dMarkers, _w206dMarkersSkipped, _w206dReadFails;
    private StreamWriter? _w206dOut;
    private readonly object _w206dLock = new();
    private bool W206DigestOn => !_w206dOff;
    private volatile bool _w206dOff;

    private string W206Machine => W137Host ? "host" : W137Joiner ? "joiner" : "solo";

    /// <summary>In the world, no load, no join, no time skip on this machine.</summary>
    private bool Wo206Quiet() =>
        _where == GameWhere.World && !_localSkipActive && !Wo123HostJoinActive && (_joinReceivedId == 0 || _joinedWorld);

    private void Wo206OnLoadStarted()
    {
        _w206dArmed = false;
        _w206dSynced = false;   // the bench (long1): a host kept writing checkpoints from an emptied quest map after its reload
        _ = Wo206ArmAsync(false);
    }

    private void Wo206OnGameplayStarted()
    {
        // The DLL keeps the keyspace and its last values across a load (nodes are found by path at every read), so the quest map stays
        // and the sweep's two passes after re-arming bring every value the load changed. Registering again cost the host minutes of
        // pipe round trips (long1: 7 min) -- only a DLL that came back without paths is registered again (Wo206ArmAsync).
        _w206dSynced = false;
        lock (_w206dLock) _w206dLastRow.Clear();
        _w206dNextCheckpointMs = Environment.TickCount64 + 15_000;   // a settle after the load before the first checkpoint
    }

    // D: the animation probe (native wo206.cpp), armed every 5 s with this machine's role (a DLL that came back has it again)
    private volatile bool _w206dProbeOff;
    private long _w206dProbeSentMs;

    private async Task Wo206ProbeAsync()
    {
        byte machine = (byte)(W137Host ? 1 : W137Joiner ? 2 : 0);
        bool on = !_w206dProbeOff && _where == GameWhere.World;
        try { await _combat.Wo163Async(Protocol.Wo206OpProbe, [(byte)(on ? 1 : 0), machine, 10, 40, 12]); }
        catch (Exception ex) { Console.WriteLine($"MP-W206 probe switch not sent to the DLL: {ex.Message}"); }
    }

    private async Task Wo206DigestNativeStatusAsync()
    {
        var r = await _combat.Wo163Async(Protocol.Wo206OpStatus, []);
        if (r is { Ok: true } rr) Console.WriteLine($"MP-W206-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
    }

    // The stall watch (found on the bench, A.5: a joiner's frame processor stood still for minutes while its reader kept reading):
    // the frame the processor is on and since when; one line when it has held the processor over 10 s, one when it lets go.
    private int _w206dProcType;
    private long _w206dProcSinceMs, _w206dStallsLogged, _w206dStallLoggedFor;

    private void Wo206NoteProcessorOn(int type)
    {
        long now = Environment.TickCount64;
        long since = Interlocked.Exchange(ref _w206dProcSinceMs, now);
        if (_w206dStallLoggedFor != 0 && _w206dStallLoggedFor == since)
            Console.WriteLine($"MP-W206 STALL over: the processor let go of frame type {_w206dProcType} after {(now - since) / 1000.0:F1} s");
        Volatile.Write(ref _w206dProcType, type);
    }

    private void Wo206StallWatch()
    {
        long since = Volatile.Read(ref _w206dProcSinceMs);
        if (since == 0 || since == _w206dStallLoggedFor) return;
        long held = Environment.TickCount64 - since;
        if (held < 10_000) return;
        _w206dStallLoggedFor = since;
        Interlocked.Increment(ref _w206dStallsLogged);
        int t = Volatile.Read(ref _w206dProcType);
        Console.WriteLine($"MP-W206 STALL the frame processor has been on one frame for {held / 1000.0:F1} s: type {t} (0x{t & 0xFF:X2}){(t == FlushTickType ? " -- the NPC Lua flush" : "")} -- the reader keeps reading behind it");
    }

    /// <summary>The 4 Hz session tick.</summary>
    private void Wo206DigestTick(long n)
    {
        Wo206StallWatch();
        long nowMs = Environment.TickCount64;
        // an agent started after the load (the launcher's order: 10 s after "Gameplay started") never read the load's line: the mod is
        // asked where the game is (WO-160's own question), or the digest and the probe would wait for a world forever (the bench, A.5)
        if (_where == GameWhere.Unknown && n % 8 == 3) _ = W160AskWhereIfDueAsync();
        if (_combat.IsConnected && nowMs - _w206dProbeSentMs >= 5000) { _w206dProbeSentMs = nowMs; _ = Wo206ProbeAsync(); }
        if (!W206DigestOn) return;
        if (!_w206dRegistered && !_w206dRegistering && _where == GameWhere.World && _combat.IsConnected) { _ = Wo206RegisterAsync(); return; }
        bool quiet = Wo206Quiet();
        if (quiet != _w206dArmed && _w206dRegistered) { _w206dArmed = quiet; _w206dSynced = false; _ = Wo206ArmAsync(quiet); }
        if (_w206dArmed && n % 2 == 0) _ = Wo206DrainAsync();
        long now = Environment.TickCount64;
        if (W137Host && quiet && _w206dSynced && now >= _w206dNextCheckpointMs)
        {
            _w206dNextCheckpointMs = now + Wo206Digest.CheckpointEveryS * 1000L;
            int seq = Interlocked.Increment(ref _w206dSeq);
            Interlocked.Increment(ref _w206dMarkers);
            int epoch = (int)Wo206Applied.WorldEpoch;   // Part E's world epoch (bumped at every host load)
            foreach (byte peer in LivePartners()) _ = Wo203SendSideAsync(peer, Wo206Digest.MarkerKind, Wo206Digest.MarkerText(epoch, seq));
            _ = Wo206CheckpointAsync(epoch, seq);
        }
    }

    /// <summary>The joiner: the host's checkpoint marker.</summary>
    private void Wo206OnMarker(LootMsg m)
    {
        if (!W206DigestOn || !Wo206Digest.TryParseMarker(m.Text, out int epoch, out int seq)) return;
        _w206dEpoch = epoch;
        if (!Wo206Quiet() || !_w206dSynced) { Interlocked.Increment(ref _w206dMarkersSkipped); return; }
        _w206dSeq = seq;
        _ = Wo206CheckpointAsync(epoch, seq);
    }

    private async Task Wo206RegisterAsync()
    {
        _w206dRegistering = true;
        try
        {
            var idx = await Wo147QuestIndexAsync();
            if (idx is null) { Console.WriteLine("MP-DIGEST quest keyspace: Scripts.pak not read -- the quest category stays empty"); _w206dRegistered = true; return; }
            var keys = idx.StatePaths.Concat(idx.ClockPaths).Distinct(StringComparer.Ordinal).Where(k => Encoding.ASCII.GetByteCount(k) <= 400).ToList();
            bool reset = true;
            int i = 0;
            while (i < keys.Count)
            {
                var batch = new List<byte[]>();
                int size = 3;
                while (i < keys.Count && batch.Count < 255)
                {
                    var pb = Encoding.ASCII.GetBytes(keys[i]);
                    if (size + 2 + pb.Length > 1000) break;
                    batch.Add(pb); size += 2 + pb.Length; i++;
                }
                var a = new byte[2 + batch.Sum(x => 2 + x.Length)];
                a[0] = (byte)(reset ? 1 : 0); a[1] = (byte)batch.Count;
                int o = 2;
                foreach (var pb in batch) { BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(o), (ushort)pb.Length); pb.CopyTo(a, o + 2); o += 2 + pb.Length; }
                var r = await _combat.Wo163Async(Protocol.Wo206OpDigestPaths, a);
                if (r is not { Ok: true }) { Console.WriteLine($"MP-DIGEST keyspace refused by the DLL at {i}/{keys.Count} -- the quest category stays empty"); return; }
                reset = false;
            }
            _w206dKeys = keys;
            _w206dRegistered = true;
            Console.WriteLine($"MP-DIGEST keyspace registered: {idx.StatePaths.Count} States + {idx.ClockPaths.Count} clocks = {keys.Count} nodes");
        }
        catch (Exception ex) { Console.WriteLine($"MP-DIGEST keyspace not registered: {ex.Message}"); }
        finally { _w206dRegistering = false; }
    }

    private async Task Wo206ArmAsync(bool on)
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo206OpDigestArm, [(byte)(on ? 1 : 0)]);
            if (r is { Ok: true, Payload.Length: >= 8 } rr)
            {
                _w206dPassesAtArm = _w206dPasses = BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload);
                if (on && _w206dRegistered && Wo206Digest.KeyspaceLost(BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload.AsSpan(4))))
                {
                    // a DLL that came back (injected again) holds no paths: the keyspace goes again and the map starts empty
                    Console.WriteLine("MP-DIGEST the DLL holds no keyspace (it came back) -- registering again");
                    _w206dQuest.Clear();
                    _w206dArmed = false;
                    _w206dRegistered = false;
                }
            }
        }
        catch { }
    }

    private int _w206dDraining;

    /// <summary>The DLL's changed entries since the last read, into the quest map.</summary>
    private async Task Wo206DrainAsync()
    {
        if (Interlocked.Exchange(ref _w206dDraining, 1) == 1) return;
        try
        {
            var keys = _w206dKeys;
            if (keys is null) return;
            for (int guard = 0; guard < 400; guard++)
            {
                var r = await _combat.Wo163Async(Protocol.Wo206OpDigestRead, [50]);
                if (r is not { Ok: true, Payload.Length: >= 13 } rr) { Interlocked.Increment(ref _w206dReadFails); return; }
                var b = rr.Payload;
                _w206dPasses = BinaryPrimitives.ReadUInt32LittleEndian(b);
                uint left = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8));
                int n = b[12];
                for (int k = 0; k < n && 13 + k * 10 + 10 <= b.Length; k++)
                {
                    int q = 13 + k * 10;
                    int i = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(q));
                    if (i < 0 || i >= keys.Count) continue;
                    bool found = b[q + 4] != 0;
                    int v = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(q + 6));
                    if (found && Wo206Digest.QuestEntry(b[q + 5], v) is string val) _w206dQuest[keys[i]] = val;
                    else _w206dQuest.TryRemove(keys[i], out _);
                }
                // whole once a pass that began after the arm has completed and nothing is left to read
                if (left == 0 && _w206dPasses >= _w206dPassesAtArm + 2) _w206dSynced = true;
                if (left == 0 || n == 0) return;
            }
        }
        finally { Volatile.Write(ref _w206dDraining, 0); }
    }

    private async Task Wo206CheckpointAsync(int epoch, int seq)
    {
        Wo206Applied.CheckpointSeq = seq;   // every APPLIED line from here on names this checkpoint
        _w206dEpoch = epoch;
        for (int i = 0; i < 100 && Volatile.Read(ref _w206dDraining) == 1; i++) await Task.Delay(30);   // a drain in flight finishes first
        await Wo206DrainAsync();
        long t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Wo206WriteRow(t, Wo206Digest.Quest, epoch, seq, new Dictionary<string, string>(_w206dQuest, StringComparer.Ordinal));
        Wo206WriteRow(t, Wo206Digest.Loot, epoch, seq, Wo206LootMap());
        var clock = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_lastPolledWorldTime is uint wt) clock["clock"] = Wo206Digest.ClockValue(wt);
        Wo206WriteRow(t, Wo206Digest.Clock, epoch, seq, clock);
        Wo206WriteRow(t, Wo206Digest.Join, epoch, seq, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // the same words on both machines: settled = no join running (the host) / in the host's world (the joiner)
            ["state"] = W137Host ? (Wo123HostJoinActive ? "joining" : "settled") : W137Joiner ? (_joinedWorld ? "settled" : "joining") : "solo",
            ["join_epoch"] = Wo206Applied.JoinEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        // the dead set: a Lua scan around the host's Henry; its row is written when the line comes back (Wo206DigestOnEvent)
        var (x, y, z) = Wo206DeadAnchor();
        _w206dPendingDeadSeq = seq;
        await ExecLuaAsync(Wo206Digest.DeadScanLua(x, y, z, seq));
    }

    private (float X, float Y, float Z) Wo206DeadAnchor()
    {
        if (W137Host || !W137Joiner) return (_lastX, _lastY, _lastZ);
        // the joiner: the host's figure (the partner whose position came in most recently)
        var best = _ghostLastPos.Values.OrderByDescending(v => v.AtUtc).FirstOrDefault();
        return best.AtUtc == default ? (_lastX, _lastY, _lastZ) : (best.X, best.Y, best.Z);
    }

    private Dictionary<string, string> Wo206LootMap()
    {
        var m = new Dictionary<string, string>(StringComparer.Ordinal);
        var l = W137Host ? _w134HostLedger : _w134JoinerLedger;
        if (l is null) return m;
        foreach (var e in l.Entries.ToList())
        {
            string k = e.C + "|" + e.Cls;
            m[k] = ((m.TryGetValue(k, out var s) ? int.Parse(s, System.Globalization.CultureInfo.InvariantCulture) : 0) + e.N)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return m;
    }

    private void Wo206DigestOnEvent(string name, string? arg)
    {
        // The bench's stand-in for the hour picker (KCDMP_TEST_W206=1 only, never a player's agent): "skip <sleep|wait> <hours>" starts
        // this game's own skip the way an accepter's starts (the DLL's skip op); after this player's approved vote the DLL's "began"
        // sends the Begin to the others as a real picker's confirm does.
        if (name == "w206_test")
        {
            var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (Environment.GetEnvironmentVariable("KCDMP_TEST_W206") != "1") { Console.WriteLine("MP-W206 w206_test refused: not a bench agent"); return; }
            if (f.Length == 3 && f[0] == "skip" && f[1] is "sleep" or "wait"
                && float.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h))
                _ = Wo140StartOwnAsync(f[1], h, "the bench (the picker's stand-in)");
            return;
        }
        if (name == "w206_digest") { _w206dOff = arg?.Trim() == "off"; Console.WriteLine($"MP-DIGEST mp_digest {(_w206dOff ? "off" : "on")}"); return; }
        if (name == "w206_probe") { _w206dProbeOff = arg?.Trim() == "off"; _w206dProbeSentMs = 0; Console.WriteLine($"MP-W206 mp_anim_probe {(_w206dProbeOff ? "off" : "on")}"); return; }
        if (name != "w206_dead" || arg is null || !Wo206Digest.TryParseDeadScan(arg, out int seq, out var map)) return;
        if (seq != _w206dPendingDeadSeq) return;   // an older scan's line
        Wo206WriteRow(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Wo206Digest.Dead, _w206dEpoch, seq, map);
    }

    private void Wo206WriteRow(long t, string cat, int epoch, int seq, IReadOnlyDictionary<string, string> map)
    {
        lock (_w206dLock)
        {
            try
            {
                _w206dOut ??= new StreamWriter(new FileStream(Path.Combine(AppContext.BaseDirectory, "digest.jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read))
                    { AutoFlush = true };
                string bucketText = string.Join(",", Wo206Digest.Buckets(map).Select(kv => kv.Key + ":" + kv.Value));
                // the full map when this category changed since its last row, and on its first row of an epoch
                string mark = epoch.ToString(System.Globalization.CultureInfo.InvariantCulture) + "#" + bucketText;
                bool full = !_w206dLastRow.TryGetValue(cat, out var last) || last != mark;
                _w206dLastRow[cat] = mark;
                _w206dOut.WriteLine(Wo206Digest.Row(t, W206Machine, cat, epoch, seq, map, full));
                _w206dRows++;
                Console.WriteLine($"MP-DIGEST category={cat} epoch={epoch} seq={seq} n={map.Count} buckets={(bucketText.Length > 160 ? bucketText[..160] + "..." : bucketText)}");
            }
            catch (IOException ex) { Console.WriteLine($"MP-DIGEST row not written: {ex.Message}"); }
        }
    }

    private string Wo206DigestStats() =>
        $"MP-W206-DIGEST on={(W206DigestOn ? 1 : 0)} keys={_w206dKeys?.Count ?? 0} armed={(_w206dArmed ? 1 : 0)} passes={_w206dPasses} quest_values={_w206dQuest.Count} epoch={_w206dEpoch} seq={_w206dSeq} " +
        $"rows={_w206dRows} markers={_w206dMarkers} markers_skipped={_w206dMarkersSkipped} read_fails={_w206dReadFails} stalls={_w206dStallsLogged} probe={(_w206dProbeOff ? "off" : "on")}";
}
