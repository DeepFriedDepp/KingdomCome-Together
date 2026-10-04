// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-155: the engine-free rule of the friendly-fire knockdown window (hits.cpp). native/tests/wo155_rules_tests.cpp
// pins it.
//
// A friendly-fire hit reaches the victim as the game's scripted hit (TakeDamage with two arguments), which knocks
// a player down whenever he is not in a combat stance (WO-151 1.1). It keeps doing so, as in 0.45.0 -- but a
// stun-lock is no fun: while he is on the ground, and for kWindowS after the hit that knocked him down, the next
// hits deal their damage through the four-argument call with SuppressHitReaction (health and stamina exact, no
// second fall).

namespace kcdmp::wo155rules {

constexpr double kWindowS = 5.0;       // after a knocking hit, no new knockdown from friendly fire
constexpr double kSettleS = 1.5;       // a knockdown shows as a down edge this soon after the hit or not at all

struct FfWindow {
    double lastKnockAt = -1e9;         // the last friendly-fire hit let through to knock him down
    bool   armed = false;              // a down edge was seen for that hit: the window is real

    // One friendly-fire hit at `now`. `downNow` = his body is down right now; `lastDownEdgeAt` = when the last
    // down edge (up -> down) was seen (-1e9 = never). True = the hit goes in suppressed (no knockdown).
    bool suppress(double now, bool downNow, double lastDownEdgeAt) {
        if (downNow) return true;                                   // already on the ground: never a second fall
        // did the previous knocking hit really knock him down? (a down edge after it, within kSettleS)
        if (!armed && lastDownEdgeAt > -1e8 && lastDownEdgeAt >= lastKnockAt && lastDownEdgeAt - lastKnockAt <= kSettleS) armed = true;
        if (armed && now - lastKnockAt < kWindowS) return true;    // inside the window of a knockdown
        // this hit may knock him down: it starts a new window (armed only once a down edge confirms it)
        lastKnockAt = now;
        armed = false;
        return false;
    }
};

} // namespace kcdmp::wo155rules
