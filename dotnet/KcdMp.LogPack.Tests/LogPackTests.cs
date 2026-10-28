// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using ZstdSharp;

namespace KcdMp.LogPack.Tests;

public class CollapserTests
{
    private static (string Text, CollapseStats St) Run(string input, OddWindows? w = null)
    {
        var (d, st) = Collapser.CollapseBytes(Encoding.Latin1.GetBytes(input), w);
        return (Encoding.Latin1.GetString(d), st);
    }

    private static string Pos(int i) => $"[KCD2-MP-DATA] v1 {i} 1.5 2.5 3.5 0 0 0 0 0";

    [Fact]
    public void A_run_of_noise_keeps_its_first_and_last_and_one_count()
    {
        string input = string.Join("\n", Enumerable.Range(0, 100).Select(Pos)) + "\nsomething that matters\n";
        var (text, st) = Run(input);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(Pos(0), lines[0]);
        Assert.StartsWith("[kct-pack] 98 more position stream", lines[1]);
        Assert.Equal(Pos(99), lines[2]);
        Assert.Equal("something that matters", lines[3]);
        Assert.Equal(101, st.LinesIn);
        Assert.Equal(98, st.Elided);
        Assert.True(st.Balanced);
    }

    [Fact]
    public void A_line_that_is_not_noise_and_not_a_repeat_is_never_dropped()
    {
        string input = "alpha\nbeta\ngamma\nalpha\n";
        var (text, st) = Run(input);
        Assert.Equal(input, text);
        Assert.Equal(0, st.Elided);
    }

    [Fact]
    public void Identical_adjacent_lines_collapse_but_the_same_line_later_does_not()
    {
        string input = string.Concat(Enumerable.Repeat("x happened\n", 10)) + "y\nx happened\n";
        var (text, st) = Run(input);
        Assert.StartsWith("x happened\n[kct-pack] 8 more identical lines between", text);
        Assert.EndsWith("x happened\ny\nx happened\n", text);
        Assert.Equal(8, st.Elided);
        Assert.True(st.Balanced);
    }

    [Fact]
    public void Marks_faults_and_stats_are_kept_in_full_between_noise()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 10; i++) sb.Append(Pos(i)).Append('\n');
        sb.Append("[KCD2-MP] MP-MARK odd world_time=5 -- the tester's marker t=12.5\n");
        for (int i = 10; i < 20; i++) sb.Append(Pos(i)).Append('\n');
        sb.Append("WO164 FAULT site=x\n");
        sb.Append("MP-WO139-STATS role=host\nMP-WO139-STATS role=host\nMP-WO139-STATS role=host\nMP-WO139-STATS role=host\n");
        var (text, st) = Run(sb.ToString());
        Assert.Contains("MP-MARK odd", text);
        Assert.Contains("FAULT site=x", text);
        Assert.Equal(4, text.Split('\n').Count(l => l.StartsWith("MP-WO139-STATS")));   // even four identical stats lines stay
        Assert.True(st.Balanced);
    }

    [Fact]
    public void Lines_inside_an_odd_window_are_all_kept_and_those_outside_are_not()
    {
        var sb = new StringBuilder();
        sb.Append("20:00:00.000 m=1 off=-+0 [pos] 1 2 3  rot=0  riding=False  read=1ms path=native\n");   // far before the mark
        for (int i = 0; i < 50; i++) sb.Append($"20:00:{i % 60:00}.000 m=1 off=-+0 [pos] 1 2 3  rot=0  riding=False  read=1ms path=native\n");
        for (int i = 0; i < 50; i++) sb.Append($"20:20:{i % 60:00}.000 m=1 off=-+0 [pos] 1 2 3  rot=0  riding=False  read=1ms path=native\n");
        var w = new OddWindows();
        w.Add(20 * 3600 + 20 * 60 + 10);   // the mark at 20:20:10
        var (text, st) = Run(sb.ToString(), w);
        Assert.Equal(50, text.Split('\n').Count(l => l.StartsWith("20:20:")));      // every line near the mark
        Assert.True(text.Split('\n').Count(l => l.StartsWith("20:00:")) < 5);       // the far run collapsed
        Assert.True(st.Balanced);
    }

    [Fact]
    public void The_odd_window_is_five_minutes_each_way_and_wraps_midnight()
    {
        var w = new OddWindows();
        w.Add(23 * 3600 + 58 * 60);
        Assert.True(w.Inside(23 * 3600 + 58 * 60 + 299));
        Assert.False(w.Inside(23 * 3600 + 58 * 60 - 301));
        Assert.True(w.Inside(3 * 60));          // 00:03 is within 5 minutes of 23:58
        Assert.False(w.Inside(10 * 60));
    }

    [Fact]
    public void Kcd_log_lines_get_their_time_from_the_header_and_t()
    {
        var c = new LineClock();
        c.Advance("Log Started at 2026-10-10  20:06:21");
        double t = c.Advance("[KCD2-MP] WO1-X something t=100.500");
        Assert.Equal(20 * 3600 + 6 * 60 + 21 + 100.5, t, 3);
        Assert.Equal(t, c.Advance("an engine line with no time"));
    }

    [Fact]
    public void A_mark_in_the_game_log_protects_the_agent_log_around_the_same_wall_time()
    {
        string kcd = "Log Started at 2026-10-10  20:00:00\n[KCD2-MP] MP-MARK odd world_time=1 -- m t=1200.0\n";
        var w = new OddWindows();
        w.Scan(kcd.Split('\n'));
        Assert.Equal(1, w.Count);
        Assert.True(w.Inside(20 * 3600 + 1200 + 200));
        Assert.False(w.Inside(20 * 3600 + 1200 + 400));
    }

    [Fact]
    public void Counts_add_up_on_a_mixed_log()
    {
        var rnd = new Random(7);
        var sb = new StringBuilder();
        for (int i = 0; i < 20000; i++)
        {
            switch (rnd.Next(6))
            {
                case 0: sb.Append(Pos(i)).Append('\n'); break;
                case 1: sb.Append("[Warning] AnimFramePoseModifier: animation fa_idle_sleep_").Append(rnd.Next(9)).Append(" not found\n"); break;
                case 2: sb.Append('\n'); break;
                case 3: sb.Append("unique line ").Append(i).Append('\n'); break;
                case 4: sb.Append("same\nsame\nsame\n"); break;
                default: sb.Append("[ping] ").Append(rnd.Next(99)).Append(" ms\n"); break;
            }
        }
        var (text, st) = Run(sb.ToString());
        Assert.True(st.Balanced);
        Assert.Equal(st.LinesKept + st.Summaries, text.Split('\n').Length - 1);
        Assert.True(text.Length < sb.Length);
    }
}

public class PackerTests
{
    private static string Temp() { var d = Path.Combine(Path.GetTempPath(), "kcdmp-logpack-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

    /// <summary>Reads a .tzst back: every zstd frame, then the tar's entries.</summary>
    internal static Dictionary<string, byte[]> Open(string path)
    {
        using var fs = File.OpenRead(path);
        using var ds = new DecompressionStream(fs);
        using var ms = new MemoryStream();
        ds.CopyTo(ms);
        var tar = ms.ToArray();
        var files = new Dictionary<string, byte[]>();
        int at = 0;
        while (at + 512 <= tar.Length)
        {
            if (tar.AsSpan(at, 512).ToArray().All(b => b == 0)) break;
            string name = Encoding.UTF8.GetString(tar, at, 100).TrimEnd('\0');
            string prefix = Encoding.UTF8.GetString(tar, at + 345, 155).TrimEnd('\0');
            if (prefix.Length > 0) name = prefix + "/" + name;
            long size = Convert.ToInt64(Encoding.ASCII.GetString(tar, at + 124, 11).Trim('\0', ' '), 8);
            // the header checksum must verify
            long want = Convert.ToInt64(Encoding.ASCII.GetString(tar, at + 148, 6), 8), sum = 0;
            for (int i = 0; i < 512; i++) sum += (i >= 148 && i < 156) ? 0x20 : tar[at + i];
            Assert.Equal(want, sum);
            files[name] = tar.AsSpan(at + 512, (int)size).ToArray();
            at += 512 + (int)((size + 511) / 512 * 512);
        }
        return files;
    }

    [Fact]
    public void A_small_bundle_is_one_valid_tzst_with_the_manifest_first()
    {
        var dir = Temp();
        try
        {
            var src = new List<SourceFile>
            {
                new("kcd.log", Bytes: Encoding.Latin1.GetBytes("one\ntwo\n[KCD2-MP-DATA] v1 1 1 1 1 1 1 1 1 1\n")),
                new("settings.json", Bytes: Encoding.UTF8.GetBytes("{\"a\":1}")),
                new("history/launch-1/kcd.log", Bytes: Encoding.Latin1.GetBytes("one\ntwo\n[KCD2-MP-DATA] v1 1 1 1 1 1 1 1 1 1\n")),   // the same bytes as kcd.log
            };
            var r = Packer.Pack(src, dir, "KCDMP-logs-test");
            Assert.Single(r.Parts);
            Assert.EndsWith("KCDMP-logs-test.tzst", r.Parts[0]);
            var f = Open(r.Parts[0]);
            Assert.True(f.ContainsKey("MANIFEST.txt"));
            Assert.True(f.ContainsKey("kcd.log"));
            Assert.True(f.ContainsKey("settings.json"));
            Assert.False(f.ContainsKey("history/launch-1/kcd.log"));   // the duplicate is not stored again
            Assert.Contains("same content as kcd.log", Encoding.UTF8.GetString(f["MANIFEST.txt"]));
            Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(f["settings.json"]));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_big_bundle_splits_into_numbered_parts_that_each_open_and_fit()
    {
        var dir = Temp();
        try
        {
            var rnd = new Random(3);
            var src = new List<SourceFile>();
            for (int i = 0; i < 6; i++)
            {
                var b = new byte[400_000]; rnd.NextBytes(b);   // incompressible
                src.Add(new($"leash/f{i}.csv", Bytes: b));
            }
            var opt = new PackOptions { MaxPartBytes = 1_200_000 + 256 * 1024 + 64 * 1024, Level = 3 };
            var r = Packer.Pack(src, dir, "KCDMP-logs-test", opt);
            Assert.True(r.Parts.Count >= 2);
            for (int i = 0; i < r.Parts.Count; i++)
            {
                Assert.EndsWith($"-part{i + 1}of{r.Parts.Count}.tzst", r.Parts[i]);
                Assert.True(new FileInfo(r.Parts[i]).Length <= opt.MaxPartBytes);
            }
            var all = new Dictionary<string, byte[]>();
            for (int i = 0; i < r.Parts.Count; i++)
            {
                var f = Open(r.Parts[i]);
                Assert.Equal(i == 0, f.ContainsKey("MANIFEST.txt"));
                foreach (var kv in f) all[kv.Key] = kv.Value;
            }
            for (int i = 0; i < 6; i++) Assert.Equal(src[i].Bytes, all[$"leash/f{i}.csv"]);
            var man = Encoding.UTF8.GetString(all["MANIFEST.txt"]);
            Assert.Contains("Parts: " + r.Parts.Count, man);
            Assert.Contains("sha256", man);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void One_file_too_big_for_a_part_is_cut_at_line_ends_and_loses_nothing()
    {
        var dir = Temp();
        try
        {
            var rnd = new Random(5);
            var sb = new StringBuilder();
            for (int i = 0; i < 30000; i++) sb.Append("line ").Append(i).Append(' ').Append(Convert.ToHexString(BitConverter.GetBytes(rnd.NextInt64()))).Append('\n');
            byte[] raw = Encoding.Latin1.GetBytes(sb.ToString());
            var opt = new PackOptions { MaxPartBytes = 256 * 1024 + 128 * 1024 + 64 * 1024 + 150_000, Level = 3, Collapse = false };
            var r = Packer.Pack(new[] { new SourceFile("big.csv", Bytes: raw) }, dir, "b", opt);
            Assert.True(r.Parts.Count >= 2, "split into parts");
            var got = new List<byte>();
            var pieces = new SortedDictionary<int, byte[]>();
            foreach (var p in r.Parts)
                foreach (var kv in Open(p))
                    if (kv.Key.StartsWith("big.csv.piece"))
                        pieces[int.Parse(kv.Key.Split("piece")[1].Split("of")[0])] = kv.Value;
            foreach (var kv in pieces) got.AddRange(kv.Value);
            Assert.Equal(raw, got.ToArray());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Collapsing_off_packs_the_bytes_as_they_are()
    {
        var dir = Temp();
        try
        {
            byte[] raw = Encoding.Latin1.GetBytes(string.Join("\n", Enumerable.Range(0, 50).Select(i => $"[KCD2-MP-DATA] v1 {i} 1 1 1 1 1 1 1 1")) + "\n");
            var r = Packer.Pack(new[] { new SourceFile("kcd.log", Bytes: raw) }, dir, "c", new PackOptions { Collapse = false });
            Assert.Equal(raw, Open(r.Parts[0])["kcd.log"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_missing_file_is_skipped_not_fatal()
    {
        var dir = Temp();
        try
        {
            var r = Packer.Pack(new SourceFile[] { new("nope.log", Path: Path.Combine(dir, "does-not-exist.log")), new("a.txt", Bytes: new byte[] { 65 }) }, dir, "d");
            var f = Open(r.Parts[0]);
            Assert.True(f.ContainsKey("a.txt"));
            Assert.False(f.ContainsKey("nope.log"));
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>WO-204 Act V: the briefing is this launch's session, carries both players' notes, and scores the new measures.</summary>
public class BriefingWo204Tests
{
    private static Briefing.Input Inp(params (string Key, string[] Lines, DateTime? Mtime)[] files)
    {
        var inp = new Briefing.Input();
        foreach (var (k, l, mt) in files) { inp.Logs[k] = l; if (mt is DateTime t) inp.Mtimes[k] = t; }
        return inp;
    }

    [Fact]
    public void Only_this_launchs_session_counts()
    {
        var now = new DateTime(2026, 10, 22, 15, 45, 30);
        var old = Inp(("agent.log", new[] { "15:07:33.812 m=6 off=? start", "15:12:41.545 m=1 off=-+0 MP-MARK ping from player 1: mark=ytf887y2wp -- this machine's block follows" }, now),
                      ("agent.prev2.log", new[] { "20:13:17.166 m=7 off=? start", "20:25:01.113 m=1 off=-+0 MP-MARK odd note=walking away after dying -- the tester's marker (typed on this machine)" }, new DateTime(2026, 10, 20, 20, 27, 0)));
        Assert.Equal(new[] { "agent.log" }, Briefing.SessionAgentKeys(old));
        var md = Briefing.Build(old);
        Assert.DoesNotContain("walking away after dying", md);   // session D's host list was headed by a mark from two days before
        Assert.Contains("15:12:41.545 the partner (player 1)", md);
        // an agent restarted inside the session: its earlier log ended 10 minutes before this one began -- it counts
        var restarted = Inp(("agent.log", new[] { "15:07:33.812 m=6 off=? start" }, now),
                            ("agent.prev.log", new[] { "14:30:00.000 m=6 off=? start" }, new DateTime(2026, 10, 22, 14, 57, 0)));
        Assert.Equal(new[] { "agent.prev.log", "agent.log" }, Briefing.SessionAgentKeys(restarted));
        // without the files' times: this launch's log alone
        var noTimes = Inp(("agent.log", new[] { "15:07:33.812 m=6 off=? start" }, null), ("agent.prev.log", new[] { "14:30:00.000 x" }, null));
        Assert.Equal(new[] { "agent.log" }, Briefing.SessionAgentKeys(noTimes));
        // a log that ran past midnight began the day before its last write
        Assert.Equal(new DateTime(2026, 10, 21, 23, 50, 0), Briefing.LogStart(Inp(("agent.log", new[] { "23:50:00.000 x" }, new DateTime(2026, 10, 22, 0, 20, 0))), "agent.log", new[] { "23:50:00.000 x" }));
    }

    [Fact]
    public void The_partners_note_is_on_his_mark()
    {
        var inp = Inp(("agent.log", new[]
        {
            "15:13:02.832 m=1 off=-+0 MP-MARK odd note=no weapon on bandit -- the tester's marker (typed on this machine)",
            "15:21:16.271 m=2 off=-+0 MP-MARK ping from player 1: mark=sxxoj9ncqg -- this machine's block follows",
            "15:21:16.290 m=3 off=-+0 MP-MARK-SNAP mark=sxxoj9ncqg agent avatar peer=1",
            "15:21:16.301 m=4 off=-+0 MP-MARK note from player 1: mark=sxxoj9ncqg note=character moving while sitting",
        }, null));
        var md = Briefing.Build(inp);
        Assert.Contains("this player -- \"no weapon on bandit\"", md);
        Assert.Contains("the partner (player 1) -- \"character moving while sitting\"", md);
    }

    [Fact]
    public void The_scorecard_has_the_new_measures()
    {
        var inp = Inp(("agent.log", new[]
        {
            "14:09:41.667 m=1 off=-2215 [ping] 85 ms (network) -- the game's queue is 601 ms behind, the game is in the background",
            "14:09:42.667 m=2 off=-2215 [ping] 80 ms (network) -- the game's queue is 120 ms behind",
            "14:10:00.000 m=3 off=0 WO203B-TALK this player's conversation 233 (normal) with ttkc_barbora -- announced to 1 partner(s)",
            "14:11:00.000 m=4 off=0 MP-W204-STATS conv_no_barks=on barks_kept_out=415 talks_announced=1",
        }, null), ("kcd.log", new[]
        {
            "[Warning] Validator: --- Mannequin Error, attempting to queue too many actions on entity 'ttkc_man_7' (queue size = 11, but only up to 10 allowed) ---",
            "[Warning] Validator: --- Mannequin Error, attempting to queue too many actions on entity 'ttkc_man_7' (queue size = 11, but only up to 10 allowed) ---",
            "[KCD2MP] WO204-DRAW ttkc_man_7 drawn delay_ms=140",
        }, null));
        var s = Briefing.Measure(inp).ToDictionary(x => x.Measure, x => x.Value);
        Assert.Equal("p99 85 ms", s["message latency (the network ping)"]);
        Assert.Equal("p99 120 ms (1)", s["the game's queue behind (game in front)"]);
        Assert.Equal("p99 601 ms (1)", s["the game's queue behind (game in the background)"]);
        Assert.Equal("2: ttkc_man_7 2", s["action-queue overflows on bodies (kcd.log)"]);
        Assert.Equal("0", s["T-poses on copies (WO204-TPOSE)"]);
        Assert.Equal("p90 140 ms (1)", s["weapon-draw delay on copies"]);
        Assert.Equal("1 (415)", s["conversations announced (barks kept out)"]);
    }
}

/// <summary>WO-210 H.4: the session's own role, this launch's logs by the launch id, the instruments' section.</summary>
public class BriefingWo210Tests
{
    private static Briefing.Input Inp(params (string Key, string[] Lines, DateTime? Mtime)[] files)
    {
        var inp = new Briefing.Input();
        foreach (var (k, l, mt) in files) { inp.Logs[k] = l; if (mt is DateTime t) inp.Mtimes[k] = t; }
        return inp;
    }

    [Fact]
    public void The_joiners_bundle_says_joiner_even_when_the_authority_passed_to_it()
    {
        var agent = new[] { "20:08:02.026 m=1 off=? [role] this client no longer holds NPC->player damage authority",
                            "20:08:02.459 m=1 off=? MP-JOIN joiner: the host (ghost 0) runs a SHARED WORLD -- this machine follows",
                            "20:14:26.647 m=1 off=+1847 [role] this client now holds NPC->player damage authority" };
        Assert.Equal("joiner", Briefing.SessionRole(agent));
        Assert.Equal("host", Briefing.SessionRole(new[] { "21:05:28.412 m=1 off=? [role] this client now holds NPC->player damage authority" }));
        Assert.Equal("?", Briefing.SessionRole(Array.Empty<string>()));
    }

    [Fact]
    public void Only_logs_naming_this_launch_count()
    {
        var now = new DateTime(2026, 10, 27, 21, 27, 44);
        var same = Inp(("agent.log", new[] { "21:01:49.130 m=5 off=? x", "21:01:49.143 m=12 off=? MP-LAUNCH id=20261027-210140 (the game's own log start)" }, now),
                       ("agent.prev.log", new[] { "20:21:44.317 m=5 off=? x", "20:21:44.330 m=6 off=? MP-LAUNCH id=20261027-202130 (the game's own log start)" }, new DateTime(2026, 10, 27, 20, 58, 4)),
                       ("agent.prev2.log", new[] { "21:00:44.317 m=5 off=? x", "21:00:44.330 m=6 off=? MP-LAUNCH id=20261027-210140 (the game's own log start)" }, new DateTime(2026, 10, 27, 21, 1, 0)));
        // the launch before ended 4 minutes before this one began (inside WO-204's 30-minute window) -- it is not this launch: out
        Assert.Equal(new[] { "agent.prev2.log", "agent.log" }, Briefing.SessionAgentKeys(same));
        Assert.Equal("20261027-210140", Briefing.LaunchId(same.Logs["agent.log"]));
        Assert.Null(Briefing.LaunchId(new[] { "x" }));
    }

    [Fact]
    public void The_instruments_section_counts_every_line()
    {
        var native = new[] { "[22:00:00.000] t1 WO210-STATE reseat npc=a was=lying(its own) want=standing path=15-host-base ms=300 -> ok [n=1]",
                             "[22:00:01.000] t1 WO210-STATE reseat npc=b was=empty want=standing path=unknown ms=300 -> ok [n=2]",
                             "[22:00:02.000] t1 WO210-CLINCH loop npc=c: 5 asks within 1000 ms (asks=5 refused=5 started_by=the-copy) [n=1]" };
        var md = W210Instruments.Section(null, native, new[] { "22:00:03.000 m=1 off=? WO210-STATE handover npc=a path=15-host-base" });
        Assert.Contains("the owner's reseats:** 2 (path unknown: 1)", md);
        Assert.Contains("loops 1", md);
        Assert.Contains("released the game's way: 1", md);
        Assert.Contains("empty bodies** (a copy's whole body with no action over 0.3 s, within 60 m): no probe", md);
    }

    [Fact]
    public void Stale_postures_come_from_the_probes_own_families()
    {
        string L(long t, string fam, string hf, double x) => "{\"t\":" + t + ",\"m\":\"joiner\",\"a\":[{\"n\":\"Dude\",\"k\":\"player\",\"p\":[0,0,0]},{\"n\":\"ttkc_man_5\",\"k\":\"copy\",\"p\":["
            + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",0,0],\"fam\":\"" + fam + "\",\"hf\":\"" + hf + "\",\"sc\":[{\"s\":\"FullBody\",\"c\":\"C_AnimActionIdleState\",\"fr\":45,\"fn\":\"Lying\",\"pr\":3}]}]}";
        var lines = new List<string>();
        for (int i = 0; i < 20; i++) lines.Add(L(1000 + i * 100, "lying", "moving", i * 0.5));   // 1.9 s lying while its NPC walks, 9.5 m slid
        for (int i = 0; i < 10; i++) lines.Add(L(3100 + i * 100, "lying", "lying", 9.5));        // its NPC lies too: fine
        var s = AnimStream.Parse(new StringReader(string.Join("\n", lines)));
        var st = W210Instruments.StalePostures(s);
        Assert.Single(st);
        Assert.Equal(1, st[0].N);
        Assert.InRange(st[0].Moved, 9.0, 10.0);
        Assert.Empty(W210Instruments.EmptyBodies(s));
    }

    [Fact]
    public void Every_mark_shows_both_clocks_and_the_sun_here()
    {
        var agent = new[] { "21:10:00.000 m=1 off=+1 WO210-TIME mark=k3x9q2mzt1 this=06:12 host=06:10 gap_game_s=+118 role=joiner",
                            "21:12:00.000 m=1 off=+1 WO210-TIME mark=local this=07:00 host=? gap_game_s=? role=joiner" };
        var kcd = new[] { "[KCD2-MP] WO210-SUN mark=k3x9q2mzt1 sun=06:40" };
        var c = W210Instruments.MarkClocks(agent, kcd);
        Assert.Equal(2, c.Count);
        Assert.Equal(("k3x9q2mzt1", "06:12", "06:10", "+118", "06:40"), c[0]);
        Assert.Equal("?", c[1].Sun);
        var md = W210Instruments.Section(null, Array.Empty<string>(), agent, kcd);
        Assert.Contains("| k3x9q2mzt1 | 06:12 | 06:10 | +118 | 06:40 |", md);
    }
}
