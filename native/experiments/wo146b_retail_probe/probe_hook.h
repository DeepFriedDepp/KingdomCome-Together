// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-146B research probe -- a capture-only entry hook.
//
// Same recipe as the shipped native/KCDMP/inline_hook.cpp (itself WO-116's
// research probe): overwrite the first `len` bytes of a function with an
// absolute jmp to a register-preserving thunk that calls back and then jumps
// to a trampoline (the copied bytes + a jump back). Written again here so the
// probe links nothing from the shipped DLL.
//
// Two differences from the shipped version, both because this is research on a
// build we have never run code on:
//   * the expected prologue bytes are ALWAYS checked, and a mismatch refuses;
//   * the callback receives the hooked call's first four integer arguments
//     (rcx, rdx, r8, r9) and its own thread id is whatever thread the engine
//     used -- which is the measurement.
//
// Nothing is ever unhooked. Every callback here only reads and counts.

#include <cstdint>
#include <cstddef>

namespace wo146b::hook {

// cb(a1, a2, a3, a4) = the hooked function's rcx, rdx, r8, r9 at its entry.
using ArgsCallback = void (*)(void* a1, void* a2, void* a3, void* a4);

// Patch `target`, which must start with exactly `expect[0..len)`; 14 <= len <= 32.
// The reason is written to *why. Call from a thread that is not the one that
// runs `target`: the patch suspends every other thread of the process.
//
// `len` MUST end on an instruction boundary and the bytes it covers must be
// position-independent (no RIP-relative operand, no relative branch), because
// they are copied verbatim into the trampoline. This probe cannot check that
// -- it carries no length decoder -- so the caller passes a length worked out
// from a disassembly of THIS image and the probe logs the bytes it is about to
// copy so the choice can be checked afterwards.
//
// WO-146B, the hard way: the first run used a blanket 18 bytes for three
// different functions. It is right for C_ModulesManager::Update (an
// instruction ends at 18) and wrong for the two combat slots, where 18 cuts an
// instruction in half; the game ran for twenty minutes and then died the
// moment combat made one of those two run. See docs/WO-146B-progress.md.
bool install(void* target, const uint8_t* expect, size_t len, ArgsCallback cb, const char** why);

} // namespace wo146b::hook
