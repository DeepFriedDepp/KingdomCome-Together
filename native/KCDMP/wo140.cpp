// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-140 -- see wo140.h.
#include "wo140.h"

#include <windows.h>
#include <intrin.h>
#include <atomic>
#include <cstdio>
#include <cstring>

#include "anchors.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "main_thread.h"
#include "pe_exports.h"
#include "wo140_rules.h"

namespace kcdmp::wo140 {
namespace {

// C_SkipTime::ShowDialog: mov rax,rsp; mov [rax+8],rbx; mov [rax+10h],rsi; mov [rax+18h],rdi; push rbp
// (16 bytes, instruction-aligned, no RIP-relative operand; the next is lea rbp,[rax-18h]).
constexpr auto& kShowPrologue = hookpro::kSkipTimeShow;   // hook_prologues.h (WO-148)
// C_Calendar::SetWorldTime(this, int64 ms): mov [rsp+8],rbx; mov [rsp+10h],rsi; push rdi; sub rsp,40h; mov rbx,rcx; mov rdi,rdx
constexpr uint8_t kSetTimePrologue[21] = {
    0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0xD9, 0x48, 0x8B, 0xFA,
};
constexpr const char* kIName = "?I@C_SkipTime@playermodule@wh@@SAAEAV123@XZ";
constexpr const char* kGiName = "?GetGameIface@wh@@YAPEBVC_GameInterface@shared@1@XZ";

constexpr size_t kOffState = 0x68, kOffId = 0x6C, kOffHours = 0x74;   // C_SkipTime
constexpr size_t kOffCalendar = 0x1B0;                                  // C_GameInterface
constexpr size_t kOffCalMs = 0x68;                                      // C_Calendar
constexpr size_t kSlotStart = 5, kSlotStop = 6, kSlotShow = 14;

using ShowFn    = bool (__fastcall*)(void*, int, float, float, const void*, float);
using StartFn   = bool (__fastcall*)(void*, int, uint32_t, const void*);   // hours arrive as bits in r8 (wh_pl_ForcedSkipTime's caller)
using StopFn    = uint8_t (__fastcall*)(void*, int, bool);
using SetTimeFn = void (__fastcall*)(void*, int64_t);
using IFn       = void* (*)();
using GiFn      = const void* (*)();

ShowFn g_show = nullptr;
SetTimeFn g_setTime = nullptr;
IFn g_I = nullptr;
GiFn g_gi = nullptr;
void* g_inst = nullptr;                // main thread, resolved on first use
bool g_instTried = false;
std::atomic<bool> g_hook{false};
std::atomic<bool> g_on{false};
const char* g_why = "not installed";
const char* g_calWhy = "not installed";
char g_where[96] = "?";

// The kept picker (the gate's arguments), main thread only.
struct Held { bool valid; void* self; int id; float f1, f2, f6; uint8_t spot[16]; };
Held g_held{};
std::atomic<bool> g_heldNew{false};
std::atomic<uint64_t> g_approveUntil{0};
wo140rules::Tracker g_track{};
FrameFn g_frame = nullptr;
std::atomic<uint32_t> c_held{0}, c_passed{0}, c_replayed{0}, c_edges{0}, c_started{0}, c_startRefused{0}, c_stopped{0}, c_pulled{0};

bool rd(const void* p, void* out, size_t n) {
    __try { std::memcpy(out, p, n); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

// The gate: rcx = the C_SkipTime, edx = the skip id. True = the picker does not open.
bool __fastcall gate_cb(void* self, void* a2) {
    const int id = static_cast<int>(static_cast<uint32_t>(reinterpret_cast<uintptr_t>(a2)));
    if (!g_on.load(std::memory_order_relaxed) || !wo140rules::gated_id(id)) return false;
    const uint64_t until = g_approveUntil.load();
    if (until && GetTickCount64() <= until) { g_approveUntil = 0; c_passed.fetch_add(1); return false; }
    // Kept: the arguments the thunk left on the stack (wo140_rules.h: the frame).
    const uint8_t* ret = static_cast<const uint8_t*>(_AddressOfReturnAddress());
    const uint8_t* t = ret + wo140rules::kThunkRetToT;
    const uint8_t* e = t + wo140rules::kTEntry;
    Held h{};
    h.self = self; h.id = id;
    const void* spot = nullptr;
    bool ok = rd(t + wo140rules::kTXmm2, &h.f1, 4) && rd(t + wo140rules::kTXmm3, &h.f2, 4)
           && rd(e + wo140rules::kEntryArg5, &spot, 8) && rd(e + wo140rules::kEntryArg6, &h.f6, 4);
    if (ok && spot) ok = rd(spot, h.spot, 16);
    h.valid = ok;
    g_held = h;
    g_heldNew = true;
    c_held.fetch_add(1);
    return true;
}

void* instance() {
    if (g_inst || g_instTried || !g_I) return g_inst;
    g_instTried = true;
    void* inst = nullptr;
    __try { inst = g_I(); } __except (EXCEPTION_EXECUTE_HANDLER) { inst = nullptr; }
    void* const* vt = nullptr;
    void* slot = nullptr;
    if (!inst || !rd(inst, &vt, 8) || !vt || !rd(vt + kSlotShow, &slot, 8) || slot != reinterpret_cast<void*>(g_show)) {
        g_why = "C_SkipTime's vftable slot 14 is not ShowDialog";
        logf("WO140-BUILD C_SkipTime instance NOT trusted -- %s (inst=%p)", g_why, inst);
        return nullptr;
    }
    g_inst = inst;
    logf("WO140-BUILD C_SkipTime instance %p (vftable slot 14 = ShowDialog, checked)", inst);
    return g_inst;
}

bool read_state(void* inst, uint32_t* st, int* id, float* hours) {
    return rd(static_cast<uint8_t*>(inst) + kOffState, st, 4) && rd(static_cast<uint8_t*>(inst) + kOffId, id, 4)
        && rd(static_cast<uint8_t*>(inst) + kOffHours, hours, 4);
}

void* vslot(void* inst, size_t slot) {
    void* const* vt = nullptr; void* fn = nullptr;
    if (!rd(inst, &vt, 8) || !vt || !rd(vt + slot, &fn, 8)) return nullptr;
    return fn;
}

bool call_show(const Held& h, bool* shown) {
    __try { *shown = g_show(h.self, h.id, h.f1, h.f2, h.spot, h.f6); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool call_start(StartFn fn, void* inst, int id, float hours, bool* started) {
    static const uint8_t kNoSpot[16] = {};
    __try { *started = fn(inst, id, wo140rules::float_bits(hours), kNoSpot); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool call_stop(StopFn fn, void* inst) {
    __try { fn(inst, 0, false); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
void* calendar() {
    if (!g_gi) return nullptr;
    const void* gi = nullptr;
    __try { gi = g_gi(); } __except (EXCEPTION_EXECUTE_HANDLER) { gi = nullptr; }
    void* cal = nullptr;
    if (!gi || !rd(static_cast<const uint8_t*>(gi) + kOffCalendar, &cal, 8)) return nullptr;
    return cal;
}
bool call_set_time(void* cal, int64_t ms) {
    __try { g_setTime(cal, ms); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool write_ms(void* cal, int64_t ms) {
    __try { *reinterpret_cast<int64_t*>(static_cast<uint8_t*>(cal) + kOffCalMs) = ms; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

void send_state(wo140rules::Edge e, int id, float hours, uint32_t st) {
    uint8_t body[1 + 1 + 1 + 4 + 1]{};
    body[0] = kFrameState;
    body[1] = static_cast<uint8_t>(e);
    body[2] = static_cast<uint8_t>(id);
    std::memcpy(body + 3, &hours, 4);
    body[7] = static_cast<uint8_t>(st);
    if (g_frame) g_frame(body, sizeof body);
}

} // namespace

void install() {
    HMODULE pm = GetModuleHandleA("PlayerModule.dll");
    HMODULE rpg = GetModuleHandleA("RPGModule.dll");
    HMODULE sh = GetModuleHandleA("Shared.dll");
    if (!pm) { g_why = "PlayerModule.dll not loaded"; logf("WO140-BUILD sleep gate NOT armed -- %s", g_why); return; }
    int n = 0;
    const uint8_t* show = anchor::function_by_string(pm, "wh::playermodule::C_SkipTime::ShowDialog", &n);
    uint8_t b[sizeof kShowPrologue]{};
    if (!show || n != 1 || !rd(show, b, sizeof b) || std::memcmp(b, kShowPrologue, sizeof b) != 0) {
        g_why = "C_SkipTime::ShowDialog not found (string + prologue)";
        logf("WO140-BUILD sleep gate NOT armed -- %s (%d functions load the string)", g_why, n);
    } else {
        g_show = reinterpret_cast<ShowFn>(const_cast<uint8_t*>(show));
        g_I = reinterpret_cast<IFn>(find_export(module_exports(pm), kIName));
        if (!g_I) { g_why = "no C_SkipTime::I export"; logf("WO140-BUILD sleep gate NOT armed -- %s", g_why); g_show = nullptr; }
        else {
            const char* why = nullptr;
            if (!inlinehook::install_gate(const_cast<uint8_t*>(show), kShowPrologue, sizeof kShowPrologue, &gate_cb, &why)) {
                g_why = why ? why : "install failed";
                logf("WO140-BUILD sleep gate NOT armed -- %s", g_why);
            } else {
                anchor::describe(show, g_where, sizeof g_where);
                g_hook = true; g_why = "";
                logf("WO140-BUILD sleep gate ARMED at %s (C_SkipTime::ShowDialog): off until the agent's Config; the instance is checked on first use", g_where);
            }
        }
    }
    // The calendar pull (independent of the gate).
    int m = 0;
    const uint8_t* st = rpg ? anchor::function_by_string(rpg, "wh::rpgmodule::C_Calendar::SetWorldTime", &m) : nullptr;
    uint8_t c[sizeof kSetTimePrologue]{};
    if (!st || m != 1 || !rd(st, c, sizeof c) || std::memcmp(c, kSetTimePrologue, sizeof c) != 0) {
        g_calWhy = "C_Calendar::SetWorldTime not found (string + prologue)";
    } else if (!sh || !(g_gi = reinterpret_cast<GiFn>(GetProcAddress(sh, kGiName)))) {
        g_calWhy = "no wh::GetGameIface export";
    } else {
        g_setTime = reinterpret_cast<SetTimeFn>(const_cast<uint8_t*>(st));
        g_calWhy = "";
    }
    char at[96] = "?";
    if (g_setTime) anchor::describe(reinterpret_cast<const void*>(g_setTime), at, sizeof at);
    logf("WO140-BUILD clock pull %s%s%s", g_setTime ? "ARMED (C_Calendar::SetWorldTime " : "NOT armed -- ", g_setTime ? at : g_calWhy, g_setTime ? ")" : "");
}

void set_frame_callback(FrameFn fn) { g_frame = fn; }

void on_pipe_closed() {
    g_on = false;
    g_approveUntil = 0;
    main_thread::post([] { g_held = {}; });
}

void tick() {
    if (!g_on.load(std::memory_order_relaxed)) { g_track = {}; return; }
    if (g_heldNew.exchange(false)) {
        const Held h = g_held;
        logf("WO140-HELD a %s picker (id %d) did not open -- a vote first%s", wo140rules::id_name(h.id), h.id,
             h.valid ? "" : " (its arguments were unreadable: it cannot be shown again)");
        uint8_t body[2] = { kFrameHeld, static_cast<uint8_t>(h.id) };
        if (g_frame) g_frame(body, sizeof body);
    }
    void* inst = instance();
    if (!inst) return;
    uint32_t stt = 0; int id = 0; float hours = 0;
    if (!read_state(inst, &stt, &id, &hours)) return;
    const uint32_t was = g_track.prev;
    if (stt == was) return;
    const auto e = g_track.step(stt);
    logf("WO140-STATE %u -> %u%s%s id=%d (%s) hours=%.2f", was, stt, e != wo140rules::Edge::None ? " " : "",
         e != wo140rules::Edge::None ? wo140rules::edge_name(e) : "", id, wo140rules::id_name(id), hours);
    if (e != wo140rules::Edge::None) {
        c_edges.fetch_add(1);
        send_state(e, id, hours, stt);
    }
}

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (!len) return kRBadRequest;
    switch (body[0]) {
        case kOpConfig: {
            if (len != 2 || cap < 2) return kRBadRequest;
            const bool on = body[1] != 0;
            void* inst = g_hook.load() ? instance() : nullptr;
            const bool armed = g_hook.load() && inst;
            if (on && !armed) { out[0] = 0; out[1] = 0xFF; *outLen = 2; return kRNotArmed; }
            const bool was = g_on.exchange(on);
            if (on != was) {
                logf("WO140-CONFIG sleep gate %s", on ? "ON (a session with a partner: a sleep or a wait waits for everyone's yes)" : "off");
                if (!on) { g_held = {}; g_approveUntil = 0; }
            }
            uint32_t stt = 0; int id = 0; float h = 0;
            if (inst && read_state(inst, &stt, &id, &h)) { if (on && !was) { g_track = {}; g_track.prev = stt; } } else stt = 0xFF;
            out[0] = armed ? 1 : 0; out[1] = static_cast<uint8_t>(stt);
            *outLen = 2;
            return kROk;
        }
        case kOpApprove: {
            if (len != 5 || cap < 1) return kRBadRequest;
            uint32_t ms = 0; std::memcpy(&ms, body + 1, 4);
            if (ms > 120000) ms = 120000;
            g_approveUntil = GetTickCount64() + ms;
            out[0] = 0; *outLen = 1;
            if (g_held.valid && g_show) {
                const Held h = g_held;
                g_held = {};
                bool shown = false;
                const bool ran = call_show(h, &shown);
                c_replayed.fetch_add(1);
                out[0] = ran && shown ? 1 : 0;
                logf("WO140-APPROVE the kept %s picker shown again -> %s", wo140rules::id_name(h.id), !ran ? "FAULTED" : shown ? "open" : "the game said no");
            } else {
                logf("WO140-APPROVE the next sleep / wait picker passes (%u ms)", ms);
            }
            return kROk;
        }
        case kOpDrop: {
            out[0] = g_held.valid ? 1 : 0; *outLen = 1;
            if (g_held.valid) logf("WO140-DROP the kept %s picker forgotten (no vote)", wo140rules::id_name(g_held.id));
            g_held = {}; g_approveUntil = 0;
            return kROk;
        }
        case kOpStart: {
            if (len != 6 || cap < 1) return kRBadRequest;
            const int id = body[1];
            float hours = 0; std::memcpy(&hours, body + 2, 4);
            *outLen = 1;
            if (!wo140rules::hours_ok(hours) || !wo140rules::start_id_ok(id)) { out[0] = 3; return kROk; }
            void* inst = instance();
            if (!inst) return kRNotArmed;
            uint32_t stt = 0; int cur = 0; float h = 0;
            if (!read_state(inst, &stt, &cur, &h)) return kRFailed;
            if (stt != 0) { out[0] = 2; logf("WO140-START refused: this game's own skip is running (state %u, id %d)", stt, cur); return kROk; }
            auto fn = reinterpret_cast<StartFn>(vslot(inst, kSlotStart));
            if (!fn) return kRFailed;
            g_approveUntil = GetTickCount64() + 2000;   // the start's own ShowDialog passes the gate
            bool started = false;
            const bool ran = call_start(fn, inst, id, hours, &started);
            g_approveUntil = 0;
            if (ran && started) c_started.fetch_add(1); else c_startRefused.fetch_add(1);
            out[0] = ran && started ? 1 : 0;
            logf("WO140-START %s for %.2f h (id %d) -> %s", wo140rules::id_name(id), hours, id, !ran ? "FAULTED" : started ? "the game's own skip runs" : "the game said no");
            return ran ? kROk : kRFailed;
        }
        case kOpStop: {
            void* inst = instance();
            *outLen = 1; out[0] = 0;
            if (!inst) return kRNotArmed;
            uint32_t stt = 0; int id = 0; float h = 0;
            if (!read_state(inst, &stt, &id, &h) || stt == 0) return kROk;
            auto fn = reinterpret_cast<StopFn>(vslot(inst, kSlotStop));
            if (!fn || !call_stop(fn, inst)) return kRFailed;
            c_stopped.fetch_add(1);
            out[0] = 1;
            logf("WO140-STOP the running %s skip ended now (state was %u)", wo140rules::id_name(id), stt);
            return kROk;
        }
        case kOpPull: {
            if (len != 9 || cap < 9) return kRBadRequest;
            uint32_t expect = 0, target = 0;
            std::memcpy(&expect, body + 1, 4); std::memcpy(&target, body + 5, 4);
            if (!g_setTime) return kRNotArmed;
            void* cal = calendar();
            int64_t cur = 0;
            if (!cal || !rd(static_cast<uint8_t*>(cal) + kOffCalMs, &cur, 8)) return kRFailed;
            auto v = wo140rules::pull_verdict(cur, expect, target);
            // WO-144 3.3: never mid-skip (a clock write ends a running skip, WO-140): the agent tries again
            if (v == wo140rules::Pull::Pulled) {
                if (void* inst = instance()) { uint32_t stt = 0; int sid = 0; float sh = 0; if (read_state(inst, &stt, &sid, &sh) && stt != 0) v = wo140rules::Pull::SkipRunning; }
            }
            uint32_t before = static_cast<uint32_t>(cur / 1000), after = before;
            if (v == wo140rules::Pull::Pulled) {
                const int64_t tms = static_cast<int64_t>(target) * 1000;
                if (!write_ms(cal, tms - 1) || !call_set_time(cal, tms)) { write_ms(cal, cur); return kRFailed; }
                int64_t now = 0;
                rd(static_cast<uint8_t*>(cal) + kOffCalMs, &now, 8);
                after = static_cast<uint32_t>(now / 1000);
                c_pulled.fetch_add(1);
            }
            out[0] = static_cast<uint8_t>(v);
            std::memcpy(out + 1, &before, 4); std::memcpy(out + 5, &after, 4);
            *outLen = 9;
            logf("WO140-PULL the clock %u -> %u (target %u, the agent read %u): %s", before, after, target, expect,
                 v == wo140rules::Pull::Pulled ? "pulled back to the host's" : v == wo140rules::Pull::NotNeeded ? "not ahead, nothing done"
                 : v == wo140rules::Pull::SkipRunning ? "waits: this game's own skip is running" : "REFUSED: the calendar went back past the agent's reading");
            return kROk;
        }
        case kOpClock: {
            if (cap < 4) return kRBadRequest;
            void* cal = calendar();
            int64_t cur = 0;
            if (!cal || !rd(static_cast<uint8_t*>(cal) + kOffCalMs, &cur, 8)) return kRFailed;
            const uint32_t s = static_cast<uint32_t>(cur / 1000);
            std::memcpy(out, &s, 4); *outLen = 4;
            return kROk;
        }
        case kOpStatus: {
            void* inst = g_hook.load() ? instance() : nullptr;
            uint32_t stt = 0xFF; int id = -1; float h = 0;
            if (inst) read_state(inst, &stt, &id, &h);
            char line[320];
            const int k = std::snprintf(line, sizeof line,
                "WO140-NATIVE gate armed=%d on=%d at=%s instance=%s state=%u id=%d hours=%.2f held=%u passed=%u replayed=%u edges=%u started=%u start_refused=%u stopped=%u clock_pull=%s pulled=%u%s%s",
                g_hook.load() ? 1 : 0, g_on.load() ? 1 : 0, g_where, inst ? "ok" : "none", stt, id, h, c_held.load(), c_passed.load(), c_replayed.load(),
                c_edges.load(), c_started.load(), c_startRefused.load(), c_stopped.load(), g_setTime ? "armed" : "off", c_pulled.load(),
                g_hook.load() ? "" : " why=", g_hook.load() ? "" : g_why);
            if (k <= 0) return kRFailed;
            const size_t mm = static_cast<size_t>(k) < cap ? static_cast<size_t>(k) : cap;
            std::memcpy(out, line, mm);
            *outLen = mm;
            return kROk;
        }
    }
    return kRBadRequest;
}

} // namespace kcdmp::wo140
