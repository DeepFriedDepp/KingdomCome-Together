// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-183 (0.50.9): the rules of the playtest patch (docs/WO-183-findings.md).</summary>
public class Wo183Tests
{
    // ---------------------------------------------------------------- 2: the twin signs (the lines are the field's, Haggling bundle)

    private const string TwinLine = "MasterSlaveManager is setting context: '5' for entities 'DialogTwin_Dude'-> 'DialogTwin_DudeCharacterCameraAttachment'. Entire context - .";
    private const string NewHaggle = "New dialogue 'Barbora.open_world.shop.bribe_haggle_negotiation_vyjednavani_smlouvani_podplaceni.smlouvani_v_obchode_muz (normal - Id: 53) (Haggle)' is starting. Params: souls = 'Ex: Dude; Ex: ttkc_man_11' forced = false";
    private const string CmdHrac = "[ID: 53] DialogCommand-Anim: dialogId=53, who=HRAC, anim=ADLG_Nod";
    private const string CmdCut = "[ID: 53] DialogCommand-Cut: dialogId=53, who=SMLOUVANI_MUZ, mode=2, cameraAlias=";
    private const string CmdGossip = "[ID: 11] DialogCommand-Anim: dialogId=11, who=GOSSIP_OBECNY_MUZ_1, anim=ADLG_Gesture";
    private const string InterruptWaitingUser = "Interrupting dialog 'Barbora.open_world.shop.bribe_haggle (normal - Id: 53) [Ex0: Dude Ex1: ttkc_man_11 state: WAITING_FOR_USER_INPUT flags: 2113297] (Haggle)' in progress.";
    private const string InterruptWaitingTwins = "Interrupting dialog 'Barbora.open_world.shop.bribe_haggle (normal - Id: 93) [Ex0: Dude Ex1: ttkc_emerich state: WAITING_FOR_TWINS flags: 2097665] (Haggle)' in progress.";

    [Fact]
    public void A_twin_line_is_forwarded_and_counts_as_running()
    {
        Assert.True(LogTailGameTransport.Wo144Passes(TwinLine));
        Assert.True(Wo179Rules.ConversationRunning(TwinLine, -1));
    }

    [Fact]
    public void The_dialogue_id_of_the_players_own_dialogue_is_read_from_the_new_dialogue_line()
    {
        Assert.Equal(53, Wo179Rules.DudeDialogId(NewHaggle));
        Assert.Equal(-1, Wo179Rules.DudeDialogId("New dialogue 'x.y (ingame monolog - Id: 4) (AI::DoMonologue)' is starting. Params: souls = 'Ex: ttkc_man_30 - meta"));
        Assert.Equal(-1, Wo179Rules.DudeDialogId(CmdHrac));
    }

    [Fact]
    public void A_command_of_the_players_dialogue_counts_a_gossips_does_not()
    {
        Assert.True(Wo179Rules.ConversationRunning(CmdHrac, -1));          // the player's own alias
        Assert.True(Wo179Rules.ConversationRunning(CmdCut, 53));           // by the dialogue id
        Assert.False(Wo179Rules.ConversationRunning(CmdCut, 99));          // another dialogue's
        Assert.False(Wo179Rules.ConversationRunning(CmdCut, -1));
        Assert.False(Wo179Rules.ConversationRunning(CmdGossip, 11));       // ambient NPC-to-NPC speech never keeps the guard away
    }

    [Fact]
    public void A_state_past_waiting_for_twins_counts_waiting_for_twins_does_not()
    {
        Assert.True(Wo179Rules.ConversationRunning(InterruptWaitingUser, -1));
        Assert.False(Wo179Rules.ConversationRunning(InterruptWaitingTwins, -1));   // the emerich haggles: the guard is right to end them
        Assert.True(LogTailGameTransport.Wo144Passes(InterruptWaitingUser));
    }

    [Fact]
    public void The_field_conversation_that_made_no_twin_shows_no_sign()
    {
        string[] lines = [NewHaggle, InterruptWaitingTwins, "[ID: 93] Dialog interrupted. [Ex0: Dude Ex1: ttkc_emerich state: WAITING_FOR_TWINS flags: 2097673]"];
        Assert.DoesNotContain(lines, l => Wo179Rules.ConversationRunning(l, 93));
    }

    /// <summary>
    /// The class test (WO-182, folded in): every literal an engine-line handler matches must be passed by the forward lists, or the handler
    /// never sees the line -- the bug that ended every joiner conversation at 3.6-3.9 s. The handlers' source is read for the literals they
    /// test a line with; a literal that is not forwarded must be listed in <see cref="NotForwardedOnPurpose"/> with its reason.
    /// </summary>
    private static readonly (string Literal, string Why)[] NotForwardedOnPurpose =
    [
        ("] Dialog ", "Wo166Rules.TryParseDialogState reads the '[ID: n] Dialog <word>' family; its forwarded members are 'Dialog interrupted.', 'Dialog ends but no response was played' and (WO-183) 'Dialog ending'"),
        ("Enable", "a refinement inside Wo164OnEngineLine on lines already forwarded by 'of minigame entity'"),
        ("[ID: ", "a position test on lines forwarded by '] DialogCommand-' / 'Dialog interrupted.'"),
        ("(BT)", "the second half of '/(BT)' which is forwarded"),
        ("(NPC)", "the soul-name reader of a behaviour-tree path line, forwarded by '/(BT)'"),
        ("New dialogue '", "forwarded as a prefix: the regexes refine it"),
        ("crime_reaction_barks.kolize", "a bark is a 'New dialogue' line (forwarded as a prefix): the host's collision bark"),
        ("Execution of ", "a planner failure line carries its behaviour-tree path: forwarded by '/(BT)' (the field's lines)"),
        ("GOSSIP_", "a refinement of '] DialogCommand-' lines (already forwarded): ambient NPC-to-NPC speech"),
        ("SOUSEDSKY_", "a refinement of '] DialogCommand-' lines (already forwarded): a greeting between two NPCs"),
        ("HRAC", "a refinement of '] DialogCommand-' lines (already forwarded): the player's own alias"),
        ("[Ex0: Dude ", "a refinement of 'Interrupting dialog' lines (forwarded as a prefix)"),
        ("[Ex0: ", "a refinement of '[ID: n] Dialog interrupted.' lines (forwarded)"),
        ("[Nx0: ", "a refinement of '[ID: n] Dialog interrupted.' lines (forwarded)"),
        (" state: ", "a refinement of 'Interrupting dialog' / 'Dialog interrupted.' lines (forwarded)"),
        ("souls = 'Ex: Dude", "a refinement of 'New dialogue' lines (forwarded as a prefix)"),
        (" - Id: ", "a refinement of 'New dialogue' lines (forwarded as a prefix)"),
        (".nextnextgenpunishment.", "a refinement of 'New dialogue' lines (forwarded as a prefix): the game's own arrest dialogues"),
        ("started async waiting", "a refinement of the BeforeGameSave readiness prefix (forwarded)"),
        ("is ready", "a refinement of the BeforeGameSave readiness prefix (forwarded)"),
        // WO-184: the conversation clock's parser
        ("meta override: COMBAT_", "a refinement of 'Attempting to start new dialogue' (the QuestLine path) and 'New dialogue' (a prefix) lines: a combat shout"),
        ("monolog", "a refinement of 'New dialogue' lines (forwarded as a prefix): '(ingame monolog - Id: n)'"),
        ("DoMonologue", "a refinement of 'New dialogue' lines (forwarded as a prefix): '(AI::DoMonologue)'"),
        // WO-189: the profile lines' and the initialized line's parsers
        ("Ex: ", "a refinement of the souls of a 'New dialogue' line (forwarded as a prefix)"),
        (" cutscene '", "a refinement of 'CutscenePlayer::OnCutsceneInitialized called for ' lines (forwarded)"),
        (" with holder '", "a refinement of 'CutscenePlayer::OnCutsceneInitialized called for ' lines (forwarded)"),
    ];

    private static string ClientDir()
    {
        string d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "dotnet", "KcdMp.Client", "LogTailGameTransport.cs")))
            d = Path.GetDirectoryName(d)!;
        Assert.NotNull(d);
        return Path.Combine(d, "dotnet", "KcdMp.Client");
    }

    /// <summary>The body of the first method named <paramref name="name"/> in a file (brace-matched), or null.</summary>
    private static string? MethodBody(string source, string name)
    {
        var m = Regex.Match(source, @"\b" + Regex.Escape(name) + @"\s*\(");
        while (m.Success)
        {
            int open = source.IndexOf('{', m.Index);
            int semi = source.IndexOf(';', m.Index);
            int arrow = source.IndexOf("=>", m.Index, StringComparison.Ordinal);
            int paren = source.IndexOf(')', m.Index);
            // an expression-bodied method: "Name(args) => expr;" (the arrow right after the parameter list)
            if (arrow > 0 && paren > 0 && arrow > paren && source[(paren + 1)..arrow].Trim().Length == 0 && semi > arrow)
                return source[arrow..(semi + 1)];
            if (open > 0 && (semi < 0 || open < semi) && (arrow < 0 || open < arrow))
            {
                int depth = 0, i = open;
                for (; i < source.Length; i++)
                {
                    if (source[i] == '{') depth++;
                    else if (source[i] == '}' && --depth == 0) break;
                }
                return source[open..(i + 1)];
            }
            m = m.NextMatch();
        }
        return null;
    }

    private static IEnumerable<string> Literals(string body)
    {
        // a string literal passed to a line test: StartsWith("..."), Contains("..."), IndexOf("..."), EndsWith("...")
        foreach (Match m in Regex.Matches(body, @"\.(?:StartsWith|Contains|IndexOf|EndsWith)\(\s*""((?:[^""\\]|\\.)*)""")) yield return Unescape(m.Groups[1].Value);
    }

    private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");

    [Fact]
    public void Every_literal_an_engine_line_handler_matches_is_forwarded_by_the_log_tail()
    {
        string dir = ClientDir();
        string All(string f) => File.ReadAllText(Path.Combine(dir, f));
        var handlers = new (string File, string Method)[]
        {
            ("GameBridge.Wo179.cs", "Wo179OnEngineLine"), ("Wo179.cs", "ConversationRunning"), ("Wo179.cs", "DudeDialogId"),
            ("GameBridge.Wo167.cs", "Wo167OnEngineLine"), ("Wo167.cs", "BtPath"), ("Wo167.cs", "SyncGuardNpc"), ("Wo167.cs", "KolizeSoul"),
            ("Wo167.cs", "FaderTook"), ("Wo167.cs", "IsTravelInterruptLine"),
            ("GameBridge.Wo166.cs", "Wo166OnEngineLine"), ("Wo166.cs", "TryParseDialogState"),
            ("GameBridge.Wo164.cs", "Wo164OnEngineLine"),
            ("GameBridge.Wo160.cs", "W160OnEngineLine"), ("Wo160.cs", "PlannerErrorNpc"),
            ("GameBridge.Wo170.cs", "Wo170OnEngineLine"),
            ("GameBridge.Wo172.cs", "Wo172OnEngineLine"), ("Wo172.cs", "ApseScreen"),
            ("GameBridge.Wo144.cs", "Wo144OnEngineLine"), ("GameBridge.Wo144.cs", "Wo144OnEngineLineMore"),
            ("GameBridge.Wo183.cs", "Wo183OnEngineLine"),
            ("GameBridge.Wo184.cs", "Wo184OnEngineLine"), ("GameBridge.Wo184.cs", "Wo184OnQuestLine"), ("Wo184.cs", "Parse"), ("Wo184.cs", "HasDude"),
            ("GameBridge.Wo189.cs", "Wo189OnEngineLine"), ("Wo189.cs", "ParseProfileLine"), ("Wo189.cs", "TryParseInitialized"),
        };
        var missing = new List<string>();
        int checkedCount = 0;
        foreach (var (file, method) in handlers)
        {
            string path = Path.Combine(dir, file);
            Assert.True(File.Exists(path), $"handler file {file} is missing: the class test's list is out of date");
            string? body = MethodBody(All(file), method);
            Assert.True(body is not null, $"handler {method} not found in {file}: the class test's list is out of date");
            foreach (string lit in Literals(body!).Where(l => l.Length >= 3).Distinct())
            {
                checkedCount++;
                if (NotForwardedOnPurpose.Any(x => x.Literal == lit)) continue;
                // a literal tested with StartsWith must pass as the line's start; any other as a part of a line
                bool passes = LogTailGameTransport.Wo144Passes(lit) || LogTailGameTransport.Wo144Passes("xx" + lit + "yy") || LogTailGameTransport.QuestLinePasses(lit)
                              || LogTailGameTransport.Wo144Prefixes.Concat(LogTailGameTransport.Wo144Contains).Any(e => e.Contains(lit, StringComparison.Ordinal));   // a fragment of a forwarded phrase
                if (!passes) missing.Add($"{file}:{method} matches \"{lit}\" but the forward lists would not pass such a line");
            }
        }
        Assert.True(checkedCount > 30, $"the class test found only {checkedCount} literals -- its reader is broken");
        Assert.True(missing.Count == 0, "engine-line literals that never reach their handler:\n" + string.Join("\n", missing));
    }

    // ---------------------------------------------------------------- 3: the pending save and the one gate
    // WO-184 (0.60.0) replaced WO-183's gate (a conversation clock that combat shouts started and nothing ended silently stopped every
    // later snapshot): Wo184Tests pins the two hard blocks, the engine's own pending state, the clock's ends, stuck, leave and health.

    // ---------------------------------------------------------------- 1: the landing

    [Fact]
    public void The_landing_rings_are_3_6_10_then_15_and_25()
    {
        Assert.Equal([3f, 6f, 10f, 15f, 25f], Wo183Rules.LandingRingsM);
        var pts = Wo183Rules.RingPoints(100f, 200f, 10f);
        Assert.Equal(8, pts.Length);
        Assert.Equal(110f, pts[0].X, 3);
        Assert.Equal(200f, pts[0].Y, 3);
        foreach (var (x, y) in pts) Assert.Equal(10.0, Math.Sqrt((x - 100.0) * (x - 100.0) + (y - 200.0) * (y - 200.0)), 2);
    }

    [Fact]
    public void Only_a_public_spot_is_a_landing()
    {
        Assert.True(Wo183Rules.IsPublicSpot("public"));
        Assert.False(Wo183Rules.IsPublicSpot("private"));
        Assert.False(Wo183Rules.IsPublicSpot("open"));      // a shop opened by its keeper is still private ground
        Assert.False(Wo183Rules.IsPublicSpot("unknown"));
        Assert.False(Wo183Rules.IsPublicSpot(null));
    }

    [Fact]
    public void The_first_public_ring_spot_in_order_is_taken_and_none_means_no_move()
    {
        var inside = new[] { "private", "private", "private", "private", "private", "private", "private", "private" };
        var mixed = new[] { "private", "private", "public", "private", "public", "private", "private", "private" };
        Assert.Equal((1, 2), Wo183Rules.FirstPublic([inside, mixed, mixed]));   // ring 6 m, the third direction
        Assert.Equal((-1, -1), Wo183Rules.FirstPublic([inside, inside, inside, inside, inside]));   // the triggerer stands in a private building
        Assert.Equal((-1, -1), Wo183Rules.FirstPublic([new[] { "unknown", "unknown" }]));
        Assert.Equal("Couldn't bring you to your partner, they're somewhere private.", Wo183Rules.NoPublicSpotText);
    }

    [Fact]
    public void A_held_bring_along_waits_at_most_thirty_seconds()
    {
        Assert.Equal(30_000, Wo183Rules.BringHeldMaxMs);
    }

    [Fact]
    public void A_punishment_name_is_a_crime_scene()
    {
        Assert.True(Wo183Rules.IsCrimeName("crime_secondArrestFader"));
        Assert.True(Wo183Rules.IsCrimeName("crime_punishment_secondArrestFader"));
        Assert.False(Wo183Rules.IsCrimeName("m03_trosky_journey"));
        Assert.False(Wo183Rules.IsCrimeName(null));
    }

    // ---------------------------------------------------------------- 4b: the debounce and the sweep

    [Fact]
    public void Combat_zero_must_hold_one_and_a_half_seconds_before_a_copy_is_released()
    {
        var l = new Wo183Rules.LeftCombat();
        Assert.False(l.Report("tvez_man_15", false, 10_000));      // the first combat=0: the clock starts
        Assert.False(l.Report("tvez_man_15", false, 11_000));
        Assert.False(l.Report("tvez_man_15", false, 11_499));
        Assert.True(l.Report("tvez_man_15", false, 11_500));       // 1.5 s of combat=0: a real leave
    }

    [Fact]
    public void A_retarget_is_not_a_leave_a_combat_report_resets_the_clock()
    {
        var l = new Wo183Rules.LeftCombat();
        Assert.False(l.Report("tvez_man_15", false, 10_000));      // the sender's gap between two targets
        Assert.False(l.Report("tvez_man_15", true, 10_300));       // target=Host: back in combat
        Assert.False(l.Report("tvez_man_15", false, 12_000));      // a later gap starts a NEW clock, not the old one
        Assert.False(l.Report("tvez_man_15", false, 13_000));
        Assert.True(l.Report("tvez_man_15", false, 13_500));
    }

    [Fact]
    public void The_debounce_is_per_npc()
    {
        var l = new Wo183Rules.LeftCombat();
        l.Report("a", false, 0);
        Assert.False(l.Report("b", false, 1_400));
        Assert.True(l.Report("a", false, 1_600));
        Assert.False(l.Report("b", false, 1_600));
    }

    [Fact]
    public void The_sweep_never_runs_on_a_copy_that_is_engaged_or_in_combat()
    {
        Assert.False(Wo183Rules.SweepAllowed(copyEngaged: true, copyInCombat: false));
        Assert.False(Wo183Rules.SweepAllowed(copyEngaged: false, copyInCombat: true));
        Assert.True(Wo183Rules.SweepAllowed(copyEngaged: false, copyInCombat: false));
    }
}
