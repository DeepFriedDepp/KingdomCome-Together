// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// A copy that walks is driven by the engine's per-frame movement request (body_drive.h). Ported from WO-220's probe (the parked
// branch wo220-probes), which measured it: step x1.00, slide 9 %, pulls 0.12 a body-minute with every copy driven.
//
// The request (CMovementRequest as this build's CryAction and EntityModule use it; every offset below is checked by the bytes of
// C_ActorMovementController::RequestMovement at install, and nothing is driven when one differs):
//   +0x00  flags (u64): bit n = "has", bit n+31 = "remove"; 2 desired speed, 4 move target, 12 pseudo-speed, 15 body target
//   +0x20  body target (a point to face)          +0x38 desired speed, +0x3C target speed (m/s)
//   +0x44  distance to the path's end (m)         +0x58 move target (a point to walk toward)
//   +0x108 pseudo-speed (this game: the logical speed class, 0 still / 1 walk / 2 run / 3 sprint)
//   +0x194 1.0f in a default-constructed request (the sub-object the proxy constructs at +0x178); the rest zero
// The controller is the actor's member at +0x1E8 (C_Actor vtbl[0x228], a plain getter, checked by its bytes); it is used only when
// its vftable is C_ActorMovementController's (RTTI) and its own +8 is that actor.
// The engine's own "this body is not run in full" flag: C_Actor +0xAE2 bit 6, set and cleared by C_Actor::SetMonsterLOD (found by
// its own name string, its read of that byte checked).
//
// The bench only (never set in play): KCDMP_TEST_W221_DRIVE_OFF=<lod,hostwalk,arrive,lead,nomove> leaves single conditions out (the
// probe's own behaviour is all five off), KCDMP_TEST_W221_DRIVE_ONLY=<name> drives that one copy, KCDMP_TEST_W221_DRIVE_LOG=<name>
// logs ten lines a second for it.
#include "body_drive.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>

#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "gait_logic.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "respawn_actions.h"
#include "wo203.h"
#include "wo208.h"
#include "wo208_rules.h"
#include "wo221.h"

namespace kcdmp::bodydrive {
namespace {

constexpr size_t kActorController = 0x1E8, kActorGetController = 0x228, kActorLodByte = 0xAE2;
constexpr uint8_t kActorLodBit = 0x40;
constexpr size_t kCtlActor = 0x08, kCtlState = 0x10;
constexpr size_t kReqFlags = 0x00, kReqBodyTarget = 0x20, kReqDesiredSpeed = 0x38, kReqTargetSpeed = 0x3C, kReqDistToEnd = 0x44,
                 kReqMoveTarget = 0x58, kReqPseudoSpeed = 0x108, kReqDefaultOne = 0x194, kReqSize = 0x200;
constexpr uint64_t kHasSpeed = 1ull << 2, kHasMove = 1ull << 4, kHasPseudo = 1ull << 12, kHasBody = 1ull << 15;
constexpr uint64_t kRemMove = kHasMove << 31, kRemBody = kHasBody << 31;

constexpr float kGain = 3.6f;          // the pull: wanted speed = the host NPC's + gain x how far behind its place the body is (1/s)
constexpr float kSnapM = 15.0f;        // further behind than this (or a floor apart): the writer places it once (score3: 8 m -- a body
                                       // an activity or a fight had held was put in by a jump where it could have walked in)
constexpr float kLookM = 1.5f;         // the move target is at least this far ahead of the place, on the NPC's own way
constexpr float kAirVzMps = 1.5f, kStandMps = 0.15f, kMaxMps = 7.0f;
constexpr float kStartMps = 0.45f;     // a body is taken up when its host NPC really walks (score1: an NPC shifting on the spot reads 0.15 to 0.16 m/s --
                                       // one copy was taken up and handed back 1,500 times in five minutes); a driven one is kept down to kStandMps
constexpr float kArriveM = 0.50f;      // C.2: within this of the stop the writer takes the last stretch
constexpr float kRewalkM = 0.80f;      // a standing body further than this from its standing NPC is walked there, not slid
constexpr float kLeadMaxS = 0.35f;     // the place is the newest sample carried on by its age, at most this
constexpr float kDeadM = 0.03f;
constexpr double kNoMoveS = 0.6, kNoMoveRestS = 5.0;   // asked to walk and not moving this long: the writer's for the rest time
constexpr float kNoMoveWantMps = 0.5f, kNoMoveBodyMps = 0.10f;
constexpr double kWindowS = 10.0, kSnapEveryS = 2.0;
// score2: a few bodies were driven for minutes 2 to 4 m from their place (the request steers straight at the place: a wall, a table
// or a door between the body and its NPC's way is not walked round), and fell 8 m behind again and again -- every one a flip. A
// driven body further than kLoseM from its place for kLoseS is given back to the writer (its blend brings it in) and is taken up
// again only once it is back on its NPC's way.
constexpr float kLoseM = 1.5f, kRetakeM = 0.6f;
constexpr double kLoseS = 1.0;
constexpr float kCloseInM = 0.35f;     // closing in at least this much in kLoseS is "getting there"
constexpr float kSureWalkMps = 0.9f;   // at this speed the NPC walks whatever its animation line says yet (the line is a lane behind)
constexpr float kCatchUpM = 1.0f;      // further behind than this the copy may take the next speed class to catch up

void* const* g_vftCtl = nullptr;
void* const* g_vftActor = nullptr;
void* g_fnRequest = nullptr;
std::atomic<bool> g_armed{false};
bool g_lodKnown = false;
std::string g_why = "not installed";

// the bench's knobs (read once)
struct Knobs { bool lod = true, hostwalk = true, arrive = true, lead = true, nomove = true, fam = true; char only[96]{}; char log[96]{}; bool read = false; };
Knobs g_k;
void read_knobs() {
    if (g_k.read) return;
    g_k.read = true;
    char b[256]{};
    DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W221_DRIVE_OFF", b, sizeof b);
    if (n > 0 && n < sizeof b) {
        g_k.lod = !std::strstr(b, "lod"); g_k.hostwalk = !std::strstr(b, "hostwalk"); g_k.arrive = !std::strstr(b, "arrive");
        g_k.lead = !std::strstr(b, "lead"); g_k.nomove = !std::strstr(b, "nomove"); g_k.fam = !std::strstr(b, "fam");
    }
    n = GetEnvironmentVariableA("KCDMP_TEST_W221_DRIVE_ONLY", g_k.only, sizeof g_k.only);
    if (n == 0 || n >= sizeof g_k.only) g_k.only[0] = 0;
    n = GetEnvironmentVariableA("KCDMP_TEST_W221_DRIVE_LOG", g_k.log, sizeof g_k.log);
    if (n == 0 || n >= sizeof g_k.log) g_k.log[0] = 0;
    if (!g_k.lod || !g_k.hostwalk || !g_k.arrive || !g_k.lead || !g_k.nomove || g_k.only[0] || g_k.log[0])
        logf("WO221-DRIVE the bench's knobs: lod=%d hostwalk=%d arrive=%d lead=%d nomove=%d only=%s log=%s", g_k.lod, g_k.hostwalk, g_k.arrive, g_k.lead,
             g_k.nomove, g_k.only[0] ? g_k.only : "-", g_k.log[0] ? g_k.log : "-");
}

struct Drv {
    uint32_t eid = 0;
    std::string name;
    bool active = false;        // a request of ours stands on its controller
    bool walking = false;       // the last request had a move target
    double lastAt = 0, snapAt = -1e9, logAt = -1e9;
    float prev[3]{};
    bool havePrev = false;
    float spd = 0;              // the body's own planar speed (smoothed)
    float hostEma = 0, wantPrev = 0;   // the host NPC's speed smoothed (0.4 s); the last wanted speed (its change is bounded)
    double noMoveSince = 0, restUntil = -1e9;   // asked to walk and not moving since; the writer's until
    double snapRestS = 30.0;                    // a snap that did not put the body right: the writer's for this long (doubled to 240 s)
    double restS = kNoMoveRestS;                // the next rest: doubled each time it does not move (to 40 s), back when it walks
    double farSince = 0;                        // driven and further than kLoseM from its place since
    float farErr = 0;                           // how far it was then: a body that closes in is left to walk
    bool lost = false;                          // given back for that: taken up again within kRetakeM only
    // the window
    double winStart = 0;
    uint32_t frames = 0, moved = 0, within50 = 0, over200 = 0, snaps = 0, walkFrames = 0, walkWithin50 = 0;
    double errSum = 0, errMax = 0, jumpMaxCm = 0, jumpSumCm = 0, spdSum = 0, wantSum = 0, hostSum = 0;
    uint32_t totalSnaps = 0;
};
std::unordered_map<uint32_t, Drv> g_drv;
std::atomic<uint32_t> c_frames{0}, c_snaps{0}, c_arrives{0}, c_ends{0}, c_lodFrames{0}, c_noMove{0}, c_restFrames{0}, c_standFrames{0}, c_famEnds{0}, c_famFrames{0}, c_lost{0}, c_lostFrames{0}, c_snapRests{0};
std::atomic<uint32_t> g_drivenNow{0};
float g_cam[3]{};          // this machine's player, read once a frame (tick)
bool g_camOk = false;
double g_lastFrameAt = -1e9;   // when a body was last asked for (frame()): the host never asks
float g_lodNearestM = 1e9f, g_fullFarthestM = 0.0f;   // run-time measure: the nearest body the engine had in its far level, the farthest it ran in full

template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "body_drive::rd");
    if (!fault::plausible_address(reinterpret_cast<uintptr_t>(base) + off)) return false;
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}
// RequestMovement: a field-by-field merge into the controller's own state (read whole in WO-220: no lock, no allocation without
// the actor-target flag, which is never set here) -- a plain call, made where the engine's own proxy makes it (the main thread).
bool call_request(void* fn, void* ctl, void* req, bool* ok) {
    KCDMP_FAULT_CALL(site, "body_drive::RequestMovement");
    return fault::guarded(site, [&] { *ok = reinterpret_cast<bool (__fastcall*)(void*, void*)>(fn)(ctl, req); });
}

bool has(HMODULE m, const void* fn, std::initializer_list<uint8_t> pat) { return anchor::function_has_bytes(m, fn, pat.begin(), pat.size()); }

void* controller_of(void* actor) {
    void* ctl = nullptr; void* vp = nullptr; void* back = nullptr;
    if (!actor || !rd(actor, kActorController, &ctl) || !ctl) return nullptr;
    if (!rd(ctl, 0, &vp) || vp != static_cast<const void*>(g_vftCtl)) return nullptr;
    if (!rd(ctl, kCtlActor, &back) || back != actor) return nullptr;
    return ctl;
}

// One request to the body's controller. walking: a move target `to` at `mps` (class `cls`), `endM` to the path's end; standing: no
// move target, no speed. Returns false when the body has no controller of the checked kind.
bool send(Drv& d, void* actor, bool walking, const float to[3], float mps, float cls, float endM) {
    void* ctl = controller_of(actor);
    if (!ctl) return false;
    alignas(16) uint8_t req[kReqSize];
    std::memset(req, 0, sizeof req);
    const float one = 1.0f;
    std::memcpy(req + kReqDefaultOne, &one, 4);
    uint64_t flags = kHasSpeed | kHasPseudo | kRemBody;
    if (walking) {
        flags |= kHasMove;
        std::memcpy(req + kReqMoveTarget, to, 12);
        std::memcpy(req + kReqDistToEnd, &endM, 4);
    } else {
        flags |= kRemMove;
        mps = 0.0f; cls = 0.0f;
    }
    std::memcpy(req + kReqDesiredSpeed, &mps, 4);
    std::memcpy(req + kReqTargetSpeed, &mps, 4);
    std::memcpy(req + kReqPseudoSpeed, &cls, 4);
    std::memcpy(req + kReqFlags, &flags, 8);
    bool ok = false;
    if (!call_request(g_fnRequest, ctl, req, &ok)) {
        g_armed = false; g_why = "RequestMovement faulted";
        logf("WO221-DRIVE RequestMovement FAULTED on %s: nothing is driven any more, the writer writes every copy", d.name.c_str());
        return false;
    }
    d.active = true;
    d.walking = walking;
    return ok;
}

void flush(Drv& d, double now) {
    if (!d.frames) { d.winStart = now; return; }
    const double n = d.frames;
    logf("WO221-DRIVE-WIN npc=%s frames=%u err_mean_cm=%.1f err_max_cm=%.1f within50=%.1f%% over200=%.1f%% walk_frames=%u walk_within50=%.1f%% body_mps=%.2f want_mps=%.2f "
         "host_mps=%.2f snaps=%u window_s=%.0f",
         d.name.c_str(), d.frames, d.errSum / n * 100.0, d.errMax * 100.0, 100.0 * d.within50 / n, 100.0 * d.over200 / n, d.walkFrames,
         d.walkFrames ? 100.0 * d.walkWithin50 / d.walkFrames : 0.0, d.spdSum / n, d.wantSum / n, d.hostSum / n, d.snaps, now - d.winStart);
    // the scoreboard's line, the same words as the position writer's (npc_drive.cpp pull_flush): the largest single-frame jump of the
    // body beyond what its own speed explains, its frames, the frames with such a jump
    logf("MP-NPCPULL npc=%s mean_cm=%.2f max_cm=%.2f frames=%u moved_frames=%u toward_anchor_cos=0.00 anchor_m=0.0 dz_mean_cm=0.00 lag_frames=0 flying=0/0 "
         "jitter_allow_ms=%d window_s=%d driven=1",
         d.name.c_str(), d.jumpSumCm / n, d.jumpMaxCm, d.frames, d.moved, static_cast<int>(npcdrive::playout_delay_s(d.eid) * 1000.0),
         static_cast<int>(now - d.winStart + 0.5));
    d.winStart = now;
    d.frames = d.moved = d.within50 = d.over200 = d.snaps = d.walkFrames = d.walkWithin50 = 0;
    d.errSum = d.errMax = d.jumpMaxCm = d.jumpSumCm = d.spdSum = d.wantSum = d.hostSum = 0;
}

void end_request(Drv& d, const char* why) {
    if (!d.active) return;
    void* actor = motion::actor_of(d.eid);
    if (actor && engine::entity_by_id(d.eid)) {
        const float to[3]{};
        send(d, actor, false, to, 0.0f, 0.0f, 0.0f);
    }
    d.active = false; d.walking = false; d.havePrev = false; d.wantPrev = 0.0f; d.noMoveSince = 0;
    const uint32_t k = c_ends.fetch_add(1, std::memory_order_relaxed) + 1;
    if (k <= 60 || k % 500 == 0) logf("WO221-DRIVE end npc=%s why=%s: its request ends (no move target, no speed) [n=%u]", d.name.c_str(), why, k);
}

void install() {
    HMODULE em = GetModuleHandleA("EntityModule.dll");
    if (!em) { g_why = "no EntityModule"; logf("WO221-DRIVE NOT armed: %s", g_why.c_str()); return; }
    g_vftCtl = anchor::find_vftable(em, ".?AVC_ActorMovementController@entitymodule@wh@@", 0);
    g_vftActor = anchor::find_vftable(em, ".?AVC_Actor@entitymodule@wh@@", 0);
    if (!g_vftCtl || !g_vftActor) { g_why = "the controller's or the actor's vftable not found"; logf("WO221-DRIVE NOT armed: %s", g_why.c_str()); return; }
    g_fnRequest = const_cast<void*>(g_vftCtl[1]);   // IMovementController: the destructor, then RequestMovement
    const void* getCtl = g_vftActor[kActorGetController / 8];
    // RequestMovement by what it does: each field filled here is read at its offset and stored into the controller's own state
    const bool layout =
        has(em, g_fnRequest, {0xF2, 0x0F, 0x10, 0x47, 0x58}) &&                    // movsd xmm0, [rdi+0x58]      the move target
        has(em, g_fnRequest, {0xF2, 0x41, 0x0F, 0x11, 0x46, 0x68}) &&              // movsd [r14+0x68], xmm0      -> state + 0x58
        has(em, g_fnRequest, {0xF3, 0x0F, 0x10, 0x47, 0x44}) &&                    // movss xmm0, [rdi+0x44]      the distance to the path's end
        has(em, g_fnRequest, {0xF3, 0x0F, 0x10, 0x57, 0x3C}) &&                    // movss xmm2, [rdi+0x3c]      the target speed
        has(em, g_fnRequest, {0xF3, 0x0F, 0x10, 0x4F, 0x38}) &&                    // movss xmm1, [rdi+0x38]      the desired speed
        has(em, g_fnRequest, {0xF2, 0x0F, 0x10, 0x47, 0x20}) &&                    // movsd xmm0, [rdi+0x20]      the body target
        has(em, g_fnRequest, {0x8B, 0x87, 0x08, 0x01, 0x00, 0x00}) &&              // mov eax, [rdi+0x108]        the pseudo-speed
        has(em, g_fnRequest, {0x41, 0x89, 0x86, 0x18, 0x01, 0x00, 0x00}) &&        // mov [r14+0x118], eax        -> state + 0x108
        has(em, g_fnRequest, {0x0F, 0x10, 0x87, 0x78, 0x01, 0x00, 0x00});          // movups xmm0, [rdi+0x178]    the block copied whatever the flags
    const bool getter = has(em, getCtl, {0x48, 0x8B, 0x83, 0xE8, 0x01, 0x00, 0x00});   // mov rax, [rbx+0x1e8]
    if (!layout || !getter) {
        g_why = !layout ? "RequestMovement's bytes are not this build's (the request's layout is unverified)" : "the actor's controller getter is not at +0x1E8";
        logf("WO221-DRIVE NOT armed: %s -- the writer writes every copy", g_why.c_str());
        return;
    }
    // the engine's far-level flag: SetMonsterLOD by its own name, its read of the byte
    int nStr = 0;
    const uint8_t* lodFn = anchor::function_by_string(em, "C_Actor::SetMonsterLODTo", &nStr);
    g_lodKnown = lodFn && nStr == 1 && has(em, lodFn, {0x41, 0x0F, 0xB6, 0xAE, 0xE2, 0x0A, 0x00, 0x00});   // movzx ebp, byte [r14+0xAE2]
    g_armed = true; g_why = "armed";
    logf("WO221-DRIVE armed: C_ActorMovementController vft=EntityModule+0x%llX RequestMovement=+0x%llX (layout checked by bytes), the actor's controller at "
         "+0x1E8; the engine's far-level flag (C_Actor+0xAE2 bit 6) %s",
         static_cast<unsigned long long>(reinterpret_cast<const uint8_t*>(g_vftCtl) - reinterpret_cast<const uint8_t*>(em)),
         static_cast<unsigned long long>(reinterpret_cast<const uint8_t*>(g_fnRequest) - reinterpret_cast<const uint8_t*>(em)),
         g_lodKnown ? "found by SetMonsterLOD's own bytes" : "NOT found: the not-moving rule alone hands far bodies to the writer");
}

void tick() {
    if (!g_armed.load(std::memory_order_relaxed)) return;
    const double now = npcdrive::now_s();
    if (g_drv.empty() && now - g_lastFrameAt > 1.0) { g_camOk = false; g_drivenNow.store(0, std::memory_order_relaxed); return; }   // nothing to drive (the host: always)
    g_camOk = g_lodKnown && actions::player_position(g_cam);
    // bodies no frame asked for (unbound, switched off, held): their request ends, their window closes
    uint32_t active = 0;
    for (auto it = g_drv.begin(); it != g_drv.end();) {
        Drv& d = it->second;
        if (now - d.lastAt > 0.5) {
            end_request(d, "no frame");
            flush(d, now);
            it = g_drv.erase(it);
        } else { if (d.active) ++active; ++it; }
    }
    g_drivenNow.store(active, std::memory_order_relaxed);
}

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n),
                         "armed=%d (%s) on=%d driven_now=%u frames=%u snaps=%u arrives=%u ends=%u far_level_frames=%u not_moving=%u rest_frames=%u "
                         "stand_frames=%u busy_host_frames=%u busy_host_ends=%u given_back=%u given_back_frames=%u engine_range: nearest_far_m=%.0f farthest_full_m=%.0f",
                         g_armed.load() ? 1 : 0, g_why.c_str(), wo221::on(wo221::kBitDrive) ? 1 : 0, g_drivenNow.load(), c_frames.load(), c_snaps.load(),
                         c_arrives.load(), c_ends.load(), c_lodFrames.load(), c_noMove.load(), c_restFrames.load(), c_standFrames.load(), c_famFrames.load(), c_famEnds.load(), c_lost.load(), c_lostFrames.load(),
                         g_lodNearestM < 1e8f ? g_lodNearestM : -1.0f, g_fullFarthestM);
}

const bool kReg = wo203::reg_install("body_drive", &install) && wo203::reg_tick("body_drive", &tick) && wo221::add_status("drive", &status);

} // namespace

bool armed() { return g_armed.load(std::memory_order_relaxed); }

Out frame(const In& in) {
    if (!g_armed.load(std::memory_order_relaxed) || !wo221::on(wo221::kBitDrive) || !in.name || !in.ent) return Out::Writer;
    read_knobs();
    if (g_k.only[0] && _stricmp(g_k.only, in.name) != 0) return Out::Writer;
    // what a request cannot do stays the writer's: figures, beasts, riders and the carried, bodies in the air, fighting bodies,
    // bodies with no physics here
    if (in.figure || in.beast || in.parented || !in.human || !in.physics || in.fight || std::fabs(in.vz) > kAirVzMps) return Out::Writer;
    void* actor = motion::actor_of(in.eid);
    float body[3];
    if (!actor || !engine::entity_world_pos(in.ent, body)) return Out::Writer;
    const double now = in.now;
    g_lastFrameAt = now;
    // the engine's own word on whether it runs this body in full (and, for the findings, at what distance from this player it stops)
    if (g_lodKnown) {
        uint8_t lb = 0;
        const bool farLevel = rd(actor, kActorLodByte, &lb) && (lb & kActorLodBit) != 0;
        if (g_camOk) {
            const float dm = std::sqrt((body[0] - g_cam[0]) * (body[0] - g_cam[0]) + (body[1] - g_cam[1]) * (body[1] - g_cam[1]));
            if (farLevel) { if (dm < g_lodNearestM) g_lodNearestM = dm; } else if (dm > g_fullFarthestM) g_fullFarthestM = dm;
        }
        if (farLevel && g_k.lod) { c_lodFrames.fetch_add(1, std::memory_order_relaxed); return Out::Writer; }
    }
    const float hostMps = in.mps > 0.0f ? in.mps : 0.0f;
    auto found = g_drv.find(in.eid);
    const bool wasActive = found != g_drv.end() && found->second.active;
    // C.1: the host NPC walks or runs by its own animation line, not by its speed alone -- an NPC at work shifts about at a walker's
    // slowest speed (score1: the smithy fell from 86 to 57 % in place with every such body driven); the idle that follows a walk is
    // the arrival's, anything else ends the drive
    if (g_k.fam) {
        const uint8_t hf = wo208::host_family(in.eid);
        const bool busy = hf == static_cast<uint8_t>(wo208rules::Fam::Other) || hf == static_cast<uint8_t>(wo208rules::Fam::Hold) ||
                          hf == static_cast<uint8_t>(wo208rules::Fam::CombatMove);
        const bool loco = hf == 0xFF || hf == static_cast<uint8_t>(wo208rules::Fam::Loco) || (!busy && hostMps >= kSureWalkMps);
        const bool rest = hf == static_cast<uint8_t>(wo208rules::Fam::Idle) || hf == static_cast<uint8_t>(wo208rules::Fam::Empty);
        if (!loco && !(wasActive && rest)) {
            if (wasActive) {
                end_request(found->second, "its host NPC took up something else");
                c_famEnds.fetch_add(1, std::memory_order_relaxed);
                return Out::Arrive;
            }
            c_famFrames.fetch_add(1, std::memory_order_relaxed);
            return Out::Writer;
        }
    }
    // the place: where the host's NPC is now -- its newest sample carried on by its age (C.1's lead), or the play-out pose
    float R[3] = { in.render[0], in.render[1], in.render[2] };
    if (g_k.lead) {
        const float age = (std::min)(kLeadMaxS, (std::max)(0.0f, in.newestAgeS));
        R[0] = in.newest[0] + in.vx * age; R[1] = in.newest[1] + in.vy * age; R[2] = in.newest[2];
    }
    const float ex = R[0] - body[0], ey = R[1] - body[1];
    const float err = std::sqrt(ex * ex + ey * ey);
    if (hostMps < (wasActive ? kStandMps : kStartMps) && g_k.hostwalk) {
        // the host's NPC stands. A driven body walks the rest of the way to within half a metre; a standing one is the writer's
        // unless it stands far enough off that placing it would be a slide
        // (the writer shows the stream one play-out delay late: the body is handed over only once that pose has reached the stop too,
        // or the blend would first pull the body back toward where the stream was a moment ago)
        const float rdx = in.render[0] - in.newest[0], rdy = in.render[1] - in.newest[1];
        const bool caughtUp = rdx * rdx + rdy * rdy < 0.05f * 0.05f;
        const bool walkOn = wasActive ? (g_k.arrive ? (err > kArriveM || (!caughtUp && err > 0.15f)) : err > 0.15f) : (err > kRewalkM && err <= kSnapM);
        if (!walkOn) {
            if (wasActive) {
                end_request(found->second, "arrived");
                c_arrives.fetch_add(1, std::memory_order_relaxed);
                return Out::Arrive;
            }
            c_standFrames.fetch_add(1, std::memory_order_relaxed);
            return Out::Writer;
        }
    }
    Drv& d = g_drv[in.eid];
    if (d.eid != in.eid) { d = Drv{}; d.eid = in.eid; d.name = in.name; d.winStart = now; }
    // a body given back for being off its NPC's way is the writer's until it is back on it
    if (d.lost) {
        if (err > kRetakeM) { d.lastAt = now; c_lostFrames.fetch_add(1, std::memory_order_relaxed); return Out::Writer; }
        d.lost = false;
    }
    if (d.active && err > kLoseM && err <= kSnapM) {
        if (d.farSince == 0) { d.farSince = now; d.farErr = err; }
        else if (now - d.farSince >= kLoseS && d.farErr - err >= kCloseInM) { d.farSince = now; d.farErr = err; }   // it is getting there: left to walk in
        else if (now - d.farSince >= kLoseS) {
            d.farSince = 0; d.lost = true;
            end_request(d, "off its NPC's way");
            const uint32_t k = c_lost.fetch_add(1, std::memory_order_relaxed) + 1;
            if (k <= 100 || k % 100 == 0)
                logf("WO221-DRIVE npc=%s is %.2f m from its place and came no nearer in %.1f s of walking at it: the writer brings it in, the driver takes it up again on its NPC's way [n=%u]",
                     d.name.c_str(), err, kLoseS, k);
            return Out::Arrive;
        }
    } else d.farSince = 0;
    if (now < d.restUntil) { d.lastAt = now; c_restFrames.fetch_add(1, std::memory_order_relaxed); return Out::Writer; }
    const double dt = d.lastAt > 0 ? now - d.lastAt : 0.0;
    {   // the stream's speed is noisy sample to sample (1.26 .. 1.47 m/s on a steady walker): the feed-forward is its 0.4 s mean
        const float a = dt > 0.0 && dt < 0.4 ? static_cast<float>(dt / 0.4) : 1.0f;
        d.hostEma += (hostMps - d.hostEma) * a;
    }
    d.lastAt = now;
    // the body's own motion since the last frame (the engine's: nothing of ours wrote it)
    float jump = 0.0f;
    if (d.havePrev && dt > 0.0005 && dt < 0.25) {
        const float jx = body[0] - d.prev[0], jy = body[1] - d.prev[1], jz = body[2] - d.prev[2];
        const float step = std::sqrt(jx * jx + jy * jy);
        d.spd += (step / static_cast<float>(dt) - d.spd) * 0.2f;
        const float expected = (std::max)(d.spd, hostMps) * static_cast<float>(dt) * 1.5f + 0.02f;
        jump = (std::max)(0.0f, std::sqrt(jx * jx + jy * jy + jz * jz) - expected);
    }
    std::memcpy(d.prev, body, sizeof body);
    d.havePrev = true;
    // left behind (or a floor apart): the one snap
    if ((err > kSnapM || std::fabs(R[2] - body[2]) > 1.5f) && now - d.snapAt >= kSnapEveryS) {
        // score4-d9: "4.30 m from its place (dz 5.16)" every 3 s for a body whose NPC stands a floor up where the copy does not stand:
        // a snap that follows a snap within 8 s did not put the body right -- the writer has it (it writes the place every frame)
        // for a rest, and nothing is snapped meanwhile
        if (now - d.snapAt < 8.0) {
            d.snapAt = now;
            d.restUntil = now + d.snapRestS;
            const uint32_t kr = c_snapRests.fetch_add(1, std::memory_order_relaxed) + 1;
            if (kr <= 100 || kr % 100 == 0)
                logf("WO221-DRIVE npc=%s snapped and still %.2f m from its place (dz %.2f): the writer has it for %.0f s [n=%u]", d.name.c_str(), err,
                     R[2] - body[2], d.snapRestS, kr);
            d.snapRestS = (std::min)(240.0, d.snapRestS * 2.0);
            d.lastAt = now;
            return Out::Writer;
        }
        if (now - d.snapAt > 300.0) d.snapRestS = 30.0;
        d.snapAt = now; ++d.snaps; ++d.totalSnaps;
        d.havePrev = false;
        const uint32_t k = c_snaps.fetch_add(1, std::memory_order_relaxed) + 1;
        if (k <= 100 || k % 100 == 0)
            logf("WO221-DRIVE snap npc=%s %.2f m from its place (dz %.2f): the writer puts it there once [n=%u]", d.name.c_str(), err, R[2] - body[2], k);
        return Out::Snap;
    }
    float to[3] = { body[0], body[1], body[2] }, want = 0.0f, endM = 10.0f;
    if (hostMps >= kStandMps) {
        // the host's NPC walks. The copy steers at a point ahead of the place on the NPC's own way (so a body a step ahead or
        // beside the way closes in without ever turning back), and its speed is the NPC's plus the pull along that way: `gain` x how
        // far behind the place the body is -- never under 0.6 of the NPC's speed (a walker is not stopped to wait), never over
        // 1.8 of it + 0.5 m/s.
        const float hv = std::sqrt(in.vx * in.vx + in.vy * in.vy);
        const float yaw = in.render[3];
        const float dx = hv > 0.01f ? in.vx / hv : -std::sin(yaw), dy = hv > 0.01f ? in.vy / hv : std::cos(yaw);
        const float along = ex * dx + ey * dy;
        const float look = (std::max)(kLookM, hostMps * 1.0f);
        to[0] = R[0] + dx * look; to[1] = R[1] + dy * look;
        const float ff = d.hostEma >= kStandMps ? d.hostEma : hostMps;
        const float dead = g_k.lead ? kDeadM : 0.10f;
        const float pull = std::fabs(along) > dead ? kGain * (along > 0 ? along - dead : along + dead) : 0.0f;
        want = ff + pull;
        want = (std::min)((std::max)(want, 0.6f * ff), (std::min)(kMaxMps, 1.8f * ff + 0.5f));
        // the wanted speed changes at most 3 m/s in a second (a start is the engine's own acceleration)
        if (d.wantPrev > 0.0f && dt > 0.0 && dt < 0.25) {
            const float lim = 3.0f * static_cast<float>(dt);
            want = (std::min)((std::max)(want, d.wantPrev - lim), d.wantPrev + lim);
        }
    } else {
        // it stands: the copy walks the rest of the way (the engine's own stop at the end)
        to[0] = R[0]; to[1] = R[1];
        want = (std::min)(1.6f, (std::max)(0.7f, kGain * err));
        endM = err;
    }
    // the speed class is the host NPC's own (the engine keeps the speed inside the class): a copy a step behind walks a little
    // faster, it does not break into a run; left over 2.5 m behind it may take the next class to catch up
    float cls = (std::max)(1.0f, gait::speed_class(hostMps >= kStandMps ? hostMps : want));
    if (hostMps >= kStandMps && err > kCatchUpM && cls < 3.0f) cls += 1.0f;
    // asked to walk and not moving: something is in the way, or the engine does not run this body after all -- the writer's for a while
    if (g_k.nomove && d.active && dt > 0.0 && dt < 0.25) {
        if (d.wantPrev >= kNoMoveWantMps && d.spd < kNoMoveBodyMps) {
            if (d.noMoveSince == 0) d.noMoveSince = now;
            else if (now - d.noMoveSince >= kNoMoveS) {
                d.noMoveSince = 0;
                d.restUntil = now + d.restS;
                const double rested = d.restS;
                d.restS = (std::min)(40.0, d.restS * 2.0);
                end_request(d, "asked to walk and does not move");
                const uint32_t k = c_noMove.fetch_add(1, std::memory_order_relaxed) + 1;
                if (k <= 100 || k % 100 == 0)
                    logf("WO221-DRIVE npc=%s was asked to walk for %.1f s and does not move (%.2f m from its place): the writer's for %.0f s [n=%u]",
                         d.name.c_str(), kNoMoveS, err, rested, k);
                return Out::Writer;
            }
        } else { d.noMoveSince = 0; if (d.spd >= 0.5f) d.restS = kNoMoveRestS; }
    }
    d.wantPrev = want;
    if (!send(d, actor, true, to, want, cls, endM)) return Out::Writer;
    c_frames.fetch_add(1, std::memory_order_relaxed);
    // the window
    ++d.frames;
    d.errSum += err; if (err > d.errMax) d.errMax = err;
    if (err <= 0.5f) ++d.within50;
    if (err > 2.0f) ++d.over200;
    if (hostMps >= kStandMps) { ++d.walkFrames; if (err <= 0.5f) ++d.walkWithin50; }
    d.spdSum += d.spd; d.wantSum += want; d.hostSum += hostMps;
    d.jumpSumCm += jump * 100.0; if (jump * 100.0 > d.jumpMaxCm) d.jumpMaxCm = jump * 100.0;
    if (jump > 0.001f) ++d.moved;
    if (g_k.log[0] && !_stricmp(g_k.log, d.name.c_str()) && now - d.logAt >= 0.1) {
        d.logAt = now;
        logf("WO221-DRIVE-LOG npc=%s body=%.2f,%.2f,%.2f place=%.2f,%.2f err_cm=%.0f host_mps=%.2f want_mps=%.2f body_mps=%.2f cls=%.0f", d.name.c_str(), body[0],
             body[1], body[2], R[0], R[1], err * 100.0f, hostMps, want, d.spd, cls);
    }
    if (now - d.winStart >= kWindowS) flush(d, now);
    return Out::Driven;
}

void release(uint32_t eid, const char* why) {
    auto it = g_drv.find(eid);
    if (it == g_drv.end()) return;
    end_request(it->second, why);
}

bool driven(uint32_t eid) {
    auto it = g_drv.find(eid);
    return it != g_drv.end() && it->second.active;
}

} // namespace kcdmp::bodydrive
