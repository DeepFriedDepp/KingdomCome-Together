// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;
using System.Text;

namespace KcdMp.Client;

/// <summary>
/// WO-202 I.1 crash_capture, the agent's half. The DLL writes its report into the game folder's kcdmp-crashes\crash-*\ the moment the
/// game crashes (native/KCDMP/crash_capture.h); the agent outlives the game, so it adds what only it knows: the last statements it
/// applied in the game's Lua (agent-statements.txt), and the game process's exit code. A game that ends without the engine's quit line
/// and without a report from the DLL (a fast-fail, a kill) gets a folder of its own, so the bundle and its briefing still show it.
/// </summary>
public static class Wo202CrashWatch
{
    public const int KeepStatements = 50;
    private static readonly object Lock = new();
    private static readonly Queue<(DateTime At, string Lua)> Applied = new();
    private static volatile bool _quitSeen;
    private static Process? _game;
    private static int _gamePid;
    private static DateTime _gameStart;
    private static int _started;

    /// <summary>The transport: these statements reached the game (its ExecuteString answered).</summary>
    public static void NoteApplied(string lua)
    {
        string head = lua.Length > 400 ? lua[..400] + " ..." : lua;
        lock (Lock)
        {
            Applied.Enqueue((DateTime.Now, head.Replace('\n', ' ').Replace('\r', ' ')));
            while (Applied.Count > KeepStatements) Applied.Dequeue();
        }
    }

    /// <summary>The engine printed its quit line: the process that ends next ends on purpose.</summary>
    public static void NoteQuit() => _quitSeen = true;

    public static IReadOnlyList<(DateTime At, string Lua)> LastApplied() { lock (Lock) return Applied.ToList(); }

    /// <summary>Starts the 2 s process watch (once per agent).</summary>
    public static void Start(Func<string?> gameFolder, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { Tick(gameFolder); } catch { }
                try { await Task.Delay(2000, ct); } catch { return; }
            }
        }, CancellationToken.None);
    }

    private static void Tick(Func<string?> gameFolder)
    {
        if (_game is null)
        {
            Process? newest = null;
            foreach (var p in Process.GetProcessesByName("KingdomCome"))
            {
                try { if (newest is null || p.StartTime > newest.StartTime) newest = p; } catch { }
            }
            if (newest is null) return;
            _game = newest; _gamePid = newest.Id; _quitSeen = false;
            try { _gameStart = newest.StartTime; } catch { _gameStart = DateTime.Now; }
            return;
        }
        bool exited;
        try { exited = _game.HasExited; } catch { exited = true; }
        if (!exited) return;
        string code = "unknown";
        try { code = $"0x{_game.ExitCode:X8}"; } catch { }
        var ended = DateTime.Now;
        bool quit = _quitSeen;
        var game = _game;
        _game = null;
        game.Dispose();
        if (quit) { Console.WriteLine($"WO202-CRASH the game (pid {_gamePid}) quit normally (exit {code})"); return; }
        string folder = WriteAgentPart(gameFolder(), ended, code);
        Console.WriteLine($"WO202-CRASH the game (pid {_gamePid}, up {(ended - _gameStart).TotalMinutes:F1} min) ended WITHOUT its quit line, exit {code} -- "
                          + $"the last {LastApplied().Count} statements this agent applied are in {folder}");
        foreach (var (at, lua) in LastApplied().TakeLast(10))
            Console.WriteLine($"WO202-CRASH last statement {at:HH:mm:ss.fff} {(lua.Length > 200 ? lua[..200] + " ..." : lua)}");
    }

    /// <summary>agent-statements.txt into the DLL's report of this crash (written within the last 5 minutes), or a folder of its own.</summary>
    public static string WriteAgentPart(string? gameFolder, DateTime ended, string exitCode)
    {
        if (string.IsNullOrWhiteSpace(gameFolder)) return "(no game folder known)";
        string root = Path.Combine(gameFolder, "kcdmp-crashes");
        try
        {
            Directory.CreateDirectory(root);
            var dllReport = new DirectoryInfo(root).GetDirectories("crash-*")
                .Where(d => d.LastWriteTime >= ended.AddMinutes(-5) && !d.Name.EndsWith("-agent", StringComparison.Ordinal))
                .OrderByDescending(d => d.LastWriteTime).FirstOrDefault();
            string dir = dllReport?.FullName ?? Path.Combine(root, $"crash-{ended:yyyyMMdd-HHmmss}-{_gamePid}-agent");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine($"Kingdom Come: Together -- the agent's part of a crash report (WO-202 crash_capture)");
            sb.AppendLine($"game process {_gamePid}, started {_gameStart:yyyy-MM-dd HH:mm:ss}, ended {ended:yyyy-MM-dd HH:mm:ss} without its quit line, exit code {exitCode}");
            if (dllReport is null)
                sb.AppendLine("The mod's DLL wrote no report for this end: a fast-fail (stack-buffer overrun, heap corruption), a kill, or a crash before the DLL was in. "
                              + "The bundle's Windows error reports and Application event log entries name the module.");
            sb.AppendLine();
            sb.AppendLine($"the last {LastApplied().Count} statements this agent applied in the game's Lua (oldest first):");
            foreach (var (at, lua) in LastApplied()) sb.AppendLine($"{at:HH:mm:ss.fff}  {lua}");
            File.WriteAllText(Path.Combine(dir, "agent-statements.txt"), sb.ToString());
            return dir;
        }
        catch (Exception ex) { return $"(not written: {ex.Message})"; }
    }
}
