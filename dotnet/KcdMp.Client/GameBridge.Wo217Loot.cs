// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-217 C / D (docs/WO-217-findings.md): a body's loot live for both, the item's owner and stolen mark. The mod keeps the sessions; this
// agent carries them on WO-134's two loot messages (new kinds, append-only) and drives the mod's poll ten times a second while a session
// lives on this machine (the game's own timers stop while a loot screen is open).
//   joiner -> host  LootAsk 6 TakeReq "<body> <item> <cls> <amt>"        LootAsk 7 TakeAck "<body> <applied|cancel>"   LootAsk 8 End "<body>"
//   host -> joiner  LootHost 8 BodyList "<body> <rev> <part> <nparts> <items|->"   items = item:cls:amt:hp:worn:stolen:owner,...
//                   LootHost 9 TakeVerdict "<grant|refuse> <body> <item> <owner|-|gone|busy> <stolen>"
public partial class GameBridge
{
    private long _w217LootPolls, _w217LootLists, _w217LootReqs, _w217LootVerdicts, _w217LootDropped;
    private volatile int _w217LootLive;

    private void Wo217LootTick(long n)
    {
        // E.1: the host's corpse census every 2 s; the joiner asks again for each corpse not yet shown
        if (n % 8 == 3 && W137Host && W217On("encounter_world")) _ = ExecLuaAsync("if KCD2MP_W217CorpseCensus then KCD2MP_W217CorpseCensus() end; if KCD2MP_W217PropCensus then KCD2MP_W217PropCensus() end");
        if (n % 8 == 7 && Wo134JoinerRole && W217On("encounter_world")) _ = Wo217CorpseRetryAsync();
        // F: the host's carts 4 times a second; the joiner measures them twice a second
        if (W137Host && W217On("carts")) _ = ExecLuaAsync("if KCD2MP_W217CartCensus then KCD2MP_W217CartCensus() end");
        if (n % 2 == 1 && Wo134JoinerRole && W217On("carts")) _ = Wo217CartMeasureAsync();
        // 4 Hz session tick: the 10 Hz poll runs on its own loop while a session lives; here, a safety poll every second
        if (_w217LootLive > 0 && n % 4 == 1 && W217On("loot_live")) _ = ExecLuaAsync("if KCD2MP_W217LootPoll then KCD2MP_W217LootPoll() end");
    }

    private CancellationTokenSource? _w217PollCts;
    private void Wo217LootLiveChanged(int n)
    {
        _w217LootLive = n;
        if (n > 0 && _w217PollCts is null)
        {
            var cts = new CancellationTokenSource();
            _w217PollCts = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested && _w217LootLive > 0)
                    {
                        await Task.Delay(100, cts.Token);
                        Interlocked.Increment(ref _w217LootPolls);
                        await ExecLuaAsync("if KCD2MP_W217LootPoll then KCD2MP_W217LootPoll() end");
                    }
                }
                catch (OperationCanceledException) { }
                finally { if (ReferenceEquals(_w217PollCts, cts)) _w217PollCts = null; }
            });
        }
        else if (n == 0) { _w217PollCts?.Cancel(); _w217PollCts = null; }
    }

    /// <summary>The mod's WO-217 loot events (the router hands every w217_ event not WO-217's own switches here).</summary>
    private bool Wo217LootEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ');
        switch (name)
        {
            case "w217_lootlive":
                if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) Wo217LootLiveChanged(n);
                return true;
            case "w217_blist":   // host: <peers> <body> <rev> <part> <nparts> <items|->
                if (f.Length == 6 && Wo134Rules.BodyName.IsMatch(f[1]))
                {
                    Interlocked.Increment(ref _w217LootLists);
                    foreach (var ps in f[0].Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (byte.TryParse(ps, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte peer))
                            _ = Wo134SendAsync(Protocol.LootHostUp, peer, Protocol.LootHostBodyList, 0, $"{f[1]} {f[2]} {f[3]} {f[4]} {f[5]}");
                }
                return true;
            case "w217_tres":    // host: <peer> <tok> <grant|refuse> <body> <item> <owner|why> <stolen>
                if (f.Length == 7 && byte.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte tp) && uint.TryParse(f[1], out uint ttok))
                    _ = Wo134SendAsync(Protocol.LootHostUp, tp, Protocol.LootHostTakeVerdict, ttok, $"{f[2]} {f[3]} {f[4]} {f[5]} {f[6]}");
                return true;
            case "w217_treq":    // joiner: <tok> <body> <item> <cls> <amt>
                if (f.Length == 5 && uint.TryParse(f[0], out uint rtok))
                {
                    Interlocked.Increment(ref _w217LootReqs);
                    _ = Wo134SendAsync(Protocol.LootAskUp, Protocol.JoinTargetHost, Protocol.LootAskTakeReq, rtok, $"{f[1]} {f[2]} {f[3]} {f[4]}");
                }
                return true;
            case "w217_tack":    // joiner: <tok> <body> <applied|cancel>
                if (f.Length == 3 && uint.TryParse(f[0], out uint atok))
                    _ = Wo134SendAsync(Protocol.LootAskUp, Protocol.JoinTargetHost, Protocol.LootAskTakeAck, atok, $"{f[1]} {f[2]}");
                return true;
            case "w217_corpses": // host: <name:x:y:z:yaw,...|->  (E.1)
                if (arg is { Length: > 0 } && arg.Length <= Protocol.LootTextMax)
                    foreach (byte j in Wo134Peers()) _ = Wo134SendAsync(Protocol.LootHostUp, j, Protocol.LootHostCorpses, 0, arg);
                return true;
            case "w217_corpse":  // joiner: <name> <spawned|waiting|alive|shown>  (E.1)
            {
                var sp = (arg ?? "").Split(' ');
                if (sp.Length == 2 && Wo134Rules.BodyName.IsMatch(sp[0])) _ = Wo217OnCorpseStateAsync(sp[0], sp[1]);
                return true;
            }
            case "w217_prop":     // host: <name|class|arch|model|x|y|z|ax|ay|az>  (E.2)
                if (arg is { Length: > 0 } && arg.Length <= Protocol.LootTextMax && Wo217PropRules.Parse(arg) is not null)
                    foreach (byte j in Wo134Peers()) _ = Wo134SendAsync(Protocol.LootHostUp, j, Protocol.LootHostProp, 0, arg);
                return true;
            case "w217_propgone": // host: <name>
                if (arg is { Length: > 0 } && Wo217PropRules.Name.IsMatch(arg))
                    foreach (byte j in Wo134Peers()) _ = Wo134SendAsync(Protocol.LootHostUp, j, Protocol.LootHostPropGone, 0, arg);
                return true;
            case "w217_carts":   // host: <name|x|y|z|ax|ay|az|riders;...|->  (F)
                if (arg is { Length: > 0 })
                {
                    string text = (Environment.TickCount64 & 0xFFFFFFFF).ToString(CultureInfo.InvariantCulture) + " " + arg;
                    if (text.Length > Protocol.LootTextMax) text = Wo217CartRules.Trim(text, Protocol.LootTextMax);
                    foreach (byte j in Wo134Peers()) _ = Wo134SendAsync(Protocol.LootHostUp, j, Protocol.LootHostCarts, 0, text);
                }
                return true;
            case "w217_lootend": // joiner: <body>
                if (Wo134Rules.BodyName.IsMatch(arg ?? ""))
                    _ = Wo134SendAsync(Protocol.LootAskUp, Protocol.JoinTargetHost, Protocol.LootAskLiveEnd, 0, arg!);
                return true;
        }
        return false;
    }

    /// <summary>WO-134's frame handler hands the WO-217 kinds here first; true when it was one.</summary>
    private async Task<bool> Wo217OnLootFrameAsync(int type, byte src, LootMsg m)
    {
        var f = m.Text.Split(' ');
        if (type == Protocol.LootAskDown)
        {
            switch (m.Kind)
            {
                case Protocol.LootAskTakeReq when f.Length == 4 && Wo134Rules.BodyName.IsMatch(f[0]) && Wo217LootRules.ItemId.IsMatch(f[1])
                        && Wo134Rules.TryClass(f[2], out var cls) && Wo134Rules.TryAmount(f[3], out int amt):
                    if (!Wo134HostRole) { Interlocked.Increment(ref _w217LootDropped); return true; }
                    await ExecLuaAsync($"if KCD2MP_W217HostTakeReq then KCD2MP_W217HostTakeReq({src}, \"{m.Tok}\", \"{f[0]}\", \"{f[1]}\", \"{cls:D}\", {amt}) end");
                    return true;
                case Protocol.LootAskTakeAck when f.Length == 2 && Wo134Rules.BodyName.IsMatch(f[0]) && f[1] is "applied" or "cancel":
                    if (!Wo134HostRole) { Interlocked.Increment(ref _w217LootDropped); return true; }
                    await ExecLuaAsync($"if KCD2MP_W217HostAck then KCD2MP_W217HostAck({src}, \"{m.Tok}\", \"{f[1]}\") end");
                    return true;
                case Protocol.LootAskLiveEnd when f.Length == 1 && Wo134Rules.BodyName.IsMatch(f[0]):
                    if (!Wo134HostRole) return true;
                    await ExecLuaAsync($"if KCD2MP_W217HostEnd then KCD2MP_W217HostEnd({src}, \"{f[0]}\") end");
                    return true;
                case Protocol.LootAskTakeReq or Protocol.LootAskTakeAck or Protocol.LootAskLiveEnd:
                    Interlocked.Increment(ref _w217LootDropped);
                    Console.WriteLine($"MP-W217 loot: a malformed {Protocol.LootAskName(m.Kind)} from ghost {src} -- dropped");
                    return true;
            }
            return false;
        }
        switch (m.Kind)
        {
            case Protocol.LootHostBodyList when f.Length == 5 && Wo134Rules.BodyName.IsMatch(f[0])
                    && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rev)
                    && int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int part)
                    && int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int np) && part >= 1 && np >= 1 && part <= np && np <= 40
                    && Wo217LootRules.ParseItems(f[4]) is { } items:
                if (!Wo134JoinerRole) { Interlocked.Increment(ref _w217LootDropped); return true; }
                await ExecLuaAsync($"if KCD2MP_W217BodyList then KCD2MP_W217BodyList(\"{f[0]}\", {rev}, {part}, {np}, {Wo217LootRules.LuaItems(items)}) end");
                return true;
            case Protocol.LootHostTakeVerdict when f.Length == 5 && f[0] is "grant" or "refuse" && Wo134Rules.BodyName.IsMatch(f[1])
                    && Wo217LootRules.ItemId.IsMatch(f[2]) && Wo217LootRules.Owner.IsMatch(f[3]) && f[4] is "0" or "1":
                if (!Wo134JoinerRole) { Interlocked.Increment(ref _w217LootDropped); return true; }
                Interlocked.Increment(ref _w217LootVerdicts);
                await ExecLuaAsync($"if KCD2MP_W217TakeResult then KCD2MP_W217TakeResult(\"{m.Tok}\", \"{f[0]}\", \"{f[1]}\", \"{f[2]}\", \"{f[3]}\", {f[4]}) end");
                return true;
            case Protocol.LootHostCorpses when Wo217LootRules.ParseCorpses(m.Text) is { } corpses:
                if (!Wo134JoinerRole) { Interlocked.Increment(ref _w217LootDropped); return true; }
                _ = Wo217OnCorpsesAsync(src, corpses);
                return true;
            case Protocol.LootHostProp when Wo217PropRules.Parse(m.Text) is { } pr:
                if (!Wo134JoinerRole || !W217On("encounter_world")) { Interlocked.Increment(ref _w217LootDropped); return true; }
                Interlocked.Increment(ref _w217PropsIn);
                await ExecLuaAsync(string.Create(CultureInfo.InvariantCulture,
                    $"if KCD2MP_W217Prop then KCD2MP_W217Prop(\"{pr.Name}\", \"{pr.Class}\", \"{pr.Arch}\", \"{pr.Model}\", {pr.X:0.##}, {pr.Y:0.##}, {pr.Z:0.##}, {pr.Ax:0.###}, {pr.Ay:0.###}, {pr.Az:0.###}) end"));
                return true;
            case Protocol.LootHostPropGone when Wo217PropRules.Name.IsMatch(m.Text):
                if (!Wo134JoinerRole) { Interlocked.Increment(ref _w217LootDropped); return true; }
                await ExecLuaAsync($"if KCD2MP_W217PropGone then KCD2MP_W217PropGone(\"{m.Text}\") end");
                return true;
            case Protocol.LootHostCarts when Wo217CartRules.Parse(m.Text) is { } carts:
                if (!Wo134JoinerRole) { Interlocked.Increment(ref _w217LootDropped); return true; }
                await Wo217OnCartsAsync(carts.HostMs, carts.Rows);
                return true;
            case Protocol.LootHostBodyList or Protocol.LootHostTakeVerdict or Protocol.LootHostCorpses or Protocol.LootHostCarts or Protocol.LootHostProp or Protocol.LootHostPropGone:
                Interlocked.Increment(ref _w217LootDropped);
                Console.WriteLine($"MP-W217 loot: a malformed {Protocol.LootHostName(m.Kind)} from the host -- dropped ({(m.Text.Length > 60 ? m.Text[..60] : m.Text)})");
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- E.1: the host's corpses, applied here once each
    // the host's last list (latest wins), each body's state here, the deaths applied (at most one per body each 20 s)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Wo217LootRules.Corpse> _w217CorpseWant = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string State, int Tries)> _w217CorpseState = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _w217CorpseAt = new(StringComparer.Ordinal);
    private long _w217CorpsesIn, _w217CorpseApplied, _w217CorpseShown, _w217CorpseMade;
    private byte _w217CorpseSrc;

    private Task Wo217CorpseLuaAsync(Wo217LootRules.Corpse c) => ExecLuaAsync(string.Create(CultureInfo.InvariantCulture,
        $"if KCD2MP_W217Corpse then KCD2MP_W217Corpse(\"{c.Name}\", {c.X:0.0}, {c.Y:0.0}, {c.Z:0.0}, {c.Yaw:0.00}) end"));

    private async Task Wo217OnCorpsesAsync(byte src, List<Wo217LootRules.Corpse> corpses)
    {
        if (!W217On("encounter_world")) return;
        Interlocked.Increment(ref _w217CorpsesIn);
        _w217CorpseSrc = src;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in corpses)
        {
            names.Add(c.Name);
            bool fresh = !_w217CorpseWant.ContainsKey(c.Name);
            _w217CorpseWant[c.Name] = c;
            if (fresh) _w217CorpseState[c.Name] = ("asked", 0);
            if (!_w217CorpseState.TryGetValue(c.Name, out var st) || st.State != "shown") await Wo217CorpseLuaAsync(c);
        }
        foreach (var k in _w217CorpseWant.Keys) if (!names.Contains(k)) { _w217CorpseWant.TryRemove(k, out _); _w217CorpseState.TryRemove(k, out _); }
    }

    // the Lua's answer for one body: made or alive here -> its death applied (the owner-death apply WO-86 built), then asked again
    private async Task Wo217OnCorpseStateAsync(string name, string state)
    {
        if (!_w217CorpseWant.TryGetValue(name, out var c)) return;
        int tries = _w217CorpseState.TryGetValue(name, out var st) ? st.Tries : 0;
        _w217CorpseState[name] = (state, tries);
        if (state == "shown") { if (st.State != "shown") Interlocked.Increment(ref _w217CorpseShown); return; }
        if (state == "spawned") Interlocked.Increment(ref _w217CorpseMade);
        if (state is "spawned" or "alive")
        {
            long now = Environment.TickCount64;
            if (_w217CorpseAt.TryGetValue(name, out long at) && now - at < 20_000) return;
            _w217CorpseAt[name] = now;
            Interlocked.Increment(ref _w217CorpseApplied);
            try { await ApplyRemoteNpcDeathAsync(name, null, _w217CorpseSrc, "w217-corpse", CancellationToken.None, bypassDedupe: state == "spawned"); }
            catch (Exception ex) { Console.WriteLine($"MP-W217 corpse {name}: the death not applied ({ex.Message})"); }
            await Wo217CorpseLuaAsync(c);
        }
    }

    // every 2 s: each listed corpse not yet shown here is asked again (15 tries, then left: named in the stats)
    private async Task Wo217CorpseRetryAsync()
    {
        foreach (var (name, c) in _w217CorpseWant)
        {
            if (!_w217CorpseState.TryGetValue(name, out var st) || st.State == "shown" || st.Tries >= 15) continue;
            _w217CorpseState[name] = (st.State, st.Tries + 1);
            await Wo217CorpseLuaAsync(c);
        }
    }

    // ---------------------------------------------------------------- F: the host's carts, played back by the DLL here
    private volatile List<Wo217CartRules.Cart> _w217Carts = [];
    private long _w217PropsIn;
    private long _w217CartBatches, _w217CartOpFails;

    private async Task Wo217OnCartsAsync(uint hostMs, List<Wo217CartRules.Cart> rows)
    {
        if (!W217On("carts")) return;
        _w217Carts = rows;
        Interlocked.Increment(ref _w217CartBatches);
        var r = await _combat.Wo163Async(Protocol.Wo217OpCarts, Wo217CartRules.OpBody(hostMs, rows));
        if (r is not { Ok: true } && Interlocked.Increment(ref _w217CartOpFails) <= 3) Console.WriteLine("MP-W217 the DLL did not take op 172 (an older KCDMP.dll?)");
    }

    private async Task Wo217CartMeasureAsync()
    {
        foreach (var c in _w217Carts)
            await ExecLuaAsync(string.Create(CultureInfo.InvariantCulture,
                $"if KCD2MP_W217CartMeasure then KCD2MP_W217CartMeasure(\"{c.Name}\", {c.X:0.###}, {c.Y:0.###}, {c.Z:0.###}, \"{c.Riders}\") end"));
    }

    private string Wo217LootStats() =>
        $" loot_polls={Interlocked.Read(ref _w217LootPolls)} loot_lists_out={Interlocked.Read(ref _w217LootLists)} loot_reqs_out={Interlocked.Read(ref _w217LootReqs)}"
        + $" loot_verdicts_in={Interlocked.Read(ref _w217LootVerdicts)} loot_dropped={Interlocked.Read(ref _w217LootDropped)} loot_live_now={_w217LootLive}"
        + $" corpse_lists_in={Interlocked.Read(ref _w217CorpsesIn)} corpse_deaths_applied={Interlocked.Read(ref _w217CorpseApplied)}"
        + $" corpses_made={Interlocked.Read(ref _w217CorpseMade)} corpses_shown={Interlocked.Read(ref _w217CorpseShown)}"
        + $" corpses_not_shown={_w217CorpseState.Count(kv => kv.Value.State != "shown")}"
        + $" cart_batches_in={Interlocked.Read(ref _w217CartBatches)} carts_now={_w217Carts.Count} cart_op_fails={Interlocked.Read(ref _w217CartOpFails)}"
        + $" props_in={Interlocked.Read(ref _w217PropsIn)}";
}

/// <summary>WO-217 C's wire checks (tests: KcdMp.Client.Tests).</summary>
public static class Wo217LootRules
{
    /// <summary>An item's id as the mod writes it (the game's WUID text: letters, digits, x, no spaces or colons).</summary>
    public static readonly Regex ItemId = new("^[0-9A-Za-z_]{1,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>An item's owner: an engine entity name, "-" for none, or a refusal's reason.</summary>
    public static readonly Regex Owner = new("^(-|[A-Za-z0-9_]{1,63})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public sealed record Item(string Id, Guid Cls, int Amt, float Hp, bool Worn, bool Stolen, string Owner);

    /// <summary>"item:cls:amt:hp:worn:stolen:owner,..." or "-" (an empty part). Null when any field is malformed.</summary>
    public static List<Item>? ParseItems(string s)
    {
        var o = new List<Item>();
        if (s == "-") return o;
        foreach (var row in s.Split(','))
        {
            var p = row.Split(':');
            if (p.Length != 7 || !ItemId.IsMatch(p[0]) || !Wo134Rules.TryClass(p[1], out var cls) || !Wo134Rules.TryAmount(p[2], out int amt)
                || !Wo134Rules.TryHealth(p[3], out float hp) || p[4] is not ("0" or "1") || p[5] is not ("0" or "1") || !Owner.IsMatch(p[6])) return null;
            o.Add(new Item(p[0], cls, amt, hp, p[4] == "1", p[5] == "1", p[6]));
            if (o.Count > 40) return null;
        }
        return o;
    }

    public sealed record Corpse(string Name, float X, float Y, float Z, float Yaw);

    /// <summary>E.1: "name:x:y:z:yaw,..." or "-". Null when any field is malformed.</summary>
    public static List<Corpse>? ParseCorpses(string s)
    {
        var o = new List<Corpse>();
        if (s == "-") return o;
        foreach (var row in s.Split(','))
        {
            var p = row.Split(':');
            if (p.Length != 5 || !Wo134Rules.BodyName.IsMatch(p[0]) || p[0].Length > 63) return null;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) || !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw)
                || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(yaw)) return null;
            o.Add(new Corpse(p[0], x, y, z, yaw));
            if (o.Count > 40) return null;
        }
        return o;
    }

    public static string LuaItems(List<Item> items) =>
        "{" + string.Join(",", items.Select(i => $"{{id=\"{i.Id}\",cls=\"{i.Cls:D}\",amt={i.Amt},hp={Wo134Rules.F(i.Hp)},worn={(i.Worn ? 1 : 0)},stolen={(i.Stolen ? 1 : 0)},owner=\"{i.Owner}\"}}")) + "}";
}

/// <summary>WO-217 F: the host's cart batch on the wire and op 172's body.</summary>
public static class Wo217CartRules
{
    public sealed record Cart(string Name, float X, float Y, float Z, float Ax, float Ay, float Az, string Riders);
    private static readonly Regex CartName = new(@"^[A-Za-z0-9_\[\]\-./:]{1,127}$", RegexOptions.Compiled);   // never a quote or a backslash (it goes into Lua)
    private static readonly Regex Riders = new(@"^(-|[A-Za-z0-9_]{1,63}(,[A-Za-z0-9_]{1,63}){0,5})$", RegexOptions.Compiled);

    /// <summary>"&lt;host_ms&gt; &lt;rows|-&gt;"; null when any field is malformed.</summary>
    public static (uint HostMs, List<Cart> Rows)? Parse(string text)
    {
        int sp = text.IndexOf(' ');
        if (sp <= 0 || !uint.TryParse(text[..sp], NumberStyles.None, CultureInfo.InvariantCulture, out uint ms)) return null;
        string body = text[(sp + 1)..];
        var o = new List<Cart>();
        if (body == "-") return (ms, o);
        foreach (var row in body.Split(';'))
        {
            var f = row.Split('|');
            if (f.Length != 8 || !CartName.IsMatch(f[0]) || f[0].Contains(',') || !Riders.IsMatch(f[7])) return null;
            var v = new float[6];
            for (int i = 0; i < 6; i++)
                if (!float.TryParse(f[1 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !float.IsFinite(v[i]) || Math.Abs(v[i]) > 1e6f) return null;
            o.Add(new Cart(f[0], v[0], v[1], v[2], v[3], v[4], v[5], f[7]));
            if (o.Count > 24) return null;
        }
        return (ms, o);
    }

    /// <summary>Whole rows only, under the frame's text limit.</summary>
    public static string Trim(string text, int max)
    {
        int cut = text.LastIndexOf(';', Math.Min(max, text.Length) - 1);
        return cut > 0 ? text[..cut] : text[..text.IndexOf(' ')] + " -";
    }

    /// <summary>Op 172's body after the op byte: [host_ms:4][n:1] n x ([len:1][name][x y z ax ay az]).</summary>
    public static byte[] OpBody(uint hostMs, List<Cart> rows)
    {
        var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(hostMs));
        var keep = rows.Where(r => r.Name.Length is > 0 and <= 127).Take(24).ToList();
        ms.WriteByte((byte)keep.Count);
        foreach (var r in keep)
        {
            var nb = System.Text.Encoding.ASCII.GetBytes(r.Name);
            ms.WriteByte((byte)nb.Length); ms.Write(nb);
            foreach (float f in new[] { r.X, r.Y, r.Z, r.Ax, r.Ay, r.Az }) ms.Write(BitConverter.GetBytes(f));
        }
        return ms.ToArray();
    }
}

/// <summary>WO-217 E.2: one prop of the host's encounter on the wire (every text field goes into a Lua string: letters, digits and
/// a few path characters only).</summary>
public static class Wo217PropRules
{
    public sealed record Prop(string Name, string Class, string Arch, string Model, float X, float Y, float Z, float Ax, float Ay, float Az);
    public static readonly Regex Name = new(@"^[A-Za-z0-9_\[\]\-./:]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex Class = new(@"^(BasicEntity|RigidBody|RigidBodyEx|GeomEntity|AnimObject|FireplaceSmartObject|CartStash|DeadBody_Horse|SO_DeadBody_Horse|DeadBody_Human)$", RegexOptions.Compiled);

    public static Prop? Parse(string row)
    {
        var f = row.Split('|');
        if (f.Length != 10 || !Name.IsMatch(f[0]) || !Class.IsMatch(f[1]) || !(f[2] == "-" || Name.IsMatch(f[2])) || !(f[3] == "-" || Name.IsMatch(f[3]))) return null;
        var v = new float[6];
        for (int i = 0; i < 6; i++)
            if (!float.TryParse(f[4 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !float.IsFinite(v[i]) || Math.Abs(v[i]) > 1e6f) return null;
        return new Prop(f[0], f[1], f[2], f[3], v[0], v[1], v[2], v[3], v[4], v[5]);
    }
}
