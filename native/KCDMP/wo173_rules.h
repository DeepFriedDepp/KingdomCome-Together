// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-173: the engine-free rules of the joiner's real hits on the host (wo173.cpp) -- unit-tested in native/tests/wo173_tests.cpp.
//
// The reaction tail. RPGProcessHit (WO-165's replay, research/WO-162/combat-RE.md Q1.7 "Level B") applies the damage and decides
// hit / blocked / perfect / broken, but the hit REACTION is the collision handler's own tail (Q1.5): after the processor returns, the
// handler (C_CombatActorCollisionProcess::OnCollision) asks two things of the victim's combat actor (can it react; is its current action
// handling the hit itself), picks a reaction kind from the result struct, calls C_CombatActorCollisionProcess::CreateHitAction and hands
// the action to the victim's action director (wh::framework::C_ActionDirector::SetAction, a Framework.dll import). Read this WO
// (docs/WO-173-findings.md, Phase 0): CreateHitAction reads only `this+8` (the attacker's combat actor) from its object, so the tail can
// run after our own processor call without the collision process the engine creates only inside an attack action. Every function and
// every offset is lifted from the handler's own bytes (parse_reaction_sites) -- no address, no offset of a build is written in code.
//
//   A  the reaction block, once in the handler:
//        call ALIVE ; test al,al ; je .. ; mov rcx,[r14+VICT] ; call OWN ; movzx r15d,al ; test al,al ; jne ..
//        mov rcx,[rsi+8] ; call AFLAG ; mov edx,[rbp+KIN] ; movzx r8d,al ; call KIND ; mov r8,[r14+VICT] ; lea rcx,[rbp+RES]
//        mov [rsp+20h],rcx ; lea rdx,[rsp+60h] ; mov rcx,rsi ; mov r9d,eax ; call CREATEHITACTION
//      (KIN - RES = the offset of the reaction-kind input inside the result struct)
//   B  the hand-over, once:
//        mov rax,[r14+VICT] ; mov [rsp+60h],rbx ; mov rdi,[rax+DIR] ; test rbx,rbx ; je .. ; mov rax,[rbx] ; mov rcx,rbx ; call [rax+ADDREF]
//        lea rdx,[rsp+60h] ; mov rcx,rdi ; call [rip+IAT] ; mov rcx,[rsp+60h] ; movzx r15d,al ; test rcx,rcx ; je .. ; mov rax,[rcx]
//        call [rax+RELEASE]
//   C  the test the handler makes before it hands an action over, once: movzx eax,word [rdi+TEAM] ; cmp word [rcx+TEAM],ax ; jne ..
//        cmp byte [rbp+K0],r13b ; jne .. ; cmp byte [rbp+K1],r13b ; jne .. ; mov rcx,rdi ; call INTERR ; test al,al ; je ..
//        mov rcx,[rdi+MODEL] ; add rcx,FLAGS ; mov rax,[rcx] ; call [rax+GET] ; test al,BIT ; je ..   -- all true = no hand-over
#pragma once
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo173rules {

// ---------------------------------------------------------------------------------------------------------------- the reaction sites
struct ReactionSites {
    uintptr_t alive = 0, ownReaction = 0, attackerFlag = 0, kind = 0, createHitAction = 0;   // call targets
    uintptr_t iatSlot = 0;                                                                     // the SetAction import's slot
    uint32_t victimOff = 0;      // collision details -> the victim's combat actor (the handler's r14 + disp)
    uint32_t kindInOff = 0;      // result struct -> the reaction kind's input (an int)
    uint32_t resultRbp = 0;      // (not used by the caller; logged)
    uint32_t directorOff = 0;    // victim combat actor -> its C_ActionDirector
    uint32_t teamOff = 0;        // combat actor -> the word the handler compares (same group)
    uint8_t addrefSlot = 0, releaseSlot = 0;   // the action's vtable byte offsets
    uintptr_t interruptible = 0;    // C: the victim's "its action may not be interrupted" predicate
    uint32_t killedOff0 = 0, killedOff1 = 0;   // C: result struct -> the two "killed" bytes the test reads
    uint32_t modelOff = 0, flagsPropOff = 0;   // C: combat actor -> its model; model -> the flags property the test reads
    uint8_t flagsGetSlot = 0, flagsBit = 0;    // C: that property's getter slot and the bit tested
    uintptr_t intendedSetter = 0;   // D: the attacker model's "intended target" property setter
    uint32_t intendedOff = 0;       // D: attacker combat model -> that property
};

// -1 = any byte
inline bool match_at(const uint8_t* p, size_t avail, const int16_t* pat, size_t n) {
    if (avail < n) return false;
    for (size_t i = 0; i < n; ++i) if (pat[i] >= 0 && p[i] != static_cast<uint8_t>(pat[i])) return false;
    return true;
}
inline int32_t s32(const uint8_t* p) { int32_t v; std::memcpy(&v, p, 4); return v; }
inline uint32_t u32(const uint8_t* p) { uint32_t v; std::memcpy(&v, p, 4); return v; }

constexpr int16_t X = -1;
inline constexpr int16_t kPatA[90] = {
    0xE8, X, X, X, X, 0x84, 0xC0, 0x0F, 0x84, X, X, X, X,
    0x49, 0x8B, 0x8E, X, X, X, X, 0xE8, X, X, X, X, 0x44, 0x0F, 0xB6, 0xF8, 0x84, 0xC0, 0x0F, 0x85, X, X, X, X,
    0x48, 0x8B, 0x4E, 0x08, 0xE8, X, X, X, X, 0x8B, 0x55, X, 0x44, 0x0F, 0xB6, 0xC0, 0xE8, X, X, X, X,
    0x4D, 0x8B, 0x86, X, X, X, X, 0x48, 0x8D, 0x4D, X, 0x48, 0x89, 0x4C, 0x24, 0x20, 0x48, 0x8D, 0x54, 0x24, 0x60,
    0x48, 0x8B, 0xCE, 0x44, 0x8B, 0xC8, 0xE8, X, X, X, X};
inline constexpr int16_t kPatB[67] = {
    0x49, 0x8B, 0x86, X, X, X, X, 0x48, 0x89, 0x5C, 0x24, 0x60, 0x48, 0x8B, 0xB8, X, X, X, X, 0x48, 0x85, 0xDB, 0x74, X,
    0x48, 0x8B, 0x03, 0x48, 0x8B, 0xCB, 0xFF, 0x50, X, 0x48, 0x8D, 0x54, 0x24, 0x60, 0x48, 0x8B, 0xCF, 0xFF, 0x15, X, X, X, X,
    0x48, 0x8B, 0x4C, 0x24, 0x60, 0x44, 0x0F, 0xB6, 0xF8, 0x48, 0x85, 0xC9, 0x74, X, 0x48, 0x8B, 0x01, 0xFF, 0x50, X};
inline constexpr int16_t kPatC[68] = {0x0F, 0xB7, 0x87, X, X, X, X, 0x66, 0x39, 0x81, X, X, X, X, 0x0F, 0x85, X, X, X, X,
    0x44, 0x38, 0x6D, X, 0x75, X, 0x44, 0x38, 0x6D, X, 0x75, X, 0x48, 0x8B, 0xCF, 0xE8, X, X, X, X, 0x84, 0xC0, 0x74, X,
    0x48, 0x8B, 0x8F, X, X, X, X, 0x48, 0x81, 0xC1, X, X, X, X, 0x48, 0x8B, 0x01, 0xFF, 0x50, X, 0xA8, X, 0x74, X};
// D  before the processor, the handler marks the attacker model's "intended target" property (the hit core's facing test is skipped when
//    it is set): mov dl,1 ; jmp +2 ; xor edx,edx ; lea rcx,[r8+INT] ; mov [rsp+1F0h],rbx ; call SETTER
inline constexpr int16_t kPatD[22] = {0xB2, 0x01, 0xEB, 0x02, 0x33, 0xD2, 0x49, 0x8D, 0x88, X, X, X, X, 0x48, 0x89, 0x9C, 0x24, X, X, X, X, 0xE8};

// `code` = a window of the collision handler's bytes starting at `addr`. True when each block occurs exactly once and its own parts
// agree (the victim offset is the same everywhere, the kind input lies inside the result struct, the team offsets of C match).
inline bool parse_reaction_sites(const uint8_t* code, size_t n, uintptr_t addr, ReactionSites* out) {
    ReactionSites s{};
    int na = 0, nb = 0, nc = 0, nd = 0;
    uint32_t bVictim = 0;
    int32_t cK0 = 0, cK1 = 0;
    for (size_t i = 0; i < n; ++i) {
        const uint8_t* p = code + i;
        const size_t avail = n - i;
        if (match_at(p, avail, kPatA, 90)) {
            const uint32_t v1 = u32(p + 16), v2 = u32(p + 61);
            const int32_t kin = static_cast<int8_t>(p[48]), res = static_cast<int8_t>(p[68]);
            if (v1 != v2 || kin - res < 0 || kin - res >= 0xA0) continue;
            s.victimOff = v1;
            s.kindInOff = static_cast<uint32_t>(kin - res);
            s.resultRbp = static_cast<uint32_t>(res);
            s.alive = addr + i + 5 + static_cast<intptr_t>(s32(p + 1));
            s.ownReaction = addr + i + 25 + static_cast<intptr_t>(s32(p + 21));
            s.attackerFlag = addr + i + 46 + static_cast<intptr_t>(s32(p + 42));
            s.kind = addr + i + 58 + static_cast<intptr_t>(s32(p + 54));
            s.createHitAction = addr + i + 90 + static_cast<intptr_t>(s32(p + 86));
            ++na;
        }
        if (match_at(p, avail, kPatB, 67)) {
            s.directorOff = u32(p + 15);
            s.addrefSlot = p[32];
            s.releaseSlot = p[66];
            s.iatSlot = addr + i + 47 + static_cast<intptr_t>(s32(p + 43));
            bVictim = u32(p + 3);   // must equal A's (checked after the scan)
            ++nb;
        }
        if (match_at(p, avail, kPatC, 68) && u32(p + 3) == u32(p + 10)) {
            s.teamOff = u32(p + 3);
            cK0 = static_cast<int8_t>(p[23]); cK1 = static_cast<int8_t>(p[29]);
            s.interruptible = addr + i + 40 + static_cast<intptr_t>(s32(p + 36));
            s.modelOff = u32(p + 47); s.flagsPropOff = u32(p + 54); s.flagsGetSlot = p[63]; s.flagsBit = p[65];
            ++nc;
        }
        if (match_at(p, avail, kPatD, 22) && avail >= 26) { s.intendedOff = u32(p + 9); s.intendedSetter = addr + i + 26 + static_cast<intptr_t>(s32(p + 22)); ++nd; }
    }
    if (na != 1 || nb != 1 || nc != 1 || nd != 1 || bVictim != s.victimOff) return false;
    if (s.intendedOff == 0 || s.intendedOff > 0x4000) return false;
    // the killed bytes are read relative to the same frame base as the result struct (A's RES)
    const int32_t res = static_cast<int8_t>(static_cast<uint8_t>(s.resultRbp));
    if (cK0 - res < 0 || cK0 - res >= 0xA0 || cK1 - res < 0 || cK1 - res >= 0xA0) return false;
    s.killedOff0 = static_cast<uint32_t>(cK0 - res); s.killedOff1 = static_cast<uint32_t>(cK1 - res);
    if (s.modelOff == 0 || s.modelOff > 0x1000 || s.flagsPropOff == 0 || s.flagsPropOff > 0x4000 || s.flagsGetSlot == 0 || (s.flagsGetSlot & 7) || s.flagsBit == 0) return false;
    if (s.victimOff == 0 || s.victimOff > 0x400 || s.directorOff == 0 || s.directorOff > 0x2000 || s.teamOff == 0 || s.teamOff > 0x2000) return false;
    if (s.addrefSlot == 0 || s.releaseSlot == 0 || s.addrefSlot == s.releaseSlot || (s.addrefSlot & 7) || (s.releaseSlot & 7)) return false;
    *out = s;
    return true;
}

// ---------------------------------------------------------------------------------------------------------------- WO-210 C.1: ClientHit
// The handler's last step after the reaction block (research: the death RE): the game rules' ClientHit queues the hit result, and the
// next frame's ProcessLocalHit sends the brain its hit signal and the victim actor its hit event -> C_HitDeathReactions::OnHit (the
// non-combat hit reaction, a lethal hit's death, or the ragdoll + StartDeadAction that makes a corpse lootable). The replay never made it.
//   E   call [rip+GetGameIface] ; lea rdx,[rbp+RES] ; mov rcx,[rax+GR] ; mov rax,[rcx] ; call [rax+SLOT]
//   F1  test r15b,r15b ; je +8 ; and dword [rbp+FL],0FFFFFFFDh ; mov byte [rbp+HD],1       -- a handled hit (the reaction took)
//   F2  cmp [r14+VICT],r13 ; je +4 ; and dword [rbp+FL],0FFFFFFDFh                       -- a victim exists
struct ClientHitSites {
    uint32_t gameRulesOff = 0;   // GetGameIface() -> its CGameRules
    uint32_t clientHitSlot = 0;  // CGameRules vtable byte offset of ClientHit
    uint32_t flagsOff = 0;       // result struct -> its flags word
    uint32_t handledOff = 0;     // result struct -> the handled byte
    uintptr_t iatSlot = 0;       // E's import slot (GetGameIface)
};
inline constexpr int16_t kPatE[26] = {0xFF, 0x15, X, X, X, X, 0x48, 0x8D, 0x55, X, 0x48, 0x8B, 0x88, X, X, X, X, 0x48, 0x8B, 0x01, 0xFF, 0x90, X, X, X, X};
inline constexpr int16_t kPatF1[13] = {0x45, 0x84, 0xFF, 0x74, 0x08, 0x83, 0x65, X, 0xFD, 0xC6, 0x45, X, 0x01};
inline constexpr int16_t kPatF2[13] = {0x4D, 0x39, 0xAE, X, X, X, X, 0x74, 0x04, 0x83, 0x65, X, 0xDF};
// res = the reaction block's own result displacement (ReactionSites::resultRbp, a signed byte); victimOff = its victim offset.
inline bool parse_clienthit_sites(const uint8_t* code, size_t n, uintptr_t addr, uint32_t resultRbp, uint32_t victimOff, ClientHitSites* out) {
    ClientHitSites s{};
    int ne = 0, nf1 = 0, nf2 = 0;
    int32_t fl1 = 0, fl2 = 0, hd = 0;
    const int32_t res = static_cast<int8_t>(static_cast<uint8_t>(resultRbp));
    for (size_t i = 0; i < n; ++i) {
        const uint8_t* p = code + i;
        const size_t avail = n - i;
        if (match_at(p, avail, kPatE, 26) && static_cast<int8_t>(p[9]) == res) {
            s.iatSlot = addr + i + 6 + static_cast<intptr_t>(s32(p + 2));
            s.gameRulesOff = u32(p + 13);
            s.clientHitSlot = u32(p + 22);
            ++ne;
        }
        if (match_at(p, avail, kPatF1, 13)) { fl1 = static_cast<int8_t>(p[7]); hd = static_cast<int8_t>(p[11]); ++nf1; }
        if (match_at(p, avail, kPatF2, 13) && u32(p + 3) == victimOff) { fl2 = static_cast<int8_t>(p[11]); ++nf2; }
    }
    if (ne != 1 || nf1 != 1 || nf2 != 1 || fl1 != fl2) return false;
    if (fl1 - res < 0 || fl1 - res >= 0xA0 || hd - res < 0 || hd - res >= 0xA0) return false;
    if (s.gameRulesOff == 0 || s.gameRulesOff > 0x1000 || s.clientHitSlot == 0 || s.clientHitSlot > 0x2000 || (s.clientHitSlot & 7)) return false;
    s.flagsOff = static_cast<uint32_t>(fl1 - res);
    s.handledOff = static_cast<uint32_t>(hd - res);
    *out = s;
    return true;
}
// The handler's own edit of the result before ClientHit: a handled hit drops flag 2 and sets the handled byte; a victim drops flag 0x20.
inline void clienthit_edit(uint8_t* result, const ClientHitSites& s, bool handled) {
    uint32_t f = 0;
    std::memcpy(&f, result + s.flagsOff, 4);
    if (handled) { f &= ~2u; result[s.handledOff] = 1; }
    f &= ~0x20u;
    std::memcpy(result + s.flagsOff, &f, 4);
}
inline constexpr uint8_t kClientHitPrologue[23] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x6C, 0x24, 0x10, 0x48, 0x89, 0x74, 0x24, 0x18,
                                                   0x57, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0xF9};

// The prologues read this WO (1.5.5). A build whose bytes differ leaves the reaction tail unarmed (the hit itself still runs).
inline constexpr uint8_t kCreateHitActionPrologue[22] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x6C, 0x24, 0x18, 0x56,
                                                         0x57, 0x41, 0x54, 0x41, 0x56, 0x41, 0x57, 0x48, 0x83, 0xEC, 0x40};
inline constexpr uint8_t kInterruptiblePrologue[9] = {0x40, 0x53, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xD9};
inline constexpr uint8_t kAlivePrologue[9] = {0x40, 0x53, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xD9};          // push rbx; sub rsp,20h; mov rbx,rcx
inline constexpr uint8_t kOwnReactionPrologue[13] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xD9};
inline constexpr uint8_t kAttackerFlagPrologue[9] = {0x40, 0x53, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xD9};
inline constexpr uint8_t kKindPrologue[10] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x20};      // ignores rcx (read)
// the property setter: mov [rsp+18h],rbx; push rbp; sub rsp,40h; mov rbp,rcx (it keeps the value at +8 and tells its listeners)
inline constexpr uint8_t kIntendedSetterPrologue[13] = {0x48, 0x89, 0x5C, 0x24, 0x18, 0x55, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0xE9};
constexpr size_t kPropertyValueOff = 8;   // the setter's own `movzx r8d, byte [rbp+8]`

// What the tail did (the WO173-HIT line's reaction=).
// SameGroup (5): the handler's own skip -- same group, nobody killed, the victim's action may not be interrupted and its model's flag says so.
enum class React : uint8_t { NotRun = 0, Queued = 1, Own = 2, NotAlive = 3, NoAction = 4, SameGroup = 5, NotTaken = 6, NotArmed = 7, Fault = 8, Off = 9,
                             PlayerKept = 10 };   // WO-178: the local player gets the ordinary hit reaction only
inline const char* react_name(React r) {
    switch (r) {
        case React::Queued: return "queued";
        case React::Own: return "own-action";
        case React::NotAlive: return "victim-cannot-react";
        case React::NoAction: return "no-reaction-found";
        case React::SameGroup: return "uninterruptible-skipped";
        case React::NotTaken: return "director-refused";
        case React::NotArmed: return "not-armed";
        case React::Fault: return "fault";
        case React::Off: return "off";
        case React::PlayerKept: return "player-only-hit-reactions";
        default: return "-";
    }
}

// "handled" = the reaction took (queued, the victim's own action took it, the group test's uninterruptible skip), or -- for a victim that
// can react -- the hit was blocked / perfect-blocked / broke the block (the handler's three opponent tests; WO-165's outcome stands in).
enum : uint8_t { kOutBlocked = 2, kOutPerfect = 3, kOutBroken = 4 };   // wo165::Outcome's numbers (wo165.h)
inline bool clienthit_handled(React r, uint8_t outcome) {
    if (r == React::Queued || r == React::Own || r == React::SameGroup) return true;
    if (r == React::NotAlive) return false;
    return outcome == kOutBlocked || outcome == kOutPerfect || outcome == kOutBroken;
}

// ---------------------------------------------------------------------------------------------------------------- C1: one blow, once
// Why a joiner's blow did not go through the engine (the WO173-HIT line's fallback=). The raw path (WO-121's apply_attributed, the
// stamina capped) then applies it -- exactly one of the two, never both, never neither.
enum class Fallback : uint8_t {
    None = 0, Off = 1, Faulted = 2, NotArmed = 3, NoRow = 4, NoSwingWindow = 5, NoVictim = 6, VictimDead = 7, Replay = 8, Filtered = 9,
    CoreNotRun = 10,
    Arranged = 11,   // WO-176 (A1): the victim is in a quest's arranged fight -- neither path runs: the blow is not routed into the story's fight
};
inline const char* fallback_name(Fallback f) {
    switch (f) {
        case Fallback::None: return "-";
        case Fallback::Off: return "mp_real_hits-off";
        case Fallback::Faulted: return "switched-off-by-a-fault";
        case Fallback::NotArmed: return "processor-not-armed";
        case Fallback::NoRow: return "no-swing-row";
        case Fallback::NoSwingWindow: return "no-swing-window";
        case Fallback::NoVictim: return "no-victim";
        case Fallback::VictimDead: return "victim-dead";
        case Fallback::Replay: return "replay-refused";
        case Fallback::Filtered: return "engine-filtered";
        case Fallback::CoreNotRun: return "engine-core-not-run";
        case Fallback::Arranged: return "arranged-fight";
        default: return "?";
    }
}

struct Pre {
    bool on = true, faulted = false, armed = true, rowKnown = true, striking = true, victimFound = true, victimDead = false;
    bool arranged = false;   // WO-176 (A1)
};
// The preconditions in their order (the first that fails names the fallback). An arranged fight comes first: whatever the switches, the
// blow goes nowhere (routes_nowhere below).
inline Fallback precondition(const Pre& p) {
    if (p.arranged) return Fallback::Arranged;
    if (!p.on) return Fallback::Off;
    if (p.faulted) return Fallback::Faulted;
    if (!p.armed) return Fallback::NotArmed;
    if (!p.rowKnown) return Fallback::NoRow;
    if (!p.victimFound) return Fallback::NoVictim;
    if (p.victimDead) return Fallback::VictimDead;
    if (!p.striking) return Fallback::NoSwingWindow;
    return Fallback::None;
}

// WO-176 (A1): the one fallback that takes neither the engine path nor the raw path.
inline bool routes_nowhere(Fallback f) { return f == Fallback::Arranged; }

// The engine's own outcome (wo165_rules.h Outcome: 0 none, 1 hit, 2 blocked, 3 perfect block, 4 broken, 5 filtered) -> who applies.
// Applied by the engine = the processor ran its core on this blow (hit or any block). Filtered / not run = the raw path.
inline Fallback after_engine(uint8_t outcome) {
    if (outcome == 5) return Fallback::Filtered;
    if (outcome == 0) return Fallback::CoreNotRun;
    return Fallback::None;
}

// ---------------------------------------------------------------------------------------------------------------- the capped raw path
// One raw blow never takes more than this share of the NPC's maximum stamina (the field: a raw stamina loss floored a fighting guard
// at once -- the engine's knock-out on stamina). The maximum is the highest stamina seen on that NPC this session, at least the floor.
constexpr float kStaminaCapShare = 0.35f;
constexpr float kStaminaMaxFloor = 100.0f;
inline float stamina_max_estimate(float seenMax, float now) {
    float m = kStaminaMaxFloor;
    if (std::isfinite(seenMax) && seenMax > m) m = seenMax;
    if (std::isfinite(now) && now > m) m = now;
    return m;
}
inline float cap_stamina(float st, float maxEst) {
    if (!std::isfinite(st) || st <= 0) return 0.0f;
    const float cap = kStaminaCapShare * maxEst;
    return st > cap ? cap : st;
}

// ---------------------------------------------------------------------------------------------------------------- the damage it took
// The engine computes a blow's damage from the ATTACKER's weapon and stats (research/WO-162 Q1.2: the record holds no amount; WO-165 P2:
// a slash took hp -21 st -40) -- here the figure's, not the joiner's. For a landed hit the NPC's health loss is brought to what the
// joiner's own game measured for the same blow (his weapon, his skills); a blocked blow keeps the engine's (the block's own factor on
// the figure's weapon -- the joiner's measure was of an unblocked blow on his copy). Stamina stays the engine's (its block / knock-out
// rules). Never raises a body the engine killed, never raises above the health before the blow.
struct Correction { bool apply = false; float extraDamage = 0, raiseBy = 0; };
inline Correction health_correction(uint8_t outcome, float engineHp, float joinerHp, float hpNow) {
    Correction c{};
    if (outcome != 1 || !std::isfinite(engineHp) || !std::isfinite(joinerHp) || !std::isfinite(hpNow) || joinerHp < 0 || engineHp < 0) return c;
    if (hpNow <= 0.01f) return c;   // dead or knocked to zero by the engine: its decision stands
    const float d = joinerHp - engineHp;
    if (std::fabs(d) < 0.05f) return c;
    c.apply = true;
    if (d > 0) c.extraDamage = d;
    else c.raiseBy = -d;
    return c;
}

// The window the measured damage waits for (hits.cpp's replay watch reports after 0.6 s), and the reaction read.
constexpr double kReactionReadS = 0.25;   // the victim's combat State is read this long after the blow (0x40 = Hit, 0x100 parry ...)
constexpr double kPendingMaxS = 2.0;      // a blow whose measure never came is logged unmeasured after this
constexpr double kStrikeHorizonS = 2.0;   // wo166::striking_recently's own horizon: the figure was in a strike window this recently

// ---------------------------------------------------------------------------------------------------------------- C2: down / up
// An NPC's down state as the host reports it (W164 NpcDown): 0 standing, 1 knocked out (IsUnconscious), 2 dead (WO-172's own path
// carries deaths; only logged). A change is reported once, a knock-out again every kDownRepeatS while it holds (a lost message recovers).
constexpr double kDownRepeatS = 3.0;
struct DownTrack { uint8_t state = 0; double sentAt = -1e9; bool known = false; };
// true = send now
inline bool down_should_send(DownTrack& t, uint8_t now, double tNow) {
    if (!t.known || now != t.state) { t.known = true; t.state = now; t.sentAt = tNow; return true; }
    if (now == 1 && tNow - t.sentAt >= kDownRepeatS) { t.sentAt = tNow; return true; }   // a knock-out repeats; a death is said once
    return false;
}

} // namespace kcdmp::wo173rules
