// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-151: the native half's switches and the guard's live check (wo151.h).
#include "wo151.h"
#include "buffs.h"
#include "fault_guard.h"
#include "hits.h"
#include "log.h"
#include "main_thread.h"
#include "respawn_actions.h"
#include "rttr_abi.h"
#include "script_context.h"
#include "weather.h"
#include "wo138.h"

#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <unordered_map>

namespace kcdmp::wo151 {

namespace {

fault::Site g_testRead{"wo151::test/read", fault::Kind::Read};
fault::Site g_testCall{"wo151::test/call", fault::Kind::Call};

using BadFn = void (*)();

// volatile, so the compiler cannot prove the address and drop the read or the call
volatile uintptr_t g_badAddress = 0x10;

// ---- Phase 1.1: a copy in a fight runs none of its own hit reactions (main thread) ----
// Each context is refcounted by the game; this clears only what it wrote itself.
constexpr const char* kCopyFightCtx[] = {
    "combat_actorSupressHitreactionAnimation",
    "switch_disabledHitReaction",
    "switch_disabledHitBehavioralReaction",
};
constexpr int kCopyFightCount = sizeof(kCopyFightCtx) / sizeof(*kCopyFightCtx);
struct CopyFight { void* soul = nullptr; uint8_t written = 0; };
std::unordered_map<uint32_t, CopyFight> g_copyFight;
std::atomic<uint32_t> c_cfOn{0}, c_cfOff{0}, c_cfFail{0}, c_tdTests{0};

void* c_soul_of(void* soul) {
    void* cs = soul ? buffs::as_c_soul(soul) : nullptr;
    return cs ? cs : soul;
}

} // namespace

void copy_fight_forget(uint32_t eid) {
    auto it = g_copyFight.find(eid);
    if (it == g_copyFight.end()) return;
    // the soul may be the same body's after a release; clear what this wrote while it is the same soul
    void* now = c_soul_of(hits::soul_of_eid(eid));
    if (now && now == it->second.soul)
        for (int i = 0; i < kCopyFightCount; ++i)
            if (it->second.written & (1u << i)) kcdmp::sctx::set_soul_context_quiet(now, kCopyFightCtx[i], false);
    g_copyFight.erase(it);
}

int status_text(char* out, int n) {
    return std::snprintf(out, n, "copy_fight=%zu cf_on=%u cf_off=%u cf_fail=%u suppress_call=%s td_tests=%u",
                         g_copyFight.size(), c_cfOn.load(), c_cfOff.load(), c_cfFail.load(),
                         rttr::can_suppress_hit_reaction() ? "yes" : "no", c_tdTests.load());
}

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (!len) return kRBadRequest;
    switch (body[0]) {
        case kOpConfig: {
            if (len < 3 || cap < 2) return kRBadRequest;
            fault::set_switch_off(body[1] != 0);
            main_thread::set_cost_meter(body[2] != 0);
            out[0] = fault::switch_off() ? 1 : 0;
            out[1] = main_thread::cost_meter() ? 1 : 0;
            *outLen = 2;
            return kROk;
        }
        case kOpStatus: {
            const fault::Totals t = fault::totals();
            char mk[160] = "";
            actions::marker_status(mk, sizeof mk);   // WO-164 C1: the agent toasts "Map markers are off this session" from this
            const int n = std::snprintf(reinterpret_cast<char*>(out), cap,
                "faults=%u sites=%u off=%u fault_switchoff=%s main_cost=%s %s", t.faults, t.sites, t.off,
                fault::switch_off() ? "on" : "off", main_thread::cost_meter() ? "on" : "off", mk);
            *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
            return kROk;
        }
        case kOpTestFault: {
            if (len < 2 || cap < 6) return kRBadRequest;
            fault::Site& s = body[1] == 1 ? g_testCall : g_testRead;
            bool ran;
            if (body[1] == 1) {
                ran = fault::guarded(s, [&] { reinterpret_cast<BadFn>(g_badAddress)(); });
            } else {
                volatile uint32_t sink = 0;
                ran = fault::guarded(s, [&] { sink = *reinterpret_cast<const volatile uint32_t*>(g_badAddress); });
                (void)sink;
            }
            out[0] = ran ? 1 : 0;
            const uint32_t f = s.faults.load();
            std::memcpy(out + 1, &f, 4);
            out[5] = fault::enabled(s) ? 0 : 1;
            *outLen = 6;
            logf("WO151-TESTFAULT kind=%s ran=%d faults_at_site=%u site=%s", body[1] == 1 ? "call" : "read", ran ? 1 : 0, f,
                 fault::enabled(s) ? "on" : "SWITCHED OFF");
            return kROk;
        }
        case kOpTestTakeDamage: {
            if (len < 18 || cap < 8) return kRBadRequest;
            uint32_t veid = 0, aeid = 0; float hp = 0, st = 0;
            std::memcpy(&veid, body + 1, 4); std::memcpy(&aeid, body + 5, 4);
            std::memcpy(&hp, body + 9, 4); std::memcpy(&st, body + 13, 4);
            const uint8_t mode = body[17];
            if (!std::isfinite(hp) || !std::isfinite(st) || hp < 0 || st < 0 || hp > 500 || st > 500 || mode > 3) return kRBadRequest;
            void* victim = c_soul_of(veid ? hits::soul_of_eid(veid) : rttr::read_player_soul());
            void* attacker = aeid ? c_soul_of(hits::soul_of_eid(aeid)) : nullptr;
            if (!victim || (aeid && !attacker)) return kRNoActor;
            float before = -1, after = -1;
            rttr::soul_state(victim, "health", &before);
            const bool ok = mode == 0 ? rttr::apply_damage_soul(victim, st, hp, nullptr)
                          : mode == 1 ? rttr::apply_damage_soul(victim, st, hp, attacker)
                                      : rttr::apply_damage_soul_ex(victim, st, hp, attacker, mode == 3 ? 1 : 0);
            rttr::soul_state(victim, "health", &after);
            c_tdTests.fetch_add(1);
            logf("WO151-TAKEDAMAGE test victim=%s attacker=%s mode=%s hp=%.1f st=%.1f -> %s (health %.1f -> %.1f)",
                 veid ? "npc" : "player", aeid ? "given" : "none",
                 mode == 0 ? "2-args" : mode == 1 ? "3-args(attacker)" : mode == 2 ? "4-args(suppress=false)" : "4-args(suppress=true)",
                 hp, st, ok ? "taken" : "FAILED", before, after);
            std::memcpy(out, &before, 4); std::memcpy(out + 4, &after, 4);
            *outLen = 8;
            return ok ? kROk : kRFailed;
        }
        case kOpCopyFight: {
            if (len < 6 || cap < 3) return kRBadRequest;
            uint32_t eid = 0;
            std::memcpy(&eid, body + 1, 4);
            const bool on = body[5] != 0;
            if (!eid) return kRBadRequest;
            if (!on) {
                const bool had = g_copyFight.count(eid) != 0;
                copy_fight_forget(eid);
                if (had) c_cfOff.fetch_add(1);
                out[0] = had ? 1 : 0; out[1] = had ? 0 : 1; out[2] = 0;
                *outLen = 3;
                return kROk;
            }
            void* soul = c_soul_of(hits::soul_of_eid(eid));
            if (!soul || soul == c_soul_of(rttr::read_player_soul())) return kRNoActor;   // never the player
            CopyFight& cf = g_copyFight[eid];
            if (cf.soul && cf.soul != soul) cf = CopyFight{};   // a new body behind the id (a load reuses ids)
            cf.soul = soul;
            uint8_t written = 0, already = 0, failed = 0;
            for (int i = 0; i < kCopyFightCount; ++i) {
                if (cf.written & (1u << i)) { ++already; continue; }
                const int r = kcdmp::sctx::set_soul_context_quiet(soul, kCopyFightCtx[i], true);
                if (r == 1) { cf.written |= static_cast<uint8_t>(1u << i); ++written; }
                else if (r == 0) ++already;
                else ++failed;
            }
            if (written) c_cfOn.fetch_add(1);
            if (failed) c_cfFail.fetch_add(1);
            out[0] = written; out[1] = already; out[2] = failed;
            *outLen = 3;
            if (written || failed)
                logf("WO151-COPYFIGHT eid=0x%X on: %u written, %u already set, %u not possible -- its own hit reactions are off while it fights",
                     eid, written, already, failed);
            return kROk;
        }
        case kOpJoinHold: {
            if (len < 4 || cap < 1) return kRBadRequest;
            uint16_t maxS = 0;
            std::memcpy(&maxS, body + 2, 2);
            const bool ok = wo138::join_hold(body[1] != 0, maxS);
            out[0] = wo138::join_held() ? 1 : 0;
            *outLen = 1;
            return ok ? kROk : kRFailed;
        }
        case kOpWeatherRead: {
            if (cap < 6) return kRBadRequest;
            char name[48];
            const uint32_t count = weather::last_blend(name, sizeof name);
            const size_t n = std::strlen(name);
            if (5 + n > cap) return kRBadRequest;
            std::memcpy(out, &count, 4);
            out[4] = static_cast<uint8_t>(n);
            std::memcpy(out + 5, name, n);
            *outLen = 5 + n;
            return weather::armed() ? kROk : kRFailed;
        }
        case kOpWeatherGate: {
            if (len < 2) return kRBadRequest;
            const size_t n = body[1];
            if (n > 47 || len < 2 + n) return kRBadRequest;
            char name[48] = {};
            std::memcpy(name, body + 2, n);
            for (size_t i = 0; i < n; ++i) {
                const char c = name[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')) return kRBadRequest;
            }
            weather::set_gate(n ? name : nullptr);
            return weather::armed() ? kROk : kRFailed;
        }
        default:
            return kRBadRequest;
    }
}

} // namespace kcdmp::wo151
