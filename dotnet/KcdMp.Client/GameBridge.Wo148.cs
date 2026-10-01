// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-148: carrying on the other screen -- the agent's half (docs/WO-148-findings.md).
//
// THE CARRIER OWNS THE BODY WHILE IT IS CARRIED (WO-119 s3.4, the settled rule).
//   * This player picks something up (the mod's w148_carry grab): the host shows it at once and
//     tells every joiner; a joiner tells the host (Carry 0x70 to target 0xFF), whose world decides.
//   * The host's world decides who carries: a joiner's grab of a body its world has already given
//     to someone else (the host's own player, another joiner) is refused (Carry Refuse to that
//     joiner, whose game puts it down and back); every other grab the host shows on that joiner's
//     avatar and forwards to the other joiners. A joiner shows every grab that arrives (they all
//     come from the host), and its own carry of the same body loses.
//   * Shown = the carrier's avatar picks up this machine's copy with the game's own pick-up, the
//     stream moves the avatar, and the set-down lands the body where the carrier's own game left
//     it (or back where it was picked up, when that would be in the air or under the ground).
//   * While anyone carries a body, the host's NPC stream for it is not applied on a joiner (the
//     carrier's game moves it), and for 5 s after the set-down (the other machine settles too).
//   * A carrier repeats a Held every 2 s; none for 10 s (a lost link) ends the carry here.
// Off with mp_carry_sync off (the mod's switch; the agent reads it through w148_cfg).
public partial class GameBridge
{
    private volatile bool _w148On = true;              // mp_carry_sync (default ON once proven, WO-148)
    private volatile bool _w148Connected;
    private readonly object _w148Lock = new();
    private readonly CarryLedger _w148Ledger = new();
    private readonly ConcurrentDictionary<string, long> _w148Held = new(StringComparer.Ordinal);   // name -> until (ms); long.MaxValue while carried
    private uint _w148Tok;
    private bool _w148WasActive;
    private long _w148GrabsOut, _w148PutsOut, _w148HeldOut, _w148GrabsIn, _w148PutsIn, _w148Applied, _w148Same, _w148RefusedOut,
                 _w148RefusedIn, _w148Losers, _w148Forwarded, _w148Lost, _w148Malformed, _w148StreamDropped, _w148NotActive, _w148QuestNotes;

    private const long W148HoldGraceMs = 5_000;

    private bool W148Host => _combatRoleApplied && _isDamageAuthority && _sharedWorld && Wo134Peers().Count > 0;
    private bool W148Joiner => W137Joiner && Wo134Peers().Count > 0;
    private bool W148Active => _w148On && (W148Host || W148Joiner) && !_w140Separate;

    private void Wo148OnConnect(CancellationToken ct)
    {
        _w148Connected = true;
        lock (_w148Lock) _w148Ledger.Clear();
        _w148Held.Clear();
        _ = ExecLuaAsync("if KCD2MP_W148CfgEmit then KCD2MP_W148CfgEmit() end");
        _ = Wo148LoopAsync(ct);
    }

    private async Task Wo148OnDisconnectAsync()
    {
        _w148Connected = false;
        List<(string Body, CarryLedger.Entry Entry)> gone;
        lock (_w148Lock) { gone = _w148Ledger.All.Select(kv => (kv.Key, kv.Value)).ToList(); _w148Ledger.Clear(); }
        _w148Held.Clear();
        foreach (byte src in gone.Where(g => g.Entry.Carrier != _myGhostId).Select(g => g.Entry.Carrier).Distinct())
            try { await ExecLuaAsync($"if KCD2MP_W148AvatarGone then KCD2MP_W148AvatarGone(\"{src}\") end"); } catch { }
        _w148AvatarHolds.Clear();
        try { await ExecLuaAsync("if KCD2MP_W148Session then KCD2MP_W148Session(false, false) end"); } catch { }
    }

    /// <summary>A peer's Disconnect: its avatar sets everything down here.</summary>
    private void Wo148OnPeerGone(byte id)
    {
        List<(string Body, CarryLedger.Entry Entry)> gone;
        lock (_w148Lock) gone = _w148Ledger.DropCarrier(id);
        if (gone.Count == 0) return;
        foreach (var (b, _) in gone) _w148Held[b] = Environment.TickCount64 + W148HoldGraceMs;
        _w148AvatarHolds.TryRemove(id, out _);
        Console.WriteLine($"MP-CARRY player {id} left carrying {string.Join(",", gone.Select(g => g.Body))} -- set down here where they are");
        _ = ExecLuaAsync($"if KCD2MP_W148AvatarGone then KCD2MP_W148AvatarGone(\"{id}\") end");
    }

    /// <summary>The host's NPC stream must not move a carried body on this machine (both drop points call this).</summary>
    private bool Wo148DropCarried(string npcName)
    {
        if (_w148Held.IsEmpty || !_w148Held.TryGetValue(npcName, out long until)) return false;
        if (Environment.TickCount64 < until) { Interlocked.Increment(ref _w148StreamDropped); return true; }
        _w148Held.TryRemove(npcName, out _);
        return false;
    }

    private void W148Hold(string body, bool carried) =>
        _w148Held[body] = carried ? long.MaxValue : Environment.TickCount64 + W148HoldGraceMs;

    // ---------------------------------------------------------------- the 1 s loop

    private async Task Wo148LoopAsync(CancellationToken ct)
    {
        long lastStats = Environment.TickCount64, lastSession = 0;
        while (!ct.IsCancellationRequested && _w148Connected)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                bool active = W148Active;
                long now = Environment.TickCount64;
                if (active != _w148WasActive || now - lastSession >= 5_000)
                {
                    lastSession = now;
                    if (active != _w148WasActive)
                        Console.WriteLine(active
                            ? $"MP-CARRY ON ({(W148Host ? "host: its world decides who carries" : "joiner: its carries go to the host first")})"
                            : "MP-CARRY off (no partner, not the shared world, or mp_carry_sync off)");
                    _w148WasActive = active;
                    await ExecLuaAsync($"if KCD2MP_W148Session then KCD2MP_W148Session({B(active)}, {B(W148Host)}) end");
                }
                if (active && !Wo136Holding)
                {
                    await Wo148HeartbeatAsync();
                    await Wo148ExpireAsync(now);
                }
                if (now - lastStats >= 60_000) { lastStats = now; Console.WriteLine(Wo148StatsLine()); }
            }
            catch (Exception ex) { Console.WriteLine($"MP-CARRY tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private long _w148LastHeldMs;
    private (string Name, uint Tok)? _w148ObjectMine;   // this player's own object carry (no ledger: no world body)
    private readonly ConcurrentDictionary<string, (float X, float Y, float Z)> _w148MineLast = new(StringComparer.Ordinal);
    /// <summary>A carry of this player's that its game has not confirmed for this long is set down on the other screens
    /// (a dead mod chain); the next confirmation picks it up there again. Long, because a menu halts the mod's timers.</summary>
    private const long W148MineSilentMs = 120_000;
    private async Task Wo148HeartbeatAsync()
    {
        long now = Environment.TickCount64;
        if (now - _w148LastHeldMs < Protocol.CarryHeldEveryMs) return;
        _w148LastHeldMs = now;
        List<(string Body, CarryLedger.Entry Entry)> mine;
        lock (_w148Lock) mine = _w148Ledger.Mine(_myGhostId);
        if (_w148ObjectMine is { } om)
        {
            var (ox, oy, oz) = Wo148MyPos();
            await Wo148SendAllAsync(Protocol.CarryHeld, om.Tok, CarryText.Held(_myGhostId, "object", om.Name, ox, oy, oz));
            Interlocked.Increment(ref _w148HeldOut);
        }
        foreach (var (body, e) in mine)
        {
            // where the game last said the body is (it rides on this player), else this player's own place
            var (x, y, z) = _w148MineLast.TryGetValue(body, out var lp) ? lp : Wo148MyPos();
            await Wo148SendAllAsync(Protocol.CarryHeld, e.Tok, CarryText.Held(_myGhostId, e.What, body, x, y, z));
            Interlocked.Increment(ref _w148HeldOut);
        }
    }

    private async Task Wo148ExpireAsync(long now)
    {
        List<(string Body, CarryLedger.Entry Entry)> silent;
        lock (_w148Lock) silent = _w148Ledger.ExpireMine(_myGhostId, now, W148MineSilentMs);
        foreach (var (body, e) in silent)
        {
            Interlocked.Increment(ref _w148Lost);
            W148Hold(body, false);
            var (lx, ly, lz) = _w148MineLast.TryGetValue(body, out var lp) ? lp : (e.Px, e.Py, e.Pz);
            Console.WriteLine($"MP-CARRY this player's carry of {body} not confirmed by the game for {W148MineSilentMs / 1000} s -- set down on the other screens (a later confirmation picks it up again)");
            await Wo148SendAllAsync(Protocol.CarryPut, e.Tok, CarryText.Put(_myGhostId, "lost", e.What, body, lx, ly, lz));
        }
        List<(string Body, CarryLedger.Entry Entry)> lost;
        lock (_w148Lock) lost = _w148Ledger.Expire(_myGhostId, now, Protocol.CarryLostAfterMs);
        foreach (var (body, e) in lost)
        {
            Interlocked.Increment(ref _w148Lost);
            W148Hold(body, false);
            Console.WriteLine($"MP-CARRY player {e.Carrier}'s carry of {body} fell silent ({Protocol.CarryLostAfterMs / 1000} s without a Held) -- set down here");
            await ExecLuaAsync(FormattableString.Invariant(
                $"if KCD2MP_W148Apply then KCD2MP_W148Apply(\"{e.Carrier}\", \"lost\", \"{e.What}\", \"{body}\", {e.Px:F3}, {e.Py:F3}, {e.Pz:F3}) end"));
        }
    }

    private (float X, float Y, float Z) Wo148MyPos() => (_lastX, _lastY, _lastZ);   // the last position this agent streamed

    // ---------------------------------------------------------------- out (this player)

    /// <summary>The mod's w148_carry: "grab|held &lt;what&gt; &lt;name&gt; x y z" or "put &lt;how&gt; &lt;what&gt; &lt;name&gt; x y z" (CarryLocalEvent).</summary>
    private async Task Wo148OnLocalAsync(string? arg)
    {
        if (!CarryLocalEvent.TryParse(arg, out var ev))
        { Interlocked.Increment(ref _w148Malformed); Console.WriteLine($"MP-CARRY local event malformed: '{arg}'"); return; }
        bool held = ev.IsHeld, grab = ev.IsGrab || ev.IsHeld;
        string what = ev.What, name = ev.Name, how = ev.How;
        float x = ev.X, y = ev.Y, z = ev.Z;

        long now = Environment.TickCount64;
        if (what == "object")
        {
            if (held) return;   // the agent's own Held repeats an object carry
            if (!W148Active) { Interlocked.Increment(ref _w148NotActive); return; }
            uint otok = grab ? Interlocked.Increment(ref _w148Tok) : _w148Tok;
            if (grab) { Interlocked.Increment(ref _w148GrabsOut); _w148ObjectMine = (name, otok); }
            else { Interlocked.Increment(ref _w148PutsOut); _w148ObjectMine = null; }
            Console.WriteLine(FormattableString.Invariant($"MP-CARRY local {(grab ? "grab" : "put " + how)} object {name} at ({x:F2}, {y:F2}, {z:F2})"));
            await Wo148SendAllAsync(grab ? Protocol.CarryGrab : Protocol.CarryPut, otok,
                grab ? CarryText.Grab(_myGhostId, "object", name, x, y, z) : CarryText.Put(_myGhostId, how, "object", name, x, y, z));
            return;
        }
        if (held)
        {
            bool known, justLost;
            lock (_w148Lock) { known = _w148Ledger.OnLocalHeld(_myGhostId, name, now, x, y, z); justLost = !known && _w148Ledger.JustLost(name, now); }
            if (justLost) { Console.WriteLine($"MP-CARRY local held {what} {name}: the carry this player just lost (its put-down is running) -- not a new grab"); return; }
            _w148MineLast[name] = (x, y, z);
            if (known) return;
            Console.WriteLine($"MP-CARRY local held {what} {name}: no carry of this player's is held here (it was set down for silence) -- a new grab");
        }
        if (grab)
        {
            uint tok = Interlocked.Increment(ref _w148Tok);
            byte? before;
            lock (_w148Lock) before = _w148Ledger.OnLocalGrab(_myGhostId, name, what, tok, now, x, y, z);
            W148Hold(name, true);
            if (!W148Active) { Interlocked.Increment(ref _w148NotActive); Console.WriteLine($"MP-CARRY local grab {what} {name} -- not shown elsewhere (carrying sync not active)"); return; }
            Interlocked.Increment(ref _w148GrabsOut);
            Console.WriteLine(FormattableString.Invariant($"MP-CARRY local grab {what} {name} at ({x:F2}, {y:F2}, {z:F2}) tok={tok}{(before is byte b ? $" -- player {b} held it here: {(W148Host ? "this host's world gives it to its own player" : "the host decides")}" : "")}"));
            await Wo148SendAllAsync(Protocol.CarryGrab, tok, CarryText.Grab(_myGhostId, what, name, x, y, z));
        }
        else
        {
            CarryLedger.Entry? e;
            lock (_w148Lock) { e = _w148Ledger.Of(name); _w148Ledger.OnLocalPut(_myGhostId, name); }
            W148Hold(name, false);
            if (!W148Active || e is null || e.Carrier != _myGhostId) { Console.WriteLine($"MP-CARRY local put {how} {name} -- not shown elsewhere ({(e is null ? "no carry held" : e.Carrier != _myGhostId ? $"player {e.Carrier} has it" : "sync not active")})"); return; }
            Interlocked.Increment(ref _w148PutsOut);
            Console.WriteLine(FormattableString.Invariant($"MP-CARRY local put {how} {what} {name} rest ({x:F2}, {y:F2}, {z:F2}) tok={e.Tok}"));
            await Wo148SendAllAsync(Protocol.CarryPut, e.Tok, CarryText.Put(_myGhostId, how, what, name, x, y, z));
        }
    }

    /// <summary>The host sends to every joiner; a joiner to the host.</summary>
    private async Task Wo148SendAllAsync(byte kind, uint tok, string text, byte? except = null)
    {
        if (W148Host)
        {
            foreach (byte j in Wo134Peers())
                if (j != except) await Wo148SendAsync(j, kind, tok, text);
        }
        else await Wo148SendAsync(Protocol.JoinTargetHost, kind, tok, text);
    }

    private async Task Wo148SendAsync(byte target, byte kind, uint tok, string text)
    {
        try { await WriteJoinAsync(new LootMsg(kind, tok, text).BuildUp(Protocol.CarryUp, target)); }
        catch (Exception ex) { Console.WriteLine($"MP-CARRY {Protocol.CarryKindName(kind)} not sent to {target}: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- in (a partner)

    private async Task Wo148OnFrameAsync(byte src, byte[] body)
    {
        if (!LootMsg.TryDecode(body, out var m) || !CarryText.TryParse(m.Kind, m.Text, out var t))
        { Interlocked.Increment(ref _w148Malformed); Console.WriteLine($"MP-CARRY malformed carry from {src} ({body.Length} bytes)"); return; }
        if (!W148Active) { Interlocked.Increment(ref _w148NotActive); return; }
        // on a joiner everything comes from the host; the carrier is named in the text
        // (the host's own carries, and the joiners' it forwards)
        if (!W148Host && Wo148HostId() >= 0 && src != Wo148HostId()) { Interlocked.Increment(ref _w148Malformed); return; }
        if (W148Host && t.Carrier != src) { Interlocked.Increment(ref _w148Malformed); Console.WriteLine($"MP-CARRY carry from {src} names carrier {t.Carrier} -- dropped"); return; }
        long now = Environment.TickCount64;
        switch (m.Kind)
        {
            case Protocol.CarryGrab:
            case Protocol.CarryHeld:
            {
                bool held = m.Kind == Protocol.CarryHeld;
                if (t.What == "object")
                {
                    // an object is lent by its pile in the carrier's world: no world body moves anywhere, so there is
                    // nothing to arbitrate -- shown on the avatar (and forwarded by the host)
                    if (!held) Interlocked.Increment(ref _w148GrabsIn);
                    if (W148Host) await Wo148ForwardAsync(m, t.Carrier);
                    await Wo148ApplyLuaAsync(t.Carrier, held ? "held" : "grab", t);
                    return;
                }
                if (!held) Interlocked.Increment(ref _w148GrabsIn);
                CarryLedger.Verdict v;
                lock (_w148Lock) v = _w148Ledger.OnPeerGrab(_myGhostId, W148Host, t.Carrier, t.Name, t.What, m.Tok, now, t.X, t.Y, t.Z, held);
                switch (v)
                {
                    case CarryLedger.Verdict.Ignore:
                        return;
                    case CarryLedger.Verdict.Refuse:
                    {
                        CarryLedger.Entry? holder;
                        lock (_w148Lock) holder = _w148Ledger.Of(t.Name);
                        Interlocked.Increment(ref _w148RefusedOut);
                        Console.WriteLine($"MP-CARRY player {t.Carrier} {(held ? "held" : "grab")} {t.Name} -> refused: {(holder?.Carrier == _myGhostId ? "this host's player" : $"player {holder?.Carrier}")} carries it in this world");
                        await Wo148SendAsync(t.Carrier, Protocol.CarryRefuse, m.Tok, CarryText.Refuse(t.Carrier, t.Name, "carried"));
                        return;
                    }
                    case CarryLedger.Verdict.Same:
                        Interlocked.Increment(ref _w148Same);
                        if (W148Host) await Wo148ForwardAsync(m, t.Carrier);
                        await Wo148ApplyLuaAsync(t.Carrier, "held", t);
                        return;
                    case CarryLedger.Verdict.LocalLoses:
                        Interlocked.Increment(ref _w148Losers);
                        Console.WriteLine($"MP-CARRY {t.Name}: the host's world gives it to player {t.Carrier} -- this player's carry loses (put down and back)");
                        await ExecLuaAsync($"if KCD2MP_W148Loser then KCD2MP_W148Loser(\"{t.Name}\", \"the host gave it to player {t.Carrier}\") end");
                        goto case CarryLedger.Verdict.Apply;
                    case CarryLedger.Verdict.Apply:
                        Interlocked.Increment(ref _w148Applied);
                        W148Hold(t.Name, true);
                        Console.WriteLine(FormattableString.Invariant($"MP-CARRY player {t.Carrier} {(held ? "held (first heard)" : "grab")} {t.What} {t.Name} at ({t.X:F2}, {t.Y:F2}, {t.Z:F2}) tok={m.Tok} -> shown on the avatar"));
                        if (W148Host) await Wo148ForwardAsync(m, t.Carrier);
                        await Wo148ApplyLuaAsync(t.Carrier, held ? "held" : "grab", t);
                        return;
                }
                return;
            }
            case Protocol.CarryPut:
            {
                Interlocked.Increment(ref _w148PutsIn);
                if (t.What == "object")
                {
                    Console.WriteLine(FormattableString.Invariant($"MP-CARRY player {t.Carrier} put {t.How} object {t.Name} at ({t.X:F2}, {t.Y:F2}, {t.Z:F2})"));
                    if (W148Host) await Wo148ForwardAsync(m, t.Carrier);
                    await Wo148ApplyLuaAsync(t.Carrier, "put", t);
                    return;
                }
                bool had;
                lock (_w148Lock) had = _w148Ledger.OnPut(t.Carrier, t.Name);
                W148Hold(t.Name, false);
                Console.WriteLine(FormattableString.Invariant($"MP-CARRY player {t.Carrier} put {t.How} {t.What} {t.Name} rest ({t.X:F2}, {t.Y:F2}, {t.Z:F2}){(had ? "" : " (no carry of theirs was held here -- set down anyway)")}"));
                if (W148Host) await Wo148ForwardAsync(m, t.Carrier);
                await Wo148ApplyLuaAsync(t.Carrier, t.How == "lost" ? "lost" : "put", t);
                Wo148NoteCarryEnd(t.Carrier, t.Name);
                return;
            }
            case Protocol.CarryRefuse:
            {
                if (W148Host) return;   // only the host refuses
                Interlocked.Increment(ref _w148RefusedIn);
                bool mine;
                lock (_w148Lock) mine = _w148Ledger.OnRefused(_myGhostId, t.Name, Environment.TickCount64);
                Console.WriteLine($"MP-CARRY the host refused this player's carry of {t.Name} ({t.Why}){(mine ? " -- put down and back" : " -- nothing held any more")}");
                if (mine && t.Why == "carried")
                {
                    W148Hold(t.Name, false);
                    await ExecLuaAsync($"if KCD2MP_W148Loser then KCD2MP_W148Loser(\"{t.Name}\", \"the host's world: someone else carries it\") end");
                }
                return;
            }
        }
    }

    /// <summary>The host's ghost id as this joiner knows it (the NPC stream's source); -1 = not known yet.</summary>
    private int Wo148HostId() => _w138HostId;

    /// <summary>Host: a joiner's carry message goes on to every other joiner, verbatim.</summary>
    private async Task Wo148ForwardAsync(LootMsg m, byte carrier)
    {
        foreach (byte j in Wo134Peers())
            if (j != carrier) { await Wo148SendAsync(j, m.Kind, m.Tok, m.Text); Interlocked.Increment(ref _w148Forwarded); }
    }

    private async Task Wo148ApplyLuaAsync(byte carrier, string op, CarryText t)
    {
        if (Wo136Holding) return;   // a load: the next Held (2 s) shows it after the settle
        if (t.What == "object") await Wo148AvatarObjectAsync(carrier, op is "grab" or "held", op == "held" ? "held" : op == "grab" ? "grab" : t.How);
        await ExecLuaAsync(FormattableString.Invariant(
            $"if KCD2MP_W148Apply then KCD2MP_W148Apply(\"{carrier}\", \"{op}\", \"{t.What}\", \"{t.Name}\", {t.X:F3}, {t.Y:F3}, {t.Z:F3}, \"{(t.How.Length > 0 ? t.How : "-")}\") end"));
    }

    // ---------------------------------------------------------------- objects on the avatar

    private readonly ConcurrentDictionary<byte, bool> _w148AvatarHolds = new();

    /// <summary>The carrier's avatar picks a sack up or puts it away with the game's own pick-up or place one-shot (the DLL's
    /// WO-143 one-shot). The sack in its hand is the mod's (KCD2MP_W148ApplyObject: the game's sack model on the avatar's right
    /// hand): the DLL's hand content refuses on a host (kRNotArmed), and the first live run showed the hands empty.</summary>
    private async Task Wo148AvatarObjectAsync(byte carrier, bool hold, string why)
    {
        bool was = _w148AvatarHolds.TryGetValue(carrier, out bool h) && h;
        if (was == hold && why == "held") return;
        _w148AvatarHolds[carrier] = hold;
        string av = $"kcd2mp_{carrier}";
        int? shot = null;
        if (why is "grab" or "put" or "drop")
            try { shot = await _combat.Wo143OneShotAsync(av, hold ? "CarryItemPickup" : "CarryItemPlace", "", 0, 0); } catch { }
        Console.WriteLine($"MP-CARRY avatar {carrier} {(hold ? "picks a sack up" : "puts the sack away")} ({why}): one-shot={(shot is int q ? q.ToString() : "-")}");
    }

    // ---------------------------------------------------------------- the mod's answers, and quest reactions

    /// <summary>The mod's w148_result: "&lt;src&gt; &lt;op&gt; &lt;name&gt; &lt;ok|refused|nottaken&gt; &lt;why&gt;" -- logged.</summary>
    private void Wo148OnResult(string? arg) => Console.WriteLine($"MP-CARRY shown here: {arg}");

    private readonly ConcurrentDictionary<string, (byte Carrier, long AtMs)> _w148RecentEnds = new(StringComparer.Ordinal);
    private void Wo148NoteCarryEnd(byte carrier, string body) => _w148RecentEnds[body] = (carrier, Environment.TickCount64);

    /// <summary>
    /// WO-148 3.3: the host's real body now moves when a joiner carries it, so a quest may react by
    /// itself (a body arriving at a grave). Called by the quest layer for every change this host's own
    /// world makes; a change within 30 s of a joiner's carry (or during one) is logged against it.
    /// WO-147's quest-safety rule still guards the destructive steps.
    /// </summary>
    internal void Wo148NoteQuestChange(string what)
    {
        if (!W148Host) return;
        long now = Environment.TickCount64;
        List<string> notes = new();
        lock (_w148Lock)
            foreach (var kv in _w148Ledger.All)
                if (kv.Value.Carrier != _myGhostId) notes.Add($"{kv.Key} carried by player {kv.Value.Carrier}");
        foreach (var kv in _w148RecentEnds)
        {
            if (now - kv.Value.AtMs > 30_000) { _w148RecentEnds.TryRemove(kv.Key, out _); continue; }
            if (kv.Value.Carrier != _myGhostId) notes.Add($"{kv.Key} set down by player {kv.Value.Carrier} {(now - kv.Value.AtMs) / 1000} s ago");
        }
        if (notes.Count == 0) return;
        Interlocked.Increment(ref _w148QuestNotes);
        Console.WriteLine($"MP-CARRY quest-reaction host quest change while a partner carries: {what} -- {string.Join("; ", notes)}");
    }

    private volatile bool _w148Objects = true;   // mp_carry_objects (the mod's; the agent only reports it)

    private void Wo148OnCfg(string? arg)
    {
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (kv.StartsWith("carry_sync=", StringComparison.Ordinal)) _w148On = kv.EndsWith("=on", StringComparison.Ordinal);
            else if (kv.StartsWith("carry_objects=", StringComparison.Ordinal)) _w148Objects = kv.EndsWith("=on", StringComparison.Ordinal);
        }
        Console.WriteLine($"MP-CARRY cfg carry_sync={On(_w148On)} carry_objects={On(_w148Objects)}");
    }

    private string Wo148StatsLine() => FormattableString.Invariant(
        $"MP-WO148-STATS carry_sync={On(_w148On)} active={On(W148Active)} host={On(W148Host)} held_now={_w148Ledger.Count} grabs_out={_w148GrabsOut} puts_out={_w148PutsOut} held_out={_w148HeldOut} grabs_in={_w148GrabsIn} puts_in={_w148PutsIn} applied={_w148Applied} same={_w148Same} refused_out={_w148RefusedOut} refused_in={_w148RefusedIn} losers={_w148Losers} forwarded={_w148Forwarded} lost={_w148Lost} stream_dropped={_w148StreamDropped} not_active={_w148NotActive} malformed={_w148Malformed} quest_notes={_w148QuestNotes}");
}
