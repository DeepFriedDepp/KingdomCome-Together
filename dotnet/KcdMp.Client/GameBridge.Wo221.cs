// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-221 (docs/WO-221-findings.md): bodies that walk right, and a horse each. Every switch ships on (W221Switch; `mp_<name> on|off` in
// the game's console, `mp_w221_status` lists them); the bench's twin is KCDMP_TEST_W221_OFF=<names|all> in both agents' and both games'
// environment. Log tags: MP-W221, MP-W221-NATIVE, MP-W221-STATS; the DLL's WO221-CFG, WO221-ACTEND.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w221On = new(StringComparer.Ordinal);
    private bool W221On(string name) => _w221On.TryGetValue(name, out bool v) ? v : !W221Switch.TwinOff(name);
    private int _w221Pushing;

    private async Task Wo221PushNativeAsync()
    {
        if (Interlocked.CompareExchange(ref _w221Pushing, 1, 0) != 0) return;
        try
        {
            var body = new byte[5];
            BitConverter.GetBytes(W221Switch.NativeBits(W221On)).CopyTo(body, 0);
            body[4] = Wo218Role();
            await _combat.Wo163Async(Protocol.Wo221OpConfig, body);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W221 the native word was not sent: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _w221Pushing, 0); }
    }

    private async Task Wo221NativeStatusAsync()
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo221OpStatus, []);
            if (r is { Ok: true } rr) Console.WriteLine($"MP-W221-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
        }
        catch { }
    }

    private Task Wo221TellModAsync() =>
        ExecLuaAsync($"if KCD2MP_W221Cfg then KCD2MP_W221Cfg(\"{string.Join(',', W221Switch.Names.Select(n => n + "=" + (W221On(n) ? "on" : "off")))}\") end");

    private string Wo221StatsLine() =>
        $"MP-W221-STATS {string.Join(' ', W221Switch.Names.Select(n => $"{n}={(W221On(n) ? "on" : "off")}"))}";

    /// <summary>The 4 Hz session tick: the native word every 2 s, the status and the stats every 30 s, the mod told every minute.</summary>
    private void Wo221Tick(long n)
    {
        if (n % 8 == 3) _ = Wo221PushNativeAsync();
        if (n % 120 == 41 && (W137Host || W137Joiner)) { _ = Wo221NativeStatusAsync(); Console.WriteLine(Wo221StatsLine()); }
        if (n % 240 == 53) _ = Wo221TellModAsync();
    }

    private void Wo221OnConnect(CancellationToken ct) { _ = Wo221TellModAsync(); _ = Wo221PushNativeAsync(); }

    private void Wo221OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w221_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off") || !W221Switch.Names.Contains(arg[..eq])) { Console.WriteLine($"MP-W221 cfg refused: '{arg}'"); return; }
                _w221On[arg[..eq]] = arg[(eq + 1)..] == "on";
                Console.WriteLine($"MP-W221 cfg {arg}");
                _ = Wo221PushNativeAsync();
                _ = Wo221TellModAsync();
                return;
            }
            case "w221_status":
                _ = Wo221NativeStatusAsync();
                Console.WriteLine(Wo221StatsLine());
                return;
        }
    }
}
