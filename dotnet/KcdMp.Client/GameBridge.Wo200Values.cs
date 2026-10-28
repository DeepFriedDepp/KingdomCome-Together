// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-200 Act II: puppets take values, not guesses (docs/WO-200-findings.md).
//   II.1 copy_health_from_host: the joiner never applies a blow to a copy itself; the copy shows the host's own health (WO-131's follow)
//        and stamina (the host's Vitals rows, extra kind 5).
//   II.2 rider_from_host: the host says per NPC "on <horse>" or "on foot" (extra kind 6); the joiner mounts and dismounts its copy on
//        that word alone (WO-170's gates), never on geometry.
//   II.4 copy_neutral_always: the DLL's config bit (wo141's reconcile asks no planner for copies).
//   II.7 world_clock_from_host: the host's clock every 2 s (W164 kind 18); the joiner slews its own toward it through the clock's ratio
//        (never a jump); large gaps stay with WO-140's pull and WO-133's forward convergence.
public partial class GameBridge
{
    private long _w200HitsNotApplied, _w200VitalsOut, _w200VitalsIn, _w200StaminaSet, _w200MountOut, _w200MountIn, _w200ClockOut, _w200ClockIn, _w200Slews;
    private readonly ConcurrentDictionary<string, (string Horse, float St, float Hp)> _w200HostVals = new(StringComparer.Ordinal);
    private double _w200Ratio = 15.0;       // the ratio this joiner last set (15 = the game's own)
    private bool _w201CatchingUp;           // WO-201 B13: a lag over 30 game s is being caught up
    private bool _w200Slewing;
    private long _w200ClockTick;

    /// <summary>The DLL's sixth config byte (Act II): 1 = copy_neutral_always.</summary>
    private byte Wo200ValueBits() => (byte)(W200("copy_neutral_always") ? 1 : 0);

    // ---------------------------------------------------------------- the host: its Lua reports its streamed NPCs' mount and stamina

    /// <summary>"w200_hostvals": "name|horse|stamina|health;..." (horse "-" = on foot), only what changed (the Lua's own pace).</summary>
    private async Task Wo200OnHostValuesAsync(string? arg)
    {
        if (!W137Host || string.IsNullOrEmpty(arg)) return;
        var vitals = new List<ExtraRow>();
        var mounts = new List<ExtraRow>();
        foreach (var e in arg.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = e.Split('|');
            if (f.Length != 4 || !NpcNamePattern.IsMatch(f[0])) continue;
            string horse = f[1] == "-" ? "" : f[1];
            if (horse.Length > 0 && !NpcNamePattern.IsMatch(horse)) continue;
            if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float st) || !float.IsFinite(st)) st = -1;
            if (!float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float hp) || !float.IsFinite(hp)) hp = -1;
            bool had = _w200HostVals.TryGetValue(f[0], out var was);
            _w200HostVals[f[0]] = (horse, st, hp);
            if (W200("rider_from_host") && (!had || was.Horse != horse)) mounts.Add(new ExtraRow { Name = f[0], Target = horse });
            if (W200("copy_health_from_host") && st >= 0) vitals.Add(new ExtraRow { Name = f[0], Health = hp, Stamina = st });
        }
        var peers = Wo134Peers();
        foreach (byte g in peers)
        {
            for (int i = 0; i < mounts.Count; i += Protocol.ExtraMaxRows) await Wo143SendAsync(g, Protocol.ExtraKindMount, mounts.Skip(i).Take(Protocol.ExtraMaxRows).ToList());
            for (int i = 0; i < vitals.Count; i += Protocol.ExtraMaxRows) await Wo143SendAsync(g, Protocol.ExtraKindVitals, vitals.Skip(i).Take(Protocol.ExtraMaxRows).ToList());
        }
        if (peers.Count > 0)
        {
            Interlocked.Add(ref _w200MountOut, mounts.Count);
            Interlocked.Add(ref _w200VitalsOut, vitals.Count);
            foreach (var m in mounts) Console.WriteLine($"MP-W200 host: {m.Name} is {(m.Target.Length > 0 ? "on " + m.Target : "on foot")} -> {peers.Count} joiner(s)");
        }
    }

    // ---------------------------------------------------------------- the joiner: the host's values in (from WO-143's extra frames)

    private async Task Wo200OnExtraAsync(byte kind, ExtraRow r)
    {
        switch (kind)
        {
            case Protocol.ExtraKindVitals:
            {
                Interlocked.Increment(ref _w200VitalsIn);
                if (!W200("copy_health_from_host") || !Wo131JoinerActive || !_w131Guarded.TryGetValue(r.Name, out uint eid)) return;
                Guid? g = await ResolveLocalSoulGuidAsync(r.Name, CancellationToken.None);
                if (g is not Guid lg) return;
                float hp = r.Health >= 0 ? r.Health : (_w131StreamHp.TryGetValue(r.Name, out float sh) ? sh : -1);
                if (hp < 0) return;
                var (ok, _, _, _) = await _combat.Wo131FollowHpAsync(lg, eid, hp, stamina: r.Stamina);
                if (ok) Interlocked.Increment(ref _w200StaminaSet);
                return;
            }
            case Protocol.ExtraKindMount:
                Interlocked.Increment(ref _w200MountIn);
                if (!W200("rider_from_host")) return;
                Console.WriteLine($"MP-W200 joiner: the host says {r.Name} is {(r.Target.Length > 0 ? "on " + r.Target : "on foot")}");
                await ExecLuaAsync($"if KCD2MP_W200Mount then KCD2MP_W200Mount(\"{r.Name}\", \"{r.Target}\") end");
                return;
        }
    }

    /// <summary>II.1: a host blow on a copy is not applied here -- the copy shows the host's own values (true = skipped).
    /// WO-201 Act VI (WO-200 ledger row 6, retired): never applied, switch or not -- the field's duel showed right both ways; the
    /// switch keeps only the health and stamina mirror.</summary>
    private bool Wo200SkipLocalHit(string npc, float hp, float st, bool fatal)
    {
        if (fatal || !Wo131JoinerActive) return false;
        long n = Interlocked.Increment(ref _w200HitsNotApplied);
        if (n <= 20 || n % 50 == 0)
            Console.WriteLine(FormattableString.Invariant($"[npcdmg] in: the host's blow on '{npc}' (hp -{hp:F1} st -{st:F1}) not applied here -- the copy shows the host's own health and stamina (copy_health_from_host; #{n})"));
        return true;
    }

    // ---------------------------------------------------------------- II.7 the clock

    /// <summary>The 1 Hz tick (WO-200's loop): the host sends its clock every 2 s; nothing on the joiner (it answers the host's message).</summary>
    private async Task Wo200ClockTickAsync()
    {
        if (++_w200ClockTick % 2 != 0) return;
        if (!W137Host || !W200("world_clock_from_host") || _lastPolledWorldTime is not uint t) return;
        var peers = Wo134Peers();
        if (peers.Count == 0) return;
        // the host's clock now: its last reading moved on by the real seconds since (the game's own ratio)
        double now = t + (DateTime.UtcNow - _lastPollUtc).TotalSeconds * WorldTimeRatio;
        string text = string.Create(CultureInfo.InvariantCulture, $"{(uint)now} {(int)(WorldTimeRatio * 1000)}");
        foreach (byte g in peers) await W164SendAsync(g, Protocol.W164Clock, text);
        Interlocked.Increment(ref _w200ClockOut);
    }

    private async Task Wo200OnHostClockAsync(string text)
    {
        Interlocked.Increment(ref _w200ClockIn);
        var f = text.Split(' ');
        if (f.Length != 2 || !uint.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint host) ||
            !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out int ratio1000) || ratio1000 < 1000 || ratio1000 > 100000) return;
        if (!W200("world_clock_from_host") || !_joinedWorld || _isDamageAuthority) { await Wo200RatioAsync(15.0, "off"); return; }
        // WO-209 D.1 (mp_clock_quiet_in_skip): the game's own time-lapse (a sleep, a wait) runs on its own ratio -- no write over it (the
        // smoke's 9 h sleep: the reset 3 s in ran the joiner's sleep 56.5 s past the host's); the next correction after it writes afresh
        bool skipping = _localSkipActive || _w140LocalSkipping;
        // WO-210 H.5: the host's clock kept for the marks; a gap outside a skip told
        if (_lastPolledWorldTime is uint mine0) Wo210OnHostClock(host, ratio1000 / 1000.0, mine0 + (DateTime.UtcNow - _lastPollUtc).TotalSeconds * _w200Ratio - host, skipping);
        if (skipping && !Wo209Rules.RatioWriteAllowed(true, W209("clock_quiet_in_skip"))) { Wo209NoteSkipQuiet(); return; }
        if (skipping || Wo136Holding || _lastPolledWorldTime is not uint mine) { if (_w200Slewing) await Wo200RatioAsync(15.0, "a skip or a load"); return; }
        // WO-208 D.2 (mp_clock_stands_with_host): this clock stands with the host's (WO-144: the host is in a dialogue, ratio 0) -- no
        // correction writes a running ratio over it (the baseline: the joiner's clock ran on through the host's talks, up to 917 game s
        // ahead, then was pulled back in jumps)
        if (!Wo208Rules.ClockSlewAllowed(_w144Following, W208("clock_stands_with_host"))) { Wo208NoteClockHeld(); return; }
        double hostRatio = ratio1000 / 1000.0;
        double myNow = mine + (DateTime.UtcNow - _lastPollUtc).TotalSeconds * _w200Ratio;
        double err = myNow - host;   // game seconds this clock is ahead of the host's
        double want = W201("clock_catchup") ? Wo201Rules.CatchUpRatio(hostRatio, err, ref _w201CatchingUp) : Wo200Rules.SlewRatio(hostRatio, err);   // WO-201 B13
        await Wo200RatioAsync(want, FormattableString.Invariant($"{err:+0;-0} game s from the host's"));
    }

    private async Task Wo200RatioAsync(double ratio, string why)
    {
        if (Interlocked.Exchange(ref _w209RatioDirty, 0) == 1) _w200Ratio = double.NaN;   // WO-209 D.1: after a skip the game's ratio is its own
        if (Math.Abs(ratio - _w200Ratio) < 0.05) return;
        _w200Ratio = ratio;
        _w200Slewing = Math.Abs(ratio - 15.0) >= 0.05;
        Interlocked.Increment(ref _w200Slews);
        Console.WriteLine(FormattableString.Invariant($"WO200-CLOCK joiner: this clock's ratio {ratio:F2} ({why}) -- a gentle correction, never a jump"));
        try { await ExecLuaAsync(FormattableString.Invariant($"pcall(function() Calendar.SetWorldTimeRatio({ratio:F3}) end)")); } catch { }
    }

    // ---------------------------------------------------------------- II.6: the figure's clinch, ended

    /// <summary>A figure's clinch the engine refused to sync: figure entity -> (its opponent's entity, when) -- its end re-asserts the guard.</summary>
    private readonly ConcurrentDictionary<uint, (uint Opp, long AtMs)> _w200ClinchRows = new();
    private long _w200ClinchEnds;

    /// <summary>The engine's own EnsureGuardAction on the figure and on its opponent copy: the clinch guard is replaced by the plain guard.</summary>
    private async Task Wo200EndClinchAsync(uint figure, uint opp, string why, CancellationToken ct)
    {
        var parts = new List<string>();
        foreach (var (eid, who) in new[] { (figure, "figure"), (opp, "opponent") })
        {
            if (eid == 0) continue;
            var a = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
            try
            {
                var r = await _combat.Wo163Async(Protocol.Wo197OpEnsureGuard, a, ct);
                parts.Add(r is { Ok: true, Payload.Length: >= 1 } x
                    ? FormattableString.Invariant($"{who}=0x{eid:X} result={x.Payload[0]} before={System.Text.Encoding.ASCII.GetString(x.Payload, 1, x.Payload.Length - 1)}")
                    : FormattableString.Invariant($"{who}=0x{eid:X} refused ({r?.Reason})"));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { parts.Add($"{who} failed ({ex.GetType().Name})"); }
        }
        Interlocked.Increment(ref _w200ClinchEnds);
        Console.WriteLine($"WO200-CLINCH end why={why} {string.Join(" ", parts)} -- a clinch with no engine sync (StartSyncGuard refused) ended with the engine's own guard");
    }

    // ---------------------------------------------------------------- Act IV: noise the host's world can hear

    private long _w200NoiseOut, _w200NoiseIn;

    /// <summary>The joiner: "&lt;kind&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;" from his script, to the host as an action.</summary>
    private async Task Wo200SendNoiseAsync(string? arg)
    {
        if (!W137Joiner || !Wo200Rules.TryParseNoise(arg, out string kind, out float x, out float y, out float z)) return;
        await W164SendAsync(Protocol.JoinTargetHost, Protocol.W164Noise, Wo200Rules.NoiseText(kind, x, y, z));
        Interlocked.Increment(ref _w200NoiseOut);
    }

    /// <summary>The host: a joiner's noise -- the engine's own perception sound there (the script rate-limits and logs it).</summary>
    private async Task Wo200OnJoinerNoiseAsync(byte src, string text)
    {
        if (!Wo200Rules.TryParseNoise(text, out string kind, out float x, out float y, out float z)) { Interlocked.Increment(ref _w164Malformed); return; }
        Interlocked.Increment(ref _w200NoiseIn);
        await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W200NoisePulses then KCD2MP_W200NoisePulses(\"{kind}\", {x:F2}, {y:F2}, {z:F2}, {src}) end"));
    }

    /// <summary>The host: a joiner's swing played on his figure is a fight the host's world hears (the engine's combat sound at the figure).</summary>
    private void Wo200FigureSwingNoise(string gid)
    {
        if (!W137Host) return;
        _ = ExecLuaAsync($"if KCD2MP_W200NoiseAtBody then KCD2MP_W200NoiseAtBody(\"combat\", \"kcd2mp_{gid}\") end");
    }

    private string Wo200ValuesStats() => FormattableString.Invariant(
        $"hits_not_applied={_w200HitsNotApplied} vitals_out={_w200VitalsOut} vitals_in={_w200VitalsIn} stamina_set={_w200StaminaSet} mount_out={_w200MountOut} mount_in={_w200MountIn} clock_out={_w200ClockOut} clock_in={_w200ClockIn} slews={_w200Slews} ratio={_w200Ratio:F2} clinch_ends={_w200ClinchEnds} noise_out={_w200NoiseOut} noise_in={_w200NoiseIn}");
}
