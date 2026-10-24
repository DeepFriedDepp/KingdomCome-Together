// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-206 E: the epochs' bridge half -- the switches (the mod's w206_cfg), the announcement and its answer, the reader's stamps, the
// processor's check, the host's bumps, mp_w206_status. The pure rules and the counters are Wo206Rules / Wo206Epochs (Wo206.cs).
public partial class GameBridge
{
    private readonly Wo206Epochs _w206 = new();
    private readonly ConcurrentDictionary<string, bool> _w206On = new(StringComparer.Ordinal);
    private bool W206(string name) => _w206On.TryGetValue(name, out bool v) ? v : Wo206Rules.DefaultOn(name);

    private uint _w206FrameStamp;        // the processor (one reader): the stamp of the frame it handles now
    private uint _w206RewindJoinEpoch;   // the join epoch known when the host's reload began here (WO-125's rewinding)
    private uint _w206ProcWorld;         // the processor: the world epoch it last saw (a change drops the old world's pending Lua pushes)
    private volatile bool _w206LoadBumped;
    private long _w206AnnouncedMs, _w206DropLogged;
    private readonly ConcurrentDictionary<uint, byte> _w206OwnAttempts = new();   // a joiner: every join id it asked for (bounded below)

    private bool W206Host => _combatRoleApplied && _isDamageAuthority;

    /// <summary>The C3 twin's research path: a death announced from the health reading again. Only with KCDMP_TEST_W206=1.</summary>
    private bool W206ResearchDeathFromHealth =>
        Wo206Rules.ResearchEffective(W206("research_death_from_health"), Environment.GetEnvironmentVariable(Wo206Rules.ResearchEnv));

    private void Wo206SyncPublic() => Wo206Applied.SetEpochs(_w206.World, _w206.Join);

    // ---------------------------------------------------------------- the reader

    /// <summary>The reader: the stamp of a frame read now (its source is payload[0] on every guarded type).</summary>
    private uint Wo206StampAtRead(byte[] payload) => payload.Length > 0 ? _w206.StampFor(payload[0], W206Host) : 0;

    /// <summary>The reader: an epoch frame is taken here, never behind the queue -- every frame read after it is stamped with the new
    /// epoch, every one read before keeps the old. True = consumed.</summary>
    private bool Wo206TakeAtRead(int type, byte[] payload)
    {
        if (!Wo206Rules.TryReadEpochFrame(type, payload, out byte src, out uint world, out uint join, out string why)) return false;
        if (why == Wo206Rules.Ack)
        {
            _w206.NoteAck(src, world);   // the host: this joiner's frames from here on were sent knowing this world
            return true;
        }
        if (W206Host) return true;   // a host never takes another machine's epochs
        bool changed = _w206.LearnWorld(world, join);
        Wo206SyncPublic();
        if (changed) Console.WriteLine($"MP-W206 the host's world epoch is {world} now ({why}; join epoch {join}) -- what was read before this is the old world's");
        // the answer goes out at once: everything this agent sends after it was sent knowing the new world
        _ = Wo203SendSideAsync(Protocol.JoinTargetHost, Protocol.W203Epoch, Wo206Rules.EpochText(world, join, Wo206Rules.Ack));
        return true;
    }

    // ---------------------------------------------------------------- the joiner's own pipeline (B2)

    /// <summary>WO-206 B2 (the bench, a 5 s cut at "world-sent"): the joiner's join was aborted (relay lost) while its pipeline was
    /// still splicing, and the pipeline went on to place and LOAD the abandoned world. True = this attempt is no longer the live one:
    /// stop here. The twin (KCDMP_TEST_W206_NO_JOIN_GONE=1, a test machine only) runs on as before.</summary>
    private bool Wo206JoinGone(JoinerJoin j, string at)
    {
        bool live = ReferenceEquals(_jj, j);
        if (live) return false;
        if (!Wo206Rules.StopAbortedJoin(live, Environment.GetEnvironmentVariable("KCDMP_TEST_W206_NO_JOIN_GONE")))
        {
            Console.WriteLine($"MP-W206 TWIN join 0x{j.JoinId:x8} was aborted but its pipeline runs on ({at}) -- the old behaviour");
            return false;
        }
        Console.WriteLine($"MP-JOIN joiner: join 0x{j.JoinId:x8} was aborted while it was prepared -- stopped {at}, nothing more placed or loaded");
        return true;
    }

    // ---------------------------------------------------------------- the processor

    /// <summary>The processor, every frame: a world write stamped with an older world epoch is dropped (counted per category).</summary>
    private bool Wo206DropStale(in InFrame f)
    {
        _w206FrameStamp = f.Stamp;
        uint w = _w206.World;
        if (w != _w206ProcWorld)
        {
            // the Lua pushes still pending (the coalescer) are the old world's rows
            if (_w206ProcWorld != 0 && _w206.Check) _npcLua.Clear();
            _w206ProcWorld = w;
        }
        if (Wo206Rules.WorldCategory(f.Type, f.Payload) is not Wo206Cat cat) return false;
        if (_w206.AdmitWorld(cat, f.Stamp)) return false;
        Wo206LogDrop($"{cat.ToString().ToLowerInvariant()} frame 0x{f.Type:X2} from ghost {(f.Payload.Length > 0 ? f.Payload[0] : 0)} stamped world {f.Stamp}, now {w}");
        return true;
    }

    /// <summary>A world write of category <paramref name="cat"/> stamped <paramref name="stamp"/> at a later apply point (a queue's drain):
    /// true = apply.</summary>
    private bool Wo206Admit(Wo206Cat cat, uint stamp, string what)
    {
        if (_w206.AdmitWorld(cat, stamp)) return true;
        Wo206LogDrop($"{cat.ToString().ToLowerInvariant()} {what} stamped world {stamp}, now {_w206.World}");
        return false;
    }

    private void Wo206LogDrop(string what)
    {
        long n = Interlocked.Increment(ref _w206DropLogged);
        if (n <= 50 || n % 100 == 0) Console.WriteLine($"MP-W206 dropped: {what} [n={n}]");
    }

    // ---------------------------------------------------------------- the host's bumps and its announcement

    /// <summary>The host's load ("Loading saved game", or "[CryAction] LoadGame" when that line was missed): one bump per load.</summary>
    private void Wo206HostWorldLoad(string why)
    {
        if (!W206Host || _w206LoadBumped) return;
        _w206LoadBumped = true;
        uint e = _w206.BumpWorld();
        Wo206SyncPublic();
        Console.WriteLine($"MP-W206 host: a load ({why}) -- world epoch {e}; nothing stamped with an older one is applied any more");
        _ = Wo206AnnounceAsync("load");
    }

    /// <summary>A load ended (Gameplay started, or the engine went back to the menu): the next load bumps the world epoch again.</summary>
    private void Wo206LoadSettled() => _w206LoadBumped = false;

    /// <summary>The host started a join (a new HostJoin): a new join epoch.</summary>
    private void Wo206HostJoinStarted(uint joinId)
    {
        uint e = _w206.StartJoin(joinId);
        Wo206SyncPublic();
        Wo206DropStaleJoinerViews();
        Console.WriteLine($"MP-W206 host: join 0x{joinId:x8} started -- join epoch {e}");
        _ = Wo206AnnounceAsync("join-start");
    }

    /// <summary>The host's join ended: anything but the joiner's ready is an abort -- a new join epoch, so nothing of that attempt counts.</summary>
    private void Wo206HostJoinEnded(uint joinId, string reason)
    {
        bool aborted = reason != "ready";
        uint e = _w206.EndJoin(joinId, aborted);
        Wo206SyncPublic();
        if (!aborted) return;
        Wo206DropStaleJoinerViews();
        Console.WriteLine($"MP-W206 host: join 0x{joinId:x8} aborted ({reason}) -- join epoch {e}: a late 'loading' of it is dropped");
        _ = Wo206AnnounceAsync("join-abort");
    }

    /// <summary>The host's kept joiner states of an older join epoch go (the leash's and the vote's view): the next report decides.</summary>
    private void Wo206DropStaleJoinerViews()
    {
        foreach (byte g in _w206.StaleJoinerViews())
            if (_leashJoinerState.TryRemove(g, out _)) Console.WriteLine($"MP-W206 host: joiner {g}'s kept state belongs to an older join epoch -- dropped until its next report");
    }

    private async Task Wo206AnnounceAsync(string why, byte? only = null)
    {
        if (!W206Host) return;
        _w206.EnsureHost();
        Wo206SyncPublic();
        Volatile.Write(ref _w206AnnouncedMs, Environment.TickCount64);
        string text = Wo206Rules.EpochText(_w206.World, _w206.Join, why);
        List<byte> to = only is byte o ? [o] : LivePartners();
        foreach (byte g in to) await Wo203SendSideAsync(g, Protocol.W203Epoch, text);
    }

    /// <summary>A partner connected: it hears the epochs at once (the 10 s announcement covers the rest).</summary>
    private void Wo206OnPeerConnected(byte id)
    {
        _w206.ForgetPeer(id);
        _w206.ForgetJoiner(id);
        if (W206Host) _ = Wo206AnnounceAsync("peer", id);
    }

    /// <summary>The 4 Hz session tick: the stats line every minute with a partner; the host's announcement every 10 s.</summary>
    private void Wo206Tick(long n)
    {
        if (n % 240 == 120 && LivePartners().Count > 0) Wo206Stats();
        if (!W206Host) return;
        if (Environment.TickCount64 - Volatile.Read(ref _w206AnnouncedMs) < Wo206Rules.AnnounceEveryMs) return;
        if (LivePartners().Count == 0) { Volatile.Write(ref _w206AnnouncedMs, Environment.TickCount64); return; }
        _ = Wo206AnnounceAsync("tick");
    }

    /// <summary>The host: a joiner's message of attempt <paramref name="joinId"/> (request, ack, done, ready, abort, its load progress) is
    /// taken only while that attempt is the live one at the current join epoch.</summary>
    private bool Wo206JoinMessageAdmitted(int type, uint joinId, byte src, string? kind = null)
    {
        kind ??= type switch
        {
            Protocol.JoinRequestDown => "request", Protocol.WorldAckDown => "ack", Protocol.WorldDoneDown => "done",
            Protocol.JoinerReadyDown => "ready", Protocol.JoinAbortDown => "abort", _ => "join",
        };
        if (_w206.AdmitJoin(kind, _w206.JoinAttemptStale(joinId))) return true;
        Wo206LogDrop($"{kind} of join 0x{joinId:x8} from ghost {src} -- that attempt is closed (join epoch {_w206.Join})");
        return false;
    }

    // ---------------------------------------------------------------- the joiner's half of the join epoch

    /// <summary>The join epoch this joiner's LeashState carries (Wo206Rules.JoinerStateStamp).</summary>
    private uint Wo206JoinerStateStamp()
    {
        bool attempt = _jj is not null || _joinRx is not null || _joinOutId != 0 || _joinReceivedId != 0;
        bool place = _where is GameWhere.Loading or GameWhere.Menu;
        return Wo206Rules.JoinerStateStamp(_w206.Join, _w206RewindJoinEpoch, _rewinding && !attempt && !place);
    }

    private void Wo206NoteOwnAttempt(uint joinId)
    {
        if (joinId == 0) return;
        if (_w206OwnAttempts.Count > 256) _w206OwnAttempts.Clear();
        _w206OwnAttempts[joinId] = 1;
    }

    /// <summary>A host status for an attempt (states 1..7) that is not live here: the joiner's UI never shows it.</summary>
    private bool Wo206JoinerStatusAdmitted(uint joinId, byte state)
    {
        bool live = joinId != 0 && (joinId == _joinOutId || joinId == _joinRx?.JoinId || joinId == _joinReceivedId || joinId == _jj?.JoinId);
        bool stale = Wo206Rules.JoinerStatusStale(state, live, _w206OwnAttempts.ContainsKey(joinId));
        if (_w206.AdmitJoin("status", stale)) return true;
        Console.WriteLine($"MP-W206 joiner: host status {Protocol.JoinStateName(state)} for join 0x{joinId:x8} dropped -- not an attempt live here");
        return false;
    }

    // ---------------------------------------------------------------- APPLIED (Part C)

    private void Wo206NoteApplied(string cat, string key, string cause, string? before, string? after, uint? epoch = null) =>
        Wo206Applied.Write(_isDamageAuthority, cat, key, cause, before, after, epoch);

    /// <summary>A death apply's cause: its path ("0x27 dead transition" -> "0x27-dead-transition").</summary>
    private static string Wo206DeathCause(string via)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in via)
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-' or '_' && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        string t = sb.ToString().Trim('-');
        return t.Length > 0 ? t : "death";
    }

    // ---------------------------------------------------------------- switches and the stats line

    private void Wo206OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w206_cfg":
                if (!Wo206Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W206 cfg refused: '{arg}'"); return; }
                _w206On[cfg] = on;
                if (cfg == "epoch_check") _w206.Check = on;
                Console.WriteLine($"MP-W206 cfg {cfg}={(on ? "on" : "off")}");
                if (cfg == "research_death_from_health" && on && !W206ResearchDeathFromHealth)
                    Console.WriteLine($"MP-W206 research_death_from_health is on but does nothing here: it needs {Wo206Rules.ResearchEnv}=1 (a test machine only)");
                return;
            case "w206_status":
                Wo206Stats();
                Console.WriteLine(Wo206DigestStats());   // C/D: the digests and the probe
                _ = Wo206DigestNativeStatusAsync();
                return;
        }
    }

    private void Wo206Stats()
    {
        Console.WriteLine(_w206.StatsLine(Wo206Applied.Written)
            + $" research_death_from_health={(W206("research_death_from_health") ? "on" : "off")}{(W206ResearchDeathFromHealth ? "(effective)" : "")}");
    }
}
