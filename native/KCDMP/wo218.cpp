// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-218: the native half's switch word and status (wo218.h).
#include "wo218.h"
#include "wo203.h"
#include "wo163.h"
#include "log.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>

namespace kcdmp::wo218 {

uint32_t twin_off_bits(const char* names) {
    if (!names) return 0;
    struct N { const char* n; uint32_t b; };
    static const N k[] = {{"takeover_stop", kBitTakeoverStop}, {"drift_release", kBitDriftRelease}, {"main_budget", kBitMainBudget}};
    uint32_t bits = 0;
    const char* p = names;
    while (*p) {
        while (*p == ',' || *p == ' ') ++p;
        const char* e = p;
        while (*e && *e != ',' && *e != ' ') ++e;
        const size_t len = static_cast<size_t>(e - p);
        if (len == 3 && std::strncmp(p, "all", 3) == 0) bits |= kBitsDefault;
        for (const N& x : k) if (std::strlen(x.n) == len && std::strncmp(p, x.n, len) == 0) bits |= x.b;
        p = e;
    }
    return bits;
}

namespace {

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<uint32_t> c_takeovers{0}, c_driftReleases{0};
std::atomic<bool> g_configured{false};
std::atomic<uint8_t> g_role{0};

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W218_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO218-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W218_OFF=%s)", s, b);
    }
    return s;
}

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "bits=0x%X twin_off=0x%X role=%u batch=served takeover_stop=%d takeovers_stopped=%u drift_release=%d drift_releases=%u main_budget=%d",
                         g_bits.load(), twin_off(), g_role.load(), on(kBitTakeoverStop) ? 1 : 0, c_takeovers.load(), on(kBitDriftRelease) ? 1 : 0,
                         c_driftReleases.load(), on(kBitMainBudget) ? 1 : 0);
}

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 6 || cap < 5) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint8_t wasRole = g_role.exchange(body[5]);
        if (wasRole != body[5]) logf("WO218-CFG role %u -> %u (0 none, 1 the host, 2 a joiner in the host's world)", wasRole, body[5]);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true))
            logf("WO218-CFG bits=0x%X (was 0x%X; twin off 0x%X): takeover_stop=%d drift_release=%d", bits, was, twin_off(), on(kBitTakeoverStop) ? 1 : 0,
                 on(kBitDriftRelease) ? 1 : 0);
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        out[4] = kCapBatch;
        *outLen = 5;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[600];
        int n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) >= sizeof t) n = static_cast<int>(sizeof t) - 1;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_status("w218", &status_reg);

} // namespace

uint8_t role() { return g_role.load(); }
uint32_t note_takeover() { return c_takeovers.fetch_add(1) + 1; }
uint32_t note_drift_release() { return c_driftReleases.fetch_add(1) + 1; }
bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }

} // namespace kcdmp::wo218
