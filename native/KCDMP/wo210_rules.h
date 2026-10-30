// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-210: the native half's pure rules, engine-free so native/tests pins them.
#include <cstddef>
#include <cstdint>
#include <cstring>
#include "wo208_rules.h"

namespace kcdmp::wo210rules {

// ---- the agent's switch word (op 138), the same bits as Wo210Rules.ConfigBits; every bit on until the agent says otherwise ----
constexpr uint32_t kBitStateAlways = 0x01;   // A mp_copy_state_always: the hand-over at every exit path, the 250 ms watchdog
constexpr uint32_t kBitClinchV2 = 0x02;      // B reserved: clinch v2 (B.2/B.3) is not in this build, never set
constexpr uint32_t kBitDeathEngine = 0x04;   // C.1/C.2 mp_death_engine
constexpr uint32_t kBitKnockdownSync = 0x08; // C.3 mp_knockdown_sync
constexpr uint32_t kBitCopyReady = 0x10;     // D.3/D.4 mp_copy_ready
constexpr uint32_t kBitHitLocalNone = 0x20;  // E.2 mp_copy_hit_local_none
constexpr uint32_t kBitRiderSync = 0x40;     // G mp_rider_sync
constexpr uint32_t kBitOneRequest = 0x80;    // H.1 mp_state_one_request
constexpr uint32_t kBitsDefault = kBitStateAlways | kBitDeathEngine | kBitKnockdownSync | kBitCopyReady | kBitHitLocalNone |
                                  kBitRiderSync | kBitOneRequest;
// Fixes with no switch of their own, on unless the bench's twin turns them off (never in the agent's word).
constexpr uint32_t kFixClinchFinal = 0x100;   // B.1: a refused clinch attempt ends (no 4-a-frame loop): clinch_refusal_final
constexpr uint32_t kFixEmptyHostNeutral = 0x200;   // A: a host scope with nothing in it hands over the copy's neutral idle: empty_host_neutral
constexpr uint32_t kFixPostureOtherGrace = 0x400;  // A.5: a posture against the host's unnamed fragment waits 2.5 s: posture_other_grace
constexpr uint32_t kFixNoRawWindow = 0x800;        // C.4: a blow with no strike window goes through the engine, not raw: no_raw_window
constexpr uint32_t kFixKdByAction = 0x1000;        // C.3: the knockdown's own action marks the host's body down at once: kd_by_action
constexpr uint32_t kFixFightBaseOwn = 0x2000;      // A: a fight's base is the copy's own engine's (no host footwork installed): fight_base_own
constexpr uint32_t kFixNoJoinerValues = 0x4000;    // C.4: the host applies none of the joiner's numbers (no correction, no raw blow): no_joiner_values
constexpr uint32_t kFixHitFx = 0x8000;             // E.2: a filtered blow on a copy still plays its impact (material effects only): hit_fx
constexpr uint32_t kFixFightHeal = 0x10000;        // E.3: a copy held in the joiner's fight but out of the engine's skirmish relation is healed: fight_heal
constexpr uint32_t kFixAiInFight = 0x20000;        // E.1: the copy's own attack only inside his fight; else the host's attack the owner's way: ai_in_fight

struct Named { const char* name; uint32_t bit; };
inline constexpr Named kNames[] = {
    {"copy_state_always", kBitStateAlways}, {"clinch_v2", kBitClinchV2}, {"death_engine", kBitDeathEngine},
    {"knockdown_sync", kBitKnockdownSync}, {"copy_ready", kBitCopyReady}, {"copy_hit_local_none", kBitHitLocalNone},
    {"rider_sync", kBitRiderSync}, {"state_one_request", kBitOneRequest}, {"clinch_refusal_final", kFixClinchFinal},
    {"empty_host_neutral", kFixEmptyHostNeutral}, {"posture_other_grace", kFixPostureOtherGrace}, {"no_raw_window", kFixNoRawWindow}, {"kd_by_action", kFixKdByAction}, {"fight_base_own", kFixFightBaseOwn},
    {"no_joiner_values", kFixNoJoinerValues}, {"hit_fx", kFixHitFx}, {"fight_heal", kFixFightHeal}, {"ai_in_fight", kFixAiInFight},
};
inline uint32_t twin_off_bits(const char* list) {
    uint32_t b = 0;
    for (const Named& x : kNames) if (wo208rules::list_has(list, x.name)) b |= x.bit;
    return b;
}

// ---- A: the whole-body family a scope's action shows -------------------------------------------------------------------------
// The posture comes from the game's own fragment name (the mannequin database's fragment-ID tags; male and female tables differ in
// numbers, never in names). The base classes say standing / moving / combat on their own; a posture class (the idle state, a fidget,
// the owner's own C_CombatAnimAction carrying the host's fragment) says it by name.
enum class PFam : uint8_t { Empty = 0, Standing = 1, Moving = 2, Combat = 3, Lying = 4, Sitting = 5, Kneeling = 6, Working = 7, Dead = 8,
                            Paired = 9, Other = 10 };
inline const char* pfam_name(PFam f) {
    switch (f) {
        case PFam::Empty: return "empty"; case PFam::Standing: return "standing"; case PFam::Moving: return "moving"; case PFam::Combat: return "combat";
        case PFam::Lying: return "lying"; case PFam::Sitting: return "sitting"; case PFam::Kneeling: return "kneeling"; case PFam::Working: return "working";
        case PFam::Dead: return "dead"; case PFam::Paired: return "paired"; default: return "other";
    }
}
inline bool has(const char* s, const char* sub) { return s && sub && std::strstr(s, sub) != nullptr; }
inline bool starts(const char* s, const char* p) { return s && p && std::strncmp(s, p, std::strlen(p)) == 0; }
inline bool ends(const char* s, const char* e) {
    if (!s || !e) return false;
    const size_t a = std::strlen(s), b = std::strlen(e);
    return a >= b && std::strcmp(s + a - b, e) == 0;
}
// A posture's way in or out (LieDown / GetUp / SitDown / StandUp / IdleToMove / MoveToIdle / LyingDialogIn / SleepOnTableOut / ...To...):
// the game's own transition between two families, allowed on its way (A.1).
inline bool transition_name(const char* n) {
    if (!n || !*n) return false;
    return has(n, "GetUp") || has(n, "StandUp") || has(n, "SitDown") || has(n, "LieDown") || has(n, "Laydown") || has(n, "IdleToMove") ||
           has(n, "MoveToIdle") || has(n, "Standup") || has(n, "Sitdown") || ends(n, "In") || ends(n, "Out") || has(n, "ToIdle") || has(n, "ToMove") ||
           has(n, "ToCombat") || has(n, "Transition") || has(n, "_in") || has(n, "_out");
}
// The family a fragment's name says, before its class is considered (Other = the name says no posture).
inline PFam posture_of_name(const char* n) {
    if (!n || !*n) return PFam::Other;
    if (has(n, "Sync") || has(n, "Clinch") || has(n, "Master") || has(n, "Slave")) return PFam::Paired;
    if (has(n, "Death") || has(n, "Dead") || has(n, "Hanged")) return PFam::Dead;
    if (has(n, "Lying") || has(n, "LieDown") || has(n, "Sleep") || has(n, "Laydown") || has(n, "Ragdoll") || has(n, "_Lie") || has(n, "idleLie") ||
        has(n, "FaceDown") || has(n, "Unconscious"))
        return PFam::Lying;
    if (has(n, "Sitting") || has(n, "SitDown") || has(n, "Sitdown") || has(n, "Bench") || has(n, "Chair") || has(n, "_Sit") || has(n, "Sit_") ||
        starts(n, "Sit"))
        return PFam::Sitting;
    if (has(n, "Kneel") || has(n, "Surrender") || has(n, "Yield")) return PFam::Kneeling;
    if (!std::strcmp(n, "MotionIdle") || starts(n, "MotionIdle")) return PFam::Standing;
    if (starts(n, "Motion") || !std::strcmp(n, "IdleToMove") || !std::strcmp(n, "MoveToIdle")) return PFam::Moving;
    if (starts(n, "Combat")) return PFam::Combat;
    return PFam::Other;
}
// The family of one installed action: its class (wo208rules class ids) and its fragment's name ("" when unknown).
inline PFam family(uint8_t cls, const char* fragName, uint32_t frag) {
    using namespace wo208rules;
    const PFam byName = posture_of_name(fragName);
    switch (cls) {
        case kClsNone: return PFam::Empty;
        case kClsAIMovement:
            if (byName == PFam::Lying || byName == PFam::Sitting || byName == PFam::Kneeling) return byName;
            return standing_idle_frag(frag) && (!fragName || !*fragName || byName == PFam::Standing) ? PFam::Standing : PFam::Moving;
        case kClsMoveTransition: case kClsPlayerMove: return PFam::Moving;
        case kClsCombatMove: return PFam::Combat;
        case kClsIdleState: case kClsVariation:
            if (byName == PFam::Lying || byName == PFam::Sitting || byName == PFam::Kneeling || byName == PFam::Dead) return byName;
            if (byName == PFam::Standing || (fragName && !*fragName && standing_idle_frag(frag))) return PFam::Standing;
            return cls == kClsVariation ? PFam::Standing : PFam::Working;   // a fidget of the standing idle; any other idle state is work
        case kClsCombatAnim:
            if (byName != PFam::Other) return byName;
            return wo208rules::paired_frag(frag) ? PFam::Paired : PFam::Other;
        default:
            return byName == PFam::Lying || byName == PFam::Sitting || byName == PFam::Kneeling || byName == PFam::Dead ? byName : PFam::Other;
    }
}
inline bool posture(PFam f) { return f == PFam::Lying || f == PFam::Sitting || f == PFam::Kneeling || f == PFam::Working; }
inline bool base(PFam f) { return f == PFam::Standing || f == PFam::Moving || f == PFam::Combat; }

// ---- A.5 the watchdog's verdict on one copy at one check ----------------------------------------------------------------------
// copy: the family its FullBody runs; host: the host NPC's current streamed family; copyTransition: the copy's fragment is a posture's
// way in or out; modAction: a mod action whose start the host named runs (the copy's own attack, a work one-shot, a paired half, the
// engine's own hit reaction or recoil after a blow).
enum class Verdict : uint8_t { Ok = 0, Empty = 1, StalePosture = 2, MissingPosture = 3, StaleDead = 4, PostureOther = 5 };
constexpr int kVerdictCount = 6;
inline const char* verdict_name(Verdict v) {
    switch (v) { case Verdict::Empty: return "empty"; case Verdict::StalePosture: return "stale-posture"; case Verdict::MissingPosture: return "missing-posture";
                 case Verdict::StaleDead: return "stale-dead"; case Verdict::PostureOther: return "posture-vs-other"; default: return "ok"; }
}
// otherSure: the host's "other" (a fragment no family names: a seated gesture, an interaction, a callback) counts as a breach of the
// copy's posture as soon as a base would (the twin posture_other_grace off); on, it gets PostureOther's longer grace -- d6-waitnight's
// seated drinkers were reseated at every gesture of their host NPC (356 reseats, the copy a half second behind each time)
inline Verdict check(PFam copy, PFam host, bool copyTransition, bool modAction, bool otherSure = false) {
    if (copy == PFam::Empty) return Verdict::Empty;
    if (modAction || copyTransition || copy == host) return Verdict::Ok;
    if (copy == PFam::Dead && host != PFam::Dead && host != PFam::Empty) return Verdict::StaleDead;
    if (posture(copy) && host == PFam::Other && !otherSure) return Verdict::PostureOther;               // a gesture in the posture, most likely
    if (posture(copy) && host != PFam::Empty && host != PFam::Paired) return Verdict::StalePosture;   // the copy lies / sits where its NPC does not
    if (posture(host) && !posture(copy)) return Verdict::MissingPosture;                               // the NPC lies / sits, the copy stands
    return Verdict::Ok;
}
// How long a verdict may last before the copy is reseated (A.1: a stale posture at most 0.3 s; an empty FullBody 0.3 s -- the detector's
// own threshold; a missing posture gives the owner's own delayed install its time first; a posture against an unnamed fragment of the
// host's gives the owner's install of that fragment its time).
inline double breach_after_s(Verdict v) {
    switch (v) { case Verdict::Empty: return 0.3; case Verdict::StalePosture: return 0.3; case Verdict::StaleDead: return 0.3;
                 case Verdict::MissingPosture: return 1.0; case Verdict::PostureOther: return 2.5; default: return 1e9; }
}
constexpr double kWatchEveryS = 0.25;   // A.5: each owned copy every 250 ms
constexpr float kWatchRadiusM = 60.0f;  // within this of the local player
constexpr double kTransitionMaxS = 4.0; // a transition fragment that runs longer than this is no transition any more
constexpr double kPostureResyncS = 2.5; // A.3: a posture the stream has not confirmed this long is asked again

// The exit paths of A.2 (the reseat line's path=; the hand-over's own counters).
enum Path : uint8_t {
    kPathUnknown = 0, kPathDrift = 1, kPathFar = 2, kPathStale = 3, kPathAttackEnd = 4, kPathClinch = 5, kPathDeath = 6, kPathGetUp = 7,
    kPathConvEnd = 8, kPathSceneEnd = 9, kPathSkipEnd = 10, kPathLoad = 11, kPathSwitch = 12, kPathFault = 13, kPathBind = 14, kPathBase = 15,
    kPathInstallPending = 16,   // no exit: the host's posture, not shown yet by the owner's own install
    kPathOwnPosture = 17,       // no exit: the copy's own engine posture (its NPC state's bed, seat, workstation)
    kPathOverflow = 18,         // WO-211 C.3: the engine said the copy's action queue is over-full -- a clean reseat
    kPathCount = 19,
};
inline const char* path_name(uint8_t p) {
    static const char* k[] = {"unknown", "1-drift", "2-far", "3-stale", "4-attack-end", "5-clinch", "6-death", "7-get-up", "8-conv-end", "9-scene-end",
                              "10-skip-end", "11-load", "12-switch", "13-fault", "14-bind", "15-host-base", "16-install-pending", "17-own-posture", "18-overflow"};
    return p < kPathCount ? k[p] : "?";
}
// The path a reseat is charged to: the last exit recorded on the copy within kPathWindowS, else unknown (a bug to find).
constexpr double kPathWindowS = 6.0;
inline uint8_t charged_path(uint8_t lastPath, double lastAt, double now) { return (lastPath && now - lastAt <= kPathWindowS) ? lastPath : kPathUnknown; }

// ---- B.1 the clinch-loop detector: more than 3 asks or refusals for one copy within 1 s, told once per stretch (a stretch ends after
// 2 s without one) ----
constexpr int kLoopAsks = 3;
constexpr double kLoopWindowS = 1.0, kLoopQuietS = 2.0;
struct Burst { double t[8]{}; int n = 0; double last = -1e9; bool told = false; uint32_t inStretch = 0; };
// One ask at `now`; true when this ask makes the burst a loop that has not been told in this stretch.
inline bool burst_ask(Burst& b, double now) {
    if (now - b.last > kLoopQuietS) { b.n = 0; b.told = false; b.inStretch = 0; }
    b.last = now;
    ++b.inStretch;
    if (b.n < 8) b.t[b.n++] = now;
    else { for (int i = 1; i < 8; ++i) b.t[i - 1] = b.t[i]; b.t[7] = now; }
    int within = 0;
    for (int i = 0; i < b.n; ++i) if (now - b.t[i] <= kLoopWindowS) ++within;
    if (within > kLoopAsks && !b.told) { b.told = true; return true; }
    return false;
}

// ---- C.3 knockdowns (mp_knockdown_sync): the host's stream says a body is down (its physics no longer a living entity: a knockdown's
// ragdoll, a knock-out, a body that just died); the copy follows by edges. On the host's down edge the copy falls the engine's way (its
// ragdoll manager's own RequestRagdoll); on the host's up edge a copy still lying is stood up the engine's way (RequestStandUp); in
// between the copy is held: the owner installs nothing and the position stream writes nothing, the engine's ragdoll has the body. A copy
// that dies is let go (it lies as it died, C.2); a copy its own engine stood up first while the host's still lies stays up (counted, never
// knocked down twice for one host fall).
// The stream's per-body byte (NpcAnim [cm:1]): bit 0 combat mode, bit 1 down.
constexpr uint8_t kCmCombat = 0x01, kCmDown = 0x02;
constexpr double kKdOwnUpGraceS = 1.0;   // a held copy not ragdolled this long after its fall was stood up by its own engine
enum class KdAct : uint8_t { None = 0, Fall = 1, StandUp = 2, Release = 3 };
struct KdState { bool hostDown = false, held = false, upPending = false; double fellAt = -1; };
inline const char* kd_act_name(KdAct a) {
    switch (a) { case KdAct::Fall: return "fall"; case KdAct::StandUp: return "stand-up"; case KdAct::Release: return "release"; default: return "none"; }
}
// One step: the switch, the host's newest down bit, the copy's ragdoll, whether the copy is dead. Returns what to do now; the state moves.
inline KdAct kd_step(KdState& s, bool on, bool hostDown, bool copyRagdoll, bool copyDead, double now) {
    if (copyDead) { const bool was = s.held; s = KdState{}; s.hostDown = hostDown; return was ? KdAct::Release : KdAct::None; }
    if (!on) { const bool was = s.held; s = KdState{}; return was ? (copyRagdoll ? KdAct::StandUp : KdAct::Release) : KdAct::None; }
    if (hostDown && !s.hostDown) {                       // the host's down edge
        s.hostDown = true; s.upPending = false;
        s.held = true; s.fellAt = now;
        return copyRagdoll ? KdAct::None : KdAct::Fall;  // already lying (a local blow): held as it is
    }
    if (!hostDown && s.hostDown) {                       // the host's up edge
        s.hostDown = false;
        if (s.held) s.upPending = true;
    }
    if (s.held && s.upPending) { s = KdState{}; return copyRagdoll ? KdAct::StandUp : KdAct::Release; }
    if (s.held && s.hostDown && !copyRagdoll && now - s.fellAt >= kKdOwnUpGraceS) { s.held = false; return KdAct::Release; }   // its engine stood it up
    return KdAct::None;
}

// ---- E.2: the local player's blow on a copy is filtered the engine's way -------------------------------------------------------
// C_CombatRPG::RPGProcessHit returning false is the engine's own "Hit was filtered" (its repeat-hit filter): the handler then skips
// the core (damage, stamina, wear, crime), the combat reaction and the victim's hit event. For the local player's blow on a copy the
// gate runs what RPGProcessHit runs -- the attacker actor's lock, the core -- with the core's apply argument 0, the form the engine's
// own two dry-run callers use (record and result filled, nothing applied), mirrors its strength epilogue and returns false.
// Read from RPGProcessHit's own code (1.5.5 offsets in brackets, for orientation only):
//   mov rax,[rbp+8]; mov r15,[rax+MODEL]; mov rax,[r15+TYPE]; lea rcx,[r15+TYPE]; call [rax+8]; cmp eax,[rip+NONE]; jne +7; mov al,1
//   call TUNING; xorps xmm2,xmm2; mov rcx,rbx; movss xmm1,[rax+TO]; call ..                       (the repeat timer's length)
//   mov rdx,[rbp+8]; lea r8,[rip+LABEL]; lea rcx,[rsp+68h]; call SCOPE; mov byte [rsp+0B0h],0     (the actor lock, intentional := 0)
//   lea rcx,[r15+STR]; mov rax,[r15+STR]; call [rax+8]; movss [rsp+40h],xmm0                      (strength)
//   mov [rsp+38h],r12; lea r9,[rsp+0B0h]; mov [rsp+30h],eax; lea r8,[rsp+40h]; mov byte [rsp+28h],1; mov rdx,r13; mov rcx,rbp;
//   mov [rsp+20h],rdi; call CORE                    (rpg, result, &strength, &intentional, hitIn, apply, type, flags)
//   lea rcx,[r15+STR]; je +5; xorps xmm1,xmm1; jmp +6; movss xmm1,[rsp+40h]; call SETTER           (strength := 0 after the target)
//   lea rcx,[rsp+68h]; call UNSCOPE; mov al,1
constexpr int16_t kW = -1;   // any byte
inline bool w210_match(const uint8_t* p, const int16_t* pat, size_t n) {
    for (size_t i = 0; i < n; ++i) if (pat[i] >= 0 && p[i] != static_cast<uint8_t>(pat[i])) return false;
    return true;
}
// the one offset in [0, n - len] where pat matches, or -1 (none, or more than one)
inline long w210_find_once(const uint8_t* code, size_t n, const int16_t* pat, size_t len) {
    long at = -1;
    for (size_t o = 0; o + len <= n; ++o) if (w210_match(code + o, pat, len)) { if (at >= 0) return -1; at = static_cast<long>(o); }
    return at;
}
inline uint32_t w210_u32(const uint8_t* p) { uint32_t v; std::memcpy(&v, p, 4); return v; }
inline uintptr_t w210_rel(uintptr_t codeAddr, const uint8_t* code, size_t at, size_t end) {   // rel32 at code+at, relative to end
    int32_t r; std::memcpy(&r, code + at, 4); return codeAddr + end + static_cast<intptr_t>(r);
}
constexpr int16_t kRphType[34] = {0x48, 0x8B, 0x45, 0x08, 0x4C, 0x8B, 0xB8, kW, kW, kW, kW, 0x49, 0x8B, 0x87, kW, kW, kW, kW, 0x49, 0x8D, 0x8F,
                                  kW, kW, kW, kW, 0xFF, 0x50, 0x08, 0x3B, 0x05, kW, kW, kW, kW};
constexpr int16_t kRphTypeTail[4] = {0x75, 0x07, 0xB0, 0x01};
//   mov rbx,[rdi+DET]; lea rcx,[rbx+VID]; call ..         (the hit-in's details -> the victim's entity id, the repeat map's key)
constexpr int16_t kRphVictim[9] = {0x48, 0x8B, 0x5F, kW, 0x48, 0x8D, 0x4B, kW, 0xE8};
constexpr int16_t kRphTimeout[20] = {0xE8, kW, kW, kW, kW, 0x0F, 0x57, 0xD2, 0x48, 0x8B, 0xCB, 0xF3, 0x0F, 0x10, 0x88, kW, kW, kW, kW, 0xE8};
constexpr int16_t kRphScope[29] = {0x48, 0x8B, 0x55, 0x08, 0x4C, 0x8D, 0x05, kW, kW, kW, kW, 0x48, 0x8D, 0x4C, 0x24, 0x68, 0xE8, kW, kW, kW, kW,
                                   0xC6, 0x84, 0x24, 0xB0, 0x00, 0x00, 0x00, 0x00};
constexpr int16_t kRphStr[23] = {0x49, 0x8D, 0x8F, kW, kW, kW, kW, 0x49, 0x8B, 0x87, kW, kW, kW, kW, 0xFF, 0x50, 0x08, 0xF3, 0x0F, 0x11, 0x44,
                                 0x24, 0x40};
constexpr int16_t kRphCore[43] = {0x4C, 0x89, 0x64, 0x24, 0x38, 0x4C, 0x8D, 0x8C, 0x24, 0xB0, 0x00, 0x00, 0x00, 0x89, 0x44, 0x24, 0x30, 0x4C, 0x8D,
                                  0x44, 0x24, 0x40, 0xC6, 0x44, 0x24, 0x28, 0x01, 0x49, 0x8B, 0xD5, 0x48, 0x8B, 0xCD, 0x48, 0x89, 0x7C, 0x24, 0x20,
                                  0xE8, kW, kW, kW, kW};
constexpr int16_t kRphSetter[25] = {0x49, 0x8D, 0x8F, kW, kW, kW, kW, 0x74, 0x05, 0x0F, 0x57, 0xC9, 0xEB, 0x06, 0xF3, 0x0F, 0x10, 0x4C, 0x24, 0x40,
                                    0xE8, kW, kW, kW, kW};
constexpr int16_t kRphUnscope[12] = {0x48, 0x8D, 0x4C, 0x24, 0x68, 0xE8, kW, kW, kW, kW, 0xB0, 0x01};
// the core's own prologue (push rbp .. r15; lea rbp,[rsp-88h]; sub rsp,188h)
inline constexpr uint8_t kRphCorePrologue[28] = {0x40, 0x55, 0x53, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57, 0x48,
                                                 0x8D, 0xAC, 0x24, 0x78, 0xFF, 0xFF, 0xFF, 0x48, 0x81, 0xEC, 0x88, 0x01, 0x00, 0x00};
struct RphSites {
    uint32_t modelOff = 0, typeOff = 0, strOff = 0, timeoutOff = 0;
    uint8_t detOff = 0, vidOff = 0;   // hit-in -> details, details -> victim entity id
    uintptr_t noneType = 0, tuning = 0, scopeCtor = 0, scopeLabel = 0, scopeDtor = 0, core = 0, setter = 0;
};
// code = RPGProcessHit's first n bytes (read at codeAddr). Every piece once, the type and strength offsets each read twice the same.
inline bool parse_rph(const uint8_t* code, size_t n, uintptr_t codeAddr, RphSites* out) {
    RphSites s{};
    const long t = w210_find_once(code, n, kRphType, 34);
    if (t < 0 || t + 38 > static_cast<long>(n) || !w210_match(code + t + 34, kRphTypeTail, 4)) return false;
    s.modelOff = w210_u32(code + t + 7);
    s.typeOff = w210_u32(code + t + 14);
    if (w210_u32(code + t + 21) != s.typeOff) return false;
    s.noneType = w210_rel(codeAddr, code, static_cast<size_t>(t + 30), static_cast<size_t>(t + 34));
    const long vi = w210_find_once(code, n, kRphVictim, 9);
    if (vi < 0) return false;
    s.detOff = code[vi + 3];
    s.vidOff = code[vi + 7];
    const long to = w210_find_once(code, n, kRphTimeout, 20);
    if (to < 0) return false;
    s.tuning = w210_rel(codeAddr, code, static_cast<size_t>(to + 1), static_cast<size_t>(to + 5));
    s.timeoutOff = w210_u32(code + to + 15);
    const long sc = w210_find_once(code, n, kRphScope, 29);
    if (sc < 0) return false;
    s.scopeLabel = w210_rel(codeAddr, code, static_cast<size_t>(sc + 7), static_cast<size_t>(sc + 11));
    s.scopeCtor = w210_rel(codeAddr, code, static_cast<size_t>(sc + 17), static_cast<size_t>(sc + 21));
    const long st = w210_find_once(code, n, kRphStr, 23);
    if (st < 0) return false;
    s.strOff = w210_u32(code + st + 3);
    if (w210_u32(code + st + 10) != s.strOff) return false;
    const long co = w210_find_once(code, n, kRphCore, 43);
    if (co < 0) return false;
    s.core = w210_rel(codeAddr, code, static_cast<size_t>(co + 39), static_cast<size_t>(co + 43));
    const long se = w210_find_once(code, n, kRphSetter, 25);
    if (se < 0 || w210_u32(code + se + 3) != s.strOff) return false;
    s.setter = w210_rel(codeAddr, code, static_cast<size_t>(se + 21), static_cast<size_t>(se + 25));
    const long us = w210_find_once(code, n, kRphUnscope, 12);
    if (us < 0) return false;
    s.scopeDtor = w210_rel(codeAddr, code, static_cast<size_t>(us + 6), static_cast<size_t>(us + 10));
    // the order RPGProcessHit runs them in: type test, the victim's id, repeat timer, scope, strength, core, setter, unscope
    if (!(t < vi && vi < to && to < sc && sc < st && st < co && co < se && se < us)) return false;
    if (s.modelOff == 0 || s.modelOff > 0x2000 || s.typeOff > 0x2000 || s.strOff > 0x2000 || s.timeoutOff > 0x2000) return false;
    *out = s;
    return true;
}
// E.3 (fight_heal): a copy the mod holds in the joiner's fight whose relation with him the engine keeps answering "not in a fight"
// (refused on every check for kHealAfterS, gaps under kHealGapS) is healed the engine's way -- its stale fight ended (StopFight) and every
// copy's hold in his fight let go, so the mirror engages each again with a fresh add -- at most once every kHealEveryS.
constexpr double kHealAfterS = 1.0, kHealGapS = 0.5, kHealEveryS = 5.0;
struct HealTrack { double since = -1, last = -1; };
inline bool heal_step(HealTrack& t, double now, double lastHealAt) {
    if (t.last < 0 || now - t.last > kHealGapS) t.since = now;   // a new stretch of refusals
    t.last = now;
    return now - t.since >= kHealAfterS && now - lastHealAt >= kHealEveryS;
}
// E.1 (ai_in_fight): the copy's own attack at the local player (WO-209 B) claims a host attack only while the engine holds the copy in his
// fight (the relation F.1 checks before that attack). A copy its own engine retargeted to the host's figure (in the figure's fight) had its
// every attack claimed by that path and then refused by F.1 -- nothing shown, the blow the host's (d19-fight3: 32 host attacks at the
// joiner from such copies, 2 shown; from copies on him 97 %) -- it shows the host's attack the owner's way instead (the fragment; the
// host judges the blow, as for every copy not on him).
inline bool ai_claims_attack(bool aiWants, bool fixOn, bool inHisFight) { return aiWants && (!fixOn || inHisFight); }

// The engine's own dry runs: a call of the core with `mov byte [rsp+28h],0` (apply = 0) within this many bytes before it.
constexpr size_t kDryRunWindow = 0x40;
inline bool dry_run_call(const uint8_t* before, size_t n) {
    static const uint8_t kApply0[5] = {0xC6, 0x44, 0x24, 0x28, 0x00};
    for (size_t o = 0; o + 5 <= n; ++o) if (!std::memcmp(before + o, kApply0, 5)) return true;
    return false;
}

// The engine's repeat filter, mirrored: one swing passing through a body hits it once (per victim, the tuning's timeout); a hit
// whose hit-in byte 0 is set (the engine skips its filter) always counts. A safety cap stops a flood (more than kHitCapPerS a second).
constexpr int kRepeatSlots = 16;
constexpr double kRepeatDefaultS = 0.5;   // until the tuning's own value is read
constexpr int kHitCapPerS = 8;
struct RepeatWin { uint32_t eid[kRepeatSlots]{}; double at[kRepeatSlots]{}; double capFrom = -1e9; int capN = 0; };
// true = the engine would filter this contact (no report, nothing played)
inline bool repeat_filtered(RepeatWin& w, uint32_t eid, bool skipFilter, double now, double timeoutS) {
    if (!skipFilter) {
        int slot = -1, oldest = 0;
        for (int i = 0; i < kRepeatSlots; ++i) {
            if (w.eid[i] == eid) { slot = i; break; }
            if (w.at[i] < w.at[oldest]) oldest = i;
        }
        if (slot >= 0 && now - w.at[slot] < timeoutS) return true;
        if (slot < 0) slot = oldest;
        w.eid[slot] = eid; w.at[slot] = now;
    }
    if (now - w.capFrom >= 1.0) { w.capFrom = now; w.capN = 0; }
    return ++w.capN > kHitCapPerS;
}
// The impact on the copy: the filled result with only "allow material effects" (the form the core itself gives item hits), handled
// (no dead action from it) and both lethal bytes clear (no death reaction).
constexpr uint32_t kFxFlags = 4;
inline void fx_edit(uint8_t* r, uint32_t flagsOff, uint32_t handledOff, uint32_t lethal0, uint32_t lethal1) {
    std::memcpy(r + flagsOff, &kFxFlags, 4);
    r[handledOff] = 1;
    r[lethal0] = 0;
    r[lethal1] = 0;
}

} // namespace kcdmp::wo210rules
