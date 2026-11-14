// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
//
// WO-221 B (mp_talk_hold): see talk_hold.h. No engine call is made here: the module keeps one small table (main thread only: the
// pipe's ops and every reader run there).
#include "talk_hold.h"
#include "wo221.h"
#include "wo203.h"
#include "wo163.h"
#include "hits.h"
#include "log.h"
#include "npc_drive.h"

#include <atomic>
#include <cctype>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>

namespace kcdmp::talkhold {

namespace {

struct Held { double until = 0; double since = 0; uint32_t eid = 0; };
std::unordered_map<std::string, Held> g_byName;   // lower-case name; main thread
std::unordered_map<uint32_t, std::string> g_byEid;
std::atomic<uint32_t> c_set{0}, c_renewed{0}, c_released{0}, c_lapsed{0};
std::atomic<uint32_t> c_lane[static_cast<int>(Lane::Count)]{};
const char* const kLaneNames[] = {"owner", "rows", "seat"};

std::string lower(const char* s) {
    std::string o(s ? s : "");
    for (auto& ch : o) ch = static_cast<char>(std::tolower(static_cast<unsigned char>(ch)));
    return o;
}

void drop(std::unordered_map<std::string, Held>::iterator it, const char* why, double now) {
    logf("WO221-TALK npc=%s released after %.1f s (%s) -- the mod's lanes ask of it again", it->first.c_str(), now - it->second.since, why);
    if (it->second.eid) g_byEid.erase(it->second.eid);
    g_byName.erase(it);
}

bool live(const std::string& key, Lane lane) {
    const auto it = g_byName.find(key);
    if (it == g_byName.end()) return false;
    const double now = npcdrive::now_s();
    if (now > it->second.until) { c_lapsed.fetch_add(1, std::memory_order_relaxed); drop(it, "its word lapsed: no end and no renewal", now); return false; }
    c_lane[static_cast<int>(lane)].fetch_add(1, std::memory_order_relaxed);
    return true;
}

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (cap < 1 || opc != kOpTalk || len < 4) return wo163::kRBadRequest;
    auto answer = [&](uint8_t code) { out[0] = code; *outLen = 1; return wo163::kROk; };
    const bool on = body[1] != 0;
    const uint8_t nl = body[2];
    if (nl == 0 || nl > 63 || static_cast<size_t>(3 + nl) != len) return wo163::kRBadRequest;
    char name[64]{};
    std::memcpy(name, body + 3, nl);
    const std::string key = lower(name);
    const double now = npcdrive::now_s();
    const auto it = g_byName.find(key);
    if (!on) {
        if (it == g_byName.end()) return answer(kNotHeld);
        c_released.fetch_add(1, std::memory_order_relaxed);
        drop(it, "the conversation ended", now);
        return answer(kReleased);
    }
    if (!wo221::on(wo221::kBitTalkHold)) return answer(kOff);
    if (it != g_byName.end()) { it->second.until = now + kLapseS; c_renewed.fetch_add(1, std::memory_order_relaxed); return answer(kHeld); }
    Held h;
    h.since = now; h.until = now + kLapseS; h.eid = hits::eid_of_name(name);
    if (h.eid) g_byEid[h.eid] = key;
    g_byName.emplace(key, h);
    c_set.fetch_add(1, std::memory_order_relaxed);
    logf("WO221-TALK npc=%s (id %u) is in a conversation here -- held: the owner's installs and queue drain, its hand / gait / one-shot rows and the "
         "seat rule ask nothing of it until the conversation ends", name, h.eid);
    return answer(kHeld);
}

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "held_now=%zu set=%u renewed=%u released=%u lapsed=%u stood_down: %s=%u %s=%u %s=%u",
                         g_byName.size(), c_set.load(), c_renewed.load(), c_released.load(), c_lapsed.load(), kLaneNames[0], c_lane[0].load(),
                         kLaneNames[1], c_lane[1].load(), kLaneNames[2], c_lane[2].load());
}

const bool kReg = wo203::reg_op(kOpTalk, &op) && wo221::add_status("talk_hold", &status);

} // namespace

bool holds(uint32_t eid, Lane lane) {
    if (g_byEid.empty() || !wo221::on(wo221::kBitTalkHold)) return false;
    const auto it = g_byEid.find(eid);
    return it != g_byEid.end() && live(it->second, lane);
}

bool holds_name(const char* name, Lane lane) {
    if (g_byName.empty() || !name || !wo221::on(wo221::kBitTalkHold)) return false;
    return live(lower(name), lane);
}

} // namespace kcdmp::talkhold
