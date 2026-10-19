// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-201: engine-free checks of the native half's rules (wo201_rules.h). Linked into KCDMP_NativeTests.
#include <cstdio>
#include <cstring>
#include <vector>

#include "wo201_rules.h"
#include "wo200_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

std::vector<uint8_t> hex(const char* s) {
    std::vector<uint8_t> v;
    for (size_t i = 0; s[i] && s[i + 1]; i += 2) {
        auto nib = [](char c) { return static_cast<uint8_t>(c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10); };
        v.push_back(static_cast<uint8_t>(nib(s[i]) << 4 | nib(s[i + 1])));
    }
    return v;
}
} // namespace

int wo201_tests(int* passed) {
    using namespace wo201rules;
    // ---- B1: the save path's own cancel, read out of the Modding Tools 1.5.5 build's code (XGenAIModule) ----
    // C_NPCManager::ValidateFastForward's first 0x40 bytes: mov [rsp+8],rcx; push rbx; sub rsp,0A0h; mov rbx,rcx; lea rcx,..; call ..;
    // cmp dword ptr [rbx+0F4h],0; je ..
    const auto vff = hex("48894c2408534881eca0000000488bd9488d0d2d4ba901e8342ca50083bbf4000000000f84cb050000e83270c2fe807818000f84bc0500004889ac24c8000000");
    WCHECK(ff_running_field(vff.data(), vff.size()) == 0xF4, "the running check reads +0xF4 (the build WO-188 read)");
    // its tail: mov rcx,[rsp+0B0h]; lea rax,[rip+lambda vftable]; mov [rsp+30h],rax; lea rdx,..; ...; call [rax+50h]
    const auto tail = hex("488b8c24b0000000488d059606e9004889442430488d542430488d4424304889442468488b01ff5050488bac24c80000");
    size_t leas[4];
    const int k = lea_rax_stores(tail.data(), tail.size(), leas, 4);
    WCHECK(k == 1 && leas[0] == 8, "one lea rax + store: the lambda's vftable (at +8), got %d", k);
    // the lambda's _Do_call: push rbx; sub rsp,20h; lea rcx,..; mov rbx,rdx; call ..; mov rax,[rbx]; xor edx,edx; mov rcx,rbx; add rsp,20h;
    // pop rbx; rex.w jmp qword ptr [rax+4B8h]
    const auto lam = hex("40534883ec20488d0d1249a601488bdae80bd0a100488b0333d2488bcb4883c4205b48ffa0b8040000");
    WCHECK(stop_slot_in_lambda(lam.data(), lam.size()) == 0x4B8, "the per-NPC cancel is C_NPC slot +0x4B8 with argument 0");
    WCHECK(kKnownStopSlot == 0x4B8 && kKnownRunningField == 0xF4, "the identity check's values");
    // a call form (call qword ptr [rax+disp32]) is read the same way; a wrong shape is refused
    const auto callForm = hex("33d2488bcbff9010050000");
    WCHECK(stop_slot_in_lambda(callForm.data(), callForm.size()) == 0x510, "call [rax+510h] after xor edx,edx");
    const auto noXor = hex("488bcbffa0b8040000");
    WCHECK(stop_slot_in_lambda(noXor.data(), noXor.size()) == -1, "no xor edx,edx (no argument 0): refused");
    const auto odd = hex("33d2ffa0b9040000");
    WCHECK(stop_slot_in_lambda(odd.data(), odd.size()) == -1, "a slot that is not 8-aligned: refused");
    const auto noCmp = hex("48894c2408534881eca0000000488bd9");
    WCHECK(ff_running_field(noCmp.data(), noCmp.size()) == -1, "no running check: refused");
    // ---- B1: the per-frame rule ----
    WCHECK(walk_due(true, true, 1, 1000, 0), "a FastForward runs on the joiner: walk");
    WCHECK(!walk_due(true, true, 0, 1000, 0), "none runs: no walk");
    WCHECK(!walk_due(true, false, 3, 1000, 0), "the host (or a joiner outside the host's world): never");
    WCHECK(!walk_due(false, true, 3, 1000, 0), "the switch off: never");
    WCHECK(!walk_due(true, true, 3, 1100, 1000) && walk_due(true, true, 3, 1200, 1000), "at most every 200 ms");
    WCHECK(!stop_allowed(0x7777, "Dude", 0x7777), "the player: never");
    WCHECK(!stop_allowed(0x2F1A4, "kcd2mp_0", 0x7777), "the mod's figures: never");
    WCHECK(stop_allowed(0x2F1A4, "tsem_seminsr", 0x7777) && stop_allowed(0x2F1A4, nullptr, 0x7777), "a host copy: yes");
    WCHECK(!stop_allowed(0, "x", 0x7777), "no id: no");
    {   // V.3: the flag word out of the per-soul save step's own code
        const std::vector<uint8_t> save = {0x48, 0x89, 0x5C, 0x24, 0x18, 0x55, 0x56, 0x57, 0x41, 0x56, 0x41, 0x57, 0x48, 0x81, 0xEC, 0xC0, 0x00,
                                           0x00, 0x00, 0x48, 0x8B, 0x05, 0x10, 0x20, 0x30, 0x00, 0x48, 0x8B, 0xF9, 0x48, 0x8B, 0x07, 0x48, 0x8B,
                                           0x10, 0x8B, 0xB2, 0x78, 0x0D, 0x00, 0x00, 0x8B, 0xEE, 0xC1, 0xED, 0x04, 0x83, 0xE5, 0x01, 0xC1, 0xEE,
                                           0x03, 0x83, 0xE6, 0x01};
        const FlagWord f = flag_word(save.data(), save.size());
        WCHECK(f.off == 0xD78 && f.playerBit == 3 && f.localBit == 4, "the flag word +0xD78, IsPlayer bit 3, IsLocalPlayer bit 4 (the build WO-199 read)");
        std::vector<uint8_t> noShr = save; noShr[noShr.size() - 4] = 0x90;   // the IsPlayer shift gone
        WCHECK(flag_word(noShr.data(), noShr.size()).off == -1, "no IsPlayer shift: refused (never a guessed offset)");
        std::vector<uint8_t> farOff = save; farOff[37] = 0x00; farOff[38] = 0x50;   // +0x5000: no soul field
        WCHECK(flag_word(farOff.data(), farOff.size()).off == -1, "an implausible offset: refused");
        WCHECK(dude2_allowed(false, 0x0, 4), "a villager soul: may be Dude2");
        WCHECK(!dude2_allowed(true, 0x18, 4), "the player's own soul: never");
        WCHECK(!dude2_allowed(false, 1u << 4, 4), "a local player: never");
        WCHECK(!dude2_allowed(false, 1u << 2, 4), "a copy: never");
        WCHECK(dude2_allowed(false, 1u << 3, 4), "already Dude2: allowed again (idempotent)");
    }
    {   // V.2: C_DistanceTrigger out of its own code (the bytes the research read, Modding Tools 1.5.5)
        const std::vector<uint8_t> act = {0x48, 0x8B, 0x5C, 0x24, 0x50, 0xC6, 0x87, 0x60, 0x02, 0x00, 0x00, 0x01, 0x48, 0x8B, 0xCF, 0x48,
                                          0x83, 0xC4, 0x40, 0x5F, 0xE9, 0x7D, 0x01, 0x00, 0x00, 0x4C, 0x8D, 0x05};
        const TailJump tj = active_tail_jump(act.data(), act.size());
        WCHECK(tj.activeOff == 0x260 && tj.jmpAt == 20, "the activation's tail: active byte +0x260, then jmp Evaluate");
        std::vector<uint8_t> noJmp = act; noJmp[20] = 0x90;
        WCHECK(active_tail_jump(noJmp.data(), noJmp.size()).jmpAt == -1, "no tail jmp: refused");
        const std::vector<uint8_t> ev = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0xF9, 0x0F, 0x29, 0x74, 0x24, 0x30,
                                         0x48, 0x8D, 0x0D, 0x7E, 0xEA, 0xE7, 0x01, 0xE8, 0xB2, 0xFF, 0xE3, 0x00, 0x48, 0x8B, 0x8F, 0x68, 0x02, 0x00,
                                         0x00, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x50, 0x48, 0x8B, 0x8F, 0x70, 0x02, 0x00, 0x00, 0xF2, 0x0F, 0x10, 0x30,
                                         0x8B, 0x58, 0x08, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x50};
        const NodeReads nr = evaluate_reads(ev.data(), ev.size());
        WCHECK(nr.origin == 0x268 && nr.target == 0x270 && nr.slot == 0x50, "Evaluate: origin +0x268, target +0x270, position slot +0x50");
        WCHECK(figure_nearer(30.0f, 5.0f) && !figure_nearer(5.0f, 30.0f) && !figure_nearer(10.0f, 10.0f), "the nearer body counts; a tie stays Henry's");
    }
    // ---- B7: the joiner's own quest logic starts no story bark ----
    {
        using namespace wo200rules;
        Cfg c; c.joined = true;
        WCHECK(joiner_story_bark(kRoleJoiner, Kind::Function, Method::Monologue, false, false, false, c), "an escort bark (RequestMonologue, no closure): refused");
        WCHECK(joiner_story_bark(kRoleJoiner, Kind::DialogueStarter, Method::Other, false, false, false, c), "a forced / fader dialogue from his own logic: refused");
        WCHECK(joiner_story_bark(kRoleJoiner, Kind::View, Method::Other, false, false, false, c), "a focus camera from his own logic: refused");
        WCHECK(joiner_story_bark(kRoleJoiner, Kind::Behaviour, Method::Other, true, false, false, c), "an NPC urging a dialogue (UrgeADialog): refused");
        WCHECK(!joiner_story_bark(kRoleJoiner, Kind::Behaviour, Method::Other, false, false, false, c), "any other behaviour: not this rule's");
        WCHECK(!joiner_story_bark(kRoleJoiner, Kind::DialogueStarter, Method::Other, false, true, false, c), "the follow-on of his own conversation (a local closure): his");
        WCHECK(!joiner_story_bark(kRoleJoiner, Kind::Function, Method::Monologue, false, false, true, c), "his own interaction monologue (the coal sack): his");
        WCHECK(!joiner_story_bark(kRoleHost, Kind::Function, Method::Monologue, false, false, false, c), "the host: never this rule");
        Cfg out = c; out.joined = false;
        WCHECK(!joiner_story_bark(kRoleJoiner, Kind::Function, Method::Monologue, false, false, false, out), "a joiner in his own world: never");
        Cfg off = c; off.storyBarks = false;
        WCHECK(!joiner_story_bark(kRoleJoiner, Kind::Function, Method::Monologue, false, false, false, off), "the switch off: never");
        WCHECK(!joiner_story_bark(kRoleJoiner, Kind::Function, Method::Other, false, false, false, c), "a plain function: never");
        WCHECK(is_interaction_parent("interactiontrigger_monologonplayer_uhli") && is_interaction_parent("InteractionTrigger_X") && !is_interaction_parent("tour_barks"), "the interaction names");
        WCHECK(std::strcmp(verdict_name(Verdict::JoinerStoryBark), "joiner-story") == 0, "its verdict word");
    }
    *passed = g_pass;
    return g_fail;
}
