// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
//
// WO-221 D.2: see body_order.h. No engine call is made here: an order is posted to wo215's queue and carried out at the AI
// update's entry, on its thread (wo215.cpp), exactly as the route's orders are.
#include "body_order.h"
#include "wo221.h"
#include "wo215.h"
#include "wo203.h"
#include "wo163.h"
#include "engine.h"
#include "log.h"
#include "npc_drive.h"

#include <atomic>
#include <cstdio>
#include <cstring>

namespace kcdmp::bodyorder {

namespace {

std::atomic<uint32_t> c_go{0}, c_stop{0}, c_noBody{0}, c_notArmed{0};

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (cap < 5) return wo163::kRBadRequest;
    auto answer = [&](uint8_t code, uint32_t id) { out[0] = code; std::memcpy(out + 1, &id, 4); *outLen = 5; return wo163::kROk; };
    char name[64]{};
    uint8_t gait = 0;
    float to[3]{};
    if (opc == kOpGo) {
        if (len < 4) return wo163::kRBadRequest;
        gait = body[1];
        const uint8_t nl = body[2];
        if (nl == 0 || nl > 63 || gait > 3 || static_cast<size_t>(3 + nl + 12) != len) return wo163::kRBadRequest;
        std::memcpy(name, body + 3, nl);
        std::memcpy(to, body + 3 + nl, 12);
    } else if (opc == kOpStop) {
        if (len < 3) return wo163::kRBadRequest;
        const uint8_t nl = body[1];
        if (nl == 0 || nl > 63 || static_cast<size_t>(2 + nl) != len) return wo163::kRBadRequest;
        std::memcpy(name, body + 2, nl);
    } else return wo163::kRBadRequest;
    if (opc == kOpGo && !wo221::on(wo221::kBitOwnHorse)) return answer(kOff, 0);
    if (!wo215::route_armed()) { c_notArmed.fetch_add(1, std::memory_order_relaxed); return answer(kNotArmed, 0); }
    void* e = npcdrive::entity_by_name(name);
    const uint32_t eid = e ? engine::entity_id(e) : 0;
    if (!eid) { c_noBody.fetch_add(1, std::memory_order_relaxed); return answer(kNoBody, 0); }
    if (opc == kOpStop) {
        wo215::order_stop(eid);
        const uint32_t k = c_stop.fetch_add(1, std::memory_order_relaxed) + 1;
        if (k <= 100 || k % 100 == 0) logf("WO221-ORDER npc=%s stop: its movement request is cancelled the task's own way [n=%u]", name, k);
        return answer(kOk, 0);
    }
    const uint32_t id = wo215::order_go(eid, to, gait);
    const uint32_t k = c_go.fetch_add(1, std::memory_order_relaxed) + 1;
    if (k <= 200 || k % 100 == 0)
        logf("WO221-ORDER npc=%s go to (%.1f,%.1f,%.1f) gait=%u -> order %u (the engine's own movement request, made at the AI update's entry) [n=%u]",
             name, to[0], to[1], to[2], gait, id, k);
    return answer(kOk, id);
}

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "go=%u stop=%u no_body=%u not_armed=%u route_armed=%d", c_go.load(), c_stop.load(), c_noBody.load(),
                         c_notArmed.load(), wo215::route_armed() ? 1 : 0);
}

const bool kReg = wo203::reg_op(kOpGo, &op) && wo203::reg_op(kOpStop, &op) && wo221::add_status("body_order", &status);

} // namespace

} // namespace kcdmp::bodyorder
