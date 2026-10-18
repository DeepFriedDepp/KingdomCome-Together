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

// wo139.cpp (after 0.45.8): C_UIHudStates::SetTrespassState(this, int level) itself -- the HUD's own refresh calls it
// directly, past the listener (a joiner's hidden warning showed in the field).
// mov [rsp+8],rbx; mov [rsp+10h],edx; push rdi; sub rsp,20h; mov rdi,rcx
inline constexpr uint8_t kTrespassImpl[17] = {
    0x48, 0x89, 0x5C, 0x24, 0x08, 0x89, 0x54, 0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xF9,
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

// weather.cpp (WO-151): EnvironmentModule C_TimeOfDayBlender::BlendToProfile (found by its __FUNCTION__ string).
// mov [rsp+10h],rbx; mov [rsp+18h],rbp; push rsi; push rdi; push r15
inline constexpr uint8_t kBlendToProfile[14] = {
    0x48, 0x89, 0x5C, 0x24, 0x10, 0x48, 0x89, 0x6C, 0x24, 0x18, 0x56, 0x57, 0x41, 0x57,
};

// wo178.cpp: the combat RPG's "is hostile to" test the player's opponent picker asks for every candidate (found through
// C_CombatPlayerController::HandleOpponentSelection's label: its callee that loads the combat RPG and calls the test).
// mov [rsp+10h],rbx; mov [rsp+18h],rsi; push rdi; sub rsp,20h
inline constexpr uint8_t kHostileTest[15] = {
    0x48, 0x89, 0x5C, 0x24, 0x10, 0x48, 0x89, 0x74, 0x24, 0x18, 0x57, 0x48, 0x83, 0xEC, 0x20,
};

// wo178.cpp: C_CombatActorClinch::StartSyncGuard (found by its __FUNCTION__ string) -- the clinch's start, gated.
// push rbp; push rsi; push rdi; push r12; push r13; push r14; push r15; sub rsp,80h
inline constexpr uint8_t kClinchStart[19] = {
    0x40, 0x55, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57, 0x48, 0x81, 0xEC, 0x80, 0x00, 0x00, 0x00,
};

// wo178.cpp (research, installed only by a duel trace): C_CombatActor's state handler -- the callback StartCombatMode registers on the
// model's State property (its slot read from the registered thunk, the vftable by RTTI). Pass-through: logs the actor and old/new state.
// push rbx; push rdi; push r14; sub rsp,30h; mov rbx,rcx; mov edi,r9d
inline constexpr uint8_t kStateHandler[15] = {
    0x40, 0x53, 0x57, 0x41, 0x56, 0x48, 0x83, 0xEC, 0x30, 0x48, 0x8B, 0xD9, 0x41, 0x8B, 0xF9,
};

// wo178.cpp (research, installed only by a duel trace): C_CombatSlotTrigger::StartSlot / EndSlot (their __FUNCTION__ strings). Pass-through.
// mov [rsp+8],rbx; push rdi; sub rsp,40h; mov rdi,rcx; movzx ebx,dl
inline constexpr uint8_t kSlotStart[16] = {
    0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0xF9, 0x0F, 0xB6, 0xDA,
};
// mov [rsp+10h],rsi; push rdi; sub rsp,40h; mov rdi,rcx; mov esi,edx
inline constexpr uint8_t kSlotEnd[15] = {
    0x48, 0x89, 0x74, 0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0xF9, 0x8B, 0xF2,
};

// wo194.cpp: C_CutscenePlayer::EnqueueCutscene (GUIModule's export) -- every enqueued cutscene, a detour (WO-194).
// mov [rsp+18h],rbx; push rdi; sub rsp,20h; mov rbx,rcx; mov rdi,rdx
inline constexpr uint8_t kEnqueueCutscene[16] = {
    0x48, 0x89, 0x5C, 0x24, 0x18, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xD9, 0x48, 0x8B, 0xFA,
};

// wo200.cpp: C_Node::Execute (ConceptModule's export; no node class overrides its slot) -- WO-200's node gate, an entry gate.
// push rbx; push rsi; push rdi; sub rsp,80h; mov rbx,rcx; mov rsi,rdx
inline constexpr uint8_t kNodeExecute[17] = {
    0x40, 0x53, 0x56, 0x57, 0x48, 0x81, 0xEC, 0x80, 0x00, 0x00, 0x00, 0x48, 0x8B, 0xD9, 0x48, 0x8B, 0xF2,
};

struct Entry { const char* name; const uint8_t* bytes; size_t len; };
inline constexpr Entry kAll[] = {
    {"motion UpdateMannequinTags", kMotionTags, sizeof kMotionTags},
    {"npc_trace CSystem::Render", kTraceRender, sizeof kTraceRender},
    {"wo135 dialogue gate", kDialogueGate, sizeof kDialogueGate},
    {"wo138 PauseGame", kPauseGame, sizeof kPauseGame},
    {"wo139 trespass listener", kTrespassListener, sizeof kTrespassListener},
    {"wo139 SetTrespassState", kTrespassImpl, sizeof kTrespassImpl},
    {"wo140 SkipTime ShowDialog", kSkipTimeShow, sizeof kSkipTimeShow},
    {"wo143 NPC-state request", kNpcStateRequest, sizeof kNpcStateRequest},
    {"weather BlendToProfile", kBlendToProfile, sizeof kBlendToProfile},
    {"wo178 hostility test", kHostileTest, sizeof kHostileTest},
    {"wo178 clinch start", kClinchStart, sizeof kClinchStart},
    {"wo178 state handler (research)", kStateHandler, sizeof kStateHandler},
    {"wo178 slot start (research)", kSlotStart, sizeof kSlotStart},
    {"wo178 slot end (research)", kSlotEnd, sizeof kSlotEnd},
    {"wo194 EnqueueCutscene", kEnqueueCutscene, sizeof kEnqueueCutscene},
    {"wo200 C_Node::Execute", kNodeExecute, sizeof kNodeExecute},
};

} // namespace kcdmp::hookpro
