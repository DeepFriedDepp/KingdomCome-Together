// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-221 D: whose horse is whose in one host world. The game holds one "player's horse" (it has one player); a joiner's horse is
/// the mod's own record: the horse's entity name in the host's world -> the owning player's identity (his install key: the same for
/// every session and every rejoin). One horse per player, as in the game alone: a new one replaces the one before. The host's own
/// horse is the game's own record and is never in here.
/// </summary>
public sealed class HorseRecord
{
    public sealed class Entry
    {
        public string Horse { get; set; } = "";
        public uint Owner { get; set; }
        public string OwnerName { get; set; } = "";
        public string How { get; set; } = "";       // bought | given | told
        public long SinceUnixS { get; set; }
        /// <summary>The quest module instance that sells this horse (learned when its purchase replayed in the host's world), or "".</summary>
        public string Module { get; set; } = "";
    }

    public List<Entry> Entries { get; set; } = new();

    private static readonly Regex NameRx = new("^[A-Za-z0-9_]{1,59}$", RegexOptions.Compiled);
    public static bool IsHorseName(string? n) => n is not null && NameRx.IsMatch(n) && !n.StartsWith("kcd2mp_", StringComparison.Ordinal);

    public string? HorseOf(uint owner) => Entries.LastOrDefault(e => e.Owner == owner)?.Horse;
    public Entry? OwnerOf(string horse) => Entries.LastOrDefault(e => string.Equals(e.Horse, horse, StringComparison.Ordinal));

    /// <summary>This player's horse is <paramref name="horse"/> now. False (nothing changed) when it already is, when the name is not
    /// a horse's, when the key is 0, or when the horse is another player's (first come first served: a recorded horse is never
    /// taken over by a claim).</summary>
    public bool Set(uint owner, string ownerName, string horse, string how, long nowUnixS)
    {
        if (owner == 0 || !IsHorseName(horse)) return false;
        var cur = OwnerOf(horse);
        if (cur is not null && cur.Owner != owner) return false;
        if (cur is not null && HorseOf(owner) == horse) { cur.OwnerName = ownerName; return false; }
        Entries.RemoveAll(e => e.Owner == owner);
        Entries.Add(new Entry { Horse = horse, Owner = owner, OwnerName = ownerName, How = how, SinceUnixS = nowUnixS });
        return true;
    }

    /// <summary>This player has no horse of his own any more (sold, lost). True when one was recorded.</summary>
    public bool Clear(uint owner) => Entries.RemoveAll(e => e.Owner == owner) > 0;

    /// <summary>"horse=ownerkeyhex,..." for the wire and the logs (sorted; "-" for none).</summary>
    public string Csv() => Entries.Count == 0 ? "-"
        : string.Join(",", Entries.OrderBy(e => e.Horse, StringComparer.Ordinal).Select(e => FormattableString.Invariant($"{e.Horse}={e.Owner:x8}")));

    public static List<KeyValuePair<string, uint>> ParseCsv(string? csv)
    {
        var o = new List<KeyValuePair<string, uint>>();
        if (string.IsNullOrWhiteSpace(csv) || csv == "-") return o;
        foreach (var item in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = item.IndexOf('=');
            if (eq <= 0 || !IsHorseName(item[..eq])) continue;
            if (!uint.TryParse(item[(eq + 1)..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint key) || key == 0) continue;
            o.Add(new(item[..eq], key));
        }
        return o;
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static HorseRecord FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new HorseRecord();
        try
        {
            var r = JsonSerializer.Deserialize<HorseRecord>(json) ?? new HorseRecord();
            r.Entries = (r.Entries ?? new()).Where(e => e is not null && e.Owner != 0 && IsHorseName(e.Horse)).ToList();
            return r;
        }
        catch (JsonException) { return new HorseRecord(); }   // a broken file is an empty record: nobody loses a save over it
    }
}

/// <summary>
/// WO-221 D: the host's horse records, one per world: the running one and a copy paired with every host save (the chest ledger's way,
/// WO-134), so the record is the one that belongs to the world as the loaded save has it -- it survives the host's save, a reload
/// and a rejoin, and a load of an older save brings that save's record back. Kept under the agent's own data folder, never in a
/// playline.
/// </summary>
public sealed class HorseStore
{
    public const int KeepPairs = 100;
    private readonly string _root;
    private readonly object _gate = new();
    private static readonly Regex TagRx = new("^[0-9a-f]{6,16}$", RegexOptions.Compiled);
    private static readonly Regex Md5Rx = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    public HorseStore(string root) { _root = root; }

    public static string DefaultRoot() => Path.Combine(Path.GetDirectoryName(HenryStore.DefaultRoot())!, "horses");

    private string Dir(string tag)
    {
        if (!TagRx.IsMatch(tag)) throw new ArgumentException("not a world tag: " + tag);
        return Path.Combine(_root, tag);
    }

    public HorseRecord Current(string tag)
    {
        lock (_gate)
        {
            var p = Path.Combine(Dir(tag), "current.json");
            return HorseRecord.FromJson(File.Exists(p) ? File.ReadAllText(p) : null);
        }
    }

    public void SaveCurrent(string tag, HorseRecord r) => Write(Path.Combine(Dir(tag), "current.json"), r.ToJson());

    /// <summary>A host world save: the running record is that save's.</summary>
    public void Pair(string tag, string md5, HorseRecord r)
    {
        md5 = md5.ToLowerInvariant();
        if (!Md5Rx.IsMatch(md5)) throw new ArgumentException("md5");
        Write(Path.Combine(Dir(tag), $"pair-{md5}.json"), r.ToJson());
        lock (_gate)
        {
            var old = Directory.EnumerateFiles(Dir(tag), "pair-*.json").Select(f => new FileInfo(f))
                               .OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeepPairs).ToList();
            foreach (var f in old) try { f.Delete(); } catch (IOException) { }
        }
    }

    /// <summary>The record paired with a host save, or null (a save from before this record existed, or made with no session).</summary>
    public HorseRecord? Paired(string tag, string md5)
    {
        lock (_gate)
        {
            var p = Path.Combine(Dir(tag), $"pair-{md5.ToLowerInvariant()}.json");
            return File.Exists(p) ? HorseRecord.FromJson(File.ReadAllText(p)) : null;
        }
    }

    private void Write(string path, string json)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".part", json);
            File.Move(path + ".part", path, overwrite: true);
        }
    }
}

/// <summary>WO-221 finish: the joiner's mod asks for the record's word when it has none (it forgets it while its session pauses: a
/// host's reload, a join). The agent keeps the host's last word for this connection.</summary>
public static class Wo221HorseAsk
{
    /// <summary>What answers the mod's ask: the agent's word for the mod (a horse name, or "" for none), or null when the host has
    /// not told this connection yet ("?") -- then the host is asked (the joiner's key, "-").</summary>
    public static string? Answer(string? myHorse) =>
        myHorse is null || myHorse == "?" ? null
        : myHorse.Length == 0 ? ""
        : HorseRecord.IsHorseName(myHorse) ? myHorse : null;
}
