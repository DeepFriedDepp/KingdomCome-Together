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
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.StartsWith("Quests/", StringComparison.OrdinalIgnoreCase) || !e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsTestingFile(e.FullName)) continue;   // WO-154 1: developer test projects are no part of the game's quest database
            byte[] bytes;
            using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); bytes = ms.ToArray(); }
            if (bytes.AsSpan().IndexOf(marker) < 0) continue;
            if (idx.AddXml(Encoding.UTF8.GetString(bytes)) > 0) idx.FileCount++;
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
