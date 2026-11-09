// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-215 (docs/WO-215-findings.md): the animation substrate -- the native half. The pure rules are wo215_rules.h.
//
// A  The route. A bound copy on the ground is moved by the engine's own movement request -- the request a brain hands the movement-task
//    manager (XGenAIModule), the one the shipped console door wh_ai_RequestDebugMovement builds: a target, a speed class, an optional
//    exact positioning (the stop that faces a heading). The copy's own movement system then walks it with its own locomotion (start and
//    stop transitions, turns, foot planting, the gait the class asks for). The per-frame writer (npc_drive.cpp) stays as the corrector and
//    for what the route cannot do (falls, ragdolls, riders and mounts, fighting bodies, a refused request).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), registered through wo203.h (numbers 150..159 are WO-215's), main thread:
//   150 Config [bits:4] -> [effective bits:4]          wo215_rules.h kBit* (KCDMP_TEST_W215_OFF names a twin's bits off)
//   151 Status -> text
//   152 Probe (A.0, the bench): [mode:1][nameLen:1][name][x y z yaw:4f][gait:1][secs:2] -> [ok:1][id:4][why...]
//        mode 1 order (exact when gait has bit 0x80) / 2 stop / 3 follow a line for secs (a new order every frame) / 4 release
//   153 Tune [correctCm:2][perFrame:1]([minMs:2][stepCm:2][maxMs:2][leadMs:2]([approachCm:2])) -> the same eleven values back (13 bytes)
//        (mp_route_correct_m; the route's per-frame mode and its cadence -- the bench's sweep; 0xFFFF / 0xFF leave a value as it is)
#include <cstddef>
#include <cstdint>
#include "wo215_rules.h"

namespace kcdmp::wo215 {

constexpr uint8_t kOpConfig = 150, kOpStatus = 151, kOpProbe = 152, kOpTune = 153;

bool on(uint32_t bit);   // the agent's word, less the bench twin's
bool route_armed();      // A.0's request was found on this build (and has not faulted)

// ---- A: one bound body, one frame (main thread; npc_drive.cpp's writer asks before it writes) ----
struct RouteIn {
    uint32_t eid = 0;
    void* ent = nullptr;
    const char* name = nullptr;
    float newest[4]{};      // the stream's newest sample: x, y, z, yaw
    float render[4]{};      // where the writer would put the body this frame (the rendered sample)
    float streamMps = 0;    // the host NPC's planar speed over its newest samples (-1 unknown)
    float streamVz = 0;     // its vertical speed
    float streamVx = 0, streamVy = 0;   // its planar velocity (the lead's direction)
    bool figure = false, beast = false, parented = false, fight = false;
    bool human = true;      // a man or a woman (npc_scan body kind 0): horses and animals keep the writer (F: their riders and harness)
    bool physics = true;    // the body has physics here (a body out of this machine's physics range is entity-written)
    bool stall = false;     // a link stall (wo215_rules.h link_stall): this body's silence is the link's, not its NPC's stop
    float gapS = 0.1f;      // the body's usual gap between samples (wo215_rules.h gap_ema): the lead follows it
    double now = 0;
};
enum class Verdict : uint8_t { Writer = 0, Routed = 1, Correct = 2 };
struct RouteOut { Verdict v = Verdict::Writer; wo215rules::WriterWhy why = wo215rules::WriterWhy::Off; };
RouteOut route_frame(const RouteIn& in);
// The body left the writer (dropped, a hold, a posture): any request of ours on it is cancelled, its state forgotten. Main thread.
void route_release(uint32_t eid, const char* why);
// motion.cpp: the engine's own movement drives this body's gait (our pseudo-speed and velocity stay off it). Main thread.
bool routed(uint32_t eid);
// wo208.cpp: the copy's walk start and stop come from its own movement system (the owner never installs them). Main thread.
bool walk_own(uint32_t eid);
// A.0's probe owns this body for its run: every writer leaves it alone. Main thread.
bool probe_owned(uint32_t eid);

// ---- D.1 (the host): the census -- a tracked NPC that is dormant (its full body empty for 2 s, and no brain running or no physics: an
// encounter pool's parked wanderer) is not streamed until it wakes. Cached per body (read at most twice a second). Main thread.
bool dormant_host(uint32_t eid, void* ent, const char* name, double now);
// ---- D.1 (the joiner): the copy's engine could not set up its combat actor (the game's "Combat actor init failed"): it is a ghost --
// unbound, parked, reported, never left standing; E reads the set. Main thread.
void note_ghost(uint32_t eid, const char* name);
bool ghost(uint32_t eid);

void tick();   // main thread, every frame: the probe's follow and windows, the status lines
int status(char* out, int n);

} // namespace kcdmp::wo215
