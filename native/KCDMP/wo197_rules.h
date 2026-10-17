// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-197: the engine-free rules of the native half (wo197.cpp). native/tests/wo197_tests.cpp pins them.
//   B1  the local player's recoil: a new FailedAttack in his combat director's scope 4 (WO-196 R1) goes out once; its kind comes from his
//       opponent's perfect-block state, else the opponent's State 0x100 (a held block in place), else unknown
//   B3  the local player's clinch: scope 1 GuardSyncMovement = the clinch idle, scope 0 SyncTransition = a clinch move (WO-196 R2.2)
//   B5  a copy's main action that is a real reaction (never ended by A6's takeover)

#include <cstdint>
#include <cstring>

namespace kcdmp::wo197rules {

// ---- wire: ActionKind (Protocol.cs, append-only) and the frame flags -------------------------------------------------------------
constexpr uint8_t kKindAvatarRecoil = 19;   // ActionKind.AvatarRecoil
constexpr uint8_t kKindAvatarClinch = 20;   // ActionKind.AvatarClinch
// recoil flags (RowEvent.Flags)
constexpr uint8_t kRecoilPerfect = 0x01;    // the opponent perfect-blocked (CombatAttackFailedPB); else a plain block (CombatAttackFailed)
constexpr uint8_t kRecoilKnown = 0x02;      // the kind was read (else: the opponent's state had moved on; the receiver takes a block)
constexpr uint8_t kRecoilRow = 0x04;        // the row GUID is the engine's own failed-attack row (else empty: the receiver picks by weapon)
// clinch flags: the phase in the low 2 bits, the role bit, the rows bit
constexpr uint8_t kClinchEnter = 1, kClinchMove = 2, kClinchLeave = 3, kClinchPhaseMask = 0x03;
constexpr uint8_t kClinchMaster = 0x04;     // this player is the master (the attacker whose blow made the clinch)
constexpr uint8_t kClinchRows = 0x08;       // a move: the master row (guid) and the slave row (alt) are the engine's own

// ---- B1 ----------------------------------------------------------------------------------------------------------------------------
constexpr int32_t kStateParryInPlace = 0x100, kStateFailedAttack = 0x10;
inline uint8_t recoil_flags(bool oppRead, bool oppPerfect, int32_t oppState) {
    if (!oppRead) return 0;
    if (oppPerfect) return kRecoilPerfect | kRecoilKnown;
    if (oppState == kStateParryInPlace) return kRecoilKnown;
    return 0;
}
inline bool is_failed_attack(const char* n) { return n && std::strcmp(n, "FailedAttack") == 0; }
// A FailedAttack that was not there on the last read (a new action object) is one recoil.
inline bool new_recoil(const void* prevAct, const void* curAct, const char* curName) {
    return curAct && curAct != prevAct && is_failed_attack(curName);
}
// The UNKNOWN of WO-196 R1: State 0x10 (the failed-attack state) without a FailedAttack in scope 4 within this many reads = no recoil row
// (blunt, unarmed) or the director refused it (an equal-priority pose modifier running in scope 4).
constexpr int kState10Reads = 6;

// ---- B3 ----------------------------------------------------------------------------------------------------------------------------
enum class Clinch : uint8_t { None = 0, Idle = 1, Move = 2 };
inline bool is_clinch_idle(const char* scope1) { return scope1 && std::strcmp(scope1, "GuardSyncMovement") == 0; }
inline bool is_clinch_move(const char* scope0) { return scope0 && std::strcmp(scope0, "SyncTransition") == 0; }
inline Clinch clinch_of(const char* scope0, const char* scope1) {
    if (is_clinch_move(scope0)) return Clinch::Move;
    if (is_clinch_idle(scope1)) return Clinch::Idle;
    return Clinch::None;
}
// The event a change of clinch state is: 0 none, else kClinchEnter / kClinchMove / kClinchLeave. A move is a NEW SyncTransition action.
inline uint8_t clinch_event(Clinch was, Clinch now, bool newMoveAction) {
    if (now == Clinch::Move && newMoveAction) return kClinchMove;
    if (was == Clinch::None && now == Clinch::Idle) return kClinchEnter;
    if (was != Clinch::None && now == Clinch::None) return kClinchLeave;
    return 0;
}

// ---- B5 ----------------------------------------------------------------------------------------------------------------------------
// A copy's scope-0 action that is a real engine reaction or a paired move under way: never ended by A6 (WO-196 R4).
inline bool keep_main_action(const char* n) {
    if (!n || !*n) return false;
    return std::strcmp(n, "Hit") == 0 || std::strcmp(n, "SyncHit") == 0 || std::strcmp(n, "SyncPerfectBlockHit") == 0 ||
           std::strcmp(n, "SyncTransition") == 0 || std::strcmp(n, "SyncAttack") == 0 || std::strstr(n, "Death") != nullptr ||
           std::strstr(n, "Knock") != nullptr || std::strstr(n, "Stagger") != nullptr;
}
// Whether EnsureGuardAction has anything to do: scope 0 is not already a plain Guard.
inline bool needs_guard(const char* n) { return !n || std::strcmp(n, "Guard") != 0; }

// ---- B2 / B3 the pair op ---------------------------------------------------------------------------------------------------------------
constexpr size_t kMaxSpec = 191;   // the swing spec cap (combat_swing.h, the GhostSwing op)
// [op][attackerEid:4][victimEid:4 (0 = the attacker's opponent)][flags:1][lenA:1][specA][lenV:1][specV]; no rows at all = the query (the
// victim's entity id only, nothing set, nothing played)
inline bool pair_request_ok(size_t len, uint8_t lenA, uint8_t lenV) {
    if (lenA > kMaxSpec || lenV > kMaxSpec || len != 1u + 4 + 4 + 1 + 1 + lenA + 1 + lenV) return false;
    return lenV > 0 || lenA == 0;
}
inline bool pair_is_query(uint8_t lenA, uint8_t lenV) { return lenA == 0 && lenV == 0; }
constexpr uint8_t kPairVictimFirst = 0x01;   // the victim's row before the attacker's (the engine's own order: SyncAttack::EnterImpl)

} // namespace kcdmp::wo197rules
