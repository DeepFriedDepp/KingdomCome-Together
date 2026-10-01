// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-96: the generated main-quest objective registry
/// (<c>mainquest-objectives.json</c>, embedded; built by
/// <c>tools/Build-MainQuestObjectives.ps1</c> from the same 32 M-coded quest
/// roots WO-94's beat registry uses). For each quest: its objectives in
/// document order, each with its localisation key (the engine writes the
/// quest's into <c>questNameOverride</c> markers), and the path(s) of its
/// display node inside the save's ConceptState tree.
///
/// WO-148 (the content audit): the journal's English titles are the game's
/// text, so the embedded file carries only the keys; <see cref="LocalizeFrom"/>
/// reads the titles at run time from the player's own
/// <c>Localization/English_xml.pak</c> (text_ui_quest.xml). Without it a label is
/// the internal name.
///
/// <see cref="Id"/> is a hash over everything a fingerprint index depends on.
/// Two agents compare fingerprints only when their ids match; otherwise the
/// comparison is refused and the mod keeps the single-marker behaviour.
/// </summary>
public sealed class QuestObjectiveRegistry
{
    public sealed class Objective
    {
        [JsonPropertyName("n")] public string Name { get; set; } = "";
        [JsonPropertyName("k")] public string StringName { get; set; } = "";
        [JsonPropertyName("t")] public string Title { get; set; } = "";
        [JsonPropertyName("optional")] public bool Optional { get; set; }
        [JsonPropertyName("paths")] public string[] Paths { get; set; } = Array.Empty<string>();
        public string Label => Title.Length > 0 ? Title : Name;
    }

    public sealed class Quest
    {
        [JsonPropertyName("code")] public string Code { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("level")] public string Level { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        /// <summary>WO-148: the quest title's localisation key (qname_...), resolved at run time.</summary>
        [JsonPropertyName("qk")] public string TitleKey { get; set; } = "";
        [JsonPropertyName("objectives")] public Objective[] Objectives { get; set; } = Array.Empty<Objective>();
        public string Label => Title.Length > 0 ? Title : Name;
    }

    private sealed class Root
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("pak")] public string Pak { get; set; } = "";
        [JsonPropertyName("quests")] public Quest[] Quests { get; set; } = Array.Empty<Quest>();
    }

    public string Id { get; }
    public string PakSha256 { get; }
    public IReadOnlyList<Quest> Quests { get; }
    private readonly Dictionary<string, Quest> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Quest> _byName = new(StringComparer.Ordinal);

    private QuestObjectiveRegistry(Root r)
    {
        Id = r.Id; PakSha256 = r.Pak; Quests = r.Quests;
        foreach (var q in r.Quests) { _byKey[q.Key] = q; _byName[q.Name] = q; }
    }

    public Quest? ByMarkerKey(string? keyLower) =>
        keyLower is not null && _byKey.TryGetValue(keyLower, out var q) ? q : null;
    public Quest? ByName(string name) => _byName.TryGetValue(name, out var q) ? q : null;

    /// <summary>The titles this registry got from a localisation pak (0 = none: labels are internal names).</summary>
    public int LocalizedTitles { get; private set; }

    private static readonly Regex RowRx = new("<Row>(.*?)</Row>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex CellRx = new("<Cell>(.*?)</Cell>", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>
    /// WO-148: the English titles from the player's own game (gameRoot/Localization/English_xml.pak,
    /// text_ui_quest.xml: a row per key, the text in its last cell). Returns how many were set; a
    /// missing or unreadable pak leaves the internal names. Never throws.
    /// </summary>
    public int LocalizeFrom(string? gameRoot)
    {
        try
        {
            if (string.IsNullOrEmpty(gameRoot)) return 0;
            string pak = Path.Combine(gameRoot, "Localization", "English_xml.pak");
            if (!File.Exists(pak)) return 0;
            var text = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var z = ZipFile.OpenRead(pak))
            {
                var e = z.Entries.FirstOrDefault(x => x.FullName.EndsWith("text_ui_quest.xml", StringComparison.OrdinalIgnoreCase));
                if (e is null) return 0;
                using var sr = new StreamReader(e.Open());
                foreach (Match row in RowRx.Matches(sr.ReadToEnd()))
                {
                    var cells = CellRx.Matches(row.Groups[1].Value);
                    if (cells.Count >= 2) text[cells[0].Groups[1].Value] = WebUtility.HtmlDecode(cells[cells.Count - 1].Groups[1].Value);
                }
            }
            int n = 0;
            foreach (var q in Quests)
            {
                if (q.TitleKey.Length > 0 && text.TryGetValue(q.TitleKey, out var qt)) { q.Title = qt; n++; }
                foreach (var o in q.Objectives)
                    if (o.StringName.Length > 0 && text.TryGetValue(o.StringName, out var ot)) { o.Title = ot; n++; }
            }
            LocalizedTitles = n;
            return n;
        }
        catch (Exception) { return 0; }
    }

    public static QuestObjectiveRegistry Parse(string json)
    {
        var r = JsonSerializer.Deserialize<Root>(json) ?? throw new InvalidDataException("empty objective registry");
        if (string.IsNullOrEmpty(r.Id) || r.Quests.Length == 0) throw new InvalidDataException("objective registry has no id or no quests");
        return new QuestObjectiveRegistry(r);
    }

    private static QuestObjectiveRegistry? _embedded;
    private static readonly object _lock = new();

    /// <summary>The registry compiled into this agent, or null if the resource is missing/corrupt (logged once).</summary>
    public static QuestObjectiveRegistry? Embedded
    {
        get
        {
            lock (_lock)
            {
                if (_embedded is not null) return _embedded;
                try
                {
                    var asm = typeof(QuestObjectiveRegistry).Assembly;
                    string? name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("mainquest-objectives.json", StringComparison.OrdinalIgnoreCase));
                    if (name is null) { Console.WriteLine("[quest] objective registry resource missing -- fingerprints disabled"); return null; }
                    using var s = asm.GetManifestResourceStream(name)!;
                    using var sr = new StreamReader(s);
                    _embedded = Parse(sr.ReadToEnd());
                    // WO-148: the titles come from the player's own game (the log locator finds its root)
                    string? log = KcdLogLocator.Find();
                    int n = _embedded.LocalizeFrom(log is null ? null : Path.GetDirectoryName(log));
                    Console.WriteLine(n > 0 ? $"[quest] objective registry: {n} titles from the game's own localisation"
                                            : "[quest] objective registry: no localisation found -- labels are the internal names");
                    return _embedded;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[quest] objective registry unreadable: {ex.Message} -- fingerprints disabled");
                    return null;
                }
            }
        }
    }
}
