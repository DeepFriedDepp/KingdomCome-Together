// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-165 (docs/WO-165-findings.md): shared combat with one human -- the bridge. The rules are Wo165Rules (Wo165.cs).
//
//   C1 mp_host_lock     (host, default ON -- P6 passed live): the DLL sets the host's explicit hostile pair with an NPC that fights a
//                       partner's avatar while the host stands near and faces it, so the engine's own lock-on can pick it
//                       (native wo165.cpp lock_tick; this side only pushes the switch: op 8).
//   C2 mp_victim_decides (joiner, default OFF since WO-166 -- the WO: its proof needs a human holding block; P3 live: the engine honours a block only in ParryInPlace, which a held block enters
//                       only against an attacker the engine sees Striking; a copy never does, so a player holding block is referred to
//                       the host's verdict, fallback=victim-blocking): a host verdict is
//                       replayed through the engine's hit processor against this player; the engine's outcome replaces the host's,
//                       the damage is applied once by the engine (applied=engine) or, when it applied nothing, by the host's verdict.
//                       Every precondition missing = the WO-161 path, fallback=<why>.
//   C3 mp_block_recoil  (joiner, default ON -- P8 dispatched, its look not judged yet): a blocked / perfectly blocked blow
//                       plays the matching failed-attack row on the copy that swung (none for unarmed and other weapons, WO-162 Q4).
//
//   WO165-REPLAY hid=<n> by=<npc> engine=hit|blocked|pb|broken dmg=<hp>/<st> host_said=<v> fallback=-|<why>
//   WO165-RECOIL npc=<npc> outcome=<v> recoil=<spec>|none why=<w>
//   WO165-OUTCOME (host) hid=<n> joiner=<g> engine=<v> dmg=<hp>/<st> host_said=<v>
public partial class GameBridge
{
    private volatile bool _w165HostLock = Wo165Rules.DefaultHostLock, _w165VictimDecides = Wo165Rules.DefaultVictimDecides,
                          _w165BlockRecoil = Wo165Rules.DefaultBlockRecoil;
    private readonly ConcurrentDictionary<string, Guid> _w165LastRow = new(StringComparer.Ordinal);   // joiner: the row last played on each copy
    private readonly ConcurrentDictionary<uint, HitVerdict> _w165Sent = new();                        // host: the verdicts it sent, by hit id
    private readonly Wo165Stats _w165Stats = new();

    private void Wo165OnDisconnect() { _w165LastRow.Clear(); _w165Sent.Clear(); }

    private void Wo165OnEvent(string name, string? arg)
    {
        if (name != "w165_cfg") return;
        foreach (var (k, on) in Wo165Rules.ParseCfg(arg))
        {
            switch (k)
            {
                case "host_lock":
                    _w165HostLock = on;
                    _ = Wo165PushHostLockAsync();
                    break;
                case "victim_decides": _w165VictimDecides = on; break;
                case "block_recoil": _w165BlockRecoil = on; break;
            }
        }
        Console.WriteLine($"MP-WO165 cfg host_lock={(_w165HostLock ? "on" : "off")} victim_decides={(_w165VictimDecides ? "on" : "off")} block_recoil={(_w165BlockRecoil ? "on" : "off")}");
    }

    private async Task Wo165PushHostLockAsync()
    {
        try
        {
            var r = await _combat.Wo163Async(8, [(byte)(_w165HostLock ? 1 : 0)]);
            Console.WriteLine($"MP-WO165 mp_host_lock {(_w165HostLock ? "on" : "off")} -> DLL {(r is { Ok: true } x && x.Payload.Length > 0 ? (x.Payload[0] != 0 ? "on" : "off (or switched off by a fault: see WO165-LOCK)") : "no answer")}");
        }
        catch (Exception ex) { Console.WriteLine($"MP-WO165 mp_host_lock not pushed ({ex.GetType().Name}: {ex.Message})"); }
    }

    /// <summary>Joiner: a host row was played on this copy (the row the replay's attack fields come from, and C3's weapon tags).</summary>
    private void Wo165NoteRowPlayed(string npc, Guid row) => _w165LastRow[npc] = row;

    /// <summary>Host: a verdict went out (for the joiner's outcome line).</summary>
    private void Wo165NoteVerdictSent(uint hid, HitVerdict v)
    {
        if (hid == 0) return;
        _w165Sent[hid] = v;
        if (_w165Sent.Count > 512) foreach (var k in _w165Sent.Keys.OrderBy(k => k).Take(256).ToArray()) _w165Sent.TryRemove(k, out _);
    }

    /// <summary>
    /// C2, joiner: the host's verdict <paramref name="m"/> replayed through this engine. Null = not replayed (the caller applies the host's
    /// verdict as WO-161 does; the reason is logged); else who applied the blow and the engine's outcome.
    /// </summary>
    private async Task<(Wo165Rules.DamageCall Call, HitVerdict Outcome, float Hp, float St)?> Wo165VictimDecidesAsync(uint hitId, HitVerdictMsg m, CancellationToken ct)
    {
        string by = m.Attacker.Length > 0 ? m.Attacker : "-";
        ActionRowCatalog? cat = _rowCatalog.IsCompletedSuccessfully ? _rowCatalog.Result : null;
        ActionRowCatalog.Row row = default;
        bool rowKnown = m.Attacker.Length > 0 && cat is not null && _w165LastRow.TryGetValue(m.Attacker, out var g) && cat.TryGet(g, out row) && row.AttackType >= 0;
        string? why = Wo165Rules.ReplayRefusal(_w165VictimDecides, m, m.Attacker.Length > 0 && _npcEntityIds.ContainsKey(m.Attacker),
                                               m.Attacker.Length > 0 && _w132Engaged.ContainsKey(m.Attacker), rowKnown,
                                               _w132LocalGate.Check(DateTime.UtcNow) != LifeGate.Why.Open);
        if (why is not null)
        {
            if (why != "off") { _w165Stats.Fallback(why); Console.WriteLine($"WO165-REPLAY hid={hitId} by={by} engine=- host_said={HitVerdictMsg.VerdictName(m.Verdict)} fallback={why}"); }
            return null;
        }
        int zone = row.Zone >= 0 ? row.Zone : m.Zone != 0 ? Protocol.ZoneToTableId((WireZone)m.Zone) : 2;   // the table's default zone (upper right) when neither says
        var r = await _combat.Wo165ReplayAsync(m.Attacker, null, (sbyte)row.AttackType, (sbyte)zone, 1, 1.0f, Wo165Rules.ReplayFlags, ct);
        if (!Wo165Rules.TryParseReplay(r?.Text, out uint seq, out string engine, out string refused) || Wo165Rules.Outcome(engine) == HitVerdict.None)
        {
            string fb = refused.Length > 0 ? refused : "engine-" + engine;
            _w165Stats.Fallback(fb);
            if (refused is "switched-off" or "fault") await Wo165SayOnceAsync("The victim-decides replay was switched off by a fault -- the host's verdicts apply as before.");
            Console.WriteLine($"WO165-REPLAY hid={hitId} by={by} engine={(engine.Length > 0 ? engine : "-")} host_said={HitVerdictMsg.VerdictName(m.Verdict)} fallback={fb}");
            return null;
        }
        var outcome = Wo165Rules.Outcome(engine);
        await Task.Delay(Wo165Rules.DamageReadMs, ct);
        var d = await _combat.Wo165ReplayDamageAsync(seq, ct);
        var call = Wo165Rules.Decide(outcome, d?.State ?? 0, d?.Health ?? 0, d?.Stamina ?? 0);
        _w165Stats.Replay(outcome, call, m.Verdict);
        float hp = d is { State: 2 } x ? x.Health : -1, st = d is { State: 2 } y ? y.Stamina : -1;
        Console.WriteLine(FormattableString.Invariant(
            $"WO165-REPLAY hid={hitId} by={by} engine={engine} dmg={(hp >= 0 ? $"{hp:F1}/{st:F1}" : "merged")} host_said={HitVerdictMsg.VerdictName(m.Verdict)} fallback={(call == Wo165Rules.DamageCall.FallbackApplyHost ? "engine-applied-nothing" : "-")} seq={seq}"));
        return (call, outcome, hp, st);
    }

    private int _w165Said;
    private async Task Wo165SayOnceAsync(string text)
    {
        if (Interlocked.Exchange(ref _w165Said, 1) != 0) return;
        Console.WriteLine($"MP-WO165 {text}");
        await ExecLuaAsync($"if KCD2MP_W165Say then KCD2MP_W165Say(\"{EscapeLua(text)}\") end");
    }

    /// <summary>C2, joiner: the engine's outcome goes to the host (W164 kind 4) so the host's line shows what the victim's engine said.</summary>
    private Task Wo165SendOutcomeAsync(uint hitId, HitVerdict outcome, float hp, float st) =>
        W164SendAsync(Protocol.JoinTargetHost, Protocol.W164HitOutcome, W164Text.HitOutcome(hitId, outcome, hp, st));

    /// <summary>Host: a joiner's engine outcome.</summary>
    private void Wo165OnOutcomeIn(byte src, string text)
    {
        if (!W164Text.TryParseHitOutcome(text, out uint hid, out var v, out float hp, out float st)) { Interlocked.Increment(ref _w164Malformed); return; }
        _w165Stats.OutcomeIn();
        string said = _w165Sent.TryGetValue(hid, out var hv) ? HitVerdictMsg.VerdictName(hv) : "unknown";
        Console.WriteLine(FormattableString.Invariant($"WO165-OUTCOME hid={hid} joiner={src} engine={HitVerdictMsg.VerdictName(v)} dmg={(hp >= 0 ? $"{hp:F1}/{st:F1}" : "merged")} host_said={said}"));
    }

    /// <summary>C3, joiner: a blocked / perfectly blocked blow makes the copy that swung recoil (its swing row's weapon decides the row).</summary>
    private async Task Wo165RecoilAsync(string npc, HitVerdict outcome, CancellationToken ct)
    {
        if (!_w165BlockRecoil || npc.Length == 0 || !Wo165Rules.Recoils(outcome, out bool perfect)) return;
        ActionRowCatalog? cat = _rowCatalog.IsCompletedSuccessfully ? _rowCatalog.Result : null;
        string why;
        ActionRowCatalog.Row? fr = null;
        if (cat is null) why = "no-catalog";
        else if (!_w165LastRow.TryGetValue(npc, out var g) || !cat.TryGet(g, out var swing)) why = "no-swing-row";
        else if ((fr = cat.FailedAttackRow(swing, perfect)) is null) why = "no-row-for-this-weapon";
        else if (!_npcEntityIds.TryGetValue(npc, out uint neid)) { why = "not-a-copy-here"; fr = null; }
        else
        {
            var r = await _combat.GhostSwingForResultAsync(neid, fr.Value.Spec, ct);
            why = r.ReasonTag;
            if (!r.Ok) fr = null;
        }
        _w165Stats.Recoil(fr is not null);
        Console.WriteLine($"WO165-RECOIL npc={npc} outcome={HitVerdictMsg.VerdictName(outcome)} recoil={(fr is { } x ? $"\"{x.Spec}\"" : "none")} why={why}");
    }

    private void Wo165WriteStats()
    {
        if (!_w165Stats.Quiet) Console.WriteLine(_w165Stats.Line());
    }
}
