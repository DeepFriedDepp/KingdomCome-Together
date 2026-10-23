// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-201: the switches, the inventory screen's lines (B5), this player's dialogues (B6 / B8), the stale queue (B2), the mount
/// lines (B3), combat as a value (B4), the clock's catch-up (B13), item models (B11 / B23), the W164 host-busy kind (B15), and the routing
/// of every event the Lua emits (B16 / B17: two were dropped as "unknown game event" in 0.61.5).</summary>
public class Wo201Tests
{
    [Fact]
    public void Switches_EighteenNames_AllOnButTheSaveTouchingOne()
    {
        Assert.Equal(21, Wo201Rules.SwitchNames.Length);   // WO-204 III.2 retired speech_share and dialogue_vote
        foreach (var n in Wo201Rules.SwitchNames)
        {
            // WO-204 I.5: nearest_player_triggers (rebuilt) is on only once its time-skip soak has passed
            bool expect = n switch { "figure_is_player" => Wo201Rules.Dude2DecodePassed, "nearest_player_triggers" => Wo201Rules.NearestPlayerV2SoakPassed, _ => true };
            Assert.Equal(expect, Wo201Rules.DefaultOn(n));
            Assert.True(Wo201Rules.TryParseCfg(n + "=off", out var name, out bool on));
            Assert.Equal(n, name);
            Assert.False(on);
        }
        Assert.False(Wo201Rules.TryParseCfg("inv_screen_guard=maybe", out _, out _));
        Assert.False(Wo201Rules.TryParseCfg("nope=on", out _, out _));
    }

    [Fact]
    public void B9FollowUp_ItsSwitchShipsOn_AndReachesTheDll()
    {
        Assert.Contains("copy_no_idle_turn", Wo201Rules.SwitchNames);
        Assert.True(Wo201Rules.DefaultOn("copy_no_idle_turn"));
        Assert.Equal(15, Wo201Rules.ConfigBits(_ => true));
        Assert.Equal(0, Wo201Rules.ConfigBits(_ => false));
        Assert.Equal(15 & ~8, Wo201Rules.ConfigBits(n => n != "copy_no_idle_turn"));
        Assert.Equal(7, Wo201Rules.ConfigBits(n => n is "joiner_scene_noposition" or "walk_from_host" or "oneshot_tick_cap"));
    }

    [Fact]
    public void B5_TheScreenLines()
    {
        Assert.Equal(Wo201Rules.ScreenSignal.In, Wo201Rules.ScreenLine("PlayAudio: ui_inv_screen_in_one_pane", out _));
        Assert.Equal(Wo201Rules.ScreenSignal.Out, Wo201Rules.ScreenLine("PlayAudio: ui_inv_screen_out_one_pane", out _));
        Assert.Equal(Wo201Rules.ScreenSignal.In, Wo201Rules.ScreenLine("PlayAudio: ApseOpen", out _));
        Assert.Equal(Wo201Rules.ScreenSignal.Out, Wo201Rules.ScreenLine("PlayAudio: ApseClose", out _));
        Assert.Equal(Wo201Rules.ScreenSignal.Intent, Wo201Rules.ScreenLine("OnLoot: Dude->zbranePanaSemina_bandit_1", out var s));
        Assert.Equal("zbranePanaSemina_bandit_1", s);
        Assert.Equal(Wo201Rules.ScreenSignal.None, Wo201Rules.ScreenLine("OnLoot: Dude->bad\"name", out _));
        Assert.Equal(Wo201Rules.ScreenSignal.None, Wo201Rules.ScreenLine("PlayAudio: MoveItem", out _));
        Assert.Contains("PlayAudio: ui_inv_screen_in", LogTailGameTransport.Wo144Prefixes);
        Assert.Contains("OnLoot: Dude->", LogTailGameTransport.Wo144Prefixes);
        Assert.True(Wo136Rules.IsTakeVerdict("busy"));
    }

    [Fact]
    public void B6_B8_ThisPlayersDialogues()
    {
        Assert.True(Wo201Rules.IsSoloDudeDialogue("New dialogue 'Barbora.x.tour_barks.hrac_se_blizi (ingame monolog - Id: 700) (x)' is starting. Params: souls = 'Ex: Dude' forced = false"));
        Assert.True(Wo201Rules.IsSoloDudeDialogue("Attempting to start new dialogue (runtime id '652') with souls 'Ex: Dude'"));
        Assert.False(Wo201Rules.IsSoloDudeDialogue("New dialogue 'a.b (normal - Id: 927) (ForceDialogNode)' is starting. Params: souls = 'Ex: Dude; Ex: tsem_seminsr; Ex: tsem_suk' forced = false"));
        Assert.False(Wo201Rules.IsSoloDudeDialogue("Attempting to start new dialogue (runtime id '559') with souls 'Ex: tsem_woman_13 - meta override: X'"));
        Assert.True(Wo201Rules.OwnTalk(1_000, 2_000));
        Assert.False(Wo201Rules.OwnTalk(1_000, 40_000));
        Assert.False(Wo201Rules.OwnTalk(0, 2_000));
        Assert.False(Wo201Rules.OwnTalk(3_000, 2_000));
        Assert.Equal(927, Wo201Rules.ClosedDialogId("[ID: 927] Dialog interrupted. [Ex0: Dude Ex1: tsem_seminsr state: WAITING_FOR_TWINS flags: 27263497]"));
        Assert.Equal(246, Wo201Rules.ClosedDialogId("[ID: 246] Dialog ending [Ex0: a Ex1: b state: CLEANUP flags: 9104]"));
        Assert.Equal(-1, Wo201Rules.ClosedDialogId("[ID: 246] DialogCommand-Anim x"));
        Assert.Equal(652, Wo201Rules.CancelledRequestId("Canceling dialog request id 652 from soul 'Dude'. Request timed out."));
        Assert.Equal(-1, Wo201Rules.CancelledRequestId("Canceling something"));
    }

    [Fact]
    public void B2_TheQueue()
    {
        const string scene = "Barbora.trosecko.zbranePanaSemina.h.v_troskovicich.zpet_u_semina.SpeakTrigger.speaktrigger";
        Assert.Equal(scene, Wo201Rules.WaitingScene($"Scene '{scene}' was addded into waiting players and will be processed after currently playing player."));
        Assert.Equal("", Wo201Rules.WaitingScene("Scene '' was addded into waiting players and will be processed after currently playing player."));
        Assert.Null(Wo201Rules.WaitingScene("Scene 'x' is fine"));
        Assert.Equal("CallbackScenePlayer", Wo201Rules.EndedScene("InteractiveSceneManager::EndScene called for CallbackScenePlayer"));
        Assert.Equal("Barbora.trosecko.zbranePanaSemina.h.v_troskovicich.zpet_u_semina", Wo201Rules.SceneModule(scene));
        Assert.True(Wo201Rules.DialogueOfScene("Barbora.trosecko.zbranePanaSemina.h.v_troskovicich.zpet_u_semina.zpet_u_semina", scene));
        Assert.False(Wo201Rules.DialogueOfScene("Barbora.trosecko.zbranePanaSemina.h.krizovatky.krizovatka_1.x", scene));
        Assert.True(Wo201Rules.TryDudeDialogueStart(
            "New dialogue 'Barbora.trosecko.zbranePanaSemina.h.v_troskovicich.zpet_u_semina.zpet_u_semina (normal - Id: 787) (ForceDialogNode)' is starting. Params: souls = 'Ex: Dude; Ex: tsem_seminsr; Ex: tsem_suk' forced = false",
            out var path, out int id));
        Assert.Equal(787, id);
        Assert.EndsWith("zpet_u_semina.zpet_u_semina", path);
        Assert.False(Wo201Rules.TryDudeDialogueStart("New dialogue 'x (normal - Id: 1) (y)' is starting. Params: souls = 'Ex: a; Ex: b'", out _, out _));
        Assert.Equal(10_000, Wo201Rules.StaleQueueMs);
    }

    [Fact]
    public void B3_TheEnginesMountLines()
    {
        Assert.True(Wo201Rules.TryMountLine("Actor 'tsem_seminsr' starts mounting horse 'tsem_seminsrHorse' ", out var a, out var h, out bool m));
        Assert.Equal(("tsem_seminsr", "tsem_seminsrHorse", true), (a, h, m));
        Assert.True(Wo201Rules.TryMountLine("Actor 'tsem_suk' starts mounting horse 'tsem_sukHorse' instantly", out a, out h, out m));
        Assert.True(m);
        Assert.True(Wo201Rules.TryMountLine("Actor 'tsem_suk' dismounting horse 'tsem_sukHorse' ", out a, out h, out m));
        Assert.False(m);
        Assert.False(Wo201Rules.TryMountLine("Actor 'DialogTwin_tsem_suk' starts mounting horse 'DialogTwin_tsem_sukHorse' instantly", out _, out _, out _));
        Assert.False(Wo201Rules.TryMountLine("Actor 'kcd2mp_1' starts mounting horse 'h' ", out _, out _, out _));
        Assert.False(Wo201Rules.TryMountLine("Actor 'x' does something", out _, out _, out _));
        Assert.Contains("Actor '", LogTailGameTransport.Wo144Prefixes);
    }

    [Fact]
    public void B4_CombatIsAValue()
    {
        Assert.True(Wo201Rules.HostFights(true, false));
        Assert.True(Wo201Rules.HostFights(false, true));
        Assert.False(Wo201Rules.HostFights(false, false));
        Assert.True(Wo201Rules.ShoutRefused(false, false, 0));          // no word from the host: a copy never starts a fight here
        Assert.True(Wo201Rules.ShoutRefused(true, false, 2_000));       // the host's NPC is not fighting
        Assert.False(Wo201Rules.ShoutRefused(true, false, 500));        // a retarget's moment of combat=0
        Assert.False(Wo201Rules.ShoutRefused(true, true, 9_000));       // the host's NPC fights: the copy fights
    }

    [Fact]
    public void B13_TheClockCatchesUp()
    {
        bool c = false;
        double r = Wo201Rules.CatchUpRatio(15.0, -45, ref c);
        Assert.True(c);
        Assert.True(r > 15.0 * 1.3, r.ToString());                       // more than WO-200's 25 %
        Assert.True(Wo201Rules.CatchUpRatio(15.0, -120, ref c) <= 22.5);
        Assert.Equal(22.5, Wo201Rules.CatchUpRatio(15.0, -120, ref c));   // at most +50 %
        Assert.True(Wo201Rules.CatchUpRatio(15.0, -10, ref c) > 15.0);    // still catching up (until within 5)
        Assert.True(c);
        Assert.Equal(15.0, Wo201Rules.CatchUpRatio(15.0, -3, ref c));     // within 5: done
        Assert.False(c);
        Assert.Equal(Wo200Rules.SlewRatio(15.0, -20), Wo201Rules.CatchUpRatio(15.0, -20, ref c));   // under 30 behind: WO-200's slew
        Assert.Equal(15.0, Wo201Rules.CatchUpRatio(15.0, -700, ref c));   // beyond 10 minutes: the other paths' job
        // a 120 game-s gap closes within 60 real s at the host's 15 (the WO's acceptance), never a jump
        double err = -120; c = false; int s = 0;
        for (; s < 120 && err < -5; s++) { double ratio = Wo201Rules.CatchUpRatio(15.0, err, ref c); err += ratio - 15.0; }
        Assert.True(s <= 60, s.ToString());
    }

    [Fact]
    public void B11_B23_ItemModels()
    {
        Assert.True(Wo201ItemModels.IsModelPath("manmade/weapons/swords_long/long_sword_duel.cgf"));
        Assert.False(Wo201ItemModels.IsModelPath("../x.cgf"));
        Assert.False(Wo201ItemModels.IsModelPath("a\"b.cgf"));
        Assert.False(Wo201ItemModels.IsModelPath("x.dds"));
        Assert.Equal("objects/manmade/x.cgf", Wo201ItemModels.EnginePath("manmade/x.cgf"));
        Assert.True(Wo201ItemModels.IsRanged("MissileWeapon"));
        Assert.False(Wo201ItemModels.IsRanged("MeleeWeapon"));
    }

    [Fact]
    public void V1_ReceiveOnly_TheField()
    {
        // the field's nine requests (0.61.5, 11:41-12:11): only his own interactions and his own conversation's steps are his to ask
        Assert.False(Wo201Rules.ReceiveOnlyAsks(false, "Barbora.trosecko.fight_clubs.podseminsky_fight_club.fightClubPodsemin.tipping_a_zadani.isTipsterCloseToArena"));
        Assert.False(Wo201Rules.ReceiveOnlyAsks(false, "Barbora.trosecko.zbranePanaSemina.h.hrac_se_na_to_vykaslal.loadedprofilestate.isProfileLoaded"));
        Assert.False(Wo201Rules.ReceiveOnlyAsks(false, "Barbora.trosecko.zbranePanaSemina.aftermath.vasek.pacholekState"));
        Assert.False(Wo201Rules.ReceiveOnlyAsks(false, "Barbora.trosecko.kumaniNaTrosecku.rvacka_v_hospode.approach.trackingPlayerNearPub"));
        Assert.False(Wo201Rules.ReceiveOnlyAsks(false, "Barbora.trosecko.zbranePanaSemina.h.utok_na_hlidku.skirmishHlidkaState"));
        Assert.True(Wo201Rules.ReceiveOnlyAsks(false, "Barbora.trosecko.zbranePanaSemina.h.krizovatky.krizovatka_1.hledani_pytle_s_uhlim.interactiontrigger_monologonplayer_oneshot.triggerActive"));
        Assert.True(Wo201Rules.ReceiveOnlyAsks(true, "Barbora.trosecko.zbranePanaSemina.h.na_semine.odjezd_ze_semina.sukSeminWaiting"));
    }

    [Fact]
    public void V3_Dude2_WatchdogAndCensus()
    {
        Assert.False(Wo201Rules.DefaultOn("figure_is_player") && !Wo201Rules.Dude2DecodePassed);   // off until its save-decode check passes
        Assert.True(Wo201Rules.DefaultOn("walk_from_host"));
        Assert.False(Wo201Rules.Dude2Trips(true, true, false, out _));
        Assert.True(Wo201Rules.Dude2Trips(false, true, false, out string w1)); Assert.Contains("save gate", w1);
        Assert.True(Wo201Rules.Dude2Trips(true, false, false, out string w2)); Assert.Contains("PlayerSoul", w2);
        Assert.True(Wo201Rules.Dude2Trips(true, true, true, out string w3)); Assert.Contains("IsLocalPlayer", w3);
        Assert.StartsWith("PASS", Wo201Rules.CensusVerdict(1, 1, true, false));    // after a load: the player's own only
        Assert.StartsWith("PASS", Wo201Rules.CensusVerdict(2, 1, true, true));     // in play: the player and Dude2
        Assert.StartsWith("FAIL", Wo201Rules.CensusVerdict(2, 1, true, false));    // a save carried Dude2's bit
        Assert.StartsWith("FAIL", Wo201Rules.CensusVerdict(1, 1, false, false));
        Assert.StartsWith("FAIL", Wo201Rules.CensusVerdict(1, 2, true, false));
        Assert.True(Wo201Rules.TryParseCfg("figure_is_player=on", out string n, out bool on) && n == "figure_is_player" && on);
    }

    [Fact]
    public void B15_TheHostBusyKind()
    {
        Assert.Equal(20, Protocol.W164HostBusy);
        Assert.Equal("host-busy", Protocol.W164KindName(Protocol.W164HostBusy));
        Assert.Equal(76, Protocol.Wo201OpStatus);
        Assert.Equal(77, Protocol.Wo201OpConfig);
        Assert.Equal(78, Protocol.Wo201OpCombatEnd);
    }

    [Fact]
    public void B16_B17_EveryLuaEventHasARoute()
    {
        // 0.61.5 dropped w200_qitems and w200_noise as "unknown game event": no quest-item count and no joiner noise ever left a machine
        string root = FindRoot();
        string lua = File.ReadAllText(Path.Combine(root, "kdcmp", "Data", "Scripts", "Startup", "kdcmp.lua"));
        var events = Regex.Matches(lua, "KCD2MP_EmitEvent\\(\\s*\"([A-Za-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        string cs = string.Join("\n", Directory.GetFiles(Path.Combine(root, "dotnet", "KcdMp.Client"), "*.cs").Select(File.ReadAllText));
        var routed = Regex.Matches(cs, "case\\s+\"([A-Za-z0-9_]+)\"").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(cs, "name == \"([A-Za-z0-9_]+)\"").Select(m => m.Groups[1].Value)).ToHashSet();
        var launcherOnly = new HashSet<string> { "w159" };   // the WO-159 menu's choices: read by the launcher's own log tail
        var missing = events.Where(e => !routed.Contains(e) && !launcherOnly.Contains(e) && !e.StartsWith("w201_", StringComparison.Ordinal)).OrderBy(e => e).ToList();
        Assert.True(missing.Count == 0, "events the agent never routes: " + string.Join(", ", missing));
        Assert.Contains("w200_qitems", routed);
        Assert.Contains("w200_noise", routed);
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "VERSION"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("the repository root (VERSION) was not found");
    }
}
