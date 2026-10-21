// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-202's native half (wo202.h): the switch word, the quest-fail tracer's records and their poll.
#include "wo202.h"
#include "wo202_rules.h"
#include "wo163.h"
#include "wo200.h"
#include "crash_capture.h"
#include "combat_swing.h"
#include "fault_guard.h"
#include "log.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <vector>

namespace kcdmp::wo202 {

namespace {

using namespace wo202rules;

std::atomic<uint32_t> g_bits{0xFFFFFFFFu};   // every mechanism on until the agent's word arrives
std::atomic<bool> g_configured{false};

// ---- II.4: when each node last started (C_Node::Execute), a fixed table: a slot is overwritten by another node, never grown ----
constexpr size_t kExecBits = 14, kExecSlots = size_t(1) << kExecBits;
struct ExecSlot { std::atomic<const void*> node{nullptr}; std::atomic<uint64_t> at{0}; };
ExecSlot g_exec[kExecSlots];
size_t exec_index(const void* n) { return static_cast<size_t>((reinterpret_cast<uintptr_t>(n) >> 4) * 0x9E3779B97F4A7C15ull >> (64 - kExecBits)); }
int64_t exec_age_ms(const void* n, uint64_t now) {
    if (!n) return -1;
    const ExecSlot& s = g_exec[exec_index(n)];
    if (s.node.load(std::memory_order_relaxed) != n) return -1;
    const uint64_t at = s.at.load(std::memory_order_relaxed);
    return at && at <= now ? static_cast<int64_t>(now - at) : -1;
}

// ---- II.4: the failures, for the agent's poll ----
struct Fail {
    uint32_t seq = 0; uint64_t atMs = 0; int32_t oldI = 0, newI = 0; uint32_t tid = 0; bool cascade = false; int64_t ranMs = -1;
    char path[300]{}, port[64]{}, cls[96]{}, srcPath[300]{}, srcPort[64]{};
};
std::mutex g_failMx;
std::vector<Fail> g_fails;   // the last 32
std::atomic<uint32_t> g_failSeq{0};
std::atomic<uint32_t> c_fails{0}, c_failNoSource{0}, c_polled{0};

void put_u32(uint8_t* p, uint32_t v) { std::memcpy(p, &v, 4); }

} // namespace

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & bit) != 0; }

void install() {
    logf("WO202-BUILD switches: crash_capture near_safe quest_fail_trace anim_shown recoil_clinch fight_pos_fresh death_engine_only -- all on "
         "until the agent's word (op 88)");
}

void tick() {}

void note_exec(const void* node) {
    if (!node) return;
    ExecSlot& s = g_exec[exec_index(node)];
    s.node.store(node, std::memory_order_relaxed);
    s.at.store(GetTickCount64(), std::memory_order_relaxed);
}

void note_change(const char* path, const char* port, int32_t oldI, int32_t newI, uint32_t tid, bool cascade) {
    if (!on(kBitQuestFail) || !is_fail_change(path, port, newI)) return;
    Fail f{};
    f.atMs = GetTickCount64();
    f.oldI = oldI; f.newI = newI; f.tid = tid; f.cascade = cascade;
    strncpy_s(f.path, path ? path : "", _TRUNCATE);
    strncpy_s(f.port, port ? port : "", _TRUNCATE);
    const void* sn = nullptr; const void* sp = nullptr;
    wo200::source_now(&sn, &sp);
    if (sp) {
        wo200::describe_source(sn, sp, f.cls, sizeof f.cls, f.srcPath, sizeof f.srcPath, f.srcPort, sizeof f.srcPort);
        // the node's own start, when the table still holds it (the owner is looked up again only for its path above)
        f.ranMs = sn ? exec_age_ms(sn, f.atMs) : -1;
    } else {
        c_failNoSource.fetch_add(1);
    }
    c_fails.fetch_add(1);
    f.seq = g_failSeq.fetch_add(1) + 1;
    char ran[24] = "?";
    if (f.ranMs >= 0) std::snprintf(ran, sizeof ran, "%.1f", f.ranMs / 1000.0);
    logf("WO202-QUESTFAIL seq=%u %s %s -> %d (from %d)%s source=%s %s :: %s%s ran_s=%s tid=%u", f.seq, f.path, f.port[0] ? f.port : "-", newI, oldI,
         cascade ? " cascade" : " root", source_kind(f.cls), f.srcPath[0] ? f.srcPath : "-", f.srcPort[0] ? f.srcPort : "-",
         f.cls[0] ? "" : " (no out-port pulse on this thread: a change made by code, not by a node)", ran, tid);
    std::lock_guard<std::mutex> lk(g_failMx);
    if (g_fails.size() >= 32) g_fails.erase(g_fails.begin());
    g_fails.push_back(f);
}

int status_text(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "w202 bits=0x%X configured=%d questfail=%u nosource=%u polled=%u", g_bits.load(),
                         g_configured.load() ? 1 : 0, c_fails.load(), c_failNoSource.load(), c_polled.load());
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    switch (op) {
        case kOpStatus: {
            char t[480];
            int k = status_text(t, sizeof t);
            if (k > 0 && k < static_cast<int>(sizeof t)) {
                char c[200];
                const int m = crash::status_text(c, sizeof c);
                if (m > 0) k += std::snprintf(t + k, sizeof t - k, " | %s", c);
                char a[120];
                if (k > 0 && k < static_cast<int>(sizeof t) && kcdmp::rttr::anim_shown_status(a, sizeof a) > 0)
                    k += std::snprintf(t + k, sizeof t - k, " | %s", a);
            }
            const size_t m = std::strlen(t) < cap ? std::strlen(t) : cap;
            std::memcpy(out, t, m);
            *outLen = m;
            return wo163::kROk;
        }
        case kOpConfig: {
            if (len != 5 || cap < 4) return wo163::kRBadRequest;
            uint32_t bits = 0;
            std::memcpy(&bits, body + 1, 4);
            const uint32_t was = g_bits.exchange(bits);
            if (was != bits || !g_configured.exchange(true))
                logf("WO202-CFG bits=0x%X (was 0x%X): crash_capture=%d near_safe=%d quest_fail_trace=%d anim_shown=%d recoil_clinch=%d "
                     "fight_pos_fresh=%d death_engine_only=%d", bits, was, (bits & kBitCrash) != 0, (bits & kBitNearSafe) != 0,
                     (bits & kBitQuestFail) != 0, (bits & kBitAnimShown) != 0, (bits & kBitRecoilClinch) != 0, (bits & kBitFightPosFresh) != 0,
                     (bits & kBitDeathEngineOnly) != 0);
            crash::set_on((bits & kBitCrash) != 0);
            put_u32(out, bits);
            *outLen = 4;
            return wo163::kROk;
        }
        case kOpFailPoll: {
            if (len != 5 || cap < 64) return wo163::kRBadRequest;
            uint32_t last = 0;
            std::memcpy(&last, body + 1, 4);
            std::lock_guard<std::mutex> lk(g_failMx);
            const Fail* f = nullptr;
            size_t after = 0;
            for (const auto& x : g_fails)
                if (x.seq > last) { if (!f) f = &x; ++after; }
            out[0] = after > 1 ? 1 : 0;
            if (!f) { *outLen = 1; return wo163::kROk; }
            c_polled.fetch_add(1);
            size_t o = 1;
            auto put_str = [&](const char* s, bool wide) {
                size_t l = std::strlen(s);
                const size_t room = cap > o + (wide ? 2 : 1) ? cap - o - (wide ? 2 : 1) : 0;
                if (l > room) l = room;
                if (!wide && l > 255) l = 255;
                if (wide) { const uint16_t w = static_cast<uint16_t>(l); std::memcpy(out + o, &w, 2); o += 2; }
                else out[o++] = static_cast<uint8_t>(l);
                std::memcpy(out + o, s, l); o += l;
            };
            put_u32(out + o, f->seq); o += 4;
            put_u32(out + o, static_cast<uint32_t>(GetTickCount64() - f->atMs)); o += 4;
            std::memcpy(out + o, &f->oldI, 4); o += 4;
            std::memcpy(out + o, &f->newI, 4); o += 4;
            out[o++] = f->cascade ? 1 : 0;
            const int32_t ran = f->ranMs >= 0 && f->ranMs < 0x7FFFFFFF ? static_cast<int32_t>(f->ranMs) : -1;
            std::memcpy(out + o, &ran, 4); o += 4;
            // the strings share what is left of the reply (the dispatcher's 512 bytes): paths are cut from their start, never their end
            const size_t pl = std::strlen(f->path), spl = std::strlen(f->srcPath);
            const char* path = f->path + (pl > 150 ? pl - 150 : 0);
            const char* srcPath = f->srcPath + (spl > 150 ? spl - 150 : 0);
            put_str(path, true); put_str(f->port, false); put_str(f->cls, false); put_str(srcPath, true); put_str(f->srcPort, false);
            *outLen = o;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

} // namespace kcdmp::wo202
