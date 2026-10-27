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

constexpr uint8_t kOpOpponent = 36, kOpStatus = 37, kOpConfig = 38, kOpGuardHold = 39, kOpPositions = 40, kOpMoveShown = 41;

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
bool avatar_hit_on();    // mp_anim_avatar_hit (motion's capture: the local player's own hit reaction goes out as ActionKind.AvatarHit)

// A2, main thread. The engine's own SetLogicalTarget on a combat actor; false when not armed or it faulted.
bool set_logical_target(void* ca, void* entity);
// WO-178's wind-up (wo166.cpp): the copy's target becomes `entity` the engine's way. hadIt = its requested target was that already.
bool ensure_target(void* ca, void* model, void* entity, bool* hadIt);
// Every wind-up of a copy (wo166.cpp, before its slot planning): its wish checked and written back if overwritten.
uint8_t on_windup(uint32_t eid, const char* name, void* ca, void* model);
// WO-195 (main thread): this copy's wish dropped (its pin cleared). Returns wo195rules::kHeldWish (| kHeldPin) for what it had, 0 = none.
uint8_t drop_wish(uint32_t eid, const char* why);
// WO-209 A (main thread): the opponent mirror's wish -- kind 1 the local player, 2 an entity (a figure, or the copy of the NPC the host's
// NPC fights), 0 clears a mirrored wish. Refreshed by every stream word (stale after kOppStaleS like an agent's), re-checked every
// kMirrorRecheckS, pinned with combat_forcedTarget at once (the copy's own AI never picks), and the agent's op 36 leaves the copy alone
// while it holds. F.1 still applies to the local player. Returns whether the copy's opponent is the wished one now (have = it).
bool mirror_wish(uint32_t copy, uint8_t kind, uint32_t target, uint32_t* have);
// A3 (wo166.cpp): one wind-up's planning.
void note_plan(const char* name, bool opponentIsPlayer, int planned, bool refused);

// C2, main thread: a row was played on this body (pipe_server's swing op).
void note_move(uint32_t eid);
// The duel trace (wo178.cpp): this body's full-body action as text, "" when the reader is not armed.
std::string scope_text(uint32_t eid);
// WO-190 0.1 (main thread, read-only): every scope of the body's action controller -- its name and its installed action (RTTI name without
// the namespace, priority, flags, fragment id; act = the action object, only compared, never dereferenced by the caller). Returns the count
// read, -1 when the reader is not armed or the body has no checked controller.
struct ScopeInfo { bool ok = false; bool empty = true; int index = 0; char name[24]{}; char action[48]{}; const void* act = nullptr; int32_t prio = 0;
                   uint32_t flags = 0, frag = 0; };
int read_all_scopes(uint32_t eid, ScopeInfo* out, int max);
// WO-204 II.2 (main thread, read-only): the body's queued (not yet installed) Mannequin actions -- the CActionController's queue vector
// at +0x50 / +0x58 (CryAction's own over-full trim reads it there: "attempting to queue too many actions ... up to 10 allowed"), the
// first one's class and priority, and how many carry flag 4 at +0x2C (the ones that trim keeps: "Can't remove anything"). -1 unreadable.
// WO-207 G.1: acts = the queued action objects (the first 16), only ever compared (the probe ages them), never dereferenced by a caller.
// WO-207 G.1: flags = each queued action's flags (+0x2C) beside it (the probe tells a never-started action from one waiting to resume).
struct QueueInfo { int n = -1; int kept = 0; char first[48]{}; int32_t firstPrio = 0; const void* acts[16]{}; uint32_t flags[16]{}; int32_t prio[16]{}; int nacts = 0; };
QueueInfo queue_info(uint32_t eid);
// WO-190 0.1 (main thread, read-only): the body's Mannequin tag state (its action controller's context +0x10, WO-100's read), n <= 20 bytes.
bool tag_state(uint32_t eid, uint8_t* out, size_t n);
// WO-201 follow-up (research, main thread, read-only): the animated character's desired motion parameters (id 0 travel speed, 2 travel
// angle, 4 turn angle, 6 stop leg: CryAnimation's EMotionParamID) and the actor's requested velocity.
struct MotionParams { int count = 0; uint8_t id[6]{}; float value[6]{}; float reqVel[2]{}; };
bool motion_params(uint32_t eid, MotionParams* out);
// The duel trace: the combat actor's current director action (its RTTI name without the namespace), "" unreadable.
std::string director_action(void* ca);
// WO-197 B0 (main thread, read-only): one scope of the combat director (C_CombatActorDirector at combat actor +0x2E0; its actions are a vector
// by scope: 0 main, 1 guard movement, 4 FailedAttack / pose modifier, 5 guard variation). ok = the director read; act = the scope's action
// (null = none running), name = its RTTI name without "C_CombatActorAction" (e.g. "Guard", "Hit", "FailedAttack", "SyncTransition").
struct DirAction { bool ok = false; int scopes = 0; const void* act = nullptr; char name[48]{}; };
DirAction director_scope(void* ca, int scope);
// "" unreadable, "none", or the scope's action name.
std::string director_text(void* ca, int scope);

void tick();   // main thread, every frame
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);
void on_pipe_closed();

} // namespace kcdmp::wo187
