// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-208: engine-free checks of wo208_rules.h. Linked into KCDMP_NativeTests; wo208_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo208_rules.h"

using namespace kcdmp::wo208rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo208_tests(int* passed) {
    // ---- the switch word and the bench's twin names
    WCHECK((kBitsDefault & kBitPlayerView) != 0, "C.2 ships on");
    WCHECK(twin_off_bits("conv_cam_player_view") == kBitPlayerView, "a twin name turns its bit off");
    WCHECK(twin_off_bits("x,conv_cam_player_view,y") == kBitPlayerView, "anywhere in the list");
    WCHECK(twin_off_bits("conv_cam_player_view_x") == 0 && twin_off_bits("cam_player_view") == 0, "only a whole name");
    WCHECK(twin_off_bits("") == 0 && twin_off_bits(nullptr) == 0, "no list, nothing off");

    // ---- C.2: the starter's own view after kHeadLiftMs without a dialogue shot
    WCHECK(!head_lift(true, 1000 + kHeadLiftMs - 1, 0, 1000), "a talk's first moments wait for its first shot");
    WCHECK(head_lift(true, 1000 + kHeadLiftMs, 0, 1000), "no shot since the talk armed: the starter's view goes out");
    WCHECK(!head_lift(true, 5000 + 300, 5000, 1000), "a shot a moment ago: the head view is the cut between shots, not sent");
    WCHECK(head_lift(true, 5000 + kHeadLiftMs, 5000, 1000), "the shots stopped: the starter's view again");
    WCHECK(!head_lift(true, 999999, 0, 0), "a sampler that never armed sends nothing");
    // twin: the old rule (the switch off) never sends the head view -- d29's talks with every dialogue camera invalid sent nothing at all
    WCHECK(!head_lift(false, 1000 + 60000, 0, 1000), "twin: off, the head view is never sent");
    WCHECK(kHeadLiftMs < 3000, "under the listener's 3 s silence rule");

    // ---- F.1: the local player as a copy's opponent only inside his fight
    WCHECK(target_player_allowed(true, true, true, true), "held in his fight by the mod and the engine agrees: allowed");
    WCHECK(!target_player_allowed(true, true, true, false), "the engine says out of his fight: not written");
    WCHECK(!target_player_allowed(true, true, false, false), "unanswered: not written (fail closed)");
    WCHECK(!target_player_allowed(true, false, true, true), "released by WO-132 while the engine still answers hostile (d9-deftwin's crash): not written");
    WCHECK(target_player_allowed(false, false, true, false), "twin: off, written whatever the fight (the crash's state)");
    WCHECK((kBitsDefault & kBitTargetInFight) != 0 && twin_off_bits("target_in_fight") == kBitTargetInFight, "ships on; its twin name");

    // ---- B: the joiner's own defence
    WCHECK(def_outcome(0, 0, 0) == kDefHit && def_outcome(1, 0, 0) == kDefBlocked && def_outcome(1, 1, 0) == kDefPerfect && def_outcome(1, 1, 1) == kDefBroken,
           "the engine's verdict bytes -> the outcome");
    WCHECK(!std::strcmp(def_outcome_name(kDefMissed), "missed") && !std::strcmp(def_outcome_name(99), "?"), "outcome names");
    WCHECK(kDefNone == 6 && !std::strcmp(def_outcome_name(kDefNone), "none"), "a window with nothing landed is told as none (the wire's 6)");
    WCHECK(def_window_missed(10.0, -1.0, true) && def_window_missed(10.0, 9.9, true) && !def_window_missed(10.0, 10.2, true),
           "a window with his dodge and no landed blow is a miss");
    WCHECK(!def_window_missed(10.0, -1.0, false), "no landed blow and no dodge: nothing told (the host's verdict applies)");
    {   // twin: the first rule (no dodge needed) told a miss for every window whose blow did not land -- the joiner invulnerable
        auto first_rule = [](double start, double landed) { return landed < start; };
        WCHECK(first_rule(10.0, -1.0), "twin: the first rule calls the same window a miss");
    }
    WCHECK((kBitsDefault & kBitDefenceLocal) != 0 && twin_off_bits("defence_local") == kBitDefenceLocal, "ships on; its twin name");

    // ---- A: the owner's rules
    WCHECK(family(nullptr, 0) == Fam::Empty && family("", 0) == Fam::Empty, "no action: empty");
    WCHECK(family("C_AnimActionIdleState", 5) == Fam::Idle && family("CAnimActionAIMovement", 5) == Fam::Idle && family("CAnimActionAIMovement", 0) == Fam::Idle,
           "the standing idle: MotionIdle (male 5, female 0)");
    WCHECK(family("C_AnimActionIdleState", 45) == Fam::Other && family("C_AnimActionIdleState", 34) == Fam::Other && family("C_AnimActionIdleState", 10) == Fam::Other,
           "a posture (Lying 45, SittingIdle 34 / female 10): the host's, installed -- the baseline's lying villagers stood on the joiner");
    WCHECK(family("C_VariationAction", 121) == Fam::Hold && family_of_id(kClsVariation, 123) == Fam::Hold, "an idle variation holds");
    WCHECK(family_of_id(kClsIdleState, 45) == Fam::Other && family_of_id(kClsIdleState, 5) == Fam::Idle && family_of_id(kClsAIMovement, 0) == Fam::Idle,
           "the wire's class ids: the same families");
    WCHECK(family("CAnimActionAIMovement", 4) == Fam::Loco && family("CAnimActionAIMovement", 1) == Fam::Loco && family("CAnimActionMovementTransition", 57) == Fam::Loco,
           "locomotion (male MotionMovement 4, female 1; a turn is the copy's own)");
    WCHECK(family("CAnimActionMovementTransition", 35) == Fam::Other && family("CAnimActionMovementTransition", 36) == Fam::Other &&
           family_of_id(kClsMoveTransition, 18) == Fam::Other && family_of_id(kClsMoveTransition, 74) == Fam::Other,
           "E.1: the walk's start and stop (IdleToMove / MoveToIdle, male 35 / 36, female 18 / 74) are the host's, installed");
    WCHECK(family("C_CombatAnimAction", 908) == Fam::Hold && family("C_CombatAnimAction", 915) == Fam::Hold && family_of_id(kClsCombatAnim, 914) == Fam::Hold &&
           family_of_id(kClsCombatAnim, 66) == Fam::Hold && family_of_id(kClsCombatAnim, 922) == Fam::Hold,
           "a paired action (sync attack 908, clinch 914 / 915, sync 66, master strike 922) holds: its other half is the local engine's");
    WCHECK(family_of_id(kClsCombatAnim, 897) == Fam::Other && family_of_id(kClsCombatAnim, 906) == Fam::Other && family_of_id(kClsCombatAnim, 937) == Fam::Other,
           "a plain attack, a special, a combo: installed");
    {
        // the fight probe's clinch slave (915): pending, stale four times -- held, nothing is installed; the twin installs it again and again
        Own none;
        WCHECK(decide(family_of_id(kClsCombatAnim, 915), 0xD1, 4, false, none) == Do::Nothing, "the clinch's slave half: nothing installed");
        WCHECK(decide(Fam::Other, 0xD1, 4, false, none) == Do::Install, "twin: taken for a plain action it is installed (and goes stale)");
    }
    WCHECK(kWithdrawnPriority < kBasePriority && kBasePriority < 2, "rule 1's base of ours: over a withdrawn action, under the copy's own AI movement (2)");
    WCHECK(family("C_CombatAnimActionMovement", 8) == Fam::CombatMove, "combat footwork");
    WCHECK(family("C_CombatAnimAction", 897) == Fam::Other && family("C_CallbackAction", 39) == Fam::Other && family("C_DialogAnimAction", 438) == Fam::Other,
           "an attack, a callback, a gesture: installed by its fragment");
    {
        Own none;
        WCHECK(decide(Fam::Loco, 0, 0, false, none) == Do::Nothing, "the host walks, nothing of ours: the copy's own locomotion");
        WCHECK(decide(Fam::Other, 0xA1, 5, false, none) == Do::Install, "the host attacks: the copy gets its fragment");
        WCHECK(decide(Fam::Other, 0xA1, 5, true, none) == Do::Nothing, "the copy already runs it: only the differences");
        Own mine; mine.have = true; mine.hostAct = 0xA1; mine.prio = 5; mine.status = 2; mine.ageS = 0.1;
        WCHECK(decide(Fam::Other, 0xA1, 5, false, mine) == Do::Nothing, "the same host action, ours installed: nothing");
        WCHECK(decide(Fam::Other, 0xA1, 3, false, mine) == Do::StepPriority, "rule 6: the host's own withdraw (5 -> 3) followed");
        WCHECK(decide(Fam::CombatMove, 0xB2, 4, false, mine) == Do::Withdraw, "rule 4: the host's attack ended -- ours goes, the base comes back");
        WCHECK(decide(Fam::Other, 0xA2, 5, false, mine) == Do::Install, "rule 2: the host's next attack -- the caller withdraws ours first");
        mine.status = 1; mine.ageS = kStaleS + 0.01;
        WCHECK(decide(Fam::Other, 0xA1, 5, false, mine) == Do::Stale, "rule 3: ours never started in 300 ms -- removed");
        mine.ageS = kStaleS - 0.01;
        WCHECK(decide(Fam::Other, 0xA1, 5, false, mine) == Do::Nothing, "rule 3: not yet");
        Own w = mine; w.status = 2; w.prio = kWithdrawnPriority;
        WCHECK(decide(Fam::Other, 0xA1, 5, false, w) == Do::Nothing, "a withdrawn action of ours is never stepped back up");
        // twin: an owner without rule 4 (no hand back) leaves the copy's attack running while the host's NPC is back in its footwork --
        // m0's c1fight: 'locomotion' against 'C_CombatAnimAction#897' was the largest family of mismatches
        WCHECK(decide(Fam::CombatMove, 0xB2, 4, false, mine) != Do::Nothing, "twin: an owner that does nothing here keeps the attack running");
        // a posture: the host's villager lies in bed (IdleState Lying), fidgets (Lying_VAR), lies again -- the copy keeps lying throughout
        Own lying; lying.have = true; lying.hostAct = 0xC1; lying.prio = 2; lying.status = 2; lying.ageS = 5.0;
        WCHECK(decide(family("C_AnimActionIdleState", 45), 0xC1, 2, false, Own{}) == Do::Install, "the host lies down: the copy gets Lying");
        WCHECK(decide(family("C_VariationAction", 788), 0xC2, 2, false, lying) == Do::Nothing, "its fidget: the copy keeps lying");
        WCHECK(decide(family("CAnimActionAIMovement", 5), 0xC3, 2, false, lying) == Do::Withdraw, "it stands up: ours goes");
        // twin: the first design's families (every idle state a base) left the lying villager standing on the copy
        WCHECK(decide(Fam::Idle, 0xC1, 2, false, Own{}) == Do::Nothing, "twin: a posture taken for the base installs nothing");
    }
    {
        FeedSample s;
        const char* line = "t 1761456789123 cm 1 s FullBody C_CombatAnimAction 897 5 2 0 0102030405060708090a0b0c0d0e0f1011121314 1f2e3d4c5b6a "
                           "s UpperBody C_CombatAnimAction 1141 5 1 4 0000000000000000000000000000000000000000 7f\n";
        WCHECK(parse_feed(line, &s) && s.t == 1761456789123ULL && s.cm == 1 && s.n == 2, "a feed line parses (%d scopes)", s.n);
        WCHECK(!std::strcmp(s.sc[0].scope, "FullBody") && !std::strcmp(s.sc[0].cls, "C_CombatAnimAction") && s.sc[0].frag == 897 && s.sc[0].prio == 5 &&
               s.sc[0].status == 2 && s.sc[0].tags[0] == 1 && s.sc[0].tags[19] == 0x14 && s.sc[0].act == 0x1f2e3d4c5b6aULL, "every field of a scope");
        WCHECK(s.sc[1].frag == 1141 && s.sc[1].flags == 4 && s.sc[1].act == 0x7f, "the second scope");
        WCHECK(parse_feed("t 5 cm 0\n", &s) && s.n == 0, "a body with no scope: an empty sample");
        WCHECK(!parse_feed("x 5 cm 0", &s) && !parse_feed("t 5 cm 0 s FullBody C 1 2 3 4 0102 ab", &s), "a malformed line is refused");
    }

    *passed = g_pass;
    return g_fail;
}
