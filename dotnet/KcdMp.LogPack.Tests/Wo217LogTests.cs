// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.LogPack;

namespace KcdMp.LogPack.Tests;

/// <summary>WO-217 H: the briefing's Link lines for the world around the bodies.</summary>
public class Wo217LogTests
{
    [Fact]
    public void The_joiners_ledger_is_balanced_when_every_take_ended()
    {
        var game = new[]
        {
            "[KCD2-MP] WO217-LEDGER side=joiner ev=request body=bandit_1 item=h1 cls=c amt=1 tok=10",
            "[KCD2-MP] WO217-LEDGER side=joiner ev=applied body=bandit_1 item=h1 cls=c amt=1 tok=10 stolen=0 owner=-",
            "[KCD2-MP] WO217-LEDGER side=joiner ev=request body=bandit_1 item=h2 cls=c amt=1 tok=11",
            "[KCD2-MP] WO217-LEDGER side=joiner ev=refused body=bandit_1 item=h2 cls=c amt=1 tok=11 why=gone",
        };
        var b = W217Section.Balance(game, "joiner");
        Assert.Equal(2, b.Requests); Assert.Equal(1, b.Applied); Assert.Equal(1, b.Refused); Assert.Empty(b.Open);
        var open = W217Section.Balance(game.Take(3), "joiner");
        Assert.Equal(new[] { "h2" }, open.Open);   // a take asked and never answered is named
    }

    [Fact]
    public void The_hosts_ledger_counts_its_grants_against_acks_and_cancels()
    {
        var game = new[]
        {
            "[KCD2-MP] WO217-LEDGER side=host ev=request body=b item=x1 cls=c amt=1 tok=7 peer=1",
            "[KCD2-MP] WO217-LEDGER side=host ev=grant body=b item=x1 cls=c amt=1 tok=7 stolen=0 owner=-",
            "[KCD2-MP] WO217-LEDGER side=host ev=removed body=b item=x1 cls=c amt=1 tok=7 into=escrow",
            "[KCD2-MP] WO217-LEDGER side=host ev=acked body=b item=x1 cls=c amt=1 tok=7 escrow=deleted",
        };
        Assert.Empty(W217Section.Balance(game, "host").Open);
        Assert.Equal(new[] { "x1" }, W217Section.Balance(game.Take(3), "host").Open);
    }

    [Fact]
    public void The_lines_say_the_placements_and_the_seats()
    {
        var native = new[] { "[20:00:00.000] t1 WO217-STATUS w217 bits=0x3FF twin_off=0x0 configured=1 role=2 | A.0 attempts placed=40/52 placed-then-lost=2/2 refused=1/1 deferred=0/0 stale=3/3 paced=0/0 held=1/1 superseded=0/4 not-tried=0/1 shown_1s_of_2s=36 | 0.2 seat_holds=900 seat_places=1 seat_releases=2 seat_pulls=0" };
        string s = W217Section.Lines(native, Array.Empty<string>());
        Assert.Contains("52 of 64 attempts shown", s);
        Assert.Contains("released from a seat their host NPC left 2 times", s);
        Assert.Contains("pulls over 50 cm 0", s);
        Assert.Contains("an older build", W217Section.Lines(Array.Empty<string>(), Array.Empty<string>()));
    }
}
