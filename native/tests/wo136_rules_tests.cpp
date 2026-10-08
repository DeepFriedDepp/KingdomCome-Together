// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-136 Phase 4: engine-free checks of the fight rules (native/KCDMP/wo136_rules.h):
// who an NPC fighting the host or a partner's avatar turns to, and who takes
// over the host's fights at his death. Linked into KCDMP_NativeTests;
// wo136_rules_tests() returns the number of failures.
#include <cstdio>

#include "wo136_rules.h"

using namespace kcdmp::wo136rules;

namespace {
int g_fail = 0, g_pass = 0;
#define RCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
constexpr uint32_t kAvatar = 0x1234, kAvatar2 = 0x5678;
} // namespace

int wo136_rules_tests(int* passed) {
    // An NPC fighting the host; the partner hits it once: stays on the host (sticky).
    {
        Ev e[] = { {kAvatar, 10.0, 2} };
        auto p = pick(e, 1, 10.1, kPlayer, -1e9);
        RCHECK(p.to == kNone, "one avatar hit alone does not turn a fighter from the host (margin %d)", kMargin);
    }
    // The partner keeps hurting it while the host does nothing: it turns to the avatar.
    {
        Ev e[] = { {kAvatar, 10.0, 2}, {kAvatar, 11.0, 2} };
        auto p = pick(e, 2, 11.1, kPlayer, -1e9);
        RCHECK(p.to == kAvatar && p.toScore == 4 && p.curScore == 0, "two avatar hits vs none from the host: it turns to the avatar (to=0x%X)", p.to);
    }
    // Both hit it the same: it keeps its opponent.
    {
        Ev e[] = { {kAvatar, 10.0, 2}, {kPlayer, 10.5, 2}, {kAvatar, 11.0, 2}, {kPlayer, 11.5, 2} };
        auto p = pick(e, 4, 11.6, kPlayer, -1e9);
        RCHECK(p.to == kNone, "equal threat: no switch (ganging up does not make it flip-flop)");
    }
    // It fights the avatar; the host now hurts it more: it turns back to the host.
    {
        Ev e[] = { {kAvatar, 10.0, 1}, {kPlayer, 11.0, 2}, {kPlayer, 11.5, 2} };
        auto p = pick(e, 3, 11.6, kAvatar, -1e9);
        RCHECK(p.to == kPlayer, "the host hurts it more than the avatar it fights: it turns to the host");
    }
    // The hold: no second switch within kSwitchHoldS.
    {
        Ev e[] = { {kAvatar, 10.0, 2}, {kAvatar, 11.0, 2} };
        auto p = pick(e, 2, 11.1, kPlayer, 9.0);
        RCHECK(p.to == kNone, "a switch 2.1 s ago holds (hold %.0f s)", kSwitchHoldS);
        auto q = pick(e, 2, 12.5, kPlayer, 9.0);
        RCHECK(q.to == kAvatar, "3.5 s after the last switch it may turn again");
    }
    // Old threat is forgotten.
    {
        Ev e[] = { {kAvatar, 1.0, 2}, {kAvatar, 2.0, 2} };
        auto p = pick(e, 2, 10.0, kPlayer, -1e9);
        RCHECK(p.to == kNone, "hits older than the %.0f s window count for nothing", kWindowS);
    }
    // No opponent yet: the first threat decides.
    {
        Ev e[] = { {kAvatar, 10.0, 1} };
        auto p = pick(e, 1, 10.1, kNone, -1e9);
        RCHECK(p.to == kAvatar, "an NPC fighting nobody turns to its first attacker (a swing is enough)");
    }
    // Two avatars: the one hurting it most.
    {
        Ev e[] = { {kAvatar, 10.0, 2}, {kAvatar2, 10.2, 2}, {kAvatar2, 10.4, 2}, {kAvatar2, 10.6, 2} };
        auto p = pick(e, 4, 10.7, kPlayer, -1e9);
        RCHECK(p.to == kAvatar2 && p.toScore == 6, "two partners: it turns to the one hurting it most");
    }
    // A future-stamped event (clock step) is ignored.
    {
        Ev e[] = { {kAvatar, 50.0, 2}, {kAvatar, 50.0, 2} };
        auto p = pick(e, 2, 10.0, kPlayer, -1e9);
        RCHECK(p.to == kNone, "events stamped in the future count for nothing");
    }
    // The host's death: the nearest avatar within reach takes over; none beyond it.
    {
        float d1[] = { 30.0f, 8.5f, 12.0f };
        RCHECK(handover_pick(d1, 3, 25.0f) == 1, "the nearest avatar within 25 m takes over the NPC");
        float d2[] = { 30.0f, 26.0f };
        RCHECK(handover_pick(d2, 2, 25.0f) == -1, "no avatar within 25 m: nobody takes over (the engine's own end)");
        float d3[] = { -1.0f, 4.0f };
        RCHECK(handover_pick(d3, 2, 25.0f) == 1, "an avatar whose position is unknown is skipped");
        RCHECK(handover_pick(d3, 0, 25.0f) == -1, "solo: no avatar at all");
    }
    // ---- WO-163 A6 (WO-162 Q5): sticky by damage, not by count -------------------------------------------------------
    // An NPC fighting the host; the avatar lands two measured hits. Count rule alone (4 >= 0 + 3) would turn it; the damage must also
    // exceed the host's by the margin.
    {
        // the count rule passes (3 hits = 6 against 1 hit = 2, margin 3) but the damage does not: many weak hits do not turn it
        Ev e[] = { {kAvatar, 10.0, 2, 3.0f}, {kAvatar, 10.5, 2, 3.0f}, {kAvatar, 11.0, 2, 3.0f}, {kPlayer, 10.2, 2, 9.0f} };   // avatar 9 hp over 3 hits, host 9 hp in one
        auto p = pick(e, 4, 11.1, kPlayer, -1e9);
        RCHECK(p.to == kNone, "9 hp is not 25 %% more than 9 hp: it keeps the host though the avatar landed three times (margin %.0f %%)", kDamageMargin * 100.0f);
    }
    {
        // 3 x 3.7 = 11.1 hp against 9 x 1.25 = 11.25: not over the margin; 3 x 4.0 = 12 hp: over it
        Ev e[] = { {kAvatar, 10.0, 2, 3.7f}, {kAvatar, 10.5, 2, 3.7f}, {kAvatar, 11.0, 2, 3.7f}, {kPlayer, 10.2, 2, 9.0f} };
        RCHECK(pick(e, 4, 11.1, kPlayer, -1e9).to == kNone, "11.1 hp vs 9 hp x 1.25 = 11.25: not over the margin");
        e[0].d = e[1].d = e[2].d = 4.0f;
        auto p = pick(e, 4, 11.1, kPlayer, -1e9);
        RCHECK(p.to == kAvatar && p.toDamage > p.curDamage * 1.25f, "12 hp vs 9 hp: over the margin, it turns (%.1f/%.1f)", p.toDamage, p.curDamage);
        // and the count rule still holds on top: two hits of 20 hp against one of 9 is a score of 4 against 2, under the margin of 3
        Ev c[] = { {kAvatar, 10.0, 2, 20.0f}, {kAvatar, 10.5, 2, 20.0f}, {kPlayer, 10.2, 2, 9.0f} };
        RCHECK(pick(c, 3, 11.0, kPlayer, -1e9).to == kNone, "the count rule (a clear lead of more than one hit) still applies");
    }
    {
        // blocked blows (0 damage) never turn an NPC, however many
        Ev e[] = { {kAvatar, 10.0, 2, 0.0f}, {kAvatar, 10.5, 2, 0.0f}, {kAvatar, 11.0, 2, 0.0f}, {kAvatar, 11.5, 2, 0.0f} };
        RCHECK(pick(e, 4, 11.6, kPlayer, -1e9).to == kNone, "four blocked blows (score 8, damage 0): a count is not a threat");
        // swings alone do not turn it either (they carry no damage)
        Ev s[] = { {kAvatar, 10.0, 1}, {kAvatar, 10.5, 1}, {kAvatar, 11.0, 1}, {kAvatar, 11.5, 1} };
        RCHECK(pick(s, 4, 11.6, kPlayer, -1e9).to == kNone, "four swings: no damage, no turn");
    }
    {
        // unmeasured landed hits count the nominal damage (the host's blows: marked, not measured), so the old two-hit turn still holds
        Ev e[] = { {kAvatar, 10.0, 2}, {kAvatar, 11.0, 2} };
        auto p = pick(e, 2, 11.1, kPlayer, -1e9);
        RCHECK(p.to == kAvatar && p.toDamage == 2.0f * kNominalHitDamage, "two unmeasured landed hits = 2 x the nominal %.0f hp", kNominalHitDamage);
        // a measured hit outweighs the same count of unmeasured ones
        Ev m[] = { {kAvatar, 10.0, 2, 3.0f}, {kPlayer, 10.2, 2}, {kPlayer, 10.5, 2}, {kPlayer, 10.8, 2} };   // avatar 3 hp measured; host 3 x 10 nominal
        RCHECK(pick(m, 4, 11.1, kAvatar, -1e9).to == kPlayer, "the host's three (nominal) hits beat the avatar's 3 measured hp: the NPC turns to the host");
    }
    {
        // the hold still applies on top of the damage rule; and nobody to be sticky to: the first threat decides, damage or not
        Ev e[] = { {kAvatar, 10.0, 2, 30.0f}, {kAvatar, 11.0, 2, 30.0f} };
        RCHECK(pick(e, 2, 11.1, kPlayer, 9.0).to == kNone, "a switch 2.1 s ago holds even against 60 hp");
        RCHECK(pick(e, 2, 12.5, kPlayer, 9.0).to == kAvatar, "3.5 s after the last switch it may turn");
        Ev z[] = { {kAvatar, 10.0, 1} };
        RCHECK(pick(z, 1, 10.1, kNone, -1e9).to == kAvatar, "no opponent yet: a swing alone is the first threat");
        // two challengers: the one that hurt it most turns it, not the one with the larger count
        Ev t[] = { {kAvatar, 10.0, 2, 2.0f}, {kAvatar, 10.2, 2, 2.0f}, {kAvatar, 10.4, 2, 2.0f}, {kAvatar, 10.6, 2, 2.0f},      // avatar: 4 hits, 8 hp
                   {kAvatar2, 10.1, 2, 8.4f}, {kAvatar2, 10.3, 2, 8.4f}, {kAvatar2, 10.5, 2, 8.4f},                          // avatar 2: 3 hits, 25.2 hp
                   {kPlayer, 10.7, 2, 1.0f} };
        auto p = pick(t, 8, 11.0, kPlayer, -1e9);
        RCHECK(p.to == kAvatar2, "of two challengers the one with the most damage (25 hp) takes it, not the larger count (to=0x%X)", p.to);
    }
    if (passed) *passed = g_pass;
    return g_fail;
}
