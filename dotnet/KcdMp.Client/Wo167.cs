// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>WO-167 (docs/WO-167-findings.md): the pure rules of 0.48.5's agent half. The bridge is GameBridge.Wo167.cs.</summary>
public static class Wo167Rules
{
    // ------------------------------------------------------------------ T4: a dialogue's interactive scene that never ends

    /// <summary>T4 mp_scene_release: a new mechanism ships on, with its switch (the maintainer's standing rule).</summary>
    public const bool DefaultSceneRelease = true;

    /// <summary>T4: a dialogue's scene still running this long after the dialogue ended, with no progress, is released (the WO's 20 s).</summary>
    public const long SceneStuckMs = 20_000;

    /// <summary>T4: after the release asked the engine, the scene's end is waited for this long before the black is lifted instead.</summary>
    public const long SceneEndWaitMs = 6_000;

    /// <summary>T4: a lifted black (wh_ui_FaderSuspend 1) is given back after this long even when the scene never says it ended.</summary>
    public const long FaderHoldMaxMs = 60_000;

    /// <summary>
    /// T4: is the interactive scene stuck after a dialogue? <paramref name="enqueues"/> / <paramref name="ends"/>: the engine's
    /// "InteractiveSceneManager::EnqueueScene" and "::EndScene" (or Interrupt) lines since the last load; <paramref name="dlgEndMs"/>:
    /// the last dialogue with this player that ended (0 = none since the last progress); <paramref name="progressMs"/>: the last sign of
    /// progress (a scene end, a new dialogue with this player, a fader that finished); <paramref name="releasedForMs"/>: the dialogue end
    /// a release already ran for (one release per stuck dialogue).
    /// </summary>
    public static bool SceneStuck(long enqueues, long ends, long dlgEndMs, long progressMs, long releasedForMs, long nowMs, long stuckMs = SceneStuckMs)
    {
        if (dlgEndMs <= 0 || enqueues <= ends) return false;           // nothing ended, or no scene is running
        if (releasedForMs == dlgEndMs) return false;                     // released already
        long since = Math.Max(dlgEndMs, progressMs);
        return nowMs - since >= stuckMs;
    }

    /// <summary>T4: the InteractiveSceneManager lines (EnqueueScene / EndScene / CleanupScene / Interrupt) -> a short stage word, or null.</summary>
    public static string? IsmStage(ReadOnlySpan<char> line)
    {
        const string ism = "InteractiveSceneManager::";
        int a = line.IndexOf(ism.AsSpan(), StringComparison.Ordinal);
        if (a < 0) return null;
        var rest = line[(a + ism.Length)..];
        if (rest.StartsWith("EnqueueScene called".AsSpan(), StringComparison.Ordinal)) return "IsmEnqueue";
        if (rest.StartsWith("EndScene called".AsSpan(), StringComparison.Ordinal)) return "IsmEnd";
        if (rest.StartsWith("CleanupScene called".AsSpan(), StringComparison.Ordinal)) return "IsmCleanup";
        if (rest.StartsWith("Interrupt called".AsSpan(), StringComparison.Ordinal)) return "IsmInterrupt";
        return null;
    }

    /// <summary>T4: "Last fader took 102.99s" -> seconds, or null.</summary>
    public static double? FaderTook(string line)
    {
        const string p = "Last fader took ";
        if (!line.StartsWith(p, StringComparison.Ordinal)) return null;
        var rest = line[p.Length..];
        int s = rest.IndexOf('s');
        if (s <= 0) return null;
        return double.TryParse(rest[..s], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
    }

    // ------------------------------------------------------------------ T2: who bumped the NPC (the host's collision bark)

    /// <summary>T2: the game's collision bark ("crime_reaction_barks.kolize") starting on an NPC -> its soul name, or null.</summary>
    public static string? KolizeSoul(string line)
    {
        if (!line.StartsWith("New dialogue '", StringComparison.Ordinal)) return null;
        if (line.IndexOf("crime_reaction_barks.kolize", StringComparison.Ordinal) < 0) return null;
        const string souls = "souls = 'Ex: ";
        int a = line.IndexOf(souls, StringComparison.Ordinal);
        if (a < 0) return null;
        int b = a + souls.Length, e = b;
        while (e < line.Length && (char.IsAsciiLetterOrDigit(line[e]) || line[e] == '_')) e++;
        if (e == b || e - b > 63) return null;
        string n = line[b..e];
        return n == "Dude" ? null : n;
    }

    // ------------------------------------------------------------------ T3: the brain's behaviour tree, from the engine's own lines

    /// <summary>
    /// T3: an engine line naming an NPC's behaviour-tree path ("/ai//(NPC)tzel_rowdy_3/(Base-SUBB)npc_basic_scheduler/(BT)dice_...") ->
    /// (npc, "subbrain/tree"), or null. The tree is the last (BT) segment before any (BT tmpl) / (N) part.
    /// </summary>
    public static (string Npc, string Path)? BtPath(string line)
    {
        int a = line.IndexOf("(NPC)", StringComparison.Ordinal);
        if (a < 0) return null;
        int b = a + 5, e = b;
        while (e < line.Length && (char.IsAsciiLetterOrDigit(line[e]) || line[e] == '_')) e++;
        if (e == b || e >= line.Length || line[e] != '/') return null;
        string npc = line[b..e];
        string sub = Segment(line, "SUBB)", e) ?? "-", tree = "";
        int t = e;
        while ((t = line.IndexOf("/(BT)", t, StringComparison.Ordinal)) >= 0)
        {
            tree = Segment(line, "/(BT)", t) ?? tree;
            t += 5;
        }
        if (tree.Length == 0) return null;
        string path = sub + "/" + tree;
        return (npc, path.Length > 96 ? path[..96] : path);
    }

    // ------------------------------------------------------------------ C1-C4: combat on the joiner's screen

    /// <summary>C2 mp_copy_weapons, C3 mp_swing_watch, C4 mp_sync_fallback: new mechanisms ship on, each with its switch.</summary>
    public const bool DefaultCopyWeapons = true, DefaultSwingWatch = true, DefaultSyncFallback = true;

    /// <summary>C3: the DLL's answer (native wo167_rules.h Seen).</summary>
    public const byte SeenNone = 0, SeenPending = 1, SeenInstalled = 2, SeenNoAnim = 3, SeenOff = 4;

    /// <summary>C3: the first look after a row was queued; a pending answer is looked at once more after <see cref="ShownSecondLookMs"/>.</summary>
    public const int ShownFirstLookMs = 200, ShownSecondLookMs = 450;

    /// <summary>C3: the reason a verdict's row did not show: the copy never animated it (the lunge plays instead).</summary>
    public const string ReasonNoAnim = "no-anim";

    /// <summary>
    /// C3: a verdict for a swing whose watch has not decided waits at most until this long after the swing was played -- the watch's last
    /// look (ShownFirstLookMs, ShownSecondLookMs, +250 ms) plus the pipe's answer and the scheduling, with room to spare. Live (WO-167's
    /// soak): every verdict came 220 ms after its swing, before the watch decided, so a swing that never animated was still judged shown
    /// and no lunge played; with 750 ms the cap still ran out just before the last look's answer (a verdict 640 ms after it arrived).
    /// </summary>
    public const long WatchVerdictCapMs = 1000;

    public static string SeenName(byte s) => s switch
    {
        SeenNone => "none", SeenPending => "pending", SeenInstalled => "installed", SeenNoAnim => "no-anim", SeenOff => "off", _ => "?",
    };

    /// <summary>C2: a row's right-hand weapon tag -- false for r_noweapon (fists), true for any weapon (r_sword, r_bluntWeapon ...), null when the row names none.</summary>
    public static bool? RowArmed(string tags)
    {
        foreach (var t in tags.Split('+', StringSplitOptions.RemoveEmptyEntries))
            if (t.StartsWith("r_", StringComparison.Ordinal)) return t != "r_noweapon";
        return null;
    }

    /// <summary>C2: an item table element kind that is a weapon held in the hand (MeleeWeapon, MissileWeapon ...).</summary>
    public static bool IsWeaponKind(string? kind) => kind is not null && kind.EndsWith("Weapon", StringComparison.Ordinal);

    /// <summary>C4: a paired (sync) attack row: its fragment says so ("CombatAttackSyncGen").</summary>
    public static bool IsSyncFragment(string fragment) => fragment.Contains("Sync", StringComparison.Ordinal);

    /// <summary>C4: the plain attack fragment a sync row falls back to.</summary>
    public const string PlainAttackFragment = "CombatAttackGen";

    /// <summary>
    /// C4: how well a plain attack row stands in for a sync one: -1 = not at all (another right-hand weapon, or a situational row: lying,
    /// sitting, mounted); else +4 the same attack type, +2 the same strength (attack_heavy / attack_light), +1 the same attack zone (aZ),
    /// +1 the same left hand.
    /// </summary>
    public static int PlainScore(string syncTags, int syncAttackType, string plainTags, int plainAttackType)
    {
        static string? Tag(string tags, string prefix)
        {
            foreach (var t in tags.Split('+', StringSplitOptions.RemoveEmptyEntries)) if (t.StartsWith(prefix, StringComparison.Ordinal)) return t;
            return null;
        }
        static bool Has(string tags, string t) => ("+" + tags + "+").Contains("+" + t + "+", StringComparison.Ordinal);
        string? r = Tag(syncTags, "r_");
        if (r is null || Tag(plainTags, "r_") != r) return -1;
        foreach (var bad in new[] { "oppLying", "oppSitting", "mounted", "horse", "oppMounted" }) if (Has(plainTags, bad)) return -1;
        int s = 0;
        if (syncAttackType >= 0 && syncAttackType == plainAttackType) s += 4;
        foreach (var st in new[] { "attack_heavy", "attack_light" }) if (Has(syncTags, st) && Has(plainTags, st)) s += 2;
        string? az = Tag(syncTags, "aZ");
        if (az is not null && Has(plainTags, az)) s += 1;
        string? l = Tag(syncTags, "l_");
        if (l is not null && Has(plainTags, l)) s += 1;
        return s;
    }

    /// <summary>C4: "Cannot change sync guard on &lt;npc&gt; because data not found for sync movement for slave." -> the NPC, or null.</summary>
    public static string? SyncGuardNpc(string line)
    {
        const string p = "Cannot change sync guard on ";
        int a = line.IndexOf(p, StringComparison.Ordinal);
        if (a < 0) return null;
        int b = a + p.Length, e = b;
        while (e < line.Length && (char.IsAsciiLetterOrDigit(line[e]) || line[e] == '_')) e++;
        return e > b && e - b <= 63 ? line[b..e] : null;
    }

    /// <summary>C4: a sync-guard line this soon after a sync row we sent to that NPC is "after our row".</summary>
    public const long SyncGuardAfterRowMs = 1000;

    /// <summary>C1: a stand-in's walk pulse is asked once its body is bound, for at most this long after its spawn.</summary>
    public const long StandInPulseWaitMs = 10_000;

    // ------------------------------------------------------------------ FT1-FT4: one shared fast travel

    /// <summary>FT2: a pick or a joiner's ask names the destination of the travel that starts within this long.</summary>
    public const long PickFreshMs = 15_000;

    /// <summary>FT2: a travel the engine was asked for must start within this long, or it said no.</summary>
    public const long TravelStartWaitMs = 6_000;

    /// <summary>FT2: a joiner's run of the shared travel holds its engine switch open at most this long.</summary>
    public const long MirrorMaxMs = 180_000;

    /// <summary>FT3: the engine's line when it stops a fast travel midway (its interrupt call).</summary>
    public const string TravelInterruptLine = "Interrupting FastTravel";

    /// <summary>
    /// FT3: the engine's line when a road event stops a fast travel (live, WO-167: an ambush on the route printed this and no
    /// "Interrupting FastTravel"): "FastTravel: random event variant '..' with option set '..' answered with '..' &lt;'..'&gt; stopping
    /// FastTravel: 'Y'" -- 'N' = the travel goes on.
    /// </summary>
    public const string TravelEventStopLine = "stopping FastTravel: 'Y'";

    /// <summary>FT3: true for either engine line that ends a fast travel midway.</summary>
    public static bool IsTravelInterruptLine(string? line) =>
        line is not null && (line.StartsWith(TravelInterruptLine, StringComparison.Ordinal)
                             || (line.StartsWith("FastTravel: random event variant ", StringComparison.Ordinal) && line.TrimEnd().EndsWith(TravelEventStopLine, StringComparison.Ordinal)));

    /// <summary>
    /// The standing rule (no silent guards): the line said once on screen when a caught fault switched a WO-167 feature off for the
    /// session. Features: shared-travel, scene-release, swing-watch, stand-in-pulse, talk-clear, talk-readiness.
    /// </summary>
    public static string FaultText(string feature) => feature switch
    {
        "shared-travel" => "Shared fast travel is off this session (a game error).",
        "scene-release" => "Stuck-scene release is off this session (a game error).",
        "swing-watch" => "Swing check is off this session (a game error).",
        "stand-in-pulse" => "Stand-in animation start is off this session (a game error).",
        "talk-clear" => "Talk clean-up after the host's talk is off this session (a game error).",
        "talk-readiness" => "Talk diagnostics are off this session (a game error).",
        "copy-weapons" => "Enemy weapon copying is off this session (a game error).",
        "talk-bump" => "Partner bump clean-up is off this session (a game error).",
        _ => "A co-op feature is off this session (a game error).",
    };

    /// <summary>FT2: the mod's answer to "can this game travel" -> one refusal word ([a-z-], 1..24); anything else is "not-now".</summary>
    public static string RefusalWord(string? r)
    {
        var s = (r ?? "").Trim();
        if (s.Length is 0 or > 24) return "not-now";
        foreach (char c in s) if (!(c is >= 'a' and <= 'z' || c == '-')) return "not-now";
        return s;
    }

    /// <summary>The name right after <paramref name="marker"/> (from <paramref name="from"/>) up to '/', ' ' or a quote; null when absent or empty.</summary>
    private static string? Segment(string line, string marker, int from)
    {
        int m = line.IndexOf(marker, from, StringComparison.Ordinal);
        if (m < 0) return null;
        int q = m + marker.Length, r = q;
        while (r < line.Length && line[r] != '/' && line[r] != ' ' && line[r] != '\'') r++;
        return r > q ? line[q..r] : null;
    }
}
