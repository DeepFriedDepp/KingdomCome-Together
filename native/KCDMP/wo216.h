// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-216 (docs/WO-216-findings.md): the rest of the body -- the native half. The pure rules are wo216_rules.h.
//
// A  route_replay. The host reads each streamed NPC's movement task -- the request its brain made: the goal, the speed class, the exact
//    end -- at the AI update's entry, on that update's own thread (wo215.cpp: the same entry hook that makes the route's requests), and
//    hands every change to the agent (op 162), which sends it on the state lane (NpcRoute 0x84 -> 0x85, kind npc-route). The joiner's
//    copy makes that request once per change (op 163 brings the lines in); the position stream only corrects.
// B  no_brains. No NPC on the joiner runs its own brain: once a second every NPC body here is checked; one with a running brain is
//    suspended at once (the engine's own wh_ai_PauseNPC: C_IntelligentObject::Suspend) and, unless the host streams it, hidden.
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), registered through wo203.h (numbers 160..169 are WO-216's):
//   160 Config [bits:4] -> [effective bits:4]          wo216_rules.h kBit* (KCDMP_TEST_W216_OFF names a twin's bits off)
//   161 Status -> text
//   162 RouteRead [max:1] -> [count:1]{[nameLen:1][name][rec:52]}   the host: the changed request lines (off the main thread)
//   163 RouteIn [count:1]{[eid:4][rec:52]}                           the joiner: the host's lines, by this world's entity ids
//   164 Brains -> text                                               B's census now (the bench)
//   165 Exempt [on:1][secs:2][nameLen:1][name]                       B: a body the mod's own feature runs here on purpose (a guard's stop,
//                                                                    a talk, the horse this player rides) is left running for secs
//   166 OutfitRead [max:1] -> [count:1]{[nameLen:1][name][n:1] n x [class:16]}   D, the host: the bodies whose worn items changed (their
//        whole worn set, armour and weapons, as the soul's EquipmentManager holds it; each streamed body read every ~4 s, round-robin)
//   167 OutfitOf [nameLen:1][name] -> [found:1][n:1] n x [class:16]               D, the joiner: what this copy wears now
//   op 160 takes a role byte after the bits: 0 none, 1 the host, 2 a joiner in the host's world (B runs only then)
#include <cstddef>
#include <cstdint>
#include "wo216_rules.h"

namespace kcdmp::wo216 {

constexpr uint8_t kOpConfig = 160, kOpStatus = 161, kOpRouteRead = 162, kOpRouteIn = 163, kOpBrains = 164, kOpExempt = 165,
                  kOpOutfitRead = 166, kOpOutfitOf = 167;

bool on(uint32_t bit);       // the agent's word, less the bench twin's
bool configured();           // the agent told this DLL its word (a joiner or a host in a session)

int status(char* out, int n);

} // namespace kcdmp::wo216
