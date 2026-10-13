// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-178: engine-free checks of the hostility answer's rules and C4's hold and engine-like cap (wo178_rules.h), and that the hook's
// prologue is in the registry. Linked into KCDMP_NativeTests; wo178_tests() returns the number of failures.
#include <cmath>
#include <cstdio>
#include <cstring>

#include "hook_prologues.h"
#include "wo173_rules.h"
#include "wo178_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
bool near_eq(float a, float b) { return std::fabs(a - b) < 1e-3f; }
} // namespace

int wo178_tests(int* passed) {
    using namespace wo178rules;
    // ---- C1: the test is found from the selector's bytes (the Modding Tools 1.5.5 selector, 0x40 bytes around its call, read this WO)
    {
        // mov rcx,[rsi]; mov rbx,[rdi]; mov rdx,rbx; mov rcx,[rcx+0x478]; call <test>; test al,al
        const uint8_t sel[] = {0x48, 0x8B, 0x0E, 0x48, 0x8B, 0x1F, 0x48, 0x8B, 0xD3, 0x48, 0x8B, 0x89, 0x78, 0x04, 0x00, 0x00,
                               0xE8, 0xCB, 0xB0, 0x03, 0x00, 0x84, 0xC0};
        const uintptr_t base = 0x338B80;
        const RpgCall r = find_rpg_call(sel, sizeof sel, base, 0);
        WCHECK(r.count == 1, "one combat-RPG call in the selector (got %d)", r.count);
        WCHECK(r.disp == 0x478, "the combat RPG's offset is read from the code (0x%X)", r.disp);
        WCHECK(r.target == 0x373C60, "the call's target is the test (0x%llX)", static_cast<unsigned long long>(r.target));
        WCHECK(find_rpg_call(sel, sizeof sel, base, 0x478).count == 1 && find_rpg_call(sel, sizeof sel, base, 0x480).count == 0,
               "a wanted offset accepts only itself");
        // the same call twice (a second loop body) is not ambiguous; a second, different target is
        uint8_t two[sizeof sel * 2];
        std::memcpy(two, sel, sizeof sel);
        std::memcpy(two + sizeof sel, sel, sizeof sel);
        WCHECK(find_rpg_call(two, sizeof sel, base, 0).count == 1, "control");
        // the second copy calls rel32 relative to its own place: a different target -> ambiguous
        WCHECK(find_rpg_call(two, sizeof two, base, 0).count == 2, "two different targets -> ambiguous (count 2)");
        two[sizeof sel + 17] = static_cast<uint8_t>(two[sizeof sel + 17] - sizeof sel);   // rel32 adjusted: the same target again
        WCHECK(find_rpg_call(two, sizeof two, base, 0).count == 1, "the same target twice -> one");
        const uint8_t far[] = {0x48, 0x8B, 0x89, 0x00, 0x30, 0x00, 0x00, 0xE8, 0, 0, 0, 0};
        WCHECK(find_rpg_call(far, sizeof far, 0, 0).count == 0, "an implausible offset (0x3000) is not the combat RPG");
        WCHECK(find_rpg_call(nullptr, 0, 0, 0).count == 0 && find_rpg_call(sel, 5, base, 0).count == 0, "short input: nothing");
    }
    {
        // the test's own head after the prologue: mov rbx,rcx; mov rsi,rdx; lea rcx,[rip+..]; call ..; mov rcx,[rbx+8]
        const uint8_t head[] = {0x48, 0x8B, 0xD9, 0x48, 0x8B, 0xF2, 0x48, 0x8D, 0x0D, 0x1E, 0x96, 0x51, 0x00, 0xE8, 0xEF, 0x23, 0x21, 0x00,
                                0x48, 0x8B, 0x4B, 0x08, 0x48, 0x85, 0xC9};
        WCHECK(owner_offset(head, sizeof head) == 8, "the owner is read at RPG +0x08");
        const uint8_t noKeep[] = {0x48, 0x8B, 0x4B, 0x08};
        WCHECK(owner_offset(noKeep, sizeof noKeep) == -1, "an owner read without `this` kept in rbx is not the shape");
        WCHECK(owner_offset(nullptr, 0) == -1, "nothing -> -1");
    }
    {
        bool reg = false;
        for (const auto& h : hookpro::kAll)
            if (h.bytes == hookpro::kHostileTest && h.len == 15 && std::strcmp(h.name, "wo178 hostility test") == 0) reg = true;
        WCHECK(reg, "the hostility test's prologue is in the registry (15 bytes: the x64 boundary check runs over it)");
        WCHECK(hookpro::kHostileTest[0] == 0x48 && hookpro::kHostileTest[14] == 0x20, "the registry bytes are the ones read");
        bool clinch = false;
        for (const auto& h : hookpro::kAll)
            if (h.bytes == hookpro::kClinchStart && h.len == 19 && std::strcmp(h.name, "wo178 clinch start") == 0) clinch = true;
        WCHECK(clinch, "the clinch start's prologue is in the registry (19 bytes: seven pushes and sub rsp,80h)");
    }
    // ---- C1: the answer
    {
        WCHECK(answer_hostile(true, true, true, Ex::None), "on, the local player asks, a listed copy, nothing excludes -> hostile");
        WCHECK(!answer_hostile(false, true, true, Ex::None), "switch off -> the game's answer");
        WCHECK(!answer_hostile(true, false, true, Ex::None), "another fighter asks (an NPC) -> the game's answer");
        WCHECK(!answer_hostile(true, true, false, Ex::None), "a neutral villager (not listed) -> the game's answer");
        WCHECK(!answer_hostile(true, true, true, Ex::Figure), "the partner's figure -> never");
        WCHECK(!answer_hostile(true, true, true, Ex::Arranged), "an arranged-fight actor -> never");
        WCHECK(!answer_hostile(true, true, true, Ex::Dead), "a dead copy -> never");
        WCHECK(!answer_hostile(true, true, true, Ex::Down), "a knocked-out copy -> never");
        Candidate c{};
        WCHECK(exclusion(c) == Ex::None, "nothing set -> no exclusion");
        c.down = true; WCHECK(exclusion(c) == Ex::Down, "down");
        c.dead = true; WCHECK(exclusion(c) == Ex::Dead, "dead before down");
        c.arranged = true; WCHECK(exclusion(c) == Ex::Arranged, "arranged before dead");
        c.figure = true; WCHECK(exclusion(c) == Ex::Figure, "figure first");
        WCHECK(why_valid(1) && why_valid(2) && why_valid(3) && !why_valid(0) && !why_valid(4), "the agent's whys: 1..3");
        WCHECK(std::strcmp(why_name(Why::Engaged), "engaged") == 0 && std::strcmp(why_name(Why::PartnerFight), "partner-fight") == 0 &&
               std::strcmp(why_name(Why::FigureFight), "figure-fight") == 0, "the log's words");
        WCHECK(std::strcmp(ex_name(Ex::Figure), "figure") == 0 && std::strcmp(ex_name(Ex::Arranged), "arranged-fight") == 0, "exclusion words");
        WCHECK(offer_live(10.0, 12.0) && offer_live(10.0, 13.5) && !offer_live(10.0, 13.6) && !offer_live(0.0, 1.0) && !offer_live(10.0, 9.0),
               "an offer lives 3.5 s after its last refresh");
        WCHECK(kMaxRows >= 16, "room for a crowd");
    }
    // ---- C4 (1): the hold
    {
        WCHECK(should_hold(true, true, 0.0) && should_hold(true, true, 0.79), "only the window missing, within 0.8 s -> hold");
        WCHECK(!should_hold(true, true, 0.8) && !should_hold(true, true, 2.0), "0.8 s reached -> no more holding (the fallback runs)");
        WCHECK(!should_hold(false, true, 0.0), "the hold switched off -> fall back at once");
        WCHECK(!should_hold(true, false, 0.0), "another precondition missing -> no hold");
    }
    // ---- C4 (2): the engine-like cap
    {
        const float v[] = {18.f, 22.f, 15.f, 28.f, 20.f};
        WCHECK(near_eq(median_of(v, 5), 20.f), "median of five");
        WCHECK(near_eq(median_of(v, 4), 20.f), "median of four (18,22,15,28 -> 20)");
        WCHECK(median_of(v, 0) < 0, "no samples -> unknown");
        Ring r{};
        WCHECK(r.median() < 0, "empty ring -> unknown");
        for (int i = 0; i < 20; ++i) r.add(10.f + i);   // keeps the last 15: 15..29
        WCHECK(r.n == kMedianWindow && near_eq(r.median(), 22.f), "the ring keeps the last 15 (median 22, got %.2f)", r.median());
        r.add(-1.f); r.add(NAN);
        WCHECK(r.n == kMedianWindow && near_eq(r.median(), 22.f), "bad samples are not kept");
        // until the engine's cost is known: the default 25
        WCHECK(near_eq(engine_like_cap(60.f, -1.f, 100.f, 100.f), 25.f), "unknown median -> 25");
        WCHECK(near_eq(engine_like_cap(10.f, -1.f, 100.f, 100.f), 10.f), "a smaller measure stays");
        WCHECK(near_eq(engine_like_cap(60.f, 18.f, 100.f, 100.f), 18.f), "the engine's median holds the blow");
        WCHECK(near_eq(engine_like_cap(60.f, 50.f, 100.f, 100.f), 35.f), "WO-173's 35 %% cap stays the outer bound");
        WCHECK(near_eq(engine_like_cap(60.f, 25.f, 30.f, 100.f), 15.f), "never below 15 %% of the maximum (30 -> 15 left)");
        WCHECK(near_eq(engine_like_cap(60.f, 25.f, 15.f, 100.f), 0.f) && near_eq(engine_like_cap(60.f, 25.f, 5.f, 100.f), 0.f),
               "at or under the floor: raw blows take no more stamina");
        WCHECK(near_eq(engine_like_cap(60.f, 25.f, -1.f, 100.f), 25.f), "stamina unknown: the floor cannot be judged, the other limits hold");
        WCHECK(near_eq(engine_like_cap(0.f, 25.f, 100.f, 100.f), 0.f) && near_eq(engine_like_cap(NAN, 25.f, 100.f, 100.f), 0.f), "nothing to apply");
        // 30 fallback-only blows on a fresh NPC never floor it (the synthetic check's arithmetic)
        float st = 100.f;
        for (int i = 0; i < 30; ++i) st -= engine_like_cap(40.f, -1.f, st, 100.f);
        WCHECK(st >= 15.f - 1e-3f, "30 raw blows leave the NPC at its floor or above (%.1f)", st);
        WCHECK(kHoldS > 0.79 && kHoldS < 0.81 && near_eq(kDefaultFallbackSt, 25.f) && near_eq(kStaminaFloorShare, 0.15f), "the WO's numbers");
    }
    // ---- the player's own block
    WCHECK(own_block(true, 0x80, true, 0.9f) && own_block(true, 0x100, true, 0.3f), "holding block, facing the striker: he blocks");
    WCHECK(!own_block(true, 0x80, true, 0.1f) && !own_block(true, 0x80, true, -0.8f), "the striker beside or behind him: no block");
    WCHECK(!own_block(true, 0x2, true, 1.0f) && !own_block(true, 0x80, false, 1.0f), "not blocking, or the state unread: no block");
    WCHECK(!own_block(false, 0x80, true, 1.0f) && !own_block(true, 0x80, true, NAN), "switch off, or the facing unknown: no block");
    // ---- the reach is the swing's own: its row's hit distance (+ the stream's slack); 2.0 m only when the row says nothing
    WCHECK(near_eq(reach_limit(1.6f), 2.0f) && near_eq(reach_limit(0.63f), 1.03f), "the longsword stab (hit_distance 1.6) reaches 2.0 m, a short jab 1.03 m");
    WCHECK(near_eq(reach_limit(0.0f), kRecoilReachM) && near_eq(reach_limit(NAN), kRecoilReachM), "no row distance: the 2.0 m default");
    WCHECK(in_reach(1.9f, reach_limit(1.6f)) && !in_reach(2.1f, reach_limit(1.6f)), "1.9 m lands, 2.1 m misses for the stab");
    WCHECK(!in_reach(1.2f, reach_limit(0.63f)), "the jab misses at 1.2 m");
    WCHECK(!in_reach(-1.0f, 2.0f) && !in_reach(NAN, 2.0f), "an unknown distance: never in reach");
    WCHECK(recoil_in_reach(2.0f) && !recoil_in_reach(2.9f), "the default without a row");
    WCHECK(target_reachable(2.9f, 1.6f, 2.7f) && !target_reachable(3.2f, 1.6f, 2.7f), "the wind-up: the alignment range (2.7 m) plus slack");
    WCHECK(target_reachable(1.9f, 1.6f, 0.0f) && !target_reachable(2.2f, 1.6f, 0.0f), "no alignment: the reach");
    WCHECK(near_eq(copy_reach_limit(1.5f, 1.6f), 1.7f), "the engine's reach for the weapon wins over the row's (+0.2 m slack)");
    WCHECK(near_eq(copy_reach_limit(-1.0f, 1.6f), reach_limit(1.6f)) && near_eq(copy_reach_limit(NAN, 0.0f), kRecoilReachM),
           "an unread engine reach falls back to the row's, then the 2.0 m default");
    WCHECK(!engine_reach_ok(0.1f) && !engine_reach_ok(12.0f) && engine_reach_ok(2.4f), "an engine reach outside 0.3..8 m is not trusted");
    WCHECK(near_eq(contact_limit(2.7f, 1.6f), 2.0f), "a blow lands at the swing's contact distance (2.0 m for the stab), not the 2.7 m wind-up reach");
    WCHECK(near_eq(contact_limit(1.2f, 1.6f), 1.4f) && near_eq(contact_limit(-1.0f, 1.6f), 2.0f), "a shorter engine reach caps it; unread = the row's");
    WCHECK(in_reach(1.6f, copy_reach_limit(1.5f, 0.0f)) && !in_reach(1.8f, copy_reach_limit(1.5f, 0.0f)), "a 1.5 m reach lands at 1.6 m, misses at 1.8 m");
    // ---- the player's recoil: a kept reaction has its own word in the WO173-HIT / status lines
    WCHECK(std::strcmp(wo173rules::react_name(wo173rules::React::PlayerKept), "player-only-hit-reactions") == 0, "the kept reaction's word");
    WCHECK(static_cast<int>(wo173rules::React::PlayerKept) == 10, "appended after Off (9)");
    if (passed) *passed = g_pass;
    return g_fail;
}
