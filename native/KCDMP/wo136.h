// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-136: world presence -- the native half of Phase 4 (fights)
// (docs/WO-136-findings.md). Host only; every call on the game's main thread.
//
// The engine picks an NPC's target from the hits and swings it perceives. A
// partner's avatar lands its hits as ATTRIBUTED damage (hits.cpp: the damage,
// the combat history, a skirmish add) -- no hit volume, so the NPC's own hit
// reaction never runs and it never turns to the avatar on its own. WO-132 then
// kept every NPC that fought the host on the host (override 0). Here the mod
// supplies the reaction the engine cannot see:
//
//   * threat: every host hit on an NPC (the player's real hits, hits.cpp's
//     marks), every attributed avatar hit and every avatar swing that lands
//     near an NPC counts for its source; an NPC switches to the source whose
//     recent threat (6 s) clearly beats its current opponent's, at most every
//     3 s -- sticky like the engine (a third party's hits alone rarely turn a
//     fighter), but a partner who keeps hurting it is fought back;
//   * a combatant: the avatar that swings at an NPC joins that NPC's skirmish
//     as ITS opponent (the avatar counts as fighting it), so the fight never
//     reads as the NPC's alone;
//   * the host's down: every NPC whose opponent is the local player and that
//     has an avatar within reach is handed to that avatar before the engine's
//     own escape rule (PlayerFlee -> SkirmishVictory, field log) closes the
//     skirmish; then the player leaves it. The fight goes on while someone is
//     still in it. An NPC turns only after it leaves its skirmish and is
//     added back against the avatar (leave + re-add; observed, H4), and its
//     combat model shows the new opponent a frame or more later: every switch
//     is read back by tick() for 3 s and redone (at most 3 times) when the
//     engine put it back on the player.
//
//   WO136-TARGET npc=<name> <from> -> <to> threat=<a>/<b> why=<w>
//   WO136-HANDOVER npc=<name> -> avatar eid=0x<id> (<dist> m) | none
//   WO136-SWING avatar eid=0x<id> at npc=<name> (<dist> m) skirmish=<added|kept>

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo136 {

// A hit or a swing on npcEid from srcEid (0 = the local player). weight: a
// landed hit 2, a swing 1. Decides at once whether the NPC turns. damage: the hit's
// measured health damage (0 = blocked / none); < 0 = not measured (WO-163 A6: a landed hit then counts a nominal damage).
void note_threat(uint32_t npcEid, uint32_t srcEid, int weight, const char* why, float damage = -1.0f);

// An avatar committed an attack (a joiner's swing): the NPC it swings at (the
// nearest live one within 4 m in its facing half) becomes its opponent and
// feels the threat. Returns that NPC's entity id (0 = nothing in reach).
uint32_t avatar_swing(uint32_t avatarEid);

// The host is down: hand its fights to the avatars fighting beside it. Returns
// how many NPCs go to a partner (taken at once, or queued and retried by
// tick() until their fight with the player closes). removePlayer: the player
// leaves the skirmish first.
int handover_fights(bool removePlayer, const char* why);

// Main thread, every frame (wo132::tick): the queued hand-overs.
void tick();

// Live checks only: a threat the host "makes" on an NPC (no input exists).
bool test_host_threat(uint32_t npcEid, int weight);

// WO-154 3.1: the host is a real target (mp_host_target, default on). A host
// blow on an NPC releases the forced target this module holds on it toward an
// avatar (wo139::host_struck releases a pursuit's), and the host's blows count
// as a threat on an NPC that fights an avatar even before an avatar touched
// it. The engine's own rules (its hit reaction, its skirmish's pick) and the
// threat rule above decide whom it fights. Off = 0.44.0.
void set_host_target(bool on);
bool host_target();

// WO-154 3.3: an avatar left its fight (its partner went down or woke, or
// ended his fights): every forced pair toward it is cleared, its threats are
// forgotten and its queued switches dropped. Returns the pairs cleared.
//   WO136-FORGET avatar eid=0x<id>: <n> forced pair(s) cleared, ... -- <why>
int forget_avatar(uint32_t avatarEid, const char* why);

void set_enabled(bool on);
bool enabled();
// WO-165 C1: the NPCs this module knows in a fight a partner is part of (threat rows and forced pairs), up to max.
int fight_npcs(uint32_t* out, int max);
int status_text(char* out, int n);

} // namespace kcdmp::wo136
