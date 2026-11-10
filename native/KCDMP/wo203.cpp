// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-203's native registry (wo203.h): the areas' ops, installs, ticks and status lines.
#include "wo203.h"
#include "wo163.h"
#include "fault_guard.h"
#include "main_thread.h"
#include "log.h"
#include <windows.h>
#include <cstdio>
#include <cstring>

namespace kcdmp::wo203 {

namespace {

constexpr int kMaxEntries = 16;

struct Named { const char* area; VoidFn fn; };
struct NamedStatus { const char* area; StatusFn fn; };

// Function-local statics: an area's static initialiser may run before this file's own.
OpFn* ops() { static OpFn t[256] = {}; return t; }
Named* installs(int*& n) { static Named t[kMaxEntries] = {}; static int c = 0; n = &c; return t; }
Named* ticks(int*& n) { static Named t[kMaxEntries] = {}; static int c = 0; n = &c; return t; }
NamedStatus* statuses(int*& n) { static NamedStatus t[kMaxEntries] = {}; static int c = 0; n = &c; return t; }

bool g_installed = false;
fault::Site g_installSite{"wo203::install", fault::Kind::Call};

} // namespace

bool reg_op(uint8_t op, OpFn fn) {
    if (!fn || ops()[op]) return false;   // one owner per number
    ops()[op] = fn;
    return true;
}

bool reg_install(const char* area, VoidFn fn) {
    int* n = nullptr;
    Named* t = installs(n);
    if (!fn || *n >= kMaxEntries) return false;
    t[(*n)++] = {area, fn};
    return true;
}

bool reg_tick(const char* area, VoidFn fn) {
    int* n = nullptr;
    Named* t = ticks(n);
    if (!fn || *n >= kMaxEntries) return false;
    t[(*n)++] = {area, fn};
    return true;
}

bool reg_status(const char* area, StatusFn fn) {
    int* n = nullptr;
    NamedStatus* t = statuses(n);
    if (!fn || *n >= kMaxEntries) return false;
    t[(*n)++] = {area, fn};
    return true;
}

OffMainFn* off_main() { static OffMainFn t[256] = {}; return t; }

bool reg_op_off_main(uint8_t op, OffMainFn fn) {
    if (!fn || off_main()[op]) return false;
    off_main()[op] = fn;
    return true;
}

bool handle_off_main(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen, uint8_t* reason) {
    OffMainFn fn = off_main()[op];
    return fn && fn(op, body, len, out, cap, outLen, reason);
}

bool has_op(uint8_t op) { return op == kOpStatus || ops()[op] != nullptr; }

int status_text(char* out, int n) {
    if (!out || n <= 0) return 0;
    int k = std::snprintf(out, size_t(n), "wo203");
    int* c = nullptr;
    NamedStatus* t = statuses(c);
    for (int i = 0; i < *c && k > 0 && k < n - 2; ++i) {
        k += std::snprintf(out + k, size_t(n - k), " | %s: ", t[i].area);
        if (k >= n - 2) break;
        k += t[i].fn(out + k, n - k);
    }
    return k < n ? k : n - 1;
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    if (op == kOpStatus) {
        char t[480];
        int k = status_text(t, sizeof t);
        if (k < 0) k = 0;
        if (size_t(k) > cap) k = int(cap);
        std::memcpy(out, t, size_t(k));
        *outLen = size_t(k);
        return wo163::kROk;
    }
    OpFn fn = ops()[op];
    if (!fn) return wo163::kRBadRequest;
    return fn(op, body, len, out, cap, outLen);
}

void install() {
    if (g_installed) return;
    g_installed = true;
    int* n = nullptr;
    Named* t = installs(n);
    for (int i = 0; i < *n; ++i) {
        const Named e = t[i];
        const bool ok = fault::guarded(g_installSite, [&] { e.fn(); });
        if (!ok) logf("WO203 install of %s FAULTED -- that area stays off", e.area);
    }
    int* tn = nullptr;
    ticks(tn);
    logf("WO203 registry: %d install(s), %d tick(s)", *n, *tn);
}

void tick() {
    int* n = nullptr;
    Named* t = ticks(n);
    static bool s_blocked[64] = {};
    static bool s_read = false;
    if (!s_read) {   // WO-210 H.3 (research only): an area KCDMP_TEST_TASK_BLOCK names ("wo203:<area>") never ticks in this game
        s_read = true;
        for (int i = 0; i < *n && i < 64; ++i) {
            char nm[64];
            std::snprintf(nm, sizeof nm, "wo203:%s", t[i].area);
            if (main_thread::test_task_blocked(nm)) { s_blocked[i] = true; logf("WO203 tick %s blocked for this game (KCDMP_TEST_TASK_BLOCK, research)", t[i].area); }
        }
    }
    // WO-218 B.2: with the cost meter on, each area's time (the top ones every 10 s: WO218-COST)
    if (!main_thread::cost_meter()) { for (int i = 0; i < *n; ++i) if (i >= 64 || !s_blocked[i]) t[i].fn(); return; }
    static double s_sum[64] = {}, s_max[64] = {}, s_win = 0;
    static unsigned s_frames = 0;
    static const double usPerTick = [] { LARGE_INTEGER f; QueryPerformanceFrequency(&f); return 1e6 / static_cast<double>(f.QuadPart); }();
    auto now = [] { LARGE_INTEGER c; QueryPerformanceCounter(&c); return static_cast<double>(c.QuadPart) * usPerTick; };
    const double w0 = now();
    if (s_win == 0) s_win = w0;
    for (int i = 0; i < *n; ++i) {
        if (i < 64 && s_blocked[i]) continue;
        const double a = now();
        t[i].fn();
        if (i < 64) { const double d = now() - a; s_sum[i] += d; if (d > s_max[i]) s_max[i] = d; }
    }
    ++s_frames;
    if (now() - s_win < 10e6) return;
    int idx[64];
    const int m = *n < 64 ? *n : 64;
    for (int i = 0; i < m; ++i) idx[i] = i;
    for (int i = 0; i < m; ++i) for (int j = i + 1; j < m; ++j) if (s_sum[idx[j]] > s_sum[idx[i]]) { const int x = idx[i]; idx[i] = idx[j]; idx[j] = x; }
    char buf[900];
    int o = std::snprintf(buf, sizeof buf, "WO218-COST wo203 areas over %u frames (mean/max us a frame):", s_frames);
    for (int k = 0; k < m && k < 12 && o > 0 && o < static_cast<int>(sizeof buf) - 48; ++k)
        o += std::snprintf(buf + o, sizeof buf - static_cast<size_t>(o), " %s=%.0f/%.0f", t[idx[k]].area, s_sum[idx[k]] / (s_frames ? s_frames : 1), s_max[idx[k]]);
    logf("%s", buf);
    for (int i = 0; i < 64; ++i) { s_sum[i] = 0; s_max[i] = 0; }
    s_frames = 0;
    s_win = now();
}

} // namespace kcdmp::wo203
