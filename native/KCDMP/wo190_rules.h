// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-190: the engine-free rules of the native half (wo190.cpp, wo166.cpp, npc_drive.cpp). native/tests/wo190_tests.cpp pins them.
//   0.1 the animation trace: which bodies, how often a body is read, how many lines a body and the whole trace may write
//   A1  the figure's guard idle through reactions: an empty full body on a fighting body gets its guard idle back, at most once a second;
//       the stand-in's weapon is the one the body holds (its weapon classes, mapped to the rows' hand tags)
//   A3  re-queues on a copy: the same request repeated is never re-sent, and a copy's animation requests are rate-limited
//   A4  gliding in an idle: through a short gap in a body's stream (<= 0.5 s) its last gait and speed are kept
//   A6  a copy still in its own last swing (0x20 withdraw, 0x40) takes the next one: the leftover ends through the engine's state setter;
//       a real engine reaction to a blow on the copy itself is never cut off

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo190rules {

// ---- 0.1 the trace ------------------------------------------------------------------------------------------------------------
// The mode: off; auto = the bodies in a fight only (a figure whose partner is in combat, a copy whose host NPC fights), rate-limited --
// mp_anim_scope_log's new default, so a field bundle shows what a body had installed at a bad pose; on = every traced body (mp_anim_trace).
enum class Mode : uint8_t { Off = 0, Auto = 1, On = 2 };
inline const char* mode_name(Mode m) { return m == Mode::On ? "on" : m == Mode::Auto ? "auto" : "off"; }
inline Mode mode_from_byte(uint8_t b) { return b == 2 ? Mode::On : b == 1 ? Mode::Auto : Mode::Off; }

enum class Role : uint8_t { Player = 0, Figure = 1, Copy = 2 };
inline const char* role_name(Role r) { return r == Role::Player ? "player" : r == Role::Figure ? "figure" : "copy"; }

constexpr int kMaxBodies = 24;          // bodies read per pass (the nearest first)
constexpr float kNearM = 40.0f;         // copies farther than this from the local player are not traced
constexpr double kReadPeriodS = 0.033;  // a body's scopes are read at most this often (~30 Hz: a change shorter than a frame or two is missed)
constexpr double kAutoReadPeriodS = 0.05;
constexpr int kMaxScopes = 12;          // scopes read per body (the controller's own count, capped)
// Lines: a body may write this many per second (a burst allowance of kBodyBurst), the whole trace this many per minute; beyond, counted.
constexpr double kBodyLinesPerS = 12.0, kBodyBurst = 24.0;
constexpr double kAutoBodyLinesPerS = 4.0, kAutoBodyBurst = 12.0;
constexpr int kTraceLinesPerMin = 6000, kAutoLinesPerMin = 600;

// A token bucket per body: `rate` lines a second, at most `burst` at once.
struct Bucket {
    double tokens = -1, at = 0;
    bool take(double now, double rate, double burst) {
        if (tokens < 0) { tokens = burst; at = now; }
        tokens += (now - at) * rate;
        if (tokens > burst) tokens = burst;
        at = now;
        if (tokens < 1.0) return false;
        tokens -= 1.0;
        return true;
    }
};

// One minute's cap for the whole trace.
struct MinuteCap {
    double start = -1; int n = 0, dropped = 0;
    bool take(double now, int cap) {
        if (start < 0 || now - start >= 60.0) { start = now; n = 0; }
        if (n >= cap) { ++dropped; return false; }
        ++n;
        return true;
    }
};

// What changed on a scope since the last read (the line is written on any of these).
struct ScopeSnap { bool ok = false; bool empty = true; const void* action = nullptr; uint32_t frag = 0, flags = 0; int32_t prio = 0; };
inline bool scope_changed(const ScopeSnap& a, const ScopeSnap& b) {
    if (a.ok != b.ok || a.empty != b.empty) return true;
    if (a.empty) return false;
    return a.action != b.action || a.frag != b.frag || a.prio != b.prio;
}

// ---- A1: the guard idle through reactions -----------------------------------------------------------------------------------
// A fighting body (a figure whose partner is in combat, a copy held in its guard) whose full-body scope stays empty this long gets its guard
// idle re-queued (the engine's own UpdateMovementAnim re-queues CombatIdle on a guard-zone change: WO-187's remedy), at most once per
// kIdleWatchRepeatS. WO-187's restore ran only once per move and only 4 s after a move it was sent; a reaction the engine played itself (a
// blow on the figure) or an empty pose that came later was never caught: the field's T-pose.
constexpr double kIdleWatchEmptyS = 0.25;
constexpr double kIdleWatchRepeatS = 1.0;
constexpr double kIdleWatchPeriodS = 0.05;
inline bool idle_watch_due(bool on, bool fighting, bool scopeEmpty, double emptyForS, double sinceLastRequeueS) {
    return on && fighting && scopeEmpty && emptyForS >= kIdleWatchEmptyS && (sinceLastRequeueS < 0 || sinceLastRequeueS >= kIdleWatchRepeatS);
}

// A1: the weapon a body holds -> the hand tags of the game's reaction rows. Weapon class ids are the game's own table (weapon_class: 0 dagger,
// 1 sword, 2 sabre, 3 axe, 4 longsword, 5 mace, 6 flail, 7 halberd, 8 shield, 9 bow, 11 torch, 12 unarmed, 16 hunting sword, 17 broken shield).
// right = the right hand's class (-1 unreadable), left = the other hand's (-1 none). Writes "r_x+l_y" (or "" when unknown) and returns
// whether the body is armed (a weapon row may play; a bare-handed reaction would die at once -- or, worse, play on a body that holds a sword).
inline bool is_shield_class(int c) { return c == 8 || c == 17; }
inline const char* right_tag(int c) {
    switch (c) {
        case 1: case 2: case 16: return "r_swords";
        case 0: return "r_shortSwords";
        case 3: case 5: case 6: return "r_bluntWeapon";
        case 4: return "r_longsword";
        case 7: return "r_halberd";
        case 12: return "r_noweapon";
        default: return nullptr;   // a torch, a bow, unknown: no reaction row names it
    }
}
// 0 = unknown, 1 = armed, 2 = bare hands
inline int hand_tags(int right, int left, char* out, size_t cap) {
    if (cap) out[0] = 0;
    if (is_shield_class(right)) { const int t = right; right = left; left = t; }   // the shield read first: the weapon is the other hand
    const char* r = right_tag(right);
    if (!r) return 0;
    const char* l = right == 4 ? "l_longsword" : right == 7 ? "l_halberd" : is_shield_class(left) ? "l_shield" : "l_noShield";
    size_t n = std::strlen(r) + 1 + std::strlen(l);
    if (n + 1 > cap) return 0;
    std::memcpy(out, r, std::strlen(r));
    out[std::strlen(r)] = '+';
    std::memcpy(out + std::strlen(r) + 1, l, std::strlen(l) + 1);
    return right == 12 ? 2 : 1;
}

// ---- A3: re-queues on a paused or idle copy ------------------------------------------------------------------------------------
// The joiner's paused copies overflowed the engine's animation queue (WO-183: 564 overflows on a joiner vs 27 on the host; WO-191's field
// bundle 1,108 vs 15): a request repeated every frame with the same value re-queues the same fragment, and a value that flips every frame
// (relaxed <-> crouched, walk <-> turn) queues a new one each time. A request is re-sent only when it differs from the last one sent, and a
// copy gets at most kRequeueMaxPerS changes a second; a held change is sent when the window allows.
constexpr double kRequeueMaxPerS = 4.0;
constexpr double kRequeueBurst = 4.0;
struct RequeueGate {
    int last = -0x7fffffff; double lastAt = -1; Bucket bucket; uint32_t deduped = 0, limited = 0;
    // true = send `v` now
    bool allow(int v, double now) {
        if (v == last) { ++deduped; return false; }
        if (!bucket.take(now, kRequeueMaxPerS, kRequeueBurst)) { ++limited; return false; }
        last = v; lastAt = now;
        return true;
    }
};

// A4/A3: a planar speed this small (m/s) is standing still -- a few millimetres a frame is the stream's own noise (WO-183's innkeeper: 3 mm
// steps queued walk / strafe-back / turn blendspaces every frame), not a walk.
constexpr float kStillMps = 0.12f;
// Hysteresis on the walk class: start moving above kStillMps * 2, stop below kStillMps.
inline bool moving_with_hysteresis(bool wasMoving, float mps) { return wasMoving ? mps >= kStillMps : mps >= 2.0f * kStillMps; }

// ---- A4: gliding in an idle ---------------------------------------------------------------------------------------------------
// The gap is the time the body's render has stood past its stream's newest sample (no newer sample to move toward). Through a gap a body
// keeps its last gait and speed (a packet late, a sender timer late) instead of decaying to idle and then gliding through the catch-up;
// a source that says it stopped stops at once. A figure's partner says so (his state block's speed 0), so a figure holds up to kGaitHoldS;
// the host's NPC stream is moved-gated (an NPC that stops sends nothing until its 2 s heartbeat), so a copy's stop looks like a gap and a
// copy holds only kCopyGaitHoldS (about two of its samples) -- longer would walk it on the spot at every stop.
constexpr double kGaitHoldS = 0.5;
constexpr double kCopyGaitHoldS = 0.25;
// The speed the body's gait is written with: the rendered speed while it renders motion; through a gap up to limitS the last moving speed
// (when the source was moving); after it the rendered speed again (0 when it stopped).
inline float gait_speed(float renderedMps, float lastMovingMps, double gapS, bool sourceStopped, double limitS = kGaitHoldS) {
    if (sourceStopped) return renderedMps;
    if (gapS >= 0 && gapS <= limitS && renderedMps < kStillMps && lastMovingMps >= 2.0f * kStillMps) return lastMovingMps;
    return renderedMps;
}
// A body that visibly moves never plays its idle: the smoothed speed (the gait's own noise filter) lags a start or a catch-up by a quarter
// second -- the body moved in its idle meanwhile. The rendered speed lifts it to a walk as soon as it shows one, never further: a catch-up
// after a gap renders two or three times the source's pace, and a run there is a move the source never made (live: 10 run flaps on a walking
// figure, 31 class changes for 20 starts and stops of a copy, before the cap).
constexpr float kFloorCapMps = 1.4f;   // a walk (gait_logic.h: the walk class is 0.1 .. 2.4 m/s)
inline float moving_floor(float smoothedMps, float renderedMps, bool snap) {
    if (snap || renderedMps < 2.0f * kStillMps) return smoothedMps;
    const float lift = renderedMps < kFloorCapMps ? renderedMps : kFloorCapMps;
    return lift > smoothedMps ? lift : smoothedMps;
}

// ---- A6: a copy still in its own last swing -----------------------------------------------------------------------------------
// The copy states: 0x20 the withdraw after a swing (the attacker's own after-contact phase), 0x40 Hit. The 0.60.7 joiner refused 15 of 34
// guard swings a wind-up because the copy read 0x20 or 0x40 from the previous swing -- no wind-up, no shield window, no parry.
constexpr int32_t kStateWithdraw = 0x20, kStateHitState = 0x40;
// Whether the new swing may end the leftover state: the copy reads 0x20 or 0x40 and nothing says it is a real engine reaction to a blow on
// it (its director runs a Hit action, or the local player hit it within kRealHitS). on = mp_copy_swing_takeover.
constexpr double kRealHitS = 1.2;
inline bool leftover_endable(bool on, int32_t cur, bool directorIsHit, bool hitByPlayerRecently) {
    if (!on) return false;
    if (cur != kStateWithdraw && cur != kStateHitState) return false;
    return !directorIsHit && !hitByPlayerRecently;
}
// The director's action name (RTTI, without "C_CombatActorAction") that is a real reaction to a blow.
inline bool director_is_reaction(const char* n) {
    if (!n || !*n) return false;
    return std::strncmp(n, "Hit", 3) == 0 || std::strstr(n, "Stagger") != nullptr || std::strstr(n, "Knock") != nullptr ||
           std::strstr(n, "Death") != nullptr;
}

} // namespace kcdmp::wo190rules
