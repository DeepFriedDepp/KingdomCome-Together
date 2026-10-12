// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

// WO-175 S2: no launcher-only usings here -- the agent test project compiles this file by link.
namespace KCDMP_launcher.Models
{
    /// <summary>
    /// WO-175 S2: rolling backups of the player's whole saves folder (&lt;Saved Games&gt;\KingdomCome2\saves), made
    /// before every session the launcher starts, and the restore that puts one back.
    /// <list type="bullet">
    /// <item>a backup is &lt;root&gt;\&lt;yyyyMMdd-HHmmss&gt;\: every file and subfolder of the saves folder, plus
    ///   manifest.sha256 (sha256, two spaces, the relative path with forward slashes; sorted). It is written into
    ///   &lt;stamp&gt;.part and renamed only once every copied file re-hashes to what was read from the source;</item>
    /// <item>retention keeps the last <see cref="KeepDefault"/> backups and never removes the newest
    ///   <see cref="KeepAlways"/>; it removes only folders named like a stamp that hold a manifest, and .part
    ///   leftovers older than a day; nothing else in the root is ever touched;</item>
    /// <item>a restore checks the backup first, backs up the current saves (so a restore can be undone), makes the
    ///   saves folder byte-equal to the backup, and checks the saves folder against the backup's manifest.</item>
    /// </list>
    /// </summary>
    public static class Wo175Backup
    {
        public const string ManifestName = "manifest.sha256";
        public const string StampFormat = "yyyyMMdd-HHmmss";
        public const int KeepDefault = 10;
        public const int KeepAlways = 3;
        public const string RootEnv = "KCDMP_BACKUP_DIR";

        private static readonly Regex StampRx = new(@"^\d{8}-\d{6}$", RegexOptions.CultureInvariant);
        private static readonly Regex PartRx = new(@"^(\d{8}-\d{6})\.part$", RegexOptions.CultureInvariant);
        private static readonly Regex LineRx = new(@"^([0-9a-f]{64})  (.+)$", RegexOptions.CultureInvariant);

        public sealed record BackupResult(string? Path, int Files, long Bytes, bool Verified, string Why, int Kept, int Removed, bool Skipped = false)
        {
            public string LogLine =>
                $"WO175-BACKUP made={Path ?? "none"} files={Files} bytes={Bytes} verified={(Verified ? "yes" : "no")} kept={Kept} removed={Removed}"
                + (Why.Length > 0 ? $" ({Why})" : "");
        }

        public sealed record VerifyResult(bool Ok, int Files, long Bytes, string Why);

        public sealed record RestoreResult(string From, string? SafetyBackup, int Files, bool Verified, string Why)
        {
            public string LogLine =>
                $"WO175-RESTORE from={From} safety_backup={SafetyBackup ?? "none"} files={Files} verified={(Verified ? "yes" : "no")}"
                + (Why.Length > 0 ? $" ({Why})" : "");
        }

        public sealed record PruneResult(int Kept, int Removed, IReadOnlyList<string> RemovedPaths);

        /// <summary>One backup in the list a player picks from.</summary>
        public sealed record BackupInfo(string Path, DateTime Taken, int Files, long Bytes);

        // ------------------------------------------------------------------ where

        /// <summary>%LOCALAPPDATA%\KingdomComeTogether\save-backups, or KCDMP_BACKUP_DIR when set (tests).</summary>
        public static string DefaultRoot() =>
            Environment.GetEnvironmentVariable(RootEnv) is { Length: > 0 } d
                ? d
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KingdomComeTogether", "save-backups");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);
        private static readonly Guid FolderIdSavedGames = new("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4");

        /// <summary>&lt;Saved Games&gt;\KingdomCome2\saves, or null -- the same rule as the agent's GameBridge.ResolveSavesDir.</summary>
        public static string? ResolveSavesDir()
        {
            string? root = null;
            try
            {
                if (SHGetKnownFolderPath(FolderIdSavedGames, 0, IntPtr.Zero, out var p) == 0)
                {
                    root = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                }
            }
            catch { }
            root ??= System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
            string dir = System.IO.Path.Combine(root, "KingdomCome2", "saves");
            return Directory.Exists(dir) ? dir : null;
        }

        public static bool IsStampName(string name) => StampRx.IsMatch(name);

        // ------------------------------------------------------------------ backup

        /// <summary>The whole saves folder into &lt;backupsRoot&gt;\&lt;stamp&gt;\, then retention.</summary>
        public static BackupResult Backup(string savesDir, string backupsRoot, DateTime now) =>
            BackupCore(savesDir, backupsRoot, now, prune: true);

        private static BackupResult BackupCore(string savesDir, string backupsRoot, DateTime now, bool prune)
        {
            string? part = null;
            try
            {
                if (!Directory.Exists(savesDir))
                    return new BackupResult(null, 0, 0, false, "no saves folder found", 0, 0, Skipped: true);
                string saves = Full(savesDir), root = Full(backupsRoot);
                if (IsUnder(root, saves) || IsUnder(saves, root))
                    return new BackupResult(null, 0, 0, false, "the backup folder and the saves folder overlap", 0, 0);

                var files = Walk(saves).Select(f => (Rel: Rel(saves, f), Full: f)).OrderBy(f => f.Rel, StringComparer.Ordinal).ToList();
                if (files.Count == 0)
                    return new BackupResult(null, 0, 0, false, "the saves folder is empty", 0, 0, Skipped: true);
                if (files.Any(f => string.Equals(f.Rel, ManifestName, StringComparison.OrdinalIgnoreCase)))
                    return new BackupResult(null, 0, 0, false, $"the saves folder holds a file named {ManifestName}", 0, 0);

                Directory.CreateDirectory(root);
                CleanStaleParts(root, now);

                // a free stamp: two backups in one second (a restore's safety backup) take the next second
                DateTime t = now;
                string stamp;
                while (true)
                {
                    stamp = t.ToString(StampFormat, CultureInfo.InvariantCulture);
                    if (!Directory.Exists(System.IO.Path.Combine(root, stamp)) && !Directory.Exists(System.IO.Path.Combine(root, stamp + ".part"))) break;
                    t = t.AddSeconds(1);
                }
                part = System.IO.Path.Combine(root, stamp + ".part");
                string final = System.IO.Path.Combine(root, stamp);
                Directory.CreateDirectory(part);

                foreach (var d in WalkDirs(saves))
                    Directory.CreateDirectory(System.IO.Path.Combine(part, Rel(saves, d)));

                var hashes = new List<(string Rel, string Hash)>(files.Count);
                long bytes = 0;
                foreach (var (rel, src) in files)
                {
                    string dst = System.IO.Path.Combine(part, rel);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst)!);
                    hashes.Add((rel, CopyHashing(src, dst, out long n)));
                    bytes += n;
                    try { File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src)); } catch { }
                }
                WriteManifest(part, hashes);

                var v = Verify(part);
                if (!v.Ok)
                {
                    TryDelete(part);
                    return new BackupResult(null, files.Count, bytes, false, "the copy did not match: " + v.Why, 0, 0);
                }
                Directory.Move(part, final);
                part = null;

                var pr = prune ? Prune(root, now) : new PruneResult(ListValid(root).Count, 0, Array.Empty<string>());
                return new BackupResult(final, files.Count, bytes, true, "", pr.Kept, pr.Removed);
            }
            catch (Exception ex)
            {
                if (part is not null) TryDelete(part);
                return new BackupResult(null, 0, 0, false, ex.Message, 0, 0);
            }
        }

        // ------------------------------------------------------------------ retention

        /// <summary>
        /// Keeps the newest <paramref name="keep"/> backups (never fewer than <see cref="KeepAlways"/>), and removes
        /// .part leftovers older than a day. Only stamp-named folders holding a manifest count as backups; anything
        /// else in the root is left alone.
        /// </summary>
        public static PruneResult Prune(string backupsRoot, DateTime now, int keep = KeepDefault)
        {
            if (!Directory.Exists(backupsRoot)) return new PruneResult(0, 0, Array.Empty<string>());
            keep = Math.Max(keep, KeepAlways);
            var removed = new List<string>();
            var valid = ListValid(backupsRoot);   // newest first
            foreach (var old in valid.Skip(keep))
            {
                try { Directory.Delete(old, recursive: true); removed.Add(old); } catch { }
            }
            removed.AddRange(CleanStaleParts(backupsRoot, now));
            return new PruneResult(valid.Count - removed.Count(r => valid.Contains(r)), removed.Count, removed);
        }

        /// <summary>Stamp-named folders that hold a manifest (and are not links), newest first.</summary>
        private static List<string> ListValid(string backupsRoot)
        {
            if (!Directory.Exists(backupsRoot)) return new List<string>();
            return new DirectoryInfo(backupsRoot).EnumerateDirectories()
                .Where(d => StampRx.IsMatch(d.Name) && (d.Attributes & FileAttributes.ReparsePoint) == 0
                            && File.Exists(System.IO.Path.Combine(d.FullName, ManifestName)))
                .Select(d => d.FullName)
                .OrderByDescending(p => System.IO.Path.GetFileName(p), StringComparer.Ordinal)
                .ToList();
        }

        private static List<string> CleanStaleParts(string backupsRoot, DateTime now)
        {
            var removed = new List<string>();
            foreach (var d in new DirectoryInfo(backupsRoot).EnumerateDirectories())
            {
                var m = PartRx.Match(d.Name);
                if (!m.Success || (d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (!DateTime.TryParseExact(m.Groups[1].Value, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var started)) continue;
                if (now - started <= TimeSpan.FromDays(1)) continue;
                try { d.Delete(recursive: true); removed.Add(d.FullName); } catch { }
            }
            return removed;
        }

        // ------------------------------------------------------------------ WO-178 0.c: the backup to pick

        /// <summary>The agent writes this beside the backups when a load found the weapons stuck (the draw probe): the stamp of the
        /// newest backup (made before that session), why, and when.</summary>
        public const string PickFileName = "restore-pick.txt";

        /// <summary>The marked backup's folder, or null (no mark, a malformed mark, or a mark whose backup is gone).</summary>
        public static string? ReadPick(string backupsRoot)
        {
            try
            {
                string f = System.IO.Path.Combine(backupsRoot, PickFileName);
                if (!File.Exists(f)) return null;
                string stamp = (File.ReadLines(f).FirstOrDefault() ?? "").Trim();
                if (!StampRx.IsMatch(stamp)) return null;
                string dir = System.IO.Path.Combine(backupsRoot, stamp);
                return File.Exists(System.IO.Path.Combine(dir, ManifestName)) ? dir : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }

        /// <summary>The mark is gone once its backup was restored.</summary>
        public static void ClearPick(string backupsRoot)
        {
            try { File.Delete(System.IO.Path.Combine(backupsRoot, PickFileName)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        /// <summary>The backups a player can restore, newest first.</summary>
        public static IReadOnlyList<BackupInfo> List(string backupsRoot)
        {
            var list = new List<BackupInfo>();
            foreach (var dir in ListValid(backupsRoot))
            {
                if (!DateTime.TryParseExact(System.IO.Path.GetFileName(dir), StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var taken)) continue;
                var (entries, _) = ReadManifest(dir);
                if (entries is null) continue;
                long bytes = 0;
                foreach (var e in entries)
                {
                    try { bytes += new FileInfo(System.IO.Path.Combine(dir, e.Rel)).Length; } catch { }
                }
                list.Add(new BackupInfo(dir, taken, entries.Count, bytes));
            }
            return list;
        }

        // ------------------------------------------------------------------ verify

        /// <summary>Every file the manifest lists re-hashes to its line, and the backup holds no file it does not list.</summary>
        public static VerifyResult Verify(string backupDir)
        {
            try
            {
                if (!Directory.Exists(backupDir)) return new VerifyResult(false, 0, 0, "the backup folder is gone");
                var (entries, why) = ReadManifest(backupDir);
                if (entries is null) return new VerifyResult(false, 0, 0, why);
                return Compare(backupDir, entries, skipRootManifest: true);
            }
            catch (Exception ex)
            {
                return new VerifyResult(false, 0, 0, ex.Message);
            }
        }

        private static VerifyResult Compare(string dir, List<(string Hash, string Rel)> entries, bool skipRootManifest)
        {
            string full = Full(dir);
            long bytes = 0;
            foreach (var (hash, rel) in entries)
            {
                string f = System.IO.Path.Combine(full, rel);
                if (!File.Exists(f)) return new VerifyResult(false, entries.Count, bytes, $"{rel} is missing");
                if (!string.Equals(HashFile(f, out long n), hash, StringComparison.Ordinal))
                    return new VerifyResult(false, entries.Count, bytes, $"{rel} does not match");
                bytes += n;
            }
            var listed = new HashSet<string>(entries.Select(e => e.Rel), StringComparer.OrdinalIgnoreCase);
            foreach (var f in Walk(full))
            {
                string rel = Rel(full, f);
                if (skipRootManifest && string.Equals(rel, ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!listed.Contains(rel)) return new VerifyResult(false, entries.Count, bytes, $"{rel} is not in the manifest");
            }
            return new VerifyResult(true, entries.Count, bytes, "");
        }

        private static (List<(string Hash, string Rel)>? Entries, string Why) ReadManifest(string backupDir)
        {
            string path = System.IO.Path.Combine(backupDir, ManifestName);
            if (!File.Exists(path)) return (null, "the backup has no manifest");
            var entries = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                var m = LineRx.Match(line);
                if (!m.Success) return (null, "the manifest is damaged");
                string rel = m.Groups[2].Value;
                if (!SafeRel(rel) || !seen.Add(rel)) return (null, $"the manifest names an unusable path: {rel}");
                entries.Add((m.Groups[1].Value, rel));
            }
            return (entries, "");
        }

        /// <summary>A manifest path stays inside its folder: relative, no "..", no drive or stream colon.</summary>
        private static bool SafeRel(string rel) =>
            !rel.StartsWith('/') && !rel.StartsWith('\\') && !rel.Contains(':') && !rel.Contains('\\')
            && rel.Split('/').All(s => s.Length > 0 && s != "." && s != "..");

        // ------------------------------------------------------------------ restore

        /// <summary>
        /// Checks the backup, backs up the current saves (no retention, so the chosen backup cannot be pruned
        /// under the restore), makes the saves folder byte-equal to the backup, and checks it against the manifest.
        /// </summary>
        public static RestoreResult Restore(string backupDir, string savesDir, string backupsRoot, DateTime now)
        {
            string? safety = null;
            int files = 0;
            try
            {
                var v = Verify(backupDir);
                if (!v.Ok) return new RestoreResult(backupDir, null, 0, false, "the backup is damaged: " + v.Why);
                string src = Full(backupDir), saves = Full(savesDir);
                if (IsUnder(src, saves) || IsUnder(saves, src))
                    return new RestoreResult(backupDir, null, 0, false, "the backup and the saves folder overlap");

                if (Directory.Exists(saves) && Walk(saves).Any())
                {
                    var b = BackupCore(saves, backupsRoot, now, prune: false);
                    if (!b.Verified) return new RestoreResult(backupDir, null, 0, false, "your current saves could not be backed up first: " + b.Why);
                    safety = b.Path;
                }
                Directory.CreateDirectory(saves);

                var (entries, why) = ReadManifest(src);
                if (entries is null) return new RestoreResult(backupDir, safety, 0, false, why);
                files = entries.Count;

                foreach (var d in WalkDirs(src))
                    Directory.CreateDirectory(System.IO.Path.Combine(saves, Rel(src, d)));
                foreach (var (_, rel) in entries)
                {
                    string dst = System.IO.Path.Combine(saves, rel);
                    if (!IsUnder(Full(dst), saves)) return new RestoreResult(backupDir, safety, files, false, $"{rel} would land outside the saves folder");
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst)!);
                    if (File.Exists(dst)) File.SetAttributes(dst, FileAttributes.Normal);
                    File.Copy(System.IO.Path.Combine(src, rel), dst, overwrite: true);
                }

                // what the backup does not have goes: files first, then the folders that are left empty
                var listed = new HashSet<string>(entries.Select(e => e.Rel), StringComparer.OrdinalIgnoreCase);
                foreach (var f in Walk(saves).ToList())
                {
                    if (listed.Contains(Rel(saves, f))) continue;
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                }
                var keepDirs = new HashSet<string>(WalkDirs(src).Select(d => Rel(src, d)), StringComparer.OrdinalIgnoreCase);
                foreach (var d in WalkDirs(saves).OrderByDescending(d => d.Length).ToList())
                {
                    if (keepDirs.Contains(Rel(saves, d))) continue;
                    if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
                }

                var after = Compare(saves, entries, skipRootManifest: false);
                return new RestoreResult(backupDir, safety, files, after.Ok, after.Ok ? "" : "the saves folder does not match the backup: " + after.Why);
            }
            catch (Exception ex)
            {
                return new RestoreResult(backupDir, safety, files, false, ex.Message);
            }
        }

        // ------------------------------------------------------------------ files

        /// <summary>Every file under <paramref name="dir"/>, hidden ones too; linked folders are not entered.</summary>
        private static IEnumerable<string> Walk(string dir)
        {
            var opts = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
            foreach (var f in Directory.EnumerateFiles(dir, "*", opts)) yield return f;
            foreach (var d in WalkDirs(dir))
                foreach (var f in Directory.EnumerateFiles(d, "*", opts)) yield return f;
        }

        /// <summary>Every folder under <paramref name="dir"/> (not itself), parents before children; links skipped.</summary>
        private static IEnumerable<string> WalkDirs(string dir)
        {
            var opts = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
            foreach (var d in new DirectoryInfo(dir).EnumerateDirectories("*", opts))
            {
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                yield return d.FullName;
                foreach (var sub in WalkDirs(d.FullName)) yield return sub;
            }
        }

        private static string CopyHashing(string src, string dst, out long bytes)
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
            using (var o = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            {
                var buf = new byte[1 << 16];
                bytes = 0;
                int n;
                while ((n = i.Read(buf, 0, buf.Length)) > 0)
                {
                    sha.AppendData(buf, 0, n);
                    o.Write(buf, 0, n);
                    bytes += n;
                }
            }
            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }

        private static string HashFile(string path, out long bytes)
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            bytes = s.Length;
            return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        }

        private static void WriteManifest(string dir, List<(string Rel, string Hash)> hashes)
        {
            var sb = new StringBuilder();
            foreach (var (rel, hash) in hashes.OrderBy(h => h.Rel, StringComparer.Ordinal))
                sb.Append(hash).Append("  ").Append(rel).Append('\n');
            File.WriteAllText(System.IO.Path.Combine(dir, ManifestName), sb.ToString(), new UTF8Encoding(false));
        }

        private static string Full(string p) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(p));

        private static string Rel(string root, string full) => System.IO.Path.GetRelativePath(root, full).Replace('\\', '/');

        /// <summary><paramref name="path"/> is <paramref name="dir"/> or inside it.</summary>
        private static bool IsUnder(string path, string dir) =>
            string.Equals(path, dir, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(dir + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
