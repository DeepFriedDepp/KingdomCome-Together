// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-202: the switches, the join's short hold and its silence, the quest-fail tracer's record and words, the network queue's
/// state keys, the voice path the speech share takes, the rejoin's retries, and the crash watch's own part of a report.</summary>
public class Wo202Tests
{
    [Fact]
    public void Switches_AllShipOn_AndTheDllBitsMatchTheNativeRules()
    {
        Assert.Equal(16, Wo202Rules.SwitchNames.Length);   // the four with no mechanism (draw, walk start, areas, dead copy items) went; WO-204 IV.1 deleted join_short_hold
        Assert.Equal(Wo202Rules.SwitchNames.Length, Wo202Rules.SwitchNames.Distinct().Count());
        foreach (var n in Wo202Rules.SwitchNames)
        {
            Assert.True(Wo202Rules.DefaultOn(n));
            Assert.True(Wo202Rules.TryParseCfg(n + "=off", out var name, out bool on));
            Assert.Equal(n, name);
            Assert.False(on);
        }
        Assert.False(Wo202Rules.TryParseCfg("crash_capture=maybe", out _, out _));
        Assert.False(Wo202Rules.TryParseCfg("nope=on", out _, out _));
        // native/KCDMP/wo202_rules.h kBit*: 1 crash, 2 near_safe, 4 quest_fail, 16 anim_shown, 32 recoil_clinch, 64 fight_pos_fresh, 512 death_engine_only
        Assert.Equal(631u, Wo202Rules.ConfigBits(_ => true));   // 1+2+4+16+32+64+512
        Assert.Equal(512u, Wo202Rules.ConfigBits(n => n == "death_engine_only"));
        Assert.Equal(0u, Wo202Rules.ConfigBits(_ => false));
        Assert.Equal(1u, Wo202Rules.ConfigBits(n => n == "crash_capture"));
        Assert.Equal(2u, Wo202Rules.ConfigBits(n => n == "near_safe"));
        Assert.Equal(4u, Wo202Rules.ConfigBits(n => n == "quest_fail_trace"));
        Assert.Equal(0u, Wo202Rules.ConfigBits(n => n is "copy_draw" or "walk_idle_to_move" or "body_items_safe"));   // no mechanism, no bit
    }

    [Fact]
    public void Join_OnlySilenceAborts()
    {
        // (II.2's short hold is deleted by WO-204 IV.1: the world stays paused until the joiner is ready -- Wo204Tests)
        // III.3: session A's joiner's acks came 43 s late; 0.61.9 aborted after 20 s
        Assert.Equal(60, Wo202Rules.JoinSilenceAbortS(true));
        Assert.Equal(20, Wo202Rules.JoinSilenceAbortS(false));
    }

    private static byte[] FailReply(bool more, uint seq, int ageMs, int old, int nw, bool cascade, int ranMs, string path, string port, string cls, string sp, string sport)
    {
        var b = new List<byte> { (byte)(more ? 1 : 0) };
        void U32(uint v) { var t = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(t, v); b.AddRange(t); }
        void I32(int v) { var t = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(t, v); b.AddRange(t); }
        void S(string s, bool wide)
        {
            var d = Encoding.UTF8.GetBytes(s);
            if (wide) { var t = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(t, (ushort)d.Length); b.AddRange(t); } else b.Add((byte)d.Length);
            b.AddRange(d);
        }
        U32(seq); I32(ageMs); I32(old); I32(nw); b.Add((byte)(cascade ? 1 : 0)); I32(ranMs);
        S(path, true); S(port, false); S(cls, false); S(sp, true); S(sport, false);
        return b.ToArray();
    }

    [Fact]
    public void QuestFail_TheHiredHandsCase_ReadsAndIsSaidPlainly()
    {
        // session B, 19:15:52: the hired hand's timer ran out 7.7 min after Semine's order (the hired hand frozen after a join)
        var p = FailReply(false, 3, 1200, 1, 5, true, 460_000, "Barbora.trosecko.zbranePanaSemina.h.boj_v_tabore.getPacholek", "SetCanceled",
                          ".?AVC_Timer@rpgmodule@wh@@", "Barbora.trosecko.zbranePanaSemina.h.boj_v_tabore.pacholek.timerUtek", "OnFinished");
        Assert.True(Wo202Rules.TryParseFail(p, out var f, out bool more));
        Assert.False(more);
        Assert.Equal(3u, f.Seq);
        Assert.Equal(5, f.New);
        Assert.True(f.Cascade);
        Assert.Equal("timer", Wo202Rules.SourceKind(f.Class));
        string words = Wo202Rules.FailInWords(f, 2);
        Assert.Contains("boj_v_tabore.getPacholek was cancelled", words);
        Assert.Contains("its timer 'pacholek.timerUtek' ran out after 7.7 min", words);
        Assert.Contains("2 mod event(s) in that window", words);
        // nothing new: only the more byte
        Assert.False(Wo202Rules.TryParseFail([0], out _, out _));
        // a cut reply is refused, never half-read
        Assert.False(Wo202Rules.TryParseFail(p[..(p.Length - 3)], out _, out _));
        // no node pulse: the game's own code
        var q = FailReply(true, 4, 0, 0, 1, false, -1, "Barbora.x.aftermath.HracSeNaQuestVykaslal", "SetTrue", "", "", "");
        Assert.True(Wo202Rules.TryParseFail(q, out var g, out bool more2));
        Assert.True(more2);
        Assert.Contains("marked the player as having walked away", Wo202Rules.FailInWords(g, 0));
        Assert.Contains("no node pulse made it", Wo202Rules.FailInWords(g, 0));
    }

    [Fact]
    public void NetQueue_OnlyStatesAreReplaced_EventsKeepTheirOrder()
    {
        Assert.Equal("g:2", Wo202Rules.StateKey(Protocol.Ghost, [2, 0, 0, 0]));
        var name = Encoding.UTF8.GetBytes("ttkc_man_3");
        var npc = new byte[2 + name.Length + 4];
        npc[0] = 0; npc[1] = (byte)name.Length; name.CopyTo(npc, 2);
        Assert.Equal("n:ttkc_man_3", Wo202Rules.StateKey(Protocol.NpcStateDown, npc));
        Assert.Null(Wo202Rules.StateKey(Protocol.NpcStateDown, [0, 9, 1]));   // a name longer than the frame
        Assert.Null(Wo202Rules.StateKey(Protocol.Pong, new byte[8]));
        Assert.Null(Wo202Rules.StateKey(Protocol.JoinStatusDown, new byte[16]));
        Assert.Null(Wo202Rules.StateKey(Protocol.Ghost, []));
    }

    [Fact]
    public void Speech_TheEnginesOwnPathIsAVoicePath()
    {
        // the field's lines: no extension -- WO-201 took only ".ogg" and captured nothing on all four machines
        Assert.True(Wo202Rules.IsVoicePath("Localization/dialog/trosecko/zbranePanaSemina/v_ta_pacholek__ale_dyt_ms_O8Jj"));
        Assert.True(Wo202Rules.IsVoicePath(@"Localization\dialog\trosecko\zbranePanaSemina\v_ta_pacholek__ale_dyt_ms_O8Jj"));
        Assert.True(Wo202Rules.IsVoicePath("Localization/dialog/open_world/x/line.ogg"));
        Assert.False(Wo202Rules.IsVoicePath("Localization/dialog/a/"));
        Assert.False(Wo202Rules.IsVoicePath("Localization/dialog/../x"));
        Assert.False(Wo202Rules.IsVoicePath("Sounds/dialog/a/b"));
        Assert.False(Wo202Rules.IsVoicePath(""));
    }

    [Fact]
    public void Rejoin_TriesThreeTimesWithGrowingGaps_ThenAsks()
    {
        Assert.Equal(20, Wo202Rules.RejoinGapS(0));
        Assert.Equal(40, Wo202Rules.RejoinGapS(1));
        Assert.Equal(80, Wo202Rules.RejoinGapS(2));
        Assert.Equal(80, Wo202Rules.RejoinGapS(9));
        Assert.False(Wo202Rules.RejoinShouldAsk(2));
        Assert.True(Wo202Rules.RejoinShouldAsk(3));
        Assert.Contains("F11 try again, F12 leave", Wo202Rules.RejoinQuestion);
    }

    [Fact]
    public void ClockV2_BunchedLateSamples_NoBackwardJump_AtMostTwoForward()
    {
        // session B's joiner: the host's clock samples (one a second) arrived bunched and late -- the one-way delay rising 35 -> 850 ms,
        // samples held back then released together. Both clocks run at real speed; the copy started in step.
        var rng = new Random(202);
        var arrivals = new List<(double PartnerS, long ArrivedMs)>();
        long backlogUntil = 0;
        for (int k = 0; k < 40; k++)
        {
            long sent = k * 1000L;
            long delay = 35 + (long)(815.0 * k / 39.0) + rng.Next(0, 120);
            if (k % 7 == 3) backlogUntil = sent + 2500;   // a stall: what is sent now waits, then comes out in a bunch
            long arrive = Math.Max(sent + delay, backlogUntil);
            arrivals.Add((k, arrive));
        }
        arrivals.Sort((a, b) => a.ArrivedMs.CompareTo(b.ArrivedMs));
        // v1 (WO-194): a set whenever the sample (plus the 35 ms one-way delay) is 0.2 s off this copy's clock
        int v1Sets = 0, v1Back = 0;
        double v1Own = 0; long v1At = 0;
        foreach (var (ps, at) in arrivals)
        {
            double own = v1Own + (at - v1At) / 1000.0;
            double target = ps + 0.035;
            if (Math.Abs(target - own) > 0.2) { v1Sets++; if (target < own) v1Back++; v1Own = target; } else v1Own = own;
            v1At = at;
        }
        Assert.True(v1Sets > 10 && v1Back > 3, $"the old rule seeks a lot on this stream ({v1Sets} sets, {v1Back} back)");
        // v2: median of 8, late samples dropped, speed 0.9..1.1, a jump only forward past 1.5 s held 1 s
        var win = new List<Wo202Rules.ClockSample>();
        double own2 = 0, speed = 1.0; long at2 = 0, behindSince = 0;
        int jumps = 0, back = 0;
        foreach (var (ps, at) in arrivals)
        {
            own2 += speed * (at - at2) / 1000.0; at2 = at;
            win.Add(new Wo202Rules.ClockSample(ps, at));
            while (win.Count > Wo202Rules.ClockSamples) win.RemoveAt(0);
            double est = Wo202Rules.PartnerNowS(win, 35, at);
            if (double.IsNaN(est)) continue;
            double drift = est - own2;
            if (drift > Wo202Rules.SeekOverS) { if (behindSince == 0) behindSince = at; } else behindSince = 0;
            if (Wo202Rules.SeekNow(drift, behindSince, at, 0)) { jumps++; if (est < own2) back++; own2 = est; behindSince = 0; }
            speed = Wo202Rules.SpeedFor(drift);
            Assert.InRange(speed, 0.9, 1.1);
        }
        Assert.Equal(0, back);
        Assert.True(jumps <= 2, $"{jumps} jumps");
        // the rules themselves
        Assert.Equal(1.0, Wo202Rules.SpeedFor(0.03));
        Assert.Equal(1.1, Wo202Rules.SpeedFor(5.0));
        Assert.Equal(0.9, Wo202Rules.SpeedFor(-5.0));
        Assert.False(Wo202Rules.SeekNow(-3.0, 1, 5000, 0));          // ahead: never back
        Assert.False(Wo202Rules.SeekNow(2.0, 4500, 5000, 0));        // behind only 0.5 s so far
        Assert.True(Wo202Rules.SeekNow(2.0, 3000, 5000, 0));
        Assert.False(Wo202Rules.SeekNow(2.0, 3000, 5000, 6000));     // a line plays until 6 s
        // out of order and late: the newest sample decides; a late one is dropped
        var w = new List<Wo202Rules.ClockSample> { new(10.0, 10_000), new(9.0, 10_100), new(11.0, 11_000) };
        Assert.InRange(Wo202Rules.PartnerNowS(w, 0, 11_000), 10.99, 11.01);
    }

    [Fact]
    public void Outfit_NewPiecesFirst_InOneCall_AndTheStripCheckCloseAfterAChange()
    {
        Assert.True(Wo202Rules.OutfitAtomic(true, true, 3));
        Assert.False(Wo202Rules.OutfitAtomic(true, false, 3));   // REST equips (mp_avatar_dress off) stay one by one
        Assert.False(Wo202Rules.OutfitAtomic(false, true, 3));
        Assert.False(Wo202Rules.OutfitAtomic(true, true, 0));
        Assert.Equal(2_000, Wo202Rules.OutfitCheckMs(true, 100_000, 110_000, 10_000));
        Assert.Equal(10_000, Wo202Rules.OutfitCheckMs(true, 100_000, 131_000, 10_000));
        Assert.Equal(10_000, Wo202Rules.OutfitCheckMs(false, 100_000, 110_000, 10_000));
        Assert.Equal(10_000, Wo202Rules.OutfitCheckMs(true, 0, 110_000, 10_000));
        var many = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        string list = Wo202Rules.OutfitList(many);
        Assert.Equal(40, list.Split(',').Length);
        Assert.True(list.Length + 120 < 2_100);   // the console's ceiling with the call around it
    }

    [Fact]
    public void Keys_TheGamesKeysOnly_AndTheShareTextIsStrict()
    {
        // MiscItem Type 4 holds the keys (key_general / key_unique icons), the shovel and the lockpick; quest keys are the quest mirror's
        Assert.True(WhsSave.IsKeyRow("Type=\"4\" IconId=\"key_general\" UIName=\"x\" Id=\"043dbb89-6a87-4dd7-a6ee-ca8c094acc18\""));
        Assert.True(WhsSave.IsKeyRow("Type=\"4\" IconId=\"key_unique\" Id=\"0067c708-9a85-4034-b9fb-94975eda6bf3\""));
        Assert.False(WhsSave.IsKeyRow("Type=\"4\" IconId=\"player_lockpick\""));
        Assert.False(WhsSave.IsKeyRow("Type=\"4\" IconId=\"player_shovel\""));
        Assert.False(WhsSave.IsKeyRow("Type=\"5\" IconId=\"key_general\""));
        Assert.False(WhsSave.IsKeyRow("Type=\"4\" IconId=\"key_general\" IsQuestItem=\"true\""));
        string? tables = Environment.GetEnvironmentVariable("KCD2MP_TABLES_PAK") ?? @"D:\SteamLibrary\steamapps\common\KCD2Mod\Data\Tables.pak";
        if (File.Exists(tables))
        {
            var k = WhsSave.KeyClasses(tables);
            Assert.InRange(k.Count, 60, 90);   // 1.5.5: 79 key rows, 7 of them quest keys
            Assert.True(k.ContainsKey("043dbb89-6a87-4dd7-a6ee-ca8c094acc18"));
            Assert.False(k.ContainsKey("169647ff-fa6d-433a-b49e-06ad7ce01b2a"));   // a quest key (IsQuestItem)
        }
        Assert.Equal("k|043dbb89-6a87-4dd7-a6ee-ca8c094acc18", Wo202Rules.KeyText(" 043dbb89-6a87-4dd7-a6ee-ca8c094acc18 "));
        Assert.Null(Wo202Rules.KeyText("not a class"));
        Assert.True(Wo202Rules.TryParseKey("k|043dbb89-6a87-4dd7-a6ee-ca8c094acc18", out string cls));
        Assert.Equal("043dbb89-6a87-4dd7-a6ee-ca8c094acc18", cls);
        Assert.False(Wo202Rules.TryParseKey("k|\"); os.exit()", out _));
        Assert.Equal("c|chest_01.a|12.50|-3.00|99.13", Wo202Rules.ChestText("chest_01.a 12.5 -3 99.13"));
        Assert.Null(Wo202Rules.ChestText("bad\"name 1 2 3"));
        Assert.Null(Wo202Rules.ChestText("chest 1 2"));
        Assert.True(Wo202Rules.TryParseChest("c|chest_01.a|12.50|-3.00|99.13", out string n, out float x, out _, out float z));
        Assert.Equal("chest_01.a", n);
        Assert.Equal(12.5f, x);
        Assert.Equal(99.13f, z, 2);
        Assert.False(Wo202Rules.TryParseChest("c|a\")b|1|2|3", out _, out _, out _, out _));
    }

    [Fact]
    public void Clinch_NamesTheBodyThePlayerClinched()
    {
        // the field: 4 of the host's clinches reached the joiner and were skipped, "his figure has no opponent body here"
        var slave = Guid.Parse("69b615fd-4fee-3ef7-a02d-4bbca37f88d2");
        var e = Wo197Rules.ClinchEvent(1, 0x05, Guid.NewGuid(), slave, "tvez_man_18");
        Assert.Equal(slave, Wo197Rules.ClinchSlaveRow(e));
        Assert.Equal("tvez_man_18", Wo197Rules.ClinchOpponent(e));
        var old = Wo197Rules.ClinchEvent(1, 0x05, Guid.NewGuid(), slave);   // 0.61.9's form: no opponent
        Assert.Equal(slave, Wo197Rules.ClinchSlaveRow(old));
        Assert.Null(Wo197Rules.ClinchOpponent(old));
        var noRows = Wo197Rules.ClinchEvent(1, 0x01, Guid.Empty, Guid.Empty, "tvez_man_18");
        Assert.Equal(Guid.Empty, Wo197Rules.ClinchSlaveRow(noRows));
        Assert.Equal("tvez_man_18", Wo197Rules.ClinchOpponent(noRows));
        Assert.Null(Wo197Rules.ClinchOpponent(Wo197Rules.ClinchEvent(1, 0x01, Guid.Empty, Guid.Empty, "bad name\"")));
        var p = new byte[8 + 11];
        BitConverter.GetBytes(0x40837u).CopyTo(p, 0); p[4] = p[5] = 255; p[7] = 11;
        System.Text.Encoding.ASCII.GetBytes("tvez_man_18").CopyTo(p, 8);
        Assert.Equal("tvez_man_18", Wo202Rules.OwnOpponentName(p));
        Assert.Null(Wo202Rules.OwnOpponentName(new byte[7]));
    }

    [Fact]
    public void Death_IsTheEnginesOnly_AndABodyWithoutItsDeathReactionIsRead()
    {
        // the observer's death goes out only when the engine's soul reads dead; an older DLL (no answer) keeps the observer's word
        Assert.True(Wo202Rules.AnnounceDeath(1));
        Assert.False(Wo202Rules.AnnounceDeath(0));
        Assert.True(Wo202Rules.AnnounceDeath(null));
        Assert.True(Wo202Rules.DeathReadGapMs > 250 && Wo202Rules.DeathReadTries >= 2);   // past the DLL's 0.25 s read cache
        // session A's host: the joiner's killing blows with reaction "victim-cannot-react" / "no-reaction-found" left the body upright
        Assert.True(Wo202Rules.DeathFallWanted(true, true, true, 3));
        Assert.True(Wo202Rules.DeathFallWanted(true, true, true, 4));
        Assert.True(Wo202Rules.DeathFallWanted(true, true, true, 0));    // the raw (TakeDamage) path: no reaction at all
        Assert.False(Wo202Rules.DeathFallWanted(true, true, true, 1));   // queued: the engine's own death reaction plays
        Assert.False(Wo202Rules.DeathFallWanted(true, false, true, 3));  // the joiner never touches the host's bodies
        Assert.False(Wo202Rules.DeathFallWanted(false, true, true, 3));
        Assert.False(Wo202Rules.DeathFallWanted(true, true, false, 3));  // nothing applied
    }

    [Fact]
    public void SaveCheck_AFileTheModMovedOut_IsTheModsFor10Minutes_WhateverItsSpelling()
    {
        // session A's joiner: the Henry pipeline moved playline2/quicksave001.whs out, the check then read it -> "could not repair" toast
        var t = new DateTime(2026, 10, 20, 19, 0, 0, DateTimeKind.Utc);
        Assert.True(Wo202Rules.MovedRecently(t, t.AddSeconds(20)));
        Assert.True(Wo202Rules.MovedRecently(t, t.AddMinutes(10)));
        Assert.False(Wo202Rules.MovedRecently(t, t.AddMinutes(11)));
        Assert.Equal(Wo202Rules.SavePathKey(@"D:\Data\X\Saved Games\kingdomcome2\saves\playline2\quicksave001.whs"),
                     Wo202Rules.SavePathKey("d:/data/x/saved games/KingdomCome2/saves/playline2/QuickSave001.whs"));
        Assert.NotEqual(Wo202Rules.SavePathKey(@"C:\s\playline2\quicksave001.whs"), Wo202Rules.SavePathKey(@"C:\s\playline2\quicksave002.whs"));
    }

    [Fact]
    public void CrashWatch_TheAgentsPartGoesIntoTheDllsReport_OrAFolderOfItsOwn()
    {
        string root = Path.Combine(Path.GetTempPath(), "wo202-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Wo202CrashWatch.NoteApplied("KCD2MP_Test(1)\nsecond line");
            string own = Wo202CrashWatch.WriteAgentPart(root, DateTime.Now, "0xC0000409");
            Assert.EndsWith("-agent", own);
            string text = File.ReadAllText(Path.Combine(own, "agent-statements.txt"));
            Assert.Contains("exit code 0xC0000409", text);
            Assert.Contains("fast-fail", text);
            Assert.Contains("KCD2MP_Test(1) second line", text);
            // a report the DLL wrote just now takes the agent's part
            string dll = Path.Combine(root, "kcdmp-crashes", "crash-20261020-200940-42");
            Directory.CreateDirectory(dll);
            string into = Wo202CrashWatch.WriteAgentPart(root, DateTime.Now, "0xC0000005");
            Assert.Equal(dll, into);
            Assert.True(File.Exists(Path.Combine(dll, "agent-statements.txt")));
            Assert.DoesNotContain("fast-fail", File.ReadAllText(Path.Combine(dll, "agent-statements.txt")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
