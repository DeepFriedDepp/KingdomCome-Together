// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Xml;
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-204: the agent's pure rules (Wo204Rules). The lines are the engine's own, from session D's logs.</summary>
public class Wo204Tests
{
    private const string HitBark = "New dialogue 'Barbora.open_world.boj_a_zraneni.hekani_pri_utocich_player (ingame - Id: 213) (CombatShout)' is starting. Params: souls = 'Ex: Dude - meta override: COMBAT_ACTOR_SCREAM_ATTACK; Nx: prepadeniNaCeste_bandit_rottenApple2 - meta override: COMBAT_SHOUT_OPPONENT' forced = true, forced decision = none";
    private const string TookHit = "New dialogue 'Barbora.open_world.boj_a_zraneni.inkasovany_zasah_silny_player (ingame - Id: 452) (CombatShout)' is starting. Params: souls = 'Ex: Dude - meta override: COMBAT_VICTIM_SCREAM_RECEIVED_HIT; Nx: prepadeniNaCeste_bandit_rottenApple2 - meta override: COMBAT_SHOUT_OPPONENT' forced = true, forced decision = none";
    private const string Surrender = "New dialogue 'Barbora.open_world.combat.skirmish_vzdavani_chat / Barbora.open_world.combat.skirmish_bojovy_pokrik_chat / Barbora.open_world.combat.nadavani.skirmish_nadavaci_chat / Barbora.open_world.combat.openworldvzdavani.openworld_player_battlecry (chat - Id: 393) (DoChat)' is starting. Params: souls = 'Ex: Dude; Ex: prepadeniNaCeste_bandit_rottenApple2 - meta override: COMBAT_CHAT_OPPONENT' forced = false, forced decision = none";
    private const string Talk = "New dialogue 'Barbora.trosecko.zebrackaBarbora.hibernable.barbora__welcome_to_ow_dialog (normal - Id: 233) (Actor::RequestDialog)' is starting. Params: souls = 'Ex: Dude; Ex: ttkc_barbora' forced = false, forced decision = none";
    private const string Forced = "New dialogue 'Barbora.trosecko.eventPlaces.tzel_tavernInside.pocestny.duelist.dialog_po_duelu (normal - Id: 1285) (ForceDialogNode)' is starting. Params: souls = 'Ex: Dude; Ex: pocestny_duelist' forced = false, forced decision = Barbora.trosecko.eventPlaces.tzel_tavernInside.pocestny.duelist.dialog_po_duelu.dec1";
    private const string Haggle = "New dialogue 'Barbora.open_world.shop.bribe_haggle_negotiation_vyjednavani_smlouvani_podplaceni.smlouvani_v_obchode_muz (normal - Id: 656) (Haggle)' is starting. Params: souls = 'Ex: Dude; Ex: ttkc_man_11 - meta override: SMLOUVANI, VYJEDNAVANI' forced = true, forced decision = none";
    private const string Gossip = "New dialogue 'Barbora.trosecko.kejkliri.bezici_cast.dialogy_md_o_samote.dialogy_md_o_samote (ingame - Id: 15) (AI::RequestDialog)' is starting. Params: souls = 'Ex: tzel_david - meta override: GOSSIP; Ex: tzel_michal - meta override: GOSSIP' forced = false, forced decision = none";

    [Fact]
    public void TheHeadIsReadFromTheEnginesLine()
    {
        Assert.Equal(("Barbora.open_world.boj_a_zraneni.hekani_pri_utocich_player", "ingame", "CombatShout"), Wo204Rules.ParseDialogueHead(HitBark));
        Assert.Equal(("Barbora.open_world.combat.skirmish_vzdavani_chat", "chat", "DoChat"), Wo204Rules.ParseDialogueHead(Surrender));
        Assert.Equal(("Barbora.trosecko.eventPlaces.tzel_tavernInside.pocestny.duelist.dialog_po_duelu", "normal", "ForceDialogNode"), Wo204Rules.ParseDialogueHead(Forced));
        Assert.Null(Wo204Rules.ParseDialogueHead("[ID: 233] Dialog ending"));
        Assert.Null(Wo204Rules.ParseDialogueHead("New dialogue 'broken"));
    }

    [Fact]
    public void CombatBarksAreNeverConversations()
    {
        // session D: the hit barks and war cries had the player and an enemy as souls -- 422 were announced, 415 offered
        Assert.NotNull(Wo203ConvRules.ParseOwnConversation(HitBark));   // the souls alone would have made it one
        Assert.False(Wo204Rules.IsConversationLine(HitBark));
        Assert.False(Wo204Rules.IsConversationLine(TookHit));
        Assert.False(Wo204Rules.IsConversationLine(Surrender));
        Assert.False(Wo204Rules.IsConversationLine(Gossip));
        Assert.Null(Wo204Rules.IsConversationLine("Dialog camera activated."));
    }

    [Fact]
    public void RealTalksAreConversations()
    {
        Assert.True(Wo204Rules.IsConversationLine(Talk));
        Assert.True(Wo204Rules.IsConversationLine(Forced));
        Assert.True(Wo204Rules.IsConversationLine(Haggle));
        Assert.True(Wo204Rules.IsConversation("Barbora.trosecko.quest.x", "normal", "AI::RequestDialog"));   // an NPC opening a full dialogue
    }

    [Fact]
    public void ABarkFamilyStaysABarkWhateverItsKind()
    {
        Assert.False(Wo204Rules.IsConversation("Barbora.open_world.boj_a_zraneni.vzdavani", "normal", "Actor::RequestDialog"));
        Assert.False(Wo204Rules.IsConversation("Barbora.open_world.combat.openworldvzdavani.x", "normal", "ForceDialogNode"));
        Assert.False(Wo204Rules.IsConversation("Barbora.trosecko.quest.x", "ingame", "Actor::RequestDialog"));
        Assert.False(Wo204Rules.IsConversation("Barbora.trosecko.quest.x", "normal", "SomethingNew"));   // a positive list: unknown origins stay out
    }

    [Fact]
    public void TheSwitchesParse()
    {
        Assert.True(Wo204Rules.TryParseCfg("conv_no_barks=off", out string n, out bool on) && n == "conv_no_barks" && !on);
        Assert.False(Wo204Rules.TryParseCfg("nope=on", out _, out _));
        Assert.False(Wo204Rules.TryParseCfg("conv_no_barks=maybe", out _, out _));
        Assert.All(Wo204Rules.SwitchNames, s => Assert.True(Wo204Rules.DefaultOn(s)));
    }

    [Fact]
    public void Join_ThePausedWorldWaitsForTheJoinersProgress()
    {
        Assert.False(Wo204Rules.JoinLoadSilent(59_999));
        Assert.True(Wo204Rules.JoinLoadSilent(60_000));
        Assert.True(Wo204Rules.JoinProgressEveryMs * 10 <= Wo204Rules.JoinLoadSilenceS * 1000);   // ten reports fit in the silence window
        string t = Wo204Rules.JoinProgressText(0x00ab12cd, "loading", 42);
        Assert.Equal("v1|00ab12cd|loading|42", t);
        Assert.True(Wo204Rules.TryParseJoinProgress(t, out uint id, out string where, out int secs) && id == 0x00ab12cd && where == "loading" && secs == 42);
        Assert.False(Wo204Rules.TryParseJoinProgress("v1|xyz|loading|1", out _, out _, out _));
        Assert.False(Wo204Rules.TryParseJoinProgress("v1|00ab12cd|lo ading|1", out _, out _, out _));
        Assert.False(Wo204Rules.TryParseJoinProgress("v2|00ab12cd|loading|1", out _, out _, out _));
    }

    [Fact]
    public void Marks_CarryTheirNoteToThePartner()
    {
        Assert.Equal("no weapon on bandit", Wo204Rules.MarkNoteOf("odd note=no weapon on bandit"));
        Assert.Equal("", Wo204Rules.MarkNoteOf("odd"));
        Assert.Equal("v1|ab12cd34ef|a b", Wo204Rules.MarkNoteText("ab12cd34ef", "a <b>"));
        Assert.True(Wo204Rules.TryParseMarkNote("v1|ab12cd34ef|character moving while sitting", out string id, out string note));
        Assert.Equal(("ab12cd34ef", "character moving while sitting"), (id, note));
        Assert.False(Wo204Rules.TryParseMarkNote("v1|AB|x", out _, out _));
        Assert.False(Wo204Rules.TryParseMarkNote("v1|ab12cd34ef|<>", out _, out _));   // nothing left of it
        Assert.Equal(120, Wo204Rules.CleanNote(new string('x', 300)).Length);
        Assert.Equal("mark-note", KcdMp.Wire.Protocol.W164KindName(KcdMp.Wire.Protocol.W164MarkNote));
    }

    [Fact]
    public void Unstick_TheStuckCopyIsReleasedPaced()
    {
        const string line = "[Error] /ai//(NPC)ttkc_marusa/NPC Context [NPCContext]:NPC state search has failed!";
        Assert.Equal("ttkc_marusa", Wo204Rules.StateSearchFailedNpc(line));
        Assert.True(LogTailGameTransport.Wo144Passes(line));   // the engine line reaches the agent
        Assert.Null(Wo204Rules.StateSearchFailedNpc("[Error] [NPCStateSearch]:NPC state search failed: can't find a path from actions."));
        Assert.True(Wo204Rules.UnstickDue("ttkc_marusa", 5, 200_000));
        Assert.False(Wo204Rules.UnstickDue("ttkc_marusa", 4, 200_000));
        Assert.False(Wo204Rules.UnstickDue("ttkc_marusa", 9, 60_000));   // released a minute ago: not again yet
        Assert.False(Wo204Rules.UnstickDue("kcd2mp_1", 50, 200_000));    // a partner's figure is his player's, never released here
    }

    [Fact]
    public void Blows_AJoinerBlockStaysABlock_AndAStallIsTold()
    {
        Assert.True(Wo204Rules.JoinerJudgedBlocked(0f, 30f));      // session D's "st30.0/hp0.0"
        Assert.False(Wo204Rules.JoinerJudgedBlocked(12.3f, 40f));  // a real hit: the host's engine (its reaction) as before
        Assert.False(Wo204Rules.JoinerJudgedBlocked(0f, 0f));      // nothing at all
        Assert.True(Wo204Rules.BlowLate(2000, 100));               // came right after a 2 s stall of the link
        Assert.False(Wo204Rules.BlowLate(2000, 900));
        Assert.False(Wo204Rules.BlowLate(120, 50));
    }

    [Fact]
    public void TheDllsSwitchWord()
    {
        Assert.Equal(3u, Wo204Rules.ConfigBits(_ => true));
        Assert.Equal(2u, Wo204Rules.ConfigBits(n => n != "copy_seat_hold"));
        Assert.Equal(0u, Wo204Rules.ConfigBits(_ => false));
    }

    /// <summary>WO-204 C3, replayed (research; runs only with KCDMP_C3_LOGS = kcd.log paths separated by ';'): every engine dialogue start
    /// of a real session through 0.62.5's rule (the player and another soul, kind normal or ingame) and through III.1's; the counts go to
    /// the test output.</summary>
    [Fact]
    public void C3_Replay_OfARealSession()
    {
        string? logs = Environment.GetEnvironmentVariable("KCDMP_C3_LOGS");
        if (string.IsNullOrEmpty(logs)) return;
        foreach (string path in logs.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int before = 0, after = 0, lines = 0;
            foreach (string line in File.ReadLines(path))
            {
                if (!line.StartsWith("New dialogue '", StringComparison.Ordinal)) continue;
                lines++;
                if (Wo203CamRules.ParseTalk(line) is { } t && Wo203CamRules.JoinableKind(t.Kind)) before++;
                if (Wo203CamRules.ParseTalk(line) is not null && Wo204Rules.IsConversationLine(line) == true) after++;
            }
            Console.WriteLine($"WO204-C3 {Path.GetFileName(Path.GetDirectoryName(path))}: dialogue starts={lines} announced_0.62.5={before} announced_now={after}");
            Assert.True(after <= before);
        }
    }

    [Fact]
    public void Conv_LinesOptionsVotes_TheWire()
    {
        Assert.Equal("zpet_hejtman_s_tak_co_syn_JIUh", Wo204Rules.StringNameOf("Localization/dialog/trosecko/x/dude_zpet_hejtman_s_tak_co_syn_JIUh"));
        Assert.Equal("s_zacatek__semin_1_KWg9", Wo204Rules.StringNameOf(@"Localization\dialog\a\sems_s_zacatek__semin_1_KWg9.ogg"));
        var l = new Wo204Rules.ConvLineMsg(233, 4, "@", "Partner|x", true, "zpet_hejtman_s_tak_co_syn_JIUh", "Localization/dialog/a/dude_zpet", 2400, false);
        string t = Wo204Rules.LineText(l, 1234);
        Assert.True(Wo204Rules.TryParseLine(t, out var back));
        Assert.Equal(l with { Name = "Partner/x" }, back);   // a field never holds '|'
        Assert.True(Wo204Rules.TryParseLine(Wo204Rules.LineText(l with { Earlier = true }, 0), out var e) && e.Earlier);
        Assert.False(Wo204Rules.TryParseLine("v1|233|4|@|n|1|bad key<>|p|100|0|0", out _));
        Assert.False(Wo204Rules.TryParseLine("v2|233|4|@|n|1|k|p|100|0|0", out _));

        // the DLL's op 91: two rows (a loc key; a shown text with marks and an icon), then the snapshot's sequence and side
        var p = new List<byte> { 1, 0, 255, 2 };
        void Row(bool used, string icon, int diff, int val, string text)
        {
            p.Add((byte)(used ? 1 : 0)); p.Add((byte)icon.Length); p.AddRange(System.Text.Encoding.ASCII.GetBytes(icon));
            p.AddRange(BitConverter.GetBytes(diff)); p.AddRange(BitConverter.GetBytes(val)); p.Add(0);
            var tb = System.Text.Encoding.UTF8.GetBytes(text); p.Add((byte)tb.Length); p.AddRange(tb);
        }
        Row(false, "", 0, 0, "@ui_barbora___seq2_7WVl");
        Row(true, "skill_speech", 2, 5, "<font color='#ff0'>Pay her</font>");
        p.AddRange(BitConverter.GetBytes(7u)); p.Add(1);
        var rows = Wo204Rules.ParseOptionsPoll(p.ToArray(), out int cur, out uint seq, out bool right);
        Assert.NotNull(rows);
        Assert.Equal((0, 7u, true), (cur, seq, right));
        Assert.Equal("ui_barbora___seq2_7WVl", rows![0].Prompt);
        Assert.Equal(("=Pay her", true, "skill_speech", 2, 5), (rows[1].Prompt, rows[1].Used, rows[1].Icon, rows[1].Difficulty, rows[1].Value));
        Assert.Null(Wo204Rules.ParseOptionsPoll([1, 0, 255, 3, 0], out _, out _, out _));   // a short reply: nothing, never a throw

        string o = Wo204Rules.OptionsText(233, 2, 0, rows);
        Assert.True(Wo204Rules.TryParseOptions(o, out int ocid, out int odec, out var orows) && ocid == 233 && odec == 2 && orows.Count == 2);
        Assert.Equal(rows, orows);
        Assert.Equal("0~ui_barbora___seq2_7WVl~0~-~0~0;1~=Pay her~1~skill_speech~2~5", Wo204Rules.RowsForLua(rows));
        Assert.True(Wo204Rules.TryParseVote(Wo204Rules.VoteText(233, 2, 1), out int vc, out int vd, out int vi) && (vc, vd, vi) == (233, 2, 1));
        Assert.True(Wo204Rules.TryParseVote(Wo204Rules.VoteText(233, 2, -1), out _, out _, out int cleared) && cleared == -1);
        Assert.False(Wo204Rules.TryParseVote("v1|233|2|9", out _, out _, out _));
        Assert.Equal(1, Wo204Rules.TopicSelected("Topic index '1' selected."));
        Assert.Equal(0, Wo204Rules.TopicSelected("Topic index '0' selected.\r"));   // the live line's trailing character (V1s)
        Assert.Equal(-1, Wo204Rules.TopicSelected("Topic index '1' activated."));
        Assert.True(LogTailGameTransport.Wo144Passes("Topic index '1' selected."));   // the engine line reaches the agent
    }

    [Fact]
    public void Trialog_RolesFromTheDialoguesOwnFile()
    {
        const string xml = "\uFEFF<?xml version=\"1.0\" encoding=\"utf-8\"?><Database Name=\"x\"><Skald>"
            + "<Dialogue Name=\"other\"><SelectedSouls><SelectedSoul Role=\"HENRY\" /><SelectedSoul Role=\"KUBA\" Soul=\"tsem_kuba\" /></SelectedSouls></Dialogue>"
            + "<Dialogue Name=\"porada\"><SelectedSouls><SelectedSoul Role=\"HENRY\" Voice=\"tomMcKay\" /><SelectedSoul Role=\"SEMINE\" Soul=\"tsem_seminsr\" />"
            + "<SelectedSoul Role=\"GNARLY\" /></SelectedSouls></Dialogue></Skald></Database>";
        var roles = Wo204Rules.RolesOf(xml, "porada");
        Assert.Equal(new (string, string?)[] { ("HENRY", null), ("SEMINE", "tsem_seminsr"), ("GNARLY", null) }, roles);
        // the engine's log order: Gnarly first -- the bound role keeps its soul, the unbound one takes what is left
        var map = Wo204Rules.MapRoles(roles, ["tsem_gnarly", "tsem_seminsr"]);
        Assert.Equal("tsem_seminsr", map["SEMINE"]);
        Assert.Equal("tsem_gnarly", map["GNARLY"]);
        Assert.False(map.ContainsKey("HENRY"));   // the player's own role stays the starter's figure
        Assert.Empty(Wo204Rules.RolesOf("<not xml", "porada"));
        Assert.Equal("Quests/Final/Barbora/open_world/shop/nakupovani_z_dialogu_muz.xml", Wo204Rules.DialogFile("Barbora.open_world.shop.nakupovani_z_dialogu_muz"));
    }

    [Fact]
    public void Keys_TheListenersOptionsActionsLiveInTheGamesOwnSuperactions()
    {
        const string supers = "<keybinds>\r\n" +
            "\t<superaction name=\"up\" ui_group=\"general\" keyboard=\"writeable\">\r\n\t\t<action name=\"up\" map=\"navigation_up\" />\r\n\t\t<control input=\"w\" controller=\"keyboard\" />\r\n\t</superaction>\r\n" +
            "\t<superaction name=\"down\" ui_group=\"general\" keyboard=\"writeable\">\r\n\t\t<action name=\"down\" map=\"navigation_down\" />\r\n\t\t<control input=\"s\" controller=\"keyboard\" />\r\n\t</superaction>\r\n" +
            "\t<superaction name=\"primary_action\" ui_group=\"general\" keyboard=\"writeable\">\r\n\t\t<action name=\"dialog_confirm\" map=\"dialog\" />\r\n\t\t<control input=\"e\" controller=\"keyboard\" />\r\n\t</superaction>\r\n" +
            "\t<superaction name=\"confirm_dialog_extra\" ui_group=\"\" keyboard=\"hidden\">\r\n\t\t<action name=\"dialog_confirm_mouse\" map=\"dialog\" />\r\n\t\t<control input=\"mouse1\" controller=\"keyboard\" />\r\n\t</superaction>\r\n" +
            "\t<superaction name=\"ui_and_dialog_wheel_up\" ui_group=\"\" keyboard=\"hidden\">\r\n\t\t<action name=\"scroll_up\" map=\"navigation_up\" />\r\n\t\t<control input=\"mwheel_up\" controller=\"keyboard\" />\r\n\t</superaction>\r\n" +
            "\t<superaction name=\"ui_and_dialog_wheel_down\" ui_group=\"\" keyboard=\"hidden\">\r\n\t\t<action name=\"scroll_down\" map=\"navigation_down\" />\r\n\t\t<control input=\"mwheel_down\" controller=\"keyboard\" />\r\n\t</superaction>\r\n" +
            "\t<conflict name=\"directions\">\r\n\t\t<superaction name=\"up\" />\r\n\t\t<superaction name=\"down\" />\r\n\t</conflict>\r\n</keybinds>\r\n";
        string? m = supers;
        foreach (var (sup, act) in KeybindPak.ListenOptionActions.Concat(KeybindPak.ListenWheelActions))   // WO-210 F.1: the wheel too
        {
            m = KeybindPak.MergeIntoSuperaction(m!, sup, KeybindPak.ListenLine(act, sup), [act], out string why);
            Assert.True(m is not null, sup + ": " + why);
        }
        var doc = new XmlDocument();
        doc.LoadXml(m!);
        Assert.NotNull(doc.SelectSingleNode("//superaction[@name='up']/action[@name='kcd2mp_listen_up'][@map='dialog']"));
        Assert.NotNull(doc.SelectSingleNode("//superaction[@name='down']/action[@name='kcd2mp_listen_down'][@map='dialog']"));
        Assert.NotNull(doc.SelectSingleNode("//superaction[@name='primary_action']/action[@name='kcd2mp_listen_confirm']"));
        Assert.NotNull(doc.SelectSingleNode("//superaction[@name='confirm_dialog_extra']/action[@name='kcd2mp_listen_confirm_mouse']"));   // WO-210 F.1: its own action
        Assert.NotNull(doc.SelectSingleNode("//superaction[@name='ui_and_dialog_wheel_up']/action[@name='kcd2mp_listen_wheel_up'][@map='dialog']"));
        Assert.NotNull(doc.SelectSingleNode("//superaction[@name='ui_and_dialog_wheel_down']/action[@name='kcd2mp_listen_wheel_down'][@map='kcd2mp_listen']"));
        // WO-207 A.4: each also in the listener's own map
        foreach (var (sup, act) in KeybindPak.ListenOptionActions.Concat(KeybindPak.ListenWheelActions))
            Assert.NotNull(doc.SelectSingleNode($"//superaction[@name='{sup}']/action[@name='{act}'][@map='kcd2mp_listen']"));
        Assert.Equal(6, doc.SelectNodes("//control")!.Count);   // the keys stay the game's: none added
        Assert.Null(doc.SelectSingleNode("//conflict/action"));    // never into the conflict groups' references
        Assert.Null(KeybindPak.MergeIntoSuperaction(m!, "up", KeybindPak.ListenLine("kcd2mp_listen_up", "up"), ["kcd2mp_listen_up"], out _));   // never twice
        Assert.Null(KeybindPak.MergeIntoSuperaction(m!, "nope", KeybindPak.ListenLine("kcd2mp_listen_up", "nope"), ["kcd2mp_listen_up"], out _));
        Assert.DoesNotContain("--", KeybindPak.ListenLine("kcd2mp_listen_up", "up").Replace("<!--", "").Replace("-->", ""));
    }

    [Fact]
    public void The_mod_is_loaded_when_its_init_line_is_in_this_launchs_log()
    {
        // the 0.62.7 field bundle's own lines: the first init line alone does not count, a quoted copy does not count
        Assert.True(LogTailGameTransport.ModInitIn(new StringReader("Loading and executing script file 'Scripts/Startup/kdcmp.lua'...\n[KCD2-MP] === MOD INIT ===\n[KCD2-MP] MOD INIT t=21.356\n")));
        Assert.False(LogTailGameTransport.ModInitIn(new StringReader("[KCD2-MP] === MOD INIT ===\n")));
        Assert.False(LogTailGameTransport.ModInitIn(new StringReader("[transport] (is the mod installed and loaded? look for '[KCD2-MP] MOD INIT' in kcd.log)\n")));
        Assert.False(LogTailGameTransport.ModInitIn(new StringReader("")));
        // read while the game holds the file open for writing; a missing file is not a loaded mod
        string path = Path.Combine(Path.GetTempPath(), "w204-modinit-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            using (var w = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
            {
                var b = System.Text.Encoding.UTF8.GetBytes("Gameplay started\n[KCD2-MP] MOD INIT t=21.356\n");
                w.Write(b, 0, b.Length); w.Flush();
                Assert.True(LogTailGameTransport.ModInitInLog(path));
            }
            Assert.False(LogTailGameTransport.ModInitInLog(path + ".missing"));
        }
        finally { File.Delete(path); }
    }
}
