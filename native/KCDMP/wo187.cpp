// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-187: 0.60.5's native half (wo187.h).
#include "wo187.h"
#include "wo187_rules.h"
#include "wo163.h"

#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "script_context.h"
#include "wo195.h"
#include "wo195_rules.h"

#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>

namespace kcdmp::wo187 {

namespace {

using namespace kcdmp::wo187rules;

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

constexpr uint32_t kPlayerEntity = 0x7777;   // the player entity id every save binds to (WO-125)
constexpr unsigned kLogFirst = 30;

// ---- switches ----------------------------------------------------------------------------------------------------------------
std::atomic<bool> g_logical{true}, g_pin{true}, g_guard{true}, g_idle{true}, g_defence{true}, g_combo{true}, g_scopeLog{false}, g_linger{true},
    g_chase{true}, g_avatarHit{true};

// ---- guarded reads -------------------------------------------------------------------------------------------------------------
template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    if (!base) return false;
    KCDMP_FAULT_READ(site, "wo187::read");
    if (!fault::enabled(site)) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}

// An object's class by its RTTI (vptr[-1] = the complete object locator; its type descriptor name at +0x10), "" when unreadable.
std::string rtti_name(const void* obj) {
    void* vt = nullptr; void* col = nullptr;
    if (!obj || !rd(obj, 0, &vt) || !vt || !rd(vt, -8, &col) || !col) return "";
    uint32_t sig = 0; int32_t tdRva = 0, selfRva = 0;
    if (!rd(col, 0, &sig) || sig != 1 || !rd(col, 12, &tdRva) || !rd(col, 20, &selfRva)) return "";
    const char* base = static_cast<const char*>(col) - selfRva;
    const char* td = base + tdRva + 16;
    char nm[96]{};
    for (int i = 0; i < 95; ++i) { char ch = 0; if (!rd(td, i, &ch) || !ch) break; nm[i] = ch; }
    return nm;
}
// ".?AVC_CombatAnimAction@combatmodule@wh@@" -> "C_CombatAnimAction"
std::string short_name(const std::string& r) {
    size_t a = r.rfind(".?AV", 0) == 0 ? 4 : (r.rfind(".?AU", 0) == 0 ? 4 : 0);
    size_t b = r.find('@', a);
    return r.substr(a, b == std::string::npos ? std::string::npos : b - a);
}

const char* name_of(uint32_t eid) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "?";
}
uint32_t player_eid() {
    void* e = engine::entity_by_id(kPlayerEntity);
    return e ? engine::entity_id(e) : 0;
}
std::string who(uint32_t eid) {
    if (!eid) return "none";
    if (eid == player_eid()) return "the-local-player";
    return name_of(eid);
}

// ---- A2: C_CombatActor::SetLogicalTarget ---------------------------------------------------------------------------------------
constexpr char kSltLabel[] = "wh::combatmodule::C_CombatActor::SetLogicalTarget";
using SltFn = void(__fastcall*)(void* ca, void* entity);
SltFn g_slt = nullptr;
bool g_sltResolved = false;
const char* g_sltWhy = "not resolved";
void* const* g_vftTarget = nullptr;   // C_CombatTarget (the target wrappers at model +0x1108 / +0x1110)
constexpr size_t kCaModel = 0x2F0, kModelTarget = 0x1108, kModelRequested = 0x1110;
std::atomic<uint32_t> c_sltCalls{0}, c_sltFaults{0};

bool resolve_slt() {
    if (g_sltResolved) return g_slt != nullptr;
    g_sltResolved = true;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    void* const* vft = cm ? anchor::find_vftable(cm, ".?AVC_CombatActor@combatmodule@wh@@", 0) : nullptr;
    int nl = 0;
    const char* label = cm ? anchor::find_cstring(cm, kSltLabel, &nl) : nullptr;
    anchor::Range text{};
    if (!vft || !label || !anchor::section(cm, ".text", &text)) { g_sltWhy = "C_CombatActor's vftable or the label not found"; logf("WO187-OPP not armed: %s", g_sltWhy); return false; }
    // the one vftable slot whose function references the engine's own label (the slot is found, never assumed)
    int found = 0; void* fn = nullptr; size_t slot = 0;
    for (size_t i = 0; i < 0x200; ++i) {
        void* f = nullptr;
        if (!rd(vft, static_cast<ptrdiff_t>(i * 8), &f) || !f || !text.contains(f)) break;
        if (anchor::function_refs(cm, f, label)) { if (f != fn) ++found; fn = f; slot = i * 8; }
    }
    if (found != 1) { g_sltWhy = "no single vftable slot references the label"; logf("WO187-OPP not armed: %s (%d)", g_sltWhy, found); return false; }
    g_slt = reinterpret_cast<SltFn>(fn);
    g_vftTarget = anchor::find_vftable(cm, ".?AVC_CombatTarget@combatmodule@wh@@", 0);
    g_sltWhy = "armed";
    char d[64];
    anchor::describe(fn, d, sizeof d);
    logf("WO187-OPP armed: C_CombatActor::SetLogicalTarget=%s (vftable slot 0x%zX, found by its label); the target wrapper's class %s -- a copy's "
         "opponent is set the engine's way (requested target + target + opponent)", d, slot, g_vftTarget ? "found" : "NOT found (read through the opponent)");
    return true;
}

// The entity a target wrapper (model +0x1108 / +0x1110) names: the wrapper's own vftable +0x08 (the engine's read in SetTarget). The wrapper
// must be a C_CombatTarget (its vptr) or nothing is called.
bool wrapper_entity(void* model, size_t off, void** ent) {
    *ent = nullptr;
    void* w = nullptr;
    if (!rd(model, static_cast<ptrdiff_t>(off), &w)) return false;
    if (!w) return true;   // none
    void* vp = nullptr;
    if (!g_vftTarget || !rd(w, 0, &vp) || vp != static_cast<const void*>(g_vftTarget)) return false;
    void* fn = nullptr;
    if (!rd(vp, 8, &fn) || !fn) return false;
    KCDMP_FAULT_CALL(site, "wo187::target_entity");
    return fault::guarded(site, [&] { *ent = reinterpret_cast<void*(__fastcall*)(void*)>(fn)(w); });
}

// ---- A2: the wishes ------------------------------------------------------------------------------------------------------------
struct Wish {
    Opp kind = Opp::None;
    uint32_t target = 0;       // the figure's entity id (kind Figure); 0 for the local player
    uint8_t why = 0;
    double refreshedAt = 0, checkedAt = 0;
    bool applied = false, pinned = false;
    int overwrites = 0;
    void* pinFrom = nullptr; void* pinTo = nullptr;
    uint32_t windups = 0;
};
std::unordered_map<uint32_t, Wish> g_wishes;   // main thread
std::atomic<uint32_t> c_wishes{0}, c_applied{0}, c_overwritten{0}, c_pinned{0}, c_windupHeld{0}, c_windupOver{0}, c_windupNoWish{0}, c_dropped{0},
    c_applyFail{0}, c_planWindups{0}, c_planPlanned{0}, c_planRefused{0}, c_planNotMe{0};

const char* why_name(uint8_t w) {
    switch (w) {
        case 1: return "host-npc-targets-me"; case 2: return "host-npc-targets-the-host"; case 3: return "host-npc-targets-another-player";
        case 4: return "engaged"; case 5: return "paired-move"; case 6: return "test"; default: return "?";
    }
}

void* wish_entity(const Wish& w) {
    if (w.kind == Opp::LocalPlayer) return engine::entity_by_id(kPlayerEntity);
    if (w.kind == Opp::Figure && w.target) return engine::entity_by_id(w.target);
    return nullptr;
}

// The copy's requested target as an entity id (the engine's requested target if the wrapper reads, else its opponent's).
uint32_t current_eid(uint32_t copy, void* model, bool* viaRequested) {
    void* ent = nullptr;
    if (wrapper_entity(model, kModelRequested, &ent)) { *viaRequested = true; return ent ? engine::entity_id(ent) : 0; }
    *viaRequested = false;
    motion::ModelRead m{};
    return motion::read_model(copy, &m) ? m.opponentEid : 0;
}

void unpin(uint32_t copy, Wish& w, const char* why) {
    if (!w.pinned) return;
    if (w.pinFrom && w.pinTo && hits::soul_of_eid(copy) == w.pinFrom) sctx::set_soul_relation(w.pinFrom, w.pinTo, "combat_forcedTarget", false);
    logf("WO187-OPP npc=%s pin cleared (%s)", name_of(copy), why);
    w.pinned = false; w.pinFrom = w.pinTo = nullptr;
}

// One wish against the copy's model: applied, or written back when something else wrote it. Main thread.
void apply_wish(uint32_t copy, Wish& w, double now, const char* moment) {
    w.checkedAt = now;
    if (wo195::dead_gate(copy, "wish")) return;   // WO-195: never applied or written back on a dead copy (0.60.5 did, in the death's own frame)
    void* ent = wish_entity(w);
    void* ca = nullptr; void* model = nullptr;
    if (!ent || !motion::combat_parts(copy, false, &ca, &model)) return;
    const uint32_t want = engine::entity_id(ent);
    bool viaReq = false;
    const uint32_t was = current_eid(copy, model, &viaReq);
    if (was == want) { w.applied = true; return; }
    if (!set_logical_target(ca, ent)) { c_applyFail.fetch_add(1); return; }
    bool v2 = false;
    const uint32_t now2 = current_eid(copy, model, &v2);
    if (!w.applied) {
        w.applied = true;
        c_applied.fetch_add(1);
        logf("WO187-OPP npc=%s was=%s now=%s why=%s%s (%s)", name_of(copy), who(was).c_str(), who(now2).c_str(), why_name(w.why),
             now2 == want ? "" : " -- NOT taken yet (the engine switches when the copy is idle or in guard)", moment);
        return;
    }
    ++w.overwrites;
    c_overwritten.fetch_add(1);
    if (c_overwritten.load() <= kLogFirst || c_overwritten.load() % 50 == 0)
        logf("WO187-OPP npc=%s overwritten (was=%s, wished %s) -- written back (#%d this wish, %s)", name_of(copy), who(was).c_str(), who(want).c_str(), w.overwrites, moment);
    if (pin_due(w.overwrites, w.pinned, g_pin.load())) {
        void* fs = hits::soul_of_eid(copy);
        void* ts = hits::soul_of_eid(want);
        const int r = fs && ts ? sctx::set_soul_relation(fs, ts, "combat_forcedTarget", true) : -1;
        if (r >= 0) { w.pinned = true; w.pinFrom = fs; w.pinTo = ts; c_pinned.fetch_add(1); }
        logf("WO187-OPP npc=%s pinned to %s with combat_forcedTarget after %d overwrites -> %s", name_of(copy), who(want).c_str(), w.overwrites,
             r == 1 ? "set (read back)" : r == 0 ? "already set" : "NOT set (the context manager refused)");
    }
}

// ---- C2: the full-body scope reader ------------------------------------------------------------------------------------------
// The actor's IActionController: C_Actor vftable +0x2D0 GetAnimatedActor, then its +0x130 GetActionController (mannequin_read.cpp's
// route, WO-100); the controller must be a CActionController (its RTTI-found vftable). Scope `id` = [ctrl+0x40] + id * 0x168, checked to
// be a CActionScope (its vptr) whose name reads back; its installed action = [scope+0x148] (the scope's own getters).
constexpr size_t kVtGetAnimatedActor = 0x2D0, kVtGetActionController = 0x130;
constexpr size_t kCtrlScopeCount = 0x3C, kCtrlScopes = 0x40, kScopeStride = 0x168, kScopeName = 0x08, kScopeAction = 0x148;
constexpr size_t kActPriority = 0x24, kActFlags = 0x2C, kActFragment = 0x38;
const void* g_vftCtrl = nullptr; const void* g_vftScope = nullptr;
bool g_scopeResolved = false, g_scopeArmed = false;
std::atomic<uint32_t> c_scopeReads{0}, c_scopeRefused{0};

bool resolve_scope() {
    if (g_scopeResolved) return g_scopeArmed;
    g_scopeResolved = true;
    HMODULE ca = GetModuleHandleA("CryAction.dll");
    g_vftCtrl = ca ? anchor::find_vftable(ca, ".?AVCActionController@@", 0) : nullptr;
    g_vftScope = ca ? anchor::find_vftable(ca, ".?AVCActionScope@@", 0) : nullptr;
    g_scopeArmed = g_vftCtrl && g_vftScope;
    logf("WO187-SCOPE reader %s: CActionController %s, CActionScope %s (read-only: the full-body scope's installed action)", g_scopeArmed ? "armed" : "NOT armed",
         g_vftCtrl ? "found" : "not found", g_vftScope ? "found" : "not found");
    return g_scopeArmed;
}

void* call_get(void* obj, size_t slot) {
    void* vt = nullptr; void* fn = nullptr; void* r = nullptr;
    if (!rd(obj, 0, &vt) || !vt || !rd(vt, static_cast<ptrdiff_t>(slot), &fn) || !fn) return nullptr;
    KCDMP_FAULT_CALL(site, "wo187::scope_get");
    if (!fault::guarded(site, [&] { r = reinterpret_cast<void*(__fastcall*)(void*)>(fn)(obj); })) return nullptr;
    return r;
}

struct ScopeRead { bool ok = false; bool empty = true; std::string action; int32_t prio = 0; uint32_t flags = 0, frag = 0; };

// One named scope of the body's controller: scope `index` checked to be a CActionScope whose own name is `name` (FullBody is scope 0; the
// upper body is found by its name among the first scopes). Read-only.
ScopeRead read_scope(uint32_t eid, const char* name, int firstIndex, int lastIndex) {
    ScopeRead s;
    if (!resolve_scope()) return s;
    void* actor = motion::actor_of(eid);
    void* anim = actor ? call_get(actor, kVtGetAnimatedActor) : nullptr;
    void* ctrl = anim ? call_get(anim, kVtGetActionController) : nullptr;
    void* vp = nullptr;
    if (!ctrl || !rd(ctrl, 0, &vp) || vp != g_vftCtrl) { c_scopeRefused.fetch_add(1); return s; }
    int32_t count = 0; char* scopes = nullptr;
    if (!rd(ctrl, kCtrlScopeCount, &count) || count < 1 || count > 64 || !rd(ctrl, kCtrlScopes, &scopes) || !scopes) { c_scopeRefused.fetch_add(1); return s; }
    for (int idx = firstIndex; idx <= lastIndex && idx < count; ++idx) {
        char* sc = scopes + static_cast<ptrdiff_t>(idx) * kScopeStride;
        void* sv = nullptr; const char* nmp = nullptr; char nm[16]{};
        if (!rd(sc, 0, &sv) || sv != g_vftScope || !rd(sc, kScopeName, &nmp) || !nmp) { c_scopeRefused.fetch_add(1); return s; }
        for (int i = 0; i < 15; ++i) { char ch = 0; if (!rd(nmp, i, &ch)) break; nm[i] = ch; if (!ch) break; }
        if (std::strcmp(nm, name) != 0) continue;
        void* act = nullptr;
        if (!rd(sc, kScopeAction, &act)) { c_scopeRefused.fetch_add(1); return s; }
        s.ok = true;
        c_scopeReads.fetch_add(1);
        if (!act) return s;
        s.empty = false;
        s.action = short_name(rtti_name(act));
        rd(act, kActPriority, &s.prio);
        rd(act, kActFlags, &s.flags);
        rd(act, kActFragment, &s.frag);
        return s;
    }
    return s;
}
ScopeRead read_fullbody(uint32_t eid) { return read_scope(eid, "FullBody", 0, 0); }
ScopeRead read_upperbody(uint32_t eid) { return read_scope(eid, "UpperBody", 1, 8); }

// WO-190 0.1: every scope of the body's controller in one read (the same checks: the controller's vptr, each scope's vptr and name).
// WO-190 0.1: the controller's tag state (its context +0x10, 20 bytes -- WO-100's read: GetContext = IActionController vtable +0xB0).
bool read_tag_state(uint32_t eid, uint8_t* out, size_t n) {
    if (!resolve_scope() || n > 20) return false;
    void* actor = motion::actor_of(eid);
    void* anim = actor ? call_get(actor, kVtGetAnimatedActor) : nullptr;
    void* ctrl = anim ? call_get(anim, kVtGetActionController) : nullptr;
    void* vp = nullptr;
    if (!ctrl || !rd(ctrl, 0, &vp) || vp != g_vftCtrl) return false;
    void* ctx = call_get(ctrl, 0xB0);
    if (!ctx) return false;
    for (size_t i = 0; i < n; ++i) if (!rd(ctx, static_cast<ptrdiff_t>(0x10 + i), &out[i])) return false;
    return true;
}

std::string scope_line(const ScopeRead& r) {
    if (!r.ok) return "unreadable";
    if (r.empty) return "action=none";
    char b[160];
    std::snprintf(b, sizeof b, "action=%s prio=%d flags=0x%X frag=%u", r.action.empty() ? "?" : r.action.c_str(), r.prio, r.flags, r.frag);
    return b;
}

// Bodies that just played a move: watched for kMoveWatchS (the scope log, the idle restore).
struct Move { double at = 0, emptySince = -1, actFirst = -1, actLeft = -1; bool restored = false, sawAction = false; std::string last; unsigned lines = 0; };
std::unordered_map<uint32_t, Move> g_moves;   // main thread
std::atomic<uint32_t> c_moves{0}, c_emptyAfter{0}, c_restoredCombat{0}, c_restoredPulse{0}, c_restoreFail{0};

void watch_moves(double now) {
    for (auto it = g_moves.begin(); it != g_moves.end();) {
        Move& m = it->second;
        const uint32_t eid = it->first;
        if (now - m.at > kMoveWatchS || !engine::entity_by_id(eid)) { it = g_moves.erase(it); continue; }
        const ScopeRead r = read_fullbody(eid);
        if (!r.ok) { ++it; continue; }
        std::string line = scope_line(r);
        if (g_scopeLog.load()) {   // and the upper body (an arm-only move -- a quick punch -- plays there, not on the full body)
            const ScopeRead u = read_upperbody(eid);
            if (u.ok) line += " ub:" + scope_line(u);
        }
        if (g_scopeLog.load() && line != m.last && m.lines < 40) {
            ++m.lines;
            logf("WO187-SCOPE body=%s t=%.0f %s", name_of(eid), (now - m.at) * 1000.0, line.c_str());
        }
        m.last = line;
        // C1: the move's own action (priority >= 5) on the full body -- or, when the full body has none, the upper body -- seen / gone
        bool act = !r.empty && r.prio >= 5;
        if (!act && m.actLeft < 0) { const ScopeRead u = read_upperbody(eid); act = u.ok && !u.empty && u.prio >= 5; }
        if (act && m.actFirst < 0) m.actFirst = now - m.at;
        if (!act && m.actFirst >= 0 && m.actLeft < 0) m.actLeft = now - m.at;
        if (!r.empty) { m.sawAction = true; m.emptySince = -1; ++it; continue; }
        if (m.emptySince < 0) m.emptySince = now;
        if (idle_restore_due(true, now - m.emptySince, m.restored, g_idle.load())) {
            m.restored = true;
            c_emptyAfter.fetch_add(1);
            bool combat = false;
            motion::combat_mode_of(eid, &combat);
            const char* remedy = "none";
            if (combat && motion::requeue_guard_idle(eid)) { remedy = "its guard zone written away and back (the engine re-queues its guard idle)"; c_restoredCombat.fetch_add(1); }
            else if (motion::request_idle_pulse(eid)) { remedy = "WO-155's walk-class pulse (its locomotion graph again)"; c_restoredPulse.fetch_add(1); }
            else c_restoreFail.fetch_add(1);
            if (c_emptyAfter.load() <= kLogFirst || c_emptyAfter.load() % 50 == 0)
                logf("WO187-IDLE body=%s full-body scope empty %.0f ms after a move (%.0f ms in; combat=%d) -> %s", name_of(eid), (now - m.emptySince) * 1000.0,
                     (now - m.at) * 1000.0, combat ? 1 : 0, remedy);
        }
        ++it;
    }
}

bool parse_state2(const uint8_t* p, motion::State2* st) { std::memcpy(st, p, sizeof *st); return true; }

} // namespace

bool tag_state(uint32_t eid, uint8_t* out, size_t n) { return read_tag_state(eid, out, n); }

int read_all_scopes(uint32_t eid, ScopeInfo* out, int max) {
    if (!resolve_scope() || max <= 0) return -1;
    void* actor = motion::actor_of(eid);
    void* anim = actor ? call_get(actor, kVtGetAnimatedActor) : nullptr;
    void* ctrl = anim ? call_get(anim, kVtGetActionController) : nullptr;
    void* vp = nullptr;
    if (!ctrl || !rd(ctrl, 0, &vp) || vp != g_vftCtrl) { c_scopeRefused.fetch_add(1); return -1; }
    int32_t count = 0; char* scopes = nullptr;
    if (!rd(ctrl, kCtrlScopeCount, &count) || count < 1 || count > 64 || !rd(ctrl, kCtrlScopes, &scopes) || !scopes) { c_scopeRefused.fetch_add(1); return -1; }
    int n = 0;
    for (int idx = 0; idx < count && n < max; ++idx) {
        char* sc = scopes + static_cast<ptrdiff_t>(idx) * kScopeStride;
        void* sv = nullptr; const char* nmp = nullptr;
        ScopeInfo& s = out[n];
        s = ScopeInfo{};
        if (!rd(sc, 0, &sv) || sv != g_vftScope || !rd(sc, kScopeName, &nmp) || !nmp) { c_scopeRefused.fetch_add(1); break; }
        for (int i = 0; i < 23; ++i) { char ch = 0; if (!rd(nmp, i, &ch)) break; s.name[i] = ch; if (!ch) break; }
        void* act = nullptr;
        if (!rd(sc, kScopeAction, &act)) { c_scopeRefused.fetch_add(1); break; }
        s.ok = true;
        s.index = idx;
        s.act = act;
        if (act) {
            s.empty = false;
            const std::string a = short_name(rtti_name(act));
            std::snprintf(s.action, sizeof s.action, "%s", a.empty() ? "?" : a.c_str());
            rd(act, kActPriority, &s.prio);
            rd(act, kActFlags, &s.flags);
            rd(act, kActFragment, &s.frag);
        }
        ++n;
    }
    c_scopeReads.fetch_add(1);
    return n;
}

uint8_t drop_wish(uint32_t eid, const char* why) {
    auto it = g_wishes.find(eid);
    if (it == g_wishes.end()) return 0;
    const uint8_t held = static_cast<uint8_t>(wo195rules::kHeldWish | (it->second.pinned ? wo195rules::kHeldPin : 0));
    unpin(eid, it->second, why);
    g_wishes.erase(it);
    c_dropped.fetch_add(1);
    logf("WO187-OPP npc=%s wish dropped (%s)", name_of(eid), why);
    return held;
}

bool opp_logical_on() { return g_logical.load(); }
bool opp_pin_on() { return g_pin.load(); }
bool guard_on() { return g_guard.load(); }
bool idle_on() { return g_idle.load(); }
bool defence_on() { return g_defence.load(); }
bool combo_on() { return g_combo.load(); }
bool scope_log_on() { return g_scopeLog.load(); }
bool linger_on() { return g_linger.load(); }
bool chase_follow_on() { return g_chase.load(); }
bool avatar_hit_on() { return g_avatarHit.load(); }

bool set_logical_target(void* ca, void* entity) {
    if (!ca || !entity || !resolve_slt()) return false;
    KCDMP_FAULT_CALL(site, "wo187::set_logical_target");
    c_sltCalls.fetch_add(1);
    if (!fault::guarded(site, [&] { g_slt(ca, entity); })) { c_sltFaults.fetch_add(1); return false; }
    return true;
}

bool ensure_target(void* ca, void* model, void* entity, bool* hadIt) {
    *hadIt = false;
    if (!ca || !model || !entity) return false;
    void* req = nullptr;
    if (wrapper_entity(model, kModelRequested, &req) && req == entity) {
        void* tgt = nullptr;
        if (wrapper_entity(model, kModelTarget, &tgt) && tgt == entity) { *hadIt = true; return true; }
    }
    if (!set_logical_target(ca, entity)) return false;
    void* now = nullptr;
    return wrapper_entity(model, kModelRequested, &now) && now == entity;
}

uint8_t on_windup(uint32_t eid, const char* name, void* ca, void* model) {
    auto it = g_wishes.find(eid);
    const bool wish = it != g_wishes.end() && opp_wish_live(now_s(), it->second.refreshedAt) && g_logical.load() && !wo195::dead_gate(eid, "wish");
    bool viaReq = false;
    const uint32_t cur = model ? current_eid(eid, model, &viaReq) : 0;
    uint32_t want = 0;
    if (wish) { void* e = wish_entity(it->second); want = e ? engine::entity_id(e) : 0; }
    const WindupOpp v = windup_verdict(wish && want, cur == want);
    (v == WindupOpp::Held ? c_windupHeld : v == WindupOpp::Overwritten ? c_windupOver : c_windupNoWish).fetch_add(1);
    if (v == WindupOpp::Overwritten) apply_wish(eid, it->second, now_s(), "at a wind-up");
    if (wish) ++it->second.windups;
    const uint32_t n = c_windupHeld.load() + c_windupOver.load();
    if (v != WindupOpp::NoWish && (n <= kLogFirst || n % 50 == 0 || v == WindupOpp::Overwritten))
        logf("WO187-OPP npc=%s windup=%s opponent=%s wished=%s (%s)", name ? name : name_of(eid), windup_name(v), who(cur).c_str(), who(want).c_str(),
             viaReq ? "its requested target" : "its opponent");
    (void)ca;
    return static_cast<uint8_t>(v);
}

void note_plan(const char* name, bool opponentIsPlayer, int planned, bool refused) {
    c_planWindups.fetch_add(1);
    if (!opponentIsPlayer) c_planNotMe.fetch_add(1);
    else if (refused || planned == 0) c_planRefused.fetch_add(1);
    else c_planPlanned.fetch_add(1);
    const uint32_t n = c_planWindups.load();
    if (n <= kLogFirst || n % 50 == 0 || !opponentIsPlayer)
        logf("WO187-PLAN npc=%s planned=%d refused=%d opponent=%s", name ? name : "?", planned, refused ? 1 : 0,
             opponentIsPlayer ? "the-local-player" : "NOT-the-local-player (no shield window is planned for him)");
}

void note_move(uint32_t eid) {
    if (!eid || (!g_idle.load() && !g_scopeLog.load())) return;
    Move& m = g_moves[eid];
    m = Move{};
    m.at = now_s();
    c_moves.fetch_add(1);
}

std::string scope_text(uint32_t eid) {
    if (!g_scopeLog.load()) return "";
    const ScopeRead r = read_fullbody(eid);
    return r.ok ? scope_line(r) : "";
}

std::string director_action(void* ca) {
    // the actor's action director (combat actor +0x2E0, a C_ActionDirector) and its first action (+0x50) -- both checked by their RTTI
    void* dir = nullptr; void* act = nullptr;
    if (!ca || !rd(ca, 0x2E0, &dir) || !dir) return "";
    if (rtti_name(dir).find("ActionDirector") == std::string::npos) return "";
    if (!rd(dir, 0x50, &act) || !act) return "none";
    const std::string n = short_name(rtti_name(act));
    return n.rfind("C_CombatActorAction", 0) == 0 ? n.substr(19) : n;
}

void tick() {
    const double now = now_s();
    for (auto it = g_wishes.begin(); it != g_wishes.end();) {
        Wish& w = it->second;
        if (wo195::dead_gate(it->first, "wish")) {   // WO-195: a dead copy's wish goes at once, with its pin
            unpin(it->first, w, "the copy is dead (WO-195)");
            c_dropped.fetch_add(1);
            logf("WO187-OPP npc=%s wish dropped (the copy is dead -- WO-195)", name_of(it->first));
            it = g_wishes.erase(it);
            continue;
        }
        if (!opp_wish_live(now, w.refreshedAt) || !engine::entity_by_id(it->first)) {
            unpin(it->first, w, "the wish ended");
            c_dropped.fetch_add(1);
            if (c_dropped.load() <= kLogFirst) logf("WO187-OPP npc=%s wish dropped (no word from the agent for %.0f s)", name_of(it->first), kOppStaleS);
            it = g_wishes.erase(it);
            continue;
        }
        if (g_logical.load() && now - w.checkedAt >= kOppRecheckS) apply_wish(it->first, w, now, "re-check");
        ++it;
    }
    if (!g_moves.empty()) watch_moves(now);
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    switch (op) {
        case kOpOpponent: {
            // [op][copyEid:4][kind:1][targetEid:4][why:1]
            if (len != 11 || cap < 1) return wo163::kRBadRequest;
            uint32_t copy = 0, target = 0;
            std::memcpy(&copy, body + 1, 4); std::memcpy(&target, body + 6, 4);
            const uint8_t kind = body[5], why = body[10];
            if (!copy || kind > 2) return wo163::kRBadRequest;
            if (!engine::entity_by_id(copy)) return wo163::kRNoActor;
            const double now = now_s();
            auto it = g_wishes.find(copy);
            if (kind == 0) {
                if (it != g_wishes.end()) { unpin(copy, it->second, "the agent cleared it"); g_wishes.erase(it); }
                out[0] = 0; *outLen = 1;
                return wo163::kROk;
            }
            if (wo195::dead_gate(copy, "wish")) {   // WO-195: no wish for a dead copy (an old one goes, with its pin)
                if (it != g_wishes.end()) { unpin(copy, it->second, "the copy is dead (WO-195)"); g_wishes.erase(it); }
                out[0] = 0; *outLen = 1;
                return wo163::kROk;
            }
            const Opp k = static_cast<Opp>(kind);
            const uint32_t tgt = k == Opp::Figure ? target : 0;
            if (k == Opp::Figure && !engine::entity_by_id(tgt)) return wo163::kRNoActor;
            if (it == g_wishes.end() || it->second.kind != k || it->second.target != tgt) {
                if (it != g_wishes.end()) unpin(copy, it->second, "a new wish");
                Wish w; w.kind = k; w.target = tgt; w.why = why; w.refreshedAt = now;
                g_wishes[copy] = w;
                c_wishes.fetch_add(1);
                if (g_logical.load()) apply_wish(copy, g_wishes[copy], now, "new wish");
            } else {
                it->second.refreshedAt = now;
                it->second.why = why;
            }
            out[0] = g_wishes[copy].applied ? 1 : 0;
            *outLen = 1;
            return wo163::kROk;
        }
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = n <= 0 || cap == 0 ? 0 : (static_cast<size_t>(n) < cap ? static_cast<size_t>(n) : cap - 1);
            return wo163::kROk;
        }
        case kOpConfig: {
            // [op][switch:1]... (255 = leave as is); a 0.60.5 first-build agent sends 9, this one 10 (mp_anim_avatar_hit)
            constexpr int kN = 10;
            if (len < 1 || len > 1 + kN || cap < kN) return wo163::kRBadRequest;
            std::atomic<bool>* sw[kN] = {&g_logical, &g_pin, &g_guard, &g_idle, &g_defence, &g_combo, &g_scopeLog, &g_linger, &g_chase, &g_avatarHit};
            const char* nm[kN] = {"mp_opp_logical", "mp_opp_pin", "mp_anim_guard", "mp_anim_idle", "mp_anim_defence", "mp_anim_combo", "mp_anim_scope_log",
                                  "mp_anim_guard (figure)", "mp_chase_follow", "mp_anim_avatar_hit"};
            for (size_t i = 0; i + 1 < len && i < kN; ++i) {
                const uint8_t b = body[1 + i];
                if (b == 255) continue;
                if (sw[i]->exchange(b != 0) != (b != 0)) logf("WO187-CONFIG %s %s", nm[i], b ? "on" : "off");
            }
            for (int i = 0; i < kN; ++i) out[i] = sw[i]->load() ? 1 : 0;
            *outLen = kN;
            return wo163::kROk;
        }
        case kOpGuardHold: {
            // [op][eid:4][on:1][State2:12]
            if (len != 1 + 4 + 1 + sizeof(motion::State2) || cap < 1) return wo163::kRBadRequest;
            uint32_t eid = 0;
            std::memcpy(&eid, body + 1, 4);
            if (!eid || !engine::entity_by_id(eid)) return wo163::kRNoActor;
            motion::State2 st{};
            parse_state2(body + 6, &st);
            if (body[5] != 0 && wo195::dead_gate(eid, "guard-hold")) {   // WO-195: a dead copy's guard is never held
                motion::set_npc_guard_hold(eid, false, nullptr, npcdrive::now_s());
                out[0] = 0; *outLen = 1;
                return wo163::kROk;
            }
            motion::set_npc_guard_hold(eid, body[5] != 0, &st, npcdrive::now_s());
            out[0] = 1; *outLen = 1;
            return wo163::kROk;
        }
        case kOpMoveShown: {
            // [op][eid:4] -> [shown:1 (wo187rules::MoveShown)][ms since the move:4]
            if (len != 1 + 4 || cap < 5) return wo163::kRBadRequest;
            uint32_t eid = 0;
            std::memcpy(&eid, body + 1, 4);
            auto it = g_moves.find(eid);
            uint8_t shown = static_cast<uint8_t>(wo187rules::MoveShown::Unknown);
            uint32_t ms = 0;
            if (it != g_moves.end()) {
                const double since = now_s() - it->second.at;
                shown = static_cast<uint8_t>(wo187rules::move_shown(since, it->second.actFirst, it->second.actLeft));
                ms = static_cast<uint32_t>(since < 0 ? 0 : since * 1000.0);
            }
            out[0] = shown;
            std::memcpy(out + 1, &ms, 4);
            *outLen = 5;
            return wo163::kROk;
        }
        case kOpPositions: {
            // [op][n:1][eid:4]*n -> [ok:1][x:4][y:4][z:4] per body
            if (len < 2) return wo163::kRBadRequest;
            const size_t n = body[1];
            if (n == 0 || n > static_cast<size_t>(kMaxPosEids) || len != 2 + 4 * n || cap < 13 * n) return wo163::kRBadRequest;
            for (size_t i = 0; i < n; ++i) {
                uint32_t eid = 0;
                std::memcpy(&eid, body + 2 + 4 * i, 4);
                void* e = engine::entity_by_id(eid ? eid : kPlayerEntity);
                float p[3]{};
                const bool ok = e && engine::entity_world_pos(e, p);
                out[13 * i] = ok ? 1 : 0;
                std::memcpy(out + 13 * i + 1, p, 12);
            }
            *outLen = 13 * n;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

int status_text(char* out, int n) {
    return std::snprintf(out, n,
        "wo187 slt=%s logical=%s pin=%s guard=%s idle=%s defence=%s combo=%s scope_log=%s linger=%s chase_follow=%s wishes=%zu wishes_made=%u applied=%u "
        "overwritten=%u pinned=%u apply_fail=%u windup_held=%u windup_overwritten=%u windup_no_wish=%u dropped=%u slt_calls=%u slt_faults=%u "
        "plan_windups=%u plan_planned=%u plan_refused=%u plan_not_me=%u scope=%s scope_reads=%u scope_refused=%u moves=%u empty_after=%u "
        "restored_combat=%u restored_pulse=%u restore_fail=%u chase_follows=%u chase_hold_ends=%u avatar_hit=%s",
        g_slt ? "armed" : (g_sltResolved ? "not-armed" : "unresolved"), g_logical.load() ? "on" : "off", g_pin.load() ? "on" : "off",
        g_guard.load() ? "on" : "off", g_idle.load() ? "on" : "off", g_defence.load() ? "on" : "off", g_combo.load() ? "on" : "off",
        g_scopeLog.load() ? "on" : "off", g_linger.load() ? "on" : "off", g_chase.load() ? "on" : "off", g_wishes.size(), c_wishes.load(), c_applied.load(), c_overwritten.load(),
        c_pinned.load(), c_applyFail.load(), c_windupHeld.load(), c_windupOver.load(), c_windupNoWish.load(), c_dropped.load(), c_sltCalls.load(),
        c_sltFaults.load(), c_planWindups.load(), c_planPlanned.load(), c_planRefused.load(), c_planNotMe.load(),
        g_scopeArmed ? "armed" : (g_scopeResolved ? "not-armed" : "unresolved"), c_scopeReads.load(), c_scopeRefused.load(), c_moves.load(),
        c_emptyAfter.load(), c_restoredCombat.load(), c_restoredPulse.load(), c_restoreFail.load(), npcdrive::chase_follows(),
        npcdrive::chase_hold_ends(), g_avatarHit.load() ? "on" : "off");
}

void on_pipe_closed() {
    // main thread is not guaranteed here: the wishes go stale (kOppStaleS) and tick drops them, unpinning on the main thread
}

} // namespace kcdmp::wo187
