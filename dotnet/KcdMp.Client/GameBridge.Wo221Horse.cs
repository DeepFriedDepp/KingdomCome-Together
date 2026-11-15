// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;

namespace KcdMp.Client;

// WO-221 D (mp_own_horse; docs/WO-221-findings.md): a horse per player.
//
// The game has one player and one "player's horse" (the player soul's primary companion of type Horse); a purchase's quest graph sets
// it (ReplacePlayerHorse) and disposes of the old one. In co-op:
//   * the host's world keeps THE RECORD of whose horse is whose (HorseRecord, Wo221Horse.cs): keyed by each joiner's install key,
//     kept beside the chest ledger, paired with every save of the host's (a reload brings back that save's record);
//   * a joiner's own game says "my horse is X" when its own purchase made it so (WO-160's word, QuestAsk kind 4; its token now
//     carries the joiner's key). The mod sends the word only for a change of that game's own horse AFTER the record was told to it
//     (a join or a reload never makes a word); the host takes it when X is not the host's own horse and no other joiner has X.
//     (The bench: the purchase's node sits in an included dialogue, its consequences run in the joiner's game only and are not
//     replayed here -- the price leaves his purse there, once. When a replay of a purchase or a gift does run in this world, the
//     DLL's quest gate passes its ReplacePlayerHorse through -- event 6 -- so this game's own horse is never touched.)
//   * the host tells every joiner the whole record (the crime channel's kind 10), on a change, on a join and every 30 s; each
//     joiner's mod then makes its own game's "player's horse" what the record says (the copy of its own horse, or none);
//   * the host's own horse is never in the record: its game keeps it itself.
// Log tags: MP-W221 horse ..., WO221-HORSE (the mod and the DLL).
public sealed partial class GameBridge
{
    private readonly HorseStore _w221HorseStore = new(HorseStore.DefaultRoot());
    private readonly object _w221HorseGate = new();
    private HorseRecord? _w221HorseRec;
    private string? _w221HorseTag;
    private readonly ConcurrentDictionary<byte, uint> _w221PeerKey = new();        // host: a joiner's install key
    private readonly ConcurrentDictionary<uint, byte> _w221ClosurePeer = new();    // host: a replay closure's joiner
    private readonly ConcurrentDictionary<byte, long> _w221HorsePassMs = new();    // host: when the gate passed a horse node for that joiner
    private readonly ConcurrentDictionary<byte, (string Horse, long AtMs)> _w221ClaimWaiting = new();   // host: a word that came before its pass
    private volatile string _w221HostOwnHorse = "";
    private volatile bool _w221HostOwnHorseKnown;
    private string _w221HorsesSent = "";
    private long _w221HorsesSentAtMs, _w221HorseClaims, _w221HorseTaken, _w221HorseRefused, _w221HorsePasses, _w221HorseTold, _w221HorseAsks, _w221AskWordAtMs;
    private volatile string _w221MyHorse = "?";   // joiner: what the record says is this player's ("" none, "?" not told yet)

    private const long W221HorsePassWindowMs = 120_000;

    private bool W221OwnHorse => W221On("own_horse");

    private HorseRecord? Wo221HorseRecord()
    {
        string? tag = _hostWorldSeed is uint s ? WhsSave.SeedTag(s) : null;
        if (tag is null) return null;
        lock (_w221HorseGate)
        {
            if (_w221HorseTag != tag || _w221HorseRec is null)
            {
                try { _w221HorseRec = _w221HorseStore.Current(tag); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { _w221HorseRec = new HorseRecord(); }
                _w221HorseTag = tag;
            }
            return _w221HorseRec;
        }
    }

    /// <summary>The host's world was saved (not loaded) or loaded: the record is paired with the save, or that save's record is back.</summary>
    private void Wo221HorseOnHostWorld(string tag, string md5, bool loaded)
    {
        lock (_w221HorseGate)
        {
            try
            {
                if (_w221HorseTag != tag || _w221HorseRec is null) { _w221HorseRec = _w221HorseStore.Current(tag); _w221HorseTag = tag; }
                if (!loaded)
                {
                    _w221HorseStore.Pair(tag, md5, _w221HorseRec);
                    _w221HorseStore.SaveCurrent(tag, _w221HorseRec);
                    Console.WriteLine($"MP-W221 horse: host save md5 {md5[..8]}: the record paired ({_w221HorseRec.Entries.Count} horse(s): {_w221HorseRec.Csv()})");
                }
                else if (_w221HorseStore.Paired(tag, md5) is { } p)
                {
                    _w221HorseRec = p;
                    _w221HorseStore.SaveCurrent(tag, p);
                    Console.WriteLine($"MP-W221 horse: host loaded md5 {md5[..8]}: its paired record is back ({p.Entries.Count} horse(s): {p.Csv()})");
                }
                else
                {
                    // a save from before any horse was recorded (or older than the pairs kept): in that world nobody's horse is
                    // recorded -- never a later save's record (a horse the joiner had not bought there)
                    int had = _w221HorseRec.Entries.Count;
                    _w221HorseRec = new HorseRecord();
                    _w221HorseStore.SaveCurrent(tag, _w221HorseRec);
                    Console.WriteLine($"MP-W221 horse: host loaded md5 {md5[..8]}: no record paired with it -- nobody's horse is recorded in that world (the running record had {had})");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.WriteLine($"MP-W221 horse: the record's pairing failed: {ex.Message}");
            }
        }
        _w221HorsesSent = "";   // told again
    }

    /// <summary>Host: a joiner's replay is about to run in this world (its closure id): a horse node the gate passes in it is that joiner's.</summary>
    private readonly ConcurrentDictionary<byte, string> _w221PassModule = new();
    private string _w221ModsSent = "\u0001"; private long _w221ModsSentAtMs, _w221HostBuys;
    private void Wo221NoteReplay(byte src, uint closure)
    {
        if (_w221ClosurePeer.Count > 4096) _w221ClosurePeer.Clear();
        _w221ClosurePeer[closure] = src;
    }

    /// <summary>The DLL's event 6: the quest gate passed a ReplacePlayerHorse (or the Kill after it) through.</summary>
    private void Wo221OnHorsePass(uint closure, string what, string path)
    {
        Interlocked.Increment(ref _w221HorsePasses);
        if (!W137Host)
        {
            Console.WriteLine($"MP-W221 horse: the host's own purchase or gift, mirrored here, left this player's horse alone ({what}: {path})");
            return;
        }
        if (what == "hostbuy")
        {
            Interlocked.Increment(ref _w221HostBuys);
            Console.WriteLine($"MP-W221 horse: the host bought a horse the record names a joiner's ({path}): it stays the joiner's; this game's own horse is unchanged; the price went to the groom");
            _ = ExecLuaAsync("if KCD2MP_Wo124Msg then KCD2MP_Wo124Msg(\"That horse is your partner's. It stays theirs; the groom kept the coin.\") end");
            return;
        }
        if (!_w221ClosurePeer.TryGetValue(closure, out byte peer))
        {
            Console.WriteLine($"MP-W221 horse: the gate passed {what} in closure {closure:X8} of no known joiner ({path})");
            return;
        }
        if (what != "replace") return;
        _w221HorsePassMs[peer] = Environment.TickCount64;
        _w221PassModule[peer] = Wo221Rules.HorseModuleOf(path);   // the module instance that sells the horse he bought
        Console.WriteLine($"MP-W221 horse: ghost {peer}'s purchase or gift ran in this world (closure {closure:X8}): this game's own horse stays as it is; his word names the horse");
    }

    /// <summary>Host: a joiner's word "my own horse is X" (X = "-": only its key, sent on a join). The token is the joiner's install key.</summary>
    private void Wo221OnHorseClaim(byte src, uint key, string horse)
    {
        if (key != 0) _w221PeerKey[src] = key;
        if (!_w221PeerKey.TryGetValue(src, out uint k)) { Console.WriteLine($"MP-W221 horse: ghost {src}'s word has no key (an older build): not recorded"); return; }
        var rec = Wo221HorseRecord();
        if (rec is null) { Console.WriteLine("MP-W221 horse: this world is not identified yet: the word waits for the next one"); return; }
        if (horse == "-") { _w221HorsesSent = ""; return; }   // a join: told the record
        Interlocked.Increment(ref _w221HorseClaims);
        long now = Environment.TickCount64;
        string why;
        lock (_w221HorseGate)
        {
            if (rec.HorseOf(k) == horse) why = "";   // his already
            else if (horse == _w221HostOwnHorse) why = "it is the host's own horse";
            else if (rec.OwnerOf(horse) is { } o && o.Owner != k) why = "another player's horse";
            else if (!_w221HostOwnHorseKnown)
            {
                _w221ClaimWaiting[src] = (horse, now);
                why = "this game's own horse is not read yet (the word waits for it)";
            }
            else
            {
                bool ranHere = _w221HorsePassMs.TryGetValue(src, out long at) && now - at <= W221HorsePassWindowMs;
                rec.Set(k, $"ghost {src}", horse, ranHere ? "bought (its purchase ran in this world)" : "bought (in his own game)", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                try { _w221HorseStore.SaveCurrent(_w221HorseTag!, rec); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (ranHere && _w221PassModule.TryRemove(src, out string? mod) && mod.Length > 0 && rec.OwnerOf(horse) is { } ent) ent.Module = mod;
                try { _w221HorseStore.SaveCurrent(_w221HorseTag!, rec); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                _w221HorsePassMs.TryRemove(src, out _);
                Interlocked.Increment(ref _w221HorseTaken);
                Console.WriteLine($"MP-W221 horse: {horse} is ghost {src}'s own horse in this world now (key {k:x8}; the record: {rec.Csv()})");
                why = "";
            }
        }
        if (why.Length > 0)
        {
            Interlocked.Increment(ref _w221HorseRefused);
            Console.WriteLine($"MP-W221 horse: ghost {src}'s word \"{horse}\" not taken: {why}");
        }
        _w221HorsesSent = "";   // every joiner is told what the record says (a refused word is corrected there)
    }

    /// <summary>Host, 1 Hz: the record to every joiner when it changed, and every 30 s; the mod is told whose horses are in this world.</summary>
    private void Wo221HorseHostTick()
    {
        if (!W137Host || !W221OwnHorse) return;
        var rec = Wo221HorseRecord();
        if (rec is null) return;
        // a word that waited for this game's own horse to be read
        if (_w221HostOwnHorseKnown)
            foreach (var (peer, w) in _w221ClaimWaiting.ToArray())
                if (_w221ClaimWaiting.TryRemove(peer, out _) && Environment.TickCount64 - w.AtMs < W221HorsePassWindowMs) Wo221OnHorseClaim(peer, 0, w.Horse);
        string csv, mods;
        lock (_w221HorseGate) { csv = rec.Csv(); mods = string.Join("\n", rec.Entries.Where(e => e.Module.Length > 0).Select(e => e.Module).Distinct()); }
        // D.1: the DLL's gate is told which modules sell a joiner's horse (on a change and every 30 s)
        long nowM = Environment.TickCount64;
        if (mods != _w221ModsSent || nowM - _w221ModsSentAtMs > 30_000)
        {
            _w221ModsSent = mods; _w221ModsSentAtMs = nowM;
            _ = _combat.Wo163Async(Protocol.Wo221OpHorseModules, Wo221Rules.HorseModulesBody(mods.Length == 0 ? [] : mods.Split('\n')));
        }
        long now = Environment.TickCount64;
        if (csv == _w221HorsesSent && now - _w221HorsesSentAtMs < 30_000) return;
        bool changed = csv != _w221HorsesSent;
        _w221HorsesSent = csv;
        _w221HorsesSentAtMs = now;
        foreach (byte g in Wo134Peers()) _ = Wo139SendAsync(Protocol.CrimeHostUp, g, Protocol.CrimeHostHorseOwners, 0, csv);
        // the mod: which ghost's horse is which (the keys stay here)
        var byPeer = new List<string>();
        foreach (var (peer, key) in _w221PeerKey)
            if (rec.HorseOf(key) is string h) byPeer.Add(string.Create(CultureInfo.InvariantCulture, $"{peer}={h}"));
        if (changed) _ = ExecLuaAsync($"if KCD2MP_W221PeerHorses then KCD2MP_W221PeerHorses(\"{string.Join(',', byPeer)}\") end");
    }

    /// <summary>Joiner: the host's record. This player's horse is the one under his own key, or none.</summary>
    private void Wo221OnHorseOwnersText(string? text)
    {
        if (!W137Joiner || !W221OwnHorse) return;
        uint me = Wo157InstallKey();
        string mine = "";
        foreach (var (horse, key) in HorseRecord.ParseCsv(text)) if (key == me) mine = horse;
        Interlocked.Increment(ref _w221HorseTold);
        if (mine != _w221MyHorse) Console.WriteLine($"MP-W221 horse: the host's record: this player's own horse is {(mine.Length > 0 ? mine : "none")}");
        _w221MyHorse = mine;   // told to the mod every time (the mod forgets it when its session pauses: a load)
        _ = ExecLuaAsync($"if KCD2MP_W221MyHorse then KCD2MP_W221MyHorse(\"{EscapeLua(mine)}\") end");
    }

    /// <summary>Joiner: this game's own word (the mod's w160_ownhorse) goes up with this install's key; "-" on a join.</summary>
    private void Wo221SendHorseWord(string horse)
    {
        if (!W137Joiner) return;
        Console.WriteLine(horse == "-" ? "MP-W221 horse: this player's key told to the host (its record answers)" : $"MP-W221 horse: this game says its player's horse is {horse} -- told to the host with this player's key");
        _ = Wo137SendAsync(Protocol.QuestAskUp, Protocol.JoinTargetHost, Protocol.QuestAskOwnHorse, Wo157InstallKey(), horse);
    }

    private string Wo221HorseStats() =>
        $" horses: record={(Wo221HorseRecordCsv())} claims={_w221HorseClaims} taken={_w221HorseTaken} refused={_w221HorseRefused} gate_passes={_w221HorsePasses} told={_w221HorseTold} asks={_w221HorseAsks} mine={_w221MyHorse} host_own={(_w221HostOwnHorse.Length > 0 ? _w221HostOwnHorse : "-")}";

    private string Wo221HorseRecordCsv()
    {
        if (!W137Host) return "-";
        var rec = Wo221HorseRecord();
        if (rec is null) return "?";
        lock (_w221HorseGate) return rec.Csv();
    }

    // ---------------------------------------------------------------- D.4: the saddlebags (mp_saddlebags)

    private readonly ConcurrentDictionary<byte, long> _w221BagListAtMs = new();   // host: when a joiner's list last came
    private long _w221BagLists, _w221BagTakes, _w221BagAnswers, _w221BagBases, _w221BagRefused;

    /// <summary>Host: a joiner's horse inventory (kind 6) or his answer to the partner's take (kind 7). Only the horse the record names his.</summary>
    private void Wo221OnBagAsk(byte src, byte kind, uint key, string text)
    {
        if (!W137Host || !W221On("saddlebags") || !W221OwnHorse) return;
        if (key != 0) _w221PeerKey[src] = key;
        if (!_w221PeerKey.TryGetValue(src, out uint k) || Wo221HorseRecord() is not { } rec) return;
        string? mine;
        lock (_w221HorseGate) mine = rec.HorseOf(k);
        if (kind == Protocol.QuestAskHorseBags)
        {
            if (!Wo221BagRules.IsBags(text) || mine is null || text.Split(' ')[0] != mine)
            {
                if (Interlocked.Increment(ref _w221BagRefused) <= 20) Console.WriteLine($"MP-W221 bags: a list from ghost {src} refused (not his recorded horse, or malformed)");
                return;
            }
            _w221BagListAtMs[src] = Environment.TickCount64;
            Interlocked.Increment(ref _w221BagLists);
            var f = text.Split(' ');
            _ = ExecLuaAsync($"if KCD2MP_W221HostBags then KCD2MP_W221HostBags({src}, \"{f[0]}\", {f[1]}, {f[2]}, {f[3]}, \"{f[4]}\") end");
        }
        else if (kind == Protocol.QuestAskBagAnswer && Wo221BagRules.IsAnswer(text))
        {
            Interlocked.Increment(ref _w221BagAnswers);
            var f = text.Split(' ');
            _ = ExecLuaAsync($"if KCD2MP_W221HostBagAnswer then KCD2MP_W221HostBagAnswer({src}, \"{f[0]}\", \"{f[1]}\") end");
        }
    }

    /// <summary>Joiner: the host's list at a join (kind 12) or the partner's take (kind 13).</summary>
    private void Wo221OnBagText(byte kind, string? text)
    {
        if (!W137Joiner || !W221On("saddlebags") || !W221OwnHorse) return;
        if (kind == Protocol.CrimeHostBagBase && Wo221BagRules.IsBase(text))
        {
            Interlocked.Increment(ref _w221BagBases);
            var f = text!.Split(' ');
            _ = ExecLuaAsync($"if KCD2MP_W221BagBase then KCD2MP_W221BagBase(\"{f[0]}\", {f[1]}, {f[2]}, \"{f[3]}\") end");
        }
        else if (kind == Protocol.CrimeHostBagTake && Wo221BagRules.IsTake(text))
        {
            Interlocked.Increment(ref _w221BagTakes);
            var f = text!.Split(' ');
            _ = ExecLuaAsync($"if KCD2MP_W221BagTake then KCD2MP_W221BagTake(\"{f[0]}\", \"{f[1]}\", \"{f[2]}\", {f[3]}) end");
        }
    }

    /// <summary>Host, every 10 s: a joiner whose record names a horse and whose list has not come yet (he has just joined) is sent what this
    /// world's horse carries -- his copy starts from it. A horse just bought needs none: its owner's list comes first.</summary>
    private void Wo221BagBaseTick()
    {
        if (!W137Host || !W221On("saddlebags") || !W221OwnHorse || Wo221HorseRecord() is not { } rec) return;
        long now = Environment.TickCount64;
        var peers = Wo134Peers().ToList();
        foreach (byte g in peers)
        {
            if (!_w221PeerKey.TryGetValue(g, out uint k)) continue;
            string? horse;
            lock (_w221HorseGate) horse = rec.HorseOf(k);
            if (horse is null) continue;
            if (_w221BagListAtMs.TryGetValue(g, out long at) && now - at < 120_000) continue;
            _ = ExecLuaAsync($"if KCD2MP_W221HostBagBase then KCD2MP_W221HostBagBase({g}, \"{horse}\") end");
        }
        foreach (byte g in _w221BagListAtMs.Keys.Where(p => !peers.Contains(p)).ToList()) _w221BagListAtMs.TryRemove(g, out _);
    }

    private string Wo221BagStats() => $" bags: lists={_w221BagLists} bases={_w221BagBases} takes={_w221BagTakes} answers={_w221BagAnswers} refused={_w221BagRefused}";

    // ---------------------------------------------------------------- F: the route lines only for who reads them (mp_route_quiet)

    private readonly ConcurrentDictionary<byte, (bool On, long AtMs)> _w221RouteUse = new();   // host: each joiner's last word
    private long _w221RouteQuietTicks, _w221RouteUseIn, _w221RouteUseOut;

    /// <summary>Joiner: says whether anything here reads the host's route lines (the replay, WO-215's mp_route).</summary>
    private void Wo221SayRouteUse()
    {
        if (!W221On("route_quiet")) return;
        Interlocked.Increment(ref _w221RouteUseOut);
        _ = Wo137SendAsync(Protocol.QuestAskUp, Protocol.JoinTargetHost, Protocol.QuestAskRouteUse, 0, W215On("route") ? "1" : "0");
    }

    private void Wo221OnRouteUse(byte src, bool on)
    {
        if (!W137Host) return;
        Interlocked.Increment(ref _w221RouteUseIn);
        bool had = _w221RouteUse.TryGetValue(src, out var was);
        _w221RouteUse[src] = (on, Environment.TickCount64);
        if (!had || was.On != on) Console.WriteLine($"MP-W221 route: ghost {src} {(on ? "reads" : "does not read")} the route lines");
    }

    /// <summary>Host: the joiners the route lines go to. With the switch off, or a joiner that has not said (an older build says nothing),
    /// everybody as before; a joiner whose last word (within 45 s) was "no" gets none; with nobody left the host's read is not made.</summary>
    private IReadOnlyList<byte> Wo221RouteReaders(IReadOnlyList<byte> peers)
    {
        if (!W221On("route_quiet") || peers.Count == 0) return peers;
        long now = Environment.TickCount64;
        var keep = Wo221Rules.RouteReaders(peers, g => _w221RouteUse.TryGetValue(g, out var u) && now - u.AtMs < 45_000 ? u.On : null);
        if (keep.Count == 0) Interlocked.Increment(ref _w221RouteQuietTicks);
        if (_w221RouteUse.Count > peers.Count)
            foreach (byte g in _w221RouteUse.Keys.Where(p => !peers.Contains(p)).ToList()) _w221RouteUse.TryRemove(g, out _);
        return keep;
    }

    private string Wo221RouteStats() => $" route_quiet: said={_w221RouteUseOut} heard={_w221RouteUseIn} quiet_ticks={_w221RouteQuietTicks}";
}
