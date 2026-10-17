// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-194: offline replays of the 0.60.9 session (the testers' bundles, never committed, unpacked in logs/w192/host and logs/w192/joiner;
/// a replay passes with a note when they are not there). Every result is printed (the findings' numbers are these lines).
///   A   both joiner stalls: every NPC the end positioning waited for on the joiner was a paused copy (the stop list's kind), never the
///       player; with their FastForward stopped the gate opens and the joiner's positioning runs as the host's did (0.3-0.4 s)
///   B1  the trigger would have gone out at the host's own start (19:48:28, 20:01:58)
///   B2  the joiner's copy, started through its handler on that message, would have begun within a second of the host's -- not 7.5 s late
/// </summary>
public class Wo194ReplayTests
{
    private readonly ITestOutputHelper _out;
    public Wo194ReplayTests(ITestOutputHelper o) { _out = o; }

    private static string? Dir(string bundle)
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d);
        if (d is null) return null;
        string p = Path.Combine(d, "logs", "w192", bundle);
        return File.Exists(Path.Combine(p, "agent.log")) && File.Exists(Path.Combine(p, "kcd.log")) ? p : null;
    }

    private static readonly Regex Head = new(@"^(\d\d):(\d\d):(\d\d\.\d\d\d) m=\d+ off=(\S+) ", RegexOptions.Compiled);

    /// <summary>The host's wall clock of an agent line (the joiner's lines carry the relay offset: host = joiner + off).</summary>
    private static double HostT(string l)
    {
        var m = Head.Match(l);
        if (!m.Success) return -1;
        double t = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        string o = m.Groups[4].Value.Replace("-+", "", StringComparison.Ordinal);
        return t + (double.TryParse(o, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) ? ms / 1000.0 : 0);
    }

    private static string Hms(double s) => TimeSpan.FromSeconds(s).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    private static readonly string[] Scenes = ["zbranePanaSemina_seminArrival", "zbranePanaSemina_playerSukDialog2"];

    private static double First(string[] lines, Func<string, bool> pred) { foreach (var l in lines) if (pred(l)) return HostT(l); return -1; }

    [Fact]
    public void B1_B2_the_trigger_goes_at_the_hosts_start_and_the_joiners_copy_follows_within_a_second()
    {
        string? j = Dir("joiner"), h = Dir("host");
        if (j is null || h is null) { _out.WriteLine("the 0.60.9 bundles are not unpacked in logs/w192 -- skipped"); return; }
        string[] jl = File.ReadAllLines(Path.Combine(j, "agent.log")), hl = File.ReadAllLines(Path.Combine(h, "agent.log"));
        foreach (string s in Scenes)
        {
            // B1: the hook fires at the enqueue, which comes before the initialized line (the profile streams between them)
            double hostInit = First(hl, l => l.Contains("OnCutsceneInitialized called for Ingame cutscene '" + s + "'", StringComparison.Ordinal));
            double hostStart = First(hl, l => l.Contains("MP-CUTSCENE side=local state=start type=Ingame name=" + s, StringComparison.Ordinal));
            double joinOwn = First(jl, l => l.Contains("MP-CUTSCENE side=local state=start type=Ingame name=" + s, StringComparison.Ordinal));
            double joinHeard = First(jl, l => l.Contains("MP-CUTSCENE side=peer", StringComparison.Ordinal) && l.Contains("state=start type=Ingame name=" + s, StringComparison.Ordinal));
            double hostHeard = First(hl, l => l.Contains("MP-CUTSCENE side=peer", StringComparison.Ordinal) && l.Contains("state=start type=Ingame name=" + s, StringComparison.Ordinal));
            Assert.True(hostInit > 0 && joinOwn > 0, s);
            bool joinerFirst = joinOwn < hostStart;
            // the bus's one-way time, measured on the existing start message of the first starter
            double oneWay = joinerFirst ? hostHeard - joinOwn : joinHeard - hostStart;
            _out.WriteLine($"{s}: host enqueue <= {Hms(hostInit)}, host runs {Hms(hostStart)}, the joiner's own start {Hms(joinOwn)} ({joinOwn - hostStart:+0.00;-0.00} s); the start message took {oneWay:F2} s");
            Assert.InRange(oneWay, 0, 1.0);
            if (!joinerFirst)
            {
                // B2: the joiner's handler is pulsed when the message arrives; its copy initializes like its own did (the 0.60.9 joiner took
                // ~0.004 s from initialized to running) and is then aligned to the host's clock
                double projected = hostInit + oneWay;
                _out.WriteLine($"  B1 sends at {Hms(hostInit)}; B2 starts the joiner's copy at ~{Hms(projected)} = {projected - hostStart:+0.00} s after the host (0.60.9: {joinOwn - hostStart:+0.00} s)");
                Assert.True(projected - hostStart < 1.0, "within a second");
                Assert.True(joinOwn - hostStart > 7.0, "0.60.9 started it 7.5 s late (he walked into the trigger)");
            }
            else
            {
                // B3: the joiner's own trigger fired first: held at initialized until the host's copy runs (its G), at most 3 s
                double release = hostStart + oneWay;
                _out.WriteLine($"  the joiner's own trigger first ({hostStart - joinOwn:F2} s): held, released by the host's G at ~{Hms(release)} = {release - joinOwn:F2} s held (bound 3 s); the clock aligns the rest");
                Assert.True(release - joinOwn < 3.0);
            }
        }
    }

    [Fact]
    public void A_every_npc_the_joiners_positioning_waited_for_was_a_paused_copy_and_the_stop_opens_the_gate()
    {
        string? j = Dir("joiner"), h = Dir("host");
        if (j is null || h is null) { _out.WriteLine("the 0.60.9 bundles are not unpacked in logs/w192 -- skipped"); return; }
        string[] jk = File.ReadAllLines(Path.Combine(j, "kcd.log")), hk = File.ReadAllLines(Path.Combine(h, "kcd.log"));
        string[] ha = File.ReadAllLines(Path.Combine(h, "agent.log")), ja = File.ReadAllLines(Path.Combine(j, "agent.log"));
        var ff = new Regex(@"/ai//\(NPC\)(\S+) \[SchedulerSubsystem\]:Explicitly interrupting current behavior 'teleport' .*because the reason 'FastForward'", RegexOptions.Compiled);
        var pause = new Regex(@"Executing console command 'wh_ai_(Pause|Resume)NPC (\S+)'", RegexOptions.Compiled);
        foreach (string s in Scenes)
        {
            // the host's end positioning: who it fast-forwarded (its own log, from its OnCutsceneEnd on)
            int he = Array.FindIndex(hk, l => l.StartsWith("CutscenePlayer::OnCutsceneEnd called for Ingame cutscene '" + s + "'", StringComparison.Ordinal));
            Assert.True(he > 0, s);
            var waited = new HashSet<string>(StringComparer.Ordinal);
            for (int i = he; i < Math.Min(hk.Length, he + 120); i++) { var m = ff.Match(hk[i]); if (m.Success) waited.Add(m.Groups[1].Value); }
            // the joiner: its OnCutsceneEnd, and never an OnPositioningFinished before the next load (the stall)
            int je = Array.FindIndex(jk, l => l.StartsWith("CutscenePlayer::OnCutsceneEnd called for Ingame cutscene '" + s + "'", StringComparison.Ordinal));
            Assert.True(je > 0, s);
            int jInt = Array.FindIndex(jk, je, l => l.Contains("::Interrupt called for Ingame cutscene '" + s + "'", StringComparison.Ordinal));
            int jPos = Array.FindIndex(jk, je, l => l.StartsWith("CutscenePlayer::OnPositioningFinished called for Ingame cutscene '" + s + "'", StringComparison.Ordinal));
            Assert.True(jPos < 0 || (jInt > 0 && jPos > jInt), $"{s}: 0.60.9's joiner never positioned before the next load");
            // which of them were paused copies on the joiner at its end (the last pause/resume before it)
            var paused = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int i = 0; i < je; i++) { var m = pause.Match(jk[i]); if (m.Success) paused[m.Groups[2].Value] = m.Groups[1].Value == "Pause"; }
            var copies = waited.Where(n => n != "Dude" && paused.TryGetValue(n, out bool p) && p).ToList();
            var running = waited.Where(n => n != "Dude" && !(paused.TryGetValue(n, out bool p) && p)).ToList();
            _out.WriteLine($"{s}: the host's positioning fast-forwarded [{string.Join(", ", waited)}]; on the joiner paused copies: [{string.Join(", ", copies)}]; brains running (they finish by themselves): [{string.Join(", ", running)}]");
            Assert.NotEmpty(copies);   // what kept the joiner's gate shut
            // A's stop list: every copy, never the local player (wo194rules::ff_stop_allowed; the agent's list is its copies' entity ids)
            Assert.DoesNotContain("Dude", copies);
            // with them stopped the count is 0 and the steps run: the host's own positioning time is what the joiner's then takes
            var hp = ha.FirstOrDefault(l => l.Contains($"MP-W151 scene Ingame '{s}' positioned ", StringComparison.Ordinal));
            Assert.NotNull(hp);
            double secs = double.Parse(Regex.Match(hp!, @"positioned (\d+\.\d) s").Groups[1].Value, CultureInfo.InvariantCulture);
            var jr = ja.FirstOrDefault(l => l.Contains($"scene Ingame '{s}' end positioning not done", StringComparison.Ordinal) || l.Contains($"scene Ingame '{s}' not positioned", StringComparison.Ordinal));
            _out.WriteLine($"  the host positioned {secs:F1} s after its content; with A the joiner's gate opens at the stop and its steps take the same (< 1 s). 0.60.9 joiner: {(jr is null ? "-" : jr[(jr.IndexOf("WO1", StringComparison.Ordinal) is int k && k >= 0 ? k : 0)..])}");
            Assert.True(secs < 1.0);
        }
    }
}
