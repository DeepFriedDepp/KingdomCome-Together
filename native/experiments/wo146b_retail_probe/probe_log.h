#pragma once
// WO-146B research probe -- logging.
//
// Every line carries the thread id: the whole point of the probe is to say
// WHICH thread the engine runs a given callback on, and the shipped plugin's
// log (docs/WO-145 s2.4) cannot answer that today.
//
// Plain file, flushed every line, opened with _SH_DENYWR so it can be read
// from outside while the game holds it. The file is written NEXT TO THE DLL,
// which for this probe lives outside both game installs; unlike the shipped
// plugin's logger there is deliberately NO mirror into the process working
// directory, because that directory is the retail install and this work order
// writes nothing inside it.

#include <windows.h>
#include <share.h>
#include <cstdio>
#include <cstdarg>
#include <mutex>
#include <string>

namespace wo146b {

inline std::mutex& log_mutex() { static std::mutex m; return m; }

inline std::string log_path() {
    char buf[MAX_PATH]{};
    HMODULE self = nullptr;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                       GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCSTR>(&log_mutex), &self);
    GetModuleFileNameA(self, buf, MAX_PATH);
    std::string p(buf);
    const size_t dot = p.find_last_of('.');
    if (dot != std::string::npos) p.resize(dot);
    return p + ".log";
}

inline void logf(const char* fmt, ...) {
    std::lock_guard<std::mutex> lock(log_mutex());
    static FILE* f = nullptr;
    static bool opened = false;
    if (!opened) {
        opened = true;
        f = _fsopen(log_path().c_str(), "w", _SH_DENYWR);
    }
    if (!f) return;
    SYSTEMTIME st{};
    GetLocalTime(&st);
    fprintf(f, "[%02d:%02d:%02d.%03d][tid=%5lu] ",
            st.wHour, st.wMinute, st.wSecond, st.wMilliseconds, GetCurrentThreadId());
    va_list args;
    va_start(args, fmt);
    vfprintf(f, fmt, args);
    va_end(args);
    fputc('\n', f);
    fflush(f);
}

} // namespace wo146b
