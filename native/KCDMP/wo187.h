// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-187 -- 0.60.5's native half. The pure rules are wo187_rules.h.
//
//   A2 a copy's opponent the engine's way: C_CombatActor::SetLogicalTarget (the vftable slot whose function references the engine's own
//      label "wh::combatmodule::C_CombatActor::SetLogicalTarget" -- found, never a fixed slot). It writes the requested target as well as
//      the target and opponent, so the opponent manager's own update (which puts the requested target back every idle/guard frame) keeps
//      it, where WO-178's direct C_CombatActorOpponentManager::SetTarget was undone. The agent names who each copy should fight (op 36,
//      from the host's npc-combat target); the wish is re-read every 0.5 s and on every wind-up, written back when something else wrote
//      it, and pinned with the game's own combat_forcedTarget relation after two overwrites (mp_opp_pin).
//        WO187-OPP npc=<n> was=<who> now=<who> why=<why>            a wish applied
//        WO187-OPP npc=<n> windup=held|overwritten opponent=<who>   every wind-up (the first 30, then every 50th)
//        WO187-OPP npc=<n> pinned ... / wish dropped ...
//   A3 the shield windows a copy's wind-up planned: WO187-PLAN npc=<n> planned=<types> refused=<0|1> opponent=<who> (wo166.cpp calls it).
//   C2 the guard between moves:
//      * a copy whose host NPC fights anyone holds combat mode (op 39: motion's guard hold, mp_anim_guard);
//      * the full-body scope reader (read-only: the actor's IActionController -> scope 0 "FullBody" -> its installed action: the RTTI
//        name, priority, flags, fragment id). CActionController / CActionScope are found by their RTTI; the scope's own vptr and name
//        are checked before anything is read from it. Logged around each move with mp_anim_scope_log (off by default):
//          WO187-SCOPE body=<n> t=<ms after the move> action=<rtti>|none prio=<p> flags=0x<f> frag=<id>
//      * mp_anim_idle (on): a body whose full-body scope stays empty 0.25 s after its move gets its idle back -- a body in combat mode its
//        guard idle (the guard zone written away and back: the engine's own UpdateMovementAnim re-queues CombatIdle on a zone change), any
//        other body WO-155's walk-class pulse.
//          WO187-IDLE body=<n> full-body scope empty <ms> ms after a move (combat=<0|1>) -> <remedy>
//   B1 positions for the agent's fight-position line (op 40).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   36 Opponent [copyEid:4][kind:1 0 clear,1 the local player,2 a figure][targetEid:4][why:1] -> [applied:1]
//   37 Status -> text
//   38 Config [logical][pin][guard][idle][defence][combo][scopeLog][linger][chaseFollow] (255 = unchanged; fewer bytes = the rest unchanged)
//      -> 9 bytes
//   39 GuardHold [eid:4][on:1][State2:12] -> [ok:1]
//   40 Positions [n:1][eid:4]*n (eid 0 = the local player) -> per body [ok:1][x:4][y:4][z:4]
#pragma once
#include <cstddef>
#include <cstdint>
#include <string>

namespace kcdmp::wo187 {

constexpr uint8_t kOpOpponent = 36, kOpStatus = 37, kOpConfig = 38, kOpGuardHold = 39, kOpPositions = 40;

// switches (the agent pushes them, op 38)
bool opp_logical_on();   // mp_opp_logical
bool opp_pin_on();       // mp_opp_pin
bool guard_on();         // mp_anim_guard
bool idle_on();          // mp_anim_idle
bool defence_on();       // mp_anim_defence (motion's capture: an NPC's block / dodge / perfect block go out as rows)
bool combo_on();         // mp_anim_combo (motion's capture: the local player's sync attacks -- combos, throws -- go out as attack rows)
bool scope_log_on();     // mp_anim_scope_log
bool linger_on();        // mp_anim_guard's figure half
bool chase_follow_on();  // mp_chase_follow (npc_drive.cpp: a running copy is not held for its swing)

// A2, main thread. The engine's own SetLogicalTarget on a combat actor; false when not armed or it faulted.
bool set_logical_target(void* ca, void* entity);
// WO-178's wind-up (wo166.cpp): the copy's target becomes `entity` the engine's way. hadIt = its requested target was that already.
bool ensure_target(void* ca, void* model, void* entity, bool* hadIt);
// Every wind-up of a copy (wo166.cpp, before its slot planning): its wish checked and written back if overwritten.
uint8_t on_windup(uint32_t eid, const char* name, void* ca, void* model);
// A3 (wo166.cpp): one wind-up's planning.
void note_plan(const char* name, bool opponentIsPlayer, int planned, bool refused);

// C2, main thread: a row was played on this body (pipe_server's swing op).
void note_move(uint32_t eid);
// The duel trace (wo178.cpp): this body's full-body action as text, "" when the reader is not armed.
std::string scope_text(uint32_t eid);
// The duel trace: the combat actor's current director action (its RTTI name without the namespace), "" unreadable.
std::string director_action(void* ca);

void tick();   // main thread, every frame
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);
void on_pipe_closed();

} // namespace kcdmp::wo187
