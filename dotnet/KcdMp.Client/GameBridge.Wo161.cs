// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-161: the hit verdict -- the bookkeeping an NPC's hit on a player never had (docs/WO-161-findings.md). The rules are
// Wo161Rules and its small classes in Wo161.cs; the message is HitVerdictMsg (ProtocolWo161.cs, 0x72/0x73).
//
// A joiner's copy of an enemy is a puppet and fires no hit event on the joiner, so the victim's engine cannot decide the
// hit: the HOST's engine still does, against the avatar's replayed guard (docs/WO-157-findings.md 3b.1). What changes:
//
//   HOST   every swing it sends for an NPC (the NpcAttack row) gets an id in a ledger, per NPC. An NPC's hit on an
//          avatar (the DLL's 0x9A, or the Lua sampler's fallback) is judged hit or blocked, matched to the NPC's latest
//          swing (1.2 s), numbered, and sent as ONE message (0x72) -- not also as the bare 0x21: one path per hit.
//   VICTIM the verdict is applied once (its id is remembered 30 s), exactly as the 0.46.0 hit was (four-argument
//          TakeDamage, no knockdown, never while down or waking); damage is NEVER dropped for lacking a swing. The line
//          WO161-HIT says whether the swing was shown with it, or why it was not; MP-WO161-STATS counts them.
//
//   WO161-HIT victim=<ghost|me> by=<npc|-> sid=<n> hid=<n> verdict=<hit|blocked> dmg=<hp>/<st> dir=<zone> shown=yes|no|generic reason=<why>
//             <sent=verdict|fallback|no | applied=yes|no|dup>
//
// Reasons a hit had no shown swing: no-swing-captured (the host's DLL saw no attack row of that NPC at all: an animal's bite),
// swing-unmatched (WO-163 A3: rows exist, none fits the blow's own start+hit timing), row-not-received, row-not-played /
// row-refused-<why> (it came and the copy did not play it), row-stale, no-attacker (the fallback path names nobody), missile,
// legacy (a bare 0x22 from a peer without this path).
//
// WO-163 (A3): the host pairs a hit with the swing whose OWN start+hit lag (the row's table: attack_time_to_start +
// attack_time_to_hit) it matches +-0.35 s, not with "the newest swing inside 1.2 s"; the victim judges a verdict against the
// played row that lag fits. (A4): a blow with nothing shown (no-swing-captured / swing-unmatched / row-not-received / row-stale)
// is shown as one generic lunge on the copy before its damage -- shown=generic, reason keeps why it was needed.
public partial class GameBridge
{
    private readonly Wo161SwingLedger _w161Swings = new();
    private readonly Wo161HitIds _w161Ids = new();
    private readonly Wo161Dedupe _w161Dedupe = new();
    private readonly Wo161Stats _w161Stats = new();
    // the victim's half: when a row of an NPC came in, was played on its copy, or was refused (TickCount64)
    private readonly ConcurrentDictionary<string, long> _w161RowRecvAt = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Wo161PlayedLog> _w161Played = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Tag, long At)> _w161RowRefused = new(StringComparer.Ordinal);

    private void Wo161OnDisconnect()
    {
        _w161Swings.Clear();
        _w161Dedupe.Clear();
        Wo165OnDisconnect();
        Wo166OnDisconnect();
        _w161RowRecvAt.Clear();
        _w161Played.Clear();
        _w161RowRefused.Clear();
    }

    // ------------------------------------------------------------------ host

    /// <summary>Host: an NPC's swing row went out (OnLocalActionAsync): it gets its id.</summary>
    private void Wo161NoteSwingOut(string npc, Guid row)
    {
        if (npc.Length == 0) return;
        _w161Swings.Record(npc, row, Environment.TickCount64, Wo161LagOf(row));
    }

    /// <summary>WO-163 (A3): the lag from a row's start to the blow it causes, from the row's own table; unknown rows are never paired.</summary>
    private int Wo161LagOf(Guid row)
    {
        if (!_rowCatalog.IsCompletedSuccessfully || _rowCatalog.Result is not { } cat) return Wo161Rules.LagNone;   // no catalog loaded: the 0.46.5 window
        return cat.TryGet(row, out var r) ? (r.HitLagMs >= 0 ? r.HitLagMs : Wo161Rules.LagNone) : Wo161Rules.LagUnknownRow;
    }

    /// <summary>
    /// Host: an NPC's hit on <paramref name="ghost"/>'s avatar (the DLL's measurement, or the sampler's fallback with no
    /// attacker): judged, matched to the NPC's latest swing, numbered and sent as one verdict; on the old path when the
    /// verdict path is off or the connection has no join channel.
    /// </summary>
    private async Task Wo161SendHitAsync(byte ghost, float health, float stamina, string attacker, bool missile)
    {
        bool named = attacker.Length > 0 && CarryText.IsName(attacker);
        string by = named ? attacker : "-";
        if (health <= 0 && stamina <= 0)
        {
            _w161Stats.NoDamage();
            Console.WriteLine(FormattableString.Invariant($"WO161-HIT victim={ghost} by={by} sid=0 hid=0 verdict=none dmg=0.0/0.0 dir=- shown=no reason=no-damage sent=no"));
            return;
        }
        if (health < 0) health = 0;
        if (stamina < 0) stamina = 0;
        var verdict = Wo161Rules.Classify(health, stamina);
        var pair = named ? _w161Swings.Match(attacker, Environment.TickCount64) : default;
        var sw = named ? pair.Swing : null;
        byte zone = named && _w132LastSent.TryGetValue(attacker, out var ev) ? (byte)ev.State.AtkZone : (byte)0;
        string hostWhy = sw is not null ? "-" : !named ? "no-attacker" : missile ? "missile" : pair.Why;
        string how = "fallback";
        uint hid = 0;
        if (config.HitVerdictEnabled && _wo122Stream is not null)
        {
            hid = _w161Ids.Next();
            try
            {
                var msg = new HitVerdictMsg(hid, verdict, Wo161Rules.Flags(sw is not null, missile, !named), zone, sw?.Id ?? 0, health, stamina, named ? attacker : "");
                await WriteJoinAsync(msg.BuildUp(ghost));
                Wo165NoteVerdictSent(hid, verdict);   // WO-165: the joiner's engine outcome is logged against it
                _w161Stats.Out();
                how = "verdict";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MP-WO161 verdict {hid} to ghost {ghost} not sent ({ex.GetType().Name}: {ex.Message}) -- the 0.46.0 hit path instead");
            }
        }
        if (how == "fallback")
        {
            _w161Stats.Fallback();
            var send = _sendPlayerHit;
            if (send is not null) await send(ghost, health, stamina);
        }
        if (named) Wo166NoteStruck(ghost, attacker);   // WO-166 R1: who struck this figure (the respawn amnesty names them)
        Console.WriteLine(FormattableString.Invariant(
            $"WO161-HIT victim={ghost} by={by} sid={sw?.Id ?? 0} hid={hid} verdict={HitVerdictMsg.VerdictName(verdict)} dmg={health:F1}/{stamina:F1} dir={Wo161Zone(zone)} shown={(sw is not null ? "yes" : "no")} reason={hostWhy}{(sw is not null ? $" fit_ms={pair.ErrMs}" : "")} sent={how}"));
    }

    private static string Wo161Zone(byte z) => z == 0 ? "-" : ((WireZone)z).ToString().ToLowerInvariant();

    // ---------------------------------------------------------------- victim

    /// <summary>Victim: a swing row of <paramref name="npc"/> came in (before any check of it).</summary>
    private void Wo161NoteRowIn(string npc) => _w161RowRecvAt[npc] = Environment.TickCount64;

    /// <summary>Victim: that row was played on the copy.</summary>
    private void Wo161NoteRowPlayed(string npc, int lagMs = Wo161Rules.LagNone)
    {
        _w161Played.GetOrAdd(npc, _ => new Wo161PlayedLog()).Note(Environment.TickCount64, lagMs);
        _w161Stats.RowPlayed();
    }

    /// <summary>
    /// WO-163 (A4), victim: a blow with no swing of its own is shown as one generic lunge on the copy -- an animal's bite row, a
    /// man's unarmed punch (the catalog's own rows, chosen by content) -- before its damage is applied. True = it was played.
    /// </summary>
    private async Task<bool> Wo161ShowGenericAsync(string npc, CancellationToken ct)
    {
        if (!config.GenericSwingEnabled || !_npcRows || !_rowCatalog.IsCompletedSuccessfully || _rowCatalog.Result is not { } cat) return false;
        if (!_npcEntityIds.TryGetValue(npc, out uint neid)) return false;                 // not a puppet here: nothing to move
        bool animal = _w141Animals.ContainsKey(npc);
        if (animal && !_w141Bites) return false;                                           // mp_animal_attacks off: an animal's attack is not animated here
        var row = cat.GenericRow(animal);
        if (row is not { } g) { Console.WriteLine($"MP-WO163 generic swing: the catalog holds no {(animal ? "animal" : "unarmed")} row -- nothing shown"); return false; }
        _ = _combat.NpcHoldAsync(npc, 900, ct);
        var r = await _combat.GhostSwingForResultAsync(neid, g.Spec, ct);
        if (r.Ok)
        {
            _w161Stats.Generic();
            await ExecLuaAsync($"if KCD2MP_NpcNativeSwingHold then KCD2MP_NpcNativeSwingHold(\"{npc}\") end");
        }
        return r.Ok;
    }

    /// <summary>Victim: that row was not played, and why.</summary>
    private void Wo161NoteRowRefused(string npc, string tag) => _w161RowRefused[npc] = (tag, Environment.TickCount64);

    /// <summary>Victim: the host's verdict on a hit on this player (0x73).</summary>
    private async Task Wo161OnVerdictInAsync(byte src, uint hitId, byte[] body, CancellationToken ct)
    {
        if (!HitVerdictMsg.TryDecode(hitId, body, out var m))
        {
            _w161Stats.Malformed();
            Console.WriteLine($"WO161-HIT victim=me by=? hid={hitId} verdict=? refused=malformed len={body.Length} from=ghost {src} -- nothing applied");
            return;
        }
        // never drop damage: the relay lets only the damage authority send a verdict, so a machine that still thinks it is the
        // host when one arrives (a role change in flight) applies it all the same and says so in the line below
        long now = Environment.TickCount64;
        string by = m.Attacker.Length > 0 ? m.Attacker : "-";
        if (!_w161Dedupe.Accept(src, hitId, now))
        {
            _w161Stats.Dup();
            Console.WriteLine(FormattableString.Invariant($"WO161-HIT victim=me by={by} sid={m.SwingId} hid={hitId} verdict={HitVerdictMsg.VerdictName(m.Verdict)} dmg={m.Health:F1}/{m.Stamina:F1} applied=dup -- this id was applied already"));
            return;
        }
        long? played = null, recv = null; string? refused = null; bool fits = false;
        if (m.Attacker.Length > 0)
        {
            if (_w161Played.TryGetValue(m.Attacker, out var pl) && pl.Best(now, Wo161Rules.OneWayMs(_clockRttMedianMs)) is { } best) { played = best.Ago; fits = best.Fits; }   // WO-166 C2
            if (_w161RowRecvAt.TryGetValue(m.Attacker, out long ra)) recv = now - ra;
            if (_w161RowRefused.TryGetValue(m.Attacker, out var rf) && Math.Abs(now - rf.At) <= Wo161Rules.RowBeforeMs + Wo161Rules.RowAfterMs) refused = rf.Tag;
        }
        var (shown, reason) = Wo161Rules.Judge(m.SwingKnown, m.Missile, m.NoAttacker, played, recv, refused, fits);
        bool generic = false;
        if (Wo161Rules.WantsGenericSwing(shown, reason) && m.Attacker.Length > 0 && (m.Health > 0 || m.Stamina > 0))
            generic = await Wo161ShowGenericAsync(m.Attacker, ct);   // before the damage: the lunge is on the screen when it lands
        // a verdict of no damage (the reserved parried / missed) has nothing to apply; every other one is applied exactly as 0.46.0's hit was
        bool hasDamage = m.Health > 0 || m.Stamina > 0;
        // WO-165 C2 (mp_victim_decides, default off): this machine's engine decides the blow against this player's own guard; the engine
        // applies it, or -- when it applied nothing for a hit -- the host's verdict does. Never both (Wo165Rules.Decide).
        var c2 = hasDamage ? await Wo165VictimDecidesAsync(hitId, m, ct) : null;
        bool byEngine = c2 is { } e2 && e2.Call != Wo165Rules.DamageCall.FallbackApplyHost;
        bool applied = byEngine || (hasDamage && await ApplyPlayerHitAsync(m.Health, m.Stamina, ct));
        if (c2 is { } e3)
        {
            HitVerdict said = e3.Call == Wo165Rules.DamageCall.FallbackApplyHost ? m.Verdict : e3.Outcome;
            await Wo165SendOutcomeAsync(hitId, said, byEngine ? e3.Hp : m.Health, byEngine ? e3.St : m.Stamina);
        }
        // WO-165 C3 (mp_block_recoil, default off): a blocked blow bounces off -- the copy that swung plays its failed attack
        _ = Wo165RecoilAsync(m.Attacker, c2 is { } e4 && e4.Call != Wo165Rules.DamageCall.FallbackApplyHost ? e4.Outcome : m.Verdict, ct);
        _w161Stats.In(applied, m.Verdict, m.Health, m.Stamina, shown || generic, generic ? "generic" : reason);
        Console.WriteLine(FormattableString.Invariant(
            $"WO161-HIT victim=me by={by} sid={m.SwingId} hid={hitId} verdict={HitVerdictMsg.VerdictName(m.Verdict)} dmg={m.Health:F1}/{m.Stamina:F1} dir={Wo161Zone(m.Zone)} shown={(shown ? "yes" : generic ? "generic" : "no")} reason={reason} applied={(byEngine ? "engine" : applied ? "yes" : hasDamage ? "no" : "none")}{(_isDamageAuthority ? " note=this-machine-thinks-it-is-the-host" : "")}"));
    }

    /// <summary>Victim: a bare 0.46.0 hit (0x22) -- a peer without the verdict path, or the host's fallback. Counted, never lost.</summary>
    private async Task Wo161OnLegacyHitInAsync(float health, float stamina, CancellationToken ct)
    {
        bool applied = await ApplyPlayerHitAsync(health, stamina, ct);
        _w161Stats.In(applied, Wo161Rules.Classify(health, stamina), health, stamina, false, "legacy");
        Console.WriteLine(FormattableString.Invariant(
            $"WO161-HIT victim=me by=- sid=0 hid=0 verdict={HitVerdictMsg.VerdictName(Wo161Rules.Classify(health, stamina))} dmg={health:F1}/{stamina:F1} dir=- shown=no reason=legacy applied={(applied ? "yes" : "no")}"));
    }

    /// <summary>Both roles: the minute line (only when there was something to say).</summary>
    private void Wo161WriteStats()
    {
        if (!_w161Stats.Quiet) Console.WriteLine(_w161Stats.Line());
        Wo165WriteStats();
    }
}
