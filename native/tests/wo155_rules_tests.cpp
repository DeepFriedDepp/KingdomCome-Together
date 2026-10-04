// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-155: engine-free checks of the friendly-fire knockdown window (native/KCDMP/wo155_rules.h).
// Linked into KCDMP_NativeTests; wo155_rules_tests() returns the number of failures.
#include <cstdio>

#include "wo155_rules.h"

using namespace kcdmp::wo155rules;

namespace {
int g_fail = 0, g_pass = 0;
#define HCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
constexpr double kNever = -1e9;
} // namespace

int wo155_rules_tests(int* passed) {
    HCHECK(kWindowS == 5.0, "the window is 5 s");

    // the first hit may knock him down (not suppressed); a second 2 s later, after the down edge, is suppressed
    {
        FfWindow w;
        HCHECK(!w.suppress(100.0, false, kNever), "the first hit knocks (not suppressed)");
        HCHECK(w.suppress(102.0, false, 100.3), "a second hit 2 s later, after the down edge: suppressed");
        HCHECK(w.suppress(104.9, false, 100.3), "4.9 s after the knocking hit: still inside the window");
        HCHECK(!w.suppress(105.1, false, 100.3), "5.1 s after: a new knockdown is allowed");
        // and that new knocking hit starts its own window
        HCHECK(w.suppress(107.0, false, 105.4), "the new window works the same");
    }
    // a hit while he is on the ground never knocks him again, whatever the clock says
    {
        FfWindow w;
        HCHECK(w.suppress(10.0, true, kNever), "down now: suppressed");
        HCHECK(!w.suppress(200.0, false, kNever), "no knockdown ever happened: the next hit knocks");
    }
    // a knocking hit that did NOT knock him down (a combat stance) opens no window
    {
        FfWindow w;
        HCHECK(!w.suppress(50.0, false, kNever), "hit 1 in a combat stance (no down edge)");
        HCHECK(!w.suppress(52.0, false, kNever), "hit 2 is not suppressed: hit 1 knocked nothing");
        HCHECK(!w.suppress(54.0, false, 20.0), "a down edge from long before the hit does not arm the window");
    }
    // a down edge more than kSettleS after the hit is not the hit's knockdown
    {
        FfWindow w;
        HCHECK(!w.suppress(30.0, false, kNever), "hit");
        HCHECK(!w.suppress(33.0, false, 31.8), "the edge came 1.8 s after the hit: another cause, no window");
    }
    // the window closes while he is up: 10 s later he can be knocked again, and a long fight alternates
    {
        FfWindow w;
        HCHECK(!w.suppress(0.0, false, kNever), "knock 1");
        HCHECK(w.suppress(1.0, true, 0.3), "down: suppressed");
        HCHECK(w.suppress(4.0, false, 0.3), "up again at 4 s: still inside the window");
        HCHECK(!w.suppress(8.0, false, 0.3), "8 s: window over, knock 2");
        HCHECK(w.suppress(9.0, false, 8.3), "and its window");
    }
    *passed = g_pass;
    return g_fail;
}
