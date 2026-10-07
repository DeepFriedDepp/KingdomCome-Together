// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KCDMP_launcher.Components.Shared;
using KCDMP_launcher.Models;
using KcdMp.Setup;
using Serilog;

namespace KCDMP_launcher.Pages
{
    /// <summary>
    /// WO-157: the first public-beta patch, launcher half.
    /// <list type="bullet">
    /// <item>No injector exe: the launcher loads KCDMP.dll into the game itself (GameInjector), with the
    ///   injector's checks (the game it started, the shipped DLL by sha256, x64) failing closed.</item>
    /// <item>The file check says what it found (missing, different, unreadable, empty) and where it looked
    ///   (the install folder, the staged copy, the Modding Tools' Mods folder), with the full path, and is
    ///   never a dead end: a mod-file finding offers LAUNCH ANYWAY.</item>
    /// <item>The Visual C++ 2013 runtime the Modding Tools' trace server needs (an optional checklist step).</item>
    /// </list>
    /// The launcher never changes Windows' security settings and never adds an exclusion: the player allows
    /// a file himself, guided by the message.
    /// </summary>
    public partial class Home
    {
        // ------------------------------------------------------------ the DLL load (2.1)

        private async Task<InjectResult> InjectFromLauncherAsync(int pid, string dllPath)
        {
            ReadInstallManifest();
            // The shipped DLL's hash when Setup's manifest lists it (a development build has no manifest).
            string? sha = installedEntries!.TryGetValue("APP|KCDMP.dll", out var e) ? e.Sha256 : null;
            var r = await Task.Run(() => GameInjector.Inject(pid, dllPath, pendingGameExe, sha));
            if (r.Ok) Log.Information(r.LogLine + (sha is null ? " (development build: no install manifest, the DLL's hash not compared)" : " (sha256 matches the install manifest)"));
            else
            {
                Log.Warning(r.LogLine);
                if (r.Outcome is InjectOutcome.DllMissing or InjectOutcome.LoadFailed)
                    Log.Information("MP-LAUNCH smart_app_control={State}", SmartAppControlState());
            }
            return r;
        }

        // ------------------------------------------------------------ the file check (2.2)

        /// <summary>What the check found for one file.</summary>
        internal enum FileFinding { Ok, Missing, Empty, Unreadable, Different }

        /// <summary>One file looked at: where, and what was there.</summary>
        internal sealed record FileCheck(string Label, string Path, FileFinding Finding, string Detail, bool ModFile);

        /// <summary>
        /// The Modding Tools' Mods\kdcmp folder for the configured game. WO-157: from the install root the
        /// setup checklist uses (the folder above Bin), not GameRootOf's steam_appid.txt walk -- that walk
        /// falls back to the exe's own Bin\... folder where no steam_appid.txt exists (a fresh Modding Tools
        /// install), and the field's launcher then reported a present kdcmp.pak as gone and refused to launch.
        /// </summary>
        internal static string ModDirFor(string gamePath) =>
            ModInstall.TargetDir(GameLocator.InstallRootOf(gamePath) ?? GameRootOf(gamePath));

        private async Task<bool> PreLaunchFilesOkAsync(string gameRoot, params string[] appFiles)
        {
            ReadInstallManifest();
            bool skipMod = skipModFileCheckOnce;   // LAUNCH ANYWAY: this one launch passes the mod-file findings
            skipModFileCheckOnce = false;
            var checks = await Task.Run(() => CheckLaunchFiles(appFiles));
            foreach (var c in checks.Where(c => c.Finding != FileFinding.Ok))
                Log.Warning("MP-LAUNCH file-check {Finding} {Label}: {Path} {Detail}", c.Finding.ToString().ToLowerInvariant(), c.Label, c.Path, c.Detail);

            // A different file (not what Setup installed) is told and logged, never blocking: a hand-built
            // pak, or a manifest from another install, must not stop a game (WO-150 step 6's rule).
            var different = checks.Where(c => c.Finding == FileFinding.Different).ToList();
            var bad = checks.Where(c => c.Finding is FileFinding.Missing or FileFinding.Empty or FileFinding.Unreadable)
                            .Where(c => !(skipMod && c.ModFile)).ToList();
            // A staged copy is looked at only to say where the files are; its own absence never blocks when the
            // Mods folder has them, and when the Mods folder is missing them the Mods finding comes first.
            if (bad.Count > 1 && bad[0].ModFile) bad = bad.OrderBy(c => c.Label.StartsWith("the staged", StringComparison.Ordinal) ? 1 : 0).ToList();
            if (bad.Count == 0)
            {
                if (different.Count > 0)
                    UiService.ShowError($"{string.Join(", ", different.Select(c => System.IO.Path.GetFileName(c.Path)))} differ from what Setup installed " +
                                        "(an older or hand-copied file?). The game starts anyway; run Setup again if the mod misbehaves.");
                return true;
            }

            var first = bad[0];
            var block = BlockFor(first);
            Log.Warning(block.LogLine + $" path={first.Path} finding={first.Finding.ToString().ToLowerInvariant()}");
            Log.Information("MP-LAUNCH smart_app_control={State}", SmartAppControlState());
            string text = FindingText(first) + (bad.Count > 1 ? $" ({bad.Count - 1} more: {string.Join(", ", bad.Skip(1).Select(c => System.IO.Path.GetFileName(c.Path)))}.)" : "");
            string next = NextStepText(first);
            // Never a dead end for the mod's own files: the game can start; the mod's absence is then said in
            // the game (the agent's "the mod did not load" line) if it is really missing.
            bool canLaunchAnyway = bad.All(c => c.ModFile);
            ShowMessage(block.Title, text, next, reportBug: true, launchAnyway: canLaunchAnyway);
            return false;
        }

        /// <summary>Every launch file, then the mod's files in the Mods folder and the staged copy.</summary>
        internal List<FileCheck> CheckLaunchFiles(IEnumerable<string> appFiles)
        {
            var list = new List<FileCheck>();
            string baseDir = System.IO.Path.GetFullPath(AppContext.BaseDirectory);
            foreach (var f in appFiles)
            {
                string rel = f.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) ? f[baseDir.Length..].TrimStart('\\', '/') : "";
                installedEntries!.TryGetValue("APP|" + rel, out var e);
                list.Add(Look("the install folder", f, e, modFile: false));
            }
            if (!string.IsNullOrWhiteSpace(settings.GamePath))
            {
                string modDir = ModDirFor(settings.GamePath);
                string staged = System.IO.Path.Combine(baseDir, "mod", "kdcmp");
                foreach (var rel in InstalledModFiles())
                {
                    installedEntries!.TryGetValue("MOD|" + rel, out var e);
                    var inMods = Look("the Modding Tools' Mods folder", System.IO.Path.Combine(modDir, rel), e, modFile: true);
                    // A held-back mod (Setup's verdict) is the checklist's to place: its absence is not a removal.
                    list.Add(inMods);
                    if (inMods.Finding == FileFinding.Missing)
                    {
                        installedEntries.TryGetValue("APP|" + System.IO.Path.Combine("mod", "kdcmp", rel), out var se);
                        list.Add(Look("the staged copy in the install folder", System.IO.Path.Combine(staged, rel), se, modFile: true));
                    }
                }
            }
            return list;
        }

        /// <summary>One file: missing, empty, unreadable (locked or blocked), different from the manifest (sha256), or ok.</summary>
        internal static FileCheck Look(string label, string path, ManifestEntry? expected, bool modFile)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new FileCheck(label, "(empty path in Settings)", FileFinding.Missing, "", modFile);
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return new FileCheck(label, fi.FullName, FileFinding.Missing, "", modFile);
                if (fi.Length == 0 && (expected is null || expected.Size > 0)) return new FileCheck(label, fi.FullName, FileFinding.Empty, "0 bytes", modFile);
                string sha;
                try { sha = GameInjector.Sha256Of(fi.FullName); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    int code = ex.HResult & 0xFFFF;
                    return new FileCheck(label, fi.FullName, FileFinding.Unreadable, $"{ex.GetType().Name} (Windows error {code})", modFile);
                }
                if (expected is not null && !string.Equals(sha, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                    return new FileCheck(label, fi.FullName, FileFinding.Different,
                        $"{fi.Length:N0} bytes, sha256 {sha[..12]}; Setup installed {expected.Size:N0} bytes, sha256 {expected.Sha256[..Math.Min(12, expected.Sha256.Length)]}", modFile);
                return new FileCheck(label, fi.FullName, FileFinding.Ok, $"{fi.Length:N0} bytes", modFile);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return new FileCheck(label, path, FileFinding.Missing, ex.GetType().Name, modFile);
            }
        }

        private LaunchBlock BlockFor(FileCheck c)
        {
            string name = System.IO.Path.GetFileName(c.Path);
            bool installed = c.ModFile || installedApp!.Count > 0;
            return c.Finding switch
            {
                FileFinding.Unreadable => new LaunchBlock(LaunchBlockKind.AccessDenied, name, 5),
                FileFinding.Empty => new LaunchBlock(LaunchBlockKind.Quarantined, name, 0),
                _ => new LaunchBlock(installed ? LaunchBlockKind.Quarantined : LaunchBlockKind.Missing, name, 2),
            };
        }

        /// <summary>What was found, where, with the full path that was checked.</summary>
        internal static string FindingText(FileCheck c)
        {
            string name = System.IO.Path.GetFileName(c.Path);
            return c.Finding switch
            {
                FileFinding.Missing => $"{name} is not in {c.Label}. The launcher checked: {c.Path}",
                FileFinding.Empty => $"{name} in {c.Label} is empty (0 bytes), which is what an antivirus leaves when it takes a file. The launcher checked: {c.Path}",
                FileFinding.Unreadable => $"{name} is in {c.Label} but Windows did not let the launcher read it ({c.Detail}): it is locked by another program or blocked. The launcher checked: {c.Path}",
                FileFinding.Different => $"{name} in {c.Label} is not the file Setup installed ({c.Detail}). The launcher checked: {c.Path}",
                _ => $"{name} is fine. The launcher checked: {c.Path}",
            };
        }

        /// <summary>
        /// What to do. Never "turn a protection off" from us: the player allows the file himself. Smart App
        /// Control has no exclusions, so an exclusion cannot help when it is the blocker; how to tell which.
        /// </summary>
        internal static string NextStepText(FileCheck c)
        {
            const string howToTell =
                " To see which program removed or blocked it: Windows Security > Virus & threat protection > Protection history lists " +
                "files Windows Security quarantined (restore and allow it there). Smart App Control blocks a program without quarantining " +
                "it and has no exclusions, so an exclusion does not help against it; its blocks show as a notification \"Smart App Control " +
                "blocked...\" (Windows Security > App & browser control). Another antivirus keeps its own quarantine list.";
            string again = " Running Setup again puts the file back, but it can be removed again until it is allowed.";
            return c.Finding switch
            {
                FileFinding.Unreadable => "Close any program that may have the file open (an antivirus scan, an archive tool, the game) and try again." + howToTell,
                FileFinding.Different => "Run Setup again to put back the file it installed.",
                _ when c.ModFile => "If you are sure the file is there, the launcher may be looking at a different Modding Tools folder: check the game path in Settings." + again + howToTell +
                                    " You can also LAUNCH ANYWAY: the game starts, and the mod says in the game if it really is missing.",
                _ => again.TrimStart() + howToTell,
            };
        }

        private bool launchAnywayAvailable;

        /// <summary>The message's LAUNCH ANYWAY: the same launch, without the mod-file check (logged).</summary>
        private async Task LaunchAnywayAsync()
        {
            showMessage = false;
            launchAnywayAvailable = false;
            if (lastLaunchServer is not { } server) return;
            Log.Warning("MP-LAUNCH launch anyway: the player started the game past the mod-file check");
            skipModFileCheckOnce = true;
            await LaunchGame(server);
        }

        private ServerInfo? lastLaunchServer;
        private bool skipModFileCheckOnce;

        // ------------------------------------------------------------ the Visual C++ 2013 runtime (2.3)

        /// <summary>Microsoft's own installer from the Steam library: silent, one UAC prompt (its manifest asks for it).</summary>
        private async Task InstallVcRuntimeAsync(SetupSnapshot snap)
        {
            if (snap.VcRedist2013 is not { } exe || !File.Exists(exe)) { UrlLauncher.Open(VcRuntime2013.DownloadPage); return; }
            SetActivity(setupActivity with { InstallingVcRuntime = true, VcRuntimeFailure = null });
            string? failure = null;
            try
            {
                Log.Information("setup: starting Microsoft's Visual C++ 2013 installer from the Steam library ({Args})", VcRuntime2013.InstallArguments);
                using var p = Process.Start(new ProcessStartInfo { FileName = exe, Arguments = VcRuntime2013.InstallArguments, UseShellExecute = true });
                if (p is not null)
                {
                    await p.WaitForExitAsync();
                    Log.Information("setup: the Visual C++ 2013 installer exited {Code}", p.ExitCode);
                    if (!VcRuntime2013.InstalledByExitCode(p.ExitCode))
                        failure = $"Microsoft's installer stopped (code {p.ExitCode}).";
                }
            }
            catch (Win32Exception w) when (w.NativeErrorCode == 1223)
            {
                failure = "Windows' permission prompt was declined.";
                Log.Information("setup: the Visual C++ 2013 install was declined at the Windows prompt");
            }
            catch (Exception ex)
            {
                failure = "Microsoft's installer could not be started.";
                Log.Warning(ex, "setup: the Visual C++ 2013 installer could not be started");
            }
            SetActivity(setupActivity with { InstallingVcRuntime = false, VcRuntimeFailure = failure });
        }
    }
}
