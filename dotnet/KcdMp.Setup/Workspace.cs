// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Setup;

public enum EntryState
{
    /// <summary>The Modding Tools path IS the game's file: a hard link, a symlink, or reached through a junction.</summary>
    Linked,
    /// <summary>A separate copy with the game file's size and time -- what the tool's [C]opy answer makes (File.Copy keeps the time).</summary>
    Copy,
    Missing,
    /// <summary>A file that is not the game's current one: an old copy, or a hard link Steam's update left behind.</summary>
    Stale,
    /// <summary>A symlink whose target is gone or is not the game's file.</summary>
    BrokenLink,
}

public enum LinkKind { None, HardLink, SymbolicLink, Copy }

public sealed record WorkspaceEntry(string Rel, string GamePath, string ModdingToolsPath, long GameLength, EntryState State, LinkKind Kind, uint LinkCount)
{
    public bool Ok => State is EntryState.Linked or EntryState.Copy;
}

public sealed record WorkspaceReport(string GameRoot, string ModdingToolsRoot, IReadOnlyList<WorkspaceEntry> Entries)
{
    public bool Complete => Entries.Count > 0 && Entries.All(e => e.Ok);
    public int OkCount => Entries.Count(e => e.Ok);
    public IEnumerable<WorkspaceEntry> NeedingWork => Entries.Where(e => !e.Ok);

    /// <summary>"91 hard links" / "86 copies, 5 symlinks" -- for the checklist line and the log.</summary>
    public string KindSummary()
    {
        var parts = Entries.Where(e => e.Ok).GroupBy(e => e.Kind).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} {Plural(g.Key, g.Count())}");
        return string.Join(", ", parts);
    }

    private static string Plural(LinkKind k, int n) => k switch
    {
        LinkKind.HardLink => n == 1 ? "hard link" : "hard links",
        LinkKind.SymbolicLink => n == 1 ? "symlink" : "symlinks",
        LinkKind.Copy => n == 1 ? "copy" : "copies",
        _ => "files",
    };
}

/// <summary>
/// What Warhorse's WorkspaceSetup.exe produces, and checking it is there.
///
/// Read from the tool itself (docs/WO-150-findings.md): it mirrors every
/// *.pak in the game's Data and Localization folders, and in each
/// Data\Levels\&lt;level&gt; folder, to the same relative path in the Modding
/// Tools -- nothing else, no other folder. On the maintainer's machine that
/// is 42 + 21 + 28 = 91 files (docs/WO-150A-workspace-map.md).
/// </summary>
public static class Workspace
{
    /// <summary>Relative paths (to the game root, identical under the Modding Tools root) in the tool's own order.</summary>
    public static List<string> ExpectedFiles(string gameRoot)
    {
        var rels = new List<string>();
        void Mirror(string relDir)
        {
            var dir = Path.Combine(gameRoot, relDir);
            if (!Directory.Exists(dir)) return;
            // The tool's own call: DirectoryInfo.GetFiles("*.pak"), top level only.
            foreach (var f in new DirectoryInfo(dir).GetFiles("*.pak").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                rels.Add(Path.Combine(relDir, f.Name));
        }
        Mirror("Data");
        Mirror("Localization");
        var levels = Path.Combine(gameRoot, "Data", "Levels");
        if (Directory.Exists(levels))
            foreach (var lv in new DirectoryInfo(levels).GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                Mirror(Path.Combine("Data", "Levels", lv.Name));
        return rels;
    }

    public static WorkspaceReport Verify(string gameRoot, string moddingToolsRoot)
    {
        var entries = new List<WorkspaceEntry>();
        foreach (var rel in ExpectedFiles(gameRoot))
            entries.Add(Check(rel, Path.Combine(gameRoot, rel), Path.Combine(moddingToolsRoot, rel)));
        return new WorkspaceReport(gameRoot, moddingToolsRoot, entries);
    }

    internal static WorkspaceEntry Check(string rel, string gamePath, string mtPath)
    {
        var game = new FileInfo(gamePath);
        long gameLen = game.Exists ? game.Length : 0;
        var mt = new FileInfo(mtPath);

        bool isSymlink = false;
        try { isSymlink = mt.LinkTarget is not null; } catch { }

        if (!mt.Exists && !isSymlink)
            return new WorkspaceEntry(rel, gamePath, mtPath, gameLen, EntryState.Missing, LinkKind.None, 0);

        var gameId = Native.Identity(gamePath);
        var mtId = Native.Identity(mtPath);   // follows a symlink or a junctioned folder to whatever is really there

        if (isSymlink)
        {
            bool same = gameId is not null && mtId is not null && Native.SameFile(gameId.Value, mtId.Value);
            return new WorkspaceEntry(rel, gamePath, mtPath, gameLen, same ? EntryState.Linked : EntryState.BrokenLink,
                                      LinkKind.SymbolicLink, mtId?.NumberOfLinks ?? 0);
        }

        if (gameId is not null && mtId is not null && Native.SameFile(gameId.Value, mtId.Value))
            return new WorkspaceEntry(rel, gamePath, mtPath, gameLen, EntryState.Linked, LinkKind.HardLink, mtId.Value.NumberOfLinks);

        // A copy is accepted only when it still matches the game's file: same
        // length and same modified time (File.Copy carries the time across, and
        // a Steam update that changes the pak changes both).
        bool copyMatches = game.Exists && mt.Length == game.Length &&
                           mt.LastWriteTimeUtc == game.LastWriteTimeUtc;
        return new WorkspaceEntry(rel, gamePath, mtPath, gameLen, copyMatches ? EntryState.Copy : EntryState.Stale,
                                  LinkKind.Copy, mtId?.NumberOfLinks ?? 0);
    }
}
