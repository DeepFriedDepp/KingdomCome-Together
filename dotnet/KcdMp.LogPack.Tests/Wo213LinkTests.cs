// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.LogPack.Tests;

/// <summary>WO-213 G: the bug report's Link section -- what it reads, and a damaged line never costs the briefing.</summary>
public class Wo213LinkTests
{
    private static readonly string[] Agent =
    [
        "06:16:15.993 m=1 off=+0 MP-LINK kinds who=joiner window_s=10.0 out_Bps=50 out_mps=3 in_Bps=5000 in_mps=60 out=pos:40/2.0,ping:10/1.0 in=npc-state:4000/50.0,pos:1000/10.0",
        "06:16:15.993 m=1 off=+0 MP-LINK npc-bands who=joiner window_s=10.4 fight=5:p50=26.1Hz:p10=20.2Hz 40m=1:p50=0.5Hz:p10=0.5Hz 150m=1:p50=9.9Hz:p10=9.9Hz far=0:p50=0.0Hz:p10=0.0Hz",
        "06:16:26.497 m=1 off=+0 MP-LINK npc-bands who=joiner window_s=10.4 fight=5:p50=.Hz:p10=.Hz 40m=1",   // a line the disk cut short
    ];

    [Fact]
    public void The_section_reads_the_stream_and_the_fight_rate_and_skips_a_damaged_line()
    {
        string s = LinkSection.Section(Agent, []);
        Assert.StartsWith(LinkSection.Heading, s);
        Assert.Contains("npc-state", s);
        Assert.Contains("26.1 Hz p50 over 1 windows", s);
        // the twin: the first parse (double.Parse of the matched digits) threw on the damaged line and took the briefing with it
        Assert.Throws<FormatException>(() => double.Parse(".", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_bundle_without_link_lines_says_so()
    {
        Assert.Contains("No link numbers", LinkSection.Section(["06:00:00.000 m=1 off=+0 MP-BUILD version=0.63.9"], []));
    }
}
