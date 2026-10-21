// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-202: crashes, quests that fail on their own, and desync (docs/WO-202-findings.md). The pure rules; the bridge is
/// GameBridge.Wo202.cs, the game-side halves are kdcmp.lua's WO-202 section, the DLL's half native/KCDMP/wo202.cpp.
/// </summary>
public static class Wo202Rules
{
    // ---- the switches (each mechanism ships on, each with its own switch) ----
    public static readonly string[] SwitchNames =
    {
        "crash_capture", "join_no_npc_suspend", "join_short_hold", "death_engine_only", "quest_fail_trace",
        "net_game_queue", "ping_network", "rejoin_recover", "anim_shown", "recoil_clinch", "fight_pos_fresh",
        "desync_score", "cutscene_clock_v2", "keys_per_player", "outfit_atomic", "mark_key",
        "near_safe",
    };

    public static bool DefaultOn(string name) => true;

    /// <summary>The DLL's op 88 bits (native/KCDMP/wo202_rules.h): 1 crash_capture, 2 near_safe, 4 quest_fail_trace, 16 anim_shown,
    /// 32 recoil_clinch, 64 fight_pos_fresh, 512 death_engine_only (8, 128 and 256 unused: no mechanism was built for them).</summary>
    public static uint ConfigBits(Func<string, bool> on) =>
        (on("crash_capture") ? 1u : 0) | (on("near_safe") ? 2u : 0) | (on("quest_fail_trace") ? 4u : 0)
        | (on("anim_shown") ? 16u : 0) | (on("recoil_clinch") ? 32u : 0) | (on("fight_pos_fresh") ? 64u : 0) | (on("death_engine_only") ? 512u : 0);

    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        if (string.IsNullOrEmpty(arg)) return false;
        int eq = arg.IndexOf('=');
        if (eq <= 0) return false;
        name = arg[..eq];
        string v = arg[(eq + 1)..];
        if (Array.IndexOf(SwitchNames, name) < 0) return false;
        if (v == "on") { on = true; return true; }
        if (v == "off") { on = false; return true; }
        return false;
    }

    // ---- II.2 join_short_hold ----
    /// <summary>The host's world is held only while the join save is written and sent: released once the joiner reports the file
    /// whole (its hash and verify passed), never kept through the joiner's load.</summary>
    public static bool ReleaseAtTransferDone(bool shortHold) => shortHold;

    /// <summary>The mod's "join_resumed" for the agent's own early release ("sent") is no cancel: the join waits on for the joiner's ready.</summary>
    public static bool IsOwnEarlyRelease(string reason) => reason == "sent";

    // ---- II.4 quest_fail_trace ----
    public readonly record struct QuestFail(uint Seq, int AgeMs, int Old, int New, bool Cascade, int RanMs, string Path, string Port, string Class,
                                            string SourcePath, string SourcePort);

    /// <summary>The DLL's op 89 reply: [more:1] then [seq:4][ageMs:4][old:4][new:4][cascade:1][ranMs:4][pathLen:2][path][portLen:1][port]
    /// [clsLen:1][cls][srcPathLen:2][srcPath][srcPortLen:1][srcPort]; only [more] when there is nothing new.</summary>
    public static bool TryParseFail(byte[] p, out QuestFail f, out bool more)
    {
        f = default; more = p.Length > 0 && p[0] != 0;
        if (p.Length < 1 + 21) return false;
        int o = 1;
        uint seq = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(o)); o += 4;
        int age = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(o)); o += 4;
        int old = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(o)); o += 4;
        int nw = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(o)); o += 4;
        bool cascade = p[o++] != 0;
        int ran = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(o)); o += 4;
        string? Str(bool wide)
        {
            int need = wide ? 2 : 1;
            if (o + need > p.Length) return null;
            int l = wide ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(o)) : p[o];
            o += need;
            if (o + l > p.Length) return null;
            string s = System.Text.Encoding.UTF8.GetString(p, o, l);
            o += l;
            return s;
        }
        string? path = Str(true), port = Str(false), cls = Str(false), sp = Str(true), sport = Str(false);
        if (path is null || port is null || cls is null || sp is null || sport is null) return false;
        f = new QuestFail(seq, age, old, nw, cascade, ran, path, port, cls, sp, sport);
        return true;
    }

    /// <summary>The kind of the node behind a failure, from its class name (the DLL's wo202rules::source_kind, the same words).</summary>
    public static string SourceKind(string cls)
    {
        if (string.IsNullOrEmpty(cls)) return "unknown";
        bool Has(string s) => cls.Contains(s, StringComparison.OrdinalIgnoreCase);
        if (Has("Timer") || Has("TimeOfDay")) return "timer";
        if (Has("Distance")) return "distance-trigger";
        if (Has("Area") || Has("Volume")) return "area-trigger";
        if (Has("Watch")) return "watch";
        if (Has("Dialog") || Has("Talk")) return "conversation";
        if (Has("Behavior") || Has("Behaviour")) return "behaviour";
        if (Has("Waiter")) return "scene-waiter";
        if (Has("Trigger")) return "trigger";
        return "node";
    }

    /// <summary>The last two dotted parts of a quest path ("boj_v_tabore.getPacholek").</summary>
    public static string PathTail(string path, int parts = 2)
    {
        var a = (path ?? "").Split('.');
        return string.Join('.', a.Skip(Math.Max(0, a.Length - parts)));
    }

    /// <summary>The briefing's words for one failure: what failed, what made it, how long it had run, whether the mod did anything in that time.</summary>
    public static string FailInWords(QuestFail f, int eventsInWindow)
    {
        string what = f.Port.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? "was cancelled"
            : f.Port.Contains("abort", StringComparison.OrdinalIgnoreCase) ? "was aborted"
            : f.Port.Contains("fail", StringComparison.OrdinalIgnoreCase) ? "failed" : "marked the player as having walked away";
        string kind = SourceKind(f.Class);
        string ran = f.RanMs >= 0 ? $" after {f.RanMs / 60000.0:F1} min" : "";
        string cause = kind switch
        {
            "timer" => $"its timer '{PathTail(f.SourcePath)}' ran out{ran}",
            "distance-trigger" => $"a distance check '{PathTail(f.SourcePath)}' fired{ran}",
            "area-trigger" => $"an area check '{PathTail(f.SourcePath)}' fired{ran}",
            "watch" => $"a watch '{PathTail(f.SourcePath)}' fired{ran}",
            "conversation" => $"a conversation ('{PathTail(f.SourcePath)}') decided it",
            "behaviour" => $"a behaviour ('{PathTail(f.SourcePath)}') ended it",
            "unknown" => "no node pulse made it (the game's own code)",
            _ => $"'{PathTail(f.SourcePath)}' ({kind}) made it{ran}",
        };
        string window = eventsInWindow > 0 ? $"; {eventsInWindow} mod event(s) in that window (a join's hold, a reload, a load -- listed)" : "";
        return $"Quest step {PathTail(f.Path)} {what}: {cause}{window}";
    }

    // ---- V.2 speech_share ----
    /// <summary>The engine's voice path: "Localization/dialog/..." with a file name, either slash, an extension or none, no "..", 5-190
    /// characters (native/KCDMP/wo202_rules.h is_voice_path; WO-201 took only ".ogg" and the engine's paths have no extension).</summary>
    public static bool IsVoicePath(string p)
    {
        if (string.IsNullOrEmpty(p) || p.Length < 5 || p.Length > 190 || p.Contains("..")) return false;
        if (!p.StartsWith("Localization", StringComparison.OrdinalIgnoreCase) || p.Length < 20) return false;
        if (p[12] is not ('/' or '\\') || !p.Substring(13, 6).Equals("dialog", StringComparison.OrdinalIgnoreCase) || p[19] is not ('/' or '\\')) return false;
        return p[^1] is not ('/' or '\\');
    }

    // ---- II.3 death_engine_only (host): a joiner's blow without the death reaction is followed by one read of the body ----
    /// <summary>WO-173's react byte 1 = the engine's reaction was queued (a killing blow plays the death reaction itself); every other
    /// answer -- the victim could not react, no reaction found, the raw path (0) -- leaves a killed body upright.</summary>
    public static bool DeathFallWanted(bool on, bool hostAuthority, bool applied, byte react) => on && hostAuthority && applied && react != 1;

    /// <summary>The death observer's announcement: the engine's soul reads dead (1); no answer at all (an older DLL, null) keeps the observer's
    /// word; a soul that reads alive (0) never dies on the wire.</summary>
    public static bool AnnounceDeath(byte? soulDead) => soulDead is null or 1;

    /// <summary>The soul is asked up to 3 times, 300 ms apart (the DLL's own read is kept 250 ms).</summary>
    public const int DeathReadTries = 3, DeathReadGapMs = 300;

    /// <summary>When the body is read after such a blow: at once (the soul dies inside the engine's hit), then once more a moment later.</summary>
    public static readonly int[] DeathFallChecksMs = { 150, 850 };

    // ---- V.1 cutscene_clock_v2 (joiner): the partner's sequence clock, smoothed; small drift by speed, a jump only forward ----
    public const int ClockSamples = 8;
    public const double ClockStaleS = 0.5;       // a sample that puts the partner this far behind the newest estimate arrived late (a backlog)
    public const double SpeedMin = 0.9, SpeedMax = 1.1, SpeedGainPerS = 0.1, SpeedDeadbandS = 0.05;
    public const double SeekOverS = 1.5;          // a jump only when the copy is this far behind ...
    public const long SeekHeldMs = 1_000;         // ... for this long ...
    public const long SeekQuietMs = 2_500;        // ... and no voice line started this recently (the engine's own voice events)

    /// <summary>One partner sample: its sequence time and when it arrived here (local ms).</summary>
    public readonly record struct ClockSample(double PartnerS, long ArrivedMs);

    /// <summary>The partner's sequence time now, from the last samples: each sample's time plus the clock sync's one-way delay plus its age;
    /// samples older in sequence time than one already kept (out of order) are not kept; of the rest, those more than ClockStaleS behind the
    /// newest estimate (delayed in a backlog) are dropped; the median of what remains. NaN when nothing remains.</summary>
    public static double PartnerNowS(IReadOnlyList<ClockSample> samples, long oneWayMs, long nowMs)
    {
        var est = new List<double>();
        double maxSeen = double.NegativeInfinity;
        foreach (var s in samples)
        {
            if (s.PartnerS < maxSeen) continue;   // out of order
            maxSeen = s.PartnerS;
            est.Add(s.PartnerS + Math.Max(0, oneWayMs) / 1000.0 + Math.Max(0, nowMs - s.ArrivedMs) / 1000.0);
        }
        if (est.Count == 0) return double.NaN;
        double top = est.Max();
        var kept = est.Where(e => top - e <= ClockStaleS).OrderBy(e => e).ToList();
        int n = kept.Count;
        return n % 2 == 1 ? kept[n / 2] : (kept[n / 2 - 1] + kept[n / 2]) / 2.0;
    }

    /// <summary>The playing speed for a drift (partner minus this copy, s): 1 inside the dead band, else 1 + gain x drift, held to 0.9..1.1.</summary>
    public static double SpeedFor(double driftS) =>
        Math.Abs(driftS) <= SpeedDeadbandS ? 1.0 : Math.Clamp(1.0 + SpeedGainPerS * driftS, SpeedMin, SpeedMax);

    /// <summary>A jump: only forward (the copy behind by more than SeekOverS), only after that held SeekHeldMs, only between voice lines.</summary>
    public static bool SeekNow(double driftS, long behindSinceMs, long nowMs, long lastVoiceMs) =>
        driftS > SeekOverS && behindSinceMs > 0 && nowMs - behindSinceMs >= SeekHeldMs && (lastVoiceMs <= 0 || nowMs - lastVoiceMs >= SeekQuietMs);

    // ---- IV.3 recoil_clinch: op 67's own-player answer [eid:4][255][255][0][nameLen:1][name] -> the name (an entity name only) ----
    public static string? OwnOpponentName(byte[] p)
    {
        if (p.Length < 8 || p[7] == 0 || p.Length < 8 + p[7]) return null;
        string n = System.Text.Encoding.ASCII.GetString(p, 8, p[7]);
        return n.Length <= 63 && n.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? n : null;
    }

    // ---- V.3 keys_per_player ----
    /// <summary>"k|class" for a class GUID; null for anything else.</summary>
    public static string? KeyText(string? arg) => Guid.TryParse((arg ?? "").Trim(), out var g) ? "k|" + g.ToString() : null;

    /// <summary>"c|name|x|y|z" from the mod's "name x y z" (a chest's entity name: letters, digits, _ . - only, at most 80).</summary>
    public static string? ChestText(string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 4 || !ChestName(f[0])) return null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (!float.TryParse(f[1], System.Globalization.NumberStyles.Float, ci, out float x) || !float.TryParse(f[2], System.Globalization.NumberStyles.Float, ci, out float y)
            || !float.TryParse(f[3], System.Globalization.NumberStyles.Float, ci, out float z) || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return null;
        return FormattableString.Invariant($"c|{f[0]}|{x:F2}|{y:F2}|{z:F2}");
    }

    private static bool ChestName(string s) => s.Length is > 0 and <= 80 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');

    public static bool TryParseKey(string? text, out string cls)
    {
        cls = "";
        if (text is null || !text.StartsWith("k|", StringComparison.Ordinal) || !Guid.TryParse(text.AsSpan(2), out var g)) return false;
        cls = g.ToString();
        return true;
    }

    public static bool TryParseChest(string? text, out string name, out float x, out float y, out float z)
    {
        name = ""; x = y = z = 0;
        var f = (text ?? "").Split('|');
        if (f.Length != 5 || f[0] != "c" || !ChestName(f[1])) return false;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (!float.TryParse(f[2], System.Globalization.NumberStyles.Float, ci, out x) || !float.TryParse(f[3], System.Globalization.NumberStyles.Float, ci, out y)
            || !float.TryParse(f[4], System.Globalization.NumberStyles.Float, ci, out z) || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return false;
        name = f[1];
        return true;
    }

    // ---- VI outfit_atomic ----
    /// <summary>The new pieces in one call (the mod's own inventory equip; REST equips stay one by one).</summary>
    public static bool OutfitAtomic(bool on, bool luaDress, int adds) => on && luaDress && adds > 0;

    /// <summary>The strip check's period: 2 s for 30 s after a partner's outfit changed, the old period otherwise.</summary>
    public static int OutfitCheckMs(bool on, long lastChangeMs, long nowMs, int normalMs) =>
        on && lastChangeMs > 0 && nowMs - lastChangeMs < 30_000 ? 2_000 : normalMs;

    /// <summary>One Lua call for the whole set: "cls1,cls2,..." (36-char GUIDs; 40 pieces stay far under the console's ~2,100 chars).</summary>
    public static string OutfitList(IEnumerable<Guid> classes) => string.Join(",", classes.Take(40).Select(c => c.ToString()));

    // ---- VI: a save the mod moved is not checked ----
    /// <summary>A file the mod moved out counts as the mod's for 10 minutes (the check settles for up to 20 s after the write).</summary>
    public static bool MovedRecently(DateTime movedUtc, DateTime nowUtc) => nowUtc - movedUtc <= TimeSpan.FromMinutes(10);

    /// <summary>One spelling per file: the saves watcher and the Henry pipeline name the same file with different slashes and case.</summary>
    public static string SavePathKey(string path)
    {
        string p = path;
        try { p = Path.GetFullPath(path); }
        catch (Exception) { }
        return p.Replace('/', '\\').ToLowerInvariant();
    }

    // ---- III.1 net_game_queue ----
    /// <summary>The body a state frame describes, or null for a frame that must keep its order (events, edges, messages): an avatar's
    /// position (Ghost, "g:" + its id) or an NPC's state (NpcStateDown, "n:" + its name). The newest of a body replaces a waiting one.</summary>
    public static string? StateKey(int type, byte[] payload)
    {
        if (type == KcdMp.Wire.Protocol.Ghost && payload.Length >= 1) return "g:" + payload[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (type == KcdMp.Wire.Protocol.NpcStateDown && payload.Length >= 3)
        {
            int n = payload[1];
            if (n == 0 || payload.Length < 2 + n) return null;
            return "n:" + System.Text.Encoding.UTF8.GetString(payload, 2, n);
        }
        return null;
    }

    // ---- III.3 rejoin_recover (joiner) ----
    public const int RejoinTries = 3;
    public const uint RejoinPromptId = 9202;   // the F11/F12 prompt's id (WO-140's prompt, its answer routed here)
    /// <summary>The gap before the next in-world rejoin request after <paramref name="fails"/> failed ones: 20, 40, 80 s.</summary>
    public static int RejoinGapS(int fails) => 20 << Math.Clamp(fails, 0, 2);
    /// <summary>After <see cref="RejoinTries"/> failed tries the player is asked (try again, or leave).</summary>
    public static bool RejoinShouldAsk(int fails) => fails >= RejoinTries;
    public const string RejoinQuestion = "The host's reload didn't reach you. F11 try again, F12 leave the host's world.";

    // ---- III.3 rejoin_recover ----
    /// <summary>How long the host's join waits with nothing at all from the joiner before it aborts (any ack, Done or status counts
    /// as progress, even late): 60 s with the switch, 20 s (the 0.61.9 value) without.</summary>
    public static int JoinSilenceAbortS(bool recover) => recover ? 60 : 20;
}
