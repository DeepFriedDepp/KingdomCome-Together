// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-201: the pure rules of the native half (wo201.cpp). No engine access; the unit tests pin every one.
#pragma once
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo201rules {

// ---- B1: the save path's own FastForward cancel, read out of its code ----
// C_NPCManager::ValidateFastForward (the one function that references the save lock's error text) begins
//     mov [rsp+8],rcx; push rbx; sub rsp,..; mov rbx,rcx; ...; cmp dword ptr [rbx+disp32], 0   <- "FastForward synchronization is going on"
// and ends by handing a std::function to the NPC walk:
//     lea rax,[rip+lambdaVftable]; mov [rsp+..],rax; ...
// whose _Do_call (vftable slot 2) is
//     mov rax,[rbx]; xor edx,edx; mov rcx,rbx; ...; jmp qword ptr [rax+disp32]   <- C_NPC's StopFastForward(npc, 0)

// The first "cmp dword ptr [rbx+disp32], 0" (83 BB disp32 00) within n bytes: its disp32, or -1.
inline int64_t ff_running_field(const uint8_t* p, size_t n) {
    for (size_t i = 0; i + 7 <= n; ++i)
        if (p[i] == 0x83 && p[i + 1] == 0xBB && p[i + 6] == 0x00) {
            int32_t d; std::memcpy(&d, p + i + 2, 4);
            if (d > 0 && d < 0x10000) return d;
        }
    return -1;
}

// Every "lea rax,[rip+disp32]" (48 8D 05) followed within 8 bytes by "mov [rsp+disp8],rax" (48 89 44 24): the offsets (from p) of the
// lea, up to max. The lambda's vftable is one of them.
inline int lea_rax_stores(const uint8_t* p, size_t n, size_t* out, int max) {
    int k = 0;
    for (size_t i = 0; i + 7 <= n && k < max; ++i) {
        if (!(p[i] == 0x48 && p[i + 1] == 0x8D && p[i + 2] == 0x05)) continue;
        for (size_t j = i + 7; j + 4 <= n && j <= i + 7 + 8; ++j)
            if (p[j] == 0x48 && p[j + 1] == 0x89 && p[j + 2] == 0x44 && p[j + 3] == 0x24) { out[k++] = i; break; }
    }
    return k;
}

// In a lambda's call body: "xor edx,edx" (33 D2) followed within 12 bytes by "jmp qword ptr [rax+disp32]" (FF A0) or
// "call qword ptr [rax+disp32]" (FF 90): the disp32 (a vtable slot offset), or -1.
inline int64_t stop_slot_in_lambda(const uint8_t* p, size_t n) {
    for (size_t i = 0; i + 2 <= n; ++i) {
        if (!(p[i] == 0x33 && p[i + 1] == 0xD2)) continue;
        for (size_t j = i + 2; j + 6 <= n && j <= i + 2 + 12; ++j)
            if (p[j] == 0xFF && (p[j + 1] == 0xA0 || p[j + 1] == 0x90)) {
                int32_t d; std::memcpy(&d, p + j + 2, 4);
                if (d > 0 && d < 0x2000 && (d % 8) == 0) return d;
            }
    }
    return -1;
}

// The build WO-188 read (Modding Tools 1.5.5): the identity check logs whether the resolved values are these.
constexpr int64_t kKnownStopSlot = 0x4B8, kKnownRunningField = 0xF4;

// The per-frame rule (joiner, in the host's world, the switch on): walk only while the manager says a FastForward runs; once per
// kWalkEveryMs at most (the walk stops early the moment it reads 0 again).
constexpr uint64_t kWalkEveryMs = 200;
inline bool walk_due(bool on, bool joinerInWorld, int32_t running, uint64_t now, uint64_t lastWalk) {
    return on && joinerInWorld && running > 0 && now - lastWalk >= kWalkEveryMs;
}

// Who is never stopped: the local player (entity 0x7777) and the mod's own figures ("kcd2mp...").
inline bool stop_allowed(uint32_t eid, const char* name, uint32_t playerEid) {
    if (eid == 0 || eid == playerEid) return false;
    if (name && std::strncmp(name, "kcd2mp", 6) == 0) return false;
    return true;
}

// ---- V.3 Dude2: the soul's flag word, read out of the engine's own per-soul save trace ----
// "Saving soul '%s' ... IsPlayer: %d, IsLocalPlayer: %d" is logged by the one function that writes a soul into a save:
//     mov esi, dword ptr [rdx+disp32]  (8B B2 disp32)   <- the flag word
//     mov ebp, esi; shr ebp, 4 (C1 ED 04)               <- IsLocalPlayer
//     ...; shr esi, 3 (C1 EE 03)                        <- IsPlayer
struct FlagWord { int64_t off = -1; int playerBit = -1, localBit = -1; };
inline FlagWord flag_word(const uint8_t* p, size_t n) {
    FlagWord f;
    for (size_t i = 0; i + 6 <= n; ++i) {
        if (!(p[i] == 0x8B && (p[i + 1] & 0xC0) == 0x80 && (p[i + 1] & 0x07) != 0x04)) continue;   // mov r32, [reg+disp32] (no SIB)
        int32_t d; std::memcpy(&d, p + i + 2, 4);
        if (d <= 0x100 || d >= 0x4000) continue;
        const int reg = (p[i + 1] >> 3) & 7;   // the destination register
        int sp = -1, sl = -1;
        for (size_t j = i + 6; j + 3 <= n && j < i + 6 + 24; ++j) {
            if (p[j] != 0xC1 || (p[j + 1] & 0xF8) != 0xE8) continue;   // shr r32, imm8
            const int r = p[j + 1] & 7, imm = p[j + 2];
            if (r == reg && imm == 3) sp = 3;
            else if (imm == 4) sl = 4;   // the copy's shift (mov ebp, esi first)
        }
        if (sp == 3 && sl == 4) { f.off = d; f.playerBit = 3; f.localBit = 4; return f; }
    }
    return f;
}
constexpr int64_t kKnownFlagOff = 0xD78;
constexpr int kCopyBit = 2;   // WO-199 A.1: bit 2 IsCopy (C_Soul slot +0x478)

// V.3: may this soul become Dude2? Never the player's own, never a copy, never one with IsLocalPlayer.
inline bool dude2_allowed(bool isPlayerSoul, uint32_t flags, int localBit) {
    if (isPlayerSoul) return false;
    if (localBit >= 0 && (flags >> localBit) & 1u) return false;
    if ((flags >> kCopyBit) & 1u) return false;
    return true;
}

// ---- V.2 nearest_player_triggers: C_DistanceTrigger, read out of its own code ----
// The activation function ends "mov byte ptr [rdi+disp32], 1 (C6 87 disp32 01); ... ; jmp Evaluate (E9 rel32)": the last such pair
// (the jmp within 16 bytes after the store).
struct TailJump { int64_t activeOff = -1; int64_t jmpAt = -1; };
inline TailJump active_tail_jump(const uint8_t* p, size_t n) {
    TailJump t;
    for (size_t i = 0; i + 7 <= n; ++i) {
        if (!(p[i] == 0xC6 && p[i + 1] == 0x87 && p[i + 6] == 0x01)) continue;
        int32_t d; std::memcpy(&d, p + i + 2, 4);
        if (d <= 0 || d >= 0x1000) continue;
        for (size_t j = i + 7; j + 5 <= n && j <= i + 7 + 16; ++j)
            if (p[j] == 0xE9) { t.activeOff = d; t.jmpAt = static_cast<int64_t>(j); break; }
    }
    return t;
}
// Evaluate: "mov rcx,[rdi+disp32] (48 8B 8F disp32); mov rax,[rcx] (48 8B 01); call [rax+disp8] (FF 50 disp8)" twice -- origin, target, slot.
struct NodeReads { int64_t origin = -1, target = -1; int slot = -1; };
inline NodeReads evaluate_reads(const uint8_t* p, size_t n) {
    NodeReads r;
    int k = 0;
    for (size_t i = 0; i + 12 <= n && k < 2; ++i) {
        if (!(p[i] == 0x48 && p[i + 1] == 0x8B && p[i + 2] == 0x8F)) continue;
        int32_t d; std::memcpy(&d, p + i + 3, 4);
        if (!(p[i + 7] == 0x48 && p[i + 8] == 0x8B && p[i + 9] == 0x01 && p[i + 10] == 0xFF && p[i + 11] == 0x50)) {
            // the target's load may sit between the call and the use of its result: accept the load alone once the slot is known
            if (k == 1 && d > 0 && d < 0x1000) { r.target = d; ++k; }
            continue;
        }
        if (i + 12 >= n) break;
        const int slot = p[i + 12];
        if (d <= 0 || d >= 0x1000) continue;
        if (k == 0) { r.origin = d; r.slot = slot; } else { r.target = d; if (slot != r.slot) r.slot = -1; }
        ++k;
    }
    if (r.origin == r.target) r.target = -1;
    return r;
}
constexpr int64_t kKnownOriginOff = 0x268, kKnownTargetOff = 0x270, kKnownActiveOff = 0x260;
// The figure counts when it is nearer to the other end than the host's Henry (by more than a centimetre: a tie stays Henry's).
inline bool figure_nearer(float dHenry, float dFigure) { return dFigure + 0.01f < dHenry; }

// ---- B9 follow-up (mp_copy_no_idle_turn): a standing copy never turns in place on its own ----
// The animated character's desired motion parameters (EntityModule, its vt +0x168): { float value[6]; uint8 id[6]; uint8 count; } --
// 0x20 bytes. The index of parameter `id` in that block, false when the block holds none (or reads malformed).
constexpr size_t kParamBlockBytes = 0x20, kParamIds = 0x18, kParamCount = 0x1E;
constexpr int kParamMax = 6;
inline bool turn_param_at(const uint8_t* block, size_t n, uint8_t id, int* index) {
    if (!block || n < kParamBlockBytes) return false;
    const int count = block[kParamCount];
    if (count > kParamMax) return false;
    for (int i = 0; i < count; ++i)
        if (block[kParamIds + i] == id) { *index = i; return true; }
    return false;
}
// Only a copy the writer shows standing (gait class 0): a walking copy's turn parameter belongs to its walk.
inline bool turn_clear_wanted(float gaitClass) { return gaitClass < 0.5f; }

} // namespace kcdmp::wo201rules
