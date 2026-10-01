// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-132 -- see wo132.h.
#include "wo132.h"

#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <vector>

#include "engine.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "rttr_abi.h"
#include "wo136.h"

namespace kcdmp::wo132 {
namespace {

uint32_t get_u32(const uint8_t* p) { uint32_t v; std::memcpy(&v, p, 4); return v; }

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

uint32_t player_eid() {
    void* e = engine::entity_by_id(0x7777);
    return e ? engine::entity_id(e) : 0;
}

// Host: the watched NPCs and what went out last.
struct Watched { uint32_t eid; motion::NpcCombat last{}; bool sent = false; double lastSent = 0; };
std::vector<Watched> g_watch;          // main thread only
constexpr size_t kMaxWatch = 48;
double g_lastSample = 0;
std::atomic<CombatFn> g_combatFn{nullptr};

// Live checks: test NPCs held in a fight (main thread).
struct TestFight { uint32_t eid; double until; double last = 0; };
std::vector<TestFight> g_testFights;

// Joiner: the engaged copies (main thread).
std::vector<uint32_t> g_engaged;
bool is_engaged(uint32_t eid) { for (uint32_t e : g_engaged) if (e == eid) return true; return false; }

constexpr float kEngageRangeM = 15.0f;
std::atomic<uint32_t> c_far{0};
std::atomic<uint32_t> c_leave{0}, c_leaveFail{0}, c_engageOn{0}, c_engageOff{0}, c_engageSkirmish{0}, c_watchOut{0}, c_fail{0};

uint8_t wire_zone(int8_t v) { return v < 0 ? 0xFF : static_cast<uint8_t>(v); }

void emit(uint32_t eid, const motion::NpcCombat& c) {
    CombatFn fn = g_combatFn.load();
    if (!fn) return;
    uint8_t b[16 + 64]{};
    std::memcpy(b, &eid, 4);
    b[4] = c.combat; b[5] = wire_zone(c.guardZone); b[6] = wire_zone(c.guardStance); b[7] = wire_zone(c.atkZone);
    b[8] = c.block; b[9] = c.opponentIsPlayer;
    std::memcpy(b + 10, &c.opponentEid, 4);
    const char* n = nullptr;
    if (void* e = engine::entity_by_id(eid)) n = engine::entity_name(e);
    size_t len = 0;
    if (n) { __try { while (len < 63 && n[len]) { b[15 + len] = static_cast<uint8_t>(n[len]); ++len; } } __except (EXCEPTION_EXECUTE_HANDLER) { len = 0; } }
    b[14] = static_cast<uint8_t>(len);
    fn(b, static_cast<uint16_t>(15 + len));
    c_watchOut.fetch_add(1);
}

bool same(const motion::NpcCombat& a, const motion::NpcCombat& b) {
    return a.combat == b.combat && a.guardZone == b.guardZone && a.guardStance == b.guardStance && a.atkZone == b.atkZone
        && a.block == b.block && a.opponentIsPlayer == b.opponentIsPlayer && a.opponentEid == b.opponentEid;
}

void put_read(uint8_t* out, const motion::NpcCombat& c) {
    out[0] = c.hasCa; out[1] = c.combat; out[2] = wire_zone(c.guardZone); out[3] = wire_zone(c.guardStance);
    out[4] = wire_zone(c.atkZone); out[5] = c.block; out[6] = c.opponentIsPlayer; out[7] = 0;
    std::memcpy(out + 8, &c.opponentEid, 4);
}

} // namespace

void set_combat_callback(CombatFn fn) { g_combatFn.store(fn); }

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 1 || cap < 32) return kRBadRequest;
    const uint8_t op = body[0];
    const uint8_t* a = body + 1;
    const size_t n = len - 1;
    switch (op) {
    case kOpLeaveFight: {
        if (n != 4) return kRBadRequest;
        uint32_t eid = get_u32(a);
        const bool isPlayer = eid == 0;
        if (isPlayer) eid = player_eid();
        if (!hits::skirmish_ready()) { c_leaveFail.fetch_add(1); return kRUnarmed; }
        void* soul = hits::soul_of_eid(eid);
        if (!soul) { c_leaveFail.fetch_add(1); return kRNoActor; }
        uint64_t rv = 0;
        const bool ok = hits::skirmish_remove(soul, &rv);
        logf("WO132-LEAVEFIGHT eid=0x%X (%s) -> %s rv=0x%llX -- this body leaves its skirmish, the fight goes on",
             eid, isPlayer ? "the local player" : "a body", ok ? "removed" : "FAILED", static_cast<unsigned long long>(rv));
        if (!ok) { c_leaveFail.fetch_add(1); return kRFailed; }
        c_leave.fetch_add(1);
        out[0] = static_cast<uint8_t>(rv & 0xFF);
        *outLen = 1;
        return kROk;
    }
    case kOpEngage: {
        if (n != 1 + 4 + sizeof(motion::State2)) return kRBadRequest;
        bool on = a[0] != 0;
        const uint32_t eid = get_u32(a + 1);
        motion::State2 st{};
        std::memcpy(&st, a + 5, sizeof st);
        const double now = now_s();
        void* soul = hits::soul_of_eid(eid);
        if (!soul) { c_fail.fetch_add(1); return kRNoActor; }
        uint8_t first = 0, sk = 0;
        // Only a copy near this player engages it (15 m); a farther one is let go.
        float dist = -1;
        {
            float pp[3]{}, cp[3]{};
            void* pe = engine::entity_by_id(player_eid());
            void* ce = engine::entity_by_id(eid);
            if (pe && ce && npcdrive::entity_pos(pe, pp) && npcdrive::entity_pos(ce, cp)) {
                const float dx = pp[0] - cp[0], dy = pp[1] - cp[1], dz = pp[2] - cp[2];
                dist = std::sqrt(dx * dx + dy * dy + dz * dz);
            }
        }
        bool tooFar = on && (dist < 0 || dist > kEngageRangeM);
        if (tooFar) { on = false; c_far.fetch_add(1); }
        if (on) {
            if (!is_engaged(eid)) {
                first = 1;
                void* psoul = hits::soul_of_eid(player_eid());
                uint64_t rv = 0;
                if (psoul && hits::skirmish_add(soul, psoul, 1, &rv)) { sk = 1; c_engageSkirmish.fetch_add(1); }
                hits::discard_from(eid, true);
                g_engaged.push_back(eid);
                c_engageOn.fetch_add(1);
                logf("WO132-ENGAGE eid=0x%X on: skirmish vs the local player %s (rv=0x%llX); its local hits on the player are discarded",
                     eid, sk ? "added" : "NOT added", static_cast<unsigned long long>(rv));
            }
            motion::set_npc_engage(eid, true, &st, now);
        } else {
            motion::set_npc_engage(eid, false, nullptr, now);
            if (is_engaged(eid)) {
                uint64_t rv = 0;
                sk = hits::skirmish_remove(soul, &rv) ? 1 : 0;
                hits::discard_from(eid, false);
                for (auto it = g_engaged.begin(); it != g_engaged.end(); ++it) if (*it == eid) { g_engaged.erase(it); break; }
                c_engageOff.fetch_add(1);
                logf("WO132-ENGAGE eid=0x%X off: combat hold released, skirmish %s", eid, sk ? "left" : "NOT left");
            }
        }
        out[0] = first; out[1] = sk;
        std::memcpy(out + 2, &dist, 4);
        *outLen = 6;
        return tooFar ? kRFar : kROk;
    }
    case kOpWatch: {
        if (n < 6 || n != static_cast<size_t>(6 + a[5]) || a[5] > 63) return kRBadRequest;
        const bool on = a[0] != 0;
        uint32_t eid = get_u32(a + 1);
        if (eid == 0) {
            char name[64]{};
            std::memcpy(name, a + 6, a[5]);
            eid = hits::eid_of_name(name);
            if (!eid) return kRNoActor;
        }
        std::memcpy(out, &eid, 4);
        *outLen = 4;
        for (auto it = g_watch.begin(); it != g_watch.end(); ++it)
            if (it->eid == eid) { if (!on) g_watch.erase(it); return kROk; }
        if (on) {
            if (g_watch.size() >= kMaxWatch) return kRFailed;
            g_watch.push_back({eid});
        }
        return kROk;
    }
    case kOpDiscard: {
        if (n != 5) return kRBadRequest;
        return hits::discard_from(get_u32(a + 1), a[0] != 0) ? kROk : kRFailed;
    }
    case kOpPlayerBlock: {
        if (n != 1) return kRBadRequest;
        const bool ok = motion::player_block(a[0] != 0);
        logf("WO132-CHECK player block %s -> %s", a[0] ? "on" : "off", ok ? "set" : "FAILED");
        return ok ? kROk : kRFailed;
    }
    case kOpTestFight: {
        // [npcEid:4][targetEid:4 (0 = the local player)][override:1]
        if (n != 9) return kRBadRequest;
        const uint32_t npc = get_u32(a);
        uint32_t tgt = get_u32(a + 4);
        if (!tgt) tgt = player_eid();
        void* ns = hits::soul_of_eid(npc);
        void* ts = hits::soul_of_eid(tgt);
        if (!ns || !ts) return kRNoActor;
        uint64_t rv = 0;
        const bool sk = hits::skirmish_add(ns, ts, a[8], &rv);
        const bool f = motion::test_fight(npc);
        bool have = false;
        for (auto& t : g_testFights) if (t.eid == npc) { t.until = now_s() + 300; have = true; }
        if (!have) g_testFights.push_back({npc, now_s() + 300});   // held for 5 min (re-asserted every 0.25 s)
        logf("WO132-CHECK test fight npc=0x%X target=0x%X: skirmish %s (override %u), combat+automation %s", npc, tgt, sk ? "added" : "NOT added", a[8], f ? "on" : "FAILED");
        return sk && f ? kROk : kRFailed;
    }
    case kOpRead: {
        if (n != 4) return kRBadRequest;
        uint32_t eid = get_u32(a);
        if (eid == 0) eid = player_eid();
        motion::NpcCombat c{};
        if (!motion::read_npc_combat(eid, &c)) return kRNoActor;
        put_read(out, c);
        *outLen = 12;
        return kROk;
    }
    case kOpHandOver: {
        if (n != 1) return kRBadRequest;
        out[0] = static_cast<uint8_t>(kcdmp::wo136::handover_fights(a[0] != 0, "live check"));
        *outLen = 1;
        return kROk;
    }
    case kOpAvatarSwing: {
        if (n != 4) return kRBadRequest;
        const uint32_t npc = kcdmp::wo136::avatar_swing(get_u32(a));
        std::memcpy(out, &npc, 4);
        *outLen = 4;
        return npc ? kROk : kRNoActor;
    }
    case kOpFights: {
        if (n != 1) return kRBadRequest;
        kcdmp::wo136::set_enabled(a[0] != 0);
        logf("WO136-FIGHTS %s -- the threat rule and the hand-over at the host's down", a[0] ? "on" : "off");
        return kROk;
    }
    case kOpHostThreat: {
        if (n != 5) return kRBadRequest;
        return kcdmp::wo136::test_host_threat(get_u32(a), a[4]) ? kROk : kRFailed;
    }
    case kOpStatus: {
        char t[480];
        int m = std::snprintf(t, sizeof(t),
            "wo132 far=%u leave=%u leave_fail=%u engage_on=%u engage_off=%u engage_skirmish=%u engaged=%zu watched=%zu watch_out=%u discard_eids=%d fail=%u skirmish=%s",
            c_far.load(), c_leave.load(), c_leaveFail.load(), c_engageOn.load(), c_engageOff.load(), c_engageSkirmish.load(), g_engaged.size(),
            g_watch.size(), c_watchOut.load(), hits::discard_count(), c_fail.load(), hits::skirmish_ready() ? "ready" : "no");
        if (m < 0) m = 0;
        if (static_cast<size_t>(m) < sizeof(t) - 2) { t[m++] = ' '; int k = kcdmp::wo136::status_text(t + m, static_cast<int>(sizeof(t)) - m); if (k > 0) m += k; if (m > static_cast<int>(sizeof(t)) - 1) m = static_cast<int>(sizeof(t)) - 1; }
        if (static_cast<size_t>(m) > cap) m = static_cast<int>(cap);
        std::memcpy(out, t, m);
        *outLen = static_cast<size_t>(m);
        return kROk;
    }
    default:
        return kRBadRequest;
    }
}

void tick() {
    const double now = now_s();
    kcdmp::wo136::tick();   // WO-136: queued hand-overs
    for (auto it = g_testFights.begin(); it != g_testFights.end();) {
        if (now > it->until || !engine::entity_by_id(it->eid)) { it = g_testFights.erase(it); continue; }
        if (now - it->last >= 0.25) {
            it->last = now;
            motion::NpcCombat c{};
            if (motion::read_npc_combat(it->eid, &c) && !c.combat) motion::test_fight(it->eid);
        }
        ++it;
    }
    if (g_watch.empty()) return;
    if (now - g_lastSample < 0.1) return;
    g_lastSample = now;
    for (auto it = g_watch.begin(); it != g_watch.end();) {
        motion::NpcCombat c{};
        if (!motion::read_npc_combat(it->eid, &c)) {
            // The body is gone (unloaded, a load): a last "not in combat" and out.
            if (it->sent && it->last.combat) { motion::NpcCombat z{}; emit(it->eid, z); }
            it = g_watch.erase(it);
            continue;
        }
        const bool changed = !it->sent || !same(c, it->last);
        // WO-147: a fight with an avatar reads combat=0 (combat mode is the player's fight's): its opponent keeps
        // the heartbeat too, or the joiner's engagement went stale after 3 s.
        const bool fighting = c.combat || c.opponentIsPlayer || c.opponentEid != 0;
        const bool hb = fighting && now - it->lastSent >= 1.0;
        if (changed || hb) {
            emit(it->eid, c);
            it->last = c; it->sent = true; it->lastSent = now;
        }
        ++it;
    }
}

void on_pipe_closed() {
    // Nothing drives the engaged copies any more: let them go (the Lua copy
    // guard gives the copies themselves back on its own agent-silent rule).
    const double now = now_s();
    for (uint32_t eid : g_engaged) {
        motion::set_npc_engage(eid, false, nullptr, now);
        uint64_t rv = 0;
        if (void* soul = hits::soul_of_eid(eid)) hits::skirmish_remove(soul, &rv);
        hits::discard_from(eid, false);
    }
    g_engaged.clear();
    g_watch.clear();
}

} // namespace kcdmp::wo132
