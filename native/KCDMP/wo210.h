// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-210 (docs/WO-210-findings.md): every copy always animated (A), clinches (B), deaths and knockdowns (C), combat readiness (D/E),
// riders (G), one state request (H.1). The native half's switch word, its status and the instruments' counters; the owner's hand-over
// itself lives with the owner (wo208.cpp).
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo210 {

constexpr uint8_t kOpConfig = 138, kOpStatus = 139;

bool on(uint32_t bit);        // the agent's word, less the bench's twin (KCDMP_TEST_W210_OFF)
bool fix_on(uint32_t bit);    // a fix with no switch of its own: on unless the twin names it

// A: the owner's hand-over and watchdog report here (counters in the status, the reseat lines in the log)
void note_handover(uint8_t path, bool installed, bool neutral);
void note_check(uint8_t verdict);
void note_reseat(const char* npc, const char* was, const char* want, uint8_t path, double breachS, bool ok);
void note_release(uint8_t path);

// B.1: every clinch start the joiner's engine asks between the local player and a copy (wo178's gate, whichever thread runs it):
// counted per copy; more than 3 within 1 s is a loop, told once per stretch (WO210-CLINCH loop) with who started it.
void note_clinch_ask(uint32_t copyEid, bool refused, bool askerIsPlayer);
void tick();   // main thread: the detector's lines (the gate's thread only counts)

int status(char* out, int n);

} // namespace kcdmp::wo210
