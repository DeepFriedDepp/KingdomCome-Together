// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-206: engine-free checks of the per-copy names (instance.h). Linked into KCDMP_NativeTests; wo206_tests() returns
// the number of failures.
#include <cstdio>
#include <string>

#include "instance.h"
#include "wo206_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo206_tests(int* passed) {
    using namespace instance;
    WCHECK(pipe_name_for(nullptr) == R"(\\.\pipe\kcdmp)" && pipe_name_for("") == R"(\\.\pipe\kcdmp)", "no id: the players' pipe");
    WCHECK(log_name_for("") == "kcdmp-native.log", "no id: the players' log name");
    WCHECK(pipe_name_for("B") == R"(\\.\pipe\kcdmp-B)" && pipe_name_for("rig_a-2") == R"(\\.\pipe\kcdmp-rig_a-2)", "an id: its own pipe");
    WCHECK(log_name_for("B") == "kcdmp-native-B.log", "an id: its own log");
    WCHECK(pipe_name_for("a\\b") == R"(\\.\pipe\kcdmp)" && pipe_name_for("..") == R"(\\.\pipe\kcdmp)" && pipe_name_for("a b") == R"(\\.\pipe\kcdmp)",
           "a path or a space is not an id (the default name)");
    WCHECK(valid_id("abcdefghijklmnopqrstuvwx") && !valid_id("abcdefghijklmnopqrstuvwxy"), "24 characters at most");
    WCHECK(pipe_name_for("A") != pipe_name_for("B") && log_name_for("A") != log_name_for("B"), "two copies never share a name");

    // ---- C: the digest sweep
    {
        using namespace wo206rules;
        Sweep s;
        size_t first = 0, seen = 0;
        size_t n = sweep_take(s, 250, 120, 10.0, 2.0, &first);
        WCHECK(n == 120 && first == 0, "a pass starts at the top, a frame's share");
        seen += n;
        n = sweep_take(s, 250, 120, 10.02, 2.0, &first); seen += n;
        n = sweep_take(s, 250, 120, 10.04, 2.0, &first); seen += n;
        WCHECK(seen == 250 && s.passes == 1 && !s.inPass && first == 240, "every entry once a pass (%zu, %u)", seen, s.passes);
        WCHECK(sweep_take(s, 250, 120, 11.0, 2.0, &first) == 0, "the next pass waits for the gap");
        WCHECK(sweep_take(s, 250, 120, 12.1, 2.0, &first) == 120 && first == 0, "then starts again");
        WCHECK(sweep_take(s, 0, 120, 99.0, 2.0, &first) == 0, "nothing registered, nothing read");
        Entry e;
        entry_apply(e, true, 3, 7);
        WCHECK(e.dirty, "the first read is reported");
        e.dirty = false; entry_apply(e, true, 3, 7);
        WCHECK(!e.dirty, "an unchanged value is not");
        entry_apply(e, true, 3, 8);
        WCHECK(e.dirty, "a changed value is");
        e.dirty = false; entry_apply(e, false, 3, 8);
        WCHECK(e.dirty, "a node gone is a change");
        // twin: an entry that compares only the value misses a node that vanished with the same value
        WCHECK(!(e.value != 8), "twin: the value alone would not have told");
    }
    // ---- D: the probe's text
    {
        using namespace wo206rules;
        char o[64];
        json_escape("a\"b\\c\n", o, sizeof o);
        WCHECK(std::string(o) == "a\\\"b\\\\c\\u000a", "names are escaped (%s)", o);
        WCHECK(ragdoll_class("C_RagdollAction") && !ragdoll_class("CAnimActionAIMovement") && !ragdoll_class(nullptr), "the ragdoll bit");
        WCHECK(std::string(machine_name(1)) == "host" && std::string(machine_name(2)) == "joiner" && std::string(machine_name(0)) == "solo",
               "the machine word");
    }
    *passed = g_pass;
    return g_fail;
}
