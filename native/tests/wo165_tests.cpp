// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-165: engine-free checks of native/KCDMP/wo165_rules.h -- the call-site parser that lifts the result ctor, the processor offset
// and RPGProcessHit out of the collision handler's live bytes; the structs we hand the engine; the outcome rule. Linked into
// KCDMP_NativeTests; wo165_tests() returns the number of failures.
#include <cmath>
#include <cstdio>
#include <cstring>

#include "wo165_rules.h"

using namespace kcdmp::wo165;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

// 80 bytes of the collision handler around the call (the Modding Tools build, 1.5.5): the hit index read, the release, then
// lea rcx,[rbp-10h]; call <ctor>; mov rcx,[rsi+8]; lea r9,[rsp+50h]; lea r8,[rsp+70h]; lea rdx,[rbp-10h]; mov rcx,[rcx+478h]; call <process>
const uint8_t kSite[] = {
    0x48, 0x85, 0xC9, 0x74, 0x1D, 0x48, 0x8B, 0x01, 0xFF, 0x90, 0x88, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x4C, 0x24, 0x60, 0x89,
    0x44, 0x24, 0x74, 0x48, 0x85, 0xC9, 0x74, 0x06, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x10, 0x48, 0x8D, 0x4D, 0xF0, 0xE8, 0x65,
    0x3B, 0x0A, 0x00, 0x48, 0x8B, 0x4E, 0x08, 0x4C, 0x8D, 0x4C, 0x24, 0x50, 0x4C, 0x8D, 0x44, 0x24, 0x70, 0x48, 0x8D, 0x55,
    0xF0, 0x48, 0x8B, 0x89, 0x78, 0x04, 0x00, 0x00, 0xE8, 0x27, 0xE3, 0x20, 0x00, 0x84, 0xC0, 0x75, 0x33, 0x48, 0x8D, 0x05,
};
}   // namespace

int wo165_tests(int* passed) {
    // ---- the call site ----
    {
        const uintptr_t base = 0x40000000;
        CallSite cs{};
        WCHECK(parse_call_site(kSite, sizeof kSite, base, &cs), "the real call site parses");
        WCHECK(cs.ctor == base + 43 + 0x0A3B65, "the ctor is the first call's target (+%llx)", static_cast<unsigned long long>(cs.ctor - base));
        WCHECK(cs.rpgOffset == 0x478, "the processor offset is the load's displacement (0x%X)", cs.rpgOffset);
        WCHECK(cs.process == base + 73 + 0x20E327, "the processor is the second call's target (+%llx)", static_cast<unsigned long long>(cs.process - base));
        // moved: the same bytes elsewhere give the same relations
        CallSite c2{};
        WCHECK(parse_call_site(kSite, sizeof kSite, base + 0x1000, &c2) && c2.ctor - cs.ctor == 0x1000 && c2.process - cs.process == 0x1000, "relative targets move with the code");
    }
    {
        // twice in the window: ambiguous, refused
        uint8_t two[sizeof kSite * 2];
        std::memcpy(two, kSite, sizeof kSite);
        std::memcpy(two + sizeof kSite, kSite, sizeof kSite);
        CallSite cs{};
        WCHECK(!parse_call_site(two, sizeof two, 0x1000, &cs), "two call sites are ambiguous: refused");
    }
    {
        // the load's call is not a direct call: no match
        uint8_t b[sizeof kSite];
        std::memcpy(b, kSite, sizeof b);
        b[68] = 0x90;
        CallSite cs{};
        WCHECK(!parse_call_site(b, sizeof b, 0x1000, &cs), "no direct call after the processor load: refused");
    }
    {
        // the processor load too far from the ctor call (more than 0x40 bytes of other code in between): refused
        uint8_t b[sizeof kSite + 0x40];
        std::memcpy(b, kSite, 43);
        std::memset(b + 43, 0x90, 0x40);
        std::memcpy(b + 43 + 0x40, kSite + 43, sizeof kSite - 43);
        CallSite cs{};
        WCHECK(!parse_call_site(b, sizeof b, 0x1000, &cs), "a load 0x40 bytes past the ctor call is not this call site");
    }
    {
        CallSite cs{};
        WCHECK(!parse_call_site(kSite, 40, 0x1000, &cs), "a window that ends before the call: no match");
        WCHECK(!parse_call_site(kSite, 0, 0x1000, &cs), "an empty window: no match");
    }

    // ---- the prologues: what resolve() compares (the build-identity test for the two functions we call) ----
    WCHECK(sizeof kProcessPrologue == 21 && kProcessPrologue[0] == 0x48 && kProcessPrologue[14] == 0x48 && kProcessPrologue[17] == 0x80,
           "RPGProcessHit's prologue: mov [rsp+20h],rbp ... sub rsp,80h");
    WCHECK(sizeof kCtorPrologue == 9 && kCtorPrologue[1] == 0x53 && kCtorPrologue[5] == 0x30, "the result ctor's prologue: push rbx; sub rsp,30h; mov rbx,rcx");

    // ---- the structs ----
    {
        uint8_t hitIn[kHitInBytes];
        std::memset(hitIn, 0xCC, sizeof hitIn);
        uint8_t d[kDetailsBytes];
        uint8_t sub[kSubHitBytes];
        void* victim = reinterpret_cast<void*>(0x1234567890ull);
        build_hit_in(hitIn, true, victim, d, sub, sub + kSubHitBytes);
        uint32_t k8, kC; void* v; void* det; void* b; void* e; void* c;
        std::memcpy(&k8, hitIn + 8, 4); std::memcpy(&kC, hitIn + 12, 4);
        std::memcpy(&v, hitIn + 0x10, 8); std::memcpy(&det, hitIn + 0x18, 8);
        std::memcpy(&b, hitIn + 0x20, 8); std::memcpy(&e, hitIn + 0x28, 8); std::memcpy(&c, hitIn + 0x30, 8);
        WCHECK(hitIn[0] == 1 && hitIn[1] == 0 && hitIn[2] == 0, "skip filter set, contact flags clear");
        WCHECK(hitIn[3] == 0, "the 'victim blocking' force byte is never set: the engine decides");
        WCHECK(k8 == 2 && kC == 2, "dwords +8 and +0xC are 2 (the hand slot comes from the attacker's AttackHandSlot)");
        WCHECK(v == victim && det == d, "victim combat actor at +0x10, the details at +0x18");
        WCHECK(b == sub && e == sub + kSubHitBytes && c == e, "one 16-byte sub-hit, capacity = end (the engine never grows it)");
        uint32_t idx; std::memcpy(&idx, hitIn + 4, 4);
        WCHECK(idx == 0 && hitIn[0x38] == 0 && hitIn[0x3F] == 0, "hit index 0, the tail zeroed");
        build_hit_in(hitIn, false, victim, d, sub, sub + kSubHitBytes);
        WCHECK(hitIn[0] == 0, "without the flag the repeat filter runs");

        const float pos[3] = {2338.0f, 2048.3f, 110.4f};
        build_details(d, pos, 0x7777u);
        float p2[3]; uint32_t eid; int32_t sp, mat, asp;
        std::memcpy(p2, d, 12); std::memcpy(&eid, d + 0x24, 4); std::memcpy(&sp, d + 0x64, 4); std::memcpy(&mat, d + 0x68, 4); std::memcpy(&asp, d + 0x6C, 4);
        WCHECK(p2[0] == pos[0] && p2[1] == pos[1] && p2[2] == pos[2], "the hit position at +0");
        WCHECK(eid == 0x7777u, "the victim's entity id at +0x24 (the repeat filter's key)");
        WCHECK(sp == -1 && mat == -1 && asp == -1, "subparts and material are 'none' (-1: the coefficient 1.0 path)");
        void* cav; std::memcpy(&cav, d + 0xC8, 8);
        WCHECK(cav == nullptr && d[0x88] == 0 && d[0x8A] == 0, "no actor and no contact flags in the details (ours are in the hit-in)");

        build_sub_hit(sub);
        float f[3]; uint32_t cond;
        std::memcpy(f, sub, 12); std::memcpy(&cond, sub + 12, 4);
        WCHECK(f[0] == 1.0f && f[1] == 1.0f && f[2] == 1.0f && cond == 0, "one sub-hit: factors 1.0, condition 0");
    }

    // ---- the outcome ----
    {
        uint8_t fl[kFlagsBytes]{};
        uint8_t rec[0x90]{};
        WCHECK(classify(false, false, fl, nullptr) == Outcome::Filtered, "the processor said no: filtered");
        WCHECK(classify(true, false, fl, nullptr) == Outcome::None, "returned without running the core (no attack type): none");
        WCHECK(classify(true, true, fl, rec) == Outcome::Hit, "the core ran, nothing blocked: hit");
        fl[0] = 1;
        WCHECK(classify(true, true, fl, rec) == Outcome::Blocked, "blocking output: blocked");
        rec[0x57] = 1;
        WCHECK(classify(true, true, fl, rec) == Outcome::Broken, "the record's broken-block: broken");
        fl[3] = 1;
        WCHECK(classify(true, true, fl, rec) == Outcome::PerfectBlock, "the perfect-block output wins");
        uint8_t fl2[kFlagsBytes]{};
        uint8_t rec2[0x90]{};
        rec2[0x54] = 1; rec2[0x56] = 1;
        WCHECK(classify(true, true, fl2, rec2) == Outcome::Hit, "the record's +0x54 / +0x56 are the victim's state the core READ, never the outcome");
        WCHECK(std::strcmp(outcome_name(Outcome::PerfectBlock), "pb") == 0 && std::strcmp(outcome_name(Outcome::Blocked), "blocked") == 0 &&
               std::strcmp(outcome_name(Outcome::Hit), "hit") == 0 && std::strcmp(outcome_name(Outcome::Broken), "broken") == 0,
               "the names the logs and the wire use");
    }
    // ---- C1: the host's lock-on ----
    {
        LockView v{};
        v.combat = true; v.oppIsAvatar = true; v.dist = 4.0f; v.facingCos = 0.9f;
        WCHECK(lock_rule(v).act == LockAct::Set, "near (4 m) and facing an NPC that fights the avatar: the pair is set");
        LockView far = v; far.dist = 6.5f;
        WCHECK(lock_rule(far).act == LockAct::None, "6.5 m: not yet");
        LockView edge = v; edge.dist = kLockSetM;
        WCHECK(lock_rule(edge).act == LockAct::Set, "exactly 6 m: set");
        LockView away = v; away.facingCos = 0.3f;
        WCHECK(lock_rule(away).act == LockAct::None, "near but looking away (more than 60 degrees): not set");
        LockView notAvatar = v; notAvatar.oppIsAvatar = false;
        WCHECK(lock_rule(notAvatar).act == LockAct::None, "an NPC fighting someone else (not a partner): never");
        LockView idle = v; idle.combat = false;
        WCHECK(lock_rule(idle).act == LockAct::None, "an NPC not in combat: never");
        LockView dead = v; dead.alive = false;
        WCHECK(lock_rule(dead).act == LockAct::None, "a dead NPC: never");

        LockView p = v; p.paired = true;
        WCHECK(lock_rule(p).act == LockAct::Keep, "paired and still near: kept");
        LockView pAway = p; pAway.facingCos = -1.0f; pAway.dist = 9.9f;
        WCHECK(lock_rule(pAway).act == LockAct::Keep, "paired: turning away or stepping back to 9.9 m keeps it (hysteresis 6 -> 10 m)");
        LockView pFar = p; pFar.dist = 10.1f;
        WCHECK(lock_rule(pFar).act == LockAct::Remove && std::strcmp(lock_rule(pFar).why, "host-left-10m") == 0, "past 10 m: removed");
        LockView pEnd = p; pEnd.combat = false;
        WCHECK(lock_rule(pEnd).act == LockAct::Remove && std::strcmp(lock_rule(pEnd).why, "fight-ended") == 0, "the fight ended: removed");
        LockView pOther = p; pOther.oppIsAvatar = false;
        WCHECK(lock_rule(pOther).act == LockAct::Remove, "the NPC left the avatar for someone else: removed");
        LockView pHost = p; pHost.oppIsHost = true; pHost.oppIsAvatar = false;
        WCHECK(lock_rule(pHost).act == LockAct::Forget, "the NPC turned on the host (his blow, P7): forgotten, never removed -- his own fight goes on");
        LockView pDead = p; pDead.alive = false; pDead.oppIsHost = true;
        WCHECK(lock_rule(pDead).act == LockAct::Remove && std::strcmp(lock_rule(pDead).why, "npc-dead") == 0, "dead wins over everything: removed");
        LockView pWd = p; pWd.farForS = kLockFarS;
        WCHECK(lock_rule(pWd).act == LockAct::Remove && std::strcmp(lock_rule(pWd).why, "watchdog-20s-over-30m") == 0, "20 s over 30 m from every player: released");
        LockView pWd2 = p; pWd2.farForS = kLockFarS - 0.1;
        WCHECK(lock_rule(pWd2).act == LockAct::Keep, "19.9 s: kept");

        // the facing from a yaw: CryEngine faces +y at yaw 0
        WCHECK(facing_cos(0.0f, 0, 0, 0, 5) > 0.99f, "yaw 0 faces +y");
        WCHECK(facing_cos(0.0f, 0, 0, 0, -5) < -0.99f, "yaw 0: an NPC behind");
        WCHECK(std::fabs(facing_cos(1.5707963f, 0, 0, -5, 0) - 1.0f) < 1e-3f, "yaw +90 degrees faces -x");
        WCHECK(facing_cos(0.0f, 0, 0, 0, 0) == 1.0f, "on top of each other: counts as facing");
        WCHECK(std::strcmp(lock_not_set_why(far), "farther-than-6m") == 0 && std::strcmp(lock_not_set_why(away), "not-facing") == 0 &&
               std::strcmp(lock_not_set_why(idle), "npc-not-fighting") == 0 && std::strcmp(lock_not_set_why(notAvatar), "not-fighting-a-partner") == 0 &&
               std::strcmp(lock_not_set_why(dead), "npc-dead") == 0 && lock_not_set_why(v)[0] == 0, "a missed lock names its reason");
    }
    if (passed) *passed = g_pass;
    return g_fail;
}
