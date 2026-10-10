// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-165: the engine-free rules of the replay (wo165.cpp) -- unit-tested in native/tests/wo165_tests.cpp.
//
// The design source is research/WO-162/combat-RE.md Q1.7 (the replay contract); the call site below was read in this WO
// (docs/WO-165-findings.md "The instrument"): in the collision handler, the engine builds its result struct with a call that takes
// only its address, loads the attacker's hit processor from the combat actor and calls RPGProcessHit:
//
//     lea  rcx,[rbp-10h]          48 8D 4D F0
//     call <result ctor>          E8 rel32
//     ...                         (the hit-in, flags and result pointers into r8, r9, rdx)
//     mov  rcx,[rcx+disp32]       48 8B 89 disp32      -- disp32 = where the combat actor holds its C_CombatRPG
//     call RPGProcessHit          E8 rel32
//
// parse_call_site() lifts the three facts out of the live bytes, so no address and no offset of this build is written in code:
// the ctor and RPGProcessHit are where the code calls, and the processor's offset is the one the code reads.
#pragma once
#include <cstddef>
#include <cstdint>
#include <cmath>
#include <cstring>

namespace kcdmp::wo165 {

struct CallSite {
    uintptr_t ctor = 0;      // the result struct's constructor
    uint32_t rpgOffset = 0;  // combat actor -> its C_CombatRPG pointer
    uintptr_t process = 0;   // RPGProcessHit (must equal the function the "RPGProcessHit" string anchors)
};

inline int32_t rel32_at(const uint8_t* p) { int32_t v; std::memcpy(&v, p, 4); return v; }

// `code` is a window of the collision handler's bytes starting at `codeAddr`. True when the sequence is found once with the
// processor load and its call within kMaxGap bytes after the ctor call.
inline bool parse_call_site(const uint8_t* code, size_t n, uintptr_t codeAddr, CallSite* out) {
    constexpr size_t kMaxGap = 0x40;
    static const uint8_t kLea[] = {0x48, 0x8D, 0x4D, 0xF0, 0xE8};
    static const uint8_t kLoad[] = {0x48, 0x8B, 0x89};
    int found = 0;
    CallSite cs{};
    for (size_t i = 0; i + sizeof kLea + 4 <= n; ++i) {
        if (std::memcmp(code + i, kLea, sizeof kLea) != 0) continue;
        const size_t callEnd = i + sizeof kLea + 4;
        const uintptr_t ctor = codeAddr + callEnd + static_cast<intptr_t>(rel32_at(code + i + sizeof kLea));
        for (size_t j = callEnd; j + 3 + 4 + 5 <= n && j < callEnd + kMaxGap; ++j) {
            if (std::memcmp(code + j, kLoad, sizeof kLoad) != 0) continue;
            uint32_t disp; std::memcpy(&disp, code + j + 3, 4);
            if (code[j + 7] != 0xE8) continue;
            const uintptr_t target = codeAddr + j + 12 + static_cast<intptr_t>(rel32_at(code + j + 8));
            cs = CallSite{ctor, disp, target};
            ++found;
            break;
        }
    }
    if (found != 1) return false;
    *out = cs;
    return true;
}

// The engine's prologues this WO read (1.5.5). A build whose bytes differ is refused at resolve time (the replay stays off and says
// why): the build-identity test for a function we CALL (we patch nothing here).
// RPGProcessHit: mov [rsp+20h],rbp; push rdi; push r12; push r13; push r14; push r15; sub rsp,80h
inline constexpr uint8_t kProcessPrologue[21] = {0x48, 0x89, 0x6C, 0x24, 0x20, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41,
                                                 0x56, 0x41, 0x57, 0x48, 0x81, 0xEC, 0x80, 0x00, 0x00, 0x00};
// the result ctor: push rbx; sub rsp,30h; mov rbx,rcx
inline constexpr uint8_t kCtorPrologue[9] = {0x40, 0x53, 0x48, 0x83, 0xEC, 0x30, 0x48, 0x8B, 0xD9};

// Sizes of the buffers we hand the engine (each larger than the most the code was read to touch).
constexpr size_t kResultBytes = 0x100;    // the ctor writes up to +0x9B; the caller's frame gives it 0xA0
constexpr size_t kHitInBytes = 0x40;      // read up to +0x30 (the vector's capacity word)
constexpr size_t kDetailsBytes = 0x100;   // read up to +0xC8 (the victim actor); our victim actor is in the hit-in instead
constexpr size_t kFlagsBytes = 0x10;      // written: +0 blocking, +1 blocked by zone, +2 damaged, +3 perfect block, +0xC weapon damaged
constexpr size_t kSubHitBytes = 0x10;     // one entry: factor A, factor B, factor C (floats), condition (u32)

// hit-in layout (the collision handler's own): +0 skip the repeat filter, +1/+2 contact flags, +3 "victim blocking" FORCE (must be 0:
// the engine decides), +4 hit index, +8 = 2, +0xC = 2 (hand slot 2 = the attacker's AttackHandSlot), +0x10 victim combat actor,
// +0x18 collision details, +0x20/+0x28/+0x30 the sub-hit vector (begin, end, capacity).
// collision details read by the hit core: +0..+8 hit position, +0x24 the VICTIM's entity id (the repeat filter's key and the
// fallback victim lookup), +0x64 victim subpart (-1 none), +0x68 material (-1 none), +0x6C attacker subpart (-1 none); +0xC..+0x20
// and +0x80 are copied into the result only.
struct HitInView { uint8_t skipFilter, c1, c2, forceBlock; uint32_t hitIndex, k8, kC; };

inline void build_hit_in(uint8_t* hitIn, bool skipFilter, void* victimCa, void* details, void* subBegin, void* subEnd) {
    std::memset(hitIn, 0, kHitInBytes);
    hitIn[0] = skipFilter ? 1 : 0;
    const uint32_t two = 2;
    std::memcpy(hitIn + 0x08, &two, 4);
    std::memcpy(hitIn + 0x0C, &two, 4);
    std::memcpy(hitIn + 0x10, &victimCa, 8);
    std::memcpy(hitIn + 0x18, &details, 8);
    std::memcpy(hitIn + 0x20, &subBegin, 8);
    std::memcpy(hitIn + 0x28, &subEnd, 8);
    std::memcpy(hitIn + 0x30, &subEnd, 8);
}

inline void build_details(uint8_t* d, const float pos[3], uint32_t victimEid) {
    std::memset(d, 0, kDetailsBytes);
    std::memcpy(d, pos, 12);
    std::memcpy(d + 0x24, &victimEid, 4);
    const int32_t none = -1;
    std::memcpy(d + 0x64, &none, 4);
    std::memcpy(d + 0x68, &none, 4);
    std::memcpy(d + 0x6C, &none, 4);
}

inline void build_sub_hit(uint8_t* e) {
    const float one = 1.0f;
    const uint32_t cond = 0;
    std::memcpy(e + 0, &one, 4);
    std::memcpy(e + 4, &one, 4);
    std::memcpy(e + 8, &one, 4);
    std::memcpy(e + 12, &cond, 4);
}

// The engine's outcome, from what it wrote (flags) and the record it built (captured in the slot hook).
enum class Outcome : uint8_t { None = 0, Hit = 1, Blocked = 2, PerfectBlock = 3, Broken = 4, Filtered = 5 };
inline const char* outcome_name(Outcome o) {
    switch (o) {
        case Outcome::Hit: return "hit";
        case Outcome::Blocked: return "blocked";
        case Outcome::PerfectBlock: return "pb";
        case Outcome::Broken: return "broken";
        case Outcome::Filtered: return "filtered";
        default: return "none";
    }
}
// returned = RPGProcessHit's bool; seen = the slot hook saw the core build the record (false: the processor returned before the core --
// the "no attack" type, or a filtered repeat); rec = the record (+0x54 blocking, +0x56 perfect block, +0x57 broken), valid when seen.
inline Outcome classify(bool returned, bool seen, const uint8_t flags[kFlagsBytes], const uint8_t* rec) {
    if (!returned) return Outcome::Filtered;
    if (!seen) return Outcome::None;
    // the processor's own outputs decide; the record adds only "broken", which no output carries (the record's +0x54 / +0x56 are
    // inputs the core READ -- the victim's state -- and are logged beside the outcome, never used as it)
    if (flags[3]) return Outcome::PerfectBlock;
    if (rec && rec[0x57]) return Outcome::Broken;
    if (flags[0]) return Outcome::Blocked;
    return Outcome::Hit;
}

// ---------------------------------------------------------------------------------------------------- C1: the host's lock-on
// research/WO-162 Q2.2/Q2.3: a combat target is lockable iff the player and the candidate share a skirmish and the pair is hostile
// (an explicit pair stored by AddSoulToSkirmish with override 1, or a negative faction value). An NPC beating the partner's avatar is
// in a skirmish with the avatar only, so the host cannot lock onto it. The host's explicit pair is set while he stands near and faces
// such an NPC, and removed when that ends. The NPC's own target is never written (P6, live: the guard stayed on the avatar while the
// host only locked; the engine's selector logged "Player: Opponent change from '<none>' to '<the guard>'").
constexpr float kLockSetM = 6.0f;          // set the pair within this distance ...
constexpr float kLockFacingCos = 0.5f;     // ... while the host faces the NPC (within 60 degrees of his forward)
constexpr float kLockDropM = 10.0f;        // removed once the host is this far
constexpr double kLockFarS = 20.0;         // the WO-164 D2 watchdog's rule: 20 s further than kLockFarM from every player
constexpr float kLockFarM = 30.0f;

struct LockView {
    bool paired = false;          // the pair this module set is in place
    bool alive = true;
    bool combat = false;          // the NPC fights: its combat mode, or an opponent held (live: a guard beating the figure read CombatMode 0
                                  // with its Opponent on the avatar -- the opponent link is the signal WO-136 also ends a fight on)
    bool oppIsAvatar = false;     // its opponent is a partner's avatar
    bool oppIsHost = false;       // its opponent is the host himself
    float dist = 1e9f;            // host -> NPC (horizontal, m)
    float facingCos = -1.0f;      // the host's forward . the direction to the NPC
    double farForS = 0.0;         // how long the NPC has been further than kLockFarM from every player
};
enum class LockAct : uint8_t { None, Set, Keep, Remove, Forget };
struct LockDecision { LockAct act; const char* why; };

// Why a candidate near the host was not paired (the field line's reason); "" when it is.
inline const char* lock_not_set_why(const LockView& v) {
    if (!v.alive) return "npc-dead";
    if (!v.combat) return "npc-not-fighting";
    if (!v.oppIsAvatar) return "not-fighting-a-partner";
    if (v.dist > kLockSetM) return "farther-than-6m";
    if (v.facingCos < kLockFacingCos) return "not-facing";
    return "";
}

inline LockDecision lock_rule(const LockView& v) {
    if (!v.paired) {
        if (v.alive && v.combat && v.oppIsAvatar && v.dist <= kLockSetM && v.facingCos >= kLockFacingCos) return {LockAct::Set, "near-and-facing"};
        return {LockAct::None, ""};
    }
    if (!v.alive) return {LockAct::Remove, "npc-dead"};
    // the NPC turned on the host: that is the host's own fight now -- the pair is forgotten, never removed (removing would end it)
    if (v.oppIsHost) return {LockAct::Forget, "npc-fights-the-host"};
    if (!v.combat || !v.oppIsAvatar) return {LockAct::Remove, "fight-ended"};
    if (v.dist > kLockDropM) return {LockAct::Remove, "host-left-10m"};
    if (v.farForS >= kLockFarS) return {LockAct::Remove, "watchdog-20s-over-30m"};
    return {LockAct::Keep, ""};
}

// The host's forward on the ground plane from his yaw (CryEngine: an entity faces +y rotated by yaw about z).
inline float facing_cos(float yaw, float hx, float hy, float nx, float ny) {
    const float dx = nx - hx, dy = ny - hy;
    const float d = std::sqrt(dx * dx + dy * dy);
    if (d < 1e-3f) return 1.0f;
    const float fx = -std::sin(yaw), fy = std::cos(yaw);
    return (fx * dx + fy * dy) / d;
}

} // namespace kcdmp::wo165
