// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-195: dead copies fall and end the fight -- the native half (wo195.h).
#include "wo195.h"
#include "wo163.h"
#include "wo195_rules.h"

#include "engine.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "wo132.h"
#include "wo172.h"
#include "wo187.h"

#include <atomic>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_set>

namespace kcdmp::wo195 {

namespace {

using namespace kcdmp::wo195rules;

std::atomic<bool> g_on{true};                  // mp_dead_release (the agent's switch)
std::unordered_set<std::string> g_said;        // "eid|what" said once (main thread)
std::atomic<uint32_t> c_refusedHold{0}, c_refusedWish{0}, c_refusedGuard{0}, c_refusedEngage{0}, c_refusedOther{0}, c_releases{0},
    c_releasedEngage{0}, c_releasedGuard{0}, c_releasedWish{0}, c_releasedPin{0}, c_releasedCombat{0}, c_asked{0}, c_checks{0};

const char* name_of(uint32_t eid) {
    void* e = engine::entity_by_id(eid);
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "?";
}

std::atomic<uint32_t>& counter_for(const char* what) {
    if (!std::strcmp(what, "combat-hold")) return c_refusedHold;
    if (!std::strcmp(what, "wish")) return c_refusedWish;
    if (!std::strcmp(what, "guard-hold")) return c_refusedGuard;
    if (!std::strcmp(what, "engage")) return c_refusedEngage;
    return c_refusedOther;
}

std::string mask_text(uint8_t m) {
    std::string s;
    auto add = [&](uint8_t bit, const char* n) { if (m & bit) { if (!s.empty()) s += '+'; s += n; } };
    add(kHeldEngage, "engage"); add(kHeldGuard, "guard"); add(kHeldWish, "wish"); add(kHeldPin, "pin"); add(kHeldCombat, "combat");
    return s.empty() ? "none" : s;
}

void read_body(uint32_t eid, uint8_t* combat, uint8_t* living) {
    bool cm = false;
    const bool cmOk = motion::combat_mode_of(eid, &cm);
    *combat = tri(cmOk, cm);
    npcdrive::PhysicsStatus ps{};
    void* e = engine::entity_by_id(eid);
    const bool psOk = e && npcdrive::physics_status(e, &ps) && ps.present;
    *living = tri(psOk, ps.living != 0);
}

} // namespace

bool on() { return g_on.load(std::memory_order_relaxed); }

bool dead_gate(uint32_t eid, const char* what) {
    if (!eid || !refuse(on(), wo172::copy_dead(eid))) return false;
    counter_for(what).fetch_add(1, std::memory_order_relaxed);
    char key[48];
    std::snprintf(key, sizeof key, "%X|%s", eid, what);
    if (static_cast<int>(g_said.size()) > kSaidMax) g_said.clear();
    if (g_said.insert(key).second)
        logf("WO195-DEAD npc=%s eid=0x%X refused=%s -- a dead copy is never held, wished or engaged again (said once per body and kind)", name_of(eid), eid, what);
    return true;
}

uint8_t release(uint32_t eid, const char* why) {
    if (!eid) return 0;
    uint8_t held = 0;
    if (wo132::release_dead(eid)) held |= kHeldEngage;
    held |= wo187::drop_wish(eid, "the copy is dead (WO-195)");
    held |= motion::drop_fight_holds(eid, why);
    c_releases.fetch_add(1);
    if (held & kHeldEngage) c_releasedEngage.fetch_add(1);
    if (held & kHeldGuard) c_releasedGuard.fetch_add(1);
    if (held & kHeldWish) c_releasedWish.fetch_add(1);
    if (held & kHeldPin) c_releasedPin.fetch_add(1);
    if (held & kHeldCombat) c_releasedCombat.fetch_add(1);
    uint8_t cm = kUnreadable, lv = kUnreadable;
    read_body(eid, &cm, &lv);
    logf("WO195-RELEASE npc=%s eid=0x%X why=%s held=%s combat_mode=%s living=%s -- nothing of the mod holds it in a fight: the game's own death takes the body",
         name_of(eid), eid, why, mask_text(held).c_str(), cm == kUnreadable ? "?" : (cm ? "on" : "off"), lv == kUnreadable ? "?" : (lv ? "yes" : "no(ragdoll)"));
    return held;
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
            if (len != 2 || cap < 1) return wo163::kRBadRequest;
            if (body[1] != 255 && g_on.exchange(body[1] != 0) != (body[1] != 0)) logf("WO195-CFG mp_dead_release %s", on() ? "on" : "off");
            out[0] = on() ? 1 : 0;
            *outLen = 1;
            return wo163::kROk;
        }
        case kOpRelease: {
            // [op][eid:4] -> [held][combatMode][living]
            if (len != 5 || cap < 3) return wo163::kRBadRequest;
            uint32_t eid = 0;
            std::memcpy(&eid, body + 1, 4);
            if (!eid || !engine::entity_by_id(eid)) return wo163::kRNoActor;
            c_asked.fetch_add(1);
            out[0] = on() ? release(eid, "the agent: a death applied") : 0;
            read_body(eid, &out[1], &out[2]);
            *outLen = 3;
            return wo163::kROk;
        }
        case kOpCheck: {
            // [op][eid:4] -> [dead][combatMode][living][fightHeld]; eid 0 + [nameLen:1][name] = by name (any body, a vanilla one too)
            if (len < 5 || cap < 4) return wo163::kRBadRequest;
            uint32_t eid = 0;
            std::memcpy(&eid, body + 1, 4);
            if (!eid && len >= 7 && len == 6u + body[5] && body[5] <= 63) {
                const std::string nm(reinterpret_cast<const char*>(body + 6), body[5]);
                eid = hits::eid_of_name(nm.c_str());
            } else if (len != 5) return wo163::kRBadRequest;
            if (!eid || !engine::entity_by_id(eid)) return wo163::kRNoActor;
            c_checks.fetch_add(1);
            out[0] = wo172::copy_dead(eid) ? 1 : 0;
            read_body(eid, &out[1], &out[2]);
            out[3] = motion::fight_held(eid) ? 1 : 0;
            *outLen = 4;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

int status_text(char* out, int n) {
    return std::snprintf(out, n,
        "wo195 dead_release=%s releases=%u (asked %u) released engage=%u guard=%u wish=%u pin=%u combat=%u refused hold=%u wish=%u guard=%u engage=%u other=%u checks=%u",
        on() ? "on" : "off", c_releases.load(), c_asked.load(), c_releasedEngage.load(), c_releasedGuard.load(), c_releasedWish.load(), c_releasedPin.load(),
        c_releasedCombat.load(), c_refusedHold.load(), c_refusedWish.load(), c_refusedGuard.load(), c_refusedEngage.load(), c_refusedOther.load(), c_checks.load());
}

} // namespace kcdmp::wo195
