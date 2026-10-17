// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-197: engine-free checks of the recoil and clinch polls, the main actions A6 keeps and the pair request (wo197_rules.h). Linked into
// KCDMP_NativeTests; wo197_tests() returns the number of failures.
#include <cstdio>

#include "wo197_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo197_tests(int* passed) {
    using namespace wo197rules;
    // B1: the kind of a recoil
    WCHECK(recoil_flags(true, true, 0x100) == (kRecoilPerfect | kRecoilKnown), "a perfect block: PB, known (before the plain block)");
    WCHECK(recoil_flags(true, false, kStateParryInPlace) == kRecoilKnown, "State 0x100: a block, known");
    WCHECK(recoil_flags(true, false, 2) == 0 && recoil_flags(false, true, 0x100) == 0, "the opponent moved on / unread: unknown");
    WCHECK(recoil_flags_at_contact(true, false, 0x20, true, false, kStateParryInPlace) == kRecoilKnown,
           "the opponent already in his withdraw (0x20): the block read before the contact counts");
    WCHECK(recoil_flags_at_contact(true, false, 0x20, true, true, 0x100) == (kRecoilPerfect | kRecoilKnown), "a perfect block read before the contact");
    WCHECK(recoil_flags_at_contact(true, true, 0x100, true, false, 2) == (kRecoilPerfect | kRecoilKnown), "the read now wins");
    WCHECK(recoil_flags_at_contact(true, false, 0x40, true, false, 2) == 0 && recoil_flags_at_contact(false, false, 0, false, false, 0) == 0,
           "a hit (0x40, before: guard) or nothing read: unknown");
    WCHECK(contact_edge(8, 0x10) && !contact_edge(4, 0x10) && !contact_edge(0x10, 0x10) && !contact_edge(8, 0x20), "the contact is 8 -> 0x10 only");
    WCHECK(recoil_rows_for_zone(5) && !recoil_rows_for_zone(0) && !recoil_rows_for_zone(1) && !recoil_rows_for_zone(2) && !recoil_rows_for_zone(-1),
           "failed-attack rows exist for the stab zone (5) only");
    int a = 0, b = 0;
    WCHECK(new_recoil(nullptr, &a, "FailedAttack"), "a FailedAttack appears: one recoil");
    WCHECK(!new_recoil(&a, &a, "FailedAttack"), "the same action again: not a second recoil");
    WCHECK(new_recoil(&a, &b, "FailedAttack"), "a new FailedAttack object: the next recoil");
    WCHECK(!new_recoil(nullptr, &a, "PoseModifier") && !new_recoil(nullptr, nullptr, "FailedAttack"), "a pose modifier, or nothing: no recoil");
    // B3: the clinch's phases
    WCHECK(clinch_of("Guard", "GuardSyncMovement") == Clinch::Idle, "scope 1 GuardSyncMovement: the clinch idle");
    WCHECK(clinch_of("SyncTransition", "GuardSyncMovement") == Clinch::Move && clinch_of("SyncTransition", "") == Clinch::Move, "scope 0 SyncTransition: a move");
    WCHECK(clinch_of("Guard", "GuardMovement") == Clinch::None && clinch_of("Attack", "") == Clinch::None, "a plain guard / attack: no clinch");
    WCHECK(clinch_event(Clinch::None, Clinch::Idle, false) == kClinchEnter, "none -> idle: enter");
    WCHECK(clinch_event(Clinch::Idle, Clinch::Move, true) == kClinchMove && clinch_event(Clinch::None, Clinch::Move, true) == kClinchMove, "a new move");
    WCHECK(clinch_event(Clinch::Move, Clinch::Move, false) == 0, "the same move on the next read: nothing");
    WCHECK(clinch_event(Clinch::Move, Clinch::Idle, false) == 0, "back to the clinch idle: nothing");
    WCHECK(clinch_event(Clinch::Idle, Clinch::None, false) == kClinchLeave && clinch_event(Clinch::Move, Clinch::None, false) == kClinchLeave, "out: leave");
    // B5: what A6 never ends, and when EnsureGuardAction has work
    WCHECK(keep_main_action("Hit") && keep_main_action("SyncHit") && keep_main_action("SyncTransition") && keep_main_action("SyncPerfectBlockHit"),
           "real reactions and paired moves are kept");
    WCHECK(!keep_main_action("Guard") && !keep_main_action("Attack") && !keep_main_action("none") && !keep_main_action(""), "a guard / attack / none is not");
    WCHECK(needs_guard("none") && needs_guard("Attack") && !needs_guard("Guard"), "EnsureGuardAction only when scope 0 is not a Guard");
    // B2: the pair request
    WCHECK(pair_request_ok(1 + 4 + 4 + 1 + 1 + 10 + 1 + 12, 10, 12), "a well-formed pair");
    WCHECK(pair_request_ok(1 + 4 + 4 + 1 + 1 + 0 + 1 + 12, 0, 12), "the victim's row alone");
    WCHECK(!pair_request_ok(1 + 4 + 4 + 1 + 1 + 10 + 1 + 0, 10, 0), "an attacker row with no victim row: refused");
    WCHECK(pair_request_ok(1 + 4 + 4 + 1 + 1 + 0 + 1 + 0, 0, 0) && pair_is_query(0, 0) && !pair_is_query(0, 3), "no rows at all: the query");
    WCHECK(!pair_request_ok(40, 10, 12), "a length that does not add up: refused");
    WCHECK(kKindAvatarRecoil == 19 && kKindAvatarClinch == 20, "the ActionKind values (Protocol.cs, append-only)");
    *passed = g_pass;
    return g_fail;
}
