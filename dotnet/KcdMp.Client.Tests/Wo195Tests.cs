// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-195: the pure rules (Wo195Rules), the crime report's 8th field (Wo139Rules) and the source pins of the hooks.</summary>
public class Wo195Tests
{
    private static string Root()
    {
        string d = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d)!;
        return d;
    }
    private static string Src(string f) => File.ReadAllText(Path.Combine(Root(), "dotnet", "KcdMp.Client", f));
    private static string Native(string f) => File.ReadAllText(Path.Combine(Root(), "native", "KCDMP", f));

    // ================================================================ 1: dead copies

    [Fact]
    public void A_death_releases_once_per_copy_and_every_path_is_named()
    {
        Assert.True(Wo195Rules.ReleaseDue(true, 0, 10_000));
        Assert.False(Wo195Rules.ReleaseDue(true, 10_000, 10_150));                       // the same death's 0x31 / 0x27 paths, 0.15 s later
        Assert.True(Wo195Rules.ReleaseDue(true, 10_000, 10_000 + Wo195Rules.ReleaseDedupeMs));
        Assert.False(Wo195Rules.ReleaseDue(false, 0, 10_000));                            // mp_dead_release off: 0.60.9's way
        Assert.Equal("owner-death", Wo195Rules.PathTag("owner-death (local copy alive)"));
        Assert.Equal("0x31-fatal", Wo195Rules.PathTag("0x31 FATAL"));
        Assert.Equal("0x27-transition", Wo195Rules.PathTag("0x27 dead transition"));
        Assert.Equal("0x27-resync", Wo195Rules.PathTag("0x27 resync dead"));
        Assert.Equal("local-fatal", Wo195Rules.PathTag("local-fatal"));
    }

    [Fact]
    public void A_dead_copy_is_never_engaged_again_while_the_switch_is_on()
    {
        Assert.True(Wo195Rules.Blocks(true, markedDead: true));
        Assert.False(Wo195Rules.Blocks(true, markedDead: false));
        Assert.False(Wo195Rules.Blocks(false, markedDead: true));
    }

    [Fact]
    public void The_native_held_mask_reads_as_words()
    {
        Assert.Equal("none", Wo195Rules.HeldText(0));
        Assert.Equal("engage+wish+pin", Wo195Rules.HeldText(Wo195Rules.HeldEngage | Wo195Rules.HeldWish | Wo195Rules.HeldPin));
        Assert.Equal("guard+combat", Wo195Rules.HeldText(Wo195Rules.HeldGuard | Wo195Rules.HeldCombat));
        Assert.Equal("?", Wo195Rules.Tri(255, "on", "off"));
        Assert.Equal("off", Wo195Rules.Tri(0, "on", "off"));
    }

    [Fact]
    public void The_native_mask_bits_match_wo195_rules_h()
    {
        string h = Native("wo195_rules.h");
        Assert.Contains("kHeldEngage = 0x01", h);
        Assert.Contains("kHeldGuard  = 0x02", h);
        Assert.Contains("kHeldWish   = 0x04", h);
        Assert.Contains("kHeldPin    = 0x08", h);
        Assert.Contains("kHeldCombat = 0x10", h);
        Assert.Contains("kOpStatus = 54, kOpConfig = 55, kOpRelease = 56, kOpCheck = 57", Native("wo195.h"));
        Assert.Equal(56, Protocol.Wo195OpRelease);
        Assert.Equal(57, Protocol.Wo195OpCheck);
    }

    [Fact]
    public void Every_death_path_and_every_hold_is_hooked()
    {
        string gb = Src("GameBridge.cs");
        int rel = gb.IndexOf("await Wo195OnCopyDeathAsync(npcName, via);", StringComparison.Ordinal);
        int apply = gb.IndexOf("applied = await _combat.ApplyDeathAsync(lg, ct);", StringComparison.Ordinal);
        Assert.True(rel > 0 && apply > rel, "the release runs before the apply (the copy is marked dead before the engine kills it)");
        Assert.Contains("Wo195OnLocalFatal(soul);", gb);
        Assert.Contains("Wo195NoteAlive(npcName);", gb);
        string w132 = Src("GameBridge.Wo132.cs");
        Assert.Contains("bool w195Dead = Wo195IsDead(ev.Name);", w132);
        Assert.Contains("!w195Dead && Wo187OnNpcCombatIn(", w132);
        // the DLL's own gates: the writer's per-frame hold, the wish (apply, re-check, wind-up, a new one), the guard hold, the engage
        Assert.Contains("wo195::dead_gate(eid, \"combat-hold\")", Native("motion.cpp"));
        string w187 = Native("wo187.cpp");
        Assert.Contains("if (wo195::dead_gate(copy, \"wish\")) return;", w187);
        Assert.Contains("if (wo195::dead_gate(it->first, \"wish\"))", w187);
        Assert.Contains("!wo195::dead_gate(eid, \"wish\")", w187);
        Assert.Contains("wo195::dead_gate(eid, \"guard-hold\")", w187);
        Assert.Contains("wo195::dead_gate(eid, \"engage\")", Native("wo132.cpp"));
    }

    // ================================================================ 2: stealth

    [Fact]
    public void The_stealth_field_round_trips_and_refuses_anything_else()
    {
        var s = new Wo195Rules.Stealth(true, false, 1.25f, 0.8f, 0.5f, 7);
        Assert.Equal("c=1,l=0,v=1.25,n=0.80,s=0.50,k=7", s.ToText());
        Assert.True(Wo195Rules.TryParseStealth(s.ToText(), out var r));
        Assert.Equal(s, r);
        Assert.True(Wo195Rules.TryParseStealth("c=0,l=1,v=-99.00,n=-99.00,s=-99.00,k=-1", out var u));
        Assert.True(u.Light && !u.Crouch && u.Vis == -99f && u.Skill == -1);
        Assert.False(Wo195Rules.TryParseStealth("c=1,l=0,v=1.25,n=0.80,s=0.50", out _));          // a field missing
        Assert.False(Wo195Rules.TryParseStealth("c=1,l=0,v=1.25,n=0.80,s=0.50,k=7\")", out _));   // a quote: never into the host's Lua
        Assert.False(Wo195Rules.TryParseStealth("", out _));
        Assert.False(Wo195Rules.TryParseStealth(null, out _));
    }

    [Fact]
    public void A_crime_report_carries_the_stealth_as_its_8th_field()
    {
        var r = new Wo139Rules.Report("trespass", 10.5f, 20.25f, 3f, "", "", "area", "c=1,l=0,v=1.00,n=1.00,s=1.00,k=7");
        string t = Wo139Rules.ReportText(r);
        Assert.Equal("trespass 10.50 20.25 3.00 - - area c=1,l=0,v=1.00,n=1.00,s=1.00,k=7", t);
        Assert.True(Wo139Rules.TryParseReport(t, out var back));
        Assert.Equal(r, back);
        // a 0.60.9 joiner: seven fields, no stealth
        Assert.True(Wo139Rules.TryParseReport("trespass 10.50 20.25 3.00 - - area", out var old));
        Assert.Equal("", old.Stealth);
        Assert.Equal("trespass 10.50 20.25 3.00 - - area", Wo139Rules.ReportText(old));
        // a malformed 8th field refuses the whole report (it would reach a Lua literal)
        Assert.False(Wo139Rules.TryParseReport("trespass 10.50 20.25 3.00 - - area c=1\"),os.exit(", out _));
    }

    [Fact]
    public void Asleep_is_lying_on_an_object_in_the_hosts_own_activity_read()
    {
        var bed = new ActivityState(ActivityState.Lying, 0, 0x4DF108F24EFFA474, ActivityState.NoUnstance, 0, ActivityState.NoMinigame, 0, 0);
        var floor = bed with { StanceObj = 0 };   // lying with no object: a corpse, a knockout -- not a sleeper
        var bench = bed with { Stance = ActivityState.Sitting };
        Assert.True(Wo195Rules.AsleepByActivity(bed));
        Assert.False(Wo195Rules.AsleepByActivity(floor));
        Assert.False(Wo195Rules.AsleepByActivity(bench));
        Assert.False(Wo195Rules.AsleepByActivity(ActivityState.None));
    }

    // ================================================================ 3: the loot screen's key

    [Fact]
    public void A_take_is_marked_screen_only_by_the_word()
    {
        Assert.True(Wo195Rules.IsScreenMark("screen"));
        Assert.False(Wo195Rules.IsScreenMark("Screen"));
        Assert.False(Wo195Rules.IsScreenMark(""));
        string w134 = Src("GameBridge.Wo134.cs");
        Assert.Contains("f.Length == 6 && name == \"w134_take\" && Wo195Rules.IsScreenMark(f[5])", w134);   // the joiner's event
        Assert.Contains("f.Length == 5 && m.Kind == Protocol.LootAskBodyTake && Wo195Rules.IsScreenMark(f[4])", w134);   // the host's frame
    }
}
