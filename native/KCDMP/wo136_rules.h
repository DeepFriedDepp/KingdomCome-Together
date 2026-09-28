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

struct Ev { uint32_t src; double t; int w; };

struct Pick { uint32_t to = kNone; int toScore = 0; int curScore = 0; };

// Which source should an NPC fighting `cur` turn to? `evs` are its recent
// threats (older than the window are ignored). kNone in `to` = keep fighting.
// An NPC with no opponent turns to the first threat; one with an opponent only
// to a source that clearly beats it (kMargin), never twice within the hold.
inline Pick pick(const Ev* evs, size_t n, double now, uint32_t cur, double lastSwitch) {
    Pick p;
    // Up to 8 distinct sources (the host and a few avatars).
    uint32_t src[8]; int sc[8]; int ns = 0;
    for (size_t i = 0; i < n; ++i) {
        if (now - evs[i].t > kWindowS || evs[i].t > now + 0.5) continue;
        int k = 0;
        while (k < ns && src[k] != evs[i].src) ++k;
        if (k == ns) { if (ns == 8) continue; src[ns] = evs[i].src; sc[ns] = 0; ++ns; }
        sc[k] += evs[i].w;
    }
    int best = -1;
    for (int k = 0; k < ns; ++k) {
        if (src[k] == cur) p.curScore = sc[k];
        if (best < 0 || sc[k] > sc[best]) best = k;
    }
    if (best < 0 || src[best] == cur) return p;
    const bool beats = cur == kNone ? sc[best] >= 1 : sc[best] >= p.curScore + kMargin;
    if (!beats || now - lastSwitch < kSwitchHoldS) return p;
    p.to = src[best];
    p.toScore = sc[best];
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
