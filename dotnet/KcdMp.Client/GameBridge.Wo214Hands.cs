// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-214 F (figure_hands, both roles): the partner's figure holds the weapon the partner holds.
//   The field: the joiner drew; the host's figure of him ran its draw ("CombatViz: ghost 1 drew weapon") and showed no weapon. The
//   figure's weapon came only from the outfit packets and Human.DrawWeapon(), which draws the figure's active weapon set -- and nothing
//   ever compared the figure's hand with the partner's. After an inventory screen the joiner's outfit packet listed one class of
//   thirteen and the host's converge took the rest off the figure.
//   Now each player's own main-hand item class rides to the partner at every draw, sheathe and change (and every 10 s while drawn), on
//   the WO-203 side channel (W203FigureHand, "v1|drawn|class"). The partner's figure is made to hold that class (the mod's
//   KCD2MP_W214FigureHand: the game's own equip into its weapon set, then the draw; DrawFromInventory last), and the converge never
//   takes off the class its partner holds drawn.
// Log: WO214-HAND own ... -> sent (the sender), WO214-HAND figure kcd2mp_<id> ... (the receiver; the mod's own line says how).
public partial class GameBridge
{
    private readonly ConcurrentDictionary<byte, (bool Drawn, Guid? Cls, long AtMs)> _w214FigureHand = new();
    private readonly ConcurrentDictionary<byte, Guid> _w214LastHeld = new();   // the weapon each partner last held drawn
    private long _w214HandSent, _w214HandIn, _w214HandOff, _w214HandOk, _w214HandFixed, _w214HandFailed, _w214HandKept, _w214PreDraw;

    /// <summary>This player's own hand (the mod's "w214_hand" event, "&lt;0|1&gt; &lt;class|-&gt;"): to the partner(s).</summary>
    private void Wo214OnOwnHand(string? arg)
    {
        if (!W214On("figure_hands")) return;
        if (!Wo214Rules.TryParseOwnHand(arg, out bool drawn, out Guid? cls)) { Console.WriteLine($"MP-W214 hand refused: '{arg}'"); return; }
        var to = W137Host ? Wo134Peers() : W137Joiner ? [Protocol.JoinTargetHost] : new List<byte>();
        string text = Wo214Rules.HandText(drawn, cls);
        foreach (byte t in to) _ = Wo203SendSideAsync(t, Protocol.W203FigureHand, text);
        long k = Interlocked.Increment(ref _w214HandSent);
        if (k <= 200 || k % 50 == 0)
            Console.WriteLine($"WO214-HAND own drawn={(drawn ? 1 : 0)} cls={(cls is Guid g ? g.ToString("D") : "-")} -> sent to {to.Count} [n={k}]");
    }

    /// <summary>The partner's hand (a W203 FigureHand row from <paramref name="src"/>): his figure here is made to hold it.</summary>
    private void Wo214OnFigureHand(byte src, LootMsg m)
    {
        if (!Wo214Rules.TryParseHand(m.Text, out bool drawn, out Guid? cls)) { Interlocked.Increment(ref _w203SideMalformed); return; }
        Interlocked.Increment(ref _w214HandIn);
        if (!W214On("figure_hands")) { Interlocked.Increment(ref _w214HandOff); return; }
        _w214FigureHand[src] = (drawn, cls, Environment.TickCount64);
        if (drawn && cls is Guid held) _w214LastHeld[src] = held;
        string c = cls is Guid g ? g.ToString("D") : "-";
        bool oversized = false;
        if (drawn && cls is Guid gc && _swingCatalog.IsCompletedSuccessfully && _swingCatalog.Result is { } cat) oversized = cat.OversizedItemOf([gc]) is not null;
        Console.WriteLine($"WO214-HAND figure kcd2mp_{src} told drawn={(drawn ? 1 : 0)} cls={c}{(oversized ? " (oversized)" : "")}");
        _ = ExecLuaAsync($"if KCD2MP_W214FigureHand then KCD2MP_W214FigureHand(\"{src}\", {(drawn ? "true" : "false")}, \"{c}\", {(oversized ? "true" : "false")}) end");
    }

    /// <summary>The mod's verdict on one figure ("w214_figure_hand": "&lt;id&gt; &lt;ok|fixed|failed&gt; &lt;how&gt;").</summary>
    private void Wo214OnFigureHandResult(string? arg)
    {
        var p = (arg ?? "").Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2) return;
        long k = p[1] switch
        {
            "ok" => Interlocked.Increment(ref _w214HandOk),
            "fixed" => Interlocked.Increment(ref _w214HandFixed),
            _ => Interlocked.Increment(ref _w214HandFailed),
        };
        if (k <= 200 || k % 50 == 0) Console.WriteLine($"WO214-HAND figure kcd2mp_{p[0]} {p[1]} ({(p.Length > 2 ? p[2] : "-")}) [n={k}]");
    }

    /// <summary>F: the converge's target for figure <paramref name="ghostId"/> keeps the class its partner holds drawn.</summary>
    private HashSet<Guid> Wo214KeepHeld(byte ghostId, HashSet<Guid> target)
    {
        if (!W214On("figure_hands") || !_w214FigureHand.TryGetValue(ghostId, out var h)
            || Environment.TickCount64 - h.AtMs > Wo214Rules.HandFreshMs) return target;
        var kept = Wo214Rules.KeepHeld(target, true, h.Drawn, h.Cls);
        if (kept.Count > target.Count)
        {
            long k = Interlocked.Increment(ref _w214HandKept);
            if (k <= 50 || k % 50 == 0)
                Console.WriteLine($"WO214-HAND figure kcd2mp_{ghostId}: its outfit packet lacks the weapon in its partner's hand ({h.Cls:D}) -- kept on the figure [n={k}]");
        }
        return kept;
    }

    /// <summary>
    /// F, at the partner's draw (his combat event, before his figure's own draw): the weapon he last held drawn is still on his figure,
    /// or it is put back first -- so that draw shows it at once (the hand message's fix comes only after his hand filled: 1.8 s on the
    /// bench). The field: his figure lost its weapon to the converge long before his next draw.
    /// </summary>
    private async Task Wo214PreDrawAsync(byte ghostId, CancellationToken ct)
    {
        if (!W214On("figure_hands") || !_w214LastHeld.TryGetValue(ghostId, out var cls)) return;
        string soul = $"kcd2mp_{ghostId}";
        var actual = await ReadGhostSetAsync(soul, ct);
        if (actual is null || actual.Contains(cls)) return;
        try { await Wo144EquipOnGhostAsync(soul, cls, true, ct); }
        catch (Exception ex) { Console.WriteLine($"WO214-HAND figure {soul}: its partner's weapon {cls:D} was not put back before his draw ({ex.Message})"); return; }
        long k = Interlocked.Increment(ref _w214PreDraw);
        if (k <= 200 || k % 50 == 0)
            Console.WriteLine($"WO214-HAND figure {soul}: its partner's weapon {cls:D} was off the figure at his draw -- put back before the figure's draw [n={k}]");
    }

    private void Wo214HandsReset() { _w214FigureHand.Clear(); _w214LastHeld.Clear(); }

    // ---- the bench's fault (KCDMP_TEST_W214_FIGURE_STRIP=1; the host's agent only, never set by an installer) ----
    // The field's end state, made at will: the partner's figure without the weapon its partner draws. On the bench a figure whose outfit
    // packets are whole draws its weapon on today's path too (the d5 run: 10 of 10), so the field's failure needs its state: after the
    // joiner's inventory screen his outfit packet listed one class of thirteen and the converge took pieces off his figure; his next draw
    // showed nothing. Here the weapon his figure wears is taken off just before each of his draws (the REST unequip the converge uses).
    private static readonly bool W214FigureStrip = Environment.GetEnvironmentVariable("KCDMP_TEST_W214_FIGURE_STRIP") == "1";
    private long _w214Stripped;

    private async Task Wo214TestStripAsync(byte ghostId)
    {
        if (!W214FigureStrip || !W137Host) return;
        var catalog = _swingCatalog.IsCompletedSuccessfully ? _swingCatalog.Result : null;
        if (catalog is null || !_ghostAppearance.TryGetValue(ghostId, out var applied)) return;
        Guid? cls;
        lock (applied) cls = catalog.MainHandItemOf(applied.ToList());
        if (cls is not Guid c) return;
        try { await _transport.UnequipItemOnGhostAsync($"kcd2mp_{ghostId}", c); }
        catch (Exception ex) { Console.WriteLine($"WO214-TEST strip figure kcd2mp_{ghostId}: the unequip failed ({ex.Message})"); return; }
        long k = Interlocked.Increment(ref _w214Stripped);
        Console.WriteLine($"WO214-TEST strip figure kcd2mp_{ghostId}: its main-hand weapon {c:D} taken off just before its draw (the field's end state) [n={k}]");
    }

    private string Wo214HandsStats() =>
        $" hand_sent={Interlocked.Read(ref _w214HandSent)} hand_in={Interlocked.Read(ref _w214HandIn)} hand_off={Interlocked.Read(ref _w214HandOff)}"
        + $" figure_hand_ok={Interlocked.Read(ref _w214HandOk)} figure_hand_fixed={Interlocked.Read(ref _w214HandFixed)} figure_hand_failed={Interlocked.Read(ref _w214HandFailed)}"
        + $" figure_hand_kept={Interlocked.Read(ref _w214HandKept)} figure_predraw_put_back={Interlocked.Read(ref _w214PreDraw)}";
}
