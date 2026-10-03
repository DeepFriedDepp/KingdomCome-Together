// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KcdMp.Wire;

/// <summary>
/// WO-154: the one way anything of ours writes a player's settings file (the launcher's
/// <c>settings.json</c>, the mod's <c>mod-settings.json</c>). The file belongs to the player:
/// <list type="bullet">
/// <item>an edit touches only its own key: a changed value is replaced in place, a missing key is
///   appended to the root object; every other byte (unknown keys, their order, the formatting,
///   a byte-order mark) stays exactly as it was;</item>
/// <item>a "fill" edit (<see cref="Edit.Fill"/>) never replaces a value that is already there;</item>
/// <item>nothing changes, nothing is written (the file keeps its bytes and its time);</item>
/// <item>a file that does not read as one JSON object is never written over;</item>
/// <item>a write goes to a temporary file first and is moved over the real one in one step.</item>
/// </list>
/// Until 0.44.0 the launcher serialised its whole settings object over the file on every save:
/// unknown keys were dropped and every value was rewritten.
/// </summary>
public static class SettingsJson
{
    /// <summary>One key to set (<see cref="Set"/>: the player's own change) or to add only when missing (<see cref="Fill"/>).</summary>
    public readonly record struct Edit(string Key, JsonNode? Value, bool OnlyIfMissing)
    {
        public static Edit Set(string key, JsonNode? value) => new(key, value, false);
        public static Edit Fill(string key, JsonNode? value) => new(key, value, true);
    }

    public enum Outcome { Unchanged, Written, Created, Refused }

    public sealed record Result(Outcome Outcome, string Why, IReadOnlyList<string> Changed)
    {
        public bool Ok => Outcome != Outcome.Refused;
    }

    private static readonly JsonReaderOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// The text with the edits applied, or null when nothing changes (or the text is not one JSON
    /// object: <paramref name="why"/> says which). Pure: the unit tests' half.
    /// </summary>
    public static byte[]? Apply(byte[] original, IReadOnlyList<Edit> edits, out string why, out List<string> changed)
    {
        changed = new List<string>();
        int bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF ? 3 : 0;
        var body = new ReadOnlySpan<byte>(original, bom, original.Length - bom);
        if (!Scan(body, out var props, out int closeAt, out bool empty, out why)) return null;

        // Replacements in place (end to start, so earlier offsets stay valid), then the appends.
        var repl = new List<(int Start, int End, byte[] Text, string Key)>();
        var appends = new List<(string Key, JsonNode? Value)>();
        foreach (var e in edits)
        {
            if (string.IsNullOrEmpty(e.Key)) continue;
            var hit = props.FindLast(p => p.Name == e.Key);
            if (hit.Name is null)
            {
                if (appends.Exists(a => a.Key == e.Key)) continue;
                appends.Add((e.Key, e.Value));
                continue;
            }
            if (e.OnlyIfMissing) continue;
            byte[] now = body.Slice(hit.Start, hit.End - hit.Start).ToArray();
            byte[] want = Encoding.UTF8.GetBytes(ValueText(e.Value));
            if (SameValue(now, e.Value)) continue;
            repl.Add((hit.Start, hit.End, want, e.Key));
        }
        if (repl.Count == 0 && appends.Count == 0) { why = "nothing to change"; return null; }

        var outBody = new List<byte>(body.Length + 64 * appends.Count);
        int at = 0;
        foreach (var r in repl.OrderBy(r => r.Start))
        {
            outBody.AddRange(body.Slice(at, r.Start - at).ToArray());
            outBody.AddRange(r.Text);
            at = r.End;
            changed.Add(r.Key);
        }
        // up to the root's closing brace, the appended keys, then the rest
        outBody.AddRange(body.Slice(at, closeAt - at).ToArray());
        if (appends.Count > 0)
        {
            string sep = Separator(body, props, closeAt, out string tail);
            var sb = new StringBuilder();
            bool first = empty;
            // the whitespace before the closing brace stays before it
            int trim = TrailingWhitespace(outBody);
            var keep = outBody.GetRange(outBody.Count - trim, trim);
            outBody.RemoveRange(outBody.Count - trim, trim);
            foreach (var (key, value) in appends)
            {
                sb.Append(first ? "" : ",").Append(sep).Append(JsonSerializer.Serialize(key)).Append(tail).Append(ValueText(value));
                first = false;
                changed.Add(key);
            }
            outBody.AddRange(Encoding.UTF8.GetBytes(sb.ToString()));
            outBody.AddRange(keep.Count > 0 ? keep : (sep.Contains('\n') ? Encoding.UTF8.GetBytes("\n") : Array.Empty<byte>()));
        }
        outBody.AddRange(body.Slice(closeAt).ToArray());

        var result = new byte[bom + outBody.Count];
        if (bom > 0) Array.Copy(original, result, bom);
        outBody.CopyTo(result, bom);
        why = "ok";
        return result;
    }

    /// <summary>A brand-new file holding only these keys (no file existed).</summary>
    public static byte[] Create(IReadOnlyList<Edit> edits)
    {
        var o = new JsonObject();
        foreach (var e in edits) if (!string.IsNullOrEmpty(e.Key) && !o.ContainsKey(e.Key)) o[e.Key] = e.Value?.DeepClone();
        return Encoding.UTF8.GetBytes(o.ToJsonString());
    }

    /// <summary>Read, edit, write atomically. A missing file is created only when <paramref name="createIfMissing"/>.</summary>
    public static Result Update(string path, IReadOnlyList<Edit> edits, bool createIfMissing = true)
    {
        try
        {
            if (!File.Exists(path))
            {
                if (!createIfMissing) return new Result(Outcome.Refused, "no settings file", Array.Empty<string>());
                WriteAtomic(path, Create(edits));
                return new Result(Outcome.Created, "created", edits.Select(e => e.Key).Distinct().ToList());
            }
            byte[] cur = File.ReadAllBytes(path);
            byte[]? next = Apply(cur, edits, out string why, out var changed);
            if (next is null)
                return why == "nothing to change" ? new Result(Outcome.Unchanged, why, Array.Empty<string>())
                                                  : new Result(Outcome.Refused, why, Array.Empty<string>());
            WriteAtomic(path, next);
            return new Result(Outcome.Written, "ok", changed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result(Outcome.Refused, ex.GetType().Name + ": " + ex.Message, Array.Empty<string>());
        }
    }

    /// <summary>The file's root object (null: missing or unreadable). Reading never changes anything.</summary>
    public static JsonObject? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            byte[] b = File.ReadAllBytes(path);
            int bom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            return JsonNode.Parse(new ReadOnlySpan<byte>(b, bom, b.Length - bom).ToArray(),
                                  documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>Temp file in the same folder, flushed, then moved over the target in one step.</summary>
    public static void WriteAtomic(string path, byte[] bytes)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string tmp = Path.Combine(dir, Path.GetFileName(path) + ".tmp-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes);
            fs.Flush(true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    // ---------------------------------------------------------------- the scan

    private readonly record struct Prop(string Name, int Start, int End, int NameStart);

    private static bool Scan(ReadOnlySpan<byte> body, out List<Prop> props, out int closeAt, out bool empty, out string why)
    {
        props = new List<Prop>();
        closeAt = -1;
        empty = true;
        try
        {
            var r = new Utf8JsonReader(body, ReadOptions);
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject) { why = "the file is not one JSON object"; return false; }
            while (r.Read())
            {
                if (r.TokenType == JsonTokenType.EndObject && r.CurrentDepth == 0)
                {
                    closeAt = (int)r.TokenStartIndex;
                    break;
                }
                if (r.TokenType != JsonTokenType.PropertyName || r.CurrentDepth != 1) { why = "unexpected token at the root"; return false; }
                string name = r.GetString() ?? "";
                int nameStart = (int)r.TokenStartIndex;
                if (!r.Read()) { why = "truncated"; return false; }
                int start = (int)r.TokenStartIndex;
                if (r.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) r.Skip();
                int end = (int)r.BytesConsumed;
                props.Add(new Prop(name, start, end, nameStart));
                empty = false;
            }
            if (closeAt < 0) { why = "the root object never closes"; return false; }
            // nothing but whitespace may follow the root object
            var rest = body.Slice(closeAt + 1);
            foreach (byte c in rest) if (c is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) { why = "text after the root object"; return false; }
            why = "ok";
            return true;
        }
        catch (JsonException ex) { why = "unreadable JSON: " + ex.Message; return false; }
    }

    /// <summary>How the file separates its keys: compact (",") or one per line with its indent.</summary>
    private static string Separator(ReadOnlySpan<byte> body, List<Prop> props, int closeAt, out string afterColon)
    {
        afterColon = ":";
        if (props.Count == 0) return "";
        var p = props[^1];
        // the whitespace before the last key's name
        int i = p.NameStart - 1;
        while (i >= 0 && body[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i--;
        string lead = Encoding.UTF8.GetString(body.Slice(i + 1, p.NameStart - i - 1));
        // the text between the name and its value (": " or ":")
        int nameEnd = p.NameStart + 1;
        bool esc = false;
        while (nameEnd < body.Length)
        {
            byte c = body[nameEnd];
            if (esc) esc = false;
            else if (c == (byte)'\\') esc = true;
            else if (c == (byte)'"') break;
            nameEnd++;
        }
        afterColon = Encoding.UTF8.GetString(body.Slice(nameEnd + 1, p.Start - nameEnd - 1));
        if (!afterColon.Contains(':')) afterColon = ":";
        return lead;
    }

    private static int TrailingWhitespace(List<byte> b)
    {
        int n = 0;
        for (int i = b.Count - 1; i >= 0 && b[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'; i--) n++;
        return n;
    }

    private static string ValueText(JsonNode? v) => v is null ? "null" : v.ToJsonString();

    private static bool SameValue(byte[] currentText, JsonNode? want)
    {
        try
        {
            var cur = JsonNode.Parse(currentText);
            return JsonNode.DeepEquals(cur, want);
        }
        catch (JsonException) { return false; }
    }
}
