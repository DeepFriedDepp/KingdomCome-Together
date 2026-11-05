// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-214 (docs/WO-214-findings.md): the pure rules of the native half (engine-free: native/tests/wo214_tests.cpp checks them).
//   B    one bind per body: a bind asked again for a body already bound to the same entity is a no-op; a silent stream holds its body
//        (its last pose) instead of dropping it, and resumes with a blend from where the body stands, never with a fresh anchor
//   C.1  a copy the owner lets go of (or whose host posture cannot be shown) is given a base in the same frame -- never an empty body
//   C.2  a combat action on a copy ends with its host NPC's fight, and never outlives a row's length
#include <cstdint>
#include <cstring>

namespace kcdmp::wo214rules {

// ---- the agent's switch word (op 148), every bit on until the agent says otherwise; the bench's twin: KCDMP_TEST_W214_OFF=<names|all>
constexpr uint32_t kBitSendTurn = 0x01;      // A.1/A.2 send_turn: the host's NPC sender counts a turn as a change
constexpr uint32_t kBitOneBind = 0x02;       // B one_bind
constexpr uint32_t kBitOwnerBase = 0x04;     // C.1 owner_base
constexpr uint32_t kBitCombatEnd = 0x08;     // C.2 combat_end
constexpr uint32_t kBitReactionRows = 0x10;  // D.1/D.2 reaction_rows: the host's hit and death rows play on a copy the owner owns
constexpr uint32_t kBitsDefault = kBitSendTurn | kBitOneBind | kBitOwnerBase | kBitCombatEnd | kBitReactionRows;

inline uint32_t twin_off_bits(const char* list) {
    struct N { const char* n; uint32_t b; };
    static const N k[] = {{"send_turn", kBitSendTurn}, {"one_bind", kBitOneBind}, {"owner_base", kBitOwnerBase}, {"combat_end", kBitCombatEnd},
                          {"reaction_rows", kBitReactionRows}};
    uint32_t b = 0;
    if (!list) return 0;
    const char* p = list;
    while (*p) {
        while (*p == ',' || *p == ' ') ++p;
        const char* s = p;
        while (*p && *p != ',' && *p != ' ') ++p;
        const size_t n = static_cast<size_t>(p - s);
        if (n == 3 && std::strncmp(s, "all", 3) == 0) b |= kBitsDefault;
        for (const N& e : k)
            if (n == std::strlen(e.n) && std::strncmp(s, e.n, n) == 0) b |= e.b;
    }
    return b;
}

// ---- A.1 the host's change test: a turn counts (a body turning in place went out only on its 2 s heartbeat) -----------------------
constexpr float kTurnEpsRad = 0.0175f;   // 1 degree
inline float wrap_pi(float a) {
    const float twopi = 6.2831853f, pi = 3.1415927f;
    while (a > pi) a -= twopi;
    while (a < -pi) a += twopi;
    return a;
}
inline bool turned(float yaw, float sentYaw, bool on) { return on && (wrap_pi(yaw - sentYaw) > kTurnEpsRad || wrap_pi(yaw - sentYaw) < -kTurnEpsRad); }

// ---- B one bind per body ------------------------------------------------------------------------------------------------------------
// The old writer dropped a body its stream had not spoken of for 4 s -- with the host's 2 s heartbeat, two lost heartbeats (the field:
// npc-state gaps of 1.9-4.4 s) dropped it, and the next sample bound it again at a fresh anchor. Now a silent stream's body holds where
// it stands; only a stream silent this long is forgotten (the host has not sent the body since: it left the host's range, or the world).
constexpr double kSilenceOldS = 4.0;
constexpr double kBoundForgetS = 30.0;
inline bool silence_drops(bool on, double silentS, bool keepMoving) {
    if (on) return silentS > kBoundForgetS;
    return !keepMoving && silentS > kSilenceOldS;
}
// A body whose stream comes back after a silence this long starts from where it stands (the blend), not from a jump to the stream --
// the old writer's drop time: a heartbeat-fed body (2 s) renders as before, a body the old writer would have dropped and bound afresh
// (with a blend from the body) resumes the same way, without the drop.
constexpr double kResumeBlendS = kSilenceOldS;
inline bool resume_blends(bool on, double silentS) { return on && silentS >= kResumeBlendS; }
// A bind asked for a body already bound to that same entity changes nothing (the field: 1,266 of 2,894 binds re-bound a bound body).
inline bool bind_noop(bool on, bool bound, uint32_t boundEid, const void* boundEnt, uint32_t reqEid, const void* reqEnt) {
    return on && bound && boundEid == reqEid && boundEnt == reqEnt && reqEid != 0;
}

// ---- C.1 a base in the same frame ----------------------------------------------------------------------------------------------------
// What the owner hands a copy it lets go of, or whose host posture its install refused: the host's base when that is known, else the
// copy's own neutral idle (learned from the host's standing idles), else a locomotion nudge (the old last resort).
enum class Base : uint8_t { HostBase = 0, NeutralIdle = 1, Nudge = 2 };
inline Base base_for(bool hostBaseKnown, bool neutralKnown) {
    return hostBaseKnown ? Base::HostBase : neutralKnown ? Base::NeutralIdle : Base::Nudge;
}

// ---- D.1/D.2 the host's reaction rows on an owned copy ----------------------------------------------------------------------------------
// WO-208 A.1's "one owner" refused every row on a copy the animation owner owns (the pipe said "engine-refused") -- the field's fought
// copy refused 23 of 25 hit rows, and its death row: the plain hit WO-187 played instead was refused too, and the body died standing.
// A reaction row (the host NPC's hit or death reaction: a CombatHit* or CombatDeath* fragment, or the death tag) plays on it; an attack row
// stays the owner's and WO-209's.
inline bool reaction_row(const char* spec) {
    if (!spec) return false;
    if (!std::strncmp(spec, "CombatHit", 9) || !std::strncmp(spec, "CombatDeath", 11)) return true;
    return std::strstr(spec, "+death+") != nullptr || std::strstr(spec, ",death+") != nullptr || std::strstr(spec, " death+") != nullptr;
}
inline bool death_row(const char* spec) {
    if (!spec) return false;
    return !std::strncmp(spec, "CombatDeath", 11) || std::strstr(spec, "+death") != nullptr || std::strstr(spec, " death+") != nullptr;
}
inline bool owner_refuses_row(bool owned, bool on, const char* spec) { return owned && !(on && reaction_row(spec)); }

// ---- C.2 an action that is no longer the host NPC's ends ---------------------------------------------------------------------------
// The field's probe: four copies' queued actions waited 203-249 s behind a "C_CombatAnimAction" -- by its fragment's name the owner's own
// install of a posture (ReadingBook, Lying, SittingIdle, Guard: the owner installs through the combat manager, so the probe saw that
// class) -- long after their host NPCs had stood up and walked away. The owner's withdraw steps a running action's priority down; a
// looping posture never yields to that, and nothing queued behind it starts. Now a full-body action on an owned copy that is not its host
// NPC's current one -- not its base, not a paired action, not the copy's own attack -- is stopped the game's own way (IAction::Stop) and
// the host's base handed over: once its host NPC has been in a base (standing, moving, combat footwork) for kHostLeftS while it held the
// body that long and an action waits behind it; a combat row (a Combat* fragment) as well once its host NPC is out of combat, and once it
// outlived any row (the bench's longest: 3.4 s) plus a second.
constexpr double kHostLeftS = 1.0;
constexpr double kOutOfFightS = 1.0;
constexpr double kCombatActionMaxS = 4.5;
enum class Stuck : uint8_t { No = 0, FightOver = 1, Outlived = 2, HostLeft = 3 };
inline Stuck stale_action(bool on, bool candidate, bool combatRow, double heldS, double hostBaseS, double hostOutOfFightS, bool queuedBehind) {
    if (!on || !candidate) return Stuck::No;
    if (hostBaseS >= kHostLeftS && heldS >= kHostLeftS && queuedBehind) return Stuck::HostLeft;
    if (combatRow && hostOutOfFightS >= kOutOfFightS && heldS >= kOutOfFightS) return Stuck::FightOver;
    if (combatRow && queuedBehind && heldS > kCombatActionMaxS) return Stuck::Outlived;
    return Stuck::No;
}
inline bool combat_fragment_name(const char* n) { return n && std::strncmp(n, "Combat", 6) == 0; }
// Only the combat manager's own action (C_CombatAnimAction) is ever stopped here: the owner's posture installs and the combat rows run
// through it -- the field's stuck actions were all of it. The engine's own movement (the walk's start and stop, CAnimActionMovementTransition),
// its idle state and its variations never are (d3's village: stopping a walk transition of a copy whose host NPC stood made the copy's own
// movement system queue it again every second -- one body stopped 35 times in two minutes, "Agent is stuck", the probe's sliding).
inline bool stoppable_class(bool combatAnimClass) { return combatAnimClass; }
// A rest after churn: an action stopped kChurnStops times on one copy within kChurnWindowS is coming back from somewhere (the owner, the
// engine) -- the rule leaves that copy alone for kChurnRestS (said once) rather than fight it.
constexpr int kChurnStops = 3;
constexpr double kChurnWindowS = 10.0;
constexpr double kChurnRestS = 30.0;
inline bool churn(double thirdLastStopAt, double now) { return now - thirdLastStopAt <= kChurnWindowS; }

} // namespace kcdmp::wo214rules
