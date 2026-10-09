// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-141: activities -- the DLL's half (docs/WO-141-findings.md).
//
// Every body the game drives (an NPC, the player, our avatars) has an NPC-state
// context: XGenAI's C_NPCContext, embedded at +0x9C0 of the body's XGenAI object
// (the one XGenAI's own ResetCurrentStateElementCommand asks by entity id). Its
// CURRENT state (+0x90) holds one element per kind in fixed slots:
//   [0] Stance    C_StanceElement    +0x28 stance id, +0x30 the smart object's WUID, +0x38 cart slot
//   [3] Unstance  C_UnstanceElement  +0x28 unstance id, +0x30 the location object's WUID
//   [5] Minigame  C_MinigameElement  +0x28 type, +0x30 the object's WUID, +0x38 slot
//   ([1]/[2] the hands, [4] equipment; further elements follow)
// That is "this body uses this object" in the game's own terms (WO-116's R5).
//
// Apply = the game's own placement after a load: the context's LOADED state
// (+0x180, a C_NPCRequiredState) is cleared by its own Clear, given a
// StanceElementRequired / UnstanceElement built by the reflection layer
// (rttr::type::create, the classes' own shared_ptr constructors), and
// C_NPCContext::ExecuteStateChangeIntoLoadedState runs it: the body is put on
// the object in the stance, or into the unstance at its location, in one frame,
// by the NPC-state machine itself -- on a suspended (paused) body too.
// The In/Out fragments are not played (it is the load's fast-forward).
//
// Pipe: 0x27 [op][...] -> 0xA6 [ok][seq][op][reason][payload]  (main thread)
//   op 1 Config [capture:1][apply:1][periodMs:2]  -> [armed:1]
//        capture bit 0 the tracked NPCs (host), bit 1 the local player
//   op 2 Apply  [nameLen:1][name][activity:30]    -> [known:1]   the body takes the activity
//        (reconciled until changed; "none" stands it up and ends the yield)
//   op 3 Leave  [nameLen:1][name]                 -> [had:1]     = Apply none
//   op 4 Status []                                -> [text]
//   op 5 Read   [nameLen:1][name]                 -> [found:1][activity:30]
//   op 6 Forget []                                -> [n:2]       every desired activity dropped (no apply)
//   op 7 Resync []                                -> []          host: every activity again on the next tick
//   op 8 Show   [tenths:1][unstLen:1][unstance][objLen:1][object entity name] -> [ok:1]
//        the local player's captured activity is that unstance at that object for
//        tenths/10 s (a one-shot of his the NPC state never holds: the trough's
//        WashFace shown as housekeeper_faceWash); 0 tenths ends it
//   op 9 Sweep  [nameLen:1][name][holdS:2]       -> [found:1][refusals:1]   WO-164 T1: loaded := the body now; the
//        host's refused activity is not asked again for holdS (0 = the usual pace)
//   op 10 UnstanceName [id:2]                    -> [name]                  WO-164 D1
// 0xA7 Activity (unsolicited): [count:1]{[kind:1][nameLen:1][name][activity:30]}
//        kind 1 a tracked NPC, 2 the local player (name empty)
// The activity is wo141rules::Activity on the wire (objects as entity GUIDs).

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo141 {

constexpr uint8_t kOpConfig = 1;
constexpr uint8_t kOpApply  = 2;
constexpr uint8_t kOpLeave  = 3;
constexpr uint8_t kOpStatus = 4;
constexpr uint8_t kOpRead   = 5;
constexpr uint8_t kOpForget = 6;
constexpr uint8_t kOpResync = 7;
constexpr uint8_t kOpShow   = 8;
constexpr uint8_t kOpSweep  = 9;          // WO-164 T1: [nameLen][name][holdS:2] -> [found:1][refusals:1]
constexpr uint8_t kOpUnstanceName = 10;   // WO-164 D1: [id:2] -> [name]

constexpr uint8_t kROk         = 0;
constexpr uint8_t kRBadRequest = 1;
constexpr uint8_t kRNotArmed   = 2;
constexpr uint8_t kRNotFound   = 3;
constexpr uint8_t kRFailed     = 4;

constexpr uint8_t kKindNpc    = 1;
constexpr uint8_t kKindPlayer = 2;

// Anchors only (reads of the loaded images); off the main thread is fine.
void install();
bool armed();

// Main thread, every frame.
void tick();

// Main thread (the pipe marshals it).
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
void on_pipe_closed();

using FrameFn = void (*)(const uint8_t* body, uint16_t len);
void set_frame_callback(FrameFn fn);

} // namespace kcdmp::wo141
