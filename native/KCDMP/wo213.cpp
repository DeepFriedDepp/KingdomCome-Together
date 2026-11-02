// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-213: the switch word and its bands, the status, the counters (wo213.h).
#include "wo213.h"
#include "wo203.h"
#include "wo163.h"
#include "log.h"
#include "npc_drive.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>

namespace kcdmp::wo213 {

namespace {

using namespace wo213rules;

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};
Bands g_bands{};   // written and read on the main thread (pipe ops and the frame hook both run there)

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W213_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO213-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W213_OFF=%s)", s, b);
    }
    return s;
}

std::atomic<uint32_t> c_band[4]{}, c_extrapCapped{0}, c_late{0}, c_lateAsks{0}, c_missing{0}, c_postureSkips{0}, c_postureDrifts{0}, c_playoutN{0};
std::atomic<uint64_t> c_playoutSumUs{0}, c_targetSumUs{0};
std::atomic<uint32_t> c_playoutMaxUs{0};

uint16_t rd16(const uint8_t* p) { uint16_t v; std::memcpy(&v, p, 2); return v; }

// D.4: this frame's read of the local player (written by the frame hook on the main thread, copied by the pipe's reader)
struct LocalRead { bool ok = false, ok2 = false; localstate::LocalState ls{}; motion::State2 st2{}; double atS = 0; };
SRWLOCK g_localLock = SRWLOCK_INIT;
LocalRead g_local{};
std::atomic<double> g_localAskedS{0};
std::atomic<uint32_t> c_localReads{0}, c_localServed{0}, c_localFallback{0};

// The frame hook (main thread): one read while the agent asks -- the same two reads the pipe's main-thread path makes for it
void tick() {
    if (!on(kBitFigureBand)) return;
    const double now = npcdrive::now_s();
    if (!local_wanted(now, g_localAskedS.load(std::memory_order_relaxed))) return;
    LocalRead r{};
    r.ok = localstate::read_local_state(&r.ls);
    if (r.ok) r.ok2 = motion::read_local_state2(&r.st2, r.ls.rotZ);
    r.atS = now;
    AcquireSRWLockExclusive(&g_localLock);
    g_local = r;
    ReleaseSRWLockExclusive(&g_localLock);
    c_localReads.fetch_add(1, std::memory_order_relaxed);
}

// [op][bits:4] or [op][bits:4][fightM:2][nearM:2][midM:2][fightMs:2][nearMs:2][midMs:2][farMs:2] -> [effective bits:4]
uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if ((len != 5 && len != 19) || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        Bands nb = g_bands;
        if (len == 19) {
            nb.fightM = rd16(body + 5); nb.nearM = rd16(body + 7); nb.midM = rd16(body + 9);
            nb.fightMs = rd16(body + 11); nb.nearMs = rd16(body + 13); nb.midMs = rd16(body + 15); nb.farMs = rd16(body + 17);
            if (nb.fightMs < 10 || nb.nearMs < 10 || nb.midMs < 10 || nb.farMs < 10) return wo163::kRBadRequest;
        }
        const bool bandsChanged = std::memcmp(&nb, &g_bands, sizeof nb) != 0;
        g_bands = nb;
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || bandsChanged || !g_configured.exchange(true))
            logf("WO213-CFG bits=0x%X (was 0x%X; twin off 0x%X): npc_bands=%d playout_adaptive=%d posture_hold_late=%d posture_no_pos=%d figure_playout=%d figure_band=%d "
                 "| bands fight %.0f m %u ms, near %.0f m %u ms, mid %.0f m %u ms, beyond %u ms",
                 bits, was, twin_off(), on(kBitNpcBands), on(kBitPlayoutAdaptive), on(kBitPostureHoldLate), on(kBitPostureNoPos), on(kBitFigurePlayout), on(kBitFigureBand),
                 g_bands.fightM, g_bands.fightMs, g_bands.nearM, g_bands.nearMs, g_bands.midM, g_bands.midMs, g_bands.farMs);
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[700];
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
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_status("w213", &status_reg)
               && wo203::reg_tick("w213", &tick);

} // namespace

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }

const wo213rules::Bands& bands() { return g_bands; }

void note_band_send(int b) { c_band[b < 0 ? 0 : b > 3 ? 3 : b].fetch_add(1, std::memory_order_relaxed); }
void note_playout(double appliedS, double targetS) {
    c_playoutN.fetch_add(1, std::memory_order_relaxed);
    c_playoutSumUs.fetch_add(static_cast<uint64_t>(appliedS * 1e6), std::memory_order_relaxed);
    c_targetSumUs.fetch_add(static_cast<uint64_t>(targetS * 1e6), std::memory_order_relaxed);
    const uint32_t us = static_cast<uint32_t>(appliedS * 1e6);
    uint32_t m = c_playoutMaxUs.load(std::memory_order_relaxed);
    while (us > m && !c_playoutMaxUs.compare_exchange_weak(m, us)) {}
}
void note_extrap_capped() { c_extrapCapped.fetch_add(1, std::memory_order_relaxed); }
void note_late_check() { c_late.fetch_add(1, std::memory_order_relaxed); }
void note_late_ask() { c_lateAsks.fetch_add(1, std::memory_order_relaxed); }
void note_missing_check() { c_missing.fetch_add(1, std::memory_order_relaxed); }
void note_posture_skip() { c_postureSkips.fetch_add(1, std::memory_order_relaxed); }
void note_posture_drift(const char* name, float offM) {
    const uint32_t k = c_postureDrifts.fetch_add(1, std::memory_order_relaxed) + 1;
    if (k <= 100 || k % 100 == 0)
        logf("WO213-DRIFT npc=%s a held posture's body is %.2f m off its host NPC's place: the stream takes it for the rest of that sitting [n=%u]",
             name, offM, k);
}

bool local_take(localstate::LocalState* ls, bool* ok, motion::State2* st2, bool* ok2) {
    const double now = npcdrive::now_s();
    g_localAskedS.store(now, std::memory_order_relaxed);
    if (!on(kBitFigureBand)) return false;
    AcquireSRWLockShared(&g_localLock);
    const LocalRead r = g_local;
    ReleaseSRWLockShared(&g_localLock);
    if (!local_fresh(now, r.atS)) { c_localFallback.fetch_add(1, std::memory_order_relaxed); return false; }
    *ls = r.ls; *ok = r.ok; *st2 = r.st2; *ok2 = r.ok2;
    c_localServed.fetch_add(1, std::memory_order_relaxed);
    return true;
}

int status(char* out, int n) {
    const uint32_t pn = c_playoutN.load();
    return std::snprintf(out, static_cast<size_t>(n),
                         "w213 bits=0x%X twin_off=0x%X configured=%d | D.2 band_sends fight=%u near=%u mid=%u far=%u | E.1 playout recomputes=%u mean_ms=%.0f "
                         "target_mean_ms=%.0f max_ms=%.0f extrap_capped_frames=%u | E.2 late_checks=%u late_asks=%u missing_checks=%u | E.3 posture_skips=%u posture_drifts=%u "
                         "| D.4 local_reads=%u local_served=%u local_fallback=%u",
                         g_bits.load(), twin_off(), g_configured.load() ? 1 : 0, c_band[0].load(), c_band[1].load(), c_band[2].load(), c_band[3].load(),
                         pn, pn ? c_playoutSumUs.load() / 1000.0 / pn : 0.0, pn ? c_targetSumUs.load() / 1000.0 / pn : 0.0, c_playoutMaxUs.load() / 1000.0,
                         c_extrapCapped.load(), c_late.load(), c_lateAsks.load(), c_missing.load(), c_postureSkips.load(), c_postureDrifts.load(),
                         c_localReads.load(), c_localServed.load(), c_localFallback.load());
}

} // namespace kcdmp::wo213
