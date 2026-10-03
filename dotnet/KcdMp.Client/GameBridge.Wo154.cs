// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-154: the agent's half of this WO (docs/WO-154-findings.md). The rules are Wo154Rules in Wo154.cs.
//
// Phase 1 -- a joiner's quest steps made by an AI behaviour (a duel, a brawl: the worker flag) on one State are
// merged before the host judges them, and judged as their net result (mp_quest_coalesce, default on).
//
// Phase 2 -- a knocked-down partner falls on this screen too. The partner's DLL sets the Downed bit of his
// body-state block while his own body is down (its physics is no living entity: a knockdown's ragdoll, a
// knockout) and debounces it there. Its edges reach the mod here: the avatar falls where it stands, lies there
// with its writer held, and stands up when he does. A death or an execution still hides the avatar at the
// death spot (WO-113/132): only the PlayerState knockdown flag keeps it visible. mp_avatar_falls on|off
// (default on).
public partial class GameBridge
{
    // ---- Phase 1: a joiner's AI-behaviour steps, merged before the host judges them (Wo154Rules.WorkerCoalescer) ----
    private volatile bool _w154Coalesce = true;   // mp_quest_coalesce on|off
    private readonly Wo154Rules.WorkerCoalescer _w154Coalescer = new();
    private static readonly AsyncLocal<List<uint>?> _w154AlsoToks = new();
    private long _w154Merged;

    /// <summary>Host: a joiner's step made by an AI behaviour waits to be merged with the next ones on its State.</summary>
    private bool Wo154CoalesceWorkerRequest(byte src, uint tok, QuestChange req, string head)
    {
        if (!_w154Coalesce || (req.Flags & QuestChange.FWorker) == 0) return false;
        bool merged = _w154Coalescer.Add(src, tok, req, Environment.TickCount64);
        if (merged) Interlocked.Increment(ref _w154Merged);
        Console.WriteLine($"{head}: an AI behaviour's step -- {(merged ? "merged with the steps before it" : "waits 1.5 s for the steps after it")} (judged as one)");
        return true;
    }

    /// <summary>Host, the 1 s loop: the merged States whose wait is over are judged, in the order they came.</summary>
    private void Wo154FlushCoalesced(bool all)
    {
        foreach (var x in _w154Coalescer.TakeReady(Environment.TickCount64, all))
        {
            var merged = Wo154Rules.WorkerCoalescer.Merged(x);
            var more = x.Toks.Skip(1).ToList();
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W154 host: {x.Path}: {x.Toks.Count} step(s) of an AI behaviour merged -> {x.FirstOld}->{x.LastNew} ({x.LastPort}), judged now"));
            Wo137Post(async () =>
            {
                _w154AlsoToks.Value = more.Count > 0 ? more : null;
                try { await Wo137HostRequestAsync(x.Src, x.Toks[0], merged); }
                finally { _w154AlsoToks.Value = null; }
            });
        }
    }

    private volatile bool _w154AvatarFalls = true;
    private readonly ConcurrentDictionary<byte, Wo154Rules.DownEdge> _w154PeerDown = new();
    private long _w154Falls, _w154Rises;

    /// <summary>Every inbound body-state block of a partner (both roles).</summary>
    private void Wo154OnPeerState2(byte ghost, BodyState2 st)
    {
        var edge = _w154PeerDown.GetOrAdd(ghost, _ => new Wo154Rules.DownEdge());
        bool? change;
        lock (edge) change = edge.Feed((st.Bits & BodyState2Bits.Downed) != 0, Environment.TickCount64);
        if (change is not bool down) return;
        if (!_w154AvatarFalls)
        {
            Console.WriteLine($"MP-W154 peer {ghost} {(down ? "is down" : "is up again")} -- mp_avatar_falls off: its avatar stays as it is");
            return;
        }
        if (down) Interlocked.Increment(ref _w154Falls); else Interlocked.Increment(ref _w154Rises);
        Console.WriteLine($"MP-W154 peer {ghost} {(down ? "is DOWN -> its avatar falls where it stands and lies there" : "is up again -> its avatar stands up")}");
        _ = ExecLuaAsync($"if KCD2MP_W154AvatarDowned then KCD2MP_W154AvatarDowned(\"{ghost}\", {B(down)}) end");
    }

    /// <summary>A partner left: his edge state goes (a rejoin starts from up).</summary>
    private void Wo154ForgetPeer(byte ghost) => _w154PeerDown.TryRemove(ghost, out _);

    private void Wo154OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w154_falls":   // mp_avatar_falls on|off
                if (f.Length >= 1 && f[0] is "on" or "off")
                {
                    _w154AvatarFalls = f[0] == "on";
                    Console.WriteLine($"MP-W154 mp_avatar_falls {(_w154AvatarFalls ? "ON" : "off")} -- a partner who is knocked down {(_w154AvatarFalls ? "falls and lies on this screen" : "is shown as before (a knockout hidden like a death)")}");
                }
                return;
            case "w154_coalesce":   // mp_quest_coalesce on|off
                if (f.Length >= 1 && f[0] is "on" or "off")
                {
                    _w154Coalesce = f[0] == "on";
                    if (!_w154Coalesce) Wo154FlushCoalesced(true);
                    Console.WriteLine($"MP-W154 mp_quest_coalesce {(_w154Coalesce ? "ON" : "off")} -- a joiner's AI-behaviour steps on one State are {(_w154Coalesce ? "judged as their net result" : "judged one by one (0.44.0)")}");
                }
                return;
            case "w154_status":
                Console.WriteLine(FormattableString.Invariant($"MP-W154-STATUS falls={(_w154AvatarFalls ? "on" : "off")} fell={_w154Falls} rose={_w154Rises} peers_down={_w154PeerDown.Count(kv => kv.Value.Down)} coalesce={(_w154Coalesce ? "on" : "off")} merged={_w154Merged} waiting={_w154Coalescer.Count} contested={_w154Contest.Count}"));
                return;
        }
    }
}
