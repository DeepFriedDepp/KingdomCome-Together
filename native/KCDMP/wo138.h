// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-138: no pausing, and the host's NPC stream in the DLL (docs/WO-138-findings.md).
//
// Three pieces, all on the frame hook (C_ModulesManager::Update), which keeps
// running in every menu, the inventory, the map, a dialogue and a cutscene:
//
//   1. The native NPC sender (host). The tracked set comes from the mod's rescan
//      (the agent forwards it); positions, yaw, health, dead, KO and drawn are
//      read here every emitMs, the Lua sender's gates and flags are applied
//      (wo138_rules.h), and each row goes to the agent as the unsolicited 0xA0,
//      which the agent puts on the wire as the same NpcState (0x26) as before.
//      A menu that stops the Lua timers no longer stops the stream.
//   2. The frame meter. The game's frame time over real time says whether this
//      machine's world runs, is slowed (the Apse inventory's time-scale ratio)
//      or is frozen (a PauseGame source holds the game timer). Changes go out as
//      the unsolicited 0xA1.
//   3. The pause gate (both machines, only while the agent says a session with
//      a partner is live): CCryAction::PauseGame(pause, source, force) declines
//      a PAUSE from a source in the mask -- by default only the ESC menu
//      (InGameMenu, source 7). Resumes always run. Every call is counted per
//      source. The pipe closing turns the gate off.
//
// Pipe: 0x24 [op][...] -> 0x9F [ok][seq][op][reason][payload]. Every op runs on
// the main thread (the pipe marshals it).
//
//   op 1 Config  [on:1][emitMs:2][heartbeatMs:2][moveEpsMm:2][cull:1][cullRadiusM:2]
//                [farBandM:2][engageM:1][attrMs:2]                      -> []
//   op 2 Track   [gen:2][part:1][parts:1][count:1]{[flags:1][nameLen:1][name]}
//                flags bit 0 = not human.  -> [committed:1][size:2]
//                The parts of one generation are assembled; the last one
//                replaces the set (per-name state kept for names still in it).
//   op 3 Anchors [count:1]{[x:4f][y:4f][z:4f]}  (<= 8; the partners)    -> []
//   op 4 Status  [] -> Status (below, 48 bytes)
//   op 5 Levers  [on:1][mask:4] -> [armed:1]
//   op 6 Hold    [on:1] -> []           joiner: npcdrive::set_hold_all
//   op 7 Text    [] -> [text]           the status as one line (logs, live checks)
//   op 8 Read    [nameLen:1][name] -> [found:1][x,y,z,rot,hp:5x4f][dead][ko][drawn][costUs:4]
//                one native read of one NPC (live checks: native vs Lua)
//   op 9 Pause   [pause:1][source:2][force:1] -> [heldMask:2]
//                live checks only: CCryAction::PauseGame(pause, source, force)
//                through the hooked entry (the gate sees it) on the instance the
//                gate captured -- a stand-in for the ESC menu (source 7) when no
//                input can be sent. (Lua's Game.PauseGame is not registered on
//                1.5.5.) kRNotFound until a first PauseGame call was seen.
//
// 0xA0 NpcStream: [count:1]{[flags:1][x:4f][y:4f][z:4f][rot:4f][hp:4f][nameLen:1][name]}
// 0xA1 World:     [world:1][scalePermille:2][heldMask:2]
//                 world 0 running, 1 slowed, 2 frozen; heldMask = the PauseGame
//                 sources holding the game (bit per source, when known).

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo138 {

constexpr uint8_t kOpConfig  = 1;
constexpr uint8_t kOpTrack   = 2;
constexpr uint8_t kOpAnchors = 3;
constexpr uint8_t kOpStatus  = 4;
constexpr uint8_t kOpLevers  = 5;
constexpr uint8_t kOpHold    = 6;
constexpr uint8_t kOpText    = 7;
constexpr uint8_t kOpRead    = 8;
constexpr uint8_t kOpPause   = 9;

constexpr uint8_t kROk         = 0;
constexpr uint8_t kRBadRequest = 1;
constexpr uint8_t kRNotArmed   = 2;   // the PauseGame gate is not installed
constexpr uint8_t kRNotFound   = 3;
constexpr uint8_t kRFailed     = 4;

// op 4 reply, little-endian, 48 bytes.
struct Status {
    uint8_t  on;             // the sender is enabled
    uint8_t  world;          // 0 running, 1 slowed, 2 frozen (the latest 250 ms)
    uint16_t tracked;        // names in the set
    uint16_t resolved;       // names with a live entity
    uint16_t rowsLastSec;    // rows sent in the last full second
    uint32_t rowsTotal;
    uint32_t ticks;          // sender ticks run
    uint16_t lastTickAgeMs;  // since the sender's last tick (0xFFFF = never)
    uint16_t costP50Us;      // sender tick cost, last 100 ticks
    uint16_t costMaxUs;
    uint16_t scalePermille;  // game time / real time, the latest full second
    uint16_t fps;            // frames in the latest full second
    uint8_t  gateArmed;
    uint8_t  gateOn;
    uint32_t declined;       // pause requests the gate declined
    uint32_t pauseCalls;     // PauseGame calls seen
    uint16_t heldMask;       // sources with a non-zero counter (when the instance is known)
    uint16_t lastSource;     // the latest PauseGame call's source
    uint8_t  lastPause;      // ... and its pause flag
    uint8_t  holdAll;        // joiner: npcdrive hold
    uint16_t attrP50Us;      // one NPC's attribute read, last 100 reads
    uint32_t frames;         // frame hook calls (main_thread::frame_count, low 32 bits)
};
static_assert(sizeof(Status) == 48, "the op 4 reply is 48 bytes");

// Off the main thread (the patch suspends every other thread): find and arm
// the PauseGame gate. Fails closed (WO138-GATE NOT armed).
void install();
bool gate_armed();

// Main thread, every frame: the meter, then the sender (rate-limited).
void tick();

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);

using FrameFn = void (*)(uint8_t type, const uint8_t* body, uint16_t len);
void set_frame_callback(FrameFn fn);   // 0xA0 / 0xA1 go out through it

void on_pipe_closed();   // any thread: sender off, gate off, hold off

// WO-141: every tracked name with a live entity (main thread).
void for_each_tracked(void (*fn)(const char* name, uint32_t eid, void* ctx), void* ctx);

} // namespace kcdmp::wo138
