// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-204: the native half's switch word and status (wo204.h).
#include "wo204.h"
#include "wo204_rules.h"
#include "wo203.h"
#include "wo163.h"
#include "log.h"
#include <atomic>
#include <cstdio>
#include <cstring>

namespace kcdmp::wo204 {

namespace {

using namespace wo204rules;

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};
std::atomic<uint32_t> c_seatHoldCopy{0}, c_seatHoldWanted{0}, c_seatMoves{0};

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 5 || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true))
            logf("WO204-CFG bits=0x%X (was 0x%X): seat_hold=%d tpose_watch=%d", bits, was, (bits & kBitSeatHold) != 0, (bits & kBitTposeWatch) != 0);
        std::memcpy(out, &bits, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[256];
        int n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

} // namespace

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "w204 bits=0x%X configured=%d seat_hold copy_state=%u host_wanted=%u seat_moves=%u", g_bits.load(),
                         g_configured.load() ? 1 : 0, c_seatHoldCopy.load(), c_seatHoldWanted.load(), c_seatMoves.load());
}

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & bit) != 0; }

void note_seat_hold(bool fromCopyState) { (fromCopyState ? c_seatHoldCopy : c_seatHoldWanted).fetch_add(1, std::memory_order_relaxed); }
uint32_t note_seat_move() { return c_seatMoves.fetch_add(1, std::memory_order_relaxed) + 1; }

namespace {
int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_status("w204", &status_reg);
} // namespace

} // namespace kcdmp::wo204
