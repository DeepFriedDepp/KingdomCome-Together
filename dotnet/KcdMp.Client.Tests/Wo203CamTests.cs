// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Xml;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-203B: the minimal Act I session and the conversation camera (Wo203CamRules), the listener's cancel in the keys pak.</summary>
public class Wo203CamTests
{
    private const string Talk = "New dialogue 'Barbora.trosecko.zebrackaBarbora.hibernable.barbora__welcome_to_ow_dialog (normal - Id: 233) (Actor::RequestDialog)' is starting. Params: souls = 'Ex: Dude; Ex: ttkc_barbora' forced = false, forced decision = none";
    private const string Chat = "New dialogue 'Barbora.open_world.reakce_na_stav_hrace.npc_zdravi_hrace_chatem__straz.npc_zdravi_hrace_chatem__straz (chat - Id: 41) (AI::RequestDialog)' is starting. Params: souls = 'Ex: ttkc_man_3 - meta override: NPC_ZDRAVI_HRACE; Ex: Dude' forced = false";

    [Fact]
    public void OnlyThePlayersOwnTalksAreJoinable()
    {
        var t = Wo203CamRules.ParseTalk(Talk);
        Assert.NotNull(t);
        Assert.Equal((233, "normal"), (t!.Value.Cid, t.Value.Kind));
        Assert.Equal(["ttkc_barbora"], t.Value.Others);
        Assert.True(Wo203CamRules.JoinableKind(t.Value.Kind));
        var c = Wo203CamRules.ParseTalk(Chat);
        Assert.NotNull(c);
        Assert.Equal("chat", c!.Value.Kind);
        Assert.False(Wo203CamRules.JoinableKind(c.Value.Kind));   // an NPC's greeting has no dialogue camera
        Assert.True(Wo203CamRules.JoinableKind("ingame"));
        Assert.False(Wo203CamRules.JoinableKind("ingame monolog"));
        Assert.True(Wo203CamRules.IsCameraActivated("Dialog camera activated."));
        Assert.False(Wo203CamRules.IsCameraActivated("Dialog camera type BONE_MEDIUM is not valid (valid:0, colision:0)"));
        Assert.True(LogTailGameTransport.Wo144Passes("Dialog camera activated."));   // the forward list carries it to the agent
    }

    [Fact]
    public void SessionTextsRoundTrip()
    {
        string a = Wo203CamRules.AnnounceText(233, "normal", true, "ttkc_barbora", ["ttkc_barbora"]);
        Assert.Equal("v1|233|normal|1|-|ttkc_barbora|ttkc_barbora|0,0,0|-", a);
        Assert.True(Wo203CamRules.TryParseAnnounce(a, out int cid, out string kind, out bool j, out string anchor));
        Assert.Equal((233, "normal", true, "ttkc_barbora"), (cid, kind, j, anchor));
        Assert.False(Wo203CamRules.TryParseAnnounce("v1|233|normal|1|-|bad name|x|0,0,0|-", out _, out _, out _, out _));
        string s = Wo203CamRules.SnapshotText(7, "normal", "ttkc_semin", ["ttkc_semin", "ttkc_suk"]);
        Assert.True(Wo203CamRules.TryParseSnapshot(s, out int scid, out string sa));
        Assert.Equal((7, "ttkc_semin"), (scid, sa));
        Assert.True(Wo203CamRules.TryParseReason(Wo203CamRules.EndText(7, "combat"), out int ecid, out string why));
        Assert.Equal((7, "combat"), (ecid, why));
        Assert.True(Wo203CamRules.TryParseReason("v1|7", out _, out why));
        Assert.Equal("ended", why);
        Assert.Equal("v1|7|other", Wo203CamRules.LeaveText(7, "a|b"));   // never a field separator in a field
        Assert.Equal("v1|9|0", Wo203CamRules.JoinText(9, false));
        foreach (string text in new[] { a, s, Wo203CamRules.EndText(7, "ended") })
            Assert.True(text.Length <= Protocol.ConvTextMax && text.All(ch => ch is >= ' ' and <= '~'));
    }

    [Fact]
    public void CameraSamplesRoundTripAndStayShort()
    {
        var xs = new[]
        {
            new Wo203CamRules.CamSample(1, 1_700_000_000_000, 1.234f, -2.5f, 1.7f, 0.1f, -0.2f, 0.3f, 0.927f, 1.1f, false, 2332.51f, 2052.04f, 108.4f, -1.5708f),
            new Wo203CamRules.CamSample(2, 1_700_000_000_033, 1.3f, -2.4f, 1.7f, 0f, 0f, 0.7071f, 0.7071f, 0.7f, true, 2332.51f, 2052.04f, 108.4f, -1.5708f),
            new Wo203CamRules.CamSample(3, 1_700_000_000_066, -0.5f, 0.75f, 1.65f, 0f, 0f, 0f, 1f, 0.9f, false, 2332.51f, 2052.04f, 108.4f, -1.5708f),
        };
        string text = Wo203CamRules.CamText(233, xs);
        Assert.True(text.Length < 400, text);
        Assert.True(text.All(ch => ch is >= ' ' and <= '~'));
        Assert.True(Wo203CamRules.TryParseCam(text, out int cid, out var back));
        Assert.Equal(233, cid);
        Assert.Equal(3, back.Count);
        Assert.Equal(xs[1].T, back[1].T);
        Assert.True(back[1].Cut && !back[0].Cut);
        Assert.Equal(1.234f, back[0].Px, 3);
        Assert.Equal(0.7071f, back[1].Qz, 4);
        Assert.Equal(-1.5708f, back[2].Yaw, 4);
        Assert.Equal(2052.04f, back[0].Ay, 2);
        // the design's original sample (no anchor sub-field) still parses
        Assert.True(Wo203CamRules.TryParseCam("v1|5|1~100~0,0,1.7~0,0,0,1~1.1~0~-", out _, out var old));
        Assert.Equal(0f, old[0].Ax);
        Assert.False(Wo203CamRules.TryParseCam("v1|5|1~100~0,0,1.7~0,0,0,1~9~0~-", out _, out _));          // a FOV no camera has
        Assert.False(Wo203CamRules.TryParseCam("v1|5|1~100~0,0~0,0,0,1~1.1~0~-", out _, out _));           // a short position
        Assert.False(Wo203CamRules.TryParseCam(Wo203CamRules.CamText(5, xs.Concat(xs)), out _, out _));      // at most 3 a message
    }

    [Fact]
    public void ClocksMapThroughTheHostClock()
    {
        // starter: a joiner 40 ms behind the host (offset +40), sample taken 25 ms ago
        long host = Wo203CamRules.HostMs(sampleQpcMs: 9_975, nowQpcMs: 10_000, nowUnixMs: 1_000_000, offsetMs: 40.0);
        Assert.Equal(1_000_015, host);
        // listener: the host itself (offset 0); the same sample arrives 30 ms later
        Assert.Equal(55_000 - 15, Wo203CamRules.LocalQpcMs(host, nowQpcMs: 55_000, nowUnixMs: 1_000_030, offsetMs: 0.0));
        Assert.Equal(55_000 - 15, Wo203CamRules.LocalQpcMs(host, 55_000, 1_000_030, null));       // no sync yet: no offset
        Assert.Equal(55_000 - 15, Wo203CamRules.LocalQpcMs(host, 55_000, 1_000_030, double.NaN));
    }

    [Fact]
    public void NativePayloadsMatchTheDll()
    {
        // op 95 cmd 2 (wo203_cam.cpp op_poll): [n] {t:i64, rel pos 3f, rel quat 4f, fov f, anchor 3f, yaw f, flags:1}
        var p = new byte[1 + 2 * 57];
        p[0] = 2;
        for (int i = 0; i < 2; i++)
        {
            int o = 1 + i * 57;
            BinaryPrimitives.WriteInt64LittleEndian(p.AsSpan(o), 1000 + i);
            for (int k = 0; k < 12; k++) BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 8 + 4 * k), k + 0.5f + i);
            p[o + 56] = (byte)i;
        }
        var d = Wo203CamRules.DecodePoll(p);
        Assert.Equal(2, d.Count);
        Assert.Equal(1001, d[1].QpcMs);
        Assert.Equal((0.5f, 6.5f, 7.5f, 11.5f), (d[0].Sample.Px, d[0].Sample.Qw, d[0].Sample.Fov, d[0].Sample.Yaw));
        Assert.True(d[1].Sample.Cut && !d[0].Sample.Cut);
        Assert.Empty(Wo203CamRules.DecodePoll([]));
        Assert.Single(Wo203CamRules.DecodePoll(p.AsSpan(0, 1 + 57 + 10).ToArray()));   // a cut-short tail is never read
        // op 94 cmd 2 (op_view): [cmd=2][n] {t:i64, pos 3f, quat 4f, fov f, cut:1, anchor 4f}
        var s = d[1].Sample;
        byte[] e = Wo203CamRules.EncodeSamples([(77L, s)]);
        Assert.Equal(2 + 57, e.Length);
        Assert.Equal((byte)2, e[0]);
        Assert.Equal((byte)1, e[1]);
        Assert.Equal(77L, BinaryPrimitives.ReadInt64LittleEndian(e.AsSpan(2)));
        Assert.Equal(s.Fov, BinaryPrimitives.ReadSingleLittleEndian(e.AsSpan(2 + 8 + 28)));
        Assert.Equal((byte)1, e[2 + 8 + 32]);
        Assert.Equal(s.Yaw, BinaryPrimitives.ReadSingleLittleEndian(e.AsSpan(2 + 8 + 32 + 1 + 12)));
        // by name
        Assert.Equal([7, 3, (byte)'a', (byte)'_', (byte)'1'], Wo203CamRules.NameCmd(7, "a_1"));
        Assert.Equal([4, 0, 3, (byte)'a', (byte)'_', (byte)'1'], Wo203CamRules.NameCmd(4, "a_1", 0));
        Assert.Null(Wo203CamRules.NameCmd(7, "a b"));
    }

    [Fact]
    public void WaysOutUseTheDllsCodes()
    {
        // wo203_cam.cpp kWhy: leave end silence combat scene load menu disconnect fault lost taken reenter hit off
        Assert.Equal(14, Wo203CamRules.WhyNames.Length);
        Assert.Equal(0, Wo203CamRules.WhyCode("cancel"));
        Assert.Equal(1, Wo203CamRules.WhyCode("ended"));
        Assert.Equal(2, Wo203CamRules.WhyCode("silence"));
        Assert.Equal(4, Wo203CamRules.WhyCode("scene"));
        Assert.Equal(6, Wo203CamRules.WhyCode("menu"));
        Assert.Equal(12, Wo203CamRules.WhyCode("hit"));
        Assert.Equal("fault", Wo203CamRules.WhyName(8));
        Assert.Equal("other", Wo203CamRules.WhyName(200));
        Assert.Equal((94, 95), (Protocol.Wo203OpConvView, Protocol.Wo203OpConvCamPoll));
    }

    [Fact]
    public void ThePromptsTextIsOneKeyOfTheModsOwn()
    {
        var doc = new XmlDocument();
        doc.LoadXml(Wo203CamRules.LocXml);
        var cells = doc.SelectNodes("/Table/Row/Cell")!;
        Assert.Equal(3, cells.Count);
        Assert.Equal("ui_hud_kct_join_conversation", cells[0]!.InnerText);
        Assert.Equal("Join conversation", cells[1]!.InnerText);
        Assert.Equal("kct_conv.xml", Wo203CamRules.LocFileName);
        Assert.Contains("@ui_hud_kct_join_conversation", File.ReadAllText(Path.Combine(RepoRoot(), "kdcmp", "Data", "Scripts", "Startup", "kdcmp.lua")));
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "KCD2-MP.sln"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("the repository root");
    }

    // ---- the listener's cancel: the game's own dialog actionmap and Back superaction ----

    private const string Profile =
        "<profile version=\"0\">\r\n" +
        "\t<actionmap name=\"interaction\" priority=\"pure_include\" exclusivity=\"0\">\r\n\t\t<action name=\"use\" onPress=\"1\" />\r\n\t</actionmap>\r\n" +
        "\t<actionmap name=\"dialog\" priority=\"minigames\" exclusivity=\"1\">\r\n\t\t<include actionmap=\"navigation\"/>\r\n" +
        "\t\t<action name=\"dialog_cancel\" onPress=\"1\" keyboard=\"_keybinds_ref_\" />\r\n\t</actionmap>\r\n" +
        "\t<actionmap name=\"dialog_crime_list_open\" priority=\"minigames\" exclusivity=\"0\">\r\n\t</actionmap>\r\n</profile>\r\n";
    private const string Supers =
        "<keybinds>\r\n\t<superaction name=\"back\" ui_group=\"general\" keyboard=\"readable\">\r\n\t\t<action name=\"open_menu\" map=\"open_menu\" />\r\n" +
        "\t\t<action name=\"dialog_cancel\" map=\"dialog\" />\r\n\r\n\t\t<control input=\"escape\" controller=\"keyboard\" />\r\n" +
        "\t\t<control input=\"backspace\" controller=\"keyboard\" />\r\n\t</superaction>\r\n\t<superaction name=\"backward\">\r\n\t</superaction>\r\n" +
        // the game's own conflict groups name "back" again (1.5.5: seven self-closing references besides the definition)
        "\t<conflict name=\"actions\">\r\n\t\t<superaction name=\"primary_action\" />\r\n\t\t<superaction name=\"back\" />\r\n\t</conflict>\r\n" +
        "\t<conflict name=\"barber\">\r\n\t\t<superaction name=\"back\"/>\r\n\t</conflict>\r\n</keybinds>\r\n";

    [Fact]
    public void ListenCancelLivesInTheDialogMapOnTheBackKeys()
    {
        string? p = KeybindPak.MergeDialog(Profile, KeybindPak.PatchBody(KeybindPak.EmbeddedPatch("defaultProfile.dialog.xml")), out string why);
        Assert.True(p is not null, why);
        var doc = new XmlDocument();
        doc.LoadXml(p!);
        var a = doc.SelectSingleNode("//actionmap[@name='dialog']/action[@name='kcd2mp_listen_cancel']");
        Assert.NotNull(a);
        Assert.Null(doc.SelectSingleNode("//actionmap[@name!='dialog']/action[@name='kcd2mp_listen_cancel']"));
        Assert.Null(KeybindPak.MergeDialog(p!, KeybindPak.PatchBody(KeybindPak.EmbeddedPatch("defaultProfile.dialog.xml")), out _));   // never twice

        string? s = KeybindPak.MergeBack(Supers, KeybindPak.PatchBody(KeybindPak.EmbeddedPatch("keybindSuperactions.back.xml")), out why);
        Assert.True(s is not null, why);
        doc.LoadXml(s!);
        var inBack = doc.SelectSingleNode("//superaction[@name='back']/action[@name='kcd2mp_listen_cancel']");
        Assert.NotNull(inBack);
        Assert.Equal("dialog", inBack!.Attributes!["map"]!.Value);
        Assert.Equal(2, doc.SelectNodes("//superaction[@name='back']/control")!.Count);   // the keys stay the game's: none added
        Assert.Equal(2, doc.SelectNodes("//control")!.Count);
        Assert.True(s!.IndexOf("kcd2mp_listen_cancel", StringComparison.Ordinal) < s.IndexOf("<control", StringComparison.Ordinal));
        Assert.Null(KeybindPak.MergeBack("<keybinds><superaction name=\"backward\"></superaction></keybinds>", "<action name=\"kcd2mp_listen_cancel\" map=\"dialog\" />", out _));
        Assert.DoesNotContain("kcd2mp_listen_cancel", KeybindPak.Actions);   // not one of the interaction map's
    }
}
