// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-151 Phase 3.7: the host's live weather (weather.h).
#include "weather.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>

#include "anchors.h"
#include "fault_guard.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"

namespace kcdmp::weather {
namespace {

constexpr const char* kBlendString = "wh::environmentmodule::C_TimeOfDayBlender::BlendToProfile";
constexpr auto& kPrologue = hookpro::kBlendToProfile;
constexpr size_t kNameMax = 48;

std::atomic<bool> g_armed{false};
const char* g_why = "not installed";

std::mutex g_mu;                 // the name buffers (any thread, a few bytes at a time)
char g_last[kNameMax] = {};
char g_gate[kNameMax] = {};
std::atomic<bool> g_gateOn{false};
std::atomic<uint32_t> g_count{0}, c_declined{0}, c_ran{0}, c_unreadable{0};

// The profile name, a game string: copied with a guarded read, printable names only.
bool copy_name(const void* p, char* out) {
    if (!p) return false;
    KCDMP_FAULT_READ(site, "weather::copy_name");
    size_t n = 0;
    const bool ok = fault::guarded(site, [&] {
        const char* s = static_cast<const char*>(p);
        for (; n + 1 < kNameMax && s[n]; ++n) out[n] = s[n];
    });
    out[n] = 0;
    if (!ok || n == 0) return false;
    for (size_t i = 0; i < n; ++i) {
        const char c = out[i];
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')) return false;
    }
    return true;
}

// BlendToProfile(this, profile, blend, force): true = declined (the original does not run, the caller sees false).
bool __fastcall blend_gate(void* /*self*/, void* profile, void* /*blend*/, void* /*force*/) {
    char name[kNameMax];
    if (!copy_name(profile, name)) { c_unreadable.fetch_add(1, std::memory_order_relaxed); return false; }
    std::lock_guard<std::mutex> lock(g_mu);
    if (g_gateOn.load(std::memory_order_relaxed) && std::strcmp(name, g_gate) != 0) {
        c_declined.fetch_add(1, std::memory_order_relaxed);
        return true;
    }
    if (std::strcmp(name, g_last) != 0 || g_count.load(std::memory_order_relaxed) == 0) {
        std::memcpy(g_last, name, kNameMax);
        g_count.fetch_add(1, std::memory_order_release);
    }
    c_ran.fetch_add(1, std::memory_order_relaxed);
    return false;
}

} // namespace

void install() {
    HMODULE em = GetModuleHandleA("EnvironmentModule.dll");
    if (!em) { g_why = "EnvironmentModule.dll not loaded"; logf("WO151-WEATHER NOT armed -- %s", g_why); return; }
    int count = 0;
    const uint8_t* fn = anchor::function_by_string(em, kBlendString, &count);
    if (!fn || count != 1) {
        g_why = "the BlendToProfile __FUNCTION__ string has no single referencing function";
        logf("WO151-WEATHER NOT armed -- %s (%d)", g_why, count);
        return;
    }
    const char* why = nullptr;
    if (!inlinehook::install_gate4(const_cast<uint8_t*>(fn), kPrologue, sizeof kPrologue, &blend_gate, &why)) {
        g_why = why ? why : "install failed";
        logf("WO151-WEATHER NOT armed -- %s", g_why);
        return;
    }
    g_armed = true; g_why = "armed";
    char where[96];
    anchor::describe(fn, where, sizeof where);
    logf("WO151-WEATHER armed at %s: every blend to a time-of-day profile is recorded (the host's live weather); a joiner's gate lets only the host's through", where);
}

bool armed() { return g_armed.load(); }

uint32_t last_blend(char* out, size_t cap) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (cap) { std::strncpy(out, g_last, cap - 1); out[cap - 1] = 0; }
    return g_count.load(std::memory_order_acquire);
}

void set_gate(const char* profile) {
    char name[kNameMax] = {};
    const bool on = profile && *profile;
    if (on) { std::strncpy(name, profile, kNameMax - 1); }
    {
        std::lock_guard<std::mutex> lock(g_mu);
        std::memcpy(g_gate, name, kNameMax);
        g_gateOn = on;
    }
    logf("WO151-WEATHER gate %s%s%s", on ? "on: only '" : "off", on ? name : "", on ? "' may blend here (the host's weather)" : " -- every blend runs");
}

void on_pipe_closed() {
    if (g_gateOn.load()) set_gate(nullptr);
}

int status_text(char* out, int n) {
    char last[kNameMax], gate[kNameMax];
    bool gateOn;
    {
        std::lock_guard<std::mutex> lock(g_mu);
        std::memcpy(last, g_last, kNameMax); std::memcpy(gate, g_gate, kNameMax);
        gateOn = g_gateOn.load();
    }
    return std::snprintf(out, n, "weather=%s last=%s blends=%u ran=%u declined=%u unreadable=%u gate=%s",
                         g_armed ? "armed" : "off", last[0] ? last : "-", g_count.load(), c_ran.load(), c_declined.load(),
                         c_unreadable.load(), gateOn ? gate : "off");
}

} // namespace kcdmp::weather
