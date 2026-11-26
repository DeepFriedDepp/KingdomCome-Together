// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-224 (docs/WO-224-findings.md): grabs and combos in step -- the native half's switch word, its status, the clinch instrument and
// the clinch a copy or a figure takes part in.
//
// Part A (w224_clinch). A clinch is the engine's own paired guard: C_CombatActorClinch::StartSyncGuard(action type, master, slave)
// puts both bodies' guard movement into a sync row, a clinch move is a SyncTransition the master starts for both, and it ends when a
// move's row ends in the plain guard. Before this work order the joiner's DLL refused every clinch between his player and a copy
// (WO-178 / WO-210 B.1), so a grab the host's NPC made on his figure played as one body's half on his screen.
//   * The instrument (w224_clinch_log, read-only, on in every twin): the two engine entries the mod already gated are detours now, so
//     the engine's own answer is seen: every clinch start (W224-SSG) and the clinch test's pass rate per kind of pair (W224-CAN),
//     with the condition a failed test of the local player's failed on (sampled on the main thread, the engine's own order).
//   * The start. The mod starts a clinch the engine's way -- the same entry, the engine's action type for a new clinch -- between a
//     copy and the local player (the host's NPC grabbed his figure) or between a figure and an NPC or its copy (the partner grabbed
//     it). The local player's own clinch test with a copy in his fight is the engine's again (he may grab it); a copy's own test
//     stays refused (its clinch is the host's NPC's to start).
//   * The end. A clinch the other machine has left is ended here: both bodies' guard type back to the plain guard and the engine's own
//     guard asserted (WO-197's EnsureGuardAction).
//
// Pipe ops in the WO-163 family, registered through wo203.h (numbers 220..229 are WO-224's):
//   220 Config [bits:4][role:1] -> [effective bits:4]
//   221 Status [page:1] -> text (0 = the switches, k = the k-th area's own line)
//   222 ClinchStart [master eid:4][slave eid:4] (0 = the local player) -> [result:1][master flag:1][slave flag:1]
//       result 1 started, 0 refused by the engine, 2 already in a clinch with each other, 255 no such bodies / not armed / switch off
//   223 ClinchEnd [a eid:4][b eid:4] (0 = the local player; b may be 0xFFFFFFFF = a's own opponent) -> [bodies ended:1]
//   224 ClinchRead [eid:4] (0 = the local player) -> [in clinch:1][master:1][guard type:4][opponent eid:4][scope0 name...]
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo224 {

constexpr uint8_t kOpConfig = 220, kOpStatus = 221, kOpClinchStart = 222, kOpClinchEnd = 223, kOpClinchRead = 224;
//   225 ClinchHost [in:1][this player's figure is the master:1][refused:1][copy eid:4] -> [1]: the joiner -- the host's word for his
//       own figure (in a clinch with that body's copy, or out; refused = the host's engine did not start the one his game started).
//       His DLL brings the local player's own clinch to it every frame (wo224.cpp, reconcile_tick).
constexpr uint8_t kOpClinchHost = 225;
//   226 ResolveNear [near eid:4][name hash:4] -> [eid:4]: the one NPC within 8 m of that entity whose name has this hash (0 = none or
//       more than one) -- the host's agent finds the body a joiner's clinch names.
constexpr uint8_t kOpResolveNear = 226;
//   227 WeaponRefresh [eid:4]([hint:1]) -> [result:1][right hand's class before:1][after:1]: a copy's combat weapon classes checked
//       against its hands and, when they differ, refreshed by the engine's own weapon-component update (1 refreshed, 0 the same,
//       255 not possible). hint = the host NPC's weapon class (0xFE it holds nothing, 0xFF no word): used while the copy's hand
//       holds no item object yet.
constexpr uint8_t kOpWeaponRefresh = 227;
//   228 ChainInput [eid:4][requested zone:1 (0xFF = none)][input class:4] -> [result:1]: B, the joiner -- the host's NPC made its
//       combo input; its copy makes the same input, with the same requested attack zone, when its own chain slot opens (1 queued,
//       0 not a copy whose attacks are its own here).
constexpr uint8_t kOpChainInput = 228;
// B: the body action the host's DLL hands its agent for an NPC's combo input (ActionKind.NpcChain): flags = the zone (0xFF none), the
// row field's first 4 bytes = the engine's input class.
constexpr uint8_t kActionNpcChain = 22;
// B (research, the bench only: KCDMP_TEST_W224_MODELS=1): a snapshot of this body's combat model and combo component, appended to
// kcdmp-w224-models.bin beside the game; a W224-MODEL line names the record. kind 1 = the host's NPC at its chain input, 2 = the copy
// at its own. Main thread; reads only.
void model_dump(int kind, uint32_t eid, int zone, int input);

// the native word's bits (the agent's W224Switch.NativeNames, in order)
constexpr uint32_t kBitClinch = 1u << 0;      // A: a grab plays on both bodies on both screens
constexpr uint32_t kBitChains = 1u << 1;      // B: an enemy's chain of swings plays on its copy as a chain
constexpr uint32_t kBitDeath = 1u << 2;       // D: the death handling without the suspect step
constexpr uint32_t kBitClinchLog = 1u << 3;   // A.0: the instrument (read-only; every twin keeps it)
constexpr uint32_t kBitsDefault = kBitClinch | kBitChains | kBitDeath | kBitClinchLog;

// The engine's combat action type for a NEW clinch (combat_action_type: guardStartSyncMovementMaster). StartSyncGuard's first
// argument is an action type, never a guard type (4 / 24 / 25): passed a guard type the engine finds no row and refuses.
constexpr int32_t kActionStartSyncGuard = 35;
constexpr int32_t kGuardTypeMain = 0;

uint8_t role();            // 0 none, 1 the host, 2 a joiner in the host's world
bool on(uint32_t bit);     // the agent's word, less the bench twin's
uint32_t twin_off_bits(const char* names);
using StatusFn = int (*)(char* out, int n);
bool add_status(const char* area, StatusFn fn);   // static-init time

// ---- A.0, the instrument (any thread: the engine's actor updates run on the main thread and on job workers) -------------------------
// wo178's detours call these with the engine's own arguments and answer. result: 1 the engine said yes, 0 the engine said no,
// 2 the mod refused before the engine was asked (a start: 3 = refused because the mod is ending that clinch). otherListed: the pair is the
// local player and a copy in the mod's table.
void note_ssg(int32_t type, const void* masterCa, const void* slaveCa, bool otherListed, int result);
void note_can(const void* askerCa, const void* oppCa, bool askerIsMe, bool oppIsMe, bool otherListed, int result);

// ---- A, the gates' rule (any thread) ----------------------------------------------------------------------------------------------
// The mod's own clinch start is on this thread's stack (wo178's start gate lets it through).
bool own_start_running();
// The engine's clinch test for a pair of the local player and a listed copy: true = the engine's own test decides (w224_clinch on and
// the local player is the one who asks); false = refused as before WO-224.
bool can_test_is_engines(bool askerIsMe);
// The engine's own clinch start for a pair of the local player and a listed copy: true = let it run (w224_clinch on; the engine's
// test already passed for it, or the mod's own start asked).
bool start_is_engines(bool masterIsMe);

// A re-applied sync guard (any action type but the start's) for a body whose clinch the mod is ending is refused: the engine's own
// Guard then falls back to the plain guard and guard type main (its own path when a sync guard cannot be re-established).
bool ending_refuses(int32_t type, const void* masterCa, const void* slaveCa);

// Any thread: a clinch with a partner's figure in it that the mod did not start is refused -- on the host the figure's own test and
// start (his own game says when he grabs), on the joiner every test and start between a copy and a figure (the host's world's alone).
bool figure_test_refused(const void* askerCa, const void* oppCa);
bool figure_start_refused(int32_t type, const void* masterCa, const void* slaveCa);

// The joiner (wo197's poll, main thread): the local player's own clinch started anew since the last ask -- its "enter" goes out even
// when the poll saw no gap between the one the mod ended and this one.
bool take_local_start();

// ---- A, the mod's clinch (main thread) ------------------------------------------------------------------------------------------
// 1 started, 0 the engine refused (why says what was read), 2 the two are in a clinch with each other already, -1 not possible here.
int start_clinch(uint32_t masterEid, uint32_t slaveEid, const char* who, char* why, size_t whyN);
// Ends a clinch on a body (and on its clinch partner when `withPartner`): guard type main, the engine's guard asserted. Returns the
// number of bodies it ended (0 = none was in one).
int end_clinch(uint32_t eid, bool withPartner, const char* who, unsigned minAgeMs = 0);
// A.3: a leave from the other machine does not end a clinch this machine's engine started less than this long ago.
constexpr unsigned kEndMinAgeMs = 400;
// A (main thread): a body's combat weapon classes against its hands, refreshed the engine's way when they differ (wo224.cpp).
int weapon_refresh(uint32_t eid, const char* why, int* beforeRight, int* afterRight);
// A.1 (the joiner, main thread): a host NPC's row for its copy while the copy is in the engine's clinch with the local player.
// 1 handled (the engine's paired move, or a half the engine's clinch already runs), 0 not such a row, -1 the engine refused the move.
int clinch_row(uint32_t copy, const char* spec);
bool clinch_row_recent(uint32_t copy);
struct ClinchRead { bool ok = false, in = false, master = false; int32_t guardType = -1; uint32_t opponent = 0; char scope0[48]{}, scope1[48]{}; };
ClinchRead read_clinch(uint32_t eid);   // 0 = the local player

} // namespace kcdmp::wo224
