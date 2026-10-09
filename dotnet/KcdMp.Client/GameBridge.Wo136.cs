// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-136: world presence -- the agent's half (docs/WO-136-findings.md).
//
// Phase 1 -- nothing touches an NPC while a world loads. The field's joins held
// the loading screen ~55 s: the host's NPC stream was applied between
// EntityModuleOnPostLoadGame and "Gameplay started" (puppets started, pauses
// and native binds landed on bodies the AI was still reconstructing). WO-135
// held back the copy guard only. Now, from the moment a load is seen until two
// seconds after "Gameplay started", the host's frames that touch NPCs or
// avatars are HELD, not dropped: the newest NpcState per NPC and the newest
// ghost sample per peer are kept, NPC damage and actions in order, and all of
// it is replayed through the normal path (native feed included) on release.
// The mod holds its own Lua paths under the same window (KCD2MP_W136Hold); the
// join's own load call sets that hold before the load starts.
public partial class GameBridge
{
    /// <summary>The settle after "Gameplay started" before any NPC is touched.</summary>
    internal static readonly TimeSpan W136Settle = TimeSpan.FromSeconds(2);
    private const int W136MaxOrdered = 512;

    private DateTime _w136SettleUntilUtc = DateTime.MinValue;
    private DateTime _w136HoldSinceUtc = DateTime.MinValue;
    private volatile bool _w136WasHolding;
    private volatile bool _w136LuaReleased = true;
    private ChannelWriter<InFrame>? _frameWriter;
    private readonly object _w136Gate = new();
    private readonly Dictionary<string, InFrame> _w136NpcHeld = new(StringComparer.Ordinal);
    private readonly Dictionary<byte, InFrame> _w136GhostHeld = new();
    private readonly Dictionary<string, InFrame> _w136DoorHeld = new(StringComparer.Ordinal);   // WO-153 5: the newest door state per door
    private readonly List<InFrame> _w136OrderedHeld = [];
    private long _w136Held, _w136Replayed, _w136Holds, _w136OrderedDropped;
    private DateTime _w136LuaToldUtc = DateTime.MinValue;

    /// <summary>A world is loading (or the menu: no world), or it loaded less than the settle ago.</summary>
    private bool Wo136Holding =>
        _where is GameWhere.Loading or GameWhere.Menu || DateTime.UtcNow < _w136SettleUntilUtc;

    /// <summary>The frames the hold keeps back: every one that moves, spawns, poses, hits or kills an NPC or an avatar.</summary>
    public static bool Wo136HeldType(int type) =>
        type == Protocol.NpcStateDown || type == Protocol.Ghost || type == Protocol.NpcDamageDown || type == Protocol.ActionDown;

    /// <summary>The processor: true = this frame waits for the world (the newest per NPC / per peer is kept).</summary>
    private bool Wo136Defer(in InFrame frame)
    {
        if (!Wo136HeldType(frame.Type) || !Wo136Holding) return false;
        lock (_w136Gate)
        {
            if (!_w136WasHolding) { _w136WasHolding = true; _w136HoldSinceUtc = DateTime.UtcNow; _w136Holds++; }
            _w136Held++;
            if (frame.Type == Protocol.NpcStateDown && Wo136NpcKey(frame.Payload) is string name) _w136NpcHeld[name] = frame;
            else if (frame.Type == Protocol.Ghost && frame.Payload.Length > 0) _w136GhostHeld[frame.Payload[0]] = frame;
            else if (Wo136DoorKey(frame.Type, frame.Payload) is string door) _w136DoorHeld[door] = frame;
            else
            {
                if (_w136OrderedHeld.Count >= W136MaxOrdered) { _w136OrderedHeld.RemoveAt(0); _w136OrderedDropped++; }
                _w136OrderedHeld.Add(frame);
            }
        }
        return true;
    }

    /// <summary>
    /// WO-153 5: the key of a held host DoorState frame (the door's name and pivot), null for anything else. A door's state is
    /// absolute (open or shut, locked or not), so only the newest one per door is kept. The field's "door open and shut twice
    /// at one moment" was the host's whole door history since the last load replayed in one millisecond when the hold ended.
    /// </summary>
    public static string? Wo136DoorKey(int type, byte[] p)
    {
        if (type != Protocol.ActionDown) return null;
        // [sourceGhostId:1][kind:1][seq:2][phase:1][gen:4][len:1][body]
        if (p.Length < 10 || p[1] != (byte)ActionKind.DoorState) return null;
        int len = p[9];
        if (p.Length < 10 + len || !DoorEvent.TryFromBytes(p.AsSpan(10, len), out var e)) return null;
        return FormattableString.Invariant($"{p[0]}|{e.Name}|{MathF.Round(e.X, 1)}|{MathF.Round(e.Y, 1)}|{MathF.Round(e.Z, 1)}");
    }

    /// <summary>The name of an NpcStateDown (the same framing rule as the handlers); null when malformed.</summary>
    public static string? Wo136NpcKey(byte[] payload)
    {
        if (payload.Length < 2 + 1 + Protocol.NpcStateFixedTail) return null;
        int nameLen = payload[1];
        if (nameLen == 0 || payload.Length != 2 + nameLen + Protocol.NpcStateFixedTail) return null;
        return Encoding.UTF8.GetString(payload, 2, nameLen);
    }

    /// <summary>The processor's flush tick: the hold has ended -> everything kept goes through the normal path now.</summary>
    private void Wo136ReplayIfReleased()
    {
        if (!_w136WasHolding || Wo136Holding) return;
        List<InFrame> replay;
        TimeSpan heldFor;
        lock (_w136Gate)
        {
            _w136WasHolding = false;
            heldFor = DateTime.UtcNow - _w136HoldSinceUtc;
            // NPC states first (the bodies exist before anything hits them), then the peers, then in order.
            replay = [.. _w136NpcHeld.Values, .. _w136GhostHeld.Values, .. _w136DoorHeld.Values, .. _w136OrderedHeld];
            _w136NpcHeld.Clear(); _w136GhostHeld.Clear(); _w136DoorHeld.Clear(); _w136OrderedHeld.Clear();
        }
        var w = _frameWriter;
        int n = 0;
        foreach (var f in replay)
        {
            // WO-144: a peer who left while the world loaded is not brought back by its held frame.
            if (f.Type == Protocol.Ghost && f.Payload.Length > 0 && Wo144DropFromRemoved(f.Payload[0])) continue;
            long now = Stopwatch.GetTimestamp();
            FeedNativeAtRead(f.Type, f.Payload, now, replay: true);
            if (w is not null && w.TryWrite(new InFrame(f.Type, f.Payload, now))) n++;
        }
        Interlocked.Add(ref _w136Replayed, n);
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W136 load hold released after {heldFor.TotalSeconds:F1} s: {n} held frame(s) replayed (the newest per NPC and per peer, then in order) -- the host's NPCs are applied now, never while the world loaded"));
    }

    /// <summary>"Loading saved game" / "[CryAction] LoadGame": the mod's hold, best effort (the join's own load call already set it).</summary>
    private void Wo136OnLoadSeen(string what)
    {
        Wo147ForgetGuardsOnLoad(what);   // WO-147: a load re-creates the copies -- their guards are gone with the old ones
        _w136SettleUntilUtc = DateTime.MinValue;
        _w136LuaReleased = false;
        _ = ExecLuaAsync($"if KCD2MP_W136Hold then KCD2MP_W136Hold(true, 240, \"agent-{what}\") end");
    }

    /// <summary>"Gameplay started": the settle starts; the hold ends W136Settle later.</summary>
    private void Wo136OnGameplayStarted()
    {
        _w136SettleUntilUtc = DateTime.UtcNow + W136Settle;
        _w136LuaReleased = false;
        Console.WriteLine(FormattableString.Invariant($"MP-W136 Gameplay started: NPCs wait {W136Settle.TotalSeconds:F0} s more (settle), then the held frames are replayed"));
        _ = Task.Run(async () =>
        {
            await Task.Delay(W136Settle + TimeSpan.FromMilliseconds(50));
            await Wo136TellModAsync(force: true);
        });
    }

    /// <summary>The mod's hold follows the agent's (idempotent; re-told every 5 s while in the world).</summary>
    private async Task Wo136TellModAsync(bool force = false)
    {
        if (Wo136Holding) return;
        var now = DateTime.UtcNow;
        if (!force && _w136LuaReleased && now - _w136LuaToldUtc < TimeSpan.FromSeconds(5)) return;
        _w136LuaToldUtc = now;
        _w136LuaReleased = true;
        await ExecLuaAsync("if KCD2MP_W136Hold then KCD2MP_W136Hold(false, 0, \"gameplay+settle\") end");
    }

    private string Wo136StatsLine() => string.Create(CultureInfo.InvariantCulture,
        $"MP-WO136-STATS holding={(Wo136Holding ? 1 : 0)} holds={_w136Holds} held={_w136Held} replayed={_w136Replayed} ordered_dropped={_w136OrderedDropped} souls_asked={_w136SoulAsks} souls_known={_w136SoulHits} rides={_w136Rides} ridden_dropped={_w136RiddenDropped} swings={_w136Swings} handovers={_w136Handovers} ko_released={_w136KoReleased} torch_local={(_w136TorchLocal ? 1 : 0)} torch_edges={_w136TorchEdges} presets_cleared={_w136PresetClears}");

    // ================================================================ events from the mod

    private long _w136SoulAsks, _w136SoulHits, _w136Rides, _w136RiddenDropped, _w136Swings, _w136Handovers, _w136KoReleased, _w136TorchEdges, _w136PresetClears;

    private void Wo136OnEvent(string name, string? arg)
    {
        arg ??= "";
        switch (name)
        {
            case "w136_soul":
                if (NpcNamePattern.IsMatch(arg.Trim())) _ = Task.Run(() => Wo136AnswerSoulAsync(arg.Trim()));
                return;
            case "w136_ride":
            {
                var f = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length == 2 && NpcNamePattern.IsMatch(f[0])) _ = Task.Run(() => Wo136OnRideAsync(f[0], f[1] == "1"));
                return;
            }
            case "w136_torch":
                _w136TorchLocal = arg.Trim() == "1";
                Console.WriteLine($"MP-W136 local torch {(_w136TorchLocal ? "OUT (lit)" : "away")} -- rides the state block (bit 0x20) to the partner");
                _ = W164TorchOutTickAsync(true);   // WO-164 TR: and the reliable side-channel (the host's torch never reached a joiner by the block)
                return;
            case "w136_check":
                _ = Task.Run(() => Wo136CheckAsync(arg));
                return;
        }
    }

    // ================================================================ Phase 2: the host's spawns, animals too

    private Task<SoulIndex?>? _soulIndex;

    private Task<SoulIndex?> SoulIndexAsync() => _soulIndex ??= Task.Run(() =>
    {
        try
        {
            string? pak = TablesPakPath() ?? WeaponSwingCatalog.FindTablesPak();
            if (pak is null) { Console.WriteLine("MP-W136 soul tables: Tables.pak not found -- stand-ins keep WO-131's guess"); return (SoulIndex?)null; }
            var sw = Stopwatch.StartNew();
            var idx = SoulIndex.LoadFrom(pak);
            Console.WriteLine($"MP-W136 soul tables: {idx.SoulCount} souls, {idx.ItemCount} item classes read in {sw.ElapsedMilliseconds} ms -- a stand-in is the host's own spawn (soul + class)");
            return idx;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MP-W136 soul tables failed ({ex.GetType().Name}: {ex.Message}) -- stand-ins keep WO-131's guess");
            return null;
        }
    });

    /// <summary>The mod asked which soul and class a host NPC it lacks is (w136_soul): answered from the game's own tables.</summary>
    private async Task Wo136AnswerSoulAsync(string npc)
    {
        Interlocked.Increment(ref _w136SoulAsks);
        var idx = await SoulIndexAsync();
        var s = idx?.StandIn(npc);
        if (s is { } v) Interlocked.Increment(ref _w136SoulHits);
        string guid = s?.Soul.ToString() ?? "", cls = s?.Class ?? "";
        if (!Regex.IsMatch(cls, "^[A-Za-z_]{0,32}$")) cls = "";
        await ExecLuaAsync($"if KCD2MP_W136SoulFor then KCD2MP_W136SoulFor(\"{npc}\", \"{guid}\", \"{cls}\") end");
    }

    // ================================================================ Phase 3: the rider owns the horse

    private readonly ConcurrentDictionary<string, DateTime> _w136Ridden = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Joiner, NpcStateDown: the host's stream for a horse this player rides is not applied (native or Lua).</summary>
    private bool Wo136DropRidden(string npcName)
    {
        if (_w136Ridden.IsEmpty || !Wo136Rules.DropRidden(_w136Ridden, npcName, DateTime.UtcNow)) return false;
        Interlocked.Increment(ref _w136RiddenDropped);
        return true;
    }

    /// <summary>w136_ride &lt;horse&gt; 1|0: this player mounted a host horse copy (take it) / its settle after the dismount ended.</summary>
    private async Task Wo136OnRideAsync(string horse, bool on)
    {
        if (on)
        {
            _w136Ridden[horse] = DateTime.MaxValue;
            Interlocked.Increment(ref _w136Rides);
            // Off the host's hold entirely: the copy guard (imm buff) and any engagement.
            if (_w131Guarded.TryRemove(horse, out uint eid))
            {
                try { await _combat.Wo131CopyGuardAsync(false, eid); } catch { }
                await Wo132DiscardAsync(false, eid);
            }
            if (_w132Engaged.TryRemove(horse, out var eg)) { try { await _combat.Wo132EngageAsync(false, eg.Eid, default); } catch { } }
            Console.WriteLine($"MP-W136 ride: this player rides {horse} -- the rider owns it: the host's stream for it is ignored, its copy guard lifted; the host's horse follows this player's avatar");
        }
        else
        {
            _w136Ridden.TryRemove(horse, out _);
            Console.WriteLine($"MP-W136 ride: {horse} is the host's again (dismounted {Wo136Rules.RideReturn.TotalSeconds:F0} s ago) -- its stream is applied from where the rider left it");
        }
    }

    // ================================================================ Phase 4: fights

    private readonly ConcurrentDictionary<string, bool> _w136HostDown = new(StringComparer.Ordinal);

    /// <summary>Every NpcStateDown on the joiner: the host NPC's dead/knocked-out bits (the engagement never beats them).</summary>
    private void Wo136NoteNpcFlags(string npc, byte flags)
    {
        if ((flags & Protocol.NpcStateFlagNotHuman) != 0) _w141Animals[npc] = true;   // WO-141: its attack rows are an animal's
        bool down = Wo136Rules.Down(flags);
        bool was = _w136HostDown.TryGetValue(npc, out bool d0) && d0;
        _w136HostDown[npc] = down;
        if (down && !was && _w132Engaged.TryRemove(npc, out var eg))
        {
            Interlocked.Increment(ref _w136KoReleased);
            _ = Task.Run(async () =>
            {
                var r = await _combat.Wo132EngageAsync(false, eg.Eid, default);
                Console.WriteLine($"MP-W136 {npc}: the host's NPC is {((flags & Protocol.NpcStateFlagDead) != 0 ? "dead" : "knocked out")} -> the engagement ends FIRST ({(r.Ok ? "released" : "reason " + r.Reason)}), then the copy goes down (WO-135's knockout mode)");
            });
        }
    }

    private bool Wo136HostNpcDown(string npc) => _w136HostDown.TryGetValue(npc, out bool d) && d;

    /// <summary>Host: a partner's committed attack row -- its avatar fights the NPC it swings at, and the swing threatens it.</summary>
    private void Wo136OnAvatarAttack(byte ghost)
    {
        if (!_isDamageAuthority || !_sharedWorld) return;
        if (!_ghostEntityIds.TryGetValue(ghost.ToString(CultureInfo.InvariantCulture), out uint eid)) return;
        Interlocked.Increment(ref _w136Swings);
        _ = Task.Run(async () =>
        {
            var a = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
            try { await _combat.Wo132Async(10, a); } catch { }
        });
    }

    /// <summary>mp_w136_check (live checks): fights on|off, handover, threat &lt;npc&gt; [w], swing &lt;ghost&gt;, torch &lt;ghost&gt; on|off, clear &lt;ghost&gt;, status.</summary>
    private async Task Wo136CheckAsync(string arg)
    {
        var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 2 && p[0] == "fights" && p[1] is "on" or "off")
        {
            var r = await _combat.Wo132Async(11, [(byte)(p[1] == "on" ? 1 : 0)]);
            Console.WriteLine($"MP-W136 check: fights {p[1]} -> {(r is { Ok: true } ? "set" : "FAILED")}");
        }
        else if (p.Length == 1 && p[0] == "handover")
        {
            var r = await _combat.Wo132Async(9, [1]);
            Interlocked.Increment(ref _w136Handovers);
            Console.WriteLine($"MP-W136 check: the host's fights handed to the partners (as at its death) -> {(r is { Ok: true } v ? $"{(v.Payload.Length > 0 ? v.Payload[0] : 0)} NPC(s)" : "FAILED")}");
        }
        else if (p.Length >= 2 && p[0] == "threat" && NpcNamePattern.IsMatch(p[1]) && p[1].StartsWith("wo13", StringComparison.Ordinal))
        {
            uint npcEid = await _combat.Wo132WatchAsync(true, p[1]) ?? 0;
            byte w = p.Length > 2 && byte.TryParse(p[2], out byte ww) ? ww : (byte)2;
            var a = new byte[5];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a, npcEid); a[4] = w;
            var r = npcEid == 0 ? null : await _combat.Wo132Async(12, a);
            Console.WriteLine($"MP-W136 check: a host threat {w} on {p[1]} (test NPCs only) -> {(r is { Ok: true } ? "noted" : "FAILED")}");
        }
        else if (p.Length == 2 && p[0] == "swing" && byte.TryParse(p[1], out byte g))
        {
            if (!_ghostEntityIds.TryGetValue(p[1], out uint eid)) { Console.WriteLine($"MP-W136 check: no avatar {p[1]}"); return; }
            var a = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
            var r = await _combat.Wo132Async(10, a);
            uint npc = r is { } v && v.Payload.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(v.Payload) : 0;
            Console.WriteLine($"MP-W136 check: avatar {g} swings -> {(npc != 0 ? $"at eid 0x{npc:X}" : "nothing in reach")}");
        }
        else if (p.Length == 3 && p[0] == "torch" && byte.TryParse(p[1], out byte tg) && p[2] is "on" or "off")
            await ExecLuaAsync($"if KCD2MP_W136AvatarTorch then KCD2MP_W136AvatarTorch(\"{tg}\", {B(p[2] == "on")}) end");
        else if (p.Length == 2 && p[0] == "clear" && byte.TryParse(p[1], out byte cg))
            await ExecLuaAsync($"if KCD2MP_W136ClearPreset then KCD2MP_W136ClearPreset(\"{cg}\", \"live check\") end");
        else if (p.Length == 1 && p[0] == "status")
        {
            Console.WriteLine(Wo136StatsLine());
            Console.WriteLine($"MP-W136 dll: {await _combat.Wo132StatusAsync() ?? "no answer"}");
            Console.WriteLine($"MP-W136 dll motion: {await _combat.Wo121StatusAsync() ?? "no answer"}");
        }
        else Console.WriteLine($"MP-W136 check: unknown '{arg}'");
    }

    // ================================================================ Phase 5: the outfit and the torch

    private volatile bool _w136TorchLocal;
    private readonly ConcurrentDictionary<byte, BodyState2> _w136PeerTorchState = new();

    private BodyState2 Wo136WithTorch(BodyState2 s) => Wo136Rules.WithTorch(s, _w136TorchLocal);

    /// <summary>Every inbound state block: the partner's torch edges reach its avatar here.</summary>
    private void Wo136OnPeerState2(byte ghost, BodyState2 st)
    {
        BodyState2? before = _w136PeerTorchState.TryGetValue(ghost, out var b) ? b : null;
        _w136PeerTorchState[ghost] = st;
        if (Wo136Rules.TorchEdge(before, st) is not bool on) return;
        Interlocked.Increment(ref _w136TorchEdges);
        W164NoteTorchShown(ghost, on);   // WO-164 TR: the side-channel does not apply this edge twice
        Console.WriteLine($"MP-W136 peer {ghost} torch {(on ? "OUT" : "away")} -> its avatar {(on ? "holds and lights the game's torch" : "puts it away")}");
        _ = ExecLuaAsync($"if KCD2MP_W136AvatarTorch then KCD2MP_W136AvatarTorch(\"{ghost}\", {B(on)}) end");
    }

    /// <summary>
    /// The appearance converge found pieces that stay worn after unequips: a
    /// preset's pieces (not inventory items). The engine's own way off: the empty
    /// preset; the target outfit is then applied over it.
    /// </summary>
    private async Task Wo136ClearPresetAsync(byte ghostId, string why)
    {
        Interlocked.Increment(ref _w136PresetClears);
        await ExecLuaAsync($"if KCD2MP_W136ClearPreset then KCD2MP_W136ClearPreset(\"{ghostId}\", \"{why}\") end");
    }

    /// <summary>A refused class, named from the item table where it can be (the belt case).</summary>
    private async Task<string> Wo136DescribeRefusalAsync(Guid cls)
    {
        var idx = await SoulIndexAsync();
        if (idx is null) return cls.ToString();
        string d = idx.Describe(cls);
        return idx.UnwearableReason(cls) is string r ? $"{d} ({r})" : d;
    }
}
