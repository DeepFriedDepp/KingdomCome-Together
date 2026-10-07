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
using KcdMp.Setup;
using Serilog;

namespace KCDMP_launcher.Pages
{
    /// <summary>
    /// WO-150: painless first-run setup. The checks run quietly in the background
    /// as the launcher opens (manifests, link identities, file sizes: well under a
    /// second) and, when everything is in place, nothing visible happens at all.
    /// The checklist appears only when a check fails, and then the launcher works
    /// through the failing steps by itself: Steam's own install windows, the
    /// workspace links (Warhorse's tool through a pipe, else the launcher's own
    /// links, one UAC prompt only if Windows insists), the held-back mod.
    ///
    /// Never a keystroke, never a focus change, never a console window: the only
    /// windows that can appear are Steam's install dialog and Windows' UAC prompt.
    /// Every step's outcome goes to the launcher log, redacted (KcdMp.Setup.Redact),
    /// so a Report-a-bug zip shows where a setup stopped.
    /// </summary>
    public partial class Home
    {
        private bool showSetup;
        private IReadOnlyList<StepView> setupSteps = Array.Empty<StepView>();
        private SetupActivity setupActivity = SetupActivity.Idle;
        private SetupSnapshot? setupSnapshot;
        private bool setupReady;
        private bool setupBusy;
        private Task? setupFirstCheck;
        private CancellationTokenSource? setupPollCts;
        private readonly List<string> setupLines = new();
        private readonly Dictionary<StepId, string> setupLogged = new();
        private bool setupOpenedGameInstall, setupOpenedMtInstall, setupStartedSteam;
        private DateTime setupSteamStartedAt;

        private const int SetupLineCount = 6;
        private static readonly TimeSpan SetupPollPeriod = TimeSpan.FromSeconds(2);

        /// <summary>From OnInitializedAsync: starts the background check without delaying the launcher's normal screen.</summary>
        private void StartSetupCheck()
        {
            setupFirstCheck = Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                var snap = TakeSetupSnapshot();
                await InvokeAsync(() =>
                {
                    ApplySetupSnapshot(snap);
                    Log.Information("setup: checked in {Ms} ms: {State}", sw.ElapsedMilliseconds,
                        setupReady ? "ready, nothing to do" : "not ready, showing the checklist");
                    if (snap.Mod?.Warnings.Any() == true)   // warned, never blocking (ModReport.Ok)
                        Log.Warning("setup: installed files differ from what Setup shipped: {Files}", string.Join(", ", snap.Mod.Warnings.Take(8)));
                    AdoptDetectedGamePath(snap);
                    if (!setupReady)
                    {
                        showSetup = true;
                        StartSetupPoll();
                    }
                    else if (SetupChecklist.OptionalNeedsYou(setupSteps))
                    {
                        // WO-157: ready, but an optional step (the Visual C++ 2013 runtime) asks for the player:
                        // shown once at start; Host and Join are not held back by it.
                        Log.Information("setup: ready; an optional step needs the player, showing the checklist once");
                        showSetup = true;
                    }
                    StateHasChanged();
                });
            });
        }

        private SetupSnapshot TakeSetupSnapshot() => SetupProbe.Take(RealSetupHost.Instance, new SetupProbeOptions
        {
            ExplicitModdingToolsExe = settings.GamePath,
            AppDir = AppContext.BaseDirectory,
            OwnsModFolder = OwnsModFolder,
        });

        private void ApplySetupSnapshot(SetupSnapshot snap)
        {
            setupSnapshot = snap;
            setupSteps = SetupChecklist.Evaluate(snap, setupActivity);
            bool wasReady = setupReady;
            setupReady = SetupChecklist.IsReady(setupSteps);
            LogStepChanges();
            if (setupReady && !wasReady && setupLogged.Count > 0)
                Log.Information("setup: Ready! Host and Join unlocked");
        }

        /// <summary>Each step once per change of state (progress in 10% steps), so the log reads as a timeline.</summary>
        private void LogStepChanges()
        {
            foreach (var s in setupSteps)
            {
                string key = $"{s.Status}|{s.Action}|{(s.Progress is double p ? ((int)(p * 10)).ToString() : "")}";
                if (setupLogged.TryGetValue(s.Id, out var last) && last == key) continue;
                setupLogged[s.Id] = key;
                Log.Information("setup: step {Step} {Title}: {Status} -- {Detail}", (int)s.Id, s.Title, s.Status, Redact.Text(s.Detail));
            }
        }

        private void StartSetupPoll()
        {
            if (setupPollCts is { IsCancellationRequested: false }) return;
            var cts = setupPollCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        if (!setupBusy)
                        {
                            var snap = TakeSetupSnapshot();
                            await InvokeAsync(async () =>
                            {
                                ApplySetupSnapshot(snap);
                                AdoptDetectedGamePath(snap);
                                await RunSetupAutoActionsAsync();
                                StateHasChanged();
                            });
                            // Ready and the checklist closed: nothing left to watch.
                            if (setupReady && !showSetup) break;
                        }
                    }
                    catch (Exception ex) { Log.Warning(ex, "setup: a background check failed"); }
                    try { await Task.Delay(SetupPollPeriod, cts.Token); } catch (OperationCanceledException) { break; }
                }
            });
        }

        /// <summary>
        /// The steps the launcher starts by itself. Each Steam install window is
        /// opened once per launcher session (the step's button opens it again);
        /// linking and placing the mod start whenever the checklist says they can.
        /// </summary>
        private async Task RunSetupAutoActionsAsync()
        {
            var snap = setupSnapshot;
            if (snap is null || setupBusy) return;
            StepView S(StepId id) => setupSteps.First(x => x.Id == id);

            if (setupActivity.StartingSteam && (snap.SteamRunning || DateTime.UtcNow - setupSteamStartedAt > TimeSpan.FromSeconds(90)))
                SetActivity(setupActivity with { StartingSteam = false });

            if (S(StepId.Steam) is { Status: StepStatus.NeedsYou, Action: StepAction.StartSteam } && !setupStartedSteam)
            {
                setupStartedSteam = true;
                StartSteamQuietly(snap);
                return;
            }
            if (!snap.SteamRunning) return;

            if (S(StepId.Game) is { Status: StepStatus.NeedsYou, Action: StepAction.InstallGame } && !setupOpenedGameInstall)
            {
                setupOpenedGameInstall = true;
                OpenSteamInstall(SteamApp.Game);
                return;
            }
            if (S(StepId.ModdingTools) is { Status: StepStatus.NeedsYou, Action: StepAction.InstallModdingTools } && !setupOpenedMtInstall &&
                S(StepId.Game).Status is StepStatus.Done or StepStatus.Working)
            {
                setupOpenedMtInstall = true;
                OpenSteamInstall(SteamApp.ModdingTools);
                return;
            }
            if (S(StepId.Workspace) is { Status: StepStatus.Working, Action: StepAction.LinkWorkspace })
            {
                await RunWorkspaceStepAsync();
                return;
            }
            if (S(StepId.Mod) is { Status: StepStatus.Working, Action: StepAction.PlaceMod })
                await PlaceModStepAsync();
        }

        private async Task HandleSetupActionAsync(StepAction action)
        {
            var snap = setupSnapshot;
            if (snap is null) return;
            Log.Information("setup: the player clicked {Action}", action);
            switch (action)
            {
                case StepAction.StartSteam: StartSteamQuietly(snap); break;
                case StepAction.InstallGame: OpenSteamInstall(SteamApp.Game); break;
                case StepAction.InstallModdingTools: OpenSteamInstall(SteamApp.ModdingTools); break;
                case StepAction.LinkWorkspace:
                case StepAction.AskPermission: await RunWorkspaceStepAsync(); break;
                case StepAction.PlaceMod: await PlaceModStepAsync(); break;
                case StepAction.InstallVcRuntime: await InstallVcRuntimeAsync(snap); break;   // WO-157
                case StepAction.OpenVcDownload: UrlLauncher.Open(VcRuntime2013.DownloadPage); break;
            }
            await CheckSetupAgainAsync();
        }

        private async Task CheckSetupAgainAsync()
        {
            var snap = await Task.Run(TakeSetupSnapshot);
            ApplySetupSnapshot(snap);
            AdoptDetectedGamePath(snap);
            if (!setupReady) StartSetupPoll();
            StateHasChanged();
        }

        /// <summary>"Check setup" in the status bar: the same checklist, any time.</summary>
        private async Task OpenSetupChecklist()
        {
            Log.Information("setup: the player opened Check setup");
            showSetup = true;
            await CheckSetupAgainAsync();
            StartSetupPoll();
        }

        private void CloseSetupChecklist()
        {
            showSetup = false;
            if (setupReady) setupPollCts?.Cancel();
        }

        /// <summary>Host and Join unlock only when every step is done. A locked click opens the checklist, which says why.</summary>
        private async Task<bool> EnsureSetupReadyAsync()
        {
            if (setupFirstCheck is not null) await setupFirstCheck;
            if (setupReady) return true;
            await CheckSetupAgainAsync();
            if (setupReady) return true;
            Log.Information("setup: Host/Join asked for before setup is ready; showing the checklist");
            showSetup = true;
            StartSetupPoll();
            StateHasChanged();
            return false;
        }

        // ------------------------------------------------------------ the steps

        /// <summary>Steam started into the tray: "-silent" opens no window (Steam's own sign-in appears only if it needs one).</summary>
        private void StartSteamQuietly(SetupSnapshot snap)
        {
            var exe = snap.Steam.Root is null ? null : Path.Combine(snap.Steam.Root, "steam.exe");
            if (exe is null || !File.Exists(exe)) { Log.Warning("setup: steam.exe not found under the Steam folder"); return; }
            try
            {
                Process.Start(new ProcessStartInfo { FileName = exe, Arguments = "-silent", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized })?.Dispose();
                setupSteamStartedAt = DateTime.UtcNow;
                SetActivity(setupActivity with { StartingSteam = true });
                Log.Information("setup: started Steam in the background (-silent)");
            }
            catch (Exception ex) { Log.Warning(ex, "setup: Steam could not be started"); }
        }

        /// <summary>Steam's own install dialog -- the one window WO-150 lets the player wait on.</summary>
        private void OpenSteamInstall(uint appId)
        {
            UrlLauncher.Open($"steam://install/{appId}");
            Log.Information("setup: opened Steam's install window for app {AppId}", appId);
            SetActivity(appId == SteamApp.Game
                ? setupActivity with { GameInstallOpened = true }
                : setupActivity with { ModdingToolsInstallOpened = true });
        }

        private async Task RunWorkspaceStepAsync()
        {
            var snap = setupSnapshot;
            if (setupBusy || snap?.GameRoot is not string game || snap.ModdingToolsRoot is not string mt) return;
            if (snap.GameProcessRunning) return;   // the checklist already says to close it

            setupBusy = true;
            setupLines.Clear();
            SetActivity(setupActivity with { Linking = true, LinkFailure = null, PermissionDeclined = false, LinkProgress = null, LinkLine = null });
            StateHasChanged();

            string helper = Path.Combine(AppContext.BaseDirectory, "KcdMpSetup.exe");
            string status = Path.Combine(Globals.AppFolder, "setup-link-status.txt");
            var options = new WorkspaceFlowOptions
            {
                Log = line => { var l = Redact.Text(line); Log.Information("setup: {Line}", l); _ = InvokeAsync(() => AddSetupLine(l)); },
                Progress = new CallbackProgress<LinkProgress>(p => _ = InvokeAsync(() =>
                {
                    SetActivity(setupActivity with
                    {
                        LinkProgress = p.Total == 0 ? 1 : (double)p.Done / p.Total,
                        LinkLine = p.Total == 0 ? null : $"Linking {p.Done} of {p.Total} game files...",
                    });
                    StateHasChanged();
                })),
                RunElevated = File.Exists(helper) ? ct => ElevatedLinker.RunAsync(helper, game, mt, status, ct) : null,
                BeforeElevation = () => _ = InvokeAsync(() => { SetActivity(setupActivity with { AwaitingPermission = true }); StateHasChanged(); }),
            };

            WorkspaceFlowOutcome outcome;
            try { outcome = await Task.Run(() => WorkspaceSetupFlow.RunAsync(game, mt, options)); }
            catch (Exception ex)
            {
                Log.Warning(ex, "setup: linking failed");
                outcome = new WorkspaceFlowOutcome(WorkspaceFlowResult.Failed,
                    "The game's files couldn't be linked. Click Try again; if it keeps failing, send the logs with Report a bug.", "error",
                    Workspace.Verify(game, mt));
            }
            LogElevatedStatus(status);
            Log.Information("setup: workspace {Result} via {Route}: {Sentence}", outcome.Result, outcome.Route, Redact.Text(outcome.Sentence));

            SetActivity(setupActivity with
            {
                Linking = false, AwaitingPermission = false, LinkProgress = null, LinkLine = null,
                PermissionDeclined = outcome.Result == WorkspaceFlowResult.PermissionDeclined,
                LinkFailure = outcome.Result is WorkspaceFlowResult.Failed or WorkspaceFlowResult.InUse ? outcome.Sentence : null,
            });
            setupBusy = false;
            await CheckSetupAgainAsync();
        }

        private void LogElevatedStatus(string status)
        {
            try
            {
                if (!File.Exists(status)) return;
                foreach (var line in File.ReadAllLines(status)) Log.Information("setup: elevated helper: {Line}", Redact.Text(line));
                File.Delete(status);
            }
            catch { }
        }

        /// <summary>
        /// Step 6 for an install Setup held the mod back from (the Modding Tools
        /// were not set up yet): copy the staged mod into &lt;MT&gt;\Mods\kdcmp,
        /// record it where the uninstaller looks, and build the keys pak from the
        /// now-linked game files.
        /// </summary>
        private async Task PlaceModStepAsync()
        {
            var snap = setupSnapshot;
            if (setupBusy || snap?.ModdingToolsRoot is not string mt || snap.Mod is not { } mod) return;
            if (mod.ForeignFolder || !mod.StagedAvailable || snap.GameProcessRunning) return;

            setupBusy = true;
            SetActivity(setupActivity with { PlacingMod = true, PlaceFailure = null });
            StateHasChanged();

            var r = await Task.Run(() => ModInstall.Place(AppContext.BaseDirectory, mt));
            Log.Information("setup: mod placement {Ok}: {Message}", r.Ok ? "done" : "FAILED", Redact.Text(r.Message));
            if (r.Ok)
            {
                RecordModsPath(ModInstall.TargetDir(mt), snap.ModdingToolsExe);
                string agentPath = ResolveAgainstLauncher(settings.AgentPath);
                if (File.Exists(agentPath) && await RefreshKeysPakAsync(agentPath, mt, IsInstalledFile(agentPath)) is { } keysBlock)
                    Log.Warning("setup: keys pak not built -- {Line}", keysBlock.LogLine);   // WO-154: told at the next launch
            }
            SetActivity(setupActivity with
            {
                PlacingMod = false,
                PlaceFailure = r.Ok ? null : $"The mod couldn't be placed ({r.Message}). Click Try again; if it keeps failing, run Setup again.",
            });
            setupBusy = false;
            await CheckSetupAgainAsync();
        }

        // ------------------------------------------------------------ helpers

        private void SetActivity(SetupActivity a)
        {
            setupActivity = a;
            if (setupSnapshot is not null)
            {
                setupSteps = SetupChecklist.Evaluate(setupSnapshot, setupActivity);
                LogStepChanges();
            }
        }

        private void AddSetupLine(string line)
        {
            setupLines.Add(line);
            while (setupLines.Count > SetupLineCount) setupLines.RemoveAt(0);
            StateHasChanged();
        }

        /// <summary>
        /// A settings.json without a usable Modding Tools path (a new player, an
        /// unzipped build, a game that moved) takes the one the shared detection
        /// found, instead of opening Settings with an error as before WO-150.
        ///
        /// WO-154: written only where the file has no game path (no file, no key,
        /// or an empty one -- the never-set state) and then as that one key. A path
        /// the player set that does not work right now (a moved game, a drive not
        /// plugged in) is replaced for this run only: the file keeps the player's
        /// path, and only the player changing it in Settings writes another.
        /// </summary>
        private void AdoptDetectedGamePath(SetupSnapshot snap)
        {
            if (snap.ModdingToolsExe is not string exe) return;
            if (IsModdingToolsBuild(settings.GamePath ?? "") && File.Exists(settings.GamePath)) return;
            settings.GamePath = exe;
            if (settingsStore.IsUnsetOnDisk(nameof(AppSettings.GamePath)))
            {
                if (WriteSettings("setup", nameof(AppSettings.GamePath)))
                    Log.Information("setup: game path set to the Modding Tools found in Steam");
            }
            else
            {
                settingsStore.Accept(settings, nameof(AppSettings.GamePath));
                Log.Information("setup: the saved game path is not the Modding Tools; using the one found in Steam for this run (settings.json keeps the saved path)");
            }
        }

        /// <summary>The kdcmp folder is ours when HKCU\Software\KCDMP\ModsPath (Setup's marker, or ours after placing) names it.</summary>
        private static bool OwnsModFolder(string target)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\KCDMP");
                return key?.GetValue("ModsPath") is string p && SetupPaths.SamePath(p, target);
            }
            catch { return false; }
        }

        private static void RecordModsPath(string target, string? gameExe)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\KCDMP");
                key.SetValue("ModsPath", target);
                if (gameExe is not null) key.SetValue("GamePath", gameExe);
            }
            catch (Exception ex) { Log.Warning(ex, "setup: the mod's location could not be recorded for the uninstaller"); }
        }

        private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T>
        {
            public void Report(T value) => report(value);
        }
    }
}
