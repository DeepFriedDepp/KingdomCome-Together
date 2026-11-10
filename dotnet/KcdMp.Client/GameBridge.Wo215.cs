// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-215 (docs/WO-215-findings.md): the animation substrate. Every switch ships on (W215Switch; `mp_w215 <name> on|off` in the game's
// console); the bench's twin is KCDMP_TEST_W215_OFF=<names|all> in both agents' and both games' environment.
//   A  route: a copy on the ground moves by the engine's own movement request (native wo215.cpp); gait: its speed class from the host's speed
//   B  ladder / letgo_cull: no empty body; the owner lets go only at the cull range
//   C  activity_line: one activity line per NPC
//   D  dormant: the host's census keeps dormant bodies out of the stream (D.2, the host NPC's outfit on its copy, is not built)
//   E  ghost_fight: the joiner's player leaves a fight whose every opponent is a ghost
//   G  probe_all: the animation probe covers the whole session (F, riders and carts, is measured, not built)
// Log tags: MP-W215, MP-W215-NATIVE, MP-W215-STATS, WO215-ROUTE, WO215-CORRECT, WO215-PROBE (the DLL's), WO215-STATUS.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w215On = new(StringComparer.Ordinal);
    private bool W215On(string name) => _w215On.TryGetValue(name, out bool v) ? v : W215Switch.DefaultOn(name) && !W215Switch.TwinOff(name);
    private bool _w215NativeToldOld;
    private long _w215Probes, _w215ProbeRefused;

    private async Task Wo215PushNativeAsync()
    {
        try
        {
            var body = BitConverter.GetBytes(W215Switch.NativeBits(W215On));
            var r = await _combat.Wo163Async(Protocol.Wo215OpConfig, body);
            if (r is not { Ok: true } && !_w215NativeToldOld) { _w215NativeToldOld = true; Console.WriteLine("MP-W215 the DLL did not take op 150 (an older KCDMP.dll?)"); }
        }
        catch (Exception ex) { Console.WriteLine($"MP-W215 the native word was not sent: {ex.Message}"); }
    }

    private async Task Wo215NativeStatusAsync()
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo215OpStatus, []);
            if (r is { Ok: true } rr) Console.WriteLine($"MP-W215-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
        }
        catch { }
    }

    /// <summary>The mod's own switches: told at every connect and every change.</summary>
    private Task Wo215TellModAsync() =>
        ExecLuaAsync($"if KCD2MP_W215Cfg then KCD2MP_W215Cfg(\"{string.Join(',', W215Switch.Names.Select(n => n + "=" + (W215On(n) ? "on" : "off")))}\") end");

    private string Wo215StatsLine() =>
        $"MP-W215-STATS {string.Join(' ', W215Switch.Names.Select(n => $"{n}={(W215On(n) ? "on" : "off")}"))} probes={Interlocked.Read(ref _w215Probes)}"
        + $" probe_refused={Interlocked.Read(ref _w215ProbeRefused)} ghost_lines={Interlocked.Read(ref _w215GhostLines)} ghost_told={Interlocked.Read(ref _w215GhostTold)} dormant_parks={Interlocked.Read(ref _w215DormantAsks)}";

    /// <summary>The 4 Hz session tick: the native word every 2 s, its status and the stats every 30 s.</summary>
    private void Wo215Tick(long n)
    {
        if (n % 8 == 7) _ = Wo215PushNativeAsync();
        if (n % 120 == 19 && (W137Host || W137Joiner)) { _ = Wo215NativeStatusAsync(); Console.WriteLine(Wo215StatsLine()); }
        if (n % 240 == 23) _ = Wo215TellModAsync();
        // E: once a second while a ghost was told in the last 30 s, the mod checks whether this player fights ghosts only
        if (n % 4 == 2 && W137Joiner && !_w215GhostToldMs.IsEmpty)
        {
            long now = Environment.TickCount64;
            if (_w215GhostToldMs.Values.Any(t => now - t < 30_000)) _ = ExecLuaAsync("if KCD2MP_W215GhostFightTick then KCD2MP_W215GhostFightTick() end");
        }
    }

    private void Wo215OnConnect() => _ = Wo215TellModAsync();

    private void Wo215OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w215_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off") || !W215Switch.Names.Contains(arg[..eq])) { Console.WriteLine($"MP-W215 cfg refused: '{arg}'"); return; }
                _w215On[arg[..eq]] = arg[(eq + 1)..] == "on";
                Console.WriteLine($"MP-W215 cfg {arg}");
                _ = Wo215PushNativeAsync();
                _ = Wo215TellModAsync();
                return;
            }
            case "w215_status":
                _ = Wo215NativeStatusAsync();
                Console.WriteLine(Wo215StatsLine());
                return;
            case "w215_probe":   // A.0 (the bench): "<mode> <name> <x> <y> <z> <yaw rad> <gait (|128 exact)> <secs>"
                _ = Wo215ProbeAsync(arg);
                return;
            case "w215_tune":    // "<corrector cm> <per-frame 0|1|->" (mp_route_correct_m; A.0's per-frame mode)
                _ = Wo215TuneAsync(arg);
                return;
        }
    }

    // ---------------------------------------------------------------- D.1/E: ghosts (the joiner)
    private readonly ConcurrentDictionary<string, long> _w215GhostToldMs = new(StringComparer.Ordinal);
    private long _w215GhostLines, _w215GhostTold, _w215DormantAsks;

    /// <summary>The engine could not set up a copy's combat actor: the mod unbinds and parks it (never left standing), E reads the set.</summary>
    private void Wo215OnEngineLine(string line)
    {
        if (Wo215Rules.GhostActor(line) is not { } name) return;
        Interlocked.Increment(ref _w215GhostLines);
        if (!W137Joiner || name.StartsWith("kcd2mp_", StringComparison.Ordinal)) return;
        long now = Environment.TickCount64;
        if (_w215GhostToldMs.TryGetValue(name, out long at) && now - at < Wo215Rules.GhostTellEveryMs) return;
        _w215GhostToldMs[name] = now;
        long k = Interlocked.Increment(ref _w215GhostTold);
        if (k <= 50 || k % 50 == 0) Console.WriteLine($"MP-W215 ghost {name}: the engine could not set up its combat actor -> the mod unbinds and parks it [n={k}]");
        _ = ExecLuaAsync($"if KCD2MP_W215Ghost then KCD2MP_W215Ghost(\"{name}\") end");
    }

    /// <summary>A.0's probe on one body: the request the DLL builds the engine's own way (op 152), its answer logged.</summary>
    private async Task Wo215ProbeAsync(string? arg)
    {
        if (!Wo215Rules.TryParseProbe(arg, out var p)) { Console.WriteLine($"MP-W215 probe refused (bad arguments): '{arg}'"); return; }
        var nb = Encoding.UTF8.GetBytes(p.Name);
        var body = new byte[2 + nb.Length + 16 + 1 + 2];
        body[0] = p.Mode; body[1] = (byte)nb.Length;
        nb.CopyTo(body, 2);
        int o = 2 + nb.Length;
        BitConverter.GetBytes(p.X).CopyTo(body, o); BitConverter.GetBytes(p.Y).CopyTo(body, o + 4);
        BitConverter.GetBytes(p.Z).CopyTo(body, o + 8); BitConverter.GetBytes(p.Yaw).CopyTo(body, o + 12);
        body[o + 16] = p.Gait;
        BitConverter.GetBytes(p.Secs).CopyTo(body, o + 17);
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo215OpProbe, body);
            Interlocked.Increment(ref _w215Probes);
            if (r is { Ok: true } rr && rr.Payload.Length >= 5)
            {
                bool ok = rr.Payload[0] == 1;
                uint id = BitConverter.ToUInt32(rr.Payload, 1);
                string why = Encoding.ASCII.GetString(rr.Payload, 5, rr.Payload.Length - 5);
                if (!ok) Interlocked.Increment(ref _w215ProbeRefused);
                Console.WriteLine(FormattableString.Invariant($"MP-W215 probe mode={p.Mode} npc={p.Name} gait={p.Gait} -> ok={(ok ? 1 : 0)} id={id} ({why})"));
            }
            else Console.WriteLine($"MP-W215 probe mode={p.Mode} npc={p.Name}: the DLL did not answer op 152");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W215 probe not sent: {ex.Message}"); }
    }

    private async Task Wo215TuneAsync(string? arg)
    {
        if (!Wo215Rules.TryParseTune(arg, out byte[] body)) { Console.WriteLine($"MP-W215 tune refused: '{arg}'"); return; }
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo215OpTune, body);
            if (r is { Ok: true } r13 && r13.Payload.Length >= 13)
                Console.WriteLine(FormattableString.Invariant($"MP-W215 tune: the corrector at {BitConverter.ToUInt16(r13.Payload, 0) / 100.0:F2} m, per-frame orders {(r13.Payload[2] == 1 ? "on" : "off")}, cadence min {BitConverter.ToUInt16(r13.Payload, 3)} ms step {BitConverter.ToUInt16(r13.Payload, 5)} cm max {BitConverter.ToUInt16(r13.Payload, 7)} ms lead {BitConverter.ToUInt16(r13.Payload, 9)} ms approach {BitConverter.ToUInt16(r13.Payload, 11)} cm"));
            else if (r is { Ok: true } rr && rr.Payload.Length >= 11)
                Console.WriteLine(FormattableString.Invariant($"MP-W215 tune: the corrector at {BitConverter.ToUInt16(rr.Payload, 0) / 100.0:F2} m, per-frame orders {(rr.Payload[2] == 1 ? "on" : "off")}, cadence min {BitConverter.ToUInt16(rr.Payload, 3)} ms step {BitConverter.ToUInt16(rr.Payload, 5)} cm max {BitConverter.ToUInt16(rr.Payload, 7)} ms lead {BitConverter.ToUInt16(rr.Payload, 9)} ms"));
            else if (r is { Ok: true } r3 && r3.Payload.Length >= 3)
                Console.WriteLine($"MP-W215 tune: the corrector at {BitConverter.ToUInt16(r3.Payload, 0) / 100.0:F2} m (an older DLL: no cadence)");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W215 tune not sent: {ex.Message}"); }
    }
}
