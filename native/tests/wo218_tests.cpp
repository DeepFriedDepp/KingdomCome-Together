// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-218: engine-free checks of wo218_rules.h (the pipe's batch). Linked into KCDMP_NativeTests; wo218_tests() returns the failures.
#include <cstdio>
#include "wo218_rules.h"

namespace {
int g_fail = 0, g_pass = 0;
#define CHECK(c) do { if (c) ++g_pass; else { std::printf("FAIL  %s:%d  %s\n", __FILE__, __LINE__, #c); ++g_fail; } } while (0)
using namespace kcdmp::wo218rules;
}

int wo218_tests(int* passed) {
    // a batch of two: [id 0x0102][n 2] (0x2B len 2 [133 7]) (0x27 len 1 [3])
    const uint8_t b[] = {0x02, 0x01, 2, 0x2B, 2, 0, 133, 7, 0x27, 1, 0, 3};
    uint16_t id = 0;
    std::vector<Sub> subs;
    CHECK(parse_batch(b, sizeof b, &id, &subs));
    CHECK(id == 0x0102);
    CHECK(subs.size() == 2);
    CHECK(subs[0].type == 0x2B && subs[0].len == 2 && b[subs[0].off] == 133 && b[subs[0].off + 1] == 7);
    CHECK(subs[1].type == 0x27 && subs[1].len == 1 && b[subs[1].off] == 3);
    // malformed: short, a length past the end, trailing bytes, an empty sub
    CHECK(!parse_batch(b, 2, &id, &subs));
    const uint8_t past[] = {0, 0, 1, 0x2B, 5, 0, 1};
    CHECK(!parse_batch(past, sizeof past, &id, &subs));
    const uint8_t trail[] = {0, 0, 1, 0x2B, 1, 0, 1, 9};
    CHECK(!parse_batch(trail, sizeof trail, &id, &subs));
    const uint8_t empty[] = {0, 0, 1, 0x2B, 0, 0};
    CHECK(!parse_batch(empty, sizeof empty, &id, &subs));
    const uint8_t none[] = {5, 0, 0};
    CHECK(parse_batch(none, sizeof none, &id, &subs) && subs.empty() && id == 5);
    // the generic families and their reply types
    CHECK(batchable(0x21) && batchable(0x2B) && !batchable(0x2C) && !batchable(0x20) && !batchable(0x10) && !batchable(kBatch));
    CHECK(reply_type(0x2B) == 0xAC && reply_type(0x27) == 0xA6 && reply_type(0x21) == 0x98 && reply_type(0x10) == 0);
    // the reply: [1][id][n] then (type, len, body); past the cap a sub keeps only its first four bytes
    Reply r;
    r.begin(0x0102, 2);
    const uint8_t s1[] = {1, 0, 133, 0, 9};
    r.add(0xAC, s1, sizeof s1);
    CHECK(r.b.size() == 4 + 3 + 5 && r.b[0] == 1 && r.b[1] == 2 && r.b[2] == 1 && r.b[3] == 2);
    CHECK(r.b[4] == 0xAC && r.b[5] == 5 && r.b[6] == 0 && r.b[11] == 9);
    std::vector<uint8_t> big(1000, 7);
    Reply c;
    c.begin(1, 255);
    for (int i = 0; i < 70; ++i) c.add(0xAC, big.data(), big.size());
    CHECK(c.b.size() <= kReplyCap && c.cut > 0 && c.subs == 70);
    // the frame's budget: one always runs, then while under 2 ms
    CHECK(may_run(0, 9999.0) && may_run(3, 1999.0) && !may_run(1, 2000.0));
    // what one frame served
    FrameCount f;
    f.add(10, 1); f.add(10, 2); f.add(11, 1); f.add(13, 5);
    CHECK(f.total == 9 && f.frames == 2 && f.maxPerFrame == 3 && f.cur == 5);
    uint8_t fb[4];
    CHECK(failure(fb, 133, kReasonTaskFaulted) == 4 && fb[0] == 0 && fb[2] == 133 && fb[3] == 17);
    // C.3: only the activity families, only the copy's own idle / variation / callback / transition
    CHECK(takeover_family(4) && takeover_family(5) && takeover_family(7) && !takeover_family(1) && !takeover_family(3) && !takeover_family(9));
    CHECK(takeover_class("CAnimActionAIMovement") && takeover_class("C_VariationAction") && takeover_class("C_CallbackAction"));
    CHECK(!takeover_class("C_CombatAnimAction") && !takeover_class("CAnimActionLooking") && !takeover_class(nullptr) && !takeover_class("C_Variation"));
    // C.1: a drift block lasts 60 s, doubled per strike, at most 8 minutes
    CHECK(drift_block_s(0) == 60.0 && drift_block_s(1) == 120.0 && drift_block_s(3) == 480.0 && drift_block_s(9) == 480.0 && drift_block_s(-2) == 60.0);
    // C.1: two writers -- ten far moves within 2 s fire once; nine do not; a gap over 2 s starts the count again
    {
        ForeignMeter m;
        bool fired = false;
        for (int i = 0; i < 9; ++i) fired |= m.add(100.0 + i * 0.04);
        CHECK(!fired);
        CHECK(m.add(100.4));          // the tenth within the window
        CHECK(!m.add(100.44));        // once, not again on the eleventh
        ForeignMeter g;
        for (int i = 0; i < 9; ++i) g.add(200.0 + i * 0.1);
        CHECK(!g.add(203.0));         // past the window: the count starts at one again
        int n = 0;
        for (int i = 1; i < 10; ++i) n += g.add(203.0 + i * 0.05) ? 1 : 0;
        CHECK(n == 1);
    }
    *passed = g_pass;
    return g_fail;
}
