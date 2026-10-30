// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Reflection;
using System.Text.RegularExpressions;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-211 -- the 0.63.9 beta patch (docs/WO-211-findings.md): the DLL's ops and the build id.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo211.h): 143 Config [bits:4] -> [bits:4] (1 ui_lua_main_only, 2 overflow_reseat,
    /// 4 handover_queue_cap), 144 Status -> text, 145 OverflowReseat [nameLen:1][name] -> [result:1] (0 off, 1 reseated, 2 not an owned
    /// copy, 3 too soon after the last).</summary>
    public const byte Wo211OpConfig = 143, Wo211OpStatus = 144, Wo211OpOverflowReseat = 145;
}

/// <summary>
/// WO-211 0.2: the build id. The version stays the maintainer's (VERSION); builds of one version are told apart by the short commit and the
/// build time, written by tools/Write-BuildId.ps1 into the repo root's git-ignored BUILD_ID before a build and stamped into every assembly
/// (Directory.Build.props: AssemblyMetadata "KcdMpBuildId") and the DLL (KCDMP_BUILD_ID). A build without one is "dev". On the wire the
/// release string is "&lt;version&gt;+&lt;build id&gt;" (a dev build: the version alone) -- the relay (always the host's own build) refuses
/// any other, and a partner on the same version but another build is told so in plain words.
/// </summary>
public static class BuildId
{
    private static readonly Regex Shape = new("^[0-9A-Za-z.-]{1,40}$", RegexOptions.CultureInvariant);

    /// <summary>This build's id (the Protocol assembly carries the same metadata as every assembly of the build), or "dev".</summary>
    public static readonly string Current = Read(typeof(BuildId).Assembly);

    public static string Read(Assembly a)
    {
        foreach (var m in a.GetCustomAttributes<AssemblyMetadataAttribute>())
            if (m.Key == "KcdMpBuildId" && IsBuildId(m.Value)) return m.Value!;
        return "dev";
    }

    public static bool IsBuildId(string? s) => s is not null && Shape.IsMatch(s);

    /// <summary>The release string on the wire: the version, and "+id" for a marked build.</summary>
    public static string Wire(string version, string? id) => IsBuildId(id) && id != "dev" ? version + "+" + id : version;

    public static (string Version, string Build) Split(string? wire)
    {
        wire ??= "";
        int i = wire.IndexOf('+');
        return i < 0 ? (wire, "") : (wire[..i], wire[(i + 1)..]);
    }

    /// <summary>Both strings name the same version but not the same build.</summary>
    public static bool SameVersionOtherBuild(string? a, string? b)
    {
        var x = Split(a); var y = Split(b);
        return x.Version.Length > 0 && x.Version == y.Version && x.Build != y.Build;
    }

    /// <summary>What a player reads when the relay refused this machine's build (theirs = the relay's wire string, the host's build).</summary>
    public static string MismatchText(string mine, string theirs)
    {
        var t = Split(theirs);
        return SameVersionOtherBuild(mine, theirs)
            ? $"Your partner is on a different {t.Version} build ({(t.Build.Length > 0 ? t.Build : "an unmarked build")}); install the same one."
            : $"Your partner runs {t.Version}, you run {Split(mine).Version} -- both players need the same version.";
    }
}
