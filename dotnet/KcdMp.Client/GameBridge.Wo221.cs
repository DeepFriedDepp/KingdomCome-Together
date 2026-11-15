// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-221 (docs/WO-221-findings.md): bodies that walk right, and a horse each. Every switch ships on (W221Switch; `mp_<name> on|off` in
// the game's console, `mp_w221_status` lists them); the bench's twin is KCDMP_TEST_W221_OFF=<names|all> in both agents' and both games'
// environment. Log tags: MP-W221, MP-W221-NATIVE, MP-W221-STATS; the DLL's WO221-CFG, WO221-ACTEND.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w221On = new(StringComparer.Ordinal);
    private bool W221On(string name) => _w221On.TryGetValue(name, out bool v) ? v : !W221Switch.TwinOff(name);
    private int _w221Pushing;

    // ---- B: the keepers' shops ---------------------------------------------------------------------------------------------
    private string _w221KeepersSent = "";
    private long _w221KeepersSentAtMs;
    private readonly List<KeyValuePair<string, int>> _w221KeeperParts = new();
    private readonly ConcurrentDictionary<string, int> _w221KeepersHere = new(StringComparer.Ordinal);   // the copies this joiner asked its DLL to make keepers
    private int _w221KeepersBusy, _w221ShopOpened, _w221ShopAlready, _w221ShopNoBody, _w221ShopNoShop, _w221ShopClosed;

    /// <summary>The host: the mod's list of keepers near the players and the shop each keeps, sent to every joiner on a change and
    /// again every 30 s (a joiner that joined, reloaded, or whose copy was streamed in since).</summary>
    private void Wo221OnHostKeepers(string? arg)
    {
        if (!W137Host || !W221On("shop_keeper")) return;
        var pairs = Wo221Rules.ParseKeepers(arg);
        string csv = Wo221Rules.KeepersCsv(pairs);
        if (csv == _w221KeepersSent && Environment.TickCount64 - _w221KeepersSentAtMs < 30_000) return;
        if (csv != _w221KeepersSent) Console.WriteLine($"MP-W221 host: keepers and their shops near the players: {(pairs.Count == 0 ? "none" : csv)}");
        _w221KeepersSent = csv;
        _w221KeepersSentAtMs = Environment.TickCount64;
        foreach (byte g in Wo134Peers())
            foreach (var t in Wo221Rules.KeepersTexts(pairs)) _ = Wo139SendAsync(Protocol.CrimeHostUp, g, Protocol.CrimeHostKeepers, 0, t);
    }

    // ---------------------------------------------------------------- B: a copy in a conversation is asked for nothing

    private long _w221TalkHeld, _w221TalkReleased, _w221TalkRenewed;

    /// <summary>The joiner: this player's talk with a copy began (the ask) or ended -- the DLL holds its own lanes off that copy for the
    /// conversation (op 194). The word lapses there after 45 s: renewed from the tick while the talk runs.</summary>
    private void Wo221OnTalk(bool on, string npc)
    {
        if (!W137Joiner || (on && !W221On("talk_hold"))) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var r = await _combat.Wo163Async(Protocol.Wo221OpTalk, Wo221Rules.TalkBody(on, npc));
                byte code = r is { Ok: true } rr && rr.Payload.Length >= 1 ? rr.Payload[0] : (byte)0;
                if (code == 1) Interlocked.Increment(ref _w221TalkHeld); else if (code == 2) Interlocked.Increment(ref _w221TalkReleased);
                // the mod wakes the copy only now: its schedule is held before its brain runs again
                if (on) _ = ExecLuaAsync($"if KCD2MP_W221TalkHeld then KCD2MP_W221TalkHeld(\"{EscapeLua(npc)}\", \"{(code == 1 ? "held" : "not-held")}\") end");
                if (code is 1 or 2) Console.WriteLine($"MP-W221 joiner: {npc} {(on ? "is in a conversation here -- the mod asks nothing of it meanwhile" : "left its conversation -- the mod's lanes again")}");
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException) { }
        });
    }

    private async Task Wo221TalkRenewAsync()
    {
        foreach (var npc in _w141Talking.Keys.ToList())
        {
            try { await _combat.Wo163Async(Protocol.Wo221OpTalk, Wo221Rules.TalkBody(true, npc)); Interlocked.Increment(ref _w221TalkRenewed); }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException) { return; }
        }
    }

    /// <summary>The joiner: one part of the host's list; with the last part the copies here are made keepers of the same shops, and
    /// a copy this joiner made a keeper whose host NPC keeps the shop no longer is taken off again.</summary>
    private void Wo221OnKeepersText(string? text)
    {
        if (!Wo221Rules.TryParseKeepersPart(text, out int part, out int nparts, out var pairs)) { Console.WriteLine($"MP-W221 keepers refused: '{text}'"); return; }
        if (part == 1) _w221KeeperParts.Clear();
        _w221KeeperParts.AddRange(pairs);
        if (part != nparts || !W221On("shop_keeper") || !W137Joiner) return;
        var want = _w221KeeperParts.ToList();
        if (Interlocked.CompareExchange(ref _w221KeepersBusy, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var names = new HashSet<string>(want.Select(p => p.Key), StringComparer.Ordinal);
                foreach (var (name, id) in want)
                {
                    // asked again with every list (a reload of the world resets its shops; the DLL answers "a keeper already" at no cost)
                    var r = await _combat.Wo163Async(Protocol.Wo221OpShopOpen, Wo221Rules.OpenBody(id, name));
                    byte code = r is { Ok: true } rr && rr.Payload.Length >= 1 ? rr.Payload[0] : (byte)0;
                    switch (code)
                    {
                        case 1: case 6:
                            _w221KeepersHere[name] = id;
                            if (Interlocked.Increment(ref _w221ShopOpened) <= 200) Console.WriteLine($"MP-W221 joiner: {name} keeps shop {id} here now, as the host's keeper does");
                            break;
                        case 2: Interlocked.Increment(ref _w221ShopAlready); break;      // a keeper by the save: not ours to close
                        case 3: Interlocked.Increment(ref _w221ShopNoBody); break;       // not streamed in here: the next list tries again
                        case 4: Interlocked.Increment(ref _w221ShopNoShop); break;
                    }
                }
                foreach (var name in _w221KeepersHere.Keys.Where(n => !names.Contains(n)).ToList())
                {
                    await _combat.Wo163Async(Protocol.Wo221OpShopClose, Wo221Rules.CloseBody(name));
                    _w221KeepersHere.TryRemove(name, out _);
                    Interlocked.Increment(ref _w221ShopClosed);
                    Console.WriteLine($"MP-W221 joiner: {name} keeps its shop here no longer (the host's keeper does not)");
                }
            }
            catch (Exception ex) { Console.WriteLine($"MP-W221 keepers not applied: {ex.Message}"); }
            finally { Interlocked.Exchange(ref _w221KeepersBusy, 0); }
        });
    }

    /// <summary>The bench's body: the host's own NPC killed through the DLL (Lua cannot: this build's scripts have no kill). The mod's
    /// death observer then announces it as any death of the host's world. Never reachable without KCDMP_TEST_W221=1.</summary>
    private async Task Wo221TestKillAsync(string npc)
    {
        try
        {
            Guid? g = await ResolveLocalSoulGuidAsync(npc, CancellationToken.None);
            bool ok = g is Guid lg && await _combat.ApplyDeathAsync(lg);
            Console.WriteLine($"MP-W221 test kill {npc}: {(g is null ? "no soul answers to that name" : ok ? "dead" : "the death call failed")}");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W221 test kill {npc} threw: {ex.Message}"); }
    }

    private async Task Wo221PushNativeAsync()
    {
        if (Interlocked.CompareExchange(ref _w221Pushing, 1, 0) != 0) return;
        try
        {
            var body = new byte[5];
            BitConverter.GetBytes(W221Switch.NativeBits(W221On)).CopyTo(body, 0);
            body[4] = Wo218Role();
            await _combat.Wo163Async(Protocol.Wo221OpConfig, body);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W221 the native word was not sent: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _w221Pushing, 0); }
    }

    private async Task Wo221NativeStatusAsync()
    {
        try
        {
            // one line per area (op 191's pages: 0 = the switches, then each area until an empty answer)
            for (byte page = 0; page < 30; page++)
            {
                var r = await _combat.Wo163Async(Protocol.Wo221OpStatus, [page]);
                if (r is not { Ok: true } rr || rr.Payload.Length == 0) break;
                Console.WriteLine($"MP-W221-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
            }
        }
        catch { }
    }

    private Task Wo221TellModAsync() =>
        ExecLuaAsync($"if KCD2MP_W221Cfg then KCD2MP_W221Cfg(\"{string.Join(',', W221Switch.Names.Select(n => n + "=" + (W221On(n) ? "on" : "off")))}\") end");

    private string Wo221StatsLine() =>
        $"MP-W221-STATS {string.Join(' ', W221Switch.Names.Select(n => $"{n}={(W221On(n) ? "on" : "off")}"))}" +
        $" shops: keepers_here={_w221KeepersHere.Count} opened={_w221ShopOpened} already={_w221ShopAlready} no_body={_w221ShopNoBody} no_shop={_w221ShopNoShop} closed={_w221ShopClosed}" +
        $" talks: held={_w221TalkHeld} released={_w221TalkReleased} renewed={_w221TalkRenewed} talking_now={_w141Talking.Count} choice_signs={Interlocked.Read(ref _w221ChoiceSigns)}" + Wo221HorseStats() + Wo221BagStats() + Wo221RouteStats() + $" orders={_w221Orders} order_refused={_w221OrderRefused} corpse_looks: read={_w221LooksRead} in={_w221LooksIn} set={_w221LooksSet} ragdoll_waits={_w221RagdollWaits} ragdoll_spared={_w221RagdollSpared}";

    /// <summary>The 4 Hz session tick: the native word every 2 s, the status and the stats every 30 s, the mod told every minute.</summary>
    private void Wo221Tick(long n)
    {
        if (n % 8 == 3) _ = Wo221PushNativeAsync();
        if (n % 120 == 41 && (W137Host || W137Joiner)) { _ = Wo221NativeStatusAsync(); Console.WriteLine(Wo221StatsLine()); }
        if (n % 240 == 53) _ = Wo221TellModAsync();
        // D: the host's record to the joiners (1 Hz check, sent on a change and every 30 s); the host's own horse read every 10 s; a
        // joiner not yet told the record says its key every 10 s
        if (n % 4 == 2) Wo221HorseHostTick();
        if (n % 40 == 33) Wo221BagBaseTick();   // D.4
        if (n % 40 == 11 && W137Joiner) Wo221SayRouteUse();   // F: every 10 s
        if (n % 8 == 5 && W137Joiner && W221On("corpse_look")) _ = ExecLuaAsync("if KCD2MP_W221CorpseLay then KCD2MP_W221CorpseLay() end");   // E.1: every 2 s
        if (n % 40 == 29 && W137Host && W221OwnHorse) _ = ExecLuaAsync("if KCD2MP_W221HostOwnHorse then KCD2MP_W221HostOwnHorse() end");
        if (n % 40 == 17 && W137Joiner && W221OwnHorse && _w221MyHorse == "?") Wo221SendHorseWord("-");
        if (n % 60 == 7 && W137Joiner && W221On("talk_hold") && !_w141Talking.IsEmpty) _ = Wo221TalkRenewAsync();   // B: every 15 s while a talk runs
        // B: the host's keepers and their shops, every 10 s (the mod scans around every player and answers with w221_keepers)
        if (n % 40 == 23 && W137Host && W221On("shop_keeper")) _ = ExecLuaAsync("if KCD2MP_W221HostKeepers then KCD2MP_W221HostKeepers() end");
    }

    private long _w221Orders, _w221OrderRefused;
    private long _w221RagdollWaits, _w221RagdollSpared;   // mp_ragdoll_once: physics ragdolls that waited; bodies that lay down meanwhile

    // ---------------------------------------------------------------- E.1: a dead body's wounds (mp_corpse_look)

    private readonly ConcurrentDictionary<string, byte[]> _w221CorpseLooks = new(StringComparer.Ordinal);   // joiner: the host's word
    private string _w221LooksSent = "";
    private long _w221LooksSentAtMs, _w221LooksRead, _w221LooksSet, _w221LooksIn;
    private int _w221LooksBusy;

    /// <summary>Host: the corpse census changed (or its 30 s repeat): each body's wounds are read (op 197) and sent to the joiners.</summary>
    private void Wo221OnHostCorpses(string list)
    {
        if (!W137Host || !W221On("corpse_look") || Interlocked.CompareExchange(ref _w221LooksBusy, 1, 0) != 0) return;
        var names = list.Split(',').Select(e => e.Split(':')[0]).Where(n => n.Length is > 0 and <= 63 && Wo137Text.IsNpc(n)).Distinct().Take(40).ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                var looks = new List<KeyValuePair<string, byte[]>>();
                foreach (var n in names)
                {
                    var r = await _combat.Wo163Async(Protocol.Wo221OpBloodRead, Wo221Rules.CloseBody(n));
                    if (r is { Ok: true } rr && rr.Payload.Length >= 7 && rr.Payload[0] == 1) { looks.Add(new(n, rr.Payload[1..7])); Interlocked.Increment(ref _w221LooksRead); }
                }
                string csv = Wo221Rules.CorpseLooksCsv(looks);
                long now = Environment.TickCount64;
                if (csv == _w221LooksSent && now - _w221LooksSentAtMs < 30_000) return;
                if (csv != _w221LooksSent) Console.WriteLine($"MP-W221 corpses: the wounds of this world's dead bodies near the players: {csv}");
                _w221LooksSent = csv; _w221LooksSentAtMs = now;
                if (csv.Length <= Protocol.CrimeTextMax) foreach (byte g in Wo134Peers()) _ = Wo139SendAsync(Protocol.CrimeHostUp, g, Protocol.CrimeHostCorpseLooks, 0, csv);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException) { }
            finally { Interlocked.Exchange(ref _w221LooksBusy, 0); }
        });
    }

    /// <summary>Joiner: the host's list of wounds; each is set on the body here (op 198; a body not here yet gets it when it is shown).</summary>
    private void Wo221OnCorpseLooksText(string? text)
    {
        if (!W137Joiner || !W221On("corpse_look")) return;
        foreach (var (name, zones) in Wo221Rules.ParseCorpseLooks(text))
        {
            Interlocked.Increment(ref _w221LooksIn);
            _w221CorpseLooks[name] = zones;
            _ = Wo221SetLookAsync(name, zones);
        }
        if (_w221CorpseLooks.Count > 400) _w221CorpseLooks.Clear();
    }

    private void Wo221OnCorpseShown(string name)
    {
        if (W137Joiner && W221On("corpse_look") && _w221CorpseLooks.TryGetValue(name, out var zones)) _ = Wo221SetLookAsync(name, zones);
    }

    private async Task Wo221SetLookAsync(string name, byte[] zones)
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo221OpBloodSet, Wo221Rules.BloodSetBody(name, zones));
            if (r is { Ok: true } rr && rr.Payload.Length >= 2 && rr.Payload[0] == 1 && rr.Payload[1] > 0)
            {
                Interlocked.Increment(ref _w221LooksSet);
                Console.WriteLine($"MP-W221 corpses: {name} has the host body's wounds here now ({rr.Payload[1]} zone(s))");
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException) { }
    }
    private string _w221HostHorseClash = "";

    private async Task Wo221OrderAsync(byte op, byte[] body, string what)
    {
        try
        {
            var r = await _combat.Wo163Async(op, body);
            byte code = r is { Ok: true } rr && rr.Payload.Length >= 1 ? rr.Payload[0] : (byte)0;
            if (code == 1) Interlocked.Increment(ref _w221Orders);
            else if (Interlocked.Increment(ref _w221OrderRefused) <= 50)
                Console.WriteLine($"MP-W221 order {(op == Protocol.Wo221OpGo ? "go" : "stop")} '{what}' not carried out: {code switch { 2 => "no such body in this world", 3 => "the movement request is not armed on this build", 4 => "mp_own_horse is off", _ => "the DLL did not answer" }}");
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException) { }
    }

    private void Wo221OnConnect(CancellationToken ct) { _w221MyHorse = "?"; _ = Wo221TellModAsync(); _ = Wo221PushNativeAsync(); }

    private void Wo221OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w221_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off") || !W221Switch.Names.Contains(arg[..eq])) { Console.WriteLine($"MP-W221 cfg refused: '{arg}'"); return; }
                _w221On[arg[..eq]] = arg[(eq + 1)..] == "on";
                Console.WriteLine($"MP-W221 cfg {arg}");
                _ = Wo221PushNativeAsync();
                _ = Wo221TellModAsync();
                return;
            }
            case "w221_test_kill":   // the bench only (KCDMP_TEST_W221=1), the host: this world's NPC dies by the DLL's own death call
                if (Environment.GetEnvironmentVariable("KCDMP_TEST_W221") == "1" && W137Host && !string.IsNullOrWhiteSpace(arg) && NpcNamePattern.IsMatch(arg))
                    _ = Wo221TestKillAsync(arg);
                else Console.WriteLine($"MP-W221 test kill refused: '{arg}' (the bench's verb: the host, KCDMP_TEST_W221=1)");
                return;
            case "w221_keepers":
                Wo221OnHostKeepers(arg);
                return;
            case "w221_go":      // D.2 (host): "<name> <x> <y> <z> <gait>" -- a body of this world sent there by its own movement
                if (W137Host && Wo221Rules.GoBody(arg) is { } gb) _ = Wo221OrderAsync(Protocol.Wo221OpGo, gb, arg!);
                else Console.WriteLine($"MP-W221 order refused: '{arg}'");
                return;
            case "w221_stop":    // D.2 (host): "<name>" -- its request cancelled
                if (W137Host && arg is { Length: > 0 and <= 63 } sn && Wo137Text.IsNpc(sn)) _ = Wo221OrderAsync(Protocol.Wo221OpStop, Wo221Rules.CloseBody(sn), sn);
                return;
            case "w221_bags":      // D.4 (joiner): "<horse> <rev> <part> <parts> <items>" -- this player's horse inventory, to the host
                if (W137Joiner && Wo221BagRules.IsBags(arg)) _ = Wo137SendAsync(Protocol.QuestAskUp, Protocol.JoinTargetHost, Protocol.QuestAskHorseBags, Wo157InstallKey(), arg!);
                return;
            case "w221_bagans":    // D.4 (joiner): "<tok> grant|refuse|busy"
                if (W137Joiner && Wo221BagRules.IsAnswer(arg)) _ = Wo137SendAsync(Protocol.QuestAskUp, Protocol.JoinTargetHost, Protocol.QuestAskBagAnswer, Wo157InstallKey(), arg!);
                return;
            case "w221_bagtake":   // D.4 (host): "<peer> <tok> <horse> <class> <units>" -- the host's player took this from a joiner's horse
            case "w221_bagbase":   // D.4 (host): "<peer> <horse> <part> <parts> <items>" -- what this world's horse carries, to its joining owner
                if (W137Host && Wo221BagRules.SplitPeer(arg, out byte bp, out string rest) && (name == "w221_bagtake" ? Wo221BagRules.IsTake(rest) : Wo221BagRules.IsBase(rest)))
                    _ = Wo139SendAsync(Protocol.CrimeHostUp, bp, name == "w221_bagtake" ? Protocol.CrimeHostBagTake : Protocol.CrimeHostBagBase, 0, rest);
                return;
            case "w221_horseask":    // D (joiner): the mod has no word of the record (its session paused: a host's reload, a join)
                if (!W137Joiner || !W221OwnHorse) return;
                Interlocked.Increment(ref _w221HorseAsks);
                if (Wo221HorseAsk.Answer(_w221MyHorse) is { } ans) _ = ExecLuaAsync($"if KCD2MP_W221MyHorse then KCD2MP_W221MyHorse(\"{EscapeLua(ans)}\") end");
                else if (Environment.TickCount64 - _w221AskWordAtMs > 5_000) { _w221AskWordAtMs = Environment.TickCount64; Wo221SendHorseWord("-"); }
                return;
            case "w221_hosthorse":   // D (host): this game's own player's horse (never recorded for a joiner)
                _w221HostOwnHorse = arg is { Length: > 0 } hh && hh != "-" && Wo137Text.IsNpc(hh) ? hh : "";
                _w221HostOwnHorseKnown = true;
                // the host's own game took a horse the record names a joiner's (the trader's list does not know the record): said, never undone here
                if (_w221HostOwnHorse.Length > 0 && Wo221HorseRecord() is { } hr && hr.OwnerOf(_w221HostOwnHorse) is { } ho && _w221HostHorseClash != _w221HostOwnHorse)
                {
                    _w221HostHorseClash = _w221HostOwnHorse;
                    Console.WriteLine($"MP-W221 horse: this game's own player now has {_w221HostOwnHorse}, which the record names {ho.OwnerName}'s (bought from the same trader): two owners -- the record keeps its entry");
                }
                return;
            case "w221_status":
                _ = Wo221NativeStatusAsync();
                Console.WriteLine(Wo221StatsLine());
                return;
        }
    }
}
