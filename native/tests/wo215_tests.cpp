// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-215: engine-free checks of wo215_rules.h. Linked into KCDMP_NativeTests; wo215_tests() returns the number of failures.
#include <cstdio>
#include <cmath>
#include <cstring>

#include "wo215_rules.h"
#include "wo210_rules.h"

using namespace kcdmp::wo215rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo215_tests(int* passed) {
    // ---- the switch word and the bench's twin names
    int nk = 0;
    const Name* k = switch_names(&nk);
    uint32_t all = 0;
    for (int i = 0; i < nk; ++i) all |= k[i].b;
    WCHECK(all == kBitsAll && nk == 8, "every WO-215 native switch has a name (%d names, 0x%X)", nk, all);
    WCHECK(kBitsDefault == (kBitsAll & ~kBitRoute), "every WO-215 switch ships on but the route (WO-216 A.4: it did not pass A.1's test)");
    WCHECK(twin_off_bits("route") == kBitRoute && twin_off_bits("gait, ladder") == (kBitGait | kBitLadder), "the twins by name");
    WCHECK(twin_off_bits("all") == kBitsAll && twin_off_bits("one_bind") == 0 && twin_off_bits(nullptr) == 0, "all (every bit, the route's too); WO-214's names are not WO-215's");

    // ---- A.2 the gait: the smallest class whose top speed covers the host's speed with 15 % headroom, hysteresis going down
    WCHECK(gait_for(1.42f, Gait::Walk) == Gait::Walk && gait_for(1.58f, Gait::Walk) == Gait::Walk,
           "the brain's own walk (1.42 m/s) and a brisk 1.58 ask for the walk class (1.70)");
    WCHECK(gait_for(2.4f, Gait::Walk) == Gait::Jog && gait_for(3.0f, Gait::Walk) == Gait::Run && gait_for(4.8f, Gait::Walk) == Gait::Sprint,
           "a jog (2.4 m/s) the run class 2.94; 3.0 the fast run 3.30; a sprint 4.8 the sprint 5.19");
    WCHECK(gait_for(2.85f, Gait::Walk) == Gait::Run, "2.85 m/s with 5 %% headroom is 2.99 -- over the run class's 2.94: the fast run");
    WCHECK(gait_for(3.5f, Gait::Walk) == Gait::Sprint, "WO-116 F2/F3: 3.5 m/s is too fast for walk and run (their lag grew to 21 and 7.9 m) -> sprint");
    WCHECK(gait_for(1.45f, Gait::Jog) == Gait::Jog && gait_for(1.1f, Gait::Jog) == Gait::Walk,
           "down only well under the threshold (1.45 m/s keeps the jog it came from, 1.1 drops to walk)");
    WCHECK(std::fabs(gait_ema(-1.0f, 1.4f, 0.016f) - 1.4f) < 1e-6f && gait_ema(1.4f, 3.0f, 0.016f) < 1.5f && std::fabs(gait_ema(1.4f, 3.0f, 1.0f) - 3.0f) < 1e-6f,
           "the gait's speed is smoothed: a one-frame spike barely moves it, a second of it does");
    WCHECK(speed_enum(Gait::Walk) == 1 && speed_enum(Gait::Jog) == 3 && speed_enum(Gait::Run) == 4 && speed_enum(Gait::Sprint) == 7,
           "the request's speed enum per gait (WO-116 s5.2)");
    for (int i = 0; i < kGaits - 1; ++i)
        WCHECK(up_threshold(static_cast<Gait>(i)) * kHeadroom <= kClassTopMps[i] + 1e-4f, "a class is never asked past its own top speed (%s)", gait_name(static_cast<Gait>(i)));

    // ---- A.1 the order cadence
    WCHECK(order_due(false, 0.0, 0.0f, false, true), "the first request goes at once");
    WCHECK(!order_due(false, 0.3, 2.0f, false, false), "never more often than every 0.5 s by distance (a5: renewals restart the plan)");
    WCHECK(order_due(false, 0.6, 1.1f, false, false) && !order_due(false, 0.6, 0.8f, false, false),
           "renewed when the target moved 1 m (1.1 yes, 0.8 not yet)");
    WCHECK(order_due(false, 1.05, 0.10f, false, false), "a slow walker's target renewed every second even under 1 m");
    WCHECK(order_due(false, 0.3, 0.4f, false, false, Cadence{}, 0.6f) && !order_due(false, 0.3, 0.4f, false, false, Cadence{}, 2.0f),
           "a copy 0.6 m from its target while its host walks on is renewed early (2 m away: not yet)");
    WCHECK(!order_due(false, 0.1, 0.4f, false, false, Cadence{}, 0.6f) && !order_due(false, 0.3, 0.05f, false, false, Cadence{}, 0.6f),
           "not within 0.25 s, and not for a target that did not move");
    WCHECK(order_due(false, 0.02, 0.0f, true, false), "a gait change goes at once");
    WCHECK(order_due(true, 0.0, 0.0f, false, false), "A.0's per-frame mode: every frame");
    {
        Cadence c; c.minS = 1.0; c.stepM = 1.5f; c.maxS = 2.0; c.leadS = 1.0;
        WCHECK(!order_due(false, 0.6, 3.0f, false, false, c) && order_due(false, 1.1, 1.6f, false, false, c) && !order_due(false, 1.1, 1.0f, false, false, c),
               "a slower cadence: not within 1 s, then only past 1.5 m");
        WCHECK(order_due(false, 2.1, 0.3f, false, false, c) && !order_due(false, 2.1, 0.01f, false, false, c),
               "after 2 s a small move renews it; a target that did not move does not (no restart of a plan for nothing)");
        const float nw[3] = {10.0f, 20.0f, 5.0f};
        float o[3];
        lead_target(nw, 1.4f, 0.0f, 1.0, o);
        WCHECK(std::fabs(o[0] - 11.4f) < 1e-4f && std::fabs(o[1] - 20.0f) < 1e-4f && o[2] == 5.0f, "the lead: 1 s of the host's velocity ahead");
        lead_target(nw, 6.0f, 0.0f, 1.0, o);
        WCHECK(std::fabs(o[0] - 14.0f) < 1e-4f, "never more than 4 m ahead");
    }

    // ---- A.1 the face
    WCHECK(face_due(false, 0.0, 0.0f, 0.0f), "standing, never faced yet: the face goes");
    WCHECK(!face_due(true, 0.2, 1.0f, 1.0f), "not again within half a second");
    WCHECK(face_due(true, 0.6, 0.30f, 0.0f) && !face_due(true, 0.6, 0.20f, 0.0f), "again when the host turned 17 degrees (not 11)");
    WCHECK(face_due(true, 0.6, 0.0f, 0.5f) && !face_due(true, 0.6, 0.0f, 0.3f), "again when the host's spot moved 0.5 m (not 0.3)");
    WCHECK(std::fabs(wrap_pi(3.1f - (-3.1f)) - (-0.0832f)) < 1e-3f, "the turn is measured across the +-pi seam");

    // ---- A.1 the corrector and A.3 the writer's reasons
    WCHECK(needs_correction(1.6f, kCorrectDefaultM) && !needs_correction(1.4f, kCorrectDefaultM), "over 1.5 m from the stream: corrected; under: the route");
    {
        const float a[2] = {0.0f, 0.0f}, b[2] = {2.0f, 0.0f};
        const float ahead[2] = {1.9f, 0.1f}, behind[2] = {-1.7f, 0.0f}, side[2] = {1.0f, 1.6f};
        WCHECK(dist_to_segment(ahead, a, b) < 0.2f, "a copy ahead of the rendered sample, on its way to the led target, is where it should be");
        WCHECK(needs_correction(dist_to_segment(behind, a, b), kCorrectDefaultM) && needs_correction(dist_to_segment(side, a, b), kCorrectDefaultM),
               "1.7 m behind the rendered sample, or 1.6 m off the way: corrected");
        const float p[2] = {3.0f, 4.0f};
        WCHECK(std::fabs(dist_to_segment(p, a, a) - 5.0f) < 1e-4f, "a standing copy (no way): its distance to the sample");
    }
    WCHECK(link_stall(0.6, 0.6) && link_stall(1.5, 2.9), "a body silent while every stream is quiet: a link stall (its walk plays out)");
    WCHECK(!link_stall(0.6, 0.05), "a body silent while other bodies' samples arrive: its host NPC stopped (moved-gated stream)");
    WCHECK(!link_stall(0.3, 0.6), "a body's sample younger than half a second: not silent");
    WCHECK(!link_stall(4.0, 3.5), "a stall past 3 s: the body stops where its stream last was");
    {
        double g = 0.0;
        for (int i = 0; i < 30; ++i) g = gap_ema(g, 0.1);
        WCHECK(std::fabs(stop_after(g) - kBodySilentS) < 1e-9, "a near body (10 samples a second) stops after half a second of silence");
        double far = 0.0;
        for (int i = 0; i < 30; ++i) far = gap_ema(far, 1.0);
        WCHECK(stop_after(far) > 1.9 && stop_after(far) < 2.1, "a far body (a sample a second) is no stop before 2 s of silence (a9's far walkers)");
        WCHECK(stop_after(gap_ema(0.0, 30.0)) <= kStallMaxS, "a gap after a long stop counts 2 s at most: the next stop is seen within 3 s");
        WCHECK(!link_stall(1.0, 1.0, stop_after(far)) && link_stall(2.2, 1.0, stop_after(far)),
               "a far body's silence is a stall only past its own stop time");
        WCHECK(std::fabs(lead_for(0.5, 0.1) - 0.5) < 1e-9 && std::fabs(lead_for(0.5, 1.0) - 1.0) < 1e-9 && std::fabs(lead_for(0.5, 2.0) - 1.5) < 1e-9,
               "the lead: half a second near, the body's own gap far, 1.5 s at most");
    }
    WCHECK(writer_reason(true, false, false, false, false, false, false) == WriterWhy::None, "a copy on the ground, at peace, is routed");
    WCHECK(writer_reason(false, false, false, false, false, false, false) == WriterWhy::Off, "the twin (route off): the writer");
    WCHECK(writer_reason(true, true, false, false, false, false, false) == WriterWhy::Figure, "a partner's figure is never routed");
    WCHECK(writer_reason(true, false, true, false, false, false, false) == WriterWhy::Beast, "a horse or an animal: the writer");
    WCHECK(writer_reason(true, false, false, true, false, false, false) == WriterWhy::Parented, "a rider or a carried body: never the ground writer's route");
    WCHECK(writer_reason(true, false, false, false, true, false, false) == WriterWhy::Air, "a fall: the writer");
    WCHECK(writer_reason(true, false, false, false, false, true, false) == WriterWhy::Fight, "a fighting body: the writer's fight path");
    WCHECK(writer_reason(true, false, false, false, false, false, true) == WriterWhy::Refused, "refused a moment ago: the writer, then the route again");
    WCHECK(writer_reason(false, false, true, false, false, false, false) == WriterWhy::Beast, "a beast is named as a beast with the switch off too");
    WCHECK(!std::strcmp(writer_why(WriterWhy::None), "routed") && !std::strcmp(writer_why(WriterWhy::Correcting), "correcting"), "the names");
    WCHECK(!std::strcmp(writer_why(WriterWhy::Far), "far") && route_far(120.0f) && !route_far(80.0f),
           "WO-216: a copy over 100 m from this player is the writer's (the joiner's pool despawns bodies about 150 m out)");
    WCHECK(!std::strcmp(writer_why(WriterWhy::NotHuman), "not-human") && !std::strcmp(writer_why(WriterWhy::NoPhysics), "no-physics") &&
           static_cast<int>(WriterWhy::kCount) == 15, "every reason has a name (a horse, a body out of physics range, a far one)");

    // ---- B the ladder and the let-go
    WCHECK(ladder(true, true, true, true) == Rung::HostActivity, "the host's activity first");
    WCHECK(ladder(false, true, true, true) == Rung::HostPosture, "then the host's posture");
    WCHECK(ladder(false, false, true, true) == Rung::OwnIdle, "then the copy's own idle");
    WCHECK(ladder(false, false, false, true) == Rung::DefaultIdle, "then the engine's default idle");
    WCHECK(ladder(false, false, false, false) == Rung::None, "nothing known: none (counted, never silent)");
    WCHECK(letgo_m(true) == kCullM && letgo_m(false) == kOldLetGoM && kCullM == 150.0f, "the let-go at the cull range; the twin at WO-209's 70 m");
    WCHECK(owner_keeps(true, true, 120.0f, 60.0f) && !owner_keeps(false, true, 120.0f, 60.0f), "an owned copy at 120 m is kept (the twin let it go at 70 m)");
    WCHECK(!owner_keeps(true, false, 65.0f, 60.0f) && owner_keeps(true, false, 55.0f, 60.0f), "a new copy is taken within 60 m only");
    WCHECK(!owner_keeps(true, true, 151.0f, 60.0f) && !owner_keeps(true, true, -1.0f, 60.0f), "past the cull, or no distance: let go");

    // ---- C the watchdog's named outcomes (no install-pending loop, no unknown)
    WCHECK(kcdmp::wo210rules::kPathRefusedHere == 21 && kcdmp::wo210rules::kPathStaleOurs == 22 && kcdmp::wo210rules::kPathDormantHost == 23 &&
           kcdmp::wo210rules::kPathNpcState == 24 && kcdmp::wo210rules::kPathNpcStateRefused == 25 && kcdmp::wo210rules::kPathCount == 26,
           "the five new outcomes have numbers");
    WCHECK(!std::strcmp(kcdmp::wo210rules::path_name(kcdmp::wo210rules::kPathNpcState), "24-npc-state") &&
           !std::strcmp(kcdmp::wo210rules::path_name(kcdmp::wo210rules::kPathNpcStateRefused), "25-npc-state-refused"), "the NPC-state path's names");
    WCHECK(!std::strcmp(kcdmp::wo210rules::path_name(kcdmp::wo210rules::kPathRefusedHere), "21-refused-here") &&
           !std::strcmp(kcdmp::wo210rules::path_name(kcdmp::wo210rules::kPathStaleOurs), "22-stale-ours") &&
           !std::strcmp(kcdmp::wo210rules::path_name(kcdmp::wo210rules::kPathDormantHost), "23-dormant-host"), "and names");

    // ---- D.1 dormant
    WCHECK(dormant(false, false, false, false), "no brain, no stance, no activity, no activation: dormant");
    WCHECK(!dormant(true, false, false, false) && !dormant(false, true, false, false) && !dormant(false, false, true, false) && !dormant(false, false, false, true),
           "any one of them: a live body");

    // ---- E the player and a ghost
    WCHECK(ghost_fight(true, true, 2, 2, 2.5), "every opponent a ghost for 2.5 s: cleared");
    WCHECK(!ghost_fight(true, true, 2, 1, 9.0), "one live opponent: the fight stands");
    WCHECK(!ghost_fight(true, true, 1, 1, 1.0), "under 2 s: not yet");
    WCHECK(!ghost_fight(false, true, 1, 1, 60.0), "the twin (ghost_fight off): never");
    WCHECK(!ghost_fight(true, false, 1, 1, 60.0) && !ghost_fight(true, true, 0, 0, 60.0), "not in combat, or no opponent: nothing to clear");

    *passed = g_pass;
    return g_fail;
}
