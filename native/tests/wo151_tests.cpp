// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-151 Phase 0: engine-free checks of the fault guard (native/KCDMP/fault_guard.h), the reflection
// argument that owns its value (rttr_abi.h Arg<T>) and the frame-rate line (frame_meter.h).
// Linked into KCDMP_NativeTests; wo151_tests() returns the number of failures.
#include <windows.h>

#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

#include "fault_guard.h"
#include "frame_meter.h"
#include "rttr_abi.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define FCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

std::vector<std::string> g_lines;
void capture(const char* line) { g_lines.push_back(line); }
int lines_with(const char* needle) {
    int n = 0;
    for (auto& l : g_lines) if (l.find(needle) != std::string::npos) ++n;
    return n;
}

volatile uintptr_t g_bad = 0x10;
int g_bodyRuns = 0;

bool fault_read(fault::Site& s) {
    return fault::guarded(s, [&] { ++g_bodyRuns; volatile uint32_t v = *reinterpret_cast<const volatile uint32_t*>(g_bad); (void)v; });
}
bool fault_call(fault::Site& s) {
    return fault::guarded(s, [&] { ++g_bodyRuns; reinterpret_cast<void (*)()>(g_bad)(); });
}
} // namespace

int wo151_tests(int* passed) {
    fault::reset_for_tests();
    fault::set_sink(&capture);

    // ---- a read site: counted and logged at the 1st, 10th and 100th, never switched off ----
    {
        static fault::Site s{"test/read", fault::Kind::Read};
        g_lines.clear();
        bool any = false;
        for (int i = 0; i < 120; ++i) any |= fault_read(s);
        FCHECK(!any && s.faults.load() == 120, "120 deliberate read faults all caught and counted (%u)", s.faults.load());
        FCHECK(lines_with("FAULT test/read: 0xC0000005 reading 0x10 at ") == 3, "the read site logs exactly three lines (got %d)", lines_with("FAULT test/read:"));
        FCHECK(lines_with("(1st)") == 1 && lines_with("(10th)") == 1 && lines_with("(100th)") == 1, "at the 1st, the 10th and the 100th");
        FCHECK(lines_with("[read site: counted, never switched off]") == 3, "the read site says it is never switched off");
        FCHECK(lines_with("KCDMP_NativeTests.exe+0x") == 3, "the faulting instruction is named as module+offset");
        FCHECK(fault::enabled(s) && lines_with("switched off after") == 0, "a read site is never switched off");
    }

    // ---- a call site: switched off at the 8th, then never runs its body ----
    {
        static fault::Site s{"test/call", fault::Kind::Call};
        g_lines.clear();
        g_bodyRuns = 0;
        for (int i = 0; i < 7; ++i) fault_call(s);
        FCHECK(fault::enabled(s) && s.faults.load() == 7, "seven faults: still on");
        FCHECK(lines_with("FAULT test/call: 0xC0000005 executing 0x10 at 0x10 (no module) (1st)") == 1,
               "a jump to a bad address: the code, the access and the place (%s)", g_lines.empty() ? "-" : g_lines[0].c_str());
        fault_call(s);
        FCHECK(!fault::enabled(s) && s.faults.load() == 8, "the 8th fault switches the site off");
        FCHECK(lines_with("FAULT test/call: switched off after 8 faults this session") == 1, "and says so");
        const int runs = g_bodyRuns;
        const bool r = fault_call(s);
        FCHECK(!r && g_bodyRuns == runs && s.faults.load() == 8, "switched off: returns false without running the body");
    }

    // ---- mp_fault_switchoff off: a call site keeps running ----
    {
        static fault::Site s{"test/call-keep", fault::Kind::Call};
        fault::set_switch_off(false);
        for (int i = 0; i < 12; ++i) fault_call(s);
        FCHECK(fault::enabled(s) && s.faults.load() == 12, "with mp_fault_switchoff off a call site keeps running (%u)", s.faults.load());
        FCHECK(lines_with("FAULT-CONFIG mp_fault_switchoff=off") == 1, "the switch logs its change");
        fault::set_switch_off(true);
    }

    // ---- guarded_or: the value, or the fallback ----
    {
        static fault::Site s{"test/or", fault::Kind::Read};
        const int ok = fault::guarded_or(s, -1, [&]() -> int { return 42; });
        const int bad = fault::guarded_or(s, -1, [&]() -> int { return static_cast<int>(*reinterpret_cast<const volatile uint32_t*>(g_bad)); });
        FCHECK(ok == 42 && bad == -1, "guarded_or: %d / %d", ok, bad);
    }

    // ---- the 60 s sum ----
    {
        g_lines.clear();
        fault::tick(1000.0);    // starts the clock
        fault::tick(1030.0);
        FCHECK(lines_with("FAULT-SUM") == 0, "no sum before 60 s");
        fault::tick(1061.0);
        FCHECK(lines_with("FAULT-SUM 60s:") == 1, "one sum line at 60 s");
        FCHECK(lines_with("test/read=120") == 1 && lines_with("test/call=8(+)(off)") == 1, "the sum names every site, new faults and the switched-off ones");
        const fault::Totals t = fault::totals();
        FCHECK(t.sites == 4 && t.faults == 120 + 8 + 12 + 1 && t.off == 1, "totals: %u sites, %u faults, %u off", t.sites, t.faults, t.off);
    }

    // ---- a new session (the tests' reset): everything forgotten ----
    {
        fault::reset_for_tests();
        g_lines.clear();
        fault::tick(0.0);
        fault::tick(100.0);
        const fault::Totals t = fault::totals();
        FCHECK(t.sites == 0 && t.faults == 0 && t.off == 0 && lines_with("FAULT-SUM") == 0, "after a reset: nothing counted, no sum while every count is zero");
        static fault::Site s{"test/after-reset", fault::Kind::Call};
        g_lines.clear();
        fault_call(s);
        FCHECK(lines_with("(1st)") == 1, "a site's first fault after the reset is logged again");
        fault::reset_for_tests();
    }

    // ---- the site name of a non-faulting body ----
    {
        static fault::Site s{"test/clean", fault::Kind::Call};
        g_lines.clear();
        int x = 0;
        const bool r = fault::guarded(s, [&] { x = 5; });
        FCHECK(r && x == 5 && s.faults.load() == 0 && g_lines.empty(), "a clean body runs, nothing is logged");
    }
    fault::set_sink(nullptr);

    // ---- describe(): module+offset, and an address outside any module ----
    {
        char buf[200];
        fault::describe(reinterpret_cast<uintptr_t>(&wo151_tests), buf, sizeof buf);
        FCHECK(std::strstr(buf, "KCDMP_NativeTests.exe+0x") == buf, "describe: %s", buf);
        fault::describe(0x10, buf, sizeof buf);
        FCHECK(std::strcmp(buf, "0x10 (no module)") == 0, "describe outside a module: %s", buf);
    }

    // ---- Arg<T>: the argument points into its own object (WO-151 Phase 0.2) ----
    {
        rttr::Type t{reinterpret_cast<void*>(0x1234)};
        const rttr::Arg<uint64_t> a(0x72706776ull, t);
        const void* const* w = static_cast<const void* const*>(a.get());
        FCHECK(a.points_into_self(), "Arg<uint64_t>: fields 0 and 8 point at its own value");
        FCHECK(*static_cast<const uint64_t*>(w[0]) == 0x72706776ull && w[2] == t.data, "the value is read back through the argument, the type is field 16");
        const char* lo = reinterpret_cast<const char*>(&a);
        const char* hi = lo + sizeof(a);
        FCHECK(static_cast<const char*>(w[0]) >= lo && static_cast<const char*>(w[0]) < hi, "the value lives inside the argument object");
        const rttr::Arg<float> f(40.0f, t);
        FCHECK(f.points_into_self() && *static_cast<const float*>(static_cast<const void* const*>(f.get())[0]) == 40.0f, "Arg<float>");
        void* soul = reinterpret_cast<void*>(0xABCDEF0);
        const rttr::Arg<void*> p(soul, t);
        FCHECK(p.points_into_self() && *static_cast<void* const*>(static_cast<const void* const*>(p.get())[0]) == soul, "Arg<void*>: points TO the pointer");
        const rttr::StringArg sa("enemies", t);
        const void* const* sw = static_cast<const void* const*>(sa.get());
        FCHECK(sa.points_into_self() && *static_cast<const std::string*>(sw[0]) == "enemies", "StringArg owns its string");
        static_assert(!std::is_copy_constructible<rttr::Arg<float>>::value && !std::is_move_constructible<rttr::Arg<float>>::value,
                      "Arg<T> can be neither copied nor moved");
        static_assert(!std::is_copy_constructible<rttr::StringArg>::value && !std::is_move_constructible<rttr::StringArg>::value,
                      "StringArg can be neither copied nor moved");
        ++g_pass;
    }

    // ---- the frame-rate line (Phase 0.4) ----
    {
        framemeter::Window w{};
        double now = 5e6;
        bool due = framemeter::add(w, now, 100);
        FCHECK(!due && w.frames == 0, "the first frame starts the window");
        // 59.984 s at 16 ms, then one 80 ms hitch: the hitch is the frame that reaches 60 s
        int lines = 0;
        for (int i = 0; i < 3749; ++i) { now += 16000; due = framemeter::add(w, now, 500); if (due) ++lines; }
        FCHECK(!due && lines == 0, "no line before 60 s (%.3f s)", (now - 5e6) / 1e6);
        now += 80000; due = framemeter::add(w, now, 3000);
        FCHECK(due, "due at %.3f s", (now - 5e6) / 1e6);
        FCHECK(w.frames == 3750 && w.slow == 1, "frames %u, slow %u", w.frames, w.slow);
        FCHECK(w.worstFrameUs == 80000 && w.worstOursUs == 3000, "worst frame 80 ms, worst own work 3 ms");
        const double fps = framemeter::fps(w);
        FCHECK(fps > 62.42 && fps < 62.45, "fps %.3f (3750 frames in 60.064 s)", fps);
        char buf[300];
        framemeter::format(w, buf, sizeof buf);
        FCHECK(std::strstr(buf, "FRAME window_s=60.1 frames=3750 fps=62.4 frame_ms_mean=16.0 frame_ms_worst=80.0 slow_frames=1 ours_us_mean=501 ours_us_max=3000") == buf,
               "the line: %s", buf);
        framemeter::restart(w);
        FCHECK(w.frames == 0 && w.startUs == now && w.lastUs == now, "the next window starts at the closing frame");
        now += 20000; framemeter::add(w, now, 0);
        FCHECK(w.frames == 1 && w.sumFrameUs == 20000, "and measures from it");
    }

    *passed = g_pass;
    return g_fail;
}
