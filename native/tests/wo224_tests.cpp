// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-224: engine-free checks of wo224_rules.h. Linked into KCDMP_NativeTests; wo224_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo224_rules.h"

using namespace kcdmp::wo224::rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

Side side(bool combat, bool hasOpp, bool oppIsOther, int guard, const char* action) {
    Side s{};
    s.read = true; s.combatMode = combat; s.hasOpponent = hasOpp; s.opponentIsOther = oppIsOther; s.guardType = guard;
    std::snprintf(s.action, sizeof s.action, "%s", action);
    return s;
}
} // namespace

int wo224_tests(int* passed) {
    // ---- the kinds of pair
    WCHECK(pair_class(true, true, false) == kClassMeCopy, "the local player and a listed copy");
    WCHECK(pair_class(true, false, false) == kClassMeOther && pair_class(true, false, true) == kClassMeOther, "the local player and any other body (the host's reference)");
    WCHECK(pair_class(false, false, true) == kClassFigure && pair_class(false, true, true) == kClassFigure, "a figure and an NPC, the local player not in it");
    WCHECK(pair_class(false, false, false) == kClassOthers, "two bodies of this world");
    WCHECK(std::strcmp(class_name(kClassMeCopy), "player-and-copy") == 0 && std::strcmp(class_name(9), "?") == 0, "names");
    WCHECK(std::strcmp(asker_name(kClassFigure, 0), "the figure") == 0 && std::strcmp(asker_name(kClassMeCopy, 1), "the other") == 0, "who asked");

    // ---- the engine's clinch test in its own order
    const Side ok = side(true, true, true, 0, "Guard");
    const float facing = -1.0f;   // face to face
    WCHECK(why_not(ok, ok, facing) == kPass, "two guards face to face in combat mode: the test passes");
    Side unread{};
    WCHECK(why_not(unread, ok, facing) == kUnread && why_not(ok, unread, facing) == kUnread, "a body that could not be read");
    WCHECK(why_not(side(true, false, false, 0, "Guard"), ok, facing) == kNoOpponent, "no opponent comes first");
    WCHECK(why_not(ok, side(true, true, false, 0, "Guard"), facing) == kNotMutual, "the other fights somebody else: not mutual");
    WCHECK(why_not(side(false, true, true, 0, "Guard"), ok, facing) == kSelfCombat, "the asker out of combat mode");
    WCHECK(why_not(side(true, true, true, 24, "Guard"), ok, facing) == kSelfGuardType, "the asker in a sync guard already");
    WCHECK(why_not(side(true, true, true, 0, "Attack"), ok, facing) == kSelfAction, "the asker mid-attack");
    WCHECK(why_not(ok, side(false, true, true, 0, "Guard"), facing) == kOppCombat, "the other out of combat mode");
    WCHECK(why_not(ok, side(true, true, true, 4, "Guard"), facing) == kOppGuardType, "the other in a sync guard");
    WCHECK(why_not(ok, side(true, true, true, 0, "none"), facing) == kOppAction, "the other with no main action (a copy playing a row)");
    WCHECK(why_not(ok, side(true, true, true, 0, "Block"), facing) == kOppAction, "a block is not clinchable");
    WCHECK(why_not(ok, ok, -0.70f) == kAngle && why_not(ok, ok, -0.77f) == kPass, "within 40 degrees of opposite");
    WCHECK(why_not(side(false, true, true, 24, "Attack"), side(false, true, true, 4, "none"), 1.0f) == kSelfCombat, "the first failed condition is the one named");
    WCHECK(why_not(side(true, true, true, -1, "Guard"), side(true, true, true, -1, "Hit"), facing) == kPass, "an unread guard type does not fail the test; a Hit is clinchable");
    WCHECK(clinchable("Guard") && clinchable("Dodge") && clinchable("Hit") && clinchable("SyncHit"), "the clinchable actions");
    WCHECK(!clinchable("Attack") && !clinchable("PerfectBlock") && !clinchable("none") && !clinchable("?") && !clinchable(nullptr), "everything else is not");
    for (int r = 0; r < kReasons; ++r) WCHECK(std::strcmp(reason_name(r), "?") != 0, "reason %d has a name", r);
    WCHECK(std::strcmp(reason_name(kReasons), "?") == 0, "no name past the last");

    // ---- in a clinch
    WCHECK(in_clinch("Guard", "GuardSyncMovement", 0), "the paired guard movement");
    WCHECK(in_clinch("SyncTransition", "none", 0), "a clinch move");
    WCHECK(in_clinch("SyncHit", "none", 25) && in_clinch("Guard", "none", 4) && in_clinch("Guard", "none", 24), "a sync guard type (a move's hit half)");
    WCHECK(!in_clinch("Guard", "GuardMovement", 0) && !in_clinch("Attack", "none", 0) && !in_clinch(nullptr, nullptr, -1), "a plain guard is not");
    WCHECK(sync_guard_type(4) && sync_guard_type(24) && sync_guard_type(25) && !sync_guard_type(0) && !sync_guard_type(5), "the sync guard types");
    WCHECK(held_in_sync_guard("GuardSyncMovement", 0) && held_in_sync_guard("none", 24), "held: the paired guard movement, or a sync guard type");
    WCHECK(!held_in_sync_guard("none", 0) && !held_in_sync_guard("GuardMovement", 0) && !held_in_sync_guard(nullptr, -1), "a move with the guard type back at main is on its way out");
    WCHECK(in_clinch("SyncTransition", "none", 0) && !held_in_sync_guard("none", 0), "in the clinch's last move: still in it, nothing for an end to undo");

    // ---- a body's name as 32 bits: the agent's Wo224Rules.NameHash, value for value
    WCHECK(name_hash("a") == 0xE40C292Cu, "FNV-1a of one byte");
    WCHECK(name_hash("tkop_vrahBandit_5") == 0x88988F7Fu && name_hash("kcd2mp_1") == 0x8FE98974u, "the agent's own values");
    WCHECK(name_hash("") == 0 && name_hash(nullptr) == 0, "no name: 0");
    WCHECK(name_hash("tkop_vrahBandit_5") != name_hash("tkop_vrahBandit_4"), "two bodies, two hashes");
    *passed = g_pass;
    return g_fail;
}
