// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-201 -- every bug from the first 0.61.5 field session, at its root (docs/WO-201-findings.md). The native half; the pure rules are
// wo201_rules.h; the agent decides everything that needs the partner (GameBridge.Wo201*.cs).
//
//   B1 joiner_scene_noposition  On the joiner in the host's world a scene never waits for the host's copies to walk to their places
//                               (they are puppets: a paused brain never finishes a FastForward -- the field: 95 s and 336 s, ended only
//                               by a save's "Cancelling FF for all NPCs"). The moment the NPC manager says a FastForward runs, every
//                               NPC but the player and the mod's figures gets the engine's own per-NPC cancel -- the very call the
//                               save path makes -- and stays where the host's stream puts it. No save is requested. The cancel and the
//                               manager's field are read out of C_NPCManager::ValidateFastForward (found by its error text), with an
//                               identity check against the build WO-188 read. WO201-FF.
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   76 Status -> text
//   77 Config [role:1][joined:1][bits:1] (255 = unchanged) -> the same 3 bytes; bits 1 joiner_scene_noposition, 2 walk_from_host (B10:
//        npc_drive's writer walks a moving copy through a gap on the host's last speed, keeps it through silence, gaits it on the host's speed)
//   78 CombatEnd [eid:4] -> [held:1][stopFight:1]   B4 copy_combat_from_host (joiner): the host's NPC left combat (or the copy started one
//        by itself) -- every fight hold of the mod on the copy released (WO-132's skirmish and hold, WO-187's wish and pin, the writer's
//        combat hold; WO-195's mask in `held`), then the engine's own end of its fight: wh::rpgmodule::StopFight on its soul. WO201-COMBAT.
//   79 Dude2 [eid:4][on:1] -> [ok:1][flagsBefore:4][flagsAfter:4]   V.3 figure_is_player (host): set (or clear) ONLY the IsPlayer bit of this
//        body's soul (never IsLocalPlayer, never PlayerSoul, never a copy); the first set arms the save gate (a detour on the per-soul save
//        step: Dude2 is written with IsPlayer 0, the bit set again right after). on=0 with eid 0 clears whatever soul holds it.
//   81 Dude2Census -> [souls:4][withPlayer:4][withLocal:4][playerSoulHasBit:1][firstOtherEid:4]   the save-decode check (after a load)
//   80 Dude2Check -> [armed:1][playerSoulOk:1][dude2Player:1][dude2Local:1][flagOff:4][saveClears:4]   the watchdog's read
//   82 Near [on:1][figureEid:4] -> [armed:1][swaps:4][bound:4]   V.2 nearest_player_triggers (host): wo201_near.cpp
//   83 SpeechPoll [on:1][figureEid:4] -> the lines the engine voiced since the last poll; 84 SpeechPlay [eid:4][len:1][path] -> [ok:1]
//        Act V F1 speech_share: wo201_speech.cpp
//   85 VotePoll [on:1][lastSeq:4][figureEid:4] -> [seq:4][dist:f32][n:1] rows [used:1][len:1][text]
//   86 RangedPoll [on:1] -> [n:1][phase...]   B11 figure_ranged: this player's crossbow / bow steps (wo201_ranged.cpp)   Act V F2 dialogue_vote: wo201_vote.cpp
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo201 {

constexpr uint8_t kOpStatus = 76, kOpConfig = 77, kOpCombatEnd = 78, kOpDude2 = 79, kOpDude2Check = 80, kOpDude2Census = 81, kOpNear = 82, kOpSpeechPoll = 83, kOpSpeechPlay = 84, kOpVotePoll = 85, kOpRangedPoll = 86;
constexpr uint8_t kRoleNone = 0, kRoleHost = 1, kRoleJoiner = 2;

void install();   // off the main thread: the resolver (reads only)
void tick();      // main thread, every frame
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);
void on_pipe_closed();   // the agent went away: every WO-201 part that acts for a session stands down

} // namespace kcdmp::wo201

namespace kcdmp::wo201speech {
void install();
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);
} // namespace kcdmp::wo201speech

namespace kcdmp::wo201ranged {
void install();
void tick();
void on_pipe_closed();
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);
} // namespace kcdmp::wo201ranged

namespace kcdmp::wo201vote {
void install();
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);
} // namespace kcdmp::wo201vote

namespace kcdmp::wo201near {
void install();
void tick();
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);

// B1: the engine's own per-NPC FastForward cancel (a C_NPC vtable offset) as the save path makes it; 0 = not resolved.
size_t ff_stop_slot();

} // namespace kcdmp::wo201
