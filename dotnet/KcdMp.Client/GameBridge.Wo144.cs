// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;

namespace KcdMp.Client;

// WO-144 1.1: the phantom partner (docs/WO-144-findings.md, 1.1).
//
// The partner set is the relay's live connections (PeerSet, Wo144.cs) and
// nothing else. Every per-partner loop -- the session mode (WO-124/125), the
// reload notice (WO-125), loot and chests (WO-134), quests (WO-137), crime
// (WO-139), sleep and wait (WO-140), activities (WO-141/143), the leash
// (WO-114) -- reads LivePartners(); the sleep and wait votes read
// VotingPartners() (connected AND in this world). A crime record the host keeps
// for a joiner survives his reconnect: parked under his name at the
// Disconnect, given back when that name connects again.
public partial class GameBridge
{
    private readonly PeerSet _peers = new();
    private readonly ConcurrentDictionary<byte, bool> _w144DropTold = new();
    private readonly ConcurrentDictionary<string, (JoinerCrimeRecord Rec, DateTime At)> _w144ParkedCrime = new();
    private long _w144Dropped;

    private bool IsLivePeer(byte id) => _peers.IsLive(id);

    /// <summary>Every connected partner (never this machine), in id order.</summary>
    private List<byte> LivePartners() => _peers.Partners(_myGhostId);

    /// <summary>The partners a sleep or wait vote waits for: connected and in this world (Wo144Rules.CountsInVote).</summary>
    private List<byte> VotingPartners()
    {
        var list = new List<byte>();
        foreach (byte g in LivePartners())
        {
            var (fresh, flags) = Wo144LeashView(g);
            if (Wo144Rules.CountsInVote(true, fresh, flags)) list.Add(g);
            else Console.WriteLine($"MP-W140 {W140Name(g)} is not asked: {Wo144Rules.VoteExclusion(true, fresh, flags)}");
        }
        return list;
    }

    /// <summary>True while a vote member still counts (connected and, by its latest state, in this world).</summary>
    private bool Wo144StillVoting(byte g)
    {
        if (!IsLivePeer(g)) return false;
        var (fresh, flags) = Wo144LeashView(g);
        return Wo144Rules.CountsInVote(true, fresh, flags);
    }

    private (bool Fresh, ushort Flags) Wo144LeashView(byte g) =>
        _leashJoinerState.TryGetValue(g, out var s)
            ? ((DateTime.UtcNow - s.AtUtc).TotalSeconds < LeashFreshS, s.S.Flags)
            : (false, (ushort)0);

    /// <summary>The relay named a client (Name packet): a partner now.</summary>
    private void Wo144OnPeerConnected(byte id, string name)
    {
        bool fresh = _peers.Connected(id, name);
        _w144DropTold.TryRemove(id, out _);
        var twins = _peers.SameName(id, name);
        if (fresh)
            Console.WriteLine($"MP-PEERS ghost {id} connected ({LivePartners().Count} partner(s) now){(twins.Count > 0 ? $" -- the same player as ghost {string.Join(",", twins)}: the relay replaces the old connection" : "")}");
        if (_w144ParkedCrime.TryRemove(name, out var parked) && (DateTime.UtcNow - parked.At).TotalHours < 2)
        {
            int dropped = parked.Rec.DropReportedTrespasses();   // WO-157 1.2: his own machine's trespass reports are not trusted
            _w139Records[id] = parked.Rec;
            Console.WriteLine($"MP-W139 host: ghost {id} is back -- the crime record kept for that player is his again ({parked.Rec.Count} crime(s)){(dropped > 0 ? $"; {dropped} trespass(es) his own game reported were dropped (WO-157)" : "")}");
        }
    }

    /// <summary>The relay's Disconnect, first thing: every loop stops counting it at once.</summary>
    private void Wo144OnPeerDisconnected(byte id)
    {
        string? name = _peers.Disconnected(id);
        if (name is not null && _w139Records.TryRemove(id, out var rec) && rec.Count > 0)
            _w144ParkedCrime[name] = (rec, DateTime.UtcNow);
        Console.WriteLine($"MP-PEERS ghost {id} disconnected ({LivePartners().Count} partner(s) now) -- it stays removed until the relay names that id again");
        // WO-151 3.6: a partner who left holds nothing here any more (his talks and stops ended with him)
        _ = ExecLuaAsync($"if KCD2MP_W151ReleasePeerHolds then KCD2MP_W151ReleasePeerHolds({id}) end");
    }

    /// <summary>The Disconnect handler's last step: per-partner tables the gone-handlers still read.</summary>
    private void Wo144AfterPeerGone(byte id)
    {
        if (_w138HostId == id) Wo144ReleaseClock("host-gone");
        _ghostNames.TryRemove(id, out _);
        _ghostReleaseVersions.TryRemove(id, out _);
        _modeTold.TryRemove(id, out _);
        _leashJoinerState.TryRemove(id, out _);
        _leashByJoiner.TryRemove(id, out _);
        _w141PeerRows.TryRemove(id, out _);
    }

    private void Wo144OnRelayLost()
    {
        _peers.Clear();
        _w144DropTold.Clear();
        Wo144ReleaseClock("relay-lost");
    }

    // ---------------------------------------------------------------- 3.3 one clock

    private volatile bool _w144ClockPaused;
    private bool _w144Following;

    /// <summary>"w144_clock 1|0" from the mod: this world's clock stands (a conversation, a quest, a minigame).</summary>
    private void Wo144OnClockLine(string? arg)
    {
        bool on = (arg ?? "").Trim() == "1";
        if (on == _w144ClockPaused) return;
        _w144ClockPaused = on;
        _ = _sendPauseIfChanged?.Invoke();
    }

    /// <summary>The joiner, on the host's pause state: its clock stands while the host's does (WO-112 T1).</summary>
    private void Wo144OnHostClock(byte was, byte now)
    {
        bool want = (now & Wo138Codec.ReasonClock) != 0 && _joinedWorld && !_w140Separate;
        if (want == _w144Following && ((was ^ now) & Wo138Codec.ReasonClock) == 0) return;
        _w144Following = want;
        Console.WriteLine($"MP-W144 the host's clock {(want ? "stands -- this clock stands with it" : "runs -- this clock runs with it")}");
        _ = ExecLuaAsync($"if KCD2MP_W144FollowHostClock then KCD2MP_W144FollowHostClock({(want ? "true" : "false")}, \"host\") end");
    }

    /// <summary>The host gone, the relay lost, this game leaving the host's world: never left standing.</summary>
    private void Wo144ReleaseClock(string why)
    {
        if (!_w144Following) return;
        _w144Following = false;
        Console.WriteLine($"MP-W144 this clock runs again ({why})");
        _ = ExecLuaAsync($"if KCD2MP_W144FollowHostClock then KCD2MP_W144FollowHostClock(false, \"{why}\") end");
    }

    // ---------------------------------------------------------------- 2.1 clothes: the game's own reasons

    private readonly ConcurrentDictionary<string, (string Reason, DateTime At)> _w144EquipReasons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The engine lines LogTailGameTransport.Wo144Line routes here.</summary>
    private void Wo144OnEngineLine(string line)
    {
        if (Wo166OnEngineLine(line)) return;  // WO-166: the loot screen's close (L1); dialogue states and pause-request timeouts (T3)
        Wo164OnEngineLine(line);              // WO-164: refusal times (the adaptive sweep), dialogues and their commands (the talk line)
        if (W160OnEngineLine(line)) return;   // WO-160: a planner failure, counted per NPC
        if (Wo144Rules.ParseCantEquip(line) is { } ce) { _w144EquipReasons[ce.Item] = (ce.Reason, DateTime.UtcNow); return; }
        Wo144OnEngineLineMore(line);
    }

    private readonly ConcurrentDictionary<string, long> _w144LineTold = new(StringComparer.Ordinal);
    private long _w153CancelMine, _w153CancelOther;

    private void Wo144OnEngineLineMore(string line)
    {
        // 1.3: the engine dropped this player's talk request (the copy never took it): the talk ends now,
        // nothing was held on the host (the hold waits for the conversation's start).
        const string drop = "Canceling dialog request id ";
        if (line.StartsWith(drop, StringComparison.Ordinal))
        {
            int sp = line.IndexOf(' ', drop.Length);
            if (int.TryParse(sp > 0 ? line[drop.Length..sp] : line[drop.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                // WO-147: the engine prints this for every soul's cancelled request (the field: 162 lines on one host,
                // 27 its player's); the mod ends a talk only when the id is this player's own (KCD2MP_W137TalkDropped).
                // WO-153 3: said plainly. The engine's "Request timed out" is its generic text: in the field every one of the
                // 15 on the joiner was refused in the SAME FRAME it was made (0.00-0.03 s), because a participant was not
                // free (this player alone, a gossip request, a greeting during a six-person conversation, a paused copy);
                // the 20 s timeout never ran. 8 were this player's own press with nobody to talk to; the host's log has
                // the same without any copy, so the cause is the game's, not ours.
                // WO-154 6.4: but not always at once -- the field's request 836 (a seated copy whose brain could not get
                // back into its activity, "Agent is stuck") waited the engine's full 20 s. This line cannot tell which:
                // the mod's "WO137-TALK end ... held_s=" says how long this player's own request waited.
                string soul = Wo144Rules.CancelSoul(line) ?? "?";
                bool mine = soul == "Dude";
                long n = mine ? Interlocked.Increment(ref _w153CancelMine) : Interlocked.Increment(ref _w153CancelOther);
                if (mine || n <= 5 || n % 50 == 0)
                    Console.WriteLine($"MP-W137 the engine cancelled dialog request {id} of '{soul}' (a participant was not free; its text says \"timed out\"){(mine ? " -- this player's own: if it was a talk, it ends here and nothing was held on the host (the mod's WO137-TALK end line has how long it waited)" : $" [n={n}, not this player's]")}");
                _ = ExecLuaAsync($"if KCD2MP_W137TalkDropped then KCD2MP_W137TalkDropped({id}) end");
            }
            return;
        }
        Wo144OnSceneLine(line);
    }

    /// <summary>Phase 4: scenes, faders, interrupted conversations, minigame objects -- logged (throttled) for now.</summary>
    private void Wo144OnSceneLine(string line)
    {
        string key = line.Length > 48 ? line[..48] : line;
        long now = Environment.TickCount64;
        if (_w144LineTold.TryGetValue(key, out long at) && now - at < 60_000) return;
        _w144LineTold[key] = now;
        Console.WriteLine($"MP-W144 engine: {(line.Length > 220 ? line[..220] : line)}");
    }

    /// <summary>What the game said about this piece in the last 30 s ("It requires 'body_cloth_padded' slot to be filled"), or null.</summary>
    private string? Wo144EquipReason(string itemName) =>
        _w144EquipReasons.TryGetValue(itemName, out var r) && (DateTime.UtcNow - r.At).TotalSeconds < 30 ? r.Reason : null;

    /// <summary>Under-layers first (the game refuses a piece whose under-layer is still empty).</summary>
    private async Task<List<Guid>> Wo144InLayerOrderAsync(IEnumerable<Guid> classes)
    {
        var idx = await SoulIndexAsync();
        return classes.OrderBy(c => idx is not null && idx.TryGetItem(c, out var it) ? Wo144Rules.EquipLayer(it.Name) : 3).ToList();
    }

    // ---------------------------------------------------------------- 2.1 / 2.4: how an avatar is dressed, and its lights

    /// <summary>mp_avatar_dress (default on): pieces from the avatar's own inventory, equipped through the actor (the mod's KCD2MP_W144Equip).</summary>
    private volatile bool _w144Dress = true;
    /// <summary>mp_avatar_lights (default on): lights are the torch sync's alone -- never part of an outfit.</summary>
    private volatile bool _w144Lights = true;

    /// <summary>
    /// One piece onto an avatar. REST EquipItem(class) made a new piece per call, and those pieces
    /// came off when the avatar walked crouched (the whole outfit in one frame, observed); the mod
    /// equips an item of that class from the avatar's own inventory (made once if it has none).
    /// </summary>
    private Task Wo144EquipOnGhostAsync(string soulName, Guid cls, bool createIfMissing, CancellationToken ct) =>
        _w144Dress
            ? _transport.ExecuteNowAsync($"if KCD2MP_W144Equip then KCD2MP_W144Equip(\"{soulName}\",\"{cls}\") end", ct)
            : _transport.EquipItemOnGhostAsync(soulName, cls, createIfMissing, ct);

    /// <summary>An outfit (or a read of what the avatar wears) without its lights: the torch sync owns those.</summary>
    private HashSet<Guid> Wo144WithoutLights(IEnumerable<Guid> classes) =>
        _w144Lights ? classes.Where(c => !Wo144Rules.IsLight(c)).ToHashSet() : [.. classes];

    /// <summary>w144 dress|lights on|off (the mod's switches, and its answer to the sync).</summary>
    private void Wo144OnModLine(string? arg)
    {
        var f = (arg ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 2 || f[1] is not ("on" or "off")) return;
        bool on = f[1] == "on";
        switch (f[0])
        {
            case "dress":
                if (_w144Dress != on) Console.WriteLine($"MP-W144 mp_avatar_dress {f[1]} -- {(on ? "avatars wear pieces from their own inventory" : "avatars are dressed through REST EquipItem (0.42.0)")}");
                _w144Dress = on;
                break;
            case "lights":
                if (_w144Lights != on) Console.WriteLine($"MP-W144 mp_avatar_lights {f[1]} -- {(on ? "lights are the torch sync's only" : "a torch in the outfit is worn as before (0.42.0)")}");
                _w144Lights = on;
                break;
        }
    }

    private long _w144OutfitRedressed;

    /// <summary>
    /// WO-144 2.1: an avatar's REAL outfit is read back every 10 s, not only when its player's outfit
    /// packet comes (a change, or the heartbeat). The engine takes an avatar's clothes off by itself
    /// (observed twice: the whole outfit in one frame, at the end of a crouched walk, no log line), and
    /// the next packet could be minutes away. Pieces the game refuses stay under their back-off; a
    /// running apply is never overlapped. Rides mp_avatar_dress.
    /// </summary>
    private async Task Wo144OutfitWatchAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(Wo144Rules.OutfitCheckMs, ct); } catch (OperationCanceledException) { return; }
            if (!_w144Dress || _where is GameWhere.Menu or GameWhere.Loading) continue;
            foreach (var g in LivePartners())
            {
                if (!_ghostWantedAppearance.TryGetValue(g, out var want)) continue;
                var gate = _ghostApplyGate.GetOrAdd(g, static _ => new SemaphoreSlim(1, 1));
                if (!await gate.WaitAsync(0, ct)) continue;   // an apply is running: it reads the real set itself
                try
                {
                    var actual = await ReadGhostSetAsync($"kcd2mp_{g}", ct);
                    if (actual is null) { Wo144LiveSoulUnread(g); continue; }
                    var target = Wo144WithoutLights(want);
                    var missing = Wo135Rules.AppearanceAdditions(Wo144WithoutLights(actual), target);
                    if (_ghostUnwearable.TryGetValue(g, out var u))
                    {
                        long now = Environment.TickCount64;
                        lock (u) missing = missing.Where(c => !u.Skips(c, now)).ToList();
                    }
                    if (missing.Count == 0) continue;
                    Interlocked.Increment(ref _w144OutfitRedressed);
                    Console.WriteLine($"[appearance] ghost {g}: {missing.Count} piece(s) came off since the last check (its player changed nothing) -- dressed again");
                    await ConvergeAppearanceAsync(g, want, ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Console.WriteLine($"[appearance] ghost {g}: the outfit check failed: {ex.Message}"); }
                finally { gate.Release(); }
            }
        }
    }

    // ---------------------------------------------------------------- 2.1: the live avatar's own soul

    private readonly ConcurrentDictionary<byte, long> _w144LiveSoulAskedMs = new();
    private long _w144LiveSoulsMapped;

    /// <summary>
    /// An avatar's REST calls go to SoulsByName/kcd2mp_N unless its live soul is known. A world saved
    /// while a partner was connected keeps that avatar's soul, and SoulsByName then answers with the
    /// saved one (observed on a joiner: a second kcd2mp_0 far away, in a villager's preset; 0.42.0
    /// dressed it and the partner stayed naked). The mod is asked (once per 10 s while unknown) for
    /// the live avatar's entity key; <see cref="Wo144OnAvatarKeyAsync"/> maps the name to that soul.
    /// </summary>
    private async Task Wo144EnsureLiveSoulAsync(byte ghostId)
    {
        var http = _httpForMenu;
        if (http is null || http.LiveSouls.ContainsKey($"kcd2mp_{ghostId}")) return;
        long now = Environment.TickCount64;
        if (_w144LiveSoulAskedMs.TryGetValue(ghostId, out long at) && now - at < 10_000) return;
        _w144LiveSoulAskedMs[ghostId] = now;
        await ExecLuaAsync($"if KCD2MP_W144AvatarKey then KCD2MP_W144AvatarKey(\"{ghostId}\") end");
        // the answer comes as a mod line; this outfit waits for it (up to 2 s) so that even the first
        // read and write go to the live soul, never to a saved one of the same name
        for (int i = 0; i < 20 && !http.LiveSouls.ContainsKey($"kcd2mp_{ghostId}"); i++) await Task.Delay(100);
    }

    /// <summary>"w144_avatar id suffix16": the soul whose key ends with the live avatar's entity key.</summary>
    private async Task Wo144OnAvatarKeyAsync(string? arg)
    {
        var f = (arg ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 2 || !byte.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte id)) return;
        var http = _httpForMenu;
        if (http is null) return;
        string name = $"kcd2mp_{id}";
        var keys = await http.ReadSoulKeysAsync();
        if (keys is null) { Console.WriteLine($"[appearance] ghost {id}: the soul list could not be read -- {name} stays addressed by name (tried again)"); return; }
        var live = Wo144Rules.SoulsWithEntityKey(keys, f[1]);
        if (live.Count != 1)
        {
            Console.WriteLine($"[appearance] ghost {id}: {live.Count} soul(s) carry the avatar's entity key {f[1]} -- {name} stays addressed by name");
            return;
        }
        var byName = await http.ReadGhostSoulGuidAsync(name);
        http.LiveSouls[name] = live[0];
        Interlocked.Increment(ref _w144LiveSoulsMapped);
        _ghostSoulGuidCache.TryRemove(id, out _);   // the WO-17 identity is the live soul's from now on
        if (byName is Guid bn && bn != live[0])
        {
            var other = await http.ReadSoulNameAtAsync(bn);
            Console.WriteLine($"[appearance] ghost {id}: {name} is another soul too (a saved one at {other?.Position ?? "?"}) -- every read and write goes to the live avatar's soul {live[0].ToString()[..8]}");
        }
        else Console.WriteLine($"[appearance] ghost {id}: the live avatar's soul {live[0].ToString()[..8]} is the one by name too");
    }

    /// <summary>A read through the live soul's key failed: that body is gone (a new one took the name) -- found again.</summary>
    private void Wo144LiveSoulUnread(byte ghostId)
    {
        if (_httpForMenu?.LiveSouls.ContainsKey($"kcd2mp_{ghostId}") != true) return;
        Wo144ForgetLiveSoul(ghostId);
        Console.WriteLine($"[appearance] ghost {ghostId}: its live soul did not answer (a new body?) -- found again at the next outfit check");
    }

    private void Wo144ForgetLiveSoul(byte ghostId)
    {
        _httpForMenu?.LiveSouls.TryRemove($"kcd2mp_{ghostId}", out _);
        _w144LiveSoulAskedMs.TryRemove(ghostId, out _);
    }

    private void Wo144ForgetLiveSouls()
    {
        _httpForMenu?.LiveSouls.Clear();
        _w144LiveSoulAskedMs.Clear();
    }

    // ---------------------------------------------------------------- 4.1 corrections that aren't

    private readonly Wo144Rules.CorrectionLedger _w144Corrections = new();

    /// <summary>A checkpoint correction's port produced another value than the host's: it is not fired for that value again.</summary>
    private void Wo144CorrectionMissed(string path, string port, int hostVal, int got)
    {
        if (_w144Corrections.Missed(path, port, hostVal, got))
            Console.WriteLine(FormattableString.Invariant($"MP-W144 correction of {path} through {port} gave {got}, not the host's {hostVal} -- not fired again for that value (the next join loads it exactly)"));
    }

    /// <summary>True (and one line) when this correction is known not to produce the host's value.</summary>
    private bool Wo144CorrectionSkipped(string path, string port, int hostVal)
    {
        if (!_w144Corrections.Skips(path, port, hostVal, out bool first)) return false;
        if (first) Console.WriteLine(FormattableString.Invariant($"MP-W144 MISMATCH {path}: the host has {hostVal}; {port} does not produce it here -- skipped (logged once)"));
        return true;
    }

    private string Wo144SyncLua() =>
        $"if KCD2MP_W144Sync then KCD2MP_W144Sync({(_w144Dress ? "true" : "false")}, {(_w144Lights ? "true" : "false")}) end";

    /// <summary>A frame from an id the relay removed (a replayed held frame, a late status): dropped, never a partner again.</summary>
    private bool Wo144DropFromRemoved(byte id)
    {
        if (!_peers.IsRemoved(id)) return false;
        Interlocked.Increment(ref _w144Dropped);
        if (_w144DropTold.TryAdd(id, true))
            Console.WriteLine($"MP-PEERS a frame from ghost {id} after its disconnect -- dropped (a removed ghost stays removed)");
        return true;
    }
}
