// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-139: crime and guards -- the DLL's half (docs/WO-139-findings.md).
//
// The trespass detector (the joiner; off until the agent's Config). On the
// joiner the host's NPCs are suspended copies: nobody there sees him, but his
// own game still knows where he stands. The engine keeps the player's trespass
// level in a signal on the player's actor (actor->vtbl[0xDD8]() + 0x10, read
// in the HUD's own init) and the HUD's C_UIHudStates listens to it: its
// listener (GUIModule, "OnTrespassChanged" below) is told every new level and
// tail-jumps to C_UIHudStates::SetTrespassState, which draws the warning.
// The listener is gated pass-through (it always runs): every level it is told
// is recorded, and the frame tick sends each change to the agent as the
// unsolicited 0xA3 with the player's position.
//
// Levels (the concept type trespassLevel, IPL_GameData Libs/concept/
// definitions.xml): 0 public, 1 semipublic, 2 semipersonal, 3 personal,
// 4 prohibited. The HUD shows its warning for 3 and 4 only (the listener maps
// 1 and 2 to 0 before drawing -- code-verified); that is the rule the agent
// uses for "trespassing" (wo139_rules.h).
//
// Anchors (fail closed, WO139-BUILD logs what armed):
//   * the string "SetTrespassState" -> of the functions loading it, the one
//     with the implementation's prologue (the other is the RTTR registration);
//   * the listener = the one function that tail-jumps to it and starts with the
//     exact 16 bytes below.
//
// Pipe: 0x25 [op][...] -> 0xA2 [ok][seq][op][reason][payload] (main thread).
//   op 1 Config [on:1]            -> [armed:1][level:1]   the detector on/off
//   op 2 Status []                -> [text]               one line (logs, live checks)
//   op 3 Pursue [on:1][avatarEid:4][nameLen:1][guard]  -> [result:1]
//        host: a guard who knows of the joiner's violent crime fights his avatar --
//        the skirmish manager's own add (guard vs avatar, override 1) and the game's
//        own "fight this one" (the Relation context combat_forcedTarget, WO-136's
//        lever) from the guard to the avatar. Off clears only a pair this module
//        set (the store is refcounted) and takes the guard out of its skirmish.
//        result: 1 set / cleared, 0 already so, 2 no such guard or avatar, 3 refused
//   op 4 Context [on:1][ctxLen:1][ctx][nameLen:1][entity] -> [result:1]
//        one entity script context on a named NPC or horse (WO-68's refcount-aware
//        setter): the host marks a victim of the joiner's avatar before a takedown
//        (crime_suppressMeleeStealthHitReaction: the game's stealth-hit branch would
//        otherwise write the HOST's player as the culprit; crime_ignoredCorpse /
//        crime_ignoredUnconsciousBody: its body found later is no one's crime);
//        the joiner marks a horse of the host's world as legal to ride
//        (crime_ignoredHorseTheft_Horse). Only names in wo139rules::context_allowed.
//        result: 1 written, 0 already so, 2 no such entity, 3 refused, 4 not allowed
//   op 5 PunishGate [on:1] -> [armed:1]   the punishment's time sets run nothing (wo137.h)
// 0xA3 Crime (unsolicited): [kind:1][...]
//   kind 1 Trespass [level:1][prev:1][x:4f][y:4f][z:4f]  a new trespass level

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo139 {

constexpr uint8_t kOpConfig = 1;
constexpr uint8_t kOpStatus = 2;
constexpr uint8_t kOpPursue = 3;
constexpr uint8_t kOpContext = 4;
constexpr uint8_t kOpPunishGate = 5;

constexpr uint8_t kROk         = 0;
constexpr uint8_t kRBadRequest = 1;
constexpr uint8_t kRNotArmed   = 2;
constexpr uint8_t kRFailed     = 4;

constexpr uint8_t kCrimeTrespass = 1;

// Off the main thread (the patch suspends every other thread).
void install();
bool armed();

// Main thread, every frame: a new level goes out (rate-limited by the engine's own edges).
void tick();

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

using FrameFn = void (*)(const uint8_t* body, uint16_t len);
void set_frame_callback(FrameFn fn);   // 0xA3 goes out through it

void on_pipe_closed();   // any thread: the detector off, every pursuit this module set cleared (on the main thread)

} // namespace kcdmp::wo139
