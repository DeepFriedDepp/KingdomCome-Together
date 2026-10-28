// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-208 -- one owner per copy, the joiner's own defence, the conversation's remaining failures (docs/WO-208-findings.md). The native
// half; the pure rules are wo208_rules.h, the agent's half GameBridge.Wo208*.cs.
//   C.2 mp_conv_cam_player_view: a talk whose dialogue cameras never take over sends the starter's own view (wo203_cam.cpp asks on()).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), registered through wo203.h's registry (numbers 130..139 are WO-208's), main thread:
//   130 Config [bits:4] -> [bits:4]   wo208_rules.h kBit* (a bit the bench's twin names in KCDMP_TEST_W208_OFF stays off)
//   131 Status -> text
//   132 StreamRead [max:1] -> [count:1] count x ([nameLen:1][name][cm:1][nsc:1] nsc x 34-byte scope)   (A.2, the host: its NPCs' two scopes)
//   133 StreamIn [n:1] n x ([eid:4][cm:1][nsc:1] nsc x 34-byte scope) -> [n:1]                         (A.2, the joiner: kept for the owner)
#pragma once
#include <cstdint>

namespace kcdmp::wo208 {

constexpr uint8_t kOpConfig = 130, kOpStatus = 131, kOpStreamRead = 132, kOpStreamIn = 133;
// WO-210 (the owner's data lives here): 140 the asks the agent does the game's way, 141 the host's stream forgets bodies (sent again)
constexpr uint8_t kOpW210Asks = 140, kOpW210StreamForget = 141;

bool on(uint32_t bit);   // wo208_rules.h kBit*: the agent's word, less the bench twin's
int status(char* out, int n);
// A.1 the probe (the bench only, KCDMP_TEST_W208_PROBE + KCDMP_TEST_W208_FEED / _FEED_HOST in the games' environment): this copy is the
// probe's -- every other writer leaves it alone (any thread; false when no probe runs).
// WO-210 0.3: an owned copy's host NPC FullBody as the owner last applied it (the probe's "hf"); false when not owned or unknown
bool host_fullbody(uint32_t eid, uint8_t* cls, uint32_t* frag);
bool probe_owned(uint32_t eid);
// The same for the writers of a copy's COMBAT STATE (its engagement in the player's fight, its strike window, its opponent, its guard
// hold): only the bench's A.1 copy (driven "only from the recorded state"); with the owner on every copy they stay (the owner's rule 7:
// combat relations are read, never rewritten by it, and Part B needs the engagement and the strike window).
bool probe_only(uint32_t eid);
bool probe_owned_name(const char* name);
// probe_only by the NPC's name (a writer addressed by name).
bool probe_only_name(const char* name);
// F.1 (mp_target_in_fight, main thread): may the local player be written as this copy's opponent now? Only while the engine has the copy and
// the player in a skirmish fight (its own relation test, read-only); counted and told when not (site: who asked -- "wish", "strike").
bool target_player_ok(uint32_t copy, const char* site);   // the same, by the NPC's name (a writer addressed by name)
// WO-210 E.1 (main thread): target_player_ok's answer alone -- read-only, nothing counted, told or healed.
bool in_player_fight(uint32_t copy);
// A.2 with B (main thread): how much later than its row's arrival this copy's swing starts -- the owner applies the host's state one
// stream delay after it arrived; 0 for a copy the owner does not drive. WO-166 opens the copy's strike window that much later.
double owner_shift_s(uint32_t eid);

} // namespace kcdmp::wo208
