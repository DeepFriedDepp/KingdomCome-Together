// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KcdMp.Wire;

namespace KCDMP_launcher.Models
{
    /// <summary>
    /// WO-154: the launcher's settings.json belongs to the player (the maintainer's rule,
    /// permanent). Everything the launcher writes into it goes through here, and from here
    /// through <see cref="SettingsJson"/>, key by key:
    /// <list type="bullet">
    /// <item>the values as loaded (and as last written) are the baseline; a save writes only the
    ///   keys whose value differs from it -- the player's change;</item>
    /// <item>only when it writes anyway, a save also adds the keys the file lacks (a fill never
    ///   replaces a value) -- new settings arrive without touching existing ones;</item>
    /// <item>nothing changed, nothing is written: a load and a save leave the file's bytes as
    ///   they were;</item>
    /// <item>every other byte stays: unknown keys, their order, the layout, a byte-order mark;</item>
    /// <item>a file that is not one JSON object is never written over;</item>
    /// <item>the file is read again at each save and when the settings window opens, so a value
    ///   another program wrote meanwhile (the agent writes the voice keys from the mod menu) is
    ///   kept, and shown, unless the player changed that key here;</item>
    /// <item>the launcher's own corrections in memory (a known stale default migrated, a game path
    ///   setup found for a path that does not work) are its reading of the file, not the player's
    ///   change: they are not written unless the player changes that key.</item>
    /// </list>
    /// Until 0.44.0 every save serialised the whole AppSettings over the file: unknown keys were
    /// lost and every value was rewritten -- which is also why a stored VoiceChatEnabled=true
    /// cannot be told from the old default (KcdMp.Wire.VoiceSetting).
    /// </summary>
    public sealed class LauncherSettingsStore
    {
        /// <summary>One save: what the file was given, by kind.</summary>
        public sealed record SaveResult(SettingsJson.Result Result, IReadOnlyList<string> Set, IReadOnlyList<string> Filled)
        {
            public bool Ok => Result.Ok;
            public bool Wrote => Result.Outcome is SettingsJson.Outcome.Written or SettingsJson.Outcome.Created;
        }

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// The keys the launcher knows, in AppSettings' declaration order: its public read/write
        /// properties minus [JsonIgnore], by their JSON names -- what the old whole-object save wrote.
        /// </summary>
        public static IReadOnlyList<(string Name, PropertyInfo Prop)> Keys { get; } =
            typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                            && p.GetSetMethod() is not null && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                .Select(p => (p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name, p))
                .ToArray();

        private readonly string _path;
        private readonly Action<AppSettings>? _migrate;
        // The value of each key the launcher holds as "not changed by the player" (as loaded and
        // corrected, or as last written), and the value the file itself held when last read or
        // written (absent: the file lacked the key, or it did not read as its type). Both are the
        // value serialised the way the old whole-object save wrote it.
        private readonly Dictionary<string, string> _known = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _disk = new(StringComparer.Ordinal);

        /// <param name="path">settings.json (the launcher's working directory, as since the first release).</param>
        /// <param name="migrate">The launcher's own corrections of known stale values, applied after every read.</param>
        public LauncherSettingsStore(string path, Action<AppSettings>? migrate = null)
        {
            _path = path;
            _migrate = migrate;
            var defaults = new AppSettings();
            foreach (var (name, prop) in Keys) _known[name] = Canon(prop, prop.GetValue(defaults));   // until Load
        }

        public string FilePath => _path;

        /// <summary>
        /// The settings as the file holds them. A missing file gives the defaults; a file that is
        /// not one JSON object gives the defaults and is never written over; a value that does not
        /// read as its type keeps its default (the file keeps its value). Each such case is added
        /// to <paramref name="problems"/> -- key names only, never a value.
        /// </summary>
        public AppSettings Load(List<string>? problems = null)
        {
            var s = new AppSettings();
            var disk = ReadRoot(_path, out string? why);
            if (why is not null) problems?.Add($"settings.json could not be read ({why}); the defaults are used and the file is left as it is");
            _disk.Clear();
            if (disk is not null)
                foreach (var name in ApplyFrom(disk, s, problems))
                    _disk[name] = Canon(Prop(name), Prop(name).GetValue(s));
            _migrate?.Invoke(s);
            _known.Clear();
            foreach (var (name, prop) in Keys) _known[name] = Canon(prop, prop.GetValue(s));
            return s;
        }

        /// <summary>The keys whose value in <paramref name="now"/> differs from what the launcher holds as unchanged.</summary>
        public IReadOnlyList<string> Changed(AppSettings now) =>
            Keys.Where(k => Canon(k.Prop, k.Prop.GetValue(now)) != _known[k.Name]).Select(k => k.Name).ToList();

        /// <summary>
        /// The merge-save: the player's changes (all, or only the keys in <paramref name="only"/>)
        /// as Set edits, plus Fill edits for the keys the file lacks -- only when there is a change
        /// to write. Nothing changed: nothing is read or written.
        /// </summary>
        public SaveResult Save(AppSettings now, IEnumerable<string>? only = null)
        {
            var scope = only is null ? null : new HashSet<string>(only, StringComparer.Ordinal);
            var changed = Changed(now).Where(k => scope is null || scope.Contains(k)).ToList();
            if (changed.Count == 0)
                return new SaveResult(new SettingsJson.Result(SettingsJson.Outcome.Unchanged, "nothing the player changed", Array.Empty<string>()),
                                      Array.Empty<string>(), Array.Empty<string>());

            // Read again: another program may have written the file since it was loaded.
            var disk = ReadRoot(_path, out string? why);
            if (why is not null)
                return new SaveResult(new SettingsJson.Result(SettingsJson.Outcome.Refused, why, Array.Empty<string>()),
                                      Array.Empty<string>(), Array.Empty<string>());

            var edits = new List<SettingsJson.Edit>();
            var set = new List<string>();
            var filled = new List<string>();
            foreach (var (name, prop) in Keys)
            {
                var value = JsonNode.Parse(Canon(prop, prop.GetValue(now)));
                if (changed.Contains(name)) { edits.Add(SettingsJson.Edit.Set(name, value)); set.Add(name); }
                else if (disk is null || !disk.ContainsKey(name)) { edits.Add(SettingsJson.Edit.Fill(name, value)); filled.Add(name); }
            }
            var r = SettingsJson.Update(_path, edits, createIfMissing: true);
            if (r.Ok)
            {
                foreach (var name in set) _known[name] = _disk[name] = Canon(Prop(name), Prop(name).GetValue(now));
                foreach (var name in filled) _disk[name] = Canon(Prop(name), Prop(name).GetValue(now));
            }
            return new SaveResult(r, set, r.Ok ? filled : Array.Empty<string>());
        }

        /// <summary>
        /// The settings window opens: a value another program wrote into the file since it was
        /// last read is taken into <paramref name="now"/>, for every key the player has not changed
        /// here (a pending change of the player's wins at the next save). Returns the keys taken.
        /// </summary>
        public IReadOnlyList<string> Refresh(AppSettings now)
        {
            var disk = ReadRoot(_path, out _);
            if (disk is null) return Array.Empty<string>();
            var raw = new AppSettings();
            var read = ApplyFrom(disk, raw, null);
            var corrected = new AppSettings();
            ApplyFrom(disk, corrected, null);
            _migrate?.Invoke(corrected);

            var taken = new List<string>();
            foreach (var (name, prop) in Keys)
            {
                if (!read.Contains(name)) continue;   // a key the file lacks, or cannot be read, says nothing new
                string onDisk = Canon(prop, prop.GetValue(raw));
                if (_disk.TryGetValue(name, out var before) && before == onDisk) continue;   // the file did not change it
                _disk[name] = onDisk;
                if (Canon(prop, prop.GetValue(now)) != _known[name]) continue;               // the player's pending change wins
                prop.SetValue(now, prop.GetValue(corrected));
                _known[name] = Canon(prop, prop.GetValue(now));
                taken.Add(name);
            }
            return taken;
        }

        /// <summary>The settings window's CANCEL: the player's unsaved changes go back to what the launcher holds. Returns the keys reverted.</summary>
        public IReadOnlyList<string> Revert(AppSettings now)
        {
            var reverted = new List<string>();
            foreach (var (name, prop) in Keys)
            {
                if (Canon(prop, prop.GetValue(now)) == _known[name]) continue;
                prop.SetValue(now, JsonSerializer.Deserialize(_known[name], prop.PropertyType));
                reverted.Add(name);
            }
            return reverted;
        }

        /// <summary>The launcher's own correction of these keys, held in memory only: not the player's change, never written for it.</summary>
        public void Accept(AppSettings now, params string[] keys)
        {
            foreach (var name in keys) _known[name] = Canon(Prop(name), Prop(name).GetValue(now));
        }

        /// <summary>
        /// The file does not give this key a value: no file, no key, JSON null, or an empty string.
        /// Filling such a key is the file gaining a value; nothing the player set is replaced.
        /// </summary>
        public bool IsUnsetOnDisk(string key)
        {
            var disk = ReadRoot(_path, out string? why);
            if (why is not null) return false;   // unreadable: never written over anyway
            if (disk is null || !disk.TryGetValue(key, out var el)) return true;
            return el.ValueKind == JsonValueKind.Null
                || (el.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(el.GetString()));
        }

        // ------------------------------------------------------------ helpers

        private static PropertyInfo Prop(string name) => Keys.First(k => k.Name == name).Prop;

        /// <summary>A value serialised the way the old whole-object save wrote it (default options).</summary>
        private static string Canon(PropertyInfo prop, object? value) => JsonSerializer.Serialize(value, prop.PropertyType);

        /// <summary>
        /// The file's root object as key -> value (a repeated key: the last one, as SettingsJson
        /// edits it). Null with <paramref name="why"/> null: no file. Null with a reason: the file
        /// is there but is not one JSON object -- it is never written over.
        /// </summary>
        internal static Dictionary<string, JsonElement>? ReadRoot(string path, out string? why)
        {
            why = null;
            try
            {
                if (!File.Exists(path)) return null;
                byte[] b = File.ReadAllBytes(path);
                int bom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
                using var doc = JsonDocument.Parse(b.AsMemory(bom), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                if (doc.RootElement.ValueKind != JsonValueKind.Object) { why = "not one JSON object"; return null; }
                var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var p in doc.RootElement.EnumerateObject()) d[p.Name] = p.Value.Clone();
                return d;
            }
            catch (JsonException) { why = "not one JSON object"; return null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { why = ex.GetType().Name; return null; }
        }

        /// <summary>The file's values into <paramref name="target"/>, key by key. Returns the keys that read as their type.</summary>
        private static HashSet<string> ApplyFrom(Dictionary<string, JsonElement> disk, AppSettings target, List<string>? problems)
        {
            var read = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, prop) in Keys)
            {
                if (!disk.TryGetValue(name, out var el)) continue;
                if (el.ValueKind == JsonValueKind.Null) continue;   // null: not set, the default stays
                try
                {
                    var v = el.Deserialize(prop.PropertyType, ReadOptions);
                    if (v is null) continue;
                    prop.SetValue(target, v);
                    read.Add(name);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
                {
                    problems?.Add($"{name} does not read as {prop.PropertyType.Name}; the default is used and the file keeps its value");
                }
            }
            return read;
        }
    }
}
