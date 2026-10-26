// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-208 (docs/WO-208-findings.md): the bridge's shared half -- the switches (Lua w208_cfg, mp_w208_status) and the DLL's switch word.
// The pure rules are Wo208Rules (Wo208.cs); the native half wo208.cpp.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w208On = new(StringComparer.Ordinal);

    /// <summary>A WO-208 switch: on unless the mod said off (a test twin's KCDMP_TEST_W208_OFF=name,name turns it off from the start).</summary>
    private bool W208(string name) =>
        _w208On.TryGetValue(name, out bool v) ? v
            : Wo208Rules.Default(name, Environment.GetEnvironmentVariable("KCDMP_TEST_W208_OFF"), Environment.GetEnvironmentVariable("KCDMP_TEST_W208_ON"));

    private async Task Wo208PushConfigAsync()
    {
        try { await _combat.Wo163Async(Protocol.Wo208OpConfig, BitConverter.GetBytes(Wo208Rules.ConfigBits(W208))); }
        catch (Exception ex) { Console.WriteLine($"MP-W208 config not sent to the DLL: {ex.Message}"); }
    }

    private async Task Wo208NativeStatusAsync()
    {
        var r = await _combat.Wo163Async(Protocol.Wo208OpStatus, []);
        if (r is { Ok: true } rr) Console.WriteLine($"MP-W208-NATIVE {System.Text.Encoding.ASCII.GetString(rr.Payload)}");
    }

    /// <summary>The 4 Hz session tick (Wo201Tick).</summary>
    private void Wo208Tick(long n)
    {
        if (n % 8 == 3) _ = Wo208PushConfigAsync();   // every 2 s: a DLL that came back (a rejoin, a new game) has the switches
        // A: every copy is the owner's (the bench's A.1 restricts the DLL's owner to one copy: KCDMP_TEST_W208_OWNER_ONLY, then not all)
        bool all = W208("copy_anim_owner") && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KCDMP_TEST_W208_OWNER_ONLY"));
        if (n % 8 == 3 && W137Joiner) _ = ExecLuaAsync($"if KCD2MP_W208SetOwnerAll then KCD2MP_W208SetOwnerAll({(all ? "true" : "false")}) end");
        if (n % 4 == 1) Wo208DefenceTick();             // B: the host's outcomes that answered no blow of its own
    }

    private long _w208ClockHeld, _w208ClockHeldTold;

    /// <summary>D.2: a host clock message that found this clock standing with the host's (told once per stand).</summary>
    private void Wo208NoteClockHeld()
    {
        long n = Interlocked.Increment(ref _w208ClockHeld);
        if (Interlocked.Exchange(ref _w208ClockHeldTold, 1) == 0)
            Console.WriteLine($"WO208-CLOCK this clock stands with the host's (its dialogue): no correction runs it on meanwhile (#{n})");
    }

    private void Wo208OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w208_cfg":
                if (!Wo208Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W208 cfg refused: '{arg}'"); return; }
                _w208On[cfg] = on;
                Console.WriteLine($"MP-W208 cfg {cfg}={(on ? "on" : "off")}");
                if (cfg == "joiner_player_triggers") _w207NpcTrigDue = true;   // D.2: the quiet list again (on adds them, off forgets them)
                _ = Wo208PushConfigAsync();
                return;
            case "w208_dialogmap":   // C.3: the game's dialog map was left on (the mod's own detector); the agent's log says it beside the engine's lines
                Console.WriteLine($"WO208-DIALOGMAP {arg} arrived with no dialogue and nobody listening -- the game's dialog map was left on");
                return;
            case "w208_status":
                _ = Wo208NativeStatusAsync();
                Console.WriteLine($"MP-W208-STATS {string.Join(' ', Wo208Rules.SwitchNames.Select(n => $"{n}={(W208(n) ? "on" : "off")}"))} | {Wo208StreamStats()}");
                return;
        }
    }
}
