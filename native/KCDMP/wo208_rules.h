// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-208: the native half's pure rules, engine-free so native/tests pins them.
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>

namespace kcdmp::wo208rules {

// ---- the agent's switch word (op 130), the same bits as Wo208Rules.ConfigBits; every bit on until the agent says otherwise ----
constexpr uint32_t kBitPlayerView = 1;   // C.2 mp_conv_cam_player_view
constexpr uint32_t kBitTargetInFight = 2;   // F.1 mp_target_in_fight: a copy is made the local player's opponent only inside his fight
constexpr uint32_t kBitDefenceLocal = 4;    // B mp_defence_local: a copy's blow on the joiner is judged by the joiner's own engine
constexpr uint32_t kBitOwner = 8;           // A mp_copy_anim_owner: one owner per copy, driven by the host NPC's streamed state
// WO-209: every bit ships on (the maintainer: "all changes shipped ON, and only switched off manually"), the owner and defence_local too
constexpr uint32_t kBitsDefault = kBitPlayerView | kBitTargetInFight | kBitDefenceLocal | kBitOwner;

// The bench's twins: KCDMP_TEST_W208_OFF in the game's own environment names the switches that stay off whatever the agent says
// (a comma list; a name matches whole, never as a part of a longer one).
struct Named { const char* name; uint32_t bit; };
inline constexpr Named kNames[] = {
    {"conv_cam_player_view", kBitPlayerView},
    {"target_in_fight", kBitTargetInFight},
    {"defence_local", kBitDefenceLocal},
    {"copy_anim_owner", kBitOwner},
};
inline bool list_has(const char* list, const char* name) {
    if (!list || !name || !*name) return false;
    const size_t n = std::strlen(name);
    for (const char* p = list; *p;) {
        const char* e = p;
        while (*e && *e != ',') ++e;
        if (static_cast<size_t>(e - p) == n && std::strncmp(p, name, n) == 0) return true;
        p = *e ? e + 1 : e;
    }
    return false;
}
inline uint32_t twin_off_bits(const char* list) {
    uint32_t b = 0;
    for (const Named& x : kNames) if (list_has(list, x.name)) b |= x.bit;
    return b;
}

// ---- F.1 the joiner's fight crash (CombatModule+0x8ff0a on m0's c1fight-2) ----
// WO-187 writes a copy's wished opponent back every 0.5 s. Wished on the local player, it wrote him back onto a copy WO-132 had taken
// out of his fight 17 ms before (tkop_vrahBandit_3, the attacker in the dump): a copy outside the player's skirmish, its combat
// automation back on, with the player as its opponent -- its own slot planner made sub-triggers on the player's perfect-block
// multi-trigger from an update the engine does not keep apart from the player's cleanup, and 4 s later the joiner's game read a freed
// one. The opponent is the local player only while the engine has the copy and the player in a skirmish fight (its own relation test).
// The bench's defence twin (d9, 2026-10-26 02:16:45) crashed the joiner in the same walk with that test in place: WO-132 released two
// copies ("skirmish left") and WO-187 wrote him back 490 and 54 ms later -- the relation test still answered hostile right after the
// release. So the mod's own word comes first: the copy is in his fight only while WO-132 holds it there (its skirmish add), and the
// engine's test must agree.
inline bool target_player_allowed(bool on, bool engaged, bool answered, bool hostile) { return !on || (engaged && answered && hostile); }

// ---- B the joiner's own defence: the outcome his engine gave a copy's blow (the wire's numbers: W203 DefenceOutcome) ----
constexpr uint8_t kDefHit = 1, kDefBlocked = 2, kDefPerfect = 3, kDefBroken = 4, kDefMissed = 5;
// A window that closed with nothing landed and no dodge: no judgement here -- told all the same, so the host applies its own verdict at
// once instead of holding it to the cap (the bench: the copies' row swings land about once a fight, and every other blow on the joiner
// waited the full 450 ms for an outcome that never came). Never a judgement: the joiner keeps applying the host's verdict.
constexpr uint8_t kDefNone = 6;
// The engine's verdict, from the hit record it built (WO-176's reads: block +0x54, perfect +0x56, guard broken +0x57).
inline uint8_t def_outcome(uint8_t blocked, uint8_t perfect, uint8_t broken) {
    if (broken) return kDefBroken;
    if (perfect) return kDefPerfect;
    if (blocked) return kDefBlocked;
    return kDefHit;
}
inline const char* def_outcome_name(uint8_t o) {
    switch (o) { case kDefHit: return "hit"; case kDefBlocked: return "blocked"; case kDefPerfect: return "perfect-block"; case kDefBroken: return "guard-broken";
                 case kDefMissed: return "missed"; case kDefNone: return "none"; default: return "?"; }
}
// A copy's strike window that ended with none of its blows landing on the local player (his dodge, a step back): missed -- told, so
// the host never applies its own judgement of a blow the joiner's engine let pass. A landed blow counts from the window's start.
// The bench's first defence run: the copies' swings (the row path's animation) land on the local player about once a fight (main
// too: 1 local hit against 86-120 host verdicts), so "no blow landed" alone said "missed" for 30 of 32 blows the host's engine had
// as hits -- and the host then sent no verdict: the joiner took no damage at all. A miss is told only with the local player's own
// dodge seen in the window (his combat state 0x200); a window with neither a landed blow nor a dodge tells nothing, and the host's
// verdict applies after its hold, as before B.
inline bool def_window_missed(double windowStart, double lastLanded, bool dodged) { return dodged && lastLanded < windowStart; }
constexpr int32_t kPlayerStateDodge = 0x200;

// ---- C.2 the starter's own view when the talk has no dialogue shot ----
// A running talk (the sampler armed) that has sampled no dialogue shot for this long -- since it armed, or since its last shot -- sends
// the starter's own rendered view instead. Under the listener's 3 s silence rule, over the moment a talk's first shot takes to come.
constexpr int64_t kHeadLiftMs = 1200;
inline bool head_lift(bool on, int64_t nowMs, int64_t lastShotMs, int64_t armedMs) {
    if (!on) return false;
    const int64_t from = lastShotMs > armedMs ? lastShotMs : armedMs;
    return from > 0 && nowMs - from >= kHeadLiftMs;
}

// ---- A: the copy animation owner's rules ----
// The action family of one scope's installed action: the standing idle, locomotion, combat footwork, a posture, an idle variation, and
// every other action by its fragment. The first three are the copy's base: its own engine runs them from its streamed position and its
// held combat mode, so the owner never installs them -- it only hands the scope back. A posture is the host's C_AnimActionIdleState
// with any other fragment than the standing idle (the baseline's host villagers: Lying 45 / 11, SittingIdle 34 / 10, leaning, guarding,
// sweeping -- their copies stood idle on the joiner): the owner installs it like any other action. An idle variation (C_VariationAction:
// a fidget, standing or in a posture) holds: the owner changes nothing for it, so a sitting copy keeps sitting through the host's fidget.
// The standing idle is MotionIdle: fragment 5 in the male table (kcd_male_fragmentids.xml with its 4 imported test tags), 0 in the
// female one (wh_female_fragmentids.xml) -- the female 5 (Cooking) counts as the idle too (left to the copy, as before).
enum class Fam : uint8_t { Empty = 0, Idle = 1, Loco = 2, CombatMove = 3, Other = 4, Hold = 5 };
inline bool standing_idle_frag(uint32_t frag) { return frag == 5 || frag == 0; }
// A.1's fight probe: a paired action (a sync attack, a clinch, a master strike's sync, a stealth grab: kcd_male_fragmentids.xml, the
// "Sync" / "Clinch" / "Master" / "Slave" combat tags) is one half of an action two bodies play together; the copy's half alone never
// starts (the probe's clinch slave: pending, stale four times). Its other half was the joiner's own Henry, whose body the mod never
// drives -- his own engine runs that action (Part B). The owner holds for it.
inline bool paired_frag(uint32_t frag) {
    return (frag >= 63 && frag <= 66) || frag == 135 || frag == 136 || (frag >= 221 && frag <= 226) || frag == 304 || frag == 305 ||
           frag == 418 || frag == 419 || frag == 560 || frag == 561 || frag == 587 || (frag >= 908 && frag <= 919) || (frag >= 921 && frag <= 924);
}
// E.1 (A.1's fight probe: the copy stood while the host's NPC started walking): the walk's start and stop transitions -- IdleToMove /
// MoveToIdle, male 35 / 36, female 18 / 74 -- are installed from the host's state, so the copy's walk starts at the host's moment
// through the game's own start, the way the partner's figure starts its walk; the walk itself stays the copy's own locomotion.
inline bool walk_transition_frag(uint32_t frag) { return frag == 35 || frag == 36 || frag == 18 || frag == 74; }
inline Fam family(const char* cls, uint32_t frag) {
    if (!cls || !*cls) return Fam::Empty;
    if (!std::strcmp(cls, "C_AnimActionIdleState")) return standing_idle_frag(frag) ? Fam::Idle : Fam::Other;
    if (!std::strcmp(cls, "C_VariationAction")) return Fam::Hold;
    if (!std::strcmp(cls, "CAnimActionAIMovement")) return standing_idle_frag(frag) ? Fam::Idle : Fam::Loco;
    if (!std::strcmp(cls, "CAnimActionMovementTransition")) return walk_transition_frag(frag) ? Fam::Other : Fam::Loco;
    if (!std::strcmp(cls, "C_PlayerMovementAction")) return Fam::Loco;
    if (!std::strcmp(cls, "C_CombatAnimActionMovement")) return Fam::CombatMove;
    if (!std::strcmp(cls, "C_CombatAnimAction") && paired_frag(frag)) return Fam::Hold;
    return Fam::Other;
}
inline bool is_base(Fam f) { return f == Fam::Empty || f == Fam::Idle || f == Fam::Loco || f == Fam::CombatMove; }

// Rule 3: an action of ours that has not started within this is removed (and counted).
constexpr double kStaleS = 0.30;
// The priority a withdrawn action of ours steps down to: under every base action (the AI movement 2, combat footwork 4), so the
// copy's own base action -- re-queued behind ours by the engine -- installs again at once (WO-202's step-down, carried to the floor).
constexpr int32_t kWithdrawnPriority = 0;
// Rule 1's base action of ours (the host's base fragment on a copy whose full body stands empty): under the copy's own base actions
// (the AI movement 2, combat footwork 4), so its own, when it comes back, takes the scope; over a withdrawn one.
constexpr int32_t kBasePriority = 1;

// What the owner does on one scope this frame, from the host's scope (its family, its action instance, its priority) and ours.
enum class Do : uint8_t { Nothing = 0, Withdraw = 1, Install = 2, StepPriority = 3, Stale = 4 };
struct Own { bool have = false; uint64_t hostAct = 0; int32_t prio = 0; uint32_t status = 0; double ageS = 0; };
inline Do decide(Fam hostFam, uint64_t hostAct, int32_t hostPrio, bool copyRunsIt, const Own& own) {
    if (hostFam == Fam::Hold) return Do::Nothing;                                 // a fidget: whatever the copy shows stays
    if (is_base(hostFam)) return own.have ? Do::Withdraw : Do::Nothing;            // rule 4: hand back
    if (own.have && own.hostAct == hostAct) {
        if (own.status < 2 && own.ageS > kStaleS) return Do::Stale;                // rule 3
        if (own.prio != hostPrio && own.prio != kWithdrawnPriority) return Do::StepPriority;   // rule 6: the host's own withdraw (5 -> 3)
        return Do::Nothing;
    }
    if (copyRunsIt) return own.have ? Do::Withdraw : Do::Nothing;                 // only the differences: the copy already runs it
    return Do::Install;                                                            // rule 2: the caller withdraws ours first
}

// A.2 the host's stream (op 132 -> the agent -> NpcAnim -> the joiner's op 133): the action's class as a small number (the wire's;
// append-only), and the family the owner acts on.
enum : uint8_t {
    kClsNone = 0, kClsCombatAnim = 1, kClsCombatMove = 2, kClsAIMovement = 3, kClsMoveTransition = 4, kClsIdleState = 5, kClsVariation = 6,
    kClsCallback = 7, kClsDialogAnim = 8, kClsHoldItem = 9, kClsPlayerMove = 10, kClsOther = 255,
};
inline uint8_t class_id(const char* cls) {
    if (!cls || !*cls) return kClsNone;
    static const struct { const char* n; uint8_t id; } k[] = {
        {"C_CombatAnimAction", kClsCombatAnim}, {"C_CombatAnimActionMovement", kClsCombatMove}, {"CAnimActionAIMovement", kClsAIMovement},
        {"CAnimActionMovementTransition", kClsMoveTransition}, {"C_AnimActionIdleState", kClsIdleState}, {"C_VariationAction", kClsVariation},
        {"C_CallbackAction", kClsCallback}, {"C_DialogAnimAction", kClsDialogAnim}, {"C_HoldItemAction", kClsHoldItem}, {"C_PlayerMovementAction", kClsPlayerMove},
    };
    for (const auto& e : k) if (!std::strcmp(cls, e.n)) return e.id;
    return kClsOther;
}
inline Fam family_of_id(uint8_t id, uint32_t frag) {
    switch (id) {
        case kClsNone: return Fam::Empty;
        case kClsIdleState: return standing_idle_frag(frag) ? Fam::Idle : Fam::Other;   // a posture (see family())
        case kClsVariation: return Fam::Hold;
        case kClsAIMovement: return standing_idle_frag(frag) ? Fam::Idle : Fam::Loco;
        case kClsMoveTransition: return walk_transition_frag(frag) ? Fam::Other : Fam::Loco;   // E.1: the walk's start and stop
        case kClsPlayerMove: return Fam::Loco;
        case kClsCombatMove: return Fam::CombatMove;
        case kClsCombatAnim: return paired_frag(frag) ? Fam::Hold : Fam::Other;   // a paired action: the local engine's (see paired_frag)
        default: return Fam::Other;
    }
}
// One scope of one NPC on the wire: [scope:1 0 FullBody 1 UpperBody][cls:1][frag:2][prio:1 i8][status:1][flags:4][tags:20][act:4]
constexpr size_t kWireScope = 1 + 1 + 2 + 1 + 1 + 4 + 20 + 4;
// The host reads its NPCs within this of a player 10 times a second and sends a body when one of its two scopes' action changed
// (another action object, fragment, priority or status) -- and every body again each kResendS (a lost frame heals).
constexpr float kStreamRadiusM = 40.0f;
constexpr double kStreamEveryS = 0.10, kResendS = 2.0;

// The bench's feed line (the probe: the host writes it, the joiner reads it; both copies on one PC). One sample:
//   t <utc ms> cm <0|1> {s <scope> <class> <frag> <prio> <status> <flags> <tags: 40 hex> <action: hex>}*
struct FeedScope { char scope[24]{}; char cls[48]{}; uint32_t frag = 0; int32_t prio = 0; uint32_t status = 0, flags = 0; uint8_t tags[20]{}; uint64_t act = 0; };
struct FeedSample { uint64_t t = 0; int cm = 0; int n = 0; FeedScope sc[8]; };
inline int hexval(char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; }
inline bool parse_feed(const char* line, FeedSample* out) {
    *out = FeedSample{};
    const char* p = line;
    auto word = [&p](char* w, size_t cap) -> bool {
        while (*p == ' ') ++p;
        if (!*p || *p == '\n' || *p == '\r') return false;
        size_t k = 0;
        while (*p && *p != ' ' && *p != '\n' && *p != '\r') { if (k + 1 < cap) w[k++] = *p; ++p; }
        w[k] = 0;
        return true;
    };
    char w[64];
    if (!word(w, sizeof w) || std::strcmp(w, "t") || !word(w, sizeof w)) return false;
    out->t = std::strtoull(w, nullptr, 10);
    if (!word(w, sizeof w) || std::strcmp(w, "cm") || !word(w, sizeof w)) return false;
    out->cm = std::atoi(w);
    while (word(w, sizeof w)) {
        if (std::strcmp(w, "s") || out->n >= 8) return out->t > 0;
        FeedScope& s = out->sc[out->n];
        char a[64], b[64], c[64], d[64], e[64], f[64];
        if (!word(s.scope, sizeof s.scope) || !word(s.cls, sizeof s.cls) || !word(a, sizeof a) || !word(b, sizeof b) || !word(c, sizeof c) || !word(d, sizeof d)
            || !word(e, sizeof e) || !word(f, sizeof f) || std::strlen(e) != 40) return false;
        s.frag = static_cast<uint32_t>(std::strtoul(a, nullptr, 10)); s.prio = std::atoi(b);
        s.status = static_cast<uint32_t>(std::strtoul(c, nullptr, 10)); s.flags = static_cast<uint32_t>(std::strtoul(d, nullptr, 10));
        for (int i = 0; i < 20; ++i) { const int h = hexval(e[2 * i]), l = hexval(e[2 * i + 1]); if (h < 0 || l < 0) return false; s.tags[i] = static_cast<uint8_t>(h * 16 + l); }
        s.act = std::strtoull(f, nullptr, 16);
        ++out->n;
    }
    return out->t > 0;
}

} // namespace kcdmp::wo208rules
