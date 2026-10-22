// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Reflection;
using KcdMp.Server.Features.ClientHandling;
using Microsoft.Extensions.DependencyInjection;

namespace KcdMp.Relay.Tests;

// =============================================================================
// WO-203 II.5: THE RELAY MESSAGE TABLE GATE. A PRE-SHIP GATE, LIKE THE
// ROUND-TRIP GATE BESIDE IT (tools\Test-RelayTable.ps1, Build-Installer).
//
// Why it exists: in session C (0.62.0) the relay dropped 463 W164 frames
// (0x74) as "unknown-or-wrong-length" -- every WO-201 speech line and some
// votes. The type WAS in the relay's table; its row's length limit (80 bytes
// of text) was older than the senders, which went up to ~500. Nothing compared
// the protocol's types with the relay's rules, nothing put a row's maximum
// through the relay, and the drop reason could not tell "unknown" from "too
// long".
//
// What it does: (a) reflects over every message id the protocol defines and
// requires a relay rule for each up type (Protocol.RelayUp or JoinWire), and
// a defined type for each rule; (b) puts every join row through the REAL relay
// at its minimum and its maximum, and one byte past each -- dropped, counted
// as a wrong length, and the framing survives; (c) does the same for every
// other up type with a minimal valid body; (d) proves Act I's and Act II's
// channels carry their 1400-byte texts both ways. A new type without a rule,
// or a rule the relay does not honour, fails here and does not ship.
// =============================================================================

public class RelayTableGateTests : IClassFixture<RelayFixture>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly RelayFixture _relay;

    public RelayTableGateTests(RelayFixture relay) => _relay = relay;

    private ClientHandler Clients => _relay.Services.GetRequiredService<ClientHandler>();

    private long Drops(string keyPrefix) => Clients.DropCount(keyPrefix);

    private static string Hex(byte t) => $"0x{t:X2}";

    // ---------------------------------------------------------------- (a) the protocol's ids against the relay's table

    // Up types whose names do not end in "Up" (Protocol.cs, the C->S block).
    private static readonly string[] UnsuffixedUps = { "Handshake", "Position", "Ping", "Invite", "InviteResponse", "SessionLeave", "DiceIntent" };
    // Down types whose names do not end in "Down" (Protocol.cs, the S->C block).
    private static readonly string[] UnsuffixedDowns =
    {
        "Ghost", "Name", "Pong", "Disconnect", "VersionMismatch", "InviteReceived", "SessionStart", "SessionEnd", "DiceState", "DiceError",
        "DiceEnd", "ReleaseVersion", "CombatRole", "ServerFull", "ReleaseVersionMismatch", "Ack",
    };
    // Constants whose names end in "Down" but are not message types: a player-state flag and a W164 kind.
    private static readonly string[] DownNamedNotTypes = { "PlayerStateFlagKnockedDown", "W164NpcDown" };

    private static Dictionary<string, byte> ByteConsts() =>
        typeof(Protocol).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(byte))
            .ToDictionary(f => f.Name, f => (byte)f.GetRawConstantValue()!);

    private static Dictionary<string, byte> UpTypes()
    {
        var all = ByteConsts();
        return all.Where(kv => kv.Key.EndsWith("Up", StringComparison.Ordinal) || UnsuffixedUps.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static Dictionary<string, byte> DownTypes()
    {
        var all = ByteConsts();
        return all.Where(kv => (kv.Key.EndsWith("Down", StringComparison.Ordinal) && !DownNamedNotTypes.Contains(kv.Key)) || UnsuffixedDowns.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    [Fact]
    public void Every_protocol_up_type_has_a_relay_rule_and_every_rule_a_defined_type()
    {
        var all = ByteConsts();
        foreach (var n in UnsuffixedUps.Concat(UnsuffixedDowns).Concat(DownNamedNotTypes))
            Assert.True(all.ContainsKey(n), $"Protocol.{n} is gone -- update this gate's name lists");

        var ups = UpTypes();
        var downs = DownTypes();

        // every up type has exactly one rule, and the relay's gate knows it
        foreach (var (name, v) in ups)
        {
            int rules = (v == Protocol.Handshake ? 1 : 0) + Protocol.RelayUp.Count(r => r.Up == v) + Protocol.JoinWire.Count(r => r.Up == v);
            Assert.True(rules == 1, $"Protocol.{name} ({Hex(v)}) has {rules} relay rules (RelayUp / JoinWire); it needs exactly one");
            Assert.True(Protocol.IsKnownUp(v), $"Protocol.{name} ({Hex(v)}) is not known to the relay's gate");
            Assert.False(Protocol.IsKnownDown(v), $"Protocol.{name} ({Hex(v)}) is an up type the agent counts as a down type");
        }
        // every rule names a defined up type (and a join row a defined down type)
        var upValues = ups.Values.ToHashSet();
        var downValues = downs.Values.ToHashSet();
        foreach (var r in Protocol.RelayUp) Assert.True(upValues.Contains(r.Up), $"RelayUp row '{r.Name}' ({Hex(r.Up)}) names no up type the protocol defines");
        foreach (var r in Protocol.JoinWire)
        {
            Assert.True(upValues.Contains(r.Up), $"JoinWire row '{r.Name}' ({Hex(r.Up)}) names no up type the protocol defines");
            Assert.True(downValues.Contains(r.Down), $"JoinWire row '{r.Name}' down {Hex(r.Down)} is no down type the protocol defines");
        }
        // the table is exactly the protocol: {Handshake} + RelayUp + JoinWire == every up type
        var table = new HashSet<byte> { Protocol.Handshake };
        foreach (var r in Protocol.RelayUp) table.Add(r.Up);
        foreach (var r in Protocol.JoinWire) table.Add(r.Up);
        Assert.Equal(upValues.OrderBy(x => x), table.OrderBy(x => x));

        // ids unique within each direction, and no up id is also a down id
        foreach (var g in ups.GroupBy(kv => kv.Value)) Assert.True(g.Count() == 1, $"up id {Hex(g.Key)} is used by {string.Join(", ", g.Select(kv => kv.Key))}");
        foreach (var g in downs.GroupBy(kv => kv.Value)) Assert.True(g.Count() == 1, $"down id {Hex(g.Key)} is used by {string.Join(", ", g.Select(kv => kv.Key))}");
        foreach (var (name, v) in ups)
            Assert.False(downValues.Contains(v), $"up id Protocol.{name} ({Hex(v)}) is also a down id ({string.Join(", ", downs.Where(d => d.Value == v).Select(d => d.Key))})");
        Assert.Equal(Protocol.RelayUp.Length, Protocol.RelayUp.Select(r => r.Name).Distinct().Count());
        Assert.Equal(Protocol.JoinWire.Length, Protocol.JoinWire.Select(r => r.Name).Distinct().Count());

        // the agent's side: every down type is known, and RelayDown + the join rows' downs are exactly the down types
        foreach (var (name, v) in downs)
        {
            Assert.True(Protocol.IsKnownDown(v), $"Protocol.{name} ({Hex(v)}) is not known to the agent's catch-all");
            Assert.False(Protocol.IsKnownUp(v), $"Protocol.{name} ({Hex(v)}) is a down type the relay's gate takes as an up type");
        }
        var downTable = Protocol.RelayDown.ToHashSet();
        foreach (var r in Protocol.JoinWire) downTable.Add(r.Down);
        Assert.Equal(downValues.OrderBy(x => x), downTable.OrderBy(x => x));

        // a byte nobody defined is unknown both ways
        for (int t = 0; t <= 0xFF; t++)
            if (!upValues.Contains((byte)t) && !downValues.Contains((byte)t))
            {
                Assert.False(Protocol.IsKnownUp(t), $"{Hex((byte)t)} is defined by nobody but the gate knows it");
                Assert.False(Protocol.IsKnownDown(t), $"{Hex((byte)t)} is defined by nobody but the agent knows it");
            }
    }

    [Fact]
    public void Every_rule_is_a_sane_range_and_the_gate_reads_it()
    {
        foreach (var r in Protocol.RelayUp)
        {
            Assert.True(r.Min >= 0 && r.Min <= r.Max && r.Max <= Protocol.FramePayloadMax, $"RelayUp '{r.Name}' {r.Min}..{r.Max}");
            Assert.True(Protocol.IsUpLenOk(r.Up, r.Min), $"RelayUp '{r.Name}' refuses its own minimum {r.Min}");
            Assert.True(Protocol.IsUpLenOk(r.Up, r.Max), $"RelayUp '{r.Name}' refuses its own maximum {r.Max}");
            if (r.Min > 0) Assert.False(Protocol.IsUpLenOk(r.Up, r.Min - 1), $"RelayUp '{r.Name}' takes {r.Min - 1}");
            if (r.Max < Protocol.FramePayloadMax) Assert.False(Protocol.IsUpLenOk(r.Up, r.Max + 1), $"RelayUp '{r.Name}' takes {r.Max + 1}");
        }
        foreach (var r in Protocol.JoinWire)
        {
            Assert.True(Protocol.IsUpLenOk(r.Up, r.Min) && Protocol.IsUpLenOk(r.Up, r.Max), r.Name);
            Assert.False(Protocol.IsUpLenOk(r.Up, r.Min - 1), r.Name);
            Assert.False(Protocol.IsUpLenOk(r.Up, r.Max + 1), r.Name);
        }
        // the two types that take only some lengths of their range
        foreach (int len in new[] { 17, 21, 29, 33 }) Assert.True(Protocol.IsUpLenOk(Protocol.Position, len), $"position {len}");
        foreach (int len in new[] { 16, 18, 20, 22, 26, 34 }) Assert.False(Protocol.IsUpLenOk(Protocol.Position, len), $"position {len}");
        Assert.True(Protocol.IsUpLenOk(Protocol.CombatEventUp, 1) && Protocol.IsUpLenOk(Protocol.CombatEventUp, 3));
        Assert.False(Protocol.IsUpLenOk(Protocol.CombatEventUp, 2));
        // the Handshake: [version][nameLen] at least
        Assert.True(Protocol.IsKnownUp(Protocol.Handshake));
        Assert.False(Protocol.IsUpLenOk(Protocol.Handshake, 1));
        Assert.True(Protocol.IsUpLenOk(Protocol.Handshake, 2));
        Assert.False(Protocol.IsKnownUp(-1));
        Assert.False(Protocol.IsKnownUp(256));
        // the side channels name their kind in a drop key
        Assert.True(Protocol.DropKeyHasKind(Protocol.W164Up) && Protocol.DropKeyHasKind(Protocol.ConvUp) && Protocol.DropKeyHasKind(Protocol.W203Up));
        Assert.False(Protocol.DropKeyHasKind(Protocol.LootAskUp));
        Assert.Equal(-1, Protocol.JoinBodyKind(new byte[Protocol.JoinHeaderLen]));
        Assert.Equal(21, Protocol.JoinBodyKind(new byte[] { 0xFF, 0, 0, 0, 0, 21, 1 }));
    }

    // ---------------------------------------------------------------- the peers

    private static byte[] Frame(byte type, byte[] body)
    {
        var pkt = new byte[3 + body.Length];
        pkt[0] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(1), (ushort)body.Length);
        body.CopyTo(pkt, 3);
        return pkt;
    }

    private async Task WaitNoClientsAsync()
    {
        var until = DateTime.UtcNow + Wait;
        while (Clients.ReadyClientCount > 0 && DateTime.UtcNow < until) await Task.Delay(20);
        Assert.Equal(0, Clients.ReadyClientCount);
    }

    /// <summary>
    /// The host (it claims the session: WO-127's flag, so it is the damage authority whatever the ids) and one joiner. Returns once
    /// the joiner has seen the host's claim -- the relay has processed it, so every later host frame meets the host's authority.
    /// </summary>
    private async Task<(Peer Host, Peer Joiner)> HostAndJoinerAsync()
    {
        await WaitNoClientsAsync();
        var host = await Peer.ConnectAsync(_relay.TcpPort, "the-host");
        var joiner = await Peer.ConnectAsync(_relay.TcpPort, "the-joiner");
        await Task.Delay(50);
        await host.SendRawAsync(ClaimPosition());
        var g = await joiner.ReadUntilAsync(Protocol.Ghost, Wait);
        Assert.Equal(host.Id, g[0]);
        Assert.True(Clients.IsDamageAuthority(Clients.GetClients().Single(c => c.IsReady && c.Id == host.Id)));
        return (host, joiner);
    }

    private static byte[] ClaimPosition()
    {
        var b = new byte[Protocol.PositionPayloadLen];
        b[16] = Protocol.PositionFlagHostClaim;
        return Frame(Protocol.Position, b);
    }

    private static async Task<byte[]> ArrivesAsync(Peer at, byte down, int len, int src = -1)
    {
        var d = await at.ReadUntilAsync(down, Wait);
        Assert.Equal(len, d.Length);
        if (src >= 0) Assert.Equal((byte)src, d[0]);
        return d;
    }

    // ---------------------------------------------------------------- (b) every join row, at its minimum and its maximum

    public static IEnumerable<object[]> JoinRows() => Protocol.JoinWire.Select(r => new object[] { r.Name });

    private static byte[] JoinBody(int len, byte target)
    {
        var b = new byte[len];
        if (len > 0) b[0] = target;   // [target][joinId:4 = 0][the rest zero: the relay never reads past the target]
        return b;
    }

    [Theory]
    [MemberData(nameof(JoinRows))]
    public async Task Every_join_row_crosses_the_relay_at_min_and_max_and_one_byte_past_is_counted(string name)
    {
        var row = Protocol.JoinWire.Single(r => r.Name == name);
        var (host, joiner) = await HostAndJoinerAsync();
        await using var _h = host; await using var _j = joiner;

        // the side that may send it: a joiner's (and an "either" row's) goes to the host by 0xFF, the host's to the joiner by id
        bool fromHost = row.From == Protocol.JoinFrom.Host;
        var (tx, rx) = fromHost ? (host, joiner) : (joiner, host);
        byte target = fromHost ? joiner.Id : Protocol.JoinTargetHost;
        string type = Hex(row.Up);
        long before = Drops(type);

        foreach (int len in new[] { row.Min, row.Max })
        {
            await tx.SendRawAsync(Frame(row.Up, JoinBody(len, target)));
            var d = await ArrivesAsync(rx, row.Down, len + 1, tx.Id);
            Assert.Equal(target, d[1]);
        }
        Assert.Equal(before, Drops(type));   // nothing of this type dropped on the way

        // one byte past the maximum and one short of the minimum: dropped AND counted as wrong lengths that name the limit; the
        // framing survives both -- the valid frame behind them is the next of its type to arrive
        string key = Protocol.DropKeyHasKind(row.Up) ? $"{type}/k0:" : $"{type}:";
        string over = $"{key}wrong-length:{row.Max + 1}>max{row.Max}", under = $"{key}wrong-length:{row.Min - 1}<min{row.Min}";
        long overBefore = Drops(over), underBefore = Drops(under);
        await tx.SendRawAsync(Frame(row.Up, JoinBody(row.Max + 1, target)));
        await tx.SendRawAsync(Frame(row.Up, JoinBody(row.Min - 1, target)));
        await tx.SendRawAsync(Frame(row.Up, JoinBody(row.Min, target)));
        await ArrivesAsync(rx, row.Down, row.Min + 1, tx.Id);
        Assert.Equal(before + 2, Drops(type));
        Assert.Equal(overBefore + 1, Drops(over));
        Assert.Equal(underBefore + 1, Drops(under));
    }

    // ---------------------------------------------------------------- (c) every other up type, with a minimal valid body

    public static IEnumerable<object[]> RelayUpRows() => Protocol.RelayUp.Select(r => new object[] { r.Name });

    [Theory]
    [MemberData(nameof(RelayUpRows))]
    public async Task Every_relay_up_row_round_trips_with_a_minimal_valid_body(string name)
    {
        var row = Protocol.RelayUp.Single(r => r.Name == name);
        var (host, joiner) = await HostAndJoinerAsync();
        await using var _h = host; await using var _j = joiner;
        string type = Hex(row.Up);
        long before = Drops(type);

        await ProbeAsync(row.Up, row.Min, host, joiner);

        Assert.Equal(before, Drops(type));
    }

    /// <summary>Sends <paramref name="up"/> with a valid body of <paramref name="len"/> bytes the way the agent does, and waits for what
    /// the relay makes of it where it must arrive.</summary>
    private static async Task ProbeAsync(byte up, int len, Peer host, Peer joiner)
    {
        var b = new byte[len];
        switch (up)
        {
            case Protocol.Position:
                b[16] = Protocol.PositionFlagHostClaim;   // the host keeps its claim
                await host.SendRawAsync(Frame(up, b));
                await ArrivesAsync(joiner, Protocol.Ghost, Protocol.GhostPayloadLen + len - Protocol.PositionPayloadLen, host.Id);
                return;
            case Protocol.Ping:
                b[0] = 0x5A;
                await host.SendRawAsync(Frame(up, b));
                Assert.Equal(b, await ArrivesAsync(host, Protocol.Pong, len));
                return;
            case Protocol.ClockSyncUp:
                await host.SendRawAsync(Frame(up, b));
                await ArrivesAsync(host, Protocol.ClockSyncDown, Protocol.ClockSyncDownPayloadLen);
                return;
            case Protocol.VoiceUp:
                await host.SendRawAsync(Frame(up, b));
                await ArrivesAsync(joiner, Protocol.VoiceDown, 1 + len, host.Id);
                return;

            // ---- WO-2 interactions and WO-5 dice: a short session flow, each message at its type's minimum
            case Protocol.Invite:
                b[0] = joiner.Id; b[1] = (byte)InteractionKind.Duel;
                await host.SendRawAsync(Frame(up, b));
                var inv = await ArrivesAsync(joiner, Protocol.InviteReceived, 5);
                Assert.Equal(host.Id, inv[2]);
                return;
            case Protocol.InviteResponse:
            {
                ushort sid = await InviteAsync(host, joiner, InteractionKind.Duel);
                BinaryPrimitives.WriteUInt16LittleEndian(b, sid); b[2] = 1;
                await joiner.SendRawAsync(Frame(up, b));
                var st = await ArrivesAsync(host, Protocol.SessionStart, 5);
                Assert.Equal(sid, BinaryPrimitives.ReadUInt16LittleEndian(st));
                return;
            }
            case Protocol.SessionEventUp:
            {
                ushort sid = await SessionAsync(host, joiner, InteractionKind.Duel);
                BinaryPrimitives.WriteUInt16LittleEndian(b, sid);
                await host.SendRawAsync(Frame(up, b));
                var ev = await ArrivesAsync(joiner, Protocol.SessionEventDown, 3);
                Assert.Equal(host.Id, ev[2]);
                return;
            }
            case Protocol.SessionLeave:
            {
                ushort sid = await InviteAsync(host, joiner, InteractionKind.Duel);
                BinaryPrimitives.WriteUInt16LittleEndian(b, sid); b[2] = (byte)SessionEndReason.Completed;
                await host.SendRawAsync(Frame(up, b));
                var end = await ArrivesAsync(joiner, Protocol.SessionEnd, 3);
                Assert.Equal(sid, BinaryPrimitives.ReadUInt16LittleEndian(end));
                return;
            }
            case Protocol.DiceIntent:
            {
                ushort sid = await SessionAsync(host, joiner, InteractionKind.Dice);
                BinaryPrimitives.WriteUInt16LittleEndian(b, sid); b[2] = (byte)DiceIntentType.Forfeit;
                await host.SendRawAsync(Frame(up, b));
                var end = await ArrivesAsync(joiner, Protocol.DiceEnd, 15);
                Assert.Equal(sid, BinaryPrimitives.ReadUInt16LittleEndian(end));
                return;
            }

            // ---- routed to the one player they name
            case Protocol.PlayerHitUp:   // the damage authority's NPC hit a player: the target id is dropped on the way down
                b[0] = joiner.Id;
                await host.SendRawAsync(Frame(up, b));
                await ArrivesAsync(joiner, Protocol.PlayerHitDown, Protocol.PlayerHitDownPayloadLen);
                return;
            case Protocol.PlayerHitV8Up:
                b[0] = joiner.Id;
                await host.SendRawAsync(Frame(up, b));
                await ArrivesAsync(joiner, Protocol.PlayerHitV8Down, PlayerHitV8.DownLen, host.Id);
                return;
        }

        // ---- the sender's facts, to every other player with the sender's id in front
        byte down;
        switch (up)
        {
            case Protocol.DamageUp: down = Protocol.DamageDown; break;
            case Protocol.DeathUp: down = Protocol.DeathDown; break;
            case Protocol.AppearanceUp: down = Protocol.AppearanceDown; break;              // [count 0]
            case Protocol.PauseUp: down = Protocol.PauseDown; break;
            case Protocol.PlayerStateUp: down = Protocol.PlayerStateDown; break;
            case Protocol.PlayerDeathUp: down = Protocol.PlayerDeathDown; break;            // no body at all
            case Protocol.NpcStateUp: b[0] = 1; b[1] = (byte)'n'; down = Protocol.NpcStateDown; break;
            case Protocol.TimeSkipUp: b[0] = Protocol.TimeSkipPhaseSync; down = Protocol.TimeSkipDown; break;
            case Protocol.HorseInfoUp: down = Protocol.HorseInfoDown; break;                // [nameLen 0] = on foot
            case Protocol.CombatEventUp: down = Protocol.CombatEventDown; break;
            case Protocol.WeatherUp: down = Protocol.WeatherDown; break;                    // [nameLen 0][blend:2]
            case Protocol.NpcDamageUp: b[0] = 1; b[1] = (byte)'n'; down = Protocol.NpcDamageDown; break;
            case Protocol.ItemDropUp: down = Protocol.ItemDropDown; break;
            case Protocol.ItemClaimUp: down = Protocol.ItemClaimDown; break;
            case Protocol.StoryBeatUp: down = Protocol.StoryBeatDown; break;                // [kind][len 0]
            case Protocol.ActionUp: b[0] = (byte)ActionKind.Jump; down = Protocol.ActionDown; break;   // the header only, len 0
            case Protocol.PlayerRespawnedUp: down = Protocol.PlayerRespawnedDown; break;
            case Protocol.GraveAddUp: down = Protocol.GraveAddDown; break;
            case Protocol.GraveRemoveUp: down = Protocol.GraveRemoveDown; break;
            case Protocol.WorldSavedUp: down = Protocol.WorldSavedDown; break;               // the damage authority's
            default:
                Assert.Fail($"RelayUp type {Hex(up)} has no probe in RelayTableGateTests.ProbeAsync -- add one with the row");
                return;
        }
        await host.SendRawAsync(Frame(up, b));
        await ArrivesAsync(joiner, down, 1 + len, host.Id);
    }

    private static async Task<ushort> InviteAsync(Peer host, Peer joiner, InteractionKind kind)
    {
        await host.SendRawAsync(Frame(Protocol.Invite, new[] { joiner.Id, (byte)kind }));
        var inv = await joiner.ReadUntilAsync(Protocol.InviteReceived, Wait);
        return BinaryPrimitives.ReadUInt16LittleEndian(inv);
    }

    private static async Task<ushort> SessionAsync(Peer host, Peer joiner, InteractionKind kind)
    {
        ushort sid = await InviteAsync(host, joiner, kind);
        var accept = new byte[3];
        BinaryPrimitives.WriteUInt16LittleEndian(accept, sid); accept[2] = 1;
        await joiner.SendRawAsync(Frame(Protocol.InviteResponse, accept));
        await host.ReadUntilAsync(Protocol.SessionStart, Wait);
        await joiner.ReadUntilAsync(Protocol.SessionStart, Wait);
        return sid;
    }

    // ---------------------------------------------------------------- wrong lengths of every other up type

    [Theory]
    [MemberData(nameof(RelayUpRows))]
    public async Task A_wrong_length_of_every_relay_up_row_is_counted_and_the_framing_survives(string name)
    {
        var row = Protocol.RelayUp.Single(r => r.Name == name);
        var bad = new List<int>();
        if (row.Min > 0) bad.Add(row.Min - 1);
        if (row.Max < Protocol.FramePayloadMax) bad.Add(row.Max + 1);
        if (row.Up == Protocol.Position) bad.AddRange(new[] { 20, 22, 26 });   // inside the range, not a length it takes (the 0.23.1 lesson)
        if (row.Up == Protocol.CombatEventUp) bad.Add(2);
        Assert.All(bad, len => Assert.False(Protocol.IsUpLenOk(row.Up, len)));
        if (bad.Count == 0) return;   // only a min-0 row with a u16 maximum has no wrong length

        var (host, joiner) = await HostAndJoinerAsync();
        await using var _h = host; await using var _j = joiner;
        string key = $"{Hex(row.Up)}:wrong-length";
        long before = Drops(key);
        foreach (int len in bad) await host.SendRawAsync(Frame(row.Up, new byte[len]));
        // the framing survives every one of them: the Ping behind them is answered
        var ping = new byte[Protocol.PingPayloadLen]; ping[0] = 0x77;
        await host.SendRawAsync(Frame(Protocol.Ping, ping));
        Assert.Equal(ping, await host.ReadUntilAsync(Protocol.Pong, Wait));
        Assert.Equal(before + bad.Count, Drops(key));
        foreach (int len in bad) Assert.True(Drops($"{key}:{len}") >= 1, $"{key}:{len}");
    }

    [Fact]
    public async Task An_unknown_type_a_down_type_and_a_second_handshake_are_counted_apart()
    {
        var (host, joiner) = await HostAndJoinerAsync();
        await using var _h = host; await using var _j = joiner;
        long unknown = Drops("0x7A:unknown-type"), ghost = Drops("0x02:unknown-type"), again = Drops("0x00:handshake-again");
        await host.SendRawAsync(Frame(0x7A, new byte[] { 1, 2, 3 }));                 // a type nobody defined (a newer peer)
        await host.SendRawAsync(Frame(Protocol.Ghost, new byte[Protocol.GhostPayloadLen]));   // a down type sent up
        await host.SendRawAsync(Frame(Protocol.Handshake, new byte[] { Protocol.Version, 0 }));
        var ping = new byte[Protocol.PingPayloadLen];
        await host.SendRawAsync(Frame(Protocol.Ping, ping));
        await host.ReadUntilAsync(Protocol.Pong, Wait);
        Assert.Equal(unknown + 1, Drops("0x7A:unknown-type"));
        Assert.Equal(ghost + 1, Drops("0x02:unknown-type"));
        Assert.Equal(again + 1, Drops("0x00:handshake-again"));
        Assert.Equal(0, Drops("0x7A:wrong-length") + Drops("0x02:wrong-length"));
    }

    [Fact]
    public async Task A_too_long_side_channel_frame_names_its_kind_in_the_drop_key()
    {
        var (host, joiner) = await HostAndJoinerAsync();
        await using var _h = host; await using var _j = joiner;
        var row = Protocol.JoinWireFor(Protocol.W164Up)!.Value;
        // a WO-201 speech line (kind 21) one byte past the row: the shape session C dropped 411 times, now with its kind and limit
        var body = new byte[row.Max + 1 - Protocol.JoinHeaderLen];
        body[0] = Protocol.W164SpeechLine;
        for (int i = Protocol.LootFixedLen; i < body.Length; i++) body[i] = (byte)'a';
        string key = $"0x74/k{Protocol.W164SpeechLine}:wrong-length:{row.Max + 1}>max{row.Max}";
        long before = Drops(key);
        await joiner.SendRawAsync(Protocol.BuildJoinUp(Protocol.W164Up, Protocol.JoinTargetHost, 0, body));
        var ok = new LootMsg(Protocol.W164SpeechLine, 1, "v1|@me|1000|0|Localization/dialog/x/y|hello");
        await joiner.SendRawAsync(ok.BuildUp(Protocol.W164Up, Protocol.JoinTargetHost));
        var d = await host.ReadUntilAsync(Protocol.W164Down, Wait);
        Assert.True(LootMsg.TryDecode(d.AsSpan(1 + Protocol.JoinHeaderLen), out var got));
        Assert.Equal(ok, got);
        Assert.Equal(before + 1, Drops(key));
        // a route refusal names the kind too (the host aiming one at itself: no target)
        long self = Drops("0x74/k21:no-target");
        await host.SendRawAsync(ok.BuildUp(Protocol.W164Up, host.Id));
        await host.SendRawAsync(Frame(Protocol.Ping, new byte[Protocol.PingPayloadLen]));
        await host.ReadUntilAsync(Protocol.Pong, Wait);
        Assert.Equal(self + 1, Drops("0x74/k21:no-target"));
    }

    [Fact]
    public async Task The_drops_that_were_silent_are_counted_with_their_reasons()
    {
        var (host, joiner) = await HostAndJoinerAsync();
        await using var _h = host; await using var _j = joiner;
        var keys = new[]
        {
            "0x1A:count-mismatch", "0x2E:namelen-mismatch", "0x30:namelen-mismatch", "0x37:len-mismatch", "0x3B:len-mismatch",
            "0x21:not-host", "0x46:not-host", "0x16:bad-intent:9",
        };
        var before = keys.ToDictionary(k => k, Drops);

        await host.SendRawAsync(Frame(Protocol.AppearanceUp, new byte[] { 2, 0 }));                       // count 2, no items
        await host.SendRawAsync(Frame(Protocol.WeatherUp, new byte[] { 5, 0, 0 }));                       // nameLen 5, no name
        var dmg = new byte[1 + 1 + Protocol.NpcDamageFixedTail]; dmg[0] = 3;                               // nameLen 3, one byte of name
        await host.SendRawAsync(Frame(Protocol.NpcDamageUp, dmg));
        await host.SendRawAsync(Frame(Protocol.StoryBeatUp, new byte[] { 1, 4, (byte)'a' }));            // len 4, one byte
        var act = new byte[Protocol.ActionUpHeaderLen + 4]; act[0] = (byte)ActionKind.Jump; act[Protocol.ActionUpHeaderLen - 1] = 9;   // claims 9, carries 4
        await host.SendRawAsync(Frame(Protocol.ActionUp, act));
        var hit = new byte[Protocol.PlayerHitUpPayloadLen]; hit[0] = host.Id;                             // the joiner is not the authority
        await joiner.SendRawAsync(Frame(Protocol.PlayerHitUp, hit));
        await joiner.SendRawAsync(Frame(Protocol.WorldSavedUp, new byte[Protocol.WorldSavedUpPayloadLen]));
        await joiner.SendRawAsync(Frame(Protocol.DiceIntent, new byte[] { 1, 0, 9 }));                    // intent 9 is no intent
        // both senders' frames are processed in order: a Ping behind each proves its drops were counted
        await host.SendRawAsync(Frame(Protocol.Ping, new byte[Protocol.PingPayloadLen]));
        await joiner.SendRawAsync(Frame(Protocol.Ping, new byte[Protocol.PingPayloadLen]));
        await host.ReadUntilAsync(Protocol.Pong, Wait);
        await joiner.ReadUntilAsync(Protocol.Pong, Wait);

        foreach (var k in keys) Assert.True(before[k] + 1 == Drops(k), $"{k}: {before[k]} -> {Drops(k)}");
    }

    // ---------------------------------------------------------------- (d) Act I's and Act II's channels carry their full texts

    [Theory]
    [InlineData(Protocol.ConvUp, Protocol.ConvDown, Protocol.ConvCam)]
    [InlineData(Protocol.W203Up, Protocol.W203Down, Protocol.W203NpcPose)]
    public async Task Conv_and_w203_frames_of_1400_bytes_cross_both_ways(byte up, byte down, byte kind)
    {
        var (host, joiner) = await HostAndJoinerAsync();
        await using var _h = host; await using var _j = joiner;
        var row = Protocol.JoinWireFor(up)!.Value;
        Assert.Equal(down, row.Down);
        Assert.Equal(Protocol.JoinFrom.Either, row.From);
        Assert.Equal(1400, row.Max - Protocol.JoinHeaderLen - Protocol.LootFixedLen);
        string type = Hex(up);
        long before = Drops(type);

        string text = "c1 " + new string('x', 1400 - 3);
        // the joiner's to the host (0xFF), the host's to the joiner (by id): either player can be the starter
        var fromJoiner = new LootMsg(kind, 7, text);
        await joiner.SendRawAsync(fromJoiner.BuildUp(up, Protocol.JoinTargetHost));
        var d1 = await ArrivesAsync(host, down, 1 + row.Max, joiner.Id);
        Assert.True(LootMsg.TryDecode(d1.AsSpan(1 + Protocol.JoinHeaderLen), out var got1));
        Assert.Equal(fromJoiner, got1);

        var fromHost = new LootMsg(kind, 8, text);
        await host.SendRawAsync(fromHost.BuildUp(up, joiner.Id));
        var d2 = await ArrivesAsync(joiner, down, 1 + row.Max, host.Id);
        Assert.True(LootMsg.TryDecode(d2.AsSpan(1 + Protocol.JoinHeaderLen), out var got2));
        Assert.Equal(fromHost, got2);

        Assert.Equal(before, Drops(type));
        // one byte more is refused before it is sent
        Assert.Throws<ArgumentOutOfRangeException>(() => new LootMsg(kind, 9, text + "x").BuildUp(up, joiner.Id));
    }
}
