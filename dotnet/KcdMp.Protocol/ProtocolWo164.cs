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
//   6 Travel    WO-167 FT2: one shared fast travel. "<verb> <seq> ...":
//                 r <seq> <dest>          joiner -> host: this player picked a destination on the travel map (asked, never run here)
//                 n <seq> <why>           host -> joiner: the host's world cannot travel now (combat, dialogue, off ...): nothing moves
//                 s <seq> <dest>          host -> joiners: the host's travel started (its own pick, a joiner's, or a quest's: dest "-")
//                 e <seq> <x,y,z> <0|1>   host -> joiners: the host's travel ended there (1 = interrupted midway, a road encounter)
//               dest: "@<name>" (a travel point the engine's wh_pl_FastTravelTo knows by name), "<x>,<y>,<z>" (a world position) or "-"
//   7 RealHit   "<npc> <verdict> <react> <how> <hp> <st>"   WO-173 C2, host -> the joiner whose blow it was: the host's engine's outcome
//                of his blow on its NPC -- verdict hit|blocked|pb|broken|none, react the engine's reaction tail (wo173_rules.h React as a
//                number: 1 queued), how e (the engine applied it) | r (the capped raw path), the health / stamina his game measured (the
//                amounts the host's NPC lost are brought to his for a landed hit). The joiner logs it (WO173-OUTCOME) and shows it on his copy
//   8 NpcDown   "<npc> <0|1|2>"   WO-173 C2, host -> joiners: the host's NPC is up (0), knocked out (1) or dead (2); sent on every change
//                and every 3 s while down, for the NPCs a figure fought or a joiner hit in the last 2 minutes. The joiner's copy follows at once
//
// An older peer never sees these (a mixed release is refused at the relay, WO-110 R9); a receiver that meets an unknown
// kind ignores it (counted). No protocol bump.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte W164Up = 0x74, W164Down = 0x75;   // WO-164

    public const int W164TextMax = 80;

    // ---- WO-178: the DLL's ops in the WO-163 family (native/KCDMP/wo178.h) ----
    public const byte Wo178OpOffer = 24, Wo178OpStatus = 25, Wo178OpConfig = 26, Wo178OpSaveManual = 27, Wo178OpHeldResult = 28, Wo178OpTrace = 29;
    // ---- WO-179: the DLL's ops in the WO-163 family (native/KCDMP/wo179.h) ----
    public const byte Wo179OpStatus = 30, Wo179OpConfig = 31, Wo179OpPoll = 32, Wo179OpFade = 33;
    // ---- WO-184: the DLL's ops in the WO-163 family (native/KCDMP/wo178.h): the engine's own save state and its own cancel ----
    public const byte Wo184OpSaveState = 34, Wo184OpSaveCancel = 35;
    // ---- WO-187: the DLL's ops in the WO-163 family (native/KCDMP/wo187.h): the copies' opponents, the switches, the guard hold, positions ----
    public const byte Wo187OpOpponent = 36, Wo187OpStatus = 37, Wo187OpConfig = 38, Wo187OpGuardHold = 39, Wo187OpPositions = 40, Wo187OpMoveShown = 41;
    // ---- WO-189: the DLL's ops in the WO-163 family (native/KCDMP/wo189.h): save readiness, the cutscene player, copies' FastForward, game profiles ----
    public const byte Wo189OpStatus = 42, Wo189OpConfig = 43, Wo189OpReady = 44, Wo189OpCutscene = 45, Wo189OpCutsceneRelease = 46, Wo189OpStopFF = 47,
                      Wo189OpProfile = 48, Wo189OpConcept = 49;
    // ---- WO-190: the DLL's ops in the WO-163 family (native/KCDMP/wo190.h): the animation trace's switches, a body's hands, the clinch refusals ----
    public const byte Wo190OpStatus = 50, Wo190OpConfig = 51, Wo190OpWeapon = 52, Wo190OpClinchRefused = 53;
    // ---- WO-195: the DLL's ops in the WO-163 family (native/KCDMP/wo195.h): a dead copy's release, its check, the switch ----
    public const byte Wo195OpStatus = 54, Wo195OpConfig = 55, Wo195OpRelease = 56, Wo195OpCheck = 57;
    // ---- WO-194: the DLL's ops in the WO-163 family (native/KCDMP/wo194.h): in-game cutscenes -- the enqueue events, the copy's start through its
    //      handler, the joiner's hold, the sequence clock, the copies' FastForward stop ----
    public const byte Wo194OpStatus = 58, Wo194OpConfig = 59, Wo194OpPoll = 60, Wo194OpStart = 61, Wo194OpHold = 62, Wo194OpSeq = 63, Wo194OpStopFF = 64;
    // ---- WO-197: the DLL's ops in the WO-163 family (native/KCDMP/wo197.h): the switches, a paired move's two halves, StartSyncGuard on a pair,
    //      EnsureGuardAction on a copy ----
    public const byte Wo197OpStatus = 65, Wo197OpConfig = 66, Wo197OpPair = 67, Wo197OpSyncGuard = 68, Wo197OpEnsureGuard = 69;

    // ---- kinds (APPEND-ONLY) ----
    public const byte W164MarkPing = 1, W164Torch = 2, W164Escort = 3, W164HitOutcome = 4, W164Weather = 5, W164Travel = 6, W164RealHit = 7, W164NpcDown = 8;
    /// <summary>WO-176 (A1), host -> joiner: "&lt;npc&gt;" -- the joiner's blow hit an actor of a quest's arranged fight; it was not routed into
    /// that fight (the story's), and the joiner is told once.</summary>
    public const byte W164StoryFight = 9;
    /// <summary>WO-177 B2, joiner -> host: "&lt;npc&gt;:&lt;x&gt;:&lt;y&gt; ..." (1..3 entries) -- dead NPC copies this joiner has near him; does the host's world still have them?</summary>
    public const byte W164CorpseAsk = 10;
    /// <summary>WO-177 B2, host -> joiner: "&lt;npc&gt;=&lt;v&gt; ..." (1..4 entries) -- v 1 = a corpse on the host too, 2 = gone from the host's world (the joiner removes its copy),
    /// 3 = alive / knocked out there (not a corpse question), 0 = the host cannot say (too far from it). Also sent unasked when the host's own corpse goes.</summary>
    public const byte W164CorpseAnswer = 11;
    /// <summary>WO-178 0.d, joiner -> host: "key|menu|console" -- the joiner's quicksave: the host's game saves the shared world (no vote).</summary>
    public const byte W164QuickSaveAsk = 12;
    /// <summary>WO-178 0.d, host -> the asking joiner: "saved" | "waited" | "refused &lt;why&gt;".</summary>
    public const byte W164QuickSaveAnswer = 13;
    /// <summary>WO-178 0.d, host -> joiners: "quicksave=on|off" -- the host's mp_quicksave (on a change and every 10 s).</summary>
    public const byte W164QuickSaveSetting = 14;
    /// <summary>WO-179 Part B, either way: the scene bus -- "S|R|E|K|L|T|H &lt;seq&gt; ..." (Wo179Rules: start, destination, end, skip key,
    /// landed, clock jump, held). A shared scene is a pre-rendered video or a scene that moves Henry; the sender is the triggerer.
    /// WO-194 appends the in-game cutscene's letters "C|Q|A|G|P|J|X &lt;seq&gt; ..." (Wo194Rules: started by this quest, its handler, the
    /// answer, the sequence runs, its clock, a skip's jump, stopped).</summary>
    public const byte W164SceneBus = 15;
    /// <summary>WO-184 B5, joiner -> host: "paired &lt;seq&gt; &lt;md5_8&gt;" (the joiner's character was saved with that host save),
    /// "unpaired &lt;n&gt;" (n host saves in a row did not pair), "stuck" (the joiner's save is stuck), "recovered" (Wo184Rules).</summary>
    public const byte W164SaveHealth = 16;
    /// <summary>WO-189 Part B, host -> joiner: "+&lt;profile&gt;" / "-&lt;profile&gt;" (a quest world layer the host's game switched on / off in play),
    /// "=&lt;profile&gt;" (one active at the joiner's join: the catch-up); joiner -> host: "?" (asks for the catch-up). Quest profiles only (Wo189Rules).</summary>
    public const byte W164Profile = 17;

    /// <summary>While the torch is out, the side-channel repeats it this often (a lost edge recovers).</summary>
    public const int W164TorchRepeatMs = 10_000;

    public static string W164KindName(byte k) => k switch
    {
        W164MarkPing => "mark-ping", W164Torch => "torch", W164Escort => "escort", W164HitOutcome => "hit-outcome", W164Weather => "weather",
        W164Travel => "travel", W164RealHit => "real-hit", W164NpcDown => "npc-down", W164StoryFight => "story-fight",
        W164CorpseAsk => "corpse-ask", W164CorpseAnswer => "corpse-answer", W164QuickSaveAsk => "quicksave-ask",
        W164QuickSaveAnswer => "quicksave-answer", W164QuickSaveSetting => "quicksave-setting", W164SceneBus => "scene-bus", W164SaveHealth => "save-health", W164Profile => "profile", W164Clock => "clock", W164Noise => "noise",
        _ => $"unknown-{k}",
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

    /// <summary>WO-173: an NPC name as the W164 texts carry it: [A-Za-z0-9_], 1..48 (the escort rule, shorter so a RealHit fits the text max).</summary>
    public static bool IsNpcName(string? s)
    {
        if (s is null || s.Length is 0 or > 48) return false;
        foreach (char c in s) if (!(char.IsAsciiLetterOrDigit(c) || c == '_')) return false;
        return true;
    }

    /// <summary>WO-173 C2: "&lt;npc&gt; &lt;verdict&gt; &lt;react&gt; &lt;e|r&gt; &lt;hp&gt; &lt;st&gt;" -- the host's engine's outcome of a joiner's blow.</summary>
    public static string RealHit(string npc, HitVerdict verdict, byte react, bool byEngine, float hp, float st) =>
        FormattableString.Invariant($"{npc} {(verdict == HitVerdict.None ? "none" : HitVerdictMsg.VerdictName(verdict))} {react} {(byEngine ? 'e' : 'r')} {Stat(hp):F1} {Stat(st):F1}");

    private static float Stat(float v) => !float.IsFinite(v) || v < 0 ? 0f : Math.Min(v, 999.9f);

    public static bool TryParseRealHit(string? text, out string npc, out HitVerdict verdict, out byte react, out bool byEngine, out float hp, out float st)
    {
        npc = ""; verdict = HitVerdict.None; react = 0; byEngine = false; hp = st = 0;
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (f.Length != 6 || !IsNpcName(f[0])) return false;
        if (f[1] != "none" && (!HitVerdictMsg.TryParseVerdict(f[1], out verdict) || verdict is HitVerdict.Parried or HitVerdict.Missed)) return false;
        if (!byte.TryParse(f[2], System.Globalization.NumberStyles.None, inv, out react) || f[3] is not ("e" or "r")) return false;
        if (!float.TryParse(f[4], System.Globalization.NumberStyles.Float, inv, out hp) || !float.TryParse(f[5], System.Globalization.NumberStyles.Float, inv, out st)) return false;
        if (!float.IsFinite(hp) || !float.IsFinite(st) || hp < 0 || st < 0 || hp > 999.9f || st > 999.9f) return false;
        npc = f[0]; byEngine = f[3] == "e";
        return true;
    }

    /// <summary>WO-173 C2: "&lt;npc&gt; &lt;0|1|2&gt;" -- up, knocked out, dead.</summary>
    public static string NpcDown(string npc, byte state) => $"{npc} {state}";

    public static bool TryParseNpcDown(string? text, out string npc, out byte state)
    {
        npc = ""; state = 0;
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 2 || !IsNpcName(f[0]) || f[1] is not ("0" or "1" or "2")) return false;
        npc = f[0]; state = (byte)(f[1][0] - '0');
        return true;
    }

    /// <summary>WO-177 B2: most entries one message carries (the text maximum is 80).</summary>
    public const int CorpseAskMax = 3, CorpseAnswerMax = 4;

    /// <summary>"&lt;npc&gt;:&lt;x&gt;:&lt;y&gt;" per entry (whole metres): where the joiner's dead copy lies.</summary>
    public static string CorpseAsk(IEnumerable<(string Npc, int X, int Y)> entries)
    {
        // up to CorpseAskMax entries, and never past the text maximum: an entry that would overflow it waits for the next question
        var sb = new System.Text.StringBuilder();
        int n = 0;
        foreach (var e in entries)
        {
            string one = FormattableString.Invariant($"{e.Npc}:{e.X}:{e.Y}");
            if (n >= CorpseAskMax || sb.Length + (n > 0 ? 1 : 0) + one.Length > Protocol.W164TextMax) break;
            if (n++ > 0) sb.Append(' ');
            sb.Append(one);
        }
        return sb.ToString();
    }

    public static bool TryParseCorpseAsk(string? text, out List<(string Npc, int X, int Y)> entries)
    {
        entries = [];
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length is 0 or > CorpseAskMax) return false;
        foreach (var e in f)
        {
            var p = e.Split(':');
            if (p.Length != 3 || !IsNpcName(p[0])) return false;
            if (!int.TryParse(p[1], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(p[2], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int y)
                || Math.Abs(x) > 100000 || Math.Abs(y) > 100000) return false;
            entries.Add((p[0], x, y));
        }
        return true;
    }

    /// <summary>"&lt;npc&gt;=&lt;v&gt;" per entry, v 0..3 (see Protocol.W164CorpseAnswer).</summary>
    public static string CorpseAnswer(IEnumerable<(string Npc, byte V)> entries) =>
        string.Join(' ', entries.Take(CorpseAnswerMax).Select(e => $"{e.Npc}={e.V}"));

    public static bool TryParseCorpseAnswer(string? text, out List<(string Npc, byte V)> entries)
    {
        entries = [];
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length is 0 or > CorpseAnswerMax) return false;
        foreach (var e in f)
        {
            var p = e.Split('=');
            if (p.Length != 2 || !IsNpcName(p[0]) || p[1] is not ("0" or "1" or "2" or "3")) return false;
            entries.Add((p[0], (byte)(p[1][0] - '0')));
        }
        return true;
    }

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

    /// <summary>
    /// WO-167 FT2: a travel destination as the wire carries it -- "@name" (a travel point by name: [A-Za-z0-9_.-], 1..48), "x,y,z" (a world
    /// position, each in -100000..100000) or "-" (not known).
    /// </summary>
    public static bool IsTravelDest(string? d)
    {
        if (d is null) return false;
        if (d == "-") return true;
        if (d.StartsWith('@'))
        {
            if (d.Length is < 2 or > 49) return false;
            foreach (char c in d.AsSpan(1)) if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-')) return false;
            return true;
        }
        return TryParsePos(d, out _, out _, out _);
    }

    public static bool TryParsePos(string? s, out float x, out float y, out float z)
    {
        x = y = z = 0;
        var f = (s ?? "").Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (f.Length != 3) return false;
        if (!float.TryParse(f[0], System.Globalization.NumberStyles.Float, inv, out x) || !float.TryParse(f[1], System.Globalization.NumberStyles.Float, inv, out y)
            || !float.TryParse(f[2], System.Globalization.NumberStyles.Float, inv, out z)) return false;
        bool ok(float v) => float.IsFinite(v) && Math.Abs(v) <= 100_000f;
        return ok(x) && ok(y) && ok(z);
    }

    public static string Pos(float x, float y, float z) => FormattableString.Invariant($"{x:F1},{y:F1},{z:F1}");

    public static string TravelRequest(uint seq, string dest) => $"r {seq} {dest}";
    public static string TravelRefused(uint seq, string why) => $"n {seq} {why}";
    public static string TravelStart(uint seq, string dest) => $"s {seq} {dest}";
    public static string TravelEnd(uint seq, float x, float y, float z, bool interrupted) => $"e {seq} {Pos(x, y, z)} {(interrupted ? 1 : 0)}";

    /// <summary>WO-167 FT2: one Travel text -> (verb r|n|s|e, seq, arg, interrupted). The refusal's word: [a-z-], 1..24.</summary>
    public static bool TryParseTravel(string? text, out char verb, out uint seq, out string arg, out bool interrupted)
    {
        verb = '?'; seq = 0; arg = ""; interrupted = false;
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 3 || f[0].Length != 1 || !uint.TryParse(f[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out seq)) return false;
        verb = f[0][0];
        switch (verb)
        {
            case 'r':
            case 's':
                if (f.Length != 3 || !IsTravelDest(f[2])) return false;
                arg = f[2]; return true;
            case 'n':
                if (f.Length != 3 || f[2].Length is 0 or > 24) return false;
                foreach (char c in f[2]) if (!(c is >= 'a' and <= 'z' || c == '-')) return false;
                arg = f[2]; return true;
            case 'e':
                if (f.Length != 4 || !TryParsePos(f[2], out _, out _, out _) || f[3] is not ("0" or "1")) return false;
                arg = f[2]; interrupted = f[3] == "1"; return true;
            default:
                return false;
        }
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
