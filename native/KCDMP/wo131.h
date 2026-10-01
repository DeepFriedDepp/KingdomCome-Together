// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-131: combat and bodies -- the native half (docs/WO-131-findings.md).
//
// One pipe request, 0x21 [op][...], answered with 0x98 [ok][seq][op][reason][payload].
// Every op runs on the game's main thread (the pipe marshals it).
//
//   op 1 HitCheck   [guid:16][nameLen][name]
//                   -> [bound:1][ageMs:2][distM:4f][flags:1][hp:4f][guarded:1]
//                   The joiner's gate before a hit on an NPC copy is forwarded
//                   (1b): is the body driven by the host's stream, how old is
//                   the newest sample, how far is the body from it, is it dead
//                   there, the copy's own health now, does it carry the guard.
//   op 2 CopyGuard  [mode:1][eid:4] -> [present:1]
//                   The joiner's copy of a host NPC can never die or be knocked
//                   out locally (1c): kcdmp_avatar_guard (imm=1, upr=1) on it,
//                   removed before the host's death is applied.
//                   WO-135 modes: 0 off (both guards off), 1 guard (the full
//                   guard, the knockout guard off), 2 knockout -- the host's
//                   NPC is knocked out, so the copy is too: the full guard is
//                   swapped for kcdmp_knockout_guard (imm=1 only: still never
//                   dies here) and the game's own unconsciousness buffs are
//                   added (unconscious_nonpersistend + the endless
//                   infinite_unconsciousness_nonpersistent: it wakes when the
//                   host's does, not on the game's timer). 3 wake -- those
//                   removed, the game's remove_unconsciousness added, the full
//                   guard back. present = the copy is unconscious (2) or
//                   carries the guard it should (0, 1, 3).
//   op 3 FollowHp   [guid:16][eid:4][hp:4f] -> [before:4f][after:4f]
//                   The copy's health follows the host's (1c). A drop we write
//                   is credited to the LocalHit sampler first, so it never
//                   goes back out as a hit.
//   op 4 StopFight  [eid:4] -> []      (eid 0 = the local player)
//                   wh::rpgmodule::StopFight on that soul (1g).
//   op 5 RestoreHp  [eid:4][hp:4f] -> [before:4f][after:4f]
//                   The host puts an avatar's health back after an NPC's hit
//                   was measured and forwarded (1e): the avatar carries the
//                   imm guard, so without this it sat at 1 hp and every later
//                   hit measured nothing.
//   op 6 Faction    [guid:16][mode:1] -> []   mode 2 = the player's faction (1d)
//   op 7 Status     [] -> [text]
//   op 8 PlayerCrouch [on:1] -> []   WO-135 live checks only: the local
//                   player's crouch through its own setter (the function the
//                   crouch key reaches); no input is made.

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo131 {

constexpr uint8_t kOpHitCheck  = 1;
constexpr uint8_t kOpCopyGuard = 2;
constexpr uint8_t kOpFollowHp  = 3;
constexpr uint8_t kOpStopFight = 4;
constexpr uint8_t kOpRestoreHp = 5;
constexpr uint8_t kOpFaction   = 6;
constexpr uint8_t kOpStatus    = 7;
constexpr uint8_t kOpPlayerCrouch = 8;   // WO-135

// CopyGuard modes (WO-135).
constexpr uint8_t kGuardOff = 0, kGuardOn = 1, kGuardKnockout = 2, kGuardWake = 3;

// Reasons (reply byte 3). Mirrored by number in GameBridge.Wo131.cs.
constexpr uint8_t kROk         = 0;
constexpr uint8_t kRBadRequest = 1;
constexpr uint8_t kRNoSoul     = 2;
constexpr uint8_t kRNoActor    = 3;
constexpr uint8_t kRUnarmed    = 4;   // the buff manager or StopFight did not arm
constexpr uint8_t kRFailed     = 5;

// Main thread. `out` gets the payload after [ok][seq][op][reason]; returns the
// reason. *outLen is the payload length.
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

} // namespace kcdmp::wo131
