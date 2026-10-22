// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-203 -- join the conversation, and every fix from session C (docs/WO-203-findings.md). The native half is split by area,
// one file each (wo203_conv.cpp, wo203_defence.cpp, wo203_stream.cpp, wo203_appear.cpp, wo203_quest.cpp, wo203_join.cpp); each area
// registers its pipe ops, its install, its per-frame tick and its status text here at static-initialisation time, so no shared
// file changes when an area grows. Registration only stores pointers: nothing runs before install().
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread, one owner per number (append-only):
//    90        Status -> text (every area's status line)
//    91 .. 95  conv     (Act I, join the conversation)
//    96 .. 100 defence  (II.2, the joiner's block / parry / perfect block judged by his own engine)
//   101 .. 104 stream   (II.6 fresh fighting positions, II.7 recoils and clinches, II.11 the frame report)
//   105 .. 108 appear   (II.8 worn sets, II.9 poses and the T-pose detector, II.10 Henry dressed)
//   109 .. 111 quest    (II.3 resets on the joiner, II.4 focus / observe)
//   112 .. 114 join     (II.1 the full hold, II.12 the journey)
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo203 {

constexpr uint8_t kOpStatus = 90;
constexpr uint8_t kOpConvFirst = 91, kOpConvLast = 95;
constexpr uint8_t kOpDefenceFirst = 96, kOpDefenceLast = 100;
constexpr uint8_t kOpStreamFirst = 101, kOpStreamLast = 104;
constexpr uint8_t kOpAppearFirst = 105, kOpAppearLast = 108;
constexpr uint8_t kOpQuestFirst = 109, kOpQuestLast = 111;
constexpr uint8_t kOpJoinFirst = 112, kOpJoinLast = 114;

// A pipe op handler: the same contract as every WO's handle() (body[0] is the op; returns a wo163 reply code).
using OpFn = uint8_t (*)(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
using VoidFn = void (*)();
using StatusFn = int (*)(char* out, int n);

// Called from an area's file at static-initialisation time (e.g. `const bool kReg = wo203::reg_op(96, &handle);`).
bool reg_op(uint8_t op, OpFn fn);
bool reg_install(const char* area, VoidFn fn);
bool reg_tick(const char* area, VoidFn fn);
bool reg_status(const char* area, StatusFn fn);

// WO-203B: an op that may run on the pipe's reader thread (no engine call in it, its own lock); returns false to leave the request
// to the main-thread path (handle()). reason = a wo163 reply code.
using OffMainFn = bool (*)(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen, uint8_t* reason);
bool reg_op_off_main(uint8_t op, OffMainFn fn);
bool handle_off_main(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen, uint8_t* reason);

bool has_op(uint8_t op);
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

void install();   // dllmain: every registered install, once
void tick();      // main thread, every frame: every registered tick
int status_text(char* out, int n);

} // namespace kcdmp::wo203
