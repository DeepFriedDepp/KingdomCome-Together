// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-136 Phase 4: the pure rules of wo136.cpp (unit-tested in
// native/tests/wo136_rules_tests.cpp). No engine access here.
#include <cstdint>
#include <cstddef>

namespace kcdmp::wo136rules {

constexpr uint32_t kPlayer = 0;              // the local player as a threat source
constexpr uint32_t kNone = 0xFFFFFFFFu;      // no opponent / nothing to switch to
constexpr double kWindowS = 6.0;             // a hit or a swing counts this long
constexpr double kSwitchHoldS = 3.0;         // at most one switch per NPC this often
constexpr int kMargin = 3;                   // a challenger must beat the current opponent by this (more than one hit)
// WO-163 (A6, WO-162 Q5): the engine holds no numeric threat to nudge, so the choice is the mod's -- and it is sticky by DAMAGE, not by
// count: an NPC fighting someone turns to a challenger only if the challenger's recent damage (the same 6 s window) exceeds the
// current opponent's by this fraction, on top of the count rule above and the same switch hold. A blocked blow (0 damage) turns nothing.
constexpr float kDamageMargin = 0.25f;
// A landed hit whose damage nobody measured (the host's own blow on an NPC: the DLL marks it, it does not read the victim's health)
// counts this much; a swing counts none. Measured damage always wins.
constexpr float kNominalHitDamage = 10.0f;

// w: a landed hit 2, a swing 1. d: the hit's measured damage in health points (0 = none, e.g. blocked); d < 0 = not measured.
struct Ev { uint32_t src; double t; int w; float d = -1.0f; };

struct Pick { uint32_t to = kNone; int toScore = 0; int curScore = 0; float toDamage = 0.0f, curDamage = 0.0f; };

inline float ev_damage(const Ev& e) { return e.d >= 0.0f ? e.d : (e.w >= 2 ? kNominalHitDamage : 0.0f); }

// Which source should an NPC fighting `cur` turn to? `evs` are its recent
// threats (older than the window are ignored). kNone in `to` = keep fighting.
// An NPC with no opponent turns to the first threat; one with an opponent only
// to a source that clearly beats it (kMargin), never twice within the hold.
inline Pick pick(const Ev* evs, size_t n, double now, uint32_t cur, double lastSwitch) {
    Pick p;
    // Up to 8 distinct sources (the host and a few avatars).
    uint32_t src[8]; int sc[8]; float dm[8]; int ns = 0;
    for (size_t i = 0; i < n; ++i) {
        if (now - evs[i].t > kWindowS || evs[i].t > now + 0.5) continue;
        int k = 0;
        while (k < ns && src[k] != evs[i].src) ++k;
        if (k == ns) { if (ns == 8) continue; src[ns] = evs[i].src; sc[ns] = 0; dm[ns] = 0.0f; ++ns; }
        sc[k] += evs[i].w;
        dm[k] += ev_damage(evs[i]);
    }
    int best = -1;
    for (int k = 0; k < ns; ++k) {
        if (src[k] == cur) { p.curScore = sc[k]; p.curDamage = dm[k]; continue; }
        // Nobody to be sticky to: the strongest by count. Fighting someone: the challenger who hurt it most (count breaks a tie).
        const bool better = best < 0 || (cur == kNone ? sc[k] > sc[best]
                                                      : (dm[k] > dm[best] || (dm[k] == dm[best] && sc[k] > sc[best])));
        if (better) best = k;
    }
    if (best < 0) return p;
    const bool beats = cur == kNone ? sc[best] >= 1 : sc[best] >= p.curScore + kMargin;
    if (!beats || now - lastSwitch < kSwitchHoldS) return p;
    if (cur != kNone && !(dm[best] > 0.0f && dm[best] > p.curDamage * (1.0f + kDamageMargin))) return p;   // WO-163 (A6): the hysteresis
    p.to = src[best];
    p.toScore = sc[best];
    p.toDamage = dm[best];
    return p;
}

// The host is down: which avatar (index into `dist`, the avatars' distances to
// the NPC, negative = unknown) takes over this NPC? -1 = none within reach.
inline int handover_pick(const float* dist, int n, float reachM) {
    int bi = -1; float bd = reachM;
    for (int i = 0; i < n; ++i)
        if (dist[i] >= 0 && dist[i] < bd) { bd = dist[i]; bi = i; }
    return bi;
}

} // namespace kcdmp::wo136rules
