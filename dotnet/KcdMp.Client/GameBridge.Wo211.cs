// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-211 (docs/WO-211-findings.md): the bridge's half of the 0.63.9 beta patch -- the switches (Lua w211_cfg, mp_w211_status), the DLL's
// switch word (op 143), A's link hold (a dropped link holds the joiner in the host's world while the agent reconnects; the host keeps the
// partner's figure), C.3's over-full queue guard, D's notice for a stop over the partner's crime. The pure rules are Wo211Rules (Wo211.cs).
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w211On = new(StringComparer.Ordinal);
    private volatile uint _w211DllEff = uint.MaxValue;

    /// <summary>A WO-211 switch: its default unless the mod said otherwise (a twin's KCDMP_TEST_W211_OFF=name turns it off from the start).</summary>
    private bool W211(string name) =>
        _w211On.TryGetValue(name, out bool v) ? v : Wo211Rules.Default(name, Environment.GetEnvironmentVariable("KCDMP_TEST_W211_OFF"));

    /// <summary>A WO-211 fix with no switch of its own: on unless the bench's twin names it.</summary>
    private static bool W211Fix(string name) => !Wo207Rules.TwinOff(Environment.GetEnvironmentVariable("KCDMP_TEST_W211_OFF"), name);

    private async Task Wo211PushConfigAsync()
    {
        try
        {
            uint bits = Wo211Rules.ConfigBits(n => n == "handover_queue_cap" ? W211Fix(n) : W211(n));
            var r = await _combat.Wo163Async(Protocol.Wo211OpConfig, BitConverter.GetBytes(bits));
            if (r is { Ok: true, Payload.Length: >= 4 } x) _w211DllEff = BitConverter.ToUInt32(x.Payload, 0);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W211 config not sent to the DLL: {ex.Message}"); }
    }

    private async Task Wo211NativeStatusAsync()
    {
        var r = await _combat.Wo163Async(Protocol.Wo211OpStatus, []);
        if (r is { Ok: true } rr) Console.WriteLine($"MP-W211-NATIVE {System.Text.Encoding.ASCII.GetString(rr.Payload)}");
    }

    /// <summary>The 4 Hz session tick (Wo201Tick).</summary>
    private void Wo211Tick(long n)
    {
        if (n % 8 == 7) _ = Wo211PushConfigAsync();
        if (n % 120 == 11 && (W137Host || W137Joiner)) _ = Wo211NativeStatusAsync();
        if (n % 4 == 2) Wo211HostFigureTick();
    }

    private string Wo211Stats() =>
        $"holds={Interlocked.Read(ref _w211Holds)} resumed_in_place={Interlocked.Read(ref _w211ResumedInPlace)} resumed_rejoin={Interlocked.Read(ref _w211ResumedRejoin)}"
        + $" hold_leaves={Interlocked.Read(ref _w211HoldLeaves)} leave_asked={Interlocked.Read(ref _w211LeaveAsked)} figures_held={Interlocked.Read(ref _w211FiguresHeld)}"
        + $" figures_back={Interlocked.Read(ref _w211FiguresBack)} figures_removed={Interlocked.Read(ref _w211FiguresRemoved)}"
        + $" snapshots_after_drop={Interlocked.Read(ref _w211SnapAfterDrop)} overflow_lines={Interlocked.Read(ref _w211OverflowLines)} overflow_asks={Interlocked.Read(ref _w211OverflowAsks)}"
        + $" overflow_reseated={Interlocked.Read(ref _w211OverflowReseated)} seat_releases_kept={Interlocked.Read(ref _w211SeatKept)} reseats_held={Interlocked.Read(ref _w211ReseatsHeld)}"
        + $" seat_loops={Interlocked.Read(ref _w211SeatLoops)} joint_stops_told={Interlocked.Read(ref _w211JointTold)} fly_refused={Interlocked.Read(ref _w211FlyRefused)}";

    private void Wo211OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w211_cfg":
                if (!Wo211Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W211 cfg refused: '{arg}'"); return; }
                _w211On[cfg] = on;
                Console.WriteLine($"MP-W211 cfg {cfg}={(on ? "on" : "off")}");
                _ = Wo211PushConfigAsync();
                return;
            case "w211_leave":       // A.1: the player chose "Leave the host's world" in the co-op menu during a link hold
                Interlocked.Increment(ref _w211LeaveAsked);
                Console.WriteLine($"WO211-LINK the player asked to leave{(_w211Holding ? " during the hold" : " (no hold runs: nothing to do)")}");
                if (_w211Holding) _w211LeaveNow = true;
                return;
            case "w211_fly":         // E: the mod refused the debug flight (or a tester's mp_allow_fly let it)
                if (arg?.StartsWith("refused", StringComparison.Ordinal) == true) Interlocked.Increment(ref _w211FlyRefused);
                Console.WriteLine($"WO211-FLY {arg}");
                return;
            case "w211_status":
                _ = Wo211NativeStatusAsync();
                Console.WriteLine($"MP-W211-STATS {string.Join(' ', Wo211Rules.SwitchNames.Select(n => $"{n}={(W211(n) ? "on" : "off")}"))}"
                    + $" fixes={string.Join(',', Wo211Rules.FixNames.Select(n => $"{n}:{(W211Fix(n) ? "on" : "TWIN-off")}"))} build={ReleaseVersionInfo.BuildIdValue} | {Wo211Stats()}");
                return;
        }
    }

    // ---------------------------------------------------------------- A.1: the joiner holds in the host's world while the link comes back

    private volatile bool _w211Holding, _w211LeaveNow;
    private DateTime _w211HoldSinceUtc;
    private uint _w211HoldEpoch;
    private int _w211HoldAttempt;
    private bool _w211EngineHeld;
    private long _w211Holds, _w211ResumedInPlace, _w211ResumedRejoin, _w211HoldLeaves, _w211LeaveAsked, _w211SnapAfterDrop;

    /// <summary>The reconnect loop's gap (GameBridge.RunLoopAsync): growing while a hold runs, the agent's own 3 s otherwise.</summary>
    private int Wo211ReconnectDelayMs() => Wo211Rules.ReconnectDelayMs(_w211Holding ? _w211HoldAttempt++ : 0, _w211Holding);

    /// <summary>The joiner, at the session's teardown (Wo124OnDisconnectAsync): a lost link starts a hold instead of the leave. True = held.</summary>
    private async Task<bool> Wo211TryHoldAsync(string why)
    {
        if (!Wo211Rules.HoldOnLoss(W211("link_hold"), _joinedWorld, _jj is not null, _leaveInProgress, _gameQuitting)) return false;
        if (_w211Holding) return true;
        _w211Holding = true;
        _w211LeaveNow = false;
        _w211HoldSinceUtc = DateTime.UtcNow;
        _w211HoldEpoch = _w206.World;
        _w211HoldAttempt = 0;
        Interlocked.Increment(ref _w211Holds);
        Console.WriteLine($"WO211-LINK hold: the link to the host dropped ({why}) -- this game stays in the host's world, still, for up to {Wo211Rules.HoldWindowS} s "
            + $"while the agent reconnects (the host's world epoch {_w211HoldEpoch}); the leave comes only after that or on the player's word");
        bool? held = null;
        try { held = await _combat.Wo151JoinHoldAsync(true, (ushort)(Wo211Rules.HoldWindowS + 20)); } catch { }
        _w211EngineHeld = held == true;
        try { await ExecLuaAsync($"if KCD2MP_W211LinkHold then KCD2MP_W211LinkHold(true, {Wo211Rules.HoldWindowS}, \"{EscapeLua(Wo211Rules.HoldText(Wo211Rules.HoldWindowS))}\") end"); } catch { }
        _ = Wo211HoldLoopAsync();
        return true;
    }

    /// <summary>The hold's own loop: the countdown, the player's Leave, the window's end.</summary>
    private async Task Wo211HoldLoopAsync()
    {
        int lastShown = -1;
        while (_w211Holding)
        {
            await Task.Delay(1000);
            if (!_w211Holding) return;
            int left = Wo211Rules.HoldWindowS - (int)(DateTime.UtcNow - _w211HoldSinceUtc).TotalSeconds;
            if (_w211LeaveNow || left <= 0)
            {
                string reason = _w211LeaveNow ? "the player chose to leave" : $"the link did not come back in {Wo211Rules.HoldWindowS} s";
                await Wo211EndHoldAsync(false, reason);
                Interlocked.Increment(ref _w211HoldLeaves);
                await LeaveSharedWorldAsync("relay-lost", _w211LeaveNow ? "You left your partner's world." : "Lost the connection to your host.");
                return;
            }
            if (left != lastShown && (left % 5 == 0 || left <= 10))
            {
                lastShown = left;
                try { await ExecLuaAsync($"if KCD2MP_W211LinkHold then KCD2MP_W211LinkHold(true, {left}, \"{EscapeLua(Wo211Rules.HoldText(left))}\") end"); } catch { }
            }
        }
    }

    private async Task Wo211EndHoldAsync(bool resumed, string why)
    {
        if (!_w211Holding) return;
        _w211Holding = false;
        double s = (DateTime.UtcNow - _w211HoldSinceUtc).TotalSeconds;
        if (_w211EngineHeld) { try { await _combat.Wo151JoinHoldAsync(false, 0); } catch { } _w211EngineHeld = false; }
        try { await ExecLuaAsync("if KCD2MP_W211LinkHold then KCD2MP_W211LinkHold(false, 0, \"\") end"); } catch { }
        Console.WriteLine(FormattableString.Invariant($"WO211-LINK hold ends after {s:F1} s: {why}"));
        if (resumed) _ = ExecLuaAsync("KCD2MP_ShowNativeToast(\"Reconnected to your host.\")");
    }

    /// <summary>A.1 step 4 (the reader, a joiner: the host's epochs heard after the link came back). The same world: the hold ends where
    /// it stands -- no load, no jump; the host's bodies are asked afresh. The host reloaded meanwhile: the in-world rejoin of a host reload.</summary>
    private void Wo211OnEpochHeard(uint world)
    {
        if (!_w211Holding) return;
        if (Wo211Rules.ResumeInPlace(_w211HoldEpoch, world))
        {
            Interlocked.Increment(ref _w211ResumedInPlace);
            _ = Task.Run(async () =>
            {
                await Wo211EndHoldAsync(true, $"the link is back and the host's world is the same (epoch {world}) -- no load, no jump");
                await Wo210AfterSkipAsync("reconnect");   // every body's whole-body state afresh
            });
            return;
        }
        Interlocked.Increment(ref _w211ResumedRejoin);
        _ = Task.Run(async () =>
        {
            await Wo211EndHoldAsync(false, $"the link is back but the host's world changed meanwhile (epoch {_w211HoldEpoch} -> {world}): the in-world rejoin of a host reload");
            Wo125OnHostReloading();
            if (_peerSeedKnown && _peerTag is not null && _peerTag == _joinedTag && !_rejoinPending)
            {
                _rejoinPending = true;
                _rejoinSinceUtc = DateTime.UtcNow;
                Console.WriteLine($"MP-HENRY joiner: the host reloaded world {_peerTag} while the link was down -- rejoining from inside the world");
            }
        });
    }

    // ---------------------------------------------------------------- A.2.3: home with the drop moment's Henry

    private (float X, float Y, float Z, float Rot) _w211LastOwnPos;   // the position loop writes, the leave reads (a torn read is one tick old)
    private (float X, float Y, float Z, float Rot)? _w211HomePlace;
    private long _w211HomeSplices, _w211HomeSpliceFails;

    /// <summary>
    /// A.2.3 (the joiner, a leave after a dropped link, the final snapshot paired): the snapshot's Henry -- his stats, skills, inventory,
    /// money, health as they were at the drop -- spliced into his own newest save the way a join splices it into the host's (the same
    /// check and verify), his own world's quest items kept and the host world's left there (QuestItemMode.Host: the target's), placed as a
    /// new file beside it and loaded instead; after the load he is put where he stood. Null (the plain own save, said why) on any failure.
    /// </summary>
    private async Task<HenrySource?> Wo211SpliceHomeAsync(string tag, HenrySource own)
    {
        try
        {
            var snap = _henry.Snapshots(tag).FirstOrDefault();
            var parts = snap is null ? null : _henry.Load(snap);
            string? tables = TablesPakPath();
            if (parts is null || tables is null) { Console.WriteLine($"WO211-HOME no splice: {(parts is null ? "no snapshot of this world" : "no Tables.pak")} -- the own save as it is"); Interlocked.Increment(ref _w211HomeSpliceFails); return null; }
            _questClasses ??= WhsSave.QuestClasses(tables);
            byte[] ownBytes = WhsSave.ReadShared(own.FullPath);
            var res = WhsSave.SpliceParts(ownBytes, parts, _questClasses, WhsSave.QuestItemMode.Host);
            var fails = WhsSave.CheckParts(ownBytes, parts, res.File, _questClasses, WhsSave.QuestItemMode.Host);
            var ver = WhsSave.Verify(res.File);
            if (fails.Count > 0 || !ver.Ok)
            {
                foreach (var f in fails.Take(6)) Console.WriteLine($"WO211-HOME   check FAIL {f}");
                Console.WriteLine($"WO211-HOME splice refused ({fails.Count} check failure(s), verify {(ver.Ok ? "ok" : ver.Reason)}) -- the own save as it is");
                Interlocked.Increment(ref _w211HomeSpliceFails);
                return null;
            }
            string name = $"mpdrop{Random.Shared.Next(1, int.MaxValue):x8}";
            string path = Path.Combine(Path.GetDirectoryName(own.FullPath)!, name + ".whs");
            using (var fs = new FileStream(path + ".part", FileMode.CreateNew, FileAccess.Write)) fs.Write(res.File);
            File.Move(path + ".part", path);
            var listed = await _combat.SaveListAsync(1, own.Playline, name);
            if (listed is null || !listed.Listed)
            {
                _henry.MoveOut(path, "a home splice the game did not list");
                Console.WriteLine($"WO211-HOME the spliced save was not listed after the rescan -- moved out; the own save as it is");
                Interlocked.Increment(ref _w211HomeSpliceFails);
                return null;
            }
            var p = _w211LastOwnPos;
            _w211HomePlace = (p.X != 0 || p.Y != 0) ? p : null;
            Interlocked.Increment(ref _w211HomeSplices);
            var rp = res.Report;
            Console.WriteLine(FormattableString.Invariant(
                $"WO211-HOME own save {own.Display} + the drop moment's Henry (snapshot {snap!.Short}) -> {SaveDisplay(path)} ({res.File.Length} B, check PASS, verify ok; quest items: the host world's {rp.QuestItemsRemoved.Count} left there, his own {rp.QuestItemsAdded.Count} kept); placed at ({p.X:F1}, {p.Y:F1}, {p.Z:F1}) after the load"));
            return new HenrySource(own.Playline, name + ".whs", path, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Console.WriteLine($"WO211-HOME splice failed ({ex.GetType().Name}: {ex.Message}) -- the own save as it is");
            Interlocked.Increment(ref _w211HomeSpliceFails);
            return null;
        }
    }

    /// <summary>A.2.3: the leave's own load is in -- the player stands where he stood at the drop (the safe placement holds him on the
    /// ground while it settles).</summary>
    private void Wo211AfterHomeLoad()
    {
        if (_w211HomePlace is not { } p) return;
        _w211HomePlace = null;
        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            try { await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W211PlaceAt then KCD2MP_W211PlaceAt({p.X:F2}, {p.Y:F2}, {p.Z:F2}, {p.Rot:F3}) end")); } catch { }
            Console.WriteLine(FormattableString.Invariant($"WO211-HOME placed where he stood at the drop ({p.X:F1}, {p.Y:F1}, {p.Z:F1})"));
        });
    }

    // ---------------------------------------------------------------- A.1.5 / A.3: the host keeps the partner's figure

    private readonly ConcurrentDictionary<byte, DateTime> _w211FigureHeld = new();
    private long _w211FiguresHeld, _w211FiguresBack, _w211FiguresRemoved;

    /// <summary>The host, a peer's disconnect: a partner who was in this world keeps its figure where it stood, marked "reconnecting", for
    /// the hold's window (the world does not pause for it). True = held (the caller does not remove the figure now).</summary>
    private bool Wo211HoldFigure(byte ghostId, bool wasInWorld)
    {
        if (!(_isDamageAuthority && W211("link_hold") && wasInWorld)) return false;
        _w211FigureHeld[ghostId] = DateTime.UtcNow;
        Interlocked.Increment(ref _w211FiguresHeld);
        Console.WriteLine($"WO211-LINK host: ghost {ghostId}'s link dropped -- its figure stays where it stood, marked reconnecting, for {Wo211Rules.HoldWindowS} s");
        _ = ExecLuaAsync($"if KCD2MP_W211FigureReconnecting then KCD2MP_W211FigureReconnecting(\"{ghostId}\", true) end");
        return true;
    }

    /// <summary>The host: a peer connected (its name): a held figure of the same ghost id takes it back.</summary>
    private void Wo211OnPeerBack(byte ghostId)
    {
        if (!_w211FigureHeld.TryRemove(ghostId, out var since)) return;
        Interlocked.Increment(ref _w211FiguresBack);
        Console.WriteLine(FormattableString.Invariant($"WO211-LINK host: ghost {ghostId} is back after {(DateTime.UtcNow - since).TotalSeconds:F1} s -- its figure carries on"));
        _ = ExecLuaAsync($"if KCD2MP_W211FigureReconnecting then KCD2MP_W211FigureReconnecting(\"{ghostId}\", false) end");
    }

    /// <summary>The host, once a second: a held figure whose partner did not come back in the window goes.</summary>
    private void Wo211HostFigureTick()
    {
        foreach (var (g, since) in _w211FigureHeld.ToArray())
        {
            if ((DateTime.UtcNow - since).TotalSeconds < Wo211Rules.HoldWindowS) continue;
            if (!_w211FigureHeld.TryRemove(g, out _)) continue;
            Interlocked.Increment(ref _w211FiguresRemoved);
            Console.WriteLine($"WO211-LINK host: ghost {g} did not come back in {Wo211Rules.HoldWindowS} s -- its figure goes");
            _ = ExecLuaAsync($"if KCD2MP_W211FigureReconnecting then KCD2MP_W211FigureReconnecting(\"{g}\", false) end; KCD2MP_RemoveGhost(\"{g}\")");
        }
    }

    // ---------------------------------------------------------------- C.1 / C.3: seats and the over-full queue

    private readonly ConcurrentDictionary<string, (ulong Seat, ulong Work, long AtMs)> _w211ReleasedObj = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w211OverflowAskedMs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w211OverflowPerCopy = new(StringComparer.Ordinal);
    private long _w211OverflowLines, _w211OverflowAsks, _w211OverflowReseated, _w211SeatKept, _w211ReseatsHeld, _w211SeatLoops;

    /// <summary>C.1: a release of the copy's own posture was made (the hand-over or the watchdog): remembered for the re-seat guard.</summary>
    private void Wo211NoteRelease(string name, ActivityState prev)
    {
        var (s, w) = Wo211Rules.HeldObjects(prev);
        _w211ReleasedObj[name] = (s, w, Environment.TickCount64);
    }

    /// <summary>C.1: WO-160's placement of <paramref name="next"/> into the very object this copy's posture was released from a moment ago
    /// is held back (the next apply places it); a seat loop is counted when one gets through (twin seat_same_object: the old rule).</summary>
    private bool Wo211ReseatHeld(string name, ActivityState next)
    {
        if (!_w211ReleasedObj.TryGetValue(name, out var r)) return false;
        long ago = Environment.TickCount64 - r.AtMs;
        if (ago > Wo211Rules.SeatLoopMs) return false;
        var (s, w) = Wo211Rules.HeldObjects(next);
        bool same = (s != 0 && (s == r.Seat || s == r.Work)) || (w != 0 && (w == r.Seat || w == r.Work));
        if (!same) return false;
        if (W211Fix("seat_same_object") && ago <= Wo211Rules.ReseatAfterReleaseMs)
        {
            long k = Interlocked.Increment(ref _w211ReseatsHeld);
            if (k <= 40 || k % 50 == 0) Console.WriteLine($"WO211-SEAT npc={name}: a placement into the seat released {ago} ms ago held back (the next apply places it) [n={k}]");
            return true;
        }
        long l = Interlocked.Increment(ref _w211SeatLoops);
        if (l <= 40 || l % 50 == 0) Console.WriteLine($"WO211-SEAT LOOP npc={name}: re-seated {ago} ms after its release [n={l}]");
        return false;
    }

    /// <summary>C.3 (every engine line; never consumes): the engine's over-full queue line for a copy -- the owner reseats it the clean way.</summary>
    private void Wo211OnEngineLine(string line)
    {
        if (Wo211Rules.OverflowEntity(line) is not { } name) return;
        Interlocked.Increment(ref _w211OverflowLines);
        _w211OverflowPerCopy.AddOrUpdate(name, 1, (_, n) => n + 1);
        if (!W137Joiner || !W211("overflow_reseat") || name.StartsWith("kcd2mp_", StringComparison.Ordinal)) return;
        long now = Environment.TickCount64;
        if (_w211OverflowAskedMs.TryGetValue(name, out long at) && now - at < Wo211Rules.OverflowAskEveryMs) return;
        _w211OverflowAskedMs[name] = now;
        Interlocked.Increment(ref _w211OverflowAsks);
        _ = Task.Run(async () =>
        {
            var nb = System.Text.Encoding.ASCII.GetBytes(name);
            var body = new byte[1 + nb.Length];
            body[0] = (byte)nb.Length;
            nb.CopyTo(body, 1);
            var r = await _combat.Wo163Async(Protocol.Wo211OpOverflowReseat, body);
            byte res = r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] : (byte)255;
            if (res == 1) Interlocked.Increment(ref _w211OverflowReseated);
            long k = Interlocked.Read(ref _w211OverflowAsks);
            if (k <= 40 || k % 50 == 0)
                Console.WriteLine($"WO211-OVERFLOW npc={name}: the engine's action queue is over-full ({_w211OverflowPerCopy.GetValueOrDefault(name)} line(s)) -> "
                    + (res switch { 1 => "reseated the clean way", 2 => "not a copy the owner drives", 3 => "reseated a moment ago", 0 => "the switch is off in the DLL", _ => "no answer" }) + $" [n={k}]");
        });
    }

    // ---------------------------------------------------------------- D: a stop for the partner's crime

    private readonly ConcurrentDictionary<string, bool> _w211OwnCrimes = new(StringComparer.Ordinal);
    private long _w211JointTold, _w211FlyRefused;

    private void Wo211NoteOwnCrime(string kind) => _w211OwnCrimes[kind] = true;
    private void Wo211CrimesCleared() => _w211OwnCrimes.Clear();

    /// <summary>D (the joiner, a stop from the host): crimes he never reported himself are his partner's (the joint crime mode) -- told
    /// plainly, so a fine is never taken for something else (the field: "i hit [the host] and got a 60 fine", the host's lockpicking).</summary>
    private void Wo211OnStop(IEnumerable<string> kinds)
    {
        var mine = new HashSet<string>(_w211OwnCrimes.Keys, StringComparer.Ordinal);
        var partner = Wo211Rules.PartnerKinds(kinds, mine);
        if (partner.Count == 0) return;
        long k = Interlocked.Increment(ref _w211JointTold);
        string text = Wo211Rules.JointStopText(partner);
        Console.WriteLine($"WO211-CRIME joint stop: {string.Join(",", partner)} {(W211("joint_crime_told") ? "told" : "not told (mp_joint_crime_told off)")}: \"{text}\" [n={k}]");
        if (W211("joint_crime_told")) _ = ExecLuaAsync($"KCD2MP_ShowNativeToast(\"{EscapeLua(text)}\")");
    }
}
