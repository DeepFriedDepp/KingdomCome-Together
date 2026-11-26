// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-209: the native half (wo209.h).
#include "wo209.h"
#include "wo209_rules.h"
#include "wo203.h"
#include "wo163.h"
#include "wo187.h"
#include "wo132.h"
#include "wo195.h"
#include "wo197.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_scan.h"
#include "npc_drive.h"
#include "anchors.h"
#include "wo208.h"
#include "wo178.h"
#include "wo210.h"
#include "wo210_rules.h"
#include "wo223.h"
#include "wo224.h"
#include "wo224_rules.h"
#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>

namespace kcdmp::wo209 {

namespace {

using namespace wo209rules;

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};
constexpr uint32_t kPlayerEntity = 0x7777;

double qpc_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

// The bench's twin (KCDMP_TEST_W209_OFF in the game's environment, read once): those bits stay off whatever the agent says.
uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W209_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO209-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W209_OFF=%s)", s, b);
    }
    return s;
}

uint32_t player_eid() {
    void* e = engine::entity_by_id(kPlayerEntity);
    return e ? engine::entity_id(e) : 0;
}
const char* name_of(uint32_t eid) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "?";
}
std::string who(uint32_t eid) {
    if (!eid) return "none";
    if (eid == player_eid()) return "the-local-player";
    return name_of(eid);
}

// ---- A, the host: each NPC near a player, its opponent (op 136) -----------------------------------------------------------------
// The NPCs within the owner stream's radius of the local player or a partner's figure (the list walked once a second); per body its combat
// model's opponent (motion's read, read-only), sent when it changed and again every kOppResendS while it has one.
struct HostOpp { uint8_t kind = kHostNone; uint32_t opp = 0; double sentAt = -1e9; bool told = false; };
std::unordered_map<uint32_t, HostOpp> g_hostOpp;   // main thread
std::vector<uint32_t> g_list;
double g_listAt = -1e9;
std::atomic<uint32_t> c_oppReads{0}, c_oppOut{0}, c_oppChanges{0};

void host_list(double now) {
    if (now - g_listAt < 1.0) return;
    g_listAt = now;
    g_list.clear();
    npcscan::Anchor an[9];
    int na = 0;
    float p[3]{};
    void* pe = engine::entity_by_id(kPlayerEntity);
    if (pe && engine::entity_world_pos(pe, p)) an[na++] = {p[0], p[1], p[2]};
    uint32_t av[8]{}; void* souls[8]{};
    const int nav = hits::avatar_list(av, souls, 8);
    for (int i = 0; i < nav && na < 9; ++i) {
        void* e = engine::entity_by_id(av[i]);
        if (e && engine::entity_world_pos(e, p)) an[na++] = {p[0], p[1], p[2]};
    }
    if (!na) return;
    uint32_t walked = 0; uint8_t refuse = 0;
    npcscan::for_each_in_radius(an, na, wo208rules::kStreamRadiusM, [](void* e, const char*, float, float, float, bool isHorse, void* c) {
        auto* v = static_cast<std::vector<uint32_t>*>(c);
        if (!isHorse && v->size() < 64) v->push_back(engine::entity_id(e));
    }, &g_list, &walked, &refuse);
    for (auto it = g_hostOpp.begin(); it != g_hostOpp.end();)
        it = std::find(g_list.begin(), g_list.end(), it->first) == g_list.end() ? g_hostOpp.erase(it) : std::next(it);
}

uint8_t op_opp_read(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    if (len != 2 || cap < 1) return wo163::kRBadRequest;
    const int max = body[1] ? body[1] : 1;
    const double now = qpc_s();
    host_list(now);
    const uint32_t me = player_eid();
    size_t o = 1;
    int count = 0;
    for (uint32_t eid : g_list) {
        if (count >= max) break;
        void* e = engine::entity_by_id(eid);
        const char* nm = e ? engine::entity_name(e) : nullptr;
        if (!nm) continue;
        const size_t nl = std::strlen(nm);
        if (nl == 0 || nl > 63) continue;
        motion::ModelRead m{};
        c_oppReads.fetch_add(1);
        const uint32_t opp = motion::read_model(eid, &m) && m.hasModel ? m.opponentEid : 0;
        const uint8_t kind = host_kind(opp, me, opp != 0 && motion::is_avatar_eid(opp));
        HostOpp& h = g_hostOpp[eid];
        const bool changed = !h.told || h.kind != kind || h.opp != opp;
        if (!h.told && kind == kHostNone) { h.told = true; continue; }   // a body that never fought: nothing to tell
        if (!opp_send_due(changed, now - h.sentAt, kind)) continue;
        const char* on = kind == kHostNpc ? name_of(opp) : "";
        const size_t onl = kind == kHostNpc ? std::min<size_t>(std::strlen(on), 63) : 0;
        const size_t need = 1 + nl + 1 + 4 + 1 + onl;
        if (o + need > cap) break;
        out[o] = static_cast<uint8_t>(nl); std::memcpy(out + o + 1, nm, nl); o += 1 + nl;
        out[o] = kind; std::memcpy(out + o + 1, &opp, 4); o += 5;
        out[o] = static_cast<uint8_t>(onl); if (onl) std::memcpy(out + o + 1, on, onl); o += 1 + onl;
        if (changed) {
            const uint32_t k = c_oppChanges.fetch_add(1) + 1;
            if (k <= 2000 || k % 100 == 0)
                logf("WO209-OPP host npc=%s opponent %s -> %s (#%u)", nm, h.told ? who(h.opp).c_str() : "(first)", who(opp).c_str(), k);
        }
        h.kind = kind; h.opp = opp; h.sentAt = now; h.told = true;
        ++count;
        c_oppOut.fetch_add(1);
    }
    out[0] = static_cast<uint8_t>(count);
    *outLen = o;
    return wo163::kROk;
}

// ---- A, the joiner: the mirror ------------------------------------------------------------------------------------------------------
struct MirrorCopy {
    uint8_t kind = kHereNone; uint32_t target = 0; double at = 0, applyAt = -1e9, changedAt = 0;
    bool followed = true;   // the copy took the newest want (the follow time measured once per change)
};
std::unordered_map<uint32_t, MirrorCopy> g_mirror;   // main thread
std::atomic<uint32_t> c_in{0}, c_bad{0}, c_samples{0}, c_match{0}, c_engaged{0}, c_engageRefused{0}, c_cleared{0}, c_follows{0};
std::atomic<uint32_t> c_combatHeld{0}, c_inChanges{0};   // mirror ticks that held a copy in combat mode on the local player
std::vector<float> g_followMs;   // the follow times (ms), the newest 512

uint8_t op_opp_in(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 2) return wo163::kRBadRequest;
    const int n = body[1];
    if (len != 2 + static_cast<size_t>(n) * 9) { c_bad.fetch_add(1); return wo163::kRBadRequest; }
    const double now = qpc_s();
    for (int i = 0; i < n; ++i) {
        const uint8_t* p = body + 2 + i * 9;
        uint32_t copy = 0, target = 0;
        std::memcpy(&copy, p, 4); std::memcpy(&target, p + 5, 4);
        const uint8_t kind = p[4] <= kHereEntity ? p[4] : kHereNone;
        if (!copy) continue;
        MirrorCopy& m = g_mirror[copy];
        if (m.kind != kind || m.target != target || m.at == 0) {
            if (m.at != 0) {
                const uint32_t k = c_inChanges.fetch_add(1) + 1;   // changes only (d8: a count of every sample went quiet in minutes)
                if (k <= 2000 || k % 200 == 0)
                    logf("WO209-OPP mirror npc=%s: the host's NPC now fights %s here", name_of(copy),
                         kind == kHerePlayer ? "the-local-player" : kind == kHereEntity ? who(target).c_str() : "nobody");
            }
            m.kind = kind; m.target = target; m.changedAt = now; m.followed = false; m.applyAt = -1e9;   // applied this frame
        }
        m.at = now;
        c_in.fetch_add(1);
    }
    if (cap >= 1) { out[0] = static_cast<uint8_t>(n); *outLen = 1; }
    return wo163::kROk;
}

// The twin (mp_copy_opponent_mirror off): nothing applied, and the copy's opponent is still sampled against the host's (A.4's twin
// measure) -- the copy's own opponent read the same way the mirror reads it.
bool g_mirrorWasOn = false;
std::atomic<uint32_t> c_twinSamples{0}, c_twinMatch{0};
void twin_sample(double now) {
    const uint32_t me = player_eid();
    for (auto it = g_mirror.begin(); it != g_mirror.end();) {
        MirrorCopy& m = it->second;
        if (now - m.at > kMirrorStaleS || !engine::entity_by_id(it->first)) { it = g_mirror.erase(it); continue; }
        if (now - m.applyAt < kMirrorTickS || !sample_counts(m.kind, now - m.changedAt)) { ++it; continue; }
        m.applyAt = now;
        motion::ModelRead r{};
        const uint32_t have = motion::read_model(it->first, &r) && r.hasModel ? r.opponentEid : 0;
        const uint32_t want = m.kind == kHerePlayer ? me : m.target;
        c_twinSamples.fetch_add(1);
        if (want && have == want) c_twinMatch.fetch_add(1);
        ++it;
    }
}

void mirror_tick(double now) {
    if (!on(kBitOppMirror)) {
        if (g_mirrorWasOn) {
            for (auto& kv : g_mirror) wo187::mirror_wish(kv.first, 0, 0, nullptr);
            g_mirrorWasOn = false;
            logf("WO209-OPP mirror off: every mirrored wish cleared (WO-187's agent wishes again); the copies' opponents are still sampled");
        }
        twin_sample(now);
        return;
    }
    g_mirrorWasOn = true;
    for (auto it = g_mirror.begin(); it != g_mirror.end();) {
        const uint32_t copy = it->first;
        MirrorCopy& m = it->second;
        if (now - m.at > kMirrorStaleS || !engine::entity_by_id(copy)) {
            wo187::mirror_wish(copy, 0, 0, nullptr);
            c_cleared.fetch_add(1);
            it = g_mirror.erase(it);
            continue;
        }
        if (now - m.applyAt < kMirrorTickS) { ++it; continue; }
        m.applyAt = now;
        const Mirror step = mirror_step(m.kind, wo132::engaged(copy));
        if (step == Mirror::EngageWish) {
            if (wo132::mirror_engage(copy, kMirrorEngageM, "mirror: the host's NPC fights your figure there")) c_engaged.fetch_add(1);
            else c_engageRefused.fetch_add(1);
        }
        // the copy joins his fight within one stream tick: combat mode held while the mirror holds it on him (WO-132's engagement hold,
        // refreshed here; the host's own combat state, op 2's, replaces the stand-in's guard when it comes). d7-defence: a copy's first
        // swings came while it was still out of combat -- its combat mode came with the host's combat state 1.5-2 s later -- refused or cut
        if (mirror_holds_combat(m.kind, wo132::engaged(copy))) { motion::set_npc_engage(copy, true, nullptr, npcdrive::now_s()); c_combatHeld.fetch_add(1); }
        uint32_t have = 0;
        const bool match = step != Mirror::Clear && wo187::mirror_wish(copy, wish_kind(m.kind), m.target, &have);
        if (step == Mirror::Clear) wo187::mirror_wish(copy, 0, 0, nullptr);
        if (match && !m.followed) {
            m.followed = true;
            c_follows.fetch_add(1);
            if (g_followMs.size() >= 512) g_followMs.erase(g_followMs.begin());
            g_followMs.push_back(static_cast<float>((now - m.changedAt) * 1000.0));
        }
        if (sample_counts(m.kind, now - m.changedAt)) {
            c_samples.fetch_add(1);
            if (match) c_match.fetch_add(1);
            // WO-223 F: the record of every copy whose host NPC fights this player -- in his fight? aimed at him? for how long not?
            wo223::note_copy(copy, name_of(copy), m.kind == kHerePlayer, have ? who(have).c_str() : "", match, wo208::in_player_fight(copy), now);
        }
        ++it;
    }
    static double s_log = 0;
    if (now - s_log > 30.0 && c_samples.load() > 0) {
        s_log = now;
        char b[256];
        status(b, sizeof b);
        logf("WO209-OPP status %s", b);
    }
}

float follow_p(double q) {
    if (g_followMs.empty()) return -1;
    std::vector<float> v = g_followMs;
    std::sort(v.begin(), v.end());
    const size_t k = (std::min)(v.size() - 1, static_cast<size_t>(q * static_cast<double>(v.size())));
    return v[k];
}

// ---- B: the copy's swing as its own combat AI's attack -----------------------------------------------------------------------------
// The engine's AI attack (CombatModule 1.5.5 +0x3189B0, C_CombatDebugAIBrain's): (brain, zone) -> the zone set the engine's way (-1: its
// own pick), an Attack built by the combat actor's attack factory ([[ca+0x498]+0xB8], the "attack" type) and handed to the actor's
// director (C_ActionDirector::SetAction) -- the same three steps the AI's and the player's attacks take. It reads only the brain's combat
// actor ([brain+8]), so the copy's own actor is passed in a two-pointer stand-in. Found by its own bytes (the profiler call masked),
// exactly once in CombatModule's .text, or never used (B then leaves every swing to the owner's animation, as before).
void* g_aiAttack = nullptr;
int g_aiAttackState = 0;   // 0 unresolved, 1 found, -1 not found
void* resolve_ai_attack() {
    if (g_aiAttackState) return g_aiAttack;
    g_aiAttackState = -1;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    anchor::Range t{};
    if (!cm || !anchor::section(cm, ".text", &t)) return nullptr;
    static const uint8_t pat[] = {0x48, 0x89, 0x5C, 0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x30, 0x48, 0x8B, 0xF9, 0x8B, 0xDA, 0x48, 0x8D, 0x0D, 0, 0, 0, 0,
                                  0xE8, 0, 0, 0, 0, 0x48, 0x8B, 0x4F, 0x08, 0xBA, 0x08, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x01, 0xFF, 0x90, 0xA0, 0x00, 0x00, 0x00};
    static bool wild[sizeof pat] = {};
    for (int i : {18, 19, 20, 21, 23, 24, 25, 26}) wild[i] = true;
    int n = 0;
    const uint8_t* hit = nullptr;
    for (const uint8_t* p = t.begin; p + sizeof pat <= t.end; ++p) {
        size_t k = 0;
        while (k < sizeof pat && (wild[k] || p[k] == pat[k])) ++k;
        if (k == sizeof pat) { hit = p; if (++n > 1) break; }
    }
    if (n == 1) { g_aiAttack = const_cast<uint8_t*>(hit); g_aiAttackState = 1; }
    logf("WO209-ATK the engine's AI attack %s (CombatModule+0x%llX, %d match(es))", n == 1 ? "found" : "NOT found -- swings stay the owner's animation",
         hit ? static_cast<unsigned long long>(hit - reinterpret_cast<const uint8_t*>(cm)) : 0ULL, n);
    return g_aiAttack;
}

struct AiWindow { uint32_t eid; double begin, end = 0; const void* act = nullptr; const void* pre = nullptr; bool dodged = false, midDone = false; bool chain = false, rowSeen = false; };
std::vector<AiWindow> g_aiWindows;   // main thread
struct AiPending { uint32_t eid, frag; int zone; double at; int side = -1; };
std::vector<AiPending> g_aiPending;  // main thread: refused attacks asked again (kAiRetryS)
std::atomic<uint32_t> c_aiNotHostile{0};   // attacks refused: the game holds the copy not hostile to the local player
std::atomic<uint32_t> c_aiCalls{0}, c_aiStarted{0}, c_aiRefused{0}, c_aiFault{0}, c_aiLanded{0}, c_aiMissed{0}, c_aiNone{0}, c_lockSamples{0}, c_lockOn{0};
std::atomic<uint32_t> c_aiRetried{0}, c_aiRetryLapsed{0}, c_rowAwaitMirror{0}, c_rowNotHeld{0};
std::atomic<uint32_t> c_aiOutOfFight{0};   // WO-210 E.1: stretches a held copy was out of his fight by the engine's relation (the owner's way)
void ai_retry_tick(double now);
void chain_tick(double now);
struct Chain {
    uint32_t eid; int zone; double at, since; double applied = 0; const void* pre = nullptr; uint32_t rowFrag = 0; int rowZone = -1; bool rowSeen = false; int input = 0; int rowSide = -1;
    double rowAt = 0;      // b6: when the follow-up's row came
    double hostAge = -1;   // ... and how old the host NPC's swing was then (that row's arrival less the row before it)
    bool forced = false;   // the mod opened the copy's slot for this input
    bool inSwing = true;   // the copy ran a swing when the input was made
};
std::vector<std::pair<uint32_t, double>> g_rowAt;   // main thread (b6): each copy's last attack row, when it came
std::vector<Chain> g_chains;   // main thread (WO-224 B): the host's chain inputs, oldest first
constexpr double kLockSampleS = 0.3;   // B.6 / A.4: the local player's own opponent read this far into the copy's attack (lock-on)

bool ai_attack_call(uint32_t eid, int zone, const void** act, const void** pre) {
    void* fn = resolve_ai_attack();
    if (!fn) return false;
    void* ca = nullptr; void* model = nullptr;
    if (!motion::combat_parts(eid, false, &ca, &model) || !ca) return false;
    struct Brain { void* vt; void* ca; } brain{nullptr, ca};
    bool ok = false;
    if (pre) *pre = wo187::director_scope(ca, 0).act;
    KCDMP_FAULT_CALL(site, "wo209::ai_attack");
    c_aiCalls.fetch_add(1);
    if (!fault::guarded(site, [&] { ok = reinterpret_cast<bool(__fastcall*)(void*, int)>(fn)(&brain, zone); })) { c_aiFault.fetch_add(1); return false; }
    // the director may install the attack on its next update: the scope-0 action read now can still be the one before it (d2's 57 ms
    // windows) -- then the window adopts the first other action that appears there
    if (ok && act) { *act = wo187::director_scope(ca, 0).act; if (pre && *act == *pre) *act = nullptr; }
    return ok;
}

// A.4 / B.6 attribution: a body's bearing from the local player -- degrees off his body's facing (forward = (-sin yaw, cos yaw)) and
// metres; the engine's lock-on pick scores the candidates in his view cone by angle and distance (WO-162 Q2.1)
bool bearing(void* pe, uint32_t eid, float* deg, float* m) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    float a[3], p[3];
    if (!pe || !e || !engine::entity_world_pos(e, a) || !engine::entity_world_pos(pe, p)) return false;
    const float yaw = npcdrive::entity_yaw(pe);
    const float fx = -std::sin(yaw), fy = std::cos(yaw), dx = a[0] - p[0], dy = a[1] - p[1];
    const float n = std::sqrt(dx * dx + dy * dy);
    *m = n;
    *deg = n > 0.01f ? std::acos((std::max)(-1.0f, (std::min)(1.0f, (fx * dx + fy * dy) / n))) * 57.29578f : 0.0f;
    return true;
}

void ai_window_tick(double now) {
    void* pe = engine::entity_by_id(kPlayerEntity);
    const uint32_t me = pe ? engine::entity_id(pe) : 0;
    motion::ModelRead pm{};
    const bool dodging = me && motion::read_model(me, &pm) && (pm.valid & motion::kMvState) && (pm.state & wo208rules::kPlayerStateDodge) != 0;
    for (auto it = g_aiWindows.begin(); it != g_aiWindows.end();) {
        AiWindow& w = *it;
        if (w.end == 0) {
            if (dodging) w.dodged = true;
            if (!w.midDone && now - w.begin >= kLockSampleS) {   // lock-on: is the swinging copy the local player's own opponent now?
                w.midDone = true;
                motion::ModelRead pm2{};
                const uint32_t po = me && motion::read_model(me, &pm2) ? pm2.opponentEid : 0;
                c_lockSamples.fetch_add(1);
                if (po == w.eid) c_lockOn.fetch_add(1);
                const uint32_t k = c_lockSamples.load();
                // every one (one line a swing): the bench's lock-on count and the measures read them
                float ca = -1, cm = -1, oa = -1, om = -1;
                const bool cb = bearing(pe, w.eid, &ca, &cm), ob = po && po != w.eid && bearing(pe, po, &oa, &om);
                logf("WO209-ATK mid npc=%s player_opponent=%s [lock %u/%u] copy_at=%.0fdeg/%.1fm opponent_at=%.0fdeg/%.1fm", name_of(w.eid),
                     po == w.eid ? "this-copy" : po ? who(po).c_str() : "none", c_lockOn.load(), k, cb ? ca : -1.0f, cb ? cm : -1.0f, ob ? oa : -1.0f, ob ? om : -1.0f);
            }
            void* ca = nullptr; void* model = nullptr;
            const bool alive = engine::entity_by_id(w.eid) && motion::combat_parts(w.eid, false, &ca, &model);
            const void* cur = alive ? wo187::director_scope(ca, 0).act : nullptr;
            if (alive && !w.act && cur && cur != w.pre) w.act = cur;   // the attack installed a frame after the call: adopted
            // WO-223 C (w223_copy_target): an attack asked for while the copy's last action still ran (a retry after the engine's
            // "busy") is installed when that action ends -- later than kAiAdoptS: the window closed at 0.25 s and told the host
            // "nothing landed", and the blow his engine judged 0.1-0.2 s later answered no blow of the host's (every host guess of
            // the two-bandit defence scene). It waits kAiAdoptLongS for the action instead.
            const bool waiting = alive && !w.act && now - w.begin < (wo223::on(wo223::kBitCopyTarget) ? wo223::kAiAdoptLongS : kAiAdoptS);   // not installed yet: wait for it a little
            const bool running = alive && (waiting || (w.act && cur == w.act));
            if (!running || now - w.begin > kAiAttackMaxS) w.end = now;
            ++it;
            continue;
        }
        if (now - w.end < kAiTellAfterS) { ++it; continue; }
        const bool landed = hits::last_local_hit_from(w.eid) >= w.begin - 0.05;
        const uint8_t o = ai_window_outcome(landed, w.dodged, w.end - w.begin);
        if (landed) c_aiLanded.fetch_add(1);
        else {
            (o == wo208rules::kDefMissed ? c_aiMissed : c_aiNone).fetch_add(1);
            if (wo208::on(wo208rules::kBitDefenceLocal)) hits::report_defence(w.eid, o, 0.0f, 0.0f);
            logf("WO209-ATK npc=%s its attack ended (%.0f ms) with no blow landed on the local player%s -> told %s", name_of(w.eid), (w.end - w.begin) * 1000.0,
                     w.dodged ? " (his dodge seen)" : "", wo208rules::def_outcome_name(o));
        }
        it = g_aiWindows.erase(it);
    }
}

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 5 || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true))
            logf("WO209-CFG bits=0x%X (was 0x%X; twin off 0x%X): copy_opponent_mirror=%d copy_attack_ai=%d copy_paired_engine=%d copy_oneshot_request=%d", bits, was,
                 twin_off(), on(kBitOppMirror) ? 1 : 0, on(kBitAttackAi) ? 1 : 0, on(kBitPairedEngine) ? 1 : 0, on(kBitShotRequest) ? 1 : 0);
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[768];
        int n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) >= sizeof t) n = static_cast<int>(sizeof t) - 1;   // snprintf's count is the untruncated length
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    if (opc == kOpOppRead) return op_opp_read(body, len, out, cap, outLen);
    if (opc == kOpOppIn) return op_opp_in(body, len, out, cap, outLen);
    return wo163::kRBadRequest;
}

void tick() {
    const double now = qpc_s();
    KCDMP_FAULT_CALL(site, "wo209::mirror_tick");
    if (!fault::guarded(site, [now] { mirror_tick(now); })) logf("WO209-OPP mirror tick FAULTED");
    if (!g_aiWindows.empty()) fault::guarded_silent([now] { ai_window_tick(now); });
    if (!g_aiPending.empty()) fault::guarded_silent([now] { ai_retry_tick(now); });
    if (!g_chains.empty()) fault::guarded_silent([now] { chain_tick(now); });
}

} // namespace

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }

bool fix_on(uint32_t fixBit) { return (twin_off() & fixBit) == 0; }

bool mirror_holds_player(uint32_t copy) {
    if (!on(kBitOppMirror)) return false;
    auto it = g_mirror.find(copy);
    return it != g_mirror.end() && it->second.kind == kHerePlayer && qpc_s() - it->second.at <= kMirrorStaleS;
}

bool host_fights(uint32_t eid) {
    const auto it = g_hostOpp.find(eid);
    return it != g_hostOpp.end() && it->second.kind != kHostNone;
}

bool mirrored(uint32_t copy) {
    if (!on(kBitOppMirror)) return false;
    auto it = g_mirror.find(copy);
    return it != g_mirror.end() && qpc_s() - it->second.at <= kMirrorStaleS;
}

namespace {
// WO-210 E.1 (ai_in_fight): the copy's own attack claims the host's attack only while the engine holds the copy in his fight (F.1's relation,
// read-only); out of it (its own engine retargeted it to the host's figure) the owner shows the host's attack -- one line a stretch.
bool ai_in_his_fight(uint32_t copy) {
    const bool fix = wo210::fix_on(wo210rules::kFixAiInFight);
    const bool in = !fix || wo208::in_player_fight(copy);
    static std::unordered_map<uint32_t, double> s_out;   // main thread: copy -> when its stretch out of his fight began
    const double now = qpc_s();
    auto it = s_out.find(copy);
    if (in) { if (it != s_out.end()) s_out.erase(it); }
    else if (it == s_out.end() || now - it->second > 30.0) {
        s_out[copy] = now;
        const uint32_t k = c_aiOutOfFight.fetch_add(1) + 1;
        if (k <= 40 || k % 50 == 0)
            logf("WO210-ATK npc=%s is held in the local player's fight but out of it by the engine's relation (its own engine's target is "
                 "another): the host's attacks shown the owner's way, the host judges their blows [n=%u]", name_of(copy), k);
    }
    return wo210rules::ai_claims_attack(true, fix, in);
}
} // namespace

bool attack_ai_wants(uint32_t copy, uint8_t cls, uint32_t frag) {
    return on(kBitAttackAi) && cls == wo208rules::kClsCombatAnim && attack_frag(frag) && mirror_holds_player(copy) && wo132::engaged(copy)
           && resolve_ai_attack() != nullptr && ai_in_his_fight(copy);
}

bool ai_attack_recent(uint32_t copy, double withinS) {
    const double now = qpc_s();
    for (const AiWindow& w : g_aiWindows) if (w.eid == copy && now - w.begin < withinS) return true;
    return false;
}

bool ai_attack_running(uint32_t copy) {
    for (const AiWindow& w : g_aiWindows) if (w.eid == copy && w.end == 0) return true;
    return false;
}

bool ai_owns_body(uint32_t copy) { return on(kBitAttackAi) && ai_attack_running(copy); }

bool attack_ai_drives(uint32_t copy) {
    return on(kBitAttackAi) && mirror_holds_player(copy) && wo132::engaged(copy) && resolve_ai_attack() != nullptr && ai_in_his_fight(copy);
}

namespace {
bool ai_attack_zone(uint32_t copy, uint32_t hostFrag, int zone, const char* via, bool retry = false, bool chainInput = false, int guardSide = -1);
bool chain_takes(uint32_t copy, uint32_t hostFrag, int zone, const char* via, int guardSide);
}

bool ai_attack(uint32_t copy, uint32_t hostFrag) {
    // the host NPC's own attack zone: WO-132's engagement holds it on the copy (its NpcCombat atkZone through the engine's setter); none
    // read -> the engine's own pick (-1), as the AI's
    motion::ModelRead m{};
    int zone = -1;
    if (motion::read_model(copy, &m) && (m.valid & motion::kMvAtkZone) && m.atkZone >= 0 && m.atkZone < 16) zone = m.atkZone;
    return ai_attack_zone(copy, hostFrag, zone, "owner");
}

bool row_attack(uint32_t copy, const char* spec) {
    // B by the swing row the host's attack sent (WO-167's path): an attack row is the copy's own attack the same way, its zone the row's own
    // aZ tag -- for every copy, owned or not: the row comes as the host's attack starts, the owner's state a stream poll later (the owner's
    // early path then finds the attack running: ai_attack_recent)
    if (!spec || !on(kBitAttackAi) || motion::is_avatar_eid(copy)) return false;
    const uint32_t frag = attack_frag_by_name(spec);
    if (!frag || !resolve_ai_attack()) return false;
    const bool holds = mirror_holds_player(copy), engaged = wo132::engaged(copy);
    if (row_waits_for_mirror(holds, engaged)) {
        // asked again each frame (ai_retry_tick) until the mirror has the copy on him, or kAiRetryS lapses
        const uint32_t k = c_rowAwaitMirror.fetch_add(1) + 1;
        if (k <= 30 || k % 50 == 0) logf("WO209-ATK npc=%s host frag=%u: the row came before the mirror's word (%s) -- waits %.0f ms for it [n=%u]",
                                         name_of(copy), frag, holds ? "on him, not yet in his fight" : engaged ? "in his fight, not yet on him" : "not on him, not in his fight",
                                         kAiRetryS * 1000.0, k);
        if (!holds && !engaged) c_rowNotHeld.fetch_add(1);
        auto it = std::find_if(g_aiPending.begin(), g_aiPending.end(), [copy](const AiPending& p) { return p.eid == copy; });
        if (it != g_aiPending.end()) { it->frag = frag; it->zone = row_zone(spec); it->side = wo224::rules::row_guard_side(spec); }
        else if (g_aiPending.size() < 64) g_aiPending.push_back({copy, frag, row_zone(spec), qpc_s(), wo224::rules::row_guard_side(spec)});
        return false;
    }
    return ai_attack_zone(copy, frag, row_zone(spec), "row", false, false, wo224::rules::row_guard_side(spec));
}

// WO-224 A: a paired move of a copy in the engine's clinch with the local player, by the host's row (the same attack request, the
// same gates; in a clinch the engine's attack factory builds the clinch move or the sync attack and starts his half itself)
bool clinch_move(uint32_t copy, uint32_t hostFrag, int zone) {
    if (!on(kBitAttackAi) || !resolve_ai_attack()) return false;
    return ai_attack_zone(copy, hostFrag, zone, "clinch-row", true);
}

namespace {
// WO-224 B: the guard the host NPC's row starts from is the copy's before its attack is asked. The host NPC moves its guard and then
// attacks; the guard's word comes by the stream, later than the attack's row -- the copy attacked from where it stood, and three of
// its swings in four came from the other side than the host NPC's (b2: the same guard side in 22 % of 161 swings, the same attack
// zone in 23 %). Set at once, through the engine's own setter (the stream's); only when the model's guard is another.
std::atomic<uint32_t> c_stanceAsked{0}, c_stanceSet{0};
// Returns the guard zone the row starts from (the request's own guard zone then), or -1 when the row does not say / the switch is off.
int stance_fix(uint32_t copy, int side, int attackZone) {
    namespace r = wo224::rules;
    const int gz = r::guard_zone_of_side(side), gs = r::guard_stance_of_side(side);
    if (gz < 0 || !wo224::on(wo224::kBitChains)) return -1;
    c_stanceAsked.fetch_add(1);
    // the attack zone the host NPC's row strikes into: the model's requested attack zone, through the stream's own setter
    if (attackZone >= 0 && attackZone <= 5) motion::set_requested_atk_zone(copy, attackZone);
    motion::ModelRead m{};
    if (!motion::read_model(copy, &m) || !(m.valid & motion::kMvGuardZone) || m.guardZone == gz) return gz;
    if (!motion::set_guard_zone(copy, gz, gs)) return gz;
    const uint32_t k = c_stanceSet.fetch_add(1) + 1;
    if (k <= 30 || k % 50 == 0)
        logf("W224-STANCE npc=%s the host NPC's row starts from its %s guard, the copy stood in guard zone %d -- set to %d before its attack [n=%u]", name_of(copy),
             side == 1 ? "left" : "right", m.guardZone, gz, k);
    return gz;
}

bool ai_attack_zone(uint32_t copy, uint32_t hostFrag, int zone, const char* via, bool retry, bool chainInput, int guardSide) {
    // WO-224 B: the host told this attack's chain input already (the copy makes it when its own slot opens, or has made it): the
    // row that names the follow-up asks nothing more
    if (!retry && !chainInput && std::strcmp(via, "chain-fresh") != 0 && chain_takes(copy, hostFrag, zone, via, guardSide)) return true;
    if (wo195::dead_gate(copy, "ai-attack")) return false;
    if (!wo208::target_player_ok(copy, "ai-attack")) { if (!retry) c_aiRefused.fetch_add(1); return false; }   // F.1: only inside his fight
    // rc4-c1x / rc5-c1x's joiner crashes (CombatModule+0x8ff0a, 8-9 ms after an attack started): each from a copy the game itself held
    // not hostile to the local player (its own answer to his picker: no, 605 of 605 / 16 of 16 -- the hook says yes for the lock) while
    // the mod held it in his fight: no attack of its own at him from such a copy (the host's verdict for its blow)
    if (fix_on(wo209rules::kFixGameHostile) && wo178::game_hostile(copy) == 0) {
        const uint32_t k = c_aiNotHostile.fetch_add(1) + 1;
        if (!retry && (k <= 20 || k % 100 == 0))
            logf("WO209-ATK npc=%s host frag=%u: the game holds it not hostile to the local player (its own answer to his picker) -- no attack of its own at him [n=%u]",
                 name_of(copy), hostFrag, k);
        return false;
    }
    const void* act = nullptr;
    const double now = qpc_s();
    const void* pre = nullptr;
    // (the engine's AI attack asks a guard change to the zone it is given before it attacks: that zone is the row's starting guard,
    // not its attack zone -- given the attack zone, the copy swung from the other side)
    const int startGuard = guardSide >= 0 ? stance_fix(copy, guardSide, zone) : -1;
    const bool ok = ai_attack_call(copy, startGuard >= 0 ? startGuard : zone, &act, &pre);
    if (!ok) {
        if (retry) return false;
        const uint32_t k = c_aiRefused.fetch_add(1) + 1;
        if (k <= 30 || k % 50 == 0) logf("WO209-ATK npc=%s host frag=%u: the engine refused the copy's attack (busy: a hit, a block, its own action) -- asked again each frame for %.0f ms [n=%u]",
                                         name_of(copy), hostFrag, kAiRetryS * 1000.0, k);
        auto it = std::find_if(g_aiPending.begin(), g_aiPending.end(), [copy](const AiPending& p) { return p.eid == copy; });
        if (it != g_aiPending.end()) { it->frag = hostFrag; it->zone = zone; it->side = guardSide; }   // the host's newer attack: the one to start
        else if (g_aiPending.size() < 64) g_aiPending.push_back({copy, hostFrag, zone, now, guardSide});
        return false;
    }
    if (chainInput) return true;   // the input is made; its follow-up is the engine's (chain_tick opens its window when it starts)
    // every one (one line a swing): the bench's defender presses on it, the measures pair it with the host's swing (d4: past the 60th
    // start the lines stopped and the defender went blind)
    const uint32_t k = c_aiStarted.fetch_add(1) + 1;
    logf("WO209-ATK npc=%s host frag=%u zone=%d via=%s: the copy's own attack at the local player started (scope-0 action %p) [n=%u]", name_of(copy), hostFrag, zone, via, act, k);
    AiWindow w{copy, now};
    w.act = act;
    w.pre = pre;
    if (g_aiWindows.size() < 64) g_aiWindows.push_back(w);
    return true;
}

// B: a refused attack asked again each frame while kAiRetryS lasts (the copy's own attack before it ends, the host's next one starts at once)
void ai_retry_tick(double now) {
    for (auto it = g_aiPending.begin(); it != g_aiPending.end();) {
        const AiPending p = *it;
        bool started = false;
        for (const AiWindow& w : g_aiWindows) if (w.eid == p.eid && w.begin >= p.at) started = true;   // another path started one since
        if (started) { it = g_aiPending.erase(it); continue; }
        if (!ai_retry_due(now, p.at)) { c_aiRetryLapsed.fetch_add(1); it = g_aiPending.erase(it); continue; }
        if (!attack_ai_drives(p.eid)) { ++it; continue; }   // the mirror's word not yet in (a row first): waits
        if (ai_attack_zone(p.eid, p.frag, p.zone, "retry", true, false, p.side)) {
            const uint32_t k = c_aiRetried.fetch_add(1) + 1;
            if (k <= 40 || k % 50 == 0) logf("WO209-ATK npc=%s host frag=%u: started on a retry %.0f ms after the engine's refusal [n=%u]", name_of(p.eid), p.frag, (now - p.at) * 1000.0, k);
            // ai_attack_zone pushed no pending (retry): the iterator stays valid
            it = g_aiPending.erase(it);
            continue;
        }
        ++it;
    }
}
} // namespace

// ---- WO-224 B: the host NPC's chain input on its copy ---------------------------------------------------------------------------------
// The host's NPC chains its swings by a combo input made while its running swing's ComboSlot is open; the follow-up's own row is sent
// only when the follow-up starts -- one link later the copy's slot has closed, the copy's fresh attack is refused until its swing's
// recovery ends, and the blow lands after the host stopped waiting for the joiner's verdict (its guess). Now the host tells the input
// as it is made (wo224.cpp's hook), and the copy makes the same input -- the engine's own AI attack, which takes its combo path when
// the slot is open -- in the frame ITS slot opens. Its follow-up then starts at its own swing's chain point: the host's timing, one
// link later. The row that names the follow-up finds the chain and asks nothing more; when the engine did not chain (a hit, a block
// in between), the row's attack is asked fresh, as before.
namespace {
struct ComboAbi { int state = 0; ptrdiff_t slotOff = 0, compOff = 0; const uint8_t* input = nullptr; };
ComboAbi g_combo;
int g_slotLayout = 0;   // 0 unchecked, 1 the property's value is the byte at +8 (its own getter's bytes say so), -1 not as read
std::atomic<uint32_t> c_chainRowFirst{0};
std::atomic<uint32_t> c_chainIn{0}, c_chainNotDriven{0}, c_chainApplied{0}, c_chainFollowed{0}, c_chainNotFollowed{0}, c_chainLapsed{0}, c_chainFresh{0}, c_chainRows{0},
    c_chainRowsAfter{0}, c_chainNoBody{0};
std::vector<float> g_chainWaitMs, g_chainFollowMs;   // main thread: the host's word -> the copy's input; the input -> the follow-up's start

bool resolve_combo() {
    if (g_combo.state) return g_combo.state == 1;
    g_combo.state = -1;
    const uint8_t* f = static_cast<const uint8_t*>(resolve_ai_attack());
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    anchor::Range t{};
    if (!f || !cm || !anchor::section(cm, ".text", &t) || f + 0x160 > t.end) { logf("W224-CHAIN not armed: the engine's AI attack is not found on this build"); return false; }
    int nA = 0, nB = 0;
    for (const uint8_t* p = f; p < f + 0x120; ++p) {
        // add rcx,imm32; mov rax,[rcx]; call [rax+8]; mov rcx,[rdi+8]; test al,al -- the "ComboSlot" property's read
        if (p[0] == 0x48 && p[1] == 0x81 && p[2] == 0xC1 && p[7] == 0x48 && p[8] == 0x8B && p[9] == 0x01 && p[10] == 0xFF && p[11] == 0x50 && p[12] == 0x08 && p[13] == 0x48 &&
            p[14] == 0x8B && p[15] == 0x4F && p[16] == 0x08 && p[17] == 0x84 && p[18] == 0xC0) {
            int32_t v = 0;
            std::memcpy(&v, p + 3, 4);
            g_combo.slotOff = v;
            ++nA;
        }
        // mov rcx,[rcx+imm32]; mov edx,ebx; call rel32 -- the combo component's input
        if (p[0] == 0x48 && p[1] == 0x8B && p[2] == 0x89 && p[7] == 0x8B && p[8] == 0xD3 && p[9] == 0xE8) {
            int32_t v = 0, rel = 0;
            std::memcpy(&v, p + 3, 4);
            std::memcpy(&rel, p + 10, 4);
            g_combo.compOff = v;
            g_combo.input = p + 14 + rel;
            ++nB;
        }
    }
    const bool ok = nA == 1 && nB == 1 && g_combo.slotOff > 0 && g_combo.slotOff < 0x4000 && g_combo.compOff > 0 && g_combo.compOff < 0x2000 && t.contains(g_combo.input);
    char a[64] = "?";
    if (ok) anchor::describe(g_combo.input, a, sizeof a);
    logf("W224-CHAIN the combo's places off the engine's AI attack: %s (slot reads %d, input calls %d; ComboSlot at model+0x%llX, the combo component at +0x%llX, its input %s)",
         ok ? "found" : "NOT found -- chains stay one row a swing", nA, nB, static_cast<unsigned long long>(g_combo.slotOff), static_cast<unsigned long long>(g_combo.compOff), a);
    if (ok) g_combo.state = 1;
    return ok;
}

// the property's own getter (its vtable +8) must read the byte at +8 -- by its bytes, once; else nothing is read through it
bool slot_layout(void* model) {
    if (g_slotLayout) return g_slotLayout == 1;
    if (!model || !resolve_combo()) return false;
    uint8_t code[0x40]{};
    bool read = false;
    KCDMP_FAULT_READ(site, "wo209::slot_layout");
    fault::guarded(site, [&] {
        const void* prop = static_cast<const char*>(model) + g_combo.slotOff;
        const void* const* vt = *reinterpret_cast<const void* const* const*>(prop);
        if (!vt || !fault::plausible_address(reinterpret_cast<uintptr_t>(vt))) return;
        const uint8_t* get = static_cast<const uint8_t*>(vt[1]);
        if (!get || !fault::plausible_address(reinterpret_cast<uintptr_t>(get))) return;
        std::memcpy(code, get, sizeof code);
        read = true;
    });
    if (!read) return false;   // asked again with the next model
    bool byte8 = false;
    for (size_t i = 0; i + 4 <= sizeof code && !byte8; ++i) {
        // movzx eax, byte ptr [rcx|rbx+8]  /  mov al, [rcx|rbx+8]
        if (code[i] == 0x0F && code[i + 1] == 0xB6 && (code[i + 2] == 0x41 || code[i + 2] == 0x43) && code[i + 3] == 0x08) byte8 = true;
        if (code[i] == 0x8A && (code[i + 1] == 0x41 || code[i + 1] == 0x43) && code[i + 2] == 0x08) byte8 = true;
    }
    g_slotLayout = byte8 ? 1 : -1;
    logf("W224-CHAIN the ComboSlot property's getter %s", byte8 ? "reads the byte at +8: the slot is read there" : "does NOT read the byte at +8 on this build -- chains stay one row a swing");
    return byte8;
}

float chain_p(const std::vector<float>& src, double q) {
    if (src.empty()) return -1;
    std::vector<float> v = src;
    std::sort(v.begin(), v.end());
    return v[(std::min)(v.size() - 1, static_cast<size_t>(q * static_cast<double>(v.size())))];
}

// the row that names a chain's follow-up: nothing more is asked of the engine
bool chain_takes(uint32_t copy, uint32_t hostFrag, int zone, const char* via, int guardSide) {
    if (!wo224::on(wo224::kBitChains)) return false;
    const double now = qpc_s();
    // the host NPC's swing was this old when this row's attack started: the row's arrival less the row before it (one link both)
    double hostAge = -1;
    {
        auto it = std::find_if(g_rowAt.begin(), g_rowAt.end(), [copy](const std::pair<uint32_t, double>& p) { return p.first == copy; });
        if (it != g_rowAt.end()) {
            // (one attack can come twice -- its row, and the owner's state a stream poll later: the second word is not another swing)
            if (now - it->second >= wo224::rules::kChainHostAgeMinS) { hostAge = now - it->second; it->second = now; }
        } else {
            if (g_rowAt.size() >= 64) g_rowAt.erase(g_rowAt.begin());
            g_rowAt.emplace_back(copy, now);
        }
    }
    for (size_t i = 0; i < g_chains.size(); ++i) {
        Chain& c = g_chains[i];
        if (c.eid != copy) continue;
        const bool first = !c.rowSeen;
        c.rowSeen = true; c.rowFrag = hostFrag; c.rowZone = zone; c.rowSide = guardSide;
        if (first) {
            c.rowAt = now;
            c.hostAge = hostAge;
            // (the stat: the follow-up runs on the host already and the copy has not been able to chain -- its slot has not opened: its
            // swing made no contact on this screen, or runs behind. b5 asked the row's attack at once: the engine refused it while the
            // swing ran and swings were lost; the entry waits, and chain_tick opens the slot itself when the copy's swing is old enough)
            if (c.applied == 0) c_chainRowFirst.fetch_add(1);
            const uint32_t k = c_chainRows.fetch_add(1) + 1;
            if (k <= 40 || k % 50 == 0)
                logf("W224-CHAIN npc=%s host frag=%u via=%s: the row finds its chain input (%s) -- nothing more asked [n=%u]", name_of(copy), hostFrag, via,
                     c.applied > 0 ? "made, the follow-up is the engine's" : "waiting for the copy's own slot", k);
        }
        return true;   // the copy's first chain entry is the one this row belongs to
    }
    for (AiWindow& w : g_aiWindows) {
        if (w.eid != copy || !w.chain || w.rowSeen || now - w.begin > wo224::rules::kChainRowAfterS) continue;
        w.rowSeen = true;
        const uint32_t k = c_chainRowsAfter.fetch_add(1) + 1;
        if (k <= 40 || k % 50 == 0)
            logf("W224-CHAIN npc=%s host frag=%u via=%s: the row names the follow-up that started %.0f ms ago by its chain -- nothing more asked [n=%u]", name_of(copy), hostFrag, via,
                 (now - w.begin) * 1000.0, k);
        return true;
    }
    return false;
}

// b6: the engine's own setter of the model's bool properties, found off the combo trigger's "enter" (the engine's opener of the slot):
//   mov dl,1; mov rcx,[rax+2F0h] (the actor's model); add rcx,<ComboSlot>; add rsp,20h; pop rbx; jmp <setter>
// one such place in the module, and the setter's own first bytes as read; else the force stays off (the chain waits as before).
const uint8_t* g_slotSetFn = nullptr;
int g_slotSetState = 0;
std::atomic<uint32_t> c_chainForced{0}, c_chainForcedFollowed{0}, c_slotOpened{0}, c_slotClosed{0};

bool force_on() {
    static int s = -1;
    if (s < 0) {
        char b[8] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W224_FORCE", b, sizeof b);
        s = (n > 0 && n < sizeof b && b[0] == '0') ? 0 : 1;   // the bench's twin of this step alone
        if (!s) logf("W224-CHAIN the slot's force is OFF by the bench's switch (KCDMP_TEST_W224_FORCE=0)");
    }
    return s == 1;
}

bool resolve_slot_set() {
    if (g_slotSetState) return g_slotSetState == 1;
    g_slotSetState = -1;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    anchor::Range t{};
    if (!resolve_combo() || !cm || !anchor::section(cm, ".text", &t) || t.end - t.begin < 64) return false;
    uint8_t pat[21] = {0xB2, 0x01, 0x48, 0x8B, 0x88, 0xF0, 0x02, 0x00, 0x00, 0x48, 0x81, 0xC1, 0, 0, 0, 0, 0x48, 0x83, 0xC4, 0x20, 0x5B};
    const int32_t off = static_cast<int32_t>(g_combo.slotOff);
    std::memcpy(pat + 12, &off, 4);
    const uint8_t* hit = nullptr;
    int n = 0;
    for (const uint8_t* p = t.begin; p + 26 <= t.end; ++p) {
        if (p[0] != 0xB2 || p[1] != 0x01 || p[21] != 0xE9 || std::memcmp(p, pat, sizeof pat) != 0) continue;
        hit = p;
        ++n;
    }
    const uint8_t* fn = nullptr;
    if (n == 1) {
        int32_t rel = 0;
        std::memcpy(&rel, hit + 22, 4);
        fn = hit + 26 + rel;
    }
    // the setter's own first bytes: mov [rsp+18h],rbx; push rbp; sub rsp,40h; mov rbp,rcx; movzx ebx,dl
    static const uint8_t head[] = {0x48, 0x89, 0x5C, 0x24, 0x18, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0xE9, 0x0F, 0xB6, 0xDA};
    const bool ok = fn && t.contains(fn) && fn + sizeof head <= t.end && std::memcmp(fn, head, sizeof head) == 0;
    char a[64] = "?";
    if (ok) anchor::describe(fn, a, sizeof a);
    logf("W224-CHAIN the slot's setter off the combo trigger's enter: %s (places %d; setter %s)", ok ? "found" : "NOT found -- a copy's slot is never opened by the mod", n, a);
    if (ok) { g_slotSetFn = fn; g_slotSetState = 1; }
    return ok;
}

// The copy's chain slot set through the engine's own setter (the property's change is signalled as the engine's own). Main thread;
// not under the fault guard.
bool slot_set(void* model, bool open) {
    if (!model || !resolve_slot_set() || !slot_layout(model)) return false;
    void* prop = static_cast<char*>(model) + g_combo.slotOff;
    reinterpret_cast<void(__fastcall*)(void*, uint8_t)>(const_cast<uint8_t*>(g_slotSetFn))(prop, open ? 1 : 0);
    (open ? c_slotOpened : c_slotClosed).fetch_add(1);
    return true;
}

// The copy's own combo input, as the host's NPC made it: the gates every attack of a copy at the local player has, the same requested
// attack zone through the engine's own setter (the stream's writer's), then the engine's combo input on the copy's combo component
// -- the call its automation and a player's attack key make. Main thread; the input itself is not under the fault guard.
bool chain_apply(const Chain& c, void* ca) {
    if (wo195::dead_gate(c.eid, "chain-input") || !wo208::target_player_ok(c.eid, "chain-input")) return false;
    if (fix_on(wo209rules::kFixGameHostile) && wo178::game_hostile(c.eid) == 0) return false;
    void* comp = nullptr;
    KCDMP_FAULT_READ(site, "wo209::chain_apply");
    if (!fault::guarded(site, [&] { comp = *reinterpret_cast<void**>(static_cast<char*>(ca) + g_combo.compOff); }) || !comp) return false;
    if (c.zone >= 0) motion::set_requested_atk_zone(c.eid, c.zone);
    c_aiCalls.fetch_add(1);
    reinterpret_cast<void(__fastcall*)(void*, int32_t)>(const_cast<uint8_t*>(g_combo.input))(comp, c.input);
    return true;
}

void chain_fresh(const Chain& c, const char* why) {
    const uint32_t k = c_chainFresh.fetch_add(1) + 1;
    if (k <= 40 || k % 50 == 0) logf("W224-CHAIN npc=%s host frag=%u: %s -- the row's attack is asked fresh [n=%u]", name_of(c.eid), c.rowFrag, why, k);
    ai_attack_zone(c.eid, c.rowFrag, c.rowZone, "chain-fresh", false, false, c.rowSide);   // as any row's: asked again each frame when the engine is busy
}

void chain_tick(double now) {
    namespace r = wo224::rules;
    uint32_t seen[16];
    int nSeen = 0;
    for (size_t i = 0; i < g_chains.size();) {
        Chain c = g_chains[i];
        bool behind = false;
        for (int k = 0; k < nSeen; ++k) if (seen[k] == c.eid) behind = true;
        if (behind) { g_chains[i].since = now; ++i; continue; }   // one at a time for a copy: the next waits from when it is first
        if (nSeen < 16) seen[nSeen++] = c.eid;
        void* ca = nullptr; void* model = nullptr;
        if (!engine::entity_by_id(c.eid) || !motion::combat_parts(c.eid, false, &ca, &model) || !ca || !model) {
            c_chainNoBody.fetch_add(1);
            g_chains.erase(g_chains.begin() + static_cast<ptrdiff_t>(i));
            continue;
        }
        const wo187::DirAction cur = wo187::director_scope(ca, 0);
        if (c.applied == 0) {
            bool known = false;
            const bool open = combo_slot_open(model, &known);
            const bool swing = r::attack_action(cur.name);
            // b6: the force's moment -- the copy's swing as old as the host NPC's was when its follow-up started
            bool forceDue = false;
            double copyAge = -1;
            if (c.rowSeen && swing && !open && known && force_on()) {
                double begin = 0;
                for (const AiWindow& w : g_aiWindows) if (w.eid == c.eid && w.end == 0 && w.begin > begin) begin = w.begin;
                if (begin > 0) copyAge = now - begin;
                forceDue = r::chain_force_due(now - c.rowAt, copyAge, c.hostAge);
            }
            const int step = r::chain_before(open, attack_ai_drives(c.eid), swing, c.rowSeen, now - c.since, forceDue);
            if (step == r::kChainWait) { ++i; continue; }
            if (step == r::kChainApply || step == r::kChainForce) {
                const bool force = step == r::kChainForce;
                // (a plain swing only: a paired strike's slot is the pair's; the slot through the engine's own setter, main thread)
                if (force && (std::strcmp(cur.name, "Attack") != 0 || !slot_set(model, true))) { ++i; continue; }
                // the engine's own combo input, in the frame the slot is open (the zone the host NPC's, then the input)
                wo224::model_dump(2, c.eid, c.zone, c.input);   // (research, the bench only: the copy as it makes the input)
                if (chain_apply(c, ca)) {
                    g_chains[i].applied = now;
                    g_chains[i].pre = cur.act;
                    g_chains[i].forced = force;
                    g_chains[i].inSwing = swing;
                    const uint32_t k = c_chainApplied.fetch_add(1) + 1;
                    if (force) c_chainForced.fetch_add(1);
                    if (g_chainWaitMs.size() < 4096) g_chainWaitMs.push_back(static_cast<float>((now - c.at) * 1000.0));
                    if (force)
                        logf("W224-CHAIN npc=%s requested_zone=%d input=%d: the copy's own chain input made %.0f ms after the host's word (its slot OPENED BY THE MOD: no contact on "
                             "this screen %.0f ms after the follow-up's row; the copy's swing %.0f ms old, the host NPC's was %.0f; its action %s) [n=%u]",
                             name_of(c.eid), c.zone, c.input, (now - c.at) * 1000.0, (now - c.rowAt) * 1000.0, copyAge * 1000.0, c.hostAge * 1000.0, cur.name, k);
                    else
                        logf("W224-CHAIN npc=%s requested_zone=%d input=%d: the copy's own chain input made %.0f ms after the host's word (its slot open, its action %s) [n=%u]",
                             name_of(c.eid), c.zone, c.input, (now - c.at) * 1000.0, cur.name[0] ? cur.name : "?", k);
                } else if (force) {
                    slot_set(model, false);
                }
                ++i;
                continue;
            }
            g_chains.erase(g_chains.begin() + static_cast<ptrdiff_t>(i));   // before the fresh attack: its row must not find this entry
            if (step == r::kChainFresh) chain_fresh(c, swing ? "the copy's slot did not open in time" : "the copy runs no swing to chain from");
            else {
                const uint32_t k = c_chainLapsed.fetch_add(1) + 1;
                if (k <= 40 || k % 50 == 0)
                    logf("W224-CHAIN npc=%s zone=%d: the copy's slot did not open for %.0f ms after the host's word (its action %s) and no row came -- dropped [n=%u]", name_of(c.eid),
                         c.zone, (now - c.since) * 1000.0, cur.name[0] ? cur.name : "?", k);
            }
            continue;
        }
        const int after = r::chain_after(cur.act != c.pre, cur.name, now - c.applied, c.inSwing);
        if (after == r::kAfterWait) { ++i; continue; }
        g_chains.erase(g_chains.begin() + static_cast<ptrdiff_t>(i));
        // A slot the mod opened is the mod's to close (the engine's trigger closes only what it opened; the follow-up's own contact
        // opens it anew). And an input made in a guard that brought nothing: the slot is closed before the fresh attack -- the
        // engine's attack takes its combo path while the slot reads open, and would start nothing again.
        if (c.forced || (!c.inSwing && after != r::kAfterFollowed)) {
            bool known = false;
            if (combo_slot_open(model, &known) && known) slot_set(model, false);
        }
        if (after == r::kAfterFollowed) {
            c_chainFollowed.fetch_add(1);
            if (c.forced) c_chainForcedFollowed.fetch_add(1);
            if (g_chainFollowMs.size() < 4096) g_chainFollowMs.push_back(static_cast<float>((now - c.applied) * 1000.0));
            const uint32_t k = c_aiStarted.fetch_add(1) + 1;
            // the same line as every start of a copy's own attack (the bench's defender and the measures read it)
            logf("WO209-ATK npc=%s host frag=%u zone=%d via=%s: the copy's own attack at the local player started (scope-0 action %p) [n=%u] -- %.0f ms after its chain input, action=%s",
                 name_of(c.eid), c.rowFrag, c.zone, c.forced ? "chain-forced" : "chain", cur.act, k, (now - c.applied) * 1000.0, cur.name);
            AiWindow w{c.eid, now};
            w.act = cur.act; w.pre = c.pre; w.chain = true; w.rowSeen = c.rowSeen;
            if (g_aiWindows.size() < 64) g_aiWindows.push_back(w);
        } else if (after == r::kAfterNotFollowed) {
            const uint32_t k = c_chainNotFollowed.fetch_add(1) + 1;
            if (k <= 40 || k % 50 == 0)
                logf("W224-CHAIN npc=%s zone=%d: the copy's swing ended in %s, not in a follow-up, %.0f ms after its chain input%s [n=%u]", name_of(c.eid), c.zone,
                     cur.name[0] ? cur.name : "nothing", (now - c.applied) * 1000.0, c.forced ? " (slot opened by the mod)" : "", k);
            if (c.rowSeen) chain_fresh(c, "the engine did not chain");
        } else {
            const uint32_t k = c_chainLapsed.fetch_add(1) + 1;
            if (k <= 40 || k % 50 == 0)
                logf("W224-CHAIN npc=%s zone=%d: no follow-up %.0f ms after the copy's chain input%s%s [n=%u]", name_of(c.eid), c.zone, (now - c.applied) * 1000.0,
                     c.inSwing ? "" : " (made with no swing running)", c.forced ? " (slot opened by the mod)" : "", k);
            if (c.rowSeen) chain_fresh(c, "no follow-up came of the chain input");
        }
    }
}
} // namespace

bool combo_abi(ptrdiff_t* slotOff, const void** inputFn) {
    if (!resolve_combo()) return false;
    if (slotOff) *slotOff = g_combo.slotOff;
    if (inputFn) *inputFn = g_combo.input;
    return true;
}

bool combo_slot_open(void* model, bool* known) {
    const bool ok = slot_layout(model);
    if (known) *known = ok;
    if (!ok) return false;
    uint8_t v = 0;
    KCDMP_FAULT_READ(site, "wo209::combo_slot");
    if (!fault::guarded(site, [&] { v = *(static_cast<const uint8_t*>(model) + g_combo.slotOff + 8); })) return false;
    return v != 0;
}

int chain_input(uint32_t copy, int zone, int input) {
    if (!wo224::on(wo224::kBitChains) || !on(kBitAttackAi) || !copy || motion::is_avatar_eid(copy) || !resolve_combo()) return 0;
    c_chainIn.fetch_add(1);
    if (!attack_ai_drives(copy)) {   // a copy that fights someone else plays the owner's animation, as before
        c_chainNotDriven.fetch_add(1);
        return 0;
    }
    if (g_chains.size() >= 64) { logf("W224-CHAIN npc=%s: 64 chain inputs wait already -- this one is not queued (its row's attack is asked as before)", name_of(copy)); return 0; }
    const double now = qpc_s();
    Chain c{copy, zone, now, now};
    c.input = input;
    g_chains.push_back(c);
    return 1;
}

int chain_status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n),
                         "places=%s slot_layout=%s in=%u not_this_players=%u waiting=%zu made=%u followed=%u not_followed=%u lapsed=%u no_body=%u rows_found_their_chain=%u (after its start %u) "
                         "fresh_instead=%u row_came_first=%u slot_opened_by_the_mod=%u (followed %u; setter %s, calls open=%u close=%u) | host word -> the copy's input p50=%.0f ms p90=%.0f ms | input -> follow-up p50=%.0f ms p90=%.0f ms | a row's starting guard: asked=%u set=%u",
                         g_combo.state == 1 ? "found" : g_combo.state ? "NOT-found" : "unresolved", g_slotLayout == 1 ? "byte+8" : g_slotLayout ? "NOT-as-read" : "unchecked", c_chainIn.load(),
                         c_chainNotDriven.load(), g_chains.size(), c_chainApplied.load(), c_chainFollowed.load(), c_chainNotFollowed.load(), c_chainLapsed.load(), c_chainNoBody.load(),
                         c_chainRows.load(), c_chainRowsAfter.load(), c_chainFresh.load(), c_chainRowFirst.load(), c_chainForced.load(), c_chainForcedFollowed.load(),
                         g_slotSetState == 1 ? "found" : g_slotSetState ? "NOT-found" : "unresolved", c_slotOpened.load(), c_slotClosed.load(), chain_p(g_chainWaitMs, 0.5), chain_p(g_chainWaitMs, 0.9), chain_p(g_chainFollowMs, 0.5),
                         chain_p(g_chainFollowMs, 0.9), c_stanceAsked.load(), c_stanceSet.load());
}

namespace { std::atomic<uint32_t> c_pairStart{0}, c_pairStartRefused{0}, c_pairMove{0}, c_pairMoveRefused{0}, c_pairHeld{0}; }

bool paired_wants(uint32_t copy, uint32_t frag) {
    return on(kBitPairedEngine) && paired_role(frag) != Paired::No && mirror_holds_player(copy) && wo132::engaged(copy);
}

bool paired_engine(uint32_t copy, uint32_t frag) {
    const Paired role = paired_role(frag);
    if (role == Paired::Held || role == Paired::No) { c_pairHeld.fetch_add(1); return false; }
    if (!wo208::target_player_ok(copy, "paired")) return false;   // F.1: only inside his fight
    if (role == Paired::MasterMove) {
        const bool ok = ai_attack(copy, frag);   // the attack request builds the sync attack / the clinch move and starts his half itself
        (ok ? c_pairMove : c_pairMoveRefused).fetch_add(1);
        return ok;
    }
    char why[256];
    // WO-224 A: the engine's action type for a new clinch, through the mod's own start (the gate lets it by); off = as before WO-224
    // (a guard type where the engine wants an action type, behind the mod's own gate: refused every time)
    int r = wo224::on(wo224::kBitClinch) ? wo224::start_clinch(copy, 0, "its host NPC started one", why, sizeof why)
                                         : wo197::start_sync_guard(kClinchGuardType, copy, player_eid(), why, sizeof why);
    if (r == 2) r = 1;   // in a clinch with him already
    (r == 1 ? c_pairStart : c_pairStartRefused).fetch_add(1);
    const uint32_t k = c_pairStart.load() + c_pairStartRefused.load();
    if (k <= 40 || k % 25 == 0)
        logf("WO209-PAIR npc=%s host frag=%u: the clinch with the local player as its slave %s%s%s [n=%u]", name_of(copy), frag, r == 1 ? "started the engine's way" : "NOT started",
             r == 1 ? "" : " -- ", r == 1 ? "" : why, k);
    return r == 1;
}

int status(char* out, int n) {
    const uint32_t s = c_samples.load(), m = c_match.load();
    const uint32_t ts = c_twinSamples.load(), tm = c_twinMatch.load();
    return std::snprintf(out, static_cast<size_t>(n), "w209 bits=0x%X twin_off=0x%X configured=%d | opp host reads=%u out=%u changes=%u | mirror off-samples=%u off-match=%u (%.1f %%) | mirror copies=%zu in=%u bad=%u "
                         "samples=%u match=%u (%.1f %%) engaged=%u combat_held=%u engage_refused=%u cleared=%u follows=%u follow_p50=%.0f ms follow_p90=%.0f ms | "
                         "ai_attack %s calls=%u started=%u refused=%u not_hostile=%u out_of_fight=%u retried=%u retry_lapsed=%u row_await_mirror=%u row_not_held=%u faults=%u landed=%u missed=%u none=%u lock_on=%u/%u | paired start=%u start_refused=%u move=%u move_refused=%u held=%u",
                         g_bits.load(), twin_off(), g_configured.load() ? 1 : 0, c_oppReads.load(), c_oppOut.load(), c_oppChanges.load(), ts, tm, ts ? 100.0 * tm / ts : 0.0, g_mirror.size(),
                         c_in.load(), c_bad.load(), s, m, s ? 100.0 * m / s : 0.0, c_engaged.load(), c_combatHeld.load(), c_engageRefused.load(), c_cleared.load(), c_follows.load(),
                         follow_p(0.5), follow_p(0.9), g_aiAttackState == 1 ? "found" : g_aiAttackState ? "NOT-found" : "unresolved", c_aiCalls.load(),
                         c_aiStarted.load(), c_aiRefused.load(), c_aiNotHostile.load(), c_aiOutOfFight.load(), c_aiRetried.load(), c_aiRetryLapsed.load(), c_rowAwaitMirror.load(), c_rowNotHeld.load(), c_aiFault.load(), c_aiLanded.load(), c_aiMissed.load(), c_aiNone.load(), c_lockOn.load(), c_lockSamples.load(), c_pairStart.load(),
                         c_pairStartRefused.load(), c_pairMove.load(), c_pairMoveRefused.load(), c_pairHeld.load());
}

namespace {
int status_reg(char* out, int n) { return status(out, n); }
void tick_reg() { tick(); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_op(kOpOppRead, &op) && wo203::reg_op(kOpOppIn, &op)
               && wo203::reg_status("w209", &status_reg) && wo203::reg_tick("w209", &tick_reg);
} // namespace

} // namespace kcdmp::wo209
