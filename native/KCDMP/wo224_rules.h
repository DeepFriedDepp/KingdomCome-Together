// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-224's pure rules (no engine, no Windows): the kinds of pair the clinch instrument counts, the engine's clinch test re-read in its
// own order (which condition a failed test failed on), what "in a clinch" is. native/tests checks them.
#include <cstdint>
#include <cstring>

namespace kcdmp::wo224::rules {

// ---- the kinds of pair ----
constexpr int kClasses = 4;
constexpr int kClassMeCopy = 0;    // the local player and a copy in the mod's table (the joiner's fights)
constexpr int kClassMeOther = 1;   // the local player and any other body (the host's own fights: the reference)
constexpr int kClassFigure = 2;    // a partner's figure and another body, the local player not in it (the host: his NPC and the joiner's figure)
constexpr int kClassOthers = 3;    // two bodies of this world's own
inline int pair_class(bool localPlayerIn, bool otherIsListedCopy, bool figureIn) {
    if (localPlayerIn) return otherIsListedCopy ? kClassMeCopy : kClassMeOther;
    return figureIn ? kClassFigure : kClassOthers;
}
inline const char* class_name(int c) {
    static const char* k[kClasses] = {"player-and-copy", "player-and-other", "figure-and-npc", "npc-and-npc"};
    return (c >= 0 && c < kClasses) ? k[c] : "?";
}
inline const char* asker_name(int c, int side) {
    if (c == kClassFigure) return side == 0 ? "the figure" : "the npc";
    if (c == kClassOthers) return side == 0 ? "an npc" : "-";
    return side == 0 ? "the player" : "the other";
}

// ---- the engine's clinch test, re-read (C_CombatActorClinch::CanClinch's own order) ----
struct Side {
    bool read = false, combatMode = false, hasOpponent = false, opponentIsOther = false;
    int32_t guardType = -1;   // -1 = not read
    char action[48]{};        // the combat director's scope-0 action ("Guard", "Attack", "none", "?")
};
enum : int { kPass = 0, kNoOpponent, kUnread, kNotMutual, kSelfCombat, kSelfGuardType, kSelfAction, kOppCombat, kOppGuardType, kOppAction, kAngle, kReasons };
inline const char* reason_name(int r) {
    static const char* k[kReasons] = {"would-pass", "no-opponent", "unread", "not-mutual-opponents", "player-not-in-combat-mode", "player-guard-type",
                                      "player-action", "other-not-in-combat-mode", "other-guard-type", "other-action", "not-facing"};
    return (r >= 0 && r < kReasons) ? k[r] : "?";
}
// the actions a body may be clinched in: a Guard, a Dodge, a Hit (unless its context forbids it: not read here), a SyncHit (in its end state)
inline bool clinchable(const char* a) {
    return a && (std::strcmp(a, "Guard") == 0 || std::strcmp(a, "Dodge") == 0 || std::strcmp(a, "Hit") == 0 || std::strcmp(a, "SyncHit") == 0);
}
constexpr float kFacingDotMax = -0.7660f;   // cos(pi - wh_cs_SyncGuardMaxXYAngle 0.698): the two forwards within 40 degrees of opposite
// self = the body that asks (the local player), other = its opponent; facingDot = the two bodies' forward vectors' dot product.
inline int why_not(const Side& self, const Side& other, float facingDot) {
    if (!self.read || !other.read) return kUnread;
    if (!self.hasOpponent) return kNoOpponent;
    if (!other.opponentIsOther) return kNotMutual;
    if (!self.combatMode) return kSelfCombat;
    if (self.guardType > 0) return kSelfGuardType;
    if (!clinchable(self.action)) return kSelfAction;
    if (!other.combatMode) return kOppCombat;
    if (other.guardType > 0) return kOppGuardType;
    if (!clinchable(other.action)) return kOppAction;
    if (facingDot > kFacingDotMax) return kAngle;
    return kPass;
}

// ---- a body's name as 32 bits (FNV-1a over its bytes; never 0): the agent's Wo224Rules.NameHash ----
inline uint32_t name_hash(const char* s) {
    if (!s || !*s) return 0;
    uint32_t h = 2166136261u;
    for (const unsigned char* p = reinterpret_cast<const unsigned char*>(s); *p; ++p) { h ^= *p; h *= 16777619u; }
    return h == 0 ? 1 : h;
}

// ---- in a clinch: the engine's paired guard movement, a clinch move, or a sync guard type (clinch 4, clinch1 24, clinch2 25) ----
inline bool sync_guard_type(int32_t g) { return g == 4 || g == 24 || g == 25; }
// held in the sync guard (what an end has to undo): the paired guard movement runs, or the guard type is still a sync type. A body
// in a move with its guard type back at main is on the engine's own way out.
inline bool held_in_sync_guard(const char* scope1, int32_t guardType) {
    return (scope1 && std::strcmp(scope1, "GuardSyncMovement") == 0) || sync_guard_type(guardType);
}
inline bool in_clinch(const char* scope0, const char* scope1, int32_t guardType) {
    return (scope1 && std::strcmp(scope1, "GuardSyncMovement") == 0) || (scope0 && std::strcmp(scope0, "SyncTransition") == 0) || sync_guard_type(guardType);
}

} // namespace kcdmp::wo224::rules
