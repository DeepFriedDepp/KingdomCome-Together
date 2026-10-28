// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-173 -- 0.49.0: the joiner's blows become real hits in the host's world (docs/WO-173-findings.md). The pure rules are wo173_rules.h.
//
//   C1 (host) the joiner's attributed hit on a host NPC, which WO-121 applied as raw damage on the NPC's soul, goes through the engine's own
//      hit processor instead: attacker = the joiner's figure (in Striking for his relayed swing -- WO-166's window on the figure), victim
//      = the NPC, the swing row's four attack fields (WO-165's replay, research/WO-162 Q1.7 "Level B"), then the collision handler's own
//      reaction tail (react below). The NPC's own combat rules decide hit / blocked / perfect / broken against a striker it sees; the engine
//      applies the blow once. A landed hit's health loss is then brought to what the joiner's game measured (wo173_rules.h
//      health_correction). Any precondition missing -> WO-121's raw path with the stamina capped (one raw blow never floors a fighter).
//        WO173-HIT npc=<n> engine=hit|blocked|pb|broken|- st=<engine>/<joiner> hp=<engine>/<joiner> joiner_said=st<f>/hp<f>
//                  reaction=<react> state=0x<victim state 0.25 s later> knockdown=yes|no fallback=-|<why>
//   C2 (host) every host NPC near a figure that goes down (IsUnconscious) or up is reported on change (the agent sends W164 NpcDown):
//      down_poll below.
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   18 RealHit [type:i8][zone:i8][hand:i8][strength:f32][rflags:1][the 0x19 attributed body: guid16 st hp flags avatarEid nameLen name]
//        rflags: 1 = the row's fields are known
//      -> [applied:1 (1 engine, 2 raw fallback, 0 nothing)][outcome:1][fallback:1][react:1][steps:1][attackerWuid:8][victimWuid:8][seq:4]
//         [st_applied:f32][hp_applied:f32]   (the raw path's amounts; the engine's are measured later and logged)
//   19 Status -> text
//   20 Config [realHits:1][threat:1][react:1] -> [realHits:1][threat:1][react:1]   (255 = unchanged)
//   21 DownPoll [count:1]{[nameLen:1][name]}* -> [count:1]{[state:1]}*   (the agent's list of NPCs near a figure; 0 up, 1 KO, 2 dead,
//        255 unknown)
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo173 {

constexpr uint8_t kOpRealHit = 18, kOpStatus = 19, kOpConfig = 20, kOpDownPoll = 21;

// Pipe thread -> main thread (run_sync_bounded). Returns a WO-163 reason code (kROk ...).
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

// Main thread, inside wo165::replay with kFlagReact, the same frame as the processor call: the collision handler's reaction tail for
// attacker combat actor `aCa` on victim combat actor `vCa` with the result struct the processor filled. Returns wo173rules::React.
uint8_t react(void* aCa, void* vCa, uint8_t* result, int32_t* kindOut);
// Main thread, same frame: the attacker model's "intended target" property, through the engine's own setter, as the collision handler sets it
// before the processor (the hit core then skips its facing test -- the joiner's own game already judged that he hit this NPC). prev = the
// value before (restored by the caller after the call). False = not armed / unreadable (the call then runs without it).
bool mark_intended(void* model, bool on, uint8_t* prev);
// WO-210 C.1: the collision handler's own ClientHit on a replay's result (react = WO-173's tail result, outcome = wo165::Outcome). Main thread.
bool client_hit(uint8_t* result, uint8_t react, uint8_t outcome);
bool clienthit_armed();
// WO-210 E.2 (main thread): the result struct's two lethal bytes, as the collision handler's reaction block reads them; false until the
// block verified (resolve_react).
bool lethal_offsets(uint32_t* lethal0, uint32_t* lethal1);

void tick();                 // main thread, every frame: the pending blows' measures and corrections
int status_text(char* out, int n);

} // namespace kcdmp::wo173
