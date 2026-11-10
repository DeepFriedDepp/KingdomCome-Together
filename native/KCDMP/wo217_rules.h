// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-217 (docs/WO-217-findings.md): the world around the bodies -- the pure rules of the native half (engine-free:
// native/tests/wo217_tests.cpp checks them).
//   0.2 seat_writer: one writer for a seated copy -- while the host's line says seated, the seat holds the body and the stream never places it
//   A   placement: an activity's placement on a copy (A.0: every attempt's outcome and reason, one line each)
//   B   ghost_far, kd_follow, long_names, layer_watch (each its own switch)
//   C-F loot_live, item_flags, encounter_world, carts
#include <cmath>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo217rules {

// ---- the agent's switch word (op 170): every bit on until the agent says otherwise; the bench's twin: KCDMP_TEST_W217_OFF=<names|all>
constexpr uint32_t kBitSeatWriter = 0x001;    // 0.2 the seat holds a seated copy; the stream never places it while the host's line says seated
constexpr uint32_t kBitPlacement = 0x002;     // A.1 the activity's placement fixes (A.0's measure runs either way)
constexpr uint32_t kBitGhostFar = 0x004;      // B.1 the ghost fight counts the mirror's opponents of this player at any distance
constexpr uint32_t kBitKdFollow = 0x008;      // B.2 a copy stays down while its host NPC is down; its get-up follows the host's
constexpr uint32_t kBitLongNames = 0x010;     // B.3 names over 63 characters are copies too
constexpr uint32_t kBitLayerWatch = 0x020;    // B.4 bodies a layer switches on are suspended and hidden at the layer's activation
constexpr uint32_t kBitLootLive = 0x040;      // C  a body's loot is live for both (one inventory, requests and grants, a ledger)
constexpr uint32_t kBitItemFlags = 0x080;     // D  an item's owner and stolen state on the wire
constexpr uint32_t kBitEncounter = 0x100;     // E  what an encounter leaves (corpses, props, the searcher's object)
constexpr uint32_t kBitCarts = 0x200;         // F  carts moved by the host's transform, never the ground writer
constexpr uint32_t kBitDialogTwins = 0x400;   // 0.1 the engine's dialogue twins (DialogTwin_*) are never hidden or frozen by WO-216's B
constexpr uint32_t kBitsDefault = 0x7FF;

struct Name { const char* n; uint32_t b; };
// 0.1: the engine's own conversation stand-ins (C_DialogueTwinController::MakeTwin spawns "DialogTwin_<soul>" for every participant,
// the local player's too) -- this engine's, by construction never a host NPC's copy
inline bool engine_dialog_twin(const char* name) { return name && std::strncmp(name, "DialogTwin_", 11) == 0; }

// ---- F: a cart played back from the host's samples ----
struct CartSample { double t = 0; float p[3]{}; float a[3]{}; };   // t = the host's clock (s); p = position; a = angles (rad, x y z)
constexpr double kCartDelayS = 0.35;   // the playback runs this far behind the host's newest (four samples a second, jitter)
constexpr double kCartExtrapS = 0.5;   // past the newest: its velocity, this long at most, then held
constexpr double kCartStaleS = 3.0;    // no sample this long: the cart is its own engine's again
inline float wrap_pi(float d) { const float k2Pi = 6.28318530718f; while (d > 3.14159265359f) d -= k2Pi; while (d < -3.14159265359f) d += k2Pi; return d; }
// samples sorted by t (ascending), n >= 1: the pose at time t
inline void cart_pose(const CartSample* s, int n, double t, float outP[3], float outA[3]) {
    if (n <= 0) return;
    if (n == 1 || t <= s[0].t) { for (int k = 0; k < 3; ++k) { outP[k] = s[n == 1 ? 0 : 0].p[k]; outA[k] = s[0].a[k]; } if (n == 1) return; if (t <= s[0].t) return; }
    for (int i = 0; i + 1 < n; ++i) {
        if (t <= s[i + 1].t) {
            const double span = s[i + 1].t - s[i].t;
            const float f = span > 1e-6 ? static_cast<float>((t - s[i].t) / span) : 1.0f;
            for (int k = 0; k < 3; ++k) {
                outP[k] = s[i].p[k] + (s[i + 1].p[k] - s[i].p[k]) * f;
                outA[k] = s[i].a[k] + wrap_pi(s[i + 1].a[k] - s[i].a[k]) * f;
            }
            return;
        }
    }
    const CartSample& a = s[n - 2]; const CartSample& b = s[n - 1];
    const double span = b.t - a.t;
    double over = t - b.t;
    if (over > kCartExtrapS) over = kCartExtrapS;
    const float f = span > 1e-6 ? static_cast<float>(over / span) : 0.0f;
    for (int k = 0; k < 3; ++k) { outP[k] = b.p[k] + (b.p[k] - a.p[k]) * f; outA[k] = b.a[k] + wrap_pi(b.a[k] - a.a[k]) * f; }
}
// CryEngine's Quat::CreateRotationXYZ(Ang3) in its memory order (x, y, z, w)
inline void quat_xyz(const float a[3], float q[4]) {
    const float sx = std::sin(a[0] * 0.5f), cx = std::cos(a[0] * 0.5f), sy = std::sin(a[1] * 0.5f), cy = std::cos(a[1] * 0.5f);
    const float sz = std::sin(a[2] * 0.5f), cz = std::cos(a[2] * 0.5f);
    q[3] = cx * cy * cz + sx * sy * sz;
    q[0] = cz * cy * sx - sz * sy * cx;
    q[1] = cz * sy * cx + sz * cy * sx;
    q[2] = sz * cy * cx - cz * sy * sx;
}
// op 172's body after the op byte: [host_ms:4][n:1] then n x ([len:1][name:len][6 floats: x y z ax ay az]); every field checked
struct CartRow { char name[128]{}; float p[3]{}; float a[3]{}; };
inline int parse_carts(const uint8_t* b, size_t len, uint32_t* hostMs, CartRow* out, int cap) {
    if (len < 5) return -1;
    std::memcpy(hostMs, b, 4);
    const int n = b[4];
    if (n > cap) return -1;
    size_t o = 5;
    for (int i = 0; i < n; ++i) {
        if (o >= len) return -1;
        const size_t l = b[o++];
        if (l == 0 || l > 127 || o + l + 24 > len) return -1;
        std::memcpy(out[i].name, b + o, l); out[i].name[l] = 0; o += l;
        for (size_t c = 0; c < l; ++c) if (out[i].name[c] < 0x21 || out[i].name[c] > 0x7E) return -1;
        float f[6]; std::memcpy(f, b + o, 24); o += 24;
        for (int k = 0; k < 6; ++k) if (!(f[k] == f[k]) || f[k] > 1e6f || f[k] < -1e6f) return -1;
        for (int k = 0; k < 3; ++k) { out[i].p[k] = f[k]; out[i].a[k] = f[3 + k]; }
    }
    return o == len ? n : -1;
}

inline const Name* switch_names(int* n) {
    static const Name k[] = {{"seat_writer", kBitSeatWriter}, {"placement", kBitPlacement}, {"ghost_far", kBitGhostFar}, {"kd_follow", kBitKdFollow},
                             {"long_names", kBitLongNames}, {"layer_watch", kBitLayerWatch}, {"loot_live", kBitLootLive}, {"item_flags", kBitItemFlags},
                             {"encounter_world", kBitEncounter}, {"carts", kBitCarts}, {"dialog_twins", kBitDialogTwins}};
    *n = static_cast<int>(sizeof k / sizeof k[0]);
    return k;
}
inline uint32_t twin_off_bits(const char* list) {
    uint32_t b = 0;
    if (!list) return 0;
    int nk = 0;
    const Name* k = switch_names(&nk);
    const char* p = list;
    while (*p) {
        while (*p == ',' || *p == ' ') ++p;
        const char* s = p;
        while (*p && *p != ',' && *p != ' ') ++p;
        const size_t n = static_cast<size_t>(p - s);
        if (n == 3 && std::strncmp(s, "all", 3) == 0) b |= kBitsDefault;
        for (int i = 0; i < nk; ++i)
            if (n == std::strlen(k[i].n) && std::strncmp(s, k[i].n, n) == 0) b |= k[i].b;
    }
    return b;
}

// ---- A.0: one activity attempt on a copy, from its host NPC's move into a posture to the next host action ---------------------------
// An attempt opens when the host NPC's full body changes to an action of a posture family (sitting, lying, kneeling, working) and closes
// when the host NPC's full body changes again (or after kMaxS). What happened on the copy meanwhile is counted, and the attempt's outcome
// is the first that fits:
//   placed          the copy showed the activity (its full body in the host's fragment, or the same posture family) and kept it
//   placed-then-lost  shown, then replaced for over kLostS while the host's held (what replaced it is named)
//   refused         the owner's install of the host's fragment was refused by the engine here (the engine's reason named)
//   deferred        the owner left the body to the NPC-state placement (WO-215 C's path 24) and nothing placed it
//   stale           installed, never started: the copy's queue held something else at its head (named) until the owner removed it
//   paced           never installed: the owner waited (a full queue, a breach backoff)
//   held            never installed: something else owned the full body (a work one-shot, the copy's own attack, a row, a knockdown)
//   superseded      the host's action changed before anything above happened (under kSupersedeS)
//   not-tried       none of the above (no state reached the owner, or it was not owned)
enum class Outcome : uint8_t { Placed = 0, PlacedThenLost, Refused, Deferred, Stale, Paced, Held, Superseded, NotTried, kCount };
inline const char* outcome_name(Outcome o) {
    switch (o) {
        case Outcome::Placed: return "placed"; case Outcome::PlacedThenLost: return "placed-then-lost"; case Outcome::Refused: return "refused";
        case Outcome::Deferred: return "deferred"; case Outcome::Stale: return "stale"; case Outcome::Paced: return "paced"; case Outcome::Held: return "held";
        case Outcome::Superseded: return "superseded"; default: return "not-tried";
    }
}
constexpr double kMaxS = 30.0;         // an attempt is closed (and judged) after this long
constexpr double kLostS = 1.0;         // shown, then not shown this long while the host still holds the action: lost
constexpr double kSupersedeS = 1.0;    // the host changed again this soon: nothing could have been shown
struct Counts {
    uint16_t installs = 0, refused = 0, stale = 0, deferred = 0, queueFull = 0, backoff = 0, held = 0, refusedHere = 0;
};
inline Outcome judge(double heldS, double shownAfterS, double lostForS, const Counts& c) {
    if (shownAfterS >= 0) return lostForS >= kLostS ? Outcome::PlacedThenLost : Outcome::Placed;
    if (c.refused || c.refusedHere) return Outcome::Refused;
    if (c.deferred) return Outcome::Deferred;
    if (c.stale) return Outcome::Stale;
    if (c.queueFull || c.backoff) return Outcome::Paced;
    if (c.held) return Outcome::Held;
    if (heldS < kSupersedeS) return Outcome::Superseded;
    return Outcome::NotTried;
}
// the copy shows the host's activity: the same fragment, or a full body of the same posture family (the probe's own test is looser:
// any non-idle, non-walk fragment)
inline bool shows(uint32_t copyFrag, uint32_t hostFrag, uint8_t copyFam, uint8_t hostFam, bool famIsPosture) {
    return copyFrag == hostFrag || (famIsPosture && copyFam == hostFam);
}

// ---- A.1 (placement): the owner no longer leaves a copy to an NPC-state placement that is never made ------------------------------------
// WO-200 II.4 (copy_neutral_always, on in every joined session) keeps the joiner's planner away from host copies -- their NPC-state is
// kept equal to what the body is, so nothing is ever placed there. WO-215 C's watchdog still left a posture copy to "the NPC-state
// placement" whenever WO-141 held a desired activity for it (path 24): with the neutral rule on, that placement never comes. With
// placement on, a neutral copy's activity is the owner's to install (the deferral is only for a body the planner really places).
// A.1 #2: the copy's own queued actions that may be stopped to unclog a full queue: never-started look and movement actions only
inline bool unclog_class(const char* cls) {
    return cls && (std::strcmp(cls, "CAnimActionLooking") == 0 || std::strcmp(cls, "CAnimActionAIMovement") == 0);
}
inline bool unclog(bool on, bool queueFull, bool ours, uint32_t status, const char* cls) {
    return on && queueFull && !ours && status < 2 && unclog_class(cls);   // status: 0/1 pending, 2 installed, 3 exiting
}
constexpr double kUnclogEveryS = 1.0;

inline bool defer_to_npc_state(bool on, bool neutral, int activityPath) {
    if (activityPath != 1 && activityPath != 2) return false;
    return !(on && neutral);
}

// ---- 0.2 seat_writer: one writer for a seated copy ------------------------------------------------------------------------------------
// The writer's WO-213 E.3 rule left a posture body alone only while it stayed within 30 cm of its host NPC's newest sample; a seat on the
// joiner 0.3-1.2 m off the host's took the body back to the stream for the rest of the sitting, and the seat's own hold and the stream
// then pulled it between them every frame (WO-216's scene 1: 95 pulls on two sitters). With seat_writer on, while the host's line says
// seated (the host NPC's own posture is sitting or lying) and the copy is in that posture, the stream never places the body -- unless it
// is far off its host NPC (kSeatFarM: a body that is not at the host's seat at all, the bench's 24 m glide): then one write puts it there
// (a teleport, not a frame-by-frame tug) and the seat holds it again.
// The shipped build's worst sitters (p0a, the tavern by night) were two writers on one body: the copy's OWN NPC-state seat (its stance
// from the save, at a seat of the joiner's) kept the body there while WO-209's drift rule handed it to the stream, which wrote it toward
// its host NPC elsewhere -- single-frame pulls up to 117 m, 45 windows on one body in 10 minutes. So a copy in an object stance of its own
// whose host NPC is not seated by it is released from that seat the game's own way (WO-160's release, paced), held still meanwhile.
constexpr float kSeatFarM = 2.5f;
enum class Seat : uint8_t { NotSeated = 0, Hold = 1, PlaceOnce = 2, Release = 3 };
// hostSeated: the host's line says its NPC sits or lies; copyObjSeated: the copy's own NPC state holds an object stance (a seat, a bed,
// a bench); copySeated: the copy's full body plays a sitting or lying fragment; offM: the copy's distance from its host NPC's newest sample
inline Seat seat_rule(bool on, bool hostSeated, bool copyObjSeated, bool copySeated, float offM) {
    if (!on) return Seat::NotSeated;
    if (copyObjSeated) {
        if (hostSeated && offM <= kSeatFarM) return Seat::Hold;   // the seat holds it, the stream never places it
        return Seat::Release;                                      // its host NPC is not sitting there: off its own seat, the game's way
    }
    // the host's line says seated: the seat (the owner's sit install) holds the copy -- the stream never places it; one write first when
    // the copy is far from its host NPC's seat (a1-b7: 253 pulls of copies the stream dragged beside a seated host NPC)
    if (hostSeated) return offM > kSeatFarM ? Seat::PlaceOnce : Seat::Hold;
    // still sitting while its host NPC is not: released the game's way, held still until it stands (a1-b7: 105 pulls of a sitting body)
    if (copySeated) return Seat::Release;
    return Seat::NotSeated;
}

// ---- B.3 long_names: the alias of a name the stream cannot carry -----------------------------------------------------------------
constexpr size_t kLongNameMin = 60;   // the scan's name field holds 59 characters
inline void long_alias(const char* name, char out[16]) {
    unsigned h1 = 0, h2 = 0;
    for (const unsigned char* q = reinterpret_cast<const unsigned char*>(name); q && *q; ++q) { h1 = (h1 * 31u + *q) % 65521u; h2 = (h2 * 37u + *q) % 65519u; }
    static const char hx[] = "0123456789abcdef";
    out[0] = 'l'; out[1] = 'h';
    const unsigned v[2] = {h1, h2};
    for (int k = 0; k < 2; ++k) for (int i = 0; i < 4; ++i) out[2 + k * 4 + i] = hx[(v[k] >> (12 - 4 * i)) & 0xF];
    out[10] = 0;
}
inline bool is_alias(const char* n) {
    if (!n || n[0] != 'l' || n[1] != 'h') return false;
    for (int i = 2; i < 10; ++i) { const char c = n[i]; if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false; }
    return n[10] == 0;
}

// ---- B.2 kd_follow: a copy stays down while its host NPC is down -----------------------------------------------------------------------
// WO-210 C.3 let a copy go when its engine stood it up before the host NPC rose (1 s after its fall without a ragdoll: "up how=own" at 1.0 s
// and 3.3 s while its host lay 23 s and 7 s). With kd_follow on, a copy whose engine stood it up while its host NPC lies is laid down
// again (the engine's own RequestRagdoll), at most kRefallMax times a fall, kRefallGapS apart; the get-up stays the host's up edge.
constexpr int kRefallMax = 12;
constexpr double kRefallGapS = 0.8;
inline bool refall(bool on, bool held, bool hostDown, bool copyRagdoll, double sinceFallS, int refalls) {
    return on && held && hostDown && !copyRagdoll && sinceFallS >= kRefallGapS && refalls < kRefallMax;
}

} // namespace kcdmp::wo217rules
