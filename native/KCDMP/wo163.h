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
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo163 {

constexpr uint8_t kOpSkirmishHostile = 1;
constexpr uint8_t kOpStatus = 2;

constexpr uint8_t kROk = 0, kRBadRequest = 1, kRNoActor = 2, kRNoSoul = 3, kRFailed = 4;

// Pipe thread -> main thread (run_sync_bounded): one request.
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

int status_text(char* out, int n);

} // namespace kcdmp::wo163
