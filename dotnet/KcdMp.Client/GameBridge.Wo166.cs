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
        if (line.StartsWith(Wo166Rules.WeatherProfilePrefix, StringComparison.Ordinal) || line.StartsWith(Wo166Rules.WeatherProfileOffPrefix, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _w166WeatherProfileLines);
            if (W137JoinerSession) Interlocked.Increment(ref _w166WeatherProfileLinesInSession);
            if (W137Host && LivePartners().Count > 0) _ = ExecLuaAsync("if KCD2MP_W166WeatherTick then KCD2MP_W166WeatherTick(true, true, false) end");
            return true;
        }
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
            case "w166_wx":          // W1 (host): <rain> -- its game's rain intensity
                _ = Wo166OnLocalWeatherAsync(arg);
                return;
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

    // ---------------------------------------------------------------- W1: the host's live weather

    private long _w166WeatherProfileLines, _w166WeatherProfileLinesInSession, _w166WeatherSent, _w166WeatherApplied;
    private float _w166WxLastSent = -1f;
    private long _w166WxLastSentMs;

    /// <summary>Host: its Lua read its game's rain ("w166_wx &lt;rain&gt;"): to every joiner, with the session's time-of-day profile.</summary>
    private async Task Wo166OnLocalWeatherAsync(string? arg)
    {
        if (!config.WeatherSyncEnabled || !W137Host) return;
        if (!float.TryParse((arg ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float rain) || !float.IsFinite(rain)) return;
        var peers = LivePartners();
        if (peers.Count == 0) return;
        string text = W164Text.Weather(rain, _sessionWeatherProfile);
        foreach (byte g in peers) await W164SendAsync(g, Protocol.W164Weather, text);
        Interlocked.Increment(ref _w166WeatherSent);
        long now = Environment.TickCount64;
        if (Math.Abs(rain - _w166WxLastSent) >= 0.05f || now - _w166WxLastSentMs >= 300_000)
        {
            _w166WxLastSent = rain; _w166WxLastSentMs = now;
            Console.WriteLine(FormattableString.Invariant($"WO166-WEATHER sent rain={rain:F2} profile={_sessionWeatherProfile ?? "-"} to={peers.Count} -- the host's own weather is the session's"));
        }
    }

    /// <summary>Joiner: the host's weather arrived -- the rain held at the host's (and its time-of-day profile through the WO-151 gate).</summary>
    private async Task Wo166OnHostWeatherAsync(float rain, string? profile)
    {
        if (!config.WeatherSyncEnabled) return;
        Interlocked.Increment(ref _w166WeatherApplied);
        await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W166WeatherApply then KCD2MP_W166WeatherApply({rain:F2}, \"{profile ?? "-"}\") end"));
        if (profile is not null)
        {
            await Wo151OnHostWeatherAsync(profile);
            await ApplyWeatherAsync(profile, WeatherBlendSeconds);
        }
    }

    // ---------------------------------------------------------------- R1: the respawn amnesty

    private readonly ConcurrentDictionary<byte, ConcurrentDictionary<string, long>> _w166Struck = new();   // host: figure -> npc -> when
    private readonly ConcurrentDictionary<byte, (float X, float Y, float Z, long AtMs)> _w166PeerDeathAt = new();
    private (float X, float Y, float Z, long AtMs) _w166LocalDownAt = (0, 0, 0, -1);
    private long _w166Amnesties;

    private void Wo166NoteStruck(byte ghost, string npc)
    {
        if (!Wo137Text.IsNpc(npc)) return;
        var m = _w166Struck.GetOrAdd(ghost, _ => new ConcurrentDictionary<string, long>(StringComparer.Ordinal));
        m[npc] = Environment.TickCount64;
        if (m.Count > 32) foreach (var k in m.OrderBy(kv => kv.Value).Take(m.Count - 32).Select(kv => kv.Key).ToList()) m.TryRemove(k, out _);
    }

    private void Wo166NotePeerDeath(byte ghost)
    {
        if (_ghostLastPos.TryGetValue(ghost, out var p)) _w166PeerDeathAt[ghost] = (p.X, p.Y, p.Z, Environment.TickCount64);
    }

    private void Wo166NoteLocalDown(float x, float y, float z) => _w166LocalDownAt = (x, y, z, Environment.TickCount64);

    /// <summary>Host: a partner woke after a death -- the NPCs that struck him, and those still fighting where he fell, stop their pursuit.</summary>
    private async Task Wo166OnPeerRespawnedAsync(byte ghost, byte reason)
    {
        if (!W137Host || reason != Protocol.RespawnReasonDeath) return;
        long now = Environment.TickCount64;
        var names = _w166Struck.TryGetValue(ghost, out var m) ? Wo166Rules.RecentStrikers(m, now) : new List<string>();
        var at = _w166PeerDeathAt.TryGetValue(ghost, out var d) && now - d.AtMs < Wo166Rules.AmnestyDeathFreshMs ? d : (0f, 0f, 0f, -1L);
        Interlocked.Increment(ref _w166Amnesties);
        Console.WriteLine($"WO166-AMNESTY host: player {ghost} woke after a death -- {names.Count} NPC(s) that struck him and every NPC still fighting where he fell get the game's own stopFight; his avatar leaves its fights; the crime record is untouched");
        await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W166Amnesty then KCD2MP_W166Amnesty(\"ghost{ghost}\", {(at.Item4 >= 0 ? $"{at.Item1:F1}, {at.Item2:F1}, {at.Item3:F1}" : "nil, nil, nil")}, \"{string.Join(' ', names)}\") end"));
        await Wo154EndFightsForPeerAsync(ghost, "respawn-amnesty");
        if (_w166Struck.TryGetValue(ghost, out var mm)) mm.Clear();
    }

    /// <summary>Either role: this player woke after a death. Host: the NPCs still fighting where he fell stop. Joiner: his copies let go.</summary>
    private async Task Wo166OnLocalRespawnedAsync(byte reason)
    {
        if (reason != Protocol.RespawnReasonDeath) return;
        long now = Environment.TickCount64;
        if (W137Host || (!W137JoinerSession && _isDamageAuthority))
        {
            var at = _w166LocalDownAt.AtMs >= 0 && now - _w166LocalDownAt.AtMs < Wo166Rules.AmnestyDeathFreshMs ? _w166LocalDownAt : (0f, 0f, 0f, -1L);
            Interlocked.Increment(ref _w166Amnesties);
            Console.WriteLine("WO166-AMNESTY this player woke after a death -- every NPC still fighting where he fell gets the game's own stopFight (the crime record is untouched)");
            await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W166Amnesty then KCD2MP_W166Amnesty(\"host\", {(at.Item4 >= 0 ? $"{at.Item1:F1}, {at.Item2:F1}, {at.Item3:F1}" : "nil, nil, nil")}, \"\") end"));
        }
        else if (W137JoinerSession)
        {
            int released = 0;
            foreach (var (name, e) in _w132Engaged.ToArray())
                if (_w132Engaged.TryRemove(name, out _)) { released++; await _combat.Wo132EngageAsync(false, e.Eid, default); }
            if (!_w147Local.IsEmpty) { released += _w147Local.Count; await Wo147ReleaseAllAsync("respawn-amnesty"); }
            Console.WriteLine($"WO166-AMNESTY joiner: this player woke after a death -- {released} engaged cop(ies) let go; the host ends the pursuits in its world");
        }
    }

    // ---------------------------------------------------------------- S1: the scan around every player

    private long _w166LastScanMs, _w166ScanFallbacks, _w166ScanLogMs;

    /// <summary>
    /// Host (the WO-138 loop, 4 Hz): the NPC scan normally runs from the host's own position loop -- which stops while the host is dead,
    /// held black, loading or failing its position read. When the last scan is older than twice its cadence and a partner has a fresh
    /// position, the scan runs here with that partner as its first anchor (every other fresh one after it).
    /// </summary>
    private async Task Wo166ScanFallbackAsync(CancellationToken ct)
    {
        if (!_npcScanNative || _npcScanGaveUp || !_isDamageAuthority) return;
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _w166LastScanMs) < 2 * (long)NpcScanInterval.TotalMilliseconds) return;
        var cutoff = DateTime.UtcNow - NpcScanGhostStaleAfter;
        foreach (var (g, p) in _ghostLastPos.ToArray())
        {
            if (g == _myGhostId || p.AtUtc < cutoff) continue;
            Interlocked.Increment(ref _w166ScanFallbacks);
            if (now - _w166ScanLogMs >= 60_000)
            {
                _w166ScanLogMs = now;
                Console.WriteLine($"WO166-SCAN fallback anchor=player{g} -- the host's own position loop is quiet; the NPCs around the partners are still streamed (n={Interlocked.Read(ref _w166ScanFallbacks)})");
            }
            await NpcScanTickAsync(p.X, p.Y, p.Z, ct);
            return;
        }
    }

    private string Wo166StatsText() =>
        $"loot_close_lines={Interlocked.Read(ref _w166LootCloseLines)} talk_timeouts={Interlocked.Read(ref _w166TalkTimeouts)} talk_busy={Interlocked.Read(ref _w166TalkBusy)} strikes={Interlocked.Read(ref _w166Strikes)} strike_refused={Interlocked.Read(ref _w166StrikeRefused)} weather_sent={Interlocked.Read(ref _w166WeatherSent)} weather_applied={Interlocked.Read(ref _w166WeatherApplied)} weather_profile_lines={Interlocked.Read(ref _w166WeatherProfileLines)} weather_profile_lines_in_session={Interlocked.Read(ref _w166WeatherProfileLinesInSession)} amnesties={Interlocked.Read(ref _w166Amnesties)} scan_fallbacks={Interlocked.Read(ref _w166ScanFallbacks)}";
}
