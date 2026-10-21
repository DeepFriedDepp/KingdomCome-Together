// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-201 V.2 nearest_player_triggers (the host): a quest's distance trigger on "the player" measures from the nearer of the host's Henry
// and the partner's figure. "Came near" fires when either does; "is far" (the escort's "player out from the tour") only when both are.
//
// The engine's C_DistanceTrigger (XGenAIModule) reads its Origin and Target ports once, when it becomes active (the function that
// traces "no origin or target on input"), keeps the two objects on the node, and evaluates on every move of either object: Evaluate
// reads each one's position through its own virtual (the slot Evaluate's own code calls), takes the distance, and fires OnMatch /
// OnUnmatch only on a change. Every piece is read out of the engine's code here, never by a fixed address:
//   * the activation function: by its trace text; its tail "mov byte [rdi+X],1; ...; jmp Evaluate" gives Evaluate and the node's
//     active byte X;
//   * Evaluate's first two "mov rcx,[rdi+disp32]; mov rax,[rcx]; call [rax+slot]" give the node's origin/target fields and the slot;
//   * Evaluate's prologue is checked against the hook-prologue registry before the detour goes in (lazily: the first time the switch
//     turns on).
// The detour: when the node is bound to the player's object and the figure's object is nearer to the other end, the node's player
// field holds the figure's object for that one evaluation (restored right after; one lock, the engine may evaluate off the main
// thread). The figure's own moves never wake the node, so the main thread re-evaluates every player-bound node it saw in the last 5 s,
// 5 times a second (each checked: the node's class and its active byte, that frame).   WO201-NEAR.
#include "wo201.h"
#include "wo201_rules.h"
#include "wo163.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "log.h"
#include "npcstate.h"
#include "rttr_abi.h"
#include "inline_hook.h"
#include "hook_prologues.h"
#include "main_thread.h"
#include "wo202.h"
#include "wo202_rules.h"
#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <vector>

namespace kcdmp::wo201near {

namespace {

using namespace wo201rules;

constexpr uint32_t kPlayerEntity = 0x7777;
constexpr size_t kMgrNpcOf = 0x20;   // C_NPCManager vtable: the C_NPC of an entity id (WO-189's read)
constexpr char kActivateText[] = "no origin or target on input";

const char* g_why = "not resolved";
void* g_eval = nullptr;
void* const* g_vftDist = nullptr;
void* const* g_vftNpc = nullptr;
void* const* g_vftNpcMgr = nullptr;
int64_t g_offO = -1, g_offT = -1, g_offActive = -1;
size_t g_posSlot = 0;
using EvalFn = void (*)(void* node);
EvalFn g_evalOrig = nullptr;
bool g_hooked = false;
std::atomic<bool> g_on{false};
std::atomic<uint32_t> g_figEid{0};
std::atomic<void*> g_P{nullptr}, g_B{nullptr};
std::atomic<uint32_t> c_evals{0}, c_bound{0}, c_swaps{0}, c_wakes{0}, c_wakeSkipped{0}, c_offMain{0}, c_unsettled{0};
// WO-202 I.2 near_safe: the figure's C_NPC as last seen on the main thread and since when it has been the same object
void* g_figStable = nullptr;
uint64_t g_figStableSince = 0;
bool g_safeSaid = false;
std::recursive_mutex g_lock;
struct Seen { void* node; uint64_t at; };
std::vector<Seen> g_seen;   // under g_lock
uint64_t g_lastWake = 0, g_lastResolve = 0, g_lastLog = 0, g_lastTick = 0, g_settleUntil = 0;
void* g_lastSoul = nullptr;

template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    if (!base) return false;
    KCDMP_FAULT_READ(site, "wo201near::read");
    if (!fault::enabled(site)) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}
bool wr_ptr(void* base, ptrdiff_t off, void* v) {
    KCDMP_FAULT_CALL(site, "wo201near::write");
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!base || !fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(reinterpret_cast<void*>(a), &v, sizeof v); });
}
bool rd_bytes(const void* p, uint8_t* out, size_t n) {
    KCDMP_FAULT_READ(site, "wo201near::read_bytes");
    if (!p || !fault::enabled(site) || !fault::plausible_address(reinterpret_cast<uintptr_t>(p))) return false;
    return fault::guarded(site, [&] { std::memcpy(out, p, n); });
}
bool is_a(const void* obj, void* const* vft) {
    void* vp = nullptr;
    return obj && vft && rd(obj, 0, &vp) && vp == static_cast<const void*>(vft);
}
void* vslot(const void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    return obj && rd(obj, 0, &vt) && vt && rd(vt, static_cast<ptrdiff_t>(off), &fn) ? fn : nullptr;
}

// An object's position through the slot Evaluate itself calls (it returns a pointer to x,y,z).
bool pos_of(void* obj, float out[3]) {
    void* fn = vslot(obj, g_posSlot);
    if (!fn) return false;
    const float* p = nullptr;
    KCDMP_FAULT_CALL(site, "wo201near::position");
    if (!fault::guarded(site, [&] { p = reinterpret_cast<const float* (*)(void*)>(fn)(obj); }) || !p) return false;
    return rd_bytes(p, reinterpret_cast<uint8_t*>(out), 12);
}
float dist(const float a[3], const float b[3]) {
    const float dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
    return std::sqrt(dx * dx + dy * dy + dz * dz);
}

void note_seen(void* node, uint64_t t) {
    for (auto& s : g_seen) if (s.node == node) { s.at = t; return; }
    if (g_seen.size() < 256) g_seen.push_back({node, t});
}

void* npc_of(void* mgr, void* get, uint32_t eid);

// WO-202 I.2 near_safe: the two C_NPCs looked up in this very call on the main thread (never a pointer cached from another frame), the
// figure's only once it has been the same object for kFigureSettleMs.
bool safe_pair(void** P, void** B) {
    void* mgr = wo141::x::npc_manager();
    void* get = is_a(mgr, g_vftNpcMgr) ? vslot(mgr, kMgrNpcOf) : nullptr;
    const uint32_t fe = g_figEid.load();
    if (!get || !fe || !engine::entity_by_id(fe)) return false;
    *P = npc_of(mgr, get, kPlayerEntity);
    *B = npc_of(mgr, get, fe);
    if (!wo202rules::figure_usable(*B, g_figStable, g_figStableSince, GetTickCount64())) { c_unsettled.fetch_add(1, std::memory_order_relaxed); return false; }
    return *P && *P != *B;
}

void eval_detour(void* node) {
    c_evals.fetch_add(1, std::memory_order_relaxed);
    void* P = g_P.load(); void* B = g_B.load();
    if (g_on.load() && wo202::on(wo202rules::kBitNearSafe)) {
        // the engine evaluates distance triggers on its job workers too: a figure (or a node) another thread is changing is never read
        // there -- that evaluation runs exactly as without the mod
        if (GetCurrentThreadId() != main_thread::main_thread_id()) { c_offMain.fetch_add(1, std::memory_order_relaxed); g_evalOrig(node); return; }
        if (!safe_pair(&P, &B)) { g_evalOrig(node); return; }
    }
    if (!g_on.load() || !P || !B || P == B) { g_evalOrig(node); return; }
    std::lock_guard<std::recursive_mutex> lk(g_lock);
    void* o = nullptr; void* t = nullptr;
    if (!rd(node, static_cast<ptrdiff_t>(g_offO), &o) || !rd(node, static_cast<ptrdiff_t>(g_offT), &t) || (o != P && t != P) || o == t) {
        g_evalOrig(node); return;
    }
    c_bound.fetch_add(1, std::memory_order_relaxed);
    note_seen(node, GetTickCount64());
    void* other = o == P ? t : o;
    const int64_t pOff = o == P ? g_offO : g_offT;
    float pp[3], pb[3], po[3];
    if (other == B || !pos_of(P, pp) || !pos_of(B, pb) || !pos_of(other, po) || !figure_nearer(dist(pp, po), dist(pb, po))) {
        g_evalOrig(node); return;
    }
    if (!wr_ptr(node, static_cast<ptrdiff_t>(pOff), B)) { g_evalOrig(node); return; }
    g_evalOrig(node);
    wr_ptr(node, static_cast<ptrdiff_t>(pOff), P);
    c_swaps.fetch_add(1, std::memory_order_relaxed);
    const uint64_t now = GetTickCount64();
    if (now - g_lastLog >= 10000) {
        g_lastLog = now;
        logf("WO201-NEAR a distance trigger on the player measured from the partner's figure (%.1f m vs Henry's %.1f m) [swaps=%u bound=%u] thread=%lu",
             dist(pb, po), dist(pp, po), c_swaps.load(), c_bound.load(), static_cast<unsigned long>(GetCurrentThreadId()));
    }
}

void* npc_of(void* mgr, void* get, uint32_t eid) {
    void* npc = nullptr;
    uint32_t id = eid;
    KCDMP_FAULT_CALL(site, "wo201near::npc_of");
    if (!fault::guarded(site, [&] { npc = reinterpret_cast<void* (*)(void*, const uint32_t*)>(get)(mgr, &id); })) return nullptr;
    return is_a(npc, g_vftNpc) ? npc : nullptr;
}

bool arm(const char** why) {
    if (g_hooked) return true;
    if (!g_eval) { *why = g_why; return false; }
    void* orig = nullptr;
    if (!inlinehook::install_detour(g_eval, hookpro::kDistEvaluate, sizeof hookpro::kDistEvaluate, reinterpret_cast<void*>(&eval_detour), &orig, why))
        return false;
    g_evalOrig = reinterpret_cast<EvalFn>(orig);
    g_hooked = true;
    logf("WO201-NEAR armed: C_DistanceTrigger's Evaluate detoured (origin +0x%llX, target +0x%llX, active byte +0x%llX, position slot +0x%zX)",
         static_cast<long long>(g_offO), static_cast<long long>(g_offT), static_cast<long long>(g_offActive), g_posSlot);
    return true;
}

} // namespace

void install_resolve();

void install() {
    install_resolve();
    if (std::strcmp(g_why, "resolved") != 0) logf("WO201-NEAR resolver: NOT resolved (%s) -- the player's distance triggers stay the host's Henry's", g_why);
}

void install_resolve() {
    HMODULE xg = GetModuleHandleA("XGenAIModule.dll");
    if (!xg) { g_why = "XGenAIModule.dll not loaded"; return; }
    g_vftDist = anchor::find_vftable(xg, ".?AVC_DistanceTrigger@xgenaimodule@wh@@", 0);
    g_vftNpc = anchor::find_vftable(xg, ".?AVC_NPC@xgenaimodule@wh@@", 0);
    g_vftNpcMgr = anchor::find_vftable(xg, ".?AVC_NPCManager@xgenaimodule@wh@@", 0);
    if (!g_vftDist || !g_vftNpc || !g_vftNpcMgr) { g_why = "a class (C_DistanceTrigger, C_NPC, C_NPCManager) not found by RTTI"; return; }
    int n = 0;
    const uint8_t* act = anchor::function_by_string(xg, kActivateText, &n);
    if (!act || n != 1) { g_why = "the activation text is referenced by no (or more than one) function"; return; }
    // The compiler splits this function into contiguous chunks (its unwind data names only the first): its code runs from the start
    // to the first int3 padding run, which ends a function
    std::vector<uint8_t> code(0x600);
    if (!rd_bytes(act, code.data(), code.size())) { g_why = "the activation function unreadable"; return; }
    size_t len = code.size();
    for (size_t i = 0; i + 3 <= code.size(); ++i)
        if (code[i] == 0xCC && code[i + 1] == 0xCC && code[i + 2] == 0xCC) { len = i; break; }
    const TailJump tj = active_tail_jump(code.data(), len);
    if (tj.jmpAt < 0) { g_why = "its tail (mov byte [rdi+X],1 ... jmp Evaluate) not found"; return; }
    int32_t rel = 0; std::memcpy(&rel, code.data() + tj.jmpAt + 1, 4);
    g_eval = const_cast<uint8_t*>(act + tj.jmpAt + 5 + rel);
    g_offActive = tj.activeOff;
    uint8_t ev[0x60]{};
    if (!rd_bytes(g_eval, ev, sizeof ev)) { g_eval = nullptr; g_why = "Evaluate unreadable"; return; }
    const NodeReads nr = evaluate_reads(ev, sizeof ev);
    if (nr.origin < 0 || nr.target < 0 || nr.slot <= 0) { g_eval = nullptr; g_why = "Evaluate's two position reads not found"; return; }
    if (std::memcmp(ev, hookpro::kDistEvaluate, sizeof hookpro::kDistEvaluate) != 0) { g_eval = nullptr; g_why = "Evaluate's prologue is not the registry's"; return; }
    g_offO = nr.origin; g_offT = nr.target; g_posSlot = static_cast<size_t>(nr.slot);
    g_why = "resolved";
    char a[96]; anchor::describe(g_eval, a, sizeof a);
    logf("WO201-NEAR resolver: Evaluate %s (origin +0x%llX target +0x%llX active +0x%llX position slot +0x%zX) -- %s the build the research read",
         a, static_cast<long long>(g_offO), static_cast<long long>(g_offT), static_cast<long long>(g_offActive), g_posSlot,
         (g_offO == kKnownOriginOff && g_offT == kKnownTargetOff && g_offActive == kKnownActiveOff) ? "the same as" : "DIFFERENT from (the resolved values are used)");
}

void tick() {
    if (!g_on.load() || !g_hooked) return;
    const uint64_t now = GetTickCount64();
    // a load frees every quest node: a long frame or a new player soul drops every node remembered for a wake (never one kept
    // across it) and waits 5 s
    const uint64_t gap = now - g_lastTick;
    g_lastTick = now;
    void* ps = rttr::read_player_soul();
    if (gap > 500 || !ps || ps != g_lastSoul) {
        g_lastSoul = ps; g_settleUntil = now + 5000;
        std::lock_guard<std::recursive_mutex> lk(g_lock);
        g_seen.clear(); g_B = nullptr; g_P = nullptr;
        return;
    }
    if (now < g_settleUntil) return;
    const bool safe = wo202::on(wo202rules::kBitNearSafe);
    if (safe || now - g_lastResolve >= 1000) {   // WO-202 near_safe: every frame -- the settle clock sees a new figure NPC at once
        g_lastResolve = now;
        void* mgr = wo141::x::npc_manager();
        void* get = is_a(mgr, g_vftNpcMgr) ? vslot(mgr, kMgrNpcOf) : nullptr;
        const uint32_t fe = g_figEid.load();
        g_P = get ? npc_of(mgr, get, kPlayerEntity) : nullptr;
        g_B = get && fe && engine::entity_by_id(fe) ? npc_of(mgr, get, fe) : nullptr;
        if (g_B.load() != g_figStable) { g_figStable = g_B.load(); g_figStableSince = now; }
    }
    if (safe) {
        // WO-202 I.2: no node pointer is kept from one frame to the next -- the engine frees quest nodes on its own schedule (a module's
        // hibernation removed 3605 nodes of Zelejov's tavern area in each field session that crashed, and the wake re-evaluated nodes
        // remembered up to 5 s). A trigger is substituted only inside the engine's own evaluation of it.
        if (!g_safeSaid) { g_safeSaid = true; logf("WO202-NEAR near_safe on: main thread only, the figure looked up per call and settled %llu ms, no wake of remembered nodes",
                                                   static_cast<unsigned long long>(wo202rules::kFigureSettleMs)); }
        std::lock_guard<std::recursive_mutex> lk(g_lock);
        g_seen.clear();
        return;
    }
    if (now - g_lastWake < 200 || !g_B.load()) return;
    g_lastWake = now;
    std::lock_guard<std::recursive_mutex> lk(g_lock);
    for (size_t i = 0; i < g_seen.size();) {
        Seen s = g_seen[i];
        uint8_t active = 0;
        if (now - s.at > 5000 || !is_a(s.node, g_vftDist) || !rd(s.node, static_cast<ptrdiff_t>(g_offActive), &active) || !active) {
            g_seen[i] = g_seen.back(); g_seen.pop_back(); c_wakeSkipped.fetch_add(1, std::memory_order_relaxed); continue;
        }
        ++i;
        KCDMP_FAULT_CALL(site, "wo201near::wake");
        fault::guarded(site, [&] { eval_detour(s.node); });
        c_wakes.fetch_add(1, std::memory_order_relaxed);
    }
}

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    // [op][on:1][figureEid:4] -> [armed:1][swaps:4][bound:4]
    if (len != 6 || cap < 9) return wo163::kRBadRequest;
    const bool on = body[1] != 0;
    uint32_t fe = 0; std::memcpy(&fe, body + 2, 4);
    if (on) {
        const char* why = "";
        if (!arm(&why)) { logf("WO201-NEAR NOT armed (%s) -- the player's distance triggers stay the host's Henry's", why); return wo163::kRFailed; }
    }
    if (on != g_on.load() || fe != g_figEid.load())
        logf("WO201-NEAR %s figure eid=0x%X", on ? "on" : "off", fe);
    g_on = on; g_figEid = fe;
    if (!on) { std::lock_guard<std::recursive_mutex> lk(g_lock); g_seen.clear(); g_B = nullptr; }
    out[0] = g_hooked ? 1 : 0;
    const uint32_t sw = c_swaps.load(), bd = c_bound.load();
    std::memcpy(out + 1, &sw, 4); std::memcpy(out + 5, &bd, 4);
    *outLen = 9;
    return wo163::kROk;
}

int status_text(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "near=%s on=%d safe=%d off_main=%u unsettled=%u fig=0x%X evals=%u bound=%u swaps=%u wakes=%u wake_dropped=%u", g_why,
                         wo202::on(wo202rules::kBitNearSafe) ? 1 : 0, c_offMain.load(), c_unsettled.load(),
                         g_on.load() ? 1 : 0, g_figEid.load(), c_evals.load(), c_bound.load(), c_swaps.load(), c_wakes.load(), c_wakeSkipped.load());
}

} // namespace kcdmp::wo201near
