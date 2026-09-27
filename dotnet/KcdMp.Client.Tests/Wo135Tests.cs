using KcdMp.Wire;
using Spec = KcdMp.Client.Tests.WhsSaveTests.Spec;

namespace KcdMp.Client.Tests;

/// <summary>WO-135: the avatar is a puppet, knockouts both ways, outfits, same-build saves (docs/WO-135-findings.md).</summary>
public class Wo135Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kcdmp-wo135-" + Guid.NewGuid().ToString("N"));
    public Wo135Tests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static readonly Guid Padded = Guid.Parse("46b051c4-d4e2-4f3a-8b88-e3f64dae4618");
    private static readonly Guid Plate = Guid.Parse("a5322fcd-27b4-4f4e-bfbf-49c519c74c74");
    private static readonly Guid Helmet = Guid.Parse("b6fe59ec-c854-402a-848e-a77f55661c19");

    // ---------------------------------------------------------------- Phase 1

    [Fact]
    public void The_quiet_mask_parses_and_names_its_groups()
    {
        Assert.Equal((byte)15, Wo135Rules.ParseQuiet("quiet=15"));
        Assert.Equal((byte)6, Wo135Rules.ParseQuiet("x=1 quiet=6"));
        Assert.Null(Wo135Rules.ParseQuiet("quiet=16"));
        Assert.Null(Wo135Rules.ParseQuiet("quiet=-1"));
        Assert.Null(Wo135Rules.ParseQuiet(""));
        Assert.Equal("speech,witness,react,defence", Wo135Rules.QuietGroups(15));
        Assert.Equal("none", Wo135Rules.QuietGroups(0));
        Assert.Equal(Wo135Rules.QuietAll, (byte)0x0F);   // the default the agent pushes (MotionConfig byte 5)
    }

    // ---------------------------------------------------------------- Phase 2

    [Fact]
    public void A_knockout_event_and_a_takedown_are_checked_field_by_field()
    {
        Assert.True(Wo135Rules.TryParseKo("ttkc_drozd 1", out var n, out var down));
        Assert.Equal("ttkc_drozd", n); Assert.True(down);
        Assert.True(Wo135Rules.TryParseKo("wo135_s1 0", out _, out down)); Assert.False(down);
        Assert.False(Wo135Rules.TryParseKo("bad name 1", out _, out _));
        Assert.False(Wo135Rules.TryParseKo("x\"); os.exit() -- 1", out _, out _));
        Assert.False(Wo135Rules.TryParseKo("npc 2", out _, out _));

        Assert.True(Wo135Rules.TryParseTakedown("ttkc_drozd mercy", out n, out var k));
        Assert.Equal(("ttkc_drozd", "mercy"), (n, k));
        Assert.True(Wo135Rules.TryParseTakedown("a knockout", out _, out _));
        Assert.True(Wo135Rules.TryParseTakedown("a stealth", out _, out _));
        Assert.False(Wo135Rules.TryParseTakedown("a pickpocket", out _, out _));   // pickpocketing stays blocked, never a request
        Assert.False(Wo135Rules.TryParseTakedown("a", out _, out _));

        Assert.True(Wo135Rules.TryParseTakedownResult("ok wo135_ko mercy", out var r, out n, out k));
        Assert.Equal(("ok", "wo135_ko", "mercy"), (r, n, k));
        Assert.True(Wo135Rules.TryParseTakedownResult("refused wo135_ko knockout", out r, out _, out _));
        Assert.Equal("refused", r);
        Assert.False(Wo135Rules.TryParseTakedownResult("maybe wo135_ko mercy", out _, out _, out _));
    }

    [Fact]
    public void The_new_loot_kinds_are_appended_and_round_trip()
    {
        // APPEND-ONLY: the WO-134 kinds keep their numbers.
        Assert.Equal(4, Protocol.LootAskItemTake);
        Assert.Equal(5, Protocol.LootAskTakedown);
        Assert.Equal(5, Protocol.LootHostLedger);
        Assert.Equal(6, Protocol.LootHostTakedownResult);
        Assert.Equal(7, Protocol.LootHostBuild);
        Assert.Equal("takedown", Protocol.LootAskName(Protocol.LootAskTakedown));
        Assert.Equal("build", Protocol.LootHostName(Protocol.LootHostBuild));
        var m = new LootMsg(Protocol.LootAskTakedown, 7, "ttkc_drozd mercy");
        var pkt = m.BuildUp(Protocol.LootAskUp, Protocol.JoinTargetHost);
        Assert.True(LootMsg.TryDecode(pkt.AsSpan(3 + Protocol.JoinHeaderLen), out var got));
        Assert.Equal(m, got);
        var b = new LootMsg(Protocol.LootHostBuild, 0, "1.5.5-release_1_5");
        Assert.True(LootMsg.TryDecode(b.BuildUp(Protocol.LootHostUp, 2).AsSpan(3 + Protocol.JoinHeaderLen), out got));
        Assert.Matches(Wo135Rules.BuildText, got.Text);
    }

    // ---------------------------------------------------------------- Phase 4

    [Fact]
    public void The_outfit_diff_is_against_the_real_set_not_a_belief()
    {
        // The field case: the avatar never wore the spawn preset, so its real set is
        // its roster soul's own. A peer outfit with the padded layer and plate on it
        // must add BOTH -- the old diff (seeded with the preset) skipped the padded one.
        var actual = new HashSet<Guid> { Helmet };
        var target = new HashSet<Guid> { Plate, Padded };
        Assert.Equal(new[] { Helmet }, Wo135Rules.AppearanceRemovals(actual, target));
        Assert.Equal(new HashSet<Guid> { Plate, Padded }, new HashSet<Guid>(Wo135Rules.AppearanceAdditions(actual, target)));
        // Converged: nothing to do.
        Assert.Empty(Wo135Rules.AppearanceAdditions(target, target));
        Assert.Empty(Wo135Rules.AppearanceRemovals(target, target));
    }

    [Fact]
    public void A_refused_class_is_skipped_only_for_the_outfit_it_failed_in()
    {
        var u = new Wo135Rules.Unwearable();
        var outfit1 = new HashSet<Guid> { Plate, Padded };
        u.OnTarget(outfit1);
        Assert.True(u.Mark(Plate));    // logged once
        Assert.False(u.Mark(Plate));   // not again
        Assert.True(u.Skips(Plate));
        Assert.False(u.Skips(Padded)); // never the whole outfit
        u.OnTarget(new HashSet<Guid> { Padded, Plate });   // the same outfit (a heartbeat): still skipped
        Assert.True(u.Skips(Plate));
        u.OnTarget(new HashSet<Guid> { Plate, Padded, Helmet });   // the outfit changed: tried again
        Assert.False(u.Skips(Plate));
        Assert.Equal(0, u.Count);
    }

    // ---------------------------------------------------------------- Phase 5

    [Fact]
    public void A_build_reads_from_the_header_alone_and_prints_plainly()
    {
        string f = Path.Combine(_root, "a.whs");
        File.WriteAllBytes(f, WhsSaveTests.File(new Spec { Build = "1.5.6-15693-release_1_5" }));
        Assert.Equal("1.5.6-15693-release_1_5", WhsSave.ReadBuildFromFile(f));
        Assert.Null(WhsSave.ReadBuildFromFile(Path.Combine(_root, "missing.whs")));
        Assert.Equal("1.5.6 (build 15693)", Wo135Rules.PlainBuild("1.5.6-15693-release_1_5"));
        Assert.Equal("1.5.5", Wo135Rules.PlainBuild("1.5.5-release_1_5"));
        Assert.Equal("unknown", Wo135Rules.PlainBuild(null));
        Assert.True(Wo135Rules.SameBuild("1.5.5-release_1_5", "1.5.5-release_1_5"));
        Assert.False(Wo135Rules.SameBuild("1.5.6-15693-release_1_5", "1.5.5-release_1_5"));
        Assert.False(Wo135Rules.SameBuild(null, "1.5.5-release_1_5"));
    }

    [Fact]
    public void Only_saves_of_the_hosts_build_are_Henry_sources_newest_first_and_skips_are_logged_once()
    {
        string saves = Path.Combine(_root, "saves");
        void Put(string pl, string name, string build, long t)
        {
            Directory.CreateDirectory(Path.Combine(saves, pl));
            File.WriteAllBytes(Path.Combine(saves, pl, name), WhsSaveTests.File(new Spec { Seed = 0x2222, SaveTime = t, Build = build }));
        }
        Put("playline2", "quicksave030.whs", "1.5.6-15693-release_1_5", 300);   // the regular game, newest
        Put("playline2", "quicksave029.whs", "1.5.5-release_1_5", 200);         // the Modding Tools build
        Put("playline3", "permanent002.whs", "1.5.2-14493-release_1_5", 100);   // an old new game
        Put("playline3", "save010.whs", "1.5.5-release_1_5", 50);
        var own = GameBridge.OwnSaves(saves, 0x1111);
        Assert.Equal(4, own.Count);
        var log = new List<string>();
        var same = GameBridge.SameBuildSaves(own, "1.5.5-release_1_5", log.Add);
        Assert.Equal(["playline2/quicksave029.whs", "playline3/save010.whs"], same.Select(o => o.Save.Display));
        Assert.Equal(2, log.Count);   // one line per other build
        Assert.Contains(log, l => l.Contains("1.5.6-15693-release_1_5") && l.Contains("quicksave030"));
        Assert.Contains(log, l => l.Contains("1.5.2-14493-release_1_5") && l.Contains("permanent002"));
        log.Clear();
        GameBridge.SameBuildSaves(own, "1.5.5-release_1_5", log.Add);
        Assert.Empty(log);            // each file once
        Assert.Empty(GameBridge.SameBuildSaves(own, "1.9.9-99999-release_1_9", null).Where(o => o.Save.File == "quicksave029.whs"));
    }

    [Fact]
    public void The_joiner_is_offered_only_what_exists_from_the_hosts_build()
    {
        const string host = "1.5.5-release_1_5";
        Assert.Equal("choose", Wo135Rules.ChooseUi(Wo135Rules.OfferFor(true, true), host, host).State);
        Assert.Equal("choose-bring", Wo135Rules.ChooseUi(Wo135Rules.OfferFor(true, false), host, host).State);
        Assert.Equal("choose-fresh", Wo135Rules.ChooseUi(Wo135Rules.OfferFor(false, true), host, host).State);
        var none = Wo135Rules.ChooseUi(Wo135Rules.OfferFor(false, false), "1.5.6-15693-release_1_5", host);
        Assert.Equal("wrong-build", none.State);
        Assert.Equal("Your saves are from game version 1.5.6 (build 15693), but your host's game is version 1.5.5. " +
                     "Start or load a game in the Modding Tools build and save once, then join again.", none.Message);
    }

    [Fact]
    public void The_launcher_shows_only_the_offered_button()
    {
        var b = new KCDMP_launcher.Models.AgentStatusBanner();
        b.Start(false, 0);
        double t = 1;
        void Js(string state) => b.Apply(new KCDMP_launcher.Models.ConnectionStatusData { State = "connected", Message = "" },
                                         new KCDMP_launcher.Models.JoinStatusData { State = state, Message = "m" }, true, t++);
        Js("choose-fresh");
        Assert.True(b.ShowChoiceButtons); Assert.False(b.ShowBring); Assert.True(b.ShowFresh);
        Js("choose-bring");
        Assert.True(b.ShowBring); Assert.False(b.ShowFresh);
        Js("wrong-build");
        Assert.False(b.ShowChoiceButtons);
    }
}
