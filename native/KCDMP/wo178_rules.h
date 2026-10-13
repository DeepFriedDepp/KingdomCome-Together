// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-178: the engine-free rules of wo178.cpp and of WO-173's fallback changes (C4), pinned by native/tests/wo178_tests.cpp.
//
//   * where the hostility test is: the player controller's opponent selection calls a selector; the selector loads the player's combat
//     RPG (`mov rcx,[rcx+disp32]`, the same offset WO-165 reads from the collision handler) and calls the test at once (`call rel32`).
//     find_rpg_call reads that pair out of a function's bytes; owner_offset reads where the test loads its owning combat actor.
//   * when the answer is "hostile": the asker is the local player, the candidate is a copy the agent named as fighting a player (or, on the
//     host with mp_host_lock_answer, an NPC fighting a partner's figure), and none of the exclusions holds. Everything else is the game's.
//   * C4: a joiner's blow waits up to kHoldS for the figure's strike window before it may fall back; a fallback blow's stamina is at most
//     what the engine took for that attack kind (its running median, kDefaultFallbackSt until known), and raw blows never take an NPC's
//     stamina below kStaminaFloorShare of its maximum.

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo178rules {

// ------------------------------------------------------------------------------------------------------------ finding the test in code

struct RpgCall {
    uintptr_t target = 0;   // the called function
    uint32_t disp = 0;      // the combat RPG's offset in the combat actor
    int count = 0;          // matches with a plausible offset (the caller wants exactly one)
};

constexpr uint32_t kRpgDispMin = 0x100, kRpgDispMax = 0x2000;

// `mov rcx,[rcx+disp32]` (48 8B 89 d32) immediately followed by `call rel32` (E8 r32), in b[0..n) loaded at `base`. wantDisp != 0 accepts
// only that offset; otherwise any offset in [kRpgDispMin, kRpgDispMax].
inline RpgCall find_rpg_call(const uint8_t* b, size_t n, uintptr_t base, uint32_t wantDisp) {
    RpgCall r{};
    if (!b || n < 12) return r;
    for (size_t i = 0; i + 12 <= n; ++i) {
        if (b[i] != 0x48 || b[i + 1] != 0x8B || b[i + 2] != 0x89 || b[i + 7] != 0xE8) continue;
        uint32_t d = 0;
        std::memcpy(&d, b + i + 3, 4);
        if (wantDisp ? d != wantDisp : (d < kRpgDispMin || d > kRpgDispMax)) continue;
        int32_t rel = 0;
        std::memcpy(&rel, b + i + 8, 4);
        const uintptr_t t = base + i + 12 + static_cast<intptr_t>(rel);
        if (r.count == 0) { r.target = t; r.disp = d; r.count = 1; }
        else if (t != r.target) ++r.count;   // a second, different target: ambiguous (the same call again is not)
    }
    return r;
}

// The test keeps `this` in rbx (48 8B D9 = mov rbx,rcx) and then loads its owning combat actor with `mov rcx,[rbx+disp8]` (48 8B 4B d8).
// Returns that offset, or -1 when the shape is not there within the first n bytes after the prologue.
inline int owner_offset(const uint8_t* b, size_t n) {
    if (!b) return -1;
    bool kept = false;
    for (size_t i = 0; i + 4 <= n; ++i) {
        if (b[i] == 0x48 && b[i + 1] == 0x8B && b[i + 2] == 0xD9) { kept = true; i += 2; continue; }
        if (kept && b[i] == 0x48 && b[i + 1] == 0x8B && b[i + 2] == 0x4B) return b[i + 3];
    }
    return -1;
}

// ------------------------------------------------------------------------------------------------------------ the answer

// Why a candidate may be hostile to the local player (the agent's word for a copy; the host's own for an NPC on a figure).
enum class Why : uint8_t { None = 0, Engaged = 1, PartnerFight = 2, FigureFight = 3 };
inline const char* why_name(Why w) {
    switch (w) {
        case Why::Engaged: return "engaged";
        case Why::PartnerFight: return "partner-fight";
        case Why::FigureFight: return "figure-fight";
        default: return "-";
    }
}
inline bool why_valid(uint8_t w) { return w >= 1 && w <= 3; }

// Why a listed candidate still gets the game's answer.
enum class Ex : uint8_t { None = 0, Figure = 1, Arranged = 2, Dead = 3, Down = 4 };
inline const char* ex_name(Ex e) {
    switch (e) {
        case Ex::Figure: return "figure";
        case Ex::Arranged: return "arranged-fight";
        case Ex::Dead: return "dead";
        case Ex::Down: return "knocked-out";
        default: return "-";
    }
}

struct Candidate {
    bool figure = false;     // a partner's figure (never lockable by this answer)
    bool arranged = false;   // its soul carries a quest's arranged-fight context (WO-176)
    bool dead = false;       // WO-172's dead gate
    bool down = false;       // knocked out (the agent's word, from the host's stream)
};

// The first exclusion that holds, in this order: figure, arranged fight, dead, down.
inline Ex exclusion(const Candidate& c) {
    if (c.figure) return Ex::Figure;
    if (c.arranged) return Ex::Arranged;
    if (c.dead) return Ex::Dead;
    if (c.down) return Ex::Down;
    return Ex::None;
}

// The whole decision: "hostile" only when the switch is on, the asker is the local player, the candidate is listed and nothing excludes it.
inline bool answer_hostile(bool on, bool askerIsLocalPlayer, bool listed, Ex ex) {
    return on && askerIsLocalPlayer && listed && ex == Ex::None;
}

// An offer the agent stopped refreshing is gone (the host streams a fighting NPC's combat state at least once a second).
constexpr double kOfferTtlS = 3.5;
inline bool offer_live(double at, double now) { return at > 0 && now >= at && now - at <= kOfferTtlS; }

// The table the hook reads: how many rows fit (more fighters than this near one player is not a fight anyone can lock in).
constexpr int kMaxRows = 48;

// ------------------------------------------------------------------------------------------------------------ the player's recoil
// An enemy copy's blow replayed on the local player plays his hit reaction only within this planar distance of the striking copy:
// the engine's hit core lands nothing past reach, and a recoil for a blow that cannot reach looks like a hit from thin air.
// The reach is the swing's own (its row's hit_distance -- 0.6..1.8 m across the game's attack rows) plus kReachSlackM for the stream's
// lag and the two bodies; kRecoilReachM only when the row says nothing (an older agent, a row without the column).
constexpr float kRecoilReachM = 2.0f;
constexpr float kReachSlackM = 0.4f;
inline float reach_limit(float hitDistance) {
    return (std::isfinite(hitDistance) && hitDistance > 0.1f) ? hitDistance + kReachSlackM : kRecoilReachM;
}
inline bool in_reach(float d, float limit) { return std::isfinite(d) && d >= 0.0f && std::isfinite(limit) && d <= limit; }
inline bool recoil_in_reach(float d) { return in_reach(d, kRecoilReachM); }
// The engine's "target reachable by attack" for a copy's wind-up: within the row's alignment range (the engine steps the attacker in over
// it during the swing -- 1.6..2.7 m for the longsword stab), else within its reach.
inline bool target_reachable(float d, float hitDistance, float alignMax) {
    if (std::isfinite(alignMax) && alignMax > 0.1f) return in_reach(d, alignMax + kReachSlackM);
    return in_reach(d, reach_limit(hitDistance));
}

// The engine's own reach for the attacker's weapon (C_CombatActor's reach: the weapon's data -- its length + 0.4 m against its class
// reach and the guard -- the same number the engine's "can hit by attack" test compares with the distance to its target every frame).
// A copy's blow lands, and the green shield opens, only within it; kEngineReachSlackM for the stream's lag. The row's number when the
// engine's could not be read (another build, a body without a weapon).
constexpr float kEngineReachSlackM = 0.2f;
inline bool engine_reach_ok(float r) { return std::isfinite(r) && r >= 0.3f && r <= 8.0f; }
inline float copy_reach_limit(float engineReach, float hitDistance) {
    return engine_reach_ok(engineReach) ? engineReach + kEngineReachSlackM : reach_limit(hitDistance);
}
// A blow LANDS only at contact distance: the row's hit_distance + slack (the swing's own, 0.6..1.8 m), never the wind-up reach above
// (the engine's includes the attacker's step in -- 2.7 m for a bandit's sword). Capped by the engine's reach when that is shorter.
inline float contact_limit(float engineReach, float hitDistance) {
    const float row = reach_limit(hitDistance);
    return engine_reach_ok(engineReach) && engineReach + kEngineReachSlackM < row ? engineReach + kEngineReachSlackM : row;
}

// ------------------------------------------------------------------------------------------------------------ the player's own block
// A copy's swing is an animation on the joiner, not an engine attack: his held block never reaches the engine's "block in place" nor turns
// to the swing (live: State 0x80, block zone 1 vs the swing's zone 5 -> zone mismatch -> a full hit while he held block). So his own
// block decides: holding block (0x80 PreparingToParry or 0x100 ParryInPlace) and facing the striking copy within kOwnBlockFacingCos -> the
// engine is told he blocks (hit-in +3) and computes the block itself.
constexpr float kOwnBlockFacingCos = 0.25f;   // about 75 degrees either side
inline bool own_block(bool on, int32_t victimState, bool stateValid, float facingCos) {
    if (!on || !stateValid || !(victimState & 0x180)) return false;
    return std::isfinite(facingCos) && facingCos >= kOwnBlockFacingCos;
}

// ------------------------------------------------------------------------------------------------------------ C4: the fallback

constexpr double kHoldS = 0.8;                 // a joiner's hit waits this long for the figure's strike window
constexpr float kDefaultFallbackSt = 25.0f;    // a fallback blow's stamina until the engine's own is known for that attack kind
constexpr float kStaminaFloorShare = 0.15f;    // raw blows never take an NPC's stamina below this share of its maximum
constexpr float kStaminaCapShare = 0.35f;      // WO-173's one-blow cap stays as the outer bound
constexpr int kMedianWindow = 15;              // the running median's last N engine blows

// Hold the blow (instead of falling back now) while only the strike window is missing and the hold has not run out.
inline bool should_hold(bool holdOn, bool onlyWindowMissing, double heldForS) {
    return holdOn && onlyWindowMissing && heldForS >= 0 && heldForS < kHoldS;
}

// The median of v[0..n) (n <= kMedianWindow; the input is not changed). -1 when n is 0.
inline float median_of(const float* v, int n) {
    if (!v || n <= 0) return -1.0f;
    float s[kMedianWindow];
    const int m = n < kMedianWindow ? n : kMedianWindow;
    for (int i = 0; i < m; ++i) s[i] = v[i];
    std::sort(s, s + m);
    return (m & 1) ? s[m / 2] : 0.5f * (s[m / 2 - 1] + s[m / 2]);
}

// A fixed ring of the last kMedianWindow engine stamina costs.
struct Ring {
    float v[kMedianWindow]{};
    int n = 0, next = 0;
    void add(float x) {
        if (!std::isfinite(x) || x < 0) return;
        v[next] = x;
        next = (next + 1) % kMedianWindow;
        if (n < kMedianWindow) ++n;
    }
    float median() const { return median_of(v, n); }
};

// One fallback blow's stamina: the joiner's measure, at most the engine's median for that attack kind (kDefaultFallbackSt until known), at
// most WO-173's one-blow cap, and never below the floor: what is left above kStaminaFloorShare of the maximum (stNow < 0 = unknown: the
// floor cannot be judged, the other limits still hold).
inline float engine_like_cap(float joinerSt, float engineMedian, float stNow, float stMax) {
    if (!std::isfinite(joinerSt) || joinerSt <= 0) return 0.0f;
    float cap = joinerSt;
    const float lim = (std::isfinite(engineMedian) && engineMedian > 0) ? engineMedian : kDefaultFallbackSt;
    if (cap > lim) cap = lim;
    if (std::isfinite(stMax) && stMax > 0) {
        const float outer = kStaminaCapShare * stMax;
        if (cap > outer) cap = outer;
        if (std::isfinite(stNow) && stNow >= 0) {
            float room = stNow - kStaminaFloorShare * stMax;
            if (room < 0) room = 0;
            if (cap > room) cap = room;
        }
    }
    return cap < 0 ? 0.0f : cap;
}

} // namespace kcdmp::wo178rules
