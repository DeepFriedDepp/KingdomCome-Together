// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
//
// A stand-in for <MT>\Tools\ModdingWorkspaceSetup\WorkspaceSetup.exe (WO-150).
// The prompts and the order of work are the real tool's (docs/WO-150-findings.md);
// this file is written from that description, not copied from the tool.
//
//   FakeWorkspaceSetup --game <root> --mt <root> --mode readkey|readline|stall [--link hard|symlink]
//   (or no arguments, and the same lines in args.txt beside the exe)
//
//   readkey   reads answers with Console.ReadKey, as the shipped tool does (a pipe cannot feed it)
//   readline  reads answers from standard input (what a pipe-drivable tool would do)
//   stall     prints the first prompt and then waits forever (a console frozen by Quick Edit)
//   --link    what "S" makes: the real tool makes symlinks; "hard" stands in for a machine
//             where that is allowed, so the pipe route can be proven end to end here.
using System.Runtime.InteropServices;

// The real tool takes no arguments, so the runner passes none: read them from args.txt beside the exe then.
var argv = args.Length > 0 ? args
    : File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "args.txt")).Where(l => l.Length > 0).ToArray();
var opt = new Dictionary<string, string>();
for (int i = 0; i + 1 < argv.Length; i += 2) opt[argv[i].TrimStart('-')] = argv[i + 1];
string game = opt["game"], mt = opt["mt"], mode = opt.GetValueOrDefault("mode", "readkey");
string linkKind = opt.GetValueOrDefault("link", "symlink");

Console.WriteLine("SteamInternal_SetMinidumpSteamID:  Caching Steam ID:  76561190000000001 [API loaded no]");
Console.WriteLine("Modding tools installed in:   " + mt);
Console.WriteLine("Game installed in:            " + game);

bool symLink = Ask("Do you want to [C]opy packs or [S]ymlink them?.", "SC") == 'S';
bool deleteAll = false;
Mirror(Path.Combine(game, "Data"), Path.Combine(mt, "Data"));
Console.WriteLine();
Mirror(Path.Combine(game, "Localization"), Path.Combine(mt, "Localization"));
foreach (var lv in new DirectoryInfo(Path.Combine(game, "Data", "Levels")).GetDirectories())
{
    Console.WriteLine();
    Console.WriteLine("Copying paks for level:  " + lv.Name);
    Mirror(lv.FullName, Path.Combine(mt, "Data", "Levels", lv.Name));
}
Ask("", "");   // the real tool ends on a ReadKey too
return 0;

char Ask(string message, string answers)
{
    while (true)
    {
        if (message.Length > 0) Console.WriteLine(message);
        char c;
        if (mode == "stall") { Thread.Sleep(Timeout.Infinite); return ' '; }
        if (mode == "readkey") c = char.ToUpper(Console.ReadKey().KeyChar);
        else
        {
            var line = Console.ReadLine();
            if (line is null) return answers.Length > 0 ? answers[0] : ' ';
            c = line.Length > 0 ? char.ToUpper(line[0]) : ' ';
        }
        Console.WriteLine();
        if (answers.Length == 0 || answers.Contains(c)) return c;
    }
}

void Mirror(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var f in new DirectoryInfo(from).GetFiles("*.pak"))
    {
        var target = Path.Combine(to, f.Name);
        Console.WriteLine("Found file:              " + f.FullName);
        if (File.Exists(target))
        {
            Console.WriteLine("File already exists:     " + target);
            bool del = deleteAll;
            if (!deleteAll)
            {
                char c = Ask("Delete File [Y]es/[N]o/Yes to [A]ll:", "YNA");
                deleteAll = c == 'A';
                del = deleteAll || c == 'Y';
            }
            if (!del) { Console.WriteLine("Skipping file:           " + target); continue; }
            Console.WriteLine("Deleting file:           " + target);
            File.Delete(target);
        }
        if (!symLink) { File.Copy(f.FullName, target); continue; }
        Console.WriteLine("Creating link from:      " + f.FullName + "; to: " + target);
        if (linkKind == "hard") { if (!CreateHardLinkW(target, f.FullName, IntPtr.Zero)) throw new IOException("hard link failed " + Marshal.GetLastWin32Error()); }
        else File.CreateSymbolicLink(target, f.FullName);
    }
}

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern bool CreateHardLinkW(string newFile, string existing, IntPtr sa);
