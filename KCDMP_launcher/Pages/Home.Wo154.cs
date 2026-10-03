// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Collections.Generic;
using System.Linq;
using KCDMP_launcher.Models;
using KcdMp.Wire;
using Serilog;

namespace KCDMP_launcher.Pages
{
    /// <summary>
    /// WO-154: the launcher's last work order before the public beta.
    ///
    /// The player's files. settings.json, custom_servers.json and favorites.json belong to the
    /// player (the maintainer's rule, permanent): every write is the player's own change, key by
    /// key or entry by entry, atomically; nothing is reset, reordered or dropped, unknown keys
    /// stay (LauncherSettingsStore, PlayerListFiles). The log names keys, never values.
    /// </summary>
    public partial class Home
    {
        private readonly LauncherSettingsStore settingsStore = new(SettingsFileName, MigrateStaleSettings);

        /// <summary>
        /// Writes what the player changed (all keys, or only <paramref name="only"/>). A file that is
        /// not one JSON object is never written over: the player is told, in plain words, and the
        /// change stays in memory for this run. True when the file has the change.
        /// </summary>
        private bool WriteSettings(string why, params string[] only)
        {
            var r = settingsStore.Save(settings, only.Length == 0 ? null : only);
            if (r.Wrote)
                Log.Information("settings: {Why}: wrote {Set}{Filled}", why, string.Join(", ", r.Set),
                    r.Filled.Count == 0 ? "" : $" (and added the missing keys {string.Join(", ", r.Filled)})");
            else if (!r.Ok)
            {
                Log.Warning("settings: {Why}: settings.json not written -- {Reason}", why, r.Result.Why);
                UiService.ShowError("Your settings file (settings.json) couldn't be read, so the launcher did not change it. " +
                                    "Your change is used until the launcher closes. To keep it, fix or delete settings.json in the install folder, then change it again.");
            }
            return r.Ok;
        }

        /// <summary>The settings window opens: values another program wrote (the mod menu, through the agent) show as saved.</summary>
        private void OpenSettings()
        {
            var taken = settingsStore.Refresh(settings);
            if (taken.Count > 0) Log.Information("settings: changed outside the launcher since it read them: {Keys}", string.Join(", ", taken));
            showSettings = true;
        }

        /// <summary>The settings window closes without SAVE: its unsaved edits go back.</summary>
        private void CancelSettings()
        {
            var reverted = settingsStore.Revert(settings);
            if (reverted.Count > 0) Log.Debug("settings: closed without saving; {Keys} put back", string.Join(", ", reverted));
            showSettings = false;
        }

        // ------------------------------------------------------------ custom_servers.json, favorites.json

        private enum ServerChange { Add, Edit, Remove }

        // Each listed server's address and port as the file has them: an edit changes the address
        // in memory before it reaches here, and the file's entry is found by the old one.
        private readonly Dictionary<ServerInfo, (string Ip, int Port)> customServerKeys = new(ReferenceEqualityComparer.Instance);

        private void NoteCustomServerKeys()
        {
            customServerKeys.Clear();
            foreach (var s in customServers) customServerKeys[s] = (s.Ip, s.Port);
        }

        private void SaveCustomServer(ServerInfo server, ServerChange change)
        {
            var key = customServerKeys.TryGetValue(server, out var k) ? k : (server.Ip, server.Port);
            var r = PlayerListFiles.Update(CustomServersFileName, (byte[]? cur, out string w) => change switch
            {
                ServerChange.Add => PlayerListFiles.ApplyServerAdd(cur, server, out w),
                ServerChange.Edit => PlayerListFiles.ApplyServerEdit(cur, key.Ip, key.Port, server, out w),
                _ => PlayerListFiles.ApplyServerRemove(cur, key.Ip, key.Port, out w),
            });
            if (change == ServerChange.Remove) customServerKeys.Remove(server);
            else if (r.Ok) customServerKeys[server] = (server.Ip, server.Port);
            ReportListWrite("custom_servers.json", $"server {change.ToString().ToLowerInvariant()}", r);
        }

        private void SaveFavorite(string address, bool add)
        {
            var r = PlayerListFiles.Update(FavoritesFileName, (byte[]? cur, out string w) => PlayerListFiles.ApplyFavorite(cur, address, add, out w));
            ReportListWrite("favorites.json", add ? "star set" : "star cleared", r);
        }

        private void ReportListWrite(string file, string what, SettingsJson.Result r)
        {
            if (r.Outcome is SettingsJson.Outcome.Written or SettingsJson.Outcome.Created)
                Log.Information("{File}: {What}: written", file, what);
            else if (!r.Ok)
            {
                Log.Warning("{File}: {What}: not written -- {Reason}", file, what, r.Why);
                UiService.ShowError($"Your {file} couldn't be read, so the launcher did not change it. " +
                                    "The change is used until the launcher closes. To keep it, fix or delete that file in the install folder, then make the change again.");
            }
        }
    }
}
