// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-166: 0.48.2's native half (wo166.h).
#include "wo166.h"
#include "wo163.h"
#include "wo166_rules.h"
#include "wo172.h"
#include "wo178.h"
#include "wo178_rules.h"
#include "wo187.h"
#include "wo187_rules.h"
#include "wo190.h"
#include "wo190_rules.h"
#include "wo197.h"
#include "wo197_rules.h"
#include <cmath>
#include "anchors.h"
#include "fault_guard.h"

#include "engine.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"

#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace kcdmp::wo166 {

namespace {

using namespace kcdmp::wo166rules;

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

constexpr double kDiscardTailS = 1.5;     // the copy stays on the discard list this long after its window (a late contact)
constexpr double kRecentS = 2.0;          // striking_recently's horizon
constexpr size_t kMaxStrikes = 32;
constexpr unsigned kLogFirst = 30;
constexpr double kFigureTailS = 0.45;     // WO-173: a figure's window runs on this long after the row's own end        // the first windows each get a line; then every 50th

struct Strike {
    uint32_t eid = 0;
    std::string name;
    double begin = 0, end = 0, discardUntil = 0, lastEnd = 0;
    motion::AttackFields fields{}, priorFields{};
    int32_t prior = 0;
    bool wrote = false, done = false, midRead = false, discardAdded = false, waited = false;
    bool engine = false, windup = false;   // WO-178: the engine's own state setter drives this window (wind-up 4 from the row's start)
    bool countdown = false;                // WO-178: the copy's countdown to its blow is ours (counted down every frame, -1 at the end)
    double hitAt = 0;                      // WO-178: the row's own hit time
    float hitDist = 0, alignMax = 0;       // WO-178: the row's own reach (hit_distance, the alignment maximum; 0 = unknown)
    float engineReach = -1;                // WO-178: the engine's reach for the copy's weapon (-1 = not read)
    bool canHitWrote = false, canHit = false;   // WO-178: the copy's CanHitByAttack is ours for this window (the green shield's condition)
    uint32_t canHitLost = 0;               // WO-178: ticks it read back other than what we wrote (the engine's own update overwrote it)
    bool targetSet = false;                // WO-178: the copy's combat target is the local player (the engine decides CanHitByAttack itself)
    bool sawCanHit = false;                // WO-178: the engine's own CanHitByAttack read yes in this window
    int planned = 0;                       // WO-178: the defender's slots this wind-up planned the engine's way (perfect block among them)
    int pbStage = 0;                       // WO-178: 1 = perfect-blocked (0x10, OpponentHit), 2 = 0x20; the window holds to pbAt + kPbHoldS
    double pbAt = 0, windupAt = 0;
    bool pbPending = false, pbReStruck = false;   // WO-185 (mp_pb_at_contact): the player's perfect block began; the copy's 0x10 step waits for the blow's contact
    double pbPendingAt = 0;
    double pbStage2S = wo166rules::kPbStage2S;    // 0x10 -> 0x20 after this (the press-time step 0.5; the contact step 0.42)
    uint8_t weapon = 0;                           // WO-187 B3: the row's weapon class (wo187rules::Weapon; 0 = a 0.60.3 agent: unknown)
    bool oppHitSet = false;
    unsigned n = 0;
};

std::vector<Strike> g_strikes;            // main thread only
std::atomic<bool> g_on{true};             // mp_copy_strikes (the agent's switch)
std::atomic<bool> g_faulted{false};       // switched off for the session by a fault
std::atomic<uint8_t> g_autoMode{kAutoAllOff};
std::atomic<uint32_t> c_requests{0}, c_queued{0}, c_wrote{0}, c_refusedState{0}, c_refusedModel{0}, c_refusedFields{0}, c_missed{0},
    c_restored{0}, c_engineMovedOn{0}, c_midStriking{0}, c_midOther{0}, c_playerOpp{0}, c_faults{0}, c_localDropped{0}, c_localDroppedStriking{0},
    c_merged{0}, c_figureWaited{0};
unsigned g_windowN = 0;
std::string g_faultSite;

// ---- WO-178: the engine's own State setter (C_CombatActorModel's state property: stores and notifies its listeners -- among them the
// actor's own state handler, registered in StartCombatMode, which at wind-up (4) opens the OPPONENT's perfect-block slot with the attack
// zone, and on every change tells the opponent). A raw write (0.48.2's C3) never notified anyone: no shield, no block-in-place, no prompts.
// Found in C_CombatActorActionAttack::EnterImpl (its label): `mov edx,4; mov rcx,[rax+2F0h]; add rcx,40h; call <setter>`.
constexpr char kAttackEnterLabel[] = "wh::combatmodule::C_CombatActorActionAttack::EnterImpl";
constexpr uint8_t kSetStateCall[16] = {0xBA, 0x04, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x88, 0xF0, 0x02, 0x00, 0x00, 0x48, 0x83, 0xC1, 0x40};
constexpr uint8_t kSetStatePrologue[15] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x30};
constexpr int32_t kStateWindup = 4;
using SetStateFn = void(__fastcall*)(void* prop, int32_t v);
SetStateFn g_setState = nullptr;
bool g_setStateResolved = false;
std::atomic<uint32_t> c_engineWindup{0}, c_engineStrike{0}, c_engineRestore{0}, c_engineOppPlayer{0}, c_engineOppOther{0};

bool code_bytes(const void* src, void* dst, size_t n) {
    KCDMP_FAULT_READ(site, "wo166::code");
    return fault::guarded(site, [&] { std::memcpy(dst, src, n); });
}

bool resolve_set_state() {
    if (g_setStateResolved) return g_setState != nullptr;
    g_setStateResolved = true;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    int n = 0;
    const uint8_t* fn = cm ? anchor::function_by_string(cm, kAttackEnterLabel, &n) : nullptr;
    const uint8_t e8[1] = {0xE8};
    const uint8_t* at = fn ? anchor::function_find_sequence(cm, fn, kSetStateCall, sizeof kSetStateCall, e8, 1, sizeof kSetStateCall + 1) : nullptr;
    uint8_t call[5]{};
    if (!at || !code_bytes(at + sizeof kSetStateCall, call, 5) || call[0] != 0xE8) {
        logf("WO178-COPYSTATE not armed: the attack action's state call is not where it was read -- copies' swings write the state raw (0.48.2)");
        return false;
    }
    int32_t rel = 0;
    std::memcpy(&rel, call + 1, 4);
    const uint8_t* target = at + sizeof kSetStateCall + 5 + rel;
    uint8_t pro[sizeof kSetStatePrologue]{};
    anchor::Range text{};
    if (!anchor::section(cm, ".text", &text) || !text.contains(target) || !code_bytes(target, pro, sizeof pro) ||
        std::memcmp(pro, kSetStatePrologue, sizeof pro) != 0) {
        logf("WO178-COPYSTATE not armed: the state setter's prologue is not the one read (another build)");
        return false;
    }
    g_setState = reinterpret_cast<SetStateFn>(const_cast<uint8_t*>(target));
    char d[64];
    anchor::describe(target, d, sizeof d);
    logf("WO178-COPYSTATE armed: the model's state setter=%s (C_CombatActorActionAttack::EnterImpl's own call, prologue checked) -- a copy's "
         "swing winds up and strikes the engine's way", d);
    return true;
}

// ---- WO-178: the attacker's TargetReachableByAttack (the named bool block at model +0x780). The defender's slot opens only while the
// attacker's opponent is him AND (RiposteState or TargetReachableByAttack) -- the slot trigger's own update; a real attack sets it from its
// action at entry (C_CombatActorActionAttack::EnterImpl: `mov rcx,[rax+2F0h]; add rcx,780h; call <bool setter>`), a copy never did.
constexpr uint8_t kReachCall[14] = {0x48, 0x8B, 0x88, 0xF0, 0x02, 0x00, 0x00, 0x48, 0x81, 0xC1, 0x80, 0x07, 0x00, 0x00};
constexpr uint8_t kBoolSetterPrologue[13] = {0x48, 0x89, 0x5C, 0x24, 0x18, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0xE9};
constexpr size_t kReachableOff = 0x780;
using SetBoolFn = void(__fastcall*)(void* prop, bool v);
SetBoolFn g_setBool = nullptr;
bool g_setBoolResolved = false;
std::atomic<uint32_t> c_reachYes{0}, c_reachNo{0};

bool resolve_set_bool() {
    if (g_setBoolResolved) return g_setBool != nullptr;
    g_setBoolResolved = true;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    int n = 0;
    const uint8_t* fn = cm ? anchor::function_by_string(cm, kAttackEnterLabel, &n) : nullptr;
    const uint8_t e8[1] = {0xE8};
    const uint8_t* at = fn ? anchor::function_find_sequence(cm, fn, kReachCall, sizeof kReachCall, e8, 1, sizeof kReachCall + 1) : nullptr;
    uint8_t call[5]{};
    if (!at || !code_bytes(at + sizeof kReachCall, call, 5) || call[0] != 0xE8) { logf("WO178-REACHABLE not armed: the attack action's reach call is not where it was read"); return false; }
    int32_t rel = 0;
    std::memcpy(&rel, call + 1, 4);
    const uint8_t* target = at + sizeof kReachCall + 5 + rel;
    uint8_t pro[sizeof kBoolSetterPrologue]{};
    anchor::Range text{};
    if (!anchor::section(cm, ".text", &text) || !text.contains(target) || !code_bytes(target, pro, sizeof pro) ||
        std::memcmp(pro, kBoolSetterPrologue, sizeof pro) != 0) { logf("WO178-REACHABLE not armed: the bool setter's prologue is not the one read"); return false; }
    g_setBool = reinterpret_cast<SetBoolFn>(const_cast<uint8_t*>(target));
    char d[64];
    anchor::describe(target, d, sizeof d);
    logf("WO178-REACHABLE armed: the bool setter=%s (C_CombatActorActionAttack::EnterImpl's own TargetReachableByAttack call)", d);
    return true;
}

bool reachable_block_named(void* model) {
    void* s = nullptr;
    char nm[28]{};
    KCDMP_FAULT_READ(site, "wo166::reachable_name");
    return model && fault::guarded(site, [&] {
               s = *reinterpret_cast<void**>(static_cast<char*>(model) + kReachableOff + 0x30);
               if (s) std::memcpy(nm, s, 24);
           }) && s && std::memcmp(nm, "TargetReachableByAttack", 24) == 0;
}

bool set_reachable(void* model, bool v) {
    if (!resolve_set_bool() || !reachable_block_named(model)) return false;
    KCDMP_FAULT_CALL(site, "wo166::set_reachable");
    return fault::guarded(site, [&] { g_setBool(static_cast<char*>(model) + kReachableOff, v); });
}

// ---- WO-178: the attacker's CanHitByAttack (the named bool block at model +0x640) and the engine's reach. The defender's PERFECT-block
// slot (the green shield) opens only while the attacker's CanHitByAttack (or its RiposteState) holds -- C_CombatTriggerPerfectBlock's
// update; the plain block slot asks TargetReachableByAttack instead (the live run: BlockSlot opened, PerfectBlockSlot never did). The
// engine sets it every frame in the actor's update (its "can hit" step: an attack action's own answer, else its reach for the weapon
// against the distance to its target). A copy has no attack action: ours, from the engine's reach. Found port-aware: the one function
// called by the actor update ("Combat:UpdateActor") that writes CanHitByAttack (add rcx,640h) and CanAlmostHitByAttack (add rcx,680h)
// and calls the reach as `mov rcx,rdi; movaps xmm7,xmm0; call <reach>`; the reach's prologue and its weapon read (+0x4D8) checked.
constexpr char kUpdateActorLabel[] = "Combat:UpdateActor";
constexpr uint8_t kCanHitAdd[7] = {0x48, 0x81, 0xC1, 0x40, 0x06, 0x00, 0x00};
constexpr uint8_t kCanAlmostAdd[7] = {0x48, 0x81, 0xC1, 0x80, 0x06, 0x00, 0x00};
constexpr uint8_t kReachLead[6] = {0x48, 0x8B, 0xCF, 0x0F, 0x28, 0xF8};
constexpr uint8_t kReachPrologue[13] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xF9};
constexpr uint8_t kReachWeaponRead[7] = {0x48, 0x8B, 0x9F, 0xD8, 0x04, 0x00, 0x00};
constexpr size_t kCanHitOff = 0x640;
constexpr int32_t kStateGuardIdle = 2;   // the reach's guard step smooths a cache per call in this state: never called there
using ReachFn = float(__fastcall*)(void* ca);
ReachFn g_reach = nullptr;
// the reach's own weapon lookup: `mov rbx,[rdi+4D8h]; xor edx,edx; lea rcx,[rbx+18h]; call <weapon class by hand slot>` -- the class's
// length at +0x44 (the reach's "length + 0.4 m"), its class reach at +0x4C; a shield (class 0xC at +8) in slot 0 means slot 1
constexpr uint8_t kWeaponLookup[11] = {0x48, 0x8B, 0x9F, 0xD8, 0x04, 0x00, 0x00, 0x33, 0xD2, 0x48, 0x8D};
using WeaponFn = void*(__fastcall*)(void* set, int32_t slot);
WeaponFn g_weapon = nullptr;
bool g_reachResolved = false;
std::atomic<uint32_t> c_canHitYes{0}, c_canHitNo{0}, c_canHitLost{0}, c_reachRead{0};

bool resolve_reach() {
    if (g_reachResolved) return g_reach != nullptr;
    g_reachResolved = true;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    int n = 0;
    const uint8_t* upd = cm ? anchor::function_by_string(cm, kUpdateActorLabel, &n) : nullptr;
    const uint8_t* targets[96]{};
    const int nt = upd ? anchor::function_call_targets(cm, upd, targets, 96) : 0;
    const uint8_t* canHitFn = nullptr;
    int matches = 0;
    for (int i = 0; i < nt; ++i)
        if (anchor::function_has_bytes(cm, targets[i], kCanHitAdd, sizeof kCanHitAdd) && anchor::function_has_bytes(cm, targets[i], kCanAlmostAdd, sizeof kCanAlmostAdd) &&
            anchor::function_has_bytes(cm, targets[i], kReachLead, sizeof kReachLead)) { canHitFn = targets[i]; ++matches; }
    if (matches != 1) { logf("WO178-CANHIT not armed: the actor update's can-hit step not found (%d candidates) -- the row's reach as before", matches); return false; }
    const uint8_t e8[1] = {0xE8};
    const uint8_t* at = anchor::function_find_sequence(cm, canHitFn, kReachLead, sizeof kReachLead, e8, 1, sizeof kReachLead + 1);
    uint8_t call[5]{};
    if (!at || !code_bytes(at + sizeof kReachLead, call, 5) || call[0] != 0xE8) { logf("WO178-CANHIT not armed: the reach call is not where it was read"); return false; }
    int32_t rel = 0;
    std::memcpy(&rel, call + 1, 4);
    const uint8_t* target = at + sizeof kReachLead + 5 + rel;
    uint8_t pro[sizeof kReachPrologue]{};
    anchor::Range text{};
    if (!anchor::section(cm, ".text", &text) || !text.contains(target) || !code_bytes(target, pro, sizeof pro) ||
        std::memcmp(pro, kReachPrologue, sizeof pro) != 0 || !anchor::function_has_bytes(cm, target, kReachWeaponRead, sizeof kReachWeaponRead)) {
        logf("WO178-CANHIT not armed: the reach's prologue or weapon read is not the one read (another build)");
        return false;
    }
    g_reach = reinterpret_cast<ReachFn>(const_cast<uint8_t*>(target));
    uint8_t wl[sizeof kWeaponLookup + 7]{};
    if (code_bytes(target + 0x19, wl, sizeof wl) && std::memcmp(wl, kWeaponLookup, sizeof kWeaponLookup) == 0 && wl[11] == 0x4B && wl[12] == 0x18 && wl[13] == 0xE8) {
        int32_t wrel = 0;
        std::memcpy(&wrel, wl + 14, 4);
        const uint8_t* wfn = target + 0x19 + 18 + wrel;
        if (text.contains(wfn)) g_weapon = reinterpret_cast<WeaponFn>(const_cast<uint8_t*>(wfn));
    }
    char d[64], d2[64];
    anchor::describe(target, d, sizeof d);
    anchor::describe(canHitFn, d2, sizeof d2);
    logf("WO178-CANHIT armed: the engine's reach=%s (called by the can-hit step %s) -- a copy's wind-up raises CanHitByAttack within its weapon's reach", d, d2);
    return true;
}

// The engine's reach for this combat actor's weapon (main thread), -1 when unreadable or in the guard state (see kStateGuardIdle).
float engine_reach(void* ca, void* model) {
    int32_t st = 0;
    if (!ca || !resolve_reach() || !motion::read_state(model, &st) || st == kStateGuardIdle) return -1.0f;
    float r = -1.0f;
    KCDMP_FAULT_CALL(site, "wo166::engine_reach");
    if (!fault::guarded(site, [&] { r = g_reach(ca); })) return -1.0f;
    c_reachRead.fetch_add(1);
    return wo178rules::engine_reach_ok(r) ? r : -1.0f;
}

// The weapon's own length and class reach (the engine's weapon class data), false when unreadable.
bool weapon_dims(void* ca, float* len, float* classReach) {
    if (!ca || !resolve_reach() || !g_weapon) return false;
    void* set = nullptr;
    KCDMP_FAULT_CALL(site, "wo166::weapon_dims");
    return fault::guarded(site, [&] {
        set = *reinterpret_cast<void**>(static_cast<char*>(ca) + 0x4D8);
        if (!set) return;
        void* w = g_weapon(static_cast<char*>(set) + 0x18, 0);
        if (w && *reinterpret_cast<int32_t*>(static_cast<char*>(w) + 8) == 0xC) w = g_weapon(static_cast<char*>(set) + 0x18, 1);
        if (!w) { set = nullptr; return; }
        *len = *reinterpret_cast<float*>(static_cast<char*>(w) + 0x44);
        *classReach = *reinterpret_cast<float*>(static_cast<char*>(w) + 0x4C);
    }) && set != nullptr;
}

// ---- WO-178: the copy's combat TARGET. A copy fights with an Opponent (WO-132's engage) but no target (model +0x1108): the engine's own
// can-hit step, every frame in the actor update, answers "no" without one -- it overwrote ours each frame in the live run, and the green
// shield never opened. A real fighter's target is set by C_CombatActorOpponentManager::SetTarget(entity) (its label; the manager at
// combat actor +0x4A8, whose own +8 is the actor -- checked); with it, the engine compares its reach for the copy's weapon with the
// distance every frame, as for any NPC.
constexpr char kOppSetTargetLabel[] = "wh::combatmodule::C_CombatActorOpponentManager::SetTarget";
constexpr uint8_t kOppSetTargetPrologue[17] = {0x40, 0x55, 0x56, 0x57, 0x48, 0x81, 0xEC, 0x80, 0x00, 0x00, 0x00, 0x48, 0x8B, 0xF1, 0x48, 0x8B, 0xFA};
constexpr size_t kOppManagerOff = 0x4A8, kTargetOff = 0x1108;
using OppSetTargetFn = void(__fastcall*)(void* mgr, void* entity);
OppSetTargetFn g_oppSetTarget = nullptr;
bool g_oppSetTargetResolved = false;
std::atomic<uint32_t> c_targetSet{0}, c_targetHad{0}, c_targetFailed{0}, c_engineCanHit{0};

bool resolve_opp_set_target() {
    if (g_oppSetTargetResolved) return g_oppSetTarget != nullptr;
    g_oppSetTargetResolved = true;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    int n = 0;
    const uint8_t* fn = cm ? anchor::function_by_string(cm, kOppSetTargetLabel, &n) : nullptr;
    uint8_t pro[sizeof kOppSetTargetPrologue]{};
    if (!fn || n != 1 || !code_bytes(fn, pro, sizeof pro) || std::memcmp(pro, kOppSetTargetPrologue, sizeof pro) != 0) {
        logf("WO178-TARGET not armed: the opponent manager's SetTarget not found or not the one read (%d) -- CanHitByAttack written by the mod", n);
        return false;
    }
    g_oppSetTarget = reinterpret_cast<OppSetTargetFn>(const_cast<uint8_t*>(fn));
    char d[64];
    anchor::describe(fn, d, sizeof d);
    logf("WO178-TARGET armed: C_CombatActorOpponentManager::SetTarget=%s -- a copy fighting the local player gets him as its combat target", d);
    return true;
}

// The copy's combat target is the local player's entity (set once by the engine's own call when it is not); false when it could not be.
bool ensure_target_player(void* ca, void* model, bool* hadIt) {
    *hadIt = false;
    void* pEnt = engine::entity_by_id(0x7777);
    void* mgr = nullptr; void* back = nullptr; void* tgt = nullptr;
    KCDMP_FAULT_READ(rsite, "wo166::target_read");
    if (!pEnt || !ca || !model || !fault::guarded(rsite, [&] {
            mgr = *reinterpret_cast<void**>(static_cast<char*>(ca) + kOppManagerOff);
            if (mgr) back = *reinterpret_cast<void**>(static_cast<char*>(mgr) + 8);
            tgt = *reinterpret_cast<void**>(static_cast<char*>(model) + kTargetOff);
        }) || !mgr || back != ca) return false;
    if (tgt) {
        // the target holder names its entity through its vftable's +8 (the engine's own read in SetTarget)
        void* te = nullptr;
        KCDMP_FAULT_CALL(tsite, "wo166::target_entity");
        if (fault::guarded(tsite, [&] { te = (*reinterpret_cast<void* (__fastcall***)(void*)>(tgt))[1](tgt); }) && te == pEnt) { *hadIt = true; return true; }
    }
    // WO-187 A2 (mp_opp_logical): the engine's own SetLogicalTarget -- the requested target as well, so the opponent manager's update keeps it
    if (wo187::opp_logical_on()) return wo187::ensure_target(ca, model, pEnt, hadIt);
    if (!resolve_opp_set_target()) return false;
    KCDMP_FAULT_CALL(site, "wo166::set_target");
    if (!fault::guarded(site, [&] { g_oppSetTarget(mgr, pEnt); })) return false;
    void* now = nullptr;
    return fault::guarded(rsite, [&] { now = *reinterpret_cast<void**>(static_cast<char*>(model) + kTargetOff); }) && now != nullptr;
}

// ---- WO-178: the defender's PERFECT-block window is planned by the ATTACKER. C_CombatActorActionAttack::EnterImpl asks the attacker's
// C_CombatActorSlotPlanner (combat actor +0x4D0) to plan the opponent's slot triggers by type -- `mov edx,<type>; mov rbx,[rax+4D0h];
// mov rcx,rbx; call <PlanSlot>`, then the next types with `mov edx,<type>; mov rcx,rbx; call <PlanSlot>` (4, 0xA, 8 in 1.5.5). PlanSlot
// times each window from the attacker's countdown (+0x5C0, ours since this WO) and the planner's own update opens it on the defender.
// A copy has no attack action: its wind-up planned nothing, so only the state handler's plain block trigger ever opened (the live
// trace: C_CombatTriggerBlock started, C_CombatTriggerPerfectBlock never). The types and the planner offset are read from the code.
constexpr uint8_t kPlanLead[1] = {0xBA};
constexpr uint8_t kPlanTail[9] = {0x48, 0x8B, 0x98, 0xD0, 0x04, 0x00, 0x00, 0x48, 0x8B};   // mov rbx,[rax+4D0h]; mov rcx,rbx (48 8B CB)
constexpr uint8_t kPlanPrologue[18] = {0x48, 0x89, 0x5C, 0x24, 0x10, 0x57, 0x48, 0x81, 0xEC, 0x80, 0x00, 0x00, 0x00, 0x48, 0x8B, 0xD9, 0x8B, 0xFA};
using PlanSlotFn = void(__fastcall*)(void* planner, int32_t type);
PlanSlotFn g_planSlot = nullptr;
bool g_planResolved = false;
int32_t g_planTypes[4]{};
int g_planTypeN = 0;
uint32_t g_plannerOff = 0;
std::atomic<uint32_t> c_planned{0}, c_planRefused{0};

bool resolve_plan_slot() {
    if (g_planResolved) return g_planSlot != nullptr;
    g_planResolved = true;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    int n = 0;
    const uint8_t* fn = cm ? anchor::function_by_string(cm, kAttackEnterLabel, &n) : nullptr;
    const uint8_t* at = fn ? anchor::function_find_sequence(cm, fn, kPlanLead, 1, kPlanTail, sizeof kPlanTail, 4 + sizeof kPlanTail) : nullptr;
    uint8_t b[64]{};
    anchor::Range text{};
    if (!at || !code_bytes(at, b, sizeof b) || b[5] != 0x48 || b[14] != 0xCB || b[15] != 0xE8 || !anchor::section(cm, ".text", &text)) {
        logf("WO178-PLAN not armed: the attack action's slot planning is not where it was read -- no perfect-block window for copies");
        return false;
    }
    std::memcpy(&g_plannerOff, b + 8, 4);
    int32_t rel = 0;
    std::memcpy(&rel, b + 16, 4);
    const uint8_t* target = at + 20 + rel;
    uint8_t pro[sizeof kPlanPrologue]{};
    if (!text.contains(target) || !code_bytes(target, pro, sizeof pro) || std::memcmp(pro, kPlanPrologue, sizeof pro) != 0 || g_plannerOff == 0 || g_plannerOff > 0x2000) {
        logf("WO178-PLAN not armed: PlanSlot's prologue is not the one read (another build)");
        return false;
    }
    std::memcpy(&g_planTypes[0], b + 1, 4);
    g_planTypeN = 1;
    // the next types: BA imm32; 48 8B CB; E8 rel32 -> the same PlanSlot
    for (size_t o = 20; o + 13 <= sizeof b && g_planTypeN < 4; o += 13) {
        if (b[o] != 0xBA || b[o + 5] != 0x48 || b[o + 6] != 0x8B || b[o + 7] != 0xCB || b[o + 8] != 0xE8) break;
        int32_t r2 = 0;
        std::memcpy(&r2, b + o + 9, 4);
        if (at + o + 13 + r2 != target) break;
        std::memcpy(&g_planTypes[g_planTypeN++], b + o + 1, 4);
    }
    for (int i = 0; i < g_planTypeN; ++i) if (g_planTypes[i] < 0 || g_planTypes[i] > 64) { logf("WO178-PLAN not armed: an implausible slot type"); return false; }
    g_planSlot = reinterpret_cast<PlanSlotFn>(const_cast<uint8_t*>(target));
    char d[64];
    anchor::describe(target, d, sizeof d);
    logf("WO178-PLAN armed: the slot planner's PlanSlot=%s at combat actor +0x%X, types %d%s%d%s%d (C_CombatActorActionAttack::EnterImpl's own) -- a copy's "
         "wind-up plans the defender's perfect-block window the engine's way", d, g_plannerOff, g_planTypes[0], g_planTypeN > 1 ? "," : "",
         g_planTypeN > 1 ? g_planTypes[1] : 0, g_planTypeN > 2 ? "," : "", g_planTypeN > 2 ? g_planTypes[2] : 0);
    return true;
}

// The attack's own slot planning for this copy (main thread, after its countdown and State 4): the number of types planned.
int plan_defender_slots(void* ca) {
    if (!ca || !resolve_plan_slot()) return 0;
    void* planner = nullptr; void* back = nullptr;
    KCDMP_FAULT_READ(rsite, "wo166::planner_read");
    if (!fault::guarded(rsite, [&] {
            planner = *reinterpret_cast<void**>(static_cast<char*>(ca) + g_plannerOff);
            if (planner) back = *reinterpret_cast<void**>(static_cast<char*>(planner) + 8);
        }) || !planner || back != ca) { c_planRefused.fetch_add(1); return 0; }
    int done = 0;
    KCDMP_FAULT_CALL(site, "wo166::plan_slot");
    for (int i = 0; i < g_planTypeN; ++i)
        if (fault::guarded(site, [&] { g_planSlot(planner, g_planTypes[i]); })) ++done;
    c_planned.fetch_add(done);
    return done;
}

// ---- WO-178: the attacker's OpponentHit (the named bool block at model +0x600): set by a real attack when its blow met the defender;
// the defender's riposte asks it (with the attacker's 0x10) -- the same bool setter as TargetReachableByAttack, the block's name checked.
constexpr size_t kOpponentHitOff = 0x600;
std::atomic<uint32_t> c_pbHits{0}, c_pbHitRefused{0};

bool set_opponent_hit(void* model, bool v) {
    void* s = nullptr;
    char nm[12]{};
    KCDMP_FAULT_READ(rsite, "wo166::opphit_name");
    if (!model || !resolve_set_bool() || !fault::guarded(rsite, [&] {
            s = *reinterpret_cast<void**>(static_cast<char*>(model) + kOpponentHitOff + 0x30);
            if (s) std::memcpy(nm, s, 12);
        }) || !s || std::memcmp(nm, "OpponentHit", 12) != 0) return false;
    KCDMP_FAULT_CALL(site, "wo166::set_opponent_hit");
    return fault::guarded(site, [&] { g_setBool(static_cast<char*>(model) + kOpponentHitOff, v); });
}

bool can_hit_block_named(void* model) {
    void* s = nullptr;
    char nm[16]{};
    KCDMP_FAULT_READ(site, "wo166::canhit_name");
    return model && fault::guarded(site, [&] {
               s = *reinterpret_cast<void**>(static_cast<char*>(model) + kCanHitOff + 0x30);
               if (s) std::memcpy(nm, s, 15);
           }) && s && std::memcmp(nm, "CanHitByAttack", 15) == 0;
}

bool read_can_hit(void* model, bool* v) {
    uint8_t b = 0;
    KCDMP_FAULT_READ(site, "wo166::canhit_read");
    if (!fault::guarded(site, [&] { b = *reinterpret_cast<const uint8_t*>(static_cast<const char*>(model) + kCanHitOff + 8); })) return false;
    *v = b != 0;
    return true;
}

bool set_can_hit(void* model, bool v) {
    if (!resolve_set_bool() || !can_hit_block_named(model)) return false;
    KCDMP_FAULT_CALL(site, "wo166::set_can_hit");
    return fault::guarded(site, [&] { g_setBool(static_cast<char*>(model) + kCanHitOff, v); });
}

float planar_to_player(uint32_t eid) {
    void* a = engine::entity_by_id(eid);
    void* p = engine::entity_by_id(0x7777);
    float pa[3]{}, pp[3]{};
    if (!a || !p || !engine::entity_world_pos(a, pa) || !engine::entity_world_pos(p, pp)) return -1.0f;
    return std::sqrt((pa[0] - pp[0]) * (pa[0] - pp[0]) + (pa[1] - pp[1]) * (pa[1] - pp[1]));
}

// The state through the engine's setter (main thread, the frame the model is read; the State block named first by read_state).
bool set_state_engine(void* model, int32_t v) {
    int32_t cur = 0;
    if (!g_setState || !motion::read_state(model, &cur)) return false;
    KCDMP_FAULT_CALL(site, "wo166::set_state_engine");
    return fault::guarded(site, [&] { g_setState(static_cast<char*>(model) + 0x40, v); });
}

// ---- WO-178: the attacker's countdown to its blow (an unnamed float block at model +0x5C0, the value at +8: -1 at rest, ~0.75 s when a
// real attack starts, falling every frame -- the duel trace). The defender's window is sized from it at the wind-up (min(window, max(0,
// countdown - tuning))), so a copy's 0 left the shield's window empty. The block has no name: its neighbour at +0x600 must name itself
// "OpponentHit" (the layout read this WO) or nothing is written.
constexpr size_t kCountdownOff = 0x5C0, kCountdownNeighbourOff = 0x600;
bool countdown_layout_ok(void* model) {
    void* s = nullptr;
    char nm[16]{};
    KCDMP_FAULT_READ(site, "wo166::countdown_layout");
    return model && fault::guarded(site, [&] {
               s = *reinterpret_cast<void**>(static_cast<char*>(model) + kCountdownNeighbourOff + 0x30);
               if (s) std::memcpy(nm, s, 12);
           }) && s && std::memcmp(nm, "OpponentHit", 12) == 0;
}
bool write_countdown(void* model, float v) {
    if (!countdown_layout_ok(model)) return false;
    KCDMP_FAULT_READ(site, "wo166::countdown");
    return fault::guarded(site, [&] { *reinterpret_cast<float*>(static_cast<char*>(model) + kCountdownOff + 8) = v; });
}

// The copy's opponent is the local player (its model's Opponent; the slot opens on the opponent).
bool copy_opp_is_player(void* model) {
    void* pca = motion::player_combat_actor();
    void* opp = nullptr;
    KCDMP_FAULT_READ(site, "wo166::copy_opp");
    if (!model || !pca || !fault::guarded(site, [&] { opp = *reinterpret_cast<void**>(static_cast<char*>(model) + 0x1118); }) || !opp) return false;
    return opp == pca || static_cast<char*>(opp) - 8 == pca;
}

uint32_t player_eid() {
    void* e = engine::entity_by_id(0x7777);
    return e ? engine::entity_id(e) : 0;
}

// WO-207 crash work (mp_plan_in_fight, on): a copy plans the local player's defence windows (WO-178's PlanSlot) only while the engine
// has the copy and the player in a skirmish fight (the skirmish manager's own relation test, read-only, this frame). Planned, the copy's
// slot planner creates sub-triggers on the player's multi-triggers from the copy's own update; the player's update deletes every
// sub-trigger not yet started. A copy the joiner's world had let go of (WO-132 released it; WO-187 still wished it on the player) did
// that from an update the engine does not keep apart from the player's: the player's dodge / perfect-block sub-trigger was deleted
// between its creation and its Start (CombatModule+0x55b065 / +0x559d32 / +0x8ff0a, the joiner's fight crash). Off (or
// KCDMP_TEST_W207_OFF naming plan_in_fight in the game's own environment, the bench's twin): planned whenever its opponent is the player.
std::atomic<int> g_planInFight{-1};   // -1: not read yet (the twin's environment decides the start)
std::atomic<uint32_t> c_planOutOfFight{0}, c_planUnanswered{0};
bool plan_in_fight_on() {
    int k = g_planInFight.load();
    if (k < 0) {
        char b[256]{};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W207_OFF", b, sizeof b);
        k = (n > 0 && n < sizeof b && std::strstr(b, "plan_in_fight")) ? 0 : 1;
        int expect = -1;
        if (g_planInFight.compare_exchange_strong(expect, k)) logf("WO207-PLAN-IN-FIGHT %s at start%s", k ? "on" : "off", k ? "" : " (KCDMP_TEST_W207_OFF: the twin)");
        k = g_planInFight.load();
    }
    return k == 1;
}
// true: plan. The copy's soul and the player's, looked up this frame; an unanswered relation plans nothing (fail closed).
bool plan_allowed(uint32_t eid, const char* name) {
    if (!plan_in_fight_on()) return true;
    const uint32_t pe = player_eid();
    void* cs = eid ? hits::soul_of_eid(eid) : nullptr;
    void* ps = pe ? hits::soul_of_eid(pe) : nullptr;
    bool answered = false, hostile = false;
    const char* why = "no soul";
    if (cs && ps) hits::skirmish_hostile(cs, ps, &answered, &hostile, &why);
    if (answered && hostile) return true;
    const uint32_t k = (answered ? c_planOutOfFight : c_planUnanswered).fetch_add(1) + 1;
    if (k <= 20 || k % 100 == 0)
        logf("WO207-PLAN-IN-FIGHT npc=%s %s -- no defence window planned on the local player [n=%u]", name ? name : "?",
             answered ? "is not in a skirmish fight with the local player (the engine's relation test)" : why, k);
    return false;
}

bool name_ok(const char* s, size_t n) {
    if (n == 0 || n > 63) return false;
    for (size_t i = 0; i < n; ++i) {
        const char c = s[i];
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.')) return false;
    }
    return true;
}

bool other_live_strike(uint32_t eid, const Strike* self) {
    for (const auto& s : g_strikes) if (&s != self && s.eid == eid && !s.done) return true;
    return false;
}

void switch_off(const char* site) {
    if (g_faulted.exchange(true)) return;
    g_faultSite = site;
    c_faults.fetch_add(1);
    logf("WO166-STRIKE switched OFF for the session -- a write failed at %s (the copies play their swings as before; the host decides as before)", site);
}

// WO-187 B3: the 0x10 -> 0x20 length for this swing's weapon -- the measured value where one exists, the fallback (said once per weapon) elsewhere.
uint32_t g_stage2Said = 0;
double stage2_for(const Strike& s) {
    bool measured = false;
    const auto w = wo187rules::weapon_from_byte(s.weapon);
    const double v = wo187rules::pb_stage2(w, s.fields.type, s.fields.zone, &measured);
    const uint32_t bit = 1u << static_cast<unsigned>(w);
    if (!measured && !(g_stage2Said & bit)) {
        g_stage2Said |= bit;
        logf("WO187-TIMING weapon=%s type=%d zone=%d: no measured perfect-blocked length for this weapon -- the copy holds 0x10 for %.2f s "
             "(the fallback; a vanilla trace of this weapon measures it)", wo187rules::weapon_name(w), s.fields.type, s.fields.zone, v);
    }
    return v;
}

// WO-190 A6 (mp_copy_swing_takeover): the host's next swing for a copy still in its last one -- the withdraw (0x20) or 0x40 its previous
// swing or reaction row left -- ends that leftover through the engine's own state setter (Guard), so the new swing winds up (and with it
// the joiner's shield window). Never over a real engine reaction to a blow on the copy itself: its director runs a Hit action, or the
// local player hit it within the last 1.2 s. The 0.60.7 joiner: 15 of the guards' 34 swings refused, 2 shield windows in 30 attacks.
std::atomic<uint32_t> c_takeovers{0}, c_takeoverKept{0}, c_takeoverFailed{0};
bool take_over_leftover(const Strike& s, void* ca, void* model, int32_t* cur) {
    if (*cur != wo190rules::kStateWithdraw && *cur != wo190rules::kStateHitState) return false;
    if (!wo190::swing_takeover_on()) return false;
    // WO-197 B5: the copy's actual main action (B0's reader); a real reaction or a paired move under way is kept
    const std::string dir = wo187::director_action(ca);
    void* soul = hits::soul_of_eid(s.eid);
    const bool hitRecently = soul && hits::hit_by_player(soul, wo190rules::kRealHitS);
    const bool reaction = wo190rules::director_is_reaction(dir.c_str()) || (wo197::guard_engine_on() && wo197rules::keep_main_action(dir.c_str()));
    if (!wo190rules::leftover_endable(true, *cur, reaction, hitRecently)) {
        if (c_takeoverKept.fetch_add(1) < 20)
            logf("WO190-TAKEOVER npc=%s kept: the copy reads 0x%X and it is a real reaction (director=%s, hit by the player within %.1f s=%d) -- never cut off",
                 s.name.c_str(), static_cast<unsigned>(*cur), dir.empty() ? "?" : dir.c_str(), wo190rules::kRealHitS, hitRecently ? 1 : 0);
        return false;
    }
    const int32_t was = *cur;
    // WO-197 B5: a main action that is not a plain Guard ends the engine's way first (EnsureGuardAction: scope 0 terminated and a new Guard in
    // the same call -- never a bare TerminateAction(0), whose empty main scope ends combat mode); the leftover State then goes back to Guard
    int ens = -2;
    if (wo197::guard_engine_on() && !dir.empty() && wo197rules::needs_guard(dir.c_str())) ens = wo197::ensure_guard(ca);
    const bool wrote = g_setState ? set_state_engine(model, kStateGuard) : motion::write_state(model, kStateGuard);
    int32_t now = 0;
    if (!wrote || !motion::read_state(model, &now) || !strike_state_ok(now)) {
        if (c_takeoverFailed.fetch_add(1) < 20)
            logf("WO190-TAKEOVER npc=%s the leftover 0x%X did not end (wrote=%d now=0x%X) -- refused as before", s.name.c_str(), static_cast<unsigned>(was), wrote ? 1 : 0,
                 static_cast<unsigned>(now));
        return false;
    }
    *cur = now;
    const uint32_t n = c_takeovers.fetch_add(1) + 1;
    if (n <= kLogFirst || n % 50 == 0)
        logf("WO190-TAKEOVER npc=%s the copy still read 0x%X from its last swing (director=%s%s) -> Guard through the %s; the new swing winds up (#%u)",
             s.name.c_str(), static_cast<unsigned>(was), dir.empty() ? "?" : dir.c_str(),
             ens == -2 ? "" : ens == 1 ? ", EnsureGuardAction: a Guard runs" : ens == 0 ? ", EnsureGuardAction refused" : ", EnsureGuardAction not armed",
             g_setState ? "engine's state setter" : "raw state (setter not armed)", n);
    return true;
}

// The prior state and fields back, if the model still reads ours.
void restore(Strike& s, bool closing) {
    // WO-172 C1: a body that died inside its window keeps what its death left -- no Guard/Idle written over a corpse
    if (wo172::skip_dead(s.eid, "strike")) { s.done = true; return; }
    void* ca = nullptr; void* model = nullptr;
    if (!motion::combat_parts(s.eid, false, &ca, &model)) { s.done = true; return; }
    int32_t cur = 0;
    const bool readOk = motion::read_state(model, &cur);
    if (s.engine && readOk && wo166rules::engine_state_ours(cur)) {
        if (!set_state_engine(model, s.prior)) switch_off("wo166::restore_state_engine");
        else { c_restored.fetch_add(1); c_engineRestore.fetch_add(1); }
    } else if (!s.engine && readOk && restore_ok(cur, kStateStriking)) {
        if (!motion::write_state(model, s.prior)) switch_off("wo166::restore_state");
        else c_restored.fetch_add(1);
    } else {
        c_engineMovedOn.fetch_add(1);
    }
    if (!motion::write_attack_fields(model, s.priorFields)) switch_off("wo166::restore_fields");
    if (s.countdown) write_countdown(model, -1.0f);   // WO-178: at rest again
    if (s.engine) set_reachable(model, false);
    if (s.canHitWrote) set_can_hit(model, false);
    if (s.oppHitSet) set_opponent_hit(model, false);
    if (s.targetSet && !s.sawCanHit && (s.n <= kLogFirst || s.n % 50 == 0))
        logf("WO178-CANHIT npc=%s window=%u the engine's own CanHitByAttack never read yes (out of its reach, or its can-hit step refused)", s.name.c_str(), s.n);
    s.done = true;
    if (s.n <= kLogFirst || s.n % 50 == 0)
        logf("WO166-STRIKE npc=%s window=%u end state_now=0x%X -> %s%s", s.name.c_str(), s.n, readOk ? static_cast<unsigned>(cur) : 0u,
             readOk && restore_ok(cur, kStateStriking) ? "prior state back" : "the engine moved on (left as it is)", closing ? " (session end)" : "");
}

} // namespace

uint8_t auto_mode() { return g_autoMode.load(std::memory_order_relaxed); }

float swing_reach_limit(uint32_t eid) {
    const Strike* best = nullptr;
    for (const auto& s : g_strikes) if (s.eid == eid && (!best || s.begin > best->begin)) best = &s;
    return best ? wo178rules::contact_limit(best->engineReach, best->hitDist) : wo178rules::reach_limit(0);
}

int weapon_class_id(void* ca) {
    // the weapon class of the combat actor's right hand (a shield there means the other hand), its id at the class's +8 (0xC = a shield)
    if (!ca || !resolve_reach() || !g_weapon) return -1;
    int32_t id = -1;
    KCDMP_FAULT_CALL(site, "wo166::weapon_class");
    if (!fault::guarded(site, [&] {
            void* set = *reinterpret_cast<void**>(static_cast<char*>(ca) + 0x4D8);
            if (!set) return;
            void* w = g_weapon(static_cast<char*>(set) + 0x18, 0);
            if (w && *reinterpret_cast<int32_t*>(static_cast<char*>(w) + 8) == 0xC) w = g_weapon(static_cast<char*>(set) + 0x18, 1);
            if (w) id = *reinterpret_cast<int32_t*>(static_cast<char*>(w) + 8);
        })) return -1;
    return id;
}

bool weapon_classes(void* ca, int* right, int* left) {
    // WO-190 A1: both hand slots' weapon classes through the reach's own lookup (slot 0, slot 1), the class id at +8; -1 = an empty slot
    *right = -1; *left = -1;
    if (!ca || !resolve_reach() || !g_weapon) return false;
    KCDMP_FAULT_CALL(site, "wo166::weapon_classes");
    return fault::guarded(site, [&] {
        void* set = *reinterpret_cast<void**>(static_cast<char*>(ca) + 0x4D8);
        if (!set) return;
        void* w0 = g_weapon(static_cast<char*>(set) + 0x18, 0);
        void* w1 = g_weapon(static_cast<char*>(set) + 0x18, 1);
        if (w0) *right = *reinterpret_cast<int32_t*>(static_cast<char*>(w0) + 8);
        if (w1) *left = *reinterpret_cast<int32_t*>(static_cast<char*>(w1) + 8);
    });
}

float swing_hit_distance(uint32_t eid) {
    float d = 0;
    double latest = -1;
    for (const auto& s : g_strikes) if (s.eid == eid && s.begin > latest) { latest = s.begin; d = s.hitDist; }
    return d;
}

bool striking_recently(uint32_t eid) {
    const double now = now_s();
    for (const auto& s : g_strikes)
        if (s.eid == eid && s.wrote && (!s.done || now - s.lastEnd < kRecentS)) return true;
    return false;
}

void note_local_hit_dropped(uint32_t eid, float hp, float st) {
    c_localDropped.fetch_add(1);
    const bool ours = striking_recently(eid);
    if (ours) c_localDroppedStriking.fetch_add(1);
    logf("WO166-LOCALHIT dropped attacker_eid=0x%X hp -%.2f st -%.2f striking=%d -- the local engine's blow from a copy is put back; the host decides",
         eid, hp, st, ours ? 1 : 0);
}

void tick() {
    if (g_strikes.empty()) return;
    const double now = now_s();
    const bool live = g_on.load() && !g_faulted.load();
    for (auto it = g_strikes.begin(); it != g_strikes.end();) {
        Strike& s = *it;
        if (!live && s.wrote && !s.done) { restore(s, true); s.discardUntil = now; }
        // WO-178: the wind-up from the row's start -- fields first (the state handler reads the attack zone), then State 4 by the setter
        if (s.engine && !s.windup && !s.wrote && !s.done && live && now < s.begin) {
            s.windup = true;
            void* ca = nullptr; void* model = nullptr;
            int32_t cur = 0;
            const bool parts = !wo172::skip_dead(s.eid, "strike") && motion::combat_parts(s.eid, false, &ca, &model) && motion::read_state(model, &cur);
            if (parts && !strike_state_ok(cur)) take_over_leftover(s, ca, model, &cur);   // WO-190 A6
            if (!parts || !strike_state_ok(cur)) {
                s.engine = false;   // the begin branch below decides as before
            } else {
                motion::AttackFields pf{};
                motion::read_attack_fields(model, &pf);
                if (!hits::is_discard_attacker(s.eid)) s.discardAdded = hits::discard_from(s.eid, true);
                wo187::on_windup(s.eid, s.name.c_str(), ca, model);   // WO-187 A2: the wished opponent checked (and written back) before the planning
                const bool oppMe = copy_opp_is_player(model);
                (oppMe ? c_engineOppPlayer : c_engineOppOther).fetch_add(1);
                s.countdown = write_countdown(model, static_cast<float>((std::max)(0.0, s.hitAt - now)));   // before the state: the window reads it
                const float dist = planar_to_player(s.eid);
                const bool reach = wo178rules::target_reachable(dist, s.hitDist, s.alignMax);
                const bool reachSet = set_reachable(model, reach);
                (reach ? c_reachYes : c_reachNo).fetch_add(1);
                if (!motion::write_attack_fields(model, s.fields) || !set_state_engine(model, kStateWindup)) {
                    motion::write_attack_fields(model, pf);
                    s.engine = false;
                    logf("WO178-COPYSTATE npc=%s wind-up not set (the setter or the fields refused) -- the raw window as before", s.name.c_str());
                } else {
                    s.prior = cur == kStateStriking ? kStateGuard : cur;
                    s.priorFields = pf;
                    s.windupAt = now;
                    c_engineWindup.fetch_add(1);
                    s.engineReach = engine_reach(ca, model);   // in State 4 now: the reach's guard step stays out
                    s.canHit = wo178rules::in_reach(dist, wo178rules::copy_reach_limit(s.engineReach, s.hitDist));
                    bool hadTarget = false;
                    s.targetSet = oppMe && ensure_target_player(ca, model, &hadTarget);
                    (s.targetSet ? (hadTarget ? c_targetHad : c_targetSet) : c_targetFailed).fetch_add(1);
                    if (!s.targetSet) s.canHitWrote = set_can_hit(model, s.canHit);   // no target: ours, re-written every frame
                    if (oppMe && s.countdown && plan_allowed(s.eid, s.name.c_str())) s.planned = plan_defender_slots(ca);   // the attack's own planning: the perfect-block window
                    wo187::note_plan(s.name.c_str(), oppMe, s.planned, oppMe && s.countdown && s.planned == 0);   // WO-187 A3
                    (s.canHit ? c_canHitYes : c_canHitNo).fetch_add(1);
                    float wlen = -1, wcls = -1;
                    weapon_dims(ca, &wlen, &wcls);
                    if (c_engineWindup.load() <= 30 || c_engineWindup.load() % 50 == 0)
                        logf("WO178-CANHIT npc=%s planned_slots=%d target=%s expect_can_hit=%s%s dist_m=%.2f engine_reach_m=%.2f weapon_length_m=%.2f "
                             "class_reach_m=%.2f (row hit_distance=%.2f) -- the green shield opens only within the weapon's reach", s.name.c_str(), s.planned,
                             s.targetSet ? (hadTarget ? "had-the-local-player" : "set-to-the-local-player (the engine decides)") : "NONE (the mod writes it)",
                             s.canHit ? "yes" : "no", s.targetSet || s.canHitWrote ? "" : "(NOT-SET)", dist, s.engineReach, wlen, wcls, s.hitDist);
                    if (c_engineWindup.load() <= 30 || c_engineWindup.load() % 50 == 0)
                        logf("WO178-COPYSTATE npc=%s wind-up state 0x%X -> 4 (the engine's setter) zone=%d type=%d opponent=%s strike_in_ms=%.0f countdown=%s:%.2fs "
                             "reachable=%s%s dist_m=%.2f hit_distance=%.2f align_max=%.2f",
                             s.name.c_str(), static_cast<unsigned>(cur), s.fields.zone, s.fields.type,
                             oppMe ? "the-local-player" : "NOT-the-local-player (no slot opens on him)", (s.begin - now) * 1000.0,
                             s.countdown ? "set" : "NOT-SET(layout)", s.hitAt - now, reach ? "yes" : "no", reachSet ? "" : "(NOT-SET)", dist, s.hitDist,
                             s.alignMax);
                }
            }
        }
        if ((s.countdown || s.canHitWrote || s.targetSet) && !s.done) {
            void* ca2 = nullptr; void* model2 = nullptr;
            if (motion::combat_parts(s.eid, false, &ca2, &model2)) {
                bool eng = false;
                if (s.targetSet && !s.sawCanHit && read_can_hit(model2, &eng) && eng) {
                    s.sawCanHit = true;
                    c_engineCanHit.fetch_add(1);
                    if (c_engineCanHit.load() <= 30 || c_engineCanHit.load() % 50 == 0)
                        logf("WO178-CANHIT npc=%s the engine's own CanHitByAttack=yes dist_m=%.2f -- the green shield's condition holds", s.name.c_str(),
                             planar_to_player(s.eid));
                }
                if (s.countdown) write_countdown(model2, static_cast<float>((std::max)(0.0, s.hitAt - now)));
                if (s.canHitWrote) {
                    bool back = s.canHit;
                    if (read_can_hit(model2, &back) && back != s.canHit && ++s.canHitLost <= 3) {
                        c_canHitLost.fetch_add(1);
                        logf("WO178-CANHIT npc=%s read back %s after ours %s (the engine's own update wrote it) -- ours again", s.name.c_str(),
                             back ? "yes" : "no", s.canHit ? "yes" : "no");
                    }
                    const float r = engine_reach(ca2, model2);
                    if (wo178rules::engine_reach_ok(r)) s.engineReach = r;
                    s.canHit = wo178rules::in_reach(planar_to_player(s.eid), wo178rules::copy_reach_limit(s.engineReach, s.hitDist));
                    set_can_hit(model2, s.canHit);
                }
            }
        }
        // WO-185: a perfect block that began (the contact step is pending) leaves the copy's state to that step
        if (s.engine && s.windup && !s.wrote && !s.done && live && s.pbStage == 0 && !s.pbPending && now >= s.begin && now <= s.end) {
            void* ca = nullptr; void* model = nullptr;
            int32_t cur = 0;
            if (!motion::combat_parts(s.eid, false, &ca, &model) || !motion::read_state(model, &cur) || cur != kStateWindup) {
                s.done = true; s.discardUntil = now;   // the engine moved the copy on in its wind-up: never cut off
                c_engineMovedOn.fetch_add(1);
            } else if (!set_state_engine(model, kStateStriking)) {
                switch_off("wo166::set_state_engine"); s.done = true; s.discardUntil = now;
            } else {
                s.wrote = true;
                c_wrote.fetch_add(1);
                c_engineStrike.fetch_add(1);
                s.n = ++g_windowN;
                if (s.n <= kLogFirst || s.n % 50 == 0)
                    logf("WO166-STRIKE npc=%s window=%u begin state 0x4 -> Striking (the engine's setter) type=%d zone=%d window_ms=%.0f", s.name.c_str(), s.n,
                         s.fields.type, s.fields.zone, (s.end - now) * 1000.0);
            }
        }
        if (!s.wrote && !s.done && !s.engine) {
            if (!live || now > s.end) { c_missed.fetch_add(1); s.done = true; s.discardUntil = now; }
            else if (now >= s.begin && wo172::skip_dead(s.eid, "strike")) { c_missed.fetch_add(1); s.done = true; s.discardUntil = now; }   // WO-172 C1
            else if (now >= s.begin) {
                void* ca = nullptr; void* model = nullptr;
                int32_t cur = 0;
                if (!motion::combat_parts(s.eid, false, &ca, &model)) { c_refusedModel.fetch_add(1); s.done = true; s.discardUntil = now; }
                else if (!motion::read_state(model, &cur)) { c_refusedModel.fetch_add(1); s.done = true; s.discardUntil = now; }
                else if (!strike_state_ok(cur) && motion::is_avatar_eid(s.eid)) {
                    // WO-173: a partner's figure in a reaction of its own (an NPC's blow on it) waits for the reaction to end inside its
                    // window -- never cut off -- and strikes then (the partner's blow that follows needs the window to go through the engine)
                    if (!s.waited) { s.waited = true; c_figureWaited.fetch_add(1); }
                } else if (!strike_state_ok(cur) && !take_over_leftover(s, ca, model, &cur)) {   // WO-190 A6: a leftover of its last swing ends first
                    c_refusedState.fetch_add(1);
                    if (c_refusedState.load() <= 20) logf("WO166-STRIKE npc=%s refused: the copy's state is 0x%X (an engine reaction of its own: never cut off)", s.name.c_str(), static_cast<unsigned>(cur));
                    s.done = true; s.discardUntil = now;
                } else {
                    motion::AttackFields pf{};
                    motion::read_attack_fields(model, &pf);
                    if (!hits::is_discard_attacker(s.eid)) s.discardAdded = hits::discard_from(s.eid, true);
                    if (!motion::write_attack_fields(model, s.fields)) { switch_off("wo166::write_fields"); s.done = true; s.discardUntil = now; }
                    else if (!motion::write_state(model, kStateStriking)) {
                        motion::write_attack_fields(model, pf);
                        switch_off("wo166::write_state"); s.done = true; s.discardUntil = now;
                    } else {
                        s.prior = cur == kStateStriking ? kStateGuard : cur;   // a copy already ours goes back to Guard
                        s.priorFields = pf;
                        s.wrote = true;
                        c_wrote.fetch_add(1);
                        s.n = ++g_windowN;
                        if (s.n <= kLogFirst || s.n % 50 == 0)
                            logf("WO166-STRIKE npc=%s window=%u begin state 0x%X -> Striking type=%d zone=%d strength=%.2f hand=%d window_ms=%.0f discard=%s",
                                 s.name.c_str(), s.n, static_cast<unsigned>(cur), s.fields.type, s.fields.zone, s.fields.strength, s.fields.hand,
                                 (s.end - now) * 1000.0, s.discardAdded ? "added" : "already");
                    }
                }
            }
        }
        if (s.wrote && !s.done && !s.midRead && now >= (s.begin + s.end) / 2) {
            s.midRead = true;
            motion::ModelRead m{}, pm{};
            const bool ok = motion::read_model(s.eid, &m);
            const uint32_t pe = player_eid();
            const bool pok = pe && motion::read_model(pe, &pm);
            const bool striking = ok && (m.valid & motion::kMvState) && m.state == kStateStriking;
            (striking ? c_midStriking : c_midOther).fetch_add(1);
            const bool oppIsCopy = pok && pm.opponentEid == s.eid;
            if (oppIsCopy) c_playerOpp.fetch_add(1);
            if (s.n <= kLogFirst || s.n % 50 == 0)
                logf("WO166-STRIKE npc=%s window=%u mid state=0x%X at=%d az=%d player_state=0x%X player_opponent=%s", s.name.c_str(), s.n,
                     ok ? static_cast<unsigned>(m.state) : 0u, ok ? m.atkType : -9, ok ? m.atkZone : -9, pok ? static_cast<unsigned>(pm.state) : 0u,
                     oppIsCopy ? "this-copy" : (pok && pm.opponentEid ? "another" : "none"));
        }
        // WO-178: the player's perfect block met the copy's blow -> the attacker's own perfect-blocked sequence (0x10 with OpponentHit, then
        // 0x20, then its prior state at the window's end): the player's riposte (the blue swords) opens on it as on a real attacker
        // WO-185 (mp_pb_at_contact, default on): a real attacker goes 0x10 when its blow is processed -- contact -- not when the defender's block
        // began, so the press only arms the step (the copy is put back to striking if the engine dropped it to Guard) and the contact does it;
        // off = the 0.60.0 behaviour (the step at the press, 0x20 0.5 s later).
        if (s.engine && s.windup && !s.done && live && s.pbStage == 0 && wo166rules::pb_moment(now, s.windupAt, s.end)) {
            const bool atContact = wo178::pb_at_contact_on();
            const bool due = !atContact || (s.pbPending && wo166rules::pb_contact_due(now, s.hitAt));
            motion::ModelRead pm{};
            const uint32_t pe = player_eid();
            const bool blocking = pe && motion::read_model(pe, &pm) && (pm.valid & motion::kMvState) && (pm.valid & motion::kMvPerfect) &&
                                  pm.opponentEid == s.eid && wo166rules::player_perfect_blocks(pm.state, pm.perfectBlock != 0);
            void* ca = nullptr; void* model = nullptr;
            int32_t cur = 0;
            if (atContact && !s.pbPending && blocking) {
                s.pbPending = true; s.pbPendingAt = now;
                const bool parts = motion::combat_parts(s.eid, false, &ca, &model) && motion::read_state(model, &cur);
                if (parts && cur == kStateGuard && set_state_engine(model, kStateStriking)) s.pbReStruck = true;
                logf("WO185-PBARM npc=%s window=%u the player's perfect block began (player state 0x%X) at +%.0f ms of the swing; the copy's 0x10 waits for contact "
                     "(+%.0f ms)%s", s.name.c_str(), s.n, static_cast<unsigned>(pm.state), (now - s.windupAt) * 1000.0, (s.hitAt - s.windupAt) * 1000.0,
                     s.pbReStruck ? "; the engine's drop to Guard put back to striking" : "");
            } else if (due && atContact && s.pbPending && !blocking) {
                s.pbPending = false;
                s.pbStage = -1;   // the perfect block ended before contact: the verdict is the engine's on whatever he does then
                logf("WO185-PBLOST npc=%s window=%u the player's perfect block ended before the blow's contact (player state 0x%X pb=%d)", s.name.c_str(), s.n,
                     static_cast<unsigned>(pm.state), pm.perfectBlock != 0 ? 1 : 0);
            } else if (due && blocking) {
                if (motion::combat_parts(s.eid, false, &ca, &model) && motion::read_state(model, &cur) && wo166rules::pb_from_state(cur)) {
                    s.oppHitSet = set_opponent_hit(model, true);
                    if (set_state_engine(model, wo166rules::kStatePerfectBlocked)) {
                        s.pbStage = 1; s.pbAt = now;
                        s.pbStage2S = atContact ? stage2_for(s) : wo166rules::kPbStage2S;
                        const double holdS = atContact ? s.pbStage2S + wo166rules::kPbContactTailS : wo166rules::kPbHoldS;
                        s.end = (std::max)(s.end, now + holdS);
                        c_pbHits.fetch_add(1);
                        logf("WO178-PBHIT npc=%s window=%u the player's perfect block met the blow (player state 0x%X) -> the copy 0x%X -> 0x10%s, 0x20 "
                             "at +%.0f ms, its prior state at +%.0f ms -- the riposte opens on it%s", s.name.c_str(), s.n, static_cast<unsigned>(pm.state),
                             static_cast<unsigned>(cur),
                             s.oppHitSet ? " with OpponentHit" : " (OpponentHit NOT set: no riposte)", s.pbStage2S * 1000.0,
                             holdS * 1000.0,
                             atContact ? " [at contact]" : " [at the press]");
                    } else {
                        if (s.oppHitSet) { set_opponent_hit(model, false); s.oppHitSet = false; }
                        c_pbHitRefused.fetch_add(1);
                    }
                } else {
                    c_pbHitRefused.fetch_add(1);
                }
            }
        }
        if (s.pbStage == 1 && !s.done && now >= s.pbAt + s.pbStage2S) {
            void* ca = nullptr; void* model = nullptr;
            int32_t cur = 0;
            if (motion::combat_parts(s.eid, false, &ca, &model) && motion::read_state(model, &cur) && cur == wo166rules::kStatePerfectBlocked) {
                // WO-187 B3: a bare-handed attacker has no withdraw: its prior state (guard) back now, as the vanilla brawler does
                if (wo187rules::pb_withdraws(wo187rules::weapon_from_byte(s.weapon))) set_state_engine(model, wo166rules::kStateAfterPerfectBlocked);
                else s.end = (std::min)(s.end, now);
            }
            s.pbStage = 2;
        }
        if (s.engine && s.windup && !s.wrote && !s.done && (!live || now > s.end)) { restore(s, !live); s.lastEnd = now; s.discardUntil = now + kDiscardTailS; }
        if (s.wrote && !s.done && now >= s.end) {
            restore(s, false);
            s.lastEnd = now;
            s.discardUntil = now + kDiscardTailS;
        }
        if (s.done && now >= s.discardUntil && now - s.lastEnd >= kRecentS) {
            if (s.discardAdded && !other_live_strike(s.eid, &s)) hits::discard_from(s.eid, false);
            it = g_strikes.erase(it);
            continue;
        }
        ++it;
    }
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    auto text = [&](const char* fmt, auto... a) {
        const int n = std::snprintf(reinterpret_cast<char*>(out), cap, fmt, a...);
        *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
    };
    switch (op) {
        case kOpStrike: {
            // [startMs:i16][hitMs:i16][type:i8][zone:i8][hand:i8][strength:f32][nameLen:1][name]
            if (len < 1 + 2 + 2 + 3 + 4 + 1 || cap < 64) return wo163::kRBadRequest;
            int16_t startMs = 0, hitMs = 0; float strength = 0;
            std::memcpy(&startMs, body + 1, 2); std::memcpy(&hitMs, body + 3, 2);
            motion::AttackFields f{};
            f.type = static_cast<int8_t>(body[5]); f.zone = static_cast<int8_t>(body[6]); f.hand = static_cast<int8_t>(body[7]);
            std::memcpy(&strength, body + 8, 4);
            f.strength = strength;
            const size_t n = body[12];
            if ((len != 13 + n && len != 13 + n + 8 && len != 13 + n + 9) || !name_ok(reinterpret_cast<const char*>(body + 13), n)) return wo163::kRBadRequest;
            float hitDist = 0, alignMax = 0;   // WO-178: the row's own reach (an 8-byte tail after the name)
            if (len >= 13 + n + 8) { std::memcpy(&hitDist, body + 13 + n, 4); std::memcpy(&alignMax, body + 17 + n, 4); }
            const uint8_t weapon = len == 13 + n + 9 ? body[21 + n] : 0;   // WO-187 B3: the row's weapon class (a ninth tail byte)
            c_requests.fetch_add(1);
            if (!g_on.load()) { text("refused=off"); return wo163::kROk; }
            if (g_faulted.load()) { text("refused=switched-off site=%s", g_faultSite.c_str()); return wo163::kRFailed; }
            if (!attack_fields_ok(f.type, f.zone, f.strength, f.hand)) { c_refusedFields.fetch_add(1); text("refused=fields"); return wo163::kROk; }
            const std::string name(reinterpret_cast<const char*>(body + 13), n);
            const uint32_t eid = hits::eid_of_name(name.c_str());
            if (!eid) { text("refused=no-entity"); return wo163::kRNoActor; }
            const double now = now_s();
            const StrikeWindow w = strike_window(now * 1000.0, startMs, hitMs);
            if (!w.valid) { text("refused=timing"); return wo163::kROk; }
            // a swing on a copy whose window is still open: the window runs on with the new row's fields
            for (auto& s : g_strikes) {
                if (s.eid == eid && !s.done) {
                    s.end = (std::max)(s.end, w.end_ms / 1000.0 + (motion::is_avatar_eid(eid) ? kFigureTailS : 0.0));
                    s.fields = f;
                    if (s.wrote) {
                        void* ca = nullptr; void* model = nullptr;
                        if (motion::combat_parts(eid, false, &ca, &model) && !motion::write_attack_fields(model, f)) switch_off("wo166::merge_fields");
                    }
                    c_merged.fetch_add(1);
                    text("merged window_end_ms=%.0f", (s.end - now) * 1000.0);
                    return wo163::kROk;
                }
            }
            if (g_strikes.size() >= kMaxStrikes) { text("refused=full"); return wo163::kROk; }
            Strike s;
            s.eid = eid; s.name = name; s.fields = f; s.weapon = weapon;
            s.begin = w.begin_ms / 1000.0; s.end = w.end_ms / 1000.0;
            // WO-178: a copy (never a partner's figure: its swing is the partner's, WO-173) winds up the engine's way from the row's start
            s.engine = !motion::is_avatar_eid(eid) && wo178::copy_engine_state_on() && resolve_set_state();
            s.hitAt = s.end - kStrikeTailMs / 1000.0;
            s.hitDist = std::isfinite(hitDist) ? hitDist : 0.0f; s.alignMax = std::isfinite(alignMax) ? alignMax : 0.0f;
            // WO-173: a partner's figure stays striking until the partner's measured hit has arrived (his game reports it a sampler period
            // after his blow lands, then the network): the host's engine judges that hit against a striker, as the victim saw it
            if (motion::is_avatar_eid(eid)) s.end += kFigureTailS;
            g_strikes.push_back(s);
            c_queued.fetch_add(1);
            text("queued window_ms=%.0f..%.0f", (s.begin - now) * 1000.0, (s.end - now) * 1000.0);
            return wo163::kROk;
        }
        case kOpStatus: {
            // a reply longer than the buffer is cut, never dropped (snprintf wrote cap-1 characters)
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = n <= 0 || cap == 0 ? 0 : (static_cast<size_t>(n) < cap ? static_cast<size_t>(n) : cap - 1);
            return wo163::kROk;
        }
        case kOpConfig: {
            if ((len != 3 && len != 4 && len != 5) || cap < 4) return wo163::kRBadRequest;
            if (len >= 4 && body[3] != 255) npcdrive::set_snap_fix(body[3] != 0);   // C4
            if (len == 5 && body[4] != 255) hits::set_figure_guard(body[4] != 0);   // WO-170 F1: mp_figure_guard
            const bool was = g_on.exchange(body[1] != 0);
            if (was != g_on.load()) logf("WO166-CFG mp_copy_strikes %s", g_on.load() ? "on" : "off");
            if (body[2] != 255 && body[2] <= kAutoMax) {
                const uint8_t old = g_autoMode.exchange(body[2]);
                if (old != body[2]) logf("WO166-CFG figure automation mode %u -> %u (0 all off, 1 all on, 2+ the lever's byte patterns)", old, body[2]);
            }
            out[0] = g_on.load() ? 1 : 0; out[1] = g_autoMode.load(); out[2] = npcdrive::snap_fix() ? 1 : 0; out[3] = hits::figure_guard() ? 1 : 0;
            *outLen = 4;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

int status_text(char* out, int n) {
    const int w0 = std::snprintf(out, n,
        "wo166 copy_strikes=%s faulted=%s requests=%u queued=%u merged=%u wrote=%u refused_state=%u refused_model=%u refused_fields=%u missed=%u "
        "restored=%u engine_moved_on=%u mid_striking=%u mid_other=%u player_opponent_copy=%u faults=%u local_dropped=%u local_dropped_striking=%u "
        "active=%zu auto_mode=%u snap_fix=%s figure_waited=%u",
        g_on.load() ? "on" : "off", g_faulted.load() ? "yes" : "no", c_requests.load(), c_queued.load(), c_merged.load(), c_wrote.load(),
        c_refusedState.load(), c_refusedModel.load(), c_refusedFields.load(), c_missed.load(), c_restored.load(), c_engineMovedOn.load(),
        c_midStriking.load(), c_midOther.load(), c_playerOpp.load(), c_faults.load(), c_localDropped.load(), c_localDroppedStriking.load(),
        g_strikes.size(), g_autoMode.load(), npcdrive::snap_fix() ? "on" : "off", c_figureWaited.load());
    if (w0 < 0 || w0 >= n) return w0;
    // WO-187 A3: the shield windows the copies' wind-ups planned (slot types planned) and the plannings refused
    return w0 + std::snprintf(out + w0, n - w0, " engine_state=%s windups=%u engine_strikes=%u engine_restores=%u opp_player=%u opp_other=%u planned=%u plan_refused=%u"
        " takeovers=%u takeover_kept=%u takeover_failed=%u plan_in_fight=%s plan_out_of_fight=%u plan_unanswered=%u",
        g_setState ? "armed" : (g_setStateResolved ? "not-armed" : "unresolved"), c_engineWindup.load(), c_engineStrike.load(), c_engineRestore.load(),
        c_engineOppPlayer.load(), c_engineOppOther.load(), c_planned.load(), c_planRefused.load(), c_takeovers.load(), c_takeoverKept.load(),
        c_takeoverFailed.load(), plan_in_fight_on() ? "on" : "off", c_planOutOfFight.load(), c_planUnanswered.load());
}

void set_plan_in_fight(bool on) { plan_in_fight_on(); g_planInFight.store(on ? 1 : 0); }

void on_pipe_closed() {
    // main thread is not guaranteed here: the windows end at the next tick (live=false restores them)
    g_on = false;
}

} // namespace kcdmp::wo166
