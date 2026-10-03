// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using System.Text.Json;
using KCDMP_launcher.Models;

namespace KcdMp.Client.Tests;

/// <summary>WO-154 (Phase 4.3/4.4): CONNECT waits until it can work; a joiner without a usable save is told plainly.</summary>
public class Wo154ConnectTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kcdmp-wo154c-" + Guid.NewGuid().ToString("N"));
    public Wo154ConnectTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // ------------------------------------------------------------------ the host's gate

    [Theory]
    [InlineData(GameStage.Starting, ConnectGateRule.LoadSaveFirst)]
    [InlineData(GameStage.Menu, ConnectGateRule.LoadSaveFirst)]
    [InlineData(GameStage.Quitting, ConnectGateRule.LoadSaveFirst)]
    [InlineData(GameStage.Loading, ConnectGateRule.WorldLoading)]
    public void The_host_waits_with_the_reason_until_a_world_is_loaded(GameStage stage, string reason)
    {
        var v = ConnectGateRule.ForHost(stage, double.NaN, logRead: true, sinceGameStartS: 300, nowS: 100);
        Assert.False(v.Open);
        Assert.Equal(reason, v.Reason);
    }

    [Fact]
    public void The_host_opens_once_the_world_has_settled()
    {
        Assert.Equal(new GateView(false, ConnectGateRule.WorldLoading), ConnectGateRule.ForHost(GameStage.World, 100, true, 300, 100 + ConnectGateRule.SettleS - 0.5));
        Assert.Equal(new GateView(true, ""), ConnectGateRule.ForHost(GameStage.World, 100, true, 300, 100 + ConnectGateRule.SettleS));
    }

    [Fact]
    public void A_log_that_cannot_be_read_never_leaves_a_dead_button()
    {
        Assert.Equal(new GateView(false, ConnectGateRule.LoadSaveFirst), ConnectGateRule.ForHost(GameStage.Starting, double.NaN, false, 10, 10));
        Assert.True(ConnectGateRule.ForHost(GameStage.Starting, double.NaN, false, ConnectGateRule.UnreadableOpenAfterS, 50).Open);
    }

    [Fact]
    public void The_games_own_lines_move_the_stage_like_the_agent_reads_them()
    {
        // The marker lines of a real host session (2026-10-02 bundles), a load from the menu, a reload
        // in the world, a failed load and the quit; other lines in between change nothing.
        var s = new GameLogStage();
        void F(string line, double t) => s.Feed(line, t);
        Assert.Equal(GameStage.Starting, s.Stage);
        F("[KCD2-MP] === MOD INIT ===", 1);
        F("PlayVideoOnly 'main_menu_trosecko', loopBeginFrame:2", 2);
        Assert.Equal(GameStage.Menu, s.Stage);
        F("Loading saved game (idx:22)...", 3);
        Assert.Equal(GameStage.Loading, s.Stage);
        F("============================ Loading level trosecko ============================", 4);
        F("Requesting textures precache ...", 5);
        F("[CryAction] LoadGame: '%USER%/saves/playline0/exit.whs'", 40);
        Assert.Equal(GameStage.Loading, s.Stage);
        F("[KCD2-MP] note: Gameplay started is only a marker at the start of a line", 41);
        Assert.Equal(GameStage.Loading, s.Stage);
        F("Gameplay started", 42);
        Assert.Equal(GameStage.World, s.Stage);
        Assert.Equal(42, s.WorldSinceS);
        F("[Warning] Teleport outside PrecacheMode detected! Camera Observer 1 moved 8648.75m in a single frame.", 43);
        Assert.Equal(GameStage.World, s.Stage);
        F("Loading saved game '%USER%/saves/playline0/autosave172.whs' AutoSave, created by '1.5.5-release_1_5', ver. 10505 ...", 60);
        Assert.Equal(GameStage.Loading, s.Stage);
        F("Gameplay started", 75);
        Assert.Equal(75, s.WorldSinceS);
        F("Exiting to main menu because save game loading failed.", 90);
        Assert.Equal(GameStage.Menu, s.Stage);
        Assert.True(double.IsNaN(s.WorldSinceS));
        F("CSystem::Quit invoked from thread 1 (main is 1), reason: Quit requested by C_UISaveLoad::ExitGame(), exit code: 0", 99);
        Assert.Equal(GameStage.Quitting, s.Stage);
    }

    private string Log(string name, string header, params string[] lines)
    {
        string p = Path.Combine(_dir, name);
        File.WriteAllText(p, "Connecting messages from trace server...\r\n\r\n" + header + "\r\nBuild info:\r\n" + string.Concat(lines.Select(l => l + "\r\n")));
        return p;
    }

    [Fact]
    public void Only_this_games_log_is_read_and_an_unended_last_line_counts_once()
    {
        var start = new DateTime(2026, 10, 2, 19, 30, 40, DateTimeKind.Local);
        // the previous game's log, still in place: it ended in a world -- it must not open the gate
        string p = Log("kcd.log", "Log Started at 2026-10-02  18:01:07", "PlayVideoOnly 'main_menu_trosecko', loopBeginFrame:2", "Gameplay started", "x");
        var f = new KcdLogFollower(p, start);
        f.Poll(1);
        Assert.False(f.Reading);
        Assert.Equal(GameStage.Starting, f.State.Stage);

        // the engine starts its own log
        File.WriteAllText(p, "Connecting messages from trace server...\r\n\r\nLog Started at 2026-10-02  19:30:51\r\nBuild info:\r\nPlayVideoOnly 'main_menu_trosecko', loopBeginFrame:2\r\n");
        f.Poll(2);
        Assert.True(f.Reading);
        Assert.Equal(GameStage.Menu, f.State.Stage);

        File.AppendAllText(p, "Loading saved game (idx:22)...\r\nGameplay sta");   // cut off mid-line
        f.Poll(3);
        Assert.Equal(GameStage.Loading, f.State.Stage);
        File.AppendAllText(p, "rted");                                             // whole, but no line end yet (the engine's way)
        f.Poll(4);
        Assert.Equal(GameStage.World, f.State.Stage);
        Assert.Equal(4, f.State.WorldSinceS);
        File.AppendAllText(p, "\r\n[Warning] next line\r\n");
        f.Poll(9);
        Assert.Equal(GameStage.World, f.State.Stage);
        Assert.Equal(4, f.State.WorldSinceS);   // read once: its line end does not restart the settle
    }

    [Fact]
    public void The_log_header_is_read_as_local_time()
    {
        Assert.Equal(new DateTime(2026, 10, 2, 17, 54, 32), KcdLogFollower.StartedAt("BackupNameAttachment=\" Build(1) 02 Oct 26 (17 54 32)\"\r\n\r\nLog Started at 2026-10-02  17:54:32\r\n"));
        Assert.Null(KcdLogFollower.StartedAt("no header here"));
        var none = new KcdLogFollower(Path.Combine(_dir, "missing.log"), DateTime.Now);
        none.Poll(1);   // no file yet: nothing read, nothing thrown
        Assert.False(none.Reading);
        Assert.Equal(GameStage.Starting, none.State.Stage);
    }

    // ------------------------------------------------------------------ the joiner's gate

    [Theory]
    [InlineData(true, "None", true, HostAnswer.Ready)]
    [InlineData(true, "HostNotRunning", false, HostAnswer.NotRunning)]
    [InlineData(true, "HostNotRunning", null, HostAnswer.NotRunning)]
    [InlineData(true, "VersionMismatch", true, HostAnswer.Ready)]
    [InlineData(true, "VersionMismatch", null, HostAnswer.Unknown)]
    [InlineData(false, "Unreachable", null, HostAnswer.Unknown)]
    public void The_hosts_relay_answer_is_read_as_the_gate_needs_it(bool reachable, string kind, bool? host, HostAnswer expected)
    {
        Assert.Equal(expected, ConnectGateRule.FromTest(reachable, kind, host));
    }

    [Fact]
    public void The_joiner_waits_only_while_the_relay_says_no_host()
    {
        Assert.Equal(new GateView(false, ConnectGateRule.HostNotReady), ConnectGateRule.ForJoiner(HostAnswer.Pending));
        Assert.Equal(new GateView(false, ConnectGateRule.HostNotReady), ConnectGateRule.ForJoiner(HostAnswer.NotRunning));
        Assert.True(ConnectGateRule.ForJoiner(HostAnswer.Ready).Open);
        Assert.True(ConnectGateRule.ForJoiner(HostAnswer.Unknown).Open);   // the agent's own flow says what is wrong
    }

    // ------------------------------------------------------------------ plain join messages

    private static ConnectionStatusData Conn() => new() { State = "connected" };

    [Theory]
    [InlineData("regular-game-saves", "Your saves are from the regular game, not the Modding Tools.")]
    [InlineData("only-host-copies", "Your only Modding Tools saves are copies of this same world.")]
    [InlineData("no-saves", "You have no Modding Tools saves yet.")]
    [InlineData("wrong-build", "Your saves are from a different game version than your host's.")]
    public void No_usable_save_shows_the_plain_reason_and_the_new_character_button(string reason, string plain)
    {
        var b = new AgentStatusBanner();
        b.Start(false, 0);
        string? line = b.Apply(Conn(), new JoinStatusData { State = "wrong-build", Message = "agent words", Reason = reason, CanFresh = true }, true, 1);
        Assert.True(b.ShowFreshJoin);
        Assert.Equal(plain + " " + AgentStatusBanner.FreshJoinOffer, b.JoinMessage);
        Assert.Contains($"reason={reason} fresh=shown", line);
    }

    [Fact]
    public void Without_the_agents_offer_its_own_words_stand_and_no_button()
    {
        var b = new AgentStatusBanner();
        b.Start(false, 0);
        b.Apply(Conn(), new JoinStatusData { State = "wrong-build", Message = "agent words", Reason = "regular-game-saves", CanFresh = false }, true, 1);
        Assert.False(b.ShowFreshJoin);
        Assert.Equal("agent words", b.JoinMessage);
        b.Apply(Conn(), new JoinStatusData { State = "wrong-build", Message = "agent words", Reason = "something-new", CanFresh = true }, true, 2);
        Assert.True(b.ShowFreshJoin);
        Assert.Equal("agent words", b.JoinMessage);   // a reason the launcher does not know: the agent's text
        b.Apply(Conn(), new JoinStatusData { State = "choose", Message = "First time in this world", CanFresh = true }, true, 3);
        Assert.False(b.ShowFreshJoin);                 // the first-join question has its own two buttons
        Assert.True(b.ShowChoiceButtons);
        b.Apply(null, null, agentAlive: false, 4);
        Assert.False(b.ShowFreshJoin);
    }

    [Fact]
    public void The_agents_json_fields_reach_the_launcher()
    {
        // NetService reads /join-status with HttpClient's JSON defaults (web: case-insensitive).
        var js = JsonSerializer.Deserialize<JoinStatusData>(
            "{\"State\":\"wrong-build\",\"Percent\":0.0,\"Message\":\"m\",\"reason\":\"only-host-copies\",\"canFresh\":true}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("only-host-copies", js.Reason);
        Assert.True(js.CanFresh);
        var old = JsonSerializer.Deserialize<JoinStatusData>("{\"State\":\"idle\",\"Message\":\"\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("", old.Reason);   // an agent before the change: no button
        Assert.False(old.CanFresh);
    }
}
