// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-137: the engine-free rules of the shared-quests native half (wo137.cpp).
// native/tests/wo137_rules_tests.cpp pins them.

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo137rules {

// A quest time set: a C_Function node whose method is wh::rpgmodule::AdvanceWorldTime or
// wh::conceptmodule::PassLongTime (the census's 179 + 5 nodes). The name is matched as the
// last `::` segment exactly, so a method that merely ends in the same letters is not one.
inline bool is_time_method(const char* m) {
    if (!m) return false;
    const char* names[] = { "AdvanceWorldTime", "PassLongTime" };
    const size_t n = std::strlen(m);
    for (const char* t : names) {
        const size_t k = std::strlen(t);
        if (n >= k && std::strcmp(m + n - k, t) == 0 && (n == k || m[n - k - 1] == ':')) return true;
    }
    return false;
}

// Quest States live under Barbora.<region>.<quest>...: anything else (Haste, test modules) is foreign.
inline bool is_barbora_path(const char* p) {
    return p && std::strncmp(p, "Barbora.", 8) == 0 && p[8] != 0;
}

// The agent re-sends its config every 10 s; the native side logs a config only when this key changes.
inline int config_key(bool on, uint8_t role, bool timeGate) {
    return (on ? 1 : 0) | (static_cast<int>(role) << 1) | (timeGate ? 0x100 : 0);
}

// The joiner's time gate is on only for an active joiner whose agent asked for it, with the hook armed.
inline bool time_gate_on(bool on, uint8_t role, uint8_t cfgFlags, bool armed) {
    return on && role == 2 && (cfgFlags & 1) != 0 && armed;
}

// WO-139: a node of the open-world punishment (IPL/Scripts Quests/Final/Barbora/open_world/
// nextnextgenpunishment: the fast travel's AdvanceWorldTime 10h, the second arrest's 9h).
// The path may carry a database segment above Barbora ("brambora.Barbora..."): matched anywhere.
inline bool is_punishment_path(const char* p) {
    if (!p) return false;
    const char* key = "Barbora.open_world.nextnextgenpunishment";
    const char* at = std::strstr(p, key);
    if (!at) return false;
    const char end = at[std::strlen(key)];
    return end == 0 || end == '.';
}

} // namespace kcdmp::wo137rules
