// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-190: 0.60.9's native half (wo190.h).
#include "wo190.h"
#include "wo190_rules.h"
#include "wo163.h"
#include "wo166.h"
#include "wo178.h"
#include "wo187.h"

#include "engine.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"

#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>

namespace kcdmp::wo190 {

namespace {

using namespace kcdmp::wo190rules;

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }
uint64_t utc_ms() {
    FILETIME ft{};
    GetSystemTimePreciseAsFileTime(&ft);
    const uint64_t t = (static_cast<uint64_t>(ft.dwHighDateTime) << 32) | ft.dwLowDateTime;
    return t / 10000ULL - 11644473600000ULL;
}

constexpr uint32_t kPlayerEntity = 0x7777;
constexpr unsigned kLogFirst = 30;

// ---- switches (the agent pushes them, op 51) ----------------------------------------------------------------------------------
std::atomic<uint8_t> g_mode{static_cast<uint8_t>(Mode::Auto)};   // mp_anim_trace on -> On; mp_anim_scope_log auto (default) -> Auto; off -> Off
std::atomic<bool> g_idleWatch{true}, g_takeover{true}, g_requeueGate{true}, g_gaitHold{true};

std::atomic<uint32_t> c_lines{0}, c_linesDropped{0}, c_bodyReads{0}, c_readFail{0}, c_idleRequeues{0}, c_idlePulses{0}, c_idleFail{0},
    c_emptyGaps{0}, c_weaponAsks{0}, c_clinchAsks{0}, c_reqDeduped{0}, c_reqLimited{0};

const char* name_of(uint32_t eid) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "?";
}
uint32_t player_eid() {
    void* e = engine::entity_by_id(kPlayerEntity);
    return e ? engine::entity_id(e) : 0;
}

struct ScopeState { ScopeSnap snap; char action[48]{}; char name[24]{}; double since = 0; };
struct BodyTrace {
    Role role = Role::Copy;
    double readAt = 0, lastSeen = 0;
    ScopeState scopes[kMaxScopes];
    int nScopes = 0;
    Bucket bucket;
    uint32_t dropped = 0;
    // A1: the full body's empty stretch and the last re-queue
    double fullEmptySince = -1, lastRequeue = -1;
    bool emptyLogged = false;
};
std::unordered_map<uint32_t, BodyTrace> g_bodies;   // main thread
MinuteCap g_minute;
double g_lastPrune = 0;

struct Cand { uint32_t eid; Role role; float dist; float speed; double age; bool fight; };

bool body_fighting(uint32_t eid, Role role) {
    if (role == Role::Copy) return motion::fight_held(eid);
    bool cm = false;
    return motion::combat_mode_of(eid, &cm) && cm;   // the player / a figure: its combat mode (a figure's follows its partner's combat bit)
}

void tag_hex(uint32_t eid, char* out, size_t cap) {
    uint8_t t[20]{};
    if (!wo187::tag_state(eid, t, sizeof t)) { std::snprintf(out, cap, "?"); return; }
    size_t o = 0;
    for (size_t i = 0; i < sizeof t && o + 3 < cap; ++i) o += std::snprintf(out + o, cap - o, "%02x", t[i]);
}

void trace_body(const Cand& c, BodyTrace& bt, double now, Mode mode, bool logOn) {
    wo187::ScopeInfo sc[kMaxScopes];
    const bool wantAll = logOn;   // the idle watch alone needs only the full body (scope 0)
    const int n = wo187::read_all_scopes(c.eid, sc, wantAll ? kMaxScopes : 1);
    c_bodyReads.fetch_add(1);
    if (n <= 0) { c_readFail.fetch_add(1); return; }
    bt.lastSeen = now;
    // A1's empty stretch: scope 0 is FullBody (WO-187: checked by its vptr and name)
    const bool fullEmpty = sc[0].ok && sc[0].empty && std::strcmp(sc[0].name, "FullBody") == 0;
    if (fullEmpty) { if (bt.fullEmptySince < 0) bt.fullEmptySince = now; }
    else if (sc[0].ok) {
        if (bt.fullEmptySince >= 0 && now - bt.fullEmptySince > kIdleWatchEmptyS) c_emptyGaps.fetch_add(1);
        bt.fullEmptySince = -1; bt.emptyLogged = false;
    }
    if (!logOn) return;
    bool tagsRead = false;
    char tags[48] = "-";
    for (int i = 0; i < n && i < kMaxScopes; ++i) {
        ScopeSnap now_{};
        now_.ok = sc[i].ok; now_.empty = sc[i].empty; now_.action = sc[i].act; now_.frag = sc[i].frag; now_.flags = sc[i].flags; now_.prio = sc[i].prio;
        ScopeState& st = bt.scopes[i];
        const bool first = i >= bt.nScopes;
        if (!first && !scope_changed(st.snap, now_)) continue;
        if (!first || !now_.empty) {
            const double heldMs = first ? -1.0 : (now - st.since) * 1000.0;
            const int cap = mode == Mode::On ? kTraceLinesPerMin : kAutoLinesPerMin;
            const bool okBody = bt.bucket.take(now, mode == Mode::On ? kBodyLinesPerS : kAutoBodyLinesPerS, mode == Mode::On ? kBodyBurst : kAutoBodyBurst);
            if (okBody && g_minute.take(now, cap)) {
                if (!tagsRead && i == 0) { tag_hex(c.eid, tags, sizeof tags); tagsRead = true; }
                c_lines.fetch_add(1);
                logf("WO190-ANIM utc=%llu body=%s role=%s scope=%s action=%s frag=%u prio=%d flags=0x%X was=%s:%u held_ms=%.0f tags=%s spd=%.2f age_ms=%.0f fight=%d%s",
                     static_cast<unsigned long long>(utc_ms()), name_of(c.eid), role_name(c.role), sc[i].name, now_.empty ? "none" : sc[i].action,
                     now_.empty ? 0u : now_.frag, now_.empty ? 0 : now_.prio, now_.empty ? 0u : now_.flags, first ? "-" : (st.snap.empty ? "none" : st.action),
                     first || st.snap.empty ? 0u : st.snap.frag, heldMs, i == 0 ? tags : "-", c.speed, c.age < 0 ? -1.0 : c.age * 1000.0, c.fight ? 1 : 0,
                     bt.dropped ? "" : "");
                if (bt.dropped) { logf("WO190-ANIM body=%s %u lines not written (the rate limit)", name_of(c.eid), bt.dropped); bt.dropped = 0; }
            } else {
                ++bt.dropped;
                c_linesDropped.fetch_add(1);
            }
        }
        st.snap = now_;
        std::snprintf(st.action, sizeof st.action, "%s", sc[i].action);
        std::snprintf(st.name, sizeof st.name, "%s", sc[i].name);
        st.since = now;
    }
    if (n > bt.nScopes) bt.nScopes = n;
}

// A1: the guard idle back on a fighting body whose full body stands empty
void idle_watch(const Cand& c, BodyTrace& bt, double now) {
    if (bt.fullEmptySince < 0) return;
    const double emptyFor = now - bt.fullEmptySince;
    const double since = bt.lastRequeue < 0 ? -1.0 : now - bt.lastRequeue;
    if (!idle_watch_due(g_idleWatch.load(), c.fight, true, emptyFor, since)) return;
    bt.lastRequeue = now;
    bool combat = false;
    motion::combat_mode_of(c.eid, &combat);
    const char* remedy = "none (no remedy took)";
    if (combat && motion::requeue_guard_idle(c.eid)) { remedy = "its guard zone written away and back (the engine re-queues its guard idle)"; c_idleRequeues.fetch_add(1); }
    else if (motion::request_idle_pulse(c.eid)) { remedy = "WO-155's walk-class pulse (its locomotion graph again)"; c_idlePulses.fetch_add(1); }
    else c_idleFail.fetch_add(1);
    const uint32_t k = c_idleRequeues.load() + c_idlePulses.load() + c_idleFail.load();
    if (k <= kLogFirst || k % 50 == 0)
        logf("WO190-IDLE body=%s role=%s empty_ms=%.0f combat=%d -> %s (#%u)", name_of(c.eid), role_name(c.role), emptyFor * 1000.0, combat ? 1 : 0, remedy, k);
}

void gather(std::vector<Cand>& out, double now) {
    out.clear();
    void* pe = engine::entity_by_id(kPlayerEntity);
    float pp[3]{};
    const bool havePp = pe && engine::entity_world_pos(pe, pp);
    const uint32_t me = player_eid();
    if (me) out.push_back({me, Role::Player, 0.0f, 0.0f, -1.0, body_fighting(me, Role::Player)});
    npcdrive::BoundBody bb[160];
    const int n = npcdrive::bound_bodies(bb, 160);
    for (int i = 0; i < n; ++i) {
        void* e = engine::entity_by_id(bb[i].eid);
        if (!e) continue;
        float p[3]{};
        float d = -1.0f;
        if (havePp && engine::entity_world_pos(e, p)) d = std::sqrt((p[0] - pp[0]) * (p[0] - pp[0]) + (p[1] - pp[1]) * (p[1] - pp[1]) + (p[2] - pp[2]) * (p[2] - pp[2]));
        const Role r = motion::is_avatar_eid(bb[i].eid) ? Role::Figure : Role::Copy;
        if (r == Role::Copy && d > kNearM) continue;
        out.push_back({bb[i].eid, r, d < 0 ? 999.0f : d, bb[i].speedMps, bb[i].streamAgeS, body_fighting(bb[i].eid, r)});
    }
    std::sort(out.begin() + (out.empty() ? 0 : 1), out.end(), [](const Cand& a, const Cand& b) {
        if (a.role != b.role) return a.role == Role::Figure;   // figures first
        return a.dist < b.dist;
    });
    if (out.size() > static_cast<size_t>(kMaxBodies)) out.resize(kMaxBodies);
    (void)now;
}

} // namespace

bool idle_watch_on() { return g_idleWatch.load(); }
bool swing_takeover_on() { return g_takeover.load(); }
bool requeue_gate_on() { return g_requeueGate.load(); }
bool gait_hold_on() { return g_gaitHold.load(); }

void note_requeue(bool deduped, bool limited) {
    if (deduped) c_reqDeduped.fetch_add(1);
    if (limited) c_reqLimited.fetch_add(1);
}

void tick() {
    const Mode mode = mode_from_byte(g_mode.load());
    const bool watch = g_idleWatch.load();
    if (mode == Mode::Off && !watch) return;
    const double now = now_s();
    static double s_lastGather = 0;
    static std::vector<Cand> s_cands;
    if (now - s_lastGather >= 0.25) { s_lastGather = now; gather(s_cands, now); }
    const double period = mode == Mode::On ? kReadPeriodS : (mode == Mode::Auto ? kAutoReadPeriodS : kIdleWatchPeriodS);
    for (const Cand& c : s_cands) {
        const bool traced = mode == Mode::On || (mode == Mode::Auto && c.fight);
        const bool watched = watch && c.fight && c.role != Role::Player;
        if (!traced && !watched) continue;
        BodyTrace& bt = g_bodies[c.eid];
        bt.role = c.role;
        if (now - bt.readAt < period) continue;
        bt.readAt = now;
        trace_body(c, bt, now, mode, traced);
        if (watched) idle_watch(c, bt, now);
    }
    if (now - g_lastPrune > 5.0) {
        g_lastPrune = now;
        for (auto it = g_bodies.begin(); it != g_bodies.end();) {
            if (now - it->second.lastSeen > 10.0 || !engine::entity_by_id(it->first)) it = g_bodies.erase(it); else ++it;
        }
    }
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    switch (op) {
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = n <= 0 || cap == 0 ? 0 : (static_cast<size_t>(n) < cap ? static_cast<size_t>(n) : cap - 1);
            return wo163::kROk;
        }
        case kOpConfig: {
            // [op][mode][idleWatch][takeover][requeueGate][gaitHold] (255 = leave as is)
            constexpr int kN = 5;
            if (len < 1 || len > 1 + kN || cap < kN) return wo163::kRBadRequest;
            if (len >= 2 && body[1] != 255) {
                const Mode m = mode_from_byte(body[1]);
                if (g_mode.exchange(static_cast<uint8_t>(m)) != static_cast<uint8_t>(m)) logf("WO190-CONFIG trace %s", mode_name(m));
            }
            std::atomic<bool>* sw[kN - 1] = {&g_idleWatch, &g_takeover, &g_requeueGate, &g_gaitHold};
            const char* nm[kN - 1] = {"mp_anim_idle_watch", "mp_copy_swing_takeover", "mp_anim_requeue_gate", "mp_anim_gait_hold"};
            for (size_t i = 2; i < len && i < 1 + kN; ++i) {
                const uint8_t b = body[i];
                if (b == 255) continue;
                if (sw[i - 2]->exchange(b != 0) != (b != 0)) logf("WO190-CONFIG %s %s", nm[i - 2], b ? "on" : "off");
            }
            out[0] = g_mode.load();
            for (int i = 0; i < kN - 1; ++i) out[1 + i] = sw[i]->load() ? 1 : 0;
            *outLen = kN;
            return wo163::kROk;
        }
        case kOpWeapon: {
            // [op][eid:4] -> [right:i8][left:i8][kind:1][n:1][tags]
            if (len != 5 || cap < 4 + 48) return wo163::kRBadRequest;
            uint32_t eid = 0;
            std::memcpy(&eid, body + 1, 4);
            if (!eid || !engine::entity_by_id(eid)) return wo163::kRNoActor;
            c_weaponAsks.fetch_add(1);
            void* ca = nullptr; void* model = nullptr;
            int right = -1, left = -1;
            const bool ok = motion::combat_parts(eid, false, &ca, &model) && wo166::weapon_classes(ca, &right, &left);
            char tags[48]{};
            const int kind = ok ? hand_tags(right, left, tags, sizeof tags) : 0;
            const size_t tn = std::strlen(tags);
            out[0] = static_cast<uint8_t>(static_cast<int8_t>(right));
            out[1] = static_cast<uint8_t>(static_cast<int8_t>(left));
            out[2] = static_cast<uint8_t>(kind);
            out[3] = static_cast<uint8_t>(tn);
            std::memcpy(out + 4, tags, tn);
            *outLen = 4 + tn;
            return wo163::kROk;
        }
        case kOpClinchRefused: {
            // [op][withinMs:4] -> [n:1]([eid:4][agoMs:4])*n
            if (len != 5 || cap < 1 + 16 * 8) return wo163::kRBadRequest;
            uint32_t within = 0;
            std::memcpy(&within, body + 1, 4);
            c_clinchAsks.fetch_add(1);
            uint32_t eids[16]{}, ago[16]{};
            const int n = wo178::clinch_refused_within(within, eids, ago, 16);
            out[0] = static_cast<uint8_t>(n);
            for (int i = 0; i < n; ++i) { std::memcpy(out + 1 + 8 * i, &eids[i], 4); std::memcpy(out + 5 + 8 * i, &ago[i], 4); }
            *outLen = 1 + 8 * static_cast<size_t>(n);
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

int status_text(char* out, int n) {
    uint32_t lc[8]{};
    motion::loco_counters(lc);
    const int w0 = std::snprintf(out, n,
        "wo190 trace=%s idle_watch=%s swing_takeover=%s requeue_gate=%s gait_hold=%s bodies=%zu lines=%u lines_dropped=%u body_reads=%u read_fail=%u "
        "empty_gaps=%u idle_requeues=%u idle_pulses=%u idle_fail=%u weapon_asks=%u clinch_asks=%u requeue_deduped=%u requeue_limited=%u",
        mode_name(mode_from_byte(g_mode.load())), g_idleWatch.load() ? "on" : "off", g_takeover.load() ? "on" : "off", g_requeueGate.load() ? "on" : "off",
        g_gaitHold.load() ? "on" : "off", g_bodies.size(), c_lines.load(), c_linesDropped.load(), c_bodyReads.load(), c_readFail.load(), c_emptyGaps.load(),
        c_idleRequeues.load(), c_idlePulses.load(), c_idleFail.load(), c_weaponAsks.load(), c_clinchAsks.load(), c_reqDeduped.load(), c_reqLimited.load());
    if (w0 < 0 || w0 >= n) return w0;
    return w0 + std::snprintf(out + w0, n - w0, " loco_fig=%u/%u/%u/%u loco_copy=%u/%u/%u/%u (frames/glide/in_place/class_changes)", lc[0], lc[1], lc[2], lc[3],
                              lc[4], lc[5], lc[6], lc[7]);
}

} // namespace kcdmp::wo190
