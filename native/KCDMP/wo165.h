// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-165 -- the victim decides: a host's blow replayed through the engine's own hit processor on the victim's machine
// (research/WO-162/combat-RE.md Q1.7 "Level B"; docs/WO-165-findings.md "The instrument").
//
// One call, main thread, inside one frame: every pointer is looked up and checked here (the attacker's combat actor and model, its
// C_CombatRPG by class and by its back pointer, the victim's combat actor), the attacker's four attack fields are written (WO-163 P1:
// a copy playing a host row never sets them), the engine's own result constructor runs on our buffer, and
// C_CombatRPG::RPGProcessHit(result, hit-in, flags) is called under fault::guarded with the slot hook's replay window open
// (hits.h: never discarded, the engine-built record captured, the victim's damage measured). The four fields are put back.
//
// Any fault on the way switches the replay OFF for the session (logged; the agent says so on screen); it never runs again until the
// game restarts. Reached through the WO-163 op family (pipe 0x2B, wo163.h ops 5-7).
#pragma once
#include <cstddef>
#include <cstdint>

#include "motion.h"
#include "wo165_rules.h"

namespace kcdmp::wo165 {

constexpr uint8_t kFlagSkipFilter = 0x01;   // hit-in byte 0: the engine's repeat filter is skipped (our verdicts are deduped by id)
constexpr uint8_t kFlagKeepFields = 0x02;   // leave the written attack fields in place (a probe reading them afterwards)
constexpr uint8_t kFlagOwnFields = 0x04;    // write nothing: call with the attacker's own fields (a real NPC, P4a's control)
constexpr uint8_t kFlagCreateCa = 0x08;     // a dummy without a combat actor gets one (the avatars' own route); never for copies

struct Request {
    uint32_t attackerEid = 0;
    uint32_t victimEid = 0;      // 0 = the local player
    motion::AttackFields fields;
    uint8_t flags = 0;
};

enum Reason : uint8_t {
    kOk = 0, kOff = 1, kNotArmed = 2, kNoAttacker = 3, kNoVictim = 4, kNoProcessor = 5, kFieldsUnnamed = 6, kFault = 7, kSameActor = 8,
    kNoPosition = 9,
};
const char* reason_name(uint8_t r);

struct Result {
    uint8_t reason = kNotArmed;
    bool called = false, returned = false, seen = false;
    Outcome outcome = Outcome::None;
    uint32_t seq = 0;
    uint8_t flags[kFlagsBytes]{};
    uint8_t recBlock = 0, recSecond = 0, recPerfect = 0, recBroken = 0, recZoneMismatch = 0;
    motion::AttackFields before, written;
    motion::ModelRead victimModel;   // the victim's block fields as they were when the call was made
};

// main thread
bool resolve();             // idempotent; false = not armed (why() says why)
bool armed();
bool off();                 // switched off by a fault this session
const char* why();
Result replay(const Request& rq);

// the measured damage of a finished replay (the slot hook's watch): state 0 unknown seq, 1 pending, 2 measured, 3 merged into an earlier
struct Damage { uint8_t state = 0; float health = 0, stamina = 0; bool victimLive = false; };
Damage damage_of(uint32_t seq);

int status_text(char* out, int n);

} // namespace kcdmp::wo165
