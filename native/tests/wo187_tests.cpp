// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-187: engine-free checks of the opponent wishes, the guard hold, the figure's linger, the idle restore and the per-weapon perfect-blocked
// length (wo187_rules.h). Linked into KCDMP_NativeTests; wo187_tests() returns the number of failures.
#include <cmath>
#include <cstdio>
#include <string_view>

#include "wo166_rules.h"
#include "wo187_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
bool near(double a, double b) { return std::fabs(a - b) < 1e-9; }
} // namespace

int wo187_tests(int* passed) {
    using namespace wo187rules;
    // ---- A2: the wish's life, the wind-up verdict, the pin
    {
        WCHECK(opp_wish_live(10.0, 7.0) && !opp_wish_live(10.0, 6.0) && !opp_wish_live(10.0, 0.0), "a wish lives kOppStaleS after its last word");
        WCHECK(windup_verdict(false, false) == WindupOpp::NoWish && windup_verdict(false, true) == WindupOpp::NoWish, "no wish: nothing to check");
        WCHECK(windup_verdict(true, true) == WindupOpp::Held && windup_verdict(true, false) == WindupOpp::Overwritten, "held / overwritten");
        WCHECK(!pin_due(1, false, true) && pin_due(2, false, true) && !pin_due(3, true, true) && !pin_due(5, false, false),
               "pinned after two overwrites, once, and only with mp_opp_pin");
        WCHECK(std::string_view(windup_name(WindupOpp::Overwritten)) == "overwritten", "the log word");
    }
    // ---- C2: the guard hold and the figure's linger
    {
        WCHECK(guard_hold_wants(true, false) && guard_hold_wants(false, true) && !guard_hold_wants(false, false),
               "a copy holds guard while its host NPC is in combat or has a target (the CombatMode bit alone reads 0 against a player)");
        WCHECK(figure_combat_wants(true, true, 0.0, true) && !figure_combat_wants(true, false, 0.5, true), "a fresh sample decides at once");
        WCHECK(figure_combat_wants(false, false, 2.0, true) && !figure_combat_wants(false, false, 3.5, true), "a quiet stream keeps combat kFigureLingerS");
        WCHECK(!figure_combat_wants(false, false, 1.0, false) && !figure_combat_wants(false, false, -1.0, true), "no linger when off, or without a combat=1 before");
        WCHECK(idle_restore_due(true, 0.3, false, true) && !idle_restore_due(true, 0.1, false, true) && !idle_restore_due(true, 0.3, true, true)
               && !idle_restore_due(false, 1.0, false, true) && !idle_restore_due(true, 0.3, false, false),
               "an empty full-body scope for 0.25 s after a move gets the idle back, once per move, only with mp_anim_idle");
    }
    // ---- B3: the perfect-blocked length by weapon: the measured sword values, the fallback (said) elsewhere
    {
        bool m = false;
        WCHECK(near(pb_stage2(Weapon::Sword, 0, 5, &m), 0.41) && m, "sword stab zone 5: measured 0.41");
        WCHECK(near(pb_stage2(Weapon::Sword, 1, 0, &m), 0.50) && m && near(pb_stage2(Weapon::Sword, 1, 1, &m), 0.29) && m
               && near(pb_stage2(Weapon::Sword, 1, 2, &m), 0.41) && m, "sword slashes: the WO-185 medians");
        WCHECK(near(pb_stage2(Weapon::Sword, 7, 3, &m), kStage2Fallback) && !m, "a sword swing not measured: the fallback");
        WCHECK(near(pb_stage2(Weapon::Blunt, 1, 1, &m), kStage2Fallback) && !m, "a mace: not measured, the fallback (never the sword's value)");
        WCHECK(near(pb_stage2(Weapon::Unarmed, 5, 2, &m), kStage2Fallback) && !m, "fists: the fallback");
        WCHECK(near(pb_stage2(Weapon::Longsword, 0, 5, &m), kStage2Fallback) && !m, "a longsword: the fallback");
        WCHECK(near(pb_stage2(Weapon::Unknown, 1, 0, &m), 0.50) && m, "an unknown weapon (a 0.60.3 agent) keeps the 0.60.3 table");
        // the 0.60.3 table and the sword row agree
        for (int t = 0; t < 2; ++t) for (int z = 0; z < 6; ++z) {
            bool mm = false;
            WCHECK(near(pb_stage2(Weapon::Sword, t, z, &mm), wo166rules::pb_contact_stage2(t, z)), "sword = wo166rules::pb_contact_stage2 (type %d zone %d)", t, z);
        }
        WCHECK(weapon_from_byte(5) == Weapon::Blunt && weapon_from_byte(6) == Weapon::Unarmed && weapon_from_byte(200) == Weapon::Unknown, "the wire byte");
        WCHECK(std::string_view(weapon_name(Weapon::Blunt)) == "blunt" && std::string_view(weapon_name(Weapon::Unknown)) == "unknown", "names");
    }
    // ---- B2: a running copy is not held for its swing
    WCHECK(kChaseFollowMps > 1.5f && kChaseFollowMps < 3.0f, "the run threshold sits above a walk (1.4 m/s) and below a jog (3 m/s)");
    *passed = g_pass;
    return g_fail;
}
