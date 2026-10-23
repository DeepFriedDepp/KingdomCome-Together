// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-204: engine-free checks of the native half's rules (wo204_rules.h). Linked into KCDMP_NativeTests.
#include <cmath>
#include <cstdio>
#include <cstring>
#include <limits>
#include <utility>

#include "wo204_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

// C_DistanceTrigger::Evaluate as XGenAIModule 1.5.5 ships it (RVA 0x12271C0), displacements included.
const uint8_t kRealEvaluate[] = {
    0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0xF9, 0x0F, 0x29, 0x74,
    0x24, 0x30, 0x48, 0x8D, 0x0D, 0x7E, 0xEA, 0xE7, 0x01, 0xE8, 0xB2, 0xFF, 0xE3, 0x00, 0x48, 0x8B,
    0x8F, 0x68, 0x02, 0x00, 0x00, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x50, 0x48, 0x8B, 0x8F, 0x70, 0x02,
    0x00, 0x00, 0xF2, 0x0F, 0x10, 0x30, 0x8B, 0x58, 0x08, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x50, 0x0F,
    0x28, 0xDE, 0x0F, 0x28, 0xD6, 0x0F, 0xC6, 0xDB, 0x55, 0xF2, 0x0F, 0x10, 0x08, 0x0F, 0x28, 0xC1,
    0xF3, 0x0F, 0x5C, 0xD1, 0x0F, 0xC6, 0xC0, 0x55, 0xF3, 0x0F, 0x5C, 0xD8, 0x66, 0x0F, 0x6E, 0xCB,
    0xF3, 0x0F, 0x5C, 0x48, 0x08, 0x0F, 0x57, 0xC0, 0xF3, 0x0F, 0x59, 0xD2, 0xF3, 0x0F, 0x59, 0xDB,
    0xF3, 0x0F, 0x59, 0xC9, 0xF3, 0x0F, 0x58, 0xDA, 0xF3, 0x0F, 0x58, 0xD9, 0x0F, 0x2E, 0xC3, 0x77,
    0x09, 0x0F, 0x57, 0xC9, 0xF3, 0x0F, 0x51, 0xCB, 0xEB, 0x0B, 0x0F, 0x28, 0xC3, 0xE8, 0x48, 0x00,
    0xE4, 0x00, 0x0F, 0x28, 0xC8, 0x48, 0x63, 0x87, 0x78, 0x02, 0x00, 0x00, 0x83, 0xF8, 0x05, 0x77,
    0x6C, 0x48, 0x8D, 0x15, 0x98, 0x8D, 0xDD, 0xFE, 0x8B, 0x8C, 0x82, 0xE0, 0x72, 0x22, 0x01, 0x48,
    0x03, 0xCA, 0xFF, 0xE1, 0x0F, 0x2E, 0x8F, 0x7C, 0x02, 0x00, 0x00, 0x7A, 0x11, 0x75, 0x0F, 0xB2,
    0x01, 0xEB, 0x42, 0x0F, 0x2E, 0x8F, 0x7C, 0x02, 0x00, 0x00, 0x7A, 0xF3, 0x75, 0xF1, 0x33, 0xD2,
    0xEB, 0x33, 0x0F, 0x2F, 0x8F, 0x7C, 0x02, 0x00, 0x00, 0x0F, 0x97, 0xC2, 0xEB, 0x27, 0x0F, 0x2F,
    0x8F, 0x7C, 0x02, 0x00, 0x00, 0xEB, 0x1B, 0xF3, 0x0F, 0x10, 0x87, 0x7C, 0x02, 0x00, 0x00, 0x0F,
    0x2F, 0xC1, 0x0F, 0x97, 0xC2, 0xEB, 0x0E, 0xF3, 0x0F, 0x10, 0x87, 0x7C, 0x02, 0x00, 0x00, 0x0F,
    0x2F, 0xC1, 0x0F, 0x93, 0xC2, 0x48, 0x8B, 0xCF, 0xE8, 0x33, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x5C,
    0x24, 0x50, 0x0F, 0x28, 0x74, 0x24, 0x30, 0x48, 0x83, 0xC4, 0x40, 0x5F, 0xC3
};
const int32_t kRealTable[6] = {19034740, 19034755, 19034770, 19034782, 19034791, 19034807};
constexpr int64_t kRealEvalRva = 0x12271C0, kRealSetResultRva = 0x1227300;
} // namespace

int wo204_tests(int* passed) {
    using namespace wo204rules;
    // ---- Act I: Evaluate's shape, read out of the shipped body ----
    static_assert(sizeof kRealEvaluate == sizeof kEvalBody, "the shipped body has the template's length");
    const EvalShape s = evaluate_shape(kRealEvaluate, sizeof kRealEvaluate);
    WCHECK(s.ok, "the shipped Evaluate has the shape");
    WCHECK(s.origin == 0x268 && s.target == 0x270, "origin +0x268, target +0x270 (the crash dumps' fields): %X %X", s.origin, s.target);
    WCHECK(s.kind == 0x278 && s.threshold == 0x27C, "kind +0x278, threshold +0x27C: %X %X", s.kind, s.threshold);
    WCHECK(s.baseRel == -kRealEvalRva, "the jump table's lea gives the module base: %lld", static_cast<long long>(s.baseRel));
    WCHECK(s.tableRva == 0x12272E0, "the jump table at RVA 0x12272E0: %X", s.tableRva);
    WCHECK(s.setResultAt == kRealSetResultRva - kRealEvalRva, "SetResult = Evaluate+0x140: %llX", static_cast<long long>(s.setResultAt));
    WCHECK(table_matches(kRealTable, s.baseRel), "the six table entries land on the six compares");
    int32_t bad[6]; std::memcpy(bad, kRealTable, sizeof bad); std::swap(bad[2], bad[4]);
    WCHECK(!table_matches(bad, s.baseRel), "a table whose compares moved is refused");
    uint8_t b[sizeof kRealEvaluate]; std::memcpy(b, kRealEvaluate, sizeof b);
    b[0xC1] ^= 0x01;   // jmp target inside the compare cases
    WCHECK(!evaluate_shape(b, sizeof b).ok, "a changed compare byte is refused (fail closed)");
    std::memcpy(b, kRealEvaluate, sizeof b);
    b[kAtThreshold[3]] = 0x80;   // one compare reads another threshold field
    WCHECK(!evaluate_shape(b, sizeof b).ok, "compares reading different thresholds are refused");
    std::memcpy(b, kRealEvaluate, sizeof b);
    b[0x15] ^= 0xFF; b[0x1A] ^= 0xFF; b[0x109] ^= 0x10;   // the profiler marker, its call, SetResult's call: masked
    WCHECK(evaluate_shape(b, sizeof b).ok && evaluate_shape(b, sizeof b).setResultAt != s.setResultAt, "displacements are masked and re-read");
    WCHECK(!evaluate_shape(kRealEvaluate, sizeof kRealEvaluate - 1).ok && !evaluate_shape(nullptr, 400).ok, "a short or missing body is refused");

    // ---- the engine's six compares ----
    bool m = false;
    WCHECK(trigger_result(0, 5.f, 5.f, &m) && m && trigger_result(0, 5.f, 6.f, &m) && !m, "0 equal");
    WCHECK(trigger_result(1, 5.f, 6.f, &m) && m && trigger_result(1, 5.f, 5.f, &m) && !m, "1 not equal");
    WCHECK(trigger_result(2, 7.f, 6.f, &m) && m && trigger_result(2, 6.f, 6.f, &m) && !m, "2 greater");
    WCHECK(trigger_result(3, 6.f, 6.f, &m) && m && trigger_result(3, 5.f, 6.f, &m) && !m, "3 greater or equal");
    WCHECK(trigger_result(4, 5.f, 6.f, &m) && m && trigger_result(4, 6.f, 6.f, &m) && !m, "4 less (the escort's 'near')");
    WCHECK(trigger_result(5, 6.f, 6.f, &m) && m && trigger_result(5, 7.f, 6.f, &m) && !m, "5 less or equal");
    const float nan = std::numeric_limits<float>::quiet_NaN();
    bool all = true;
    for (int k : {0, 2, 3, 4, 5}) { all = all && trigger_result(k, nan, 6.f, &m) && !m; }
    WCHECK(all && trigger_result(1, nan, 6.f, &m) && m, "an unordered distance: false, except 'not equal' (ucomiss/comiss flags)");
    WCHECK(!trigger_result(6, 1.f, 2.f, &m) && !trigger_result(-1, 1.f, 2.f, &m), "an unknown kind sets nothing (the engine's ja past the table)");
    const float a[3] = {0, 0, 0}, c[3] = {3, 4, 12};
    WCHECK(std::fabs(distance3(a, c) - 13.f) < 1e-5f, "the distance");

    // ---- I.3: quiet windows ----
    WCHECK(skip_quiet(1) && skip_quiet(2) && skip_quiet(3) && !skip_quiet(0), "the picker and the skip are quiet, idle is not");
    WCHECK(near_quiet(1000, 2000, false) && !near_quiet(2000, 2000, false) && near_quiet(5000, 0, true), "until the window ends, or while the agent says so");
    WCHECK(snapshot_fresh(1500, 1000) && !snapshot_fresh(1501, 1000) && !snapshot_fresh(1000, 0) && !snapshot_fresh(900, 1000),
           "the figure's snapshot is used only while fresh (500 ms)");
    WCHECK(kQuietAfterMs == 5000, "5 s after a time skip ends");

    // ---- II.3: a seated copy is the seat's ----
    WCHECK(seat_hold(true, false, true) && seat_hold(false, true, true) && !seat_hold(false, false, true), "held when the host wants it or the copy sits");
    WCHECK(!seat_hold(false, true, false) && seat_hold(true, true, false), "the switch off: WO-141's own hold only");
    WCHECK(kBitsDefault == (kBitSeatHold | kBitTposeWatch), "every WO-204 bit on by default");
    // ---- II.2: the T-pose detector ----
    WCHECK(!tpose_due(0.25) && !tpose_due(0.3) && tpose_due(0.31), "an empty full body longer than 0.3 s is a T-pose");

    // ---- B7's exception: tutorials ----
    WCHECK(is_tutorial_path(true, "Barbora.open_world.tutorialy.tutorial_ob_t18__encumbered.displaytutorial3"), "a tutorial view is the player's own");
    WCHECK(!is_tutorial_path(true, "Barbora.trosecko.zebrackaBarbora.hibernable.FocusCamWithinDistance.focuscamera8"), "a story's focus camera is not");
    WCHECK(!is_tutorial_path(false, "Barbora.open_world.tutorialy.x.y") && !is_tutorial_path(true, nullptr), "only a view, never a null path");

    *passed = g_pass;
    return g_fail;
}
