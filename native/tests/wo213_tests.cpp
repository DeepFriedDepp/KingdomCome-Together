// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-213: engine-free checks of wo213_rules.h. Linked into KCDMP_NativeTests; wo213_tests() returns the number of failures.
#include <cstdio>
#include <cmath>

#include "wo213_rules.h"

using namespace kcdmp::wo213rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
bool near_(double a, double b) { return std::fabs(a - b) < 1e-9; }
} // namespace

int wo213_tests(int* passed) {
    // ---- the switch word and the bench's twin names
    WCHECK((kBitsDefault & kBitNpcBands) && (kBitsDefault & kBitPlayoutAdaptive) && (kBitsDefault & kBitPostureHoldLate) && (kBitsDefault & kBitPostureNoPos)
           && (kBitsDefault & kBitFigurePlayout) && (kBitsDefault & kBitFigureBand), "every WO-213 native switch ships on");
    WCHECK(twin_off_bits("npc_bands") == kBitNpcBands && twin_off_bits("x, playout_adaptive,posture_no_pos") == (kBitPlayoutAdaptive | kBitPostureNoPos), "the twins");
    WCHECK(twin_off_bits("all") == kBitsDefault && twin_off_bits("link_sched") == 0 && twin_off_bits(nullptr) == 0, "all, and the agent's own names are not native");

    WCHECK(twin_off_bits("figure_band") == kBitFigureBand, "D.4's twin");

    // ---- D.4 the local player's state, read each frame while the agent asks, answered at once while fresh
    WCHECK(local_wanted(10.0, 9.5) && !local_wanted(10.0, 8.9) && !local_wanted(10.0, 0), "read each frame only while the agent asked within 1 s");
    WCHECK(local_fresh(10.0, 9.9) && !local_fresh(10.0, 9.7) && !local_fresh(10.0, 0) && !local_fresh(10.0, 10.1),
           "answered from a read of the last 250 ms; older (a menu, a load) or none: the main-thread path");

    // ---- D.2 the bands
    Bands b{};
    WCHECK(band(10, true, b) == 0 && band(10, false, b) == 1 && band(16, true, b) == 1, "fighting within 15 m is the top band; drawn at 16 m is 40 m's");
    WCHECK(band(40, false, b) == 1 && band(41, false, b) == 2 && band(150, false, b) == 2 && band(151, false, b) == 3, "40 m, 150 m, beyond");
    WCHECK(near_(band_period_s(0, b), 0.033) && near_(band_period_s(1, b), 0.050) && near_(band_period_s(2, b), 0.100) && near_(band_period_s(3, b), 0.500), "30, 20, 10, 2 Hz");
    WCHECK(near_(tick_s(b), 0.033), "the sender ticks at the fastest band");
    WCHECK(band_due(0.033, 0, b) && !band_due(0.020, 0, b) && band_due(0.1, 2, b) && !band_due(0.3, 3, b), "an NPC is due once its band's period passed");
    {   // the fight band on 22 ms frames (45 fps) for 3 s: its own cadence gives 30 Hz; the twin (the next one a period after the send) 22.5
        auto rate = [&](bool slots) {
            double slot = 0; int sends = 0;
            for (int f = 1; f * 0.022 <= 3.0; ++f) {
                const double now = f * 0.022;
                if (!band_due(now - slot, 0, b)) continue;
                slot = slots ? band_slot(slot, now, band_period_s(0, b)) : now;
                ++sends;
            }
            return sends / 3.0;
        };
        WCHECK(rate(true) > 28.0 && rate(true) < 31.0, "the fight band at 45 fps keeps 30 Hz (%.1f)", rate(true));
        WCHECK(rate(false) < 23.0, "the twin: a period after each send, every second frame (%.1f)", rate(false));
        WCHECK(near_(band_slot(1.0, 1.5, 0.033), 1.5), "a send far behind its slot starts the cadence again");
    }

    // ---- E.1 the play-out delay
    double g[8] = {0.10, 0.10, 0.11, 0.09, 0.10, 0.12, 0.10, 0.30};
    WCHECK(gap_p90(g, 3) < 0, "too few gaps: not known");
    WCHECK(near_(gap_p90(g, 8), 0.12), "the p90 of a 10 Hz stream with one late gap is its jitter, not the outlier");
    WCHECK(near_(playout_target(0.12, 0.12), 0.12 + kPlayoutMarginS), "p90 plus the margin");
    WCHECK(near_(playout_target(0.01, 0.12), kPlayoutMinS) && near_(playout_target(0.9, 0.12), kPlayoutMaxS), "bounded 50..300 ms");
    WCHECK(near_(playout_target(-1, 0.12), 0.12), "not known yet: the bind's delay");
    WCHECK(near_(playout_step(0.12, 0.30, 0.1), 0.13) && near_(playout_step(0.12, 0.121, 0.1), 0.121), "slewed at a tenth of real time, never a step");
    WCHECK(near_(extrap_max_s(true, 2.0), kExtrapCapS) && near_(extrap_max_s(true, 0.1), 0.1) && near_(extrap_max_s(false, 2.0), 2.0), "150 ms of extrapolation at most (the twin: the old 2 s)");

    // ---- E.2 late against missing
    WCHECK(lateness(0.5) == Late::Fresh && lateness(kStateLateS) == Late::Fresh, "within the host's resend the state is fresh");
    WCHECK(lateness(kStateLateS + 0.1) == Late::Late && lateness(kStateLateS + kLateHoldS) == Late::Late, "past it: late, held");
    WCHECK(lateness(kStateLateS + kLateHoldS + 0.1) == Late::AskAgain, "after the hold: asked for again");

    // ---- E.3 a holding posture
    WCHECK(posture_holds(true, true, true) && !posture_holds(true, false, true) && !posture_holds(false, true, true) && !posture_holds(true, true, false), "both in a posture, and on");
    // ... while the body stays in place: the talk's "sitting" copy 24 m off its seat took the stream again within one frame (the twin, the
    // posture alone, held it there), and kept it for the rest of that sitting (the twin, a hold back after 2 s of writes: the village's
    // copy yanked by the owner's installs was written, yanked, written -- a flicker)
    {
        PostureSitting st;
        WCHECK(posture_hold_sitting(true, true, 0.10f, 0.20f, 10.0, &st) && !st.drifted, "in place (0.22 m): held, no write");
        WCHECK(!posture_hold_sitting(true, true, 0.0f, 0.31f, 10.1, &st) && st.drifted, "0.31 m off: written");
        WCHECK(!posture_hold_sitting(true, true, 0.0f, 0.0f, 12.5, &st), "back in place 2.4 s later: still written, the rest of that sitting");
        WCHECK(!posture_hold_sitting(false, true, 0.0f, 0.0f, 13.0, &st) && st.drifted, "the posture stops holding: written, still that sitting");
        WCHECK(!posture_hold_sitting(true, true, 0.0f, 0.0f, 13.5, &st) && st.drifted, "holding again within 2 s (the owner's withdraw and install): the same sitting");
        WCHECK(!posture_hold_sitting(false, true, 0.0f, 0.0f, 14.0, &st) && !posture_hold_sitting(false, true, 0.0f, 0.0f, 16.1, &st) && !st.drifted,
               "not holding for 2 s: that sitting is over");
        WCHECK(posture_hold_sitting(true, true, 0.0f, 0.0f, 16.2, &st), "the next sitting, in place: held");
        WCHECK(!posture_hold_sitting(true, true, 24.0f, 0.0f, 17.0, &st) && posture_holds(true, true, true), "the twin: the posture alone holds a body 24 m off");
        PostureSitting none;
        WCHECK(posture_hold_sitting(true, false, 24.0f, 0.0f, 17.0, &none) && !none.drifted, "no position read: the hold stays");
    }

    *passed = g_pass;
    return g_fail;
}
