// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Server.Features.ClientHandling;
using Microsoft.Extensions.DependencyInjection;

namespace KcdMp.Relay.Tests;

// =============================================================================
// WO-144 1.1: the same player's new connection replaces his old one at once --
// the field's phantom partner was a joiner's leftover connection that stayed
// until its timeout while the host counted it as a second partner.
// =============================================================================
public class Wo144RelayTests : IClassFixture<RelayFixture>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly RelayFixture _relay;
    public Wo144RelayTests(RelayFixture relay) => _relay = relay;
    private ClientHandler Clients => _relay.Services.GetRequiredService<ClientHandler>();

    [Fact]
    public async Task A_second_connection_of_the_same_player_replaces_the_first_at_once()
    {
        await using var host = await Peer.ConnectAsync(_relay.TcpPort, "w144-host");
        await using var oldJoiner = await Peer.ConnectAsync(_relay.TcpPort, "w144-joiner");
        byte oldId = oldJoiner.Id;
        await host.ReadUntilAsync(Protocol.Name, Wait);   // the host learns the first connection

        // The joiner's game restarted: a new connection, the old one still open.
        var t0 = DateTime.UtcNow;
        await using var newJoiner = await Peer.ConnectAsync(_relay.TcpPort, "w144-joiner");
        Assert.NotEqual(oldId, newJoiner.Id);

        // The host is told the old one is gone -- now, not after the 30 s idle timeout.
        var gone = await host.ReadUntilAsync(Protocol.Disconnect, Wait);
        Assert.Equal(oldId, gone[0]);
        Assert.True(DateTime.UtcNow - t0 < TimeSpan.FromSeconds(3));

        // And the relay keeps exactly one of them.
        var until = DateTime.UtcNow + Wait;
        while (Clients.GetClients().Count(c => c.IsReady && c.Name == "w144-joiner") != 1 && DateTime.UtcNow < until) await Task.Delay(50);
        var joiners = Clients.GetClients().Where(c => c.IsReady && c.Name == "w144-joiner").ToList();
        Assert.Single(joiners);
        Assert.Equal(newJoiner.Id, joiners[0].Id);
    }

    [Fact]
    public async Task Two_players_with_different_names_on_one_machine_both_stay()
    {
        await using var a = await Peer.ConnectAsync(_relay.TcpPort, "w144-alpha");
        await using var b = await Peer.ConnectAsync(_relay.TcpPort, "w144-bravo");
        Assert.True(await a.NoneOfAsync(Protocol.Disconnect, TimeSpan.FromSeconds(1)));
        Assert.Equal(2, Clients.GetClients().Count(c => c.IsReady && (c.Name == "w144-alpha" || c.Name == "w144-bravo")));
    }
}
