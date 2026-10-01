// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-140: engine-free checks of the sleep DLL half (native/KCDMP/wo140_rules.h):
// which skips are held, what a C_SkipTime state change means, the thunk frame
// the gate reads the held picker's arguments from, and the clock pull's verdict.
// Linked into KCDMP_NativeTests; wo140_rules_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo140_rules.h"

using namespace kcdmp::wo140rules;

namespace {
int g_fail = 0, g_pass = 0;
#define SCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo140_rules_tests(int* passed) {
    // the player's own skips are held; a quest's, jail's, the bath's, fainting are not
    SCHECK(gated_id(1) && gated_id(2) && gated_id(4), "wait, sleep, read are held");
    SCHECK(!gated_id(0) && !gated_id(5) && !gated_id(6) && !gated_id(7) && !gated_id(14) && !gated_id(57), "undefined, jail, unconscious, bath, fake sleep, the punishment's: never held");
    SCHECK(start_id_ok(2) && start_id_ok(1) && start_id_ok(14) && !start_id_ok(0) && !start_id_ok(5), "op 5 starts the player's own skips and the forced sleep screen only");
    SCHECK(std::strcmp(id_name(2), "sleep") == 0 && std::strcmp(id_name(1), "wait") == 0 && std::strcmp(id_name(4), "read") == 0, "names");

    // the picker's edges (the observed sequences)
    {
        Tracker t;
        SCHECK(t.step(1) == Edge::Opened, "idle -> picker: opened");
        SCHECK(t.step(2) == Edge::Began, "picker -> skipping: began (the asker confirmed the length)");
        SCHECK(t.step(3) == Edge::None, "inside the skip: nothing");
        SCHECK(t.step(1) == Edge::None, "the picker back for its fade-out: nothing");
        SCHECK(t.step(0) == Edge::Ended, "closed after a skip ran: ended (observed 0-1-2-3-1-0)");
    }
    {
        Tracker t;
        SCHECK(t.step(1) == Edge::Opened && t.step(0) == Edge::BackedOut, "picker closed without a skip: backed out");
    }
    {
        Tracker t;
        SCHECK(t.step(2) == Edge::Began && t.step(0) == Edge::Ended, "a forced start (0 -> 2) then idle: began, ended");
        SCHECK(t.step(0) == Edge::None, "no change: nothing");
        SCHECK(t.step(1) == Edge::Opened, "the next picker: opened again");
    }
    SCHECK(std::strcmp(edge_name(Edge::Began), "began") == 0, "edge names");

    // the thunk frame (inline_hook.cpp: 4 pushes, sub rsp,0x68, call cb)
    SCHECK(kTEntry == 4 * 8 + 0x68, "the hooked entry's rsp is 0x88 above the thunk's");
    SCHECK(kTXmm2 == 0x40 && kTXmm3 == 0x50, "xmm2 / xmm3 saved at +0x40 / +0x50");
    SCHECK(kEntryArg5 == 0x28 && kEntryArg6 == 0x30, "the 5th / 6th arguments at entry+0x28 / +0x30 (return + 4 home slots)");

    // the hours
    SCHECK(hours_ok(1.f) && hours_ok(24.f) && hours_ok(0.25f), "1, 24, a quarter: ok");
    SCHECK(!hours_ok(0.f) && !hours_ok(25.f) && !hours_ok(-1.f), "0, 25, negative: refused");
    float nan = 0.f; nan = nan / nan;
    SCHECK(!hours_ok(nan), "NaN refused");
    SCHECK(float_bits(1.f) == 0x3F800000u, "1.0f bits (r8d)");

    // the clock pull: backward only, and only when the calendar reads what the agent read
    SCHECK(pull_verdict(830000000, 830000, 829000) == Pull::Pulled, "ahead by 1000 s: pulled");
    SCHECK(pull_verdict(830000000, 830000, 830000) == Pull::NotNeeded, "equal: not needed");
    SCHECK(pull_verdict(830000000, 830000, 831000) == Pull::NotNeeded, "behind: not needed (forward is Lua's)");
    SCHECK(pull_verdict(830000000, 829000, 828000) == Pull::Pulled, "WO-144: the calendar 1000 s further ahead than the agent read: still pulled");
    SCHECK(pull_verdict(820000000, 829000, 828000) == Pull::Mismatch, "the calendar went 9000 s back past the agent's reading (a load): refused");
    SCHECK(pull_verdict(830000000, 829900, 828000) == Pull::Pulled, "within the 120 s slack: pulled");

    *passed = g_pass;
    return g_fail;
}
