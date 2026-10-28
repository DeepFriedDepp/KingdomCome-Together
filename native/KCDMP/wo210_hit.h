// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-210 E.2 (docs/WO-210-findings.md): the local player's blow on a copy of the host's NPC is never judged on this screen.
// C_CombatRPG::RPGProcessHit's entry is gated (the prologue registry's kRpgProcessHit): for the local player's blow on a bound copy the
// gate runs what RPGProcessHit itself runs -- the attacker actor's lock and the hit core -- with the core's apply argument 0 (the engine's
// own dry run: record and result filled, nothing applied), mirrors its strength epilogue (one target per swing) and returns 0, the
// engine's "Hit was filtered": no damage, stamina, wear, reaction or death here. On the main tick the copy plays the impact (its hit
// event with the filled result, material effects only; the game's own entity context combat_actorSupressHitreactionAnimation stops the
// reaction after them) and the blow goes to the host as a LocalHit frame whose byte 26 says "judge it there" -- the host's engine decides.
// Switch: mp_copy_hit_local_none (bit 0x20); the impact alone: the twin's hit_fx. Fails closed (WO210-HITLOCAL not armed).
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo210hit {

// pipe_server: the LocalHit frame body [guid:16][stamina:4f][health:4f][died:1][byPlayer:1][judge:1]
using FrameFn = void (*)(const void* body, uint16_t len);
void set_frame_callback(FrameFn fn);

} // namespace kcdmp::wo210hit
