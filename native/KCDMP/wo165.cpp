// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-165: the replay through the engine's hit processor (wo165.h).
#include "wo165.h"
#include "inline_hook.h"
#include "wo210_rules.h"
#include "wo210.h"
#include "wo166_rules.h"
#include "wo166.h"
#include "wo178.h"
#include "wo178_rules.h"
#include "wo173_rules.h"

#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <unordered_map>

#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "local_state.h"
#include "log.h"
#include "npc_drive.h"   // KUBA 19: the victim NPC's facing
#include "combat_swing.h"   // KUBA 21: the block's own fragment on the NPC
#include "wo187.h"          // KUBA 21: its tags
#include "main_thread.h"    // KUBA 23: the guard's per-frame hold
#include "wo208.h"          // KUBA 30 stage 2: the NPCs a partner's local duel drives
#include "motion.h"
#include "rttr_abi.h"
#include "wo173.h"

namespace kcdmp::wo165 {
namespace {

// the collision handler's profiler label: the one function that references it is the engine's own caller of RPGProcessHit
constexpr char kCallerLabel[] = "wh::combatmodule::C_CombatActorCollisionProcess::OnCollision";
constexpr char kProcessLabel[] = "RPGProcessHit";
constexpr char kRpgClass[] = ".?AVC_CombatRPG@combatmodule@wh@@";
constexpr uint32_t kPlayerEntity = 0x7777;

template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "wo165::rd");
    if (!fault::plausible_address(reinterpret_cast<uintptr_t>(base) + off)) return false;
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}
bool is_a(void* obj, void* const* vft) { void* vp = nullptr; return obj && vft && rd(obj, 0, &vp) && vp == static_cast<const void*>(vft); }
bool copy_bytes(const void* src, void* dst, size_t n) {
    KCDMP_FAULT_READ(site, "wo165::copy_bytes");
    return fault::guarded(site, [&] { std::memcpy(dst, src, n); });
}

// The two calls into the engine. Call sites: a fault switches the whole replay off (below), not just the site.
bool call_ctor(void* fn, void* result) {
    KCDMP_FAULT_CALL(site, "wo165::call_result_ctor");
    return fault::guarded(site, [&] { reinterpret_cast<void* (__fastcall*)(void*)>(fn)(result); });
}
bool call_process(void* fn, void* rpg, void* result, const void* hitIn, void* flags, bool* ret) {
    KCDMP_FAULT_CALL(site, "wo165::call_rpg_process_hit");
    return fault::guarded(site, [&] { *ret = reinterpret_cast<bool (__fastcall*)(void*, void*, const void*, void*)>(fn)(rpg, result, hitIn, flags); });
}

bool g_resolved = false, g_armed = false;
std::atomic<bool> g_off{false};
const char* g_why = "not resolved";
void* g_fnProcess = nullptr;
void* g_fnCtor = nullptr;
uint32_t g_rpgOffset = 0;
void* const* g_vftRpg = nullptr;
uint32_t g_seq = 0;
std::atomic<uint32_t> c_calls{0}, c_hits{0}, c_blocked{0}, c_perfect{0}, c_broken{0}, c_filtered{0}, c_none{0}, c_refused{0}, c_faults{0};

// measured damage per sequence number (a small ring; the slot hook's watch fills it from the main thread)
struct Slot { uint32_t seq = 0; Damage d; };
Slot g_ring[64];
std::mutex g_ringMutex;

Slot* slot_for(uint32_t seq) { return &g_ring[seq % 64]; }

void on_damage(uint32_t seq, uint32_t victimEid, float health, float stamina, bool live) {
    (void)victimEid;
    std::lock_guard<std::mutex> lock(g_ringMutex);
    Slot* s = slot_for(seq);
    if (s->seq != seq) return;
    s->d.state = health < 0 ? 3 : 2;
    s->d.health = health < 0 ? 0 : health;
    s->d.stamina = stamina < 0 ? 0 : stamina;
    s->d.victimLive = live;
}

void switch_off(const char* what) {
    if (!g_off.exchange(true)) {
        c_faults.fetch_add(1);
        logf("WO165-REPLAY switched OFF for this session: %s faulted (the fault guard logged the site); the host's verdicts apply as before "
             "(WO-161) until the game restarts tid=%lu", what, GetCurrentThreadId());
    }
}

bool victim_blocking(const motion::ModelRead& m) { return victim_blocking_state(m.state, (m.valid & motion::kMvState) != 0); }

uint32_t player_eid() {
    void* e = engine::entity_by_id(kPlayerEntity);
    return e ? engine::entity_id(e) : 0;
}

}   // namespace

const char* reason_name(uint8_t r) {
    switch (r) {
        case kOk: return "ok";
        case kOff: return "switched-off";
        case kNotArmed: return "not-armed";
        case kNoAttacker: return "no-attacker-combat-actor";
        case kNoVictim: return "no-victim-combat-actor";
        case kNoProcessor: return "no-hit-processor";
        case kFieldsUnnamed: return "attack-fields-unnamed";
        case kFault: return "fault";
        case kSameActor: return "attacker-is-victim";
        case kNoPosition: return "no-victim-position";
        case kVictimBlocking: return "victim-blocking";
        case kOutOfReach: return "out-of-reach";
        default: return "?";
    }
}

bool resolve() {
    if (g_resolved) return g_armed;
    g_resolved = true;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    if (!cm) { g_why = "CombatModule not loaded"; logf("WO165-REPLAY not armed: %s", g_why); return false; }
    int n = 0;
    const uint8_t* process = anchor::function_by_string(cm, kProcessLabel, &n);
    if (!process) { g_why = "the RPGProcessHit label is not referenced by exactly one function"; logf("WO165-REPLAY not armed: %s (%d)", g_why, n); return false; }
    uint8_t pro[sizeof kProcessPrologue]{};
    bool proOk = copy_bytes(process, pro, sizeof pro) && std::memcmp(pro, kProcessPrologue, sizeof pro) == 0;
    // WO-210 E.2: the mod's own gate sits at its entry (wo210_hit.cpp) -- the bytes it replaced are the prologue read; calls through it pass
    if (!proOk) proOk = inlinehook::original_bytes(process, pro, sizeof pro) && std::memcmp(pro, kProcessPrologue, sizeof pro) == 0;
    if (!proOk) {
        g_why = "RPGProcessHit's prologue is not the one read (another build)"; logf("WO165-REPLAY not armed: %s", g_why); return false;
    }
    const uint8_t* caller = anchor::function_by_string(cm, kCallerLabel, &n);
    anchor::Range text{};
    if (!caller || !anchor::section(cm, ".text", &text) || !text.contains(caller)) {
        g_why = "the collision handler is not found by its label"; logf("WO165-REPLAY not armed: %s", g_why); return false;
    }
    // The call site sits in a chained fragment after the handler's primary entry (unwind chunks follow each other), so a fixed window
    // from the entry is read, clipped to .text; the sequence must occur in it exactly once.
    static uint8_t buf[0x1000];
    const size_t want = static_cast<size_t>(text.end - caller) < sizeof buf ? static_cast<size_t>(text.end - caller) : sizeof buf;
    if (!copy_bytes(caller, buf, want)) { g_why = "the collision handler could not be read"; logf("WO165-REPLAY not armed: %s", g_why); return false; }
    CallSite cs{};
    if (!parse_call_site(buf, want, reinterpret_cast<uintptr_t>(caller), &cs)) {
        g_why = "the result-ctor / processor call sequence is not in the collision handler (once)"; logf("WO165-REPLAY not armed: %s", g_why); return false;
    }
    if (cs.process != reinterpret_cast<uintptr_t>(process)) {
        g_why = "the collision handler calls another function than the RPGProcessHit label's"; logf("WO165-REPLAY not armed: %s", g_why); return false;
    }
    uint8_t cpro[sizeof kCtorPrologue]{};
    if (!copy_bytes(reinterpret_cast<const void*>(cs.ctor), cpro, sizeof cpro) || std::memcmp(cpro, kCtorPrologue, sizeof cpro) != 0) {
        g_why = "the result constructor's prologue is not the one read"; logf("WO165-REPLAY not armed: %s", g_why); return false;
    }
    if (cs.rpgOffset < 0x100 || cs.rpgOffset > 0x2000) { g_why = "the processor offset read from the code is implausible"; logf("WO165-REPLAY not armed: %s 0x%X", g_why, cs.rpgOffset); return false; }
    g_vftRpg = anchor::find_vftable(cm, kRpgClass, 0);
    if (!g_vftRpg) { g_why = "RTTI C_CombatRPG vftable not unique"; logf("WO165-REPLAY not armed: %s", g_why); return false; }
    g_fnProcess = const_cast<uint8_t*>(process);
    g_fnCtor = reinterpret_cast<void*>(cs.ctor);
    g_rpgOffset = cs.rpgOffset;
    hits::set_replay_damage_callback(&on_damage);
    g_armed = true;
    g_why = "armed";
    char a[64], b[64];
    anchor::describe(g_fnProcess, a, sizeof a);
    anchor::describe(g_fnCtor, b, sizeof b);
    logf("WO165-REPLAY armed: RPGProcessHit=%s (label + prologue + the handler's own call), result ctor=%s, processor at combat actor +0x%X "
         "(read from the handler's code), C_CombatRPG by RTTI tid=%lu", a, b, g_rpgOffset, GetCurrentThreadId());
    return true;
}

bool armed() { return g_armed; }
bool off() { return g_off.load(); }
const char* why() { return g_off.load() ? "switched off by a fault this session" : g_why; }

// KUBA 23 (host, 16.11 11:49-12:03: KUBA 21's block fragment was queued "ok" 43 times, yet the guard played CombatHitLowMovementGen
// every time -- the hit reaction is the engine's pick from the NPC's REAL guard, and the NPC was never in it: the replay is an instant
// hit, its AI never saw the figure's swing). Now the block is decided when the figure's swing STARTS (wo136::avatar_swing: the host
// knows its target then) and the NPC's own block is held up the engine's way (combat_SetBlock's setter, re-asserted every frame against
// its AI) until the blow lands or 1.2 s pass -- the blow then meets a raised guard and the engine blocks it with its own animation and
// numbers. The blow's KUBA 19 keeps that decision. KCDMP_KUBA_SWING_GUARD=off.
namespace {
struct KubaGuard { uint32_t attacker = 0; double until = 0, startedAt = 0, firstAt = 0; bool block = false, held = false, logged = false; };
std::unordered_map<uint32_t, KubaGuard> g_kubaGuards;   // npc eid -> the decision for the swing in flight (main thread)
std::unordered_map<uint32_t, double> g_kubaGuardEnd;    // 23c: npc eid -> when its last held guard came down (its AI's rest)
bool g_kubaGuardTick = false;
uint32_t g_kubaGuardN = 0;
double kuba_now_s() { return static_cast<double>(GetTickCount64()) / 1000.0; }
bool kuba_swing_guard_on() {
    static int s = -1;
    if (s < 0) {
        char v[16] = {0};
        const DWORD n = GetEnvironmentVariableA("KCDMP_KUBA_SWING_GUARD", v, sizeof v);
        s = (n > 0 && n < sizeof v && (!_stricmp(v, "off") || !std::strcmp(v, "0"))) ? 0 : 1;
        logf("KUBA-23 an NPC raises its block when a partner's figure swings at it (KCDMP_KUBA_SWING_GUARD=off turns it off): %s", s ? "on" : "OFF");
    }
    return s == 1;
}
int kuba_block_pct_env() {
    static int s = -1;
    if (s < 0) {
        char b[16] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_KUBA_NPC_BLOCK_PCT", b, sizeof b);
        // 23d: 40 % (13:10-13:22 at 55 % with the combo hold: 38 % of the partner's blows blocked, "almost impossible to fight")
        const int v = (n > 0 && n < sizeof b) ? std::atoi(b) : 40;
        s = v < 0 ? 0 : (v > 100 ? 100 : v);
    }
    return s;
}
uint32_t kuba_rng() { static uint32_t r = 0x7F4A7C15u; r = r * 1664525u + 1013904223u; return r >> 8; }
void kuba_guard_tick() {
    if (g_kubaGuards.empty()) return;
    const double now = kuba_now_s();
    for (auto it = g_kubaGuards.begin(); it != g_kubaGuards.end();) {
        KubaGuard& g = it->second;
        if (now >= g.until) {
            if (g.held) {
                motion::kuba_npc_set_block(it->first, false, true);   // block down, its combat AI back
                if (g_kubaGuardEnd.size() > 256) g_kubaGuardEnd.clear();
                g_kubaGuardEnd[it->first] = now;                      // 23c: its AI's rest starts
            }
            it = g_kubaGuards.erase(it);
            continue;
        }
        if (g.block) g.held = motion::kuba_npc_set_block(it->first, true, !g.held) || g.held;   // held up every frame (its AI off once)
        // 23b: the evidence -- what the NPC's model says a quarter second into the raised guard (Blocking or not)
        if (g.held && !g.logged && now - g.startedAt >= 0.25) {
            g.logged = true;
            motion::ModelRead m{};
            static uint32_t s_n = 0;
            if (motion::read_model(it->first, &m) && ++s_n <= 60)
                logf("KUBA-23 guard check npc=0x%X 250 ms into the raised block: state=0x%X block_zone=%d [n=%u]", it->first,
                     static_cast<unsigned>(m.state), (m.valid & motion::kMvBlockZone) ? m.blockZone : -9, s_n);
        }
        ++it;
    }
}
}   // namespace

void kuba_swing_guard(uint32_t npcEid, uint32_t attackerEid) {
    if (!npcEid || !attackerEid || !kuba_swing_guard_on() || motion::is_avatar_eid(npcEid)) return;
    if (wo208::kuba_remote_duel(npcEid)) return;   // KUBA 30 stage 2: the partner's own brain blocks for it on his screen
    const int pct = kuba_block_pct_env();
    if (pct <= 0) return;
    if (!g_kubaGuardTick) { g_kubaGuardTick = true; main_thread::post_repeating("kuba23-guard", [] { kuba_guard_tick(); }); }
    motion::ModelRead m{};
    if (!motion::read_model(npcEid, &m) || !(m.valid & motion::kMvState)) return;
    const int32_t vs = m.state;
    const bool inCombat = (m.valid & motion::kMvCombatMode) && m.combatMode != 0;
    const bool busy = (vs & 0x8) || (vs & 0x40) || (vs & 0x20);   // Striking, Hit, Withdraw (as KUBA 19)
    void* vEnt = engine::entity_by_id(npcEid);
    void* aEnt = engine::entity_by_id(attackerEid);
    float vp[3]{}, ap[3]{};
    const float fc = (vEnt && aEnt && engine::entity_world_pos(vEnt, vp) && engine::entity_world_pos(aEnt, ap))
                         ? facing_cos(npcdrive::entity_yaw(vEnt), vp[0], vp[1], ap[0], ap[1]) : -2.0f;
    const double now = kuba_now_s();
    // KUBA 23c (16.11 12:45-12:58): the swing -> blow gap on the host reached 1.3 s (12:50:18.3 BLOCK UP -> 12:50:19.6 the blow), the
    // 1.2 s window had closed -- now 2 s (KCDMP_KUBA_SWING_GUARD_MS). A guard already up for this attacker stays up through his combo (no
    // re-roll that drops it mid-combo), at most 3 s in all; after a window the NPC gets its combat AI back for a rest (1.2 s,
    // KCDMP_KUBA_GUARD_REST_MS) before the next block -- with every swing opening a window it stood AI-less, never turning to the player.
    static int s_winMs = -1, s_restMs = -1;
    if (s_winMs < 0) {
        char b[16] = {};
        DWORD n = GetEnvironmentVariableA("KCDMP_KUBA_SWING_GUARD_MS", b, sizeof b);
        s_winMs = (n > 0 && n < sizeof b) ? (std::clamp)(std::atoi(b), 300, 5000) : 1500;   // 23d: 1.5 s (2 s kept a missed NPC AI-less too long)
        n = GetEnvironmentVariableA("KCDMP_KUBA_GUARD_REST_MS", b, sizeof b);
        s_restMs = (n > 0 && n < sizeof b) ? (std::clamp)(std::atoi(b), 0, 10000) : 1200;
        logf("KUBA-23d swing guard window %d ms, rest after a guard %d ms, block chance %d%%, no hold through a combo", s_winMs, s_restMs, pct);
    }
    KubaGuard& g = g_kubaGuards[npcEid];
    const bool wasHeld = g.held;
    // 23d: the hold through a combo is gone (Kuba 13:22: "the fix made it worse, you can hardly fight" -- a guard up for a whole combo
    // blocked blow after blow of an NPC that could neither turn nor strike back); every swing decides on its own again
    const bool combo = false;
    auto re = g_kubaGuardEnd.find(npcEid);
    const bool capped = wasHeld && g.block && now - g.firstAt >= 3.0;   // held 3 s through a combo: down, its AI's turn
    const bool resting = capped || (!wasHeld && re != g_kubaGuardEnd.end() && (now - re->second) * 1000.0 < s_restMs);
    if (capped) g_kubaGuardEnd[npcEid] = now;
    const bool roll = static_cast<int>(kuba_rng() % 100u) < pct;
    const bool block = combo || (inCombat && !busy && !resting && std::isfinite(fc) && fc >= 0.2f && roll);
    if (wasHeld && !block) motion::kuba_npc_set_block(npcEid, false, true);
    if (!combo) g.firstAt = now;
    g.attacker = attackerEid; g.startedAt = now; g.until = now + s_winMs / 1000.0; g.block = block; g.held = false; g.logged = false;
    if (block) g.held = motion::kuba_npc_set_block(npcEid, true, !wasHeld);   // its combat AI off for the window (once)
    ++g_kubaGuardN;
    if (g_kubaGuardN <= 80 || g_kubaGuardN % 50 == 0)
        logf("KUBA-23 swing guard npc=0x%X attacker=0x%X state=0x%X combat=%d facing_cos=%.2f chance=%d%% -> %s [n=%u]", npcEid, attackerEid,
             static_cast<unsigned>(vs), inCombat ? 1 : 0, fc, pct,
             block ? (g.held ? (combo ? "BLOCK UP (kept through the combo)" : "BLOCK UP (held until the blow)") : "block decided, the setter refused") :
             !inCombat ? "no (not in combat mode)" : busy ? "no (its own swing / reaction)" : resting ? "no (its AI's turn after a guard)" :
             fc < 0.2f ? "no (not facing the striker)" : "no (the roll)", g_kubaGuardN);
}

Result replay(const Request& rq) {
    Result res{};
    if (g_off.load()) { res.reason = kOff; c_refused.fetch_add(1); return res; }
    if (!resolve()) { res.reason = kNotArmed; c_refused.fetch_add(1); return res; }

    // ---- every pointer, this frame ----
    const uint32_t peid = player_eid();
    const uint32_t veid = rq.victimEid ? rq.victimEid : peid;
    if (!veid) { res.reason = kNoVictim; c_refused.fetch_add(1); return res; }
    if (rq.attackerEid == veid) { res.reason = kSameActor; c_refused.fetch_add(1); return res; }
    void* aCa = nullptr; void* aModel = nullptr;
    if (!motion::combat_parts(rq.attackerEid, (rq.flags & kFlagCreateCa) != 0, &aCa, &aModel)) { res.reason = kNoAttacker; c_refused.fetch_add(1); return res; }
    void* vCa = nullptr; void* vModel = nullptr;
    const bool victimIsPlayer = veid == peid;
    const bool vok = victimIsPlayer ? motion::player_combat_parts(&vCa, &vModel)
                                    : motion::combat_parts(veid, (rq.flags & kFlagCreateCa) != 0 || rq.createVictimCa, &vCa, &vModel);
    if (!vok || vCa == aCa) { res.reason = kNoVictim; c_refused.fetch_add(1); return res; }
    void* rpg = nullptr; void* back = nullptr;
    if (!rd(aCa, g_rpgOffset, &rpg) || !is_a(rpg, g_vftRpg) || !rd(rpg, 8, &back) || back != aCa) { res.reason = kNoProcessor; c_refused.fetch_add(1); return res; }
    void* vEnt = engine::entity_by_id(veid);
    float pos[3]{};
    if (!vEnt || !engine::entity_world_pos(vEnt, pos)) { res.reason = kNoPosition; c_refused.fetch_add(1); return res; }
    pos[2] += 1.2f;   // the chest, not the feet: the record's hit position is only used for effects
    void* vSoul = victimIsPlayer ? rttr::read_player_soul() : hits::soul_of_eid(veid);
    motion::read_model(veid, &res.victimModel);
    motion::read_model(rq.attackerEid, &res.attackerModel);
    if (refer_blocking((rq.flags & kFlagReferBlocking) != 0, (rq.flags & kFlagReferUnlessStriking) != 0, victim_blocking(res.victimModel),
                       res.attackerModel.state, (res.attackerModel.valid & motion::kMvState) != 0, res.victimModel.blockZone,
                       (res.victimModel.valid & motion::kMvBlockZone) != 0)) {
        res.reason = kVictimBlocking; c_refused.fetch_add(1); return res;
    }

    // ---- WO-178: a blow that cannot reach the local player on this screen is a miss (the replay has no contact test: it landed damage
    // at ~3 m in a live run while the player's own screen showed the copy out of reach) ----
    if (victimIsPlayer && wo178::hit_reach_on()) {
        float ap[3]{};
        void* aEnt = engine::entity_by_id(rq.attackerEid);
        const float d = (aEnt && engine::entity_world_pos(aEnt, ap)) ? std::sqrt((ap[0] - pos[0]) * (ap[0] - pos[0]) + (ap[1] - pos[1]) * (ap[1] - pos[1])) : -1.0f;
        const float limit = wo166::swing_reach_limit(rq.attackerEid);
        if (d >= 0.0f && !wo178rules::in_reach(d, limit)) {
            logf("WO178-REACH player blow=missed dist_m=%.2f reach_m=%.2f (the swing's contact distance + slack) attacker=0x%X -- out of reach on this screen: "
                 "nothing applied, the host is told 'missed'", d, limit, rq.attackerEid);
            res.reason = kOutOfReach; c_refused.fetch_add(1); return res;
        }
    }

    // ---- the attacker's four fields (WO-163 P1) ----
    if (!motion::read_attack_fields(aModel, &res.before)) { res.reason = kFieldsUnnamed; c_refused.fetch_add(1); return res; }
    res.written = res.before;
    if (!(rq.flags & kFlagOwnFields)) {
        res.written = rq.fields;
        if (!motion::write_attack_fields(aModel, rq.fields)) { res.reason = kFieldsUnnamed; c_refused.fetch_add(1); return res; }
    }

    // ---- the engine's structs, ours ----
    alignas(16) uint8_t result[kResultBytes]{};
    alignas(16) uint8_t hitIn[kHitInBytes]{};
    alignas(16) uint8_t details[kDetailsBytes]{};
    alignas(16) uint8_t flags[kFlagsBytes]{};
    alignas(16) uint8_t sub[kSubHitBytes]{};
    build_details(details, pos, veid);
    build_sub_hit(sub);
    // WO-178 (mp_own_block): the player's own held block decides a copy's blow (wo178_rules.h own_block)
    bool ownBlock = false;
    bool kubaBlock = false;   // KUBA 21: KUBA 19 decided this NPC blocks
    float ownFacing = -2.0f;
    if (victimIsPlayer && wo178::own_block_on() && (res.victimModel.valid & motion::kMvState)) {
        localstate::LocalState ls{};
        float ap[3]{};
        void* aEnt = engine::entity_by_id(rq.attackerEid);
        if (localstate::read_local_state(&ls) && aEnt && engine::entity_world_pos(aEnt, ap)) ownFacing = facing_cos(ls.rotZ, pos[0], pos[1], ap[0], ap[1]);
        ownBlock = wo178rules::own_block(true, res.victimModel.state, true, ownFacing);
        if (victim_blocking_state(res.victimModel.state, true))
            logf("WO178-OWNBLOCK player state=0x%X facing_cos=%.2f block=%s block_zone=%d->%d attacker=0x%X", static_cast<unsigned>(res.victimModel.state),
                 ownFacing, ownBlock ? "yes (the engine is told he blocks, facing the swing)" : "no (not facing the striker)", res.victimModel.blockZone,
                 ownBlock ? res.written.zone : res.victimModel.blockZone, rq.attackerEid);
    }
    // KUBA 19 (host; joiner 20:28-20:33 vs three guards: 0 blocks in the host's stream, every partner blow "hit" -- the replay is an
    // instant hit, the NPC's AI never saw a swing to raise its block against): an NPC in combat mode, facing the striking figure, not
    // striking or reeling itself, defends the blow by chance -- the engine computes the block (stamina, broken, the block reaction).
    // KUBA 23c (16.11 12:45-12:58): a guard held up for this very swing decides first, whatever else the model reads -- with its combat AI
    // switched off the blow often skipped KUBA 19's gate (12:51:43 BLOCK UP -> 12:51:44 "engine=hit", no KUBA-19 line at all)
    bool kubaGuardDone = false;
    // KUBA 30 stage 2: an NPC a partner's local duel drives -- his engine judged the blow on his screen (its own brain blocked it or not);
    // that verdict is the engine's input here, never a roll and never a guard raised on this paused body
    if (!victimIsPlayer && wo208::kuba_remote_duel(veid)) {
        kubaGuardDone = true;
        ownBlock = rq.kubaVerdict == 2;
        static uint32_t s_d = 0;
        if (++s_d <= 80 || s_d % 50 == 0)
            logf("KUBA-30 duel blow victim=0x%X the partner's verdict=%s -> %s [n=%u]", veid,
                 rq.kubaVerdict == 2 ? "blocked" : rq.kubaVerdict == 1 ? "hit" : "none", ownBlock ? "BLOCKS" : "hit", s_d);
    }
    if (!kubaGuardDone && !victimIsPlayer && rq.npcBlockPct > 0) {
        auto kg = g_kubaGuards.find(veid);
        if (kg != g_kubaGuards.end() && kg->second.attacker == rq.attackerEid && kuba_now_s() < kg->second.until && kg->second.block && kg->second.held) {
            kubaGuardDone = true;
            ownBlock = true;   // a real raised guard: the engine plays its own block (KUBA 21's fragment not needed)
            kg->second.until = kuba_now_s() + 0.4;
            static uint32_t s_g = 0;
            if (++s_g <= 80 || s_g % 50 == 0)
                logf("KUBA-23 blow on the raised guard victim=0x%X state=0x%X -> BLOCKS (the engine judges it, its own animation) (attack zone %d) [n=%u]", veid,
                     static_cast<unsigned>(res.victimModel.state), res.written.zone, s_g);
        }
    }
    if (!kubaGuardDone && !victimIsPlayer && rq.npcBlockPct > 0 && (res.victimModel.valid & motion::kMvState) && (res.victimModel.valid & motion::kMvCombatMode) &&
        res.victimModel.combatMode != 0) {
        const int32_t vs = res.victimModel.state;
        const bool busy = (vs & 0x8) || (vs & 0x40) || (vs & 0x20);   // Striking, Hit, Withdraw: no block from inside its own action
        float ap[3]{};
        void* aEnt = engine::entity_by_id(rq.attackerEid);
        const float fc = (aEnt && engine::entity_world_pos(aEnt, ap)) ? facing_cos(npcdrive::entity_yaw(vEnt), pos[0], pos[1], ap[0], ap[1]) : -2.0f;
        static uint32_t s_rng = 0x9E3779B9u;
        s_rng = s_rng * 1664525u + 1013904223u;
        bool roll = static_cast<int>((s_rng >> 8) % 100u) < rq.npcBlockPct;
        bool block = !busy && std::isfinite(fc) && fc >= 0.2f && roll;
        // KUBA 23: the swing's own decision stands (its guard is up now, or it was decided not to block)
        auto kg = g_kubaGuards.find(veid);
        const bool fromSwing = kg != g_kubaGuards.end() && kg->second.attacker == rq.attackerEid && kuba_now_s() < kg->second.until;
        if (fromSwing) {
            block = kg->second.block && std::isfinite(fc) && fc >= 0.2f;
            kg->second.until = kuba_now_s() + (kg->second.held ? 0.4 : 0.0);   // held a moment more for the block reaction, then let go
        } else if (kuba_swing_guard_on()) {
            // 23c: no guard was raised for this blow -- no block in the numbers alone (an invisible block: a hit on the screen, no damage)
            block = false;
        }
        static uint32_t s_n = 0;
        if (++s_n <= 80 || s_n % 50 == 0)
            logf("KUBA-19 npc block victim=0x%X state=0x%X facing_cos=%.2f chance=%u%%%s -> %s (attack zone %d) [n=%u]", veid, static_cast<unsigned>(vs), fc,
                 rq.npcBlockPct, fromSwing ? " (KUBA-23: the swing's decision)" : kuba_swing_guard_on() ? " (KUBA-23c: no guard raised for it)" : "",
                 block ? "BLOCKS (the engine judges the block)" : busy ? "busy (its own swing / reaction)" : fc < 0.2f ? "not facing the striker" : "no block",
                 res.written.zone, s_n);
        if (block) { ownBlock = true; kubaBlock = !(fromSwing && kg->second.held); }   // KUBA 21's fragment only where no real guard was up
    }
    build_hit_in(hitIn, (rq.flags & kFlagSkipFilter) != 0, vCa, details, sub, sub + kSubHitBytes, ownBlock);

    res.seq = ++g_seq;
    {
        std::lock_guard<std::mutex> lock(g_ringMutex);
        Slot* s = slot_for(res.seq);
        s->seq = res.seq; s->d = Damage{1, 0, 0, true};
    }
    c_calls.fetch_add(1);
    bool ok = call_ctor(g_fnCtor, result);
    bool ret = false;
    hits::ReplayCapture cap{};
    if (ok) {
        uint8_t prevIntended = 0;
        const bool marked = (rq.flags & kFlagIntended) && wo173::mark_intended(aModel, true, &prevIntended);
        hits::replay_begin(rq.attackerEid, veid, res.seq, vSoul, victimIsPlayer);
        // WO-178: his own held block decides -- it faces the swing as the engine's "block in place" would (a copy's swing never puts
        // him there: live, block zone 2 vs the swing's zone 5 left a "blocked" blow at almost a hit's damage); put back after the call
        int32_t prevBz = 0;
        const bool bzSet = ownBlock && motion::write_block_zone(vModel, res.written.zone, &prevBz);
        int32_t prevState = 0;
        const bool stSet = rq.strikeForCall && motion::read_state(aModel, &prevState) && prevState != kStateStriking && motion::write_state(aModel, kStateStriking);
        ok = call_process(g_fnProcess, rpg, result, hitIn, flags, &ret);
        if (stSet) motion::write_state(aModel, prevState);
        if (bzSet) motion::write_block_zone(vModel, prevBz, nullptr);
        hits::replay_end(&cap);
        if (marked && !prevIntended) wo173::mark_intended(aModel, false, nullptr);
        res.called = true;
    }
    // WO-173: the collision handler's reaction tail, in the same frame, with the result struct the processor filled
    bool react = ok && ret && cap.seen && (rq.flags & kFlagReact);
    if (react && victimIsPlayer) {
        // WO-178: the player's own recoil only for a blow that can reach him on this screen -- the engine's hit core gives no damage
        // past reach (live: pushed back ~3 m by the recoils, the held copy's later blows landed nothing, yet each played a recoil)
        float ap[3]{};
        void* aEnt = engine::entity_by_id(rq.attackerEid);
        const float d = (aEnt && engine::entity_world_pos(aEnt, ap)) ? std::sqrt((ap[0] - pos[0]) * (ap[0] - pos[0]) + (ap[1] - pos[1]) * (ap[1] - pos[1])) : -1.0f;
        const float rlimit = wo166::swing_reach_limit(rq.attackerEid);
        if (!wo178rules::in_reach(d, rlimit)) {
            react = false;
            res.react = static_cast<uint8_t>(wo173rules::React::NotRun);
            logf("WO178-RECOIL player reaction=out-of-reach dist_m=%.2f reach_m=%.2f seq=%u -- no recoil for a blow that cannot reach", d, rlimit, res.seq);
        } else {
            logf("WO178-RECOIL player reaction=asked dist_m=%.2f seq=%u", d, res.seq);
        }
    }
    if (react) res.react = wo173::react(aCa, vCa, result, &res.reactKind);
    if (!(rq.flags & (kFlagKeepFields | kFlagOwnFields))) motion::write_attack_fields(aModel, res.before);
    if (!ok) {
        switch_off(res.called ? "RPGProcessHit" : "the result constructor");
        res.reason = kFault;
        std::lock_guard<std::mutex> lock(g_ringMutex);
        slot_for(res.seq)->d.state = 0;
        return res;
    }
    res.returned = ret;
    res.seen = cap.seen;
    std::memcpy(res.flags, flags, sizeof flags);
    if (cap.seen) {
        res.recBlock = cap.rec[0x54]; res.recSecond = cap.rec[0x55]; res.recPerfect = cap.rec[0x56]; res.recBroken = cap.rec[0x57];
        res.recZoneMismatch = cap.rec[0x61];
    } else {
        std::lock_guard<std::mutex> lock(g_ringMutex);
        slot_for(res.seq)->d = Damage{2, 0, 0, true};   // no record, no blow: nothing to measure
    }
    res.outcome = classify(ret, cap.seen, flags, cap.seen ? cap.rec : nullptr);
    // WO-210 C.1 (mp_death_engine): the handler's last step on the same result -- ClientHit (the engine's own hit event: the reaction of a
    // victim out of combat mode, a lethal hit's death, the corpse's dead state the loot check asks for). NPC victims only; "Hit was
    // filtered" skips it, as the handler does.
    if (cap.seen && !victimIsPlayer && (rq.flags & kFlagReact) && res.outcome != Outcome::Filtered && wo210::on(wo210rules::kBitDeathEngine))
        res.clientHit = wo173::client_hit(result, res.react, static_cast<uint8_t>(res.outcome));
    switch (res.outcome) {
        case Outcome::Hit: c_hits.fetch_add(1); break;
        case Outcome::Blocked: c_blocked.fetch_add(1); break;
        case Outcome::PerfectBlock: c_perfect.fetch_add(1); break;
        case Outcome::Broken: c_broken.fetch_add(1); break;
        case Outcome::Filtered: c_filtered.fetch_add(1); break;
        default: c_none.fetch_add(1); break;
    }
    // KUBA 21 (host, 16.11 11:20-11:31: KUBA 19's 12 blocks were all "engine=blocked", yet the guard played CombatHitGen each time -- on
    // the host and so on the joiner's copy: a block in the numbers, a hit on the screen. The replay is an instant hit, the NPC was never in
    // its guard, the reaction tail picks the hit reaction). A blow KUBA 19 had it block, and the engine judged blocked, gets the block's own
    // fragment on the NPC over the hit reaction (the NPC's natural block: C_CombatAnimAction CombatBlockGen 900, prio 5, flags 0x1A20;
    // perfect 901, broken 899) -- the host's stream then carries it to the copy. KCDMP_KUBA_BLOCK_ANIM=off.
    if (kubaBlock && !victimIsPlayer && (res.outcome == Outcome::Blocked || res.outcome == Outcome::PerfectBlock || res.outcome == Outcome::Broken)) {
        static int s_on = -1;
        if (s_on < 0) {
            char v[16] = {0};
            const DWORD n = GetEnvironmentVariableA("KCDMP_KUBA_BLOCK_ANIM", v, sizeof v);
            s_on = (n > 0 && n < sizeof v && (!_stricmp(v, "off") || !std::strcmp(v, "0"))) ? 0 : 1;
            logf("KUBA-21 an NPC's block of a partner's blow plays its block animation %s (KCDMP_KUBA_BLOCK_ANIM=off turns it off)", s_on ? "on" : "OFF");
        }
        if (s_on) {
            const uint32_t frag = res.outcome == Outcome::PerfectBlock ? 901u : res.outcome == Outcome::Broken ? 899u : 900u;
            uint8_t tg[20]{};
            if (!wo187::tag_state(veid, tg, sizeof tg)) std::memset(tg, 0, sizeof tg);
            void* act = nullptr;
            const rttr::SwingResult r = rttr::install_fragment(veid, frag, tg, 6, 0x1A20, &act);
            if (r == rttr::SwingResult::Ok && act) rttr::release_action(act);
            static uint32_t s_n = 0;
            if (++s_n <= 80 || s_n % 50 == 0)
                logf("KUBA-21 npc block anim victim=0x%X outcome=%s frag=%u -> %s [n=%u]", veid,
                     res.outcome == Outcome::PerfectBlock ? "perfect" : res.outcome == Outcome::Broken ? "broken" : "blocked", frag, rttr::swing_result_name(r), s_n);
        }
    }
    res.reason = kOk;
    if (cap.seen) {
        // the probe's evidence: the record the engine built (its gates and factors) and the result it filled -- why a blow did what it did
        auto f32 = [&](size_t o) { float v; std::memcpy(&v, cap.rec + o, 4); return v; };
        uint64_t aw = 0, ww = 0, bw = 0;
        std::memcpy(&aw, cap.rec + 0x78, 8); std::memcpy(&ww, cap.rec + 0x80, 8); std::memcpy(&bw, cap.rec + 0x88, 8);
        int32_t dmgType = 0, subpart = 0;
        std::memcpy(&dmgType, cap.rec + 0x24, 4); std::memcpy(&subpart, cap.rec + 0x50, 4);
        float apos[3]{};
        void* aEnt = engine::entity_by_id(rq.attackerEid);
        const bool ap = aEnt && engine::entity_world_pos(aEnt, apos);
        const float dx = pos[0] - apos[0], dy = pos[1] - apos[1];
        logf("WO165-REC seq=%u dist_m=%.2f dmg_type=%d strength=%.2f factorC=%.2f subpart=%d gates=[combo %u riposte %u intentional %u b5C %u b5D %u b5E %u b5F %u kind %u f62 %u f63 %u f64 %u f65 %u] "
             "factorA=%.3f factorB=%.3f f70=%.3f weapon=%016llX second=%016llX block_weapon=%016llX tid=%lu",
             res.seq, ap ? std::sqrt(dx * dx + dy * dy) : -1.0f, dmgType, f32(0x2C), f32(0x30), subpart, cap.rec[0x59], cap.rec[0x5A], cap.rec[0x5B],
             cap.rec[0x5C], cap.rec[0x5D], cap.rec[0x5E], cap.rec[0x5F], cap.rec[0x60], cap.rec[0x62], cap.rec[0x63], cap.rec[0x64], cap.rec[0x65],
             f32(0x68), f32(0x6C), f32(0x70), static_cast<unsigned long long>(aw), static_cast<unsigned long long>(ww),
             static_cast<unsigned long long>(bw), GetCurrentThreadId());
        char hex[3 * 0xA0 + 1]{};
        for (size_t i = 0; i < 0xA0; ++i) std::snprintf(hex + 3 * i, 4, "%02X ", result[i]);
        logf("WO165-RESULT seq=%u %s", res.seq, hex);
    }
    const motion::ModelRead& vm = res.victimModel;
    logf("WO165-REPLAY seq=%u attacker=0x%X victim=0x%X%s engine=%s returned=%d core=%s flags=[blk %u zone %u dmg %u pb %u wpn %u] "
         "rec=[blk %u second %u pb %u broken %u zone_mismatch %u] fields at=%d az=%d ah=%d as=%.2f (were at=%d az=%d ah=%d as=%.2f) "
         "victim state=0x%X gz=%d bz=%d pb=%u opp=0x%X%s attacker_state=0x%X tid=%lu",
         res.seq, rq.attackerEid, veid, victimIsPlayer ? "(player)" : "", outcome_name(res.outcome), ret ? 1 : 0, cap.seen ? "ran" : "not-run",
         flags[0], flags[1], flags[2], flags[3], flags[0xC], res.recBlock, res.recSecond, res.recPerfect, res.recBroken, res.recZoneMismatch,
         res.written.type, res.written.zone, res.written.hand, res.written.strength, res.before.type, res.before.zone, res.before.hand,
         res.before.strength, static_cast<unsigned>(vm.state), vm.guardZone, vm.blockZone, vm.perfectBlock, vm.opponentEid,
         vm.opponentIsPlayer ? "(me)" : "", static_cast<unsigned>(res.attackerModel.state), GetCurrentThreadId());
    return res;
}

Damage damage_of(uint32_t seq) {
    std::lock_guard<std::mutex> lock(g_ringMutex);
    Slot* s = slot_for(seq);
    return s->seq == seq ? s->d : Damage{};
}

// ---------------------------------------------------------------------------------------------------- C1
namespace {
std::atomic<bool> g_lockOn{true}, g_lockOff{false};
struct LockRow { bool paired = false; double farSince = -1, notSetLogged = -1e9, onFigureSince = -1; };   // WO-166 C1: onFigureSince
std::unordered_map<uint32_t, LockRow> g_locks;   // main thread only
double g_lockNext = 0;
std::atomic<uint32_t> c_lockSet{0}, c_lockRemoved{0}, c_lockForgot{0}, c_lockFail{0};

double qpc_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

const char* npc_name(uint32_t eid) {
    void* e = engine::entity_by_id(eid);
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "?";
}

bool npc_alive(uint32_t eid) {
    void* soul = hits::soul_of_eid(eid);
    float hp = 0;
    return soul && rttr::soul_state(soul, "health", &hp) && hp > 0.0f;
}

// The host leaves its skirmish only when no other pair of ours remains and no NPC fights the host himself (that fight is his own).
void drop_pair(uint32_t npc, const char* why, float dist, bool hostInOwnFight) {
    int others = 0;
    for (const auto& kv : g_locks) if (kv.first != npc && kv.second.paired) ++others;
    const char* how = "kept (another pair of ours remains)";
    if (!others && hostInOwnFight) how = "kept (the host is in a fight of his own)";
    else if (!others) {
        uint64_t rv = 0;
        const uint32_t peid = player_eid();
        void* hs = peid ? hits::soul_of_eid(peid) : nullptr;
        const bool ok = hs && hits::skirmish_remove(hs, &rv);
        how = ok ? "done" : "FAILED";
        if (!ok) c_lockFail.fetch_add(1);
    }
    c_lockRemoved.fetch_add(1);
    logf("WO165-LOCK npc=%s pair=removed why=%s dist_m=%.1f skirmish_leave=%s tid=%lu", npc_name(npc), why, dist, how, GetCurrentThreadId());
}
}   // namespace

void set_host_lock(bool on) {
    if (g_lockOn.exchange(on) == on) return;
    logf("WO165-LOCK mp_host_lock %s tid=%lu", on ? "on" : "off", GetCurrentThreadId());
    if (!on) g_lockNext = 0;   // the next tick releases every pair
}
bool host_lock() { return g_lockOn.load() && !g_lockOff.load(); }

void lock_tick(const uint32_t* npcs, int n) {
    const double now = qpc_s();
    const bool on = host_lock();
    if (!on && g_locks.empty()) return;
    if (now < g_lockNext) return;
    g_lockNext = now + 0.25;
    localstate::LocalState ls{};
    if (!localstate::read_local_state(&ls)) return;
    uint32_t aeids[16]; void* asouls[16];
    const int na = hits::avatar_list(aeids, asouls, 16);
    // what each candidate is doing, read this frame
    bool hostInOwnFight = false;
    struct C { uint32_t eid; LockView v; };
    C cs[96]; int nc = 0;
    for (int i = 0; i < n && nc < 96; ++i) {
        motion::NpcCombat c{};
        if (!motion::read_npc_combat(npcs[i], &c)) continue;
        if (c.opponentIsPlayer) hostInOwnFight = true;
        float p[3]{};
        void* e = engine::entity_by_id(npcs[i]);
        if (!e || !engine::entity_world_pos(e, p)) continue;
        LockView v{};
        v.alive = npc_alive(npcs[i]);
        v.combat = c.combat != 0 || c.opponentEid != 0 || c.opponentIsPlayer != 0;
        v.oppIsHost = c.opponentIsPlayer != 0;
        v.oppIsAvatar = c.opponentEid && motion::is_avatar_eid(c.opponentEid);
        v.dist = std::sqrt((p[0] - ls.x) * (p[0] - ls.x) + (p[1] - ls.y) * (p[1] - ls.y));
        v.facingCos = facing_cos(ls.rotZ, ls.x, ls.y, p[0], p[1]);
        float nearest = v.dist;
        for (int k = 0; k < na; ++k) {
            float ap[3]{};
            void* ae = engine::entity_by_id(aeids[k]);
            if (ae && engine::entity_world_pos(ae, ap))
                nearest = (std::min)(nearest, std::sqrt((p[0] - ap[0]) * (p[0] - ap[0]) + (p[1] - ap[1]) * (p[1] - ap[1])));
        }
        LockRow& row = g_locks[npcs[i]];
        if (nearest > kLockFarM) { if (row.farSince < 0) row.farSince = now; } else row.farSince = -1;
        // WO-166 C1 (diagnostic): an NPC on a partner's figure within 3 m for 10 s that commits no attack says what both combat models
        // read, once per NPC per minute -- the field's zero blows on the figure (0.48.0) was not reproduced by the harness (the same
        // setup landed blows every ~1.5 s), so the next session names the gate
        if (v.oppIsAvatar && v.alive) {
            float od = 1e9f;
            float op[3]{};
            void* oe = engine::entity_by_id(c.opponentEid);
            if (oe && engine::entity_world_pos(oe, op)) od = std::sqrt((p[0] - op[0]) * (p[0] - op[0]) + (p[1] - op[1]) * (p[1] - op[1]));
            if (od <= 3.0f) {
                if (row.onFigureSince < 0) row.onFigureSince = now;
                const double la = motion::last_npc_attack_s(npcs[i]);
                static kcdmp::wo166rules::LineLimiter s_c1;
                if (now - row.onFigureSince >= 10.0 && (la < 0 || now - la >= 10.0) && s_c1.allow(npcs[i], now)) {
                    motion::ModelRead nm{}, fm{};
                    const bool nok = motion::read_model(npcs[i], &nm), fok = motion::read_model(c.opponentEid, &fm);
                    logf("WO166-C1 npc=%s on the figure eid=0x%X for %.0f s within %.1f m, no attack committed for %s: npc state=0x%X cm=%u at=%d "
                         "figure state=0x%X cm=%u opp=%s automation_mode=%u tid=%lu", npc_name(npcs[i]), c.opponentEid, now - row.onFigureSince, od,
                         la < 0 ? "ever" : "10 s+", nok ? static_cast<unsigned>(nm.state) : 0u, nok ? nm.combatMode : 0u, nok ? nm.atkType : -9,
                         fok ? static_cast<unsigned>(fm.state) : 0u, fok ? fm.combatMode : 0u,
                         fok ? (fm.opponentEid == npcs[i] ? "this-npc" : fm.opponentEid ? "another" : "none") : "?",
                         kcdmp::wo166::auto_mode(), GetCurrentThreadId());
                }
            } else row.onFigureSince = -1;
        } else row.onFigureSince = -1;
        v.farForS = row.farSince < 0 ? 0.0 : now - row.farSince;
        v.paired = row.paired;
        cs[nc++] = C{npcs[i], v};
    }
    // a pair whose NPC is no longer a candidate (WO-136 forgot it, the body is gone): the fight is over for us
    for (auto it = g_locks.begin(); it != g_locks.end();) {
        bool seen = false;
        for (int i = 0; i < nc; ++i) if (cs[i].eid == it->first) { seen = true; break; }
        if (!seen && it->second.paired) { it->second.paired = false; drop_pair(it->first, "npc-gone", -1.0f, hostInOwnFight); }
        if (!seen) it = g_locks.erase(it); else ++it;
    }
    for (int i = 0; i < nc; ++i) {
        LockRow& row = g_locks[cs[i].eid];
        // WO-178 (mp_host_lock_answer, default on since 0.50.6): an NPC fighting a partner's figure becomes lockable by the hostility
        // answer instead of a hostile skirmish pair -- no skirmish is touched; a pair this module already holds is released as usual
        if (kcdmp::wo178::host_answer_on()) {
            const LockView& v = cs[i].v;
            if (v.alive && v.combat && v.oppIsAvatar && !v.oppIsHost && v.dist <= kLockDropM) kcdmp::wo178::offer_local(cs[i].eid);
            if (!row.paired) continue;
        }
        LockDecision d = on ? lock_rule(cs[i].v) : LockDecision{cs[i].v.paired ? LockAct::Remove : LockAct::None, "mp_host_lock-off"};
        switch (d.act) {
            case LockAct::Set: {
                if (hits::scene_owned_eid(cs[i].eid, "wo165-lock")) break;   // WO-174: the host never joins a scripted brawl's skirmish through the mod's lock
                const uint32_t peid = player_eid();
                void* hs = peid ? hits::soul_of_eid(peid) : nullptr;
                void* ns = hits::soul_of_eid(cs[i].eid);
                uint64_t rv = 0;
                const bool ok = hs && ns && hits::skirmish_add(hs, ns, 1, &rv);
                if (ok) { row.paired = true; c_lockSet.fetch_add(1); }
                else if (c_lockFail.fetch_add(1) + 1 >= 3 && !g_lockOff.exchange(true))
                    logf("WO165-LOCK switched OFF for this session: the skirmish add failed 3 times (the host can no longer lock onto a partner's attacker) tid=%lu",
                         GetCurrentThreadId());
                logf("WO165-LOCK npc=%s pair=%s why=%s dist_m=%.1f facing_cos=%.2f skirmish=%s tid=%lu", npc_name(cs[i].eid), ok ? "set" : "not-set", d.why,
                     cs[i].v.dist, cs[i].v.facingCos, ok ? "done" : "FAILED", GetCurrentThreadId());
                break;
            }
            case LockAct::Remove:
                row.paired = false;
                drop_pair(cs[i].eid, d.why, cs[i].v.dist, hostInOwnFight);
                break;
            case LockAct::Forget:
                row.paired = false;
                c_lockForgot.fetch_add(1);
                logf("WO165-LOCK npc=%s pair=forgotten why=%s dist_m=%.1f (the host's own fight: not left) tid=%lu", npc_name(cs[i].eid), d.why, cs[i].v.dist,
                     GetCurrentThreadId());
                break;
            case LockAct::None: {
                // a candidate within reach that was not paired says why, so a missed lock explains itself -- WO-166 C5: once per NPC per
                // minute (the field: 417 lines "npc-dead" in one session at the old 5 s)
                static kcdmp::wo166rules::LineLimiter s_notSet;
                if (on && cs[i].v.dist <= kLockDropM && s_notSet.allow(cs[i].eid, now)) {
                    row.notSetLogged = now;
                    logf("WO165-LOCK npc=%s pair=not-set why=%s dist_m=%.1f facing_cos=%.2f tid=%lu", npc_name(cs[i].eid), lock_not_set_why(cs[i].v),
                         cs[i].v.dist, cs[i].v.facingCos, GetCurrentThreadId());
                }
                break;
            }
            default: break;
        }
    }
}

int status_text(char* out, int n) {
    return std::snprintf(out, n, "replay=%s calls=%u hit=%u blocked=%u pb=%u broken=%u filtered=%u none=%u refused=%u faults=%u seen=%u "
                         "lock=%s lock_set=%u lock_removed=%u lock_forgotten=%u lock_failed=%u",
                         g_off.load() ? "OFF(fault)" : g_armed ? "armed" : g_resolved ? "not-armed" : "unresolved", c_calls.load(), c_hits.load(),
                         c_blocked.load(), c_perfect.load(), c_broken.load(), c_filtered.load(), c_none.load(), c_refused.load(), c_faults.load(),
                         hits::replay_seen(), g_lockOff.load() ? "OFF(fault)" : g_lockOn.load() ? "on" : "off", c_lockSet.load(), c_lockRemoved.load(),
                         c_lockForgot.load(), c_lockFail.load());
}

}   // namespace kcdmp::wo165
