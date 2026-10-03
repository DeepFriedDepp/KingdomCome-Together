// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-151 Phase 0.1: the one fault guard (docs/WO-148-findings.md s7.12 item 1).
//
// Every structured-exception guard in this DLL goes through guarded() / guarded_or()
// with a named Site. Before WO-151 each of the 286 guards was a raw __try whose
// handler returned quietly; one of them (the stamina read in rttr::sample_health)
// faulted inside the game's stat getter thousands of times and left an entry on the
// game's own stat stack each time, with no line in any log (the 0.42.5-0.42.7 frame
// rate collapse). Now:
//
//   * the first fault at a site is logged at once, with the exception code and the
//     faulting instruction as module+offset (and, for an access violation, the
//     address it touched), again at the 10th and the 100th;
//   * while any count is non-zero, one FAULT-SUM line every 60 s lists them;
//   * a Call site (a call into the game's code: a fault there can leave the game's own
//     state half-changed) is switched off after kSwitchOffAfter faults in a session:
//     its guarded() returns false without calling the game, and the log says so.
//     mp_fault_switchoff off (pipe 0x2A op 1) keeps every site running.
//   * a Read site (our own read or write of game memory; no game code runs) is counted
//     and logged the same way but never switched off: a stale pointer changes nothing
//     in the game, and several of these are probes that expect to fault.
//
// "A session" is one run of the game (the DLL's lifetime): the damage a fault in game
// code can do (WO-148: the leaked stat stack) lasts until the game restarts, so a
// reconnect does not clear a switched-off site. reset_for_tests() is for the tests.
//
// The rule the gate enforces (tools/Test-NativeGuards.ps1): no raw __try anywhere in
// native/KCDMP except in this header. The body handed to guarded() is the call or the
// read and nothing that needs unwinding (C2712 applies to the lambda's frame as it did
// to the old helpers).

// Deliberately not <windows.h>: this header goes into every file of the DLL, and
// several define NOMINMAX (or other switches) before their own <windows.h>.
#include <excpt.h>   // EXCEPTION_EXECUTE_HANDLER, GetExceptionInformation
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <type_traits>

struct _EXCEPTION_POINTERS;

namespace kcdmp::fault {

enum class Kind : uint8_t {
    Call,   // a call into the game's code: switched off after kSwitchOffAfter faults
    Read,   // our own read / write of game memory: counted and logged, never switched off
};

constexpr uint32_t kSwitchOffAfter = 8;
constexpr double   kSumPeriodS     = 60.0;

struct Site {
    const char* const name;
    const Kind        kind;
    std::atomic<uint32_t>  faults{0};
    std::atomic<uint32_t>  summed{0};      // the count the last FAULT-SUM line reported
    std::atomic<bool>      off{false};
    std::atomic<bool>      listed{false};
    std::atomic<uint32_t>  lastCode{0};
    std::atomic<uintptr_t> lastAddr{0};    // the faulting instruction
    std::atomic<uintptr_t> lastData{0};    // access violation: the address touched
    std::atomic<int>       lastAccess{-1}; // access violation: 0 read, 1 write, 8 execute
    Site* next = nullptr;                  // the registry (sites that faulted), under its lock

    constexpr Site(const char* n, Kind k) noexcept : name(n), kind(k) {}
    Site(const Site&) = delete;
    Site& operator=(const Site&) = delete;
};

// The __except filter: notes the fault on the site; always EXCEPTION_EXECUTE_HANDLER
// (every guard caught everything before WO-151, and still does).
int filter(Site& s, struct _EXCEPTION_POINTERS* ep) noexcept;
// The __except handler's one call: counts, logs, switches a Call site off.
void on_fault(Site& s) noexcept;

inline bool enabled(const Site& s) noexcept { return !s.off.load(std::memory_order_relaxed); }

// WO-153 5: an address no user-mode pointer can hold -- the null page, or above the canonical range. A read through one is
// refused before it happens (no fault, no log line). The field's `reading 0xffffffffffffffff` was a pointer slot that held data.
inline bool plausible_address(uintptr_t a) noexcept { return a >= 0x10000 && a < 0x0000800000000000ull; }

// Run f() under the guard. False when the site is switched off (f never runs) or when
// f faulted.
template <class F>
bool guarded(Site& s, F&& f) {
    if (!enabled(s)) return false;
    __try {
        f();
        return true;
    } __except (filter(s, GetExceptionInformation())) {
        on_fault(s);
        return false;
    }
}

// The same for a body that computes a value: f()'s result, or `fallback` when the site
// is off or f faulted. R must need no destructor.
template <class R, class F>
R guarded_or(Site& s, R fallback, F&& f) {
    static_assert(std::is_trivially_destructible<R>::value, "guarded_or: R must need no unwinding");
    if (!enabled(s)) return fallback;
    R r = fallback;
    __try {
        r = f();
    } __except (filter(s, GetExceptionInformation())) {
        on_fault(s);
        return fallback;
    }
    return r;
}

// Once a frame from the main-thread hook: the 60 s FAULT-SUM line while any count is
// non-zero.
void tick(double nowS) noexcept;

// mp_fault_switchoff (default on). Off: Call sites keep running after 8 faults (the
// logging is unchanged). Turning it on again does not re-enable a site already off.
void set_switch_off(bool on) noexcept;
bool switch_off() noexcept;

// Totals for the status line: sites with a fault, faults, sites switched off.
struct Totals { uint32_t sites = 0, faults = 0, off = 0; };
Totals totals() noexcept;

// The log sink (default: kcdmp::logf). The native tests capture the lines.
using Sink = void (*)(const char* line);
void set_sink(Sink s) noexcept;

// Tests only: forget every registered site's counts and switches, and the sum clock.
void reset_for_tests() noexcept;

// Formats an address as "Module.dll+0x1234" (or the raw address outside any module).
// Pure apart from the module lookup; the tests pin the text.
void describe(uintptr_t addr, char* out, size_t cap) noexcept;

} // namespace kcdmp::fault

// A named site, declared where the guard is (a function-local static: constant-
// initialized, no init guard).
#define KCDMP_FAULT_CALL(var, nm) static ::kcdmp::fault::Site var{nm, ::kcdmp::fault::Kind::Call}
#define KCDMP_FAULT_READ(var, nm) static ::kcdmp::fault::Site var{nm, ::kcdmp::fault::Kind::Read}

// The same as an expression, for a site handed straight to a helper that takes one
// (read_object_property(KCDMP_SITE_CALL("rttr::walk/RPGModule"), ...)): each expansion
// is its own lambda with its own static site.
#define KCDMP_SITE_CALL(nm) \
    ([]() noexcept -> ::kcdmp::fault::Site& { static ::kcdmp::fault::Site s_{nm, ::kcdmp::fault::Kind::Call}; return s_; }())
#define KCDMP_SITE_READ(nm) \
    ([]() noexcept -> ::kcdmp::fault::Site& { static ::kcdmp::fault::Site s_{nm, ::kcdmp::fault::Kind::Read}; return s_; }())
