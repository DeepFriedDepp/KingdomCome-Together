// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace KcdMp.LogPack;

/// <summary>WO-206 D: the comparison's switches (each check's twin turns one off).</summary>
public sealed class AnimCompareOptions
{
    /// <summary>Search the stream delay (off = compare at the same clock time, offset 0).</summary>
    public bool EstimateDelay { get; set; } = true;
    /// <summary>Compare the FullBody action and fragment (off = the share is not measured).</summary>
    public bool ComparePose { get; set; } = true;
    /// <summary>The delay search: 0 .. this, coarse in <see cref="StepMs"/>, then to the millisecond around the best.</summary>
    public long MaxOffsetMs { get; set; } = 1500;
    public int StepMs { get; set; } = 10;
}

public sealed record AnimBodyCompare(string Name, int Pairs, double P50, double P95, double? PoseMismatch, double WeaponMismatch);

public sealed class AnimCompareResult
{
    public int Npcs, Pairs;
    public long OffsetMs;
    public bool OffsetEstimated, OffsetAtEdge;
    public double P50, P95, WeaponMismatch;
    public double? PoseMismatch;
    public List<AnimBodyCompare> Bodies = new();
    public string Section = "";
}

/// <summary>
/// WO-206 D: the joiner's copy of every NPC against the host's real NPC, from the two machines' probe files. The joiner's copy
/// shows the host's NPC as it was one stream delay ago, so each joiner sample is compared with the host's NPC at (its time minus
/// a constant offset); the offset is the one in 0..1500 ms that makes the positions agree best. It also absorbs any difference
/// between the two machines' clocks (both write unix time), which the report says.
/// </summary>
public static class AnimCompare
{
    public const string Heading = "## Animation, host vs joiner";
    /// <summary>One position error counts at most this much in the delay search (a load's teleport must not steer it).</summary>
    private const double ClipM = 5.0;
    /// <summary>The delay search uses at most this many joiner samples (spread evenly): seconds, not minutes, on a long session.</summary>
    private const int SearchSamples = 20000;

    public static AnimCompareResult Compare(AnimStream host, AnimStream joiner, AnimCompareOptions? opt = null)
    {
        opt ??= new AnimCompareOptions();
        var res = new AnimCompareResult();
        // the host's real NPCs, the joiner's copies of them, by name (a name seen under two roles: the series with more samples)
        var hs = Pick(host, ser => ser.Machine != "joiner" && ser.Kind is "npc" or "");
        var js = Pick(joiner, ser => ser.Machine != "host" && ser.Kind is "copy" or "npc" or "");
        var pairs = hs.Keys.Intersect(js.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).Select(n => (Name: n, H: hs[n], J: js[n])).ToList();
        res.Npcs = pairs.Count;

        if (opt.EstimateDelay && pairs.Count > 0)
        {
            int total = pairs.Sum(p => p.J.S.Count);
            int stride = Math.Max(1, total / SearchSamples);
            double Cost(long off)
            {
                double sum = 0; int n = 0, k = 0;
                foreach (var (_, h, j) in pairs)
                    foreach (var js0 in j.S)
                    {
                        if (k++ % stride != 0) continue;
                        if (!At(h.S, js0.T - off, out var x, out var y, out var z, out _)) continue;
                        sum += Math.Min(ClipM, Dist(x, y, z, js0)); n++;
                    }
                return n == 0 ? double.MaxValue : sum / n;
            }
            long best = 0; double bestCost = double.MaxValue;
            for (long off = 0; off <= opt.MaxOffsetMs; off += opt.StepMs)
            { double c = Cost(off); if (c < bestCost) { bestCost = c; best = off; } }
            long lo = Math.Max(0, best - opt.StepMs), hi = Math.Min(opt.MaxOffsetMs, best + opt.StepMs);
            for (long off = lo; off <= hi; off++)
            { double c = Cost(off); if (c < bestCost) { bestCost = c; best = off; } }
            res.OffsetMs = best;
            res.OffsetEstimated = true;
            res.OffsetAtEdge = best == 0 || best == opt.MaxOffsetMs;
        }

        var all = new List<double>();
        int poseN = 0, poseBad = 0, wBad = 0;
        foreach (var (name, h, j) in pairs)
        {
            var errs = new List<double>();
            int bp = 0, bpBad = 0, bw = 0;
            foreach (var x in j.S)
            {
                if (!At(h.S, x.T - res.OffsetMs, out var hx, out var hy, out var hz, out int idx)) continue;
                errs.Add(Dist(hx, hy, hz, x));
                var hsm = h.S[idx];   // the host's sample in effect at that moment
                if (opt.ComparePose) { bp++; if (!string.Equals(hsm.Fb, x.Fb, StringComparison.Ordinal) || hsm.FbFrag != x.FbFrag) bpBad++; }
                if (hsm.Weapon != x.Weapon) bw++;
            }
            if (errs.Count == 0) continue;
            all.AddRange(errs);
            poseN += bp; poseBad += bpBad; wBad += bw;
            res.Bodies.Add(new AnimBodyCompare(name, errs.Count, Pct(errs, 50), Pct(errs, 95), opt.ComparePose ? (double)bpBad / bp : null, (double)bw / errs.Count));
        }
        res.Pairs = all.Count;
        if (all.Count > 0)
        {
            res.P50 = Pct(all, 50); res.P95 = Pct(all, 95);
            res.PoseMismatch = opt.ComparePose && poseN > 0 ? (double)poseBad / poseN : null;
            res.WeaponMismatch = (double)wBad / all.Count;
        }
        res.Section = Section(res, opt);
        return res;
    }

    private static Dictionary<string, AnimSeries> Pick(AnimStream s, Func<AnimSeries, bool> keep)
    {
        var d = new Dictionary<string, AnimSeries>(StringComparer.Ordinal);
        foreach (var ser in s.Bodies.Values.Where(keep))
            if (!d.TryGetValue(ser.Name, out var had) || had.S.Count < ser.S.Count) d[ser.Name] = ser;
        return d;
    }

    /// <summary>The host's body at time t, between its two samples (none across a gap the probe did not see).</summary>
    private static bool At(List<AnimSample> S, long t, out double x, out double y, out double z, out int idx)
    {
        x = y = z = 0; idx = -1;
        if (S.Count == 0 || t < S[0].T || t > S[^1].T) return false;
        int lo = 0, hi = S.Count - 1;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (S[mid].T <= t) lo = mid; else hi = mid - 1; }
        idx = lo;
        var a = S[lo];
        if (lo == S.Count - 1 || a.T == t) { x = a.X; y = a.Y; z = a.Z; return true; }
        var b = S[lo + 1];
        if (b.T - a.T > AnimLimits.MaxGapMs) return false;
        double f = (double)(t - a.T) / (b.T - a.T);
        x = a.X + (b.X - a.X) * f; y = a.Y + (b.Y - a.Y) * f; z = a.Z + (b.Z - a.Z) * f;
        return true;
    }

    private static double Dist(double x, double y, double z, in AnimSample s) => Math.Sqrt((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) + (z - s.Z) * (z - s.Z));

    private static double Pct(List<double> v, int p)
    {
        var s = v.OrderBy(x => x).ToList();
        return s[Math.Clamp((int)Math.Ceiling(p / 100.0 * s.Count) - 1, 0, s.Count - 1)];
    }

    private static string P(double share) => (100 * share).ToString("F1", CultureInfo.InvariantCulture) + " %";
    private static string M(double m) => m.ToString("F2", CultureInfo.InvariantCulture) + " m";

    public static string Section(AnimCompareResult r, AnimCompareOptions opt)
    {
        var sb = new StringBuilder();
        sb.Append(Heading).Append("\n\n");
        if (r.Pairs == 0)
        {
            sb.Append($"Compared by anim-compare (WO-206 D): {r.Npcs} NPC(s) in both probe files, no moment both machines saw. Nothing to compare.\n\n");
            return sb.ToString();
        }
        sb.Append($"Compared by anim-compare (WO-206 D): {r.Npcs} NPC(s) seen on both machines, {r.Pairs.ToString("N0", CultureInfo.InvariantCulture)} sample pairs. ");
        if (r.OffsetEstimated)
        {
            sb.Append($"The joiner's copies run {r.OffsetMs} ms behind the host (the constant offset in 0..{opt.MaxOffsetMs} ms that makes the positions agree best; it includes any difference between the two machines' clocks)");
            if (r.OffsetAtEdge) sb.Append(" -- at the edge of the search: the real delay may lie outside it");
            sb.Append(".\n\n");
        }
        else sb.Append("Compared at the same clock time (the delay search was off).\n\n");
        sb.Append("| measure | joiner's copies against the host's NPCs |\n|---|---|\n");
        sb.Append($"| stream delay | {(r.OffsetEstimated ? r.OffsetMs + " ms" : "not estimated")} |\n");
        sb.Append($"| position error, p50 | {M(r.P50)} |\n| position error, p95 | {M(r.P95)} |\n");
        sb.Append($"| full-body action or fragment differs | {(r.PoseMismatch is double pm ? P(pm) : "off")} |\n");
        sb.Append($"| weapon drawn differs | {P(r.WeaponMismatch)} |\n\n");
        sb.Append("| NPC | pairs | position p50 | position p95 | action differs | weapon differs |\n|---|---|---|---|---|---|\n");
        foreach (var b in r.Bodies.OrderByDescending(b => b.P95).ThenBy(b => b.Name, StringComparer.Ordinal).Take(20))
            sb.Append($"| {b.Name} | {b.Pairs} | {M(b.P50)} | {M(b.P95)} | {(b.PoseMismatch is double x ? P(x) : "off")} | {P(b.WeaponMismatch)} |\n");
        if (r.Bodies.Count > 20) sb.Append($"\nAnd {r.Bodies.Count - 20} more NPCs.\n");
        sb.Append('\n');
        return sb.ToString();
    }
}

/// <summary>
/// WO-206 D: a bundle for anim-compare: a packed bundle (.tzst, or any part of a split one) or a folder (an unpacked bundle, or
/// a game folder holding kcdmp-anim.jsonl). Reads the probe file (whole, or from its pieces) and rewrites BRIEFING.md with a
/// section replaced or appended: in a folder the file itself, in a packed bundle the part that holds it (every file its own zstd
/// frame again, the same level the packer uses, written beside it and then moved over it).
/// </summary>
public sealed class AnimBundle
{
    public string Source = "";
    public bool IsFolder;
    public List<string> Parts = new();
    public byte[]? AnimBytes;
    public string? Briefing;
    private string? _briefingPart;
    private List<(string Name, byte[] Data, DateTime Mtime)>? _briefingEntries;

    private static readonly Regex PartName = new(@"^(?<b>.*)-part(?<i>\d+)of(?<n>\d+)\.tzst$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static AnimBundle Open(string path)
    {
        var b = new AnimBundle { Source = path };
        if (Directory.Exists(path))
        {
            b.IsFolder = true;
            string md = System.IO.Path.Combine(path, "BRIEFING.md");
            if (File.Exists(md)) b.Briefing = File.ReadAllText(md, Encoding.UTF8);
            string a1 = System.IO.Path.Combine(path, "anim", AnimStream.FileName), a2 = System.IO.Path.Combine(path, AnimStream.FileName);
            if (File.Exists(a1)) b.AnimBytes = ReadShared(a1);
            else if (File.Exists(a2)) b.AnimBytes = ReadShared(a2);
            else
            {
                var dir = System.IO.Path.Combine(path, "anim");
                if (Directory.Exists(dir))
                    b.AnimBytes = Join(Directory.EnumerateFiles(dir, AnimStream.FileName + ".piece*").Select(f => (System.IO.Path.GetFileName(f), ReadShared(f))), AnimStream.FileName);
            }
            return b;
        }
        if (!File.Exists(path)) throw new FileNotFoundException("no bundle or folder here", path);
        var m = PartName.Match(System.IO.Path.GetFileName(path));
        if (m.Success)
        {
            int n = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";
            for (int i = 1; i <= n; i++)
            {
                string p = System.IO.Path.Combine(dir, $"{m.Groups["b"].Value}-part{i}of{n}.tzst");
                if (File.Exists(p)) b.Parts.Add(p);
            }
        }
        else b.Parts.Add(path);
        var anim = new List<(string, byte[])>();
        foreach (var p in b.Parts)
        {
            var entries = ReadTar(p);
            foreach (var e in entries)
            {
                if (e.Name == "BRIEFING.md") { b.Briefing = Encoding.UTF8.GetString(e.Data); b._briefingPart = p; b._briefingEntries = entries; }
                else if (e.Name == AnimStream.BundleEntry) b.AnimBytes = e.Data;
                else if (e.Name.StartsWith(AnimStream.BundleEntry + ".piece", StringComparison.Ordinal)) anim.Add((e.Name, e.Data));
            }
        }
        if (b.AnimBytes is null && anim.Count > 0) b.AnimBytes = Join(anim, AnimStream.BundleEntry);
        return b;
    }

    /// <summary>The pieces ("name.piece2of3") in order, as one file.</summary>
    internal static byte[] Join(IEnumerable<(string Name, byte[] Data)> pieces, string baseName)
    {
        var ordered = pieces.Select(p => (K: int.TryParse(Regex.Match(p.Name, @"\.piece(\d+)of").Groups[1].Value, out int k) ? k : 0, p.Data)).OrderBy(p => p.K);
        using var ms = new MemoryStream();
        foreach (var (_, d) in ordered) ms.Write(d);
        _ = baseName;
        return ms.ToArray();
    }

    /// <summary>The section under <paramref name="heading"/> replaced (from it to the next "## " heading) or, when absent, appended.</summary>
    public static string ReplaceSection(string md, string heading, string section)
    {
        if (!section.EndsWith('\n')) section += "\n";
        int at = md.StartsWith(heading, StringComparison.Ordinal) ? 0 : md.IndexOf("\n" + heading, StringComparison.Ordinal) is int i && i >= 0 ? i + 1 : -1;
        if (at < 0) return md.TrimEnd('\n') + "\n\n" + section;
        int next = md.IndexOf("\n## ", at + heading.Length, StringComparison.Ordinal);
        return md[..at] + section + (next < 0 ? "" : md[(next + 1)..]);
    }

    /// <summary>Writes the section into this bundle's BRIEFING.md. False when the bundle has none (a bare game folder).</summary>
    public bool WriteSection(string heading, string section)
    {
        if (Briefing is null) return false;
        Briefing = ReplaceSection(Briefing, heading, section);
        if (IsFolder) { File.WriteAllText(System.IO.Path.Combine(Source, "BRIEFING.md"), Briefing, new UTF8Encoding(false)); return true; }
        var opt = new PackOptions();
        using var comp = new Compressor(opt.Level);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, opt.WindowLog);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
        string tmp = _briefingPart! + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var (name, data, mtime) in _briefingEntries!)
                fs.Write(comp.Wrap(Tar.Entry(name, name == "BRIEFING.md" ? Encoding.UTF8.GetBytes(Briefing) : data, name == "BRIEFING.md" ? DateTime.UtcNow : mtime)));
            fs.Write(comp.Wrap(Tar.End()));
        }
        File.Move(tmp, _briefingPart!, true);
        return true;
    }

    private static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>A part's entries in order (every zstd frame read; the ustar headers the packer writes).</summary>
    internal static List<(string Name, byte[] Data, DateTime Mtime)> ReadTar(string path)
    {
        byte[] tar;
        using (var fs = File.OpenRead(path))
        using (var ds = new DecompressionStream(fs))
        using (var ms = new MemoryStream()) { ds.CopyTo(ms); tar = ms.ToArray(); }
        var o = new List<(string, byte[], DateTime)>();
        int at = 0;
        while (at + 512 <= tar.Length)
        {
            if (tar.AsSpan(at, 512).IndexOfAnyExcept((byte)0) < 0) break;
            string name = Encoding.UTF8.GetString(tar, at, 100).TrimEnd('\0');
            string prefix = Encoding.UTF8.GetString(tar, at + 345, 155).TrimEnd('\0');
            if (prefix.Length > 0) name = prefix + "/" + name;
            long size = Convert.ToInt64(Encoding.ASCII.GetString(tar, at + 124, 11).Trim('\0', ' '), 8);
            long mt = Convert.ToInt64(Encoding.ASCII.GetString(tar, at + 136, 11).Trim('\0', ' ') is { Length: > 0 } t ? t : "0", 8);
            o.Add((name, tar.AsSpan(at + 512, (int)size).ToArray(), DateTimeOffset.FromUnixTimeSeconds(mt).UtcDateTime));
            at += 512 + (int)((size + 511) / 512 * 512);
        }
        return o;
    }
}
