// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-139 -- see wo139.h.
#include "wo139.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <unordered_map>

#include "anchors.h"
#include "engine.h"
#include "hits.h"
#include "main_thread.h"
#include "script_context.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "npc_drive.h"
#include "wo137.h"
#include "wo139_rules.h"

namespace kcdmp::wo139 {
namespace {

// C_UIHudStates::SetTrespassState(this, int level): mov [rsp+8],rbx; mov [rsp+10h],edx; push rdi; sub rsp,20h; mov rdi,rcx
constexpr uint8_t kImplPrologue[17] = {
    0x48, 0x89, 0x5C, 0x24, 0x08, 0x89, 0x54, 0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xF9,
};
// The listener (this, uint8 level): mov [rsp+8],rbx; push rdi; sub rsp,20h; mov rdi,rcx; movzx ebx,dl
// (16 bytes, instruction-aligned, no RIP-relative operand: the next instruction is the first lea [rip+]).
constexpr auto& kListenerPrologue = hookpro::kTrespassListener;   // hook_prologues.h (WO-148)
constexpr uint32_t kPlayerEid = 0x7777;

std::atomic<bool> g_armed{false};
std::atomic<bool> g_on{false};
std::atomic<uint32_t> g_level{0xFF};       // the last level the engine told the HUD (0xFF = none yet)
std::atomic<uint32_t> g_told{0};           // listener calls
uint32_t g_sentLevel = 0xFF;               // main thread: the last level sent
std::atomic<uint32_t> c_sent{0}, c_edges{0};
const char* g_why = "not installed";
char g_where[96] = "?";
FrameFn g_frame = nullptr;

bool rd_bytes(const uint8_t* p, uint8_t* out, size_t n) {
    __try { std::memcpy(out, p, n); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

// Every distinct function that loads `target` with a RIP-relative lea (48/4C 8D /r, mod 00 rm 101).
int functions_loading(HMODULE mod, const void* target, const uint8_t** out, int max) {
    anchor::Range text{};
    if (!anchor::section(mod, ".text", &text) || text.size() < 8) return 0;
    int n = 0;
    __try {
        for (const uint8_t* p = text.begin; p + 7 <= text.end && n < max; ++p) {
            if ((p[0] != 0x48 && p[0] != 0x4C) || p[1] != 0x8D || (p[2] & 0xC7) != 0x05) continue;
            int32_t disp = 0; std::memcpy(&disp, p + 3, 4);
            if (p + 7 + disp != static_cast<const uint8_t*>(target)) continue;
            anchor::Range fr{};
            if (!anchor::function_range(mod, p, &fr)) continue;
            bool dup = false;
            for (int i = 0; i < n; ++i) if (out[i] == fr.begin) dup = true;
            if (!dup) out[n++] = fr.begin;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) { return 0; }
    return n;
}

// Every distinct function that tail-jumps (E9 rel32) to `target`.
int functions_jumping_to(HMODULE mod, const void* target, const uint8_t** out, int max) {
    anchor::Range text{};
    if (!anchor::section(mod, ".text", &text) || text.size() < 8) return 0;
    int n = 0;
    __try {
        for (const uint8_t* p = text.begin; p + 5 <= text.end && n < max; ++p) {
            if (p[0] != 0xE9) continue;
            int32_t rel = 0; std::memcpy(&rel, p + 1, 4);
            if (p + 5 + rel != static_cast<const uint8_t*>(target)) continue;
            anchor::Range fr{};
            if (!anchor::function_range(mod, p, &fr)) continue;
            bool dup = false;
            for (int i = 0; i < n; ++i) if (out[i] == fr.begin) dup = true;
            if (!dup) out[n++] = fr.begin;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) { return 0; }
    return n;
}

// rcx = the C_UIHudStates, dl = the new level. Never refuses: the HUD draws as always.
bool __fastcall listener_gate(void* /*self*/, void* a2) {
    g_level.store(static_cast<uint32_t>(reinterpret_cast<uintptr_t>(a2) & 0xFF), std::memory_order_relaxed);
    g_told.fetch_add(1, std::memory_order_relaxed);
    return false;
}

bool player_pos(float p[3]) {
    void* e = engine::entity_by_id(kPlayerEid);
    return e && npcdrive::entity_pos(e, p);
}

// Pursuits this module set: guard eid -> the avatar and both souls (main thread only).
struct Pursuit { uint32_t avatar; void* guardSoul; void* avatarSoul; bool forced; };
std::unordered_map<uint32_t, Pursuit> g_pursuits;
std::atomic<uint32_t> c_pursueOn{0}, c_pursueOff{0}, c_pursueFail{0}, c_context{0};
constexpr const char* kForcedTarget = "combat_forcedTarget";

// 1 set, 0 already so, 2 no such guard / avatar, 3 refused.
uint8_t pursue(bool on, uint32_t avatarEid, const char* guard) {
    const uint32_t geid = hits::eid_of_name(guard);
    void* gs = geid && engine::entity_by_id(geid) ? hits::soul_of_eid(geid) : nullptr;
    auto it = g_pursuits.find(geid);
    if (!on) {
        if (it == g_pursuits.end()) return 0;
        const Pursuit p = it->second;
        g_pursuits.erase(it);
        void* as = engine::entity_by_id(p.avatar) ? hits::soul_of_eid(p.avatar) : nullptr;
        int r = 0;
        if (p.forced && gs && as && gs == p.guardSoul && as == p.avatarSoul) r = sctx::set_soul_relation(gs, as, kForcedTarget, false);
        uint64_t rv = 0;
        const bool left = gs && gs == p.guardSoul && hits::skirmish_ready() && hits::skirmish_remove(gs, &rv);
        c_pursueOff.fetch_add(1);
        logf("WO139-PURSUE off guard=%s eid=0x%X avatar=0x%X forced_target=%s skirmish=%s", guard, geid, p.avatar,
             !p.forced ? "not ours" : r > 0 ? "cleared (read back)" : r == 0 ? "already clear" : "clear FAILED", left ? "left" : "kept");
        return 1;
    }
    void* as = avatarEid && engine::entity_by_id(avatarEid) ? hits::soul_of_eid(avatarEid) : nullptr;
    if (!gs || !as) { c_pursueFail.fetch_add(1); logf("WO139-PURSUE on guard=%s avatar=0x%X -- %s", guard, avatarEid, !gs ? "no such guard here" : "no such avatar"); return 2; }
    if (it != g_pursuits.end() && it->second.avatar == avatarEid) return 0;
    uint64_t rv = 0;
    const bool added = hits::skirmish_ready() && hits::skirmish_add(gs, as, 1, &rv);
    const int fr = sctx::set_soul_relation(gs, as, kForcedTarget, true);
    if (!added && fr < 0) { c_pursueFail.fetch_add(1); logf("WO139-PURSUE on guard=%s -> avatar 0x%X REFUSED (skirmish add and forced target both failed)", guard, avatarEid); return 3; }
    g_pursuits[geid] = {avatarEid, gs, as, fr == 1};
    c_pursueOn.fetch_add(1);
    logf("WO139-PURSUE on guard=%s eid=0x%X -> avatar 0x%X skirmish=%s forced_target=%s -- the joiner's crime, the host's guard",
         guard, geid, avatarEid, added ? "added" : "NOT added", fr == 1 ? "set (read back)" : fr == 0 ? "already set by the game (left to it)" : "NOT set");
    return 1;
}

void clear_all_pursuits(const char* why) {
    for (auto it = g_pursuits.begin(); it != g_pursuits.end(); it = g_pursuits.begin()) {
        const uint32_t geid = it->first; const Pursuit p = it->second;
        g_pursuits.erase(it);
        void* gs = engine::entity_by_id(geid) ? hits::soul_of_eid(geid) : nullptr;
        void* as = engine::entity_by_id(p.avatar) ? hits::soul_of_eid(p.avatar) : nullptr;
        if (p.forced && gs && as && gs == p.guardSoul && as == p.avatarSoul) sctx::set_soul_relation(gs, as, kForcedTarget, false);
        logf("WO139-PURSUE off eid=0x%X avatar=0x%X -- %s", geid, p.avatar, why);
    }
}

} // namespace

void install() {
    HMODULE gm = GetModuleHandleA("GUIModule.dll");
    if (!gm) { g_why = "GUIModule.dll not loaded"; logf("WO139-BUILD trespass detector NOT armed -- %s", g_why); return; }
    const char* s = anchor::find_cstring(gm, "SetTrespassState");
    if (!s) { g_why = "no \"SetTrespassState\" string"; logf("WO139-BUILD trespass detector NOT armed -- %s", g_why); return; }
    const uint8_t* loaders[8]{};
    const int nl = functions_loading(gm, s, loaders, 8);
    const uint8_t* impl = nullptr;
    int nImpl = 0;
    for (int i = 0; i < nl; ++i) {
        uint8_t b[sizeof kImplPrologue]{};
        if (rd_bytes(loaders[i], b, sizeof b) && std::memcmp(b, kImplPrologue, sizeof b) == 0) { impl = loaders[i]; ++nImpl; }
    }
    if (!impl || nImpl != 1) {
        g_why = "SetTrespassState's implementation not found (prologue)";
        logf("WO139-BUILD trespass detector NOT armed -- %s (%d functions load the string, %d match)", g_why, nl, nImpl);
        return;
    }
    const uint8_t* jumpers[8]{};
    const int nj = functions_jumping_to(gm, impl, jumpers, 8);
    const uint8_t* listener = nullptr;
    int nListener = 0;
    for (int i = 0; i < nj; ++i) {
        uint8_t b[sizeof kListenerPrologue]{};
        if (rd_bytes(jumpers[i], b, sizeof b) && std::memcmp(b, kListenerPrologue, sizeof b) == 0) { listener = jumpers[i]; ++nListener; }
    }
    if (!listener || nListener != 1) {
        g_why = "the trespass listener not found (tail-jump + prologue)";
        logf("WO139-BUILD trespass detector NOT armed -- %s (%d functions jump to SetTrespassState, %d match)", g_why, nj, nListener);
        return;
    }
    const char* why = nullptr;
    if (!inlinehook::install_gate(const_cast<uint8_t*>(listener), kListenerPrologue, sizeof kListenerPrologue, &listener_gate, &why)) {
        g_why = why ? why : "install failed";
        logf("WO139-BUILD trespass detector NOT armed -- %s", g_why);
        return;
    }
    anchor::describe(listener, g_where, sizeof g_where);
    char implAt[96]; anchor::describe(impl, implAt, sizeof implAt);
    g_armed = true; g_why = "";
    logf("WO139-BUILD trespass detector ARMED at %s (C_UIHudStates::SetTrespassState %s): pass-through, off until the agent's Config", g_where, implAt);
}

bool armed() { return g_armed.load(); }

void set_frame_callback(FrameFn fn) { g_frame = fn; }

void on_pipe_closed() {
    g_on = false;
    main_thread::post([] { g_sentLevel = 0xFF; clear_all_pursuits("the agent went away"); });
}

void tick() {
    if (!g_on.load(std::memory_order_relaxed)) return;
    const uint32_t lv = g_level.load(std::memory_order_relaxed);
    if (lv == 0xFF || lv == g_sentLevel) return;
    const uint32_t prev = g_sentLevel;
    g_sentLevel = lv;
    float p[3]{};
    const bool havePos = player_pos(p);
    uint8_t body[1 + 1 + 1 + 12]{};
    body[0] = kCrimeTrespass;
    body[1] = static_cast<uint8_t>(lv);
    body[2] = static_cast<uint8_t>(prev);
    std::memcpy(body + 3, p, 12);
    c_edges.fetch_add(1);
    logf("WO139-TRESPASS level %u -> %u (%s) at (%.1f, %.1f, %.1f)%s", prev, lv, wo139rules::trespass_level_name(lv),
         p[0], p[1], p[2], havePos ? "" : " -- the player's position was unreadable");
    if (g_frame) { g_frame(body, sizeof body); c_sent.fetch_add(1); }
}

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (!len) return kRBadRequest;
    switch (body[0]) {
        case kOpConfig: {
            if (len != 2 || cap < 2) return kRBadRequest;
            const bool on = body[1] != 0;
            if (on && !g_armed.load()) { out[0] = 0; out[1] = 0xFF; *outLen = 2; return kRNotArmed; }
            const bool was = g_on.exchange(on);
            if (on && !was) g_sentLevel = 0xFF;   // the current level goes out once (the joiner may already stand in one)
            if (on != was) logf("WO139-CONFIG trespass detector %s", on ? "ON (joiner of a shared world)" : "off");
            out[0] = g_armed.load() ? 1 : 0;
            out[1] = static_cast<uint8_t>(g_level.load() & 0xFF);
            *outLen = 2;
            return kROk;
        }
        case kOpPursue: {
            if (len < 7 || cap < 1) return kRBadRequest;
            const bool on = body[1] != 0;
            uint32_t avatar = 0; std::memcpy(&avatar, body + 2, 4);
            const size_t nl = body[6];
            if (nl < 1 || nl > 63 || len != 7 + nl) return kRBadRequest;
            char guard[64]{};
            std::memcpy(guard, body + 7, nl);
            if (!wo139rules::is_engine_name(guard, nl)) return kRBadRequest;
            out[0] = pursue(on, avatar, guard);
            *outLen = 1;
            return kROk;
        }
        case kOpContext: {
            if (len < 5 || cap < 1) return kRBadRequest;
            const bool on = body[1] != 0;
            const size_t cl = body[2];
            if (cl < 1 || cl > 63 || len < 3 + cl + 1) return kRBadRequest;
            char ctx[64]{};
            std::memcpy(ctx, body + 3, cl);
            const size_t nl = body[3 + cl];
            if (nl < 1 || nl > 63 || len != 3 + cl + 1 + nl) return kRBadRequest;
            char name[64]{};
            std::memcpy(name, body + 4 + cl, nl);
            if (!wo139rules::is_engine_name(name, nl)) return kRBadRequest;
            *outLen = 1;
            if (!wo139rules::context_allowed(ctx)) { out[0] = 4; logf("WO139-CONTEXT %s on %s REFUSED -- not an allowed context", ctx, name); return kROk; }
            const uint32_t eid = hits::eid_of_name(name);
            void* soul = eid && engine::entity_by_id(eid) ? hits::soul_of_eid(eid) : nullptr;
            if (!soul) { out[0] = 2; logf("WO139-CONTEXT %s %s on %s -- no such entity here", ctx, on ? "set" : "clear", name); return kROk; }
            const int r = sctx::set_soul_context(soul, ctx, on);
            out[0] = r == 1 ? 1 : r == 0 ? 0 : 3;
            c_context.fetch_add(1);
            logf("WO139-CONTEXT %s %s on %s -> %s", ctx, on ? "set" : "clear", name, r == 1 ? "written (read back)" : r == 0 ? "already so" : "REFUSED (see SCTX)");
            return kROk;
        }
        case kOpPunishGate: {
            if (len != 2 || cap < 1) return kRBadRequest;
            const bool ok = wo137::set_punish_gate(body[1] != 0);
            out[0] = wo137::punish_gate_armed() ? 1 : 0;
            *outLen = 1;
            return ok ? kROk : kRNotArmed;
        }
        case kOpStatus: {
            char line[300];
            const int n = std::snprintf(line, sizeof line,
                "WO139-NATIVE trespass armed=%d on=%d at=%s level=%u told=%u edges=%u sent=%u pursuits=%zu on=%u off=%u fail=%u contexts=%u punish_gate_armed=%d punish_skipped=%u%s%s",
                g_armed.load() ? 1 : 0, g_on.load() ? 1 : 0, g_where, g_level.load(), g_told.load(), c_edges.load(), c_sent.load(),
                g_pursuits.size(), c_pursueOn.load(), c_pursueOff.load(), c_pursueFail.load(), c_context.load(),
                wo137::punish_gate_armed() ? 1 : 0, wo137::punish_skipped(),
                g_armed.load() ? "" : " why=", g_armed.load() ? "" : g_why);
            if (n <= 0) return kRFailed;
            const size_t m = static_cast<size_t>(n) < cap ? static_cast<size_t>(n) : cap;
            std::memcpy(out, line, m);
            *outLen = m;
            return kROk;
        }
    }
    return kRBadRequest;
}

} // namespace kcdmp::wo139
