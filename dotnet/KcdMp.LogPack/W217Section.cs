// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.LogPack;

/// <summary>
/// WO-217 H: the world around the bodies, one line per contract in plain words at the end of the Link section -- the activities placed on
/// the joiner's copies (by outcome and reason), the seated copies held by their seat, the loot ledger (this machine's half: balanced, or
/// the item that is not), the stolen marks, the corpses and props sent, the carts. Read from the native log (WO217-STATUS, WO217-*) and
/// the game's log (WO217-LEDGER, WO217-LOOT, WO217-CORPSE, WO217-PROP, WO217-CART).
/// </summary>
public static class W217Section
{
    private static readonly Regex Ledger = new(@"WO217-LEDGER side=(?<side>\S+) ev=(?<ev>\S+) body=\S+ item=(?<item>\S+)", RegexOptions.Compiled);
    private static readonly Regex Outcome = new(@" (?<k>placed|placed-then-lost|refused|deferred|stale|paced|held|superseded|not-tried)=(?<a>\d+)/(?<b>\d+)", RegexOptions.Compiled);
    private static readonly Regex Num = new(@"(?<k>[a-z_0-9]+)=(?<v>-?[\d.]+)", RegexOptions.Compiled);

    private static Dictionary<string, double> Fields(string? s)
    {
        var d = new Dictionary<string, double>(StringComparer.Ordinal);
        if (s is null) return d;
        foreach (Match m in Num.Matches(s))
            if (double.TryParse(m.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) d.TryAdd(m.Groups["k"].Value, v);
        return d;
    }
    private static double V(Dictionary<string, double> d, string k) => d.TryGetValue(k, out double v) ? v : 0;

    /// <summary>The loot ledger's balance on this machine: every request answered, every grant applied or cancelled (pure; tested).</summary>
    public static (int Requests, int Grants, int Applied, int Refused, int Cancelled, List<string> Open) Balance(IEnumerable<string> lines, string side)
    {
        var ev = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            var m = Ledger.Match(l);
            if (!m.Success || m.Groups["side"].Value != side) continue;
            string item = m.Groups["item"].Value;
            if (!ev.TryGetValue(item, out var c)) ev[item] = c = new Dictionary<string, int>(StringComparer.Ordinal);
            c[m.Groups["ev"].Value] = c.GetValueOrDefault(m.Groups["ev"].Value) + 1;
        }
        int req = 0, gr = 0, ap = 0, rf = 0, cn = 0;
        var open = new List<string>();
        foreach (var (item, c) in ev)
        {
            int r = c.GetValueOrDefault("request"), g = c.GetValueOrDefault("grant"), a = c.GetValueOrDefault("applied") + c.GetValueOrDefault("acked");
            int f = c.GetValueOrDefault("refused") + c.GetValueOrDefault("refuse"), x = c.GetValueOrDefault("cancel") + c.GetValueOrDefault("cancelled");
            req += r; gr += g; ap += a; rf += f; cn += x;
            // the joiner: a request is applied, refused or cancelled; the host: a grant to a joiner is acked or cancelled (its own takes apply at once)
            bool balanced = side == "joiner" ? r == a + f + x : g == a + x;
            if (!balanced && open.Count < 4) open.Add(item);
        }
        return (req, gr, ap, rf, cn, open);
    }

    public static string Lines(IReadOnlyList<string> native, IReadOnlyList<string> game)
    {
        var sb = new StringBuilder();
        sb.Append("- **the world around the bodies** (WO-217):\n");
        string? st = native.LastOrDefault(l => l.Contains("WO217-STATUS w217", StringComparison.Ordinal));
        if (st is null)
        {
            sb.Append("  - no WO-217 status in this log (an older build)\n");
            return sb.ToString();
        }
        var o = new List<string>();
        int tot = 0, placed = 0;
        foreach (Match m in Outcome.Matches(st))
        {
            int b = int.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture);
            tot += b;
            if (m.Groups["k"].Value is "placed") placed += b;
            if (b > 0) o.Add($"{m.Groups["k"].Value} {b}");
        }
        if (tot > 0)
            sb.Append($"  - activities placed on the copies: {placed} of {tot} attempts shown ({string.Join(", ", o)})\n");
        var seat = Fields(st[(st.IndexOf("0.2 ", StringComparison.Ordinal) is int si and >= 0 ? si : 0)..]);
        if (V(seat, "seat_holds") > 0 || V(seat, "seat_releases") > 0 || V(seat, "seat_pulls") > 0)
            sb.Append($"  - seated copies: held by their seat {V(seat, "seat_holds"):0} frames, released from a seat their host NPC left {V(seat, "seat_releases"):0} times, pulls over 50 cm {V(seat, "seat_pulls"):0}\n");
        string side = game.Any(l => l.Contains("WO217-LEDGER side=joiner", StringComparison.Ordinal)) ? "joiner" : "host";
        var bal = Balance(game, side);
        if (bal.Requests + bal.Grants > 0)
            sb.Append(side == "joiner"
                ? $"  - the loot ledger (this joiner): {bal.Requests} takes asked, {bal.Applied} granted and applied, {bal.Refused} already taken, {bal.Cancelled} cancelled -- {(bal.Open.Count == 0 ? "balanced" : "NOT balanced: " + string.Join(", ", bal.Open))}\n"
                : $"  - the loot ledger (the host): {bal.Grants} takes granted, {bal.Applied} applied, {bal.Cancelled} put back -- {(bal.Open.Count == 0 ? "balanced" : "NOT balanced: " + string.Join(", ", bal.Open))}\n");
        string? ls = game.LastOrDefault(l => l.Contains("WO217-LOOT status", StringComparison.Ordinal));
        if (ls is not null)
        {
            var f = Fields(ls);
            if (V(f, "flags") > 0) sb.Append($"  - stolen marks set on items here: {V(f, "flags"):0}\n");
        }
        int corpses = game.Count(l => l.Contains("WO217-CORPSE ", StringComparison.Ordinal));
        int props = game.Count(l => l.Contains("WO217-PROP ", StringComparison.Ordinal)) + native.Count(l => l.Contains("WO217-PROP ", StringComparison.Ordinal));
        int carts = game.Count(l => l.Contains("WO217-CART ", StringComparison.Ordinal)) + native.Count(l => l.Contains("WO217-CART ", StringComparison.Ordinal));
        if (corpses + props + carts > 0) sb.Append($"  - what encounters leave: {corpses} corpse lines, {props} prop lines, {carts} cart lines\n");
        return sb.ToString();
    }
}
