// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-202 Act III, the network's half.
//   III.1 net_game_queue: where the field's 40-100 s backlog built. The relay socket is read at once (ReceiveLoopAsync), but every frame
//      then waits in ONE in-process queue whose handlers await the game's console; a game that answers slowly (in the background, ~25 fps,
//      or loading) let the queue grow while the reader kept filling it, and session A's joiner's pings measured that queue (35-107 s,
//      draining in shrinking bursts). A state frame -- an avatar's position, an NPC's state -- that is still waiting when a newer one of the
//      same body arrives is replaced in place (it keeps its place in the order, it carries the newest content), never queued twice; the
//      native writer already took every sample at the reader. Events, edges and messages keep their order and are never dropped.
//   III.2 ping_network: the pong and the clock-sync reply are taken where they arrive, so the ping is the network's round trip and the
//      clock offset is not skewed by the queue (a late sample used to read its queue delay as one-way delay); the mod is told the queue's
//      own lag and whether the game window is in front, so its connection toast blames the right thing.
public partial class GameBridge
{
    private const int Wo202CoalescedType = -2;   // an InFrame that stands for the newest waiting state of one body
    private readonly object _w202QLock = new();
    private readonly Dictionary<string, InFrame> _w202Latest = new(StringComparer.Ordinal);
    private long _w202Coalesced, _w202PingsAtRead, _w202ClockAtRead;
    private int _w202LastPingMs = -1;

    /// <summary>The reader: a pong or a clock-sync reply is handled here, never behind the queue. True = consumed.</summary>
    private bool Wo202TakeAtRead(int type, byte[] payload)
    {
        if (type == Protocol.Pong && payload.Length == 8)
        {
            long ts = BinaryPrimitives.ReadInt64LittleEndian(payload);
            if (!_pingsSent.TryRemove(ts, out long sentAt)) return true;
            int ms = (int)((Stopwatch.GetTimestamp() - sentAt) * 1000L / Stopwatch.Frequency);
            Interlocked.Increment(ref _w202PingsAtRead);
            _w202LastPingMs = ms;
            long lag = Wo202QueueLagMs();
            int fg = Wo202GameInFront();
            Console.WriteLine($"[ping] {ms} ms (network){(lag >= 500 ? $" -- the game's queue is {lag} ms behind{(fg == 0 ? ", the game is in the background" : "")}" : "")}");
            _stats.OnPong(ms);
            _ = ExecLuaAsync($"KCD2MP_ShowPing({ms}, {lag}, {fg})");   // never awaited here: the reader never waits on the game
            return true;
        }
        if (type == Protocol.ClockSyncDown && payload.Length == Protocol.ClockSyncDownPayloadLen)
        {
            long t3 = DateTime.UtcNow.Ticks;
            Interlocked.Increment(ref _w202ClockAtRead);
            OnClockSample(BinaryPrimitives.ReadInt64LittleEndian(payload), BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(8)),
                          BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(16)), t3);
            return true;
        }
        return false;
    }

    /// <summary>How far behind the reader the frame processor is right now (ms; 0 when it keeps up).</summary>
    private long Wo202QueueLagMs()
    {
        long at = Volatile.Read(ref _w147ProcAtMs);   // the arrival stamp of the frame the processor handles now
        if (at <= 0) return 0;
        // WO-207 B.4: nothing waits for the processor -> it keeps up. Measured against every frame read, an idle processor "trailed"
        // the pings and leash frames of a joiner's load: the host's queue read 34 s behind through a join hold while nothing waited.
        // Off (the twin, KCDMP_TEST_W207_OFF=queue_measure): measured against every frame read, as before.
        bool own = W207("queue_measure");
        if (own && Wo207Trace.WaitingTotal == 0) return 0;
        long newest = own ? Volatile.Read(ref _w207LastQueuedMs) : Volatile.Read(ref _w202LastReadMs);
        return Math.Max(0, newest - at);   // how far it trails the newest frame it was handed (0 when it keeps up)
    }
    private long _w202LastReadMs;
    private long _w207LastQueuedMs;   // WO-207 B.4: the newest frame handed to the processor's own queue

    /// <summary>The reader hands the processor a frame: counted first (the processor may take it at once), uncounted if refused.</summary>
    private bool Wo207Queue(ChannelWriter<InFrame> writer, InFrame f, int kind)
    {
        Wo207Trace.Enqueued(kind);
        if (f.Arrival > 0) Volatile.Write(ref _w207LastQueuedMs, W147StampMs(f.Arrival));
        if (writer.TryWrite(f)) return true;
        Wo207Trace.Dequeued(kind);
        return false;
    }

    /// <summary>The reader: a state frame of a body that already waits is replaced in place. True = consumed (queued or merged).</summary>
    private bool Wo202Coalesce(ChannelWriter<InFrame> writer, int type, byte[] payload, long arrival, uint stamp)
    {
        Volatile.Write(ref _w202LastReadMs, W147StampMs(arrival));
        string? key = Wo202Rules.StateKey(type, payload);
        if (key is null) return false;
        lock (_w202QLock)
        {
            if (_w202Latest.ContainsKey(key))
            {
                _w202Latest[key] = new InFrame(type, payload, arrival, stamp);
                _w202Coalesced++;
                Volatile.Write(ref _w207LastQueuedMs, W147StampMs(arrival));   // WO-207 B.4: the waiting slot is this frame now
                return true;
            }
            _w202Latest[key] = new InFrame(type, payload, arrival, stamp);
        }
        Wo207Queue(writer, new InFrame(Wo202CoalescedType, Encoding.UTF8.GetBytes(key), arrival, 0), type);   // WO-207 B.1
        return true;
    }

    /// <summary>The processor: a coalesced slot becomes the newest frame of its body (null when it was already taken).</summary>
    private InFrame? Wo202Resolve(InFrame f)
    {
        if (f.Type != Wo202CoalescedType) return f;
        string key = Encoding.UTF8.GetString(f.Payload);
        lock (_w202QLock)
        {
            if (!_w202Latest.Remove(key, out var latest)) return null;
            return latest;
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>1 = the game's window is in front, 0 = another window is, -1 = unknown.</summary>
    private static int Wo202GameInFront()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return -1;
            GetWindowThreadProcessId(h, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return string.Equals(p.ProcessName, "KingdomCome", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        catch { return -1; }
    }
}

// WO-202 III.3 rejoin_recover, the joiner's half: a rejoin that failed (the host aborted, the transfer died) clears what kept this game
// "reloading" (the leash then reports it in the world, the host's saves pair again, the sleep vote asks it), is asked again by itself up to
// three times with growing gaps, and then the player is asked -- try again, or leave -- instead of being sent to the main menu without a word.
public partial class GameBridge
{
    private volatile int _w202RejoinFails;
    private volatile bool _w202RejoinAsked;

    // WO-207 C.4: the link dropped while a join or an in-world rejoin ran (the bench: the relay cut it at the host's world transfer). The
    // game leaves for its own world as before; once the relay is back it asks for the host's world again from there (WO-125's join from
    // a world), and that leave's failed join is not "the same failure" that stops automatic tries. Off (the twin): it stayed in its own
    // world with "quit and wait at the main menu".
    private volatile bool _w207LinkRejoin;

    private void Wo207ArmLinkRejoin(uint joinId)
    {
        _w207LinkRejoin = true;
        _joinFromWorldOnce = true;
        Console.WriteLine($"MP-W202 rejoin: the relay dropped while {(joinId != 0 ? $"join 0x{joinId:x8}" : "an in-world rejoin")} ran -- once it is back this game asks for the host's world again (WO-207 C.4)");
    }

    private long _w207TraceMs;

    private void Wo207LinkRejoinTrace()
    {
        long now = Environment.TickCount64;
        if (now - _w207TraceMs < 20_000) return;
        _w207TraceMs = now;
        Console.WriteLine($"MP-W202 rejoin gates: host_mode={(_hostModeKnown ? (_hostSharedWorld ? "shared" : "separate") : "unknown")} jj={(_jj is null ? "-" : _jj.Phase)} rx={(_joinRx is null ? "-" : "on")} " +
                          $"out=0x{_joinOutId:x8} leaving={_leaveInProgress} own_load_expected={_ownLoadExpected} where={_where} from_world={_joinFromWorldOnce} joined={_joinedWorld} " +
                          $"rejoin_pending={_rejoinPending} auto={_autoRequests} next_in_s={(int)Math.Max(0, (_autoNextUtc - DateTime.UtcNow).TotalSeconds)} target={_w207LeaveTarget ?? "-"}");
    }

    private string? _w207LeaveTarget;
    private long _w207LeaveAskedMs;

    /// <summary>The leave's own save is the mod's last load (asked at most every 5 s, 10 s after the leave began): it is in.</summary>
    private async Task<bool> Wo207LeaveLandedAsync()
    {
        if (_w207LeaveTarget is not { } target || (DateTime.UtcNow - _leaveSinceUtc).TotalSeconds < 10) return false;
        long now = Environment.TickCount64;
        if (now - _w207LeaveAskedMs < 5000) return false;
        _w207LeaveAskedMs = now;
        string last = await AskModAsync("KCD2MP_Wo125LastLoaded", 4000);
        string got = last.StartsWith("last=", StringComparison.Ordinal) ? last[5..] : "";
        if (!string.Equals(got, target, StringComparison.OrdinalIgnoreCase)) return false;
        _ownLoadExpected = false;
        Console.WriteLine($"MP-W202 rejoin: the leave's own load is in ('{got}', its Gameplay started was not seen while the link was down) -- the join gate opens (WO-207 C.4)");
        try { await ExecLuaAsync("if KCD2MP_Wo124Where then KCD2MP_Wo124Where() end"); } catch { }   // the mod's own word on where it is
        return true;
    }

    private void Wo202RejoinReset(string why)
    {
        if (_w202RejoinFails != 0 || _w202RejoinAsked) Console.WriteLine($"MP-W202 rejoin: the count starts over ({why})");
        _w202RejoinFails = 0;
        _w202RejoinAsked = false;
    }

    /// <summary>The host aborted an in-world rejoin (or it died on the way).</summary>
    private void Wo202RejoinFailed(string why)
    {
        if (!_rejoinPending || !W202("rejoin_recover")) return;
        _w202RejoinFails++;
        _rewinding = false;   // nothing waits for "the host is reloading" any more: the host is back, the rejoin is what is left
        _autoNextUtc = DateTime.UtcNow.AddSeconds(Wo202Rules.RejoinGapS(_w202RejoinFails - 1));
        Console.WriteLine($"MP-W202 rejoin failed ({why}), try {_w202RejoinFails} of {Wo202Rules.RejoinTries} -- the reloading state is cleared"
                          + (Wo202Rules.RejoinShouldAsk(_w202RejoinFails) ? "; the player is asked next" : $"; asked again in {Wo202Rules.RejoinGapS(_w202RejoinFails - 1)} s"));
        SetJoinUi("rejoin-retry", "Rejoining your host after its reload...");
    }

    private async Task Wo202AskRejoinAsync()
    {
        _w202RejoinAsked = true;
        Console.WriteLine($"MP-W202 rejoin: {_w202RejoinFails} tries failed -- asking the player (F11 try again / F12 leave)");
        SetJoinUi("rejoin-ask", Wo202Rules.RejoinQuestion);
        await ExecLuaAsync($"if KCD2MP_W140Prompt then KCD2MP_W140Prompt({Wo202Rules.RejoinPromptId}, \"{EscapeLua(Wo202Rules.RejoinQuestion)}\", 600) end");
    }

    private async Task Wo202RejoinAnsweredAsync(bool tryAgain)
    {
        if (!_w202RejoinAsked) return;
        _w202RejoinAsked = false;
        if (tryAgain)
        {
            Console.WriteLine("MP-W202 rejoin: the player said try again");
            _w202RejoinFails = 0;
            _autoNextUtc = DateTime.UtcNow;
            return;
        }
        Console.WriteLine("MP-W202 rejoin: the player chose to leave the host's world");
        _rejoinPending = false;
        await LeaveSharedWorldAsync("rejoin-declined", "You left your host's world (the reload didn't reach you).");
    }
}

// WO-202 VI: files the mod itself moved out of a playline (the joiner's Henry pipeline: a snapshot's QuickSave). The save check skips them.
public partial class GameBridge
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _w202MovedOut = new(StringComparer.OrdinalIgnoreCase);

    private void Wo202NoteMovedOut(string path) => _w202MovedOut[Wo202Rules.SavePathKey(path)] = DateTime.UtcNow;

    private bool Wo202MovedByUs(string path) =>
        _w202MovedOut.TryGetValue(Wo202Rules.SavePathKey(path), out var at) && Wo202Rules.MovedRecently(at, DateTime.UtcNow) && !File.Exists(path);
}
