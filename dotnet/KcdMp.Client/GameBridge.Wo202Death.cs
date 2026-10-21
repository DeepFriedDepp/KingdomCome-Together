// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;

namespace KcdMp.Client;

// WO-202 II.3 death_engine_only (host). The field's "dead but walking away" (session A's host: 19:12, 19:30, 20:25; its joiner: "dead for
// me, not for him, moving dead on the ground") is not a death the mod made up: every one of those NPCs died the engine's way ("Soul died
// ... (DR_Combat)", killer the joiner's figure). Each died from a joiner blow the engine took (RPGProcessHit) whose reaction tail was
// refused -- the victim could not react (a fleeing NPC) or no reaction was found -- so the soul died with no death reaction: the body kept
// its living physics and its last motion (5.9 m and 8.2 m walked after the death in the host's own stream; 0.4 m at most for every host
// kill, NPC kill and joiner kill whose reaction was queued). WO-195 found the same on the joiner's copies and lays them down with the
// game's own ragdoll; this is that half for the host's world: a joiner blow applied without a queued reaction is followed by one read of
// the body (op 57, by name), and a body that is dead yet still upright is laid down by the game's ragdoll. The engine alone decided the
// death; nothing here kills or revives anything.
public partial class GameBridge
{
    private long _w202DeathChecks, _w202DeathFalls, _w202DeathFallFailed, _w202DeathLying;

    /// <summary>Every joiner blow the host's world took (WO-173's real hit, its held completion): the body is read when no death reaction came.</summary>
    private void Wo202NoteJoinerBlow(string npc, bool applied, byte react)
    {
        if (!Wo202Rules.DeathFallWanted(W202("death_engine_only"), _isDamageAuthority, applied, react) || !W164Text.IsNpcName(npc)) return;
        _ = Wo202DeathFallAsync(npc, Wo173Rules.ReactName(react));
    }

    private long _w202DeathAnnounced, _w202DeathRefused;

    /// <summary>The mod's death observer (the Lua's actor:IsDead) saw a death: it goes out only when the engine's soul reads dead (op 57, the
    /// soul's own IsDead) -- never on a reading the engine does not share. No answer from the DLL (an older one): the observer's word.</summary>
    private async Task Wo202AnnounceDeathAsync(string npc, string hp, string src, Func<string, Task> send)
    {
        byte? dead = null;
        try
        {
            byte[] name = Encoding.ASCII.GetBytes(npc);
            if (name.Length <= 63)
            {
                var a = new byte[5 + name.Length];
                a[4] = (byte)name.Length;
                name.CopyTo(a, 5);
                // the DLL keeps a soul read 0.25 s: an "alive" just before the death is asked again once that has run out
                for (int i = 0; i < Wo202Rules.DeathReadTries && dead != 1; i++)
                {
                    if (i > 0) await Task.Delay(Wo202Rules.DeathReadGapMs);
                    var r = await _combat.Wo163Async(KcdMp.Wire.Protocol.Wo195OpCheck, a);
                    if (r is { Ok: true, Payload.Length: >= 4 } x) dead = x.Payload[0];
                    else break;
                }
            }
        }
        catch (Exception) { }
        if (!Wo202Rules.AnnounceDeath(dead))
        {
            long n = Interlocked.Increment(ref _w202DeathRefused);
            if (n <= 50) Console.WriteLine($"WO202-DEATH npc={npc} NOT announced: the observer saw it dead (hp={hp}, by {src}) but the engine's soul reads alive");
            return;
        }
        Interlocked.Increment(ref _w202DeathAnnounced);
        Console.WriteLine($"[npcdeath] out: mod observed '{npc}' die locally (hp={hp}, seen by {src}; the engine's soul {(dead == 1 ? "reads dead" : "unread: an older DLL")}) -- sending FATAL{CatchupTag()}");
        try { await send(npc); }
        catch (Exception ex) { Console.WriteLine($"[npcdeath] FATAL for '{npc}' not sent: {ex.GetBaseException().Message}"); }
    }

    private async Task Wo202DeathFallAsync(string npc, string reaction)
    {
        try
        {
            byte[] name = Encoding.ASCII.GetBytes(npc);
            if (name.Length > 63) return;
            var a = new byte[5 + name.Length];   // eid 0 + [len][name]: op 57 finds the body by its name (any body, the host's own too)
            a[4] = (byte)name.Length;
            name.CopyTo(a, 5);
            foreach (int wait in Wo202Rules.DeathFallChecksMs)
            {
                await Task.Delay(wait);
                var r = await _combat.Wo163Async(KcdMp.Wire.Protocol.Wo195OpCheck, a);
                if (r is not { Ok: true, Payload.Length: >= 4 } x) return;   // the body is gone (or an older DLL)
                Interlocked.Increment(ref _w202DeathChecks);
                if (x.Payload[0] != 1) continue;                              // not dead (yet): the blow did not kill
                if (!Wo195Rules.Upright(x.Payload[0], x.Payload[2]))
                {
                    Interlocked.Increment(ref _w202DeathLying);
                    return;                                                   // it lies as the engine's own kills do
                }
                await ExecLuaAsync($"if KCD2MP_W202DeathFall then KCD2MP_W202DeathFall(\"{EscapeLua(npc)}\") end");
                await Task.Delay(Wo195Rules.RagdollSettleMs);
                var r2 = await _combat.Wo163Async(KcdMp.Wire.Protocol.Wo195OpCheck, a);
                bool down = r2 is { Ok: true, Payload.Length: >= 4 } y && y.Payload[2] == 0;
                Interlocked.Increment(ref down ? ref _w202DeathFalls : ref _w202DeathFallFailed);
                Console.WriteLine($"WO202-DEATHFALL npc={npc} reaction={reaction} dead=yes upright=yes -> ragdoll {(down ? "down (the game's own ragdoll; lootable)" : "STILL UPRIGHT")} " +
                                  $"-- a joiner's killing blow without the death reaction (the soul died, the body kept its living physics)");
                return;
            }
        }
        catch (Exception ex) { Console.WriteLine($"WO202-DEATHFALL npc={npc} check failed ({ex.GetType().Name})"); }
    }
}

// WO-202 VI outfit_atomic: the figure's new pieces in one call (KCD2MP_W202EquipAll, the mod's own inventory equip, one frame).
public partial class GameBridge
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<byte, long> _w202OutfitChangedMs = new();

    private Task Wo202EquipAllOnGhostAsync(string soulName, IReadOnlyList<Guid> classes, CancellationToken ct) =>
        _transport.ExecuteNowAsync($"if KCD2MP_W202EquipAll then KCD2MP_W202EquipAll(\"{soulName}\",\"{Wo202Rules.OutfitList(classes)}\") end", ct);
}

// WO-202 IV.3 recoil_clinch: this player's own combat opponent, read every 0.5 s while a session runs (op 67's own-player query: the
// DLL reads the player's combat model), so the clinch this player makes names the body it clinched (Wo197OutboundPacket).
public partial class GameBridge
{
    private volatile string? _w202OwnOpponent;

    private async Task Wo202OpponentLoopAsync(CancellationToken ct)
    {
        var q = new byte[11];   // [a:4 = 0xFFFFFFFF][v:4 = 0][flags][lenA 0][lenV 0]
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(q, 0);
        while (!ct.IsCancellationRequested && _w202Connected)
        {
            try { await Task.Delay(500, ct); } catch { break; }
            if (!W202("recoil_clinch") || !Wo134Peers().Any()) { _w202OwnOpponent = null; continue; }
            try
            {
                var r = await _combat.Wo163Async(KcdMp.Wire.Protocol.Wo197OpPair, q, ct);
                _w202OwnOpponent = r is { Ok: true } x ? Wo202Rules.OwnOpponentName(x.Payload) : null;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { _w202OwnOpponent = null; }
        }
    }
}
