// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-209: engine-free checks of wo209_rules.h. Linked into KCDMP_NativeTests; wo209_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo209_rules.h"

using namespace kcdmp::wo209rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo209_tests(int* passed) {
    // ---- the switch word and the bench's twin names
    WCHECK((kBitsDefault & kBitOppMirror) != 0, "A ships on");
    WCHECK(twin_off_bits("copy_opponent_mirror") == kBitOppMirror, "a twin name turns its bit off");
    WCHECK(twin_off_bits("x,copy_attack_ai") == kBitAttackAi, "anywhere in the list");
    WCHECK(twin_off_bits("copy_opponent_mirror_x") == 0 && twin_off_bits("opponent_mirror") == 0, "only a whole name");

    // ---- A: the host's kinds
    WCHECK(host_kind(0, 0x7777, false) == kHostNone, "no opponent: nobody");
    WCHECK(host_kind(0x7777, 0x7777, false) == kHostPlayer, "the host's own player");
    WCHECK(host_kind(0x7FF65, 0x7777, true) == kHostFigure, "a partner's figure (an avatar entity)");
    WCHECK(host_kind(0x30585, 0x7777, false) == kHostNpc, "another NPC");
    WCHECK(host_kind(0x7777, 0, false) == kHostNpc, "no local player read: never taken for the host");

    // ---- A: sent on a change, and again while it has one
    WCHECK(opp_send_due(true, 0.0, kHostNone), "a change to nobody is told");
    WCHECK(!opp_send_due(false, 100.0, kHostNone), "nobody, unchanged: quiet");
    WCHECK(!opp_send_due(false, kOppResendS - 0.01, kHostFigure) && opp_send_due(false, kOppResendS, kHostFigure), "resent every kOppResendS while fighting");
    WCHECK(kOppResendS < kMirrorStaleS, "the resend comes before the joiner's wish goes stale");

    // ---- A: the mirror's step per copy (adds only: an engage when the host's NPC fights the joiner's figure, never a removal)
    WCHECK(mirror_step(kHerePlayer, false) == Mirror::EngageWish, "the local player, not in his fight yet: his skirmish joined first");
    WCHECK(mirror_step(kHerePlayer, true) == Mirror::Wish, "already in his fight: the wish only");
    WCHECK(mirror_step(kHereEntity, true) == Mirror::Wish, "another opponent while in his fight: retargeted, never taken out of his skirmish");
    WCHECK(mirror_holds_combat(kHerePlayer, true), "held on him and in his fight: combat mode held (d7: first swings refused out of combat)");
    WCHECK(!mirror_holds_combat(kHerePlayer, false) && !mirror_holds_combat(kHereEntity, true) && !mirror_holds_combat(kHereNone, true),
           "not engaged, another opponent, nobody: WO-132's own word only");
    WCHECK(mirror_step(kHereEntity, false) == Mirror::Wish, "the host's figure or an NPC's copy: the wish");
    WCHECK(mirror_step(kHereNone, true) == Mirror::Clear, "nobody: the mirrored wish goes (the fight's end releases the skirmish, not the mirror)");
    WCHECK(wish_kind(kHerePlayer) == 1 && wish_kind(kHereEntity) == 2 && wish_kind(kHereNone) == 0, "the wo187 wish kinds");
    WCHECK(kMirrorTickS <= 0.1, "one stream tick");

    // ---- A.4's sample
    WCHECK(!sample_counts(kHereNone, 10.0), "nobody wanted: not sampled");
    WCHECK(!sample_counts(kHerePlayer, kFollowGraceS - 0.01) && sample_counts(kHerePlayer, kFollowGraceS), "the follow window first");

    // ---- B: the copy's own attack
    WCHECK(attack_frag(897) && attack_frag(906) && attack_frag(937) && attack_frag(907) && attack_frag(9) && attack_frag(89), "the attack fragments");
    WCHECK(!attack_frag(908) && !attack_frag(915) && !attack_frag(902) && !attack_frag(900) && !attack_frag(5), "paired actions, hits, blocks and idles are not");
    WCHECK(ai_window_outcome(true, true) == 0 && ai_window_outcome(true, false) == 0, "a landed blow: the defence watch told it");
    WCHECK(ai_window_outcome(false, true, 0.1) == kcdmp::wo208rules::kDefMissed, "nothing landed, his dodge seen: missed");
    WCHECK(ai_window_outcome(false, false, 0.6) == kcdmp::wo208rules::kDefMissed, "the copy's own attack ran its course without touching him: missed");
    WCHECK(ai_window_outcome(false, false, 0.2) == kcdmp::wo208rules::kDefNone, "cut short: none (the host's verdict at once)");
    WCHECK(ai_retry_due(10.1, 10.0) && ai_retry_due(10.5, 10.0), "a refused attack is asked again for half a second (d4: a combo's second swing)");
    WCHECK(!ai_retry_due(10.51, 10.0) && !ai_retry_due(9.9, 10.0), "then it lapses (the host's next attack comes as its own)");
    WCHECK(row_waits_for_mirror(false, true), "a row before the mirror's word, the copy in his fight: it waits (d7: a 897 dropped, started 0.9 s late)");
    WCHECK(row_waits_for_mirror(false, false) && row_waits_for_mirror(true, false), "a fight's first row, before the copy is in his fight: it waits too (d10)");
    WCHECK(!row_waits_for_mirror(true, true), "on him and in his fight: at once");

    // ---- the owner's copies and the activity rows (the neutral rule)
    WCHECK(!activity_refused_for_owner(false, true, true, true), "an owned copy takes the rows with the neutral rule on (d5: the camp bandit's spot)");
    WCHECK(activity_refused_for_owner(false, true, true, false), "twin (owner_neutral off): refused, as in WO-208");
    WCHECK(activity_refused_for_owner(true, true, true, true) && activity_refused_for_owner(false, true, false, true), "the one-copy probe, or the neutral rule off: refused");
    WCHECK(!activity_refused_for_owner(false, false, false, true) && !activity_refused_for_owner(false, false, true, false), "a copy the owner does not own: always taken");
    WCHECK(twin_off_bits("owner_neutral") == kFixOwnerNeutral && (kBitsDefault & kFixOwnerNeutral) == 0, "a fix's twin by name, never in the agent's word");
    WCHECK(twin_off_bits("owner_queue_cap") == kFixOwnerQueueCap && (kBitsDefault & kFixOwnerQueueCap) == 0 && kFixOwnerQueueCap != kFixOwnerNeutral,
           "the queue cap's twin by name");
    WCHECK(twin_off_bits("death_leave_clears") == kFixDeathLeave && (kBitsDefault & kFixDeathLeave) == 0 &&
           (kFixDeathLeave & (kFixOwnerNeutral | kFixOwnerQueueCap)) == 0, "the death leave's twin by name (rc4-c1x's joiner crash)");
    WCHECK(twin_off_bits("game_hostile_gate") == kFixGameHostile && (kBitsDefault & kFixGameHostile) == 0 &&
           (kFixGameHostile & (kFixOwnerNeutral | kFixOwnerQueueCap | kFixDeathLeave)) == 0, "the hostile gate's twin by name (rc5-c1x's joiner crash)");
    WCHECK(twin_off_bits("activity_drift_release") == kFixActivityDrift && twin_off_bits("owner_near_only") == kFixOwnerNear &&
           (kBitsDefault & (kFixActivityDrift | kFixOwnerNear)) == 0 && kFixActivityDrift != kFixOwnerNear, "the village fixes' twins by name");
    WCHECK(!activity_drifted(0.5f) && !activity_drifted(4.0f) && activity_drifted(4.5f) && activity_drifted(46.0f), "drift: a seated copy 46 m from its host NPC (rc4-village-1) lets go");
    WCHECK(owner_keeps(false, 10.0f) && owner_keeps(false, 60.0f) && !owner_keeps(false, 65.0f) && owner_keeps(true, 65.0f) && !owner_keeps(true, 71.0f) && !owner_keeps(true, 135.0f),
           "the owner near the local player: takes within 60 m, lets go past 70 m (130-148 m T-poses in rc6-village-2)");
    WCHECK(!owner_keeps(false, -1.0f), "no distance: not taken");
    WCHECK(!owner_queue_full(-1) && !owner_queue_full(0) && !owner_queue_full(5), "room: the owner installs (an unreadable queue is no cap)");
    WCHECK(owner_queue_full(6) && owner_queue_full(10), "six queued: the owner waits (rc1-c2tavern-1: 11,004 overflows on one copy)");

    // ---- B without the owner: the swing row's fragment and zone
    WCHECK(attack_frag_by_name("CombatAttackGen, leftGuard+eZ1+aZ1+slash") == 897, "an attack row");
    WCHECK(attack_frag_by_name("CombatAttackSpecialGen, r_noweapon+aZ5+kick") == 906 && attack_frag_by_name("CombatAttackComboGen, x") == 937, "special and combo");
    WCHECK(attack_frag_by_name("CombatHitGen, r_noweapon+sZ1") == 0 && attack_frag_by_name("CombatAttackSyncGen, x") == 0, "a hit and a sync attack are not");
    WCHECK(attack_frag_by_name("CombatAttackGenX, x") == 0 && attack_frag_by_name("") == 0 && attack_frag_by_name(nullptr) == 0, "a whole name only");
    WCHECK(row_zone("CombatAttackGen, leftGuard+eZ1+aZ1+slash") == 1 && row_zone("CombatAttackSpecialGen, r_noweapon+leftGuard+eZ2+aZ5+kick") == 5, "the aZ tag");
    WCHECK(row_zone("CombatHitGen, r_noweapon+sZ1+dZ5") == -1, "no aZ tag: the engine's pick");

    // ---- C.1: paired roles
    WCHECK(paired_role(914) == Paired::MasterStart && paired_role(918) == Paired::MasterStart, "the clinch start and guard as master: StartSyncGuard");
    WCHECK(paired_role(908) == Paired::MasterMove && paired_role(916) == Paired::MasterMove && paired_role(922) == Paired::MasterMove, "a sync attack, a clinch move, a master strike: the attack request");
    WCHECK(paired_role(915) == Paired::Held && paired_role(917) == Paired::Held && paired_role(912) == Paired::Held && paired_role(919) == Paired::Held, "the slave halves: his own engine's");
    WCHECK(paired_role(897) == Paired::No && paired_role(5) == Paired::No, "not paired");

    // ---- D.4: the wake's root
    { const int a[] = {1, 1, 1, 0}; WCHECK(wake_root(a, 4) == 2, "the topmost hibernated ancestor (the module)"); }
    { const int a[] = {1, 0, 1}; WCHECK(wake_root(a, 3) == 0, "the node alone when its parent is awake"); }
    { const int a[] = {0, 1, 1}; WCHECK(wake_root(a, 3) == -1, "an awake node: nothing to wake"); }
    WCHECK(wake_root(nullptr, 0) == -1, "no chain");
    WCHECK(kWakeReasonScript == 2, "the quest data's own Wake port's reason");
    WCHECK(twin_off_bits("quest_wake_with_host") == kBitQuestWake && (kBitsDefault & kBitQuestWake), "D.4 ships on, its twin turns it off");

    *passed = g_pass;
    return g_fail;
}
