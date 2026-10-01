// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;

namespace KcdMp.Client;

// WO-131: combat and bodies -- the agent's half (docs/WO-131-findings.md).
//
// One world, the host's. On the JOINER:
//   * the copy guard (Lua KCD2MP_W131Tick, once a second): every host-owned NPC
//     is the host's stream or parked (suspended + hidden), never a free brain;
//   * a hit on a copy is forwarded only when the DLL says the body is driven by
//     a fresh host sample within 3 m (1b), never as a kill (1c);
//   * the copy cannot die locally: the imm guard on every puppet, its health
//     written from the host's stream, and the guard lifted right before the
//     host's death is applied (1c).
// On the HOST:
//   * an avatar joins the player's faction and is never AI-ignorant (1d);
//   * after an NPC's hit on an avatar is measured and forwarded its health is
//     put back, so the next hit is measured too (1e);
//   * the joiner's death / respawn ends the fight around its avatar (1g).
public partial class GameBridge
{
    private volatile bool _w131Connected;
    private volatile bool _w131Perceive = true;   // mirrors the mod's KCD2MP.w131.perceive (event w131_cfg)
    private readonly ConcurrentDictionary<string, uint> _w131Guarded = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (float Hp, long AtMs)> _w131HpWritten = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, float> _w131StreamHp = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Guid> _w131FactionDone = new();
    private readonly ConcurrentDictionary<string, long> _w131FactionTriedMs = new();
    private long _w131HitsForwarded, _w131HitsDropped, _w131DeathsBlocked, _w131GuardOn, _w131GuardFail,
                 _w131Follow, _w131FollowFail, _w131Restore, _w131RestoreFail, _w131StopFights, _w131FactionOk, _w131FactionFail;
    private readonly ConcurrentDictionary<string, long> _w131DropReasons = new();

    private static long W131NowMs() => Environment.TickCount64;

    /// <summary>This machine is the joiner of a host-authority shared world (the copy guard's condition).</summary>
    private bool Wo131JoinerActive =>
        _w131Connected && _combatRoleApplied && !_isDamageAuthority && _hostAuthority && JoinerSharedEffective;

    private bool Wo131HostPerceiveActive => _w131Connected && _combatRoleApplied && _isDamageAuthority && _sharedWorld && _w131Perceive;

    private void Wo131OnConnect(CancellationToken ct)
    {
        _w131Connected = true;
        _w131Guarded.Clear();
        _w131HpWritten.Clear();
        _w131FactionDone.Clear();
        _w131FactionTriedMs.Clear();
        _ = Wo131LoopAsync(ct);
    }

    private async Task Wo131OnDisconnectAsync()
    {
        _w131Connected = false;
        // The copy guard on puppets is harmless once nothing drives them, but it
        // is the mod's buff on the world's NPCs: take it back off.
        foreach (var (name, eid) in _w131Guarded.ToArray())
        {
            try { await _combat.Wo131CopyGuardAsync(false, eid); } catch { }
            _w131Guarded.TryRemove(name, out _);
        }
        try { await _transport.ExecuteNowAsync("if KCD2MP_W131Tick then KCD2MP_W131Tick(false, false) end"); } catch { }
    }

    private async Task Wo131LoopAsync(CancellationToken ct)
    {
        long lastStats = W131NowMs();
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                if (_where == GameWhere.Menu) continue;
                // WO-135: never while a world loads. The guard's pauses landed right after
                // EntityModuleOnPostLoadGame, before "Gameplay started": suspended NPCs never
                // finish the AI's post-load reconstruction, so the loading screen waited for the
                // engine's timeout ("Loading screen timeouted while still in post load
                // reconstruction", observed on a join, run J1). The load drops every suspension
                // anyway; the first tick after Gameplay started re-parks (the reassert path).
                // WO-136: and not in the settle after it, nor at the menu -- the load hold.
                if (Wo136Holding) continue;
                await Wo136TellModAsync();
                bool joiner = _combatRoleApplied && !_isDamageAuthority && _hostAuthority;
                // WO-136 (J2): a shared-world joiner back in its own world (after a leave, before
                // the next join) has none of the host's NPCs -- no copy guard, no stand-ins there
                // (a wolf stand-in was spawned into the player's own world, observed).
                bool shared = joiner ? JoinerSharedEffective && _joinedWorld : _sharedWorld;
                _ = ExecLuaAsync($"if KCD2MP_W131Tick then KCD2MP_W131Tick({(joiner ? "true" : "false")}, {(shared ? "true" : "false")}) end");
                if (Wo131HostPerceiveActive) await Wo131FactionSweepAsync(ct);
                if (W131NowMs() - lastStats >= 60_000)
                {
                    lastStats = W131NowMs();
                    string reasons = string.Join(",", _w131DropReasons.Select(kv => $"{kv.Key}:{kv.Value}"));
                    string? nat = await _combat.Wo131StatusAsync(ct);
                    Console.WriteLine(FormattableString.Invariant(
                        $"MP-WO131-STATS joiner={(Wo131JoinerActive ? 1 : 0)} perceive_host={(Wo131HostPerceiveActive ? 1 : 0)} hits_fwd={_w131HitsForwarded} hits_drop={_w131HitsDropped} [{reasons}] deaths_blocked={_w131DeathsBlocked} guard_on={_w131GuardOn} guard_fail={_w131GuardFail} follow={_w131Follow} follow_fail={_w131FollowFail} restore={_w131Restore} restore_fail={_w131RestoreFail} stopfight={_w131StopFights} faction_ok={_w131FactionOk} faction_fail={_w131FactionFail} | {nat ?? "native: no answer"}"));
                    Console.WriteLine(Wo136StatsLine());   // WO-136
                }
            }
            catch (Exception ex) { Console.WriteLine($"MP-WO131 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    // ------------------------------------------------------------------ joiner

    /// <summary>
    /// 1b/1c, from OnLocalHit: may this measured hit on <paramref name="npcName"/>
    /// go to the host, and with what health? Outside a joiner session every
    /// hit passes unchanged. A joiner never sends FATAL.
    /// </summary>
    private async Task<(bool Send, float Health, bool Fatal)> Wo131GateOutboundAsync(Guid soul, string npcName, float health, bool died)
    {
        if (Wo140HoldOutbound($"a hit on {npcName}")) return (false, 0, false);   // WO-140: its own world, not the host's
        if (!Wo131JoinerActive) return (true, health, died);
        Wo131HitCheck? hc = null;
        try { hc = await _combat.Wo131HitCheckAsync(soul, npcName); } catch { }
        var (v, h) = Wo131Rules.GateJoinerHit(hc, health);
        if (v != Wo131Rules.HitVerdict.Forward)
        {
            Interlocked.Increment(ref _w131HitsDropped);
            _w131DropReasons.AddOrUpdate(Wo131Rules.Tag(v), 1, (_, n) => n + 1);
            Console.WriteLine(FormattableString.Invariant(
                $"MP-DMG dir=drop npc={npcName} hp={health:F1} fatal={(died ? 1 : 0)} reason=wo131-{Wo131Rules.Tag(v)} bound={(hc?.Bound == true ? 1 : 0)} age_ms={(hc?.AgeMs ?? 65535)} dist_m={(hc?.DistM ?? -1):F2} -- a hit counts only on the host's NPC where the host has it"));
            // The refused blow leaves no trace on a guarded copy: its health goes
            // straight back to the host's (credited, so it never echoes).
            if (_w131Guarded.TryGetValue(npcName, out uint geid) && _w131StreamHp.TryGetValue(npcName, out float shp))
            {
                var (fok, fb, fa, _) = await _combat.Wo131FollowHpAsync(soul, geid, shp);
                if (fok) Console.WriteLine(FormattableString.Invariant($"MP-WO131 refused hit on {npcName}: copy health put back {fb:F1} -> {fa:F1} (the host's)"));
            }
            return (false, 0, false);
        }
        Interlocked.Increment(ref _w131HitsForwarded);
        if (died) Interlocked.Increment(ref _w131DeathsBlocked);
        if (h != health || died)
            Console.WriteLine(FormattableString.Invariant(
                $"MP-WO131 hit on {npcName}: measured {health:F1}{(died ? " (local FATAL -- not sent: the host decides death)" : "")}, forwarded {h:F1}{(h != health ? " (the copy sat at the imm floor)" : "")}"));
        return (true, h, false);
    }

    /// <summary>1c: the mod's own death observer on a joiner -- the host decides deaths, so nothing goes out.</summary>
    private bool Wo131BlockLocalDeath(string npcName)
    {
        if (!Wo131JoinerActive) return false;
        Interlocked.Increment(ref _w131DeathsBlocked);
        Console.WriteLine($"[npcdeath] out: '{npcName}' died locally on the joiner -- NOT sent (WO-131: only the host's world decides a death)");
        return true;
    }

    /// <summary>1c: a new puppet on the joiner -- it can never die or be knocked out locally.</summary>
    private async Task Wo131OnPuppetAsync(string name, uint eid)
    {
        if (!Wo131JoinerActive || eid == 0) return;
        if (_w131Guarded.TryGetValue(name, out var had) && had == eid) return;
        var (ok, present, reason) = await _combat.Wo131CopyGuardAsync(true, eid);
        if (ok && present)
        {
            _w131Guarded[name] = eid;
            await Wo132DiscardAsync(true, eid);   // WO-132: its local hits on me never count
            if (Interlocked.Increment(ref _w131GuardOn) <= 20)
                Console.WriteLine($"MP-WO131 copy guard on {name} (eid 0x{eid:X}): it dies only when the host says so");
        }
        else if (Interlocked.Increment(ref _w131GuardFail) <= 10)
            Console.WriteLine($"MP-WO131 copy guard on {name} FAILED (reason {reason})");
    }

    /// <summary>1c: every host sample on the joiner -- the copy's health follows the host's.</summary>
    private void Wo131OnNpcSample(string name, float hp, bool dead)
    {
        Wo147NoteStreamDead(name, dead);   // WO-147: a copy the host's stream calls dead is never engaged again
        if (!dead && hp >= 0) _w131StreamHp[name] = hp;
        if (!Wo131JoinerActive || !_w131Guarded.TryGetValue(name, out uint eid)) return;
        long now = W131NowMs();
        var last = _w131HpWritten.TryGetValue(name, out var l) ? l : (float.NaN, 0L);
        if (!Wo131Rules.FollowHpDue(last.Item1, hp, now - last.Item2, dead)) return;
        _w131HpWritten[name] = (hp, now);
        _ = Task.Run(async () =>
        {
            Guid? g = await ResolveLocalSoulGuidAsync(name, CancellationToken.None);
            if (g is not Guid lg) return;
            var (ok, before, after, reason) = await _combat.Wo131FollowHpAsync(lg, eid, hp);
            if (ok)
            {
                long n = Interlocked.Increment(ref _w131Follow);
                if (n <= 20 || Math.Abs(before - after) > 5)
                    Console.WriteLine(FormattableString.Invariant($"MP-WO131 follow {name}: host hp {hp:F1} -> copy {before:F1} -> {after:F1}"));
            }
            else if (Interlocked.Increment(ref _w131FollowFail) <= 10)
                Console.WriteLine($"MP-WO131 follow {name} FAILED (reason {reason})");
        });
    }

    /// <summary>1c: right before the host's death is applied to a copy, lift its guard.</summary>
    private async Task Wo131BeforeDeathAsync(string name)
    {
        if (!_w131Guarded.TryRemove(name, out uint eid)) return;
        try { await _combat.Wo131CopyGuardAsync(false, eid); } catch { }
        await Wo132DiscardAsync(false, eid);
        if (_w132Engaged.TryRemove(name, out var eg)) { try { await _combat.Wo132EngageAsync(false, eg.Eid, default); } catch { } }
        Console.WriteLine($"MP-WO131 copy guard off {name}: the host's death is applied now, at the host's position");
    }

    // -------------------------------------------------------------------- host

    /// <summary>1e: an NPC's hit on avatar <paramref name="ghostId"/> was measured and forwarded -- put its health back.</summary>
    private async Task Wo131RestoreAvatarAsync(byte ghostId)
    {
        if (!_isDamageAuthority) return;
        if (!_ghostEntityIds.TryGetValue(ghostId.ToString(CultureInfo.InvariantCulture), out uint eid)) return;
        var (ok, before, after, reason) = await _combat.Wo131RestoreHpAsync(eid, Wo131Rules.AvatarRestoreHp);
        if (ok)
        {
            long n = Interlocked.Increment(ref _w131Restore);
            if (n <= 20) Console.WriteLine(FormattableString.Invariant($"MP-WO131 avatar {ghostId} health restored {before:F1} -> {after:F1} (the next hit is measured too)"));
        }
        else if (Interlocked.Increment(ref _w131RestoreFail) <= 10)
            Console.WriteLine($"MP-WO131 avatar {ghostId} health restore FAILED (reason {reason})");
    }

    // 1g (the joiner died or woke): WO-132 replaced the avatar's StopFight -- it
    // ended the whole fight, the host's with it -- by the avatar leaving its
    // skirmish at the down itself (GameBridge.Wo132.cs).

    /// <summary>1d: every avatar here joins the player's faction (once per spawn; retried every 10 s).</summary>
    private async Task Wo131FactionSweepAsync(CancellationToken ct)
    {
        long now = W131NowMs();
        foreach (var key in _ghostEntityIds.Keys.ToArray())
        {
            if (!byte.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte gid)) continue;
            if (_w131FactionTriedMs.TryGetValue(key, out long tried) && now - tried < 10_000) continue;
            Guid? g = await ResolveGhostSoulGuidAsync(gid, ct);
            if (g is not Guid soul) continue;
            if (_w131FactionDone.TryGetValue(key, out var done) && done == soul) continue;
            _w131FactionTriedMs[key] = now;
            var (ok, reason) = await _combat.Wo131FactionAsync(soul, 2, ct);
            if (ok) { _w131FactionDone[key] = soul; Interlocked.Increment(ref _w131FactionOk); }
            else Interlocked.Increment(ref _w131FactionFail);
            Console.WriteLine($"MP-WO131 avatar {gid} -> the player's faction: {(ok ? "joined (read back)" : $"FAILED (reason {reason})")}");
        }
    }

    private void Wo131OnCfg(string arg)
    {
        foreach (var kv in arg.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (kv.StartsWith("perceive=", StringComparison.Ordinal)) _w131Perceive = kv.EndsWith("on", StringComparison.Ordinal);
        if (_w131Perceive) return;
        // Off: every avatar back to its pre-attach orphan faction (mode 0).
        var done = _w131FactionDone.ToArray();
        _w131FactionDone.Clear(); _w131FactionTriedMs.Clear();
        _ = Task.Run(async () =>
        {
            foreach (var (key, soul) in done)
            {
                var (ok, _) = await _combat.Wo131FactionAsync(soul, 0);
                Console.WriteLine($"MP-WO131 avatar {key} -> faction detached (mp_avatar_perceive off): {(ok ? "ok" : "FAILED")}");
            }
        });
    }
}
