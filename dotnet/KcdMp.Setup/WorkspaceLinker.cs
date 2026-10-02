// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Setup;

public enum LinkMethod { HardLink, SymbolicLink }

public sealed record LinkOutcome(string Rel, bool Ok, LinkMethod? Method, int Error, string Note);

public sealed record LinkProgress(int Done, int Total, string Rel);

public sealed class LinkRun
{
    public List<LinkOutcome> Outcomes { get; } = new();
    /// <summary>Windows refused a symlink for want of admin rights / Developer Mode; the rest wait for one elevated run.</summary>
    public bool NeedsElevation { get; set; }
    /// <summary>A file could not be replaced because something has it open -- in practice, the game.</summary>
    public bool InUse { get; set; }
    public int Linked => Outcomes.Count(o => o.Ok);
    public int Failed => Outcomes.Count(o => !o.Ok);
    public int HardLinks => Outcomes.Count(o => o.Ok && o.Method == LinkMethod.HardLink);
    public int Symlinks => Outcomes.Count(o => o.Ok && o.Method == LinkMethod.SymbolicLink);
    /// <summary>Hard links were wanted but the volume pair refused them (different drives, or a file system without them).</summary>
    public bool FellBackToSymlinks { get; set; }
}

/// <summary>The three file-system calls the linker makes, so the tests can make a hard link fail "across drives" on one drive.</summary>
public interface ILinkPrimitives
{
    int CreateHardLink(string newFile, string existingFile);
    int CreateSymlink(string link, string target);
    int MoveReplace(string from, string to);
}

public sealed class Win32LinkPrimitives : ILinkPrimitives
{
    public static readonly Win32LinkPrimitives Instance = new();
    public int CreateHardLink(string newFile, string existingFile) => Native.CreateHardLink(newFile, existingFile);
    public int CreateSymlink(string link, string target) => Native.CreateFileSymlink(link, target);
    public int MoveReplace(string from, string to) => Native.MoveReplace(from, to);
}

/// <summary>
/// Makes the links WorkspaceSetup.exe would, when the tool itself cannot be
/// driven (it reads its answers with Console.ReadKey, which a pipe cannot
/// feed -- docs/WO-150-findings.md).
///
/// Which kind: a hard link when the game and the Modding Tools share a
/// volume -- no admin rights, no Developer Mode, no disk space, and the game
/// cannot tell it from the file. Across volumes a hard link is impossible
/// (ERROR_NOT_SAME_DEVICE), so a symlink -- the kind the tool's own [S]
/// answer makes -- which Windows allows only with Developer Mode or admin
/// rights; refused (ERROR_PRIVILEGE_NOT_HELD), the run stops and reports
/// <see cref="LinkRun.NeedsElevation"/> so the caller can ask once, through
/// UAC, for the elevated helper to make the rest. Copies (the tool's [C]) are
/// never made: 92 GB of duplicate data is not a setup step.
///
/// Each link is made under a temporary name and moved over the target in
/// one step, so a pak the game needs is never missing, not even for a moment,
/// and an interrupted run leaves the old file rather than nothing. Only the
/// expected *.pak paths are ever touched; nothing else in the Modding Tools.
/// </summary>
public static class WorkspaceLinker
{
    public const string TempSuffix = ".kcdmp-link";

    /// <param name="symlinksOnly">
    /// Skip the hard-link attempt: the elevated step, which runs only because hard links were
    /// impossible between these two folders (KcdMpSetup.exe link --kind symlink).
    /// </param>
    public static LinkRun Link(WorkspaceReport report, ILinkPrimitives? primitives = null,
                               IProgress<LinkProgress>? progress = null, CancellationToken ct = default,
                               bool symlinksOnly = false)
    {
        var io = primitives ?? Win32LinkPrimitives.Instance;
        var run = new LinkRun();
        var work = report.NeedingWork.ToList();
        bool hardLinksRefused = symlinksOnly;

        for (int i = 0; i < work.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e = work[i];
            progress?.Report(new LinkProgress(i, work.Count, e.Rel));

            if (run.NeedsElevation)
            {
                run.Outcomes.Add(new LinkOutcome(e.Rel, false, null, Native.ERROR_PRIVILEGE_NOT_HELD, "waiting for permission"));
                continue;
            }

            var outcome = LinkOne(e, io, ref hardLinksRefused, run);
            run.Outcomes.Add(outcome);
            if (outcome.Error == Native.ERROR_PRIVILEGE_NOT_HELD) run.NeedsElevation = true;
            if (outcome.Error is Native.ERROR_SHARING_VIOLATION) run.InUse = true;
        }
        progress?.Report(new LinkProgress(work.Count, work.Count, ""));
        return run;
    }

    private static LinkOutcome LinkOne(WorkspaceEntry e, ILinkPrimitives io, ref bool hardLinksRefused, LinkRun run)
    {
        try
        {
            var dir = Path.GetDirectoryName(e.ModdingToolsPath)!;
            Directory.CreateDirectory(dir);
            var tmp = e.ModdingToolsPath + TempSuffix;
            DeleteOurTemp(tmp);

            LinkMethod method;
            int hardErr = hardLinksRefused ? -1 : io.CreateHardLink(tmp, e.GamePath);
            if (hardErr == 0) method = LinkMethod.HardLink;
            else
            {
                if (hardErr != -1)
                {
                    // the same pair of volumes refuses every file the same way; stop asking after the first
                    if (IsHardLinkImpossible(hardErr)) hardLinksRefused = true;
                    run.FellBackToSymlinks = true;
                }
                int symErr = io.CreateSymlink(tmp, e.GamePath);
                if (symErr != 0)
                    return new LinkOutcome(e.Rel, false, null, symErr, symErr == Native.ERROR_PRIVILEGE_NOT_HELD
                        ? "symlink needs permission" : $"symlink failed (Windows error {symErr}; hard link: {hardErr})");
                method = LinkMethod.SymbolicLink;
            }

            // A read-only old copy would refuse the replace (WO-74: one read-only file stopped a whole install).
            try
            {
                var old = new FileInfo(e.ModdingToolsPath);
                if (old.Exists && old.LinkTarget is null && old.IsReadOnly) old.IsReadOnly = false;
            }
            catch { }

            int err = io.MoveReplace(tmp, e.ModdingToolsPath);
            if (err != 0)
            {
                DeleteOurTemp(tmp);
                if (err == Native.ERROR_ACCESS_DENIED) err = Native.ERROR_SHARING_VIOLATION;   // a pak held open by a running game
                return new LinkOutcome(e.Rel, false, null, err, $"could not replace the old file (Windows error {err})");
            }
            return new LinkOutcome(e.Rel, true, method, 0, method == LinkMethod.HardLink ? "hard link" : "symlink");
        }
        catch (Exception ex)
        {
            return new LinkOutcome(e.Rel, false, null, ex.HResult, ex.GetType().Name);
        }
    }

    /// <summary>A hard link cannot work for this pair of folders at all, as opposed to failing for this one file.</summary>
    internal static bool IsHardLinkImpossible(int err) =>
        err is Native.ERROR_NOT_SAME_DEVICE or Native.ERROR_INVALID_FUNCTION or Native.ERROR_NOT_SUPPORTED or Native.ERROR_TOO_MANY_LINKS;

    private static void DeleteOurTemp(string tmp)
    {
        // Only ever our own temporary name; a link (or a leftover from a killed run) is deleted, never followed.
        try
        {
            var fi = new FileInfo(tmp);
            if (fi.Exists || fi.LinkTarget is not null) fi.Delete();
        }
        catch { }
    }
}
