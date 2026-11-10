// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.LogPack;

namespace KcdMp.LogPack.Tests;

/// <summary>WO-218 F.4: the briefing opens with the queue, in plain words; the pipe's lines read the agent's and the DLL's numbers.</summary>
public class Wo218LogTests
{
    static IEnumerable<string> Lags(params int[] ms) => ms.Select((m, i) => $"12:00:{i % 60:00}.000 m=1 off=+0 MP-W207-Q lag_ms={m} waiting=0 by= lua_ms=0 pipe_ms=0");

    [Fact]
    public void The_queue_is_read_as_p50_p99_and_the_worst_with_a_plain_verdict()
    {
        var q = W218Section.QueueOf(Lags(Enumerable.Repeat(0, 98).Concat(new[] { 200, 900 }).ToArray()));
        Assert.Equal(100, q.N);
        Assert.Equal(0, q.P50);
        Assert.Equal(900, q.Worst);
        Assert.StartsWith("on time", W218Section.Verdict(q.N, 200, 900));
        Assert.StartsWith("mostly on time", W218Section.Verdict(100, 800, 3000));
        Assert.StartsWith("BEHIND", W218Section.Verdict(100, 60000, 100000));
        Assert.StartsWith("no queue numbers", W218Section.Verdict(0, 0, 0));
    }

    [Fact]
    public void The_section_opens_with_the_verdict_and_names_the_role()
    {
        var lines = Lags(0, 0, 70000).Prepend("12:00:00.000 m=1 off=? MP-WO122 cfg shared_world=on role=joiner connected=on").ToList();
        string s = W218Section.Section(lines);
        Assert.StartsWith(W218Section.Heading, s);
        Assert.Contains("BEHIND", s);
        Assert.Contains("the joiner's queue", s);
    }

    [Fact]
    public void The_pipe_lines_sum_the_batches_and_say_an_older_build_plainly()
    {
        Assert.Contains("older build", W218Section.Lines([], ["no pipe here"]));
        var agent = new[]
        {
            "12:00:00.000 m=1 off=+0 MP-PIPE on=1 dll=1 batches=10 subs=40 subs_per_batch=4.0 subs_max=9 rtt_ms_mean=30 rtt_ms_max=80 wait_ms_mean=5 wait_ms_max=20 posted=12 merged=3 superseded=0 failed=0 bytes_max=900 waiting=0 | shed level=0 level_max=0 on=0 superseded_far=0 far_rate=0 mid=0 asks=0 | act batches=1",
            "12:00:10.000 m=1 off=+0 MP-PIPE on=1 dll=1 batches=5 subs=10 subs_per_batch=2.0 subs_max=3 rtt_ms_mean=30 rtt_ms_max=40 wait_ms_mean=5 wait_ms_max=20 posted=2 merged=1 superseded=0 failed=1 bytes_max=900 waiting=0 | shed level=1 level_max=1 on=1 superseded_far=4 far_rate=0 mid=0 asks=1 | act batches=1",
        };
        var native = new[] { "[12:00:05.000] t1 WO218-PIPE window_s=10.0 batches_in=15 batches_done=15 subs=50 subs_frames=12 subs_per_frame_mean=4.17 subs_per_frame_max=9 singles=30 singles_frames=20 singles_per_frame_mean=1.50 singles_per_frame_max=3 carry_frames=0 drains=12 drain_us_mean=300 drain_us_max=900 faulted=0 cut=0 not_batchable=0 malformed=0 reply_max=800" };
        string s = W218Section.Lines(native, agent);
        Assert.Contains("15 batches carried 50 requests", s);
        Assert.Contains("1 requests failed", s);
        Assert.Contains("shed 4 states of far bodies", s);
        Assert.Contains("requests a frame on average", s);
    }
}
