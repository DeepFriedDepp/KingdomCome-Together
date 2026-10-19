// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-201 B11 figure_ranged (both): this player's crossbow and bow, observed. Ranged shooting is not combat-director work (WO-121's attack
// capture never sees it -- the field: 0 attack rows while the joiner shot a crossbow); every actor runs it as its own shooting
// expansion (C_ActorShootingExpansion, EntityModule), whose running main action holds the current step: one of
// C_ActorActionShooting{Charging, InstantCharging, Aiming, Firing, Reloading, Uncharging, Unloading, TransitioningToIdle}.
// Read once a frame for the local player only, no hook: the actor's expansion holder (motion.cpp's slot, checked by what its body
// returns), the expansion with id 4 by the holder's own lookup (it never creates one), its main action, the step -- every pointer
// checked against its RTTI vtable that frame. A change of step is queued for the agent (op 86), which shows it on this player's figure
// on the partner's screen (an animation of the game's own fragments: never a shot of the figure's own -- no projectile).  WO201-RANGED.
#include "wo201.h"
#include "wo163.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "log.h"
#include "motion.h"
#include "rttr_abi.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <vector>

namespace kcdmp::wo201ranged {

namespace {

constexpr uint32_t kPlayerEntity = 0x7777;
constexpr size_t kActorExpHolder = 0x980;   // motion.cpp: returns actor+0x308, the expansion holder (checked below by its body)
constexpr size_t kHolderGetExp = 0x70;      // motion.cpp: holder->vtbl[0x70](holder, id) = the expansion of that id, never created
constexpr uint8_t kShootingId = 4;          // the id the expansion factory registers C_ActorShootingExpansion under
constexpr ptrdiff_t kExpMain = 0x68;        // the expansion's running main action (checked by its class)
constexpr ptrdiff_t kMainStep = 0xA0;       // the main action's current step (checked by its class)

const char* g_why = "not resolved";
void* const* g_vftExp = nullptr;
void* const* g_vftMain = nullptr;
struct StepClass { const char* rtti; const char* name; uint8_t phase; void* const* vft; };
StepClass g_steps[] = {
    {".?AVC_ActorActionShootingCharging@entitymodule@wh@@", "charging", 1, nullptr},
    {".?AVC_ActorActionShootingInstantCharging@entitymodule@wh@@", "charging", 1, nullptr},
    {".?AVC_ActorActionShootingAiming@entitymodule@wh@@", "aiming", 2, nullptr},
    {".?AVC_ActorActionShootingFiring@entitymodule@wh@@", "firing", 3, nullptr},
    {".?AVC_ActorActionShootingReloading@entitymodule@wh@@", "reloading", 4, nullptr},
    {".?AVC_ActorActionShootingUncharging@entitymodule@wh@@", "uncharging", 5, nullptr},
    {".?AVC_ActorActionShootingUnloading@entitymodule@wh@@", "leaving", 6, nullptr},
    {".?AVC_ActorActionShootingTransitioningToIdle@entitymodule@wh@@", "leaving", 6, nullptr},
};
bool g_holderOk = false;
std::atomic<bool> g_on{false};
uint8_t g_last = 0;
void* g_lastSoul = nullptr;
uint64_t g_settleUntil = 0, g_lastRead = 0, g_lastTick = 0;
std::atomic<uint32_t> c_reads{0}, c_changes{0}, c_unreadable{0};
std::mutex g_lock;
std::vector<uint8_t> g_q;   // under g_lock: phases in order, at most 32

template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    if (!base) return false;
    KCDMP_FAULT_READ(site, "wo201ranged::read");
    if (!fault::enabled(site)) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}
bool is_a(const void* obj, void* const* vft) {
    void* vp = nullptr;
    return obj && vft && rd(obj, 0, &vp) && vp == static_cast<const void*>(vft);
}
void* vslot(const void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    return obj && rd(obj, 0, &vt) && vt && rd(vt, static_cast<ptrdiff_t>(off), &fn) ? fn : nullptr;
}
bool body_has(const void* fn, const uint8_t* pat, size_t n, size_t within) {
    uint8_t b[64]{};
    if (within > sizeof b) within = sizeof b;
    KCDMP_FAULT_READ(site, "wo201ranged::body");
    if (!fn || !fault::guarded(site, [&] { std::memcpy(b, fn, within); })) return false;
    for (size_t i = 0; i + n <= within; ++i) if (std::memcmp(b + i, pat, n) == 0) return true;
    return false;
}

// The player's current shooting phase: 0 none, 1 charging, 2 aiming, 3 firing, 4 reloading, 5 uncharging, 6 leaving; 255 unreadable.
uint8_t read_phase() {
    void* actor = motion::actor_of(kPlayerEntity);
    if (!actor) return 255;
    void* hf = vslot(actor, kActorExpHolder);
    if (!hf) return 255;
    if (!g_holderOk) {
        static const uint8_t movHolder[] = {0x48, 0x8B, 0x83, 0x08, 0x03, 0x00, 0x00};   // mov rax,[rbx+0x308]
        static const uint8_t leaHolder[] = {0x48, 0x8D, 0x81, 0x08, 0x03, 0x00, 0x00};   // lea rax,[rcx+0x308]
        g_holderOk = body_has(hf, movHolder, sizeof movHolder, 48) || body_has(hf, leaHolder, sizeof leaHolder, 48);
        if (!g_holderOk) { g_why = "the actor's expansion-holder slot does not return the holder"; return 255; }
    }
    void* holder = nullptr;
    KCDMP_FAULT_CALL(hsite, "wo201ranged::holder");
    if (!fault::guarded(hsite, [&] { holder = reinterpret_cast<void* (*)(void*)>(hf)(actor); }) || !holder) return 255;
    void* get = vslot(holder, kHolderGetExp);
    void* exp = nullptr;
    KCDMP_FAULT_CALL(gsite, "wo201ranged::expansion");
    if (!get || !fault::guarded(gsite, [&] { exp = reinterpret_cast<void* (*)(void*, uint8_t)>(get)(holder, kShootingId); })) return 255;
    if (!exp) return 0;                        // never shot this session: no expansion yet
    if (!is_a(exp, g_vftExp)) return 255;
    void* main = nullptr;
    if (!rd(exp, kExpMain, &main)) return 255;
    if (!main) return 0;
    if (!is_a(main, g_vftMain)) return 255;
    void* step = nullptr;
    if (!rd(main, kMainStep, &step) || !step) return 0;
    void* vp = nullptr;
    if (!rd(step, 0, &vp)) return 255;
    for (const auto& s : g_steps) if (s.vft && vp == static_cast<const void*>(s.vft)) return s.phase;
    return 0;   // a step this read does not name (start / done)
}

const char* phase_name(uint8_t p) {
    switch (p) {
        case 0: return "none"; case 1: return "charging"; case 2: return "aiming"; case 3: return "firing";
        case 4: return "reloading"; case 5: return "uncharging"; case 6: return "leaving";
    }
    return "?";
}

} // namespace

void install() {
    HMODULE em = GetModuleHandleA("EntityModule.dll");
    if (!em) { g_why = "EntityModule.dll not loaded"; return; }
    g_vftExp = anchor::find_vftable(em, ".?AVC_ActorShootingExpansion@entitymodule@wh@@", 0);
    g_vftMain = anchor::find_vftable(em, ".?AVC_ActorActionShootingMain@entitymodule@wh@@", 0);
    int found = 0;
    for (auto& s : g_steps) { s.vft = anchor::find_vftable(em, s.rtti, 0); if (s.vft) ++found; }
    if (!g_vftExp || !g_vftMain || found < 6) { g_why = "the shooting classes not found by RTTI"; return; }
    g_why = "resolved";
    logf("WO201-RANGED resolver: the shooting expansion, its main action and %d of %zu step classes by RTTI -- the local player's crossbow and "
         "bow are read once a frame (no hook)", found, sizeof g_steps / sizeof g_steps[0]);
}

void tick() {
    if (!g_on.load() || std::strcmp(g_why, "resolved") != 0) return;
    const uint64_t t = GetTickCount64();
    // never through a load or a menu: a long frame, or a new player soul (a load swaps it), waits 5 s before the next read
    const uint64_t gap = t - g_lastTick;
    g_lastTick = t;
    if (gap > 500) g_settleUntil = t + 5000;
    void* ps = rttr::read_player_soul();
    if (!ps) { g_settleUntil = t + 5000; return; }
    if (ps != g_lastSoul) { g_lastSoul = ps; g_settleUntil = t + 5000; return; }
    if (t < g_settleUntil || t - g_lastRead < 50) return;   // 20 reads a second: a shot's step lasts its whole clip
    g_lastRead = t;
    const uint8_t p = read_phase();
    c_reads.fetch_add(1, std::memory_order_relaxed);
    if (p == 255) { c_unreadable.fetch_add(1, std::memory_order_relaxed); return; }
    if (p == g_last) return;
    const uint8_t was = g_last;
    g_last = p;
    c_changes.fetch_add(1, std::memory_order_relaxed);
    {
        std::lock_guard<std::mutex> lk(g_lock);
        if (g_q.size() < 32) g_q.push_back(p);
    }
    static uint64_t lastLog = 0; static uint32_t quiet = 0;
    const uint64_t now = GetTickCount64();
    if (now - lastLog >= 1000 || p == 3) {
        lastLog = now;
        logf("WO201-RANGED local %s -> %s%s [changes=%u] thread=%lu", phase_name(was), phase_name(p), quiet ? " (and more)" : "", c_changes.load(),
             static_cast<unsigned long>(GetCurrentThreadId()));
        quiet = 0;
    } else ++quiet;
}

// op 86 Poll [on:1] -> [n:1][phase...]
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len != 2 || cap < 2) return wo163::kRBadRequest;
    g_on = body[1] != 0;
    if (std::strcmp(g_why, "resolved") != 0) return wo163::kRFailed;
    std::lock_guard<std::mutex> lk(g_lock);
    size_t n = g_q.size() < cap - 1 ? g_q.size() : cap - 1;
    out[0] = static_cast<uint8_t>(n);
    for (size_t i = 0; i < n; ++i) out[1 + i] = g_q[i];
    g_q.erase(g_q.begin(), g_q.begin() + static_cast<ptrdiff_t>(n));
    *outLen = 1 + n;
    return wo163::kROk;
}

void on_pipe_closed() { g_on = false; g_last = 0; std::lock_guard<std::mutex> lk(g_lock); g_q.clear(); }

int status_text(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "ranged=%s on=%d reads=%u changes=%u unreadable=%u now=%s", g_why, g_on.load() ? 1 : 0,
                         c_reads.load(), c_changes.load(), c_unreadable.load(), phase_name(g_last));
}

} // namespace kcdmp::wo201ranged
