// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-217 (docs/WO-217-findings.md): the world around the bodies -- the native half. The pure rules are wo217_rules.h.
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), registered through wo203.h (numbers 170..179 are WO-217's):
//   170 Config [bits:4][role:1] -> [effective bits:4]   wo217_rules.h kBit* (KCDMP_TEST_W217_OFF names a twin's bits off)
//   171 Status -> text
#include <cstddef>
#include <cstdint>
#include "wo217_rules.h"

namespace kcdmp::wo217 {

constexpr uint8_t kOpConfig = 170, kOpStatus = 171;
//   172 Carts [host_ms:4][n:1] n x ([len:1][name][x y z ax ay az: 6 floats]) -> [] (F: the host's carts, the joiner plays them back)
constexpr uint8_t kOpCarts = 172;
uint8_t role();              // 0 none, 1 the host, 2 a joiner in the host's world (op 170's)

bool on(uint32_t bit);       // the agent's word, less the bench twin's
bool configured();
int status(char* out, int n);

// ---- A.0: one activity attempt on an owned copy (the owner calls these on the main thread) ----
struct PlaceEp {
    bool open = false;
    uint64_t hostAct = 0;           // the host NPC's full-body action of the attempt
    uint32_t frag = 0;              // its fragment
    uint8_t fam = 0;                // its posture family (wo210rules::PFam)
    double t0 = 0, shownAt = -1, lostAt = -1, lostFor = 0;
    uint16_t losses = 0;
    wo217rules::Counts c;
    char refuseWhy[32]{};           // the engine's refusal of the install (rttr::swing_result_name)
    char staleHead[48]{};           // what held the copy's queue head when our install went stale
    char lostTo[64]{};              // what replaced a shown activity
    char act[112]{};                // WO-141's activity for the body when the attempt opened (stance, unstance, objects)
    int path = 0;                   // WO-141's activity_path at the open (0 none, 1 placing, 2 in step, 3 given up)
    bool neutral = false;           // WO-200 II.4 on (the planner is never asked for this body)
    float dist = -1;                // the copy's distance from the local player at the open
    float objDist = -1;             // the copy's distance from the activity's object at the open (-1: none / not here)
};
enum class Note : uint8_t { InstallOk, Refused, Stale, Deferred, QueueFull, Backoff, Held, RefusedHere };
void place_note(PlaceEp& ep, Note n, const char* detail = nullptr);
// every owner pass: the host's full body (have, its action, fragment, posture family), the copy's full body (fragment, family, its action's
// class name, ours or not); opens, measures and closes attempts (one WO217-PLACE line each)
void place_tick(uint32_t eid, const char* name, PlaceEp& ep, bool haveHost, uint64_t hostAct, uint32_t hostFrag, uint8_t hostFam,
                uint32_t copyFrag, uint8_t copyFam, const char* copyCls, bool copyOurs, double now);
void place_close(uint32_t eid, const char* name, PlaceEp& ep, double now, const char* why);   // the owner lets the copy go

// ---- 0.2: the seated-copy writer (npc_drive.cpp's writer asks once a frame; the owner's family is the copy's) ----
// true = the writer leaves this copy alone this frame; *placeOnce = write the host's newest position once now (it is far from its seat)
bool seat_holds(uint32_t eid, const char* name, float offM, bool* placeOnce);
void seat_note_pull(uint32_t eid, const char* name, float cm, const char* writer);

} // namespace kcdmp::wo217
