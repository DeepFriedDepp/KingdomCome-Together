// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-192: saves, simplified -- the pure rules (Wo192Rules) and the source pins of what was removed.</summary>
public class Wo192Tests
{
    private static string Root()
    {
        string d = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(d, "dotnet", "KcdMp.Client"))) d = Path.GetDirectoryName(d)!;
        return d;
    }
    private static string Src(string f) => File.ReadAllText(Path.Combine(Root(), "dotnet", "KcdMp.Client", f));
    private static string Lua() => File.ReadAllText(Path.Combine(Root(), "kdcmp", "Data", "Scripts", "Startup", "kdcmp.lua"));

    // ================================================================ 1: the only blocks are (a) and (b)

    [Fact]
    public void The_joiners_ask_has_two_hard_blocks_only()
    {
        Assert.Equal("", Wo192Rules.AskBlock(savePending: false, inWorld: true, joining: false, rewinding: false));
        Assert.Contains("pending", Wo192Rules.AskBlock(savePending: true, inWorld: true, joining: false, rewinding: false));   // (a)
        Assert.Contains("loading", Wo192Rules.AskBlock(false, inWorld: false, false, false));                                // (b)
        Assert.Contains("join", Wo192Rules.AskBlock(false, true, joining: true, false));                                     // (b)
        Assert.Contains("reloading", Wo192Rules.AskBlock(false, true, false, rewinding: true));                              // (b)
    }

    [Fact]
    public void A_scene_the_readiness_or_a_stuck_cutscene_is_a_log_line_never_a_block()
    {
        // the 0.60.9 field's every refusal: the positioning manager waiting, scenes open, the readiness not ready, a cutscene at its start
        string note = Wo192Rules.SceneNote(positioningWaits: true, openScenes: 2, ready: false, notReady: "C_XGenAIModule[0x2]", stuckCutscene: "konec");
        Assert.Contains("fast-forward set", note);
        Assert.Contains("2 interactive scene(s)", note);
        Assert.Contains("C_XGenAIModule", note);
        Assert.Contains("konec", note);
        Assert.Equal("", Wo192Rules.SceneNote(false, 0, true, "", ""));
        Assert.Equal("", Wo192Rules.SceneNote(false, 0, null, "", ""));
        Assert.StartsWith("WO192-SAVE during a scene: the host save for host save seq=5 is asked all the same", Wo192Rules.SceneLine("host save", 5, note));
        // the ask itself has no input for any of them: whatever runs, the gate is the same two blocks
        Assert.Equal("", Wo192Rules.AskBlock(false, true, false, false));
    }

    [Fact]
    public void The_snapshot_path_asks_through_the_two_blocks_and_the_engines_own_not_now_only()
    {
        string b = Src("GameBridge.Wo184.cs");
        Assert.DoesNotContain("Wo189AskBlockAsync", b);
        Assert.DoesNotContain("NotReadyRetryMs", b);
        Assert.Contains("await Wo192SceneNoteAsync(purpose, w.Seq);", b);
        // the engine's own "not now" stays (pending, CanSave), retried every 2 s
        Assert.Contains("NotNow(\"a save is pending on this machine\", Wo184Rules.EngineRetryMs)", b);
        Assert.Contains("Wo184Rules.EngineRetryMs); return W184Take.NotNow; }", b);
        // the ESC menu's early snapshot, the joiner's own snapshot and the reminder: gone, with their switches
        Assert.DoesNotContain("Wo184OnMenu", b);
        string b189 = Src("GameBridge.Wo189.cs");
        Assert.DoesNotContain("AutoSnap", b189);
        Assert.DoesNotContain("Reminder", b189);
        string lua = Lua();
        Assert.DoesNotContain("mp_joiner_autosnap\"", lua);
        Assert.DoesNotContain("AddCCommand(\"mp_save_reminder\"", lua);
        Assert.DoesNotContain("AddCCommand(\"mp_joiner_autosnap\"", lua);
        // kept: A1, A2, the leave's final snapshot at the exit dialog, mp_save_status, "Partner saved."
        Assert.Contains("await Wo189CancelAfterWaitAsync(purpose, tReq);", b);
        Assert.Contains("Wo189WatchdogAsync(now)", b189);
        Assert.Contains("Wo184FinalSnapshotAsync(\"quit (the exit dialog)\")", b);
        Assert.Contains("AddCCommand(\"mp_save_status\"", lua);
        Assert.Contains("Wo184Rules.HostPartnerSavedText", b);
    }

    // ================================================================ 2: the host's autosave

    [Fact]
    public void The_autosave_counts_play_in_a_session_and_is_due_at_the_interval()
    {
        const long I = 30 * 60_000;
        var a = new Wo192Rules.Autosave();
        long t = 1_000;
        a.Tick(t, inSession: false, inWorld: true);
        for (int i = 0; i < 10; i++) { t += 1_000; a.Tick(t, false, true); }
        Assert.Equal(0, a.PlayMs);                       // no session: no clock
        a.Tick(t, true, true);
        for (int i = 0; i < 1799; i++) { t += 1_000; a.Tick(t, true, true); }
        Assert.False(a.Due(t, I));
        t += 1_000; a.Tick(t, true, true);
        Assert.True(a.Due(t, I));
        // the menu (not in the world) does not count
        var b = new Wo192Rules.Autosave();
        long u = 0;
        b.Tick(u, true, true);
        for (int i = 0; i < 600; i++) { u += 1_000; b.Tick(u, true, inWorld: false); }
        Assert.Equal(0, b.PlayMs);
        // a stall (a load hitch) never counts as minutes of play
        b.Tick(u + 600_000, true, true);
        Assert.Equal(5_000, b.PlayMs);
    }

    [Fact]
    public void The_autosave_waits_60_s_when_the_game_cant_save_and_never_pushes()
    {
        const long I = 60_000;
        var a = new Wo192Rules.Autosave();
        long t = 0;
        a.Tick(t, true, true);
        for (int i = 0; i < 60; i++) { t += 1_000; a.Tick(t, true, true); }
        Assert.True(a.Due(t, I));
        a.Wait(t, "combat");
        Assert.Equal("combat", a.LastWait);
        for (int i = 0; i < 59; i++) { t += 1_000; a.Tick(t, true, true); Assert.False(a.Due(t, I)); }
        t += 1_000; a.Tick(t, true, true);
        Assert.True(a.Due(t, I));                          // asked again 60 s later
        a.Ask(t);
        Assert.False(a.Due(t + 5_000, I));                 // a save on its way: nothing more asked
        a.Saved(wasAutosave: true);
        Assert.Equal(1, a.Done);
        Assert.Equal(0, a.PlayMs);                         // the next interval starts
        Assert.False(a.Due(t + 1_000, I));
    }

    [Fact]
    public void A_manual_save_resets_the_interval_and_a_session_end_forgets_it()
    {
        var a = new Wo192Rules.Autosave();
        long t = 0;
        a.Tick(t, true, true);
        for (int i = 0; i < 20 * 60; i++) { t += 1_000; a.Tick(t, true, true); }
        Assert.Equal(20 * 60_000, a.PlayMs);
        a.Saved(wasAutosave: false);                      // Home, or the player's own save
        Assert.Equal(0, a.PlayMs);
        Assert.Equal(0, a.Done);
        Assert.Equal(30 * 60_000, a.LeftMs(30 * 60_000));
        for (int i = 0; i < 60; i++) { t += 1_000; a.Tick(t, true, true); }
        a.Tick(t + 1_000, inSession: false, true);
        Assert.Equal(0, a.PlayMs);
        Assert.False(a.InSession);
    }

    [Fact]
    public void The_autosave_switches_and_its_answer_parse()
    {
        Assert.True(Wo192Rules.DefaultHostAutosave);
        Assert.Equal(30, Wo192Rules.DefaultAutosaveMinutes);
        Assert.Equal(60_000, Wo192Rules.RetryMs);
        Assert.Equal(30, Wo192Rules.ParseMinutes("on"));
        Assert.Equal(0, Wo192Rules.ParseMinutes("off"));
        Assert.Equal(1.5, Wo192Rules.ParseMinutes("1.5"));
        Assert.Null(Wo192Rules.ParseMinutes("x"));
        Assert.Null(Wo192Rules.ParseMinutes("601"));
        Assert.Equal((true, ""), Wo192Rules.ParseAskAnswer("asked"));
        Assert.Equal((false, "combat"), Wo192Rules.ParseAskAnswer("wait:combat"));
        Assert.Equal((false, "the mod did not answer"), Wo192Rules.ParseAskAnswer("timeout"));
        Assert.True(Wo178Rules.IsSaveWhy(Wo192Rules.AutosaveWhy));   // the schnapps path accepts the word
        Assert.Contains("WO192-AUTOSAVE done file=playline1/quicksave3.whs", Wo192Rules.DoneLine("playline1/quicksave3.whs", 1_800_000));
        Assert.Contains("WO192-AUTOSAVE waiting why=dialogue", Wo192Rules.WaitLine("dialogue", 1_800_000));
    }

    [Fact]
    public void The_autosave_goes_the_schnapps_way_and_never_the_resting_save()
    {
        string lua = Lua();
        int a = lua.IndexOf("function KCD2MP_W192Autosave(tok)", StringComparison.Ordinal);
        Assert.True(a > 0);
        string body = lua[a..lua.IndexOf("\nend", a, StringComparison.Ordinal)];
        Assert.Contains("W178.blockReason", body);             // the game's own rules: dialogue, scene, combat, arranged fight, a transaction
        Assert.Contains("KCD2MP_W178RequestSave(\"autosave\")", body);
        Assert.DoesNotContain("SaveGameViaResting", body);
        Assert.Contains("AddCCommand(\"mp_host_autosave\"", lua);
        Assert.Contains("AddCCommand(\"mp_host_autosave_min\"", lua);
        // the old resting-save world autosave stays off and untouched
        Assert.Contains("worldAutosave = false,", lua);
        string b = Src("GameBridge.Wo122.cs");
        Assert.Contains("Wo192OnHostSaved(s.Display);", b);   // any host save restarts the interval
    }

    // ================================================================ 3: no screenshots

    [Fact]
    public void A_mark_never_takes_a_screenshot()
    {
        string lua = Lua();
        Assert.DoesNotContain("GetScreenShot", lua, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mp_mark_shot", lua);
        Assert.DoesNotContain("markShot", lua);
        string gate = File.ReadAllText(Path.Combine(Root(), "tools", "Build-Installer.ps1"));
        Assert.Contains("Test-NoScreenshots.py", gate);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Root(), "dotnet", "KcdMp.Client"), "*.cs"))
            Assert.DoesNotContain("GetScreenShot", File.ReadAllText(f), StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ 4b: the wake waits for the host

    [Fact]
    public void The_joiners_wake_waits_for_the_hosts_bounded_at_15_s()
    {
        Assert.True(Wo192Rules.HoldWake(hostWoke: false, ownWakeAtMs: 1_000, nowMs: 15_999));
        Assert.False(Wo192Rules.HoldWake(false, 1_000, 16_000));   // the bound
        Assert.False(Wo192Rules.HoldWake(true, 1_000, 2_000));     // the host woke
        Assert.Equal(0, Wo192Rules.HoldFor(joinerWokeMs: 10_000, hostWokeMs: 9_000));      // the host first: no hold
        Assert.Equal(3_300, Wo192Rules.HoldFor(10_000, 12_300));                            // 2.3 s early + the settle
        Assert.Equal(15_000, Wo192Rules.HoldFor(10_000, 60_000));                           // bounded
        string b = Src("GameBridge.Wo140.cs");
        Assert.Contains("if (!host) Wo192OnHostWoke();", b);
        Assert.Contains("if (!host) await Wo192OnLocalWokeAsync();", b);
        Assert.Contains("_w192WakeHolding ? \"the wake waiting for the host (WO-192)\"", Src("GameBridge.Wo191.cs"));   // WO-191's fader bound leaves it
    }

    // ================================================================ 4c: a station's save lock

    [Fact]
    public void A_station_lock_held_past_the_bound_is_due_once_and_a_load_forgets_it()
    {
        Assert.Equal((1, "waterTub"), Wo192Rules.ParseLockLine("Adding script save lock 'waterTub', uiDescription '@ui_saveLock_waterTub'"));
        Assert.Equal((-1, "waterTub"), Wo192Rules.ParseLockLine("Removing script save lock 'waterTub'"));
        Assert.Equal((0, ""), Wo192Rules.ParseLockLine("Removing script save lock"));
        var w = new Wo192Rules.StationLockWatch();
        w.Line("Adding script save lock 'kcdmp_host_only', uiDescription 'The host saves this world.'", 0);
        Assert.Empty(w.Due(100_000));                                            // not a station's
        w.Line("Adding script save lock 'waterTub', uiDescription 'x'", 1_000);
        Assert.Empty(w.Due(15_999));
        var due = w.Due(16_000);
        Assert.Single(due);
        Assert.Equal("waterTub", due[0].Name);
        Assert.Empty(w.Due(30_000));                                             // once per hold
        w.Line("Removing script save lock 'waterTub'", 31_000);
        Assert.False(w.Held("waterTub"));
        w.Line("Adding script save lock 'waterTub', uiDescription 'x'", 40_000);
        w.Line("Removing script save lock 'waterTub'", 45_200);                 // a normal wash: 5.2 s
        Assert.Empty(w.Due(100_000));
        w.Line("Adding script save lock 'waterTub', uiDescription 'x'", 200_000);
        w.Clear();                                                               // a load wipes every script lock
        Assert.Empty(w.Due(300_000));
        Assert.True(LogTailGameTransport.Wo144Passes("Adding script save lock 'waterTub', uiDescription 'x'"));
        Assert.True(LogTailGameTransport.Wo144Passes("Removing script save lock 'waterTub'"));
        string lua = Lua();
        Assert.Contains("function KCD2MP_W192ReleaseStation(tok, name)", lua);
        Assert.Contains("pcall(Game.RemoveSaveLock, \"waterTub\")", lua);         // mp_unstuck too
    }
}
