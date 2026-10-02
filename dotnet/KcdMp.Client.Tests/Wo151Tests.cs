// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-151: the fight list's names, the new action kind, the hit reaction's row event.</summary>
public class Wo151Tests
{
    [Theory]
    [InlineData("tbuk_man_1", true)]
    [InlineData("zranenyLovci_wolf_2", true)]
    [InlineData("A", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("bad name", false)]
    [InlineData("quote\"inject", false)]
    [InlineData("comma,list", false)]
    public void Only_wire_names_reach_the_fight_list(string? name, bool ok)
    {
        Assert.Equal(ok, Wo151Rules.IsWireName(name));
    }

    [Fact]
    public void A_name_over_64_characters_is_refused()
    {
        Assert.True(Wo151Rules.IsWireName(new string('a', 64)));
        Assert.False(Wo151Rules.IsWireName(new string('a', 65)));
    }

    [Fact]
    public void A_copy_stays_in_a_fight_ten_seconds_after_the_last_blow()
    {
        Assert.Equal(10_000, Wo151Rules.FightHoldMs);
    }

    [Fact]
    public void Npc_hit_is_a_new_action_kind_after_npc_combat()
    {
        Assert.Equal(15, (int)ActionKind.NpcHit);
        Assert.Equal(14, (int)ActionKind.NpcCombat);
        Assert.Equal(13, (int)ActionKind.NpcAttack);
    }

    [Fact]
    public void A_hit_reaction_row_event_round_trips()
    {
        var row = Guid.Parse("bd1c48b1-11cb-3e65-a1d2-d469e186f838");   // live L4: CombatHitGen, r_noweapon...
        var ev = new RowEvent(4242u, 0, row, "w151_g1");
        Assert.True(RowEvent.TryFromBytes(ev.ToBytes(), out var back));
        Assert.Equal((4242u, row, "w151_g1"), (back.SenderMs, back.Row, back.Name));
    }
}

/// <summary>WO-151 3.9: a door on the wire -- its kinds, its payload, the Lua event's parse.</summary>
public class Wo151DoorTests
{
    [Fact]
    public void The_whistle_is_the_first_emote()
    {
        Assert.Equal(3, (int)ActionKind.Emote);
        Assert.Equal(1, EmoteId.Whistle);
        Assert.Equal("whistle", EmoteId.Name(EmoteId.Whistle));
    }

    [Fact]
    public void Door_state_and_ask_are_new_action_kinds_after_npc_hit()
    {
        Assert.Equal(16, (int)ActionKind.DoorState);
        Assert.Equal(17, (int)ActionKind.DoorAsk);
    }

    [Fact]
    public void A_door_event_round_trips()
    {
        var ev = new DoorEvent(77u, -1, DoorEvent.FlagLocked, 812.5f, -1450.25f, 31.75f, "AnimDoor_mill.back-2");
        Assert.True(DoorEvent.TryFromBytes(ev.ToBytes(), out var back));
        Assert.Equal(ev, back);
    }

    [Fact]
    public void The_longest_door_name_still_fits_one_action()
    {
        var ev = new DoorEvent(1u, 1, 0, 0, 0, 0, new string('d', DoorEvent.MaxNameLen));
        Assert.Equal(Protocol.ActionPayloadMaxLen, ev.ToBytes().Length);
        Assert.Throws<ArgumentException>(() => new DoorEvent(1u, 1, 0, 0, 0, 0, new string('d', DoorEvent.MaxNameLen + 1)).ToBytes());
    }

    [Theory]
    [InlineData("AnimDoor12", true)]
    [InlineData("door_inn.back-1", true)]
    [InlineData("", false)]
    [InlineData("two words", false)]
    [InlineData("quote\"inject", false)]
    [InlineData("paren)", false)]
    public void Only_level_names_are_door_names(string name, bool ok)
    {
        Assert.Equal(ok, DoorEvent.IsDoorName(name));
    }

    [Fact]
    public void A_malformed_door_payload_is_refused()
    {
        var good = new DoorEvent(5u, 1, 0, 1, 2, 3, "AnimDoor1").ToBytes();
        Assert.False(DoorEvent.TryFromBytes(good.AsSpan(0, DoorEvent.FixedLen), out _));       // no name
        var badDir = (byte[])good.Clone(); badDir[4] = 2;
        Assert.False(DoorEvent.TryFromBytes(badDir, out _));
        var nan = (byte[])good.Clone(); BitConverter.GetBytes(float.NaN).CopyTo(nan, 6);
        Assert.False(DoorEvent.TryFromBytes(nan, out _));
        var badName = (byte[])good.Clone(); badName[DoorEvent.FixedLen] = (byte)'"';
        Assert.False(DoorEvent.TryFromBytes(badName, out _));
    }

    [Fact]
    public void The_lua_door_event_parses()
    {
        var st = Wo151Rules.ParseDoorEvent("state AnimDoor7 -1 1 812.50 -1450.25 31.75", 9u);
        Assert.NotNull(st);
        Assert.False(st!.Value.Ask);
        Assert.Equal(new DoorEvent(9u, -1, DoorEvent.FlagLocked, 812.5f, -1450.25f, 31.75f, "AnimDoor7"), st.Value.Ev);
        var ask = Wo151Rules.ParseDoorEvent("ask AnimDoor7 1 0 1.00 2.00 3.00", 9u);
        Assert.True(ask!.Value.Ask);
        Assert.Equal((sbyte)1, ask.Value.Ev.Dir);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("open AnimDoor7 1 0 1 2 3")]
    [InlineData("state AnimDoor7 2 0 1 2 3")]
    [InlineData("state AnimDoor7 1 2 1 2 3")]
    [InlineData("state AnimDoor7 1 0 1 2")]
    [InlineData("state Anim\"Door 1 0 1 2 3")]
    [InlineData("state AnimDoor7 1 0 nan 2 3")]
    public void A_bad_lua_door_event_is_dropped(string? arg)
    {
        Assert.Null(Wo151Rules.ParseDoorEvent(arg, 1u));
    }
}

/// <summary>WO-151 3.1: the host's verdict with each State's history (the field's mis-handled steps).</summary>
public class Wo151CatchUpTests
{
    static Wo151Rules.CatchUpVerdict J(int host, int old, int nw, bool isBool, params int[] passed) =>
        Wo151Rules.Judge(true, host, old, nw, isBool, passed);

    [Fact]
    public void A_replayed_backward_step_is_already_not_applied()
    {
        // #28 deliverWater SetDeliverFlask 3->2: the host had done 1->2 and 2->3 (APPLIED in the field: the quest went back)
        Assert.Equal(Wo151Rules.CatchUpVerdict.AlreadyPassed, J(3, 3, 2, false, 1, 2));
        // #63 trackHorse SetActive 2->1: the host had finished tracking (reopened the search in the field)
        Assert.Equal(Wo151Rules.CatchUpVerdict.AlreadyPassed, J(2, 2, 1, false, 0, 1, 9));
    }

    [Fact]
    public void A_recount_of_a_counter_the_host_has_passed_is_already_not_refused()
    {
        // resolvedbanditscounter.state3: the joiner's own recount while the host was at 3 (12 refusals in the field)
        Assert.Equal(Wo151Rules.CatchUpVerdict.AlreadyPassed, J(3, 1, 0, false, 0, 1, 2));
        Assert.Equal(Wo151Rules.CatchUpVerdict.AlreadyPassed, J(3, 0, 1, false, 0, 1, 2));
        Assert.Equal(Wo151Rules.CatchUpVerdict.AlreadyPassed, J(3, 1, 2, false, 0, 1, 2));
    }

    [Fact]
    public void A_bool_may_go_back_and_forth()
    {
        // waitingForReactors 0->1->0: a later 0->1 is a new step
        Assert.Equal(Wo151Rules.CatchUpVerdict.Apply, J(0, 0, 1, true, 1));
    }

    [Fact]
    public void The_old_rules_stand_where_there_is_no_history()
    {
        Assert.Equal(Wo151Rules.CatchUpVerdict.Already, J(4, 3, 4, false));
        Assert.Equal(Wo151Rules.CatchUpVerdict.Apply, J(3, 3, 4, false));
        Assert.Equal(Wo151Rules.CatchUpVerdict.Refused, J(5, 3, 4, false));
        Assert.Equal(Wo151Rules.CatchUpVerdict.Apply, J(3, 3, 4, false, 1, 2));   // a step forward
        Assert.Equal(Wo151Rules.CatchUpVerdict.Refused, Wo151Rules.Judge(false, 3, 3, 4, false, null));
    }

    [Fact]
    public void A_catch_up_waits_for_the_first_part_less_than_its_longest()
    {
        Assert.True(Wo151Rules.CatchUpFirstPartMs < Wo151Rules.CatchUpMaxMs);
    }
}

/// <summary>WO-151 3.4: the scene guard's clock -- the engine's scene-player lines.</summary>
public class Wo151SceneStageTests
{
    [Theory]
    [InlineData("CutscenePlayer::OnCutsceneEnd called for Fader cutscene 'zranenyLovci_hideout' with holder 'zranenyLovci_c_hideout' from module 'brambora::Barbora'",
        "OnCutsceneEnd", "Fader", "zranenyLovci_hideout")]
    [InlineData("CutscenePlayer::ReleaseScene called for Ingame cutscene 'socky_3_tavern' with holder 'h' from module 'm'", "ReleaseScene", "Ingame", "socky_3_tavern")]
    [InlineData("CutscenePlayer::OnPositioningFinished called for Fader cutscene 'crime_secondArrestFader' with holder 'x' from module 'y'", "OnPositioningFinished", "Fader", "crime_secondArrestFader")]
    [InlineData("CutscenePlayer::Interrupt called for Fader cutscene 'fightClub_fightTeleport' with holder 'x' from module 'y'", "Interrupt", "Fader", "fightClub_fightTeleport")]
    public void A_scene_stage_line_parses(string line, string stage, string type, string name)
    {
        Assert.True(LogTailGameTransport.TryParseSceneStage(line, out var s, out var t, out var n));
        Assert.Equal((stage, type, name), (s, t, n));
    }

    [Theory]
    [InlineData("CutscenePlayer::SomethingElse called for Fader cutscene 'a' with holder")]
    [InlineData("CutscenePlayer::ReleaseScene called for Fader cutscene 'bad name' with holder")]
    [InlineData("CutscenePlayer::ReleaseScene called for Fader cutscene 'quote\"x' with holder")]
    [InlineData("InteractiveSceneManager::EndScene called for Fader cutscene 'a' with holder")]
    [InlineData("CutscenePlayer::ReleaseScene called for Fader cutscene '")]
    public void Anything_else_is_not_a_stage(string line)
    {
        Assert.False(LogTailGameTransport.TryParseSceneStage(line, out _, out _, out _));
    }

    [Fact]
    public void The_scene_queue_echo_matches_the_engines_own_spelling()
    {
        // the engine writes "addded" (three d's): WO-144's pattern never matched it (WO-149 3.7)
        const string line = "Scene 'x' was addded into waiting players";
        Assert.Contains(LogTailGameTransport.Wo144Contains, m => line.Contains(m, StringComparison.Ordinal));
    }

    [Fact]
    public void The_guard_resumes_before_it_rescues_and_gives_up_last()
    {
        Assert.True(GameBridge.SceneResumeAfterS < GameBridge.SceneRescueAfterS);
        Assert.True(GameBridge.SceneRescueAfterS < GameBridge.SceneMaxS);
        Assert.True(GameBridge.SceneMaxS <= 90);   // the field's longest positioning was 87.5 s: never longer than that
    }
}

/// <summary>WO-151 4.1: a minigame's own tutorial is the player's, never mirrored (the field's anvil).</summary>
public class Wo151MinigameTests
{
    [Theory]
    [InlineData("Barbora.trosecko.kovar.hibernace.blacksmithing_minigame.tutorialState", "BlacksmithingTutorialProgress", true)]
    [InlineData("Barbora.trosecko.kovar.hibernace.blacksmithing_minigame.pohyb_mysi_polotovarem_ve_vyhni.pohybuj_mecem_ve_vyhni_state", "Progress", true)]
    [InlineData("Barbora.trosecko.kovar.hibernace.blacksmithing_minigame", "Progress", true)]
    [InlineData("Barbora.trosecko.kovar.StavPrnihoTutorialu", "SomeTutorialProgress", true)]
    [InlineData("Barbora.trosecko.kovar.hibernace.kovar_ceka", "Progress", false)]
    [InlineData("Barbora.trosecko.minigames_board.state", "Progress", false)]
    [InlineData("Barbora.trosecko.zranenyLovci.lovec_krici", "bool", false)]
    public void A_minigames_tutorial_is_the_players_own(string path, string type, bool local)
    {
        Assert.Equal(local, Wo137Rules.PlayerMinigame(path, type));
    }
}
