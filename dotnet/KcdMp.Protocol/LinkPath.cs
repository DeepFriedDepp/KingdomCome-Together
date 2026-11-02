// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

/// <summary>
/// WO-213: what carries one link right now, as the scheduler sees it. Known = the path reports its own numbers (Steam's real-time
/// status; the test shaper); a plain TCP socket does not, and its own buffer is kept small instead (the relay's SendBufferSize).
/// </summary>
public readonly record struct LinkPathStatus(bool Known, int RateBytesPerS, int PendingBytes, int QueueMs)
{
    /// <summary>With <see cref="ILinkPath.StateOwnLane"/>: what the events' lane still holds (PendingBytes is then the state lane's).</summary>
    public int EventPendingBytes { get; init; }
}

/// <summary>
/// WO-213 B/C: one link's way out. <see cref="SendAsync"/> with <c>state</c> true sends a "latest wins" state frame the path's
/// unreliable way when it has one (Steam's state lane, the shaper's loss model) -- each call is one whole frame, so a lost one never
/// splits the stream the reader reassembles; false (an event) is reliable and in order.
/// </summary>
public interface ILinkPath
{
    /// <summary>"tcp", "steam" or "shaped".</summary>
    string Transport { get; }
    bool CanSendUnreliable { get; }
    /// <summary>State has a lane of its own beside the events (Steam's lane 1): the path shares its rate between the two, so an event
    /// burst does not spend the state's budget and <see cref="Status"/>'s queue is the state lane's.</summary>
    bool StateOwnLane => false;
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, bool state, CancellationToken ct);
    LinkPathStatus Status();
}
