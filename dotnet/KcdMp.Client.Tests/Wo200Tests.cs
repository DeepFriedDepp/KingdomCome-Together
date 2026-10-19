// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-200: the switches, the replay / ack / item-count texts, the closure-marked change frame, the vitals and mount rows, the
/// clock's slew, this player's own conversation lines, and the static closure of an out-port (a waiter's OnFinished included).</summary>
public class Wo200Tests
{
    [Fact]
    public void Switches_TwelveNames_AllOn()
    {
        Assert.Equal(12, Wo200Rules.SwitchNames.Length);   // WO-201: Act V's three placeholders are WO-201's switches now
        foreach (var n in Wo200Rules.SwitchNames)
        {
            Assert.True(Wo200Rules.TryParseCfg(n + "=off", out var name, out bool on));
            Assert.Equal(n, name);
            Assert.False(on);
            Assert.True(Wo200Rules.DefaultOn(n));
        }
        Assert.False(Wo200Rules.TryParseCfg("s1_replay=maybe", out _, out _));
        Assert.False(Wo200Rules.TryParseCfg("no_such_switch=on", out _, out _));
        Assert.False(Wo200Rules.TryParseCfg("=on", out _, out _));
    }

    [Fact]
    public void ConfigBits_MatchTheDll()
    {
        Assert.Equal(7, Wo200Rules.TapBits(true, true));     // capture 1, quiet 2, waiters 4
        Assert.Equal(2, Wo200Rules.TapBits(false, true));    // no replay: no capture, no waiter continuation
        Assert.Equal(7, Wo200Rules.GateBits(true, true, true));
        Assert.Equal(1, Wo200Rules.GateBits(true, false, false));
    }

    [Fact]
    public void ReplayText_RoundTrips_AndRefusesJunk()
    {
        const string path = "Barbora.trosecko.zbranePanaSemina.h.na_semine.odjezd_ze_semina.zacatek_jizdy__semin_1";
        string t = Wo200Rules.ReplayText(0x80000001u, 1, "jindra_ma_nasledovat_semina_a_suka", path);
        Assert.True(Wo200Rules.TryParseReplayText(t, out uint c, out uint s, out string port, out string p));
        Assert.Equal(0x80000001u, c);
        Assert.Equal(1u, s);
        Assert.Equal("jindra_ma_nasledovat_semina_a_suka", port);
        Assert.Equal(path, p);
        Assert.False(Wo200Rules.TryParseReplayText("1 2 port", out _, out _, out _, out _));
        Assert.False(Wo200Rules.TryParseReplayText("x 2 port a.b", out _, out _, out _, out _));
        Assert.False(Wo200Rules.TryParseReplayText("1 2 po;rt a.b", out _, out _, out _, out _));
    }

    [Fact]
    public void Ack_VerdictsAndFallback()
    {
        Assert.Equal("ran", Wo200Rules.VerdictOf(1));
        Assert.Equal("refused-notloaded", Wo200Rules.VerdictOf(0));
        Assert.Equal("refused-notloaded", Wo200Rules.VerdictOf(4));   // hibernated = not loaded for the joiner's purpose
        Assert.Equal("refused-noport", Wo200Rules.VerdictOf(3));
        Assert.Equal("unarmed", Wo200Rules.VerdictOf(6));
        string a = Wo200Rules.AckText(5, "ran", 12, "Barbora.x.y");
        Assert.True(Wo200Rules.TryParseAckText(a, out uint c, out string v, out int rt, out string p));
        Assert.Equal((5u, "ran", 12, "Barbora.x.y"), (c, v, rt, p));
        Assert.False(Wo200Rules.TryParseAckText("5 maybe 12 Barbora.x.y", out _, out _, out _, out _));
        Assert.False(Wo200Rules.FallsBack("ran"));
        Assert.False(Wo200Rules.FallsBack("queued"));
        foreach (var r in new[] { "refused-notloaded", "refused-noport", "fault", "off", "unarmed" }) Assert.True(Wo200Rules.FallsBack(r));
    }

    [Fact]
    public void ItemCount_RoundTrips_AndBounds()
    {
        var g = Guid.Parse("41d4149c-0000-4000-8000-000000000001");
        Assert.True(Wo200Rules.TryParseItemCountText(Wo200Rules.ItemCountText(2, g), out int n, out Guid cls));
        Assert.Equal((2, g), (n, cls));
        Assert.True(Wo200Rules.TryParseItemCountText("0 " + g.ToString("D"), out n, out _));
        Assert.Equal(0, n);   // a removal travels as count 0
        Assert.False(Wo200Rules.TryParseItemCountText("-1 " + g.ToString("D"), out _, out _));
        Assert.False(Wo200Rules.TryParseItemCountText("10000 " + g.ToString("D"), out _, out _));
        Assert.False(Wo200Rules.TryParseItemCountText("1 " + Guid.Empty.ToString("D"), out _, out _));
        Assert.False(Wo200Rules.TryParseItemCountText("1", out _, out _));
        // the class list to the game's script stays under the console ceiling (~2,100 encoded characters)
        int encoded = Wo200Rules.QClassChunk * (36 + 3) + 80;
        Assert.True(encoded < 2000, encoded.ToString());
    }

    [Fact]
    public void HostClosure_SeparatesPeers_DropsTheLocalBit()
    {
        uint a = Wo200Rules.HostClosure(1, 0x80000001u), b = Wo200Rules.HostClosure(2, 0x80000001u);
        Assert.NotEqual(a, b);
        Assert.Equal(0u, a & 0x80000000u);
        Assert.Equal(0x01000001u, a);
    }

    [Fact]
    public void TalkLines_OwnConversationOnly()
    {
        const string trialog = "New dialogue 'Barbora.trosecko.zbranePanaSemina.h.v_troskovicich.debata_suk__semin__rychtar_je_debil.debata_suk__semin__rychtar_je_debil (normal - Id: 2699) (ForceDialogNode)' is starting. Params: souls = 'Ex: Dude; Ex: tsem_seminsr; Ex: tsem_suk' forced = false, forced decision = x";
        Assert.True(Wo200Rules.TryParseTalkStart(trialog, out var names, out int id));
        Assert.Equal(2699, id);
        Assert.Single(names);
        Assert.True(Wo200Rules.PathMatchesTalk("Barbora.trosecko.zbranePanaSemina.h.v_troskovicich.debata_suk__semin__rychtar_je_debil.debata_suk__semin__rychtar_je_debil", names[0]));
        const string bark = "New dialogue 'Barbora.open_world.boj.x (ingame - Id: 7) (CombatShout)' is starting. Params: souls = 'Ex: tsem_seminsr - meta override: X; Nx: kcd2mp_0' forced = true";
        Assert.False(Wo200Rules.TryParseTalkStart(bark, out _, out _));   // no Dude
        const string mono = "New dialogue 'Barbora.a.b (ingame monolog - Id: 9) (AI::DoMonologue)' is starting. Params: souls = 'Ex: Dude; Ex: x'";
        Assert.False(Wo200Rules.TryParseTalkStart(mono, out _, out _));
        Assert.True(Wo200Rules.TryParseTalkEnd("[ID: 2699] Dialog ending [Ex0: Dude Ex1: tsem_seminsr state: CLEANUP flags: 1]", out int e));
        Assert.Equal(2699, e);
        Assert.True(Wo200Rules.TryParseTalkEnd("[ID: 12] Dialog interrupted. [Ex0: Dude state: WAITING_FOR_TWINS flags: 2]", out e));
        Assert.Equal(12, e);
    }

    [Fact]
    public void Noise_RoundTrips_AndRefusesJunk()
    {
        Assert.True(Wo200Rules.TryParseNoise(Wo200Rules.NoiseText("lockpick", 2041.5f, 1816.25f, 76.9f), out string k, out float x, out float y, out float z));
        Assert.Equal(("lockpick", 2041.5f, 1816.25f, 76.9f), (k, x, y, z));
        Assert.True(Wo200Rules.TryParseNoise("door 1 2 3", out k, out _, out _, out _));
        Assert.Equal("door", k);
        Assert.False(Wo200Rules.TryParseNoise("scream 1 2 3", out _, out _, out _, out _));   // only the engine's sounds the WO names
        Assert.False(Wo200Rules.TryParseNoise("door 1 2", out _, out _, out _, out _));
        Assert.False(Wo200Rules.TryParseNoise("door NaN 2 3", out _, out _, out _, out _));
        Assert.False(Wo200Rules.TryParseNoise("door 1e9 2 3", out _, out _, out _, out _));
        Assert.False(Wo200Rules.TryParseNoise(null, out _, out _, out _, out _));
    }

    [Fact]
    public void RejoinText_SaysHowOld()
    {
        Assert.Equal("Continue with your character (saved just now)", Wo200Rules.RejoinText(TimeSpan.FromSeconds(20)));
        Assert.Equal("Continue with your character (saved 1 min ago)", Wo200Rules.RejoinText(TimeSpan.FromSeconds(70)));
        Assert.Equal("Continue with your character (saved 18 min ago)", Wo200Rules.RejoinText(TimeSpan.FromMinutes(18.2)));
        Assert.Equal("Continue with your character (saved 3 h ago)", Wo200Rules.RejoinText(TimeSpan.FromHours(3.1)));
        Assert.Equal("Continue with your character (saved 4 days ago)", Wo200Rules.RejoinText(TimeSpan.FromDays(4)));
        Assert.Equal("Continue with your character (saved just now)", Wo200Rules.RejoinText(TimeSpan.FromMinutes(-5)));   // a clock step back
    }

    [Fact]
    public void ClockSlew_GentleAndBounded()
    {
        Assert.Equal(15.0, Wo200Rules.SlewRatio(15.0, 2));        // within 4 s: the host's own pace
        Assert.Equal(15.0, Wo200Rules.SlewRatio(15.0, 900));      // a gap of a quarter hour is WO-140's / WO-133's
        Assert.True(Wo200Rules.SlewRatio(15.0, 30) < 15.0);       // ahead: slower
        Assert.True(Wo200Rules.SlewRatio(15.0, -30) > 15.0);      // behind: faster
        Assert.Equal(15.0 * 0.75, Wo200Rules.SlewRatio(15.0, 500), 2);   // never more than 25 %
        Assert.Equal(15.0 * 1.25, Wo200Rules.SlewRatio(15.0, -500), 2);
        Assert.Equal(15.0, Wo200Rules.SlewRatio(15.0, double.NaN));
    }

    private static byte[] ChangeFrame(byte flags, string port, string type, string path, int questLen, uint? closure)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(7u); w.Write(flags); w.Write(0); w.Write(1);
        w.Write((byte)port.Length); w.Write(Encoding.ASCII.GetBytes(port));
        w.Write((byte)type.Length); w.Write(Encoding.ASCII.GetBytes(type));
        w.Write((ushort)path.Length); w.Write(Encoding.ASCII.GetBytes(path));
        w.Write((ushort)questLen);
        if (closure is uint c) w.Write(c);
        return ms.ToArray();
    }

    [Fact]
    public void ChangeFrame_ClosureFlag_CarriesItsId()
    {
        const string path = "Barbora.trosecko.zbranePanaSemina.h.na_semine.SaS_docekali";
        var f = ChangeFrame(QuestChange.FClosure | QuestChange.FNewOk, "Set", "State", path, 33, 0x01000001u);
        Assert.True(QuestChange.TryParse(f, out var c));
        Assert.True(c.InClosure);
        Assert.Equal(0x01000001u, c.Closure);
        var plain = ChangeFrame(QuestChange.FNewOk, "Set", "State", path, 33, null);
        Assert.True(QuestChange.TryParse(plain, out var p));
        Assert.False(p.InClosure);
        Assert.Equal(0u, p.Closure);
        // a closure flag without its id (or an id without the flag) is a malformed frame
        Assert.False(QuestChange.TryParse(ChangeFrame(QuestChange.FClosure, "Set", "State", path, 33, null), out _));
        Assert.False(QuestChange.TryParse(ChangeFrame(0, "Set", "State", path, 33, 5u), out _));
    }

    [Fact]
    public void VitalsAndMountRows_RoundTrip_AndRefuseJunk()
    {
        var body = ExtraCodec.BuildBody(Protocol.ExtraKindVitals, new[] { new ExtraRow { Name = "tsem_seminsr", Health = 87.5f, Stamina = 112f } });
        Assert.True(ExtraCodec.TryDecode(body, out byte k, out var rows));
        Assert.Equal(Protocol.ExtraKindVitals, k);
        Assert.Equal(("tsem_seminsr", 87.5f, 112f), (rows[0].Name, rows[0].Health, rows[0].Stamina));
        var mount = ExtraCodec.BuildBody(Protocol.ExtraKindMount, new[] { new ExtraRow { Name = "tsem_suk", Target = "tsem_sukHorse" }, new ExtraRow { Name = "tsem_seminsr", Target = "" } });
        Assert.True(ExtraCodec.TryDecode(mount, out k, out rows));
        Assert.Equal(Protocol.ExtraKindMount, k);
        Assert.Equal("tsem_sukHorse", rows[0].Target);
        Assert.Equal("", rows[1].Target);   // on foot
        Assert.Throws<ArgumentException>(() => ExtraCodec.BuildBody(Protocol.ExtraKindVitals, new[] { new ExtraRow { Name = "x", Health = float.NaN } }));
        var bad = (byte[])body.Clone();
        BinaryPrimitives.WriteSingleLittleEndian(bad.AsSpan(bad.Length - 4), 1e9f);   // a stamina no soul has
        Assert.False(ExtraCodec.TryDecode(bad, out _, out _));
        var beyond = (byte[])body.Clone();
        beyond[0] = (byte)(Protocol.ExtraKindMount + 1);
        Assert.False(ExtraCodec.TryDecode(beyond, out _, out _));
    }

    [Fact]
    public void QuestGraph_FollowsStates_Waiters_SubModulesAndOutputs()
    {
        // the departure's shape: a dialogue's out-port -> a State and a waiter's Enqueue; the waiter's OnFinished -> a sub-module's In;
        // inside it a State and an Output back to the parent's own port, which feeds another State
        var mods = new Dictionary<string, string>
        {
            ["Q.h"] = """
                <Module><Nodes>
                  <Dialog Name="talk"/>
                  <State Name="metThem"><Edge From="talk.out1" To="SetDone"/></State>
                  <SceneFinishedWaiter Name="waiter"><Edge From="talk.out1" To="Enqueue"/></SceneFinishedWaiter>
                  <Module Name="depart"><Edge From="waiter.OnFinished" To="Go"/></Module>
                  <State Name="afterDepart"><Edge From="depart.Left" To="SetActive"/></State>
                  <State Name="unrelated"><Edge From="talk.out2" To="SetDone"/></State>
                </Nodes></Module>
                """,
            ["Q.h.depart"] = """
                <Module><Nodes>
                  <State Name="riding"><Edge From="Go" To="SetActive"/></State>
                  <Output Name="out"><Edge From="riding.OnActive" To="Left"/></Output>
                </Nodes></Module>
                """,
        };
        var g = new Wo200QuestGraph(p => mods.TryGetValue(p, out var x) ? x : null);
        var c = g.Of("Q.h.talk", "out1");
        Assert.Contains("Q.h.metThem", c.States);
        Assert.Contains("Q.h.depart.riding", c.States);   // through the waiter's OnFinished and the sub-module's In port
        Assert.Contains("Q.h.afterDepart", c.States);     // out through the sub-module's Output
        Assert.DoesNotContain("Q.h.unrelated", c.States); // another out-port's
        Assert.True(c.NodeTypes.ContainsKey("SceneFinishedWaiter"));
        Assert.False(c.Truncated);
        Assert.Same(c, g.Of("Q.h.talk", "out1"));          // cached
        Assert.Empty(g.Of("Q.h.nowhere", "x").States);
    }
}
