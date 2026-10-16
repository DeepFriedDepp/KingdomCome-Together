// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-195: engine-free checks of the dead copy's gates (wo195_rules.h). Linked into KCDMP_NativeTests; wo195_tests() returns the failures.
#include <cstdio>
#include <string_view>

#include "wo195_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo195_tests(int* passed) {
    using namespace wo195rules;
    // the gates refuse only a dead body, and only with the switch on
    WCHECK(refuse(true, true) && !refuse(true, false) && !refuse(false, true) && !refuse(false, false), "refuse = switch on and dead");
    WCHECK(!wish_kept(true, true) && wish_kept(true, false) && wish_kept(false, true), "a dead copy's wish is dropped (switch on)");
    // the per-frame gate: only a dead body the mod still holds in a fight
    WCHECK(release_due(true, true, true, false) && release_due(true, true, false, true), "engaged or guard-held and dead: released");
    WCHECK(!release_due(true, true, false, false), "dead but not held: nothing to release (no line every frame)");
    WCHECK(!release_due(true, false, true, true), "alive and held: kept");
    WCHECK(!release_due(false, true, true, true), "switch off: 0.60.9's behaviour (the holds run out on their own clocks)");
    // the 0.60.5 field sequence (tzel_man_13, 21:03:46): the wish re-check came 17 ms before ApplyDeath, the engagement 1 ms after it.
    // With the agent's release ahead of the apply, the copy is marked dead first: every one of them is refused.
    WCHECK(refuse(true, true), "the re-check on the marked copy is refused");
    // the mask bits are distinct and named
    const uint8_t all = kHeldEngage | kHeldGuard | kHeldWish | kHeldPin | kHeldCombat;
    WCHECK(all == 0x1F, "five distinct bits");
    WCHECK(std::string_view(part(kHeldWish | kHeldPin, kHeldPin, "pin")) == "pin" && std::string_view(part(kHeldWish, kHeldPin, "pin")).empty(), "part()");
    // op 57's tri-state
    WCHECK(tri(true, true) == 1 && tri(true, false) == 0 && tri(false, true) == kUnreadable, "tri-state reads");
    *passed = g_pass;
    return g_fail;
}
