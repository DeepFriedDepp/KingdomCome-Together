// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.LogPack;

namespace KcdMp.LogPack.Tests;

/// <summary>WO-224 C: the bug report's briefing says whether the build is the project's own (the agent's offline check, its
/// MP-OFFICIAL line).</summary>
public class Wo224LogTests
{
    private static Briefing.Input Inp(params string[] agent)
    {
        var inp = new Briefing.Input();
        inp.Logs["agent.log"] = agent;
        return inp;
    }

    [Fact]
    public void A_signed_build_reads_official_with_its_version()
    {
        var inp = Inp("MP-BUILD version=0.65.5 build=abc1234-2611270000 (x)",
                      "MP-OFFICIAL official=1 text=\"Official build 0.65.5\" reason=\"signed by the project's key; every file as signed\"");
        Assert.Equal("**Official build 0.65.5**\n\n", Briefing.OfficialLine(inp));
    }

    [Fact]
    public void A_build_without_the_signature_reads_unofficial_with_the_reason()
    {
        var inp = Inp("MP-OFFICIAL official=0 text=\"Unofficial build — not made by Kingdom Come: Together\" reason=\"no signature beside the binaries: a build made without the project's key\"");
        string line = Briefing.OfficialLine(inp);
        Assert.StartsWith("**Unofficial build — not made by Kingdom Come: Together**", line);
        Assert.Contains("no signature beside the binaries", line);
    }

    [Fact]
    public void A_line_that_claims_official_without_the_flag_or_the_words_reads_unofficial()
    {
        // the flag and the words must agree: neither alone makes the briefing say "Official"
        Assert.StartsWith("**Unofficial build", Briefing.OfficialLine(Inp("MP-OFFICIAL official=0 text=\"Official build 0.65.5\" reason=\"x\"")));
        Assert.StartsWith("**Unofficial build", Briefing.OfficialLine(Inp("MP-OFFICIAL official=1 text=\"The best build 0.65.5\" reason=\"x\"")));
    }

    [Fact]
    public void An_agent_older_than_the_check_is_not_stated()
    {
        Assert.StartsWith("**Build status:** not stated", Briefing.OfficialLine(Inp("MP-BUILD version=0.65.2 build=abc (x)")));
    }

    [Fact]
    public void The_newest_statement_counts()
    {
        var inp = Inp("MP-OFFICIAL official=1 text=\"Official build 0.65.5\" reason=\"signed\"",
                      "MP-OFFICIAL official=0 text=\"Unofficial build — not made by Kingdom Come: Together\" reason=\"KCDMP.dll is not the file that was signed\"");
        Assert.Contains("KCDMP.dll is not the file that was signed", Briefing.OfficialLine(inp));
    }
}
