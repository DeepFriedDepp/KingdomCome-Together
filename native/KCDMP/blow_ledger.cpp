// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-222 A.0 / E: every blow and every swing accounted for (blow_ledger.h). Read-only.
//
// Threads: the three on_* entry points may run on the engine's own threads (the action capture hook, the hit processor); they take
// g_mu, copy plain numbers in and return -- no engine call. Everything that reads the engine (names, souls, the combat model,
// positions) runs in tick(), on the main thread, where the rest of the mod reads them (thread and lock audit: the WO's notes).
#include "blow_ledger.h"

#include <windows.h>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

#include "engine.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "rttr_abi.h"
#include "wo132.h"
#include "wo173_rules.h"
#include "wo203.h"
#include "wo222.h"

namespace kcdmp::blow_ledger {
namespace {

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

constexpr double kNextWindowS = 0.8;     // what the body does next: its first own action this long after the blow
constexpr double kAttackRecentS = 1.3;   // an attack of its own started this recently = the blow lands mid-attack
constexpr double kBlockRecentS = 1.0;
constexpr double kHitRecentS = 1.0;
constexpr double kSwingWindowS = 1.5;
constexpr double kStrikeByS = 0.7;       // a swing's blade arrives by this long after its attack action entered    // a swing's touch is looked for this long after its attack action entered
constexpr float kReachM = 2.8f;          // a sword's reach and a step: farther in front is out of reach
constexpr float kFrontM = 6.0f, kFrontDeg = 60.0f;

struct History { double atk = -9, blk = -9, perf = -9, dodge = -9, hit = -9; };
struct Blow {
    uint32_t veid = 0; double t0 = 0; bool byFigure = false;
    uint8_t verdict = 0, react = 0, fallback = 0; int32_t kind = -1; double heldS = -1;
    int8_t type = -1, zone = -1, pType = -1, pZone = -1, pIc = -1; float strength = 0;
    History h{};               // the body's history at the blow
    bool read = false;         // the main-thread reads done (first tick after the blow)
    int32_t state = -1; float stPct = -1; int cm = -1; uint32_t opp = 0; int oppPlayer = -1;
    std::string name;
    int nextCls = -1; bool nextByPlayer = false; double nextDt = -1;
};
struct Swing { double t0 = 0; int8_t type = -1, zone = -1; uint32_t touched = 0; uint8_t verdict = 0; double hitAt = -1; };

std::mutex g_mu;
std::unordered_map<uint32_t, History> g_hist;   // eid -> its own last actions (any thread, under g_mu)
std::vector<Blow> g_blows;                       // pending (under g_mu)
std::vector<Swing> g_swings;                     // the joiner's pending swings (under g_mu)
struct LastAtk { double t = -9; int8_t type = -1, zone = -1, ic = -1; } g_lastAtk;
std::unordered_map<uint32_t, float> g_stMax;     // main thread
uint32_t c_blows[3]{}, c_swings = 0, c_touched = 0, c_untouched = 0;   // main thread
std::unordered_map<std::string, uint32_t> c_reason;   // main thread

const char* cls_name(int c) {
    switch (c) { case 0: return "attack"; case 1: return "dodge"; case 2: return "perfect"; case 3: return "block"; case 4: return "hit";
                 case 5: return "paired"; default: return "none"; }
}
const char* verdict_name(uint8_t v) {
    switch (v) { case 1: return "hit"; case 2: return "blocked"; case 3: return "pb"; case 4: return "broken";
                 case kGatedSent: return "sent-to-host"; case kGatedNoType: return "no-attack-type"; case kGatedRepeat: return "repeat-of-swing"; default: return "-"; }
}

// The body's situation when the blow landed (A.0's rows): its own blocked / mid-attack / after a block / in a hit reaction / low
// stamina / in guard.
const char* situation(const Blow& b) {
    const double t = b.t0;
    if (b.verdict == 2 || b.verdict == 3) return "blocked";
    if (t - b.h.atk < kAttackRecentS) return "mid-attack";
    if (t - b.h.blk < kBlockRecentS || t - b.h.perf < kBlockRecentS) return "after-block";
    if (t - b.h.hit < kHitRecentS) return "in-reaction";
    if (b.stPct >= 0 && b.stPct < 25.0f) return "low-stamina";
    return "guard";
}

void read_now(Blow& b) {
    b.read = true;
    if (void* e = engine::entity_by_id(b.veid)) { if (const char* n = engine::entity_name(e)) b.name = n; }
    if (b.name.empty()) b.name = "?";
    motion::ModelRead m{};
    if (motion::read_model(b.veid, &m)) {
        if (m.valid & motion::kMvState) b.state = m.state;
        if (m.valid & motion::kMvCombatMode) b.cm = m.combatMode;
        b.opp = m.opponentEid; b.oppPlayer = m.opponentIsPlayer;
    }
    if (void* soul = hits::soul_of_eid(b.veid)) {
        float st = -1;
        if (rttr::soul_state(soul, "stamina", &st) && st >= 0) {
            float& mx = g_stMax[b.veid];
            if (st > mx) mx = st;
            const float cap = mx > 100.0f ? mx : 100.0f;
            b.stPct = 100.0f * st / cap;
        }
    }
}

void finish_blow(const Blow& b) {
    const uint32_t k = ++c_blows[b.byFigure ? 1 : 0];
    char extra[200] = "";
    if (b.byFigure)
        std::snprintf(extra, sizeof extra, " react=%s kind=%d held_ms=%.0f row=t%d/z%d/s%.2f fallback=%u",
                      wo173rules::react_name(static_cast<wo173rules::React>(b.react)), b.kind, b.heldS < 0 ? -1.0 : b.heldS * 1000.0,
                      b.type, b.zone, b.strength, b.fallback);
    else
        std::snprintf(extra, sizeof extra, " atk=t%d/z%d/ic%d", b.pType, b.pZone, b.pIc);
    logf("W222-BLOW by=%s npc=%s verdict=%s sit=%s state=0x%X cm=%d opp=%s st=%.0f next=%s by_player=%d dt_ms=%.0f%s [n=%u]", b.byFigure ? "joiner" : "host",
         b.name.c_str(), verdict_name(b.verdict), situation(b), b.state < 0 ? 0u : static_cast<unsigned>(b.state), b.cm,
         b.oppPlayer == 1 ? "player" : b.opp ? "other" : "-", b.stPct, cls_name(b.nextCls),
         b.nextByPlayer ? 1 : 0, b.nextDt < 0 ? -1.0 : b.nextDt * 1000.0, extra, k);
}

// E: what was in front of the player's blade when a swing touched nothing.
void finish_swing(const Swing& s) {
    ++c_swings;
    const uint32_t k = c_swings;
    if (s.touched) {
        ++c_touched;
        c_reason[s.verdict == kGatedRepeat ? "touched-repeat" : s.verdict == kGatedNoType ? "touched-no-attack-type" : "touched"]++;
        const bool copy = npcdrive::is_bound_eid(s.touched);
        const char* n = nullptr;
        if (void* e = engine::entity_by_id(s.touched)) n = engine::entity_name(e);
        logf("W222-SWING by=%s n=%u touched=%s npc=%s engaged=%d verdict=%s reason=%s", wo222::role() == 1 ? "host" : "joiner", k, copy ? "copy" : "body", n ? n : "?",
             wo132::engaged(s.touched) ? 1 : 0, verdict_name(s.verdict), s.verdict == kGatedNoType ? "touched-no-attack-type" : s.verdict == kGatedRepeat ? "touched-repeat" : "touched");
        return;
    }
    ++c_untouched;
    if (s.hitAt >= 0) {   // his own hit reaction cut the swing short before its blade arrived (the same happens to anyone hit mid-swing)
        c_reason["interrupted-by-a-hit"]++;
        logf("W222-SWING by=%s n=%u touched=none hit_after_ms=%.0f reason=interrupted-by-a-hit", wo222::role() == 1 ? "host" : "joiner", k, (s.hitAt - s.t0) * 1000.0);
        return;
    }
    void* pe = engine::entity_by_id(0x7777);
    float pp[3]{};
    if (!pe || !engine::entity_world_pos(pe, pp)) { c_reason["no-player"]++; logf("W222-SWING by=%s n=%u touched=none reason=no-player", wo222::role() == 1 ? "host" : "joiner", k); return; }
    const float yaw = npcdrive::entity_yaw(pe);
    const float fx = -std::sin(yaw), fy = std::cos(yaw);
    npcdrive::BoundBody bb[64];
    const int nb = npcdrive::bound_bodies(bb, 64);
    int best = -1; float bestD = 1e9f, bestDeg = 0;
    for (int i = 0; i < nb; ++i) {
        void* e = engine::entity_by_id(bb[i].eid);
        float q[3]{};
        if (!e || !engine::entity_world_pos(e, q)) continue;
        const float dx = q[0] - pp[0], dy = q[1] - pp[1];
        const float d = std::sqrt(dx * dx + dy * dy);
        if (d < 0.05f || d > kFrontM) continue;
        const float c = (dx * fx + dy * fy) / d;
        const float deg = std::acos(c < -1 ? -1 : c > 1 ? 1 : c) * 57.2958f;
        if (deg > kFrontDeg) continue;
        if (d < bestD) { bestD = d; bestDeg = deg; best = i; }
    }
    // the player's own lock-on opponent (his combat model's), for every untouched swing
    char opp[48] = "-";
    {
        motion::ModelRead pm{};
        if (motion::read_model(engine::entity_id(pe), &pm) && pm.opponentEid) {
            const char* on = nullptr;
            if (void* oe = engine::entity_by_id(pm.opponentEid)) on = engine::entity_name(oe);
            std::snprintf(opp, sizeof opp, "%s", on ? on : "?");
        }
    }
    if (best < 0) { c_reason["nothing-in-front"]++; logf("W222-SWING by=%s n=%u touched=none reason=nothing-in-front", wo222::role() == 1 ? "host" : "joiner", k); return; }
    const uint32_t ce = bb[best].eid;
    const char* n = nullptr;
    if (void* e = engine::entity_by_id(ce)) n = engine::entity_name(e);
    bool dead = false;
    if (void* soul = hits::soul_of_eid(ce)) rttr::soul_bool(soul, "IsDead", &dead);
    const bool eng = wo132::engaged(ce);
    motion::NpcCombat c{};
    const bool cm = motion::read_npc_combat(ce, &c) && c.combat;
    const char* why = dead ? "dead" : bestD > kReachM ? "out-of-reach" : !eng ? "not-hostile-here" : bb[best].held ? "held" : "in-reach-no-touch";
    c_reason[why]++;
    logf("W222-SWING by=%s n=%u touched=none front=%s@%.1fm/%.0fdeg engaged=%d combat=%d held=%d speed=%.1f my_opponent=%s reason=%s", wo222::role() == 1 ? "host" : "joiner", k,
         n ? n : "?", bestD, bestDeg, eng ? 1 : 0, cm ? 1 : 0, bb[best].held ? 1 : 0, bb[best].speedMps, opp, why);
}

void tick() {
    const double now = now_s();
    std::vector<Blow> done;
    std::vector<Swing> sdone;
    {
        std::lock_guard<std::mutex> lk(g_mu);
        for (auto it = g_blows.begin(); it != g_blows.end();) {
            if (now - it->t0 >= kNextWindowS) { done.push_back(*it); it = g_blows.erase(it); }
            else ++it;
        }
        for (auto it = g_swings.begin(); it != g_swings.end();) {
            if (now - it->t0 >= kSwingWindowS) { sdone.push_back(*it); it = g_swings.erase(it); }
            else ++it;
        }
    }
    // the first reads of a fresh blow (the frame it landed in, or the next): outside the lock, then put back
    std::vector<Blow> fresh;
    {
        std::lock_guard<std::mutex> lk(g_mu);
        for (auto& b : g_blows) if (!b.read) { fresh.push_back(b); b.read = true; }
    }
    for (auto& b : fresh) {
        read_now(b);
        std::lock_guard<std::mutex> lk(g_mu);
        for (auto& p : g_blows) if (p.veid == b.veid && p.t0 == b.t0) { p.state = b.state; p.stPct = b.stPct; p.name = b.name; p.cm = b.cm; p.opp = b.opp; p.oppPlayer = b.oppPlayer; break; }
    }
    for (auto& b : done) { if (b.name.empty()) read_now(b); finish_blow(b); }
    for (const auto& s : sdone) finish_swing(s);
}

int status(char* out, int n) {
    std::string r;
    for (const auto& kv : c_reason) { r += ' '; r += kv.first; r += '='; r += std::to_string(kv.second); }
    return std::snprintf(out, static_cast<size_t>(n), "blows host=%u joiner=%u swings=%u touched=%u untouched=%u by_reason:%s", c_blows[0], c_blows[1],
                         c_swings, c_touched, c_untouched, r.empty() ? " -" : r.c_str());
}

const bool kReg = wo203::reg_tick("blow_ledger", &tick) && wo222::add_status("blow_ledger", &status);

void add_blow(Blow b) {
    std::lock_guard<std::mutex> lk(g_mu);
    if (g_blows.size() > 256) return;
    auto h = g_hist.find(b.veid);
    if (h != g_hist.end()) b.h = h->second;
    // a joiner's blow is recorded after wo173 applied it: a hit reaction its own SetAction started in that call entered just before
    if (b.byFigure && b.t0 - b.h.hit < 0.05) { b.nextCls = 4; b.nextByPlayer = false; b.nextDt = 0; b.h.hit = -9; }
    g_blows.push_back(std::move(b));
}

}   // namespace

void on_npc_action(uint32_t eid, uint8_t cls, bool byPlayer) {
    if (!eid) return;
    const double t = now_s();
    std::lock_guard<std::mutex> lk(g_mu);
    if (g_hist.size() > 1024) g_hist.clear();
    History& h = g_hist[eid];
    switch (cls) { case 0: case 5: h.atk = t; break; case 1: h.dodge = t; break; case 2: h.perf = t; break; case 3: h.blk = t; break; case 4: h.hit = t; break; default: break; }
    for (auto& b : g_blows) {
        if (b.veid != eid || b.nextCls >= 0 || t < b.t0) continue;
        b.nextCls = cls; b.nextByPlayer = byPlayer; b.nextDt = t - b.t0;
    }
}

void on_player_attack(int8_t type, int8_t zone, int8_t ic) {
    const double t = now_s();
    std::lock_guard<std::mutex> lk(g_mu);
    g_lastAtk = LastAtk{t, type, zone, ic};
    const uint8_t role = wo222::role();
    if ((role == 1 || role == 2) && wo222::on(wo222::kBitSwingLog) && g_swings.size() < 64) g_swings.push_back(Swing{t, type, zone, 0, 0});
}

void on_player_hit() {
    const double t = now_s();
    std::lock_guard<std::mutex> lk(g_mu);
    // before the blade's own moment (a swing strikes 0.5-0.6 s after its action starts): later, the swing had its chance
    for (auto& s : g_swings) if (!s.touched && s.hitAt < 0 && t - s.t0 < kStrikeByS && t >= s.t0) s.hitAt = t;
}

void on_player_blow(uint32_t veid, uint8_t verdict) {
    if (!veid) return;
    const uint8_t role = wo222::role();
    const double t = now_s();
    {
        std::lock_guard<std::mutex> lk(g_mu);
        // the newest swing still open that has not touched yet takes the touch (a blade's later contacts are the same swing)
        for (auto it = g_swings.rbegin(); it != g_swings.rend(); ++it)
            if (t - it->t0 < kSwingWindowS) { if (!it->touched) { it->touched = veid; it->verdict = verdict; } break; }
    }
    if (role != 1 || verdict > 4) return;
    Blow b{};
    b.veid = veid; b.t0 = t; b.verdict = verdict;
    {
        std::lock_guard<std::mutex> lk(g_mu);
        if (t - g_lastAtk.t < 2.5) { b.pType = g_lastAtk.type; b.pZone = g_lastAtk.zone; b.pIc = g_lastAtk.ic; }
    }
    add_blow(std::move(b));
}

void on_figure_blow(uint32_t veid, uint8_t verdict, uint8_t react, int32_t kind, double heldS, int8_t type, int8_t zone, float strength,
                    uint8_t fallback) {
    if (!veid || wo222::role() != 1) return;
    Blow b{};
    b.veid = veid; b.t0 = now_s(); b.byFigure = true; b.verdict = verdict; b.react = react; b.kind = kind; b.heldS = heldS;
    b.type = type; b.zone = zone; b.strength = strength; b.fallback = fallback;
    add_blow(std::move(b));
}

} // namespace kcdmp::blow_ledger
