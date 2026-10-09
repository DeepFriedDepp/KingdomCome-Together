// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-163 -- shared combat, built on the WO-162 contracts (docs/WO-163-findings.md, research/WO-162/combat-RE.md): the native half's
// pipe ops. The pure rules are wo163_rules.h (engine-free, unit-tested).
//
// Pipe 0x2B [op][...] -> 0xAC [ok][seq][op][reason][payload]:
//   1 SkirmishHostile [nameLen:1][name] -> [answered:1][hostile:1]
//       A7 (WO-162 Q2.3), read-only: the engine's own relation test between the LOCAL PLAYER (the host) and the NPC of that name --
//       true iff they are hostile opponents in one skirmish. Main thread, fault-guarded, both souls looked up in this frame.
//       answered 0 = the call could not be made (not armed on this build, no such NPC, no soul).
//   2 Status -> text
//   3 ModelRead [nameLen:1][name] -> text   (nameLen 0 = the local player) -- the probes P1 / P3 (docs/WO-163-findings.md), read-only: the combat model's
//       State / guard / block fields and the attack fields the hit core reads (research/WO-162 Q1.4), each marked valid only when its
//       property block names itself (a trailing '!' = it did not); the Opponent's entity id. Keys: ca / pca = has a combat actor / it is
//       the player's; state (a bitmask: 1 Idle 2 Guard 4 ReadyToStrike 8 Striking 0x10 FailedAttack 0x20 Withdraw 0x40 Hit 0x80 PreparingToParry
//       0x100 ParryInPlace 0x200 Dodge 0x400 Transition); gz GuardZone; bz BlockZoneId; bh BlockHandSlot; bm BlockMode; pb PerfectBlockState;
//       az AttackZone; at AttackType; as AttackStrength; ah AttackHandSlot; cm CombatMode; opp the Opponent's entity id ((me) = the player).
//   4 SkirmishPair [on:1][override:1][nameLen:1][name] -> [done:1]   -- the probe P6 and the host lock-on's lever (C1): on = the HOST's soul joins the
//       skirmish of the NPC with the given override (1 = the explicit hostile pair the lock-on rule needs, WO-162 Q2.3); off = the host's
//       soul leaves its skirmish (RemoveSoulFromSkirmish; the fight itself goes on). Main thread, fault-guarded, souls looked up this frame.
//   5 Replay [flags:1][type:i8][zone:i8][hand:i8][strength:f32][aLen:1][attacker][vLen:1][victim] -> text   (WO-165, wo165.h: the blow
//       replayed through the engine's own hit processor; vLen 0 = the local player; flags wo165::kFlag*). The text starts
//       "seq=<n> engine=<hit|blocked|pb|broken|filtered|none> ..." or "refused=<reason>".
//   6 ReplayDamage [seq:4] -> [state:1][health:f32][stamina:f32][victimLive:1]   (WO-165: 0 unknown, 1 pending, 2 measured, 3 merged)
//   7 ReplayStatus -> text
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo163 {

constexpr uint8_t kOpSkirmishHostile = 1;
constexpr uint8_t kOpStatus = 2;
constexpr uint8_t kOpModelRead = 3;
constexpr uint8_t kOpSkirmishPair = 4;
constexpr uint8_t kOpReplay = 5;
constexpr uint8_t kOpReplayDamage = 6;
constexpr uint8_t kOpReplayStatus = 7;

constexpr uint8_t kROk = 0, kRBadRequest = 1, kRNoActor = 2, kRNoSoul = 3, kRFailed = 4;

// Pipe thread -> main thread (run_sync_bounded): one request.
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

int status_text(char* out, int n);

} // namespace kcdmp::wo163
