// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-143: engine-free checks of activities part 2 (native/KCDMP/wo143_rules.h):
// hand classes and "in step", the gait plan (only pairs this DLL set are
// cleared), the quieter log of a refused activity, the one-shot throttle, the
// look budget, the host's refresh.
// Linked into KCDMP_NativeTests; wo143_rules_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo143_rules.h"
#include "wo141_rules.h"

using namespace kcdmp::wo143rules;

namespace {
int g_fail = 0, g_pass = 0;
#define HCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

ClassId cls(uint8_t tag) { ClassId c; for (int i = 0; i < 16; ++i) c.b[i] = static_cast<uint8_t>(tag + i); return c; }
} // namespace

int wo143_rules_tests(int* passed) {
    // hands: an all-zero class is an empty hand; the comparison is per hand
    {
        ClassId empty;
        HCHECK(empty.empty() && !cls(1).empty(), "zero = an empty hand");
        Hands a; a.left = cls(0x10);
        Hands b = a;
        HCHECK(a == b && !(a != b), "the same tool in the same hand: in step");
        b.right = cls(0x20);
        HCHECK(a != b, "a tool more in the right hand: not in step");
        Hands swapped; swapped.right = cls(0x10);
        HCHECK(a != swapped, "the hoe in the other hand is not the same state");
        HCHECK(Hands{}.empty() && !a.empty(), "empty hands");
        HCHECK(kSlotLeft == 1 && kSlotRight == 2 && kHandLeft == 1 && kHandRight == 2, "slot [1] left, [2] right; +0x48 hand 1 left, 2 right");
    }

    // gaits: set what the host has and the copy lacks; clear only what this DLL set
    {
        HCHECK(kGaitCount == 9 && kGaitMaskAll == 0x1FF, "nine actorCondition contexts");
        HCHECK(std::strcmp(gait_name(0), "actorCondition_forcedHoeing") == 0 && std::strcmp(gait_name(8), "actorCondition_forcedCrimeWatching_nonViolent") == 0 &&
               std::strcmp(gait_name(9), "?") == 0, "the names");
        GaitPlan p = gait_plan(0x001, 0x000, 0x000);
        HCHECK(p.set == 0x001 && p.clear == 0, "the host hoes, the copy does not: set it");
        p = gait_plan(0x001, 0x001, 0x001);
        HCHECK(p.set == 0 && p.clear == 0, "in step: nothing");
        p = gait_plan(0x000, 0x001, 0x001);
        HCHECK(p.set == 0 && p.clear == 0x001, "the host stopped hoeing: clear what we set");
        p = gait_plan(0x000, 0x004, 0x000);
        HCHECK(p.clear == 0, "a context the copy's own world set is never removed");
        p = gait_plan(0x003, 0x002, 0x002);
        HCHECK(p.set == 0x001 && p.clear == 0, "one more to set, none to clear");
        p = gait_plan(0xFFFF, 0, 0);
        HCHECK(p.set == kGaitMaskAll, "bits past the nine are ignored");
    }

    // Phase 7: a refused activity -- the first three tries, then once a minute with the count
    {
        LogPace lp; int sup = -1;
        HCHECK(log_due(lp, 0.0, &sup) && sup == 0, "try 1 logged");
        HCHECK(log_due(lp, 1.5, &sup), "try 2 logged");
        HCHECK(log_due(lp, 3.0, &sup), "try 3 logged");
        int quiet = 0;
        for (double t = 18.0; t < 62.0; t += 15.0) if (!log_due(lp, t, &sup)) ++quiet;
        HCHECK(quiet == 3, "tries 4..6 (15 s apart) are quiet");
        HCHECK(log_due(lp, 63.0, &sup) && sup == 3, "a minute after the last line: logged, with the 3 quiet tries");
        HCHECK(!log_due(lp, 78.0, &sup), "then quiet again");
        log_reset(lp);
        HCHECK(log_due(lp, 79.0, &sup) && sup == 0, "a success starts over");
        // 45 minutes of 15-s retries: 3 + 45 lines at most, not WO-141's 154+ lines
        LogPace l2; int lines = 0;
        for (double t = 0; t < 45 * 60.0; t += 15.0) if (log_due(l2, t, &sup)) ++lines;
        HCHECK(lines <= 3 + 45 && lines >= 45, "45 minutes of retries: about one line a minute (%d)", lines);
    }

    // one-shots: one per NPC every 1.5 s, eight a second overall
    {
        OneShotBudget b;
        HCHECK(oneshot_allowed(b, -1e9, 10.0), "the first");
        HCHECK(!oneshot_allowed(b, 10.0, 11.0), "the same NPC 1 s later: dropped");
        HCHECK(oneshot_allowed(b, 10.0, 11.6), "1.6 s later: sent");
        OneShotBudget c; int sent = 0;
        for (int i = 0; i < 20; ++i) if (oneshot_allowed(c, -1e9, 20.0 + i * 0.01)) ++sent;
        HCHECK(sent == kOneShotPerSecond, "twenty NPCs in one second: eight go (%d)", sent);
        HCHECK(oneshot_allowed(c, -1e9, 21.5), "the next second: again");
        HCHECK(kOneShotTickLimitS >= 10.0 && kOneShotTickLimitS <= 30.0, "a paused copy is ticked for a bounded time");
    }

    // looks: near players only, a few changes a second
    {
        LookBudget b; int sent = 0;
        for (int i = 0; i < 30; ++i) if (look_allowed(b, 5.0 + i * 0.01)) ++sent;
        HCHECK(sent == kLookPerSecond, "thirty changes at once: %d go", sent);
        HCHECK(look_allowed(b, 6.5), "a second later: again");
        HCHECK(kLookRangeM > 5.0f && kLookRangeM <= 30.0f && kLookPeriodS >= 0.25, "cheap: near, and not every frame");
        HCHECK(kTargetNone == 0 && kTargetHostPlayer == 1 && kTargetPeer == 2 && kTargetNpc == 3, "the target kinds on the wire");
    }

    // the live runs' rules: a hoer creeps at the walking class, the tags see the hoeing pace
    {
        float ox = 0, oy = 0;
        HCHECK(!hoe_tags(0.0f, 0.0f, &ox, &oy) && ox == 0.0f && oy == 0.0f, "standing still: standing");
        HCHECK(!hoe_tags(0.003f, 0.0f, &ox, &oy), "a few millimetres a second (the stream settling): standing");
        HCHECK(hoe_tags(0.072f, 0.035f, &ox, &oy), "H1's creep (0.08 m/s): walking");
        const float v = ox * ox + oy * oy;
        HCHECK(v > 0.159f && v < 0.161f, "the tags see 0.4 m/s (got %.3f^2)", v);
        HCHECK(ox > 0.0f && oy > 0.0f && ox / oy > 2.0f && ox / oy < 2.2f, "along her own row (%.3f, %.3f)", ox, oy);
        HCHECK(hoe_tags(0.9f, 0.0f, &ox, &oy) && ox == 0.9f && oy == 0.0f, "walking faster than the hoeing pace: as streamed");
        HCHECK(hoeing(0x001) && hoeing(0x1FF) && !hoeing(0x002) && !hoeing(0), "only forcedHoeing is the hoer's work");
        HCHECK(kSettleS >= 1.0 && kSettleS <= 5.0 && kHoeSettleS >= 2.0 && kHoeSettleS <= 6.0, "a bounded settle after a take, and after hoeing");
    }

    // the writer's hold (WO-141's reconcile, J1-J3)
    {
        namespace W = kcdmp::wo141rules;
        HCHECK(!W::hold_wanted(false, true, 0, false, W::kSitting), "a body that owns no place is always written");
        HCHECK(W::hold_wanted(true, true, 9, true, W::kSitting), "in step: held");
        HCHECK(W::hold_wanted(true, false, 1, true, 0), "being tried: held");
        HCHECK(!W::hold_wanted(true, false, W::kMissesBeforeBackoff, true, 0), "refused and backed off: written again (no body turned by a look)");
        HCHECK(W::hold_wanted(true, false, W::kMissesBeforeBackoff, false, 0), "the backed-off retry itself: held");
        HCHECK(W::hold_wanted(true, false, 50, true, W::kCart), "a cart stance stays held even when refused (its cart is not streamed)");
    }

    // tools never ride a seated, lying or kneeling body; a refused tool is dropped (WO-141's apply)
    {
        namespace W = kcdmp::wo141rules;
        HCHECK(W::hands_ride(true, false, 0) && W::hands_ride(true, false, W::kStanding), "standing or walking: the tools ride");
        HCHECK(!W::hands_ride(true, false, W::kSitting) && !W::hands_ride(true, false, W::kLying) && !W::hands_ride(true, false, W::kKneel) &&
               !W::hands_ride(true, false, W::kCart), "seated: never (J2: the guest was stood up)");
        HCHECK(!W::hands_ride(true, true, 0) && !W::hands_ride(false, false, 0), "dropped, or none wanted");
        HCHECK(W::kHandsTriesBeforeDrop == 3, "three real refusals");
    }

    // the host's refresh (as WO-141's)
    HCHECK(send_due(true, true, 0) && !send_due(false, true, 1e6), "a change now; empty hands never repeated");
    HCHECK(!send_due(false, false, 9.9) && send_due(false, false, 10.0), "a held tool again every 10 s");

    *passed = g_pass;
    return g_fail;
}
