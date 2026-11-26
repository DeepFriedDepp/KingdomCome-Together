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
    // ---- B: the guard a row starts from
    WCHECK(row_guard_side("CombatAttackGen, leftGuard+eZ1+aZ1+slash+attack_heavy") == 1 && row_guard_side("CombatAttackSpecialGen, rightGuard+eZ2+aZ5+stab") == 0,
           "the row's own tag");
    WCHECK(row_guard_side("CombatAttackSyncGen, clinch1+eZ1+endClinch1+aZ2+slash") == -1 && row_guard_side(nullptr) == -1 && row_guard_side("") == -1, "a row that does not say");
    WCHECK(guard_zone_of_side(1) == 2 && guard_stance_of_side(1) == 0 && guard_zone_of_side(0) == 1 && guard_stance_of_side(0) == 1,
           "the model's own values before a row of each guard: the other side from the swing's own zone");
    WCHECK(guard_zone_of_side(-1) == -1 && guard_stance_of_side(-1) == -1, "no side: nothing to set");
    // ---- B: a chain input
    WCHECK(attack_action("Attack") && attack_action("StaticAttack") && attack_action("SyncAttack"), "the attack actions");
    WCHECK(!attack_action("FailedAttack") && !attack_action("Guard") && !attack_action("Hit") && !attack_action("none") && !attack_action(nullptr), "everything else is not a follow-up");
    WCHECK(chain_tell(true, false, false, true), "an NPC's input with its slot open, at a partner's figure: told");
    WCHECK(!chain_tell(false, false, false, true), "the slot closed: the engine itself dropped the input, nothing to tell");
    WCHECK(!chain_tell(true, true, false, true) && !chain_tell(true, false, true, true) && !chain_tell(true, false, false, false), "not the local player's, not a figure's, not at anybody else");
    WCHECK(chain_before(true, true, true, false, 0.0) == kChainApply, "the copy's slot is open: its input now");
    WCHECK(chain_before(false, true, true, false, 0.4) == kChainWait && chain_before(false, true, true, true, 0.4) == kChainWait, "its swing runs, the slot not open yet: waits (row or no row)");
    WCHECK(chain_before(true, false, true, false, 0.1) == kChainWait, "not held in the local player's fight yet: waits");
    WCHECK(chain_before(false, true, false, true, 0.2) == kChainFresh, "the follow-up's row is here and the copy runs no swing: a fresh attack, not a wait");
    WCHECK(chain_before(false, true, false, true, 0.1) == kChainWait && chain_before(false, true, false, false, 0.5) == kChainWait, "a moment first; without the row there is nothing to start");
    WCHECK(chain_before(false, true, true, true, kChainSlotWaitS + 0.01) == kChainFresh && chain_before(true, true, true, false, kChainSlotWaitS + 0.01) == kChainLapse,
           "past the wait: the row's attack fresh, or nothing");
    WCHECK(chain_after(true, "Attack", 0.3) == kAfterFollowed && chain_after(true, "Guard", 0.3) == kAfterNotFollowed && chain_after(true, "Hit", 0.1) == kAfterNotFollowed,
           "the next action in the main scope: the follow-up, or not");
    WCHECK(chain_after(false, "Attack", 0.5) == kAfterWait && chain_after(false, "Attack", kChainFollowS + 0.01) == kAfterLapse, "the swing still runs: waits, bounded");
    WCHECK(chain_after(false, "Guard", 0.3, false) == kAfterWait && chain_after(false, "Guard", kChainFollowIdleS + 0.01, false) == kAfterLapse &&
           chain_after(false, "Attack", kChainFollowIdleS + 0.01, true) == kAfterWait, "an input made with no swing running: a short wait only");
    // b6: the slot's force
    WCHECK(chain_before(false, true, true, true, 0.4, true) == kChainForce, "the host NPC chained, the copy's swing made no contact: the slot is forced");
    WCHECK(chain_before(false, true, true, false, 0.4, true) == kChainWait, "no row yet: the host NPC's follow-up is not known to have started");
    WCHECK(chain_before(true, true, true, true, 0.4, true) == kChainApply, "the engine opened it: no force");
    WCHECK(chain_before(false, false, true, true, 0.4, true) == kChainWait, "not in the local player's fight: no force");
    WCHECK(chain_before(false, true, false, true, 0.1, true) == kChainWait && chain_before(false, true, false, true, 0.2, true) == kChainFresh, "no swing: nothing to force");
    WCHECK(!chain_force_due(0.01, 0.9, 0.5), "never in the row's own frame");
    WCHECK(!chain_force_due(0.1, 0.4, 0.6) && chain_force_due(0.25, 0.6, 0.6), "when the copy's swing is as old as the host NPC's was");
    WCHECK(chain_force_due(kChainForceMaxS, 0.3, 0.9), "bounded after the row");
    WCHECK(!chain_force_due(0.1, -1.0, 0.6) && chain_force_due(0.2, -1.0, 0.6) && !chain_force_due(0.1, 0.5, 0.05) && chain_force_due(0.2, 0.5, 5.0),
           "ages not known or not plausible: a fixed moment after the row");
    *passed = g_pass;
    return g_fail;
}
