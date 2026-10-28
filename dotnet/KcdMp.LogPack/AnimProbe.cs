// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using System.Text.Json;

namespace KcdMp.LogPack;

/// <summary>
/// WO-206 D: one body at one probe tick. A struct in a list: a two-hour session with thirty bodies in range is two million of
/// them, so only what the detectors and the comparison read is kept (the rotation and the per-scope priorities are not).
/// </summary>
public struct AnimSample
{
    public long T;              // unix ms, the writing machine's clock
    public long F;              // the game's frame number (-1 when the line had none)
    public float X, Y, Z;       // metres, z up
    public float Gz;            // the terrain's z under the body; NaN = unknown (the probe wrote null or nothing)
    public bool Dead, Rag, Weapon, Mounted;
    public int Q;               // queued actions
    public int Qa;              // WO-207 G.1: ms the oldest queued action has waited without starting; -1 = not in the line (an older probe)
    public bool Qd;             // WO-207 G.1: the line carried the queue detail ("qd")
    public int QOutrank;        // WO-207 G.1: ms the oldest queued action that outranks the running FullBody action (or faces an empty one) has
                                // waited; -1 = no priorities in the line (a queued action waiting behind a higher one is the engine's normal)
    public string? Tg;          // WO-207 G.2: the body's Mannequin tag state (hex), null = not in the line
    public string? Fb;          // the FullBody scope's action class; null = the scope is empty
    public int FbFrag;          // its fragment; -1 = unknown
    public int Scopes;          // installed scopes holding an action
    public AnimBones? Bones;    // only on the few bodies the probe samples bones for
    // WO-210 0.3 (null / -1 in an older probe): the FullBody's fragment name, its whole-body family and (a copy) the host NPC's family as
    // the owner last applied it, the combat mode, the opponent's name
    public string? FbName, Fam, Hf, Op;
    public sbyte Cm;
}

/// <summary>WO-206 D: the optional bones (world metres). Any one may be missing.</summary>
public sealed class AnimBones
{
    public float[]? Root, Spine, Head, LHand, RHand, LFoot, RFoot;
}

/// <summary>WO-206 D: every sample of one body on one machine, in time order.</summary>
public sealed class AnimSeries
{
    public string Machine = "", Name = "", Kind = "";
    public List<AnimSample> S = new();
}

/// <summary>
/// WO-206 D: the per-body animation probe's file (kcdmp-anim.jsonl, written by the game DLL into the game folder, 10 Hz, every
/// tracked body within 40 m). One JSON object per line; the parser tolerates missing optional fields, a null terrain z, unknown
/// extra fields and broken lines (counted, skipped): the probe is written live and a bundle can cut its last line in half.
/// </summary>
public sealed class AnimStream
{
    public const string FileName = "kcdmp-anim.jsonl";
    /// <summary>Where the probe file sits inside a bundle: the bundle's anim folder (the bundle had none before WO-206 D).</summary>
    public const string BundleEntry = "anim/" + FileName;
    public const string ReportEntry = "anim/anim-report.html";

    public readonly Dictionary<(string Machine, string Name), AnimSeries> Bodies = new();
    public int Lines, BadLines, BadActors, Samples;
    public long T0 = long.MaxValue, T1 = long.MinValue;
    public readonly SortedSet<string> Machines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pool = new(StringComparer.Ordinal);   // action class names, stored once

    public static AnimStream Parse(byte[] bytes)
    {
        using var r = new StreamReader(new MemoryStream(bytes, false), Encoding.UTF8, false, 1 << 16);
        return Parse(r);
    }

    public static AnimStream Parse(TextReader r)
    {
        var s = new AnimStream();
        string? l;
        while ((l = r.ReadLine()) is not null) s.AddLine(l);
        s.Finish();
        return s;
    }

    public void AddLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Lines++;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Long(root, "t", out long t)) { BadLines++; return; }
            string m = Str(root, "m") ?? "unknown";
            long f = Long(root, "f", out long ff) ? ff : -1;
            Machines.Add(m);
            if (!root.TryGetProperty("a", out var a) || a.ValueKind != JsonValueKind.Array) return;   // a tick with no body in range
            foreach (var act in a.EnumerateArray()) AddActor(m, t, f, act);
        }
        catch (JsonException) { BadLines++; }
    }

    private void AddActor(string m, long t, long f, JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) { BadActors++; return; }
        string? name = Str(a, "n");
        float[]? p = Vec(a, "p");
        if (name is null || p is null) { BadActors++; return; }
        long fbPrio = long.MinValue;
        var smp = new AnimSample
        {
            T = t, F = f, X = p[0], Y = p[1], Z = p[2],
            Gz = a.TryGetProperty("gz", out var gz) && gz.ValueKind == JsonValueKind.Number ? (float)gz.GetDouble() : float.NaN,
            Dead = Flag(a, "dead"), Rag = Flag(a, "rag"), Weapon = Flag(a, "w"),
            Mounted = Flag(a, "mnt") || Flag(a, "mounted"),   // not in the line format yet; read if a later probe adds it
            Q = Long(a, "q", out long q) ? (int)q : 0,
            Qa = Long(a, "qa", out long qa) ? (int)qa : -1,
            Qd = a.TryGetProperty("qd", out var qdEl) && qdEl.ValueKind == JsonValueKind.Array,
            QOutrank = -1,
            Tg = Str(a, "tg") is { Length: > 0 } tg ? Intern(tg) : null,
            FbFrag = -1,
            Fam = Str(a, "fam") is { Length: > 0 } fam ? Intern(fam) : null,
            Hf = Str(a, "hf") is { Length: > 0 } hf && hf != "-" ? Intern(hf) : null,
            Op = Str(a, "op") is { Length: > 0 } op ? Intern(op) : null,
            Cm = (sbyte)(Long(a, "cm", out long cmv) ? (cmv != 0 ? 1 : 0) : -1),
        };
        if (a.TryGetProperty("sc", out var sc) && sc.ValueKind == JsonValueKind.Array)
            foreach (var e in sc.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                string? c = Str(e, "c");
                if (string.IsNullOrEmpty(c)) continue;   // listed but holding nothing = empty
                smp.Scopes++;
                c = Intern(c);
                // a rider is carried by the horse: no locomotion of his own, his height is the saddle's
                if (c.Contains("Ride", StringComparison.OrdinalIgnoreCase) || c.Contains("Mount", StringComparison.OrdinalIgnoreCase)
                    || c.Contains("Horse", StringComparison.OrdinalIgnoreCase)) smp.Mounted = true;
                if (string.Equals(Str(e, "s"), "FullBody", StringComparison.OrdinalIgnoreCase))
                {
                    smp.Fb = c;
                    smp.FbFrag = Long(e, "fr", out long fr) ? (int)fr : -1;
                    smp.FbName = Str(e, "fn") is { Length: > 0 } fnm ? Intern(fnm) : null;
                    fbPrio = Long(e, "pr", out long pr) ? pr : long.MinValue;
                }
            }
        // WO-207 G.1: [flags, waited ms, priority] per queued action; only a probe that writes the priority is judged
        if (smp.Qd)
            foreach (var qe in qdEl.EnumerateArray())
            {
                if (qe.ValueKind != JsonValueKind.Array || qe.GetArrayLength() < 3) continue;
                long age = qe[1].TryGetInt64(out long ag) ? ag : -1, prio = qe[2].TryGetInt64(out long pq) ? pq : long.MinValue;
                if (age < 0 || prio == long.MinValue) continue;
                if (smp.QOutrank < 0) smp.QOutrank = 0;
                if ((smp.Fb is null || prio > fbPrio) && age > smp.QOutrank) smp.QOutrank = (int)Math.Min(age, int.MaxValue);
            }
        if (a.TryGetProperty("b", out var b) && b.ValueKind == JsonValueKind.Object)
        {
            var bo = new AnimBones { Root = Vec(b, "root"), Spine = Vec(b, "spine"), Head = Vec(b, "head"), LHand = Vec(b, "lhand"),
                                     RHand = Vec(b, "rhand"), LFoot = Vec(b, "lfoot"), RFoot = Vec(b, "rfoot") };
            if (bo.Root is not null || bo.Head is not null || bo.LHand is not null || bo.LFoot is not null) smp.Bones = bo;
        }
        if (!Bodies.TryGetValue((m, name), out var ser)) Bodies[(m, name)] = ser = new AnimSeries { Machine = m, Name = name };
        string? kind = Str(a, "k");
        if (kind is not null) ser.Kind = Intern(kind);
        ser.S.Add(smp);
        Samples++;
        if (t < T0) T0 = t;
        if (t > T1) T1 = t;
    }

    /// <summary>Every body's samples in time order (the probe writes them so; a merged or appended file may not be).</summary>
    public void Finish()
    {
        foreach (var ser in Bodies.Values)
        {
            bool sorted = true;
            for (int i = 1; i < ser.S.Count && sorted; i++) sorted = ser.S[i].T >= ser.S[i - 1].T;
            if (!sorted) ser.S = ser.S.OrderBy(x => x.T).ToList();   // OrderBy is stable: same-tick samples keep their order
        }
    }

    private string Intern(string s) { if (_pool.TryGetValue(s, out var v)) return v; _pool[s] = s; return s; }

    private static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Long(JsonElement e, string k, out long v)
    {
        v = 0;
        if (!e.TryGetProperty(k, out var x) || x.ValueKind != JsonValueKind.Number) return false;
        if (x.TryGetInt64(out v)) return true;
        v = (long)x.GetDouble();
        return true;
    }

    private static bool Flag(JsonElement e, string k)
    {
        if (!e.TryGetProperty(k, out var x)) return false;
        return x.ValueKind switch { JsonValueKind.True => true, JsonValueKind.Number => x.GetDouble() != 0, _ => false };
    }

    private static float[]? Vec(JsonElement e, string k)
    {
        if (!e.TryGetProperty(k, out var x) || x.ValueKind != JsonValueKind.Array || x.GetArrayLength() < 3) return null;
        var o = new float[3];
        for (int i = 0; i < 3; i++)
        {
            var c = x[i];
            if (c.ValueKind != JsonValueKind.Number) return null;
            o[i] = (float)c.GetDouble();
        }
        return o;
    }
}
