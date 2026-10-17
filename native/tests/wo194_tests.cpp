// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-194: engine-free checks of the shared in-game cutscene's native rules (wo194_rules.h). Linked into KCDMP_NativeTests.
#include <cstdio>
#include <string_view>

#include "wo194_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo194_tests(int* passed) {
    using namespace wo194rules;
    // B3: only the joiner's own in-game enqueue is held, only while armed, never the mod's start, never when the partner already plays it
    WCHECK(hold_wanted(kRoleJoiner, true, true, false, false), "the joiner's own enqueue, armed: held");
    WCHECK(!hold_wanted(kRoleHost, true, true, false, false), "the host never waits on the partner");
    WCHECK(!hold_wanted(kRoleUnknown, true, true, false, false), "no role known: never held");
    WCHECK(!hold_wanted(kRoleJoiner, false, true, false, false), "not armed (no partner / switch off): not held");
    WCHECK(!hold_wanted(kRoleJoiner, true, false, false, false), "not an in-game cutscene: not held");
    WCHECK(!hold_wanted(kRoleJoiner, true, true, true, false), "the mod's own start (B2): plays at once");
    WCHECK(!hold_wanted(kRoleJoiner, true, true, false, true), "the partner already plays it (the 0.60.9 seminArrival 7.5 s case): plays at once, aligned");
    // the bound and the order of reasons
    WCHECK(release_due(1000, 3999, kHoldBoundMs, Release::None, false) == Release::None, "2.999 s: still held");
    WCHECK(release_due(1000, 4000, kHoldBoundMs, Release::None, false) == Release::Bound, "3 s: plays solo");
    WCHECK(release_due(1000, 1001, kHoldBoundMs, Release::Partner, false) == Release::Partner, "the host's copy runs: released at once");
    WCHECK(release_due(1000, 1001, kHoldBoundMs, Release::Partner, true) == Release::Save, "a save request wins (never held across a save)");
    WCHECK(release_due(1000, 9000, kHoldBoundMs, Release::None, true) == Release::Save, "a save request past the bound is still named a save");
    WCHECK(release_due(1000, 1001, kHoldBoundMs, Release::Agent, false) == Release::Agent, "a menu / load / the host not at that step");
    WCHECK(kHoldBoundMs == 3000, "the WO's 3 s");
    // how a release is done
    WCHECK(release_act(false, true, false, 2) == Act::Gone, "the handler no longer holds the configuration: nothing written");
    WCHECK(release_act(true, true, true, 3) == Act::AlreadyPlaying, "already in progress");
    WCHECK(release_act(true, true, false, 2) == Act::Pulse, "initialized and waiting: the handler's PlayCutscene input");
    WCHECK(release_act(true, false, false, 1) == Act::RestoreOnly, "still queued: AutoPlay back only");
    WCHECK(release_act(true, true, false, 1) == Act::RestoreOnly, "active but not yet initialized: AutoPlay back only");
    WCHECK(recheck_stuck(true, false) && !recheck_stuck(true, true) && !recheck_stuck(false, false), "the re-check plays only an active, idle configuration");
    WCHECK(kRecheckMs[0] == 1500 && kRecheckMs[1] == 4000, "re-checks at 1.5 s and 4 s");
    // D: the time gate
    WCHECK(time_gate_wanted(kRoleJoiner, true, true, true), "the joiner's scene with its own time: the host's clock gives it");
    WCHECK(!time_gate_wanted(kRoleHost, true, true, true), "the host's scene sets the world's time");
    WCHECK(!time_gate_wanted(kRoleJoiner, true, true, false) && !time_gate_wanted(kRoleJoiner, false, true, true), "no own time / switch off");
    // A: the FastForward stop
    WCHECK(ff_stop_allowed(kRoleJoiner, true, 0x1234, 0x7777), "a copy on the joiner");
    WCHECK(!ff_stop_allowed(kRoleJoiner, true, 0x7777, 0x7777), "never the local player (his own positioning places him)");
    WCHECK(!ff_stop_allowed(kRoleHost, true, 0x1234, 0x7777), "never on the host (its positioning moves the real NPCs)");
    WCHECK(!ff_stop_allowed(kRoleJoiner, false, 0x1234, 0x7777) && !ff_stop_allowed(kRoleJoiner, true, 0, 0x7777), "switch off / no entity");
    WCHECK(std::string_view(release_name(Release::Save)).find("save") != std::string_view::npos, "release names");
    *passed = g_pass;
    return g_fail;
}
