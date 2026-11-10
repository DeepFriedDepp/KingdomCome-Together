// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-216 (docs/WO-216-findings.md): the rest of the body -- the pure rules of the native half (engine-free:
// native/tests/wo216_tests.cpp checks them).
//   A  route_replay: the host reads each streamed NPC's movement task (its request: goal, speed class, exact end) on the AI update's
//      thread; a copy makes that request once per change; the position stream only corrects.
//   B  no_brains: no NPC on the joiner runs its own brain -- every body suspended; one the host does not stream is frozen and hidden.
#include <cmath>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo216rules {

// ---- the agent's switch word (op 160): every bit on until the agent says otherwise; the bench's twin: KCDMP_TEST_W216_OFF=<names|all>
constexpr uint32_t kBitRouteReplay = 0x001;   // A the host's own request replayed on its copy (off: WO-215's walk-to-sample)
constexpr uint32_t kBitNoBrains = 0x002;      // B no brain runs on the joiner (off: the bind-time suspend only)
constexpr uint32_t kBitActivityLine = 0x004;  // C one activity line from the host (off: the two descriptions, as 0.64.2)
constexpr uint32_t kBitOutfits = 0x008;       // D the host NPC's worn items on its copy
constexpr uint32_t kBitRiders = 0x010;        // E riders attached the engine's way, horses on the route
constexpr uint32_t kBitPoses = 0x020;         // F look poses, falls and get-ups, clinches, recoils through the one owner
constexpr uint32_t kBitNpcFights = 0x040;     // G fights the players are not in, mirrored
constexpr uint32_t kBitsDefault = 0x07F;

struct Name { const char* n; uint32_t b; };
inline const Name* switch_names(int* n) {
    static const Name k[] = {{"route_replay", kBitRouteReplay}, {"no_brains", kBitNoBrains}, {"activity_line", kBitActivityLine},
                             {"outfits", kBitOutfits}, {"riders", kBitRiders}, {"poses", kBitPoses}, {"npc_fights", kBitNpcFights}};
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

// ---- A.0 the host's request, as its NPC's movement task keeps it (docs/WO-216-findings.md A.0) ----------------------------------------
// The task (C_MovementTask) copies the request into itself at its start: the request id (+0x10, -1 when none runs), the speed class
// (+0x160), the path request it clones (+0x118: the goal at +0x80 of an S_PathFindingRequestDefault), the exact positioning (the end point
// and its direction, the flag), and the request's own tolerances. The host reads it, and sends it on a change.
constexpr uint32_t kNoReq = 0xFFFFFFFFu;
enum : uint8_t {
    kRecActive = 0x01,     // a request runs on the host's NPC
    kRecExact = 0x02,      // ... with an exact end (point + facing)
    kRecGoal = 0x04,       // ... and its goal is known (a path request of the default kind)
    kRecPrePath = 0x08,    // ... it follows a path given with the request (no path request: the goal is not read)
    kRecBeast = 0x10,      // the body is not a man or a woman (a horse: E)
};
struct RouteRec {
    uint32_t reqId = kNoReq;   // the host's request id (its task's +0x10)
    uint8_t speed = 1;         // the request's speed enum (1 walk, 2 slow, 3 run, 4 fast run, 7 sprint)
    uint8_t flags = 0;
    uint8_t style = 7;         // the request's +0xFC (default 7)
    uint8_t rsv = 0;
    float goal[3]{};           // the path request's target
    float exact[3]{};          // the exact end
    float exactYaw = 0;        // its facing (CryEngine yaw: 0 faces +Y)
    float tolA = -1.0f;        // the request's +0x110 (default -1)
    float tolB = -2.0f;        // the request's +0x114 (default -2)
    uint32_t seq = 0;          // the host's change count for this body
    uint32_t pathId = kNoReq;  // the path finder's request in flight (the task's +0x124; -1 none)
};
constexpr int kRecWire = 4 + 4 + 12 + 12 + 4 + 4 + 4 + 4 + 4;   // 52 bytes on the wire after the name
inline void rec_put(const RouteRec& r, uint8_t* o) {
    std::memcpy(o, &r.reqId, 4); o[4] = r.speed; o[5] = r.flags; o[6] = r.style; o[7] = r.rsv;
    std::memcpy(o + 8, r.goal, 12); std::memcpy(o + 20, r.exact, 12); std::memcpy(o + 32, &r.exactYaw, 4);
    std::memcpy(o + 36, &r.tolA, 4); std::memcpy(o + 40, &r.tolB, 4); std::memcpy(o + 44, &r.seq, 4); std::memcpy(o + 48, &r.pathId, 4);
}
inline bool finite3(const float* v) { return std::isfinite(v[0]) && std::isfinite(v[1]) && std::isfinite(v[2]); }
inline bool rec_get(const uint8_t* i, RouteRec* r) {
    std::memcpy(&r->reqId, i, 4); r->speed = i[4]; r->flags = i[5]; r->style = i[6]; r->rsv = i[7];
    std::memcpy(r->goal, i + 8, 12); std::memcpy(r->exact, i + 20, 12); std::memcpy(&r->exactYaw, i + 32, 4);
    std::memcpy(&r->tolA, i + 36, 4); std::memcpy(&r->tolB, i + 40, 4); std::memcpy(&r->seq, i + 44, 4); std::memcpy(&r->pathId, i + 48, 4);
    return finite3(r->goal) && finite3(r->exact) && std::isfinite(r->exactYaw) && std::isfinite(r->tolA) && std::isfinite(r->tolB) && r->speed <= 15;
}
inline float yaw_of_dir(float dx, float dy) { return std::atan2(-dx, dy); }   // the inverse of dir = (-sin yaw, cos yaw)
// A speed enum the engine knows: its speed tables answer 0..11 (the controller's own jump tables, 0x1A36250 / 0x1A362E0 on this build);
// the request ctor's default is 1 (WO-116 s5.2 measured 1, 3, 4, 7; the task's start forces 2 for its own reason). Class 0 is common in
// the host's brains (b1: the village's slower walkers) -- replayed as it is, never swapped for the walk.
inline bool known_speed(int s) { return s >= 0 && s <= 11; }

// A change worth a line: another request, a request that ended or began, another class or end, a goal moved this far.
constexpr float kGoalMovedM = 0.25f;
constexpr float kYawMovedRad = 0.10f;
inline float d3(const float* a, const float* b) {
    const float dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
    return std::sqrt(dx * dx + dy * dy + dz * dz);
}
inline float wrap_pi(float a) {
    const float twopi = 6.2831853f, pi = 3.1415927f;
    while (a > pi) a -= twopi;
    while (a < -pi) a += twopi;
    return a;
}
inline bool rec_changed(const RouteRec& a, const RouteRec& b) {
    if (a.reqId != b.reqId || a.flags != b.flags || a.speed != b.speed || a.style != b.style) return true;
    if (!(a.flags & kRecActive)) return false;
    if ((a.flags & kRecGoal) && d3(a.goal, b.goal) > kGoalMovedM) return true;
    if (a.flags & kRecExact) {
        if (d3(a.exact, b.exact) > kGoalMovedM) return true;
        if (std::fabs(wrap_pi(a.exactYaw - b.exactYaw)) > kYawMovedRad) return true;
    }
    return std::fabs(a.tolA - b.tolA) > 0.05f || std::fabs(a.tolB - b.tolB) > 0.05f;
}
// The capture's cadence on the AI update's thread, and the repeat of a running request (the state lane may drop a line: latest wins)
constexpr double kCaptureEveryS = 0.05;
constexpr double kRepeatActiveS = 2.0;
constexpr double kRepeatIdleS = 6.0;
inline bool repeat_due(bool active, double sinceSentS) { return sinceSentS >= (active ? kRepeatActiveS : kRepeatIdleS); }

// ---- A.1 the replay ------------------------------------------------------------------------------------------------------------------
// A line is live this long after it arrived (the host repeats a running one every 2 s, an idle one every 6 s); a copy with no live line
// is WO-215's (walk-to-sample, the face), counted.
constexpr double kLineLiveS = 3.5;
constexpr double kIdleLineLiveS = 8.0;
inline bool line_live(bool active, double ageS) { return ageS >= 0.0 && ageS < (active ? kLineLiveS : kIdleLineLiveS); }
// Replay once per change: a new request id, or the same request re-aimed (another goal, end or class).
inline bool replay_due(bool haveReplayed, uint32_t replayedReq, uint32_t replayedSeq, const RouteRec& line) {
    if (!(line.flags & kRecActive) || !(line.flags & kRecGoal)) return false;
    if (!haveReplayed) return true;
    return line.reqId != replayedReq || line.seq != replayedSeq;
}
// The corrector's causes (route_corrections by cause)
enum class Cause : uint8_t { Drift = 0, Refused = 1, NoPath = 2, Stall = 3, kCount = 4 };
inline const char* cause_name(Cause c) {
    static const char* const k[] = {"drift", "refused", "no-path", "stall"};
    const int i = static_cast<int>(c);
    return i >= 0 && i < static_cast<int>(Cause::kCount) ? k[i] : "?";
}
// A drift found within this long after a link stall is the stall's (the stream jumps once the link is back)
constexpr double kStallCauseS = 1.0;
inline Cause drift_cause(double sinceStallEndS) { return sinceStallEndS >= 0.0 && sinceStallEndS < kStallCauseS ? Cause::Stall : Cause::Drift; }
// The copy's own request ended while the host's still runs and the copy is this far from the goal: the engine found no way here
constexpr float kNoPathM = 1.5f;
inline bool no_path(bool hostActive, bool ourEnded, float copyToGoalM) { return hostActive && ourEnded && copyToGoalM > kNoPathM; }
// After a refusal or a no-path the copy rests on WO-215's walk-to-sample this long
constexpr double kReplayRestS = 5.0;
// The host's request ended (or was cancelled) while the copy's still runs: the copy's is cancelled unless it is this close to the goal
constexpr float kLetFinishM = 0.6f;
inline bool cancel_on_host_end(bool ourActive, float copyToGoalM) { return ourActive && copyToGoalM > kLetFinishM; }

// ---- A.1 the corrector's way: the host NPC's own path over its last 1.2 s (a point every 0.05 s, the newest = the host now). A copy
// replaying its host's request a moment later is on its way while it is near any of it; the corrector acts when it is off that way
// (b6: against the host's newest point alone, a copy 0.8 s behind on the same path was corrected 16.8 times a body-minute)
constexpr int kWayPoints = 24;
constexpr double kWayStepS = 0.05;
constexpr double kWayGapS = 0.3;   // the way is begun again after a gap this long (the copy left the replay, the stream was quiet)
inline float seg_dist2d(const float p[2], const float a[2], const float b[2]) {
    const float vx = b[0] - a[0], vy = b[1] - a[1], wx = p[0] - a[0], wy = p[1] - a[1];
    const float vv = vx * vx + vy * vy;
    float t = vv > 1e-8f ? (wx * vx + wy * vy) / vv : 0.0f;
    t = t < 0.0f ? 0.0f : (t > 1.0f ? 1.0f : t);
    const float dx = wx - t * vx, dy = wy - t * vy;
    return std::sqrt(dx * dx + dy * dy);
}
// pts: the way's points, oldest first (x, y); the distance from p to the nearest of its segments (one point: to that point)
inline float way_off(const float p[2], const float (*pts)[2], int n) {
    if (n <= 0) return 0.0f;
    if (n == 1) { const float dx = p[0] - pts[0][0], dy = p[1] - pts[0][1]; return std::sqrt(dx * dx + dy * dy); }
    float best = 1e30f;
    for (int i = 0; i + 1 < n; ++i) { const float d = seg_dist2d(p, pts[i], pts[i + 1]); if (d < best) best = d; }
    return best;
}

// ---- A.2 path parity: a route's mean lateral offset (the copy to the host NPC's way), in 10 cm bins up to 3 m
constexpr int kParityBins = 31;
inline int parity_bin(float meanM) { int b = static_cast<int>(meanM * 10.0f); return b < 0 ? 0 : (b >= kParityBins ? kParityBins - 1 : b); }

// ---- B no brain runs on the joiner -----------------------------------------------------------------------------------------------------
// The census once a second: every NPC body here with a running brain (state 0) is a breach -- named, suspended at once. A body the
// host does not stream (unbound) is frozen and hidden; a bound copy is moved by A and the owner.
constexpr double kBrainCensusS = 1.0;
enum class Body : uint8_t { Figure = 0, Bound = 1, Unbound = 2, Dead = 3 };
inline bool breach(int brainState) { return brainState == 0; }
// what to do with a body found running: suspend it always; hide it unless the host streams it
inline bool hide_unbound(bool bound, bool figure) { return !bound && !figure; }
// The stream carries names of at most 63 characters (the animal care's horses are "horse[animalcare/<name>_<guid>]", 65): a body whose name
// is longer can never be a copy -- it stays shown here (suspended, frozen), never hidden while the host shows its own
constexpr size_t kStreamNameMax = 63;
inline bool streamable_name(size_t nameLen) { return nameLen > 0 && nameLen <= kStreamNameMax; }

} // namespace kcdmp::wo216rules
