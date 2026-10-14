// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#include "wo178.h"
#include "wo178_rules.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "motion.h"
#include "wo163.h"
#include "wo172.h"
#include "wo176.h"

namespace kcdmp::wo178 {
namespace {

using namespace kcdmp::wo178rules;

constexpr char kSelectionLabel[] = "wh::combatmodule::C_CombatPlayerController::HandleOpponentSelection";
constexpr char kClinchLabel[] = "wh::combatmodule::C_CombatActorClinch::StartSyncGuard";
constexpr char kManagerClass[] = ".?AVC_PlayerProfileWHManager@framework@wh@@";
constexpr char kExpInitiateSave[] = "?InitiateSaveGame@C_PlayerProfileWHManager@framework@wh@@QEAA_NW4Type@E_SaveGameType@23@HPEBD@Z";
constexpr char kExpCanSave[] = "?CanSave@C_PlayerProfileWHManager@framework@wh@@QEBA_NW4Type@E_SaveGameType@23@@Z";
// WO-184: the engine's own pending-save state and its own cancel (the one LoadSavedGame runs before every load)
constexpr char kExpIsAnyPending[] = "?IsAnySaveGamePending@C_PlayerProfileWHManager@framework@wh@@QEBA_NXZ";
constexpr char kExpIsSaving[] = "?IsSaving@C_PlayerProfileWHManager@framework@wh@@QEBA_NXZ";
constexpr char kExpCancelPending[] = "?CancelPendingSaveGame@C_PlayerProfileWHManager@framework@wh@@QEAAXXZ";
constexpr uint8_t kSaveTypeManual = 2;        // E_SaveGameType: 0 Permanent, 1 Auto, 2 Manual, 3 Quick, 5 Exit (the enum's own name table)
constexpr uint8_t kSaveTypeQuick = 3;
constexpr size_t kSelectorWindow = 0x400;     // the selector's call sits 0x160 bytes in; the window is clipped to .text
constexpr size_t kManagerScan = 0x400;        // how far into the game interface the manager pointer is looked for

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

// ---------------------------------------------------------------------------------------------------------------- switches
std::atomic<bool> g_joinerLock{true};    // mp_joiner_lock
std::atomic<bool> g_hostAnswer{true};    // mp_host_lock_answer (on since 0.50.6; the agent pushes the switch)
std::atomic<bool> g_hold{true};          // C4 (1)
std::atomic<bool> g_engineCap{true};     // C4 (2)
std::atomic<bool> g_hitReach{true};      // mp_hit_reach (the joiner's replay: out of reach = a miss)
std::atomic<bool> g_clinchGuard{true};   // mp_clinch_guard (no clinch between the local player and a listed copy)
std::atomic<bool> g_ownBlock{true};      // mp_own_block (the local player's held block decides a copy's blow)
std::atomic<bool> g_copyEngineState{true};   // mp_copy_engine_state (a copy's swing through the engine's state setter)
std::atomic<bool> g_pbAtContact{true};       // mp_pb_at_contact (the copy's perfect-blocked sequence starts at contact, as a real attacker's does)
std::atomic<bool> g_isHost{false};       // the last Config's role word (which switch the answer reads)
std::atomic<bool> g_off{false};          // a fault switched the answer to pass-through for this session

// ---------------------------------------------------------------------------------------------------------------- the hook
using HostileFn = bool(__fastcall*)(void* rpg, const void* candidate);
HostileFn g_orig = nullptr;
std::atomic<bool> g_armed{false};
const char* g_why = "not resolved";
int g_ownerOff = -1;
uint32_t g_rpgOff = 0;
void* g_test = nullptr;

// The table the hook reads: a four-slot rotation behind an atomic index, rebuilt by the main thread at 4 Hz and never written in place
// while it is the current one (a reader that took the previous index finishes on rows nobody writes for another 750 ms).
struct Row { const void* ca; uint32_t eid; uint8_t why; uint8_t ex; uint8_t slot; };
struct Table { int n; Row rows[kMaxRows]; };
Table g_tables[4]{};
std::atomic<int> g_cur{-1};

// Per-candidate counters the hook bumps (slots assigned by the main thread, stable per entity while it is listed).
struct Slot { std::atomic<uint32_t> eid{0}, yes{0}, gameNo{0}, gameYes{0}, excluded{0}; };
Slot g_slots[64];
std::atomic<const void*> g_lastOwner{nullptr}, g_lastMe{nullptr}, g_lastCand{nullptr};   // the live read's evidence (status)
std::atomic<uint32_t> c_asked{0}, c_askedLocal{0}, c_listed{0}, c_yes{0}, c_gameYes{0}, c_gameNo{0}, c_foreign{0}, c_faults{0};

bool read_ptr(const void* base, int off, void** out) {
    KCDMP_FAULT_READ(site, "wo178::owner");
    if (!fault::enabled(site)) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { *out = *reinterpret_cast<void* const*>(a); });
}

bool __fastcall hostile_detour(void* rpg, const void* candidate) {
    c_asked.fetch_add(1, std::memory_order_relaxed);
    void* me = motion::player_combat_actor();
    void* owner = nullptr;
    if (!me || g_off.load(std::memory_order_relaxed) || !read_ptr(rpg, g_ownerOff, &owner)) {
        if (me && !g_off.load(std::memory_order_relaxed)) {
            c_faults.fetch_add(1, std::memory_order_relaxed);
            g_off.store(true);   // the owner could not be read: pass-through for the session (tick says so)
        }
        return g_orig(rpg, candidate);
    }
    g_lastOwner.store(owner, std::memory_order_relaxed); g_lastMe.store(me, std::memory_order_relaxed);
    if (owner != me) { c_foreign.fetch_add(1, std::memory_order_relaxed); return g_orig(rpg, candidate); }
    c_askedLocal.fetch_add(1, std::memory_order_relaxed);
    g_lastCand.store(candidate, std::memory_order_relaxed);
    const int cur = g_cur.load(std::memory_order_acquire);
    const Row* hit = nullptr;
    if (cur >= 0) {
        const Table& t = g_tables[cur];
        for (int i = 0; i < t.n; ++i) if (t.rows[i].ca == candidate) { hit = &t.rows[i]; break; }
    }
    const bool game = g_orig(rpg, candidate);
    (game ? c_gameYes : c_gameNo).fetch_add(1, std::memory_order_relaxed);
    if (!hit) return game;
    c_listed.fetch_add(1, std::memory_order_relaxed);
    Slot& s = g_slots[hit->slot & 63];
    (game ? s.gameYes : s.gameNo).fetch_add(1, std::memory_order_relaxed);
    const bool on = g_isHost.load(std::memory_order_relaxed) ? g_hostAnswer.load(std::memory_order_relaxed) : g_joinerLock.load(std::memory_order_relaxed);
    if (!answer_hostile(on, true, true, static_cast<Ex>(hit->ex))) {
        if (hit->ex) s.excluded.fetch_add(1, std::memory_order_relaxed);
        return game;
    }
    s.yes.fetch_add(1, std::memory_order_relaxed);
    c_yes.fetch_add(1, std::memory_order_relaxed);
    return true;
}

// ---- the clinch (C_CombatActorClinch::StartSyncGuard(type, fighterA, fighterB, ...) -> bool): a copy cannot take its half -- its body
// plays the host's rows, so the paired push-back fought its own swings (the field: it jittered between the two). Refused when one fighter is
// the local player and the other a copy in the table (engaged with him, or fighting his host); the engine's callers take "not started".
std::atomic<bool> g_clinchArmed{false};
std::atomic<uint32_t> c_clinchAsked{0}, c_clinchRefused{0};
std::atomic<uint32_t> g_clinchLastEid{0};

bool listed_eid(const void* ca, uint32_t* eid) {
    const int cur = g_cur.load(std::memory_order_acquire);
    if (cur < 0) return false;
    const Table& t = g_tables[cur];
    for (int i = 0; i < t.n; ++i) if (t.rows[i].ca == ca) { if (eid) *eid = t.rows[i].eid; return true; }
    return false;
}

bool clinch_gate(void* /*type*/, void* a, void* b, void* /*r9*/) {
    if (!g_clinchGuard.load(std::memory_order_relaxed)) return false;
    void* me = motion::player_combat_actor();
    if (!me || (a != me && b != me)) return false;
    c_clinchAsked.fetch_add(1, std::memory_order_relaxed);
    uint32_t eid = 0;
    if (!listed_eid(a == me ? b : a, &eid)) return false;
    c_clinchRefused.fetch_add(1, std::memory_order_relaxed);
    g_clinchLastEid.store(eid, std::memory_order_relaxed);
    return true;
}

bool copy_code(const void* src, uint8_t* out, size_t n) {
    KCDMP_FAULT_READ(site, "wo178::code");
    return fault::guarded(site, [&] { std::memcpy(out, src, n); });
}

bool resolve_hook() {
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    if (!cm) { g_why = "CombatModule not loaded"; return false; }
    int n = 0;
    const uint8_t* sel = anchor::function_by_string(cm, kSelectionLabel, &n);
    if (!sel) { g_why = "HandleOpponentSelection's label is not referenced by exactly one function"; return false; }
    anchor::Range text{};
    if (!anchor::section(cm, ".text", &text)) { g_why = "no .text"; return false; }
    const uint8_t* callees[32]{};
    const int nc = anchor::function_call_targets(cm, sel, callees, 32);
    RpgCall found{};
    int distinct = 0;
    static uint8_t buf[kSelectorWindow];
    for (int i = 0; i < nc; ++i) {
        if (!text.contains(callees[i])) continue;
        const size_t want = static_cast<size_t>(text.end - callees[i]) < sizeof buf ? static_cast<size_t>(text.end - callees[i]) : sizeof buf;
        if (!copy_code(callees[i], buf, want)) continue;
        const RpgCall r = find_rpg_call(buf, want, reinterpret_cast<uintptr_t>(callees[i]), 0);
        if (r.count == 0) continue;
        if (r.count > 1 || (distinct && r.target != found.target)) { g_why = "more than one combat-RPG call in the selection's callees (ambiguous)"; return false; }
        if (!distinct) found = r;
        distinct = 1;
    }
    if (!distinct) { g_why = "no callee of the selection loads the combat RPG and calls a test"; return false; }
    if (!text.contains(reinterpret_cast<const void*>(found.target))) { g_why = "the test lies outside CombatModule's code"; return false; }
    uint8_t head[64]{};
    if (!copy_code(reinterpret_cast<const void*>(found.target), head, sizeof head)) { g_why = "the test's code could not be read"; return false; }
    if (std::memcmp(head, hookpro::kHostileTest, sizeof hookpro::kHostileTest) != 0) { g_why = "the test's prologue is not the one read (another build)"; return false; }
    const int own = owner_offset(head + sizeof hookpro::kHostileTest, sizeof head - sizeof hookpro::kHostileTest);
    if (own <= 0 || own > 0x78) { g_why = "the test does not load its owner the way it was read"; return false; }
    g_test = reinterpret_cast<void*>(found.target);
    g_rpgOff = found.disp;
    g_ownerOff = own;
    return true;
}

// ---------------------------------------------------------------------------------------------------------------- offers (main thread)
struct Offer { uint8_t why = 0; bool down = false; double at = 0; bool local = false; };
std::mutex g_offerMx;
std::unordered_map<uint32_t, Offer> g_offers;
std::unordered_map<uint32_t, int> g_slotOf;   // main thread
struct Said { double at = 0; uint32_t yes = 0, gameNo = 0, excluded = 0; };
std::unordered_map<uint32_t, Said> g_said;     // main thread
double g_next = 0, g_statsAt = 0;
DWORD g_mainTid = 0;
bool g_offSaid = false, g_crossChecked = false;
uint32_t s_asked0 = 0, s_local0 = 0, s_yes0 = 0, s_listed0 = 0, s_gy0 = 0, s_gn0 = 0;
std::atomic<uint32_t> c_offers{0}, c_withdrawn{0}, c_rows{0}, c_noBody{0};

const char* name_of(uint32_t eid) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "?";
}

int slot_for(uint32_t eid) {
    auto it = g_slotOf.find(eid);
    if (it != g_slotOf.end()) return it->second;
    for (int i = 0; i < 64; ++i) {
        bool used = false;
        for (const auto& kv : g_slotOf) if (kv.second == i) { used = true; break; }
        if (used) continue;
        g_slots[i].eid.store(eid); g_slots[i].yes.store(0); g_slots[i].gameNo.store(0); g_slots[i].gameYes.store(0); g_slots[i].excluded.store(0);
        g_slotOf[eid] = i;
        return i;
    }
    return -1;
}

void rebuild(double now) {
    std::unordered_map<uint32_t, Offer> live;
    {
        std::lock_guard<std::mutex> lk(g_offerMx);
        for (auto it = g_offers.begin(); it != g_offers.end();) {
            if (!offer_live(it->second.at, now)) it = g_offers.erase(it);
            else { live[it->first] = it->second; ++it; }
        }
    }
    // slots of bodies no longer offered are freed (their last counts are said first)
    for (auto it = g_slotOf.begin(); it != g_slotOf.end();) {
        if (live.count(it->first)) { ++it; continue; }
        g_said.erase(it->first);
        it = g_slotOf.erase(it);
    }
    const int cur = g_cur.load();
    const int next = (cur + 1) & 3;
    Table& t = g_tables[next];
    t.n = 0;
    for (const auto& kv : live) {
        if (t.n >= kMaxRows) break;
        void* ca = nullptr; void* model = nullptr;
        if (!motion::combat_parts(kv.first, false, &ca, &model) || !ca) { c_noBody.fetch_add(1); continue; }
        Candidate c{};
        c.figure = motion::is_avatar_eid(kv.first);
        c.arranged = wo176::arranged_context(kv.first) == 1;
        c.dead = wo172::copy_dead(kv.first);
        c.down = kv.second.down;
        const int slot = slot_for(kv.first);
        if (slot < 0) break;
        t.rows[t.n++] = Row{ca, kv.first, kv.second.why, static_cast<uint8_t>(exclusion(c)), static_cast<uint8_t>(slot)};
    }
    c_rows.store(static_cast<uint32_t>(t.n));
    g_cur.store(next, std::memory_order_release);
}

void say_rows(double now) {
    const int cur = g_cur.load();
    if (cur < 0) return;
    const Table& t = g_tables[cur];
    for (int i = 0; i < t.n; ++i) {
        const Row& r = t.rows[i];
        Slot& s = g_slots[r.slot & 63];
        Said& d = g_said[r.eid];
        const uint32_t yes = s.yes.load(), gn = s.gameNo.load(), ex = s.excluded.load();
        const bool news = yes != d.yes || ex != d.excluded;
        if (!news || now - d.at < 10.0) continue;
        d.at = now;
        const bool answered = yes != d.yes;
        logf("WO178-HOSTILE answer=%s npc=%s why=%s asked=%u game_said_no=%u -- %s", answered ? "yes" : "game", name_of(r.eid),
             answered ? why_name(static_cast<Why>(r.why)) : ex_name(static_cast<Ex>(r.ex)), yes + ex, gn,
             answered ? "the local player's picker may lock it (everything else is the game's)" : "listed, but excluded: the game's own answer stands");
        d.yes = yes; d.gameNo = gn; d.excluded = ex;
    }
}

// ---------------------------------------------------------------------------------------------------------------- the schnapps save
using InitiateFn = bool(__fastcall*)(void* mgr, uint8_t type, int32_t overwrite, const char* quest);
using CanSaveFn = bool(__fastcall*)(const void* mgr, uint8_t type);
using MgrBoolFn = bool(__fastcall*)(const void* mgr);
using MgrVoidFn = void(__fastcall*)(void* mgr);
InitiateFn g_initiate = nullptr;
CanSaveFn g_canSave = nullptr;
MgrBoolFn g_isAnyPending = nullptr, g_isSaving = nullptr;   // WO-184 (optional: missing ones leave save_state not armed)
MgrVoidFn g_cancelPending = nullptr;
std::atomic<uint32_t> c_cancels{0};
void* const* g_vftMgr = nullptr;
int g_mgrOff = -1;
bool g_saveResolved = false, g_saveArmed = false;
const char* g_saveWhy = "not resolved";
std::atomic<uint32_t> c_saveReq{0}, c_saveRefused{0}, c_saveFault{0};

bool resolve_save() {
    if (g_saveResolved) return g_saveArmed;
    g_saveResolved = true;
    HMODULE fw = GetModuleHandleA("Framework.dll");
    if (!fw) { g_saveWhy = "Framework not loaded"; return false; }
    g_initiate = reinterpret_cast<InitiateFn>(GetProcAddress(fw, kExpInitiateSave));
    g_canSave = reinterpret_cast<CanSaveFn>(GetProcAddress(fw, kExpCanSave));
    g_isAnyPending = reinterpret_cast<MgrBoolFn>(GetProcAddress(fw, kExpIsAnyPending));
    g_isSaving = reinterpret_cast<MgrBoolFn>(GetProcAddress(fw, kExpIsSaving));
    g_cancelPending = reinterpret_cast<MgrVoidFn>(GetProcAddress(fw, kExpCancelPending));
    logf("WO184-SAVE exports: IsAnySaveGamePending=%s IsSaving=%s CancelPendingSaveGame=%s (the joiner's snapshot reads the engine's own state)",
         g_isAnyPending ? "found" : "MISSING", g_isSaving ? "found" : "MISSING", g_cancelPending ? "found" : "MISSING");
    if (!g_initiate || !g_canSave) { g_saveWhy = "InitiateSaveGame / CanSave not exported by Framework"; return false; }
    g_vftMgr = anchor::find_vftable(fw, kManagerClass, 0);
    if (!g_vftMgr) { g_saveWhy = "RTTI C_PlayerProfileWHManager vftable not unique"; return false; }
    g_saveArmed = true;
    g_saveWhy = "armed";
    return true;
}

// The manager: the game interface's pointer whose object carries the manager's own vftable (found once, the offset cached and re-checked).
void* manager() {
    void* gi = engine::game_iface();
    if (!gi || !g_vftMgr) return nullptr;
    KCDMP_FAULT_READ(site, "wo178::manager");
    void* found = nullptr;
    auto at = [&](int off) -> void* {
        void* p = nullptr; void* vp = nullptr;
        if (!read_ptr(gi, off, &p) || !p) return nullptr;
        if (!fault::guarded(site, [&] { vp = *reinterpret_cast<void**>(p); }) || vp != static_cast<const void*>(g_vftMgr)) return nullptr;
        return p;
    };
    if (g_mgrOff >= 0) found = at(g_mgrOff);
    if (!found) {
        for (int off = 0; off < static_cast<int>(kManagerScan); off += 8) {
            if ((found = at(off)) != nullptr) { g_mgrOff = off; break; }
        }
    }
    return found;
}

std::string g_lastSave = "none";

// ---- the duel trace: the actor state handler probe (installed with the first trace; pass-through) ----
struct StateCall { double t; const void* ca; int32_t oldS, newS; };
std::vector<StateCall> g_stateCalls;   // under g_traceMx
struct SlotCall { double t; bool start; const void* trig; int32_t arg; };
std::vector<SlotCall> g_slotCalls;     // under g_traceMx
std::atomic<bool> g_stateProbe{false};
bool g_stateProbeTried = false;
std::mutex g_traceMx;
std::atomic<double> g_traceUntil{0.0};
bool state_probe_cb(void* ca, void* /*prop*/, void* oldS, void* newS) {
    LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f);
    const double t = double(q.QuadPart) / double(f.QuadPart);
    if (t > g_traceUntil.load()) return false;
    std::lock_guard<std::mutex> lk(g_traceMx);
    if (g_stateCalls.size() < 4096)
        g_stateCalls.push_back(StateCall{t, ca, static_cast<int32_t>(reinterpret_cast<intptr_t>(oldS)), static_cast<int32_t>(reinterpret_cast<intptr_t>(newS))});
    return false;   // never refuses: the engine's handler always runs
}
bool slot_cb(bool start, void* trig, void* arg) {
    LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f);
    const double t = double(q.QuadPart) / double(f.QuadPart);
    if (t > g_traceUntil.load()) return false;
    std::lock_guard<std::mutex> lk(g_traceMx);
    if (g_slotCalls.size() < 4096) g_slotCalls.push_back(SlotCall{t, start, trig, static_cast<int32_t>(reinterpret_cast<intptr_t>(arg))});
    return false;
}
bool slot_start_cb(void* trig, void* dl, void*, void*) { return slot_cb(true, trig, dl); }
bool slot_end_cb(void* trig, void* edx, void*, void*) { return slot_cb(false, trig, edx); }
// an object's class by its RTTI (vptr[-1] = the complete object locator; its type descriptor name at +0x10), "?" when unreadable
std::string rtti_name(const void* obj) {
    void* vt = nullptr; void* col = nullptr;
    if (!obj || !read_ptr(obj, 0, &vt) || !vt || !read_ptr(vt, -8, &col) || !col) return "?";
    int32_t tdRva = 0, selfRva = 0;
    KCDMP_FAULT_READ(site, "wo178::rtti");
    if (!fault::guarded(site, [&] { tdRva = *reinterpret_cast<const int32_t*>(static_cast<const char*>(col) + 12);
                                     selfRva = *reinterpret_cast<const int32_t*>(static_cast<const char*>(col) + 20); })) return "?";
    const char* base = static_cast<const char*>(col) - selfRva;
    char nm[96]{};
    const char* td = base + tdRva + 16;
    if (!fault::guarded(site, [&] { for (int i = 0; i < 95 && td[i]; ++i) nm[i] = td[i]; })) return "?";
    return nm;
}
void install_slot_probes() {
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    int n = 0;
    const uint8_t* s = cm ? anchor::function_by_string(cm, "wh::combatmodule::C_CombatSlotTrigger::StartSlot", &n) : nullptr;
    const uint8_t* e = cm ? anchor::function_by_string(cm, "wh::combatmodule::C_CombatSlotTrigger::EndSlot", &n) : nullptr;
    const char* why = nullptr;
    const bool okS = s && inlinehook::install_gate4(const_cast<uint8_t*>(s), hookpro::kSlotStart, sizeof hookpro::kSlotStart, &slot_start_cb, &why);
    const char* whyS = why;
    const bool okE = e && inlinehook::install_gate4(const_cast<uint8_t*>(e), hookpro::kSlotEnd, sizeof hookpro::kSlotEnd, &slot_end_cb, &why);
    logf("WO178-TRACE slot probes: StartSlot %s, EndSlot %s", okS ? "installed" : (whyS ? whyS : "not found"), okE ? "installed" : (why ? why : "not found"));
}
void install_state_probe() {
    if (g_stateProbeTried) return;
    g_stateProbeTried = true;
    install_slot_probes();
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    int n = 0;
    const uint8_t* scm = cm ? anchor::function_by_string(cm, "wh::combatmodule::C_CombatActor::StartCombatMode", &n) : nullptr;
    void* const* vft = cm ? anchor::find_vftable(cm, ".?AVC_CombatActor@combatmodule@wh@@", 0) : nullptr;
    anchor::Range text{};
    if (!scm || !vft || !anchor::section(cm, ".text", &text)) { logf("WO178-TRACE state probe not installed: StartCombatMode / the vftable not found"); return; }
    // the registered thunk: lea rax,[rip+X] where X = `mov rax,[rcx]; jmp [rax+disp32]` (48 8B 01 FF A0 d32)
    static uint8_t buf[0x600];
    const size_t want = static_cast<size_t>(text.end - scm) < sizeof buf ? static_cast<size_t>(text.end - scm) : sizeof buf;
    if (!copy_code(scm, buf, want)) return;
    uint32_t slot = 0;
    for (size_t i = 0; i + 7 <= want && !slot; ++i) {
        if (buf[i] != 0x48 || buf[i + 1] != 0x8D || buf[i + 2] != 0x05) continue;
        int32_t d = 0; std::memcpy(&d, buf + i + 3, 4);
        const uint8_t* th = scm + i + 7 + d;
        uint8_t tb[9]{};
        if (!text.contains(th) || !copy_code(th, tb, sizeof tb)) continue;
        if (!(tb[0] == 0x48 && tb[1] == 0x8B && tb[2] == 0x01 && tb[3] == 0xFF && tb[4] == 0xA0)) continue;
        // the State property's listener: registered on the list at model+0x58 (State +0x40, its list +0x18): lea rcx,[rbx+58h] follows
        bool stateList = false;
        for (size_t k = i + 7; k + 4 <= want && k < i + 0x50; ++k)
            if (buf[k] == 0x48 && buf[k + 1] == 0x8D && buf[k + 2] == 0x4B && buf[k + 3] == 0x58) { stateList = true; break; }
        if (stateList) std::memcpy(&slot, tb + 5, 4);
    }
    if (!slot || slot > 0x2000 || (slot & 7)) { logf("WO178-TRACE state probe not installed: StartCombatMode's registered thunk not found"); return; }
    void* fn = nullptr;
    if (!read_ptr(vft, static_cast<int>(slot), &fn) || !fn || !text.contains(fn)) { logf("WO178-TRACE state probe not installed: vftable slot unreadable"); return; }
    const char* why = nullptr;
    if (!inlinehook::install_gate4(fn, hookpro::kStateHandler, sizeof hookpro::kStateHandler, &state_probe_cb, &why)) {
        logf("WO178-TRACE state probe not installed: %s", why ? why : "?");
        return;
    }
    g_stateProbe.store(true);
    char a[64];
    anchor::describe(fn, a, sizeof a);
    logf("WO178-TRACE state probe installed: C_CombatActor state handler=%s (StartCombatMode's registered thunk -> vftable slot 0x%X)", a, slot);
}
// the combat actor -> entity name (its owner C_Actor at +0x2D8, the entity id at +0x30), "me" for the local player
std::string ca_name(const void* ca) {
    void* me = motion::player_combat_actor();
    if (ca && (ca == me || static_cast<const char*>(ca) - 8 == me)) return "me";
    void* owner = nullptr; uint32_t eid = 0;
    if (!ca || !read_ptr(ca, 0x2D8, &owner) || !owner) return "?";
    KCDMP_FAULT_READ(site, "wo178::ca_name");
    if (!fault::guarded(site, [&] { eid = *reinterpret_cast<const uint32_t*>(static_cast<const char*>(owner) + 0x30); })) return "?";
    return name_of(eid);
}

// ---- the duel trace ----
double g_traceT0 = 0;
std::string g_traceLastMe, g_traceLastOpp;
struct TraceAct { double t; uint8_t cls; bool player; uint32_t eid; };
std::vector<TraceAct> g_traceActs;
constexpr uint32_t kTracePlayerEntity = 0x7777;   // the player entity id every save binds to (WO-125)
const char* cls_name(uint8_t c) {
    switch (c) { case 0: return "Attack"; case 1: return "Dodge"; case 2: return "PerfectBlock"; case 3: return "Block"; case 4: return "Hit";
                 case 5: return "SyncAttack"; default: return "?"; }
}
std::string model_text(const motion::ModelRead& m, bool attacker) {
    char b[200];
    if (attacker)
        std::snprintf(b, sizeof b, "state=0x%X at=%d az=%d as=%.2f ah=%d pb=%u bz=%d gz=%d cm=%u", static_cast<unsigned>(m.state), m.atkType, m.atkZone,
                      m.atkStrength, m.atkHand, m.perfectBlock, m.blockZone, m.guardZone, m.combatMode);
    else
        std::snprintf(b, sizeof b, "state=0x%X gz=%d bz=%d bm=%d bh=%d pb=%u at=%d az=%d cm=%u", static_cast<unsigned>(m.state), m.guardZone, m.blockZone,
                      m.blockMode, m.blockHand, m.perfectBlock, m.atkType, m.atkZone, m.combatMode);
    return b;
}
// research: a model's named property blocks (the name pointer at block+0x30, the value at +8) -- once per opponent, and +0x5C0 each sample
bool prop_name_at(void* model, size_t off, char* out, size_t n) {
    void* s = nullptr;
    out[0] = 0;
    if (!read_ptr(model, static_cast<int>(off + 0x30), &s) || !s || !fault::plausible_address(reinterpret_cast<uintptr_t>(s))) return false;
    KCDMP_FAULT_READ(site, "wo178::prop_name");
    size_t i = 0;
    const bool ok = fault::guarded(site, [&] { for (; i + 1 < n; ++i) { const char c = static_cast<const char*>(s)[i]; if (!c) break; out[i] = c; } });
    out[i] = 0;
    if (!ok || i < 3) return false;
    for (size_t k = 0; k < i; ++k) if (!((out[k] >= 'A' && out[k] <= 'Z') || (out[k] >= 'a' && out[k] <= 'z') || (out[k] >= '0' && out[k] <= '9') || out[k] == '_')) return false;
    return true;
}
float prop_float(void* model, size_t off) {
    float f = 0; KCDMP_FAULT_READ(site, "wo178::prop_float");
    fault::guarded(site, [&] { f = *reinterpret_cast<const float*>(static_cast<const char*>(model) + off + 8); });
    return f;
}
void dump_props(const char* who, void* model) {
    char nm[48];
    for (size_t off = 0; off < 0x1400; off += 8) {
        if (!prop_name_at(model, off, nm, sizeof nm)) continue;
        int32_t iv = 0; KCDMP_FAULT_READ(site, "wo178::dump_props");
        fault::guarded(site, [&] { iv = *reinterpret_cast<const int32_t*>(static_cast<const char*>(model) + off + 8); });
        logf("WO178-TRACE props %s off=0x%zX name=%s f=%.3f i=%d", who, off, nm, prop_float(model, off), iv);
    }
}
std::unordered_map<uint32_t, bool> g_propsDumped;

void trace_tick(double now) {
    if (now > g_traceUntil.load()) return;
    const double t = (now - g_traceT0) * 1000.0;
    std::vector<TraceAct> acts;
    std::vector<StateCall> calls;
    std::vector<SlotCall> slots;
    { std::lock_guard<std::mutex> lk(g_traceMx); acts.swap(g_traceActs); calls.swap(g_stateCalls); slots.swap(g_slotCalls); }
    for (const auto& c : slots) {
        void* owner = nullptr; void* att = nullptr;
        read_ptr(c.trig, 8, &owner);
        read_ptr(c.trig, 0x38, &att);
        logf("WO178-TRACE t=%.0f slot%s class=%s owner=%s attacker=%s arg=%d trig=%p", (c.t - g_traceT0) * 1000.0, c.start ? "START" : "END",
             rtti_name(c.trig).c_str(), ca_name(owner).c_str(), att ? ca_name(att).c_str() : "-", c.arg, c.trig);
    }
    for (const auto& c : calls)
        logf("WO178-TRACE t=%.0f statehandler actor=%s 0x%X->0x%X", (c.t - g_traceT0) * 1000.0, ca_name(c.ca).c_str(), static_cast<unsigned>(c.oldS),
             static_cast<unsigned>(c.newS));
    for (const auto& a : acts)
        logf("WO178-TRACE t=%.0f action=%s by=%s", (a.t - g_traceT0) * 1000.0, cls_name(a.cls), a.player ? "me" : name_of(a.eid));
    motion::ModelRead me{};
    if (!motion::read_model(kTracePlayerEntity, &me)) return;
    std::string mt = model_text(me, false);
    {   // the defender's own windows (named blocks: BlockSlot 0x998, PerfectBlockSlot 0x9D8, MasterStrikeSlot 0xA58, RiposteSlot 0xAD8)
        void* pca = nullptr; void* pm = nullptr;
        if (motion::player_combat_parts(&pca, &pm) && pm) {
            char x[160]; int n = 0;
            const struct { size_t off; const char* nm; const char* shortNm; } slots[] = {
                {0x998, "BlockSlot", "bs"}, {0x9D8, "PerfectBlockSlot", "pbs"}, {0xA58, "MasterStrikeSlot", "mss"}, {0xAD8, "RiposteSlot", "rs"}};
            for (const auto& sl : slots) {
                char nm[32];
                if (!prop_name_at(pm, sl.off, nm, sizeof nm) || std::strcmp(nm, sl.nm) != 0) continue;
                int32_t v = 0; KCDMP_FAULT_READ(site, "wo178::slot_read");
                fault::guarded(site, [&] { v = *reinterpret_cast<const int32_t*>(static_cast<const char*>(pm) + sl.off + 8); });
                n += std::snprintf(x + n, sizeof x - n, " %s=%d", sl.shortNm, v);
            }
            mt += x;
            // the combat cursor's own state (C_CombatPlayerController at the player's +0x568, its UI at +0x30): the shield's state machine
            // (+0x94; 1 = the shield), the cursor's slot icon (+0x48; 7 = the shield) and its flags (+0x4C; bit 2 shows the slot icon), the
            // indicator settings (+0x38 -> +0x11 off, +0x15)
            void* ctrl = nullptr; void* ui = nullptr; void* cfg = nullptr;
            int32_t st94 = -99, ic48 = -99, fl4c = -99; uint8_t c11 = 0xFF, c15 = 0xFF;
            if (read_ptr(pca, 0x568, &ctrl) && ctrl && read_ptr(ctrl, 0x30, &ui) && ui) {
                KCDMP_FAULT_READ(usite, "wo178::ui_read");
                fault::guarded(usite, [&] {
                    st94 = *reinterpret_cast<const int32_t*>(static_cast<const char*>(ui) + 0x94);
                    ic48 = *reinterpret_cast<const int32_t*>(static_cast<const char*>(ui) + 0x48);
                    fl4c = *reinterpret_cast<const int32_t*>(static_cast<const char*>(ui) + 0x4C);
                });
                if (read_ptr(ctrl, 0x38, &cfg) && cfg)
                    fault::guarded(usite, [&] { c11 = *(static_cast<const uint8_t*>(cfg) + 0x11); c15 = *(static_cast<const uint8_t*>(cfg) + 0x15); });
            }
            char u[96];
            std::snprintf(u, sizeof u, " ui94=%d ui48=%d ui4c=%d cfg11=%u cfg15=%u", st94, ic48, fl4c, c11, c15);
            mt += u;
        }
    }
    std::string ot = "-";
    motion::ModelRead op{};
    if (me.opponentEid && motion::read_model(me.opponentEid, &op)) {
        ot = std::string(name_of(me.opponentEid)) + " " + model_text(op, true);
        void* oca = nullptr; void* om = nullptr;
        if (motion::combat_parts(me.opponentEid, false, &oca, &om) && om) {
            char nm[48] = "?";
            prop_name_at(om, 0x5C0, nm, sizeof nm);
            char x[96]; std::snprintf(x, sizeof x, " p5c0=%s:%.3f", nm, prop_float(om, 0x5C0));
            ot += x;
            if (!g_propsDumped[me.opponentEid]) { g_propsDumped[me.opponentEid] = true; dump_props(name_of(me.opponentEid), om); }
        }
    }
    if (mt != g_traceLastMe || ot != g_traceLastOpp) {
        logf("WO178-TRACE t=%.0f me %s | opp %s", t, mt.c_str(), ot.c_str());
        g_traceLastMe = mt; g_traceLastOpp = ot;
    }
}

}   // namespace

bool trace_on() { return now_s() <= g_traceUntil.load(); }
void trace_action(uint8_t cls, bool isPlayer, uint32_t npcEid) {
    std::lock_guard<std::mutex> lk(g_traceMx);
    if (g_traceActs.size() < 4096) g_traceActs.push_back(TraceAct{now_s(), cls, isPlayer, npcEid});
}

uint8_t save_manual(const char* why) {
    why = why && *why ? why : "?";
    if (!resolve_save()) {
        logf("WO178-SAVE result=not-armed why=%s reason=%s", why, g_saveWhy);
        return 2;
    }
    void* mgr = manager();
    if (!mgr) {
        logf("WO178-SAVE result=not-armed why=%s reason=no C_PlayerProfileWHManager among the game interface's pointers", why);
        return 2;
    }
    KCDMP_FAULT_CALL(site, "wo178::save_manual");
    bool can = false, ok = false;
    if (!fault::guarded(site, [&] { can = g_canSave(mgr, kSaveTypeManual); })) {
        c_saveFault.fetch_add(1);
        logf("WO178-SAVE result=fault why=%s (CanSave)", why);
        return 3;
    }
    if (!can) {
        c_saveRefused.fetch_add(1);
        logf("WO178-SAVE result=refused why=%s -- the engine cannot save now (a lock, a scene, a fight, the menu): CanSave(ManualSave) false", why);
        g_lastSave = std::string("refused:") + why;
        return 0;
    }
    if (!fault::guarded(site, [&] { ok = g_initiate(mgr, kSaveTypeManual, -1, nullptr); })) {
        c_saveFault.fetch_add(1);
        logf("WO178-SAVE result=fault why=%s (InitiateSaveGame)", why);
        return 3;
    }
    if (!ok) {
        c_saveRefused.fetch_add(1);
        logf("WO178-SAVE result=refused why=%s -- InitiateSaveGame(ManualSave) declined (another save pending, or a script lock)", why);
        g_lastSave = std::string("declined:") + why;
        return 0;
    }
    c_saveReq.fetch_add(1);
    char a[64];
    anchor::describe(reinterpret_cast<const void*>(g_initiate), a, sizeof a);
    logf("WO178-SAVE result=requested why=%s type=ManualSave overwrite=-1 -- the schnapps save without the item (InitiateSaveGame %s, manager at "
         "game interface +0x%X by RTTI); the file follows when the engine writes it", why, a, g_mgrOff);
    g_lastSave = std::string("requested:") + why;
    return 1;
}

SaveState save_state() {
    SaveState s{false, false, false, false};
    if (!resolve_save() || !g_isAnyPending) return s;
    void* mgr = manager();
    if (!mgr) return s;
    KCDMP_FAULT_CALL(site, "wo184::save_state");
    bool pending = false, saving = false, can = false;
    if (!fault::guarded(site, [&] { pending = g_isAnyPending(mgr); })) return s;
    if (g_isSaving && !fault::guarded(site, [&] { saving = g_isSaving(mgr); })) saving = false;
    if (!fault::guarded(site, [&] { can = g_canSave(mgr, kSaveTypeQuick); })) can = false;
    s.armed = true; s.pending = pending; s.saving = saving; s.canQuick = can;
    return s;
}

uint8_t save_cancel(const char* why, bool* pendingBefore, bool* pendingAfter) {
    why = why && *why ? why : "?";
    *pendingBefore = *pendingAfter = false;
    if (!resolve_save() || !g_isAnyPending || !g_cancelPending) {
        logf("WO184-SAVE cancel result=not-armed why=%s -- IsAnySaveGamePending / CancelPendingSaveGame not found", why);
        return 2;
    }
    void* mgr = manager();
    if (!mgr) { logf("WO184-SAVE cancel result=not-armed why=%s -- no C_PlayerProfileWHManager among the game interface's pointers", why); return 2; }
    KCDMP_FAULT_CALL(site, "wo184::save_cancel");
    bool before = false, after = false;
    if (!fault::guarded(site, [&] { before = g_isAnyPending(mgr); })) { logf("WO184-SAVE cancel result=fault why=%s (IsAnySaveGamePending)", why); return 3; }
    *pendingBefore = before;
    if (!before) { logf("WO184-SAVE cancel result=nothing-pending why=%s -- no save is pending: nothing called", why); return 0; }
    if (!fault::guarded(site, [&] { g_cancelPending(mgr); })) { logf("WO184-SAVE cancel result=fault why=%s (CancelPendingSaveGame)", why); return 3; }
    if (!fault::guarded(site, [&] { after = g_isAnyPending(mgr); })) after = true;
    *pendingAfter = after;
    c_cancels.fetch_add(1);
    logf("WO184-SAVE cancel result=%s why=%s pending_before=1 pending_after=%d -- the engine's own CancelPendingSaveGame (the call LoadSavedGame makes before "
         "every load): its BeforeGameSave wait is dropped and E_MMI_SaveGameRequestEnd sent [n=%u]", after ? "STILL-PENDING" : "cancelled", why, after ? 1 : 0, c_cancels.load());
    return after ? 4 : 1;
}

void install_clinch() {
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    int n = 0;
    const uint8_t* fn = cm ? anchor::function_by_string(cm, kClinchLabel, &n) : nullptr;
    if (!fn) { logf("WO178-CLINCH guard not armed: StartSyncGuard's label is not referenced by exactly one function (%d)", n); return; }
    const char* why = nullptr;
    if (!inlinehook::install_gate4(const_cast<uint8_t*>(fn), hookpro::kClinchStart, sizeof hookpro::kClinchStart, &clinch_gate, &why)) {
        logf("WO178-CLINCH guard not armed: the patch was refused (%s)", why ? why : "?");
        return;
    }
    g_clinchArmed.store(true);
    char a[64];
    anchor::describe(fn, a, sizeof a);
    logf("WO178-CLINCH guard armed: StartSyncGuard=%s (label + the registry's prologue) -- no clinch between the local player and a listed copy", a);
}

void install() {
    install_clinch();
    if (!resolve_hook()) { logf("WO178-HOOK not armed: %s -- the opponent picker keeps the game's own hostility (no lock on copies)", g_why); return; }
    const char* why = nullptr;
    void* orig = nullptr;
    if (!inlinehook::install_detour(g_test, hookpro::kHostileTest, sizeof hookpro::kHostileTest, reinterpret_cast<void*>(&hostile_detour), &orig, &why)) {
        g_why = "the patch was refused";
        logf("WO178-HOOK not armed: %s (%s)", g_why, why ? why : "?");
        return;
    }
    g_orig = reinterpret_cast<HostileFn>(orig);
    g_armed.store(true);
    g_why = "armed";
    char a[64];
    anchor::describe(g_test, a, sizeof a);
    logf("WO178-HOOK armed: hostility test=%s (HandleOpponentSelection's label -> its selector's combat-RPG call; prologue %u bytes from the "
         "registry), combat RPG at combat actor +0x%X, owner at RPG +0x%X -- answers only for the local player and listed copies",
         a, static_cast<unsigned>(sizeof hookpro::kHostileTest), g_rpgOff, g_ownerOff);
}

bool host_answer_on() { return g_isHost.load() && g_hostAnswer.load() && g_armed.load() && !g_off.load(); }
bool hold_on() { return g_hold.load(); }
bool hit_reach_on() { return g_hitReach.load(); }
bool own_block_on() { return g_ownBlock.load(); }
bool copy_engine_state_on() { return g_copyEngineState.load(); }
bool pb_at_contact_on() { return g_pbAtContact.load(); }
bool engine_cap_on() { return g_engineCap.load(); }

void offer_local(uint32_t eid) {
    if (!eid) return;
    std::lock_guard<std::mutex> lk(g_offerMx);
    Offer& o = g_offers[eid];
    o.why = static_cast<uint8_t>(Why::FigureFight); o.down = false; o.at = now_s(); o.local = true;
}

void tick() {
    if (!g_mainTid) g_mainTid = GetCurrentThreadId();
    const double now = now_s();
    trace_tick(now);
    if (g_off.load() && !g_offSaid) {
        g_offSaid = true;
        logf("WO178-HOSTILE switched to pass-through for this session: the test's owner could not be read (faults=%u) -- the game's own answer "
             "for every candidate until the game restarts", c_faults.load());
    }
    {
        static uint32_t s_clinchSaid = 0;
        const uint32_t r = c_clinchRefused.load();
        if (r != s_clinchSaid) {
            s_clinchSaid = r;
            logf("WO178-CLINCH refused npc=%s refused=%u asked=%u -- a copy cannot take its half of the clinch (its body plays the host's swings)",
                 name_of(g_clinchLastEid.load()), r, c_clinchAsked.load());
        }
    }
    if (!g_armed.load() || now < g_next) return;
    g_next = now + 0.25;
    rebuild(now);
    say_rows(now);
    if (now - g_statsAt >= 60.0) {
        const uint32_t a = c_asked.load(), l = c_askedLocal.load(), y = c_yes.load(), li = c_listed.load(), gy = c_gameYes.load(), gn = c_gameNo.load();
        if (g_statsAt > 0 && (a != s_asked0 || y != s_yes0))
            logf("WO178-HOSTILE-STATS per_min asked=%u local_player=%u listed=%u answered_yes=%u game_yes=%u game_no=%u rows=%u offers=%u withdrawn=%u "
                 "no_body=%u role=%s joiner_lock=%s host_answer=%s pass_through=%s",
                 a - s_asked0, l - s_local0, li - s_listed0, y - s_yes0, gy - s_gy0, gn - s_gn0, c_rows.load(), c_offers.load(), c_withdrawn.load(),
                 c_noBody.load(), g_isHost.load() ? "host" : "joiner", g_joinerLock.load() ? "on" : "off", g_hostAnswer.load() ? "on" : "off",
                 g_off.load() ? "YES" : "no");
        s_asked0 = a; s_local0 = l; s_yes0 = y; s_listed0 = li; s_gy0 = gy; s_gn0 = gn;
        g_statsAt = now;
    }
}

int status_text(char* out, int n) {
    const int cur = g_cur.load();
    char rows[600] = "";
    int len = 0;
    if (cur >= 0) {
        const Table& t = g_tables[cur];
        for (int i = 0; i < t.n && len < static_cast<int>(sizeof rows) - 80; ++i) {
            const Slot& s = g_slots[t.rows[i].slot & 63];
            len += std::snprintf(rows + len, sizeof rows - len, " %s:%s%s%s/y%u/n%u", name_of(t.rows[i].eid), why_name(static_cast<Why>(t.rows[i].why)),
                                 t.rows[i].ex ? ":" : "", t.rows[i].ex ? ex_name(static_cast<Ex>(t.rows[i].ex)) : "", s.yes.load(), s.gameNo.load());
        }
    }
    return std::snprintf(out, n, "hook=%s%s role=%s joiner_lock=%s host_answer=%s hold=%s engine_cap=%s asked=%u local=%u listed=%u yes=%u game_yes=%u "
                         "game_no=%u foreign=%u faults=%u owner=%p me=%p cand=%p rows=%u%s save=%s(%s) saves=%u refused=%u last=%s clinch=%s(%s) clinch_asked=%u clinch_refused=%u reach=%s own_block=%s",
                         g_armed.load() ? "armed" : g_why, g_off.load() ? "(PASS-THROUGH)" : "", g_isHost.load() ? "host" : "joiner",
                         g_joinerLock.load() ? "on" : "off", g_hostAnswer.load() ? "on" : "off", g_hold.load() ? "on" : "off",
                         g_engineCap.load() ? "on" : "off", c_asked.load(), c_askedLocal.load(), c_listed.load(), c_yes.load(), c_gameYes.load(),
                         c_gameNo.load(), c_foreign.load(), c_faults.load(), g_lastOwner.load(), g_lastMe.load(), g_lastCand.load(), c_rows.load(), rows, g_saveArmed ? "armed" : "unresolved", g_saveWhy,
                         c_saveReq.load(), c_saveRefused.load(), g_lastSave.c_str(), g_clinchArmed.load() ? "armed" : "not-armed",
                         g_clinchGuard.load() ? "on" : "off", c_clinchAsked.load(), c_clinchRefused.load(), g_hitReach.load() ? "on" : "off",
                         g_ownBlock.load() ? "on" : "off");
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    switch (op) {
        case kOpOffer: {
            if (len != 7 || cap < 1) return wo163::kRBadRequest;
            uint32_t eid = 0;
            std::memcpy(&eid, body + 1, 4);
            const uint8_t why = body[5];
            if (!eid || (why != 0 && !why_valid(why))) return wo163::kRBadRequest;
            std::lock_guard<std::mutex> lk(g_offerMx);
            if (why == 0) { if (g_offers.erase(eid)) c_withdrawn.fetch_add(1); }
            else {
                Offer& o = g_offers[eid];
                o.why = why; o.down = (body[6] & 1) != 0; o.at = now_s(); o.local = false;
                c_offers.fetch_add(1);
            }
            out[0] = static_cast<uint8_t>(g_offers.size() > 255 ? 255 : g_offers.size());
            *outLen = 1;
            return wo163::kROk;
        }
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : (cap ? cap - 1 : 0);
            return wo163::kROk;
        }
        case kOpConfig: {
            if (len < 6 || len > 11 || cap < 10) return wo163::kRBadRequest;
            auto set = [](std::atomic<bool>& a, uint8_t v, const char* nm) {
                if (v == 255) return;
                const bool b = v != 0;
                if (a.exchange(b) != b) logf("WO178-CONFIG %s %s", nm, b ? "on" : "off");
            };
            set(g_joinerLock, body[1], "mp_joiner_lock");
            set(g_hostAnswer, body[2], "mp_host_lock_answer");
            set(g_hold, body[3], "mp_fallback_hold");
            set(g_engineCap, body[4], "mp_fallback_engine_cap");
            if (body[5] != 255) set(g_isHost, body[5], "role=host");
            if (len >= 7) set(g_hitReach, body[6], "mp_hit_reach");
            if (len >= 8) set(g_clinchGuard, body[7], "mp_clinch_guard");
            if (len >= 9) set(g_ownBlock, body[8], "mp_own_block");
            if (len >= 10) set(g_copyEngineState, body[9], "mp_copy_engine_state");
            if (len >= 11) set(g_pbAtContact, body[10], "mp_pb_at_contact");
            out[0] = g_joinerLock.load(); out[1] = g_hostAnswer.load(); out[2] = g_hold.load(); out[3] = g_engineCap.load(); out[4] = g_isHost.load();
            out[5] = g_hitReach.load(); out[6] = g_clinchGuard.load(); out[7] = g_ownBlock.load(); out[8] = g_copyEngineState.load(); out[9] = g_pbAtContact.load();
            *outLen = len - 1;
            return wo163::kROk;
        }
        case kOpTrace: {
            if (len != 3 || cap < 1) return wo163::kRBadRequest;
            uint16_t secs = 0;
            std::memcpy(&secs, body + 1, 2);
            if (secs > 600) secs = 600;
            g_traceT0 = now_s();
            g_traceLastMe.clear(); g_traceLastOpp.clear();
            g_traceUntil.store(secs ? g_traceT0 + secs : 0.0);
            if (secs) install_state_probe();   // research: once, on the pipe thread (the patch suspends the others)
            logf("WO178-TRACE %s for %u s (read-only: the player's and his opponent's combat models every frame, the actions entering)",
                 secs ? "started" : "stopped", secs);
            out[0] = 1;
            *outLen = 1;
            return wo163::kROk;
        }
        case kOpSaveManual: {
            if (len < 2 || len != static_cast<size_t>(2 + body[1]) || body[1] > 40 || cap < 2) return wo163::kRBadRequest;
            const std::string why(reinterpret_cast<const char*>(body + 2), body[1]);
            const uint8_t r = save_manual(why.c_str());
            out[0] = r;
            const int n = std::snprintf(reinterpret_cast<char*>(out + 1), cap - 1, "%s", r == 1 ? "requested" : r == 0 ? "refused" : r == 2 ? g_saveWhy : "fault");
            *outLen = 1 + ((n > 0 && static_cast<size_t>(n) < cap - 1) ? static_cast<size_t>(n) : 0);
            return wo163::kROk;
        }
        case kOpSaveState: {   // WO-184
            if (len != 1 || cap < 4) return wo163::kRBadRequest;
            const SaveState s = save_state();
            out[0] = s.armed ? 1 : 0; out[1] = s.pending ? 1 : 0; out[2] = s.saving ? 1 : 0; out[3] = s.canQuick ? 1 : 0;
            *outLen = 4;
            return wo163::kROk;
        }
        case kOpSaveCancel: {   // WO-184
            if (len < 2 || len != static_cast<size_t>(2 + body[1]) || body[1] > 40 || cap < 3) return wo163::kRBadRequest;
            const std::string why(reinterpret_cast<const char*>(body + 2), body[1]);
            bool before = false, after = false;
            out[0] = save_cancel(why.c_str(), &before, &after);
            out[1] = before ? 1 : 0; out[2] = after ? 1 : 0;
            *outLen = 3;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

} // namespace kcdmp::wo178
