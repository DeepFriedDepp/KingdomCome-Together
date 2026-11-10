// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-218 (docs/WO-218-findings.md): the release candidate. Every switch ships on (W218Switch; `mp_<name> on|off` in the game's console);
// the bench's twin is KCDMP_TEST_W218_OFF=<names|all> in both agents' and both games' environment.
// Log tags: MP-W218, MP-W218-NATIVE, MP-W218-STATS, MP-PIPE (the batch and the queue's pressure, every 10 s); the DLL's WO218-PIPE, WO218-CFG.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w218On = new(StringComparer.Ordinal);
    private bool W218On(string name) => _w218On.TryGetValue(name, out bool v) ? v : !W218Switch.TwinOff(name);
    private int _w218Probing;

    /// <summary>0 none, 1 the host, 2 a joiner in the host's world and not in a load.</summary>
    private byte Wo218Role() => W137Host ? (byte)1 : (W137Joiner && !Wo136Holding ? (byte)2 : (byte)0);

    /// <summary>The native word, and the probe that this DLL serves batches (op 180's caps; an older DLL does not know the op).</summary>
    private async Task Wo218PushNativeAsync()
    {
        if (Interlocked.CompareExchange(ref _w218Probing, 1, 0) != 0) return;
        try
        {
            _combat.BatchOn = W218On("pipe_batch");
            var body = new byte[5];
            BitConverter.GetBytes(W218Switch.NativeBits(W218On)).CopyTo(body, 0);
            body[4] = Wo218Role();
            var r = await _combat.Wo163Async(Protocol.Wo218OpConfig, body);
            if (r is { Ok: true } rr) _combat.SetBatchSupported(rr.Payload.Length >= 5 && (rr.Payload[4] & 1) != 0);
            else if (r is not null) _combat.SetBatchSupported(false);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W218 the native word was not sent: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _w218Probing, 0); }
    }

    private async Task Wo218NativeStatusAsync()
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo218OpStatus, []);
            if (r is { Ok: true } rr) Console.WriteLine($"MP-W218-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
        }
        catch { }
    }

    private Task Wo218TellModAsync() =>
        ExecLuaAsync($"if KCD2MP_W218Cfg then KCD2MP_W218Cfg(\"{string.Join(',', W218Switch.Names.Select(n => n + "=" + (W218On(n) ? "on" : "off")))}\") end");

    private string Wo218StatsLine() =>
        $"MP-W218-STATS {string.Join(' ', W218Switch.Names.Select(n => $"{n}={(W218On(n) ? "on" : "off")}"))} batch_active={(_combat.BatchActive ? 1 : 0)}" + Wo218MoreStats();

    /// <summary>The 4 Hz session tick: the native word every 2 s (and every half second while this connection is not probed), MP-PIPE every
    /// 10 s, the status and the stats every 30 s, the mod told every minute.</summary>
    private void Wo218Tick(long n)
    {
        if (n % 8 == 6 || (_combat.IsConnected && !_combat.BatchProbed && n % 2 == 0)) _ = Wo218PushNativeAsync();
        if (n % 40 == 17 && _combat.IsConnected) Console.WriteLine("MP-PIPE " + _combat.TakeBatchLine() + Wo218ShedLine());
        if (n % 120 == 29 && (W137Host || W137Joiner)) { _ = Wo218NativeStatusAsync(); Console.WriteLine(Wo218StatsLine()); }
        if (n % 240 == 37) _ = Wo218TellModAsync();
        Wo218TickMore(n);
    }

    private void Wo218OnConnect(CancellationToken ct) { _ = Wo218TellModAsync(); _ = Wo218PushNativeAsync(); }

    private void Wo218OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w218_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off") || !W218Switch.Names.Contains(arg[..eq])) { Console.WriteLine($"MP-W218 cfg refused: '{arg}'"); return; }
                _w218On[arg[..eq]] = arg[(eq + 1)..] == "on";
                Console.WriteLine($"MP-W218 cfg {arg}");
                _ = Wo218PushNativeAsync();
                _ = Wo218TellModAsync();
                return;
            }
            case "w218_status":
                _ = Wo218NativeStatusAsync();
                Console.WriteLine(Wo218StatsLine());
                Console.WriteLine("MP-PIPE " + _combat.TakeBatchLine() + Wo218ShedLine());
                return;
            default:
                Wo218OnEventMore(name, arg);
                return;
        }
    }
}
