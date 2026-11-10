// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-216: engine-free checks of wo216_rules.h. Linked into KCDMP_NativeTests; wo216_tests() returns the number of failures.
#include <cstdio>
#include <cmath>
#include <cstring>

#include "wo216_rules.h"
#include "wo215_rules.h"

using namespace kcdmp::wo216rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo216_tests(int* passed) {
    // ---- the switch word and the bench's twin names
    int nk = 0;
    const Name* k = switch_names(&nk);
    uint32_t all = 0;
    for (int i = 0; i < nk; ++i) all |= k[i].b;
    WCHECK(all == kBitsDefault && nk == 7, "every WO-216 native switch has a name and ships on (%d names, 0x%X)", nk, all);
    WCHECK(twin_off_bits("route_replay") == kBitRouteReplay && twin_off_bits("no_brains, poses") == (kBitNoBrains | kBitPoses), "the twins by name");
    WCHECK(twin_off_bits("all") == kBitsDefault && twin_off_bits("route") == 0 && twin_off_bits(nullptr) == 0, "all; WO-215's names are not WO-216's");

    // ---- A.0 the record on the wire: 52 bytes after the name, every field back as it went
    {
        RouteRec r;
        r.reqId = 4711; r.speed = 3; r.flags = kRecActive | kRecGoal | kRecExact; r.style = 5;
        r.goal[0] = 1.5f; r.goal[1] = -2.25f; r.goal[2] = 30.0f;
        r.exact[0] = 1.0f; r.exact[1] = 2.0f; r.exact[2] = 3.0f; r.exactYaw = 1.25f; r.tolA = 0.4f; r.tolB = -2.0f; r.seq = 9; r.pathId = 77;
        uint8_t w[kRecWire];
        rec_put(r, w);
        RouteRec b;
        WCHECK(kRecWire == 52 && rec_get(w, &b), "the record is 52 bytes and reads back");
        WCHECK(b.reqId == 4711 && b.speed == 3 && b.flags == r.flags && b.style == 5 && b.seq == 9 && b.pathId == 77 &&
               std::memcmp(b.goal, r.goal, 12) == 0 && std::memcmp(b.exact, r.exact, 12) == 0 && b.exactYaw == 1.25f && b.tolA == 0.4f,
               "every field comes back as it went");
        w[8] = 0xFF; w[9] = 0xFF; w[10] = 0xFF; w[11] = 0x7F;   // goal.x = NaN
        WCHECK(!rec_get(w, &b), "a record with a non-finite goal is refused");
    }
    WCHECK(std::fabs(yaw_of_dir(-std::sin(0.7f), std::cos(0.7f)) - 0.7f) < 1e-5f, "the facing back out of the exact end's direction (yaw 0 faces +Y)");
    WCHECK(known_speed(0) && known_speed(1) && known_speed(7) && known_speed(11) && !known_speed(12) && !known_speed(-1), "the speed enums the engine's tables answer (0..11)");

    // ---- A.0 a change worth a line
    {
        RouteRec a, b;
        a.flags = b.flags = kRecActive | kRecGoal;
        a.reqId = b.reqId = 5;
        WCHECK(!rec_changed(a, b), "the same request is no change");
        b.goal[0] = 0.2f;
        WCHECK(!rec_changed(a, b), "a goal moved 20 cm is no change (25 cm is)");
        b.goal[0] = 0.3f;
        WCHECK(rec_changed(a, b), "a goal moved 30 cm is a change");
        b = a; b.reqId = 6;
        WCHECK(rec_changed(a, b), "another request is a change");
        b = a; b.speed = 3;
        WCHECK(rec_changed(a, b), "another class is a change");
        b = a; b.flags = 0; b.reqId = kNoReq;
        WCHECK(rec_changed(a, b), "the request ended is a change");
        RouteRec i1, i2;
        i2.goal[0] = 9.0f;
        WCHECK(!rec_changed(i1, i2), "two idle lines are the same whatever their stale fields say");
        b = a; b.flags |= kRecExact; a.flags |= kRecExact; b.exactYaw = 0.2f;
        WCHECK(rec_changed(a, b), "the exact end turned 0.2 rad is a change");
    }
    WCHECK(repeat_due(true, 2.1) && !repeat_due(true, 1.5) && !repeat_due(false, 3.0) && repeat_due(false, 6.5),
           "a running request is repeated every 2 s, an idle line every 6 s");

    // ---- A.1 the replay
    {
        RouteRec L;
        L.flags = kRecActive | kRecGoal; L.reqId = 10; L.seq = 3;
        WCHECK(replay_due(false, 0, 0, L), "a copy with no replay of its host's request makes it");
        WCHECK(!replay_due(true, 10, 3, L), "the same request and seq: once per change, never renewed");
        WCHECK(replay_due(true, 10, 2, L) && replay_due(true, 9, 3, L), "re-aimed (a new seq) or a new request: made again");
        RouteRec idle;
        WCHECK(!replay_due(false, 0, 0, idle), "no request on the host: nothing to replay (WO-215's face stands it)");
        RouteRec pre; pre.flags = kRecActive | kRecPrePath; pre.reqId = 4;
        WCHECK(!replay_due(false, 0, 0, pre), "a path given with the request (no goal read): not replayed (WO-215's walk-to-sample)");
    }
    WCHECK(line_live(true, 3.0) && !line_live(true, 3.6) && line_live(false, 7.0) && !line_live(false, 8.5) && !line_live(true, -1.0),
           "a running line lives 3.5 s (the host repeats it every 2 s), an idle one 8 s");
    WCHECK(drift_cause(0.4) == Cause::Stall && drift_cause(1.5) == Cause::Drift && drift_cause(-1.0) == Cause::Drift,
           "a drift within 1 s after a link stall is the stall's");
    WCHECK(no_path(true, true, 3.0f) && !no_path(true, true, 1.0f) && !no_path(false, true, 3.0f) && !no_path(true, false, 3.0f),
           "our request ended 3 m from the goal while the host's runs: no-path (1 m: it arrived first)");
    WCHECK(cancel_on_host_end(true, 2.0f) && !cancel_on_host_end(true, 0.4f) && !cancel_on_host_end(false, 2.0f),
           "the host's request ended: ours cancelled unless it is within 0.6 m of its goal");
    WCHECK(std::strcmp(cause_name(Cause::NoPath), "no-path") == 0 && std::strcmp(cause_name(Cause::Stall), "stall") == 0, "the causes by name");
    WCHECK(parity_bin(0.0f) == 0 && parity_bin(0.25f) == 2 && parity_bin(9.0f) == kParityBins - 1 && parity_bin(-1.0f) == 0, "the parity bins (10 cm)");
    {   // the corrector's way: the host's last 1.2 s
        const float a[2] = {0, 0}, b[2] = {10, 0};
        const float p1[2] = {5, 2}, p2[2] = {-3, 4}, p3[2] = {12, 0};
        WCHECK(std::fabs(seg_dist2d(p1, a, b) - 2.0f) < 1e-4f, "beside a segment: the perpendicular");
        WCHECK(std::fabs(seg_dist2d(p2, a, b) - 5.0f) < 1e-4f && std::fabs(seg_dist2d(p3, a, b) - 2.0f) < 1e-4f, "beyond its ends: the end");
        WCHECK(std::fabs(seg_dist2d(p1, a, a) - std::sqrt(29.0f)) < 1e-4f, "a zero-length segment is its point");
        const float way[4][2] = {{0, 0}, {1.4f, 0}, {2.8f, 0}, {4.2f, 0}};   // a walker's 3 s at 1.4 m/s, the newest last
        const float behind[2] = {1.0f, 0.3f};   // 0.3 m beside the way, about 2.3 s behind the host's point now
        WCHECK(way_off(behind, way, 4) < 0.31f, "a copy behind its host on the host's own way is on its way (b6 pulled it back)");
        const float beside[2] = {3.0f, 2.0f};
        WCHECK(std::fabs(way_off(beside, way, 4) - 2.0f) < 1e-4f, "a copy 2 m beside the way is off it");
        const float one[1][2] = {{1, 1}};
        const float q[2] = {4, 5};
        WCHECK(std::fabs(way_off(q, one, 1) - 5.0f) < 1e-4f && way_off(q, one, 0) == 0.0f, "one point: the distance to it; none: nothing");
    }

    // ---- B
    WCHECK(breach(0) && !breach(1) && !breach(2) && !breach(-1), "a running brain (state 0) is a breach; suspended or unknown are not");
    WCHECK(hide_unbound(false, false) && !hide_unbound(true, false) && !hide_unbound(false, true), "an unbound body is hidden; a copy or a figure is not");
    WCHECK(streamable_name(63) && !streamable_name(65) && !streamable_name(0),
           "a name the stream cannot carry (the animal care's horses, 65) is never hidden: it can never be a copy");

    if (passed) *passed = g_pass;
    return g_fail;
}
