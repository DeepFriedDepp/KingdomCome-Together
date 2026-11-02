// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-213 (docs/WO-213-findings.md): the internet stream's native half -- its switch word (op 146, with D.2's bands), its status
// (op 147), and the counters the sender (wo138.cpp), the writer (npc_drive.cpp) and the owner (wo208.cpp) report to.
#include <cstddef>
#include <cstdint>
#include "wo213_rules.h"
#include "local_state.h"
#include "motion.h"

namespace kcdmp::wo213 {

constexpr uint8_t kOpConfig = 146, kOpStatus = 147;

bool on(uint32_t bit);                  // wo213_rules.h kBit*: the agent's word, less the bench's twin (KCDMP_TEST_W213_OFF)
const wo213rules::Bands& bands();       // D.2: the agent's (the defaults until it says)

// D.2 the sender's counters (host)
void note_band_send(int band);
// E.1 the writer's: a stream's applied play-out delay at a second's recompute, and its target; frames held at the extrapolation cap
void note_playout(double appliedS, double targetS);
void note_extrap_capped();
// E.2 the owner's: a check that found the host's state late (no reseat), a body asked for again, a breach that was missing (fresh state)
void note_late_check();
void note_late_ask();
void note_missing_check();
// E.3: a frame whose position write was skipped for a holding posture; a held body found off its place (it takes the stream again)
void note_posture_skip();
void note_posture_drift(const char* name, float offM);

// D.4 (the pipe's reader thread): the local player's state from this frame's read, when the switch is on and the read is fresh;
// false: the caller reads on the main thread as before. Every call marks the agent as asking (the frame hook reads while it asks).
bool local_take(localstate::LocalState* ls, bool* ok, motion::State2* st2, bool* ok2);

int status(char* out, int n);

} // namespace kcdmp::wo213
