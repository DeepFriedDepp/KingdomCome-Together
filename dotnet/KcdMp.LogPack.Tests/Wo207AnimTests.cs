// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;

namespace KcdMp.LogPack.Tests;

/// <summary>WO-207 G.2: "the joiner's copy runs a different full-body action from the host's NPC", defined -- the same family within the
/// stream's delay, idle and locomotion by speed band; and its twins (each equivalence switched off finds the difference again).</summary>
public class Wo207AnimTests
{
    [Fact]
    public void Idle_variations_and_standing_are_one_family_and_locomotion_compares_by_gait()
    {
        Assert.Equal("idle", AnimCompare.Family("C_VariationAction", 1718));
        Assert.Equal("idle", AnimCompare.Family("CAnimActionAIMovement", 5));
        Assert.Equal("locomotion", AnimCompare.Family("CAnimActionAIMovement", 4));
        Assert.Equal("C_CallbackAction#43", AnimCompare.Family("C_CallbackAction", 43));
        Assert.True(AnimCompare.SameAction("C_VariationAction", 1718, 0.0, "C_AnimActionIdleState", 45, 0.1));      // both standing
        Assert.True(AnimCompare.SameAction("CAnimActionAIMovement", 4, 1.4, "CAnimActionMovementTransition", 35, 1.2));   // both walking
        Assert.False(AnimCompare.SameAction("CAnimActionAIMovement", 4, 1.4, "C_AnimActionIdleState", 45, 0.0));   // twin: walking vs standing
        Assert.False(AnimCompare.SameAction("CAnimActionAIMovement", 4, 3.5, "CAnimActionAIMovement", 4, 1.2));     // twin: running vs walking
        Assert.True(AnimCompare.SameAction("C_CallbackAction", 43, 0, "C_CallbackAction", 43, 0));
        Assert.False(AnimCompare.SameAction("C_CallbackAction", 43, 0, "C_VariationAction", 1718, 0));             // twin: an activity vs idling
        Assert.False(AnimCompare.SameAction(null, -1, 1.4, "CAnimActionAIMovement", 4, 1.4));                      // twin: an empty FullBody
    }

    [Fact]
    public void WO208_the_fragment_family_counts_one_fragment_as_one_animation_whatever_class_carries_it()
    {
        Assert.Equal("fragment#43", AnimCompare.FragmentFamily("C_CallbackAction", 43));
        Assert.Equal("idle", AnimCompare.FragmentFamily("C_VariationAction", 1718));
        Assert.True(AnimCompare.SameFragment("C_CallbackAction", 43, 0, "C_CombatAnimAction", 43, 0));     // the owner's install of the host's fragment
        Assert.False(AnimCompare.SameAction("C_CallbackAction", 43, 0, "C_CombatAnimAction", 43, 0));     // the WO-207 measure counts the class
        Assert.False(AnimCompare.SameFragment("C_CallbackAction", 43, 0, "C_CombatAnimAction", 44, 0));    // twin: another fragment differs
        Assert.False(AnimCompare.SameFragment(null, -1, 0, "C_CombatAnimAction", 43, 0));                  // twin: an empty FullBody differs
    }

    private static string Line(long t, string m, string name, string kind, double x, string cls, int fr) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{{\"t\":{t},\"m\":\"{m}\",\"f\":1,\"a\":[{{\"n\":\"{name}\",\"k\":\"{kind}\",\"p\":[{x:0.###},0,10],\"dead\":0,\"rag\":0,\"q\":0,\"sc\":[{{\"s\":\"FullBody\",\"c\":\"{cls}\",\"fr\":{fr},\"pr\":2}}]}}]}}\n");

    private static AnimCompareResult Pair(Func<int, (string Cls, int Fr)> host, Func<int, (string Cls, int Fr)> joiner, int jLagSamples = 2)
    {
        var h = new StringBuilder(); var j = new StringBuilder();
        const long T0 = 1_760_000_000_000;
        for (int i = 0; i < 200; i++)
        {
            double x = i * 0.14;   // 1.4 m/s
            var (hc, hf) = host(i);
            h.Append(Line(T0 + i * 100, "host", "npc_a", "npc", x, hc, hf));
            int k = Math.Max(0, i - jLagSamples);   // the copy shows the host 200 ms late
            var (jc, jf) = joiner(k);
            j.Append(Line(T0 + i * 100, "joiner", "npc_a", "copy", k * 0.14, jc, jf));
        }
        return AnimCompare.Compare(AnimStream.Parse(Encoding.UTF8.GetBytes(h.ToString())), AnimStream.Parse(Encoding.UTF8.GetBytes(j.ToString())));
    }

    [Fact]
    public void A_copy_that_walks_in_another_clip_runs_the_same_action_but_one_that_idles_does_not()
    {
        // the host's NPC walks in the AI movement action; the copy in the transition clip (the same gait): not a different action, though
        // WO-206's exact measure counts every sample
        var same = Pair(_ => ("CAnimActionAIMovement", 4), _ => ("CAnimActionMovementTransition", 35));
        Assert.True(same.ActionMismatch < 0.05, $"{same.ActionMismatch}");
        Assert.True(same.PoseMismatch > 0.9, $"{same.PoseMismatch}");
        // twin: the host's NPC uses an object (an activity) for the middle 10 s while its copy stands idle: about half the samples differ
        var diff = Pair(i => i is >= 50 and < 150 ? ("C_CallbackAction", 43) : ("CAnimActionAIMovement", 4), _ => ("CAnimActionAIMovement", 4));
        Assert.InRange(diff.ActionMismatch ?? 0, 0.40, 0.55);
        Assert.Contains(diff.TopMismatches, m => m.Host == "C_CallbackAction#43" && m.Joiner == "locomotion");
    }

    [Fact]
    public void A_change_inside_the_window_around_the_delay_is_the_stream_not_a_different_action()
    {
        // the host switches action at sample 100; the copy follows 200 ms later (the stream): within the 300 ms window, no mismatch
        var r = Pair(i => i < 100 ? ("C_CallbackAction", 43) : ("C_CallbackAction", 44), i => i < 100 ? ("C_CallbackAction", 43) : ("C_CallbackAction", 44), jLagSamples: 2);
        Assert.True(r.ActionMismatch < 0.02, $"{r.ActionMismatch}");
    }
}
