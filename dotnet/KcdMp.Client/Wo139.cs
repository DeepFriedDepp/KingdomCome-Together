// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-139: the pure rules of crime and guards (docs/WO-139-findings.md). The
/// host keeps one <see cref="JoinerCrimeRecord"/> per joiner: what he did in
/// its world, who of its NPCs could see it, and whether a guard knows.
/// </summary>
public static class Wo139Rules
{
    /// <summary>One row of the game's own crime table (Tables.pak Libs/Tables/rpg/crime.xml).</summary>
    public readonly record struct CrimeRow(string Label, int Fine, bool Violent, float ExpirationDays, int JailDays, bool Confiscation);

    /// <summary>The crime table rows this WO uses, copied from Tables.pak rpg/crime.xml (1.5.5). Fines are decagroschen.</summary>
    public static readonly Dictionary<string, CrimeRow> Table = new(StringComparer.Ordinal)
    {
        ["theft"]           = new("theft", 500, false, 4, 3, true),
        ["lockpick"]        = new("lockpick", 600, false, 4, 3, true),
        ["trespass"]        = new("trespass", 250, false, 2, 1, true),
        ["horseTheft"]      = new("horseTheft", 2000, false, 4, 3, false),
        ["corpseViolation"] = new("corpseViolation", 2000, true, 4, 5, true),
        ["assault"]         = new("assault", 1500, true, 5, 5, false),
        ["murder"]          = new("murder", 20000, true, 7, 7, true),
    };

    /// <summary>The game's crime label for one of this WO's crime words.</summary>
    public static string LabelOf(string crime) => crime switch
    {
        "theft" => "theft",
        "lockpick" => "lockpick",
        "trespass" => "trespass",
        "horsetheft" => "horseTheft",
        "robbody" => "theft",          // looting a body not legal to loot: the game's theft (method lootCorpse)
        "assault" => "assault",
        "knockout" => "assault",
        "murder" => "murder",
        _ => "theft",
    };

    public static CrimeRow RowOf(string crime) => Table[LabelOf(crime)];

    /// <summary>Violent crimes bring a fight (the guards pursue); the others a stop (a talk).</summary>
    public static bool IsViolent(string crime) => RowOf(crime).Violent;

    /// <summary>
    /// The settlement a faction belongs to: "trosecko_settlements_zelejov_soldiers_militia" ->
    /// "trosecko_settlements_zelejov" (the region, "settlements", the place). Other faction
    /// shapes keep their first two segments; empty or odd names give null.
    /// </summary>
    public static string? SettlementOf(string? faction)
    {
        if (string.IsNullOrEmpty(faction)) return null;
        var seg = faction.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (seg.Length >= 3 && seg[1] == "settlements") return $"{seg[0]}_{seg[1]}_{seg[2]}";
        if (seg.Length >= 2) return $"{seg[0]}_{seg[1]}";
        return null;
    }

    // ---- the texts (checked field by field on both ends) ----

    public readonly record struct Report(string Crime, float X, float Y, float Z, string Victim, string Item, string Where);

    public static string ReportText(Report r) => FormattableString.Invariant(
        $"{r.Crime} {r.X:F2} {r.Y:F2} {r.Z:F2} {Dash(r.Victim)} {Dash(r.Item)} {r.Where}");

    public static bool TryParseReport(string? text, out Report r)
    {
        r = default;
        var f = Split(text, 7);
        if (f is null || !Wo139Text.IsReportedCrime(f[0])) return false;
        if (!Wo139Text.TryCoord(f[1], out float x) || !Wo139Text.TryCoord(f[2], out float y) || !Wo139Text.TryCoord(f[3], out float z)) return false;
        if (!Wo139Text.IsNameOrDash(f[4]) || !Wo139Text.IsItemOrDash(f[5]) || !Wo139Text.IsWhere(f[6])) return false;
        r = new Report(f[0], x, y, z, f[4] == "-" ? "" : f[4], f[5] == "-" ? "" : f[5], f[6]);
        return true;
    }

    public static string OutcomeText(string result, string guard, int fine) =>
        FormattableString.Invariant($"{result} {Dash(guard)} {Math.Max(0, fine)}");

    public static bool TryParseOutcome(string? text, out string result, out string guard, out int fine)
    {
        result = guard = ""; fine = 0;
        var f = Split(text, 3);
        if (f is null || !Wo139Text.IsResult(f[0]) || !Wo139Text.IsNameOrDash(f[1]) || !Wo139Text.TryInt(f[2], out fine) || fine < 0) return false;
        result = f[0]; guard = f[1] == "-" ? "" : f[1];
        return true;
    }

    public static string JudgedText(string crime, int witnesses, int guards, bool known, string? settlement) =>
        FormattableString.Invariant($"{crime} {Math.Max(0, witnesses)} {Math.Max(0, guards)} {(known ? 1 : 0)} {Dash(settlement)}");

    public static bool TryParseJudged(string? text, out string crime, out int witnesses, out int guards, out bool known, out string settlement)
    {
        crime = settlement = ""; witnesses = guards = 0; known = false;
        var f = Split(text, 5);
        if (f is null || !Wo139Text.IsCrime(f[0]) || !Wo139Text.TryInt(f[1], out witnesses) || !Wo139Text.TryInt(f[2], out guards)
            || witnesses < 0 || guards < 0 || f[3] is not ("0" or "1") || !Wo139Text.IsSettlementOrDash(f[4])) return false;
        crime = f[0]; known = f[3] == "1"; settlement = f[4] == "-" ? "" : f[4];
        return true;
    }

    /// <summary>"theft:2,trespass:1" -- the open crimes of one stop or record.</summary>
    public static string CrimesText(IEnumerable<string> crimes)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in crimes) counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
        return counts.Count == 0 ? "-" : string.Join(",", counts.Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    public static bool TryParseCrimes(string? text, out List<(string Crime, int Count)> crimes)
    {
        crimes = new();
        if (text == "-") return true;
        if (string.IsNullOrEmpty(text) || text.Length > 400) return false;
        foreach (var part in text.Split(','))
        {
            var kv = part.Split(':');
            if (kv.Length != 2 || !Wo139Text.IsCrime(kv[0]) || !Wo139Text.TryInt(kv[1], out int n) || n < 1 || n > 999) return false;
            crimes.Add((kv[0], n));
        }
        return crimes.Count > 0;
    }

    public static string StopText(string guard, IEnumerable<string> crimes, int fine) =>
        FormattableString.Invariant($"{guard} {CrimesText(crimes)} {Math.Max(0, fine)}");

    public static bool TryParseStop(string? text, out string guard, out List<(string Crime, int Count)> crimes, out int fine)
    {
        guard = ""; crimes = new(); fine = 0;
        var f = Split(text, 3);
        if (f is null || !Wo139Text.IsName(f[0]) || !TryParseCrimes(f[1], out crimes) || crimes.Count == 0
            || !Wo139Text.TryInt(f[2], out fine) || fine < 0) return false;
        guard = f[0];
        return true;
    }

    public static string PursueText(bool on, string guard) => $"{(on ? "on" : "off")} {guard}";

    public static bool TryParsePursue(string? text, out bool on, out string guard)
    {
        on = false; guard = "";
        var f = Split(text, 2);
        if (f is null || !Wo139Text.IsOnOff(f[0]) || !Wo139Text.IsName(f[1])) return false;
        on = f[0] == "on"; guard = f[1];
        return true;
    }

    public static string RecordText(int open, IEnumerable<string> crimes, IEnumerable<string> settlements)
    {
        var s = settlements.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return FormattableString.Invariant($"{Math.Max(0, open)} {CrimesText(crimes)} {(s.Count == 0 ? "-" : string.Join(",", s))}");
    }

    public static bool TryParseRecord(string? text, out int open, out List<(string Crime, int Count)> crimes, out List<string> settlements)
    {
        open = 0; crimes = new(); settlements = new();
        var f = Split(text, 3);
        if (f is null || !Wo139Text.TryInt(f[0], out open) || open < 0 || !TryParseCrimes(f[1], out crimes)) return false;
        if (f[2] != "-")
            foreach (var s in f[2].Split(','))
            {
                if (!Wo139Text.IsSettlement(s)) return false;
                settlements.Add(s);
            }
        return true;
    }

    /// <summary>Horse names in parts that each fit a message (and a Lua call).</summary>
    public static List<string> HorsesTexts(IReadOnlyCollection<string> names, int maxChars = 900)
    {
        var parts = new List<List<string>>();
        var cur = new List<string>(); int len = 0;
        foreach (var n in names.Where(Wo139Text.IsName).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (cur.Count > 0 && len + n.Length + 1 > maxChars) { parts.Add(cur); cur = new(); len = 0; }
            cur.Add(n); len += n.Length + 1;
        }
        if (cur.Count > 0 || parts.Count == 0) parts.Add(cur);
        var texts = new List<string>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
            texts.Add(FormattableString.Invariant($"{i + 1} {parts.Count} {(parts[i].Count == 0 ? "-" : string.Join(",", parts[i]))}"));
        return texts;
    }

    public static bool TryParseHorses(string? text, out int part, out int nparts, out List<string> names)
    {
        part = nparts = 0; names = new();
        var f = Split(text, 3);
        if (f is null || !Wo139Text.TryInt(f[0], out part) || !Wo139Text.TryInt(f[1], out nparts) || part < 1 || nparts < 1 || part > nparts || nparts > 50) return false;
        if (f[2] == "-") return true;
        foreach (var n in f[2].Split(','))
        {
            if (!Wo139Text.IsName(n)) return false;
            names.Add(n);
        }
        return true;
    }

    public static string ClearedText(string why, string? settlement) => $"{why} {Dash(settlement)}";

    public static bool TryParseCleared(string? text, out string why, out string settlement)
    {
        why = settlement = "";
        var f = Split(text, 2);
        if (f is null || !Wo139Text.IsWord(f[0]) || !Wo139Text.IsSettlementOrDash(f[1])) return false;
        why = f[0]; settlement = f[1] == "-" ? "" : f[1];
        return true;
    }

    public static string ModeText(bool on, string why) => $"{(on ? "on" : "off")} {why}";

    public static bool TryParseMode(string? text, out bool on, out string why)
    {
        on = false; why = "";
        var f = Split(text, 2);
        if (f is null || !Wo139Text.IsOnOff(f[0]) || !Wo139Text.IsWord(f[1])) return false;
        on = f[0] == "on"; why = f[1];
        return true;
    }

    public static bool TryParseResync(string? text, out string why)
    {
        why = "";
        var f = Split(text, 1);
        if (f is null || !Wo139Text.IsWord(f[0])) return false;
        why = f[0];
        return true;
    }

    /// <summary>
    /// What a stop's outcome does to the joiner's record in that settlement: the game's own
    /// resolution (a fine paid, a punishment served, an execution, a bribe or a persuasion that
    /// worked) clears it; a fight or a flight leaves it and makes it violent (resisting arrest);
    /// "talked" / "refused" / "nostop" leave it as it was.
    /// </summary>
    public enum OutcomeEffect { Clear, Resist, Keep }

    public static OutcomeEffect EffectOf(string result) => result switch
    {
        "paid" or "punished" or "executed" or "bribed" or "persuaded" => OutcomeEffect.Clear,
        "fought" or "fled" => OutcomeEffect.Resist,
        _ => OutcomeEffect.Keep,
    };

    private static string Dash(string? s) => string.IsNullOrEmpty(s) ? "-" : s;

    private static string[]? Split(string? text, int n)
    {
        if (string.IsNullOrEmpty(text) || text.Length > Protocol.CrimeTextMax) return null;
        var f = text.Split(' ');
        return f.Length == n ? f : null;
    }
}

/// <summary>
/// WO-139, host: one joiner's open crimes in this world. A crime nobody saw is not kept (the
/// game's own rule: an unseen theft is no crime). Crimes expire after the game's own
/// expiration days; a resolution clears a settlement; a host reload clears everything (the
/// world, and the joiner with it, went back to before them).
///
/// What a guard does about them is the game's own rule (the native ChooseReaction, research
/// B): he ATTACKS for a violent crime that is fresh, or for any crime once the criminal
/// resisted (an escalation, crime_arrestEscalationPeriod = 5 min); otherwise he ARRESTS.
/// Here: hostile = pursuit on the host; arrestable = a stop on the joiner's machine.
/// </summary>
public sealed class JoinerCrimeRecord
{
    public sealed class Crime
    {
        public uint Id;                  // the joiner's crime id (0 for the host's own detections)
        public string Kind = "";         // this WO's crime word
        public string Settlement = "";   // where (the victim's / the witnesses' settlement); "" = the wilds
        public float X, Y, Z;
        public string Victim = "";
        public int Witnesses, Guards;
        public bool Known;               // a guard of that settlement knows (saw it, or was told)
        public double AtWorldS;          // the host's world time (s) when it was raised
        public long AtMs;                // this machine's clock, for the report delay
        public bool Resisted;            // a stop for it ended in a fight / a flight
        public long KnownAtMs;           // when a guard came to know it (this machine's clock)
        public long ResistedAtMs;        // when the resist happened
        public bool Calmed;              // a fight over it ended (the joiner went down, or died): no longer fresh
    }

    private readonly List<Crime> _open = new();
    public IReadOnlyList<Crime> Open => _open;

    /// <summary>A civilian's report reaches the settlement's guards after this long.</summary>
    public const long ReportDelayMs = 20_000;

    /// <summary>A violent crime is fresh (the guards attack) this long after they came to know it.</summary>
    public const long FreshMs = 120_000;

    /// <summary>A resist keeps the guards attacking this long (the game's crime_arrestEscalationPeriod, 300000 ms).</summary>
    public const long EscalationMs = 300_000;

    public int Count => _open.Count;

    /// <summary>
    /// A witnessed crime joins the record; an unwitnessed one is not a crime. True = kept.
    /// A trespass is one crime per settlement while it is open: the joiner re-reports it every
    /// few seconds while he stays inside (someone may walk in on him), and a later report only
    /// adds what it saw -- the game's own trespass is one escalation, not one per warning.
    /// </summary>
    public bool Add(Crime c)
    {
        if (c.Witnesses <= 0) return false;
        if (c.Guards > 0) { c.Known = true; c.KnownAtMs = c.AtMs; }
        if (c.Kind == "trespass" && _open.FirstOrDefault(o => o.Kind == "trespass" && o.Settlement == c.Settlement) is { } open)
        {
            open.Witnesses = Math.Max(open.Witnesses, c.Witnesses);
            open.Guards = Math.Max(open.Guards, c.Guards);
            if (c.Known && !open.Known) { open.Known = true; open.KnownAtMs = c.AtMs; }
            return true;
        }
        _open.Add(c);
        if (_open.Count > 200) _open.RemoveAt(0);
        return true;
    }

    /// <summary>Civilian reports reach the guards (after the delay). Returns the crimes that became known now.</summary>
    public List<Crime> Tick(long nowMs, double worldS)
    {
        var became = new List<Crime>();
        _open.RemoveAll(c => worldS > 0 && c.AtWorldS > 0 && worldS - c.AtWorldS > Wo139Rules.RowOf(c.Kind).ExpirationDays * 86400.0);
        foreach (var c in _open)
            if (!c.Known && nowMs - c.AtMs >= ReportDelayMs) { c.Known = true; c.KnownAtMs = nowMs; became.Add(c); }
        return became;
    }

    /// <summary>Open crimes a guard of this settlement knows about.</summary>
    public List<Crime> KnownIn(string settlement) => _open.Where(c => c.Known && c.Settlement == settlement).ToList();

    /// <summary>The guards attack for it now: a fresh violent crime, or an escalation (a resist) under 5 min old.</summary>
    public static bool IsHostile(Crime c, long nowMs) =>
        c.Known && ((Wo139Rules.IsViolent(c.Kind) && !c.Calmed && nowMs - c.KnownAtMs < FreshMs)
                    || (c.Resisted && nowMs - c.ResistedAtMs < EscalationMs));

    public bool AnyHostileKnownIn(string settlement, long nowMs) => _open.Any(c => c.Settlement == settlement && IsHostile(c, nowMs));

    /// <summary>Known crimes a guard of this settlement arrests for (a stop): every one that is not hostile now.</summary>
    public List<Crime> ArrestableIn(string settlement, long nowMs) =>
        _open.Where(c => c.Known && c.Settlement == settlement && !IsHostile(c, nowMs)).ToList();

    /// <summary>The fine the crime table puts on these crimes (the dialogue's own fine is computed by the game).</summary>
    public static int FineOf(IEnumerable<Crime> crimes) => crimes.Sum(c => Wo139Rules.RowOf(c.Kind).Fine);

    public int Clear(string? settlement)
    {
        if (string.IsNullOrEmpty(settlement)) { int n = _open.Count; _open.Clear(); return n; }
        return _open.RemoveAll(c => c.Settlement == settlement);
    }

    public void MarkResisted(string settlement, long nowMs)
    {
        foreach (var c in _open) if (c.Settlement == settlement) { c.Resisted = true; c.ResistedAtMs = nowMs; }
    }

    /// <summary>A fight is over (the joiner went down, or died): the crimes stand, but no longer as fresh
    /// or escalated -- the next guard to see him arrests him. Null = every settlement.</summary>
    public void Calm(string? settlement)
    {
        foreach (var c in _open)
            if (string.IsNullOrEmpty(settlement) || c.Settlement == settlement) { c.Calmed = true; c.Resisted = false; }
    }

    public IEnumerable<string> Settlements => _open.Select(c => c.Settlement).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal);
}
