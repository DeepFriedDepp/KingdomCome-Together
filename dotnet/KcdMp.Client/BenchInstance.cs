// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-206 A.2: one agent per game copy when the test bench runs two copies on one PC. The rig sets KCDMP_INSTANCE (the
/// copy's id, the same one its game process has, native/KCDMP/instance.h) and KCD2MP_INSTALL (the copy's own root).
/// With an id, the agent talks to that copy only: its pipe (kcdmp-&lt;id&gt;), its root's newest kcd log (a second
/// instance writes kcd(1).log), and the saves folder that copy's engine says it uses ("User folder is ..." in that log;
/// the engine appends "(1)" to a second instance's folder). A player's agent never has an id, and every lookup is the
/// one it always was.
/// </summary>
public static partial class BenchInstance
{
    /// <summary>This agent's copy id, or null (a player's agent). Same rule as the DLL: 1..24 of [A-Za-z0-9_-].</summary>
    public static string? Id { get; } = Valid(Environment.GetEnvironmentVariable("KCDMP_INSTANCE"));

    public static string PipeName => PipeNameFor(Id);

    public static string PipeNameFor(string? id) => id is null ? "kcdmp" : "kcdmp-" + id;

    public static string? Valid(string? s) => s is { Length: > 0 and <= 24 } && IdRx().IsMatch(s) ? s : null;

    /// <summary>The copy's own root (KCD2MP_INSTALL), only when this agent has an id.</summary>
    public static string? GameRoot =>
        Id is not null && Environment.GetEnvironmentVariable("KCD2MP_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    /// <summary>The newest kcd.log / kcd(N).log in a root, or null.</summary>
    public static string? NewestLog(string root)
    {
        try
        {
            return new DirectoryInfo(root).EnumerateFiles("kcd*.log", SearchOption.TopDirectoryOnly)
                .Where(f => LogNameRx().IsMatch(f.Name))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }

    /// <summary>"&lt;user folder&gt;\saves" from the engine's own "User folder is '...'" line in a log, or null.</summary>
    public static string? SavesDirFromLog(string? log)
    {
        if (log is null) return null;
        try
        {
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            for (int i = 0; i < 2000 && r.ReadLine() is string line; i++)
            {
                var m = UserFolderRx().Match(line);
                if (m.Success) return Path.Combine(m.Groups[1].Value, "saves");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdRx();

    [GeneratedRegex(@"^kcd(\(\d+\))?\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LogNameRx();

    [GeneratedRegex(@"^User folder is '([^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex UserFolderRx();
}
