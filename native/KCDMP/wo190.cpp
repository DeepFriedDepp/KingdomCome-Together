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
#include "gait_logic.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "npc_scan.h"

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
bool g_envTrace = false;   // WO-201 follow-up (research): KCDMP_ANIM_TRACE=on keeps the full trace on whatever the agent says

std::atomic<uint32_t> c_lines{0}, c_linesDropped{0}, c_bodyReads{0}, c_readFail{0}, c_idleRequeues{0}, c_idlePulses{0}, c_idleFail{0},
    c_emptyGaps{0}, c_weaponAsks{0}, c_clinchAsks{0}, c_reqDeduped{0}, c_reqLimited{0}, c_evidence{0};

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
    // WO-192 4d: the evidence window outside fights (auto)
    Evidence ev;
    double probeAt = 0;
    float hystCls = -1;
    bool evEmptyArmed = false;
    // WO-201 follow-up (mp_anim_trace on): the per-second locomotion snapshot
    double locoAt = 0; float locoPos[3]{}; bool locoHave = false;
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
                // WO-197 B0: the combat director's running actions on the full-body line (scope 0 main / 1 movement / 4 recoil)
                char dirs[120] = "";
                void* dca = nullptr; void* dmodel = nullptr;
                if (i == 0 && motion::combat_parts(c.eid, false, &dca, &dmodel)) {
                    const std::string d0 = wo187::director_text(dca, 0), d1 = wo187::director_text(dca, 1), d4 = wo187::director_text(dca, 4);
                    std::snprintf(dirs, sizeof dirs, " dir=%s/%s/%s", d0.empty() ? "?" : d0.c_str(), d1.empty() ? "?" : d1.c_str(), d4.empty() ? "?" : d4.c_str());
                }
                c_lines.fetch_add(1);
                logf("WO190-ANIM utc=%llu body=%s role=%s scope=%s action=%s frag=%u prio=%d flags=0x%X was=%s:%u held_ms=%.0f tags=%s spd=%.2f age_ms=%.0f fight=%d%s",
                     static_cast<unsigned long long>(utc_ms()), name_of(c.eid), role_name(c.role), sc[i].name, now_.empty ? "none" : sc[i].action,
                     now_.empty ? 0u : now_.frag, now_.empty ? 0 : now_.prio, now_.empty ? 0u : now_.flags, first ? "-" : (st.snap.empty ? "none" : st.action),
                     first || st.snap.empty ? 0u : st.snap.frag, heldMs, i == 0 ? tags : "-", c.speed, c.age < 0 ? -1.0 : c.age * 1000.0, c.fight ? 1 : 0,
                     dirs);
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

// WO-192 4d: in auto, a body that does not fight is still probed (its full body only, 4 Hz); a full body empty 0.5 s, or a gait class that
// flipped 3 times in 2 s, opens a 4 s window in which the body is traced like a fighting one (the evidence of a T-pose while running).
void evidence_probe(const Cand& c, BodyTrace& bt, double now) {
    if (now - bt.probeAt < kEvidenceProbePeriodS) return;
    bt.probeAt = now;
    trace_body(c, bt, now, Mode::Auto, false);   // scope 0 only: fullEmptySince
    const char* why = nullptr;
    const double emptyFor = bt.fullEmptySince < 0 ? -1.0 : now - bt.fullEmptySince;
    if (emptyFor < 0) bt.evEmptyArmed = false;
    else if (!bt.evEmptyArmed && Evidence::empty_due(emptyFor)) { bt.evEmptyArmed = true; why = "its full body stands empty"; }
    if (c.role != Role::Player) {
        bt.hystCls = gait::speed_class_hyst(c.speed, bt.hystCls);
        if (bt.ev.gait(bt.hystCls, now) && !why) why = "its gait class flips";
    }
    if (!why) return;
    const bool was = bt.ev.on(now);
    bt.ev.arm(now);
    if (was) return;
    const uint32_t k = c_evidence.fetch_add(1) + 1;
    if (k <= kLogFirst || k % 50 == 0)
        logf("WO190-EVIDENCE body=%s role=%s why=%s empty_ms=%.0f spd=%.2f cls=%.0f -> traced %.0f s (#%u)", name_of(c.eid), role_name(c.role), why,
             emptyFor < 0 ? -1.0 : emptyFor * 1000.0, c.speed, bt.hystCls, kEvidenceHoldS, k);
}

// WO-201 follow-up (mp_anim_trace on, read-only): once a second per traced body -- its full-body action, its ground speed over that
// second, the animated character's desired motion parameters and the requested velocity (WO190-LOCO)
void loco_line(const Cand& c, BodyTrace& bt, double now) {
    if (now - bt.locoAt < 1.0) return;
    void* e = engine::entity_by_id(c.eid);
    float p[3]{};
    if (!e || !engine::entity_world_pos(e, p)) return;
    const double dt = now - bt.locoAt;
    const float sp = bt.locoHave && dt < 2.0 ? static_cast<float>(std::sqrt((p[0] - bt.locoPos[0]) * (p[0] - bt.locoPos[0]) + (p[1] - bt.locoPos[1]) * (p[1] - bt.locoPos[1])) / dt) : -1.0f;
    bt.locoAt = now; bt.locoHave = true;
    std::memcpy(bt.locoPos, p, sizeof p);
    wo187::ScopeInfo sc[1];
    const int n = wo187::read_all_scopes(c.eid, sc, 1);
    wo187::MotionParams mp;
    const bool ok = wo187::motion_params(c.eid, &mp);
    char m[160] = "-";
    if (ok) {
        size_t o = 0;
        for (int i = 0; i < mp.count && o + 16 < sizeof m; ++i) o += std::snprintf(m + o, sizeof m - o, "%s%u:%.2f", i ? "," : "", mp.id[i], mp.value[i]);
        if (!mp.count) std::snprintf(m, sizeof m, "none");
    }
    char sp4[96] = "";   // the skeleton's own desired parameters: travel speed, turn speed, travel angle, turn angle
    {
        size_t o = 0;
        const uint32_t ids[4] = {0, 1, 2, 4};
        for (uint32_t id : ids) { float v = 0; if (motion::skel_desired_param(e, id, &v) && o + 14 < sizeof sp4) o += std::snprintf(sp4 + o, sizeof sp4 - o, "%s%u:%.2f", o ? "," : "", id, v); }
        if (!o) o = std::snprintf(sp4, sizeof sp4, "none");
        float ps = -1;
        if (motion::skel_layer_scale(e, 0, &ps) && o + 12 < sizeof sp4) std::snprintf(sp4 + o, sizeof sp4 - o, " ps0=%.2f", ps);
        float pseudo = -1; int kind = -1, cnt = -1;
        const size_t o2 = std::strlen(sp4);
        if (motion::pseudo_speed(c.eid, &pseudo, &kind, &cnt) && o2 + 32 < sizeof sp4) std::snprintf(sp4 + o2, sizeof sp4 - o2, " pseudo=%.2f kind=%d n=%d", pseudo, kind, cnt);
    }
    if (!g_minute.take(now, kTraceLinesPerMin)) return;
    logf("WO190-LOCO body=%s role=%s full=%s:%u ground=%.2f wspd=%.2f mp=%s sk=%s rv=%.2f,%.2f dist=%.0f", name_of(c.eid), role_name(c.role),
         n > 0 && !sc[0].empty ? sc[0].action : "none", n > 0 && !sc[0].empty ? sc[0].frag : 0u, sp, c.speed, m, sp4, ok ? mp.reqVel[0] : 0.0f,
         ok ? mp.reqVel[1] : 0.0f, c.dist);
}

// WO-201 follow-up (research): KCDMP_FIFO_NAMES=a,b in the game's environment -- those bodies' layer-0 animation FIFO, every read for
// 2 s out of every 15 (WO190-FIFO)
void fifo_lines(const Cand& c, double now) {
    static std::string s_names; static bool s_read = false;
    if (!s_read) { s_read = true; char v[256]{}; size_t got = 0; if (getenv_s(&got, v, sizeof v, "KCDMP_FIFO_NAMES") == 0 && got) s_names = std::string(",") + v + ","; }
    static int s_all = -1;
    if (s_all < 0) { char v[4]{}; size_t got = 0; s_all = (getenv_s(&got, v, sizeof v, "KCDMP_FIFO_ALL") == 0 && got && v[0] == '1') ? 1 : 0; }
    if (s_names.empty() || (!s_all && std::fmod(now, 15.0) > 2.0)) return;
    const std::string key = std::string(",") + name_of(c.eid) + ",";
    if (s_names.find(key) == std::string::npos) return;
    void* e = engine::entity_by_id(c.eid);
    float p[3]{};
    if (!e || !engine::entity_world_pos(e, p)) return;
    char t[1800];
    if (motion::skel_fifo_text(e, 0, t, sizeof t) < 0) return;
    logf("WO190-FIFO body=%s role=%s t=%.4f pos=%.3f,%.3f %s", name_of(c.eid), role_name(c.role), now, p[0], p[1], t);
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
    // WO-201 follow-up: with the full trace on, the game's own NPCs near the player too (role npc) -- the reference a copy is compared with
    if (havePp && mode_from_byte(g_mode.load()) == Mode::On) {
        struct Ctx { std::vector<Cand>* out; const float* pp; };
        Ctx cx{&out, pp};
        const npcscan::Anchor a{pp[0], pp[1], pp[2]};
        uint32_t walked = 0; uint8_t refuse = 0;
        npcscan::for_each_in_radius(&a, 1, kNearM, [](void* e, const char*, float x, float y, float z, bool isHorse, void* c) {
            if (isHorse) return;
            auto* k = static_cast<Ctx*>(c);
            const uint32_t eid = engine::entity_id(e);
            for (const Cand& q : *k->out) if (q.eid == eid) return;
            const float d = std::sqrt((x - k->pp[0]) * (x - k->pp[0]) + (y - k->pp[1]) * (y - k->pp[1]) + (z - k->pp[2]) * (z - k->pp[2]));
            k->out->push_back({eid, Role::Npc, d, 0.0f, -1.0, false});
        }, &cx, &walked, &refuse);
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
    static bool s_env = false;   // WO-201 follow-up (research): KCDMP_ANIM_TRACE=on in the game's environment starts the full trace (no agent needed)
    if (!s_env) {
        s_env = true;
        char v[8]{}; size_t got = 0;
        if (getenv_s(&got, v, sizeof v, "KCDMP_ANIM_TRACE") == 0 && got && std::strcmp(v, "on") == 0) {
            g_mode = static_cast<uint8_t>(Mode::On);
            g_envTrace = true;
            logf("WO190-CONFIG trace on (KCDMP_ANIM_TRACE in the environment)");
        }
    }
    const Mode mode = mode_from_byte(g_mode.load());
    const bool watch = g_idleWatch.load();
    if (mode == Mode::Off && !watch) return;
    const double now = now_s();
    static double s_lastGather = 0;
    static std::vector<Cand> s_cands;
    if (now - s_lastGather >= 0.25) { s_lastGather = now; gather(s_cands, now); }
    const double period = mode == Mode::On ? kReadPeriodS : (mode == Mode::Auto ? kAutoReadPeriodS : kIdleWatchPeriodS);
    for (const Cand& c : s_cands) {
        if (mode == Mode::Auto && !c.fight) {
            BodyTrace& pb = g_bodies[c.eid];
            pb.role = c.role;
            evidence_probe(c, pb, now);
        }
        bool traced = mode == Mode::On || (mode == Mode::Auto && c.fight);
        if (!traced && mode == Mode::Auto) { auto it = g_bodies.find(c.eid); traced = it != g_bodies.end() && it->second.ev.on(now); }
        const bool watched = watch && c.fight && c.role != Role::Player;
        if (!traced && !watched) continue;
        BodyTrace& bt = g_bodies[c.eid];
        bt.role = c.role;
        if (now - bt.readAt < period) continue;
        bt.readAt = now;
        trace_body(c, bt, now, mode, traced);
        if (mode == Mode::On) loco_line(c, bt, now);
        if (mode == Mode::On) fifo_lines(c, now);
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
            if (len >= 2 && body[1] != 255 && !g_envTrace) {
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
        "empty_gaps=%u idle_requeues=%u idle_pulses=%u idle_fail=%u weapon_asks=%u clinch_asks=%u requeue_deduped=%u requeue_limited=%u evidence=%u",
        mode_name(mode_from_byte(g_mode.load())), g_idleWatch.load() ? "on" : "off", g_takeover.load() ? "on" : "off", g_requeueGate.load() ? "on" : "off",
        g_gaitHold.load() ? "on" : "off", g_bodies.size(), c_lines.load(), c_linesDropped.load(), c_bodyReads.load(), c_readFail.load(), c_emptyGaps.load(),
        c_idleRequeues.load(), c_idlePulses.load(), c_idleFail.load(), c_weaponAsks.load(), c_clinchAsks.load(), c_reqDeduped.load(), c_reqLimited.load(), c_evidence.load());
    if (w0 < 0 || w0 >= n) return w0;
    return w0 + std::snprintf(out + w0, n - w0, " loco_fig=%u/%u/%u/%u loco_copy=%u/%u/%u/%u (frames/glide/in_place/class_changes)", lc[0], lc[1], lc[2], lc[3],
                              lc[4], lc[5], lc[6], lc[7]);
}

} // namespace kcdmp::wo190
