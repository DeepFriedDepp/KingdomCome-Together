// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Security.Cryptography;

namespace KcdMp.Setup;

public sealed record ManifestEntry(string Kind, string Rel, long Size, string Sha256);

public sealed class ModReport
{
    /// <summary>No install-manifest.txt beside the launcher: a development build run from the repo. The mod check does not apply.</summary>
    public bool DevelopmentBuild { get; init; }
    public bool VerdictPass { get; init; }
    public string VerdictLine { get; init; } = "";
    /// <summary>Install-folder files that are missing or the wrong size (sizes only: hashing ~700 files is not a startup check).</summary>
    public List<string> AppProblems { get; init; } = new();
    /// <summary>Mod files that are not in &lt;MT&gt;\Mods\kdcmp at all.</summary>
    public List<string> ModMissing { get; init; } = new();
    /// <summary>Mod files that are there but are not the bytes Setup shipped (sha256: two small files) -- a hand-built pak, typically.</summary>
    public List<string> ModDifferent { get; init; } = new();
    /// <summary>The installer's staged copy (&lt;app&gt;\mod\kdcmp) is present and matches the manifest, so the launcher can place it.</summary>
    public bool StagedAvailable { get; init; }
    /// <summary>A kdcmp folder is there whose mod.manifest is not ours, and nothing says we put it there.</summary>
    public bool ForeignFolder { get; init; }
    public string TargetDir { get; init; } = "";

    public bool Placed => !DevelopmentBuild && ModMissing.Count == 0 && !ForeignFolder;

    /// <summary>
    /// What blocks Host and Join: Setup's own verdict said the install failed, the mod is not
    /// there, or the folder is somebody else's. Files that differ from the shipped bytes only
    /// warn (<see cref="Warnings"/>): the same rule as the launcher's InstallIntegrity check --
    /// a false alarm that stops a player connecting is worse than a warned mismatch, and a
    /// hand-built pak is the developer's normal state. They are never overwritten either.
    /// </summary>
    public bool Ok => DevelopmentBuild || (VerdictPass && Placed);

    public IEnumerable<string> Warnings => AppProblems.Concat(ModDifferent);
}

/// <summary>
/// The mod half of setup (checklist step 6): the same checks as
/// tools\Verify-Install.ps1 against Setup's own install-manifest.txt, and the
/// placement the installer now leaves to the launcher when the Modding Tools
/// are not set up yet.
///
/// install-manifest.txt (v2, WO-74): &lt;kind&gt;|&lt;rel&gt;|&lt;size&gt;|&lt;sha256&gt;. APP
/// entries are files in the install folder, MOD entries files in
/// &lt;MT&gt;\Mods\kdcmp. Since WO-150 the payload also carries the two MOD files
/// as APP entries under mod\kdcmp\ -- the staged copy the launcher places.
/// </summary>
public static class ModInstall
{
    public const string ManifestName = "install-manifest.txt";
    public const string VerdictName = "install-verify.txt";
    public const string StagedRel = @"mod\kdcmp";
    /// <summary>Built by the agent from the player's own game files (WO-148); ours, kept across a re-place.</summary>
    public const string KeysPak = @"Data\kdcmp_keys.pak";

    public static string TargetDir(string moddingToolsRoot) => Path.Combine(moddingToolsRoot, "Mods", "kdcmp");

    public static List<ManifestEntry> ReadManifest(string path)
    {
        var list = new List<ManifestEntry>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('|');
            if (f.Length < 4 || f[1].Length == 0) continue;
            if (!long.TryParse(f[2], out long size)) continue;
            list.Add(new ManifestEntry(f[0].ToUpperInvariant(), f[1], size, f[3].ToLowerInvariant()));
        }
        return list;
    }

    public static ModReport Check(string appDir, string moddingToolsRoot, bool ownsModFolder)
    {
        var manifestPath = Path.Combine(appDir, ManifestName);
        var target = TargetDir(moddingToolsRoot);
        if (!File.Exists(manifestPath))
            return new ModReport { DevelopmentBuild = true, TargetDir = target };

        List<ManifestEntry> entries;
        try { entries = ReadManifest(manifestPath); }
        catch { return new ModReport { VerdictLine = "install-manifest.txt could not be read", TargetDir = target }; }

        string verdict = "";
        try { verdict = File.ReadLines(Path.Combine(appDir, VerdictName)).FirstOrDefault() ?? ""; } catch { }

        var app = new List<string>();
        foreach (var e in entries.Where(e => e.Kind == "APP"))
        {
            var fi = new FileInfo(Path.Combine(appDir, e.Rel));
            if (!fi.Exists) app.Add($"{e.Rel} (missing)");
            else if (fi.Length != e.Size) app.Add($"{e.Rel} ({fi.Length} bytes, expected {e.Size})");
        }

        var mods = entries.Where(e => e.Kind == "MOD").ToList();
        var missing = new List<string>();
        var different = new List<string>();
        foreach (var e in mods)
        {
            if (Matches(Path.Combine(target, e.Rel), e, out var why)) continue;
            if (why == "missing") missing.Add(e.Rel);
            else different.Add($"{e.Rel} ({why})");
        }

        bool staged = mods.Count > 0 && mods.All(e => Matches(Path.Combine(appDir, StagedRel, e.Rel), e, out _));

        // Ours by content: our mod.manifest has never changed between releases, so a kdcmp folder whose
        // mod.manifest is not those bytes was put there by someone else -- unless the registry says it was us.
        var manifestEntry = mods.FirstOrDefault(e => string.Equals(e.Rel, "mod.manifest", StringComparison.OrdinalIgnoreCase));
        var targetManifest = Path.Combine(target, "mod.manifest");
        bool foreign = !ownsModFolder && manifestEntry is not null && File.Exists(targetManifest) &&
                       !Matches(targetManifest, manifestEntry, out _);

        return new ModReport
        {
            VerdictPass = verdict.StartsWith("PASS", StringComparison.Ordinal),
            VerdictLine = verdict,
            AppProblems = app,
            ModMissing = missing,
            ModDifferent = foreign ? new List<string>() : different,
            StagedAvailable = staged,
            ForeignFolder = foreign,
            TargetDir = target,
        };
    }

    private static bool Matches(string path, ManifestEntry e, out string why)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) { why = "missing"; return false; }
        if (fi.Length != e.Size) { why = "wrong size"; return false; }
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            if (hash != e.Sha256) { why = "wrong content"; return false; }
        }
        catch { why = "could not be read"; return false; }
        why = "";
        return true;
    }

    public sealed record PlaceResult(bool Ok, string Message);

    /// <summary>
    /// Copies the staged mod into &lt;MT&gt;\Mods\kdcmp. Never over a foreign
    /// kdcmp (the caller checks <see cref="ModReport.ForeignFolder"/> first).
    /// Prunes the folder the way the installer does for its own upgrades
    /// (WO-74): everything but our files goes -- loose pak sources there break
    /// the game outright ("114 tables are not loaded") -- and each of our files
    /// is written under a temporary name and moved into place, so a failure
    /// leaves the previous version rather than nothing.
    /// </summary>
    public static PlaceResult Place(string appDir, string moddingToolsRoot)
    {
        var target = TargetDir(moddingToolsRoot);
        try
        {
            var mods = ReadManifest(Path.Combine(appDir, ManifestName)).Where(e => e.Kind == "MOD").ToList();
            if (mods.Count == 0) return new PlaceResult(false, "the install manifest lists no mod files");
            foreach (var e in mods)
                if (!Matches(Path.Combine(appDir, StagedRel, e.Rel), e, out var why))
                    return new PlaceResult(false, $"the installer's copy of {e.Rel} is {why}");

            Directory.CreateDirectory(Path.Combine(target, "Data"));
            Prune(target, mods.Select(m => m.Rel).Append(KeysPak).ToList());

            foreach (var e in mods)
            {
                var dest = Path.Combine(target, e.Rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var tmp = dest + ".kcdmp-new";
                File.Copy(Path.Combine(appDir, StagedRel, e.Rel), tmp, overwrite: true);
                if (File.Exists(dest)) new FileInfo(dest).IsReadOnly = false;
                File.Move(tmp, dest, overwrite: true);
            }

            foreach (var e in mods)
                if (!Matches(Path.Combine(target, e.Rel), e, out var why))
                    return new PlaceResult(false, $"{e.Rel} is {why} after copying");
            return new PlaceResult(true, $"placed {mods.Count} file(s)");
        }
        catch (Exception ex)
        {
            return new PlaceResult(false, ex is IOException ? "a file is in use (is the game running?)" : ex.GetType().Name);
        }
    }

    private static void Prune(string target, List<string> keepRels)
    {
        var keep = new HashSet<string>(keepRels.Select(r => Path.GetFullPath(Path.Combine(target, r))), StringComparer.OrdinalIgnoreCase);
        PruneDir(new DirectoryInfo(target), keep, isRoot: true);
    }

    /// <summary>Never follows a junction or a directory symlink: one is removed as a link, its target untouched.</summary>
    private static void PruneDir(DirectoryInfo dir, HashSet<string> keep, bool isRoot)
    {
        foreach (var entry in dir.EnumerateFileSystemInfos().ToList())
        {
            if (entry is DirectoryInfo sub)
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) { sub.Delete(); continue; }
                PruneDir(sub, keep, isRoot: false);
                bool isData = isRoot && string.Equals(sub.Name, "Data", StringComparison.OrdinalIgnoreCase);
                if (!isData && !sub.EnumerateFileSystemInfos().Any()) sub.Delete();
                continue;
            }
            if (keep.Contains(Path.GetFullPath(entry.FullName))) continue;
            if ((entry.Attributes & FileAttributes.ReadOnly) != 0) entry.Attributes &= ~FileAttributes.ReadOnly;
            entry.Delete();
        }
    }
}
