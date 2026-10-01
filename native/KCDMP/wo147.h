// WO-147 -- the joiner can fight, and the leash pulls: the native half
// (docs/WO-147-findings.md).
//
// Pipe 0x29 [op][...] -> 0xAA [ok][seq][op][reason][payload]:
//   1 TestPlayerHit  [eid:4][hp:4f][st:4f] -> [before:4f][after:4f]
//       A console stand-in for one landed swing of the LOCAL player (no input is
//       ever sent): the body is marked as hit by the player, exactly as the
//       C_CombatSoul hit hook marks a real blow (hits.cpp), and the damage is
//       taken with the player's soul as the attacker. Everything after the swing
//       is the real path: the health sampler reports the drop as the player's
//       LocalHit, the agent's gate judges it, the host applies it.
//   2 Status         -> text
//   3 SoulGuidOfEid  [eid:4] -> [guid:16]
//       The soul a body really holds. A by-name soul lookup answers with ANY soul of
//       that name: for a stand-in copy of a host-spawned NPC it is the game's own
//       unplaced soul of the same name (Position 0,0,0) -- the live run killed that
//       one five times while the copy stood at 70 hp.
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo147 {

constexpr uint8_t kOpTestPlayerHit = 1;
constexpr uint8_t kOpStatus = 2;
constexpr uint8_t kOpSoulGuidOfEid = 3;

constexpr uint8_t kROk = 0, kRBadRequest = 1, kRNoActor = 2, kRFailed = 3;

// Pipe thread -> main thread (run_sync_bounded): one request.
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

} // namespace kcdmp::wo147
