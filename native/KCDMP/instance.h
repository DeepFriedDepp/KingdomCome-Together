// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-206 A.2: two copies of the game on one PC (the test bench) each need their own pipe and their own DLL log. The
// test rig sets KCDMP_INSTANCE in the game's environment; a player's game never has it, and then every name is the
// one it always was (\\.\pipe\kcdmp, kcdmp-native.log). An id is 1..24 characters of [A-Za-z0-9_-]; anything else is
// ignored (the default names), so a stray value can never build a path or a pipe name outside ours.
// Engine-free: native/tests/wo206_tests.cpp pins it.

#include <cstddef>
#include <cstdlib>
#include <string>

namespace kcdmp::instance {

constexpr size_t kMaxId = 24;

inline bool valid_id(const char* s) {
    if (!s || !*s) return false;
    size_t n = 0;
    for (; s[n]; ++n) {
        const char c = s[n];
        const bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
        if (!ok || n >= kMaxId) return false;
    }
    return true;
}

// The pipe for an id ("" or invalid = the players' name).
inline std::string pipe_name_for(const char* id) {
    return valid_id(id) ? std::string(R"(\\.\pipe\kcdmp-)") + id : std::string(R"(\\.\pipe\kcdmp)");
}

// The DLL log's file name for an id (it sits beside the DLL; two copies may load one DLL file).
inline std::string log_name_for(const char* id) {
    return valid_id(id) ? std::string("kcdmp-native-") + id + ".log" : std::string("kcdmp-native.log");
}

// This process's id, read once (the environment does not change under a running game).
inline const char* id() {
    static const std::string v = [] {
        char* e = nullptr;
        size_t n = 0;
        std::string s;
        if (_dupenv_s(&e, &n, "KCDMP_INSTANCE") == 0 && e) { s = e; std::free(e); }
        return valid_id(s.c_str()) ? s : std::string();
    }();
    return v.c_str();
}

inline const std::string& pipe_name() { static const std::string v = pipe_name_for(id()); return v; }

}  // namespace kcdmp::instance
