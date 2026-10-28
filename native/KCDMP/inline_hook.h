// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-118: an entry hook that calls back and then runs the original function,
// for debug tooling only (mp_npc_trace's render-side sample).
//
// The recipe is the WO-116 research probe's, which hooked 24 engine functions
// live without a fault (docs/WO-116-progress.md s4):
//   * the patch overwrites `len` bytes of the function's first instructions
//     with an absolute `jmp [rip+0]` (14 bytes); the caller picks `len` on an
//     instruction boundary with no RIP-relative operand and no branch inside,
//     and passes the exact bytes it expects there -- any difference refuses
//   * a register-preserving thunk (rcx, rdx, r8, r9, xmm0-3) calls `cb`, then
//     jumps to a trampoline (the copied bytes + a jump back)
//   * the write happens with every other thread of the process suspended, and
//     is retried while any suspended thread's RIP is inside the patched range
//
// Nothing is ever unpatched: the callback is expected to be cheap and to
// check its own on/off state.
//
// WO-148: before patching, the live prologue is decoded (x64_len.h) and the hook
// is refused -- fail closed, the reason in *why -- unless `len` ends exactly on an
// instruction boundary with no relative branch, call, return or RIP-relative
// operand inside. The expected bytes of every shipped hook live in
// hook_prologues.h, where native/tests checks them the same way.

#include <cstddef>
#include <cstdint>

namespace kcdmp::inlinehook {

using Callback = void (*)();

// Patch `target` (which must start with exactly `expect[0..len)`, 14 <= len
// <= 32). Returns false, with the reason in *why, when the bytes differ, an
// allocation or protection change fails, or the patch could not be placed
// safely. Call from a thread that is NOT the one that runs `target` (the
// patch suspends every other thread).
bool install(void* target, const uint8_t* expect, size_t len, Callback cb, const char** why);

// WO-129: the same hook, for a callback that reads the hooked function's first
// argument. The thunk leaves rcx/rdx/r8/r9 as they were at the hooked entry
// when it calls `cb`, so `cb`'s own first argument IS the original rcx (the
// `this` of a method). The callback may run on any thread the engine runs the
// target on (actor updates run on the main thread and eight job workers).
using ThisCallback = void (*)(void* self);
bool install_this(void* target, const uint8_t* expect, size_t len, ThisCallback cb, const char** why);

// WO-135: a gate at a function's ENTRY. `cb` gets the hooked call's first two
// arguments (rcx, rdx untouched); when it returns true the function is not run
// and returns 0 to its caller (at the entry the stack is the caller's, so a
// plain `ret` is correct); false runs the original. For functions whose own
// failure value is 0.
using GateCallback = bool (*)(void* a1, void* a2);
bool install_gate(void* target, const uint8_t* expect, size_t len, GateCallback cb, const char** why);
// WO-138: the same gate for a callback that reads the first FOUR arguments --
// the thunk calls `cb` with rcx/rdx/r8/r9 exactly as they were at the entry.
using GateCallback4 = bool (*)(void* a1, void* a2, void* a3, void* a4);
bool install_gate4(void* target, const uint8_t* expect, size_t len, GateCallback4 cb, const char** why);

// WO-178: a full detour. The patch jumps straight to `detour` (a function with the hooked one's own signature and calling convention);
// *original receives the trampoline -- the copied prologue and a jump back -- through which the detour calls the engine's own function.
// *original is written before the patch goes in, so the detour never sees it unset.
bool install_detour(void* target, const uint8_t* expect, size_t len, void* detour, void** original, const char** why);

// WO-202 I.1: every hook this file placed, for the crash report ("which mod hooks were on the stack"): the patched function, the
// page holding its trampoline (at +0) and thunk (at +kThunkAt), and the callback or detour it runs. Lock-free reads.
constexpr size_t kHookPageBytes = 4096, kThunkAt = 128;
struct HookRec {
    uintptr_t target = 0;   // the engine function whose first bytes jump to us
    uintptr_t page = 0;     // trampoline at page+0, thunk at page+kThunkAt (a detour has no thunk)
    uintptr_t fn = 0;       // the callback (thunk hooks) or the detour
    bool detour = false;
    uint32_t len = 0;       // WO-210: the bytes the patch replaced (their copy starts the trampoline)
};
int hook_count();
bool hook_at(int i, HookRec* out);
// The hook whose page holds `addr` (its trampoline or thunk), or -1.
int hook_of_page(uintptr_t addr);
// WO-210: a function's first n bytes as they were before this file patched it (the trampoline's copy, then the live bytes past the
// patch); false when no hook of ours starts at `target` -- the caller compares the live bytes then.
bool original_bytes(const void* target, uint8_t* out, size_t n);

} // namespace kcdmp::inlinehook
