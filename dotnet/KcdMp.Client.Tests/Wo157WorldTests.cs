// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;
using Spec = KcdMp.Client.Tests.WhsSaveTests.Spec;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-157 2.4: a start save several groups host. The four joiner cases on fixtures (synthetic saves and seeds only,
/// WhsSaveTests' builder): a fresh joiner, one who hosted the same start save, one with only regular-game saves, one
/// with his own Modding Tools save -- and two hosts of one start save kept apart on a joiner.
/// </summary>
public class Wo157WorldTests : IDisposable
{
    private const uint StartSeed = 0x5717;   // synthetic: the start save every group was given
    private const uint OwnSeed = 0x0A0B;     // synthetic: a playthrough of the joiner's own
    private const string Mt = "1.5.5-release_1_5", Regular = "1.5.5-15315-release_1_5";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "kcdmp-wo157-" + Guid.NewGuid().ToString("N"));
    private string Saves => Path.Combine(_root, "saves");

    public Wo157WorldTests() => Directory.CreateDirectory(Saves);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private void Put(string pl, string name, uint seed, long t, string build = Mt, bool pristine = false)
    {
        Directory.CreateDirectory(Path.Combine(Saves, pl));
        File.WriteAllBytes(Path.Combine(Saves, pl, name), WhsSaveTests.File(new Spec { Seed = seed, SaveTime = t, Build = build, Pristine = pristine }));
    }

    private HenryStore Store() => new(Path.Combine(_root, "henry"));

    private static WhsSave.PlayerInfo Who(GameBridge.OwnSave s) => WhsSave.PlayerOf(WhsSave.Inflate(File.ReadAllBytes(s.Save.FullPath)).Raw);

    /// <summary>What a first join offers, the way the agent decides it: (the no-save reason, Bring, a new character).</summary>
    private (string Reason, bool Bring, bool Fresh) Offer(uint hostSeed, string hostBuild, Func<uint, bool>? hostedHere)
    {
        var own = GameBridge.OwnSaves(Saves, hostSeed, null, hostedHere);
        var same = GameBridge.SameBuildSaves(own, hostBuild);
        bool bring = same.Any(s => Who(s).IsHenry);
        bool fresh = Wo157Rules.FreshCandidates(same, GameBridge.OwnSaves(Saves, null), hostBuild, hostSeed)
            .Any(s => Who(s) is { IsHenry: true, Pristine: true });
        int all = GameBridge.ListOwnSaves(Saves).Count;
        var builds = own.Select(s => WhsSave.ReadBuildFromFile(s.Save.FullPath)).ToList();
        string reason = bring ? "" : Wo154Rules.NoSaveReason(all, own.Count,
            builds.Count(b => Wo135Rules.SameBuild(b, hostBuild)), builds.Count(Wo154Rules.IsRegularGameBuild), hostBuild);
        return (reason, bring, fresh);
    }

    [Fact]
    public void Case1_a_fresh_joiner_is_told_plainly_and_joins_with_a_new_character_from_the_start_save_he_was_given()
    {
        Assert.Equal(("no-saves", false, false), Offer(StartSeed, Mt, null));   // nothing at all: said so
        Assert.Contains("no Modding Tools saves yet", Wo154Rules.NoSaveMessage("no-saves", false, null, Mt));
        // the start save his group handed out: the host's seed, a Henry who has done nothing yet
        Put("playline0", "permanent002.whs", StartSeed, 100, pristine: true);
        var o = Offer(StartSeed, Mt, null);
        Assert.Equal(("only-host-copies", false, true), o);   // 0.45.1: the same reason, and no way in
        string msg = Wo154Rules.NoSaveMessage(o.Reason, o.Fresh, null, Mt);
        Assert.Contains("all of this same world", msg);
        Assert.EndsWith("You can join with a new character.", msg);
    }

    [Fact]
    public void Case2_a_joiner_who_hosted_the_same_start_save_brings_his_own_character()
    {
        Put("playline0", "permanent002.whs", StartSeed, 100, pristine: true);   // the start save
        Put("playline0", "autosave018.whs", StartSeed, 300);                     // his own play of it, as a host
        Put("playline0", "exit.whs", StartSeed, 400);
        // 0.45.1 (and any install that never hosted it): all copies, Bring gone
        Assert.Equal(("only-host-copies", false, true), Offer(StartSeed, Mt, null));
        var st = Store();
        st.MarkHosted(WhsSave.SeedTag(StartSeed));
        Func<uint, bool> hosted = s => st.HostedHere(WhsSave.SeedTag(s));
        var o = Offer(StartSeed, Mt, hosted);
        Assert.True(o.Bring);
        Assert.True(o.Fresh);
        Assert.Equal(3, GameBridge.OwnSaves(Saves, StartSeed, null, hosted).Count);
        Assert.Equal("playline0/exit.whs", GameBridge.OwnSaves(Saves, StartSeed, null, hosted)[0].Save.Display);   // newest first
        // the mark is per world: a save of a world he never hosted is still a copy when that world is the host's
        Put("playline1", "quicksave005.whs", 0x7777, 500);
        Assert.DoesNotContain(GameBridge.OwnSaves(Saves, 0x7777, null, hosted), x => x.Seed == 0x7777);
    }

    [Fact]
    public void Case3_a_joiner_with_only_regular_game_saves_is_told_what_the_Modding_Tools_need()
    {
        Put("playline2", "quicksave030.whs", OwnSeed, 300, build: Regular);
        Put("playline2", "permanent002.whs", OwnSeed, 100, build: Regular, pristine: true);
        var o = Offer(StartSeed, Mt, null);
        Assert.Equal(("regular-game-saves", false, false), o);   // a regular-game save can't be loaded by the Modding Tools
        string msg = Wo154Rules.NoSaveMessage(o.Reason, o.Fresh, Regular, Mt);
        Assert.Contains("regular game", msg);
        Assert.Contains("Start a new game in the Modding Tools once", msg);
    }

    [Fact]
    public void Case4_a_joiner_with_his_own_Modding_Tools_save_brings_his_character_as_before()
    {
        Put("playline3", "save010.whs", OwnSeed, 200);
        Put("playline3", "permanent002.whs", OwnSeed, 100, pristine: true);
        Put("playline0", "permanent002.whs", StartSeed, 50, pristine: true);   // a copy of the start save beside them
        var o = Offer(StartSeed, Mt, null);
        Assert.True(o.Bring);
        Assert.True(o.Fresh);
        // the new character comes from his OWN first save first; the start save's copy only after his own
        var same = GameBridge.SameBuildSaves(GameBridge.OwnSaves(Saves, StartSeed), Mt);
        var order = Wo157Rules.FreshCandidates(same, GameBridge.OwnSaves(Saves, null), Mt, StartSeed);
        Assert.Equal(["playline3/permanent002.whs", "playline3/save010.whs", "playline0/permanent002.whs"], order.Select(x => x.Save.Display));
    }

    [Fact]
    public void Two_hosts_of_the_same_start_save_are_two_worlds_on_a_joiner()
    {
        const uint hostA = 0x1111AAAA, hostB = 0x2222BBBB;
        string plain = WhsSave.SeedTag(StartSeed);
        Assert.Equal(plain, Wo157Rules.WorldTag(StartSeed, null, null));      // a host without WO-157: as before
        Assert.Equal(plain, Wo157Rules.WorldTag(StartSeed, hostA, null));     // the first host claims the plain folder
        Assert.Equal(plain, Wo157Rules.WorldTag(StartSeed, hostA, hostA));
        string b = Wo157Rules.WorldTag(StartSeed, hostB, hostA);              // another host of the same seed
        Assert.NotEqual(plain, b);
        Assert.Matches("^[0-9a-f]{10}$", b);
        Assert.Equal(b, WhsSave.HostWorldTag(StartSeed, hostB));
        Assert.NotEqual(WhsSave.HostWorldTag(StartSeed, hostA), b);

        var st = Store();
        Assert.Null(st.WorldHostKey(plain));
        st.ClaimHost(plain, plain, hostA);
        Assert.Equal(hostA, st.WorldHostKey(plain));
        st.ClaimHost(plain, plain, hostB);                                    // never re-claimed by another host
        Assert.Equal(hostA, st.WorldHostKey(plain));
        st.ClaimHost(b, plain, hostB);
        Assert.Equal(plain, st.BaseTagOf(b));
        Assert.Equal(plain, st.BaseTagOf(plain));
        Assert.False(st.HasWorld(b));                                         // a claim alone is no snapshot
        uint k = st.InstallKey();
        Assert.NotEqual(0u, k);
        Assert.Equal(k, Store().InstallKey());                                // made once, kept
    }

    [Fact]
    public void The_host_key_is_a_join_status_a_joiner_without_WO157_ignores()
    {
        Assert.Equal(10, Protocol.JoinStateHostKey);
        Assert.Equal("host-key", Protocol.JoinStateName(Protocol.JoinStateHostKey));
        Assert.Equal(33, Array.IndexOf(Protocol.JoinBusyReasons, "host-key"));   // appended: every earlier id unchanged
        var p = JoinStatusCodec.Build(Protocol.JoinTargetHost, 0xDEADBEEF, Protocol.JoinStateHostKey, Protocol.JoinReasonId("host-key"), 0);
        Assert.Equal(3 + Protocol.JoinHeaderLen + 4, p.Length);                  // the same 9-byte body: the relay's gate passes it
    }
}
