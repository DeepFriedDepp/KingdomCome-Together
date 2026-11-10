// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-218 (docs/WO-218-findings.md): the release candidate -- the native half's switch word and status. The pipe's batch itself is
// pipe_server.cpp (wo218_rules.h); its switch is the agent's (the DLL serves a batch whenever one comes).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), registered through wo203.h (numbers 180..189 are WO-218's):
//   180 Config [bits:4][role:1] -> [effective bits:4][caps:1]   caps bit 0: this DLL serves batches (kBatch 0x2D)
//   181 Status -> text
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo218 {

constexpr uint8_t kOpConfig = 180, kOpStatus = 181;
constexpr uint8_t kCapBatch = 1;

// the native word's bits (the agent's W218Switch.NativeNames, in order)
constexpr uint32_t kBitTakeoverStop = 1u << 0;   // C.3: a placed activity is never taken over by the copy's own later action
constexpr uint32_t kBitDriftRelease = 1u << 1;   // C.1: a drifted activity hold releases the copy's own posture too (one writer)
constexpr uint32_t kBitMainBudget = 1u << 2;   // B.2: the digest sweep spends at most kDigestBudgetUs of a frame
constexpr uint32_t kBitsDefault = kBitTakeoverStop | kBitDriftRelease | kBitMainBudget;
constexpr double kDigestBudgetUs = 300.0;
constexpr size_t kDigestMinReads = 8;

uint8_t role();            // 0 none, 1 the host, 2 a joiner in the host's world
bool on(uint32_t bit);     // the agent's word, less the bench twin's
uint32_t twin_off_bits(const char* names);
uint32_t note_takeover();  // C.3: one copy's own action stopped (the count)
uint32_t note_drift_release();  // C.1: one drifted copy's own posture released (the count)

} // namespace kcdmp::wo218
