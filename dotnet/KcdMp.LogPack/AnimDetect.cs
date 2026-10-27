// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.LogPack;

/// <summary>
/// WO-206 D: every threshold the animation detectors use, in one place. Distances in metres, times in milliseconds, speeds in
/// metres per second; "horizontal" = x and y (z is up).
/// </summary>
public static class AnimLimits
{
    /// <summary>A gap longer than this between two samples of one body (it left the 40 m, a load, a menu) ends every run: the
    /// detectors never bridge a stretch the probe did not see.</summary>
    public const long MaxGapMs = 1000;

    /// <summary>tpose: the FullBody scope empty for longer than this on a living, non-ragdoll body (the bind pose shows).</summary>
    public const long TposeEmptyMs = 300;
    /// <summary>WO-207 G.1: tpose waits this long after a body appears (it streams in, or comes back after a load or a gap), after its own
    /// teleport and after the observing player's: a body that streams in shows no action for its first second or two on BOTH machines
    /// (WO-206's baseline: 17 of the host's own T-poses, all within 1.8 s of the body appearing).</summary>
    public const long TposeGraceMs = 3000;
    /// <summary>WO-209 C.3: tpose and stuck judge a body only within this of its machine's own player. WO-208's one fight T-pose was the
    /// bandits 846 m from the joiner before the scenario took him there: a body that far is not animated, its full body reads empty.</summary>
    public const double ViewM = 150;
    /// <summary>WO-207 G.1: a jump of a body longer than this between two samples is a teleport (its own grace starts).</summary>
    public const double TeleportM = 5.0;
    /// <summary>WO-207 G.1: the observing player's jump longer than this (the rig's teleports, a fast travel) gives every body on that
    /// machine the grace.</summary>
    public const double PlayerTeleportM = 20.0;
    /// <summary>tpose, bones: the two hands further apart than this (arms held straight out; a relaxed stance is ~0.5 m).</summary>
    public const double TposeHandSpanM = 1.4;
    /// <summary>tpose, bones: each hand within this of shoulder height (the arms are level, not swinging a weapon overhead).</summary>
    public const double TposeHandHeightTolM = 0.25;
    /// <summary>tpose, bones: shoulder height = the head bone's z minus this.</summary>
    public const double TposeShoulderBelowHeadM = 0.15;
    /// <summary>tpose, bones: the arms-out pose held at least this long (one sample can be a legitimate swing's frame).</summary>
    public const long TposeBonesMs = 300;

    /// <summary>sliding: horizontal speed above this ...</summary>
    public const double SlideSpeedMps = 0.5;
    /// <summary>sliding: ... held this long with no locomotion action in FullBody (the body glides).</summary>
    public const long SlideMinMs = 500;
    /// <summary>sliding, bones: only judged while the root moves faster than this (a walk; slower and the feet barely move).</summary>
    public const double SlideRootSpeedMps = 0.8;
    /// <summary>sliding, bones: the feet's mean horizontal speed below this share of the root's (walking feet average the body's
    /// speed: one planted, one swinging at twice it).</summary>
    public const double SlideFootRatio = 0.30;

    /// <summary>floating / sinking: the body's z this far above (or below) the terrain under it ...</summary>
    public const double GroundOffsetM = 0.35;
    /// <summary>floating / sinking: ... for at least this long.</summary>
    public const long GroundMinMs = 500;

    /// <summary>jitter: a step along one axis counts only above this (sub-step noise is not two writers).</summary>
    public const double JitterStepM = 0.05;
    /// <summary>jitter: this many sign reversals of the step along one axis ...</summary>
    public const int JitterReversals = 3;
    /// <summary>jitter: ... within this window.</summary>
    public const long JitterWindowMs = 1000;

    /// <summary>rubberband: a jump longer than this between two consecutive samples ...</summary>
    public const double RubberJumpM = 1.5;
    /// <summary>rubberband: ... and within this time ...</summary>
    public const long RubberWindowMs = 1000;
    /// <summary>rubberband: ... a jump back (a step of at least <see cref="RubberReturnStepM"/>) landing within this of where it was.</summary>
    public const double RubberReturnM = 0.5;
    /// <summary>rubberband: the way back must itself be a jump (walking 1.5 m back in a second is not a rubberband).</summary>
    public const double RubberReturnStepM = 1.0;

    /// <summary>deathpose: a dead body in a living action (or a living one in a death action) for longer than this.</summary>
    public const long DeathPoseMs = 1000;

    /// <summary>WO-207 G.1 stuck: a queued action that has waited this long without starting (the probe's "qa": the same action object
    /// still in the queue), on a living body that is not in its normal idle. The queue's length alone is not it: the host's own NPCs hold
    /// 2-4 queued actions in most samples (WO-206's long1: 127 "stuck" findings on bodies the game's own brain runs).</summary>
    public const long StuckAgeMs = 2000;
}

/// <summary>WO-206 D: one finding: a detector's run on one body on one machine.</summary>
/// <param name="Worst">the run's worst value, in <see cref="AnimDetect.Unit"/> of the detector (positive = worse)</param>
public sealed record AnimFinding(string Detector, string Body, string Kind, string Machine, long TStart, long TEnd, double Worst, int Samples, string Note)
{
    public double Seconds => (TEnd - TStart) / 1000.0;
    /// <summary>For the worst-bodies ranking: the worst value against the detector's threshold, weighted by how bad the detector's
    /// kind of fault looks to a player (a T-pose is worse than a queue that stalls).</summary>
    public double Score => AnimDetect.Weight(Detector) * Math.Abs(Worst) / AnimDetect.Threshold(Detector);
}

/// <summary>WO-206 D: what one body looked like on one machine over the probe's file.</summary>
public sealed class AnimBodyInfo
{
    public string Machine = "", Name = "", Kind = "";
    public int Samples;
    public long First, Last;
    public List<(long From, long To)> Present = new();   // split where the probe lost the body for longer than MaxGapMs
}

public sealed class AnimAnalysis
{
    public AnimStream Stream = new();
    public List<AnimFinding> Findings = new();
    public HashSet<string> Enabled = new(StringComparer.Ordinal);
    public List<AnimBodyInfo> Bodies = new();
    public int Count(string det) => Findings.Count(f => f.Detector == det);
    public int BodyCount(string det) => Findings.Where(f => f.Detector == det).Select(f => (f.Machine, f.Body)).Distinct().Count();

    /// <summary>The worst bodies, each with its worst finding and its number of findings.</summary>
    public List<(AnimFinding Worst, int Count)> WorstBodies(int n) =>
        Findings.GroupBy(f => (f.Machine, f.Body))
                .Select(g => (Worst: g.OrderByDescending(f => f.Score).ThenBy(f => f.TStart).First(), Count: g.Count()))
                .OrderByDescending(x => x.Worst.Score).ThenBy(x => x.Worst.Body, StringComparer.Ordinal).Take(n).ToList();
}

/// <summary>
/// WO-206 D: the animation detectors, our own, written from WO-206's definitions. Each runs per body per machine over the
/// samples in time order and reports runs (start, end, worst value, sample count). <see cref="Enabled"/> switches them; the
/// tests pass their own set so that switching one off (each check's twin) never races another test.
/// </summary>
public static class AnimDetect
{
    public static readonly string[] All = { "tpose", "sliding", "floating", "sinking", "jitter", "rubberband", "deathpose", "stuck" };

    /// <summary>The detectors the bundle runs (all of them; a switch for a detector that misfires in the field).</summary>
    public static readonly HashSet<string> Enabled = new(All, StringComparer.Ordinal);

    public static string Meaning(string det) => det switch
    {
        "tpose" => "the bind pose shows: FullBody empty on a living body, or both arms straight out",
        "sliding" => "the body moves without walking: no locomotion action, or feet that do not keep up",
        "floating" => "the body hangs above the ground",
        "sinking" => "the body sits below the ground",
        "jitter" => "the position flips back and forth (two writers)",
        "rubberband" => "a jump away and straight back",
        "deathpose" => "a dead body in a living action, or a living body in a death action",
        "stuck" => "a queued action waits and never starts, on a body that is not idling",
        _ => "",
    };

    public static string Unit(string det) => det switch
    {
        "tpose" or "deathpose" => "s",
        "sliding" => "m/s",
        "floating" or "sinking" or "rubberband" => "m",
        "jitter" => "reversals/s",
        "stuck" => "s waited",
        _ => "",
    };

    public static double Threshold(string det) => det switch
    {
        "tpose" => AnimLimits.TposeEmptyMs / 1000.0,
        "sliding" => AnimLimits.SlideSpeedMps,
        "floating" or "sinking" => AnimLimits.GroundOffsetM,
        "jitter" => AnimLimits.JitterReversals,
        "rubberband" => AnimLimits.RubberJumpM,
        "deathpose" => AnimLimits.DeathPoseMs / 1000.0,
        "stuck" => AnimLimits.StuckAgeMs / 1000.0,
        _ => 1,
    };

    public static double Weight(string det) => det switch
    {
        "tpose" => 4, "deathpose" => 3, "rubberband" => 3, "jitter" => 2, "sliding" => 2, "floating" => 1.5, "sinking" => 1.5, _ => 1,
    };

    // the probe's real classes: CAnimActionAIMovement is locomotion (fragment 4 = moving); others by their names
    internal static bool IsLocomotion(string? c) => c is not null && (c == "CAnimActionAIMovement"
        || c.Contains("Move", StringComparison.OrdinalIgnoreCase) || c.Contains("Locomotion", StringComparison.OrdinalIgnoreCase));
    internal static bool IsDeath(string? c) => c is not null && (c.Contains("Death", StringComparison.OrdinalIgnoreCase)
        || c.Contains("Dead", StringComparison.OrdinalIgnoreCase) || c.Contains("Ragdoll", StringComparison.OrdinalIgnoreCase));
    internal static bool IsLiving(string? c) => !IsDeath(c) && (IsLocomotion(c) || (c is not null && c.Contains("Idle", StringComparison.OrdinalIgnoreCase)));
    /// <summary>WO-207 G.1: a body standing in its normal idle (an idle state, an idle variation, the AI movement action standing: its
    /// fragment 5) -- actions queued behind it wait by design.</summary>
    internal static bool IsNormalIdle(string? c, int frag) => c is "C_AnimActionIdleState" or "C_VariationAction" || (c == "CAnimActionAIMovement" && frag == 5);

    public static AnimAnalysis Analyse(AnimStream s, ISet<string>? enabled = null)
    {
        var en = new HashSet<string>(enabled ?? Enabled, StringComparer.Ordinal);
        var an = new AnimAnalysis { Stream = s, Enabled = en };
        var playerJumps = PlayerTeleports(s);
        var players = s.Bodies.Values.Where(x => x.Kind == "player").GroupBy(x => x.Machine, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var ser in s.Bodies.Values.OrderBy(x => x.Machine, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            if (ser.S.Count == 0) continue;
            an.Bodies.Add(Info(ser));
            var o = an.Findings;
            var far = Far(ser, players.TryGetValue(ser.Machine, out var pl) ? pl : null);
            if (en.Contains("tpose")) Tpose(ser, o, Grace(ser, playerJumps.TryGetValue(ser.Machine, out var pj) ? pj : new List<long>()), far);
            if (en.Contains("sliding")) Sliding(ser, o);
            if (en.Contains("floating")) Ground(ser, o, +1);
            if (en.Contains("sinking")) Ground(ser, o, -1);
            if (en.Contains("jitter")) Jitter(ser, o);
            if (en.Contains("rubberband")) Rubberband(ser, o);
            if (en.Contains("deathpose")) DeathPose(ser, o);
            if (en.Contains("stuck")) Stuck(ser, o, far);
        }
        return an;
    }

    private static AnimBodyInfo Info(AnimSeries ser)
    {
        var S = ser.S;
        var b = new AnimBodyInfo { Machine = ser.Machine, Name = ser.Name, Kind = ser.Kind, Samples = S.Count, First = S[0].T, Last = S[^1].T };
        long from = S[0].T;
        for (int i = 1; i < S.Count; i++)
            if (S[i].T - S[i - 1].T > AnimLimits.MaxGapMs) { b.Present.Add((from, S[i - 1].T)); from = S[i].T; }
        b.Present.Add((from, S[^1].T));
        return b;
    }

    /// <summary>
    /// The runs where <paramref name="on"/> holds. on(i): true / false / null (unknown: the sample neither grows nor ends a run,
    /// e.g. no terrain z, no bones on this tick). A state run lasts until the first sample where the state is gone (the state was
    /// seen to last that long); an interval run (speeds: on(i) judges the step into i) starts at <paramref name="from"/>(first)
    /// and ends on its last step. A gap over MaxGapMs ends a run at its last true sample. <paramref name="val"/> null = the worst
    /// value is the run's length in seconds.
    /// </summary>
    private static void Runs(AnimSeries ser, string det, Func<int, bool?> on, Func<int, double>? val, long minMs, bool strict,
                             Func<int, long>? from, string note, List<AnimFinding> o)
    {
        var S = ser.S;
        int start = -1, lastTrue = -1, prevKnown = -1, n = 0;
        double worst = double.MinValue;
        void Close(long end)
        {
            if (start < 0) return;
            long st = from is null ? S[start].T : from(start);
            long dur = end - st;
            if (strict ? dur > minMs : dur >= minMs)
                o.Add(new AnimFinding(det, ser.Name, ser.Kind, ser.Machine, st, end, val is null ? dur / 1000.0 : worst, n, note));
            start = -1; n = 0; worst = double.MinValue;
        }
        for (int i = 0; i < S.Count; i++)
        {
            bool? v = on(i);
            if (v is null) continue;
            if (start >= 0 && S[i].T - S[prevKnown].T > AnimLimits.MaxGapMs) Close(S[lastTrue].T);
            prevKnown = i;
            if (v == true)
            {
                if (start < 0) start = i;
                lastTrue = i; n++;
                if (val is not null) worst = Math.Max(worst, val(i));
            }
            else if (start >= 0) Close(from is null ? S[i].T : S[lastTrue].T);
        }
        if (start >= 0) Close(S[lastTrue].T);
    }

    private static bool Living(in AnimSample x) => !x.Dead && !x.Rag;

    /// <summary>WO-207 G.1: per machine, when its observing player jumped further than <see cref="AnimLimits.PlayerTeleportM"/>.</summary>
    private static Dictionary<string, List<long>> PlayerTeleports(AnimStream s)
    {
        var d = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        foreach (var ser in s.Bodies.Values.Where(x => x.Kind == "player"))
        {
            if (!d.TryGetValue(ser.Machine, out var l)) d[ser.Machine] = l = new List<long>();
            for (int i = 1; i < ser.S.Count; i++)
                if (Dist3(ser.S[i], ser.S[i - 1]) > AnimLimits.PlayerTeleportM) l.Add(ser.S[i].T);
        }
        foreach (var l in d.Values) l.Sort();
        return d;
    }

    /// <summary>WO-207 G.1: per sample, true while the body is inside its grace: <see cref="AnimLimits.TposeGraceMs"/> after it appeared
    /// (its first sample, or the first after a gap: a stream-in, a load), after its own teleport, after the observing player's.</summary>
    internal static bool[] Grace(AnimSeries ser, List<long> playerJumps)
    {
        var S = ser.S;
        var g = new bool[S.Count];
        long since = long.MinValue;
        int pj = 0;
        for (int i = 0; i < S.Count; i++)
        {
            if (i == 0 || S[i].T - S[i - 1].T > AnimLimits.MaxGapMs || Dist3(S[i], S[i - 1]) > AnimLimits.TeleportM) since = S[i].T;
            while (pj < playerJumps.Count && playerJumps[pj] <= S[i].T) { since = Math.Max(since, playerJumps[pj]); pj++; }
            g[i] = S[i].T - since < AnimLimits.TposeGraceMs;
        }
        return g;
    }

    /// <summary>WO-209 C.3: per sample, true when the body is farther than <see cref="AnimLimits.ViewM"/> from its machine's own player at
    /// that moment (the player's nearest sample in time); false when there is no player series (judged as before).</summary>
    internal static bool[] Far(AnimSeries ser, AnimSeries? player)
    {
        var S = ser.S;
        var f = new bool[S.Count];
        if (player is null || player.S.Count == 0 || ser.Kind == "player") return f;
        var P = player.S;
        int j = 0;
        for (int i = 0; i < S.Count; i++)
        {
            while (j + 1 < P.Count && Math.Abs(P[j + 1].T - S[i].T) <= Math.Abs(P[j].T - S[i].T)) j++;
            f[i] = Dist3(S[i], P[j]) > AnimLimits.ViewM;
        }
        return f;
    }

    private static void Tpose(AnimSeries ser, List<AnimFinding> o, bool[] grace, bool[]? far = null)
    {
        var S = ser.S;
        far ??= new bool[S.Count];
        Runs(ser, "tpose", i => grace[i] || far[i] ? null : Living(S[i]) && S[i].Fb is null, null, AnimLimits.TposeEmptyMs, true, null, "FullBody empty", o);
        Runs(ser, "tpose", i =>
        {
            var b = S[i].Bones;
            if (b?.LHand is null || b.RHand is null || b.Head is null) return null;
            if (grace[i] || far[i]) return null;
            if (!Living(S[i])) return false;
            return ArmsOut(b);
        }, null, AnimLimits.TposeBonesMs, false, null, "arms straight out", o);
    }

    internal static bool ArmsOut(AnimBones b)
    {
        double shoulder = b.Head![2] - AnimLimits.TposeShoulderBelowHeadM;
        return Dist3(b.LHand!, b.RHand!) > AnimLimits.TposeHandSpanM
            && Math.Abs(b.LHand![2] - shoulder) <= AnimLimits.TposeHandHeightTolM
            && Math.Abs(b.RHand![2] - shoulder) <= AnimLimits.TposeHandHeightTolM;
    }

    private static void Sliding(AnimSeries ser, List<AnimFinding> o)
    {
        var S = ser.S;
        double Speed(int i) => HDist(S[i].X, S[i].Y, S[i - 1].X, S[i - 1].Y) * 1000.0 / (S[i].T - S[i - 1].T);
        // a ragdoll is thrown by physics and a rider carried by the horse: neither walks
        bool Carried(int i) => S[i].Rag || S[i].Mounted || S[i - 1].Rag || S[i - 1].Mounted;
        Runs(ser, "sliding", i =>
        {
            if (i == 0) return null;
            long dt = S[i].T - S[i - 1].T;
            if (dt <= 0 || dt > AnimLimits.MaxGapMs) return null;
            if (Carried(i)) return false;
            return Speed(i) > AnimLimits.SlideSpeedMps && !IsLocomotion(S[i].Fb);
        }, i => Speed(i), AnimLimits.SlideMinMs, false, i => S[i - 1].T, "no locomotion action", o);

        // bones: the previous sample with feet and root, for the bone variant's speeds
        var prev = new int[S.Count];
        int last = -1;
        for (int i = 0; i < S.Count; i++) { prev[i] = last; if (HasFeet(S[i].Bones)) last = i; }
        double RootSpeed(int i) { var a = S[i].Bones!.Root!; var b = S[prev[i]].Bones!.Root!; return HDist(a[0], a[1], b[0], b[1]) * 1000.0 / (S[i].T - S[prev[i]].T); }
        double FootSpeed(int i)
        {
            var a = S[i].Bones!; var b = S[prev[i]].Bones!; double dt = (S[i].T - S[prev[i]].T) / 1000.0;
            return (HDist(a.LFoot![0], a.LFoot[1], b.LFoot![0], b.LFoot[1]) + HDist(a.RFoot![0], a.RFoot[1], b.RFoot![0], b.RFoot[1])) / 2 / dt;
        }
        Runs(ser, "sliding", i =>
        {
            if (!HasFeet(S[i].Bones) || prev[i] < 0) return null;
            long dt = S[i].T - S[prev[i]].T;
            if (dt <= 0 || dt > AnimLimits.MaxGapMs) return null;
            if (S[i].Rag || S[i].Mounted) return false;
            double root = RootSpeed(i);
            return root > AnimLimits.SlideRootSpeedMps && FootSpeed(i) < AnimLimits.SlideFootRatio * root;
        }, i => RootSpeed(i), AnimLimits.SlideMinMs, false, i => S[prev[i]].T, "feet do not keep up", o);
    }

    private static bool HasFeet(AnimBones? b) => b?.Root is not null && b.LFoot is not null && b.RFoot is not null;

    private static void Ground(AnimSeries ser, List<AnimFinding> o, int sign)
    {
        var S = ser.S;
        string det = sign > 0 ? "floating" : "sinking";
        Runs(ser, det, i =>
        {
            if (float.IsNaN(S[i].Gz)) return null;   // the probe could not read the terrain: unknown
            if (!Living(S[i]) || S[i].Mounted) return false;
            return sign * (S[i].Z - S[i].Gz) > AnimLimits.GroundOffsetM;
        }, i => sign * (S[i].Z - S[i].Gz), AnimLimits.GroundMinMs, false, null, sign > 0 ? "above the terrain" : "below the terrain", o);
    }

    private static void Jitter(AnimSeries ser, List<AnimFinding> o)
    {
        var S = ser.S;
        var found = new List<AnimFinding>();
        string[] axes = { "x", "y", "z" };
        for (int ax = 0; ax < 3; ax++)
        {
            float P(int i) => ax == 0 ? S[i].X : ax == 1 ? S[i].Y : S[i].Z;
            var rev = new List<int>();   // the sample at which the step reversed
            for (int i = 2; i < S.Count; i++)
            {
                if (S[i].T - S[i - 1].T > AnimLimits.MaxGapMs || S[i - 1].T - S[i - 2].T > AnimLimits.MaxGapMs) continue;
                double d1 = P(i - 1) - P(i - 2), d2 = P(i) - P(i - 1);
                if (Math.Abs(d1) > AnimLimits.JitterStepM && Math.Abs(d2) > AnimLimits.JitterStepM && Math.Sign(d1) != Math.Sign(d2)) rev.Add(i);
            }
            int lo = 0;
            AnimFinding? cur = null;
            for (int j = 0; j < rev.Count; j++)
            {
                while (S[rev[j]].T - S[rev[lo]].T > AnimLimits.JitterWindowMs) lo++;
                int cnt = j - lo + 1;
                if (cnt < AnimLimits.JitterReversals) continue;
                long st = S[rev[lo] - 2].T, en = S[rev[j]].T;
                if (cur is not null && st <= cur.TEnd)
                    cur = cur with { TEnd = en, Worst = Math.Max(cur.Worst, cnt) };
                else
                {
                    if (cur is not null) found.Add(cur);
                    cur = new AnimFinding("jitter", ser.Name, ser.Kind, ser.Machine, st, en, cnt, 0, axes[ax] + " axis");
                }
            }
            if (cur is not null) found.Add(cur);
        }
        // one event seen on two axes is one finding
        AnimFinding? m = null;
        foreach (var f in found.OrderBy(f => f.TStart))
        {
            if (m is not null && f.TStart <= m.TEnd)
                m = m with { TEnd = Math.Max(m.TEnd, f.TEnd), Worst = Math.Max(m.Worst, f.Worst), Note = AxesNote(m.Note, f.Note) };
            else { if (m is not null) o.Add(WithSamples(m, S)); m = f; }
        }
        if (m is not null) o.Add(WithSamples(m, S));
    }

    // "x axis" + "z axis" -> "x+z axis"
    private static string AxesNote(string a, string b) =>
        string.Join("+", (a.Split(' ')[0] + "+" + b.Split(' ')[0]).Split('+').Distinct().OrderBy(x => x, StringComparer.Ordinal)) + " axis";

    private static AnimFinding WithSamples(AnimFinding f, List<AnimSample> S) => f with { Samples = S.Count(x => x.T >= f.TStart && x.T <= f.TEnd) };

    private static void Rubberband(AnimSeries ser, List<AnimFinding> o)
    {
        var S = ser.S;
        for (int i = 1; i < S.Count; i++)
        {
            if (S[i].T - S[i - 1].T > AnimLimits.MaxGapMs) continue;
            double jump = Dist3(S[i], S[i - 1]);
            if (jump <= AnimLimits.RubberJumpM) continue;
            for (int k = i + 1; k < S.Count && S[k].T - S[i].T <= AnimLimits.RubberWindowMs; k++)
            {
                if (Dist3(S[k], S[i - 1]) < AnimLimits.RubberReturnM && Dist3(S[k], S[k - 1]) >= AnimLimits.RubberReturnStepM)
                {
                    o.Add(new AnimFinding("rubberband", ser.Name, ser.Kind, ser.Machine, S[i - 1].T, S[k].T, jump, k - i + 2, $"away {jump:F1} m and back"));
                    i = k;
                    break;
                }
            }
        }
    }

    private static void DeathPose(AnimSeries ser, List<AnimFinding> o)
    {
        var S = ser.S;
        var runs = new List<AnimFinding>();
        // WO-207 G.1: a dead body that lies (its physics a ragdoll: a corpse keeps its last idle in the controller) is not standing dead
        Runs(ser, "deathpose", i => S[i].Dead && !S[i].Rag && IsLiving(S[i].Fb), null, AnimLimits.DeathPoseMs, true, null, "dead in a living action", runs);
        // a knocked-out body lies in a ragdoll alive: only an upright (non-ragdoll) living body in a death action is wrong
        Runs(ser, "deathpose", i => !S[i].Dead && !S[i].Rag && IsDeath(S[i].Fb), null, AnimLimits.DeathPoseMs, true, null, "alive in a death action", runs);
        // WO-207 G.1: one finding per body per stretch it was seen (WO-206's baseline: one corpse made 93): from the stretch's first run to
        // its last, the longest run as its worst, the runs counted in its note
        foreach (var (from, to) in Info(ser).Present)
        {
            var inIt = runs.Where(f => f.TStart >= from && f.TStart <= to).OrderBy(f => f.TStart).ToList();
            if (inIt.Count == 0) continue;
            var first = inIt[0];
            o.Add(first with { TEnd = inIt.Max(f => f.TEnd), Worst = inIt.Max(f => f.Worst), Samples = inIt.Sum(f => f.Samples),
                               Note = inIt.Count == 1 ? first.Note : $"{first.Note} ({inIt.Count} runs in this stretch)" });
        }
    }

    /// <summary>WO-207 G.1: a queued action that waits longer than <see cref="AnimLimits.StuckAgeMs"/> without starting, on a living body that is
    /// not in its normal idle (the probe's queue age with its queue detail; a probe file without them is not judged: its queue length alone
    /// flagged healthy NPCs, and the first queue age counted actions waiting to resume).</summary>
    private static void Stuck(AnimSeries ser, List<AnimFinding> o, bool[]? far = null)
    {
        var S = ser.S;
        if (!S.Any(x => x.QOutrank >= 0)) return;
        far ??= new bool[S.Count];
        var runs = new List<AnimFinding>();
        Runs(ser, "stuck", i =>
        {
            if (S[i].QOutrank < 0 || far[i]) return null;
            if (S[i].Dead || S[i].Rag || IsNormalIdle(S[i].Fb, S[i].FbFrag)) return false;
            return S[i].QOutrank > AnimLimits.StuckAgeMs;
        }, i => S[i].QOutrank / 1000.0, 0, false, null, "", runs);
        foreach (var f in runs)   // the note names what the body was doing when it began
        {
            var st = S.First(x => x.T >= f.TStart);
            o.Add(f with { Note = $"a queued action never started behind {st.Fb ?? "empty"} fragment {st.FbFrag}" });
        }
    }

    internal static double HDist(double x1, double y1, double x2, double y2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
    internal static double Dist3(in AnimSample a, in AnimSample b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
    internal static double Dist3(float[] a, float[] b) =>
        Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
}
