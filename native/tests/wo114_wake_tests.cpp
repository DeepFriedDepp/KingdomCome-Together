// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-114 Phase 2: engine-free checks of the wake spot within the leash
// (native/KCDMP/wake_pick.h). Linked into KCDMP_NativeTests; wo114_wake_tests()
// returns the number of failures.
//
// The regression this guards: the first two-player session (0.30.0) woke all
// four deaths at the same spot, 309-363 m from the death, because "the nearest
// spot at least 100 m away" never looked at where the other player was.
#include <cstdio>
#include <vector>

#include "wake_pick.h"

using namespace kcdmp::wake;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

Cand spot(float x, float y, uint64_t id, bool usable = true) { Cand c; c.x = x; c.y = y; c.z = 100; c.id = id; c.usable = usable; return c; }
} // namespace

int wo114_wake_tests(int* passed) {
    const float death[3] = {0, 0, 100};
    const float partner[3] = {400, 0, 100};   // the WO's test: the partner 400 m away

    // The field geometry, roughly: one spot 330 m behind the death, others around.
    std::vector<Cand> c = {
        spot(-330, 0, 1),     // the session's "same spot every time": nearest >= 100 m from the death
        spot(50, 0, 2),       // too close to the death (< 100 m)
        spot(300, 0, 3),      // 100 m from the partner
        spot(420, 30, 4),     // 36 m from the partner: the nearest to it
        spot(1200, 0, 5),     // 800 m from the partner: outside the leash
        spot(410, 0, 6, false), // nearest of all to the partner, but off the navmesh
    };

    // rules 1-4: nearest to the partner among >= 100 m from the death and <= 600 m from the partner
    Pick p = pick(c.data(), (int)c.size(), death, partner, 600.f, 100.f, 0);
    WCHECK(p.rule == Rule::Partner && p.index == 3, "partner rule picks spot 4 (index %d, rule %d)", p.index, (int)p.rule);
    WCHECK(p.farEnough == 4 && p.inLeash == 2, "counts: far enough %d (4), in leash %d (2)", p.farEnough, p.inLeash);
    WCHECK(p.toPartner > 35.f && p.toPartner < 37.f, "36 m from the partner (%.1f)", p.toPartner);
    WCHECK(p.toPartner <= 600.f, "within the warning distance");

    // rule 3: not the last spot again while another is left
    p = pick(c.data(), (int)c.size(), death, partner, 600.f, 100.f, 4);
    WCHECK(p.rule == Rule::Partner && p.index == 2, "last spot 4 skipped -> spot 3 (index %d)", p.index);
    // ... but reused when it is the only one inside the leash
    std::vector<Cand> one = {spot(-330, 0, 1), spot(420, 30, 4)};
    p = pick(one.data(), (int)one.size(), death, partner, 600.f, 100.f, 4);
    WCHECK(p.rule == Rule::PartnerReused && p.index == 1, "the only spot in the leash is reused (index %d, rule %d)", p.index, (int)p.rule);

    // rule 5: nothing within the leash -> beside the partner
    const float farPartner[3] = {5000, 5000, 100};
    p = pick(c.data(), (int)c.size(), death, farPartner, 600.f, 100.f, 0);
    WCHECK(p.rule == Rule::BesidePartner && p.index < 0, "no spot near a far partner -> beside (rule %d)", (int)p.rule);
    p = pick(nullptr, 0, death, partner, 600.f, 100.f, 0);
    WCHECK(p.rule == Rule::BesidePartner, "a partner but no spots at all -> beside");

    // rule 6: no partner -> the caller's own rule
    p = pick(c.data(), (int)c.size(), death, nullptr, 600.f, 100.f, 0);
    WCHECK(p.rule == Rule::Solo && p.index < 0, "no partner -> solo");
    p = pick(c.data(), (int)c.size(), death, partner, 0.f, 100.f, 0);
    WCHECK(p.rule == Rule::Solo, "radius 0 (leash off) -> solo");

    // the 100 m rule still holds: a partner standing on the death spot
    p = pick(c.data(), (int)c.size(), death, death, 600.f, 100.f, 0);
    WCHECK(p.index >= 0 && p.fromDeath >= 100.f, "a partner at the death: the pick is still >= 100 m away (%.1f)", p.fromDeath);
    WCHECK(p.index == 2, "... and it is spot 3, nearest to the partner past 100 m (index %d)", p.index);

    // horizontal only: a 90 m height gap does not matter
    std::vector<Cand> hill = {spot(400, 0, 7)};
    hill[0].z = 190;
    p = pick(hill.data(), 1, death, partner, 600.f, 100.f, 0);
    WCHECK(p.index == 0 && p.toPartner < 0.01f, "height ignored (%.2f)", p.toPartner);

    // the radius edge is inclusive
    std::vector<Cand> edge = {spot(1000, 0, 8)};
    p = pick(edge.data(), 1, death, partner, 600.f, 100.f, 0);
    WCHECK(p.index == 0, "exactly 600 m from the partner is inside");

    if (passed) *passed = g_pass;
    return g_fail;
}
