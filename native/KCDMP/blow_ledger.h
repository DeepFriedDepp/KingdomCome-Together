// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-222 A.0 / E (docs/WO-222-findings.md): every blow and every swing accounted for. Read-only: nothing here changes a blow.
//
// The host (role 1): every melee blow on a body -- its own player's (the engine's hit processor, hits.cpp) and a joiner's (applied by
// wo173.cpp) -- is one record: who struck, the body's situation at that moment (what its own actions were: an attack under way, a
// block, a hit reaction; its stamina; its combat model's State), the engine's verdict, and what the body did next (the engine's own
// hit reaction entering on it, another of its own actions, or nothing). One line per blow once it is known:
//   W222-BLOW by=host|joiner npc=<n> verdict=<v> sit=<s> state=0x<s> st=<pct> next=<hit|attack|block|perfect|dodge|none> dt_ms=<n>
//             react=<wo173 code> kind=<n> held_ms=<n> row=t<type>/z<zone>/s<strength> atk=t<type>/z<zone>/ic<class> [n=<k>]
// The joiner (role 2): every attack of this player (the game's own attack action entering -- never input) is one record: what its blade
// touched (the hit processor) or, when it touched nothing, what was in front of it and why it was not touched:
//   W222-SWING n=<k> touched=<copy|body|none> npc=<n> engaged=<0|1> front=<n>@<d>m/<deg>deg reason=<r>
// The reasons: touched, out-of-reach, not-hostile-here (the copy is not in this player's fight), held (the copy is held for a swing of
// its own), dead, in-reach-no-touch (in reach and in this fight, nothing touched: a dodge, a miss, or no collision), nothing-in-front.
#include <cstdint>

namespace kcdmp::blow_ledger {

// any thread (the action capture hook): an NPC's action entered. cls: motion.cpp's classes (0 attack, 1 dodge, 2 perfect, 3 block,
// 4 hit reaction, 5 paired attack); byPlayer: a hit reaction whose attacker is the local player's combat actor.
void on_npc_action(uint32_t eid, uint8_t cls, bool byPlayer);
// any thread: the local player's own attack action entered (its combat model's attack type / zone / input class).
void on_player_attack(int8_t type, int8_t zone, int8_t ic);
// any thread: the local player's own hit reaction entered (an NPC's blow on him) -- an open swing it cuts short is "interrupted"
void on_player_hit();
// any thread (the hit processor): the local player's melee blow on a body. verdict: 1 hit, 2 blocked, 3 perfect block, 4 broken.
void on_player_blow(uint32_t veid, uint8_t verdict);
// the joiner's touches on copies go through WO-210's gate (judged on the host): its verdict codes for on_player_blow
constexpr uint8_t kGatedSent = 9, kGatedNoType = 10, kGatedRepeat = 11;
// main thread (wo173.cpp): a joiner's blow was applied on the host. verdict as above (0 = the engine did not judge it); react = wo173's
// React code; heldS = how long it waited for the figure's strike window (-1 not held).
void on_figure_blow(uint32_t veid, uint8_t verdict, uint8_t react, int32_t kind, double heldS, int8_t type, int8_t zone, float strength,
                    uint8_t fallback);

} // namespace kcdmp::blow_ledger
