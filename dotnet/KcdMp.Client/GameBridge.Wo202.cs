// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;

namespace KcdMp.Client;

// WO-202 (docs/WO-202-findings.md): the bridge's halves. The pure rules are Wo202Rules (Wo202.cs).
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w202On = new(StringComparer.Ordinal);
    private bool W202(string name) => _w202On.TryGetValue(name, out bool v) ? v : Wo202Rules.DefaultOn(name);
    private long _w202JoinEarlyReleases, _w202JoinSilenceWaits;

    private void Wo202OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w202_cfg":
                if (!Wo202Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W202 cfg refused: '{arg}'"); return; }
                _w202On[cfg] = on;
                Console.WriteLine($"MP-W202 cfg {cfg}={(on ? "on" : "off")}");
                Wo202OnCfg(cfg, on);
                return;
            case "w202_status":
                Wo202Status();
                return;
            case "w202_key":
            case "w202_chest":
                _ = Wo202KeysOnEventAsync(name, arg);   // V.3
                return;
        }
    }

    private void Wo202OnCfg(string name, bool on)
    {
        if (name is "crash_capture" or "near_safe" or "quest_fail_trace" or "anim_shown" or "recoil_clinch" or "fight_pos_fresh" or "death_engine_only")
            _ = Wo202PushConfigAsync();
    }

    private void Wo202Status()
    {
        string sw = string.Join(" ", Wo202Rules.SwitchNames.Select(n => $"{n}={(W202(n) ? "on" : "off")}"));
        Console.WriteLine($"MP-W202-STATS {sw} join_early_releases={_w202JoinEarlyReleases} join_silence_waits={_w202JoinSilenceWaits} quest_fails={_w202FailsLogged} " +
                          $"death_checks={_w202DeathChecks} death_falls={_w202DeathFalls} death_fall_failed={_w202DeathFallFailed} death_lying={_w202DeathLying} death_announced={_w202DeathAnnounced} death_refused={_w202DeathRefused} " + Wo202ClockStats() + " " + Wo202KeysStats());
    }

    private volatile bool _w202Connected;

    private void Wo202OnConnect(CancellationToken ct)
    {
        _w202Connected = true;
        _ = Wo202PushConfigAsync();
        _ = Wo202LoopAsync(ct);
        _ = Wo202OpponentLoopAsync(ct);   // IV.3
    }

    private void Wo202OnDisconnect() { _w202Connected = false; }

    /// <summary>The DLL's WO-202 switches (op 88): every change and every connect.</summary>
    private async Task Wo202PushConfigAsync()
    {
        try { await _combat.Wo163Async(KcdMp.Wire.Protocol.Wo202OpConfig, BitConverter.GetBytes(Wo202Rules.ConfigBits(W202))); }
        catch (Exception ex) { Console.WriteLine($"MP-W202 config not pushed ({ex.GetType().Name})"); }
    }
}
