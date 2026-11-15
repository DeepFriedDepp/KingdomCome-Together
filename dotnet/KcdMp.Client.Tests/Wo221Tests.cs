// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

public class Wo221Tests
{
    [Fact]
    public void Keepers_parse_only_names_and_ids()
    {
        var p = Wo221Rules.ParseKeepers("ttkc_man_11=520, ttkc_baska=503,bad name=1,x=,=5,y=-3,z=1000000,ttkc_man_11=7");
        Assert.Equal(2, p.Count);
        Assert.Equal("ttkc_man_11", p[0].Key); Assert.Equal(520, p[0].Value);
        Assert.Equal("ttkc_baska", p[1].Key); Assert.Equal(503, p[1].Value);
        Assert.Empty(Wo221Rules.ParseKeepers("-"));
        Assert.Empty(Wo221Rules.ParseKeepers(null));
    }

    [Fact]
    public void Keepers_texts_round_trip_in_parts()
    {
        var pairs = Enumerable.Range(0, 120).Select(i => new KeyValuePair<string, int>($"ttkc_keeper_number_{i:D3}", 500 + i)).ToList();
        var texts = Wo221Rules.KeepersTexts(pairs, 300);
        Assert.True(texts.Count > 1);
        var back = new List<KeyValuePair<string, int>>();
        for (int i = 0; i < texts.Count; i++)
        {
            Assert.True(Wo221Rules.TryParseKeepersPart(texts[i], out int part, out int nparts, out var ps));
            Assert.Equal(i + 1, part); Assert.Equal(texts.Count, nparts);
            Assert.All(texts[i].Split(' ', 3)[2], c => Assert.True(c != ' '));
            back.AddRange(ps);
        }
        Assert.Equal(Wo221Rules.KeepersCsv(pairs), Wo221Rules.KeepersCsv(back));
        Assert.Equal(new[] { "1 1 -" }, Wo221Rules.KeepersTexts(new List<KeyValuePair<string, int>>()));
        Assert.True(Wo221Rules.TryParseKeepersPart("1 1 -", out _, out _, out var none)); Assert.Empty(none);
        Assert.False(Wo221Rules.TryParseKeepersPart("2 1 a=1", out _, out _, out _));
        Assert.False(Wo221Rules.TryParseKeepersPart("1 1 nothing-here", out _, out _, out _));
    }

    [Fact]
    public void Shop_op_bodies()
    {
        Assert.Equal(new byte[] { 0x08, 0x02, 0, 0, 3, (byte)'a', (byte)'b', (byte)'c' }, Wo221Rules.OpenBody(520, "abc"));
        Assert.Equal(new byte[] { 2, (byte)'h', (byte)'i' }, Wo221Rules.CloseBody("hi"));
    }

    [Fact]
    public void Switch_names_and_twin()
    {
        Assert.Contains("combat_action_end", W221Switch.NativeNames);
        Assert.Contains("shop_keeper", W221Switch.AgentNames);
        Assert.True(W221Switch.TwinOff("all", "shop_keeper"));
        Assert.True(W221Switch.TwinOff("drive, shop_keeper", "shop_keeper"));
        Assert.False(W221Switch.TwinOff("drive", "shop_keeper"));
        Assert.Equal(0b0101u, W221Switch.NativeBits(n => n is "combat_action_end" or "drive"));
        Assert.Equal(4, Array.IndexOf(W221Switch.NativeNames, "own_horse"));   // wo221.h kBitOwnHorse = 1 << 4
        Assert.Contains("talk_force", W221Switch.AgentNames);
    }

    [Fact]
    public void Talk_and_order_op_bodies()
    {
        Assert.Equal(new byte[] { 1, 2, (byte)'h', (byte)'i' }, Wo221Rules.TalkBody(true, "hi"));
        Assert.Equal(new byte[] { 0, 2, (byte)'h', (byte)'i' }, Wo221Rules.TalkBody(false, "hi"));
        byte[]? go = Wo221Rules.GoBody("tsem_horse_1 1.5 -2 3 3");
        Assert.NotNull(go);
        Assert.Equal(2 + 12 + 12, go!.Length);
        Assert.Equal(3, go[0]);
        Assert.Equal(12, go[1]);
        Assert.Equal(1.5f, BitConverter.ToSingle(go, 14));
        Assert.Equal(-2f, BitConverter.ToSingle(go, 18));
        Assert.Equal(3f, BitConverter.ToSingle(go, 22));
        Assert.Null(Wo221Rules.GoBody("tsem_horse_1 1 2 3 4"));        // no such gait
        Assert.Null(Wo221Rules.GoBody("tsem_horse_1 1 2 NaN 1"));
        Assert.Null(Wo221Rules.GoBody("bad name 1 2 3 1"));
        Assert.Null(Wo221Rules.GoBody("tsem_horse_1 1 2 3"));
        Assert.Null(Wo221Rules.GoBody(null));
    }

    [Fact]
    public void Saddlebag_texts_only_in_their_shapes()
    {
        const string c1 = "4a6fa310-067a-404d-9813-bd1761d1c70d", c2 = "c67de991-e22a-4a19-8b68-9369919c41dd";
        Assert.True(Wo221BagRules.IsBags($"tsem_horseForSale_1 3 1 1 {c1}:2:100,{c2}:1:87"));
        Assert.True(Wo221BagRules.IsBags("tsem_horseForSale_1 1 1 1 -"));                       // an empty horse
        Assert.False(Wo221BagRules.IsBags($"tsem_horseForSale_1 3 1 1 {c1}:2"));                 // no condition
        Assert.False(Wo221BagRules.IsBags($"bad name 3 1 1 {c1}:2:100"));
        Assert.False(Wo221BagRules.IsBags($"horse 3 1 1 {c1}:2:100\"); os.exit() --"));        // nothing but the shape reaches a script
        Assert.False(Wo221BagRules.IsBags($"horse 3 1 1 {c1.ToUpperInvariant()}:2:100"));
        Assert.True(Wo221BagRules.IsBase($"horse_a 1 2 {c1}:1:100"));
        Assert.False(Wo221BagRules.IsBase($"horse_a 1 {c1}:1:100"));
        Assert.True(Wo221BagRules.IsTake($"1004 horse_a {c2} 1"));
        Assert.False(Wo221BagRules.IsTake($"1004 horse_a notaclass 1"));
        Assert.False(Wo221BagRules.IsTake($"x horse_a {c2} 1"));
        Assert.True(Wo221BagRules.IsAnswer("1004 grant") && Wo221BagRules.IsAnswer("1004 refuse") && Wo221BagRules.IsAnswer("1004 busy"));
        Assert.False(Wo221BagRules.IsAnswer("1004 maybe"));
        Assert.False(Wo221BagRules.IsAnswer("grant"));
        Assert.True(Wo221BagRules.SplitPeer("1 1004 horse_a x", out byte peer, out string rest));
        Assert.Equal(1, peer);
        Assert.Equal("1004 horse_a x", rest);
        Assert.False(Wo221BagRules.SplitPeer("one two", out _, out _));
        Assert.False(Wo221BagRules.SplitPeer(null, out _, out _));
    }

    [Fact]
    public void Corpse_wounds_round_trip_and_leave_clean_bodies_out()
    {
        var looks = new List<KeyValuePair<string, byte[]>>
        {
            new("ttkc_man_7", [0, 0, 0, 0, 0, 0]),                 // never bloodied: not sent
            new("w221_roadside_1", [230, 230, 230, 230, 0, 0]),
            new("bad name", [1, 1, 1, 1, 1, 1]),
        };
        string csv = Wo221Rules.CorpseLooksCsv(looks);
        Assert.Equal("w221_roadside_1=e6e6e6e60000", csv);
        var back = Wo221Rules.ParseCorpseLooks(csv);
        Assert.Single(back);
        Assert.Equal("w221_roadside_1", back[0].Key);
        Assert.Equal(new byte[] { 230, 230, 230, 230, 0, 0 }, back[0].Value);
        Assert.Equal("-", Wo221Rules.CorpseLooksCsv([]));
        Assert.Empty(Wo221Rules.ParseCorpseLooks("-"));
        Assert.Empty(Wo221Rules.ParseCorpseLooks("x=zz,y=0102,=010203040506"));
        Assert.Equal(new byte[] { 1, (byte)'a', 9, 8, 7, 6, 5, 4 }, Wo221Rules.BloodSetBody("a", [9, 8, 7, 6, 5, 4]));
    }

    [Fact]
    public void Route_lines_go_only_to_joiners_that_read_them()
    {
        byte[] peers = [1, 2, 3];
        // 1 said no, 2 said yes, 3 said nothing (an older build): 2 and 3 get the lines
        Assert.Equal(new byte[] { 2, 3 }, Wo221Rules.RouteReaders(peers, g => g == 1 ? false : g == 2 ? true : null));
        Assert.Empty(Wo221Rules.RouteReaders(peers, _ => false));          // nobody reads: the host's read is not made
        Assert.Equal(peers, Wo221Rules.RouteReaders(peers, _ => null));     // nobody said: as before
        Assert.Empty(Wo221Rules.RouteReaders([], _ => true));
        Assert.Contains("route_quiet", W221Switch.AgentNames);
        Assert.Equal(8, Protocol.QuestAskRouteUse);
    }

    [Fact]
    public void A_conversation_at_its_choices_is_running()
    {
        const string forced = "New dialogue 'Barbora.open_world.shop.nakupovani_z_dialogu_zena / Barbora.open_world.shop.x (normal - Id: 273) (ScriptBindDialog::ForceDialog)' is starting. Params: souls = 'Ex: ttkc_woman_2; Ex: Dude' forced = true";
        const string own = "New dialogue 'Barbora.open_world.shop.nakupovani_z_dialogu_muz (normal - Id: 44) (Actor::RequestDialog)' is starting. Params: souls = 'Ex: Dude; Ex: ttkc_man_11' forced = false, forced decision = none";
        const string chat = "New dialogue 'Barbora.x.svepomoc__chat_1_2 (chat - Id: 581) (AI::RequestDialog)' is starting. Params: souls = 'Ex: ttkc_man_11 - meta override: SVEPOMOC_CHAT; Ex: Dude' f";
        const string bark = "New dialogue 'Barbora.x.prvni_vyzva (ingame monolog - Id: 580) (AI::DoMonologue)' is starting. Params: souls = 'Ex: ttkc_man_11 - meta override: SVEPOMOC_PRVNI_VYZVA'";
        const string solo = "New dialogue 'Barbora.x.mono (ingame monolog - Id: 9) (AI::DoMonologue)' is starting. Params: souls = 'Ex: Dude'";
        Assert.Equal(273, Wo221Rules.DudeDialogIdAnywhere(forced));
        Assert.Equal(44, Wo221Rules.DudeDialogIdAnywhere(own));
        Assert.Equal(581, Wo221Rules.DudeDialogIdAnywhere(chat));
        Assert.Equal(-1, Wo221Rules.DudeDialogIdAnywhere(bark));    // not this player's
        Assert.Equal(-1, Wo221Rules.DudeDialogIdAnywhere(solo));    // his own monologue: no choices
        Assert.Equal(-1, Wo221Rules.DudeDialogIdAnywhere("[ID: 273] 1) Barbora.a.b [used=false][priority=0]"));
        Assert.Equal(-1, Wo221Rules.DudeDialogIdAnywhere(null));
        Assert.True(Wo221Rules.IsChoiceOf("[ID: 273] 1) Barbora.open_world.shop.haggle.dec1.seq1 [used=false][priority=0]", 273));
        Assert.True(Wo221Rules.IsChoiceOf("[ID: 273] 12) Barbora.a.b [used=true][priority=3]", 273));
        Assert.False(Wo221Rules.IsChoiceOf("[ID: 274] 1) Barbora.a.b [used=false][priority=0]", 273));      // another dialogue's
        Assert.False(Wo221Rules.IsChoiceOf("[ID: 273] Dialog interrupted. [Ex0: ttkc_woman_2 Ex1: Dude state: WAITING_FOR_INTERACTION flags: 1]", 273));
        Assert.False(Wo221Rules.IsChoiceOf("[ID: 273] DialogCommand-Anim: dialogId=273, who=HRAC, anim=ADLG_Confusion", 273));
        Assert.False(Wo221Rules.IsChoiceOf("[ID: 273] 1) Barbora.a.b [used=false]", -1));
        Assert.False(Wo221Rules.IsChoiceOf(null, 273));
    }

    [Fact]
    public void A_dialogue_of_the_talks_quest_module_is_the_talks()
    {
        const string talk = "Barbora.trosecko.nakup_koni__trosecko.horse_trader_semin.podkoni_trosecko__inspekce_koni";
        Assert.True(Wo221Rules.SameQuestModule("Barbora.trosecko.nakup_koni__trosecko.horse_trader_semin.podkoni_trosecko__obchod", talk));
        Assert.False(Wo221Rules.SameQuestModule(talk, talk));                                                              // the talk's own: the old rule's
        Assert.False(Wo221Rules.SameQuestModule("Barbora.trosecko.nakup_koni__trosecko.other_trader.podkoni__obchod", talk)); // another module
        Assert.False(Wo221Rules.SameQuestModule("Barbora.trosecko.nakup_koni__trosecko.horse_trader_semin.sub.podkoni__obchod", talk));
        Assert.False(Wo221Rules.SameQuestModule("Barbora.open_world.shop.bribe", "Barbora.open_world.shop.nakupovani_z_dialogu_muz")); // the open world's
        Assert.False(Wo221Rules.SameQuestModule("Barbora.x", "Barbora.y"));                                                 // too shallow
        Assert.False(Wo221Rules.SameQuestModule(null, talk));
        Assert.False(Wo221Rules.SameQuestModule("", ""));
    }

    [Fact]
    public void The_choice_lines_reach_the_agent()
    {
        const string choice = "[ID: 357] 1) Barbora.open_world.nextnextgenpunishment.arrestdialogue.straze__zatykaci_chat.dec2.surrender [used=false][priority=0]";
        Assert.True(LogTailGameTransport.Wo144Passes(choice));          // the forward list passes what the rule reads
        Assert.True(Wo221Rules.IsChoiceOf(choice, 357));
        const string start = "New dialogue 'Barbora.open_world.nextnextgenpunishment.arrestdialogue.straze__zatykaci_chat (chat - Id: 357) (AI::RequestDialog)' is starting. Params: souls = 'Ex: ttkc_man_2 - meta override: STRAZ_ZATYKANI_CHAT; Ex: Dude' forced = false";
        Assert.Equal(357, Wo221Rules.DudeDialogIdAnywhere(start));
    }

    [Fact]
    public void The_module_that_sells_a_horse_is_read_from_the_passed_node()
    {
        const string node = "Barbora.trosecko.nakup_koni__trosecko.horse_trader_semin.horseinstance_1.replaceplayerhorse_disposeoldifexists.replaceplayerhorse4_1";
        Assert.Equal("Barbora.trosecko.nakup_koni__trosecko.horse_trader_semin.horseinstance_1", Wo221Rules.HorseModuleOf(node));
        Assert.Equal("", Wo221Rules.HorseModuleOf("Barbora.a.b"));       // not that deep
        Assert.Equal("", Wo221Rules.HorseModuleOf("not a path"));
        Assert.Equal("", Wo221Rules.HorseModuleOf(null));
        byte[] body = Wo221Rules.HorseModulesBody(["Barbora.a.b.c.d", "bad path", "Barbora.a.b.c.d"]);
        Assert.Equal(1, body[0]);                                         // the duplicate and the bad one are left out
        Assert.Equal(15, body[1] | (body[2] << 8));
        Assert.Equal("Barbora.a.b.c.d", System.Text.Encoding.ASCII.GetString(body, 3, 15));
        Assert.Equal(new byte[] { 0 }, Wo221Rules.HorseModulesBody([]));
        Assert.Equal(199, Protocol.Wo221OpHorseModules);
    }
}
