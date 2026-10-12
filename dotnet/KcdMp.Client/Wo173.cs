// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;

namespace KcdMp.Client;

/// <summary>WO-173: the DLL's answer to a real hit (native wo173.h op 18).</summary>
public readonly record struct Wo173Hit(bool Ok, byte Reason, byte Applied, byte Outcome, byte Fallback, byte React, byte Steps, ulong AttackerWuid,
                                       ulong VictimWuid, uint Seq, float StApplied, float HpApplied)
{
    public const byte AppliedNone = 0, AppliedEngine = 1, AppliedRaw = 2;
    public bool ByEngine => Applied == AppliedEngine;
    public bool AppliedAny => Applied is AppliedEngine or AppliedRaw;

    /// <summary>The 33-byte answer; anything shorter (an older DLL's "bad request" carries no payload) -> Applied none, Ok false.</summary>
    public static Wo173Hit? TryDecode(bool ok, byte reason, byte[] p)
    {
        if (!ok || p.Length < 33) return new Wo173Hit(false, reason, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return new Wo173Hit(true, reason, p[0], p[1], p[2], p[3], p[4], BinaryPrimitives.ReadUInt64LittleEndian(p.AsSpan(5)),
                            BinaryPrimitives.ReadUInt64LittleEndian(p.AsSpan(13)), BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(21)),
                            BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(25)), BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(29)));
    }
}

/// <summary>WO-173 (docs/WO-173-findings.md): the pure rules of 0.49.0's agent half. The bridge is GameBridge.Wo173.cs.</summary>
public static class Wo173Rules
{
    /// <summary>
    /// mp_real_hits. WO-173 shipped it OFF (its public-release rule: the block-rate check could not be staged unattended, findings C1);
    /// WO-174 D (0.49.1, for the maintainer and one partner to test) is ON by the maintainer's choice. The capped raw path stays the per-blow fallback.
    /// </summary>
    // On since 0.49.1 (WO-174 D). WO-176 B1 could not stage its block-rate check (a camp bandit never fought the partner's figure: the
    // figure's body is a villager's soul to the engine); the maintainer kept it on for 0.50.0. The capped raw fallback stays per blow.
    public const bool DefaultRealHits = true;

    /// <summary>The joiner's last swing row is the one a hit belongs to when it is at most this old (a swing's start + hit is under 1.3 s).</summary>
    public const long RowFreshMs = 2_500;

    /// <summary>C2: the host's NPCs a joiner hit (or that fought a figure) are polled for down / up this long after the last such event.</summary>
    public const long DownWatchMs = 120_000;
    public const int DownPollMs = 100;   // the get-up reaches the joiner within 0.5 s (receipt to on its feet: 0.32-0.38 s live)
    public const int DownPollMax = 32;
    public const long DownRepeatMs = 3_000;

    /// <summary>The native outcome byte (wo165_rules.h Outcome) -> the wire's HitVerdict.</summary>
    public static HitVerdict VerdictOf(byte outcome) => outcome switch
    {
        1 => HitVerdict.Hit, 2 => HitVerdict.Blocked, 3 => HitVerdict.PerfectBlock, 4 => HitVerdict.Broken, _ => HitVerdict.None,
    };

    public static string FallbackName(byte f) => f switch
    {
        0 => "-", 1 => "mp_real_hits-off", 2 => "switched-off-by-a-fault", 3 => "processor-not-armed", 4 => "no-swing-row", 5 => "no-swing-window",
        6 => "no-victim", 7 => "victim-dead", 8 => "replay-refused", 9 => "engine-filtered", 10 => "engine-core-not-run",
        11 => "arranged-fight",   // WO-176 (A1): neither path ran -- the victim is in a quest's arranged fight
        _ => $"f{f}",
    };

    public static string ReactName(byte r) => r switch
    {
        0 => "-", 1 => "queued", 2 => "own-action", 3 => "victim-cannot-react", 4 => "no-reaction-found", 5 => "same-group-skipped",
        6 => "director-refused", 7 => "not-armed", 8 => "fault", 9 => "off", _ => $"r{r}",
    };

    /// <summary>The Lua's "w173_cfg key=value ..." event.</summary>
    public static IEnumerable<(string Key, string Value)> ParseCfg(string? arg)
    {
        foreach (var part in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || eq == part.Length - 1) continue;
            string k = part[..eq], v = part[(eq + 1)..];
            if (k is "real_hits" or "threat" or "react" && v is "on" or "off") yield return (k, v);
        }
    }

    /// <summary>
    /// The joiner's swing a hit belongs to: the last Attack row of that ghost, if fresh. The fields are WO-166's (the row's attack type and
    /// zone, the right hand, strength 1.0); null = no row (the hit then goes the capped raw way, fallback=no-swing-row).
    /// </summary>
    public static Wo166Rules.Strike? FieldsFor(ActionRowCatalog.Row? row, long rowAtMs, long nowMs)
    {
        if (row is not { } r || nowMs - rowAtMs > RowFreshMs || nowMs < rowAtMs) return null;
        return Wo166Rules.StrikeFor(r);
    }

    /// <summary>C2: what the joiner's copy plays for the host's outcome of his blow (the reaction itself comes as the host NPC's own NpcHit row).</summary>
    public enum CopyShow { Nothing, HitReaction, Block, PerfectBlock, BlockBroken }

    public static CopyShow ShowFor(HitVerdict v) => v switch
    {
        HitVerdict.Hit => CopyShow.HitReaction,
        HitVerdict.Blocked or HitVerdict.Parried => CopyShow.Block,
        HitVerdict.PerfectBlock => CopyShow.PerfectBlock,
        HitVerdict.Broken => CopyShow.BlockBroken,
        _ => CopyShow.Nothing,
    };

    /// <summary>C2: one NPC's down state report rule (wo173_rules.h down_should_send): a change at once, a held knock-out every 3 s.</summary>
    public sealed class DownTrack
    {
        public byte State;
        public long SentMs = long.MinValue / 2;
        public bool Known;

        public bool ShouldSend(byte now, long nowMs)
        {
            if (!Known || now != State) { Known = true; State = now; SentMs = nowMs; return true; }
            if (now == 1 && nowMs - SentMs >= DownRepeatMs) { SentMs = nowMs; return true; }   // a knock-out repeats; a death is said once
            return false;
        }
    }

    /// <summary>The NPCs to poll: those touched within the watch window, newest first, at most DownPollMax, wire-safe names only.</summary>
    public static List<string> DownCandidates(IEnumerable<KeyValuePair<string, long>> touched, long nowMs) =>
        touched.Where(kv => nowMs - kv.Value <= DownWatchMs && W164Text.IsNpcName(kv.Key))
               .OrderByDescending(kv => kv.Value).Take(DownPollMax).Select(kv => kv.Key).ToList();
}
