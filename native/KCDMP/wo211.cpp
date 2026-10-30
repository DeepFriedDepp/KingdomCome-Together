// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-211: the 0.63.9 beta patch's native half (wo211.h).
#include "wo211.h"
#include "wo211_rules.h"
#include "wo208.h"
#include "wo203.h"
#include "wo163.h"
#include "anchors.h"
#include "fault_guard.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "main_thread.h"
#include "npc_drive.h"
#include "engine.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>

#ifndef KCDMP_BUILD_ID
#define KCDMP_BUILD_ID "dev"
#endif

namespace kcdmp::wo211 {

namespace {

using namespace wo211rules;

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W211_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO211-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W211_OFF=%s)", s, b);
    }
    return s;
}

// ---- B: the guard on CryAction's element-listener Lua dispatch ------------------------------------------------------------------
fault::Site g_siteRead{"wo211::ui-read", fault::Kind::Read};
std::atomic<bool> g_uiArmed{false};
const char* g_uiWhy = "not installed";
std::atomic<uint32_t> c_uiOffMain{0}, c_uiSkipped{0}, c_uiMatched{0}, c_uiLetThrough{0}, c_uiOnMain{0};
std::atomic<uint32_t> c_uiLastTid{0};
char g_uiLast[96] = "-";   // the last off-main event (element/event), written by the gate's thread under its own lock
SRWLOCK g_uiLastLock = SRWLOCK_INIT;

// The listener container's entries and an event's info, as CryAction's dispatch reads them (B.1's disassembly):
//   container: [+0] begin, [+8] end of 16-byte slots whose first qword is the listener (null = a free slot)
//   listener:  [+0x18] instance (int32, -1 any), [+0x20] event name (char*), [+0x28] element name (char*)
//   info:      [+0] instance (int32), [+8] event name (char*), [+0x10] element name (char*)
bool any_listener_matches(void* container, void* info, char* lastOut, size_t lastCap) {
    bool matched = false;
    fault::guarded(g_siteRead, [&] {
        auto c = static_cast<uint8_t* const*>(container);
        const uint8_t* b = c[0];
        const uint8_t* e = c[1];
        auto in = static_cast<const uint8_t*>(info);
        const int32_t inst = *reinterpret_cast<const int32_t*>(in);
        const char* ev = *reinterpret_cast<const char* const*>(in + 8);
        const char* el = *reinterpret_cast<const char* const*>(in + 0x10);
        if (lastOut) std::snprintf(lastOut, lastCap, "%.40s/%.40s", el ? el : "?", ev ? ev : "?");
        if (!b || !e || e < b || static_cast<size_t>(e - b) > 16u * 4096u) return;
        for (const uint8_t* s = b; s < e; s += 16) {
            const uint8_t* l = *reinterpret_cast<const uint8_t* const*>(s);
            if (!l) continue;
            const int32_t li = *reinterpret_cast<const int32_t*>(l + 0x18);
            const char* lev = *reinterpret_cast<const char* const*>(l + 0x20);
            const char* lel = *reinterpret_cast<const char* const*>(l + 0x28);
            if (listener_matches(lel, lev, li, el, ev, inst)) { matched = true; return; }
        }
    });
    return matched;
}

// The gate (any thread the engine dispatches on): true = the dispatch is not run (it returns nothing; its caller reads nothing back).
bool ui_gate(void* container, void* info) {
    const unsigned long mainTid = main_thread::main_thread_id();
    const DWORD tid = GetCurrentThreadId();
    const Ui d = ui_dispatch(mainTid != 0, tid == mainTid, on(kBitUiLuaMainOnly));
    if (d == Ui::Run) { c_uiOnMain.fetch_add(1, std::memory_order_relaxed); return false; }
    char last[96] = "-";
    const bool matched = any_listener_matches(container, info, last, sizeof last);
    const uint32_t k = c_uiOffMain.fetch_add(1) + 1;
    if (matched) c_uiMatched.fetch_add(1);
    (d == Ui::Skip ? c_uiSkipped : c_uiLetThrough).fetch_add(1);
    c_uiLastTid.store(tid);
    AcquireSRWLockExclusive(&g_uiLastLock);
    std::memcpy(g_uiLast, last, sizeof g_uiLast);
    ReleaseSRWLockExclusive(&g_uiLastLock);
    if (k <= 20 || k % 100 == 0)
        logf("WO211-UILUA %s an element event's Lua dispatch on thread %lu (not the main thread %lu): %s%s [n=%u]",
             d == Ui::Skip ? "skipped" : "TWIN let through", static_cast<unsigned long>(tid), mainTid, last,
             matched ? " -- a listener of the mod's matched it (dropped)" : " -- no listener takes it", k);
    return d == Ui::Skip;
}

void install_ui() {
    HMODULE ca = GetModuleHandleA("CryAction.dll");
    if (!ca) { g_uiWhy = "CryAction not loaded"; logf("WO211-UILUA not armed: %s", g_uiWhy); return; }
    int n = 0;
    const uint8_t* unload = anchor::function_by_string(ca, "OnUnload", &n);
    const char* tag = anchor::find_cstring(ca, "__ui_action_name");
    if (!unload || !tag) { g_uiWhy = "the element listeners' OnUnload or the dispatch's label is not there once"; logf("WO211-UILUA not armed: %s (%d)", g_uiWhy, n); return; }
    // OnUnload's own dispatch: the one function it calls that reads the listeners' "__ui_action_name"
    const uint8_t* targets[64]{};
    const int nt = anchor::function_call_targets(ca, unload, targets, 64);
    const uint8_t* fn = nullptr;
    int hits = 0;
    for (int i = 0; i < nt; ++i)
        if (targets[i] && anchor::function_refs(ca, targets[i], tag)) { fn = targets[i]; ++hits; }
    if (!fn || hits != 1) { g_uiWhy = "OnUnload does not call exactly one function that reads __ui_action_name"; logf("WO211-UILUA not armed: %s (%d of %d)", g_uiWhy, hits, nt); return; }
    const char* why = nullptr;
    if (!inlinehook::install_gate(const_cast<uint8_t*>(fn), hookpro::kUiElementLuaNotify, sizeof hookpro::kUiElementLuaNotify, &ui_gate, &why)) {
        g_uiWhy = "the patch was refused (another build's prologue)"; logf("WO211-UILUA not armed: %s (%s)", g_uiWhy, why ? why : "?"); return;
    }
    g_uiArmed.store(true);
    g_uiWhy = "armed";
    char a[96];
    anchor::describe(fn, a, sizeof a);
    logf("WO211-UILUA armed: the element listeners' Lua dispatch %s (OnUnload's call, label + prologue) runs on the main thread only; "
         "off it the event is counted and not dispatched (twin: KCDMP_TEST_W211_OFF=ui_lua_main_only)", a);
}

// ---- C ----------------------------------------------------------------------------------------------------------------------------
std::atomic<uint32_t> c_ovAsks{0}, c_ovOwned{0}, c_ovNotOwned{0}, c_ovInstalled{0}, c_ovThrottled{0}, c_hoQueueFull{0};

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 5 || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true))
            logf("WO211-CFG bits=0x%X (was 0x%X; twin off 0x%X): ui_lua_main_only=%d overflow_reseat=%d handover_queue_cap=%d build=%s",
                 bits, was, twin_off(), on(kBitUiLuaMainOnly), on(kBitOverflowReseat), on(kBitHandoverQueueCap), build_id());
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[1024];
        int n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) >= sizeof t) n = static_cast<int>(sizeof t) - 1;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    if (opc == kOpOverflowReseat) {
        // [op][nameLen:1][name]: C.3 -- the engine said the copy's action queue is over-full. Main thread.
        if (len < 3 || body[1] == 0 || static_cast<size_t>(body[1]) + 2 != len || cap < 1) return wo163::kRBadRequest;
        char name[128]{};
        std::memcpy(name, body + 2, body[1] < sizeof name - 1 ? body[1] : sizeof name - 1);
        c_ovAsks.fetch_add(1);
        uint8_t r = 0;   // 0 off, 1 reseated, 2 not an owned copy, 3 throttled
        if (on(kBitOverflowReseat)) {
            void* e = npcdrive::entity_by_name(name);
            const uint32_t eid = e ? engine::entity_id(e) : 0;
            r = wo208::overflow_reseat(eid, name, kOverflowReseatEveryS);
        }
        out[0] = r;
        *outLen = 1;
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

int status_reg(char* out, int n) { return status(out, n); }

} // namespace

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }

void note_overflow_reseat(bool owned, bool installed) {
    (owned ? c_ovOwned : c_ovNotOwned).fetch_add(1);
    if (installed) c_ovInstalled.fetch_add(1);
}
void note_handover_queue_full() { c_hoQueueFull.fetch_add(1); }

const char* build_id() {
    static const char* const s = build_id_ok(KCDMP_BUILD_ID) ? KCDMP_BUILD_ID : "dev";
    return s;
}

int status(char* out, int n) {
    char last[96];
    AcquireSRWLockShared(&g_uiLastLock);
    std::memcpy(last, g_uiLast, sizeof last);
    ReleaseSRWLockShared(&g_uiLastLock);
    return std::snprintf(out, static_cast<size_t>(n),
                         "w211 bits=0x%X twin_off=0x%X configured=%d build=%s | B ui_lua armed=%d (%s) off_main=%u skipped=%u matched=%u let_through=%u "
                         "on_main=%u last=%s tid=%u | C overflow asks=%u reseated=%u not_owned=%u installed=%u handover_queue_full=%u",
                         g_bits.load(), twin_off(), g_configured.load() ? 1 : 0, build_id(), g_uiArmed.load() ? 1 : 0, g_uiWhy, c_uiOffMain.load(),
                         c_uiSkipped.load(), c_uiMatched.load(), c_uiLetThrough.load(), c_uiOnMain.load(), last, c_uiLastTid.load(), c_ovAsks.load(),
                         c_ovOwned.load(), c_ovNotOwned.load(), c_ovInstalled.load(), c_hoQueueFull.load());
}

namespace {
void install_reg() { install_ui(); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_op(kOpOverflowReseat, &op) &&
                  wo203::reg_status("w211", &status_reg) && wo203::reg_install("w211", &install_reg);
} // namespace

} // namespace kcdmp::wo211
