// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-140: sleeping together -- the DLL's half (docs/WO-140-findings.md).
//
// Every skip of the player's own (a bed's sleep, the Wait key, reading in bed)
// opens PlayerModule's C_SkipTime picker through ONE function,
// C_SkipTime::ShowDialog(this, int id, float, float, const S16* spot, float),
// which returns false when it shows nothing. While the agent's Config says a
// vote is needed, that function is gated: a sleep / wait / read picker that
// was not approved does not open (it returns false, exactly the game's own
// "cannot skip now"), its arguments are kept and the agent is told (0xA5 kind
// 1). The mod holds the bed itself earlier, before the lie-down (kdcmp.lua,
// WO-140); this gate catches every other way in (the Wait key, sitting on a
// bed and then sleeping, reading). On the vote's yes the agent Approves: the
// kept picker is shown again with the same arguments (or the bed's own flow
// runs and its picker passes the one-shot approval).
//
// The frame tick reads C_SkipTime's state (+0x68: 0 idle, 1 the picker, 2/3
// the skip), its id (+0x6C) and the chosen hours (+0x74, written by
// OnAcceptDialog) and sends every edge (0xA5 kind 2): the asker's "began" with
// the hours starts everyone else's sleep screen.
//
// The accepter's own sleep screen is the game's: StartSkipTime (vftable slot 5,
// the one wh_pl_ForcedSkipTime calls: ShowDialog + the hours + confirm) with
// the Sleep id -- the sleep screen, the clock, the rest.
//
// One clock (the joiner's copy only): C_Calendar::SetWorldTime refuses a time
// lower than the current ("World time must not be set backwards"), so a pull
// lowers the calendar's own milliseconds (+0x68) to one below the target and
// then calls the real setter with the target: the game's own change handlers
// run.
//
// Anchors (fail closed, WO140-BUILD logs what armed):
//   * ShowDialog: the one function loading "wh::playermodule::C_SkipTime::ShowDialog",
//     with the exact 16-byte prologue below;
//   * the instance: PlayerModule's export ?I@C_SkipTime@playermodule@wh@@SAAEAV123@XZ;
//     its vftable slot 14 must BE ShowDialog (else nothing arms);
//   * the calendar: Shared.dll's wh::GetGameIface() + 0x1B0 (the pointer
//     C_GameInterface::SetCalendar stores); C_Calendar::SetWorldTime = the one
//     function loading "wh::rpgmodule::C_Calendar::SetWorldTime", with its prologue.
//
// Pipe: 0x26 [op][...] -> 0xA4 [ok][seq][op][reason][payload] (main thread).
//   op 1 Config  [gate:1]            -> [armed:1][state:1]   the gate + the state edges on/off
//   op 2 Status  []                  -> [text]
//   op 3 Approve [ms:4]              -> [replayed:1]         the next picker passes (one-shot, ms window);
//                                                            a kept picker is shown again now
//   op 4 Drop    []                  -> [had:1]              the kept picker forgotten, the approval cleared
//   op 5 Start   [id:1][hours:4f]    -> [result:1]           this game's own skip, no bed: 1 started,
//                                                            0 refused (the game said no), 2 busy, 3 bad hours
//   op 6 Stop    []                  -> [result:1]           the running skip ends now (1 stopped, 0 none)
//   op 7 Pull    [expect:4][target:4]-> [result:1][before:4][after:4]   world seconds; 1 pulled,
//                                                            0 not needed, 2 the calendar reads otherwise
//   op 8 Clock   []                  -> [sec:4]
// 0xA5 Sleep (unsolicited): [kind:1][...]
//   kind 1 Held  [id:1]                              a picker was held (the gate)
//   kind 2 State [edge:1][id:1][hours:4f][state:1]   an edge (wo140rules::Edge)

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo140 {

constexpr uint8_t kOpConfig  = 1;
constexpr uint8_t kOpStatus  = 2;
constexpr uint8_t kOpApprove = 3;
constexpr uint8_t kOpDrop    = 4;
constexpr uint8_t kOpStart   = 5;
constexpr uint8_t kOpStop    = 6;
constexpr uint8_t kOpPull    = 7;
constexpr uint8_t kOpClock   = 8;

constexpr uint8_t kROk         = 0;
constexpr uint8_t kRBadRequest = 1;
constexpr uint8_t kRNotArmed   = 2;
constexpr uint8_t kRFailed     = 4;

constexpr uint8_t kFrameHeld  = 1;
constexpr uint8_t kFrameState = 2;

// Off the main thread (the patch suspends every other thread).
void install();

// Main thread, every frame: the state edges, the kept frames.
void tick();

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

using FrameFn = void (*)(const uint8_t* body, uint16_t len);
void set_frame_callback(FrameFn fn);   // 0xA5 goes out through it

void on_pipe_closed();   // any thread: the gate off, nothing kept

} // namespace kcdmp::wo140
