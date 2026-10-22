// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-203B: the pure rules of the listener's conversation camera (wo203_cam.cpp), header-only so the native tests run them.
//
//   * A pose is the camera's world position, rotation (a unit quaternion, the engine's: x, y, z, w; the view looks along its +Y)
//     and vertical field of view (radians).
//   * The anchor is the conversation's main NPC as a position and a yaw (rotation about +Z only -- its pitch and roll are body
//     noise). relative() = anchor^-1 * camera on the starter; world() = anchor * relative on the listener, with the listener's own
//     copy of the anchor, so the shot frames the copy wherever it stands.
//   * A cut is the starter's cut flag, or a jump between two consecutive samples of more than 0.6 m or 25 degrees: never
//     interpolated across.
//   * play() is the play-out at one local time: between two samples linear position / slerp rotation / linear FOV; across a cut the
//     earlier shot holds until the later sample's time, then snaps; past the newest sample it extrapolates at most kExtrapMs, then
//     holds the last pose (never drifts).
#pragma once
#include <cmath>
#include <cstdint>

namespace kcdmp::wo203cam {

constexpr float kCutMetres = 0.6f;
constexpr float kCutDegrees = 25.0f;
constexpr int64_t kDelayMs = 100;      // B.2: the play-out runs this far behind the starter's clock
constexpr int64_t kExtrapMs = 150;     // B.2: at most this far past the newest sample
constexpr int64_t kSilenceMs = 3000;   // B.5: no sample for this long -> the player's own camera comes back

struct Quat { float x = 0, y = 0, z = 0, w = 1; };

struct Pose {
    float p[3] = {0, 0, 0};
    Quat q{};
    float fov = 1.0f;
};

struct Sample {
    int64_t tMs = 0;   // the local clock (the agent maps the starter's host-clock time onto this machine's)
    Pose pose{};
    bool cut = false;
    float anchor[4] = {0, 0, 0, 0};   // the STARTER's anchor (x, y, z, yaw) when the sample was taken -- B.3's framing check
};

constexpr float kFrameOffsetMetres = 0.5f;   // B.3: the listener's copy of the anchor this far from the starter's -> logged

inline Quat qmul(const Quat& a, const Quat& b) {
    return {a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z};
}

inline Quat qconj(const Quat& q) { return {-q.x, -q.y, -q.z, q.w}; }

inline float qdot(const Quat& a, const Quat& b) { return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w; }

inline Quat qnorm(const Quat& q) {
    const float n = std::sqrt(qdot(q, q));
    if (!(n > 1e-6f)) return Quat{};
    return {q.x / n, q.y / n, q.z / n, q.w / n};
}

inline void qrot(const Quat& q, const float v[3], float out[3]) {
    const Quat r = qmul(qmul(q, Quat{v[0], v[1], v[2], 0}), qconj(q));
    out[0] = r.x; out[1] = r.y; out[2] = r.z;
}

inline Quat qyaw(float yaw) { return {0, 0, std::sin(yaw * 0.5f), std::cos(yaw * 0.5f)}; }

// The angle between two rotations, degrees (0..180).
inline float qangle_deg(const Quat& a, const Quat& b) {
    float d = std::fabs(qdot(qnorm(a), qnorm(b)));
    if (d > 1.0f) d = 1.0f;
    return 2.0f * std::acos(d) * 57.29578f;
}

// t in [0,1] interpolates; t > 1 extrapolates along the same arc (the play-out's short extrapolation).
inline Quat qslerp(const Quat& a0, const Quat& b0, float t) {
    const Quat a = qnorm(a0);
    Quat b = qnorm(b0);
    float d = qdot(a, b);
    if (d < 0) { b = {-b.x, -b.y, -b.z, -b.w}; d = -d; }
    if (d > 0.9995f)
        return qnorm({a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t});
    const float th = std::acos(d), s = std::sin(th);
    const float wa = std::sin((1 - t) * th) / s, wb = std::sin(t * th) / s;
    return qnorm({a.x * wa + b.x * wb, a.y * wa + b.y * wb, a.z * wa + b.z * wb, a.w * wa + b.w * wb});
}

// The rotation of an engine Matrix34 (row-major 3x4: m[r*4+c]; translation in column 3).
inline Quat qfrom_m34(const float m[12]) {
    const float m00 = m[0], m01 = m[1], m02 = m[2], m10 = m[4], m11 = m[5], m12 = m[6], m20 = m[8], m21 = m[9], m22 = m[10];
    const float tr = m00 + m11 + m22;
    Quat q;
    if (tr > 0) {
        const float s = std::sqrt(tr + 1.0f) * 2;
        q = {(m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s};
    } else if (m00 > m11 && m00 > m22) {
        const float s = std::sqrt(1.0f + m00 - m11 - m22) * 2;
        q = {0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s};
    } else if (m11 > m22) {
        const float s = std::sqrt(1.0f + m11 - m00 - m22) * 2;
        q = {(m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s};
    } else {
        const float s = std::sqrt(1.0f + m22 - m00 - m11) * 2;
        q = {(m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s};
    }
    return qnorm(q);
}

// The yaw of an entity's world matrix: its forward (+Y column) projected on the ground.
inline float yaw_from_m34(const float m[12]) { return std::atan2(-m[1], m[5]); }

inline Pose pose_from_camera(const float m[12], float fov) {
    Pose p;
    p.p[0] = m[3]; p.p[1] = m[7]; p.p[2] = m[11];
    p.q = qfrom_m34(m);
    p.fov = fov;
    return p;
}

inline Pose relative(const Pose& cam, const float anchor[3], float yaw) {
    Pose r;
    const Quat inv = qyaw(-yaw);
    const float d[3] = {cam.p[0] - anchor[0], cam.p[1] - anchor[1], cam.p[2] - anchor[2]};
    qrot(inv, d, r.p);
    r.q = qnorm(qmul(inv, cam.q));
    r.fov = cam.fov;
    return r;
}

inline Pose world(const Pose& rel, const float anchor[3], float yaw) {
    Pose w;
    const Quat a = qyaw(yaw);
    float d[3];
    qrot(a, rel.p, d);
    w.p[0] = anchor[0] + d[0]; w.p[1] = anchor[1] + d[1]; w.p[2] = anchor[2] + d[2];
    w.q = qnorm(qmul(a, rel.q));
    w.fov = rel.fov;
    return w;
}

inline float dist(const float a[3], const float b[3]) {
    const float dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
    return std::sqrt(dx * dx + dy * dy + dz * dz);
}

inline bool is_jump(const Pose& a, const Pose& b) {
    return dist(a.p, b.p) > kCutMetres || qangle_deg(a.q, b.q) > kCutDegrees;
}

inline Pose lerp(const Pose& a, const Pose& b, float t) {
    Pose o;
    for (int i = 0; i < 3; ++i) o.p[i] = a.p[i] + (b.p[i] - a.p[i]) * t;
    o.q = qslerp(a.q, b.q, t);
    o.fov = a.fov + (b.fov - a.fov) * t;
    return o;
}

enum class Play : uint8_t { None = 0, Interp, Snap, Extrap, Hold };

inline const char* play_name(Play p) {
    switch (p) {
        case Play::Interp: return "interp";
        case Play::Snap: return "snap";
        case Play::Extrap: return "extrap";
        case Play::Hold: return "hold";
        default: return "none";
    }
}

// s[0..n) ascending by tMs. The pose at local time t (already the play-out time: now - kDelayMs).
inline Play play(const Sample* s, int n, int64_t t, Pose* out) {
    if (!s || n <= 0 || !out) return Play::None;
    if (t <= s[0].tMs) { *out = s[0].pose; return Play::Hold; }
    for (int i = 0; i + 1 < n; ++i) {
        const Sample& a = s[i];
        const Sample& b = s[i + 1];
        if (t >= b.tMs) continue;
        if (b.cut || is_jump(a.pose, b.pose) || b.tMs <= a.tMs) { *out = a.pose; return Play::Snap; }
        *out = lerp(a.pose, b.pose, float(t - a.tMs) / float(b.tMs - a.tMs));
        return Play::Interp;
    }
    const Sample& last = s[n - 1];
    const int64_t past = t - last.tMs;
    if (n >= 2 && past <= kExtrapMs) {
        const Sample& prev = s[n - 2];
        const int64_t span = last.tMs - prev.tMs;
        if (span > 0 && !last.cut && !is_jump(prev.pose, last.pose)) {
            *out = lerp(prev.pose, last.pose, 1.0f + float(past) / float(span));
            return Play::Extrap;
        }
    }
    if (n >= 2 && past > kExtrapMs) {
        // Hold where the extrapolation stopped, so the view does not jump back when it ends.
        const Sample& prev = s[n - 2];
        const int64_t span = last.tMs - prev.tMs;
        if (span > 0 && !last.cut && !is_jump(prev.pose, last.pose)) {
            *out = lerp(prev.pose, last.pose, 1.0f + float(kExtrapMs) / float(span));
            return Play::Hold;
        }
    }
    *out = last.pose;
    return Play::Hold;
}

// B.6: the starter's camera sits in its own player's head (the normal first-person view, not a dialogue shot): within this
// horizontal distance of the player's position and this band above his feet.
inline bool in_player_head(const float cam[3], const float feet[3]) {
    const float dx = cam[0] - feet[0], dy = cam[1] - feet[1], dz = cam[2] - feet[2];
    return std::sqrt(dx * dx + dy * dy) < 0.30f && dz > 1.2f && dz < 2.0f;
}

} // namespace kcdmp::wo203cam
