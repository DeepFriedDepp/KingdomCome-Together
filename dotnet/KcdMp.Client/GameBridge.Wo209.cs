// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-209 (docs/WO-209-findings.md): the bridge's half -- the switches (Lua w209_cfg, mp_w209_status), the DLL's switch word, and A, each
// copy's opponent mirrored from the host's NPC's: the host's DLL reads each NPC's opponent near a player (op 136) on the NpcAnim stream's
// 10 Hz loop; the agent names a figure by its peer and sends the bodies that changed (NpcOpp, 0x7C -> 0x7D) to each joiner; the joiner's
// agent turns every name into this world's entity (the host's player is the host's figure here, the joiner's own figure there is the local
// player here, an NPC is its copy) and hands them to its DLL (op 137), whose mirror sets the copy's opponent the engine's way (wo209.cpp).
// The pure rules are Wo209Rules (Wo209.cs).
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w209On = new(StringComparer.Ordinal);
    private long _w209OppPolls, _w209OppBodiesOut, _w209OppFramesIn, _w209OppBodiesIn, _w209OppUnknown, _w209OppBad, _w209KeptEngaged;
    private long _w209RatioDirty, _w209SkipQuiet, _w209ForcedYields, _w209StaleKept, _w209VerdictWaitSkipped, _w209VerdictWaitFaults;
    private readonly ConcurrentDictionary<string, long> _w209NoneAt = new(StringComparer.Ordinal);   // joiner: attacker -> this engine's "none"
    private readonly ConcurrentDictionary<string, (byte Kind, long At)> _w209OppLast = new(StringComparer.Ordinal);   // joiner: the host's word per NPC
    private readonly ConcurrentDictionary<string, long> _w209NpcIn = new(StringComparer.Ordinal);

    /// <summary>The joiner's minute line: the host's position samples read per NPC in the last minute (a copy that stands while its host NPC
    /// walks: were its samples there?).</summary>
    private void Wo209NpcInLine()
    {
        var snap = _w209NpcIn.ToArray();
        _w209NpcIn.Clear();
        if (snap.Length == 0) return;
        Console.WriteLine((W137Host ? "MP-W209-NPCOUT " : "MP-W209-NPCIN ") + string.Join(' ', snap.OrderByDescending(kv => kv.Value).Take(24).Select(kv => kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture))));
    }
    private readonly ConcurrentDictionary<byte, (int Cid, string Path, long StartMs, string Anchor)> _w209PartnerForced = new();

    /// <summary>
    /// D.2: this machine's forced talk against the partner's (one partner, or every one that announced a forced talk): the duplicate ends the
    /// game's own way (InterruptDialog) and its player listens to the partner's, the same join as C.4's.
    /// </summary>
    private void Wo209CheckForcedTwin(object talkObj, byte? onlySrc)
    {
        if (talkObj is not W203Talk mine || mine.ForcedPath.Length == 0) return;
        foreach (var (src, pf) in _w209PartnerForced.ToArray())
        {
            if (onlySrc is byte o && o != src) continue;
            if (!Wo209Rules.ForcedTalkYields(W209("forced_talk_once"), mine.ForcedPath, pf.Path, mine.ForcedStartMs, pf.StartMs, W137Host)) continue;
            long n = Interlocked.Increment(ref _w209ForcedYields);
            Console.WriteLine(FormattableString.Invariant($"WO209-FORCED this player's forced talk {mine.Cid} ('{mine.ForcedPath}', started {mine.ForcedStartMs - pf.StartMs:+0;-0} ms after the partner's) is partner {src}'s conversation {pf.Cid} already -- ends here, and this player listens to the partner's [n={n}]"));
            _ = Task.Run(async () =>
            {
                await ExecLuaAsync("pcall(function() if player.human:IsInDialog() then player.human:InterruptDialog() end end)");
                await Task.Delay(800);
                await ExecLuaAsync($"if KCD2MP_W203Offer then KCD2MP_W203Offer({src}, {pf.Cid}, 1, \"{EscapeLua(pf.Anchor)}\", 1) end");
            });
            return;
        }
    }

    /// <summary>D.1: a host clock message that came while this machine's own time-lapse ran -- no ratio written (told once per skip).</summary>
    private void Wo209NoteSkipQuiet()
    {
        long n = Interlocked.Increment(ref _w209SkipQuiet);
        if (Interlocked.Exchange(ref _w209RatioDirty, 1) == 0)
            Console.WriteLine($"WO209-CLOCK this machine's own time-lapse runs: no clock correction is written over it; the next one after it writes afresh (#{n})");
    }

    /// <summary>A WO-209 switch: on unless the mod said off (a test twin's KCDMP_TEST_W209_OFF=name,name turns it off from the start).</summary>
    private bool W209(string name) =>
        _w209On.TryGetValue(name, out bool v) ? v
            : Wo209Rules.Default(name, Environment.GetEnvironmentVariable("KCDMP_TEST_W209_OFF"), Environment.GetEnvironmentVariable("KCDMP_TEST_W209_ON"));

    private async Task Wo209PushConfigAsync()
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo209OpConfig, BitConverter.GetBytes(Wo209Rules.ConfigBits(W209)));
            if (r is { Ok: true, Payload.Length: >= 4 } x) _w209DllEff = BitConverter.ToUInt32(x.Payload, 0);   // the DLL's own word, less its twin's
        }
        catch (Exception ex) { Console.WriteLine($"MP-W209 config not sent to the DLL: {ex.Message}"); }
    }

    private volatile uint _w209DllEff = uint.MaxValue;   // unknown until the DLL answered: as the agent's word

    /// <summary>A switch with a DLL bit, as the DLL runs it: the agent's word and the DLL's (a game-side twin turns the DLL's off).</summary>
    private bool W209Eff(string name, uint bit) => W209(name) && (_w209DllEff & bit) != 0;

    private async Task Wo209NativeStatusAsync()
    {
        var r = await _combat.Wo163Async(Protocol.Wo209OpStatus, []);
        if (r is { Ok: true } rr) Console.WriteLine($"MP-W209-NATIVE {System.Text.Encoding.ASCII.GetString(rr.Payload)}");
    }

    /// <summary>The 4 Hz session tick (Wo201Tick).</summary>
    private void Wo209Tick(long n)
    {
        if (n % 8 == 5) _ = Wo209PushConfigAsync();   // every 2 s: a DLL that came back (a rejoin, a new game) has the switches
        if (n % 120 == 7 && (W137Host || W137Joiner)) _ = Wo209NativeStatusAsync();   // every 30 s: the mirror's own numbers in the agent's log
        if (n % 240 == 11 && (W137Joiner || W137Host)) Wo209NpcInLine();
    }

    // ---------------------------------------------------------------- A, the host

    /// <summary>The host, 10 times a second (the NpcAnim stream's loop): the NPCs whose opponent changed, to every joiner in the world.</summary>
    private async Task Wo209OppTickAsync()
    {
        if (!W137Host || !W209("copy_opponent_mirror")) return;
        var peers = Wo134Peers();
        if (peers.Count == 0) return;
        var r = await _combat.Wo163Async(Protocol.Wo209OpOppRead, [(byte)Protocol.NpcOppMaxBodies]);
        Interlocked.Increment(ref _w209OppPolls);
        if (r is not { Ok: true } rr || rr.Payload.Length < 2 || rr.Payload[0] == 0) return;
        if (!Wo209Rules.TryHostWire(rr.Payload, Wo209GhostOfEntity, out byte[] wire, out int bodies)) { Interlocked.Increment(ref _w209OppBad); return; }
        if (bodies == 0) return;
        Interlocked.Add(ref _w209OppBodiesOut, bodies);
        foreach (byte j in peers)
        {
            try { await WriteJoinAsync(Protocol.BuildJoinUp(Protocol.NpcOppUp, j, 0, wire)); }
            catch (Exception ex) { Console.WriteLine($"MP-W209 npc-opp not sent to {j}: {ex.Message}"); }
        }
    }

    /// <summary>The peer whose figure is this entity here (0xFF: none).</summary>
    private byte Wo209GhostOfEntity(uint eid)
    {
        if (eid == 0) return 0xFF;
        foreach (var kv in _ghostEntityIds)
            if (kv.Value == eid && byte.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte g)) return g;
        return 0xFF;
    }

    // ---------------------------------------------------------------- A, the joiner

    /// <summary>The joiner: the host's opponents (0x7D) to this DLL's mirror, in this world's entities.</summary>
    private async Task Wo209OnNpcOppAsync(byte src, byte[] body)
    {
        if (!W137Joiner) return;
        Interlocked.Increment(ref _w209OppFramesIn);
        long nowMs = Environment.TickCount64;
        foreach (var (n, k) in Wo209Rules.WireKinds(body)) _w209OppLast[n] = (k, nowMs);
        if (!Wo209Rules.TryJoinerOp(body, _myGhostId, src,
                                    name => _npcEntityIds.TryGetValue(name, out uint e) ? e : 0u,
                                    g => _ghostEntityIds.TryGetValue(g.ToString(CultureInfo.InvariantCulture), out uint fe) ? fe : 0u,
                                    out byte[] op, out int known, out int unknown))
        {
            Interlocked.Increment(ref _w209OppBad);
            return;
        }
        Interlocked.Add(ref _w209OppBodiesIn, known);
        Interlocked.Add(ref _w209OppUnknown, unknown);
        if (known == 0) return;
        Wo207Trace.Kind.Value = Protocol.NpcOppDown;   // a frame's pipe call: before the background polls at the gate
        await _combat.Wo163Async(Protocol.Wo209OpOppIn, op);
    }

    /// <summary>A ("adds only"): does the mirror's stream still say this copy's host NPC fights someone?</summary>
    private bool Wo209MirrorFights(string name) =>
        _w209OppLast.TryGetValue(name, out var o) && Wo209Rules.StaleKeeps(W209Eff("copy_opponent_mirror", 1u), o.Kind, Environment.TickCount64 - o.At);

    private string Wo209Stats() =>
        $"copy_opponent_mirror={(W209("copy_opponent_mirror") ? "on" : "off")} opp_polls={Interlocked.Read(ref _w209OppPolls)} opp_out={Interlocked.Read(ref _w209OppBodiesOut)}"
        + $" opp_frames_in={Interlocked.Read(ref _w209OppFramesIn)} opp_in={Interlocked.Read(ref _w209OppBodiesIn)} opp_not_here={Interlocked.Read(ref _w209OppUnknown)}"
        + $" opp_bad={Interlocked.Read(ref _w209OppBad)} kept_engaged={Interlocked.Read(ref _w209KeptEngaged)} skip_quiet={Interlocked.Read(ref _w209SkipQuiet)} forced_yields={Interlocked.Read(ref _w209ForcedYields)} stale_kept={Interlocked.Read(ref _w209StaleKept)} verdict_wait_skipped={Interlocked.Read(ref _w209VerdictWaitSkipped)} verdict_wait_faults={Interlocked.Read(ref _w209VerdictWaitFaults)}";

    private void Wo209OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w209_cfg":
                if (!Wo209Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W209 cfg refused: '{arg}'"); return; }
                _w209On[cfg] = on;
                Console.WriteLine($"MP-W209 cfg {cfg}={(on ? "on" : "off")}");
                _ = Wo209PushConfigAsync();
                return;
            case "w209_status":
                _ = Wo209NativeStatusAsync();
                Console.WriteLine($"MP-W209-STATS {string.Join(' ', Wo209Rules.SwitchNames.Select(n => $"{n}={(W209(n) ? "on" : "off")}"))} | {Wo209Stats()}");
                return;
        }
    }
}
