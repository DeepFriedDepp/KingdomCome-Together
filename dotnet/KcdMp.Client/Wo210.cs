// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-210 H.3 (the bench's bisect of a lone host's memory, never set on a player's machine): KCDMP_TEST_PIPE_BLOCK="types=a-b,c|ops=x-y"
/// keeps the named pipe frame types (and, inside the WO-163 family's frame, the named op numbers) from ever reaching the DLL -- as if
/// that area were never configured or asked. Unset: nothing is blocked.
/// </summary>
public static class Wo210PipeBlock
{
    private static readonly Lazy<(HashSet<int> Types, HashSet<int> Ops)> Spec = new(() => Parse(Environment.GetEnvironmentVariable("KCDMP_TEST_PIPE_BLOCK")));

    public static bool Blocks(byte type, int op)
    {
        var (types, ops) = Spec.Value;
        return types.Contains(type) || (op >= 0 && ops.Contains(op));
    }

    public static (HashSet<int> Types, HashSet<int> Ops) Parse(string? spec)
    {
        var types = new HashSet<int>(); var ops = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(spec)) return (types, ops);
        foreach (var part in spec.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var set = part[..eq] switch { "types" => types, "ops" => ops, _ => null };
            if (set is null) continue;
            foreach (var r in part[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var ab = r.Split('-');
                if (Num(ab[0]) is not int a) continue;
                int b = ab.Length > 1 && Num(ab[1]) is int bb ? bb : a;
                for (int i = Math.Max(0, a); i <= Math.Min(255, b); i++) set.Add(i);
            }
        }
        return (types, ops);
    }

    private static int? Num(string s) => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? (int.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int h) ? h : null)
        : (int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int d) ? d : null);
}

/// <summary>WO-210 (docs/WO-210-findings.md): the agent's pure rules -- the switches, the DLL's switch word, the copy's posture release.</summary>
public static class Wo210Rules
{
    /// <summary>Every WO-210 switch the agent knows (the mod's mp_* commands send "name=on|off" as w210_cfg). New mechanisms ship on.</summary>
    public static readonly string[] SwitchNames = ["copy_state_always", "death_engine", "knockdown_sync", "copy_ready", "copy_hit_local_none",
                                                   "rider_sync", "state_one_request", "listen_wheel", "conv_hover", "conv_pick_hold", "conv_mark_over_icon",
                                                   "combo_hold", "profile_snapshot"];

    // ---------------------------------------------------------------- H.6: the joiner's game queue after a join

    /// <summary>
    /// H.6 (session E: the joiner's game queue 8-12 s behind, ~8 s after each of the three joins): the host's WO-189 catch-up sends every
    /// quest profile active in its world, one message each (88-89 per join), and the joiner asked its DLL about each one -- one pipe op
    /// a frame right after the load, the copies' state rows waiting behind them. 87-88 of each catch-up were already active here. A
    /// catch-up row ('=') whose profile this player's own snapshot lists needs no DLL op: the DLL's answer would be "already active".
    /// </summary>
    public static bool ProfileAlreadyActive(bool on, char op, string name, IReadOnlySet<string>? mine) =>
        on && op == '=' && mine is not null && mine.Contains(name);

    /// <summary>H.5: a mark id the game's script may carry in a string (the local mark, or W164Text.IsMarkId's [a-z0-9] 6..16).</summary>
    public static bool SafeMarkId(string? id) => id == "local" || W164Text.IsMarkId(id);

    /// <summary>H.6: one snapshot serves a catch-up (its rows come within a second or two); a live '+'/'-' row or a load drops it.</summary>
    public const long ProfileSnapshotMaxAgeMs = 20_000;

    // ---------------------------------------------------------------- F: the shared dialogue

    /// <summary>F.2a: at most 10 hover messages a second per talk (the newest row wins a coalesced send).</summary>
    public const long HoverMinGapMs = 100;
    /// <summary>F.2b: a line that comes while a decision is up waits this long for its pick; the pick shows this long before the next line.</summary>
    public const long PickWaitMs = 400, PickShowMs = 1000, LineHoldMaxMs = 1400;

    /// <summary>"Topic index 'N' activated." (the starter's cursor moved; the log tail keeps a trailing character) -> N (0..7), else -1. The
    /// haggle's negative indices are refused.</summary>
    public static int TopicActivated(string line)
    {
        const string head = "Topic index '", tail = "' activated.";
        var t = line.AsSpan().TrimEnd();
        while (t.Length > 0 && char.IsControl(t[^1])) t = t[..^1].TrimEnd();
        if (!t.StartsWith(head, StringComparison.Ordinal) || !t.EndsWith(tail, StringComparison.Ordinal)) return -1;
        return int.TryParse(t.Slice(head.Length, t.Length - head.Length - tail.Length), System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out int n) && n < 8 ? n : -1;
    }

    /// <summary>ConvHover's text: "v1|cid|dec|row|seq" (row -1 = none or unknown).</summary>
    public static string HoverText(int cid, int dec, int row, long seq) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"v1|{cid}|{dec}|{(row is >= 0 and < 8 ? row : -1)}|{seq}");

    public static bool TryParseHover(string? text, out int cid, out int dec, out int row, out long seq)
    {
        cid = dec = row = 0; seq = 0;
        if (string.IsNullOrEmpty(text) || text.Length > 60) return false;
        var f = text.Split('|');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (f.Length != 5 || f[0] != "v1" || !int.TryParse(f[1], System.Globalization.NumberStyles.None, inv, out cid) ||
            !int.TryParse(f[2], System.Globalization.NumberStyles.None, inv, out dec) || !int.TryParse(f[3], System.Globalization.NumberStyles.AllowLeadingSign, inv, out row) ||
            !long.TryParse(f[4], System.Globalization.NumberStyles.None, inv, out seq)) return false;
        return row is >= -1 and < 8;
    }

    /// <summary>F.2b: when a line that arrived at <paramref name="arrivedMs"/> may play, its pick having come at <paramref name="pickMs"/>:
    /// the pick's second first, never more than <see cref="LineHoldMaxMs"/> after the line arrived.</summary>
    public static long LineReleaseMs(long pickMs, long arrivedMs) => Math.Min(pickMs + PickShowMs, arrivedMs + LineHoldMaxMs);

    /// <summary>The switches that start off; the bench's KCDMP_TEST_W210_ON names one on.</summary>
    public static readonly string[] DefaultOff = [];

    /// <summary>A switch the mod has not set: on, unless it starts off (DefaultOff) and the bench's on-list does not name it, or a twin names it off.</summary>
    public static bool Default(string name, string? twinOff, string? benchOn) =>
        !Wo207Rules.TwinOff(twinOff, name) && (!DefaultOff.Contains(name, StringComparer.Ordinal) || Wo207Rules.TwinOff(benchOn, name));

    /// <summary>The DLL's switch word (op 138), the same bits as wo210_rules.h kBit* (0x02, clinch v2, is reserved: B.2/B.3 are not in this build).</summary>
    public static uint ConfigBits(Func<string, bool> on) => (on("copy_state_always") ? 0x01u : 0u)
                                                             | (on("death_engine") ? 0x04u : 0u) | (on("knockdown_sync") ? 0x08u : 0u)
                                                             | (on("copy_ready") ? 0x10u : 0u) | (on("copy_hit_local_none") ? 0x20u : 0u)
                                                             | (on("rider_sync") ? 0x40u : 0u) | (on("state_one_request") ? 0x80u : 0u);

    /// <summary>"name=on|off" from the mod: false when malformed or not one of <see cref="SwitchNames"/>.</summary>
    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        if (string.IsNullOrEmpty(arg)) return false;
        int eq = arg.IndexOf('=');
        if (eq <= 0) return false;
        string n = arg[..eq], v = arg[(eq + 1)..];
        if (v is not ("on" or "off") || !SwitchNames.Contains(n, StringComparer.Ordinal)) return false;
        name = n; on = v == "on";
        return true;
    }

    /// <summary>
    /// A (session E's floating sleepers): a host copy whose host NPC left the posture the copy's own body holds -- a bed, a seat, a
    /// workstation -- has its stance and unstance released the game's own way before the host's next activity is applied. The neutral
    /// rule (WO-200 II.4) keeps a copy's loaded state equal to what the body is, so a copy put to bed stayed in bed after its NPC got up:
    /// its own idle state lying at priority 3, slid along by the position stream (84.8 m on one body). An avatar (a partner's figure) has
    /// WO-160's own release; a repeated row of the same posture asks for nothing.
    /// </summary>
    public static bool ReleaseOnLeave(bool on, bool isAvatar, bool had, ActivityState prev, ActivityState next)
    {
        if (!on || isAvatar || !had) return false;
        var p = prev.Normalised();
        if (!p.OwnsPosition) return false;
        var n = next.Normalised();
        return !(n.OwnsPosition && n.SameBody(p));
    }

    /// <summary>
    /// C.4: the attack data of a joiner's blow on a host NPC with no fresh swing row: his last row's own attack (its type, zone, weapon,
    /// reach), else a plain slash (type 1) at zone 2 with the right hand at full power -- the host's engine judges and computes it.
    /// </summary>
    public static Wo166Rules.Strike FallbackStrike(ActionRowCatalog.Row? last)
    {
        if (last is { } r && Wo166Rules.StrikeFor(r) is { } s) return s;
        return new Wo166Rules.Strike(-1, -1, 1, 2, 1, 1.0f);
    }

    /// <summary>C.1: after the engine's own ClientHit, WO-202's fall reads the body only once the engine's death had its time (its HitDeath
    /// fragment into its ragdoll); a body still dead and upright then is the last resort's.</summary>
    public static readonly int[] DeathFallAfterEngineMs = { 2500, 1500 };

    /// <summary>E.3: a blow of the same attacker on the same figure within this of the last is a combo's later swing.</summary>
    public const long ComboGapMs = 1600;
    public static bool ComboContinues(long lastBlowMs, long blowMs) => lastBlowMs > 0 && blowMs - lastBlowMs >= 0 && blowMs - lastBlowMs <= ComboGapMs;
    /// <summary>E.3: the hold for a combo's later swing: the swing's own window plus 150 ms and one round trip, at most 650 ms.</summary>
    public const int ComboHoldCapMs = 650;
    public static int ComboHoldMs(int rttMs) => Math.Clamp(450 + 150 + Math.Max(0, rttMs) / 2, 0, ComboHoldCapMs);

    /// <summary>Op 140's reply: [n:1] n x ([kind:1][nameLen:1][name]) -> (kind, name) pairs; malformed rows end the list.</summary>
    public static List<(byte Kind, string Name)> ParseAsks(ReadOnlySpan<byte> b)
    {
        var r = new List<(byte, string)>();
        if (b.Length < 1) return r;
        int n = b[0], o = 1;
        for (int i = 0; i < n; i++)
        {
            if (o + 2 > b.Length) break;
            byte kind = b[o]; int nl = b[o + 1];
            if (nl == 0 || nl > Protocol.MaxNpcNameLen || o + 2 + nl > b.Length) break;
            string name = System.Text.Encoding.ASCII.GetString(b.Slice(o + 2, nl));
            o += 2 + nl;
            if (ActivityStateNameOk(name)) r.Add((kind, name));
        }
        return r;
    }

    private static bool ActivityStateNameOk(string n) => n.Length > 0 && n.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    /// <summary>NpcAnimAsk's body: [count:1] per body [nameLen:1][name] (count 0 = every body); null when a name does not fit.</summary>
    public static byte[]? AnimAskBody(IReadOnlyList<string> names)
    {
        if (names.Count > Protocol.NpcAnimAskMaxBodies) return null;
        var ms = new MemoryStream();
        ms.WriteByte((byte)names.Count);
        foreach (var n in names)
        {
            if (!ActivityStateNameOk(n) || n.Length > Protocol.MaxNpcNameLen) return null;
            ms.WriteByte((byte)n.Length);
            ms.Write(System.Text.Encoding.ASCII.GetBytes(n));
        }
        return ms.ToArray();
    }

    /// <summary>NpcAnimAsk's names (the host's side); false when malformed.</summary>
    public static bool TryParseAnimAsk(ReadOnlySpan<byte> b, out List<string> names)
    {
        names = [];
        if (b.Length < 1) return false;
        int n = b[0], o = 1;
        if (n > Protocol.NpcAnimAskMaxBodies) return false;
        for (int i = 0; i < n; i++)
        {
            if (o + 1 > b.Length) return false;
            int nl = b[o];
            if (nl == 0 || nl > Protocol.MaxNpcNameLen || o + 1 + nl > b.Length) return false;
            string s = System.Text.Encoding.ASCII.GetString(b.Slice(o + 1, nl));
            if (!ActivityStateNameOk(s)) return false;
            names.Add(s);
            o += 1 + nl;
        }
        return o == b.Length;
    }
}
