// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-222 (docs/WO-222-findings.md): the native half's switch word and status. The mechanisms live in files named after their system
// (blow_ledger.cpp: every blow and every swing accounted for; the others where their part says).
//
// Pipe ops in the WO-163 family, registered through wo203.h (numbers 200..209 are WO-222's):
//   200 Config [bits:4][role:1] -> [effective bits:4]
//   201 Status [page:1] -> text (0 = the switches, k = the k-th area's own line)
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo222 {

constexpr uint8_t kOpConfig = 200, kOpStatus = 201, kOpShown = 202;   // 202: B.1 is a reaction row showing on a body

// the native word's bits (the agent's W222Switch.NativeNames, in order)
constexpr uint32_t kBitReactParity = 1u << 0;   // A: a joiner's blow is applied on the host the way the host's own blow would be
constexpr uint32_t kBitReactShow = 1u << 1;     // B: the host's reaction plays on its copy ahead of anything the copy holds
constexpr uint32_t kBitFleeLive = 1u << 2;      // D: a fleeing host NPC is still an enemy here (its copy engaged, running)
constexpr uint32_t kBitSwingLog = 1u << 3;      // E: every swing of this player accounted for (read-only)
constexpr uint32_t kBitDeadLay = 1u << 4;       // F.1: a dead copy left standing is laid down
constexpr uint32_t kBitsDefault = kBitReactParity | kBitReactShow | kBitFleeLive | kBitSwingLog | kBitDeadLay;

// B: the priority a host's hit reaction row plays at on its copy (the engine's own hit reactions run over a swing's 5)
constexpr uint32_t kReactionPriority = 7;

uint8_t role();            // 0 none, 1 the host, 2 a joiner in the host's world
bool on(uint32_t bit);     // the agent's word, less the bench twin's
uint32_t twin_off_bits(const char* names);
using StatusFn = int (*)(char* out, int n);
bool add_status(const char* area, StatusFn fn);   // static-init time

} // namespace kcdmp::wo222
