// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-154: the rules of this WO (docs/WO-154-findings.md).</summary>
public class Wo154Tests
{
    // the field's per-player trigger (the host's and the joiner's own distance to the intermission)
    private const string Trigger = "Barbora.trosecko.socky.straznikuv_monokl.IntermissionTriggerByDistance.WaitForIntermission";
    // a story step that moves on
    private const string Counter = "Barbora.trosecko.mysi1.carryingBags.pickedSacks";

    [Fact]
    public void A_value_put_straight_back_by_this_world_is_contested()
    {
        var c = new Wo154Contest();
        c.NoteImposed(Trigger, 1, 0, 1000);                     // the host's 1->0 applied here
        Assert.True(c.NoteLocal(Trigger, 0, 1, 3000, out var why));   // this world's own trigger: back to 1
        Assert.Contains("put it back from 0 to 1", why);
        Assert.True(c.IsContested(Trigger));
        Assert.Equal(1, c.Count);
        Assert.False(c.NoteLocal(Trigger, 1, 0, 4000, out _));  // found once
    }

    [Fact]
    public void A_story_step_that_moves_on_is_never_contested()
    {
        var c = new Wo154Contest();
        c.NoteImposed(Counter, 2, 3, 1000);
        Assert.False(c.NoteLocal(Counter, 3, 4, 2000, out _));   // progress, not a return
        Assert.False(c.IsContested(Counter));
    }

    [Fact]
    public void A_return_after_the_window_is_ordinary_play()
    {
        var c = new Wo154Contest();
        c.NoteImposed(Trigger, 1, 0, 1000);
        Assert.False(c.NoteLocal(Trigger, 0, 1, 1000 + Wo154Contest.WindowMs + 1, out _));
        Assert.False(c.IsContested(Trigger));
    }

    [Fact]
    public void Without_an_imposed_value_nothing_is_contested()
    {
        var c = new Wo154Contest();
        Assert.False(c.NoteLocal(Trigger, 0, 1, 1000, out _));
        Assert.False(c.NoteLocal(Trigger, 1, 0, 1100, out _));
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void A_return_to_another_value_is_not_a_return()
    {
        var c = new Wo154Contest();
        c.NoteImposed(Counter, 1, 3, 1000);
        Assert.False(c.NoteLocal(Counter, 3, 2, 1500, out _));
        Assert.False(c.IsContested(Counter));
    }

    [Fact]
    public void The_other_side_marks_it_once_and_a_reset_forgets()
    {
        var c = new Wo154Contest();
        Assert.True(c.Mark(Trigger, "the host's world puts it back by itself"));
        Assert.False(c.Mark(Trigger, "again"));
        Assert.True(c.IsContested(Trigger));
        c.NoteImposed(Trigger, 0, 1, 1000);   // nothing is tracked for a contested State
        c.Reset();
        Assert.False(c.IsContested(Trigger));
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void A_downed_bit_falls_only_after_it_held_and_rises_only_after_it_cleared()
    {
        var d = new Wo154Rules.DownEdge();
        Assert.Null(d.Feed(true, 1000));                    // a frame of ragdoll: not yet
        Assert.Null(d.Feed(false, 1050));                   // gone again: nothing ever fell
        Assert.False(d.Down);
        Assert.Null(d.Feed(true, 2000));
        Assert.Null(d.Feed(true, 2150));
        Assert.True(d.Feed(true, 2200));                    // held 200 ms: he is down
        Assert.True(d.Down);
        Assert.Null(d.Feed(true, 3000));                    // no second fall
        Assert.Null(d.Feed(false, 4000));                   // the ragdoll settles for a moment...
        Assert.Null(d.Feed(true, 4100));                    // ...still down: no stand-up and fall again
        Assert.Null(d.Feed(false, 5000));
        Assert.Null(d.Feed(false, 5400));
        Assert.False(d.Feed(false, 5500));                  // clear 500 ms: he is up
        Assert.False(d.Down);
        Assert.Null(d.Feed(false, 6000));
    }

    [Fact]
    public void The_contested_verdict_parses_and_the_worker_flag_reads()
    {
        string text = Wo137Rules.ResultText(Wo154Rules.VerdictContested, 1, "", Trigger);
        Assert.True(Wo137Rules.TryParseResultText(text, out var v, out int hv, out var port, out var path));
        Assert.Equal("contested", v);
        Assert.Equal(1, hv);
        Assert.Equal("", port);
        Assert.Equal(Trigger, path);
        var q = new QuestChange(1, QuestChange.FNotify | QuestChange.FWorker, 1, 0, "SetFalse", "bool", Trigger, 40);
        Assert.True(q.Worker);
        Assert.False(q.Cascade);
        Assert.NotEqual(QuestChange.FWorker, Wo147Rules.FlagConversation);
    }
}
