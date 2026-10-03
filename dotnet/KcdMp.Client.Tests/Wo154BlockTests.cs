// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.ComponentModel;
using System.Diagnostics;
using KCDMP_launcher.Models;

namespace KcdMp.Client.Tests;

/// <summary>WO-154 (Phase 7): Windows blocking a file the mod needs, classified and told in plain words.</summary>
public class Wo154BlockTests
{
    [Theory]
    [InlineData(4551, LaunchBlockKind.AppControl)]    // the field evidence: KCDMP_LauncherInjector.exe at CONNECT
    [InlineData(4556, LaunchBlockKind.AppControl)]    // Smart App Control: malicious reputation
    [InlineData(4559, LaunchBlockKind.AppControl)]    // ... reputation service unreachable
    [InlineData(4580, LaunchBlockKind.AppControl)]    // ... unfriendly file
    [InlineData(4582, LaunchBlockKind.AppControl)]    // ... explicitly denied
    [InlineData(1260, LaunchBlockKind.GroupPolicy)]
    [InlineData(225, LaunchBlockKind.Virus)]
    [InlineData(226, LaunchBlockKind.Virus)]
    [InlineData(5, LaunchBlockKind.AccessDenied)]
    public void A_start_windows_refused_is_classified_by_its_code(int code, LaunchBlockKind kind)
    {
        var b = LaunchBlocks.FromStartFailure(new Win32Exception(code), "KCDMP_LauncherInjector.exe", installed: true);
        Assert.NotNull(b);
        Assert.Equal(kind, b!.Kind);
        Assert.Equal(code, b.Code);
    }

    [Theory]
    [InlineData(4550)]   // a policy rollback: not a block of our file
    [InlineData(4552)]
    [InlineData(193)]    // not a valid Win32 application: the caller's own message
    [InlineData(1223)]   // the player said No to UAC
    [InlineData(0)]
    public void Anything_else_is_not_a_block(int code)
    {
        Assert.Null(LaunchBlocks.FromCode(code, "KcdMpClient.exe", installed: true));
        Assert.Null(LaunchBlocks.FromStartFailure(new InvalidOperationException("x"), "KcdMpClient.exe", true));
    }

    [Fact]
    public void Not_found_at_start_is_a_quarantine_when_setup_installed_it()
    {
        // A real start of a file that is not there: Windows' own code 2, through Process.Start.
        string gone = Path.Combine(Path.GetTempPath(), "kcdmp-wo154-gone-" + Guid.NewGuid().ToString("N"), "KCDMP_LauncherInjector.exe");
        var ex = Assert.ThrowsAny<Exception>(() => Process.Start(new ProcessStartInfo { FileName = gone, UseShellExecute = false }));
        var b = LaunchBlocks.FromStartFailure(ex, Path.GetFileName(gone), installed: true);
        Assert.Equal(LaunchBlockKind.Quarantined, b!.Kind);
        Assert.Equal(2, b.Code);
        Assert.Equal(LaunchBlockKind.Missing, LaunchBlocks.FromStartFailure(ex, Path.GetFileName(gone), installed: false)!.Kind);
    }

    [Fact]
    public void A_file_gone_or_emptied_before_the_launch_is_told_before_windows_error()
    {
        Assert.Equal(LaunchBlockKind.Quarantined, LaunchBlocks.FromFileCheck("KCDMP.dll", exists: false, length: 0, installed: true)!.Kind);
        Assert.Equal(LaunchBlockKind.Missing, LaunchBlocks.FromFileCheck("KCDMP.dll", exists: false, length: 0, installed: false)!.Kind);
        var empty = LaunchBlocks.FromFileCheck("kdcmp.pak", exists: true, length: 0, installed: true)!;
        Assert.Equal(LaunchBlockKind.Quarantined, empty.Kind);
        Assert.Equal(0, empty.Code);
        Assert.Null(LaunchBlocks.FromFileCheck("KCDMP.dll", exists: true, length: 0, installed: false));   // a dev build's own business
        Assert.Null(LaunchBlocks.FromFileCheck("KCDMP.dll", exists: true, length: 123456, installed: true));
    }

    [Fact]
    public void The_log_line_names_the_kind_the_file_and_the_code()
    {
        var b = LaunchBlocks.FromCode(4551, "KCDMP_LauncherInjector.exe", true)!;
        Assert.Equal("MP-LAUNCH blocked kind=app-control file=KCDMP_LauncherInjector.exe code=4551", b.LogLine);
        Assert.Equal("MP-LAUNCH blocked kind=quarantined file=kdcmp.pak code=2", LaunchBlocks.FromFileCheck("kdcmp.pak", false, 0, true)!.LogLine);
    }

    [Fact]
    public void Smart_app_control_is_explained_plainly_with_its_trade_off()
    {
        var b = LaunchBlocks.FromCode(4551, "KCDMP_LauncherInjector.exe", true)!;
        Assert.Contains("Windows blocked a file the mod needs: KCDMP_LauncherInjector.exe", b.Message);
        Assert.Contains("Windows Security > App & browser control > Smart App Control", b.NextStep);
        Assert.Contains("lets unsigned programs like this mod run", b.NextStep);
        Assert.Contains("cannot turn it back on without resetting or reinstalling Windows", b.NextStep);
        Assert.Contains("your decision", b.NextStep);
    }

    [Theory]
    [InlineData(4551)]
    [InlineData(1260)]
    [InlineData(225)]
    [InlineData(5)]
    [InlineData(2)]
    public void Every_message_names_the_file_and_shows_no_path(int code)
    {
        foreach (bool installed in new[] { true, false })
        {
            var b = LaunchBlocks.FromCode(code, "KcdMpServer.exe", installed)!;
            Assert.Contains("KcdMpServer.exe", b.Message);
            foreach (string text in new[] { b.Title, b.Message, b.NextStep, b.LogLine })
            {
                Assert.DoesNotContain("\\", text);
                Assert.DoesNotContain(":\\", text);
                Assert.DoesNotContain("Users", text);
            }
            Assert.False(string.IsNullOrWhiteSpace(b.NextStep));
        }
    }
}
