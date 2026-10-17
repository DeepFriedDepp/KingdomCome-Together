// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-197 -- the animation-sync release: combat, the rest (WO-190 Part B with WO-196's answers). The pure rules are wo197_rules.h.
//
//   B0  the combat director reader (wo187.cpp director_scope): the running actions by scope, read from C_CombatActorDirector's vector.
//   B1  mp_anim_recoil: the local player's recoil -- each new FailedAttack in his director's scope 4 goes out as ActionKind.AvatarRecoil (its
//       kind from his opponent's perfect-block state / State 0x100; its row when the row object names one). State 0x10 with no FailedAttack
//       is counted and logged (WO-196's open question: an equal-priority pose modifier in scope 4 refusing it).
//         WO197-RECOIL kind=pb|block|unknown row=<guid|-> scope4_before=<name> / WO197-RECOIL state 0x10 without a FailedAttack ...
//   B2  mp_anim_paired (op 67): the victim's half of a paired move -- both opponents set to each other (SetLogicalTarget, WO-187's call),
//       the victim's row queued first, then the attacker's, in one main-thread step (the engine's own order: SyncAttack::EnterImpl).
//   B3  mp_anim_clinch: the local player's clinch (scope 1 GuardSyncMovement = the clinch idle, scope 0 SyncTransition = a move, the clinch
//       object's master flag) goes out as ActionKind.AvatarClinch; on the watcher both rows play through op 67, or StartSyncGuard on the pair
//       (op 68: the engine's own clinch guard, both paired idles).
//   B5  mp_copy_guard_engine: A6's takeover ends a copy's leftover with the engine's EnsureGuardAction (terminates scope 0 and installs a new
//       Guard in the same call: CombatModule's callee of StartSyncGuard that imports C_ActionDirector::TerminateInternal), never a bare
//       TerminateAction(0) -- an empty main scope ends combat mode.
//
// Pipe ops in the WO-163 family (main thread):
//   65 Status -> text
//   66 Config [recoil][paired][clinch][guardEngine][categories mask] (255 = unchanged) -> 5 bytes
//   67 Pair [attackerEid:4][victimEid:4 (0 = the attacker's opponent)][flags:1][lenA:1][specA][lenV:1][specV]
//        -> [victimEid:4][victimRow:1 SwingResult][attackerRow:1 SwingResult (255 = none sent)][targets:1 bits 1 attacker->victim, 2 victim->attacker]
//   68 SyncGuard [guardType:4][masterEid:4][slaveEid:4] -> [result:1 0 refused, 1 started, 2 not armed][masterFlag:1][slaveFlag:1]
//   69 EnsureGuard [eid:4] -> [result:1 0 refused, 1 a Guard runs, 2 not armed][before:name]
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo197 {

constexpr uint8_t kOpStatus = 65, kOpConfig = 66, kOpPair = 67, kOpSyncGuard = 68, kOpEnsureGuard = 69;

// Part C (main thread): which everyday categories' fixes are on -- config op 66, byte 5, one bit per category (0 doors, 1 seats, 2 items,
// 3 work, 4 riding, 5 routines, 6 emotes); the agent's mp_anim_seats / mp_anim_work / mp_anim_routines drive bits 1, 3 and 5 (the others
// have no fix and stay set). activity_now: a changed activity row of this kind is applied on the next frame, not the next 250 ms tick.
constexpr uint8_t kCatDoors = 1, kCatSeats = 2, kCatItems = 4, kCatWork = 8, kCatRiding = 16, kCatRoutines = 32, kCatEmotes = 64;
bool cat_on(uint8_t bit);
bool activity_now(bool minigame, bool stance, bool unstance);
bool recoil_on();
bool paired_on();
bool clinch_on();
bool guard_engine_on();

// B5 (main thread): the engine's EnsureGuardAction on this combat actor. 1 = a Guard runs in scope 0 now (installed, or already there),
// 0 = the engine refused, -1 = not armed (the function was not found on this build).
int ensure_guard(void* ca);

void tick();   // main thread, every frame (B1 / B3 polls)
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);

} // namespace kcdmp::wo197
