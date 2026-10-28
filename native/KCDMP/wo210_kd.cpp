// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-210 C.3: a host NPC's knockdown on its copy, the engine's way (wo210_kd.h).
#include "wo210_kd.h"
#include "wo210.h"
#include "wo210_rules.h"
#include "wo206_rules.h"
#include "wo203.h"
#include "wo163.h"
#include "wo172.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "npc_scan.h"
#include "rttr_abi.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>

namespace kcdmp::wo210kd {

namespace {

using namespace wo210rules;

// ---- the engine's calls, read from this build at install ---------------------------------------------------------------------------
// The hit-death handler's own lethal-no-reaction branch asks the actor for its ragdoll manager (C_Actor slot RM) and calls the
// manager's slot 0 with mode 0 (start the ragdoll action now): `mov rcx,[r15+48h]; mov rax,[rcx]; call [rax+RM]; xor edx,edx;
// mov rcx,[rax]; mov r8,[rcx]; mov rcx,rax; call r8`. The script bind's StandUp calls RequestStandUp(manager, 1) with the manager read
// at the actor's own offset: `mov rcx,[rax+OFF]; mov edx,1; call RequestStandUp`.
struct Api {
    bool armed = false;
    std::string why = "not installed";
    uint32_t rmSlot = 0, rmOff = 0;
    void* const* vftRm = nullptr;
    void* requestRagdoll = nullptr;   // C_RagdollManager slot 0: (rm, int mode)
    void* isRagdoll = nullptr;        // slot 1: bool (rm)
    void* requestStandUp = nullptr;   // non-virtual: (rm, int)
    // C.1 / C.2 the finish (OnHit's lethal-no-reaction branch)
    bool deadArmed = false;
    std::string whyDead = "not installed";
    uint32_t modelSlot = 0, isStateSlot = 0, holderSlot = 0, isDeadSlot = 0;
    uint8_t deadState = 0;
    void* expGetter = nullptr;        // (holder) -> the state expansion
    void* startDead = nullptr;        // (expansion, bool keepPrevious)
} A;

KCDMP_FAULT_CALL(g_siteFall, "wo210kd::RequestRagdoll");
KCDMP_FAULT_CALL(g_siteUp, "wo210kd::RequestStandUp");
KCDMP_FAULT_CALL(g_siteIs, "wo210kd::IsRagdoll");
KCDMP_FAULT_CALL(g_siteRm, "wo210kd::ragdoll_manager");

std::unordered_map<uint32_t, KdState> g_kd;   // main thread
std::unordered_map<uint32_t, double> g_heldAt;   // the copies held down now (the drive's question), main thread
std::atomic<uint32_t> c_falls{0}, c_fallRefused{0}, c_ups{0}, c_ownUps{0}, c_deadReleases{0}, c_switchUps{0}, c_hostDownSent{0}, c_noManager{0};

bool find_seq_exact(const uint8_t* b, size_t n, const uint8_t* pat, size_t pn, size_t* at) {
    for (size_t o = 0; o + pn <= n; ++o) if (!std::memcmp(b + o, pat, pn)) { *at = o; return true; }
    return false;
}

void install() {
    HMODULE em = GetModuleHandleA("EntityModule.dll");
    if (!em) { A.why = "EntityModule not loaded"; logf("WO210-KD-BUILD not armed: %s", A.why.c_str()); return; }
    // the ragdoll manager's slot on C_Actor, from the hit-death handler's own call
    const uint8_t* onHit = anchor::function_by_string(em, "wh::entitymodule::C_HitDeathReactions::OnHit");
    static const uint8_t kRm1[] = {0x49, 0x8B, 0x4F, 0x48, 0x48, 0x8B, 0x01, 0xFF, 0x90};
    static const uint8_t kRm2[] = {0x33, 0xD2, 0x48, 0x8B, 0x08, 0x4C, 0x8B, 0x01, 0x48, 0x8B, 0xC8, 0x41, 0xFF, 0xD0};
    const uint8_t* rm = onHit ? anchor::function_find_sequence(em, onHit, kRm1, sizeof kRm1, kRm2, sizeof kRm2, sizeof kRm1 + 4 + sizeof kRm2) : nullptr;
    if (!onHit) A.why = "the hit-death handler (its label) not found";
    else if (!rm || std::memcmp(rm + sizeof kRm1 + 4, kRm2, sizeof kRm2) != 0) A.why = "the handler's ragdoll call not found";
    else std::memcpy(&A.rmSlot, rm + sizeof kRm1, 4);
    void* const* vftActor = anchor::find_vftable(em, ".?AVC_Actor@entitymodule@wh@@", 0);
    A.vftRm = anchor::find_vftable(em, ".?AVC_RagdollManager@entitymodule@wh@@", 0);
    void* const* vftBind = anchor::find_vftable(em, ".?AVC_ScriptBindActor@entitymodule@wh@@", 0);
    anchor::Range text{};
    anchor::section(em, ".text", &text);
    if (A.rmSlot && (A.rmSlot % 8 != 0 || A.rmSlot > 0x2000)) { A.why = "the ragdoll slot is not a vtable slot"; A.rmSlot = 0; }
    if (A.rmSlot && (!vftActor || !A.vftRm || !vftBind)) { A.why = "RTTI C_Actor / C_RagdollManager / C_ScriptBindActor vftable not unique"; A.rmSlot = 0; }
    // the actor's slot returns its own field: mov rax,[rcx|rbx+OFF] near its start
    if (A.rmSlot) {
        const uint8_t* g = static_cast<const uint8_t*>(vftActor[A.rmSlot / 8]);
        size_t at = 0;
        static const uint8_t kRbx[] = {0x48, 0x8B, 0x83}, kRcx[] = {0x48, 0x8B, 0x81};
        if (!text.contains(g)) A.why = "the actor's ragdoll slot is outside the module's code";
        else if (find_seq_exact(g, 0x30, kRbx, 3, &at) || find_seq_exact(g, 0x30, kRcx, 3, &at)) std::memcpy(&A.rmOff, g + at + 3, 4);
        else A.why = "the actor's ragdoll slot reads no field";
    }
    // RequestStandUp: the one bind method that reads the actor's manager at that field and calls a function with 1
    if (A.rmOff) {
        int found = 0;
        for (int s = 0; s < 0x400 / 8; ++s) {
            const uint8_t* f = static_cast<const uint8_t*>(vftBind[s]);
            if (!text.contains(f) || !text.contains(f + 0x80)) continue;
            for (size_t o = 0; o + 17 <= 0x80; ++o) {
                if (f[o] != 0x48 || f[o + 1] != 0x8B || f[o + 2] != 0x88) continue;
                uint32_t off = 0; std::memcpy(&off, f + o + 3, 4);
                static const uint8_t kOne[] = {0xBA, 0x01, 0x00, 0x00, 0x00, 0xE8};
                if (off != A.rmOff || std::memcmp(f + o + 7, kOne, sizeof kOne) != 0) continue;
                int32_t rel = 0; std::memcpy(&rel, f + o + 13, 4);
                const uint8_t* tgt = f + o + 17 + rel;
                anchor::Range fr{};
                if (text.contains(tgt) && anchor::function_range(em, tgt, &fr) && fr.begin == tgt) { A.requestStandUp = const_cast<uint8_t*>(tgt); ++found; }
                break;
            }
        }
        if (found != 1) { A.requestStandUp = nullptr; A.why = found ? "more than one bind method stands an actor up" : "the script bind's StandUp not found"; }
    }
    if (A.requestStandUp) {
        A.requestRagdoll = A.vftRm[0];
        A.isRagdoll = A.vftRm[1];
        if (!text.contains(A.requestRagdoll) || !text.contains(A.isRagdoll)) A.why = "the ragdoll manager's slots are outside the module's code";
        else { A.armed = true; A.why.clear(); }
    }
    char d1[96] = "-", d2[96] = "-", d3[96] = "-";
    if (A.requestRagdoll) anchor::describe(A.requestRagdoll, d1, sizeof d1);
    if (A.isRagdoll) anchor::describe(A.isRagdoll, d2, sizeof d2);
    if (A.requestStandUp) anchor::describe(A.requestStandUp, d3, sizeof d3);
    if (A.armed) logf("WO210-KD-BUILD armed: C_Actor slot 0x%X -> its manager (+0x%X); RequestRagdoll %s IsRagdoll %s RequestStandUp %s", A.rmSlot, A.rmOff, d1, d2, d3);
    else logf("WO210-KD-BUILD not armed: %s -- a copy does not fall with its host NPC (the old behaviour)", A.why.c_str());

    // ---- C.1 / C.2: the hit-death handler's own lethal-no-reaction branch -- the actor's model and its dead test, the state-expansion
    // holder, the expansion getter and StartDeadAction:
    //   mov rcx,[r15+48h]; mov rax,[rcx]; call [rax+MODEL]; mov rdi,rax              (the model)
    //   ...; mov rax,[rdi]; mov dl,16h; mov rcx,rdi; call [rax+ISSTATE]; test al,al; jne ..   (dead = 22)
    //   mov rcx,[r15+48h]; mov rax,[rcx]; call [rax+HOLDER]; mov rcx,rax; call EXP; xor edx,edx; mov rcx,rax; call START_DEAD
    if (A.armed && onHit) {
        static const uint8_t kM1[] = {0x49, 0x8B, 0x4F, 0x48, 0x48, 0x8B, 0x01, 0xFF, 0x90}, kM2[] = {0x48, 0x8B, 0xF8};
        static const uint8_t kD1[] = {0x48, 0x8B, 0x07, 0xB2}, kD2[] = {0x48, 0x8B, 0xCF, 0xFF, 0x90};
        const uint8_t* d = anchor::function_find_sequence(em, onHit, kD1, sizeof kD1, kD2, sizeof kD2, sizeof kD1 + 1 + sizeof kD2);
        bool ok = d && !std::memcmp(d + 5, kD2, sizeof kD2) && d[14] == 0x84 && d[15] == 0xC0 && d[16] == 0x75 &&
                  !std::memcmp(d + 18, kM1, sizeof kM1) && d[31] == 0x48 && d[32] == 0x8B && d[33] == 0xC8 && d[34] == 0xE8 &&
                  d[39] == 0x33 && d[40] == 0xD2 && d[41] == 0x48 && d[42] == 0x8B && d[43] == 0xC8 && d[44] == 0xE8;
        if (ok) {
            A.deadState = d[4];
            std::memcpy(&A.isStateSlot, d + 10, 4);
            std::memcpy(&A.holderSlot, d + 27, 4);
            int32_t r1 = 0, r2 = 0; std::memcpy(&r1, d + 35, 4); std::memcpy(&r2, d + 45, 4);
            A.expGetter = const_cast<uint8_t*>(d + 39 + r1);
            A.startDead = const_cast<uint8_t*>(d + 49 + r2);
            // the model getter: the nearest "mov rcx,[r15+48h]; mov rax,[rcx]; call [rax+X]; mov rdi,rax" within 0x40 bytes before, then
            // the actor's own dead test right after it: "mov rcx,[r15+48h]; mov rax,[rcx]; call [rax+Y]; test al,al; je"
            for (const uint8_t* p = d - 1; p > d - 0x40 && !A.modelSlot; --p)
                if (!std::memcmp(p, kM1, sizeof kM1) && !std::memcmp(p + 13, kM2, sizeof kM2)) {
                    std::memcpy(&A.modelSlot, p + 9, 4);
                    if (!std::memcmp(p + 16, kM1, sizeof kM1) && p[29] == 0x84 && p[30] == 0xC0 && p[31] == 0x74) std::memcpy(&A.isDeadSlot, p + 25, 4);
                }
        }
        static const uint8_t kType[] = {0x41, 0xB0, 0x0F};   // mov r8b,0Fh: the action-by-type factory asked for type 0xF ("dead")
        anchor::Range fr{};
        size_t at = 0;
        if (!ok) A.whyDead = "the handler's dead-action branch not found";
        else if (!A.modelSlot || !A.isDeadSlot) A.whyDead = "the handler's model call or the actor's dead test not found";
        else if (A.modelSlot % 8 || A.isStateSlot % 8 || A.holderSlot % 8 || A.isDeadSlot % 8 || A.modelSlot > 0x2000 || A.isStateSlot > 0x1000 ||
                 A.holderSlot > 0x2000 || A.isDeadSlot > 0x2000)
            A.whyDead = "a slot is not a vtable slot";
        else if (!text.contains(A.startDead) || !anchor::function_range(em, A.startDead, &fr) || fr.begin != A.startDead ||
                 !find_seq_exact(static_cast<const uint8_t*>(A.startDead), 0x60, kType, sizeof kType, &at))
            A.whyDead = "StartDeadAction is not the dead-action starter";
        else if (!text.contains(A.expGetter)) A.whyDead = "the expansion getter is outside the module's code";
        else { A.deadArmed = true; A.whyDead.clear(); }
        if (A.deadArmed)
            logf("WO210-DEATH finish armed: the actor's dead test slot 0x%X, model slot 0x%X, its state test slot 0x%X (dead = %u), holder slot 0x%X, "
                 "StartDeadAction found by the handler's own call", A.isDeadSlot, A.modelSlot, A.isStateSlot, A.deadState, A.holderSlot);
        else logf("WO210-DEATH finish not armed: %s -- a body dead without the death reaction keeps WO-202's physics fall", A.whyDead.c_str());
    } else if (!A.armed) A.whyDead = "the ragdoll manager is not armed";
}

// The copy's ragdoll manager through the actor's own slot, checked by RTTI; null when none.
void* manager_of(uint32_t eid) {
    void* actor = motion::actor_of(eid);
    if (!actor) return nullptr;
    void* rm = fault::guarded_or<void*>(g_siteRm, nullptr, [&]() -> void* {
        void* const* vt = *static_cast<void* const* const*>(actor);
        using Get = void* (*)(void*);
        return reinterpret_cast<Get>(vt[A.rmSlot / 8])(actor);
    });
    if (!rm || !fault::plausible_address(reinterpret_cast<uintptr_t>(rm))) return nullptr;
    void* const* vt = nullptr;
    if (!fault::guarded(g_siteRm, [&] { vt = *static_cast<void* const* const*>(rm); }) || vt != A.vftRm) return nullptr;
    return rm;
}

bool is_ragdoll(void* rm) {
    return fault::guarded_or<bool>(g_siteIs, false, [&]() -> bool { using F = bool (*)(void*); return reinterpret_cast<F>(A.isRagdoll)(rm); });
}

void count_line(const char* npc, const char* what, double heldS, std::atomic<uint32_t>& c) {
    const uint32_t k = c.fetch_add(1) + 1;
    if (k <= 200 || k % 100 == 0) logf("WO210-KD %s npc=%s held_ms=%.0f [n=%u]", what, npc ? npc : "?", heldS * 1000.0, k);
}

} // namespace

bool host_down(uint32_t eid, void* e, const char* fullBodyClass) {
    if (!e || npcscan::body_kind(e) != 0) return false;   // humans only: a horse's physics is never a living entity
    // the knockdown's own action (its class names the ragdoll or the knockdown: the probe's test) starts the fall 0.7-1 s before the
    // physics goes limp (d8: the copies fell 0.7-1.0 s late, p50, on the physics alone); either one is down
    if (wo210::fix_on(wo210rules::kFixKdByAction) && wo206rules::ragdoll_class(fullBodyClass)) { c_hostDownSent.fetch_add(1, std::memory_order_relaxed); return true; }
    npcdrive::PhysicsStatus ps{};
    const bool down = npcdrive::physics_status(e, &ps) && ps.present && !ps.living;
    if (down) c_hostDownSent.fetch_add(1, std::memory_order_relaxed);
    (void)eid;
    return down;
}

bool copy_tick(uint32_t eid, const char* name, bool hostDown, double now) {
    if (!A.armed) return false;
    auto it = g_kd.find(eid);
    if (!hostDown && it == g_kd.end()) return false;   // the common case: no knockdown, no state
    KdState& s = g_kd[eid];
    void* rm = manager_of(eid);
    if (!rm) {
        if (s.held) { c_noManager.fetch_add(1); g_heldAt.erase(eid); }
        g_kd.erase(eid);
        return false;
    }
    const bool rag = is_ragdoll(rm);
    const double fellAt = s.fellAt;
    const KdAct act = kd_step(s, wo210::on(kBitKnockdownSync), hostDown, rag, wo172::copy_dead(eid), now);
    switch (act) {
        case KdAct::Fall: {
            const bool ok = fault::guarded(g_siteFall, [&] { using F = void (*)(void*, int); reinterpret_cast<F>(A.requestRagdoll)(rm, 0); });
            if (!ok) { s = KdState{}; c_fallRefused.fetch_add(1); logf("WO210-KD fall npc=%s FAULTED -- the copy stands (its host NPC's knockdown not shown)", name ? name : "?"); break; }
            const uint32_t k = c_falls.fetch_add(1) + 1;
            if (k <= 200 || k % 100 == 0) logf("WO210-KD fall npc=%s (its host NPC is down: the engine's own ragdoll) [n=%u]", name ? name : "?", k);
            break;
        }
        case KdAct::StandUp: {
            const bool off = !wo210::on(kBitKnockdownSync);
            const bool ok = fault::guarded(g_siteUp, [&] { using F = void (*)(void*, int); reinterpret_cast<F>(A.requestStandUp)(rm, 1); });
            count_line(name, ok ? (off ? "up how=switch-off" : "up how=ours") : "up FAULTED", now - fellAt, off ? c_switchUps : c_ups);
            break;
        }
        case KdAct::Release:
            if (wo172::copy_dead(eid)) count_line(name, "release how=dead (it lies as it died)", now - fellAt, c_deadReleases);
            else count_line(name, "up how=own (its engine stood it up first)", now - fellAt, c_ownUps);
            break;
        case KdAct::None: break;
    }
    if (s.held) g_heldAt[eid] = now; else g_heldAt.erase(eid);
    if (!s.held && !s.hostDown) g_kd.erase(eid);
    return s.held;
}

// A hold lasts while the owner keeps asking (its tick refreshes it); one its stream stopped speaking of for kHeldStaleS is no hold (the
// body is its own engine's again, and the next state settles it).
constexpr double kHeldStaleS = 3.0;
bool held_down(uint32_t eid) {
    if (g_heldAt.empty()) return false;
    const auto it = g_heldAt.find(eid);
    return it != g_heldAt.end() && npcdrive::now_s() - it->second <= kHeldStaleS;
}

void forget(uint32_t eid) { g_kd.erase(eid); g_heldAt.erase(eid); }

// ---- C.1 / C.2: a body dead without the death reaction, finished the engine's way (op 142, main thread) ----------------------------
namespace {

constexpr uint8_t kOpFinish = 142;
constexpr uint8_t kFinDone = 0, kFinAlready = 1, kFinAlive = 2, kFinNotArmed = 3, kFinNoBody = 4, kFinFaulted = 5;
KCDMP_FAULT_CALL(g_siteFinish, "wo210kd::finish");
std::atomic<uint32_t> c_fin[6]{};

const char* fin_name(uint8_t r) {
    static const char* const k[] = {"finished", "already-dead-state", "alive", "not-armed", "no-body", "faulted"};
    return r < 6 ? k[r] : "?";
}

// The handler's own order: the actor's dead test, the model's dead state, the ragdoll (unless lying), StartDeadAction(expansion, false).
uint8_t finish(uint32_t eid, uint8_t* deadStateAfter, bool* ragdolled) {
    *deadStateAfter = 0; *ragdolled = false;
    if (!A.deadArmed) return kFinNotArmed;
    void* actor = motion::actor_of(eid);
    if (!actor) return kFinNoBody;
    using B0 = bool (*)(void*);
    using P0 = void* (*)(void*);
    using BS = bool (*)(void*, uint8_t);
    using VB = void (*)(void*, bool);
    uint8_t res = kFinFaulted;
    void* model = nullptr;
    const bool ok1 = fault::guarded(g_siteFinish, [&] {
        void* const* vt = *static_cast<void* const* const*>(actor);
        if (!reinterpret_cast<B0>(vt[A.isDeadSlot / 8])(actor)) { res = kFinAlive; return; }
        model = reinterpret_cast<P0>(vt[A.modelSlot / 8])(actor);
        if (!model) return;
        void* const* mvt = *static_cast<void* const* const*>(model);
        if (reinterpret_cast<BS>(mvt[A.isStateSlot / 8])(model, A.deadState)) { res = kFinAlready; *deadStateAfter = 1; return; }
        res = kFinDone;
    });
    if (!ok1 || res != kFinDone) return ok1 ? res : kFinFaulted;
    if (void* rm = manager_of(eid)) {
        if (!is_ragdoll(rm) && fault::guarded(g_siteFall, [&] { using F = void (*)(void*, int); reinterpret_cast<F>(A.requestRagdoll)(rm, 0); })) *ragdolled = true;
    }
    const bool ok2 = fault::guarded(g_siteFinish, [&] {
        void* const* vt = *static_cast<void* const* const*>(actor);
        void* holder = reinterpret_cast<P0>(vt[A.holderSlot / 8])(actor);
        if (!holder) { res = kFinFaulted; return; }
        void* exp = reinterpret_cast<P0>(A.expGetter)(holder);
        if (!exp) { res = kFinFaulted; return; }
        reinterpret_cast<VB>(A.startDead)(exp, false);
        void* const* mvt = *static_cast<void* const* const*>(model);
        *deadStateAfter = reinterpret_cast<BS>(mvt[A.isStateSlot / 8])(model, A.deadState) ? 1 : 0;
    });
    return ok2 ? res : kFinFaulted;
}

// [op][eid:4][nameLen:1][name] (eid 0 = by name) -> [result:1][deadState:1]
uint8_t op_finish(uint8_t, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 6 || cap < 2) return wo163::kRBadRequest;
    uint32_t eid = 0;
    std::memcpy(&eid, body + 1, 4);
    const size_t nl = body[5];
    if (len != 6 + nl || nl > 63) return wo163::kRBadRequest;
    char name[64] = {};
    std::memcpy(name, body + 6, nl);
    if (!eid && nl) eid = hits::eid_of_name(name);
    uint8_t dead = 0; bool rag = false;
    const uint8_t r = eid ? finish(eid, &dead, &rag) : kFinNoBody;
    const uint32_t k = c_fin[r < 6 ? r : 5].fetch_add(1) + 1;
    if (r != kFinAlready || k <= 50)
        logf("WO210-DEATH finish npc=%s result=%s dead_state=%u ragdoll=%s [n=%u]", nl ? name : "?", fin_name(r), dead, rag ? "requested" : "lying-already-or-none", k);
    out[0] = r; out[1] = dead;
    *outLen = 2;
    return wo163::kROk;
}

} // namespace

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "w210kd armed=%d%s%s falls=%u fall_refused=%u ups=%u own_ups=%u dead_releases=%u switch_ups=%u held=%zu no_manager=%u host_down_reads=%u"
                         " | finish armed=%d%s%s finished=%u already=%u alive=%u no_body=%u faulted=%u",
                         A.armed ? 1 : 0, A.armed ? "" : " why=", A.armed ? "" : A.why.c_str(), c_falls.load(), c_fallRefused.load(), c_ups.load(), c_ownUps.load(),
                         c_deadReleases.load(), c_switchUps.load(), g_heldAt.size(), c_noManager.load(), c_hostDownSent.load(),
                         A.deadArmed ? 1 : 0, A.deadArmed ? "" : " why=", A.deadArmed ? "" : A.whyDead.c_str(), c_fin[kFinDone].load(), c_fin[kFinAlready].load(),
                         c_fin[kFinAlive].load(), c_fin[kFinNoBody].load(), c_fin[kFinFaulted].load());
}

namespace {
int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_install("w210kd", &install) && wo203::reg_status("w210kd", &status_reg) && wo203::reg_op(kOpFinish, &op_finish);
} // namespace

} // namespace kcdmp::wo210kd
