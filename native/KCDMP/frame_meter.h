// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-151 Phase 0.4: the frame rate in the native log (docs/WO-148-findings.md s7.12 item 5).
//
// The main-thread hook runs once a frame. Every 60 s it writes one FRAME line: the frame
// count, the frame rate, the mean and worst frame time, the frames slower than 50 ms (under
// 20 FPS), and what this DLL's own work after the game's update cost. A tester's Report Bug
// zip carries the native log, so a decline shows in the field even when nobody noticed it
// (0.42.5-0.42.7 fell from 75 to single digits over minutes with no line anywhere).
//
// Pure arithmetic: the native tests drive it with a fake clock.

#include <cstddef>
#include <cstdint>
#include <cstdio>

namespace kcdmp::framemeter {

constexpr double kPeriodUs = 60e6;
constexpr double kSlowFrameUs = 50e3;

struct Window {
    double   startUs = 0;        // the window's first frame (0 = nothing yet)
    double   lastUs = 0;         // the previous frame
    uint32_t frames = 0;         // frame intervals measured in this window
    double   sumFrameUs = 0, worstFrameUs = 0;
    double   sumOursUs = 0, worstOursUs = 0;
    uint32_t slow = 0;
};

// One frame at nowUs (when the hook ran), oursUs what this DLL's work cost in it.
// True when the window has reached kPeriodUs: format() it, then restart().
inline bool add(Window& w, double nowUs, double oursUs) {
    if (w.startUs == 0) { w.startUs = nowUs; w.lastUs = nowUs; return false; }
    const double dt = nowUs - w.lastUs;
    w.lastUs = nowUs;
    if (dt >= 0) {
        ++w.frames;
        w.sumFrameUs += dt;
        if (dt > w.worstFrameUs) w.worstFrameUs = dt;
        if (dt > kSlowFrameUs) ++w.slow;
    }
    w.sumOursUs += oursUs;
    if (oursUs > w.worstOursUs) w.worstOursUs = oursUs;
    return nowUs - w.startUs >= kPeriodUs;
}

inline double fps(const Window& w) {
    return w.sumFrameUs > 0 ? w.frames * 1e6 / w.sumFrameUs : 0.0;
}

// The FRAME line (without the fault totals, which the caller appends).
inline int format(const Window& w, char* out, size_t cap) {
    const double f = w.frames ? static_cast<double>(w.frames) : 1.0;
    return std::snprintf(out, cap,
        "FRAME window_s=%.1f frames=%u fps=%.1f frame_ms_mean=%.1f frame_ms_worst=%.1f slow_frames=%u "
        "ours_us_mean=%.0f ours_us_max=%.0f",
        w.sumFrameUs / 1e6, w.frames, fps(w), w.sumFrameUs / f / 1e3, w.worstFrameUs / 1e3, w.slow,
        w.sumOursUs / f, w.worstOursUs);
}

// A new window starting at the frame that closed the last one.
inline void restart(Window& w) {
    const double last = w.lastUs;
    w = Window{};
    w.startUs = last;
    w.lastUs = last;
}

} // namespace kcdmp::framemeter
