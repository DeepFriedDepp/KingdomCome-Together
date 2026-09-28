using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-141: activities -- the agent's half (docs/WO-141-findings.md).
//
// SYNC WHAT THE ACTIVITY IS, NOT THE ANIMATION. The DLL reads every body's NPC
// state (the game's own "this body is in this stance on this object / in this
// unstance at this location"), and applies one with the game's own placement.
//   * The host: its tracked NPCs' activities (the DLL's capture, on change and
//     every 10 s while not "none") go to every joiner (ActivityHost 0x6A).
//   * Every player: its own body's activity (sitting on a bench, lying in a bed)
//     goes to the others (a joiner's to the host 0x6C, which forwards it).
//   * A joiner: the host's NPC rows are applied on its copies, the players' rows
//     on their avatars (the DLL reconciles each body until it matches, and the
//     native writer yields while an activity holds the body on its object).
// A copy leaves its activity first when it fights, is down or talks; nothing is
// applied while a world loads (the rows are kept and applied after the settle).
public partial class GameBridge
{
    /// <summary>mp_activities default. The maintainer's rule: new fail-closed mechanisms ship ON.</summary>
    public const bool W141Default = true;
    /// <summary>The kinds on by default (Wo141Rules.KindStance | KindUnstance | KindPlayer).</summary>
    public const int W141KindsDefault = Wo141Rules.KindsAll;

    private volatile bool _w141On = W141Default;
    /// <summary>mp_animal_attacks (default ON): the host's animals' attack rows play on the joiner's copies.</summary>
    private volatile bool _w141Bites = true;
    private long _w141SyncAtMs;
    private volatile int _w141Kinds = W141KindsDefault;
    private volatile bool _w141Connected;
    private int _w141CfgKey = -1;
    private long _w141CfgAtMs;
    private bool _w141WasActive, _w141WasHolding;
    private readonly ConcurrentDictionary<string, ActivityState> _w141HostRows = new(StringComparer.Ordinal);   // joiner: the host's latest per NPC
    private readonly ConcurrentDictionary<string, string> _w141Blocked = new(StringComparer.Ordinal);            // joiner: NPC -> why it left
    private readonly ConcurrentDictionary<string, bool> _w141Talking = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<byte, ActivityState> _w141PeerRows = new();                          // the latest per player
    private ActivityState _w141Mine = ActivityState.None;
    private HashSet<byte> _w141KnownPeers = new();
    private readonly ConcurrentDictionary<string, bool> _w141Animals = new(StringComparer.Ordinal);   // NPC names the host streams as not human
    private long _w141Bite;
    private long _w141Shows;
    private long _w141RowsOut, _w141RowsIn, _w141Applies, _w141Leaves, _w141Rejoins, _w141Forwarded, _w141HeldRows;

    private bool W141Host => _combatRoleApplied && _isDamageAuthority && _sharedWorld && Wo134Peers().Count > 0;
    private bool W141Joiner => W137Joiner && Wo134Peers().Count > 0;
    private bool W141Active => Wo141Rules.Active(_w141On, W141Host, W141Joiner, Wo136Holding, _w140Separate);

    private void Wo141OnConnect(CancellationToken ct)
    {
        _w141Connected = true;
        _w141CfgKey = -1;
        _combat.OnActivityFrame = rows => { _ = Task.Run(() => Wo141OnDllRowsAsync(rows)); };
        _ = Wo141LoopAsync(ct);
    }

    private async Task Wo141OnDisconnectAsync()
    {
        _w141Connected = false;
        _combat.OnActivityFrame = null;
        try { await _combat.Wo141ConfigAsync(0, false, 250); } catch { }
        _w141CfgKey = -1;
        _w141HostRows.Clear(); _w141Blocked.Clear(); _w141Talking.Clear(); _w141PeerRows.Clear();
        _w141Mine = ActivityState.None;
        _w141KnownPeers = new();
    }

    // ---------------------------------------------------------------- the 1 s loop

    private async Task Wo141LoopAsync(CancellationToken ct)
    {
        long lastStats = Environment.TickCount64;
        while (!ct.IsCancellationRequested && _w141Connected)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                bool holding = Wo136Holding;
                bool active = W141Active;
                await Wo141PushConfigAsync(active, W141Host);
                if (holding && !_w141WasHolding)
                {
                    // a world loads: the DLL forgets every body (the rows stay here, applied after the settle)
                    int? n = await _combat.Wo141ForgetAsync();
                    if (n is > 0) Console.WriteLine($"MP-W141 a world loads: {n} activit{(n == 1 ? "y" : "ies")} put aside until the settle");
                }
                if (active && (!_w141WasActive || _w141WasHolding)) await Wo141ReapplyAllAsync(_w141WasHolding ? "after the load" : "session");
                _w141WasHolding = holding;
                _w141WasActive = active;
                if (active)
                {
                    await Wo141BlockTickAsync();
                    if (W141Host) await Wo141NewPeersAsync();
                }
                long now = Environment.TickCount64;
                if (now - _w141SyncAtMs >= 5_000)
                {
                    _w141SyncAtMs = now;
                    _ = ExecLuaAsync($"if KCD2MP_W141Sync then KCD2MP_W141Sync({B(_w141On)}, {B(_w141Bites)}) end");
                }
                if (now - lastStats >= 60_000) { lastStats = now; Console.WriteLine(Wo141StatsLine()); }
            }
            catch (Exception ex) { Console.WriteLine($"MP-W141 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private async Task Wo141PushConfigAsync(bool active, bool host)
    {
        byte cap = Wo141Rules.CaptureMask(active, host);
        if ((_w141Kinds & Wo141Rules.KindPlayer) == 0) cap = (byte)(cap & 1);
        int key = cap | (active ? 0x100 : 0);
        long now = Environment.TickCount64;
        if (key == _w141CfgKey && now - _w141CfgAtMs < 10_000) return;
        bool changed = key != _w141CfgKey;
        var r = await _combat.Wo141ConfigAsync(cap, active, 250);
        if (r is null) { if (changed && active) Console.WriteLine("MP-W141 the DLL did not answer (no plugin, or not armed) -- activities are not shown; tried again"); return; }
        _w141CfgKey = key; _w141CfgAtMs = now;
        if (changed)
            Console.WriteLine(active
                ? $"MP-W141 activities ON ({(host ? "host: its NPCs and its player go out" : "joiner: the host's NPCs and the players are shown")}; read {(r.Value.Read ? "armed" : "NOT ARMED")}, apply {(r.Value.Apply ? "armed" : "NOT ARMED")}, kinds {_w141Kinds})"
                : "MP-W141 activities off (no partner, not the shared world, a load, or mp_activities off)");
    }

    // ---------------------------------------------------------------- out: the DLL's rows

    private async Task Wo141OnDllRowsAsync(List<Wo141DllRow> rows)
    {
        if (!W141Active) return;
        bool host = W141Host;
        var npc = new List<ActivityRow>();
        foreach (var r in rows)
        {
            if (r.Kind == Wo141Codec.KindNpc)
            {
                if (host) npc.Add(new ActivityRow(Protocol.ActivityPeerNone, r.Name, Wo141Rules.Filter(r.A, _w141Kinds)));
                continue;
            }
            if ((_w141Kinds & Wo141Rules.KindPlayer) == 0) continue;
            _w141Mine = r.A;
            var mine = new List<ActivityRow> { new(_myGhostId, "", r.A) };
            Console.WriteLine($"MP-W141 this player: {r.A}");
            if (host) foreach (byte j in Wo134Peers()) await Wo141SendAsync(Protocol.ActivityHostUp, j, Protocol.ActivityKindPlayer, mine);
            else await Wo141SendAsync(Protocol.ActivityPeerUp, Protocol.JoinTargetHost, Protocol.ActivityKindPlayer, mine);
        }
        if (npc.Count > 0)
            foreach (byte j in Wo134Peers())
                foreach (var b in Wo141Rules.Batches(npc))
                    await Wo141SendAsync(Protocol.ActivityHostUp, j, Protocol.ActivityKindNpc, b);
    }

    private async Task Wo141SendAsync(byte type, byte target, byte kind, List<ActivityRow> rows)
    {
        try
        {
            await WriteJoinAsync(ActivityCodec.BuildUp(type, target, kind, rows));
            Interlocked.Add(ref _w141RowsOut, rows.Count);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W141 {rows.Count} row(s) not sent to {target}: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- in

    private async Task Wo141OnFrameAsync(int type, byte src, byte[] body)
    {
        if (!ActivityCodec.TryDecode(body, out byte kind, out var rows)) { Console.WriteLine($"MP-W141 malformed activity frame from {src} ({body.Length} bytes)"); return; }
        Interlocked.Add(ref _w141RowsIn, rows.Count);
        if (type == Protocol.ActivityHostDown)
        {
            if (!W141Joiner) return;
            foreach (var r in rows)
            {
                if (kind == Protocol.ActivityKindNpc)
                {
                    _w141HostRows[r.Name] = r.A;
                    if (_w141Blocked.ContainsKey(r.Name)) continue;
                    await Wo141ApplyAsync(r.Name, r.A, "host");
                }
                else if (r.Peer != _myGhostId)
                {
                    _w141PeerRows[r.Peer] = r.A;
                    await Wo141ApplyAsync(Wo141Rules.AvatarName(r.Peer), Wo141Rules.ForAvatar(r.A), $"player {r.Peer}");
                    await Wo141AvatarLabelAsync(r.Peer, Wo141Rules.ForAvatar(r.A));
                }
            }
            return;
        }
        if (type == Protocol.ActivityPeerDown)
        {
            if (!W141Host || kind != Protocol.ActivityKindPlayer || rows.Count != 1) return;
            var a = rows[0].A;
            _w141PeerRows[src] = a;
            Console.WriteLine($"MP-W141 player {src}: {a}");
            await Wo141ApplyAsync(Wo141Rules.AvatarName(src), Wo141Rules.ForAvatar(a), $"player {src}");
            await Wo141AvatarLabelAsync(src, Wo141Rules.ForAvatar(a));
            var fwd = new List<ActivityRow> { new(src, "", a) };
            foreach (byte j in Wo134Peers())
                if (j != src) { await Wo141SendAsync(Protocol.ActivityHostUp, j, Protocol.ActivityKindPlayer, fwd); Interlocked.Increment(ref _w141Forwarded); }
        }
    }

    private async Task Wo141ApplyAsync(string name, ActivityState a, string why)
    {
        if (!W141Active) { Interlocked.Increment(ref _w141HeldRows); return; }   // a load: kept, applied after the settle
        bool? known = await _combat.Wo141ApplyAsync(name, a);
        Interlocked.Increment(ref _w141Applies);
        if (known is null) Console.WriteLine($"MP-W141 {name}: the DLL did not take the activity ({why}: {a})");
    }

    /// <summary>The nameplate over an avatar in an activity goes onto its body (seated / lying height).</summary>
    private Task Wo141AvatarLabelAsync(byte peer, ActivityState a) =>
        ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W141AvatarStance then KCD2MP_W141AvatarStance({peer}, {(a.OwnsPosition ? (a.Stance != 0 ? a.Stance : 1) : 0)}) end"));

    /// <summary>Every row kept (a session start, the end of a load): the host's NPCs, the players.</summary>
    private async Task Wo141ReapplyAllAsync(string why)
    {
        int n = 0;
        if (W141Joiner)
            foreach (var (name, a) in _w141HostRows)
                if (!a.IsNone && !_w141Blocked.ContainsKey(name)) { await _combat.Wo141ApplyAsync(name, a); n++; }
        foreach (var (peer, a) in _w141PeerRows)
            if (!a.IsNone && peer != _myGhostId) { await _combat.Wo141ApplyAsync(Wo141Rules.AvatarName(peer), Wo141Rules.ForAvatar(a)); n++; }
        if (n > 0) Console.WriteLine($"MP-W141 {n} activit{(n == 1 ? "y" : "ies")} applied again ({why})");
    }

    // ---------------------------------------------------------------- leave first: fight, down, talk

    private async Task Wo141BlockTickAsync()
    {
        if (!W141Joiner) return;
        foreach (var (name, a) in _w141HostRows)
        {
            string? why = Wo141Rules.BlockReason(_w132Engaged.ContainsKey(name), Wo136HostNpcDown(name), _w141Talking.ContainsKey(name));
            bool was = _w141Blocked.ContainsKey(name);
            if (why is not null && !was)
            {
                _w141Blocked[name] = why;
                if (!a.IsNone)
                {
                    await _combat.Wo141LeaveAsync(name);
                    Interlocked.Increment(ref _w141Leaves);
                    Console.WriteLine($"MP-W141 {name} leaves its activity first ({why}): stands up, the stream takes the body");
                }
            }
            else if (why is null && was)
            {
                _w141Blocked.TryRemove(name, out _);
                if (!a.IsNone && !_w136HostDown.GetValueOrDefault(name))
                {
                    await Wo141ApplyAsync(name, a, "back");
                    Interlocked.Increment(ref _w141Rejoins);
                    Console.WriteLine($"MP-W141 {name} back in the host's activity ({a})");
                }
            }
        }
    }

    /// <summary>WO-137's talk (w137_talk on|off npc): the copy's own brain has it for the conversation.</summary>
    private void Wo141OnTalk(bool on, string npc)
    {
        if (on) _w141Talking[npc] = true; else _w141Talking.TryRemove(npc, out _);
    }

    // ---------------------------------------------------------------- a new joiner

    private async Task Wo141NewPeersAsync()
    {
        var now = Wo134Peers().ToHashSet();
        var fresh = now.Where(p => !_w141KnownPeers.Contains(p)).ToList();
        _w141KnownPeers = now;
        if (fresh.Count == 0) return;
        await _combat.Wo141ResyncAsync();   // the DLL sends every NPC activity again on its next tick
        foreach (byte j in fresh)
        {
            var rows = new List<ActivityRow>();
            if (!_w141Mine.IsNone) rows.Add(new ActivityRow(_myGhostId, "", _w141Mine));
            foreach (var (peer, a) in _w141PeerRows) if (peer != j && !a.IsNone) rows.Add(new ActivityRow(peer, "", a));
            foreach (var b in Wo141Rules.Batches(rows)) await Wo141SendAsync(Protocol.ActivityHostUp, j, Protocol.ActivityKindPlayer, b);
            Console.WriteLine($"MP-W141 player {j} joined: every activity goes to it again ({rows.Count} player row(s) now, the NPCs on the DLL's next tick)");
        }
    }

    // ---------------------------------------------------------------- settings and status

    private void Wo141OnModLine(string? arg)
    {
        var f = (arg ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length == 0) return;
        switch (f[0])
        {
            case "on":
            case "off":
            {
                bool on = f[0] == "on";
                if (on != _w141On) Console.WriteLine($"MP-W141 mp_activities {(on ? "on" : "off")}");
                _w141On = on;
                _w141CfgKey = -1;
                if (!on) _ = _combat.Wo141ForgetAsync();
                return;
            }
            case "bites" when f.Length >= 2 && f[1] is "on" or "off":
            {
                bool on = f[1] == "on";
                if (on != _w141Bites) Console.WriteLine($"MP-W141 mp_animal_attacks {(on ? "on" : "off")}");
                _w141Bites = on;
                return;
            }
            case "kinds" when f.Length >= 2 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int k):
                _w141Kinds = k & Wo141Rules.KindsAll;
                _w141CfgKey = -1;
                Console.WriteLine($"MP-W141 kinds {_w141Kinds} (1 NPC stances, 2 NPC unstances, 4 players)");
                return;
            case "anim" when f.Length >= 3:
            {
                // w141 anim <action> <linked object entity name>: the player played an ActionTrigger one-shot
                // (Lua's WaterTubeActionTrigger.ReportUse wrap); the ones with an NPC match are shown there.
                var shot = Wo141Rules.OneShot(f[1]);
                string obj = string.Join(' ', f.Skip(2));
                if (shot is not { } m || !W141Active || (_w141Kinds & Wo141Rules.KindPlayer) == 0) return;
                Interlocked.Increment(ref _w141Shows);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var ok = await _combat.Wo141ShowAsync(m.Unstance, obj, m.Tenths);
                        Console.WriteLine($"MP-W141 anim {f[1]} at {obj}: shown as {m.Unstance} for {m.Tenths / 10.0:0.0} s -> {(ok == true ? "ok" : ok is null ? "no answer" : "refused")}");
                    }
                    catch (Exception ex) { Console.WriteLine($"MP-W141 anim {f[1]} at {obj}: not shown ({ex.GetType().Name}: {ex.Message})"); }
                });
                return;
            }
            case "status":
                Console.WriteLine(Wo141StatsLine());
                _ = Task.Run(async () => { var s = await _combat.Wo141StatusAsync(); Console.WriteLine($"MP-W141 native: {s ?? "no answer"}"); });
                return;
        }
    }

    private string Wo141StatsLine() => FormattableString.Invariant(
        $"MP-W141 stats: {(_w141On ? "on" : "off")} bites={(_w141Bites ? "on" : "off")} kinds={_w141Kinds} active={W141Active} host={W141Host} joiner={W141Joiner} rows out={Interlocked.Read(ref _w141RowsOut)} in={Interlocked.Read(ref _w141RowsIn)} applies={Interlocked.Read(ref _w141Applies)} held={Interlocked.Read(ref _w141HeldRows)} leaves={Interlocked.Read(ref _w141Leaves)} back={Interlocked.Read(ref _w141Rejoins)} forwarded={Interlocked.Read(ref _w141Forwarded)} animal_attacks={Interlocked.Read(ref _w141Bite)} shows={Interlocked.Read(ref _w141Shows)} npcs={_w141HostRows.Count} blocked={_w141Blocked.Count} players={_w141PeerRows.Count}");
}
