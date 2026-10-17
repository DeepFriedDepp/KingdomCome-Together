// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-194 -- shared in-game cutscenes: the native half (docs/WO-194-findings.md). The pure rules are wo194_rules.h; the agent decides
// everything that needs the partner (GameBridge.Wo194.cs).
//
//   B1 the trigger   a detour on C_CutscenePlayer::EnqueueCutscene (GUIModule's export; prologue in hook_prologues.h): every in-game
//                    cutscene a quest enqueues on this machine is an event (its name from the cutscene, its handler's concept path from
//                    the caller's own argument -- &handler+0x340, the handler checked by RTTI C_CutsceneHandler). Other kinds pass.
//   B2 the copy      op 61: the other machine's own CutsceneHandler started through its EnqueueCutscene input (WO-137's pulse: the
//                    engine's own C_InputTriggerPort::Trigger), only when that handler is idle (state 0) and awake -- its quest then runs
//                    BeforePlay, streams the scene, plays and gets its own AfterPlay. Not idle = not at that step: nothing is played.
//   B3 the hold      the joiner's own enqueue (not the mod's, the partner not already playing it) is held at "initialized": AutoPlay
//                    (configuration +0x20, read by OnCutsceneInitialized) cleared. Released by the agent (the host's copy runs, a menu,
//                    the host is not at that step), by any save request (never held across a save), or by the DLL's own 3 s bound.
//                    A release puts AutoPlay back; an initialized copy is played through its handler's own PlayCutscene input (else
//                    C_CutscenePlayer::PlayCutscene, WO-189's call). Rechecked 1.5 s and 4 s later.
//   B3 / C time      op 63: the active in-game sequence's time (CMovieSystem GetPlayingTime / SetPlayingTime, the movie system found by
//                    RTTI among gEnv's pointers) and its stop (StopSequence: the normal end follows, OnCutsceneEnd).
//   A  the end       op 64: on the joiner, for every listed copy in a FastForward (C_NPC IsInFastForward(0)), C_NPC's own
//                    StopFastForward(0) -- WO-189's per-NPC call -- so the NPC manager's FastForward count drops to 0 and the scene
//                    positioning (gated on it) finishes. Never the local player; never on the host.
//   D  time of day   on the joiner, a scene's own time of day (C_IngameCutscene +0xC0, "has a time") is switched off until its Play ran
//                    (restored then): the host's clock gives the world its time (WO-137's rule for quest time sets).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   58 Status -> text
//   59 Config [role:1][hold:1][ffStop:1][timeGate:1][holdMs:2] -> the same   (255 / 0xFFFF = unchanged; role 1 host, 2 joiner)
//   60 Poll [sinceSeq:4] -> [lastSeq:4][n:1] n x [seq:4][kind:1][ageMs:4][nameLen:1][name][pathLen:1][path][detLen:1][detail]
//        kind 1 enqueued by this machine's quest, 2 enqueued by the mod (op 61), 3 enqueued and held, 4 hold released, 5 time of day
//        switched off, 6 time of day restored, 7 a release's re-check played it
//   61 Start [pathLen:1][path][nameLen:1][name] -> [result:1][state:1]   result 1 started (the enqueue was seen), 0 the handler is not idle
//        (state = its state), 2 no such node / not a CutsceneHandler, 3 fault, 4 port refused, 5 refused (not armed), 6 fired but no
//        enqueue followed, 7 the node is hibernated
//   62 Hold [sub:1] ... sub 1 PartnerPlaying [on:1][nameLen:1][name] -> [n:1]; sub 2 Release [why:1][nameLen:1][name] -> [n:1]
//        (why = wo194rules::Release; an empty name = every hold); sub 3 HandlerState [pathLen:1][path] -> [found:1][handler:1][state:1]
//   61 takes one more byte [asOwn:1] from the harness: 1 = the enqueue counts as this quest's own (the hold applies)
//   63 Seq [sub:1][nameLen:1][name] (+ [t:4f] for sub 2) -> sub 1 Get [res:1][t:4f]; sub 2 Set [res:1][before:4f][after:4f];
//        sub 3 Stop [res:1]   res 1 done, 0 no active in-game cutscene of that name (or not playing), 2 not armed, 3 fault, 4 refused
//   64 StopFF [n:2][eid:4]*n -> [stopped:2][inFF:2][noNpc:2][countBefore:4][countAfter:4]   (count = the NPC manager's FastForward count,
//        -1 unreadable); n = 0xFFFF and no list = every NPC of the world (live: on the joiner the gate also waited on NPCs the agent never
//        streamed -- the WO-131 guard's parked ones), the walk ends as soon as the count reads 0; the mod's own figures are skipped
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo194 {

constexpr uint8_t kOpStatus = 58, kOpConfig = 59, kOpPoll = 60, kOpStart = 61, kOpHold = 62, kOpSeq = 63, kOpStopFF = 64;
constexpr uint8_t kEvOwn = 1, kEvMod = 2, kEvHeld = 3, kEvReleased = 4, kEvTimeOff = 5, kEvTimeBack = 6, kEvRecheck = 7;

void install();   // off the main thread: the EnqueueCutscene detour (fails closed: WO194-HOOK not armed)
void tick();      // main thread, every frame
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);

} // namespace kcdmp::wo194
