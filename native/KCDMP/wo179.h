// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-179 -- 0.50.7: the scene guard's eyes and the scene bus's source (read-only; nothing in the game is written here).
//
//   tick() (main thread, every frame):
//     * the screen's fade: the GUI module's fader controller (RTTI C_FaderController, the WO-113 resolver) asked "faded out?"
//       every frame; every change is an edge -- WO179-FADE-NATIVE on|off held_ms=<n> -- with the frame time (GetTickCount64)
//     * the local player's position at 20 Hz: a move of more than kJumpM in one sample is a relocation edge -- the game moved
//       Henry (a scene's fade-and-move, a scripted trip, a load) -- WO179-JUMP from=... to=... own=0|1. own=1 = within kOwnMs of
//       this DLL's own teleport (the leash, a placement): the agent knows those.
//   Both kinds go into one ring the agent polls (op 32). The agent decides what a jump means (it knows the scene, the load, its pulls).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   30 Status -> text
//   31 Config [on:1] -> [on:1]   (255 = unchanged; on = mp_scene_guard's eyes and the bus's source)
//   32 Poll [sinceSeq:4] -> [lastSeq:4][faded:1][havePos:1][x:4][y:4][z:4][n:1] then n x
//        [seq:4][kind:1][ageMs:4][fx:4][fy:4][fz:4][tx:4][ty:4][tz:4]   (kind 1 fade on, 2 fade off, 3 jump, 4 own jump)
//   33 Fade [out:1][seconds:4 float] -> [ok:1]   the partner's own fade around a bring-along (the WO-113 fader: C_BasicFader
//        'KCDMP_respawn' on the RTTI-checked controller); out=0 fades back in (only after this DLL's own fade-out)
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo179 {

constexpr uint8_t kOpStatus = 30, kOpConfig = 31, kOpPoll = 32, kOpFade = 33;
constexpr uint8_t kEvFadeOn = 1, kEvFadeOff = 2, kEvJump = 3, kEvOwnJump = 4;

void tick();                 // main thread, every frame
void note_own_teleport();    // any thread: this DLL is about to move the local player (actions::teleport_player)
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);

} // namespace kcdmp::wo179
