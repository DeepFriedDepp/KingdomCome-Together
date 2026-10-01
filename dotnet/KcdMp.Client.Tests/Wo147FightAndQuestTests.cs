// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;
using Act = KcdMp.Client.LeashLogic.Act;
using D = KcdMp.Client.Wo147Rules.Destruction;
using E = KcdMp.Client.Wo132Rules.EngageVerdict;
using Hold = KcdMp.Client.LeashLogic.Hold;
using V = KcdMp.Client.QuestValueIndex.Value;
using W = KcdMp.Client.Wo147Rules.EngageWant;

namespace KcdMp.Client.Tests;

/// <summary>WO-147: the joiner's fight reaches the host, the hostile copies are targetable, and quest safety (docs/WO-147-findings.md).</summary>
public class Wo147FightAndQuestTests
{
    // ================================================================ the hit forward (humans and animals)

    [Theory]
    [InlineData(9.35f, 0f, false)]    // a sword blow on a bandit
    [InlineData(0f, 21.0f, false)]    // a blocked blow: stamina only (the field sent none of these)
    [InlineData(0.3f, 12.0f, false)]  // a glancing blow that mostly cost stamina
    [InlineData(0.04f, 0.2f, true)]   // contact-frame noise
    [InlineData(0f, 0f, true)]
    public void A_blow_that_cost_only_stamina_is_no_noise(float hp, float st, bool nothing) =>
        Assert.Equal(nothing, Wo147Rules.HitCarriesNothing(hp, st));

    [Fact]
    public void On_a_joiner_only_its_own_blows_go_once_the_hook_says_whose_they_are()
    {
        // the host avatar's blow on a hidden local copy (the field) -- not this player's: dropped
        Assert.True(Wo147Rules.DropNotOwnBlow(joinerActive: true, byPlayer: false, hookArmed: true));
        // this player's own blow, on a bandit's copy or a wolf's: sent
        Assert.False(Wo147Rules.DropNotOwnBlow(true, true, true));
        // no armed hook: nothing says whose it is, so the old rule stands (every drop goes)
        Assert.False(Wo147Rules.DropNotOwnBlow(true, false, false));
        // the host, or a solo game: never filtered here
        Assert.False(Wo147Rules.DropNotOwnBlow(false, false, true));
    }

    [Fact]
    public void The_joiners_gate_forwards_a_blow_on_a_standing_npc_whose_stream_is_a_heartbeat()
    {
        // the field: a standing archer, 0.00 m from its sample, dropped as stale at 3,538 ms
        var archer = new Wo131HitCheck(Bound: true, AgeMs: 3538, DistM: 0.0f, Flags: 0, Hp: 80f, Guarded: true);
        Assert.Equal(Wo131Rules.HitVerdict.Forward, Wo131Rules.GateJoinerHit(archer, 12f).Verdict);
        // a stamina-only blow passes with its zero health: the host applies the stamina
        var (v, h) = Wo131Rules.GateJoinerHit(archer, 0f);
        Assert.Equal(Wo131Rules.HitVerdict.Forward, v);
        Assert.Equal(0f, h);
        Assert.Equal(Wo131Rules.HitVerdict.DropStale, Wo131Rules.GateJoinerHit(archer with { AgeMs = 6100 }, 12f).Verdict);
    }

    // ================================================================ the avatar as the attacker: the NPC fights back at the joiner

    private static NpcCombatEvent Ev(bool combat, NpcCombatTarget target, byte ghost) =>
        new(1, new BodyState2(0, 0, combat ? BodyState2Bits.CombatMode : BodyState2Bits.None, 0, 0, 0, 0, 0, 0), target, ghost, "prepadeni_bandit_1");

    [Fact]
    public void An_npc_fighting_this_joiners_avatar_is_engaged_although_it_reads_combat_0()
    {
        // the field: every NpcCombat with target=Avatar had combat=0 -- the joiner never got combat mode against it
        Assert.Equal(E.Engage, Wo132Rules.JudgeEngage(Ev(false, NpcCombatTarget.Avatar, 1), true, true, true, false, myGhost: 1));
        // another player's avatar: not this joiner's fight
        Assert.Equal(E.Ignore, Wo132Rules.JudgeEngage(Ev(false, NpcCombatTarget.Avatar, 2), true, true, true, false, myGhost: 1));
        // a copy that is not the host's (not guarded, not bound) is never engaged
        Assert.Equal(E.Ignore, Wo132Rules.JudgeEngage(Ev(false, NpcCombatTarget.Avatar, 1), true, false, true, false, myGhost: 1));
        Assert.Equal(E.Ignore, Wo132Rules.JudgeEngage(Ev(false, NpcCombatTarget.Avatar, 1), true, true, false, false, myGhost: 1));
        // the fight moved to the host's player and the NPC left combat: released
        Assert.Equal(E.Release, Wo132Rules.JudgeEngage(Ev(false, NpcCombatTarget.Host, 0xFF), true, true, true, true, myGhost: 1));
        // unknown own ghost id (0xFF): the old rule
        Assert.Equal(E.Ignore, Wo132Rules.JudgeEngage(Ev(false, NpcCombatTarget.Avatar, 0xFF), true, true, true, false));
    }

    // ================================================================ targetable hostile copies

    [Fact]
    public void The_mods_hostile_list_parses_and_bad_entries_are_skipped()
    {
        var h = Wo147Rules.ParseHostiles("drawn=1 on=1 n=3 list=prepadeni_bandit_1:-1.00:4.2:1;wolf_pack_2:-1.00:9.9:1;bad name:0.1:1:1;tzel_man_3:0.40:3.0:1");
        Assert.NotNull(h);
        Assert.True(h!.Value.Drawn);
        Assert.True(h.Value.On);
        Assert.Equal(["prepadeni_bandit_1", "wolf_pack_2", "tzel_man_3"], h.Value.Copies.Select(c => c.Name));
        Assert.Equal(-1f, h.Value.Copies[1].Rel);
        Assert.Equal(9.9f, h.Value.Copies[1].DistM, 3);
        Assert.Null(Wo147Rules.ParseHostiles("n=0 list="));
        var none = Wo147Rules.ParseHostiles("drawn=0 on=1 n=0 list=");
        Assert.NotNull(none);
        Assert.Empty(none!.Value.Copies);
    }

    [Fact]
    public void An_enemy_copy_is_targetable_with_the_weapon_out_whether_or_not_the_host_fights_it()
    {
        const float relMax = -0.1f;
        // a bandit (or a wolf: the mod reads an encounter animal as -1) 4 m away, the weapon out
        Assert.Equal(W.Engage, Wo147Rules.JudgeHostileEngage(-1f, relMax, alive: true, distM: 4f, drawn: true, engaged: false, sheathedLong: false));
        // not until the weapon is out, and not past 12 m
        Assert.Equal(W.Nothing, Wo147Rules.JudgeHostileEngage(-1f, relMax, true, 4f, false, false, false));
        Assert.Equal(W.Nothing, Wo147Rules.JudgeHostileEngage(-1f, relMax, true, 12.5f, true, false, false));
        // an engaged one is kept to 15 m (the DLL's own limit), then let go
        Assert.Equal(W.Engage, Wo147Rules.JudgeHostileEngage(-1f, relMax, true, 14f, true, true, false));
        Assert.Equal(W.Release, Wo147Rules.JudgeHostileEngage(-1f, relMax, true, 15.5f, true, true, false));
        // the weapon away 5 s, or the copy down or dead: let go
        Assert.Equal(W.Release, Wo147Rules.JudgeHostileEngage(-1f, relMax, true, 4f, false, true, true));
        Assert.Equal(W.Release, Wo147Rules.JudgeHostileEngage(-1f, relMax, false, 4f, true, true, false));
        Assert.Equal(W.Nothing, Wo147Rules.JudgeHostileEngage(-1f, relMax, false, 4f, true, false, false));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.4f)]
    [InlineData(-0.05f)]
    [InlineData(float.NaN)]
    public void A_friendly_copy_stays_friendly(float rel) =>
        Assert.Equal(W.Friendly, Wo147Rules.JudgeHostileEngage(rel, -0.1f, true, 2f, true, false, false));

    [Fact]
    public void The_state_held_on_a_self_engaged_copy_is_combat_mode()
    {
        var s = Wo147Rules.LocalEngageState();
        Assert.True(s.CombatMode);
    }

    // ================================================================ the leash: an owed fast-travel pull lapses

    [Fact]
    public void An_owed_fast_travel_pull_lapses_after_two_minutes_behind_a_load()
    {
        var l = new LeashLogic();
        l.Tick(0, 300, Hold.None);
        l.NoteHostFastTravel();
        long t = 1000;
        for (; t < LeashLogic.FastTravelOwedMaxMs; t += 1000)
            Assert.DoesNotContain(l.Tick(t, 300, Hold.JoinerLoading), a => a.Kind is Act.Pull or Act.FastTravelExpired);
        var acts = l.Tick(t + 1000, 300, Hold.JoinerLoading);
        Assert.Contains(acts, a => a.Kind == Act.FastTravelExpired);
        Assert.False(l.FastTravelPending);
        // the field's case: the partner walked 250 m off meanwhile -- no pull at all now (under the 650 m line)
        Assert.DoesNotContain(l.Tick(t + 2000, 250, Hold.None), a => a.Kind == Act.Pull);
    }

    [Fact]
    public void A_new_fast_travel_starts_the_owed_clock_over()
    {
        var l = new LeashLogic();
        l.NoteHostFastTravel();
        l.Tick(0, 900, Hold.JoinerLoading);
        l.Tick(100_000, 900, Hold.JoinerLoading);
        l.NoteHostFastTravel();
        Assert.DoesNotContain(l.Tick(101_000, 900, Hold.JoinerLoading), a => a.Kind == Act.FastTravelExpired);
        Assert.Contains(l.Tick(102_000, 900, Hold.None), a => a.Kind == Act.Pull && Protocol.LeashReasonBase(a.Reason) == Protocol.LeashReasonFastTravel);
    }

    // ================================================================ quest safety: which requests are destructive

    private static QuestValueIndex Index()
    {
        var idx = new QuestValueIndex();
        idx.AddXml("""
            <Quest>
              <Type TypeName="Challenge"><StateTypeEnumeration Name="None"/><StateTypeEnumeration Name="Active" ObjectiveValueType="Started"/><StateTypeEnumeration Name="Won" ObjectiveValueType="Completed"/><StateTypeEnumeration Name="Aborted" /></Type>
              <Type TypeName="ProgressHiddenDone"><StateTypeEnumeration Name="None"/><StateTypeEnumeration Name="Started" ObjectiveValueType="Started"/><StateTypeEnumeration Name="Done"/></Type>
              <Type TypeName="ImportantNpcIsDead"><StateTypeEnumeration Name="NpcIsAlive"/><StateTypeEnumeration Name="NpcIsDead"/></Type>
              <Type TypeName="EscortObjective"><StateTypeEnumeration Name="None"/><StateTypeEnumeration Name="Started" ObjectiveValueType="Started"/><StateTypeEnumeration Name="Canceled" ObjectiveValueType="Canceled"/><StateTypeEnumeration Name="Completed" ObjectiveValueType="Completed"/></Type>
              <Type TypeName="CarryingBags"><StateTypeEnumeration Name="None"/><StateTypeEnumeration Name="Started" ObjectiveValueType="Started"/><StateTypeEnumeration Name="Cart"/><StateTypeEnumeration Name="Barn"/><StateTypeEnumeration Name="Done" ObjectiveValueType="Completed"/></Type>
              <Type TypeName="Knowledge"><StateTypeEnumeration Name="None"/><StateTypeEnumeration Name="PlayerFoundDeadBody"/><StateTypeEnumeration Name="SpokeWithBailiffAboutDeadBandit"/></Type>
            </Quest>
            """);
        return idx;
    }

    [Fact]
    public void The_quest_data_reader_keeps_every_value_and_its_objective_type()
    {
        var idx = Index();
        var c = Assert.Single(idx.Definitions("Challenge"));
        Assert.Equal([new V("None", "None"), new V("Active", "Started"), new V("Won", "Completed"), new V("Aborted", "None")], c);
        // the engine's own quest progress is built in, qualified or not
        Assert.Equal("Canceled", idx.Definitions("wh::questmodule::QuestProgress")[0][3].Objective);
        Assert.Single(idx.Definitions("some::ns::Challenge"));   // the unqualified name after the last ::
        Assert.Empty(idx.Definitions("NoSuchType"));
    }

    [Fact]
    public void The_fields_three_destructive_requests_are_destructive()
    {
        var idx = Index();
        // npcIsDead SetNpcIsDead 0->1 (the host's NPC was alive)
        Assert.Equal(D.NpcDeadOrDown, Wo147Rules.Destructive(idx, "ImportantNpcIsDead", "SetNpcIsDead", 0, 1).Kind);
        // defeatOpponent_objective Challenge.SetNone 1->0 (a running objective taken back)
        Assert.Equal(D.CancelsObjective, Wo147Rules.Destructive(idx, "Challenge", "SetNone", 1, 0).Kind);
        // QuestProgress.SetFailed 1->3 (the quest fails)
        Assert.Equal(D.FailsQuest, Wo147Rules.Destructive(idx, "wh::questmodule::QuestProgress", "SetFailed", 1, 3).Kind);
    }

    [Fact]
    public void Cancels_and_aborts_of_a_running_objective_are_destructive()
    {
        var idx = Index();
        Assert.Equal(D.CancelsObjective, Wo147Rules.Destructive(idx, "EscortObjective", "SetCanceled", 1, 2).Kind);   // ObjectiveValueType Canceled
        Assert.Equal(D.CancelsObjective, Wo147Rules.Destructive(idx, "Challenge", "SetAborted", 1, 3).Kind);         // a None-type value named for it
        // no type data: a name that fails or cancels
        Assert.Equal(D.CancelsObjective, Wo147Rules.Destructive(idx, "UnknownType", "SetFailed", 1, 2).Kind);
        Assert.Equal(D.FailsQuest, Wo147Rules.Destructive(null, "QuestProgress", "SetFailed", 1, 3).Kind);
    }

    [Fact]
    public void Progress_is_never_destructive()
    {
        var idx = Index();
        Assert.Equal(D.None, Wo147Rules.Destructive(idx, "Challenge", "SetActive", 0, 1).Kind);
        Assert.Equal(D.None, Wo147Rules.Destructive(idx, "Challenge", "SetWon", 1, 2).Kind);
        // SetDone on a None-type Done: a silent completion (9 such types in the game), not a withdrawal
        Assert.Equal(D.None, Wo147Rules.Destructive(idx, "ProgressHiddenDone", "SetDone", 1, 2).Kind);
        Assert.Equal(D.None, Wo147Rules.Destructive(idx, "CarryingBags", "SetCart", 1, 2).Kind);
        Assert.Equal(D.None, Wo147Rules.Destructive(idx, "CarryingBags", "SetBarn", 2, 3).Kind);
        // a None value that takes back nothing running (it was not started)
        Assert.Equal(D.None, Wo147Rules.Destructive(idx, "Challenge", "SetNone", 0, 0).Kind);
        // relative and data-driven ports are not Set<Value> ports
        Assert.Equal(D.None, Wo147Rules.Destructive(idx, "Counter", "Increment", 3, 4).Kind);
    }

    [Theory]
    [InlineData("SetPlayerFoundDeadBody")]            // a discovery
    [InlineData("SetSpokeWithBailiffAboutDeadBandit")] // a conversation's outcome
    [InlineData("SetNobodyDead")]
    [InlineData("SetFrancekIsNotUnconscious")]
    [InlineData("SetDeathTimer")]
    [InlineData("SetZvedniMrtvoluStart")]              // pick up the corpse
    public void Names_that_speak_of_death_without_marking_one_are_not_deaths(string port) =>
        Assert.NotEqual(D.NpcDeadOrDown, Wo147Rules.Destructive(Index(), "Knowledge", port, 0, 1).Kind);

    [Theory]
    [InlineData("SetSoldierDied")]
    [InlineData("SetMasterKnockedOut")]
    [InlineData("SetPlayerUnconscious")]
    [InlineData("SetVazounJeMrtvy")]
    [InlineData("SetNekdoUmrel")]
    [InlineData("SetbykZabit")]
    [InlineData("SetSomebodyDied")]
    [InlineData("SetAnyDiedNotInPub")]
    public void Deaths_and_knockouts_are_destructive_in_english_and_czech(string port) =>
        Assert.Equal(D.NpcDeadOrDown, Wo147Rules.Destructive(Index(), "Whatever", port, 0, 1).Kind);

    // ================================================================ corrections fire only a port that produces the host's value

    [Fact]
    public void A_correction_fires_the_port_named_for_the_hosts_value()
    {
        var idx = Index();
        // the field: "carryingBags SetCart 4->2 (the host had 3)" -- the host's LAST port fired, not one producing 3
        Assert.Equal("SetBarn", Wo147Rules.CorrectionPort(idx, "CarryingBags", 3, null));
        Assert.Equal("SetDone", Wo147Rules.CorrectionPort(idx, "CarryingBags", 4, null));
    }

    [Fact]
    public void Without_type_data_only_a_learned_port_for_exactly_that_value_is_fired()
    {
        var learned = new Dictionary<string, int> { ["SetStageTwo"] = 2, ["SetStageThree"] = 3 };
        Assert.Equal("SetStageThree", Wo147Rules.CorrectionPort(null, "Unknown", 3, learned));
        Assert.Null(Wo147Rules.CorrectionPort(null, "Unknown", 4, learned));   // none: the next join loads it exactly
        Assert.Null(Wo147Rules.CorrectionPort(null, "Unknown", 4, null));
    }

    [Fact]
    public void Two_definitions_that_disagree_on_the_value_name_fall_back_to_learning()
    {
        var idx = new QuestValueIndex();
        idx.Add("Twice", [new V("None", "None"), new V("A", "Started")]);
        idx.Add("Twice", [new V("None", "None"), new V("B", "Started")]);
        Assert.Null(Wo147Rules.CorrectionPort(idx, "Twice", 1, null));
        Assert.Equal("SetB", Wo147Rules.CorrectionPort(idx, "Twice", 1, new Dictionary<string, int> { ["SetB"] = 1 }));
        // out of range for the type: no port by the type
        Assert.Null(Wo147Rules.CorrectionPort(idx, "Twice", 7, null));
    }

    [Fact]
    public void The_hosts_verdict_on_a_destructive_request()
    {
        Assert.Equal(Wo147Rules.DestructiveVerdict.Already, Wo147Rules.JudgeDestructive(hostReachedIt: true, fromConversation: false));
        Assert.Equal(Wo147Rules.DestructiveVerdict.Already, Wo147Rules.JudgeDestructive(true, true));
        Assert.Equal(Wo147Rules.DestructiveVerdict.ApplyConversation, Wo147Rules.JudgeDestructive(false, true));
        Assert.Equal(Wo147Rules.DestructiveVerdict.Refuse, Wo147Rules.JudgeDestructive(false, false));
        Assert.Equal(32, Wo147Rules.FlagConversation);
    }

    // ================================================================ barks are no conversation

    [Theory]
    [InlineData("Ex: tzel_bretislav - meta override: COMBAT_ACTOR_SCREAM_ATTACK; Nx: Dude - meta override: COMBAT_SHOUT_OPPONENT", true)]
    [InlineData("Ex: Dude - meta override: HRAC_KUN_DOCHAZI_STAMINA", true)]
    [InlineData("Ex: prepadeni_bandit_1 - meta override: SKIRMISH_COMBATIDLE_SOURCE; Ex: Dude", true)]
    [InlineData("Ex: tzel_rowdy_3 - meta override: KOSTKAR_UNISEX; Ex: Dude", false)]            // the dice player: a real conversation
    [InlineData("Ex: Dude; Ex: ttkc_man_11 - meta override: SMLOUVANI, VYJEDNAVANI", false)]      // bargaining
    [InlineData("Ex: Dude; Ex: tzel_olbram", false)]
    [InlineData("Ex: x - meta override: ; Ex: Dude - meta override: COMBAT_SHOUT_OPPONENT", true)]
    public void A_combat_shout_or_the_players_own_bark_is_a_bark_and_nothing_else_is(string souls, bool bark)
    {
        Assert.Equal(bark, Wo137Rules.IsBarkAttempt(souls));
        Assert.True(Wo137Rules.TryParseQuestLine($"Attempting to start new dialogue (runtime id '7') with souls '{souls}'", out var q));
        Assert.Equal(bark, q.Bark);
    }
}
