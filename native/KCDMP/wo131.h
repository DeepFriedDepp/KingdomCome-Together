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
//   op 2 CopyGuard  [on:1][eid:4] -> [present:1]
//                   The joiner's copy of a host NPC can never die or be knocked
//                   out locally (1c): kcdmp_avatar_guard (imm=1, upr=1) on it,
//                   removed before the host's death is applied.
//   op 3 FollowHp   [guid:16][hp:4f] -> [before:4f][after:4f]
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
