// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-214 (docs/WO-214-findings.md): the native half's switch word (op 148), its status (op 149), and the counters the sender (wo138.cpp),
// the writer (npc_drive.cpp) and the owner (wo208.cpp) report to.
#include <cstddef>
#include <cstdint>
#include "wo214_rules.h"

namespace kcdmp::wo214 {

constexpr uint8_t kOpConfig = 148, kOpStatus = 149;

bool on(uint32_t bit);   // wo214_rules.h kBit*: the agent's word, less the bench's twin (KCDMP_TEST_W214_OFF)

// A.1 (host): a send whose only change was a turn
void note_turn_send();
// B (joiner's writer): a bind asked again for a bound body (left as it was); a silent body held past the old 4 s drop; a stream that came
// back after a silence (its body blended from where it stood); a body forgotten after kBoundForgetS of silence
void note_rebind_noop();
void note_silence_hold();
void note_silence_resume(double silentS);
void note_forget();
// C.1 (joiner's owner): what a let-go or a refused posture was given; a posture install refused
void note_base_given(wo214rules::Base b, bool atLetGo);
void note_posture_refused();
// C.2 (joiner's owner): a combat action withdrawn because its host NPC's fight was over, or because it outlived a row
void note_combat_withdrawn(wo214rules::Stuck why, const char* name, double heldS, uint32_t frag);
void note_combat_churn(const char* name, uint32_t frag, double spanS);   // C.2: the rule rests on a copy whose stopped action keeps coming back
// D.1/D.2 (pipe, main thread): a reaction row played on a copy the owner owns (a death row, or a hit)
void note_owned_reaction(bool death);

int status(char* out, int n);

} // namespace kcdmp::wo214
