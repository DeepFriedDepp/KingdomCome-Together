// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-204 -- sleep and wait stop crashing, every copy's body gets one owner, the conversation feature finished
// (docs/WO-204-findings.md). The native half; the pure rules are wo204_rules.h, the agent's half GameBridge.Wo204*.cs.
// Act I (nearest_player_v2) lives in wo201_near.cpp (it rebuilt WO-201's V.2 and keeps its op 82).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), registered through wo203.h's registry (numbers 115..119 are WO-204's), main thread:
//   115 Config [bits:4] -> [bits:4]   wo204_rules.h kBit*: 1 seat_hold, 2 tpose_watch
//   116 Status -> text
#pragma once
#include <cstdint>

namespace kcdmp::wo204 {

constexpr uint8_t kOpConfig = 115, kOpStatus = 116;

bool on(uint32_t bit);   // wo204_rules.h kBit*; every bit on until the agent says otherwise
int status(char* out, int n);

// II.3 (wo141's reconcile, main thread): a copy held because it is seated or aligned at an object itself (not because the host wants it)
void note_seat_hold(bool fromCopyState);
uint32_t note_seat_move();   // a seated copy moved more than kSeatMoveCm since it was last seen seated; returns the count

} // namespace kcdmp::wo204
