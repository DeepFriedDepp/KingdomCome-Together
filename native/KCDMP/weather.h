// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-151 Phase 3.7 -- the host's live weather (docs/WO-151-findings.md).
//
// KCD2's weather is a time-of-day profile the environment blends to: the game's own
// random preset every N game hours, a load's saved one, a quest's. All of them go
// through EnvironmentModule.dll C_TimeOfDayBlender::BlendToProfile(this, const char*
// profile, blend, bool force) -> bool -- found by its own __FUNCTION__ string (one
// referencing function), the prologue compared before the patch (hook_prologues.h).
//
//   * host: every blend that runs is recorded (the profile, a change counter); the
//     agent reads it (wo151 op 6) and sends the session the host's own weather.
//   * joiner (in a session): only the host's profile may blend (wo151 op 7); the game's
//     own random preset on this machine is declined (BlendToProfile returns false, as
//     for a missing profile) -- the host's next profile arrives as a blend of its own.
//
// The callback runs on whatever thread blends (the environment module is a parallel
// updater): it copies at most 47 bytes under a short lock and touches nothing else.
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::weather {

void install();   // off the main thread, beside wo138::install (the patch suspends other threads)
bool armed();

// The last profile a blend ran with (NUL-terminated into out); the change counter
// (0 = no blend seen since the DLL loaded).
uint32_t last_blend(char* out, size_t cap);

// Joiner: only `profile` may blend; nullptr or "" = every blend runs.
void set_gate(const char* profile);
void on_pipe_closed();   // the gate opens (an agent that went away holds nothing)

int status_text(char* out, int n);

} // namespace kcdmp::weather
