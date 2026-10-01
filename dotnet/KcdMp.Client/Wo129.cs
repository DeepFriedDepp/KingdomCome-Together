// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-129: small, testable rules from the first shared-world session.
/// </summary>
public static class Wo129
{
    /// <summary>
    /// The age of a timestamp the HOST wrote with its own wall clock, on this
    /// machine's wall clock. <paramref name="offsetMs"/> is the measured clock
    /// offset (the relay's clock minus this machine's, WO-98 MP-CLOCK; the relay
    /// runs on the host), so host-time now = local now + offset. Without an
    /// offset the raw difference is returned and <paramref name="corrected"/>
    /// is false. The first session's joiner ran 8.04 s ahead of its host and
    /// logged every host save as 8 s old.
    /// </summary>
    public static long HostStampAgeMs(long localNowUnixMs, long hostStampUnixMs, double? offsetMs, out bool corrected)
    {
        corrected = offsetMs is double o && double.IsFinite(o);
        double hostNow = localNowUnixMs + (corrected ? offsetMs!.Value : 0.0);
        return (long)Math.Round(hostNow - hostStampUnixMs);
    }
}
