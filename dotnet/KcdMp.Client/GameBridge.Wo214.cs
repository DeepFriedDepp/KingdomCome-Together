// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-214 (docs/WO-214-findings.md): after 0.64.0 in the field. Every switch ships on (W214Switch; `mp_w214 <name> on|off` in the game's
// console); the bench's twin is KCDMP_TEST_W214_OFF=<names|all> in both agents' and both games' environment.
//   A.0/G  MP-LINK npc-moving: the band rates of the bodies that walked in the window (the band line's median is mostly idle bodies)
//   B      one_bind: a silent stream in the host's range releases its puppet after 12 s, not 3 s (the DLL holds the body meanwhile)
//   D.1    death_row: a death row the copy did not play is never replaced by a plain hit -- the copy's own weapon's death row, else the fall
//   D.3    death_ground: every death applied here ends on the ground (WO214-DEATH; still-upright must read 0)
//   E      draw_event: the host NPC's weapon in its hand is its copy's at once, in a fight or not, held while the host's hand holds it
// Log tags: MP-W214, MP-W214-NATIVE, MP-W214-STATS, WO214-DEATHROW, WO214-DEATH, WO214-DRAW, MP-LINK npc-moving.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w214On = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w214HeldOnce = new(StringComparer.Ordinal);   // B: a silence held past the old 3 s (once each)
    private readonly ConcurrentDictionary<string, long> _w214DeathRowPlayedMs = new(StringComparer.Ordinal);   // D.3: a death row played on this copy
    private bool W214On(string name) => _w214On.TryGetValue(name, out bool v) ? v : !W214Switch.TwinOff(name);
    private bool _w214NativeToldOld;
    private long _w214SilenceHeld, _w214DeathRowRefused, _w214DeathRowStandIn, _w214DeathRowFall, _w214DeathDown, _w214StillUpright,
                 _w214Draws, _w214Holsters;

    private async Task Wo214PushNativeAsync()
    {
        try
        {
            var body = BitConverter.GetBytes(W214Switch.NativeBits(W214On));
            var r = await _combat.Wo163Async(Protocol.Wo214OpConfig, body);
            if (r is not { Ok: true } && !_w214NativeToldOld) { _w214NativeToldOld = true; Console.WriteLine("MP-W214 the DLL did not take op 148 (an older KCDMP.dll?)"); }
        }
        catch (Exception ex) { Console.WriteLine($"MP-W214 the native word was not sent: {ex.Message}"); }
    }

    private async Task Wo214NativeStatusAsync()
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo214OpStatus, []);
            if (r is { Ok: true } rr) Console.WriteLine($"MP-W214-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
        }
        catch { }
    }

    /// <summary>The mod's own switches (anim_hold, one_bind's Lua half, draw_event's hold): told at every connect and every change.</summary>
    private Task Wo214TellModAsync() =>
        ExecLuaAsync($"if KCD2MP_W214Cfg then KCD2MP_W214Cfg(\"{string.Join(',', W214Switch.Names.Select(n => n + "=" + (W214On(n) ? "on" : "off")))}\") end");

    private string Wo214StatsLine() =>
        $"MP-W214-STATS {string.Join(' ', W214Switch.Names.Select(n => $"{n}={(W214On(n) ? "on" : "off")}"))} silence_held={Interlocked.Read(ref _w214SilenceHeld)}"
        + $" deathrow_refused={Interlocked.Read(ref _w214DeathRowRefused)} deathrow_standin={Interlocked.Read(ref _w214DeathRowStandIn)} deathrow_fall={Interlocked.Read(ref _w214DeathRowFall)}"
        + $" deaths_down={Interlocked.Read(ref _w214DeathDown)} still_upright={Interlocked.Read(ref _w214StillUpright)}"
        + $" draws={Interlocked.Read(ref _w214Draws)} holsters={Interlocked.Read(ref _w214Holsters)}"
        + $" silent_in_range={Interlocked.Read(ref _w214SilentInRange)} silent_beyond={Interlocked.Read(ref _w214SilentOut)} silent_unknown={Interlocked.Read(ref _w214SilentUnknown)}"
        + Wo214HandsStats();

    /// <summary>The 4 Hz session tick (beside WO-213's): the native word every 2 s, its status and the stats every 30 s.</summary>
    private void Wo214Tick(long n)
    {
        if (n % 8 == 6) _ = Wo214PushNativeAsync();
        if (n % 120 == 17 && (W137Host || W137Joiner)) { _ = Wo214NativeStatusAsync(); Console.WriteLine(Wo214StatsLine()); }
        if (n % 240 == 21) _ = Wo214TellModAsync();   // a reloaded mod takes them again
    }

    private void Wo214OnConnect() => _ = Wo214TellModAsync();

    private void Wo214OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w214_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off") || !W214Switch.Names.Contains(arg[..eq])) { Console.WriteLine($"MP-W214 cfg refused: '{arg}'"); return; }
                _w214On[arg[..eq]] = arg[(eq + 1)..] == "on";
                Console.WriteLine($"MP-W214 cfg {arg}");
                _ = Wo214PushNativeAsync();
                _ = Wo214TellModAsync();
                return;
            }
            case "w214_status":
                _ = Wo214NativeStatusAsync();
                Console.WriteLine(Wo214StatsLine());
                return;
            case "w214_hand":          // F: this player's own hand, to the partner(s)
                Wo214OnOwnHand(arg);
                return;
            case "w214_figure_hand":   // F: the mod's verdict on a partner's figure
                Wo214OnFigureHandResult(arg);
                return;
        }
    }

    // ---------------------------------------------------------------- B: the silence release, by the body's range

    /// <summary>The nearer of this player and the host's figure to the host's last sample of <paramref name="npc"/>; -1 when unknown.</summary>
    private float Wo214NearestPlayerM(string npc)
    {
        if (!_w187Stream.TryGetValue(npc, out var s)) return -1f;
        float d = float.MaxValue;
        if (_lastX != 0 || _lastY != 0) d = Wo187Rules.Dist2D(s.X, s.Y, _lastX, _lastY);
        foreach (var g in _ghostLastPos.Values) d = Math.Min(d, Wo187Rules.Dist2D(s.X, s.Y, g.X, g.Y));
        return d == float.MaxValue ? -1f : d;
    }

    private long _w214SilentInRange, _w214SilentOut, _w214SilentUnknown;

    /// <summary>B: one stream told silent to the mod (its puppet is released) -- in the host's range or beyond it, with its distance.</summary>
    private void Wo214NoteSilent(string npc, long silentMs, int releaseMs)
    {
        float d = Wo214NearestPlayerM(npc);
        long k = d < 0 ? Interlocked.Increment(ref _w214SilentUnknown)
               : d <= Wo214Rules.InRangeM ? Interlocked.Increment(ref _w214SilentInRange) : Interlocked.Increment(ref _w214SilentOut);
        if (k <= 100 || k % 50 == 0)
            Console.WriteLine(FormattableString.Invariant($"WO214-SILENT npc={npc} dmin_m={(d < 0 ? -1 : d):F0} silent_ms={silentMs} release_ms={releaseMs} in_range={(d >= 0 && d <= Wo214Rules.InRangeM ? 1 : 0)} [n={k}]"));
    }

    /// <summary>B (one_bind): the silence after which the mod is told this NPC's stream is silent.</summary>
    private int Wo214SilenceReleaseMs(string npc)
    {
        int ms = Wo214Rules.SilenceReleaseMs(W214On("one_bind"), Wo214NearestPlayerM(npc));
        return ms;
    }

    // ---------------------------------------------------------------- D.1: a death row the copy did not play

    /// <summary>
    /// Joiner: the host NPC's death row did not play on its copy. Never a plain hit instead (0.64.0's WO-187 fallback: the body then died
    /// standing) -- the copy's own weapon's death row (the engine's own death reaction, if this copy takes one); none played: the fall is
    /// owed at the soul's death (D.3 lays it down within a second).
    /// </summary>
    private async Task<PipeResult?> Wo214DeathRowAsync(string npc, uint eid, Guid failedGuid, ActionRowCatalog.Row failed, string why, CancellationToken ct)
    {
        Interlocked.Increment(ref _w214DeathRowRefused);
        ActionRowCatalog? cat = _rowCatalog.IsCompletedSuccessfully ? _rowCatalog.Result : null;
        PipeResult? r = null;
        string standIn = "none";
        if (cat is not null)
        {
            ActionRowCatalog.Row? body = (_w165LastRow.TryGetValue(npc, out var lg) || _w187FigureRow.TryGetValue(npc, out lg)) && cat.TryGet(lg, out var sr) ? sr : null;
            var (hands, handTags) = await Wo190HandsAsync(eid);
            if (Wo190Rules.BodyFromHands(hands, handTags, body?.ActorClass ?? 0) is { } held) body = held;
            if (cat.DeathFallbackRow(failedGuid, failed, body) is { } fb)
            {
                r = await _combat.GhostSwingForResultAsync(eid, fb.Row.Spec, ct);
                standIn = $"{fb.Guid} spec=\"{fb.Row.Spec}\" result={r.Value.ReasonTag}";
                if (r.Value.Ok) { Interlocked.Increment(ref _w214DeathRowStandIn); _w214DeathRowPlayedMs[npc] = Environment.TickCount64; }
            }
        }
        if (r is not { Ok: true }) Interlocked.Increment(ref _w214DeathRowFall);
        Console.WriteLine($"WO214-DEATHROW npc={npc} row={failedGuid} why={why} standin={standIn} -> {(r is { Ok: true } ? "the copy's own death row plays" : "no death row here: the fall at the soul's death")}");
        return r;
    }

    // ---------------------------------------------------------------- A.0 / G: the walking bodies' rates

    // per body, this window: its last sample (position, arrival), and while it walked: the samples that moved and the time they spanned
    private readonly ConcurrentDictionary<string, (float X, float Y, long Ts, int Moved, double MovedS)> _w214NpcMove = new(StringComparer.Ordinal);

    /// <summary>Beside WO-213's band count: the rate of a body's samples WHILE IT WALKS -- a sample that moved it at least 2 cm counts,
    /// with the time since the body's sample before it (capped at 1 s: a longer gap is a silence, counted once as 1 s).</summary>
    private void Wo214NoteBandSample(string name, float x, float y)
    {
        long ts = Stopwatch.GetTimestamp();
        _w214NpcMove.AddOrUpdate(name, (x, y, ts, 0, 0.0), (_, v) =>
        {
            double dt = Math.Min(1.0, (ts - v.Ts) / (double)Stopwatch.Frequency);
            bool moved = Wo187Rules.Dist2D(x, y, v.X, v.Y) >= Wo214Rules.MovingStepM;
            return (x, y, ts, v.Moved + (moved ? 1 : 0), v.MovedS + (moved ? dt : 0.0));
        });
    }

    /// <summary>MP-LINK npc-moving: per band, the rate of the bodies that walked at least 2 s of the window, over the time they walked (the
    /// snapshot WO-213's line took).</summary>
    private string Wo214MovingLine(string who, double windowS, KeyValuePair<string, (int N, int Band)>[] snap)
    {
        var moved = _w214NpcMove.ToArray().ToDictionary(kv => kv.Key, kv => (kv.Value.Moved, kv.Value.MovedS), StringComparer.Ordinal);
        foreach (var k in moved.Keys) _w214NpcMove.AddOrUpdate(k, _ => default, (_, v) => (v.X, v.Y, v.Ts, 0, 0.0));   // the next window starts at the last sample
        var sb = new StringBuilder($"MP-LINK npc-moving who={who} window_s={windowS.ToString("F1", CultureInfo.InvariantCulture)}");
        for (int b = 0; b < W213BandNames.Length; b++)
        {
            var hz = snap.Where(kv => kv.Value.Band == b && moved.TryGetValue(kv.Key, out var m) && m.MovedS >= Wo214Rules.MovingWindowS)
                         .Select(kv => moved[kv.Key].Moved / moved[kv.Key].MovedS).ToList();
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $" {W213BandNames[b]}={hz.Count}:p50={(hz.Count > 0 ? ArrivalStats.Quantile(hz, 0.5) : 0):F1}Hz:p10={(hz.Count > 0 ? ArrivalStats.Quantile(hz, 0.1) : 0):F1}Hz"));
        }
        return sb.ToString();
    }
}
