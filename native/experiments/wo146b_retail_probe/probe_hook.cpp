// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#include "probe_hook.h"
#include "probe_log.h"

#include <windows.h>
#include <tlhelp32.h>
#include <cstring>
#include <initializer_list>

namespace wo146b::hook {

namespace {

constexpr int kMaxThreads = 4096;

// Handles collected BEFORE any thread is suspended: nothing below allocates
// from a heap another (suspended) thread might hold the lock of.
struct Suspended {
    HANDLE h[kMaxThreads];
    int    n = 0;
};

Suspended g_sus;   // static: kMaxThreads handles do not belong on a stack

void suspend_others(Suspended* s) {
    s->n = 0;
    const DWORD self = GetCurrentThreadId(), pid = GetCurrentProcessId();
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snap == INVALID_HANDLE_VALUE) return;
    THREADENTRY32 te{};
    te.dwSize = sizeof(te);
    if (Thread32First(snap, &te)) {
        do {
            if (te.th32OwnerProcessID != pid || te.th32ThreadID == self) continue;
            if (s->n >= kMaxThreads) break;
            HANDLE t = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT, FALSE, te.th32ThreadID);
            if (!t) continue;
            if (SuspendThread(t) != static_cast<DWORD>(-1)) s->h[s->n++] = t;
            else CloseHandle(t);
        } while (Thread32Next(snap, &te));
    }
    CloseHandle(snap);
}

void resume_all(Suspended* s) {
    for (int i = 0; i < s->n; ++i) { ResumeThread(s->h[i]); CloseHandle(s->h[i]); }
    s->n = 0;
}

bool rip_inside(const Suspended* s, uintptr_t a, size_t n) {
    for (int i = 0; i < s->n; ++i) {
        CONTEXT c{};
        c.ContextFlags = CONTEXT_CONTROL;
        if (GetThreadContext(s->h[i], &c) && c.Rip >= a && c.Rip < a + n) return true;
    }
    return false;
}

// push rcx/rdx/r8/r9; sub rsp,0x68; save xmm0-3; call cb (whose own rcx..r9 are
// the hooked entry's, restored just before the call); restore; pops; jmp to the
// trampoline. At the hooked entry RSP == 8 (mod 16); four pushes plus 0x68
// leave it 16-aligned for the call with 0x20 of shadow space.
size_t emit_thunk(uint8_t* p, ArgsCallback cb, void* tramp) {
    uint8_t* s = p;
    auto put = [&](std::initializer_list<uint8_t> b) { for (uint8_t x : b) *p++ = x; };
    auto put64 = [&](uint64_t v) { std::memcpy(p, &v, 8); p += 8; };
    put({0x51, 0x52, 0x41, 0x50, 0x41, 0x51});          // push rcx; push rdx; push r8; push r9
    put({0x48, 0x83, 0xEC, 0x68});                      // sub rsp, 0x68
    put({0xF3, 0x0F, 0x7F, 0x44, 0x24, 0x20});          // movdqu [rsp+0x20], xmm0
    put({0xF3, 0x0F, 0x7F, 0x4C, 0x24, 0x30});          // movdqu [rsp+0x30], xmm1
    put({0xF3, 0x0F, 0x7F, 0x54, 0x24, 0x40});          // movdqu [rsp+0x40], xmm2
    put({0xF3, 0x0F, 0x7F, 0x5C, 0x24, 0x50});          // movdqu [rsp+0x50], xmm3
    put({0x48, 0xB8}); put64(reinterpret_cast<uint64_t>(cb));   // mov rax, cb
    put({0xFF, 0xD0});                                  // call rax  (rcx..r9 unchanged)
    put({0xF3, 0x0F, 0x6F, 0x44, 0x24, 0x20});          // movdqu xmm0, [rsp+0x20]
    put({0xF3, 0x0F, 0x6F, 0x4C, 0x24, 0x30});
    put({0xF3, 0x0F, 0x6F, 0x54, 0x24, 0x40});
    put({0xF3, 0x0F, 0x6F, 0x5C, 0x24, 0x50});
    put({0x48, 0x83, 0xC4, 0x68});                      // add rsp, 0x68
    put({0x41, 0x59, 0x41, 0x58, 0x5A, 0x59});          // pop r9; pop r8; pop rdx; pop rcx
    put({0xFF, 0x25, 0, 0, 0, 0}); put64(reinterpret_cast<uint64_t>(tramp));
    return static_cast<size_t>(p - s);
}

bool bytes_differ(const uint8_t* a, const uint8_t* b, size_t n) {
    __try { return std::memcmp(a, b, n) != 0; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return true; }
}

} // namespace

bool install(void* target, const uint8_t* expect, size_t len, ArgsCallback cb, const char** why) {
    const char* dummy = nullptr;
    if (!why) why = &dummy;
    if (!target || !expect || !cb || len < 14 || len > 32) { *why = "bad arguments"; return false; }
    auto* tgt = static_cast<uint8_t*>(target);
    if (bytes_differ(tgt, expect, len)) { *why = "prologue bytes differ from the expected ones"; return false; }

    auto* mem = static_cast<uint8_t*>(VirtualAlloc(nullptr, 4096, MEM_RESERVE | MEM_COMMIT,
                                                   PAGE_EXECUTE_READWRITE));
    if (!mem) { *why = "VirtualAlloc failed"; return false; }
    std::memcpy(mem, tgt, len);                       // trampoline: the copied prologue
    uint8_t* q = mem + len;
    q[0] = 0xFF; q[1] = 0x25; std::memset(q + 2, 0, 4);
    const uint64_t back = reinterpret_cast<uint64_t>(tgt + len);
    std::memcpy(q + 6, &back, 8);
    uint8_t* thunk = mem + 128;
    emit_thunk(thunk, cb, mem);
    FlushInstructionCache(GetCurrentProcess(), mem, 4096);

    uint8_t patch[32];
    std::memset(patch, 0x90, sizeof(patch));
    patch[0] = 0xFF; patch[1] = 0x25; std::memset(patch + 2, 0, 4);
    const uint64_t th = reinterpret_cast<uint64_t>(thunk);
    std::memcpy(patch + 6, &th, 8);

    for (int tries = 0; tries < 50; ++tries) {
        suspend_others(&g_sus);
        if (rip_inside(&g_sus, reinterpret_cast<uintptr_t>(tgt), len)) {
            resume_all(&g_sus);
            Sleep(1);
            continue;
        }
        DWORD old = 0;
        if (!VirtualProtect(tgt, len, PAGE_EXECUTE_READWRITE, &old)) {
            resume_all(&g_sus);
            *why = "VirtualProtect failed";
            return false;
        }
        std::memcpy(tgt, patch, len);
        DWORD ignored = 0;
        VirtualProtect(tgt, len, old, &ignored);
        FlushInstructionCache(GetCurrentProcess(), tgt, len);
        resume_all(&g_sus);
        logf("HOOK: patched %p (%zu bytes) -> thunk %p, trampoline %p, %d threads suspended",
             target, len, thunk, mem, g_sus.n);
        *why = "ok";
        return true;
    }
    *why = "a thread stayed inside the patch range for 50 attempts";
    return false;
}

} // namespace wo146b::hook
