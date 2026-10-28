// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-121 Phases 5 and 6 -- hits that carry an attacker (NPCs fight back) and
// friendly fire.
//
// The chokepoint (WO-119 s2.3, corrected live in WO-121 session 1): every
// melee hit passes C_CombatSoul vtable slot 0x150 (missiles: 0x158), called on
// the ATTACKER's combat soul with S_CombatHitData: +0x00 attacker WUID, +0x08
// attacker entity id, +0x10 victim WUID, +0x18 victim entity id, +0x40 the hit
// position. Both slots are vtable-patched here (RTTI C_CombatSoul, RPGModule).
//
//   * victim = a peer's avatar (registered by motion.cpp): the original runs
//     -- skipping it returns an empty cause and CRASHED the game 0.6 s later
//     (observed, session 1) -- and its health/stamina are read before and
//     after and put straight back, so the local hit never lands on the
//     avatar. With friendly fire on, the difference goes to the peer (pipe
//     frame 0x97 -> PlayerHit 0x44). Belt and braces: every avatar also holds
//     kcdmp_avatar_guard (imm+upr), so it can never die or be knocked out here.
//   * attacker = the local player, victim anything else: the victim is
//     remembered for ~1.5 s, so the WO-40 LocalHit report for it carries
//     "the local player dealt this" (NpcDamage flag ATTRIBUTED).
//   * everything else: the original, untouched.
//
// On the NPC's authority (pipe 0x19), an attributed peer hit is applied with
// the peer's avatar as the attacker: TakeDamage(attacker), the combat-history
// writer (RPGModule, the one slot 0x150 itself calls), and once per engagement
// AddSoulToSkirmish(victim, avatar, override 1). The brain message
// (hitReaction) is the agent's, through the engine's own debug-command shape.
//
// A partner's friendly-fire hit on OUR Henry (pipe 0x1A) is plain TakeDamage
// with no attacker -- naming one is what starts fights -- and an unarmed flag
// that tells the death guard's classifier a fist floored him (a knockdown).

#include <cstddef>
#include <cstdint>

namespace kcdmp::hits {

void install();   // main thread, after motion::install(); logs WO121-HITS

// 0x18: [friendlyFire][attribution][pvpHook]
uint8_t on_config(const uint8_t* body, size_t len);

// Avatars, from motion.cpp (main thread): the soul the hook restores.
void note_avatar(uint32_t eid, void* soul, bool on);

struct AttribResult { bool ok = false; uint8_t steps = 0; uint8_t reason = 0; uint64_t attackerWuid = 0, victimWuid = 0; };
// 0x19, main thread: [guid:16][st:4f][hp:4f][flags:1][attackerEid:4][nameLen:1][name]
AttribResult apply_attributed(const uint8_t* body, size_t len);
// WO-173 (main thread), the same body: rawDamage = the TakeDamage step runs (false: the engine already applied the blow -- only the
// victim-side history, the skirmish and, with threat, WO-136's avatar-hit threat run; steps bit 0 then means "the engine applied it");
// stCap >= 0 caps the stamina of the raw step (the capped fallback); threat = WO-136's note_threat (the substitute for a hit the engine
// cannot perceive).
AttribResult apply_attributed_ex(const uint8_t* body, size_t len, bool rawDamage, float stCap, bool threat);

// 0x1A, main thread: [st:4f][hp:4f][flags:1][attackerGhost:1]
bool apply_pvp_hit(const uint8_t* body, size_t len);

// For the pipe's LocalHit (main thread): did the local player hit this soul
// in the last `withinS` seconds?
bool hit_by_player(void* soul, double withinS);

// A local-player hit on an avatar, for the agent (pipe frame 0x97).
using PvpFn = void (*)(uint32_t victimEid, float stamina, float health, uint8_t flags, uint8_t material);
void set_pvp_callback(PvpFn fn);

// WO-132: an NPC's hit on an avatar, measured over the watch window and put
// back (pipe frame 0x9A -> the agent forwards it to the avatar's owner).
using NpcHitFn = void (*)(uint32_t victimEid, float stamina, float health, uint32_t attackerEid, uint8_t flags, const char* attackerName);
void set_npc_hit_callback(NpcHitFn fn);
void set_npc_watch(bool on);
// WO-132: an engaged copy's local hit on the player was put back (logs only, 0x9C).
using DiscardFn = void (*)(uint32_t attackerEid, float stamina, float health);
void set_discard_callback(DiscardFn fn);
// WO-208 B (mp_defence_local, joiner): an engaged copy's blow on the local player judged by this machine's own engine and KEPT -- the
// engine's verdict (wo208_rules.h DefOutcome: 1 hit, 2 blocked, 3 perfect block, 4 guard broken; 5 missed, from wo166's window) and
// the damage it applied, measured over the watch window. Main thread (pipe frame 0xAE -> the agent sends it to the host).
using DefenceFn = void (*)(uint32_t attackerEid, uint8_t outcome, float stamina, float health);
void set_defence_callback(DefenceFn fn);
void report_defence(uint32_t attackerEid, uint8_t outcome, float stamina, float health);   // wo166's missed window (main thread)
// WO-208 B: when an engaged copy's blow last landed on the local player (QPC seconds; -1 never) -- wo166's window asks at its end.
double last_local_hit_from(uint32_t attackerEid);
// WO-132 (joiner, main thread): hits by this attacker on the local player are discarded.
bool discard_from(uint32_t eid, bool on);
int discard_count();
bool is_discard_attacker(uint32_t eid);   // WO-166 C3: already on the list (the strike window adds and removes only its own)
// WO-132: a forwarded host hit just landed on the local player (main thread).
void note_player_damage(float stamina, float health);
// WO-132 (main thread): the skirmish manager's own add / remove-one-soul.
bool skirmish_ready();
bool skirmish_add(void* soul, void* reference, uint8_t overrideRelation, uint64_t* rv);
bool skirmish_remove(void* soul, uint64_t* rv);
// WO-174 (main thread): does this NPC belong to a scripted brawl or scene (by its name; wo174_rules.h)? true = the mod keeps a partner's figure out of
// its fight: no skirmish add, no threat, no forced target, no lock. Logs WO174-BRAWL (once per actor per 10 s) and counts; site names the caller.
bool scene_owned_eid(uint32_t eid, const char* site);
uint32_t scene_kept_count();
// WO-163 (A7; docs/WO-162 Q2.3, read-only, main thread, fault-guarded): the engine's own relation test on two souls -- true iff
// they are hostile opponents inside ONE skirmish (the player-lock rule's own question: same skirmish, active members, a hostile pair or
// a negative faction value). false/false = the call could not be made (not armed, no manager, a soul missing): *answered stays false.
// why (optional): on a false return, the reason in words ("not armed", "no manager", "no relation object", "the call faulted").
bool skirmish_hostile(void* soulA, void* soulB, bool* answered, bool* hostile, const char** why = nullptr);
bool skirmish_relation_armed();
// WO-136: the avatars (entity id, soul) the hook knows; returns the count.
int avatar_list(uint32_t* eids, void** souls, int max);
void* soul_of_eid(uint32_t eid);
uint32_t eid_of_name(const char* name);   // one entity walk per name, cached and re-verified
// WO-147: as the hit hook marks a real blow of the local player's on this body (any thread).
void mark_player_hit(uint32_t victimEid);
// WO-210 E.2: the gate's dry run of the local player's blow on a copy (wo210_hit.cpp), on the calling thread: while it is open the hit
// hook runs the original and nothing else -- no "hit by the player" mark (a later drop on that copy would be sent as his blow), no
// host-side bookkeeping, no watch. The blow's report is the gate's own.
void dry_run_begin();
void dry_run_end();

// WO-165 (main thread): the replay window around one call into the engine's hit processor (wo165.cpp). While it is open, the slot hook
// treats a blow of exactly this attacker on exactly this victim, on this thread, as the replay: never discarded (WO-162 Q1.3 #9), the
// engine-built 0x90-byte record copied into the capture, the victim's soul watched for kWatchS and the damage the engine applied
// reported (measured, nothing put back) to the callback with the replay's sequence number.
struct ReplayCapture { bool seen = false; void* self = nullptr; uint8_t rec[0x90]{}; };
void replay_begin(uint32_t attackerEid, uint32_t victimEid, uint32_t seq, void* victimSoul, bool victimIsPlayer);
void replay_end(ReplayCapture* out);
using ReplayDmgFn = void (*)(uint32_t seq, uint32_t victimEid, float health, float stamina, bool victimLive);
void set_replay_damage_callback(ReplayDmgFn fn);
uint32_t replay_seen();

// WO-170 F1 (docs/WO-170-findings.md): the figure guard. A partner's figure (an avatar) never lands a blow of its own on this machine: its
// melee hit is applied only when it belongs to a swing the partner really sent -- the relayed swing the DLL queued on that figure
// (note_figure_swing, combat_construct.cpp), once per swing, inside kSwingHitS; every other one is measured and put back
// (WO170-FIGUREHIT dropped ... why=not-relayed). Any thread (the ledger has its own lock); the switch is the agent's (mp_figure_guard).
void set_figure_guard(bool on);
bool figure_guard();
void note_figure_swing(uint32_t figureEid);
// A TakeDamage of ours (an attributed hit of the partner's, a friendly-fire hit) just landed on this victim (main thread): an open
// put-back watch of a dropped blow on the same victim moves its baseline by it, so it is never put back with the dropped blow's damage.
void note_applied_damage(uint32_t victimEid, float stamina, float health);

void tick();   // main thread: drain the hook's queue, resolve victims
int status_text(char* out, int n);

} // namespace kcdmp::hits
