// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-201 Act V F2 dialogue_vote: the talker's decision, read where the game's own dialogue HUD gets it. The HUD's interface
// (C_UIHudInteractiveDialog, GUIModule) collects one row per offered option and flushes them to Flash in SetDialogSequences (the one
// function that references "ClearDialogSequences"); the rows are a vector the function itself reads ([this+B] .. [this+E], 40 bytes
// each: +0 the shown text, +8 used). The detour copies the rows' texts (presentation only -- the choice stays the game's own, by its
// own index) into a snapshot with a sequence number and calls the original; no rows = the decision closed. The agent polls (op 85).
// Every field is read out of SetDialogSequences' own code ("mov rdi,[rsi+E]; ...; sub rdi,[rsi+B]"); its prologue is checked against
// the hook-prologue registry before the detour goes in (lazily: the first poll with the switch on).   WO201-VOTE.
#include "wo201.h"
#include "wo163.h"
#include "anchors.h"
#include "fault_guard.h"
#include "log.h"
#include "engine.h"
#include <cmath>
#include "inline_hook.h"
#include "hook_prologues.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

namespace kcdmp::wo201vote {

namespace {

constexpr char kClearText[] = "ClearDialogSequences";
constexpr size_t kRowSize = 0x28;
constexpr int kMaxRows = 8;

const char* g_why = "not resolved";
void* g_fn = nullptr;
int g_offB = -1, g_offE = -1;
using SetFn = void (*)(void* self, uint64_t isOnRight);
SetFn g_orig = nullptr;
bool g_hooked = false;
std::atomic<bool> g_on{false};
std::atomic<uint32_t> c_flushes{0};

std::mutex g_lock;
uint32_t g_seq = 0;                        // under g_lock
std::vector<std::pair<bool, std::string>> g_rows;   // under g_lock: (used, text)

template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    if (!base) return false;
    KCDMP_FAULT_READ(site, "wo201vote::read");
    if (!fault::enabled(site)) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}
bool rd_bytes(const void* p, uint8_t* out, size_t n) {
    KCDMP_FAULT_READ(site, "wo201vote::read_bytes");
    if (!p || !fault::enabled(site) || !fault::plausible_address(reinterpret_cast<uintptr_t>(p))) return false;
    return fault::guarded(site, [&] { std::memcpy(out, p, n); });
}
bool rd_str(const char* p, char* out, size_t n) {
    out[0] = 0;
    if (!p) return false;
    KCDMP_FAULT_READ(site, "wo201vote::read_str");
    if (!fault::enabled(site) || !fault::plausible_address(reinterpret_cast<uintptr_t>(p))) return false;
    return fault::guarded(site, [&] {
        size_t i = 0;
        for (; i + 1 < n && p[i]; ++i) out[i] = p[i];
        out[i] = 0;
    });
}

void set_detour(void* self, uint64_t isOnRight) {
    if (g_on.load()) {
        char* b = nullptr; char* e = nullptr;
        std::vector<std::pair<bool, std::string>> rows;
        if (rd(self, g_offB, &b) && rd(self, g_offE, &e) && b && e >= b) {
            const size_t n = static_cast<size_t>(e - b) / kRowSize;
            for (size_t i = 0; i < n && i < static_cast<size_t>(kMaxRows); ++i) {
                const char* tp = nullptr; uint8_t used = 0;
                char t[160];
                if (!rd(b + i * kRowSize, 0, &tp) || !rd_str(tp, t, sizeof t)) break;
                rd(b + i * kRowSize, 8, &used);
                rows.emplace_back(used != 0, t);
            }
        }
        std::lock_guard<std::mutex> lk(g_lock);
        if (rows != g_rows) { g_rows.swap(rows); ++g_seq; }
        c_flushes.fetch_add(1, std::memory_order_relaxed);
    }
    g_orig(self, isOnRight);
}

bool arm(const char** why) {
    if (g_hooked) return true;
    if (!g_fn) { *why = g_why; return false; }
    void* orig = nullptr;
    if (!inlinehook::install_detour(g_fn, hookpro::kSetDialogSequences, sizeof hookpro::kSetDialogSequences, reinterpret_cast<void*>(&set_detour), &orig, why))
        return false;
    g_orig = reinterpret_cast<SetFn>(orig);
    g_hooked = true;
    logf("WO201-VOTE armed: the dialogue HUD's SetDialogSequences detoured (the talker's options are read, never changed)");
    return true;
}

} // namespace

void install() {
    HMODULE gm = GetModuleHandleA("GUIModule.dll");
    if (!gm) { g_why = "GUIModule.dll not loaded"; return; }
    int n = 0;
    const uint8_t* fn = anchor::function_by_string(gm, kClearText, &n);
    if (!fn || n != 1) { g_why = "the clear text is referenced by no (or more than one) function"; return; }
    uint8_t code[0x60]{};
    if (!rd_bytes(fn, code, sizeof code)) { g_why = "SetDialogSequences unreadable"; return; }
    if (std::memcmp(code, hookpro::kSetDialogSequences, sizeof hookpro::kSetDialogSequences) != 0) { g_why = "its prologue is not the registry's"; return; }
    // mov rdi,[rsi+E] (48 8B 7E disp8) ... sub rdi,[rsi+B] (48 2B 7E disp8)
    for (size_t i = 0; i + 4 <= sizeof code; ++i) {
        if (code[i] == 0x48 && code[i + 1] == 0x8B && code[i + 2] == 0x7E && g_offE < 0) g_offE = code[i + 3];
        if (code[i] == 0x48 && code[i + 1] == 0x2B && code[i + 2] == 0x7E && g_offB < 0) g_offB = code[i + 3];
    }
    if (g_offB < 0 || g_offE < 0 || g_offE - g_offB != 8) { g_why = "the rows' vector not found in its code"; g_offB = g_offE = -1; return; }
    g_fn = const_cast<uint8_t*>(fn);
    g_why = "resolved";
    logf("WO201-VOTE resolver: SetDialogSequences, rows [+0x%X, +0x%X) -- %s the build the research read", g_offB, g_offE,
         (g_offB == 0x10 && g_offE == 0x18) ? "the same as" : "DIFFERENT from (the resolved values are used)");
}

// op 85 Poll [on:1][lastSeq:4][figureEid:4] -> [seq:4][dist:f32][n:1] per row [used:1][len:1][text]
//   (n = 0: no decision shown; unchanged seq: n = 255; dist: this player to the partner's figure, -1 unknown)
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len != 10 || cap < 9) return wo163::kRBadRequest;
    const bool on = body[1] != 0;
    uint32_t last = 0, fe = 0; std::memcpy(&last, body + 2, 4); std::memcpy(&fe, body + 6, 4);
    float dist = -1.0f, a[3], b[3];
    void* me = engine::entity_by_id(0x7777);
    void* fig = fe ? engine::entity_by_id(fe) : nullptr;
    if (me && fig && engine::entity_world_pos(me, a) && engine::entity_world_pos(fig, b))
        dist = std::sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
    if (on && !g_hooked) {
        const char* why = "";
        if (!arm(&why)) { logf("WO201-VOTE NOT armed (%s) -- the partner cannot vote", why); return wo163::kRFailed; }
    }
    g_on = on;
    std::lock_guard<std::mutex> lk(g_lock);
    std::memcpy(out, &g_seq, 4); std::memcpy(out + 4, &dist, 4);
    if (g_seq == last) { out[8] = 255; *outLen = 9; return wo163::kROk; }
    size_t o = 9; uint8_t k = 0;
    for (const auto& r : g_rows) {
        const size_t tl = r.second.size() > 150 ? 150 : r.second.size();
        if (o + 2 + tl > cap) break;
        out[o] = r.first ? 1 : 0; out[o + 1] = static_cast<uint8_t>(tl); std::memcpy(out + o + 2, r.second.data(), tl);
        o += 2 + tl; ++k;
    }
    out[8] = k; *outLen = o;
    return wo163::kROk;
}

int status_text(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "vote=%s on=%d flushes=%u seq=%u", g_why, g_on.load() ? 1 : 0, c_flushes.load(), g_seq);
}

} // namespace kcdmp::wo201vote
