// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;
using System.Text;
using KcdMp.LogPack;
using ZstdSharp;

// usage: KcdMp.LogPack.Tool <folder holding a bundle's files> <out folder> [--earlier] [--max-part-bytes N] [--no-verify]
// The folder is a bundle as the launcher collects it (kcd.log, agent.log, history/..., leash/...). Prints sizes and checks the promises.
if (args.Length >= 2 && args[0] == "brief")
{
    // WO-202: the briefing alone, for a folder of a bundle's files (the field analysis)
    var bf = Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories)
        .Select(f => new SourceFile(Path.GetRelativePath(args[1], f).Replace('\\', '/'), f)).ToList();
    Console.Write(Briefing.Build(Briefing.Read(bf)));
    return;
}
string src = args[0], outDir = args[1];
bool earlier = args.Contains("--earlier"), verify = !args.Contains("--no-verify");
var opt = new PackOptions();
int mi = Array.IndexOf(args, "--max-part-bytes"); if (mi >= 0) opt.MaxPartBytes = long.Parse(args[mi + 1]);

var files = new List<SourceFile>();
long before = 0;
foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
{
    string rel = Path.GetRelativePath(src, f).Replace('\\', '/');
    if (!earlier && rel.StartsWith("history/", StringComparison.Ordinal)) continue;
    files.Add(new SourceFile(rel, f));
    before += new FileInfo(f).Length;
}
Directory.CreateDirectory(outDir);
var sw = Stopwatch.StartNew();
var res = Packer.Pack(files, outDir, "bundle", opt, s => Console.WriteLine("  " + s));
sw.Stop();
Console.WriteLine($"files {files.Count}, original {before:N0} bytes -> {res.Parts.Count} part(s), {res.PartBytes:N0} bytes, {sw.Elapsed.TotalSeconds:F1} s, marks {res.OddMarks}");
foreach (var p in res.Parts) Console.WriteLine($"  {Path.GetFileName(p)} {new FileInfo(p).Length:N0}");
Console.WriteLine($"lines in {res.Totals.LinesIn:N0}, kept {res.Totals.LinesKept:N0}, collapsed {res.Totals.Elided:N0}, summaries {res.Totals.Summaries:N0}, balanced {res.Totals.Balanced}");
if (!verify) return;

// ---- the promises, checked against what was written
var outFiles = new Dictionary<string, byte[]>();
foreach (var p in res.Parts)
{
    using var ds = new DecompressionStream(File.OpenRead(p));
    using var ms = new MemoryStream(); ds.CopyTo(ms);
    var tar = ms.ToArray(); int at = 0;
    while (at + 512 <= tar.Length && tar.AsSpan(at, 512).ToArray().Any(b => b != 0))
    {
        string name = Encoding.UTF8.GetString(tar, at, 100).TrimEnd('\0');
        string prefix = Encoding.UTF8.GetString(tar, at + 345, 155).TrimEnd('\0'); if (prefix.Length > 0) name = prefix + "/" + name;
        long size = Convert.ToInt64(Encoding.ASCII.GetString(tar, at + 124, 11).Trim('\0', ' '), 8);
        outFiles[name] = tar.AsSpan(at + 512, (int)size).ToArray();
        at += 512 + (int)((size + 511) / 512 * 512);
    }
}
int bad = 0;
var wins = new OddWindows();
foreach (var f in files.Where(f => f.EntryName.EndsWith(".log")))
    using (var r = new StreamReader(f.Path!, Encoding.Latin1)) wins.Scan(Lines(r));
foreach (var rep in res.Files.Where(r => r.Collapse is not null))
{
    var orig = File.ReadAllText(files.First(f => f.EntryName == rep.Entry).Path!, Encoding.Latin1).Split('\n');
    if (orig.Length > 0 && orig[^1].Length == 0) orig = orig[..^1];
    var kept = new HashSet<string>(StringComparer.Ordinal);
    // the output lines in order; a subsequence check for the windowed lines
    var outLines = Encoding.Latin1.GetString(outFiles[rep.Entry]).Split('\n');
    if (outLines.Length > 0 && outLines[^1].Length == 0) outLines = outLines[..^1];
    var clock = new LineClock();
    int oi = 0, miss = 0, wcount = 0, protectedMiss = 0, protectedN = 0;
    foreach (var l0 in orig)
    {
        string l = l0.TrimEnd('\r');
        double t = clock.Advance(l);
        bool must = wins.Inside(t), prot = NoiseRules.IsProtected(l);
        if (!must && !prot) continue;
        if (must) wcount++; if (prot) protectedN++;
        int j = oi; while (j < outLines.Length && outLines[j].TrimEnd('\r') != l) j++;
        if (j == outLines.Length) { if (must) miss++; if (prot) protectedMiss++; } else oi = j + 1;
    }
    Console.WriteLine($"  verify {rep.Entry}: window lines {wcount:N0} missing {miss}, protected lines {protectedN:N0} missing {protectedMiss}, lines in {rep.Collapse!.LinesIn:N0} = kept {rep.Collapse.LinesKept:N0} + collapsed {rep.Collapse.Elided:N0} ({(rep.Collapse.Balanced ? "balanced" : "NOT BALANCED")}), output lines {outLines.Length:N0} = kept + summaries {rep.Collapse.LinesKept + rep.Collapse.Summaries:N0}");
    if (miss > 0 || protectedMiss > 0 || !rep.Collapse.Balanced || outLines.Length != rep.Collapse.LinesKept + rep.Collapse.Summaries) bad++;
}
// every non-collapsed file byte-equal
foreach (var rep in res.Files.Where(r => r.Collapse is null && r.SameAs is null && !outFiles.ContainsKey(r.Entry) && !outFiles.Keys.Any(k => k.StartsWith(r.Entry + ".piece"))))
{ Console.WriteLine("  MISSING " + rep.Entry); bad++; }
Console.WriteLine(bad == 0 ? "VERIFY OK" : $"VERIFY FAILED ({bad})");
Environment.ExitCode = bad == 0 ? 0 : 1;

static IEnumerable<string> Lines(TextReader r) { string? l; while ((l = r.ReadLine()) is not null) yield return l; }
