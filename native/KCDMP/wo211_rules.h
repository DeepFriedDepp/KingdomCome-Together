// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-211: the pure rules of the native half (engine-free: native/tests check them).
#include <cctype>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo211rules {

// ---- the agent's switch word (op 143), the same bits as Wo211Rules.ConfigBits; every bit on until the agent says otherwise ----
constexpr uint32_t kBitUiLuaMainOnly = 0x01;      // B   mp_ui_lua_main_only: no Lua UI dispatch off the main thread
constexpr uint32_t kBitOverflowReseat = 0x02;     // C.3 mp_overflow_reseat: an over-full copy is reseated the clean way
constexpr uint32_t kBitHandoverQueueCap = 0x04;   // C.2 (a fix, twin handover_queue_cap): the hand-over never queues behind a long queue
constexpr uint32_t kBitsDefault = kBitUiLuaMainOnly | kBitOverflowReseat | kBitHandoverQueueCap;

// The bench's twin names (KCDMP_TEST_W211_OFF=name,name).
inline uint32_t twin_off_bits(const char* list) {
    struct N { const char* n; uint32_t b; };
    static const N k[] = {{"ui_lua_main_only", kBitUiLuaMainOnly}, {"overflow_reseat", kBitOverflowReseat}, {"handover_queue_cap", kBitHandoverQueueCap}};
    uint32_t b = 0;
    if (!list) return 0;
    const char* p = list;
    while (*p) {
        while (*p == ',' || *p == ' ') ++p;
        const char* s = p;
        while (*p && *p != ',' && *p != ' ') ++p;
        const size_t n = static_cast<size_t>(p - s);
        for (const N& e : k)
            if (n == std::strlen(e.n) && std::strncmp(s, e.n, n) == 0) b |= e.b;
    }
    return b;
}

// B: what the guard does with one element event dispatched to Lua. Off the main thread it never runs (unless the twin lets it):
// the engine's own dispatch would build a Lua table there whether or not a listener matches.
enum class Ui : uint8_t { Run = 0, Skip = 1, LetThrough = 2 };
inline Ui ui_dispatch(bool mainKnown, bool onMain, bool fixOn) {
    if (!mainKnown || onMain) return Ui::Run;
    return fixOn ? Ui::Skip : Ui::LetThrough;
}

// B: would a registered listener take this event? The engine's own test (CryAction's element-listener dispatch): the listener's element
// name empty or equal, its event name empty or equal (case-insensitive here: a match counted too often is only a count), its instance
// -1 or equal.
inline bool ieq(const char* a, const char* b) {
    if (!a || !b) return false;
    for (; *a && *b; ++a, ++b)
        if (std::tolower(static_cast<unsigned char>(*a)) != std::tolower(static_cast<unsigned char>(*b))) return false;
    return *a == *b;
}
inline bool listener_matches(const char* cbElement, const char* cbEvent, int32_t cbInstance, const char* element, const char* event, int32_t instance) {
    if (cbElement && *cbElement && !(element && std::strcmp(cbElement, element) == 0)) return false;
    if (cbEvent && *cbEvent && !ieq(cbEvent, event)) return false;
    return cbInstance == -1 || cbInstance == instance;
}

// C.3: an over-full copy is reseated at most once per this many seconds (the engine prints its line every queue attempt).
constexpr double kOverflowReseatEveryS = 2.0;

// 0.2: a build id is [0-9A-Za-z.-]{1,40}; anything else reads as "dev".
inline bool build_id_ok(const char* s) {
    if (!s || !*s) return false;
    size_t n = 0;
    for (; s[n]; ++n) {
        const char c = s[n];
        if (n >= 40 || !(std::isalnum(static_cast<unsigned char>(c)) || c == '.' || c == '-')) return false;
    }
    return true;
}

} // namespace kcdmp::wo211rules
