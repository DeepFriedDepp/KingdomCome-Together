// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;

namespace KcdMp.Setup;

/// <summary>
/// Steam's text KeyValues format (libraryfolders.vdf, appmanifest_*.acf):
/// quoted "key" "value" pairs and "key" { ... } sections, backslashes doubled.
///
/// Tolerant on purpose. A truncated or hand-edited file yields the pairs read
/// before the damage rather than an exception -- the installer's old Pascal
/// reader degraded the same way ("just the Steam root"), and a setup check
/// that throws on a bad vdf would be worse than one that finds less.
/// </summary>
public static class Vdf
{
    public readonly record struct Pair(string Key, string Value, int Depth);

    /// <summary>Every "key" "value" pair in file order, with its section depth (0 = top level).</summary>
    public static List<Pair> Pairs(string text)
    {
        var pairs = new List<Pair>();
        var tokens = Tokens(text);
        int depth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            var (kind, value) = tokens[i];
            if (kind == '{') { depth++; continue; }
            if (kind == '}') { depth = Math.Max(0, depth - 1); continue; }
            // a string: a pair when the next token is a string, a section name when it is '{'
            if (i + 1 < tokens.Count && tokens[i + 1].Kind == 's')
            {
                pairs.Add(new Pair(value, tokens[i + 1].Value, depth));
                i++;
            }
        }
        return pairs;
    }

    /// <summary>The first value for <paramref name="key"/> at any depth (keys compare case-insensitively, as Steam's do).</summary>
    public static string? Value(IEnumerable<Pair> pairs, string key)
    {
        foreach (var p in pairs)
            if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static List<(char Kind, string Value)> Tokens(string text)
    {
        var list = new List<(char, string)>();
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                while (i < n && text[i] != '\n') i++;
                continue;
            }
            if (c == '{' || c == '}') { list.Add((c, "")); i++; continue; }
            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                bool closed = false;
                while (i < n)
                {
                    char d = text[i];
                    if (d == '\\' && i + 1 < n)
                    {
                        char e = text[i + 1];
                        sb.Append(e switch { 'n' => '\n', 't' => '\t', _ => e });
                        i += 2;
                        continue;
                    }
                    if (d == '"') { closed = true; i++; break; }
                    sb.Append(d);
                    i++;
                }
                if (!closed) break;   // truncated mid-string: keep what came before it
                list.Add(('s', sb.ToString()));
                continue;
            }
            // an unquoted token (rare; Steam itself never writes them)
            int start = i;
            while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}' && text[i] != '"') i++;
            list.Add(('s', text[start..i]));
        }
        return list;
    }
}
