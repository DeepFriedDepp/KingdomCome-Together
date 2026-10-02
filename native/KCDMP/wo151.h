// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-151 -- the safeguards (docs/WO-151-findings.md): the native half's switches.
//
// Pipe 0x2A [op][...] -> 0xAB [ok][seq][op][reason][payload]:
//   1 Config    [faultSwitchOff:1][mainCost:1] -> [faultSwitchOff:1][mainCost:1]
//       mp_fault_switchoff (default on: a game-code call site that faulted 8 times this run
//       is switched off, fault_guard.h) and mp_main_cost (default off: the per-task
//       frame-cost meter, main_thread.cpp).
//   2 Status    -> text: the fault totals, the switches.
//   3 TestFault [kind:1] -> [ran:1][faults:4][off:1]
//       A deliberate fault for the live check of the guard: kind 0 reads address 0x10 at
//       the Read site "wo151::test/read" (never switched off), kind 1 calls address 0x10
//       at the Call site "wo151::test/call" (switched off at the 8th). Nothing of the
//       game's is touched: the faulting instruction is our own read, or the jump.
//   4 TestTakeDamage [victimEid:4][attackerEid:4][hp:f][st:f][mode:1] -> [before:f][after:f]
//       CombatSoul::TakeDamage on the soul of victimEid (0 = the local player) with the
//       soul of attackerEid as the cause (0 = none): mode 0 two arguments, 1 three (the
//       attacker), 2 four with SuppressHitReaction=false, 3 four with it true. The live
//       check of which call plays the victim's hit reaction (Phase 1.2, 1.3).
//   5 CopyFight [eid:4][on:1] -> [written:1][already:1][failed:1]
//       Phase 1.1: a joiner's copy in a fight runs none of its own hit reactions -- the
//       entity contexts combat_actorSupressHitreactionAnimation (the game's own switch:
//       side effect actorSupressHitreactionAnimation), switch_disabledHitReaction and
//       switch_disabledHitBehavioralReaction on its soul. Off clears only what this set.
//   6 WeatherRead -> [count:4][len:1][name]: the last time-of-day profile a blend ran with (weather.h).
//   7 WeatherGate [len:1][name] -> [] : a joiner's gate -- only that profile may blend (len 0 = off).
//   8 JoinHold [on:1][maxS:2] -> [held:1]: the host's world held for a whole join (wo138::join_hold).
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo151 {

constexpr uint8_t kOpConfig = 1;
constexpr uint8_t kOpStatus = 2;
constexpr uint8_t kOpTestFault = 3;
constexpr uint8_t kOpTestTakeDamage = 4;
constexpr uint8_t kOpCopyFight = 5;
constexpr uint8_t kOpWeatherRead = 6;
constexpr uint8_t kOpWeatherGate = 7;
constexpr uint8_t kOpJoinHold = 8;     // [on:1][maxS:2] -> [held:1]: wo138::join_hold (3.8)

constexpr uint8_t kROk = 0, kRBadRequest = 1, kRNoActor = 2, kRFailed = 3;

// Pipe thread -> main thread (run_sync_bounded): one request.
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

// Phase 1.1 (main thread): a copy left the session or was released -- its contexts go.
void copy_fight_forget(uint32_t eid);
int status_text(char* out, int n);

} // namespace kcdmp::wo151
