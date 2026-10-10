// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-166 (docs/WO-166-findings.md): 0.48.2 -- the bridge's halves. The pure rules are Wo166Rules (Wo166.cs); the game-side halves
// are kdcmp.lua's WO-166 section; the native half wo166.cpp.
//
//   L1 the loot screen's close: the engine's "PlayAudio: ui_inv_screen_out..." line, relayed to the Lua (a second signal beside the
//      game's own ItemTransfer OnClosed event)
//   T3 a talk that never started: the engine's dialogue state for that copy (its last "Dialog interrupted/ending ... state: X", or
//      none -- the request never became a dialogue), whether the dialogue controller's NPC pause request timed out, and the Lua's
//      facts about the copy (paused, in a dialogue, the stream held or driving it) -- one line, WO166-TALK timeout ...
//   T4 a talk refused because the host's NPC is busy (counted; the Lua told the player)
//
//   WO166-TALK timeout npc=<n> state=<engine state|request-only> pause_requests=<timed-out|-> waited_s=<s> placed=<host row> <lua facts>
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, (string State, double At)> _w166DlgState = new(StringComparer.Ordinal);
    private double _w166PauseReqTimeoutAt = -1e9;
    private long _w166LootCloseLines, _w166TalkTimeouts, _w166TalkBusy;

    private void Wo166OnDisconnect() { _w166DlgState.Clear(); }

    /// <summary>The engine lines LogTailGameTransport.Wo144Line routes (Wo144OnEngineLine calls this first). True = handled.</summary>
    private bool Wo166OnEngineLine(string line)
    {
        if (line.StartsWith(Wo166Rules.LootScreenOutPrefix, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _w166LootCloseLines);
            if (W137JoinerSession) _ = ExecLuaAsync("if KCD2MP_W166LootClosed then KCD2MP_W166LootClosed(\"audio\") end");
            return true;
        }
        if (line.Contains(Wo166Rules.PauseRequestsTimedOut, StringComparison.Ordinal))
        {
            _w166PauseReqTimeoutAt = W164Now();
            return false;
        }
        if (Wo166Rules.TryParseDialogState(line, out var souls, out string state))
        {
            double now = W164Now();
            foreach (var s in souls) if (s != "Dude") _w166DlgState[s] = (state, now);
        }
        return false;
    }

    private void Wo166OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w166_talkstate":   // <npc> paused=.. copy_dialog=.. dead=.. resumed_for_talk=.. stream=.. via=.. retried=..
            {
                if (f.Length < 1 || !Wo137Text.IsNpc(f[0])) return;
                string npc = f[0];
                double now = W164Now();
                _w164Talks.TryGetValue(npc, out var t);
                double asked = t?.AskedAt ?? now;
                string st = _w166DlgState.TryGetValue(npc, out var ds) && ds.At >= asked - 1 ? ds.State : "request-only";
                bool pauseTimedOut = _w166PauseReqTimeoutAt >= asked - 1;
                Interlocked.Increment(ref _w166TalkTimeouts);
                Console.WriteLine(FormattableString.Invariant(
                    $"WO166-TALK timeout npc={npc} state={st} pause_requests={(pauseTimedOut ? "timed-out" : "-")} waited_s={now - asked:F1} placed={(t?.Placed ?? "?").Replace(' ', '_')} {string.Join(' ', f.Skip(1))}"));
                return;
            }
            case "w166_cfg":         // copy_strikes=on|off snap_fix=on|off auto_mode=<n>
                Wo166OnCfg(arg);
                return;
            case "w166_talk":        // busy <npc> <why>
                if (f.Length >= 3 && f[0] == "busy" && Wo137Text.IsNpc(f[1]))
                {
                    Interlocked.Increment(ref _w166TalkBusy);
                    Console.WriteLine($"WO166-TALK busy npc={f[1]} why={f[2]} -- not asked: the host's world has this person occupied (the player was told)");
                }
                return;
        }
    }

    // ---------------------------------------------------------------- C3 / C4 / C1: the switches and the striking window

    private volatile bool _w166CopyStrikes = Wo166Rules.DefaultCopyStrikes, _w166SnapFix = Wo166Rules.DefaultSnapFix;
    private volatile byte _w166AutoMode = Wo166Rules.DefaultAutoMode;
    private long _w166Strikes, _w166StrikeRefused;
    private int _w166StrikeSaid;

    /// <summary>C3, joiner: a host row was played on this copy -- its striking window (the row's own timings and attack fields).</summary>
    private async Task Wo166StrikeAsync(string npc, ActionRowCatalog.Row row, CancellationToken ct)
    {
        if (!_w166CopyStrikes || !W137JoinerSession) return;
        var f = Wo166Rules.StrikeFor(row);
        if (f is null) { Interlocked.Increment(ref _w166StrikeRefused); return; }
        string? r;
        try { r = await _combat.Wo166StrikeAsync(npc, f.Value.StartMs, f.Value.HitMs, f.Value.Type, f.Value.Zone, f.Value.Hand, f.Value.Strength, ct); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Console.WriteLine($"WO166-STRIKE npc={npc} not sent ({ex.GetType().Name})"); return; }
        if (r is null) return;
        if (r.StartsWith("queued", StringComparison.Ordinal) || r.StartsWith("merged", StringComparison.Ordinal)) Interlocked.Increment(ref _w166Strikes);
        else Interlocked.Increment(ref _w166StrikeRefused);
        if (r.StartsWith("refused=switched-off", StringComparison.Ordinal) && Interlocked.Exchange(ref _w166StrikeSaid, 1) == 0)
        {
            Console.WriteLine($"WO166-STRIKE switched off by a fault ({r}) -- told on screen once");
            await ExecLuaAsync("if KCD2MP_W165Say then KCD2MP_W165Say(\"Enemies' strikes on your screen were switched off by a fault -- the host decides as before.\") end");
        }
    }

    /// <summary>Every 10 s (the WO-164 tick): the DLL's WO-166 switches (cheap; survives a DLL re-injection or a reconnect).</summary>
    private async Task Wo166PushConfigAsync()
    {
        try
        {
            var r = await _combat.Wo166ConfigAsync(_w166CopyStrikes, _w166AutoMode, (byte)(_w166SnapFix ? 1 : 0));
            if (r is { } c && (c.CopyStrikes != _w166CopyStrikes || c.AutoMode != _w166AutoMode || c.SnapFix != _w166SnapFix))
                Console.WriteLine($"MP-WO166 the DLL holds copy_strikes={(c.CopyStrikes ? "on" : "off")} auto_mode={c.AutoMode} snap_fix={(c.SnapFix ? "on" : "off")} (asked {(_w166CopyStrikes ? "on" : "off")}/{_w166AutoMode}/{(_w166SnapFix ? "on" : "off")})");
        }
        catch (Exception ex) { Console.WriteLine($"MP-WO166 config not pushed ({ex.GetType().Name})"); }
    }

    private void Wo166OnCfg(string? arg)
    {
        foreach (var (k, v) in Wo166Rules.ParseCfg(arg))
        {
            switch (k)
            {
                case "copy_strikes": _w166CopyStrikes = v == "on"; break;
                case "snap_fix": _w166SnapFix = v == "on"; break;
                case "auto_mode": if (byte.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out byte m) && m <= Wo166Rules.MaxAutoMode) _w166AutoMode = m; break;
            }
        }
        Console.WriteLine($"MP-WO166 cfg copy_strikes={(_w166CopyStrikes ? "on" : "off")} snap_fix={(_w166SnapFix ? "on" : "off")} auto_mode={_w166AutoMode}");
        _ = Wo166PushConfigAsync();
    }

    private string Wo166StatsText() =>
        $"loot_close_lines={Interlocked.Read(ref _w166LootCloseLines)} talk_timeouts={Interlocked.Read(ref _w166TalkTimeouts)} talk_busy={Interlocked.Read(ref _w166TalkBusy)} strikes={Interlocked.Read(ref _w166Strikes)} strike_refused={Interlocked.Read(ref _w166StrikeRefused)}";
}
