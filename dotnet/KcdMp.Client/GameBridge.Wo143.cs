// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-143: activities, part 2 -- the agent's half (docs/WO-143-findings.md).
//
// What else the bodies do, said the game's own way and placed by the other
// machine's game. The host's DLL reads its tracked NPCs (native wo143.cpp):
//   * the tool in each hand (the NPC state's hand content, as item classes),
//   * the gait context (the 9 actorCondition_* contexts),
//   * the one-shot fragments they start (caught at RequestStateChange),
//   * who they look at (C_NPCLookTarget's resolved target),
// and this agent sends them to every joiner (ActivityExtra 0x6E). A joiner's
// copy takes them: its tools ride WO-141's apply (an item of that class from
// its own inventory, else a temporary one the mod gives it and takes back), its
// gait contexts are set (only pairs its DLL set are cleared), its one-shots are
// played through the same NPC-state request (a paused copy's NPC-state machine
// ticked for as long as it plays), and its look is the actor's own forced look.
// The players' minigames need no message: WO-141's player rows carry them, and
// the avatar plays that minigame's own loop fragment on the receiving machine.
// Every piece has its own switch, on by default, and fails quietly: a refusal
// leaves the body as it was.
public partial class GameBridge
{
    /// <summary>The maintainer's rule: new fail-closed mechanisms ship ON (mp_hand_items, mp_activity_gaits, mp_oneshots, mp_player_minigames, mp_idles).</summary>
    public const bool W143Default = true;

    /// <summary>WO-153 1: mp_avatar_herbs ships OFF -- the avatar's herb-picking loop ended both of the joiner's 0.43.0 crashes.</summary>
    public const bool W143HerbsDefault = false;

    private volatile bool _w143Hands = W143Default, _w143Gaits = W143Default, _w143Shots = W143Default, _w143Minigames = W143Default, _w143Idles = W143Default;
    // WO-153 1: mp_avatar_herbs -- the avatar's herb-picking loop, OFF by default (the joiner's 0.43.0 crashes ended on it)
    private volatile bool _w143Herbs = W143HerbsDefault;
    private long _w143HerbsWithheldAtMs;
    private volatile bool _w143Connected;
    private int _w143CfgKey = -1;
    private long _w143CfgAtMs, _w143SyncAtMs;
    private byte _w143Armed;
    private bool _w143WasActive, _w143WasHolding;
    private byte _w143HostId = Protocol.ActivityPeerNone;
    // joiner: the host's latest rows (kept across a load, applied again after the settle)
    private readonly ConcurrentDictionary<string, ExtraRow> _w143HostHands = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ushort> _w143HostGaits = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string?> _w143LookNow = new(StringComparer.Ordinal);   // copy -> who it looks at here (null none)
    // joiner: the temporary tools the mod gave each copy (class texts)
    private readonly ConcurrentDictionary<string, HashSet<string>> _w143Temps = new(StringComparer.Ordinal);
    // both: each avatar's minigame on this machine (the player's row: the minigame and its object)
    private sealed class W143Mini
    {
        public byte Type; public ulong Obj; public int Phase; public Wo143Rules.MinigameShow Show = null!;
        public long StepAt; public bool StepAligned; public bool Held; public bool FellBack;
    }
    private readonly ConcurrentDictionary<byte, W143Mini> _w143Mini = new();
    private long _w143HandRowsOut, _w143GaitRowsOut, _w143LookRowsOut, _w143ShotsOut, _w143RowsIn, _w143ShotsIn, _w143ShotsPlayed,
                 _w143ShotsSkipped, _w143ShotsDone, _w143ShotsFailed, _w143Temps0, _w143TempsReleased, _w143Looks, _w143Loops;

    private W143Settings W143Now => new(_w143Hands, _w143Gaits, _w143Shots, _w143Minigames, _w143Idles);

    private void Wo143OnConnect(CancellationToken ct)
    {
        _w143Connected = true;
        _w143CfgKey = -1;
        _combat.OnExtraFrame = f => { _ = Task.Run(() => Wo143OnDllFrameAsync(f)); };
        _ = Wo143LoopAsync(ct);
    }

    private async Task Wo143OnDisconnectAsync()
    {
        _w143Connected = false;
        _combat.OnExtraFrame = null;
        try { await _combat.Wo143ConfigAsync(0, 0); } catch { }
        _w143CfgKey = -1;
        await Wo143ReleaseAllTempsAsync("the session ended");
        _w143HostHands.Clear(); _w143HostGaits.Clear(); _w143LookNow.Clear(); _w143Mini.Clear();
    }

    // ---------------------------------------------------------------- the 1 s loop

    private async Task Wo143LoopAsync(CancellationToken ct)
    {
        long lastStats = Environment.TickCount64;
        var knownPeers = new HashSet<byte>();
        while (!ct.IsCancellationRequested && _w143Connected)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                bool holding = Wo136Holding;
                bool active = W141Active;
                await Wo143PushConfigAsync(active);
                if (holding && !_w143WasHolding)
                {
                    int? n = await _combat.Wo143ForgetAsync();
                    if (n is > 0) Console.WriteLine($"MP-W143 a world loads: {n} gait/one-shot record(s) put aside until the settle");
                    _w143LookNow.Clear(); _w143Mini.Clear();
                }
                if (active && (!_w143WasActive || _w143WasHolding)) await Wo143ReapplyAllAsync(_w143WasHolding ? "after the load" : "session");
                _w143WasHolding = holding;
                _w143WasActive = active;
                if (active && W141Host)
                {
                    var now = Wo134Peers().ToHashSet();
                    if (now.Any(p => !knownPeers.Contains(p))) { await _combat.Wo143ResyncAsync(); Console.WriteLine("MP-W143 a player joined: every hand, gait and look row goes out again"); }
                    knownPeers = now;
                }
                if (active && W141Joiner) await Wo143QuietTickAsync();
                long t = Environment.TickCount64;
                if (t - _w143SyncAtMs >= 5_000)
                {
                    _w143SyncAtMs = t;
                    _ = ExecLuaAsync($"if KCD2MP_W143Sync then KCD2MP_W143Sync({B(_w143Hands)}, {B(_w143Gaits)}, {B(_w143Shots)}, {B(_w143Minigames)}, {B(_w143Idles)}, {B(_w143Herbs)}) end");
                    _ = ExecLuaAsync(Wo144SyncLua());   // WO-144: mp_avatar_dress / mp_avatar_lights as this game has them
                }
                if (t - lastStats >= 60_000) { lastStats = t; Console.WriteLine(Wo143StatsLine()); }
            }
            catch (Exception ex) { Console.WriteLine($"MP-W143 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private async Task Wo143PushConfigAsync(bool active)
    {
        var (cap, ap) = Wo143Rules.Masks(W143Now.ToRules(), active, W141Host, W141Joiner);
        int key = cap | (ap << 8);
        long now = Environment.TickCount64;
        if (key == _w143CfgKey && now - _w143CfgAtMs < 10_000) return;
        bool changed = key != _w143CfgKey;
        var armed = await _combat.Wo143ConfigAsync(cap, ap);
        if (armed is null) { if (changed && key != 0) Console.WriteLine("MP-W143 the DLL did not answer (no plugin, or not armed) -- hands, gaits, one-shots and looks are not shown; tried again"); return; }
        _w143CfgKey = key; _w143CfgAtMs = now; _w143Armed = armed.Value;
        if (changed)
            Console.WriteLine(key == 0
                ? "MP-W143 activities part 2 off (no partner, not the shared world, a load, mp_activities off, or every piece switched off)"
                : $"MP-W143 on: capture 0x{cap:X2} apply 0x{ap:X2} (1 hands, 2 gaits, 4 one-shots, 8 looks, 16 avatars' minigames); armed 0x{armed:X2} (1 hands, 2 gaits, 4 one-shots, 8 capture hook, 16 looks, 32 a paused copy's tick)");
    }

    // ---------------------------------------------------------------- the host: the DLL's rows out

    private async Task Wo143OnDllFrameAsync(Wo143DllFrame f)
    {
        try
        {
            switch (f.Kind)
            {
                case Wo143DllFrame.KindHands:
                case Wo143DllFrame.KindGaits:
                case Wo143DllFrame.KindLooks:
                case Wo143DllFrame.KindOneShot:
                {
                    if (!W141Active || !W141Host) return;
                    byte kind = f.Kind switch
                    {
                        Wo143DllFrame.KindHands => Protocol.ExtraKindHands, Wo143DllFrame.KindGaits => Protocol.ExtraKindGaits,
                        Wo143DllFrame.KindLooks => Protocol.ExtraKindLooks, _ => Protocol.ExtraKindOneShot,
                    };
                    foreach (byte j in Wo134Peers())
                        foreach (var b in Wo143Batches(kind, f.Rows)) await Wo143SendAsync(j, kind, b);
                    long n = f.Rows.Count;
                    if (kind == Protocol.ExtraKindHands) Interlocked.Add(ref _w143HandRowsOut, n);
                    else if (kind == Protocol.ExtraKindGaits) Interlocked.Add(ref _w143GaitRowsOut, n);
                    else if (kind == Protocol.ExtraKindLooks) Interlocked.Add(ref _w143LookRowsOut, n);
                    else Interlocked.Add(ref _w143ShotsOut, n);
                    return;
                }
                case Wo143DllFrame.KindNeedItem:
                {
                    // a copy's tool it does not own: the mod gives it a temporary one (its own inventory; never a world item)
                    var r = f.Rows[0];
                    string cls = ExtraRow.ClassText(r.Left);
                    if (!_w143Hands || cls == "-") return;
                    var set = _w143Temps.GetOrAdd(r.Name, _ => new HashSet<string>());
                    lock (set) { if (!set.Add(cls)) return; }
                    Interlocked.Increment(ref _w143Temps0);
                    await ExecLuaAsync($"if KCD2MP_W143Provide then KCD2MP_W143Provide(\"{EscapeLua(r.Name)}\", \"{cls}\") end");
                    return;
                }
                case Wo143DllFrame.KindShotDone:
                {
                    string name = f.Rows[0].Name;
                    if (f.Result == Wo143Rules.ResultDone) Interlocked.Increment(ref _w143ShotsDone); else Interlocked.Increment(ref _w143ShotsFailed);
                    if (name.StartsWith("kcd2mp_", StringComparison.Ordinal) && byte.TryParse(name.AsSpan(7), out byte peer))
                        await Wo143AvatarStepDoneAsync(peer, f.Result);
                    return;
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"MP-W143 DLL frame {f.Kind} not handled: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static IEnumerable<List<ExtraRow>> Wo143Batches(byte kind, IReadOnlyList<ExtraRow> rows)
    {
        int step = kind == Protocol.ExtraKindOneShot ? 1 : Protocol.ExtraMaxRows;
        for (int i = 0; i < rows.Count; i += step) yield return rows.Skip(i).Take(step).ToList();
    }

    private async Task Wo143SendAsync(byte target, byte kind, List<ExtraRow> rows)
    {
        try { await WriteJoinAsync(ExtraCodec.BuildUp(target, kind, rows)); }
        catch (Exception ex) { Console.WriteLine($"MP-W143 {rows.Count} row(s) of kind {kind} not sent to {target}: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- the joiner: the host's rows in

    private async Task Wo143OnFrameAsync(byte src, byte[] body)
    {
        if (!ExtraCodec.TryDecode(body, out byte kind, out var rows)) { Console.WriteLine($"MP-W143 malformed extra frame from {src} ({body.Length} bytes)"); return; }
        if (!W141Joiner) return;
        _w143HostId = src;
        Interlocked.Add(ref _w143RowsIn, rows.Count);
        foreach (var r in rows)
        {
            switch (kind)
            {
                case Protocol.ExtraKindHands:
                    _w143HostHands[r.Name] = r;
                    if (W141Active && _w143Hands) await Wo143ApplyHandsAsync(r);
                    break;
                case Protocol.ExtraKindGaits:
                    _w143HostGaits[r.Name] = r.Gaits;
                    if (W141Active && _w143Gaits && !W151InFight(r.Name)) await _combat.Wo143GaitsAsync(r.Name, r.Gaits);   // WO-151 1.1
                    break;
                case Protocol.ExtraKindLooks:
                    if (W141Active && _w143Idles) await Wo143ApplyLookAsync(r.Name, Wo143Rules.LookTargetHere(r.TargetKind, r.Target, src, _myGhostId));
                    break;
                case Protocol.ExtraKindOneShot:
                    Interlocked.Increment(ref _w143ShotsIn);
                    if (!W141Active || !_w143Shots) break;
                    if (Wo143Rules.Quiet(_w141Blocked.GetValueOrDefault(r.Name))) { Interlocked.Increment(ref _w143ShotsSkipped); break; }
                    var id = await _combat.Wo143OneShotAsync(r.Name, r.Fragment, r.Tags, r.AlignGuid, r.Flags);
                    if (id is >= 0) Interlocked.Increment(ref _w143ShotsPlayed); else Interlocked.Increment(ref _w143ShotsSkipped);
                    break;
            }
        }
    }

    private async Task Wo143ApplyHandsAsync(ExtraRow r)
    {
        // WO-151 1.1: a copy in a fight holds no tool (its weapon is the fight's); the row waits for the fight's end
        if (W151InFight(r.Name)) { Interlocked.Increment(ref _w151HandsHeld); return; }
        await _combat.Wo143HandsAsync(r.Name, r.Left, r.Right);
        // a temporary tool the copy no longer wants goes (once it is out of the hand: the mod checks)
        if (_w143Temps.TryGetValue(r.Name, out var set))
        {
            List<string> go;
            lock (set) go = Wo143Rules.TempsToRelease(set, r.Left, r.Right).ToList();
            foreach (var cls in go)
            {
                lock (set) set.Remove(cls);
                _ = Task.Run(async () => { await Task.Delay(2_500); await Wo143ReleaseTempAsync(r.Name, cls); });
            }
        }
    }

    /// <summary>The mod deletes the temporary tool once it is out of the copy's hand; "inhand" comes back as a mod line and is tried again.</summary>
    private Task Wo143ReleaseTempAsync(string name, string cls, int tries = 0) =>
        ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W143Release then KCD2MP_W143Release(\"{EscapeLua(name)}\", \"{cls}\", {tries}) end"));

    private async Task Wo143ReleaseAllTempsAsync(string why)
    {
        int n = _w143Temps.Values.Sum(s => s.Count);
        if (n == 0) return;
        try { await ExecLuaAsync("if KCD2MP_W143ReleaseAll then KCD2MP_W143ReleaseAll() end"); } catch { }
        _w143Temps.Clear();
        Console.WriteLine($"MP-W143 {n} temporary tool(s) taken back ({why})");
    }

    private async Task Wo143ApplyLookAsync(string copy, string? target)
    {
        if (Wo143Rules.Quiet(_w141Blocked.GetValueOrDefault(copy))) target = null;
        string? was = _w143LookNow.GetValueOrDefault(copy);
        if (was == target && _w143LookNow.ContainsKey(copy)) return;
        _w143LookNow[copy] = target;
        Interlocked.Increment(ref _w143Looks);
        await ExecLuaAsync($"if KCD2MP_W143Look then KCD2MP_W143Look(\"{EscapeLua(copy)}\", \"{EscapeLua(target ?? "")}\") end");
    }

    /// <summary>A copy that leaves its activity (fight, talk, down: WO-141's block) looks at nobody we forced.</summary>
    private async Task Wo143QuietTickAsync()
    {
        foreach (var (copy, target) in _w143LookNow)
            if (target is not null && Wo143Rules.Quiet(_w141Blocked.GetValueOrDefault(copy))) await Wo143ApplyLookAsync(copy, null);
    }

    /// <summary>After a load, or a session start: every kept row again (the looks come with the host's next change).</summary>
    private async Task Wo143ReapplyAllAsync(string why)
    {
        if (!W141Joiner) return;
        int n = 0;
        if (_w143Hands) foreach (var r in _w143HostHands.Values) if (!r.HandsEmpty) { await _combat.Wo143HandsAsync(r.Name, r.Left, r.Right); n++; }
        if (_w143Gaits) foreach (var (name, m) in _w143HostGaits) if (m != 0) { await _combat.Wo143GaitsAsync(name, m); n++; }
        if (n > 0) Console.WriteLine($"MP-W143 {n} hand/gait row(s) applied again ({why})");
    }

    // ---------------------------------------------------------------- the avatars: the players' minigames

    /// <summary>
    /// WO-141 hands this every player row that reaches an avatar here (either role). A minigame starts the way the game
    /// starts the player's: its entry at the same object (aligned where the game's is: the game puts the avatar where it
    /// put the player; the writer stays off him meanwhile), then its loop, and at the end its out. Anything the game
    /// refuses falls back to the loop where the avatar stands, and then to standing.
    /// </summary>
    private async Task Wo143OnPlayerRowAsync(byte peer, ActivityState a)
    {
        if (!_w143Minigames || !W141Active || peer == _myGhostId) return;
        var show = Wo143Rules.AvatarShow(a, _w143Herbs);
        if (show is null && a.Minigame == Wo143Rules.HerbMinigame && Environment.TickCount64 - _w143HerbsWithheldAtMs > 60_000)
        {
            _w143HerbsWithheldAtMs = Environment.TickCount64;
            Console.WriteLine($"MP-W143 player {peer}: herb gathering -- the avatar stands (mp_avatar_herbs is off: the 0.43.0 joiner crashes ended on its PickingHerbs loop)");
        }
        var cur = _w143Mini.GetValueOrDefault(peer);
        if (cur is not null && show is not null && cur.Type == show.Type && cur.Obj == a.MinigameObj) return;
        if (cur is null && show is null) return;
        if (cur is not null) await Wo143EndMinigameAsync(peer, cur, show is null ? "the minigame ended" : "another minigame");
        if (show is null) return;
        var m = new W143Mini { Type = show.Type, Obj = a.MinigameObj, Show = show };
        _w143Mini[peer] = m;
        // WO-151 4.2/4.3: never at the station's object unless mp_minigame_align on -- the field's smithing entry
        // aligned at the anvil showed nothing on the host, floated the avatar and kept the partner's anvil "in use";
        // the grindstone that worked played where he stood (the player's own place is at the station anyway)
        var (frag, tags, aligned, phase) = Wo143Rules.FirstStep(show, _w151MinigameAlign ? a.MinigameObj : 0);
        Console.WriteLine($"MP-W143 player {peer}: {show.Name} -- the avatar plays {frag}{(aligned ? $" at the same object ({a.MinigameObj:X16})" : " where he stands")}, then {show.Loop}");
        if (aligned) m.Held = await _combat.Wo143HoldAsync(Wo141Rules.AvatarName(peer), true);
        await Wo143StepAsync(peer, m, frag, tags, aligned, phase);
    }

    private async Task Wo143StepAsync(byte peer, W143Mini m, string frag, string tags, bool aligned, int phase)
    {
        m.Phase = phase; m.StepAt = Environment.TickCount64; m.StepAligned = aligned;
        var id = await _combat.Wo143OneShotAsync(Wo141Rules.AvatarName(peer), frag, tags, aligned ? m.Obj : 0, (byte)(aligned ? 2 : 0));
        Interlocked.Increment(ref _w143Loops);
        if (id is not >= 0)
        {
            Console.WriteLine($"MP-W143 avatar {peer}: {frag} not played (request {id?.ToString(CultureInfo.InvariantCulture) ?? "no answer"})");
            await Wo143AvatarStepDoneAsync(peer, Wo143Rules.ResultFailed);
        }
    }

    private async Task Wo143AvatarStepDoneAsync(byte peer, byte result)
    {
        if (!_w143Mini.TryGetValue(peer, out var m)) return;
        long ran = Environment.TickCount64 - m.StepAt;
        var (action, phase) = Wo143Rules.AfterStep(m.Phase, result, m.StepAligned, ran);
        switch (action)
        {
            case "loop":
                await Wo143StepAsync(peer, m, m.Show.Loop, m.Show.LoopTags, false, Wo143Rules.PhaseLoop);
                break;
            case "loop-in-place" when !m.FellBack:
                m.FellBack = true;
                Console.WriteLine($"MP-W143 avatar {peer}: the aligned {m.Show.Name} step was refused or carried him away (result {result}) -- the loop where he stands");
                if (m.Held) { await _combat.Wo143HoldAsync(Wo141Rules.AvatarName(peer), false); m.Held = false; }
                await Wo143StepAsync(peer, m, m.Show.Loop, m.Show.LoopTags, false, Wo143Rules.PhaseLoop);
                break;
            case "loop-in-place":
            case "stand":
                Console.WriteLine($"MP-W143 avatar {peer}: {m.Show.Name} is not shown (result {result}) -- the avatar stands");
                if (m.Held) { await _combat.Wo143HoldAsync(Wo141Rules.AvatarName(peer), false); m.Held = false; }
                m.Phase = Wo143Rules.PhaseNone;
                break;
            case "release":
                if (m.Held) { await _combat.Wo143HoldAsync(Wo141Rules.AvatarName(peer), false); m.Held = false; }
                _w143Mini.TryRemove(new KeyValuePair<byte, W143Mini>(peer, m));
                break;
        }
    }

    private async Task Wo143EndMinigameAsync(byte peer, W143Mini m, string why)
    {
        string avatar = Wo141Rules.AvatarName(peer);
        if (m.Phase is Wo143Rules.PhaseEntry or Wo143Rules.PhaseLoop && m.Show.Out is { } outFrag)
        {
            Console.WriteLine($"MP-W143 player {peer}: {why} -- the avatar plays {outFrag}");
            m.Phase = Wo143Rules.PhaseOut; m.StepAt = Environment.TickCount64; m.StepAligned = false;
            var id = await _combat.Wo143OneShotAsync(avatar, outFrag, m.Show.OutTags, 0, 0);
            if (id is >= 0)
            {
                // the out's own end releases the hold; a stuck one is released after 6 s anyway
                _ = Task.Run(async () =>
                {
                    await Task.Delay(6_000);
                    if (m.Held) { m.Held = false; await _combat.Wo143HoldAsync(avatar, false); }
                    _w143Mini.TryRemove(new KeyValuePair<byte, W143Mini>(peer, m));
                });
                return;
            }
        }
        Console.WriteLine($"MP-W143 player {peer}: {why} -- the avatar's loop stops");
        await _combat.Wo143StopAsync(avatar);
        if (m.Held) { m.Held = false; await _combat.Wo143HoldAsync(avatar, false); }
        _w143Mini.TryRemove(new KeyValuePair<byte, W143Mini>(peer, m));
    }

    // ---------------------------------------------------------------- settings and status

    private void Wo143OnModLine(string? arg)
    {
        var f = (arg ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length == 0) return;
        switch (f[0])
        {
            case "inhand" when f.Length >= 4 && int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tries):
                // the mod found the temporary tool still in the copy's hand: again in 2 s, six times at most
                if (tries < 6) { string n = f[1], c = f[2]; _ = Task.Run(async () => { await Task.Delay(2_000); await Wo143ReleaseTempAsync(n, c, tries + 1); }); }
                else { Console.WriteLine($"MP-W143 {f[1]}: its temporary {f[2]} stayed in its hand -- taken back at the end of the session"); _w143Temps.GetOrAdd(f[1], _ => new HashSet<string>()).Add(f[2]); }
                return;
            case "released" when f.Length >= 3:
                Interlocked.Increment(ref _w143TempsReleased);
                return;
        }
        if (f[0] == "status")
        {
            Console.WriteLine(Wo143StatsLine());
            _ = Task.Run(async () => { var s = await _combat.Wo143StatusAsync(); Console.WriteLine($"MP-W143 native: {s ?? "no answer"}"); });
            return;
        }
        if (f.Length < 2 || f[1] is not ("on" or "off")) return;
        bool on = f[1] == "on";
        string? setting = f[0] switch
        {
            "hands" => "mp_hand_items", "gaits" => "mp_activity_gaits", "oneshots" => "mp_oneshots", "minigames" => "mp_player_minigames", "idles" => "mp_idles", "herbs" => "mp_avatar_herbs", _ => null,
        };
        if (setting is null) return;
        bool was = f[0] switch { "hands" => _w143Hands, "gaits" => _w143Gaits, "oneshots" => _w143Shots, "minigames" => _w143Minigames, "herbs" => _w143Herbs, _ => _w143Idles };
        switch (f[0])
        {
            case "hands": _w143Hands = on; break;
            case "gaits": _w143Gaits = on; break;
            case "oneshots": _w143Shots = on; break;
            case "minigames": _w143Minigames = on; break;
            case "idles": _w143Idles = on; break;
            case "herbs": _w143Herbs = on; break;
        }
        _w143CfgKey = -1;
        if (was != on) Console.WriteLine($"MP-W143 {setting} {(on ? "on" : "off")}");
        if (!on && f[0] == "hands") _ = Task.Run(async () => { await Task.Delay(3_000); await Wo143ReleaseAllTempsAsync("mp_hand_items off"); });
        if (!on && f[0] == "idles") _ = Task.Run(async () => { foreach (var c in _w143LookNow.Keys.ToList()) await Wo143ApplyLookAsync(c, null); });
        if (!on && f[0] == "herbs")
            _ = Task.Run(async () => { foreach (var (peer, m) in _w143Mini.Where(kv => kv.Value.Type == Wo143Rules.HerbMinigame).ToList()) await Wo143EndMinigameAsync(peer, m, "mp_avatar_herbs off"); });
        if (!on && f[0] == "minigames")
            _ = Task.Run(async () => { foreach (var (peer, m) in _w143Mini.ToList()) await Wo143EndMinigameAsync(peer, m, "mp_player_minigames off"); });
    }

    private string Wo143StatsLine() => FormattableString.Invariant(
        $"MP-W143 stats: hands={(_w143Hands ? "on" : "off")} gaits={(_w143Gaits ? "on" : "off")} oneshots={(_w143Shots ? "on" : "off")} minigames={(_w143Minigames ? "on" : "off")} idles={(_w143Idles ? "on" : "off")} herbs={(_w143Herbs ? "on" : "off")} armed=0x{_w143Armed:X2} host={W141Host} joiner={W141Joiner} out hands={Interlocked.Read(ref _w143HandRowsOut)} gaits={Interlocked.Read(ref _w143GaitRowsOut)} looks={Interlocked.Read(ref _w143LookRowsOut)} shots={Interlocked.Read(ref _w143ShotsOut)} | in rows={Interlocked.Read(ref _w143RowsIn)} shots={Interlocked.Read(ref _w143ShotsIn)} played={Interlocked.Read(ref _w143ShotsPlayed)} skipped={Interlocked.Read(ref _w143ShotsSkipped)} done={Interlocked.Read(ref _w143ShotsDone)} failed={Interlocked.Read(ref _w143ShotsFailed)} looks={Interlocked.Read(ref _w143Looks)} temps={Interlocked.Read(ref _w143Temps0)} released={Interlocked.Read(ref _w143TempsReleased)} avatar_loops={Interlocked.Read(ref _w143Loops)} copies_with_tools={_w143HostHands.Count(kv => !kv.Value.HandsEmpty)}");
}

/// <summary>WO-143: the five switches as the bridge keeps them.</summary>
public readonly record struct W143Settings(bool Hands, bool Gaits, bool OneShots, bool Minigames, bool Idles)
{
    public Wo143Rules.Settings ToRules() => new(Hands, Gaits, OneShots, Minigames, Idles);
}
