// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-214: engine-free checks of wo214_rules.h. Linked into KCDMP_NativeTests; wo214_tests() returns the number of failures.
#include <cstdio>
#include <cmath>
#include <cstring>

#include "wo214_rules.h"
#include "wo210_rules.h"

using namespace kcdmp::wo214rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo214_tests(int* passed) {
    // ---- the switch word and the bench's twin names
    WCHECK((kBitsDefault & kBitSendTurn) && (kBitsDefault & kBitOneBind) && (kBitsDefault & kBitOwnerBase) && (kBitsDefault & kBitCombatEnd),
           "every WO-214 native switch ships on");
    WCHECK(twin_off_bits("one_bind") == kBitOneBind && twin_off_bits("x, owner_base,combat_end") == (kBitOwnerBase | kBitCombatEnd), "the twins");
    WCHECK(twin_off_bits("all") == kBitsDefault && twin_off_bits("npc_bands") == 0 && twin_off_bits(nullptr) == 0, "all; WO-213's names are not WO-214's");

    // ---- A.1 a turn is a change
    WCHECK(turned(0.10f, 0.0f, true) && !turned(0.01f, 0.0f, true), "a turn over 1 degree sends; under it, not");
    WCHECK(turned(3.135f, -3.135f, true) == false && turned(3.0f, -3.0f, true), "the turn is measured across the +-pi seam (0.013 rad: no; 0.28 rad: yes)");
    WCHECK(!turned(1.0f, 0.0f, false), "the twin (send_turn off): a turn alone never sends");

    // ---- B one bind per body
    int ent = 0, other = 0;
    WCHECK(bind_noop(true, true, 7, &ent, 7, &ent), "a bind asked again for the body bound to that same entity is a no-op");
    WCHECK(!bind_noop(true, true, 7, &ent, 8, &ent) && !bind_noop(true, true, 7, &ent, 7, &other) && !bind_noop(true, false, 7, &ent, 7, &ent),
           "another entity id, another entity object (a re-spawn), or nothing bound: a real bind");
    WCHECK(!bind_noop(false, true, 7, &ent, 7, &ent), "the twin (one_bind off): every bind binds afresh, as 0.64.0");
    WCHECK(!silence_drops(true, 4.5, false) && !silence_drops(true, 29.0, false) && silence_drops(true, 30.5, false),
           "a silent stream's body holds: two lost heartbeats (4.5 s) never drop it, only 30 s forgets it");
    WCHECK(silence_drops(false, 4.5, false) && !silence_drops(false, 4.5, true) && !silence_drops(false, 3.5, false),
           "the twin: 0.64.0's 4 s drop (a moving body kept to 10 s by WO-201's own rule)");
    WCHECK(resume_blends(true, 4.0) && resume_blends(true, 8.0) && !resume_blends(true, 2.1) && !resume_blends(false, 8.0),
           "a stream back after the old drop time (4 s) starts from where the body stands, a 2 s heartbeat renders as before; the twin: nothing");

    // ---- C.1 a base in the same frame
    WCHECK(base_for(true, true) == Base::HostBase && base_for(false, true) == Base::NeutralIdle && base_for(false, false) == Base::Nudge,
           "the host's base when known, else the copy's neutral idle, else the nudge");
    WCHECK(kcdmp::wo210rules::kPathEmptyRefused == 19 && kcdmp::wo210rules::kPathEmptyOwn == 20 && kcdmp::wo210rules::kPathCount == 21,
           "the two empty-body paths have names (no reseat is charged to 'unknown' for them)");
    WCHECK(std::strcmp(kcdmp::wo210rules::path_name(kcdmp::wo210rules::kPathEmptyRefused), "19-empty-refused") == 0 &&
           std::strcmp(kcdmp::wo210rules::path_name(kcdmp::wo210rules::kPathEmptyOwn), "20-empty-own") == 0, "their names");

    // ---- C.2 an action that is no longer the host NPC's ends
    // the field: the owner's ReadingBook went on 26 s after its host NPC stood up (6 actions queued behind it)
    WCHECK(stale_action(true, true, false, 26.0, 23.0, 1e9, true) == Stuck::HostLeft, "the host NPC in a base for 23 s, the posture still held, actions waiting: stopped");
    WCHECK(stale_action(true, true, false, 26.0, 0.5, 1e9, true) == Stuck::No, "the host NPC only just reached its base (its stand-up's end): not yet");
    WCHECK(stale_action(true, true, false, 26.0, 23.0, 1e9, false) == Stuck::No, "nothing waits behind it: left (the owner's own hand-over runs)");
    WCHECK(stale_action(true, false, false, 249.0, 249.0, 1e9, true) == Stuck::No, "no candidate (our base, a base family, a paired action, mirrors the host): nothing");
    WCHECK(stale_action(true, true, true, 2.0, 0.0, 1.5, false) == Stuck::FightOver, "a combat row, its host NPC out of combat 1.5 s: stopped");
    WCHECK(stale_action(true, true, true, 0.5, 0.0, 1.5, true) == Stuck::No, "a combat row younger than a second is left to finish");
    WCHECK(stale_action(true, true, true, 5.0, 0.0, 0.0, true) == Stuck::Outlived && stale_action(true, true, true, 4.0, 0.0, 0.0, true) == Stuck::No,
           "in a fight: a combat row held 5 s with an action behind it outlived any row; 4 s, not yet");
    WCHECK(stale_action(true, true, false, 5.0, 0.0, 0.0, true) == Stuck::No, "a posture (not a combat row) while its host NPC is not in a base: left");
    WCHECK(combat_fragment_name("CombatHitGen") && combat_fragment_name("CombatAttackGen") && !combat_fragment_name("ReadingBook") && !combat_fragment_name("Lying"),
           "a combat row is known by its fragment's name -- the owner's posture installs run through the combat manager too");
    WCHECK(stale_action(false, true, false, 249.0, 249.0, 1e9, true) == Stuck::No, "the twin (combat_end off): the field's 249 s behind the action stays");
    WCHECK(stoppable_class(true) && !stoppable_class(false),
           "only the combat manager's own action is stopped -- never the engine's walk transition (d3: 35 stops on one body, its movement system queued it again)");
    WCHECK(churn(10.0, 19.5) && !churn(10.0, 20.5), "three stops within 10 s on one copy: it keeps coming back -- the rule rests (not after 10.5 s)");
    WCHECK(kChurnStops == 3 && kChurnRestS == 30.0, "the rest after churn: three stops, then 30 s");

    *passed = g_pass;
    return g_fail;
}
