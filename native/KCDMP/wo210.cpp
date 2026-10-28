// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-210: the native half's switch word, its status and the instruments' counters (wo210.h).
#include "wo210.h"
#include "wo210_rules.h"
#include "wo203.h"
#include "wo163.h"
#include "engine.h"
#include "log.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>

namespace kcdmp::wo210 {

namespace {

using namespace wo210rules;

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W210_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO210-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W210_OFF=%s)", s, b);
    }
    return s;
}

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

// ---- A counters
std::atomic<uint32_t> c_hand[kPathCount]{}, c_handInstalled{0}, c_handNeutral{0}, c_handNone{0};
std::atomic<uint32_t> c_checks{0}, c_verdict[kVerdictCount]{}, c_reseats{0}, c_reseatFailed{0}, c_reseatUnknown{0}, c_release[kPathCount]{};

// ---- B.1 the clinch detector: the gate's thread records, the main thread tells
struct ClinchRec { Burst b; uint32_t asks = 0, refusals = 0, playerAsks = 0; bool pending = false; double since = 0; };
std::mutex g_clinchMu;
std::unordered_map<uint32_t, ClinchRec> g_clinch;
std::atomic<uint32_t> c_clinchAsks{0}, c_clinchRefused{0}, c_clinchLoops{0};

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 5 || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true))
            logf("WO210-CFG bits=0x%X (was 0x%X; twin off 0x%X): copy_state_always=%d clinch_v2=%d death_engine=%d knockdown_sync=%d copy_ready=%d hit_local_none=%d rider_sync=%d one_request=%d",
                 bits, was, twin_off(), on(kBitStateAlways), on(kBitClinchV2), on(kBitDeathEngine), on(kBitKnockdownSync), on(kBitCopyReady),
                 on(kBitHitLocalNone), on(kBitRiderSync), on(kBitOneRequest));
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[1400];
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

const char* name_of(uint32_t eid) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "?";
}

} // namespace

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }
bool fix_on(uint32_t bit) { return (twin_off() & bit) == 0; }

void note_handover(uint8_t path, bool installed, bool neutral) {
    c_hand[path < kPathCount ? path : 0].fetch_add(1);
    (installed ? (neutral ? c_handNeutral : c_handInstalled) : c_handNone).fetch_add(1);
}
void note_check(uint8_t verdict) { c_checks.fetch_add(1); c_verdict[verdict < kVerdictCount ? verdict : 0].fetch_add(1); }
void note_release(uint8_t path) { c_release[path < kPathCount ? path : 0].fetch_add(1); }

void note_reseat(const char* npc, const char* was, const char* want, uint8_t path, double breachS, bool ok) {
    const uint32_t k = c_reseats.fetch_add(1) + 1;
    if (!ok) c_reseatFailed.fetch_add(1);
    if (path == kPathUnknown) c_reseatUnknown.fetch_add(1);
    if (k <= 200 || k % 50 == 0)
        logf("WO210-STATE reseat npc=%s was=%s want=%s path=%s ms=%.0f -> %s [n=%u]", npc ? npc : "?", was ? was : "?", want ? want : "?", path_name(path),
             breachS * 1000.0, ok ? "the host's state installed" : "NOT installed (the next check asks again)", k);
}

void note_clinch_ask(uint32_t copyEid, bool refused, bool askerIsPlayer) {
    c_clinchAsks.fetch_add(1);
    if (refused) c_clinchRefused.fetch_add(1);
    const double t = now_s();
    std::lock_guard<std::mutex> lk(g_clinchMu);
    ClinchRec& r = g_clinch[copyEid];
    ++r.asks;
    if (refused) ++r.refusals;
    if (askerIsPlayer) ++r.playerAsks;
    if (burst_ask(r.b, t)) { r.pending = true; r.since = t; }
}

void tick() {
    struct Tell { uint32_t eid, asks, refusals, playerAsks, inStretch; };
    Tell tell[8]; int n = 0;
    {
        std::lock_guard<std::mutex> lk(g_clinchMu);
        for (auto& kv : g_clinch) {
            if (!kv.second.pending || n >= 8) continue;
            kv.second.pending = false;
            tell[n++] = {kv.first, kv.second.asks, kv.second.refusals, kv.second.playerAsks, kv.second.b.inStretch};
        }
    }
    for (int i = 0; i < n; ++i) {
        const uint32_t k = c_clinchLoops.fetch_add(1) + 1;
        logf("WO210-CLINCH loop npc=%s: %u asks within %.0f ms (asks=%u refused=%u started_by=%s) -- one attempt asked again each frame [n=%u]", name_of(tell[i].eid),
             tell[i].inStretch, kLoopWindowS * 1000.0, tell[i].asks, tell[i].refusals, tell[i].playerAsks * 2 >= tell[i].asks ? "the-local-player" : "the-copy", k);
    }
}

int status(char* out, int n) {
    char hand[256]; int o = 0;
    for (int p = 1; p < kPathCount && o < static_cast<int>(sizeof hand) - 16; ++p)
        if (c_hand[p].load()) o += std::snprintf(hand + o, sizeof hand - static_cast<size_t>(o), "%s%s:%u", o ? "," : "", path_name(static_cast<uint8_t>(p)), c_hand[p].load());
    if (!o) std::snprintf(hand, sizeof hand, "-");
    char rel[256]; o = 0;
    for (int p = 1; p < kPathCount && o < static_cast<int>(sizeof rel) - 16; ++p)
        if (c_release[p].load()) o += std::snprintf(rel + o, sizeof rel - static_cast<size_t>(o), "%s%s:%u", o ? "," : "", path_name(static_cast<uint8_t>(p)), c_release[p].load());
    if (!o) std::snprintf(rel, sizeof rel, "-");
    return std::snprintf(out, static_cast<size_t>(n), "w210 bits=0x%X twin_off=0x%X configured=%d | A handovers installed=%u neutral=%u none=%u by_path=%s | releases=%s | "
                         "checks=%u empty=%u stale_posture=%u missing_posture=%u stale_dead=%u posture_vs_other=%u reseats=%u failed=%u unknown_path=%u | "
                         "B clinch asks=%u refused=%u loops=%u",
                         g_bits.load(), twin_off(), g_configured.load() ? 1 : 0, c_handInstalled.load(), c_handNeutral.load(), c_handNone.load(), hand, rel,
                         c_checks.load(), c_verdict[1].load(), c_verdict[2].load(), c_verdict[3].load(), c_verdict[4].load(), c_verdict[5].load(), c_reseats.load(),
                         c_reseatFailed.load(), c_reseatUnknown.load(), c_clinchAsks.load(), c_clinchRefused.load(), c_clinchLoops.load());
}

namespace {
int status_reg(char* out, int n) { return status(out, n); }
void tick_reg() { tick(); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_status("w210", &status_reg) && wo203::reg_tick("w210", &tick_reg);
} // namespace

} // namespace kcdmp::wo210
