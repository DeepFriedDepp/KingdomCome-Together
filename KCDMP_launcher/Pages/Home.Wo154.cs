// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        // ------------------------------------------------------------ Windows blocking the mod (Phase 7)
        //
        // Every program the launcher starts for the mod goes through StartChecked: Windows refusing it
        // (Smart App Control / an App Control policy, a group policy, an antivirus verdict, a file an
        // antivirus took) becomes one plain message naming the file, never its path, with Report a bug
        // beside it, and one MP-LAUNCH line in the log. Before a launch, the files it needs are looked
        // at first (LaunchBlocks.FromFileCheck): a quarantine is told before Windows' own error.

        /// <summary>A start Windows refused; carries the classification to the caller's catch.</summary>
        private sealed class LaunchBlockedException(LaunchBlock block, Exception inner) : Exception(block.LogLine, inner)
        {
            public LaunchBlock Block { get; } = block;
        }

        private Process? StartChecked(ProcessStartInfo psi)
        {
            try { return Process.Start(psi); }
            catch (Exception ex) when (LaunchBlocks.FromStartFailure(ex, Path.GetFileName(psi.FileName), IsInstalledFile(psi.FileName)) is { } b)
            {
                throw new LaunchBlockedException(b, ex);
            }
        }

        private void ShowBlocked(LaunchBlock b)
        {
            Log.Warning(b.LogLine);
            Log.Information("MP-LAUNCH smart_app_control={State}", SmartAppControlState());
            ShowMessage(b.Title, b.Message, b.NextStep, reportBug: true);
        }

        /// <summary>
        /// The launch's own files, before anything starts: <paramref name="appFiles"/> (full paths) and the
        /// mod's files in &lt;game root&gt;\Mods\kdcmp as the install manifest lists them. True: told, stop.
        /// </summary>
        private bool BlockedBeforeLaunch(string gameRoot, params string[] appFiles)
        {
            var check = appFiles.Select(f => (Path: f, Installed: IsInstalledFile(f))).ToList();
            if (!string.IsNullOrWhiteSpace(gameRoot))
            {
                string modDir = Path.Combine(gameRoot, "Mods", "kdcmp");
                foreach (var rel in InstalledModFiles())
                    check.Add((Path.Combine(modDir, rel), true));
            }
            foreach (var (path, installed) in check)
            {
                var fi = new FileInfo(path);
                if (LaunchBlocks.FromFileCheck(fi.Name, fi.Exists, fi.Exists ? fi.Length : 0, installed) is { } b)
                {
                    ShowBlocked(b);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Runs the agent as a one-shot helper; a start Windows refused is told and gives (null, true).</summary>
        private async Task<(T? Result, bool Blocked)> RunHelperAsync<T>(string agentPath, string arguments, string tag, TimeSpan timeout) where T : class
        {
            try { return (await AgentHelper.RunAsync<T>(agentPath, arguments, tag, timeout), false); }
            catch (Exception ex) when (LaunchBlocks.FromStartFailure(ex, Path.GetFileName(agentPath), IsInstalledFile(agentPath)) is { } b)
            {
                ShowBlocked(b);
                return (null, true);
            }
        }

        // install-manifest.txt (Setup's own list, beside the launcher): a file it lists was installed, so a
        // missing one was taken (quarantine). A development build has no manifest: "missing" then.
        private HashSet<string>? installedApp;
        private List<string>? installedMod;

        private void ReadInstallManifest()
        {
            if (installedApp is not null) return;
            installedApp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            installedMod = new List<string>();
            try
            {
                string manifest = Path.Combine(AppContext.BaseDirectory, KcdMp.Setup.ModInstall.ManifestName);
                if (!File.Exists(manifest)) return;
                foreach (var e in KcdMp.Setup.ModInstall.ReadManifest(manifest))
                {
                    if (e.Kind == "APP") installedApp.Add(e.Rel);
                    else if (e.Kind == "MOD") installedMod.Add(e.Rel);
                }
            }
            catch (Exception ex) { Log.Warning("MP-LAUNCH install-manifest.txt could not be read ({Kind})", ex.GetType().Name); }
        }

        private bool IsInstalledFile(string fullPath)
        {
            ReadInstallManifest();
            string baseDir = Path.GetFullPath(AppContext.BaseDirectory);
            string full;
            try { full = Path.GetFullPath(fullPath); } catch { return false; }
            if (!full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) return false;
            return installedApp!.Contains(full[baseDir.Length..].TrimStart('\\', '/'));
        }

        private IReadOnlyList<string> InstalledModFiles()
        {
            ReadInstallManifest();
            return installedMod!;
        }

        /// <summary>Smart App Control's state, for the log beside a block (HKLM\...\CI\Policy VerifiedAndReputablePolicyState).</summary>
        private static string SmartAppControlState()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy");
                return key?.GetValue("VerifiedAndReputablePolicyState") switch
                {
                    0 => "off",
                    1 => "on",
                    2 => "evaluation",
                    null => "not-present",
                    var v => "value-" + v,
                };
            }
            catch { return "unknown"; }
        }
    }
}
