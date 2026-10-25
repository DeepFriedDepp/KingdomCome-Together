// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-147: every quest State type's values, read from the installed game's own
/// Data\Scripts.pak (the quest XMLs' &lt;Type&gt;&lt;StateTypeEnumeration Name=
/// ObjectiveValueType=/&gt;&lt;/Type&gt; blocks; ~2,800 types in 1,532 files, read in
/// about a second), plus the engine's own QuestProgress. A State's value is its
/// index in its type's list; its port is "Set" + the value's name (the State
/// node's own Set&lt;Value&gt; ports, e.g. SetNpcIsDead on ImportantNpcIsDead).
///
/// Two uses: the host tells which joiner requests are destructive
/// (<see cref="Wo147Rules.Destructive"/>), and a correction fires only a port
/// that produces the host's value (<see cref="Wo147Rules.CorrectionPort"/>).
/// </summary>
public sealed class QuestValueIndex
{
    public readonly record struct Value(string Name, string Objective);

    // Type name -> every definition's values (a name can be defined in more than one quest file).
    private readonly Dictionary<string, List<Value[]>> _types = new(StringComparer.Ordinal);

    public int TypeCount => _types.Count;
    public int FileCount { get; private set; }

    // WO-166 T2: State path -> its declared TypeT ("Barbora.trosecko.kovar.hibernace.porovnani_kvality.kvalitaMece" -> "uint").
    private readonly Dictionary<string, string> _stateTypes = new(StringComparer.Ordinal);
    public int StateCount => _stateTypes.Count;
    private static readonly Regex StateRx = new(@"<State Name=""([^""]+)""([^>]*)>", RegexOptions.CultureInvariant);
    private static readonly Regex TypeTRx = new(@"TypeT=""([^""]+)""", RegexOptions.CultureInvariant);

    // WO-206 C: the clocks (Timer, TimeOfDayWatch nodes) -- the digest's keyspace beyond the State mirror (the Jaunt's 13:00 watch was one).
    private readonly SortedSet<string> _clockPaths = new(StringComparer.Ordinal);
    private static readonly Regex ClockRx = new(@"<(Timer|TimeOfDayWatch) Name=""([^""]+)""", RegexOptions.CultureInvariant);

    /// <summary>WO-206 C: every State path with a declared type, in path order.</summary>
    public IReadOnlyList<string> StatePaths => _stateTypes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    /// <summary>WO-206 C: every Timer and TimeOfDayWatch node path, in path order.</summary>
    public IReadOnlyCollection<string> ClockPaths => _clockPaths;

    /// <summary>WO-206 C: the clocks of one quest file (the same path rule as <see cref="AddStates"/>). Returns the clocks added.</summary>
    public int AddClocks(string entry, string xml)
    {
        string e = entry.Replace('\\', '/');
        const string root = "Quests/Final/";
        if (!e.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !e.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return 0;
        string module = e[root.Length..^4].Replace('/', '.');
        int n = 0;
        foreach (Match m in ClockRx.Matches(xml))
            if (_clockPaths.Add(module + "." + m.Groups[2].Value)) n++;
        return n;
    }

    // WO-207 D.2: the AreaTriggers whose souls are a named NPC (a SoulAsset of the trigger's module or one of the modules above it), never one
    // that names the player (an alias with "player" in it, a soul list fed by an edge, an alias no module defines): on a joiner those NPCs are
    // copies, so their own triggers must not write the joiner's States (the host's world decides).
    private readonly Dictionary<string, HashSet<string>> _soulAssets = new(StringComparer.OrdinalIgnoreCase);   // module path -> its SoulAsset names
    private readonly List<(string Module, string Node, string Alias)> _areaTriggers = new();
    private static readonly Regex SoulAssetRx = new(@"<SoulAsset Name=""([^""]+)""", RegexOptions.CultureInvariant);
    private static readonly Regex AreaTriggerRx = new(@"<AreaTrigger Name=""([^""]+)""[^>]*>(.*?)</AreaTrigger>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex SoulsAliasRx = new(@"<Asset Name=""Souls"" Alias=""([^""]+)""", RegexOptions.CultureInvariant);

    /// <summary>WO-207 D.2: one quest file's soul assets and AreaTriggers (resolved together in <see cref="NpcTriggerPaths"/>).</summary>
    public void AddTriggers(string entry, string xml)
    {
        string e = entry.Replace('\\', '/');
        const string root = "Quests/Final/";
        if (!e.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !e.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return;
        string module = e[root.Length..^4].Replace('/', '.');
        foreach (Match m in SoulAssetRx.Matches(xml))
        {
            if (!_soulAssets.TryGetValue(module, out var set)) _soulAssets[module] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(m.Groups[1].Value);
        }
        foreach (Match m in AreaTriggerRx.Matches(xml))
            if (SoulsAliasRx.Match(m.Groups[2].Value) is { Success: true } a) _areaTriggers.Add((module, m.Groups[1].Value, a.Groups[1].Value));
    }

    /// <summary>WO-207 D.2: the NPC triggers' node paths, in path order.</summary>
    public IReadOnlyList<string> NpcTriggerPaths()
    {
        var o = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (module, node, alias) in _areaTriggers)
        {
            if (alias.Contains("player", StringComparison.OrdinalIgnoreCase)) continue;
            for (string m = module; ; )
            {
                if (_soulAssets.TryGetValue(m, out var set) && set.Contains(alias)) { o.Add(module + "." + node); break; }
                int dot = m.LastIndexOf('.');
                if (dot <= 0) break;
                m = m[..dot];
            }
        }
        return o.ToList();
    }

    /// <summary>WO-166 T2: the State's own declared type (the quest file's TypeT), or null when no file declares it.</summary>
    public string? StateType(string path) => _stateTypes.TryGetValue(path, out var t) ? t : null;

    /// <summary>
    /// WO-166 T2: the States of one quest file. The engine's path of a node is the file's path under Quests/Final/ with dots, then the
    /// node's name (Quests/Final/Barbora/trosecko/kovar/hibernace/porovnani_kvality.xml + kvalitaMece). Returns the States added.
    /// </summary>
    public int AddStates(string entry, string xml)
    {
        string e = entry.Replace('\\', '/');
        const string root = "Quests/Final/";
        if (!e.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !e.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return 0;
        string module = e[root.Length..^4].Replace('/', '.');
        int n = 0;
        foreach (Match m in StateRx.Matches(xml))
        {
            var t = TypeTRx.Match(m.Groups[2].Value);
            if (!t.Success) continue;
            _stateTypes[module + "." + m.Groups[1].Value] = t.Groups[1].Value;
            n++;
        }
        return n;
    }

    /// <summary>The engine's own quest progress (wh::questmodule::QuestProgress, IPL_GameData definitions.xml).</summary>
    public static readonly Value[] QuestProgress =
        [new("None", "None"), new("Active", "Started"), new("Done", "Completed"), new("Failed", "Canceled")];

    private static readonly Regex TypeRx = new(@"<Type TypeName=""([^""]+)"">(.*?)</Type>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex ValRx = new(@"<StateTypeEnumeration Name=""([^""]+)""(?: ObjectiveValueType=""([^""]*)"")?", RegexOptions.CultureInvariant);

    public QuestValueIndex()
    {
        Add("wh::questmodule::QuestProgress", QuestProgress);
        Add("QuestProgress", QuestProgress);
    }

    public void Add(string type, Value[] values)
    {
        if (!_types.TryGetValue(type, out var list)) _types[type] = list = new List<Value[]>();
        list.Add(values);
    }

    /// <summary>Every definition of this type (exact name first, then the unqualified name after the last "::").</summary>
    public IReadOnlyList<Value[]> Definitions(string? type)
    {
        if (string.IsNullOrEmpty(type)) return Array.Empty<Value[]>();
        if (_types.TryGetValue(type, out var l)) return l;
        int c = type.LastIndexOf("::", StringComparison.Ordinal);
        if (c >= 0 && _types.TryGetValue(type[(c + 2)..], out var l2)) return l2;
        return Array.Empty<Value[]>();
    }

    /// <summary>Reads one quest XML's type blocks (the loader's unit; tests feed text directly).</summary>
    public int AddXml(string xml)
    {
        int n = 0;
        foreach (Match m in TypeRx.Matches(xml))
        {
            var vals = ValRx.Matches(m.Groups[2].Value)
                .Select(v => new Value(v.Groups[1].Value, v.Groups[2].Success && v.Groups[2].Value.Length > 0 ? v.Groups[2].Value : "None"))
                .ToArray();
            if (vals.Length == 0) continue;
            Add(m.Groups[1].Value, vals);
            n++;
        }
        return n;
    }

    public static QuestValueIndex LoadFrom(string scriptsPak)
    {
        var idx = new QuestValueIndex();
        using var zip = ZipFile.OpenRead(scriptsPak);
        byte[] marker = Encoding.ASCII.GetBytes("<StateTypeEnumeration ");
        byte[] stateMarker = Encoding.ASCII.GetBytes("<State ");   // WO-166 T2
        byte[] timerMarker = Encoding.ASCII.GetBytes("<Timer "), watchMarker = Encoding.ASCII.GetBytes("<TimeOfDayWatch ");   // WO-206 C
        byte[] areaMarker = Encoding.ASCII.GetBytes("<AreaTrigger "), soulMarker = Encoding.ASCII.GetBytes("<SoulAsset ");   // WO-207 D.2
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.StartsWith("Quests/", StringComparison.OrdinalIgnoreCase) || !e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsTestingFile(e.FullName)) continue;   // WO-154 1: developer test projects are no part of the game's quest database
            byte[] bytes;
            using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); bytes = ms.ToArray(); }
            bool types = bytes.AsSpan().IndexOf(marker) >= 0, states = bytes.AsSpan().IndexOf(stateMarker) >= 0;
            bool clocks = bytes.AsSpan().IndexOf(timerMarker) >= 0 || bytes.AsSpan().IndexOf(watchMarker) >= 0;
            bool triggers = bytes.AsSpan().IndexOf(areaMarker) >= 0 || bytes.AsSpan().IndexOf(soulMarker) >= 0;
            if (!types && !states && !clocks && !triggers) continue;
            string text = Encoding.UTF8.GetString(bytes);
            if (triggers) idx.AddTriggers(e.FullName, text);
            if (states) idx.AddStates(e.FullName, text);
            if (clocks) idx.AddClocks(e.FullName, text);
            if (types && idx.AddXml(text) > 0) idx.FileCount++;
        }
        return idx;
    }

    /// <summary>
    /// WO-154 1: Scripts.pak ships its developers' test projects beside the quests (Quests/Testing/&lt;name&gt;/...). Their
    /// type definitions are not the game's: one test file defines Challenge as None/Won/Lost where the game's own
    /// quests (and every other definition) say None/InProgress/Won/Lost, so the all-agree rule found no
    /// correction port for the field's Moravian fight (stateBitkaSMoravakem).
    /// </summary>
    public static bool IsTestingFile(string entry) =>
        entry.Replace('\\', '/').Contains("/Testing/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The installed game's Data\Scripts.pak (beside Tables.pak), or null.</summary>
    public static string? FindScriptsPak()
    {
        string? dir = Environment.GetEnvironmentVariable("KCD2MP_INSTALL");
        if (string.IsNullOrEmpty(dir)) dir = KcdLogLocator.Find() is string log ? Path.GetDirectoryName(log) : null;
        if (dir is null) return null;
        string p = Path.Combine(dir, "Data", "Scripts.pak");
        return File.Exists(p) ? p : null;
    }
}
