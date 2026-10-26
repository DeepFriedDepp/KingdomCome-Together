// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-202 I.1 crash_capture (crash_capture.h): the chained unhandled-exception filter and its writer thread.   WO202-CRASH.
#include "crash_capture.h"
#include "crash_rules.h"
#include "engine.h"
#include "fault_guard.h"
#include "inline_hook.h"
#include "log.h"
#include "main_thread.h"
#include "motion.h"
#include "x64_len.h"

#include <windows.h>
#include <dbghelp.h>
#include <psapi.h>
#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <cstring>

namespace kcdmp::crash {

namespace {

using namespace crashrules;

std::atomic<bool> g_on{true};
std::atomic<bool> g_installed{false};
std::atomic<LPTOP_LEVEL_EXCEPTION_FILTER> g_prev{nullptr};
std::atomic<long> g_busy{0};
HANDLE g_go = nullptr, g_done = nullptr;
DWORD g_writerTid = 0;
EXCEPTION_POINTERS* volatile g_ep = nullptr;
volatile DWORD g_crashTid = 0;
char g_root[MAX_PATH] = "";
char g_dir[MAX_PATH] = "";
uint64_t g_t0 = 0;
std::atomic<uint32_t> g_reasserts{0}, g_written{0};
thread_local bool t_inFilter = false;
std::atomic<bool> g_testCrash{false};
bool g_testChain = false;
bool g_sufBlocked = false;     // SetUnhandledExceptionFilter returns without installing (the game's reporter keeps its filter)
bool g_uefHooked = false;      // kernelbase!UnhandledExceptionFilter detoured
const char* g_uefWhy = "not tried";
using UefFn = LONG(WINAPI*)(EXCEPTION_POINTERS*);
UefFn g_uefOrig = nullptr;
std::atomic<bool> g_testMain{false};

fault::Site g_readSite{"crash_capture::named_read", fault::Kind::Read};   // WO-207 crash work: the register chains' reads
using MdwFn = BOOL(WINAPI*)(HANDLE, DWORD, HANDLE, MINIDUMP_TYPE, PMINIDUMP_EXCEPTION_INFORMATION, PMINIDUMP_USER_STREAM_INFORMATION,
                            PMINIDUMP_CALLBACK_INFORMATION);
MdwFn g_mdw = nullptr;

// ---- the module table (a crash must not take the loader lock to name an address: refreshed on the main thread, double-buffered) --
struct Mod { uintptr_t base, end; char name[64]; };
constexpr int kMaxMods = 768;
Mod g_mods[2][kMaxMods];
int g_modN[2] = {0, 0};
std::atomic<int> g_modCur{0};

void refresh_modules() {
    const int next = 1 - g_modCur.load();
    static HMODULE hs[kMaxMods];
    DWORD need = 0;
    if (!K32EnumProcessModules(GetCurrentProcess(), hs, sizeof hs, &need)) return;
    int n = static_cast<int>(need / sizeof(HMODULE));
    if (n > kMaxMods) n = kMaxMods;
    int k = 0;
    for (int i = 0; i < n; ++i) {
        MODULEINFO mi{};
        if (!K32GetModuleInformation(GetCurrentProcess(), hs[i], &mi, sizeof mi)) continue;
        Mod& m = g_mods[next][k];
        m.base = reinterpret_cast<uintptr_t>(mi.lpBaseOfDll);
        m.end = m.base + mi.SizeOfImage;
        if (!K32GetModuleBaseNameA(GetCurrentProcess(), hs[i], m.name, sizeof m.name)) std::strcpy(m.name, "?");
        ++k;
    }
    g_modN[next] = k;
    g_modCur.store(next);
}

const Mod* module_of(uintptr_t a) {
    const int c = g_modCur.load();
    for (int i = 0; i < g_modN[c]; ++i)
        if (a >= g_mods[c][i].base && a < g_mods[c][i].end) return &g_mods[c][i];
    return nullptr;
}

void addr_text(uintptr_t a, char* out, size_t cap) {
    if (const Mod* m = module_of(a)) std::snprintf(out, cap, "%s+0x%llx", m->name, static_cast<unsigned long long>(a - m->base));
    else std::snprintf(out, cap, "0x%llx", static_cast<unsigned long long>(a));
}

bool is_mod_addr(uintptr_t a) { const Mod* m = module_of(a); return m && is_mod_module(m->name); }

// ---- the last pipe requests ----
struct PipeNote { uint64_t at; uint8_t type, op; uint16_t len; };
constexpr int kPipeRing = 64;
PipeNote g_pipe[kPipeRing];
std::atomic<long> g_pipeNext{0};

// ---- the report text (static: the heap may be what broke) ----
char g_txt[320 * 1024];
size_t g_len = 0;
void put(const char* fmt, ...) {
    if (g_len + 2 >= sizeof g_txt) return;
    va_list a;
    va_start(a, fmt);
    const int n = std::vsnprintf(g_txt + g_len, sizeof g_txt - g_len, fmt, a);
    va_end(a);
    if (n > 0) g_len = (g_len + static_cast<size_t>(n) < sizeof g_txt - 1) ? g_len + static_cast<size_t>(n) : sizeof g_txt - 1;
}

bool write_file(const char* path, const void* data, size_t n) {
    HANDLE f = CreateFileA(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (f == INVALID_HANDLE_VALUE) return false;
    DWORD w = 0;
    const BOOL ok = WriteFile(f, data, static_cast<DWORD>(n), &w, nullptr);
    CloseHandle(f);
    return ok && w == n;
}

const char* access_word(ULONG_PTR a) { return a == 0 ? "reading" : a == 1 ? "writing" : a == 8 ? "executing" : "touching"; }

// The faulting thread's frames: RtlVirtualUnwind where the code has unwind data, the thunk's known layout in a hook page, a leaf
// return otherwise. Stops at a trampoline, a frame that does not move up, or 64 frames.
int unwind(CONTEXT ctx, uintptr_t* rips, FrameKind* kinds, int maxN) {
    int n = 0;
    while (n < maxN) {
        const uintptr_t rip = static_cast<uintptr_t>(ctx.Rip), rsp = static_cast<uintptr_t>(ctx.Rsp);
        if (!rip) break;
        const int h = inlinehook::hook_of_page(rip);
        inlinehook::HookRec hr{};
        if (h >= 0) inlinehook::hook_at(h, &hr);
        rips[n] = rip;
        kinds[n] = classify(is_mod_addr(rip), h >= 0, h >= 0 ? rip - hr.page : 0, inlinehook::kThunkAt);
        ++n;
        if (h >= 0) {
            if (hr.detour || rip - hr.page < inlinehook::kThunkAt + kThunkCallEnd) break;
            uint64_t ret = 0;
            if (!fault::guarded_silent([&] { std::memcpy(&ret, reinterpret_cast<const void*>(rsp + kThunkRetAt), 8); })) break;
            ctx.Rip = ret;
            ctx.Rsp = rsp + kThunkFrame;
            continue;
        }
        DWORD64 imageBase = 0;
        PRUNTIME_FUNCTION fe = nullptr;
        fault::guarded_silent([&] { fe = RtlLookupFunctionEntry(rip, &imageBase, nullptr); });
        if (!fe) {
            uint64_t ret = 0;
            if (!fault::guarded_silent([&] { std::memcpy(&ret, reinterpret_cast<const void*>(rsp), 8); })) break;
            ctx.Rip = ret;
            ctx.Rsp = rsp + 8;
        } else {
            void* hd = nullptr;
            DWORD64 est = 0;
            if (!fault::guarded_silent([&] { RtlVirtualUnwind(UNW_FLAG_NHANDLER, imageBase, rip, fe, &ctx, &hd, &est, nullptr); })) break;
        }
        if (static_cast<uintptr_t>(ctx.Rsp) <= rsp) break;
    }
    return n;
}

bool executable(uintptr_t a) {
    MEMORY_BASIC_INFORMATION mbi{};
    if (!VirtualQuery(reinterpret_cast<const void*>(a), &mbi, sizeof mbi) || mbi.State != MEM_COMMIT) return false;
    return (mbi.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;
}

void write_report() {
    EXCEPTION_POINTERS* ep = g_ep;
    const DWORD tid = g_crashTid;
    SYSTEMTIME st{};
    GetLocalTime(&st);
    char name[80];
    folder_name(name, sizeof name, st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, GetCurrentProcessId());
    CreateDirectoryA(g_root, nullptr);
    std::snprintf(g_dir, sizeof g_dir, "%s\\%s", g_root, name);
    CreateDirectoryA(g_dir, nullptr);
    g_len = 0;

    const EXCEPTION_RECORD* r = ep ? ep->ExceptionRecord : nullptr;
    const uint32_t code = r ? static_cast<uint32_t>(r->ExceptionCode) : 0;
    const uintptr_t at = r ? reinterpret_cast<uintptr_t>(r->ExceptionAddress) : 0;
    char where[160];
    addr_text(at, where, sizeof where);
    const unsigned long mainTid = main_thread::main_thread_id();
    put("Kingdom Come: Together -- crash report (WO-202 crash_capture)\r\n");
    put("time:      %04d-%02d-%02d %02d:%02d:%02d.%03d (this machine's clock)\r\n", st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute,
        st.wSecond, st.wMilliseconds);
    put("uptime:    %.1f s since the mod was injected\r\n", (GetTickCount64() - g_t0) / 1000.0);
    put("exception: 0x%08X %s", code, exception_name(code));
    if (r && code == 0xC0000005u && r->NumberParameters >= 2)
        put(" %s 0x%llx", access_word(r->ExceptionInformation[0]), static_cast<unsigned long long>(r->ExceptionInformation[1]));
    put("\r\nfaulting:  %s%s\r\n", where, is_mod_addr(at) ? "  <- the mod's code" : "");
    put("thread:    %lu (%s)\r\n", tid, tid == mainTid ? "the game's main thread" : mainTid ? "not the main thread" : "the main thread unknown");
    {
        HMODULE self = nullptr;
        GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCSTR>(&write_report), &self);
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(self);
        const auto* nt = dos ? reinterpret_cast<const IMAGE_NT_HEADERS*>(reinterpret_cast<const uint8_t*>(self) + dos->e_lfanew) : nullptr;
        char me[96];
        addr_text(reinterpret_cast<uintptr_t>(self), me, sizeof me);
        put("mod dll:   %s timestamp 0x%08lX size 0x%lX (matches the build's .pdb)\r\n", me, nt ? nt->FileHeader.TimeDateStamp : 0ul,
            nt ? nt->OptionalHeader.SizeOfImage : 0ul);
    }

    // the stack
    uintptr_t rips[64];
    FrameKind kinds[64];
    int nf = 0;
    if (ep && ep->ContextRecord) nf = unwind(*ep->ContextRecord, rips, kinds, 64);
    int modFrames = 0, hookFrames = 0;
    put("\r\nthe faulting thread's stack (unwound):\r\n");
    for (int i = 0; i < nf; ++i) {
        char a[160];
        addr_text(rips[i], a, sizeof a);
        if (kinds[i] == FrameKind::HookThunk || kinds[i] == FrameKind::HookTrampoline) {
            ++hookFrames;
            inlinehook::HookRec hr{};
            inlinehook::hook_at(inlinehook::hook_of_page(rips[i]), &hr);
            char t[160];
            addr_text(hr.target, t, sizeof t);
            put("  #%02d %s%s of %s\r\n", i, a, frame_tag(kinds[i]), t);
        } else {
            if (kinds[i] == FrameKind::Mod) ++modFrames;
            put("  #%02d %s%s\r\n", i, a, frame_tag(module_of(rips[i]) ? kinds[i] : FrameKind::Unknown));
        }
    }
    put("mod hooks on the stack: %s (%d frame(s) in the mod's code, %d in hook pages)\r\n",
        (modFrames + hookFrames) ? "YES" : "none found by the unwind", modFrames, hookFrames);

    // the scan: every value on that stack that points into the mod's code or a hook page
    if (ep && ep->ContextRecord) {
        const uintptr_t rsp = static_cast<uintptr_t>(ep->ContextRecord->Rsp);
        MEMORY_BASIC_INFORMATION mbi{};
        uintptr_t end = rsp;
        if (VirtualQuery(reinterpret_cast<const void*>(rsp), &mbi, sizeof mbi))
            end = reinterpret_cast<uintptr_t>(mbi.BaseAddress) + mbi.RegionSize;
        if (end > rsp + 96 * 1024) end = rsp + 96 * 1024;
        put("\r\nmod code addresses found in that stack (a scan: may include values left from earlier calls):\r\n");
        int found = 0;
        uintptr_t seen[48];
        int ns = 0;
        for (uintptr_t p = rsp & ~uintptr_t(7); p + 8 <= end && found < 48; p += 8) {
            uint64_t v = 0;
            if (!fault::guarded_silent([&] { std::memcpy(&v, reinterpret_cast<const void*>(p), 8); })) break;
            const uintptr_t a = static_cast<uintptr_t>(v);
            const bool hook = inlinehook::hook_of_page(a) >= 0;
            if (!hook && !is_mod_addr(a)) continue;
            if (!executable(a)) continue;
            bool dup = false;
            for (int i = 0; i < ns; ++i) dup = dup || seen[i] == a;
            if (dup) continue;
            if (ns < 48) seen[ns++] = a;
            char t[160];
            addr_text(a, t, sizeof t);
            put("  [rsp+0x%llx] %s%s\r\n", static_cast<unsigned long long>(p - rsp), t, hook ? "  (a hook page)" : "");
            ++found;
        }
        if (!found) put("  none\r\n");
        const CONTEXT& c = *ep->ContextRecord;
        put("\r\nregisters:\r\n  rip %016llx rsp %016llx rbp %016llx\r\n  rax %016llx rbx %016llx rcx %016llx rdx %016llx\r\n"
            "  rsi %016llx rdi %016llx r8  %016llx r9  %016llx\r\n  r10 %016llx r11 %016llx r12 %016llx r13 %016llx\r\n"
            "  r14 %016llx r15 %016llx\r\n",
            c.Rip, c.Rsp, c.Rbp, c.Rax, c.Rbx, c.Rcx, c.Rdx, c.Rsi, c.Rdi, c.R8, c.R9, c.R10, c.R11, c.R12, c.R13, c.R14, c.R15);
        // WO-207 crash work: the entities the registers lead to -- a register, the object it points to, that object's soul (+0x30, a
        // C_SkirmishSoul's) and the soul's entity (+0xD68) -- named. Reads and the entity's own GetName only, each guarded.
        put("\r\nentities the registers lead to (reg -> [reg] -> +0x30 -> +0xD68):\r\n");
        const char* rn[] = {"rax", "rbx", "rcx", "rdx", "rsi", "rdi", "r12", "r13", "r14", "r15"};
        const DWORD64 rv[] = {c.Rax, c.Rbx, c.Rcx, c.Rdx, c.Rsi, c.Rdi, c.R12, c.R13, c.R14, c.R15};
        auto q = [](uintptr_t a, uintptr_t* out) {
            if (a < 0x10000 || a > 0x00007FFFFFFFFFFFull || (a & 7)) return false;
            return fault::guarded(g_readSite, [&] { *out = *reinterpret_cast<const uintptr_t*>(a); });
        };
        int named = 0;
        for (int i = 0; i < 10; ++i) {
            uintptr_t chain[4] = {static_cast<uintptr_t>(rv[i]), 0, 0, 0};
            if (!q(chain[0], &chain[1])) continue;
            if (!q(chain[1] + 0x30, &chain[2])) chain[2] = 0;
            if (!chain[2] || !q(chain[2] + 0xD68, &chain[3])) chain[3] = 0;
            for (int k = 0; k < 4; ++k) {
                if (!chain[k]) continue;
                const char* nm = engine::entity_name(reinterpret_cast<void*>(chain[k]));
                if (nm && nm[0]) { put("  %s step %d (%016llx) is the entity '%.60s'\r\n", rn[i], k, static_cast<unsigned long long>(chain[k]), nm); ++named; }
            }
        }
        if (!named) put("  none\r\n");
    }

    // every hook placed
    const int nh = inlinehook::hook_count();
    put("\r\nthe mod's hooks (%d):\r\n", nh);
    for (int i = 0; i < nh; ++i) {
        inlinehook::HookRec hr{};
        if (!inlinehook::hook_at(i, &hr)) continue;
        char t[160], f[160];
        addr_text(hr.target, t, sizeof t);
        addr_text(hr.fn, f, sizeof f);
        put("  %s -> %s (%s)\r\n", t, f, hr.detour ? "detour" : "thunk");
    }

    // the last pipe requests
    {
        const long n = g_pipeNext.load();
        const uint64_t now = GetTickCount64();
        put("\r\nthe last pipe requests from the agent (newest last, seconds before the crash):\r\n");
        for (long i = n > kPipeRing ? n - kPipeRing : 0; i < n; ++i) {
            const PipeNote& p = g_pipe[i % kPipeRing];
            put("  -%.2fs type 0x%02X op %u len %u\r\n", (now - p.at) / 1000.0, p.type, p.op, p.len);
        }
    }

    // the last native log lines
    {
        const CrashRing& rg = crash_ring();
        const long n = rg.next;
        put("\r\nthe last %d native log lines:\r\n", n < kCrashRingLines ? static_cast<int>(n) : kCrashRingLines);
        for (long i = n > kCrashRingLines ? n - kCrashRingLines : 0; i < n; ++i) {
            char line[kCrashRingWidth + 1];
            std::memcpy(line, rg.line[i % kCrashRingLines], kCrashRingWidth);
            line[kCrashRingWidth] = 0;
            put("%s\r\n", line);
        }
    }
    char path[MAX_PATH + 32];
    std::snprintf(path, sizeof path, "%s\\crash.txt", g_dir);
    write_file(path, g_txt, g_len);

    // the minidump last (the slow part)
    bool dumped = false;
    if (g_mdw && ep) {
        std::snprintf(path, sizeof path, "%s\\crash.dmp", g_dir);
        HANDLE f = CreateFileA(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (f != INVALID_HANDLE_VALUE) {
            MINIDUMP_EXCEPTION_INFORMATION mei{};
            mei.ThreadId = tid;
            mei.ExceptionPointers = ep;
            mei.ClientPointers = FALSE;
            const MINIDUMP_TYPE type = static_cast<MINIDUMP_TYPE>(MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules |
                                                                  MiniDumpWithIndirectlyReferencedMemory | MiniDumpScanMemory |
                                                                  MiniDumpWithProcessThreadData);
            dumped = g_mdw(GetCurrentProcess(), GetCurrentProcessId(), f, type, &mei, nullptr, nullptr) != FALSE;
            CloseHandle(f);
        }
    }
    g_written.fetch_add(1);
    // a line in the native log too, when its lock is free (the crashing thread may hold it)
    for (int i = 0; i < 50; ++i) {
        if (log_mutex().try_lock()) {
            log_mutex().unlock();
            logf("WO202-CRASH 0x%08X %s at %s, thread %lu%s -- %s written (%s, crash.txt%s)", code, exception_name(code), where, tid,
                 tid == mainTid ? " (main)" : "", g_dir, name, dumped ? ", crash.dmp" : ", NO minidump");
            break;
        }
        Sleep(10);
    }
}

DWORD WINAPI writer_main(LPVOID) {
    for (;;) {
        WaitForSingleObject(g_go, INFINITE);
        fault::guarded_silent([] { write_report(); });
        SetEvent(g_done);
    }
}

// The report, once per process: the writer thread writes it while this (the crashing) thread waits.
void capture(EXCEPTION_POINTERS* ep) {
    if (!g_on.load() || GetCurrentThreadId() == g_writerTid || !g_go || !g_done) return;
    if (g_busy.exchange(1) == 0) {
        g_ep = ep;
        g_crashTid = GetCurrentThreadId();
        SetEvent(g_go);
    }
    WaitForSingleObject(g_done, 30000);   // manual-reset: a second crashing thread waits on the same report
}

// research only: a deliberate test crash ends here unless KCDMP_TEST_CRASH_CHAIN=1 -- the game's own crash reporter (and its upload
// dialog) is not run for a crash the harness made on purpose
bool test_ends_here() { return g_testCrash.load() && !g_testChain; }

LONG WINAPI filter(EXCEPTION_POINTERS* ep) {
    const LPTOP_LEVEL_EXCEPTION_FILTER prev = g_prev.load();
    if (t_inFilter) return EXCEPTION_CONTINUE_SEARCH;   // a filter installed after ours called back into us: already written
    t_inFilter = true;
    capture(ep);
    if (test_ends_here()) { t_inFilter = false; return EXCEPTION_EXECUTE_HANDLER; }
    const LONG res = prev ? prev(ep) : EXCEPTION_CONTINUE_SEARCH;
    t_inFilter = false;
    return res;
}

// kernelbase!UnhandledExceptionFilter: every exception no frame handled passes through it on its way to the top-level filter (the
// game's crash reporter). The report is written first, then the original runs exactly as without the mod.
LONG WINAPI uef_detour(EXCEPTION_POINTERS* ep) {
    if (!t_inFilter) {
        t_inFilter = true;
        capture(ep);
        t_inFilter = false;
        if (test_ends_here()) return EXCEPTION_EXECUTE_HANDLER;
    }
    return g_uefOrig(ep);
}

// The detour goes in only where the live prologue decodes cleanly: the shortest length >= 14 that ends on an instruction boundary
// with nothing position-dependent inside (x64_len.h); else nothing is patched and the status says why.
void hook_uef() {
    HMODULE kb = GetModuleHandleA("kernelbase.dll");
    void* target = kb ? reinterpret_cast<void*>(GetProcAddress(kb, "UnhandledExceptionFilter")) : nullptr;
    if (!target) { g_uefWhy = "kernelbase!UnhandledExceptionFilter not found"; return; }
    uint8_t live[48]{};
    if (!fault::guarded_silent([&] { std::memcpy(live, target, sizeof live); })) { g_uefWhy = "its prologue unreadable"; return; }
    size_t len = 0;
    for (size_t l = 14; l <= 32 && !len; ++l)
        if (x64::check_patch(live, sizeof live, l).verdict == x64::Boundary::Ok) len = l;
    if (!len) { g_uefWhy = "its prologue has no clean 14-32 byte patch length"; return; }
    void* orig = nullptr;
    const char* why = "";
    if (!inlinehook::install_detour(target, live, len, reinterpret_cast<void*>(&uef_detour), &orig, &why)) { g_uefWhy = why; return; }
    g_uefOrig = reinterpret_cast<UefFn>(orig);
    g_uefHooked = true;
    g_uefWhy = "ok";
}

// ---- research only (KCDMP_TEST_CRASH=1 in the game's environment): a deliberate crash, for the acceptance ----
bool g_testArmed = false;
char g_testFile[MAX_PATH] = "";
FILETIME g_testSeen{};
#pragma warning(push)
#pragma warning(disable : 4717)
void stack_bomb(volatile int depth) { volatile char pad[4096]; pad[0] = static_cast<char>(depth); stack_bomb(depth + 1 + pad[0] * 0); }
#pragma warning(pop)
DWORD WINAPI worker_av(LPVOID) { *reinterpret_cast<volatile int*>(0x10) = 1; return 0; }
void test_watch() {
    WIN32_FILE_ATTRIBUTE_DATA fa{};
    if (!GetFileAttributesExA(g_testFile, GetFileExInfoStandard, &fa)) return;
    if (CompareFileTime(&fa.ftLastWriteTime, &g_testSeen) == 0) return;
    g_testSeen = fa.ftLastWriteTime;
    char word[32]{};
    HANDLE f = CreateFileA(g_testFile, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    if (f == INVALID_HANDLE_VALUE) return;
    DWORD got = 0;
    ReadFile(f, word, sizeof word - 1, &got, nullptr);
    CloseHandle(f);
    for (char& c : word) if (c == '\r' || c == '\n' || c == ' ') c = 0;
    logf("WO202-CRASH test: '%s' requested (KCDMP_TEST_CRASH)%s", word, g_testChain ? " -- chained to the game's own handler" : "");
    g_testCrash = true;
    if (!std::strcmp(word, "av")) g_testMain = true;   // main_thread.cpp faults outside every guard on the next frame
    else if (!std::strcmp(word, "av-worker")) CloseHandle(CreateThread(nullptr, 0, worker_av, nullptr, 0, nullptr));
    else if (!std::strcmp(word, "stack")) stack_bomb(0);
    else if (!std::strcmp(word, "fastfail")) __fastfail(7);
}

// ---- WO-207 crash work, the bench only (KCDMP_TEST_SKIRMISH_PROBE=1): who removes a soul from its skirmish ----
// A pass-through gate at RPGModule's C_Skirmish::RemoveSoul (found by its own log label): the soul's name (its vt+0x18, the call the
// function makes itself next), the thread, and the return addresses a scan of the stack finds (values just after a call instruction).
bool g_skirProbe = false, g_skirTried = false;
fault::Site g_skirSite{"crash_capture::skirmish_probe", fault::Kind::Read};
bool skirmish_remove_cb(void* skirmish, void* soul) {
    using NameFn = const char*(__fastcall*)(void*);
    const char* nm = nullptr;
    fault::guarded(g_skirSite, [&] { nm = (*reinterpret_cast<NameFn* const*>(soul))[3](soul); });
    char name[64] = "?";
    if (nm) fault::guarded_silent([&] { std::snprintf(name, sizeof name, "%s", nm); });
    char line[1400];
    int n = std::snprintf(line, sizeof line, "W207-SKIRMISH remove soul=%s skirmish=%p tid=%lu%s stack:", name, skirmish, GetCurrentThreadId(),
                          GetCurrentThreadId() == main_thread::main_thread_id() ? " (main)" : "");
    // the frames, unwound (this callback, the thunk's known layout, then the engine's own unwind data) -- printed from the hooked call's caller
    CONTEXT ctx{};
    RtlCaptureContext(&ctx);
    uintptr_t rips[28]{};
    FrameKind kinds[28]{};
    const int fr = unwind(ctx, rips, kinds, 28);
    bool past = false;
    for (int i = 0; i < fr && n < static_cast<int>(sizeof line) - 80; ++i) {
        if (inlinehook::hook_of_page(rips[i]) >= 0) { past = true; continue; }
        if (!past) continue;
        const Mod* m = module_of(rips[i]);
        if (m) n += std::snprintf(line + n, sizeof line - n, " %s+0x%llx", m->name, static_cast<unsigned long long>(rips[i] - m->base));
        else n += std::snprintf(line + n, sizeof line - n, " 0x%llx", static_cast<unsigned long long>(rips[i]));
    }
    logf("%s", line);
    return false;   // never refuses: the engine's own RemoveSoul always runs
}
void skirmish_probe_try() {
    if (g_skirTried) return;
    HMODULE rpg = GetModuleHandleA("RPGModule.dll");
    if (!rpg) return;
    g_skirTried = true;
    // the function: the one whose code loads "wh::rpgmodule::C_Skirmish::RemoveSoul" (this build: RPGModule+0x63D230)
    static constexpr uint8_t kRemoveSoul[16] = {0x48, 0x89, 0x5C, 0x24, 0x18, 0x55, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57};
    uint8_t* fn = reinterpret_cast<uint8_t*>(rpg) + 0x63D230;
    const char* why = nullptr;
    g_skirProbe = inlinehook::install_gate(fn, kRemoveSoul, sizeof kRemoveSoul, &skirmish_remove_cb, &why);
    logf("W207-SKIRMISH probe (bench only) at RPGModule+0x63D230: %s", g_skirProbe ? "installed" : (why ? why : "?"));
}

// ---- WO-207 crash work, the bench only (KCDMP_TEST_TRIGGER_PROBE=1): who destroys the local player's slot triggers ----
// The joiner's fight crash: the player's perfect-block / dodge multi-trigger's sub-trigger is destroyed between its creation and its
// Start. Pass-through gates at two destructors (C_CombatSlotTrigger's deleting destructor and the perfect-block family's shared body,
// this build's CombatModule+0x559560 / +0x55B760, their exact prologues checked): a trigger whose combat actor (+8) is the local
// player's is logged with its class, the thread and the unwound stack.
bool g_trigProbe = false, g_trigTried = false;
std::atomic<void*> g_playerCa{nullptr};   // the main thread's tick: the local player's combat actor
std::atomic<uint32_t> g_trigLogged{0};
void log_stack_line(const char* head) {
    char line[1400];
    int n = std::snprintf(line, sizeof line, "%s tid=%lu%s stack:", head, GetCurrentThreadId(),
                          GetCurrentThreadId() == main_thread::main_thread_id() ? " (main)" : "");
    CONTEXT ctx{};
    RtlCaptureContext(&ctx);
    uintptr_t rips[28]{};
    FrameKind kinds[28]{};
    const int fr = unwind(ctx, rips, kinds, 28);
    bool past = false;
    for (int i = 0; i < fr && n < static_cast<int>(sizeof line) - 80; ++i) {
        if (inlinehook::hook_of_page(rips[i]) >= 0) { past = true; continue; }
        if (!past) continue;
        const Mod* m = module_of(rips[i]);
        if (m) n += std::snprintf(line + n, sizeof line - n, " %s+0x%llx", m->name, static_cast<unsigned long long>(rips[i] - m->base));
        else n += std::snprintf(line + n, sizeof line - n, " 0x%llx", static_cast<unsigned long long>(rips[i]));
    }
    logf("%s", line);
}
bool trigger_dtor_cb(void* trig, void* /*flags*/) {
    void* ca = nullptr;
    void* me = g_playerCa.load();
    if (!me || !fault::guarded_silent([&] { ca = *reinterpret_cast<void* const*>(static_cast<char*>(trig) + 8); })) return false;
    if (ca != me && static_cast<char*>(ca) - 8 != me) return false;
    if (g_trigLogged.fetch_add(1) >= 4000) return false;
    char cls[96] = "?";
    fault::guarded_silent([&] {
        const char* vt = *reinterpret_cast<const char* const*>(trig);
        const char* col = *reinterpret_cast<const char* const*>(vt - 8);
        const int32_t td = *reinterpret_cast<const int32_t*>(col + 12), self = *reinterpret_cast<const int32_t*>(col + 20);
        const char* name = col - self + td + 16;
        int i = 0;
        for (; i < 95 && name[i]; ++i) cls[i] = name[i];
        cls[i] = 0;
    });
    char head[160];
    std::snprintf(head, sizeof head, "W207-TRIGGER destroyed %s %p (the local player's)", cls, trig);
    log_stack_line(head);
    return false;   // never refuses: the engine's own destructor always runs
}
// The other side: a sub-trigger created on one of the player's multi-triggers (CombatModule+0x8FDD0, the attacker's slot planner's
// update creates it): the multi-trigger's class, the attacker's entity id (its combat actor's owner +0x30) and the thread.
bool trigger_create_cb(void* multi, void* attackerCa, void*, void*) {
    void* ca = nullptr; void* owner = nullptr;
    uint32_t eid = 0;
    void* me = g_playerCa.load();
    if (!me || !fault::guarded_silent([&] { ca = *reinterpret_cast<void* const*>(static_cast<char*>(multi) + 8); })) return false;
    if (ca != me && static_cast<char*>(ca) - 8 != me && static_cast<char*>(ca) + 8 != me) return false;
    fault::guarded_silent([&] {
        owner = *reinterpret_cast<void* const*>(static_cast<char*>(attackerCa) + 0x2D8);
        if (owner) eid = *reinterpret_cast<const uint32_t*>(static_cast<char*>(owner) + 0x30);
    });
    if (g_trigLogged.fetch_add(1) >= 4000) return false;
    char cls[96] = "?";
    fault::guarded_silent([&] {
        const char* vt = *reinterpret_cast<const char* const*>(multi);
        const char* col = *reinterpret_cast<const char* const*>(vt - 8);
        const int32_t td = *reinterpret_cast<const int32_t*>(col + 12), self = *reinterpret_cast<const int32_t*>(col + 20);
        const char* name = col - self + td + 16;
        int i = 0;
        for (; i < 95 && name[i]; ++i) cls[i] = name[i];
        cls[i] = 0;
    });
    logf("W207-TRIGGER created on %s %p by attacker eid=0x%X tid=%lu%s", cls, multi, eid, GetCurrentThreadId(),
         GetCurrentThreadId() == main_thread::main_thread_id() ? " (main)" : "");
    return false;
}
void trigger_probe_try() {
    if (g_trigTried) return;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    if (!cm) return;
    g_trigTried = true;
    static constexpr uint8_t kSlotDtor[15] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x8B, 0xDA, 0x48, 0x8B, 0xF9};
    static constexpr uint8_t kPbBody[18] = {0x48, 0x89, 0x5C, 0x24, 0x10, 0x48, 0x89, 0x74, 0x24, 0x18, 0x57, 0x48, 0x81, 0xEC, 0x80, 0x00, 0x00, 0x00};
    const char* w1 = nullptr; const char* w2 = nullptr;
    const bool a = inlinehook::install_gate(reinterpret_cast<uint8_t*>(cm) + 0x559560, kSlotDtor, sizeof kSlotDtor, &trigger_dtor_cb, &w1);
    const bool b = inlinehook::install_gate(reinterpret_cast<uint8_t*>(cm) + 0x55B760, kPbBody, sizeof kPbBody, &trigger_dtor_cb, &w2);
    static constexpr uint8_t kCreate[15] = {0x48, 0x8B, 0xC4, 0x48, 0x83, 0xEC, 0x68, 0x48, 0x89, 0x58, 0x10, 0x48, 0x89, 0x70, 0x18};
    const char* w3 = nullptr;
    const bool c = inlinehook::install_gate4(reinterpret_cast<uint8_t*>(cm) + 0x8FDD0, kCreate, sizeof kCreate, &trigger_create_cb, &w3);
    g_trigProbe = a || b || c;
    logf("W207-TRIGGER probe (bench only): C_CombatSlotTrigger dtor +0x559560 %s, perfect-block family body +0x55B760 %s, sub-trigger "
         "creation +0x8FDD0 %s", a ? "installed" : (w1 ? w1 : "?"), b ? "installed" : (w2 ? w2 : "?"), c ? "installed" : (w3 ? w3 : "?"));
}

} // namespace

void install() {
    if (g_installed.exchange(true)) return;
    g_t0 = GetTickCount64();
    char cwd[MAX_PATH]{};
    GetCurrentDirectoryA(MAX_PATH, cwd);
    std::snprintf(g_root, sizeof g_root, "%s%skcdmp-crashes", cwd, (cwd[0] && cwd[std::strlen(cwd) - 1] == '\\') ? "" : "\\");
    if (HMODULE dbg = LoadLibraryA("dbghelp.dll")) g_mdw = reinterpret_cast<MdwFn>(GetProcAddress(dbg, "MiniDumpWriteDump"));
    { char e[8]{}; g_skirProbe = GetEnvironmentVariableA("KCDMP_TEST_SKIRMISH_PROBE", e, sizeof e) == 1 && e[0] == '1'; g_skirTried = !g_skirProbe; }
    { char e[8]{}; g_trigProbe = GetEnvironmentVariableA("KCDMP_TEST_TRIGGER_PROBE", e, sizeof e) == 1 && e[0] == '1'; g_trigTried = !g_trigProbe; }
    refresh_modules();
    g_go = CreateEventA(nullptr, FALSE, FALSE, nullptr);
    g_done = CreateEventA(nullptr, TRUE, FALSE, nullptr);
    HANDLE w = CreateThread(nullptr, 1u << 20, writer_main, nullptr, 0, &g_writerTid);
    if (w) CloseHandle(w);
    g_prev = SetUnhandledExceptionFilter(&filter);
    // the game's crash reporter can block the API (it then returns nothing and installs nothing): a second call tells
    g_sufBlocked = SetUnhandledExceptionFilter(&filter) != &filter;
    if (g_sufBlocked) g_prev = nullptr;
    hook_uef();
    char prev[160] = "none";
    if (g_prev.load()) addr_text(reinterpret_cast<uintptr_t>(g_prev.load()), prev, sizeof prev);
    logf("WO202-CRASH armed: a crash writes %s" "\\" "crash-<time>" "\\" "crash.txt + crash.dmp, then the game's own handler runs as before -- "
         "UnhandledExceptionFilter detour %s; top-level filter %s%s",
         g_root, g_uefHooked ? "on" : g_uefWhy, g_sufBlocked ? "blocked by the game's crash reporter (it keeps its own)" : prev,
         g_mdw ? "" : " -- NO MiniDumpWriteDump (dbghelp.dll missing): crash.txt only");
    char env[8]{};
    if (GetEnvironmentVariableA("KCDMP_TEST_CRASH", env, sizeof env) && env[0] == '1') {
        std::snprintf(g_testFile, sizeof g_testFile, "%s%skcdmp-crash-test.txt", cwd, (cwd[0] && cwd[std::strlen(cwd) - 1] == '\\') ? "" : "\\");
        WIN32_FILE_ATTRIBUTE_DATA fa{};
        if (GetFileAttributesExA(g_testFile, GetFileExInfoStandard, &fa)) g_testSeen = fa.ftLastWriteTime;   // only a new write counts
        g_testArmed = true;
        char ch[8]{};
        g_testChain = GetEnvironmentVariableA("KCDMP_TEST_CRASH_CHAIN", ch, sizeof ch) && ch[0] == '1';
        logf("WO202-CRASH test trigger armed (research only): write av | av-worker | stack | fastfail into %s", g_testFile);
    }
}

void tick() {
    if (!g_installed.load()) return;
    const uint64_t now = GetTickCount64();
    static uint64_t lastMods = 0, lastAssert = 0, lastTest = 0;
    if (now - lastMods >= 10000) { lastMods = now; refresh_modules(); }
    if (g_on.load() && !g_sufBlocked && now - lastAssert >= 5000 && g_reasserts.load() < 20) {
        lastAssert = now;
        const LPTOP_LEVEL_EXCEPTION_FILTER cur = SetUnhandledExceptionFilter(&filter);
        if (cur != &filter) {
            g_prev = cur;
            g_reasserts.fetch_add(1);
            char who[160] = "none";
            if (cur) addr_text(reinterpret_cast<uintptr_t>(cur), who, sizeof who);
            logf("WO202-CRASH another filter (%s) was installed after ours: ours runs first again and calls it after writing (#%u)", who,
                 g_reasserts.load());
        }
    }
    if (g_testArmed && now - lastTest >= 1000) { lastTest = now; test_watch(); }
    if (!g_skirTried) skirmish_probe_try();   // WO-207 crash work (bench only): once RPGModule is loaded
    if (!g_trigTried) trigger_probe_try();     // WO-207 crash work (bench only): once CombatModule is loaded
    if (g_trigProbe) g_playerCa.store(motion::player_combat_actor());
}

void note_pipe(uint8_t type, uint8_t op, uint16_t len) {
    const long i = g_pipeNext.fetch_add(1);
    g_pipe[i % kPipeRing] = PipeNote{GetTickCount64(), type, op, len};
}

void set_on(bool on) {
    const bool was = g_on.exchange(on);
    if (was == on) return;
    if (on) {
        const LPTOP_LEVEL_EXCEPTION_FILTER cur = SetUnhandledExceptionFilter(&filter);
        if (cur != &filter) g_prev = cur;
    } else {
        SetUnhandledExceptionFilter(g_prev.load());   // the game's own handler alone again
    }
    logf("WO202-CRASH mp_crash_capture=%s", on ? "on" : "off (a crash goes straight to the game's own handler)");
}

bool on() { return g_on.load(); }

bool test_main_fault_due() { return g_testMain.exchange(false); }

int status_text(char* out, int n) {
    char prev[160] = "none";
    if (g_prev.load()) addr_text(reinterpret_cast<uintptr_t>(g_prev.load()), prev, sizeof prev);
    return std::snprintf(out, static_cast<size_t>(n), "crash=%s on=%d uef=%s top=%s reasserts=%u written=%u dump=%d", g_installed.load() ? "armed" : "off",
                         g_on.load() ? 1 : 0, g_uefHooked ? "hooked" : g_uefWhy, g_sufBlocked ? "blocked" : prev, g_reasserts.load(),
                         g_written.load(), g_mdw ? 1 : 0);
}

} // namespace kcdmp::crash
