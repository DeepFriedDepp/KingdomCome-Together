// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-203B area "cam": engine-free checks of the conversation camera's rules (wo203_cam_rules.h). Called from wo203_tests.cpp.
#include <cmath>
#include <cstdio>

#include "wo203_cam_rules.h"

using namespace kcdmp::wo203cam;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

bool near(float a, float b, float e = 1e-3f) { return std::fabs(a - b) <= e; }
bool same(const Pose& a, const Pose& b, float m = 1e-3f, float deg = 0.05f) {
    return dist(a.p, b.p) <= m && qangle_deg(a.q, b.q) <= deg && near(a.fov, b.fov, 1e-3f);
}
Pose pose(float x, float y, float z, float yawDeg, float fov = 1.0f) {
    Pose p;
    p.p[0] = x; p.p[1] = y; p.p[2] = z;
    p.q = qyaw(yawDeg * 0.0174533f);
    p.fov = fov;
    return p;
}
// An engine Matrix34 (row-major) of a pose: the rotation's columns, the position in column 3.
void m34(const Pose& p, float m[12]) {
    const float ax[3][3] = {{1, 0, 0}, {0, 1, 0}, {0, 0, 1}};
    float c[3][3];
    for (int i = 0; i < 3; ++i) qrot(p.q, ax[i], c[i]);
    for (int r = 0; r < 3; ++r) {
        for (int k = 0; k < 3; ++k) m[r * 4 + k] = c[k][r];
        m[r * 4 + 3] = p.p[r];
    }
}
} // namespace

int wo203_cam_tests(int* passed) {
    // the matrix round trip: the engine's camera matrix -> the pose it holds
    {
        Pose p = pose(10, 20, 30, 37);
        p.q = qnorm(qmul(p.q, Quat{std::sin(0.2f), 0, 0, std::cos(0.2f)}));   // pitched down a little too
        float m[12];
        m34(p, m);
        const Pose back = pose_from_camera(m, 1.0f);
        WCHECK(same(back, p), "a camera matrix gives back its own position and rotation");
        const Pose flipped = pose(0, 0, 0, 179.0f);
        m34(flipped, m);
        WCHECK(qangle_deg(pose_from_camera(m, 1).q, flipped.q) < 0.05f, "the trace-negative branch (yaw near 180) round-trips");
        const Pose ent = pose(1, 2, 3, 90);
        m34(ent, m);
        WCHECK(near(yaw_from_m34(m), 1.5708f), "an entity facing +90 degrees reads yaw +90 degrees");
    }
    // relative / world: anchor^-1 * camera, then anchor * relative
    {
        const float a[3] = {100, 200, 50};
        const Pose cam = pose(101, 203, 51.7f, 200, 0.9f);
        const Pose rel = relative(cam, a, 0.7f);
        WCHECK(same(world(rel, a, 0.7f), cam), "world(relative(cam)) is the camera");
        const float b[3] = {-5, 7, 3};
        const Pose moved = world(rel, b, 0.7f);
        WCHECK(near(moved.p[0] - b[0], cam.p[0] - a[0]) && near(moved.p[1] - b[1], cam.p[1] - a[1]) && near(moved.p[2] - b[2], cam.p[2] - a[2]),
               "the same shot of an anchor standing elsewhere keeps its offset to the anchor");
        WCHECK(qangle_deg(moved.q, cam.q) < 0.05f, "and its rotation (same anchor yaw)");
        const Pose turned = world(rel, a, 0.7f + 1.5708f);
        WCHECK(near(qangle_deg(turned.q, cam.q), 90.0f, 0.05f), "an anchor turned 90 degrees turns the shot 90 degrees with it");
        WCHECK(near(dist(turned.p, a), dist(cam.p, a)), "at the same distance from the anchor");
    }
    // the cut rule: 0.6 m or 25 degrees
    {
        WCHECK(!is_jump(pose(0, 0, 0, 0), pose(0.59f, 0, 0, 0)), "0.59 m is a move");
        WCHECK(is_jump(pose(0, 0, 0, 0), pose(0.61f, 0, 0, 0)), "0.61 m is a cut");
        WCHECK(!is_jump(pose(0, 0, 0, 0), pose(0, 0, 0, 24.5f)), "24.5 degrees is a move");
        WCHECK(is_jump(pose(0, 0, 0, 0), pose(0, 0, 0, 25.5f)), "25.5 degrees is a cut");
    }
    // the play-out
    {
        Sample s[4];
        s[0] = {1000, pose(0, 0, 0, 0, 1.10f), false};
        s[1] = {1100, pose(0.2f, 0, 0, 10, 0.70f), false};
        s[2] = {1200, pose(5, 0, 0, 90, 0.9f), true};          // a cut
        s[3] = {1300, pose(5.1f, 0, 0, 92, 0.9f), false};
        Pose o;
        WCHECK(play(s, 0, 1000, &o) == Play::None, "no samples: nothing");
        WCHECK(play(s, 4, 900, &o) == Play::Hold && same(o, s[0].pose), "before the first sample: the first pose");
        WCHECK(play(s, 4, 1050, &o) == Play::Interp && near(o.p[0], 0.1f) && near(o.fov, 0.90f) && near(qangle_deg(o.q, s[0].pose.q), 5.0f, 0.01f),
               "half way: linear position, slerped rotation, linear FOV");
        WCHECK(play(s, 4, 1150, &o) == Play::Snap && same(o, s[1].pose), "across the cut: the shot before holds until the cut's time");
        WCHECK(play(s, 4, 1200, &o) == Play::Interp && same(o, s[2].pose), "at the cut's time: the new shot, at once");
        Sample j[2] = {{0, pose(0, 0, 0, 0), false}, {100, pose(0, 0, 0, 40), false}};   // a 40 degree jump with no cut flag
        WCHECK(play(j, 2, 50, &o) == Play::Snap && same(o, j[0].pose), "a jump with no flag is a cut too");
        WCHECK(play(s, 4, 1350, &o) == Play::Extrap && near(o.p[0], 5.15f) && near(qangle_deg(o.q, s[3].pose.q), 1.0f, 0.02f),
               "50 ms past the newest: extrapolated along the last move");
        Pose a, b;
        WCHECK(play(s, 4, 1300 + kExtrapMs, &a) == Play::Extrap && play(s, 4, 1300 + kExtrapMs + 500, &b) == Play::Hold && same(a, b),
               "past 150 ms: holds where the extrapolation stopped (never drifts, never jumps back)");
        Sample c[2] = {{0, pose(0, 0, 0, 0), false}, {100, pose(3, 0, 0, 0), true}};
        WCHECK(play(c, 2, 180, &o) == Play::Hold && same(o, c[1].pose), "never extrapolated past a cut");
        Sample one[1] = {{0, pose(1, 2, 3, 4), false}};
        WCHECK(play(one, 1, 500, &o) == Play::Hold && same(o, one[0].pose), "one sample: held");
    }
    // B.2 constants
    WCHECK(kDelayMs == 100 && kExtrapMs == 150 && kSilenceMs == 3000, "the WO's 100 ms, 150 ms and 3 s");
    // B.6: the starter's own first-person camera
    {
        const float feet[3] = {10, 10, 5};
        const float eye[3] = {10.1f, 10.05f, 6.7f}, ots[3] = {10.6f, 10.4f, 6.6f}, low[3] = {10.0f, 10.0f, 5.5f};
        WCHECK(in_player_head(eye, feet), "the camera in the player's head is his own view");
        WCHECK(!in_player_head(ots, feet), "an over-the-shoulder shot is not");
        WCHECK(!in_player_head(low, feet), "a camera at his knees is not");
    }
    *passed = g_pass;
    return g_fail;
}
