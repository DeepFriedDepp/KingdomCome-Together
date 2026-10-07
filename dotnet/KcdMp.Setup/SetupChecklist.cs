// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Setup;

public enum StepId { Steam = 1, Game = 2, DiskSpace = 3, ModdingTools = 4, Workspace = 5, Mod = 6, Ready = 7, VcRuntime = 8 }

public enum StepStatus
{
    Done,
    /// <summary>In progress: Steam downloading, the launcher linking -- nothing for the player to do.</summary>
    Working,
    /// <summary>Stopped until the player does the one thing the detail line says.</summary>
    NeedsYou,
    /// <summary>Not started: an earlier step has to finish first.</summary>
    Waiting,
}

/// <summary>What the step's button does. The launcher runs it; the checklist only names it.</summary>
public enum StepAction
{
    None,
    StartSteam,
    InstallGame,
    InstallModdingTools,
    LinkWorkspace,
    AskPermission,
    PlaceMod,
    /// <summary>WO-157: run Microsoft's Visual C++ 2013 installer from the Steam library (one Windows prompt).</summary>
    InstallVcRuntime,
    /// <summary>WO-157: open Microsoft's download page.</summary>
    OpenVcDownload,
}

/// <summary><paramref name="Optional"/> (WO-157): shown, but never holds Host and Join back.</summary>
public sealed record StepView(StepId Id, string Title, StepStatus Status, string Detail,
                              StepAction Action = StepAction.None, string? ActionLabel = null, double? Progress = null,
                              bool Optional = false);

/// <summary>What the launcher is doing right now, which the files on disk cannot show yet.</summary>
public sealed record SetupActivity
{
    public bool StartingSteam { get; init; }
    public bool GameInstallOpened { get; init; }
    public bool ModdingToolsInstallOpened { get; init; }
    public bool Linking { get; init; }
    public double? LinkProgress { get; init; }
    public string? LinkLine { get; init; }
    public bool AwaitingPermission { get; init; }
    public bool PermissionDeclined { get; init; }
    /// <summary>The last link run's failure, in one plain sentence; null when it did not fail.</summary>
    public string? LinkFailure { get; init; }
    public bool PlacingMod { get; init; }
    public string? PlaceFailure { get; init; }
    /// <summary>WO-157: Microsoft's VC++ 2013 installer is running.</summary>
    public bool InstallingVcRuntime { get; init; }
    /// <summary>WO-157: its last run's failure, in one plain sentence.</summary>
    public string? VcRuntimeFailure { get; init; }

    public static readonly SetupActivity Idle = new();
}

/// <summary>
/// WO-150's checklist, decided purely from a <see cref="SetupSnapshot"/> (the
/// real state, read fresh every time -- never a saved "done" flag) plus what
/// the launcher is doing at the moment. No I/O, so every branch is a unit test.
///
/// "Installed" and "set up" are separate steps everywhere: the Modding Tools
/// being on disk (4) says nothing about the workspace being linked (5).
/// </summary>
public static class SetupChecklist
{
    public const string ReadyTitle = "Ready!";

    public static IReadOnlyList<StepView> Evaluate(SetupSnapshot s, SetupActivity a)
    {
        var steps = new List<StepView>(7);
        var game = GameStep(s);
        var disk = DiskStep(s);
        var mt = ModdingToolsStep(s, a, disk);
        var steam = SteamStep(s, a, needSteam: game.Status != StepStatus.Done || mt.Status != StepStatus.Done);
        bool steamBlocks = steam.Status == StepStatus.NeedsYou && !s.Steam.Found;

        steps.Add(steam);
        steps.Add(steamBlocks && game.Status != StepStatus.Done ? Wait(StepId.Game, game.Title, "Waiting for Steam.") : WithGameActivity(game, a));
        steps.Add(disk);
        steps.Add(steamBlocks && mt.Status != StepStatus.Done ? Wait(StepId.ModdingTools, mt.Title, "Waiting for Steam.") : mt);
        var ws = WorkspaceStep(s, a, game.Status == StepStatus.Done && mt.Status == StepStatus.Done);
        steps.Add(ws);
        steps.Add(ModStep(s, a, ws.Status == StepStatus.Done));
        steps.Add(VcRuntimeStep(s, a));

        bool allDone = steps.All(x => x.Optional || x.Status == StepStatus.Done);
        steps.Add(allDone
            ? new StepView(StepId.Ready, ReadyTitle, StepStatus.Done, "Everything is set up. Host or Join whenever you like.")
            : new StepView(StepId.Ready, ReadyTitle, StepStatus.Waiting, "Host and Join unlock when every step above is done."));
        return steps;
    }

    public static bool IsReady(IReadOnlyList<StepView> steps) => steps.All(x => x.Optional || x.Status == StepStatus.Done);

    /// <summary>WO-157: ready, with an optional step still asking for the player (shown once at start).</summary>
    public static bool OptionalNeedsYou(IReadOnlyList<StepView> steps) => steps.Any(x => x.Optional && x.Status == StepStatus.NeedsYou);

    private static StepView Wait(StepId id, string title, string why) => new(id, title, StepStatus.Waiting, why);

    private static StepView SteamStep(SetupSnapshot s, SetupActivity a, bool needSteam)
    {
        const string title = "Steam running and signed in";
        if (!s.Steam.Found)
            return new(StepId.Steam, title, StepStatus.NeedsYou,
                "Steam isn't installed on this PC. Install it from store.steampowered.com, sign in, then click Check again.");
        if (s.SteamRunning)
            return new(StepId.Steam, title, StepStatus.Done, s.SteamSignedIn == true ? "Steam is running and signed in." : "Steam is running.");
        if (!needSteam)
            return new(StepId.Steam, title, StepStatus.Done, "Not needed right now: the game starts Steam itself.");
        if (a.StartingSteam)
            return new(StepId.Steam, title, StepStatus.Working, "Starting Steam in the background...");
        return new(StepId.Steam, title, StepStatus.NeedsYou,
            "Steam isn't running. Start it and sign in; the launcher carries on by itself.", StepAction.StartSteam, "Start Steam");
    }

    private static StepView GameStep(SetupSnapshot s)
    {
        const string title = "Kingdom Come: Deliverance II installed";
        var m = s.GameManifest;
        if (s.GameInstalled && (m is null || !m.Busy))
            return new(StepId.Game, title, StepStatus.Done, "Found in your Steam library.");
        if (m is not null && m.Busy)
            return new(StepId.Game, title, StepStatus.Working,
                $"Steam is {(s.GameInstalled ? "updating" : "downloading")} the game{Percent(m.Progress)}. This can take a while; you can use your PC meanwhile.",
                Progress: m.Progress);
        if (m is not null && m.Paused)
            return new(StepId.Game, title, StepStatus.NeedsYou,
                "The game's download is paused in Steam. Resume it in Steam's Downloads page.", StepAction.InstallGame, "Open Steam");
        if (m is not null)
            return new(StepId.Game, title, StepStatus.NeedsYou,
                "Steam lists the game but its files are missing. In Steam, right-click the game > Properties > Installed Files > Verify integrity.");
        return new(StepId.Game, title, StepStatus.NeedsYou,
            "The game isn't installed. Install Kingdom Come: Deliverance II in Steam's install window.", StepAction.InstallGame, "Install in Steam");
    }

    private static StepView WithGameActivity(StepView game, SetupActivity a)
    {
        if (game.Status == StepStatus.NeedsYou && game.Action == StepAction.InstallGame && a.GameInstallOpened && game.ActionLabel == "Install in Steam")
            return game with { Detail = "Steam's install window is open: click Install there. This list updates by itself.", ActionLabel = "Open it again" };
        return game;
    }

    private static StepView DiskStep(SetupSnapshot s)
    {
        const string title = "Disk space for the Modding Tools";
        if (s.ModdingToolsInstalled || s.ModdingToolsManifest is { Busy: true })
            return new(StepId.DiskSpace, title, StepStatus.Done, "Not needed: the Modding Tools are already there.");
        if (s.DownloadVolumeFreeBytes is not long free || s.DownloadVolume is null)
            return new(StepId.DiskSpace, title, StepStatus.Done, "Couldn't read the free space; Steam checks it again when it downloads.");
        long need = SteamApp.ModdingToolsBytes + SteamApp.ModdingToolsMarginBytes;
        string drive = s.DownloadVolume.TrimEnd('\\');
        if (free >= need)
            return new(StepId.DiskSpace, title, StepStatus.Done, $"{Gb(free)} free on {drive} (they need about {Gb(SteamApp.ModdingToolsBytes)}).");
        return new(StepId.DiskSpace, title, StepStatus.NeedsYou,
            $"Free up {Gb(need - free)} on {drive}, or choose another drive in Steam's install window (Windows will then ask once for permission to link the game's files).",
            StepAction.InstallModdingTools, "Install on another drive");
    }

    private static StepView ModdingToolsStep(SetupSnapshot s, SetupActivity a, StepView disk)
    {
        const string title = "KCD2 Modding Tools installed";
        var m = s.ModdingToolsManifest;
        if (s.ModdingToolsInstalled && (m is null || !m.Busy))
            return new(StepId.ModdingTools, title, StepStatus.Done, "Found in your Steam library.");
        if (m is not null && m.Busy)
            return new(StepId.ModdingTools, title, StepStatus.Working,
                $"Steam is {(s.ModdingToolsInstalled ? "updating" : "downloading")} the Modding Tools{Percent(m.Progress)}. You can use your PC meanwhile.",
                Progress: m.Progress);
        if (m is not null && m.Paused)
            return new(StepId.ModdingTools, title, StepStatus.NeedsYou,
                "The Modding Tools' download is paused in Steam. Resume it in Steam's Downloads page.", StepAction.InstallModdingTools, "Open Steam");
        if (m is not null)
            return new(StepId.ModdingTools, title, StepStatus.NeedsYou,
                "Steam lists the Modding Tools but their files are missing. In Steam, right-click \"Kingdom Come: Deliverance II Modding tools\" > Properties > Installed Files > Verify integrity.");
        if (disk.Status == StepStatus.NeedsYou)
            return new(StepId.ModdingTools, title, StepStatus.Waiting, "Waiting for disk space (above).");
        if (a.ModdingToolsInstallOpened)
            return new(StepId.ModdingTools, title, StepStatus.NeedsYou,
                "Steam's install window is open: click Install there. This list updates by itself.", StepAction.InstallModdingTools, "Open it again");
        return new(StepId.ModdingTools, title, StepStatus.NeedsYou,
            "The free \"Kingdom Come: Deliverance II Modding tools\" aren't installed. Install them in Steam's install window.",
            StepAction.InstallModdingTools, "Install in Steam");
    }

    private static StepView WorkspaceStep(SetupSnapshot s, SetupActivity a, bool ready)
    {
        const string title = "Game files linked into the Modding Tools";
        if (!ready) return Wait(StepId.Workspace, title, "Waiting for the game and the Modding Tools.");
        var w = s.Workspace;
        if (w is { Complete: true } && !a.Linking)
            return new(StepId.Workspace, title, StepStatus.Done, $"All {w.Entries.Count} game files are in place ({w.KindSummary()}).");
        if (w is null || w.Entries.Count == 0)
            return new(StepId.Workspace, title, StepStatus.NeedsYou,
                "The game's data folder has no files to link. In Steam, verify the game's files, then click Check again.");
        if (a.AwaitingPermission)
            return new(StepId.Workspace, title, StepStatus.Working,
                "Windows needs your permission to link the game's files into the Modding Tools. Choose Yes in the Windows prompt.");
        if (a.Linking)
            return new(StepId.Workspace, title, StepStatus.Working,
                a.LinkLine ?? $"Linking {w.Entries.Count - w.OkCount} of {w.Entries.Count} game files...", Progress: a.LinkProgress);
        if (a.PermissionDeclined)
            return new(StepId.Workspace, title, StepStatus.NeedsYou,
                "Windows needs your permission to link the game's files into the Modding Tools. Click Ask again and choose Yes.",
                StepAction.AskPermission, "Ask again");
        if (s.GameProcessRunning)
            return new(StepId.Workspace, title, StepStatus.NeedsYou,
                "Close the game first: its files can't be linked while it is running.", StepAction.LinkWorkspace, "Try again");
        if (a.LinkFailure is not null)
            return new(StepId.Workspace, title, StepStatus.NeedsYou, a.LinkFailure, StepAction.LinkWorkspace, "Try again");
        return new(StepId.Workspace, title, StepStatus.Working,
            $"{w.Entries.Count - w.OkCount} of {w.Entries.Count} game files still need linking; starting...", StepAction.LinkWorkspace);
    }

    private static StepView ModStep(SetupSnapshot s, SetupActivity a, bool ready)
    {
        const string title = "Kingdom Come: Together mod installed and complete";
        if (!ready) return Wait(StepId.Mod, title, "Waiting for the game files to be linked.");
        var m = s.Mod;
        if (m is null)
            return new(StepId.Mod, title, StepStatus.Done, "Not checked (no install folder given).");
        if (m.DevelopmentBuild)
            return new(StepId.Mod, title, StepStatus.Done, "Development build: no install manifest beside the launcher, so nothing to check.");
        if (!m.VerdictPass)
            return new(StepId.Mod, title, StepStatus.NeedsYou,
                "The install is incomplete. Close the launcher and run Setup again; it repairs an install in this state.");
        if (m.ForeignFolder)
            return new(StepId.Mod, title, StepStatus.NeedsYou,
                "A different mod named \"kdcmp\" is in the Modding Tools' Mods folder. Run Setup again and let it replace that folder.");
        if (m.Ok)
        {
            int n = m.Warnings.Count();
            return new(StepId.Mod, title, StepStatus.Done, n == 0 ? "Installed and verified."
                : $"Installed. {n} file{(n == 1 ? "" : "s")} differ from what Setup installed (a hand-copied build?); run Setup again to restore them.");
        }
        if (a.PlacingMod)
            return new(StepId.Mod, title, StepStatus.Working, "Placing the mod into the Modding Tools...");
        if (!m.StagedAvailable)
            // WO-154: Setup put them there, so something took them -- most often an antivirus.
            return new(StepId.Mod, title, StepStatus.NeedsYou,
                "The mod's files are missing from the install, most likely removed by Windows Security or an antivirus. " +
                "Restore them from quarantine (Windows Security > Virus & threat protection > Protection history) and allow them, or run Setup again.");
        if (s.GameProcessRunning)
            return new(StepId.Mod, title, StepStatus.NeedsYou, "Close the game first, then click Try again.", StepAction.PlaceMod, "Try again");
        if (a.PlaceFailure is not null)
            return new(StepId.Mod, title, StepStatus.NeedsYou, a.PlaceFailure, StepAction.PlaceMod, "Try again");
        return new(StepId.Mod, title, StepStatus.Working, "Placing the mod into the Modding Tools...", StepAction.PlaceMod);
    }

    /// <summary>
    /// WO-157 (2.3): Microsoft's Visual C++ 2013 runtime. The Modding Tools' trace server needs it ("MSVCP120.dll
    /// was not found"); the game and the mod do not, so the step is optional.
    /// </summary>
    private static StepView VcRuntimeStep(SetupSnapshot s, SetupActivity a)
    {
        const string title = "Visual C++ 2013 runtime (needed by the Modding Tools)";
        if (s.VcRuntime2013)
            return new(StepId.VcRuntime, title, StepStatus.Done, "Installed.", Optional: true);
        if (a.InstallingVcRuntime)
            return new(StepId.VcRuntime, title, StepStatus.Working,
                "Microsoft's installer is running. If Windows asks for permission, choose Yes.", Optional: true);
        const string why = "The Modding Tools' trace server needs Microsoft's Visual C++ 2013 runtime; without it Windows says " +
                           "\"MSVCP120.dll was not found\" when the game starts. Host and Join work without it.";
        if (s.VcRedist2013 is not null)
            return new(StepId.VcRuntime, title, StepStatus.NeedsYou,
                (a.VcRuntimeFailure is null ? "" : a.VcRuntimeFailure + " ") + why +
                " Install it: Microsoft's own installer from your Steam library, and Windows asks once for permission.",
                StepAction.InstallVcRuntime, "Install it", Optional: true);
        return new(StepId.VcRuntime, title, StepStatus.NeedsYou,
            why + " Download \"Visual Studio 2013 (VC++ 12.0)\", x64, from Microsoft's page and run it.",
            StepAction.OpenVcDownload, "Open Microsoft's page", Optional: true);
    }

    private static string Percent(double? p) => p is double v ? $" ({v * 100:0}%)" : "";

    internal static string Gb(long bytes) => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
}
