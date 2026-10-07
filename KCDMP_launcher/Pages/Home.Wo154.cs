// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KCDMP_launcher.Components.Shared;
using KCDMP_launcher.Models;
using KcdMp.Wire;
using Serilog;

namespace KCDMP_launcher.Pages
{
    /// <summary>
    /// WO-154: the launcher's last work order before the public beta.
    /// <list type="bullet">
    /// <item>The player's files. settings.json, custom_servers.json and favorites.json belong to
    ///   the player (the maintainer's rule, permanent): every write is the player's own change, key
    ///   by key or entry by entry, atomically; nothing is reset, reordered or dropped, unknown keys
    ///   stay (LauncherSettingsStore, PlayerListFiles). The log names keys, never values.</item>
    /// <item>Windows blocking the mod (Phase 7): every start goes through StartChecked; a refused
    ///   or quarantined file is one plain message by its name (LaunchBlock).</item>
    /// <item>CONNECT waits until it can work (Phase 4.3), with the reason under it (ConnectGate).</item>
    /// <item>The relay's ports held by an earlier launcher's relay (Phase 7): reused or replaced,
    ///   another program's never touched (RelayReuse).</item>
    /// </list>
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
                UiService.ShowError(NotWrittenText("settings.json", r.Result.Why));
            }
            return r.Ok;
        }

        /// <summary>A player's file the launcher left as it is, in plain words: unreadable (never written over) or not writable.</summary>
        private static string NotWrittenText(string file, string why) =>
            why.StartsWith("not one JSON", StringComparison.Ordinal) || why.StartsWith("unreadable JSON", StringComparison.Ordinal)
            || why.StartsWith("the file is not one JSON", StringComparison.Ordinal) || why.StartsWith("unexpected token", StringComparison.Ordinal)
            || why.StartsWith("the root object", StringComparison.Ordinal) || why.StartsWith("text after", StringComparison.Ordinal) || why == "truncated"
                ? $"Your {file} couldn't be read, so the launcher did not change it. Your change is used until the launcher closes. " +
                  $"To keep it, fix or delete {file} in the install folder, then make the change again."
                : $"Windows didn't let the launcher save {file} (it may be read-only, or open in another program). Your change is used " +
                  "until the launcher closes. Check the file, then make the change again.";

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
                UiService.ShowError(NotWrittenText(file, r.Why));
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
                // An empty or malformed path (one blanked in Settings) is a missing file, not an exception.
                string name = "a file whose path is empty in Settings";
                bool exists = false;
                long length = 0;
                try
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var fi = new FileInfo(path);
                        name = fi.Name;
                        exists = fi.Exists;
                        length = exists ? fi.Length : 0;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException) { }
                if (LaunchBlocks.FromFileCheck(name, exists, length, installed) is { } b)
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
        private Dictionary<string, KcdMp.Setup.ManifestEntry>? installedEntries;   // WO-157: sizes and hashes, by kind|rel

        private void ReadInstallManifest()
        {
            if (installedApp is not null) return;
            installedApp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            installedMod = new List<string>();
            installedEntries = new Dictionary<string, KcdMp.Setup.ManifestEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string manifest = Path.Combine(AppContext.BaseDirectory, KcdMp.Setup.ModInstall.ManifestName);
                if (!File.Exists(manifest)) return;
                foreach (var e in KcdMp.Setup.ModInstall.ReadManifest(manifest))
                {
                    installedEntries[e.Kind + "|" + e.Rel] = e;
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

        // ------------------------------------------------------------ CONNECT waits until it can work (Phase 4.3)
        //
        // The rule is ConnectGateRule (Models/ConnectGate.cs): the host's button opens when the game's own
        // kcd.log says the world is loaded and has been for ConnectGateRule.SettleS; the joiner's when the
        // host's relay says the host's agent is in. The reason is shown in plain words under the button.
        // Passive: the launcher reads a log and asks the relay -- no key, click or focus change, ever.

        private bool connectGateOpen = true;
        private string connectGateReason = "";
        private CancellationTokenSource? connectGateCts;

        /// <summary>The CONNECT button's state: always on with the ConnectGate setting off.</summary>
        private bool ConnectEnabled => !settings.ConnectGate || connectGateOpen;

        /// <summary>From LaunchGame, once the game is up: the gate for this launch.</summary>
        private void StartConnectGate(bool hosting, ServerInfo server, DateTime gameStartLocal)
        {
            StopConnectGate();
            connectGateOpen = true;
            connectGateReason = "";
            if (!settings.ConnectGate) return;
            connectGateOpen = false;
            connectGateReason = hosting ? ConnectGateRule.LoadSaveFirst : ConnectGateRule.HostNotReady;
            Log.Information("MP-LAUNCH connect gate closed ({Who}) -- {Reason}", hosting ? "host: the game's log" : "joiner: the host's relay", connectGateReason);
            var cts = connectGateCts = new CancellationTokenSource();
            _ = hosting ? HostGateLoopAsync(gameStartLocal, cts.Token) : JoinerGateLoopAsync(server, cts.Token);
        }

        private void StopConnectGate()
        {
            connectGateCts?.Cancel();
            connectGateCts = null;
        }

        private async Task HostGateLoopAsync(DateTime gameStartLocal, CancellationToken ct)
        {
            var clock = Stopwatch.StartNew();
            KcdLogFollower? follower = null;
            double nextFind = 0;
            while (!ct.IsCancellationRequested)
            {
                double now = clock.Elapsed.TotalSeconds;
                if (follower is null && now >= nextFind)
                {
                    string root = LogBundleGameRoot;
                    if (await Task.Run(() => LogBundle.FindKcdLog(root)) is string path) follower = new KcdLogFollower(path, gameStartLocal);
                    else nextFind = now + 5;
                }
                if (follower is not null) await Task.Run(() => follower.Poll(now));
                bool reading = follower?.Reading == true;
                var v = ConnectGateRule.ForHost(follower?.State.Stage ?? GameStage.Starting, follower?.State.WorldSinceS ?? double.NaN,
                                            reading, (DateTime.Now - gameStartLocal).TotalSeconds, now);
                await SetGateAsync(v, reading ? $"game {follower!.State.Stage.ToString().ToLowerInvariant()}" : "the game's log is not readable yet", ct);
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }

        private async Task JoinerGateLoopAsync(ServerInfo server, CancellationToken ct)
        {
            string agent = ResolveAgainstLauncher(settings.AgentPath);
            string args = server.SteamCode is null
                ? $"--test-connection --host {server.Ip} --port {server.Port}"
                : $"--test-connection --steam \"{server.SteamCode}\" --steam-app {settings.SteamAppId}{SteamGameArg}";
            while (!ct.IsCancellationRequested)
            {
                TestConnectionData? r = null;
                try { r = await AgentHelper.RunAsync<TestConnectionData>(agent, args, "TEST-CONNECTION", TimeSpan.FromSeconds(45)); }
                catch (Exception ex) { Log.Debug("MP-LAUNCH connect gate: the host check did not run ({Kind})", ex.GetType().Name); }
                if (ct.IsCancellationRequested) break;
                var answer = r is null ? HostAnswer.Unknown : ConnectGateRule.FromTest(r.Reachable, r.Kind, r.HostConnected);
                var v = ConnectGateRule.ForJoiner(answer);
                await SetGateAsync(v, $"host {answer.ToString().ToLowerInvariant()}", ct);
                if (v.Open) break;   // the host is in (or the relay cannot say): CONNECT stays open
                try { await Task.Delay(server.SteamCode is null ? 8000 : 12000, ct); } catch (OperationCanceledException) { break; }
            }
        }

        private async Task SetGateAsync(GateView v, string detail, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;   // CONNECT was pressed (or the launch ended): this loop no longer decides
            if (v.Open == connectGateOpen && v.Reason == connectGateReason) return;
            connectGateOpen = v.Open;
            connectGateReason = v.Reason;
            Log.Information("MP-LAUNCH connect gate {State} ({Detail}){Reason}", v.Open ? "open" : "closed", detail, v.Open ? "" : " -- " + v.Reason);
            await InvokeAsync(StateHasChanged);
        }

        // ------------------------------------------------------------ the relay's ports (Phase 7)

        /// <summary>Who holds the relay's two ports (TCP Host Port, HTTP Server Info Port), and what our own relay there says.</summary>
        private async Task<RelayDecision> DecideRelayAsync(string relayPath)
        {
            var holders = await Task.Run(() => RelayReuse.Holders(relayPath, settings.HostPort, settings.ServerInfoPort));
            RelayAnswer? answer = holders.Count > 0 && holders.All(h => h.Ours) ? await AskRelayAsync() : null;
            return RelayReuse.Decide(holders, settings.HostPort, answer, Globals.Version, settings.HostAllowSteam, settings.SteamAppId);
        }

        private async Task<RelayAnswer?> AskRelayAsync()
        {
            var st = await NetService.GetRelayLocalStatusAsync(settings.ServerInfoPort);
            return st is null ? null : new RelayAnswer(st.Release, st.Steam.State, st.Steam.AppId);
        }

        /// <summary>Carries a decision out. True: start this install's relay now.</summary>
        private async Task<bool> ApplyRelayDecisionAsync(RelayDecision d)
        {
            switch (d.Plan)
            {
                case RelayPlan.Reuse:
                    try
                    {
                        hostedRelayProcess = Process.GetProcessById(d.Pid);
                        Log.Information(d.LogLine);
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        Log.Information("MP-RELAY the relay to reuse (pid {Pid}) has just exited; starting this install's", d.Pid);
                        return true;
                    }
                case RelayPlan.Replace:
                    Log.Information(d.LogLine);
                    await Task.Run(() => { foreach (int pid in d.Pids) StopOldRelay(pid); });   // off the window's thread
                    await WaitPortsFreeAsync(TimeSpan.FromSeconds(5));
                    return true;
                case RelayPlan.Refuse:
                    Log.Warning(d.LogLine);
                    hostErrorMessage = d.PlayerMessage;
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>One process of this install's own relay (Decide named it ours by its executable): stopped, waited for.</summary>
        private static void StopOldRelay(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill();
                if (!p.WaitForExit(5000)) Log.Warning("MP-RELAY the old relay (pid {Pid}) did not exit within 5 s", pid);
            }
            catch (ArgumentException) { }   // gone already
            catch (Exception ex) { Log.Warning("MP-RELAY the old relay (pid {Pid}) could not be stopped: {Kind}", pid, ex.GetType().Name); }
        }

        private async Task WaitPortsFreeAsync(TimeSpan max)
        {
            var until = DateTime.UtcNow + max;
            while (DateTime.UtcNow < until)
            {
                if (PortOwners.ListeningPids(settings.HostPort).Count == 0 && PortOwners.ListeningPids(settings.ServerInfoPort).Count == 0) return;
                await Task.Delay(200);
            }
        }

        /// <summary>A just-started relay is up when it answers; it may also exit (a port it could not bind). At most 8 s.</summary>
        private async Task WaitForRelayAsync(Process relay)
        {
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            await Task.Delay(300);
            while (DateTime.UtcNow < until)
            {
                if (relay.HasExited) return;
                if (await AskRelayAsync() is not null) return;
                await Task.Delay(250);
            }
            Log.Warning("MP-RELAY the relay did not answer within 8 s; it keeps running");
        }

        /// <summary>The relay died at start: which port, and whether another program holds it, in plain words.</summary>
        private string RelayExitedMessage(string relayPath)
        {
            var holders = RelayReuse.Holders(relayPath, settings.HostPort, settings.ServerInfoPort);
            var d = RelayReuse.Decide(holders, settings.HostPort, null, Globals.Version, settings.HostAllowSteam, settings.SteamAppId);
            if (d.Plan == RelayPlan.Refuse)
            {
                Log.Warning(d.LogLine);
                return d.PlayerMessage;
            }
            return $"Hosting stopped right after it started. Another copy may already be running on port {settings.HostPort}: close it (or restart the computer) and try again.";
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
