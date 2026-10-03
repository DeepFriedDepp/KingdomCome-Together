// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KcdMp.Wire;

namespace KCDMP_launcher.Models
{
    /// <summary>
    /// WO-154: favorites.json and custom_servers.json belong to the player too. Each of the
    /// player's actions -- a star set or cleared, a server added, edited or removed -- changes its
    /// own entry in the file's array and nothing else: the other entries, and the fields of an
    /// entry the launcher does not know, stay as the file has them. Nothing changed, nothing is
    /// written; a file that is not one JSON array is never written over; a write goes to a
    /// temporary file moved over the real one (<see cref="SettingsJson.WriteAtomic"/>).
    /// Until 0.44.0 each action serialised the launcher's whole in-memory list over the file --
    /// with the live ping and player counts of the moment in every entry.
    /// </summary>
    public static class PlayerListFiles
    {
        private static readonly JsonDocumentOptions ReadOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        // ------------------------------------------------------------ favorites.json: an array of addresses

        /// <summary>The file's text with <paramref name="address"/> starred or unstarred, or null: no change (or not an array: <paramref name="why"/>).</summary>
        public static byte[]? ApplyFavorite(byte[]? original, string address, bool add, out string why)
        {
            if (!TryParse(original, out var arr, out var layout, out why)) return null;
            int at = -1;
            for (int i = 0; i < arr.Count; i++) if (Str(arr[i]) == address) { at = i; break; }
            if (add)
            {
                if (at >= 0) { why = "already starred"; return null; }
                arr.Add(address);
            }
            else
            {
                if (at < 0) { why = "not starred"; return null; }
                for (int i = arr.Count - 1; i >= 0; i--) if (Str(arr[i]) == address) arr.RemoveAt(i);
            }
            why = "ok";
            return Write(arr, layout);
        }

        // ------------------------------------------------------------ custom_servers.json: an array of server objects

        /// <summary>A new server at the end, as the launcher has always written one; null when the address and port are listed already.</summary>
        public static byte[]? ApplyServerAdd(byte[]? original, ServerInfo server, out string why)
        {
            if (!TryParse(original, out var arr, out var layout, out why)) return null;
            foreach (var n in arr)
                if (IsServer(n, server.Ip, server.Port)) { why = "listed already"; return null; }
            arr.Add(JsonSerializer.SerializeToNode(server));
            why = "ok";
            return Write(arr, layout);
        }

        /// <summary>
        /// The player edited the server listed as <paramref name="oldIp"/>:<paramref name="oldPort"/>:
        /// the fields the edit window has (name, map, address, port) take the new values, every other
        /// field of the entry stays. An entry the file no longer has is added, so the edit is not lost.
        /// </summary>
        public static byte[]? ApplyServerEdit(byte[]? original, string oldIp, int oldPort, ServerInfo now, out string why)
        {
            if (!TryParse(original, out var arr, out var layout, out why)) return null;
            JsonObject? entry = null;
            foreach (var n in arr)
                if (IsServer(n, oldIp, oldPort)) { entry = (JsonObject)n!; break; }
            if (entry is null)
            {
                arr.Add(JsonSerializer.SerializeToNode(now));
            }
            else
            {
                var before = entry.ToJsonString();
                entry[nameof(ServerInfo.Name)] = now.Name;
                entry[nameof(ServerInfo.MapName)] = now.MapName;
                entry[nameof(ServerInfo.Ip)] = now.Ip;
                entry[nameof(ServerInfo.Port)] = now.Port;
                if (entry.ToJsonString() == before) { why = "nothing to change"; return null; }
            }
            why = "ok";
            return Write(arr, layout);
        }

        /// <summary>Every entry with this address and port goes; null when there is none.</summary>
        public static byte[]? ApplyServerRemove(byte[]? original, string ip, int port, out string why)
        {
            if (!TryParse(original, out var arr, out var layout, out why)) return null;
            bool any = false;
            for (int i = arr.Count - 1; i >= 0; i--)
                if (IsServer(arr[i], ip, port)) { arr.RemoveAt(i); any = true; }
            if (!any) { why = "not listed"; return null; }
            why = "ok";
            return Write(arr, layout);
        }

        // ------------------------------------------------------------ on disk

        /// <summary>Read, apply one action, write atomically. No file yet: the action starts it.</summary>
        public static SettingsJson.Result Update(string path, ApplyFn apply)
        {
            try
            {
                byte[]? cur = File.Exists(path) ? File.ReadAllBytes(path) : null;
                byte[]? next = apply(cur, out string why);
                if (next is null)
                {
                    bool refused = why.StartsWith("not one JSON array", StringComparison.Ordinal);
                    return new SettingsJson.Result(refused ? SettingsJson.Outcome.Refused : SettingsJson.Outcome.Unchanged, why, Array.Empty<string>());
                }
                SettingsJson.WriteAtomic(path, next);
                return new SettingsJson.Result(cur is null ? SettingsJson.Outcome.Created : SettingsJson.Outcome.Written, "ok", Array.Empty<string>());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new SettingsJson.Result(SettingsJson.Outcome.Refused, ex.GetType().Name, Array.Empty<string>());
            }
        }

        public delegate byte[]? ApplyFn(byte[]? original, out string why);

        // ------------------------------------------------------------ helpers

        private readonly record struct Layout(bool Bom, bool Indented);

        private static bool TryParse(byte[]? original, out JsonArray arr, out Layout layout, out string why)
        {
            arr = new JsonArray();
            layout = new Layout(false, false);
            why = "ok";
            if (original is null) return true;   // no file yet
            int bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF ? 3 : 0;
            try
            {
                using var doc = JsonDocument.Parse(original.AsMemory(bom), ReadOptions);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) { why = "not one JSON array"; return false; }
                arr = JsonArray.Create(doc.RootElement.Clone()) ?? new JsonArray();
                // Materialise now: a duplicate key inside an entry throws here, not halfway through an edit.
                foreach (var n in arr) if (n is JsonObject o) _ = o.Count;
                layout = new Layout(bom > 0, Array.IndexOf(original, (byte)'\n', bom) >= 0);
                return true;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
            {
                why = "not one JSON array we can read";
                return false;
            }
        }

        private static byte[] Write(JsonArray arr, Layout layout)
        {
            byte[] body = Encoding.UTF8.GetBytes(arr.ToJsonString(new JsonSerializerOptions { WriteIndented = layout.Indented }));
            if (!layout.Bom) return body;
            var b = new byte[body.Length + 3];
            b[0] = 0xEF; b[1] = 0xBB; b[2] = 0xBF;
            body.CopyTo(b, 3);
            return b;
        }

        private static string? Str(JsonNode? n)
        {
            try { return n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null; }
            catch (InvalidOperationException) { return null; }
        }

        private static bool IsServer(JsonNode? n, string ip, int port)
        {
            if (n is not JsonObject o) return false;
            if (!o.TryGetPropertyValue(nameof(ServerInfo.Ip), out var ipNode) || Str(ipNode) != ip) return false;
            if (!o.TryGetPropertyValue(nameof(ServerInfo.Port), out var portNode) || portNode is not JsonValue pv) return false;
            try { return pv.TryGetValue<int>(out int p) && p == port; }
            catch (InvalidOperationException) { return false; }
        }
    }
}
