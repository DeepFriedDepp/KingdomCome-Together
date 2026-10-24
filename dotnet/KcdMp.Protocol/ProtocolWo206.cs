// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-206 -- the test bench: checkpoint digests across both machines, the animation probe (docs/WO-206-findings.md).
// No new message type: the checkpoint marker rides the W203 side channel.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    // W203 kind 10 (W203DigestCheckpoint, ProtocolWo203.cs), host -> joiner: "v1|<world epoch>|<checkpoint seq>" -- take this
    // checkpoint's digest now.

    /// <summary>The DLL's ops (native/KCDMP/wo206.h): 120 Status, 121 DigestPaths, 122 DigestArm, 123 DigestRead, 124 Probe.</summary>
    public const byte Wo206OpStatus = 120, Wo206OpDigestPaths = 121, Wo206OpDigestArm = 122, Wo206OpDigestRead = 123, Wo206OpProbe = 124;
}
