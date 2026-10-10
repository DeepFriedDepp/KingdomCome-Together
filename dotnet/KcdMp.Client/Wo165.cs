// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-165 (docs/WO-165-findings.md): the engine-free rules of the victim-decides replay (C2), the attacker's recoil (C3) and the
/// switches. GameBridge.Wo165.cs is the bridge; native/KCDMP/wo165.* the DLL half; Wo165Tests pins these.
/// </summary>
public static class Wo165Rules
{
    /// <summary>
    /// The switches and their defaults: all ON, each with a console switch to turn it off (the maintainer, 2026-10-09: testing needs a real
    /// partner, so new pieces ship on). P6 passed live; C2 (P3: a held block is not honoured -- a blocking player is referred to the host's
    /// verdict, flag 0x10) and C3 (P8: dispatched, look not judged) are not proven with two players.
    /// </summary>
    // WO-166: mp_victim_decides OFF again -- the WO's own line ("stays off: its proof needs a human holding block"); with C3 the copy is
    // Striking on the joiner's screen and the host decides the outcome (the local engine's blow is put back)
    public const bool DefaultHostLock = true, DefaultVictimDecides = false, DefaultBlockRecoil = true;

    /// <summary>The replay's flags: skip the engine's repeat filter (our verdicts are deduped by id), refer a blocking victim to the host's verdict.</summary>
    public const byte ReplayFlags = 0x01 | 0x10;

    /// <summary>How long after the replay its damage is read back (the slot hook watches the victim 0.6 s).</summary>
    public const int DamageReadMs = 800;

    /// <summary>
    /// C2's preconditions on the joiner, in order; null = the replay may run, else the reason it falls back to the host's verdict
    /// (logged as fallback=&lt;why&gt;). Every one is a fact the agent holds when the verdict arrives.
    /// </summary>
    public static string? ReplayRefusal(bool switchOn, HitVerdictMsg m, bool isCopyHere, bool engaged, bool rowKnown, bool playerDownOrWaking)
    {
        if (!switchOn) return "off";
        if (m.Missile) return "missile";
        if (m.NoAttacker || m.Attacker.Length == 0) return "no-attacker";
        if (!isCopyHere) return "no-copy";
        if (!engaged) return "copy-not-engaged";
        if (!rowKnown) return "no-row";
        if (playerDownOrWaking) return "player-down";
        return null;
    }

    /// <summary>The engine's word from the DLL's reply ("seq=&lt;n&gt; engine=&lt;x&gt; ..." or "refused=&lt;why&gt;").</summary>
    public static bool TryParseReplay(string? text, out uint seq, out string engine, out string refused)
    {
        seq = 0; engine = ""; refused = "";
        if (string.IsNullOrEmpty(text)) { refused = "no-answer"; return false; }
        if (text.StartsWith("refused=", StringComparison.Ordinal)) { refused = text[8..].Split(' ')[0]; return false; }
        foreach (var kv in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (kv.StartsWith("seq=", StringComparison.Ordinal)) uint.TryParse(kv[4..], out seq);
            else if (kv.StartsWith("engine=", StringComparison.Ordinal)) engine = kv[7..];
        }
        if (seq == 0 || engine.Length == 0) { refused = "unreadable-answer"; return false; }
        return true;
    }

    /// <summary>The engine's outcome as a verdict; None = the engine judged nothing (filtered, the core never ran): the host's verdict applies.</summary>
    public static HitVerdict Outcome(string engine) => engine switch
    {
        "hit" => HitVerdict.Hit, "blocked" => HitVerdict.Blocked, "pb" => HitVerdict.PerfectBlock, "broken" => HitVerdict.Broken,
        _ => HitVerdict.None,
    };

    public enum DamageCall { Engine, EngineMerged, FallbackApplyHost }

    /// <summary>
    /// After the read-back: who applied the blow. state 2 = measured, 3 = merged into an earlier replay's measure (applied by the engine,
    /// not separable), anything else = unknown. Damage is never dropped silently: a "hit" the engine applied nothing for, or a blow whose
    /// measure never came, falls back to the host's verdict -- but only when the engine measurably applied NOTHING (no blow twice).
    /// </summary>
    public static DamageCall Decide(HitVerdict outcome, byte state, float hp, float st)
    {
        if (state == 3) return DamageCall.EngineMerged;
        if (state != 2) return outcome == HitVerdict.Hit || outcome == HitVerdict.Broken ? DamageCall.FallbackApplyHost : DamageCall.Engine;
        bool nothing = hp <= 0.01f && st <= 0.01f;
        if (nothing && (outcome == HitVerdict.Hit || outcome == HitVerdict.Broken)) return DamageCall.FallbackApplyHost;
        return DamageCall.Engine;
    }

    /// <summary>C3: does this outcome make the attacker recoil, and which kind (perfect block = the PB row).</summary>
    public static bool Recoils(HitVerdict v, out bool perfect)
    {
        perfect = v == HitVerdict.PerfectBlock;
        return v is HitVerdict.Blocked or HitVerdict.PerfectBlock;
    }

    /// <summary>The weapon tags of a row ("l_..." / "r_..."), the part a failed-attack row is chosen by (WO-162 Q4).</summary>
    public static IReadOnlyList<string> WeaponTags(string tags) =>
        tags.Split('+', StringSplitOptions.RemoveEmptyEntries).Where(t => t.StartsWith("l_", StringComparison.Ordinal) || t.StartsWith("r_", StringComparison.Ordinal)).ToArray();

    /// <summary>"w165_cfg key=on|off ...": the switches it names (unknown keys and values are ignored).</summary>
    public static Dictionary<string, bool> ParseCfg(string? arg)
    {
        var d = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            string k = kv[..eq], v = kv[(eq + 1)..];
            if (v is not ("on" or "off") || k is not ("host_lock" or "victim_decides" or "block_recoil")) continue;
            d[k] = v == "on";
        }
        return d;
    }
}

/// <summary>WO-165: the minute line's counters.</summary>
public sealed class Wo165Stats
{
    private long _replays, _engine, _merged, _fallback, _hit, _blocked, _pb, _broken, _agree, _disagree, _recoilPlayed, _recoilNone, _outcomeIn;
    private readonly Dictionary<string, long> _why = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public void Replay(HitVerdict o, Wo165Rules.DamageCall call, HitVerdict hostSaid)
    {
        Interlocked.Increment(ref _replays);
        switch (call) { case Wo165Rules.DamageCall.Engine: Interlocked.Increment(ref _engine); break; case Wo165Rules.DamageCall.EngineMerged: Interlocked.Increment(ref _merged); break; default: Interlocked.Increment(ref _fallback); break; }
        switch (o) { case HitVerdict.Hit: Interlocked.Increment(ref _hit); break; case HitVerdict.Blocked: Interlocked.Increment(ref _blocked); break; case HitVerdict.PerfectBlock: Interlocked.Increment(ref _pb); break; case HitVerdict.Broken: Interlocked.Increment(ref _broken); break; }
        Interlocked.Increment(ref (o == hostSaid || (o is HitVerdict.Blocked or HitVerdict.PerfectBlock && hostSaid == HitVerdict.Blocked)) ? ref _agree : ref _disagree);
    }
    public void Fallback(string why) { lock (_lock) _why[why] = _why.TryGetValue(why, out long n) ? n + 1 : 1; }
    public void Recoil(bool played) => Interlocked.Increment(ref played ? ref _recoilPlayed : ref _recoilNone);
    public void OutcomeIn() => Interlocked.Increment(ref _outcomeIn);

    public bool Quiet => Interlocked.Read(ref _replays) == 0 && Interlocked.Read(ref _recoilPlayed) == 0 && Interlocked.Read(ref _recoilNone) == 0 &&
                         Interlocked.Read(ref _outcomeIn) == 0 && _why.Count == 0;

    public string Line()
    {
        string why;
        lock (_lock) why = _why.Count == 0 ? "-" : string.Join(",", _why.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}:{k.Value}"));
        return $"MP-WO165-STATS replays={_replays} applied_engine={_engine} engine_merged={_merged} fallback_apply_host={_fallback} hit={_hit} blocked={_blocked} pb={_pb} broken={_broken} " +
               $"agree_host={_agree} disagree_host={_disagree} not_replayed=[{why}] recoil_played={_recoilPlayed} recoil_none={_recoilNone} outcomes_in={_outcomeIn}";
    }
}
