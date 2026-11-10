// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-217 F: the joiner's carts follow the host's. Op 172 brings the host's samples (its agent's clock, each cart's position and angles);
// once a frame each cart is played back kCartDelayS behind the host's newest, interpolated, and written with IEntity::SetPosRotScale --
// the call the NPC writer makes each frame (npc_drive.cpp's write, WO-118's audit) -- the host's height as it is, no ground writer.
// A cart the host stopped naming for kCartStaleS is left to its own engine again. Main thread (the op and the tick both).
//   WO217-CART adopt name=<n> eid=0x.. | missing name=<n> | released name=<n> why=<w> | status carts=<k> writes=<w> ...
#include "wo217.h"
#include "wo203.h"
#include "wo163.h"
#include "engine.h"
#include "log.h"
#include "npc_drive.h"
#include "npcstate.h"

#include <atomic>
#include <cstring>
#include <deque>
#include <string>
#include <unordered_map>

namespace kcdmp::wo217 {
namespace {

using namespace wo217rules;

struct Cart {
    uint32_t eid = 0;
    std::deque<CartSample> s;   // by the host's clock
    double lastSampleAt = -1e9; // local clock of the newest
    bool missingNoted = false;
    uint32_t writes = 0;
};
std::unordered_map<std::string, Cart> g_carts;
double g_offset = 0; bool g_haveOffset = false;   // local - host clock (the smallest seen: the least delayed batch)
double g_offsetAt = -1e9;
std::atomic<uint32_t> c_batches{0}, c_bad{0}, c_writes{0}, c_adopt{0}, c_missing{0}, c_released{0}, c_failed{0};

uint8_t op_carts(uint8_t, const uint8_t* body, size_t len, uint8_t* out, size_t, size_t* outLen) {
    (void)out;
    *outLen = 0;
    static CartRow rows[24];
    uint32_t hostMs = 0;
    const int n = len > 1 ? parse_carts(body + 1, len - 1, &hostMs, rows, 24) : -1;
    if (n < 0) { c_bad.fetch_add(1, std::memory_order_relaxed); return wo163::kRBadRequest; }
    c_batches.fetch_add(1, std::memory_order_relaxed);
    if (!on(kBitCarts) || role() != 2) return wo163::kROk;
    const double now = npcdrive::now_s();
    const double ht = hostMs / 1000.0;
    // the clock offset: the least delayed batch of the last 10 s (a late batch never pulls the playback back)
    const double off = now - ht;
    if (!g_haveOffset || off < g_offset || now - g_offsetAt > 10.0) { g_offset = off; g_haveOffset = true; g_offsetAt = now; }
    for (int i = 0; i < n; ++i) {
        Cart& c = g_carts[rows[i].name];
        CartSample sm; sm.t = ht;
        std::memcpy(sm.p, rows[i].p, sizeof sm.p); std::memcpy(sm.a, rows[i].a, sizeof sm.a);
        if (!c.s.empty() && ht <= c.s.back().t) continue;   // an old batch
        c.s.push_back(sm);
        while (c.s.size() > 8) c.s.pop_front();
        c.lastSampleAt = now;
    }
    return wo163::kROk;
}

double g_lastLog = 0;
void tick_carts() {
    if (g_carts.empty()) return;
    const double now = npcdrive::now_s();
    const bool act = on(kBitCarts) && role() == 2 && g_haveOffset;
    for (auto it = g_carts.begin(); it != g_carts.end();) {
        Cart& c = it->second;
        if (!act || now - c.lastSampleAt > kCartStaleS) {
            if (c.eid) { const uint32_t k = c_released.fetch_add(1) + 1; if (k <= 200) logf("WO217-CART released name=%s why=%s writes=%u [n=%u]", it->first.c_str(), act ? "the host stopped naming it" : "carts off", c.writes, k); }
            it = g_carts.erase(it);
            continue;
        }
        void* e = c.eid ? engine::entity_by_id(c.eid) : nullptr;
        if (!e) {
            e = wo141::x::entity_named(it->first.c_str());
            if (!e) {
                if (!c.missingNoted) { c.missingNoted = true; const uint32_t k = c_missing.fetch_add(1) + 1; if (k <= 100) logf("WO217-CART missing name=%s -- the host's cart is not in this world [n=%u]", it->first.c_str(), k); }
                ++it; continue;
            }
            c.eid = engine::entity_id(e);
            const uint32_t k = c_adopt.fetch_add(1) + 1;
            if (k <= 200) logf("WO217-CART adopt name=%s eid=0x%X -- moved by the host's cart from now on (its own route set aside) [n=%u]", it->first.c_str(), c.eid, k);
        }
        CartSample buf[8]; int n = 0;
        for (const auto& sm : c.s) buf[n++] = sm;
        float p[3], a[3], q[4];
        cart_pose(buf, n, now - g_offset - kCartDelayS, p, a);
        quat_xyz(a, q);
        if (npcdrive::write_entity_tm(e, p, q)) { ++c.writes; c_writes.fetch_add(1, std::memory_order_relaxed); }
        else c_failed.fetch_add(1, std::memory_order_relaxed);
        ++it;
    }
    if (now - g_lastLog >= 30.0) {
        g_lastLog = now;
        logf("WO217-CART status carts=%zu batches=%u bad=%u writes=%u failed=%u adopted=%u missing=%u released=%u offset_ms=%.0f",
             g_carts.size(), c_batches.load(), c_bad.load(), c_writes.load(), c_failed.load(), c_adopt.load(), c_missing.load(), c_released.load(),
             g_haveOffset ? g_offset * 1000.0 : -1.0);
    }
}

const bool kRegCarts = wo203::reg_op(kOpCarts, &op_carts) && wo203::reg_tick("w217cart", &tick_carts);

} // namespace
} // namespace kcdmp::wo217
