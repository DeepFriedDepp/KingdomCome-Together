// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;
using KcdMp.Wire;
using Xunit;
using Xunit.Abstractions;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-187: replays of the 0.60.3 two-player session's guard chase through the new rules.
///   * The field replay reads the testers' bundles (private, never committed) unpacked in logs/w187/field-host/ and logs/w187/field-joiner/;
///     it is skipped (passes with a note) when they are not there.
///   * The timeline replay always runs: the session's timeline as the findings quote it (the guard fought the host from 00:34; from 00:40:09
///     the host streamed combat=1 target=Avatar:1 with the guard 40 m from the joiner's avatar; its first blow on him at 00:40:27; on the joiner's
///     screen its copy ran 4-9 m away from where the host had it), one host report a second, through the 0.60.3 and the 0.60.5 rules.
/// </summary>
public class Wo187ReplayTests
{
    private readonly ITestOutputHelper _out;
    public Wo187ReplayTests(ITestOutputHelper o) { _out = o; }

    private static NpcCombatEvent Ev(bool combat, NpcCombatTarget t, byte ghost) =>
        new(1, new BodyState2(0, 0, combat ? BodyState2Bits.CombatMode : BodyState2Bits.None, 0, 0, 0, 0, 0, 0), t, ghost, "ttac_man_9");

    private sealed record Tick(int T, bool Combat, NpcCombatTarget Target, byte Ghost, float HostDistM, float CopyDistM, bool Hit);

    /// <summary>The guard chase, a report a second (T = seconds after 00:40:00): the host's distance from the joiner's avatar closes from 40 m at
    /// about 2 m/s; the copy on the joiner's screen runs 6 m behind the host's NPC (the field's 4-9 m); its first blow lands at T=27.</summary>
    private static List<Tick> Timeline()
    {
        var l = new List<Tick>();
        for (int t = 0; t < 9; t++) l.Add(new Tick(t, true, NpcCombatTarget.Host, 0, 60f, 60f, false));   // still on the host (00:34 on)
        for (int t = 9; t <= 32; t++)
        {
            float host = MathF.Max(1.6f, 40f - 2f * (t - 9));
            float copy = host + 6f;
            l.Add(new Tick(t, true, NpcCombatTarget.Avatar, 1, host, copy, t == 27));
        }
        return l;
    }

    [Fact]
    public void Timeline_EngagesAndSetsTheOpponentAtTheTargetChange()
    {
        const byte me = 1, hostGhost = 0;
        int oppOld = -1, oppNew = -1, engageOld = -1, engageNew = -1, lines = 0, wishes = 0;
        (byte, byte, byte)? lastWish = null;
        bool engagedOld = false, engagedNew = false;
        foreach (var k in Timeline())
        {
            var ev = Ev(k.Combat, k.Target, k.Ghost);
            // 0.60.3: engaged when the verdict says so AND the copy here is within 15 m (the DLL's own check); the copy's opponent was the
            // engine's (the engage's skirmish), WO-178 only set the target once the opponent already was this player -- at its first wind-up
            var v = Wo132Rules.JudgeEngage(ev, true, true, true, engagedOld, myGhost: me);
            if (v == Wo132Rules.EngageVerdict.Engage && k.CopyDistM <= Wo187Rules.EngageRangeM && !engagedOld) { engagedOld = true; engageOld = k.T; }
            if (engagedOld && oppOld < 0 && k.Hit) oppOld = k.T;   // the field: the opponent was the joiner only from the blow
            // 0.60.5: the wish at once (A2); the engage by the copy's range OR the host's own position (A1)
            var w = Wo187Rules.OppWish(ev, me, hostGhost);
            if (w is { } ww && (lastWish is null || lastWish.Value != ww)) { wishes++; lastWish = ww; if (ww.Kind == Wo187Rules.OppLocalPlayer && oppNew < 0) oppNew = k.T; }
            var v2 = Wo132Rules.JudgeEngage(ev, true, true, true, engagedNew, myGhost: me);
            bool intent = Wo187Rules.IntentInRange(true, k.Target == NpcCombatTarget.Avatar && k.Ghost == me, k.HostDistM, 200);
            if (v2 == Wo132Rules.EngageVerdict.Engage && (k.CopyDistM <= Wo187Rules.EngageRangeM || intent) && !engagedNew) { engagedNew = true; engageNew = k.T; }
            // B1: a fight-position line every second of the fight on the joiner
            if (k.Target != NpcCombatTarget.None)
            {
                string line = Wo187Rules.JoinerPosLine("ttac_man_9", $"00:40:{k.T:00}.000", k.CopyDistM, 0f, (k.HostDistM, 0f, 100, 20, 0u), 0f, 0f, k.Target == NpcCombatTarget.Host ? "host" : "avatar:1");
                Assert.Contains(FormattableString.Invariant($"lag_m={MathF.Abs(k.CopyDistM - k.HostDistM):F2}"), line);
                lines++;
            }
        }
        _out.WriteLine($"A2 opponent = the joiner: 0.60.3 at 00:40:{oppOld:00} (the first blow), 0.60.5 at 00:40:{oppNew:00} (the target change)");
        _out.WriteLine($"A1 engage: 0.60.3 at 00:40:{engageOld:00} (the copy within 15 m here), 0.60.5 at 00:40:{engageNew:00} (the host's NPC within 15 m)");
        _out.WriteLine($"A2 wishes sent on a change: {wishes} (the host, then the joiner); B1 lines: {lines}");
        Assert.Equal(9, oppNew);                          // the target change at 00:40:09 sets the opponent at once
        Assert.True(engageNew < engageOld, "engaged earlier than 0.60.3");
        Assert.Equal(27, oppOld);
        Assert.Equal(2, wishes);
        Assert.Equal(24 + 9, lines);
    }

    [Fact]
    public void Timeline_TargetSwitchesBackAndForth_EveryChangeIsAWish()
    {
        // the guard switches to the host and back twice: each change is one wish, the "none" moment between two targets none
        var seq = new[] { (NpcCombatTarget.Avatar, (byte)1), (NpcCombatTarget.None, (byte)0), (NpcCombatTarget.Host, (byte)0), (NpcCombatTarget.Host, (byte)0),
                          (NpcCombatTarget.Avatar, (byte)1), (NpcCombatTarget.Host, (byte)0), (NpcCombatTarget.Avatar, (byte)1) };
        (byte, byte, byte)? last = null;
        var kinds = new List<byte>();
        foreach (var (t, g) in seq)
            if (Wo187Rules.OppWish(Ev(true, t, g), 1, 0) is { } w && (last is null || last.Value != w)) { last = w; kinds.Add(w.Kind); }
        Assert.Equal(new byte[] { Wo187Rules.OppLocalPlayer, Wo187Rules.OppFigure, Wo187Rules.OppLocalPlayer, Wo187Rules.OppFigure, Wo187Rules.OppLocalPlayer }, kinds);
    }

    // ------------------------------------------------------------------ the field bundles, when present

    private static string? Bundle(string name)
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d);
        if (d is null) return null;
        string p = Path.Combine(d, "logs", "w187", name);
        return File.Exists(Path.Combine(p, "agent.log")) ? p : null;
    }

    private static readonly Regex CombatOut = new(@"^(\d\d:\d\d:\d\d\.\d{3}) .*MP-W132 npc-combat out npc=(?<npc>\S+) .*combat=(?<c>[01]).* target=(?<t>None|Host|Other|Avatar:\d+)", RegexOptions.Compiled);
    private static readonly Regex EngageOn = new(@"^(\d\d:\d\d:\d\d\.\d{3}) .*MP-W132 engage on (?<npc>\S+) ", RegexOptions.Compiled);

    [Fact]
    public void Field_TheGuardsTargetChangeIsAWishAtOnce()
    {
        string? host = Bundle("field-host"), joiner = Bundle("field-joiner");
        if (host is null || joiner is null) { _out.WriteLine("the 0.60.3 session's bundles are not in logs/w187/field-host and field-joiner -- skipped"); return; }
        var outs = File.ReadLines(Path.Combine(host, "agent.log")).Select(l => CombatOut.Match(l)).Where(m => m.Success).ToList();
        var firstMine = outs.FirstOrDefault(m => m.Groups["t"].Value == "Avatar:1");
        Assert.NotNull(firstMine);
        string npc = firstMine!.Groups["npc"].Value;
        var engaged = File.ReadLines(Path.Combine(joiner, "agent.log")).Select(l => EngageOn.Match(l)).FirstOrDefault(m => m.Success && m.Groups["npc"].Value == npc);
        var ev = Ev(firstMine.Groups["c"].Value == "1", NpcCombatTarget.Avatar, 1);
        Assert.Equal(Wo187Rules.OppLocalPlayer, Wo187Rules.OppWish(ev, 1, 0)!.Value.Kind);
        _out.WriteLine($"{npc}: the host's first target=Avatar:1 at {firstMine.Groups[1].Value}; the 0.60.3 joiner engaged at {(engaged is { Success: true } e ? e.Groups[1].Value : "never")}; "
                       + "0.60.5 wishes the opponent at the first report");
    }
}
