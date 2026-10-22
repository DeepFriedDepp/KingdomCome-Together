// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-203 (docs/WO-203-findings.md): the shared half of the work order's agent code. Each area lives in its own file
// (GameBridge.Wo203Conv*.cs Act I; GameBridge.Wo203Defence.cs II.2; GameBridge.Wo203Stream.cs II.6 / II.7 / II.11;
// GameBridge.Wo203Appear.cs II.8 / II.9 / II.10; GameBridge.Wo203Quest.cs II.3 / II.4; GameBridge.Wo203Join.cs II.1 / II.12 /
// II.13) and implements the partial hooks below; an area that does not implement one simply has none (C# partial methods).
// Channels: Conv 0x76/0x77 (Act I) and W203 0x78/0x79 (Act II), both LootMsg-shaped (ProtocolWo203.cs). Lua events: "w203_" is
// Act I's, "w203<area letter>_" each Act II area's (d defence, s stream, a appear, q quest, j join).
public partial class GameBridge
{
    private long _w203ConvMalformed, _w203SideMalformed, _w203SideUnknown, _w203SendRefused;

    // ---- the areas' hooks (each implemented at most once, in its own file) ----
    partial void Wo203ConvOnConnect(CancellationToken ct);
    partial void Wo203DefenceOnConnect(CancellationToken ct);
    partial void Wo203StreamOnConnect(CancellationToken ct);
    partial void Wo203AppearOnConnect(CancellationToken ct);
    partial void Wo203QuestOnConnect(CancellationToken ct);
    partial void Wo203JoinOnConnect(CancellationToken ct);
    partial void Wo203ConvOnDisconnect();
    partial void Wo203DefenceOnDisconnect();
    partial void Wo203JoinOnDisconnect();

    partial void Wo203ConvOnFrame(byte src, LootMsg m);         // a Conv frame (Act I)
    partial void Wo203ConvOnEngineLine(string line);            // every forwarded engine line (Act I: this player's conversations)
    partial void Wo203DefenceOnFrame(byte src, LootMsg m);      // W203 kinds 1-4
    partial void Wo203AppearOnFrame(byte src, LootMsg m);       // W203 kinds 5-6
    partial void Wo203JoinOnFrame(byte src, LootMsg m);         // W203 kinds 7, 9
    partial void Wo203QuestOnFrame(byte src, LootMsg m);        // W203 kind 8

    partial void Wo203ConvOnEvent(string name, string? arg);       // "w203_"
    partial void Wo203DefenceOnEvent(string name, string? arg);    // "w203d_"
    partial void Wo203StreamOnEvent(string name, string? arg);     // "w203s_"
    partial void Wo203AppearOnEvent(string name, string? arg);     // "w203a_"
    partial void Wo203QuestOnEvent(string name, string? arg);      // "w203q_"
    partial void Wo203JoinOnEvent(string name, string? arg);       // "w203j_"

    partial void Wo203ConvStats(ref string line);
    partial void Wo203DefenceStats(ref string line);
    partial void Wo203StreamStats(ref string line);
    partial void Wo203AppearStats(ref string line);
    partial void Wo203QuestStats(ref string line);
    partial void Wo203JoinStats(ref string line);

    // ---- wiring (called from GameBridge.cs, GameBridge.Wo123.cs, GameBridge.Wo151.cs) ----
    private void Wo203OnConnect(CancellationToken ct)
    {
        Wo203ConvOnConnect(ct);
        Wo203DefenceOnConnect(ct);
        Wo203StreamOnConnect(ct);
        Wo203AppearOnConnect(ct);
        Wo203QuestOnConnect(ct);
        Wo203JoinOnConnect(ct);
    }

    private void Wo203OnDisconnect()
    {
        Wo203ConvOnDisconnect();
        Wo203DefenceOnDisconnect();
        Wo203JoinOnDisconnect();
    }

    private void Wo203OnEngineLine(string line) => Wo203ConvOnEngineLine(line);

    private void Wo203OnEvent(string name, string? arg)
    {
        if (name == "w203_status") { Wo203Status(); return; }
        if (name.StartsWith("w203d_", StringComparison.Ordinal)) Wo203DefenceOnEvent(name, arg);
        else if (name.StartsWith("w203s_", StringComparison.Ordinal)) Wo203StreamOnEvent(name, arg);
        else if (name.StartsWith("w203a_", StringComparison.Ordinal)) Wo203AppearOnEvent(name, arg);
        else if (name.StartsWith("w203q_", StringComparison.Ordinal)) Wo203QuestOnEvent(name, arg);
        else if (name.StartsWith("w203j_", StringComparison.Ordinal)) Wo203JoinOnEvent(name, arg);
        else Wo203ConvOnEvent(name, arg);
    }

    private Task Wo203OnConvFrameAsync(byte src, byte[] body)
    {
        if (!LootMsg.TryDecode(body, out var m)) { Interlocked.Increment(ref _w203ConvMalformed); return Task.CompletedTask; }
        Wo203ConvOnFrame(src, m);
        return Task.CompletedTask;
    }

    private Task Wo203OnSideFrameAsync(byte src, byte[] body)
    {
        if (!LootMsg.TryDecode(body, out var m)) { Interlocked.Increment(ref _w203SideMalformed); return Task.CompletedTask; }
        switch (m.Kind)
        {
            case Protocol.W203DefenceSwing: case Protocol.W203DefenceOutcome: case Protocol.W203GuardState: case Protocol.W203EngageEarly:
                Wo203DefenceOnFrame(src, m); break;
            case Protocol.W203NpcWorn: case Protocol.W203NpcPose:
                Wo203AppearOnFrame(src, m); break;
            case Protocol.W203JoinProgress: case Protocol.W203TravelRoute:
                Wo203JoinOnFrame(src, m); break;
            case Protocol.W203FocusEdge:
                Wo203QuestOnFrame(src, m); break;
            default:
                Interlocked.Increment(ref _w203SideUnknown); break;
        }
        return Task.CompletedTask;
    }

    // ---- sending (target: Protocol.JoinTargetHost from a joiner, a partner's id from the host) ----
    private async Task Wo203SendConvAsync(byte target, byte kind, string text)
    {
        try { await WriteJoinAsync(new LootMsg(kind, Interlocked.Increment(ref _w137Tok), text).BuildUp(Protocol.ConvUp, target)); }
        catch (Exception ex) { Interlocked.Increment(ref _w203SendRefused); Console.WriteLine($"WO203 conv {Protocol.ConvKindName(kind)} not sent to {target}: {ex.Message}"); }
    }

    private async Task Wo203SendSideAsync(byte target, byte kind, string text)
    {
        try { await WriteJoinAsync(new LootMsg(kind, Interlocked.Increment(ref _w137Tok), text).BuildUp(Protocol.W203Up, target)); }
        catch (Exception ex) { Interlocked.Increment(ref _w203SendRefused); Console.WriteLine($"WO203 {Protocol.W203KindName(kind)} not sent to {target}: {ex.Message}"); }
    }

    private void Wo203Status()
    {
        string line = $"MP-W203-STATS conv_malformed={_w203ConvMalformed} side_malformed={_w203SideMalformed} side_unknown={_w203SideUnknown} send_refused={_w203SendRefused}";
        Wo203ConvStats(ref line);
        Wo203DefenceStats(ref line);
        Wo203StreamStats(ref line);
        Wo203AppearStats(ref line);
        Wo203QuestStats(ref line);
        Wo203JoinStats(ref line);
        Console.WriteLine(line);
    }
}
