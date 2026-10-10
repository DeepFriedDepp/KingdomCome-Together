// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-166: 0.48.2's native half (wo166.h).
#include "wo166.h"
#include "wo163.h"
#include "wo166_rules.h"

#include "engine.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"

#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace kcdmp::wo166 {

namespace {

using namespace kcdmp::wo166rules;

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

constexpr double kDiscardTailS = 1.5;     // the copy stays on the discard list this long after its window (a late contact)
constexpr double kRecentS = 2.0;          // striking_recently's horizon
constexpr size_t kMaxStrikes = 32;
constexpr unsigned kLogFirst = 30;        // the first windows each get a line; then every 50th

struct Strike {
    uint32_t eid = 0;
    std::string name;
    double begin = 0, end = 0, discardUntil = 0, lastEnd = 0;
    motion::AttackFields fields{}, priorFields{};
    int32_t prior = 0;
    bool wrote = false, done = false, midRead = false, discardAdded = false;
    unsigned n = 0;
};

std::vector<Strike> g_strikes;            // main thread only
std::atomic<bool> g_on{true};             // mp_copy_strikes (the agent's switch)
std::atomic<bool> g_faulted{false};       // switched off for the session by a fault
std::atomic<uint8_t> g_autoMode{kAutoAllOff};
std::atomic<uint32_t> c_requests{0}, c_queued{0}, c_wrote{0}, c_refusedState{0}, c_refusedModel{0}, c_refusedFields{0}, c_missed{0},
    c_restored{0}, c_engineMovedOn{0}, c_midStriking{0}, c_midOther{0}, c_playerOpp{0}, c_faults{0}, c_localDropped{0}, c_localDroppedStriking{0},
    c_merged{0};
unsigned g_windowN = 0;
std::string g_faultSite;

uint32_t player_eid() {
    void* e = engine::entity_by_id(0x7777);
    return e ? engine::entity_id(e) : 0;
}

bool name_ok(const char* s, size_t n) {
    if (n == 0 || n > 63) return false;
    for (size_t i = 0; i < n; ++i) {
        const char c = s[i];
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.')) return false;
    }
    return true;
}

bool other_live_strike(uint32_t eid, const Strike* self) {
    for (const auto& s : g_strikes) if (&s != self && s.eid == eid && !s.done) return true;
    return false;
}

void switch_off(const char* site) {
    if (g_faulted.exchange(true)) return;
    g_faultSite = site;
    c_faults.fetch_add(1);
    logf("WO166-STRIKE switched OFF for the session -- a write failed at %s (the copies play their swings as before; the host decides as before)", site);
}

// The prior state and fields back, if the model still reads ours.
void restore(Strike& s, bool closing) {
    void* ca = nullptr; void* model = nullptr;
    if (!motion::combat_parts(s.eid, false, &ca, &model)) { s.done = true; return; }
    int32_t cur = 0;
    const bool readOk = motion::read_state(model, &cur);
    if (readOk && restore_ok(cur, kStateStriking)) {
        if (!motion::write_state(model, s.prior)) switch_off("wo166::restore_state");
        else c_restored.fetch_add(1);
    } else {
        c_engineMovedOn.fetch_add(1);
    }
    if (!motion::write_attack_fields(model, s.priorFields)) switch_off("wo166::restore_fields");
    s.done = true;
    if (s.n <= kLogFirst || s.n % 50 == 0)
        logf("WO166-STRIKE npc=%s window=%u end state_now=0x%X -> %s%s", s.name.c_str(), s.n, readOk ? static_cast<unsigned>(cur) : 0u,
             readOk && restore_ok(cur, kStateStriking) ? "prior state back" : "the engine moved on (left as it is)", closing ? " (session end)" : "");
}

} // namespace

uint8_t auto_mode() { return g_autoMode.load(std::memory_order_relaxed); }

bool striking_recently(uint32_t eid) {
    const double now = now_s();
    for (const auto& s : g_strikes)
        if (s.eid == eid && s.wrote && (!s.done || now - s.lastEnd < kRecentS)) return true;
    return false;
}

void note_local_hit_dropped(uint32_t eid, float hp, float st) {
    c_localDropped.fetch_add(1);
    const bool ours = striking_recently(eid);
    if (ours) c_localDroppedStriking.fetch_add(1);
    logf("WO166-LOCALHIT dropped attacker_eid=0x%X hp -%.2f st -%.2f striking=%d -- the local engine's blow from a copy is put back; the host decides",
         eid, hp, st, ours ? 1 : 0);
}

void tick() {
    if (g_strikes.empty()) return;
    const double now = now_s();
    const bool live = g_on.load() && !g_faulted.load();
    for (auto it = g_strikes.begin(); it != g_strikes.end();) {
        Strike& s = *it;
        if (!live && s.wrote && !s.done) { restore(s, true); s.discardUntil = now; }
        if (!s.wrote && !s.done) {
            if (!live || now > s.end) { c_missed.fetch_add(1); s.done = true; s.discardUntil = now; }
            else if (now >= s.begin) {
                void* ca = nullptr; void* model = nullptr;
                int32_t cur = 0;
                if (!motion::combat_parts(s.eid, false, &ca, &model)) { c_refusedModel.fetch_add(1); s.done = true; s.discardUntil = now; }
                else if (!motion::read_state(model, &cur)) { c_refusedModel.fetch_add(1); s.done = true; s.discardUntil = now; }
                else if (!strike_state_ok(cur)) {
                    c_refusedState.fetch_add(1);
                    if (c_refusedState.load() <= 20) logf("WO166-STRIKE npc=%s refused: the copy's state is 0x%X (an engine reaction of its own: never cut off)", s.name.c_str(), static_cast<unsigned>(cur));
                    s.done = true; s.discardUntil = now;
                } else {
                    motion::AttackFields pf{};
                    motion::read_attack_fields(model, &pf);
                    if (!hits::is_discard_attacker(s.eid)) s.discardAdded = hits::discard_from(s.eid, true);
                    if (!motion::write_attack_fields(model, s.fields)) { switch_off("wo166::write_fields"); s.done = true; s.discardUntil = now; }
                    else if (!motion::write_state(model, kStateStriking)) {
                        motion::write_attack_fields(model, pf);
                        switch_off("wo166::write_state"); s.done = true; s.discardUntil = now;
                    } else {
                        s.prior = cur == kStateStriking ? kStateGuard : cur;   // a copy already ours goes back to Guard
                        s.priorFields = pf;
                        s.wrote = true;
                        c_wrote.fetch_add(1);
                        s.n = ++g_windowN;
                        if (s.n <= kLogFirst || s.n % 50 == 0)
                            logf("WO166-STRIKE npc=%s window=%u begin state 0x%X -> Striking type=%d zone=%d strength=%.2f hand=%d window_ms=%.0f discard=%s",
                                 s.name.c_str(), s.n, static_cast<unsigned>(cur), s.fields.type, s.fields.zone, s.fields.strength, s.fields.hand,
                                 (s.end - now) * 1000.0, s.discardAdded ? "added" : "already");
                    }
                }
            }
        }
        if (s.wrote && !s.done && !s.midRead && now >= (s.begin + s.end) / 2) {
            s.midRead = true;
            motion::ModelRead m{}, pm{};
            const bool ok = motion::read_model(s.eid, &m);
            const uint32_t pe = player_eid();
            const bool pok = pe && motion::read_model(pe, &pm);
            const bool striking = ok && (m.valid & motion::kMvState) && m.state == kStateStriking;
            (striking ? c_midStriking : c_midOther).fetch_add(1);
            const bool oppIsCopy = pok && pm.opponentEid == s.eid;
            if (oppIsCopy) c_playerOpp.fetch_add(1);
            if (s.n <= kLogFirst || s.n % 50 == 0)
                logf("WO166-STRIKE npc=%s window=%u mid state=0x%X at=%d az=%d player_state=0x%X player_opponent=%s", s.name.c_str(), s.n,
                     ok ? static_cast<unsigned>(m.state) : 0u, ok ? m.atkType : -9, ok ? m.atkZone : -9, pok ? static_cast<unsigned>(pm.state) : 0u,
                     oppIsCopy ? "this-copy" : (pok && pm.opponentEid ? "another" : "none"));
        }
        if (s.wrote && !s.done && now >= s.end) {
            restore(s, false);
            s.lastEnd = now;
            s.discardUntil = now + kDiscardTailS;
        }
        if (s.done && now >= s.discardUntil && now - s.lastEnd >= kRecentS) {
            if (s.discardAdded && !other_live_strike(s.eid, &s)) hits::discard_from(s.eid, false);
            it = g_strikes.erase(it);
            continue;
        }
        ++it;
    }
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    auto text = [&](const char* fmt, auto... a) {
        const int n = std::snprintf(reinterpret_cast<char*>(out), cap, fmt, a...);
        *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
    };
    switch (op) {
        case kOpStrike: {
            // [startMs:i16][hitMs:i16][type:i8][zone:i8][hand:i8][strength:f32][nameLen:1][name]
            if (len < 1 + 2 + 2 + 3 + 4 + 1 || cap < 64) return wo163::kRBadRequest;
            int16_t startMs = 0, hitMs = 0; float strength = 0;
            std::memcpy(&startMs, body + 1, 2); std::memcpy(&hitMs, body + 3, 2);
            motion::AttackFields f{};
            f.type = static_cast<int8_t>(body[5]); f.zone = static_cast<int8_t>(body[6]); f.hand = static_cast<int8_t>(body[7]);
            std::memcpy(&strength, body + 8, 4);
            f.strength = strength;
            const size_t n = body[12];
            if (len != 13 + n || !name_ok(reinterpret_cast<const char*>(body + 13), n)) return wo163::kRBadRequest;
            c_requests.fetch_add(1);
            if (!g_on.load()) { text("refused=off"); return wo163::kROk; }
            if (g_faulted.load()) { text("refused=switched-off site=%s", g_faultSite.c_str()); return wo163::kRFailed; }
            if (!attack_fields_ok(f.type, f.zone, f.strength, f.hand)) { c_refusedFields.fetch_add(1); text("refused=fields"); return wo163::kROk; }
            const std::string name(reinterpret_cast<const char*>(body + 13), n);
            const uint32_t eid = hits::eid_of_name(name.c_str());
            if (!eid) { text("refused=no-entity"); return wo163::kRNoActor; }
            const double now = now_s();
            const StrikeWindow w = strike_window(now * 1000.0, startMs, hitMs);
            if (!w.valid) { text("refused=timing"); return wo163::kROk; }
            // a swing on a copy whose window is still open: the window runs on with the new row's fields
            for (auto& s : g_strikes) {
                if (s.eid == eid && !s.done) {
                    s.end = (std::max)(s.end, w.end_ms / 1000.0);
                    s.fields = f;
                    if (s.wrote) {
                        void* ca = nullptr; void* model = nullptr;
                        if (motion::combat_parts(eid, false, &ca, &model) && !motion::write_attack_fields(model, f)) switch_off("wo166::merge_fields");
                    }
                    c_merged.fetch_add(1);
                    text("merged window_end_ms=%.0f", (s.end - now) * 1000.0);
                    return wo163::kROk;
                }
            }
            if (g_strikes.size() >= kMaxStrikes) { text("refused=full"); return wo163::kROk; }
            Strike s;
            s.eid = eid; s.name = name; s.fields = f;
            s.begin = w.begin_ms / 1000.0; s.end = w.end_ms / 1000.0;
            g_strikes.push_back(s);
            c_queued.fetch_add(1);
            text("queued window_ms=%.0f..%.0f", (s.begin - now) * 1000.0, (s.end - now) * 1000.0);
            return wo163::kROk;
        }
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
            return wo163::kROk;
        }
        case kOpConfig: {
            if ((len != 3 && len != 4) || cap < 3) return wo163::kRBadRequest;
            if (len == 4 && body[3] != 255) npcdrive::set_snap_fix(body[3] != 0);   // C4
            const bool was = g_on.exchange(body[1] != 0);
            if (was != g_on.load()) logf("WO166-CFG mp_copy_strikes %s", g_on.load() ? "on" : "off");
            if (body[2] != 255 && body[2] <= kAutoMax) {
                const uint8_t old = g_autoMode.exchange(body[2]);
                if (old != body[2]) logf("WO166-CFG figure automation mode %u -> %u (0 all off, 1 all on, 2+ the lever's byte patterns)", old, body[2]);
            }
            out[0] = g_on.load() ? 1 : 0; out[1] = g_autoMode.load(); out[2] = npcdrive::snap_fix() ? 1 : 0;
            *outLen = 3;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

int status_text(char* out, int n) {
    return std::snprintf(out, n,
        "wo166 copy_strikes=%s faulted=%s requests=%u queued=%u merged=%u wrote=%u refused_state=%u refused_model=%u refused_fields=%u missed=%u "
        "restored=%u engine_moved_on=%u mid_striking=%u mid_other=%u player_opponent_copy=%u faults=%u local_dropped=%u local_dropped_striking=%u "
        "active=%zu auto_mode=%u snap_fix=%s",
        g_on.load() ? "on" : "off", g_faulted.load() ? "yes" : "no", c_requests.load(), c_queued.load(), c_merged.load(), c_wrote.load(),
        c_refusedState.load(), c_refusedModel.load(), c_refusedFields.load(), c_missed.load(), c_restored.load(), c_engineMovedOn.load(),
        c_midStriking.load(), c_midOther.load(), c_playerOpp.load(), c_faults.load(), c_localDropped.load(), c_localDroppedStriking.load(),
        g_strikes.size(), g_autoMode.load(), npcdrive::snap_fix() ? "on" : "off");
}

void on_pipe_closed() {
    // main thread is not guaranteed here: the windows end at the next tick (live=false restores them)
    g_on = false;
}

} // namespace kcdmp::wo166
