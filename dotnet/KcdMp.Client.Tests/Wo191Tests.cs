// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-191: the hotfix's rules (Wo191Rules) and the action channel's new receiver rules (ActionInbox), each item on its own.</summary>
public class Wo191Tests
{
    private static byte[] Down(byte sender, byte[] up)
    {
        var body = up.AsSpan(3).ToArray();
        var payload = new byte[1 + body.Length];
        payload[0] = sender;
        body.CopyTo(payload, 1);
        return payload;
    }

    private static byte[] Attack(ActionOutbox o) => o.Build(ActionKind.Attack, ActionPhase.Commit, new AttackPayload(0, 1, 1, 0).ToBytes());

    // ================================================================ 1: relaunch, reconnect, death, an older sender, wraparound

    [Fact]
    public void Relaunch_a_new_run_is_adopted_at_once_and_none_of_its_actions_is_stale()
    {
        var host = new ActionInbox();
        var run1 = new ActionOutbox(runId: 11);
        run1.BumpEpoch();                                       // connect: 1.1.11
        for (int i = 0; i < 16; i++) Assert.NotNull(host.Accept(Down(1, Attack(run1)), out _));
        var run2 = new ActionOutbox(runId: 12);                 // the joiner restarts the game: a new agent run
        run2.BumpEpoch();                                       // 1.1.12, seq back at 1
        for (int i = 0; i < 16; i++)
        {
            Assert.NotNull(host.Accept(Down(1, Attack(run2)), out var r));
            Assert.Equal(ActionReject.None, r);
        }
        Assert.Equal(0, host.Stale);
        Assert.Equal(1, host.RunsAdopted);
    }

    [Fact]
    public void Relaunch_the_old_receiver_rule_is_what_the_field_saw_16_stale_then_accepted()
    {
        // the 0.60.5 sender: every run 1.1.0 -- same gen, so the old held sequence stood (16 of the field's attacks refused)
        var host = new ActionInbox();
        var run1 = new ActionOutbox(runId: 0); run1.BumpEpoch();
        for (int i = 0; i < 16; i++) host.Accept(Down(1, Attack(run1)), out _);
        var run2 = new ActionOutbox(runId: 0); run2.BumpEpoch();
        int stale = 0, ok = 0;
        for (int i = 0; i < 21; i++) { if (host.Accept(Down(1, Attack(run2)), out _) is null) stale++; else ok++; }
        Assert.Equal(16, stale);
        Assert.Equal(5, ok);
    }

    [Fact]
    public void Relaunch_an_older_sender_is_fixed_by_the_receivers_forget_alone()
    {
        var host = new ActionInbox();
        var run1 = new ActionOutbox(runId: 0); run1.BumpEpoch();
        for (int i = 0; i < 50; i++) host.Accept(Down(1, Attack(run1)), out _);
        Assert.True(host.Forget(1));                           // MP-PEERS ghost 1 disconnected
        Assert.False(host.Forget(1));                          // nothing left: the second forget is a no-op
        var run2 = new ActionOutbox(runId: 0); run2.BumpEpoch();
        for (int i = 0; i < 50; i++) Assert.NotNull(host.Accept(Down(1, Attack(run2)), out _));
        Assert.Equal(0, host.Stale);
        Assert.Equal(1, host.Forgotten);
    }

    [Fact]
    public void Reconnect_inside_one_run_moves_the_epoch_and_is_accepted()
    {
        var host = new ActionInbox();
        var o = new ActionOutbox(runId: 7); o.BumpEpoch();
        for (int i = 0; i < 5; i++) host.Accept(Down(1, Attack(o)), out _);
        o.BumpEpoch();                                          // the relay connection dropped and came back: 1.2.7
        Assert.NotNull(host.Accept(Down(1, Attack(o)), out var r));
        Assert.Equal(ActionReject.None, r);
        Assert.Equal(0, host.RunsAdopted);
    }

    [Fact]
    public void A_new_incarnation_after_death_is_accepted_and_the_old_bodys_straggler_expires()
    {
        var host = new ActionInbox();
        var o = new ActionOutbox(runId: 9); o.BumpEpoch();
        var before = new List<byte[]>();
        for (int i = 0; i < 5; i++) before.Add(Attack(o));
        host.Accept(Down(1, before[0]), out _);
        o.BumpIncarnation();                                    // died and came back: 2.1.9
        Assert.NotNull(host.Accept(Down(1, Attack(o)), out _));
        Assert.Null(host.Accept(Down(1, before[4]), out var r)); // a packet of the dead body, late
        Assert.Equal(ActionReject.Expired, r);
    }

    [Fact]
    public void Wraparound_of_the_sequence_and_of_the_epoch_is_not_stale()
    {
        Assert.True(ActionInbox.ByteIsNewer(0, 255));
        Assert.True(ActionInbox.ByteIsNewer(3, 250));
        Assert.False(ActionInbox.ByteIsNewer(250, 3));
        Assert.False(ActionInbox.ByteIsNewer(4, 4));
        var host = new ActionInbox();
        var o = new ActionOutbox(runId: 3);
        for (int i = 0; i < 255; i++) o.BumpEpoch();            // epoch 255
        host.Accept(Down(1, Attack(o)), out _);
        o.BumpEpoch();                                          // wraps to 0: still forward
        Assert.NotNull(host.Accept(Down(1, Attack(o)), out var r));
        Assert.Equal(ActionReject.None, r);
        // the sequence: 70,000 attacks pass 65,535 without a single refusal
        var h2 = new ActionInbox();
        var s = new ActionOutbox(runId: 4);
        int refused = 0;
        for (int i = 0; i < 70_000; i++) if (h2.Accept(Down(2, Attack(s)), out _) is null) refused++;
        Assert.Equal(0, refused);
    }

    [Fact]
    public void A_duplicate_is_still_refused_and_named_with_its_kind()
    {
        var host = new ActionInbox();
        var o = new ActionOutbox(runId: 5);
        var p = Attack(o);
        Assert.NotNull(host.Accept(Down(1, p), out _));
        Assert.Null(host.Accept(Down(1, p), out var r));
        Assert.Equal(ActionReject.StaleOrDuplicate, r);
        Assert.Equal(ActionKind.Attack, host.LastRejectKind);
        Assert.Contains("seq=1 held_seq=1", host.LastRejectDetail);
        Assert.Contains("rejected_kinds=Attack:1", host.SummaryLine());
    }

    [Fact]
    public void Every_run_gets_a_nonzero_id_and_runs_differ()
    {
        var rng = new Random(191);
        var ids = Enumerable.Range(0, 2000).Select(_ => Wo191Rules.NewRunId(rng)).ToList();
        Assert.DoesNotContain((byte)0, ids);
        Assert.True(ids.Distinct().Count() > 200);
        Assert.NotEqual((byte)0, new ActionOutbox().Gen.Revision);
    }

    [Fact]
    public void The_reject_line_is_once_per_kind_per_minute_with_the_count_between()
    {
        var log = new Wo191Rules.RejectLog();
        Assert.Equal((1L, 1L), log.Note(ActionKind.Attack, ActionReject.StaleOrDuplicate, 1_000));
        Assert.Null(log.Note(ActionKind.Attack, ActionReject.StaleOrDuplicate, 2_000));
        Assert.Null(log.Note(ActionKind.Attack, ActionReject.StaleOrDuplicate, 30_000));
        Assert.Equal((1L, 1L), log.Note(ActionKind.Jump, ActionReject.StaleOrDuplicate, 30_000));   // another kind: its own line
        Assert.Equal((4L, 3L), log.Note(ActionKind.Attack, ActionReject.StaleOrDuplicate, 61_001));   // 3 since the last line, this one included
        Assert.Equal(4, log.Total(ActionKind.Attack, ActionReject.StaleOrDuplicate));
    }

    // ================================================================ 2: the fight guard, per trigger

    [Theory]
    [InlineData("focus")]
    [InlineData("skip")]
    [InlineData("adaptive")]
    [InlineData("retry")]
    [InlineData("hostconv")]
    [InlineData("console")]
    public void No_trigger_sweeps_a_copy_that_fights_or_fought_in_the_last_10_s(string trigger)
    {
        // the guard does not look at the trigger: the agent asks the same question for each (GameBridge.Wo191SweepRefused)
        var g = new Wo191Rules.FightGuard();
        string npc = "copy_" + trigger;
        Assert.Equal("engaged", g.Blocks(npc, 1_000, engaged: true, combatMode: false, targetedByPlayer: false, targetsPlayer: false));
        Assert.Equal("combat-mode", g.Blocks(npc, 2_000, false, true, false, false));
        Assert.Equal("a-player-targets-it", g.Blocks(npc, 3_000, false, false, true, false));
        Assert.Equal("it-targets-a-player", g.Blocks(npc, 4_000, false, false, false, true));
        Assert.StartsWith("it-targets-a-player-", g.Blocks(npc, 13_999, false, false, false, false));
        Assert.Null(g.Blocks(npc, 14_001, false, false, false, false));
    }

    [Fact]
    public void A_skip_is_told_once_per_copy_per_fight()
    {
        var g = new Wo191Rules.FightGuard();
        g.Note("a", 1_000, "engaged");
        Assert.True(g.TellSkip("a"));
        g.Note("a", 5_000, "a-player-hit-it");
        Assert.False(g.TellSkip("a"));                         // the same fight
        g.Note("a", 20_000, "engaged");                        // after 10 s of quiet: a new fight
        Assert.True(g.TellSkip("a"));
        Assert.False(g.TellSkip("never-fought"));
    }

    // ================================================================ 3: the attacker's body

    [Fact]
    public void The_engines_target_line_names_the_npc_and_the_partner()
    {
        Assert.True(Wo191Rules.TryParseTargetLine("[ttac_dog_1] Set target: kcd2mp_1", out var n, out var p));
        Assert.Equal("ttac_dog_1", n); Assert.Equal(1, p);
        Assert.True(Wo191Rules.TryParseTargetLine("[tvez_lovciPokladu_man_2] Set target: kcd2mp_12", out n, out p));
        Assert.Equal(12, p);
        Assert.False(Wo191Rules.TryParseTargetLine("[ttac_dog_1] Model: Set target: kcd2mp_1", out _, out _));   // the twin line
        Assert.False(Wo191Rules.TryParseTargetLine("[ttac_dog_1] Set target: Dude", out _, out _));             // the host's own player: no body missing
        Assert.False(Wo191Rules.TryParseTargetLine("[ttac dog] Set target: kcd2mp_1", out _, out _));
        Assert.False(Wo191Rules.TryParseTargetLine("Skirmish event: TargetChanged on ttac_dog_1 (target kcd2mp_1)", out _, out _));
        Assert.True(LogTailGameTransport.Wo144Passes("[ttac_dog_1] Set target: kcd2mp_1"));
    }

    [Fact]
    public void The_joiners_ask_names_the_attacker_and_an_older_host_still_reads_a_reason()
    {
        var b = Wo191Rules.AttackerResyncPayload("ttac_dog_1");
        Assert.Equal(NpcResyncReason.Attacker, b[0]);
        Assert.Equal("attacker", NpcResyncReason.Name(b[0]));
        Assert.Equal("ttac_dog_1", Wo191Rules.AttackerOf(b));
        Assert.Null(Wo191Rules.AttackerOf([NpcResyncReason.NewPeer]));
        Assert.Null(Wo191Rules.AttackerOf([NpcResyncReason.Attacker]));
        Assert.Null(Wo191Rules.AttackerOf([NpcResyncReason.Attacker, (byte)'a', (byte)' ', (byte)'b']));
        // it crosses the channel whole (the payload limit)
        var o = new ActionOutbox(runId: 1);
        var a = new ActionInbox().Accept(Down(1, o.Build(ActionKind.NpcResync, ActionPhase.Commit, b)), out _);
        Assert.Equal("ttac_dog_1", Wo191Rules.AttackerOf(a!.Value.Payload));
    }

    [Fact]
    public void The_joiner_asks_once_per_2_s_per_attacker_and_only_without_a_body()
    {
        Assert.True(Wo191Rules.AskBind(haveBody: false, on: true, lastAskMs: 0, nowMs: 5_000));
        Assert.False(Wo191Rules.AskBind(false, true, 5_000, 6_999));
        Assert.True(Wo191Rules.AskBind(false, true, 5_000, 7_000));
        Assert.False(Wo191Rules.AskBind(true, true, 0, 5_000));
        Assert.False(Wo191Rules.AskBind(false, false, 0, 5_000));
    }

    // ================================================================ 4: the fader

    [Fact]
    public void A_fader_with_any_content_is_left_alone()
    {
        Assert.Null(Wo191Rules.FaderContent("-", 0, false, false, false));
        Assert.Null(Wo191Rules.FaderContent("-", Wo138Codec.ReasonClock | Wo138Codec.ReasonFrozen, false, false, false));   // the host's clock, the meter: not content
        Assert.StartsWith("scene ", Wo191Rules.FaderContent("Dialog:x", 0, false, false, false));
        Assert.StartsWith("paused: ", Wo191Rules.FaderContent("-", Wo138Codec.ReasonLoad, false, false, false));
        Assert.StartsWith("paused: ", Wo191Rules.FaderContent("-", Wo138Codec.ReasonSkipTime, false, false, false));
        Assert.StartsWith("paused: ", Wo191Rules.FaderContent("-", Wo138Codec.ReasonDialogue, false, false, false));
        Assert.StartsWith("paused: ", Wo191Rules.FaderContent("-", Wo138Codec.ReasonInventory, false, false, false));
        Assert.Equal("fast travel", Wo191Rules.FaderContent("-", 0, true, false, false));
        Assert.NotNull(Wo191Rules.FaderContent("-", 0, false, true, false));
        Assert.NotNull(Wo191Rules.FaderContent("-", 0, false, false, true));
    }

    [Fact]
    public void The_waiting_conversation_is_the_fields_second_shop_talk()
    {
        var c = new Wo191Rules.PendingConversation();
        // the first talk: started, ran (a twin, a command), ended
        c.OnLine("New dialogue 'Barbora.open_world.shop.nakupovani_z_dialogu_muz (normal - Id: 164) (Actor::RequestDialog)' is starting. Params: souls = 'Ex: Dude; Ex: ttac_man_4' forced = false, forced decision = none", 1);
        Assert.Equal(164, c.Waiting);
        c.OnLine("[ID: 164] DialogCommand-Anim: dialogId=164, who=OBCHODNIK_ZDRAVI_JINDRU_NEBO_BOHUTU, anim=ADLG_Gesture", 2);
        Assert.Equal(-1, c.Waiting);
        c.OnLine("[ID: 164] Dialog ending [Ex0: Dude Ex1: ttac_man_4 state: CLEANUP flags: 2114321]", 3);
        Assert.Equal(-1, c.Id);
        // the second: never past WAITING_FOR_TWINS (the 20 s black)
        c.OnLine("New dialogue 'Barbora.open_world.shop.nakupovani_z_dialogu_muz (normal - Id: 175) (Actor::RequestDialog)' is starting. Params: souls = 'Ex: Dude; Ex: ttac_man_4' forced = false, forced decision = none", 4);
        c.OnLine("[ID: 164] DialogCommand-Anim: dialogId=164, who=X, anim=Y", 5);   // another dialogue's command: not this one
        Assert.Equal(175, c.Waiting);
        Assert.Contains("id=175", Wo191Rules.HolderText(c.Waiting));
        c.OnLine("[ID: 175] Dialog interrupted. [Ex0: Dude Ex1: ttac_man_4 state: WAITING_FOR_TWINS flags: 2107913]", 6);
        Assert.Equal(-1, c.Waiting);
        // an NPC's own monologue is not this player's
        c.OnLine("New dialogue 'Barbora.open_world.spanek_npc.npc_se_probudilo_ze_spani_ (ingame monolog - Id: 166) (AI::DoMonologue)' is starting. Params: souls = 'Ex: ttac_man_4 - meta override: NPC_SI_JDE_LEHNOUT' forced = true", 7);
        Assert.Equal(-1, c.Waiting);
    }

    [Fact]
    public void The_switches_ship_on()
    {
        Assert.True(Wo191Rules.DefaultActionForget && Wo191Rules.DefaultFightGuard && Wo191Rules.DefaultAttackerBind && Wo191Rules.DefaultFaderBound);
    }
}
