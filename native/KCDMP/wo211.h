// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-211 (docs/WO-211-findings.md): the 0.63.9 beta patch's native half.
//   B: the engine's UI-element-to-Lua dispatch never runs off the main thread. The mod's Lua element listeners (the map, the modal
//      dialog, the item transfer, the main menu) make CryAction build a Lua table for EVERY element event -- an element unloaded on the
//      render thread (the field host's crash: "Attempt to Release already released script table") built and freed one there, racing
//      the main thread's Lua. Vanilla registers no Lua element listener, so it never dispatches there.
//   C.2 / C.3: the owner's hand-over never queues behind a long queue; a copy the engine says is over-full gets a clean reseat.
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo211 {

constexpr uint8_t kOpConfig = 143, kOpStatus = 144, kOpOverflowReseat = 145;

bool on(uint32_t bit);   // wo211_rules.h kBit*: the agent's word, less the bench's twin (KCDMP_TEST_W211_OFF)

// C: the owner's counters (wo208.cpp reports here)
void note_overflow_reseat(bool owned, bool installed);
void note_handover_queue_full();

const char* build_id();   // 0.2: this DLL's build id (BUILD_ID at build time; "dev" without one)
int status(char* out, int n);

} // namespace kcdmp::wo211
