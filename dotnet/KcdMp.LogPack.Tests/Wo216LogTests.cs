// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.LogPack;

namespace KcdMp.LogPack.Tests;

/// <summary>WO-216 I: the session probe's file belongs to this launch or is left out (and said).</summary>
public class Wo216LogTests
{
    [Fact]
    public void The_launch_time_is_read_from_the_games_own_log_head()
    {
        var t = SessionRule.LaunchTime("Connecting messages from trace server...\nBackupNameAttachment=\" Build(1) 09 Nov 26 (09 33 05)\"\n\nLog Started at 2026-11-09  09:33:05\nBuild info:");
        Assert.Equal(new DateTime(2026, 11, 9, 9, 33, 5), t);
        Assert.Null(SessionRule.LaunchTime("no header here"));
        Assert.Null(SessionRule.LaunchTime(null!));
    }

    [Fact]
    public void A_file_last_written_before_the_launch_is_an_earlier_sessions()
    {
        var launch = new DateTime(2026, 11, 9, 9, 33, 5);
        Assert.True(SessionRule.Stale(launch, launch.AddHours(-3)));
        Assert.False(SessionRule.Stale(launch, launch.AddMinutes(20)));
        Assert.False(SessionRule.Stale(launch, launch.AddSeconds(-30)));   // the coarse stamp's slack
        Assert.False(SessionRule.Stale(null, launch.AddHours(-3)));        // no launch time: kept (the briefing reads its own times)
        Assert.False(SessionRule.Stale(launch, null));
    }

    [Fact]
    public void A_stale_session_file_is_left_out_and_the_bundle_says_so()
    {
        string dir = Path.Combine(Path.GetTempPath(), "w216log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string kcd = Path.Combine(dir, "kcd.log");
            File.WriteAllText(kcd, "Log Started at 2026-11-09  09:33:05\nBuild info:\n");
            string ses = Path.Combine(dir, AnimStream.SessionFileName);
            File.WriteAllText(ses, "{}\n");
            File.SetLastWriteTime(ses, new DateTime(2026, 11, 8, 22, 0, 0));
            var outp = Packer.ArrangeAnimSession([new SourceFile("kcd.log", kcd)]);
            Assert.DoesNotContain(outp, s => s.EntryName == AnimStream.SessionEntry);
            var note = Assert.Single(outp, s => s.EntryName == SessionRule.NoteEntry);
            Assert.Contains(SessionRule.NoSessionFile, Encoding.UTF8.GetString(note.Bytes!));
            // written during this launch: kept, no note
            File.SetLastWriteTime(ses, new DateTime(2026, 11, 9, 10, 0, 0));
            var keep = Packer.ArrangeAnimSession([new SourceFile("kcd.log", kcd)]);
            Assert.Contains(keep, s => s.EntryName == AnimStream.SessionEntry);
            Assert.DoesNotContain(keep, s => s.EntryName == SessionRule.NoteEntry);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void The_rest_of_the_body_reads_in_plain_words_from_the_joiners_status()
    {
        var native = new List<string>
        {
            "[10:00:00.000] t1 WO215-CENSUS npc=a dormant (full body empty, physics none, brain not running state=1): not streamed until it wakes dist_host_m=120 [n=1]",
            "[10:00:01.000] t1 WO215-CENSUS npc=b dormant (full body empty, physics none, brain not running state=1): not streamed until it wakes dist_host_m=300 [n=2]",
            "[10:01:00.000] t1 WO210-KD fall npc=x (its host NPC is down: the engine's own ragdoll) [n=1]",
            "[10:02:00.000] t1 WO216-STATUS w216 bits=0x7F twin_off=0x0 configured=1 | A.0 read=armed (armed) set=0 ticks=0 | A.1 lines_in=500 replay_orders=40 exact=10 " +
            "replay_frames=900 replay_body_s=300 fallback_frames=10 (no_line=1 no_goal=0 rest=0) host_end_cancels=0 arrived=2 ended_near=0 ended_seen=0 stall_holds=0 " +
            "route_corrections=5 (drift=4 refused=0 no_path=1 stall=0) per_body_min=1.00 | A.2 routes=30 parity_mean_m=0.40 p50_bin=3 p90_bin=6 (10 cm) | A.3 replay_gait windows=9 within15=8 " +
            "| stalls moving_frames=1 | E horse_replay_frames=77 | B no_brains=on role=2 active=1 (armed; spawn watch armed) samples=600 nonzero=0 max_running=0 sweep_paused=400 " +
            "sweep_hidden=380 local_spawns=3 last=- | D outfits=on reads=0 | F.4/G owner_rows played=12 refused=1 held_frames=40",
        };
        var agent = new List<string> { "10:02:00.000 m=1 MP-W216-STATS route_replay=on dressed=20 dress_checked=20 dress_matched=19 line_activity_applied=7 line_hands_applied=3 rows_retired=9" };
        string s = W216Section.Lines(native, agent);
        Assert.Contains("own movement request 40 time(s)", s);
        Assert.Contains("1 a body-minute", s);
        Assert.Contains("brains running here: none", s);
        Assert.Contains("19 of 20 checked matched", s);
        Assert.Contains("77 frames", s);
        Assert.Contains("played through the owner 12", s);
        Assert.Contains("falls 1", s);
        Assert.Contains("2, from the host 300 m p50", s);
        Assert.Contains("120 m nearest", s);
    }

    [Fact]
    public void The_briefing_line_says_there_is_no_session_file_for_this_launch()
    {
        string s = W215Section.Lines(null, [], [], [], noSessionNote: true);
        Assert.Contains(SessionRule.NoSessionFile, s);
        string t = W215Section.Lines(null, [], [], []);
        Assert.DoesNotContain(SessionRule.NoSessionFile, t);
    }
}
