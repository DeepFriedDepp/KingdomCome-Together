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

    /// <summary>The joiner: one part of the host's list; with the last part the copies here are made keepers of the same shops, and
    /// a copy this joiner made a keeper whose host NPC keeps the shop no longer is taken off again.</summary>
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
            var r = await _combat.Wo163Async(Protocol.Wo221OpStatus, []);
            if (r is { Ok: true } rr) Console.WriteLine($"MP-W221-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
        }
        catch { }
    }

    private Task Wo221TellModAsync() =>
        ExecLuaAsync($"if KCD2MP_W221Cfg then KCD2MP_W221Cfg(\"{string.Join(',', W221Switch.Names.Select(n => n + "=" + (W221On(n) ? "on" : "off")))}\") end");

    private string Wo221StatsLine() =>
        $"MP-W221-STATS {string.Join(' ', W221Switch.Names.Select(n => $"{n}={(W221On(n) ? "on" : "off")}"))}" +
        $" shops: keepers_here={_w221KeepersHere.Count} opened={_w221ShopOpened} already={_w221ShopAlready} no_body={_w221ShopNoBody} no_shop={_w221ShopNoShop} closed={_w221ShopClosed}" +
        $" talks: held={_w221TalkHeld} released={_w221TalkReleased} renewed={_w221TalkRenewed} talking_now={_w141Talking.Count}";

    /// <summary>The 4 Hz session tick: the native word every 2 s, the status and the stats every 30 s, the mod told every minute.</summary>
    private void Wo221Tick(long n)
    {
        if (n % 8 == 3) _ = Wo221PushNativeAsync();
        if (n % 120 == 41 && (W137Host || W137Joiner)) { _ = Wo221NativeStatusAsync(); Console.WriteLine(Wo221StatsLine()); }
        if (n % 240 == 53) _ = Wo221TellModAsync();
        if (n % 60 == 7 && W137Joiner && W221On("talk_hold") && !_w141Talking.IsEmpty) _ = Wo221TalkRenewAsync();   // B: every 15 s while a talk runs
        // B: the host's keepers and their shops, every 10 s (the mod scans around every player and answers with w221_keepers)
        if (n % 40 == 23 && W137Host && W221On("shop_keeper")) _ = ExecLuaAsync("if KCD2MP_W221HostKeepers then KCD2MP_W221HostKeepers() end");
    }

    private void Wo221OnConnect(CancellationToken ct) { _ = Wo221TellModAsync(); _ = Wo221PushNativeAsync(); }

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
            case "w221_status":
                _ = Wo221NativeStatusAsync();
                Console.WriteLine(Wo221StatsLine());
                return;
        }
    }
}
