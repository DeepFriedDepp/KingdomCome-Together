// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-208 A.2 (mp_copy_anim_owner; docs/WO-208-findings.md): the host streams its NPCs' full and upper body as state, not events. The
// host's DLL reads every NPC near a player 10 times a second (op 132: each scope's action class, fragment, priority, status, flags, tags
// and action object) and hands over the bodies that changed; the agent sends that payload as it is (NpcAnim, 0x7A -> 0x7B) to each
// joiner in the world. The joiner's agent turns names into its copies' entity ids and hands the states to its DLL (op 133) at the pipe's
// frame lane (never the game's console), where the copy's one owner applies them (wo208.cpp).
public partial class GameBridge
{
    private long _w208StreamPolls, _w208StreamBodiesOut, _w208StreamFramesIn, _w208StreamBodiesIn, _w208StreamUnknown, _w208StreamBad;

    /// <summary>The host, 10 times a second (Wo208StreamLoopAsync): the changed bodies to every joiner in the world.</summary>
    private async Task Wo208StreamTickAsync()
    {
        if (!W137Host || !W208("copy_anim_owner")) return;
        var peers = Wo134Peers();
        if (peers.Count == 0) return;
        var r = await _combat.Wo163Async(Protocol.Wo208OpStreamRead, [(byte)Protocol.NpcAnimMaxBodies]);
        Interlocked.Increment(ref _w208StreamPolls);
        if (r is not { Ok: true } rr || rr.Payload.Length < 2 || rr.Payload[0] == 0) return;
        Interlocked.Add(ref _w208StreamBodiesOut, rr.Payload[0]);
        foreach (byte j in peers)
        {
            try { await WriteJoinAsync(Protocol.BuildJoinUp(Protocol.NpcAnimUp, j, 0, rr.Payload)); }
            catch (Exception ex) { Console.WriteLine($"MP-W208 npc-anim not sent to {j}: {ex.Message}"); }
        }
    }

    private async Task Wo208StreamLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);
                try { await Wo208StreamTickAsync(); }
                catch (Exception ex) { Console.WriteLine($"MP-W208 stream tick failed: {ex.GetType().Name}: {ex.Message}"); }
                try { await Wo209OppTickAsync(); }   // WO-209 A: the same stream's opponents
                catch (Exception ex) { Console.WriteLine($"MP-W209 opponent tick failed: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>The joiner: the host's bodies (0x7B) to this DLL's owner, by this world's copies' entity ids.</summary>
    private async Task Wo208OnNpcAnimAsync(byte[] body)
    {
        if (!W137Joiner) return;
        Interlocked.Increment(ref _w208StreamFramesIn);
        if (!Wo208Rules.TryRekeyNpcAnim(body, name => _npcEntityIds.TryGetValue(name, out uint e) ? e : 0u, out byte[] op, out int known, out int unknown))
        {
            Interlocked.Increment(ref _w208StreamBad);
            return;
        }
        Interlocked.Add(ref _w208StreamBodiesIn, known);
        Interlocked.Add(ref _w208StreamUnknown, unknown);
        if (known == 0) return;
        Wo207Trace.Kind.Value = Protocol.NpcAnimDown;   // a frame's pipe call: before the background polls at the gate
        await _combat.Wo163Async(Protocol.Wo208OpStreamIn, op);
    }

    private string Wo208StreamStats() =>
        $"copy_anim_owner={(W208("copy_anim_owner") ? "on" : "off")} stream_polls={Interlocked.Read(ref _w208StreamPolls)} bodies_out={Interlocked.Read(ref _w208StreamBodiesOut)}"
        + $" frames_in={Interlocked.Read(ref _w208StreamFramesIn)} bodies_in={Interlocked.Read(ref _w208StreamBodiesIn)} not_here={Interlocked.Read(ref _w208StreamUnknown)} bad={Interlocked.Read(ref _w208StreamBad)}";
}
