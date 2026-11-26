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

// ---- B: a host NPC's chain input on its copy ----
// The engine chains an attack when the combo input comes while the running swing's "ComboSlot" is open; the follow-up then starts at
// the swing's own chain point. The host tells the input as its NPC makes it; the copy makes the same input when ITS slot opens.
constexpr double kChainSlotWaitS = 1.2;    // the copy's own slot must open this long after the host's word came (a swing's length)
constexpr double kChainFollowS = 1.2;      // the engine's follow-up must start this long after the copy's input (seen: 911 ms at most)
constexpr double kChainFollowIdleS = 0.45; // ... after an input made with no swing running (the slot read open in a guard)
// the slot's force (b6): see chain_force_due
constexpr double kChainForceMinS = 0.03;      // never before this long after the follow-up's row
constexpr double kChainForceUnknownS = 0.2;   // the ages are not known: this long after the row
constexpr double kChainForceMaxS = 0.5;       // and never later than this after the row
constexpr double kChainHostAgeMinS = 0.2, kChainHostAgeMaxS = 2.0;   // a host swing's age at its follow-up, as a plausible one
constexpr double kChainNoSwingS = 0.15;    // the follow-up's row is here and the copy runs no swing to chain from: a fresh attack after this
constexpr double kChainRowAfterS = 1.0;    // a row that comes this long after a chain's follow-up started names that follow-up
inline bool attack_action(const char* n) {
    return n && (std::strcmp(n, "Attack") == 0 || std::strcmp(n, "StaticAttack") == 0 || std::strcmp(n, "SyncAttack") == 0);
}
// ---- B: the guard an attack row starts from (its leftGuard / rightGuard tag): 1 left, 0 right, -1 the row does not say ----
// The combat model's own values BEFORE such a row starts (read on the host at 109 chain inputs, each just before its follow-up):
// GuardZone 2 and GuardStance 0 before a leftGuard row, GuardZone 1 and GuardStance 1 before a rightGuard row. During the row both
// read the other side (the swing crosses over), which is why a row's attack zone is not its starting guard.
inline int row_guard_side(const char* spec) {
    if (!spec) return -1;
    if (std::strstr(spec, "leftGuard")) return 1;
    if (std::strstr(spec, "rightGuard")) return 0;
    return -1;
}
inline int guard_zone_of_side(int side) { return side == 1 ? 2 : side == 0 ? 1 : -1; }
inline int guard_stance_of_side(int side) { return side == 1 ? 0 : side == 0 ? 1 : -1; }

// the host: is this combo input one to tell -- the body's slot was open (the engine took it), an NPC's, its opponent a partner's figure
inline bool chain_tell(bool slotOpen, bool isLocalPlayer, bool isFigure, bool opponentIsFigure) { return slotOpen && !isLocalPlayer && !isFigure && opponentIsFigure; }
// the copy, before its input: what this frame does
enum { kChainWait = 0, kChainApply = 1, kChainFresh = 2, kChainLapse = 3, kChainForce = 4 };
inline int chain_before(bool slotOpen, bool driven, bool swingRuns, bool rowSeen, double waitedS, bool forceDue = false) {
    if (waitedS > kChainSlotWaitS) return rowSeen ? kChainFresh : kChainLapse;
    if (!driven) return kChainWait;
    if (slotOpen) return kChainApply;
    if (!swingRuns && rowSeen && waitedS > kChainNoSwingS) return kChainFresh;
    if (swingRuns && rowSeen && forceDue) return kChainForce;   // the host NPC chained; the copy's blow made no contact here
    return kChainWait;
}
// The engine opens a swing's chain slot on CONTACT (its combo trigger listens for the contact event and keeps the slot open for a
// while): a blow the local player dodged makes none, so the copy of an NPC that chained on the host cannot chain, waits out its
// swing's recovery and falls a swing behind. The mod opens the slot itself when the copy's swing is as old as the host NPC's was
// when its follow-up started (this row's arrival less the row before it: one link both) -- had there been contact here, the engine
// would have opened it by now. Ages not known: a fixed moment after the row.
inline bool chain_force_due(double sinceRowS, double copyAgeS, double hostAgeS) {
    if (sinceRowS < kChainForceMinS) return false;
    if (copyAgeS < 0 || hostAgeS < kChainHostAgeMinS || hostAgeS > kChainHostAgeMaxS) return sinceRowS >= kChainForceUnknownS;
    return copyAgeS >= hostAgeS || sinceRowS >= kChainForceMaxS;
}
// the copy, after its input: the engine's follow-up is the next action in the main scope
enum { kAfterWait = 0, kAfterFollowed = 1, kAfterNotFollowed = 2, kAfterLapse = 3 };
inline int chain_after(bool actionChanged, const char* nowAction, double sinceInputS, bool inSwing = true) {
    if (actionChanged) return attack_action(nowAction) ? kAfterFollowed : kAfterNotFollowed;
    return sinceInputS > (inSwing ? kChainFollowS : kChainFollowIdleS) ? kAfterLapse : kAfterWait;
}

} // namespace kcdmp::wo224::rules
