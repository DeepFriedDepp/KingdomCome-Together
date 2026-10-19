// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-201 B11 / B23: an item class's own model (its row's Model attribute under libs/tables/item/ in Tables.pak), so a hand item or a
/// quest item can be shown as a visual-only attachment of the same model -- never an inventory item, never the planner.
/// </summary>
public static class Wo201ItemModels
{
    private static readonly Regex Row = new(@"<(\w+)\s([^>]*\bModel=""([^""]+)""[^>]*)>", RegexOptions.CultureInvariant);
    private static readonly Regex Gid = new(@"\bId=""([0-9a-fA-F-]{36})""", RegexOptions.CultureInvariant);

    /// <summary>Item class GUID (lower) -> its model path under Objects/ ("manmade/weapons/...cgf"), the row's kind (MeleeWeapon, ...).</summary>
    public static Dictionary<string, (string Model, string Kind)> Read(string tablesPak)
    {
        var o = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        using var z = ZipFile.OpenRead(tablesPak);
        foreach (var e in z.Entries)
        {
            string low = e.FullName.ToLowerInvariant();
            if (!low.EndsWith(".xml", StringComparison.Ordinal) || !low.StartsWith("libs/tables/item/", StringComparison.Ordinal)) continue;
            string text;
            using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); text = Encoding.Latin1.GetString(ms.ToArray()); }
            foreach (Match m in Row.Matches(text))
            {
                var g = Gid.Match(m.Groups[2].Value);
                if (!g.Success) continue;
                string model = m.Groups[3].Value;
                if (!IsModelPath(model)) continue;
                o[g.Groups[1].Value.ToLowerInvariant()] = (model, m.Groups[1].Value);
            }
        }
        return o;
    }

    /// <summary>A model path safe to hand to the game's script: relative, a .cgf / .cdf, plain characters.</summary>
    public static bool IsModelPath(string p)
    {
        if (string.IsNullOrEmpty(p) || p.Length > 160 || p.Contains("..", StringComparison.Ordinal)) return false;
        if (!(p.EndsWith(".cgf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".cdf", StringComparison.OrdinalIgnoreCase))) return false;
        foreach (char c in p) if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/')) return false;
        return true;
    }

    /// <summary>The path the engine's attachment wants (objects/ + the row's path).</summary>
    public static string EnginePath(string model) => model.StartsWith("objects/", StringComparison.OrdinalIgnoreCase) ? model : "objects/" + model;

    /// <summary>B11: a ranged weapon (a bow or a crossbow: the item table's MissileWeapon rows).</summary>
    public static bool IsRanged(string kind) => kind == "MissileWeapon";
}
