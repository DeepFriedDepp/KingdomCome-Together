#pragma once
// WO-143: activities, part 2 -- the DLL's half (docs/WO-143-findings.md).
//
// What else a body does, read and shown the game's own way:
//   * hand content (M3): the tool in each hand. Read from the NPC-state
//     context's current state, slots [1] LeftHand / [2] RightHand
//     (C_HandContentElement: +0x28 the item's WUID, +0x40 a flag, +0x48 the
//     hand 1|2 -- its GameLoad reads exactly those). The item's class id is the
//     game's own (C_Item slot 0x18). Applied through WO-141's placement: a
//     HandContentElementRequired in the loaded state beside the stance or the
//     unstance, holding an item of that class from the copy's own inventory.
//   * activity gaits (M5): the 9 actorCondition_* entity contexts (the WO-68
//     context manager's read and write, only pairs this DLL set).
//   * one-shots (M4): a C_AnimAction the NPC-state request carries as its extra
//     action (fragment +0x1A8, tags +0x1B0, align object +0x1B8), caught at
//     C_NPCContext::RequestStateChange, and played on the other machine's body
//     through the same request -- the one PlayerStateHandler.PlayAnimationAction
//     builds for the player (an anim action from the action database's
//     template, an anim-event context, the default required state).
//   * look targets (idles): C_NPCLookTarget (npc->vtbl[0x210]): the resolved
//     target +0x30 kind (0 none, 1 an entity, 2 a point), +0x38 its WUID.
//
// Pipe: 0x28 [op][...] -> 0xA8 [ok][seq][op][reason][payload]   (main thread)
// 0xA9 (unsolicited): the host's capture (wo143_rules.h frames).

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo143 {

constexpr uint8_t kROk         = 0;
constexpr uint8_t kRBadRequest = 1;
constexpr uint8_t kRNotArmed   = 2;
constexpr uint8_t kRNotFound   = 3;
constexpr uint8_t kRFailed     = 4;

// Anchors only (reads of the loaded images) plus the one entry hook
// (RequestStateChange); from the injection thread.
void install();

// Main thread, every frame.
void tick();

// Main thread (the pipe marshals it).
uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
void on_pipe_closed();

using FrameFn = void (*)(const uint8_t* body, uint16_t len);
void set_frame_callback(FrameFn fn);

// Main thread (the gait writer, motion.cpp): the host shows this copy hoeing --
// its creep along the row is the walking class, not standing.
bool activity_locomotion(uint32_t eid);

} // namespace kcdmp::wo143
