// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-221 (docs/WO-221-findings.md): the native half's switch word and status. The mechanisms themselves live in files named after
// their system (combat_actions.cpp, body_drive.cpp, holders.cpp), not after this work order.
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), registered through wo203.h (numbers 190..199 are WO-221's):
//   190 Config [bits:4][role:1] -> [effective bits:4]
//   191 Status -> text
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo221 {

constexpr uint8_t kOpConfig = 190, kOpStatus = 191;

// the native word's bits (the agent's W221Switch.NativeNames, in order)
constexpr uint32_t kBitCombatActionEnd = 1u << 0;   // A: no combat animation action of ours outlives its body's combat part
constexpr uint32_t kBitTalkHold = 1u << 1;          // B: a copy woken for a talk has its own schedule held while it talks
constexpr uint32_t kBitDrive = 1u << 2;             // C: a walking human copy is moved by the engine's per-frame movement request
constexpr uint32_t kBitHolders = 1u << 3;           // C.3: one table of who holds a body
constexpr uint32_t kBitOwnHorse = 1u << 4;          // D: each player's own horse (the quest gate's horse rule on this machine)
constexpr uint32_t kBitCorpseLook = 1u << 5;        // E.1: a dead body's wounds from the host's body
constexpr uint32_t kBitsDefault = kBitCombatActionEnd | kBitTalkHold | kBitDrive | kBitHolders | kBitOwnHorse | kBitCorpseLook;

uint8_t role();            // 0 none, 1 the host, 2 a joiner in the host's world
bool on(uint32_t bit);     // the agent's word, less the bench twin's
uint32_t twin_off_bits(const char* names);
// A WO-221 mechanism's own numbers, appended to the status text (op 191; the agent prints it as MP-W221-NATIVE). Static-init time.
using StatusFn = int (*)(char* out, int n);
bool add_status(const char* area, StatusFn fn);

} // namespace kcdmp::wo221
