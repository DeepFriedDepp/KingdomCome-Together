// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.ComponentModel;

namespace KCDMP_launcher.Models
{
    /// <summary>WO-154 (Phase 7): how Windows stopped a file the mod needs.</summary>
    public enum LaunchBlockKind
    {
        /// <summary>Windows' app control (Smart App Control, or an App Control policy): 4551, and Smart App Control's own reputation verdicts.</summary>
        AppControl,
        /// <summary>A software restriction or AppLocker policy: 1260.</summary>
        GroupPolicy,
        /// <summary>Windows Security or an antivirus called it a threat: 225, 226.</summary>
        Virus,
        /// <summary>Setup installed it and it is gone or empty now: an antivirus took it.</summary>
        Quarantined,
        /// <summary>Not there, and nothing says it ever was (a development build, a path changed in Settings).</summary>
        Missing,
        /// <summary>Windows refused to start it: 5.</summary>
        AccessDenied,
    }

    /// <summary>
    /// WO-154 (Phase 7): Windows stopping a file the mod needs, told in plain words -- what
    /// happened, what the player can do -- with the file's name only, never a path.
    ///
    /// The evidence (2026-10-02 bundles): a tester's launcher logged <c>Win32Exception (4551):
    /// ... start process '...KCDMP_LauncherInjector.exe' ... An Application Control policy has
    /// blocked this file.</c> at CONNECT three times within ten minutes, each answered with the
    /// generic "couldn't be started, try again". That is Smart App Control (or an App Control
    /// policy) refusing an unsigned program; trying again cannot help.
    /// </summary>
    public sealed record LaunchBlock(LaunchBlockKind Kind, string File, int Code)
    {
        public string KindName => Kind switch
        {
            LaunchBlockKind.AppControl => "app-control",
            LaunchBlockKind.GroupPolicy => "group-policy",
            LaunchBlockKind.Virus => "virus",
            LaunchBlockKind.Quarantined => "quarantined",
            LaunchBlockKind.Missing => "missing",
            _ => "access-denied",
        };

        /// <summary>The one line for the launcher log (the file's name only).</summary>
        public string LogLine => $"MP-LAUNCH blocked kind={KindName} file={File} code={Code}";

        public string Title => Kind switch
        {
            LaunchBlockKind.Quarantined => "A MOD FILE WAS REMOVED",
            LaunchBlockKind.Missing => "A MOD FILE IS MISSING",
            _ => "WINDOWS BLOCKED THE MOD",
        };

        /// <summary>What happened.</summary>
        public string Message => Kind switch
        {
            LaunchBlockKind.AppControl =>
                $"Windows blocked a file the mod needs: {File}. Windows' app control (usually Smart App Control) " +
                "stops programs that have no publisher signature, and this mod has none.",
            LaunchBlockKind.GroupPolicy =>
                $"Windows blocked a file the mod needs: {File}. A policy on this computer (group policy or AppLocker) does not allow it to run.",
            LaunchBlockKind.Virus =>
                $"Windows blocked a file the mod needs: {File}. Windows Security (or your antivirus) reported it as a threat and stopped it.",
            LaunchBlockKind.Quarantined =>
                $"A file the mod needs is gone: {File}. Setup installed it, so Windows Security or your antivirus most likely removed it (quarantine).",
            LaunchBlockKind.Missing =>
                $"A file the mod needs is missing: {File}.",
            _ =>
                $"Windows didn't let the launcher start a file the mod needs: {File} (access denied).",
        };

        /// <summary>What the player can do -- plainly, and for Smart App Control neutrally: it is the player's decision.</summary>
        public string NextStep => Kind switch
        {
            LaunchBlockKind.AppControl =>
                "Smart App Control can't allow a single program: the mod runs only with it turned off. It is in Windows Security > " +
                "App & browser control > Smart App Control. Turning it off lets unsigned programs like this mod run. Once it is off, " +
                "Windows cannot turn it back on without resetting or reinstalling Windows. Whether to do that is your decision. " +
                "On a computer managed by work or school, ask whoever manages it.",
            LaunchBlockKind.GroupPolicy =>
                "On a computer managed by work or school, ask whoever manages it. On your own computer, the rule was set by a program " +
                "or by hand; the mod can't run until it allows this file.",
            LaunchBlockKind.Virus =>
                "If you trust this mod, allow the file in Windows Security > Virus & threat protection > Protection history (or in your " +
                "antivirus) and restore it from quarantine, then try again. Reinstalling puts the file back, but it may be removed " +
                "again until it is allowed.",
            LaunchBlockKind.Quarantined =>
                "Restore it from quarantine and allow it (Windows Security > Virus & threat protection > Protection history, or your " +
                "antivirus), or run the installer again. It may be removed again until it is allowed.",
            LaunchBlockKind.Missing =>
                "Run the installer again. If you changed the paths in Settings, check them.",
            _ =>
                "Your antivirus or a security setting may be blocking it: allow it there and try again. Running the installer again " +
                "can also repair the file.",
        };
    }

    /// <summary>WO-154 (Phase 7): the classification. Pure: the launcher passes what it saw.</summary>
    public static class LaunchBlocks
    {
        // winerror.h
        public const int ErrorFileNotFound = 2;
        public const int ErrorPathNotFound = 3;
        public const int ErrorAccessDenied = 5;
        /// <summary>"Operation did not complete successfully because the file contains a virus or potentially unwanted software."</summary>
        public const int ErrorVirusInfected = 225;
        /// <summary>"This file contains a virus or potentially unwanted software and cannot be opened ... the file has been removed from this location."</summary>
        public const int ErrorVirusDeleted = 226;
        /// <summary>"This program is blocked by group policy."</summary>
        public const int ErrorAccessDisabledByPolicy = 1260;
        /// <summary>
        /// ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION. The tester's Windows said "An Application Control policy
        /// has blocked this file."; Windows 11 22631 says "Your organization used Device Guard to block this
        /// app." for the same code -- the code is what is classified, never the text.
        /// </summary>
        public const int ErrorAppControlPolicy = 4551;

        /// <summary>
        /// The other "System Integrity policy has been violated" verdicts -- Smart App Control's reputation
        /// checks (malicious, potentially unwanted, dangerous extension, reputation service unreachable;
        /// unfriendly file, infrastructure issue, explicitly denied), read off Windows' own messages.
        /// </summary>
        public static bool IsAppControlCode(int code) =>
            code == ErrorAppControlPolicy || code is >= 4556 and <= 4559 or >= 4580 and <= 4582;

        /// <summary>
        /// A start that failed (Process.Start's Win32Exception), classified; null when it is not
        /// Windows stopping the file (the caller's own message stands). <paramref name="installed"/>:
        /// the install manifest lists the file, so "not found" means something removed it.
        /// </summary>
        public static LaunchBlock? FromStartFailure(Exception ex, string file, bool installed) =>
            ex is Win32Exception w ? FromCode(w.NativeErrorCode, file, installed) : null;

        public static LaunchBlock? FromCode(int code, string file, bool installed)
        {
            LaunchBlockKind? kind = code switch
            {
                _ when IsAppControlCode(code) => LaunchBlockKind.AppControl,
                ErrorAccessDisabledByPolicy => LaunchBlockKind.GroupPolicy,
                ErrorVirusInfected or ErrorVirusDeleted => LaunchBlockKind.Virus,
                ErrorFileNotFound or ErrorPathNotFound => installed ? LaunchBlockKind.Quarantined : LaunchBlockKind.Missing,
                ErrorAccessDenied => LaunchBlockKind.AccessDenied,
                _ => null,
            };
            return kind is LaunchBlockKind k ? new LaunchBlock(k, file, code) : null;
        }

        /// <summary>
        /// A file the launch needs, looked at before anything starts. Missing: quarantined when Setup
        /// installed it, else missing. Empty (0 bytes) while Setup installed it with content: what an
        /// antivirus leaves behind. Null: the file is there.
        /// </summary>
        public static LaunchBlock? FromFileCheck(string file, bool exists, long length, bool installed)
        {
            if (!exists) return new LaunchBlock(installed ? LaunchBlockKind.Quarantined : LaunchBlockKind.Missing, file, ErrorFileNotFound);
            if (length == 0 && installed) return new LaunchBlock(LaunchBlockKind.Quarantined, file, 0);
            return null;
        }
    }
}
