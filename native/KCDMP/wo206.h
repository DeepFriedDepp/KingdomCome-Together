// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-206 -- the test bench's two native halves (read-only; the pure rules are wo206_rules.h).
//
//   C  The digest sweep. The agent registers the quest keyspace (every State, Timer and TimeOfDayWatch node path of the installed
//      quest files) and arms the sweep at quiet points; the sweep reads each node's value through WO-200's read (a clock's state byte
//      or a State's int) a few a frame, and the agent reads back only the entries that changed. The agent builds the checkpoint
//      digests from them (WO-206 C: the whole keyspace, so a timer outside the State mirror is visible).
//   D  The animation probe. 10 times a second: every body within 40 m of the local player (the player, the partners' figures, the
//      copies this DLL drives, the game's own NPCs), its position and yaw, dead, its queued actions, every installed scope of its
//      action controller (WO-190's reader, a budget of reads a sample; the rest carry their last read), as one JSON line in
//      <game folder>\kcdmp-anim.jsonl (the bundle's research data; the log packer runs the detectors on it).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   120 Status -> text
//   121 DigestPaths [reset:1][n:1] n x ([len:2][path]) -> [total:4]
//   122 DigestArm [on:1] -> [passes:4][total:4]
//   123 DigestRead [max:1] -> [passes:4][total:4][dirtyLeft:4][n:1] n x ([index:4][found:1][kind:1][value:4])
//   124 Probe [on:1][machine:1 0 solo,1 host,2 joiner][hz:1][radiusM:1][scopeReads:1] (255 = unchanged) -> [on:1][lines:4]
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo206 {

constexpr uint8_t kOpStatus = 120, kOpDigestPaths = 121, kOpDigestArm = 122, kOpDigestRead = 123, kOpProbe = 124;

void tick();   // main thread, every frame
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);
void on_pipe_closed();   // main thread: the sweep disarms, the probe stops

} // namespace kcdmp::wo206
