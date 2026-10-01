// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client.Tests;

/// <summary>WO-148: carrying on the other screen -- the wire, one carrier at a time, the landing
/// rule and the fail-safe returns; and the content audit's dice-key pak (our lines only, merged into
/// the player's own game files).</summary>
public class Wo148Tests
{
    private const byte Host = 1, Joiner = 2, Joiner2 = 3;

    // ---------------------------------------------------------------- the mod's own event (w148_carry)

    // The first three strings are verbatim from the first live run's kcd.log, where the agent
    // rejected every one of them as malformed (it counted the words one short).
    [Theory]
    [InlineData("grab dead wo148_body_1 2106.300 2823.000 131.100", "grab", "", "dead", "wo148_body_1", 2106.3f, 2823.0f, 131.1f)]
    [InlineData("held dead wo148_body_1 2103.731 2822.778 132.127", "held", "", "dead", "wo148_body_1", 2103.731f, 2822.778f, 132.127f)]
    [InlineData("put put dead wo148_body_1 2104.216 2822.921 130.956", "put", "put", "dead", "wo148_body_1", 2104.216f, 2822.921f, 130.956f)]
    [InlineData("grab object sack 1.000 -2.500 3.000", "grab", "", "object", "sack", 1.0f, -2.5f, 3.0f)]
    [InlineData("put drop object sack 1.000 2.000 3.000", "put", "drop", "object", "sack", 1.0f, 2.0f, 3.0f)]
    [InlineData("put lost ko villager_9 -1.5 2 3", "put", "lost", "ko", "villager_9", -1.5f, 2.0f, 3.0f)]
    public void The_mods_own_carry_events_parse(string line, string op, string how, string what, string name, float x, float y, float z)
    {
        Assert.True(CarryLocalEvent.TryParse(line, out var ev), line);
        Assert.Equal((op, how, what, name), (ev.Op, ev.How, ev.What, ev.Name));
        Assert.Equal(x, ev.X, 3); Assert.Equal(y, ev.Y, 3); Assert.Equal(z, ev.Z, 3);
        Assert.Equal(op == "grab", ev.IsGrab); Assert.Equal(op == "held", ev.IsHeld); Assert.Equal(op == "put", ev.IsPut);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("grab dead wo148_body_1 2106.3 2823.0")]                 // a word short (the old count)
    [InlineData("put dead wo148_body_1 2104.2 2822.9 130.9")]            // no how
    [InlineData("grab dead wo148_body_1 2106.3 2823.0 131.1 extra")]
    [InlineData("grab alive wo148_body_1 1 2 3")]                         // not a kind
    [InlineData("put throwit dead wo148_body_1 1 2 3")]                   // not a how
    [InlineData("grab dead bad-name 1 2 3")]                              // not a wire name
    [InlineData("grab dead wo148_body_1 1 NaN 3")]
    [InlineData("drop dead wo148_body_1 1 2 3")]
    public void A_bad_carry_event_is_refused(string? line)
    {
        Assert.False(CarryLocalEvent.TryParse(line, out _));
    }

    // ---------------------------------------------------------------- the wire

    [Fact]
    public void Carry_rides_the_join_channel_both_ways()
    {
        var r = Protocol.JoinWireFor(Protocol.CarryUp)!.Value;
        Assert.Equal(Protocol.CarryDown, r.Down);
        Assert.Equal(Protocol.JoinFrom.Either, r.From);
        Assert.Equal(((byte)0x70, (byte)0x71), (Protocol.CarryUp, Protocol.CarryDown));
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.LootFixedLen + 1, r.Min);
        Assert.Equal(Protocol.JoinHeaderLen + Protocol.LootFixedLen + Protocol.CarryTextMax, r.Max);
        Assert.Single(Protocol.JoinWire, x => x.Up == Protocol.CarryUp);
        Assert.DoesNotContain(Protocol.JoinWire, x => x.Up != Protocol.CarryUp && (x.Up == 0x70 || x.Down == 0x71));
        Assert.True(Protocol.IsJoinDown(Protocol.CarryDown, Protocol.JoinHeaderLen + Protocol.LootFixedLen + 10 + 1));
    }

    [Fact]
    public void The_texts_round_trip_and_bad_ones_are_refused()
    {
        string g = CarryText.Grab(Joiner, "dead", "bandit_camp_3", 1234.5f, -50.25f, 33.125f);
        Assert.Equal("2 dead bandit_camp_3 1234.5 -50.25 33.125", g);
        Assert.True(CarryText.TryParse(Protocol.CarryGrab, g, out var t));
        Assert.Equal((Joiner, "dead", "bandit_camp_3"), (t.Carrier, t.What, t.Name));
        Assert.Equal(1234.5f, t.X, 3); Assert.Equal(-50.25f, t.Y, 3); Assert.Equal(33.125f, t.Z, 3);

        string p = CarryText.Put(Host, "drop", "ko", "villager_7", 1f, 2f, 3f);
        Assert.True(CarryText.TryParse(Protocol.CarryPut, p, out var tp));
        Assert.Equal(("drop", "ko", "villager_7"), (tp.How, tp.What, tp.Name));

        Assert.True(CarryText.TryParse(Protocol.CarryRefuse, CarryText.Refuse(Joiner, "villager_7", "carried"), out var tr));
        Assert.Equal(("villager_7", "carried"), (tr.Name, tr.Why));

        Assert.False(CarryText.TryParse(Protocol.CarryGrab, "2 dead name with space 1 2 3", out _));
        Assert.False(CarryText.TryParse(Protocol.CarryGrab, "2 alive bandit 1 2 3", out _), "a living body is not a carry");
        Assert.False(CarryText.TryParse(Protocol.CarryGrab, "2 dead kcd-2 1 2 3", out _), "names are [A-Za-z0-9_]");
        Assert.False(CarryText.TryParse(Protocol.CarryGrab, "2 dead bandit NaN 2 3", out _));
        Assert.False(CarryText.TryParse(Protocol.CarryGrab, "2 dead bandit 1e9 2 3", out _));
        Assert.False(CarryText.TryParse(Protocol.CarryPut, "1 fling dead x 1 2 3", out _));
        Assert.False(CarryText.TryParse(Protocol.CarryRefuse, "1 x because", out _));
        Assert.False(CarryText.TryParse(9, g, out _));
        Assert.False(CarryText.TryParse(Protocol.CarryGrab, "", out _));
        Assert.False(CarryText.TryParse(Protocol.CarryGrab, "2 dead " + new string('a', 65) + " 1 2 3", out _));

        // the longest carry fits the text limit
        string longest = CarryText.Put(255, "throw", "object", new string('a', Protocol.MaxNpcNameLen), -99999.999f, -99999.999f, -99999.999f);
        Assert.True(longest.Length <= Protocol.CarryTextMax, $"{longest.Length}");
        Assert.True(CarryText.TryParse(Protocol.CarryPut, longest, out _));
        // and builds as a join message
        var pkt = new LootMsg(Protocol.CarryPut, 7, longest).BuildUp(Protocol.CarryUp, Protocol.JoinTargetHost);
        Assert.Equal(Protocol.CarryUp, pkt[0]);
    }

    // ---------------------------------------------------------------- one carrier at a time

    [Fact]
    public void The_host_refuses_a_grab_of_what_its_world_gave_someone_else()
    {
        var host = new CarryLedger();
        Assert.Null(host.OnLocalGrab(Host, "corpse_1", "dead", 1, 0, 1, 2, 3));
        Assert.Equal(CarryLedger.Verdict.Refuse, host.OnPeerGrab(Host, true, Joiner, "corpse_1", "dead", 5, 10, 1, 2, 3, false));
        Assert.Equal(Host, host.Of("corpse_1")!.Carrier);

        // a second joiner's grab of a body the first joiner holds: refused too
        Assert.Equal(CarryLedger.Verdict.Apply, host.OnPeerGrab(Host, true, Joiner, "corpse_2", "dead", 6, 10, 0, 0, 0, false));
        Assert.Equal(CarryLedger.Verdict.Refuse, host.OnPeerGrab(Host, true, Joiner2, "corpse_2", "dead", 1, 11, 0, 0, 0, false));
        Assert.Equal(Joiner, host.Of("corpse_2")!.Carrier);
        // the holder's own Held is "the same"
        Assert.Equal(CarryLedger.Verdict.Same, host.OnPeerGrab(Host, true, Joiner, "corpse_2", "dead", 6, 2000, 0, 0, 0, true));
    }

    [Fact]
    public void The_host_wins_its_own_grab_and_the_joiner_is_put_back()
    {
        // the host's world: the joiner's avatar holds it, then the host's player takes it
        var host = new CarryLedger();
        Assert.Equal(CarryLedger.Verdict.Apply, host.OnPeerGrab(Host, true, Joiner, "corpse_1", "dead", 1, 0, 0, 0, 0, false));
        Assert.Equal(Joiner, host.OnLocalGrab(Host, "corpse_1", "dead", 2, 5, 0, 0, 0));
        Assert.Equal(Host, host.Of("corpse_1")!.Carrier);

        // the joiner's world: its own carry, then the host's grab arrives -> the joiner loses
        var joiner = new CarryLedger();
        Assert.Null(joiner.OnLocalGrab(Joiner, "corpse_1", "dead", 1, 0, 0, 0, 0));
        Assert.Equal(CarryLedger.Verdict.LocalLoses, joiner.OnPeerGrab(Joiner, false, Host, "corpse_1", "dead", 2, 5, 0, 0, 0, false));
        Assert.Equal(Host, joiner.Of("corpse_1")!.Carrier);
        // the refusal the host also sent finds nothing left to undo
        Assert.False(joiner.OnRefused(Joiner, "corpse_1"));
    }

    [Fact]
    public void A_carry_this_player_just_lost_is_remembered_for_its_late_held()
    {
        // the live race: the joiner carried, the host's grab arrived (the joiner loses), and the joiner's own
        // 2 s Held came after -- read as a new grab, the host refused it and the player was told twice
        var j = new CarryLedger();
        j.OnLocalGrab(Joiner, "corpse_r", "dead", 1, 1000, 0, 0, 0);
        Assert.Equal(CarryLedger.Verdict.LocalLoses, j.OnPeerGrab(Joiner, false, Host, "corpse_r", "dead", 2, 2000, 0, 0, 0, false));
        Assert.False(j.OnLocalHeld(Joiner, "corpse_r", 3000, 0, 0, 0));   // not this player's any more
        Assert.True(j.JustLost("corpse_r", 3000));
        Assert.True(j.JustLost("corpse_r", 2000 + CarryLedger.LostQuietMs - 1));
        Assert.False(j.JustLost("corpse_r", 2000 + CarryLedger.LostQuietMs));
        Assert.False(j.JustLost("other", 3000));
        // a refusal is a loss too
        j.OnLocalGrab(Joiner, "corpse_s", "dead", 3, 10_000, 0, 0, 0);
        Assert.True(j.OnRefused(Joiner, "corpse_s", 10_500));
        Assert.True(j.JustLost("corpse_s", 11_000));
        j.Clear();
        Assert.False(j.JustLost("corpse_s", 11_000));
    }

    [Fact]
    public void A_refused_joiner_puts_down_once()
    {
        var j = new CarryLedger();
        j.OnLocalGrab(Joiner, "corpse_9", "ko", 3, 0, 4, 5, 6);
        Assert.True(j.OnRefused(Joiner, "corpse_9"));
        Assert.False(j.Holds("corpse_9"));
        Assert.False(j.OnRefused(Joiner, "corpse_9"), "twice is nothing");
    }

    [Fact]
    public void Both_grab_at_once_the_hosts_world_decides()
    {
        // each machine grabbed locally first; the messages cross
        var host = new CarryLedger();
        var joiner = new CarryLedger();
        host.OnLocalGrab(Host, "corpse_x", "dead", 1, 0, 0, 0, 0);
        joiner.OnLocalGrab(Joiner, "corpse_x", "dead", 1, 0, 0, 0, 0);
        // the joiner's grab reaches the host: refused
        Assert.Equal(CarryLedger.Verdict.Refuse, host.OnPeerGrab(Host, true, Joiner, "corpse_x", "dead", 1, 50, 0, 0, 0, false));
        // the host's grab reaches the joiner: the joiner's carry loses
        Assert.Equal(CarryLedger.Verdict.LocalLoses, joiner.OnPeerGrab(Joiner, false, Host, "corpse_x", "dead", 1, 50, 0, 0, 0, false));
        Assert.Equal(Host, host.Of("corpse_x")!.Carrier);
        Assert.Equal(Host, joiner.Of("corpse_x")!.Carrier);
    }

    [Fact]
    public void Puts_ends_and_echoes()
    {
        var l = new CarryLedger();
        Assert.Equal(CarryLedger.Verdict.Ignore, l.OnPeerGrab(Joiner, false, Joiner, "a", "dead", 1, 0, 0, 0, 0, false));
        l.OnPeerGrab(Joiner, false, Host, "a", "dead", 1, 0, 0, 0, 0, false);
        Assert.False(l.OnPut(Joiner2, "a"), "only its carrier sets it down");
        Assert.True(l.OnPut(Host, "a"));
        Assert.False(l.Holds("a"));
        // a Held for a carry never seen here (a reload) shows it
        Assert.Equal(CarryLedger.Verdict.Apply, l.OnPeerGrab(Joiner, false, Host, "b", "object", 4, 0, 0, 0, 0, true));
    }

    [Fact]
    public void A_carrier_who_leaves_or_falls_silent_sets_everything_down()
    {
        var l = new CarryLedger();
        l.OnPeerGrab(Host, true, Joiner, "a", "dead", 1, 0, 0, 0, 0, false);
        l.OnPeerGrab(Host, true, Joiner2, "b", "dead", 1, 0, 0, 0, 0, false);
        l.OnLocalGrab(Host, "c", "dead", 1, 0, 0, 0, 0);
        var gone = l.DropCarrier(Joiner);
        Assert.Equal("a", Assert.Single(gone).Body);
        Assert.Empty(l.Expire(Host, Protocol.CarryLostAfterMs, Protocol.CarryLostAfterMs));
        var silent = l.Expire(Host, Protocol.CarryLostAfterMs + 1, Protocol.CarryLostAfterMs);
        Assert.Equal("b", Assert.Single(silent).Body);
        Assert.True(l.Holds("c"), "this player's own carry never expires here");
        Assert.Equal("c", Assert.Single(l.Mine(Host)).Body);
    }

    [Fact]
    public void This_players_carry_lives_while_the_game_confirms_it()
    {
        var l = new CarryLedger();
        l.OnLocalGrab(Host, "corpse_c", "dead", 1, 0, 0, 0, 0);
        Assert.True(l.OnLocalHeld(Host, "corpse_c", 60_000, 1, 1, 1));
        Assert.Empty(l.ExpireMine(Host, 150_000, 120_000));                       // confirmed at 60 s: alive at 150 s
        var gone = l.ExpireMine(Host, 181_000, 120_000);                          // 121 s without a word
        Assert.Equal("corpse_c", Assert.Single(gone).Body);
        Assert.False(l.OnLocalHeld(Host, "corpse_c", 182_000, 1, 1, 1), "a confirmation after that is a new grab");
        // a partner's carry is not this player's to expire this way
        l.OnPeerGrab(Host, true, Joiner, "corpse_j", "dead", 1, 0, 0, 0, 0, false);
        Assert.Empty(l.ExpireMine(Host, 1_000_000, 120_000));
        Assert.False(l.OnLocalHeld(Host, "corpse_j", 0, 0, 0, 0), "a partner's body is never confirmed as this player's");
    }

    // ---------------------------------------------------------------- where it lands

    [Fact]
    public void The_carriers_spot_wins_when_it_is_reachable()
    {
        // came to rest 1.5 m from where the carrier's game left it: moved there
        Assert.Equal(Wo148Rules.Land.MoveToCarrier, Wo148Rules.Decide(10, 0, 5, 5, 11.5f, 0, 5, 5));
        // within the half metre: kept
        Assert.Equal(Wo148Rules.Land.Keep, Wo148Rules.Decide(10, 0, 5, 5, 10.3f, 0, 5, 5));
    }

    [Fact]
    public void A_body_in_the_air_or_under_the_ground_is_put_back()
    {
        // in the air: 3 m above the first surface, and the carrier's spot is no better
        Assert.Equal(Wo148Rules.Land.PutBack, Wo148Rules.Decide(10, 0, 8, 5, 10, 0, 8, 5));
        // under the ground: the surface is 2 m above it
        Assert.Equal(Wo148Rules.Land.PutBack, Wo148Rules.Decide(10, 0, 3, 5, 10, 0, 3, 5));
        // nothing under it at all
        Assert.Equal(Wo148Rules.Land.PutBack, Wo148Rules.Decide(10, 0, 3, null, 10, 0, 3, null));
        // the local rest is bad but the carrier's spot is fine: moved there, not put back
        Assert.Equal(Wo148Rules.Land.MoveToCarrier, Wo148Rules.Decide(10, 0, 3, 5, 10.2f, 0, 5.1f, 5));
        // the carrier's spot is bad here, the local rest is fine: kept
        Assert.Equal(Wo148Rules.Land.Keep, Wo148Rules.Decide(10, 0, 5, 5, 20, 0, 9, 5));
    }

    [Fact]
    public void Reachable_is_lying_on_the_ground()
    {
        Assert.True(Wo148Rules.Reachable(5.0f, 5.0f));
        Assert.True(Wo148Rules.Reachable(5.7f, 5.0f), "a body on a step or a heap");
        Assert.True(Wo148Rules.Reachable(4.5f, 5.0f), "sunk a little into soft ground");
        Assert.False(Wo148Rules.Reachable(6.0f, 5.0f), "in the air");
        Assert.False(Wo148Rules.Reachable(4.2f, 5.0f), "under the ground");
        Assert.False(Wo148Rules.Reachable(5.0f, null));
        Assert.False(Wo148Rules.Reachable(float.NaN, 5.0f));
    }

    // ---------------------------------------------------------------- the dice-key pak (the content audit)

    // A made-up stand-in for the game's files: the same shape, none of their content.
    private const string FakeProfile =
        "<profile version=\"0\">\r\n" +
        "\t<actionmap name=\"movement\" priority=\"pure_include\" exclusivity=\"0\">\r\n\t\t<action name=\"jump\" onPress=\"1\" />\r\n\t</actionmap>\r\n" +
        "\t<actionmap name=\"interaction\" priority=\"pure_include\" exclusivity=\"0\">\t\t<!-- only for include -->\r\n" +
        "\t\t<action name=\"use\" onPress=\"1\" keyboard=\"_keybinds_ref_\" />\r\n" +
        "\t</actionmap>\t\r\n" +
        "\t<actionmap name=\"interaction_talk\" priority=\"pure_include\" exclusivity=\"0\">\r\n\t</actionmap>\r\n" +
        "</profile>\r\n";
    private const string FakeSuperactions =
        "<keybinds>\r\n\t<ui_group name=\"general\" ui_label=\"x\" />\r\n\t<superaction name=\"use\" ui_group=\"general\">\r\n\t\t<action name=\"use\" map=\"interaction\" />\r\n\t</superaction>\r\n</keybinds>\r\n";

    private static string ProfileBody() => KeybindPak.PatchBody(KeybindPak.EmbeddedPatch("defaultProfile.interaction.xml"));
    private static string SuperBody() => KeybindPak.PatchBody(KeybindPak.EmbeddedPatch("keybindSuperactions.append.xml"));

    [Fact]
    public void Our_lines_go_at_the_end_of_the_interaction_actionmap()
    {
        string? m = KeybindPak.MergeProfile(FakeProfile, ProfileBody(), out string why);
        Assert.NotNull(m);
        Assert.Equal("ok", why);
        int open = m!.IndexOf("<actionmap name=\"interaction\"", StringComparison.Ordinal);
        int close = m.IndexOf("</actionmap>", open, StringComparison.Ordinal);
        foreach (string a in KeybindPak.Actions)
        {
            int at = m.IndexOf($"name=\"{a}\"", StringComparison.Ordinal);
            Assert.True(at > open && at < close, a);
        }
        Assert.Contains("\t</actionmap>\t\r\n", m);   // the game's own line is untouched
        Assert.StartsWith(FakeProfile[..FakeProfile.IndexOf("\t</actionmap>\t", StringComparison.Ordinal)], m);
        Assert.EndsWith(FakeProfile[FakeProfile.IndexOf("\t</actionmap>\t", StringComparison.Ordinal)..], m);
        Assert.Null(KeybindPak.MergeProfile(m, ProfileBody(), out why));   // never twice
        Assert.Contains("already", why);
    }

    [Fact]
    public void Our_superactions_go_at_the_end_of_the_root()
    {
        string? m = KeybindPak.MergeSuperactions(FakeSuperactions, SuperBody(), out string why);
        Assert.NotNull(m);
        Assert.EndsWith("</superaction>\r\n</keybinds>\r\n", m);
        Assert.Contains("<control input=\"f9\" controller=\"keyboard\" />", m);
        Assert.StartsWith(FakeSuperactions[..FakeSuperactions.IndexOf("</keybinds>", StringComparison.Ordinal)], m);
    }

    [Fact]
    public void A_file_of_another_shape_is_refused_and_nothing_is_merged()
    {
        Assert.Null(KeybindPak.MergeProfile("<profile version=\"0\"></profile>", ProfileBody(), out string why));
        Assert.Contains("0 actionmaps", why);
        string two = FakeProfile.Replace("interaction_talk", "interaction");
        Assert.Null(KeybindPak.MergeProfile(two, ProfileBody(), out why));
        Assert.Contains("2 actionmaps", why);
        Assert.Null(KeybindPak.MergeProfile("<profile><actionmap name=\"interaction\">", ProfileBody(), out why));
        Assert.Contains("does not parse", why);
        Assert.Null(KeybindPak.MergeSuperactions("<other />", SuperBody(), out why));
        Assert.Null(KeybindPak.MergeSuperactions("<keybinds>", SuperBody(), out _));
    }

    [Fact]
    public void Line_endings_follow_the_games_file()
    {
        string lf = FakeProfile.Replace("\r\n", "\n");
        string? m = KeybindPak.MergeProfile(lf, ProfileBody(), out _);
        Assert.NotNull(m);
        Assert.DoesNotContain("\r\n", m);
    }

    [Fact]
    public void The_patch_files_hold_only_our_ten_actions()
    {
        string p = ProfileBody(), s = SuperBody();
        foreach (string a in KeybindPak.Actions) { Assert.Contains($"name=\"{a}\"", p); Assert.Contains($"name=\"{a}\"", s); }
        Assert.Equal(10, p.Split("<action ").Length - 1);
        Assert.Equal(10, s.Split("<superaction ").Length - 1);
        Assert.DoesNotContain("<actionmap", p);
        Assert.DoesNotContain("<keybinds", s);
    }

    [Fact]
    public void The_pak_is_written_once_and_rewritten_only_on_a_change()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wo148-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string pak = Path.Combine(dir, KeybindPak.PakFileName);
            var entries = new[] { (KeybindPak.ProfileEntry, Encoding.ASCII.GetBytes("<a/>")), (KeybindPak.SuperactionsEntry, Encoding.ASCII.GetBytes("<b/>")) };
            Assert.Equal("written", KeybindPak.WritePak(pak, entries));
            Assert.Equal("unchanged", KeybindPak.WritePak(pak, entries));
            entries[1] = (KeybindPak.SuperactionsEntry, Encoding.ASCII.GetBytes("<c/>"));
            Assert.Equal("written", KeybindPak.WritePak(pak, entries));
            using var z = ZipFile.OpenRead(pak);
            Assert.Equal(new[] { KeybindPak.ProfileEntry, KeybindPak.SuperactionsEntry }, z.Entries.Select(e => e.FullName).ToArray());
            Assert.False(File.Exists(pak + ".tmp"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void A_failure_message_keeps_no_folder_of_this_machine()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string msg = @"Access to the path 'X:\Games\KCD2Mod\Mods\kdcmp\Data\kdcmp_keys.pak.tmp' is denied. See X:\GAMES\KCD2Mod\Data and " + home + @"\AppData";
        string s = KeybindPak.NoPaths(msg, @"X:\Games\KCD2Mod\Mods\kdcmp\", @"X:\Games\KCD2Mod");
        Assert.Equal(@"Access to the path '<mod>\Data\kdcmp_keys.pak.tmp' is denied. See <game>\Data and <home>\AppData", s);
        Assert.Equal("nothing to hide", KeybindPak.NoPaths("nothing to hide", "", ""));
    }

    [Fact]
    public void Paks_are_searched_patch_first_then_game_data()
    {
        var order = KeybindPak.SearchOrder(new[] { @"D\Tables.pak", @"D\IPL_GameData.pak", @"D\patch_1.pak", @"D\patch_2.pak", @"D\Scripts.pak" }).ToArray();
        Assert.Equal(new[] { @"D\patch_2.pak", @"D\patch_1.pak", @"D\IPL_GameData.pak", @"D\Scripts.pak", @"D\Tables.pak" }, order);
    }

    // ---------------------------------------------------------------- the quest registry carries keys only

    [Fact]
    public void The_embedded_quest_registry_carries_no_game_text()
    {
        var asm = typeof(QuestObjectiveRegistry).Assembly;
        string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("mainquest-objectives.json", StringComparison.OrdinalIgnoreCase));
        using var s = asm.GetManifestResourceStream(name)!;
        string json = new StreamReader(s).ReadToEnd();
        Assert.DoesNotContain("\"t\":", json.Replace(" ", ""));
        Assert.DoesNotContain("\"title\":", json.Replace(" ", ""));
        var reg = QuestObjectiveRegistry.Parse(json);
        Assert.Equal(32, reg.Quests.Count);
        Assert.All(reg.Quests, q => Assert.StartsWith("qname_", q.TitleKey));
        Assert.Equal(626, reg.Quests.Sum(q => q.Objectives.Length));
        Assert.Equal("b6b917b72323", reg.Id);   // the id never covered the texts: unchanged
    }

    [Fact]
    public void Titles_come_from_the_players_own_localisation_pak()
    {
        var asm = typeof(QuestObjectiveRegistry).Assembly;
        string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("mainquest-objectives.json", StringComparison.OrdinalIgnoreCase));
        using var s = asm.GetManifestResourceStream(name)!;
        var reg = QuestObjectiveRegistry.Parse(new StreamReader(s).ReadToEnd());
        var q = reg.Quests[0];
        var o = q.Objectives[0];
        Assert.Equal(q.Name, q.Label);   // no pak yet: the internal names
        Assert.Equal(o.Name, o.Label);
        string root = Path.Combine(Path.GetTempPath(), "wo148-loc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Localization"));
        try
        {
            // made-up strings in the game's table shape (a row per key, the text in its last cell)
            string xml = $"<Table><Row><Cell>{q.TitleKey}</Cell><Cell>Made-up quest</Cell></Row>" +
                         $"<Row><Cell>{o.StringName}</Cell><Cell>x</Cell><Cell>Made-up objective &amp; more</Cell></Row></Table>";
            using (var z = ZipFile.Open(Path.Combine(root, "Localization", "English_xml.pak"), ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("text_ui_quest.xml").Open()))
                w.Write(xml);
            Assert.Equal(2, reg.LocalizeFrom(root));
            Assert.Equal("Made-up quest", q.Label);
            Assert.Equal("Made-up objective & more", o.Label);
            Assert.Equal(0, reg.LocalizeFrom(Path.Combine(root, "nope")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Without_a_game_the_cli_says_why_and_fails()
    {
        var sw = new StringWriter();
        int rc = KeybindPak.RunCli(new[] { "--keys-pak", "--game-root", Path.Combine(Path.GetTempPath(), "no-game-" + Guid.NewGuid().ToString("N")) }, sw);
        Assert.Equal(1, rc);
        Assert.StartsWith("KEYS-PAK {\"ok\":false", sw.ToString());
        sw = new StringWriter();
        Assert.Equal(2, KeybindPak.RunCli(new[] { "--keys-pak" }, sw));
    }
}
