// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-137: engine-free checks of the shared-quests native rules (native/KCDMP/wo137_rules.h):
// which C_Function calls are quest time sets (the joiner's time gate), which paths are quests,
// when a config is logged, when the time gate is on. Linked into KCDMP_NativeTests;
// wo137_rules_tests() returns the number of failures.
#include <cstdio>

#include "wo137_rules.h"

using namespace kcdmp::wo137rules;

namespace {
int g_fail = 0, g_pass = 0;
#define QCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo137_rules_tests(int* passed) {
    // the joiner's time gate: exactly the two time-setting methods
    QCHECK(is_time_method("wh::rpgmodule::AdvanceWorldTime"), "AdvanceWorldTime is a quest time set");
    QCHECK(is_time_method("wh::conceptmodule::PassLongTime"), "PassLongTime is a quest time set");
    QCHECK(is_time_method("AdvanceWorldTime"), "a bare method name matches too");
    QCHECK(!is_time_method("wh::rpgmodule::GetWorldTime"), "reading the time is not setting it");
    QCHECK(!is_time_method("wh::rpgmodule::XAdvanceWorldTime"), "a longer name that ends the same is not it");
    QCHECK(!is_time_method("wh::rpgmodule::AdvanceWorldTimeX"), "a longer name that starts the same is not it");
    QCHECK(!is_time_method(""), "empty is not a time set");
    QCHECK(!is_time_method(nullptr), "null is not a time set");

    // quests only
    QCHECK(is_barbora_path("Barbora.trosecko.hledaniPsa.h.findVorech"), "a quest State path");
    QCHECK(!is_barbora_path("Haste.test.x"), "Haste modules are not quests");
    QCHECK(!is_barbora_path("Barbora."), "the bare root is not a path");
    QCHECK(!is_barbora_path("barbora.trosecko.x"), "case matters (the engine's names)");
    QCHECK(!is_barbora_path(nullptr), "null is not a path");

    // the config is logged on a change only (the agent re-sends it every 10 s)
    QCHECK(config_key(true, 1, false) == config_key(true, 1, false), "the same config: the same key (not logged again)");
    QCHECK(config_key(true, 1, false) != config_key(false, 0, false), "on -> off is a change");
    QCHECK(config_key(true, 2, true) != config_key(true, 2, false), "the time gate going off is a change");
    QCHECK(config_key(true, 1, false) != config_key(true, 2, false), "a role change is a change");

    // the time gate: only an active joiner that asked for it, with the hook armed
    QCHECK(time_gate_on(true, 2, 1, true), "an active joiner with the flag: on");
    QCHECK(!time_gate_on(true, 1, 1, true), "the host never gates its own time sets");
    QCHECK(!time_gate_on(false, 2, 1, true), "an inactive mirror (a load, the kill switch): off");
    QCHECK(!time_gate_on(true, 2, 0, true), "the agent did not ask: off");
    QCHECK(!time_gate_on(true, 2, 1, false), "the hook not armed: off");

    if (passed) *passed = g_pass;
    return g_fail;
}
