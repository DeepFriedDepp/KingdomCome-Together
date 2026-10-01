// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Reflection;

namespace KcdMp.Server.Features.ClientHandling;

/// <summary>
/// WO-110 R9: this relay's own release version, read from the assembly's
/// InformationalVersion -- KcdMp.Server.csproj sets $(Version) from the
/// repo-root VERSION file at build time, exactly as the agent's
/// ReleaseVersionInfo does, so both sides of a same-build pair carry the same
/// string and a mismatch is a real mixed-version session.
/// </summary>
public static class RelayReleaseVersion
{
    public static readonly string Current =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}
