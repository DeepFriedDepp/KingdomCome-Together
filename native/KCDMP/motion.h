// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-121 -- movement and combat on the bodies the native writer drives.
//
// The rule (the maintainer's): sync the INPUTS that cause animations, never
// "play animation X". Each native-written body -- a peer's avatar
// (kcd2mp_<id>) or an NPC copy -- gets, at the frame hook, right after the
// WO-118 position write:
//
//   gait    C_Actor::SetPseudoSpeed (C_Actor vftable slot 0x448) from the
//           streamed speed (the avatar's v8 state block) or the rendered
//           speed (NPC copies). The engine then picks walk/run, direction and
//           footfalls itself (WO-119 s3.3, observed). Toggles mp_avatar_gait /
//           mp_npc_gait; off, the mod's Lua clip loops come back.
//           WO-129: pseudo-speed is a logical speed CLASS (1 walk, 2 run,
//           3 sprint; the engine maps round(x)-1), clamped to the body's own
//           range, and it is re-applied -- with the rendered velocity as the
//           requested velocity -- at the entry of C_Actor::UpdateMannequinTags
//           (slot 0xC98): the actor's movement controller overwrites both every
//           frame after our frame-hook write, which made every body slide.
//           Without that hook the gait piece does not arm.
//   crouch  C_ActorStateExpansion::SetCrouch (slot 0xE8) from the state bit.
//   jump    C_ActorStateExpansion::RequestJump (slot 0x100) on the Jump event.
//           (mp_avatar_moves)
//   combat  the avatar's combat automation off (the shipped
//           combat_EnableAutomation path, CombatModule), then combat mode held
//           from the stream (TryStartCombatMode, combat-actor slot 0x360, plus
//           the guard-request flag, SetFlag(model+0xEE8, 4, 1) -- without it the
//           engine ends combat the next frame), guard zone/stance, requested
//           attack zone, held block. (mp_avatar_combat)
//
// And on the SENDER side: the local player's v8 state block (read for the
// 0x86 local-state reply), and the committed-action capture -- a vtable patch
// on EnterImpl (slot 0x1C8) of C_CombatActorActionAttack / Dodge /
// PerfectBlock / Block that reads the row's mn_fragment_guid from the
// descriptor (action+0x60 -> +0x84, WO-121 session 1), and on
// C_ActorStateExpansion::RequestJump for the player's jump.
//
// Every address is re-found by anchor at install (RTTI vftables, the shipped
// combat test commands' Execute slots, string-anchored functions, structural
// byte checks); a piece whose anchor does not verify does not install, says so
// in WO121-MOTION, and the rest still does. A fault inside a call disarms that
// piece. Threads: on_* run on the pipe thread and only queue; body_frame,
// tick and read_local_state2 run on the game's main thread; the EnterImpl /
// RequestJump hooks run on whatever thread the engine calls them on and only
// queue.

#include <cstddef>
#include <cstdint>

namespace kcdmp::motion {

// The v8 state block, byte-for-byte the wire's (KcdMp.Wire.BodyState2).
#pragma pack(push, 1)
struct State2 {
    uint16_t speedCm = 0;
    int8_t   moveDir = 0;      // 1/256 turn, heading of the requested velocity minus facing
    uint8_t  bits = 0;         // kBit*
    uint8_t  guardZone = 0;    // wire zone: table id + 1 (0 = undefined)
    uint8_t  guardStance = 0;  // wire stance: table id + 1 (0 = none)
    uint8_t  atkZone = 0;
    uint8_t  charge = 0;
    int16_t  aimYaw = 0, aimPitch = 0;
};
#pragma pack(pop)
static_assert(sizeof(State2) == 12, "State2 is the 12-byte wire block");

constexpr uint8_t kBitCombat = 0x01, kBitBlock = 0x02, kBitCrouch = 0x04, kBitRanged = 0x08, kBitLocked = 0x10;
// WO-154 2: the sender's own body is down -- its physics is no living entity (a knockdown's ragdoll,
// a knockout). 0x20 is the agent's (TorchLit).
constexpr uint8_t kBitDowned = 0x40;

// Resolve anchors, install the capture hooks. Main thread, once, after
// npcdrive::install(). Logs one WO121-MOTION line.
void install();

// ---- pipe thread: parse + queue ------------------------------------------------
// 0x16 MotionConfig: [avatarGait][npcGait][avatarMoves][avatarCombat][npcRows]
uint8_t on_config(const uint8_t* body, size_t len);
// 0x17 AvatarEvent: [kind:1][eid:4]; kind 1 = jump, kind 2 (WO-160) = a loop on this avatar was stopped: the T-pose pulse again
uint8_t on_avatar_event(const uint8_t* body, size_t len);
// WO-160: an avatar's looping animation stopped (a one-shot's end, a clip the mod stopped): its locomotion graph can stand in the
// T-pose again until something moves it (the WO-155 bind transient, seen at a loop's end too) -- the walk-class pulse runs again,
// once, if the body is still. Main thread.
void rearm_nudge(uint32_t eid, const char* why);

// ---- main thread ---------------------------------------------------------------
// npc_drive calls this for every body it wrote this frame. `st` is the newest
// state block from the stream (null when the stream never carried one) and
// `stAgeS` its age in seconds (receiver clock: arrival, never the sender's).
// renderVx/Vy: this frame's rendered planar velocity (world, m/s).
void body_frame(const char* key, void* ent, uint32_t eid, float renderSpeedMps, float renderVx, float renderVy,
                const State2* st, double stAgeS, double now);
// The writer stopped driving this body (unbind, drop, disarm): give it back. An avatar keeps its identity
// (WO-154 2: its reaction contexts and speech gate stay through every unbind).
void body_released(const char* key, uint32_t eid);
// WO-154 2: the avatar's identity at spawn, by its soul's guid (the agent's isolate call); main thread.
void identity(const unsigned char guid[16], bool on);
// WO-154 2: the session is over: every avatar identity this DLL set is cleared; main thread.
void on_pipe_closed();
// The local player's state block (main thread; the 0x86 read). facingYaw is
// the yaw local_state read from the entity matrix in the same call.
bool read_local_state2(State2* out, float facingYaw);
// WO-155: the debounced down state of the local player's body (the WO-154 DOWN edges) and when it last went down
// (QPC seconds, npcdrive::now_s(); -1e9 = never); read on the main thread.
bool local_down_now();
double local_last_down_edge_s();
// Per-frame: drain the capture queue to the callback, heartbeat checks.
void tick();

// One committed action on this machine, for the agent (pipe frame 0x96).
// kind: ActionKind (1 attack, 2 jump, 6 block impulse, 7 dodge). eid 0 = the
// local player; otherwise an NPC by its entity name.
// WO-163 (A1): altGuid = what the legacy read (+0x84) of a sync attack's descriptor holds, or null -- the agent logs if both reads
// hit its catalog. Sent as an optional 16-byte tail of the frame.
using ActionFn = void (*)(uint8_t kind, uint8_t phase, int8_t inputClass, int8_t zoneTableId, int8_t attackType,
                          uint8_t flags, const uint8_t guid[16], uint32_t eid, const char* name, const uint8_t* altGuid);
void set_action_callback(ActionFn fn);

// Human-readable armed/off state and counters for the 0x1B status reply.
int status_text(char* out, int n);

// WO-132 (joiner, main thread): hold a native-written NPC copy in the host NPC's
// combat state (combat mode, guard zone/stance, attack zone, block) -- the same
// applier the avatars use. It lets go 3 s after the last update, or on `on=false`.
void set_npc_engage(uint32_t eid, bool on, const State2* st, double now);
bool npc_engaged(uint32_t eid);
// WO-132 (live checks only): the local player's held block through the engine's
// own SetBlockMode -- the function the block button calls; no input is made.
bool player_block(bool on);
// WO-135 (live checks only): the local player's crouch through the state
// expansion's own SetCrouch -- the function the crouch key reaches; no input.
bool player_set_crouch(bool on);
// WO-132 (live checks only): a test NPC fights -- combat mode, the guard-request
// flag and its combat automation ON (what the shipped combat autotests use).
bool test_fight(uint32_t eid);
// WO-154 3.5 (live checks only): the local player's own combat automation on or
// off (the same combat_EnableAutomation path): the engine fights for the host
// against his skirmish opponent, so his blows go through the game's own hit --
// the proof of "the host is a real target" without any input.
bool player_automation(bool on);
size_t npc_engaged_count();

// WO-132 (host, main thread): one NPC's combat state, read from its combat model.
struct NpcCombat {
    uint8_t hasCa = 0, combat = 0, block = 0, opponentIsPlayer = 0;
    int8_t  guardZone = -1, guardStance = -1, atkZone = -1;
    uint32_t opponentEid = 0;   // the opponent's entity id (0 = none / unreadable)
};
bool read_npc_combat(uint32_t eid, NpcCombat* out);   // false = no actor for eid

// WO-163 (the probes P1 / P3, read-only, main thread): the combat model fields the engine's hit core reads -- the victim's
// State / GuardZone / BlockZoneId / BlockHandSlot / BlockMode / PerfectBlockState / Opponent and the attacker's AttackZone / AttackType /
// AttackStrength / AttackHandSlot (docs/WO-100 s10, research/WO-162 Q1.4). Each field is valid only when the property block at its
// offset names itself (the model reader's own check); an invalid field reads 0 and its valid flag is false.
struct ModelRead {
    uint8_t hasCa = 0, hasModel = 0, isPlayerCa = 0, opponentIsPlayer = 0;
    uint16_t valid = 0;                       // bit per field, kModelValid* below
    int32_t state = 0, guardZone = 0, blockZone = 0, blockHand = 0, blockMode = 0, atkZone = 0, atkType = 0, atkHand = 0;
    uint8_t perfectBlock = 0, combatMode = 0;
    float atkStrength = 0;
    uint32_t opponentEid = 0;
};
constexpr uint16_t kMvState = 1, kMvGuardZone = 2, kMvBlockZone = 4, kMvBlockHand = 8, kMvBlockMode = 16, kMvPerfect = 32,
                   kMvAtkZone = 64, kMvAtkType = 128, kMvAtkStrength = 256, kMvAtkHand = 512, kMvCombatMode = 1024;
bool read_model(uint32_t eid, ModelRead* out);        // false = no actor for eid

// For hits.cpp: is this entity id a native-written avatar, and its soul.
bool is_avatar_eid(uint32_t eid);
// The local player's combat actor (main-thread cache; 0 when unknown).
void* player_combat_actor();

// WO-165 (main thread): the combat actor and its model for an entity (create = make the combat actor when it has none, as the
// avatars do); the local player's from the cache, checked by class. False when either is missing.
bool combat_parts(uint32_t eid, bool create, void** ca, void** model);
bool player_combat_parts(void** ca, void** model);
// WO-165: the four attack fields the hit core reads from the ATTACKER's model (research/WO-162 Q1.4; WO-163 P1: a copy playing a host
// row never sets them). Read and written as plain values at the property block's value slot (+8), each block checked by its own name
// first -- no setter runs, so no change listener fires. write: all four or none.
struct AttackFields { int32_t type = -1, zone = -1, hand = 0; float strength = 0; };
bool read_attack_fields(void* model, AttackFields* out);
bool write_attack_fields(void* model, const AttackFields& f);
// WO-166 C3: the combat model's State (E_CombatActorStateId: 1 Idle 2 Guard 8 Striking ...), the property block named first; write = a
// plain value at the block's value slot (no setter, no listener) -- the copy's striking window.
bool read_state(void* model, int32_t* out);
bool write_state(void* model, int32_t v);

} // namespace kcdmp::motion
