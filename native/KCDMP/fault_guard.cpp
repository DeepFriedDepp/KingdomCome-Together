// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-151 Phase 0.1: the fault guard's bookkeeping (fault_guard.h).
#include <windows.h>

#include "fault_guard.h"
#include "log.h"

#include <cstdio>
#include <cstring>
#include <mutex>

namespace kcdmp::fault {

namespace {

std::mutex        g_lock;            // the registry and the sum clock
Site*             g_head = nullptr;  // every site that has faulted at least once
std::atomic<bool> g_switchOff{true};
double            g_lastSumS = -1.0;
std::atomic<uint32_t> g_sinceSum{0}; // faults since the last FAULT-SUM line

void default_sink(const char* line) { kcdmp::logf("%s", line); }
std::atomic<Sink> g_sink{&default_sink};

void emit(const char* line) {
    Sink s = g_sink.load(std::memory_order_relaxed);
    if (s) s(line);
}

void enlist(Site& s) {
    if (s.listed.load(std::memory_order_acquire)) return;
    std::lock_guard<std::mutex> lk(g_lock);
    if (s.listed.load(std::memory_order_relaxed)) return;
    s.next = g_head;
    g_head = &s;
    s.listed.store(true, std::memory_order_release);
}

const char* ordinal(uint32_t n) {
    switch (n) { case 1: return "1st"; case 10: return "10th"; case 100: return "100th"; default: return "nth"; }
}

const char* access_word(int a) {
    switch (a) { case 0: return "reading"; case 1: return "writing"; case 8: return "executing"; default: return "touching"; }
}

} // namespace

void describe(uintptr_t addr, char* out, size_t cap) noexcept {
    if (!out || !cap) return;
    HMODULE mod = nullptr;
    if (addr && GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                   reinterpret_cast<LPCSTR>(addr), &mod) && mod) {
        char path[MAX_PATH]{};
        GetModuleFileNameA(mod, path, MAX_PATH);
        const char* base = path;
        for (const char* p = path; *p; ++p) if (*p == '\\' || *p == '/') base = p + 1;
        std::snprintf(out, cap, "%s+0x%llx", base[0] ? base : "?",
                      static_cast<unsigned long long>(addr - reinterpret_cast<uintptr_t>(mod)));
        return;
    }
    std::snprintf(out, cap, "0x%llx (no module)", static_cast<unsigned long long>(addr));
}

int filter(Site& s, struct _EXCEPTION_POINTERS* ep) noexcept {
    const EXCEPTION_RECORD* r = ep ? ep->ExceptionRecord : nullptr;
    s.lastCode.store(r ? static_cast<uint32_t>(r->ExceptionCode) : 0, std::memory_order_relaxed);
    s.lastAddr.store(r ? reinterpret_cast<uintptr_t>(r->ExceptionAddress) : 0, std::memory_order_relaxed);
    if (r && r->ExceptionCode == EXCEPTION_ACCESS_VIOLATION && r->NumberParameters >= 2) {
        s.lastAccess.store(static_cast<int>(r->ExceptionInformation[0]), std::memory_order_relaxed);
        s.lastData.store(static_cast<uintptr_t>(r->ExceptionInformation[1]), std::memory_order_relaxed);
    } else {
        s.lastAccess.store(-1, std::memory_order_relaxed);
        s.lastData.store(0, std::memory_order_relaxed);
    }
    return EXCEPTION_EXECUTE_HANDLER;
}

void on_fault(Site& s) noexcept {
    const uint32_t n = s.faults.fetch_add(1, std::memory_order_relaxed) + 1;
    g_sinceSum.fetch_add(1, std::memory_order_relaxed);
    enlist(s);
    if (n == 1 || n == 10 || n == 100) {
        char where[160], touched[96] = "";
        describe(s.lastAddr.load(std::memory_order_relaxed), where, sizeof where);
        const int acc = s.lastAccess.load(std::memory_order_relaxed);
        if (acc >= 0)
            std::snprintf(touched, sizeof touched, " %s 0x%llx", access_word(acc),
                          static_cast<unsigned long long>(s.lastData.load(std::memory_order_relaxed)));
        char line[400];
        std::snprintf(line, sizeof line, "FAULT %s: 0x%08X%s at %s (%s)%s", s.name,
                      s.lastCode.load(std::memory_order_relaxed), touched, where, ordinal(n),
                      s.kind == Kind::Read ? " [read site: counted, never switched off]" : "");
        emit(line);
    }
    if (s.kind == Kind::Call && n >= kSwitchOffAfter && g_switchOff.load(std::memory_order_relaxed) &&
        !s.off.exchange(true, std::memory_order_relaxed)) {
        char line[300];
        std::snprintf(line, sizeof line,
                      "FAULT %s: switched off after %u faults this session -- its calls now return 'failed' "
                      "without calling the game (mp_fault_switchoff; a restart of the game clears it)",
                      s.name, n);
        emit(line);
    }
}

void tick(double nowS) noexcept {
    if (g_lastSumS < 0) { g_lastSumS = nowS; return; }
    if (nowS - g_lastSumS < kSumPeriodS) return;
    g_lastSumS = nowS;
    std::lock_guard<std::mutex> lk(g_lock);
    if (!g_head) return;
    char line[1200];
    uint32_t total = 0, sites = 0, off = 0;
    for (Site* s = g_head; s; s = s->next) {
        ++sites;
        total += s->faults.load(std::memory_order_relaxed);
        if (s->off.load(std::memory_order_relaxed)) ++off;
    }
    int len = std::snprintf(line, sizeof line, "FAULT-SUM %us: %u site(s), %u fault(s) this session, %u new, %u switched off |",
                            static_cast<unsigned>(kSumPeriodS), sites, total,
                            g_sinceSum.exchange(0, std::memory_order_relaxed), off);
    for (Site* s = g_head; s && len > 0 && len < static_cast<int>(sizeof line) - 80; s = s->next) {
        const uint32_t f = s->faults.load(std::memory_order_relaxed);
        const uint32_t was = s->summed.exchange(f, std::memory_order_relaxed);
        len += std::snprintf(line + len, sizeof line - len, " %s=%u%s%s", s->name, f,
                             f > was ? "(+)" : "", s->off.load(std::memory_order_relaxed) ? "(off)" : "");
    }
    emit(line);
}

void set_switch_off(bool on) noexcept {
    const bool was = g_switchOff.exchange(on);
    if (was != on) {
        char line[200];
        std::snprintf(line, sizeof line, "FAULT-CONFIG mp_fault_switchoff=%s (was %s)%s", on ? "on" : "off",
                      was ? "on" : "off", on ? "" : " -- a game-code site keeps running after 8 faults");
        emit(line);
    }
}

bool switch_off() noexcept { return g_switchOff.load(); }

Totals totals() noexcept {
    Totals t{};
    std::lock_guard<std::mutex> lk(g_lock);
    for (Site* s = g_head; s; s = s->next) {
        ++t.sites;
        t.faults += s->faults.load(std::memory_order_relaxed);
        if (s->off.load(std::memory_order_relaxed)) ++t.off;
    }
    return t;
}

void set_sink(Sink s) noexcept { g_sink.store(s ? s : &default_sink); }

void reset_for_tests() noexcept {
    std::lock_guard<std::mutex> lk(g_lock);
    for (Site* s = g_head; s;) {
        Site* nx = s->next;
        s->faults.store(0); s->summed.store(0); s->off.store(false); s->listed.store(false);
        s->next = nullptr;
        s = nx;
    }
    g_head = nullptr;
    g_lastSumS = -1.0;
    g_sinceSum.store(0);
    g_switchOff.store(true);
}

} // namespace kcdmp::fault
