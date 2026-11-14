// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// A copy in a conversation is asked for nothing (WO-221 B, mp_talk_hold; docs/WO-221-findings.md).
//
// A joiner's copy is woken for a talk: its own brain runs for the conversation, the game's own dialogue makes its twins, plays
// its lines and opens its shop. Until 0.65.0 three things were held for that copy (the host's activity placement, WO-157; the
// stream's position, WO-166; the neutral rule, WO-216) and the rest of the mod went on asking: the owner re-installed the host
// NPC's fragments on it, its hand, gait and one-shot rows were applied, the seat rule released it "from its own seat" with the
// game's state reset, and the owner's queue drain stopped its never-started actions -- each of them a request to a body the
// game's dialogue is holding still for its twins.
//
// Here the copy's whole "schedule" (everything the mod shows of its host NPC's day) stands down for the conversation: one word,
// told by the agent with the talk's ask and taken back with its end, read by every lane that asks something of a copy. The word
// lapses by itself (kLapseS) unless the agent renews it, so a lost end never leaves a copy unheld for good.
//
// Pipe op (the WO-163 family, registered through wo203.h; numbers 190..199 are WO-221's):
//   194 Talk [on:1][name len:1][name] -> [code:1]   (1 held, 2 released, 3 not held, 4 off)
#include <cstdint>

namespace kcdmp::talkhold {

constexpr uint8_t kOpTalk = 194;
constexpr uint8_t kHeld = 1, kReleased = 2, kNotHeld = 3, kOff = 4;
constexpr double kLapseS = 45.0;   // the agent renews every 15 s while the conversation runs

enum class Lane { Owner, Rows, Seat, Count };

// main thread: is this copy in a conversation now (and the switch on)? `lane` counts who stood down.
bool holds(uint32_t eid, Lane lane);
bool holds_name(const char* name, Lane lane);

} // namespace kcdmp::talkhold
