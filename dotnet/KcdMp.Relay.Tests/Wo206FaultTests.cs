// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using KcdMp.Client;
using KcdMp.Server;
using KcdMp.Server.Features.ClientHandling;
using KcdMp.Server.Features.Faults;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KcdMp.Relay.Tests;

// =============================================================================
// WO-206 B: the relay's test-only fault mode (dotnet/KcdMp.Server/Features/Faults).
//
// THE TWIN RULE: every check here has a twin -- the same measurement with the
// fault (or its switch) off -- and each check is one predicate that must be
// TRUE with the fault on and FALSE on its twin. A check that passes either way
// proves nothing, so the twin is what shows the predicate can see the fault.
//
// The collection runs alone (DisableParallelization): the switch's first half
// is a process environment variable, set only around one Program.CreateApp.
// tools/Test-RelayFaultsGate.ps1 runs these by the filter "Wo206Fault".
// =============================================================================

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wo206FaultCollection
{
    public const string Name = "wo206-relay-faults";
}

/// <summary>One real relay with (or without) a fault plan, on free loopback ports.</summary>
public sealed class FaultRelay : IAsyncDisposable
{
    public int TcpPort { get; private set; }
    public WebApplication App { get; private set; } = null!;
    private string? _planPath;

    /// <summary>The fault engine, or null when the relay runs without faults.</summary>
    public RelayFaults? Faults => App.Services.GetService<RelayFaults>();

    /// <summary>
    /// Starts a relay. <paramref name="env"/> sets KCDMP_TEST_RELAY_FAULTS=1 around
    /// CreateApp (cleared otherwise); <paramref name="option"/> passes --test-faults
    /// with the plan written to a temp file.
    /// </summary>
    public static async Task<FaultRelay> StartAsync(string? planJson, bool env = true, bool option = true)
    {
        var r = new FaultRelay { TcpPort = FreePort() };
        int httpPort = FreePort();
        var args = new List<string>
        {
            "--port", r.TcpPort.ToString(),
            "--Urls", $"http://127.0.0.1:{httpPort}",
            "--Serilog:WriteTo:1:Name", "Console",
        };
        if (planJson is not null)
        {
            r._planPath = Path.Combine(Path.GetTempPath(), $"wo206-plan-{Guid.NewGuid():N}.json");
            File.WriteAllText(r._planPath, planJson);
            if (option) { args.Add(RelayFaultSwitch.Option); args.Add(r._planPath); }
        }

        string? before = Environment.GetEnvironmentVariable(RelayFaultSwitch.EnvVar);
        Environment.SetEnvironmentVariable(RelayFaultSwitch.EnvVar, env ? "1" : null);
        try { r.App = Program.CreateApp([.. args]); }
        finally { Environment.SetEnvironmentVariable(RelayFaultSwitch.EnvVar, before); }

        await r.App.StartAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, r.TcpPort);
                return r;
            }
            catch (SocketException) when (DateTime.UtcNow < deadline) { await Task.Delay(50); }
        }
    }

    public RelayFaults.RuleState Rule(string name) => Faults!.Rules.Single(x => x.Rule.Name == name);

    /// <summary>Waits until the named rule is active; without faults, just lets the relay settle.</summary>
    public async Task WaitActiveAsync(string name)
    {
        if (Faults is null) { await Task.Delay(200); return; }
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!Rule(name).IsActive && DateTime.UtcNow < until) await Task.Delay(10);
        Assert.True(Rule(name).IsActive, $"rule {name} never fired");
    }

    public async ValueTask DisposeAsync()
    {
        try { await App.StopAsync(); } catch (ObjectDisposedException) { }   // a test may have stopped it already (the final summary)
        await App.DisposeAsync();
        if (_planPath is not null) try { File.Delete(_planPath); } catch { }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

[Collection(Wo206FaultCollection.Name)]
public class Wo206FaultTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private const uint JoinId = 0x00C0FFEE;

    // ---------------------------------------------------------------- helpers

    private static byte[] Beat(string text)
    {
        var t = Encoding.ASCII.GetBytes(text);
        var p = new byte[3 + 2 + t.Length];
        p[0] = Protocol.StoryBeatUp;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), (ushort)(2 + t.Length));
        p[3] = 1;
        p[4] = (byte)t.Length;
        t.CopyTo(p, 5);
        return p;
    }

    private static string BeatText(byte[] down) => Encoding.ASCII.GetString(down, 3, down[2]);

    /// <summary>The host first (id 0: the damage authority), then the joiner.</summary>
    private static async Task<(Peer Host, Peer Joiner)> PairAsync(FaultRelay relay, string joinerName = "w206-joiner")
    {
        var host = await Peer.ConnectAsync(relay.TcpPort, "w206-host");
        var joiner = await Peer.ConnectAsync(relay.TcpPort, joinerName);
        await Task.Delay(150);
        return (host, joiner);
    }

    private static byte[] JoinRequest() => Protocol.BuildJoinUp(Protocol.JoinRequestUp, Protocol.JoinTargetHost, JoinId, [0]);

    private static byte[] Paused(byte joiner) => Protocol.BuildJoinUp(Protocol.JoinStatusUp, joiner, JoinId, [Protocol.JoinStatePaused, 0, 0, 0]);

    private static byte[] Offer(byte joiner, int chunks) =>
        Protocol.BuildJoinUp(Protocol.WorldOfferUp, joiner, JoinId, new WorldOffer(chunks * 5, 5, chunks, new byte[32], 7, new byte[16]).Encode());

    private static byte[] Chunk(byte joiner, int index)
    {
        var b = new byte[4 + 5];
        BinaryPrimitives.WriteInt32LittleEndian(b, index);
        return Protocol.BuildJoinUp(Protocol.WorldChunkUp, joiner, JoinId, b);
    }

    private static byte[] Done() => Protocol.BuildJoinUp(Protocol.WorldDoneUp, Protocol.JoinTargetHost, JoinId, new byte[8]);

    private static byte[] Progress(string where) =>
        new LootMsg(Protocol.W203JoinProgress, 1, $"v1|{JoinId:x8}|{where}|3").BuildUp(Protocol.W203Up, Protocol.JoinTargetHost);

    private static byte[] Ready() => Protocol.BuildJoinUp(Protocol.JoinerReadyUp, Protocol.JoinTargetHost, JoinId, BitConverter.GetBytes(7u));

    /// <summary>Reads StoryBeats until <paramref name="n"/> arrived; each with its arrival time (Stopwatch ms).</summary>
    private static async Task<List<(string Text, double AtMs)>> ReadBeatsAsync(Peer p, int n, Stopwatch clock, TimeSpan timeout)
    {
        var got = new List<(string, double)>();
        var until = DateTime.UtcNow + timeout;
        while (got.Count < n)
        {
            var left = until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) break;
            try
            {
                var (type, payload) = await p.ReadPacketAsync(left);
                if (type == Protocol.StoryBeatDown) got.Add((BeatText(payload), clock.Elapsed.TotalMilliseconds));
            }
            catch (OperationCanceledException) { break; }
        }
        return got;
    }

    /// <summary>True if the peer's connection is closed by the relay within the window (EOF or reset).</summary>
    private static async Task<bool> ClosedWithinAsync(Peer p, TimeSpan window)
    {
        var until = DateTime.UtcNow + window;
        try
        {
            while (DateTime.UtcNow < until) await p.ReadPacketAsync(until - DateTime.UtcNow);
        }
        catch (EndOfStreamException) { return true; }
        catch (IOException) { return true; }
        catch (OperationCanceledException) { return false; }
        return false;
    }

    private static string Plan(string rulesJson, int seed = 206) => $"{{ \"seed\": {seed}, \"rules\": [ {rulesJson} ] }}";

    // ---------------------------------------------------------------- lag

    private const int LagMs = 250;
    private static readonly string LagPlan = Plan($"{{ \"name\": \"lagd\", \"kind\": \"lag\", \"role\": \"joiner\", \"dir\": \"down\", \"ms\": {LagMs}, \"at\": \"t:+0.3\" }}");

    private static bool LagHolds(IReadOnlyList<double> latencies) => latencies.Count == 8 && latencies.Min() >= LagMs;

    private static async Task<List<double>> MeasureLagAsync(FaultRelay relay)
    {
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        await relay.WaitActiveAsync("lagd");
        var clock = Stopwatch.StartNew();
        var sent = new Dictionary<string, double>();
        var reader = ReadBeatsAsync(joiner, 8, clock, TimeSpan.FromSeconds(8));
        for (int i = 0; i < 8; i++)
        {
            sent[$"lag{i}"] = clock.Elapsed.TotalMilliseconds;
            await host.SendRawAsync(Beat($"lag{i}"));
            await Task.Delay(60);
        }
        var got = await reader;
        return got.Select(g => g.AtMs - sent[g.Text]).ToList();
    }

    [Fact]
    public async Task Wo206Fault_lag_delays_the_joiners_down_link_by_at_least_its_ms()
    {
        await using var relay = await FaultRelay.StartAsync(LagPlan);
        var lat = await MeasureLagAsync(relay);
        Assert.True(LagHolds(lat), $"latencies {string.Join(",", lat.Select(l => l.ToString("0")))} ms, lag {LagMs}");
        Assert.True(lat.Max() < LagMs + 500);   // a delay, not a stall
        Assert.True(relay.Rule("lagd").FramesHeld >= 8);
        Assert.Contains(relay.Faults!.RecentLines(), l => l.StartsWith("RELAY-FAULT lagd fired kind=lag role=joiner dir=down at=t:+0.3 via=time"));
        Assert.Matches(@"delayed=([1-9]\d*)/\d+B", relay.Faults.SummaryLine(false));
    }

    [Fact]
    public async Task Wo206Fault_lag_twin_without_the_switch_arrives_sooner()
    {
        await using var relay = await FaultRelay.StartAsync(LagPlan, env: false);
        Assert.Null(relay.Faults);
        var lat = await MeasureLagAsync(relay);
        Assert.Equal(8, lat.Count);
        Assert.False(LagHolds(lat), $"latencies {string.Join(",", lat.Select(l => l.ToString("0")))} ms without faults");
    }

    // ---------------------------------------------------------------- jitter-hold

    private const int HoldMs = 400, EveryMs = 1000, HoldSends = 110, HoldSendEveryMs = 25;
    private static readonly string HoldPlan = Plan($"{{ \"name\": \"hold\", \"kind\": \"jitter-hold\", \"role\": \"joiner\", \"dir\": \"down\", \"hold_ms\": {HoldMs}, \"every_ms\": {EveryMs}, \"at\": \"step:request\" }}");

    /// <summary>Bursts: a gap of about the hold, then at least 8 messages within 30 ms of each other. Two of them.</summary>
    private static int BurstsSeen(IReadOnlyList<double> at)
    {
        int bursts = 0;
        for (int i = 1; i < at.Count; i++)
        {
            double gap = at[i] - at[i - 1];
            if (gap < HoldMs * 0.75 || gap > HoldMs + 150) continue;
            int together = 1;
            while (i + together < at.Count && at[i + together] - at[i] <= 30) together++;
            if (together >= 8) bursts++;
        }
        return bursts;
    }

    private static async Task<List<(string Text, double AtMs)>> MeasureHoldAsync(FaultRelay relay)
    {
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        await joiner.SendRawAsync(JoinRequest());
        await relay.WaitActiveAsync("hold");
        var clock = Stopwatch.StartNew();
        var reader = ReadBeatsAsync(joiner, HoldSends, clock, TimeSpan.FromSeconds(10));
        for (int i = 0; i < HoldSends; i++)
        {
            await host.SendRawAsync(Beat($"h{i:00}"));
            await Task.Delay(HoldSendEveryMs);
        }
        return await reader;
    }

    [Fact]
    public async Task Wo206Fault_jitter_hold_releases_held_messages_as_bursts()
    {
        await using var relay = await FaultRelay.StartAsync(HoldPlan);
        var got = await MeasureHoldAsync(relay);
        Assert.Equal(Enumerable.Range(0, HoldSends).Select(i => $"h{i:00}"), got.Select(g => g.Text));   // in order, none lost
        int bursts = BurstsSeen(got.Select(g => g.AtMs).ToList());
        Assert.True(bursts >= 2, $"bursts seen {bursts}");
        Assert.True(relay.Rule("hold").BurstCount >= 2);
        Assert.Contains(relay.Faults!.RecentLines(), l => l.StartsWith("RELAY-FAULT hold fired kind=jitter-hold") && l.Contains("via=join-request"));
    }

    [Fact]
    public async Task Wo206Fault_jitter_hold_twin_without_the_switch_has_no_bursts()
    {
        await using var relay = await FaultRelay.StartAsync(HoldPlan, env: false);
        var got = await MeasureHoldAsync(relay);
        Assert.Equal(HoldSends, got.Count);
        Assert.Equal(0, BurstsSeen(got.Select(g => g.AtMs).ToList()));
    }

    // ---------------------------------------------------------------- throttle

    private const int Rate = 4000, ThrottleSends = 60;
    private static readonly string ThrottlePlan = Plan($"{{ \"name\": \"thin\", \"kind\": \"throttle\", \"role\": \"joiner\", \"dir\": \"down\", \"bytes_per_s\": {Rate}, \"at\": \"step:request\" }}");

    /// <summary>Throughput after the first frame, in bytes/s, within 20 % of the cap.</summary>
    private static bool CappedNearRate(double bytesPerS) => bytesPerS >= Rate * 0.8 && bytesPerS <= Rate * 1.2;

    private static async Task<double> MeasureThroughputAsync(FaultRelay relay)
    {
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        await joiner.SendRawAsync(JoinRequest());
        await relay.WaitActiveAsync("thin");
        var clock = Stopwatch.StartNew();
        var reader = ReadBeatsAsync(joiner, ThrottleSends, clock, TimeSpan.FromSeconds(10));
        string text = new('x', 120);
        for (int i = 0; i < ThrottleSends; i++) await host.SendRawAsync(Beat(text[..118] + $"{i:00}"));
        var got = await reader;
        Assert.Equal(ThrottleSends, got.Count);
        int frame = 3 + 3 + 120;                             // StoryBeatDown: [src][kind][len][text]
        double seconds = (got[^1].AtMs - got[0].AtMs) / 1000.0;
        return (ThrottleSends - 1) * frame / Math.Max(seconds, 1e-3);
    }

    [Fact]
    public async Task Wo206Fault_throttle_caps_the_joiners_down_link_near_its_rate()
    {
        await using var relay = await FaultRelay.StartAsync(ThrottlePlan);
        double bps = await MeasureThroughputAsync(relay);
        Assert.True(CappedNearRate(bps), $"throughput {bps:0} B/s, cap {Rate}");
        Assert.True(relay.Rule("thin").FramesHeld >= ThrottleSends / 2);
        Assert.Matches(@"throttled=([1-9]\d*)/\d+B", relay.Faults!.SummaryLine(false));
    }

    [Fact]
    public async Task Wo206Fault_throttle_twin_without_the_switch_is_not_capped()
    {
        await using var relay = await FaultRelay.StartAsync(ThrottlePlan, env: false);
        double bps = await MeasureThroughputAsync(relay);
        Assert.False(CappedNearRate(bps), $"throughput {bps:0} B/s without faults");
    }

    // ---------------------------------------------------------------- stall

    private const int StallMs = 1500, StallSends = 20;
    private static readonly string StallPlan = Plan("{ \"name\": \"freeze\", \"kind\": \"stall\", \"role\": \"joiner\", \"dir\": \"down\", \"for_s\": 1.5, \"at\": \"step:request\" }");
    // The flood needs time to pile up past the limit while the stall holds it.
    private static readonly string FloodPlan = Plan("{ \"name\": \"freeze\", \"kind\": \"stall\", \"role\": \"joiner\", \"dir\": \"down\", \"for_s\": 4, \"at\": \"step:request\" }");

    /// <summary>Nothing for most of the stall, then everything, in order.</summary>
    private static bool StalledThenReleased(IReadOnlyList<(string Text, double AtMs)> got, double sinceRequestMs) =>
        got.Count == StallSends
        && got.Select(g => g.Text).SequenceEqual(Enumerable.Range(0, StallSends).Select(i => $"s{i:00}"))
        && got[0].AtMs - sinceRequestMs >= StallMs * 0.85;

    private static async Task<(List<(string Text, double AtMs)> Got, double RequestAt)> MeasureStallAsync(FaultRelay relay)
    {
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        var clock = Stopwatch.StartNew();
        double requestAt = clock.Elapsed.TotalMilliseconds;
        await joiner.SendRawAsync(JoinRequest());
        await relay.WaitActiveAsync("freeze");
        var reader = ReadBeatsAsync(joiner, StallSends, clock, TimeSpan.FromSeconds(8));
        for (int i = 0; i < StallSends; i++) await host.SendRawAsync(Beat($"s{i:00}"));
        return (await reader, requestAt);
    }

    [Fact]
    public async Task Wo206Fault_stall_holds_a_direction_then_releases_it_in_order_with_nothing_lost()
    {
        await using var relay = await FaultRelay.StartAsync(StallPlan);
        var (got, at) = await MeasureStallAsync(relay);
        Assert.True(StalledThenReleased(got, at), $"{got.Count} arrived, first after {(got.Count > 0 ? got[0].AtMs - at : -1):0} ms");
        Assert.True(relay.Rule("freeze").FramesHeld >= StallSends);
        var until = DateTime.UtcNow.AddSeconds(2);
        while (!relay.Faults!.RecentLines().Any(l => l.StartsWith("RELAY-FAULT freeze ended reason=window")) && DateTime.UtcNow < until) await Task.Delay(20);
        Assert.Contains(relay.Faults.RecentLines(), l => l.StartsWith("RELAY-FAULT freeze ended reason=window") && l.Contains("hollow=0"));
    }

    [Fact]
    public async Task Wo206Fault_stall_twin_without_the_switch_arrives_at_once()
    {
        await using var relay = await FaultRelay.StartAsync(StallPlan, env: false);
        var (got, at) = await MeasureStallAsync(relay);
        Assert.Equal(StallSends, got.Count);
        Assert.False(StalledThenReleased(got, at));
    }

    // ---------------------------------------------------------------- over-limit: counted, not a silent disconnect

    private static async Task<(int Received, long OverLimit, bool Lines)> FloodDuringStallAsync(int messages)
    {
        await using var relay = await FaultRelay.StartAsync(FloodPlan);
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        await joiner.SendRawAsync(JoinRequest());
        await relay.WaitActiveAsync("freeze");
        var clock = Stopwatch.StartNew();
        var reader = ReadBeatsAsync(joiner, messages, clock, TimeSpan.FromSeconds(20));
        string text = new('y', 128);
        for (int i = 0; i < messages; i++) await host.SendRawAsync(Beat(text));
        var got = await reader;
        return (got.Count, relay.Rule("freeze").OverLimitCount,
            relay.Faults!.RecentLines().Any(l => l.StartsWith("RELAY-FAULT freeze over-limit dir=down")));
    }

    [Fact]
    public async Task Wo206Fault_a_held_burst_past_the_session_queue_limit_is_counted_and_the_client_kept()
    {
        int n = RelayFaults.SessionQueueLimitBytes / 134 + 400;   // 134 B a StoryBeatDown of 128 chars: past 512 KB held
        var (received, over, lines) = await FloodDuringStallAsync(n);
        Assert.Equal(n, received);       // not disconnected: all of it arrived after the stall
        Assert.True(over >= 1 && lines);
    }

    [Fact]
    public async Task Wo206Fault_over_limit_twin_under_the_limit_counts_nothing()
    {
        var (received, over, lines) = await FloodDuringStallAsync(200);
        Assert.Equal(200, received);
        Assert.False(over >= 1 && lines);
    }

    // ---------------------------------------------------------------- the join steps

    /// <summary>Drives a whole fake join: request, paused, offer + 2 chunks, done, (progress), ready.</summary>
    private static async Task DriveJoinAsync(Peer host, Peer joiner, string? progressWhere)
    {
        await joiner.SendRawAsync(JoinRequest());
        await host.ReadUntilAsync(Protocol.JoinRequestDown, Wait);
        await host.SendRawAsync(Paused(joiner.Id));
        await joiner.ReadUntilAsync(Protocol.JoinStatusDown, Wait);
        await host.SendRawAsync(Offer(joiner.Id, 2));
        await host.SendRawAsync(Chunk(joiner.Id, 0));
        await host.SendRawAsync(Chunk(joiner.Id, 1));
        await joiner.ReadUntilAsync(Protocol.WorldOfferDown, Wait);
        await joiner.ReadUntilAsync(Protocol.WorldChunkDown, Wait);
        await joiner.ReadUntilAsync(Protocol.WorldChunkDown, Wait);
        await joiner.SendRawAsync(Done());
        await host.ReadUntilAsync(Protocol.WorldDoneDown, Wait);
        if (progressWhere is not null)
        {
            await joiner.SendRawAsync(Progress(progressWhere));
            await host.ReadUntilAsync(Protocol.W203Down, Wait);
        }
        await joiner.SendRawAsync(Ready());
        await host.ReadUntilAsync(Protocol.JoinerReadyDown, Wait);
        await Task.Delay(100);
    }

    private static readonly string StepsPlan = Plan(
        "{ \"name\": \"onload\", \"kind\": \"lag\", \"role\": \"host\", \"dir\": \"up\", \"ms\": 1, \"for_s\": 0.1, \"at\": \"step:loaded\" }," +
        "{ \"name\": \"never\", \"kind\": \"lag\", \"ms\": 1, \"at\": \"t:+3600\" }");

    /// <summary>Every step seen once, and "loaded" read from the joiner's world report (not the fallback).</summary>
    private static bool StepsSeenWithLoadedFromTheReport(RelayFaults f) =>
        RelayFaults.Steps.All(s => f.StepCount(s) == 1)
        && f.RecentLines().Any(l => l.StartsWith("RELAY-FAULT onload fired") && l.Contains("via=w203-join-progress"));

    [Fact]
    public async Task Wo206Fault_every_join_step_is_detected_and_loaded_comes_from_the_world_report()
    {
        await using var relay = await FaultRelay.StartAsync(StepsPlan);
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        await DriveJoinAsync(host, joiner, "world");
        Assert.True(StepsSeenWithLoadedFromTheReport(relay.Faults!),
            string.Join(" ", RelayFaults.Steps.Select(s => $"{s}={relay.Faults!.StepCount(s)}")));
    }

    [Fact]
    public async Task Wo206Fault_steps_twin_a_loading_report_is_not_loaded_and_ready_falls_back()
    {
        await using var relay = await FaultRelay.StartAsync(StepsPlan);
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        await DriveJoinAsync(host, joiner, "loading");
        Assert.False(StepsSeenWithLoadedFromTheReport(relay.Faults!));
        Assert.Equal(1, relay.Faults!.StepCount("loaded"));   // still once: from the ready
        Assert.Contains(relay.Faults.RecentLines(), l => l.StartsWith("RELAY-FAULT onload fired") && l.Contains("via=ready-fallback"));
    }

    // ---------------------------------------------------------------- cut at a join step

    private static readonly string CutPlan = Plan("{ \"name\": \"cutw\", \"kind\": \"cut\", \"role\": \"joiner\", \"at\": \"step:world-sent\" }");

    private static async Task<bool> JoinerCutAfterTheWorldAsync(FaultRelay relay)
    {
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        await joiner.SendRawAsync(JoinRequest());
        await host.ReadUntilAsync(Protocol.JoinRequestDown, Wait);
        await host.SendRawAsync(Paused(joiner.Id));
        await host.SendRawAsync(Offer(joiner.Id, 2));
        await host.SendRawAsync(Chunk(joiner.Id, 0));
        await host.SendRawAsync(Chunk(joiner.Id, 1));
        // The host is told the joiner left: the relay closed the joiner's connection
        try
        {
            var gone = await host.ReadUntilAsync(Protocol.Disconnect, TimeSpan.FromSeconds(2));
            return gone[0] == joiner.Id;
        }
        catch (TimeoutException) { return false; }
        catch (OperationCanceledException) { return false; }
    }

    [Fact]
    public async Task Wo206Fault_cut_fires_at_the_world_sent_step_and_the_counters_say_so()
    {
        await using var relay = await FaultRelay.StartAsync(CutPlan);
        Assert.True(await JoinerCutAfterTheWorldAsync(relay));
        var f = relay.Faults!;
        Assert.Equal(1, f.StepCount("world-sent"));
        Assert.Equal(1, relay.Rule("cutw").CutCount);
        Assert.Contains(f.RecentLines(), l => l.StartsWith("RELAY-FAULT cutw fired kind=cut role=joiner") && l.Contains("at=step:world-sent via=world-chunk") && l.Contains("targets=1"));
        Assert.Contains(f.RecentLines(), l => l.StartsWith("RELAY-FAULT cutw cut id="));
        Assert.Contains(f.RecentLines(), l => l.StartsWith("RELAY-FAULT cutw ended reason=one-shot") && l.Contains("cuts=1") && l.Contains("hollow=0"));
        Assert.Matches(@" cut=1 ", f.SummaryLine(false));
    }

    // WO-207 C.4: "#2" = the step's second time (the rejoin after a host reload is the session's second join): the first join passes,
    // the second is cut
    private static readonly string CutSecondPlan = Plan("{ \"name\": \"cut2\", \"kind\": \"cut\", \"role\": \"joiner\", \"at\": \"step:world-sent#2\" }");

    [Fact]
    public async Task Wo207Fault_a_step_rule_with_an_occurrence_waits_for_that_time()
    {
        await using var relay = await FaultRelay.StartAsync(CutSecondPlan);
        Assert.False(await JoinerCutAfterTheWorldAsync(relay));   // the first world-sent: kept
        Assert.Equal(0, relay.Rule("cut2").CutCount);
        Assert.True(await JoinerCutAfterTheWorldAsync(relay));    // the second: cut
        Assert.Equal(2, relay.Faults!.StepCount("world-sent"));
        Assert.Contains(relay.Faults.RecentLines(), l => l.StartsWith("RELAY-FAULT cut2 fired kind=cut") && l.Contains("at=step:world-sent#2"));
    }

    [Theory]
    [InlineData("step:world-sent#0")]
    [InlineData("step:world-sent#x")]
    [InlineData("step:world-sent#100")]
    public void Wo207Fault_an_occurrence_out_of_range_is_refused(string at)
    {
        var ex = Assert.Throws<FormatException>(() => RelayFaultPlan.Parse("{ \"rules\": [ { \"kind\": \"cut\", \"at\": \"" + at + "\" } ] }"));
        Assert.Contains("#1..#99", ex.Message);
    }

    [Fact]
    public async Task Wo206Fault_cut_twin_without_the_switch_keeps_the_joiner()
    {
        await using var relay = await FaultRelay.StartAsync(CutPlan, env: false);
        Assert.False(await JoinerCutAfterTheWorldAsync(relay));
    }

    // ---------------------------------------------------------------- cut + heal

    private static readonly string HealPlan = Plan("{ \"name\": \"cuth\", \"kind\": \"cut\", \"role\": \"joiner\", \"at\": \"step:request\", \"for_s\": 2 }");

    private static async Task<(bool ImmediateAck, bool LaterAck, bool Cut)> CutThenReconnectAsync(FaultRelay relay)
    {
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        await joiner.SendRawAsync(JoinRequest());
        bool cut = await ClosedWithinAsync(joiner, TimeSpan.FromSeconds(1));

        async Task<bool> AckAsync()
        {
            try
            {
                var (p, type, _) = await Peer.ConnectRawAsync(relay.TcpPort, "w206-joiner", ReleaseVersionInfo.Current, Protocol.Version);
                await p.DisposeAsync();
                return type == Protocol.Ack;
            }
            catch (Exception ex) when (ex is EndOfStreamException or IOException or OperationCanceledException) { return false; }
        }

        bool immediate = await AckAsync();
        await Task.Delay(2300);
        bool later = await AckAsync();
        return (immediate, later, cut);
    }

    private static bool RefusedThenHealed((bool ImmediateAck, bool LaterAck, bool Cut) r) => r.Cut && !r.ImmediateAck && r.LaterAck;

    [Fact]
    public async Task Wo206Fault_cut_refuses_the_cut_client_until_the_heal_then_lets_it_back()
    {
        await using var relay = await FaultRelay.StartAsync(HealPlan);
        var r = await CutThenReconnectAsync(relay);
        Assert.True(RefusedThenHealed(r), $"cut={r.Cut} immediateAck={r.ImmediateAck} laterAck={r.LaterAck}");
        var rule = relay.Rule("cuth");
        Assert.Equal(1, rule.CutCount);
        Assert.True(rule.RefusedCount >= 1);
        Assert.Contains(relay.Faults!.RecentLines(), l => l.StartsWith("RELAY-FAULT cuth refused a reconnect"));
        Assert.Contains(relay.Faults.RecentLines(), l => l.StartsWith("RELAY-FAULT cuth ended reason=healed"));
    }

    [Fact]
    public async Task Wo206Fault_cut_heal_twin_without_the_switch_never_refuses()
    {
        await using var relay = await FaultRelay.StartAsync(HealPlan, env: false);
        Assert.False(RefusedThenHealed(await CutThenReconnectAsync(relay)));
    }

    // ---------------------------------------------------------------- the switch

    private static readonly string AnyPlan = Plan("{ \"kind\": \"lag\", \"ms\": 5 }");

    [Fact]
    public async Task Wo206Fault_switch_both_halves_load_the_plan()
    {
        await using var relay = await FaultRelay.StartAsync(AnyPlan, env: true, option: true);
        Assert.NotNull(relay.Faults);
        Assert.NotNull(relay.App.Services.GetRequiredService<ClientSessionRunner>().Faults);
        Assert.Equal("r0", relay.Faults!.Rules[0].Rule.Name);
    }

    [Fact]
    public async Task Wo206Fault_switch_twin_env_var_without_the_option_loads_nothing()
    {
        await using var relay = await FaultRelay.StartAsync(AnyPlan, env: true, option: false);
        Assert.Null(relay.Faults);
        Assert.Null(relay.App.Services.GetRequiredService<ClientSessionRunner>().Faults);
        // and the relay still relays
        var (host, joiner) = await PairAsync(relay);
        await using var _h = host; await using var _j = joiner;
        await host.SendRawAsync(Beat("plain"));
        Assert.Equal("plain", BeatText(await joiner.ReadUntilAsync(Protocol.StoryBeatDown, Wait)));
    }

    [Fact]
    public async Task Wo206Fault_switch_twin_option_without_the_env_var_loads_nothing_and_is_no_config_key()
    {
        await using var relay = await FaultRelay.StartAsync(AnyPlan, env: false, option: true);
        Assert.Null(relay.Faults);
        Assert.Null(relay.App.Services.GetRequiredService<ClientSessionRunner>().Faults);
        Assert.Null(relay.App.Configuration["test-faults"]);   // the option never reached a configuration source
    }

    [Fact]
    public void Wo206Fault_switch_resolve_needs_both_halves_and_always_strips_the_option()
    {
        string[] a = ["--port", "1", RelayFaultSwitch.Option, "p.json"];
        Assert.Equal("p.json", RelayFaultSwitch.Resolve(ref a, "1", out var note));
        Assert.Equal(new[] { "--port", "1" }, a);
        Assert.Null(note);

        string[] b = ["--port", "1", RelayFaultSwitch.Option + "=p.json"];
        Assert.Null(RelayFaultSwitch.Resolve(ref b, null, out note));       // twin: no env var
        Assert.Equal(new[] { "--port", "1" }, b);
        Assert.Contains(RelayFaultSwitch.EnvVar, note);

        string[] c = ["--port", "1"];
        Assert.Null(RelayFaultSwitch.Resolve(ref c, "1", out note));        // twin: no option
        Assert.Null(note);                                                   // the env var alone says nothing

        string[] d = [RelayFaultSwitch.Option, "p.json"];
        Assert.Null(RelayFaultSwitch.Resolve(ref d, "true", out _));         // only exactly "1"
    }

    // ---------------------------------------------------------------- non-hollow

    [Fact]
    public async Task Wo206Fault_a_rule_that_never_fired_makes_the_final_summary_hollow()
    {
        await using var relay = await FaultRelay.StartAsync(Plan("{ \"name\": \"late\", \"kind\": \"stall\", \"for_s\": 1, \"at\": \"step:ready\" }"));
        var f = relay.Faults!;
        await relay.App.StopAsync();   // exit: the final summary
        Assert.Contains(f.RecentLines(), l => l.StartsWith("RELAY-FAULT late never fired at=step:ready effect=0 hollow=1"));
        Assert.Contains(f.RecentLines(), l => l.StartsWith("RELAY-FAULTS ") && l.Contains("final=1") && l.Contains("hollow=1"));
    }

    [Fact]
    public async Task Wo206Fault_hollow_twin_a_rule_that_did_its_work_is_not_hollow()
    {
        await using var relay = await FaultRelay.StartAsync(LagPlan);
        await MeasureLagAsync(relay);
        var f = relay.Faults!;
        await relay.App.StopAsync();
        Assert.Contains(f.RecentLines(), l => l.StartsWith("RELAY-FAULT lagd ended reason=exit") && l.Contains("hollow=0"));
        Assert.Contains(f.RecentLines(), l => l.StartsWith("RELAY-FAULTS ") && l.Contains("final=1") && l.Contains("hollow=0"));
    }

    // ---------------------------------------------------------------- seeded

    [Fact]
    public void Wo206Fault_the_same_seed_is_the_same_jitter_schedule()
    {
        var a = RelayFaults.JitterDraws(206, 0, 2, FaultDir.Down, 80, 64);
        var b = RelayFaults.JitterDraws(206, 0, 2, FaultDir.Down, 80, 64);
        Assert.Equal(a, b);
        Assert.Contains(a, x => x > 0);
        Assert.All(a, x => Assert.InRange(x, 0, 80));
    }

    [Fact]
    public void Wo206Fault_seed_twin_another_seed_is_another_schedule()
    {
        var a = RelayFaults.JitterDraws(206, 0, 2, FaultDir.Down, 80, 64);
        var c = RelayFaults.JitterDraws(207, 0, 2, FaultDir.Down, 80, 64);
        Assert.NotEqual(a, c);
    }

    // ---------------------------------------------------------------- the plan

    [Theory]
    [InlineData("{ \"rules\": [ { \"kind\": \"lag\", \"ms\": 5, \"bogus\": 1 } ] }", "unknown key 'bogus'")]
    [InlineData("{ \"rules\": [ { \"kind\": \"stall\" } ] }", "for_s > 0")]
    [InlineData("{ \"rules\": [ { \"kind\": \"lag\", \"ms\": 5, \"at\": \"step:nowhere\" } ] }", "unknown step")]
    [InlineData("{ \"rules\": [ { \"kind\": \"throttle\", \"bytes_per_s\": 5, \"ms\": 3 } ] }", "'ms' does not apply")]
    [InlineData("{ \"rules\": [ ] }", "no rules")]
    [InlineData("{ \"faults\": 1, \"rules\": [ { \"kind\": \"lag\", \"ms\": 5 } ] }", "unknown key 'faults'")]
    public void Wo206Fault_plan_refuses_what_it_cannot_mean(string json, string why)
    {
        var ex = Assert.Throws<FormatException>(() => RelayFaultPlan.Parse(json));
        Assert.Contains(why, ex.Message);
    }

    [Fact]
    public void Wo206Fault_plan_twin_a_valid_plan_of_every_kind_parses()
    {
        var p = RelayFaultPlan.Parse(Plan(
            "{ \"name\": \"a\", \"kind\": \"lag\", \"role\": \"joiner\", \"dir\": \"down\", \"ms\": 150, \"jitter_ms\": 40, \"at\": \"t:+5\" }," +
            "{ \"name\": \"b\", \"kind\": \"jitter-hold\", \"dir\": \"both\", \"hold_ms\": 400, \"every_ms\": 2000, \"at\": \"step:received\", \"for_s\": 30 }," +
            "{ \"name\": \"c\", \"kind\": \"throttle\", \"role\": \"joiner\", \"dir\": \"down\", \"bytes_per_s\": 65536, \"at\": \"step:paused\", \"for_s\": 60 }," +
            "{ \"name\": \"d\", \"kind\": \"stall\", \"role\": \"host\", \"dir\": \"up\", \"at\": \"step:loaded\", \"for_s\": 8 }," +
            "{ \"name\": \"e\", \"kind\": \"cut\", \"role\": \"joiner\", \"at\": \"step:world-sent\", \"for_s\": 15 }"));
        Assert.Equal(5, p.Rules.Count);
        Assert.Equal(206, p.Seed);
        Assert.Equal(new[] { FaultKind.Lag, FaultKind.JitterHold, FaultKind.Throttle, FaultKind.Stall, FaultKind.Cut }, p.Rules.Select(r => r.Kind));
        Assert.Equal(5000, p.Rules[0].AtMs);
        Assert.Equal("step:world-sent", p.Rules[4].At);
    }

    // ---------------------------------------------------------------- the gate's own self-check

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "tools", "Test-RelayFaultsGate.ps1"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root (tools\\Test-RelayFaultsGate.ps1) not found");
    }

    private static int RunGate(string payloadDir)
    {
        var psi = new ProcessStartInfo("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(RepoRoot(), "tools", "Test-RelayFaultsGate.ps1")}\" -SkipTests -Payload \"{payloadDir}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(120_000);
        return p.ExitCode;
    }

    private static string PayloadCopy(bool plantFaultKey)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"wo206-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "dotnet", "KcdMp.Server", "appsettings.json"));
        if (plantFaultKey) src = src.Replace("\"Echo\": false,", "\"Echo\": false,\n  \"RelayFaults\": { \"Plan\": \"lag.json\" },");
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), src);
        return dir;
    }

    [Fact]
    public void Wo206Fault_gate_fails_on_a_planted_fault_key_in_a_config_copy()
    {
        string dir = PayloadCopy(plantFaultKey: true);
        try { Assert.Equal(1, RunGate(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Wo206Fault_gate_twin_passes_on_a_clean_config_copy()
    {
        string dir = PayloadCopy(plantFaultKey: false);
        try { Assert.Equal(0, RunGate(dir)); }
        finally { Directory.Delete(dir, true); }
    }
}
