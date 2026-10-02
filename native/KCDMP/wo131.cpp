// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-131 -- see wo131.h.
#include "wo131.h"
#include "fault_guard.h"

#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>

#include "buffs.h"
#include "engine.h"
#include "log.h"
#include "npc_drive.h"
#include "respawn_actions.h"
#include "rttr_abi.h"
#include "motion.h"

namespace kcdmp::wo131 {
namespace {

constexpr size_t kActorGetSoul = 0x6E0;   // hits.cpp / motion.cpp
constexpr const char* kGuardGuidText = "4b43444d-7121-4d67-b1a5-9e2f6d8c0a15";   // kcdmp_avatar_guard (buff__kcdmp.xml)
// WO-135: a knocked-out copy. kcdmp_knockout_guard is buff__kcdmp.xml's imm-only
// guard (WO-113); the other three are the game's own (Tables :: rpg/buff.xml).
constexpr const char* kKoGuardGuidText  = "4b43444d-7113-4d67-b1a5-9e2f6d8c0a14";   // kcdmp_knockout_guard
constexpr const char* kUnconsciousText  = "f8d60fe4-e2c1-420a-946a-213e1cd09265";   // unconscious_nonpersistend (Cpp:Unconscious)
constexpr const char* kInfiniteUncText  = "74cf0c29-d03e-4233-9352-b91ca5ea69ea";   // infinite_unconsciousness_nonpersistent (ufo*0)
constexpr const char* kRemoveUncText    = "bd22f98a-e61f-4d83-b39c-79d1d85b6b91";   // remove_unconsciousness

template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "wo131::rd");
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}
void* vslot(void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    if (!obj || !rd(obj, 0, &vt) || !vt || !rd(vt, off, &fn)) return nullptr;
    return fn;
}
bool call_p0(void* fn, void* self, void** out) {
    KCDMP_FAULT_CALL(site, "wo131::call_p0");
    return fault::guarded(site, [&] { *out = reinterpret_cast<void* (__fastcall*)(void*)>(fn)(self); });
}
bool call_p1u(void* fn, void* self, uint32_t a, void** out) {
    KCDMP_FAULT_CALL(site, "wo131::call_p1u");
    return fault::guarded(site, [&] { *out = reinterpret_cast<void* (__fastcall*)(void*, uint32_t)>(fn)(self, a); });
}
void* actor_by_eid(uint32_t eid) {
    void* gi = engine::game_iface();
    void* am = nullptr;
    if (!gi || !rd(gi, 0x188, &am) || !am) return nullptr;
    void* fn = vslot(am, 0x18);
    void* actor = nullptr;
    if (!fn || !call_p1u(fn, am, eid, &actor)) return nullptr;
    return actor;
}
void* soul_of_eid(uint32_t eid) {
    void* actor = actor_by_eid(eid);
    void* fn = actor ? vslot(actor, kActorGetSoul) : nullptr;
    void* soul = nullptr;
    return fn && call_p0(fn, actor, &soul) ? soul : nullptr;
}

unsigned char g_guard[16]{};
bool g_guardParsed = false;
const unsigned char* guard_guid() {
    if (!g_guardParsed) g_guardParsed = buffs::parse_guid(kGuardGuidText, g_guard);
    return g_guardParsed ? g_guard : nullptr;
}

struct KoGuids { unsigned char ko[16], unc[16], inf[16], rem[16]; bool ok = false, tried = false; } g_ko;
const KoGuids* ko_guids() {
    if (!g_ko.tried) {
        g_ko.tried = true;
        g_ko.ok = buffs::parse_guid(kKoGuardGuidText, g_ko.ko) && buffs::parse_guid(kUnconsciousText, g_ko.unc)
               && buffs::parse_guid(kInfiniteUncText, g_ko.inf) && buffs::parse_guid(kRemoveUncText, g_ko.rem);
    }
    return g_ko.ok ? &g_ko : nullptr;
}

std::atomic<uint32_t> c_checks{0}, c_guardOn{0}, c_guardOff{0}, c_follow{0}, c_followCredit{0},
                      c_stopFight{0}, c_restore{0}, c_faction{0}, c_fail{0}, c_knockouts{0}, c_wakes{0};

void put_f(uint8_t* p, float v) { std::memcpy(p, &v, 4); }
float get_f(const uint8_t* p) { float v; std::memcpy(&v, p, 4); return v; }
uint32_t get_u32(const uint8_t* p) { uint32_t v; std::memcpy(&v, p, 4); return v; }

} // namespace

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 1 || cap < 32) return kRBadRequest;
    const uint8_t op = body[0];
    const uint8_t* a = body + 1;
    const size_t n = len - 1;
    switch (op) {
    case kOpHitCheck: {
        if (n < 17 || n != static_cast<size_t>(17 + a[16]) || a[16] > 63) return kRBadRequest;
        char name[64]{};
        std::memcpy(name, a + 17, a[16]);
        npcdrive::HitCheck hc{};
        npcdrive::hit_check(name, &hc);
        // Everything by the bound body's entity id: rttr::find_soul_by_guid is a
        // full reflection walk of the soul list (seconds on a live world --
        // the first live run timed the agent out), the actor's soul is two calls.
        float hp = -1;
        int guarded = -1;
        if (hc.eid) {
            void* s0 = soul_of_eid(hc.eid);
            void* cs = s0 && buffs::as_c_soul(s0) ? buffs::as_c_soul(s0) : s0;
            if (cs) {
                rttr::soul_state(cs, "health", &hp);
                if (guard_guid() && buffs::ready()) guarded = buffs::has(cs, guard_guid());
                // WO-135: a knocked-out copy carries the imm-only guard instead.
                if (guarded == 0 && ko_guids() && buffs::ready()) guarded = buffs::has(cs, ko_guids()->ko);
            }
        }
        c_checks.fetch_add(1);
        const double ageMs = hc.ageS < 0 ? 65535.0 : hc.ageS * 1000.0;
        const uint16_t age = static_cast<uint16_t>(ageMs > 65535.0 ? 65535.0 : ageMs);
        out[0] = hc.bound ? 1 : 0;
        std::memcpy(out + 1, &age, 2);
        put_f(out + 3, hc.distM);
        out[7] = hc.flags;
        put_f(out + 8, hp);
        out[12] = static_cast<uint8_t>(guarded < 0 ? 0xFF : guarded);
        *outLen = 13;
        return kROk;
    }
    case kOpCopyGuard: {
        if (n != 5) return kRBadRequest;
        const uint8_t mode = a[0];
        const uint32_t eid = get_u32(a + 1);
        if (mode > kGuardWake) return kRBadRequest;
        if (!guard_guid() || !buffs::ready()) { c_fail.fetch_add(1); return kRUnarmed; }
        const KoGuids* k = ko_guids();
        if (mode >= kGuardKnockout && !k) { c_fail.fetch_add(1); return kRUnarmed; }
        void* soul = soul_of_eid(eid);
        if (!soul) { c_fail.fetch_add(1); return kRNoActor; }
        void* cs = buffs::as_c_soul(soul) ? buffs::as_c_soul(soul) : soul;
        bool present = false;
        switch (mode) {
        case kGuardOn:
            if (k) buffs::remove_all(cs, k->ko);
            if (buffs::has(cs, guard_guid()) <= 0 && !buffs::add(cs, guard_guid())) { c_fail.fetch_add(1); return kRFailed; }
            c_guardOn.fetch_add(1);
            present = buffs::has(cs, guard_guid()) > 0;
            break;
        case kGuardOff:
            buffs::remove_all(cs, guard_guid());
            if (k) buffs::remove_all(cs, k->ko);
            c_guardOff.fetch_add(1);
            present = buffs::has(cs, guard_guid()) <= 0;
            break;
        case kGuardKnockout:
            // Order: the imm-only guard first (never a moment without imm), then
            // the full guard off (its upr=1 blocks unconsciousness), then the
            // game's own unconsciousness.
            if (buffs::has(cs, k->ko) <= 0 && !buffs::add(cs, k->ko)) { c_fail.fetch_add(1); return kRFailed; }
            buffs::remove_all(cs, guard_guid());
            if (buffs::has(cs, k->inf) <= 0) buffs::add(cs, k->inf);
            if (buffs::has(cs, k->unc) <= 0 && !buffs::add(cs, k->unc)) { c_fail.fetch_add(1); return kRFailed; }
            c_knockouts.fetch_add(1);
            present = buffs::has(cs, k->unc) > 0;
            logf("WO135-KO copy eid=0x%X knocked out (the host's NPC is): unconscious=%d imm-only guard=%d", eid,
                 buffs::has(cs, k->unc), buffs::has(cs, k->ko));
            break;
        case kGuardWake:
            buffs::remove_all(cs, k->inf);
            buffs::remove_all(cs, k->unc);
            buffs::add(cs, k->rem);
            if (buffs::has(cs, guard_guid()) <= 0) buffs::add(cs, guard_guid());
            buffs::remove_all(cs, k->ko);
            c_wakes.fetch_add(1);
            present = buffs::has(cs, guard_guid()) > 0;
            logf("WO135-KO copy eid=0x%X woken (the host's NPC is up): unconscious=%d guard=%d", eid,
                 buffs::has(cs, k->unc), buffs::has(cs, guard_guid()));
            break;
        }
        out[0] = static_cast<uint8_t>(present ? 1 : 0);
        *outLen = 1;
        return kROk;
    }
    case kOpFollowHp: {
        // [guid:16][eid:4][hp:4f]: the soul by entity id (cheap); the GUID only
        // keys the sampler's credit.
        if (n != 24) return kRBadRequest;
        const uint32_t eid = get_u32(a + 16);
        const float want = get_f(a + 20);
        if (!std::isfinite(want) || want < 0) return kRBadRequest;
        void* s0 = soul_of_eid(eid);
        void* soul = s0 && buffs::as_c_soul(s0) ? buffs::as_c_soul(s0) : s0;
        if (!soul) { c_fail.fetch_add(1); return kRNoSoul; }
        float before = -1, after = -1;
        if (!rttr::soul_state(soul, "health", &before)) { c_fail.fetch_add(1); return kRFailed; }
        // Never below 1: the host's death comes as its own order (op 2 off, then
        // ApplyDeath); a follow is never the thing that kills a copy.
        const float target = want < 1.0f ? 1.0f : want;
        if (std::fabs(target - before) < 0.5f) { put_f(out, before); put_f(out + 4, before); *outLen = 8; return kROk; }
        if (target < before) { rttr::note_remote_damage(a, before - target); c_followCredit.fetch_add(1); }
        if (!rttr::soul_set_state(soul, "health", target)) { c_fail.fetch_add(1); return kRFailed; }
        rttr::soul_state(soul, "health", &after);
        c_follow.fetch_add(1);
        put_f(out, before); put_f(out + 4, after);
        *outLen = 8;
        return kROk;
    }
    case kOpStopFight: {
        if (n != 4) return kRBadRequest;
        const uint32_t eid = get_u32(a);
        if (!actions::stop_fight_available()) { c_fail.fetch_add(1); return kRUnarmed; }
        void* soul = eid == 0 ? rttr::read_player_soul() : soul_of_eid(eid);
        if (!soul) { c_fail.fetch_add(1); return kRNoActor; }
        void* cs = buffs::as_c_soul(soul) ? buffs::as_c_soul(soul) : soul;
        const bool ok = actions::stop_fight(cs);
        logf("WO131-STOPFIGHT eid=0x%X (%s) -> %s", eid, eid == 0 ? "the local player" : "a body", ok ? "called" : "FAILED");
        if (!ok) { c_fail.fetch_add(1); return kRFailed; }
        c_stopFight.fetch_add(1);
        return kROk;
    }
    case kOpRestoreHp: {
        if (n != 8) return kRBadRequest;
        const uint32_t eid = get_u32(a);
        const float want = get_f(a + 4);
        if (!std::isfinite(want) || want <= 0) return kRBadRequest;
        void* soul = soul_of_eid(eid);
        if (!soul) { c_fail.fetch_add(1); return kRNoActor; }
        void* cs = buffs::as_c_soul(soul) ? buffs::as_c_soul(soul) : soul;
        float before = -1, after = -1;
        rttr::soul_state(cs, "health", &before);
        if (!rttr::soul_set_state(cs, "health", want)) { c_fail.fetch_add(1); return kRFailed; }
        rttr::soul_state(cs, "health", &after);
        c_restore.fetch_add(1);
        put_f(out, before); put_f(out + 4, after);
        *outLen = 8;
        return kROk;
    }
    case kOpFaction: {
        if (n != 17) return kRBadRequest;
        const uint8_t mode = a[16];
        bool ok = false;
        if (mode == 2) ok = rttr::set_ghost_faction_player(a);
        else ok = rttr::set_ghost_faction_hostile(a, mode == 1);
        if (!ok) { c_fail.fetch_add(1); return kRFailed; }
        c_faction.fetch_add(1);
        return kROk;
    }
    case kOpPlayerCrouch: {
        if (n != 1) return kRBadRequest;
        const bool ok = motion::player_set_crouch(a[0] != 0);
        logf("WO135-CROUCH check: the player's own SetCrouch(%d) -> %s (no input)", a[0] != 0 ? 1 : 0, ok ? "called" : "FAILED");
        return ok ? kROk : kRFailed;
    }
    case kOpStatus: {
        char t[256];
        int m = std::snprintf(t, sizeof(t),
            "wo131 checks=%u guard_on=%u guard_off=%u follow=%u follow_credit=%u stopfight=%u restore=%u faction=%u fail=%u buffs=%s stopfight_armed=%s knockouts=%u wakes=%u",
            c_checks.load(), c_guardOn.load(), c_guardOff.load(), c_follow.load(), c_followCredit.load(), c_stopFight.load(),
            c_restore.load(), c_faction.load(), c_fail.load(), buffs::ready() ? "ready" : "no", actions::stop_fight_available() ? "yes" : "no",
            c_knockouts.load(), c_wakes.load());
        if (m < 0) m = 0;
        if (static_cast<size_t>(m) > cap) m = static_cast<int>(cap);
        std::memcpy(out, t, m);
        *outLen = static_cast<size_t>(m);
        return kROk;
    }
    default:
        return kRBadRequest;
    }
}

} // namespace kcdmp::wo131
