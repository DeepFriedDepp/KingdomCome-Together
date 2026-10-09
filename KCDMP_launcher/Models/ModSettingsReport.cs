// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KCDMP_launcher.Models
{
    /// <summary>
    /// WO-154: what of the mod menu's mod-settings.json (beside the agent) goes into a bug report -- the
    /// menu's own choices only: the keys it remembers (KcdMp.Client.Wo154MenuRules.Settings; a test keeps the
    /// two lists equal), each with a JSON bool or "joint"/"individual". Anything else in the file -- a key or
    /// a value typed by hand, which could be a name, a path, an id or an address -- is left out and only
    /// counted. Null: no file, or not one JSON object.
    /// </summary>
    public static class ModSettingsReport
    {
        public static readonly string[] KnownKeys =
        {
            "NameBadges", "PingLine", "CleanScreen", "FriendlyFire", "CrimeMode",
            "FastTravel", "Leash", "SleepVote", "Whistle", "PartnerHerbs", "PartnerMarker",
        };

        public static string? Filter(byte[]? file)
        {
            if (file is null) return null;
            int bom = file.Length >= 3 && file[0] == 0xEF && file[1] == 0xBB && file[2] == 0xBF ? 3 : 0;
            try
            {
                using var doc = JsonDocument.Parse(file.AsMemory(bom), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                var o = new JsonObject();
                int left = 0;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    bool known = KnownKeys.Contains(p.Name, StringComparer.Ordinal) && !o.ContainsKey(p.Name);
                    JsonNode? v = p.Value.ValueKind switch
                    {
                        JsonValueKind.True => JsonValue.Create(true),
                        JsonValueKind.False => JsonValue.Create(false),
                        JsonValueKind.String when p.Value.GetString() is "joint" or "individual" => JsonValue.Create(p.Value.GetString()),
                        _ => null,
                    };
                    if (known && v is not null) o[p.Name] = v;
                    else left++;
                }
                if (left > 0) o["_left_out"] = left;
                return o.ToJsonString();
            }
            catch (JsonException) { return null; }
        }
    }
}
