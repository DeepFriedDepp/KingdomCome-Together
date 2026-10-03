// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using KCDMP_launcher.Models;

namespace KcdMp.Client.Tests;

/// <summary>WO-154 (Phase 7): Host when an earlier launcher's relay still holds the ports.</summary>
public class Wo154RelayTests
{
    private const int Tcp = 7778, Http = 5273;
    private static PortHolder Ours(int port, int pid = 4242) => new(port, pid, "KcdMpServer", true);
    private static RelayAnswer Same(string steam = "ready", uint app = 2429020) => new("0.45.0", steam, app);

    private static RelayDecision D(IReadOnlyList<PortHolder> holders, RelayAnswer? answer, bool allowSteam = true, uint app = 2429020) =>
        RelayReuse.Decide(holders, Tcp, answer, "0.45.0", allowSteam, app);

    [Fact]
    public void Free_ports_start_a_relay()
    {
        var d = D(Array.Empty<PortHolder>(), null);
        Assert.Equal(RelayPlan.StartNew, d.Plan);
        Assert.Empty(d.Pids);
    }

    [Fact]
    public void Our_relay_of_the_same_release_and_settings_is_used_again()
    {
        var d = D(new[] { Ours(Tcp), Ours(Http) }, Same());
        Assert.Equal(RelayPlan.Reuse, d.Plan);
        Assert.Equal(4242, d.Pid);
        Assert.Equal("MP-RELAY reuse pid=4242", d.LogLine);
        Assert.Equal(RelayPlan.Reuse, D(new[] { Ours(Tcp), Ours(Http) }, Same(steam: "off"), allowSteam: false).Plan);
    }

    [Theory]
    [InlineData("0.44.0", "ready", 2429020u, true, 2429020u, "release 0.44.0")]
    [InlineData("0.45.0", "off", 0u, true, 2429020u, "Steam is off in it")]
    [InlineData("0.45.0", "ready", 2429020u, false, 2429020u, "Steam is on in it")]
    [InlineData("0.45.0", "failed", 2429020u, false, 2429020u, "Steam is on in it")]
    [InlineData("0.45.0", "ready", 480u, true, 2429020u, "Steam app 480")]
    public void Our_relay_that_differs_is_replaced(string release, string steam, uint app, bool allowSteam, uint wantApp, string why)
    {
        var d = RelayReuse.Decide(new[] { Ours(Tcp), Ours(Http) }, Tcp, new RelayAnswer(release, steam, app), "0.45.0", allowSteam, wantApp);
        Assert.Equal(RelayPlan.Replace, d.Plan);
        Assert.Contains(why, d.Why);
        Assert.StartsWith("MP-RELAY replaced old pid=4242 (", d.LogLine);
    }

    [Fact]
    public void Our_relay_that_does_not_answer_or_listens_elsewhere_is_replaced()
    {
        Assert.Equal("it does not answer", D(new[] { Ours(Tcp), Ours(Http) }, null).Why);
        // the player changed Host Port: the old relay holds only the HTTP port
        var moved = D(new[] { Ours(Http) }, Same());
        Assert.Equal(RelayPlan.Replace, moved.Plan);
        Assert.Contains("does not listen on port 7778", moved.Why);
        var two = D(new[] { Ours(Tcp, 1), Ours(Http, 2) }, Same());
        Assert.Equal(RelayPlan.Replace, two.Plan);
        Assert.Equal(new[] { 1, 2 }, two.Pids);
    }

    [Fact]
    public void Another_programs_port_is_never_touched_and_the_player_is_told_which()
    {
        var d = D(new[] { Ours(Http), new PortHolder(Tcp, 999, "SomeGameServer", false) }, Same());
        Assert.Equal(RelayPlan.Refuse, d.Plan);
        Assert.Equal(new[] { 999 }, d.Pids);
        Assert.Equal(Tcp, d.BusyPort);
        Assert.Contains("Port 7778 is in use by another program (SomeGameServer)", d.PlayerMessage);
        Assert.Contains("nothing stopped", d.LogLine);
        var unknown = D(new[] { new PortHolder(Http, 4, null, false) }, null);
        Assert.Contains("Port 5273 is in use by another program, so hosting can't start", unknown.PlayerMessage);
        Assert.Equal("", D(new[] { Ours(Tcp), Ours(Http) }, Same()).PlayerMessage);
    }

    [Fact]
    public void A_listener_is_found_by_its_port_with_its_process()
    {
        var v4 = new TcpListener(IPAddress.Loopback, 0);
        v4.Start();
        try
        {
            int port = ((IPEndPoint)v4.LocalEndpoint).Port;
            Assert.Contains(Environment.ProcessId, PortOwners.ListeningPids(port));
            string self = Process.GetCurrentProcess().MainModule!.FileName;
            var mine = RelayReuse.Holders(self, port);
            Assert.Contains(mine, h => h.Pid == Environment.ProcessId && h.Ours && h.Port == port);
            var notMine = RelayReuse.Holders(Path.Combine(Path.GetTempPath(), "KcdMpServer.exe"), port);
            Assert.Contains(notMine, h => h.Pid == Environment.ProcessId && !h.Ours);
        }
        finally { v4.Stop(); }
    }

    [Fact]
    public void An_ipv6_listener_is_found_too_and_a_closed_port_has_none()
    {
        if (!Socket.OSSupportsIPv6) return;
        var v6 = new TcpListener(IPAddress.IPv6Loopback, 0);
        v6.Start();
        int port = ((IPEndPoint)v6.LocalEndpoint).Port;
        try { Assert.Contains(Environment.ProcessId, PortOwners.ListeningPids(port)); }
        finally { v6.Stop(); }
        Assert.DoesNotContain(Environment.ProcessId, PortOwners.ListeningPids(port));
    }

    [Fact]
    public void Paths_compare_as_windows_does()
    {
        Assert.True(RelayReuse.SamePath(@"C:\Users\x\AppData\Local\KCDMP\KcdMpServer.exe", @"c:\users\X\appdata\local\kcdmp\.\KcdMpServer.EXE"));
        Assert.False(RelayReuse.SamePath(@"C:\A\KcdMpServer.exe", @"C:\B\KcdMpServer.exe"));
        Assert.False(RelayReuse.SamePath(null, @"C:\A\KcdMpServer.exe"));
    }
}
