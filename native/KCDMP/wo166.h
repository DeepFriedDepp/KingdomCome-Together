// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-166 -- 0.48.2's native half (docs/WO-166-findings.md). The pure rules are wo166_rules.h.
//
//   C3 mp_copy_strikes (joiner, default ON): during the window of a host swing the copy plays at the joiner, the copy's combat model
//      reads Striking (research/WO-162 Q1.4: E_CombatActorStateId 8) with the row's four attack fields, so the joiner's engine answers
//      it as it answers a real attacker (block, parry, riposte prompts); at the window's end the prior state and fields go back. Every
//      field is a named property block checked by its own name first (motion.cpp prop_named), main thread, fault-guarded, the copy
//      looked up in the frame it is written. The copy is on the WO-132 discard list for the window: any engine hit it lands on the
//      local player is measured and put back -- the host still decides (WO166-LOCALHIT dropped). Any fault switches C3 off for the
//      session (logged; the agent says it on screen once).
//   C1 the figure's combat automation (host): which of the engine's four combat automations stay on while a partner's figure is
//      held in combat (motion.cpp apply_combat) -- the A/B lever of the attack gate (docs/WO-166-findings.md 0.5 / C1).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   9  Strike [startMs:i16][hitMs:i16][type:i8][zone:i8][hand:i8][strength:f32][nameLen:1][name] -> text "queued window_ms=<b>..<e>" | "refused=<why>"
//   10 Status -> text
//   11 Config [copyStrikes:1][autoMode:1][snapFix:1] -> [copyStrikes:1][autoMode:1][snapFix:1]   (autoMode / snapFix 255 = unchanged)
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo166 {

constexpr uint8_t kOpStrike = 9, kOpStatus = 10, kOpConfig = 11;

// C1: the figure's automation while held in combat. 0 = all four off (0.48.0); 1 = all on; 2.. = on with the enable bytes of mode-2
// (bit 0 = +0x79, bit 1 = +0x7A, bit 2 = +0x7B; the automation call's enable flag set) -- the lever's patterns.
constexpr uint8_t kAutoAllOff = 0, kAutoAllOn = 1, kAutoBytesBase = 2, kAutoMax = 9;

// Pipe thread -> main thread (run_sync_bounded). Returns a WO-163 reason code (kROk ...).
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

void tick();                       // main thread, every frame (dllmain post_repeating)
bool striking_recently(uint32_t eid);   // main thread: this copy is (or was within 2 s) in a strike window of ours
void note_local_hit_dropped(uint32_t eid, float hp, float st);   // hits.cpp's discard watch (main thread)
uint8_t auto_mode();               // C1 (motion.cpp reads it)
int status_text(char* out, int n);
void on_pipe_closed();

} // namespace kcdmp::wo166
