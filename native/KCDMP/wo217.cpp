// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-217: the world around the bodies -- the native half's switch word, status, A.0's attempt lines and 0.2's seated-copy rule (wo217.h).
#include "wo217.h"
#include "wo203.h"
#include "wo163.h"
#include "wo208.h"
#include "wo141.h"
#include "engine.h"
#include "log.h"
#include "npc_drive.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>

namespace kcdmp::wo217 {

namespace {

using namespace wo217rules;

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};
std::atomic<uint8_t> g_role{0};

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W217_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO217-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W217_OFF=%s)", s, b);
    }
    return s;
}

std::atomic<uint32_t> c_out[static_cast<int>(Outcome::kCount)]{};
std::atomic<uint32_t> c_out2s[static_cast<int>(Outcome::kCount)]{};   // attempts the host held 2 s or more
std::atomic<uint32_t> c_shown1s{0}, c_lines{0};
std::atomic<uint32_t> c_seatHolds{0}, c_seatPlaces{0}, c_seatPulls{0}, c_seatReleases{0};

} // namespace
uint8_t role() { return g_role.load(); }
namespace {

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if ((len != 5 && len != 6) || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        if (len == 6) {
            const uint8_t was = g_role.exchange(body[5]);
            if (was != body[5]) logf("WO217-CFG role %u -> %u (0 none, 1 the host, 2 a joiner in the host's world)", was, body[5]);
        }
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true))
            logf("WO217-CFG bits=0x%X (was 0x%X; twin off 0x%X): seat_writer=%d placement=%d ghost_far=%d kd_follow=%d long_names=%d layer_watch=%d "
                 "loot_live=%d item_flags=%d encounter_world=%d carts=%d", bits, was, twin_off(), on(kBitSeatWriter), on(kBitPlacement), on(kBitGhostFar),
                 on(kBitKdFollow), on(kBitLongNames), on(kBitLayerWatch), on(kBitLootLive), on(kBitItemFlags), on(kBitEncounter), on(kBitCarts));
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[1600];
        int n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) >= sizeof t) n = static_cast<int>(sizeof t) - 1;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

double g_lastLine = 0;
void tick_reg() {
    const double now = npcdrive::now_s();
    if (now - g_lastLine >= 30.0) {
        g_lastLine = now;
        char t[1600];
        if (status(t, sizeof t) > 0 && g_configured.load()) logf("WO217-STATUS %s", t);
    }
}
int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_tick("w217", &tick_reg) &&
                  wo203::reg_status("w217", &status_reg);

void copy_text(char* dst, size_t n, const char* src) {
    if (!dst || !n) return;
    dst[0] = 0;
    if (src) { std::strncpy(dst, src, n - 1); dst[n - 1] = 0; }
}

} // namespace

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }
bool configured() { return g_configured.load(std::memory_order_relaxed); }

// ---------------------------------------------------------------------------------------------------- A.0: one line per attempt
void place_note(PlaceEp& ep, Note n, const char* detail) {
    if (!ep.open) return;
    Counts& c = ep.c;
    switch (n) {
        case Note::InstallOk: ++c.installs; break;
        case Note::Refused: ++c.refused; if (detail && !ep.refuseWhy[0]) copy_text(ep.refuseWhy, sizeof ep.refuseWhy, detail); break;
        case Note::Stale: ++c.stale; if (detail) copy_text(ep.staleHead, sizeof ep.staleHead, detail); break;
        case Note::Deferred: ++c.deferred; break;
        case Note::QueueFull: ++c.queueFull; if (detail && !ep.staleHead[0]) copy_text(ep.staleHead, sizeof ep.staleHead, detail); break;
        case Note::Backoff: ++c.backoff; break;
        case Note::Held: ++c.held; if (detail && !ep.refuseWhy[0]) copy_text(ep.refuseWhy, sizeof ep.refuseWhy, detail); break;
        case Note::RefusedHere: ++c.refusedHere; break;
    }
}

namespace {
void close_line(uint32_t eid, const char* name, PlaceEp& ep, double now, const char* why) {
    if (!ep.open) return;
    ep.open = false;
    const double held = now - ep.t0;
    if (ep.lostAt >= 0) ep.lostFor += now - ep.lostAt;
    const double shownAfter = ep.shownAt >= 0 ? ep.shownAt - ep.t0 : -1.0;
    const Outcome o = judge(held, shownAfter, ep.lostAt >= 0 ? now - ep.lostAt : 0.0, ep.c);
    c_out[static_cast<int>(o)].fetch_add(1, std::memory_order_relaxed);
    if (held >= 2.0) c_out2s[static_cast<int>(o)].fetch_add(1, std::memory_order_relaxed);
    if (shownAfter >= 0 && shownAfter <= 1.0 && held >= 2.0) c_shown1s.fetch_add(1, std::memory_order_relaxed);
    char frag[48] = "?";
    wo208::fragment_name_of(eid, ep.frag, frag, sizeof frag);
    const uint32_t k = c_lines.fetch_add(1, std::memory_order_relaxed) + 1;
    if (k <= 6000 || k % 50 == 0)
        logf("WO217-PLACE npc=%s fam=%s frag=%u(%s) held_s=%.1f outcome=%s shown_ms=%.0f lost_ms=%.0f losses=%u installs=%u refused=%u(%s) stale=%u(%s) "
             "deferred=%u queue_full=%u backoff=%u held=%u refused_here=%u path=%d neutral=%d dist_m=%.1f obj_m=%.1f lost_to=%s act=[%s] closed=%s [n=%u]",
             name ? name : "?", wo208::pfam_text(ep.fam), ep.frag, frag, held, outcome_name(o), shownAfter >= 0 ? shownAfter * 1000.0 : -1.0,
             ep.lostFor * 1000.0, ep.losses, ep.c.installs, ep.c.refused, ep.refuseWhy[0] ? ep.refuseWhy : "-", ep.c.stale, ep.staleHead[0] ? ep.staleHead : "-",
             ep.c.deferred, ep.c.queueFull, ep.c.backoff, ep.c.held, ep.c.refusedHere, ep.path, ep.neutral ? 1 : 0, ep.dist, ep.objDist,
             ep.lostTo[0] ? ep.lostTo : "-", ep.act, why, k);
}
} // namespace

void place_close(uint32_t eid, const char* name, PlaceEp& ep, double now, const char* why) { close_line(eid, name, ep, now, why); }

void place_tick(uint32_t eid, const char* name, PlaceEp& ep, bool haveHost, uint64_t hostAct, uint32_t hostFrag, uint8_t hostFam,
                uint32_t copyFrag, uint8_t copyFam, const char* copyCls, bool copyOurs, double now) {
    if (haveHost && hostAct != ep.hostAct) {
        close_line(eid, name, ep, now, "the host's next action");
        ep.hostAct = hostAct;
        if (wo208::pfam_is_posture(hostFam)) {
            ep = PlaceEp{};
            ep.open = true; ep.hostAct = hostAct; ep.frag = hostFrag; ep.fam = hostFam; ep.t0 = now;
            wo208::place_context(eid, name, ep.act, sizeof ep.act, &ep.path, &ep.neutral, &ep.dist, &ep.objDist);
        }
    }
    if (!ep.open) return;
    const bool shown = shows(copyFrag, ep.frag, copyFam, ep.fam, true);
    if (shown) {
        if (ep.shownAt < 0) ep.shownAt = now;
        if (ep.lostAt >= 0) { ep.lostFor += now - ep.lostAt; ep.lostAt = -1; }
    } else if (ep.shownAt >= 0 && ep.lostAt < 0) {
        ep.lostAt = now;
        ++ep.losses;
        char frag[40] = "?";
        wo208::fragment_name_of(eid, copyFrag, frag, sizeof frag);
        std::snprintf(ep.lostTo, sizeof ep.lostTo, "%s:%s:%s", copyCls ? copyCls : "-", frag, copyOurs ? "ours" : "its-own");
    }
    if (now - ep.t0 >= kMaxS) close_line(eid, name, ep, now, "30 s");
}

// ---------------------------------------------------------------------------------------------------- 0.2: one writer for a seat
bool seat_holds(uint32_t eid, const char* name, float offM, bool* placeOnce) {
    *placeOnce = false;
    bool hostSeated = false, copySeated = false;
    const bool owned = wo208::seat_state(eid, &hostSeated, &copySeated);
    // the copy's own NPC-state seat, read twice a second per body
    struct Obj { double at = -1e9; bool seated = false; };
    static std::unordered_map<uint32_t, Obj> s_obj;   // main thread
    const double now0 = npcdrive::now_s();
    Obj& ob = s_obj[eid];
    if (now0 - ob.at >= 0.5) { ob.at = now0; ob.seated = wo141::object_stance_now(eid); }
    if (!owned && !ob.seated) return false;   // the owner's word (the host's line, the copy's full body) or the copy's own seat
    const Seat s = seat_rule(on(kBitSeatWriter), hostSeated, ob.seated, copySeated, offM);
    // a release holds the copy still at most kReleaseHoldS: one that has not stood up by then is the stream's again (a failed release never
    // freezes a copy while its host NPC walks away)
    static std::unordered_map<uint32_t, double> s_relSince;   // main thread
    if (s != Seat::Release) s_relSince.erase(eid);
    if (s == Seat::NotSeated) return false;
    if (s == Seat::Release) {
        constexpr double kReleaseHoldS = 4.0;
        auto rs = s_relSince.emplace(eid, now0).first;
        if (now0 - rs->second > kReleaseHoldS) return false;
        if (wo208::ask_release(eid, name)) {
            const uint32_t k = c_seatReleases.fetch_add(1, std::memory_order_relaxed) + 1;
            if (k <= 200 || k % 50 == 0)
                logf("WO217-SEAT npc=%s released from its own seat (its host NPC %s, %.1f m off) -- the game's own release; held still until it is [n=%u]",
                     name ? name : "?", hostSeated ? "sits elsewhere" : "is not seated", offM, k);
        }
        c_seatHolds.fetch_add(1, std::memory_order_relaxed);
        return true;
    }
    if (s == Seat::PlaceOnce) {
        static std::unordered_map<uint32_t, double> s_placedAt;   // main thread: one write per body per kSeatPlaceGapS, else held as it is
        // a placement that does not stick (a2-b10: the copy's sit snapped it back to its own seat 6 m off, every 5 s -- two pulls each time):
        // after two, the body is held where it sits for 60 s
        struct Tries { int n = 0; double firstAt = -1e9; };
        static std::unordered_map<uint32_t, Tries> s_tries;
        const double now = npcdrive::now_s();
        double& at = s_placedAt[eid];
        if (now - at < 5.0) { c_seatHolds.fetch_add(1, std::memory_order_relaxed); return true; }
        Tries& tr = s_tries[eid];
        if (now - tr.firstAt > 60.0) { tr.n = 0; tr.firstAt = now; }
        if (tr.n >= 2) {
            if (tr.n == 2) {
                ++tr.n;
                logf("WO217-SEAT npc=%s placed twice and its sit took it back (%.1f m off): held where it sits for 60 s", name ? name : "?", offM);
            }
            c_seatHolds.fetch_add(1, std::memory_order_relaxed);
            return true;
        }
        ++tr.n;
        at = now;
        *placeOnce = true;
        const uint32_t k = c_seatPlaces.fetch_add(1, std::memory_order_relaxed) + 1;
        if (k <= 200 || k % 50 == 0)
            logf("WO217-SEAT npc=%s placed once: %.2f m from its host NPC's seat (over %.1f m) -- one write, then the seat holds it [n=%u]", name ? name : "?", offM,
                 kSeatFarM, k);
        return false;
    }
    c_seatHolds.fetch_add(1, std::memory_order_relaxed);
    return true;
}

void seat_note_pull(uint32_t eid, const char* name, float cm, const char* writer) {
    (void)eid;
    const uint32_t k = c_seatPulls.fetch_add(1, std::memory_order_relaxed) + 1;
    if (k <= 400 || k % 50 == 0) logf("WO217-SEATPULL npc=%s cm=%.0f %s [n=%u]", name ? name : "?", cm, writer ? writer : "-", k);
}

int status(char* out, int n) {
    if (!out || n <= 0) return 0;
    int k = std::snprintf(out, static_cast<size_t>(n), "w217 bits=0x%X twin_off=0x%X configured=%d role=%u | A.0 attempts", g_bits.load(), twin_off(),
                          g_configured.load() ? 1 : 0, g_role.load());
    for (int i = 0; i < static_cast<int>(Outcome::kCount) && k < n - 40; ++i)
        k += std::snprintf(out + k, static_cast<size_t>(n - k), " %s=%u/%u", outcome_name(static_cast<Outcome>(i)), c_out2s[i].load(), c_out[i].load());
    if (k < n - 120)
        k += std::snprintf(out + k, static_cast<size_t>(n - k), " shown_1s_of_2s=%u | 0.2 seat_holds=%u seat_places=%u seat_releases=%u seat_pulls=%u",
                           c_shown1s.load(), c_seatHolds.load(), c_seatPlaces.load(), c_seatReleases.load(), c_seatPulls.load());
    return k < n ? k : n - 1;
}

} // namespace kcdmp::wo217
