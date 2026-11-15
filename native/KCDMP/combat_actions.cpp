// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// The lifetime of the combat animation actions this mod queues (combat_actions.h).
#include "combat_actions.h"
#include "combat_swing.h"
#include "wo221.h"
#include "wo203.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "log.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <vector>

namespace kcdmp::combatactions {

namespace {

// ---- anchors (read out of this build's files; every one is re-checked by its bytes at install, none is a fixed address) ---------
// C_Actor (EntityModule, RTTI .?AVC_Actor@entitymodule@wh@@): vftable slot 0xB60 = ReleaseCombatActor -- the one place a body's
// combat part (+0x300) is given back to the combat module: called (virtually only) by SetMonsterLOD, by the actor's reset / death
// path (slot 0x528 and its two overrides) and by the actor's shutdown.
constexpr size_t kActorVtReleaseCombat = 0xB60;
constexpr size_t kActorCombatActor = 0x300;
constexpr size_t kActorEntity = 0x38;      // the actor's entity (read in SetMonsterLOD: mov rdx,[r14+38h])
// C_CombatAnimAction (CombatModule, RTTI .?AVC_CombatAnimAction@combatmodule@wh@@): +0x28 status (0 none, 1 pending, 2 installed,
// 3 exiting, 4 finished), +0x58 reference count, +0x9C its body's entity id; vftable slot 14 (+0x70, "fragment started"), slot 17
// (+0x88, "the fragment is about to end"), slot 18 (+0x90): the three that reach for the combat part; slot 23 (+0xB8) deletes.
constexpr size_t kActStatus = 0x28, kActRefs = 0x58;
constexpr size_t kSlotStarted = 14, kSlotEnding = 17, kSlotEvent18 = 18, kSlotDelete = 23;
constexpr size_t kVtCopy = 48;          // slots copied into the quiet vftable (the class has 29; the rest is never called)
constexpr size_t kMaxTracked = 16384;
constexpr double kEndedKeepS = 60.0;    // an ended (stopped, quiet) action that never finished is forgotten after this, our reference kept
constexpr double kDoneKeepS = 30.0;     // a finished action is forgotten after this

struct Entry { uint32_t eid = 0; void* actor = nullptr; void* action = nullptr; double t0 = 0; bool ended = false; double endedAt = 0; };

std::mutex g_mu;
std::vector<Entry> g_tracked;
std::atomic<bool> g_armed{false};
void* const* g_actionVft = nullptr;      // the engine's own
void* g_quiet[1 + kVtCopy] = {};         // [0] = the engine's RTTI locator (vftable[-1]); [1..] = the quiet copy
using ReleaseFn = void (*)(void*);
ReleaseFn g_origRelease = nullptr;
std::atomic<uint32_t> c_withPart{0}, c_tracked{0}, c_hook{0}, c_ended{0}, c_stopped{0}, c_quieted{0}, c_explicit{0}, c_forgotten{0}, c_offMain{0}, c_orphans{0};
DWORD g_mainTid = 0;

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

void quiet_slot(void*) {}   // what the three callbacks become: nothing is read, nothing is called

bool rd_u32(const void* p, size_t off, uint32_t* out) {
    KCDMP_FAULT_READ(site, "combat_actions::rd_u32");
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const uint32_t*>(static_cast<const char*>(p) + off); });
}
bool rd_ptr(const void* p, size_t off, void** out) {
    KCDMP_FAULT_READ(site, "combat_actions::rd_ptr");
    return fault::guarded(site, [&] { *out = *reinterpret_cast<void* const*>(static_cast<const char*>(p) + off); });
}
bool add_ref(void* action) {
    KCDMP_FAULT_READ(site, "combat_actions::add_ref");
    return fault::guarded(site, [&] { InterlockedIncrement(reinterpret_cast<volatile LONG*>(static_cast<char*>(action) + kActRefs)); });
}
// the controller's own way of giving a reference back (CryAction: lock xadd [+0x58], -1; at zero the action's slot 23)
bool drop_ref(void* action) {
    KCDMP_FAULT_READ(site, "combat_actions::drop_ref");
    LONG left = 1;
    void** vt = nullptr;
    if (!fault::guarded(site, [&] {
            left = InterlockedDecrement(reinterpret_cast<volatile LONG*>(static_cast<char*>(action) + kActRefs));
            vt = *reinterpret_cast<void***>(action);
        })) return false;
    // the action's own delete frees memory (the allocator's lock): a plain call, never under the fault guard
    if (left <= 0 && vt) reinterpret_cast<void (*)(void*)>(vt[kSlotDelete])(action);
    return true;
}
bool set_vft(void* action) {
    KCDMP_FAULT_READ(site, "combat_actions::set_vft");
    bool done = false;
    fault::guarded(site, [&] {
        void** slot = reinterpret_cast<void**>(action);
        // only an object that still carries the engine's own vftable (never a freed or re-used block)
        done = InterlockedCompareExchangePointer(slot, static_cast<void*>(&g_quiet[1]), const_cast<void*>(static_cast<const void*>(g_actionVft))) ==
               static_cast<const void*>(g_actionVft);
    });
    return done;
}

// One recorded action ended (g_mu held). The stop is the engine's own (flags Stopping | BlendOut: a waiting action is never
// installed, a running one is blended out by its controller's next update); the quiet vftable closes the time until that update.
void end_entry(Entry& e, int* stopped, int* quieted) {
    if (e.ended) return;
    e.ended = true;
    e.endedAt = now_s();
    uint32_t st = 0;
    if (rd_u32(e.action, kActStatus, &st) && st >= 1 && st <= 3 && rttr::stop_action(e.action)) { ++*stopped; c_stopped.fetch_add(1); }
    if (g_armed.load() && set_vft(e.action)) { ++*quieted; c_quieted.fetch_add(1); }
    c_ended.fetch_add(1);
}

int end_where(uint32_t eid, void* actor, bool all, const char* why) {
    int n = 0, stopped = 0, quieted = 0;
    {
        std::lock_guard<std::mutex> lock(g_mu);
        for (Entry& e : g_tracked) {
            if (e.ended) continue;
            if (!all && !((eid && e.eid == eid) || (actor && e.actor == actor))) continue;
            if (!eid) eid = e.eid;
            end_entry(e, &stopped, &quieted);
            ++n;
        }
    }
    if (n) {
        static std::atomic<uint32_t> s_lines{0};
        const uint32_t k = s_lines.fetch_add(1) + 1;
        if (k <= 400 || k % 100 == 0)
            logf("WO221-ACTEND eid=0x%X actions=%d stopped=%d quiet=%d why=%s%s [n=%u]", all ? 0u : eid, n, stopped, quieted, why,
                 GetCurrentThreadId() == g_mainTid ? "" : " (not the main thread)", k);
    }
    return n;
}

// C_Actor::ReleaseCombatActor, every vftable's slot: ours first (while the combat part is still there), then the engine's own.
void hk_release_combat_actor(void* actor) {
    c_hook.fetch_add(1, std::memory_order_relaxed);
    if (GetCurrentThreadId() != g_mainTid) c_offMain.fetch_add(1, std::memory_order_relaxed);
    void* ca = nullptr;
    const bool has = rd_ptr(actor, kActorCombatActor, &ca) && ca;
    if (has) {
        // the bench's view of when the engine does this (the first calls, then every 500th): the body by its entity (C_Actor +0x38)
        const uint32_t k = c_withPart.fetch_add(1, std::memory_order_relaxed) + 1;
        if (k <= 150 || k % 500 == 0) {
            void* ent = nullptr;
            const char* nm = rd_ptr(actor, kActorEntity, &ent) && ent ? engine::entity_name(ent) : nullptr;
            char name[64] = "?";
            if (nm) { KCDMP_FAULT_READ(site, "combat_actions::name"); fault::guarded(site, [&] { strncpy_s(name, nm, _TRUNCATE); }); }
            logf("WO221-ACTEND the engine gives %s's combat part back%s [n=%u]", name, GetCurrentThreadId() == g_mainTid ? "" : " (not the main thread)", k);
        }
        // nothing to release means nothing of ours can be reading it either
        if (wo221::on(wo221::kBitCombatActionEnd)) end_where(0, actor, false, "the engine gives the body's combat part back");
    }
    g_origRelease(actor);
}

void fail(const char* why) { logf("WO221-ACTEND NOT ARMED: %s -- 0.65.0's behaviour (an action of ours can outlive its body's combat part)", why); }

void install() {
    HMODULE em = GetModuleHandleA("EntityModule.dll");
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    if (!em || !cm) { fail("EntityModule.dll or CombatModule.dll not loaded"); return; }
    void* const* actorVft = anchor::find_vftable(em, ".?AVC_Actor@entitymodule@wh@@", 0);
    g_actionVft = anchor::find_vftable(cm, ".?AVC_CombatAnimAction@combatmodule@wh@@", 0);
    if (!actorVft || !g_actionVft) { fail("RTTI C_Actor / C_CombatAnimAction vftable not unique"); return; }

    // the release: reads +0x300, calls the combat part's own shutdown (slot 0x2F0), hands it to the module (slot 0x68), clears +0x300
    const void* rel = actorVft[kActorVtReleaseCombat / 8];
    static const uint8_t pRead[] = {0x48, 0x8B, 0x97, 0x00, 0x03, 0x00, 0x00};                                  // mov rdx,[rdi+300h]
    static const uint8_t pShut[] = {0xFF, 0x90, 0xF0, 0x02, 0x00, 0x00};                                        // call [rax+2F0h]
    static const uint8_t pClear[] = {0x48, 0xC7, 0x87, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};         // mov qword [rdi+300h],0
    if (!anchor::function_has_bytes(em, rel, pRead, sizeof pRead) || !anchor::function_has_bytes(em, rel, pShut, sizeof pShut) ||
        !anchor::function_has_bytes(em, rel, pClear, sizeof pClear)) { fail("C_Actor slot 0xB60 is not ReleaseCombatActor by its bytes"); return; }
    // the two unguarded callbacks: each reads the entity id (+0x9C) and the actor's combat part (+0x300)
    static const uint8_t pEid14[] = {0x8B, 0x97, 0x9C, 0x00, 0x00, 0x00};                                       // mov edx,[rdi+9Ch]
    static const uint8_t pMgr14[] = {0x48, 0x8B, 0x81, 0x90, 0x04, 0x00, 0x00};                                 // mov rax,[rcx+490h]
    static const uint8_t pEid17[] = {0x8B, 0x95, 0x9C, 0x00, 0x00, 0x00};                                       // mov edx,[rbp+9Ch]
    static const uint8_t pCa17[] = {0x48, 0x8B, 0xB8, 0x00, 0x03, 0x00, 0x00};                                  // mov rdi,[rax+300h]
    if (!anchor::function_has_bytes(cm, g_actionVft[kSlotStarted], pEid14, sizeof pEid14) || !anchor::function_has_bytes(cm, g_actionVft[kSlotStarted], pMgr14, sizeof pMgr14) ||
        !anchor::function_has_bytes(cm, g_actionVft[kSlotEnding], pEid17, sizeof pEid17) || !anchor::function_has_bytes(cm, g_actionVft[kSlotEnding], pCa17, sizeof pCa17)) {
        fail("C_CombatAnimAction slots 14 / 17 are not the callbacks read in this build");
        return;
    }
    // the quiet copy: the engine's own table (its RTTI locator kept, so the object still names its class), three slots silenced
    g_quiet[0] = const_cast<void*>(static_cast<const void*>(g_actionVft[-1]));
    for (size_t i = 0; i < kVtCopy; ++i) g_quiet[1 + i] = const_cast<void*>(g_actionVft[i]);
    g_quiet[1 + kSlotStarted] = reinterpret_cast<void*>(&quiet_slot);
    g_quiet[1 + kSlotEnding] = reinterpret_cast<void*>(&quiet_slot);
    g_quiet[1 + kSlotEvent18] = reinterpret_cast<void*>(&quiet_slot);
    // rm6: an ended action is never deleted either -- a death takes the body's combat part (and its action manager) apart while the
    // controller's queue still holds the action; the block stays allocated (0x1A8 bytes) and the queue's walk finds a quiet action
    g_quiet[1 + kSlotDelete] = reinterpret_cast<void*>(&quiet_slot);

    // every vftable that carries the release (C_Actor and the classes derived from it) gets ours in that slot
    anchor::Range rdata{};
    if (!anchor::section(em, ".rdata", &rdata)) { fail("EntityModule .rdata not found"); return; }
    g_origRelease = reinterpret_cast<ReleaseFn>(const_cast<void*>(rel));
    int slots = 0, failed = 0;
    for (const uint8_t* p = rdata.begin; p + 8 <= rdata.end; p += 8) {
        void* v = nullptr;
        std::memcpy(&v, p, 8);
        if (v != rel) continue;
        DWORD old = 0;
        void** slot = reinterpret_cast<void**>(const_cast<uint8_t*>(p));
        if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &old)) { ++failed; continue; }
        InterlockedExchangePointer(slot, reinterpret_cast<void*>(&hk_release_combat_actor));
        DWORD ignored = 0;
        VirtualProtect(slot, sizeof(void*), old, &ignored);
        ++slots;
    }
    if (!slots) { fail("no vftable slot holds ReleaseCombatActor"); return; }
    g_armed = true;
    char d[128]{};
    anchor::describe(rel, d, sizeof d);
    logf("WO221-ACTEND armed: ReleaseCombatActor=%s in %d vftable slot(s)%s; the quiet vftable has slots 14, 17, 18 silenced -- every combat "
         "action of ours is recorded and ended before its body's combat part goes (mp_combat_action_end %s)",
         d, slots, failed ? " (some slots could not be written)" : "", wo221::on(wo221::kBitCombatActionEnd) ? "on" : "off");
}

// Main thread, once a second: finished actions are forgotten (our reference given back).
void tick() {
    if (!g_mainTid) g_mainTid = GetCurrentThreadId();   // the frame hook's thread (install runs on the injector's)
    static double s_last = 0;
    const double now = now_s();
    if (now - s_last < 1.0) return;
    s_last = now;
    std::vector<void*> done;
    {
        std::lock_guard<std::mutex> lock(g_mu);
        for (size_t i = 0; i < g_tracked.size();) {
            Entry& e = g_tracked[i];
            uint32_t st = 1;
            const bool readable = rd_u32(e.action, kActStatus, &st);
            // Our reference goes back only for a finished action (status none or finished: its controller is through with it).
            // Never for a waiting or running one, whatever its count says: rm3-d4 -- "only this table holds it" (count <= 1) was
            // true of two waiting installs the controller's queue still walked; the drop deleted them and the next walk ran into the
            // freed blocks. A record that was ended (its body's combat part went: stopped and quiet) and did not finish within
            // kEndedKeepS is forgotten with our reference KEPT: the action stays allocated (0x1A8 bytes), nothing can run into it.
            const bool finished = readable && (st == 0 || st == 4) && now - e.t0 > kDoneKeepS;
            const bool left = readable && !finished && e.ended && now - e.endedAt > kEndedKeepS;
            if (left) c_orphans.fetch_add(1, std::memory_order_relaxed);
            if (!readable || finished || left) {
                if (finished) done.push_back(e.action);
                g_tracked[i] = g_tracked.back();
                g_tracked.pop_back();
                c_forgotten.fetch_add(1, std::memory_order_relaxed);
            } else ++i;
        }
    }
    for (void* a : done) drop_ref(a);
}

int status(char* out, int n) {
    size_t live = 0;
    { std::lock_guard<std::mutex> lock(g_mu); live = g_tracked.size(); }
    return std::snprintf(out, static_cast<size_t>(n), "armed=%d on=%d recorded=%u live=%zu forgotten=%u (ref_kept=%u) release_calls=%u with_part=%u (off_main=%u) ended=%u stopped=%u quiet=%u explicit=%u",
                         g_armed.load() ? 1 : 0, wo221::on(wo221::kBitCombatActionEnd) ? 1 : 0, c_tracked.load(), live, c_forgotten.load(), c_orphans.load(), c_hook.load(),
                         c_withPart.load(), c_offMain.load(), c_ended.load(), c_stopped.load(), c_quieted.load(), c_explicit.load());
}

const bool kReg = wo203::reg_install("combat_actions", &install) && wo203::reg_tick("combat_actions", &tick) && wo221::add_status("combat_actions", &status);

} // namespace

bool armed() { return g_armed.load(); }

void track(uint32_t eid, void* actor, void* action) {
    if (!action || !wo221::on(wo221::kBitCombatActionEnd)) return;
    if (!add_ref(action)) return;
    c_tracked.fetch_add(1, std::memory_order_relaxed);
    void* evicted = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_mu);
        if (g_tracked.size() >= kMaxTracked) {   // the oldest record is ended (stopped, quiet) and forgotten -- our reference kept (never a delete here)
            int s = 0, q = 0;
            end_entry(g_tracked.front(), &s, &q);
            c_orphans.fetch_add(1, std::memory_order_relaxed);
            g_tracked.front() = g_tracked.back();
            g_tracked.pop_back();
        }
        g_tracked.push_back(Entry{eid, actor, action, now_s(), false});
    }
    if (evicted) drop_ref(evicted);
}

int end_for_entity(uint32_t eid, const char* why) {
    if (!eid || !wo221::on(wo221::kBitCombatActionEnd)) return 0;
    const int n = end_where(eid, nullptr, false, why);
    if (n) c_explicit.fetch_add(static_cast<uint32_t>(n), std::memory_order_relaxed);
    return n;
}

int end_all(const char* why) {
    if (!wo221::on(wo221::kBitCombatActionEnd)) return 0;
    const int n = end_where(0, nullptr, true, why);
    if (n) c_explicit.fetch_add(static_cast<uint32_t>(n), std::memory_order_relaxed);
    return n;
}

} // namespace kcdmp::combatactions
