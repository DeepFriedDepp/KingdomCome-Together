// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-135 -- see wo135.h.
#include "wo135.h"
#include "fault_guard.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>

#include "anchors.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"

namespace kcdmp::wo135 {
namespace {

constexpr size_t kSoulIdOff = 0x40;           // script_context.h: the soul's WUID, the id dialogue requests carry
constexpr size_t kReqExBegin = 0x58, kReqExEnd = 0x60;
constexpr int kMaxAvatars = 16;
constexpr auto& kPrologue = hookpro::kDialogueGate;   // hook_prologues.h (WO-148): push x5; lea rbp,[rsp-37h]; sub rsp,0C0h

std::atomic<uint64_t> g_ids[kMaxAvatars];     // 0 = free slot
std::atomic<bool> g_armed{false};
std::atomic<uint32_t> c_refused{0}, c_seen{0};
const char* g_why = "not installed";

template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "wo135::rd");
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}

bool blocked(uint64_t id) {
    if (!id) return false;
    for (auto& s : g_ids) if (s.load(std::memory_order_relaxed) == id) return true;
    return false;
}

// rcx = the dialogue manager, rdx = the request. True = refuse (return 0).
bool __fastcall gate(void* /*self*/, void* req) {
    c_seen.fetch_add(1, std::memory_order_relaxed);
    const uint64_t* b = nullptr; const uint64_t* e = nullptr;
    if (!req || !rd(req, kReqExBegin, &b) || !rd(req, kReqExEnd, &e) || !b || e < b || e - b > 32) return false;
    for (const uint64_t* p = b; p < e; ++p) {
        uint64_t id = 0;
        if (!rd(p, 0, &id)) return false;
        if (blocked(id)) {
            const uint32_t n = c_refused.fetch_add(1, std::memory_order_relaxed) + 1;
            if (n <= 20 || n % 200 == 0) logf("WO135-DIALOG refused #%u: an avatar (soul id 0x%llX) was a speaker -- the avatar is never heard", n, static_cast<unsigned long long>(id));
            return true;
        }
    }
    return false;
}

} // namespace

void install() {
    HMODULE dm = GetModuleHandleA("DialogModule.dll");
    if (!dm) { g_why = "DialogModule.dll not loaded"; logf("WO135-DIALOG gate NOT armed -- %s", g_why); return; }
    int count = 0;
    const uint8_t* fn = anchor::function_by_string(dm, "New dialogue '%s' is starting. Params: souls = '%s' forced = %s, forced decision = %s", &count);
    if (!fn || count != 1) { g_why = "the 'New dialogue' string has no single referencing function"; logf("WO135-DIALOG gate NOT armed -- %s (%d)", g_why, count); return; }
    const char* why = nullptr;
    if (!inlinehook::install_gate(const_cast<uint8_t*>(fn), kPrologue, sizeof kPrologue, &gate, &why)) {
        g_why = why ? why : "install failed";
        logf("WO135-DIALOG gate NOT armed -- %s", g_why);
        return;
    }
    g_armed = true; g_why = "";
    char where[96]; anchor::describe(fn, where, sizeof where);
    logf("WO135-DIALOG gate armed at %s: a dialogue with an avatar among its speakers is refused", where);
}

bool armed() { return g_armed.load(); }

void set_speaker_blocked(void* soul, bool on) {
    uint64_t id = 0;
    if (!soul || !rd(soul, kSoulIdOff, &id) || !id) return;
    if (on) {
        if (blocked(id)) return;
        for (auto& s : g_ids) { uint64_t z = 0; if (s.compare_exchange_strong(z, id)) break; }
    } else {
        for (auto& s : g_ids) { uint64_t v = id; s.compare_exchange_strong(v, 0); }
    }
    logf("WO135-DIALOG avatar soul id 0x%llX %s (gate %s)", static_cast<unsigned long long>(id), on ? "silenced" : "released",
         g_armed ? "armed" : g_why);
}

int status_text(char* out, int n) {
    int blockedN = 0;
    for (auto& s : g_ids) if (s.load()) ++blockedN;
    return std::snprintf(out, n, "wo135 dialog_gate=%s blocked=%d refused=%u seen=%u", g_armed ? "armed" : "off", blockedN, c_refused.load(), c_seen.load());
}

} // namespace kcdmp::wo135
