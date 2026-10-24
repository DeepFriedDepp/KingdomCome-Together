// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Net;
using System.Text;

namespace KcdMp.LogPack;

/// <summary>
/// WO-206 D: what the animation detectors found, written twice: the "## Animation" section every BRIEFING.md ends with (counts per
/// detector, the ten worst bodies), and anim/anim-report.html (a table per detector and every body's timeline as an inline
/// SVG strip). The page is self-contained (no script, no font, no link: it opens from inside the bundle anywhere) and well-formed
/// XML as well as HTML, so a test can parse it.
/// </summary>
public static class AnimReport
{
    public const string NoProbeLine = "## Animation -- no probe data in this bundle";
    public const int WorstBodies = 10;
    private const int MaxRowsPerTable = 200, MaxStrips = 60, StripWidth = 720;

    public static string Time(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().ToString("HH:mm:ss.f", CultureInfo.InvariantCulture);
    private static string N(int v) => v.ToString("N0", CultureInfo.InvariantCulture);
    private static string F(double v, string fmt = "F2") => v.ToString(fmt, CultureInfo.InvariantCulture);
    public static string Value(AnimFinding f) => $"{F(f.Worst, f.Detector is "jitter" or "stuck" ? "F0" : "F2")} {AnimDetect.Unit(f.Detector)}";

    /// <summary>The briefing's section: <see cref="NoProbeLine"/> without a probe file, the error when it could not be read.</summary>
    public static string Section(AnimAnalysis? an, string? error = null)
    {
        var sb = new StringBuilder();
        if (an is null)
        {
            if (error is null) return NoProbeLine + "\n\n";
            return "## Animation\n\nThe probe file (" + AnimStream.BundleEntry + ") could not be read: " + error + "\n\n";
        }
        var s = an.Stream;
        sb.Append("## Animation\n\n");
        sb.Append($"The animation probe ({AnimStream.BundleEntry}, WO-206 D): {N(s.Lines)} lines, {an.Bodies.Count} bodies, {N(s.Samples)} samples");
        if (s.Samples > 0) sb.Append($", {Time(s.T0)} to {Time(s.T1)}");
        sb.Append($", machine(s): {(s.Machines.Count == 0 ? "?" : string.Join(", ", s.Machines))}");
        if (s.BadLines + s.BadActors > 0) sb.Append($" ({s.BadLines} broken lines and {s.BadActors} broken bodies skipped)");
        sb.Append($". Every finding with its timeline: {AnimStream.ReportEntry}.\n\n");
        sb.Append("| detector | what it means | findings | bodies |\n|---|---|---|---|\n");
        foreach (var d in AnimDetect.All)
            sb.Append($"| {d} | {AnimDetect.Meaning(d)} | {(an.Enabled.Contains(d) ? an.Count(d).ToString(CultureInfo.InvariantCulture) : "off")} | {(an.Enabled.Contains(d) ? an.BodyCount(d).ToString(CultureInfo.InvariantCulture) : "off")} |\n");
        sb.Append('\n');
        var worst = an.WorstBodies(WorstBodies);
        if (worst.Count == 0) { sb.Append("No finding: every body passed every detector that ran.\n\n"); return sb.ToString(); }
        sb.Append($"The worst {worst.Count} bodies, each with its worst finding:\n\n");
        sb.Append("| body | kind | machine | worst finding | from | for | worst value | findings |\n|---|---|---|---|---|---|---|---|\n");
        foreach (var (f, n) in worst)
            sb.Append($"| {f.Body} | {(f.Kind.Length == 0 ? "?" : f.Kind)} | {f.Machine} | {f.Detector} ({f.Note}) | {Time(f.TStart)} | {F(f.Seconds, "F1")} s | {Value(f)} | {n} |\n");
        sb.Append('\n');
        return sb.ToString();
    }

    private static readonly Dictionary<string, string> Colour = new(StringComparer.Ordinal)
    {
        ["tpose"] = "#d6336c", ["sliding"] = "#f08c00", ["floating"] = "#1c7ed6", ["sinking"] = "#6741d9",
        ["jitter"] = "#e8590c", ["rubberband"] = "#c92a2a", ["deathpose"] = "#495057", ["stuck"] = "#2b8a3e",
    };

    private static string E(string s) => WebUtility.HtmlEncode(s);

    /// <summary>anim/anim-report.html: small (at most 200 rows per table, 60 strips), no external anything.</summary>
    public static string Html(AnimAnalysis an)
    {
        var s = an.Stream;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\"/>\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>\n");
        sb.Append("<title>Animation report</title>\n<style>\n");
        sb.Append(":root{--bg:#ffffff;--fg:#1f2328;--muted:#656d76;--line:#d0d7de;--track:#d0d7de}\n");
        sb.Append("@media (prefers-color-scheme: dark){:root{--bg:#0d1117;--fg:#e6edf3;--muted:#8d96a0;--line:#30363d;--track:#30363d}}\n");
        sb.Append("body{background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,sans-serif;margin:16px;max-width:1100px}\n");
        sb.Append("table{border-collapse:collapse;margin:8px 0 20px;font-size:13px}th,td{border:1px solid var(--line);padding:3px 8px;text-align:left;vertical-align:middle}\n");
        sb.Append("th{font-weight:600}.muted{color:var(--muted)}.key{display:inline-block;width:10px;height:10px;margin:0 4px 0 12px;border-radius:2px}\n");
        sb.Append("svg{display:block}.wrap{overflow-x:auto}\n</style>\n</head>\n<body>\n");
        sb.Append("<h1>Animation report</h1>\n");
        sb.Append("<p class=\"muted\">WO-206 D: the bundle's animation probe (").Append(E(AnimStream.BundleEntry)).Append($"), {N(s.Lines)} lines, {an.Bodies.Count} bodies, {N(s.Samples)} samples");
        if (s.Samples > 0) sb.Append($", {Time(s.T0)} to {Time(s.T1)} (this machine's clock)");
        sb.Append(", machine(s): ").Append(E(string.Join(", ", s.Machines))).Append(".</p>\n");

        // the counts
        sb.Append("<table>\n<tr><th>detector</th><th>what it means</th><th>findings</th><th>bodies</th></tr>\n");
        foreach (var d in AnimDetect.All)
        {
            bool on = an.Enabled.Contains(d);
            sb.Append($"<tr><td><span class=\"key\" style=\"background:{Colour[d]}\"></span>{d}</td><td>{E(AnimDetect.Meaning(d))}</td><td>{(on ? an.Count(d).ToString(CultureInfo.InvariantCulture) : "off")}</td><td>{(on ? an.BodyCount(d).ToString(CultureInfo.InvariantCulture) : "off")}</td></tr>\n");
        }
        sb.Append("</table>\n");

        // a table per detector
        foreach (var d in AnimDetect.All)
        {
            sb.Append($"<h2 id=\"{d}\">{d}</h2>\n");
            if (!an.Enabled.Contains(d)) { sb.Append("<p class=\"muted\">Switched off.</p>\n"); continue; }
            var rows = an.Findings.Where(f => f.Detector == d).OrderByDescending(f => f.Score).ThenBy(f => f.TStart).ToList();
            if (rows.Count == 0) { sb.Append("<p class=\"muted\">None.</p>\n"); continue; }
            sb.Append("<table>\n<tr><th>body</th><th>kind</th><th>machine</th><th>from</th><th>to</th><th>for</th><th>worst</th><th>samples</th><th>note</th></tr>\n");
            foreach (var f in rows.Take(MaxRowsPerTable))
                sb.Append($"<tr><td>{E(f.Body)}</td><td>{E(f.Kind)}</td><td>{E(f.Machine)}</td><td>{Time(f.TStart)}</td><td>{Time(f.TEnd)}</td><td>{F(f.Seconds, "F1")} s</td><td>{E(Value(f))}</td><td>{f.Samples}</td><td>{E(f.Note)}</td></tr>\n");
            sb.Append("</table>\n");
            if (rows.Count > MaxRowsPerTable) sb.Append($"<p class=\"muted\">And {rows.Count - MaxRowsPerTable} more.</p>\n");
        }

        // per-body timelines: the bodies with findings first (worst first), then the others, up to MaxStrips
        sb.Append("<h2 id=\"timelines\">Timelines</h2>\n<p class=\"muted\">Each strip runs over the whole file; grey = the probe saw the body; a coloured block = a finding.");
        foreach (var d in AnimDetect.All) sb.Append($"<span class=\"key\" style=\"background:{Colour[d]}\"></span>{d}");
        sb.Append("</p>\n");
        var score = an.Findings.GroupBy(f => (f.Machine, f.Body)).ToDictionary(g => g.Key, g => g.Max(f => f.Score));
        var bodies = an.Bodies.OrderByDescending(b => score.TryGetValue((b.Machine, b.Name), out var v) ? v : -1)
                              .ThenBy(b => b.Machine, StringComparer.Ordinal).ThenBy(b => b.Name, StringComparer.Ordinal).ToList();
        long t0 = s.T0, span = Math.Max(1, s.T1 - s.T0);
        double X(long t) => Math.Round((t - t0) * (double)StripWidth / span, 1);
        sb.Append("<div class=\"wrap\"><table>\n<tr><th>body</th><th>machine</th><th>findings</th><th>").Append(s.Samples > 0 ? $"{Time(s.T0)} to {Time(s.T1)}" : "").Append("</th></tr>\n");
        foreach (var b in bodies.Take(MaxStrips))
        {
            var fs = an.Findings.Where(f => f.Machine == b.Machine && f.Body == b.Name).ToList();
            sb.Append($"<tr><td>{E(b.Name)}</td><td>{E(b.Machine)}</td><td>{fs.Count}</td><td>");
            sb.Append($"<svg width=\"{StripWidth}\" height=\"14\" viewBox=\"0 0 {StripWidth} 14\" role=\"img\" aria-label=\"{E(b.Name)}: {fs.Count} findings\">");
            foreach (var (from, to) in b.Present)
                sb.Append($"<rect x=\"{F(X(from), "F1")}\" y=\"6\" width=\"{F(Math.Max(1, X(to) - X(from)), "F1")}\" height=\"2\" style=\"fill:var(--track)\"/>");
            foreach (var f in fs)
                sb.Append($"<rect x=\"{F(X(f.TStart), "F1")}\" y=\"1\" width=\"{F(Math.Max(2, X(f.TEnd) - X(f.TStart)), "F1")}\" height=\"12\" fill=\"{Colour[f.Detector]}\"><title>{E($"{f.Detector} {Time(f.TStart)} {F(f.Seconds, "F1")} s, {Value(f)}, {f.Note}")}</title></rect>");
            sb.Append("</svg></td></tr>\n");
        }
        sb.Append("</table></div>\n");
        if (bodies.Count > MaxStrips) sb.Append($"<p class=\"muted\">And {bodies.Count - MaxStrips} more bodies without a strip.</p>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }
}
