#pragma once
// WO-114 Phase 2: the death wake spot within the leash -- the pure half.
// respawn.cpp owns the engine (the spot list, the teleport); this owns the
// choice, and native/tests/wo114_wake_tests.cpp pins it.
//
// For a death with a partner in the world (the other player; the agent sends
// its position every second, pipe 0x20):
//   1. candidates: usable spots at least `minDist` (100 m) from the death,
//      horizontally -- today's rule;
//   2. keep the ones within `radius` (mp_leash_warn_m, 600 m) of the partner,
//      horizontally (the leash's own measure), so a wake never starts a pull;
//   3. not the spot this player woke at last time, if another one is left;
//   4. the nearest of them to the partner;
//   5. none left: wake beside the partner (the WO-124 placement) -- the
//      caller does that, this says so;
//   6. no partner: today's rule (the caller's, unchanged).
// Engine-free: standard headers only.

#include <cmath>
#include <cstdint>

namespace kcdmp::wake {

struct Cand {
    float    x = 0, y = 0, z = 0;   // the navmesh point the teleport lands on
    uint64_t id = 0;                // the spot's link id (stable for the level)
    bool     usable = false;        // on the navmesh (the game's own validity rule)
};

enum class Rule : uint8_t {
    Solo = 0,        // no partner: the caller's rule
    Partner,         // rules 1-4
    PartnerReused,   // rules 1-4, and the last spot was the only one left
    BesidePartner,   // rule 5
};

struct Pick {
    int  index = -1;      // into the candidate array; -1 for Solo / BesidePartner
    Rule rule = Rule::Solo;
    int  farEnough = 0;   // candidates passing rule 1
    int  inLeash = 0;     // ... and rule 2
    float toPartner = -1; // horizontal distance from the pick to the partner
    float fromDeath = -1; // horizontal distance from the pick to the death
};

inline float dist2d(float ax, float ay, float bx, float by) {
    const float dx = ax - bx, dy = ay - by;
    return std::sqrt(dx * dx + dy * dy);
}

inline Pick pick(const Cand* c, int n, const float death[3], const float* partner, float radius, float minDist,
                 uint64_t lastId) {
    Pick p{};
    if (!partner || !c || n <= 0 || !(radius > 0.f)) {
        if (partner && radius > 0.f) p.rule = Rule::BesidePartner;   // a partner but no spots at all
        return p;
    }
    int best = -1, bestLast = -1;
    float bestD = 1e30f, bestLastD = 1e30f;
    for (int i = 0; i < n; ++i) {
        const Cand& s = c[i];
        if (!s.usable) continue;
        if (dist2d(s.x, s.y, death[0], death[1]) < minDist) continue;
        ++p.farEnough;
        const float dp = dist2d(s.x, s.y, partner[0], partner[1]);
        if (dp > radius) continue;
        ++p.inLeash;
        if (lastId != 0 && s.id == lastId) {
            if (dp < bestLastD) { bestLastD = dp; bestLast = i; }
            continue;
        }
        if (dp < bestD) { bestD = dp; best = i; }
    }
    if (best >= 0) { p.index = best; p.rule = Rule::Partner; p.toPartner = bestD; }
    else if (bestLast >= 0) { p.index = bestLast; p.rule = Rule::PartnerReused; p.toPartner = bestLastD; }
    else { p.rule = Rule::BesidePartner; return p; }
    p.fromDeath = dist2d(c[p.index].x, c[p.index].y, death[0], death[1]);
    return p;
}

inline const char* rule_name(Rule r) {
    switch (r) {
        case Rule::Solo: return "solo (today's rule)";
        case Rule::Partner: return "nearest to the partner, within the leash, at least 100 m from the death";
        case Rule::PartnerReused: return "nearest to the partner, within the leash (the last spot again: the only one left)";
        case Rule::BesidePartner: return "no spot within the leash -- beside the partner";
    }
    return "?";
}

} // namespace kcdmp::wake
