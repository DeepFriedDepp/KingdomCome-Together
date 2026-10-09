// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-164: the engine-free rules of the 0.46.5 / 0.47.0 tester findings (docs/WO-164-findings.md).
/// <see cref="GameBridge"/> feeds them its live flags; Wo164Tests pins them.
/// </summary>
public static partial class Wo164Rules
{
    // ------------------------------------------------------------------ T0: the talk line

    /// <summary>What a conversation was, from the dialogue's own name (the engine's "Running dialogue 'name' ..." text).</summary>
    public static string TalkKind(string? dialogue)
    {
        if (string.IsNullOrEmpty(dialogue)) return "talk";
        string d = dialogue.ToLowerInvariant();
        if (d.Contains("smlouvani", StringComparison.Ordinal)) return "haggle";
        if (d.Contains("nakupovani_z_chatu", StringComparison.Ordinal)) return "chat";
        if (d.Contains("kostky", StringComparison.Ordinal)) return "dice";
        if (d.StartsWith("barbora.", StringComparison.Ordinal)) return "quest";
        return "other";
    }

    /// <summary>
    /// Why a talk that never started failed, from what was true when it was asked (the WO-164 T0 classes):
    /// busy (the host holds that NPC for its own talk, or carries it), preempted (the copy started its own dialogue first),
    /// quest (a quest value of the joiner's disagreed with the host's), torn (the copy's planner refused in the 10 s before),
    /// wedged (another player request was still open), neither (none of these: a cause still to be named).
    /// </summary>
    public static string FailCause(bool hostBusy, string? preemptedBy, int questMismatch, int plannerErr10s, int openRequests)
    {
        if (hostBusy) return "busy";
        if (!string.IsNullOrEmpty(preemptedBy)) return "preempted";
        if (questMismatch > 0) return "quest";
        if (plannerErr10s > 0) return "torn";
        if (openRequests > 1) return "wedged";
        return "neither";
    }

    // ------------------------------------------------------------------ T1: the sweep

    /// <summary>The adaptive trigger: this many planner refusals of one copy ...</summary>
    public const int AdaptiveErrors = 5;
    /// <summary>... inside this window (seconds).</summary>
    public const double AdaptiveWindowS = 10.0;
    /// <summary>One copy is swept at most this often (seconds), whatever the trigger.</summary>
    public const double SweepCooldownS = 20.0;
    /// <summary>After a sweep the host's refused activity is not asked of the copy again for this long (seconds).</summary>
    public const int SweepHoldS = 120;
    /// <summary>A sweep's report is written this long after it (the planner's errors in that time).</summary>
    public const int SweepReportMs = 6000;

    /// <summary>True when <paramref name="errorTimesS"/> (one copy's refusal times, seconds) hold AdaptiveErrors inside the window ending now.</summary>
    public static bool AdaptiveDue(IReadOnlyList<double> errorTimesS, double nowS, double lastSweptS)
    {
        if (nowS - lastSweptS < SweepCooldownS) return false;
        int n = 0;
        for (int i = errorTimesS.Count - 1; i >= 0; i--)
        {
            if (nowS - errorTimesS[i] > AdaptiveWindowS) break;
            n++;
        }
        return n >= AdaptiveErrors;
    }

    /// <summary>Planner errors of one copy between <paramref name="fromS"/> and <paramref name="toS"/> (inclusive).</summary>
    public static int ErrorsBetween(IReadOnlyList<double> errorTimesS, double fromS, double toS)
    {
        int n = 0;
        foreach (double t in errorTimesS) if (t >= fromS && t <= toS) n++;
        return n;
    }

    // ------------------------------------------------------------------ T3 / T6: retries and quest corrections

    /// <summary>A player request open this long without its dialogue starting is cancelled and re-issued once (seconds).</summary>
    public const double RetryAfterS = 4.0;

    /// <summary>A no-port mismatch older than this is written directly (seconds).</summary>
    public const double QuestFixAfterS = 10.0;

    /// <summary>The quest value types the direct write may set (stored inline in the State's variant, no enum range to break).</summary>
    public static bool QuestFixTypeSafe(string? type) => type is "int" or "bool";

    /// <summary>
    /// The direct write of a quest State on a joiner: never on the host, only a value the checkpoint says differs, only a safe
    /// type, only after the mismatch stood QuestFixAfterS (a port correction gets its chance first), never a contested State.
    /// </summary>
    public static string? QuestFixRefusal(bool isJoiner, string? type, double mismatchAgeS, bool contested, bool hasPort)
    {
        if (!isJoiner) return "not-a-joiner";
        if (contested) return "contested";
        if (hasPort) return "a-port-corrects-it";
        if (!QuestFixTypeSafe(type)) return "type-not-safe";
        if (mismatchAgeS < QuestFixAfterS) return "too-young";
        return null;
    }

    // ------------------------------------------------------------------ D2: the flee watchdog

    public const double WatchAgeS = 45.0, WatchNoBlowS = 20.0, WatchFarM = 30.0;

    /// <summary>A hold the mod made (an engagement, a forced target, a skirmish pair) that a fleeing or distant enemy outlived.</summary>
    public static bool WatchdogRelease(double ageS, double sinceBlowS, bool hostFlee, double nearestPlayerM)
        => ageS > WatchAgeS && sinceBlowS > WatchNoBlowS && (hostFlee || nearestPlayerM > WatchFarM);

    /// <summary>The host's activity for a fleeing NPC (its unstance name), e.g. FleeLookingAround.</summary>
    public static bool IsFleeUnstance(string? unstance) => unstance is not null && unstance.StartsWith("Flee", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ S2: a stale sit on an avatar

    public const double StaleSitS = 1.5;

    /// <summary>The avatar's body sits or lies while its player's stream has said "no seat" for StaleSitS: cleared.</summary>
    public static bool StaleSit(bool bodySeated, bool streamSeated, double sinceStreamUnseatedS)
        => bodySeated && !streamSeated && sinceStreamUnseatedS > StaleSitS;

    // ------------------------------------------------------------------ S3: unstuck

    /// <summary>A second "I'm stuck" within this many seconds is step 2 (beside the partner).</summary>
    public const double UnstuckStep2S = 10.0;

    public static int UnstuckStep(double sinceLastS) => sinceLastS >= 0 && sinceLastS <= UnstuckStep2S ? 2 : 1;

    // ------------------------------------------------------------------ M: the mark snapshot

    public const int SnapMaxLines = 60;

    /// <summary>A new mark id: 10 characters of [a-z0-9] from <paramref name="rnd"/>.</summary>
    public static string NewMarkId(Random rnd)
    {
        const string abc = "abcdefghijklmnopqrstuvwxyz0123456789";
        var c = new char[10];
        for (int i = 0; i < c.Length; i++) c[i] = abc[rnd.Next(abc.Length)];
        return new string(c);
    }

    private static readonly Regex Ip = new(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled);
    private static readonly Regex WinPath = new(@"[A-Za-z]:\\[^ ]*", RegexOptions.Compiled);
    private static readonly Regex HomePath = new(@"(/home/|/Users/|\\Users\\)[^ ]*", RegexOptions.Compiled);

    /// <summary>A snapshot line with anything that could name a person or a machine taken out (addresses, paths, e-mail).</summary>
    public static string Scrub(string line)
    {
        string s = Ip.Replace(line, "<ip>");
        s = WinPath.Replace(s, "<path>");
        s = HomePath.Replace(s, "<path>");
        if (s.Contains('@')) s = Regex.Replace(s, @"\S+@\S+", "<addr>");
        return s;
    }

    /// <summary>The block as written: every line prefixed "MP-MARK-SNAP mark=&lt;id&gt; ", scrubbed, at most SnapMaxLines (the last says how many were cut).</summary>
    public static List<string> SnapBlock(string markId, string side, IEnumerable<string> lines)
    {
        var all = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var outp = new List<string>();
        int room = SnapMaxLines - 1;
        foreach (var l in all.Take(room)) outp.Add($"MP-MARK-SNAP mark={markId} {side} {Scrub(l)}");
        outp.Add($"MP-MARK-SNAP mark={markId} {side} end lines={Math.Min(all.Count, room)}{(all.Count > room ? $" cut={all.Count - room}" : "")}");
        return outp;
    }

    // ------------------------------------------------------------------ RL: the host's reload, told

    public const string ReloadStartText = "Your host is reloading the world - please wait";
    public const string ReloadBackText = "Back with your host";

    // ------------------------------------------------------------------ TR: the torch side-channel

    /// <summary>A torch edge from the side-channel, given what the state block already showed for that ghost (null = nothing).</summary>
    public static bool? TorchSideEdge(bool? shown, bool now) => shown == now ? null : now;
}
