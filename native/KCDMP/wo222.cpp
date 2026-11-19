// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-222: the native half's switch word and status (wo222.h).
#include "wo222.h"
#include "wo203.h"
#include "wo163.h"
#include "log.h"
#include "combat_swing.h"
#include "wo187.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>

namespace kcdmp::wo222 {

namespace {
struct N { const char* n; uint32_t b; };
const N kNames[] = {{"react_parity", kBitReactParity}, {"react_show", kBitReactShow}, {"flee_live", kBitFleeLive}, {"swing_log", kBitSwingLog}, {"dead_lay", kBitDeadLay}};
}

uint32_t twin_off_bits(const char* names) {
    if (!names) return 0;
    uint32_t bits = 0;
    const char* p = names;
    while (*p) {
        while (*p == ',' || *p == ' ') ++p;
        const char* e = p;
        while (*e && *e != ',' && *e != ' ') ++e;
        const size_t len = static_cast<size_t>(e - p);
        if (len == 3 && std::strncmp(p, "all", 3) == 0) bits |= kBitsDefault & ~kBitSwingLog;   // the swing log is an instrument: every run keeps it
        for (const N& x : kNames) if (std::strlen(x.n) == len && std::strncmp(p, x.n, len) == 0) bits |= x.b;
        p = e;
    }
    return bits;
}

namespace {

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};
std::atomic<uint8_t> g_role{0};

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W222_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO222-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W222_OFF=%s)", s, b);
    }
    return s;
}

struct More { const char* area; StatusFn fn; };
More g_more[24];
int g_moreN = 0;

int status(char* out, int n) {
    int k = std::snprintf(out, static_cast<size_t>(n), "bits=0x%X twin_off=0x%X role=%u", g_bits.load(), twin_off(), g_role.load());
    for (const N& x : kNames) {
        if (k < 0 || k >= n) break;
        k += std::snprintf(out + k, static_cast<size_t>(n - k), " %s=%d", x.n, on(x.b) ? 1 : 0);
    }
    for (int i = 0; i < g_moreN && k > 0 && k < n - 8; ++i) {
        k += std::snprintf(out + k, static_cast<size_t>(n - k), " | %s: ", g_more[i].area);
        if (k >= n) break;
        const int m = g_more[i].fn(out + k, n - k);
        if (m > 0) k += (m < n - k ? m : n - k - 1);
    }
    return k;
}

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 6 || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint8_t wasRole = g_role.exchange(body[5]);
        if (wasRole != body[5]) logf("WO222-CFG role %u -> %u (0 none, 1 the host, 2 a joiner in the host's world)", wasRole, body[5]);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true)) {
            char t[300];
            status(t, sizeof t);
            logf("WO222-CFG %s (was 0x%X)", t, was);
        }
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[1000];
        // [page:1] (optional): 0 = the switches, k = the k-th area's own line, past the last = nothing. No page: all that fits (0.65.x).
        int n = 0;
        if (len >= 2) {
            const int page = body[1];
            if (page == 0) {
                n = std::snprintf(t, sizeof t, "bits=0x%X twin_off=0x%X role=%u", g_bits.load(), twin_off(), g_role.load());
                for (const N& x : kNames) if (n > 0 && n < static_cast<int>(sizeof t)) n += std::snprintf(t + n, sizeof t - static_cast<size_t>(n), " %s=%d", x.n, on(x.b) ? 1 : 0);
            } else if (page <= g_moreN) {
                n = std::snprintf(t, sizeof t, "%s: ", g_more[page - 1].area);
                const int m = g_more[page - 1].fn(t + n, static_cast<int>(sizeof t) - n);
                if (m > 0) n += (m < static_cast<int>(sizeof t) - n ? m : static_cast<int>(sizeof t) - n - 1);
            }
        } else n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) >= sizeof t) n = static_cast<int>(sizeof t) - 1;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

// B.1 (op 202, main thread, read-only): [op][eid:4] -> [state:1][our fragment:4][full body's fragment:4][its priority:4][ms since queued:4]
// state: 0 no row of ours queued on the body, 1 ours plays on one of its scopes (a torso hit plays on the upper body), 2 another action
// plays on the full body, 3 the full body is empty,
// 4 unreadable.
uint8_t op_shown(uint8_t, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len != 5 || cap < 17) return wo163::kRBadRequest;
    uint32_t eid = 0;
    std::memcpy(&eid, body + 1, 4);
    uint32_t frag = 0, prio = 0;
    double age = 0;
    uint8_t state = 0;
    uint32_t nowFrag = 0;
    int32_t nowPrio = 0;
    if (rttr::last_swing(eid, &frag, &prio, &age)) {
        // the full body first; a torso-only reaction (CombatHitTorso) plays on the upper body: any scope playing our fragment shows it
        wo187::ScopeInfo sc[12];
        const int n = wo187::read_all_scopes(eid, sc, 12);
        if (n < 1 || !sc[0].ok) state = 4;
        else {
            state = sc[0].empty ? 3 : 2;
            nowFrag = sc[0].frag; nowPrio = sc[0].prio;
            for (int i = 0; i < n; ++i)
                if (sc[i].ok && !sc[i].empty && sc[i].frag == frag) { state = 1; nowFrag = sc[i].frag; nowPrio = sc[i].prio; break; }
        }
    }
    out[0] = state;
    std::memcpy(out + 1, &frag, 4);
    std::memcpy(out + 5, &nowFrag, 4);
    std::memcpy(out + 9, &nowPrio, 4);
    const uint32_t ms = static_cast<uint32_t>(age * 1000.0);
    std::memcpy(out + 13, &ms, 4);
    *outLen = 17;
    return wo163::kROk;
}

int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_op(kOpShown, &op_shown) && wo203::reg_status("w222", &status_reg);

} // namespace

bool add_status(const char* area, StatusFn fn) {
    if (g_moreN >= 24) return false;
    g_more[g_moreN++] = More{area, fn};
    return true;
}
uint8_t role() { return g_role.load(); }
bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }

} // namespace kcdmp::wo222
