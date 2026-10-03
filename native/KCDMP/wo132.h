// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-132: damage safety and combat engagement -- the native half
// (docs/WO-132-findings.md).
//
// One pipe request, 0x22 [op][...], answered with 0x99 [ok][seq][op][reason][payload].
// Every op runs on the game's main thread (the pipe marshals it).
//
//   op 1 LeaveFight [eid:4] -> [removed:1]          (eid 0 = the local player)
//                   I_SkirmishManager::RemoveSoulFromSkirmish on that body's
//                   soul: it leaves its skirmish, the fight goes on without it
//                   (the enemies drop it as a target). Replaces WO-131's
//                   StopFight on an avatar, which ended the whole fight -- the
//                   host's too. WO-154: an avatar leaving its fight also loses
//                   every forced target and threat WO-136 holds toward it.
//   op 2 Engage     [on:1][eid:4][state:12]  -> [first:1][skirmish:1][distM:4f]
//                   Joiner: a bound, suspended copy of a host NPC that is in a
//                   fight near this player. First time: the copy joins a
//                   skirmish against the local player (AddSoulToSkirmish,
//                   override 1: its target is the player) and its hits on the
//                   player are discarded; every time: the host NPC's combat
//                   state (the v8 State2 block, wire zones) is held on the copy
//                   (motion.cpp, the avatars' applier). Off: the hold ends, the
//                   copy leaves the skirmish, the discard ends.
//   op 3 Watch      [on:1][eid:4][nameLen:1][name] -> [eid:4]   (eid 0 = find it by name)
//                   Host: sample this NPC's combat state every 100 ms; changes
//                   (and a 1 s heartbeat while it is in combat) go out as the
//                   unsolicited frame 0x9B.
//   op 4 Status     [] -> [text]
//   op 5 Discard    [on:1][eid:4] -> []
//                   Joiner: this copy's hits on the local player are measured
//                   and put back (hits.cpp), whether or not it is engaged.
//   op 6 Read       [eid:4] -> [NpcCombat 12 bytes]   (eid 0 = the local player)
//                   One combat-state read, for logs and the live checks.
//   op 7 PlayerBlock [on:1] -> []
//                   Live checks only: the local player's held block through the
//                   engine's own SetBlockMode (what the block button calls).

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo132 {

constexpr uint8_t kOpLeaveFight = 1;
constexpr uint8_t kOpEngage     = 2;
constexpr uint8_t kOpWatch      = 3;
constexpr uint8_t kOpStatus     = 4;
constexpr uint8_t kOpDiscard    = 5;
constexpr uint8_t kOpRead       = 6;
constexpr uint8_t kOpPlayerBlock = 7;
// WO-136 Phase 4 (host; docs/WO-136-findings.md, wo136.h):
constexpr uint8_t kOpHandOver    = 9;    // [removePlayer:1] -> [handed:1]   the host's fights to the partners' avatars (live checks; the death path calls it itself)
constexpr uint8_t kOpAvatarSwing = 10;   // [avatarEid:4] -> [npcEid:4]      a joiner's committed attack: its avatar fights that NPC, the swing threatens it
constexpr uint8_t kOpFights      = 11;   // [on:1] -> []                     mp_w136_fights on|off (the threat rule and the hand-over)
constexpr uint8_t kOpHostThreat  = 12;   // [npcEid:4][weight:1] -> []       live checks only: a threat the host "makes" (no input exists)
// WO-154 3.1 (host; wo136.h): mp_host_target on|off -- a host blow frees an NPC from the mod's locks on an avatar
constexpr uint8_t kOpHostTarget  = 13;   // [on:1] -> []
// WO-154 3.5 (live checks only): the host's Henry fights npcEid by the engine's own combat automation for secs
// (no input; his blows are the game's own hits). off: automation off at once. [on:1][npcEid:4][secs:1] -> []
constexpr uint8_t kOpPlayerFight = 14;
constexpr uint8_t kOpTestFight  = 8;   // [npcEid:4][targetEid:4][override:1] live checks only: a test NPC fights (skirmish + combat + automation)   // [on:1] live checks only: the player's block via SetBlockMode (no input)

constexpr uint8_t kROk         = 0;
constexpr uint8_t kRBadRequest = 1;
constexpr uint8_t kRNoSoul     = 2;
constexpr uint8_t kRNoActor    = 3;
constexpr uint8_t kRUnarmed    = 4;   // the skirmish manager or the combat applier did not arm
constexpr uint8_t kRFailed     = 5;
constexpr uint8_t kRFar        = 6;   // Engage: the copy is more than 15 m from the local player (released)

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

// 0x9B: [eid:4][combat:1][guardZone:1][guardStance:1][atkZone:1][block:1][oppIsPlayer:1][oppEid:4][nameLen:1][name]
// (zones as table ids, 0xFF = none).
using CombatFn = void (*)(const uint8_t* body, uint16_t len);
void set_combat_callback(CombatFn fn);

void tick();   // main thread, every frame
void on_pipe_closed();

} // namespace kcdmp::wo132
