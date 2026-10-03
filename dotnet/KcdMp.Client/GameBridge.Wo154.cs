// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-154: the agent's half of this WO (docs/WO-154-findings.md). The rules are Wo154Rules in Wo154.cs.
//
// Phase 2 -- a knocked-down partner falls on this screen too. The partner's DLL sets the Downed bit of his
// body-state block while his own body is down (its physics is no living entity: a knockdown's ragdoll, a
// knockout). Its edges reach the mod here, debounced (a body that only flickers through a ragdoll for a
// frame does not fall): the avatar falls where it stands, lies there with its writer held, and stands up
// when he does. A death or an execution still hides the avatar at the death spot (WO-113/132): only the
// PlayerState knockdown flag keeps it visible. mp_avatar_falls on|off (default on).
public partial class GameBridge
{
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
            case "w154_status":
                Console.WriteLine(FormattableString.Invariant($"MP-W154-STATUS falls={(_w154AvatarFalls ? "on" : "off")} fell={_w154Falls} rose={_w154Rises} peers_down={_w154PeerDown.Count(kv => kv.Value.Down)}"));
                return;
        }
    }
}
