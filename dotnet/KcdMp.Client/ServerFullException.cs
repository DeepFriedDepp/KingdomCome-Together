// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// Thrown when the relay rejects us with ServerFull (0x36) -- it was already
/// at its configured player cap when our Handshake arrived. Fatal for this
/// connection attempt, the same way <see cref="ProtocolVersionMismatchException"/>
/// is: retrying immediately cannot help (the relay is not about to grow a
/// slot in the next few seconds), so <see cref="GameBridge.RunAsync"/> stops
/// instead of reconnecting. See Protocol's 0x36 notes for why this packet
/// exists at all (WO-76).
/// </summary>
public sealed class ServerFullException(byte maxPlayers) : Exception(
    $"Relay is full (max {maxPlayers} players).")
{
    public byte MaxPlayers { get; } = maxPlayers;
}
