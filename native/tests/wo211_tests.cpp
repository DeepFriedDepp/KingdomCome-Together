// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-211: engine-free checks of wo211_rules.h. Linked into KCDMP_NativeTests; wo211_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo211_rules.h"
#include "wo210_rules.h"
#include "hook_prologues.h"

using namespace kcdmp::wo211rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo211_tests(int* passed) {
    // ---- the switch word and the bench's twin names
    WCHECK((kBitsDefault & kBitUiLuaMainOnly) && (kBitsDefault & kBitOverflowReseat) && (kBitsDefault & kBitHandoverQueueCap), "every WO-211 switch ships on");
    WCHECK(twin_off_bits("ui_lua_main_only") == kBitUiLuaMainOnly, "B's twin");
    WCHECK(twin_off_bits("x, overflow_reseat,handover_queue_cap") == (kBitOverflowReseat | kBitHandoverQueueCap), "C's twins, spaces and unknown names");
    WCHECK(twin_off_bits("ui_lua") == 0 && twin_off_bits(nullptr) == 0 && twin_off_bits("") == 0, "only a whole name");

    // ---- B: the dispatch runs on the main thread only (the field host's render-thread release)
    WCHECK(ui_dispatch(true, true, true) == Ui::Run && ui_dispatch(true, true, false) == Ui::Run, "the main thread always runs it");
    WCHECK(ui_dispatch(true, false, true) == Ui::Skip, "the render thread (or any other) never runs it");
    WCHECK(ui_dispatch(true, false, false) == Ui::LetThrough, "the twin lets it through (the old behaviour, counted)");
    WCHECK(ui_dispatch(false, false, true) == Ui::Run, "before the first frame the main thread is not known: the engine's own way");
    WCHECK(listener_matches("ApseMap", "OnDoubleClicked", -1, "ApseMap", "OnDoubleClicked", 0), "the mod's map listener takes its own event");
    WCHECK(!listener_matches("ApseMap", "OnDoubleClicked", -1, "ApseMap", "OnUnload", 0), "an element's unload is no listener's of the mod");
    WCHECK(!listener_matches("ItemTransfer", "OnClosed", -1, "ApseMap", "OnClosed", 0), "another element");
    WCHECK(listener_matches("", "", -1, "Anything", "OnHide", 3) && listener_matches("Menu", "onbutton", -1, "Menu", "OnButton", 1), "empty names take all; events case-blind");
    WCHECK(!listener_matches("Menu", "OnButton", 2, "Menu", "OnButton", 1) && listener_matches("Menu", "OnButton", 1, "Menu", "OnButton", 1), "an instance");
    WCHECK(!listener_matches("Menu", "OnButton", -1, nullptr, "OnButton", 0) && !listener_matches("Menu", "OnButton", -1, "Menu", nullptr, 0), "unreadable names never match");

    // ---- B: the gate's prologue is on the registry (the x64 decoder checks its boundary with the others)
    bool listed = false;
    for (const auto& e : kcdmp::hookpro::kAll)
        if (e.bytes == kcdmp::hookpro::kUiElementLuaNotify && e.len == 14) listed = true;
    WCHECK(listed, "the element-listener dispatch's 14 bytes are on the prologue registry");

    // ---- C.3 and the paths
    WCHECK(kOverflowReseatEveryS >= 1.0 && kOverflowReseatEveryS <= 5.0, "an over-full copy is reseated at most every couple of seconds");
    WCHECK(kcdmp::wo210rules::kPathOverflow + 1 == kcdmp::wo210rules::kPathCount &&
           std::strcmp(kcdmp::wo210rules::path_name(kcdmp::wo210rules::kPathOverflow), "18-overflow") == 0, "the overflow reseat has its own path");

    // ---- 0.2: build ids
    WCHECK(build_id_ok("d4f2cb4-2610300412") && build_id_ok("dev") && build_id_ok("a.b-1"), "a short commit and a time");
    WCHECK(!build_id_ok("") && !build_id_ok(nullptr) && !build_id_ok("a b") && !build_id_ok("x\"y") && !build_id_ok("0123456789012345678901234567890123456789X"),
           "nothing else reads as one");

    *passed = g_pass;
    return g_fail;
}
