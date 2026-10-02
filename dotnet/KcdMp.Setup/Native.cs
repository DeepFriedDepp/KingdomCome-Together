// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KcdMp.Setup;

/// <summary>The few Win32 calls .NET has no managed API for: file identity, hard links, link errors.</summary>
internal static partial class Native
{
    public const int ERROR_INVALID_FUNCTION = 1;    // the file system has no hard links (FAT32, exFAT)
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_NOT_SAME_DEVICE = 17;     // a hard link across volumes
    public const int ERROR_SHARING_VIOLATION = 32;   // the target is open (the game is running)
    public const int ERROR_NOT_SUPPORTED = 50;
    public const int ERROR_INVALID_PARAMETER = 87;   // pre-1703 Windows: no unprivileged symlink flag
    public const int ERROR_TOO_MANY_LINKS = 1142;
    public const int ERROR_PRIVILEGE_NOT_HELD = 1314; // a symlink without admin rights or Developer Mode

    public const uint SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE = 0x2;
    public const uint MOVEFILE_REPLACE_EXISTING = 0x1;

    private const uint FILE_READ_ATTRIBUTES = 0x80;
    private const uint FILE_SHARE_ALL = 0x1 | 0x2 | 0x4;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationLow, CreationHigh;
        public uint AccessLow, AccessHigh;
        public uint WriteLow, WriteHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh, FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh, FileIndexLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string newFile, string existingFile, IntPtr security);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CreateSymbolicLinkW(string link, string target, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileExW(string from, string to, uint flags);

    /// <summary>
    /// The file's identity (volume + index) and link count, following any
    /// symlink or junction on the way -- so a symlink to the game's file, a hard
    /// link to it, and a file reached through a junctioned folder all answer
    /// with the game file's own identity. Null when it cannot be opened.
    /// </summary>
    public static ByHandleFileInformation? Identity(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(@"\\", StringComparison.Ordinal)) full = @"\\?\" + full;   // long paths; UNC stays as it is
        using var h = CreateFile(full, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero,
                                 OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h.IsInvalid) return null;
        return GetFileInformationByHandle(h, out var info) ? info : null;
    }

    public static bool SameFile(ByHandleFileInformation a, ByHandleFileInformation b) =>
        a.VolumeSerialNumber == b.VolumeSerialNumber && a.FileIndexHigh == b.FileIndexHigh && a.FileIndexLow == b.FileIndexLow;

    public static int CreateHardLink(string newFile, string existingFile) =>
        CreateHardLinkW(newFile, existingFile, IntPtr.Zero) ? 0 : Marshal.GetLastPInvokeError();

    public static int CreateFileSymlink(string link, string target)
    {
        if (CreateSymbolicLinkW(link, target, SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE)) return 0;
        int err = Marshal.GetLastPInvokeError();
        if (err != ERROR_INVALID_PARAMETER) return err;
        return CreateSymbolicLinkW(link, target, 0) ? 0 : Marshal.GetLastPInvokeError();
    }

    public static int MoveReplace(string from, string to) =>
        MoveFileExW(from, to, MOVEFILE_REPLACE_EXISTING) ? 0 : Marshal.GetLastPInvokeError();
}
