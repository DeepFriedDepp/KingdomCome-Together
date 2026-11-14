// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// A dead body's wounds, read on the host and shown on the joiner (WO-221 E.1, mp_corpse_look; docs/WO-221-findings.md).
//
// The game keeps a body's blood per zone (six zones: the script's head, torso, arm_left, arm_right and the legs) in the actor's
// dirt helper (C_ActorDirtHelper, EntityModule: made on first use, kept by the actor; found by RTTI and checked by its bytes). A
// body the joiner did not see die -- one of the save, one killed while he was away, one an event of the host's game made and
// bloodied by script -- has none of it on the joiner's stand-in: clean where the host's is beaten. The host reads the six values
// with the helper's own read, the joiner sets them with the helper's own SetBloodGlobal -- the call the game's script bind
// (Actor.AddBlood) makes, on the main thread.
//
// Pipe ops (the WO-163 family, registered through wo203.h; numbers 190..199 are WO-221's), main thread:
//   197 Read [name len:1][name]            -> [code:1][six zones, 0..255 each]
//   198 Set  [name len:1][name][six zones] -> [code:1][zones changed:1]
// Codes: 1 ok, 2 no such body here, 3 not armed on this build, 4 the switch is off, 5 the body has no helper (never bloodied; Read: zeros).
#include <cstdint>

namespace kcdmp::corpselook {

constexpr uint8_t kOpRead = 197, kOpSet = 198;
constexpr uint8_t kOk = 1, kNoBody = 2, kNotArmed = 3, kOff = 4, kNoHelper = 5;
constexpr int kZones = 6;

} // namespace kcdmp::corpselook
