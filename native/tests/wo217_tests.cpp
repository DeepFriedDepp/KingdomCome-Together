// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-217: engine-free checks of wo217_rules.h. Linked into KCDMP_NativeTests; wo217_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo217_rules.h"

using namespace kcdmp::wo217rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo217_tests(int* passed) {
    // ---- the switch word and the bench's twin names
    int nk = 0;
    const Name* k = switch_names(&nk);
    uint32_t all = 0;
    for (int i = 0; i < nk; ++i) all |= k[i].b;
    WCHECK(all == kBitsDefault && nk == 11, "every WO-217 native switch has a name and ships on (%d names, 0x%X)", nk, all);
    WCHECK(engine_dialog_twin("DialogTwin_ttkc_woman_2") && engine_dialog_twin("DialogTwin_Dude") && !engine_dialog_twin("ttkc_woman_2")
           && !engine_dialog_twin("Dialog") && !engine_dialog_twin(nullptr), "0.1: the engine's dialogue twins by name, nothing else");
    WCHECK(twin_off_bits("placement") == kBitPlacement && twin_off_bits("loot_live, item_flags") == (kBitLootLive | kBitItemFlags), "the twins by name");
    WCHECK(twin_off_bits("all") == kBitsDefault && twin_off_bits("no_brains") == 0 && twin_off_bits(nullptr) == 0, "all; WO-216's names are not WO-217's");

    // ---- A.0 an attempt's outcome: the first that fits
    {
        Counts c{};
        WCHECK(judge(5.0, 0.4, 0.0, c) == Outcome::Placed, "shown 0.4 s in and kept: placed");
        WCHECK(judge(5.0, 0.4, 1.5, c) == Outcome::PlacedThenLost, "shown, then gone 1.5 s while the host held it: placed then lost");
        WCHECK(judge(5.0, 0.4, 0.5, c) == Outcome::Placed, "a 0.5 s flicker is no loss");
        c.refused = 1; c.deferred = 3;
        WCHECK(judge(5.0, -1, 0, c) == Outcome::Refused, "an engine refusal names the attempt first");
        c = {}; c.deferred = 4; c.queueFull = 2;
        WCHECK(judge(5.0, -1, 0, c) == Outcome::Deferred, "left to the NPC-state placement");
        c = {}; c.stale = 1; c.installs = 1;
        WCHECK(judge(5.0, -1, 0, c) == Outcome::Stale, "installed, never started: stale");
        c = {}; c.queueFull = 3;
        WCHECK(judge(5.0, -1, 0, c) == Outcome::Paced, "never installed for a full queue: paced");
        c = {}; c.held = 2;
        WCHECK(judge(5.0, -1, 0, c) == Outcome::Held, "a one-shot or an attack held the body");
        c = {};
        WCHECK(judge(0.6, -1, 0, c) == Outcome::Superseded && judge(3.0, -1, 0, c) == Outcome::NotTried, "superseded under 1 s, else not tried");
        WCHECK(shows(7, 7, 0, 5, true) && shows(9, 7, 5, 5, true) && !shows(9, 7, 1, 5, true), "the same fragment, or the same posture family");
    }

    // ---- A.1 the deferral to a placement that never comes
    WCHECK(!defer_to_npc_state(true, true, 1) && !defer_to_npc_state(true, true, 2), "placement on, the neutral rule on: never deferred");
    WCHECK(defer_to_npc_state(false, true, 1) && defer_to_npc_state(true, false, 2), "the twin, or a planner that places: deferred as before");
    WCHECK(!defer_to_npc_state(true, true, 0) && !defer_to_npc_state(false, false, 3), "no desired activity, or given up: not this rule's");

    // ---- A.1 #2 the copy's own stale actions
    WCHECK(unclog(true, true, false, 1, "CAnimActionLooking") && unclog(true, true, false, 0, "CAnimActionAIMovement"),
           "a full queue held by the copy's own pending look or movement: stopped");
    WCHECK(!unclog(true, true, true, 1, "CAnimActionLooking") && !unclog(true, true, false, 2, "CAnimActionLooking") &&
           !unclog(true, true, false, 1, "C_CombatAnimAction") && !unclog(true, false, false, 1, "CAnimActionLooking") &&
           !unclog(false, true, false, 1, "CAnimActionLooking"), "ours, running, another class, room in the queue, or the twin: never");

    // ---- 0.2 one writer for a seated copy
    WCHECK(seat_rule(true, true, false, true, 0.8f) == Seat::Hold, "seated 0.8 m off the host's: the seat holds it (the old 30 cm rule handed it back)");
    WCHECK(seat_rule(true, true, false, true, 3.0f) == Seat::PlaceOnce, "3 m from its host NPC's seat: one write puts it there");
    WCHECK(seat_rule(true, true, true, true, 1.0f) == Seat::Hold, "in its own seat by its seated host NPC: held");
    WCHECK(seat_rule(true, false, true, false, 0.5f) == Seat::Release && seat_rule(true, true, true, true, 34.0f) == Seat::Release,
           "its own seat while its host NPC stands, or sits 34 m off (p0a's 117 m tugs): released the game's way");
    WCHECK(seat_rule(false, true, true, true, 0.8f) == Seat::NotSeated && seat_rule(true, false, false, false, 0.1f) == Seat::NotSeated,
           "the twin, or nobody seated: the old rules");
    WCHECK(seat_rule(true, true, false, false, 0.6f) == Seat::Hold && seat_rule(true, true, false, false, 4.0f) == Seat::PlaceOnce,
           "its host NPC seated, the copy not yet: held where it is (the stream never drags it), one write when it is far");
    WCHECK(seat_rule(true, false, false, true, 0.1f) == Seat::Release, "still sitting while its host NPC stands: released the game's way");

    // ---- B.2 the copy stays down
    WCHECK(refall(true, true, true, false, 1.0, 0), "stood up by its engine while its host lies: laid down again");
    WCHECK(!refall(true, true, true, true, 1.0, 0) && !refall(true, true, false, false, 1.0, 0) && !refall(false, true, true, false, 1.0, 0),
           "still ragdolled, the host up, or the twin: no refall");
    WCHECK(!refall(true, true, true, false, 0.3, 0) && !refall(true, true, true, false, 5.0, kRefallMax), "the gap and the bound");

    // ---- B.3 the alias of a long name (the same sums the mod computes)
    {
        char a[16];
        long_alias("horse[animalcare/horseCare6_ff15a088-7332-4a17-9536-3e8f6a8898ad]", a);
        WCHECK(std::strcmp(a, "lh838eb35a") == 0 && is_alias(a), "the animal care's horse 6: lh838eb35a (%s)", a);
        long_alias("horse[animalcare/horseCare7_b349ab28-b506-4522-b81c-dea3e8ed0e17]", a);
        WCHECK(std::strcmp(a, "lhe580b514") == 0, "horse 7: lhe580b514 (%s)", a);
        WCHECK(!is_alias("lh838eb35") && !is_alias("lh838eb35az") && !is_alias("ttkc_man_1") && !is_alias("lhXYZ00000"), "an alias is lh + 8 hex digits only");
    }

    // ---- F: a cart played back from the host's samples
    {
        CartSample s[3];
        s[0].t = 10.0; s[0].p[0] = 0; s[0].a[2] = 3.0f;
        s[1].t = 10.25; s[1].p[0] = 1; s[1].a[2] = -3.0f;   // across +-pi: the short way round
        s[2].t = 10.5; s[2].p[0] = 2; s[2].a[2] = -3.0f;
        float p[3], a[3];
        cart_pose(s, 3, 10.125, p, a);
        WCHECK(std::fabs(p[0] - 0.5f) < 1e-4f, "halfway between two samples: halfway there (%.3f)", p[0]);
        WCHECK(std::fabs(std::fabs(a[2]) - 3.1416f) < 0.01f, "a yaw across +-pi turns the short way (%.3f)", a[2]);
        cart_pose(s, 3, 11.5, p, a);
        WCHECK(std::fabs(p[0] - 4.0f) < 1e-3f, "past the newest: its velocity for 0.5 s at most, then held (%.3f)", p[0]);
        cart_pose(s, 3, 9.0, p, a);
        WCHECK(p[0] == 0.0f, "before the oldest: the oldest");
        float q[4]; const float yaw[3] = { 0, 0, 1.0f };
        quat_xyz(yaw, q);
        WCHECK(std::fabs(q[2] - std::sin(0.5f)) < 1e-5f && std::fabs(q[3] - std::cos(0.5f)) < 1e-5f && q[0] == 0 && q[1] == 0,
               "a yaw alone: the writer's own quaternion (0, 0, sin, cos)");
        uint8_t b[64]{}; size_t o = 0;
        const uint32_t ms = 123456; std::memcpy(b, &ms, 4); o = 4; b[o++] = 1;
        const char* nm = "WHCart[c1]"; b[o++] = static_cast<uint8_t>(std::strlen(nm)); std::memcpy(b + o, nm, std::strlen(nm)); o += std::strlen(nm);
        const float f[6] = { 1, 2, 3, 0, 0, 1 }; std::memcpy(b + o, f, 24); o += 24;
        CartRow rows[4]; uint32_t hm = 0;
        WCHECK(parse_carts(b, o, &hm, rows, 4) == 1 && hm == ms && std::strcmp(rows[0].name, "WHCart[c1]") == 0 && rows[0].p[1] == 2.0f,
               "op 172's body: the host's clock, the cart's name and pose");
        WCHECK(parse_carts(b, o - 1, &hm, rows, 4) == -1 && parse_carts(b, o + 1, &hm, rows, 4) == -1, "a short or a long body: refused");
        b[6] = 'X' - 'X' + 0x20;   // a space in the name
        WCHECK(parse_carts(b, o, &hm, rows, 4) == -1, "a name with a space: refused");
    }

    *passed = g_pass;
    return g_fail;
}
