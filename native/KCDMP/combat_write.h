// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once

// WO-100.5 Phase 1 -- the first combat WRITE, file-triggered and one-shot.
namespace kcdmp::combatwrite {

// Polled on the main thread. Does nothing at all until kcdmp-combatwrite.txt
// exists and names a command; each distinct command fires exactly once.
void write_watch();

} // namespace kcdmp::combatwrite
