// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.LogPack;

namespace KcdMp.LogPack.Tests;

/// <summary>WO-215 G: the copies' bodies in the briefing, the session probe in the bundle, activities missing.</summary>
public class Wo215LogTests
{
    private static string Line(long t, string name, string kind, string fam, string hf, bool empty = false) =>
        "{\"t\":" + t + ",\"m\":\"joiner\",\"f\":1,\"s\":1,\"a\":[{\"n\":\"" + name + "\",\"k\":\"" + kind + "\",\"p\":[1,2,3],\"r\":[0,0,0],\"dead\":0,\"rag\":0,\"q\":0,\"qa\":0,\"qd\":[],"
        + "\"tg\":\"\",\"sa\":0,\"cm\":0,\"cs\":0,\"op\":\"\",\"fam\":\"" + fam + "\",\"hf\":\"" + hf + "\",\"sc\":["
        + (empty ? "" : "{\"s\":\"FullBody\",\"c\":\"CAnimActionAIMovement\",\"fr\":5,\"fn\":\"MotionIdle\",\"pr\":2}") + "]}]}";

    [Fact]
    public void A_copy_standing_while_its_host_npc_works_is_an_activity_missing()
    {
        var s = new AnimStream();
        for (int i = 0; i < 30; i++) s.AddLine(Line(1000 + i * 100L, "smith", "copy", "standing", "working"));
        for (int i = 0; i < 30; i++) s.AddLine(Line(1000 + i * 100L, "guard", "copy", "standing", "standing"));
        s.Finish();
        var miss = W210Instruments.ActivityMissing(s);
        Assert.Single(miss);
        Assert.Equal("smith", miss[0].Name);
        Assert.True(miss[0].Secs >= 2.5);
    }

    [Fact]
    public void Under_a_second_is_no_finding()
    {
        var s = new AnimStream();
        for (int i = 0; i < 8; i++) s.AddLine(Line(1000 + i * 100L, "smith", "copy", "standing", "working"));
        s.AddLine(Line(1900, "smith", "copy", "working", "working"));
        s.Finish();
        Assert.Empty(W210Instruments.ActivityMissing(s));
    }

    [Fact]
    public void The_section_says_each_contract_in_plain_words()
    {
        var native = new List<string>
        {
            "[17:09:13.361] t1 WO215-STATUS w215 bits=0x1FF twin_off=0x0 configured=1 | A.0 request=armed (armed) queued=10 refused=0 cancels=1 faults=0 | A.1 routes=4 active=3 "
            + "routed_frames=1200 orders=40 faces=5 route_corrections=2 releases=1 correct_m=1.50 per_frame=0 | writer_frames off=1 beast=0 parented=0 air=0 fight=7 refused=0 no_npc=0 "
            + "not_armed=0 correcting=3 figure=9 budget=0 not_human=0 no_physics=0 | A.2 gait orders walk=30 jog=10 run=0 sprint=0 changes=1 meter windows=20 within15=19 p50_bin=0 p90_bin=2 (5% bins) "
            + "| D.1 census dormant_now=1 went_dormant=2 woke=1 skipped_sends=5 ghosts=0",
            "[17:09:14.000] t1 MP-NPCPULL npc=a mean_cm=1.0 max_cm=60.0 frames=10",
            "[17:09:15.000] t1 WO215-ACTIVITY-MISSING npc=ttkc_kovar host=working(Smith_Hammer frag 300) refused-here",
        };
        var kcd = new List<string> { "[KCD2-MP] WO215-DORMANT npc=dummyWanderer_cuman_3 unbound=true" };
        string t = W215Section.Lines(null, native, new List<string>(), kcd);
        Assert.Contains("copies walked by the game's own walk", t);
        Assert.Contains("the writer pulled a copy back 2 time(s)", t);
        Assert.Contains("within 15 % in 95 %", t);
        Assert.Contains("single-frame jumps over 50 cm: 1 window(s)", t);
        Assert.Contains("ttkc_kovar", t);
        Assert.Contains("copies parked because their host NPC was dormant: 1", t);
        Assert.Contains("the session probe's file is not in this bundle", t);
    }

    [Fact]
    public void The_session_probe_files_go_into_the_anim_folder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "w215pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "kcd.log"), "x\n");
            File.WriteAllText(Path.Combine(dir, AnimStream.SessionFileName), Line(1, "a", "copy", "standing", "standing") + "\n");
            var src = new List<SourceFile> { new("kcd.log", Path.Combine(dir, "kcd.log")) };
            var outp = Packer.ArrangeAnimSession(src);
            Assert.Contains(outp, s => s.EntryName == AnimStream.SessionEntry);
            Assert.DoesNotContain(outp, s => s.EntryName == AnimStream.SessionPrevEntry);   // no .1 file beside it: none listed
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
