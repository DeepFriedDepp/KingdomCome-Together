// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-165: the replay through the engine's hit processor (wo165.h).
#include "wo165.h"

#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>

#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "log.h"
#include "rttr_abi.h"

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
    if (!copy_bytes(process, pro, sizeof pro) || std::memcmp(pro, kProcessPrologue, sizeof pro) != 0) {
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
                                    : motion::combat_parts(veid, (rq.flags & kFlagCreateCa) != 0, &vCa, &vModel);
    if (!vok || vCa == aCa) { res.reason = kNoVictim; c_refused.fetch_add(1); return res; }
    void* rpg = nullptr; void* back = nullptr;
    if (!rd(aCa, g_rpgOffset, &rpg) || !is_a(rpg, g_vftRpg) || !rd(rpg, 8, &back) || back != aCa) { res.reason = kNoProcessor; c_refused.fetch_add(1); return res; }
    void* vEnt = engine::entity_by_id(veid);
    float pos[3]{};
    if (!vEnt || !engine::entity_world_pos(vEnt, pos)) { res.reason = kNoPosition; c_refused.fetch_add(1); return res; }
    pos[2] += 1.2f;   // the chest, not the feet: the record's hit position is only used for effects
    void* vSoul = victimIsPlayer ? rttr::read_player_soul() : hits::soul_of_eid(veid);
    motion::read_model(veid, &res.victimModel);

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
    build_hit_in(hitIn, (rq.flags & kFlagSkipFilter) != 0, vCa, details, sub, sub + kSubHitBytes);

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
        hits::replay_begin(rq.attackerEid, veid, res.seq, vSoul, victimIsPlayer);
        ok = call_process(g_fnProcess, rpg, result, hitIn, flags, &ret);
        hits::replay_end(&cap);
        res.called = true;
    }
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
    switch (res.outcome) {
        case Outcome::Hit: c_hits.fetch_add(1); break;
        case Outcome::Blocked: c_blocked.fetch_add(1); break;
        case Outcome::PerfectBlock: c_perfect.fetch_add(1); break;
        case Outcome::Broken: c_broken.fetch_add(1); break;
        case Outcome::Filtered: c_filtered.fetch_add(1); break;
        default: c_none.fetch_add(1); break;
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
         "victim state=0x%X gz=%d bz=%d pb=%u opp=0x%X%s tid=%lu",
         res.seq, rq.attackerEid, veid, victimIsPlayer ? "(player)" : "", outcome_name(res.outcome), ret ? 1 : 0, cap.seen ? "ran" : "not-run",
         flags[0], flags[1], flags[2], flags[3], flags[0xC], res.recBlock, res.recSecond, res.recPerfect, res.recBroken, res.recZoneMismatch,
         res.written.type, res.written.zone, res.written.hand, res.written.strength, res.before.type, res.before.zone, res.before.hand,
         res.before.strength, static_cast<unsigned>(vm.state), vm.guardZone, vm.blockZone, vm.perfectBlock, vm.opponentEid,
         vm.opponentIsPlayer ? "(me)" : "", GetCurrentThreadId());
    return res;
}

Damage damage_of(uint32_t seq) {
    std::lock_guard<std::mutex> lock(g_ringMutex);
    Slot* s = slot_for(seq);
    return s->seq == seq ? s->d : Damage{};
}

int status_text(char* out, int n) {
    return std::snprintf(out, n, "replay=%s calls=%u hit=%u blocked=%u pb=%u broken=%u filtered=%u none=%u refused=%u faults=%u seen=%u",
                         g_off.load() ? "OFF(fault)" : g_armed ? "armed" : g_resolved ? "not-armed" : "unresolved", c_calls.load(), c_hits.load(),
                         c_blocked.load(), c_perfect.load(), c_broken.load(), c_filtered.load(), c_none.load(), c_refused.load(), c_faults.load(),
                         hits::replay_seen());
}

}   // namespace kcdmp::wo165
