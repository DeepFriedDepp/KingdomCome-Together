// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-154 Phase 5: riding, smooth on both screens (docs/WO-154-findings.md).
//
// The field: a ridden horse was the one body still on the legacy Lua path -- the DLL unbound the riding avatar
// and Lua moved the horse to a new point every ~31 ms (every second frame at 58-65 fps), its own brain ran too,
// and a forced gallop clip fought the rider's idle (the engine's "segments do not have the same duration",
// 24-41 a second). Now, once the avatar is mounted, the horse is a puppet like any other: its brain held, the
// DLL writes it every frame from the RIDER'S stream (fed here under the horse's name, the saddle height taken
// off), and the DLL's gait gives it the engine's own locomotion. mp_ride_native on|off (default on).
// mp_gait_hysteresis on|off (default on): the gait class changes only past a band (the trot on a boundary).
public partial class GameBridge
{
    private volatile bool _w154RideNative = true, _w154GaitHyst = true;
    // Phase 6.2: mp_bind_far -- the native writer binds a body with no physics yet (far: the field's 287 "not-living"
    // refusals, median 635 m away) and a body seated on a cart (held in its seat) instead of leaving them to Lua.
    private volatile bool _w154BindFar = true;
    private readonly ConcurrentDictionary<byte, (string Horse, float Dz)> _w154RideFeed = new();
    private long _w154RideFeeds, _w154RideSamples;

    /// <summary>w154_ride &lt;ghost&gt; &lt;horse&gt; &lt;dz&gt; | &lt;ghost&gt; off: the mod bound (or let go of) a ridden horse.</summary>
    private void Wo154OnRide(string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 2 || !byte.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte ghost)) return;
        if (f[1] == "off")
        {
            if (_w154RideFeed.TryRemove(ghost, out var was))
                Console.WriteLine($"MP-W154 ride: ghost {ghost} off {was.Horse} -- its horse is no longer fed from the rider's stream");
            return;
        }
        if (f.Length < 3 || !NpcNamePattern.IsMatch(f[1]) || f[1].Length > NativeNpcCodec.MaxNameLen
            || !float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float dz) || !float.IsFinite(dz) || dz < 0 || dz > 3) return;
        _w154RideFeed[ghost] = (f[1], dz);
        Interlocked.Increment(ref _w154RideFeeds);
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W154 ride: ghost {ghost} rides {f[1]} -- the DLL writes the horse every frame from the rider's stream (saddle {dz:F2} m taken off)"));
    }

    /// <summary>Every inbound ghost position: a ridden horse gets the rider's sample under its own name.</summary>
    private void Wo154FeedRiddenHorse(in GhostSample gs, ushort seq, long arrival)
    {
        if (!_w154RideNative || !gs.IsRiding || !_w154RideFeed.TryGetValue(gs.GhostId, out var rf)) return;
        _nativeFeed.Enqueue(new NativeNpcSample(gs.GhostId, rf.Horse, gs.X, gs.Y, gs.Z - rf.Dz, gs.RotZ, 0, seq, gs.SenderMs, arrival));
        Interlocked.Increment(ref _w154RideSamples);
    }

    private string Wo154RideStatsText() => FormattableString.Invariant(
        $"ride_native={(_w154RideNative ? "on" : "off")} gait_hyst={(_w154GaitHyst ? "on" : "off")} rides_fed={_w154RideFeeds} ride_samples={_w154RideSamples} riding_now={_w154RideFeed.Count}");
}
