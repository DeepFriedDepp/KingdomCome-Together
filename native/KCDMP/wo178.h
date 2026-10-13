// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-178 -- 0.50.5's native half. The pure rules are wo178_rules.h.
//
//   C1 the hostility answer (mp_joiner_lock, joiner; mp_host_lock_answer, host, default off). The player's opponent picker drops every
//      candidate the combat RPG's "is hostile to" test refuses, and that test only knows a shared skirmish -- which the joiner's paused
//      copies never join (their brains never run). A detour on the test (found through C_CombatPlayerController::HandleOpponentSelection's
//      label: its selector loads the combat RPG and calls the test; prologue in hook_prologues.h) answers "hostile" only when the asker is
//      the local player and the candidate is a copy the agent named (op 24: engaged with this player, or its host NPC fighting the host),
//      or on the host with mp_host_lock_answer an NPC fighting a partner's figure -- never a figure, an arranged-fight actor (WO-176), a
//      dead (WO-172) or knocked-out body. Every other test the picker applies (distance, angle, visibility, its priorities) stays the
//      game's; no skirmish is created or changed, nothing is saved. The hook reads one pointer of the engine (the test's owner) and a
//      table the main thread publishes; it may run on any thread. Any fault switches it to pass-through for the session.
//        WO178-HOSTILE answer=yes|game npc=<n> why=engaged|partner-fight|figure-fight|<exclusion> asked=<n> game_said_no=<n>
//        WO178-HOSTILE-STATS per_min ...
//   Part 0 the schnapps save: C_PlayerProfileWHManager::InitiateSaveGame(ManualSave, -1, null) -- what drinking a Saviour Schnapps asks
//      for, without the item (the item is consumed by the inventory, not by the save). The manager is found by its RTTI among the game
//      interface's pointers; CanSave(ManualSave) is asked first (locks, scenes, combat, death).
//        WO178-SAVE result=requested|refused|not-armed|fault why=<caller> ...
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   24 Offer [eid:4][why:1][flags:1] -> [rows:1]   (why 0 = withdraw; flags bit 0 = knocked out; an offer lives kOfferTtlS)
//   25 Status -> text
//   26 Config [joinerLock:1][hostAnswer:1][hold:1][engineCap:1][host:1][hitReach:1]?[clinchGuard:1]?[ownBlock:1]? [copyEngineState:1]? -> five..nine
//   (mp_clinch_guard: C_CombatActorClinch::StartSyncGuard gated -- no clinch between the local player and a listed copy)   (255 = unchanged; hold / engineCap are C4's, wo173.cpp;
//      host = this machine is the host: the answer then reads mp_host_lock_answer)
//   27 SaveManual [whyLen:1][why] -> [result:1][text]   (1 requested, 0 refused by the engine, 2 not armed, 3 fault)
//   28 HeldResult [seq:4] -> [state:1][WO-173 op 18's 33-byte answer]   (C4: a held blow; 0 unknown, 1 still held, 2 done)
//   29 Trace [secs:2] -> [ok:1]   (the duel trace for secs, 0 = stop; at most 600)
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo178 {

constexpr uint8_t kOpOffer = 24, kOpStatus = 25, kOpConfig = 26, kOpSaveManual = 27, kOpHeldResult = 28, kOpTrace = 29;

// The duel trace (research, read-only; op 29 [secs:2]): every frame the local player's combat model and his opponent's are read and each
// change is logged with its time, with the combat actions entering for either (motion's capture point). WO178-TRACE t=<ms> ...
bool trace_on();
void trace_action(uint8_t cls, bool isPlayer, uint32_t npcEid);   // any thread

// Off the main thread at start (the patch suspends every other thread). Fails closed: WO178-HOOK not armed: <why>.
void install();
void tick();   // main thread, every frame (rebuilds the table at 4 Hz, logs, the stats once a minute)
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);

// Host (WO-165's lock tick, main thread): an NPC fighting a partner's figure, offered while mp_host_lock_answer is on.
bool host_answer_on();
void offer_local(uint32_t eid);

// C4 switches (wo173.cpp reads them).
bool hold_on();
bool hit_reach_on();   // mp_hit_reach: a copy's blow out of reach of the local player is a miss (wo165.cpp)
bool own_block_on();   // mp_own_block: the local player's held block decides a copy's blow (wo165.cpp)
bool copy_engine_state_on();   // mp_copy_engine_state: a copy's swing winds up and strikes through the engine's state setter (wo166.cpp)
bool engine_cap_on();

// Part 0, main thread: the schnapps save. 1 requested, 0 refused by the engine, 2 not armed, 3 fault.
uint8_t save_manual(const char* why);

} // namespace kcdmp::wo178
