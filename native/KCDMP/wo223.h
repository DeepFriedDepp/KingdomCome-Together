// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-223 (docs/WO-223-findings.md): enemies notice the joiner, and fight him on both screens -- the native half's switch word, its
// status, and Part B's lever: the game's own per-pair "attend to this one" setting (the relation context PerceptionPriorityBoost,
// Tables :: Libs/Tables/ai/ScriptContext.xml, which Warhorse's own watch and check behaviours set from an NPC to the player) set from
// a hostile NPC to a joiner's figure. With it the NPC's own perception -- its sight cone, its view radius, its rays, the figure's
// own visibility -- attends to the figure as it attends to a player; what it then does is the engine's own enemy reaction.
//
// Pipe ops in the WO-163 family, registered through wo203.h (numbers 210..219 are WO-223's):
//   210 Config [bits:4][role:1] -> [effective bits:4]
//   211 Status [page:1] -> text (0 = the switches, k = the k-th area's own line)
//   212 Attend [on:1][npc eid:4][figure eid:4] -> [result:1] (1 changed, 0 already so, 255 refused)   (main thread)
//   213 AttendSet "<figure name>|<npc name>,<npc name>,..." -> [set:1][cleared:1][refused:1]: the hostile NPCs that attend to this
//       figure now; a pair of this figure's not named is cleared. A pair nobody named for kPairStaleS is cleared by the tick (the
//       agent or the mod stopped), and every pair when the switch goes off or this game is no host.
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo223 {

constexpr uint8_t kOpConfig = 210, kOpStatus = 211, kOpAttend = 212, kOpAttendSet = 213, kOpEngine = 214;
// B.3 (the mod's w223_split, the host): 215 "<npc name>" -- the enemy the mod called on the host's own player is pinned to him with the
// game's own forced-target relation (combat_forcedTarget) for kCallPinS; the tick clears the pin it set (never one the game set).
//   WO223-CALL on npc=<name> -> the host's own player: forced target for <s> s -> <set | was set already | refused>
//   WO223-CALL off npc=0x<eid> (<why>) -> <r>
constexpr uint8_t kOpCallPin = 215;
constexpr double kCallPinS = 6.0;
// C (w223_copy_target, the joiner): a copy whose host NPC fights this player (the mirror's word, fresh within kCopyPinFreshMs) is
// pinned to the local player with combat_forcedTarget; set again every kCopyPinAgainMs (WO-187's own unpin clears the same pair),
// cleared when its host NPC turns away.
//   WO223-PIN on npc=<name> -> the local player: its host NPC fights you (forced target <set | was set already>)
//   WO223-PIN off npc=0x<eid> (<why>)
constexpr unsigned kCopyPinFreshMs = 500, kCopyPinAgainMs = 250;
constexpr double kPairStaleS = 6.0;
constexpr float kFightSeatM = 0.75f;   // C: a held copy in the local player's fight this far from its host NPC's place...
constexpr double kFightSeatS = 0.3;    // ...this long: its hold ends (npc_drive)
constexpr double kHealSoonerS = 3.0;   // C: WO-210's repair of a copy out of his fight may come again after 2 s instead of 5 (wo208)
constexpr double kAiAdoptLongS = 0.7;  // C: a copy's own attack may be installed this long after it was asked for (wo209's window)

// the native word's bits (the agent's W223Switch.NativeNames, in order)
constexpr uint32_t kBitPerceive = 1u << 0;     // B: hostile NPCs attend to a joiner's figure as they attend to a player
constexpr uint32_t kBitCopyTarget = 1u << 1;   // C: on the joiner's screen a copy fights whom its host NPC fights, mapped to his world
constexpr uint32_t kBitCombatMode = 1u << 2;   // D: the engine's combat mode first, WO-222's offer for a blow it still reads out of it (off: the engine's word alone)
constexpr uint32_t kBitGuard = 1u << 3;        // E: what the old rules protect stays protected
constexpr uint32_t kBitNoticeLog = 1u << 4;    // F: who noticed whom first, whom each enemy fights on both screens (read-only)
constexpr uint32_t kBitsDefault = kBitPerceive | kBitCopyTarget | kBitCombatMode | kBitGuard | kBitNoticeLog;

constexpr const char* kAttendContext = "PerceptionPriorityBoost";

uint8_t role();            // 0 none, 1 the host, 2 a joiner in the host's world
bool on(uint32_t bit);     // the agent's word, less the bench twin's
uint32_t twin_off_bits(const char* names);
using StatusFn = int (*)(char* out, int n);
bool add_status(const char* area, StatusFn fn);   // static-init time

// F (the joiner, main thread; wo209's mirror calls it for every counted sample of a copy): the host's NPC fights <hostFightsMe ? the
// local player : someone else>; here the copy's opponent is `have` (named haveName), which is or is not the wanted one (match), and the
// copy is or is not in the local player's skirmish (inMyFight). Counted, and a change of right/wrong logged:
//   WO223-COPY npc=<n> wrong: the host's NPC fights you; its copy here fights <who> (in your fight: 0|1)
//   WO223-COPY npc=<n> right again after <s> s
void note_copy(uint32_t copy, const char* name, bool hostFightsMe, const char* haveName, bool match, bool inMyFight, double now);

// D (the host, main thread; wo132's watch calls it for every sample of an NPC whose opponent is a partner's figure): does the engine
// count that NPC as in combat mode?
void note_figure_combat(bool combatMode);

// D (the host, main thread; wo173's react calls it when the engine says a body cannot react and the body's opponent is the very
// figure that struck it -- WO-222's case): true = WO-222's offer is not made and the engine's answer stands (w223_combat_mode off
// with w223_perceive on). The default keeps the offer: the engine counts such a body in combat mode at most blows, not at all,
// and the blows it does not count are the only ones the offer ever fires for (the findings, Part D); counted either way.
bool retire_parity_offer();

// C (the joiner, main thread; npc_drive asks it for a body an activity holds): is this copy in the local player's fight, with
// w223_copy_target on? Then it is not held at a seat its host NPC has left. note_fight_seat counts and logs a hold ended that way:
//   WO223-SEAT npc=<n> in your fight and <m> m from its host NPC's place: its seat's hold ends, its own posture released
bool fight_seat_rule(uint32_t copy);
void note_fight_seat(const char* name, float offM, bool asked);

// B (main thread): the pair set or cleared; only pairs set here are ever cleared (the engine's store is counted).
// Returns 1 changed, 0 already so, -1 refused (a soul missing, the context not on this build, the store not armed).
int attend(uint32_t npcEid, uint32_t figureEid, bool on, const char* why);

} // namespace kcdmp::wo223
