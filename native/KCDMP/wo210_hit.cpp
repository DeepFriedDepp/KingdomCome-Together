// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-210 E.2: the local player's blow on a copy, filtered the engine's way (wo210_hit.h).
#include "wo210_hit.h"
#include "wo210.h"
#include "wo210_rules.h"
#include "wo210_kd.h"
#include "wo203.h"
#include "wo173.h"
#include "wo172.h"
#include "wo165_rules.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "rttr_abi.h"
#include "script_context.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace kcdmp::wo210hit {

namespace {

using namespace wo210rules;

using GetI = int32_t(__fastcall*)(void*);
using GetF = float(__fastcall*)(void*);
using SetF = void(__fastcall*)(void*, float);
using CoreFn = uint8_t(__fastcall*)(void* rpg, void* result, float* strength, uint8_t* intentional, void* hitIn, uint8_t apply, int32_t type,
                                     void* flags);
using ScopeFn = void(__fastcall*)(void* scope, void* ca, const char* label);
using UnscopeFn = void(__fastcall*)(void* scope);
using TuningFn = const uint8_t*(__fastcall*)();
using OnHitFn = void(__fastcall*)(void* reactions, const void* hit);

constexpr char kLabel[] = "RPGProcessHit";
constexpr char kOnHitLabel[] = "wh::entitymodule::C_HitDeathReactions::OnHit";
constexpr char kCtx[] = "combat_actorSupressHitreactionAnimation";   // the game's own "no hit reaction" entity context (side effect 0x53)
constexpr size_t kResultBytes = 0xA0;                               // the handler's result struct (wo165_rules.h kResultBytes covers it)

// ---- the engine, read from this build at install (read-only afterwards) ------------------------------------------------------------
RphSites g_rph{};
void* g_rphFn = nullptr;
std::atomic<bool> g_armed{false}, g_off{false};
const char* g_why = "not installed";
void* g_handleEvent = nullptr;   // C_Actor vt+0xB8 (the actor's event handler: event 0x20 = its hit)
void* g_onHit = nullptr;         // C_HitDeathReactions::OnHit (reactions, hit)
uint32_t g_hdrOff = 0;           // C_Actor -> its hit-death reactions, read from the handler's own call
uint8_t g_onHitFlagsOff = 0;     // OnHit's own test of the material-effects bit: test byte [r12+FL],4
uint8_t g_onHitByteTests[8]{};   // the hit info's bytes OnHit compares with 0 (cmp byte [r12+X],0): the lethal byte and the handled byte
int g_onHitByteTestN = 0;
const char* g_fxInstallWhy = "not installed";
// main thread: the result offsets (WO-173's reading of the handler) and the impact's arming
bool g_fxResolved = false;
std::atomic<bool> g_fxArmed{false};
const char* g_fxWhy = "not resolved";
uint32_t g_flagsOff = 0, g_handledOff = 0, g_lethal0 = 0, g_lethal1 = 0;
FrameFn g_frame = nullptr;

KCDMP_FAULT_READ(g_siteRead, "wo210hit::read");
KCDMP_FAULT_CALL(g_siteFields, "wo210hit::attack fields");
KCDMP_FAULT_CALL(g_siteScope, "wo210hit::actor scope");
KCDMP_FAULT_CALL(g_siteCore, "wo210hit::hit core (apply 0)");
KCDMP_FAULT_CALL(g_siteUnscope, "wo210hit::actor unscope");
KCDMP_FAULT_CALL(g_siteSetter, "wo210hit::strength setter");
KCDMP_FAULT_CALL(g_siteOnHit, "wo210hit::OnHit (impact)");

bool copy_code(const void* src, void* dst, size_t n) { return fault::guarded(g_siteRead, [&] { std::memcpy(dst, src, n); }); }

// ---- the bound copies for the gate's thread: four tables, the main tick fills the next and publishes it ----------------------------
constexpr int kRows = 160;
struct Table { int n = 0; uint32_t eid[kRows]{}; };
Table g_tables[4]{};
std::atomic<int> g_cur{-1};
bool listed(uint32_t eid) {
    if (!eid) return false;
    const int cur = g_cur.load(std::memory_order_acquire);
    if (cur < 0) return false;
    const Table& t = g_tables[cur];
    for (int i = 0; i < t.n; ++i) if (t.eid[i] == eid) return true;
    return false;
}

// ---- the gate's queue to the main tick ------------------------------------------------------------------------------------------
struct Blow {
    uint32_t eid = 0;
    int32_t type = -1;
    float strength = 0;
    uint8_t intentional = 0, skipFilter = 0;
    bool hasFx = false;
    alignas(16) uint8_t fx[0x100]{};
};
std::mutex g_mu;
std::vector<Blow> g_q;   // under g_mu
RepeatWin g_win{};       // under g_mu
constexpr size_t kQueueMax = 64;

std::atomic<uint32_t> c_blows{0}, c_repeat{0}, c_noneType{0}, c_dryFaults{0}, c_fx{0}, c_fxSkipped{0}, c_reported{0}, c_noGuid{0},
    c_queueFull{0}, c_ctxSet{0}, c_ctxGame{0}, c_ctxCleared{0}, c_ctxFailed{0};
std::atomic<float> g_timeoutS{static_cast<float>(kRepeatDefaultS)};

double now_s() { return npcdrive::now_s(); }

void switch_off(const char* what) {
    if (g_off.exchange(true)) return;
    logf("WO210-HITLOCAL switched OFF for this session: %s faulted (the fault guard logged the site) -- the local player's blows on copies "
         "land here again (the old way)", what);
}

// RPGProcessHit's entry (any thread the melee handlers run on). true = filtered (RPGProcessHit returns 0, its caller skips the victim).
bool gate(void* rpg, void* result, void* hitIn, void* flags) {
    if (g_off.load(std::memory_order_relaxed) || !rpg || !hitIn || !result || !wo210::on(kBitHitLocalNone)) return false;
    void* me = motion::player_combat_actor();
    if (!me) return false;
    void* aCa = nullptr; uint32_t veid = 0; uint8_t skip = 0;
    if (!fault::guarded(g_siteRead, [&] {
            aCa = *reinterpret_cast<void**>(static_cast<uint8_t*>(rpg) + 8);
            skip = *static_cast<const uint8_t*>(hitIn);
            const uint8_t* det = *reinterpret_cast<uint8_t* const*>(static_cast<uint8_t*>(hitIn) + g_rph.detOff);
            if (det) veid = *reinterpret_cast<const uint32_t*>(det + g_rph.vidOff);
        }))
        return false;
    if (aCa != me || !listed(veid)) return false;
    // the attacker's attack type and strength, each read through its property's own getter (RPGProcessHit's reads)
    void* model = nullptr; int32_t type = -1, none = 0; float strength = 0;
    if (!fault::guarded(g_siteFields, [&] {
            model = *reinterpret_cast<void**>(static_cast<uint8_t*>(aCa) + g_rph.modelOff);
            if (!model) return;
            void* tp = static_cast<uint8_t*>(model) + g_rph.typeOff;
            type = reinterpret_cast<GetI>((*reinterpret_cast<void***>(tp))[1])(tp);
            void* sp = static_cast<uint8_t*>(model) + g_rph.strOff;
            strength = reinterpret_cast<GetF>((*reinterpret_cast<void***>(sp))[1])(sp);
            none = *reinterpret_cast<const int32_t*>(g_rph.noneType);
            const uint8_t* tun = reinterpret_cast<TuningFn>(g_rph.tuning)();
            if (tun) { const float to = *reinterpret_cast<const float*>(tun + g_rph.timeoutOff); if (to > 0.01f && to < 5.0f) g_timeoutS.store(to); }
        })) {
        switch_off("reading the attacker's attack fields");
        return false;
    }
    if (!model) return false;
    if (type == none) { c_noneType.fetch_add(1); return true; }   // the engine: no attack type, no core -- for a copy nothing at all
    const double now = now_s();
    {
        std::lock_guard<std::mutex> lk(g_mu);
        if (repeat_filtered(g_win, veid, skip != 0, now, g_timeoutS.load())) { c_repeat.fetch_add(1); return true; }
    }
    // the engine's own dry run under the attacker actor's lock, exactly as RPGProcessHit calls the core but for apply = 0
    float str = strength;
    uint8_t intentional = 0;
    alignas(16) uint8_t scope[0x80]{};
    if (!fault::guarded(g_siteScope, [&] { reinterpret_cast<ScopeFn>(g_rph.scopeCtor)(scope, aCa, reinterpret_cast<const char*>(g_rph.scopeLabel)); })) {
        switch_off("the attacker actor's scope");
        return true;   // nothing ran: filtered all the same
    }
    hits::dry_run_begin();   // the hit hook runs the engine's own call and nothing of ours (no "hit by the player" mark: the report is ours)
    const bool ran = fault::guarded(g_siteCore, [&] { reinterpret_cast<CoreFn>(g_rph.core)(rpg, result, &str, &intentional, hitIn, 0, type, flags); });
    hits::dry_run_end();
    fault::guarded(g_siteUnscope, [&] { reinterpret_cast<UnscopeFn>(g_rph.scopeDtor)(scope); });
    if (!ran) { c_dryFaults.fetch_add(1); switch_off("the hit core's dry run"); return true; }
    // RPGProcessHit's epilogue: one target per swing (the strength after the intended target is 0)
    if (!skip) fault::guarded(g_siteSetter, [&] { reinterpret_cast<SetF>(g_rph.setter)(static_cast<uint8_t*>(model) + g_rph.strOff, intentional ? 0.0f : str); });
    Blow b{};
    b.eid = veid; b.type = type; b.strength = strength; b.intentional = intentional; b.skipFilter = skip;
    if (g_fxArmed.load(std::memory_order_acquire) && wo210::fix_on(kFixHitFx) &&
        fault::guarded(g_siteRead, [&] { std::memcpy(b.fx, result, kResultBytes); })) {
        fx_edit(b.fx, g_flagsOff, g_handledOff, g_lethal0, g_lethal1);
        b.hasFx = true;
    }
    {
        std::lock_guard<std::mutex> lk(g_mu);
        if (g_q.size() < kQueueMax) g_q.push_back(b);
        else c_queueFull.fetch_add(1);
    }
    c_blows.fetch_add(1);
    return true;
}

// ---- install (off the main thread, with the other gates) -----------------------------------------------------------------------
bool install_fx(HMODULE em) {
    void* const* vftActor = anchor::find_vftable(em, ".?AVC_Actor@entitymodule@wh@@", 0);
    const uint8_t* onHit = anchor::function_by_string(em, kOnHitLabel);
    anchor::Range text{};
    if (!vftActor || !onHit || !anchor::section(em, ".text", &text)) { g_fxInstallWhy = "no C_Actor vtable or no OnHit label"; return false; }
    const uint8_t* he = static_cast<const uint8_t*>(vftActor[0xB8 / 8]);
    if (!text.contains(he) || !text.contains(he + 0x400)) { g_fxInstallWhy = "the actor's event handler is outside the module's code"; return false; }
    uint8_t buf[0x400];
    if (!copy_code(he, buf, sizeof buf)) { g_fxInstallWhy = "the actor's event handler could not be read"; return false; }
    // mov rdx,[rsi+18h]; mov rcx,[rbx+HDR]; mov rdx,[rdx]; call OnHit       and before it: cmp eax,20h; je (the hit event)
    static const int16_t kCall[16] = {0x48, 0x8B, 0x56, 0x18, 0x48, 0x8B, 0x8B, kW, kW, kW, kW, 0x48, 0x8B, 0x12, 0xE8, kW};
    static const int16_t kEvt[5] = {0x83, 0xF8, 0x20, 0x0F, 0x84};
    const long at = w210_find_once(buf, sizeof buf - 4, kCall, 15);
    if (at < 0 || w210_find_once(buf, sizeof buf, kEvt, 5) < 0) { g_fxInstallWhy = "the hit event's call is not in the actor's event handler (once)"; return false; }
    const uintptr_t target = w210_rel(reinterpret_cast<uintptr_t>(he), buf, static_cast<size_t>(at + 15), static_cast<size_t>(at + 19));
    if (target != reinterpret_cast<uintptr_t>(onHit)) { g_fxInstallWhy = "the actor's hit event calls another function than OnHit's label"; return false; }
    // OnHit's own material-effects test: test byte [r12+FL],4
    static uint8_t ob[0x1400];
    if (!text.contains(onHit + sizeof ob) || !copy_code(onHit, ob, sizeof ob)) { g_fxInstallWhy = "OnHit could not be read"; return false; }
    static const int16_t kMat[6] = {0x41, 0xF6, 0x44, 0x24, kW, 0x04};
    const long m = w210_find_once(ob, sizeof ob, kMat, 6);
    if (m < 0) { g_fxInstallWhy = "OnHit's material-effects test not found (once)"; return false; }
    g_hdrOff = w210_u32(buf + at + 7);
    g_onHitFlagsOff = ob[m + 4];
    // cmp byte [r12+X],0 -- the distinct X OnHit tests (the lethal byte, the handled byte)
    static const int16_t kCmp[6] = {0x41, 0x80, 0x7C, 0x24, kW, 0x00};
    for (size_t o = 0; o + 6 <= sizeof ob; ++o) {
        if (!w210_match(ob + o, kCmp, 6)) continue;
        bool seen = false;
        for (int i = 0; i < g_onHitByteTestN; ++i) seen = seen || g_onHitByteTests[i] == ob[o + 4];
        if (!seen && g_onHitByteTestN < 8) g_onHitByteTests[g_onHitByteTestN++] = ob[o + 4];
    }
    if (g_hdrOff == 0 || g_hdrOff > 0x2000) { g_fxInstallWhy = "the reactions offset is implausible"; return false; }
    g_handleEvent = const_cast<uint8_t*>(he);
    g_onHit = const_cast<uint8_t*>(onHit);
    g_fxInstallWhy = nullptr;
    return true;
}

void install() {
    HMODULE cm = GetModuleHandleA("CombatModule.dll"), em = GetModuleHandleA("EntityModule.dll");
    if (!cm || !em) { g_why = "CombatModule / EntityModule not loaded"; logf("WO210-HITLOCAL not armed: %s", g_why); return; }
    int count = 0;
    const uint8_t* rph = anchor::function_by_string(cm, kLabel, &count);
    anchor::Range text{};
    if (!rph || !anchor::section(cm, ".text", &text) || !text.contains(rph)) { g_why = "RPGProcessHit's label is not referenced by exactly one function"; logf("WO210-HITLOCAL not armed: %s (%d)", g_why, count); return; }
    uint8_t pro[sizeof wo165::kProcessPrologue]{};
    if (!copy_code(rph, pro, sizeof pro) || std::memcmp(pro, wo165::kProcessPrologue, sizeof pro) != 0 ||
        std::memcmp(pro, hookpro::kRpgProcessHit, sizeof hookpro::kRpgProcessHit) != 0) {
        g_why = "RPGProcessHit's prologue is not the one read (another build)"; logf("WO210-HITLOCAL not armed: %s", g_why); return;
    }
    static uint8_t body[0x240];
    const size_t want = static_cast<size_t>(text.end - rph) < sizeof body ? static_cast<size_t>(text.end - rph) : sizeof body;
    RphSites s{};
    if (!copy_code(rph, body, want) || !parse_rph(body, want, reinterpret_cast<uintptr_t>(rph), &s)) {
        g_why = "RPGProcessHit's code is not the one read (type test, victim id, repeat timer, scope, core call, strength setter -- once each)";
        logf("WO210-HITLOCAL not armed: %s", g_why); return;
    }
    const uintptr_t calls[6] = {s.tuning, s.scopeCtor, s.scopeDtor, s.core, s.setter, 0};
    for (int i = 0; i < 5; ++i) if (!text.contains(reinterpret_cast<const void*>(calls[i]))) { g_why = "a call target lies outside the module's code"; logf("WO210-HITLOCAL not armed: %s", g_why); return; }
    uint8_t cpro[sizeof kRphCorePrologue]{};
    if (!copy_code(reinterpret_cast<const void*>(s.core), cpro, sizeof cpro) || std::memcmp(cpro, kRphCorePrologue, sizeof cpro) != 0) {
        g_why = "the hit core's prologue is not the one read"; logf("WO210-HITLOCAL not armed: %s", g_why); return;
    }
    char lab[sizeof kLabel]{};
    if (!copy_code(reinterpret_cast<const void*>(s.scopeLabel), lab, sizeof lab) || std::memcmp(lab, kLabel, sizeof kLabel) != 0) {
        g_why = "the actor scope's label is not RPGProcessHit's"; logf("WO210-HITLOCAL not armed: %s", g_why); return;
    }
    // the core's apply argument: the engine's own callers that pass 0 (its dry runs) -- at least two besides RPGProcessHit
    int dry = 0, all = 0;
    {
        const uint8_t* p = text.begin;
        const size_t n = static_cast<size_t>(text.end - text.begin);
        fault::guarded(g_siteRead, [&] {
            for (size_t o = kDryRunWindow; o + 5 <= n; ++o) {
                if (p[o] != 0xE8) continue;
                int32_t r; std::memcpy(&r, p + o + 1, 4);
                if (reinterpret_cast<uintptr_t>(p + o + 5) + static_cast<intptr_t>(r) != s.core) continue;
                ++all;
                if (dry_run_call(p + o - kDryRunWindow, kDryRunWindow)) ++dry;
            }
        });
    }
    if (dry < 2) { g_why = "the engine's own dry runs of the hit core (apply 0) are not there"; logf("WO210-HITLOCAL not armed: %s (%d of %d calls)", g_why, dry, all); return; }
    g_rph = s;
    g_rphFn = const_cast<uint8_t*>(rph);
    const bool fx = install_fx(em);
    const char* why = nullptr;
    if (!inlinehook::install_gate4(g_rphFn, hookpro::kRpgProcessHit, sizeof hookpro::kRpgProcessHit, &gate, &why)) {
        g_why = "the patch was refused"; logf("WO210-HITLOCAL not armed: %s (%s)", g_why, why ? why : "?"); return;
    }
    g_armed.store(true);
    g_why = "armed";
    char a[96], c[96];
    anchor::describe(g_rphFn, a, sizeof a);
    anchor::describe(reinterpret_cast<const void*>(s.core), c, sizeof c);
    logf("WO210-HITLOCAL armed: RPGProcessHit=%s gated (label + prologue), core=%s (%d engine dry runs of %d calls), model +0x%X, type +0x%X, "
         "strength +0x%X, victim id hit-in +0x%X -> +0x%X, repeat timeout tuning +0x%X; impact: %s (reactions at actor +0x%X, OnHit tests flags +0x%X)",
         a, c, dry, all, s.modelOff, s.typeOff, s.strOff, s.detOff, s.vidOff, s.timeoutOff, fx ? "OnHit by the actor's own hit event" : g_fxInstallWhy,
         g_hdrOff, g_onHitFlagsOff);
}

// ---- main tick ---------------------------------------------------------------------------------------------------------------------
struct Ctx { void* soul = nullptr; bool ours = false, ok = false; double retryAt = 0; };
std::unordered_map<uint32_t, Ctx> g_ctx;   // the copies' context state (main thread)
double g_nextRebuild = 0;

bool fx_wanted() { return g_armed.load() && !g_off.load() && wo210::on(kBitHitLocalNone) && g_fxArmed.load() && wo210::fix_on(kFixHitFx); }

void resolve_fx() {
    if (g_fxResolved) return;
    g_fxResolved = true;
    if (!g_armed.load()) { g_fxWhy = "the gate is not armed"; return; }
    if (!g_onHit) { g_fxWhy = g_fxInstallWhy ? g_fxInstallWhy : "OnHit not found"; logf("WO210-HITLOCAL impact not armed: %s -- filtered blows play nothing on the copy", g_fxWhy); return; }
    // the flags word and the handled byte as OnHit itself reads them; the two lethal bytes as the collision handler's reaction block reads
    // them (WO-173): the handled byte is OnHit's one byte test that is not a lethal byte
    uint32_t l0 = 0, l1 = 0;
    if (!wo173::lethal_offsets(&l0, &l1)) { g_fxWhy = "the result's lethal bytes are not read (WO-173's reaction block)"; logf("WO210-HITLOCAL impact not armed: %s", g_fxWhy); return; }
    uint32_t h = 0; int others = 0;
    for (int i = 0; i < g_onHitByteTestN; ++i) if (g_onHitByteTests[i] != l0 && g_onHitByteTests[i] != l1) { h = g_onHitByteTests[i]; ++others; }
    const uint32_t f = g_onHitFlagsOff;
    if (others != 1 || f + 4 > kResultBytes || h >= kResultBytes || l0 >= kResultBytes || l1 >= kResultBytes) {
        g_fxWhy = "OnHit's flag and handled tests are not the ones read"; logf("WO210-HITLOCAL impact not armed: %s (flags +0x%X, %d other byte test(s))", g_fxWhy, f, others); return;
    }
    g_flagsOff = f; g_handledOff = h; g_lethal0 = l0; g_lethal1 = l1;
    g_fxArmed.store(true, std::memory_order_release);
    g_fxWhy = "armed";
    logf("WO210-HITLOCAL impact armed: result flags +0x%X (material effects only), handled +0x%X, lethal +0x%X/+0x%X cleared; the copies get the "
         "game's own context %s", f, h, l0, l1, kCtx);
}

void rebuild(bool on, const std::unordered_set<uint32_t>& copies) {
    const int cur = g_cur.load();
    const int next = (cur + 1) & 3;
    Table& t = g_tables[next];
    t.n = 0;
    if (on) for (uint32_t e : copies) { if (t.n >= kRows) break; t.eid[t.n++] = e; }
    g_cur.store(next, std::memory_order_release);
}

void contexts(bool want, const std::unordered_set<uint32_t>& copies, double now) {
    for (auto it = g_ctx.begin(); it != g_ctx.end();) {
        const bool keep = want && copies.count(it->first);
        if (keep) { ++it; continue; }
        if (it->second.ours && it->second.soul && hits::soul_of_eid(it->first) == it->second.soul &&
            sctx::set_soul_context_quiet(it->second.soul, kCtx, false) >= 0)
            c_ctxCleared.fetch_add(1);
        it = g_ctx.erase(it);
    }
    if (!want) return;
    for (uint32_t e : copies) {
        Ctx& c = g_ctx[e];
        if (c.ok || now < c.retryAt) continue;
        void* soul = hits::soul_of_eid(e);
        const int r = soul ? sctx::set_soul_context_quiet(soul, kCtx, true) : -1;
        if (r == 1) { c.soul = soul; c.ours = true; c.ok = true; c_ctxSet.fetch_add(1); }
        else if (r == 0) { c.soul = soul; c.ours = false; c.ok = true; c_ctxGame.fetch_add(1); }   // set already (a quest's): not ours
        else { c.retryAt = now + 2.0; c_ctxFailed.fetch_add(1); }
    }
}

const char* name_of(uint32_t eid) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "?";
}

void deliver(const Blow& b) {
    // the impact: the copy's own hit event with the filled result, material effects only -- not on a copy lying (C.3) or dead
    const char* fx = "off";
    if (b.hasFx) {
        auto it = g_ctx.find(b.eid);
        void* actor = motion::actor_of(b.eid);
        void* vt = nullptr; void* reactions = nullptr;
        if (it == g_ctx.end() || !it->second.ok) fx = "no-context";
        else if (wo172::copy_dead(b.eid) || wo210kd::held_down(b.eid)) fx = "lying";
        else if (!actor || !fault::guarded(g_siteRead, [&] { vt = (*reinterpret_cast<void***>(actor))[0xB8 / 8]; reactions = *reinterpret_cast<void**>(static_cast<uint8_t*>(actor) + g_hdrOff); }) ||
                 vt != g_handleEvent || !reactions)
            fx = "no-actor";
        else if (!fault::guarded(g_siteOnHit, [&] { reinterpret_cast<OnHitFn>(g_onHit)(reactions, b.fx); })) {
            fx = "FAULT";
            g_fxArmed.store(false);
            logf("WO210-HITLOCAL impact switched OFF for this session: OnHit faulted -- filtered blows play nothing on the copy");
        } else fx = "played";
        (std::strcmp(fx, "played") == 0 ? c_fx : c_fxSkipped).fetch_add(1);
    }
    // the report: the LocalHit frame with no numbers, by the player, "judge it there"
    void* soul = hits::soul_of_eid(b.eid);
    unsigned char body[16 + 4 + 4 + 3]{};
    if (!soul || !rttr::soul_guid(soul, body)) { c_noGuid.fetch_add(1); return; }
    body[25] = 1;   // by the player
    body[26] = 1;   // judge it there: the host's engine decides, this screen applied nothing
    if (g_frame) g_frame(body, sizeof body);
    const uint32_t k = c_reported.fetch_add(1) + 1;
    if (k <= 100 || k % 50 == 0)
        logf("WO210-HITLOCAL blow npc=%s type=%d strength=%.2f intentional=%u skip_filter=%u impact=%s -- not judged here, sent to the host [n=%u]",
             name_of(b.eid), b.type, b.strength, b.intentional, b.skipFilter, fx, k);
}

void tick() {
    if (!g_armed.load()) return;
    resolve_fx();
    const double now = now_s();
    const bool on = !g_off.load() && wo210::on(kBitHitLocalNone);
    if (now >= g_nextRebuild) {
        g_nextRebuild = now + 0.1;
        std::unordered_set<uint32_t> copies;
        if (on) {
            npcdrive::BoundBody bb[kRows];
            const int n = npcdrive::bound_bodies(bb, kRows);
            for (int i = 0; i < n; ++i) if (bb[i].eid && !motion::is_avatar_eid(bb[i].eid)) copies.insert(bb[i].eid);
        }
        rebuild(on, copies);
        contexts(fx_wanted(), copies, now);
    }
    std::vector<Blow> q;
    { std::lock_guard<std::mutex> lk(g_mu); q.swap(g_q); }
    for (const Blow& b : q) deliver(b);
}

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "w210hit gate=%s%s impact=%s blows=%u repeat_or_cap=%u no_type=%u dry_faults=%u played=%u "
                         "not_played=%u reported=%u no_guid=%u queue_full=%u ctx_set=%u ctx_game=%u ctx_cleared=%u ctx_failed=%u ctx_now=%zu timeout_s=%.2f",
                         g_why, g_off.load() ? "(OFF by a fault)" : "", g_fxWhy, c_blows.load(), c_repeat.load(), c_noneType.load(), c_dryFaults.load(),
                         c_fx.load(), c_fxSkipped.load(), c_reported.load(), c_noGuid.load(), c_queueFull.load(), c_ctxSet.load(), c_ctxGame.load(),
                         c_ctxCleared.load(), c_ctxFailed.load(), g_ctx.size(), static_cast<double>(g_timeoutS.load()));
}

int status_reg(char* out, int n) { return status(out, n); }
void tick_reg() { tick(); }
void install_reg() { install(); }
const bool kReg = wo203::reg_install("w210hit", &install_reg) && wo203::reg_tick("w210hit", &tick_reg) && wo203::reg_status("w210hit", &status_reg);

} // namespace

void set_frame_callback(FrameFn fn) { g_frame = fn; }

} // namespace kcdmp::wo210hit
