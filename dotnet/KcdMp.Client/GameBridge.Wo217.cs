// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-217 (docs/WO-217-findings.md): the world around the bodies. Every switch ships on (W217Switch; `mp_<name> on|off` in the game's
// console); the bench's twin is KCDMP_TEST_W217_OFF=<names|all> in both agents' and both games' environment.
// Log tags: MP-W217, MP-W217-NATIVE, MP-W217-STATS; the DLL's WO217-PLACE, WO217-SEAT, WO217-SEATPULL, WO217-STATUS.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w217On = new(StringComparer.Ordinal);
    private bool W217On(string name) => _w217On.TryGetValue(name, out bool v) ? v : !W217Switch.TwinOff(name);
    private bool _w217NativeToldOld;

    /// <summary>0 none, 1 the host, 2 a joiner in the host's world and not in a load.</summary>
    private byte Wo217Role() => W137Host ? (byte)1 : (W137Joiner && !Wo136Holding ? (byte)2 : (byte)0);

    private async Task Wo217PushNativeAsync()
    {
        try
        {
            var body = new byte[5];
            BitConverter.GetBytes(W217Switch.NativeBits(W217On)).CopyTo(body, 0);
            body[4] = Wo217Role();
            var r = await _combat.Wo163Async(Protocol.Wo217OpConfig, body);
            if (r is not { Ok: true } && !_w217NativeToldOld) { _w217NativeToldOld = true; Console.WriteLine("MP-W217 the DLL did not take op 170 (an older KCDMP.dll?)"); }
        }
        catch (Exception ex) { Console.WriteLine($"MP-W217 the native word was not sent: {ex.Message}"); }
    }

    private async Task Wo217NativeStatusAsync()
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo217OpStatus, []);
            if (r is { Ok: true } rr) Console.WriteLine($"MP-W217-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
        }
        catch { }
    }

    /// <summary>The mod's own switches: told at every connect and every change.</summary>
    private Task Wo217TellModAsync() =>
        ExecLuaAsync($"if KCD2MP_W217Cfg then KCD2MP_W217Cfg(\"{string.Join(',', W217Switch.Names.Select(n => n + "=" + (W217On(n) ? "on" : "off")))}\") end");

    private string Wo217StatsLine() =>
        $"MP-W217-STATS {string.Join(' ', W217Switch.Names.Select(n => $"{n}={(W217On(n) ? "on" : "off")}"))}" + Wo217MoreStats();

    /// <summary>The 4 Hz session tick: the native word every 2 s, its status and the stats every 30 s, the mod told every minute.</summary>
    private void Wo217Tick(long n)
    {
        if (n % 8 == 5) _ = Wo217PushNativeAsync();
        if (n % 120 == 23 && (W137Host || W137Joiner)) { _ = Wo217NativeStatusAsync(); Console.WriteLine(Wo217StatsLine()); }
        if (n % 240 == 31) _ = Wo217TellModAsync();
        Wo217TickMore(n);
    }

    private void Wo217OnConnect(CancellationToken ct) { _ = Wo217TellModAsync(); _ = Wo217PushNativeAsync(); }

    private void Wo217OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w217_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off") || !W217Switch.Names.Contains(arg[..eq])) { Console.WriteLine($"MP-W217 cfg refused: '{arg}'"); return; }
                _w217On[arg[..eq]] = arg[(eq + 1)..] == "on";
                Console.WriteLine($"MP-W217 cfg {arg}");
                _ = Wo217PushNativeAsync();
                _ = Wo217TellModAsync();
                return;
            }
            case "w217_status":
                _ = Wo217NativeStatusAsync();
                Console.WriteLine(Wo217StatsLine());
                return;
            default:
                Wo217OnEventMore(name, arg);
                return;
        }
    }
}
