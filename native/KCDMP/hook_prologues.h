// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-148: the expected first bytes of every function the shipped DLL patches with
// an inline hook (inline_hook.cpp), in one place, so the native unit tests check
// exactly the bytes the hooks use (native/tests/wo148_x64_tests.cpp).
//
// Each length is the patch length: it must end on an instruction boundary with no
// relative branch and no RIP-relative operand inside -- inline_hook.cpp decodes the
// live bytes (x64_len.h) and refuses otherwise. These are the Modding Tools build's
// bytes (1.5.5); they will not port, which is why every hook also compares them
// before patching and refuses on any difference.

#include <cstddef>
#include <cstdint>

namespace kcdmp::hookpro {

// motion.cpp: C_Actor::UpdateMannequinTags (vftable slot 0xC98, `this` RTTI-checked).
// mov [rsp+10h],rdx; push rbx; push rdi; push r13; push r15; sub rsp,0A8h
inline constexpr uint8_t kMotionTags[18] = {
    0x48, 0x89, 0x54, 0x24, 0x10, 0x53, 0x57, 0x41, 0x55, 0x41, 0x57, 0x48, 0x81, 0xEC, 0xA8, 0x00, 0x00, 0x00,
};

// npc_trace.cpp: CSystem::Render (found by its profiler label). Debug tooling, first trace only.
// push rsi; push r14; sub rsp,0D8h; mov rsi,rcx
inline constexpr uint8_t kTraceRender[14] = {
    0x40, 0x56, 0x41, 0x56, 0x48, 0x81, 0xEC, 0xD8, 0x00, 0x00, 0x00, 0x48, 0x8B, 0xF1,
};

// wo135.cpp: the native dialogue gate.
// push rbp; push rbx; push rsi; push r13; push r15; lea rbp,[rsp-37h]; sub rsp,0C0h
inline constexpr uint8_t kDialogueGate[20] = {
    0x40, 0x55, 0x53, 0x56, 0x41, 0x55, 0x41, 0x57,
    0x48, 0x8D, 0x6C, 0x24, 0xC9,
    0x48, 0x81, 0xEC, 0xC0, 0x00, 0x00, 0x00,
};

// wo138.cpp: CCryAction::PauseGame (found by its log string).
// mov [rsp+8],rbx; mov [rsp+18h],rsi; mov [rsp+20h],rdi
inline constexpr uint8_t kPauseGame[15] = {
    0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x18, 0x48, 0x89, 0x7C, 0x24, 0x20,
};

// wo139.cpp: the HUD trespass listener (this, uint8 level).
// mov [rsp+8],rbx; push rdi; sub rsp,20h; mov rdi,rcx; movzx ebx,dl
inline constexpr uint8_t kTrespassListener[16] = {
    0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xF9, 0x0F, 0xB6, 0xDA,
};

// wo140.cpp: C_SkipTime::ShowDialog.
// mov rax,rsp; mov [rax+8],rbx; mov [rax+10h],rsi; mov [rax+18h],rdi; push rbp
inline constexpr uint8_t kSkipTimeShow[16] = {
    0x48, 0x8B, 0xC4, 0x48, 0x89, 0x58, 0x08, 0x48, 0x89, 0x70, 0x10, 0x48, 0x89, 0x78, 0x18, 0x55,
};

// wo143.cpp: the NPC-state request-change function.
// push rbp; push rbx; push rsi; push rdi; push r13; push r14; lea rbp,[rsp-168h]; sub rsp,268h
inline constexpr uint8_t kNpcStateRequest[24] = {
    0x40, 0x55, 0x53, 0x56, 0x57, 0x41, 0x55, 0x41, 0x56,
    0x48, 0x8D, 0xAC, 0x24, 0x98, 0xFE, 0xFF, 0xFF,
    0x48, 0x81, 0xEC, 0x68, 0x02, 0x00, 0x00,
};

struct Entry { const char* name; const uint8_t* bytes; size_t len; };
inline constexpr Entry kAll[] = {
    {"motion UpdateMannequinTags", kMotionTags, sizeof kMotionTags},
    {"npc_trace CSystem::Render", kTraceRender, sizeof kTraceRender},
    {"wo135 dialogue gate", kDialogueGate, sizeof kDialogueGate},
    {"wo138 PauseGame", kPauseGame, sizeof kPauseGame},
    {"wo139 trespass listener", kTrespassListener, sizeof kTrespassListener},
    {"wo140 SkipTime ShowDialog", kSkipTimeShow, sizeof kSkipTimeShow},
    {"wo143 NPC-state request", kNpcStateRequest, sizeof kNpcStateRequest},
};

} // namespace kcdmp::hookpro
