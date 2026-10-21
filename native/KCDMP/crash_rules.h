// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-202 I.1: the crash capture's pure rules (crash_capture.cpp), engine-free so native/tests pins them.
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace kcdmp::crashrules {

// The folder one crash writes into: crash-YYYYMMDD-HHMMSS-<pid>.
inline int folder_name(char* out, size_t cap, int y, int mo, int d, int h, int mi, int s, unsigned long pid) {
    return std::snprintf(out, cap, "crash-%04d%02d%02d-%02d%02d%02d-%lu", y, mo, d, h, mi, s, pid);
}

// The exception's plain name (the codes a game crash shows).
inline const char* exception_name(uint32_t code) {
    switch (code) {
        case 0xC0000005u: return "access violation";
        case 0xC00000FDu: return "stack overflow";
        case 0xC000001Du: return "illegal instruction";
        case 0xC0000094u: return "integer division by zero";
        case 0xC0000096u: return "privileged instruction";
        case 0xC0000006u: return "in-page error";
        case 0xC0000409u: return "stack buffer overrun / fast fail";
        case 0xC0000374u: return "heap corruption";
        case 0xC000008Eu: case 0xC000008Fu: case 0xC0000090u: case 0xC0000091u: case 0xC0000093u: return "floating-point fault";
        case 0x80000003u: return "breakpoint";
        case 0xE06D7363u: return "C++ exception (not caught)";
        default: return "exception";
    }
}

// A module of this mod: KCDMP.dll or a copy with another name (KCDMP_w202_3_201500.dll, KCDMP_<tag>.dll), case-insensitive.
inline bool is_mod_module(const char* base) {
    if (!base) return false;
    const char want[] = "kcdmp";
    for (size_t i = 0; i < 5; ++i) {
        const char c = base[i];
        if (!c || (c | 0x20) != want[i]) return false;
    }
    const size_t n = std::strlen(base);
    return n >= 9 && (base[n - 4] == '.') && ((base[n - 3] | 0x20) == 'd') && ((base[n - 2] | 0x20) == 'l') && ((base[n - 1] | 0x20) == 'l');
}

// A thunk hook (inline_hook.cpp emit_thunk): entered by a jmp from the patched function, so at its entry [rsp] is the return
// address into the patched function's caller; it pushes four registers and subtracts 0x68 before it calls the callback. Unwinding
// from the callback lands on the instruction after that call with rsp = entry - 0x88: the caller's return address is at rsp+0x88,
// and the caller's rsp is rsp+0x90.
constexpr uint64_t kThunkRetAt = 0x88, kThunkFrame = 0x90;
// The thunk's own bytes before its call instruction (the pushes, the sub, four xmm saves, mov rax,imm64): an address in the thunk
// at or past this offset is "after the call".
constexpr size_t kThunkCallEnd = 6 + 4 + 4 * 6 + 10 + 2;

// One unwound frame's tag in crash.txt.
enum class FrameKind : uint8_t { Game, Mod, HookThunk, HookTrampoline, Unknown };
inline const char* frame_tag(FrameKind k) {
    switch (k) {
        case FrameKind::Mod: return "  <- the mod's code";
        case FrameKind::HookThunk: return "  <- a mod hook's thunk";
        case FrameKind::HookTrampoline: return "  <- a mod hook's trampoline (the engine's own first bytes)";
        case FrameKind::Unknown: return "  <- no module";
        default: return "";
    }
}
inline FrameKind classify(bool inModModule, bool inHookPage, size_t pageOffset, size_t thunkAt) {
    if (inHookPage) return pageOffset >= thunkAt ? FrameKind::HookThunk : FrameKind::HookTrampoline;
    return inModModule ? FrameKind::Mod : FrameKind::Game;
}

} // namespace kcdmp::crashrules
