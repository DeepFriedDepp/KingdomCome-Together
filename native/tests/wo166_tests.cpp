// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-166: engine-free checks of the 0.48.2 native rules -- T2 (the quest direct write's runtime and type gates, with the field's
// 635 cases replayed), C3 (the copy's striking window), C5 (the lock-on's not-set lines per NPC per minute). Linked into
// KCDMP_NativeTests; wo166_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo137_rules.h"
#include "wo166_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo166_tests(int* passed) {
    using namespace wo137rules;
    // ---- T2: the runtime gate (ConceptModule: HibernateInternal writes 1, WakeInternal 0)
    WCHECK(runtime_loaded(0), "an awake State is written");
    WCHECK(runtime_loaded(1), "a hibernated State is loaded too: written (the field's 612 'asleep')");
    WCHECK(!runtime_loaded(-1), "an unread runtime (no answer) is refused");
    WCHECK(!runtime_loaded(2), "a runtime state this code has not read is refused");
    WCHECK(runtime_hibernating(1) && !runtime_hibernating(0), "the hibernated write is reported as such");

    // ---- T2: the type gate
    WCHECK(set_value_type_ok("int", -5) && set_value_type_ok("int", 3), "int: any value");
    WCHECK(set_value_type_ok("uint", 3), "uint (TypeT=\"uint\", the smith's kvalitaMece): a non-negative value");
    WCHECK(set_value_type_ok("unsigned int", 0), "rttr's long spelling of uint");
    WCHECK(!set_value_type_ok("uint", -1), "uint never takes a negative value");
    WCHECK(set_value_type_ok("bool", 0) && set_value_type_ok("bool", 1), "bool: 0 or 1");
    WCHECK(!set_value_type_ok("bool", 2), "bool: never 2");
    WCHECK(!set_value_type_ok("float", 1), "float stays refused (not an inline 32-bit integer write)");
    WCHECK(!set_value_type_ok("Progress", 1), "an enum stays refused (WO-164 guard: its range is not read)");
    WCHECK(!set_value_type_ok("", 1), "an empty type name is refused");
    WCHECK(!set_value_type_ok(nullptr, 1), "no type is refused");

    // ---- T2: the field's 635 cases through the new gates (counts from docs/WO-166-findings.md 0.3; no log line copied):
    // 612 refused 'asleep' = runtime 1 with type uint; 23 refused 'type-refused' = runtime 0 with type uint. Host value 3.
    {
        struct Case { int runtime; const char* type; int n; } cases[] = { { 1, "uint", 612 }, { 0, "uint", 23 } };
        int pass = 0, total = 0;
        for (const auto& c : cases) {
            total += c.n;
            if (runtime_loaded(c.runtime) && set_value_type_ok(c.type, 3)) pass += c.n;
        }
        WCHECK(total == 635, "the field's 635 cases");
        WCHECK(pass * 100 >= total * 90, "at least 90 %% of them pass the new gates (%d of %d)", pass, total);
        WCHECK(pass == 635, "all 635 pass");
    }

    // ---- C3: the striking window of a copy (wo166_rules.h)
    {
        using namespace wo166rules;
        // a row with its own start+hit lag: Striking from the row's start + attack_time_to_start until its hit + the hold
        const StrikeWindow w = strike_window(1000.0, 400, 300);
        WCHECK(w.valid && w.begin_ms == 1400.0 && w.end_ms == 1700.0 + kStrikeTailMs, "start 400 ms, hit 300 ms after: striking 1400..hit+tail");
        WCHECK(in_window(w, 1500.0) && !in_window(w, 1399.0) && !in_window(w, w.end_ms + 1), "inside / before / after the window");
        const StrikeWindow none = strike_window(1000.0, -1, -1);
        WCHECK(none.valid && none.begin_ms == 1000.0 + kStrikeDefaultStartMs, "a row without timings: the default start");
        WCHECK(!strike_window(1000.0, 5000, 300).valid, "a row whose start is longer than any swing is refused (not a timing)");
        WCHECK(strike_state_ok(2) && strike_state_ok(1), "a copy in Guard or Idle may be put into Striking");
        WCHECK(!strike_state_ok(0x40) && !strike_state_ok(0x80) && !strike_state_ok(0x100) && !strike_state_ok(0x200),
               "never over Hit, PreparingToParry, ParryInPlace or Dodge (the engine's own states)");
        WCHECK(restore_ok(8, 8) && !restore_ok(8, 0x40), "the prior state goes back only if Striking is still ours");
        WCHECK(attack_fields_ok(1, 2, 1.0f, 0) && !attack_fields_ok(-1, 2, 1.0f, 0) && !attack_fields_ok(1, 9, 1.0f, 0)
               && !attack_fields_ok(1, 2, 3.0f, 0) && !attack_fields_ok(1, 2, 1.0f, 5), "the four attack fields within the engine's ranges");
    }

    // ---- C5: the lock-on's not-set line, once per NPC per minute
    {
        using namespace wo166rules;
        LineLimiter lim;
        WCHECK(lim.allow(7, 0.0), "the first not-set line of an NPC is logged");
        WCHECK(!lim.allow(7, 30.0), "the same NPC 30 s later: suppressed");
        WCHECK(lim.allow(8, 30.0), "another NPC: logged");
        WCHECK(lim.allow(7, 60.5), "the same NPC a minute later: logged");
        WCHECK(lim.suppressed() == 1, "the suppressed count");
        int logged = 0;
        LineLimiter lim2;
        for (int i = 0; i < 417; ++i) if (lim2.allow(42, i * 0.25)) ++logged;   // the field's 417 lines, 4 a second, one NPC
        WCHECK(logged == 2, "the field's 417 lines in 104 s become 2 (%d)", logged);
    }

    if (passed) *passed = g_pass;
    return g_fail;
}
