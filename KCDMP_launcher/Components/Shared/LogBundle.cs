// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace KCDMP_launcher.Components.Shared
{
    /// <summary>
    /// WO-39, item K: the tester diagnostics bundle. WO-38's real two-player
    /// test produced logs with ZERO game telemetry in them -- the testers sent
    /// the launcher's app.log because that was the only file they knew about,
    /// while everything that mattered (kcd.log, the agent console) was never
    /// collected. This gathers every diagnostic surface into one zip a tester
    /// can just hand over.
    ///
    /// WO-40: the first real bundles (2026-08-18) proved two collection bugs.
    /// kcd.log was missing from BOTH players' zips -- the steam_appid.txt
    /// walk-up in GameRootOf silently falls back to the exe's own directory,
    /// where kcd.log is not -- so the bundle now hunts for kcd.log itself
    /// (walk up from the game root, then scan every Steam library the way the
    /// agent's KcdLogLocator does). And "config.json" was collected from
    /// %AppData%\KCDMP_Launcher where the launcher never writes anything; the
    /// real settings live in settings.json in the launcher's working
    /// directory. Both are collected now (settings.json as the real one,
    /// config.json kept in case an old install has it).
    /// </summary>
    public static class LogBundle
    {
        /// <summary>
        /// Collects kcd.log, the agent's log files and the launcher's recent
        /// app logs into a timestamped zip on the user's Desktop. Returns the
        /// zip path. Files that do not exist are skipped, never fatal -- a
        /// partial bundle beats no bundle.
        /// </summary>
        /// <param name="relayDirectory">
        /// WO-81: where the relay executable lives, i.e. where it writes
        /// relay*.log if it has ever run from here. Empty is fine -- this
        /// machine may be a joiner with no local relay process, exactly like
        /// an empty GameRoot/AgentDirectory; the file simply will not exist
        /// and is skipped like any other missing file. Before this WO the
        /// relay had no persistent log at all (Console-only Serilog sink), so
        /// there was nothing here to collect regardless of this parameter --
        /// see docs/WO-81-findings.md.
        /// </param>
        public static string Collect(string gameRoot, string agentDirectory, string relayDirectory = "", bool includeEarlierLaunches = true)
        {
            string zipPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"KCDMP-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            // The game holds kcd.log open while running; the agent holds
            // agent.log; Serilog holds the current app log. Everything is
            // therefore read with full sharing rather than ZipArchive's own
            // exclusive CreateEntryFromFile.
            foreach (var sf in Gather(gameRoot, agentDirectory, relayDirectory, includeEarlierLaunches))
            {
                try
                {
                    var entry = zip.CreateEntry(sf.EntryName, CompressionLevel.Optimal);
                    using var dst = entry.Open();
                    if (sf.Bytes is not null) { dst.Write(sf.Bytes); continue; }
                    using var src = new FileStream(sf.Path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    src.CopyTo(dst);
                }
                catch
                {
                    // One unreadable file must not sink the bundle.
                }
            }
            return zipPath;
        }

        /// <summary>
        /// WO-171 L: the bundle as parts of at most 19 MB (Discord's free upload limit is 20 MB): a tar compressed with zstd
        /// (<c>.tzst</c>; 7-Zip opens it), repeated noise lines collapsed, identical files stored once, a manifest in part 1.
        /// The current launch by default; the earlier launches (history\) only when asked for. Falls back to the old zip,
        /// and says so, when the packer cannot run.
        /// </summary>
        public static CollectResult CollectPacked(string gameRoot, string agentDirectory, string relayDirectory, bool includeEarlierLaunches, Action<string>? progress = null)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string baseName = $"KCDMP-logs-{DateTime.Now:yyyyMMdd-HHmmss}";
            var sources = Gather(gameRoot, agentDirectory, relayDirectory, includeEarlierLaunches);
            try
            {
                var r = KcdMp.LogPack.Packer.Pack(sources, desktop, baseName, new KcdMp.LogPack.PackOptions(), progress);
                return new CollectResult(r.Parts, r.OriginalBytes, r.PartBytes, false, "");
            }
            catch (Exception ex)
            {
                string zip = Collect(gameRoot, agentDirectory, relayDirectory, includeEarlierLaunches);
                long size = new FileInfo(zip).Length;
                return new CollectResult(new[] { zip }, 0, size, true, ex.Message);
            }
        }

        public sealed record CollectResult(IReadOnlyList<string> Files, long OriginalBytes, long PackedBytes, bool UsedZipFallback, string FallbackReason);

        /// <summary>Everything a bundle holds, as (entry name, file or bytes); a file that is not there is simply not listed.</summary>
        internal static List<KcdMp.LogPack.SourceFile> Gather(string gameRoot, string agentDirectory, string relayDirectory, bool includeEarlierLaunches)
        {
            var list = new List<KcdMp.LogPack.SourceFile>();
            void AddIfPresent(string? path, string entryName)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                    list.Add(new KcdMp.LogPack.SourceFile(entryName, path));
                }
                catch { }
            }

            string? kcdLog = FindKcdLog(gameRoot);
            AddIfPresent(kcdLog, "kcd.log");

            // WO-58: the two files this bundle was missing when it mattered.
            // The 2026-08-25 host freeze was only diagnosable because the
            // native mirror log and the engine's own kcd.log backup happened
            // to survive on disk -- neither was in any exported bundle.
            // kcdmp-native.mirror.log lives in the game root (next to
            // kcd.log, see native/KCDMP/log.h), and CryEngine rotates the
            // PREVIOUS run's kcd.log into logbackups\ -- which is the run a
            // tester is usually reporting about, since they collect logs
            // after restarting a crashed game.
            if (kcdLog is not null)
            {
                string kcdDir = Path.GetDirectoryName(kcdLog) ?? "";
                AddIfPresent(Path.Combine(kcdDir, "kcdmp-native.mirror.log"), "kcdmp-native.mirror.log");
                // WO-177 A4: the DLL keeps the last five earlier logs of each kind (kcdmp-native.mirror.1..5.log).
                for (int n = 1; n <= NativeLogKeep; n++)
                    AddIfPresent(Path.Combine(kcdDir, $"kcdmp-native.mirror.{n}.log"), $"kcdmp-native.mirror.{n}.log");
                try
                {
                    var backups = new DirectoryInfo(Path.Combine(kcdDir, "logbackups"))
                        .GetFiles("*.log")
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .Take(2);
                    foreach (var b in backups)
                        AddIfPresent(b.FullName, $"logbackups/{b.Name}");
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(agentDirectory))
            {
                AddIfPresent(Path.Combine(agentDirectory, "agent.log"), "agent.log");
                AddIfPresent(Path.Combine(agentDirectory, "agent.prev.log"), "agent.prev.log");
                AddIfPresent(Path.Combine(agentDirectory, "agent.prev2.log"), "agent.prev2.log");
                // The primary native log sits beside the DLL, which ships in
                // the same directory as the agent.
                AddIfPresent(Path.Combine(agentDirectory, "kcdmp-native.log"), "kcdmp-native.log");
                for (int n = 1; n <= NativeLogKeep; n++)
                    AddIfPresent(Path.Combine(agentDirectory, $"kcdmp-native.{n}.log"), $"kcdmp-native.{n}.log");
                // WO-154: the mod menu's remembered choices, filtered (Models/ModSettingsReport.cs): its own
                // keys with on/off or joint/individual only -- nothing typed by hand reaches the zip.
                try
                {
                    string ms = Path.Combine(agentDirectory, "mod-settings.json");
                    if (File.Exists(ms))
                    {
                        byte[] raw;
                        using (var src = new FileStream(ms, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var mem = new MemoryStream())
                        {
                            src.CopyTo(mem);
                            raw = mem.ToArray();
                        }
                        if (KCDMP_launcher.Models.ModSettingsReport.Filter(raw) is string filtered)
                            list.Add(new KcdMp.LogPack.SourceFile("mod-settings.json", Bytes: System.Text.Encoding.UTF8.GetBytes(filtered)));
                    }
                }
                catch { }
                // WO-127: the leash recorder's CSVs (mp_leash_trace), newest first.
                try
                {
                    var leash = new DirectoryInfo(Path.Combine(agentDirectory, "leash"));
                    if (leash.Exists)
                        foreach (var f in leash.GetFiles("leash-*.csv").OrderByDescending(f => f.LastWriteTimeUtc).Take(8))
                            AddIfPresent(f.FullName, $"leash/{f.Name}");
                }
                catch { }
            }

            // Serilog's rolling file names are app<yyyyMMdd>.log; take the two
            // newest so the bundle covers a session that straddled midnight.
            try
            {
                foreach (var f in new DirectoryInfo(Globals.AppFolder)
                             .GetFiles("app*.log")
                             .OrderByDescending(f => f.LastWriteTimeUtc)
                             .Take(2))
                    AddIfPresent(f.FullName, f.Name);
            }
            catch { }

            // WO-81: the relay's own Serilog output (relay<yyyyMMdd>.log, same
            // rolling shape as this launcher's app.log -- see
            // dotnet/KcdMp.Server/appsettings.json), including its
            // [WO66-REJECT]/[CLAIM]/[CLAIM-CONTESTED] lines. Only exists on
            // whichever machine hosted; DirectoryInfo on a bad/empty path or
            // GetFiles finding nothing both no-op through the same try/catch
            // used above, so a joiner (or a host who never started a relay
            // this install) collects a bundle with this simply absent, not an
            // error or a misleading empty entry.
            if (!string.IsNullOrWhiteSpace(relayDirectory))
            {
                try
                {
                    foreach (var f in new DirectoryInfo(relayDirectory)
                                 .GetFiles("relay*.log")
                                 .OrderByDescending(f => f.LastWriteTimeUtc)
                                 .Take(2))
                        AddIfPresent(f.FullName, f.Name);
                }
                catch { }
            }

            // WO-144: the logs of the launches before this one (KeepHistory), newest first -- a
            // restart used to overwrite the only copy of a crash's or a black screen's logs.
            try
            {
                var hist = new DirectoryInfo(HistoryFolder);
                if (includeEarlierLaunches && hist.Exists)
                    foreach (var d in hist.GetDirectories("launch-*").OrderByDescending(d => d.Name).Take(HistoryKeep))
                        foreach (var f in d.GetFiles())
                            AddIfPresent(f.FullName, $"history/{d.Name}/{f.Name}");
            }
            catch { }

            // WO-202 I.1: every crash's evidence from the last 24 h -- the mod's own reports, the game's crash reporter, Windows'.
            if (kcdLog is not null) GatherCrashes(Path.GetDirectoryName(kcdLog) ?? "", list, AddIfPresent);

            // The launcher's real settings file (written by Home.SaveSettings
            // as a working-directory-relative "settings.json").
            AddIfPresent(Path.GetFullPath("settings.json"), "settings.json");
            AddIfPresent(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"),
                "settings.launcher-dir.json");
            AddIfPresent(Globals.ConfigFilePath, "config.json");

            return list;
        }

        /// <summary>WO-202 I.1: how far back a crash's evidence is collected.</summary>
        internal static readonly TimeSpan CrashWindow = TimeSpan.FromHours(24);

        /// <summary>
        /// WO-202 I.1: not one crash of the 2026-10-20 sessions left evidence in a bundle. Collected now, each part never fatal:
        ///   * crashes/   the DLL's reports (kcdmp-crashes\crash-*: crash.txt, crash.dmp, the agent's agent-statements.txt);
        ///   * game-crash/ the game's own crash reporter (BugSplat on the Modding Tools build): its %TEMP%\bugsplat_*.log lines
        ///     (the faulting module and offset; the machine and account lines left out), its minidump while it is still there,
        ///     and its BugSplatAttachments (small files only);
        ///   * windows/   Windows Error Reporting's reports for KingdomCome.exe and the Application event log's crash entries
        ///     (the only trace a fast-fail leaves).
        /// </summary>
        internal static void GatherCrashes(string gameDir, List<KcdMp.LogPack.SourceFile> list, Action<string?, string> add)
        {
            DateTime since = DateTime.Now - CrashWindow;
            try
            {
                var root = new DirectoryInfo(Path.Combine(gameDir, "kcdmp-crashes"));
                if (root.Exists)
                    foreach (var d in root.GetDirectories("crash-*").Where(d => d.LastWriteTime >= since).OrderByDescending(d => d.LastWriteTime).Take(4))
                        foreach (var f in d.GetFiles())
                            add(f.FullName, $"crashes/{d.Name}/{f.Name}");
            }
            catch { }
            try
            {
                var tmp = new DirectoryInfo(Path.GetTempPath());
                foreach (var f in tmp.GetFiles("bugsplat_*.log").Where(f => f.LastWriteTime >= since && f.Length > 3).OrderByDescending(f => f.LastWriteTime).Take(6))
                {
                    var keep = File.ReadAllLines(f.FullName).Where(l => !l.Contains("Attributes:") && !l.Contains("setUser") && !l.Contains("setMail"));
                    list.Add(new KcdMp.LogPack.SourceFile($"game-crash/{f.Name}", Bytes: System.Text.Encoding.UTF8.GetBytes(string.Join("\n", keep))));
                }
                foreach (var f in tmp.GetFiles("Kingdom Come*.dmp").Where(f => f.LastWriteTime >= since && f.Length < 64L << 20).OrderByDescending(f => f.LastWriteTime).Take(2))
                    add(f.FullName, $"game-crash/{f.Name.Replace(':', '_')}");
            }
            catch { }
            try
            {
                var att = new DirectoryInfo(Path.Combine(gameDir, "BugSplatAttachments"));
                if (att.Exists)
                    foreach (var f in att.GetFiles().Where(f => f.LastWriteTime >= since && f.Length < 2L << 20).OrderByDescending(f => f.LastWriteTime).Take(8))
                        add(f.FullName, $"game-crash/attachments/{f.Name}");
            }
            catch { }
            try
            {
                string wer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER");
                foreach (var kind in new[] { "ReportArchive", "ReportQueue" })
                {
                    var d = new DirectoryInfo(Path.Combine(wer, kind));
                    if (!d.Exists) continue;
                    foreach (var r in d.GetDirectories("App*_KingdomCome.exe_*").Where(r => r.LastWriteTime >= since).OrderByDescending(r => r.LastWriteTime).Take(4))
                        add(Path.Combine(r.FullName, "Report.wer"), $"windows/{kind}/{r.Name}/Report.wer");
                }
            }
            catch { }
            try
            {
                string events = CrashEvents(since);
                if (events.Length > 0) list.Add(new KcdMp.LogPack.SourceFile("windows/application-events.txt", Bytes: System.Text.Encoding.UTF8.GetBytes(events)));
            }
            catch { }
        }

        /// <summary>The Application event log's crash entries (1000 application error, 1001 its report, 1026 .NET) that name
        /// KingdomCome.exe, newest first.</summary>
        internal static string CrashEvents(DateTime since)
        {
            long ms = (long)Math.Max(1, (DateTime.Now - since).TotalMilliseconds);
            var psi = new System.Diagnostics.ProcessStartInfo("wevtutil.exe",
                $"qe Application /q:\"*[System[(EventID=1000 or EventID=1001 or EventID=1026) and TimeCreated[timediff(@SystemTime) <= {ms}]]]\" /f:text /rd:true /c:200")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return "";
            string text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            var blocks = text.Split("Event[", StringSplitOptions.RemoveEmptyEntries)
                .Where(b => b.Contains("KingdomCome", StringComparison.OrdinalIgnoreCase))
                .Select(b => "Event[" + string.Join("\n", b.TrimEnd().Split('\n')
                    .Where(l => !l.TrimStart().StartsWith("Computer:", StringComparison.Ordinal) && !l.TrimStart().StartsWith("User:", StringComparison.Ordinal))));   // no machine or account name
            return string.Join("\n\n", blocks);
        }

        /// <summary>WO-177 A4: how many earlier native logs the DLL keeps (native/KCDMP/log.h kLogKeep).</summary>
        internal const int NativeLogKeep = 5;

        /// <summary>WO-144: where KeepHistory puts the logs of earlier launches (one folder per launch).</summary>
        public static string HistoryFolder => Path.Combine(Globals.AppFolder, "log-history");

        /// <summary>WO-144: how many earlier launches are kept.</summary>
        public const int HistoryKeep = 6;

        /// <summary>
        /// WO-144 (log retention): called right before the game starts. The game writes a new kcd.log
        /// at every start and keeps ONE backup; the DLL's logs start over at every injection. A second
        /// restart after a crash therefore used to overwrite the only copy of the crash's logs. This
        /// copies the logs as they are now -- the previous launch's -- into log-history\launch-&lt;time&gt;
        /// and keeps the newest <see cref="HistoryKeep"/> launches. Never fatal.
        /// </summary>
        public static void KeepHistory(string gameRoot, string agentDirectory)
        {
            try
            {
                var files = new List<(string Path, string Name)>();
                string? kcdLog = FindKcdLog(gameRoot);
                if (kcdLog is not null)
                {
                    string kcdDir = Path.GetDirectoryName(kcdLog) ?? "";
                    files.Add((kcdLog, "kcd.log"));
                    files.Add((Path.Combine(kcdDir, "kcdmp-native.mirror.log"), "kcdmp-native.mirror.log"));
                }
                if (!string.IsNullOrWhiteSpace(agentDirectory))
                    files.Add((Path.Combine(agentDirectory, "kcdmp-native.log"), "kcdmp-native.log"));
                files = files.Where(f => File.Exists(f.Path)).ToList();
                if (files.Count == 0) return;

                // named by the time the previous launch's kcd.log was last written (its end)
                DateTime stamp = files.Max(f => File.GetLastWriteTime(f.Path));
                string dir = Path.Combine(HistoryFolder, $"launch-{stamp:yyyyMMdd-HHmmss}");
                if (Directory.Exists(dir)) return;   // this launch's logs are kept already
                Directory.CreateDirectory(dir);
                foreach (var (path, name) in files)
                {
                    try
                    {
                        using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var dst = new FileStream(Path.Combine(dir, name), FileMode.CreateNew, FileAccess.Write);
                        src.CopyTo(dst);
                    }
                    catch { }
                }
                foreach (var old in new DirectoryInfo(HistoryFolder).GetDirectories("launch-*")
                             .OrderByDescending(d => d.Name).Skip(HistoryKeep))
                {
                    try { old.Delete(recursive: true); } catch { }
                }
            }
            catch
            {
                // log retention must never stop a launch
            }
        }

        /// <summary>
        /// Finds the game's kcd.log the way the agent does, rather than
        /// trusting one derived directory: try the supplied game root, walk
        /// up from it a few levels, then scan every KCD-ish folder in every
        /// Steam library and take the most recently written match.
        /// </summary>
        internal static string? FindKcdLog(string gameRoot)   // WO-154: also the CONNECT gate's (Home.Wo154.cs)
        {
            try
            {
                // 1. The supplied root and its parents.
                string dir = gameRoot ?? "";
                for (int i = 0; i < 4 && !string.IsNullOrWhiteSpace(dir); i++)
                {
                    string candidate = Path.Combine(dir, "kcd.log");
                    if (File.Exists(candidate)) return candidate;
                    dir = Path.GetDirectoryName(dir) ?? "";
                }

                // 2. Steam library scan (the agent's KcdLogLocator logic).
                var logs = new List<FileInfo>();
                foreach (var root in SteamLibraryRoots())
                {
                    string common = Path.Combine(root, "steamapps", "common");
                    if (!Directory.Exists(common)) continue;

                    IEnumerable<string> subdirs;
                    try { subdirs = Directory.EnumerateDirectories(common); }
                    catch { continue; }

                    foreach (var d in subdirs)
                    {
                        string name = Path.GetFileName(d);
                        if (!name.Contains("Kingdom", StringComparison.OrdinalIgnoreCase) &&
                            !name.Contains("KCD", StringComparison.OrdinalIgnoreCase))
                            continue;
                        try
                        {
                            logs.AddRange(new DirectoryInfo(d)
                                .EnumerateFiles("kcd.log", SearchOption.TopDirectoryOnly));
                        }
                        catch { }
                    }
                }

                return logs.Count == 0
                    ? null
                    : logs.OrderByDescending(f => f.LastWriteTimeUtc).First().FullName;
            }
            catch { return null; }
        }

        private static IEnumerable<string> SteamLibraryRoots()
        {
            string? steam =
                Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string
                ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string
                ?? Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null) as string;

            if (steam is null) yield break;
            yield return steam;

            string vdf = Path.Combine(steam, "config", "libraryfolders.vdf");
            if (!File.Exists(vdf)) yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };
            IEnumerable<string> lines;
            try { lines = File.ReadLines(vdf); }
            catch { yield break; }

            foreach (var line in lines)
            {
                var m = Regex.Match(line.Trim(), @"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                string p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (seen.Add(p)) yield return p;
            }
        }
    }
}
