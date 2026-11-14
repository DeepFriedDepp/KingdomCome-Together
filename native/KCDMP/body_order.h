// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// A body of this world sent somewhere by its own movement (WO-221 D.2, mp_own_horse; docs/WO-221-findings.md).
//
// The game calls its one player's horse through the horse's own brain: a "come to me" command starts a move to the player (walk
// under 10 m, sprint under 20 m, else a dash; it stops 3 to 4 m short), and only when the horse has not arrived after 15 s is it
// put down out of sight behind the player. That target is the game's one player. For a joiner's horse the host's mod runs the same
// recipe with the engine's own movement request -- the one a brain hands the movement-task manager (wo215.h's order: a target and
// a speed class; the body's own movement system finds the way and walks it) -- made at the AI update's entry on its own thread.
//
// Pipe ops (the WO-163 family, registered through wo203.h; numbers 190..199 are WO-221's), main thread:
//   195 Go   [gait:1][name len:1][name][x y z:3f] -> [code:1][order:4]   (gait: 0 walk, 1 jog, 2 run, 3 sprint)
//   196 Stop [name len:1][name]                   -> [code:1]
// Codes: 1 ordered / stopped, 2 no such body here, 3 the request is not armed on this build, 4 the switch is off.
#include <cstdint>

namespace kcdmp::bodyorder {

constexpr uint8_t kOpGo = 195, kOpStop = 196;
constexpr uint8_t kOk = 1, kNoBody = 2, kNotArmed = 3, kOff = 4;

} // namespace kcdmp::bodyorder
