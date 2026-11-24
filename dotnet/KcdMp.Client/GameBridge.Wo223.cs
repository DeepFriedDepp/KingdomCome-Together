// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-223 (docs/WO-223-findings.md): enemies notice the joiner, and fight him on both screens. Every switch ships on (W223Switch;
// `mp_<name> on|off` in the game's console, `mp_w223_status` lists them); the bench's twin is KCDMP_TEST_W223_OFF=<names|all> in both
// agents' and both games' environment. Log tags: MP-W223, MP-W223-NATIVE, MP-W223-STATS; the mod's WO223-GATE / WO223-LOD and the
// DLL's WO223-ATTEND.
//
// B (w223_perceive):
//   * the joiner's agent sends his mod's stealth snapshot (WO-195's w195_stealth text: his own crouch, light, visibility, noise,
//     conspicuousness, stealth skill) to the host on the WO-203 side channel (W203Stealth), on a change and every ten seconds;
//   * the host's agent hands it to the host's mod (KCD2MP_W223Stealth) and runs the mod's look at every figure twice a second
//     (KCD2MP_W223Tick: an agent-driven beat, so a load that kills the mod's own timers never stops it);
//   * the mod names the hostile NPCs that would notice a player in his place (w223_attend "figure|npc,..."); the agent hands the set
//     to the DLL (op 213), which keeps the game's own attention pairs.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w223On = new(StringComparer.Ordinal);
    private bool W223On(string name) => _w223On.TryGetValue(name, out bool v) ? v : !W223Switch.TwinOff(name);
    private int _w223Pushing;
    private string _w223StealthSent = "";
    private readonly ConcurrentDictionary<byte, Wo223Rules.AnswerTally> _w223Answers = new();   // C (host): each joiner's last waits
    private readonly ConcurrentDictionary<(byte, string), List<long>> _w223Waiting = new();     // C (host): one attacker's blows still waiting (lock: the list)
    private long _w223OrderKept, _w223NoneEarly, _w223NoneGrace, _w223GrabBlows;

    /// <summary>C (w223_copy_target, the host): is this blow on a joiner's figure one struck inside a grab (Wo223Rules.GrabRow)? Then
    /// the host's own verdict goes at once; counted and logged apart from the guesses after a wait:
    ///   WO208-DEF grab npc=&lt;n&gt; joiner=&lt;g&gt; host_said=&lt;v&gt; -- ...</summary>
    private bool Wo223GrabBlow(byte ghost, string attacker, Guid row, HitVerdict hostSaid)
    {
        if (!W223On("w223_copy_target") || !W208("defence_local")) return false;
        ActionRowCatalog? cat = _rowCatalog.IsCompletedSuccessfully ? _rowCatalog.Result : null;
        if (cat is null || !cat.TryGet(row, out var r) || !Wo223Rules.GrabRow(r.Fragment, r.Tags)) return false;
        Interlocked.Increment(ref _w223GrabBlows);
        Console.WriteLine($"WO208-DEF grab npc={attacker} joiner={ghost} host_said={HitVerdictMsg.VerdictName(hostSaid)} -- a blow inside a grab: the joiner's game has no grab with a copy, the host's verdict at once (no wait)");
        return true;
    }
    private long _w223StealthSentAt, _w223StealthOut, _w223StealthIn, _w223AttendSets, _w223AttendRefused, _w223PairsSet, _w223PairsCleared, _w223PairsRefused;

    private async Task Wo223PushNativeAsync()
    {
        if (Interlocked.CompareExchange(ref _w223Pushing, 1, 0) != 0) return;
        try
        {
            var body = new byte[5];
            BitConverter.GetBytes(W223Switch.NativeBits(W223On)).CopyTo(body, 0);
            body[4] = Wo218Role();
            await _combat.Wo163Async(Protocol.Wo223OpConfig, body);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W223 the native word was not sent: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _w223Pushing, 0); }
    }

    private async Task Wo223NativeStatusAsync()
    {
        try
        {
            for (byte page = 0; page < 30; page++)
            {
                var r = await _combat.Wo163Async(Protocol.Wo223OpStatus, [page]);
                if (r is not { Ok: true } rr || rr.Payload.Length == 0) break;
                Console.WriteLine($"MP-W223-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
            }
        }
        catch { }
    }

    private async Task Wo223TellModAsync()
    {
        await ExecLuaAsync($"if KCD2MP_W223Cfg then KCD2MP_W223Cfg(\"{string.Join(',', W223Switch.Names.Select(n => n + "=" + (W223On(n) ? "on" : "off")))}\") end");
        if (!W137Host) return;
        // the engine's own recognition numbers, as the DLL read them from the game (nothing read: the mod keeps its built-in ones)
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo223OpEngine, []);
            if (r is { Ok: true, Payload.Length: > 0 } x && Wo223Rules.TryEngineText(Encoding.ASCII.GetString(x.Payload), out string text))
                await ExecLuaAsync($"if KCD2MP_W223Engine then KCD2MP_W223Engine(\"{text}\") end");
        }
        catch { }
    }

    private string Wo223StatsLine() =>
        $"MP-W223-STATS {string.Join(' ', W223Switch.Names.Select(n => $"{n}={(W223On(n) ? "on" : "off")}"))}" +
        $" stealth_out={Interlocked.Read(ref _w223StealthOut)} stealth_in={Interlocked.Read(ref _w223StealthIn)}" +
        $" attend_sets={Interlocked.Read(ref _w223AttendSets)} refused_lines={Interlocked.Read(ref _w223AttendRefused)}" +
        $" pairs_set={Interlocked.Read(ref _w223PairsSet)} pairs_cleared={Interlocked.Read(ref _w223PairsCleared)} pairs_refused={Interlocked.Read(ref _w223PairsRefused)}" +
        (_w223Answers.IsEmpty ? "" : " def_answered:" + string.Join(',', _w223Answers.Select(kv => { var (a, t) = kv.Value.Read(); return $"{kv.Key}:{a}/{t}"; }))) +
        $" copy_eyes_told={Interlocked.Read(ref _w223EyesTold)} def_grab_blows={Interlocked.Read(ref _w223GrabBlows)}" +
        $" def_left_to_an_older_blow={Interlocked.Read(ref _w223OrderKept)} def_none_before_the_blow={Interlocked.Read(ref _w223NoneEarly)} def_none_grace={Interlocked.Read(ref _w223NoneGrace)}" +
        $" called_on_the_host_pins={Interlocked.Read(ref _w223CallPins)} (refused {Interlocked.Read(ref _w223CallPinsRefused)})";

    /// <summary>The 4 Hz session tick: the native word every 2 s, the status and the stats every 30 s, the mod told every minute; the
    /// host's look at every figure twice a second; the joiner's stealth snapshot when it is due.</summary>
    private void Wo223Tick(long n)
    {
        if (n % 8 == 6) _ = Wo223PushNativeAsync();
        if (n % 120 == 79 && (W137Host || W137Joiner)) { _ = Wo223NativeStatusAsync(); Console.WriteLine(Wo223StatsLine()); }
        if (n % 240 == 99) _ = Wo223TellModAsync();
        if (n % 2 == 0 && W137Host) _ = ExecLuaAsync("if KCD2MP_W223Tick then KCD2MP_W223Tick() end");   // off as well as on: the mod puts its own state back
        if (W137Joiner) Wo223StealthTick();
        if (W137Joiner) Wo223CopyEyesTick(n);
    }

    private string _w223EyesSent = "";
    private long _w223EyesTold;

    /// <summary>C.2 (w223_copy_target, the joiner): once a second, the copies whose host NPC fights this player (the host's opponent
    /// stream, fresh within the mirror's own 4 s) are named to the mod, which holds each one's own perception off every other player's
    /// figure -- its own brain cannot pick the figure again (KCD2MP_W223CopyEyes). Told once more with nobody when the last one stops;
    /// the mod drops every hold by itself when the telling stops.</summary>
    private void Wo223CopyEyesTick(long n) { if (n % 4 == 1) Wo223CopyEyesTell(false); }

    /// <summary>... and at once when the host's opponent stream changes who fights this player (the hold is in place before the copy's
    /// fight is next repaired).</summary>
    private void Wo223CopyEyesTell(bool onlyIfChanged)
    {
        if (!W137Joiner) return;
        string names = W223On("w223_copy_target") ? Wo217OpponentNames() : "";
        if (names.Length == 0 && _w223EyesSent.Length == 0) return;
        if (onlyIfChanged && names == _w223EyesSent) return;
        _w223EyesSent = names;
        Interlocked.Increment(ref _w223EyesTold);
        _ = ExecLuaAsync($"if KCD2MP_W223CopyEyes then KCD2MP_W223CopyEyes(\"{EscapeLua(names)}\") end");
    }

    private void Wo223OnConnect(CancellationToken ct)
    {
        _w223StealthSent = ""; _w223StealthSentAt = 0;
        _ = Wo223TellModAsync(); _ = Wo223PushNativeAsync();
    }

    /// <summary>The session is over (or this game is no host any more): the mod puts the engine's own radius back and names nobody; the
    /// DLL clears a pair nobody named for six seconds by itself.</summary>
    private void Wo223OnDisconnect()
    {
        _w223Answers.Clear();
        _w223Waiting.Clear();
        _ = ExecLuaAsync("if KCD2MP_W223Reset then KCD2MP_W223Reset('the session ended') end");
    }

    /// <summary>The joiner: his mod's stealth snapshot (WO-195 keeps the latest) to the host, when it changed or every ten seconds.</summary>
    private void Wo223StealthTick()
    {
        if (!W223On("w223_perceive")) return;
        string now = _w195Stealth;
        long t = Environment.TickCount64;
        if (!Wo223Rules.StealthDue(now, _w223StealthSent, t - _w223StealthSentAt)) return;
        _w223StealthSent = now; _w223StealthSentAt = t;
        Interlocked.Increment(ref _w223StealthOut);
        _ = Wo203SendSideAsync(Protocol.JoinTargetHost, Protocol.W203Stealth, now);
    }

    /// <summary>The host: a joiner's own stealth snapshot arrived; his mod's parser decides what it is worth (an unknown text is nothing).</summary>
    private void Wo223OnStealth(byte src, LootMsg m)
    {
        if (!W137Host || !Wo195Rules.TryParseStealth(m.Text, out _)) { Interlocked.Increment(ref _w203SideMalformed); return; }
        Interlocked.Increment(ref _w223StealthIn);
        if (!W223On("w223_perceive")) return;
        _ = ExecLuaAsync($"if KCD2MP_W223Stealth then KCD2MP_W223Stealth(\"{src}\", \"{m.Text}\") end");
    }

    private void Wo223OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w223_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off") || !W223Switch.Names.Contains(arg[..eq])) { Console.WriteLine($"MP-W223 cfg refused: '{arg}'"); return; }
                _w223On[arg[..eq]] = arg[(eq + 1)..] == "on";
                Console.WriteLine($"MP-W223 cfg {arg}");
                _ = Wo223PushNativeAsync();
                _ = Wo223TellModAsync();
                return;
            }
            case "w223_status":
                _ = Wo223NativeStatusAsync();
                Console.WriteLine(Wo223StatsLine());
                return;
            case "w223_call":   // the host's mod called this enemy on the host's own player (B.3): the DLL pins it to him for a few seconds
                if (!W137Host || !Wo223Rules.TryCallName(arg, out string called)) return;
                _ = Wo223CallPinAsync(called);
                return;
            case "w223_attend":   // the host's mod: the hostile NPCs that attend to this figure now
                if (!W137Host) return;
                if (!Wo223Rules.TryAttendText(arg, out string text)) { Interlocked.Increment(ref _w223AttendRefused); return; }
                Interlocked.Increment(ref _w223AttendSets);
                _ = Wo223AttendSetAsync(text);
                return;
        }
    }

    private long _w223CallPins, _w223CallPinsRefused;

    private async Task Wo223CallPinAsync(string npc)
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo223OpCallPin, Encoding.ASCII.GetBytes(npc));
            bool ok = r is { Ok: true, Payload.Length: >= 1 } x && x.Payload[0] != 255;
            Interlocked.Increment(ref ok ? ref _w223CallPins : ref _w223CallPinsRefused);
            Console.WriteLine($"WO223-CALL npc={npc} called on the host's own player by his mod: the DLL's pin {(ok ? "set" : "refused")}");
        }
        catch { }
    }

    private async Task Wo223AttendSetAsync(string text)
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo223OpAttendSet, Encoding.ASCII.GetBytes(text));
            if (r is { Ok: true, Payload.Length: >= 3 } x)
            {
                Interlocked.Add(ref _w223PairsSet, x.Payload[0]);
                Interlocked.Add(ref _w223PairsCleared, x.Payload[1]);
                Interlocked.Add(ref _w223PairsRefused, x.Payload[2]);
            }
        }
        catch { }
    }
}
