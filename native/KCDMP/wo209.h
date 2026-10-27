// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-209 -- each copy's opponent mirrored from the host's NPC's, the joiner's defence through the copy's own combat AI, the owner on every
// copy. The native half; the pure rules are wo209_rules.h, the agent's half GameBridge.Wo209*.cs.
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), registered through wo203.h's registry (numbers 134..139 are WO-209's), main thread:
//   134 Config [bits:4] -> [bits:4]   wo209_rules.h kBit* (a bit the bench's twin names in KCDMP_TEST_W209_OFF stays off)
//   135 Status -> text
//   136 OppRead [max:1] -> [count:1] count x ([nameLen:1][name][kind:1][oppEid:4][oppNameLen:1][oppName])   (A, the host)
//         kind 0 nobody, 1 the host's own player, 2 a partner's figure (oppEid: its entity), 3 another NPC (oppName)
//   137 OppIn [n:1] n x ([copyEid:4][kind:1][targetEid:4]) -> [n:1]                                        (A, the joiner)
//         kind 0 nobody, 1 the local player, 2 an entity here (a figure, or the copy of the NPC the host's NPC fights)
#pragma once
#include <cstdint>

namespace kcdmp::wo209 {

constexpr uint8_t kOpConfig = 134, kOpStatus = 135, kOpOppRead = 136, kOpOppIn = 137;

bool on(uint32_t bit);   // wo209_rules.h kBit*: the agent's word, less the bench twin's
// A fix with no switch of its own (wo209_rules.h kFix*): on unless the bench's twin turns it off.
bool fix_on(uint32_t fixBit);
int status(char* out, int n);
// A (main thread): the mirror holds this copy in the local player's fight -- its host NPC fights his figure there now (wo132's op 2 never
// lets it go for its distance meanwhile).
bool mirror_holds_player(uint32_t copy);
// A (main thread): the mirror speaks for this copy's opponent (WO-187's agent wish leaves it alone).
bool mirrored(uint32_t copy);
// B (main thread, mp_copy_attack_ai): is this host action (its class and fragment) to be the copy's own combat attack at the local player --
// an attack fragment, the mirror holding the copy in his fight, the engine's AI attack found? The owner asks before it installs.
bool attack_ai_wants(uint32_t copy, uint8_t cls, uint32_t frag);
// B: the copy's attack driven this way (the owner drives it and the mirror holds it on him): WO-166's strike window leaves it alone (the
// engine's own attack runs its own states and windows).
bool attack_ai_drives(uint32_t copy);
// B (main thread): start the copy's own attack the engine's way; false when the engine refused it (the owner then installs the host's
// fragment as before). Its window's outcome is told at its end (a landed blow: the defence watch; else missed with a dodge, or none).
bool ai_attack(uint32_t copy, uint32_t hostFrag);
// B without the owner (main thread): a swing row of the host's attack (WO-167's spec, "Fragment, tags") on an NPC copy the mirror holds on
// the local player -- the copy's own attack instead, its zone the row's aZ tag. True = started (the row is not played).
bool row_attack(uint32_t copy, const char* spec);
// B (main thread): an attack of the copy's own started within the last withinS seconds (by the row or the owner).
bool ai_attack_recent(uint32_t copy, double withinS);
// B: the copy's own attack is still running (its window open).
bool ai_attack_running(uint32_t copy);
// B: the copy's own combat attack owns its full body now (running, B on): the owner leaves its full body alone.
bool ai_owns_body(uint32_t copy);
// C.1 (main thread, mp_copy_paired_engine): is this host fragment a paired action to play the engine's way with the local player as the
// partner (the mirror holds the copy in his fight)? The owner asks for a paired fragment (which it otherwise holds).
bool paired_wants(uint32_t copy, uint32_t frag);
// C.1: the host's NPC leads it: the clinch start through StartSyncGuard (master = the copy, slave = the local player), a sync attack or a
// clinch move through the copy's own attack request; a half the joiner leads stays held. False = refused or held (logged with the reason).
bool paired_engine(uint32_t copy, uint32_t frag);

} // namespace kcdmp::wo209
