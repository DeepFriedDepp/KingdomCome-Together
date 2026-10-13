// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-173: the joiner's real hits on the host (wo173.h).
#include "wo173.h"
#include "wo173_rules.h"

#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>

#include "anchors.h"
#include "buffs.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "rttr_abi.h"
#include "wo163.h"
#include "wo165.h"
#include "wo166.h"
#include "wo166_rules.h"
#include "wo172.h"
#include "wo178.h"
#include "wo178_rules.h"

namespace kcdmp::wo173 {
namespace {

using namespace kcdmp::wo173rules;

constexpr char kCallerLabel[] = "wh::combatmodule::C_CombatActorCollisionProcess::OnCollision";
constexpr char kChaLabel[] = "wh::combatmodule::C_CombatActorCollisionProcess::CreateHitAction";

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "wo173::rd");
    if (!base || !fault::plausible_address(reinterpret_cast<uintptr_t>(base) + off)) return false;
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}
bool copy_bytes(const void* src, void* dst, size_t n) {
    KCDMP_FAULT_READ(site, "wo173::copy_bytes");
    return fault::guarded(site, [&] { std::memcpy(dst, src, n); });
}
bool prologue_is(uintptr_t fn, const uint8_t* pat, size_t n) {
    uint8_t b[32]{};
    return fn && n <= sizeof b && copy_bytes(reinterpret_cast<const void*>(fn), b, n) && std::memcmp(b, pat, n) == 0;
}

// ---- the calls into the engine's reaction tail (each guarded; a fault switches the tail off for the session) ----
bool call_b1(uintptr_t fn, void* a, bool* out) {
    KCDMP_FAULT_CALL(site, "wo173::call_victim_test");
    return fault::guarded(site, [&] { *out = reinterpret_cast<bool (__fastcall*)(void*)>(fn)(a); });
}
bool call_kind(uintptr_t fn, int32_t in, bool flag, int32_t* out) {
    KCDMP_FAULT_CALL(site, "wo173::call_reaction_kind");
    return fault::guarded(site, [&] { *out = reinterpret_cast<int32_t (__fastcall*)(void*, int32_t, bool)>(fn)(nullptr, in, flag); });
}
bool call_cha(uintptr_t fn, void* self, void** out, void* victim, int32_t kind, void* result) {
    KCDMP_FAULT_CALL(site, "wo173::call_create_hit_action");
    return fault::guarded(site, [&] { reinterpret_cast<void** (__fastcall*)(void*, void**, void*, int32_t, void*)>(fn)(self, out, victim, kind, result); });
}
bool call_set_action(uintptr_t fn, void* director, void** sp, bool* out) {
    KCDMP_FAULT_CALL(site, "wo173::call_set_action");
    return fault::guarded(site, [&] { *out = reinterpret_cast<bool (__fastcall*)(void*, void**)>(fn)(director, sp); });
}
bool call_setter(uintptr_t fn, void* prop, bool v) {
    KCDMP_FAULT_CALL(site, "wo173::call_intended_setter");
    return fault::guarded(site, [&] { reinterpret_cast<void (__fastcall*)(void*, bool)>(fn)(prop, v); });
}
bool vcall_u32(void* obj, uint8_t slot, uint32_t* out) {
    KCDMP_FAULT_CALL(site, "wo173::call_flags_getter");
    void* vt = nullptr; void* fn = nullptr;
    if (!rd(obj, 0, &vt) || !vt || !rd(vt, slot, &fn) || !fn) return false;
    return fault::guarded(site, [&] { *out = reinterpret_cast<uint32_t (__fastcall*)(void*)>(fn)(obj); });
}
bool vcall0(void* obj, uint8_t slot) {
    KCDMP_FAULT_CALL(site, "wo173::call_action_ref");
    void* vt = nullptr; void* fn = nullptr;
    if (!rd(obj, 0, &vt) || !vt || !rd(vt, slot, &fn) || !fn) return false;
    return fault::guarded(site, [&] { reinterpret_cast<void (__fastcall*)(void*)>(fn)(obj); });
}

// ---- state ----
std::atomic<bool> g_on{true};          // mp_real_hits (the agent's switch; off in 0.49.0, on since 0.49.1: WO-174 D, kept on for 0.50.0 by the maintainer)
std::atomic<bool> g_threat{true};      // WO-136's avatar-hit threat on engine-processed blows (C3 decides)
std::atomic<bool> g_reactOn{true};     // the reaction tail (a sub-switch: mp_real_hits_react)
std::atomic<bool> g_faulted{false};    // the whole path switched off by a fault this session
std::atomic<bool> g_reactFaulted{false};
bool g_reactResolved = false, g_reactArmed = false;
const char* g_reactWhy = "not resolved";
ReactionSites g_sites{};
void* const* g_vftHitAction = nullptr;   // WO-178
std::atomic<uint32_t> c_playerRecoil{0}, c_playerKept{0};
uintptr_t g_setAction = 0;

struct Pending {
    uint32_t seq = 0, veid = 0, avatarEid = 0;
    std::string name;
    unsigned char guid[16]{};
    float joinerSt = 0, joinerHp = 0, st0 = -1, hp0 = -1, stMax = 100, capSt = -1;
    uint8_t outcome = 0, react = 0;
    int32_t reactKind = -1;
    bool engine = false, stateRead = false;
    int32_t stateAfter = 0;
    Fallback fb = Fallback::None;
    double t0 = 0;
    int8_t atkType = -1;      // WO-178 C4: the row's attack type (-1 unknown) -- the engine's cost is kept per kind
    float capMedian = -1;     // the engine median the fallback's stamina was held to (-1 none known: the default)
    double heldS = -1;        // how long the blow waited for the figure's strike window (-1 not held)
};
std::vector<Pending> g_pending;                       // main thread only
std::unordered_map<uint32_t, float> g_stMaxSeen;      // NPC entity id -> highest stamina seen (main thread)

std::atomic<uint32_t> c_kept{0};   // WO-176 (A1): blows refused because the victim is in a quest's arranged fight
std::atomic<uint32_t> c_requests{0}, c_engine{0}, c_fallback{0}, c_hit{0}, c_blocked{0}, c_pb{0}, c_broken{0}, c_reactQueued{0}, c_reactOther{0},
    c_corrected{0}, c_raised{0}, c_koAfter{0}, c_koFullStamina{0}, c_capped{0}, c_unmeasured{0}, c_stateHit{0}, c_faults{0};
uint32_t c_fb[16]{};

// WO-178 C4 (1): blows that wait for the figure's strike window, and their answers once done (main thread).
struct Held { std::vector<uint8_t> body; double t0 = 0; uint32_t id = 0; uint32_t avatarEid = 0; };
std::vector<Held> g_held;
std::unordered_map<uint32_t, std::vector<uint8_t>> g_heldDone;   // id -> op 18's 33-byte answer
uint32_t g_heldSeq = 0;
std::atomic<uint32_t> c_held{0}, c_heldEngine{0}, c_heldRaw{0}, c_heldOther{0}, c_floorStopped{0}, c_medianCapped{0};
// WO-178 C4 (2): the engine's own stamina cost of a landed blow, per NPC and attack kind, and per kind alone (main thread).
std::unordered_map<uint64_t, kcdmp::wo178rules::Ring> g_medNpc;
std::unordered_map<int, kcdmp::wo178rules::Ring> g_medKind;
float engine_median(uint32_t veid, int8_t type) {
    if (type < 0) return -1.0f;
    auto a = g_medNpc.find((static_cast<uint64_t>(veid) << 8) | static_cast<uint8_t>(type));
    if (a != g_medNpc.end() && a->second.n > 0) return a->second.median();
    auto b = g_medKind.find(type);
    if (b != g_medKind.end() && b->second.n > 0) return b->second.median();
    return -1.0f;
}

void switch_off(const char* what) {
    if (g_faulted.exchange(true)) return;
    c_faults.fetch_add(1);
    logf("WO173-HIT switched OFF for this session: %s -- the joiner's blows go the raw way (capped) until the game restarts", what);
}

void react_off(const char* what) {
    if (g_reactFaulted.exchange(true)) return;
    c_faults.fetch_add(1);
    logf("WO173-REACT switched OFF for this session: %s faulted (the fault guard logged the site) -- blows still go through the engine, "
         "without the victim's reaction", what);
}

bool in_module_text(HMODULE m, uintptr_t p) {
    anchor::Range t{};
    return m && anchor::section(m, ".text", &t) && t.contains(reinterpret_cast<const void*>(p));
}

bool resolve_react() {
    if (g_reactResolved) return g_reactArmed;
    g_reactResolved = true;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    HMODULE fw = GetModuleHandleA("Framework.dll");
    if (!cm || !fw) { g_reactWhy = "CombatModule / Framework not loaded"; logf("WO173-REACT not armed: %s", g_reactWhy); return false; }
    int n = 0;
    const uint8_t* caller = anchor::function_by_string(cm, kCallerLabel, &n);
    anchor::Range text{};
    if (!caller || !anchor::section(cm, ".text", &text) || !text.contains(caller)) {
        g_reactWhy = "the collision handler is not found by its label"; logf("WO173-REACT not armed: %s", g_reactWhy); return false;
    }
    static uint8_t buf[0x1000];
    const size_t want = static_cast<size_t>(text.end - caller) < sizeof buf ? static_cast<size_t>(text.end - caller) : sizeof buf;
    if (!copy_bytes(caller, buf, want)) { g_reactWhy = "the collision handler could not be read"; logf("WO173-REACT not armed: %s", g_reactWhy); return false; }
    ReactionSites s{};
    if (!parse_reaction_sites(buf, want, reinterpret_cast<uintptr_t>(caller), &s)) {
        g_reactWhy = "the reaction block / hand-over / group test is not in the collision handler (once each)"; logf("WO173-REACT not armed: %s", g_reactWhy); return false;
    }
    const uint8_t* cha = anchor::function_by_string(cm, kChaLabel, &n);
    if (!cha || reinterpret_cast<uintptr_t>(cha) != s.createHitAction) {
        g_reactWhy = "the handler calls another function than the CreateHitAction label's"; logf("WO173-REACT not armed: %s", g_reactWhy); return false;
    }
    const uintptr_t calls[7] = {s.alive, s.ownReaction, s.attackerFlag, s.kind, s.createHitAction, s.intendedSetter, s.interruptible};
    for (uintptr_t c : calls) if (!text.contains(reinterpret_cast<const void*>(c))) { g_reactWhy = "a call target lies outside the module's code"; logf("WO173-REACT not armed: %s", g_reactWhy); return false; }
    if (!prologue_is(s.createHitAction, kCreateHitActionPrologue, sizeof kCreateHitActionPrologue) ||
        !prologue_is(s.alive, kAlivePrologue, sizeof kAlivePrologue) || !prologue_is(s.ownReaction, kOwnReactionPrologue, sizeof kOwnReactionPrologue) ||
        !prologue_is(s.attackerFlag, kAttackerFlagPrologue, sizeof kAttackerFlagPrologue) || !prologue_is(s.kind, kKindPrologue, sizeof kKindPrologue) ||
        !prologue_is(s.intendedSetter, kIntendedSetterPrologue, sizeof kIntendedSetterPrologue) ||
        !prologue_is(s.interruptible, kInterruptiblePrologue, sizeof kInterruptiblePrologue)) {
        g_reactWhy = "a prologue of the reaction tail is not the one read (another build)"; logf("WO173-REACT not armed: %s", g_reactWhy); return false;
    }
    // the hand-over is an import: its slot must lie in this module's image, its target in Framework.dll's code
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(cm);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(reinterpret_cast<const uint8_t*>(cm) + dos->e_lfanew);
    const uintptr_t lo = reinterpret_cast<uintptr_t>(cm), hi = lo + nt->OptionalHeader.SizeOfImage;
    uintptr_t target = 0;
    if (s.iatSlot < lo || s.iatSlot + 8 > hi || !rd(reinterpret_cast<const void*>(s.iatSlot), 0, &target) || !in_module_text(fw, target)) {
        g_reactWhy = "the action director's SetAction import does not lead into Framework.dll's code"; logf("WO173-REACT not armed: %s", g_reactWhy); return false;
    }
    g_sites = s;
    g_setAction = target;
    // WO-178: the player's own hit reaction is the ordinary C_CombatActorActionHit only (react() checks the created action's class)
    g_vftHitAction = anchor::find_vftable(cm, ".?AVC_CombatActorActionHit@combatmodule@wh@@", 0);
    g_reactArmed = true;
    g_reactWhy = "armed";
    char a[64], b[64], c[64];
    anchor::describe(reinterpret_cast<void*>(s.createHitAction), a, sizeof a);
    anchor::describe(reinterpret_cast<void*>(s.kind), b, sizeof b);
    anchor::describe(reinterpret_cast<void*>(target), c, sizeof c);
    logf("WO173-REACT armed: CreateHitAction=%s (label + prologue + the handler's own call), kind=%s, SetAction=%s (the handler's import), "
         "victim at details +0x%X, kind input at result +0x%X, director at combat actor +0x%X, group word +0x%X, action addref/release slots 0x%X/0x%X, "
         "intended-target property at model +0x%X", a, b, c, s.victimOff, s.kindInOff, s.directorOff, s.teamOff, s.addrefSlot, s.releaseSlot, s.intendedOff);
    return true;
}

float npc_max_stamina(uint32_t veid, void* soul) {
    float st = -1;
    if (soul && rttr::soul_state(soul, "stamina", &st)) {
        float& m = g_stMaxSeen[veid];
        if (st > m) m = st;
        return stamina_max_estimate(m, st);
    }
    auto it = g_stMaxSeen.find(veid);
    return stamina_max_estimate(it == g_stMaxSeen.end() ? 0.0f : it->second, -1.0f);
}

// WO-178 C4: what the log says about the hold and the engine-like cap.
std::string held_note(const Pending& p) {
    char b[160] = "";
    int n = 0;
    if (p.heldS >= 0) n += std::snprintf(b + n, sizeof b - n, " held_ms=%.0f", p.heldS * 1000.0);
    if (!p.engine && p.capSt >= 0) {
        if (p.capMedian > 0) n += std::snprintf(b + n, sizeof b - n, " cap=engine-median:%.1f", p.capMedian);
        else n += std::snprintf(b + n, sizeof b - n, " cap=default:%.0f", kcdmp::wo178rules::kDefaultFallbackSt);
    }
    return b;
}

void finish(Pending& p, bool measured, float eHp, float eSt) {
    void* soul = hits::soul_of_eid(p.veid);
    bool ko = false, dead = false;
    if (soul) { rttr::soul_bool(soul, "IsUnconscious", &ko); rttr::soul_bool(soul, "IsDead", &dead); }
    float hpNow = -1, stNow = -1;
    if (soul) { rttr::soul_state(soul, "health", &hpNow); rttr::soul_state(soul, "stamina", &stNow); }
    char corr[96] = "-";
    if (p.engine) {
        const Correction c = health_correction(p.outcome, measured ? eHp : -1.0f, p.joinerHp, hpNow);
        float credit = (measured ? eHp : p.joinerHp) - p.joinerHp;   // what the echo guard was told up front vs what landed
        if (c.apply && c.extraDamage > 0) {
            void* soulR = rttr::find_soul_by_guid(p.guid);
            void* aSoul = hits::soul_of_eid(p.avatarEid);
            void* att = aSoul ? (buffs::as_c_soul(aSoul) ? buffs::as_c_soul(aSoul) : aSoul) : nullptr;
            const bool ok = soulR && (rttr::apply_damage_soul_ex(soulR, 0.0f, c.extraDamage, att, 1) || rttr::apply_damage_soul(soulR, 0.0f, c.extraDamage, att));
            if (ok) { credit += c.extraDamage; c_corrected.fetch_add(1); }
            std::snprintf(corr, sizeof corr, "+%.2f(%s)", c.extraDamage, ok ? "applied" : "FAILED");
        } else if (c.apply && c.raiseBy > 0 && soul) {
            float raise = c.raiseBy;
            if (p.hp0 > 0 && hpNow + raise > p.hp0) raise = p.hp0 - hpNow;
            if (raise > 0.01f && rttr::soul_set_state(soul, "health", hpNow + raise)) { credit -= raise; c_raised.fetch_add(1); }
            std::snprintf(corr, sizeof corr, "-%.2f(raised back)", raise);
        }
        if (std::fabs(credit) > 0.01f) rttr::note_remote_damage(p.guid, credit);
    }
    if (p.engine && measured && p.outcome == static_cast<uint8_t>(kcdmp::wo165::Outcome::Hit) && p.atkType >= 0 && std::isfinite(eSt) && eSt > 0) {
        g_medNpc[(static_cast<uint64_t>(p.veid) << 8) | static_cast<uint8_t>(p.atkType)].add(eSt);   // WO-178 C4: what the engine took
        g_medKind[p.atkType].add(eSt);
    }
    if (ko) {
        c_koAfter.fetch_add(1);
        if (p.st0 >= 0.9f * p.stMax) c_koFullStamina.fetch_add(1);
    }
    if (p.stateRead && p.stateAfter == kcdmp::wo166rules::kStateHit) c_stateHit.fetch_add(1);
    if (!measured && p.engine) c_unmeasured.fetch_add(1);
    char est[32], ehp[32];
    if (p.engine && measured) { std::snprintf(est, sizeof est, "%.2f", eSt); std::snprintf(ehp, sizeof ehp, "%.2f", eHp); }
    else { std::snprintf(est, sizeof est, p.engine ? "?" : "raw"); std::snprintf(ehp, sizeof ehp, p.engine ? "?" : "raw"); }
    logf("WO173-HIT npc=%s seq=%u engine=%s st=%s/%.2f hp=%s/%.2f joiner_said=st%.2f/hp%.2f reaction=%s kind=%d state=0x%X knockdown=%s dead=%s "
         "st_before=%.1f/%.0f correction=%s fallback=%s%s%s",
         p.name.c_str(), p.seq, p.engine ? kcdmp::wo165::outcome_name(static_cast<kcdmp::wo165::Outcome>(p.outcome)) : "-", est,
         p.engine ? p.joinerSt : (p.capSt >= 0 ? p.capSt : p.joinerSt), ehp, p.joinerHp, p.joinerSt, p.joinerHp,
         react_name(static_cast<React>(p.react)), p.reactKind, p.stateRead ? static_cast<unsigned>(p.stateAfter) : 0u, ko ? "yes" : "no",
         dead ? "yes" : "no", p.st0, p.stMax, corr, fallback_name(p.fb), p.capSt >= 0 && p.capSt < p.joinerSt ? " (stamina capped)" : "", held_note(p).c_str());
}

// mayHold: WO-178 C4 (1) -- a blow whose only missing precondition is the figure's strike window is not applied: *held says so and the
// caller keeps it (tick runs it again with mayHold false once the window opens or kHoldS runs out). heldS: how long it waited (-1 none).
uint8_t do_real_hit(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen, bool mayHold, bool* held, double heldS) {
    if (held) *held = false;
    // [op][type][zone][hand][strength:f32][rflags][attributed body]
    if (len < 9 + 30 || cap < 33) return wo163::kRBadRequest;
    const uint8_t* ab = body + 9;
    const size_t alen = len - 9;
    if (alen != static_cast<size_t>(30 + ab[29])) return wo163::kRBadRequest;
    if (heldS < 0) c_requests.fetch_add(1);   // a held blow run again is the same request
    motion::AttackFields f{};
    f.type = static_cast<int8_t>(body[1]); f.zone = static_cast<int8_t>(body[2]); f.hand = static_cast<int8_t>(body[3]);
    std::memcpy(&f.strength, body + 4, 4);
    const uint8_t rflags = body[8];
    float st = 0, hp = 0; uint32_t avatarEid = 0;
    std::memcpy(&st, ab + 16, 4); std::memcpy(&hp, ab + 20, 4); std::memcpy(&avatarEid, ab + 25, 4);
    if (!std::isfinite(st) || !std::isfinite(hp)) return wo163::kRBadRequest;
    const std::string name(reinterpret_cast<const char*>(ab + 30), ab[29]);
    const uint32_t veid = name.empty() ? 0 : hits::eid_of_name(name.c_str());
    void* vsoul = veid ? hits::soul_of_eid(veid) : nullptr;

    Pending p{};
    p.veid = veid; p.avatarEid = avatarEid; p.name = name; std::memcpy(p.guid, ab, 16);
    p.joinerSt = st; p.joinerHp = hp; p.t0 = now_s();
    if (vsoul) { rttr::soul_state(vsoul, "health", &p.hp0); rttr::soul_state(vsoul, "stamina", &p.st0); }
    p.stMax = npc_max_stamina(veid, vsoul);

    Pre pre{};
    pre.on = g_on.load();
    pre.faulted = g_faulted.load();
    pre.armed = !wo165::off() && wo165::resolve();
    pre.rowKnown = (rflags & 1) != 0 && kcdmp::wo166rules::attack_fields_ok(f.type, f.zone, f.strength, f.hand);
    pre.victimFound = veid != 0 && vsoul != nullptr;
    pre.victimDead = veid != 0 && wo172::copy_dead(veid);
    pre.striking = avatarEid != 0 && wo166::striking_recently(avatarEid);
    pre.arranged = veid != 0 && hits::scene_owned_eid(veid, "wo173-real-hit");   // WO-176 (A1): a quest's arranged fight takes no figure's blow
    Fallback fb = precondition(pre);
    if (mayHold && held && fb == Fallback::NoSwingWindow && kcdmp::wo178rules::should_hold(wo178::hold_on(), true, 0.0)) {
        *held = true;   // nothing applied, nothing logged yet: the blow waits for the window (applied once either way)
        return wo163::kROk;
    }
    p.atkType = (rflags & 1) ? f.type : static_cast<int8_t>(-1);
    p.heldS = heldS;
    wo165::Result rr{};
    if (fb == Fallback::None) {
        wo165::Request rq{};
        rq.attackerEid = avatarEid; rq.victimEid = veid; rq.fields = f;
        rq.flags = wo165::kFlagSkipFilter | wo165::kFlagIntended | (g_reactOn.load() && !g_reactFaulted.load() ? wo165::kFlagReact : 0);
        rr = wo165::replay(rq);
        if (rr.reason != wo165::kOk) {
            fb = Fallback::Replay;
            static unsigned s_said = 0;
            if (s_said++ < 20) logf("WO173-HIT npc=%s the replay refused: %s", name.c_str(), wo165::reason_name(rr.reason));
            if (rr.reason == wo165::kFault) switch_off("the engine's hit processor");
        } else {
            fb = after_engine(static_cast<uint8_t>(rr.outcome));
        }
    }
    hits::AttribResult ar{};
    uint8_t applied = 0;
    float stApplied = 0, hpApplied = 0;
    if (fb == Fallback::None) {
        // the engine applied the blow: only the victim-side history, the skirmish and (switch) the threat of WO-121's path run
        ar = hits::apply_attributed_ex(ab, alen, false, -1.0f, g_threat.load());
        applied = 1;
        c_engine.fetch_add(1);
        switch (rr.outcome) {
            case wo165::Outcome::Hit: c_hit.fetch_add(1); break;
            case wo165::Outcome::Blocked: c_blocked.fetch_add(1); break;
            case wo165::Outcome::PerfectBlock: c_pb.fetch_add(1); break;
            case wo165::Outcome::Broken: c_broken.fetch_add(1); break;
            default: break;
        }
        (rr.react == static_cast<uint8_t>(React::Queued) ? c_reactQueued : c_reactOther).fetch_add(1);
        p.engine = true; p.seq = rr.seq; p.outcome = static_cast<uint8_t>(rr.outcome); p.react = rr.react; p.reactKind = rr.reactKind;
    } else if (routes_nowhere(fb)) {
        c_kept.fetch_add(1);   // WO-176 (A1): no engine, no raw blow -- the agent tells the joiner the fight is the story's
    } else {
        float capSt;
        if (wo178::engine_cap_on()) {
            // WO-178 C4 (2): an engine-like blow -- at most the engine's own cost for this attack kind, never below the stamina floor
            p.capMedian = engine_median(veid, p.atkType);
            capSt = kcdmp::wo178rules::engine_like_cap(st, p.capMedian, p.st0, p.stMax);
            if (p.capMedian > 0 && capSt < st) c_medianCapped.fetch_add(1);
            if (p.st0 >= 0 && capSt <= 0.0f && st > 0) c_floorStopped.fetch_add(1);
        } else {
            capSt = cap_stamina(st, p.stMax);
        }
        if (capSt < st) c_capped.fetch_add(1);
        ar = hits::apply_attributed_ex(ab, alen, true, capSt, true);
        applied = (ar.steps & 1) ? 2 : 0;
        stApplied = capSt; hpApplied = hp;
        c_fallback.fetch_add(1);
        c_fb[static_cast<uint8_t>(fb) & 15]++;
        p.capSt = capSt;
    }
    p.fb = fb;
    if (veid) g_pending.push_back(p);
    out[0] = applied;
    out[1] = static_cast<uint8_t>(rr.outcome);
    out[2] = static_cast<uint8_t>(fb);
    out[3] = rr.react;
    out[4] = ar.steps;
    std::memcpy(out + 5, &ar.attackerWuid, 8);
    std::memcpy(out + 13, &ar.victimWuid, 8);
    std::memcpy(out + 21, &rr.seq, 4);
    std::memcpy(out + 25, &stApplied, 4);
    std::memcpy(out + 29, &hpApplied, 4);
    *outLen = 33;
    return wo163::kROk;
}

}   // namespace

bool mark_intended(void* model, bool on, uint8_t* prev) {
    if (!model || g_reactFaulted.load() || !resolve_react()) return false;
    void* prop = static_cast<char*>(model) + g_sites.intendedOff;
    void* pvt = nullptr;
    uint8_t cur = 0;
    if (!rd(prop, 0, &pvt) || !pvt || !rd(prop, kPropertyValueOff, &cur)) return false;
    if (prev) *prev = cur;
    if ((cur != 0) == on) return true;
    if (!call_setter(g_sites.intendedSetter, prop, on)) { react_off("the intended-target setter"); return false; }
    return true;
}

uint8_t react(void* aCa, void* vCa, uint8_t* result, int32_t* kindOut) {
    if (kindOut) *kindOut = -1;
    if (!g_reactOn.load()) return static_cast<uint8_t>(React::Off);
    if (g_reactFaulted.load()) return static_cast<uint8_t>(React::Fault);
    if (!resolve_react()) return static_cast<uint8_t>(React::NotArmed);
    if (!aCa || !vCa || !result) return static_cast<uint8_t>(React::NotRun);
    const ReactionSites& s = g_sites;
    bool alive = false, own = false, flag = false;
    if (!call_b1(s.alive, vCa, &alive)) { react_off("the victim's can-react test"); return static_cast<uint8_t>(React::Fault); }
    if (!alive) return static_cast<uint8_t>(React::NotAlive);
    if (!call_b1(s.ownReaction, vCa, &own)) { react_off("the victim's own-action test"); return static_cast<uint8_t>(React::Fault); }
    if (own) return static_cast<uint8_t>(React::Own);
    if (!call_b1(s.attackerFlag, aCa, &flag)) { react_off("the attacker's flag"); return static_cast<uint8_t>(React::Fault); }
    int32_t kin = 0, kind = 0;
    std::memcpy(&kin, result + s.kindInOff, 4);
    if (!call_kind(s.kind, kin, flag, &kind)) { react_off("the reaction kind"); return static_cast<uint8_t>(React::Fault); }
    if (kindOut) *kindOut = kind;
    // CreateHitAction reads nothing of its object but +8, the attacker's combat actor (read this WO): ours is that much, zero elsewhere
    void* self[4] = {nullptr, aCa, nullptr, nullptr};
    void* act = nullptr;
    if (!call_cha(s.createHitAction, self, &act, vCa, kind, result)) { react_off("CreateHitAction"); return static_cast<uint8_t>(React::Fault); }
    if (!act) return static_cast<uint8_t>(React::NoAction);
    // WO-178: a copy's blow on the LOCAL PLAYER (the joiner's replay) gets his recoil -- the engine's own hit reaction, nothing else:
    // an action of any other class is released here (an NPC's blow never knocks the player down; a knockdown is not a combat action, but
    // nothing but the ordinary hit reaction is handed over for the player)
    if (vCa == motion::player_combat_actor()) {
        void* vp = nullptr;
        if (!g_vftHitAction || !rd(act, 0, &vp) || vp != static_cast<const void*>(g_vftHitAction)) {
            vcall0(act, s.releaseSlot);
            c_playerKept.fetch_add(1);
            logf("WO178-RECOIL player reaction=kept kind=%d -- the created action is not the ordinary hit reaction (released)", kind);
            return static_cast<uint8_t>(React::PlayerKept);
        }
        c_playerRecoil.fetch_add(1);
    }
    // the handler's own test before the hand-over (block C): same group, nobody killed, the victim's action not interruptible, its flag set
    uint16_t ta = 0, tv = 0;
    if (!rd(aCa, s.teamOff, &ta) || !rd(vCa, s.teamOff, &tv)) { vcall0(act, s.releaseSlot); return static_cast<uint8_t>(React::NotTaken); }
    if (ta == tv && result[s.killedOff0] == 0 && result[s.killedOff1] == 0) {
        bool unint = false;
        if (!call_b1(s.interruptible, vCa, &unint)) { vcall0(act, s.releaseSlot); react_off("the victim's interrupt test"); return static_cast<uint8_t>(React::Fault); }
        if (unint) {
            void* model = nullptr;
            uint32_t flags = 0;
            if (!rd(vCa, s.modelOff, &model) || !model) { vcall0(act, s.releaseSlot); return static_cast<uint8_t>(React::NotTaken); }
            if (!vcall_u32(static_cast<char*>(model) + s.flagsPropOff, s.flagsGetSlot, &flags)) { vcall0(act, s.releaseSlot); react_off("the victim's flags"); return static_cast<uint8_t>(React::Fault); }
            if (flags & s.flagsBit) { vcall0(act, s.releaseSlot); return static_cast<uint8_t>(React::SameGroup); }
        }
    }
    void* director = nullptr;
    void* dvt = nullptr;
    if (!rd(vCa, s.directorOff, &director) || !director || !rd(director, 0, &dvt) || !dvt) {
        vcall0(act, s.releaseSlot);
        return static_cast<uint8_t>(React::NotTaken);
    }
    void* sp = act;
    if (!vcall0(sp, s.addrefSlot)) { react_off("the action's addref"); return static_cast<uint8_t>(React::Fault); }
    bool taken = false;
    const bool ok = call_set_action(g_setAction, director, &sp, &taken);
    vcall0(sp, s.releaseSlot);
    vcall0(act, s.releaseSlot);
    if (!ok) { react_off("SetAction"); return static_cast<uint8_t>(React::Fault); }
    return static_cast<uint8_t>(taken ? React::Queued : React::NotTaken);
}

void tick_held(double now) {
    for (auto it = g_held.begin(); it != g_held.end();) {
        const double waited = now - it->t0;
        const bool open = it->avatarEid && wo166::striking_recently(it->avatarEid);
        if (!open && waited < kcdmp::wo178rules::kHoldS) { ++it; continue; }
        uint8_t res[33]{};
        size_t rl = 0;
        const uint8_t r = do_real_hit(it->body.data(), it->body.size(), res, sizeof res, &rl, false, nullptr, waited);
        if (r != wo163::kROk || rl != 33) { c_heldOther.fetch_add(1); std::memset(res, 0, sizeof res); }
        else (res[0] == 1 ? c_heldEngine : res[0] == 2 ? c_heldRaw : c_heldOther).fetch_add(1);
        logf("WO178-HOLD id=%u waited_ms=%.0f window=%s result=%s", it->id, waited * 1000.0, open ? "opened" : "timed-out",
             r != wo163::kROk ? "bad-request" : res[0] == 1 ? "engine" : res[0] == 2 ? "raw" : "none");
        if (g_heldDone.size() > 256) g_heldDone.clear();
        g_heldDone[it->id] = std::vector<uint8_t>(res, res + 33);
        it = g_held.erase(it);
    }
}

void tick() {
    const double now = now_s();
    if (!g_held.empty()) tick_held(now);
    if (g_pending.empty()) return;
    for (auto it = g_pending.begin(); it != g_pending.end();) {
        Pending& p = *it;
        if (!p.stateRead && now - p.t0 >= kReactionReadS) {
            motion::ModelRead m{};
            if (motion::read_model(p.veid, &m) && (m.valid & motion::kMvState)) p.stateAfter = m.state;
            p.stateRead = true;
        }
        bool done = false, measured = false;
        float eHp = 0, eSt = 0;
        if (p.engine) {
            const wo165::Damage d = wo165::damage_of(p.seq);
            if (d.state == 2) { measured = true; eHp = d.health; eSt = d.stamina; done = p.stateRead; }
            else if (d.state == 3 || d.state == 0) done = p.stateRead;   // merged into an earlier measure / unknown: logged unmeasured
            if (now - p.t0 > kPendingMaxS) done = true;
        } else {
            done = now - p.t0 >= 0.7;   // the raw blow's knock-out read
        }
        if (!done) { ++it; continue; }
        finish(p, measured, eHp, eSt);
        it = g_pending.erase(it);
    }
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    switch (op) {
        case kOpRealHit: {
            bool held = false;
            const uint8_t r = do_real_hit(body, len, out, cap, outLen, true, &held, -1.0);
            if (r != wo163::kROk || !held) return r;
            // WO-178 C4 (1): applied = 4 (held) and the hold's id in the seq field; op 28 answers once tick has run it
            Held h{};
            h.body.assign(body, body + len);
            h.t0 = now_s();
            h.id = ++g_heldSeq;
            std::memcpy(&h.avatarEid, body + 9 + 25, 4);
            g_held.push_back(h);
            c_held.fetch_add(1);
            std::memset(out, 0, 33);
            out[0] = 4;
            out[2] = static_cast<uint8_t>(Fallback::NoSwingWindow);
            std::memcpy(out + 21, &h.id, 4);
            *outLen = 33;
            return wo163::kROk;
        }
        case wo178::kOpHeldResult: {
            if (len != 5 || cap < 34) return wo163::kRBadRequest;
            uint32_t id = 0;
            std::memcpy(&id, body + 1, 4);
            auto d = g_heldDone.find(id);
            bool pending = false;
            for (const auto& h : g_held) if (h.id == id) { pending = true; break; }
            std::memset(out, 0, 34);
            out[0] = d != g_heldDone.end() ? 2 : pending ? 1 : 0;
            if (d != g_heldDone.end()) { std::memcpy(out + 1, d->second.data(), 33); g_heldDone.erase(d); }
            *outLen = 34;
            return wo163::kROk;
        }
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = n <= 0 || cap == 0 ? 0 : (static_cast<size_t>(n) < cap ? static_cast<size_t>(n) : cap - 1);
            return wo163::kROk;
        }
        case kOpConfig: {
            if (len != 4 || cap < 3) return wo163::kRBadRequest;
            if (body[1] != 255) { const bool was = g_on.exchange(body[1] != 0); if (was != g_on.load()) logf("WO173-CFG mp_real_hits %s", g_on.load() ? "on" : "off"); }
            if (body[2] != 255) { const bool was = g_threat.exchange(body[2] != 0); if (was != g_threat.load()) logf("WO173-CFG avatar-hit threat on engine blows %s", g_threat.load() ? "on" : "off"); }
            if (body[3] != 255) { const bool was = g_reactOn.exchange(body[3] != 0); if (was != g_reactOn.load()) logf("WO173-CFG reaction tail %s", g_reactOn.load() ? "on" : "off"); }
            out[0] = g_on.load() ? 1 : 0; out[1] = g_threat.load() ? 1 : 0; out[2] = g_reactOn.load() ? 1 : 0;
            *outLen = 3;
            return wo163::kROk;
        }
        case kOpDownPoll: {
            // [op][count]{[nameLen][name]}* -> [count]{[state]}*
            if (len < 2) return wo163::kRBadRequest;
            const size_t n = body[1];
            if (n > 32 || cap < 1 + n) return wo163::kRBadRequest;
            size_t at = 2;
            out[0] = static_cast<uint8_t>(n);
            for (size_t i = 0; i < n; ++i) {
                if (at >= len) return wo163::kRBadRequest;
                const size_t k = body[at];
                if (at + 1 + k > len || k == 0 || k > 63) return wo163::kRBadRequest;
                const std::string name(reinterpret_cast<const char*>(body + at + 1), k);
                at += 1 + k;
                uint8_t state = 255;
                const uint32_t eid = hits::eid_of_name(name.c_str());
                void* soul = eid ? hits::soul_of_eid(eid) : nullptr;
                bool ko = false, dead = false;
                if (soul && rttr::soul_bool(soul, "IsDead", &dead)) {
                    rttr::soul_bool(soul, "IsUnconscious", &ko);
                    state = dead ? 2 : ko ? 1 : 0;
                }
                out[1 + i] = state;
            }
            if (at != len) return wo163::kRBadRequest;
            *outLen = 1 + n;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

int status_text(char* out, int n) {
    return std::snprintf(out, n,
        "wo173 real_hits=%s faulted=%s react=%s(%s) threat=%s requests=%u engine=%u hit=%u blocked=%u pb=%u broken=%u react_queued=%u react_other=%u "
        "fallback=%u [off=%u no_row=%u no_window=%u no_victim=%u dead=%u replay=%u filtered=%u core=%u not_armed=%u] capped=%u corrected=%u raised=%u "
        "state_hit=%u ko_after=%u ko_full_stamina=%u unmeasured=%u faults=%u pending=%zu arranged_kept=%u held=%u[engine=%u raw=%u other=%u waiting=%zu] "
        "median_capped=%u floor_stopped=%u hold=%s engine_cap=%s player_recoil=%u player_kept=%u",
        g_on.load() ? "on" : "off", g_faulted.load() ? "yes" : "no", g_reactOn.load() ? (g_reactFaulted.load() ? "OFF(fault)" : "on") : "off",
        g_reactResolved ? g_reactWhy : "unresolved", g_threat.load() ? "on" : "off", c_requests.load(), c_engine.load(), c_hit.load(), c_blocked.load(),
        c_pb.load(), c_broken.load(), c_reactQueued.load(), c_reactOther.load(), c_fallback.load(),
        c_fb[static_cast<int>(Fallback::Off)], c_fb[static_cast<int>(Fallback::NoRow)], c_fb[static_cast<int>(Fallback::NoSwingWindow)],
        c_fb[static_cast<int>(Fallback::NoVictim)], c_fb[static_cast<int>(Fallback::VictimDead)], c_fb[static_cast<int>(Fallback::Replay)],
        c_fb[static_cast<int>(Fallback::Filtered)], c_fb[static_cast<int>(Fallback::CoreNotRun)], c_fb[static_cast<int>(Fallback::NotArmed)],
        c_capped.load(), c_corrected.load(), c_raised.load(), c_stateHit.load(), c_koAfter.load(), c_koFullStamina.load(), c_unmeasured.load(),
        c_faults.load(), g_pending.size(), c_kept.load(), c_held.load(), c_heldEngine.load(), c_heldRaw.load(), c_heldOther.load(), g_held.size(),
        c_medianCapped.load(), c_floorStopped.load(), wo178::hold_on() ? "on" : "off", wo178::engine_cap_on() ? "on" : "off", c_playerRecoil.load(), c_playerKept.load());
}

}   // namespace kcdmp::wo173
