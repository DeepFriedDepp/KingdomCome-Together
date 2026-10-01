// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// Thrown when the relay rejects our protocol version. Fatal: retrying cannot
/// help, so <see cref="GameBridge.RunAsync"/> stops instead of reconnecting.
/// </summary>
public sealed class ProtocolVersionMismatchException(byte serverVersion) : Exception(
    $"Relay speaks protocol v{serverVersion}, this agent speaks v{Protocol.Version}. " +
    "Update both the agent and the relay to the same build.")
{
    public byte ServerVersion { get; } = serverVersion;
}
