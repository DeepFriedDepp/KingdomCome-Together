// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// Deliberately not using the engine's logger. At the point this DLL loads we
// have not resolved anything yet, and a logger that depends on the thing we are
// diagnosing is useless. Plain file, flushed every line, next to the DLL.

#include <windows.h>
#include <share.h>
#include <cstdio>
#include <cstdarg>
#include <mutex>
#include <string>

namespace kcdmp {

inline std::mutex& log_mutex() { static std::mutex m; return m; }

inline std::string log_path() {
    char buf[MAX_PATH]{};
    HMODULE self = nullptr;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                       GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCSTR>(&log_mutex), &self);
    GetModuleFileNameA(self, buf, MAX_PATH);
    std::string p(buf);
    const size_t slash = p.find_last_of("\\/");
    if (slash != std::string::npos) p.resize(slash + 1);
    return p + "kcdmp-native.log";
}

// WO-45: a second copy of every line, written into the game process's working
// directory (where the game writes kcd.log). The primary log sits beside the
// DLL under %LocalAppData%\KCDMP, and reads of that path from the coding shell
// return silently stale snapshots (the sandbox redirection WO-43 §7 hit) --
// the game's own directory does not have that problem. The DLL runs inside
// the game process, so it can write there directly.
inline std::string mirror_log_path() {
    char cwd[MAX_PATH]{};
    if (!GetCurrentDirectoryA(MAX_PATH, cwd) || !cwd[0]) return {};
    std::string p(cwd);
    if (p.back() != '\\') p += '\\';
    return p + "kcdmp-native.mirror.log";
}

// WO-148: every line carries the id of the thread that wrote it, in one fixed
// column right after the time:
//   [HH:MM:SS.mmm] tNNNNNN text
// six digits, zero-padded (Windows thread ids are far below a million in
// practice; a larger one would widen its own line only). The text starts at
// column 23 (kLogPrefixLen). Parsers that search for a substring are unaffected;
// one that needs the thread reads the seven characters after the "] ".
constexpr int kLogPrefixLen = 23;

// Writes the prefix (no terminator past it) into out[0..kLogPrefixLen) and returns
// the count written. Pure, so the native unit tests can pin the column.
inline int format_log_prefix(char* out, size_t cap, int hour, int minute, int second, int ms,
                             unsigned long tid) {
    return std::snprintf(out, cap, "[%02d:%02d:%02d.%03d] t%06lu ", hour, minute, second, ms, tid);
}

inline void logf(const char* fmt, ...) {
    std::lock_guard<std::mutex> lock(log_mutex());
    static FILE* f = nullptr;
    static FILE* mirror = nullptr;
    static bool  opened = false;
    if (!opened) {
        opened = true;
        // _fsopen with _SH_DENYWR, not fopen: the game holds this handle for its
        // whole run, and fopen's default share mode makes the log unreadable
        // from outside until the process exits -- which defeats the point of
        // having a log at all.
        f = _fsopen(log_path().c_str(), "w", _SH_DENYWR);
        const std::string mp = mirror_log_path();
        if (!mp.empty()) mirror = _fsopen(mp.c_str(), "w", _SH_DENYWR);
    }
    if (!f && !mirror) return;
    SYSTEMTIME st{};
    GetLocalTime(&st);
    char prefix[48];
    format_log_prefix(prefix, sizeof prefix, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
                      static_cast<unsigned long>(GetCurrentThreadId()));
    va_list args;
    for (FILE* dst : { f, mirror }) {
        if (!dst) continue;
        fputs(prefix, dst);
        va_start(args, fmt);
        vfprintf(dst, fmt, args);
        va_end(args);
        fputc('\n', dst);
        fflush(dst);
    }
}

} // namespace kcdmp
