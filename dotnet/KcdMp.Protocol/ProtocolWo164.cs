// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-164 -- the mark snapshot and the torch side-channel (docs/WO-164-findings.md).
//
// One message rides the WO-123 join channel (same header, same routing, a row in Protocol.JoinWire; "Either": a joiner's
// goes to the host (target 0xFF), the host's to one joiner by id -- the relay needs no code of its own):
//
//   type up/down  name   up body (after [target:1][joinId:4])
//   0x74 / 0x75   W164   [kind:1][tok:4][text:1..W164TextMax]     (the LootMsg shape)
//
// Kinds (APPEND-ONLY):
//   1 MarkPing  "<markId>"       a tester pressed "Something's wrong here" (mark_odd) on the sender: the receiver writes its
//                                own MP-MARK-SNAP block under the same mark id (the two blocks join by mark=, not by clock)
//   2 Torch     "<ghost> 1|0"    the sender's torch is out (1) or away (0): sent on every edge and every 10 s while out.
//                                The state block's bit 0x20 (WO-136) still rides; this is the reliable copy (the 0.47.0
//                                session: the host's torch never reached the joiner through the block, the joiner's did)
//   3 Escort    "<npc> 1|0"      a joiner: a quest NPC follows this player in its own world (the quest asked it to) -- the host
//                                walks its NPC behind the joiner's figure until 0 (or the quest's end step, or 15 minutes)
//   4 HitOutcome "<hid> <outcome> <hp> <st>"   WO-165: a joiner's own engine decided the host's verdict <hid> (the replay, wo165.h):
//                                outcome hit|blocked|pb|broken, the health / stamina it took (measured; -1 = not separable).
//                                The host logs it beside its own verdict (WO165-OUTCOME) and counts agreement
//   5 Weather   "<rain> <profile|->"   WO-166 W1: the host's live weather -- its game's rain intensity (0..1, the engine's own computed
//                                value) and its time-of-day profile when known; sent on a change and every 60 s. The joiner holds
//                                its own rain at the host's (the engine's wh_env_RainIntensityOverride) while the session lasts
//
// An older peer never sees these (a mixed release is refused at the relay, WO-110 R9); a receiver that meets an unknown
// kind ignores it (counted). No protocol bump.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte W164Up = 0x74, W164Down = 0x75;   // WO-164

    public const int W164TextMax = 80;

    // ---- kinds (APPEND-ONLY) ----
    public const byte W164MarkPing = 1, W164Torch = 2, W164Escort = 3, W164HitOutcome = 4, W164Weather = 5;

    /// <summary>While the torch is out, the side-channel repeats it this often (a lost edge recovers).</summary>
    public const int W164TorchRepeatMs = 10_000;

    public static string W164KindName(byte k) => k switch
    {
        W164MarkPing => "mark-ping", W164Torch => "torch", W164Escort => "escort", W164HitOutcome => "hit-outcome", W164Weather => "weather", _ => $"unknown-{k}",
    };
}

/// <summary>WO-164: the text rules of the W164 message (pure; Wo164Tests pins them).</summary>
public static class W164Text
{
    /// <summary>A mark id: 6..16 characters of [a-z0-9] (no name, no path, no clock text).</summary>
    public static bool IsMarkId(string? s)
    {
        if (s is null || s.Length < 6 || s.Length > 16) return false;
        foreach (char c in s) if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9')) return false;
        return true;
    }

    /// <summary>"&lt;ghost&gt; 1|0" -> the ghost id and the torch state.</summary>
    public static bool TryParseTorch(string? text, out byte ghost, out bool on)
    {
        ghost = 0; on = false;
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 2 || !byte.TryParse(f[0], out ghost) || f[1] is not ("1" or "0")) return false;
        on = f[1] == "1";
        return true;
    }

    public static string Torch(byte ghost, bool on) => $"{ghost} {(on ? 1 : 0)}";

    public static string Escort(string npc, bool on) => $"{npc} {(on ? 1 : 0)}";

    /// <summary>WO-166 W1: "&lt;rain&gt; &lt;profile|-&gt;" -- the host's rain (0..1, two decimals) and its time-of-day profile name.</summary>
    public static string Weather(float rain, string? profile)
    {
        float r = float.IsFinite(rain) ? Math.Clamp(rain, 0f, 1f) : 0f;
        string p = !string.IsNullOrEmpty(profile) && IsProfileName(profile) ? profile : "-";
        return FormattableString.Invariant($"{r:F2} {p}");
    }

    /// <summary>A time-of-day profile name as the game's tables spell them: [A-Za-z0-9_], 1..48.</summary>
    public static bool IsProfileName(string s)
    {
        if (s.Length is 0 or > 48) return false;
        foreach (char c in s) if (!(c is >= 'a' and <= 'z' || c is >= 'A' and <= 'Z' || c is >= '0' and <= '9' || c == '_')) return false;
        return true;
    }

    public static bool TryParseWeather(string? text, out float rain, out string? profile)
    {
        rain = 0; profile = null;
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 2 || !float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rain)) return false;
        if (!float.IsFinite(rain) || rain < 0f || rain > 1f) return false;
        if (f[1] != "-" && !IsProfileName(f[1])) return false;
        profile = f[1] == "-" ? null : f[1];
        return true;
    }

    /// <summary>WO-165: "&lt;hid&gt; &lt;outcome&gt; &lt;hp&gt; &lt;st&gt;" -- the joiner's engine's outcome of the host's verdict hid.</summary>
    public static string HitOutcome(uint hid, HitVerdict outcome, float hp, float st) =>
        FormattableString.Invariant($"{hid} {HitVerdictMsg.VerdictName(outcome)} {Clamp(hp):F1} {Clamp(st):F1}");

    private static float Clamp(float v) => !float.IsFinite(v) || v < 0 ? -1f : Math.Min(v, Protocol.HitVerdictStatMax);

    public static bool TryParseHitOutcome(string? text, out uint hid, out HitVerdict outcome, out float hp, out float st)
    {
        hid = 0; outcome = HitVerdict.None; hp = st = 0;
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (f.Length != 4 || !uint.TryParse(f[0], System.Globalization.NumberStyles.None, inv, out hid) || hid == 0) return false;
        if (!HitVerdictMsg.TryParseVerdict(f[1], out outcome) || outcome is HitVerdict.Parried or HitVerdict.Missed) return false;
        if (!float.TryParse(f[2], System.Globalization.NumberStyles.Float, inv, out hp) || !float.TryParse(f[3], System.Globalization.NumberStyles.Float, inv, out st)) return false;
        bool ok(float v) => float.IsFinite(v) && (v == -1f || (v >= 0 && v <= Protocol.HitVerdictStatMax));
        return ok(hp) && ok(st);
    }

    /// <summary>"&lt;npc&gt; 1|0" -> the NPC (an authored entity name) and on/off.</summary>
    public static bool TryParseEscort(string? text, out string npc, out bool on)
    {
        npc = ""; on = false;
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 2 || f[1] is not ("1" or "0") || f[0].Length is 0 or > 64) return false;
        foreach (char c in f[0]) if (!(char.IsAsciiLetterOrDigit(c) || c == '_')) return false;
        npc = f[0]; on = f[1] == "1";
        return true;
    }
}
