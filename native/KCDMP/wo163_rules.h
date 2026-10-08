// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-163: the pure (engine-free) rules of the shared-combat build, unit-tested in native/tests/wo163_tests.cpp.
// The design source is research/WO-162/combat-RE.md; each rule cites its section.
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo163 {

// ---- A1: where a committed sync attack keeps its row's GUID (WO-162 Q3.1 / Q3.3) -------------------------------
// The descriptor is reached through the action's own pointer at action+0x60 (unchanged). A committed attack keeps its row GUID at
// descriptor +0x84 (live, WO-121); a sync attack's row object (RTTI S_CombatActionSyncAttackData) keeps it at +0x7C -- 16 of 16
// field dumps hold a combat_action_sync_attack GUID exactly there (24 of 24 distinct ones in every bundle on disk), and the
// +0x84 read was half of that GUID plus 8 bytes of the next field, which is in no table. The legacy read is kept as a check:
// the agent logs if BOTH reads hit its catalog (they never should).
constexpr size_t kSyncGuidOff = 0x7C;
constexpr size_t kSyncGuidLegacyOff = 0x84;

// The GUID a sync attack's descriptor holds, read from a copy of it (a field dump in a test, or the guarded reads' result).
inline bool sync_guid_from(const uint8_t* desc, size_t len, size_t off, uint8_t out[16]) {
    if (!desc || off + 16 > len) return false;
    std::memcpy(out, desc + off, 16);
    return true;
}
inline bool guid_nonzero(const uint8_t g[16]) {
    for (int i = 0; i < 16; ++i) if (g[i]) return true;
    return false;
}

// ---- A2: the perfect-block class of an NPC is captured as a swing's source (WO-162 Q3.3) ---------------------------
// An NPC's PerfectBlock-class action (flag bit 0 of the captured frame) is sent to the agent, which decides by the row's table
// whether it is a master strike (a swing) or the block itself (dropped). The class constant lives in motion.cpp.
constexpr uint8_t kFrameFlagPerfectBlockClass = 0x01;

// ---- A5: MP-FIGHTSNAP (WO-162 Q6) --------------------------------------------------------------------------------
// One line per puppet per 10 s window, computed from values the writer already has: where the engine had the body (cur), where
// we set it (pose) and whether we wrote this frame. The applied position -- `wrote ? pose : cur` -- IS the render position (0.0 mm
// in all 129 390 traced frames, WO-162 Q6), so no render hook is needed.
//
// The definitions are tools/wo118/fight118.py's, in the horizontal plane (it measures x, y), so the line and the harness agree:
//   hold    = a run of >= kHoldMinFrames consecutive unwritten frames (the writer left the body to the engine); counted at its
//             first written frame, hold_ms = from the run's first frame to that one
//   resume  = at that first written frame: horizontal |pose - cur|  (fight118's "resume step")
//   post    = the largest horizontal frame-to-frame step of the applied position from the resume frame (its own step included)
//             to 1.0 s after it  (fight118's "largest render step in the next 1 s")
//   step    = the same step over the whole window;  corr = over written frames, 3D |pose - cur| (the correction), its 95th
//             percentile from a 64-bucket histogram
// The keeping rule a snap fix is judged by: the p90 of resume_max_cm and of post_hold_step_max_cm over a session must fall.
struct Vec3 { float x = 0, y = 0, z = 0; };
inline float dist3(const Vec3& a, const Vec3& b) {
    const float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
    return std::sqrt(dx * dx + dy * dy + dz * dz);
}
inline float dist_xy(const Vec3& a, const Vec3& b) {
    const float dx = a.x - b.x, dy = a.y - b.y;
    return std::sqrt(dx * dx + dy * dy);
}

constexpr int kHoldMinFrames = 5;
constexpr double kPostHoldS = 1.0;
constexpr int kCorrBuckets = 64;
constexpr float kCorrBucketCm = 2.0f;     // 0..126 cm in 2 cm buckets, the last one open-ended
constexpr float kSnapStepM = 5.0f;        // the old counter: a step over 5 m

struct FightSnapOut {
    unsigned frames = 0, fightFrames = 0, holds = 0, snapsGt5m = 0;
    double holdMsMax = 0;
    float corrMaxCm = 0, corrP95Cm = 0, resumeMaxCm = 0, resumeMeanCm = 0, stepMaxCm = 0, postHoldStepMaxCm = 0, blendMaxCm = 0;
};

class FightSnap {
public:
    // One frame. engaged = the copy is engaged or inside a swing hold / its blend; wrote = the writer set the body this frame.
    void frame(double now, const Vec3& cur, const Vec3& pose, bool wrote, bool engaged) {
        ++frames_;
        if (engaged) ++fight_;
        const bool resuming = wrote && run_ >= kHoldMinFrames;
        if (resuming) postUntil_ = now + kPostHoldS;     // fight118's window starts at the resume frame, its own step included
        const Vec3 applied = wrote ? pose : cur;
        if (haveLast_) {
            const float stepM = dist_xy(applied, last_);
            const float cm = stepM * 100.0f;
            stepMax_ = std::max(stepMax_, cm);
            if (stepM > kSnapStepM) ++snaps_;
            if (postUntil_ > 0 && now <= postUntil_) postMax_ = std::max(postMax_, cm);
        }
        if (wrote) {
            // The first written frame of a body (a bind: the writer puts a fresh copy on the stream, a teleport of tens of metres) or after a
            // non-fight hold is a placement, not a correction: it counts as a frame, not in the correction (live, the first window of a bind
            // read corr_max 65 m).
            // (live: a bind's teleport also reads as tens of metres for the two frames the physics body lags the write -- so a "correction" over
            // kSnapStepM is a placement too; a real snap shows in the applied step and snaps_gt5m)
            const float corrCm = dist3(pose, cur) * 100.0f;
            if (haveLast_ && corrCm <= kSnapStepM * 100.0f) {
                corrMax_ = std::max(corrMax_, corrCm);
                int b = static_cast<int>(corrCm / kCorrBucketCm);
                if (b >= kCorrBuckets) b = kCorrBuckets - 1;
                ++hist_[b]; ++corrN_;
            }
            if (resuming) {
                const float resumeCm = dist_xy(pose, cur) * 100.0f;
                ++holds_;
                holdMsMax_ = std::max(holdMsMax_, (now - runStart_) * 1000.0);
                resumeMax_ = std::max(resumeMax_, resumeCm);
                resumeSum_ += resumeCm; ++resumeN_;
            }
            run_ = 0;
        } else {
            if (run_ == 0) runStart_ = now;
            ++run_;
        }
        last_ = applied; haveLast_ = true;
    }

    // A hold that ended while the window was open is counted at its first written frame; one still running at the flush belongs
    // to the next window. The blend maximum is the writer's own (g_cost.blendMaxCm).
    FightSnapOut flush(float blendMaxCm) {
        FightSnapOut o;
        o.frames = frames_; o.fightFrames = fight_; o.holds = holds_; o.snapsGt5m = snaps_;
        o.holdMsMax = holdMsMax_; o.corrMaxCm = corrMax_; o.resumeMaxCm = resumeMax_;
        o.resumeMeanCm = resumeN_ ? static_cast<float>(resumeSum_ / resumeN_) : 0.0f;
        o.stepMaxCm = stepMax_; o.postHoldStepMaxCm = postMax_; o.blendMaxCm = blendMaxCm;
        if (corrN_) {
            const unsigned target = static_cast<unsigned>(std::ceil(0.95 * corrN_));
            unsigned acc = 0;
            for (int b = 0; b < kCorrBuckets; ++b) { acc += hist_[b]; if (acc >= target) { o.corrP95Cm = std::min((b + 1) * kCorrBucketCm, corrMax_); break; } }
        }
        reset_window();
        return o;
    }

    // Worth a line: the window held a hold, or the puppet was engaged in it, or a hold is running.
    bool worth_a_line() const { return holds_ > 0 || fight_ > 0 || run_ >= kHoldMinFrames; }
    unsigned frames_in_window() const { return frames_; }

    // A body that left or was re-bound starts clean (no step across a re-spawn).
    void forget() { reset_window(); haveLast_ = false; run_ = 0; postUntil_ = 0; }

    // The writer stopped owning the body for a reason that is not a fight (dead, carried, parented, an activity): no run, no step
    // across it, and what the window already holds stays.
    void break_run() { haveLast_ = false; run_ = 0; postUntil_ = 0; }

private:
    void reset_window() {
        frames_ = fight_ = holds_ = snaps_ = corrN_ = resumeN_ = 0;
        holdMsMax_ = 0; corrMax_ = resumeMax_ = stepMax_ = postMax_ = 0; resumeSum_ = 0;
        std::memset(hist_, 0, sizeof hist_);
    }
    unsigned frames_ = 0, fight_ = 0, holds_ = 0, snaps_ = 0, corrN_ = 0, resumeN_ = 0, hist_[kCorrBuckets] = {};
    double holdMsMax_ = 0, resumeSum_ = 0, runStart_ = 0, postUntil_ = 0;
    float corrMax_ = 0, resumeMax_ = 0, stepMax_ = 0, postMax_ = 0;
    int run_ = 0;
    Vec3 last_; bool haveLast_ = false;
};

} // namespace kcdmp::wo163
