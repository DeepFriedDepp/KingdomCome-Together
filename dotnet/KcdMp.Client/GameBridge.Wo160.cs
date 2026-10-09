// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-160: the 2026-10-07 tester findings, agent half (docs/WO-160-findings.md). The pure rules are Wo160Rules (Wo160.cs).
public partial class GameBridge
{
    // ------------------------------------------------------------ 2 the false "You loaded your own save"

    // The joiner agent outlives its game. When a game ends without a quit line (a crash, a kill) the agent's _where kept
    // "World"; the next process was still at its main menu when the host connected, and the own-world verdict said "You
    // loaded your own save" ~20 s before the join's load even started (field, joiner 17:41:54). The verdict now needs the
    // mod's own, recent "world" answer, and is silent during and for 60 s after a join's activity.

    private long _w160ConfirmedMs;      // TickCount64 of the mod's last "world" answer (or Gameplay started); 0 = never
    private long _w160AskedMs;          // the last time the mod was asked where the game is (this feature's asks)
    private long _w160JoinActiveMs;     // TickCount64 of the last tick a join was in flight; 0 = none
    private long _w160Held;             // verdicts held back by the rule (the log counts them)

    /// <summary>The mod said a world is loaded (its own answer), or "Gameplay started" just landed.</summary>
    private void W160WorldConfirmed() => Interlocked.Exchange(ref _w160ConfirmedMs, Math.Max(1, Environment.TickCount64));

    /// <summary>The game is not in a world (menu, a load, a quit, a failed load): the proof goes.</summary>
    private void W160WorldGone() => Interlocked.Exchange(ref _w160ConfirmedMs, 0);

    private long W160ConfirmedAgeMs()
    {
        long c = Interlocked.Read(ref _w160ConfirmedMs);
        return c == 0 ? -1 : Environment.TickCount64 - c;
    }

    /// <summary>Ask the mod where the game is when the proof is missing or aging (the answer lands in Wo124OnWhere).</summary>
    private async Task W160AskWhereIfDueAsync()
    {
        long now = Environment.TickCount64;
        if (!Wo160Rules.WhereRefreshDue(W160ConfirmedAgeMs(), now - _w160AskedMs)) return;
        _w160AskedMs = now;
        try { await ExecLuaAsync("if KCD2MP_Wo124Where then KCD2MP_Wo124Where() end"); } catch { }
    }

    /// <summary>Called every second by the WO-140 loop: a join in flight keeps the verdict quiet for 60 s after it ends.</summary>
    private void W160NoteJoinActivity()
    {
        if (W140JoinBusy) _w160JoinActiveMs = Math.Max(1, Environment.TickCount64);
    }

    private long W160JoinQuietAgeMs() => _w160JoinActiveMs == 0 ? -1 : Environment.TickCount64 - _w160JoinActiveMs;

    /// <summary>The own-world verdict's gate (WO-160 2). The first hold in a row is logged.</summary>
    private async Task<bool> W160SeparateAllowedAsync()
    {
        await W160AskWhereIfDueAsync();
        bool ok = Wo160Rules.SeparateVerdictAllowed(W160ConfirmedAgeMs(), W140JoinBusy, W160JoinQuietAgeMs());
        if (!ok && Interlocked.Increment(ref _w160Held) % 30 == 1)
            Console.WriteLine($"WO160-SEPARATE held: no verdict (world confirmed by the mod {(W160ConfirmedAgeMs() < 0 ? "never" : $"{W160ConfirmedAgeMs() / 1000.0:F0} s ago")}, join busy={W140JoinBusy}, join activity {(W160JoinQuietAgeMs() < 0 ? "none" : $"{W160JoinQuietAgeMs() / 1000.0:F0} s ago")}, where={_where}) -- a game at its main menu or a join's load is not 'your own save'");
        return ok;
    }

    // ------------------------------------------------------------ 5 the herb proxy, the loop stop's re-pulse

    private readonly ConcurrentDictionary<byte, bool> _w160Herb = new();

    /// <summary>The avatar of <paramref name="peer"/> bends and picks (on) or goes back to its locomotion (off): a plain clip, told to the mod once per change.</summary>
    private async Task W160HerbProxyAsync(byte peer, bool on)
    {
        bool was = _w160Herb.GetValueOrDefault(peer);
        if (was == on) return;
        _w160Herb[peer] = on;
        Console.WriteLine($"MP-WO160 player {peer}: herb gathering -- the avatar {(on ? "bends and picks (a plain clip: no minigame fragment, no camera selector)" : "stands up again")}");
        await ExecLuaAsync($"if KCD2MP_W160HerbProxy then KCD2MP_W160HerbProxy({peer}, {B(on)}) end");
    }

    /// <summary>The mod stopped a loop on an avatar (its herb clip): the DLL's walk-class pulse runs again (AvatarEvent kind 2).</summary>
    private async Task W160LoopStoppedAsync(string idText, string why)
    {
        if (!_ghostEntityIds.TryGetValue(idText, out uint eid)) return;
        var r = await _combat.AvatarEventAsync(2, eid);
        Console.WriteLine($"MP-WO160 avatar {idText}: {why} -- T-pose pulse asked of the DLL ({r.ReasonTag})");
    }

    // ------------------------------------------------------------ 1 the joiner's NPC copies: one context root cause

    // The game's planner could not place 5,308 of the field joiner's 8,316 placements (docs/WO-160-findings.md). The DLL no longer
    // clears the loaded state (loaded mode 4); this releases a placed copy's stance and unstance before it is placed in another
    // activity, and counts what the planner still refuses, per NPC, from the engine's own lines.

    private volatile bool _w160ReleaseOn = true;
    private readonly ConcurrentDictionary<string, ActivityState> _w160Given = new(StringComparer.Ordinal);   // NPC -> the activity the DLL was last given
    private readonly ConcurrentDictionary<string, int> _w160Refused = new(StringComparer.Ordinal);          // NPC -> "Couldn't find actions" lines
    private readonly ConcurrentDictionary<string, int> _w160Unreached = new(StringComparer.Ordinal);        // NPC -> "couldn't reach the loaded state" lines
    private long _w160Released, _w160Placed, _w160ReportsOpen, _w160StatsMs, _w160StatsRefused, _w160StatsUnreached;
    private long _w160ConvIn, _w160ConvOut;   // 3: conversation messages the joiner received / the host sent

    /// <summary>The engine's planner failure lines (LogTailGameTransport.Wo144Line routes them here). True = it was one.</summary>
    private bool W160OnEngineLine(string line)
    {
        string? npc = Wo160Rules.PlannerErrorNpc(line, out bool refused);
        if (npc is null) return false;
        (refused ? _w160Refused : _w160Unreached).AddOrUpdate(npc, 1, (_, n) => n + 1);
        return true;
    }

    private int W160ErrorsOf(string name) => _w160Refused.GetValueOrDefault(name) + _w160Unreached.GetValueOrDefault(name);

    /// <summary>
    /// A host row for an NPC copy (or a kept one again): its stance and unstance are released first when it was placed before
    /// and the activity changed, then the DLL places it. Awaited in order: the release is a console call and must have run before
    /// the placement's pipe op does.
    /// </summary>
    private async Task<bool?> W160ApplyAsync(string name, ActivityState a, bool force = false)
    {
        bool avatar = name.StartsWith("kcd2mp_", StringComparison.Ordinal);
        bool had = _w160Given.TryGetValue(name, out var prev);
        bool changed = force || !had || prev != a;   // force: a time skip changed every copy's state under its activity
        bool release = _w160ReleaseOn && Wo160Rules.ReleaseBeforeApply(avatar, had && !prev.IsNone, changed);
        int before = W160ErrorsOf(name);
        if (release)
        {
            try { await ExecLuaAsync($"if KCD2MP_W160Release then KCD2MP_W160Release(\"{EscapeLua(name)}\", \"activity-changed\") end"); } catch { }
            Interlocked.Increment(ref _w160Released);
        }
        bool? known = await _combat.Wo141ApplyAsync(name, a);
        _w160Given[name] = a;
        if (changed && !avatar)
        {
            Interlocked.Increment(ref _w160Placed);
            // one line per copy and treatment, the errors the game's planner printed for it in the 6 s after
            if (Interlocked.Increment(ref _w160ReportsOpen) <= 40)
                _ = W160ReportAsync(name, a, release, before);
            else Interlocked.Decrement(ref _w160ReportsOpen);
        }
        return known;
    }

    private long _w160SettleMs;

    /// <summary>
    /// After a time skip (this player's own sleep or wait ended, or the host's announced skip was applied) every copy this joiner
    /// holds is brought into agreement with its host row again: its stance and unstance released, the host's activity placed -- the
    /// schedule has changed outfits and workstations under every paused copy (field: the "ChangeEquipment ... stash[Chest]" spikes
    /// right after the joiner's time skips at 16:58 and 17:12). One settle in five seconds at most; the game's own skip sim is given
    /// three seconds first; at most 80 copies.
    /// </summary>
    private async Task W160SettleAfterSkipAsync(string why)
    {
        if (!W141Joiner || !W141Active) return;
        long now = Environment.TickCount64;
        if (now - Interlocked.Exchange(ref _w160SettleMs, now) < 5_000) return;
        if (_w164On)
        {
            // WO-164 T1: the field showed the re-placement below refused again and again after a skip (the error storm 60-100 s
            // later); every copy within 300 m is swept instead (released, its loaded state := its body), paced by the Lua, and the
            // reconcile places it again at its own pace
            try { await Task.Delay(3_000); } catch { return; }
            await ExecLuaAsync($"if KCD2MP_W164AfterSkip then KCD2MP_W164AfterSkip(\"{EscapeLua(why)}\") end");
            Console.WriteLine($"WO164-SWEEP after {why}: every copy within 300 m is swept (the Lua paces it, 4 per tick)");
            return;
        }
        try { await Task.Delay(3_000); } catch { return; }
        int n = 0;
        foreach (var (name, a) in _w141HostRows)
        {
            if (a.IsNone || _w141Blocked.ContainsKey(name)) continue;
            if (n >= 80) break;
            await W160ApplyAsync(name, a, force: true);
            n++;
        }
        Console.WriteLine($"WO160-CTX settle after {why}: {n} cop{(n == 1 ? "y" : "ies")} released and placed again");
    }

    private async Task W160ReportAsync(string name, ActivityState a, bool released, int before)
    {
        try
        {
            await Task.Delay(6000);
            int after = W160ErrorsOf(name);
            Console.WriteLine($"WO160-CTX npc={name} placed={(a.IsNone ? "none" : a.ToString())} released={(released ? "yes" : "no")} loaded=kept cleared=no errors_before={before} errors_after={after} (+{after - before} in 6 s)");
        }
        finally { Interlocked.Decrement(ref _w160ReportsOpen); }
    }

    /// <summary>One line a minute while the planner is failing placements (or has been): the totals and the worst NPCs. Null when quiet.</summary>
    private string? W160StatsLine()
    {
        long refused = _w160Refused.Values.Sum(v => (long)v), unreached = _w160Unreached.Values.Sum(v => (long)v);
        if (refused == 0 && unreached == 0 && _w160Placed == 0) return null;
        long dRef = refused - _w160StatsRefused, dUn = unreached - _w160StatsUnreached;
        _w160StatsRefused = refused; _w160StatsUnreached = unreached;
        var worst = _w160Refused.Keys.Concat(_w160Unreached.Keys).Distinct()
            .Select(n => (n, c: W160ErrorsOf(n))).OrderByDescending(x => x.c).Take(3).Select(x => $"{x.n}:{x.c}");
        return $"MP-WO160-STATS planner refused={refused} (+{dRef}/min) unreached={unreached} (+{dUn}/min) npcs={_w160Refused.Keys.Concat(_w160Unreached.Keys).Distinct().Count()} placed={_w160Placed} released={_w160Released} worst=[{string.Join(",", worst)}]";
    }
}
