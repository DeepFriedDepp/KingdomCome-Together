// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-215 (docs/WO-215-findings.md): the animation substrate -- the pure rules of the native half (engine-free:
// native/tests/wo215_tests.cpp checks them).
//   A  copies move by the engine's own movement request (route): a moving stream is a request toward its newest sample at the gait
//      the host's own speed asks for; a standing stream is a stop that faces the host's heading; the per-frame writer only corrects.
//   B  no empty body: the owner's ladder when a clip ends or a state is let go; the let-go distance is the cull range.
//   D  only live NPCs become copies (the host's census marks the dormant ones); E the player is never held in a fight with a ghost.
#include <cmath>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo215rules {

// ---- the agent's switch word (op 150): every bit on until the agent says otherwise; the bench's twin: KCDMP_TEST_W215_OFF=<names|all>
constexpr uint32_t kBitRoute = 0x001;        // A.1 route: a copy on the ground moves by the engine's movement request
constexpr uint32_t kBitGait = 0x002;         // A.2 gait: the request's speed class follows the host's speed (off: every request walks)
constexpr uint32_t kBitLadder = 0x004;       // B ladder: the owner never leaves a living bound copy's full body empty
constexpr uint32_t kBitLetGoCull = 0x008;    // B: the owner lets go only at the cull range (off: WO-209's 70 m)
constexpr uint32_t kBitActivityLine = 0x010; // C.0 one activity line per NPC (off: the two descriptions, as before)
constexpr uint32_t kBitDormant = 0x020;      // D.1 the host's census keeps dormant bodies out of the stream
constexpr uint32_t kBitGhostFight = 0x040;   // E the joiner's player leaves a fight whose every opponent is a ghost
constexpr uint32_t kBitProbeAll = 0x080;     // G the animation probe covers the whole session (one body a frame)
constexpr uint32_t kBitsDefault = 0x0FF;

struct Name { const char* n; uint32_t b; };
inline const Name* switch_names(int* n) {
    static const Name k[] = {{"route", kBitRoute}, {"gait", kBitGait}, {"ladder", kBitLadder}, {"letgo_cull", kBitLetGoCull},
                             {"activity_line", kBitActivityLine}, {"dormant", kBitDormant}, {"ghost_fight", kBitGhostFight},
                             {"probe_all", kBitProbeAll}};
    *n = static_cast<int>(sizeof k / sizeof k[0]);
    return k;
}
inline uint32_t twin_off_bits(const char* list) {
    uint32_t b = 0;
    if (!list) return 0;
    int nk = 0;
    const Name* k = switch_names(&nk);
    const char* p = list;
    while (*p) {
        while (*p == ',' || *p == ' ') ++p;
        const char* s = p;
        while (*p && *p != ',' && *p != ' ') ++p;
        const size_t n = static_cast<size_t>(p - s);
        if (n == 3 && std::strncmp(s, "all", 3) == 0) b |= kBitsDefault;
        for (int i = 0; i < nk; ++i)
            if (n == std::strlen(k[i].n) && std::strncmp(s, k[i].n, n) == 0) b |= k[i].b;
    }
    return b;
}

// ---- A.2 the gait --------------------------------------------------------------------------------------------------------------------
// The movement request's speed is one enum (WO-116 s5.2, measured on a suspended walker: 1 walk 1.70 m/s, 3 run 2.94, 4 fastRun 3.30,
// 7 sprint 5.19; the brain's own walk is the walk class throttled to ~1.42). A copy is asked for the smallest class whose top speed
// covers the host's own speed with a margin -- a class too slow falls behind for good (WO-116 F2: 21 m), a class far too fast closes in,
// stops and starts again (F5/F6/F8). Hysteresis: a class goes up at once past its threshold and down only well under it.
enum class Gait : uint8_t { Walk = 0, Jog = 1, Run = 2, Sprint = 3 };
constexpr int kGaits = 4;
inline const char* gait_name(Gait g) {
    static const char* const k[kGaits] = {"walk", "jog", "run", "sprint"};
    const int i = static_cast<int>(g);
    return i >= 0 && i < kGaits ? k[i] : "?";
}
inline int speed_enum(Gait g) {   // the request's +0xF8
    switch (g) { case Gait::Walk: return 1; case Gait::Jog: return 3; case Gait::Run: return 4; default: return 7; }
}
constexpr float kClassTopMps[kGaits] = {1.70f, 2.94f, 3.30f, 5.19f};
constexpr float kHeadroom = 1.05f;          // the class's top speed covers the host's speed times this (a3: 15 % sent village walkers jogging)
constexpr float kDownRatio = 0.85f;         // a class goes down only under this share of its threshold
// The host's speed the gait reads is smoothed (the stream's own speed over its newest samples flaps with the sample timing: a3's village
// changed class 2,199 times in five minutes and ordered more jogs than walks): an exponential average over ~0.6 s.
constexpr float kGaitTauS = 0.6f;
inline float gait_ema(float prev, float sample, float dtS) {
    if (prev < 0.0f) return sample;
    const float k = dtS <= 0.0f ? 0.0f : (dtS >= kGaitTauS ? 1.0f : dtS / kGaitTauS);
    return prev + (sample - prev) * k;
}
inline float up_threshold(Gait g) {         // the host speed above which g is too slow
    return kClassTopMps[static_cast<int>(g)] / kHeadroom;
}
inline Gait gait_for(float hostMps, Gait prev) {
    Gait g = Gait::Walk;
    for (int i = 0; i < kGaits - 1; ++i)
        if (hostMps > up_threshold(static_cast<Gait>(i))) g = static_cast<Gait>(i + 1);
    if (static_cast<int>(g) >= static_cast<int>(prev)) return g;
    // going down: keep prev while the speed is not well under the threshold that brought prev
    const Gait below = static_cast<Gait>(static_cast<int>(prev) - 1);
    if (hostMps > up_threshold(below) * kDownRatio) return prev;
    return g;
}

// ---- A.1 the route -------------------------------------------------------------------------------------------------------------------
// A stream moves when its host NPC's newest samples cover this speed; it stands after this long under it.
constexpr float kMovingMps = 0.25f;
constexpr double kStillS = 0.30;
// The stream is moved-gated: a host NPC that stops goes silent, so a body's silence is its stop -- unless every stream is silent (a link
// stall: the field's link has two a minute). A body silent this long while no body's sample arrived for kLinkQuietS (and not yet
// kStallMaxS) is in a stall: the request in flight plays out -- no stop at its last sample, which a led walker has passed (it would turn
// and walk back), no correction against a sample the link holds back.
constexpr double kBodySilentS = 0.50;
constexpr double kLinkQuietS = 0.30;
constexpr double kStallMaxS = 3.0;
// A body's own cadence: the host sends a body beyond the near band about once a second (the field's briefing: walking NPCs within 150 m
// at 9.9 Hz p50, the slowest tenth 1.2 Hz) -- half a second of silence is no stop for it. A body is stopped after twice its usual gap
// between samples (smoothed, each gap counted up to 2 s), never under half a second, never over 3 s (a9: far walkers were stopped
// between every two samples -- 1,466 stops and 2,222 corrections in 12 minutes of the village).
constexpr double kGapCapS = 2.0;
constexpr double kGapK = 0.2;
inline double gap_ema(double prev, double gapS) {
    const double g = gapS > kGapCapS ? kGapCapS : (gapS < 0.0 ? 0.0 : gapS);
    return prev <= 0.0 ? g : prev + (g - prev) * kGapK;
}
inline double stop_after(double gapEmaS) {
    double s = 2.0 * gapEmaS;
    if (s < kBodySilentS) s = kBodySilentS;
    if (s > kStallMaxS) s = kStallMaxS;
    return s;
}
inline bool link_stall(double bodySilentS, double linkQuietS, double stopAfterS = kBodySilentS) {
    return bodySilentS >= stopAfterS && linkQuietS >= kLinkQuietS && linkQuietS < kStallMaxS;
}
// The lead follows the cadence too: a target half a second ahead is reached halfway between two samples a second apart (the copy would
// stop and start between them) -- at least the body's own gap ahead, at most 1.5 s.
inline double lead_for(double leadS, double gapEmaS) {
    double l = gapEmaS > leadS ? gapEmaS : leadS;
    return l > 1.5 ? 1.5 : l;
}
// A moving stream's request is renewed when its newest sample moved this far from the last request's target, at most this often (the
// movement system re-plans a request it is given; WO-116 s5.3 followed a 10 Hz target at a constant lag with no stop-start), and at least
// this often while moving (a request whose target the body reached stops it). Per-frame (A.0's test): every frame.
// a5's sweep (the village, three cadences against 0.64.1's writer: walk shown / sliding per 100 samples of walking host NPCs): renewing
// every 0.1 s 84 % / 6.5 (each renewal restarts the plan), every 0.5 s with a 0.5 s lead 76 % / 1.7, every 1 s with a 1 s lead 71 % / 1.6;
// the writer 76-82 % / 3.0. The default is the middle one, renewed early as a copy nears its target while its host NPC walks on.
constexpr float kOrderStepM = 1.0f;
constexpr double kOrderMinS = 0.50;
constexpr double kOrderMaxS = 1.00;
// The lead (a12's village and lead sweep): a target half a second ahead is about a metre away, inside the distance where the engine
// plans its own stop -- copies of walking host NPCs showed a start or a stop transition 50-70 % of the time and fell behind (16-18 route
// corrections per body-minute); a target 1.5 s ahead keeps the engine in its walk (start/stop ~0 %, corrections under 1 per body-minute).
// A copy may walk up to that far past a host NPC that stops before the stop is seen, and turns back to its spot.
constexpr double kOrderLeadS = 1.50;
constexpr float kApproachM = 1.0f;     // a walking copy this close to its target is given the next one (it would stop there otherwise)
constexpr double kApproachMinS = 0.25;
// The cadence, tunable on the bench (op 153): a3 found that every new request restarts the copy's plan (a request every frame moved the
// probe's body 1.35 m in 6 s; at 10 Hz the village walkers flickered MotionIdle / MotionMovement), so the route asks less often, for a
// point ahead of the host's newest sample by its own velocity times leadS.
struct Cadence { double minS = kOrderMinS; float stepM = kOrderStepM; double maxS = kOrderMaxS; double leadS = kOrderLeadS; float approachM = kApproachM; };
// bodyToTargetM: the copy's distance to the target it was last given (< 0 unknown) -- a copy about to arrive while its host walks on is
// renewed early (not before kApproachMinS), so it never stops at a target its host has long passed
inline bool order_due(bool perFrame, double sinceLastS, float targetMovedM, bool gaitChanged, bool first, const Cadence& c = Cadence{},
                      float bodyToTargetM = -1.0f) {
    if (first || gaitChanged) return true;
    if (perFrame) return true;
    if (c.approachM > 0.0f && bodyToTargetM >= 0.0f && bodyToTargetM < c.approachM && sinceLastS >= kApproachMinS && targetMovedM >= 0.10f) return true;
    if (sinceLastS < c.minS) return false;
    return targetMovedM >= c.stepM || (sinceLastS >= c.maxS && targetMovedM >= 0.05f);
}
// The point a moving copy is asked for: the newest sample, led along the host's own velocity (clamped to 4 m ahead).
inline void lead_target(const float newest[3], float vx, float vy, double leadS, float out[3]) {
    float dx = static_cast<float>(vx * leadS), dy = static_cast<float>(vy * leadS);
    const float l = std::sqrt(dx * dx + dy * dy);
    if (l > 4.0f) { dx *= 4.0f / l; dy *= 4.0f / l; }
    out[0] = newest[0] + dx; out[1] = newest[1] + dy; out[2] = newest[2];
}
// Standing: one request to the host's spot that faces the host's heading (the exact positioning: the engine walks the last bit, stops
// with its own stop transition and turns), again only when the host turned or moved on.
constexpr float kFaceTurnRad = 0.26f;       // 15 degrees
constexpr float kFaceMoveM = 0.40f;
constexpr double kFaceMinS = 0.50;
inline float wrap_pi(float a) {
    const float twopi = 6.2831853f, pi = 3.1415927f;
    while (a > pi) a -= twopi;
    while (a < -pi) a += twopi;
    return a;
}
inline bool face_due(bool sent, double sinceLastS, float yawDelta, float movedM) {
    if (!sent) return true;
    if (sinceLastS < kFaceMinS) return false;
    return std::fabs(wrap_pi(yawDelta)) >= kFaceTurnRad || movedM >= kFaceMoveM;
}
// The corrector: a routed body this far from where the stream says (the rendered sample, mp_route_correct_m) is blended back by the
// writer and counted; a body this close again goes back to the route. Never a snap: the writer's own blend (WO-118) runs it.
constexpr float kCorrectDefaultM = 1.5f;
constexpr float kCorrectBackM = 0.30f;
inline bool needs_correction(float distM, float thresholdM) { return distM > thresholdM; }
// Where a walking copy may be: on the way from the rendered sample to the target it was given (a lead puts that target ahead of the
// host's newest sample, so a routed copy is meant to be ahead of the rendered sample -- a5's sweep corrected led copies 1,441 times in two
// minutes for being where they were sent). The corrector's distance is the copy's planar distance to that segment.
inline float dist_to_segment(const float p[2], const float a[2], const float b[2]) {
    const float abx = b[0] - a[0], aby = b[1] - a[1];
    const float apx = p[0] - a[0], apy = p[1] - a[1];
    const float l2 = abx * abx + aby * aby;
    float t = l2 > 1e-6f ? (apx * abx + apy * aby) / l2 : 0.0f;
    if (t < 0.0f) t = 0.0f; else if (t > 1.0f) t = 1.0f;
    const float dx = apx - t * abx, dy = apy - t * aby;
    return std::sqrt(dx * dx + dy * dy);
}
// A.2's gait meter: the copy's own pace against its host NPC's over this window (a gait is a sustained pace, not one stride: the route's
// copy walks at its class's pace and eases into each target)
constexpr double kMeterWindowS = 2.0;
// New requests a frame, at most (a village's ~100 standing copies would otherwise all ask in the first frame of a bind wave): a body that
// has no request yet waits on the writer for a frame; one that has one keeps it and asks again the next frame.
constexpr int kOrdersPerFrame = 12;
inline bool budget_left(int usedThisFrame) { return usedThisFrame < kOrdersPerFrame; }
// A body whose request the engine refused rests on the writer this long before the route asks again (the refusal is logged).
constexpr double kRefusedRestS = 5.0;
// A body falling or flying is the writer's: the stream's own vertical speed over its newest samples (a fall, a jump, a cart's bump).
constexpr float kAirVzMps = 1.5f;

// Why a bound body is not routed this frame (A.3: the writer stays for what the route cannot do -- counted, never silent).
enum class WriterWhy : uint8_t { None = 0, Off = 1, Beast = 2, Parented = 3, Air = 4, Fight = 5, Refused = 6, NoNpc = 7, NotArmed = 8,
                                 Correcting = 9, Figure = 10, Budget = 11, NotHuman = 12, NoPhysics = 13, kCount = 14 };
inline const char* writer_why(WriterWhy w) {
    static const char* const k[] = {"routed", "off", "beast", "parented", "air", "fight", "refused", "no-npc", "not-armed", "correcting", "figure",
                                    "budget", "not-human", "no-physics"};
    const int i = static_cast<int>(w);
    return i >= 0 && i < static_cast<int>(WriterWhy::kCount) ? k[i] : "?";
}
// The order of the tests: a figure (a partner's body) and a beast (a horse or an animal: no movement task) never; a parented body (a
// rider, a carried body) never; the switch; a body in the air; a fighting body (the writer's fight path: holds, chase, the fight band);
// a body the engine refused a moment ago; then the route.
inline WriterWhy writer_reason(bool on, bool figure, bool beast, bool parented, bool air, bool fight, bool refusedRest) {
    if (figure) return WriterWhy::Figure;
    if (beast) return WriterWhy::Beast;
    if (parented) return WriterWhy::Parented;
    if (!on) return WriterWhy::Off;
    if (air) return WriterWhy::Air;
    if (fight) return WriterWhy::Fight;
    if (refusedRest) return WriterWhy::Refused;
    return WriterWhy::None;
}

// ---- B no empty body -------------------------------------------------------------------------------------------------------------------
// The owner's ladder when a clip ends or a state is let go: the host's activity, else the host's posture, else the copy's own idle, else
// the engine's default idle for its archetype. "Empty" is never a state a living bound copy is left in.
enum class Rung : uint8_t { HostActivity = 0, HostPosture = 1, OwnIdle = 2, DefaultIdle = 3, None = 4 };
inline const char* rung_name(Rung r) {
    static const char* const k[] = {"host-activity", "host-posture", "own-idle", "default-idle", "none"};
    const int i = static_cast<int>(r);
    return i >= 0 && i <= 4 ? k[i] : "?";
}
inline Rung ladder(bool hostActivity, bool hostPosture, bool ownIdle, bool defaultIdle) {
    if (hostActivity) return Rung::HostActivity;
    if (hostPosture) return Rung::HostPosture;
    if (ownIdle) return Rung::OwnIdle;
    if (defaultIdle) return Rung::DefaultIdle;
    return Rung::None;
}
// The let-go distance: the cull range the host's stream keeps (WO-213's bands send every NPC within 150 m of a player); WO-209's 70 m
// let-go stays only with the switch off.
constexpr float kCullM = 150.0f;
constexpr float kOldLetGoM = 70.0f;
inline float letgo_m(bool cullOn) { return cullOn ? kCullM : kOldLetGoM; }
// The owner takes a copy within takeM of the local player and keeps an owned one out to the let-go distance.
inline bool owner_keeps(bool cullOn, bool owned, float distM, float takeM) { return distM >= 0 && distM <= (owned ? letgo_m(cullOn) : takeM); }

// ---- D.1 dormant bodies (the host's census) ---------------------------------------------------------------------------------------------
// A body the host holds with no brain running (suspended or never woken), no stance, no activity and no activation -- the encounter
// pools' wanderers, parked until an encounter takes them -- is not a copy anyone should see. Sent again the moment any of these changes.
inline bool dormant(bool brainRunning, bool hasStance, bool hasActivity, bool activated) {
    return !brainRunning && !hasStance && !hasActivity && !activated;
}

// ---- E the player and a ghost ----------------------------------------------------------------------------------------------------------
// The joiner's player in combat whose every opponent is a copy with a failed combat actor (the engine cannot fight it, nor end the
// fight on its own): after this long the mod clears the player's combat the engine's way.
constexpr double kGhostFightS = 2.0;
inline bool ghost_fight(bool on, bool playerInCombat, int opponents, int ghostOpponents, double sinceS) {
    return on && playerInCombat && opponents > 0 && ghostOpponents == opponents && sinceS >= kGhostFightS;
}

} // namespace kcdmp::wo215rules
