// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace KcdMp.LogPack.Tests;

/// <summary>
/// WO-206 D: the animation probe's detectors, the briefing's section, the HTML report and anim-compare. House rule of this WO:
/// every check ships with its mutation twin -- the same test with that detector (or switch) off, where the check must fail. Each
/// is a theory: on = the fault is found, off = it is not (and nothing else finds it either: the fixture isolates its detector).
/// The fixtures are generated here: a walker (locomotion, 1.4 m/s), a stander (idle), a dead ragdoll; 20 s at 10 Hz, bones at 5 Hz.
/// </summary>
public class Wo206AnimTests
{
    private const long T0 = 1_760_000_000_000;
    private const string Walker = "ttkc_man_1", Stander = "ttkc_woman_2", Corpse = "ttkc_bandit_3";

    private sealed class Smp
    {
        public long T; public double X, Y, Z; public double? Gz;
        public string? Fb = "CAnimActionAIMovement"; public int Fr = 4;
        public bool Dead, Rag, W, Bones, ArmsOut; public int Q;
        public (double X, double Y)? FeetAt;   // the feet pinned here (the bone slide)
    }

    private sealed class Body
    {
        public string Name = "", Kind = "npc";
        public List<Smp> S = new();
        public IEnumerable<Smp> Between(double from, double to) => S.Where(s => s.T >= T0 + (long)(from * 1000) && s.T < T0 + (long)(to * 1000));
    }

    /// <summary>The clean scene: nothing in it is wrong.</summary>
    private static List<Body> Clean()
    {
        var walker = new Body { Name = Walker };
        var stander = new Body { Name = Stander };
        var corpse = new Body { Name = Corpse };
        for (int i = 0; i < 200; i++)
        {
            long t = T0 + i * 100;
            walker.S.Add(new Smp { T = t, X = 1.4 * i / 10.0, Y = 0, Z = 10, Gz = 10, Bones = i % 2 == 0 });
            stander.S.Add(new Smp { T = t, X = 5, Y = 5, Z = 12, Gz = 12, Fr = 1, Bones = i % 2 == 0 });
            corpse.S.Add(new Smp { T = t, X = 8, Y = 8, Z = 2.9, Gz = 3.0, Fb = null, Fr = -1, Dead = true, Rag = true });
        }
        return new List<Body> { walker, stander, corpse };
    }

    private static string I(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string V(double x, double y, double z) => $"[{I(x)},{I(y)},{I(z)}]";

    private static string Jsonl(IEnumerable<Body> bodies, string machine = "host")
    {
        var byTick = new SortedDictionary<long, List<string>>();
        foreach (var b in bodies)
            foreach (var s in b.S)
            {
                var a = new StringBuilder();
                a.Append($"{{\"n\":\"{b.Name}\",\"k\":\"{b.Kind}\",\"p\":{V(s.X, s.Y, s.Z)},\"r\":[0,0,1.57],\"dead\":{(s.Dead ? 1 : 0)},\"rag\":{(s.Rag ? 1 : 0)},\"q\":{s.Q},\"sc\":[");
                if (s.Fb is not null) a.Append($"{{\"s\":\"FullBody\",\"c\":\"{s.Fb}\",\"fr\":{s.Fr},\"pr\":2,\"h\":-1}},");
                a.Append("{\"s\":\"HoldItem\",\"c\":\"C_HoldItemAction\",\"fr\":0,\"pr\":1,\"h\":-1}]");
                a.Append($",\"gz\":{(s.Gz is double g ? I(g) : "null")},\"w\":{(s.W ? 1 : 0)}");
                if (s.Bones)
                {
                    double hx = s.ArmsOut ? 0.8 : 0.25, hz = s.ArmsOut ? 1.55 : 0.9;
                    var (fx, fy) = s.FeetAt ?? (s.X, s.Y);
                    a.Append($",\"b\":{{\"root\":{V(s.X, s.Y, s.Z + 1)},\"spine\":{V(s.X, s.Y, s.Z + 1.3)},\"head\":{V(s.X, s.Y, s.Z + 1.7)}");
                    a.Append($",\"lhand\":{V(s.X - hx, s.Y, s.Z + hz)},\"rhand\":{V(s.X + hx, s.Y, s.Z + hz)}");
                    a.Append($",\"lfoot\":{V(fx, fy - 0.12, s.Z + 0.05)},\"rfoot\":{V(fx, fy + 0.12, s.Z + 0.05)}}}");
                }
                a.Append('}');
                if (!byTick.TryGetValue(s.T, out var l)) byTick[s.T] = l = new();
                l.Add(a.ToString());
            }
        var sb = new StringBuilder();
        long f = 1000;
        foreach (var kv in byTick) sb.Append($"{{\"t\":{kv.Key},\"m\":\"{machine}\",\"f\":{f++},\"a\":[{string.Join(",", kv.Value)}]}}\n");
        return sb.ToString();
    }

    private static AnimAnalysis Run(List<Body> bodies, string detector, bool on)
    {
        var set = new HashSet<string>(AnimDetect.All);
        if (!on) set.Remove(detector);
        return AnimDetect.Analyse(AnimStream.Parse(Encoding.UTF8.GetBytes(Jsonl(bodies))), set);
    }

    private static Body B(List<Body> s, string name) => s.First(b => b.Name == name);

    /// <summary>Checks one planted fault: found by its detector when on; with it off nothing at all is found.</summary>
    private static List<AnimFinding> Check(List<Body> scene, string detector, bool on, string body)
    {
        var an = Run(scene, detector, on);
        var mine = an.Findings.Where(f => f.Detector == detector && f.Body == body).ToList();
        Assert.Equal(on, mine.Count > 0);
        if (on) Assert.All(an.Findings, f => Assert.Equal(detector, f.Detector));   // nothing else misfires on the fixture
        else Assert.Empty(an.Findings);
        return mine;
    }

    // ------------------------------------------------------------------ the clean stream

    [Theory]
    [InlineData(false)]
    [InlineData(true)]   // the twin: the same scene with one bind-pose sample run planted is not clean (the check is not vacuous)
    public void A_clean_stream_has_no_findings(bool planted)
    {
        var scene = Clean();
        if (planted) foreach (var s in B(scene, Stander).Between(5, 5.5)) s.Fb = null;
        var an = AnimDetect.Analyse(AnimStream.Parse(Encoding.UTF8.GetBytes(Jsonl(scene))));
        Assert.Equal(3, an.Bodies.Count);
        Assert.Equal(600, an.Stream.Samples);
        Assert.Equal(planted, an.Findings.Count > 0);
    }

    // ------------------------------------------------------------------ one planted fault per detector, each with its twin

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_inserted_bind_pose_is_found_as_tpose(bool on)
    {
        var scene = Clean();
        foreach (var s in B(scene, Stander).Between(5, 5.5)) s.Fb = null;   // FullBody empty for 0.5 s
        var f = Check(scene, "tpose", on, Stander);
        if (on) { Assert.Single(f); Assert.Equal(0.5, f[0].Seconds, 2); Assert.Equal("FullBody empty", f[0].Note); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Arms_straight_out_are_found_as_tpose(bool on)
    {
        var scene = Clean();
        foreach (var s in B(scene, Stander).Between(7, 7.6)) s.ArmsOut = true;   // FullBody still holds its action: only the bones say it
        var f = Check(scene, "tpose", on, Stander);
        if (on) Assert.Equal("arms straight out", Assert.Single(f).Note);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_inserted_slide_is_found_as_sliding(bool on)
    {
        var scene = Clean();
        var st = B(scene, Stander).S;
        for (int i = 0; i < st.Count; i++)
        {
            double t = i / 10.0;
            if (t > 8 && t <= 9.001) { st[i].X = 5 + (t - 8); st[i].Fb = "C_CallbackAction"; st[i].Fr = 0; }   // 1 m/s for 1 s, no locomotion
            else if (t > 9) st[i].X = 6;
        }
        var f = Check(scene, "sliding", on, Stander);
        if (on) { var x = Assert.Single(f); Assert.Equal(1.0, x.Worst, 1); Assert.True(x.Seconds >= 0.9, $"{x.Seconds}"); Assert.Equal("no locomotion action", x.Note); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Feet_that_stay_while_the_root_walks_are_found_as_sliding(bool on)
    {
        var scene = Clean();
        foreach (var s in B(scene, Walker).Between(4, 5.2)) s.FeetAt = (5.6, 0);   // the walker keeps its locomotion action
        var f = Check(scene, "sliding", on, Walker);
        if (on) Assert.Equal("feet do not keep up", Assert.Single(f).Note);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_body_above_the_ground_is_found_as_floating(bool on)
    {
        var scene = Clean();
        foreach (var s in B(scene, Walker).Between(15, 16)) s.Z = 10.6;
        var f = Check(scene, "floating", on, Walker);
        if (on) Assert.Equal(0.6, Assert.Single(f).Worst, 2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_body_below_the_ground_is_found_as_sinking(bool on)
    {
        var scene = Clean();
        foreach (var s in B(scene, Stander).Between(3, 4)) s.Z = 11.5;
        var f = Check(scene, "sinking", on, Stander);
        if (on) Assert.Equal(0.5, Assert.Single(f).Worst, 2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Two_writers_flipping_the_position_are_found_as_jitter(bool on)
    {
        var scene = Clean();
        int k = 0;
        foreach (var s in B(scene, Stander).Between(10, 11)) s.X = 5 + (k++ % 2) * 0.2;
        var f = Check(scene, "jitter", on, Stander);
        if (on) { var x = Assert.Single(f); Assert.True(x.Worst >= AnimLimits.JitterReversals); Assert.Equal("x axis", x.Note); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_jump_away_and_back_is_found_as_rubberband(bool on)
    {
        var scene = Clean();
        B(scene, Walker).Between(12, 12.05).Single().Y = 3;   // one sample 3 m off the path, the next back on it
        var f = Check(scene, "rubberband", on, Walker);
        if (on) Assert.Equal(3.0, Assert.Single(f).Worst, 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_dead_body_walking_and_a_live_one_dying_are_found_as_deathpose(bool on)
    {
        var scene = Clean();
        foreach (var s in B(scene, Corpse).Between(5, 7)) { s.Rag = false; s.Fb = "CAnimActionAIMovement"; s.Fr = 1; }
        foreach (var s in B(scene, Stander).Between(2, 3.5)) s.Fb = "C_DeathAction";
        var an = Run(scene, "deathpose", on);
        var d = an.Findings.Where(f => f.Detector == "deathpose").ToList();
        Assert.Equal(on, d.Any(f => f.Body == Corpse && f.Note == "dead in a living action"));
        Assert.Equal(on, d.Any(f => f.Body == Stander && f.Note == "alive in a death action"));
        if (on) Assert.All(an.Findings, f => Assert.Equal("deathpose", f.Detector)); else Assert.Empty(an.Findings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_queue_that_rises_while_the_action_stands_still_is_found_as_stuck(bool on)
    {
        var scene = Clean();
        foreach (var s in B(scene, Stander).Between(14, 18.5)) s.Q = 1 + (int)((s.T - T0 - 14000) / 1000);   // 1 .. 5
        var f = Check(scene, "stuck", on, Stander);
        if (on) { var x = Assert.Single(f); Assert.Equal(5, x.Worst); Assert.True(x.Seconds >= 4, $"{x.Seconds}"); }
    }

    // ------------------------------------------------------------------ the parser

    [Theory]
    [InlineData("{\"t\":1,\"m\":\"joiner\",\"a\":[{\"n\":\"x\",\"p\":[1,2,3],\"gz\":null}]}", 1, 0)]                      // optional fields missing, gz null
    [InlineData("{\"t\":1,\"m\":\"host\",\"f\":2,\"new\":5,\"a\":[{\"n\":\"x\",\"k\":\"npc\",\"p\":[1,2,3],\"x\":{}}]}", 1, 0)]  // extra fields
    [InlineData("{\"t\":1,\"m\":\"host\",\"a\":[{\"n\":\"x\",\"k\":\"npc\"}]}", 0, 0)]                                      // twin: no position, no sample
    [InlineData("{\"t\":1,\"m\":\"host\",\"a\":[{\"n\":\"x\",\"p\":[1,2", 0, 1)]                                            // twin: a line cut in half
    public void The_parser_tolerates_missing_optional_fields_and_skips_broken_lines(string line, int samples, int bad)
    {
        var s = AnimStream.Parse(Encoding.UTF8.GetBytes(line + "\n"));
        Assert.Equal(samples, s.Samples);
        Assert.Equal(bad, s.BadLines);
        if (samples == 1) { var x = s.Bodies.Values.Single().S[0]; Assert.True(float.IsNaN(x.Gz)); Assert.Null(x.Fb); }
    }

    // ------------------------------------------------------------------ the briefing and the report

    private static List<Body> Faulty()
    {
        var scene = Clean();
        foreach (var s in B(scene, Stander).Between(5, 5.5)) s.Fb = null;
        B(scene, Walker).Between(12, 12.05).Single().Y = 3;
        return scene;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_briefing_gets_the_animation_section_with_counts_and_worst_bodies(bool on)
    {
        var inp = new Briefing.Input { Anim = Run(Faulty(), "tpose", on) };
        var md = Briefing.Build(inp);
        int at = md.IndexOf("\n## Animation\n", StringComparison.Ordinal);
        Assert.True(at > md.IndexOf("## What stood out", StringComparison.Ordinal), "appended after the existing sections");
        string sec = md[at..];
        Assert.Contains("| rubberband | " + AnimDetect.Meaning("rubberband") + " | 1 | 1 |", sec);
        Assert.Contains("| sliding | " + AnimDetect.Meaning("sliding") + " | 0 | 0 |", sec);
        Assert.Contains($"| {Walker} | npc | host | rubberband (away 3.0 m and back) |", sec);
        Assert.Equal(on, sec.Contains("| tpose | " + AnimDetect.Meaning("tpose") + " | 1 | 1 |"));
        Assert.Equal(on, sec.Contains($"| {Stander} | npc | host | tpose (FullBody empty) |"));
        Assert.Equal(!on, sec.Contains("| tpose | " + AnimDetect.Meaning("tpose") + " | off | off |"));
        // the T-pose is the worst body (weight 4) when it runs
        Assert.Equal(on, sec.IndexOf(Stander, StringComparison.Ordinal) is int si && si >= 0 && si < sec.IndexOf($"| {Walker} |", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_html_report_is_written_well_formed_and_self_contained(bool on)
    {
        var an = Run(Faulty(), "tpose", on);
        string html = AnimReport.Html(an);
        Assert.StartsWith("<!DOCTYPE html>", html);
        var doc = XDocument.Parse(html["<!DOCTYPE html>".Length..]);   // well-formed: parses as XML
        foreach (var bad in new[] { "<script", "<link", "@import", "url(", "src=", "http:", "https:", "@font-face" })
            Assert.DoesNotContain(bad, html, StringComparison.OrdinalIgnoreCase);
        Assert.True(html.Length < 100_000, $"{html.Length} bytes");
        Assert.Equal(3, doc.Descendants("svg").Count());   // one strip per body
        // the tpose table: a row with the stander when on; "Switched off." when off
        var h = doc.Descendants("h2").Single(e => (string?)e.Attribute("id") == "tpose");
        var next = h.ElementsAfterSelf().First();
        Assert.Equal(on, next.Name == "table" && next.Descendants("td").Any(td => td.Value == Stander));
        Assert.Equal(!on, next.Value == "Switched off.");
        Assert.Equal(on, doc.Descendants("rect").Any(r => ((string?)r.Element("title"))?.StartsWith("tpose") == true));
    }

    private static string Temp() { var d = Path.Combine(Path.GetTempPath(), "kcdmp-wo206-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]   // the twin: no probe file: the bundle is today's plus the one-line section
    public void The_packer_takes_the_probe_file_from_beside_kcd_log_into_anim(bool withProbe)
    {
        var dir = Temp();
        try
        {
            var game = Path.Combine(dir, "game"); Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "kcd.log"), "Log Started at 2026-10-23  20:00:00\nline\n");
            if (withProbe) File.WriteAllText(Path.Combine(game, AnimStream.FileName), Jsonl(Faulty()));
            var r = Packer.Pack(new[] { new SourceFile("kcd.log", Path.Combine(game, "kcd.log")) }, Path.Combine(dir, "out"), "b");
            var f = PackerTests.Open(r.Parts[0]);
            Assert.Equal(withProbe, f.ContainsKey(AnimStream.BundleEntry));
            Assert.Equal(withProbe, f.ContainsKey(AnimStream.ReportEntry));
            Assert.False(f.ContainsKey(AnimStream.FileName));   // never at the top level
            var md = Encoding.UTF8.GetString(f["BRIEFING.md"]);
            Assert.Equal(!withProbe, md.EndsWith("\n" + AnimReport.NoProbeLine + "\n\n", StringComparison.Ordinal));
            Assert.Equal(withProbe, md.Contains("\n## Animation\n", StringComparison.Ordinal));
            Assert.Equal(withProbe, md.Contains("tpose (FullBody empty)", StringComparison.Ordinal));
            Assert.Contains("## What stood out", md);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ------------------------------------------------------------------ anim-compare

    /// <summary>The host's NPC walks a circle (r 5 m, 2.5 m/s, weapon drawn 20..25 s); the joiner's copy shows it
    /// <paramref name="delayMs"/> late, sampled on its own ticks (37 ms off the host's), its FullBody wrong from 10 to 19 s when
    /// <paramref name="poseFault"/>. A second host NPC the joiner never saw must not count.</summary>
    private static (string Host, string Joiner) Pair(int delayMs, bool poseFault)
    {
        (double X, double Y) Pos(double t) => (5 * Math.Cos(0.5 * t), 5 * Math.Sin(0.5 * t));
        var h = new Body { Name = "ttkc_man_9" };
        var other = new Body { Name = "ttkc_man_10" };
        var j = new Body { Name = "ttkc_man_9", Kind = "copy" };
        for (int i = 0; i < 300; i++)
        {
            double t = i / 10.0;
            var (x, y) = Pos(t);
            h.S.Add(new Smp { T = T0 + i * 100, X = x, Y = y, Z = 1, Gz = 1, W = t >= 20 && t < 25 });
            other.S.Add(new Smp { T = T0 + i * 100, X = 40, Y = 40, Z = 1, Gz = 1, Fr = 1 });
            double tj = t + 0.037, th = tj - delayMs / 1000.0;
            var (jx, jy) = Pos(th);
            j.S.Add(new Smp { T = T0 + i * 100 + 37, X = jx, Y = jy, Z = 1, Gz = 1, W = th >= 20 && th < 25,
                              Fb = poseFault && tj >= 10 && tj < 19 ? "C_CallbackAction" : "CAnimActionAIMovement", Fr = poseFault && tj >= 10 && tj < 19 ? 0 : 4 });
        }
        return (Jsonl(new[] { h, other }, "host"), Jsonl(new[] { j }, "joiner"));
    }

    private static AnimCompareResult Cmp((string Host, string Joiner) p, AnimCompareOptions? o = null) =>
        AnimCompare.Compare(AnimStream.Parse(Encoding.UTF8.GetBytes(p.Host)), AnimStream.Parse(Encoding.UTF8.GetBytes(p.Joiner)), o);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]   // the twin: the delay search off compares at the same clock time
    public void Anim_compare_finds_a_planted_400_ms_delay(bool estimate)
    {
        var r = Cmp(Pair(400, false), new AnimCompareOptions { EstimateDelay = estimate });
        Assert.Equal(1, r.Npcs);
        Assert.Equal(estimate, Math.Abs(r.OffsetMs - 400) <= 60);
        Assert.Equal(estimate, r.P50 < 0.05);            // aligned, the copy sits on the NPC
        Assert.True(r.WeaponMismatch < (estimate ? 0.01 : 0.05));
        Assert.Contains(AnimCompare.Heading, r.Section);
        Assert.Equal(estimate, r.Section.Contains($"run {r.OffsetMs} ms behind the host", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]   // the twin: the pose comparison off measures nothing
    public void Anim_compare_finds_a_planted_pose_mismatch(bool comparePose)
    {
        var r = Cmp(Pair(400, true), new AnimCompareOptions { ComparePose = comparePose });
        Assert.Equal(comparePose, r.PoseMismatch is double pm && pm > 0.25 && pm < 0.35);
        Assert.Equal(comparePose, r.Section.Contains("| full-body action or fragment differs | 3", StringComparison.Ordinal));
        Assert.Equal(!comparePose, r.Section.Contains("| full-body action or fragment differs | off |", StringComparison.Ordinal));
        // without the fault the same comparison finds none
        if (comparePose) Assert.True(Cmp(Pair(400, false)).PoseMismatch < 0.01);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]    // twin: not written
    [InlineData(false, true)]
    [InlineData(false, false)]   // twin: not written
    public void Anim_compare_writes_its_section_into_both_briefings(bool archive, bool write)
    {
        var dir = Temp();
        try
        {
            var (hj, jj) = Pair(400, true);
            string Make(string role, string jsonl)
            {
                var src = new[] { new SourceFile("kcd.log", Bytes: Encoding.UTF8.GetBytes("line\n")), new SourceFile(AnimStream.FileName, Bytes: Encoding.UTF8.GetBytes(jsonl)) };
                var r = Packer.Pack(src, Path.Combine(dir, role), role, new PackOptions { Level = 3 });
                if (archive) return r.Parts[0];
                var folder = Path.Combine(dir, role + "-unpacked");
                foreach (var kv in PackerTests.Open(r.Parts[0]))
                {
                    var p = Path.Combine(folder, kv.Key); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, kv.Value);
                }
                return folder;
            }
            string hp = Make("host", hj), jp = Make("joiner", jj);
            for (int pass = 0; pass < 2; pass++)   // twice: the section is replaced, never doubled
            {
                var hb = AnimBundle.Open(hp); var jb = AnimBundle.Open(jp);
                Assert.NotNull(hb.AnimBytes); Assert.NotNull(jb.AnimBytes);
                var cmp = AnimCompare.Compare(AnimStream.Parse(hb.AnimBytes!), AnimStream.Parse(jb.AnimBytes!));
                if (write) { Assert.True(hb.WriteSection(AnimCompare.Heading, cmp.Section)); Assert.True(jb.WriteSection(AnimCompare.Heading, cmp.Section)); }
            }
            foreach (var p in new[] { hp, jp })
            {
                var files = archive ? PackerTests.Open(p) : Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                                                                      .ToDictionary(f => Path.GetRelativePath(p, f).Replace('\\', '/'), File.ReadAllBytes);
                var md = Encoding.UTF8.GetString(files["BRIEFING.md"]);
                int n = md.Split(AnimCompare.Heading).Length - 1;
                Assert.Equal(write ? 1 : 0, n);
                Assert.True(md.IndexOf("\n## Animation\n", StringComparison.Ordinal) > 0);   // the bundle's own section stays
                Assert.True(files.ContainsKey("MANIFEST.txt") && files.ContainsKey(AnimStream.BundleEntry) && files.ContainsKey(AnimStream.ReportEntry));
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Replacing_a_section_keeps_the_ones_after_it()
    {
        string md = "# t\n\n## A\n\na\n\n" + AnimCompare.Heading + "\n\nold\n\n## B\n\nb\n";
        string o = AnimBundle.ReplaceSection(md, AnimCompare.Heading, AnimCompare.Heading + "\n\nnew\n\n");
        Assert.Equal("# t\n\n## A\n\na\n\n" + AnimCompare.Heading + "\n\nnew\n\n## B\n\nb\n", o);
        Assert.DoesNotContain("old", o);
    }
}
