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
#include <cstdint>

namespace kcdmp {

inline std::mutex& log_mutex() { static std::mutex m; return m; }

// WO-177 A4: each injection starts a new native log, and the previous one is kept instead of
// overwritten (WO-176 lost a whole night's native log to the next injection). "<dir>\kcdmp-native.log"
// -> "<dir>\kcdmp-native.1.log" (the newest earlier one) .. ".5.log" (the oldest); a file named with
// the numbered form never gets another number. Pure string work, so the unit tests pin it.
constexpr int kLogKeep = 5;

inline std::string numbered_log_name(const std::string& path, int n) {
    const size_t dot = path.find_last_of('.');
    const size_t slash = path.find_last_of("\\/");
    if (dot == std::string::npos || (slash != std::string::npos && dot < slash))
        return path + "." + std::to_string(n);
    return path.substr(0, dot) + "." + std::to_string(n) + path.substr(dot);
}

// Shifts path -> .1 -> .2 ... -> .keep (the oldest is dropped) and returns how many earlier logs are
// kept now, or -1 when a step could not run (a file still held open by a game that is still running):
// then nothing is deleted that was not already the oldest, and the caller says so in the new log.
inline int rotate_log_files(const std::string& path, int keep = kLogKeep) {
    if (keep < 1) return 0;
    const auto exists = [](const std::string& f) {
        std::FILE* h = std::fopen(f.c_str(), "rb");
        if (h) std::fclose(h);
        return h != nullptr;
    };
    if (!exists(path)) return 0;   // nothing earlier to keep
    std::remove(numbered_log_name(path, keep).c_str());
    for (int i = keep - 1; i >= 1; --i) {
        const std::string from = numbered_log_name(path, i);
        if (exists(from) && std::rename(from.c_str(), numbered_log_name(path, i + 1).c_str()) != 0) return -1;
    }
    if (std::rename(path.c_str(), numbered_log_name(path, 1).c_str()) != 0) return -1;
    int kept = 0;
    for (int i = 1; i <= keep; ++i)
        if (exists(numbered_log_name(path, i))) ++kept;
    return kept;
}

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
    static int   rotated_primary = 0, rotated_mirror = 0;
    static bool  note_rotation = false;
    if (!opened) {
        opened = true;
        // _fsopen with _SH_DENYWR, not fopen: the game holds this handle for its
        // whole run, and fopen's default share mode makes the log unreadable
        // from outside until the process exits -- which defeats the point of
        // having a log at all.
        // WO-177 A4: the earlier logs are shifted aside first (the newest five kept), never truncated.
        const std::string lp = log_path();
        const std::string mp = mirror_log_path();
        rotated_primary = rotate_log_files(lp);
        if (!mp.empty()) rotated_mirror = rotate_log_files(mp);
        f = _fsopen(lp.c_str(), "w", _SH_DENYWR);
        if (!mp.empty()) mirror = _fsopen(mp.c_str(), "w", _SH_DENYWR);
        note_rotation = true;
    }
    if (!f && !mirror) return;
    SYSTEMTIME st{};
    GetLocalTime(&st);
    char prefix[48];
    format_log_prefix(prefix, sizeof prefix, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
                      static_cast<unsigned long>(GetCurrentThreadId()));
    if (note_rotation) {
        note_rotation = false;
        for (FILE* dst : { f, mirror }) {
            if (!dst) continue;
            fputs(prefix, dst);
            std::fprintf(dst, "LOG: earlier native logs kept as kcdmp-native.1..%d.log (primary %d kept%s, mirror %d kept%s)\n", kLogKeep,
                         rotated_primary < 0 ? 0 : rotated_primary, rotated_primary < 0 ? " -- NOT rotated: the earlier log was still open" : "",
                         rotated_mirror < 0 ? 0 : rotated_mirror, rotated_mirror < 0 ? " -- NOT rotated: the earlier log was still open" : "");
            fflush(dst);
        }
    }
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
