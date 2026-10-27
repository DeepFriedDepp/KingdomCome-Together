// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-209: the native half's pure rules, engine-free so native/tests pins them.
#include <cstddef>
#include <cstdint>
#include "wo208_rules.h"

namespace kcdmp::wo209rules {

// ---- the agent's switch word (op 134), the same bits as Wo209Rules.ConfigBits; every bit on until the agent says otherwise ----
constexpr uint32_t kBitOppMirror = 1;     // A mp_copy_opponent_mirror: a copy's opponent is the host NPC's own, translated
constexpr uint32_t kBitAttackAi = 2;      // B mp_copy_attack_ai: a copy's swing at the local player is its own combat AI's attack
constexpr uint32_t kBitPairedEngine = 4;  // C.1 mp_copy_paired_engine: a paired action with the local player plays the engine's own way
constexpr uint32_t kBitShotRequest = 8;   // C.2 mp_copy_oneshot_request: a work one-shot on an owned copy is the game's own NPC-state request
constexpr uint32_t kBitQuestWake = 16;    // D.4 mp_quest_wake_with_host: a host quest change into a module asleep here wakes it the engine's way
constexpr uint32_t kBitsDefault = kBitOppMirror | kBitAttackAi | kBitPairedEngine | kBitShotRequest | kBitQuestWake;

// Fixes with no switch of their own, on unless the bench's twin turns them off (never in the agent's word).
constexpr uint32_t kFixOwnerNeutral = 0x100;   // the owner's copies take the activity rows (the neutral rule): owner_neutral
constexpr uint32_t kFixOwnerQueueCap = 0x200;  // the owner queues nothing on a copy whose action queue is long: owner_queue_cap
constexpr uint32_t kFixDeathLeave = 0x400;     // the local player's death ends every copy's engagement (as op 1's leave): death_leave_clears
constexpr uint32_t kFixGameHostile = 0x800;    // no attack of its own at the local player from a copy the game holds not hostile: game_hostile_gate

// The bench's twins: KCDMP_TEST_W209_OFF in the game's own environment (a comma list, whole names: wo208rules::list_has).
struct Named { const char* name; uint32_t bit; };
inline constexpr Named kNames[] = {
    {"copy_opponent_mirror", kBitOppMirror},
    {"copy_attack_ai", kBitAttackAi},
    {"copy_paired_engine", kBitPairedEngine},
    {"copy_oneshot_request", kBitShotRequest},
    {"quest_wake_with_host", kBitQuestWake},
    {"owner_neutral", kFixOwnerNeutral},
    {"owner_queue_cap", kFixOwnerQueueCap},
    {"death_leave_clears", kFixDeathLeave},
    {"game_hostile_gate", kFixGameHostile},
};

// The activity rows (WO-141) on a copy the animation owner owns. WO-208 kept every owned copy out of them, and with the WO-200 neutral rule
// on that also kept out the neutral write -- a copy bound at a camp's waiting spot kept the spot's demand and stood there (d5-defence: a
// bandit's copy 16.7 m from its fight for 7 minutes, every attack refused; Part 0: two camp bandits at 19-32 m with the owner on, none
// off). Refused only for the one-copy probe (A.1, no other writer), or with the neutral rule off (the activity would be the planner's).
// The owner's queue cap: nothing of the owner's is queued on a copy whose action queue holds this many (the engine refuses past 10 --
// rc1's taverns: the owner's installs piled up behind a seated copy's own actions that never finished). Unreadable (-1): no cap.
constexpr int kOwnerQueueCap = 6;
inline bool owner_queue_full(int queued) { return queued >= kOwnerQueueCap; }

inline bool activity_refused_for_owner(bool probeOnly, bool owned, bool neutral, bool fixOn) {
    return probeOnly || (owned && !(neutral && fixOn));
}
inline uint32_t twin_off_bits(const char* list) {
    uint32_t b = 0;
    for (const Named& x : kNames) if (wo208rules::list_has(list, x.name)) b |= x.bit;
    return b;
}

// ---- A: the host NPC's opponent on the wire ----
// The host's kinds (op 136): nobody, the host's own player, a partner's figure (its entity; the agent names the partner), another NPC (by
// name). The joiner's kinds (op 137, after the agent translated them): nobody, the local player, an entity here (a figure or a copy).
enum : uint8_t { kHostNone = 0, kHostPlayer = 1, kHostFigure = 2, kHostNpc = 3 };
enum : uint8_t { kHereNone = 0, kHerePlayer = 1, kHereEntity = 2 };
inline uint8_t host_kind(uint32_t opponentEid, uint32_t localPlayerEid, bool opponentIsAvatar) {
    if (!opponentEid) return kHostNone;
    if (localPlayerEid && opponentEid == localPlayerEid) return kHostPlayer;
    return opponentIsAvatar ? kHostFigure : kHostNpc;
}
// The host sends a body's opponent when it changed, and again every kOppResendS while it has one (a lost frame heals; the joiner's wish
// goes stale after 4 s without a word).
constexpr double kOppResendS = 1.5;
inline bool opp_send_due(bool changed, double sinceSentS, uint8_t kind) { return changed || (kind != kHostNone && sinceSentS >= kOppResendS); }
// The joiner: a copy the stream has not spoken of for this long is let go (its wish clears; WO-187's agent word may take it again).
constexpr double kMirrorStaleS = 4.0;
// The mirror re-applies (and samples) each copy this often: one stream tick.
constexpr double kMirrorTickS = 0.1;
// The mirror engages a copy in the local player's fight up to this far from him (the host's NPC fights his figure there; the copy may
// lag behind its host NPC in a chase). Farther is a copy whose position is not to be trusted: left to op 2's own range.
constexpr float kMirrorEngageM = 45.0f;

// What the mirror does for one copy this tick. Engage = join the local player's skirmish first (adds only), then the wish.
enum class Mirror : uint8_t { Clear = 0, Wish = 1, EngageWish = 2 };
inline Mirror mirror_step(uint8_t hereKind, bool engaged) {
    if (hereKind == kHerePlayer) return engaged ? Mirror::Wish : Mirror::EngageWish;
    if (hereKind == kHereEntity) return Mirror::Wish;
    return Mirror::Clear;
}
// The wo187 wish kind for a joiner kind (wo187rules::Opp: 1 the local player, 2 an entity).
inline uint8_t wish_kind(uint8_t hereKind) { return hereKind == kHerePlayer ? 1 : hereKind == kHereEntity ? 2 : 0; }

// The acceptance's own sample: the copy's opponent against the one the stream names (A.4: at least 95 %). A copy whose want changed this
// tick is not sampled until the follow window (one stream tick plus the engine's own switch) has passed -- the follow time is measured
// on its own.
constexpr double kFollowGraceS = 0.25;
inline bool sample_counts(uint8_t hereKind, double sinceChangeS) { return hereKind != kHereNone && sinceChangeS >= kFollowGraceS; }

// The mirror holds a copy in combat mode (WO-132's engagement hold) while it holds the copy on the local player and in his fight.
inline bool mirror_holds_combat(uint8_t hereKind, bool engaged) { return hereKind == kHerePlayer && engaged; }

// ---- B: the copy's swing at the local player as its own combat AI's attack (mp_copy_attack_ai) ----
// WO-208 found that a swing played as an animation row is not the combat AI's attack action, and only the latter's blow tests its
// target: the joiner's engine judged 0 blows. With the mirror holding the copy in his fight, the owner starts the host's attack on the
// copy through the engine's own AI attack (CombatModule's C_CombatDebugAIBrain attack: the zone, the attack factory, the director's
// SetAction), so the copy's own blow is resolved by his engine (hit, block, perfect block, dodge). The host's attack fragments (male
// table; no female body attacks): CombatAttack 9, CombatAttackCombo 89, CombatAttackGen 897, CombatAttackSpecialGen 906,
// CombatAttackRiposteGen 907, CombatAttackComboGen 937. Paired (908..) stay held: their partner is his own engine's (C.1).
inline bool attack_frag(uint32_t frag) { return frag == 9 || frag == 89 || frag == 897 || frag == 906 || frag == 907 || frag == 937; }
// The engine's attack runs at most this long (the window's end is its scope-0 action leaving; this is the cap); the outcome of a blow
// that did not land is told this long after the window (the engine applies a blow on the frame after).
constexpr double kAiAttackMaxS = 2.5, kAiTellAfterS = 0.12;   // 0.3 until d8: a whiffed blow's "missed" came just past the host's hold
// An attack the engine refused (busy: the copy's own attack before it, a hit, a block) is asked again every frame for this long -- the
// host's NPC chains its next attack straight after the last (d4-defence: a combo's second swing refused, started 410 ms later by the
// owner's next look, its blow past the host's hold).
constexpr double kAiRetryS = 0.5;
inline bool ai_retry_due(double now, double refusedAt) { return now >= refusedAt && now - refusedAt <= kAiRetryS; }
// A row that comes before the copy's opponent update (d7: the host's NPC turned on the joiner's figure with that very swing; the mirror's
// word a stream tick later) waits for it, kAiRetryS at most: the attack starts only if the mirror then has the copy on him and in his fight
// (attack_ai_drives). d10: a fight's first row came before the copy was in his fight at all, and the owner's state started it 0.8 s late.
inline bool row_waits_for_mirror(bool mirrorHolds, bool engaged) { return !(mirrorHolds && engaged); }
// The attack an AI call queued appears in the director's scope 0 within this, or the window ends (the engine never ran it).
constexpr double kAiAdoptS = 0.25;
// The row path (WO-167's spec "Fragment, tag+tag..."): its fragment's id by name (the same attack fragments), 0 = not an attack.
inline uint32_t attack_frag_by_name(const char* spec) {
    if (!spec) return 0;
    static const struct { const char* n; uint32_t id; } k[] = {
        {"CombatAttackGen", 897}, {"CombatAttackSpecialGen", 906}, {"CombatAttackRiposteGen", 907}, {"CombatAttackComboGen", 937},
        {"CombatAttack", 9}, {"CombatAttackCombo", 89},
    };
    size_t n = 0;
    while (spec[n] && spec[n] != ',' && spec[n] != ' ') ++n;
    for (const auto& e : k) if (std::strlen(e.n) == n && std::strncmp(spec, e.n, n) == 0) return e.id;
    return 0;
}
// The row's attack zone (its aZ<n> tag), -1 when it has none.
inline int row_zone(const char* spec) {
    if (!spec) return -1;
    for (const char* p = spec; *p; ++p)
        if (p[0] == 'a' && p[1] == 'Z' && p[2] >= '0' && p[2] <= '9' && (p == spec || p[-1] == '+' || p[-1] == ' ' || p[-1] == ','))
            return p[2] - '0';
    return -1;
}
// The window's verdict when no blow landed. This is the copy's own attack, aimed at him by the engine (not WO-208's animation row, which
// could not land): one that ran its course without touching him missed on his screen -- his engine's judgement, as his dodge is. One cut
// short (under kAiFullS: a hit on the copy, the engine replacing it) judged nothing: none, the host's verdict at once.
constexpr double kAiFullS = 0.35;
inline uint8_t ai_window_outcome(bool landed, bool dodged, double ranS = 1.0) {
    if (landed) return 0;   // the defence watch already told the engine's verdict
    return (dodged || ranS >= kAiFullS) ? wo208rules::kDefMissed : wo208rules::kDefNone;
}

// ---- C.1: paired actions, the game's own way ----
// A paired action the host's NPC leads against the joiner's figure is played on the joiner the engine's own way with the local Henry as
// the partner: the clinch start / clinch guard as master (CombatClinchStartGuardMasterGen 914, CombatClinchGuardMasterGen 918, the older
// CombatClinchGuardMaster 63, CombatSyncGuardMaster 560) through C_CombatActorClinch::StartSyncGuard(clinch 4, master = the copy, slave = him);
// a sync attack or a clinch move as master (CombatAttackSyncGen 908, CombatClinchTransitionMasterGen 916, CombatAttackSync 66, the master
// strikes 922 / 135) through the copy's own attack request, which builds the paired action itself and starts his half in the same call
// (SyncAttack / SyncTransition EnterImpl). Every other half (the slave's: the joiner leads it, his own engine runs both) stays held.
enum class Paired : uint8_t { No = 0, MasterStart = 1, MasterMove = 2, Held = 3 };
inline Paired paired_role(uint32_t frag) {
    if (frag == 914 || frag == 918 || frag == 63 || frag == 560) return Paired::MasterStart;
    if (frag == 908 || frag == 916 || frag == 66 || frag == 922 || frag == 135) return Paired::MasterMove;
    return wo208rules::paired_frag(frag) ? Paired::Held : Paired::No;
}
constexpr int32_t kClinchGuardType = 4;   // the clinch guard (sync_guard="true": 4, 24, 25)

// ---- D.4: a module asleep here that the host has awake ----
// The host's quest change proves its node's module is awake there (a hibernated node drops every pulse). On the joiner the same module
// sleeps because what wakes it -- a distance or area trigger on the player -- is quiet here by design (WO-207's joiner_player_triggers,
// WO-204's nearest player): the change was dropped after 30 s (WO-208 D.2's kind 4). The joiner wakes the topmost hibernated ancestor
// the engine's own way, C_Node::Wake with the reason the quest data's own Wake port passes (2), then applies the change.
constexpr int32_t kWakeReasonScript = 2;
// The topmost hibernated node of a runtime chain (the node first, then each parent): its index, -1 when the node is not hibernated. A
// chain whose hibernated run does not start at the node itself is not this case.
inline int wake_root(const int* chain, int n) {
    if (n <= 0 || chain[0] != 1) return -1;
    int top = 0;
    for (int i = 1; i < n && chain[i] == 1; ++i) top = i;
    return top;
}

} // namespace kcdmp::wo209rules
