// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-110 R9: the relay refused this agent's release version (0x3D). Fatal
/// like <see cref="ProtocolVersionMismatchException"/>: both machines must run
/// the same build, so reconnecting cannot help.
/// </summary>
public sealed class ReleaseVersionMismatchException(string relayRelease) : Exception(
    $"Relay runs KCD2-MP {relayRelease}, this machine runs {ReleaseVersionInfo.Current}. " +
    "Both machines must install the same KCD2-MP release; the relay refuses anything else.")
{
    public string RelayRelease { get; } = relayRelease;
}
