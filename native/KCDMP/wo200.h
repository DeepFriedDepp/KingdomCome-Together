// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-200 -- one world, one story: actions, not results. The native half: the two gates (docs/WO-200-findings.md). The pure rules are
// wo200_rules.h; the agent decides everything that needs the partner (GameBridge.Wo200.cs).
//
//   Gate 1, the out-port tap   C_OutputTriggerPort's Trigger (its vftable by RTTI; the slot is the one whose function names itself
//                              "C_ActiveTriggerPort::Trigger" and that C_ActiveTriggerPort shares): every quest node's output trigger
//                              pulse, seen with (node, class, port) BEFORE it runs downstream. WO200-TAP.
//                                * the joiner drops every C_Timer / C_TimeOfDayWatch fire in a joined world (I.4 quiet clocks; WO200-QUIET)
//                                * a dialogue node's pulse is recorded (I.1) and runs as before; what it runs, synchronously, is that
//                                  pulse's closure (a thread mark), and a C_SceneFinishedWaiter enqueued in it continues it when it
//                                  finishes (the departure's OnFinished, 48 s later), for 5 minutes
//                                * WO-203 I.5: a C_CutsceneHandler whose scene a closure started carries that closure to its out-ports
//                                  (OnQueued, BeforePlay, AfterPlay, OnFinished -- frames later) for the node gate only: the forced
//                                  dialogue a fader's AfterPlay starts is then the joiner's own chain (B7 runs it) or, on the host, the
//                                  replay's (refused). Nothing is captured or sent for it; closure_now() does not see it.
//   Gate 2, the node gate      C_Node::Execute's entry (the export every node class uses: no class overrides that slot; prologue in
//                              hook_prologues.h): each node about to execute, decided by its RTTI class, a Function node's method, the
//                              souls its inputs name (the local player or not) and the closure it runs in. WO200-GATE.
//                              WO-203 I.5: in a replay's closure the host also starts no C_DialogueNode / C_DialogueWrapper and no
//                              C_CutsceneHandler scene that is a fader or an in-game cutscene (or not readable: fail closed) -- HostScene.
//   Replay (op 73, host)       the joiner's dialogue out-port, pulsed on the host's own node: the same port's own Trigger, inside a
//                              closure mark carrying the joiner's id (I.2); refused when the node is not loaded, hibernated or not a
//                              dialogue node.
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   70 Status -> text
//   71 Config [role:1][joined:1][tap:1][gate:1][log:1] (255 = unchanged) -> the same 5 bytes
//        tap bits 1 capture, 2 quiet clocks, 4 waiters (and scene handlers, WO-203) continue a closure; gate bits 1 puppets (I.5), 2 the
//        host's closure rules (I.2; WO-203 I.5's dialogue and scene starts too),
//        4 talker-only routing (I.3); log bits 1 every closure class the first time it is met, 2 research port dumps
//   72 Poll [sinceSeq:4] -> [lastSeq:4][more:1][n:1] n x [seq:4][kind:1][closure:4][ageMs:4][aLen:1][a][bLen:2][b]
//        kind 1 capture (a port, b node path), 2 quiet drop (a port, b clock path), 3 refused (a "verdict kind method", b path),
//        4 closure class (a kind, b "class method"), 5 replay ran (a result, b path)
//   73 Replay [closure:4][pathLen:2][path][portLen:1][port] -> [result:1][runtime:1]
//        result 1 ran, 0 no such node, 2 not a dialogue node, 3 no such out-port, 4 hibernated / not loaded, 5 fault, 6 not armed
//   74 ReadNode [pathLen:2][path] -> [found:1][kind:1][runtime:1][value:4][clsLen:1][cls]   (a clock's state byte, a State's int)
//   75 Research [sub:1]... sub 1 [pathLen:2][path]: drop that node's next out-port fire (O4-narrow); sub 2 [pathLen:2][path]: log every
//        port of that node with its direction, type and, for a soul type, what it reads now; -> [ok:1]
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo200 {

constexpr uint8_t kOpStatus = 70, kOpConfig = 71, kOpPoll = 72, kOpReplay = 73, kOpReadNode = 74, kOpResearch = 75;
constexpr uint8_t kEvCapture = 1, kEvQuietDrop = 2, kEvRefused = 3, kEvClosureClass = 4, kEvReplay = 5;

void install();   // off the main thread: the tap's slot and the node gate's inline hook (each fails closed on its own)
void tick();      // main thread, every frame
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);

// The closure the calling thread runs in (0 = none). wo137's detector marks a State change made inside one (flag 128) and sends its id.
// A scene's carry (WO-203 I.5) is not one: what a scene's out-port changes goes out as it did before the carry.
uint32_t closure_now();
// WO-206 C (main thread, read-only): a quest node's value for the cross-machine digest -- a clock's (C_Timer, C_TimeOfDayWatch) state
// byte or a State's int (-1 = not readable as either); kind = the gate's Kind. False = no node at that path.
bool read_value(const char* path, uint8_t* kind, int32_t* value);
// The joiner's apply of a host change the host's own conversation made: the talker-only rules hold while it runs (wo137's apply).
void set_mirror_talker(bool on);
// II.4 copy_neutral_always (config byte 6 bit 1, joiner in the host's world): a host copy's loaded state is kept equal to what it is
// on every reconcile tick, and the planner is never asked to place it (wo141's reconcile).
bool copy_neutral_always();

// WO-202 II.4: the node whose out-port pulse runs on this thread right now (its port), or nulls -- the source of a State change made
// inside that pulse (a timer's OnFinished, a watch, a trigger, a conversation's consequence).
void source_now(const void** node, const void** port);
// Its class (RTTI), its path in the quest graph and the port's name, read on this thread while the pulse holds the node.
void describe_source(const void* node, const void* port, char* cls, size_t ncls, char* path, size_t npath, char* pname, size_t npname);

} // namespace kcdmp::wo200
