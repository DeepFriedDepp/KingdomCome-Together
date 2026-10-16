// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-195: the pure rules of wo195.cpp (unit-tested in native/tests/wo195_tests.cpp). No engine access here.
#include <cstdint>

namespace kcdmp::wo195rules {

// What a dead copy still had from the mod when its release ran (the WO195-RELEASE line and op 56's answer).
constexpr uint8_t kHeldEngage = 0x01;   // WO-132: engaged with the local player (its skirmish, the combat hold, the discard)
constexpr uint8_t kHeldGuard  = 0x02;   // WO-187 C2: the guard hold (combat mode for the host NPC's fight)
constexpr uint8_t kHeldWish   = 0x04;   // WO-187 A2: the opponent wish (SetLogicalTarget, re-written on every re-check)
constexpr uint8_t kHeldPin    = 0x08;   // WO-187 A2: the wish's combat_forcedTarget pin
constexpr uint8_t kHeldCombat = 0x10;   // the writer held its combat mode / its automation off

// A gate refuses a request (or drops a hold) only with the switch on and a body that reads dead.
inline bool refuse(bool switchOn, bool dead) { return switchOn && dead; }

// The per-frame gate in the writer runs the release when the body is dead and the mod still holds it in a fight.
inline bool release_due(bool switchOn, bool dead, bool engageHeld, bool guardHeld) { return refuse(switchOn, dead) && (engageHeld || guardHeld); }

// A wish on a dead copy is never applied, never written back and never pinned: it is dropped (and its pin cleared).
inline bool wish_kept(bool switchOn, bool dead) { return !refuse(switchOn, dead); }

// One WO195-DEAD line per body and kind of refusal (the rest are counted).
constexpr int kSaidMax = 4096;

// op 57's answer: the body's combat mode and whether its physics is still a living entity (a fallen corpse is a ragdoll).
constexpr uint8_t kUnreadable = 255;
inline uint8_t tri(bool readable, bool v) { return readable ? (v ? 1 : 0) : kUnreadable; }

// The text of a held mask, for the logs ("engage+guard+wish", "none").
inline const char* part(uint8_t mask, uint8_t bit, const char* name) { return (mask & bit) ? name : ""; }

} // namespace kcdmp::wo195rules
