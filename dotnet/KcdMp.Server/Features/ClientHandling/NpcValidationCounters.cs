// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Server.Features.ClientHandling;

/// <summary>
/// WO-66: running totals of NpcStateUp packets the relay rejected, one per
/// reason tag (matching the [WO66-REJECT] log lines). Served by
/// GET api/information/npc-validation. Speed includes non-finite positions;
/// Rotation is non-finite rotZ. All zero on a healthy wire.
/// </summary>
public record NpcValidationCounters(long Speed, long Rotation, long ReservedName, long StaleOwner);
