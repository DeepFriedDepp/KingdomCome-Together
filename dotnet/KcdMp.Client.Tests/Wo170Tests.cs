// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-170 (docs/WO-170-findings.md): 0.48.6's agent rules -- F1 (why an NPC took a partner's figure on; the switch), F2 (the inbound Attack
/// count), F4 (the interactive scenes the engine runs). The Lua halves are tools/Test-WO170Synthetic.lua.
/// </summary>
public class Wo170Tests
{
    // The field's line (0.48.5, the host, 16:18:55): tzel_adam takes the joiner's figure on.
    private const string FieldShout =
        "New dialogue 'Barbora.open_world.boj_a_zraneni.hekani_pri_utocich (ingame - Id: 1786) (CombatShout)' is starting. Params: souls = " +
        "'Ex: tzel_adam - meta override: COMBAT_ACTOR_SCREAM_ATTACK; Nx: kcd2mp_2 - meta override: COMBAT_SHOUT_OPPONENT' flags: 1";

    [Fact]
    public void A_combat_shout_naming_a_figure_is_an_npc_taking_it_on()
    {
        var a = Wo170Rules.AggroAgainstFigure(FieldShout);
        Assert.NotNull(a);
        Assert.Equal("tzel_adam", a!.Value.Npc);
        Assert.Equal(2, a.Value.Ghost);
        Assert.Equal("COMBAT_ACTOR_SCREAM_ATTACK", a.Value.NpcMeta);
        Assert.Equal("COMBAT_SHOUT_OPPONENT", a.Value.FigureMeta);
        Assert.Equal("hekani_pri_utocich", a.Value.Dialogue);
        Assert.Equal("COMBAT_ACTOR_SCREAM_ATTACK", Wo170Rules.AggroReason(a.Value.NpcMeta, a.Value.FigureMeta));
        Assert.Equal("COMBAT_SHOUT_OPPONENT", Wo170Rules.AggroReason("", a.Value.FigureMeta));
        Assert.Equal("unknown", Wo170Rules.AggroReason("", ""));
    }

    [Fact]
    public void Other_barks_are_not_aggression_but_are_remembered()
    {
        const string monolog = "New dialogue 'Barbora.open_world.crime_reaction_barks.reakce_na_zvuk.npc_reaguje_na_zvuk__univerzal__zena (ingame monolog - Id: 1764) (AI::DoMonologue)' is starting. Params: souls = 'Ex: tzel_woman_11 - meta override: NPC_REAGUJE_NA_ZVUK' flags: 0";
        Assert.Null(Wo170Rules.AggroAgainstFigure(monolog));
        var b = Wo170Rules.ParseBark(monolog);
        Assert.NotNull(b);
        Assert.Equal("npc_reaguje_na_zvuk__univerzal__zena", b!.Value.Dialogue);
        Assert.Equal("AI::DoMonologue", b.Value.Kind);
        Assert.Equal("tzel_woman_11", b.Value.Speaker);
        Assert.Null(b.Value.Other);
        // the figure shouting at an NPC is not an NPC's aggression
        Assert.Null(Wo170Rules.AggroAgainstFigure(FieldShout.Replace("Ex: tzel_adam", "Ex: kcd2mp_2").Replace("Nx: kcd2mp_2", "Nx: tzel_adam")));
        // a shout between two NPCs, a line that is no bark, a malformed one
        Assert.Null(Wo170Rules.AggroAgainstFigure(FieldShout.Replace("kcd2mp_2", "tzel_bretislav")));
        Assert.Null(Wo170Rules.AggroAgainstFigure("Running dialogue 'x' [Ex0: tzel_adam Nx1: kcd2mp_2 state: ANIMATING] (CombatShout)"));
        Assert.Null(Wo170Rules.ParseBark("New dialogue 'x"));
        Assert.Null(Wo170Rules.ParseBark("New dialogue 'x (ingame) (CombatShout)' is starting. Params: nothing"));
    }

    [Theory]
    [InlineData("kcd2mp_2", 2)]
    [InlineData("kcd2mp_17", 17)]
    [InlineData("kcd2mp_", null)]
    [InlineData("kcd2mp_x", null)]
    [InlineData("tzel_adam", null)]
    [InlineData("kcd2mp_1234", null)]
    public void A_figure_is_named_kcd2mp_and_its_ghost_id(string name, int? ghost) => Assert.Equal(ghost, Wo170Rules.FigureGhost(name));

    [Fact]
    public void Scenes_running_is_enqueues_minus_ends_never_below_zero()
    {
        Assert.Equal(0, Wo170Rules.ScenesRunning(0, 0));
        Assert.Equal(2, Wo170Rules.ScenesRunning(3, 1));
        Assert.Equal(0, Wo170Rules.ScenesRunning(1, 3));
    }

    [Fact]
    public void The_figure_guard_and_the_scene_hold_ship_on()
    {
        Assert.True(Wo170Rules.DefaultFigureGuard);
        Assert.True(Wo170Rules.DefaultSceneHold);
    }

    [Fact]
    public void The_config_line_carries_the_figure_guard()
    {
        var kv = Wo166Rules.ParseCfg("figure_guard=off copy_strikes=on figure_guard=maybe bogus=1").ToList();
        Assert.Contains(("figure_guard", "off"), kv);
        Assert.Contains(("copy_strikes", "on"), kv);
        Assert.Equal(2, kv.Count);
    }

    // ---------------------------------------------------------------- F2: the inbound Attack count

    [Fact]
    public void Ten_inbound_attacks_are_ten_accepted_attacks_in_the_summary()
    {
        var outbox = new ActionOutbox();
        var inbox = new ActionInbox();
        for (int i = 0; i < 10; i++)
        {
            var up = outbox.Build(ActionKind.Attack, ActionPhase.Commit, new AttackPayload(1, 2, 1, AttackPayload.FlagPrepared).ToBytes());
            var body = up.AsSpan(3).ToArray();
            var payload = new byte[1 + body.Length];
            payload[0] = 2;
            body.CopyTo(payload, 1);
            Assert.NotNull(inbox.Accept(payload, out _));
        }
        Assert.Equal(10, inbox.AcceptedByKind[ActionKind.Attack]);
        string line = inbox.SummaryLine();
        Assert.Contains("accepted=10", line);
        Assert.Contains("kinds=Attack:10", line);
    }

    [Fact]
    public void An_empty_inbox_says_no_kinds()
    {
        Assert.Contains(" kinds=- ", new ActionInbox().SummaryLine());   // WO-191: rejected_kinds= and the forget counts follow it
    }
}
